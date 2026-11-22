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
# The reviewed FLUX.3 Image Klein editing package as this prototype accepted
# it: single-file ggml weights for the transformer and the Qwen3-VL text
# encoder plus the FLUX.2 autoencoder. The one-click profiles below name their
# files through the installer's own pins instead of this hand-written triplet.
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


# The native sdcpp profiles all run the same reviewed runner binary family:
# x86_64 Linux carries the accepted floor-tier build (the CPU prototype
# below also pins it), darwin-arm64 the reviewed Metal build, and Windows
# joins with its first accepted CUDA build. A pin stays absent until that
# platform's first-edit acceptance exists.
NATIVE_RUNNER_SHA256 = {
    "linux-x86_64": RUNNER_SHA256,
    "darwin-arm64": "3b4d9683b79ca8626288715fc9b7b07c047c4ebcb75a7e7e6cf38403b0a0d345",
    "windows-x86_64": None,
}

FLUX2_MODEL_ID = "flux2-klein-4b-native"


def native_device(system, machine):
    """The reviewed inference device per platform: Metal on Apple Silicon,
    CUDA on Windows once its build is accepted, CPU everywhere else."""
    if system == "Darwin" and machine == "arm64":
        return "metal"
    if system == "Windows":
        return "cuda"
    return "cpu"


def _package_files(package_id):
    """The installer's pinned file names for a reviewed package. The adapter
    and the catalogue keep describing the same reviewed files."""
    return tuple(file["name"] for file in model_install.PACKAGES[package_id]["files"])


def _native_parameters(recipe, maximum_resident_bytes):
    """Finish a frozen recipe into the request-level parameters record: the
    platform's inference device, its pinned runner build and the memory
    budget of this profile. A request recorded with these values runs with
    exactly them, except the sampling-steps override of the profile's bounds."""
    return {**recipe,
            "inferenceDevice": native_device(platform.system(), platform.machine()),
            "runnerSha256": NATIVE_RUNNER_SHA256.get(platform_key()),
            "maximumResidentBytes": maximum_resident_bytes}


def png_frame(data):
    # The coordinator validated the frame before submission; the worker re-checks
    # the header before the runner starts so a replaced file never loads.
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[8:16] != b"\x00\x00\x00\x0dIHDR":
        raise ValueError("invalidImage")
    return struct.unpack(">II", data[16:24])


