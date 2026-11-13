# 51 — Sales analysis (sales reps, quotas, QuickBooks history, product movement and charts)

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter `docs/manual/51-analisis-de-ventas.md`. The server only answers in Spanish, so every "Exact message" column keeps the **exact Spanish text** the screen shows, followed by its English meaning.

Batch 241 (2026-11-12). One screen to know **how much each sales rep and each route sold against their quota**, **what moves and what is about to run out** for each product, and **charts ready to present**. It replaces the two Excel files that are built by hand today (`Analisis_Ventas_<Month>.xlsx` and `Movimiento_de_productos.xlsx`) and can export them with the same sheets. It reads **two sources**: the **QuickBooks history** (what was sold before Teikem, imported from two reports) and the **Teikem invoices** (from the cutoff date on).

> Screenshots pending: the screenshots of this chapter have not been taken yet. Missing: the «For the analysis to add up:» notice, the «By sales rep» tab (cards, Comparison, «Totals and reconciliation», Lines), «Product movement» (Summary with colors and the monthly grids), «Charts» and the printed sheet, «Setup» (Quotas, Lines and brands, Import history with a simulation, Match by hand) and the «Sales rep» panel of the client record.

## 1. What it is, where it is and who can

- **Where:** menu **Analysis → Sales analysis** (`/analytics/sales`, fifth item of the Analysis group, after Charts). Four tabs: **By sales rep**, **Product movement**, **Charts** and **Setup**. The tab is kept in the address (`?tab=movement`, `?tab=charts`, `?tab=setup&sec=quotas`…), so the link can be shared.
- **Module:** **Sales orders (`SALES_ORDERS`)**. With the module off the whole API answers 403 with `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` (The module 'SALES_ORDERS' is not enabled for this company.)
- **Permissions** (Billing category; both imply being able to see Sales orders, `sales.view`):

| Permission | What it allows | Who has it |
|---|---|---|
| `sales.analysis.view` | See the three views, the history status, the setup (read only), export to Excel, print, the «Sales rep» panel of the client record and the «Month sales vs quota» figure of the Pulse | Company admin, Sales supervisor, Sales office |
| `sales.analysis.manage` | Everything above **plus** change routes, sales reps, quotas, lines and brands, each client's sales rep, the settings, **import the QuickBooks history** and **match by hand** | Company admin, Sales supervisor |

- In companies that already existed, the Company admin (`TenantAdmin`) and the Sales supervisor (`SalesSupervisor`) received both permissions and the Sales office (`SalesOffice`) the view one. **No other role gets them on its own**: the administrator assigns them in System → Roles. The templates of new companies already carry them.
- The screen and the routes accept **either** permission (`view|manage`). With only `.view`, **Setup** is read only (notice «Read only: changing the analysis setup needs the “Manage sales analysis” permission.») and **Import history** and **Match by hand** do not appear.
- The `TenantId` comes from the session: each company sees only its own.

## 2. Setup, step by step

Do it in this order. While something is missing, the screen says so at the top: **«For the analysis to add up:»** with a link to the section where it is fixed («Assign sales reps», «Assign lines», «Match by hand», «Set the cutoff date», «Import the history»). The notice only appears if something is pending.

All of this is **Setup** (needs `sales.analysis.manage`). The sections: **Routes · Sales reps · Quotas · Lines and brands · Clients without a rep · Settings · Import history · Match by hand**.

### 2.1 Routes

A **route** groups sales reps in the Comparison (Chains, North, Metro, East, South, West…). «Chains» is just another route.

- **New route**: **Name** (required, up to 100 characters, unique among the active routes) and **Order** (lowest first; it is the order they appear in the Comparison; 100 by default).
- **Edit**, **Deactivate** and **Reactivate**. Nothing is deleted: it is deactivated. A route **with active sales reps** cannot be deactivated (move its sales reps to another route first).
- API: `GET /api/v1/sales-analysis/routes?includeInactive=false`, `POST …/routes` with `{ "name": "North", "sortOrder": 20 }`, `PATCH …/routes/{id}`, `POST …/routes/{id}/deactivate` and `…/reactivate`.

### 2.2 Sales reps

A **sales rep** is someone who sells: name, **route** (optional) and, if they sign in to Teikem, their **user** (optional, from this company).

- **New sales rep**: **Name** (required, up to 100, unique among the active ones), **Route** and **Teikem user (optional)**. If you cannot see users, the selector says «You are not allowed to see the users.»
- The list shows the route, the user, how many active **clients** they have and their **current quota** for this month.
- A sales rep **with active assigned clients** cannot be deactivated: move the clients to another. A deactivated one still shows in the months they sold.
- **Use the «Sales rep» field**: if the clients come from the QuickBooks migration, they carry a «Sales rep» custom field. This button **creates a sales rep for each distinct value** and **links the clients that do not have a sales rep yet**; what already exists or is already linked does not change, so it can be repeated. The custom field is left as it was. Result: «Sales reps created: N; clients linked: N (already linked: N).» The sales reps are born **without a route**: set it afterwards. In companies that already existed, the migration was done once when this batch was installed.
- API: `GET/POST /api/v1/sales-analysis/reps`, `PATCH …/reps/{id}` (`{ "name", "routeId", "clearRoute", "userId", "clearUser" }`), `…/deactivate`, `…/reactivate`, `POST …/reps/from-custom-field`.

### 2.3 Assigning a sales rep to the clients

This is what decides **whose each sale is** (see section 3.4).

- **Clients without a rep** (in Setup): paginated list of active clients. Filter **Without a sales rep**, **All clients** or **Of «sales rep»**, search by name or code, tick with the checkboxes (or **Select this page**), choose **Assign to…** (or **Remove the sales rep**) and press **Assign**. Up to 2,000 can be assigned at once. Result: «N client(s) assigned to X.»
- A **new** client is born without a sales rep. It can also be changed one by one on the **client record** (section 9).
- API: `GET …/clients?repId=&unassigned=true&q=&skip=0&take=100`, `PUT …/clients/{clientId}/rep` with `{ "repId": 3 }` (`null` removes it), `POST …/clients/assign-rep` with `{ "clientIds": [12, 13], "repId": 3 }`.

### 2.4 Lines and brands

