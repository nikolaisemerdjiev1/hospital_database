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

## Configuration and secrets

Copy `.env.example` to `.env` only when you want to override the local PostgreSQL defaults. The committed values are intentionally local-only and the database port binds only to `127.0.0.1`.

The browser can read every `VITE_*` value bundled into the frontend. Treat the API URL, Auth0 domain, client ID, and audience as public configuration; never put a database password, Auth0 secret, Azure credential, or API key in a `VITE_*` variable.

The API project has a .NET user-secrets ID. When Milestone 2 introduces the connection string and Auth0 configuration, local secrets will be set with `dotnet user-secrets` and production secrets will come from Azure Container Apps. GitHub Actions will receive only deployment values that cannot use OIDC.

Development supplies `Frontend:Origin` as `http://localhost:5173`. Production intentionally has no fallback: deployment must set `Frontend__Origin` to the exact public React origin or the API refuses to start. This fail-fast behavior prevents a healthy-looking deployment with unusable browser CORS.

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
| OpenAPI | `http://localhost:5050/openapi/v1.json` |
| PostgreSQL | `localhost:5432` |

## Validate before committing

Backend:

```shell
dotnet restore Hospital.slnx --locked-mode
dotnet format Hospital.slnx --verify-no-changes --no-restore
dotnet build Hospital.slnx --configuration Release --no-restore
dotnet test Hospital.slnx --configuration Release --no-build --no-restore
```

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
