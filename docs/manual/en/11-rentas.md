# 11 — Rentals (Warehouse submodule)

Rentals lets you **rent your own serial-tracked units** (hospital beds, oxygen concentrators, wheelchairs…) to a
**client location**, with its contract, its pickup date (end of the rental) and its billing conditions. This chapter covers
**block R1 (Batch 27)**: the rental from creation until it is dispatched, the **extensions** of the pickup date and the
**cancellation**; and **block R2 (Batch 28)**: the **rental return** (with its reason, total or partial, at the end or early), the
**configurable processing** of the returned unit (inspection, cleaning, testing, repair… up to "Ready" or "Scrapped") and the units
on rent in the **cycle count** (D7); and **block R3 (Batch 29)**: the **reports, indicators and the chart** of rentals in Analytics and
the **overdue or about to expire rentals** notice in "Needs your attention" of the Pulse (section 10). Everything in this chapter belongs to the server
(API); the rental screens (list, record, create, schedule, dispatch, extend, cancel) and "Convert to serial" are in the screens chapter
[F17](frontend/f17-rentas.md); those of returns, equipment processing and rental reports (block F-R2), in
[F18](frontend/f18-devoluciones-y-proceso-de-rentas.md).

Owner decisions that govern the module:
- **The rented unit is still ours** (D1): it does not leave the inventory. It stays in the **EN-RENTA** bin of the warehouse, with serial
  status **"On rent"** and **reserved**: it counts in *on hand* but **not** in *available*, on any screen.
- The dispatch is a **record of the rental itself** (dates, units, serials, estimated transport cost) with an **empty** link to the
  future shipment (D2). It works today without the shipments module.
- **Billing: only the conditions are saved** (fixed or by time, rate, currency per unit) with an empty link to the invoice (D3).
  Nothing is calculated or charged.
- **Extending** a rental is a record with permission and log (new date, reason, rate if it changes), without approval from a second
  user (D4).
- The **contract** is the contract number and date inside the rental (D6).
- The **return** is also a record of the rental itself (D2), with an **empty** link to the future pickup shipment.
- A serial on rent that appears in a **cycle count** blocks it: the return is recorded first; the EN-RENTA bin is not
  counted (D7).
- **Serials of the Advance Depot units** (D5): the products came from the migration without tracking and **no migration source
  brings serial numbers**; they are made serial-tracked with **"Convert to serial"** (chapter 6 §2.1). Re-importing does not bring them: the
  migration can only create **new products without stock** as serial-tracked (Batch 30; chapter 10 §6 and
  `docs/migracion/depot-series-y-rentas.md`).

## 1. Who can and where

| Action | Permission | Endpoint |
|---|---|---|
| View the list, the record and the extensions log | `rental.view` | `GET /api/v1/rentals`, `GET /api/v1/rentals/{publicId}`, `GET /api/v1/rentals/{publicId}/extensions` |
| Create, edit, add or remove units, set a unit's rate, schedule, dispatch and cancel | `rental.manage` | `POST /api/v1/rentals`, `PATCH /api/v1/rentals/{publicId}`, `POST /api/v1/rentals/{publicId}/lines`, `DELETE /api/v1/rentals/{publicId}/lines/{lineId}`, `PUT /api/v1/rentals/{publicId}/lines/{lineId}/rate`, `POST …/schedule`, `POST …/dispatch`, `POST …/cancel` |
| Extend the pickup date | `rental.extend` (new) | `POST /api/v1/rentals/{publicId}/extensions` |
| Record the return of a rental | `rental.return` | `POST /api/v1/rentals/{publicId}/returns` |
| View the returns (list and record) | `rental.view` | `GET /api/v1/rental-returns`, `GET /api/v1/rental-returns/{publicId}` |
| View the processing queue of returned units | `rental.view` | `GET /api/v1/rental-processes` |
| Advance the process and finish it ("Ready") | `rental.maintenance` | `POST /api/v1/rental-processes/{id}/advance`, `POST /api/v1/rental-processes/{id}/complete` |
| Scrap the unit in its process | `rental.maintenance` **and** `inventory.adjust` | `POST /api/v1/rental-processes/{id}/scrap` |
| Status history of the rental / of the process | `rental.view` | `GET /api/v1/status/history/RENTAL/{id}`, `GET /api/v1/status/history/RENTAL_PROCESS/{id}` |
| Rental reports, indicators and chart (Analytics) | `analytics.view` **and** `rental.view` | `GET /api/v1/analytics/reports`, `POST /api/v1/analytics/reports/{id}/run`, `GET /api/v1/analytics/indicators/{id}/value`, `GET /api/v1/analytics/charts/{id}/data`, `POST /api/v1/analytics/reports/RENTAL/preview` (section 10) |
| Overdue or about to expire rentals notice in "Needs your attention" | `pulse.attention` **and** `rental.view` | `GET /api/v1/analytics/attention` (section 10.5) |

