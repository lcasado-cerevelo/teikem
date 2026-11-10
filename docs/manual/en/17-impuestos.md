# 17 — Taxes (IVU)

Batch 55 (2026-10-15). Teikem brings the **complete tax mechanism** and the company **configures** it: no rate, percentage or rule is fixed in the code. When a rate changes, there is no need to touch the program: the previous one's effective period is closed and the new one is created.
This chapter describes the server (API). Sales orders (P4) and billing will use this calculation; the screen is described in [L56 — Taxes](frontend/l56-impuestos.md).

> **Important:** Teikem **does not seed rates or legal figures**. The rates, their components (state, municipal…) and what is taxable are defined by the company's accountant.

## 1. Who can

- Permission **`tax.manage`** (“Manage taxes (rates, exemptions)”). It comes with the **Company admin** (template and already cloned roles); the administrator assigns it to the other roles.
- Module **Taxes (`TAX`)**: it comes **off**; it is turned on per company (System → Modules). With the module off the whole API responds 403.
- Those without the permission only see the **result** of the tax on the order and on the invoice.

## 2. Rates

- A **rate** has a code (e.g. `IVU`), a name, an **effective period** (from / to) and one or more **components** (e.g. State 10.5 % + Municipal 1 %). The **rate percentage is the sum of its components**.
- **A rate is not edited.** If it changes, its effective period is **closed** (last day it applies) and **another one is created** with the same code starting the next day. The history is kept and the documents already issued store the rate they were calculated with.
- For the same code, **effective periods cannot overlap**.
- Status: **In effect** (applies today), **Scheduled** (starts later) or **Closed** (already ended).

## 3. Company rules

A single row per company; it is created by itself with default values (prices without tax included, rounding per line, no default rate, no taxable charge).

- **Prices with tax included**: if active, the amount of each line already includes the tax (it is extracted); otherwise it is added on top.
- **Rounding**: `LINE` rounds the tax of **each line** to 2 decimals and adds up; `DOCUMENT` adds up without rounding and rounds the document's tax **only once** (it can differ by a cent).
- **Default rate**: the code of the rate used for what is taxable (the one in effect on the document date is looked up).
- **Taxable charges**: per charge type (freight, handling, surcharge, pickup) it is indicated whether it pays tax. The **product sale** is decided by the product.

## 4. What pays tax

| Line | Result |
|---|---|
| **Taxable** product (default, without a category) | Tax with the rate in effect (`TAXABLE`) |
| **Zero rate** product | Taxable at 0 % (`ZERO_RATE`) |
| **Exempt** product | No tax (`EXEMPT_PRODUCT`, marked as exempt) |
| Charge (freight, handling…) | According to the charge rule: taxable (`TAXABLE`) or `NOT_TAXABLE_CHARGE` |
| Client with a **current exemption from the whole tax** | Nothing pays tax (`EXEMPT_CLIENT`), with the **certificate recorded** |
| Client with a **current exemption from some components** (e.g. the municipal one; Batch 237) | What is taxable pays the rate **without the exempt components** (`EXEMPT_CLIENT_PARTIAL`): with IVU State 10.5 % + Municipal 1 % it pays **10.5 %**. The exempt component comes out as 0 with the **certificate recorded**. What already paid nothing (exempt product, zero rate, non-taxable charge) stays the same |
| Client with several current exemptions | They are **combined**: one for the whole tax wins; if they are partial their components add up (if together they cover the whole rate, nothing pays: `EXEMPT_CLIENT`) |
| Client with an **expired** exemption | The tax is charged (the expired exemption does not apply) and the calculation brings a **warning** with the certificate that expired |
| Partial exemption naming a component the rate in effect **does not have** | The tax does not change; the calculation brings a **warning** |

A product's tax category is `TAXABLE`, `EXEMPT` or `ZERO_RATE` (it is stored empty when taxable). It is changed in Accounting → Taxes → Products and, since Batch 237, also in the Warehouse **product editor** (**Tax** field of the Prices block; [06](06-inventario-y-almacen.md)).

## 5. Client exemptions

A client has one or more exemptions: **type**, **certificate number**, **valid from / to** (no end date = open), **what it applies to** and notes. Status: **Valid**, **Scheduled**, **Expired** or **Deactivated**. They are never deleted: they are deactivated and can be reactivated.

