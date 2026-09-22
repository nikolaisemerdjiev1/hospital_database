# Combined application delivery

`release.yml` delivers the React SPA and .NET API as one digest-pinned image. This
implementation is locally tested; a successful hosted release is a separate checkpoint.
It never provisions resources, retrieves database secret values, changes Auth0 or downgrades
the database. After successful rollout and smoke it enables the daily synthetic reset job.
`demo-reset.yml` runs an explicitly approved manual reset using the same deployed image.

## Approval and artifact flow

1. Dispatch **Release combined application** from `main`, supplying its full current
   lowercase SHA and `DEPLOY_SYNTHETIC_DEMO`. This also authorizes enabling recurring
   destructive synthetic resets. The dispatch commit must equal that SHA.
2. `release_gate.py` requires successful **push** runs of CI and CodeQL from this
   repository and exact main SHA. It checks the latest run and its current attempt's
   required jobs; missing, skipped, failed, fork and PR runs do not qualify. It does not
   download another run's artifacts. Weekly/manual scans do not replace main push gates.
3. Approve the `production` environment for image publication. The job builds once without
   layer cache, uses the three public Auth0 build variables and the existing BuildKit demo
   password secret, requires UID 1654, and scans HIGH/CRITICAL vulnerabilities including
   unfixed findings. A finding stops publication. No database credential enters the image.
4. Publish `ghcr.io/nikolaisemerdjiev1/hospital_database:sha-<sha>-<run>-<attempt>` and
   select its `@sha256:...` digest. Deployment and maintenance use that digest, never a
   mutable tag. Anonymous manifest retrieval must succeed before deployment can proceed.
5. Approve the `production` environment for Azure delivery. Only this job receives
   `id-token: write`; it has no package-write permission. Image publication has no Azure
   OIDC permission. Both have only contents/actions read access otherwise. Existing Azure
   trust accepts this repository's exact `environment:production` subject and grants
   Contributor only on `rg-harbor-care-demo`; it does not grant subscription access or
   role-assignment management. GitHub's environment must continue restricting branches to
   `main`, requiring the owner reviewer, and disabling admin bypass. A sole owner leaves
   prevent-self-review off. Two approvals are intentional permission boundaries.
6. `release.py` verifies the subscription, existing app/environment/hostname, HTTPS ingress,
   single-revision mode, secret **names**, manual job settings and absence of a running
   maintenance execution. It pauses the reset schedule, confirms successful provisioning
   in Manual mode, and drains active reset executions before migration. It patches job
   templates and reset trigger configuration. It waits for the maintenance job's
   expected image/SHA, then rechecks current main immediately before starting exactly one
   execution. Main may have advanced while the reset drained or the template provisioned.
   No mutation is auto-retried.
7. After maintenance succeeds, recheck current main and update the existing web app at
   0.25 vCPU / 0.5 GiB, minimum 0 / maximum 1 replica. Startup, liveness and readiness
   probes all use `/health/live`, independent of Neon. The hostname stays unchanged.
8. Passive smoke checks the exact release SHA, database readiness, shell/deep links,
   security/cache behavior and anonymous API denial. It does not sign in or mutate data.
9. Only then update `job-harbor-care-reset` to the successful digest/SHA, verify its command,
   public settings and maintenance secret reference, and enable `0 11 * * *` (11:00 UTC daily).
   Azure adds resource fields such as ephemeral storage; only required CPU/memory are compared.

Gates repeat after approval, before publication, before maintenance and before rollout.
If main advances, dispatch the new checked SHA after inspecting any maintenance already
performed. These checks cannot prevent main advancing immediately after a check;
the shared `hospital-production-maintenance` concurrency group serializes release and manual
reset workflows, not Azure's scheduler or unrelated repository activity. Do not dispatch
multiple releases or perform competing portal changes. GitHub retains at most one pending
run in a concurrency group; pending runs are not a FIFO delivery queue.

## Database command and failure handling

