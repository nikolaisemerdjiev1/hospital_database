# Combined SPA and API hosting

> **Hosted checkpoint:** The combined application is released at SHA `4f52f261cdc0b68b92848220ae0b7f0a11f5dba1`. User-reported opening was about 15 seconds and ready UI about 1 second; sampled one-minute highs were about 0.0954 core and 117.2 MiB with zero reported restarts. These are bounded observations, not an SLA, load test, or capacity guarantee.

React remains a client-rendered SPA. ASP.NET Core serves its compiled files from `wwwroot`
and the existing JWT-protected API from the same origin. No server-side rendering, Node
runtime, server session, or authorization bypass is introduced. Local Vite on 5173 and API
on 5050 remain separate; a production build without `VITE_API_BASE_URL` uses its own origin.

## Build and security

The Dockerfile uses a digest-pinned Node 24 build stage and locked npm dependencies, then
copies only `dist` into the existing non-root .NET image. The Docker context excludes root
and nested `.env` files, local build outputs, dependencies, and test artifacts.

Supply the three public `VITE_AUTH0_DOMAIN`, `VITE_AUTH0_CLIENT_ID`, and
`VITE_AUTH0_AUDIENCE` build arguments explicitly. Use BuildKit secret ID `demo_password`
for the intentionally public demo password; never a build argument or committed file.
The user supplies its value privately at the production configuration checkpoint. It is
intentionally present in the compiled UI, so public image consumers can also recover it.
BuildKit secret contents do not invalidate cache: rebuild the frontend stage without cache
when rotating that public value. Never put database credentials or an Auth0 client secret
in frontend configuration. Local and ordinary CI checks use fake values only.

Only `/assets/*`, `/hospital-mark.svg`, and GET/HEAD SPA routes `/`, `/app`, `/app/*`, and
`/auth/callback` are public. Missing asset files, unknown API/health paths, and non-GET SPA
requests do not fall through to HTML. Hashed Vite assets use immutable one-year caching;
HTML and the unversioned icon revalidate. API responses remain `no-store`.

The HTML CSP allows same-origin scripts/styles/fonts, exact configured Auth0 connections
and frames, data images/fonts, the Auth0 SDK's blob worker for memory-only refresh tokens,
and inline style attributes used by UI libraries. It allows no
inline scripts, eval, wildcard origins, objects, embedding, or form submissions. API CSP
remains restrictive. Public asset downloads bypass the API burst budget; API rate limits
and identity/role/ownership policies remain unchanged. Application logs contain route
templates rather than callback query values; retain the configured ASP.NET warning log level.

## Platform probes and low-cost operation

Use the [production configuration reference](production-configuration.md) for the actual
hostname, Neon privileges, Auth0 origins and confirmed preparation. Use the single remaining
[delivery setup checklist](delivery.md#one-remaining-setup-and-authorization-checklist) for
separately authorized secret/job setup and application release; do not repeat completed setup.

Use `/health/live` for Container Apps startup, liveness, and traffic readiness. It checks
the running process, not PostgreSQL, and is exempt from visitor throttling. Keep
`/health/ready` database-backed for the landing's bounded checks and passive release smoke.
Do not attach continuous uptime monitors or platform probes to database readiness.

Start with Consumption, HTTP scale-to-zero, minimum 0, maximum 1, and one active revision.
The proposed 0.25 vCPU / 0.5 GiB allocation needs hosted validation; local Docker limits
provide only a baseline. Monthly free compute corresponds to about 200 running hours at
that size, shared with jobs/other applications. Request, network, logs, and database quotas
remain separate. Replica limits and budget notifications are not billing hard caps.
Keep student spending protection enabled; choose bounded logging before release.

The first page request waits for the app to start. It may encounter a platform failure
before our page runs; the React retry UI cannot cover this interval. Once running, the shell
stays available through database outages. Hosted image pull, Azure activation, Neon wake-up,
real Auth0 callbacks and memory under authenticated workflows remain release checks.

## Validation

CI builds a fake-config combined image and runs it on loopback port 8086 with a separate
empty PostgreSQL fixture, 0.25 CPU / 512 MiB, non-root user, read-only root filesystem, dropped
capabilities, and no new privileges. It runs passive smoke and
`npx playwright test --config playwright.container.config.ts` from `frontend` against that
image. These tests exercise actual HTTP headers/CSP, deep links, same-origin readiness,
the SDK redirect boundary, keyboard/reflow/reduced-motion behavior and axe. They do not
log into real Auth0 accounts. Keep ordinary browser regressions as well.

ASP.NET integration tests verify the public shell with an unavailable database, private
file/error fallthrough, API denial, cache policies, and static asset bursts. No validation
may connect to the demo or Neon database; use isolated synthetic fixtures. Production
HTTPS/HSTS, real authentication, cold starts, resource capacity, and full workflow checks
remain separate from these local results.

Local assessment on September 13, 2026: at 0.25 CPU / 512 MiB, the combined image passed
the production browser suite and 100 anonymous shell requests at concurrency four.
Recorded cgroup memory peak was 74,780,672 bytes (about 71.3 MiB), with no OOM termination
or restart. One local process-start measurement was 3.72 seconds to liveness with the
image already present. These are local baseline observations, not Azure cold-start timing
or authenticated care-workflow capacity. Stopping the isolated database left `/` and
`/health/live` at 200 while `/health/ready` returned 503.
