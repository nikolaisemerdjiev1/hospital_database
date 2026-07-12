# Hospital Coordination Platform Modernization Blueprint

- **Status:** Approved for implementation planning
- **Date:** July 11, 2026
- **Scope:** Architecture and delivery plan only; no application implementation is included in this document.

## 1. Executive summary

This repository began as a freshman-year Java console project that simulated a hospital database with CSV files, in-memory users, numeric clearance levels, and console-driven workflows. The modernization will preserve that history while rebuilding the project as a polished, publicly hosted coordinated-care platform.

The new system is a portfolio-grade production-style simulation. It will use synthetic data and professional engineering practices, but it will not claim to be suitable for real clinical use or compliant with healthcare regulations.

The central workflow is:

```text
Patient books appointment
        -> Doctor completes consultation
        -> Doctor issues prescription using RxNorm
        -> Pharmacist fulfills prescription
        -> Patient sees the updated care status
```

The primary portfolio story is not merely a rewrite. It is evidence of growth: an early object-oriented project is being re-evaluated using experience gained in the field, then redesigned with explicit boundaries, secure authentication, relational persistence, automated testing, CI/CD, external API integration, cloud deployment, and human-centered frontend design.

## 2. Goals

- Produce a resume-ready public demo that a reviewer can open without local setup.
- Demonstrate ASP.NET Core, C#, React, TypeScript, PostgreSQL, Auth0, GitHub Actions, Docker, and Azure.
- Build one complete multi-role workflow rather than many disconnected hospital features.
- Use RxNorm as a meaningful external data source in the prescribing workflow.
- Add openFDA as a second-phase enrichment source.
- Make the architecture understandable to a developer learning ASP.NET Core.
- Keep the codebase easy to trace, test, explain in interviews, and modify later.
- Preserve the legacy Java version through Git history and a tagged release so the repository documents professional growth without carrying legacy source in the modern branch.

## 3. Non-goals

- Real patient data or protected health information
- Real clinical decision support
- A claim of HIPAA compliance
- Billing, insurance claims, medical-device behavior, or emergency workflows
- Multi-tenant SaaS capabilities or subscription billing
- Microservices, event sourcing, full CQRS, or distributed messaging
- Enterprise hospital scale or guaranteed availability

The product should be described as a **cloud-hosted, production-style coordinated-care simulation**, not as a real hospital system or a complete SaaS product.

## 4. Requirements and operating assumptions

| Area | Approved assumption |
|---|---|
| Audience | Recruiters, hiring managers, and technical reviewers for full-stack/.NET roles |
| Ownership | Primarily one developer |
| Scale | Hundreds or thousands of synthetic records and fewer than 100 simultaneous users |
| Security | Synthetic data only, least-privilege access, no committed secrets, no compliance claim |
| Reliability | Best-effort public demo with health checks and graceful external-service degradation |
| Performance | Normal database-backed screens should feel immediate; external searches are debounced and cached |
| Maintenance | Clear modules, tests, architecture records, reproducible setup, and conservative dependency choices |
| Learning | Each milestone includes request-flow explanations and interview-ready concepts |

## 5. Architecture decision

Use a **feature-oriented modular monolith with light layering**.

There will be one deployable ASP.NET Core backend, one React frontend, and one PostgreSQL database. The backend is separated into code projects and feature modules, not independently deployed services.

```text
Browser
  |
  +--> Auth0 Universal Login
  |
  +--> React + TypeScript on Azure Static Web Apps
           |
           | Bearer access token
           v
       ASP.NET Core API on Azure Container Apps
           |
           +--> Core use cases and authorization checks
           |
           +--> Entity Framework Core --> Neon PostgreSQL
           |
           +--> RxNorm API --> local cache
           |
           +--> openFDA API in Phase 2
```

This structure provides useful boundaries without requiring microservices, repositories for every entity, MediatR, or handler classes for simple CRUD.

## 6. Repository and project structure

