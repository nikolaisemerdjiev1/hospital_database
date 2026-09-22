# Architecture Decision Log

- **Project:** Hospital Coordination Platform modernization
- **Status:** Accepted decisions
- **Date:** July 11, 2026

This log records the important decisions behind the modernization. Each decision should be revisited only when its stated trigger occurs, not merely because another pattern or service is fashionable.

## ADR-001: Preserve legacy history without keeping legacy code in the modern branch

**Status:** Accepted

### Context

The repository contains a freshman-year Java console simulation. The goal is to demonstrate growth after professional experience without leaving obsolete Java, CSV, and compiled artifacts in the active project tree.

### Decision

Tag the current Java release as `legacy-java-v1.0` and retain it through Git history, with an optional GitHub release or legacy branch. Remove the legacy implementation from the modern branch and retain only a short historical document that links to the tag and explains the modernization.

### Rationale

- Creates an authentic before-and-after portfolio narrative.
- Shows the ability to critique and modernize an existing system.
- Keeps the active codebase, CI pipeline, security scans, and reviewer experience focused on the modern application.
- Preserves credit and historical evidence without presenting old code as part of the current architecture.

### Trade-offs

- Reviewers must follow a tag or historical link if they want to inspect the original source.
- Milestone 0 must carefully preserve the correct commit before legacy files are removed.

### Revisit trigger

Create a separate archival repository only if the tag/history approach proves difficult for reviewers to discover.

## ADR-002: Replace Java with ASP.NET Core and React

**Status:** Accepted

### Context

The owner already has a full-stack Java project and is targeting general full-stack/.NET roles.

### Decision

Use .NET 10 LTS, ASP.NET Core, C#, React, TypeScript, and Vite, subject to a compatibility check immediately before scaffolding.

### Rationale

- Expands the portfolio into a new professional ecosystem.
- Demonstrates API design, dependency injection, EF Core, and modern frontend development.
- Aligns with target roles while remaining portable.

### Trade-offs

- The owner must learn ASP.NET Core conventions during delivery.
- Reusing the original Java code is less valuable than preserving its behavior and history.

### Revisit trigger

Revisit only if target roles change materially or current supported framework versions become incompatible.

## ADR-003: Use a feature-oriented modular monolith

**Status:** Accepted

### Context

The project is owned by one developer, needs to become resume-ready quickly, and has one cohesive relational workflow.

### Decision

Deploy one ASP.NET Core API organized into Api, Core, and Infrastructure projects and feature modules. Core owns a narrow `IApplicationDbContext` and external-service contracts; Infrastructure supplies the concrete EF Core/Npgsql and HTTP implementations. Do not add a generic repository per entity.

### Alternatives considered

- One unstructured controller/service/data project
- Full Clean Architecture with CQRS, MediatR, and repository abstractions
- Microservices and asynchronous messaging

### Rationale

- Keeps the request path understandable.
- Provides meaningful boundaries without distributed-system overhead.
- Allows modules to be extracted later if a real need appears.

### Trade-offs

- Boundaries rely partly on code discipline rather than network isolation.
- Some application and domain concepts share the Core project.

### Revisit trigger

Revisit when independently scaled modules, a larger team, or clearly separate release cadences become real requirements.

## ADR-004: Use PostgreSQL with EF Core and Npgsql

**Status:** Accepted

### Context

Appointments, consultations, prescriptions, and fulfillments are relational. External APIs may also produce JSON snapshots.

### Decision

Use PostgreSQL as the system of record through EF Core and Npgsql.

### Alternatives considered

- MySQL with Oracle Connector/NET or Pomelo
- CSV persistence from the legacy project
- A document database

### Rationale

- Strong relational constraints and transactions fit the workflow.
- Npgsql provides a direct EF Core provider path.
- JSONB supports integration snapshots without weakening the normalized model.
- PostgreSQL leaves useful future search options.

### Trade-offs

- PostgreSQL-specific JSON or search features reduce automatic database portability.
- Some available PostgreSQL features exceed current needs and must not be added for appearance.

### Revisit trigger

Revisit if the selected host strongly favors MySQL, a target employer specifically requires it, or provider compatibility changes.

## ADR-005: Use Auth0 Free with a custom role claim

**Status:** Accepted

### Context

The project must demonstrate Auth0, but the current Free plan does not include built-in Role Management and paid Auth0 RBAC is disproportionate for four fixed demo users.

### Decision

Use Auth0 for Universal Login and API access tokens. Store a protected `app_role` in each demo user's Auth0 `app_metadata`. Add it to the access token through a Post Login Action. Enforce role policies and record ownership in ASP.NET Core.

Create the fixed Auth0 demo identities before the production seed, pass their non-secret Auth0 subjects into the seed configuration, and require the signed token role to match the active local profile type. Unknown, inactive, or mismatched profiles are denied rather than silently repaired.

### Alternatives considered

- Auth0 Essentials with built-in Role Management
- Store roles only in PostgreSQL and query them on each request
- Replace Auth0 with ASP.NET Core Identity

### Rationale