The image's explicit `dotnet Hospital.Api.dll --prepare-release` mode uses only
`ConnectionStrings__HospitalMaintenanceDatabase`. Ordinary HTTP startup does not migrate.
It checks the exact database/maintenance role, ownership and restricted runtime privileges,
validates all seed subjects/current UTC anchor, and holds a session advisory lock across
EF migrations, seed-empty and the embedded `grant-runtime.sql`. It reuses the compiled
migrations and initializer from the same image, rather than producing a second EF bundle.
This keeps migration, seed and reviewed grants tied to the artifact that will serve traffic.
An existing dataset is preserved; this command never calls the resetter.

The operations are **not one global transaction**: EF migration transactions, seed and grants
have their own boundaries. A failed grant can follow a committed migration. Fix forward,
inspect migration history/role metadata privately, and rerun only after the cause is known.
The runtime role cannot read migration history, delete/truncate care data or grant privileges.
New tables must be reviewed and added to the grant allowlist explicitly.

The manual job permits one replica, zero retries, and a 600-second execution timeout;
the application command has an eight-minute cancellation budget. The release waits a bounded
time for execution/revisions. An uncertain start stops without starting a second execution.
Failure before web rollout leaves the existing web revision untouched, but may leave schema
changes. Keep migrations backward compatible with that revision (expand/contract). Destructive
schema changes need a separate reviewed maintenance plan.

On rollout or smoke failure the script copies the previous ready revision's template into
a new `rb-<run>-<attempt>` revision and waits for Azure to mark it ready in Single mode.
This restores the frontend/API and environment references together, **not secret values or
database state**. A failure of recovery is explicitly reported as unconfirmed. Azure-ready
recovery is not authenticated workflow acceptance; verify the restored public endpoint and
its expected prior release manually. On the first release, recovery returns to NGINX.

Runner termination, forced workflow cancellation or the workflow timeout can interrupt this
recovery handler. `cancel-in-progress: false` prevents automatic cancellation by a new
release; it cannot prevent an operator cancellation or runner loss. Inspect the job execution,
latest/ready revision and public endpoint before retrying. Never respond to uncertain delivery
by resetting data, changing credentials or downgrading the database. Retain the previous
revision and image until the hosted checkpoint passes; do not prune them during a release.

## Scheduled and manual synthetic resets

The reset job has one replica/completion, zero retries, a 600-second replica timeout and
0.25 vCPU / 0.5 GiB. Its command is `dotnet Hospital.Api.dll --reset-demo-data`, with
`DemoReset__ExpectedDatabaseName=hospital_coordination` and `DemoSeed__AnchorDate=today`.
It references only `hospital-maintenance-database`. Production requires both the exact
database and `hospital_maintenance` role; runtime/owner logins cannot invoke this entrypoint.
The eight-minute command budget includes the production preflight. EF/resetter raw error
logging is disabled in the template; the command emits a sanitized outcome and nonzero
exit on failure. Do not enable sensitive SQL/provider logging to investigate production.

Reset obtains transaction advisory lock **7239061401**, shared with release's session lock,
before reading migration history. Applied and compiled migration IDs must match exactly,
including refusal of unknown newer migrations. It then acquires the existing seed lock,
truncates only the allowlisted synthetic tables, reseeds/verifies, and commits atomically.
Failure rolls back data and identity changes; migration history and grants are preserved.
An active reset makes release fail its nonblocking lock acquisition; reset waits a bounded
time for a release or another reset. Normal app reads/writes do not take this advisory lock:
reset briefly interrupts demo workflows and discards visitor changes by design.

For an approved manual reset, dispatch **Reset synthetic demo** from `main` with its full
SHA and exact confirmation **RESET_SYNTHETIC_DEMO**, then approve `production`. The existing
OIDC identity and environment protections apply. The runner requires current-main CI and
CodeQL, a stable deployed revision at that SHA, identical app/reset immutable digest,
the reviewed reset settings, and no active maintenance/reset execution. If main has moved
ahead of deployment, **release the checked new main first**; there is no stale-main bypass.
The runner requires an already enabled Schedule job: it refuses an initial placeholder or
a paused job left by a failed release/reset. Inspect and recover those states before a new
authorized release. It pauses/drains, rechecks app/template/main, starts exactly once,
waits for success, runs passive smoke and resumes the schedule. No blind execution retries.