```text
hospital-database/
|-- frontend/                       React + TypeScript application
|-- backend/
|   |-- Hospital.Api/               HTTP, middleware, auth, OpenAPI, DI
|   |-- Hospital.Core/              Features, rules, use cases, contracts
|   `-- Hospital.Infrastructure/    EF Core, PostgreSQL, external clients
|-- tests/                          Backend, integration, frontend, browser
|-- docs/
|   |-- architecture/               Blueprint and decision records
|   `-- history/                    Legacy summary and modernization comparison
`-- docker-compose.yml              Local PostgreSQL and supporting services
```

The deployment remains a single API even though the backend uses multiple code projects.

### Dependency direction

- `Hospital.Api` is the HTTP front door and composition root.
- `Hospital.Core` contains application decisions and does not reference the concrete Infrastructure project or Npgsql provider.
- Core owns a narrow `IApplicationDbContext` contract for EF-backed queries and transactions, plus contracts for genuine external boundaries such as `IMedicationCatalog`.
- `Hospital.Infrastructure` implements those Core-owned contracts with the concrete EF `DbContext`, Npgsql, PostgreSQL, and HTTP clients.
- React communicates only with the API.
- Business logic must not depend on Azure-specific SDKs.

Core may use EF Core abstractions through `IApplicationDbContext`; it does not introduce a generic repository for every table. This keeps LINQ and transaction flow visible while preventing Core from constructing or configuring the concrete database provider.

## 7. Feature modules

| Module | Responsibility |
|---|---|
| Profiles | Links Auth0 subjects to synthetic patient and staff profiles |
| Scheduling | Clinician availability and appointment lifecycle |
| Consultations | Visit notes, patient-facing summaries, and care instructions |
| Prescriptions | Medication selection, dosage instructions, and prescription lifecycle |
| Pharmacy | Fulfillment queue, review, readiness, and dispensing history |
| Medications | RxNorm lookup, normalized results, caching, and openFDA enrichment later |
| Audit | Records important workflow transitions without storing unnecessary sensitive content |

A normal request should remain easy to trace. The arrows below represent calls through Core-owned interfaces; Core never instantiates Infrastructure classes:

```text
React -> API controller -> Core use case -> Core-owned interface
                                             |
                                             v
                                  Infrastructure implementation
                                  (EF Core or external adapter)
```

## 8. Roles and core workflow

### Patient

- View their own profile and care timeline.
- Browse clinician availability.
- Book or cancel their own appointments.
- View patient-facing consultation summaries.
- Track their own prescriptions and fulfillment states.

### Doctor

- View appointments assigned to them.
- Access the relevant synthetic patient context for an assigned appointment.
- Start and complete consultations.
- Search standardized medicines through RxNorm.
- Issue or cancel prescriptions for valid consultations.

### Pharmacist

- View prescriptions eligible for fulfillment.
- Move fulfillment through review, ready, and dispensed states.
- View medication details needed for the mock workflow.

### Administrator

- Manage local operational profiles and application account status, not Auth0 credentials.
- View system-level operational information.
- Avoid unrestricted access to clinical notes by default.
- Use non-destructive capabilities in the public demo.

### State transitions

```text
Appointment:
  Scheduled -> In progress -> Completed
      |
      +-> Cancelled
      +-> No-show

Prescription:
  Issued -> Cancelled

Fulfillment:
  Pending -> In review -> Ready -> Dispensed
      |           |         |
      +-----------+---------+-> Cancelled when its prescription is cancelled
