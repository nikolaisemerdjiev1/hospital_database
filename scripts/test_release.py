import copy
import unittest

import release
import reset_job
from release_gate import ReleaseFailure, check


def config():
    return release.configuration({
        "AZURE_SUBSCRIPTION_ID": "11111111-1111-1111-1111-111111111111",
        "AZURE_TENANT_ID": "22222222-2222-2222-2222-222222222222",
        "AZURE_RESOURCE_GROUP": "rg-fixture", "AZURE_CONTAINER_APP_ENVIRONMENT": "env-fixture",
        "AZURE_CONTAINER_APP_NAME": "app-fixture", "APP_ORIGIN": "https://app-fixture.example.azurecontainerapps.io",
        "VITE_AUTH0_DOMAIN": "auth.example.invalid", "VITE_AUTH0_AUDIENCE": "https://api.example.invalid",
        "AUTH0_ROLE_CLAIM": "https://api.example.invalid/role", "RELEASE_SHA": "a" * 40,
        "RELEASE_IMAGE": "ghcr.io/fixture/repo@sha256:" + "b" * 64,
        "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1", "GITHUB_REPOSITORY": "fixture/repo",
    })


class GitHubFixture:
    def __init__(self):
        self.sha = "a" * 40
        self.conclusion = "success"
        self.event = "push"
        self.repository = "fixture/repo"
        self.skip = False

    def get(self, path):
        if path == "/git/ref/heads/main":
            return {"object": {"sha": self.sha}}
        if "/runs?" in path:
            return {"workflow_runs": [{"id": 1 if "ci.yml" in path else 2, "run_attempt": 2,
                     "head_sha": "a" * 40, "head_branch": "main", "event": self.event,
                     "head_repository": {"full_name": self.repository}, "status": "completed", "conclusion": self.conclusion}]}
        names = (["Backend", "Frontend", "Combined application container", "Workflow and smoke tooling"]
                 if "/runs/1/" in path else ["CodeQL (csharp)", "CodeQL (javascript-typescript)", "CodeQL (actions)"])
        return {"jobs": [{"name": name, "status": "completed", "conclusion": "skipped" if self.skip else "success"} for name in names]}


class GateTests(unittest.TestCase):
    def test_exact_main_success_and_required_jobs(self):
        check(GitHubFixture(), "a" * 40, "fixture/repo")

    def test_rejects_moving_main_failed_runs_pr_fork_and_skipped_jobs(self):
        for attribute, value in (("sha", "c" * 40), ("conclusion", "failure"), ("event", "pull_request"),
                                 ("repository", "attacker/fork"), ("skip", True)):
            with self.subTest(attribute=attribute):
                fixture = GitHubFixture()
                setattr(fixture, attribute, value)
                with self.assertRaises(ReleaseFailure):
                    check(fixture, "a" * 40, "fixture/repo")

    def test_rejects_partial_or_injected_sha(self):
        for sha in ("main", "a" * 39, "A" * 40, "a" * 40 + "\n", "$(echo bad)"):
            with self.subTest(sha=sha), self.assertRaises(ReleaseFailure):
                check(GitHubFixture(), sha, "fixture/repo")


