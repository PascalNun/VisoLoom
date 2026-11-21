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
import re
import shutil
import struct
import subprocess
import sys
import time
import traceback

sys.path.insert(0, str(Path(__file__).absolute().parent))
from common import EngineError, WorkerLock, default_state_directory
import model_install
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


def platform_key(system=None, machine=None):
    """The per-platform key for runner pins and staged paths. Windows reports
    its CPU architecture as AMD64; the pins and staged paths say x86_64."""
    system = (system if system is not None else platform.system()).lower()
    machine = (machine if machine is not None else platform.machine()).lower()
    if system == "windows" and machine == "amd64":
        machine = "x86_64"
    return f"{system}-{machine}"


def runner_path():
    """Per-platform runner location inside the private engine root (ADR 0022)."""
    name = "sd-cli.exe" if os.name == "nt" else "sd-cli"
    return default_state_directory() / "bin" / platform_key() / name


# The native FLUX.2 klein 4B editing profile: the reviewed one-click download
# package on the same runner binary family, one pinned build per platform.
FLUX2_MODEL_ID = "flux2-klein-4b-native"
FLUX2_RUNNER_SHA256 = {
    # x86_64 Linux carries the accepted floor-tier build; darwin-arm64 is the
    # reviewed Metal build. A Windows pin lands with that platform's first-edit
    # acceptance and stays absent until then.
    "linux-x86_64": RUNNER_SHA256,
    "darwin-arm64": "3b4d9683b79ca8626288715fc9b7b07c047c4ebcb75a7e7e6cf38403b0a0d345",
    "windows-x86_64": None,
}


def _flux2_device(system, machine):
    if system == "Darwin" and machine == "arm64":
        return "metal"
    if system == "Windows":
        return "cuda"
    return "cpu"


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
    # Class-level on purpose: the FLUX.2 native profile lists its own reviewed
    # package files, and every method below names files through this attribute.
    PACKAGE_FILES = PACKAGE_FILES
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

    @classmethod
    def compatible_folder(cls, root):
        root = Path(root)
        if not root.is_dir():
            return False
        return all((root / name).is_file() for name in cls.PACKAGE_FILES)

    def runner_present(self):
        return (self.runner.is_file() and not self.runner.is_symlink() and
                os.access(self.runner, os.X_OK))

    def runner_digest(self):
        value = hashlib.sha256()
        with self.runner.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                value.update(chunk)
        return value.hexdigest()

    @classmethod
    def runner_pin(cls):
        """The reviewed digest for this platform's pinned build (ADR 0022)."""
        return RUNNER_SHA256

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
    def spawn_runner(job, command_arguments):
        """Start the runner inside the worker's own process group. The
        coordinator stops the whole reviewed tree with one process-group
        kill (engine.stop_worker); a runner that opened its own session
        would survive a cancelled, timed-out or budget-killed worker and
        keep running without its deadline or memory budget. Output is a
        pipe the worker replays into runner.txt while parsing step progress."""
        return subprocess.Popen(command_arguments, cwd=job, stdin=subprocess.DEVNULL,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT)

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


