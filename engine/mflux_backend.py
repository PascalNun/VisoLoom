"""Optional Mac adapter. All ML imports stay in the isolated generation process."""

import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import platform
import shutil
import sys
import time

# -I excludes the caller's working directory and PYTHONPATH. Only our own source
# directory is added for these two local modules, never the model/job directory.
sys.path.insert(0, str(Path(__file__).absolute().parent))
from common import EngineError, WorkerLock

MFLUX_VERSION = "0.22.0"
MODEL_ID = "flux2-klein-4b"


class MfluxBackend:
    identifier = "mac-mflux-0.22.0-prototype-1"
    model_id = MODEL_ID
    parameters = {"seed": 1, "steps": 4, "guidance": 1.0, "scheduler": "flow_match_euler_discrete",
                  "requestedQuantize": 4, "computePrecision": "float16", "useKvCache": False}

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
        return result

    @staticmethod
    def compatible_folder(root):
        if not root.is_dir():
            return False
        for component in ["transformer", "text_encoder", "vae"]:
            if not any((root / component).glob("*.safetensors")):
                return False
        return (root / "tokenizer" / "tokenizer_config.json").is_file()

    @staticmethod
    def fingerprint(root):
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


def run(job):
    # Kept here too for direct worker invocations. Library offline flags are not an OS sandbox.
    os.environ.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1", DO_NOT_TRACK="1")
    request = json.loads((job / "request.json").read_text(encoding="utf-8"))
    if (request.get("modelId") != MODEL_ID or request.get("backend") != MfluxBackend.identifier or
            request.get("parameters") != MfluxBackend.parameters):
        raise ValueError("unsupportedRuntime")
    model_path = Path(request["modelDirectory"])
    check = MfluxBackend().check(model_path)
    if not check["available"]:
        raise ValueError(check["code"])
    if MfluxBackend.fingerprint(model_path) != request["modelDigest"]:
        raise ValueError("missingLocalComponents")

    from PIL import Image
    from transformers import Qwen2TokenizerFast
    import mlx.core as mx
    from mflux.models.common.config import ModelConfig
    from mflux.models.flux2.variants import Flux2KleinEdit

    Image.MAX_IMAGE_PIXELS = 1572864
    with Image.open(job / "reference.png") as source:
        if source.format != "PNG" or source.size != (request["width"], request["height"]) or getattr(source, "n_frames", 1) != 1:
            raise ValueError("invalidImage")
        source.load()
    tokenizer = Qwen2TokenizerFast.from_pretrained(str(model_path / "tokenizer"), local_files_only=True)
    token_count = count_prompt(tokenizer, request["prompt"])
    started = time.monotonic()
    model = Flux2KleinEdit(model_config=ModelConfig.flux2_klein_4b(), model_path=str(model_path),
                          quantize=4, compute_precision=mx.float16, lora_paths=[], lora_scales=[])
    # Check the loaded wrapper as well: its template/settings must match the preflight.
    wrapper = model.tokenizers["qwen3"]
    if (wrapper.max_length != 512 or not wrapper.use_chat_template or wrapper.template or
            wrapper.chat_template_kwargs != {"enable_thinking": False} or not wrapper.add_special_tokens):
        raise ValueError("unsupportedRuntime")
    token_count = count_prompt(wrapper.tokenizer, request["prompt"])
    image = model.generate_image(seed=request["parameters"]["seed"], prompt=request["prompt"], image_paths=[job / "reference.png"],
                                 num_inference_steps=4, width=request["width"], height=request["height"],
                                 guidance=1.0, scheduler="flow_match_euler_discrete", use_kv_cache=False)
    # MFLUX's wrapper save embeds metadata, including local image paths. Export
    # pixels only; the full provenance belongs in our private job record.
    pixels = image.image.copy()
    pixels.info.clear()
    pixels.save(str(job / "result.png"), format="PNG")
    with Image.open(job / "result.png") as output:
        if output.format != "PNG" or output.size != (request["width"], request["height"]) or getattr(output, "n_frames", 1) != 1:
            raise ValueError("invalidImage")
        output.load()
    evidence = {"runtime": MFLUX_VERSION, "promptTokens": token_count, "elapsedSeconds": time.monotonic() - started,
                "sourceSha256": hashlib.sha256((job / "reference.png").read_bytes()).hexdigest(),
                "outputSha256": hashlib.sha256((job / "result.png").read_bytes()).hexdigest(),
                "requestedQuantize": 4, "loadedQuantize": model.bits, "computePrecision": "float16", "scheduler": "flow_match_euler_discrete",
                "referenceConditioning": "singleImageTokens", "consumedPasses": ["reference"]}
    (job / "result.json").write_text(json.dumps(evidence), encoding="utf-8")
    for path in [job / "result.png", job / "result.json"]:
        if os.name != "nt":
            path.chmod(0o600)


if __name__ == "__main__":
    os.umask(0o077)
    job = Path(sys.argv[1])
    try:
        with WorkerLock(job.parent, ".inference.lock"):
            # A crashed coordinator cannot leave an older worker racing a new job.
            state = json.loads((job / "state.json").read_text(encoding="utf-8"))
            if state["state"] != "running":
                raise ValueError("workerFailed")
            run(job)
    except Exception as exc:
        known = {"promptTooLong", "invalidImage", "missingLocalComponents", "runtimeMismatch", "unsupportedRuntime"}
        code = str(exc) if isinstance(exc, ValueError) and str(exc) in known else "workerFailed"
        (job / "failure.json").write_text(json.dumps({"code": code}), encoding="utf-8")
        if os.name != "nt":
            (job / "failure.json").chmod(0o600)
        sys.exit(1)
