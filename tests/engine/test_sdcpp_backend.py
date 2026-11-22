"""stable-diffusion.cpp adapter fixtures: tiny standalone files, never weights."""

import hashlib
import io
import json
import os
from contextlib import ExitStack
from pathlib import Path
import platform
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "engine"))
from common import EngineError
from sdcpp_backend import (PACKAGE_BACKENDS, PACKAGE_FILES, RUNNER_SHA256, NATIVE_RUNNER_SHA256,
                           SdcppBackend, SdcppFlux2Backend, SdcppFlux3KleinNative,
                           SdcppFlux3KleinBaseNative, SdcppFlux2Klein9bNative, SdcppQwenEditNative,
                           SdcppLongcatEditNative, SdcppFlux2DevNative, SdcppLladaEditNative, native_device,
                           parse_step_progress, progress, recipe_for_request, replay_runner_output)
from mflux_backend import MfluxBackend
import model_install
import model_library
import model_store


class SdcppPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.package = self.root / "klein-package"
        self.package.mkdir()
        for name in PACKAGE_FILES:
            (self.package / name).write_bytes(b"authored-fixture-" + name.encode())
        (self.package / "klein-LICENSE.md").write_text("fixture terms", encoding="utf-8")

    def tearDown(self):
        self.temporary.cleanup()

    def test_complete_package_is_recognised_and_incomplete_is_not(self):
        self.assertTrue(SdcppBackend.compatible_folder(self.package))
        self.assertFalse(SdcppBackend.compatible_folder(self.root))
        (self.package / PACKAGE_FILES[1]).unlink()
        self.assertFalse(SdcppBackend.compatible_folder(self.package))

    def test_fingerprint_accepts_only_the_reviewed_package_file_types(self):
        value = SdcppBackend().fingerprint(self.package)
        self.assertEqual(64, len(value))
        (self.package / "notes.txt").write_text("fixture note", encoding="utf-8")
        self.assertNotEqual(value, SdcppBackend().fingerprint(self.package))
        (self.package / "start.py").write_text("print('never')", encoding="utf-8")
        with self.assertRaises(EngineError):
            SdcppBackend().fingerprint(self.package)
        (self.package / "start.py").unlink()
        (self.package / "linked.gguf").symlink_to(self.package / PACKAGE_FILES[0])
        with self.assertRaises(EngineError):
            SdcppBackend().fingerprint(self.package)


class SdcppCheckTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.package = self.root / "klein-package"
        self.package.mkdir()
        for name in PACKAGE_FILES:
            (self.package / name).write_bytes(b"authored-fixture")
        self.runner = self.root / "sd-cli"
        self.runner.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        self.runner.chmod(0o700)
        self.digest = hashlib.sha256(self.runner.read_bytes()).hexdigest()

    def tearDown(self):
        self.temporary.cleanup()

    def backend(self):
        return SdcppBackend(runner=self.runner)

    def check(self, directory, backend=None, exit_code=0):
        # Test runner/receipt validation independently of the CI host. The
        # reviewed executable is Linux-only; fixture files are never executed.
        with patch("sdcpp_backend.platform.system", return_value="Linux"), \
             patch("sdcpp_backend.platform.machine", return_value="x86_64"), \
             patch("sdcpp_backend.subprocess.run", return_value=subprocess.CompletedProcess([], exit_code)):
            return (backend or self.backend()).check(directory)

    def test_check_fails_closed_without_a_runner(self):
        result = self.check(self.package, backend=SdcppBackend(runner=self.root / "absent"))
        self.assertFalse(result["available"])
        self.assertEqual("runtimeMissing", result["code"])
        self.assertTrue(result["automaticDownloads"] is False)

    def test_check_rejects_altered_and_broken_runners(self):
        content = self.runner.read_bytes()
        self.runner.write_bytes(content + b"changed")
        self.runner.chmod(0o700)
        self.assertEqual("runtimeMismatch", self.check(self.package)["code"])
        self.runner.write_bytes(content)
        self.runner.chmod(0o700)
        self.runner.write_text("#!/bin/sh\nexit 3\n", encoding="utf-8")
        self.runner.chmod(0o700)
        with patch("sdcpp_backend.RUNNER_SHA256", hashlib.sha256(self.runner.read_bytes()).hexdigest()):
            self.assertEqual("runtimeMismatch", self.check(self.package, exit_code=3)["code"])

    def test_check_reports_missing_components_and_positive_availability(self):
        with patch("sdcpp_backend.RUNNER_SHA256", self.digest):
            self.assertEqual("missingLocalComponents", self.check(self.root / "elsewhere")["code"])
            result = self.check(self.package)
        self.assertTrue(result["available"])
        self.assertEqual("unvalidated", result["acceptance"])
        self.assertEqual(SdcppBackend.identifier, result["backend"])
        self.assertEqual("flux3-image-klein-q4k", result["modelId"])
        self.assertEqual(1572864, result["maximumPixels"])

    def test_check_reads_acceptance_only_from_the_receipt_of_this_profile(self):
        receipt = self.root / "installation.json"
        record = model_store.verify(self.package, receipt, profile=SdcppBackend.profile)
        model_store.accept(receipt, record["digest"], {"backend": SdcppBackend.identifier, "width": 512, "height": 448},
                           profile=SdcppBackend.profile)
        with patch("sdcpp_backend.RUNNER_SHA256", self.digest):
            result = self.check(self.package, backend=SdcppBackend(installation=receipt, runner=self.runner))
        self.assertTrue(result["available"])
        self.assertEqual("nativeEditPassed", result["acceptance"])
        record["digest"] = "0" * 64
        model_store.write_receipt(receipt, record)
        with patch("sdcpp_backend.RUNNER_SHA256", self.digest):
            with self.assertRaises(EngineError):
                self.check(self.package, backend=SdcppBackend(installation=receipt, runner=self.runner))

    def test_unreviewed_platform_is_rejected_before_runner_execution(self):
        for system, architecture in [("Darwin", "arm64"), ("Windows", "AMD64"), ("Linux", "aarch64")]:
            with self.subTest(system=system, architecture=architecture), \
                 patch("sdcpp_backend.platform.system", return_value=system), \
                 patch("sdcpp_backend.platform.machine", return_value=architecture), \
                 patch("sdcpp_backend.subprocess.run") as run:
                self.assertEqual("backendUnavailable", self.backend().check(self.package)["code"])
                run.assert_not_called()

    def test_acceptance_never_crosses_backend_profiles(self):
        receipt = self.root / "installation.json"
        model_store.verify(self.package, receipt, profile=SdcppBackend.profile)
        with self.assertRaises(EngineError):
            model_store.checked(receipt, self.package)  # expects the MFLUX profile
        self.assertEqual(SdcppBackend.profile, model_store.profile_of(receipt))

    def test_runner_command_pins_every_generation_setting(self):
        job = Path("/job")
        command = SdcppBackend.runner_command(self.runner, self.package, job, 512, 448,
                                              dict(SdcppBackend.parameters, steps=16))
        flags = [str(item) for item in command]
        for expected in [str(self.runner), "--diffusion-model", "--llm", "--vae", "-r", "--prompt-file",
                         "-o", "--cfg-scale", "1.0", "--steps", "16", "--sampling-method", "euler",
                         "--seed", "1", "-W", "512", "-H", "448", "--backend", "cpu",
                         "--offload-to-cpu", "--disable-image-metadata",
                         "--preview", "vae", "--preview-path", str(job / "preview.png"),
                         "--preview-interval", "4", "-v"]:
            self.assertIn(expected, flags)
        self.assertEqual(str(self.package / PACKAGE_FILES[0]), flags[flags.index("--diffusion-model") + 1])
        self.assertEqual(str(job / "result.png"), flags[flags.index("-o") + 1])
        self.assertEqual(str(job / "reference.png"), flags[flags.index("-r") + 1])

    def test_generation_identity_is_frozen_in_parameters(self):
        self.assertEqual(RUNNER_SHA256, SdcppBackend.parameters["runnerSha256"])
        self.assertEqual(16, SdcppBackend.parameters["steps"])
        self.assertEqual("cpu", SdcppBackend.parameters["inferenceDevice"])
        self.assertEqual(4 * 60 * 60, SdcppBackend.maximum_edit_seconds)

    def test_runner_shares_the_workers_process_group(self):
        if sys.platform == "win32":
            self.skipTest("POSIX process groups")
        # The coordinator stops the whole reviewed tree with one process-group
        # kill. A runner that opened its own session would survive a cancelled,
        # timed-out or budget-killed worker and keep running unsupervised.
        process = SdcppBackend.spawn_runner(self.root, [sys.executable, "-c", "pass"])
        try:
            self.assertEqual(os.getpgrp(), os.getpgid(process.pid))
        finally:
            process.wait(timeout=10)
            process.stdout.close()

    def test_resident_bytes_accounts_for_spawned_children(self):
        if sys.platform != "linux":
            self.skipTest("Linux /proc budget sampling")
        child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"])
        try:
            own = SdcppBackend.resident_bytes(child.pid)
            self.assertGreater(own, 0)
            parent_watch = SdcppBackend.resident_bytes(os.getpid())
            self.assertGreaterEqual(parent_watch, own)
        finally:
            child.kill()
            child.wait()


