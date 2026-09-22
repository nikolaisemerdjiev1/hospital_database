# Production configuration checkpoint

Updated September 21, 2026. **The reviewed NGINX placeholder and its HTTPS verification
are complete.** The user has since confirmed the Neon database/role logins, production
Auth0 SPA and two-client Action, and GitHub production protections/eleven variables/demo
secret. These confirmations were not independently queried during local pre-release review.
Do not recreate roles/clients, rotate passwords or repeat accepted local browser workflows.
The hospital application is not deployed; Azure database secrets and both maintenance jobs
are not confirmed created. Use the single remaining [delivery setup checklist](delivery.md#one-remaining-setup-and-authorization-checklist).

Verified production origin (no trailing slash):

```text
https://ca-harbor-care-demo.salmonsea-5286e167.westus.azurecontainerapps.io
```

This address currently shows the NGINX welcome page. It is not the hospital demo yet.
Keep the same Container App resource when a later authorized deployment replaces the image.
The earlier setup procedure below is retained as a reference, not a request to repeat it.

The earlier Microsoft sample was rejected before execution: three HIGH package findings
across `CVE-2026-63076` (OpenSSL) and `CVE-2026-84445` (gRPC). That scan identified known
vulnerable dependencies, not a demonstrated exploit or a problem with the hospital image.
Its ignored report is `.artifacts/trivy-placeholder.json`. Do not reuse that sample.

## Verified placeholder prerequisites (September 14 historical snapshot)

- Azure for Students subscription is Enabled with spending limit On.
- Before creation, `rg-harbor-care-demo` contained only `cae-harbor-care-demo` and the
  deployment identity. After creation, only `ca-harbor-care-demo` was added to that inventory.
  The West US environment is Succeeded with only the Consumption profile and no configured
  Log Analytics destination.
- West US managed-environment quota: 1 used / 1 allowed. Reuse the existing environment.
- Pre-deployment environment Consumption core quota: 0 used / 100 allowed. This is quota headroom,
  not a free allowance, reservation, guaranteed capacity, or proof of a successful deployment.
- GitHub OIDC subject is exactly
  `repo:nikolaisemerdjiev1/hospital_database:environment:production`; the identity has
  Contributor only at this project's resource-group scope.
- Repository is public, default branch `main`, current GitHub user has admin access.
  GitHub reported zero environments at this snapshot. The production environment and
  protections were subsequently user-confirmed; the current status above supersedes this.

The pinned image's HIGH/CRITICAL scan passed again immediately before creation; ignored
report `.artifacts/trivy-placeholder-nginx-predeploy.json`. Azure returned Succeeded/Running
and ready revision `ca-harbor-care-demo--jcs4zer`. The configuration matches Consumption,
0.25 CPU, 0.5 GiB, external ingress target8080, `allowInsecure=false`, Single revision mode,
100% traffic to latest, min0/max1, cooldown300s and polling30s. Identity type is None;
there are zero secrets, no container environment settings and no registry credentials.
No custom probes were specified. Local Docker hardening options are not claimed in Azure.

Passive hosted checks passed: HTTPS GET200 with the expected welcome page, HEAD200,
HTTP301 to the exact HTTPS origin, and missing-path404. TLS used standard certificate and
hostname validation without a bypass. An initial test converted headers to a case-sensitive
dictionary; its redirect assertion was corrected to case-insensitive lookup, then the
complete check passed. The service required no change. Ignored evidence:
`.artifacts/placeholder-nginx-hosted-http.json`.
Successful image pull/start and HTTPS are verified for the placeholder. Scale-to-zero is
configured; an actual idle-to-zero/wake cycle and its timing have not been observed.
Hospital image startup, Auth0, database readiness and clinical acceptance remain later
hosted checks. No recurring requests or keep-alive monitor were installed.

## Verified replacement image

Use the upstream NGINX unprivileged image from its documented public GitHub Container
Registry. It serves the stock welcome page; no repository build, push, credential, Auth0
client, database connection or application identity is needed. This is only a temporary
hostname placeholder, not the recruiter-facing hospital application.

| Property | Verified value |
| --- | --- |
| Image | `ghcr.io/nginx/nginx-unprivileged` |
| Discovery tag | `stable-alpine`, reporting NGINX 1.30.4 / Alpine 3.24.1 |
| Pinned Linux/AMD64 manifest | `sha256:b8c179cd3c2ae222a873dd59fbae240fadc03836cae5198afc9e9c19919c3880` |
| Parent multi-platform index | `sha256:442753882674b49ae2c1de83ed67896131c0777f56df5005e356e62bc3f7e7ce` |
| Local image ID | `sha256:8b5953dae38d27a76bca22373bb920fd6ce8d9d7da21578d2926e678002de8a0` |
| Image size | 60,077,538 bytes, approximately 57.3 MiB uncompressed |
| Runtime | Linux/AMD64, UID/GID 101, HTTP port 8080 |

Trivy 0.74.0 scanned that exact manifest with the vulnerability database updated at
2026-09-14 01:15 UTC: **zero HIGH/CRITICAL findings**, exit 0, no suppressed findings or
`--ignore-unfixed` option. It detected 70 OS packages and no language-package files.
This result is limited to the scanner's package/advisory coverage and selected severities.
Trivy warned that Alpine 3.24 was missing from its lifecycle list; the
[official release table](https://alpinelinux.org/releases/) confirms current support,
with branch end of support June 1, 2028. The warning did not prevent package scanning.

Local verification used a loopback-only port, 0.25 CPU, 512 MiB, no additional swap,
non-root user, read-only filesystem, a 16 MiB temporary `/tmp`, all capabilities dropped,
no-new-privileges and a 128-PID limit. No host files or secrets were mounted.
The default entrypoint and configuration passed `nginx -t`. GET/HEAD returned 200;
the welcome-page content and HTML type matched; a missing route returned 404; POST
returned 405; ETag revalidation returned 304. All 100 GET requests at concurrency four
passed. Cgroup memory peak was 25,788,416 bytes (24.6 MiB), with zero OOM events/restarts.
These are placeholder-only observations, not hospital workflow capacity or Azure timing.
The temporary test container was ownership-checked and removed; the image and ignored
reports `.artifacts/trivy-placeholder-nginx.json` and
`.artifacts/placeholder-nginx-http.json` remain available locally.

## Resolve the hostname first

The environment default domain is a suffix, not an application URL. Do not configure
Auth0 against a guessed hostname, wildcard, revision URL, or localhost in production.

Checkpoint sequence:

1. **Complete:** separately authorized creation of the temporary `ca-harbor-care-demo`
   placeholder inside the existing environment, using only the verified NGINX image.
2. **Complete:** read `properties.configuration.ingress.fqdn` after successful creation and
   verify HTTPS. Use the actual origin above as `APP_ORIGIN`; do not substitute a revision URL.
3. **User-confirmed complete:** separate production Auth0 SPA using the actual origin,
   two-client Post Login Action, Neon role logins and GitHub production configuration.
   Keep the local client and users untouched; do not rerun the historical procedure below.
4. Delivery/reset automation is implemented and locally validated. Remaining Azure
   secret/job setup and Git publication/release each require separate authorization under
   the delivery checklist. Only a later explicitly
   authorized application deployment replaces the placeholder **in the same app resource**.
   Keep target port 8080 and switch to the hospital image's process-only probes.
   Retain the app resource to retain its verified default hostname.

The following command was executed once after explicit user approval and passing checks.
It is an audit record, **not a command to rerun** against the existing app:

```powershell
az containerapp create --subscription 2cc76c42-5385-4200-b537-6d0ca3d89532 --name ca-harbor-care-demo --resource-group rg-harbor-care-demo --environment cae-harbor-care-demo --image ghcr.io/nginx/nginx-unprivileged@sha256:b8c179cd3c2ae222a873dd59fbae240fadc03836cae5198afc9e9c19919c3880 --ingress external --target-port 8080 --cpu 0.25 --memory 0.5Gi --min-replicas 0 --max-replicas 1 --revisions-mode single --workload-profile-name Consumption --query properties.configuration.ingress.fqdn --output tsv
```

Name absence, selected subscription, spending protection, quota and pinned-image scan were
checked before creation; resource inventory and configuration were verified afterward.
Do not use `containerapp up`, which can create extra resources.
The placeholder can consume the same compute/request allowances as any app; retain spending
protection and scale-to-zero. No plan upgrade or new paid service is part of this proposal.
The local hardening flags were test conditions; the create command did not configure
read-only storage, tmpfs, PID limits or Linux security options in Azure. It retains the
image's non-root user and port. Do not claim Docker-only settings are deployed.
The placeholder serves HTTP internally; Azure
ingress provides the public TLS endpoint. Do not add custom-domain, registry or database
resources for this step. Actual idle/wake scaling and hospital-image behavior remain unverified.

## Historical setup reference — confirmed preparation, not remaining tasks

Neon logins/private saved URLs, the production Auth0 SPA/two-client Action and GitHub
configuration in sections 1–3 were user-confirmed after the original placeholder checkpoint.
Retain the values and reasoning below for reference; do not execute creation, password or
Action-edit steps again. Azure app/job secrets and production migration/seed/deployment
remain separate gates in the [single current checklist](delivery.md#one-remaining-setup-and-authorization-checklist).

### 1. Neon roles and private connection configuration

Follow the [executable private Neon procedure](neon-access.md) for this step. It includes
the exact psql launch, transactional role setup, hidden password prompts, read-only role
verification and clipboard helper. Run only its current setup steps; initial migration,
seed/reset and the post-migration grant script remain deferred. The procedures are locally
tested. Role logins/private connection setup were later user-confirmed; initial production
migrations, grants and seeded workflows have not been executed by this local implementation.

Keep project/branch/database `harbor-care-demo` / `production` / `hospital_coordination`
in Oregon. Do not reset, replace or move the database. The existing privately saved owner
URLs are provisioning credentials, not the app's runtime credentials.

Create two SQL-managed login roles after reviewing/testing grants against an isolated
fixture. Neon Console/CLI/API role creation grants `neon_superuser` membership; use SQL for
limited roles. Both roles should have no superuser, role-creation, database-creation,
replication or bypass-RLS capability and no elevated group membership.

| Role | Intended privileges and use |
| --- | --- |
| `hospital_runtime` | CONNECT/USAGE; after migration, SELECT on care tables, INSERT/UPDATE on appointment, consultation, prescription, fulfillment and the medication cache; INSERT and SELECT(id) only on audit_event. No audit-content read, DELETE/TRUNCATE, sequence mutation, schema CREATE, ownership, migration-history access or maintenance-role membership. |
| `hospital_maintenance` | CREATE/USAGE in the dedicated database's existing public schema and ownership of objects it migrates; use for migration, seed-empty initialization and the existing controlled reset job only. Schema ownership stays unchanged. Object ownership supports `TRUNCATE ... RESTART IDENTITY`; never attach this credential to the web app. |

Initial role/schema setup is a database mutation and requires separate authorization.
Review existing ownership/schema state before any ownership change. Future migrations
must run as the same maintenance owner. Review explicit grants after migrations rather
than granting every future table to runtime, which could expose maintenance/history data.
Test actual workflows and negative DDL/TRUNCATE/audit-update/history-write permissions in
an isolated database before applying grants to Neon. First production migration/seed-empty
is another explicit write checkpoint; an empty database and successful TCP connection do
not prove that a working production schema exists.

Initialize passwordless roles with the hidden-input recovery helper in the linked Neon
procedure. The previous `psql \password` instruction was incompatible with Neon, and the
Console reset action cannot initialize a passwordless role. Do not repeat either or recreate
roles. Never place password literals in SQL-editor history, chat, shell arguments or tracked scripts.
After each successful change, update that role's saved private URLs/Npgsql strings in the same batch.

For the same branch/database, obtain a **pooled runtime** endpoint and **direct maintenance**
endpoint using their respective roles. Privately configure Npgsql key/value strings, not
PostgreSQL URL syntax, with `SSL Mode=VerifyFull;Channel Binding=Require`. Do not enable
certificate trust bypass. Correctly quote special characters through a connection-string
builder or supported UI; manual URI-to-string text replacement is unsafe.

Store the two strings privately now; do not configure the placeholder. At the later Azure
secret configuration checkpoint, enter values directly into these forms:

- Web app secret `hospital-runtime-database` -> `ConnectionStrings__HospitalDatabase`.
- Each job's secret `hospital-maintenance-database` ->
  `ConnectionStrings__HospitalMaintenanceDatabase` for both `--prepare-release` and
  `--reset-demo-data`. Production delivery uses `--prepare-release`, which migrates,
  seeds only an empty database and applies reviewed grants. Do not configure production
  migration with the older `--initialize-database`/runtime-connection setting.

Do not overwrite local development user-secrets. In ignored `.env.neon.local`, preserve the
original owner URL fields and keep them current after owner rotation; privately add/update
`NEON_RUNTIME_POOLED_DATABASE_URL` and `NEON_MAINTENANCE_DIRECT_DATABASE_URL` for app roles
using the recovery checklist's URL helper. The agent must never read this file. For Npgsql
key/value staging, the user may create an ignored root `.env.production.local` with keys
`HOSPITAL_RUNTIME_DATABASE` and `HOSPITAL_MAINTENANCE_DATABASE`; the user edits it locally
and the agent must never read it. No credential values belong in frontend configuration.

### 2. Auth0 production SPA

In the existing tenant, create `Hospital Coordination Web - Production`, type **Single
Page Application**. Keep the existing generic database users, login connection, API
audience and namespaced role claim. Hosting in .NET does not make this a Regular Web App.

| Auth0 setting | Value after `APP_ORIGIN` is verified |
| --- | --- |
| Allowed Callback URLs | `https://ca-harbor-care-demo.salmonsea-5286e167.westus.azurecontainerapps.io/auth/callback` |
| Allowed Logout URLs | `https://ca-harbor-care-demo.salmonsea-5286e167.westus.azurecontainerapps.io` |
| Allowed Web Origins | `https://ca-harbor-care-demo.salmonsea-5286e167.westus.azurecontainerapps.io` |
| Allowed Origins (CORS), if configured | `https://ca-harbor-care-demo.salmonsea-5286e167.westus.azurecontainerapps.io` |

Use the exact HTTPS values, without wildcards. Enable the existing demo database connection
for this client; keep public signup disabled. Verify Authorization Code/PKCE, refresh-token
rotation and the existing API's offline-access setting for the current SDK flow. Keep
tokens in memory and existing session recovery. Confirm the existing Post Login Action
applies the same role claim for the new client; do not edit it blindly if client filtering
is present. Do not change shared API settings without reviewing their effect on local login.
Save only the public domain/client ID/audience/role-claim identifiers for deployment; the
SPA/API do not need an Auth0 client secret. Live production sign-in remains unconfirmed.
Save configuration only at this checkpoint: the NGINX placeholder cannot handle the
Auth0 callback or load the hospital workspace yet. Do not repeat local browser acceptance.

#### Confirmed single-client Action filter: manual correction

The user found `if (event.client.client_id !== hospitalWebClientId) { return; }` in
the connected **Add hospital app role** Action. Replace ONLY that condition/block with:

```javascript
if (![hospitalWebClientId, "REPLACE_WITH_PRODUCTION_CLIENT_ID"].includes(event.client.client_id)) {
  return;
}
```

Replace the placeholder with **Hospital Coordination Web - Production -> Settings -> Client ID**
(the same public value saved as GitHub `VITE_AUTH0_CLIENT_ID`). Keep the existing
`hospitalWebClientId` declaration/value, role allowlist, deny behavior, and all token claim
calls unchanged. Do not paste the Client Secret. This admits the existing and production
clients to the same existing role checks; other clients retain the previous early-return
behavior (it does not deny their unrelated login).

User applies this in Auth0's code editor, saves/deploys the Action and verifies it remains
bound in the Post Login flow. Action deployment activates authentication logic; it is not
a hospital container deployment. No agent-driven Auth0 mutation is authorized. Local
fragment tests use fake client IDs/roles only and do not prove hosted login or the complete
unseen Action. Production login remains deferred while the origin serves NGINX.

### 3. GitHub production environment

Under repository Settings -> Environments, create exactly `production`. Select **Selected
branches and tags**, add **Branch: main**, and add no tag or wildcard rule. Configure the
owner as required reviewer and disable administrator bypass. For a sole maintainer, leave
Prevent self-review off so the owner can approve their manually triggered release; a
separate reviewer is needed before turning that on. Never weaken the branch rule to deploy
the current uncommitted milestone branch.

Prepare these **environment variables** (names are the delivery configuration contract):

| Variable | Value/source |
| --- | --- |
| `AZURE_CLIENT_ID` | `568089e3-1d0f-4bde-a1ff-68e09c00fed7` (existing deployment identity; public identifier) |
| `AZURE_TENANT_ID` | `3a36e4f0-74c8-47db-81f7-ef4495480fc0` |
| `AZURE_SUBSCRIPTION_ID` | `2cc76c42-5385-4200-b537-6d0ca3d89532` |
| `AZURE_RESOURCE_GROUP` | `rg-harbor-care-demo` |
| `AZURE_CONTAINER_APP_ENVIRONMENT` | `cae-harbor-care-demo` |
| `AZURE_CONTAINER_APP_NAME` | `ca-harbor-care-demo` |
| `APP_ORIGIN` | `https://ca-harbor-care-demo.salmonsea-5286e167.westus.azurecontainerapps.io` |
| `VITE_AUTH0_DOMAIN` | New production SPA Settings -> Domain, hostname only, without `https://` |
| `VITE_AUTH0_CLIENT_ID` | New production SPA Settings -> Client ID, not Client Secret |
| `VITE_AUTH0_AUDIENCE` | Existing hospital API Settings -> Identifier in Auth0 |
| `AUTH0_ROLE_CLAIM` | Exact existing namespaced claim URI set by the existing Auth0 Post Login Action; inspect without editing it |

Enter the intentionally public project-only password directly in **environment secret
`VITE_DEMO_PASSWORD`**. Never paste it in chat. Leave production `VITE_API_BASE_URL` unset
so the SPA uses its own origin. Database strings stay in Azure app/job secrets, not in
GitHub; no Azure client secret, Static Web Apps deployment token or GHCR PAT is needed.
Future delivery uses OIDC and scoped `GITHUB_TOKEN` package permissions.

The delivery workflow maps domain/audience/role claim to `Authentication__Auth0__*`,
`APP_ORIGIN` to `Frontend__Origin`, requires `ReverseProxy__TrustForwardedHeaders=true`,
and supplies the exact main SHA as `Release__Revision`. Generic seed subjects and
`DemoReset__ExpectedDatabaseName=hospital_coordination` are maintenance-job configuration,
not browser permissions. The manual maintenance job, secret references, logging choices
and release sequence are specified in [delivery.md](delivery.md); actual resource/secret
setup remains separately authorized. Variables being entered is not a successful OIDC or deployment test.

Report only a single consolidated completion/blocker summary after authorized setup.
Never attach Connect screens, secret values, full environment listings or credential logs.

Suggested single reply: `Neon roles and both login checks: ...; private connection strings
saved: ...; production Auth0 SPA/origins: ...; GitHub production protections/variables/demo
password secret: ...; blockers: ...`. Do not include credentials or their screenshots.
This confirms configuration only; live hospital login, initial migration, seed data and
delivery remain separate checkpoints.

## Delivery implementation and remaining setup

Neon role logins/private URLs, the production Auth0 client/connected two-client Action,
and GitHub production protections/eleven variables/demo secret are user-confirmed.
The locally implemented delivery stage and its single remaining Azure secrets/job and
first-release authorization checklist are in [delivery.md](delivery.md).
No hosted hospital release or production migration/seed is claimed.

## Remaining authorization boundaries

1. Temporary placeholder creation was explicitly authorized and is complete. No hospital
   image deployment or future image update was authorized by that exception.
2. Auth0 client/settings, GitHub environment/settings/secrets and Neon role/grant setup are
   external mutations; this preparation stage authorizes none of them.
3. Initial migrations/seed-empty, real image publication/deployment, scheduled reset and
   first live clinical browser checks require their recorded later authorizations.
4. Git operations remain prohibited until the user explicitly authorizes them after review.

## Sources

- [Azure environment quota API](https://learn.microsoft.com/en-us/rest/api/resource-manager/containerapps/managed-environment-usages/list?view=rest-resource-manager-containerapps-2026-01-01)
- [Microsoft's sample container](https://learn.microsoft.com/en-us/azure/container-apps/get-started)
- [NGINX unprivileged image and supported registries](https://github.com/nginx/docker-nginx-unprivileged)
- [Alpine release support](https://alpinelinux.org/releases/)
- [Neon role privileges](https://neon.com/docs/manage/roles)
- [Auth0 application settings](https://auth0.com/docs/get-started/applications/application-settings)
- [Auth0 refresh-token rotation](https://auth0.com/docs/secure/tokens/refresh-tokens/configure-refresh-token-rotation)
- [GitHub environment protection](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments)
