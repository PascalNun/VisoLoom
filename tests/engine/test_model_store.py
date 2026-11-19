"""Verification-cache tests use tiny authored files, not downloaded weights."""
import os
from pathlib import Path
import sys
import tempfile
import subprocess
import json
import unittest
import time
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "engine"))
from common import EngineError, WorkerLock
import model_store
from mflux_backend import MfluxBackend


class ModelStoreTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.model = self.root / "model"
        for component in ["vae", "transformer", "text_encoder", "tokenizer"]:
            (self.model / component).mkdir(parents=True)
            (self.model / component / ("tokenizer_config.json" if component == "tokenizer" else "0.safetensors")).write_bytes(b"authored-fixture")
        self.receipt = self.root / "installation.json"
        self.record = model_store.verify(self.model, self.receipt)

    def tearDown(self):
        self.temporary.cleanup()

    def test_cached_digest_matches_full_scan_without_reading_weights_again(self):
        self.assertEqual(MfluxBackend().fingerprint(self.model), self.record["digest"])
        original = Path.open
        def reject_weights(path, *args, **kwargs):
            if path.suffix == ".safetensors": self.fail("A cached check read weight bytes")
            return original(path, *args, **kwargs)
        with patch.object(Path, "open", reject_weights):
            self.assertEqual(self.record["digest"], MfluxBackend(self.receipt).fingerprint(self.model))

    def test_same_size_change_with_restored_mtime_requires_verification(self):
        weight = self.model / "vae/0.safetensors"
        before = weight.stat()
        # Separate the two writes so this fixture does not rely on the Windows
        # filesystem clock advancing during an immediate same-size rewrite.
        if os.name == "nt": time.sleep(0.02)
        weight.write_bytes(b"modified-fixture")
        os.utime(weight, ns=(before.st_atime_ns, before.st_mtime_ns))
        with self.assertRaises(EngineError): model_store.checked(self.receipt)

    def test_extra_missing_or_replaced_file_invalidates_cache(self):
        added = self.model / "added.json"
        added.write_text("{}")
        with self.assertRaises(EngineError): model_store.checked(self.receipt)
        added.unlink()
        removed = self.model / "transformer/0.safetensors"
        removed.unlink()
        with self.assertRaises(EngineError): model_store.checked(self.receipt)

    def test_native_acceptance_is_bound_to_the_verified_model_and_profile(self):
        with self.assertRaises(EngineError): model_store.accept(self.receipt, "0" * 64, {})
        self.assertNotIn("nativeAcceptance", model_store.checked(self.receipt))
        model_store.accept(self.receipt, self.record["digest"], {"backend": MfluxBackend.identifier, "width": 512, "height": 448})
        self.assertEqual(MfluxBackend.identifier, model_store.checked(self.receipt)["nativeAcceptance"]["backend"])
        # Explicit full re-verification clears old acceptance rather than trusting a changed receipt.
        self.assertNotIn("nativeAcceptance", model_store.verify(self.model, self.receipt))

    def test_installation_lock_excludes_workers_from_other_job_roots(self):
        jobs = self.root / "other-jobs"
        jobs.mkdir()
        job = jobs / "fixture"
        job.mkdir()
        (job / "request.json").write_text(json.dumps({"installation": str(self.receipt)}))
        (job / "state.json").write_text(json.dumps({"state": "running"}))
        with WorkerLock(self.root, ".inference.lock"):
            result = subprocess.run([sys.executable, "-I", str(Path(__file__).resolve().parents[2] / "engine/mflux_backend.py"), str(job)],
                capture_output=True, timeout=10)
        self.assertEqual(1, result.returncode)
        self.assertEqual("engineBusy", json.loads((job / "failure.json").read_text())["code"])

    @unittest.skipIf(os.name == "nt", "Creating symlinks needs Windows developer privileges")
    def test_symlinks_cannot_reuse_a_verification_record(self):
        (self.model / "link.json").symlink_to(self.model / "tokenizer/tokenizer_config.json")
        with self.assertRaises(EngineError): model_store.checked(self.receipt)
        with self.assertRaises(EngineError): model_store.verify(self.model, self.receipt)
