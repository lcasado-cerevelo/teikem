# Chapter 07 — Day pulse and Recent activity (Batch 7A: Warehouse; Batch F8a: panels, permissions and order; Batch 14: Needs your attention; Batch 15: Day pulse)

This chapter describes the **Warehouse** part of "Recent activity" (the recent events panel of the Day
pulse), the Warehouse indicators/chart added to the Pulse in Batch 7A and, since Batch F8a, **how each user's
Pulse is built**: panels with their own permission, which indicators and charts each person can read, and the order at two
levels (company and user) — section 3; since Batch 14, the **Needs your attention** panel (section 4); and, since Batch 15, the **Warehouse today** band, the fixed rows, the indicators per row, the charts always drawn, the 2 company charts and the **Puerto Rico days** (section 5). Operations (7B) and Accounting (7C)
add their own Recent activity tab and their own indicators later, on the same
infrastructure. The messages are verified against the code (`src/Teikem.Infrastructure/Services/Activity/
ActivityRules.cs`, `src/Teikem.Infrastructure/Services/Activity/WarehouseActivityProvider.cs`,
`src/Teikem.Infrastructure/Services/ActivityFeedService.cs`, `src/Teikem.Api/Controllers/ActivityController.cs`,
`src/Teikem.Infrastructure/Services/ProductService.cs`, `src/Teikem.Infrastructure/Seeding/
SystemAnalyticsSeeder.cs`, and since Batch 15 `src/Teikem.Infrastructure/Services/WarehousePulseService.cs`, `src/Teikem.Domain/Wms/WarehousePulseRules.cs`, `src/Teikem.Infrastructure/Services/AnalyticsService.cs` and `src/Teikem.Infrastructure/Analytics/AnalyticsEngine.cs`). The questions and answers for each message are in [faq.md](faq.md). The Day pulse
itself (indicators, charts, my date range) is described in chapter
[F1 — frontend](frontend/f1-nucleo-y-mi-cuenta.md); this chapter is the backend part that panel consumes.

---

## 1. Recent activity (Warehouse)

What it does: shows, in a short window, the Warehouse business events that just happened — receivings,
warehouse tasks, cycle counts, pick batches, purchase orders, cross-dock, inventory adjustments and
transfers, product or warehouse deactivation — without being the technical audit (`AuditLog`, which requires `admin.audit`). There
is no events table: each query computes the events on read, from the status history, the inventory
ledger and the log, within the requested window. Each event has a fixed code (`ActivityEventType` list), a
label in the user's language, a **mandatory** flag (always visible) or **optional** (it can be hidden
with "Mandatory only" or turned off by default), and it is linked to the document that originated it.

Who can: the tab of a business module is seen with that module's permission — Warehouse: `inventory.view` (and the
tenant module `WMS_LOTSERIAL` on) — in addition to `analytics.view` and the `ANALYTICS` module, which the
whole endpoint requires. Without `inventory.view` (or with `WMS_LOTSERIAL` off) the Warehouse tab does not appear; requesting it
anyway responds 403. Without `analytics.view` (or with `ANALYTICS` off) you do not even get that far: the controller
policy responds 403 before running the service.

How it is used:
- `GET /api/v1/analytics/activity?module=WAREHOUSE&window=24h&onlyMandatory=false&skip=0&take=50` — response:
  `{ "total": 12, "visibleModules": ["WAREHOUSE"], "items": [ { "occurredAtUtc": "...", "code":
  "RECEIPT_CONFIRMED", "module": "WAREHOUSE", "mandatory": true, "label": "Recibo confirmado", "entityType":
  "RECEIPT", "entityId": 41, "publicId": "...", "reference": "REC-000041", "detail": "ALM-01 · Cliente X · 3
  líneas", "userId": 7, "userName": "Ana Ruiz" } ] }`.
- `module` — tab to read (for now only `WAREHOUSE`); if omitted, the first module visible to the
  user is used. If the user sees no module, the response is `{ "total": 0, "visibleModules": [], "items": [] }`
  (200, not 403).
