"""Reviewed model package installer. Pinned HTTPS sources only; nothing is
registered until every file matches its pinned size and SHA-256. A partial or
mismatched download never reaches the destination folder and is never
registered: this module extends the existing receipt machinery, it does not
add a new verification concept. An installed package is still not renderable
until the local runner's own readiness check accepts it."""

import argparse
import hashlib
import json
import os
import shutil
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

# Isolated mode (-I) excludes the script's own directory; restore it so the
# engine modules import each other exactly as their documented CLI requires.
sys.path.insert(0, str(Path(__file__).absolute().parent))
from common import EngineError, default_state_directory
import model_store

ALLOWED_HOSTS = ("huggingface.co",)
SPACE_SLACK_BYTES = 512 * 1024 * 1024
READ_CHUNK = 1024 * 1024
URL_TIMEOUT = 60.0
MAX_LEDGER_INSTALLS = 32
STAGING_MARKER = ".visoloom-staging"

# The first reviewed download package, from ADR 0022's native-runner fixture.
# Sizes and x-linked-etag SHA-256 headers were captured anonymously from each
# pinned HTTPS source on 2026-11-21. The leejet Qwen3 mirror at this exact
# commit now refuses anonymous resolve (401); the official Qwen repository
# serves the identical file (same commit, same linked size and etag), so the
# encoder pin uses the official, revision-pinned path.
PACKAGES = {
    "flux2-klein-4b-gguf": {
        "id": "flux2-klein-4b-gguf",
        "display": "FLUX.2 klein 4B (ggml, quantised)",
        "profile": "sdcpp-80c4707-flux2-klein-4b",
        "purpose": "First recommended local editing model: sketch and photorealism on the bundled runner.",
        "terms": {
            "licence": "Apache-2.0",
            "files": [
                "https://huggingface.co/leejet/FLUX.2-klein-4B-GGUF/blob/3b1f5a9dc3abb32238b053aeb3d823c30afdacbd/flux-2-klein-4b-Q4_0.gguf",
                "https://huggingface.co/Qwen/Qwen3-4B-GGUF/blob/bc640142c66e1fdd12af0bd68f40445458f3869b/Qwen3-4B-Q4_K_M.gguf",
                "https://huggingface.co/black-forest-labs/FLUX.2-small-decoder/blob/a3efc24f613ef42d9428af62fdbd6f5fd8856c4a/full_encoder_small_decoder.safetensors",
            ],
        },
        "headersChecked": "2026-11-21",
        "files": [
            {
                "name": "flux-2-klein-4b-Q4_0.gguf",
                "url": "https://huggingface.co/leejet/FLUX.2-klein-4B-GGUF/resolve/3b1f5a9dc3abb32238b053aeb3d823c30afdacbd/flux-2-klein-4b-Q4_0.gguf",
                "bytes": 2460378560,
                "sha256": "d1023499ef3f2f82ff7c50e6778495195c1b6cc34835741778868428111f9ff4",
            },
            {
                "name": "Qwen3-4B-Q4_K_M.gguf",
                "url": "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/bc640142c66e1fdd12af0bd68f40445458f3869b/Qwen3-4B-Q4_K_M.gguf",
                "bytes": 2497280256,
                "sha256": "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5",
            },
            {
                "name": "full_encoder_small_decoder.safetensors",
                "url": "https://huggingface.co/black-forest-labs/FLUX.2-small-decoder/resolve/a3efc24f613ef42d9428af62fdbd6f5fd8856c4a/full_encoder_small_decoder.safetensors",
                "bytes": 249519092,
                "sha256": "ea4273f02d1fafbf8e1d1c2cf6018ed8748652eb0bf34f2dd91171f16f15ab62",
            },
        ],
    },
    # The second reviewed download package: the Qwen Image Edit 2511 recipe
    # from the runner's documented qwen_image_edit guide. Sizes and
    # x-linked-etag SHA-256 headers were captured anonymously from each pinned
    # HTTPS source on 2026-11-23. The engine has no reviewed adapter for this
    # profile yet; an installation stays non-renderable until that recipe
    # ships and passes a real edit, which the engine reports as-is.
    "qwen-image-edit-2511-gguf": {
        "id": "qwen-image-edit-2511-gguf",
        "display": "Qwen Image Edit 2511 (ggml, quantised)",
        "profile": "sdcpp-80c4707-qwen-image-edit",
        "purpose": "First larger non-FLUX comparison: carries assigned materials and architectural detail into whole-view edits.",
        "terms": {
            "licence": "Apache-2.0",
            "files": [
                "https://huggingface.co/unsloth/Qwen-Image-Edit-2511-GGUF/blob/52838251db242ddd8e8d0118de36c71006d7c397/qwen-image-edit-2511-Q4_K_M.gguf",
                "https://huggingface.co/mradermacher/Qwen2.5-VL-7B-Instruct-GGUF/blob/cfa2baa09946b211c107e6e104948987a64dd2c1/Qwen2.5-VL-7B-Instruct.Q4_K_M.gguf",
                "https://huggingface.co/mradermacher/Qwen2.5-VL-7B-Instruct-GGUF/blob/cfa2baa09946b211c107e6e104948987a64dd2c1/Qwen2.5-VL-7B-Instruct.mmproj-Q8_0.gguf",
                "https://huggingface.co/Comfy-Org/Qwen-Image/blob/1f12b17be14c89b026c51a91d67c32f84bb047bc/split_files/vae/qwen_image_vae.safetensors",
            ],
        },
        "headersChecked": "2026-11-23",
        "files": [
            {
                "name": "qwen-image-edit-2511-Q4_K_M.gguf",
                "url": "https://huggingface.co/unsloth/Qwen-Image-Edit-2511-GGUF/resolve/52838251db242ddd8e8d0118de36c71006d7c397/qwen-image-edit-2511-Q4_K_M.gguf",
                "bytes": 12842830432,
                "sha256": "07951f61941a81e236cbd4a9b6f250d49626b3ba518f260135bf2e01a87cf1c8",
            },
            {
                "name": "Qwen2.5-VL-7B-Instruct.Q4_K_M.gguf",
                "url": "https://huggingface.co/mradermacher/Qwen2.5-VL-7B-Instruct-GGUF/resolve/cfa2baa09946b211c107e6e104948987a64dd2c1/Qwen2.5-VL-7B-Instruct.Q4_K_M.gguf",
                "bytes": 4683072512,
                "sha256": "0f00a930ba3108b6861ddadf74d8ebbd82e257c63eba728e62c3e8970f5eed94",
            },
            {
                "name": "Qwen2.5-VL-7B-Instruct.mmproj-Q8_0.gguf",
                "url": "https://huggingface.co/mradermacher/Qwen2.5-VL-7B-Instruct-GGUF/resolve/cfa2baa09946b211c107e6e104948987a64dd2c1/Qwen2.5-VL-7B-Instruct.mmproj-Q8_0.gguf",
                "bytes": 853119712,
                "sha256": "2185ca0adf339f96845318c7b666f5d242f86bbace5657a89cb834785d0cea57",
            },
            {
                "name": "qwen_image_vae.safetensors",
                "url": "https://huggingface.co/Comfy-Org/Qwen-Image/resolve/1f12b17be14c89b026c51a91d67c32f84bb047bc/split_files/vae/qwen_image_vae.safetensors",
                "bytes": 253806246,
                "sha256": "a70580f0213e67967ee9c95f05bb400e8fb08307e017a924bf3441223e023d1f",
            },
        ],
    }
}


