"""Read-only exact-main-commit gates. Never executes artifacts from another workflow."""

import json
import os
import re
import sys
import urllib.error
import urllib.request


class ReleaseFailure(Exception):
    pass


def require(condition, message):
    if not condition:
        raise ReleaseFailure(message)


class GitHub:
    def __init__(self, repository, token):
        require(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository), "Invalid repository")
        self.base = f"https://api.github.com/repos/{repository}"
        self.token = token

    def get(self, path):
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, req, fp, code, msg, headers, newurl):
                return None

        request = urllib.request.Request(self.base + path, headers={
            "Authorization": f"Bearer {self.token}", "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "hospital-release-gate",
        })
        try:
            with urllib.request.build_opener(NoRedirect).open(request, timeout=20) as response:
                data = response.read(2_000_001)
                require(len(data) <= 2_000_000, "GitHub response too large")
                return json.loads(data)
        except (OSError, ValueError, urllib.error.URLError):
            raise ReleaseFailure("GitHub gate query failed; no deployment allowed") from None


def check(github, sha, repository):
    require(re.fullmatch(r"[0-9a-f]{40}", sha), "Release must be a full lowercase commit SHA")
    require(github.get("/git/ref/heads/main")["object"]["sha"] == sha, "Requested commit is not current main")
    checks = {
        "ci.yml": {"Backend", "Frontend", "Combined application container", "Workflow and smoke tooling"},
        "codeql.yml": {"CodeQL (csharp)", "CodeQL (javascript-typescript)", "CodeQL (actions)"},
    }
    for workflow, expected in checks.items():
        runs = github.get(f"/actions/workflows/{workflow}/runs?head_sha={sha}&branch=main&event=push&per_page=100")["workflow_runs"]
        valid = [run for run in runs if run.get("head_sha") == sha and run.get("head_branch") == "main"
                 and run.get("event") == "push" and run.get("head_repository", {}).get("full_name") == repository]
        require(valid, f"No trusted main push run for {workflow}")
        latest = max(valid, key=lambda run: run["id"])
        require(latest.get("status") == "completed" and latest.get("conclusion") == "success", f"Latest {workflow} run did not pass")
        jobs = github.get(f"/actions/runs/{latest['id']}/attempts/{latest['run_attempt']}/jobs?per_page=100")["jobs"]
        passed = {job["name"] for job in jobs if job.get("status") == "completed" and job.get("conclusion") == "success"}
        require(expected <= passed, f"Required jobs missing or skipped in {workflow}")


def main():
    try:
        require(os.environ.get("GITHUB_REF") == "refs/heads/main", "Dispatch from main only")
        sha = os.environ["RELEASE_SHA"]
        require(sha == os.environ["GITHUB_SHA"], "Dispatch commit and requested release differ")
        check(GitHub(os.environ["GITHUB_REPOSITORY"], os.environ["GH_TOKEN"]), sha, os.environ["GITHUB_REPOSITORY"])
        print("PASS: exact current main SHA, CI and CodeQL jobs")
    except (ReleaseFailure, KeyError, TypeError):
        print("Release gate failed; verify exact main SHA and successful CI/CodeQL push runs.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
