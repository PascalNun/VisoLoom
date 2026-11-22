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
# Byte progress is reported at fixed byte steps, so a multi-gigabyte file
# shows continuous movement with a bounded number of events: a 6 GB file
# emits one line every 32 MiB, roughly every line the user sees.
PROGRESS_STEP_BYTES = 32 * 1024 * 1024
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
    },
    # The third reviewed package: the current-generation FLUX restyler with
    # one gated file. Everything pinned here is public on 2026-11-22 — repo,
    # revision, file names and sizes — and the gated autoencoder's digest is
    # never shipped: the downloader reads it from the publisher's tree with
    # the downloading user's own token, the same right that serves the bytes
    # (ADR 0032). The engine has no reviewed adapter for this profile yet; an
    # installation stays non-renderable until that recipe ships and passes a
    # real edit, which the engine reports as-is.
    "flux3-image-klein-gguf": {
        "id": "flux3-image-klein-gguf",
        "display": "FLUX.3 Image Klein (ggml, quantised)",
        "profile": "sdcpp-80c4707-flux3-image-klein",
        "purpose": "Current-generation FLUX restyling: keeps the capture's geometry and outlines while applying new light, materials and style.",
        "terms": {
            "licence": "FLUX Kommunity License v.1.0",
            "files": [
                "https://huggingface.co/black-forest-labs/flux-3-image-klein/blob/main/LICENSE.md",
            ],
        },
        "headersChecked": "2026-11-22",
        "files": [
            {
                "name": "flux3-image-klein-Q4_K.gguf",
                "url": "https://huggingface.co/leejet/flux-3-image-klein-GGUF/resolve/57dfa91df2284f4985a4107c399d05d12e0f9b63/flux3-image-klein-Q4_K.gguf",
                "bytes": 3514023104,
                "sha256": "19ddbfbed61da4bb16ebf169aaf48e9e5db43aba557be1ee537e9f00f586092e",
            },
            {
                "name": "Qwen3VL-4B-Instruct-Q4_K_M.gguf",
                "url": "https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct-GGUF/resolve/1cd86afb9a95c410a6038ab3b40d8b578c892266/Qwen3VL-4B-Instruct-Q4_K_M.gguf",
                "bytes": 2497281664,
                "sha256": "66358cb18bb6b3b1b6675aa412c7a88ef01d228f481184d13668e5201c730a0a",
            },
            {
                "name": "ae.safetensors",
                "url": "https://huggingface.co/black-forest-labs/flux-3-image-klein/resolve/3b767ff9b5261d71645e2ec49d87d298d955238d/ae.safetensors",
                "bytes": 336211292,
                "authenticated": True,
            },
        ],
    },
    # The base variant of the same recipe: undistilled, for longer-schedule
    # studies on the same captures. Pins are public facts captured on
    # 2026-11-22; the autoencoder is the same gated file, its digest read
    # live with the downloading user's token.
    "flux3-image-klein-base-gguf": {
        "id": "flux3-image-klein-base-gguf",
        "display": "FLUX.3 Image Klein Base (ggml, quantised)",
        "profile": "sdcpp-80c4707-flux3-image-klein-base",
        "purpose": "Undistilled FLUX.3 variant with a longer schedule and guidance for fine-tuning and LoRA evaluation.",
        "terms": {
            "licence": "FLUX Kommunity License v.1.0",
            "files": [
                "https://huggingface.co/black-forest-labs/flux-3-image-klein/blob/main/LICENSE.md",
            ],
        },
        "headersChecked": "2026-11-22",
        "files": [
            {
                "name": "flux3-image-klein-base-Q4_K.gguf",
                "url": "https://huggingface.co/leejet/flux-3-image-klein-base-GGUF/resolve/d4b505c40929b77695c1b629e6ae3689239d65b9/flux3-image-klein-base-Q4_K.gguf",
                "bytes": 3493575712,
                "sha256": "924ec5cf4b02ce612cbcf6722a031ef0ef63a765b21b5012e7df8c675b1bd404",
            },
            {
                "name": "Qwen3VL-4B-Instruct-Q4_K_M.gguf",
                "url": "https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct-GGUF/resolve/1cd86afb9a95c410a6038ab3b40d8b578c892266/Qwen3VL-4B-Instruct-Q4_K_M.gguf",
                "bytes": 2497281664,
                "sha256": "66358cb18bb6b3b1b6675aa412c7a88ef01d228f481184d13668e5201c730a0a",
            },
            {
                "name": "ae.safetensors",
                "url": "https://huggingface.co/black-forest-labs/flux-3-image-klein/resolve/3b767ff9b5261d71645e2ec49d87d298d955238d/ae.safetensors",
                "bytes": 336211292,
                "authenticated": True,
            },
        ],
    },
    # The licensed FLUX.2 step up from klein 4B, the runner's documented
    # klein 9B recipe. Public pins from 2026-11-22; the gated FLUX.2-dev
    # autoencoder's digest is read with the downloading user's token.
    "flux2-klein-9b-gguf": {
        "id": "flux2-klein-9b-gguf",
        "display": "FLUX.2 klein 9B (ggml, quantised)",
        "profile": "sdcpp-80c4707-flux2-klein-9b",
        "purpose": "Licensed FLUX.2 step up from klein 4B for photorealistic detail on the same captures.",
        "terms": {
            "licence": "BFL FLUX.2 licence",
            "files": [
                "https://huggingface.co/black-forest-labs/FLUX.2-klein-9B/blob/main/LICENSE.md",
            ],
        },
        "headersChecked": "2026-11-22",
        "files": [
            {
                "name": "flux-2-klein-9b-Q4_0.gguf",
                "url": "https://huggingface.co/leejet/FLUX.2-klein-9B-GGUF/resolve/cc588497a95ffc2937ebbd6b9b3916a11ada6e5b/flux-2-klein-9b-Q4_0.gguf",
                "bytes": 5616208032,
                "sha256": "a7e77afa96871d16679ff7b949bd25f20c8179f219c4b662cac91e81ed99b944",
            },
            {
                "name": "Qwen3-8B-Q4_K_M.gguf",
                "url": "https://huggingface.co/unsloth/Qwen3-8B-GGUF/resolve/a6adef130ffb23ddaf1a62fec9dced968c9bc482/Qwen3-8B-Q4_K_M.gguf",
                "bytes": 5027784512,
                "sha256": "120307ba529eb2439d6c430d94104dabd578497bc7bfe7e322b5d9933b449bd4",
            },
            {
                "name": "ae.safetensors",
                "url": "https://huggingface.co/black-forest-labs/FLUX.2-dev/resolve/e8af80c9dd820ab07ee4171d024f03e5c4b3024b/ae.safetensors",
                "bytes": 336211292,
                "authenticated": True,
            },
        ],
    },
    # The quick non-FLUX comparison beside klein 4B, the runner's documented
    # LongCat image edit recipe. Public pins from 2026-11-22; the gated
    # FLUX.1-dev autoencoder's digest is read with the downloading user's
    # token, and the model itself stays Apache-2.0.
    "longcat-image-edit-gguf": {
        "id": "longcat-image-edit-gguf",
        "display": "LongCat Image Edit Turbo (ggml, quantised)",
        "profile": "sdcpp-80c4707-longcat-image-edit",
        "purpose": "Quick non-FLUX restyling comparison beside klein 4B: broad treatments and competition sketches.",
        "terms": {
            "licence": "Apache-2.0 · gated BFL autoencoder",
            "files": [
                "https://huggingface.co/meituan-longcat/LongCat-Image-Edit-Turbo",
                "https://huggingface.co/black-forest-labs/FLUX.1-dev/blob/main/LICENSE.md",
            ],
        },
        "headersChecked": "2026-11-22",
        "files": [
            {
                "name": "LongCat-Image-Edit-Q4_K_M.gguf",
                "url": "https://huggingface.co/vantagewithai/LongCat-Image-Edit-GGUF/resolve/8bfeb76dd77f783aecbe99bcc32f22ee1a45e222/LongCat-Image-Edit-Q4_K_M.gguf",
                "bytes": 3867152736,
                "sha256": "104cd11e357a43c5fab60e16e8c8517f8e6b7df596004042b507556145fd4f63",
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
                "name": "ae.safetensors",
                "url": "https://huggingface.co/black-forest-labs/FLUX.1-dev/resolve/45d95c497444bbaebe766e1528792fc9158c0c3a/ae.safetensors",
                "bytes": 335304388,
                "authenticated": True,
            },
        ],
    },
    # The high-memory FLUX.2 comparison for detailed materials and
    # multi-reference work, the runner's documented Flux.2-dev recipe.
    # Public pins from 2026-11-22; the gated FLUX.2-dev autoencoder's digest
    # is read with the downloading user's token, like the 9B package above.
    "flux2-dev-gguf": {
        "id": "flux2-dev-gguf",
        "display": "FLUX.2 dev (ggml, quantised)",
        "profile": "sdcpp-80c4707-flux2-dev",
        "purpose": "High-memory FLUX.2 comparison for detailed materials, lighting and multi-reference work.",
        "terms": {
            "licence": "BFL FLUX.2-dev licence",
            "files": [
                "https://huggingface.co/black-forest-labs/FLUX.2-dev/blob/main/LICENSE.md",
            ],
        },
        "headersChecked": "2026-11-22",
        "files": [
            {
                "name": "flux2-dev-Q4_K_S.gguf",
                "url": "https://huggingface.co/city96/FLUX.2-dev-gguf/resolve/ade5d688ddab0d9cf4a5b01bf4321e01c115020d/flux2-dev-Q4_K_S.gguf",
                "bytes": 19299128288,
                "sha256": "b9c1c8295ed044f54c3a9894a800e003b4ecc94bfb0f63192b68bafc232c2b27",
            },
            {
                "name": "Mistral-Small-3.2-24B-Instruct-2506-Q4_K_M.gguf",
                "url": "https://huggingface.co/unsloth/Mistral-Small-3.2-24B-Instruct-2506-GGUF/resolve/b750ec2299225e492f1bd27cab88a0a595fa848f/Mistral-Small-3.2-24B-Instruct-2506-Q4_K_M.gguf",
                "bytes": 14333922848,
                "sha256": "a3cc56310807ed0d145eaf9f018ccda9ae7ad8edb41ec870aa2454b0d4700b3c",
            },
            {
                "name": "ae.safetensors",
                "url": "https://huggingface.co/black-forest-labs/FLUX.2-dev/resolve/e8af80c9dd820ab07ee4171d024f03e5c4b3024b/ae.safetensors",
                "bytes": 336211292,
                "authenticated": True,
            },
        ],
    },
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
            "requiresToken": any(file.get("authenticated") for file in record["files"]),
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


