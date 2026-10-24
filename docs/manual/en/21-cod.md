# 21 — COD: collection on delivery and reconciliation

Batch 84 (P8, 1 of 3, 2026-10-17). The courier collects the COD from the consignee on delivery, holds it and **reconciles** it against the sheets (invoices) that come back from the street; afterwards it is **remitted** to the client minus the commission (remittance, following batches). This batch builds the **collection** and the **reconciliation**, including reconciliation by **scanned sheets** (the sheet's barcode is the invoice number). **COD** module (`COD`). API: `/api/v1/cod`.

## 1. Who can and where

| Action | Permission |
|---|---|
| View shipments with COD, their detail and the reconciliation runs | `cod.view` |
| Record a collection (the driver will also do it from the app, P7) | `cod.collect` |
| Reconcile what was collected by codes | `cod.reconcile` |

The remittance to the client (`cod.remit`) comes in the next batch.

## 2. The collection

- A shipment with COD is born with the status **To collect** (`PENDING`) and the amount in `CodAmount`. **`POST /cod/orders/{publicId}/collections`** records a collection: `method` (`CASH`, `CHECK`, `COMPANY_CHECK`, `ATH_MOVIL`, `CARD`), `amount` (greater than zero, 2 decimals), `checkNumber` (mandatory for check and company check), `reference` (ATH Móvil, deposit…), `notes` and, if omitted, the date is now.
- **Several partial collections** can be recorded; the sum cannot exceed what is left to collect. The shipment's COD moves to **Partial** and, when completed, to **Collected** (collecting everything at once goes through Partial in the history). No collection is made on a draft or cancelled shipment.
- Each collection stays in **Collected** until it is reconciled. The log records who collected it.

## 3. The reconciliation (PWBack)

**`POST /cod/reconcile`** receives a list of **codes** and, for each one, reconciles what was collected for its order: the collections move to **Reconciled** and, if the shipment was already fully collected, its COD moves to **Reconciled**. If the shipment is only partially collected, what was collected is reconciled but the shipment's COD stays in Partial; whatever is collected later remains to be reconciled.

- **Where the codes come from:** from the **barcode of the scanned sheet** (the client's invoice number, or the shipment's) or typed by hand. Reading the barcode from the files (PDF or image) is done by the screen in the browser (next batch); the API only receives the codes, each with its `source` (file and page).
- **How the shipment is found:** first by **shipment number**, and if not, by **client invoice number**. The invoice is counted **per client** (`FAC-00001` exists in each client), so:
  1. with `clientPublicId` in the request (the sheets belong to that client) only its shipments are searched;
  2. without it, if the code matches several shipments it keeps the ones with COD and, if there are still several, the ones with collections to reconcile; if it is still not a single one, the result is **ambiguous**.
- **`dryRun: true`** only says what would happen (preview): it reconciles nothing and does not save the run.
- Each run is saved with its codes and results (`GET /cod/runs`, `GET /cod/runs/{id}`): who, when, the source and how many were reconciled. A code that gives a problem **does not stop** the others.

### Results per code

| Result | What it means | What to do |
|---|---|---|
| `RECONCILED` | The shipment's collections were reconciled (`Reconciled: N collection(s) for X.`) | — |
| `ALREADY_RECONCILED` | `It was already reconciled.` | Nothing: the sheet had already been processed |
| `NOT_FOUND` | `There is no shipment with that invoice or shipment number.` | Check the code read; try by hand |
| `AMBIGUOUS` | `The code matches more than one shipment: reconcile it by hand from the shipment.` | Specify the client of the sheets or reconcile by hand |
| `NO_COD` | `The shipment has no COD.` | Check the sheet |
| `NOTHING_COLLECTED` | `The shipment has COD, but no collection has been recorded yet.` | Record the collection first |
| `DUPLICATE` | `The code already came earlier in this run.` | The same sheet twice: it is ignored |

## 3b. Validations and messages

| Situation | Message | HTTP |
|---|---|---|
| No method / nonexistent method | `Specify the collection method.` / `The collection method does not exist.` | 400 |
| Invalid amount | `The amount must be greater than zero (up to 2 decimals).` | 400 |
| Check without number / long number | `Specify the check number.` / `The check number cannot exceed 40 characters.` | 400 |
| Long reference or note | `The reference cannot exceed 80 characters.` / `The note cannot exceed 300 characters.` | 400 |
| Shipment without COD | `The shipment has no COD.` | 422 |
| Draft or cancelled shipment | `A collection is not recorded on a draft or cancelled shipment.` | 422 |
| Collection greater than what is pending | `The collection exceeds what is left to collect for {order}: {amount}.` | 422 |
| Reconciling without codes / empty code | `Specify at least one code.` | 400 |
| Long code / more than 2000 codes | `A code cannot exceed 80 characters.` / `A run cannot have more than 2000 codes.` | 400 |
| Long source | `The source cannot exceed 300 characters.` | 400 |
| Client does not exist | `The client does not exist.` | 400 |
| Shipment or run does not exist | `Shipment not found.` / `Run not found.` | 404 |

## 4. For support

- Table `CodCollection` (it was already in the original schema) with the columns `CollectedBy`, `ReconciledBy` and `ReconcileRunId`; tables `CodReconcileRun` and `CodReconcileItem`; EntityType `COD_COLLECTION`: section **2026-10-17 (x)** of `Diseño/logistica-db-update.sql`.
- The formal cycle with history is that of the **shipment's COD** (`EntityType ORDER_COD`: To collect → Partial → Collected → Reconciled → Remitted). The status of each collection (`CodCollectionStatus`) changes without a history of its own.
- **Voiding a collection** and **recording from the driver app with the POD** (P7) are outside this batch.

## 5. On the screen (Batch 85)

**Accounting → Process deliveries** ([L85](frontend/l85-cod.md)): shipments with COD and recording of collections; **Reconcile** by uploading the scanned sheets (PDF or images). The **browser** reads the barcode of each sheet (pdf.js and zxing-wasm, served with the application itself; the files **are not uploaded**) and only the codes with their source (file and page) reach the API; preview (`dryRun`), reconcile with confirmation and previous runs. Pages where no code was found are reported so they can be checked by hand. If the client of the sheets is chosen, `clientPublicId` is sent (see §3).

## 6. The remittance to the client (Batch 86)

The **remittance** (`REM-#####`) gives back to the client the money that was collected for them on the street, minus the **commission**. **Teikem calculates and exports; it does not issue the payment.** API: `/api/v1/cod/remittances`. View with `cod.view`; everything else with **`cod.remit`**.

### The flow

**Open → Reconciled → Approved → Remitted.** An open or reconciled one can be **cancelled**.

1. **Candidates** (`GET /cod/remittances/candidates?clientPublicId=&periodStart=&periodEnd=&commissionPct=`): what would go in (without creating anything) and the commission that would apply.
2. **Create** (`POST /cod/remittances`: client and period): groups the **reconciled** collections of that client's shipments whose **collection date** falls within the period (in the company's time zone) and that are not yet in another remittance. **Each collection goes into only one remittance at a time.** What was collected but not reconciled does not go in.
3. **Reconcile** (`POST .../review`): the reconciliation was reviewed. **Approve** (`POST .../approve`): requires **re-authentication (AAL2)** like approving a billing run; who and when is recorded. **Remit** (`POST .../remit`, with an optional payment reference, e.g. the check number): the collections move to **Remitted** and, when all the collections of a shipment have been remitted, the **shipment's COD moves to Remitted** (terminal).
4. **Cancel** (`POST .../cancel`): only open or reconciled; the remittance is deactivated (it leaves the list) and its collections are free for another remittance.

