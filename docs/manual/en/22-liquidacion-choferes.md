# 22 — Driver settlement

Batches 93 to 95 (P9, 2026-10-17). Each driver is paid for what they did on the street: per delivery, per attempt and per trip. This chapter grows with each batch of P9: the foundation (delivery attempts, §1) and the **settlement** (§2). The rates and the pay policy already exist (Drivers & rates, chapter 04).

## 1. Delivery attempts

Every time a driver tries to deliver an order there is an **attempt**: its number (1, 2, 3…), the driver, the date and time, whether it was **delivered** or **failed** (with the failure reason) and a note. It is the **source of truth** for the settlement: nothing is paid to a driver outside this record.

### Where they come from

| Source (`source`) | When |
|---|---|
| `STATUS` | Automatically: when an order moves to **Delivered** (delivered attempt) or **Failed** (failed attempt). It carries the driver of the order's **current route** and the route; the comment of the status change becomes the note. |
| `MANUAL` | The office records it by hand (until the driver app exists, P7). |
| `APP` | Reserved for the driver app. |

Rules of the automatic record: a **special delivery** (one that has a paid trip, `DriverTrip`) does not generate attempts, because it is paid per trip; if the order already has a current delivered attempt, no other one is recorded; if the route has no driver, the attempt is left **without a driver** and the office completes it (voiding it and recording it with a driver) before settling.

### Who can

Read with `driverpay.view`; record and void with `driverpay.manage`. `LTL_GROUND` module on.

### Endpoints

| Endpoint | What for |
|---|---|
| `GET /api/v1/delivery-attempts` | Paginated list. Filters: `driverPublicId`, `orderPublicId`, `from`/`to` (company days), `delivered`, `settled`, `withoutDriver`, `includeVoided`, `skip`, `take` (max. 200). Most recent first. |
| `POST /api/v1/delivery-attempts` | Records an attempt by hand: `orderPublicId`, `driverPublicId`, `delivered`, `attemptNumber` (empty = the next one), `attemptedAtUtc` (empty = now), `failureReason` (catalog code, only if not delivered), `notes`. |
| `POST /api/v1/delivery-attempts/{publicId}/void` | Voids an attempt (`reason`). The voided number can be used again. |

### Validations

| Situation | Message | Code |
|---|---|---|
| No order | `Specify the shipment of the attempt.` | 400 |
| No driver | `Specify the driver of the attempt.` | 400 |
| Delivered with a failure reason | `A delivered attempt does not carry a failure reason.` | 400 |
| Reason that does not exist | `The failure reason '{code}' does not exist.` | 400 |
| Note too long | `The note cannot exceed 400 characters.` | 400 |
| Future date | `The attempt date cannot be in the future.` | 400 |
| Repeated number | `The shipment already has an attempt number {n}.` | 400 |
| Two delivered | `Only one attempt can be the delivery.` | 400 |
| Delivered one that is not the last | `The delivery must be the last attempt.` | 400 |
| Special delivery | `It is a special delivery: it is paid per trip, not per attempts.` | 422 |
| Order or driver does not exist | 404 | 404 |
| Voiding without a reason | `Enter the reason for voiding.` | 400 |
| Voiding one already voided | `The attempt is already void.` | 409 |
| Voiding one already settled | `The attempt is already in a settlement and cannot be voided.` | 409 |

The sequence rule (at most one delivered and it must be the last) is the same as the pay engine's, so everything that is recorded can be settled.

### Frequent cases

- **A delivered order has no attempts:** it is a special delivery (paid per trip) or it was delivered before this batch. Record them by hand.
- **An attempt came out “without a driver”:** the order's route had no driver. Void it and record it with the right driver.
- **I picked the wrong driver:** void the attempt (with a reason) and record it again; if it is already in a settlement it cannot be voided.

## 2. The settlement (Batch 94)

A **settlement** (`LIQ-#####`) gathers, for one driver or for all, what there is to pay in a period: the **deliveries** (with their attempts) and the **trips**. It calculates the pay with the rates and formula in force and **freezes** it. It follows the same human approval flow as a billing run: **Generated → Reviewed → Approved → Exported** (or Cancelled). Teikem calculates and exports; **it does not issue the payment**.

### Who can

| Action | Permission |
|---|---|
| View settlements and the preview | `driverpay.view` |
| Generate, review, adjust a line and cancel | `driverpay.manage` |
| Approve (asks for re-authentication, AAL2) | `driverpay.approve` |
| Export | `driverpay.export` |

`driverpay.approve` and `driverpay.export` are held by default only by the company administrator (whoever generates does not approve). `LTL_GROUND` module.

### What goes into a settlement

- **Deliveries:** the current, unsettled attempts of each order and driver, with a date within the period (company days). The pay engine turns them into money with the policy's **formula**:
  1. *Delivery + each attempt* (the factory one): the delivery rate once (if there was a delivery) plus that of each attempt, including the successful one.
  2. *Delivery includes the 1st attempt*: the delivery plus the attempts from the 2nd on.
  3. *Failed attempt replaces delivery*: each failed attempt pays its rate; the successful one pays only the delivery.
  The **delivery rate** is the driver's for the order's service and the package type of its **first cargo line**, in force on the day of the delivery; the **attempt** rates are the driver's per level (an attempt beyond the last level uses the last level). A missing rate leaves the line at **$0 with the note “no rate configured”**; an amount is never made up.