Failures before schedule activation leave a **confirmed** pause in place. An uncertain
pause request is not proof that scheduling stopped. An uncertain activation request may
already have enabled the schedule: inspect configuration and executions before retrying.
Release activation failure leaves the healthy web release in place; it does not roll back
the app or claim the reset is paused. Failed scheduled executions have zero replica retries,
but the next day's schedule remains enabled; inspect repeated failures before allowing more
runs. Pausing prevents future scheduled starts, not executions already queued/running.
The shared database lock also covers a late scheduler start. Unknown/Processing/Degraded
execution states are treated as busy; paginated or malformed execution listings fail closed.
Recovery of a paused schedule uses a separately reviewed successful release, not an automatic
resume after failure. Runner cancellation requires the same private inspection.

Render the initial job locally without Azure authentication or any secret values:

```powershell
python scripts/reset_job.py --environment-id /subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg-fixture/providers/Microsoft.App/managedEnvironments/env-fixture
```

Replace the fake resource ID with the public existing environment ID only when preparing
authorized setup. The JSON is a review definition, not a provisioning script: it names the
maintenance secret reference but intentionally cannot supply/create its value. Stage the
job privately with that secret separately. Its initial trigger is **Manual**, with the
reviewed placeholder `/bin/sh -c 'exit 0'`; even an accidental start cannot reset data.
Only a successful separately authorized release installs the real reset template and schedule.

## Approved Azure setup batch — complete

The user explicitly approved this batch on September 21 local / September 22 UTC, 2026.
Both jobs are now provisioned and verified `Succeeded`, Manual/inert, with zero executions.
The fresh pinned-image scan found zero HIGH/CRITICAL vulnerabilities, including unfixed.
The user confirmed all three private entries saved. Names-only Azure verification confirms
`hospital-runtime-database` on the app and `hospital-maintenance-database` on EACH job.
All three resources are `Succeeded`; app revision is unchanged, both jobs remain Manual/inert
with no executions. No secret value was read or database connectivity tested.
Do not recreate the jobs or repeat the approval request. Target subscription
`2cc76c42-5385-4200-b537-6d0ca3d89532`, resource group `rg-harbor-care-demo`, existing
West US environment `cae-harbor-care-demo`. Public preflight confirmed the expected tenant,
enabled subscription with spending limit On, Consumption-only environment, unchanged
NGINX app/revision/ingress and scale 0–1. Azure returns a null logging destination and no
Log Analytics configuration; this disabled logging state was preserved without changes.

| Resource | Completed change | Private value supplied by |
| --- | --- | --- |
| Existing app `ca-harbor-care-demo` | Add secret `hospital-runtime-database` | User privately enters existing **pooled** `hospital_runtime` Npgsql string |
| New job `job-harbor-care-migrate` | Create Manual/inert container `maintenance`; add secret `hospital-maintenance-database` | User privately enters existing **direct** `hospital_maintenance` Npgsql string |
| New job `job-harbor-care-reset` | Create Manual/inert container `reset`; add secret `hospital-maintenance-database` | User privately enters the same existing maintenance string into this separate resource |

The two credential-free request bodies are prepared locally in
`.artifacts/azure-setup-batch/job-harbor-care-migrate.json` and
`.artifacts/azure-setup-batch/job-harbor-care-reset.json`. They reuse the reset renderer's
reviewed definition with the concrete environment ID, explicit `Consumption` workload
profile and the container names above. Each has Manual trigger, timeout 600 seconds,
zero retries, one replica/completion, 0.25 vCPU / 0.5 GiB and the pinned NGINX
`/bin/sh -c 'exit 0'` placeholder below. No job execution or schedule is included.

Unlike the generic reset render, these initial create bodies have an empty `env` array:
the inert command needs no database connection, and this avoids a dangling secret reference
before the user enters each resource's secret. There are no secret values or secret arrays
in either file. The separately authorized release later installs the actual secret references
and commands. Do not replace these with the generic render during initial creation.

