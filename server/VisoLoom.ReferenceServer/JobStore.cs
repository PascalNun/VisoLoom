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
                    State = ManagedModels.Find(stored.Submission?.ModelId) is not null && stored.Job.State == ServerJobState.Running
                        ? ServerJobState.Running : ServerJobState.Cancelled,
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
            bool managed = ManagedModels.Find(stored.Submission?.ModelId) is not null;
            if (stored.Job.State != ServerJobState.Completed || outputId != (managed ? "result" : "reference"))
                throw new ProtocolFailure(404, "outputNotFound", "The output is not available.");
            var path = Path.Combine(DirectoryFor(id), managed ? "result.png" : "reference.png");
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
            var stored = jobs.Values.Where(x => x.Job.State == ServerJobState.Queued && ManagedModels.Find(x.Submission?.ModelId) is null).OrderBy(x => x.Job.CreatedAt).FirstOrDefault();
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

    public StoredJob? TakeManaged()
    {
        lock (gate)
        {
            Expire();
            var stored = jobs.Values.Where(x => x.Job.State == ServerJobState.Queued && ManagedModels.Find(x.Submission?.ModelId) is not null)
                .OrderBy(x => x.Job.CreatedAt).FirstOrDefault();
            if (stored is null) return null;
            stored = stored with { Job = stored.Job with { State = ServerJobState.Running, Revision = stored.Job.Revision + 1, UpdatedAt = DateTimeOffset.UtcNow } };
            Save(stored); jobs[stored.Job.Id] = stored;
            return stored;
        }
    }

    public void CompleteManaged(Guid id, LocalEngineOutcome outcome)
    {
        lock (gate)
        {
            var stored = Find(id);
            if (stored.Job.IsTerminal) return;
            JobResult? result = null;
            if (outcome.Png is { } png)
            {
                if (png.Length > 16_777_216 || PngHeader.Read(png) != stored.Submission!.Plan.Native)
                    throw new IOException("Invalid engine output.");
                PrivateStorage.WriteBytes(Path.Combine(DirectoryFor(id), "result.png"), png);
                result = new() { SceneId = stored.SceneId, SceneDigest = stored.Submission!.SceneDigest,
                    ModelId = stored.Submission.ModelId, ModelVersion = stored.Submission.ModelVersion, Plan = stored.Submission.Plan,
                    IsUnchangedReference = false, ProcessingHistory = ["localSingleReferenceEdit", "fourBitWeights", "uniformFrameSamplingRecorded"],
                    Warnings = [new("generativeDeviation", "Compare geometry, details and materials with the captured reference.")],
                    Outputs = [new() { OutputId = "result", DownloadPath = $"/api/v1/jobs/{id:D}/outputs/result",
                        ByteCount = png.LongLength, Sha256 = ContentDigest.Sha256(png), Size = stored.Submission.Plan.Native }] };
            }
            stored = stored with { Job = stored.Job with { State = outcome.Cancelled ? ServerJobState.Cancelled :
                result is not null ? ServerJobState.Completed : ServerJobState.Failed, Result = result, Failure = outcome.Failure,
                Revision = stored.Job.Revision + 1, UpdatedAt = DateTimeOffset.UtcNow } };
            Save(stored); jobs[id] = stored;
        }
    }

    internal string ManagedDirectory(Guid id) => DirectoryFor(id);

    private void Expire()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var stored in jobs.Values.ToArray())
        {
            if (stored.ExpiredAt is { } expired)
            {
                RemoveFinishedEngineCopy(stored.Job.Id);
                if (now - expired > options.ExpiredIdRetention)
                {
                    Directory.Delete(DirectoryFor(stored.Job.Id), true);
                    jobs.Remove(stored.Job.Id);
                }
            }
            else if (stored.Job.IsTerminal && now - stored.Job.CreatedAt > options.Retention)
            {
                RemoveFinishedEngineCopy(stored.Job.Id);
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
                    "request.json", "prompt.txt", "result.png"
                }

                )
                    File.Delete(Path.Combine(DirectoryFor(stored.Job.Id), name));
            }
        }
    }

    private void RemoveFinishedEngineCopy(Guid id)
    {
        string directory = Path.Combine(options.StorageRoot, "engine-jobs", id.ToString("D"));
        string state = Path.Combine(directory, "state.json");
        if (!PrivateStorage.IsRegularFile(state) || new FileInfo(state).Length > 65536 ||
            (File.GetAttributes(Path.GetDirectoryName(directory)!) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(state));
            var root = document.RootElement;
            if (root.GetProperty("jobId").GetString() == id.ToString("D") &&
                root.GetProperty("state").GetString() is "completed" or "failed" or "cancelled")
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IOException) { }
        // Never delete files out from under an orphan that still reports running.
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

internal sealed class ManagedWorker(JobStore store, ReferenceServerOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.WorkerInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var stored = store.TakeManaged();
            if (stored is null) continue;
            LocalEngineOutcome outcome;
            try
            {
                outcome = await options.LocalEngine!.EditAsync(stored.Job.Id, store.ManagedDirectory(stored.Job.Id), stored.Submission!,
                    () => store.Get(stored.Job.Id).CancelRequested, stoppingToken);
            }
            catch (Exception)
            { outcome = new(Failure: new("engineStopped", "The local engine stopped. No automatic generation retry was made.")); }
            store.CompleteManaged(stored.Job.Id, outcome);
        }
    }
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