class AzureFixture:
    def __init__(self, settings):
        self.settings = settings
        environment = settings["base"] + "/managedEnvironments/env-fixture"
        self.app = {"state": "Succeeded", "mode": "Single", "environment": environment,
                    "host": "app-fixture.example.azurecontainerapps.io", "port": 8080,
                    "insecure": False, "external": True, "revision": "app-fixture--previous",
                    "latest": "app-fixture--previous", "names": ["web"], "secrets": [release.RUNTIME_SECRET],
                    "image": settings["RELEASE_IMAGE"], "sha": settings["RELEASE_SHA"]}
        self.job = {"state": "Succeeded", "environment": environment, "trigger": "Manual", "retries": 0,
                    "timeout": 600, "manual": {"parallelism": 1, "replicaCompletionCount": 1},
                    "names": ["maintenance"], "secrets": [release.MAINTENANCE_SECRET]}
        self.calls = []
        self.execution_status = "Succeeded"
        self.running = []
        self.fail_patch = False
        self.fail_rollback = False
        self.reset = {**copy.deepcopy(self.job), "trigger": "Schedule", "schedule": reset_job.configuration(True)["scheduleTriggerConfig"]}
        self.reset_template = reset_job.template(release.template(settings, "reset", True))
        self.reset_running = []

    def run(self, args, output=True):
        self.calls.append(("command", args))
        if args[:2] == ["account", "show"]:
            return {"id": self.settings["AZURE_SUBSCRIPTION_ID"], "tenant": self.settings["AZURE_TENANT_ID"], "state": "Enabled"}
        if "--from-revision" in args:
            if self.fail_rollback:
                raise ReleaseFailure("Fake recovery failure")
            self.app["revision"] = "app-fixture--rb-123-1"
        if args[:3] == ["containerapp", "job", "start"] and "--help" not in args:
            return "migration-fixture-123"

    def get(self, resource, query):
        if resource.endswith("/jobs/" + reset_job.RESET_JOB):
            if query == reset_job.QUERY:
                return copy.deepcopy(self.reset)
            if query == reset_job.TEMPLATE_QUERY:
                container = self.reset_template["containers"][0]
                return {"state": self.reset["state"], "image": container["image"], "command": container["command"],
                        "args": container["args"], "cpu": container["resources"]["cpu"], "memory": container["resources"]["memory"],
                        "init": len(self.reset_template.get("initContainers") or []), "volumes": len(self.reset_template.get("volumes") or []),
                        "env": [{"name": item["name"], "secret": item.get("secretRef"), "hasValue": "value" in item} for item in container["env"]],
                        "values": [{"name": item["name"], "value": item.get("value")} for item in container["env"] if item["name"] in reset_job.PUBLIC_ENV]}
            return {"state": self.reset["state"], "trigger": self.reset["trigger"]}
        if query == release.APP_QUERY:
            return copy.deepcopy(self.app)
        if query == release.JOB_QUERY:
            return copy.deepcopy(self.job)
        if query == release.JOB_IMAGE_QUERY:
            return {"state": "Succeeded", "image": self.settings["RELEASE_IMAGE"], "revision": self.settings["RELEASE_SHA"]}
        if resource.endswith("/executions"):
            return self.reset_running if reset_job.RESET_JOB in resource else self.running
        if "/executions/" in resource:
            return self.execution_status
        if query == "properties.provisioningState":
            return "Succeeded"
        if query == "properties.latestReadyRevisionName":
            return self.app["revision"]
        raise AssertionError("Unexpected fixture query")

    def mutate(self, resource, method, body=None, query=None):
        self.calls.append((method, resource, body))
        if method == "post":
            return "migration-fixture-123"
        if resource.endswith("/jobs/" + reset_job.RESET_JOB):
            properties = body["properties"]
            if "configuration" in properties:
                values = properties["configuration"]
                self.reset.update(trigger=values["triggerType"], manual=values["manualTriggerConfig"], schedule=values["scheduleTriggerConfig"])
            if "template" in properties:
                self.reset_template = copy.deepcopy(properties["template"])
        if "/containerApps/" in resource:
            self.app["revision"] = "app-fixture--" + self.settings["suffix"]
            self.app["latest"] = self.app["revision"]
            if self.fail_patch:
                raise ReleaseFailure("Uncertain update response")