A **line** groups product **brands** (for example «Global Imports» = A.Belle, Global, Yardley…). That way the views group by line instead of by each brand.

- **New line**: **Name** (required, up to 100, unique among the active ones) and **Order**.
- **Brands of the line**: the brand editor offers **the brands that have no line and this line's**. **A brand goes in ONE line only**; it compares without distinguishing upper/lower case or accents. To move a brand to another line, remove it from the first one.
- **Product brands**: a table with every brand of the active own products, how many products each has and its line. The ones **without a line are highlighted**; on the row, **Assign to…**.
- **A brand without a line counts as its own line**, and a product **without a brand** shows in the line **«Without a brand»** (the server's label «Sin marca»). That is why every product should have a brand (typed in Products & inventory) and every brand should be in a line.
- Deactivating a line undoes it: each of its brands shows again as its own line. The brands of a deactivated line cannot be changed.
- API: `GET/POST …/lines`, `PATCH …/lines/{id}`, `…/deactivate`, `…/reactivate`, `PUT …/lines/{id}/brands` with `{ "brands": ["ABELLE", "GLOBAL"] }` (replaces the list) and `GET …/brands`.

### 2.5 Quotas by month

The **quota** is a sales rep's monthly sales goal, in dollars.

- **Quotas** (Setup): grid of **sales rep × 12 months** of the chosen **year**. Type the amount (without `$`) in each month. **Leave it empty so the most recent previous month's quota applies**: in the empty cell the inherited quota shows faded («Takes the 2026-08 quota»). **Copy previous** (per column) copies the previous month's quotas.
- **Nothing is saved until «Save (n)»**, which sends only what changed (one call per month). **Discard changes** undoes it. If an amount is invalid, the button does not save and you read «Some quotas cannot be read: the quota must be zero or more, with up to 2 decimals.»
- **Removing a month's quota** (clear the cell and save) makes the previous one apply again; nothing is lost.
- A month **with no previous quota at all** has no quota: attainment shows empty («No quota»).
- API: `GET …/quotas?month=2026-10`, `PUT …/quotas/2026-10` with `{ "items": [ { "repId": 1, "amount": 16500.00 }, { "repId": 2, "amount": null } ] }` (`null` = remove the month's own), `GET …/reps/{id}/quotas`.

### 2.6 Cutoff date (Settings)

QuickBooks and Teikem **coexist**: the invoices Teikem issues are passed to QuickBooks ([49](49-quickbooks.md)), so a QuickBooks report exported after Teikem started invoicing **already carries the same sales** as Teikem. The **cutoff date** avoids counting them twice:

- **The QuickBooks history counts BEFORE the cutoff date (that day does not) and the Teikem invoices count FROM that day.**
- **How to choose it:** the **first day whose invoices are issued in Teikem** (and are complete). Check that no day is left without a source: the history must reach **the day before** the cutoff date (or Teikem must have the invoices of those days). The **Imported history** panel (section 2.7) shows the dates of each batch; the overlap notice appears when the two sources cross.
- **Without a cutoff date** **both sources count in full**: if they overlap, those days add up twice. The screen warns «No hay fecha de corte y el historial importado (hasta AAAA-MM-DD) se traslapa con las facturas de Teikem (desde AAAA-MM-DD): las ventas de esos días se pueden estar sumando dos veces. Ponga la fecha de corte en Ajustes del análisis.» (There is no cutoff date and the imported history (up to …) overlaps the Teikem invoices (from …): the sales of those days may be counted twice. Set the cutoff date in the analysis settings.)
- With a cutoff date, the imported history lines **from the cutoff day on do not count**: the notice says «N línea(s) del historial importado son del AAAA-MM-DD o después: no cuentan (desde la fecha de corte cuentan las facturas de Teikem).» (N imported history line(s) are from … or later: they do not count (from the cutoff date on, the Teikem invoices count).) It is normal if you exported the QuickBooks report beyond that date.
- It is set in **Settings** (**Cutoff date** / **Clear the date**) or when importing (optional field «Cutoff date», section 2.7).
- The same Settings keep the **Lead time (days)** (1 to 365; how long a supplier order takes to arrive) and the **Service level** (90, 95, 97.5, 99 % or a z value typed by hand, greater than 0 and up to 4; 95 % = z 1.645 is the usual). They are used for the suggested reorder point (section 4.1). Defaults: 5 days and z = 1.645.
- API: `GET/PUT …/settings` with `{ "cutoffDate": "2026-11-16", "clearCutoff": false, "leadTimeDays": 5, "serviceLevelZ": 1.645 }`.

### 2.7 Importing the QuickBooks history

**Who:** `sales.analysis.manage`. **Where:** Setup → **Import history**.

**1) Export the two QuickBooks Desktop reports**, always **complete (all items)**:

- Reports → Sales → **Sales by Item Detail**.
- Reports → Purchases → **Purchases by Item Detail**.
- Range: **from the beginning to the last day you want to bring**, with all the default columns.
- Export to Excel → **Create New Worksheet → «Create a comma separated values (.csv) file»**. The CSV is accepted in Windows-1252 or UTF-8. (With only sales or only purchases it also works: the other file is optional.)
- **Always the complete report**: a new file **replaces everything already imported for its date range** (see «Re-importing»).

**2) Simulate first.** Upload the files (**Sales report** and/or **Purchases report**), type the **Cutoff date (optional)** if you want, and press **Simulate**. **Nothing is saved.** Check in the result:

| What to look at | What should happen |
|---|---|
| **Lines read / To save** | Purchases show «Quantity 0 (skipped)» (QuickBooks brings them empty) and «Duplicates (skipped)» (see below) |
| **Total** against **Report TOTAL** | Says **«matches»**. If it says «does not match», the file is incomplete or was misread: do not import |
| **Dates** (first and last) | It is the range it is going to replace |
| **By class** (sales) or **by transaction type** (purchases) | Product, Discount, Shipping, Other charge (sales); Bill, Item Receipt, Credit (purchases) |
| **Totals by month** | Compare them with QuickBooks month by month |
| **Already imported** | «New», «Replaces the same file (N lines)» or «Replaces its date range (N lines)» |
| **Unmatched clients / items** | Ideally «All matched.» What does not match is dealt with in section 2.8 |
| File warnings | «Línea N: el total de «X» dice A y la suma de sus renglones da B.», «Línea N: «Total X» no cierra ningún encabezado abierto.», «Línea N: fecha «X» no válida; se omite.», «Línea N: transacción fuera de un ítem; se omite.» (Line N: the total of «X» says A and the sum of its rows is B / «Total X» closes no open heading / date «X» is not valid; skipped / transaction outside an item; skipped.) |