### The commission

For each collection: **amount × % ÷ 100**, rounded to 2 decimals (half up); **net = gross − commission**. The % is, in this order: the one requested when creating (`commissionPct`); that of the client's **active contract** (`CodCommissionPct`); the **company default** (`GET/PUT /cod/settings`, 8 % out of the box; changed by whoever has `cod.remit`). The remittance stores the % it used. (Not to be confused with the billing *COD fee*, which is a charge from Advance to the client for the service.)

### Export

`GET /cod/remittances/{id}/export?templatePublicId=` builds the file of an **approved or remitted** remittance: **one row per collection**, with the remittance data repeated, with a template of the `COD_REMITTANCE` scope (the same template engine as billing: CSV or TXT, delimiter, date format, columns and headers). Without a template the first active one is used (**Standard format**, all the columns, created automatically). Templates are managed in `/export-templates?scope=COD_REMITTANCE` (with the billing permissions). Fields: remittance number, period, client, currency, shipment, invoice, date, method, check, reference, amount collected, commission (% and amount), net and the remittance totals.

### Validations and messages

| Situation | Message | HTTP |
|---|---|---|
| No client / nonexistent client | `Specify the client of the remittance.` / `The client does not exist.` | 400 |
| No period / inverted period | `Specify the remittance period (from and to).` / `The period is not valid: the end date cannot be before the start date.` | 400 |
| Commission outside 0–100 or with more than 2 decimals | `The commission must be between 0 and 100 (up to 2 decimals).` | 400 |
| Invalid default commission | `The default commission must be between 0 and 100 (up to 2 decimals).` | 400 |
| Nothing to remit | `There are no reconciled collections of that client in that period to remit.` | 422 |
| Reconciling one that is not open | `Only an open remittance can be reconciled.` | 422 |
| Approving one that is not reconciled | `Only a reconciled remittance can be approved.` | 422 |
| Remitting one that is not approved | `Only an approved remittance can be remitted.` | 422 |
| Cancelling an approved or remitted one | `Only an open or reconciled remittance can be cancelled (an approved or remitted one no longer).` | 422 |
| Exporting one that is neither approved nor remitted | `Only an approved or remitted remittance can be exported.` | 422 |
| Long payment reference | `The reference cannot exceed 80 characters.` | 400 |
| Approving without re-authentication | `This action requires recent re-authentication (AAL2).` (`aal2_required`) | 403 |
| Remittance does not exist (or is cancelled) | `Remittance not found.` | 404 |