- Demonstrates real OAuth/OIDC integration at no recurring identity cost.
- Keeps authorization logic visible and teachable in ASP.NET Core.
- Signed claims cannot be modified by the browser.

### Trade-offs

- This is not formal Auth0 RBAC and must not be described as such.
- Manual metadata assignment is appropriate only for a small fixed demo population.
- Role changes take effect after token renewal.

### Revisit trigger

Adopt formal RBAC or a database role source when users or permissions become dynamic, self-service administration is needed, or a paid Auth0 plan becomes justified.

## ADR-006: Integrate RxNorm first and openFDA second

**Status:** Accepted

### Context

The project needs a meaningful external API integration that is understandable without medical expertise.

### Decision

Use RxNorm for standardized medication search in Phase 1. Add openFDA label, warning, and recall enrichment in Phase 2.

### Rationale

- RxNorm naturally supports the prescription workflow.
- Standard identifiers prevent duplicate or inconsistent medication names.
- Phasing protects the resume-ready timeline.

### Trade-offs

- External availability and response formats are outside project control.
- Caching and seeded fallbacks add implementation work.

### Revisit trigger

Replace or supplement the provider if official availability, terms, response quality, or project needs change.

## ADR-007: Use Azure for the public application and Neon for PostgreSQL

**Status:** Accepted

### Context

The target is a low-traffic public demo for .NET roles with minimal recurring cost.

### Decision

Host React on Azure Static Web Apps, the API on Azure Container Apps, container images on GitHub Container Registry, and PostgreSQL on Neon.

### Alternatives considered

- AWS Amplify plus App Runner or Lambda
- All-in-one hobby platforms
- Azure Database for PostgreSQL
- PlanetScale Postgres

### Rationale

- Azure strengthens the .NET portfolio narrative.
- Container Apps runs the normal API container and can scale to zero.
- Neon keeps PostgreSQL costs low for an intermittent demo.
- Standard containers and PostgreSQL preserve portability.

### Trade-offs

- Cross-provider configuration adds setup and CORS concerns.
- Scale-to-zero introduces a possible first-request delay.
- Free tiers have no production SLA and may change.

### Revisit trigger

Recheck all prices and limits before provisioning. Change providers if cost, cold starts, service availability, or target-job requirements materially change.

## ADR-008: Make GitHub Actions part of the product evidence

**Status:** Accepted

### Context

The repository should visibly demonstrate validation, security practices, and automated delivery.

### Decision

Use GitHub Actions for CI, security checks, deployment, smoke testing, and demo reset. Protect `main` with required checks and use GitHub environments for deployment controls. Run an EF Core migration bundle as a one-off Azure Container Apps Job before shifting traffic to the new API revision; failed migrations leave the prior revision active.

### Rationale

- Reviewers can inspect real workflow definitions and status badges.
- Automated checks reduce regression and deployment risk.
- Environment secrets and OIDC demonstrate modern credential handling.

### Trade-offs

- Workflows require maintenance as action and tool versions change.
- Browser and container tests increase CI duration.
- Public-demo resets need carefully scoped database credentials.

### Revisit trigger

Split or optimize workflows when runtime, cost, flakiness, or provider changes justify it.

## ADR-009: Optimize for a task-centered, role-adaptive interface

**Status:** Accepted

### Context

Patients, doctors, pharmacists, and administrators have different goals and levels of information density.

### Decision

Use a calm clinical visual language with role-specific workspaces and a shared care-relay timeline.

### Rationale

- Users see the next relevant action instead of generic dashboard metrics.
- The care relay makes the coordinated workflow memorable and understandable.
- Accessibility and failure recovery are treated as architecture requirements.

### Trade-offs

- Separate role experiences require more design and browser testing than one generic dashboard.
- Visual tokens must be validated for contrast before implementation.

### Revisit trigger

Revise the information architecture after usability testing reveals repeated confusion or inefficient task completion.

## ADR-010: Deliver scheduling as a patient-only vertical slice

**Status:** Accepted

### Context

Milestone 3 needs to prove an authenticated end-to-end workflow without expanding into every role at once. Scheduling also has ownership, time-bound validation, double-booking, and stale-update risks that cannot live safely in UI code.

### Decision

Let patients browse seeded clinician availability, list their own appointments, book a slot, and cancel an upcoming scheduled appointment. Implement the business rules as concrete Core transaction-script use cases over `IApplicationDbContext`. Use the existing PostgreSQL filtered unique index as the final double-booking guard and `xmin` for optimistic concurrency.

### Alternatives considered

- Add doctor and administrator slot authoring in the same milestone
- Put scheduling rules directly in API controllers
- Introduce MediatR, generic repositories, CQRS, or a domain-event bus
- Rely only on an application-level availability check

### Rationale

- Produces one complete, demonstrable patient journey quickly.
- Keeps authorization and ownership rules testable outside the UI.
- Reuses the existing modular-monolith and EF Core boundaries.
- Handles real concurrent booking behavior without premature infrastructure.

### Trade-offs

- Seeded availability must be refreshed deliberately for a current-looking public demo.
- Clinicians cannot manage their own schedules yet.
- Runtime audit events remain deferred until a later milestone introduces the required transaction boundary.

### Revisit trigger

