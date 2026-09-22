"""Public reset-job definitions and release coordination; never reads secret values."""

import argparse
import copy
import json
import re

from release_gate import require

RESET_JOB = "job-harbor-care-reset"
CRON = "0 11 * * *"
SECRET = "hospital-maintenance-database"
QUERY = ("{state:properties.provisioningState,environment:properties.environmentId,"
         "trigger:properties.configuration.triggerType,retries:properties.configuration.replicaRetryLimit,"
         "timeout:properties.configuration.replicaTimeout,manual:properties.configuration.manualTriggerConfig,"
         "schedule:properties.configuration.scheduleTriggerConfig,secrets:properties.configuration.secrets[].name,"
         "names:properties.template.containers[].name}")
PUBLIC_ENV = ("ASPNETCORE_ENVIRONMENT", "Release__Revision", "ReverseProxy__TrustForwardedHeaders",
              "Frontend__Origin", "Authentication__Auth0__Domain", "Authentication__Auth0__Audience",
              "Authentication__Auth0__RoleClaim", "Logging__LogLevel__Microsoft.AspNetCore",
              "Logging__LogLevel__Microsoft.EntityFrameworkCore", "DemoSeed__AnchorDate",
              "DemoSeed__Subjects__Patient", "DemoSeed__Subjects__Doctor", "DemoSeed__Subjects__Pharmacist",
              "DemoSeed__Subjects__Administrator", "DemoReset__ExpectedDatabaseName",
              "Logging__LogLevel__Hospital.Infrastructure.Persistence.Initialization.DemoDataResetter")
TEMPLATE_QUERY = ("{state:properties.provisioningState,image:properties.template.containers[0].image,"
                  "command:properties.template.containers[0].command,args:properties.template.containers[0].args,"
                  "cpu:properties.template.containers[0].resources.cpu,memory:properties.template.containers[0].resources.memory,"
                  "init:length(not_null(properties.template.initContainers,`[]`)),volumes:length(not_null(properties.template.volumes,`[]`)),"
                  "env:properties.template.containers[0].env[].{name:name,secret:secretRef,hasValue:value!=`null`},"
                  "values:properties.template.containers[0].env[?contains(`" + json.dumps(PUBLIC_ENV) + "`,name)].{name:name,value:value}}")
# Refuse incomplete/paginated listings rather than accidentally declare the job idle.
ACTIVE_QUERY = "length(value[?properties.status!='Succeeded' && properties.status!='Failed' && properties.status!='Stopped' && properties.status!='Canceled']) > `0` || nextLink || type(value)!='array'"


def configuration(enabled):
    single = {"parallelism": 1, "replicaCompletionCount": 1}
    return {"triggerType": "Schedule" if enabled else "Manual", "replicaRetryLimit": 0,
            "replicaTimeout": 600, "manualTriggerConfig": None if enabled else single,
            "scheduleTriggerConfig": {**single, "cronExpression": CRON} if enabled else None}


def template(maintenance_template):
    result = copy.deepcopy(maintenance_template)
    container = result["containers"][0]
    container["args"] = ["Hospital.Api.dll", "--reset-demo-data"]
    container["env"] += [{"name": "DemoReset__ExpectedDatabaseName", "value": "hospital_coordination"}]
    # The maintenance template already suppresses EF/provider errors.
    container["env"].append({"name": "Logging__LogLevel__Hospital.Infrastructure.Persistence.Initialization.DemoDataResetter", "value": "None"})
    return result


