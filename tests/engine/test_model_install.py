"""Installer fixtures served over loopback; no external network or real weights."""
import hashlib
import contextlib
import io
import json
import os
import socket
import sys
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "engine"))
from common import EngineError
import model_install
import model_store

import unittest


class LocalPackages:
    """Serves pinned fixture bytes over loopback for the installer's seam."""

    def __init__(self, files, mode="complete"):
        self.files, self.mode = files, mode
        outer = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.0"

            def do_GET(self):
                name = self.path.lstrip("/")
                data = outer.files.get(name)
                if data is None:
                    self.send_response(404)
                    self.end_headers()
                    return
                if outer.mode == "silent-close":
                    self.connection.close()
                    return
                self.send_response(200)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                if outer.mode == "truncate":
                    self.wfile.write(data[:-1])
                else:
                    self.wfile.write(data)

            def log_message(self, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.port = self.server.server_port
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def url(self, name):
        return f"http://127.0.0.1:{self.port}/{name}"

    def stop(self):
        self.server.shutdown()
        self.server.server_close()


class ModelInstallTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.state = self.root / "state"
        self.state.mkdir()
        self.files = {
            "transformer-Q4_0.gguf": b"a" * 16,
            "encoder-Q4_K_M.gguf": b"b" * 24,
            "decoder.safetensors": b"c" * 8,
        }
        self.server = LocalPackages(self.files)
        model_install.PACKAGES["test-fixture"] = {
            "id": "test-fixture",
            "display": "Fixture package",
            "profile": "sdcpp-test-flux-klein-4b",
            "purpose": "Fixture only.",
            "terms": {"licence": "Apache-2.0", "files": []},
            "files": [
                {
                    "name": name,
                    "url": self.server.url(name),
                    "bytes": len(data),
                    "sha256": hashlib.sha256(data).hexdigest(),
                }
                for name, data in self.files.items()
            ],
        }

    def tearDown(self):
        model_install.PACKAGES.pop("test-fixture", None)
        self.server.stop()
        self.temporary.cleanup()

    def install(self, name="serial-model", **overrides):
        settings = {"package_id": "test-fixture", "destination": self.root / name, "receipt": self.root / f"{name}.json", "state_directory": self.state}
        settings.update(overrides)
        return model_install.install(**settings)

    def test_install_downloads_verifies_and_registers_through_the_existing_receipt(self):
        result = self.install()
        for name, data in self.files.items():
            self.assertEqual((Path(result["destination"]) / name).read_bytes(), data)
        record = model_store.checked(self.root / "serial-model.json", self.root / "serial-model", profile="sdcpp-test-flux-klein-4b")
        self.assertEqual(record["digest"], result["digest"])
        ledger = json.loads((self.state / "installs.json").read_text())
        self.assertEqual(ledger["installs"][-1]["destination"], result["destination"])
        self.assertFalse((self.root / "serial-model.partial").exists())

    def test_changed_content_never_reaches_a_destination_or_ledger(self):
        self.server.files["transformer-Q4_0.gguf"] = b"x" * 16
        with self.assertRaises(EngineError) as thrown:
            self.install()
        self.assertEqual("packageMismatch", thrown.exception.code)
        self.assertFalse((self.root / "serial-model").exists())
        self.assertFalse((self.root / "serial-model.partial").exists())
        self.assertFalse((self.root / "serial-model.json").exists())
        self.assertFalse((self.state / "installs.json").exists())

    def test_truncated_download_fails_closed(self):
        self.server.mode = "truncate"
        with self.assertRaises(EngineError) as thrown:
            self.install()
        self.assertEqual("packageMismatch", thrown.exception.code)

    def test_response_longer_than_the_pin_fails_closed(self):
        model_install.PACKAGES["test-fixture"]["files"][0]["bytes"] = 8
        with self.assertRaises(EngineError) as thrown:
            self.install()
        self.assertEqual("packageMismatch", thrown.exception.code)

    def test_connection_failure_maps_to_a_network_error_and_cleans_staging(self):
        self.server.mode = "silent-close"
        with self.assertRaises(EngineError) as thrown:
            self.install()
        self.assertIn(thrown.exception.code, {"packageNetwork", "packageMismatch"})
        self.assertFalse((self.root / "serial-model.partial").exists())

    def test_unreviewed_public_hosts_are_refused_before_any_network_use(self):
        model_install.PACKAGES["test-fixture"]["files"][0]["url"] = "https://example.com/transformer.gguf"
        def no_network(*args, **kwargs):
            raise AssertionError("A request was made")
        with patch.object(model_install.urllib.request, "urlopen", no_network):
            with self.assertRaises(EngineError) as thrown:
                self.install()
        self.assertEqual("packageSource", thrown.exception.code)

    def test_existing_destination_and_foreign_staging_refuse_instead_of_deleting(self):
        (self.root / "serial-model").mkdir()
        with self.assertRaises(EngineError) as thrown:
            self.install()
        self.assertEqual("packageDestination", thrown.exception.code)
        foreign = self.root / "other-model.partial"
        foreign.mkdir()
        (foreign / "important.txt").write_text("not ours")
        with self.assertRaises(EngineError) as thrown:
            self.install("other-model")
        self.assertEqual("packageStaging", thrown.exception.code)
        self.assertTrue((foreign / "important.txt").exists())

    def test_our_own_interrupted_staging_is_cleaned_and_restarted(self):
        staging = self.root / "serial-model.partial"
        staging.mkdir()
        (staging / model_install.STAGING_MARKER).write_text("{}")
        (staging / "transformer-Q4_0.gguf").write_bytes(b"interrupted")
        result = self.install()
        self.assertEqual(len(self.files) + 0, len(result["installed"]))
        for name, data in self.files.items():
            self.assertEqual((Path(result["destination"]) / name).read_bytes(), data)
        self.assertFalse((self.root / "serial-model.partial").exists())

    def test_plan_is_read_only_and_explains_itself(self):
        result = model_install.plan("test-fixture", self.root / "fresh-model", allow_hosts=("127.0.0.1",))
        self.assertTrue(result["ready"])
        self.assertEqual(result["totalBytes"], sum(len(data) for data in self.files.values()))
        self.assertFalse((self.root / "fresh-model").exists())
        self.assertEqual(
            "The destination already exists.",
            model_install.plan("test-fixture", self.root, allow_hosts=("127.0.0.1",))["reason"],
        )
        with patch.object(model_install.shutil, "disk_usage", lambda parent: type("usage", (), {"free": 0})()):
            sized = model_install.plan("test-fixture", self.root / "fresh-model", allow_hosts=("127.0.0.1",))
        self.assertFalse(sized["ready"])
        self.assertIn("free space", sized["reason"])

    def test_install_refuses_without_room_for_the_package_and_slack(self):
        with patch.object(model_install.shutil, "disk_usage", lambda parent: type("usage", (), {"free": 3 * 16})()):
            with self.assertRaises(EngineError) as thrown:
                self.install()
        self.assertEqual("packageSpace", thrown.exception.code)

    def test_shipped_pins_are_commit_pinned_https_huggingface_and_hashed(self):
        entry = model_install.PACKAGES["flux2-klein-4b-gguf"]
        self.assertEqual(5207177908, sum(file["bytes"] for file in entry["files"]))
        self.assertEqual("sdcpp-80c4707-flux2-klein-4b", entry["profile"])
        for file in entry["files"]:
            parsed = model_install.urllib.parse.urlsplit(file["url"])
            self.assertEqual("https", parsed.scheme)
            self.assertEqual("huggingface.co", parsed.hostname)
            self.assertIn("/resolve/", file["url"])
            self.assertNotIn("/main/", file["url"])
            self.assertEqual(64, len(file["sha256"]))
            int(file["sha256"], 16)
            self.assertGreater(file["bytes"], 0)

    def test_the_install_ledger_stays_bounded(self):
        for index in range(model_install.MAX_LEDGER_INSTALLS + 1):
            self.install(name=f"model-{index}")
        ledger = json.loads((self.state / "installs.json").read_text())
        self.assertEqual(model_install.MAX_LEDGER_INSTALLS, len(ledger["installs"]))
        self.assertEqual(
            str(self.root / f"model-{model_install.MAX_LEDGER_INSTALLS}"),
            ledger["installs"][-1]["destination"],
        )
        self.assertEqual(
            str(self.root / "model-1"),
            ledger["installs"][0]["destination"],
        )


    def test_progress_reports_one_event_per_completed_file(self):
        events = []
        result = self.install(progress=events.append)
        self.assertEqual(len(self.files), len(events))
        self.assertEqual([event["name"] for event in events], list(self.files))
        for index, event in enumerate(events, start=1):
            self.assertEqual("fileComplete", event["event"])
            self.assertEqual(index, event["index"])
            self.assertEqual(len(self.files), event["fileCount"])
            self.assertEqual(len(self.files[event["name"]]), event["bytes"])
        self.assertEqual(sum(event["bytes"] for event in events), sum(len(data) for data in self.files.values()))
        self.assertEqual(len(self.files), len(result["installed"]))

    def test_cli_progress_prints_one_json_object_per_line_and_a_compact_record(self):
        captured = io.StringIO()
        with contextlib.redirect_stdout(captured):
            code = model_install.main(["install", "--package", "test-fixture", "--progress",
                                       "--destination", str(self.root / "cli-model"),
                                       "--receipt", str(self.root / "cli-model.json"),
                                       "--state-directory", str(self.state)])
        self.assertEqual(0, code)
        lines = [line for line in captured.getvalue().splitlines() if line]
        objects = [json.loads(line) for line in lines]
        for index, event in enumerate(objects[:-1], start=1):
            self.assertEqual("fileComplete", event["event"])
            self.assertEqual(index, event["index"])
        final = objects[-1]
        self.assertNotIn("event", final)
        self.assertEqual(str(self.root / "cli-model"), final["destination"])
        self.assertEqual(len(self.files), len(final["installed"]))

    def test_installs_lists_registered_installations_without_network_use(self):
        self.assertEqual([], model_install.installs(self.state))
        result = self.install()
        recorded = model_install.installs(self.state)
        self.assertEqual(1, len(recorded))
        self.assertEqual(recorded[0]["destination"], result["destination"])
        self.assertEqual(recorded[0]["digest"], result["digest"])
        self.assertEqual(recorded[0]["id"], "test-fixture")

    def test_missing_ledger_is_an_empty_installation_list(self):
        self.assertEqual([], model_install.installs(self.state / "absent"))

    def test_unreadable_ledger_fails_closed(self):
        self.install()
        (self.state / "installs.json").write_text("{not json}")
        with self.assertRaises(EngineError) as thrown:
            model_install.installs(self.state)
        self.assertEqual("invalidStorage", thrown.exception.code)

    def test_uninstall_removes_only_ledger_registered_downloads(self):
        result = self.install()
        foreign = self.root / "foreign-model"
        foreign.mkdir()
        (foreign / "keep.txt").write_text("not ours")
        with self.assertRaises(EngineError) as thrown:
            model_install.uninstall(foreign, self.state)
        self.assertEqual("notManaged", thrown.exception.code)
        self.assertTrue((foreign / "keep.txt").exists())
        removed = model_install.uninstall(result["destination"], self.state)
        self.assertEqual("test-fixture", removed["removed"])
        self.assertFalse(Path(result["destination"]).exists())
        self.assertFalse(Path(self.root / "serial-model.json").exists())
        self.assertEqual([], model_install.installs(self.state))
        with self.assertRaises(EngineError) as thrown:
            model_install.uninstall(result["destination"], self.state)
        self.assertEqual("notManaged", thrown.exception.code)


if __name__ == "__main__":
    unittest.main()
