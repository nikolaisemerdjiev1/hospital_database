# Patient scheduling vertical slice

- **Milestone:** 3 - Patient and scheduling
- **Branch:** `milestone/03-patient-scheduling`
- **Audience:** A recruiter exploring the public demo and a patient using a fictional care portal

## Understanding and scope

Milestone 3 turns the data and identity foundation into the first complete user journey. An authenticated patient can find an active clinician, browse that clinician's future unbooked time, book one slot, review their own appointments, and cancel an upcoming scheduled appointment.

The application remains an educational portfolio demonstration. It uses only synthetic people and health information, expects demo-scale traffic, and makes no healthcare-regulatory or production-service claim. Auth0 owns authentication, the API enforces the Patient policy, and PostgreSQL owns durable scheduling state.

Clinicians and administrators do not create or edit availability in this milestone. Availability is seeded so the slice stays focused on patient interaction, ownership, validation, and concurrency. Consultation and prescription workflows remain later milestones.

## Architecture

```text
React patient workspace
  -> Auth0 access token
  -> ASP.NET Core scheduling controllers
  -> concrete Hospital.Core scheduling use cases
  -> IApplicationDbContext
  -> EF Core + Npgsql
  -> PostgreSQL
```

Controllers translate HTTP inputs and scheduling outcomes into status codes and Problem Details. Concrete Core use cases contain the transaction-script business rules and shape read models with no tracking. Infrastructure continues to supply the existing scoped `ApplicationDbContext`; no repository, mediator, event bus, or new project is introduced.

`TimeProvider` supplies the current instant so past/future boundary behavior is deterministic in automated tests. One `SaveChangesAsync` call makes each booking or cancellation atomic. PostgreSQL's existing filtered unique index on active appointments is the final race-proof double-booking guard, while mapped `xmin` values provide optimistic concurrency versions.

## HTTP contract

All endpoints require the Patient policy.

| Method and route | Purpose |
|---|---|
| `GET /api/v1/clinicians` | List active doctors with ID, display name, and specialty. |
| `GET /api/v1/clinicians/{id}/availability?from=&to=` | List future, unbooked slots in a required UTC window of at most 31 days. |
| `GET /api/v1/appointments?page=&pageSize=&status=` | List only the current patient's appointments with bounded pagination. |
| `POST /api/v1/appointments` | Book a slot using its ID, observed availability version, and a 1-500 character reason. |
| `POST /api/v1/appointments/{id}/transitions` | Cancel an owned, scheduled, not-yet-started appointment using its observed version. |

Successful creation returns `201 Created`; successful cancellation returns `200 OK`. Malformed input or time windows return `400`, missing/unavailable resources return `404`, and stale versions, conflicting bookings, overlaps, or invalid state transitions return `409`. Expected failures use Problem Details with a trace ID. An appointment owned by another patient is indistinguishable from a missing appointment.

## Scheduling rules

Booking requires an active patient and clinician, a future slot, a matching slot version, a nonblank trimmed reason, no active appointment already using the slot, and no overlapping non-cancelled appointment for the patient. Two intervals overlap when the existing start is before the new end and the existing end is after the new start; touching boundaries are allowed.

Cancellation requires ownership, `Scheduled` status, a future start, a matching appointment version, and an optional trimmed cancellation reason no longer than 500 characters. Cancellation writes the status, timestamp, and reason together. Runtime audit-event creation is deferred to the later audit milestone so this slice does not introduce a multi-save transaction abstraction prematurely.

## Interaction design

The patient workspace is a care itinerary, not an analytics dashboard. Its main job is to make the patient's next action obvious.

```text
+------------------------------------------------------+
| Harbor Care                            Profile / Exit |
+------------------------------------------------------+
| Good morning, Avery                                  |
| NEXT VISIT                                           |
| Tue, Sep 08  9:00 AM   Dr. Maya Chen   [Manage]      |
+---------------------------+--------------------------+
| Your appointments         | Book a visit             |
| Upcoming / Past states    | clinician -> time -> why |
+---------------------------+--------------------------+
```

The visual system keeps the existing navy and sea-glass identity, using Manrope for display, Atkinson Hyperlegible for body copy, and IBM Plex Mono for time and status metadata. The signature element is a vertical itinerary line connecting appointment state to the next action. Motion is limited to one booking-panel transition and respects reduced-motion preferences. Keyboard focus, semantic headings, error recovery, loading skeletons, empty states, and mobile layout are required behavior.

The API returns UTC instants. The browser formats them in the viewer's local timezone and labels that fact. No Auth0 subject, medical-record number, birth date, allergy text, access token, or other unnecessary profile data appears in scheduling responses.

## Non-functional assumptions

- Demo scale: tens of concurrent visitors and thousands, not millions, of synthetic records.
- Performance target: normal database-backed API responses should feel immediate on a warm demo instance; cold-start delay is communicated separately.
- Reliability: friendly retries and conflict recovery are sufficient for the public portfolio demo; no production SLA is claimed.
- Security: deny by default, require a validated Patient identity, scope all reads and writes by ownership, and never log tokens or sensitive request bodies.
- Maintenance: one developer owns the modular monolith; complexity must earn its place.

## Test strategy

- Core/use-case coverage for input rules, ownership, filtering, time boundaries, overlap behavior, and transitions.
- PostgreSQL integration coverage for the real filtered unique index and `xmin` concurrency behavior.
- API integration coverage for happy paths, `401`/`403`, cross-patient isolation, `400`/`404`/`409` Problem Details, and response-data minimization.
- React tests for authentication states, loading and empty states, booking, cancellation, conflict recovery, and keyboard-accessible controls.

## Decision log

1. Use a patient-only slice over seeded availability to deliver portfolio value without adding a second role-management surface.
2. Use concrete transaction-script use cases and direct EF Core queries because the workflow is cohesive and uses one database.
3. Derive patient ownership from the resolved Auth0-linked local profile; never accept a patient ID from the client.
4. Combine friendly application prechecks with database constraints and optimistic concurrency because prechecks alone cannot prevent races.
5. Keep API timestamps in UTC and render them in the browser's local timezone.
6. Defer runtime audit writes and slot authoring until their workflows justify additional transaction and overlap infrastructure.
