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