def _read_token():
    """One line from standard input, read only when an authenticated file
    needs it (ADR 0032). The bridge pipes the saved token in and closes the
    stream: argv, the environment and state files never carry it, and it is
    never echoed or stored by this process."""
    token = sys.stdin.readline().rstrip("\r\n")
    if len(token) < 16 or len(token) > 4096 or any(ord(char) < 33 for char in token):
        raise EngineError("tokenInvalid", "This computer's saved Hugging Face token is unreadable. Enter it again in VisoLoom Settings.")
    return token


def _reader_digest(file, headers, allow_hosts):
    """The expected SHA-256 of a gated file, read from the publisher's own
    tree metadata with the downloading user's token (ADR 0032): a granted
    account receives the file's true digest with its listing; an account
    without the publisher's acceptance receives a masked placeholder
    instead. Shipped pins therefore carry only public facts — repo,
    revision, path and size — and nothing derived from anyone's licence
    acceptance ever ships inside the plugin."""
    prefix, _, remainder = file["url"].partition("/resolve/")
    revision, _, path = remainder.partition("/")
    directory, _, name = path.rpartition("/")
    origin = urllib.parse.urlsplit(prefix)
    repo = origin.path.strip("/")
    tree = f"/api/models/{repo}/tree/{revision}" + (f"/{directory}" if directory else "")
    listing = urllib.parse.urlunsplit((origin.scheme, origin.netloc, tree, "recursive=false", ""))
    _validate_source(listing, allow_hosts)
    with urllib.request.urlopen(urllib.request.Request(listing, method="GET", headers=headers), timeout=URL_TIMEOUT) as response:
        entries = json.load(response)
    for entry in entries if isinstance(entries, list) else []:
        listed = entry.get("path", "")
        if not (listed == path or listed.rsplit("/", 1)[-1] == name):
            continue
        digest = (entry.get("lfs") or {}).get("oid", "")
        if len(digest) == 64 and all(char in "0123456789abcdef" for char in digest):
            size = (entry.get("lfs") or {}).get("size", entry.get("size"))
            if size != file["bytes"]:
                raise EngineError("packageMismatch", "The publisher's file size no longer matches this package's recorded size.")
            return digest
        # A masked digest means the account was not granted access;
        # acceptance lives on the publisher's page, never in this app.
        raise EngineError("licenceAcceptance", "The publisher declined the download. Accept this model's licence on the publisher's page, then try again.")
    raise EngineError("packageSource", "The publisher's file listing no longer contains this package's file.")


