# Solution foundation

- **Milestone:** 1 - Foundation
- **Branch:** `milestone/01-solution-foundation`
- **Purpose:** Create a small, testable base for every later vertical slice.

## Project boundaries

```text
Hospital.Api --------------------> Hospital.Core
     |
     `----> Hospital.Infrastructure ----> Hospital.Core

Hospital.Core.Tests ------------> Hospital.Core
Hospital.Api.IntegrationTests --> Hospital.Api
```

`Hospital.Api` is the HTTP front door and **composition root**. A composition root is the one place where concrete application pieces are registered and connected. It owns controllers, middleware, configuration, CORS, OpenAPI, and the host process.

`Hospital.Core` will own use cases, workflow rules, and interfaces for capabilities such as persistence and medication lookup. It must not reference API or Infrastructure. An architecture test currently enforces that direction.

`Hospital.Infrastructure` will implement Core-owned interfaces with EF Core, Npgsql, PostgreSQL, RxNorm, and later openFDA. It references Core because an implementation must know the contract it implements; Core does not know that implementation exists.

## How an ASP.NET Core request moves

```text
HTTP request
  -> request logging
  -> exception handler
  -> status-code handling
  -> endpoint routing and route-template capture
  -> CORS
  -> authorization
  -> controller/endpoint execution
  -> HTTP response
```

ASP.NET Core middleware is an ordered pipeline. Each middleware can inspect a request, call the next component, and inspect the response on the way back out. Ordering matters: request logging and the exception handler wrap routing so even routing failures are observed, routing selects a stable endpoint template before a small capture middleware preserves it, and authorization runs before protected controller actions. The exception handler converts later failures into Problem Details, while request logs use the captured route template, final handled status, and the same trace identifier returned to the client.

Controllers translate HTTP input into application calls and application results back into HTTP responses. They should not contain database queries or workflow decisions. Those decisions will live in Core as features are added.

## Foundation capabilities

| Capability | Implementation | Why it exists |
|---|---|---|
| Configuration | `appsettings.json`, environment variables, validated options, user-secrets ID | Keeps environment-specific values outside code, requires an explicit Production frontend origin, and keeps secrets outside source control |
| Logging | `ILogger` with compile-time `LoggerMessage` methods | Produces structured fields without unnecessary allocations |
| Error responses | Exception handler plus Problem Details and trace IDs | Gives clients a consistent, debuggable error contract without exposing stack traces |
| CORS | Exact configured frontend origin | Allows the browser client without granting every origin access |
| OpenAPI | `/openapi/v1.json` | Makes the HTTP contract inspectable and testable |
| Health | `/health/live` | Lets Docker, Azure, and reviewers determine whether the process is alive |
| Testing | xUnit architecture and in-memory HTTP integration tests | Verifies dependency direction and the real middleware/controller pipeline |

## Frontend foundation

The React application calls only the local API. Its initial page demonstrates the product's care-relay concept and handles loading, connected, and unavailable API states. Runtime shape validation prevents an unexpected JSON response from being treated as valid merely because TypeScript compiled.

The approved visual tokens and typography are local assets, so the page does not depend on a third-party font request. Keyboard focus, semantic landmarks, an explicit retry action, responsive layouts, and reduced-motion behavior are part of the foundation.

## Delivery foundation

GitHub Actions runs independent backend, frontend, and API-container jobs. Dependencies restore from committed lockfiles, official actions are pinned to commit SHAs, permissions are read-only, and Dependabot monitors NuGet, npm, Actions, and container dependencies.

The API Dockerfile uses separate build and runtime stages. Only published output reaches the final image, and the process runs as the non-root user supplied by the official .NET image.

## Intentionally deferred

Milestone 1 does not add domain entities, an EF Core `DbContext`, migrations, seed records, Auth0, RxNorm, or role screens. Those belong to later milestones and would make the foundation harder to review if introduced together.
