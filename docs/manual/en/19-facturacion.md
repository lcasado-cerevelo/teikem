# 19 — Billing

Batch 72 (P5, 2026-10-16). **Billing** turns what already went out (dispatched) into invoices to the client, through **runs** that are generated, reviewed, approved and exported.
This chapter describes the runs and invoices of the **product sales** of sales orders ([chapter 18](18-ordenes-de-venta.md)) and, since Batch 74, the **freight of transport
orders** (section 2b) and, since Batch 75, **payments, the statement and accounts receivable** (section 4b). Since Batch 77, the **export to accounting** with templates (section 4c) and, since Batch 79, **credit notes** (section 4d).

## 1. Who can and where

- **Billing** module (`BILLING`; it comes off, it is turned on per company) and, to sell product, the Sales orders module.
- **View** runs and invoices: `billing.view` (new; it was added to the roles that already generate, approve or export billing and to the company administrator).
- **Generate, review, cancel and adjust:** `billing.generate`. **Approve a run, void an invoice and change the settings:** `billing.approve`; approving and voiding also require **recent re-authentication (AAL2)**.
- API: `/api/v1/billing/*` (`settings`, `runs`, `invoices`).

## 2. How it works

1. **Generate** (`POST /billing/runs` with a period and, optionally, a client): the system gathers what was **dispatched and not invoiced** from the sales orders and builds a **draft invoice per client** (and currency).
   Each line is a `PRODUCT_SALE` with the order, the product, the quantity, the order price and the **tax copy** of the order line (in proportion to what is invoiced; the last part takes the exact remainder, so the parts add up to the line's tax).
2. **Review** (`.../review`): marks the run as reviewed. While it is generated or reviewed the amounts can be **adjusted**.
3. **Approve** (`.../approve`, AAL2): the invoices move to **Issued** with today's issue date and a due date according to the company's days.
4. **Cancel** (`.../cancel`): only a generated or reviewed run; its invoices are voided and what they claimed becomes billable again. An approved run is corrected **by voiding its invoices**.

**Nothing is ever invoiced twice.** When generating, each sales order line is locked and the invoiced quantity is recorded (`QtyInvoiced`); two runs at the same time do not invoice the same thing (tested with three simultaneous runs
against SQL Server). The billable part of a line is `dispatched − returned − already invoiced`. Voiding an invoice or cancelling the run releases exactly that.

**What goes into the period.** By default **everything pending up to the end date** goes in (what was dispatched earlier and not invoiced is not lost). With *only within the period* (`onlyInPeriod`), only what was
dispatched between the start and end dates. A kit is invoiced **in kits**.

**Billing basis (company setting).** `ON_DISPATCH` (default, Advance Logistics): billed on dispatch. `ON_DELIVERY` (Island Wide): what carries no delivery (pickup) or whose
transport order is already **Delivered** is billed. It is changed in `PUT /billing/settings` together with the **due days** (30 by default).

## 2b. The freight of transport orders (Batch 74)

The same run also invoices the **freight** of transport orders, with what was **quoted to the client when confirming the order** (`QuotedAmount`). An order goes in when:

- it is active, has a quoted amount greater than zero and its freight has not been invoiced;
- it is in a status that its **contract** already admits according to the **billing model** (the contract's `BillingModel`): **By pickup** (`BY_PICKUP`) from the moment it is picked up (Pickup, Unassigned, Assigned, In transit, Delivered or In warehouse; Batch 106); **By delivery** (`BY_DELIVERY`), **Mixed** (`MIXED`) and orders without a contract or without a model, **only when Delivered** (mixed charges on delivery because it needs both things fulfilled). An order in Failed attempt, Submitted or Cancelled is not invoiced;
- the event (the first time it reached a billable status) falls on or before the run's end date (or within the period with *only within the period*).

**How the lines are built.** The quote is recalculated with the rates in force **on the day it was quoted** and **one line per concept** is invoiced, each with the `RateComponentId` of the rate that produced it:

| Concept | Charge type | Stored rate |
|---|---|---|
| Freight by service and package type | Freight (`FREIGHT`) | the rate per service |
| Extra pieces (graduated tiers) | Freight (`FREIGHT`) | the extra piece rate |
| Dispatch fee | Handling (`HANDLING`) | — (comes from the contract) |
| COD fee | Surcharge (`SURCHARGE`) | — (comes from the contract) |
| Special service (special delivery) | Freight (`FREIGHT`) | — |

If the breakdown **no longer matches** what was quoted to the order (the contract's billing model was changed afterwards, the client was deactivated, the special service was closed…), **a single line “Freight (quoted amount)”** is invoiced with the quoted amount: what the client was told prevails.

**Tax.** Each line goes through the tax engine with its charge type (the rule per charge type decides whether freight, handling and surcharge are taxable) and the client's current exemption; the copy (rate, percentage, amount) stays on the line, calculated on the day the run is generated.

**Never twice.** When generating, each order is locked and `TransportOrder.FreightInvoiced` is set; two runs at the same time do not invoice the same order. Cancelling the run or voiding the invoice releases it. When creating the run you choose **what to bill**: product sales, freight or both (by default both; at least one).

The manual adjustment (section 3) also applies to freight lines.

## 3. The manual adjustment

`PATCH /billing/invoices/{id}/lines/{lineId}` with the new unit amount (from 0 upwards, up to 4 decimals) and the **mandatory reason** (up to 250 characters). The line keeps the **calculated**
amount (`originalUnitAmount`; a second adjustment does not overwrite it), the line's tax is recalculated with its copied rate and the totals of the invoice and the run are updated. It stays in the log.

## 4. Statuses

- **Run:** Generated → Reviewed → Approved → Exported; or Cancelled.
- **Invoice:** Draft → Issued → Paid; Overdue and Void.

## 4b. Payments, statement and accounts receivable (Batch 75)

The screen for all of this is described in [L76 — Collections](frontend/l76-cobros.md).

**Record a payment** (`POST /billing/invoices/{id}/payments`, permission `billing.generate`): amount, **method** (ACH, check, card or cash), **payment date** (today by default; neither in the future nor before the issue date) and **reference** (check or confirmation number, up to 80 characters). Only an **Issued or Overdue** invoice receives payments; the amount is greater than zero, with up to 2 decimals and **does not exceed the balance** (the balance is the total minus the non-voided payments). The invoice is locked when recording, so two payments at the same time do not exceed the balance. When the amount paid covers the total, the invoice moves to **Paid** (also if it was Overdue).

**Void a payment** (`POST /billing/payments/{id}/void`, permission `billing.approve` and **AAL2** re-authentication): requires the reason (up to 250 characters); the payment stays in the history crossed out and no longer counts toward the balance. **The payment of an already Paid invoice cannot be voided** (that status is terminal): a payment recorded there by mistake is corrected with a credit note (it comes with the returns) or with support. Voiding all the payments of an invoice allows voiding it (section 3).

**Due date.** The scheduled job `BILLING_OVERDUE_SCAN` (every hour, with the Billing module on) moves issued invoices whose due date has passed to **Overdue**; the status history records “The payment term expired”. An overdue invoice can still be paid.

**Statement** (`GET /billing/clients/{clientId}/statement?from&to`, `billing.view`): the issued invoices (debit, on the issue date) and the payments (credit, on the payment date) of the period with the **running balance**, the opening balance (what came before the period) and the closing one, the invoiced and paid totals, and the **aging** of what is open today. By default, the last 90 days. Draft and void invoices do not count.

**Accounts receivable** (`GET /billing/receivables?asOf`, `billing.view`): per client, how many open invoices, the balance and its aging in five buckets: **due soon**, **1–30**, **31–60**, **61–90** and **more than 90** days overdue, from the largest to the smallest balance; a client with no balance does not appear. The statement adds the amounts as they are, without separating currencies: it assumes one currency per client.

## 4c. Export to accounting with templates (Batch 77)

The screen is described in [L78 — Export to accounting](frontend/l78-exportacion.md).

An **approved** (or already exported) run is exported to a **CSV or TXT** file with a configurable **template**: the engine does not depend on a fixed format (QuickBooks or another is adjusted with a real sample, only by changing the template).

**Templates** (`/api/v1/export-templates`; view with `billing.view`, create, change and deactivate with `billing.export`): each one has a **name**, the **format** (CSV or TXT), the **delimiter** (comma, semicolon, tab or vertical bar), the **date format** (`YYYY-MM-DD`, `MM/DD/YYYY` or `DD/MM/YYYY`), whether it has a **header** and the **columns in the chosen order**, each with an optional header of its own (if not, the catalog one, in English, as accounting programs require). The first time it is queried, the company receives **“Standard format”** (CSV with comma, ISO date, header and the 23 columns). The name is not repeated among the active ones (ignoring case and accents). A template **is not deleted: it is deactivated**, and the last one cannot be deactivated. The field catalog (`GET /export-templates/fields`) brings the code, the type, the default header and the label in the company's language.

**Billing fields** (one row per invoice line; the invoice and run data are repeated on each line): `INVOICE_NUMBER`, `ISSUE_DATE`, `DUE_DATE`, `CLIENT_CODE`, `CLIENT_NAME`, `CURRENCY`, `LINE_NO`, `CHARGE_TYPE`, `DESCRIPTION`, `ORDER_NUMBER` (the sales order or the transport order), `SKU`, `QUANTITY`, `UNIT_AMOUNT`, `LINE_SUBTOTAL`, `TAX_RATE_PCT`, `TAX_AMOUNT`, `LINE_TOTAL`, `INVOICE_SUBTOTAL`, `INVOICE_TAX`, `INVOICE_TOTAL`, `RUN_NUMBER`, `PERIOD_START`, `PERIOD_END`.

**The file.**
- **Preview** (`GET /billing/runs/{id}/export-preview?templateId=`, `billing.export`): builds the file without changing anything.
- **Export** (`POST /billing/runs/{id}/export` with `templatePublicId`, `billing.export`): builds the same file and, if the run was **Approved**, moves it to **Exported** (with who and when, and the status history). An exported run can be exported again (another template, another copy): it does not change status again.
- **Reproducible:** the same run with the same template always gives the same text (invoices by number, lines in order). The **void** invoices of the run are not included.
- **Format:** money with 2 decimals and a dot; quantities and unit amounts with up to 4 decimals without trailing zeros; CRLF line endings; UTF-8. In **CSV**, whatever contains the delimiter, quotes or line breaks goes between quotes (quotes are doubled); in **TXT** there are no quotes: the delimiter and the line breaks inside a value are replaced with a space. Fields that do not exist in the catalog are left out.
- File name: `facturacion-corrida-{número}.csv` or `.txt`.

## 4d. Credit notes (Batch 79)

The screen is described in [L80 — Credit notes](frontend/l80-notas-de-credito.md).

A **credit note** (`NC-#####`, one counter per company) credits the client part or all of what was invoiced: a **return**, a **price adjustment**, a **service failure** or a **billing error** (`CreditReason` catalog). Statuses: **Draft → Approved → Applied**, or **Void**. **Approving it does not move inventory** (that is done by receiving the return); what is not applied to invoices remains as the client's **credit balance**.

**Permissions** (`/api/v1/billing/credit-notes`): view with `billing.view`; create, change, apply and void with **`billing.credit`** (new permission: it was added to the roles that already approve billing and to the company administrator, also in the roles already cloned); **approving** also requires **recent re-authentication (AAL2)**.

**Create the note** (`POST`, as a draft):
- **On a Teikem invoice** (issued, overdue or paid): each line indicates the **invoice line** it credits (`invoiceLineId`). By default it credits the whole line at its amount; with `quantity` and `unitAmount` less is credited (a price adjustment is crediting a smaller unit amount). **No more than what was invoiced on that line is credited**, deducting what the **approved or applied** notes already credited (the cap is checked again when approving, under invoice lock, so two draft notes do not credit twice).
- **Tax:** that of the original line (the copy: rate and exemption), proportional to what is credited and never more than what was charged.
- **Without a Teikem invoice** (an external invoice from another system or an adjustment): the **client** is needed (and, optionally, `externalInvoiceRef`); the lines are free (description, quantity, unit amount and charge type, or a product) and carry the tax of today's **tax engine**.
- With the note in **draft** the reason, the note and the lines can be **changed** (`PUT` replaces them all); the client and the invoice do not change.

**Apply** (`POST .../apply` with the invoice and the amount): deducts from the balance of an **issued or overdue** invoice of the **same client**, up to the lesser of the note's balance and the invoice's (up to 2 decimals). It is like a payment: **the invoice moves to Paid if it reaches zero**, and the note moves to **Applied** when it is used up. The invoice record shows the **credited** amount and its balance already deducts it (payments + applied notes), and the payment of an invoice cannot exceed that balance.

**Void** (`POST .../void`, with a reason): a draft or approved note **without applications**. An **invoice with approved or applied notes cannot be voided** (void the note first, if it was not applied). A **Paid** invoice is now corrected with a credit note.

**Statement and receivables.** The client's statement brings the approved notes as credit movements (with their running balance), the **credit balance** (`unappliedCredit`: what is approved and has not been applied) and the aging already deducts what was applied. Accounts receivable brings the **credit balance** column per client.

## 5. Validations and messages

| Situation | Message | HTTP |
|---|---|---|
| Generating without dates | `Specify the run period (start and end date).` | 400 |
| End date before the start date | `The end date cannot be before the start date.` | 400 |
| Note longer than 500 characters | `The note cannot exceed 500 characters.` | 400 |
| Nothing to bill | `There is nothing to bill with that data.` | 422 |
| Neither product nor freight chosen | `Choose what to bill: product sales, freight or both.` | 400 |
| Reviewing a run that is not generated | `Only a generated run can be reviewed.` | 422 |
| Approving without reviewing | `Review the run before approving it.` | 422 |
| Cancelling an approved or exported run | `Only a generated or reviewed run can be cancelled; an approved one is corrected by voiding its invoices.` | 422 |
| Adjusting outside a generated or reviewed run | `Only an invoice of a generated or reviewed run can be adjusted.` | 422 |
| Adjustment without amount / invalid amount | `Specify the new unit amount.` / `The unit amount must be a number from 0 upwards with up to 4 decimals.` | 400 |
| Adjustment without reason / long reason | `Enter the reason for the adjustment.` / `The adjustment reason cannot exceed 250 characters.` | 400 |
| Adjustment to the same amount | `The unit amount is the same; there is nothing to adjust.` | 422 |
| Voiding an invoice that is neither issued nor overdue | `Only an issued or overdue invoice can be voided.` | 422 |
| Voiding an invoice with payments | `The invoice has recorded payments: it cannot be voided.` | 422 |
| Invalid billing basis | `The billing basis must be ON_DISPATCH (on dispatch) or ON_DELIVERY (on delivery).` | 400 |
| Due days outside 0–365 | `The due days must be between 0 and 365.` | 400 |
| Payment without amount / invalid amount | `Specify the payment amount.` / `The payment amount must be greater than zero and have up to 2 decimals.` | 400 |
| Payment without method / nonexistent method | `Specify the payment method.` / `The payment method does not exist.` | 400 |
| Payment date in the future / before the issue date | `The payment date cannot be in the future.` / `The payment date cannot be before the invoice issue date.` | 400 |
| Reference longer than 80 characters | `The reference cannot exceed 80 characters.` | 400 |
| Payment of an invoice that is neither issued nor overdue | `Payments are only recorded for an issued or overdue invoice.` | 422 |
| Payment greater than the balance | `The payment (X) is greater than the invoice balance (Y).` | 422 |
| Voiding a payment without reason / long reason | `Enter the reason for voiding the payment.` / `The reason cannot exceed 250 characters.` | 400 |
| Voiding an already voided payment | `The payment is already void.` | 422 |
| Voiding the payment of a paid invoice | `The invoice is already fully paid: a payment recorded by mistake is corrected with a credit note or with support.` | 422 |
| Statement with an end date before the start date | `The end date cannot be before the start date.` | 400 |
| Payment or client does not exist | `Payment not found.` / `Client not found.` | 404 |
| Exporting or viewing the file of a run that is neither approved nor exported | `Only an approved or exported run can be exported.` | 422 |
| Template without name / long name / repeated name | `Enter the template name.` / `The name cannot exceed 80 characters.` / `A template with that name already exists.` | 400 / 400 / 409 |
| Invalid format, delimiter or date format | `The format must be CSV or TXT.` / `The delimiter must be comma, semicolon, tab or vertical bar.` / `The date format must be YYYY-MM-DD, MM/DD/YYYY or DD/MM/YYYY.` | 400 |
| Template without columns / with too many | `Choose at least one column.` / `The template cannot have more than 40 columns.` | 400 |
| Column that does not exist / repeated / long header | `One of the columns is not an exportable field.` / `A column is repeated in the template.` / `A column header cannot exceed 60 characters.` | 400 |
| Nonexistent template scope | `The template scope does not exist.` | 400 |
| Deactivating the only template | `The only template of the scope cannot be deactivated: leave at least one.` | 409 |
| Template does not exist (or belongs to another company) | `Export template not found.` | 404 |
| Note without reason / nonexistent reason | `Specify the credit note reason.` / `The credit note reason does not exist.` | 400 |
| Note without lines / with more than 200 | `Add at least one line to the credit note.` / `The credit note cannot have more than 200 lines.` | 400 |
| Note longer than 500 characters / external reference longer than 80 | `The note cannot exceed 500 characters.` / `The external invoice reference cannot exceed 80 characters.` | 400 |
| Note without client (and without invoice) | `Specify the client of the credit note.` | 400 |
| Invoice of another client | `The invoice does not belong to that client.` | 400 |
| Draft or void invoice | `Only an issued, overdue or paid invoice can be credited.` | 422 |
| Line without the invoice line / not of that invoice / in a note without invoice | `Each line of a note on an invoice must indicate the invoice line it credits.` / `The line does not belong to that invoice.` / `A note without an invoice does not credit invoice lines.` | 400 |
| Free line without description / long / without charge type / nonexistent type | `Enter the line description.` / `The description cannot exceed 250 characters.` / `Specify the line's charge type.` / `The charge type does not exist.` | 400 |
| Invalid quantity or amount | `The quantity must be greater than zero (up to 3 decimals).` / `The unit amount must be greater than zero (up to 4 decimals).` | 400 |
| Crediting more than what was invoiced | `You cannot credit more than what was invoiced: “X” has Y left to credit.` | 422 |
| Changing a note that is not a draft / approving one that is not | `Only a draft credit note can be changed.` / `Only a draft credit note can be approved.` | 422 |
| Voiding without reason / long reason / note already closed / with applications | `Enter the reason for voiding the credit note.` / `The reason cannot exceed 250 characters.` / `Only a draft or approved note can be voided.` / `The note has already been applied to an invoice: it cannot be voided.` | 400 / 400 / 422 / 422 |
| Applying a note that is not approved or has no balance | `Only an approved credit note with a balance can be applied.` | 422 |
| Applying without amount / invalid amount | `Specify the amount to apply.` / `The amount to apply must be greater than zero and have up to 2 decimals.` | 400 |
| Applying to a closed invoice or one of another client | `It can only be applied to an issued or overdue invoice with a balance.` / `The note and the invoice must belong to the same client.` | 422 |
| Amount greater than the balance of the note or the invoice | `The amount (X) is greater than the note balance (Y).` / `The amount (X) is greater than the invoice balance (Y).` | 422 |
| Voiding an invoice with credit notes | `The invoice has approved or applied credit notes: it cannot be voided.` | 422 |
| Note does not exist | `Credit note not found.` | 404 |
| Approving or voiding without recent re-authentication | `This action requires recent re-authentication (AAL2).` | 403 |
| Run / invoice / line does not exist | `Billing run not found.` / `Invoice not found.` / `Invoice line not found.` | 404 |

## 6. For support

- Tables `BillingRun`, `Invoice`, `InvoiceLine` and `Payment` (those of the original design) plus the columns of section **2026-10-16 (r)** of `Diseño/logistica-db-update.sql`: `BillingRun.ClientId/Note/ReviewedBy/ReviewedAtUtc/ExportedAtUtc/ExportedBy`,
  `Invoice.Notes`, `InvoiceLine.SalesOrderLineId/OriginalUnitAmount/AdjustmentNote`, `Tenant.SalesBillingTrigger/InvoiceDueDays`; in section **2026-10-16 (s)** (Batch 74) `TransportOrder.FreightInvoiced`; in **(t)** (Batch 75) `Payment.RecordedBy/VoidedAtUtc/VoidedBy/VoidReason`; in **(u)** (Batch 77) the `ExportTemplate` table and the `EXPORT_TEMPLATE` EntityType; in **(v)** (Batch 79) the `CreditNote`, `CreditNoteLine` and `CreditApplication` tables, the `CreditNoteStatus` status domain, the `CreditReason` catalog, the `CREDIT_NOTE` EntityType, the `CREDITNOTE` counter (NC-#####) and the `billing.credit` permission; the `BILLINVOICE` counter (**FAC-#####**, one per company), the
  **Cancelled** status of the run, the `BILLING_RUN` EntityType and the `billing.view` permission.
- `InvoiceLine.LineTotal` and `Invoice.Total` are computed columns of the database; the system adds up the lines.
- The status history of the run and of each invoice is in `EntityStatusHistory` (with the cancel/void comment).