def _download_verified(file, staging, allow_hosts, token_reader=None, bytes_report=None):
    _validate_source(file["url"], allow_hosts)
    target = staging / file["name"]
    # An authenticated pin sends exactly one header: the publisher's own
    # access scheme. Anonymous pins send no header at all and verify against
    # the publicly pinned digest; an authenticated pin reads its digest
    # live with the user's token, because that digest is readable only by
    # an account the publisher granted.
    headers = {}
    expected = file.get("sha256")
    if file.get("authenticated"):
        token = token_reader() if token_reader is not None else _read_token()
        headers["Authorization"] = "Bearer " + token
        expected = _reader_digest(file, headers, allow_hosts)
    try:
        with urllib.request.urlopen(urllib.request.Request(file["url"], method="GET", headers=headers), timeout=URL_TIMEOUT) as response, \
                open(target, "wb") as stream:
            hasher, received, reported = hashlib.sha256(), 0, -1
            if bytes_report is not None:
                # The first signal lands the moment the file starts, not
                # minutes later when a multi-gigabyte file completes.
                bytes_report(0)
                reported = 0
            while True:
                chunk = response.read(READ_CHUNK)
                if not chunk:
                    break
                received += len(chunk)
                if received > file["bytes"]:
                    raise EngineError("packageMismatch", "The downloaded model file is larger than its pinned size.")
                hasher.update(chunk)
                stream.write(chunk)
                if bytes_report is not None and received - reported >= PROGRESS_STEP_BYTES:
                    bytes_report(received)
                    reported = received
            if received != file["bytes"]:
                raise EngineError("packageMismatch", "The downloaded model file ended before its pinned size.")
            if hasher.hexdigest() != expected:
                raise EngineError("packageMismatch", "The downloaded model file does not match its verified SHA-256.")
            stream.flush()
            os.fsync(stream.fileno())
    except EngineError:
        target.unlink(missing_ok=True)
        raise
    except urllib.error.HTTPError as error:
        target.unlink(missing_ok=True)
        if error.code in (401, 403):
            # The publisher's gate: acceptance lives on its page, in the
            # user's name, never in this app (ADR 0032).
            raise EngineError("licenceAcceptance", "The publisher declined the download. Accept this model's licence on the publisher's page, then try again.") from None
        raise EngineError("packageNetwork", "The model file could not be downloaded. No image was generated.") from None
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


def install(package_id, destination, receipt, state_directory=None, allow_hosts=ALLOWED_HOSTS, progress=None, token_reader=None):
    """Download, verify and register one reviewed package. Interrupted or
    failing runs leave no partial package and never an unregistered copy. A
    token reader is consumed lazily, only if an authenticated pin needs it."""
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
        completed = 0
        # Progress travels in two events: bounded byte steps while a file
        # streams (fileProgress), one completion event per file, and the
        # final record last — a long download always shows movement, its
        # honest size and how far along it is.
        for file in record["files"]:
            def report(received, _file=file, _index=len(transferred) + 1, _before=completed):
                progress({"event": "fileProgress", "name": _file["name"], "index": _index,
                          "fileCount": len(record["files"]), "bytes": _file["bytes"], "bytesDone": received,
                          "packageBytes": total, "packageBytesDone": _before + received})
            transferred.append(_download_verified(file, staging, allow_hosts, token_reader,
                                                 report if progress is not None else None))
            completed += file["bytes"]
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
