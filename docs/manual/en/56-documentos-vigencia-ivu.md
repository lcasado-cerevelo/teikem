# 56 — Documents with validity, IVU exemption tied to its certificate and expiry notices

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter [`../56-documentos-vigencia-ivu.md`](../56-documentos-vigencia-ivu.md) (Batch 248, 2026-11-26). The Spanish manual is the reference. **The API returns its error messages in Spanish only**, so the messages below are quoted in Spanish with their English meaning next to them. Interface labels use the English texts of the software.

Batch 248 (2026-11-26; Batch D of the Solutions plan). Four new things:

1. **Client documents with a number and a validity.** Each document (certificate, permit, license…) can carry its number and its «From» and «Until» dates. If the type «has a validity period», the «Until» date is required.
2. **The client's IVU exemption is tied to the document that backs it** (the certificate). The exemption's number and validity are the document's. There are three new certificate types: state, municipal and total.
3. **Seasonal product exemption**: a decree that exempts all products, or only chosen categories and products, from IVU between two dates.
4. **Expiry notices**: a daily job notifies the sellers (and, if the company turns it on, the client) when a document is about to expire or has expired, and also when a special price expires. There is also an «Expiring documents» list and, when selling, a notice when the client's exemption is about to expire or has expired.

> **This touches money.** A linked exemption is worth what its document says: if the document expires, IVU is charged again. The exemptions that already existed **do not change amount**: they stay valid as before and show as «Document missing» (section 4).

> **No action in this chapter sends an e-mail by itself.** Notices go out through the **notification rules** (System → Notification rules). With no rule, the system records the notice and delivers it to nobody (section 7).

Screens with captures: [L248](../frontend/l248-documentos-ivu.md) (Spanish edition; the captures `img/l248-*-en-*.png` in that folder were taken in English). Every error message is in the [FAQ](faq.md). What this chapter says about tax completes [17 — Taxes (IVU)](17-impuestos.md) and [55 — Special prices and offers](55-precios-especiales-y-ofertas.md) (a special price is still a price **before** IVU).

## 1. Where it is and who can

- **Where:**
  - **Lists → Clients and contracts** → the client's page → **«Client documents»**, **«Taxes»** and **«Client notifications»** panels (`/catalog/clients`).
  - **Lists → Expiring documents** (`/catalog/expiring-documents`).
  - **Accounting → Taxes** (`/money/tax`) → **«Exemptions»** and **«Seasonal exemption»** tabs.
  - **System → Settings → Operation → «Expiry notices»** (`/system/settings`; shown only with the catalog module).
  - **System → Catalogs** → «Client document types» (the «Has a validity period» and «Is a tax document» switches).
  - **System → Notification rules** (events «Client document about to expire», «Client document expired» and «Special price about to expire») and **System → Scheduled jobs** («Expiring documents notices»).
  - The **guided order**, its summary and the **order window**: the client's exemption notice.
- **Modules:** Catalog (`CATALOG`: documents, list and notices), Taxes (`TAX`: exemptions and seasons) and Sales orders (`SALES_ORDERS`: notice when selling and special price about to expire). With a module off, the route answers 403 with `El módulo 'TAX' no está habilitado para esta compañía.` (the module is not enabled for this company; with the name of the module concerned).
- **Permissions** (no new permissions):

| What | Permission | Module |
|---|---|---|
| See a client's documents | `clients.read` | `CATALOG` |
| Upload, edit or remove a document | `clients.update` (or `clients.create_basic` if the client is a **prospect**) | `CATALOG` |
| Change the number or dates of a document that backs an IVU exemption | also `tax.manage` | `CATALOG` and `TAX` |
| See the «Expiring documents» list | `clients.read` | `CATALOG` |
| See exemptions without a document in that list | also `tax.manage` | `TAX` |
| See and change IVU exemptions, apply them from a document, link them | `tax.manage` | `TAX` |
| One-step capture of the exemption from the client's page (`/clients/{id}/tax-exemptions`) | `tax.manage` | `TAX` |
| Create, change, deactivate and reactivate seasonal exemption periods | `tax.manage` | `TAX` |
| See the exemption notice when selling | `sales.create` or `sales.view` | `SALES_ORDERS` |
| Change the thresholds and switches of «Expiry notices» | `admin.tenant` | — |
| Set up who receives each notice | `notifications.manage` | — |
| Run the job by hand | `jobs.manage` | — |
| Read the «Client documents» data source (`CLIENT_DOCUMENT`) | `clients.read` | `CATALOG` |

- `TenantId` comes from the session. A client, document, period, product or category of another company answers 404, without saying whether it exists.
- Without the permission, the route answers 403 with `Falta el permiso 'tax.manage'.` (missing permission; with the missing code) and leaves a `PERMISSION_DENIED` event in the security events.
- **Neither the portal nor the client app show documents or expiry notices** in this batch.

## 2. Client documents with a number and a validity

**What it does.** Next to the file, it stores the document's **number** and its validity: «From» and «Until» (both days count). The system works out by itself how many days are left and the **status** (section 9). The status is never typed by hand.

**Who can.** See: `clients.read`. Upload, edit and remove: `clients.update`, or `clients.create_basic` while the client is a prospect. Module `CATALOG`.

**How to use it (screen).** The client's page → **«Client documents»**. Each row shows the type, the number, the dates (from → until), the days and the status: **Valid**, **Expiring**, **Expired**, **Renewed**, **Missing «until»** or **No validity**. The **«Backs exemption»** mark shows on the document that holds up an IVU exemption.
- **Upload:** choose the type, the file (JPG, PNG, WEBP, GIF or PDF), and type the number and dates. If the type has a validity period, the field says **«Until (required)»**. An «Until» already in the past **is** accepted: it is the record of a real document.
- **Edit** (pencil): number, «From», «Until» and note. The type and the file are not changed.
- **Remove** (trash can): the document leaves the list; the file is not deleted from storage.
- If the type is **«a tax document»** and you have `tax.manage` with the `TAX` module on, uploading it makes the program ask **«Apply the IVU exemption?»**. «Yes, apply» opens the exemption capture with that document.