Add slot authoring, stronger schedule-overlap enforcement, and a transaction abstraction when clinician workflows or multi-write audit requirements enter scope.

## ADR-011: Deploy the portfolio demonstration across Azure and Neon

**Status:** Accepted

### Context

Milestone 6 must produce an accessible public demo, demonstrate cloud delivery for .NET roles, and avoid recurring cost for a single-maintainer portfolio project.

### Decision

Deploy React to Azure Static Web Apps Free, the ASP.NET Core container to Azure Container Apps Consumption with zero minimum replicas, and PostgreSQL to Neon Free. Publish immutable SHA-tagged images to public GitHub Container Registry. Use a documented Azure CLI bootstrap rather than a large infrastructure-as-code layer for the single production environment.

### Alternatives considered

- AWS-hosted frontend and API services
- Azure Database for PostgreSQL
- An all-in-one hobby hosting provider
- A complete Bicep deployment for every external resource

### Rationale

- Azure reinforces the target .NET portfolio narrative.
- Scale-to-zero and the selected free services fit intermittent recruiter traffic.
- Containers and standard PostgreSQL keep the application portable.
- A small documented bootstrap is easier for one maintainer to understand and operate.

### Trade-offs

- Cross-provider networking, secrets, and CORS need explicit configuration.
- The first API request may wait for a cold start.
- Free services provide no production availability commitment.
- Some resources are provisioned through documented external control-plane steps rather than one declarative deployment.

### Revisit trigger

Introduce Bicep or another infrastructure-as-code system when staging, multiple deployments, or repeatable disaster recovery becomes a real requirement.

## ADR-013: Serve the React SPA and API together on Container Apps

**Status:** Accepted, September 13, 2026. Supersedes the separate frontend-hosting portions of ADR-011 and the earlier Azure hosting decision.

### Context

The student subscription's allowed locations have no intersection with Static Web Apps
regions. The quota-request route asks for a paid subscription upgrade. The existing West US
Container Apps Consumption environment is available, and preserving near-zero recruiter-demo
costs matters more than adding another hosting provider.

### Decision

Build React with locked Node dependencies, copy its static output into the non-root .NET
image, and serve the SPA and bearer-token API from one Container App origin. Retain Neon
Oregon, Auth0 SPA authentication, public GHCR and scoped GitHub OIDC. Start with zero minimum
replicas; validate 0.25 vCPU / 0.5 GiB before release. No Static Web Apps token is required.

### Alternatives and trade-offs

- A subscription-policy exception might retain the original static host, but approval and
  a working support route are unconfirmed; do not upgrade billing to bypass the restriction.
- An independent free static provider preserves an immediately available landing page but
  adds a provider and delivery path. Revisit if hosted first-load latency is unacceptable.
- Always-on Container Apps avoids idle cold starts but consumes ongoing resources; it is
  not approved as an automatic fix.
- Combined hosting simplifies origins, builds and rollback, but initial HTML waits for
  container startup and frontend availability is coupled to the server process.

### Consequences

React remains a SPA, not server-rendered HTML. API authorization and ownership stay intact;
only static assets and shell routes are anonymous. Separate frontend/API CSP and cache
policies prevent the SPA fallback from disguising API errors or exposing configuration.
Platform probes use process-only liveness; visitor readiness checks wake the database.
The approximate 200 running hours inside the monthly compute grant are a cost estimate,
not measured capacity or a guarantee. Hosted cold-start, authenticated memory/load and
production Auth0 checks remain release gates. See [combined hosting](../development/combined-hosting.md).

## ADR-012: Reset only the allowlisted synthetic dataset in one transaction

**Status:** Accepted

### Context

Public visitors share mutable synthetic appointments, consultations, prescriptions, and fulfillments. The demo needs predictable recovery without exposing a destructive HTTP endpoint or weakening the existing non-destructive initializer.

### Decision

Keep `--initialize-database` as the migrate-and-seed-empty command. Add a separate `--reset-demo-data` maintenance mode that validates the expected database, identity bindings, UTC anchor, and migration state; acquires a PostgreSQL transaction advisory lock; truncates an explicit application-table allowlist with `RESTART IDENTITY RESTRICT`; reseeds and verifies the canonical dataset; and commits once. Preserve EF migration history and expose reset only through scheduled and protected operator jobs.

### Alternatives considered

- Reset through a public administrator endpoint
- Drop and recreate the production database
- Use `TRUNCATE ... CASCADE`
- Delete rows individually through EF Core
- Restore a database snapshot

### Rationale

- One database transaction prevents reviewers from observing a partially rebuilt workflow.
- An allowlist fails when an unknown dependency appears instead of silently erasing it.
- The same deterministic seeder keeps local, test, and production behavior aligned.
- Maintenance-mode execution has no public application route.

### Trade-offs

- The transaction briefly locks all application tables.
- New domain tables must be added deliberately to the reset allowlist and invariant checks.
- A failed or concurrent reset must be surfaced operationally and retried later.

### Revisit trigger

Replace full-dataset reset with tenant- or session-isolated demos if traffic, data volume, reset duration, or concurrent visitor disruption becomes material.
