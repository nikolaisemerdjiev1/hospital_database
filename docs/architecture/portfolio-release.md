# Milestone 6: Portfolio release architecture

> **Release checkpoint (2026-09-23):** The released SHA is `4f52f261cdc0b68b92848220ae0b7f0a11f5dba1`; the combined release and one protected manual reset succeeded. The scheduled reset was then observed succeeding as `job-harbor-care-reset-29836020` at 11:00:00–11:00:28 UTC. This document preserves the accepted architecture; [release evidence](../releases/milestone-06-evidence.md) distinguishes observed facts from design intent.

- **Status:** Approved for implementation
- **Date:** September 9, 2026
- **Scope:** Zero-cost portfolio deployment, production safeguards, deterministic demo recovery, release evidence, and documentation

## Outcome

Milestone 6 turns the completed coordinated-care vertical slice into a public, production-style portfolio demonstration. It does not turn the application into a real clinical system. All records remain fictional and synthetic, availability is best effort, and the project makes no healthcare-regulatory compliance claim.

The release must be understandable to a recruiter, safe to expose as a shared demonstration, reproducible for local review, and inexpensive to leave online. The target audience is recruiters, hiring managers, and technical reviewers for full-stack and .NET roles.

## Deployment topology

```text
Browser
  |
  +--> Auth0 Universal Login
  |
  +--> Azure Container Apps Consumption (one origin)
       ASP.NET Core serves the React SPA and bearer-token API
           |
           +--> Neon PostgreSQL Free
           |
           +--> RxNorm

GitHub Actions
  +--> CI, security, and container validation
  +--> public SHA-tagged image in GHCR
  +--> one-shot migration job
  +--> combined application deployment
  +--> post-deployment smoke tests
  `--> protected manual demo reset