**How to use it (API).**

| What | Route | Permission |
|---|---|---|
| List | `GET /api/v1/clients/{clientPublicId}/documents` | `clients.read` |
| Upload (multipart) | `POST /api/v1/clients/{clientPublicId}/documents` | `clients.update` or `clients.create_basic` (prospect) |
| Edit | `PATCH /api/v1/clients/{clientPublicId}/documents/{documentPublicId}` | same (and `tax.manage` if it backs an exemption) |
| Remove | `DELETE /api/v1/clients/{clientPublicId}/documents/{documentPublicId}` | same |

Upload (multipart/form-data): `file`, `documentType` (catalog code, for example `IVU_EXEMPTION_STATE`), `documentNumber`, `validFrom` and `validTo` (format `YYYY-MM-DD`) and `notes`. It answers the document with `daysToExpiry`, `expiryState`, `isTax`, `requiresValidTo`, `backsExemption`, `rowVersion` and, only when uploading, `suggestExemption` (the type is a tax type, you have `tax.manage` and `TAX` is on).

Edit (JSON). `null` or not sending it = no change; an empty `documentNumber` or `notes` = remove them; `clearValidFrom` / `clearValidTo` = remove the date. `rowVersion` is the one from the list: if the document changed, 409.

```json
{
  "documentNumber": "E-12345",
  "validFrom": "2026-01-01",
  "validTo": "2027-12-31",
  "notes": "Renewed by the Treasury",
  "rowVersion": "AAAAAAAAB9E="
}
```

**Fields and rules.**
- `documentType`: an **active** code of the «Client document types» catalog that the company has not turned off. It is no longer a fixed list of three: the company can add its own types.
- `documentNumber`: up to 60 characters. It is trimmed; empty = no number.
- `validFrom`, `validTo`: «Until» cannot be before «From». With a type that has a validity period, «Until» is required.
- `notes`: up to 300 characters.
- **The final state is validated when editing.** An old document of a type that became «has a validity period» is not blocked, but when editing it, even just the note, the «Until» date must be entered.
- The file is saved **after** everything is validated: an error leaves no orphan files.

**A document that backs an IVU exemption has three guards:**
1. **It cannot be removed while the exemption is in effect** (409). First deactivate the exemption in Taxes. If the exemption has already expired, it can be removed.
2. **Changing its number or dates also requires `tax.manage`** (403). Extending the «Until» extends the exemption, and that is money.
3. **Its «Until» cannot be removed** (400).
When its number or dates change, the exemption takes the new ones in the same save.

## 3. Document types and their marks

The types are values of the **«Client document types»** catalog (System → Catalogs). Each type has two marks:

| Mark | What it does |
|---|---|
| **Has a validity period** | Requires the «Until» date when uploading or editing the document and includes it in the expiry notices and in «Expiring documents» (also when it lacks the «Until»). |
| **Is a tax document** | Lets the document back an IVU exemption. |

The marks are saved in the catalog value; the company can change them for itself without affecting the others. This batch seeds:

| Type (code) | Has a validity period | Is a tax document |
|---|---|---|
| `MERCHANT_REGISTRATION` (merchant registration) | No | No |
| `RESELLER_CERTIFICATE` (reseller certificate) | **No** | **Yes** |
| `IVU_EXEMPTION_STATE` (state IVU exemption certificate) | Yes | Yes |
| `IVU_EXEMPTION_CITY` (municipal IVU exemption certificate) | Yes | Yes |
| `IVU_EXEMPTION_TOTAL` (total IVU exemption certificate, state and municipal) | Yes | Yes |
| `OTHER` (other) | No | No |

- **The reseller certificate has no validity period on purpose:** the sellers' app uploads it with the type only, without dates. If the company wants it to have one, turn on «Has a validity period» for it (the sellers' app could then no longer upload it without dates).
- Turning a type off in Catalogs makes uploading with that code give 400 («El tipo de documento no existe o está apagado en Catálogos de valores.»).
- **The type does not fix the exemption's scope.** The screen suggests «the whole tax» for the total one and «only the checked components» for the state and municipal ones, but you choose. Pending confirmation with the accountant: which certificates exempt the state IVU.

## 4. IVU exemption tied to its document

**What it does.** A client's exemption (the whole tax, or only some components, such as the municipal one) is backed by a **client document of a «tax» type**. While the exemption is linked, **its number and validity are the document's**: if the document changes its «Until», the exemption changes with it. If the document is removed, the exemption **stops applying**.

**Who can.** `tax.manage`. Module `TAX`.

**How to use it (screen).**
- **The client's page → «Taxes»**: the client's exemptions with the document that backs each one. **«New exemption»** does the **one-step capture**: it uploads the certificate and creates the exemption in a single step, with the scope you choose (the whole tax, or the checked components). **«Renew»** does the same starting from the previous scope.
- **Apply with an already uploaded document:** the question **«Apply the IVU exemption?»** when uploading a certificate, or **Accounting → Taxes → Exemptions → «New exemption»** and choose the document.
- **Accounting → Taxes → Exemptions:** a «Document» column, the **«Document missing»** chip and the **«Link document»** action on earlier exemptions.
- **Deactivating and reactivating:** deactivating can always be done. Reactivating requires the document to still be active, in effect and free.

**How to use it (API).**

| What | Route | Permission |
|---|---|---|
| A client's exemptions | `GET /api/v1/clients/{clientPublicId}/tax-exemptions` | `tax.manage` |
| **One-step capture** (new file **or** already uploaded document) | `POST /api/v1/clients/{clientPublicId}/tax-exemptions` (multipart) | `tax.manage` |
| Apply with an already uploaded document | `POST /api/v1/tax/exemptions` | `tax.manage` |
| Link or renew | `PATCH /api/v1/tax/exemptions/{id}` with `clientDocumentPublicId` | `tax.manage` |
| Deactivate / reactivate | `POST /api/v1/tax/exemptions/{id}/deactivate` and `…/reactivate` | `tax.manage` |