- `window` — `24h` (default), `48h` or `today` (from midnight **today in Puerto Rico time**, 04:00 UTC; since
  Batch 15. Before it was UTC midnight. The company does not store its time zone yet: it is Puerto Rico's for all).
- `onlyMandatory` — `true` leaves only the mandatory events of the list (or of the tenant override, section 3).
- `skip`/`take` — pagination; `take` default and maximum 50 ("See more" requests the next page with `skip`).
- `GET /api/v1/products?belowMin=true` (the same endpoint as always, with the new filter) — only active products with
  a defined minimum and available quantity below it (the same calculation as `isBelowMin` in the list); with
  `warehousePublicId` as well, the available quantity is that warehouse's. It is used to count "below minimum" by category in
  the Warehouse panel of the Pulse with `take=1` and the `total` of the response, without fetching the full list.

### List of Warehouse events (23, `LookupCode.Entity = 'ActivityEventType'`)

| Code | Label (en) | Mandatory | On by default | When it is generated |
|---|---|---|---|---|
| `RECEIPT_CONFIRMED` | Receipt confirmed | Yes | Yes | Receipt goes to `RECEIVED` (Completed) or `RECEIVED_VARIANCE` (Completed with variance) |
| `RECEIPT_VARIANCE` | Receipt variance | Yes | Yes | Ledger adjustment with reason `RECEIPT_VARIANCE` on the receipt |
| `RECEIPT_PUTAWAY_DONE` | Receipt put away | No | Yes | Receipt goes to `PUTAWAY` |
| `ASN_CANCELLED` | Arrival notice cancelled | No | Yes | Arrival notice goes to `CANCELLED` |
| `PUTAWAY_DONE` | Putaway completed | No | Yes | Warehouse task of type `PUTAWAY` goes to `DONE` |
| `REPLENISH_DONE` | Replenishment completed | No | Yes | Warehouse task of type `REPLENISH` goes to `DONE` |
| `TASK_CANCELLED` | Task cancelled | No | Yes | Any warehouse task goes to `CANCELLED` |
| `COUNT_FINISHED` | Count finished | No | Yes | Cycle count goes to `COUNTED` |
| `COUNT_RECONCILED` | Count reconciled | Yes | Yes | Cycle count goes to `RECONCILED` (Matched) or `RECONCILED_VARIANCE` (Variance, since Batch 14) |
| `COUNT_VARIANCE` | Count variance applied | Yes | Yes | Ledger adjustment with reason `COUNT_VARIANCE` on the count |
| `INVENTORY_ADJUSTED` | Inventory adjustment | Yes | Yes | Ledger adjustment with a manual reason (DAMAGE, LOSS, FOUND, EXPIRED, OTHER) or any other without a source document |
| `INVENTORY_TRANSFERRED` | Transfer between warehouses | No | Yes | Transfer between two different warehouses |
| `BIN_MOVED` | Bin move | No | **No** | Transfer within the same warehouse (bin to bin) |
| `PICK_COLLECTED` | Pick batch created | No | Yes | Pick batch goes to `COLLECTED` |
| `PICK_PACKED` | Pick batch packed | No | Yes | Pick batch goes to `PACKED` |
| `PICK_CANCELLED` | Pick batch deleted | Yes | Yes | Pick batch goes to `CANCELLED` |
| `PO_SENT` | Purchase order sent | No | Yes | Purchase order goes to `SENT` |
| `PO_RECEIVED` | Purchase order fully received | No | Yes | Purchase order goes to `RECEIVED` |
| `PO_CANCELLED` | Purchase order cancelled | Yes | Yes | Purchase order goes to `CANCELLED` |
| `PO_SHORTAGE_RESOLVED` | Shortage resolved | No | Yes | A shortage resolution is recorded (CLOSE, REORDER or MANUAL_ADJUSTMENT) |
| `CROSSDOCK_COMPLETED` | Cross-dock completed | No | Yes | Cross-dock plan goes to `COMPLETED` (only with the `CROSSDOCK` module on) |
| `PRODUCT_DEACTIVATED` | Product deactivated | No | Yes | The deactivation (`IsActive` 1→0) of a product is audited |
| `WAREHOUSE_DEACTIVATED` | Warehouse deactivated | Yes | Yes | Warehouse goes to `INACTIVE` |

There is no status of its own for "Recent activity": each row of the feed reflects a transition or a movement that already
happened in its own entity (receipt, task, count, purchase order, etc.); that life cycle is described in
chapter [06 — Inventory and warehouse](06-inventario-y-almacen.md) and in the Purchases one. A **mandatory** event
is always shown (with or without "Mandatory only"); an **optional** one is shown unless it is off by default
(`BIN_MOVED`) or "Mandatory only" is requested.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| `take` > 50 | `The maximum per page is 50.` | 400 |
| `window` other than `24h`, `48h` or `today` | `The window must be 24h, 48h or today.` | 400 |
| `module` requested without seeing that tab (without the module permission, or with the tenant module off) | `You do not have permission to see the activity of module {module}.` (e.g. `WAREHOUSE`) | 403 |
| With the `ANALYTICS` module off for the tenant | `The 'ANALYTICS' module is not enabled for this company.` (`ModuleDisabledException`, ProblemDetails) | 403 |
| Without `analytics.view` | No body: the controller's `[RequirePermission]` policy rejects it before entering the service (empty ASP.NET Core 403, not a domain ProblemDetails) | 403 |

The 403 for a non-visible module also leaves a `PERMISSION_DENIED` `SecurityEvent` (visible in
`GET /api/v1/audit/security-events` for whoever has `admin.audit`).

---

## 2. Warehouse indicators and chart in the Pulse

What it does: adds to the Day pulse three new indicators and one new chart, system ones (`IsSystem`), visible to the whole
organization (`ShowInPulse = true`), in the `WAREHOUSE` module:

- **Units received** — signed sum of the receiving movements (`RECEIPT`) plus the ledger adjustments with
  reason `RECEIPT_VARIANCE`, over the last 7 days. It is the net of both, not only what was expected on the receipt: a receipt
  with 10 expected units and 8 received adds 8, not 10 (one receiving line for what was expected plus an adjustment for the
  difference; see the FAQ).
- **Counts with variance** — number of cycle counts in status **Variance** (`RECONCILED_VARIANCE`) in the last 30 days.
  Since Batch 14 the indicator's filter is `StatusCode eq RECONCILED_VARIANCE` (before it was `RECONCILED` with "has variance"): a
  count ends in Variance when at least one movement was posted on confirming it (chapter 06, section 6). The seed corrects the filter
  in companies already created, only if they had the exact previous filter (a customized one is not touched).
- **Products below minimum** and **Active products** — already existed since Batch 6 (same filter as the
  "Inventory below minimum" view); they are not duplicated in this batch.
- **Inventory movements by type** (bar chart) — since Batch 15 it measures **units moved as a positive number** (field
  `Units`, the absolute value of the quantity; before it added the signed quantity and dispatch came out negative) over the
  last 7 days, grouped by movement type (receiving, dispatch, adjustment, transfer, cross-dock). It is no longer a
  system one: it is a **company chart** that can be edited (section 5.5).

Who can: they are seen in the Day pulse with the same rule as any indicator/chart (section 3): the panel
permission (`pulse.indicators` / `pulse.charts`) and, **since Batch F8a**, being able to read their data source — all of them are
warehouse ones (`INVENTORY_TRANSACTION`, `CYCLE_COUNT`, `PRODUCT`, `STOCK_BALANCE`), so they require `inventory.view` — with the
`WMS_LOTSERIAL` module on (business module `WAREHOUSE`). Up to Batch 7A, `analytics.view` was enough.

How it is used: there is no endpoint of its own; they are read with the rest of the Pulse, `GET /api/v1/analytics/pulse`, and listed like
any indicator with `GET /api/v1/analytics/indicators` / `GET /api/v1/analytics/charts`.

### Validations

There are no validations of its own in this section: they are system data seeded by the provider, with no user
input.

---

## 3. Day pulse: panels, permissions and order (Batch F8a)

What it does: the Day pulse (the home screen) is built from **panels**. Each panel has its own `pulse.*`
permission, the read permissions of the data it shows and the tenant module that must be on; the server
only returns the panels the user can see, and within "Your indicators" and "Your charts" only the elements whose
data source they can read. This is how you configure, for example, a warehouse user who **only sees warehouse things**. Also, the
order and the hidden ones are saved at two levels: **the company's** (seen by everyone who does not have their own) and **mine**.

### Pulse permissions (category `PULSE`, "Day pulse")

| Code | Label (es / en) | What it opens |
|---|---|---|
| `pulse.indicators` | Ver indicadores en el Pulso / See indicators on the Pulse | "Your indicators" section (each indicator also requires reading its source) |
| `pulse.charts` | Ver gráficos en el Pulso / See charts on the Pulse | "Your charts" section (same) |
| `pulse.warehouse` | Ver el panel Almacén del Pulso / See the Warehouse panel | Warehouse panel. Data: `inventory.view`; module `WMS_LOTSERIAL` |
| `pulse.activity` | Ver Actividad reciente en el Pulso / See Recent activity | Recent activity panel. Data: `analytics.view`; module `ANALYTICS` |
| `pulse.organize_company` | Organizar el Pulso de la compañía / Organize the company Pulse | "Organize the company's" (`PUT …/layout?scope=company`) |
| `pulse.attention` (Batch 14) | Ver la sección Necesita tu atención del Pulso / See the Needs your attention section | Needs your attention panel (section 4). No data permission or module on the panel: each notice type decides its own |

Organizing **my** Pulse requires no permission (they are personal preferences). There is no `pulse.view`: the home screen always
exists; if the user has no panel, they see the welcome.

Role templates: **Company admin** (TenantAdmin) all of them (the 5 from F8a and `pulse.attention`; the "Warehouse today" band uses `pulse.warehouse`, there is no new permission); **Warehouse operator** `pulse.warehouse`,
`pulse.indicators`, `pulse.charts`, `pulse.activity`; **Dispatcher**, **Billing** and **Read-only**
`pulse.indicators`, `pulse.charts`, `pulse.activity`; **Driver** none. Note: the Warehouse operator has
`pulse.activity` but not `analytics.view` (the panel's data), so they **do not see Recent activity** until they are given
`analytics.view`.

Roles that already existed before this batch: when the platform is updated (db-init), each company role with a template
name receives the `pulse.*` of its template, and each custom role receives `pulse.indicators`, `pulse.charts` and
`pulse.activity` if it had `analytics.view`, and `pulse.warehouse` if it had `inventory.view` (what it already saw in the Pulse). This
is done only once, in the update that creates the permissions; afterwards, what an administrator removes does not come back.

### Panel registry

| Key | Permission | Data permissions | Module | Default order |
|---|---|---|---|---|
| `INDICATORS` ("Your indicators") | `pulse.indicators` | (per element) | `ANALYTICS` | 20 |
| `CHARTS` ("Your charts") | `pulse.charts` | (per element) | `ANALYTICS` | 30 |
| `ATTENTION` (Needs your attention, Batch 14) | `pulse.attention` | (per notice type) | (per notice type) | 5 |
| `WAREHOUSE_DAY` (Warehouse today, Batch 15) | `pulse.warehouse` | `inventory.view` | `WMS_LOTSERIAL` | −10 |
| `WAREHOUSE` (Warehouse) | `pulse.warehouse` | `inventory.view` | `WMS_LOTSERIAL` | 40 |
| `ACTIVITY` (Recent activity) | `pulse.activity` | `analytics.view` | `ANALYTICS` | 50 |

Reserved for future batches (they do not appear yet): `ORDERS_RIVER` (10, "Packages on the street"), `COD_RIVER` (11,
"COD money coming back"), `DECISIONS` (15, "Needs your decision") and `RADAR` (60). The `ATTENTION` panel (Batch 14) does not replace
`DECISIONS`: it uses its own key because the owner calls it "Needs your attention"; with order 5 it sits above the indicators and charts. Since Batch 15, `WAREHOUSE_DAY` (the "Warehouse today" band, order −10) goes **first**, even in Pulses already organized (Organize saves the order as index × 10 from 0, so only a negative order keeps it on top). Default order: `WAREHOUSE_DAY`, `ATTENTION`, `INDICATORS`, `CHARTS`, `WAREHOUSE`, `ACTIVITY`. With the `ANALYTICS` module off the
Pulse can only show the Warehouse panels (the band and the Warehouse panel).

### Which indicators and charts each user sees

An indicator or chart appears in the Pulse (and in the Indicators and Charts lists) only if, **all at once**:

1. the user has `pulse.indicators` (or `pulse.charts`) — only for the Pulse;
2. they can see it by its visibility (their own private one, shared with them or their role, or company-wide) — this also applies to the “Default” ones since Batch 169;
3. **they can read its data source**: `STOCK_BALANCE`, `INVENTORY_TRANSACTION`, `PRODUCT`, `RECEIPT`, `WAREHOUSE`,
   `WAREHOUSE_TASK`, `PICK_BATCH`, `CYCLE_COUNT` → `inventory.view`; `TRANSPORT_ORDER` → `orders.view`; `TRIP` →
   `trips.view`; `VEHICLE`, `DRIVER`, `WORK_ORDER`, `FUEL_LOG`, `FLEET_DOCUMENT` → `fleet.view`; `CLIENT` → `clients.read`;
   `CONTRACT` → `contracts.read`; `LOCATION` → `locations.read`; `PURCHASE_ORDER` → `purchasing.view`; `AUDIT_LOG` and
   `SECURITY_EVENT` → `admin.audit`; `USER` → `admin.users`;
4. its **business module** is on for the company: Warehouse (`WAREHOUSE`) → `WMS_LOTSERIAL`; Operations
   (`OPERATIONS`) → `LTL_GROUND`; Accounting (`ACCOUNTING`) → `COD` or `LTL_GROUND`.

What does not meet 3 or 4 is not listed and, when requested by id (`GET …/indicators/{id}`, `…/value`, `…/charts/{id}/data`, "my range",
"my Pulse"), responds 404 "Indicator '…' not found." / "Chart '…' not found." (its existence is not revealed). Creating or
editing an indicator or chart on a source the user cannot read responds 404 "Data source '…' not
found.".

### Order and visibility at two levels

- **Panel**: the user's own row → the company row → registry (default order and visible). The origin travels in
  `source`: `user`, `company` or `default`.
- **Indicator or chart** within its panel: the personal preference (order and "show in Pulse", field by field) → the
  definition, which is the company level (`SortOrder` and "Show in Pulse" of the definition). `source` is `user` if the
  user has something of their own and `company` if not.
- List order: by the effective order and, on ties, by name.
- `hasPersonalLayout` = the user has some order or hidden item of their own (including the "Show in Pulse" switch of an
  indicator or chart). `canOrganizeCompany` = has `pulse.organize_company`.
- Hidden ones **come in the response** (with `isVisible: false`) so that "Organize" can show them again; they are not
  computed (value `null`, chart without points) and the screen does not draw them.

How it is used:
- `GET /api/v1/analytics/pulse` — `{ "indicators": [ { "id", "name", "value", "isMoney", "dateRangeMode", "fromUtc",
  "toUtc", "businessModule", "sortOrder", "isVisible", "source" } ], "charts": [ …same + "chartType", "points" ],
  "panels": [ { "key": "INDICATORS", "isVisible": true, "sortOrder": 20, "source": "default" } ], "hasPersonalLayout":
  false, "canOrganizeCompany": true }`. No permission or module of its own: any authenticated user.
- `PUT /api/v1/analytics/pulse/layout?scope=mine|company` — body `{ "items": [ { "kind": "indicator", "id": 12,
  "sortOrder": 10, "isVisible": true } ], "panels": [ { "key": "ACTIVITY", "sortOrder": 5, "isVisible": true } ] }`.
  Only what is sent is written (what is absent is not touched); repeating it changes nothing. `mine` saves my order; `company` requires
  `pulse.organize_company` and saves the company's (order and "Show in Pulse" of each definition and the company's panel
  rows). It returns the Pulse already updated.
- `DELETE /api/v1/analytics/pulse/layout/mine` — "Back to the company's": deletes my order and my hidden items (panels,
  indicators and charts); **keeps my date ranges**. 204.
- `PUT …/indicators/{id}/my-pulse` and `PUT …/charts/{id}/my-pulse` (already existed) remain as a single-element shortcut:
  they are equivalent to one item of `PUT …/layout?scope=mine` with only the visibility.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| `scope` absent or other than `mine` / `company` (field `scope`) | `Invalid scope: use mine or company.` | 400 |
| `panels[i].key` not in the registry | `Unknown Pulse panel: {key}.` (e.g. `Unknown Pulse panel: RADAR.`) | 400 |
| `items[i].kind` other than `indicator` / `chart` | `Invalid type: use indicator or chart.` | 400 |
| `scope=company` without `pulse.organize_company` | `Missing permission 'pulse.organize_company'.` (and `PERMISSION_DENIED` `SecurityEvent`) | 403 |
| Registry panel whose permission, data or module the user does not have | `Pulse panel '{key}' not found.` (not revealed) | 404 |
| Indicator or chart the user does not see or cannot read (or without `pulse.indicators` / `pulse.charts`) | `Indicator '{id}' not found.` / `Chart '{id}' not found.` | 404 |

First the shape of the whole body is validated (400) and then the scope (403/404); if anything fails, nothing is saved.

Status and audit: there is no status. Each panel order row (`PulsePanelSetting`, both the company's and
each user's) is recorded in the log (`AuditLog`, type `PULSE_PANEL_SETTING`: creation, change and, when going back to the company's,
deletion); the company order of an indicator or chart is recorded as a change to its definition (`INDICATOR_DEFINITION` /
`CHART_DEFINITION`). It generates no `SecurityEvent` except for the 403.

### Frequent cases

- **User who only sees warehouse**: role with `inventory.view`, `warehouse.*` and `pulse.warehouse`, `pulse.indicators`,
  `pulse.charts` (and `analytics.view` + `pulse.activity` if they must see Recent activity), without `orders.view`, `trips.*`,
  `cod.*`. Their Pulse shows the Warehouse panel and only the indicators and charts of warehouse sources; one about orders does not
  appear even if it is in the company Pulse.
- **Reordering the home for everyone**: whoever has `pulse.organize_company` uses "Organize the company's"; it is seen by
  everyone who does not have a Pulse of their own. Whoever already organized theirs keeps seeing theirs until they press "Back to the
  company's".

---

## 4. Needs your attention (Batch 14)

What it does: it is the top panel of the Day pulse. It lists what **needs a person to review it**. Today it has a single notice type:
a pending **movement ledger ↔ balance discrepancy** (chapter 06, section 3.3). Each row says "Discrepancy in {sku}" (or "in the total of {sku}"), the
product, where (warehouse, bin and lot, or "all positions"), the figures **Ledger**, **Balance** and **Difference** (balance − ledger,
signed) and since when it is pending, and carries the **Review** button. It shows the **5 oldest**; **View all (N)** leads to the
full list. With nothing pending it says **"All in order"** ("Nothing pending to review."). There is no notices table: each type computes
its pending items on read, so a resolved discrepancy disappears from the panel instantly. Later, Operations
and COD notices can be added with their own providers.

Who can: the **`pulse.attention`** permission (category PULSE) opens the panel. Also, **each notice type requires its own**: the
discrepancies, `inventory.view` with the **WMS_LOTSERIAL** module on. What the person cannot see does not add up (no 403: the panel is the
sum of what they see). Role templates: Company admin, Warehouse operator, Billing and Read-only include it; Dispatcher and Driver do not.
When the platform is updated, every company role that already had `inventory.view` received `pulse.attention` **once only**; what an
administrator removes afterwards does not come back. Like the other panels, it can be **hidden and moved** from "Organize my Pulse" and "Organize the
company's" (section 3).

How it is used: `GET /api/v1/analytics/attention` (permission `pulse.attention`; no module in the policy). Response:

```
{ "total": 7,
  "items": [ { "code": "INVENTORY_DISCREPANCY", "module": "WAREHOUSE", "tone": "danger", "count": 1,
               "params": { "publicId": "…", "kind": "BALANCE", "sku": "PROD-100", "productName": "…", "warehouse": "ALM-01",
                           "bin": "A01-R01-N1-P01", "lot": "", "where": "A01-R01-N1-P01", "ledgerQty": "10",
                           "balanceQty": "11", "difference": "1", "detectedAtUtc": "2026-09-30T13:41:02Z", "checkCount": "1" },
               "route": "/warehouse/kardex", "query": { "tab": "reconciliation", "discrepancy": "…" },
               "sinceUtc": "2026-09-30T13:41:02Z" } ],
  "groups": [ { "code": "INVENTORY_DISCREPANCY", "module": "WAREHOUSE", "total": 7, "route": "/warehouse/kardex",
                "query": { "tab": "reconciliation", "status": "OPEN" } } ] }
```

`total` is the sum of pending items of all the types the person can see ("View all (N)"); `items`, the 5 oldest; `groups`, the
total and the "View all" route per type. Numbers go as strings with a decimal point and dates in ISO 8601 UTC; the web app builds the text
in the person's language. "Review" opens the movement ledger on the **Reconciliation** tab with that discrepancy open
(`/warehouse/kardex?tab=reconciliation&discrepancy=<publicId>`).

### Open counts (Warehouse panel)

The **Open counts** card of the Warehouse panel (chapter F7A) now counts the **Pending and Counted** cycle counts with the
**real total**: it uses `GET /api/v1/cycle-counts/page?status=OPEN&status=COUNTED&take=1` and reads `total`. Before, it counted the length of the
`GET /api/v1/cycle-counts` list, which is cut at 200, so with many counts ("what changed" creates one per bin) the figure
stayed at 200. Matched and Variance counts no longer count as open.

### Validations

| Case | Exact message | HTTP |
|---|---|---|
| `GET /api/v1/analytics/attention` without the `pulse.attention` permission | `Missing permission 'pulse.attention'.` | 403 |
| No session | (no application body) | 401 |

There is no other validation: the panel is read-only. A notice type whose module is off or whose permission is missing simply does not appear.

### Frequent cases

- **I don't see "Needs your attention".** You are missing `pulse.attention` (ask your administrator) or the administrator hid the panel in the company
  Pulse or in yours ("Organize"). If you have the panel but not `inventory.view`, or the WMS_LOTSERIAL module is off, the panel
  always shows "All in order": discrepancies do not add up.
- **It says "All in order" but I know there is a discrepancy.** The automatic review takes a few seconds after each movement; the panel
  is requested again every time the Pulse is opened. If the discrepancy was caused directly in the database, press **Run
  reconciliation** in Movement ledger › Reconciliation.
- **The operator cannot resolve a discrepancy.** They see the row and **Review**, but **Fix** and **Dismiss** require `inventory.adjust`,
  which the Warehouse operator does not have in their template.

---

### 4.x Sales order with backorder ready to fill (Batch 67)

Code `SALES_BACKORDER_READY`. For whoever has `sales.backorder` with the Sales orders module on. One row per open order with a backorder that can now be
filled, the longest waiting first. Parameters: `publicId`, `number`, `client`, `warehouse`, `lines`, `units` (fillable) and `days` (of the line that has waited the longest). Tone
`warn`, or `danger` if it has been more than 7 days. “Review” → `/sales-orders?order=<publicId>`; “View all” → `/sales-orders`. Details in [18 §16](18-ordenes-de-venta.md#16-sales-order-analytics-batch-67).

## 5. Day pulse (Batch 15)

This batch reorders the Pulse: it puts a band of Warehouse numbers on top, keeps the top rows fixed when scrolling, groups the
indicators in one row per module, **always** draws the charts as a chart and turns two Warehouse charts into company
charts that can be edited. Also, **all analytics now count days in Puerto Rico time** (section 5.6). The screens
are in [F7A — Pulse: Warehouse today](frontend/f7a-pulso-almacen-y-actividad.md).

### 5.1 "Warehouse today · last 7 days" band

What it does: it is the row of four cards just below the date. It shows what happened **today** in the warehouse and how the
last 7 days are going (today included). It is the `WAREHOUSE_DAY` panel, with order −10: it always comes first, also in Pulses someone already
organized.

Who can: the **`pulse.warehouse`** permission with **`inventory.view`** and the **`WMS_LOTSERIAL`** module on (the same as the Warehouse
panel; there is no new permission). The Company admin and the Warehouse operator have them. The endpoint that feeds it requires `inventory.view`.

How it is used: on the home screen there is nothing to configure. Through the API:
`GET /api/v1/inventory/pulse/days?warehousePublicIds=<guid>&days=7` (`warehousePublicIds` can be repeated; without it, all warehouses;
`days` is 1 to 14 and defaults to 7; the screen always requests 7). Abbreviated response:

```
{ "timeZone": "America/Puerto_Rico", "today": "2026-09-30",
  "fromUtc": "2026-09-24T04:00:00Z", "toUtc": "2026-10-01T04:00:00Z",
  "days": [ { "date": "2026-09-24", "receivedUnits": 0, "receivedMovements": 0, "outboundUnits": 0,
              "outboundMovements": 0, "countsWithVariance": 0 },
            … 7 rows, from the oldest to today … ],
  "receivedToday": 12, "receivedTotal": 72, "outboundToday": 5, "outboundTotal": 11,
  "countsWithVarianceToday": 1, "countsWithVarianceTotal": 3, "belowMinProducts": 1,
  "countsAlert": true, "belowMinAlert": true }
```

As many rows as `days` always come: a day without movement brings zeros.

**What each card counts**

| Card | Big number | Small text | Small bars | What it counts | Orange |
|---|---|---|---|---|---|
| Units received | Today's | "7 days: N" | 7 | Receivings (`RECEIPT`) plus receipt variance adjustments (`RECEIPT_VARIANCE`): what **really arrived**. If 10 were expected and 8 arrived, it counts 8 | Never |
| Units out | Today's | "7 days: N" | 7 | Picking (`ISSUE`) plus cross-dock (`CROSSDOCK`). **Deleting a pick batch subtracts** those units on the day it is deleted. It does not count damage, losses, expired, counts or transfers | Never |
| Counts with variance | Cycle counts closed today in status **Variance** | "7 days: N" | 7 | Active counts in `RECONCILED_VARIANCE`, by the day they were closed | If there was any today |
| Transfers and adjustments | Today's movements: transfers + adjustments | "Transfers N · Adjustments N" (7 days) | 7 stacked bars (one segment per type) | `TRANSFER` and `ADJUSTMENT` movements of the movement ledger per local day, all of them (system ones too), by the movement's warehouse | Never |
| Products below minimum | Those below their minimum **right now** | “N products on backorder” (products with a pending shortage on some open line of a sales order) | None | The same query as the Warehouse panel (`GET /products?belowMin=true`): the figure is identical | If there is any |

- Inventory goes down when **picking**, not when the truck is dispatched: that is why "out" counts pick batches (chapter 06, section 7).
- There are 7 small bars, one per day; the one on the right is **today** and grows during the day. A day without movement is an empty bar (a
  line). Hovering over a bar shows the full date and the quantity ("Today, Wednesday, September 30, 2026 · Units: 72").
  The bars only display: they are not clicked.
- The 7 days are **Puerto Rico days**: each one starts at 00:00 Puerto Rico time (04:00 UTC). A receipt made at 23:59 Puerto Rico time
  counts on that day, not the next.
- The card turns **orange** (border and number, and today's bar in Counts) according to the `countsAlert` and `belowMinAlert` flags
  sent by the server. The band is purple the rest of the time.
- A day's outbound figure can come out **negative** if on that day only pick batches were deleted (the reversal subtracts).
- While loading, each number says "…". If the query fails (for example, a 403 when switching company), it says "—" and does not take the
  user out of the Pulse. The band is requested again every 5 minutes, so it changes day at midnight, and refreshes on its own when
  inventory movements are made in the session.

**Where the click leads** (the whole card is a link; with a warehouse chosen, `warehousePublicIds` is added)

| Card | Screen | Filters it carries |
|---|---|---|
| Units received | Movement ledger (`/warehouse/kardex`) | Type Receiving, and From and To = the 7 days (from 6 days ago to today) |
| Units out | Movement ledger | Types Dispatch and Cross-dock, and the 7 days |
| Counts with variance | Cycle count (`/warehouse/cycle-counts`) | Status Variance (no dates: see FAQ) |
| Products below minimum | Products & inventory (`/warehouse/products`) | "Below minimum" box checked |

Cycle count and Products & inventory read those filters from the address once, when opened. Watch out for the movement ledger sum: see the FAQ.

**Warehouse of the band.** A small selector next to the title ("All warehouses" or the warehouse **code**; the name appears on
hover). It is the **same** warehouse as the "Warehouse" panel further down: changing it in one changes it in the other, and it is remembered per user in
that browser. The four cards are computed with it.

**Validations**

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| `days` | Integer from 1 to 14 | `Days must be between 1 and 14.` (comes in `errors.days`) | 400 |
| Without `inventory.view` | The controller policy rejects it | (no application body) | 403 |
| `WMS_LOTSERIAL` module off | The class module | `The 'WMS_LOTSERIAL' module is not enabled for this company.` | 403 |
| No session | — | (no application body) | 401 |
| `warehousePublicIds` with a warehouse that does not exist or belongs to another company | Not an error: everything comes as zero (it never falls back to "all") | — | 200 |

**Statuses and transitions.** The band has no statuses of its own and changes no status: it only reads. What moves each card:

| Card | What moves it | Who | What it validates | Effect | What it blocks |
|---|---|---|---|---|---|
| Units received | Confirming a receipt (chapter 06, section 4) | Whoever confirms the receipt | The receipt's rules | A `RECEIPT` movement (and a `RECEIPT_VARIANCE` adjustment if there is a variance) | Nothing |
| Units out | Creating a pick batch or a cross-dock; deleting a pick batch subtracts | Whoever picks or deletes | The pick batch's rules | An `ISSUE` or `CROSSDOCK` movement; on deletion, a `PICK_BATCH_REVERSAL` adjustment | Nothing |
| Counts with variance | Confirming a cycle count that posts some movement: Pending or Counted → **Variance** (chapter 06, section 6) | Whoever has `warehouse.count` | The count's rules | Status `RECONCILED_VARIANCE` with its closing date | A Variance count can no longer be edited |
| Products below minimum | Any movement that changes the available quantity, or changing a product's minimum | Depending on the action | — | — | Nothing |

### 5.2 Header pinned while scrolling

What it does: as you scroll down the Pulse, **only the header stays pinned at the top**: the date with the "Organize my Pulse" and "Organize the
company's" buttons, the greeting ("Welcome…") and the "Company Pulse" chip. **Everything else, including the "Warehouse today" band, scrolls**: no section stays pinned (owner's decision). This only happens on the home screen; Indicators and Charts do not change.

| Situation | What stays pinned |
|---|---|
| Normally | The header: date with buttons, greeting and chip |
| While organizing (Organize mode open) | None of this: the Organize bar is already pinned |
| Phone (less than 720 px wide) | The header, compact: the title and the small buttons on one wrapping line |
| Phone in landscape (less than 560 px tall) | Nothing: the header scrolls so it does not take up the screen |

(Before the change of 2026-10-05 the "Warehouse today" band also stayed pinned below the date, and Organize carried a note explaining it.
That note no longer exists.)

### 5.3 Indicators in one row per module

"Your indicators" now shows **one row per business module**, each with its label, in the order of the menu: **Operations**,
**Warehouse** and **Accounting** (the latter only if there is some indicator of that module, for example "COD to collect"). Within each row the order
is the usual one. "Changes recorded", "Failed logins" and "Active users" are marked as Operations and appear in that row. The
small label of each card shows only the range ("Last 7 days"), because the row already says the module.

In **Organize** the indicators are listed by row and **only move within their row** (with the note "Indicators are shown in
one row per module; here they are ordered within their row."). On save, the order is saved per row.

### 5.4 Charts are always drawn (and "Others")

- **Always a chart.** Before, a chart with 3 points or fewer was shown as a list of "label · value". Now it is **always** a
  chart, no matter how many points it has: a donut with a single slice, a single bar and a line with a single day are drawn (a line with 12
  points or fewer draws every point). Only a chart **with no points at all** shows the notice: "This chart has no data in the configured
  range." on the Pulse, and "This chart has no data with the current filters and range." in Analytics → Charts.
- **Tooltip and accessibility.** Hovering shows the value and, if the chart groups by day, the full date ("Wednesday, September 30,
  2026"; the axis shows it short, "Sep 30"). Each chart carries a description with its values for screen readers.
- **Donut.** Shows the total in the center; if the total is long, in compact form ("$1.2M").
- **2 per row.** In "Your charts" there are never more than 2 charts per row (on a phone, one below the other). Analytics → Charts does not change.
- **"Others".** In bars, donut and pie charts that **sum or count** (SUM or COUNT), if there are more than 8 groups the **7 largest** are shown plus a
  last point **"Others"** with the exact sum of the rest; the sum of the points is the total. With average, minimum or
  maximum nothing is merged: the 8 largest remain. **Note:** this also changes the bar charts that already existed with more than 8 groups (for example
  "Changes by user"): before, they showed the 8 largest. The chart editor's preview does the same calculation.

### 5.5 The company's 2 warehouse charts

What it does: two charts that already existed out of the box ("Inventory value by category" and "Inventory movements by type") stop
being system charts and become **company charts**: without an owner, editable and deletable by whoever has the permission, and seeded in
**all** companies (also in the ones already created, with the database update).

| | Inventory value by category | Inventory movements by type |
|---|---|---|
| Type | Donut with the total in the center | Vertical bars |
| Source | `STOCK_BALANCE` | `INVENTORY_TRANSACTION` |
| Measure | Sum of `CostValue` ("Value at cost": stock on hand at purchase cost), in money | Sum of **`Units`** (units moved, always positive) |
| Grouped by | Product category: 7 largest plus "Others" | Movement type (Receiving, Dispatch, Transfer, Adjustment, Cross-dock) |
| Period | None: it is the snapshot of now (no "Range" button) | Last 7 days; each user changes it with **Range** (there is no 5-day option) |
| Module and visibility | Warehouse, whole company | Warehouse, whole company |
| Position in "Your charts" | First row, left (order 1) | First row, right (order 2) |

- **Who edits or deletes them:** whoever has the `analytics.manage` permission ("Create views, indicators and charts"; today the company
  administrator). In Analytics → Charts they carry the **"Company"** chip and, with the permission, the Edit and Delete buttons. Whoever has
  `analytics.dates` also changes their default range.
- **If deleted, they do not come back.** When deleting one, the screen warns: "Delete chart {name}? It belongs to the company: once deleted it
  is not created again." Neither renaming nor deleting it recreates it in a future update. Hiding them (with Organize or with "Show in Pulse")
  leaves them hidden like any other.
- **Position.** In a company that **already organized** its charts (some active chart with order 0) its order is respected and they are not moved to the
  first row.
- **Companies already created.** Only the factory chart that is still **exactly** as seeded (same name, source, field and
  type) is converted. One the company already touched is not modified.
- **The other duplicate charts are not touched** ("Movements by type" as a 30-day donut stays as it was).

| Case | Exact message | HTTP |
|---|---|---|
| Edit or delete (`PUT` or `DELETE /api/v1/analytics/charts/{id}`) without `analytics.manage` | (no application body: the controller's policy rejects it) | 403 |
| Edit or delete a **system** chart | `The platform's default items cannot be edited or deleted.` | 403 |
| Edit or delete a chart that **has another owner** | `Only the owner can edit or delete this item.` | 403 |
| Chart type other than bar, donut or line (the `PIE` pie chart is not enabled) | `Chart type: BAR, DONUT or LINE.` (field `chartType`) | 400 |
| Create a chart with a name already used by an active one | `Chart '{name}' already exists.` | 409 |
| A chart the user does not see or cannot read | `Chart '{id}' not found.` | 404 |

### 5.6 "Today" and the days, in Puerto Rico time

Since Batch 15, everything that depends on the day uses Puerto Rico time (UTC−4, no daylight saving), not the UTC day. The day starts at
00:00 Puerto Rico time (04:00 UTC). **What changed in behavior:**

| Where | What happens now |
|---|---|
| Ranges of indicators, charts, views and exports (**Last 7 days, Last 30 days, Current month and Custom range**) | They are Puerto Rico days. "Last 7 days" = today and the 6 previous ones, from 00:00 local time of the first day to 00:00 local time of tomorrow. A movement between 20:00 and 24:00 Puerto Rico time counts in its local day (before, it counted in the next day) |
| Charts grouped by date | One point per Puerto Rico day. In "Movements by day", the label is `2026-09-30` |
| `Date` field of the movements source | It is the Puerto Rico day and comes out as `"2026-09-30"` (before, `"2026-09-30T00:00:00"` in UTC) |
| Routes and contracts | Their calendar day is compared against the local range |
| Expirations (fleet documents, drivers, current contracts) | "Today" is Puerto Rico's |
| Recent activity, **Today** window | From 00:00 Puerto Rico time |
| "Warehouse today" band | Its 7 days are the same local days, so the band's sum matches the "last 7 days" indicator and a per-day chart |

**Still in UTC (outside this batch):** the minimum and maximum date validation of custom fields, the "today" of the client record
and of the driver's documents and rates. A value on those screens may differ by hours from an indicator or chart. The time zone per
company is left for a later batch (today it is Puerto Rico's for all).

### 5.7 "Pending discrepancies" indicator

Counts the ledger ↔ balance discrepancies in status **Pending** (`OPEN`, chapter 06, section 3.3), in Warehouse, with no date range (it is the
state of now). It is a system indicator and **comes turned off on the Pulse**, because "Needs your attention" (section 4) already shows them.
It appears in Analytics → Indicators; it can be turned on with "Show in the day's Pulse". It is seen by whoever can read the data source
(`inventory.view` with `WMS_LOTSERIAL`).

---

## 6. The two currents of the Pulse (Batch 88, P12)

Below “Needs your attention” the Pulse brings the **two currents** of the mockup: **Packages on the street** (blue, the flow of orders) and **COD money** (orange, the flow of money). Each one is a **panel** of the Pulse (`ORDERS_RIVER`, order 10, and `COD_RIVER`, order 11) with its permission, its data permission and its module, and it can be ordered or hidden with “Organize” like the others:

| Panel | Panel permission | Data | Module |
|---|---|---|---|
| Packages on the street | `pulse.orders` | `orders.view` | `LTL_GROUND` |
| COD money | `pulse.cod` | `cod.view` | `COD` |

`pulse.orders` comes with Dispatcher, Billing and Read-only; `pulse.cod`, with Billing and Read-only; the company administrator, both. They are propagated automatically to roles already created (section 2026-10-17 (z) of `update.sql`).

They are **snapshots of now** (the 7-day mini bars come from the daily history, see §8): each card counts what there is at that point. They are requested again every minute. Endpoints: `GET /api/v1/pulse/rivers/orders` (`orders.view`, module `LTL_GROUND`) and `GET /api/v1/pulse/rivers/cod` (`cod.view`, module `COD`).

### Packages on the street

Five cards, **in this order** (dictated by Luis, 2026-10-21):

| Card | What it counts | Small text | On click |
|---|---|---|---|
| **Inbound** | shipments created today (excluding cancelled and deactivated), with the 7-day mini bars | “new shipments today · N waiting” (N = Submitted and Pickup, of any date) | Shipments |
| **In warehouse** | the shipments that **entered the warehouse** (by pickup or by the warehouse scan): **each shipment only once, on the day it arrived for the first time**; with the 7-day mini bars. In a healthy flow it matches Inbound | “entered the warehouse today” | Shipments |
| **Routes** | the **routes that left** that day (dispatched, in progress or completed, by their plan date), with the 7-day mini bars | “left today” | Dispatch room |
| **Delivered** | shipments delivered **that day** (by the status history), with the mini bars of **all** 7 days | “today” | Shipments |
| **On route** (what is happening right now) | shipments In transit, with the drivers of the dispatched or in-progress routes; **below and smaller**, the shipments **completed** (delivered) today | “N drivers” and “N shipments completed today” | Route monitor |

The “To dispatch” card is no longer in the river (assigned shipments without a driver are still reported in “Needs your attention” and in the Radar). The daily indicator snapshots keep saving the current stock (in warehouse and to dispatch).

### COD money

| Card | What it counts | Small text | On click |
|---|---|---|---|
| **To collect** | **Delivered** orders with COD that have not been fully collected | “orders · $X short” | COD (Shipments with COD) |
| **Collected** | money collected on the street **not reconciled** | “cash $X · checks $Y” (and “others $Z” if there is ATH Móvil or card) | COD → Reconcile |
| **Reconciled** | reconciled money that is still **not in a remittance** | “no remittance · N clients” | COD → Remittances |
| **Remittance ready** | the **net** of the open, reconciled or approved remittances (not yet remitted) | “N remittances · N clients” | COD → Remittances |

The money sums assume **a single currency** (the company's). A **cancelled or deactivated** order does not count in any card. If the server does not respond, the section shows the error instead of the cards.

### Frequent cases

- **I don't see the currents:** you need the panel permission (`pulse.orders` / `pulse.cod`), the data permission (`orders.view` / `cod.view`) and the module turned on; if the Pulse was already organized (personal or the company's), the new panel appears in its default order (10 and 11) until it is reorganized.
- **“In warehouse” does not match “Inbound”:** it counts the shipments that **arrived** at the warehouse today (picked up or scanned in the warehouse), not the ones that were there before; a shipment that goes through pickup and then through the warehouse counts **only once**. If the flow is going well they should be similar; if Inbound is much larger, there are submitted shipments nobody has picked up or scanned yet.
- **“Delivered” has no bars or they are almost empty:** the bars come from the status history. In shipments **migrated from Colavora** the delivery date is that of their last activity in Colavora (no longer the day of the migration); if a shipment does not carry that date, it does not appear in the bar of the day it should.
- **“Delivered” does not match Orders:** it counts the deliveries that happened **today** (company time), not all delivered ones.

## 7. Radar of open shipments and operations notices (Batch 89, P12)

### The Radar

Pulse panel `RADAR` (order 60) with the same permissions as the orders river: `pulse.orders`, `orders.view` and the `LTL_GROUND` module. It counts the **open** orders —Submitted, Pickup, Unassigned, Assigned, In transit or with Failed attempt (since Batch 106 the Submitted ones count; before, the draft did not)— and **not** the delivered, cancelled, In warehouse or deactivated ones (“what's delivered doesn't get in the way here”).

- **How many are open** and, without a search, the **groups with the most orders** (up to 6) with the COD they carry: **by town** of the consignee (the delivery stop) or **by client** (the “Group by” selector). A group opens **Orders already filtered** (the town or the client and only the open statuses); **See all in Shipments** opens the list with only what is open.
- **Search box:** from 2 letters it searches among what is open by **invoice, shipment number, package or consignee** (the same free-text search as Orders) and shows up to 6 with their status and COD; a click opens the order.
- Endpoint: `GET /api/v1/pulse/radar?groupBy=TOWN|CLIENT&take=6` (`orders.view`, module `LTL_GROUND`; invalid `groupBy` → 400 “Group by town (TOWN) or by client (CLIENT).”; `take` from 1 to 20).
- **Orders** accepts the filters of the link: `/orders?city=…`, `/orders?client=<publicId>` and `/orders?statuses=PICKUP,IN_TRANSIT…`.

### New notices in “Needs your attention”

They are **aggregated** rows (a single row with the count, as in the mockup) and go **before** the per-record rows (rentals, discrepancies…), so that a long list of rentals does not hide them:

| Notice | What it says | Who sees it | “Review” |
|---|---|---|---|
| **Shipments without a driver** (`ORDERS_NO_DRIVER`) | “N orders without a driver”: Unassigned or Assigned **without a route with a driver**; the figure “The oldest” (days); red from 2 days | `trips.dispatch` and module `LTL_GROUND` | Dispatch room |
| **COD not reconciled** (`COD_UNRECONCILED`) | “$X of COD not reconciled”: what was collected on the street that is not yet reconciled, with the collections, the orders and “The oldest”; red from 3 days | `cod.reconcile` and module `COD` | COD → Reconcile |

### SLA at risk (Batch 122, P12)

With the proof of delivery (Batch 118) there is now a real delivery time, so the SLA is measured. The **SLA** of an order is the service level of its **contract** for its **service type** (the client's SLA tab: “maximum hours” of transit).

- **When the clock runs:** from when the order is **picked up** (the first step into “Pickup”; if there is none, from when it was confirmed) until it is **delivered**. An order not yet picked up, cancelled, returned, in warehouse (pick up) or of a service without maximum hours **has no SLA**.
- **States:** **On time** (`ON_TRACK`); **At risk** (`AT_RISK`: less than 25 % of the term remains, and at least one hour of margin); **Overdue** (`BREACHED`: the term has passed and it is still open); **Met** (`MET`: delivered within the term); **Out of SLA** (`MISSED`: delivered afterwards); **No SLA** (`NO_SLA`).
- **“SLA at risk” notice** (`SLA_AT_RISK`) in “Needs your attention”: an aggregated row “N orders at risk of missing their SLA” with “At risk”, “Overdue” and “The tightest” (how much it has left or how long it has been overdue); red if any is overdue, yellow if not. It is seen by those with `orders.view` and the `LTL_GROUND` module; “Review” opens Orders. It only counts open orders already picked up (cap of 2,000 per read).
- **Fields for views, indicators and charts** (source `TRANSPORT_ORDER`): `SlaState`, `SlaMaxHours`, `SlaDueUtc` and `SlaHoursLeft`. For example, filter `SlaState = BREACHED` or group by `SlaState` to see compliance.
- **No notice** for an order delivered late: it is already past; it shows as “Out of SLA” in the fields.

### Operational alerts and SLA as an indicator (Batch 125, P12)

Four more aggregated notices in “Needs your attention” (one row each, with the count; each person sees their own by permission and module):

| Notice | What it says | Who sees it | “Review” |
|---|---|---|---|
| **Failed attempt** (`ORDERS_FAILED`) | “N orders in failed attempt” with “The oldest” (days since it was left in Failed attempt); red if over 2 days | `orders.view` and module `LTL_GROUND` | Orders filtered by Failed attempt |
| **Open claims** (`CLAIMS_OPEN`) | “N open claims” (unresolved) with the oldest and the declared value; red if over 7 days | `claims.view` and module `LTL_GROUND` | Claims |
| **Fleet documents** (`FLEET_DOCS_DUE`) | “N fleet documents expired or about to expire” (those of the “Expiring documents” panel, 30-day window) with expired, about to expire and how long until the first one; red if any is expired | `fleet.view` and Catalogs module | Fleet |
| **Overdue invoices** (`INVOICES_OVERDUE`) | “N overdue invoices” with “The most overdue” (days); always red | `billing.view` and Billing module | Billing |

If there is nothing to report, the row does not appear. **SLA indicators and chart** (seeded in every company; turned on from “Organize”): **Shipments with SLA at risk** (on the Pulse by default), **Deliveries within SLA** (percentage of those delivered in the last 30 days with SLA that arrived within their contract's term; field `SlaMetPct`: 100 if met, 0 if not) and the chart **Shipments by SLA state** (last 30 days).

### Frequent cases

- **The Radar does not show my order:** it only counts what is open; a delivered, cancelled or In warehouse order does not show. Look for it in Orders.
- **A town appears twice:** the town is the one saved in the delivery stop of each order; “Ponce” and “PONCE” are grouped together in SQL Server, but “Ponce Playa” is a different one.
- **I don't see the orders-without-driver notice:** you need `trips.dispatch`; and it is only shown if there are Unassigned or Assigned orders without a route with a driver.

## 8. Daily indicator snapshots (Batch 90, P12)

Every night the scheduled job **`KPI_SNAPSHOT`** (“Daily indicator snapshot”, at **23:55** company time, active by default) saves in the `KpiDailySnapshot` table the day's figures, one row per day and indicator. That way the Pulse currents and the charts have **history**.

| Key (`KpiKey`) | What it saves | Type | Module |
|---|---|---|---|
| `ORDERS_CREATED` | new orders of the day | count | `LTL_GROUND` |
| `ORDERS_IN_WAREHOUSE` | orders in warehouse | count | `LTL_GROUND` |
| `ORDERS_UNASSIGNED` | orders without a driver | count | `LTL_GROUND` |
| `ORDERS_ON_ROUTE` | orders on route | count | `LTL_GROUND` |
| `ORDERS_DELIVERED` | orders delivered in the day | count | `LTL_GROUND` |
| `ORDERS_OPEN` | open orders (Radar) | count | `LTL_GROUND` |
| `COD_TO_COLLECT` | COD to collect (amount) | money | `COD` |
| `COD_COLLECTED` | COD collected, not reconciled | money | `COD` |
| `COD_RECONCILED` | COD reconciled, no remittance | money | `COD` |
| `COD_REMITTANCE_READY` | net of remittances ready | money | `COD` |

- **Only the indicators of the modules turned on are saved.** With none turned on, the job reports “No module with indicators is turned on; nothing was saved.”
- **One row per day and indicator:** “Run now” (System → Scheduled jobs) repeats the snapshot and **replaces** today's; it can be run as many times as needed.
- **Mini bars of the currents:** In warehouse, To dispatch and On route (orders) and the four nodes of COD money (in orange) draw the **6 previous days from the snapshots** and the last one with the value of now. A day without a snapshot counts 0. If there is no snapshot at all and today is 0, nothing is drawn.
- **Data source `KPI_DAILY`** (“Daily indicator history”): fields Day, Key, Indicator, Type and Value; it is used in indicators and charts with a date range. It is read with `analytics.view`.
- No new permissions or new endpoints: the currents return the mini bars in the `spark` field they already had.

### Frequent cases

- **The mini bars come out empty:** the job runs at 23:55; on the first day there is no history yet. To test, “Run now”.
- **A day is missing from the chart:** the server was off at 23:55 or the job is disabled; that day counts 0.
- **I changed modules:** the indicators of the module turned off stop being saved; what was already saved is kept.

## 9. COD, billing and sales indicators and charts; Operations and Accounting activity (Batch 91, P12)

### New data sources

| Source (`DataSourceKey`) | One row per | Period date | Read permission | Module |
|---|---|---|---|---|
| `COD_COLLECTION` (“COD collections”) | COD collection | `CollectedAtUtc` | `cod.view` | `COD` |
| `INVOICE` (“Invoices”) | client invoice | `IssueDate` (company day; drafts have no date and are left out when there is a range) | `billing.view` | `BILLING` |

Fields of `COD_COLLECTION`: order, client invoice, client, method, status, amount collected, collected on, reconciled on, driver, check number. Fields of `INVOICE`: number, client, status, with balance (`IsOpen` = Issued or Overdue), issue and due date, days overdue, subtotal, tax, total, paid (non-voided payments) and balance. They can be used in your own views, indicators and charts like any other source.

### Factory indicators (system)

| Indicator | What it sums or counts | Range | On the Pulse |
|---|---|---|---|
| COD collected, not reconciled | money collected in status Collected | now | Yes |
| COD reconciled, no remittance | money in status Reconciled | now | No |
| COD collected (30 days) | non-voided collections of the last 30 days | 30 days | No |
| Invoices receivable | balance of the Issued and Overdue invoices | now | Yes |
| Overdue invoices | invoices in status Overdue | now | Yes |
| Invoiced (30 days) | total of the issued, paid or overdue invoices | 30 days | No |
| Sales (30 days) | total of the sales orders that are neither draft nor cancelled | 30 days | No |

### Factory charts (system)

| Chart | Type | Range | On the Pulse |
|---|---|---|---|
| COD collections by method | donut (amount) | 30 days | No |
| COD collected by day | line (amount) | 30 days | Yes |
| Invoices by status | donut | 30 days | Yes |
| Invoiced by day | line (amount) | 30 days | No |
| Sales by day | line (amount) | 30 days | No |

The COD and billing ones belong to **Accounting**; the sales ones, to **Warehouse**. They are hidden when their module is off (COD, Billing, Sales orders). They are seeded automatically in every company when the database is initialized (`db-init`); each user turns on or hides the ones they want with “Organize”. The sums assume a single currency (the company's).

### Recent activity: Operations and Accounting tabs

Besides Warehouse, the “Recent activity” panel brings two tabs. Each one is seen with its permission and its module; a user with `billing.view` and `cod.view` sees in Accounting the events of both.

| Tab | Permission | Module | Event | Mandatory | Origin |
|---|---|---|---|---|---|
| Operations | `orders.view` | `LTL_GROUND` | Order delivered / on hold | no | order status history |
| | | | Order failed / cancelled | yes | order status history |
| | | | Route dispatched / completed | no | route status history |
| | | | Route cancelled | yes | route status history |
| Accounting | `billing.view` | `BILLING` | Invoice issued / paid, payment recorded | no | invoice history and payments table |
| | | | Invoice overdue / voided, payment voided, run approved | yes | invoice history, payments and run |
| Accounting | `cod.view` | `COD` | COD collection recorded / reconciled | no | collections table (one event per collection) |
| | | | COD remittance approved / remitted | yes | remittance history |

The reference is the order number, the route code, the invoice or remittance number (the run carries its period); the detail, the client and the amount. On click: orders and collections → the order; routes → Dispatch room with the route; invoices and runs → Billing; remittances → COD → Remittances. The catalog (`ActivityEventType`, section (ab) of `update.sql`) allows renaming or turning off the optional ones per company; the mandatory ones are always shown.

### Frequent cases

- **I don't see the Accounting tab:** you need `billing.view` with Billing turned on, or `cod.view` with COD turned on.
- **I don't see a COD or invoice indicator or chart:** the module of its source is off, or the user hid it in “Organize”.
- **“Invoiced by day” does not count the draft:** it only sums issued, paid or overdue invoices (with an issue date).

### Who sees the “Default” ones and how to change it (Batch 169, 2026-10-21)

Indicators and charts belong to **each company**. The “Default” ones (those the system creates in each company) only mean **that the system created them**: they are born visible to **the whole company**, but the company decides who sees them, just like with its own.

- **Where:** Indicators and Charts → on the card of a “Default” one, **“Who sees it”** button (only with `analytics.manage`). Choose **Private**, **Whole tenant** or **Shared** (with people and/or roles) and save. API: `PUT /api/v1/analytics/indicators/{id}/visibility`, `…/charts/{id}/visibility` and `…/reports/{id}/visibility` (body `{ "visibility": "PRIVATE|TENANT|SHARED", "shares": [{ "userId": 5 }, { "roleId": 3 }] }`; `shares` only counts with `SHARED`).
- **What is NOT changed:** name, source, field, function and filters of a “Default” one (they are the official metrics); nor is it deleted (`403` “The platform's default items cannot be edited or deleted.”). For a different version, create your own.
- **Whoever manages Analytics (`analytics.manage`) always sees** the “Default” ones and the company's ownerless ones, even if they are private or shared with others, so they can be put back to “Whole tenant”. The platform administrator sees everything. A user without access gets `404` when opening it and it does not show in Indicators, Charts or Pulse.
- **The read rule is kept:** besides the visibility, you need to be able to read the source and have the module turned on.
- The default range and “Show in Pulse” remain each user's preference (with the definition's value as the starting point).

| Case | Exact message | HTTP |
|---|---|---|
| Visibility that is not PRIVATE, TENANT or SHARED | `Visibility must be PRIVATE, TENANT or SHARED.` (field `visibility`) | 400 |
| Person who is not from the company | `User {id} does not belong to this company.` (field `shares`) | 400 |
| Role that is not from the company | `Role {id} does not belong to this company.` (field `shares`) | 400 |
| Without `analytics.manage` | (the controller's policy rejects it) | 403 |
| Item of another user (not “Default”) | `Only the owner can edit or delete this item.` | 403 |

## Frequently asked questions of this chapter

Those of Batch 15 ("Warehouse today" band, pinned rows, company charts, "Others" and Puerto Rico days) are in [faq.md](faq.md), section "Batch 15".

See [faq.md](faq.md), section "Batch 7A — Warehouse Pulse and Recent activity", for the detail of each message
(what it means and what to do), including why a `FOUND` adjustment appears twice, what happens when renaming or
disabling an event of the list from the tenant override, why deactivating a category does not appear as
"Product deactivated", and why someone without `inventory.view` can see cycle counts in Analytics (decision to
review, not a defect of this chapter). Those of the Pulse by panels (section 3) are in the section "Batch F8a — Day
pulse: panels, permissions and order" of the FAQ: why I don't see the Warehouse panel, why my Pulse does not look like another
user's, how I go back to the company Pulse and why an indicator marked for the Pulse does not show for a user.
