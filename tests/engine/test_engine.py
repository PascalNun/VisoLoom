"""Control-process acceptance tests; fixture images are not model generations."""

from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid
import zlib

SOURCE = Path(__file__).resolve().parents[2] / "engine"
sys.path.insert(0, str(SOURCE))
from common import EngineError, WorkerLock
from engine import Engine, OBJECTIVE, read_json, worker_environment, write_json
from mflux_backend import MfluxBackend, count_prompt, run


def png(width=128, height=128, colour=b"\xff\xff\xff"):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress((b"\0" + colour * width) * height)) + chunk(b"IEND", b""))


FIXTURE_WORKER = '''
import hashlib, json, os, pathlib, sys, time
job = pathlib.Path(sys.argv[1])
mode = sys.argv[2]
(job / "started.json").write_text(json.dumps({"pid": os.getpid()}))
if mode == "wait":
    time.sleep(60)
if mode == "fail":
    (job / "failure.json").write_text(json.dumps({"code":"promptTooLong"}))
    sys.exit(1)
source = (job / "reference.png").read_bytes()
output = pathlib.Path(sys.argv[3]).read_bytes()
(job / "result.png").write_bytes(output)
proof = {"sourceSha256": hashlib.sha256(source).hexdigest(), "outputSha256": hashlib.sha256(output).hexdigest()}
if mode == "wrong-proof":
    proof["sourceSha256"] = "0" * 64
(job / "result.json").write_text(json.dumps(proof))
'''


class FixtureBackend:
    identifier = "fixture-no-model"
    model_id = "fixture-no-model"
    parameters = {"fixture": True}
    maximum_resident_bytes = 1024
    memory = 0

    def resident_bytes(self, pid):
        return self.memory

    def __init__(self, script, output, mode="success"):
        self.script, self.output, self.mode = script, output, mode

    def fingerprint(self, root):
        return hashlib.sha256((root / "model.txt").read_bytes()).hexdigest()

    def check(self, path):
        return {"available": True}

    def command(self, job):
        return [sys.executable, "-I", str(self.script), str(job), self.mode, str(self.output)]


class EngineTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.base = Path(self.temporary.name).resolve()
        self.model = self.base / "model"
        self.model.mkdir()
        (self.model / "model.txt").write_text("fixture model identity")
        self.reference = self.base / "reference.png"
        self.reference.write_bytes(png())
        self.output = self.base / "fixture.png"
        self.output.write_bytes(png(colour=b"\x33\x33\x33"))
        self.prompt = self.base / "prompt.txt"
        self.prompt.write_text(OBJECTIVE + "\nDraw a restrained architectural sketch.", encoding="utf-8")
        self.script = self.base / "fixture_worker.py"
        self.script.write_text(FIXTURE_WORKER)
        self.backend = FixtureBackend(self.script, self.output)
        self.engine = Engine(self.base / "jobs", self.backend, timeout_seconds=5)
        self.job_id = str(uuid.uuid4())

    def tearDown(self):
        self.temporary.cleanup()

    def edit(self, job_id=None):
        return self.engine.edit(job_id or self.job_id, self.reference, self.prompt, self.model)

    def assert_code(self, code, function):
        with self.assertRaises(EngineError) as error:
            function()
        self.assertEqual(code, error.exception.code)

    def wait_started(self):
        job = self.engine.job_directory(self.job_id)
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            if (job / "started.json").exists():
                return job
            time.sleep(0.01)
        self.fail("Fixture worker did not start")

    def test_completed_job_has_frozen_inputs_and_verifiable_output(self):
        result = self.edit()
        self.assertEqual("completed", result["state"])
        job = self.engine.job_directory(self.job_id)
        self.assertEqual(self.reference.read_bytes(), (job / "reference.png").read_bytes())
        self.assertEqual(self.prompt.read_bytes().decode("utf-8"), read_json(job / "request.json")["prompt"])
        self.assertEqual(hashlib.sha256(self.output.read_bytes()).hexdigest(), result["outputSha256"])
        self.assertNotEqual(result["outputSha256"], result["sourceSha256"])
        self.assertEqual(["reference"], result["consumedPasses"])
        if os.name != "nt":
            self.assertEqual(0o700, job.stat().st_mode & 0o777)
            self.assertEqual(0o600, (job / "request.json").stat().st_mode & 0o777)

    def test_same_identity_returns_record_without_starting_again(self):
        first = self.edit()
        with patch("subprocess.Popen", side_effect=AssertionError("Must not restart")):
            self.assertEqual(first, self.edit())
        restarted = Engine(self.engine.root, self.backend)
        self.assertEqual(first, restarted.status(self.job_id))

    def test_changed_prompt_or_model_conflicts_without_starting(self):
        self.edit()
        self.prompt.write_text(OBJECTIVE + "\nChanged instructions")
        self.assert_code("idempotencyConflict", self.edit)
        self.prompt.write_text(OBJECTIVE + "\nDraw a restrained architectural sketch.")
        (self.model / "model.txt").write_text("different weights")
        self.assert_code("idempotencyConflict", self.edit)

    def test_bad_prompt_and_frame_never_start_a_worker(self):
        with patch("subprocess.Popen", side_effect=AssertionError("Must not start")):
            self.prompt.write_text("No objective")
            self.assert_code("invalidPrompt", self.edit)
            self.prompt.write_text(OBJECTIVE + "x" * 2048)
            self.assert_code("inputTooLarge", self.edit)
            self.prompt.write_text(OBJECTIVE)
            self.reference.write_bytes(png(129, 128))
            self.assert_code("unsupportedFrame", self.edit)
            self.reference.write_bytes(b"not a PNG")
            self.assert_code("invalidImage", self.edit)

    def test_cancel_waits_for_process_exit_and_discards_output(self):
        self.backend.mode = "wait"
        with ThreadPoolExecutor(1) as pool:
            future = pool.submit(self.edit)
            job = self.wait_started()
            pid = read_json(job / "started.json")["pid"]
            response = self.engine.cancel(self.job_id)
            self.assertTrue(response["cancelRequested"])
            result = future.result(timeout=5)
        self.assertEqual("cancelled", result["state"])
        self.assertFalse((job / "result.png").exists())
        if os.name != "nt":
            with self.assertRaises(ProcessLookupError):
                os.kill(pid, 0)
        self.assertEqual(result, self.engine.cancel(self.job_id))

    def test_timeout_terminates_worker_and_never_retries(self):
        self.backend.mode = "wait"
        self.engine.timeout_seconds = 0.2
        result = self.edit()
        self.assertEqual(("failed", "timeout"), (result["state"], result["failure"]))
        with patch("subprocess.Popen", side_effect=AssertionError("Must not retry")):
            self.assertEqual(result, self.edit())

    def test_memory_budget_stops_worker_and_discards_output(self):
        self.backend.mode = "wait"
        self.backend.memory = 2048
        result = self.edit()
        self.assertEqual(("failed", "memoryBudget"), (result["state"], result["failure"]))
        self.assertEqual(2048, result["observedResidentBytes"])
        self.assertFalse((self.engine.job_directory(self.job_id) / "result.png").exists())

    def test_only_one_job_runs_and_recovery_cannot_touch_live_job(self):
        self.backend.mode = "wait"
        with ThreadPoolExecutor(1) as pool:
            future = pool.submit(self.edit)
            self.wait_started()
            self.assert_code("engineBusy", lambda: self.edit(str(uuid.uuid4())))
            other = Engine(self.engine.root, self.backend)
            self.assert_code("engineBusy", lambda: WorkerLock(other.root).__enter__())
            self.engine.cancel(self.job_id)
            self.assertEqual("cancelled", future.result(timeout=5)["state"])

    def test_recovery_marks_abandoned_job_failed_without_resubmission(self):
        result = self.edit()
        job = self.engine.job_directory(self.job_id)
        write_json(job / "state.json", dict(result, state="running"))
        with WorkerLock(self.engine.root):
            self.engine.recover()
        self.assertEqual("coordinatorStopped", self.engine.status(self.job_id)["failure"])
        with patch("subprocess.Popen", side_effect=AssertionError("Must not restart")):
            self.assertEqual("failed", self.edit()["state"])

    def test_recovery_waits_for_an_orphan_inference_lock(self):
        with WorkerLock(self.engine.root, ".inference.lock"):
            self.assert_code("engineBusy", self.engine.recover)

    def test_worker_failure_invalid_proof_and_wrong_frame_are_terminal(self):
        for mode, output, failure in [("fail", png(), "promptTooLong"), ("wrong-proof", png(), "workerFailed"),
                                      ("success", png(256, 128), "workerFailed")]:
            with self.subTest(mode=mode, failure=failure):
                self.backend.mode = mode
                self.output.write_bytes(output)
                job_id = str(uuid.uuid4())
                result = self.edit(job_id)
                self.assertEqual(("failed", failure), (result["state"], result["failure"]))
                self.assertFalse((self.engine.job_directory(job_id) / "result.png").exists())

    def test_storage_traversal_and_unknown_jobs_rejected(self):
        self.assert_code("invalidJobId", lambda: self.engine.status("../reference"))
        self.assert_code("jobNotFound", lambda: self.engine.cancel(str(uuid.uuid4())))
        if os.name != "nt":
            link = self.base / "link"
            link.symlink_to(self.engine.root, target_is_directory=True)
            self.assert_code("invalidStorage", lambda: Engine(link, self.backend))

    def test_worker_environment_excludes_credentials_and_custom_imports(self):
        with patch.dict(os.environ, {"OPENAI_API_KEY": "test-secret", "HF_TOKEN": "test-secret", "PYTHONPATH": "foreign"}):
            environment = worker_environment(self.engine.root)
        self.assertFalse({"OPENAI_API_KEY", "HF_TOKEN", "PYTHONPATH"} & environment.keys())
        self.assertEqual("1", environment["HF_HUB_OFFLINE"])
        self.assertEqual("1", environment["TRANSFORMERS_OFFLINE"])