One-step capture with a new file (multipart/form-data, with the `Idempotency-Key` header: repeating the same key returns the same exemption without duplicating the document):

```
POST /api/v1/clients/{clientPublicId}/tax-exemptions
Idempotency-Key: 3f1b6c7e-…
file=@certificate.pdf
documentType=IVU_EXEMPTION_CITY
documentNumber=MUN-2026-0042
validFrom=2026-11-01
validTo=2027-10-31
scope=COMPONENTS
componentCodes=CITY
notes=Annual renewal
```

With an already uploaded document (no file): `clientDocumentPublicId=<guid>`, plus `scope` and `componentCodes`. **File and document at the same time: 400.** An empty `exemptionType` = the label of the document type. The answer carries the exemption, `documentPublicId` and `filePublicId`.

Apply with an already uploaded document (JSON). The number and dates of the body **are ignored**: they are the document's.

```json
{
  "clientId": 15,
  "clientDocumentPublicId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "scope": "COMPONENTS",
  "componentCodes": ["CITY"],
  "notes": "Municipal certificate"
}
```

The exemption the API answers carries the **effective** `validFrom`, `validTo` and `certificateNumber` (the document's), `documentPublicId`, `documentType`, `documentTypeLabel`, `filePublicId` and `missingDocument`. Its `status` is `VALID`, `SCHEDULED`, `EXPIRED`, `INACTIVE` or **Document removed** (`DOCUMENT_REMOVED`: the document was removed: it no longer exempts anything).

**Rules of the backing document.** It must be **of the same client** (another client or another company: 404), be **active**, be of a **«tax»** type, have a **number** and have an **«Until» of today or later** (the last day still works). A document backs at most **one** active exemption (409 if another one uses it).

**The one-step capture** validates **everything before saving the file** and saves the file, the document and the exemption in a single transaction: if anything fails, neither the document nor the exemption remain.

**Exemptions that predate the batch («Document missing»).**
- **They stay valid exactly as before**, even without an «Until» date: no amount changed with this batch.
- They show in «Expiring documents» (only for someone with `tax.manage`) so their certificate can be linked.
- **Editing or reactivating them requires linking a document**; deactivating them can always be done.
- To link: **«Link document»**, or `PATCH` with `clientDocumentPublicId`. From then on, the number and validity are the document's.

**Effective validity.** A single rule decides it for the calculation, the list and the notice when selling:
- Exemption without a document: its own dates and certificate, as always.
- Exemption with an active document: the document's «from», «until» and number.
- Exemption with a removed document: it does not apply.

**Tax.** The calculation did not change, except that it reads the effective validity. Example with state IVU 10.5 % and municipal 1 %, line of 100.00: without exemption, 11.50; with the municipal exemption (components), 10.50; with the total exemption, 0.00 (reason `EXEMPT_CLIENT`). With the exemption expired the full tax is charged and the calculation warns «El cliente tiene una exención vencida (certificado …, venció el …): se cobró el impuesto.» (the client has an expired exemption … the tax was charged).

## 5. Seasonal product exemption

**What it does.** A decree that exempts products from IVU between two dates (both days count). While it is in effect, the **product lines** it covers come out **without tax**, with the reason **`EXEMPT_PRODUCT_PERIOD`** and the period's name (`exemptionPeriod` in the calculation's answer). The client's exemption is not added on top.

**Who can.** `tax.manage`. Module `TAX`.

**How to use it (screen).** **Accounting → Taxes → «Seasonal exemption»**: the list of periods (with «Show deactivated»), **«New period»**, edit, deactivate and reactivate.

**How to use it (API).**

| What | Route |
|---|---|
| List (`includeInactive=true` adds the deactivated ones) | `GET /api/v1/tax/exemption-periods` |
| Create | `POST /api/v1/tax/exemption-periods` |
| See one | `GET /api/v1/tax/exemption-periods/{publicId}` |
| Change | `PATCH /api/v1/tax/exemption-periods/{publicId}` |
| Deactivate / reactivate | `POST …/{publicId}/deactivate` and `…/reactivate` |

There is no `DELETE`: a period is deactivated.

```json
{
  "name": "School supplies August 2026",
  "reason": "Administrative order 2026-12 of the Treasury Department",
  "validFrom": "2026-08-01",
  "validTo": "2026-08-31",
  "scope": "SELECTED",
  "categoryIds": [14],
  "productPublicIds": ["0f8fad5b-d9cb-469f-a165-70867728950e"]
}
```

**Fields.**
- `name` (required, up to 100) and `reason` (required, up to 300): for example, the administrative order that decrees it.
- `validFrom`, `validTo`: required; «until» not before «from» nor before today.
- `scope`: `ALL` (all products; the selection is ignored) or `SELECTED` (chosen categories and products: at least one; up to 2000 products and 200 categories).
- `categoryIds` are the **product categories** of the product catalog (not the tax category) and **include their subcategories**, not the sibling ones.
- `rowVersion` (in the `PATCH`): the one from the list. If it changed, 409.

**What it exempts and what it does not.**
- It exempts **product** lines that are taxable or zero-rate. A product of category «exempt» keeps its reason `EXEMPT_PRODUCT`.
- It **does not exempt charges** (shipping, freight).
- The client's partial exemption (for example, the municipal one) does not change on that line: the period wins. The other lines keep paying.
- If two periods overlap, they are joined and the name of the one that starts first is shown.
- A deactivated target (product or category) does not exempt.
- The period is evaluated with the calculation's date. A **manual** credit note recalculates IVU with **today's** date (a known limit, not changed).

## 6. Exemption notice when selling