### 5.1 What the exemption applies to (Batch 237)
<!-- kemi:staff -->

Each exemption has a **scope** (“Applies to”):

- **The whole tax** (`ALL`): the client is charged no tax at all while the exemption is valid. This is how it always worked and how **every exemption that already existed** was left.
- **Only these components** (`COMPONENTS`): the client stops being charged **only** the rate components that are checked; the rest is charged. At least one must be checked. On screen the checkboxes are the components of the **default rate in effect** (Taxes → Rules).

**Example — municipal exemption (Puerto Rico).** The `IVU` rate has two components: State 10.5 % and Municipal 1 % (11.5 %). The client brings a **municipal** exemption certificate valid from January 1 to December 31. The exemption is created with “Only these components” and the **Municipal** box checked. On a $100.00 sale of a taxable product:

| | State 10.5 % | Municipal 1 % | Tax | Total |
|---|---|---|---|---|
| No exemption | 10.50 | 1.00 | 11.50 | 111.50 |
| With the municipal exemption valid | 10.50 | 0.00 (exempt, certificate recorded) | **10.50** | **110.50** |
| With the municipal exemption expired | 10.50 | 1.00 | 11.50 (with a warning) | 111.50 |

- The **copy** each line keeps (sales order, invoice, credit note) carries the percentage **actually charged** (10.5, not 11.5). The line is **not** marked as exempt: it pays tax.
- The same applies to everything that uses the calculation: the sales order, the **shipping charge with IVU**, the invoice's taxable charges (freight, handling…), credit notes and the IVU line of the **QuickBooks** file (it carries the tax actually charged).
- The date that counts is the **document's**: an order made on the last valid day still carries the exemption; the next day it does not.
- **Renewal**: when the certificate expires, create another exemption with the new dates (or change the dates of the existing one). If the expired one is already covered by another valid one with the same components, there is no warning.
- With **prices with tax included**, only the charged part is extracted from the price (the price does not change).
- Rounding (per line or per document) works the same, with the charged percentage.

## 6. Calculation (what sales orders and the invoice will use)

`POST /api/v1/tax/calculate` receives client (optional), date (optional, today by default) and the lines (product **or** charge type, and amount). It returns, per line: net, tax, total, reason, whether it was exempt, **the rate and percentage used** (with a partial exemption, the percentage actually charged; an exempt component comes as 0 with `exemptReason = EXEMPT_CLIENT_PARTIAL` and `exemptionCertificate`) (the “copy” each line will store) and the breakdown per component; and for the whole document: subtotal, tax, total, tax per component and warnings. **It saves nothing.** It also works as a **simulator** to check the rules with the accountant.

## 7. API (everything with `tax.manage`, module TAX)

`GET /tax/rates?activeOnly=` · `POST /tax/rates` · `POST /tax/rates/{id}/close` · `GET|PATCH /tax/settings` · `GET /tax/exemptions?clientId=` · `POST /tax/exemptions` · `PATCH /tax/exemptions/{id}` · `POST /tax/exemptions/{id}/deactivate|reactivate` · `GET /tax/products?q=&category=&skip=&take=` (active products with their category; up to 500 per page) · `GET|PUT /tax/products/{id}/category` · `POST /tax/calculate`. There is no PUT/PATCH/DELETE of rates.

## 8. Validations and messages