```

An issued prescription and its pending fulfillment are created in one database transaction. A prescription may be cancelled only before dispensing; cancellation also cancels any non-dispensed fulfillment in the same transaction. The patient-facing care relay derives its combined label from both records.

| Transition | Allowed actor | Owning operation |
|---|---|---|
| Scheduled -> In progress | Assigned doctor | Create the appointment's consultation |
| Scheduled -> Cancelled | Owning patient or assigned doctor | Appointment transition request |
| Scheduled -> No-show | Assigned doctor after the scheduled start | Appointment transition request |
| In progress -> Completed | Assigned doctor | Complete the consultation |
| Issued -> Cancelled | Prescribing doctor before dispensing | Prescription cancellation request |
| Pending -> In review | Pharmacist | First review transition also claims the fulfillment |
| In review -> Ready -> Dispensed | Assigned pharmacist | Fulfillment transition request |

The API owns transition rules. React may request a transition but cannot assign arbitrary states.

## 9. Domain and data model

```text
UserProfile
  |-- PatientProfile
  |-- ClinicianProfile
  `-- PharmacistProfile

ClinicianProfile --< AvailabilitySlot
PatientProfile   --< Appointment >-- AvailabilitySlot
Appointment      --0..1 Consultation
Consultation     --< Prescription
Prescription     >-- Medication
Prescription     --0..1 Fulfillment
UserProfile      --< AuditEvent
```

### Entity intent

- **UserProfile:** internal identifier, Auth0 `sub`, display name, exactly one profile type, and account status. No password fields.
- **PatientProfile:** synthetic demographics and optional allergy information.
- **ClinicianProfile:** synthetic staff identifier, specialty, and scheduling data.
- **PharmacistProfile:** synthetic staff and pharmacy data.
- **AvailabilitySlot:** clinician, UTC start/end instants, and concurrency version.
- **Appointment:** patient, availability slot, reason, status, and concurrency version.
- **Consultation:** appointment outcome, clinician notes, patient-facing summary, instructions, Draft/Completed status, and concurrency version.
- **Medication:** RxCUI, standardized display information, and source metadata.
- **Prescription:** one medication order associated with a consultation, prescriber, patient, dose, instructions, quantity, status, and concurrency version.
- **Fulfillment:** nullable assigned pharmacist, status, fulfillment timestamps, and concurrency version. It is created unassigned in Pending state.
- **AuditEvent:** actor, action, affected record, timestamp, trace identifier, and limited JSON metadata.

Core domain fields stay normalized. JSONB is reserved for external response snapshots, integration diagnostics, or limited audit context.

Role-specific profiles use one-to-one composition, not EF inheritance. A Phase 1 user has exactly one `ProfileType`: Patient, Doctor, Pharmacist, or Administrator. Patient, doctor, and pharmacist users must have the matching subtype row; an administrator needs no subtype initially.

Appointments use discrete availability slots. `Appointment.AvailabilitySlotId` is a foreign key, and a partial unique constraint permits at most one active appointment for a slot while still allowing a cancelled future slot to be booked again. All stored instants use UTC. A configured fictional-hospital timezone controls schedule generation and display; the browser presents local labels without changing stored instants.

Database constraints should protect unique Auth0 identities, active-slot booking, one fulfillment per prescription, valid foreign-key relationships, and optimistic concurrency.

## 10. REST API

The React client consumes a versioned REST API under `/api/v1`.

Representative resources:

```text
GET    /api/v1/me
GET    /api/v1/clinicians
GET    /api/v1/clinicians/{id}/availability

GET    /api/v1/appointments
POST   /api/v1/appointments
GET    /api/v1/appointments/{id}
POST   /api/v1/appointments/{id}/transitions

POST   /api/v1/appointments/{id}/consultations
GET    /api/v1/consultations/{id}
PUT    /api/v1/consultations/{id}
POST   /api/v1/consultations/{id}/completion

POST   /api/v1/consultations/{id}/prescriptions
GET    /api/v1/prescriptions
GET    /api/v1/prescriptions/{id}
POST   /api/v1/prescriptions/{id}/cancellation

GET    /api/v1/medications?query=acetaminophen

GET    /api/v1/fulfillments
GET    /api/v1/fulfillments/{id}
POST   /api/v1/fulfillments/{id}/transitions
```

