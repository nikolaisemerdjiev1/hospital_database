"""One protected manual synthetic reset of the stable, exact-main deployed image."""

import argparse
import json
import os
import re
import subprocess
import sys

import release
from release_gate import GitHub, ReleaseFailure, check, require
from reset_job import ACTIVE_QUERY, RESET_JOB, ResetJob


def execute(config, azure, gate, smoke_check, wait=release.wait):
    gate()
    account = azure.run(["account", "show", "--query", "{id:id,tenant:tenantId,state:state}"])
    require(account == {"id": config["AZURE_SUBSCRIPTION_ID"], "tenant": config["AZURE_TENANT_ID"], "state": "Enabled"}, "Wrong Azure account")
    app_id = config["base"] + "/containerApps/" + config["AZURE_CONTAINER_APP_NAME"]
    migration = config["base"] + "/jobs/" + release.JOB_NAME
    environment = config["base"] + "/managedEnvironments/" + config["AZURE_CONTAINER_APP_ENVIRONMENT"]

    def inspect_app():
        app = azure.get(app_id, release.APP_QUERY)
        require(app["state"] == "Succeeded" and app["mode"] == "Single"
                and app["environment"].lower() == environment.lower() and len(app["names"]) == 1
                and release.RUNTIME_SECRET in app["secrets"] and release.MAINTENANCE_SECRET not in app["secrets"]
                and app["revision"] == app["latest"] and app["revision"].startswith(config["AZURE_CONTAINER_APP_NAME"] + "--")
                and app["sha"] == config["RELEASE_SHA"] and app["image"] == config["RELEASE_IMAGE"]
                and "https://" + app["host"] == config["APP_ORIGIN"] and app["port"] == 8080
                and app["external"] and not app["insecure"], "Stable exact-main deployed application required")
        require(not azure.get(migration + "/executions", ACTIVE_QUERY), "Maintenance execution already active")
        return app["revision"]

    revision = inspect_app()
    reset = ResetJob(config, azure, release.template, wait)
    require(reset.inspect()["trigger"] == "Schedule", "Reset schedule must have been enabled by a successful release; inspect paused state")
    require(reset.verify_template(), "Reset image/configuration differs from deployed release")
    require(not azure.get(reset.resource + "/executions", ACTIVE_QUERY), "Reset execution already active")
    smoke_check()
    azure.run(["containerapp", "job", "start", "--help"], output=False)
    gate()
    reset.pause()
    require(inspect_app() == revision and reset.verify_template(), "Application/reset changed while pausing")
    gate()
    # Exactly one start, never retry an uncertain provider response.
    execution = azure.run(["containerapp", "job", "start", "--name", RESET_JOB,
                           "--resource-group", config["AZURE_RESOURCE_GROUP"], "--query", "name"])
    require(isinstance(execution, str) and re.fullmatch(r"[a-z0-9-]+", execution), "Reset start outcome uncertain; inspect executions before retry")
    wait(lambda: azure.get(reset.resource + "/executions/" + execution, "properties.status"), "Succeeded", 660)
    require(inspect_app() == revision, "Application changed during reset; schedule remains paused")
    smoke_check()
    try:
        reset.activate()
    except (ReleaseFailure, KeyError, TypeError, OSError, subprocess.TimeoutExpired):
        raise ReleaseFailure("Reset succeeded; schedule activation is unconfirmed and may be active. Inspect before retry.") from None
    return {"reset_execution": execution, "revision": revision, "image": config["RELEASE_IMAGE"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args()
    try:
        require(args.execute and os.environ.get("GITHUB_ACTIONS") == "true"
                and os.environ.get("GITHUB_REF") == "refs/heads/main"
                and os.environ.get("RESET_AUTHORIZATION") == "RESET_SYNTHETIC_DEMO", "Protected reset workflow required")
        require(os.environ["RELEASE_SHA"] == os.environ["GITHUB_SHA"], "Requested commit differs from workflow")
        github = GitHub(os.environ["GITHUB_REPOSITORY"], os.environ["GH_TOKEN"])
        gate = lambda: check(github, os.environ["RELEASE_SHA"], os.environ["GITHUB_REPOSITORY"])
        gate()
        # Bootstrap public resource addressing, then validate the actual deployed digest.
        config = release.configuration({**os.environ, "RELEASE_IMAGE": "ghcr.io/" + os.environ["GITHUB_REPOSITORY"].lower() + "@sha256:" + "0" * 64})
        azure = release.Azure()
        app = azure.get(config["base"] + "/containerApps/" + config["AZURE_CONTAINER_APP_NAME"], release.APP_QUERY)
        config = release.configuration({**config, "RELEASE_IMAGE": app["image"]})
        print(json.dumps(execute(config, azure, gate, lambda: release.check_smoke(config))))
    except ReleaseFailure as error:
        print(str(error), file=sys.stderr)
        return 1
    except (KeyError, TypeError, OSError, subprocess.TimeoutExpired):
        print("Reset stopped; inspect schedule/execution state before retry. No automatic retry was performed.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