class SdcppFlux2Tests(unittest.TestCase):
    """The native FLUX.2 klein profile: its own package, pins and platform gate."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.package = self.root / "flux2-package"
        self.package.mkdir()
        for name in SdcppFlux2Backend.PACKAGE_FILES:
            (self.package / name).write_bytes(b"authored-fixture-" + name.encode())
        self.runner = self.root / "sd-cli"
        self.runner.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        self.runner.chmod(0o700)
        self.digest = hashlib.sha256(self.runner.read_bytes()).hexdigest()

    def tearDown(self):
        self.temporary.cleanup()

    def check(self, directory, backend=None, exit_code=0):
        # A pinned Metal platform trimmed to fixtures; nothing here executes
        # the staged runner.
        with patch("sdcpp_backend.platform.system", return_value="Darwin"), \
             patch("sdcpp_backend.platform.machine", return_value="arm64"), \
             patch("sdcpp_backend.NATIVE_RUNNER_SHA256", {"darwin-arm64": self.digest}), \
             patch("sdcpp_backend.subprocess.run", return_value=subprocess.CompletedProcess([], exit_code)):
            return (backend or SdcppFlux2Backend(runner=self.runner)).check(directory)

    def test_package_files_are_the_installers_reviewed_pins(self):
        # The adapter names exactly the files the one-click installer stages,
        # in transformer / encoder / decoder order; the flux3 package differs.
        reviewed = tuple(file["name"] for file in model_install.PACKAGES["flux2-klein-4b-gguf"]["files"])
        self.assertEqual(reviewed, SdcppFlux2Backend.PACKAGE_FILES)
        self.assertNotEqual(PACKAGE_FILES, SdcppFlux2Backend.PACKAGE_FILES)
        self.assertTrue(SdcppFlux2Backend.compatible_folder(self.package))
        self.assertFalse(SdcppBackend.compatible_folder(self.package))
        (self.package / SdcppFlux2Backend.PACKAGE_FILES[1]).unlink()
        self.assertFalse(SdcppFlux2Backend.compatible_folder(self.package))

    def test_check_gates_on_this_platforms_own_reviewed_runner(self):
        # A platform without an accepted pinned build is unavailable there and
        # the staged runner is never probed.
        with patch("sdcpp_backend.platform.system", return_value="Windows"), \
             patch("sdcpp_backend.platform.machine", return_value="AMD64"), \
             patch("sdcpp_backend.subprocess.run") as run:
            result = SdcppFlux2Backend(runner=self.runner).check(self.package)
        self.assertFalse(result["available"])
        self.assertEqual("backendUnavailable", result["code"])
        run.assert_not_called()

    def test_check_reports_the_native_profile_and_reads_acceptance_from_its_receipt(self):
        self.assertEqual("missingLocalComponents", self.check(self.root / "elsewhere")["code"])
        result = self.check(self.package)
        self.assertTrue(result["available"])
        self.assertEqual("unvalidated", result["acceptance"])
        self.assertEqual(SdcppFlux2Backend.identifier, result["backend"])
        self.assertEqual(SdcppFlux2Backend.model_id, result["modelId"])
        self.assertEqual(1048576, result["maximumPixels"])
        self.assertEqual(64, result["frameGrid"])
        receipt = self.root / "installation.json"
        record = model_store.verify(self.package, receipt, profile=SdcppFlux2Backend.profile)
        model_store.accept(receipt, record["digest"],
                           {"backend": SdcppFlux2Backend.identifier, "width": 512, "height": 448},
                           profile=SdcppFlux2Backend.profile)
        accepted = self.check(self.package, backend=SdcppFlux2Backend(installation=receipt, runner=self.runner))
        self.assertTrue(accepted["available"])
        self.assertEqual("nativeEditPassed", accepted["acceptance"])

    def test_runner_command_pins_the_reviewed_files_and_distilled_schedule(self):
        frozen = dict(SdcppFlux2Backend.parameters, seed=7, inferenceDevice="metal")
        job = Path("/job")
        flags = [str(item) for item in
                 SdcppFlux2Backend.runner_command(self.runner, self.package, job, 512, 448, frozen)]
        for expected in [str(self.runner), "--diffusion-model", "--llm", "--vae", "-r", "--prompt-file",
                         "-o", "--cfg-scale", "1.0", "--steps", "--sampling-method", "euler", "--seed",
                         "-W", "512", "-H", "448", "--backend", "--offload-to-cpu",
                         "--disable-image-metadata", "-v"]:
            self.assertIn(expected, flags)
        self.assertEqual(str(self.package / SdcppFlux2Backend.PACKAGE_FILES[0]), flags[flags.index("--diffusion-model") + 1])
        self.assertEqual(str(self.package / SdcppFlux2Backend.PACKAGE_FILES[1]), flags[flags.index("--llm") + 1])
        self.assertEqual(str(self.package / SdcppFlux2Backend.PACKAGE_FILES[2]), flags[flags.index("--vae") + 1])
        self.assertEqual("4", flags[flags.index("--steps") + 1])
        self.assertEqual("metal", flags[flags.index("--backend") + 1])
        self.assertEqual(str(job / "reference.png"), flags[flags.index("-r") + 1])
        self.assertEqual(str(job / "result.png"), flags[flags.index("-o") + 1])

    def test_generation_identity_is_frozen_in_parameters(self):
        self.assertEqual(4, SdcppFlux2Backend.parameters["steps"])
        self.assertEqual(1.0, SdcppFlux2Backend.parameters["cfgScale"])
        self.assertEqual("euler", SdcppFlux2Backend.parameters["samplingMethod"])
        self.assertEqual("flux2", SdcppFlux2Backend.parameters["referencePreset"])
        self.assertEqual(7 * 1024 ** 3, SdcppFlux2Backend.maximum_resident_bytes)
        self.assertEqual(4 * 60 * 60, SdcppFlux2Backend.maximum_edit_seconds)
        # The recorded identity follows this platform's own pin and device.
        key = f"{platform.system().lower()}-{platform.machine().lower()}"
        self.assertEqual(NATIVE_RUNNER_SHA256.get(key), SdcppFlux2Backend.parameters["runnerSha256"])
        self.assertEqual(native_device(platform.system(), platform.machine()),
                         SdcppFlux2Backend.parameters["inferenceDevice"])

    def test_platform_readiness_needs_this_platforms_pin_and_the_staged_runner(self):
        with patch("sdcpp_backend.platform.system", return_value="Windows"), \
             patch("sdcpp_backend.platform.machine", return_value="AMD64"), \
             patch("sdcpp_backend.runner_path", return_value=self.runner):
            with patch("sdcpp_backend.NATIVE_RUNNER_SHA256", {"windows-x86_64": "f" * 64}):
                self.assertTrue(SdcppFlux2Backend.platform_ready())
            with patch("sdcpp_backend.NATIVE_RUNNER_SHA256", {"windows-x86_64": None}):
                self.assertFalse(SdcppFlux2Backend.platform_ready())
        with patch("sdcpp_backend.platform.system", return_value="Darwin"), \
             patch("sdcpp_backend.platform.machine", return_value="arm64"), \
             patch("sdcpp_backend.NATIVE_RUNNER_SHA256", {"darwin-arm64": "f" * 64}), \
             patch("sdcpp_backend.runner_path", return_value=self.root / "absent"):
            self.assertFalse(SdcppFlux2Backend.platform_ready())


class BackendSelectionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.receipt = self.root / "installation.json"

    def tearDown(self):
        self.temporary.cleanup()

    def write_receipt(self, profile):
        model_store.write_receipt(self.receipt, {"format": 1, "profile": profile,
                                                 "directory": str(self.root), "files": [],
                                                 "digest": "0" * 64})

    def test_receipt_profile_selects_the_named_adapter(self):
        from engine import BACKENDS, select_backend
        self.assertEqual((MfluxBackend, SdcppBackend, *PACKAGE_BACKENDS), BACKENDS)
        self.write_receipt("mflux-0.22.0-klein-4b-q4")
        self.assertIsInstance(select_backend(self.receipt), MfluxBackend)
        self.write_receipt("sdcpp-80c4707-klein-q4k")
        selected = select_backend(self.receipt)
        self.assertIsInstance(selected, SdcppBackend)
        self.assertEqual(self.receipt, selected.installation)
        self.assertEqual(SdcppBackend.profile, selected.profile)
        self.write_receipt("sdcpp-80c4707-flux2-klein-4b")
        selected = select_backend(self.receipt)
        self.assertIsInstance(selected, SdcppFlux2Backend)
        self.assertEqual(SdcppFlux2Backend.profile, selected.profile)
        self.write_receipt("sdcpp-80c4707-qwen-image-edit")
        selected = select_backend(self.receipt)
        self.assertIsInstance(selected, SdcppQwenEditNative)
        self.assertEqual(SdcppQwenEditNative.profile, selected.profile)

    def test_unknown_profile_fails_closed(self):
        from engine import select_backend
        self.write_receipt("some-other-engine")
        with self.assertRaises(EngineError):
            select_backend(self.receipt)

    def test_without_receipt_only_a_ready_platform_is_selected(self):
        from engine import select_backend
        # No adapter reports its platform ready, so nothing serves at all;
        # then exactly one gate opens and its own adapter answers — a
        # platform-ready family member is never silently substituted.
        with ExitStack() as gates:
            for backend in (MfluxBackend, SdcppBackend, *PACKAGE_BACKENDS):
                gates.enter_context(patch.object(backend, "platform_ready", return_value=False))
            with self.assertRaises(EngineError):
                select_backend()
        with ExitStack() as gates:
            gates.enter_context(patch.object(MfluxBackend, "platform_ready", return_value=False))
            gates.enter_context(patch.object(SdcppBackend, "platform_ready", return_value=False))
            for backend in PACKAGE_BACKENDS:
                gates.enter_context(patch.object(backend, "platform_ready", return_value=False))
            gates.enter_context(patch.object(SdcppQwenEditNative, "platform_ready", return_value=True))
            self.assertIsInstance(select_backend(), SdcppQwenEditNative)


class LibraryGgufPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.package = self.root / "klein-package"
        self.package.mkdir()
        for name in PACKAGE_FILES:
            (self.package / name).write_bytes(b"authored-fixture")
        self.library = model_library.Library(self.root / "private/models.json")

    def tearDown(self):
        self.temporary.cleanup()

    def test_flat_gguf_package_is_a_candidate_with_the_sdcpp_layout(self):
        result = model_library.inspect(self.package)
        self.assertTrue(result["candidate"])
        self.assertEqual("stable-diffusion.cpp FLUX.3 klein Q4_K package", result["layout"])
        self.assertEqual(3, result["fileCount"])
        (self.package / PACKAGE_FILES[2]).unlink()
        incomplete = model_library.inspect(self.package)
        self.assertFalse(incomplete["candidate"])
        self.assertIn("Missing local components", incomplete["message"])

    def test_plan_and_list_route_the_gguf_layout_to_the_sdcpp_adapter(self):
        saved = self.library.add(self.package)
        self.assertTrue(saved["candidate"])
        with patch.object(SdcppBackend, "check", return_value={"available": True, "acceptance": "unlisted"}) as check:
            listing = self.library.list(None, None)["models"]
            self.assertEqual(1, len(listing))
            self.assertFalse(listing[0].get("configured"))
            plan = self.library.plan(saved["id"])
        self.assertEqual(1, check.call_count)
        steps = {step["name"]: step for step in plan["steps"]}
        self.assertIn("Local runtime", steps)
        self.assertEqual("verificationNeeded", plan["state"])
        self.assertFalse(plan["modelAccepted"])
        # The configured ready flow routes through the same adapter and receipt.
        receipt = self.root / "installation.json"
        model_store.verify(self.package, receipt, profile=SdcppBackend.profile)
        model_store.accept(receipt, model_store.checked(receipt, self.package, profile=SdcppBackend.profile)["digest"],
                           {"backend": SdcppBackend.identifier, "width": 512, "height": 448}, profile=SdcppBackend.profile)
        with patch.object(SdcppBackend, "check", return_value={"available": True, "acceptance": "nativeEditPassed",
                                                               "modelDigest": "0" * 64}):
            listing = self.library.list(saved["directory"], receipt)["models"]
            plan = self.library.plan(saved["id"], saved["directory"], receipt)
        self.assertEqual("ready", listing[0]["readiness"])
        self.assertTrue(plan["modelAccepted"])


class SdcppProgressTests(unittest.TestCase):
    """The runner's own sampling line becomes live client progress; every byte
    stays in the private runner.txt exactly as the runner wrote it."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.job = Path(self.temporary.name).resolve() / "job"
        self.job.mkdir()

    def tearDown(self):
        self.temporary.cleanup()

    def test_step_markers_read_the_real_runner_format(self):
        line = b"\r  |============>                                     | 1/4 -  67.56s/it - ETA 03:22\x1b[K"
        self.assertEqual({"step": 1, "steps": 4, "secondsPerStep": 67.56, "etaSeconds": 202},
                         parse_step_progress(line))

    def test_the_last_complete_marker_wins_and_partial_tails_are_ignored(self):
        self.assertIsNone(parse_step_progress(b"\r  |===== 1/4 -  6"))
        both = b"\r|== 1/4 -  67.56s/it - ETA 03:22\x1b[K\r|===== 2/4 -  67.56s/it - ETA 02:15"
        self.assertEqual(2, parse_step_progress(both)["step"])

    def test_markers_the_runner_cannot_honestly_report_are_discarded(self):
        self.assertIsNone(parse_step_progress(b"5/4 -  67.56s/it - ETA 03:22"))
        self.assertIsNone(parse_step_progress(b"1/4 -  0.00s/it - ETA 03:22"))
        self.assertIsNone(parse_step_progress(b"1/4 -  67.56s/it - ETA 999:99"))
        self.assertIsNone(parse_step_progress(b"ggml_metal_library_init: using embedded metal library"))

    def test_replay_keeps_every_log_byte_and_records_only_new_steps(self):
        class Chunked:
            def __init__(self, chunks):
                self.chunks = iter(chunks)

            def read1(self, size):
                return next(self.chunks, b"")

        # A marker split across chunk boundaries must still complete; a redraw
        # of the same step must not rewrite the record.
        chunks = [
            b"[V] flux compute buffer size: 27.31 MB(RAM) on CPU --- ggml_runner.cpp:1094\r  |====  1/4 -  67.",
            b"56s/it - ETA 03:22\x1b[K\x1b[K\r  |====  1/4 -  67.5s/it - ETA 03:21\x1b[K",
            b"\r  |====  2/4 -  66.9s/it - ETA 02:13\x1b[K",
            b"\r  |======  4/4 -  66.9s/it - ETA 00:00\x1b[K[V] saved image",
        ]
        log = io.BytesIO()
        replay_runner_output(self.job, log, types.SimpleNamespace(stdout=Chunked(chunks)))
        self.assertEqual(b"".join(chunks), log.getvalue())
        record = json.loads((self.job / "progress.json").read_text(encoding="utf-8"))
        self.assertEqual("generating", record["phase"])
        self.assertEqual(4, record["step"])
        self.assertEqual(4, record["steps"])
        self.assertTrue("startedAt" in record)

    def test_progress_record_carries_the_phase_and_optional_details(self):
        progress(self.job, "loadingModel")
        first = json.loads((self.job / "progress.json").read_text(encoding="utf-8"))
        self.assertEqual("loadingModel", first["phase"])
        progress(self.job, "generating", step=1, steps=4, secondsPerStep=67.56, etaSeconds=202)
        second = json.loads((self.job / "progress.json").read_text(encoding="utf-8"))
        self.assertEqual(("generating", 1, 4, 67.56, 202),
                         (second["phase"], second["step"], second["steps"],
                          second["secondsPerStep"], second["etaSeconds"]))