Collection endpoints filter results to the current user's permitted scope. Request and response DTOs remain separate from EF Core entities. Collections support bounded pagination, filtering, and sorting. OpenAPI documents contracts, authorization requirements, and error responses.

Transition requests contain a constrained target state and the caller's expected concurrency version. They are not generic JSON Patch documents. A stale version or invalid transition returns `409 Conflict` with the latest resource version.

Creating a consultation atomically creates a Draft consultation and moves its appointment from Scheduled to In progress. `PUT /consultations/{id}` updates only the assigned doctor's editable draft fields and requires the expected consultation version. Completing it atomically changes the consultation to Completed, changes the appointment to Completed, adds audit events, and makes the patient-facing summary visible. Appointment transition requests cannot directly request In progress or Completed.

Issuing a prescription creates the prescription, an unassigned Pending fulfillment, and audit records in one transaction. The first pharmacist who requests Pending -> In review atomically claims the fulfillment by setting `AssignedPharmacistId`. Only that pharmacist may move it to Ready or Dispensed in Phase 1. Cancellation by the prescribing doctor cancels both a non-dispensed fulfillment and its prescription atomically.

## 11. Authentication and authorization

### Authentication

- React redirects users to Auth0 Universal Login.
- Auth0 handles credentials; the application never stores passwords.
- React obtains an access token for the API audience.
- ASP.NET Core validates signature, issuer, audience, and expiration.
- PostgreSQL maps the token's stable Auth0 `sub` to `UserProfile`.

### Free-plan role design

As of July 2026, Auth0's Free plan does not include built-in Role Management. Paying for Auth0 Essentials is not justified for four fixed synthetic demo identities.

Each Auth0 demo user therefore receives protected `app_metadata` such as:

```json
{ "app_role": "doctor" }
```

An Auth0 Post Login Action copies that value into a collision-resistant namespaced claim in the signed access token. ASP.NET Core maps the claim to authorization policies. The application exposes no way for a user to edit this metadata.

This must be described accurately as **Auth0 authentication with application role authorization**, not paid Auth0 RBAC.

### Demo identity provisioning

1. Create the four Auth0 demo users manually before the production seed is run.
2. Set each user's protected `app_metadata.app_role` value.
3. Record each non-secret Auth0 `sub` in protected deployment configuration used by the seed command.
4. Seed the matching `UserProfile` and subtype rows with those subjects.
5. Use stable fake subjects in local development and CI; live Auth0 is not required for automated tests.

The signed token role establishes broad permission, while the local profile establishes application identity and relationships. The role claim must match `UserProfile.ProfileType`, and the profile must be active. An unknown subject, inactive profile, or mismatch denies access and produces a security log event. The application never silently changes either source.

### Authorization layers

1. Token validation establishes identity.
2. Role policies determine the category of action the caller may perform.
3. Core resource checks determine which specific records the caller may access.

For example, a doctor role may issue prescriptions, but only for a consultation associated with an appointment assigned to that doctor.

### Resource-access matrix

| Resource | Patient | Doctor | Pharmacist | Administrator |
|---|---|---|---|---|
| Profile | Own profile | Minimum assigned-patient context | Own staff profile | Local operational fields only |
| Appointment | Own | Assigned | None | Operational schedule fields |
| Consultation | Patient-facing summary only | Full record when assigned | None | No clinical notes |
| Prescription | Own | Prescriptions they issued for assigned care | Issued prescription details required for fulfillment | Operational status only |
| Fulfillment | Own status | Status for their prescription | Read and transition eligible records | Operational status only |
| Audit event | Own care milestones only | Events for assigned care | Fulfillment events they acted on | Security/operations view without clinical note bodies |

The server shapes DTOs per permitted view. Hiding a button in React is never considered authorization.

## 12. RxNorm and openFDA

React calls the local medications endpoint, never RxNorm directly.

