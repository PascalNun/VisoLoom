# Local engine prototype

VisoLoom should let a designer choose a local model in Rhino without opening another rendering app. This folder starts the engine behind that experience. It is an experimental development connection, not a model/runtime installer. The local job service and Rhino Settings/Model selector are implemented; the loaded Rhino workflow still needs native acceptance.

`engine.py` owns job identity, frozen inputs, one worker at a time, private records and cancellation. `mflux_backend.py` is the first model adapter: an isolated Apple Silicon process using MFLUX's FLUX.2 klein 4B **image-editing** pipeline. `model_store.py` owns private verification receipts; `prepare_model.py` prepares an explicit local copy once. `common.py` holds the shared OS lock. None of these files or ML dependencies are loaded into Rhino.

## Check this computer

From the repository root, with Python 3.10 or later:

```sh
python3 engine/engine.py check
python3 engine/engine.py check --model-directory /absolute/path/to/complete-model
python3 -m unittest discover -s tests/engine -v
```

Use `python` on Windows. The controller and fixture tests support both platforms. The inference adapter currently needs an Apple Silicon Mac; Windows returns `backendUnavailable`, rather than silently using a CPU or another model.

Checks install and download nothing. A positive check means that MFLUX **0.22.0** and a plausible local folder were found, **not** that hardware suitability or the model has passed acceptance. A real edit still needs to establish that. Use the Python executable belonging to the separately prepared runtime for both check and edit. The [Mac development lock](requirements-mac-arm64-py314.lock) pins all 56 selected wheels and hashes for Python 3.14/Apple Silicon on this Mac's macOS wheel set. It is not a universal runtime profile or an installer; do not treat a globally installed Python stack as a production installation. [Dependency/conditional licence review](../docs/legal/local-engine-runtime.md).

The model folder needs `transformer/`, `text_encoder/` and `vae/` safetensors plus `tokenizer/tokenizer_config.json` and the remaining tokenizer files. It must be a complete materialised MFLUX/Diffusers model folder, without symlinks, executable code or pickle checkpoints. Draw Things' `.ckpt` files do not pass this check. Hashes identify every file for job provenance; they do not authenticate an unknown model publisher. The runtime's loader verifies actual tensor compatibility.

