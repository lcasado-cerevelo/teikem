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
| Client with a **current exemption** | Nothing pays tax (`EXEMPT_CLIENT`), with the **certificate recorded** |
| Client with an **expired** exemption | The tax is charged and the calculation brings a **warning** with the certificate that expired |

A product's tax category is `TAXABLE`, `EXEMPT` or `ZERO_RATE` (it is stored empty when taxable).

## 5. Client exemptions

A client has one or more exemptions: **type**, **certificate number**, **valid from / to** (no end date = open) and notes. Status: **Valid**, **Scheduled**, **Expired** or **Deactivated**. They are never deleted: they are deactivated and can be reactivated.

## 6. Calculation (what sales orders and the invoice will use)

`POST /api/v1/tax/calculate` receives client (optional), date (optional, today by default) and the lines (product **or** charge type, and amount). It returns, per line: net, tax, total, reason, whether it was exempt, **the rate and percentage used** (the “copy” each line will store) and the breakdown per component; and for the whole document: subtotal, tax, total, tax per component and warnings. **It saves nothing.** It also works as a **simulator** to check the rules with the accountant.

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
| Without the permission or with the module off | “No permission” | 403 |

## 9. For support

- Tables `TaxRate`, `TaxRateComponent`, `TenantTaxSetting`, `TaxChargeRule`, `ClientTaxExemption`, column `Product.TaxCategoryLookupId` and the columns `TaxRateId`, `TaxRatePct`, `TaxAmount`, `IsTaxExempt` of `InvoiceLine` (section **2026-10-15 (j)** of `Diseño/logistica-db-update.sql`). The log records `TAX_RATE`, `TAX_EXEMPTION` and `TAX_SETTING`.
- The **tax report by period** and the copy of the tax on each sales order and invoice line are connected when those documents exist (P4 and billing): today there are no invoices to report on.