```text
Doctor types a medication
  -> React debounces input
  -> ASP.NET API checks cache
  -> cache miss calls RxNorm
  -> Infrastructure normalizes the result
  -> result is cached and returned
```

`Hospital.Core` defines an `IMedicationCatalog` boundary. `Hospital.Infrastructure` implements it through a typed HTTP client.

The normalized response contains RxCUI, display name, classification when available, strength/dose form when available, and source. A prescription stores the RxCUI plus a display snapshot so later external changes cannot rewrite its history.

Timeouts, limited transient retries, caching, and a small seeded medication fallback keep the public demo usable when RxNorm is unavailable. Logs capture latency, result counts, failures, and cache hits without patient context.

Phase 2 adds openFDA warnings, labels, adverse-reaction information, and recalls behind a separate adapter. The application will clearly state that external data is informational and not validated for clinical use.

## 13. Frontend and human-computer interaction

The frontend is a calm clinical workspace with role-adaptive information density.

### Design tokens

| Token | Value | Purpose |
|---|---|---|
| Deep ink | `#132A33` | Primary text and navigation |
| Clinical teal | `#147D78` | Actions and active states |
| Mist blue | `#DCEEF2` | Contextual surfaces |
| Paper | `#F7FAFA` | Application background |
| Signal amber | `#C47A16` | Pending or attention states |
| Critical red | `#B83A3A` | Errors and destructive actions |

- Manrope for headings and navigation
- Atkinson Hyperlegible for body text and forms
- IBM Plex Mono for medication identifiers and timestamps

All tokens require accessibility testing before implementation is accepted.

### Layout concept

```text
+--------------+-------------------------------------+
| Role-aware   | Page title              User menu   |
| navigation   +-------------------------------------+
|              | Care journey / current status       |
|              +----------------------+--------------+
|              | Primary task area    | Context      |
|              |                      | and history  |
+--------------+----------------------+--------------+
```

The visual signature is a **care relay** showing appointment, consultation, prescription, and pharmacy progress. It communicates real state rather than acting as decoration.

The experience must support keyboard navigation, visible focus, screen readers, reduced motion, responsive layouts, meaningful empty states, and errors that explain recovery. Status never relies on color alone.

## 14. Error handling and resilience

ASP.NET Core uses a global exception pipeline and standardized Problem Details responses.

| Condition | HTTP result |
|---|---|
| Invalid request | 400 with field errors |
| Missing or invalid token | 401 |
| Valid identity without permission | 403 |
| Missing or intentionally concealed record | 404 |
| Invalid state or concurrency conflict | 409 |
| Rate limited | 429 |
| Temporary dependency failure | 503 |

Important behaviors:

- PostgreSQL transaction and constraints allow only one booking for a slot.
- Concurrency versions prevent silent overwrites from stale screens.
- Authenticated users without a local profile receive a guided setup error.
- Safe RxNorm reads may retry transient failures; prescription writes do not retry blindly.
- Health checks report API and database readiness.
- Logs include endpoint, result, duration, stable user identifier, and trace ID.
- Logs exclude tokens, secrets, connection strings, and detailed clinical text.

## 15. Testing strategy

### Backend

- Unit tests for state transitions, validation, and business rules
- API integration tests using the real ASP.NET Core pipeline
- Temporary PostgreSQL containers rather than the EF in-memory provider
- Migration and constraint tests
- Role and record-level authorization tests
- RxNorm adapter tests using controlled HTTP responses

### Frontend

- Component tests for forms, loading, empty, success, and error states
- Accessibility checks for labels, focus, contrast, and keyboard behavior
- Browser tests for the complete coordinated-care workflow

### Authentication in tests

Ordinary CI uses a test authentication handler with predictable claims. CI does not depend on live Auth0. A smaller post-deployment smoke test validates the actual Auth0 configuration.

The main browser scenario is patient booking through final pharmacy fulfillment.

## 16. CI/CD and repository security

### `ci.yml`