def package(package_id):
    """Copy a reviewed package definition or fail closed on an unknown id."""
    record = PACKAGES.get(package_id)
    if record is None:
        raise EngineError("packageUnknown", "This VisoLoom catalogue does not contain that model package.")
    return {**record, "files": [dict(file) for file in record["files"]]}


def packages():
    """Public summary rows; never the download pins themselves."""
    return [
        {
            "id": record["id"],
            "display": record["display"],
            "purpose": record["purpose"],
            "terms": record["terms"]["licence"],
            "profile": record["profile"],
            "totalBytes": sum(file["bytes"] for file in record["files"]),
            "fileCount": len(record["files"]),
        }
        for record in PACKAGES.values()
    ]


def _symlink_free(value):
    value = Path(value).absolute()
    if any(path.is_symlink() for path in [value, *value.parents]):
        raise EngineError("invalidStorage", "Model installation must not use symbolic links.")


def _validate_source(url, allow_hosts):
    parsed = urllib.parse.urlsplit(url)
    host = (parsed.hostname or "").lower()
    # Loopback over plain http is the documented local-fixture seam for
    # tests; every shipped URL is https on an allowed public host.
    loopback = host in {"127.0.0.1", "::1", "localhost"}
    reviewed_https = parsed.scheme == "https" and not loopback and host in allow_hosts
    local_https_or_http = loopback and parsed.scheme in {"http", "https"}
    if not (reviewed_https or local_https_or_http):
        raise EngineError("packageSource", "This package does not come from a reviewed HTTPS source.")


