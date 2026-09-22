# Azure bootstrap for the portfolio demo

Bootstrap creates the resource group, empty hosting resources, and deployment identity.
It does not publish the application, connect Neon, run migrations, change Auth0, or create
a GitHub deployment workflow. Current subscription/resource identifiers belong in the
ignored milestone handoff; never copy connection strings or deployment tokens here.

## Prerequisites and region selection

Use an enabled subscription selected through Azure CLI interactive login. The bootstrap
operator needs resource creation and RBAC assignment permissions. Verify the subscription
policy's allowed locations as well as the resource provider's supported regions: a region
listed by `Microsoft.App` or `Microsoft.Web` can still be denied by subscription policy.

The initial student subscription permits `westus` for Container Apps. Its allowed region
list has no intersection with Static Web Apps' supported regions. ADR-013 replaces that
resource with combined React/API hosting in Container Apps. The quota support route also
requires an unsupported student-plan upgrade; do not repeat it, remove policy, or change
billing. Neon stays in AWS Oregon.

## Resource configuration

| Resource | Configuration |
| --- | --- |
| Resource group | `rg-harbor-care-demo`, West US, project/environment tags |
| Container Apps environment | `cae-harbor-care-demo`, West US, built-in Consumption profile only |
| Logs | No Log Analytics workspace or diagnostic export provisioned during bootstrap |
| Deployment identity | User-assigned managed identity `id-harbor-care-github` |
| Identity authorization | Contributor at the demo resource-group scope only |
| Federation | GitHub issuer, exact repository and `production` environment subject, Azure token-exchange audience |
| Combined Container App | React static build plus .NET API; application creation/deployment remains a separate checkpoint |

After verifying the selected subscription, policy, quota, and that names are unused, the
hosting bootstrap uses these Azure CLI options. Commands are reference documentation, not
an instruction to rerun creation over existing resources:

```powershell
az group create --name rg-harbor-care-demo --location westus --tags project=harbor-care environment=demo milestone=06
az containerapp env create --name cae-harbor-care-demo --resource-group rg-harbor-care-demo --location westus --enable-workload-profiles true --logs-destination none
az identity create --name id-harbor-care-github --resource-group rg-harbor-care-demo --location westus
az identity federated-credential create --name github-production --identity-name id-harbor-care-github --resource-group rg-harbor-care-demo --issuer https://token.actions.githubusercontent.com --subject repo:nikolaisemerdjiev1/hospital_database:environment:production --audiences api://AzureADTokenExchange
```

Register `Microsoft.App` and `Microsoft.ManagedIdentity` first. `Microsoft.Web` is no longer
needed. Assign Contributor using the managed identity's
principal ID, ServicePrincipal type, and the exact demo resource-group scope; do not grant
subscription-wide Contributor or Owner to GitHub. No Azure client secret is needed.

Before delivery, configure GitHub's `production` environment with the intended branch and
approval protections. A federated credential is trust configuration, not evidence of a
successful GitHub login. Live federation validation requires a later authorized workflow.
No Static Web Apps wizard or deployment token is needed. App deployment still requires
its own authorization checkpoint.

## Cost controls

The goal is $0 at light portfolio traffic. Container Apps' monthly subscription-level
allowances cover 180,000 vCPU-seconds, 360,000 GiB-seconds, and two million HTTP requests.
At 0.25 vCPU and 0.5 GiB, the compute allowances correspond to 200 active replica-hours;
this is arithmetic, not measured application capacity. Jobs and other apps share allowances.
The eventual API should start with minimum zero and maximum one replica, subject to
memory/load validation. Persistent probes and traffic can keep it running.

An empty Consumption-only environment has no running app replicas. Avoid Dedicated
profiles, private endpoints, custom networking, paid monitoring, paid registries, and
optional add-ons unless separately approved. Final image storage uses public GHCR;
Neon remains Free. Logging retention and its cost limit must
be decided before release; live log streaming alone does not provide incident history.

For Azure for Students, retain the existing spending limit. Credit exhaustion/expiry can
interrupt service; verify remaining credit and renewal eligibility in the account portal.
Do not promise an unlimited free deployment or remove spending protection to fix a resource
restriction. A small monthly cost budget can notify the owner, but budget alerts do not stop
resources. No budget, paid upgrade, or billing-setting change is part of this bootstrap.

## Validation and remaining gates

Verify resource provisioning completes, the only workload profile is Consumption, no
workspace or dedicated resources appeared, and identity RBAC/federation match the intended
scope. Inspect only safe projections of resource metadata, never full secret-bearing output.
Keep policy restrictions, capacity/quotas, and application health as separate checks.

After authorized Container App creation supplies the exact hostname, continue the production Auth0
checkpoint, database runtime-role/grant and secret setup, protected GitHub environment,
deployment automation, and actual release validation. Existing local acceptance remains
complete; no production behavior is inferred from successful bootstrap.

## Sources

- [Container Apps billing](https://learn.microsoft.com/en-us/azure/container-apps/billing)
- [Azure spending limits](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/spending-limit)
- [Azure/GitHub OIDC](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect)
- [Azure subscription support](https://learn.microsoft.com/en-us/azure/azure-portal/supportability/how-to-create-azure-support-request)
