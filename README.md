# Hospital Coordination Platform

[![Continuous integration](https://github.com/nikolaisemerdjiev1/hospital_database/actions/workflows/ci.yml/badge.svg)](https://github.com/nikolaisemerdjiev1/hospital_database/actions/workflows/ci.yml)

This repository is being rebuilt from an early Java console project into a portfolio-grade coordinated-care platform.

The modern application will demonstrate a complete workflow across four synthetic roles:

```text
Patient books an appointment
    -> Doctor completes a consultation
    -> Doctor issues an RxNorm-backed prescription
    -> Pharmacist fulfills the prescription
    -> Patient sees the updated care status
```

## Technology stack

- ASP.NET Core and .NET 10
- React and TypeScript
- PostgreSQL with Entity Framework Core and Npgsql
- Auth0 authentication with signed custom role claims
- RxNorm medication search, with openFDA planned for Phase 2
- Docker-based local development
- GitHub Actions CI/CD
- Azure Static Web Apps and Azure Container Apps
- Neon PostgreSQL

## Current status

Milestone 2 adds the data and identity foundation. The repository now contains:

- A .NET 10 solution with API, Core, and Infrastructure boundaries
- A React 19 and TypeScript application with the initial care-relay experience
- API configuration, structured request logging, Problem Details, health, and OpenAPI endpoints
- A normalized coordinated-care domain model with explicit PostgreSQL constraints and indexes
- EF Core migrations, `xmin` optimistic concurrency, and deterministic synthetic demo data
- Auth0 JWT validation, fail-closed role policies, and active local-profile resolution
- Backend architecture and integration tests plus frontend component and client tests
- PostgreSQL 18.4 for local development through Docker Compose
- A non-root production API container and GitHub Actions continuous integration

The remaining Milestone 2 work is final validation and review of the completed data and identity foundation.

## Run locally

Prerequisites are the .NET 10 SDK, Node.js 24, and Docker Desktop.

Start PostgreSQL:

```shell
docker compose up -d database
docker compose ps
```

Start the API from the repository root:

```shell
dotnet tool restore
dotnet restore Hospital.slnx --locked-mode
dotnet user-secrets set "ConnectionStrings:HospitalDatabase" "Host=127.0.0.1;Port=5432;Database=hospital_coordination;Username=hospital_app;Password=hospital_local_only" --project backend/Hospital.Api
dotnet run --project backend/Hospital.Api -- --initialize-database
dotnet run --project backend/Hospital.Api
```

The explicit initialization command applies pending migrations and creates the repeatable fictional dataset. Normal API startup never changes the schema or seeds data.

In a second terminal, start React:

```shell
cd frontend
npm ci
npm run dev
```

Open `http://localhost:5173`. API liveness is available at `http://localhost:5050/health/live`, database readiness at `http://localhost:5050/health/ready`, and OpenAPI at `http://localhost:5050/openapi/v1.json`.

## Repository structure

```text
frontend/                       React and TypeScript application
backend/Hospital.Api/           HTTP boundary and composition root
backend/Hospital.Core/          Application rules and contracts
backend/Hospital.Infrastructure/ PostgreSQL and external adapters
tests/                          Backend architecture and API integration tests
docs/                           Architecture, development, and project history
```

Core does not reference API or Infrastructure. The API composes the application, while Infrastructure will implement Core-owned database and external-service contracts.

## Documentation

- [Modernization blueprint](docs/architecture/modernization-blueprint.md)
- [Architecture decision log](docs/architecture/decision-log.md)
- [Solution foundation](docs/architecture/solution-foundation.md)
- [Data and identity foundation](docs/architecture/data-identity-foundation.md)
- [Local development guide](docs/development/getting-started.md)
- [Legacy project history](docs/history/legacy-java.md)

## Legacy project

The original freshman-year Java implementation is preserved in the `legacy-java-v1.0` Git tag rather than carried in the modern source tree. The historical document explains what it did, what its limitations were, and how the new architecture addresses them.

## Disclaimer

This is an educational portfolio application containing only fictional, synthetic data. It is not intended for real clinical use and does not claim healthcare-regulatory compliance.
