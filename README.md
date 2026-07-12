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

Milestone 1 establishes the modern solution foundation. The repository now contains:

- A .NET 10 solution with API, Core, and Infrastructure boundaries
- A React 19 and TypeScript application with the initial care-relay experience
- API configuration, structured request logging, Problem Details, health, and OpenAPI endpoints
- Backend architecture and integration tests plus frontend component and client tests
- PostgreSQL 18.4 for local development through Docker Compose
- A non-root production API container and GitHub Actions continuous integration

Domain data, EF Core migrations, deterministic seed data, and Auth0 arrive in Milestone 2.

## Run locally

Prerequisites are the .NET 10 SDK, Node.js 24, and Docker Desktop.

Start PostgreSQL:

```shell
docker compose up -d database
docker compose ps
```

Start the API from the repository root:

```shell
dotnet restore Hospital.slnx --locked-mode
dotnet run --project backend/Hospital.Api
```

In a second terminal, start React:

```shell
cd frontend
npm ci
npm run dev
```

Open `http://localhost:5173`. The API health check is available at `http://localhost:5050/health/live`, and its OpenAPI document is available at `http://localhost:5050/openapi/v1.json`.

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
- [Local development guide](docs/development/getting-started.md)
- [Legacy project history](docs/history/legacy-java.md)

## Legacy project

The original freshman-year Java implementation is preserved in the `legacy-java-v1.0` Git tag rather than carried in the modern source tree. The historical document explains what it did, what its limitations were, and how the new architecture addresses them.

## Disclaimer

This is an educational portfolio application containing only fictional, synthetic data. It is not intended for real clinical use and does not claim healthcare-regulatory compliance.