class BackendTests(unittest.TestCase):
    def test_model_identity_covers_all_components_and_rejects_executable_formats(self):
        with tempfile.TemporaryDirectory() as temporary:
            model = Path(temporary).resolve()
            for component in ["transformer", "text_encoder", "vae"]:
                (model / component).mkdir()
                (model / component / "model.safetensors").write_bytes(b"fixture weights")
            (model / "tokenizer").mkdir()
            (model / "tokenizer/tokenizer_config.json").write_text("{}")
            self.assertTrue(MfluxBackend.compatible_folder(model))
            first = MfluxBackend.fingerprint(model)
            (model / "text_encoder/model.safetensors").write_bytes(b"changed encoder")
            self.assertNotEqual(first, MfluxBackend.fingerprint(model))
            (model / "model.pkl").write_bytes(b"not allowed")
            with self.assertRaises(EngineError) as error:
                MfluxBackend.fingerprint(model)
            self.assertEqual("invalidModel", error.exception.code)

    def test_check_has_no_ml_imports_or_downloads_and_windows_is_explicit(self):
        with patch("platform.system", return_value="Windows"):
            result = MfluxBackend().check()
        self.assertFalse(result["available"])
        self.assertEqual("backendUnavailable", result["code"])
        self.assertFalse(result["automaticDownloads"])
        self.assertNotIn("mlx.core", sys.modules)

    def test_worker_runs_isolated_without_shell_or_foreign_module_path(self):
        command = MfluxBackend.command(Path("/private/job"))
        self.assertEqual([sys.executable, "-I"], command[:2])
        self.assertEqual(SOURCE / "mflux_backend.py", Path(command[2]))

    def test_token_budget_checks_complete_formatted_prompt_without_truncation(self):
        class Tokenizer:
            tokens = 512
            def apply_chat_template(self, messages, **kwargs):
                self.messages, self.template_options = messages, kwargs
                return "formatted"
            def __call__(self, text, **kwargs):
                self.options = kwargs
                return {"input_ids": [1] * self.tokens}
        tokenizer = Tokenizer()
        self.assertEqual(512, count_prompt(tokenizer, "user words"))
        self.assertFalse(tokenizer.options["truncation"])
        self.assertEqual("user words", tokenizer.messages[0]["content"])
        tokenizer.tokens = 513
        with self.assertRaisesRegex(ValueError, "promptTooLong"):
            count_prompt(tokenizer, "user words")

    def test_adapter_uses_edit_pipeline_one_reference_and_pixel_only_export(self):
        calls = {}
        class Pixels:
            format, size, n_frames = "PNG", (128, 128), 1
            def __init__(self):
                self.info = {"source-path": "private-test-path"}
            def __enter__(self):
                return self
            def __exit__(self, *args):
                pass
            def load(self):
                pass
            def copy(self):
                return Pixels()
            def save(self, path, **kwargs):
                calls["exportInfo"] = self.info.copy()
                Path(path).write_bytes(png(colour=b"\x33\x33\x33"))
        class Tokenizer:
            def apply_chat_template(self, *args, **kwargs):
                return "full formatted prompt"
            def __call__(self, text, **kwargs):
                self.options = kwargs
                return {"input_ids": [1] * 400}
        tokenizer = Tokenizer()
        class Model:
            bits = 4
            def __init__(self, **kwargs):
                calls["load"] = kwargs
                self.tokenizers = {"qwen3": SimpleNamespace(tokenizer=tokenizer, max_length=512,
                    use_chat_template=True, template=None, chat_template_kwargs={"enable_thinking": False}, add_special_tokens=True)}
            def generate_image(self, **kwargs):
                calls["edit"] = kwargs
                return SimpleNamespace(image=Pixels())
        core = SimpleNamespace(float16="float16", set_memory_limit=lambda value: None,
                               set_cache_limit=lambda value: None, get_peak_memory=lambda: 1024)
        modules = {"PIL": SimpleNamespace(Image=SimpleNamespace(open=lambda path: Pixels())),
                   "mlx": SimpleNamespace(core=core), "mlx.core": core,
                   "transformers": SimpleNamespace(Qwen2TokenizerFast=SimpleNamespace(from_pretrained=lambda *a, **k: tokenizer)),
                   "mflux.models.common.config": SimpleNamespace(ModelConfig=SimpleNamespace(flux2_klein_4b=lambda: "explicit-4b")),
                   "mflux.models.flux2.variants": SimpleNamespace(Flux2KleinEdit=Model)}
        with tempfile.TemporaryDirectory() as temporary:
            job = Path(temporary).resolve()
            write_json(job / "request.json", {"modelId": MfluxBackend.model_id, "backend": MfluxBackend.identifier,
                "parameters": MfluxBackend.parameters, "modelDirectory": str(job / "model"), "modelDigest": "fixed-test-digest",
                "width": 128, "height": 128, "prompt": OBJECTIVE})
            (job / "reference.png").write_bytes(png())
            with patch.dict(sys.modules, modules), patch.object(MfluxBackend, "check", return_value={"available": True}), \
                    patch.object(MfluxBackend, "fingerprint", return_value="fixed-test-digest"):
                run(job)
            proof = read_json(job / "result.json")
            self.assertEqual([job / "reference.png"], calls["edit"]["image_paths"])
            self.assertEqual("explicit-4b", calls["load"]["model_config"])
            self.assertEqual(OBJECTIVE, calls["edit"]["prompt"])
            self.assertEqual((4, 1.0), (calls["edit"]["num_inference_steps"], calls["edit"]["guidance"]))
            self.assertEqual({}, calls["exportInfo"])
            self.assertEqual(400, proof["promptTokens"])
            self.assertEqual(4, proof["loadedQuantize"])

    def test_isolated_worker_rejects_wrong_profile_with_structured_error(self):
        with tempfile.TemporaryDirectory() as temporary:
            job = Path(temporary).resolve() / "job"
            job.mkdir()
            write_json(job / "state.json", {"state": "running"})
            write_json(job / "request.json", {"modelDirectory": str(job / "missing"), "modelId": "invalid-model"})
            result = subprocess.run(MfluxBackend.command(job), env=worker_environment(job),
                                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
            self.assertEqual(1, result.returncode)
            self.assertEqual(b"", result.stdout)
            self.assertEqual(b"", result.stderr)
            failure = read_json(job / "failure.json")
            self.assertEqual("unsupportedRuntime", failure["code"])
            self.assertEqual("ValueError", failure["exceptionType"])
            self.assertTrue(all("/" not in f["file"] and "\\" not in f["file"] for f in failure["stack"]))


if __name__ == "__main__":
    unittest.main()
