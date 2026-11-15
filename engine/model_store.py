"""Local verification receipts. This is an integrity cache, not publisher authentication."""

import hashlib
import json
import os
from pathlib import Path
import stat
import uuid

from common import EngineError

# The default profile is the first reviewed adapter. A receipt written by
# another backend carries that backend's profile; every function taking a
# receipt states which profile it expects, so cross-backend reuse fails closed.
PROFILE = "mflux-0.22.0-klein-4b-q4"
MAX_RECEIPT_BYTES = 2 * 1024 * 1024


def change_time(path, info):
    if os.name != "nt":
        return info.st_ctime_ns
    # Python's Windows ctime is creation time, not the NTFS change timestamp.
    # Use FILE_BASIC_INFO; never accept a weak mtime-only verification cache.
    import ctypes
    from ctypes import wintypes
    import msvcrt
    class BasicInfo(ctypes.Structure):
        _fields_ = [(name, ctypes.c_longlong) for name in ["created", "accessed", "written", "changed"]] + [("attributes", wintypes.DWORD)]
    query = ctypes.WinDLL("kernel32", use_last_error=True).GetFileInformationByHandleEx
    query.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
    query.restype = wintypes.BOOL
    fd = os.open(path, os.O_RDONLY | os.O_BINARY)
    try:
        basic = BasicInfo()
        if not query(msvcrt.get_osfhandle(fd), 0, ctypes.byref(basic), ctypes.sizeof(basic)) or basic.changed <= 0:
            raise EngineError("cacheUnsupported", "This filesystem cannot supply a reliable model change timestamp.")
        return basic.changed
    finally:
        os.close(fd)


def entries(root):
    root = Path(root).absolute()
    if not root.is_dir() or any(p.is_symlink() for p in [root, *root.parents]):
        raise EngineError("invalidModel", "Choose a materialised local model directory.")
    result = []
    for path in sorted(root.rglob("*")):
        info = path.lstat()
        if getattr(info, "st_file_attributes", 0) & 0x400:
            raise EngineError("invalidModel", "Model files must not use Windows reparse points.")
        if stat.S_ISDIR(info.st_mode):
            continue
        if not stat.S_ISREG(info.st_mode) or path.suffix not in {".safetensors", ".gguf", ".json", ".txt", ".jinja", ".model", ".md"}:
            raise EngineError("invalidModel", "The model contains an unsupported file or symbolic link.")
        if len(result) >= 4096:
            raise EngineError("invalidModel", "The model exceeds its file limit.")
        identity = [info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, change_time(path, info)]
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


def profile_of(receipt):
    """Read the backend profile a receipt was written for, without validating its files."""
    receipt = Path(receipt).absolute()
    if not receipt.is_file() or any(p.is_symlink() for p in [receipt, *receipt.parents]):
        raise EngineError("installationChanged", "Verify the prepared model again before using it.")
    try:
        with receipt.open("rb") as stream:
            data = stream.read(MAX_RECEIPT_BYTES + 1)
        if len(data) > MAX_RECEIPT_BYTES:
            raise ValueError()
        profile = json.loads(data)["profile"]
        if not isinstance(profile, str) or not profile or len(profile) > 120 or profile != profile.strip():
            raise ValueError()
        return profile
    except (KeyError, TypeError, ValueError, OSError):
        raise EngineError("installationChanged", "The prepared model changed. Verify it again; no image was generated.") from None


def verify(root, receipt, profile=PROFILE):
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
    record = {"format": 1, "profile": profile, "directory": str(root), "files": files, "digest": digest(files)}
    write_receipt(receipt, record)
    return record


def checked(receipt, root=None, profile=PROFILE):
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
        if (record["format"] != 1 or record["profile"] != profile or not directory.is_absolute() or
                root is not None and directory != Path(root).absolute() or digest(record["files"]) != record["digest"] or
                [{"path": f["path"], "identity": f["identity"]} for f in record["files"]] != entries(directory)):
            raise ValueError()
        return record
    except (KeyError, TypeError, ValueError, OSError, EngineError):
        raise EngineError("installationChanged", "The prepared model changed. Verify it again; no image was generated.") from None


def accept(receipt, model_digest, evidence, profile=PROFILE):
    record = checked(receipt, profile=profile)
    if record["digest"] != model_digest:
        raise EngineError("installationChanged", "The prepared model changed during the edit.")
    record["nativeAcceptance"] = {"backend": evidence["backend"], "width": evidence["width"], "height": evidence["height"]}
    write_receipt(receipt, record)