Runs for pull requests and pushes to `main`:

- Restore, build, and test .NET
- Apply migrations to a temporary PostgreSQL database
- Install frontend dependencies deterministically
- Lint, type-check, test, and build React
- Build the deployable API container

### `security.yml`

- Static analysis for C# and TypeScript
- Dependency review and vulnerability checks
- Scheduled security checks
- Dependabot configuration and secret-scanning guidance

### `deploy.yml`

- Runs only after successful validation on `main`
- Authenticates to Azure with short-lived OIDC credentials where practical
- Publishes a uniquely tagged image to GitHub Container Registry
- Runs an EF Core migration bundle as a one-off Azure Container Apps Job before directing traffic to the new API revision
- Deploys the API and frontend
- Executes health and smoke checks

If the migration job fails, deployment stops and the previous API revision remains active. Schema changes should be backward-compatible for at least one release because old and new revisions may briefly overlap. Application rollback redeploys the prior image; destructive database downgrades are not automated and require an explicit recovery plan or forward-fix.

### `demo-reset.yml`

- Restores deterministic synthetic data on a schedule and through manual dispatch
- Uses a protected production environment
- Never exposes the database connection string in logs

The `main` branch requires successful checks. GitHub environments separate public variables from secrets and limit deployment credentials to deployment jobs.

## 17. Configuration and secrets

### Public configuration

Values such as the Auth0 domain, SPA client ID, API audience, API base URL, and deployment region are configuration, not confidential secrets. Frontend build variables are assumed to be visible to users.

### Secrets

- Production PostgreSQL connection string
- Azure Static Web Apps deployment token if required by the chosen workflow
- Any future openFDA API key
- Any provider credential that cannot use OIDC

Local development uses `.env.example` plus .NET user-secrets or ignored environment files. Production runtime secrets live in Azure Container Apps. GitHub environment secrets contain only values required by CI/CD.

Actual secret values must never be pasted into documentation, chat, source control, screenshots, or test fixtures.

## 18. Deployment

| Component | Service |
|---|---|
| React frontend | Azure Static Web Apps Free |
| ASP.NET Core API | Azure Container Apps Consumption |
| Container image | GitHub Container Registry |
| PostgreSQL | Neon Free |
| Authentication | Auth0 Free |
| External medication data | NLM RxNorm |

The API container may scale to zero. The static landing page loads immediately and presents an intentional startup state while the API wakes. Azure budget alerts and conservative scaling limits protect against unexpected charges.

The architecture stays portable by using standard containers, PostgreSQL, Auth0, and environment configuration. Azure SDK calls do not enter business logic.

Cloud pricing, quotas, runtime compatibility, and provider limits must be rechecked immediately before infrastructure is created.

## 19. Seeded public demo

The fictional hospital contains repeatable synthetic data generated from a controlled anchor date:

- 30-50 patients
- 8-12 doctors
- 3-5 pharmacists
- Past and upcoming appointments
- Consultations, prescriptions, fulfillments, and audit events in varied states

The public landing page provides clearly labeled patient, doctor, pharmacist, and limited administrator demo credentials. The application exposes no signup, password-management, role-management, or account-deletion screens. The Auth0 connection disables public signup; only the repository owner controls the demo users and their protected role metadata.

Stable identifiers and a fixed pseudorandom seed make each reset repeatable. CI and local tests use a fixed date anchor. The public reset job explicitly supplies the current reset date as its anchor so upcoming appointments stay current while all other generated values remain reproducible. The reset preserves the configured Auth0 subject mapping, recreates domain data transactionally, and records the reset version.

A persistent disclaimer states that all people and health information are fictional. Rate limits, validation, output encoding, constrained free text, scheduled resets, and audit records reduce public-demo abuse.

The README will provide the live URL, demo accounts, screenshots, a one-minute guided workflow, architecture overview, security explanation, and synthetic-data disclaimer.

## 20. Delivery roadmap