class DeploymentTests(unittest.TestCase):
    def test_manifest_secret_separation_and_process_only_probes(self):
        settings = config()
        app = release.template(settings, "web")
        job = release.template(settings, "maintenance", True)
        app_env = app["containers"][0]["env"]
        job_env = job["containers"][0]["env"]
        self.assertEqual([item["secretRef"] for item in app_env if "secretRef" in item], [release.RUNTIME_SECRET])
        self.assertEqual([item["secretRef"] for item in job_env if "secretRef" in item], [release.MAINTENANCE_SECRET])
        self.assertTrue(all(item["httpGet"]["path"] == "/health/live" for item in app["containers"][0]["probes"]))
        self.assertEqual(job["containers"][0]["args"], ["Hospital.Api.dll", "--prepare-release"])
        self.assertEqual(next(item["value"] for item in job_env
                              if item["name"] == "Logging__LogLevel__Microsoft.EntityFrameworkCore"), "None")
        self.assertEqual(app["scale"], {"minReplicas": 0, "maxReplicas": 1, "rules": []})

    def test_success_orders_maintenance_before_web_and_smoke(self):
        settings = config()
        azure = AzureFixture(settings)
        gates = []
        smoke_calls = []
        result = release.deploy(settings, azure, lambda: gates.append(True), lambda: smoke_calls.append(True))
        mutations = [call for call in azure.calls if call[0] != "command"
                     or (call[1][:3] == ["containerapp", "job", "start"] and "--help" not in call[1])]
        self.assertIn(reset_job.RESET_JOB, mutations[0][1])
        self.assertIn(release.JOB_NAME, mutations[1][1])
        self.assertEqual(mutations[2][1][:3], ["containerapp", "job", "start"])
        self.assertIn("/containerApps/", mutations[3][1])
        self.assertIn("template", mutations[4][2]["properties"])
        self.assertEqual(mutations[5][2]["properties"]["configuration"]["triggerType"], "Schedule")
        self.assertEqual(len(gates), 4)
        self.assertEqual(smoke_calls, [True])
        self.assertEqual(result["previous_revision"], "app-fixture--previous")

    def test_failed_migration_never_updates_or_rolls_back_web(self):
        settings = config()
        azure = AzureFixture(settings)
        azure.execution_status = "Failed"
        with self.assertRaises(ReleaseFailure):
            release.deploy(settings, azure, lambda: None, lambda: self.fail("Smoke must not run"))
        self.assertFalse(any(call[0] == "patch" and "/containerApps/" in call[1] for call in azure.calls))
        self.assertFalse(any(call[0] == "command" and "--from-revision" in call[1] for call in azure.calls))

    def test_smoke_or_uncertain_update_failure_restores_previous_template(self):
        for uncertain in (False, True):
            settings = config()
            azure = AzureFixture(settings)
            azure.fail_patch = uncertain

            def failed_smoke():
                raise ReleaseFailure("Fake unhealthy response")

            with self.subTest(uncertain=uncertain), self.assertRaisesRegex(ReleaseFailure, "NOT downgraded"):
                release.deploy(settings, azure, lambda: None, failed_smoke)
            rollback = [call for call in azure.calls if call[0] == "command" and "--from-revision" in call[1]]
            self.assertEqual(len(rollback), 1)
            self.assertIn("app-fixture--previous", rollback[0][1])

    def test_recovery_failure_is_explicit_and_never_reported_as_restored(self):
        settings = config()
        azure = AzureFixture(settings)
        azure.fail_patch = True
        azure.fail_rollback = True
        with self.assertRaisesRegex(ReleaseFailure, "recovery is unconfirmed"):
            release.deploy(settings, azure, lambda: None, lambda: None)

    def test_stale_job_template_never_starts_maintenance(self):
        settings = config()
        azure = AzureFixture(settings)
        original_get = azure.get
        azure.get = lambda resource, query: ({"state": "Succeeded", "image": "old-image", "revision": "old"}
                                           if query == release.JOB_IMAGE_QUERY else original_get(resource, query))
        ticks = [0]
        with self.assertRaisesRegex(ReleaseFailure, "timed out"):
            release.deploy(settings, azure, lambda: None, lambda: None,
                           lambda: ticks[0], lambda seconds: ticks.__setitem__(0, ticks[0] + seconds))
        self.assertFalse(any(call[0] == "command" and call[1][:3] == ["containerapp", "job", "start"]
                             and "--help" not in call[1] for call in azure.calls))

    def test_wrong_host_secret_setup_retry_policy_or_active_execution_refuses_before_writes(self):
        for change in (lambda a: a.app.update(host="wrong.example"),
                       lambda a: a.app.update(secrets=[release.MAINTENANCE_SECRET]),
                       lambda a: a.job.update(retries=1), lambda a: setattr(a, "running", ["other-execution"])):
            settings = config()
            azure = AzureFixture(settings)
            change(azure)
            with self.assertRaises(ReleaseFailure):
                release.deploy(settings, azure, lambda: None, lambda: None)
            self.assertFalse(any(call[0] in ("patch", "post") for call in azure.calls))

    def test_main_moving_after_migration_stops_before_web_update(self):
        settings = config()
        azure = AzureFixture(settings)
        calls = []

        def gate():
            calls.append(True)
            if len(calls) == 4:
                raise ReleaseFailure("Main moved")

        with self.assertRaises(ReleaseFailure):
            release.deploy(settings, azure, gate, lambda: None)
        self.assertFalse(any(call[0] == "patch" and "/containerApps/" in call[1] for call in azure.calls))

    def test_main_moving_during_job_preparation_stops_before_migration(self):
        settings = config()
        azure = AzureFixture(settings)
        github = GitHubFixture()
        original_get = azure.get

        def main_moves(resource, query):
            if query == release.JOB_IMAGE_QUERY:
                github.sha = "c" * 40
            return original_get(resource, query)

        azure.get = main_moves
        with self.assertRaises(ReleaseFailure):
            release.deploy(settings, azure, lambda: check(github, settings["RELEASE_SHA"], "fixture/repo"), lambda: None)
        self.assertFalse(any(call[0] == "command" and call[1][:3] == ["containerapp", "job", "start"]
                             and "--help" not in call[1] for call in azure.calls))
        self.assertEqual(azure.reset["trigger"], "Manual")

    def test_polling_is_bounded_and_does_not_retry_mutations(self):
        ticks = [0]
        with self.assertRaisesRegex(ReleaseFailure, "timed out"):
            release.wait(lambda: "Running", "Succeeded", 10, lambda: ticks[0], lambda seconds: ticks.__setitem__(0, ticks[0] + seconds))
        self.assertEqual(ticks[0], 10)


if __name__ == "__main__":
    unittest.main()