- **Trips:** the open paid trips (`DriverTrip`) with a trip date within the period, with the amount frozen when they were created (those without a rate go in at $0 with the same note).
- **Left out:** special deliveries (paid through their trip), voided or already settled attempts, and attempts **without a driver** (they are counted under “without a driver” in the settlement so the office completes them).

### Steps and rules

| Step | What it does | Message if not possible (422) |
|---|---|---|
| **Preview** `POST /driver-settlements/preview` | Shows what would go in, without saving anything. | — |
| **Generate** `POST /driver-settlements` (`driverPublicId` optional, `periodStart`, `periodEnd`, `note`) | Creates the settlement with its lines, **freezes** the formula and the amounts and **reserves** the attempts and trips (nothing is paid twice). | `There is nothing to settle in the period.` |
| **Review** `POST …/review` | Generated → Reviewed. | `Only a generated settlement can be reviewed.` |
| **Adjust a line** `PATCH …/lines/{id}` (`amount`, `note`) | Changes the amount of a line (keeps the calculated one); the reason is mandatory. Going back to the calculated amount removes the adjustment. Only generated or reviewed. | `Only a generated or reviewed settlement can be adjusted.` |
| **Approve** `POST …/approve` (AAL2) | Reviewed → Approved; the settlement's trips move to **Settled**. | `Only a reviewed settlement can be approved.` |
| **Cancel** `POST …/cancel` (`comment`) | Generated or Reviewed → Cancelled; the reserved attempts and trips are free again. | `Only a generated or reviewed settlement can be cancelled.` |

Body validations (400): `Enter the period (from and to).` · `The period is not valid: the end date cannot be before the start date.` · `The driver does not exist.` · `The note cannot exceed 500 characters.` · of the adjustment: `Enter the adjustment amount.` · `The amount must be between 0 and 1,000,000.00 with at most two decimals.` · `Enter the adjustment reason.` · `The reason cannot exceed 250 characters.`

### Frozen

The settlement stores the formula and the breakdown of each line (delivery and attempts, with its note). **If the rates or the policy change later, settlements already generated are not recalculated.** To recalculate, cancel it and generate another one.

### Frequent cases

- **A delivery came out at $0:** the driver's rate for that service and package type (or that of some attempt) is missing. Complete it in Drivers & rates, cancel the settlement and generate it again; or adjust the line with its reason.
- **“There are N attempts without a driver”:** the route of those orders had no driver. Void the attempt and record it with the driver (§1) and generate again.
- **I cannot find a delivery:** check that it has current attempts in the period, that it is not a special delivery and that it is not already in another settlement.
- **Approving asks to re-authenticate:** it is normal (AAL2), the same as approving a billing run.

## 3. Exporting the settlement (Batch 95)

An **approved** settlement is exported to a file (CSV or TXT) for payroll or accounting, with the same **export template** engine as Billing and the COD remittance: format, delimiter, date format, header and columns in their order. The template scope is **`DRIVER_SETTLEMENT`** and it brings “Standard format” with all the columns the first time.

| Action | Endpoint | Permission |
|---|---|---|
| File preview (changes nothing) | `GET /api/v1/driver-settlements/{id}/export-preview?templatePublicId=` | `driverpay.export` |
| Export: builds the file and moves the approved settlement to **Exported** (who and when) | `POST /api/v1/driver-settlements/{id}/export?templatePublicId=` | `driverpay.export` |
| View the templates and their fields (`?scope=DRIVER_SETTLEMENT`) | `GET /api/v1/export-templates` and `/fields` | `billing.view`, `cod.view` or `driverpay.view` |
| Create, change or deactivate templates | `/api/v1/export-templates` | `billing.export` |

If `templatePublicId` is not sent, the first active template of the scope is used. An **exported** settlement can be exported again (another template, another copy). Error: `Only an approved or exported settlement can be exported.` (422).

**One row per line** of the settlement (by service date first). Available fields: `SETTLEMENT_NUMBER`, `PERIOD_START`, `PERIOD_END`, `DRIVER_CODE`, `DRIVER_NAME`, `CURRENCY`, `LINE_NO`, `LINE_KIND` (delivery or trip), `SERVICE_DATE`, `ORDER_NUMBER`, `DESCRIPTION`, `ATTEMPTS`, `DELIVERY_AMOUNT` (what the delivery or the trip pays), `ATTEMPTS_AMOUNT` (what the attempts pay), `CALCULATED_AMOUNT`, `AMOUNT` (what is paid, with the adjustment if any), `ADJUSTMENT_NOTE`, `PAYOUT_FORMULA` and `RUN_TOTAL`. The file is named `liquidacion-LIQ-#####`. The same file always comes out for the same settlement and template (it is reproducible).

Teikem **does not issue the payment**: the file is taken to payroll or accounting.

## 4. The screen (Batch 96)

All of the above is used from **Accounting → Driver settlement** (two tabs: Settlements and Delivery attempts). How it looks and which button appears with each status and permission: [screen manual, L96](frontend/l96-liquidacion-choferes.md).

The attempts API also returns the **client name** of the order (`clientName`), because the shipment number repeats across clients; the description of a delivery line says “Order {number} · {client}”.