### For support

- Tables `CodRemittanceBatch` and `CodRemittanceLine` (they were already in the original schema; `NetRemitted` and `NetAmount` are computed columns); `Tenant.CodDefaultCommissionPct`; EntityType `COD_REMITTANCE`; counter `CODREMIT` and its CHECK: section **2026-10-17 (y)** of `Diseño/logistica-db-update.sql`.
- A **cancelled** remittance stays in the database with `IsActive = 0` and its lines (it is not deleted); its collections are left without a remittance again.
- The **contract commission** is read from `Contract.CodCommissionPct`; there is no field yet to edit it on the contracts screen (it can be written through the database or the API); the remittances screen (Batch 87) shows where the applied % comes from.

The **Remittances tab** of the same screen ([L87](frontend/l87-remesas.md)) covers §6: list, new remittance with the candidates and the commission, record with the flow, export and the default commission.

## 7. Processing the client's invoice (“second check”, Batch 126)

> Dictated by Luis (2026-10-18). In the old system, to “process” the client's invoice they created a fictitious delivery shipment whose piece was the invoice. **In Teikem there are no fictitious shipments:** the client's invoice travels with the shipment (`ClientInvoiceNumber`; if it does not come, a label is stuck on it) and, once the shipment has been delivered, the invoice is **processed** against it as a check.

**Where:** the same **Process deliveries → Reconcile** screen (COD). The scanned sheets (PDF or images) are uploaded or the numbers are typed, the same as for COD. Permission `cod.reconcile`, module `COD`.

**What are you uploading?** A selector at the start: **Detect** (default), **COD sheets**, **Invoices** or **Both**. The system **always processes everything it read**: for each sheet it looks up its order and classifies it itself:
- the shipment **has COD** → it is a **COD sheet**: it reconciles the collection (as always) **and** marks the shipment's invoice as processed;
- the shipment **has no COD** → it is an **invoice**: it checks it against the delivered shipment and marks it as processed.

The selector only says what you expected, and that is where the **warnings** come from (informational, nothing is discarded): “You said COD sheets, but some belong to shipments without COD: I processed them as invoices” · “You said invoices, but some are sheets of shipments with COD: I squared them as COD” · “You said both, but no invoices of shipments without COD came in” · “You said both, but no COD sheets came in”. With **Detect** there are no warnings.

**New results per sheet** (in addition to the COD ones): **Invoice processed** (`INVOICE_CHECKED`: the shipment is delivered; in the preview, “would be processed”), **Undelivered** (`NOT_DELIVERED`: the shipment exists but has not been delivered; it is processed when it is delivered) and **Already processed** (`ALREADY_PROCESSED`: that shipment already had its invoice processed in another run). The **Type** column says whether the sheet was COD or invoice. The summary counts “N invoice(s) processed” apart from what was reconciled, and **Reconcile** is enabled if there is something to reconcile or some invoice to process.

**The client of the sheets is mandatory to process invoices:** the invoice number repeats across clients, so if the preview brings invoices and the client was not chosen, “Reconcile” asks to choose it (“To process invoices, choose the client of the sheets…”). For COD sheets it is still optional.

**What stays on the shipment:** `InvoiceProcessedAtUtc`, `InvoiceProcessedBy` and `InvoiceProcessRunId` (the reading run, with the file and the row of each sheet). **Only once per order**: the second sheet of the same shipment comes out as “Already processed” and changes nothing. The shipment record shows **Invoice processed: date · who** (or “Not yet”) in the COD panel.

**Configurable charge (contract → Invoice check):** off by default. Turned on with an amount, each processed invoice (check or COD sheet) generates in the **next billing run** a line **“Invoice check of shipment X”** (charge type `INVOICE_CHECK`, with tax according to the client's tax profile), only once per order. Without a contract or with the charge off, processing costs nothing.

| Method and route | Permission | What it does |
|---|---|---|
| `POST /api/v1/cod/reconcile` (`mode`: AUTO · COD · INVOICE · BOTH) | `cod.reconcile` | Reads the codes; returns `items[].kind`, `checked` and `warnings[]`. 400 “The sheet type must be AUTO, COD, INVOICE or BOTH.” |
| `GET /api/v1/cod/runs/{publicId}` | `cod.view` | The detail brings `kind` per sheet and `checked`. |
| `PATCH /api/v1/contracts/{publicId}/invoice-check-fee` (`enabled`, `amount`) | `contracts.update` | 400 “Enter the amount per checked invoice.” · “The check amount cannot be negative.” |
