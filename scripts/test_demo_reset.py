import copy
import unittest
from unittest.mock import patch

import demo_reset
import release
import reset_job
from release_gate import ReleaseFailure
from test_release import AzureFixture, config


class ResetTests(unittest.TestCase):
    def setUp(self):
        self.config = config()
        self.azure = AzureFixture(self.config)
        self.ticks = 0

    def wait(self, fetch, expected, budget):
        def advance(seconds):
            self.ticks += seconds
        release.wait(fetch, expected, budget, lambda: self.ticks, advance)

    def execute(self, gate=lambda: None, smoke=lambda: None):
        return demo_reset.execute(self.config, self.azure, gate, smoke, self.wait)

    def starts(self):
        return [call for call in self.azure.calls if call[0] == "command"
                and call[1][:3] == ["containerapp", "job", "start"] and "--help" not in call[1]]

    def test_manual_success_starts_once_between_pause_and_activation(self):
        smoke_modes = []
        self.execute(smoke=lambda: smoke_modes.append(self.azure.reset["trigger"]))
        self.assertEqual(smoke_modes, ["Schedule", "Manual"])
        self.assertEqual(len(self.starts()), 1)
        self.assertIn(reset_job.RESET_JOB, self.starts()[0][1])
        self.assertEqual(self.azure.reset["trigger"], "Schedule")
        self.assertFalse(any(call[0] == "patch" and "/containerApps/" in call[1] for call in self.azure.calls))

    def test_unsafe_manual_preflight_never_mutates(self):
        for mutate in (
            lambda a: a.app.update(sha="c" * 40), lambda a: a.app.update(image="old"),
            lambda a: a.app.update(latest="other"), lambda a: a.reset.update(trigger="Manual"),
            lambda a: a.reset.update(retries=1), lambda a: a.reset.update(secrets=["runtime"]),
            lambda a: setattr(a, "running", ["processing"]), lambda a: setattr(a, "reset_running", ["unknown"]),
            lambda a: a.reset_template["containers"][0].update(command=["sh"]),
            lambda a: a.reset_template["containers"][0]["env"].append({"name": "ConnectionStrings__HospitalDatabase", "value": "never-read"}),
            lambda a: a.reset_template["containers"][0]["env"][-2].update(value="never-read"),
        ):
            self.azure = AzureFixture(self.config)
            mutate(self.azure)
            with self.subTest(mutate=mutate), self.assertRaises(ReleaseFailure):
                self.execute()
            self.assertFalse(self.starts())
            self.assertFalse(any(call[0] == "patch" for call in self.azure.calls))

    def test_provider_extra_resources_and_env_order_are_accepted(self):
        self.azure.reset_template["containers"][0]["resources"]["ephemeralStorage"] = "1Gi"
        self.azure.reset_template["containers"][0]["env"].reverse()
        self.execute()

    def test_failed_or_timed_out_execution_leaves_manual_and_no_retry(self):
        for status in ("Failed", "Running", "Unknown"):
            self.azure = AzureFixture(self.config)
            self.azure.execution_status = status
            with self.subTest(status=status), self.assertRaises(ReleaseFailure):
                self.execute()
            self.assertEqual(len(self.starts()), 1)
            self.assertEqual(self.azure.reset["trigger"], "Manual")

    def test_uncertain_start_never_retries(self):
        run = self.azure.run

        def uncertain(args, output=True):
            result = run(args, output)
            if args[:3] == ["containerapp", "job", "start"] and "--help" not in args:
                raise ReleaseFailure("Uncertain start")
            return result

        self.azure.run = uncertain
        with self.assertRaises(ReleaseFailure):
            self.execute()
        self.assertEqual(len(self.starts()), 1)
        self.assertEqual(self.azure.reset["trigger"], "Manual")

    def test_moved_main_after_pause_stops_before_start(self):
        def gate():
            if self.azure.reset["trigger"] == "Manual":
                raise ReleaseFailure("Main moved")
        with self.assertRaises(ReleaseFailure):
            self.execute(gate=gate)
        self.assertFalse(self.starts())
        self.assertEqual(self.azure.reset["trigger"], "Manual")

    def test_release_waits_for_drain_and_never_migrates_on_timeout(self):
        self.azure.reset_running = ["queued-or-running"]
        with self.assertRaisesRegex(ReleaseFailure, "timed out"):
            release.deploy(self.config, self.azure, lambda: None, lambda: None,
                           lambda: self.ticks, lambda seconds: setattr(self, "ticks", self.ticks + seconds))
        self.assertFalse(self.starts())
        self.assertEqual(self.azure.reset["trigger"], "Manual")

    def test_activation_uncertainty_does_not_rollback_healthy_release_or_claim_paused(self):
        mutate = self.azure.mutate

        def uncertain(resource, method, body=None, query=None):
            result = mutate(resource, method, body, query)
            if body and body["properties"].get("configuration", {}).get("triggerType") == "Schedule":
                raise ReleaseFailure("Uncertain activation")
            return result

        self.azure.mutate = uncertain
        with self.assertRaisesRegex(ReleaseFailure, "may be scheduled"):
            release.deploy(self.config, self.azure, lambda: None, lambda: None)
        self.assertEqual(self.azure.reset["trigger"], "Schedule")
        self.assertFalse(any(call[0] == "command" and "--from-revision" in call[1] for call in self.azure.calls))

    def test_template_failure_cannot_enable_schedule(self):
        self.azure.reset["trigger"] = "Manual"
        job = reset_job.ResetJob(self.config, self.azure, release.template, self.wait)
        with patch.object(job, "verify_template", return_value=False), self.assertRaisesRegex(ReleaseFailure, "timed out"):
            job.activate()
        self.assertEqual(self.azure.reset["trigger"], "Manual")

    def test_render_is_inert_and_only_references_secret(self):
        result = reset_job.definition(self.azure.app["environment"])
        self.assertEqual(result["properties"]["configuration"]["triggerType"], "Manual")
        self.assertEqual(result["properties"]["template"]["containers"][0]["args"], ["-c", "exit 0"])
        self.assertNotIn("secrets", result["properties"]["configuration"])
        original = release.template(self.config, "maintenance", True)
        snapshot = copy.deepcopy(original)
        reset_job.template(original)
        self.assertEqual(original, snapshot)


if __name__ == "__main__":
    unittest.main()