The completed creation preflight verified only public resource metadata: correct subscription,
existing environment/profile, unchanged app and absence of the two proposed job names.
Any future creation requires rechecking the pinned image's current HIGH/CRITICAL scan. Stop on a scan finding,
configuration mismatch, existing job/secret or uncertain provisioning outcome; inspect
nonsecret status and reconcile the remaining scope without blindly overwriting or retrying.
Do not retrieve secret values (`listSecrets`), capture private portal screens, or read
authentication-cache files. Secret verification is names only; the user confirms private entry.

The reviewed job creation requests already executed once per job were
`PUT /subscriptions/2cc76c42-5385-4200-b537-6d0ca3d89532/resourceGroups/rg-harbor-care-demo/providers/Microsoft.App/jobs/{job-name}?api-version=2025-07-01`
against `https://management.azure.com`, with the matching JSON body above. PUT also updates
existing resources, which is why confirmed name absence is required. Await provisioning
success before private secret entry; never start a job to test this batch.

The user performs all credential entry in their own terminal and Azure portal, outside
agent/browser automation. The completed reference procedure below uses the helper or an already saved correct
Npgsql string. Open each resource's **Secrets**, add the exact name above, paste privately
and save. Finish the runtime paste before clearing its clipboard; prepare the maintenance
string only after both jobs exist, then paste into both jobs before pressing Enter in the
helper to clear it. Report only the three resource names and saved/not-saved status in chat.
No password rotation, saved-URL edits, owner credential or GitHub database secret is needed.

Adding the app secret does not create a revision ([Azure secret documentation](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets)).
Leave app environment bindings, revision, ingress and scaling unchanged. Both jobs stay
inert with no secret binding until a separately authorized release. Preserve logging
destination `none`, student spending protection and all existing billing/Auth0 settings.
The approval covers only this table; publication, migrations/seed/grants, deployment,
job starts, schedule activation and Git mutations remain separate decisions.

## Completed private setup reference — do not repeat

Resource creation and all three private secret entries are complete under the user's explicit
approval. The following procedure is retained as a reference, not outstanding work.
Existing Neon role logins, saved private URLs, Auth0 two-client Action and eleven GitHub
variables/demo password have been user-confirmed; do not repeat their setup or rotate passwords.

1. Private runtime and maintenance entry is already confirmed; do not regenerate or overwrite.
   The **user**, never the agent,
   privately generates the two **Npgsql** connection strings using the existing helper:

   ```powershell
   ./scripts/production/Copy-NeonConnection.ps1 -Role hospital_runtime -Format Npgsql -Copy
   ./scripts/production/Copy-NeonConnection.ps1 -Role hospital_maintenance -Format Npgsql -Copy
   ```

   Run these in your own interactive terminal, one command at a time; wait until both
   jobs exist before running the maintenance command. Paste into its authorized destination before clearing the
   clipboard. Runtime uses the pooled hostname; maintenance uses the direct hostname.
   Enter each already-saved role password privately. Do not paste a PostgreSQL URI into
   an Npgsql field. Never share the clipboard, passwords, full URLs or secret screenshots.
   `.env.neon.local` remains the private URL record; no password changed in this stage,
   so no URL update or reinitialization is required.
2. Existing Container App secret `hospital-runtime-database` is saved and name-verified;
   do not repeat or overwrite it. Do not add maintenance/owner credentials. Leave the NGINX revision and
   ingress alone; delivery later sets the runtime environment reference.
3. Both manual Container Apps jobs `job-harbor-care-migrate` and `job-harbor-care-reset`
   now exist in `rg-harbor-care-demo`, existing `cae-harbor-care-demo`, West US, Consumption,
   0.25 vCPU / 0.5 GiB. Preserve replica timeout 600 seconds, retry limit 0, parallelism 1
   and completion count 1. No schedule, ingress,
   extra identity, registry credentials, volumes or log workspace. Use the already reviewed
   placeholder image below with container names `maintenance` and `reset`, command `/bin/sh`, and
   arguments `-c`, `exit 0`. Do not recreate or start either job.

   ```text
   ghcr.io/nginx/nginx-unprivileged@sha256:b8c179cd3c2ae222a873dd59fbae240fadc03836cae5198afc9e9c19919c3880
   ```

   Job secret `hospital-maintenance-database` is saved and name-verified on each
   job. The prepared initial bodies above are creation records; the generic reset render shows the later
   secret reference. No secret value belongs in any JSON artifact.
   Delivery replaces the maintenance container template with
   the scanned hospital image/maintenance command and secret reference before execution.
   The reset placeholder remains inert until rollout and smoke pass, then delivery installs
   the same image/reset command and enables the daily UTC schedule. No owner credential or
   GitHub database secret is needed. Stop on portal/capacity errors;
   do not change plan, region, billing or replica size automatically.
