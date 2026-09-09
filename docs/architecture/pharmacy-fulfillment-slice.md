# Pharmacy fulfillment vertical slice

- **Milestone:** 5 - Pharmacist fulfillment and patient medication status
- **Branch:** `milestone/05-pharmacy-fulfillment`
- **Status:** Approved for implementation
- **Audience:** Recruiters exploring the public demo, pharmacists processing fictional orders, and patients following their synthetic care journey

## Understanding and scope

Milestone 5 completes the coordinated medication handoff. A doctor already issues an RxNorm-backed prescription and an unassigned pending fulfillment in one transaction. This slice gives a pharmacist a focused queue for claiming and advancing that work, then gives the owning patient a read-only view of the latest prescription and pharmacy status.

The project remains a cloud-hosted, production-style simulation. All people, prescriptions, and health details are synthetic. It is not clinical software and does not claim HIPAA compliance or suitability for real pharmacy operations.

The approved workflow is:

```text
Doctor issues prescription
  -> Pending fulfillment enters shared pharmacy queue
  -> Pharmacist starts review and claims it
  -> Assigned pharmacist marks it ready
  -> Assigned pharmacist confirms it dispensed
  -> Owning patient sees the latest read-only status
```

Inventory, refills, substitutions, payments, insurance, clinical verification, messaging, notifications, openFDA enrichment, and real-time infrastructure are explicit non-goals.

## Operating assumptions

- The public portfolio demo serves fewer than 100 simultaneous users and hundreds or thousands of synthetic records.
- Every Phase 1 pharmacist belongs to the same fictional pharmacy.
- A pending fulfillment is unassigned; starting review atomically assigns the authenticated pharmacist.
- Only the assigned pharmacist may move an in-review fulfillment to ready or dispensed.
- Fulfillment movement is forward-only: `Pending -> InReview -> Ready -> Dispensed`.
- Cancelled and dispensed fulfillments are terminal.
- Doctor cancellation continues to cancel a non-dispensed fulfillment atomically.
- Explicit refresh and refresh-after-action are sufficient; SignalR and automatic polling are unnecessary.
- Patient responses expose only the owning patient's medication and patient-safe status information.
- Existing PostgreSQL state constraints and `xmin` concurrency remain the system-of-record protections.

## Architecture

```text
Pharmacist React workspace
  -> typed pharmacy API client
  -> Auth0 access token with pharmacist role
  -> ASP.NET Core fulfillment controllers
  -> concrete Hospital.Core pharmacy use cases
  -> IApplicationDbContext
  -> EF Core + Npgsql
  -> PostgreSQL

Patient React workspace
  -> typed patient prescription API
  -> patient-authorized ASP.NET Core endpoint
  -> patient-owned prescription projection
  -> PostgreSQL prescription + fulfillment state
```

The feature remains inside the existing modular monolith. Controllers are HTTP adapters, Core use cases own visibility and workflow rules, and Infrastructure supplies the configured EF Core context. No generic repository, mediator, new service, queue, or frontend global-state library is introduced.

Pharmacist views use the immutable medication, dose, and direction snapshots stored with the prescription. They do not call RxNorm while fulfilling an already-issued order, so external availability cannot block the pharmacy workflow.

## REST contracts

| Method and route | Purpose |
|---|---|
| `GET /api/v1/fulfillments` | List bounded fulfillment work visible to the current pharmacist, optionally filtered by status. |
| `GET /api/v1/fulfillments/{id}` | Load one permitted fulfillment with the minimum prescription and patient context. |
| `POST /api/v1/fulfillments/{id}/transitions` | Claim or advance a fulfillment using a constrained target state and expected version. |
| `GET /api/v1/prescriptions` | List patient-owned prescriptions with patient-safe fulfillment status. |

Pending records are visible while unassigned. In-review and ready records are visible only to their assigned pharmacist. Dispensed history is visible only to the pharmacist who completed it. Inaccessible records look missing with `404` so assignment and patient information are not disclosed.

Transition requests contain only `targetStatus` and `expectedVersion`. The server derives assignment and timestamps. Malformed input returns `400`, stale or invalid workflow state returns `409`, and expected failures use the existing Problem Details shape with error code and trace ID.

## PostgreSQL transactions and concurrency

