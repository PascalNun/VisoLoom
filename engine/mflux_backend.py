"""Optional Mac adapter. All ML imports stay in the isolated generation process."""

import hashlib
from contextlib import ExitStack
import importlib.metadata
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import time
import traceback

# -I excludes the caller's working directory and PYTHONPATH. Only our own source
# directory is added for these two local modules, never the model/job directory.
sys.path.insert(0, str(Path(__file__).absolute().parent))
from common import EngineError, WorkerLock
import model_store

MFLUX_VERSION = "0.22.0"
MODEL_ID = "flux2-klein-4b"


class MfluxBackend:
    identifier = "mac-mflux-0.22.0-prototype-5"
    model_id = MODEL_ID
    parameters = {"seed": 1, "steps": 4, "guidance": 1.0, "scheduler": "flow_match_euler_discrete",
                  "requestedQuantize": 4, "computePrecision": "float16", "useKvCache": False,
                  "memoryGuidelineBytes": 8 * 1024**3, "cacheLimitBytes": 256 * 1024**2,
                  "maximumResidentBytes": 10 * 1024**3,
                  "metalOperationsPerBuffer": 1, "metalMegabytesPerBuffer": 8,
                  "materializeWeights": "cpuIndividualArrays", "inferenceDevice": "gpu"}
    maximum_resident_bytes = parameters["maximumResidentBytes"]

    def __init__(self, installation=None):
        self.installation = Path(installation).absolute() if installation else None

    @staticmethod
    def resident_bytes(pid):
        # Mac ps reports resident KiB. It is a sampled abort budget, not an OS
        # allocation cap or a measurement of whole-system memory pressure.
        result = subprocess.run(["/bin/ps", "-o", "rss=", "-p", str(pid)],
                                capture_output=True, text=True, timeout=2, check=False)
        return int(result.stdout.strip() or "0") * 1024

    def check(self, model_directory=None):
        result = {"backend": self.identifier, "modelId": MODEL_ID, "available": False,
                  "requiresReference": True, "consumedPasses": ["reference"],
                  "maximumPromptBytes": 2048, "maximumPromptTokens": 512,
                  "promptOverflow": "reject", "frameGrid": 64, "maximumPixels": 1572864,
                  "supportsCancellation": "processExitConfirmed", "networkListener": False,
                  "automaticDownloads": False}
        def unavailable(code, message):
            return dict(result, code=code, message=message)
        if platform.system() != "Darwin" or platform.machine() != "arm64":
            return unavailable("backendUnavailable", "The MFLUX adapter needs an Apple Silicon Mac. The Windows adapter is not implemented yet.")
        try:
            installed = importlib.metadata.version("mflux")
        except importlib.metadata.PackageNotFoundError:
            return unavailable("runtimeMissing", "The isolated MFLUX runtime is not installed. This check installs nothing.")
        if installed != MFLUX_VERSION:
            return unavailable("runtimeMismatch", "This prototype requires MFLUX 0.22.0; no replacement runtime was selected.")
        if model_directory is None or not self.compatible_folder(Path(model_directory)):
            return unavailable("missingLocalComponents", "Choose a complete local FLUX.2 klein 4B MFLUX/Diffusers folder, including encoders and tokenizer. Draw Things checkpoints are not accepted.")
        result.update(available=True, acceptance="unvalidated", message="Local files and runtime version found. Model loading and hardware suitability still need a real edit test.")
        result["freeBytes"] = shutil.disk_usage(model_directory).free
        if self.installation:
            record = model_store.checked(self.installation, model_directory)
            result.update(modelDigest=record["digest"], acceptance="nativeEditPassed" if
                          record.get("nativeAcceptance", {}).get("backend") == self.identifier else "unvalidated")
        return result

    @staticmethod
    def compatible_folder(root):
        if not root.is_dir():
            return False
        for component in ["transformer", "text_encoder", "vae"]:
            if not any((root / component).glob("*.safetensors")):
                return False
        return (root / "tokenizer" / "tokenizer_config.json").is_file()

    def fingerprint(self, root):
        if self.installation:
            return model_store.checked(self.installation, root)["digest"]
        if not MfluxBackend.compatible_folder(root):
            raise EngineError("missingLocalComponents", "A complete compatible local model folder is required; no download was started.")
        digest = hashlib.sha256()
        count = 0
        for path in sorted(root.rglob("*")):
            if path.is_symlink():
                raise EngineError("invalidModel", "Use a materialised model folder without symbolic links.")
            if path.is_file():
                if path.suffix not in {".safetensors", ".json", ".txt", ".jinja", ".model", ".md"}:
                    raise EngineError("invalidModel", "The model folder contains an unsupported file type.")
                count += 1
                if count > 4096:
                    raise EngineError("invalidModel", "The model folder exceeds its file limit.")
                file_hash = hashlib.sha256()
                with path.open("rb") as stream:
                    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                        file_hash.update(chunk)
                digest.update(path.relative_to(root).as_posix().encode() + b"\0" + file_hash.digest())
        return digest.hexdigest()

    @staticmethod
    def command(job):
        return [sys.executable, "-I", str(Path(__file__).absolute()), str(job)]