class SdcppFlux2Backend(SdcppBackend):
    """The native FLUX.2 klein 4B editing profile: the reviewed one-click
    package run through the per-platform pinned runner. Package names come from
    the installer's own pins, so the adapter and the catalogue describe the
    same reviewed files; the distilled klein schedule (4 steps, cfg 1, euler,
    flux2 reference preset) is the runner's recipe, frozen in parameters."""
    identifier = "sdcpp-80c4707-flux2-native-1"
    model_id = FLUX2_MODEL_ID
    profile = "sdcpp-80c4707-flux2-klein-4b"
    PACKAGE_FILES = tuple(file["name"] for file in model_install.PACKAGES["flux2-klein-4b-gguf"]["files"])
    parameters = {"seed": 1, "steps": 4, "cfgScale": 1.0, "samplingMethod": "euler",
                  "inferenceDevice": _flux2_device(platform.system(), platform.machine()),
                  "referencePreset": "flux2",
                  "runnerSha256": FLUX2_RUNNER_SHA256.get(platform_key()),
                  "maximumResidentBytes": 7 * 1024**3}
    maximum_resident_bytes = parameters["maximumResidentBytes"]
    # Metal and CUDA complete a distilled edit in seconds to minutes; the CPU
    # floor tier keeps the generous bound until measured budgets are recorded.
    maximum_edit_seconds = 4 * 60 * 60

    @staticmethod
    def platform_ready():
        # Ready only with this platform's own reviewed build pinned and staged.
        return FLUX2_RUNNER_SHA256.get(platform_key()) is not None and runner_path().is_file()

    @classmethod
    def runner_pin(cls):
        return FLUX2_RUNNER_SHA256.get(platform_key())

    def check(self, model_directory=None):
        pin = self.runner_pin()
        result = {"backend": self.identifier, "modelId": FLUX2_MODEL_ID, "available": False,
                  "requiresReference": True, "consumedPasses": ["reference"],
                  "maximumPromptBytes": 2048, "promptOverflow": "reject", "frameGrid": 64,
                  "maximumPixels": 1048576, "supportsCancellation": "processExitConfirmed",
                  "networkListener": False, "automaticDownloads": False}
        def unavailable(code, message):
            return dict(result, code=code, message=message)
        if pin is None:
            return unavailable("backendUnavailable",
                               "The FLUX.2 klein 4B native profile needs a reviewed runner for this computer: Apple Silicon Mac (Metal), Windows with NVIDIA CUDA, or the x86_64 Linux build. None is staged here.")
        if not self.runner_present():
            return unavailable("runtimeMissing", f"The reviewed runner is missing at {self.runner}. Stage the reviewed build; this check installs nothing.")
        if self.runner_digest() != pin:
            return unavailable("runtimeMismatch", f"The runner at {self.runner} does not match the reviewed build. Re-stage it; this check installs nothing.")
        try:
            probe = subprocess.run([str(self.runner), "--help"], stdout=subprocess.DEVNULL,
                                   stderr=subprocess.DEVNULL, timeout=10, check=False)
        except (OSError, subprocess.TimeoutExpired):
            return unavailable("runtimeMismatch", "The staged runner does not run on this computer; no replacement runtime was selected.")
        if probe.returncode != 0:
            return unavailable("runtimeMismatch", "The staged runner does not run on this computer; no replacement runtime was selected.")
        if model_directory is None or not self.compatible_folder(Path(model_directory)):
            return unavailable("missingLocalComponents", "Choose the installed FLUX.2 klein 4B package: the transformer and encoder gguf files and the shipped decoder. Nothing was downloaded.")
        result.update(available=True, acceptance="unvalidated",
                      message="Pinned runner and complete local package found. Model loading and hardware suitability still need a real edit test.")
        result["freeBytes"] = shutil.disk_usage(model_directory).free
        if self.installation:
            record = model_store.checked(self.installation, Path(model_directory), profile=self.profile)
            result.update(modelDigest=record["digest"], acceptance="nativeEditPassed" if
                          record.get("nativeAcceptance", {}).get("backend") == self.identifier else "unvalidated")
        return result

    @staticmethod
    def runner_command(runner, model_path, job, width, height):
        """The pinned runner invocation; mirrors the base recipe with this
        profile's package files and frozen distilled schedule."""
        p = SdcppFlux2Backend.parameters
        return [str(runner),
                "--diffusion-model", str(Path(model_path) / SdcppFlux2Backend.PACKAGE_FILES[0]),
                "--llm", str(Path(model_path) / SdcppFlux2Backend.PACKAGE_FILES[1]),
                "--vae", str(Path(model_path) / SdcppFlux2Backend.PACKAGE_FILES[2]),
                "-r", str(job / "reference.png"), "--prompt-file", str(job / "prompt.txt"),
                "-o", str(job / "result.png"),
                "--cfg-scale", str(p["cfgScale"]), "--steps", str(p["steps"]),
                "--sampling-method", p["samplingMethod"], "--seed", str(p["seed"]),
                "-W", str(width), "-H", str(height),
                "--backend", p["inferenceDevice"], "--offload-to-cpu",
                "--disable-image-metadata", "-v"]


def progress(job, phase, **details):
    record = {"phase": phase, "startedAt": time.time()}
    record.update(details)
    temporary = job / ".progress.tmp"
    temporary.write_text(json.dumps(record), encoding="utf-8")
    os.replace(temporary, job / "progress.json")


# The runner redraws one line per step: "1/4 -  67.56s/it - ETA 03:22".
STEP_MARKER = re.compile(rb"(\d+)/(\d+) - +([0-9]+(?:\.[0-9]+)?)s/it - ETA ([0-9]+):([0-9]{2})")


def parse_step_progress(tail):
    """The last complete step marker in the output tail, or None. Numbers the
    runner cannot honestly report (a step outside the schedule, a missing
    rate) are discarded rather than passed to the waiting client."""
    match = None
    for match in STEP_MARKER.finditer(tail):
        pass
    if match is None:
        return None
    step, steps = int(match.group(1)), int(match.group(2))
    rate = float(match.group(3))
    eta = int(match.group(4)) * 60 + int(match.group(5))
    # No honest estimate exceeds the coordinator's own edit deadline.
    if not 0 < step <= steps or steps > 10000 or rate <= 0 or eta > 14400:
        return None
    return {"step": step, "steps": steps, "secondsPerStep": rate, "etaSeconds": eta}


def replay_runner_output(job, log_file, process):
    """Copy the runner's bytes into runner.txt and mirror sampling progress
    into progress.json. Only the first marker after a new step is recorded, so
    the file is written at most once per sampling step."""
    tail = b""
    reported = None
    while True:
        chunk = process.stdout.read1(4096)
        if not chunk:
            break
        log_file.write(chunk)
        tail = (tail + chunk)[-512:]
        parsed = parse_step_progress(tail)
        if parsed is not None and parsed["step"] != reported:
            reported = parsed["step"]
            progress(job, "generating", **parsed)
    log_file.flush()


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
    backend_class = next((candidate for candidate in (SdcppBackend, SdcppFlux2Backend)
                          if request.get("modelId") == candidate.model_id), None)
    if backend_class is None or request.get("backend") != backend_class.identifier or \
            request.get("parameters") != backend_class.parameters:
        raise ValueError("unsupportedRuntime")
    backend = backend_class(request.get("installation"))
    if not backend.runner_present() or backend.runner_digest() != backend_class.runner_pin():
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
    started = time.monotonic()
    generation_seconds = None
    # Private job-local record of the runner's own messages. It never leaves the
    # job folder and is removed with normal retention, like the result files.
    log_file = (job / "runner.txt").open("wb")
    process = None
    try:
        process = backend_class.spawn_runner(job,
                                             backend_class.runner_command(backend.runner, model_path, job, width, height))
        # The "generating" phase starts with the runner's first sampling step;
        # everything before it (weight loading, buffer allocation) stays honest
        # under "loadingModel" in the waiting client's status line.
        replay_runner_output(job, log_file, process)
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
    p = backend_class.parameters
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