The existing `fulfillment` table already contains assignment, status timestamps, state constraints, foreign keys, indexes, and an EF row-version property mapped to PostgreSQL `xmin`. No migration is planned unless implementation evidence identifies a missing database invariant or query index.

Each transition changes one tracked fulfillment and adds one audit event in a single `SaveChangesAsync`. EF Core wraps that save in a transaction, so state and audit succeed or roll back together. An explicit transaction wrapper is reserved for use cases that require multiple saves.

Concurrent claims use optimistic concurrency. Two pharmacists may read the same pending version, but only one update can match the original `xmin`. The loser receives a `DbUpdateConcurrencyException`, which Core maps to a recoverable `409`. The same mechanism prevents a pharmacist transition and doctor cancellation from creating a mixed state. PostgreSQL's default `READ COMMITTED` isolation remains sufficient; serializable transactions would add retry complexity without a demonstrated need.

Queue reads project only required columns, remain bounded, and avoid lazy-loading loops. Existing status/creation-time, assignment, and patient foreign-key indexes support the approved scale.

## React and interaction design

```text
/app/pharmacy
  -> pending shared queue
  -> my in-review work
  -> ready-for-dispensing work
  -> recent completed history

/app/pharmacy/fulfillments/:id
  -> review order and patient safety context
  -> perform the single permitted next action
```

The pharmacist workspace is a task board, not an analytics dashboard. Queue items show patient, medication, dose, quantity, issue time, status, and one clear action. The detail screen emphasizes the allergy summary, medication snapshot, directions, prescriber, assignment, timestamps, and a four-step fulfillment tracker without exposing consultation notes.

Starting review claims work immediately. Marking ready avoids unnecessary modal friction. Confirming dispensing uses an explicit confirmation dialog because it permanently prevents prescription cancellation. The UI waits for the authoritative server response rather than applying optimistic workflow state.

The patient dashboard gains a read-only Medications and pharmacy section. Internal states become plain-language labels: Received by pharmacy, Under pharmacist review, Ready for pickup, Dispensed, or Cancelled. Pharmacist identity and operational metadata are omitted.

Semantic structure, written status, keyboard access, visible focus, confirmation focus management, live feedback, responsive layout, reduced motion, and text alternatives to color are acceptance requirements.

## Failure handling and testing

- Core tests cover pharmacist visibility, assignment, forward-only rules, terminal states, and patient-safe ownership.
- PostgreSQL integration tests prove concurrent-claim behavior, cancellation races, audit atomicity, constraints, and real `xmin` conflicts.
- API tests cover policies, pagination, filtering, minimized DTOs, and `400`/`404`/`409` Problem Details.
- React tests cover role routing, queue filters, detail actions, confirmation focus, conflict refresh, patient labels, and non-actionable states.
- Live validation signs in as the seeded pharmacist, completes one fulfillment, then signs in as its patient to verify the persisted read-only status.

Public demo-state reset is deferred to deployment hardening. Seeded data retains examples in varied states so the workflow remains understandable after individual actions are completed.

## Delivery sequence

1. Pharmacy Core models, access helpers, queue/detail use cases, and dependency registration.
2. Pharmacist-authorized queue and detail REST endpoints with focused tests.
3. Transactional claim, ready, and dispense transitions with concurrency tests.
4. Patient-owned prescription status API.
5. React pharmacist queue and fulfillment detail routes.
6. Patient read-only medication status enhancement.
7. Full validation, live cross-role browser test, review, commit, push, and pull request.

## Decision log

1. Choose the bounded end-to-end vertical slice over an inline-only queue or expanded operations dashboard; it completes the portfolio story without pharmacy-system scope.
2. Reuse the modular monolith and synchronous REST because independent scaling and real-time delivery are not required.
3. Reuse the fulfillment schema and `xmin`; add a migration only when a proven invariant or query plan requires one.
4. Claim a pending fulfillment during the first review transition so assignment and workflow state cannot diverge.
5. Restrict later transitions to the assigned pharmacist and return generic `404` for records assigned elsewhere.
6. Confirm dispensing but not the reversible review and ready steps.
7. Use stored prescription snapshots during fulfillment so RxNorm outages do not block existing orders.
8. Create separate pharmacist and patient DTOs rather than sharing a broad clinical response.
9. Defer openFDA, inventory, notifications, real-time updates, and automatic demo reset.
10. Consider route-based code splitting only after the required slice passes; it is performance polish rather than milestone scope.
