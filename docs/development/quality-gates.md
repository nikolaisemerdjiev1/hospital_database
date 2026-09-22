# Quality and security gates

Milestone 6 extends the existing CI without granting deployment access to quality jobs.
PR checks use `pull_request`, never `pull_request_target`; checkout credentials are not persisted. Tests
use synthetic CI values and isolated PostgreSQL databases. Real Auth0 accounts, passwords,
production connection strings, and clinical browser mutations are unnecessary.

## Automated gates

| Job | Required behavior |
| --- | --- |
| Backend | Locked restore, all-severity direct/transitive NuGet audit, formatting, Release build, migration snapshot drift check, full tests against disposable PostgreSQL |
| Frontend | Locked install, lint, types, unit tests, npm audit at moderate severity, Chromium/axe tests with fake authentication, production build with fake configuration |
| API container | Compose validation, image build, UID `1654`, Trivy high/critical scan including unfixed findings, read-only/non-root production container, anonymous smoke checks |
| Workflow and smoke tooling | Python regression tests and checksum-pinned actionlint; Linux CI also checks embedded shell commands through ShellCheck |
| CodeQL | Separate C#, JavaScript/TypeScript, and Actions analyses using `security-extended`; C# uses a traced Release build |
| Dependency review | Reject newly introduced moderate-or-higher vulnerable dependencies on pull requests; no PR comments or write token |

CI and CodeQL run for pull requests, pushes to `main`, manual dispatch, and weekly schedules.
The schedules catch newly disclosed vulnerabilities without requiring a code change.
Dependency review requires the PR base/head comparison and runs only on pull requests.
Feed, scanner, checksum, and audit failures fail their job; no vulnerability ignore list is supplied.

CodeQL alone receives `security-events: write` for analysis upload; it has no deployment,
package-publishing, or repository-write permission. Uploading CodeQL findings does not itself
enforce an alert-severity merge threshold. Before release, confirm repository code scanning
and dependency graph availability, configure the desired code-scanning ruleset and required
status checks, and review the actual GitHub results. Secret scanning and push protection are
separate repository settings; these workflow files do not enable them. Local validation is
not evidence that GitHub jobs, settings, or production acceptance have passed.

## Local commands

Use the repository's .NET and Node toolchains. The existing development guide covers backend,
frontend, migration, and PostgreSQL test setup. Run these additional checks from the root:

```shell
dotnet restore Hospital.slnx --locked-mode
python scripts/audit_nuget.py
python -m unittest discover -s scripts -p 'test_*.py'
python scripts/install_tool.py actionlint --directory .artifacts/tools
python scripts/install_tool.py trivy --directory .artifacts/tools
```

Then run `.artifacts/tools/actionlint` and build and scan the final image:

```shell
docker compose config --quiet
docker build --file backend/Hospital.Api/Dockerfile --tag hospital-api:local --build-arg VITE_AUTH0_DOMAIN=demo-auth.example.invalid --build-arg VITE_AUTH0_CLIENT_ID=browser-test-client --build-arg VITE_AUTH0_AUDIENCE=https://demo-api.example.invalid .
.artifacts/tools/trivy image hospital-api:local --scanners vuln --severity HIGH,CRITICAL --exit-code 1 --skip-version-check --timeout 10m
```

This local build supplies required fake public settings and omits the optional public demo
password; CI supplies a fake BuildKit secret for password-control browser coverage.
Windows executables have an `.exe` suffix; PowerShell accepts the commands above. The installer
supports Windows/Linux x64, verifies the full archive SHA-256 before writing anything, and
extracts only the named executable. `.artifacts/` is ignored and excluded from Docker builds.
The NuGet wrapper suppresses raw package-feed diagnostics, which may contain private URLs;
its nonzero exit covers vulnerable packages and incomplete or unavailable audit data.

All actions use full commit pins. Dependabot maintains action, npm, NuGet, SDK, and container
updates. Direct tool releases need a reviewed update to `scripts/tool-versions.json`: verify
the official release and platform archive checksums, change both supported platform entries,
and rerun installer tests, actionlint, and the image scan. No downloaded installer script runs.

The final API image uses the digest-pinned ASP.NET Core 10.0.12 Alpine image. The initial
10.0.9 image failed the new gate with eight high-severity findings (OpenSSL and .NET runtime);
the patched image passed without exclusions. The build-stage SDK is independently pinned
to the existing repository toolchain; build tools are absent from the shipped image.

## Passive release smoke

After a separately authorized deployment, use public origins and the deployed source revision:

```shell
python scripts/smoke.py --api-url https://api.example.com --frontend-origin https://demo.example.com --frontend-url https://demo.example.com --expected-revision <40-character-lowercase-git-sha>
```

Replace the placeholders locally. No password, token, cookie, or database connection string
is an input. The script sends anonymous GETs only, does not follow redirects, and never
prints response bodies. HTTPS is mandatory except for explicitly enabled loopback HTTP
(`--allow-local-http`) when checking disposable containers.

Checks cover PostgreSQL readiness, exact frontend CORS origin and exposed `Retry-After`,
liveness, the Production system identity and expected revision, a fresh timestamp, API
security headers, and anonymous `401` denial at `/api/v1/identity/me`. Supplying
`--frontend-url` also checks the HTML application shell, SPA deep links, frontend CSP and
revalidation, and missing API/health/asset fallthrough. For combined hosting, all three URL
arguments use the same origin. Readiness has at most ten attempts
and a 90-second retry budget, requests have at most ten-second socket timeouts, and bodies
are capped at 64 KiB. `Retry-After` delays are honored; a cooldown beyond the budget fails
without another request. Other endpoints are checked once. Scheduler/socket behavior can
add overhead to the retry budget; CI also has a job timeout.

CI uses a dedicated empty PostgreSQL service and a production combined container with fake
configuration, a read-only root filesystem, dropped capabilities, and no added privileges.
The empty database is sufficient for connection readiness; it is not evidence of a deployed
schema or seeded workflows. This smoke does not migrate, seed, reset, authenticate, book,
prescribe, or dispense. Chromium also tests the real container CSP, role-card SDK redirects,
deep-link reload, 320/768/1440 reflow, keyboard, reduced motion and axe at 0.25 CPU / 512 MiB.
See [combined hosting](combined-hosting.md). HTTPS termination/HSTS and hosted cold starts must be checked after
deployment; local HTTP container success does not prove those production properties.

CI also runs `check_release_container.py` against a separate labelled disposable database
to exercise the release image's maintenance/reset modes, shared-lock contention in both
directions, concurrent resets, rollback, exact migration history and preserved runtime grants.
The delivery workflow requires exact-main CI/CodeQL success, protected environment approval,
a scanned immutable image and passive post-release smoke; see [delivery.md](delivery.md).
Manual three-role production acceptance and actual cloud execution remain separate release
checkpoints. Local fixtures do not prove a hosted release.

## References

- [CodeQL action and language setup](https://github.com/github/codeql-action)
- [Dependency review configuration](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/manage-your-dependency-security/configure-dependency-review-action)
- [.NET 10 servicing releases](https://github.com/dotnet/core/blob/main/release-notes/10.0/README.md)
- [Trivy releases](https://github.com/aquasecurity/trivy/releases)
