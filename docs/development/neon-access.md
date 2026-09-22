# Private Neon access setup

User-confirmed recovery complete: runtime direct/pooled login and maintenance direct login
passed; owner URLs are current and application URLs saved privately. Do not rerun password
initialization or bootstrap. The procedure below is retained as a recovery reference. This is the Neon
part of the [single production setup checklist](production-configuration.md#one-grouped-checklist-after-the-hostname-and-setup-authorization).
Use your own terminal for the following manual setup. Do not share passwords, connection
URLs, clipboard contents or screenshots of credential screens with the agent.

## Current recovery: existing roles have no passwords

The owner login succeeded, but the original `psql \password` instructions failed on Neon:
the client sends a pre-hashed password and Neon rejected it. The Console reset action then
reported `cannot update password for role without password`. Those are confirmed user
observations. Neither action initialized the two application-role passwords. Do not repeat
them, recreate roles, or rerun bootstrap. The owner password was separately rotated by the
user; leave that current password unchanged.

Run this complete batch in your own terminal. No agent should run it against Neon.

1. Exit psql with `\q` if still connected. Keep the existing project, production branch,
   database and both roles. In your password manager, generate and save two different random
   **32-character ASCII passwords without spaces**, labeled `hospital_runtime` and
   `hospital_maintenance`. Do not use your owner or Auth0 password for either.
2. From the repository root, build the helper, then launch it:

   ```powershell
   dotnet build scripts/production/NeonPasswordSetup/NeonPasswordSetup.csproj --configuration Release -p:RestoreLockedMode=true
   dotnet scripts/production/NeonPasswordSetup/bin/Release/net10.0/NeonPasswordSetup.dll
   ```

   This standalone tool uses the existing Npgsql dependency, not the application startup
   or configuration loaders. It never opens `.env.neon.local`, user-secrets, or other
   credential files. Use a normal private terminal without transcription, debug tracing,
   output redirection or session recording.
3. Answer its prompts in this order: mode **initialize**; direct endpoint hostname only
   (from Neon Connect, pooling off); role **hospital_runtime**; its new password twice;
   existing database owner role name; **INITIALIZE**; the current **owner** password.
   Password input is hidden. The owner password only authorizes this operation; it is not
   changed. Expected: change acknowledged, direct login PASS, pooled runtime login PASS.
4. Launch the same `dotnet ...NeonPasswordSetup.dll` command again for
   **hospital_maintenance**, using its own new password and the same owner/hostname.
   Expected: change acknowledged and direct login PASS. There is no pooled maintenance check.
5. **Update `.env.neon.local` privately immediately after each successful initialization.**
   Its original `NEON_POOLED_DATABASE_URL` and `NEON_DIRECT_DATABASE_URL` entries are owner
   provisioning URLs: keep them as owner URLs, updated to the owner password you already
   rotated. Do not replace their username with an application role. Add the distinct fields
   below, or update them if already present:

   | Local field | Account / endpoint |
   | --- | --- |
   | `NEON_RUNTIME_POOLED_DATABASE_URL` | `hospital_runtime`, pooled hostname |
   | `NEON_MAINTENANCE_DIRECT_DATABASE_URL` | `hospital_maintenance`, direct hostname |

   Disable clipboard history, cross-device sync and any external clipboard manager. Generate
   each URL using the following command; the helper copies it privately without printing it.
   Enter that role's hostname and its saved new password. Paste ONLY the URL after the
   matching `KEY=` in your local editor, save, then press Enter in the terminal to clear the
   clipboard. Do not put the command itself or the Npgsql key/value format into these URL fields.

   ```powershell
   ./scripts/production/Copy-NeonConnection.ps1 -Role hospital_runtime -Format PostgreSqlUrl -Copy
   ./scripts/production/Copy-NeonConnection.ps1 -Role hospital_maintenance -Format PostgreSqlUrl -Copy
   ```

   The URL helper percent-encodes special characters; do not splice a raw password into a URL.
   These ignored fields are private staging only, not automatically loaded by the app. Local
   Docker/user-secrets and frontend credentials remain unchanged. For any later rotation,
   update every saved URL/Npgsql string for THAT role in the same batch.
6. Report one combined non-secret result: runtime direct/pooled login; maintenance direct
   login; owner URLs current; two application URLs saved. No screenshots or values.

If a stage fails, stop and share only its printed stage. A failed response after a password
change may have an uncertain outcome; keep the password you entered. Once the connection
issue is resolved, choose **verify** mode with the same saved application password to check
logins without changing anything or needing the owner password. Never automatically rerun
initialize or reset roles. An input-stage failure means check spelling, password match and
the documented password requirements. A logging-preflight failure needs review, not bypass.

### Recovery architecture and evidence boundary

The initializer allowlists the two roles, checks the current database owner, expected empty
public schema, existing role capabilities/memberships and restricted runtime creation
privileges. It changes only the selected password. It creates a session-temporary function
containing no secret, passes the new password as a bound parameter, and formats the SQL
literal on the server. The function sanitizes exceptions and disappears when the owner
connection closes. All Neon connections require certificate/hostname verification and
channel binding. Connection/command timeouts are bounded; no mutation is retried.

The client has no configured SQL logger, file output or credential arguments. It disables
error parameter logging in its session and refuses verbose statement/duration/audit/auto-explain
settings and nested pg_stat_statements tracking. This reduces PostgreSQL log exposure;
it is not an audit of Neon's internal control-plane telemetry or a guarantee about provider
storage. Plaintext necessarily exists briefly in process/server memory and inside TLS.
The agent has not connected to Neon or handled real credentials. Local PostgreSQL success
does not prove Neon's control-plane behavior; actual success requires the user's PASS output.

## Initial setup reference (do not repeat during recovery)

## 1. Open a private owner session

In Neon, select **harbor-care-demo -> production -> hospital_coordination -> Connect**.
Select the existing database owner and turn pooling off. Privately retrieve its password
from your saved credentials. Copy only the direct endpoint's hostname when prompted below.
Do not create the two app roles through the Neon Console's role-creation UI.

From the repository root in your own PowerShell terminal, with Docker running:

```powershell
$neonHost = Read-Host 'Direct Neon endpoint hostname only, without URL or password'
$neonOwner = Read-Host 'Existing database owner role name'
if ($neonHost -cnotmatch '^ep-[a-z0-9-]+\.(?:[a-z0-9-]+\.)+neon\.tech$' -or $neonHost.Split('.')[0].EndsWith('-pooler')) { throw 'Use the direct Neon hostname only.' }
if ($neonOwner -cnotmatch '^[a-zA-Z_][a-zA-Z0-9_]*$') { throw 'Check the owner role name.' }
$setupPath = (Resolve-Path './scripts/production').Path
docker run --rm -it --mount "type=bind,source=$setupPath,target=/setup,readonly" --env PGSSLMODE=verify-full --env PGSSLROOTCERT=/etc/ssl/certs/ca-certificates.crt --env PGCHANNELBINDING=require --entrypoint psql postgres:18.4-alpine3.24@sha256:9a8afca54e7861fd90fab5fdf4c42477a6b1cb7d293595148e674e0a3181de15 -X -W --host $neonHost --username $neonOwner --dbname hospital_coordination --set ON_ERROR_STOP=on --set HISTFILE=/dev/null
```

Enter the owner password only at psql's hidden password prompt. This runs only the psql
client, mounts only the non-secret setup scripts, disables psql startup/history files,
and verifies TLS certificates, hostname and channel binding. It does not start or initialize
a database. No password is placed in a shell argument, environment variable or script.
The pinned client was checked locally: PostgreSQL 18.4 with its public CA bundle present.
Actual Neon connectivity is still a manual result, not a locally proven result.

## 2. Create roles once, then initialize passwords with the recovery helper

Inside psql, run these separately, in order:

```text
\i /setup/bootstrap-roles.sql
\i /setup/verify-roles.sql
\q
```

Use the recovery helper above to initialize passwords after this one-time setup. Do not
use `\password` or the Console reset action for passwordless roles. The bootstrap transaction
creates roles with no password initially; it never embeds a password in SQL. It refuses
the wrong database, existing role names, nonempty public schema, or extra user schemas.
If any step errors, run `ROLLBACK;` and `\q`; report only the error category. Do not drop
roles, remove existing tables, change ownership or bypass a guard to make it run.

The read-only verification should show both login roles, all elevated flags false, and
zero application tables at this checkpoint. Runtime has CONNECT/USAGE but cannot create
tables, schemas or temporary tables. Maintenance may create tables in this dedicated
database's existing `public` schema and will own its migrated objects. Existing schema
ownership stays unchanged; no application table, migration or seed data is created here.

The recovery helper verifies the actual direct/pooled logins. For an additional read-only
metadata check, reopen the owner session above and replace the hostname markers below
with hostname-only values from the same Neon project's production branch:

```text
\connect hospital_coordination hospital_runtime <pooled-hostname>
\i /setup/verify-roles.sql
\connect hospital_coordination hospital_maintenance <direct-hostname>
\i /setup/verify-roles.sql
\q
```

Connection changes preserve the TLS settings and request the new role's password. Check
the reported connected role after each successful verification. Do not send these commands
with literal angle-bracket markers. No application data needs to be read or written.

## 3. Prepare private Npgsql strings

Before using the clipboard helper, turn off Windows clipboard history and cross-device
clipboard sync. Run each command below in your own terminal. Enter the corresponding
hostname and password at its prompts; paste the result into a private password-manager
entry, then return to the terminal and press Enter to clear the clipboard.

```powershell
./scripts/production/Copy-NeonConnection.ps1 -Role hospital_runtime -Copy
./scripts/production/Copy-NeonConnection.ps1 -Role hospital_maintenance -Copy
```

Runtime requires the pooled hostname; maintenance requires the direct hostname. The
helper builds Npgsql key/value syntax with correct special-character quoting, verified
TLS, required channel binding, bounded timeouts and a runtime pool capped at 20. It neither
connects nor reads a file, and never prints or writes the string to disk. Plaintext exists
briefly in process memory and the clipboard, as necessary to paste into a secret form;
clipboard clearing does not erase external clipboard-manager history. Keep these strings
in your private vault until the later Azure app/job secret configuration checkpoint.
Do not alter the NGINX placeholder's settings or overwrite local development credentials.

## Deferred: migration and runtime grants

**Do not run migrations, initialize/seed data, reset data, or run `grant-runtime.sql` now.**
After the separately authorized initial migration as `hospital_maintenance`, that role
must run `scripts/production/grant-runtime.sql` before the hospital app receives traffic.
It checks the exact current table set and ownership, grants required access, and removes
stale table/column/sequence grants. No blanket privileges for future tables are installed.

The runtime can read the care tables and insert/update workflow rows and the medication
cache. Audit access is INSERT plus SELECT of `id` only for EF's generated-key return.
It cannot read audit metadata, edit/delete audit events, mutate user identities, access
migration history, delete/truncate rows, call future functions by default, or escalate to
the maintenance/owner roles. IDENTITY ALWAYS inserts need no separate sequence grant;
explicit sequence mutation is denied. Every later migration needs a grant review.

Local evidence: real PostgreSQL 18 with password authentication, non-superuser provisioning owner, actual EF migration
and synthetic seed, runtime booking -> consultation -> prescription -> dispensing, and
negative privilege/rollback checks passed. PowerShell tests use synthetic passwords only;
9 Npgsql/URL quoting/security/endpoint cases passed without clipboard or network access.
The recovery suite covers 11 tests including passwordless-login failure, initialization,
quoted-password logins, unchanged role/grant/ownership metadata, wrong-password rejection,
wrong-role/database and unsafe-state guards, verbose logging refusal, and sanitized server errors.

Sources: [Neon SQL-role behavior](https://neon.com/docs/reference/compatibility),
[psql password prompts](https://www.postgresql.org/docs/18/app-psql.html),
[ALTER ROLE behavior and logging cautions](https://www.postgresql.org/docs/18/sql-alterrole.html),
[TLS verification](https://www.postgresql.org/docs/current/libpq-ssl.html),
[Npgsql settings](https://www.npgsql.org/doc/connection-string-parameters).