- **Module**: `RENTAL_EQUIPMENT`, now called **"Rentals"**, is in the **Warehouse** category and **depends on `WMS_LOTSERIAL`**
  (Inventory and traceability): it cannot be turned on without it and **turning off `WMS_LOTSERIAL` turns off Rentals** (and its dependent "Rental
  billing"). With the module off the endpoints respond 403 `module_disabled`.
- **Role templates**: the **Company admin** has everything; the **Warehouse operator** has `rental.view`, `rental.manage`,
  `rental.maintenance` and now also **`rental.extend`** and **`rental.return`**. When the platform is updated, every company role that
  already managed rentals (`rental.manage`) receives `rental.extend` and `rental.return` once; the rest are granted from Roles.
- The company comes from the session: a rental, a return, a process, a client, a location, a warehouse, a bin or a product
  of another company respond **404**.
- Every change to the rental, its units, rates and extensions is recorded in the **audit** (type `RENTAL`); returns, in type
  `RENTAL_RETURN`; the process, in type `RENTAL_PROCESS`. Status changes, in the status history.
- **Scrapping** requires two permissions: `rental.maintenance` (the one for the action) and also `inventory.adjust`, because it removes the unit from the
  inventory (an outbound adjustment). Without `inventory.adjust` it responds 403 `Missing permission 'inventory.adjust'.`. The Warehouse operator
  has both.

## 2. The rental

A rental has: number **REN-#####** (automatic, per company), **client** (active), **client location** where the
unit will be, client contact (optional), **origin warehouse** (where the units come from), **start date**, **pickup date**
(current) and **agreed pickup date** (the original one, which does not change with extensions), **contract** (number and signing date,
optional), **estimated transport cost** and its currency (data only), notes and the **units**.

A rental = **one client, one location, several units**. **Only** your **own** units (without a 3PL owner) that are tracked **by
serial** are rented: each unit is **one serial**. If the units arrived without a serial (Advance Depot), they are first converted with **Convert to serial**
(chapter 06 §2.1).

### 2.1 Create (Draft)

`POST /api/v1/rentals` (`rental.manage`)

```json
{ "clientPublicId": "…", "locationPublicId": "…", "startDate": "2026-10-05", "pickupDate": "2026-11-04",
  "warehousePublicId": "…", "clientContactId": 12, "contractNumber": "CT-77", "contractSignedOn": "2026-10-04",
  "estimatedDeliveryCost": 45, "transportCurrency": "USD", "notes": "Llevar cargador",
  "lines": [ { "productPublicId": "…", "serialNumbers": ["SN-0001", "SN-0002"],
               "rate": { "frequency": "MONTHLY", "amount": 150, "currency": "USD" } } ] }
```

- `warehousePublicId` is optional if the company has **a single active warehouse**.
- `lines` is optional: units can be added later. Each row is a product and its serial numbers (one line per serial);
  `rate` is optional (the rate is not mandatory to schedule).
- The rental is born in **Draft** and **does not reserve** anything: the unit remains available until it is scheduled.
- Each serial must be **Available** in a **pickable** bin of the origin warehouse (not in quarantine, cross-dock or
  On rent) and **cannot** be in another open rental (Draft, Scheduled or On rent).
- Response 200: the record (section 2.6).

### 2.2 Edit

`PATCH /api/v1/rentals/{publicId}` — only in **Draft** or **Scheduled**. Only what changes is sent (`null` = no change):
`locationPublicId` (another location **of the same client**), `clientContactId` / `clearClientContact`, `warehousePublicId` (only if the rental
**has no units**), `startDate` and `pickupDate` (only if the rental **has no extensions**; the agreed date follows the current one and the
initial rate of each unit moves with the start date), `contractNumber` (`""` removes it), `contractSignedOn` /
`clearContractSignedOn`, `estimatedDeliveryCost` / `clearEstimatedDeliveryCost`, `transportCurrency`, `notes` (`""` removes them) and
`rowVersion` (the one from the record). **The client is not changed** (cancel the rental and create another); nor the number, the status, the agreed date
or the links to shipment and invoice.

### 2.3 Units and rates

- **Add**: `POST /api/v1/rentals/{publicId}/lines` with `{ "productPublicId", "serialNumbers": [...], "rate"? }` (Draft or
  Scheduled). In a **Scheduled** rental, the added serials **are reserved** at that moment.
- **Remove**: `DELETE /api/v1/rentals/{publicId}/lines/{lineId}` (Draft or Scheduled). In a Scheduled rental it **releases** the reservation of
  that serial. The unit is not deleted: it stays inactive in the rental (audit). Returns the record.
- **Rate of a unit**: `PUT /api/v1/rentals/{publicId}/lines/{lineId}/rate` with `{ "frequency", "amount", "currency"?, "rowVersion"? }`
  (only before dispatch). Frequencies: **`DAILY`** (daily), **`WEEKLY`** (weekly), **`MONTHLY`** (monthly) and **`ONE_TIME`**
  (**"Fixed"**, a single charge). Currency: the company's if not indicated. The rate is **data only** (D3): it is not calculated or invoiced.
  Before dispatch the current rate is corrected in place; afterwards, the rate only changes with an **extension** (section 3).
- Maximum **200** active units per rental.

### 2.4 Schedule (Draft → Scheduled)

`POST /api/v1/rentals/{publicId}/schedule` with `{ "comment"?, "rowVersion"? }` (optional body; the comment goes to the history).

- Requires at least one unit and an active client.
- **Reserves** each serial in the bin where it is **today** (if it was moved since it was added, the new bin is taken, as long as it is still
  Available and in a pickable bin of the origin warehouse). The serial becomes **Reserved** and the product's **available** goes down;
  **on hand** does not change.

### 2.5 Dispatch (Scheduled → On rent)

`POST /api/v1/rentals/{publicId}/dispatch` with `{ "comment"?, "rowVersion"? }`.

- The first time a warehouse dispatches a rental, the zone **RENT ("On rent", type RENTAL)** with the bin
  **EN-RENTA** is created on its own.
- For each unit a **transfer** is recorded in the movement ledger from its bin to **EN-RENTA**, with the reference **"Renta
  REN-#####"**. A transfer is **neutral** in the ledger total: the product still has the same units on hand.
- In **EN-RENTA** the unit remains **on hand and reserved** (available 0); the serial becomes **"On rent"** (`ON_RENT`). The rental becomes **On
  rent** and the dispatch date is sealed; each unit saves its dispatch movement.
- The On rent zone **is not assigned, picked, put away or received into**: neither picking, nor putaway
  suggestions, nor direct-to-bin receiving use it, and "only with available" does not count it.
- A serial **On rent cannot be moved, picked, adjusted or received again** through the normal screens: it comes back with the
  **rental return** (block R2).

### 2.6 The record and the list

Record (`GET /api/v1/rentals/{publicId}`): `rental` (number, client, location, warehouse, dates, **`daysToPickup`** = days until
pickup — negative if it already passed —, **`isOverdue`** = "overdue", contract, status with its color, `units` = active units,
`extensionCount`, dispatch and closing dates), contact, signing date, estimated transport and currency, **`deliveryShipmentId`** and
**`invoiceId`** (empty: they will be filled when Shipments and Billing exist), notes, what can be done (`canEdit`, `canSchedule`,
`canDispatch`, `canExtend`, `canCancel`), the **units** (product, serial, lot, origin bin, dispatch movement and date,
current rate and **rate history**) and `rowVersion`. In a **cancelled** rental the units it had are shown, inactive.

List (`GET /api/v1/rentals`): paginated (`skip`, `take` ≤ 200), most recent first. Filters:
- `status` (one or several: `DRAFT`, `SCHEDULED`, `ON_RENT`, `RETURNED`, `CANCELLED`) and `clientPublicId`;
- **`dueWithinDays=N`**: **about to expire** = Scheduled or On rent whose pickup date is between **today and today + N days**;
- **`overdue=true`**: **overdue** = Scheduled or On rent whose pickup date **already passed** (before today);
- both together: overdue **or** about to expire; with either of them the list is sorted by pickup date;
- `search`: rental number, contract number, client, location or serial number.

"Today" is the day in the **company's time zone**. "Overdue" **is not a status**: it is calculated.

## 3. Extend the pickup date (D4)

`POST /api/v1/rentals/{publicId}/extensions` (**`rental.extend`**) — only in **Scheduled** or **On rent**.

```json
{ "newPickupDate": "2026-11-19", "reason": "El hospital pidió dos semanas más",
  "rates": [ { "lineId": 31, "frequency": "MONTHLY", "amount": 165 } ], "rowVersion": "…" }
```

- The new date must be **later** than the current one; the **reason** is mandatory (up to 300 characters).
- It is saved in the **log** (previous date, new date, reason, who and when) and the rental takes the new pickup date; the
  **agreed** date does not change. No approval from another person is needed.
- `rates` (optional): new rate per unit. **Only if it changes** (frequency, amount or currency) the current one is closed and a new version
  is opened **from the day after the previous pickup**, linked to the extension. A rate equal to the current one does not create a version.
- Response: the record. The log: `GET /api/v1/rentals/{publicId}/extensions` (each extension with `daysAdded` and the rates it opened).

## 4. Cancel

`POST /api/v1/rentals/{publicId}/cancel` with `{ "comment"?, "rowVersion"? }` (`rental.manage`) — **only in Draft or Scheduled**.

- In a Scheduled rental it **releases** the reservations: the serials go back to **Available** and the product's available goes up.
- The units become inactive (the serial is free for another rental), the closing date is sealed and the comment goes to the history.
- A **dispatched rental is not cancelled**: it ends with its return (R2).

## 5. Return a rental (block R2)

`POST /api/v1/rentals/{publicId}/returns` (**`rental.return`**) — only in a rental **On rent**. Records a **rental return**
DRN-##### (its own numbering per company) with the units that come back:

```json
{ "reason": "EARLY_DAMAGE", "returnedOn": "2026-10-20", "notes": "Pantalla rota en el hospital",
  "toBinId": 412, "estimatedPickupCost": 30, "transportCurrency": "USD", "rowVersion": "…",
  "lines": [ { "serialNumber": "SN-0002", "condition": "DAMAGED", "toBinId": 415, "requiresProcess": true, "notes": "Golpe lateral" } ] }
```

- **Reason** (one per return, mandatory): `END_OF_CONTRACT` "End of contract", `EARLY_DAMAGE` "Early due to damage",
  `EARLY_CLIENT` "Early at the client's request" or `OTHER` "Other" (with "Other" the **notes** are mandatory).
- **Units**: each one by its **serial number**, which must be a unit **dispatched and not returned from that rental**. You can return
  **a part** (for example, the unit that was damaged) and the rest later: the rental stays **On rent** until the last unit comes back.
- **Condition** of each unit: `GOOD` "Good" (default), `DAMAGED` "Damaged" or `INCOMPLETE` "Incomplete".
- **Destination bin**: the unit's (`lines[].toBinId`), otherwise the header's (`toBinId`) and, if none is indicated, **the bin
  the unit came from**. It can be in **another warehouse** of the company (for example, a workshop's quarantine). **Never** the On
  rent zone.
- **Goes through processing?** (`requiresProcess`, **yes by default**): decided by whoever receives it. With processing, the unit stays **on hand but
  reserved** and its serial **"In processing"** (does not count as available) and its **process** (section 6) opens at the first step
  ("Pending"). Without processing, it is **Available** immediately.
- **Date** of the return: today by default (company day); it cannot be in the future or before the start of the rental. The return
  is **early** (`isEarly`) if the date is before the current pickup date: it is a calculated value for the reports.
- **Estimated pickup cost** and its currency: data only; the link to the future pickup shipment (`pickupShipmentId`) stays empty (D2).

In the inventory, for each unit a **transfer** is recorded in the movement ledger from **EN-RENTA** to the destination bin, with the
reference **"Devolución de renta DRN-#####"** (neutral in the total: the unit remained the company's). When **all** the
dispatched units come back, the rental becomes **Returned** (terminal) and its closing date is sealed; the history comment is
"Devolución DRN-#####".

Response (the return record): number, rental, client, date, reason, `isEarly`, `units`, `openProcesses` (units still in
processing), the status of the rental after the return (`rentalStatusCode`), notes, pickup cost and the units (product, serial,
condition, destination warehouse and bin, whether it goes through processing, ledger movement, notes and the process with its status).

List: `GET /api/v1/rental-returns` (`rental.view`), paginated (`skip`, `take` ≤ 200), most recent first. Filters: `rentalPublicId`,
`clientPublicId`, `reason` (one or several), `from`/`to` (return date), `early=true|false` and `search` (DRN number, rental number or
serial). Record: `GET /api/v1/rental-returns/{publicId}`. The rental record shows each unit's `returnedAtUtc`.

## 6. Processing of the returned unit (configurable)

Each unit returned "with processing" has its **process** (`RentalProcess`) with the statuses of the **RentalProcessStatus** pipeline, which the
company can **rename, reorder and deactivate** from the status configuration:

| Status | Code | Type | What it means |
|---|---|---|---|
| Pending | `PENDING` | initial | just returned, not yet checked |
| Inspection | `INSPECTION` | stage | the unit is checked |
| Cleaning | `CLEANING` | stage | cleaning / disinfection |
| Testing | `TESTING` | stage | functional tests |
| Ready | `READY` | terminal | the unit is **available** again |
| Repair | `REPAIR` | detour | under repair (from any step) |
| Awaiting parts | `AWAITING_PARTS` | detour | stopped waiting for parts (from any step) |
| Scrapped | `SCRAPPED` | terminal | the unit leaves the inventory |

- **Queue**: `GET /api/v1/rental-processes` (`rental.view`): open ones first (oldest at the top), then the finished ones. Filters
  `status` (one or several), `open=true|false`, `warehousePublicId`, `productPublicId` and `search` (serial, SKU, return or rental
  number). Each row: serial, product, current warehouse and bin, status with its color, `isFinished`, origin return and rental, condition
  in which it came back, start and end dates, notes (those of the unit in the return) and `rowVersion`.
- **Advance**: `POST /api/v1/rental-processes/{id}/advance` with `{ "status": "CLEANING", "comment"?, "rowVersion"? }`
  (`rental.maintenance`). It can move to **any enabled status**, with the usual pipeline rules: from one step to the
  **next** enabled one; to **Repair** or **Awaiting parts** from any step, and from there back to the step it was in or to the
  next one; to a terminal (Ready or Scrapped). An illegal jump responds 422 with the status engine's message. Moving to `SCRAPPED`
  this way also requires `inventory.adjust` (it is the same as scrapping).
- **Finish** ("Ready"): `POST /api/v1/rental-processes/{id}/complete` with `{ "binId"?, "comment"?, "rowVersion"? }` (`rental.maintenance`).
  With `binId` (a bin of the **same warehouse**, not of the On rent zone), the unit is first **moved** (transfer with the reference
  "Proceso #id", still reserved and "In processing") and then it becomes Ready. On reaching **Ready** the **reservation is released**: the serial goes from
  "In processing" to **Available** and the product's available **comes back**.
- **Scrap**: `POST /api/v1/rental-processes/{id}/scrap` with `{ "comment"?, "rowVersion"? }` (`rental.maintenance` **and**
  `inventory.adjust`). An **outbound adjustment** (`ADJUSTMENT −1`) is recorded from the process bin with the reason **DAMAGE**
  ("Damage") and the reference "Proceso #id" (note "Baja del proceso #id"); the serial becomes **Scrapped** (it does not return to the inventory) and the product's on
  hand goes down by one.
- The effects on the inventory depend **only on the terminals** (Ready releases, Scrapped scraps): the intermediate steps do not move
  inventory, which is why the company can configure them freely. Reaching Ready through "advance" does the same as "finish" (without a move).
- A **finished** process (Ready or Scrapped) is read-only: any action responds 422 `The process has already finished; it is read-only.`
- History: `GET /api/v1/status/history/RENTAL_PROCESS/{id}` (each step with its comment, who and when).

## 7. Units on rent and the cycle count (D7)

- The **EN-RENTA bin is not counted**: creating a count asking for that bin or the RENT zone, a count of "what changed" with the
  RENT zone, or adding a line in EN-RENTA responds 422 `Bin {bin} is for units on rent; it is not counted.`. Broad counts (whole
  warehouse, by product, what changed) **skip** it without error.
- A serial **On rent** captured in another bin **blocks** the count: the preview marks the line and "Confirm count & adjust"
  responds 409 `Serial {s} is on rent ({REN-n}); record its return before reconciling the count.` without moving anything. If the unit
  really came back, record its return (section 5) and capture again; if it was a capture error, remove it and confirm.
- Serials "In processing" are in their bin, on hand and **reserved**: they are counted as always; if they are not found, the count
  responds the 409 of "what was counted is less than what is reserved" (finish or scrap the process first).

## 8. Statuses and transitions

| Status | Code | Type | What it allows |
|---|---|---|---|
| Draft | `DRAFT` | initial | edit, units, rates, schedule, cancel |
| Scheduled | `SCHEDULED` | stage | edit, units (reserve/release), rates, dispatch, extend, cancel |
| On rent | `ON_RENT` | stage | extend; return (total or partial) |
| Returned | `RETURNED` | terminal | read-only (its returns are shown) |
| Cancelled | `CANCELLED` | terminal | read-only |

| From → to | Who | Effects | What blocks it |
|---|---|---|---|
| (new) → Draft | `rental.manage` | number REN-#####; units not reserved | deactivated client, location of another client or deactivated, serial not available or in another rental |
| Draft → Scheduled | `rental.manage` | reserves the serials (serial Reserved; available goes down) | no units, deactivated client, serial not available |
| Scheduled → On rent | `rental.manage` | transfer to EN-RENTA, reserved at the destination; serial On rent; dispatch date | deactivated client; lost reservation; EN-RENTA bin deactivated; serial status `ON_RENT` deactivated by the company |
| Draft / Scheduled → Cancelled | `rental.manage` | releases reservations; units inactive; closing date | dispatched (the system also prevents it even if the rule is changed) |
| On rent → Returned | `rental.return` | the **last** return (when no dispatched unit remains unreturned); closing date; comment "Devolución DRN-#####" | status `RETURNED` deactivated by the company (422) |
| (return) | `rental.return` | per unit: transfer EN-RENTA → destination; serial In processing and reserved (with processing) or Available; opens the process | rental not On rent; serial that is not on rent in that rental; destination in the On rent zone |

**Serial** statuses added by Rentals (detours, system-owned): **On rent** (`ON_RENT`) and **In processing** (`IN_PROCESS`, while
the returned unit is in its process). Both count as "in inventory" in the cycle count and in the "serials to capture" indicator.
Serial cycle of a rented unit: Available → Reserved (schedule) → On rent (dispatch) → In processing (return with processing) →
Available (Ready) or Scrapped; without processing, from On rent to Available.

**Process** transitions (section 6):

| From → to | Who | Effects | What blocks it |
|---|---|---|---|
| (return with processing) → Pending | `rental.return` | opens with the serial In processing and reserved at the destination bin | initial status deactivated (422) |
| step → next step, step ↔ Repair / Awaiting parts | `rental.maintenance` | none in the inventory | illegal jump or deactivated status (422); finished process (422) |
| any → Ready | `rental.maintenance` | optional move within the warehouse and **releases the reservation** (serial Available); end date | bin of another warehouse or of the On rent zone (400); finished process (422) |
| any → Scrapped | `rental.maintenance` + `inventory.adjust` | **outbound adjustment** with reason DAMAGE; serial Scrapped; end date | without `inventory.adjust` (403); finished process (422) |

The company can **rename** and **reorder** the rental statuses, but must **not** deactivate them: if it deactivates "Scheduled", "On
rent" (of the rental or of the serial) or "Cancelled", the corresponding action responds 422 `Status '{código}' does not exist or is not
enabled for this company.`

## 9. Messages (server)

| Case | Exact message | HTTP |
|---|---|---|
| Create without client | `Enter the rental's client.` | 400 |
| Create without location | `Enter the client location where the equipment will be.` | 400 |
| Create without start date / without pickup date | `Enter the rental's start date.` / `Enter the pickup date.` | 400 |
| Pickup before the start (create or edit) | `The pickup date cannot be earlier than the start date.` | 400 |
| Location of another client (create or edit) | `The location does not belong to the rental's client.` | 400 |
| Contract number over 80 / notes over 1000 characters | `The contract number allows at most 80 characters.` / `Notes allow at most 1000 characters.` | 400 |
| Negative transport cost | `The estimated transport cost cannot be negative.` | 400 |
| Currency that does not exist (transport or rate) | `Unknown currency: '{código}'.` | 400 |
| Unit without product / without serials | `Enter the unit's product.` / `Enter at least one serial number.` | 400 |
| Empty, long, repeated or too many serials | `Serial numbers cannot be empty.` / `A serial number allows at most 80 characters.` / `Serial number {s} is repeated.` / `A line allows at most 500 serial numbers.` | 400 |
| More than 200 units | `A rental allows at most 200 units.` | 400 |
| Product of a 3PL client | `Only own equipment is rented; {sku} belongs to a client.` | 400 |
| Product without serial tracking | `Product {sku} is not serial-tracked; only equipment with a serial number is rented.` | 400 |
| Rate without frequency / unknown frequency | `Enter the billing frequency: DAILY, WEEKLY, MONTHLY or ONE_TIME.` / `Unknown billing frequency: '{x}'. Use DAILY, WEEKLY, MONTHLY or ONE_TIME.` | 400 |
| Rate without amount / negative | `Enter the rate amount.` / `The rate cannot be negative.` | 400 |
| Change the client in the PATCH | `The rental's client cannot be changed; cancel the rental and create another.` | 400 |
| Change number, status, agreed date, dispatch/closing dates or links in the PATCH | `The field '{campo}' cannot be modified.` | 400 |
| Extension without new date / without reason / long reason | `Enter the new pickup date.` / `Enter the reason for the extension.` / `The reason allows at most 300 characters.` | 400 |
| Extension with a date equal to or earlier than the current one | `The new pickup date must be later than the current one ({aaaa-mm-dd}).` | 400 |
| Extension rate without unit | `Enter the unit (lineId) of the rate.` | 400 |
| List with negative `dueWithinDays` | `Days must be 0 or more.` | 400 |
| `rowVersion` that is not base64 | `Invalid rowVersion: the base64 value returned by the record is expected.` | 400 |
| Without the action's permission | (403 `PERMISSION_DENIED`) | 403 |
| Rentals module off | `The 'RENTAL_EQUIPMENT' module is not enabled for this company.` | 403 |
| Rental / client / location / contact / warehouse / product / unit of another company or nonexistent | `Rental not found.` / `Client not found.` / `Location not found.` / `Contact not found.` / `Warehouse not found.` / `Product not found.` / `Line not found.` | 404 |
| Deactivated client (create, schedule, dispatch) | `The client has been deactivated; only its history can be viewed.` | 409 |
| The serial is already in another open rental | `Serial {s} is already in rental {REN-n}.` | 409 |
| The serial is not Available in a pickable bin of the origin warehouse (it does not exist, it is reserved, rented, in quarantine, in another warehouse…) | `Serial {s} is not available in {bin or warehouse}.` | 409 |
| Another operation took the serial at the same time | `One of the serials was just added to another rental; reload and try again.` | 409 |
| Change the warehouse of a rental with units | `Remove the rental's equipment before changing the source warehouse.` | 409 |
| Another user changed the rental (with `rowVersion`) | `The record was modified by another user; reload and try again.` | 409 |
| The warehouse already uses the code RENT or EN-RENTA for another zone or bin | `Warehouse {código} already has a RENT zone that is not for rentals; change its code to be able to dispatch rentals.` / `Warehouse {código} already has an EN-RENTA bin outside the On rent zone; change its code to be able to dispatch rentals.` | 409 |
| Edit, add/remove units or rate of a dispatched or returned rental | `Rental {n} was already dispatched; it cannot be modified.` | 422 |
| Edit a cancelled rental | `Rental {n} is cancelled; it is read-only.` | 422 |
| Schedule or dispatch without units | `The rental has no equipment; add at least one.` | 422 |
| Schedule a rental that is not in Draft | `Only a Draft rental can be scheduled; rental {n} is not.` | 422 |
| Dispatch a rental that is not Scheduled | `Only a Scheduled rental can be dispatched; schedule rental {n} first.` | 422 |
| Cancel a rental On rent, Returned or Cancelled | `Only a Draft or Scheduled rental can be cancelled; to end it, record the return.` | 422 |
| Extend a rental in Draft, Returned or Cancelled | `Only a Scheduled or On rent rental can be extended.` | 422 |
| Change dates of a rental with extensions | `The rental already has extensions; the pickup date changes with an extension.` | 422 |
| Deactivated location | `The location has been deactivated; choose another.` | 422 |
| Inactive origin warehouse | `Warehouse {código} is inactive; it does not allow inventory movements.` | 422 |
| EN-RENTA bin deactivated by hand | `The EN-RENTA bin is inactive; it does not allow inventory movements.` | 422 |

Return, process and count (block R2):

| Case | Exact message | HTTP |
|---|---|---|
| Return without reason / unknown reason | `Enter the return reason: END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT or OTHER.` / `Unknown return reason: '{x}'. Use END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT or OTHER.` | 400 |
| Reason "Other" without notes | `With the reason 'Other', describe the return in the notes.` | 400 |
| No units / unit without serial / repeated serial | `Enter at least one serial being returned.` / `Enter the serial number of the returned unit.` / `Serial number {s} is repeated.` | 400 |
| Unknown condition | `Unknown condition: '{x}'. Use GOOD, DAMAGED or INCOMPLETE.` | 400 |
| Return notes over 1000 / unit notes over 500 characters | `Notes allow at most 1000 characters.` / `Unit notes allow at most 500 characters.` | 400 |
| Future date / before the start | `The return date cannot be in the future.` / `The return date cannot be before the rental start ({aaaa-mm-dd}).` | 400 |
| Negative pickup cost / unknown currency | `The estimated pickup cost cannot be negative.` / `Unknown currency: '{código}'.` | 400 |
| Destination bin in the On rent zone (return or finishing the process) | `The destination bin cannot be in the On rent zone.` | 400 |
| Finish the process with a bin of another warehouse | `The destination bin must belong to the process's warehouse {código}.` | 400 |
| Advance without status | `Enter the status the process moves to.` | 400 |
| Scrap (or advance to SCRAPPED) without `inventory.adjust` | `Missing permission 'inventory.adjust'.` | 403 |
| Return / process / bin of another company or nonexistent | `Rental return not found.` / `Process not found.` / `Bin not found.` / `Rental not found.` | 404 |
| The serial is not a unit dispatched and not returned from that rental | `Serial {s} is not on rent in {REN-n}.` | 409 |
| Another operation returned the same unit at the same time | `One of the units was just returned in another operation; reload and try again.` | 409 |
| Another user changed the rental or the process (with `rowVersion`) | `The record was modified by another user; reload and try again.` | 409 |
| Count: serial On rent captured in another bin | `Serial {s} is on rent ({REN-n}); record its return before reconciling the count.` | 409 |
| Return a rental that is not On rent (Draft, Scheduled, Returned, Cancelled) | `A return can only be recorded for an On rent rental; rental {n} is not.` | 422 |
| Any action on a Ready or Scrapped process | `The process has already finished; it is read-only.` | 422 |
| Advance to a deactivated status or with an illegal jump | `Status '{código}' does not exist or is not enabled for this company.` / `Illegal jump: from '{de}' you can only advance to '{siguiente}'.` / `From the detour '{x}' you can only go back to '{paso}' or advance to '{siguiente}'.` | 422 |
| Count: request the EN-RENTA bin or the RENT zone, or add a line in EN-RENTA | `Bin {bin} is for units on rent; it is not counted.` | 422 |
| Inactive destination (bin or warehouse) | `Bin {código} is inactive; it does not allow inventory movements.` / `Warehouse {código} is inactive; it does not allow inventory movements.` | 422 |

Inventory messages seen when trying to move a rented serial (through the normal screens):

| Case | Exact message | HTTP |
|---|---|---|
| Transfer or adjust a serial On rent | `Serial {s} is not available in EN-RENTA.` | 409 |
| Pick a serial On rent | `Serial {s} is not available in the warehouse (it does not exist, it already left or it is in another bin or lot).` | 409 |
| Receive again (normal return) a serial On rent | `Serial {s} is already in inventory.` | 409 |

## 10. Reports, indicators and notices (block R3)

Rentals, their returns and the processes of the returned units can be consulted in **Analytics** (views, indicators and charts,
with the usual filters, columns and groupings) and in the **Pulse** ("Needs your attention"). Rules that apply to everything in this
section:

- **Who**: `rental.view` is needed (in addition to `analytics.view` for Analytics or `pulse.attention` for the Pulse). Without `rental.view` the
  rental data sources do not appear in the list of sources and their views, indicators and charts are neither listed nor read (404, like a source
  without permission).
- **Module**: with the **Rentals** module (`RENTAL_EQUIPMENT`) **off**, the rental data sources, their views, indicators and charts (the system
  ones and those the company created) and the notice **disappear**; reading one by its id responds 404 (`Indicator '{id}' not
  found.`, `Chart '{id}' not found.`, `View '{id}' not found.`). When the module is turned on again, everything comes back as
  it was (nothing is deleted). Since Rentals depends on Inventory and traceability, turning off `WMS_LOTSERIAL` also hides them.
- **Company**: each company sees only its rentals, returns and processes.
- **"Today"** is the day in the **company's time zone** (Tenant settings): "overdue", "about to expire", "days to pickup" and
  "days in processing" are calculated when read, they are not stored. "Overdue" is not a status.

### 10.1 Data sources

| Source (key) | One row per | Date field (range) | Fields |
|---|---|---|---|
| **Rentals** (`RENTAL`) | rental | **Start date** (`StartDate`) | Number, client, location, origin warehouse, status and its code, **Open** (Scheduled or On rent), start date, **pickup date** (current), agreed pickup, **Days to pickup** (negative = already passed), **Overdue** (open with the pickup before today), extensions, days extended (current pickup − agreed), **Units** (active), **Units at the client** (dispatched and not returned), returned units, contract number and date, estimated transport (money) and its currency, dispatch, closing and creation dates. Relations: Client, Location, Warehouse |
| **Rental returns** (`RENTAL_RETURN`) | DRN return | **Return date** (`ReturnedOn`) | Number, rental, client, location, return date, rental pickup date, **Reason** and its code, **Early** (before the current pickup date), days early, **Condition** (the conditions of its units, in order Good, Damaged, Incomplete; e.g. "Good, Incomplete") and its code, **With damaged unit**, units (total, in good condition, damaged, incomplete), units with processing, open processes, estimated pickup (money) and its currency, registration date. Relations: Rental, Client |
| **Returned equipment processing** (`RENTAL_PROCESS`) | process of a unit | **Started on** (`StartedAtUtc`) | Serial, product (SKU and name), warehouse, current bin, status and its code, **Open** (has not reached Ready or Scrapped), return, rental, client, **Condition on return**, start, end, **Days in processing** (company days, until the end or until today). Relations: Product, Warehouse, Return, Rental |

- The three sources accept the **custom fields** of their entity (`RENTAL`, `RENTAL_RETURN`, `RENTAL_PROCESS`) as columns.
- The **date range** of an indicator or chart filters by the source's date field (company days). For the current state
  (what is overdue today, what is in processing today) the **"All"** range (ALL) is used: that is how they come seeded.
- Limit: 20,000 rows per source (the most recent by their date), like the rest of the sources.

### 10.2 System views (reports)

They are seeded in each company (they are system views: they are not edited or deleted). They run with the "All" range unless
another is indicated when running them.

| View | Source | What it shows | Filter | Columns / grouping | Order |
|---|---|---|---|---|---|
| **Units on rent by client** | Rentals | What is **on rent today**, by client | Status = On rent | Grouped by **Client**: number of rentals and **sum of units at the client**, with a totals row | — |
| **Rentals due soon (7 days)** | Rentals | Open rentals picked up **today or in the next 7 days** | Open and days to pickup between 0 and 7 | Number, client, location, warehouse, status, pickup date, days to pickup, units, units at the client, contract | Pickup date (the nearest at the top) |
| **Overdue rentals** | Rentals | Open rentals with the pickup date **already passed** | Overdue | The same columns | Pickup date (the most overdue at the top) |
| **Rental returns by reason** | Rental returns | Returns by **reason** | — | Grouped by **Reason**: number of returns and sum of returned units, with totals | — |
| **Units in processing** | Returned equipment processing | Returned units that **are still in their process** | Open | Serial, SKU, product, warehouse, bin, status, condition, return, rental, client, started on, days in processing | Started on (the oldest at the top) |

"Open" = **Scheduled or On rent** (a Scheduled rental whose pickup already passed also counts as overdue: the unit is set aside).

### 10.3 System indicators

| Indicator | Source | Calculation | Range | Pulse |
|---|---|---|---|---|
| **Rentals due soon (7 days)** | Rentals | Count of open rentals with the pickup between today and today + 7 | All | Off (each user can turn it on with "show on Pulse") |
| **Overdue rentals** | Rentals | Count of open rentals with the pickup before today | All | Off |

They are in the **Warehouse** business module and come off in the Pulse because "Needs your attention" already shows each overdue or
due-soon rental (same criterion as "Pending discrepancies").

### 10.4 System chart

**Rental returns by reason**: donut with the number of returns of the **last 30 days** (by return date) by reason
(End of contract, Early due to damage, Early at the client's request, Other). Off in the Pulse; each user can change the range or
turn it on.

### 10.5 "Needs your attention" notice (Pulse)

`GET /api/v1/analytics/attention` adds, next to the inventory discrepancies, one row for each **open** rental (Scheduled or On
rent) that is **overdue** or that **is due in the next 7 days** (pickup between today and today + 7, both included):

- **Who sees it**: whoever has `pulse.attention` and `rental.view`, with the Rentals module on. Without `rental.view` or with the module
  off, the notice contributes no rows and does not count in the total (no error).
- **Order**: those with the oldest pickup first (the most overdue at the top); together with the other notices, the panel shows the 5
  oldest.
- **Tone**: red (`danger`) if overdue; amber (`warn`) if due soon.
- **Row data** (`params`): rental number, client, location, warehouse, pickup date (yyyy-mm-dd), days to pickup
  (negative = overdue), `overdue` (true/false), unreturned units and status. `sinceUtc` = midnight (company time) of the pickup
  day. Notice code: **`RENTAL_DUE`**.
- **"Review"** opens `/warehouse/rentals?rental={publicId}` and **"View all (N)"** `/warehouse/rentals?dueWithinDays=7&overdue=true` (the list
  of rentals with the "due soon" and "overdue" filters together, section 2.6).
- The notice disappears only when the rental stops being overdue or due soon: when **extending it** (if the new date is more than 7
  days away), when **returning** all its units or when **cancelling it** (if it was Scheduled).

From the Rentals web (Batch F17, block F-R1) the panel shows the row with its text (*"Rental REN-… overdue"*, *"…: pickup today"* or
*"…: due in N days"*, client · location · warehouse, pickup, units and days overdue) and "Review"/"View all" open the record and the list
of Warehouse → Rentals (screens chapter [F17](frontend/f17-rentas.md)).

**On the web (Batch F18, block F-R2)**: the return is recorded from the rental record (**Register return**), returns are
consulted in Warehouse → **Rental returns**, the processing queue in Warehouse → **Equipment processing** (Advance, Complete, Scrap)
and the views, indicators and chart of this section in **Rental reports** (from the rental list; it also requires `analytics.view`).
The rental list shows at the top the totals On rent today, Due soon (7 days) and Overdue. Screens chapter
[F18](frontend/f18-devoluciones-y-proceso-de-rentas.md).

### 10.6 Messages

| Case | Exact message | HTTP |
|---|---|---|
| Read a rental indicator, chart or view without `rental.view` or with the Rentals module off | `Indicator '{id}' not found.` / `Chart '{id}' not found.` / `View '{id}' not found.` | 404 |
| Preview or creation of a view, indicator or chart on a rental source without `rental.view` or with the module off | `Data source 'RENTAL' not found.` (or `RENTAL_RETURN`, `RENTAL_PROCESS`) | 404 |
| Analytics without `analytics.view` / panel without `pulse.attention` | (403 `PERMISSION_DENIED`) | 403 |
| Analytics with the Analytics module off | `The 'ANALYTICS' module is not enabled for this company.` | 403 |

## 11. Movement ledger, audit and data

- Movement ledger: the dispatch consists of **transfers** with the unit's bin as origin and **EN-RENTA** as destination, reference **"Renta REN-#####"**
  (in English "Rental REN-#####"); filter by `refEntity=RENTAL&refId={id}`. The movement detail shows the rental (number, status,
  client and contract number).
- Movement ledger of the return: **transfers** from EN-RENTA to the destination bin with the reference **"Devolución de renta DRN-#####"**
  (`refEntity=RENTAL_RETURN&refId={id}`); the detail shows the return (number, date, client and rental number).
- Movement ledger of the process: the move when finishing is a **transfer** "Proceso #id" and the scrapping an **adjustment −1** with reason DAMAGE
  (`refEntity=RENTAL_PROCESS&refId={id}`); the detail shows the process with its status and, as parent document, its return.
- Balances: the EN-RENTA bin shows what is rented **on hand and reserved**. The product: on hand unchanged, available without what is rented.
- Serials: `GET /api/v1/products/{publicId}/serials` shows the rented ones with status "On rent" in EN-RENTA.
- Audit: `GET /api/v1/audit/changes?entityType=RENTAL&entityId={id}`; returns `entityType=RENTAL_RETURN`, processes
  `entityType=RENTAL_PROCESS`.

## 12. Frequent cases

- **I want to rent a Depot unit and it won't let me**: the product is not serial-tracked (400). Convert it with **Convert to serial**
  (chapter 06 §2.1) capturing the serials of each unit, and add it by its serial.
- **I scheduled a rental and the product shows as "Not available"**: that is expected: the serials are reserved for the rental.
- **I dispatched and the inventory did not go down**: correct (D1): the unit is still ours; it is in the EN-RENTA bin, on hand and reserved.
- **The client asked for more time**: use **Extend** (`rental.extend`) with the new date and the reason; if the rate changes, indicate it.
- **I picked the wrong unit before dispatching**: remove it (DELETE of the line) and add the right one; in Scheduled the reservation is released and
  the new one is taken.
- **The client backed out before delivery**: cancel the rental (only Draft or Scheduled).
- **The unit was damaged at the client**: record a return **of that unit only** with the reason "Early due to damage" and the condition
  "Damaged" (to quarantine, for example); it goes through processing. The rest stays On rent until its return.
- **The rental ended**: return all the units (reason "End of contract"); the rental becomes Returned. Those that do not need review,
  with `requiresProcess: false`: they are available immediately.
- **I returned a unit and it shows "Not available"**: it is in its process ("In processing", reserved). Advance the process and finish it ("Ready")
  so that it becomes available again.
- **The unit cannot be repaired**: in its process use **Scrap** (requires `inventory.adjust`): it leaves the inventory with reason "Damage" and the
  serial is scrapped.
- **When reconciling a count it says a serial is on rent**: the unit is recorded at the client. If it really came back, record
  its return and capture again; if not, remove the serial from the capture.
- **I don't want the "Cleaning" step**: deactivate it in the process status configuration; the effects on the inventory only depend on
  "Ready" and "Scrapped".
- **Which rentals do I have to pick up this week?** Run the view **"Rentals due soon (7 days)"** (or look at "Needs your attention"):
  it shows the open ones picked up from today to 7 days, the nearest at the top.
- **A rental shows as overdue and the unit already came back**: its return has not been recorded (section 5). If the client is still using it, extend
  the rental (section 3): with the new date it stops being overdue.
- **A Scheduled rental shows as overdue**: the pickup already passed and it was never dispatched; dispatch it and extend it, or cancel it if it is no longer going.
- **How many units do I have at each client today?** View **"Units on rent by client"** (sum of units at the client by client,
  with total).
- **Why are units returned early?** View or chart **"Rental returns by reason"**; filter "Early" to
  see only the early ones, or group by "Condition" to see how many came back damaged.
- **Which units have been under review the longest?** View **"Units in processing"** (column "Days in processing"; the oldest at the top).
- **I don't see the rental reports**: check that the **Rentals** module is on and that your role has `rental.view` (and `analytics.view`).
- **I want to see overdue rentals on my Pulse**: turn on "show on Pulse" in the "Overdue rentals" indicator (it comes off because the
  "Needs your attention" notice already shows them).
