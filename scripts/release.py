"""Explicit production orchestration; importable pure manifests and injected tests.

No secret retrieval, provisioning, password literals, automatic migration retries or DB downgrade.
Only the protected GitHub workflow may invoke --execute. Local checks import these functions.
"""

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import smoke
from release_gate import GitHub, ReleaseFailure, check, require
from reset_job import ACTIVE_QUERY, ResetJob

API_VERSION = "2025-07-01"
JOB_NAME = "job-harbor-care-migrate"
RUNTIME_SECRET = "hospital-runtime-database"
MAINTENANCE_SECRET = "hospital-maintenance-database"


def configuration(env):
    result = {key: env[key] for key in (
        "AZURE_SUBSCRIPTION_ID", "AZURE_TENANT_ID", "AZURE_RESOURCE_GROUP",
        "AZURE_CONTAINER_APP_ENVIRONMENT", "AZURE_CONTAINER_APP_NAME", "APP_ORIGIN",
        "VITE_AUTH0_DOMAIN", "VITE_AUTH0_AUDIENCE", "AUTH0_ROLE_CLAIM", "RELEASE_SHA",
        "RELEASE_IMAGE", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_REPOSITORY",
    )}
    for key in ("AZURE_SUBSCRIPTION_ID", "AZURE_TENANT_ID"):
        require(re.fullmatch(r"[0-9a-f-]{36}", result[key]), "Invalid Azure identifier")
    for key in ("AZURE_RESOURCE_GROUP", "AZURE_CONTAINER_APP_ENVIRONMENT", "AZURE_CONTAINER_APP_NAME"):
        require(re.fullmatch(r"[a-z][a-z0-9-]{1,40}", result[key]), "Invalid resource name")
    require(re.fullmatch(r"[0-9a-f]{40}", result["RELEASE_SHA"]), "Invalid release SHA")
    require(re.fullmatch(r"[a-z0-9.-]+", result["VITE_AUTH0_DOMAIN"]), "Invalid Auth0 domain")
    require(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", result["GITHUB_REPOSITORY"]), "Invalid repository")
    image_prefix = "ghcr.io/" + result["GITHUB_REPOSITORY"].lower() + "@sha256:"
    require(result["RELEASE_IMAGE"].startswith(image_prefix)
            and re.fullmatch(r"[0-9a-f]{64}", result["RELEASE_IMAGE"][len(image_prefix):]), "Digest-pinned repository image required")
    require(smoke.origin(result["APP_ORIGIN"]) == result["APP_ORIGIN"], "Canonical origin required")
    require(result["APP_ORIGIN"].endswith(".azurecontainerapps.io"), "Existing Azure origin required")
    for key in ("VITE_AUTH0_AUDIENCE", "AUTH0_ROLE_CLAIM"):
        require(result[key].startswith("https://") and not any(c.isspace() for c in result[key]), "Invalid public Auth0 identifier")
    for key in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"):
        require(re.fullmatch(r"[0-9]{1,12}", result[key]), "Invalid run identifier")
    result["suffix"] = f"r-{result['RELEASE_SHA'][:12]}-{result['GITHUB_RUN_ID']}-{result['GITHUB_RUN_ATTEMPT']}"
    result["base"] = (f"/subscriptions/{result['AZURE_SUBSCRIPTION_ID']}/resourceGroups/"
                      f"{result['AZURE_RESOURCE_GROUP']}/providers/Microsoft.App")
    return result


def template(config, container_name, maintenance=False):
    values = {
        "ASPNETCORE_ENVIRONMENT": "Production", "Release__Revision": config["RELEASE_SHA"],
        "ReverseProxy__TrustForwardedHeaders": "true", "Frontend__Origin": config["APP_ORIGIN"],
        "Authentication__Auth0__Domain": config["VITE_AUTH0_DOMAIN"],
        "Authentication__Auth0__Audience": config["VITE_AUTH0_AUDIENCE"],
        "Authentication__Auth0__RoleClaim": config["AUTH0_ROLE_CLAIM"],
        "Logging__LogLevel__Microsoft.AspNetCore": "Warning",
        "Logging__LogLevel__Microsoft.EntityFrameworkCore": "Warning",
    }
    if maintenance:
        values.update({
            "Logging__LogLevel__Microsoft.EntityFrameworkCore": "None",
            "DemoSeed__AnchorDate": "today",
            "DemoSeed__Subjects__Patient": "auth0|6aa32126f68055702eb783a3",
            "DemoSeed__Subjects__Doctor": "auth0|6aa321658159f5c4c53cb5f5",
            "DemoSeed__Subjects__Pharmacist": "auth0|6aa32181874554e636b5f3f6",
            "DemoSeed__Subjects__Administrator": "demo-seed|administrator",
        })
    env = [{"name": key, "value": value} for key, value in values.items()]
    env.append({"name": "ConnectionStrings__HospitalMaintenanceDatabase" if maintenance else "ConnectionStrings__HospitalDatabase",
                "secretRef": MAINTENANCE_SECRET if maintenance else RUNTIME_SECRET})
    container = {"name": container_name, "image": config["RELEASE_IMAGE"], "env": env,
                 "resources": {"cpu": 0.25, "memory": "0.5Gi"},
                 "command": ["dotnet"], "args": ["Hospital.Api.dll"]}
    if maintenance:
        container["args"].append("--prepare-release")
        return {"containers": [container], "initContainers": [], "volumes": []}
    container["probes"] = [{
        "type": kind, "httpGet": {"path": "/health/live", "port": 8080, "scheme": "HTTP"},
        "initialDelaySeconds": 1, "periodSeconds": period, "timeoutSeconds": 2,
        "failureThreshold": failures,
    } for kind, period, failures in (("Startup", 2, 60), ("Liveness", 10, 3), ("Readiness", 5, 3))]
    return {"revisionSuffix": config["suffix"], "containers": [container], "initContainers": [], "volumes": [],
            "scale": {"minReplicas": 0, "maxReplicas": 1, "rules": []}}


class Azure:
    def run(self, args, output=True):
        try:
            result = subprocess.run(["az", *args, "--only-show-errors", "--output", "json" if output else "none"],
                                    capture_output=True, text=True, check=False, timeout=180)
            require(result.returncode == 0, "Azure operation failed; inspect resource state before retry")
            return json.loads(result.stdout) if output and result.stdout.strip() else None
        except (OSError, ValueError, subprocess.TimeoutExpired):
            raise ReleaseFailure("Azure command failed; outcome may be uncertain") from None

    def get(self, resource, query):
        return self.run(["rest", "--method", "get", "--url",
                         f"https://management.azure.com{resource}?api-version={API_VERSION}", "--query", query])

    def mutate(self, resource, method, body=None, query=None):
        args = ["rest", "--method", method, "--url", f"https://management.azure.com{resource}?api-version={API_VERSION}"]
        if query:
            args += ["--query", query]
        # Body contains only public config and secret REFERENCES, never credential values.
        with tempfile.TemporaryDirectory(prefix="hospital-release-") as directory:
            if body is not None:
                path = Path(directory) / "request.json"
                path.write_text(json.dumps(body), encoding="utf-8")
                args += ["--body", "@" + str(path)]
            return self.run(args, output=query is not None)


APP_QUERY = ("{state:properties.provisioningState,mode:properties.configuration.activeRevisionsMode,"
             "environment:not_null(properties.managedEnvironmentId,properties.environmentId),host:properties.configuration.ingress.fqdn,"
             "port:properties.configuration.ingress.targetPort,insecure:properties.configuration.ingress.allowInsecure,"
             "external:properties.configuration.ingress.external,revision:properties.latestReadyRevisionName,"
             "latest:properties.latestRevisionName,names:properties.template.containers[].name,"
             "secrets:properties.configuration.secrets[].name,image:properties.template.containers[0].image,"
             "sha:properties.template.containers[0].env[?name=='Release__Revision'].value|[0]}")
JOB_QUERY = ("{state:properties.provisioningState,environment:properties.environmentId,"
             "trigger:properties.configuration.triggerType,retries:properties.configuration.replicaRetryLimit,"
             "timeout:properties.configuration.replicaTimeout,manual:properties.configuration.manualTriggerConfig,"
             "names:properties.template.containers[].name,secrets:properties.configuration.secrets[].name}")
JOB_IMAGE_QUERY = "{state:properties.provisioningState,image:properties.template.containers[0].image,revision:properties.template.containers[0].env[?name=='Release__Revision'].value|[0]}"


def wait(fetch, expected, budget, clock=time.monotonic, sleep=time.sleep):
    deadline = clock() + budget
    while clock() < deadline:
        state = fetch()
        if state == expected:
            return
        require(state not in ("Failed", "Canceled", "Stopped", "Degraded"), "Azure execution/revision failed")
        sleep(5)
    raise ReleaseFailure("Azure readiness timed out; no automatic execution retry")


def deploy(config, azure, gate, smoke_check, clock=time.monotonic, sleep=time.sleep):
    gate()
    account = azure.run(["account", "show", "--query", "{id:id,tenant:tenantId,state:state}"])
    require(account == {"id": config["AZURE_SUBSCRIPTION_ID"], "tenant": config["AZURE_TENANT_ID"], "state": "Enabled"}, "Wrong Azure account")
    app_id = config["base"] + "/containerApps/" + config["AZURE_CONTAINER_APP_NAME"]
    job_id = config["base"] + "/jobs/" + JOB_NAME
    environment = config["base"] + "/managedEnvironments/" + config["AZURE_CONTAINER_APP_ENVIRONMENT"]
    app = azure.get(app_id, APP_QUERY)
    job = azure.get(job_id, JOB_QUERY)
    require(app["state"] == "Succeeded" and app["mode"] == "Single" and app["environment"].lower() == environment.lower(), "Unexpected app/environment")
    require("https://" + app["host"] == config["APP_ORIGIN"] and app["port"] == 8080
            and app["external"] and not app["insecure"], "Existing ingress differs from approved origin")
    require(len(app["names"]) == 1 and len(job["names"]) == 1, "Only single-container resources supported")
    require(RUNTIME_SECRET in app["secrets"] and MAINTENANCE_SECRET not in app["secrets"], "Runtime secret setup required")
    require(job["state"] == "Succeeded" and job["environment"].lower() == environment.lower()
            and job["trigger"] == "Manual" and job["retries"] == 0 and job["timeout"] == 600
            and job["manual"] == {"parallelism": 1, "replicaCompletionCount": 1}
            and MAINTENANCE_SECRET in job["secrets"] and RUNTIME_SECRET not in job["secrets"], "Maintenance job setup required")
    require(not azure.get(job_id + "/executions", ACTIVE_QUERY), "Maintenance execution already active")
    previous = app["revision"]
    require(previous == app["latest"] and previous.startswith(config["AZURE_CONTAINER_APP_NAME"] + "--"), "Existing rollout is incomplete")
    # This command must exist before any mutation; rollback uses revision copy in Single mode.
    azure.run(["containerapp", "revision", "copy", "--help"], output=False)
    azure.run(["containerapp", "job", "start", "--help"], output=False)
    gate()
    reset = ResetJob(config, azure, template, lambda fetch, expected, budget: wait(fetch, expected, budget, clock, sleep))
    reset.pause()
    print("Preparing the existing maintenance job with the scanned image.")
    azure.mutate(job_id, "patch", {"properties": {"template": template(config, job["names"][0], True)}})

    def maintenance_updated():
        updated = azure.get(job_id, JOB_IMAGE_QUERY)
        if updated == {"state": "Succeeded", "image": config["RELEASE_IMAGE"], "revision": config["RELEASE_SHA"]}:
            return "Ready"
        return updated["state"]

    wait(maintenance_updated, "Ready", 180, clock, sleep)
    # Pausing/draining and template provisioning may outlast the previous main check.
    gate()
    # CLI waits for Azure's asynchronous start response; never retry an uncertain start.
    execution = azure.run(["containerapp", "job", "start", "--name", JOB_NAME,
                           "--resource-group", config["AZURE_RESOURCE_GROUP"], "--query", "name"])
    require(isinstance(execution, str) and re.fullmatch(r"[a-z0-9-]+", execution), "Job start outcome uncertain")
    wait(lambda: azure.get(job_id + "/executions/" + execution, "properties.status"), "Succeeded", 660, clock, sleep)
    # A new main commit while maintenance ran must not silently release the older code.
    gate()
    print("Maintenance succeeded; updating the existing web application.")
    expected = config["AZURE_CONTAINER_APP_NAME"] + "--" + config["suffix"]
    try:
        azure.mutate(app_id, "patch", {"properties": {"template": template(config, app["names"][0])}})
        wait(lambda: azure.get(app_id, "properties.latestReadyRevisionName"), expected, 300, clock, sleep)
        updated = azure.get(app_id, APP_QUERY)
        require(updated["host"] == app["host"] and updated["mode"] == "Single"
                and updated["image"] == config["RELEASE_IMAGE"] and updated["sha"] == config["RELEASE_SHA"]
                and updated["revision"] == updated["latest"] == expected, "Origin/revision/image changed")
        smoke_check()
    except (ReleaseFailure, smoke.SmokeFailure, KeyError, TypeError, OSError, subprocess.TimeoutExpired):
        # Recreate the prior template as a new revision; Azure keeps Single-mode routing.
        # It restores prior frontend/API/env references, never database schema or secrets.
        rollback_suffix = "rb-" + config["GITHUB_RUN_ID"] + "-" + config["GITHUB_RUN_ATTEMPT"]
        print("Rollout or smoke failed; attempting previous application revision recovery.")
        try:
            azure.run(["containerapp", "revision", "copy", "--name", config["AZURE_CONTAINER_APP_NAME"],
                       "--resource-group", config["AZURE_RESOURCE_GROUP"], "--from-revision", previous,
                       "--revision-suffix", rollback_suffix], output=False)
            wait(lambda: azure.get(app_id, "properties.latestReadyRevisionName"),
                 config["AZURE_CONTAINER_APP_NAME"] + "--" + rollback_suffix, 300, clock, sleep)
        except (ReleaseFailure, KeyError, TypeError, OSError, subprocess.TimeoutExpired):
            raise ReleaseFailure("Application recovery is unconfirmed; inspect Azure revisions before any retry. Database was NOT downgraded.") from None
        raise ReleaseFailure("Release failed; prior application template restored. Database was NOT downgraded.") from None
    # Only a successful rollout may select the new reset image and resume its schedule.
    # Failure here leaves the healthy web release in place; inspect reset configuration.
    try:
        reset.activate()
    except (ReleaseFailure, KeyError, TypeError, OSError, subprocess.TimeoutExpired):
        raise ReleaseFailure("Web release succeeded; reset activation is unconfirmed and may be scheduled. Inspect the reset job before retry.") from None
    return {"revision": expected, "previous_revision": previous, "image": config["RELEASE_IMAGE"], "migration_execution": execution}


def check_smoke(config):
    command = [sys.executable, str(Path(__file__).with_name("smoke.py")), "--api-url", config["APP_ORIGIN"],
               "--frontend-url", config["APP_ORIGIN"], "--frontend-origin", config["APP_ORIGIN"],
               "--expected-revision", config["RELEASE_SHA"]]
    require(subprocess.run(command, check=False, timeout=180).returncode == 0, "Hosted smoke failed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args()
    try:
        require(args.execute and os.environ.get("GITHUB_ACTIONS") == "true"
                and os.environ.get("GITHUB_REF") == "refs/heads/main"
                and os.environ.get("RELEASE_AUTHORIZATION") == "DEPLOY_SYNTHETIC_DEMO", "Protected workflow execution required")
        config = configuration(os.environ)
        require(config["RELEASE_SHA"] == os.environ["GITHUB_SHA"], "Requested commit differs from workflow")
        github = GitHub(config["GITHUB_REPOSITORY"], os.environ["GH_TOKEN"])

        result = deploy(config, Azure(), lambda: check(github, config["RELEASE_SHA"], config["GITHUB_REPOSITORY"]), lambda: check_smoke(config))
        print(json.dumps(result))  # Public resource metadata only.
    except ReleaseFailure as error:
        # ReleaseFailure messages are fixed local diagnostics, never raw provider output.
        print(str(error), file=sys.stderr)
        return 1
    except (smoke.SmokeFailure, KeyError, TypeError, subprocess.TimeoutExpired):
        print("Release stopped. Inspect job/revision status before retry; no database rollback was performed.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