### Milestone 0 - Preserve history and decisions

- Tag the current Java project as `legacy-java-v1.0` before modernization changes replace it.
- Optionally publish that tag as a GitHub release or retain a legacy branch.
- Remove Java source files, legacy CSV data, and compiled `.class` artifacts from the modern branch.
- Add `docs/history/legacy-java.md` with a concise architecture summary, limitations, lessons learned, and a link to the preserved tag.
- Add the approved blueprint and decision records.

### Milestone 1 - Foundation

- Scaffold ASP.NET Core and React.
- Add local PostgreSQL through Docker Compose.
- Establish configuration, logging, errors, OpenAPI, and CI.

### Milestone 2 - Data and identity

- Implement entities, migrations, constraints, and deterministic seed data.
- Configure Auth0, custom role claims, JWT validation, and local profiles.
- Validate role and resource authorization.

### Milestone 3 - Patient and scheduling vertical slice

- Build clinician availability, booking, cancellation, and patient dashboard.
- Complete the first React-to-PostgreSQL workflow.

### Milestone 4 - Clinical workflow

- Build doctor dashboard, consultation workspace, RxNorm integration, and prescriptions.

### Milestone 5 - Pharmacy and coordination

- Build fulfillment queue, state transitions, care-relay timeline, and audit history.

### Milestone 6 - Portfolio release

- Complete responsive and accessibility passes.
- Finish integration, browser, and security tests.
- Deploy Azure, Neon, and Auth0 resources.
- Add demo resets, screenshots, diagrams, README, and resume bullets.

### Phase 2

- Add openFDA enrichment.
- Consider notifications and further UX refinements only after the portfolio release is stable.

## 21. Resume-ready completion criteria

The first release is complete when:

- A reviewer can open a public URL and understand the project quickly.
- All four roles can be demonstrated with synthetic identities.
- The appointment-to-fulfillment journey works end to end.
- Auth0 authenticates users and signed custom role claims drive API policies.
- Record-level authorization prevents cross-user access.
- RxNorm visibly powers medication selection and has a tested fallback.
- PostgreSQL migrations and deterministic seeds work from an empty database.
- CI, security checks, and deployment workflows pass.
- The application is responsive, keyboard accessible, and usable under loading/error conditions.
- No secrets or real personal information exist in the repository.
- Architecture, security decisions, setup, demo steps, and trade-offs are documented.

## 22. Implementation teaching agreement

Implementation will proceed one milestone at a time. For each milestone, the developer should be able to explain:

- What was added and why
- How an HTTP request travels through ASP.NET Core
- Where dependency injection participates
- Which code owns validation, authorization, business rules, and persistence
- How EF Core translates the operation into PostgreSQL work
- What tests prove the behavior
- What trade-off was accepted and when the design should be revisited

The project is not successful merely because generated code runs. It is successful when its owner can confidently maintain it and explain its architecture in an interview.

## 23. Time-sensitive references

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [ASP.NET Core Web API documentation](https://learn.microsoft.com/en-us/aspnet/core/web-api/)
- [EF Core database providers](https://learn.microsoft.com/en-us/ef/core/providers/)
- [Npgsql EF Core provider](https://www.npgsql.org/efcore/)
- [Auth0 React quickstart](https://auth0.com/docs/quickstart/spa/react)
- [Auth0 ASP.NET Core API quickstart](https://auth0.com/docs/quickstart/backend/aspnet-core-webapi)
- [Auth0 custom claims](https://dev.auth0.com/docs/secure/tokens/json-web-tokens/create-custom-claims)
- [Auth0 pricing](https://auth0.com/pricing)
- [RxNorm APIs](https://lhncbc.nlm.nih.gov/RxNav/APIs/index.html)
- [openFDA drug APIs](https://open.fda.gov/apis/drug/)
- [Azure Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/)
- [Azure Static Web Apps pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/static/)
- [Neon pricing](https://neon.com/pricing)
