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
import model_store


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

    def test_setup_report_does_not_inherit_acceptance_from_another_model(self):
        entry = self.library.add(self.model)
        with patch("mflux_backend.MfluxBackend.check", return_value={"available": True, "acceptance": "nativeEditPassed"}):
            result = self.library.plan(entry["id"], self.root / "another-model", self.root / "another-receipt.json")
        self.assertFalse(result["modelAccepted"])
        self.assertEqual("verificationNeeded", result["state"])
        self.assertNotIn("passed", [step["state"] for step in result["steps"]])

    def test_accepted_files_still_require_a_live_service_connection_check(self):
        record = model_store.verify(self.model, self.root / "receipt.json")
        entry_id = model_library.identifier(self.model)
        before = (self.root / "receipt.json").read_bytes()
        with patch("mflux_backend.MfluxBackend.check", return_value={"available": True, "acceptance": "nativeEditPassed"}):
            result = self.library.plan(entry_id, self.model, self.root / "receipt.json")
        self.assertTrue(result["modelAccepted"])
        self.assertEqual("connectionCheckNeeded", result["state"])
        self.assertEqual("checkRequired", result["steps"][-1]["state"])
        self.assertFalse(self.library.path.exists())
        self.assertEqual(before, (self.root / "receipt.json").read_bytes())
        self.assertEqual(record["digest"], model_store.checked(self.root / "receipt.json")["digest"])

    def test_setup_distinguishes_changed_files_from_an_untested_verified_copy(self):
        receipt = self.root / "receipt.json"
        model_store.verify(self.model, receipt)
        entry_id = model_library.identifier(self.model)
        with patch("mflux_backend.MfluxBackend.check", return_value={"available": True, "acceptance": "unvalidated"}):
            self.assertEqual("editTestNeeded", self.library.plan(entry_id, self.model, receipt)["state"])
            (self.model / "vae/0.safetensors").write_bytes(b"changed-fixture")
            result = self.library.plan(entry_id, self.model, receipt)
            self.assertEqual("verificationNeeded", result["state"])
            self.assertFalse(result["modelAccepted"])

    def test_setup_stops_at_missing_runtime_without_copy_or_space_check(self):
        entry = self.library.add(self.model)
        with patch("mflux_backend.MfluxBackend.check", return_value={"available": False, "message": "Windows adapter not implemented."}), \
                patch("model_library.shutil.disk_usage", side_effect=AssertionError("Unexpected disk check")):
            result = self.library.plan(entry["id"])
        self.assertEqual("runtimeNeeded", result["state"])
        self.assertFalse(result["modelAccepted"])
        self.assertIn("Windows", result["steps"][-1]["message"])

    def test_preparation_space_is_checked_on_destination_volume_without_creating_it(self):
        self.index.unlink()
        (self.model / "transformer/config.json").write_text(json.dumps({"_class_name": "Flux2Transformer2DModel",
            "num_layers": 5, "num_single_layers": 20, "num_attention_heads": 24, "attention_head_dim": 128}))
        entry = self.library.add(self.model)
        destination = self.root / "destination"
        destination.mkdir()
        with patch("model_library.default_state_directory", return_value=destination / "private"), \
                patch("mflux_backend.MfluxBackend.check", return_value={"available": True}), \
                patch("model_library.shutil.disk_usage") as space:
            space.return_value.free = 5 * 1024**3
            self.assertEqual("spaceNeeded", self.library.plan(entry["id"])["state"])
            space.assert_called_with(destination)
            space.return_value.free = 20 * 1024**3
            self.assertEqual("preparationNeeded", self.library.plan(entry["id"])["state"])
        self.assertEqual([], list(destination.iterdir()))

    def test_setup_report_is_read_only_and_does_not_read_tensor_bytes(self):
        entry = self.library.add(self.model)
        before = self.library.path.read_bytes()
        original = Path.open
        def no_weights(path, *args, **kwargs):
            if path.suffix == ".safetensors": self.fail("Setup report read model weights")
            return original(path, *args, **kwargs)
        with patch.object(Path, "open", no_weights), \
                patch("mflux_backend.MfluxBackend.check", return_value={"available": True}):
            self.library.plan(entry["id"])
        self.assertEqual(before, self.library.path.read_bytes())
        self.assertFalse((self.root / "receipt.json").exists())

    def test_removed_entry_and_unavailable_drive_cannot_proceed_with_setup(self):
        entry = self.library.add(self.model)
        self.model.rename(self.root / "disconnected-drive")
        result = self.library.plan(entry["id"])
        self.assertEqual("folderNeeded", result["state"])
        self.assertFalse(result["modelAccepted"])
        self.library.remove(entry["id"])
        with self.assertRaises(EngineError) as error: self.library.plan(entry["id"])
        self.assertEqual("entryNotFound", error.exception.code)


if __name__ == "__main__":
    unittest.main()