def count_prompt(tokenizer, prompt):
    formatted = tokenizer.apply_chat_template([{ "role": "user", "content": prompt }],
                                               tokenize=False, add_generation_prompt=True, enable_thinking=False)
    count = len(tokenizer(formatted, truncation=False, add_special_tokens=True)["input_ids"])
    if count > 512:
        raise ValueError("promptTooLong")
    return count


def progress(job, phase):
    temporary = job / ".progress.tmp"
    temporary.write_text(json.dumps({"phase": phase, "startedAt": time.time()}), encoding="utf-8")
    os.replace(temporary, job / "progress.json")


def diagnostic_message(exc, job):
    message = str(exc)
    request = json.loads((job / "request.json").read_text(encoding="utf-8"))
    for sensitive in [request.get("prompt"), request.get("modelDirectory"), str(job)]:
        if sensitive:
            message = message.replace(sensitive, "<omitted>")
    return message[:3000]


def failure_code(exc):
    if isinstance(exc, RuntimeError) and "kIOGPUCommandBufferCallbackErrorTimeout" in str(exc):
        return "gpuTimeout"
    known = {"promptTooLong", "invalidImage", "missingLocalComponents", "runtimeMismatch", "unsupportedRuntime", "installationChanged"}
    return str(exc) if isinstance(exc, ValueError) and str(exc) in known else "workerFailed"


