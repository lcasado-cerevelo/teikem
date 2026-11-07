# 49 — Invoices to QuickBooks (IIF file)

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter `docs/manual/49-quickbooks.md`. The server only answers in Spanish, so every "Exact message" column keeps the **exact Spanish text** the screen shows, followed by its English meaning.

Batch 225 (2026-11-07) — salespeople's order, part P5. The office passes the **issued sales invoices** to **QuickBooks Desktop** with an **IIF** file that Teikem builds, and keeps a record of which ones already went in. There is no direct connection with QuickBooks: Teikem downloads the file and the office imports it. The screen is in **Accounting → Sales orders → «QuickBooks» tab** (`/sales-orders?tab=quickbooks`); accounts and names are set in **System → Settings → Operations → QuickBooks**. Invoices come from sales billing ([19](19-facturacion.md)) and from the order ([18](18-ordenes-de-venta.md)).

> Screenshots pending (P8): the QuickBooks tab (list, notice «N of these already went out…»), the «Export to QuickBooks» window, the «Did you already import these N?» panel, «see data» of a new client, the «Already in QuickBooks» history and Settings → QuickBooks.

## 1. Who can and where

- Module **Sales orders (`SALES_ORDERS`)**.
- Three permissions, all imply `sales.view`; the **Sales office** role and the Company admin hold them:
  - **`sales.quickbooks.view`** — see the invoices to pass, «see data» and the history; receives the notice, the Pulse figure and the «Needs your attention» row.
  - **`sales.quickbooks.export`** — **Export to QuickBooks** and confirm «Yes, they are in».
  - **`sales.quickbooks.mark`** — **Mark as in QuickBooks** (one, several or all) and remove the mark.
- The tab shows with **any** of the three (whoever marks or exports needs to see the list). The list and the history are read by the three permissions.
- **Settings → Operations → QuickBooks**: read with `sales.quickbooks.view` or `admin.tenant`; changed with **`admin.tenant`**.

## 2. Which invoice is passed

**To pass** = an **issued** sales invoice (Issued, Overdue or Paid; not the draft nor the voided one) that carries at least one sales order line or its shipping charge, and that **does not yet have the «in QuickBooks» mark**. A transport-only invoice (freight) does not appear. If the client's same invoice also carries freight or other transport charges, those lines go out with the charge type code (`FREIGHT`, `HANDLING`…) as the item, which must exist in QuickBooks as a service item.

**A single mark.** Each invoice has one «in QuickBooks» mark (yes or no; with who and when). **Exporting does not mark it**: it is marked by whoever confirms «Yes, they are in» after importing, or whoever marks it by hand. Exporting **always** takes **all** the invoices to pass, whether they went out before or not.

## 3. How to use it (screen)