def sampling_runner_command(runner, diffusion, llm, vae, job, width, height, parameters,
                            llm_vision=None, extra=()):
    """The one pinned sdcpp invocation every native profile shares: the model
    and encoder files by role, the recipe with the request's bounded sampling
    steps, the same frame contract, and a bounded live preview the runner
    decodes with the model's own VAE so the panel can show real progress
    instead of a silent wait. Output metadata stays off: generation
    provenance lives only in the private job record."""
    command = [str(runner), "--diffusion-model", diffusion, "--llm", llm]
    if llm_vision is not None:
        command += ["--llm-vision", llm_vision]
    command += ["--vae", vae,
                "-r", str(job / "reference.png"), "--prompt-file", str(job / "prompt.txt"),
                "-o", str(job / "result.png"),
                "--cfg-scale", str(parameters["cfgScale"]), "--steps", str(parameters["steps"]),
                "--sampling-method", parameters["samplingMethod"], "--seed", str(parameters["seed"]),
                "-W", str(width), "-H", str(height),
                "--backend", parameters["inferenceDevice"], "--offload-to-cpu",
                "--disable-image-metadata",
                "--preview", "vae", "--preview-path", str(job / "preview.png"),
                "--preview-interval", str(max(1, parameters["steps"] // 4)),
                "-v"]
    return command + list(extra)


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
    # The CPU floor-tier prototype accepts a bounded sampling-steps override
    # like every native profile; its frozen recipe stays the default.
    STEP_BOUNDS = (1, 32)
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
                  "networkListener": False, "automaticDownloads": False,
                  "stepsBounds": list(self.STEP_BOUNDS), "recipeSteps": self.parameters["steps"]}
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
    def runner_command(runner, model_path, job, width, height, parameters):
        """The pinned prototype invocation: this profile keeps its accepted
        hand-written file triplet; the shared builder adds the bounded live
        preview and this run's sampling-steps override."""
        p = SdcppBackend.PACKAGE_FILES
        return sampling_runner_command(runner, str(Path(model_path) / p[0]), str(Path(model_path) / p[1]),
                                       str(Path(model_path) / p[2]), job, width, height, parameters)


class SdcppPackageBackend(SdcppBackend):
    """A one-click installer package served on the per-platform pinned native
    runner. A concrete profile declares only its package identity, the role
    map for the installer's pinned files and the frozen sampling recipe from
    the runner's own model documentation; everything else — the pinned build,
    the platform gate, folder and receipt verification, the invocation, the
    bounded sampling-steps override, preview and progress — is shared here
    once and identical for every family member.

    The recipe is generation identity (ADR 0022): a run is recorded with
    exactly these values, on exactly the pinned runner build, except the
    number of sampling steps, which a request may lower or raise inside the
    profile's bounds. Acceptance stays per installation receipt: the check
    reports unvalidated until a real edit passed on this computer.
    """
    PACKAGE_ID = ""      # installer manifest key; a profile without one is a programming error
    # model_id/profile/identifier carry the house names every consumer reads:
    # the engine registry selects by profile, the receipts and model store
    # record by profile, and the worker validates by modelId and identifier.
    model_id = ""
    profile = ""
    identifier = ""
    # Role map: the installer's pinned file index behind each runner input.
    # FLUX families carry the transformer, one encoder and the VAE; the
    # Qwen and LongCat editing families add the encoder's vision projector.
    ROLES = {"llm": 1, "vae": 2}
    # The runner's documented recipe, frozen. referencePreset names the
    # reference-preparation family the invocation implements, so every
    # job's record states which editing contract produced it.
    RECIPE = {}
    EXTRA_RUNNER_FLAGS = ()
    STEP_BOUNDS = (1, 32)
    MAXIMUM_PIXELS = 1572864
    MAXIMUM_RESIDENT_BYTES = 10 * 1024**3
    MAXIMUM_EDIT_SECONDS = 4 * 60 * 60
    maximum_edit_seconds = MAXIMUM_EDIT_SECONDS
    PLATFORM_MISSING_MESSAGE = "This native model needs a reviewed runner for this computer: Apple Silicon Mac (Metal), Windows with NVIDIA CUDA, or the x86_64 Linux build. None is staged here."
    PACKAGE_MISSING_MESSAGE = ""

    @staticmethod
    def platform_ready():
        # Ready only with this platform's own reviewed build pinned and staged.
        return NATIVE_RUNNER_SHA256.get(platform_key()) is not None and runner_path().is_file()

    @classmethod
    def runner_pin(cls):
        return NATIVE_RUNNER_SHA256.get(platform_key())

    def check(self, model_directory=None):
        pin = self.runner_pin()
        result = {"backend": self.identifier, "modelId": self.model_id, "available": False,
                  "requiresReference": True, "consumedPasses": ["reference"],
                  "maximumPromptBytes": 2048, "promptOverflow": "reject", "frameGrid": 64,
                  "maximumPixels": type(self).MAXIMUM_PIXELS,
                  "supportsCancellation": "processExitConfirmed",
                  "networkListener": False, "automaticDownloads": False,
                  "stepsBounds": list(type(self).STEP_BOUNDS), "recipeSteps": self.parameters["steps"]}
        def unavailable(code, message):
            return dict(result, code=code, message=message)
        if pin is None:
            return unavailable("backendUnavailable", self.PLATFORM_MISSING_MESSAGE)
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
            return unavailable("missingLocalComponents", self.PACKAGE_MISSING_MESSAGE)
        result.update(available=True, acceptance="unvalidated",
                      message="Pinned runner and complete local package found. Model loading and hardware suitability still need a real edit test.")
        result["freeBytes"] = shutil.disk_usage(model_directory).free
        if self.installation:
            record = model_store.checked(self.installation, Path(model_directory), profile=self.profile)
            result.update(modelDigest=record["digest"], acceptance="nativeEditPassed" if
                          record.get("nativeAcceptance", {}).get("backend") == self.identifier else "unvalidated")
        return result

    @classmethod
    def runner_command(cls, runner, model_path, job, width, height, parameters):
        """The shared native invocation: every family member differs only in
        its files, its recipe and its documented extra flags."""
        files = {role: str(Path(model_path) / cls.PACKAGE_FILES[index]) for role, index in cls.ROLES.items()}
        return sampling_runner_command(runner, str(Path(model_path) / cls.PACKAGE_FILES[0]),
                                       files["llm"], files["vae"], job, width, height, parameters,
                                       llm_vision=files.get("llm_vision"), extra=cls.EXTRA_RUNNER_FLAGS)


class SdcppFlux2Backend(SdcppPackageBackend):
    """The native FLUX.2 klein 4B editing profile: the first reviewed one-click
    package on the per-platform pinned runner, distilled 4-step schedule, cfg
    1, euler. Its profile, identifier and receipt names are the shipped ones;
    the shared family carries its mechanics since 2026-11-23."""
    PACKAGE_ID = "flux2-klein-4b-gguf"
    model_id = FLUX2_MODEL_ID
    profile = "sdcpp-80c4707-flux2-klein-4b"
    identifier = "sdcpp-80c4707-flux2-native-1"
    RECIPE = {"seed": 1, "steps": 4, "cfgScale": 1.0, "samplingMethod": "euler", "referencePreset": "flux2"}
    STEP_BOUNDS = (1, 16)
    MAXIMUM_PIXELS = 1048576
    MAXIMUM_RESIDENT_BYTES = 7 * 1024**3
    PACKAGE_FILES = _package_files(PACKAGE_ID)
    parameters = _native_parameters(RECIPE, MAXIMUM_RESIDENT_BYTES)
    maximum_resident_bytes = MAXIMUM_RESIDENT_BYTES
    PLATFORM_MISSING_MESSAGE = "The FLUX.2 klein 4B native profile needs a reviewed runner for this computer: Apple Silicon Mac (Metal), Windows with NVIDIA CUDA, or the x86_64 Linux build. None is staged here."
    PACKAGE_MISSING_MESSAGE = "Choose the installed FLUX.2 klein 4B package: the transformer and encoder gguf files and the shipped decoder. Nothing was downloaded."


class SdcppFlux3KleinNative(SdcppPackageBackend):
    """FLUX.3 Image Klein: distilled 16-step schedule, cfg 1.0, euler — the
    runner's documented recipe for the newest FLUX whole-view model."""
    PACKAGE_ID = "flux3-image-klein-gguf"
    model_id = "flux3-image-klein-native"
    profile = "sdcpp-80c4707-flux3-image-klein"
    identifier = "sdcpp-80c4707-flux3-image-klein-native-1"
    RECIPE = {"seed": 1, "steps": 16, "cfgScale": 1.0, "samplingMethod": "euler", "referencePreset": "flux3"}
    STEP_BOUNDS = (1, 40)
    MAXIMUM_RESIDENT_BYTES = 10 * 1024**3
    PACKAGE_FILES = _package_files(PACKAGE_ID)
    parameters = _native_parameters(RECIPE, MAXIMUM_RESIDENT_BYTES)
    maximum_resident_bytes = MAXIMUM_RESIDENT_BYTES
    PLATFORM_MISSING_MESSAGE = "The FLUX.3 Image Klein native profile needs a reviewed runner for this computer: Apple Silicon Mac (Metal), Windows with NVIDIA CUDA, or the x86_64 Linux build. None is staged here."
    PACKAGE_MISSING_MESSAGE = "Choose the installed FLUX.3 Image Klein package: the quantised transformer and its Qwen3-VL encoder and the autoencoder. Nothing was downloaded."


class SdcppFlux3KleinBaseNative(SdcppPackageBackend):
    """FLUX.3 Image Klein Base: the undistilled variant's 20-step guided
    schedule, cfg 3.5, euler, per the runner documentation."""
    PACKAGE_ID = "flux3-image-klein-base-gguf"
    model_id = "flux3-image-klein-base-native"
    profile = "sdcpp-80c4707-flux3-image-klein-base"
    identifier = "sdcpp-80c4707-flux3-image-klein-base-native-1"
    RECIPE = {"seed": 1, "steps": 20, "cfgScale": 3.5, "samplingMethod": "euler", "referencePreset": "flux3"}
    STEP_BOUNDS = (4, 60)
    MAXIMUM_RESIDENT_BYTES = 10 * 1024**3
    PACKAGE_FILES = _package_files(PACKAGE_ID)
    parameters = _native_parameters(RECIPE, MAXIMUM_RESIDENT_BYTES)
    maximum_resident_bytes = MAXIMUM_RESIDENT_BYTES
    PLATFORM_MISSING_MESSAGE = "The FLUX.3 Image Klein Base native profile needs a reviewed runner for this computer: Apple Silicon Mac (Metal), Windows with NVIDIA CUDA, or the x86_64 Linux build. None is staged here."
    PACKAGE_MISSING_MESSAGE = "Choose the installed FLUX.3 Image Klein Base package: the quantised transformer and its Qwen3-VL encoder and the autoencoder. Nothing was downloaded."


class SdcppQwenEditNative(SdcppPackageBackend):
    """Qwen-Image-Edit-2511: the 20-step guided editing recipe with flow shift
    3 and the zero-cond-t conditioning the runner documents as required for
    the 2511 mode; the Qwen2.5-VL encoder passes its own vision projector as
    the mmproj file, so the model reads the reference with the weights the
    publisher shipped for exactly that."""
    PACKAGE_ID = "qwen-image-edit-2511-gguf"
    model_id = "qwen-image-edit-2511-native"
    profile = "sdcpp-80c4707-qwen-image-edit"
    identifier = "sdcpp-80c4707-qwen-image-edit-2511-native-1"
    ROLES = {"llm": 1, "llm_vision": 2, "vae": 3}
    RECIPE = {"seed": 1, "steps": 20, "cfgScale": 2.5, "samplingMethod": "euler", "referencePreset": "qwen_edit"}
    # Zero-cond-t keeps the 2511 edit honest; with it off the runner documents
    # visibly degraded edits, so it belongs to the frozen recipe.
    EXTRA_RUNNER_FLAGS = ("--flow-shift", "3", "--model-args", "qwen_image_zero_cond_t=true")
    STEP_BOUNDS = (4, 60)
    MAXIMUM_RESIDENT_BYTES = 22 * 1024**3
    PACKAGE_FILES = _package_files(PACKAGE_ID)
    parameters = _native_parameters(RECIPE, MAXIMUM_RESIDENT_BYTES)
    maximum_resident_bytes = MAXIMUM_RESIDENT_BYTES
    PLATFORM_MISSING_MESSAGE = "The Qwen-Image-Edit-2511 native profile needs a reviewed runner for this computer: Apple Silicon Mac (Metal), Windows with NVIDIA CUDA, or the x86_64 Linux build. None is staged here."
    PACKAGE_MISSING_MESSAGE = "Choose the installed Qwen-Image-Edit-2511 package: the quantised transformer, the Qwen2.5-VL encoder with its vision projector, and the Qwen VAE. Nothing was downloaded."


class SdcppLongcatEditNative(SdcppPackageBackend):
    """LongCat-Image-Edit-Turbo: the publisher's distilled 8-step edit
    recipe, cfg 1.0; the runner's LongCat documentation contributes the
    flow shift 3 and requires the encoder's mmproj vision file for edits."""
    PACKAGE_ID = "longcat-image-edit-gguf"
    model_id = "longcat-image-edit-native"
    profile = "sdcpp-80c4707-longcat-image-edit"
    identifier = "sdcpp-80c4707-longcat-image-edit-native-1"
    ROLES = {"llm": 1, "llm_vision": 2, "vae": 3}
    RECIPE = {"seed": 1, "steps": 8, "cfgScale": 1.0, "samplingMethod": "euler", "referencePreset": "longcat_edit"}
    EXTRA_RUNNER_FLAGS = ("--flow-shift", "3")
    STEP_BOUNDS = (1, 24)
    MAXIMUM_RESIDENT_BYTES = 12 * 1024**3
    PACKAGE_FILES = _package_files(PACKAGE_ID)
    parameters = _native_parameters(RECIPE, MAXIMUM_RESIDENT_BYTES)
    maximum_resident_bytes = MAXIMUM_RESIDENT_BYTES
    PLATFORM_MISSING_MESSAGE = "The LongCat-Image-Edit native profile needs a reviewed runner for this computer: Apple Silicon Mac (Metal), Windows with NVIDIA CUDA, or the x86_64 Linux build. None is staged here."
    PACKAGE_MISSING_MESSAGE = "Choose the installed LongCat-Image-Edit package: the quantised transformer, the Qwen2.5-VL encoder with its vision projector, and the autoencoder. Nothing was downloaded."


class SdcppFlux2Klein9bNative(SdcppPackageBackend):
    """FLUX.2 klein 9B: the licensed step up from klein 4B, the runner's own
    distilled 4-step schedule with the Qwen3-8B encoder this family documents."""
    PACKAGE_ID = "flux2-klein-9b-gguf"
    model_id = "flux2-klein-9b-native"
    profile = "sdcpp-80c4707-flux2-klein-9b"
    identifier = "sdcpp-80c4707-flux2-klein-9b-native-1"
    ROLES = {"llm": 1, "vae": 2}
    RECIPE = {"seed": 1, "steps": 4, "cfgScale": 1.0, "samplingMethod": "euler", "referencePreset": "flux2"}
    STEP_BOUNDS = (1, 16)
    MAXIMUM_PIXELS = 1572864
    MAXIMUM_RESIDENT_BYTES = 14 * 1024**3
    PACKAGE_FILES = _package_files(PACKAGE_ID)
    parameters = _native_parameters(RECIPE, MAXIMUM_RESIDENT_BYTES)
    maximum_resident_bytes = MAXIMUM_RESIDENT_BYTES
    PLATFORM_MISSING_MESSAGE = "The FLUX.2 klein 9B native profile needs a reviewed runner for this computer: Apple Silicon Mac (Metal), Windows with NVIDIA CUDA, or the x86_64 Linux build. None is staged here."
    PACKAGE_MISSING_MESSAGE = "Choose the installed FLUX.2 klein 9B package: the quantised transformer and its Qwen3-8B encoder and the autoencoder. Nothing was downloaded."


class SdcppFlux2DevNative(SdcppPackageBackend):
    """FLUX.2 dev: the high-memory comparison model with the Mistral-Small 3.2
    24B encoder the runner documents; the distilled-family schedule stays
    cfg 1.0 at the runner's documented 20 steps."""
    PACKAGE_ID = "flux2-dev-gguf"
    model_id = "flux2-dev-native"
    profile = "sdcpp-80c4707-flux2-dev"
    identifier = "sdcpp-80c4707-flux2-dev-native-1"
    ROLES = {"llm": 1, "vae": 2}
    RECIPE = {"seed": 1, "steps": 20, "cfgScale": 1.0, "samplingMethod": "euler", "referencePreset": "flux2"}
    STEP_BOUNDS = (4, 60)
    MAXIMUM_RESIDENT_BYTES = 40 * 1024**3
    PACKAGE_FILES = _package_files(PACKAGE_ID)
    parameters = _native_parameters(RECIPE, MAXIMUM_RESIDENT_BYTES)
    maximum_resident_bytes = MAXIMUM_RESIDENT_BYTES
    PLATFORM_MISSING_MESSAGE = "The FLUX.2 dev native profile needs a reviewed runner for this computer: Apple Silicon Mac (Metal), Windows with NVIDIA CUDA, or the x86_64 Linux build. None is staged here."
    PACKAGE_MISSING_MESSAGE = "Choose the installed FLUX.2 dev package: the quantised transformer and its Mistral 3.2 encoder and the autoencoder. Nothing was downloaded."


# The reviewed one-click families on the native runner. Order is the engine's
# registration order; every member claims a disjoint package layout.
PACKAGE_BACKENDS = (SdcppFlux2Backend, SdcppFlux3KleinNative, SdcppFlux3KleinBaseNative,
                    SdcppFlux2Klein9bNative, SdcppQwenEditNative, SdcppLongcatEditNative,
                    SdcppFlux2DevNative)


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
             "unsupportedRuntime", "installationChanged", "stepsOutsideRecipe"}
    return str(exc) if isinstance(exc, ValueError) and str(exc) in known else "workerFailed"


