"""Local engine prototype. No listener, downloads, Rhino imports or ML imports here."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import struct
import subprocess
import sys
import time
import uuid

sys.path.insert(0, str(Path(__file__).absolute().parent))
from mflux_backend import MfluxBackend
from common import EngineError, WorkerLock
import model_store

VERSION = "managed-engine-prototype-1"
MAX_IMAGE_BYTES = 16 * 1024 * 1024
MAX_PROMPT_BYTES = 2048
OBJECTIVE = (
    "Preserve the captured architecture and camera: geometry, proportions, openings, "
    "stairs, sections, object placement and composition. Change representation and "
    "appearance only. Geometric deviations are defects to reduce; generative output "
    "cannot guarantee exact preservation."
)
TERMINAL = {"completed", "failed", "cancelled"}


def private_directory(path):
    path = Path(path).absolute()
    if os.name == "nt" and str(path).startswith("\\\\"):
        raise EngineError("invalidStorage", "Private engine storage must be on this computer.")
    if any(p.is_symlink() for p in [path, *path.parents]):
        raise EngineError("invalidStorage", "Private storage must not use symbolic links.")
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    if os.name != "nt":
        path.chmod(0o700)
    return path


def write_json(path, value):
    path = Path(path)
    temporary = path.with_name("." + uuid.uuid4().hex + ".tmp")
    try:
        with os.fdopen(os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w", encoding="utf-8") as stream:
            json.dump(value, stream, ensure_ascii=False, allow_nan=False)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def read_bytes(path, maximum):
    path = Path(path)
    if path.is_symlink() or not path.is_file():
        raise EngineError("invalidFile", "Expected a regular local file.")
    with path.open("rb") as stream:
        data = stream.read(maximum + 1)
    if len(data) > maximum:
        raise EngineError("inputTooLarge", "The input exceeds its byte limit.")
    return data


def read_json(path):
    return json.loads(read_bytes(path, 65536).decode("utf-8"))


def png_size(data):
    # The backend must fully decode as well; this bounds the process before it starts.
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[8:16] != b"\x00\x00\x00\rIHDR":
        raise EngineError("invalidImage", "The reference must be a PNG image.")
    width, height = struct.unpack(">II", data[16:24])
    if (min(width, height) < 128 or max(width, height) > 1536 or
            width * height > 1572864 or width % 64 or height % 64):
        raise EngineError("unsupportedFrame", "Use a prepared frame divisible by 64, at most 1536 pixels per edge and 1.5 megapixels.")
    return width, height


def worker_environment(job):
    # Pass no cloud credentials or user-defined Python import path to the worker.
    allowed = {"PATH", "SYSTEMROOT", "WINDIR", "TMP", "TEMP", "LANG", "LC_ALL", "LOCALAPPDATA", "USERPROFILE"}
    env = {key: value for key, value in os.environ.items() if key.upper() in allowed}
    env.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1",
               HF_HOME=str(job / "cache"), DO_NOT_TRACK="1", TOKENIZERS_PARALLELISM="false")
    if os.environ.get("VISOLOOM_ENGINE_DIAGNOSTICS") == "1":
        env["VISOLOOM_ENGINE_DIAGNOSTICS"] = "1"
    return env


def stop_worker(process):
    if process.poll() is None:
        if os.name == "nt":
            process.kill()
        else:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
    process.wait(timeout=10)


def default_state_directory():
    if os.name == "nt":
        return Path(os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData/Local"))) / "VisoLoom/engine-prototype"
    return Path.home() / ".local/share/VisoLoom/engine-prototype"


class Engine:
    def __init__(self, root, backend=None, timeout_seconds=900):
        self.root = private_directory(root)
        self.backend = backend or MfluxBackend()
        self.timeout_seconds = timeout_seconds

    def job_directory(self, job_id):
        try:
            identifier = str(uuid.UUID(job_id))
        except (ValueError, AttributeError):
            raise EngineError("invalidJobId", "A UUID job identity is required.") from None
        return self.root / identifier

    def status(self, job_id):
        path = self.job_directory(job_id)
        if not (path / "state.json").is_file():
            raise EngineError("jobNotFound", "This engine has no record of that job.")
        record = read_json(path / "state.json")
        if record["state"] not in TERMINAL:
            record["cancelRequested"] = (path / "cancel.json").exists()
            if (path / "progress.json").is_file():
                record["phase"] = read_json(path / "progress.json")["phase"]
        return record

    def cancel(self, job_id):
        record = self.status(job_id)
        if record["state"] not in TERMINAL:
            write_json(self.job_directory(job_id) / "cancel.json", {"requested": True})
        # Requesting cancellation does not itself claim that inference has stopped.
        return self.status(job_id)

    def recover(self):
        # Called only with the OS worker lock held: no live coordinator is abandoned.
        with WorkerLock(self.root, ".inference.lock"):
            for path in self.root.iterdir():
                if path.is_dir() and not path.is_symlink() and (path / "state.json").is_file():
                    record = read_json(path / "state.json")
                    if record["state"] not in TERMINAL:
                        record.update(state="failed", failure="coordinatorStopped", updatedAt=time.time())
                        write_json(path / "state.json", record)

    def edit(self, job_id, reference_path, prompt_path, model_directory):
        job = self.job_directory(job_id)
        reference = read_bytes(reference_path, MAX_IMAGE_BYTES)
        width, height = png_size(reference)
        prompt = read_bytes(prompt_path, MAX_PROMPT_BYTES).decode("utf-8")
        if OBJECTIVE not in prompt or "\x00" in prompt:
            raise EngineError("invalidPrompt", "Keep the preservation objective in the prepared prompt.")
        model_path = Path(model_directory).resolve()
        if os.name == "nt" and str(model_path).startswith("\\\\"):
            raise EngineError("invalidModel", "This computer's model must be on a local disk.")
        request = {"version": VERSION, "modelId": self.backend.model_id, "backend": self.backend.identifier,
                   "modelDirectory": str(model_path), "prompt": prompt, "width": width, "height": height,
                   "parameters": self.backend.parameters}
        if getattr(self.backend, "installation", None):
            request["installation"] = str(self.backend.installation)
        source_hash = hashlib.sha256(reference).hexdigest()
        with WorkerLock(self.root):
            self.recover()
            # Hash the complete compatible model folder, not just its path/name.
            model_digest = self.backend.fingerprint(model_path)
            request["modelDigest"] = model_digest
            digest = hashlib.sha256(json.dumps(request, sort_keys=True).encode() + reference).hexdigest()
            if job.exists():
                record = self.status(job_id)
                if record["requestDigest"] != digest:
                    raise EngineError("idempotencyConflict", "This job identity already belongs to different content.")
                return record
            check = self.backend.check(model_path)
            if not check["available"]:
                raise EngineError(check["code"], check["message"])
            private_directory(job)
            record = {"jobId": str(uuid.UUID(job_id)), "requestDigest": digest, "state": "running",
                      "createdAt": time.time(), "updatedAt": time.time(), "cancelRequested": False,
                      "sourceSha256": source_hash, "modelDigest": model_digest, "modelId": self.backend.model_id,
                      "backend": self.backend.identifier, "width": width, "height": height}
            write_json(job / "request.json", request)
            with os.fdopen(os.open(job / "reference.png", os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "wb") as stream:
                stream.write(reference)
            write_json(job / "state.json", record)
            process = None
            try:
                process = subprocess.Popen(self.backend.command(job), cwd=job, env=worker_environment(job),
                                           stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                           start_new_session=os.name != "nt")
                deadline = time.monotonic() + self.timeout_seconds
                next_memory_check = time.monotonic()
                while process.poll() is None:
                    if (job / "cancel.json").exists() or time.monotonic() >= deadline:
                        stop_worker(process)
                        record.update(state="cancelled" if (job / "cancel.json").exists() else "failed",
                                      cancelRequested=(job / "cancel.json").exists(), failure="timeout" if time.monotonic() >= deadline else None)
                        break
                    if time.monotonic() >= next_memory_check and hasattr(self.backend, "resident_bytes"):
                        resident = self.backend.resident_bytes(process.pid)
                        next_memory_check = time.monotonic() + 1
                        if resident > self.backend.maximum_resident_bytes:
                            stop_worker(process)
                            record.update(state="failed", failure="memoryBudget", observedResidentBytes=resident)
                            break
                    time.sleep(0.05)
                else:
                    if (job / "cancel.json").exists():
                        record.update(state="cancelled", cancelRequested=True)
                    elif process.returncode != 0:
                        record.update(state="failed", failure="workerFailed")
                        if (job / "failure.json").is_file():
                            failure = read_json(job / "failure.json").get("code")
                            if failure in {"promptTooLong", "invalidImage", "missingLocalComponents", "runtimeMismatch", "unsupportedRuntime", "gpuTimeout", "installationChanged", "engineBusy"}:
                                record["failure"] = failure
                    else:
                        output = read_bytes(job / "result.png", MAX_IMAGE_BYTES)
                        if png_size(output) != (width, height):
                            raise EngineError("invalidResult", "The engine returned a different frame.")
                        evidence = read_json(job / "result.json")
                        output_hash = hashlib.sha256(output).hexdigest()
                        if evidence.get("sourceSha256") != source_hash or evidence.get("outputSha256") != output_hash:
                            raise EngineError("invalidResult", "The result does not match this job.")
                        if getattr(self.backend, "installation", None):
                            model_store.accept(self.backend.installation, model_digest,
                                               {"backend": self.backend.identifier, "width": width, "height": height})
                        record.update(state="completed", outputSha256=output_hash, outputBytes=len(output),
                                      consumedPasses=["reference"], result=evidence,
                                      warnings=["Compare geometry and materials with the reference; preservation is an objective."])
            except KeyboardInterrupt:
                if process is not None:
                    stop_worker(process)
                record.update(state="cancelled", cancelRequested=True)
            except Exception:
                if process is not None:
                    stop_worker(process)
                record.update(state="failed", failure="workerFailed")
            finally:
                if record["state"] != "completed":
                    (job / "result.png").unlink(missing_ok=True)
                    (job / "result.json").unlink(missing_ok=True)
                record["updatedAt"] = time.time()
                write_json(job / "state.json", record)
            return record


def main():
    parser = argparse.ArgumentParser(description="VisoLoom managed-engine prototype (developer tool)")
    parser.add_argument("action", choices=["check", "edit", "status", "cancel", "recover", "verify-model"])
    parser.add_argument("--model-directory", type=Path)
    parser.add_argument("--installation", type=Path)
    parser.add_argument("--state-directory", type=Path, default=default_state_directory())
    parser.add_argument("--job-id")
    parser.add_argument("--reference", type=Path)
    parser.add_argument("--prompt", type=Path)
    args = parser.parse_args()
    try:
        backend = MfluxBackend(args.installation)
        if args.action == "verify-model":
            if not args.installation or not args.model_directory:
                parser.error("verify-model requires --installation and --model-directory")
            record = model_store.verify(args.model_directory, args.installation)
            result = {"verified": True, "modelDigest": record["digest"], "acceptance": "unvalidated"}
        elif args.action == "check":
            result = backend.check(args.model_directory)
        else:
            engine = Engine(args.state_directory, backend)
            if args.action in {"status", "cancel"}:
                result = getattr(engine, args.action)(args.job_id)
            elif args.action == "recover":
                with WorkerLock(engine.root):
                    engine.recover()
                result = {"recovered": True}
            else:
                if not all([args.job_id, args.reference, args.prompt, args.model_directory]):
                    parser.error("edit requires --job-id, --reference, --prompt and --model-directory")
                result = engine.edit(args.job_id, args.reference, args.prompt, args.model_directory)
        print(json.dumps(result, ensure_ascii=False))
        return 1 if result.get("state") in {"failed", "cancelled"} or result.get("available") is False else 0
    except EngineError as exc:
        print(json.dumps({"code": exc.code, "message": exc.message}))
        return 1
    except (OSError, ValueError, TypeError):
        print(json.dumps({"code": "invalidInput", "message": "Check the local files and private storage."}))
        return 1


if __name__ == "__main__":
    sys.exit(main())