1. The **QuickBooks** tab lists the invoices to pass: date, invoice (with its orders), client (and its name in QuickBooks if different), total, **New client** / **New product** (with «see data») and «Went out on {date}» if it already went out in a file. Search by invoice, client or order. On top, the server's **warnings** and, if it applies, «{n} of these already went out on {date} in a file and have not been confirmed. If they are already in QuickBooks, mark them; if not, they go out again in the next file.»
2. **Export to QuickBooks** (`sales.quickbooks.export`): the window says how many invoices go out and their total, and asks **«Include the new clients and products»** (checked by default; uncheck it if you already added them by hand in QuickBooks). **Export and download the file** downloads `teikem-quickbooks-YYYYMMDD-HHmm.iif`.
3. On the same screen the **«Did you already import these N?»** panel appears with the list checked. Import the file in QuickBooks (**File → Utilities → Import → IIF Files**), **uncheck the ones QuickBooks did not accept** (they will go out again in the next file) and press **Yes, they are in (N)**. **Download the file again** and **Not yet** are there too. If you leave the tab, the invoices stay to pass with «Went out on …».
4. **Mark as in QuickBooks** (`sales.quickbooks.mark`): checkboxes + «Mark as in QuickBooks (N)», each row's action, or **Mark all** (asks for confirmation). Use it when they are already in QuickBooks some other way.
5. **See the ones already passed** opens the «Already in QuickBooks» history (invoice, client, total, **who** and **when**, last file; filter by the mark's day and search box) with **Remove the mark** (`sales.quickbooks.mark`) to fix a mark put by mistake: the invoice goes back to «to pass»; its clients and products do **not** go out as new again.

**How to use it (API)** (`/api/v1/sales-orders/quickbooks`):

| Action | Call | Permission |
|---|---|---|
| List of those to pass (unpaged) | `GET /` | any of the three |
| Export | `POST /export` `{ "includeNew": true }` → file in base64, name and the list for «Did you already import?» | `sales.quickbooks.export` |
| Confirm «Yes, they are in» | `POST /confirm` `{ "invoiceIds": ["…"] }` | `sales.quickbooks.export` |
| Mark by hand | `POST /mark` `{ "invoiceIds": ["…"] }` or `{ "all": true }` | `sales.quickbooks.mark` |
| Remove the mark | `POST /unmark` `{ "invoiceIds": ["…"] }` | `sales.quickbooks.mark` |
| History (`from`, `to`, `q`, `skip`, `take`) | `GET /history` | any of the three |
| Settings | `GET /settings` · `PATCH /settings` | view or `admin.tenant` · `admin.tenant` |

## 4. The file

Text with **tabs** and CRLF rows, in **Windows-1252** (what does not fit that code page comes out as «?»), dates MM/DD/YYYY and amounts with a period. In this order: **new products** (`!INVITEM`), **new clients** (`!CUST`) and **invoices** (`!TRNS` / `!SPL` / `!ENDTRNS`).

- **Each invoice balances to zero**: the main line carries the total to accounts receivable and each sales row the income as a negative.
- **IVU goes as a single line per invoice** (Teikem's total IVU) with the tax item from Settings, to the tax account; QuickBooks does not recompute it. Each line carries its taxable Y/N.
- **The shipping charge** goes out with the shipping item from Settings.
- **Kits do not travel as an item**: each kit line goes out **broken down by component** (the order's component snapshot; if it has none, the catalog recipe), with the amount **prorated** by list price × quantity per kit and **closed to the cent** (example: OFFER1 × 2 = 83.88 → 70.46 + 13.42). Without list prices, in equal parts.
- **Clean names**: tabs and line breaks → space; double quotes → single; in client and item names, colons → hyphen (in QuickBooks «A:B» is a sub-client or sub-item); accounts keep their colons («Sales:Monitors»).
- **Item = Teikem SKU.** The file uses the SKU as the item name: in QuickBooks the item must be named **exactly the same** (QuickBooks keeps the history when renaming).
- **Client in QuickBooks** = the migration's «QuickBooks code» field (the exact client name in QuickBooks) or, if it has none, the business name. A client with that field is **never new**. For the new client: Teikem client number in the note, legal name as company, SURI as resale number, mailing address as «bill to» and physical as «ship to».
- **Terms**: each client's payment term (`NET30`…) is translated to the name it has in QuickBooks (Settings). Without a name, the invoice goes out **without terms** and the list warns.
- **«New» clients and products** = those of these invoices that were never marked nor confirmed. Those already passed do not go out again.

**Warnings the list gives (they do not block):**
- `El nombre del cliente «{nombre}» pasa de 41 caracteres: QuickBooks no lo acepta como nombre de cliente.` — The client name exceeds 41 characters: QuickBooks does not accept it as a client name.
- `El SKU «{sku}» pasa de 31 caracteres: QuickBooks no lo acepta como nombre de ítem.` — The SKU exceeds 31 characters: QuickBooks does not accept it as an item name.
- `El término «{código}» no tiene nombre en QuickBooks (Ajustes → Operación → QuickBooks): esas facturas salen sin término.` — The term has no name in QuickBooks: those invoices go out without terms.

## 5. Settings → Operations → QuickBooks

Empty = default value.

| Field | What it is | Default | Limit |
|---|---|---|---|
| Accounts receivable | Accounts receivable account | `Accounts Receivable` | 159 |
| Sales account | A single one per company (no account per category) | `Sales` | 159 |
| Cost of goods sold account | For new products | `Cost of Goods Sold` | 159 |
| Inventory account | For new products | `Inventory Asset` | 159 |
| IVU payable account | Where IVU lands | `Sales Tax Payable` | 159 |
| IVU item | Name of the tax item | `IVU` | 31 |
| Shipping item | Name of the shipping charge item | `Shipping` | 31 |
| Payment terms | Table: Teikem term → name in QuickBooks (an empty name removes the term from the map) | — | 31 |

The daily «Invoices to pass to QuickBooks» notice goes out at **07:00** company time through the scheduled job `SALES_QUICKBOOKS_PENDING` (the time is changed in System → Scheduled jobs), **one a day and only if there is something**. Who gets it and through which channel (bell, email) is set by a **notification rule** that the administrator creates in System → Notification rules: none is seeded. In the Pulse, the «Invoices to send to QuickBooks» figure of the Sales panel and the «Needs your attention» row (red if the oldest is over 7 days) show up on their own for whoever has `sales.quickbooks.view`.

## 6. Before the first export

- **Products that already exist in QuickBooks**: migrated products have no «already sent» mark, and the first export would bring all of them as «New product». Support marks them as already sent with the company's data script (`QuickBooksSentAt`); migrated clients already count as sent through their «QuickBooks code».
- **Renaming items in QuickBooks**: QuickBooks items not named like Teikem's SKU must be renamed first (check against the QuickBooks item list and the real SKUs). Examples from the analysis, to be confirmed: `NECH 1001` (and `NECH 1002` to `1004`, if applicable), `24081028` → `24-081028`, `24083225` → `24-083225`, `PD-23-36-45gms15/10` → `PD-23-36/15/10`, and the items `PD-23-36-45gms3/50` and `Boost Immune` to the SKU they have in Teikem.
- Recommendation: the **first export without «Include the new clients and products»** (`includeNew: false`) until the names are confirmed.
- Settings → QuickBooks with the **real accounts**, the IVU item and the **terms** (`NET15`, `NET20`, `NET30`, `NET45`, `COD`…).

## 7. Validations and messages

| Situation | Rule | Exact message (Spanish, as sent) | Meaning | HTTP |
|---|---|---|---|---|
| Export with nothing to pass | — | `No hay facturas por pasar a QuickBooks.` | There are no invoices to pass to QuickBooks. | 422 |
| Mark or confirm without invoices | `invoiceIds` or `all` | `Indique las facturas (invoiceIds) o pida todas (all = true).` | Give the invoices (invoiceIds) or ask for all (all = true). | 400 (`invoiceIds`) |
| More than 1000 in one call | Use «all» | `No se pueden marcar más de 1000 facturas en una sola llamada; use «todas».` | No more than 1000 invoices can be marked in a single call; use «all». | 400 |
| An invoice that does not exist or is not an issued sales invoice | — | `Alguna de las facturas indicadas no existe o no es una factura de venta emitida.` | One of the given invoices does not exist or is not an issued sales invoice. | 400 |
| Confirming one that never went out in a file | Mark by hand | `La factura {número} no ha salido en ningún archivo de QuickBooks; para marcarla a mano use «Marcar como en QuickBooks».` | Invoice {number} has not gone out in any QuickBooks file; to mark it by hand use «Mark as in QuickBooks». | 422 |
| No body | — | `El cuerpo de la solicitud es obligatorio.` | The request body is required. | 400 |
| Settings: long account | Up to 159 | `El nombre de una cuenta de QuickBooks no puede pasar de 159 caracteres.` | A QuickBooks account name cannot exceed 159 characters. | 400 |
| Settings: long item | Up to 31 | `El nombre de un ítem de QuickBooks no puede pasar de 31 caracteres.` | A QuickBooks item name cannot exceed 31 characters. | 400 |
| Settings: long term | Up to 31 | `El nombre de un término de QuickBooks no puede pasar de 31 caracteres.` | A QuickBooks term name cannot exceed 31 characters. | 400 |
| Settings: term that does not exist | From the payment terms catalog | `El término de pago «{código}» no existe en el catálogo de términos de pago.` | Payment term «{code}» does not exist in the payment terms catalog. | 400 (`terms`) |
| View, export or mark without the permission | — | `Falta el permiso 'sales.quickbooks.view'.` (or `export` / `mark` / `admin.tenant`) | The permission is missing. | 403 |
| Module off | — | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | Module 'SALES_ORDERS' is not enabled for this company. | 403 |

## 8. Status of an invoice with respect to QuickBooks

It is not a status of the engine: it is two data of the invoice, `QuickBooksExportedAt` (when it last went out in a file) and the **mark** `QuickBooksAt` / `QuickBooksBy` (when and who gave it as passed).

| From → to | Who | What it validates | Effects | What it blocks |
|---|---|---|---|---|
| To pass → Went out in a file (**Export**) | `sales.quickbooks.export` | That there are invoices to pass | Writes the file's date on each invoice (it shows in the invoice's log); does not mark | Nothing: it stays to pass and goes out again in the next file |
| Went out in a file → **In QuickBooks** (**Yes, they are in**) | `sales.quickbooks.export` | That each one went out in some file | Marks (who and when); their clients and products are recorded as already sent | It stops going out in files and counting as «to pass» |
| To pass → **In QuickBooks** (**Mark** by hand, one, several or all) | `sales.quickbooks.mark` | That they are issued sales invoices; up to 1000 per call, or `all` | Same as above | Same |
| In QuickBooks → To pass (**Remove the mark**) | `sales.quickbooks.mark` | That they are issued sales invoices | Returns to the list; clients and products do **not** go back to «new» | — |

A **Voided** or **Draft** invoice does not appear; a **Paid** or **Overdue** one does.

## 9. Frequently asked questions of the module

- **I do not see the QuickBooks tab.** Any of `sales.quickbooks.view`, `.mark` or `.export` is needed and the Sales orders module turned on.
- **I exported, imported and the invoices are still in the list.** Expected: exporting does not mark. Press **Yes, they are in** in «Did you already import these N?» or mark them by hand. If you already left that screen, use **Mark as in QuickBooks**.
- **QuickBooks rejected some invoices.** Uncheck them in «Did you already import these N?» and confirm the rest: the unchecked ones go out in the next file. First check that their items are named like the SKU and that the terms have a name.
- **An invoice I already passed came out again as «went out on …».** It went out in a file and was not confirmed. If it is already in QuickBooks, mark it.
- **The client shows as «new» and already exists in QuickBooks.** Fill its «QuickBooks code» field with the exact client name in QuickBooks (clients with that field are never new) or uncheck «Include the new clients and products».
- **The invoice went out without terms.** The client's term has no name in QuickBooks: add it in Settings → QuickBooks → Payment terms.

The messages are in the [FAQ](faq.md#batch-225--quickbooks-iif-file).

## 10. For support

- Columns `Invoice.QuickBooksAt`, `QuickBooksBy`, `QuickBooksExportedAt` (filtered index `IX_Invoice_QuickBooksPending`), `Client.QuickBooksSentAt`, `Product.QuickBooksSentAt` and those of `Tenant` (`QuickBooksReceivableAccount`, `…SalesAccount`, `…CostAccount`, `…InventoryAccount`, `…TaxAccount`, `QuickBooksTaxItem`, `QuickBooksShippingItem`, `QuickBooksTermsJson`) and the three permissions: section «2026-11-07 — Lote 225» of `Diseño/logistica-db-update.sql` (mark `L225_SALES_QUICKBOOKS` for the cloned Company admin and Sales office roles).
- The file travels in JSON (base64) together with the list for «Did you already import these N?»; exporting writes `QuickBooksExportedAt` and never marks.
- «See data» shows the client and the product **as they will go out in the file** (`quickBooksCustomer`, `quickBooksName`). The «new client / new product» mark is in the QuickBooks list (per invoice), not on the sales order.