**3) Import for real.** The **Import for real** button is only enabled **after simulating the same files** («Simulate first with these same files.») and asks for confirmation («Import for real? … Anything already imported for the same date range is replaced. Continue?»). Each file is saved in **a single transaction**: it goes in complete or not at all. If you type the cutoff date, **it is saved when importing for real** (not when simulating). The **Turn the clients' “Sales rep” field into sales reps** checkbox (marked by default) does what section 2.2 describes.

**4) Check.** The **Imported history** panel lists the current batches (type, file, dates, lines, total, when and by whom). The notice at the top should have lost «Import the history».

**Re-importing replaces the range.** Each file has a fingerprint (SHA-256):

- **The same file** again: replaces its own batch (`REPLACE_SAME_FILE`); it does not duplicate.
- **A different file of the same type**: deletes **all** the already imported lines of that type whose dates fall between the **first and the last** date of the new file (whichever batch they came from) and saves the new ones (`REPLACE_RANGE`). What is outside that range is not touched. That is why you must upload **the complete report**: a partial one (one client, one item) would delete the rest of its range.
- Replaced lines are **really deleted** (they are the copy of an external file); the previous batch is marked as replaced with its date. The links made by hand (section 2.8) **are kept**.

**Purchases: Item Receipt and Bill.** In QuickBooks an *Item Receipt* that is later billed becomes a *Bill*. If the report brought both with the same supplier, number, item and quantity, only the *Item Receipt* is counted (the date the goods arrived) and the *Bill* is discarded (it shows in «Duplicates (skipped)»). Lines with quantity 0 are ignored and *Credit* lines subtract.

**How each line of the file is matched** with Teikem:

- **Client**, in this order: the link made by hand → the «QuickBooks code» field left by the migration → the normalized name (without accents, upper case, without periods or commas) → the normalized legal name. **If a normalized name matches two clients, it does not match** (it must be linked by hand).
- **Item**: the link by hand → the **SKU** without spaces and in upper case («NECH 1003» = «NECH1003»), against the own products (active first).

**Class of each sales line:** an inventory section (*Inventory*, *Assembly*) or an item that matches a product = **Product**; a discount section or item (*Discount*, «descuento») = **Discount**; a shipping item (*shipping*, *delivery*, *envío*, *flete*, *freight*) = **Shipping**; services, «Parts», taxes and other charges without a product = **Other charge**. An unknown section is taken as Product (and if it does not match, it is reported). A price in percent (a 2 % discount) is saved empty: the amount rules.

**API:** `POST /api/v1/sales-analysis/import` (multipart): `salesFile`, `purchasesFile`, `dryRun` (**true** by default), `cutoffDate`, `repsFromCustomField`. The response is the same report as the screen. It is **synchronous** (about 5 seconds for 13,000 lines) and accepts up to 20 MB per file.

### 2.8 Matching by hand

**Who:** `sales.analysis.manage`. **Where:** Setup → **Match by hand**. Three lists: **Unmatched clients**, **Unmatched sales items** and **Unmatched purchase items**, each with the name «as it comes in QuickBooks», the lines, the amount and the date range.

