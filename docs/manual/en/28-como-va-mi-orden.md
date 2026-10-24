# 28 — “How is my shipment doing?” and the progress bar
<!-- kemi:portal -->

Batch 103 (client portal, 2026-10-17). Two related things: the **progress bar** of a shipment and the **public page** where anyone with the waybill number sees which stage their package is at. The public response is **only the progress**: **never names, addresses, phone numbers or amounts** (decided with Luis).

## 1. The progress bar
> **Change of Batch 108 (2026-10-17, dictated by Luis):** the bar **no longer** has as many stages as statuses. It has **four fixed stages** and all the other statuses are boxed into one of them. (Replaces the criterion of Batch 103.)

| Stage | Which statuses fall here | What it says under the bar |
|---|---|---|
| 1. **Submitted** | Submitted | “Submitted” |
| 2. **Received** | Picked up, Unassigned, Assigned (the company already has it) | “Received” |
| 3. **On route** | In transit (the driver has it) | “On route” |
| 3. **On route** (in alert) | Failed attempt | “Attempted” |
| 3. **On route** | Unassigned or Assigned **after a failed attempt** (it came back to the warehouse) | “Rescheduled” |
| 4. **Completed** | **any final status**: Delivered, Cancelled, In warehouse, Returned… | the name of whatever final status it is (its own prevails) |

- **The path of a failed attempt:** On route → **Attempted** (alert, amber) → when it reaches the warehouse and “Arrived” is scanned, **Rescheduled** (still in stage 3) → when the driver takes it again, **On route**. The failed attempt stays in the history.
- **What it shows:** the stages already passed in blue, **the current one highlighted (“it is here”)**, the remaining ones dimmed, and below “Step 2 of 4 · Received”. In the record and on the public page, also the **list of the four stages**.
- **Completed:** the whole bar in green with the name of the final status (“Delivered”, “In warehouse”…). **Cancelled:** the bar goes **dimmed** and says “Cancelled”.
- **The stage names do not depend on the company's statuses** (turning off, adding or renaming statuses does not change the bar). What appears in “Completed” is indeed the name of the final status.
- **Where it is:** in the **shipments list** and the **home** of the client portal (low bar), in the shipment **record** (bar + list of stages) and on the **public page**. The consignee portal (next batch) uses the same component.

## 2. The public page “How is my package doing?” (Batch 116: by link only)
> **Change of Batch 116 (2026-10-18, dictated by Luis):** the manual search by waybill number and the company's “tracking identifier” **were removed**. The page is **by link lookup only**: the notice the consignee receives carries a link with a **token of the shipment's own, long and impossible to guess**, and on opening it the result is seen. The page **does not receive data**.
- **Address:** `<client app>/track/<token>`. No session; the language comes from the link/browser (or the one chosen in the preferences bar).
- **What it answers:** the company name (the branding one), the waybill, the stages with which one is current, the status and **when the last movement was**. Nothing else: **never names, addresses, phone numbers or amounts**.
- **Who receives the link:** the consignee, in the notices the **client** turned on (chapter 34).

## 3. There is no longer a company setting
**Tenant settings → General → Public tracking** no longer exists (there is nothing to turn on: the link travels in each notice). A link that does not exist responds 404.

## 4. Endpoints
| Endpoint | What for | Who |
|---|---|---|
| `GET /api/v1/public/track/link/{token}?lang=es\|en` | The package's progress (`step` of `total`, `state`, `statusLabel`, `steps[]`, `lastUpdateUtc`) | Public, **20 per minute per IP** |

`state`: `IN_PROGRESS`, `COMPLETED`, `ALERT` (stopped) or `CANCELLED`. The client portal (`/api/v1/portal/orders` and `/portal/orders/{id}`) brings the same progress in `progress` (and `stepLabels` in the record).

## 5. Messages and errors
| Situation | Response |
|---|---|
| Link that does not exist, is mistyped or belongs to a deactivated shipment | **404 `We could not find that tracking link.`** (the same in every case: which one it was is not revealed) |
| More than 20 lookups per minute from one IP | 429 `Too many attempts; wait a minute and try again.` |

## Frequent cases
- **“The page says it cannot find my package”:** the link is incomplete or is not from a real notice; use the link exactly as it comes in the e-mail. The page answers the same in every case.
- **“Why don't the name and the address appear?”:** on purpose: anyone with a number can look it up, so only the progress is shown.
- **“The bar has more (or fewer) stages than before”:** the stages are the statuses of the company's normal path; if a status was added, removed or turned off, the bar changes.
- **“My package had a failed attempt”:** the bar stays where it was and the notice says “Failed attempt”; when the company reschedules it, it keeps advancing.
- **The token cannot be guessed:** that is why there is no longer a search by number; even so there is a lookup limit per IP and the page only reveals the progress, never people's data.
