using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.ReferenceServer;

internal sealed record StoredJob(string Key, string Digest, JobSubmission? Submission, Guid SceneId, JobResponse Job, DateTimeOffset? ExpiredAt = null);
internal sealed class JobStore : IDisposable
{
    private readonly ReferenceServerOptions options;
    private readonly Dictionary<Guid, StoredJob> jobs = [];
    private readonly object gate = new();
    private readonly FileStream storageLock;
    public JobStore(ReferenceServerOptions options)
    {
        this.options = options;
        PrivateStorage.CreateDirectory(options.StorageRoot);
        storageLock = new FileStream(Path.Combine(options.StorageRoot, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        foreach (var directory in Directory.EnumerateDirectories(options.StorageRoot))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                continue;
            string path = Path.Combine(directory, "job.json");
            if (!PrivateStorage.IsRegularFile(path))
                continue;
            var stored = SubmissionValidator.Deserialize<StoredJob>(File.ReadAllBytes(path));
            if (stored.Job.Id != id)
                throw new IOException("Invalid stored job identity.");
            if (!stored.Job.IsTerminal)
            {
                stored = stored with
                {
                    Job = stored.Job with
                    {
                        State = ServerJobState.Failed,
                        Revision = stored.Job.Revision + 1,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        Failure = new("serverRestarted", "The reference server restarted before completion.")
                    }
                };
                Save(stored);
            }

            jobs.Add(id, stored);
        }

        Expire();
    }

    public JobResponse Submit(string key, string digest, JobSubmission submission, SceneManifest scene, MultipartSubmission parts)
    {
        lock (gate)
        {
            Expire();
            var existing = jobs.Values.FirstOrDefault(x => x.Key == key);
            if (existing is not null)
            {
                if (existing.Digest != digest)
                    throw new ProtocolFailure(409, "idempotencyConflict", "This idempotency key was already used for different content.");
                if (existing.ExpiredAt is not null)
                    throw new ProtocolFailure(410, "jobExpired", "The job has expired.");
                return existing.Job;
            }

            if (jobs.Values.Count(x => !x.Job.IsTerminal) >= options.QueueCapacity)
                throw new ProtocolFailure(429, "queueFull", "The reference queue is full.");
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid();
            var job = new JobResponse
            {
                Id = id,
                State = ServerJobState.Queued,
                Revision = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            var stored = new StoredJob(key, digest, submission, scene.SceneId, job);
            string staging = Path.Combine(options.StorageRoot, ".pending-" + id.ToString("N"));
            PrivateStorage.CreateDirectory(staging);
            try
            {
                PrivateStorage.WriteBytes(Path.Combine(staging, "reference.png"), parts.Reference);
                PrivateStorage.WriteBytes(Path.Combine(staging, "scene.json"), parts.Scene);
                PrivateStorage.WriteBytes(Path.Combine(staging, "request.json"), parts.Request);
                PrivateStorage.WriteJsonAtomically(Path.Combine(staging, "job.json"), stored, WireJson.Options);
                Directory.Move(staging, DirectoryFor(id));
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }

            jobs.Add(id, stored);
            return job;
        }
    }

    public JobResponse Get(Guid id)
    {
        lock (gate)
        {
            Expire();
            return Find(id).Job;
        }
    }

    public JobResponse Cancel(Guid id)
    {
        lock (gate)
        {
            var stored = Find(id);
            if (stored.Job.IsTerminal)
                return stored.Job;
            stored = stored with
            {
                Job = stored.Job with
                {
                    State = ServerJobState.Cancelled,
                    CancelRequested = true,
                    Revision = stored.Job.Revision + 1,
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            };
            Save(stored);
            jobs[id] = stored;
            return stored.Job;
        }
    }

    public string OutputPath(Guid id, string outputId)
    {
        lock (gate)
        {
            Expire();
            var stored = Find(id);
            if (stored.Job.State != ServerJobState.Completed || outputId != "reference")
                throw new ProtocolFailure(404, "outputNotFound", "The output is not available.");
            var path = Path.Combine(DirectoryFor(id), "reference.png");
            if (!PrivateStorage.IsRegularFile(path))
                throw new ProtocolFailure(404, "outputNotFound", "The output is not available.");
            return path;
        }
    }

    public void ProcessNext()
    {
        lock (gate)
        {
            Expire();
            var stored = jobs.Values.Where(x => x.Job.State == ServerJobState.Queued).OrderBy(x => x.Job.CreatedAt).FirstOrDefault();
            if (stored is null)
                return;
            var job = stored.Job with
            {
                State = ServerJobState.Running,
                Revision = stored.Job.Revision + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            stored = stored with
            {
                Job = job
            };
            Save(stored);
            jobs[job.Id] = stored;
            try
            {
                string path = Path.Combine(DirectoryFor(job.Id), "reference.png");
                if (!PrivateStorage.IsRegularFile(path))
                    throw new IOException("Missing source asset.");
                var bytes = File.ReadAllBytes(path);
                var size = PngHeader.Read(bytes);
                var result = new JobResult
                {
                    SceneId = stored.SceneId,
                    SceneDigest = stored.Submission!.SceneDigest,
                    ModelId = ReferenceCapabilities.ModelId,
                    ModelVersion = ReferenceCapabilities.ModelVersion,
                    Plan = stored.Submission!.Plan,
                    IsUnchangedReference = true,
                    Outputs = [
                        new()
                        {
                            OutputId = "reference",
                            DownloadPath = $"/api/v1/jobs/{job.Id:D}/outputs/reference",
                            ByteCount = bytes.LongLength,
                            Sha256 = ContentDigest.Sha256(bytes),
                            Size = size
                        }
                    ]
                };
                stored = stored with
                {
                    Job = job with
                    {
                        State = ServerJobState.Completed,
                        Revision = job.Revision + 1,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        Result = result
                    }
                };
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                stored = stored with
                {
                    Job = job with
                    {
                        State = ServerJobState.Failed,
                        Revision = job.Revision + 1,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        Failure = new("storageFailure", "The reference asset could not be processed.")
                    }
                };
            }

            Save(stored);
            jobs[job.Id] = stored;
        }
    }

    private void Expire()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var stored in jobs.Values.ToArray())
        {
            if (stored.ExpiredAt is { } expired)
            {
                if (now - expired > options.ExpiredIdRetention)
                {
                    Directory.Delete(DirectoryFor(stored.Job.Id), true);
                    jobs.Remove(stored.Job.Id);
                }
            }
            else if (stored.Job.IsTerminal && now - stored.Job.CreatedAt > options.Retention)
            {
                var tombstone = stored with
                {
                    ExpiredAt = now,
                    Submission = null,
                    SceneId = Guid.Empty,
                    Job = stored.Job with
                    {
                        Result = null,
                        Failure = null
                    }
                };
                Save(tombstone);
                jobs[stored.Job.Id] = tombstone;
                foreach (var name in new[]
                {
                    "reference.png",
                    "scene.json",
                    "request.json"
                }

                )
                    File.Delete(Path.Combine(DirectoryFor(stored.Job.Id), name));
            }
        }
    }

    private StoredJob Find(Guid id)
    {
        if (!jobs.TryGetValue(id, out var job))
            throw new ProtocolFailure(404, "jobNotFound", "The job is unknown.");
        if (job.ExpiredAt is not null)
            throw new ProtocolFailure(410, "jobExpired", "The job has expired.");
        return job;
    }

    private string DirectoryFor(Guid id) => Path.Combine(options.StorageRoot, id.ToString("N"));
    private void Save(StoredJob value) => PrivateStorage.WriteJsonAtomically(Path.Combine(DirectoryFor(value.Job.Id), "job.json"), value, WireJson.Options);
    public void Dispose() => storageLock.Dispose();
}

internal sealed class ReferenceWorker(JobStore store, ReferenceServerOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.WorkerInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            store.ProcessNext();
    }
}