```

The combined app scales to zero. The initial HTML waits for container startup; no in-page message can cover the time before that HTML arrives. Once ASP.NET Core starts, the landing page remains available while its bounded checks wake Neon. Platform startup, liveness, and traffic-readiness probes use database-independent `/health/live`; `/health/ready` remains database-backed for visitor readiness and bounded release smoke. Continuous database probes would prevent Neon sleeping and make database outages hide the landing page. West US hosting and Neon Oregon remain the selected regions. Free services have no production service-level objective; this is a best-effort public demo. ADR-013 supersedes the separate static-host design.

## Identity and configuration

Local and production frontends use separate Auth0 SPA clients and the same generic database-connection users. Patient, doctor, and pharmacist credentials are intentionally public, project-only credentials. The administrator seed uses `demo-seed|administrator`, a synthetic subject with no login-capable Auth0 account.

Auth0 passwords, mailbox credentials, recovery codes, client secrets, database connection strings, and Azure credentials never enter Git history. Auth0 domains, SPA client IDs, API audiences, role-claim names, application origins, and Auth0 subjects are identifiers rather than secrets. The public demo password is supplied through ignored local configuration and a GitHub production-environment secret; it is intentionally visible in the compiled public landing page.

Production configuration fails closed when the exact frontend origin, database connection, Auth0 issuer/audience/role claim, demo subjects, or reset guard values are absent or invalid.

## Recruiter entry and API readiness

The landing page preserves the Harbor Care typography, navy/teal palette, and care-handoff motif. Its role cards offer account-specific email hints with `prompt=login`, then return through the existing Auth0 callback to the workspace selected from the verified identity. This avoids treating a requested role as an authorization decision and makes deliberate account changes available to an already signed-in visitor.

Auth0 tokens remain in memory. After a reload, the SDK first attempts session recovery. If it cannot restore a previously authenticated tab on a protected route, the application makes one top-level Auth0 redirect, preserving the requested path, query, and fragment. A tab-local `sessionStorage` flag records only that authentication previously succeeded; it contains no token, identity, or role and grants no access. Recovery consumes the flag before redirecting, and explicit sign-out clears it. Failed or cancelled recovery does not loop, first-time visitors keep the normal sign-in screen, and the public landing page never starts automatic recovery. Auth0 can reuse an existing session or require interactive login when needed.

A separate readiness function and React hook own anonymous database-readiness checks, per-request cancellation, bounded serial retry, and server-requested cooldown. This keeps maintenance commands and authenticated workflow mutations outside the wake-up path. Once the server can deliver HTML, the public page remains readable during database and Auth0 initialization. Four attempts with eight-second request timeouts and 2/4/8-second backoff avoid indefinite polling; explicit retry follows exhaustion. `Retry-After` is exposed through the existing exact-origin CORS policy so browsers can observe the cooldown.

The shared password is injected through public build configuration and revealed or copied only on a visitor's request. Its absence produces setup-neutral visitor guidance. Actual credentials are never test fixtures: scoped Chromium and axe tests substitute a fake public value and fake Auth0 domain, exercise the real SDK redirect boundary, and intercept readiness responses. They supplement manual role-flow acceptance and do not certify accessibility or validate a live Auth0 tenant.

## Delivery and migration

Deployment begins only after CI succeeds for the exact commit on `main`. GitHub Actions authenticates to Azure through OpenID Connect instead of a stored Azure password. The API container is built once, tagged with the immutable Git SHA, scanned, published to public GHCR, and reused by application and maintenance jobs.

A one-shot Azure Container Apps job runs `--prepare-release` from the same immutable image against the direct Neon maintenance connection before a new API revision receives traffic. It uses the compiled EF migrations, seeds only an empty database, and applies embedded, allowlisted runtime grants. This replaces the proposed separate EF bundle, avoiding a second artifact and tying migrations, seed and grants to the released code. A failed migration leaves the previous API revision active. Application rollback copies the previous ready revision's template; database changes use a reviewed forward corrective migration rather than an automated downgrade. See [delivery and remaining setup](../development/delivery.md) for exact-main gates, permission boundaries and failure handling.

There is no Static Web Apps resource or deployment token. One image contains the public frontend build and .NET runtime; Node is a build dependency only. Runtime and direct maintenance database connections live in Azure secrets. A GitHub `production` environment contains only the variables and secrets required by delivery automation. Frontend and API roll back together.

## Transactional demo reset

The existing `--initialize-database` command remains non-destructive: it migrates and seeds only an empty database. A separate `--reset-demo-data` maintenance mode resets the known synthetic dataset without starting the web server.

Before changing data, reset validates:

- the command-line mode is unambiguous;
- the configured database name equals the expected production database name;
- all configured Auth0 subjects are present, unique, and valid;
- the seed anchor resolves to a UTC date;
- production uses the exact `hospital_maintenance` role and `hospital_coordination` database;
- applied migration IDs match the image exactly, checked after taking the shared maintenance lock.

The reset opens one short PostgreSQL transaction and applies lock and statement timeouts. It acquires transaction advisory lock `7239061401`, shared with release maintenance's session lock, before checking migration history, then takes the seed lock and truncates an explicit allowlist of application tables with `RESTART IDENTITY RESTRICT`. It never uses `CASCADE` and never truncates `__EFMigrationsHistory`. It reseeds the deterministic current-date dataset, verifies counts, identity/profile composition, bookable availability, and workflow states, and commits only when every invariant passes. An exception rolls the entire operation back.

The scheduled reset runs at 11:00 UTC daily through an Azure Container Apps job, enabled only after successful separately authorized delivery. A protected `workflow_dispatch` GitHub workflow may start the same job after the operator types `RESET_SYNTHETIC_DEMO`; current main, the stable app and reset job must match SHA/digest. Release and manual reset share GitHub concurrency, and pause/drain the Azure schedule before maintenance. The database lock covers late scheduler starts; normal web workflows can still be interrupted by reset. There is no HTTP reset endpoint. See the [delivery runbook](../development/delivery.md) for failure handling and the single remaining setup checklist.

## Public-demo safeguards

ASP.NET Core uses subject-aware partitioned rate limiting with trusted client-address handling. General requests, short bursts, mutations, RxNorm search, readiness probes, and request concurrency have separate proportional limits. Rejections return Problem Details with `429 Too Many Requests` and `Retry-After` without disclosing internal policy details.

Structured JSON logs record route templates, status, latency, trace identifiers, and maintenance outcomes without request bodies, bearer tokens, credentials, or connection strings. Azure built-in metrics, logs, revision state, and job history provide enough monitoring for the portfolio scale. Auth0 attack protection remains enabled, and recovery favors IP-level containment over locking the shared accounts for every visitor.

Security headers, exact-origin CORS, existing fail-closed authorization, ownership checks, bounded pagination, optimistic concurrency, and transaction boundaries remain defense-in-depth controls. The public deployment uses only synthetic data and performs no intrusive production security testing.

## Quality and release evidence

The final gate includes:

- .NET formatting, build, unit, API, and real-PostgreSQL integration tests;
- frontend lint, type checking, component tests, and production build;
- Playwright browser checks with axe-assisted accessibility coverage;
- manual keyboard, focus, screen-reader, contrast, 200% zoom, 320-pixel reflow, reduced-motion, touch, and three-role workflow checks;
- CodeQL, dependency review, NuGet/npm audit, secret scanning, push protection, and final-container scanning;
- migration-drift, configuration, non-root container, health, CORS, authentication, authorization, rate-limit, and passive post-deployment smoke checks.

Automated CI does not use live Auth0 passwords. Real Auth0 validation is a manual release check for the three public roles.

## Documentation and portfolio presentation

The README becomes a recruiter-first overview with a live-demo link, one product image, public role guidance, the care-relay story, architecture, engineering highlights, local setup, limitations, and the modernization narrative. Detailed deployment, reset, recovery, security, testing, and demo instructions live in focused documents.

Five deterministic screenshots cover the public landing page, patient workspace, doctor consultation and RxNorm search, pharmacist fulfillment, and responsive mobile experience. Two versioned Excalidraw diagrams cover the deployment architecture and the coordinated-care journey; exported SVG files are embedded in documentation. Mermaid remains the format for detailed deployment and reset sequences.

The release concludes with a `v1.0.0` GitHub release, a configured repository description/live URL/topics/social preview, and factual resume bullets covering the full-stack architecture, transactional workflows, external API resilience, and CI/CD delivery.

## Implementation sequence and manual gates

1. Create `milestone/06-portfolio-release` from synchronized `main` and persist this plan.
2. Implement production configuration, logging, health, rate limiting, security headers, and container safeguards.
3. Implement and test the transactional reset maintenance mode.
4. Stop for creation of generic Auth0 identities and validate them locally.
5. Add the public landing/cold-start experience and release-quality automation.
6. Stop for Neon provisioning.
7. Stop for Azure bootstrap and GitHub OIDC.
8. Stop for exact production Auth0 origins and API authorization.
9. Stop for GitHub production variables and secrets.
10. Merge only after final review, deploy from `main`, and run passive smoke checks.
11. Stop for manual three-role production acceptance.
12. Capture stable assets, finish documentation, publish `v1.0.0`, and prepare resume/interview material.

No code is committed until the milestone's final pre-commit review and complete validation gates pass.

## Risks and accepted trade-offs

- Scale-to-zero can delay the first API request; the static frontend communicates and retries readiness.
- Free service limits and availability can change; local Docker and Auth0 instructions remain the fallback.
- Shared credentials and mutable shared state can be abused; rate limits, Auth0 protection, deterministic resets, and documented recovery limit impact.
- Cross-provider hosting adds CORS and secret-management work; exact configuration validation and documented ownership make the boundary explicit.
- A documented Azure CLI bootstrap is used instead of extensive infrastructure-as-code because there is one owner and one free environment. Reconsider Bicep when multiple environments or repeatable disaster recovery become real requirements.

## Completion criteria

Milestone 6 is complete when the release can be opened publicly, all three roles complete their intended synthetic workflow, scheduled and protected manual resets restore the canonical dataset atomically, production configuration contains no committed secrets, CI/CD and security checks pass, documentation works for a fresh reader, and the repository presents a truthful modernization story.
