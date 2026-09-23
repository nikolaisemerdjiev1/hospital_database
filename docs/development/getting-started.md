# Local development

> The hosted demo is already available at https://ca-harbor-care-demo.salmonsea-5286e167.westus.azurecontainerapps.io. This guide remains for safe local development only; local initialization/reset commands must never target the hosted database.

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

Create the ignored local browser configuration and verify these Auth0 Single Page Application settings:

```powershell
Set-Location frontend
Copy-Item .env.example .env.local
Set-Location ..
```

- Allowed Callback URL: `http://localhost:5173/auth/callback`
- Allowed Logout URL: `http://localhost:5173`
- Allowed Web Origin: `http://localhost:5173`

The frontend requests an access token for `https://hospital-coordination-api` and keeps it in memory. Auth0 handles credentials; React never receives or stores the user's password.

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

Development uses `DemoSeed:AnchorDate="today"`, so a newly initialized database includes
bookable clinician availability in the upcoming 31-day window. Integration tests and other
repeatable environments may supply an explicit `yyyy-MM-dd` anchor instead.

Initialization is an explicit maintenance mode. It validates the configured demo identity subjects, migrates the database, seeds an empty database in one advisory-locked transaction, and then exits. Running it again is safe: the versioned marker, complete dataset shape, and four identity/profile mappings are verified without duplicating records.

The initializer refuses to add demo records to a partially populated database. Normal `dotnet run --project backend/Hospital.Api` startup never migrates or seeds, which prevents an application replica from racing another replica or unexpectedly changing a production schema.

## Reset the synthetic demo

The initializer remains non-destructive and will not refresh a database that visitors have changed. To deliberately restore the known synthetic dataset, run the separate maintenance command:

```shell
dotnet run --project backend/Hospital.Api -- --reset-demo-data
```

Development may reuse `ConnectionStrings:HospitalDatabase` for this command because the Docker database is local and direct. Production requires a separate `ConnectionStrings:HospitalMaintenanceDatabase` setting; the normal web process continues to use the pooled runtime connection.

Before deleting any synthetic records, reset verifies `DemoReset:ExpectedDatabaseName`, the four configured identity bindings and the UTC seed anchor. Within one transaction it acquires the advisory lock shared with release maintenance **before** requiring exact equality of applied and image migration IDs (pending or unknown newer migrations both refuse reset). It then takes the seed lock, truncates only the explicit application-table allowlist, reseeds and verifies the canonical workflow, and commits once. `__EFMigrationsHistory` is never truncated. A failure rolls back the complete operation. See [delivery](delivery.md#scheduled-and-manual-synthetic-resets) for protected production reset automation.

Do not point this command at a database containing real or independently owned records. It exists only for this project's synthetic shared demonstration and is not exposed through HTTP.

Development supplies `Frontend:Origin` as `http://localhost:5173`. Production intentionally has no fallback: deployment must set `Frontend__Origin` to the exact public React origin or the API refuses to start. This fail-fast behavior prevents a healthy-looking deployment with unusable browser CORS.

## Recruiter landing page and readiness

The public landing page explains the shared synthetic care journey and offers patient, doctor, and pharmacist account cards. Each card sends an email hint and requests a fresh Auth0 login. The signed-in account and API policies determine access; choosing a card never assigns a role. The existing workspace remains available to an already signed-in user.

To publish the intentionally public, project-only demo password locally, edit the existing ignored `frontend/.env.local` file in your editor and add `VITE_DEMO_PASSWORD` with the value supplied by the demo owner. Preserve the other settings. Do not paste the value into chat, terminal commands, source files, or screenshots. Restart Vite after editing. The tracked `.env.example` deliberately leaves this value blank; without it, the landing page provides guidance for users who already know the password. Never use a mailbox password or any private credential here: Vite embeds `VITE_*` values in public browser assets.

The landing page checks the API's anonymous `/health/ready` endpoint, which includes PostgreSQL readiness. It stays readable while checking, and enables the account sign-in buttons once ready. Checks are serial, with four attempts, an eight-second timeout per request, and two-, four-, and eight-second delays between attempts: a nominal maximum of 46 seconds, subject to browser timer scheduling. A `Retry-After` response stops automatic checking and disables explicit retry until the requested time; HTTP 429 without a usable header defaults to a 60-second wait. The API exposes this header only to the configured frontend origin through CORS. Leaving the page cancels its pending check and retry timer.

Readiness is a point-in-time availability check, not a guarantee that a later operation will succeed. Existing authenticated workflows retain their error handling. No mutation is replayed to wake the API, and readiness performs no migration, seeding, or reset.

Focused browser checks use a separate Vite server on port 4174, a fake Auth0 domain, a fake public password, and intercepted health responses. They do not require Docker, real Auth0 credentials, or a populated database:

```shell
cd frontend
npx playwright install chromium
npm run test:browser
```

Use the repository's Node 24 and npm 11.16 toolchain. These checks cover 320/768/1440-pixel layouts, axe WCAG A/AA checks, keyboard focus and section navigation, reduced motion, all three SDK authorization redirects, cold-start failure/retry, and cross-origin cooldown behavior. Generated screenshots and failure artifacts are ignored under `frontend/test-results/`. Browser automation verifies the login request boundary; actual Auth0 sign-in, role separation, and end-to-end care workflows still require manual acceptance.

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
npm audit --audit-level=moderate
npm run build
```

Containers:

```shell
docker compose config --quiet
docker build --file backend/Hospital.Api/Dockerfile --tag hospital-api:local .
```

See [Quality and security gates](quality-gates.md) for NuGet audits, CodeQL, dependency review,
workflow lint, final-image scanning, and passive production smoke commands. Automated CI
uses fake Auth0/password configuration and disposable PostgreSQL services.
