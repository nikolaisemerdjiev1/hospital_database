# Local development

## Prerequisites

- .NET SDK `10.0.301` or a compatible latest patch in that feature band
- Node.js `24.18.0` through `fnm`, `nvm`, or another version manager
- Docker Desktop with Docker Compose
- Git

Verify the toolchain:

```shell
dotnet --version
node --version
npm --version
docker --version
docker compose version
```

Restore the repository-pinned EF Core command-line tool:

```shell
dotnet tool restore
```

## Configuration and secrets

Copy `.env.example` to `.env` only when you want to override the local PostgreSQL defaults. The committed values are intentionally local-only and the database port binds only to `127.0.0.1`.

The browser can read every `VITE_*` value bundled into the frontend. Treat the API URL, Auth0 domain, client ID, and audience as public configuration; never put a database password, Auth0 secret, Azure credential, or API key in a `VITE_*` variable.

`frontend/.env.example` contains the public SPA client ID, Auth0 domain, and API audience. The API uses the same domain and audience plus the namespaced role-claim URI from `appsettings.Development.json`. Production intentionally supplies the equivalent `Authentication__Auth0__Domain`, `Authentication__Auth0__Audience`, and `Authentication__Auth0__RoleClaim` settings at deployment time. The API never needs the SPA's client secret, and no Auth0 client secret is created for this browser flow.

The API project has a .NET user-secrets ID. Local secrets are stored with `dotnet user-secrets`, while production secrets will come from Azure Container Apps. GitHub Actions will receive only deployment values that cannot use OIDC.

Store the local PostgreSQL connection string outside the repository:

```shell
dotnet user-secrets set "ConnectionStrings:HospitalDatabase" "Host=127.0.0.1;Port=5432;Database=hospital_coordination;Username=hospital_app;Password=hospital_local_only" --project backend/Hospital.Api
```

The password above protects only the loopback-bound local demo database. Never reuse it for a hosted database.

The committed development seed uses fake `local-auth|...` subjects so a cloned repository and CI do not depend on the repository owner's Auth0 tenant. To exercise the API with the four live Auth0 demo users, override the four non-secret subject bindings locally:

```shell
dotnet user-secrets set "DemoSeed:Subjects:Patient" "<patient-auth0-sub>" --project backend/Hospital.Api
dotnet user-secrets set "DemoSeed:Subjects:Doctor" "<doctor-auth0-sub>" --project backend/Hospital.Api
dotnet user-secrets set "DemoSeed:Subjects:Pharmacist" "<pharmacist-auth0-sub>" --project backend/Hospital.Api
dotnet user-secrets set "DemoSeed:Subjects:Administrator" "<administrator-auth0-sub>" --project backend/Hospital.Api
```

Identity bindings are immutable seed inputs. Apply those overrides only before initializing an empty local database; the initializer deliberately refuses to silently reassign an existing profile to a different external identity.

## Initialize the database

Apply pending EF Core migrations and create the deterministic fictional dataset:

```shell
dotnet run --project backend/Hospital.Api -- --initialize-database
```

Initialization is an explicit maintenance mode. It validates the configured demo identity subjects, migrates the database, seeds an empty database in one advisory-locked transaction, and then exits. Running it again is safe: the versioned marker, complete dataset shape, and four identity/profile mappings are verified without duplicating records.

The initializer refuses to add demo records to a partially populated database. Normal `dotnet run --project backend/Hospital.Api` startup never migrates or seeds, which prevents an application replica from racing another replica or unexpectedly changing a production schema.

Development supplies `Frontend:Origin` as `http://localhost:5173`. Production intentionally has no fallback: deployment must set `Frontend__Origin` to the exact public React origin or the API refuses to start. This fail-fast behavior prevents a healthy-looking deployment with unusable browser CORS.

## Authentication behavior

Auth0 signs access tokens and publishes the verification keys used by the API. ASP.NET Core validates the signature, issuer, exact API audience, expiration, and RS256 algorithm. It then requires exactly one `sub` claim and exactly one namespaced application-role claim.

A valid token does not grant access by itself. The `sub` must map to one active local `UserProfile`, the signed role must match the database profile type, and the expected patient, clinician, or pharmacist subtype must exist. Invalid or missing tokens return a generic `401` Problem Details response; authenticated identities that fail local resolution or role policy return a generic `403`. Both responses include a trace ID without exposing tokens or validation details.

`GET /api/v1/identity/me` is the first protected endpoint. It returns only the resolved local profile ID, display name, and role. System status, health probes, and OpenAPI remain intentionally public; all future endpoints are protected by a fail-closed fallback policy unless explicitly marked anonymous.

## Start the application

From the repository root:

```shell
docker compose up -d database
docker compose ps
dotnet run --project backend/Hospital.Api
```

In another terminal:

```shell
cd frontend
npm ci
npm run dev
```

Local addresses:

| Service | Address |
|---|---|
| React | `http://localhost:5173` |
| API status | `http://localhost:5050/api/v1/system/status` |
| API health | `http://localhost:5050/health/live` |
| API readiness | `http://localhost:5050/health/ready` |
| OpenAPI | `http://localhost:5050/openapi/v1.json` |
| PostgreSQL | `localhost:5432` |

## Validate before committing

Backend:

```powershell
$env:HOSPITAL_TEST_CONNECTION_STRING = "Host=127.0.0.1;Port=5432;Database=postgres;Username=hospital_app;Password=hospital_local_only"
dotnet restore Hospital.slnx --locked-mode
dotnet format Hospital.slnx --verify-no-changes --no-restore
dotnet build Hospital.slnx --configuration Release --no-restore
dotnet test Hospital.slnx --configuration Release --no-build --no-restore
```

Database integration tests use that server-level connection only to lease uniquely named temporary databases. Each database is migrated from empty state and dropped after its test collection, so tests never alter `hospital_coordination`.

Authentication integration tests generate short-lived JWTs with a test-only RSA key and inject static OpenID Connect metadata into the test host. They validate the same JWT bearer and authorization pipeline without sending credentials to Auth0 or depending on internet access.

Frontend:

```shell
cd frontend
npm ci
npm run lint
npm run typecheck
npm test
npm audit --audit-level=high
npm run build
```

Containers:

```shell
docker compose config --quiet
docker build --file backend/Hospital.Api/Dockerfile --tag hospital-api:local .
```
