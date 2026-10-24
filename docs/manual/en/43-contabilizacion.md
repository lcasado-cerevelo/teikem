# 43 — Purchase accounting and dispatch accounting

Batch 181 (2026-10-21). **Accounting** takes to your accounting system **the cost of your own goods that entered and left the warehouse**. There are two screens under **Accounting**: **Purchase accounting**
(what came in, by confirmed receipts) and **Dispatch accounting** (what went out, by pick batches and dispatches). Each one works by **runs**: it collects the period's lines, you review the run,
**accept** it and **export** it with your company's template. Teikem **collects and exports**; it does not post the journal entry in your accounting system.

> **Rule agreed with the owner (to be confirmed).** What is accounted for is **what belongs to the company**: the **own** inventory (products without an owner client). What belongs to a **client** (3PL) is **not** accounted for, no
> matter the document it arrived with (purchase order, arrival notice or blind receipt). The **already frozen cost** of each line is used (the receipt's cost layer, Batch 147; the cost of the issue, Batch 148): nothing is recalculated.

## 1. Who can

| What | Purchases | Dispatches |
|---|---|---|
| Module | Purchases (`PURCHASING`) | Lot and serial inventory (`WMS_LOTSERIAL`) |
| Open the screen | `purchaseaccounting.view` | `dispatchaccounting.view` |
| See runs | `purchasing.view` | `billing.view` |
| Generate, accept and cancel | `purchasing.manage` | `billing.generate` |
| Export and configure the template | `purchasing.manage` | `billing.export` |

The export templates (Accounting → the same “Configure…” window as Billing) are read with any of the view permissions and changed with the export permission of the type. The Company admin
and Billing already have these permissions. The templates require **any** of the Billing, COD, Driver settlement, Purchases or Lot inventory modules to be turned on.

## 2. What goes into each run

**Purchases** — one line per line of a **confirmed receipt** (with receiving date) in the period:
- only **own** products; a client's goods are left out;
- it counts what was **received** (damaged goods too: they cost the same);
- sales returns do **not** go in (they are not a purchase);
- the cost is the one the line **froze when the receipt was confirmed**: the one from the purchase order line, otherwise the arrival notice price, otherwise the product cost (“Cost source” column: `PO`, `ASN` or `PRODUCT`). With none of them the line goes **without cost** (blank) and the run warns about it;
- a receipt that is **closed in batches** (Batch 174) counts when the receipt is closed, not batch by batch.

**Dispatches** — one line per line of a **current pick batch or dispatch** in the period (includes the manual issue and the dispatches against a sales order or exit notice):
- only own products;
- **deleted** pick batches and **reversed** lines do not go in;
- the cost is the one the issue **froze** when taken out (according to the company's valuation method).

**The period** is company days (time zone from Settings → Region & formats): “from” and “to” inclusive. If there is nothing of your own to account for, the server responds 422 and the screen does not let you generate.

**A line is never in two live runs.** What is already in a generated, accepted or exported run is not offered again; when a run is **cancelled**, its lines become free.

## 3. Screen

**Accounting → Purchase accounting / Dispatch accounting** (`/money/purchases`, `/money/dispatch`). The list shows number (`CMP-#####` / `DSP-#####`), period, lines, lines **without cost**, total, status and when it was generated; filter by status, search box by number and full export of the table.

1. **Generate run**: choose “From” and “To” (by default, the last 30 days) and an optional note. Before generating you see **what would go in**: total, number of lines, warning about lines without cost and the detail (up to 200 lines on screen; the file has them all). **Generate** creates the run (it becomes **Generated**) and opens its record.
2. **The record** shows the dates, who generated, accepted and exported it, the total and the lines (date, receipt or pick batch, order or notice, supplier or client, product with lot, quantity, unit cost and total; lines without cost are marked).
3. **Accept** freezes the run (Generated → Accepted).
4. **Export** (only accepted or exported): choose the **template**, adjust it with **Configure…** (CSV/TXT format, delimiter, date format, header, columns and their order), look at the file with **View file** and **Download**. The **first download** moves the run to **Exported**; it can be downloaded again (another template, another copy).
5. **Cancel run** (generated or accepted): its lines become free for another run.

## 4. Statuses and transitions

| From → to | Who | Effect | What blocks it |
|---|---|---|---|
| (new) → **Generated** | manager of the type | collects the period's lines with their frozen cost and takes the number | nothing to account for (422) |
| Generated → **Accepted** | manager | it becomes frozen; saves who and when | only from Generated |
| Accepted → **Exported** | exporter | first download of the file; saves who and when | only from Accepted |
| Generated or Accepted → **Cancelled** | manager | the lines become free | an exported run cannot be cancelled |

## 5. Export file

One row per line, with the run's data repeated. Each scope (`PURCHASE_ACCOUNTING`, `DISPATCH_ACCOUNTING`) has its field catalog and its “Standard format” created the first time:
run, period, line, date, document, order or notice, supplier or client, warehouse, product, lot, quantity, unit cost, total cost, cost source (purchases only), currency and run total. Money with two decimals and a period; the unit cost with up to four. The same file with the same template always comes out the same.

## 6. Endpoints

Purchases: `/api/v1/purchase-accounting`. Dispatches: `/api/v1/dispatch-accounting`. Same shape:

| What | Endpoint |
|---|---|
| List (`status`, `q`, `skip`, `take`) | `GET /api/v1/{purchase\|dispatch}-accounting` |
| Record with lines | `GET /api/v1/{…}-accounting/{publicId}` |
| See what would go in (does not save) | `POST /api/v1/{…}-accounting/preview` with `{periodStart, periodEnd}` |
| Generate | `POST /api/v1/{…}-accounting` with `{periodStart, periodEnd, note?}` |
| Accept | `POST /api/v1/{…}-accounting/{publicId}/accept` |
| Cancel | `POST /api/v1/{…}-accounting/{publicId}/cancel` with `{comment?}` |
| File without changing anything | `GET /api/v1/{…}-accounting/{publicId}/export-preview?templatePublicId=` |
| Export | `POST /api/v1/{…}-accounting/{publicId}/export?templatePublicId=` |

A purchase run is not visible from dispatches nor the other way around (404 “Run not found.”).

## 7. Error messages

| Message | HTTP | When |
|---|---|---|
| “The request body is required.” | 400 | the request was sent without a body |
| “Enter the period (from and to).” | 400 (`periodStart`) | one of the two dates is missing |
| “The period is not valid: the end date cannot be earlier than the start date.” | 400 (`periodEnd`) | “to” before “from” |
| “The note cannot exceed 500 characters.” | 400 (`note`) | note too long |
| “Nothing to account for in the period.” | 422 | there are no free own lines in the period |
| “Only a generated run can be accepted.” | 422 | accepting one that is no longer Generated |
| “Only a generated or accepted run can be cancelled.” | 422 | cancelling an exported or already cancelled one |
| “Only an accepted or exported run can be exported.” | 422 | exporting a generated or cancelled one |
| “Run not found.” | 404 | it does not exist or belongs to the other type |
| “The run changed while saving. Review and try again.” | 409 | another person changed it at the same time |
| “Export template not found.” · “The template is not a billing template.” | 404 · 400 | template does not exist or belongs to another scope |

## 8. Frequent cases

- **“My purchases come out with lines without cost”.** The receipt's layer had no cost (neither the purchase order, nor the notice, nor the product had one when confirming). They are accounted for anyway, with a blank cost, so that accounting completes them. Setting the cost on the product **afterwards** does not change an already frozen layer.
- **“I deleted a pick batch that was already in a run”.** The run does not correct itself (what was posted was posted): cancel the run if it is not exported yet and generate it again. If it was already exported, the adjustment is made in the accounting system.
- **“I received the client's goods and they do not show up”.** That is the rule: a client's goods are not accounted for.
- **“I want to export again with another format”.** Open the record of the exported run → Export → another template.