| Situation | Exact message | HTTP |
|---|---|---|
| Empty rate code | `Enter the rate code.` | 400 (`code`) |
| Code longer than 30 characters | `The rate code cannot be longer than 30 characters.` | 400 |
| Code with characters not allowed | `The code can only contain letters, numbers, hyphen and underscore.` | 400 |
| Empty name | `Enter the rate name.` | 400 (`name`) |
| Name longer than 100 characters | `The name cannot be longer than 100 characters.` | 400 |
| No start date | `Enter the date the rate takes effect.` | 400 (`effectiveFrom`) |
| End date before the start date | `The end date cannot be before the start date.` | 400 |
| Rate without components | `The rate needs at least one component (for example, State).` | 400 (`components`) |
| Two components with the same code | `There are two components with the same code.` | 400 |
| Component percentage outside 0 to 100 | `Each component percent must be between 0 and 100.` | 400 |
| Sum of components greater than 100 | `The components cannot add up to more than 100 %.` | 400 |
| Overlapping effective periods with the same code | `There is already a rate with that code covering those dates. Close the previous one's effective period first.` | 409 |
| Nonexistent rate (or from another company) | `Rate not found.` | 404 |
| Closing a rate that already has an end date | `The rate already has an end date.` | 409 |
| No closing date | `Enter the last day the rate applies.` | 400 |
| Closing date before the start of the effective period | `The closing date cannot be before the start of the effective period.` | 400 |
| Rounding other than LINE / DOCUMENT | `Rounding must be LINE (per line) or DOCUMENT (per document).` | 400 |
| Default rate that does not exist | `There is no rate with that code.` | 400 |
| Rule for a charge type that does not exist (or PRODUCT_SALE) | `Invalid charge type.` | 400 |
| Exemption without a type | `Enter the exemption type.` | 400 |
| Exemption without a certificate | `Enter the certificate number.` | 400 |
| Type longer than 60 / certificate longer than 60 / notes longer than 300 | `The exemption type cannot be longer than 60 characters.` / `The certificate number cannot be longer than 60 characters.` / `The notes cannot be longer than 300 characters.` | 400 |
| Nonexistent client (or from another company) | `Client not found.` | 404 |
| Nonexistent exemption | `Exemption not found.` | 404 |
| Nonexistent product | `Product not found.` | 404 |
| Category other than TAXABLE / EXEMPT / ZERO_RATE | `The tax category must be TAXABLE, EXEMPT or ZERO_RATE.` | 400 |
| Calculating without lines | `Add at least one line to calculate.` | 400 |
| More than 500 lines | `Up to 500 lines can be calculated at a time.` | 400 |
| Empty or negative amount | `The line amount is not valid.` | 400 |
| Line without a product or charge type | `Each line needs a product or a charge type.` | 400 |
| There are taxable lines and no default rate was chosen | `Choose the default rate in Taxes → Rules.` | 400 |
| No rate in effect on the date | `There is no tax rate in effect on that date. Set one up in Taxes → Rates.` | 400 |
| Scope other than ALL / COMPONENTS | `The exemption scope must be ALL (the whole tax) or COMPONENTS (only some components).` | 400 (`scope`) |
| “Only these components” with no component | `Check at least one tax component the exemption covers.` | 400 (`componentCodes`) |
| Badly written component code | `A component code is not valid (letters, numbers, hyphen and underscore; up to 30 characters).` | 400 (`componentCodes`) |
| More than 10 components | `An exemption can name up to 10 components.` | 400 (`componentCodes`) |
| Component that no rate of the company has | `No rate of the company has the component “{code}”.` | 400 (`componentCodes`) |
| Without the permission or with the module off | “No permission” | 403 |

**Calculation warnings** (not errors: the calculation responds 200 and brings them in `warnings`):

| Situation | Exact warning |
|---|---|
| The client has an expired exemption that no longer covers them | `The client has an expired exemption (certificate {number}, expired on {dd/mm/yyyy}): the tax was charged.` |
| A valid partial exemption names a component the rate in effect does not have | `The client's exemption (certificate {number}) names the component {code}, which the rate in effect does not have: the tax did not change.` |

## 9. For support

- Tables `TaxRate`, `TaxRateComponent`, `TenantTaxSetting`, `TaxChargeRule`, `ClientTaxExemption`, column `Product.TaxCategoryLookupId` and the columns `TaxRateId`, `TaxRatePct`, `TaxAmount`, `IsTaxExempt` of `InvoiceLine` (section **2026-10-15 (j)** of `Diseño/logistica-db-update.sql`). The log records `TAX_RATE`, `TAX_EXEMPTION` and `TAX_SETTING`.
- The **tax report by period** and the copy of the tax on each sales order and invoice line are connected when those documents exist (P4 and billing): today there are no invoices to report on.
- **Batch 237** (section “2026-11-10 — Lote 237” of `Diseño/logistica-db-update.sql`): columns `ClientTaxExemption.Scope` (`ALL` by default / `COMPONENTS`) and `ClientTaxExemption.ComponentCodes` (codes in upper case, sorted, comma-separated; `NULL` with `ALL`) and the constraint `CK_ClientTaxExemption_Scope`. The product record (`GET /products/{id}`) brings `taxCategory` (read-only; it is changed with `PUT /tax/products/{id}/category`). `POST|PATCH /tax/exemptions` carry `scope` and `componentCodes`.
