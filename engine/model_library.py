"""Explicit local-folder library. Inspection never imports ML or reads weights."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import sys

sys.path.insert(0, str(Path(__file__).absolute().parent))
from common import EngineError, WorkerLock
from engine import default_state_directory, private_directory, write_json

MAX_ENTRIES = 32
MAX_NODES = 4096
MAX_JSON = 1024 * 1024
ALLOWED = {".safetensors", ".json", ".txt", ".jinja", ".model", ".md"}


def local_path(value):
    path = Path(value).absolute()
    if str(path).startswith("\\\\"):
        raise EngineError("invalidFolder", "Choose a folder on a local disk, rather than a network share.")
    for item in [path, *path.parents]:
        if item.exists() or item.is_symlink():
            info = item.lstat()
            if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                raise EngineError("linkedFolder", "Choose a materialised model folder. Linked app caches need a separate import step.")
    return Path(os.path.abspath(path))


def read_json(path):
    path = local_path(path)
    if not path.is_file() or path.stat().st_size > MAX_JSON:
        raise EngineError("invalidRecord", "The local model record is missing or too large.")
    with path.open("rb") as stream:
        data = stream.read(MAX_JSON + 1)
    if len(data) > MAX_JSON:
        raise EngineError("invalidRecord", "The local model record is too large.")
    return json.loads(data)


def identifier(path):
    return hashlib.sha256(os.path.normcase(str(path)).encode("utf-8")).hexdigest()[:24]


def inspect(value):
    path = local_path(value)
    report = {"id": identifier(path), "name": path.name[:120], "directory": str(path),
              "status": "incomplete", "candidate": False, "fileCount": 0, "bytes": 0,
              "message": "Choose a complete model folder, including its image model, encoders and tokenizer."}
    if not path.is_dir():
        return dict(report, status="missing", message="This folder is unavailable. Reconnect its drive or choose another folder.")
    files = set()
    pending = [path]
    nodes = 0
    unsupported = False
    checkpoint = False
    while pending:
        with os.scandir(pending.pop()) as entries:
            for item in entries:
                nodes += 1
                if nodes > MAX_NODES:
                    raise EngineError("folderTooLarge", "Choose the model's own folder, rather than a whole drive or app library.")
                info = item.stat(follow_symlinks=False)
                if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                    raise EngineError("linkedFolder", "This folder contains links. Linked app caches need a separate import step.")
                if stat.S_ISDIR(info.st_mode):
                    pending.append(Path(item.path))
                elif stat.S_ISREG(info.st_mode):
                    file = Path(item.path)
                    checkpoint |= file.suffix.lower() == ".ckpt"
                    unsupported |= file.suffix not in ALLOWED
                    files.add(file.relative_to(path).as_posix())
                    report["fileCount"] += 1
                    report["bytes"] += info.st_size
                else:
                    unsupported = True
    if checkpoint:
        return dict(report, status="appCheckpoint", message="This folder contains app checkpoints (.ckpt), such as Draw Things models. VisoLoom cannot load them directly. An app connection or a compatible model copy is needed; no conversion was started.")
    if unsupported:
        return dict(report, status="unsupportedFiles", message="This folder contains files outside the supported model layout. Choose the model's own folder. No file was loaded or changed.")
    missing = [part for part in ["transformer", "text_encoder", "vae"]
               if not any(f.startswith(part + "/") and f.count("/") == 1 and f.endswith(".safetensors") for f in files)]
    if "tokenizer/tokenizer_config.json" not in files:
        missing.append("tokenizer")
    if missing:
        return dict(report, message="Missing local components: " + ", ".join(missing) + ". Nothing was downloaded.")
    # Metadata is a hint, not proof of tensor compatibility or publisher identity.
    if "transformer/config.json" in files:
        config = read_json(path / "transformer/config.json")
        expected = {"_class_name": "Flux2Transformer2DModel", "num_layers": 5,
                    "num_single_layers": 20, "num_attention_heads": 24, "attention_head_dim": 128}
        if not isinstance(config, dict) or any(config.get(key) != value for key, value in expected.items()):
            return dict(report, status="differentModel", message="The model metadata does not match the current FLUX.2 klein 4B adapter. Other model adapters are planned.")
        layout = "Diffusers FLUX.2 klein 4B layout"
    elif "transformer/model.safetensors.index.json" in files:
        index = read_json(path / "transformer/model.safetensors.index.json")
        if not isinstance(index, dict) or not isinstance(index.get("metadata"), dict) or index["metadata"].get("mflux_version") != "0.22.0":
            return dict(report, status="unknownLayout", message="The folder has model components, but its format needs review before reuse.")
        layout = "MFLUX 0.22.0 layout"
    else:
        return dict(report, status="unknownLayout", message="The folder has model components, but its format needs review before reuse.")
    return dict(report, status="candidate", candidate=True, layout=layout,
                message=layout + " found. This folder still needs verification, any required preparation and a real edit test. Saving it does not switch the rendering model.")


class Library:
    def __init__(self, path):
        self.path = local_path(path)

    def records(self):
        if not self.path.exists():
            return []
        value = read_json(self.path)
        if not isinstance(value, dict) or value.get("format") != 1 or not isinstance(value.get("folders"), list) or len(value["folders"]) > MAX_ENTRIES:
            raise EngineError("invalidLibrary", "The saved model library needs review. It has not been replaced.")
        folders = value["folders"]
        if any(not isinstance(folder, str) or not Path(folder).is_absolute() for folder in folders):
            raise EngineError("invalidLibrary", "The saved model library contains an invalid folder.")
        return folders

    def add(self, directory):
        report = inspect(directory)
        if not report["candidate"]:
            raise EngineError(report["status"], report["message"])
        private_directory(self.path.parent)
        with WorkerLock(self.path.parent, ".library.lock"):
            folders = self.records()
            if not any(identifier(Path(f)) == report["id"] for f in folders):
                if len(folders) >= MAX_ENTRIES:
                    raise EngineError("libraryFull", "The library holds up to 32 folders. Remove an unused entry first.")
                folders.append(report["directory"])
            write_json(self.path, {"format": 1, "folders": folders})
        return report

    def remove(self, entry_id):
        private_directory(self.path.parent)
        with WorkerLock(self.path.parent, ".library.lock"):
            folders = self.records()
            write_json(self.path, {"format": 1, "folders": [f for f in folders if identifier(Path(f)) != entry_id]})
        return {"removed": True}  # Model files and verification records are never deleted.

    def list(self, configured=None, installation=None):
        folders = self.records()
        configured_path = str(local_path(configured)) if configured else None
        configured_id = identifier(Path(configured_path)) if configured_path else None
        if configured_path and not any(identifier(Path(f)) == configured_id for f in folders):
            folders = [configured_path, *folders]
        reports = []
        for folder in folders:
            try:
                report = inspect(folder)
            except (EngineError, OSError, ValueError) as exc:
                report = {"id": identifier(Path(folder)), "name": Path(folder).name[:120], "directory": folder,
                          "status": "unavailable", "candidate": False, "fileCount": 0, "bytes": 0,
                          "message": exc.message if isinstance(exc, EngineError) else "Could not inspect this folder. Check the drive and model files."}
            report["configured"] = report["id"] == configured_id
            if report["configured"] and installation:
                try:
                    # Uses package metadata and file identities only, never imports ML
                    # or starts a generation worker. Acceptance belongs to the receipt.
                    from mflux_backend import MfluxBackend
                    check = MfluxBackend(local_path(installation)).check(Path(folder))
                    report["readiness"] = "ready" if check.get("available") and check.get("acceptance") == "nativeEditPassed" else "notReady"
                    report["readinessMessage"] = ("The configured model passed a real edit for this engine profile." if report["readiness"] == "ready" else
                                                  "The configured model needs runtime, verification or edit checks before rendering.")
                except (EngineError, OSError, ValueError):
                    report["readiness"] = "notReady"
                    report["readinessMessage"] = "The configured model changed or is unavailable. Check it again before rendering."
            reports.append(report)
        return {"models": reports}

    def plan(self, entry_id, configured=None, installation=None):
        """Explain the next setup step. Never hash/load weights or write records.

        Selection uses a saved opaque ID, not a new path supplied by a network job.
        Only the configured folder can use the configured acceptance receipt.
        """
        reports = self.list(configured, installation)["models"]
        report = next((item for item in reports if item["id"] == entry_id), None)
        if report is None:
            raise EngineError("entryNotFound", "This model folder is no longer in the library. Refresh the list first.")
        steps = []
        def step(name, state, message):
            steps.append({"name": name, "state": state, "message": message})
        result = {"id": entry_id, "steps": steps, "modelAccepted": False,
                  "minimumPreparationFreeBytes": 10 * 1024**3,
                  "estimatedPreparedBytes": 4_620_000_000, "sourceBytes": report["bytes"]}
        def finish(state, message):
            return dict(result, state=state, message=message)
        if not report["candidate"]:
            step("Model folder", "needsAttention", report["message"])
            return finish("folderNeeded", "Choose a compatible complete folder; no files have been loaded or changed.")
        step("Model folder", "found", "Expected components found. Source identity and actual model loading are checked separately.")
        from mflux_backend import MfluxBackend
        runtime = MfluxBackend().check(Path(report["directory"]))
        if not runtime.get("available"):
            step("Local runtime", "needsAttention", runtime["message"])
            return finish("runtimeNeeded", "The local runtime must be available before preparation or an edit test. This check installs nothing.")
        step("Local runtime", "found", "MFLUX 0.22.0 is installed.")
        if report["configured"] and installation:
            if report.get("readiness") == "ready":
                step("Verified copy", "passed", "The receipt still matches these files.")
                step("Image-edit test", "passed", "This copy passed a real edit for the current engine profile.")
                step("Rhino connection", "checkRequired", "Return to Settings and use Check local model. The running service must advertise this exact copy before it can be selected.")
                result["modelAccepted"] = True
                return finish("connectionCheckNeeded", "Model files accepted. Check the live connection in Settings; this report does not switch the rendering model.")
            # Distinguish stale integrity records from an intact but untested copy.
            import model_store
            try:
                model_store.checked(local_path(installation), Path(report["directory"]))
            except (EngineError, OSError, ValueError):
                step("Verified copy", "needsAttention", "The receipt is missing or no longer matches. Re-verification and another image-edit test are required.")
                return finish("verificationNeeded", "Repair or re-verify the prepared development copy before using it. The current connection was not changed.")
            step("Verified copy", "passed", "The receipt still matches these files.")
            step("Image-edit test", "checkRequired", "This copy has not passed an edit for the current engine profile.")
            return finish("editTestNeeded", "Run the explicit development image-edit acceptance test. No image was generated by this check.")
        step("Verified copy", "checkRequired", "A saved folder cannot inherit another copy's verification or edit acceptance.")
        # Report space on the intended managed-copy volume, not the source drive.
        storage = local_path(default_state_directory().parent / "models")
        existing = storage
        while not existing.exists():
            existing = existing.parent
        result["preparationFreeBytes"] = shutil.disk_usage(existing).free
        if report.get("layout", "").startswith("Diffusers"):
            step("Preparation", "checkRequired", "Verify the source revision and terms, then prepare a separate local copy. The original folder is retained.")
            enough = result["preparationFreeBytes"] >= result["minimumPreparationFreeBytes"]
            return finish("preparationNeeded" if enough else "spaceNeeded",
                          "Source verification, preparation and an image-edit test remain development steps. Nothing was copied or activated." if enough else
                          "The managed-copy volume has less than 10 GiB free. Free space before preparing a separate copy; nothing was copied or activated.")
        step("Image-edit test", "checkRequired", "Verify this copy and test the current engine profile before configuring it. The folder's metadata alone cannot establish compatibility.")
        return finish("verificationNeeded", "This existing copy needs its own verification and image-edit test. No model was activated.")


def main():
    parser = argparse.ArgumentParser(description="Inspect and remember local model folders; no downloads or inference")
    parser.add_argument("action", choices=["inspect", "add", "list", "remove", "plan"])
    parser.add_argument("--directory", type=Path)
    parser.add_argument("--id")
    parser.add_argument("--connection", type=Path)
    parser.add_argument("--library", type=Path, default=default_state_directory() / "models.json")
    args = parser.parse_args()
    try:
        library = Library(args.library)
        if args.action in {"inspect", "add"}:
            if not args.directory:
                parser.error("--directory is required")
            result = inspect(args.directory) if args.action == "inspect" else library.add(args.directory)
        elif args.action == "remove":
            if not args.id:
                parser.error("--id is required")
            result = library.remove(args.id)
        elif args.action == "plan":
            if not args.id:
                parser.error("--id is required")
            config = read_json(args.connection) if args.connection else {}
            result = library.plan(args.id, config.get("modelDirectory"), config.get("installation"))
        else:
            config = read_json(args.connection) if args.connection else {}
            result = library.list(config.get("modelDirectory"), config.get("installation"))
        print(json.dumps(result, ensure_ascii=False))
        return 0
    except EngineError as exc:
        print(json.dumps({"code": exc.code, "message": exc.message}))
        return 1
    except (OSError, ValueError, TypeError, AttributeError):
        print(json.dumps({"code": "invalidInput", "message": "Could not inspect the local model library. Check the selected folder and private records."}))
        return 1


if __name__ == "__main__":
    sys.exit(main())
