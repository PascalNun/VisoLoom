# Local engine prototype

VisoLoom should let a designer choose a local model in Rhino without opening another rendering app. This folder starts the engine behind that experience. It is a developer prototype, not an installed Rhino connection or a model installer.

`engine.py` owns job identity, frozen inputs, one worker at a time, private records and cancellation. `mflux_backend.py` is the first model adapter: an isolated Apple Silicon process using MFLUX's FLUX.2 klein 4B **image-editing** pipeline. `common.py` holds the shared OS lock. None of these files or ML dependencies are loaded into Rhino.

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

## Windows and the next integration

The coordinator has no GPU-framework dependency. Another adapter supplies a model identity, preflight, model-file fingerprint, fixed parameters and a worker command chosen by VisoLoom. Network requests must never supply executable commands, Python scripts or arbitrary workflows.

Evaluate [Diffusers' FLUX.2 pipelines](https://huggingface.co/docs/diffusers/api/pipelines/flux2) with PyTorch/CUDA for the first Windows/NVIDIA adapter. Pin and review its runtime, then test the same capture/prompt/frame contract on real hardware. Intel/AMD GPUs, Windows ARM and CPU inference are not advertised as supported. Qwen and other editing models can follow with their own tested adapters and limits. A model switch need not use the same runtime on every operating system.

Next: reuse a verified quantised installation and remove repeated cold-start work, then connect this worker boundary to the existing authenticated capabilities/job service and Rhino Settings/Model selector. The CLI is not the intended designer workflow. The reference server remains unchanged. There is no listener or LAN mode here; an office connection still requires authenticated HTTPS and approved origins.

Initial native attempts reached GPU evaluation but failed with a Metal GPU timeout. Smaller command batches and evaluating individual weight arrays on the GPU did not resolve it. A process trace also showed file reads while GPU evaluation waited; the revised CPU preparation path separates those stages. This observation is not proof of the timeout's root cause. The revised profile completed one native 512 × 448 GPU edit in 25.668 seconds, following 258.527 seconds of model construction/weight preparation. Worker lifetime including verification was 492.858 seconds, plus a separate controller scan before job creation. This is one small-frame development acceptance, not a production readiness claim. See the implementation review for measurements and fidelity limits. Full model scans currently add several minutes on the external drive; installation verification and a trusted model record must be separated from the normal render path before panel integration.

Offline Hugging Face/Transformers settings and a credential-free environment avoid their normal downloads and credential use. They are **not an OS network sandbox** and do not establish malicious-code isolation. A shipped engine needs an audited runtime, verified model manifest, installer/storage checks, OS permission/egress tests and automatic process supervision. Unix job folders/files use 0700/0600; Windows inherits the user's profile ACLs, whose native acceptance remains outstanding. No inference package, runtime or weights are bundled with the plug-in.