def _staging_path(destination):
    return destination.with_name(destination.name + ".partial")


def _reset_staging(staging):
    if staging.exists():
        if not staging.is_dir() or staging.is_symlink() or not (staging / STAGING_MARKER).is_file():
            raise EngineError("packageStaging", "An incomplete installation folder exists without our staging marker. Remove it or choose another destination.")
        shutil.rmtree(staging)


def plan(package_id, destination, allow_hosts=ALLOWED_HOSTS):
    """Read-only space and destination check; no directory is created."""
    record = package(package_id)
    destination = Path(destination).absolute()
    total = sum(file["bytes"] for file in record["files"])
    free = shutil.disk_usage(destination.parent).free if destination.parent.is_dir() else 0
    reason = ""
    if any(path.is_symlink() for path in [destination, *destination.parents]):
        reason = "The destination uses a symbolic link."
    elif destination.exists():
        reason = "The destination already exists."
    elif not destination.parent.is_dir():
        reason = "The parent folder does not exist."
    elif free < total + SPACE_SLACK_BYTES:
        reason = "Not enough free space for the package and working slack."
    for file in record["files"]:
        _validate_source(file["url"], allow_hosts)
    return {"id": record["id"], "totalBytes": total, "freeBytes": free, "ready": not reason, "reason": reason}


def _download_verified(file, staging, allow_hosts):
    _validate_source(file["url"], allow_hosts)
    target = staging / file["name"]
    try:
        with urllib.request.urlopen(urllib.request.Request(file["url"], method="GET"), timeout=URL_TIMEOUT) as response, \
                open(target, "wb") as stream:
            hasher, received = hashlib.sha256(), 0
            while True:
                chunk = response.read(READ_CHUNK)
                if not chunk:
                    break
                received += len(chunk)
                if received > file["bytes"]:
                    raise EngineError("packageMismatch", "The downloaded model file is larger than its pinned size.")
                hasher.update(chunk)
                stream.write(chunk)
            if received != file["bytes"]:
                raise EngineError("packageMismatch", "The downloaded model file ended before its pinned size.")
            if hasher.hexdigest() != file["sha256"]:
                raise EngineError("packageMismatch", "The downloaded model file does not match its pinned SHA-256.")
            stream.flush()
            os.fsync(stream.fileno())
    except EngineError:
        target.unlink(missing_ok=True)
        raise
    except (urllib.error.URLError, OSError, TimeoutError):
        target.unlink(missing_ok=True)
        raise EngineError("packageNetwork", "The model file could not be downloaded. No image was generated.") from None
    return file["name"]


def _ledger_path(state_directory=None):
    directory = Path(state_directory) if state_directory else default_state_directory()
    return Path(directory).absolute() / "installs.json"


def _read_ledger(state_directory=None):
    path = _ledger_path(state_directory)
    if not path.is_file():
        return []
    try:
        with path.open("rb") as stream:
            data = json.loads(stream.read(model_store.MAX_RECEIPT_BYTES))
        if data.get("format") != 1 or not isinstance(data.get("installs"), list):
            raise ValueError()
        return list(data["installs"])
    except (OSError, ValueError):
        raise EngineError("invalidStorage", "The installation ledger is unreadable. Remove it and reinstall.") from None


def _ledger_append(state_directory, entry):
    installs_list = _read_ledger(state_directory)
    installs_list.append(entry)
    model_store.write_receipt(_ledger_path(state_directory), {"format": 1, "installs": installs_list[-MAX_LEDGER_INSTALLS:]})


def installs(state_directory=None):
    """Registered installations, without network use."""
    return [{**entry} for entry in _read_ledger(state_directory)]


def uninstall(destination, state_directory=None):
    """Delete a VisoLoom-managed download and its registration. Only folders
    the installation ledger itself registered are eligible; no other folder
    is ever deleted."""
    target = Path(destination).absolute()
    entries = _read_ledger(state_directory)
    record = None
    for entry in entries:
        if entry.get("destination") == str(target):
            record = entry
    if record is None:
        raise EngineError("notManaged", "Only VisoLoom-managed downloads can be deleted through VisoLoom. This folder was not installed by VisoLoom.")
    _symlink_free(target)
    try:
        if target.is_dir():
            shutil.rmtree(target)
    except OSError:
        raise EngineError("packageStorage", "The downloaded files could not be deleted. Close other programs using that folder and try again.") from None
    Path(record["receipt"]).unlink(missing_ok=True)
    model_store.write_receipt(_ledger_path(state_directory),
                              {"format": 1, "installs": [entry for entry in entries if entry is not record][-MAX_LEDGER_INSTALLS:]})
    return {"removed": record["id"], "destination": str(target)}


