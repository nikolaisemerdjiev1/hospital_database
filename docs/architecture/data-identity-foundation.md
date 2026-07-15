# Data and identity foundation

- **Milestone:** 2 - Data and identity
- **Checkpoint:** PostgreSQL domain model, deterministic demo data, and Auth0-backed local identity resolution
- **Branch:** `milestone/02-data-identity`

## Responsibility boundaries

`Hospital.Core` owns `IApplicationDbContext`, the persistence capability needed by use cases. The contract exposes the approved entity sets and `SaveChangesAsync`. Core references EF Core abstractions so application code can compose LINQ queries, but it does not reference Npgsql or Infrastructure.

`Hospital.Infrastructure` owns the concrete `ApplicationDbContext`, entity configurations, Npgsql configuration, migrations assembly, and database health check. It implements the Core contract and discovers mappings from its own assembly.

`Hospital.Api` remains the composition root. It reads `ConnectionStrings:HospitalDatabase`, fails fast when the setting is absent, and passes the connection string into Infrastructure during dependency-injection registration.

```text
Hospital.Api -- configuration --> Hospital.Infrastructure -- implements --> Hospital.Core
                                          |
                                          `--> EF Core --> Npgsql --> PostgreSQL
```

## DbContext lifetime

`ApplicationDbContext` is registered as scoped, which normally means one context per HTTP request. `IApplicationDbContext` resolves to that same object, so a request has one change tracker and one unit of database work. A context must not be shared across requests or used for parallel operations.

## Health semantics

- `/health/live` proves the API process can answer. It deliberately runs no database checks.
- `/health/ready` proves the API can connect to PostgreSQL through EF Core and Npgsql.

Separating the probes prevents an orchestrator from repeatedly restarting a healthy API process merely because its database is temporarily unavailable.

## Configuration and tooling

The development connection string is stored with .NET user secrets, not in tracked JSON. Production will supply `ConnectionStrings__HospitalDatabase` through protected environment configuration.

The repository pins `dotnet-ef` in `dotnet-tools.json`. Run `dotnet tool restore` after cloning so migration commands do not depend on a developer's globally installed tool version.

## Coordinated-care data model

The first migration creates 11 domain tables:

| Area | Tables | Purpose |
|---|---|---|
| Identity | `user_profile`, `patient_profile`, `clinician_profile`, `pharmacist_profile` | Maps a stable external identity subject to one local care role and its role-specific data. |
| Scheduling | `availability_slot`, `appointment` | Represents a clinician's time and a patient's booking without copying schedule facts into the appointment. |
| Care | `consultation`, `prescription` | Preserves the clinical visit outcome and the medication order that resulted from it. |
| Medication | `medication` | Stores the local RxNorm-oriented reference record used when issuing a prescription. |
| Pharmacy | `fulfillment` | Tracks one pharmacy workflow for one prescription. |
| Operations | `audit_event` | Records synthetic security and care milestones without storing full clinical note bodies. |

Entities are plain Core types. Infrastructure uses one `IEntityTypeConfiguration<T>` per entity to define singular snake-case PostgreSQL names, maximum lengths, enum strings, check constraints, foreign keys, and indexes. Keeping those provider details out of Core makes the model understandable without coupling business code to Npgsql.

All primary keys are PostgreSQL `GENERATED ALWAYS AS IDENTITY` `bigint` values. Auth0 subjects, medical-record numbers, staff identifiers, and RxCUIs have named unique indexes. Historical relationships use restricted deletes; the only `SET NULL` relationship is the optional audit actor, allowing an audit record to remain if a local user profile is retired.

Database checks reject impossible local states such as an end time before a start time, a cancellation before its appointment was created, a completed consultation without its required summaries, and fulfillment timestamps in the wrong order. A clinician/start-time unique index blocks duplicate simultaneous slots, while a partial appointment index permits a cancelled slot to be rebooked but prevents two active appointments from claiming it.

Cross-table ownership rules remain use-case responsibilities. For example, prescription patient and prescriber IDs must be derived from the consultation's appointment rather than accepted from a client. Likewise, identity resolution must require exactly one subtype matching the signed role claim. General schedule-overlap checks beyond identical start times will run in the Milestone 3 scheduling transaction. Database triggers would add complexity without replacing those application and authorization checks.

## Migrations and concurrency

`InitialCreate` is a meaningful migration generated only after the domain mappings were reviewed. EF's model snapshot is version controlled with it, allowing later migrations to describe the difference between the old and new models.

Normal API startup does not call `Migrate` or `EnsureCreated`. Schema changes happen only through the explicit initializer or deployment migration job. This avoids startup races and keeps production changes observable.

Availability slots, appointments, consultations, prescriptions, and fulfillments map a `Version` property to PostgreSQL's system `xmin` column. EF includes the original `xmin` value in updates; if another request already changed that row, zero rows match and EF raises `DbUpdateConcurrencyException` instead of silently overwriting the newer workflow state.

## Deterministic synthetic data

Run the maintenance mode after configuring the connection string:

```shell
dotnet run --project backend/Hospital.Api -- --initialize-database
```

Development configuration supplies a fixed `2026-07-15` anchor date and fake `local-auth|...` subjects. The seed contains only fictional data: 36 patients, 10 clinicians, 4 pharmacists, 60 availability slots, 36 appointments, 14 consultations, 12 fallback medication references, 9 prescriptions, 9 fulfillments, and 46 audit events.

The initializer validates that all four login subjects are present, unique, free of accidental surrounding whitespace, and within the schema limit before migration or data writes. The seed itself runs in a transaction guarded by a PostgreSQL advisory lock, refuses to mix with a partially populated database, and writes a versioned marker. A repeated run performs no inserts and verifies both the complete v1 dataset shape and that each configured subject is active with exactly the subtype required by its local role.

Production will replace the four non-secret fake subjects with the manually created Auth0 user `sub` values through protected deployment configuration. No Auth0 client secret, user password, database password, or access token belongs in tracked settings.

## Authentication and local authorization

Auth0 owns credentials and authentication; PostgreSQL owns application membership and role state. The API validates access-token signature, issuer, exact audience, lifetime, expiration, and RS256 algorithm before ASP.NET Core creates an authenticated principal. Claim mapping is disabled so the stable `sub` and collision-resistant application-role claim retain their original names.

Authorization then performs one no-tracking database projection. It requires exactly one `sub`, exactly one supported lower-case role, an active `UserProfile`, a matching `ProfileType`, and exact subtype composition. Patients have one patient subtype, doctors one clinician subtype, pharmacists one pharmacist subtype, and administrators no clinical subtype. An unknown user, inactive profile, role mismatch, duplicate security claim, or malformed profile composition is denied rather than repaired.

```text
Bearer access token
  -> JWT signature / issuer / audience / lifetime validation
  -> exact sub + signed role claim
  -> active UserProfile lookup
  -> matching local role + subtype
  -> endpoint role or ownership policy
```

Missing or invalid credentials produce `401`; a valid Auth0 identity that fails local or endpoint authorization produces `403`. Both use generic RFC 9457-style Problem Details with a trace ID, while the server logs only controlled failure categories rather than tokens or exception messages. A fallback policy protects new endpoints automatically. System status, health, and OpenAPI are explicitly anonymous.

`GET /api/v1/identity/me` exposes only the local profile ID, display name, and resolved role. It never returns the Auth0 subject, token, password, or unnecessary clinical data.

## Real PostgreSQL validation

CI starts the same pinned PostgreSQL container family used by local development. Integration tests use `HOSPITAL_TEST_CONNECTION_STRING` to create a uniquely named temporary database, apply the migration from empty state, exercise PostgreSQL constraints and optimistic concurrency, and drop the database afterward. This catches provider-specific behavior that an in-memory database cannot reproduce.

Authentication tests use their own temporary PostgreSQL database plus locally signed test JWTs and static OpenID Connect configuration. They never call the live Auth0 tenant. The suite covers invalid signatures and token metadata, duplicate or missing claims, inactive and mismatched profiles, malformed subtype composition, safe `401`/`403` responses, successful identity resolution, and the four-role policy matrix.
