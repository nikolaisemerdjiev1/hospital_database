# Doctor clinical workflow vertical slice

- **Milestone:** 4 - Clinical workflow and medication handoff
- **Branch:** `milestone/04-clinical-workflow`
- **Audience:** A recruiter exploring the public demo and a doctor using fictional clinical records

## Understanding and scope

Milestone 4 follows an assigned visit from the doctor's care queue through consultation documentation, completion, standardized medication lookup, prescription issuance, and pre-dispensing cancellation. It demonstrates coordinated state changes across React, ASP.NET Core, EF Core, PostgreSQL, and the external RxNorm service without claiming to be clinical software.

The workflow remains intentionally narrow. Doctors can act only on appointments assigned to their local clinician profile. Prescription issuance is available only after consultation completion. Pharmacy review and fulfillment transitions belong to the next role-specific slice, and openFDA enrichment remains a later enhancement.

All people and health details are synthetic. The application is an educational portfolio demonstration, not medical guidance or a production healthcare service.

## Architecture

```text
React doctor routes
  -> typed clinical API module
  -> Auth0 access token
  -> ASP.NET Core clinical controllers
  -> concrete Hospital.Core use cases
  -> IApplicationDbContext + IApplicationTransaction
  -> EF Core + Npgsql
  -> PostgreSQL

Medication search use case
  -> resilient RxNorm catalog adapter
  -> bounded memory cache
  -> seeded PostgreSQL fallback records
```

React route pages own screen state and task sequencing. `api/clinical.ts` owns URLs, request bodies, response types, and concurrency tokens. Shared feedback UI maps recoverable API failures into a consistent alert with an optional retry and trace reference. The browser never decides whether a transition is allowed; it presents the server's workflow and sends the version the user observed.

ASP.NET Core controllers remain HTTP adapters. Concrete Core use cases enforce clinician ownership, state transitions, field limits, and optimistic concurrency. EF Core provides the unit of work, while the explicit application transaction boundary keeps consultation completion and prescription operations atomic when they update multiple entities and audit records.

## HTTP contract

All endpoints require the Doctor policy and resolve ownership from the authenticated local clinician profile.

| Method and route | Purpose |
|---|---|
| `GET /api/v1/clinical-worklist` | List assigned visits in a bounded UTC window, optionally filtered by status. |
| `POST /api/v1/appointments/{id}/consultations` | Start the assigned scheduled visit using the observed appointment version. |
| `GET /api/v1/consultations/{id}` | Load the assigned consultation, patient context, and visit prescriptions. |
| `PUT /api/v1/consultations/{id}` | Save a draft using the observed consultation version. |
| `POST /api/v1/consultations/{id}/completion` | Complete the record and appointment atomically. |
| `GET /api/v1/medications` | Search standardized medication concepts and disclose live, cached, or fallback catalog state. |
| `POST /api/v1/consultations/{id}/prescriptions` | Issue a medication order and pending fulfillment atomically. |
| `POST /api/v1/prescriptions/{id}/cancellation` | Cancel an owned, non-dispensed order and fulfillment atomically. |

Malformed input returns `400`, inaccessible resources look missing with `404`, and stale versions or invalid workflow transitions return `409` Problem Details. Expected failures include a trace ID for support without exposing tokens or external identity subjects.

## React interaction architecture

The doctor experience is a task board, not a generic analytics dashboard.

```text
/app
  -> resolve local role
  -> /app/doctor
       -> scan assigned visits
       -> start or continue one consultation
       -> /app/doctor/consultations/:id
            -> document and save draft
            -> deliberately complete the record
            -> search RxNorm concepts
            -> enter dose, directions, and quantity
            -> issue or safely cancel the order
```

The care queue favors recognition over recall: local time, patient name, visit reason, status, and one appropriate action appear in a consistent row. Status filters use native buttons with pressed state, loading and empty states preserve the board structure, and conflicts refresh the current server state.

The consultation keeps the synthetic patient context visible beside the record, gives allergy information stronger contrast, and visually separates care-team notes from patient-facing summary and instructions. An explicit saved/unsaved indicator protects draft work. The page registers dirty state with one application-level navigation guard, which covers every internal route transition, browser history navigation, and full-page exits instead of attaching warnings to individual links. Completion is disabled until all four sections contain content, requires confirmation, and makes the record read-only.

The four-stage visit ribbon connects appointment, consultation, prescription, and pharmacy state. It is the signature element shared with the project's care-relay concept rather than an ornamental dashboard chart.

## Medication-search resilience and safety

The search waits briefly while the doctor types, cancels superseded requests, and ignores stale results. The UI reports whether results are live from RxNorm, recently cached, or served from the local reference fallback. A fallback is disclosed as degraded reference data instead of being presented as a live result.

Selecting a medication concept does not populate the prescribed dose. Catalog strength remains reference text; the doctor must explicitly enter the ordered dose, patient directions, and quantity. This separation prevents a catalog label from silently becoming a medical instruction. Issuance clears the form only after success and adds the returned server record to the visit history.

## Accessibility and human-computer interaction

- Semantic landmarks, headings, forms, labels, fieldsets, lists, status text, and live feedback work without a mouse.
- Keyboard users can move through medication results with arrow keys, select with Enter, and dismiss results with Escape.
- Native modal dialogs contain focus for completion and cancellation; initial focus rests on the safe back action.
- Workflow state is always written as text and never communicated by color alone.
- UTC instants are formatted in the viewer's local timezone and labeled accordingly.
- The layout collapses to one column on smaller screens, while the visit ribbon becomes a compact two-by-two sequence.
- Existing global focus-visible and reduced-motion behavior applies to the doctor routes.
- Unsupported signed roles receive an honest milestone message instead of another role's workspace.

## Test strategy

- Core and PostgreSQL integration tests cover clinician ownership, state rules, transaction rollback, audit writes, and real `xmin` concurrency.
- RxNorm adapter tests cover live lookup, bounded caching, fallback behavior, timeout and malformed-response handling.
- API integration tests cover the full doctor contract, authorization, minimized responses, validation, and `404`/`409` Problem Details.
- Typed React API tests pin query strings, request methods, trimmed bodies, and expected versions.
- React interaction tests cover role routing, worklist actions, required documentation, draft save, deliberate completion, fallback disclosure, blank-dose safety, issuance, cancellation, and confirmation focus.
- Live browser validation checks desktop and narrow layouts, keyboard navigation, Auth0 routing, real PostgreSQL state, and external-catalog status without recording credentials.

## Decision log

1. Extend the existing Harbor Care visual system with a denser clinical handoff board rather than introducing a separate design language.
2. Keep route-level workflow state local and typed HTTP concerns in one API module; do not add a global state library for this bounded slice.
3. Use optimistic concurrency tokens in every state-changing request and refresh on conflicts.
4. Require explicit completion before prescribing and explicit dose entry after catalog selection.
5. Disclose live, cached, and fallback medication sources so degraded behavior stays understandable.
6. Use native modal and form controls wherever possible, then add keyboard behavior only where the medication-result interaction requires it.
7. Defer pharmacist fulfillment screens and openFDA enrichment until their own milestones.
8. Run React Router in Data mode and centralize unsaved-work protection above the route tree so new navigation controls inherit the guard automatically.
