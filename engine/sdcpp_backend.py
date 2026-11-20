"""stable-diffusion.cpp adapter (ggml). The pinned runner edits on this computer.

This module stays import-safe: no ML library is imported here. Generation runs
as an isolated process tree: the coordinator spawns this file as a worker, the
worker spawns the reviewed native runner (sd-cli), and nothing else. The
coordinator owns cancellation, deadline and memory budgets for the whole tree.
"""

from contextlib import ExitStack
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import struct
import subprocess
import sys
import time
import traceback

sys.path.insert(0, str(Path(__file__).absolute().parent))
from common import EngineError, WorkerLock, default_state_directory
import model_store

SDCPP_COMMIT = "80c4707"
MODEL_ID = "flux3-image-klein-q4k"
PACKAGE_FILES = ("flux3-image-klein-Q4_K.gguf", "Qwen3VL-4B-Instruct-Q4_K_M.gguf", "flux2_ae.safetensors")
# The reviewed FLUX.3 Image Klein editing package: single-file ggml weights for
# the transformer and the Qwen3-VL text encoder plus the FLUX.2 autoencoder.
# Names are the pinned package identity; the receipt hashes their contents.
ALLOWED_FILE_SUFFIXES = {".gguf", ".safetensors", ".md", ".json", ".txt"}
# The staged runner for this platform. A rebuilt or altered runner fails closed.
RUNNER_SHA256 = "3540d278aea9337177b01a9a8f7ae52ce06526000a5536897688cb1435b0cbba"
MAX_PACKAGE_FILES = 64


def runner_path():
    """Per-platform runner location inside the private engine root (ADR 0022)."""
    system = platform.system().lower()
    name = "sd-cli.exe" if os.name == "nt" else "sd-cli"
    return default_state_directory() / "bin" / f"{system}-{platform.machine().lower()}" / name


def png_frame(data):
    # The coordinator validated the frame before submission; the worker re-checks
    # the header before the runner starts so a replaced file never loads.
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[8:16] != b"\x00\x00\x00\rIHDR":
        raise ValueError("invalidImage")
    return struct.unpack(">II", data[16:24])