The [MFLUX documentation](https://github.com/mflux-community/mflux/blob/main/src/mflux/models/flux2/README.md) estimates a standard 4B download at roughly 15 GB, before the runtime and working space. Quantised distributions may be smaller, but need a separately reviewed source and compatibility test. For development on 2026-10-15 the user supplied an external drive. Official model revision `e7b7dc27f91deacad38e78976d1f2b499d76a294` was provisioned there: 18 files, 15,980,141,250 bytes, with LFS SHA256 or Git blob hashes checked against that revision. The isolated internal runtime was installed from the hash lock and passed `pip check`. The engine's commands still perform no automatic download/installation. See the [implementation review](../docs/development/phase-4-review.md) for live test status.

## Prepare and reuse local weights

Use the Python executable from the separately installed, reviewed MFLUX environment. These explicit developer commands download/install nothing:

```sh
/absolute/runtime/bin/python engine/prepare_model.py \
  /absolute/original-model /absolute/prepared-model \
  --receipt /absolute/private/installation.json
```

Preparation refuses to overwrite either destination, requires at least 10 GiB free, runs quantisation on the CPU in a separate process, and retains the original model card. It stops after 30 minutes or an observed 10 GiB resident budget, removes its incomplete destination on handled failure/cancellation, and publishes a receipt only after hashing the completed model. A hard crash can leave an unregistered partial destination; it is never advertised as ready. This is development preparation of already verified weights, not a downloader or production installer.

Add `--installation /absolute/private/installation.json` to `check` and `edit`. The receipt binds every relative file name, SHA256 and filesystem identity (device/file ID, size, modification and change timestamps). Both controller and worker check the file inventory/identities; the worker checks again after generation. File additions, replacement or changes require explicit verification. On Windows the cache uses [FILE_BASIC_INFO.ChangeTime](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_basic_info), since Python's Windows ctime is creation time. Unsupported change timestamps fail closed.

```sh
/absolute/runtime/bin/python engine/engine.py verify-model \
  --model-directory /absolute/prepared-model \
  --installation /absolute/private/installation.json
```

Re-verification clears prior acceptance. A successful real edit with that receipt records first-edit acceptance for the exact backend profile. The service advertises the model only after that acceptance. The receipt is a private local integrity cache, not publisher authentication or protection against someone who can maliciously rewrite the user's receipt/filesystem metadata. Signed installation/updates and broader platform acceptance remain release work.

The first prepared model is 4,619,709,935 bytes. A 512 × 448 job now completes in 27.7 seconds, with about 2 seconds of construction/loading, instead of repeated scans/quantisation taking several minutes. Its pixels matched the prior test at the same input/prompt/settings. This does not promise deterministic model output across platforms/versions. The 1152 × 896 benchmark completes in 107.2 seconds with 9.72 GiB MLX peak allocation; memory pressure alongside a large Rhino model still needs evaluation.

## Find existing model folders

Rhino Settings now has **Model library… → Find existing model…**. The native folder picker checks only the selected model directory. It does not search the user's home folder or another app's library. A matching folder can be remembered for later setup; **Remove from list** deletes only that entry. Missing drives stay listed for reconnection. The configured model is pinned separately and can show readiness only from its existing runtime/verification/edit acceptance.

`model_library.py` performs bounded metadata inspection without ML imports or reading tensor bytes. It recognises the first Diffusers FLUX.2 klein 4B layout and the current MFLUX saved layout as candidates, not verified tensor/model identities. It explains app `.ckpt` files, incomplete components, unknown model layouts, links and unsupported files. It never starts conversion, inference, downloads or package installation. Source/licence review and full verification/edit acceptance remain separate. Other model families and formats need reviewed adapters.

```sh
python3 -I engine/model_library.py inspect --directory /absolute/model-folder
python3 -I engine/model_library.py add --directory /absolute/model-folder
python3 -I engine/model_library.py list
python3 -I engine/model_library.py remove --id <entry-id>
```

The private `engine-prototype/models.json` stores up to 32 explicit folder paths. Inspection visits at most 4096 files/directories and bounds individual JSON reads to 1 MiB. Writes use a separate OS lock and atomic replacement; no source files or receipts are deleted. This local catalogue is not executable configuration, and saving an entry never rewrites `connection.json` or advertises a new render capability. Symlinked Hugging Face/app caches require a future explicit materialisation/import step. Windows UNC/reparse paths are rejected; this is not a general network-filesystem detector on mounted Unix volumes.

The desktop bridge starts only the fixed `model_library.py` adjacent to the engine script named by the existing private runtime connection. It passes no cloud keys, uses isolated Python, limits output, and stops inspection after 15 seconds. There is no new HTTP endpoint for filesystem paths or executable commands. The library currently needs that development runtime/configuration; standalone engine installation, preparation/activation from Settings and app/server connectors remain future work. Exercise this same bridge without Rhino with `dotnet run --project examples/LocalModelCheck -- library` or `inspect-folder /absolute/model-folder`.

## Run one prepared edit

Prepare a PNG with edges divisible by 64, at most 1536 pixels per edge and 1.5 megapixels. Any scaling/padding belongs in the recorded capture plan before this boundary. The engine never crops, stretches or resizes it. Prepare the effective prompt in a UTF-8 text file using the concise preset, preservation objective and optional user instructions; see `OBJECTIVE` in `engine.py`.

```sh
python3 engine/engine.py edit \
  --job-id 6148b30e-cc69-4c84-bab6-8ef4ff34b53c \
  --model-directory /absolute/path/to/complete-model \
  --reference /absolute/path/to/prepared-reference.png \
  --prompt /absolute/path/to/prepared-prompt.txt
```

That example identity represents **one** edit. Reusing it with identical content returns its stored terminal record; changed content or changed model files conflict. A new deliberate edit needs a new UUID. Jobs persist under the user's VisoLoom engine-prototype folder (`LOCALAPPDATA` on Windows, `.local/share` on Mac). `--state-directory` selects another private local folder. There is no automatic retention cleanup yet.

```sh
python3 engine/engine.py status --job-id 6148b30e-cc69-4c84-bab6-8ef4ff34b53c
python3 engine/engine.py cancel --job-id 6148b30e-cc69-4c84-bab6-8ef4ff34b53c
python3 engine/engine.py recover
```

The edit command waits while its worker runs; status and cancellation can be called from another process. A cancellation request is distinct from confirmed process exit. The coordinator kills its isolated worker, waits for exit and then records cancellation. Ctrl-C follows the same path. The 15-minute deadline stops the process and records failure, without retrying. Other submissions return `engineBusy`; there is no queue yet.

Default failure records contain a code, exception type and stack locations without exception text or absolute paths. For a deliberate developer diagnostic run, set `VISOLOOM_ENGINE_DIAGNOSTICS=1`. This adds a bounded message to private `failure.json`, omitting the known prompt, model directory and job path. It never appears in normal status output. Keep that file private; redaction of known inputs is not a guarantee that arbitrary library messages contain no sensitive information.

After a hard coordinator crash, an orphan worker may finish before its inference lock is released. `recover` refuses to touch a live inference worker; once the lock is free it marks abandoned jobs failed. It never reruns them or accepts orphan outputs. This is a bounded prototype lifecycle, not a persistent service with automatic restart supervision. Windows termination is for the single worker process; future backends that spawn descendants need an OS process-group/job-object implementation before claiming cancellation support.

The adapter uses four steps, guidance 1, seed 1, float16 compute and requested 4-bit quantisation. It records the loaded quantisation too, since a saved checkpoint can carry its own quantisation. MLX receives an 8 GiB allocation guideline and a 256 MiB cache limit; the guideline is not a hard allocation cap. The coordinator separately samples Mac resident memory once per second and stops a worker observed above 10 GiB. This abort budget is not whole-system memory-pressure protection. These are model-specific test settings, not a universal fidelity control. Weight loading and quantisation run on the CPU before GPU inference, so disk reads are not mixed into the first GPU graph. The adapter also bounds Metal command batches using [MLX's documented controls](https://ml-explore.github.io/mlx/build/html/usage/environment_variables.html) (one operation / approximately 8 MB). Neither measure changes the reference, prompt or four-step editing contract. MFLUX 0.22.0 internally downsizes references above 1024² pixels and centre-crops residual dimensions to multiples of 16; the 512 × 448 acceptance input needs neither transform. Larger-frame conditioning must be recorded and checked before panel integration.

The prepared prompt must fit 2048 UTF-8 bytes. The worker separately counts the **full chat-template-formatted prompt** with the model's local tokenizer and rejects more than 512 tokens before generation. No user text is silently truncated. Only the reference image and effective prompt condition this edit: camera JSON, material names and scene metadata are not claimed as consumed. Results keep the prepared dimensions. The worker fully decodes the input/output with the runtime's Pillow; the coordinator checks size and provenance hashes. Exported PNGs contain pixels only, with provenance in private JSON.

## Connect the local service and Rhino

The development launcher reads a private `~/.local/share/VisoLoom/engine-prototype/connection.json` when present. Its contents point only to local, explicitly prepared tooling:

```json
{
  "python": "/absolute/runtime/bin/python",
  "script": "/absolute/VisoLoom/engine/engine.py",
  "installation": "/absolute/private/installation.json",
  "modelDirectory": "/absolute/prepared-model"
}
```

Keep that file private; do not commit machine paths. Alternatively the service accepts `VISOLOOM_ENGINE_CONFIG` pointing to the private file. No network request can supply executable commands, scripts, runtime/model paths or a workflow. Startup only performs a lightweight check, never generation. The existing authenticated loopback service/automatic token handoff remain in use; no unauthenticated listener is added.

After starting the rebuilt launcher and loading the updated plug-in, choose **Settings → Check local model**, then **FLUX.2 klein 4B · this computer** in the panel. Model preparation is separate from this readiness check. Preview and prompt review remain local; Render submits the frozen prepared PNG and concise effective prompt. Scene records remain private provenance, not claimed as model conditioning. OpenAI's prompt/context behavior is unchanged.

Local canvases are limited to 1,048,576 pixels on a 64-pixel grid, so MFLUX's internal reference preparation needs neither downscaling nor a residual crop. An optional fractional sampling rectangle records a single uniform scale when the complete source aspect cannot fit as whole pixels. Capture and save dimensions stay explicit; an upscaled export is not native high-resolution generation. The existing OpenAI integer framing/codec path is retained. Mathematical framing tests pass; the new fractional Rhino drawing path and Settings interaction still need an in-host check. Standalone Mac drawing cannot initialise Rhino's host bindings, and the Mac was locked during native UI verification.

The authenticated service verifies output/model/source hashes before serving the result. Cancellation remains running/requested until process exit is confirmed. Duplicate accepted submissions recover the same saved result; no accepted generation is retried automatically. Normal terminal-job retention also removes its finished Python copy. An orphan that still reports running is never deleted underneath a live worker; a full orphan supervisor/cleanup policy remains open.

## Windows and remaining work

The coordinator and fixture transport tests support Mac and Windows. Another adapter supplies identity, limits and a worker command chosen by VisoLoom. Evaluate [Diffusers' FLUX.2 pipelines](https://huggingface.co/docs/diffusers/api/pipelines/flux2) with PyTorch/CUDA for Windows/NVIDIA, with a separately pinned/reviewed runtime and real hardware. Windows GPU inference, Intel/AMD GPUs, Windows ARM and CPU inference are not advertised as supported. Other editing models follow after the first integrated slice passes native acceptance.

There is no runtime/model downloader, queue supervisor, automatic updater or office-connection UI. Office transport still requires authenticated HTTPS and approved origins. Native runtime redistribution review remains incomplete; no ML runtime or weights are bundled with Rhino.

Offline Hugging Face/Transformers settings and a credential-free environment avoid their normal downloads and credential use. They are **not an OS network sandbox** and do not establish malicious-code isolation. Unix job folders/files use 0700/0600; Windows inherits profile ACLs, whose native acceptance remains outstanding. The memory guideline and sampled RSS budget are not whole-system memory-pressure protection.
