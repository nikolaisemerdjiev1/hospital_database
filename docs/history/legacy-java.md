# Legacy Java Project

## Why this version is preserved

This repository started as a freshman-year Java assignment that simulated a small hospital database. It is preserved because it establishes the starting point for the modernization and makes the developer's growth visible.

The legacy source does not remain in the modern branch. The exact original version is available through the `legacy-java-v1.0` Git tag once that tag is published to the remote repository.

## What the original project demonstrated

The console application included:

- Basic Java object-oriented programming
- A shared `Person` model for patients, doctors, and pharmacists
- Numeric clearance levels for role-dependent behavior
- Randomly assigned synthetic health records
- Prescription and over-the-counter medicine subclasses
- CSV parsing and medication-file updates
- Console registration, login, and role-specific menus

Its main components were:

| Component | Original responsibility |
|---|---|
| `Main` | Startup, console menus, registration, and expired-medication cleanup |
| `Users` | In-memory user lookup, authentication, and role-specific workflows |
| `Person` | Shared patient, doctor, and pharmacist representation |
| `HR` | Synthetic medications, allergies, immunizations, insurance, and age |
| `Medicine` | Shared medicine fields and CSV mutation behavior |
| `Prescription` / `OverTheCounter` | Medicine-type specialization |
| `File` | Custom CSV loading and parsing |

## Why it is being replaced

The original design was appropriate for an introductory programming project, but it is not a foundation for a modern web application:

- Presentation, business rules, authentication, and persistence were mixed together.
- Users existed only in process memory.
- Credentials and role checks used plain strings and numeric values.
- Patient names acted as identifiers.
- Health records were selected randomly instead of linked through durable relationships.
- CSV files acted as the database and were modified directly.
- Absolute machine-specific file paths prevented portable execution.
- There was no web interface, API contract, automated test suite, deployment pipeline, or production-style configuration model.

## Modernization mapping

| Legacy approach | Modern replacement |
|---|---|
| Console menus | Role-adaptive React interface |
| `Main` as coordinator | ASP.NET Core controllers and Core use cases |
| Numeric clearance levels | Auth0-signed role claim plus ASP.NET Core policies |
| Name-based users | Auth0 subject mapped to UUID-based PostgreSQL profiles |
| In-memory user registry | Entity Framework Core and PostgreSQL |
| Random health record | Deterministic relational synthetic data |
| Direct CSV mutation | Transactions, constraints, and EF Core migrations |
| Hard-coded file paths | Environment-based configuration and secret stores |
| Local-only execution | Docker development and Azure deployment |
| Manual verification | Unit, integration, authorization, accessibility, and browser tests |
| No delivery automation | GitHub Actions CI/CD and security checks |
| Hard-coded medicine list | RxNorm integration with caching and fallback data |

## Portfolio narrative

The modernization intentionally keeps the original idea while replacing its implementation. It shows the ability to revisit early work, identify concrete architectural limitations, make proportionate design decisions, and deliver a system that is secure, testable, observable, deployable, and understandable.

The legacy project is evidence of where the learning started. The modern platform is evidence of what was learned afterward.