class SdcppBackend:
    identifier = "sdcpp-80c4707-prototype-1"
    model_id = MODEL_ID
    profile = "sdcpp-80c4707-klein-q4k"
    # The distilled klein schedule from the runner's own model documentation.
    # These values are generation identity: a request recorded with them runs
    # with exactly these settings, on exactly this pinned runner build.
    parameters = {"seed": 1, "steps": 16, "cfgScale": 1.0, "samplingMethod": "euler",
                  "inferenceDevice": "cpu", "referencePreset": "flux2",
                  "runnerSha256": RUNNER_SHA256,
                  "maximumResidentBytes": 7 * 1024**3}
    maximum_resident_bytes = parameters["maximumResidentBytes"]
    # Floor-tier CPU budget: the ADR 0022 spike measured 98 minutes for a full
    # 512 x 448 edit (16 steps, ~350 s/step). Four hours bounds the acceptance
    # runs; production frames need a measured per-tier budget before wider use.
    maximum_edit_seconds = 4 * 60 * 60

    def __init__(self, installation=None, runner=None):
        self.installation = Path(installation).absolute() if installation else None
        self.runner = Path(runner) if runner else runner_path()

    @staticmethod
    def platform_ready():
        # The pinned runner build was accepted on x86_64 Linux only. Other
        # platforms need their own reviewed runner build and acceptance first.
        return (platform.system() == "Linux" and platform.machine() == "x86_64" and
                runner_path().is_file())

    @staticmethod
    def resident_bytes(pid):
        # Sample the whole process tree: the worker supervises the runner, and
        # the runner holds the generation state. This is a sampled abort budget,
        # not an OS allocation cap or whole-system memory-pressure protection.
        if os.name != "posix":
            return 0
        try:
            parents, resident = {}, {}
            for entry in Path("/proc").iterdir():
                if not entry.name.isdigit():
                    continue
                with (entry / "stat").open() as stream:
                    stat = stream.read()
                # The comm field can contain spaces and ')' characters; the
                # real fields only start after the last ')'.
                fields = stat[stat.rindex(")") + 2:].split()
                parents[entry.name] = fields[1]
                with (entry / "status").open() as stream:
                    for line in stream:
                        if line.startswith("VmRSS:"):
                            resident[entry.name] = int(line.split()[1]) * 1024
                            break
        except OSError:
            return 0
        total, counted, pending = 0, {str(pid)}, [str(pid)]
        while pending:
            current = pending.pop()
            for name, parent in parents.items():
                if parent == current and name not in counted and name in resident:
                    counted.add(name)
                    pending.append(name)
        for name in counted:
            total += resident.get(name, 0)
        return total

    @staticmethod
    def compatible_folder(root):
        root = Path(root)
        if not root.is_dir():
            return False
        return all((root / name).is_file() for name in PACKAGE_FILES)

    def runner_present(self):
        return (self.runner.is_file() and not self.runner.is_symlink() and
                os.access(self.runner, os.X_OK))

    def runner_digest(self):
        value = hashlib.sha256()
        with self.runner.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                value.update(chunk)
        return value.hexdigest()

    def check(self, model_directory=None):
        result = {"backend": self.identifier, "modelId": MODEL_ID, "available": False,
                  "requiresReference": True, "consumedPasses": ["reference"],
                  "maximumPromptBytes": 2048, "promptOverflow": "reject", "frameGrid": 64,
                  "maximumPixels": 1572864, "supportsCancellation": "processExitConfirmed",
                  "networkListener": False, "automaticDownloads": False}
        def unavailable(code, message):
            return dict(result, code=code, message=message)
        if platform.system() != "Linux" or platform.machine() != "x86_64":
            return unavailable("backendUnavailable", "The stable-diffusion.cpp adapter needs an x86_64 Linux computer for its reviewed runner.")
        if not self.runner_present():
            return unavailable("runtimeMissing", f"The reviewed runner is missing at {self.runner}. Stage the reviewed build; this check installs nothing.")
        if self.runner_digest() != RUNNER_SHA256:
            return unavailable("runtimeMismatch", f"The runner at {self.runner} does not match the reviewed build. Re-stage it; this check installs nothing.")
        try:
            probe = subprocess.run([str(self.runner), "--help"], stdout=subprocess.DEVNULL,
                                   stderr=subprocess.DEVNULL, timeout=10, check=False)
        except (OSError, subprocess.TimeoutExpired):
            return unavailable("runtimeMismatch", "The staged runner does not run on this computer; no replacement runtime was selected.")
        if probe.returncode != 0:
            return unavailable("runtimeMismatch", "The staged runner does not run on this computer; no replacement runtime was selected.")
        if model_directory is None or not self.compatible_folder(Path(model_directory)):
            return unavailable("missingLocalComponents", "Choose a complete local FLUX.3 klein Q4_K package: the transformer and text-encoder gguf files and the FLUX.2 autoencoder. Nothing was downloaded.")
        result.update(available=True, acceptance="unvalidated",
                      message="Pinned runner and complete local package found. Model loading and hardware suitability still need a real edit test.")
        result["freeBytes"] = shutil.disk_usage(model_directory).free
        if self.installation:
            record = model_store.checked(self.installation, Path(model_directory), profile=self.profile)
            result.update(modelDigest=record["digest"], acceptance="nativeEditPassed" if
                          record.get("nativeAcceptance", {}).get("backend") == self.identifier else "unvalidated")
        return result

    def fingerprint(self, root):
        if self.installation:
            return model_store.checked(self.installation, root, profile=self.profile)["digest"]
        if not self.compatible_folder(root):
            raise EngineError("missingLocalComponents", "A complete compatible local model package is required; no download was started.")
        digest = hashlib.sha256()
        count = 0
        for path in sorted(Path(root).rglob("*")):
            if path.is_symlink():
                raise EngineError("invalidModel", "Use a materialised model package without symbolic links.")
            if path.is_file():
                if path.suffix not in ALLOWED_FILE_SUFFIXES:
                    raise EngineError("invalidModel", "The model package contains an unsupported file type.")
                count += 1
                if count > MAX_PACKAGE_FILES:
                    raise EngineError("invalidModel", "The model package exceeds its file limit.")
                file_hash = hashlib.sha256()
                with path.open("rb") as stream:
                    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                        file_hash.update(chunk)
                digest.update(path.relative_to(root).as_posix().encode() + b"\0" + file_hash.digest())
        return digest.hexdigest()

    @staticmethod
    def command(job):
        return [sys.executable, "-I", str(Path(__file__).absolute()), str(job)]

    @staticmethod
    def spawn_runner(job, log_file, command_arguments):
        """Start the runner inside the worker's own process group. The
        coordinator stops the whole reviewed tree with one process-group
        kill (engine.stop_worker); a runner that opened its own session
        would survive a cancelled, timed-out or budget-killed worker and
        keep running without its deadline or memory budget."""
        return subprocess.Popen(command_arguments, cwd=job, stdin=subprocess.DEVNULL,
                                stdout=log_file, stderr=subprocess.STDOUT)

    @staticmethod
    def runner_command(runner, model_path, job, width, height):
        """The pinned runner invocation. Every file and setting comes from the
        frozen request. Output metadata embedding stays off so generation
        provenance lives only in the private job record."""
        p = SdcppBackend.parameters
        return [str(runner),
                "--diffusion-model", str(Path(model_path) / PACKAGE_FILES[0]),
                "--llm", str(Path(model_path) / PACKAGE_FILES[1]),
                "--vae", str(Path(model_path) / PACKAGE_FILES[2]),
                "-r", str(job / "reference.png"), "--prompt-file", str(job / "prompt.txt"),
                "-o", str(job / "result.png"),
                "--cfg-scale", str(p["cfgScale"]), "--steps", str(p["steps"]),
                "--sampling-method", p["samplingMethod"], "--seed", str(p["seed"]),
                "-W", str(width), "-H", str(height),
                "--backend", p["inferenceDevice"], "--offload-to-cpu",
                "--disable-image-metadata", "-v"]


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
    known = {"promptTooLong", "invalidImage", "missingLocalComponents", "runtimeMismatch",
             "unsupportedRuntime", "installationChanged"}
    return str(exc) if isinstance(exc, ValueError) and str(exc) in known else "workerFailed"


