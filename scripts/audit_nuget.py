"""Fail on vulnerable direct/transitive NuGet packages or an incomplete audit."""

import json
import subprocess
import sys
from pathlib import Path


def check_report(report):
    projects = report.get("projects")
    if report.get("version") != 1 or not isinstance(projects, list) or not projects or report.get("problems"):
        raise ValueError("NuGet audit returned an incomplete report")
    findings = 0
    for project in projects:
        if not isinstance(project, dict) or not project.get("path"):
            raise ValueError("NuGet audit returned an invalid project")
        for framework in project.get("frameworks", []):
            for group in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(group, []):
                    findings += len(package.get("vulnerabilities", []))
    if findings:
        raise ValueError(f"NuGet audit failed: {findings} vulnerable package/advisory occurrences")
    return len(projects)


def main():
    try:
        result = subprocess.run(
            ["dotnet", "list", "Hospital.slnx", "package", "--vulnerable", "--include-transitive",
             "--format", "json", "--no-restore"],
            cwd=Path(__file__).resolve().parent.parent, capture_output=True, text=True,
            encoding="utf-8", timeout=300, check=False,
        )
        if result.returncode:
            raise ValueError("NuGet audit command failed; restore dependencies and verify feed access")
        count = check_report(json.loads(result.stdout))
    except (ValueError, TypeError, AttributeError, OSError, subprocess.TimeoutExpired):
        # Package-source diagnostics may contain private feed URLs; do not echo raw output.
        print("NuGet audit failed: vulnerabilities, invalid report, or unavailable package feed.", file=sys.stderr)
        return 1
    print(f"NuGet audit passed: {count} projects, no known vulnerable direct/transitive packages.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