def install(package_id, destination, receipt, state_directory=None, allow_hosts=ALLOWED_HOSTS, progress=None):
    """Download, verify and register one reviewed package. Interrupted or
    failing runs leave no partial package and never an unregistered copy."""
    record = package(package_id)
    destination = Path(destination).absolute()
    receipt = Path(receipt).absolute()
    _symlink_free(destination)
    _symlink_free(receipt)
    if receipt.is_dir():
        raise EngineError("invalidStorage", "The receipt path must be a file path.")
    if destination.exists():
        raise EngineError("packageDestination", "The destination folder already exists; nothing was changed.")
    if not destination.parent.is_dir():
        raise EngineError("packageDestination", "The parent folder does not exist.")
    total = sum(file["bytes"] for file in record["files"])
    if shutil.disk_usage(destination.parent).free < total + SPACE_SLACK_BYTES:
        raise EngineError("packageSpace", "Not enough free space for the package and working slack.")

    staging = _staging_path(destination)
    _reset_staging(staging)
    staging.mkdir(mode=0o700)
    try:
        marker = staging / STAGING_MARKER
        marker.write_text(json.dumps({"id": record["id"], "started": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}), encoding="utf-8")
        transferred = []
        # One progress object per completed file, then the final record below:
        # a long download reports in bounded events instead of staying silent.
        for file in record["files"]:
            transferred.append(_download_verified(file, staging, allow_hosts))
            if progress is not None:
                progress({"event": "fileComplete", "name": file["name"], "index": len(transferred),
                          "fileCount": len(record["files"]), "bytes": file["bytes"]})
        marker.unlink()
        os.rename(staging, destination)
    except BaseException:
        shutil.rmtree(staging, ignore_errors=True)
        raise
    try:
        verified = model_store.verify(destination, receipt, profile=record["profile"])
    except BaseException:
        # Never leave a complete-looking folder without a verified receipt.
        shutil.rmtree(destination, ignore_errors=True)
        raise
    entry = {
        "id": record["id"],
        "profile": record["profile"],
        "destination": str(destination),
        "receipt": str(receipt),
        "digest": verified["digest"],
        "totalBytes": total,
        "files": len(record["files"]),
        "installedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    ledger_directory = state_directory if state_directory else Path(default_state_directory())
    _ledger_append(ledger_directory, entry)
    return {"installed": transferred, "destination": str(destination), "digest": verified["digest"]}


def main(argv=None):
    parser = argparse.ArgumentParser(description="Reviewed model package installer.")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("list", help="List reviewed packages without network use.")
    ledger = commands.add_parser("installs", help="List registered installations without network use.")
    ledger.add_argument("--state-directory")
    planner = commands.add_parser("plan", help="Check destination space and readiness.")
    planner.add_argument("--package", required=True)
    planner.add_argument("--destination", required=True)
    installer = commands.add_parser("install", help="Download, verify and register one package.")
    installer.add_argument("--package", required=True)
    installer.add_argument("--destination", required=True)
    installer.add_argument("--receipt", required=True)
    installer.add_argument("--state-directory")
    installer.add_argument("--progress", action="store_true",
                           help="Report one JSON object per completed file on stdout, with the final record as one line.")
    remover = commands.add_parser("uninstall", help="Delete a managed download and its registration.")
    remover.add_argument("--destination", required=True)
    remover.add_argument("--state-directory")
    arguments = parser.parse_args(argv)
    try:
        if arguments.command == "list":
            result = packages()
        elif arguments.command == "plan":
            result = plan(arguments.package, arguments.destination)
        elif arguments.command == "installs":
            result = installs(arguments.state_directory)
        elif arguments.command == "uninstall":
            result = uninstall(arguments.destination, arguments.state_directory)
        else:
            progress = (lambda event: print(json.dumps(event), flush=True)) if arguments.progress else None
            result = install(arguments.package, arguments.destination, arguments.receipt, arguments.state_directory, progress=progress)
    except EngineError as error:
        print(json.dumps({"error": error.code, "message": error.message}), file=sys.stderr)
        return 1
    # The bridge polls install stdout as one JSON object per line; everything
    # else prints indented records as before.
    compact = arguments.command == "install" and arguments.progress
    print(json.dumps(result) if compact else json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