class ResetJob:
    def __init__(self, config, azure, template_factory, wait):
        self.config, self.azure, self.factory, self.wait = config, azure, template_factory, wait
        self.resource = config["base"] + "/jobs/" + RESET_JOB

    def inspect(self):
        job = self.azure.get(self.resource, QUERY)
        environment = self.config["base"] + "/managedEnvironments/" + self.config["AZURE_CONTAINER_APP_ENVIRONMENT"]
        require(job["state"] == "Succeeded" and job["environment"].lower() == environment.lower()
                and job["retries"] == 0 and job["timeout"] == 600 and len(job["names"]) == 1
                and job["secrets"] == [SECRET], "Reset job setup differs from reviewed configuration")
        require(job["trigger"] in ("Manual", "Schedule"), "Unexpected reset trigger")
        key = "schedule" if job["trigger"] == "Schedule" else "manual"
        require(job[key] == configuration(job["trigger"] == "Schedule")[key + "TriggerConfig"], "Unexpected reset schedule/parallelism")
        return job

    def pause(self):
        self.inspect()
        self.azure.mutate(self.resource, "patch", {"properties": {"configuration": configuration(False)}})
        self.wait(lambda: self.azure.get(self.resource, "{state:properties.provisioningState,trigger:properties.configuration.triggerType}"),
                  {"state": "Succeeded", "trigger": "Manual"}, 180)
        self.inspect()
        # Pause prevents new scheduled starts; already started executions must finish.
        self.wait(lambda: "Busy" if self.azure.get(self.resource + "/executions", ACTIVE_QUERY) else "Idle", "Idle", 660)
        print("Reset schedule paused and executions drained; failures leave it paused.")

    def verify_template(self):
        current = self.azure.get(self.resource, TEMPLATE_QUERY)
        container = template(self.factory(self.config, "reset", True))["containers"][0]
        expected = {"state": "Succeeded", "image": self.config["RELEASE_IMAGE"],
                    "command": ["dotnet"], "args": ["Hospital.Api.dll", "--reset-demo-data"],
                    "cpu": 0.25, "memory": "0.5Gi", "init": 0, "volumes": 0,
                    "env": [{"name": item["name"], "secret": item.get("secretRef"), "hasValue": "value" in item} for item in container["env"]],
                    "values": [{"name": item["name"], "value": item["value"]} for item in container["env"] if "value" in item]}
        for key in ("env", "values"):
            current[key] = sorted(current[key], key=lambda item: item["name"])
            expected[key] = sorted(expected[key], key=lambda item: item["name"])
        return current == expected

    def activate(self):
        job = self.inspect()
        require(job["trigger"] == "Manual", "Reset must remain paused until successful delivery")
        body = template(self.factory(self.config, job["names"][0], True))
        self.azure.mutate(self.resource, "patch", {"properties": {"template": body}})
        self.wait(lambda: "Ready" if self.verify_template() else "Updating", "Ready", 180)
        self.azure.mutate(self.resource, "patch", {"properties": {"configuration": configuration(True)}})
        self.wait(lambda: self.azure.get(self.resource, "{state:properties.provisioningState,trigger:properties.configuration.triggerType}"),
                  {"state": "Succeeded", "trigger": "Schedule"}, 180)
        self.inspect()
        print("Reset job uses the successful release digest; daily 11:00 UTC schedule enabled.")


def definition(environment):
    require(re.fullmatch(r"/subscriptions/[0-9a-f-]{36}/resourceGroups/[a-zA-Z0-9-]+/providers/Microsoft.App/managedEnvironments/[a-zA-Z0-9-]+", environment),
            "Public managed environment resource ID required")
    # Inert even if accidentally started; release replaces this after successful smoke.
    return {"name": RESET_JOB, "location": "westus", "properties": {
        "environmentId": environment, "configuration": configuration(False), "template": {"containers": [{
            "name": "reset", "image": "ghcr.io/nginx/nginx-unprivileged@sha256:b8c179cd3c2ae222a873dd59fbae240fadc03836cae5198afc9e9c19919c3880",
            "command": ["/bin/sh"], "args": ["-c", "exit 0"], "resources": {"cpu": 0.25, "memory": "0.5Gi"},
            "env": [{"name": "ConnectionStrings__HospitalMaintenanceDatabase", "secretRef": SECRET}]}],
            "initContainers": [], "volumes": []}}}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Render inert reset-job JSON locally; no Azure calls or secret values.")
    parser.add_argument("--environment-id", required=True)
    print(json.dumps(definition(parser.parse_args().environment_id), indent=2))
