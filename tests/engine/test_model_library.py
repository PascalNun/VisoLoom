"""Small authored folder fixtures; no ML imports, downloads or real checkpoints."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "engine"))
from common import EngineError
import model_library


class ModelLibraryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.model = self.root / "Existing model"
        for component in ["transformer", "text_encoder", "vae", "tokenizer"]:
            (self.model / component).mkdir(parents=True)
            name = "tokenizer_config.json" if component == "tokenizer" else "0.safetensors"
            (self.model / component / name).write_bytes(b"authored-fixture")
        self.index = self.model / "transformer/model.safetensors.index.json"
        self.index.write_text(json.dumps({"metadata": {"mflux_version": "0.22.0", "quantization_level": "4"}}))
        self.library = model_library.Library(self.root / "private/models.json")

    def tearDown(self):
        self.temporary.cleanup()

    def test_inspection_does_not_read_weight_bytes_or_claim_readiness(self):
        original = Path.open
        def no_weights(path, *args, **kwargs):
            if path.suffix == ".safetensors": self.fail("Inspection read model weights")
            return original(path, *args, **kwargs)
        with patch.object(Path, "open", no_weights):
            result = model_library.inspect(self.model)
        self.assertTrue(result["candidate"])
        self.assertEqual("candidate", result["status"])
        self.assertNotIn("acceptance", result)
        self.assertNotIn("ready", result)

    def test_metadata_distinguishes_other_model_architectures(self):
        self.index.unlink()
        config = self.model / "transformer/config.json"
        config.write_text(json.dumps({"_class_name": "Flux2Transformer2DModel", "num_layers": 5,
            "num_single_layers": 20, "num_attention_heads": 24, "attention_head_dim": 128}))
        self.assertTrue(model_library.inspect(self.model)["candidate"])
        data = json.loads(config.read_text())
        data["num_layers"] = 8
        config.write_text(json.dumps(data))
        self.assertEqual("differentModel", model_library.inspect(self.model)["status"])
        with self.assertRaises(EngineError): self.library.add(self.model)

    def test_app_checkpoints_are_explained_without_conversion_or_registration(self):
        (self.model / "flux_2_klein_4b_q8p.ckpt").write_bytes(b"fixture")
        result = model_library.inspect(self.model)
        self.assertEqual("appCheckpoint", result["status"])
        self.assertFalse(result["candidate"])
        with self.assertRaises(EngineError): self.library.add(self.model)
        self.assertFalse(self.library.path.exists())

    def test_incomplete_model_and_unknown_format_are_not_registered(self):
        self.index.unlink()
        self.assertEqual("unknownLayout", model_library.inspect(self.model)["status"])
        (self.model / "vae/0.safetensors").unlink()
        self.assertIn("vae", model_library.inspect(self.model)["message"])
        with self.assertRaises(EngineError): self.library.add(self.model)

    def test_save_deduplicates_and_remove_never_deletes_originals(self):
        before = sorted(str(p) for p in self.model.rglob("*"))
        entry = self.library.add(self.model)
        self.library.add(self.model / ".")
        self.assertEqual(1, len(self.library.list()["models"]))
        self.library.remove(entry["id"])
        self.assertEqual([], self.library.list()["models"])
        self.assertEqual(before, sorted(str(p) for p in self.model.rglob("*")))
        if os.name != "nt": self.assertEqual(0o600, self.library.path.stat().st_mode & 0o777)

    def test_missing_drive_keeps_library_entry_for_reconnection(self):
        self.library.add(self.model)
        self.model.rename(self.root / "temporarily disconnected")
        reports = self.library.list()["models"]
        self.assertEqual("missing", reports[0]["status"])
        self.assertFalse(reports[0]["candidate"])
        self.assertEqual(1, len(self.library.records()))

    def test_configured_model_is_listed_without_editing_connection_or_creating_library(self):
        reports = self.library.list(self.model)["models"]
        self.assertEqual(1, len(reports))
        self.assertTrue(reports[0]["configured"])
        self.assertFalse(self.library.path.exists())

    def test_readiness_requires_actual_receipt_and_runtime_acceptance(self):
        with patch("mflux_backend.MfluxBackend.check", return_value={"available": True, "acceptance": "unvalidated"}):
            result = self.library.list(self.model, self.root / "receipt.json")["models"][0]
            self.assertEqual("notReady", result["readiness"])
        with patch("mflux_backend.MfluxBackend.check", return_value={"available": True, "acceptance": "nativeEditPassed"}):
            result = self.library.list(self.model, self.root / "receipt.json")["models"][0]
            self.assertEqual("ready", result["readiness"])
        with patch("mflux_backend.MfluxBackend.check", side_effect=EngineError("installationChanged", "changed")):
            result = self.library.list(self.model, self.root / "receipt.json")["models"][0]
            self.assertEqual("notReady", result["readiness"])

    def test_saved_folder_cannot_change_active_connection_or_inherit_acceptance(self):
        entry = self.library.add(self.model)
        active = self.root / "active"
        active.mkdir()
        config = self.root / "connection.json"
        config.write_text(json.dumps({"modelDirectory": str(active), "installation": str(self.root / "receipt.json")}))
        before = config.read_bytes()
        result = self.library.list(active)["models"]
        saved = next(item for item in result if item["id"] == entry["id"])
        self.assertFalse(saved["configured"])
        self.assertNotIn("readiness", saved)
        self.library.remove(saved["id"])
        self.assertEqual(before, config.read_bytes())

    def test_malformed_and_oversized_records_are_not_overwritten(self):
        self.library.path.parent.mkdir()
        for text in ["{", '{"format":1,"folders":"bad"}', " " * (model_library.MAX_JSON + 1)]:
            self.library.path.write_text(text)
            with self.assertRaises((EngineError, ValueError)): self.library.add(self.model)
            self.assertEqual(text, self.library.path.read_text())

    def test_scan_and_registration_limits(self):
        with patch.object(model_library, "MAX_NODES", 2):
            with self.assertRaises(EngineError): model_library.inspect(self.model)
        with patch.object(model_library, "MAX_ENTRIES", 0):
            with self.assertRaises(EngineError): self.library.add(self.model)
        (self.model / "unexpected.py").write_text("raise RuntimeError('must never run')")
        self.assertEqual("unsupportedFiles", model_library.inspect(self.model)["status"])

    @unittest.skipIf(os.name == "nt", "Symlink creation requires Windows developer privileges")
    def test_links_are_rejected_in_model_and_library_storage(self):
        linked = self.root / "linked"
        linked.symlink_to(self.model, target_is_directory=True)
        with self.assertRaises(EngineError): model_library.inspect(linked)
        (self.model / "linked.json").symlink_to(self.index)
        with self.assertRaises(EngineError): model_library.inspect(self.model)
        linked_file = self.root / "library.json"
        linked_file.symlink_to(self.index)
        with self.assertRaises(EngineError): model_library.Library(linked_file)

    def test_cli_uses_isolated_python_and_needs_no_ml_packages(self):
        command = [sys.executable, "-I", str(Path(model_library.__file__)), "list", "--library", str(self.library.path)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=10)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual({"models": []}, json.loads(result.stdout))


if __name__ == "__main__":
    unittest.main()
