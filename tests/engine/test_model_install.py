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

# The publisher-side fixture gate: only a request carrying this exact bearer
# passes, mirroring huggingface.co's gated-file behaviour.
GATED_TOKEN = "hf-fixture-download-token-0123456789"


class LocalPackages:
    """Serves pinned fixture bytes over loopback for the installer's seam."""

    def __init__(self, files, mode="complete", gated=()):
        self.files, self.mode, self.gated = files, mode, frozenset(gated)
        self.seen = []
        outer = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.0"

            def listing(self):
                # Mirrors the publisher's metadata endpoint: a granted bearer
                # sees each file's true digest, anyone else sees the masked
                # placeholder the real gate serves to unaccepted accounts.
                granted = self.headers.get("Authorization") == "Bearer " + GATED_TOKEN
                entries = []
                for name, data in outer.files.items():
                    masked = name in outer.gated and not granted
                    if outer.mode == "wrong-oid":
                        oid = hashlib.sha256(b"unrelated").hexdigest()
                    else:
                        oid = "*" * 64 if masked else hashlib.sha256(data).hexdigest()
                    entries.append({"path": name, "size": len(data),
                                   "lfs": {"oid": oid, "size": len(data)}})
                body = json.dumps(entries).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def do_GET(self):
                path = self.path.split("?")[0].lstrip("/")
                name = path.rsplit("/", 1)[-1]
                outer.seen.append((path, self.headers.get("Authorization")))
                if "/tree/" in path:
                    self.listing()
                    return
                data = outer.files.get(name)
                if data is None:
                    self.send_response(404)
                    self.end_headers()
                    return
                if outer.mode in ("gate", "wrong-oid") and name in outer.gated and \
                        self.headers.get("Authorization") != "Bearer " + GATED_TOKEN:
                    self.send_response(403)
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

    def repo_url(self, name, repo="fixture-repo", revision="rev1"):
        return f"http://127.0.0.1:{self.port}/{repo}/resolve/{revision}/{name}"

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

    def test_byte_progress_reports_live_steps_with_package_totals(self):
        events = []
        with patch.object(model_install, "PROGRESS_STEP_BYTES", 4):
            self.install(name="progress-model", progress=events.append)
        # A loopback file arrives in one read, so each file reports its start
        # the moment it begins and the step it crossed as it finished; the
        # package total runs across all files, and completion stays its own
        # event.
        trace = [(event["event"], event.get("bytesDone"), event.get("packageBytesDone")) for event in events]
        self.assertEqual([
            ("fileProgress", 0, 0), ("fileProgress", 16, 16), ("fileComplete", None, None),
            ("fileProgress", 0, 16), ("fileProgress", 24, 40), ("fileComplete", None, None),
            ("fileProgress", 0, 40), ("fileProgress", 8, 48), ("fileComplete", None, None),
        ], trace)
        self.assertEqual({event["packageBytes"] for event in events if event["event"] == "fileProgress"},
                         {48})
        self.assertEqual([event["bytes"] for event in events if event["event"] == "fileProgress"],
                         [16, 16, 24, 24, 8, 8])

    def test_shipped_pins_are_commit_pinned_https_huggingface_and_hashed(self):
        shipped = ["flux2-klein-4b-gguf", "qwen-image-edit-2511-gguf",
                   "flux3-image-klein-gguf", "flux3-image-klein-base-gguf",
                   "flux2-klein-9b-gguf", "longcat-image-edit-gguf", "flux2-dev-gguf"]
        for package_id in shipped:
            entry = model_install.PACKAGES[package_id]
            self.assertTrue(entry["profile"], package_id)
            for file in entry["files"]:
                parsed = model_install.urllib.parse.urlsplit(file["url"])
                self.assertEqual("https", parsed.scheme, package_id)
                self.assertEqual("huggingface.co", parsed.hostname, package_id)
                self.assertIn("/resolve/", file["url"], package_id)
                self.assertNotIn("/main/", file["url"], package_id)
                self.assertGreater(file["bytes"], 0, package_id)
                if file.get("authenticated"):
                    # A gated pin ships public facts only; its digest is read
                    # live with the downloading user's token (ADR 0032) and
                    # must never be baked into the shipped manifest.
                    self.assertNotIn("sha256", file, package_id)
                else:
                    self.assertEqual(64, len(file["sha256"]), package_id)
                    int(file["sha256"], 16)
        totals = {package_id: sum(file["bytes"] for file in model_install.PACKAGES[package_id]["files"])
                  for package_id in shipped}
        self.assertEqual(5207177908, totals["flux2-klein-4b-gguf"])
        self.assertEqual(18632828902, totals["qwen-image-edit-2511-gguf"])
        self.assertEqual(6347516060, totals["flux3-image-klein-gguf"])
        self.assertEqual(6327068668, totals["flux3-image-klein-base-gguf"])
        self.assertEqual(10980203836, totals["flux2-klein-9b-gguf"])
        self.assertEqual(9738649348, totals["longcat-image-edit-gguf"])
        self.assertEqual(33969262428, totals["flux2-dev-gguf"])
        self.assertEqual("sdcpp-80c4707-flux2-klein-4b", model_install.PACKAGES["flux2-klein-4b-gguf"]["profile"])
        self.assertEqual("sdcpp-80c4707-flux3-image-klein", model_install.PACKAGES["flux3-image-klein-gguf"]["profile"])
        self.assertEqual("sdcpp-80c4707-flux2-klein-9b", model_install.PACKAGES["flux2-klein-9b-gguf"]["profile"])
        self.assertEqual("sdcpp-80c4707-longcat-image-edit", model_install.PACKAGES["longcat-image-edit-gguf"]["profile"])
        self.assertEqual("sdcpp-80c4707-flux2-dev", model_install.PACKAGES["flux2-dev-gguf"]["profile"])

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
        completed = [event for event in events if event["event"] == "fileComplete"]
        self.assertEqual(len(self.files), len(completed))
        self.assertEqual([event["name"] for event in completed], list(self.files))
        for index, event in enumerate(completed, start=1):
            self.assertEqual(index, event["index"])
            self.assertEqual(len(self.files), event["fileCount"])
            self.assertEqual(len(self.files[event["name"]]), event["bytes"])
        self.assertEqual(sum(event["bytes"] for event in completed), sum(len(data) for data in self.files.values()))
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
            self.assertIn(event["event"], ("fileProgress", "fileComplete"))
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