- **Link** opens a search (clients by name; products by name or SKU). When you choose, the already imported lines with that name are re-linked at once and **the link stays for future imports**. Result: «“X” is now linked to Y (N line(s)).»
- A linked item starts counting as **Product** if it was Other charge.
- **Combined item** («53310/53350»): it is not a product, stays unmatched until someone links it, and it can only be linked to **one** of the two.
- While a **client** does not match, its sales show under **«No sales rep»**; while an **item** does not match, it shows in the line **«Unmatched product»** (the server's label «Producto sin emparejar»; it counts in the totals).
- API: `POST …/unmatched/link` with `{ "kind": "CLIENT", "raw": "Farmacia Central Drug.", "clientId": 120 }` or `{ "kind": "PRODUCT", "raw": "NECH 1003", "productId": 39 }`.

### 2.9 The `import-sales-history` command (for whoever runs it on the server)

It does the same as the screen (same logic, with simulation). Useful for the big initial load or if you prefer to run it on the server.

1. Copy the two `.CSV` files to a folder on the server and, next to them, a `config.json` (comments and trailing commas are allowed):

```json
{
  "company": "Advance Solutions",
  "sales": "SalesByItemDetail.CSV",
  "purchases": "PurchasesByItemDetail.CSV",
  "cutoffDate": "2026-11-16",
  "repsFromCustomField": true
}
```

| Field | Rule |
|---|---|
| `company` | Required. The **exact** name of the company in Teikem (it is not created) |
| `sales`, `purchases` | At least one. Absolute path or relative to the `config.json` folder |
| `cutoffDate` | Optional. Saved as the cutoff date when importing for real |
| `repsFromCustomField` | Optional, **true by default** (on the screen the checkbox is also marked; through the API without a checkbox, `repsFromCustomField` is false unless sent) |

2. **Simulate:** in the API folder, `dotnet Teikem.Api.dll import-sales-history C:\path\config.json --dry-run`. It prints the same report as the screen and leaves a `historial-ventas-YYYYMMDD-HHMMSS-simulacion.json` next to the `config.json`.
3. Check (section 2.7, step 2) and, if there are unmatched clients or items, fix them or link them later on the screen.
4. **Import:** the same command **without** `--dry-run`. Exit code **0** = fine; **1** = error (the message says so; nothing is left half done); **2** = the command is badly written.
5. Running it again with the same file does not duplicate (`REPLACE_SAME_FILE`).

Command messages (exact text in section 10.2).

### 2.10 Checklist

1. Routes → 2. Sales reps (or «Use the “Sales rep” field») → 3. Assign sales reps to all clients → 4. Lines and brands (no brand without a line) → 5. Quotas of the year → 6. Import the history (simulate, check, import) → 7. Match by hand what is left → 8. Cutoff date → 9. Confirm the notice at the top is gone and that **«Difference to review»** in the reconciliation is 0.

## 3. By sales rep

**By sales rep** tab: what each sales rep and each route sold in a month, against another month and against their quota. It is the `Analisis_Ventas_<Month>.xlsx` book. **Who:** `sales.analysis.view` or `.manage`. API: `GET /api/v1/sales-analysis/by-rep?month=2026-10&compareMonth=2026-09`.

### 3.1 The month

- **Month** (by default **the last full month**) and **Compare with** (by default **the month before** the chosen one). A month is written `YYYY-MM`.
- «Sales» = **products + discounts** (net sales). **Shipping and other charges belong to no sales rep**: they go apart in the reconciliation.
- Date of each sale: the **invoice** date, by month. A credit memo, the day it was approved.

### 3.2 Each table and figure

| Piece | What it shows |
|---|---|
| **Month figures** (cards) | **{month} sales** (with «Month total with shipping and no-rep sales»), **Change vs {month}** (in $ and «was $») and **Total quota attainment** («of a $ quota»), with a bar |
| **Comparison by route and sales rep** | One row per active sales rep (and inactive ones that sold in either month), ordered by the **Order** of their route, then route and name: **Route, Sales rep, sales of the comparison month, Sales, Change $, Change %, Quota $, Attainment**. Plus the **routes total**. Attainment = Sales ÷ Quota (100 % = met). With no current quota: empty. Color: green 100 % or more, amber 90 % or more, red below |
| **Totals and reconciliation** | See 3.3 |
| **Lines** | The **product** sales of each line in the month, **% of net sales** (over products + discounts), **% before discounts** (over product sales), and the **top seller by dollars** (the sales rep who sold the most of that line) with their route and sales. «Discounts without a line» goes apart, with «N/A» where it does not apply |
| **Line by sales rep** | How much of each sales rep's sales is from each line (and its %). Includes «No sales rep» if it sold. **Hide zero rows** |
| **Product by sales rep** | What each sales rep sold of each product (**Net units**, **Net sales $**), including the history discounts (class «Discount») |
| **Product by route** | **Each product sold in the month for each route, also where it did not sell** (zero, faded). Filters by route, sales rep, line and a search box |
| **Month transactions** | Each invoice, credit memo or history line that counts in the month: Date, **Source** (QuickBooks, Teikem invoice, Credit memo), Type, Number, Client, Sales rep, Product, Line, Class, Units, Price, Sales $. Search by client, number, SKU or product; filter by sales rep. Paginated by the server (up to 500 per page); when exporting, it brings **all** the month |

### 3.3 The reconciliation («Totals and reconciliation»)

It says whether what you see **adds up to the month total**:

```
Routes total         (the sales of all the sales reps)
+ No sales rep       (sales of clients without a sales rep or of history not matched to a client)
+ Shipping and other charges
= Month total        (all the lines of the month)
```

- Below, the breakdown of the total: **Products**, **discounts**, **shipping** and **other**.
- **«Difference to review»** must be **0.00**. If it is not, tell support with the month.
- What falls under «No sales rep» is fixed with section 2.3 (clients without a rep) or 2.8 (unmatched history).
- The month's **«Books total»** (the one compared with accounting) is the **Month total**: it includes shipping and other charges.

### 3.4 Whose sale it is

**The sale belongs to the sales rep the client has assigned TODAY** (`Client → Sales rep`), in any month, also the history's (the QuickBooks report does not carry a sales rep). Consequences:

- If you **change a client's sales rep, their past months show with the new one**. There is no historical "snapshot" of the sales rep.
- If a client has no sales rep, its sales show under **«No sales rep»** (with a row of its own in the tables), also those of the comparison months.
- A credit memo subtracts from the client's **current** sales rep, even if the old Excel put it under «Credit without a sales rep».
- QuickBooks has the «Sales by Rep Detail» report; it is **not imported**.

## 4. Product movement

**Product movement** tab: for each product, how much was sold, how much is left and how long it will last. It is the `Movimiento_de_productos.xlsx` book. **Who:** `sales.analysis.view` or `.manage`. API: `GET /api/v1/sales-analysis/product-movement?fromMonth=2026-01&toMonth=2026-10`.

- **Month range** (**From / To**): by default **the 12 months ending in today's month**; 36 months at most. Stock is **today's** (all warehouses). The **cards** summarize the range (product sales, units, products with sales and how many have less than 2 weeks of coverage).
- It lists **all active own products that are not kits** (a kit is split among its pieces; an assembled kit counts like any product), whether they sell or not, plus the history items that did not match (marked «unmatched»).
- **Sales $ and units** in this view are **product** ones (no discounts, no shipping), with credit memos subtracting. That is why it can differ from the «net sales» of By sales rep by the amount of the discounts.
- The **Summary by product** table has 34 columns. They are grouped so they fit: **Sales · Stock and coverage · Reorder · Price and margin · By month · Clients · All · Choose…** (SKU and product are always there). Sort by any column and search by product, SKU, brand, line or client. There are filters for ABC, status and coverage.
- **«What each column means»** is folded on the same screen (it is the Excel's «Notes» sheet).

### 4.1 Each column, with its exact definition

| Column | Definition |
|---|---|
| **ABC class** | Products are ordered from highest to lowest **product sales $** of the range. A product is **A** if what accumulated **before it** is less than **80 %** of the total; **B** if less than **95 %**; **C** the rest (the final 5 %). A product without positive sales is **«-»**. So the one that crosses 80 % is still A. Kits do not count (they are split) |
| **Status** (evaluated in this order) | **No sales**: it sold nothing in the range. **Stalled**: its last sale was **more than 45 days** ago (to today). **New**: its first month with sales (within the range) is the last full month, the one before it, or the current month. **Rising / Falling**: the **trend** is **+25 % or more** / **−25 % or less**. **Stable**: everything else |
| **Price, Cost, Margin %** | Sale price and purchase cost of the product; Margin = (price − cost) ÷ price (only with a price above 0 and a cost) |
| **On hand today, Reserved, Available** | Sum of all warehouses. **Available = on hand − reserved** |
| **Units, Sales $, % of sales $** | Net of the range (credit memos subtract). The % is over the sales $ of all products |
| **Monthly average, Lowest month, Highest month, Best month** | Units sold per month using the **FULL months since the product's first month with sales**. **Months without sales count 0. The current month does not count** (it is incomplete). Best month = the one with the most units (tie: the first) |
| **Weekly average (8 wk)** | Units per week of the **last 8 full weeks (Monday to Sunday)**, ending on the Sunday before today's week (today Thursday Nov 5, 2026: Monday Sep 7 to Sunday Nov 1). It uses **the weeks since the first week with sales** in that window, and the average is over those weeks. Also the (population) standard deviation for the reorder point |
| Confidence of the weekly average | **no basis**: no sales in the 8 weeks. **few weeks**: less than 3 weeks of history. **sporadic**: it sold half of the weeks used or fewer. **ok**: everything else |
| **Weeks of coverage** | **Available ÷ weekly average** (1 decimal): how long the stock lasts if sales stay the same. Negative available counts 0. With no weekly average: «no basis». **Colors**: red **less than 2 weeks**, amber **2 to 4**, green **4 or more**, gray no basis. In months = weeks ÷ 4.348 |
| **Balance in 1 / 2 / 3 months** | **Available − weekly average × 4.348 × months**, never below 0 and rounded to units. It does not count what is pending receipt nor returns |
| **Minimum (reorder)** | The **minimum saved on the product** (Products & inventory). Empty if the product has none |
| **Suggested reorder point** | **⌈ (weekly average ÷ 7) × lead days + z × (weekly deviation ÷ √7) × √lead days ⌉**, with the lead days and z of Settings (5 and 1.645 by default). It is **red if available stock is already at the suggested point or below**: time to order. With no weekly average: empty |
| **Trend** | The **last full month against the average of the two before** (−23.5 % = it sold 23.5 % less). **Only calculated if it sold in all three months**; otherwise empty |
| **Distinct clients, Invoices** | Clients and invoices (type Invoice) that carry the product in the range, **even if the line comes with quantity 0** |
| **Top client, Units to top client** | The client who was sold the most **invoiced** units (**credit memos not subtracted**); tie: by name. The name as the client is called in Teikem |
| **Last sale** | Date of the last line with a positive quantity |
| **Pending receipt** | Ordered minus received on purchase orders that are **Sent** or **Partially received** |

Example (test data of the batch, Nov 5, 2026): *Padimont Gloves Large 1/100ct.* has 835 available, a weekly average of 106.25 and a deviation of 39.67. Coverage = 835 ÷ 106.25 = **7.9 weeks** (1.8 months). Balance in 1 month = 835 − 106.25 × 4.348 = **373**; in 2 months it would be negative, so **0**. Suggested reorder point with 5 days and z = 1.645: 15.18 × 5 + 1.645 × (39.67 ÷ √7) × √5 = 75.9 + 55.2 = 131.04, rounded up to **132**.

### 4.2 The grids and the clients

- **Units by month**, **Sales $ by month** and **Purchases by month**: one row per product and one column per month of the range, with a **color scale** (it marks the months with the most units), a total per month and, in units, the minimum, maximum and average of the full months. They scroll inside their box.
- **Purchases**: the QuickBooks history (*Purchases by Item Detail*) before the cutoff date and, from it on, **what was received in Teikem against purchase orders** (at the cost of the order line and on the day it was received). Only receipts against the arrival notice of a purchase order count.
- **Clients by product**: the 5 clients (by default) who bought the most invoiced units of each product (**Rank, Client, Units, % of product**).
- **Charts on the tab**: coverage (the 15 products with the least coverage) and units by month of the top 6 by sales $.

## 5. Charts and «Print presentation or save PDF»

**Charts** tab: five charts to present to the stockholders. **Who:** `sales.analysis.view` or `.manage`. API: `GET /api/v1/sales-analysis/charts?fromMonth=&toMonth=&month=&topProducts=6`.

- **Chart period** (by default the **12 months ending in the last full month**; 36 at most) and **Quota month** (by default the last one of the period).
- The five charts: **Sales by month** (total net sales, or stacked **by line** with discounts below zero), **Sales vs quota by sales rep** (green met, amber 90 % or more, red below 90 %), **Share by line** (over product sales, without discounts; small lines are grouped in «Others»), **Units by month of the top 6** (by sales $) and **Coverage by product** (weeks the available stock lasts at the pace of the last 8 weeks, as of today). Each one has **See the data** (the table behind the drawing).
- **«Print presentation or save PDF»** opens the browser's print dialog with a **clean sheet**: the company's light logo (if it has one), name, «Sales analysis», the period («Sales from … to … · … quotas · prepared on …») and **one chart per block**; no menu and no Kemi assistant. In the dialog choose **«Save as PDF»** for the file. The sheet always comes out in the **light theme**, without animation, with the drawing complete.

## 6. Exporting to Excel

**Export to Excel** button in «By sales rep» and in «Product movement». It builds an `.xlsx` in the browser (the name carries the month or range and the day: `analisis-de-ventas-2026-10-YYYY-MM-DD.xlsx`). It uses the texts of the screen's language. It has headers, widths, currency and percent formats, dates as Excel dates, SKU as text, autofilter and **formulas with the value already calculated** (`D5-C5`, `IF(C5<>0,E5/C5,"")`, `SUM(…)`, `MAX(0,F4-G4*1)`…).

| Book | Sheets |
|---|---|
| **By sales rep** (the chosen month) | **Comparison** (with ROUTES TOTAL and the reconciliation), **Brands**, **Rep products**, **Brand sales by rep** (with REP TOTAL and the month TOTAL), **Varieties by route**, **Inventory** and **Transactions** (the whole month) |
| **Product movement** (the range) | **Summary**, **Units by month**, **Sales $ by month**, **Purchases by month**, **Clients by product**, **Chart** and **Notes** |

What you need to know:

- **The Excel does NOT carry drawn charts.** The **Chart** sheet brings **the data** of the «Units by month of the top 6» chart and a note: to see it, select the table and **Insert → Chart** in Excel, or use **Print presentation** in Teikem (section 5). It does not carry styles (bold, fills) or frozen panes either, and **the ABC, status and coverage colors are not in** the Excel.
- The **Inventory** sheet (by-sales-rep book) brings **today's** stock and the units of the chosen month; **coverage and balances are calculated with the units of THAT month** (coverage = available ÷ the month's units; balance after 1, 2 and 3 months = available − the month's units × n), like the old Excel. **It is not the weekly coverage of the screen** («Product movement»), so the numbers can differ. If the month's movement could not be read when exporting, that sheet is not included.
- **Summary** has the 34 columns, and **Notes** the definition of each one and where the data comes from.
- The **Transactions** export brings the whole month (not only the page on screen) and takes a few seconds if the month has thousands of lines.

## 7. How discounts, shipping, credit memos and kits count

| Concept | How it counts |
|---|---|
| **Teikem invoices** | Only the **issued, overdue or paid** ones (active), by their **invoice date**, and only the lines **of a sales order**. Amount = quantity × the line's price (the discount is already in the price). They count **from the cutoff date** (or all of them, without a cutoff date) |
| **Discounts** | From the **history**: QuickBooks discount items (class Discount); they subtract from the sales rep's net sales and show apart as «Discounts without a line». In Teikem the discount is already in the line's price |
| **Shipping** | The order's shipping charge (Teikem) or the shipping item (history). **It belongs to no sales rep**: it goes in the reconciliation («Shipping and other charges») and counts in the «Month total». Other manual charges on a sales invoice do not enter |
| **Other charges** | Services, taxes and the rest of the history without a product. Like shipping: reconciliation, not sales rep |
| **Credit memos** | The **approved or applied** ones, on the company's day they were **approved**. They **subtract** units and amount of their product lines (or of the ones that credit a sales or shipping line). **Free-text** ones **without a product** (transport, warehouse) **do not enter** |
| **Kits** | A **kit** line is **split among its pieces**: each piece's units = kits × quantity per kit, and the amount is prorated with **the same rule as the QuickBooks file** ([49](49-quickbooks.md)): weight = quantity per kit × list price, closed to the cent. The composition of the order line is used or, if it has none, the catalog recipe. **The kit itself does not appear** in Product movement. An **assembled kit** (with its own stock) sells like any product |

## 8. Pulse: month sales against the quota

In the **Sales** panel of the Day pulse the first figure is **«Month sales vs quota»** (for whoever has `pulse.sales` and `sales.analysis.view` or `.manage`; otherwise it does not appear):

- **Sales**: net sales (products + discounts) **of the current month up to today**, of all clients. **Quota**: the sum of the **current quotas of the active sales reps** for this month. It shows as «$sales» and below «of $quota · N %», with a small bar: green 100 % or more, amber 90 % or more, orange below. With no quotas it says «no quotas set».
- A click opens **Analysis → Sales analysis**.
- Since sales include clients without a sales rep but the quota only includes sales reps, the figure looks a bit higher than the sum of the sales reps. Assign sales reps (2.3) so it adds up.

## 9. Client record: «Sales rep» panel

On the client record ([02 §1.9](02-clientes-y-contratos.md#19-sales-rep-on-the-client-record-batch-241)) there is a collapsible panel **«Sales rep»** with the sales rep and their route. With `sales.analysis.manage` it is changed right there (**Change the sales rep**). It is seen with `sales.analysis.view` or `.manage` and the Sales orders module on; otherwise it does not appear.

## 10. Validations and messages

### 10.1 API messages

The API answers in Spanish; the message goes in the error's `title`. Validation errors also carry `errors` with the field.

| Field or situation | Rule | Exact message (Spanish, as sent) | Meaning | HTTP |
|---|---|---|---|---|
| Month (`month`, `compareMonth`, `fromMonth`, `toMonth`, the quota's `{month}`) | `YYYY-MM` (`YYYY-MM-DD` is also read) | `El mes debe tener la forma AAAA-MM (por ejemplo 2026-10).` | The month must have the form YYYY-MM (for example 2026-10). | 400 |
| Month range | End not before the start (Movement, Charts) | `El mes final no puede ser anterior al inicial.` | The end month cannot be before the start. | 400 (`toMonth`) |
| Month range | Up to 36 months | `El rango no puede pasar de 36 meses.` | The range cannot exceed 36 months. | 400 (`fromMonth`) |
| Route: name | Required | `Escriba el nombre de la ruta.` | Type the route name. | 400 (`name`) |
| Sales rep: name | Required | `Escriba el nombre del representante.` | Type the sales rep's name. | 400 (`name`) |
| Line: name | Required | `Escriba el nombre de la línea.` | Type the line name. | 400 (`name`) |
| Route, sales rep or line name | Up to 100 characters | `El nombre no puede pasar de 100 caracteres.` | The name cannot exceed 100 characters. | 400 (`name`) |
| Route when creating or editing a sales rep | Active | `La ruta está desactivada: actívela o escoja otra.` | The route is deactivated: reactivate it or pick another. | 400 (`routeId`) |
| Sales rep when assigning clients | Active | `El representante está desactivado: actívelo o escoja otro.` | The sales rep is deactivated: reactivate them or pick another. | 400 (`repId`) |
| Brands of a line | The line must be active | `La línea está desactivada: actívela antes de cambiar sus marcas.` | The line is deactivated: reactivate it before changing its brands. | 400 (`brands`) |
| The sales rep's user | From this company | `El usuario no pertenece a esta compañía.` | The user does not belong to this company. | 400 (`userId`) |
| Quota | Zero or more, up to 2 decimals | `La cuota debe ser cero o mayor, con hasta 2 decimales.` | The quota must be zero or greater, with up to 2 decimals. | 400 (`items`) |
| Rows per call (quotas, brands, clients) | Up to 2,000 | `No se pueden mandar más de 2000 renglones en una sola llamada.` | No more than 2000 rows can be sent in a single call. | 400 (`items`, `brands`, `clientIds`) |
| Assigning several clients | At least one | `Indique los clientes (clientIds).` | Give the clients (clientIds). | 400 (`clientIds`) |
| Empty brand | Required | `Escriba la marca.` | Type the brand. | 400 (`brands`) |
| Long brand | Up to 100 | `La marca no puede pasar de 100 caracteres.` | The brand cannot exceed 100 characters. | 400 (`brands`) |
| Lead time | 1 to 365 | `Los días de entrega deben estar entre 1 y 365.` | Lead days must be between 1 and 365. | 400 (`leadTimeDays`) |
| Service level (z) | Greater than 0 and up to 4 | `El nivel de servicio (z) debe ser mayor que 0 y hasta 4.` | The service level (z) must be greater than 0 and up to 4. | 400 (`serviceLevelZ`) |
| Transactions: `skip` | 0 or more | `La página debe ser 1 o mayor.` | The page must be 1 or greater. | 400 (`skip`) |
| Transactions and clients: `take` | 1 to 500 (except `all=true`) | `El tamaño de página debe estar entre 1 y 500.` | The page size must be between 1 and 500. | 400 (`take`) |
| Matching: kind | `CLIENT` or `PRODUCT` | `El tipo debe ser CLIENT o PRODUCT.` | The kind must be CLIENT or PRODUCT. | 400 (`kind`) |
| Matching: name | Required | `Indique el nombre tal como viene en el historial (raw).` | Give the name as it comes in the history (raw). | 400 (`raw`) |
| Matching: target | `clientId` (client) or `productId` (product) | `Indique el cliente o el producto de Teikem al que se enlaza.` | Give the Teikem client or product it is linked to. | 400 (`clientId` or `productId`) |
| Import: no files | At least one | `Suba al menos uno de los dos archivos (ventas o compras).` | Upload at least one of the two files (sales or purchases). | 400 (`files`) |
| Import: size | Up to 20 MB per file | `El archivo no puede pasar de 20 MB.` | The file cannot exceed 20 MB. | 400 (`files`) |
| Import: wrong file | It must carry the columns Type, Date, Qty and Amount | `El archivo X no parece un reporte «Sales by Item Detail» o «Purchases by Item Detail» de QuickBooks (faltan las columnas Type, Date, Qty y Amount).` (X = the file name) | File X does not look like a QuickBooks «Sales by Item Detail» or «Purchases by Item Detail» report (columns Type, Date, Qty and Amount are missing). | 400 (`files`) |
| Import: file in the wrong place | Sales in `salesFile`, purchases in `purchasesFile` | `El archivo X es un reporte de compras pero se indicó como de ventas.` (or `… de ventas pero se indicó como de compras.`) | File X is a purchases report but was given as sales (or the other way round). | 400 (`files`) |
| Empty body | `PATCH`/`PUT`/`POST` with a body | `El cuerpo de la solicitud es obligatorio.` | The request body is required. | 400 (`body`) |
| Nonexistent route or from another company | — | `Ruta no encontrada.` | Route not found. | 404 |
| Nonexistent sales rep (also in bulk quotas) | — | `Representante no encontrado.` | Sales rep not found. | 404 |
| Nonexistent line | — | `Línea no encontrada.` | Line not found. | 404 |
| Nonexistent client (assign, link) | — | `Cliente no encontrado.` | Client not found. | 404 |
| Nonexistent product (link) | Own product | `Producto no encontrado.` | Product not found. | 404 |
| Repeated route | Unique name among the active ones (also when reactivating) | `Ya hay una ruta activa con ese nombre.` | There is already an active route with that name. | 409 |
| Repeated sales rep | Same | `Ya hay un representante activo con ese nombre.` | There is already an active sales rep with that name. | 409 |
| Repeated line | Same | `Ya hay una línea activa con ese nombre.` | There is already an active line with that name. | 409 |
| Deactivating a route | No active sales reps | `La ruta tiene representantes activos: páselos a otra ruta antes de desactivarla.` | The route has active sales reps: move them to another route before deactivating it. | 409 |
| Deactivating a sales rep | No assigned active clients | `El representante tiene clientes asignados: páselos a otro representante antes de desactivarlo.` | The sales rep has assigned clients: move them to another sales rep before deactivating. | 409 |
| Brand in two lines | A brand goes in one line only | `La marca «X» ya está en la línea «Y»: quítela de ahí primero.` | Brand «X» is already in line «Y»: remove it from there first. | 409 |
| Simultaneous edit (quotas, clients, links) | — | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | The record was modified by another user; reload and try again. | 409 |
| Without the permission | `sales.analysis.view` or `.manage` (managing: `.manage`) | The API answers 403 (permission policy; the exact body text is not documented for these routes) | — | 403 |
| Module off | `SALES_ORDERS` | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | The module 'SALES_ORDERS' is not enabled for this company. | 403 |

Screen notices (not errors; they come in `warnings`): `N cliente(s) activo(s) sin representante: su venta sale aparte («Sin representante»).` (N active client(s) without a sales rep: their sales go apart) · `N marca(s) sin línea: cada una cuenta como su propia línea.` (N brand(s) without a line: each counts as its own line) · `N cliente(s) del historial sin emparejar: enlácelos a mano para atribuir su venta.` (N history client(s) unmatched: link them by hand to assign their sales) · `N ítem(s) del historial sin emparejar con un producto.` (N history item(s) not matched to a product) · the overlap one and the one about history lines that do not count (section 2.6).

Screen: `Simulate first with these same files.` (you cannot import for real without simulating the same files) and `Some quotas cannot be read: the quota must be zero or more, with up to 2 decimals.`

### 10.2 `import-sales-history` command messages

| Situation | Exact message (Spanish, as printed) | Meaning | Exit |
|---|---|---|---|
| Badly written command (no path, unknown options or more than one path) | `Uso: dotnet run --project src/Teikem.Api -- import-sales-history <config.json> [--dry-run]` | Usage: … | 2 |
| The `config.json` or a file does not exist | `Error de configuración: El archivo no existe: {ruta}.` | Configuration error: the file does not exist: {path}. | 1 |
| Badly written JSON | `Error de configuración: El archivo de configuración no es un JSON válido: …` | Configuration error: the configuration file is not valid JSON: … | 1 |
| Empty JSON | `Error de configuración: El archivo de configuración está vacío.` | Configuration error: the configuration file is empty. | 1 |
| No company | `Error de configuración: company es obligatorio.` | Configuration error: company is required. | 1 |
| No files | `Error de configuración: Indique al menos uno de los dos archivos: sales o purchases.` | Configuration error: give at least one of the two files: sales or purchases. | 1 |
| Nonexistent company | `No existe la compañía «X» (se busca por nombre exacto).` | The company «X» does not exist (searched by exact name). | 1 |
| Failure while importing (a file that is not the report, etc.) | `La importación se detuvo: {mensaje}` (the message is one from table 10.1) | The import stopped: {message} | 1 |

## 11. Activations and deactivations, and imported batches

The analysis does not use the status engine: routes, sales reps, lines and quotas are **activated and deactivated** (never deleted), and imported batches stay **current** or **replaced**.

| From → to | Who | What it validates | Effects | What it blocks |
|---|---|---|---|---|
| Route **active → inactive** | `sales.analysis.manage` | No active sales reps | Stops showing in the Comparison | Assigning it to a sales rep |
| Route **inactive → active** | `sales.analysis.manage` | No other active one with the same name | Counts again | — |
| Sales rep **active → inactive** | `sales.analysis.manage` | No assigned active clients | Stops showing (except in months they sold) and stops adding their quota to the Pulse | Assigning clients to them |
| Sales rep **inactive → active** | `sales.analysis.manage` | Name not repeated | Counts again | — |
| Line **active → inactive** | `sales.analysis.manage` | — | Its brands show again as their own line | Changing its brands |
| Line **inactive → active** | `sales.analysis.manage` | Name not repeated | Groups its brands again | — |
| A month's quota: **remove it** | `sales.analysis.manage` | — | The most recent previous month's quota applies again | — |
| Imported batch **current → replaced** | `sales.analysis.manage` (when importing) | Same file, or a file of the same type whose range covers its lines | Its lines are deleted; the batch stays inactive with the date | — |

## 12. Frequent cases and support

### «It doesn't match QuickBooks»

1. Look at the month's **reconciliation**: «Difference to review» must be 0. Compare the **Month total** with QuickBooks (it includes shipping and other charges), not the sum of the sales reps.
2. In **Import history → Imported history**, check the **dates** and the batch total against the report, and the **totals by month** in a simulation.
3. Check the **cutoff date**: the history counts before it and Teikem from it. A day with no source or with both changes the month.
4. «No sales rep» and «Unmatched product» remain in the totals; they only change row.
5. In «Product movement» sales are **product** sales (no discounts or shipping): it is not the QuickBooks total.

### «A client shows without a sales rep»

Assign it in Setup → **Clients without a rep** or on the client record. If the client is from the **history** and never matched a Teikem client, link it in **Match by hand** (its sales stay under «No sales rep» until then). If it shows with a different sales rep than you expected, remember that **the sales rep of TODAY counts** (section 3.4).

### «The underpads don't count in Padimont»

Underpads (SKU `PD-…`) **have no brand** in QuickBooks, so they fall in the line **«Without a brand»**. Give them a brand (in Products & inventory) and add that brand to the **Padimont** line in Setup → **Lines and brands** (in the batch's test the brand «PADIMONT UNDERPADS» was used).

### «September doesn't match my Excel»

September is the **comparison** month. The October Excel took it from **another report** («reference by routes same date») whereas here it is the full month with the same rules as October and **with today's assigned sales rep**. Check (a) that all clients have a sales rep (the part without one shows under «No sales rep»), (b) that the September history is complete and (c) the cutoff date. September's history total is QuickBooks'.

### «I re-imported and a month changed»

The new file **replaces all the already imported lines between its first and its last date**. If the new report has different content (transactions corrected in QuickBooks, another range, other filters) the month changes. Compare in the simulation **«Replaces its date range (N lines)»** and the totals by month before importing for real. A partial report deletes the rest of its range: always export the complete one.

### Others

- **West (or another route) shows a few dollars of difference against my Excel.** It is almost always a credit memo of a client whose sales rep changed: here it subtracts from the current sales rep (3.4).
- **All products show «New».** The month range is too short: «New» is measured by the first month with sales **inside the range**. Widen the range.
- **A product is «Stalled» but sold last month.** «Stalled» = more than 45 days from its last sale to today.
- **The trend is empty.** It is only calculated if the product sold in the last three full months.
- **I want to change the lead days to see the reorder.** Settings (Setup); the suggested reorder point is recalculated. It does not change the product's «Minimum (reorder)».
- **I don't see the whole Setup tab.** Without `sales.analysis.manage` it is read only and has no Import or Match.

The messages are in the [FAQ](faq.md#batch-241--sales-analysis).

## 13. For support

- **Tables:** `SalesRoute`, `SalesRep`, `SalesQuota` (sales rep + month), `ProductLine` + `ProductLineBrand` (brand → line), `SalesAnalysisSetting` (cutoff date, lead days, z), `SalesHistoryBatch`, `SalesHistoryLine`, `PurchaseHistoryLine`, `SalesHistoryAlias` and `Client.SalesRepId`; index `IX_Invoice_IssueDate`. Section «2026-11-12 — Lote 241» of `Diseño/logistica-db-update.sql` (marks `L241_SALES_ANALYSIS_PERMS` and `L241_SALES_REP_FROM_FIELD`).
- **Code:** pure rules in `SalesAnalysisRules` (ABC, statuses, averages, coverage, reorder, quotas, reconciliation, kits), line reader `SalesAnalysisLineReader`, views `SalesAnalysisService`, administration `SalesAnalysisSetupService`, importer `SalesHistoryImportService` / `SalesHistoryImportRunner`, CSV reader `QuickBooksDetailReportParser`, API `SalesAnalysisController` (`/api/v1/sales-analysis/*`, module `SALES_ORDERS`).
- **Audit EntityTypes:** `SALES_ROUTE`, `SALES_REP`, `SALES_QUOTA`, `PRODUCT_LINE` and `SALES_ANALYSIS`. **Data source** for Views, Indicators and Charts: `SALES_ANALYSIS_LINE` «Sales lines for analysis» (date field `Date`, read `sales.analysis.view`, up to 20,000 lines, the most recent).
- **Known limits:** the sale is attributed to the client's **current** sales rep (the past changes if reassigned); Teikem invoices contribute only their sales lines and the order's shipping; Teikem purchases, only receipts against purchase orders; the views are computed on each query (no cache; tenths of a second with 13,000 lines); the import through the API is synchronous and accepts 20 MB per file; the Excel carries no charts or styles; the quota grid makes one call per month.
- **Not tested:** the path of Teikem invoices, kits and credit memos is covered only by an automated test (there are no real Teikem invoices in the test database); the screenshots have not been taken.
- Audit: changes to routes, sales reps, quotas, lines and settings are logged with the EntityTypes above. Imported lines carry **no** audit trail (they are replaced whole).