4. Keep the environment's current logging destination `none`, student spending protection,
   no always-on replicas and no continuous readiness monitor. ASP.NET Core and EF categories
   use Warning in the web template; custom route-template request summaries remain Information.
   EF logging is disabled in both maintenance templates
   so provider errors cannot bypass the commands' sanitized outcome messages.
   Built-in revision/job status and platform metrics remain available. There is **no retained
   application log history** with this choice; incident investigation uses explicitly opened
   live logs and status, without capturing credentials or request bodies. Adding paid retained
   logs is a separate decision. Free allowances are shared; zero cost is a target, not a cap.
## One remaining setup and authorization checklist

Azure setup is complete. No additional private credential entry is currently required.

1. Local pre-release review is complete; prepare and separately authorize Git publication/merge. First delivery
   requires the reviewed code on current `main` with all required CI/CodeQL jobs successful.
   Do not bypass the gate to release this dirty milestone branch. Reconfirm environment
   protections and existing OIDC scope before authorizing the initial release.
2. A **new GHCR package defaults to private**. The first approved publication may therefore
   stop at the anonymous-pull check, before Azure delivery. Manually make only this image
   package public under package settings after reviewing its intended public contents.
   Then separately approve a fresh release dispatch (or failed-job retry) for the still-current
   checked main SHA. Do not create a registry PAT or weaken private-source permissions.
3. Explicit first-release authorization must cover image publication, one maintenance
   execution (migrate + seed-empty + grants), update of the existing app, passive smoke and
   application recovery if needed, and **enabling daily destructive synthetic resets at
   11:00 UTC**. A separately approved `RESET_SYNTHETIC_DEMO` dispatch validates the manual
   reset; verify its outcome and a scheduled execution before declaring hosted reset complete.
   Later hosted verification covers HTTPS/HSTS, real OIDC,
   Neon least privilege, Auth0 three-role workflows, cold start and resource usage. Local
   fixtures do not confirm these.

## Local validation

`python -m unittest discover -s scripts -p 'test_*.py'` exercises fake GitHub/Azure clients,
including missing gates, moved main, job failures, stale job templates, uncertain starts/
activation, recovery failures, pause/drain ordering and bounded waiting.
`python scripts/check_release_container.py --image <local-image>`
creates its own labelled PostgreSQL container/network using fake credentials, tests the actual
production maintenance/reset entrypoints, both directions of shared-lock contention,
concurrent resets, lock timeout, migration-history recheck after waiting, rollback and
preserved grants, then removes only those resources. `DemoResetTests` also covers missing
and unknown migration IDs, UTC anchor, canonical data and transactional rollback.
CI runs this check in
addition to the existing combined-container smoke/browser/security gates. It never reads a
local environment file or connects to Neon. Do not run `release.py --execute` or
`demo_reset.py --execute` locally. Local checks cannot verify Azure schedule timing,
OIDC, portal configuration or hosted behavior.

## References

- [Azure job execution and configuration](https://learn.microsoft.com/en-us/azure/container-apps/jobs)
- [Azure job create API](https://learn.microsoft.com/en-us/rest/api/resource-manager/containerapps/jobs/create-or-update?view=rest-resource-manager-containerapps-2025-07-01)
- [Azure job update API](https://learn.microsoft.com/en-us/rest/api/resource-manager/containerapps/jobs/update?view=rest-resource-manager-containerapps-2025-07-01)
- [Azure revision copy](https://learn.microsoft.com/en-us/cli/azure/containerapp/revision?view=azure-cli-latest#az-containerapp-revision-copy)
- [GHCR authentication and visibility](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