class GatedDownloadTests(unittest.TestCase):
    """ADR 0032: gated pins carry public facts only. The token the account
    saved is the same key that reads the digest and the bytes; nothing that
    required anyone's licence acceptance ships inside the plugin."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.state = self.root / "state"
        self.state.mkdir()
        self.files = {
            "transformer-Q4_0.gguf": b"a" * 16,
            "encoder-Q4_K_M.gguf": b"b" * 24,
        }
        self.server = LocalPackages(self.files, mode="gate", gated=["transformer-Q4_0.gguf"])
        model_install.PACKAGES["token-fixture"] = {
            "id": "token-fixture",
            "display": "Fixture gated package",
            "profile": "sdcpp-test-gated",
            "purpose": "Fixture only.",
            "terms": { "licence": "Apache-2.0", "files": []},
            "files": [
                {
                    # No sha256 ships for a gated pin, exactly like the real
                    # manifest: the digest is read live with the token.
                    "name": "transformer-Q4_0.gguf",
                    "url": self.server.repo_url("transformer-Q4_0.gguf"),
                    "bytes": 16,
                    "authenticated": True,
                },
                {
                    "name": "encoder-Q4_K_M.gguf",
                    "url": self.server.repo_url("encoder-Q4_K_M.gguf"),
                    "bytes": 24,
                    "sha256": hashlib.sha256(b"b" * 24).hexdigest(),
                },
            ],
        }

    def tearDown(self):
        model_install.PACKAGES.pop("token-fixture", None)
        self.server.stop()
        self.temporary.cleanup()

    def install(self, name="serial-model", **overrides):
        settings = {"package_id": "token-fixture", "destination": self.root / name,
                    "receipt": self.root / (name + ".json"), "state_directory": self.state}
        settings.update(overrides)
        return model_install.install(**settings)

    def test_the_digest_and_the_bytes_read_with_the_same_token(self):
        reads = []
        def reader():
            reads.append(1)
            return GATED_TOKEN
        result = self.install(token_reader=reader)
        self.assertEqual(str(self.root / "serial-model"), result["destination"])
        self.assertEqual(self.server.seen, [
            ("api/models/fixture-repo/tree/rev1", "Bearer " + GATED_TOKEN),
            ("fixture-repo/resolve/rev1/transformer-Q4_0.gguf", "Bearer " + GATED_TOKEN),
            ("fixture-repo/resolve/rev1/encoder-Q4_K_M.gguf", None),
        ])
        self.assertEqual([1], reads)

    def test_an_unaccepted_account_sees_a_masked_digest_and_downloads_nothing(self):
        with self.assertRaises(EngineError) as thrown:
            self.install(token_reader=lambda: "hf-accepted-nowhere-token")
        self.assertEqual("licenceAcceptance", thrown.exception.code)
        self.assertIn("Accept this model's licence", thrown.exception.message)
        self.assertEqual([("api/models/fixture-repo/tree/rev1", "Bearer hf-accepted-nowhere-token")], self.server.seen)
        self.assertFalse((self.root / "serial-model").exists())
        self.assertFalse((self.root / "serial-model.partial").exists())
        self.assertFalse((self.state / "installs.json").exists())

    def test_downloaded_bytes_still_verify_against_the_live_read_digest(self):
        self.server.mode = "wrong-oid"
        with self.assertRaises(EngineError) as thrown:
            self.install(token_reader=lambda: GATED_TOKEN)
        self.assertEqual("packageMismatch", thrown.exception.code)
        self.assertIn("does not match its verified SHA-256", thrown.exception.message)
        self.assertFalse((self.root / "serial-model").exists())
        self.assertFalse((self.state / "installs.json").exists())

    def test_an_unusable_token_fails_closed_before_any_request(self):
        for broken in ("", "short", "two words", "tab\tinside"):
            with patch.object(model_install.sys, "stdin", io.StringIO(broken + "\n")):
                with self.assertRaises(EngineError) as thrown:
                    self.install()
            self.assertEqual("tokenInvalid", thrown.exception.code)
        self.assertEqual([], self.server.seen)

    def test_the_stdin_reader_takes_one_exact_line(self):
        with patch.object(model_install.sys, "stdin", io.StringIO(GATED_TOKEN + "\n")):
            self.assertEqual(GATED_TOKEN, model_install._read_token())
        with patch.object(model_install.sys, "stdin", io.StringIO(GATED_TOKEN + "\r\ntrailing")):
            self.assertEqual(GATED_TOKEN, model_install._read_token())

    def test_the_package_summary_reports_the_token_requirement(self):
        summary = {row["id"]: row for row in model_install.packages()}
        self.assertTrue(summary["token-fixture"]["requiresToken"])
        self.assertFalse(summary["flux2-klein-4b-gguf"]["requiresToken"])
        self.assertFalse(summary["qwen-image-edit-2511-gguf"]["requiresToken"])
        for gated in ("flux3-image-klein-gguf", "flux3-image-klein-base-gguf",
                      "flux2-klein-9b-gguf", "longcat-image-edit-gguf", "flux2-dev-gguf"):
            self.assertTrue(summary[gated]["requiresToken"], gated)
        self.assertEqual("FLUX Kommunity License v.1.0", summary["flux3-image-klein-gguf"]["terms"])
        self.assertEqual("BFL FLUX.2 licence", summary["flux2-klein-9b-gguf"]["terms"])
        self.assertEqual("Apache-2.0 · gated BFL autoencoder", summary["longcat-image-edit-gguf"]["terms"])


if __name__ == "__main__":
    unittest.main()