**What it does.** When choosing the client in the guided order, in its summary and in the order window, the program warns if the client's IVU exemption **expires soon** or **has already expired**. **It only warns: it blocks nothing.**
- **«…expires on {date} (in N days): IVU will be charged afterwards.»** when the exemption in effect ends within the company's first notice (the largest positive threshold; 30 days by default). A coverage with no «Until» does not warn.
- **«…expired on {date}: IVU will be charged.»** when an exemption with a document has expired and nothing in effect covers what it covered.
- Old exemptions without a document count as coverage, but they are **never** the reason for the notice.
- With the `TAX` module off, there is no notice. The text comes out in the interface language and with the company's date format.

**Who can.** `sales.create` or `sales.view`. Module `SALES_ORDERS`.

**How to use it (API).** `GET /api/v1/sales-orders/tax-notice?client={clientPublicId}`. It answers `level` (`NONE`, `EXPIRING` or `EXPIRED`), `message` (in Spanish, only as a fallback), `validTo`, `days` (the days left; negative if already expired), `certificateNumber` and `documentTypeLabel`.

## 7. Expiry notices

### What it notifies and when

A daily job, **«Expiring documents notices»** (`DOCUMENT_EXPIRY_SCAN`, 06:00 company time; changed or run by hand in System → Scheduled jobs), checks:
- The **client documents** that have an «Until» date (of any type), of active clients and that are **not renewed**.
- The **special prices** (Batch 247) that are published and in effect, only where `SALES_ORDERS` is on. These notify **before they expire or on the day they expire**, never afterwards and never the client.

It publishes one notice per document and threshold:

| Event | When |
|---|---|
| **Client document about to expire** (`SALES_CLIENT_DOCUMENT_EXPIRING`) | The days of a threshold are left, or on the day it expires |
| **Client document expired** (`SALES_CLIENT_DOCUMENT_EXPIRED`) | The «Until» date has passed (per the negative thresholds or the daily notice) |
| **Special price about to expire** (`SALES_SPECIAL_PRICE_EXPIRING`) | The days of a threshold are left, or on the day it expires |

The events are **optional** and appear in the «Sales» group of Notification rules. **No rule is seeded.** For someone to receive the notice, the administrator creates the rule in System → Notification rules.

### The company's settings

Settings → Operation → **«Expiry notices»** (`admin.tenant`):

| Setting | What it does | Default |
|---|---|---|
| **When to notify** (thresholds, in days) | Positive = before expiry, 0 = the day, negative = after. Up to 10, from -365 to 365, no repeats | 30, 15 and 5 |
| **Notify daily after expiry** | While the document stays expired and not renewed, it notifies every day | Off |
| **Notify the client** | The expiring-document notice also reaches the client (main email, contacts marked «Receives email notices» and extra emails) | Off |

Through the API: `PUT /api/v1/tenant/settings` with `docExpiryThresholdDays`, `docExpiryDailyAfterExpiry` and `docExpiryNotifyClient`. What is not sent does not change; `[]` = no thresholds (nothing is notified). They are saved from largest to smallest.

```json
{ "docExpiryThresholdDays": [30, 15, 5, 0, -7], "docExpiryDailyAfterExpiry": false, "docExpiryNotifyClient": true }
```

### How the job decides

- With the «Until» date **d** days from today: if d ≥ 0, it notifies **only the nearest crossed threshold** (with 30, 15 and 5 and d = 12 it notifies the 15 one; a document uploaded late does not get the 30 one and the 15 one at once).
- If it has already expired (d < 0): with the **daily notice** on, one notice every day; without it, the nearest negative threshold. If the job did not run on the threshold's day, the notice is **recovered up to 7 days later**; a document expired for months does not notify (without the daily one).
- **It does not repeat:** each notice carries a key (document + «Until» date + threshold). Correcting the «Until» date notifies again. Threshold 0 published as «about to expire» is not repeated the next day as «expired».
- **Cap of 500 new notices per run and per company.** The rest goes out in the next run; the job's result says so («N quedaron para la siguiente corrida»: N were left for the next run).
- **It is recorded even with no rule.** Creating the rule afterwards does not fire thresholds that already passed all at once; it only notifies what is crossed from then on.
- The job's result says, for example, «N aviso(s) nuevo(s): N por vencer y N vencido(s); N ya se habían avisado.» (N new notice(s): N about to expire and N expired; N had already been notified) or «Nada que avisar.» (nothing to notify).

### Who receives it

Each event rule chooses recipients; the two of the documents event are:
- **«Customer's sales reps (route and preferred)»** (`CLIENT_SELLERS`): the users of the client's sales route and its preferred seller (Batch 244). If the client has neither a route nor a preferred seller with an active user, **it reaches nobody** and the Notice log records it as «Skipped» with the reason «El cliente no tiene ruta de venta ni vendedor preferido con usuario activo.» (the client has no sales route or preferred seller with an active user). Also put a fixed role in the rule (for example the sales supervisor).
- **«Document's customer»** (`CLIENT`): only if the company turns on **«Notify the client»**. It goes to the main email, to contacts marked «Receives email notices» and to the extra emails of its notice; in the client's language. Each client can turn it off on its page → **«Client notifications» → «Expiring documents»**: **it only goes by e-mail** (the portal inbox and SMS are off and cannot be turned on). With no saved row, the client follows the company.
- A special price of a **chain** notifies the sellers of its active clients (up to 50 people).

**When sending, the document is checked.** If it was already removed, renewed, changed its «Until» date or the client was deactivated, the notice **is not sent** and stays «Skipped» in the log with its reason (for example «El documento ya se renovó: no se avisa.»: the document was already renewed, no notice).

> **A change that affects all client notices.** A contact's **«Receives email notices»** mark now applies to **all** client notices. The usual summaries (picked up, out for delivery, delivered, warehouse, COD remittance and invoice) now also reach the **client's main email and its marked contacts**, besides the portal users and the extra emails. Before, they did not reach there. Whoever does not want a contact to receive notices removes the mark on its record.