def recipe_for_request(backend_class, requested):
    """Generation identity with the one honest user knob. The profile's recipe
    is frozen; the request may only replace the number of sampling steps, and
    only inside the profile's bounds. The returned record is exactly what
    runs and what the job's evidence will state."""
    if not isinstance(requested, dict):
        raise ValueError("unsupportedRuntime")
    effective = dict(backend_class.parameters)
    steps = requested.get("steps", effective["steps"])
    low, high = backend_class.STEP_BOUNDS
    if isinstance(steps, bool) or not isinstance(steps, int) or not low <= steps <= high:
        raise ValueError("stepsOutsideRecipe")
    effective["steps"] = steps
    if any(requested.get(key) != value for key, value in effective.items()):
        raise ValueError("unsupportedRuntime")
    return effective


def run(job):
    progress(job, "checkingLocalFiles")
    request = json.loads((job / "request.json").read_text(encoding="utf-8"))
    backend_class = next((candidate for candidate in (SdcppBackend, *PACKAGE_BACKENDS)
                          if request.get("modelId") == candidate.model_id), None)
    if backend_class is None or request.get("backend") != backend_class.identifier:
        raise ValueError("unsupportedRuntime")
    recipe = recipe_for_request(backend_class, request.get("parameters"))
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
                                             backend_class.runner_command(backend.runner, model_path, job, width, height, recipe))
        # The "generating" phase starts with the runner's first sampling step;
        # everything before it (weight loading, buffer allocation) stays honest
        # under "loadingModel" in the waiting client's status line.
        replay_runner_output(job, log_file, process)
        process.wait()
        generation_seconds = time.monotonic() - started
        if process.returncode != 0:
            raise ValueError("workerFailed")
    finally:
        # The live preview served progress only; the result is the record, and
        # nothing half-decoded outlives the run it belonged to.
        (job / "preview.png").unlink(missing_ok=True)
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
    p = recipe
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