def run(job):
    progress(job, "checkingLocalFiles")
    request = json.loads((job / "request.json").read_text(encoding="utf-8"))
    backend = SdcppBackend(request.get("installation"))
    if (request.get("modelId") != MODEL_ID or request.get("backend") != SdcppBackend.identifier or
            request.get("parameters") != SdcppBackend.parameters):
        raise ValueError("unsupportedRuntime")
    if not backend.runner_present() or backend.runner_digest() != RUNNER_SHA256:
        raise ValueError("runtimeMismatch")
    model_path = Path(request["modelDirectory"])
    if not backend.compatible_folder(model_path):
        raise ValueError("missingLocalComponents")
    check = backend.check(model_path)
    if not check["available"]:
        raise ValueError(check["code"])
    if backend.fingerprint(model_path) != request["modelDigest"]:
        raise ValueError("missingLocalComponents")

    progress(job, "decodingReference")
    reference = (job / "reference.png").read_bytes()
    width, height = png_frame(reference)
    if (width, height) != (request["width"], request["height"]):
        raise ValueError("invalidImage")

    progress(job, "checkingPrompt")
    prompt = request["prompt"]
    if not isinstance(prompt, str) or len(prompt.encode("utf-8")) > 2048 or "\x00" in prompt:
        raise ValueError("promptTooLong")
    prompt_file = job / "prompt.txt"
    prompt_file.write_text(prompt, encoding="utf-8")
    if os.name != "nt":
        prompt_file.chmod(0o600)

    progress(job, "loadingModel")
    progress(job, "generating")
    started = time.monotonic()
    generation_seconds = None
    # Private job-local record of the runner's own messages. It never leaves the
    # job folder and is removed with normal retention, like the result files.
    log_file = (job / "runner.txt").open("w", encoding="utf-8")
    process = None
    try:
        process = SdcppBackend.spawn_runner(job, log_file,
                                             SdcppBackend.runner_command(backend.runner, model_path, job, width, height))
        process.wait()
        generation_seconds = time.monotonic() - started
        if process.returncode != 0:
            raise ValueError("workerFailed")
    finally:
        log_file.close()
        if process is not None and process.poll() is None:
            process.kill()
            process.wait(timeout=10)

    progress(job, "validatingOutput")
    output = (job / "result.png").read_bytes()
    if png_frame(output) != (width, height):
        raise ValueError("invalidImage")
    if backend.installation and backend.fingerprint(model_path) != request["modelDigest"]:
        raise ValueError("installationChanged")
    p = SdcppBackend.parameters
    evidence = {"runtime": "stable-diffusion.cpp " + SDCPP_COMMIT,
                "elapsedSeconds": time.monotonic() - started, "generationSeconds": generation_seconds,
                "sourceSha256": hashlib.sha256(reference).hexdigest(),
                "outputSha256": hashlib.sha256(output).hexdigest(),
                "seed": p["seed"], "steps": p["steps"], "cfgScale": p["cfgScale"],
                "samplingMethod": p["samplingMethod"], "inferenceDevice": p["inferenceDevice"],
                "referencePreset": p["referencePreset"],
                "consumedPasses": ["reference"]}
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