### «Renewed»

A **renewed document stops notifying** and leaves «Expiring documents». It is renewed if there is **another active document of the same client and the same type** with a later «Until» date (with the same «Until», the one uploaded later; or one uploaded later without «Until»). An old document **without** «Until» never replaces one with a date. If it is renewed with a different type, the old one keeps notifying until it is removed.

## 8. «Expiring documents» list and data source

**Lists → Expiring documents** (`clients.read`, module `CATALOG`). It shows, by «Until» ascending, the documents of active clients that expire within the window, those already expired and those of a type with a validity period that **have no «Until»**. Renewed ones do not show. It has the free search box (client, type or number), the **«Expires within»** filter (by default, the company's first notice), and the **«Include expired»** and **«Include those missing «until»»** switches. The client's name leads to its page. With `tax.manage` and `TAX`, the **exemptions without a document** also show («IVU exemption without document»). There is a cap of 2000 rows: if exceeded, it warns and shows the 2000 closest to expiring.

API: `GET /api/v1/client-documents/expiring?withinDays=&includeExpired=&includeMissing=&client=&q=`. It answers `warnDays`, `withinDays`, `truncated` and `items[]` with `rowKind` (`DOCUMENT` or `EXEMPTION_WITHOUT_DOCUMENT`), client, type, number, dates, `daysToExpiry`, `expiryState`, `isTax`, `backsExemption`.

**«Client documents» data source (`CLIENT_DOCUMENT`).** For Views, Indicators and Charts: one row per active document with `ClientName`, `ClientCode`, `DocumentType`, `DocumentTypeLabel`, `DocumentNumber`, `ValidFrom`, `ValidTo`, `DaysToExpiry`, `ExpiryState`, `IsTax`, `BacksExemption` and `CreatedAtUtc`. It is a **current-state** source (today's), with no period date field, like the fleet documents one. It is read with `clients.read`.

## 9. Statuses and transitions

**There are no new catalog statuses.** No action in this chapter uses the status service or writes status history. The statuses are **calculated from the date**; every change is recorded in the audit log.

**A document's status** (`expiryState`):

| Status | When | Shows in «Expiring documents» |
|---|---|---|
| **Renewed** (`RENEWED`) | Another active document of the same client and type replaces it | No |
| **No validity** (`NO_EXPIRY`) | It has no «Until» and the type has no validity period | No |
| **Missing «until»** (`MISSING_VALID_TO`) | It has no «Until» and the type has a validity period (an old document of a type that became «has a validity period») | Yes |
| **Expired** (`EXPIRED`) | The «Until» date has passed | Yes |
| **Expiring** (`EXPIRING`) | Today or fewer days than the company's first notice remain (the largest positive threshold; the day it expires counts) | Yes |
| **Valid** (`VALID`) | More days remain than the first notice | Only within the «Expires within» window |

**An exemption's status** (`status`): **Valid** (`VALID`), **Scheduled** (`SCHEDULED`: it has not started yet), **Expired** (`EXPIRED`), **Deactivated** (`INACTIVE`) or **Document removed** (`DOCUMENT_REMOVED`: the document was removed and the exemption no longer exempts anything).

**A seasonal period's status** (`state`):

| Status | When | What can be done |
|---|---|---|
| **Scheduled** (`SCHEDULED`) | It starts after today | Everything is editable: name, reason, dates, scope and selection; deactivate it |
| **In effect** (`ACTIVE`) | Today is between «from» and «until» | Only move the **end date** (today or later) or deactivate it; any other change gives 409 |
| **Ended** (`ENDED`) | Its «until» has passed | Only consult it (and deactivate it) |
| **Deactivated** (`INACTIVE`) | It was deactivated | Reactivate it (it returns to the status its dates give it) |

**Actions and what each one triggers:**

| Action | Who can | What it validates | Effect |
|---|---|---|---|
| Upload a document | `clients.update` (or `clients.create_basic` if prospect) | Active type, number ≤ 60, «Until» if the type has a validity period, dates in order, note ≤ 300, valid file | Creates the document; with a tax type and `tax.manage`, the answer suggests applying the exemption |
| Edit a document | same; also `tax.manage` if it backs an exemption and number or dates change | The same on the final state; `rowVersion` | Saves; synchronizes the number and dates of the exemption it backs |
| Remove a document | same | It does not back an exemption **in effect** | Deactivates the document and its file (the content is not deleted) |
| Apply the exemption (one-step capture or with a document) | `tax.manage` | Document of the client, active, tax type, with a number and an «Until» of today or later, free; valid scope | Creates the linked exemption (and, with a new file, the document) |
| Link or renew an exemption | `tax.manage` | The same of the document; free | The exemption takes the new document's dates and number |
| Change the dates or number of a linked exemption | — | Not possible: they are the document's | 400; fix them in Client documents |
| Deactivate an exemption | `tax.manage` | None | The exemption stops applying |
| Reactivate an exemption | `tax.manage` | Linked document, active, with an «Until» in effect, and free | The exemption applies again |
| Create, edit, deactivate or reactivate a period | `tax.manage` | See section 5 | Affects the calculation of lines in its dates |
| Change thresholds or switches | `admin.tenant` | Up to 10 thresholds from -365 to 365, no repeats | Changes what the job notifies and the days of the «first notice» |
| Run the job | `jobs.manage` (or the schedule) | `CATALOG` module on | Publishes the new notices; does not repeat |

**What stays blocked:**
- **Document that backs an exemption in effect:** it cannot be removed (409); changing its number or dates without `tax.manage`, 403; removing its «Until», 400.
- **Linked exemption:** its dates and number cannot be changed directly (400).
- **Exemption without a document (an earlier one):** it is not edited or reactivated without linking a document (400).
- **A document that already backs another active exemption:** it does not back a second one (409).
- **Period in effect:** only the end date changes; ended or deactivated, it can only be consulted.
- **Removed document:** its exemption stops applying (status `DOCUMENT_REMOVED`).

## 10. Validations and messages

The message comes as-is in the error's `title` field (and in red on the screen, except those the screen warns about before sending). **Messages are in Spanish; the English meaning is in the last column.**

### Client documents (`/api/v1/clients/{id}/documents`)

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `documentType` | Active code, turned on in Catalogs | `El tipo de documento no existe o está apagado en Catálogos de valores.` | 400 | The document type does not exist or is turned off |
| `documentNumber` | Up to 60 characters | `El número del documento no puede pasar de 60 caracteres.` | 400 | The number cannot exceed 60 characters |
| `validTo` | Required if the type has a validity period | `Este tipo de documento lleva vigencia: escriba la fecha «hasta».` | 400 | This type has a validity period: enter «until» |
| `validTo` | Not before «from» | `La fecha «hasta» no puede ser anterior a la fecha «desde».` | 400 | «Until» before «from» |
| `validFrom`, `validTo` | Format `YYYY-MM-DD` (when uploading) | `La fecha debe tener el formato AAAA-MM-DD.` | 400 | The date must have the format YYYY-MM-DD |
| `notes` | Up to 300 characters | `La nota del documento no puede pasar de 300 caracteres.` | 400 | The note cannot exceed 300 characters |
| `rowVersion` | Base64 value from the list | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 | Invalid rowVersion |
| `rowVersion` | The document did not change since it was read | `El documento cambió mientras lo editaba. Vuelva a abrirlo.` | 409 | The document changed while editing |
| `validTo` (document that backs an exemption) | Cannot be removed | `Este documento respalda una exención de IVU: no se le puede quitar la fecha «hasta».` | 400 | Its «until» cannot be removed |
| Number or dates (document that backs an exemption) | Requires `tax.manage` | `Este documento respalda una exención de IVU: cambiar su número o su vigencia exige el permiso de impuestos (tax.manage).` | 403 | Needs the tax permission |
| Remove (document that backs an exemption in effect) | First deactivate the exemption | `Este documento respalda una exención de IVU vigente: dé de baja la exención en Impuestos antes de quitarlo.` | 409 | Deactivate the exemption first |
| Permission | `clients.update` (or `clients.create_basic` if prospect) | `Subir documentos de este cliente exige el permiso de editar clientes (clients.update).` | 403 | Uploading needs clients.update |
| Client | Exists in the company | `Cliente no encontrado.` | 404 | Client not found |
| Document | Exists, is the client's and is active | `Documento '{id}' no encontrado.` | 404 | Document not found |

The file (size, type JPG, PNG, WEBP, GIF or PDF) is validated by the file store with the Batch 222 messages; see the [FAQ](faq.md).

### IVU exemptions (`/api/v1/tax/exemptions` and `/api/v1/clients/{id}/tax-exemptions`)

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `clientDocumentPublicId` | Required when applying, or when editing or reactivating an earlier exemption | `La exención necesita el documento que la respalda.` | 400 | The exemption needs its backing document |
| `file` and `clientDocumentPublicId` | One or the other, not both | `Mande el archivo del certificado o escoja un documento ya subido, no las dos cosas.` | 400 | Send the file or choose an uploaded document, not both |
| `file` | Cannot be empty | `Adjunte el archivo del certificado.` | 400 | Attach the certificate file |
| `documentType` | Active code, turned on | `El tipo de documento no existe o está apagado en Catálogos de valores.` | 400 | The document type does not exist or is turned off |
| `documentType` / document | The type has the «is a tax document» mark | `Ese tipo de documento no es de impuestos: no puede respaldar una exención.` | 400 | That type is not a tax type |
| `documentNumber` | Required (capture with a file) | `Escriba el número del certificado.` | 400 | Enter the certificate number |
| `documentNumber` | Up to 60 characters | `El número del documento no puede pasar de 60 caracteres.` | 400 | The number cannot exceed 60 characters |
| `validFrom` | Required (capture with a file) | `Escriba desde cuándo vale el certificado.` | 400 | Enter the «from» date |
| `validTo` | Required (capture with a file) | `Escriba hasta cuándo vale el certificado.` | 400 | Enter the «until» date |
| `validTo` | Not before «from» (capture with a file) | `La fecha «hasta» no puede ser anterior a la fecha «desde».` | 400 | «Until» before «from» |
| `validTo` | Today or later (capture with a file) | `La fecha «hasta» ya pasó: la exención nacería vencida.` | 400 | The «until» date passed: the exemption would be born expired |
| `exemptionType` | Up to 60 characters | `El tipo de exención no puede pasar de 60 caracteres.` | 400 | The exemption type cannot exceed 60 characters |
| `notes` | Up to 300 characters | `Las notas no pueden pasar de 300 caracteres.` | 400 | The notes cannot exceed 300 characters |
| `scope` | `ALL` or `COMPONENTS` | `El alcance de la exención debe ser ALL (todo el impuesto) o COMPONENTS (solo algunos componentes).` | 400 | Invalid scope |
| `componentCodes` | With `COMPONENTS`, at least one | `Marque al menos un componente del impuesto que la exención cubre.` | 400 | Check at least one component |
| `componentCodes` | A component of some company rate | `Ninguna tasa de la compañía tiene el componente «{código}».` | 400 | No rate has that component |
| Document | Is active | `El documento que respaldaba la exención se quitó: suba el certificado vigente.` | 400 | The backing document was removed |
| Document | Has a number | `El documento no tiene número: escríbalo en el documento antes de aplicar la exención.` | 400 | The document has no number |
| Document | Has an «Until» | `El documento no tiene fecha «hasta»: la exención necesita la vigencia del documento.` | 400 | The document has no «until» date |
| Document | Is not expired | `El documento ya venció: suba el certificado vigente.` | 400 | The document already expired |
| `validFrom`, `validTo`, `clearValidTo`, `certificateNumber` in the `PATCH` of a linked exemption | They are the document's | `La vigencia y el número de la exención son los del documento: corríjalos en Documentos del cliente.` | 400 | Fix them in Client documents |
| Document | Does not already back another active exemption | `Ese documento ya respalda otra exención activa.` | 409 | That document already backs another exemption |
| Document | Is of the same client | `Documento no encontrado.` | 404 | Document not found |
| Exemption | Exists | `Exención no encontrada.` | 404 | Exemption not found |
| Client | Exists in the company | `Cliente no encontrado.` | 404 | Client not found |
| (body) | Must come | `El cuerpo de la solicitud es obligatorio.` | 400 | The request body is required |

### Seasonal exemption periods (`/api/v1/tax/exemption-periods`)

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| (body) | Must come | `El cuerpo de la solicitud es obligatorio.` | 400 | The request body is required |
| `name` | Required | `Escriba el nombre del período de exención.` | 400 | Enter the period's name |
| `name` | Up to 100 characters | `El nombre del período no puede pasar de 100 caracteres.` | 400 | Name too long |
| `reason` | Required | `Escriba el motivo de la exención (por ejemplo, la orden administrativa que la decreta).` | 400 | Enter the reason |
| `reason` | Up to 300 characters | `El motivo no puede pasar de 300 caracteres.` | 400 | Reason too long |
| `validFrom` | Required | `Escriba desde qué fecha rige la exención.` | 400 | Enter the start date |
| `validTo` | Required | `Escriba hasta qué fecha rige la exención.` | 400 | Enter the end date |
| `validTo` | Not before «from» | `La fecha final no puede ser anterior a la inicial.` | 400 | End before start |
| `validTo` | Not before today | `La fecha final no puede quedar antes de hoy.` | 400 | End date before today |
| `scope` | `ALL` or `SELECTED` | `El alcance debe ser ALL (todos los productos) o SELECTED (categorías o productos escogidos).` | 400 | Invalid scope |
| `productPublicIds`, `categoryIds` | With `SELECTED`, at least one | `Escoja al menos una categoría o un producto.` | 400 | Choose at least one |
| `productPublicIds`, `categoryIds` | Up to 2000 products and 200 categories | `Un período puede escoger hasta 2000 productos y 200 categorías.` | 400 | Too many targets |
| Period already started | Only the end date moves or it is deactivated | `El período ya empezó: solo se le puede cambiar la fecha final o darlo de baja.` | 409 | The period already started |
| `rowVersion` | The period did not change since it was read | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 | Modified by another user |
| Period | Exists | `Período de exención no encontrado.` | 404 | Period not found |
| Product | Exists in the company | `Producto no encontrado.` | 404 | Product not found |
| Category | Exists in the company | `Categoría no encontrada.` | 404 | Category not found |

### Notice when selling, list and settings

| Where | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `GET /sales-orders/tax-notice` | The client is missing | `Indique el cliente.` | 400 | Specify the client |
| `GET /sales-orders/tax-notice` | The client exists in the company | `Cliente no encontrado.` | 404 | Client not found |
| `GET /client-documents/expiring` | `withinDays` from 1 to 365 | `Los días deben ser un número entero entre 1 y 365.` | 400 | Days must be an integer from 1 to 365 |
| `GET /client-documents/expiring` | `client` exists | `Cliente no encontrado.` | 404 | Client not found |
| `PUT /tenant/settings` | Each threshold an integer from -365 to 365 | `Cada umbral es un número entero de días entre -365 y 365 (positivo = antes de vencer, 0 = el día, negativo = después).` | 400 | Each threshold is a whole number of days from -365 to 365 |
| `PUT /tenant/settings` | No repeats | `Hay un umbral repetido.` | 400 | A threshold is repeated |
| `PUT /tenant/settings` | Up to 10 | `Se pueden poner hasta 10 umbrales.` | 400 | Up to 10 thresholds |
| `PUT /clients/{id}/notifications/settings` | `DOCUMENT_EXPIRING` only by e-mail | `El aviso de documentos por vencer solo sale por correo.` | 400 | The expiring-documents notice only goes by e-mail |
| Permission | Missing permission | `Falta el permiso '…'.` | 403 | Missing permission |
| Module | Turned off | `El módulo '…' no está habilitado para esta compañía.` | 403 | The module is not enabled |

### Notices that are not errors

The **exemption notice when selling** and the **expiry notices** never block anything. The reasons why a notice stays «Skipped» in the Notice log are not errors: «El documento ya se quitó: no se avisa.» (the document was removed), «El documento ya se renovó: no se avisa.» (renewed), «La fecha «hasta» del documento cambió: este aviso ya no aplica.» (the «until» date changed), «El cliente está dado de baja: no se avisa.» (the client is deactivated), «La compañía no tiene encendido «Avisar al cliente» (Ajustes → Avisos de vencimiento).» (the company has «Notify the client» off), «El cliente tiene apagado el aviso de documentos por vencer.» (the client turned the notice off), «El cliente no tiene correo principal ni contactos marcados «Recibe avisos por correo».» (no main email or marked contacts) and «El cliente no tiene ruta de venta ni vendedor preferido con usuario activo.» (no route or preferred seller). Those of the special price: «El precio especial se desactivó: no se avisa.», «El precio especial volvió a borrador: no se avisa.», «El precio especial ya se renovó: no se avisa.», «La fecha «hasta» del precio especial cambió: este aviso ya no aplica.», «El cliente o la cadena del precio especial está dado de baja: no se avisa.» and «Ningún cliente activo de la cadena tiene ruta de venta ni vendedor preferido con usuario activo.».

## 11. Frequently asked questions of the module

**I uploaded the certificate but the exemption was not applied. Why?**
Uploading the document does **not** create the exemption. Answer «Yes, apply» to **«Apply the IVU exemption?»**, or use «New exemption» (client's page → Taxes, or Accounting → Taxes). The question only appears if the type is a «tax» type and you have `tax.manage` with the `TAX` module on.

**Why can't I remove the document?**
It backs an IVU exemption in effect. Deactivate the exemption in Accounting → Taxes → Exemptions and remove the document afterwards. If what you need is a new certificate, use **«Renew»**: it uploads the new one and the exemption moves to it.

**It will not let me change a document's «Until».**
If the document backs an exemption, the `tax.manage` permission is also needed (403), and its «Until» cannot be removed (400). If it backs no exemption, `clients.update` is needed.

**An exemption says «Document missing». Did it stop being valid?**
No. Exemptions from before the batch stay valid as before. The mark only warns that they have no certificate in the system: their expiry cannot be followed. Use «Link document».

**The exemption was charged as expired on an order. Why?**
The calculation takes the validity from the document. If the document's «Until» has passed, IVU is charged and the calculation warns «El cliente tiene una exención vencida (certificado …, venció el …): se cobró el impuesto.». Upload the valid certificate with «Renew».

**The document has «Missing «until»». What do I do?**
It is a document of a type that became «has a validity period» after it was uploaded. Edit it and enter the «Until». Meanwhile it shows in «Expiring documents» with that status.

**Why does a document stop showing in «Expiring documents»?**
It is **renewed**: there is another active document of the same client and the same type with a later «Until». Or the document was removed, or the client is deactivated.

**I did not receive the notice that a document expires.**
Check, in order: (1) that there is a **rule** for «Client document about to expire» in System → Notification rules; (2) that the client has a **sales route or preferred seller** with an active user, if the rule uses «Customer's sales reps»; (3) that the «Expiring documents notices» job is on; (4) that the document has an «Until» and is not renewed; (5) the **Notice log**: it stays «Skipped» there with the reason. If the notice was recorded before the rule existed, it is not repeated.

**The client does not receive the notice.**
**«Notify the client»** must be on in Settings → Operation → «Expiry notices», the client must not have turned it off on its page («Client notifications» → «Expiring documents»), and it must have a main email, a contact marked «Receives email notices» or an extra email.

**I get notices of the same document every day.**
**«Notify daily after expiry»** is on and the document is still expired and not renewed. Upload the renewed document, remove it or turn the switch off.

**I changed the thresholds and nothing happened.**
Thresholds are only used when the job runs (06:00 by default) or when someone runs it by hand. A new threshold notifies in the next run only if it is the nearest one the document has already crossed; if a nearer one was already notified, it is not repeated. The «first notice» (the largest positive threshold) also defines when a document shows as «Expiring».

**The client summaries started reaching more people.**
It is the change of the «Receives email notices» mark (section 7): it now applies to all client notices and the summaries also reach the main email and the marked contacts. Remove the mark from the contact who should not receive them.

**Why does my line have no IVU if the client has no exemption?**
A **seasonal exemption period** may be in effect covering the product (or its category). The line's reason is `EXEMPT_PRODUCT_PERIOD` and it carries the period's name. Check Accounting → Taxes → «Seasonal exemption».

**It will not let me change a period.**
If it already started, only the end date can be moved or it can be deactivated (409). To change the products or categories of a period in effect, shorten its end date and create another period with the new selection.

**What happens with a credit note for a product sold in season?**
A **manual** credit note recalculates IVU with **today's** date. If the period has ended, the note calculates IVU. It is a known limit; today it is corrected by hand.

**Which certificate exempts the state IVU?**
Pending confirmation with the accountant. Meanwhile, the document type does not force the scope: you choose whether the exemption covers the whole tax or only some components.

## 12. For support

- **Tables:** `ClientDocument` (`DocumentNumber`, `ValidFrom`, `ValidTo`, `RowVersion`; remove = `IsActive = 0`), `ClientTaxExemption.ClientDocumentId` (empty = «document missing»), `TaxExemptionPeriod` with `TaxExemptionPeriodProduct` and `TaxExemptionPeriodCategory` (removing a target = `IsActive = 0`), `Tenant.DocExpiryThresholdDays`, `DocExpiryDailyAfterExpiry` and `DocExpiryNotifyClient`, and `NotificationOutbox` (the no-repeat key).
- **One exemption per document:** the index `UX_ClientTaxExemption_Document` only counts active exemptions. The composite key `FK_ClientTaxExemption_Document` (document, company and client) prevents tying another client's or another company's certificate (error 547 in the database).
- **«Why was IVU charged (or not)?»:** in the calculation, the line's reason (`EXEMPT_CLIENT`, `EXEMPT_CLIENT_PARTIAL`, `EXEMPT_PRODUCT`, `EXEMPT_PRODUCT_PERIOD`) and, for the season, `exemptionPeriod`. The order line stores only today's copy (`IsTaxExempt`, `TaxRatePct`, `TaxAmount`): it does not say which exemption exempted it. That comes in a later batch.
- **«Why did it not notify?»:** look in `NotificationOutbox` for the key `CD:{document id}:{YYYYMMDD of the until date}:T{threshold}` (or `D{YYYYMMDD}` with the daily notice; `SP:…` for special prices). If it exists, it was already notified. The Notice log says whether it was delivered or why it was skipped.
- **Job:** System → Scheduled jobs → «Expiring documents notices» (`DOCUMENT_EXPIRY_SCAN`): the last result says how many notices went out and whether some were left for the next run (cap of 500).
- **Audit:** document changes are recorded with the type `CLIENT_DOCUMENT`, exemption changes with `TAX_EXEMPTION`, period changes with `TAX_EXEMPTION_PERIOD` and company setting changes with `TENANT`.
- **Money:** before putting this in production, check how many active exemptions are left «without a document» (the migration prints it: «Lote 248: N exenciones activas quedan marcadas «falta el documento»…»). They stay valid as before; the «Expiring documents» list shows them to whoever has `tax.manage`.
- Without the permission, or with the module off, the 403s leave a `PERMISSION_DENIED` security event.
