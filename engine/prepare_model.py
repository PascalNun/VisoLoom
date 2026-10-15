"""Explicit developer preparation, using installed MFLUX; no downloads or package installation."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).absolute().parent))
from common import EngineError, WorkerLock
from engine import private_directory, stop_worker, worker_environment
from mflux_backend import MfluxBackend
import model_store


def convert(source, target):
    from mlx.utils import tree_flatten
    import mlx.core as mx
    from mflux.models.common.config import ModelConfig
    from mflux.models.common.weights import ModelSaver
    from mflux.models.flux2.weights.flux2_weight_definition import Flux2KleinWeightDefinition
    from mflux.models.flux2.variants import Flux2KleinEdit
    mx.set_memory_limit(8 * 1024**3)
    mx.set_cache_limit(256 * 1024**2)
    with mx.stream(mx.cpu):
        # Save original compute precision. MFLUX explicitly rejects saving a
        # model whose attention weights were cast for float16 inference.
        model = Flux2KleinEdit(model_config=ModelConfig.flux2_klein_4b(), model_path=str(source),
                              quantize=4, lora_paths=[], lora_scales=[])
        for _, parameter in tree_flatten(model.parameters()):
            mx.eval(parameter)
        ModelSaver.save_model(model, model.bits, str(target), Flux2KleinWeightDefinition)
    if (source / "README.md").is_file():
        shutil.copyfile(source / "README.md", target / "README.md")
    (target / "VisoLoom-preparation.md").write_text(
        "Locally prepared 4-bit weights using unmodified MFLUX 0.22.0. Original model/encoder terms still apply.\n",
        encoding="utf-8")


def prepare(source, target, receipt):
    backend = MfluxBackend()
    check = backend.check(source)
    if not check["available"]:
        raise EngineError(check["code"], check["message"])
    if target.exists() or receipt.exists():
        raise EngineError("alreadyExists", "Preparation never overwrites an existing model or verification record.")
    private_directory(target.parent)
    private_directory(receipt.parent)
    if shutil.disk_usage(target.parent).free < 10 * 1024**3:
        raise EngineError("insufficientSpace", "Keep at least 10 GiB free before preparing this model.")
    with WorkerLock(receipt.parent), WorkerLock(receipt.parent, ".inference.lock"):
        print(json.dumps({"phase": "verifyingSource"}), flush=True)
        before = model_store.entries(source)
        source_digest = backend.fingerprint(source)
        target.mkdir(mode=0o700)
        process = None
        try:
            print(json.dumps({"phase": "preparingWeights"}), flush=True)
            process = subprocess.Popen([sys.executable, "-I", str(Path(__file__).absolute()), "--worker", str(source), str(target)],
                env=worker_environment(target), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                start_new_session=os.name != "nt")
            deadline = time.monotonic() + 1800
            while process.poll() is None:
                if time.monotonic() > deadline or backend.resident_bytes(process.pid) > backend.maximum_resident_bytes:
                    raise EngineError("preparationStopped", "Preparation exceeded its time or memory budget.")
                time.sleep(1)
            if process.returncode or model_store.entries(source) != before or not backend.compatible_folder(target):
                raise EngineError("preparationFailed", "Preparation did not finish or the source changed. No model was registered.")
            print(json.dumps({"phase": "verifyingPreparedModel"}), flush=True)
            record = model_store.verify(target, receipt)
            record["sourceDigest"] = source_digest
            model_store.write_receipt(receipt, record)
            return {"prepared": True, "modelDigest": record["digest"], "bytes": sum(f["identity"][2] for f in record["files"]),
                    "acceptance": "unvalidated"}
        except BaseException:
            if process is not None:
                stop_worker(process)
            shutil.rmtree(target)
            receipt.unlink(missing_ok=True)
            raise


if __name__ == "__main__":
    os.umask(0o077)
    parser = argparse.ArgumentParser(description="Prepare a local development copy once; downloads nothing")
    parser.add_argument("source", type=Path)
    parser.add_argument("target", type=Path)
    parser.add_argument("--receipt", type=Path)
    parser.add_argument("--worker", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    try:
        if args.worker:
            convert(args.source, args.target)
        elif args.receipt:
            print(json.dumps(prepare(args.source.absolute(), args.target.absolute(), args.receipt.absolute())))
        else:
            parser.error("--receipt is required")
    except EngineError as exc:
        print(json.dumps({"code": exc.code, "message": exc.message}))
        sys.exit(1)
