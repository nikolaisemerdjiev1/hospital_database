# Hospital Coordination Platform

This repository is being rebuilt from an early Java console project into a portfolio-grade coordinated-care platform.

The modern application will demonstrate a complete workflow across four synthetic roles:

```text
Patient books an appointment
    -> Doctor completes a consultation
    -> Doctor issues an RxNorm-backed prescription
    -> Pharmacist fulfills the prescription
    -> Patient sees the updated care status
```

## Planned stack

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

Architecture planning is complete. Application scaffolding begins after the legacy release is archived and the modern repository foundation is established.

- [Modernization blueprint](docs/architecture/modernization-blueprint.md)
- [Architecture decision log](docs/architecture/decision-log.md)
- [Legacy project history](docs/history/legacy-java.md)

## Legacy project

The original freshman-year Java implementation is preserved in the `legacy-java-v1.0` Git tag rather than carried in the modern source tree. The historical document explains what it did, what its limitations were, and how the new architecture addresses them.

## Disclaimer

This is an educational portfolio application containing only fictional, synthetic data. It is not intended for real clinical use and does not claim healthcare-regulatory compliance.