def run(job):
    # Kept here too for direct worker invocations. Library offline flags are not an OS sandbox.
    os.environ.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1", DO_NOT_TRACK="1")
    # Limit command-buffer batches for the initial M1 test. These documented MLX
    # controls change scheduling, not the model's image/prompt inputs or step count.
    os.environ["MLX_MAX_OPS_PER_BUFFER"] = str(MfluxBackend.parameters["metalOperationsPerBuffer"])
    os.environ["MLX_MAX_MB_PER_BUFFER"] = str(MfluxBackend.parameters["metalMegabytesPerBuffer"])
    progress(job, "checkingLocalFiles")
    request = json.loads((job / "request.json").read_text(encoding="utf-8"))
    if (request.get("modelId") != MODEL_ID or request.get("backend") != MfluxBackend.identifier or
            request.get("parameters") != MfluxBackend.parameters):
        raise ValueError("unsupportedRuntime")
    model_path = Path(request["modelDirectory"])
    backend = MfluxBackend(request.get("installation"))
    check = backend.check(model_path)
    if not check["available"]:
        raise ValueError(check["code"])
    if backend.fingerprint(model_path) != request["modelDigest"]:
        raise ValueError("missingLocalComponents")

    from PIL import Image
    from transformers import Qwen2TokenizerFast
    import mlx.core as mx
    from mlx.utils import tree_flatten
    from mflux.models.common.config import ModelConfig
    from mflux.models.flux2.variants import Flux2KleinEdit

    mx.set_memory_limit(MfluxBackend.parameters["memoryGuidelineBytes"])
    mx.set_cache_limit(MfluxBackend.parameters["cacheLimitBytes"])

    progress(job, "decodingReference")
    Image.MAX_IMAGE_PIXELS = 1572864
    with Image.open(job / "reference.png") as source:
        if source.format != "PNG" or source.size != (request["width"], request["height"]) or getattr(source, "n_frames", 1) != 1:
            raise ValueError("invalidImage")
        source.load()
    progress(job, "checkingPrompt")
    tokenizer = Qwen2TokenizerFast.from_pretrained(str(model_path / "tokenizer"), local_files_only=True)
    token_count = count_prompt(tokenizer, request["prompt"])
    started = time.monotonic()
    progress(job, "loadingModel")
    # Slow disk reads must finish before GPU work waits on their results. Loading
    # and quantisation use the CPU; the actual image edit still uses the GPU.
    with mx.stream(mx.cpu):
        model = Flux2KleinEdit(model_config=ModelConfig.flux2_klein_4b(), model_path=str(model_path),
                              quantize=4, compute_precision=mx.float16, lora_paths=[], lora_scales=[])
        progress(job, "preparingWeights")
        parameters = tree_flatten(model.parameters())
        for _, parameter in parameters:
            mx.eval(parameter)
    # Check the loaded wrapper as well: its template/settings must match the preflight.
    wrapper = model.tokenizers["qwen3"]
    if (wrapper.max_length != 512 or not wrapper.use_chat_template or wrapper.template or
            wrapper.chat_template_kwargs != {"enable_thinking": False} or not wrapper.add_special_tokens):
        raise ValueError("unsupportedRuntime")
    token_count = count_prompt(wrapper.tokenizer, request["prompt"])
    progress(job, "generating")
    generation_started = time.monotonic()
    with mx.stream(mx.gpu):
        image = model.generate_image(seed=request["parameters"]["seed"], prompt=request["prompt"], image_paths=[job / "reference.png"],
                                     num_inference_steps=4, width=request["width"], height=request["height"],
                                     guidance=1.0, scheduler="flow_match_euler_discrete", use_kv_cache=False)
    generation_seconds = time.monotonic() - generation_started
    # MFLUX's wrapper save embeds metadata, including local image paths. Export
    # pixels only; the full provenance belongs in our private job record.
    pixels = image.image.copy()
    pixels.info.clear()
    pixels.save(str(job / "result.png"), format="PNG")
    progress(job, "validatingOutput")
    if backend.installation and backend.fingerprint(model_path) != request["modelDigest"]:
        raise ValueError("installationChanged")
    with Image.open(job / "result.png") as output:
        if output.format != "PNG" or output.size != (request["width"], request["height"]) or getattr(output, "n_frames", 1) != 1:
            raise ValueError("invalidImage")
        output.load()
    evidence = {"runtime": MFLUX_VERSION, "promptTokens": token_count, "elapsedSeconds": time.monotonic() - started,
                "sourceSha256": hashlib.sha256((job / "reference.png").read_bytes()).hexdigest(),
                "outputSha256": hashlib.sha256((job / "result.png").read_bytes()).hexdigest(),
                "requestedQuantize": 4, "loadedQuantize": model.bits, "computePrecision": "float16", "scheduler": "flow_match_euler_discrete",
                "referenceConditioning": "singleImageTokens", "consumedPasses": ["reference"]}
    evidence["mlxPeakBytes"] = mx.get_peak_memory()
    evidence["generationSeconds"] = generation_seconds
    evidence["modelConstructionSeconds"] = generation_started - started
    evidence["materializedParameterArrays"] = len(parameters)
    evidence["weightPreparationDevice"] = "cpu"
    evidence["inferenceDevice"] = "gpu"
    evidence["metalCommandBuffer"] = {"operations": MfluxBackend.parameters["metalOperationsPerBuffer"],
                                      "megabytes": MfluxBackend.parameters["metalMegabytesPerBuffer"]}
    (job / "result.json").write_text(json.dumps(evidence), encoding="utf-8")
    for path in [job / "result.png", job / "result.json"]:
        if os.name != "nt":
            path.chmod(0o600)


if __name__ == "__main__":
    os.umask(0o077)
    job = Path(sys.argv[1])
    try:
        with ExitStack() as locks:
            locks.enter_context(WorkerLock(job.parent, ".inference.lock"))
            request = json.loads((job / "request.json").read_text(encoding="utf-8"))
            if request.get("installation"):
                installation_root = Path(request["installation"]).parent.resolve()
                if installation_root != job.parent.resolve():
                    locks.enter_context(WorkerLock(installation_root, ".inference.lock"))
            # A crashed coordinator cannot leave an older worker racing a new job.
            state = json.loads((job / "state.json").read_text(encoding="utf-8"))
            if state["state"] != "running":
                raise ValueError("workerFailed")
            run(job)
    except Exception as exc:
        code = exc.code if isinstance(exc, EngineError) else failure_code(exc)
        # Keep debugging useful without copying library exception messages, prompts,
        # absolute paths or credentials into logs or normal status responses.
        stack = [{"file": Path(frame.filename).name, "line": frame.lineno, "function": frame.name}
                 for frame in traceback.extract_tb(exc.__traceback__)[-12:]]
        failure = {"code": code, "exceptionType": type(exc).__name__, "stack": stack}
        if os.environ.get("VISOLOOM_ENGINE_DIAGNOSTICS") == "1":
            try:
                failure["diagnostic"] = diagnostic_message(exc, job)
            except (OSError, ValueError):
                pass
        (job / "failure.json").write_text(json.dumps(failure), encoding="utf-8")
        if os.name != "nt":
            (job / "failure.json").chmod(0o600)
        sys.exit(1)
