# Milestone 6 release evidence

| Evidence | Result | Qualification |
| --- | --- | --- |
| Release | SHA `4f52f261cdc0b68b92848220ae0b7f0a11f5dba1`, [run 35761286852](https://github.com/nikolaisemerdjiev1/hospital_database/actions/runs/35761286852) succeeded | Immutable release evidence, not an uptime claim. |
| Hosted role journey | User-confirmed patient, doctor, pharmacist, reload, denial, and privacy checks passed | Manual/user observation; not automated evidence. |
| Manual reset | [run 35833965206](https://github.com/nikolaisemerdjiev1/hospital_database/actions/runs/35833965206), `job-harbor-care-reset-d2x143o`, succeeded 07:53:14–07:53:43 UTC | Manual execution, not scheduler proof. |
| Scheduled reset | `job-harbor-care-reset-29836020` succeeded 2026-09-23 11:00:00–11:00:28 UTC | Read-only execution-history observation; distinct from the manual run. |
| Hosted timing | User observed about 15 seconds to open and about 1 second to ready UI | Approximate observation, not an SLA or percentile. |
| Hosted resources | Samples peaked near 0.0954 core / 117.2 MiB; 0 restarts, maximum 1 replica | One-minute observations, not capacity or load proof. |

The local non-product social-preview card is available at `docs/assets/social-preview.png`; it is not repository metadata and makes no claim that GitHub social preview is configured.

## Screenshot provenance

All five captures were supplied by the user from read-only hosted seeded views on 2026-09-23. They show only the application view and synthetic records; no password, login form, browser chrome, or account overlay is present. Origin: `https://ca-harbor-care-demo.salmonsea-5286e167.westus.azurecontainerapps.io`. Released revision: `4f52f261cdc0b68b92848220ae0b7f0a11f5dba1`.

| Asset | Captured state | Raster dimensions | Qualification |
| --- | --- | --- | --- |
| `landing.png` | Landing hero, readiness, and role cards | 970×1255 | Password concealed; public role email hints are part of the page. |
| `patient.png` | Patient itinerary and received-by-pharmacy status | 832×1253 | Existing seeded record; no action taken. |
| `doctor.png` | Completed consultation and medication search | 621×1053 | Existing completed record; no prescription issued. |
| `pharmacist.png` | Unclaimed fulfillment detail | 997×818 | Existing seeded record; no claim or transition action. |
| `mobile.png` | Patient itinerary at a 390px-class viewport | 786×4532 | Retina-scale raster (about 393 CSS px); existing seeded record. |

The desktop captures preserve the supplied crop rather than being resampled into a claimed 1440×900 viewport. Date-relative seeded content can change after reset, so this provenance—not pixel identity—is the record of what was captured. No screenshot is fabricated or created by a test fixture.
