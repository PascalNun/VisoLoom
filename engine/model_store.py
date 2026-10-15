"""Local verification receipts. This is an integrity cache, not publisher authentication."""

import hashlib
import json
import os
from pathlib import Path
import stat
import uuid

from common import EngineError

PROFILE = "mflux-0.22.0-klein-4b-q4"
MAX_RECEIPT_BYTES = 2 * 1024 * 1024


def entries(root):
    root = Path(root).absolute()
    if not root.is_dir() or any(p.is_symlink() for p in [root, *root.parents]):
        raise EngineError("invalidModel", "Choose a materialised local model directory.")
    result = []
    for path in sorted(root.rglob("*")):
        info = path.lstat()
        if stat.S_ISDIR(info.st_mode):
            continue
        if not stat.S_ISREG(info.st_mode) or path.suffix not in {".safetensors", ".json", ".txt", ".jinja", ".model", ".md"}:
            raise EngineError("invalidModel", "The model contains an unsupported file or symbolic link.")
        if len(result) >= 4096:
            raise EngineError("invalidModel", "The model exceeds its file limit.")
        identity = [info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns]
        result.append({"path": path.relative_to(root).as_posix(), "identity": identity})
    if not result:
        raise EngineError("invalidModel", "The model directory is empty.")
    return result


def digest(files):
    value = hashlib.sha256()
    for item in files:
        value.update(item["path"].encode() + b"\0" + bytes.fromhex(item["sha256"]))
    return value.hexdigest()


def write_receipt(path, record):
    path = Path(path).absolute()
    if any(p.is_symlink() for p in [path, *path.parents]):
        raise EngineError("invalidStorage", "Verification records must not use symbolic links.")
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    temporary = path.with_name("." + uuid.uuid4().hex + ".tmp")
    try:
        with os.fdopen(os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w", encoding="utf-8") as stream:
            json.dump(record, stream, allow_nan=False)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def verify(root, receipt):
    root = Path(root).absolute()
    files = entries(root)
    for item in files:
        value = hashlib.sha256()
        with (root / item["path"]).open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                value.update(chunk)
        item["sha256"] = value.hexdigest()
    if [{"path": f["path"], "identity": f["identity"]} for f in files] != entries(root):
        raise EngineError("modelChanged", "The model changed while being verified.")
    record = {"format": 1, "profile": PROFILE, "directory": str(root), "files": files, "digest": digest(files)}
    write_receipt(receipt, record)
    return record


def checked(receipt, root=None):
    receipt = Path(receipt).absolute()
    if not receipt.is_file() or any(p.is_symlink() for p in [receipt, *receipt.parents]):
        raise EngineError("installationChanged", "Verify the prepared model again before using it.")
    try:
        with receipt.open("rb") as stream:
            data = stream.read(MAX_RECEIPT_BYTES + 1)
        if len(data) > MAX_RECEIPT_BYTES:
            raise ValueError()
        record = json.loads(data)
        directory = Path(record["directory"])
        if (record["format"] != 1 or record["profile"] != PROFILE or not directory.is_absolute() or
                root is not None and directory != Path(root).absolute() or digest(record["files"]) != record["digest"] or
                [{"path": f["path"], "identity": f["identity"]} for f in record["files"]] != entries(directory)):
            raise ValueError()
        return record
    except (KeyError, TypeError, ValueError, OSError, EngineError):
        raise EngineError("installationChanged", "The prepared model changed. Verify it again; no image was generated.") from None


def accept(receipt, model_digest, evidence):
    record = checked(receipt)
    if record["digest"] != model_digest:
        raise EngineError("installationChanged", "The prepared model changed during the edit.")
    record["nativeAcceptance"] = {"backend": evidence["backend"], "width": evidence["width"], "height": evidence["height"]}
    write_receipt(receipt, record)