if __name__ == "__main__":
    unittest.main()


class NativeFamilyTests(unittest.TestCase):
    """The one-click profiles on the shared native runner: each declares a
    package, a role map and a frozen recipe; the family mechanism itself is
    under the flux2 and prototype tests above."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.runner = Path(self.temporary.name).resolve() / "sd-cli"
        self.runner.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        self.runner.chmod(0o700)

    def tearDown(self):
        self.temporary.cleanup()

    def test_every_family_member_describes_its_own_installers_package(self):
        for profile in PACKAGE_BACKENDS:
            with self.subTest(profile=profile.model_id):
                record = model_install.PACKAGES[profile.PACKAGE_ID]
                self.assertEqual(record["profile"], profile.profile)
                self.assertEqual(tuple(file["name"] for file in record["files"]), profile.PACKAGE_FILES)

    def test_registering_every_downloadable_package_once(self):
        downloadable = {package_id for package_id in model_install.PACKAGES
                        if model_install.PACKAGES[package_id].get("files")}
        self.assertEqual({profile.PACKAGE_ID for profile in PACKAGE_BACKENDS}, downloadable)
        self.assertEqual(len({profile.model_id for profile in PACKAGE_BACKENDS}), len(PACKAGE_BACKENDS))
        self.assertEqual(len({profile.profile for profile in PACKAGE_BACKENDS}), len(PACKAGE_BACKENDS))
        self.assertEqual(len({profile.identifier for profile in PACKAGE_BACKENDS}), len(PACKAGE_BACKENDS))
        # Disjoint claims: one folder layout, one adapter.
        transformers = [profile.PACKAGE_FILES[0] for profile in PACKAGE_BACKENDS]
        self.assertEqual(len(transformers), len(set(transformers)))

    def test_frozen_recipes_are_the_runner_documentation(self):
        recipes = {
            SdcppFlux2Backend: (4, 1.0, (1, 16)),
            SdcppFlux3KleinNative: (16, 1.0, (1, 40)),
            SdcppFlux3KleinBaseNative: (20, 3.5, (4, 60)),
            SdcppFlux2Klein9bNative: (4, 1.0, (1, 16)),
            SdcppQwenEditNative: (20, 2.5, (4, 60)),
            SdcppLongcatEditNative: (8, 1.0, (1, 24)),
            SdcppFlux2DevNative: (20, 1.0, (4, 60)),
            SdcppLladaEditNative: (4, 1.0, (1, 16)),
        }
        for profile, (steps, cfg, bounds) in recipes.items():
            with self.subTest(profile=profile.model_id):
                self.assertEqual(steps, profile.parameters["steps"])
                self.assertEqual(cfg, profile.parameters["cfgScale"])
                self.assertEqual("euler", profile.parameters["samplingMethod"])
                self.assertEqual(bounds, profile.STEP_BOUNDS)
                low, high = bounds
                self.assertTrue(low <= steps <= high)
                self.assertGreater(profile.maximum_resident_bytes, 0)

    def test_family_member_commands_name_their_files_by_role(self):
        job = Path("/job")
        cases = [
            (SdcppFlux2Backend, None),
            (SdcppFlux3KleinNative, None),
            (SdcppFlux3KleinBaseNative, None),
            (SdcppFlux2Klein9bNative, None),
            (SdcppQwenEditNative, "llm_vision"),
            (SdcppLongcatEditNative, "llm_vision"),
            (SdcppFlux2DevNative, None),
        ]
        for profile, vision in cases:
            with self.subTest(profile=profile.model_id):
                package = Path("/package") / profile.PACKAGE_ID
                flags = [str(item) for item in
                         profile.runner_command(self.runner, package, job, 512, 448, dict(profile.parameters))]
                self.assertEqual(str(package / profile.PACKAGE_FILES[0]),
                                 flags[flags.index("--diffusion-model") + 1])
                self.assertEqual(str(package / profile.PACKAGE_FILES[profile.ROLES["llm"]]),
                                 flags[flags.index("--llm") + 1])
                self.assertEqual(str(package / profile.PACKAGE_FILES[profile.ROLES["vae"]]),
                                 flags[flags.index("--vae") + 1])
                if vision is None:
                    self.assertNotIn("--llm-vision", flags)
                else:
                    self.assertEqual(str(package / profile.PACKAGE_FILES[profile.ROLES["llm_vision"]]),
                                     flags[flags.index("--llm-vision") + 1])
                # The bounded live preview: same contract for every member.
                self.assertEqual("vae", flags[flags.index("--preview") + 1])
                self.assertEqual(str(job / "preview.png"), flags[flags.index("--preview-path") + 1])
                self.assertEqual(str(max(1, profile.parameters["steps"] // 4)),
                                 flags[flags.index("--preview-interval") + 1])
                self.assertEqual(str(profile.parameters["steps"]), flags[flags.index("--steps") + 1])

    def test_the_llada_family_passes_its_own_files_by_role(self):
        flags = [str(item) for item in
                 SdcppLladaEditNative.runner_command(self.runner, Path("/p"), Path("/job"), 512, 448,
                                                     dict(SdcppLladaEditNative.parameters))]
        package = Path("/p")
        self.assertEqual(str(package / SdcppLladaEditNative.PACKAGE_FILES[3]),
                         flags[flags.index("--tokenizer") + 1])
        self.assertEqual(str(package / SdcppLladaEditNative.PACKAGE_FILES[2]),
                         flags[flags.index("--embeddings-connectors") + 1])
        for other in PACKAGE_BACKENDS:
            if other is SdcppLladaEditNative:
                continue
            with self.subTest(profile=other.model_id):
                member = [str(item) for item in
                          other.runner_command(self.runner, package, Path("/job"), 512, 448, dict(other.parameters))]
                self.assertNotIn("--tokenizer", member)
                self.assertNotIn("--embeddings-connectors", member)

    def test_documented_family_flags_are_frozen(self):
        job = Path("/job")
        # Qwen 2511: the zero-cond-t conditioning is part of the recipe; with it
        # off the runner documents visibly degraded edits.
        qwen = [str(item) for item in
                SdcppQwenEditNative.runner_command(self.runner, Path("/p"), job, 512, 448,
                                                   dict(SdcppQwenEditNative.parameters))]
        self.assertEqual("--flow-shift", qwen[qwen.index("--flow-shift")])
        self.assertEqual("3", qwen[qwen.index("--flow-shift") + 1])
        self.assertEqual("qwen_image_zero_cond_t=true", qwen[qwen.index("--model-args") + 1])
        # LongCat: the runner's own family flag, no model args.
        longcat = [str(item) for item in
                   SdcppLongcatEditNative.runner_command(self.runner, Path("/p"), job, 512, 448,
                                                         dict(SdcppLongcatEditNative.parameters))]
        self.assertEqual("3", longcat[longcat.index("--flow-shift") + 1])
        self.assertNotIn("--model-args", longcat)

    def test_steps_override_stays_inside_the_recipes_bounds(self):
        for profile in PACKAGE_BACKENDS:
            with self.subTest(profile=profile.model_id):
                frozen = profile.parameters
                # The default request runs the frozen recipe unchanged.
                self.assertEqual(frozen, recipe_for_request(profile, dict(frozen)))
                low, high = profile.STEP_BOUNDS
                effective = recipe_for_request(profile, dict(frozen, steps=high))
                self.assertEqual(high, effective["steps"])
                with self.assertRaises(ValueError) as rejected:
                    recipe_for_request(profile, dict(frozen, steps=high + 1))
                self.assertEqual("stepsOutsideRecipe", str(rejected.exception))
                with self.assertRaises(ValueError) as rejected:
                    recipe_for_request(profile, dict(frozen, steps=low - 1))
                self.assertEqual("stepsOutsideRecipe", str(rejected.exception))
                # Anything else the request changed stays refused.
                with self.assertRaises(ValueError) as rejected:
                    recipe_for_request(profile, dict(frozen, steps=frozen["steps"], cfgScale=9.0))
                self.assertEqual("unsupportedRuntime", str(rejected.exception))
                with self.assertRaises(ValueError):
                    recipe_for_request(profile, None)

    def test_family_checks_report_their_own_recipe_and_nothing_without_a_pin(self):
        digest = hashlib.sha256(self.runner.read_bytes()).hexdigest()
        for profile in PACKAGE_BACKENDS:
            with self.subTest(profile=profile.model_id):
                package = Path(self.temporary.name).resolve() / profile.PACKAGE_ID
                package.mkdir(exist_ok=True)
                for name in profile.PACKAGE_FILES:
                    (package / name).write_bytes(b"authored-fixture-" + name.encode())
                # A platform this profile has no reviewed pin for advertises nothing.
                with patch("sdcpp_backend.platform.system", return_value="Windows"), \
                     patch("sdcpp_backend.platform.machine", return_value="AMD64"), \
                     patch("sdcpp_backend.NATIVE_RUNNER_SHA256", {"windows-x86_64": None}):
                    self.assertEqual("backendUnavailable", profile(runner=self.runner).check(package)["code"])
                # With this platform's pin and running staging, the check names
                # this profile's own model, recipe and bounded override.
                with patch("sdcpp_backend.platform.system", return_value="Windows"), \
                     patch("sdcpp_backend.platform.machine", return_value="AMD64"), \
                     patch("sdcpp_backend.NATIVE_RUNNER_SHA256", {"windows-x86_64": digest}):
                    result = profile(runner=self.runner).check(package)
                    self.assertTrue(result["available"])
                    self.assertEqual(profile.model_id, result["modelId"])
                    self.assertEqual(profile.identifier, result["backend"])
                    self.assertEqual(list(profile.STEP_BOUNDS), result["stepsBounds"])
                    self.assertEqual(profile.parameters["steps"], result["recipeSteps"])
                    self.assertEqual("unvalidated", result["acceptance"])
                # The installer's own package message stays honest about what to choose.
                self.assertTrue(profile.PACKAGE_MISSING_MESSAGE.strip().endswith("Nothing was downloaded."))
