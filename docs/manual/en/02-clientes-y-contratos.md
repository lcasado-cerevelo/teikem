# Chapter 02 — Clients and contracts (Batch 2)

This chapter describes, as the user experiences it, everything Batch 2 makes available: the client file
(profile, addresses, numbering, contacts), its consignees and locations, the contracts with their billing
model, the rates per service and per extra piece, the quote, the special services and the client's portal
users. Each section states **what it does**, **who can**, **how it is used**, the **validations** with the exact message
and the HTTP code, and the **statuses** with their transitions and effects. The messages are verified against the code
(`src/Teikem.Infrastructure/Services/*`, `src/Teikem.Infrastructure/Clients/*`, `src/Teikem.Domain/Clients/*`) and
exercised by `scripts/smoke.sh`. The questions and answers for each message are in [faq.md](faq.md).

Conventions of the chapter:

- Resources are identified by their `publicId` (GUID) in the URL; children (contacts, tiers, special services) by their numeric `id`,
  **always inside the parent's URL**. A `publicId` from another company responds `404` as if it did not exist.
- The whole module requires the **CATALOG** module to be on; portal users require **CLIENT_PORTAL** (see 8).
- A validation error responds `400` with `title` (message) and `errors` (`{ field: [message] }`); a conflict `409`; a
  status rule `422`; a foreign or nonexistent resource `404`.
- Dates are `yyyy-MM-dd`; "today" is the server's UTC date.

---

## 1. Clients

### 1.1 Creating a client (modal "+ New client")

What it does: creates the client and, optionally, its **initial contract** in a single action. The initial contract is born in
status **DRAFT**, with the "Per service" component on and the other four off, number `{Code}-C1`, title
"Master contract" if none is given, and the service levels (SLA) that are sent. The client is born in the tenant's initial
status (**ACTIVE** in the seed) and its status history is recorded from the very first moment.

Who can: `clients.create`.

How it is used: `POST /api/v1/clients`

```json
{ "name": "Farmacia Las Marías", "code": null, "legalName": null, "taxId": null, "paymentTerm": "NET30", "currency": "USD", "creditLimit": 5000,
  "contract": { "startDate": "2026-01-01", "title": null, "endDate": null, "autoRenew": false,
                "serviceLevels": [ { "serviceType": "STANDARD", "maxTransitHours": 48 } ] } }
```

It responds with the full record (see 1.2) with `currentContract` if a contract was created.

The **code** is optional: if it is not sent it is generated from the name (uppercase, no accents, everything non-alphanumeric
becomes `-`, 20 characters at most: "Farmacia Las Marías" → `FARMACIA-LAS-MARIAS`). If it already exists, the suffix `-2`,
`-3`… is added, trimming the base if needed so as not to exceed 20 (`FARMACIA-LAS-MARIA-2`). An explicit code that already exists
responds 409.

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| `name` empty | required, ≤ 200 | `The name is required.` / `Cannot exceed 200 characters.` | 400 |
| `name` without letters or digits | the code cannot be generated | `The client name contains no letters or digits to generate the code.` | 400 |
| `code` > 30 characters | — | `The code cannot exceed 30 characters.` | 400 |
| `code` already exists (or last-moment collision) | unique per company | `A client with that code already exists.` | 409 |
| `legalName` / `taxId` too long | ≤ 250 / ≤ 50 | `Cannot exceed 250 characters.` / `Cannot exceed 50 characters.` | 400 |
| `creditLimit` negative | ≥ 0 | `The credit limit cannot be negative.` | 400 |
| `paymentTerm` / `currency` unknown | list code | `List PaymentTerm 'X' not found.` / `List Currency 'X' not found.` | 404 |
| `contract.endDate` < `contract.startDate` | validity period | `The end date cannot be earlier than the start date.` (field `contract.endDate`) | 400 |
| `contract.title` > 200 | — | `The title cannot exceed 200 characters.` | 400 |
| Repeated / invalid SLA | same rules as 4.6, field `contract.serviceLevels[i].*` | see table in 4.6 | 400 |
| SLA with a nonexistent service type | — | `Unknown service type: 'X'.` | 400 |

### 1.2 Client record

What it does: returns the full file: profile, status, **physical address** (active location of type CORPORATE),
**mailing address** (type BILLING; `null` means "same as the physical one"), **pickup point** (the default warehouse
or, if there is none, the corporate one with `isDefaultFromCorporate: true`), its **warehouses** (`pickupLocations`, type PICKUP or BOTH),
the numbering settings with a preview, its **contact persons** with phones/emails, the **phones/emails
of the client itself**, its **contracts** (summary with the billing model) and the **current contract** (`currentContract`,
see 4.1) with its summary (`billingSummary`, e.g. "Per service, COD"). It includes `rowVersion` for concurrent editing.

Who can: `clients.read`.

How it is used: `GET /api/v1/clients/{publicId}`; list: `GET /api/v1/clients?search=&includeInactive=false` (searches by
code, name or legal name; by default it hides deactivated ones).

### 1.3 Profile (without name)

What it does: edits the legal name, tax ID, credit limit, payment term, currency and the **usual pickup
point**. The **name is not edited** (it is the client's identity): to correct it you must deactivate the client and create another one.

Who can: `clients.update`.

How it is used: `PATCH /api/v1/clients/{publicId}/profile` with only the fields that change; `rowVersion` optional (the one from the
record) to detect that another user edited first. `defaultPickupLocationPublicId` sets the default warehouse;
`clearDefaultPickup: true` goes back to "picked up at the corporate address".
Batch 43 (2026-10-12): `autoPrintInvoice: true|false` saves this client's “Invoice label” (the one in charge);
without the field it does not change, and the record returns it in `autoPrintInvoice` (off by default). Since 2026-10-21, when saving a shipment with an invoice
number, after the package labels a single invoice label comes out (barcode of the number with the number below; redesignable). See chapter [12 — Printers and labels](12-impresoras-y-labels.md) §6.

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| `defaultPickupLocationPublicId` is not an active address of the client | it must be the client's own active location: the physical one (CORPORATE), the mailing one (BILLING) or a pickup point (PICKUP or BOTH) | `The pickup point must be an active address of the client: the physical one, the mailing one or a pickup point.` | 400 |
| `rowVersion` different from the current one | concurrent editing | `The record was modified by another user; reload and try again.` | 409 |
| `creditLimit` negative | — | `The credit limit cannot be negative.` | 400 |

**Default service and package (2026-10-21).** In the client's profile you can choose its **default service** and its **default package** (`defaultServiceType` and `defaultPackageType` in `PATCH /clients/{id}/profile`; empty string = back to the company's; a code that is not from that catalog gives 400). The quick shipment entry brings them preset when the client is chosen.

### 1.4 Numbering per client

What it does: two independent questions — **who assigns the shipment number?** and **who assigns the invoice number?**
(`clientAssignsOrderNumber`, `clientAssignsInvoiceNumber`; `false` = Teikem) — and three patterns (shipment, invoice, package)
where `#` is a digit of the sequence and `@` the series letter. An empty string in a pattern = go back to the system's default
pattern: `ORD-#####`, `FAC-#####`, `PQT-#####` (preview `ORD-00001`, `FAC-00001`, `PQT-00001`). If the sequence
has more digits than `#`, it is not truncated (`AX-##` with 123 → `AX-123`). The record shows the effective pattern and its preview.

Who can: `clients.update` (edit), `clients.read` (preview).

How it is used: `PATCH /api/v1/clients/{publicId}/number-settings`
`{ "clientAssignsInvoiceNumber": true, "orderNumberFormat": "AX-#####", "invoiceNumberFormat": "FAC-####" }`;
live preview: `GET /api/v1/clients/number-format/preview?pattern=AX-%23%23%23%23%23&seq=1` → `{ "value": "AX-00001" }`.

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| Pattern without `#` | at least one digit | `The pattern must include at least one '#' for the sequence.` | 400 |
| Pattern > 40 characters | — | `The pattern cannot exceed 40 characters.` | 400 |
| Character not allowed | letters, digits and `- _ / . # @` | `Character not allowed in the pattern: 'x'. Use letters, digits and - _ / . # @.` | 400 |
| Preview without a pattern | — | `The pattern is required.` | 400 |
| Preview with a negative `seq` | — | `The sequence cannot be negative.` | 400 |

The `number-settings` errors all arrive together in `errors` with the field key (`orderNumberFormat`, …).

### 1.5 Contact persons

What it does: the client's people (name, role, primary) with their own phones and emails. There can only be **one active
primary contact** per client: when another one is marked as primary, the previous one automatically stops being it. An
**inactive contact is never the primary one**. The **client's** phones/emails go through `/api/v1/contacts/CLIENT/{id}` and those of
each **person** through `/api/v1/contacts/CLIENT_CONTACT/{id}` (chapter 01, section 6).

Who can: `clients.read` (view), `clients.update` (create/edit); standalone contact points, `contacts.manage`.

How it is used:
- `GET /api/v1/clients/{publicId}/contacts?includeInactive=false`
- `POST /api/v1/clients/{publicId}/contacts` `{ "fullName": "Ana Pérez", "role": "Compras", "isPrimary": true, "contactPoints": [ { "contactType": "EMAIL", "value": "ana@cliente.pr", "isPrimary": true } ] }`
- `PATCH /api/v1/clients/{publicId}/contacts/{id}` `{ "fullName"?, "role"?, "isPrimary"?, "isActive"? }`

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| `fullName` empty / too long | required, ≤ 150 | `The contact name is required.` / `Cannot exceed 150 characters.` | 400 |
| `role` > 80 | — | `Cannot exceed 80 characters.` | 400 |
| Contact point without a type | — | `Each contact method needs contactType.` | 400 |
| Malformed email / phone | — | `Invalid email.` / `Invalid phone.` | 400 |
| Contact of another client or nonexistent | looked up through the client | `Contact '<id>' not found.` | 404 |
| Two active primaries (race) | unique index | `There is already an active primary contact for this client.` | 409 |
| Nonexistent client (or from another company) | — | `Client not found.` | 404 |

### 1.6 Client status

Statuses (`ClientStatus`), since 2026-10-21 in this order: **PROSPECT** (pipeline) → **ACTIVE** (initial: the new client is born Active); **SUSPENDED** (lateral, reversible). The domain **has no terminal stage**: “Deactivate” is not a status, it deactivates the client (it stays hidden unless “Show inactive” is on). Each company can turn off “Prospect” or change the names and the order in **System → Catalogs → Statuses** (chapter 01, section 5). The
transitions go through the status engine of chapter 01 (section 5): the stage is validated, the history is written
(`GET /api/v1/status/history/CLIENT/{id}`) and effects are triggered. A **SUSPENDED** client cannot activate contracts (see 4.7).

How it is used: `POST /api/v1/clients/{publicId}/status` `{ "toCode": "SUSPENDED", "comment": "morosidad" }` (`clients.update`).

| Case | Exact message | HTTP |
|---|---|---|
| `toCode` empty | `The target status is required.` | 400 |
| Nonexistent or disabled status | `The status 'X' does not exist or is not enabled for this company.` | 422 |
| Same status | `The record is already in that status.` | 422 |
| Illegal jump / lateral not allowed | `Illegal jump: from 'A' you can only move forward to 'B'.` / `Moving to 'X' from 'Y' is not allowed under this company's model.` / `From the lateral 'SUSPENDED' you can only go back to 'ACTIVE'…` | 422 |

### 1.7 Deactivating and reactivating the client

What it does: logical deactivation (`isActive = false`), it is never deleted: the client disappears from the list (unless `includeInactive=true`),
its record remains accessible and it can be reactivated. It is idempotent (204 even if it was already deactivated).

Who can: `clients.update`. How it is used: `POST /api/v1/clients/{publicId}/deactivate` / `.../reactivate` → 204.

> **Updated by Batch 3**: a deactivated client blocks the creation of anything new (shipments, contracts, rates,
> special services, portal invitations) with `409` `The client has been deactivated; only its history can be viewed.`; **since 2026-10-11 (D39) the
> quote too** (`POST /api/v1/billing/contract-rate-quote`) responds with that same 409;
> reads and closures/cancellations are still allowed. See [chapter 03, section 5](03-ordenes-de-transporte.md#5-deactivated-client-only-what-is-new-is-blocked).

---

## 2. Consignees and locations

What it does: a single table stores the **client's addresses** (CORPORATE = physical, BILLING = mailing; at most one active
of each per client), its **warehouses or pickup points** (PICKUP, BOTH), its **consignees** (DELIVERY) and the
operator's **shared locations** (without an owner, `isShared: true`). The owner is visible and editable. It is never deleted:
deactivation is logical and keeps the history; an inactive location does not appear in the dropdowns.

Who can: `locations.read` (view), `locations.create` (create), `locations.update` (edit, activate/deactivate).

How it is used:
- `GET /api/v1/locations?clientId={publicId}&includeShared=true&includeInactive=false&locationType=&search=` — with `clientId`,
  the client's own plus the shared ones; without `clientId`, all of the company's.
- `POST /api/v1/locations` `{ "clientPublicId": null|guid, "code"?, "name", "locationType": "DELIVERY", "line1", "line2"?, "city", "state"?, "postalCode"?, "country": "PR", "defaultServiceMinutes": 0, "defaultWindowStart"?: "08:00:00", "defaultWindowEnd"?: "12:00:00", "accessNotes"?, "deliveryNotes"?, "allowDupInvoice": false }`
- `GET /api/v1/locations/{publicId}` · `PATCH /api/v1/locations/{publicId}` (only what changes; `makeShared: true` removes the owner;
  `clientPublicId` changes it; `clearWindow: true` clears the window; an empty string clears an optional text; `rowVersion` optional)
- `POST /api/v1/locations/{publicId}/deactivate` / `.../reactivate` → 204

Automatic effects: if the location is its client's **default warehouse** and stops serving as such (it is deactivated,
changes owner or changes type to something other than PICKUP/BOTH), the client's pickup goes back to "same as the corporate one".
`allowDupInvoice` (allow repeated invoices for that consignee) is stored here and consumed by the Shipments module.

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| `locationType` empty / unknown | `LocationType` list | `The location type is required.` / `List LocationType 'X' not found.` (list: `Unknown location type.`) | 400 / 404 / 400 |
| `name`, `line1`, `city` empty | required | `The name is required.` / `Address (line 1) is required.` / `City is required.` | 400 |
| Long texts | name 200, code 40, lines 200, city/state 100, postal 20, notes 500 | `At most N characters.` | 400 |
| `country` unknown | `Country` list (default PR) | `List Country 'X' not found.` | 404 |
| Window with only a start or only an end | both or neither | `The time window needs both a start and an end time.` (field `defaultWindowStart`) | 400 |
| End not after the start | — | `The window end time must be after the start time.` (field `defaultWindowEnd`) | 400 |
| `defaultServiceMinutes` negative | — | `Service minutes cannot be negative.` | 400 |
| CORPORATE or BILLING without a client | they always belong to a client | `A corporate or mailing (billing) address must belong to a client; it cannot be shared.` | 400 |
| Second active CORPORATE of the client | one active | `The client already has an active corporate address; deactivate or edit it.` | 409 |
| Second active BILLING of the client | one active | `The client already has an active mailing (billing) address; deactivate or edit it.` | 409 |
| `makeShared` and `clientPublicId` at the same time | mutually exclusive | `Specify a client or mark the location as shared, not both.` | 400 |
| `clientPublicId` from another company | — | `Client not found.` | 404 |
| Foreign or nonexistent location | — | `Location '<guid>' not found.` | 404 |
| `rowVersion` different | concurrent editing | `The location was modified by another user; reload and try again.` | 409 |
| `contactName` too long (Batch 45) | contact person, optional, max. 150; an empty string clears it in the PATCH | `At most 150 characters.` (field `contactName`) | 400 |

Frequent cases: reactivating a CORPORATE/BILLING when there is already another active one responds with the same 409; changing the type of a
consignee to CORPORATE when an active one already exists does too.

### 2.1 Consignee import (Batch 38)

What it does: creates **many consignees at once** from a CSV file, in two steps: **validate** (reads the file,
checks each row and shows a preview **without creating anything**) and **confirm** (creates the valid rows) or **discard**. The
file is read with a **template**: each **column position** is assigned a field (the header text does not
matter). Every imported consignee is of type **DELIVERY**. It only creates: a code that already exists is an error, an existing consignee is never
updated (use its record for that). It uses the same rules as manual creation (`POST /locations`) and
leaves the same audit trail.

Who can: **Lists** module (`CATALOG`). Viewing templates, the field list and a batch: `locations.read`. Creating and editing
templates, validating, confirming and discarding: `locations.create`. There are no new permissions.

#### Templates

How it is used:
- `GET /api/v1/locations/import-templates?clientId=&includeInactive=false` — with `clientId`, the general ones and those of that client.
- `POST /api/v1/locations/import-templates` `{ "name": "Consignatarios Farmacias", "columns": [ {"position":1,"field":"code"}, {"position":2,"field":"name"}, {"position":3,"field":"line1"}, {"position":4,"field":"city"}, {"position":5,"field":"clientCode"}, {"position":6,"field":"contactPhone"} ], "defaults": {"country":"PR","defaultServiceMinutes":"10"}, "clientPublicId"?: guid, "delimiter": ",", "hasHeader": true }`
- `GET` / `PATCH /api/v1/locations/import-templates/{publicId}` · `POST .../{publicId}/deactivate` · `POST .../{publicId}/reactivate`
- `GET /api/v1/locations/import/fields` — the 18 fields (17 until Batch 45, which added `contactName`) with `required` (required in the row) and `defaultAllowed` (accepts
  a default value in the template).

A template is **general** (without a client, it works for any batch) or **for one client** (it only works for batches of that
client). The `defaults` fill in a field when the cell is empty or the column does not exist; a cell with a value always wins.
Consignee templates are not visible through `/api/v1/import-templates` (that route is for shipments) nor the other way around.

| Field | Required in the row | Accepts default | Goes to |
|---|---|---|---|
| `code` | no | no | consignee code (max. 40) |
| `name` | **yes** (column) | no | name (max. 200) |
| `line1` | **yes** (column) | no | address line 1 (max. 200) |
| `line2` | no | yes | line 2 (max. 200) |
| `city` | **yes** (column or default) | yes | city (max. 100) |
| `state` | no | yes | state (max. 100) |
| `postalCode` | no | yes | postal code (max. 20) |
| `country` | no (PR if missing) | yes | 2-letter ISO country, active and not disabled by the company |
| `clientCode` | no | no | owner: client code |
| `clientName` | no | no | owner: client name |
| `defaultServiceMinutes` | no (0) | yes | service minutes, integer ≥ 0 |
| `defaultWindowStart` / `defaultWindowEnd` | no (both or neither) | yes | time window, 24-hour `HH:mm` (also `H:mm` and `HH:mm:ss`) |
| `accessNotes` / `deliveryNotes` | no | yes | notes (max. 500) |
| `allowDupInvoice` | no (no) | yes | allow repeated invoices: `sí/si/s/yes/y/true/1` or `no/n/false/0` (case-insensitive) |
| `contactPhone` | no | yes | phone, stored as a **primary PHONE** contact (label “Phone”); since Batch 45 the 10 digits are stored (see §2.2) |
| `contactName` (Batch 45) | no | yes | the consignee's contact person (max. 150) |

| Case (template) | Exact message | HTTP |
|---|---|---|
| No columns | `Specify at least one column.` (field `columns`) | 400 |
| More than 100 columns | `At most 100 columns.` | 400 |
| Position &lt; 1 / &gt; 500 / repeated | `The position must be 1 or greater.` / `The position cannot exceed 500.` / `Position N is repeated.` (field `columns[i].position`) | 400 |
| Unknown / repeated field | `Unknown field: X.` / `The field X is repeated.` (field `columns[i].field`; in defaults, `defaults.X`) | 400 |
| The `name` or `line1` column is missing | `The template must include the name and line1 columns.` (field `columns`) | 400 |
| No `city` in a column or in defaults | `The template must give the city (city) in a column or in the defaults.` | 400 |
| `clientCode` and `clientName` at the same time | `Use clientCode or clientName, not both.` | 400 |
| Default for `code`, `name`, `line1`, `clientCode` or `clientName` | `The field X does not accept a default value.` (field `defaults.X`) | 400 |
| Default with an invalid value (minutes, times, `allowDupInvoice`, country format, length, phone) | the same message the row would give (table below), in `defaults.X` | 400 |
| Empty / long name | `The template name is required.` / `At most 120 characters.` | 400 |
| Invalid delimiter | `The delimiter must be a single character (e.g. ',' or ';'), other than a quote or a line break.` | 400 |
| Repeated name (among consignee templates) | `An import template with that name already exists.` | 409 |
| `clientPublicId` and `makeGeneral` at the same time (PATCH) | `Specify a client or mark the template as general, not both.` | 400 |
| `defaults` and `clearDefaults` at the same time (PATCH) | `Specify new defaults or clearDefaults, not both.` | 400 |
| Deactivating an inactive one / reactivating an active one | `The template is already inactive.` / `The template is already active.` | 409 |
| Nonexistent template, from another company or a shipment template | `Import template not found.` | 404 |
| `kind` other than `CONSIGNEE` in the body | `Unknown template type: X.` | 400 |

#### Validate (step 1 of 2)

How it is used:
- `POST /api/v1/locations/import/validate` (JSON) `{ "templatePublicId": guid, "clientPublicId"?: guid, "content": "<CSV>", "fileName"?: "archivo.csv" }`
- `POST /api/v1/locations/import/validate-file` (`multipart/form-data`): `templatePublicId`, `clientPublicId` (optional) and the
  file in `file`.
- `GET /api/v1/locations/import/{batchPublicId}` — shows the preview again (and, after confirming, the result).

Limits: **5,000 rows and 2 MB** per file (the full multipart request, up to 4 MB). The file must be in **UTF-8**.

**Batch client** (`clientPublicId`) is optional. **Owner of each row**, in this order: the `clientCode` column; if not, the
`clientName` column; if not, the batch client; if there is none either, the row is left **shared** (without an owner, visible to all
clients). A **suspended** client does accept consignees; a **deactivated** one does not.

The response (200) brings `batchPublicId`, `status` (`VALIDATED`), `rowCount`, `validRows` and `rows[]` with, per row: `values`
(what was read, already normalized: time `HH:mm`, `allowDupInvoice` `true/false`, country in uppercase, formatted phone),
`owner` (`clientPublicId`/`clientName`, or `isShared: true`; `null` if it could not be resolved), `errors` per field (the row will **not**
be created), `warnings` (notices: the row **will** be created) and `isValid`.

| Case (request) | Exact message | HTTP |
|---|---|---|
| No content or empty file | `Provide the contents of the CSV file.` (field `content` or `file`) | 400 |
| More than 5,000 rows or 2 MB | `The file exceeds the limit of 5,000 rows or 2 MB.` (field `content` or `file`) | 400 |
| Multipart request over 4 MB | (the server cuts it off) | 413 |
| File without data rows | `The file has no data rows.` | 400 |
| Nonexistent template, a shipment template or one of another client | `Import template not found.` | 404 |
| Inactive template | `The template is inactive; reactivate it or choose another one.` (field `templatePublicId`) | 400 |
| Nonexistent batch client or from another company | `Client not found.` | 404 |
| Deactivated batch client | `The client has been deactivated; only its history can be viewed.` | 409 |
| `validate` without `Content-Type: application/json` | — | 415 |
| Without the permission / Lists module off | `Missing permission 'locations.create'.` / `module_disabled` | 403 |

| Case (row; the preview responds 200 and the row is left invalid) | Exact message (field) |
|---|---|
| No name / line 1 / city | `The name is required.` (`name`) / `Address (line 1) is required.` (`line1`) / `City is required.` (`city`) |
| Long text | `At most N characters.` |
| Country with an invalid format | `The country code must be ISO 3166-1 alpha-2 (2 letters).` (`country`) |
| Country unknown or disabled by the company | `Unknown country: XX.` (`country`) |
| Minutes not integer / negative | `Service minutes must be a whole number.` / `Service minutes cannot be negative.` (`defaultServiceMinutes`) |
| Unreadable time | `The time must have the format HH:mm (24 hours).` (`defaultWindowStart` or `defaultWindowEnd`) |
| Only one time of the window | `The time window needs both a start and an end time.` (the missing one) |
| End not after the start | `The window end time must be after the start time.` (`defaultWindowEnd`) |
| Unreadable `allowDupInvoice` | `The value of allowDupInvoice must be yes or no (also 1/0 or true/false).` |
| Invalid phone | `Invalid phone.` (`contactPhone`) |
| `clientCode` with no match | `There is no client with the code 'X'.` (`clientCode`) |
| `clientName` with no match / with several | `There is no client with the name 'X'.` / `There is more than one client with the name 'X'; use clientCode.` (`clientName`) |
| Row client deactivated | `The client has been deactivated; only its history can be viewed.` (`clientCode` or `clientName`) |
| ~~The row names a client other than the batch's~~ | It is **no longer an error** since Batch 45 (2026-10-13): the batch client is the *default client*, and a row with its own valid client is left with THAT client (see §2.2). The message `The row indicates client 'A', but the batch belongs to client 'B'.` no longer appears. |

#### Duplicates

They are compared by **owner** (the same client, or both shared). The code is case-insensitive; name and line 1 are
compared without accents, without case and without double spaces. A client row clashes with that client's consignees
**and** with the shared ones; a shared row, only with the shared ones.

| Case | Exact message | Type |
|---|---|---|
| Code repeated in the file (same owner) | `The code X is repeated in row N of the file.` (`code`, in the later row) | error |
| Name repeated in the file (same owner; the address does not count) | `The name 'X' is repeated in row N of the file; add something distinctive to the name (or the number).` (`name`, in the later row) | error |
| Code already used by an active consignee | `A consignee with the code 'X' ('Name') already exists.` (`code`) | error |
| Code used by an inactive consignee | `An inactive consignee with the code 'X' ('Name') already exists; reactivate it instead of importing it.` (`code`) | error |
| Name of an active consignee of the same owner (the address does not count) | `An active consignee with the same name already exists. Add something distinctive to the name (or the number) to tell them apart.` (`name`) | error |
| Name of an inactive one of the same owner | `An inactive consignee with the same name already exists; reactivate it instead of importing it, or add something distinctive to the name (or the number).` (`name`) | error |
| Shared row with a code used by a client's consignee | `The code X is also used by a consignee of a client; for that client it will keep resolving to theirs.` | warning |

#### Confirm (step 2 of 2) and discard

How it is used: `POST /api/v1/locations/import/{batchPublicId}/confirm` with an empty body, `{}` (all valid rows) or
`{ "rows": [1, 3] }` (only those rows; the rest are left `skipped`). `POST /api/v1/locations/import/{batchPublicId}/discard`
discards a pending batch.

What it does when confirming: each chosen row is created **in its own transaction** (one that fails does not bring down the others). Before
creating, it is checked again against what exists **at that moment** (the client is still active; nobody took the code and the name + line 1
since it was validated): imports of the same company are confirmed **one row at a time**,
so two batches with the same new code never create two consignees. The batch moves to **CONFIRMED** with the comment
“Consignees created: C; failed: F; skipped: O.”. The response brings `created`, `failed`, `skipped` and `rows[]` with
`locationPublicId` (created) or `error` (failed). Failed rows are not retried: validate a new file with them.

| Case | Exact message | HTTP |
|---|---|---|
| `rows` with a row that does not exist | `Row N does not exist in the batch.` (field `rows[N]`) | 400 |
| `rows` with a row that has errors | `Row N has errors and cannot be confirmed.` (field `rows[N]`) | 400 |
| No valid row to create | `The batch has no valid rows to create.` (field `rows`) | 400 |
| Batch already confirmed | `The batch was already confirmed; validate a new file to import more consignees.` | 409 |
| Discarded batch | `The batch was discarded; validate the file again.` | 409 |
| Two confirmations at once, or discarding while confirming | `The batch is already being confirmed or was confirmed; check its result.` | 409 |
| Batch client deactivated after validating | `The client has been deactivated; only its history can be viewed.` | 409 |
| Discarding a batch that is not pending | `Only a batch pending confirmation can be discarded.` | 422 |
| Nonexistent batch, from another company or a shipment batch | `Import batch not found.` | 404 |
| Row that clashes when confirming (someone created the code in the meantime) | the duplicate message from the previous table, in `rows[i].error` | 200 (failed row) |
| Unexpected error in a row | `Unexpected error creating the consignee; check the row and try again.` in `rows[i].error` | 200 (failed row) |

Batch status (`ImportBatchStatus`): `VALIDATED` → `CONFIRMED` (confirm) or → `DISCARDED` (discard). The history
(`GET /api/v1/status/history/IMPORT_BATCH/{id}`) requires the `orders.view` permission (the one of the shipment importer): a user who
only has consignee permissions gets 403 there, although they do see the batch through `GET /api/v1/locations/import/{id}`.

### 2.2 Consignee import: Excel, “(Do not use)”, joined columns, default client and contact person (Batch 45, F-B2)

What changes (same permissions and module as §2.1; no new permissions). Screen: [frontend/fb2-importacion-consignatarios.md](frontend/fb2-importacion-consignatarios.md).

- **Excel (.xlsx) in addition to CSV**, with the same flow (validate → preview → confirm). The server reads the workbook (ExcelDataReader
  library, MIT license): a single **sheet**, the one requested in `sheet` or the first one. Each cell is converted to text as
  the user sees it: an integer without “.0” (`901`), with a leading-zeros format (`00000`) it is padded (`00901`); a
  date with zero time → `yyyy-MM-dd`, with time → `yyyy-MM-dd HH:mm`; a **time-only** cell (08:00) → `HH:mm`, ready for the
  time window. The old (binary) `.xls` is not accepted. The limits are those of CSV (5,000 data rows, 2 MB file;
  in addition the uncompressed workbook cannot exceed 64 MB).
- **CSV in UTF-8 or Windows-1252**: the encoding is detected automatically (BOM mark; otherwise UTF-8 when the bytes are; otherwise
  Windows-1252, which is what Excel on Windows saves when exporting “CSV”). The ñ and accents arrive fine in both cases.
- **Inspect the file without saving anything**: `POST /api/v1/locations/import/inspect-file` (multipart: `file`, `delimiter`,
  `hasHeader`, optional `sheet`; `locations.create`) responds `format` (`CSV`/`XLSX`), `sheets` (workbook sheets; empty for CSV),
  `sheet` (the one read), `rowCount`, `columns[]` (`position`, `header`, up to 3 `samples`) and `sampleRows` (the first 3 rows).
  The screen uses it for the sheet selector and to show headers and samples in the editor.
- `validate-file` accepts `sheet`. The JSON `validate` is still CSV only (text in `content`).
- **“(Do not use)” column**: in `columns`, `{ "position": 3, "field": "ignore" }`. It is saved in the template (the editor
  remembers it) and when importing the column is ignored entirely; **the row is not skipped**. A “Do not use” column cannot also have
  another field.
- **One column to several fields** (e.g. the code goes to `code` and is also part of the name): the same `position` in two
  entries with different fields.
- **One field with several joined columns**: the field repeated in several positions, with `part` (send 1, 2…) and `separator` (text
  that goes BEFORE that part; without it, a space). Example “Name = Code + “ - ” + Name”:
  `[{"position":1,"field":"code"},{"position":1,"field":"name","part":1},{"position":2,"field":"name","part":2,"separator":" - "}]`.
  An empty cell leaves no separator; if all the parts come empty, the field takes its default value. Text fields can be joined
  (number, name, addresses, town, state, ZIP, notes, phone, contact person), up to 5 columns; fields
  with their own format (country, client, minutes, times, repeated invoice) take a single one. When saving, the server numbers the parts and
  removes `part`/`separator` where they do not apply.
- **Default client**: the batch's `clientPublicId` is the client of **the whole file**; a row that brings its own valid client
  (`clientCode`/`clientName`) is left with **that** client even if it is a different one (before, it was an error). The template saves its default
  client: `defaultClientPublicId` on create and in the PATCH (`clearDefaultClient: true` removes it); the DTO brings
  `defaultClientPublicId` and `defaultClientName`. Confirming with `{ "rememberClient": true }` saves the batch client in the template
  (or none, if it was “shared”).
- **Contact person**: new consignee field `contactName` (`dbo.Location.ContactName`, max. 150) in `POST`/`PATCH
  /locations`, in the DTO and as a mapping target (accepts a default value). The phone is still a PHONE contact.
- **Phone**: the formats of real files are accepted (`787-555-0101`, `787 555-0101`, `7875550101`, `(787)555-0101`,
  `+1 787…`) and the **10 digits** are stored. If the cell brings **two or more numbers**, the first one is taken and the row carries a warning.
- A Puerto Rico or United States **ZIP** that lost its zeros (3 or 4 digits) is padded to 5 (`901` → `00901`). An empty ZIP is not
  an error (the next batch will infer it from the town with the postal code list).

| Case | Exact message | HTTP / type |
|---|---|---|
| `.xls` file (Excel 97-2003) | `Unsupported file format: use CSV or Excel (.xlsx).` (field `file`) | 400 |
| Damaged workbook or not an .xlsx | `The Excel file could not be read; open it and save it again as .xlsx.` (field `file`) | 400 |
| `sheet` that is not in the workbook | `The workbook has no sheet 'X'.` (field `sheet`) | 400 |
| Workbook over 5,000 rows, 2 MB or 64 MB uncompressed | `The file exceeds the limit of 5,000 rows or 2 MB.` | 400 |
| Empty file (also `inspect-file`) | `Provide the contents of the CSV file.` (field `file`) | 400 |
| “Do not use” column with another field | `Column N is marked “Do not use” and also has a field assigned.` (field `columns[i].field`) | 400 |
| The same column twice in the same field | `Position N is repeated.` (field `columns[i].position`) | 400 |
| Own-format field in two columns | `The field X is repeated.` | 400 |
| More than 5 columns in a field | `The field X accepts at most 5 joined columns.` | 400 |
| Separator over 10 characters | `The separator accepts at most 10 characters.` (field `columns[i].separator`) | 400 |
| Default client different from the client of a client template | `The default client must be the same client as the template's.` (field `defaultClientPublicId`) | 400 |
| `defaultClientPublicId` and `clearDefaultClient` at the same time | `Specify a default client or remove it, not both.` | 400 |
| Default client deactivated / from another company | `The client has been deactivated; only its history can be viewed.` / `Client not found.` | 409 / 404 |
| Long contact person | `At most 150 characters.` (`contactName`) | row with error / 400 in `/locations` |
| Two phones in the cell | `The phone cell brings more than one number; the first one was taken (7875550101).` | warning (the row is imported) |
| Consignee number repeated in the file | `The code X is repeated in row N of the file.` (names the number and the first row where it appears) | row error |

Structure (section 2026-10-13 of `Diseño/logistica-db-update.sql`): `dbo.Location.ContactName NVARCHAR(150) NULL` and
`dbo.ImportTemplate.DefaultClientId INT NULL` with a key to `dbo.Client`. No new permissions.

Frequent cases:
- **Excel**: save the file as **“CSV UTF-8 (Comma delimited)”**. With “CSV (Comma delimited)” Excel saves it in
  Windows-1252 and the accents arrive damaged (“Mar�a”). If your Excel uses `;`, create the template with `"delimiter": ";"`.
- **Shared consignees** (for all clients): do not specify a batch client or a client column.
- **One client with many consignees**: specify the batch client; the client column is not needed.
- **“…reactivate it instead of importing it”**: the consignee already existed and is inactive; reactivate it from its record
  (`POST /locations/{id}/reactivate`) and remove that row from the file.
- **Large files**: confirming 5,000 rows takes about half a minute; do not close the screen. If the connection drops
  halfway, what was created up to that moment stays created, but the batch may be left `VALIDATED` and reserved (confirm or discard
  respond 409 “The batch is already being confirmed or was confirmed…”). In that case validate **the same file again**: the
  rows already created come out with “A consignee with the code… already exists” and only the rest is confirmed.

### 2.3 Consignee import: town, state and ZIP with the postal code list, and mapping variants (Batch 46, F-B3)

Same permissions and module as §2.1 (no new permissions). Screen: [frontend/fb3-variantes-y-codigos-postales.md](frontend/fb3-variantes-y-codigos-postales.md).

**Postal code list.** `dbo.PostalLocality` (global) gains `ZipClass` (`STANDARD` normal, `PO_BOX` P.O. boxes, `UNIQUE` unique to one entity, `MILITARY`
APO/FPO; NULL = unclassified), `LocaleType`, `DistrictName`, `DistrictNo`, `Latitude`, `Longitude` and `GeoAccuracy` (section 2026-10-14 of
`Diseño/logistica-db-update.sql`, with `CK_PostalLocality_ZipClass` and `IX_PostalLocality_PostalCode`). They are filled by the separate **data** script
`Diseño/logistica-db-zip-catalog.sql` (41,486 ZIPs; USPS “Zip Locale Detail” and **GeoNames**, CC BY 4.0 license: credit must be given), which is run by hand with
`sqlcmd … -I -f 65001 -i "Diseño/logistica-db-zip-catalog.sql"` **after** `db-update`; it is idempotent and, if the columns are missing, it stops without touching anything
(“logistica-db-zip-catalog.sql: faltan las columnas nuevas de dbo.PostalLocality; aplique primero logistica-db-update.sql (db-update). No se cambió nada.”). The API reads
the list into memory for one hour: after running the script, restart the API (or wait).

**GET /api/v1/postal-localities** (authenticated user) returns `zipClass` in each row and accepts `?zipClass=` (comma-separated list of `STANDARD`, `PO_BOX`,
`UNIQUE`, `MILITARY`, or `DELIVERY` = the delivery ones: normal, unique and the unclassified). Without a filter all of them come out (mailing addresses). Creating and editing
consignees use `DELIVERY`.

**Inference during import** (rows of a country covered by the list: Puerto Rico and United States; before requiring the mandatory fields):

- ZIP only → `city` (in Puerto Rico the municipality, with accents) and `state` from the list, if missing.
- Town only → `postalCode` = the FIRST delivery ZIP of the town (postal city; if none, municipality), in a fixed order: `STANDARD`, then `UNIQUE`, then
  unclassified; for the same type, the lowest code. With the row's state, a choice is made among towns of the same name in several states.
- ZIP and town → both are saved; if they do not match, a warning. The row's state is never changed (if it differs from the ZIP's, a warning).
- `PO_BOX` or `MILITARY` ZIP → it is replaced by the first delivery ZIP of the SAME city (postal city of the ZIP; if it has none, its municipality); the row carries
  `originalPostalCode` and the warning “Postal code adjusted…”. Without a compatible one, the row is rejected.
- ZIPs are compared as 5-digit text (ZIP+4 counts by its 5); towns, without accents or case.
- Each row brings `inferred` (completed fields: `city`, `state`, `postalCode`) and `originalPostalCode` in the preview (`validate`, `GET` of the batch) and in the
  result of `confirm`.
- The template no longer requires `city`: `city` **or** `postalCode` is enough (in a column or in defaults). In the field list (`/locations/import/fields`), `city` stops being
  `required`.

**Mapping variants** (consignee templates only): `variants: [{ name, conditions: [{ position, operator, value }], columns: [...] }]` on create and in the PATCH
(`ImportTemplate.VariantsJson`; `ColumnsJson` keeps the columns of the first one). `operator`: `EMPTY`, `NOT_EMPTY`, `EQUALS`, `CONTAINS` (text without accents or
case). Each row gets the FIRST variant whose conditions are all met; one without conditions always applies (it can only go last). Each variant is
validated with the column rules; the defaults are shared. PATCH: `variants` absent = no change; `[]` = back to a single variant (with `columns` or those of the
first one). Rows bring `variant` (1..n) and `variantName`; the one that does not fit brings `cells` (its cells). A template without variants works the same as before.

| Case | Exact message | HTTP / type |
|---|---|---|
| ZIP in another format | `The postal code X is not valid: use 5 digits (or ZIP+4, such as 00901-1234).` (`postalCode`) | row error |
| ZIP not in the list | `The postal code X is not in the postal code list; correct it, or leave it empty and give the town.` (`postalCode`) | row error |
| Town not in the list | `The town 'X' is not in the postal code list; correct it or give the postal code.` (`city`) | row error |
| Town without a delivery ZIP | `The town 'X' has no delivery postal code (only P.O. box or military ones); give the postal code of the physical address.` (`city`) | row error |
| Town in several states (U.S.) | `The town 'X' exists in several states (IL, MA); give the state or the postal code.` (`city`) | row error |
| P.O. box / military ZIP without a substitute | `The postal code X is for P.O. boxes and Y has no delivery postal code to replace it with; give the postal code of the physical address.` (or `…is for military mail (APO/FPO)…`) (`postalCode`) | row error |
| No variant applies | `No variant of the template applies to this row; check the variants' conditions.` (`variant`) | row error |
| P.O. box ZIP replaced | `Postal code adjusted: 00734 is for P.O. boxes; 00715 was used, a delivery code in Ponce.` | warning |
| Town / state / ZIP completed | `Town taken from postal code 00952: Toa Baja.` · `State taken from the postal code list: PR.` · `Postal code assigned: 00956, the first delivery code of Bayamón.` | warning |
| ZIP and town do not match | `The postal code 00716 belongs to Ponce, not Caguas; both are saved as they come.` | warning |
| State different from the ZIP's | `The state NY does not match the postal code's (PR); it is saved as it comes.` | warning |
| Template without town or ZIP | `The template must give the city or the postal code, in a column or in the defaults.` (`columns`; before “…the city (city) in a column or in the defaults.”) | 400 |
| Variant without a condition that is not the last | `Variant N has no condition and leaves the following ones unused; move it to the end or give it a condition.` (`variants[i].conditions`) | 400 |
| “equals” / “contains” without a value | `The «equals» or «contains» condition needs a value.` (`variants[i].conditions[j].value`) | 400 |
| Unknown operator | `Unknown condition: X. Use EMPTY, NOT_EMPTY, EQUALS or CONTAINS.` (`…operator`) | 400 |
| Condition column out of range | `The position must be 1 or greater.` / `The position cannot exceed 500.` (`…position`) | 400 |
| More than 10 variants / more than 5 conditions | `A template accepts at most 10 variants.` / `A variant accepts at most 5 conditions.` | 400 |
| Long variant name / long value | `At most 60 characters.` / `At most 100 characters.` | 400 |
| Invalid mapping of a variant | the usual message with the variant's key (e.g. `variants[1].columns`: `The template must include the name and line1 columns.`) | 400 |
| `columns` and `variants` at the same time (or `columns` in the PATCH of a template with variants) | `This template uses variants: the columns go inside each variant (variants), not in columns.` (`columns`) | 400 |
| Variants in a shipment template | `Mapping variants only exist in consignee templates.` (`variants`) | 400 |
| Unknown `zipClass` in `/postal-localities` | `Unknown postal code type: X. Use STANDARD, PO_BOX, UNIQUE, MILITARY or DELIVERY.` (`zipClass`) | 400 |

Frequent cases:
- **File with two column layouts**: one variant per layout; the condition is usually a column that only one of the two brings (e.g. column 9 = “PUERTO
  RICO” in one and empty in the other).
- **The town brings the neighborhood**: if the file brings “Bo. Pueblo” in one column and the municipality in another, map the municipality to **Town** and the neighborhood to **Address (line 2)**.
- **All ZIPs come out “unclassified” / there is no “Postal code adjusted”**: `logistica-db-zip-catalog.sql` has not been run (or the API still has the old list in
  memory: restart it).

---

## 3. Client addresses and pickup point (summary)

- **Physical**: the client's active location of type CORPORATE (`physicalAddress` in the record).
- **Mailing**: the active one of type BILLING (`postalAddress`); if it does not exist, it is understood as "same as the physical one".
- **Usual pickup**: the warehouse chosen in the profile (`defaultPickupLocationPublicId`, type PICKUP or BOTH) or, if there is none,
  the corporate one (`pickupAddress.isDefaultFromCorporate: true`). There can be as many warehouses as wanted (`pickupLocations`).
- **Consignees**: addresses of type DELIVERY; they are not clients, they do not appear in the client list and have no contract.

---

## 4. Contracts

### 4.1 Current contract

A client's "current contract" on a date is the active contract with status **ACTIVE** and `startDate` ≤ that date.
**The end date is not evaluated**: a contract with a past `endDate` remains current until someone moves it to EXPIRED or
CANCELLED (if it is not renewed, you continue with what there is). If there is no ACTIVE one, the **most recent DRAFT** by start
date is used (to set up rates before activating). Never EXPIRED or CANCELLED. It is the contract used by the quote, the
special services and the `billingSummary` summary of the record and of the client list.

### 4.2 List and record

Who can: `contracts.read`. How it is used: `GET /api/v1/contracts?clientId={publicId}&includeInactive=false`;
`GET /api/v1/contracts/{publicId}` → number, title, dates, `autoRenew`, status, currency, billing trigger
(`billingTrigger`: BY_PICKUP | BY_DELIVERY | MIXED), notes, `billingModel` (5 flags + `summary`), `dispatchFee`,
`codFee` (`null` if it was never set), `serviceLevels`, `canEdit` (whether the current status allows editing) and `rowVersion`.

### 4.3 Additional contract

What it does: creates another contract for the client, in DRAFT, with "Per service" on and automatic number `{Code}-C{n+1}`
(or the explicit number that is sent).

Who can: `contracts.create`. How it is used: `POST /api/v1/contracts`
`{ "clientPublicId", "contractNumber"?, "title", "startDate", "endDate"?, "autoRenew": false, "currency"?, "billingTrigger"?, "notes"?, "serviceLevels"? }`

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| `title` empty / > 200 | required | `The contract title is required.` / `At most 200 characters.` | 400 |
| `endDate` < `startDate` | validity period | `The end date cannot be earlier than the start date.` | 400 |
| `contractNumber` already exists | unique per company | `A contract with the number 'X' already exists.` | 409 |
| `currency` / `billingTrigger` unknown | `Currency` / `BillingModel` lists | `List Currency 'X' not found.` / `List BillingModel 'X' not found.` | 404 |
| Invalid SLA | see 4.6 | — | 400 |

### 4.4 General data (title, "client since", end date, renewal, currency, trigger, notes)

What it does: inline editing of the general data. **`startDate` ("client since") is editable** and decides the current
contract; `endDate` and `autoRenew` are informational (there is no automatic expiration or renewal). `clearEndDate: true`
clears the end date; `notes: ""` clears the notes.

Who can: `contracts.update`. How it is used: `PATCH /api/v1/contracts/{publicId}` `{ "title"?, "startDate"?, "endDate"?, "clearEndDate"?, "autoRenew"?, "currency"?, "billingTrigger"?, "notes"?, "rowVersion"? }`

| Case | Exact message | HTTP |
|---|---|---|
| End date (new or existing) earlier than the start (new or existing) | `The end date cannot be earlier than the start date.` (field `startDate` if only the start was sent; `endDate` otherwise) | 400 |
| `endDate` and `clearEndDate` at the same time | `Specify an end date or mark it to be cleared, not both.` | 400 |
| `rowVersion` different | `The contract was modified by another user; reload and try again.` | 409 |
| Status that does not allow editing (EXPIRED/CANCELLED by default) | `The current status does not allow the action 'EDIT_CONTRACT'.` | 422 |

### 4.5 Billing model, dispatch fee and COD fee

What it does: the contract's **5 checkboxes** — Per service, Extra piece, Dispatch, COD, Special — and their readable summary
("Per service, Extra piece, Dispatch, COD, Special"; "No components" if none). **Unchecking deletes nothing**: the dispatch
fee, the COD fee, the rates and the special services are kept and simply not charged (when checked
again, they reappear). The **dispatch fee** is an amount per order. The **COD fee** is **FIXED** (fixed amount per order)
or **PERCENT** (percentage **of the COD amount collected**, never of the shipment total); "none" = COD checkbox off, which is
the default value of every new contract.

Who can: `contracts.update`. How it is used:
- `PATCH /api/v1/contracts/{publicId}/billing-model` `{ "billPerService"?, "billExtraPiece"?, "billDispatchFee"?, "billCodFee"?, "billSpecialServices"? }`
- `PATCH /api/v1/contracts/{publicId}/dispatch-fee` `{ "amount": 3 }`
- `PATCH /api/v1/contracts/{publicId}/cod-fee` `{ "type": "PERCENT", "value": 2.5 }`

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| `amount` negative | ≥ 0 | `The dispatch fee cannot be negative.` | 400 |
| `type` empty | FIXED or PERCENT | `Specify the COD fee type: FIXED (fixed amount per shipment) or PERCENT (percentage of the COD amount).` | 400 |
| `type` unknown | — | `Unknown COD fee type: 'X'. Use FIXED or PERCENT.` | 400 |
| `value` missing | — | `Enter the COD fee value.` | 400 |
| FIXED negative | ≥ 0 | `The fixed COD fee cannot be negative.` | 400 |
| PERCENT outside 0..100 | — | `The COD fee percentage must be between 0 and 100.` | 400 |

### 4.6 Service levels (SLA)

What it does: one level per service type (maximum transit hours, pickup window in minutes, on-time target,
penalty). `PUT` **replaces the whole list**: absent types are deactivated.

Who can: `contracts.update`. How it is used: `PUT /api/v1/contracts/{publicId}/service-levels`
`[ { "serviceType": "STANDARD", "maxTransitHours": 24, "pickupWindowMin"?: 30, "onTimeTargetPct"?: 95, "penaltyAmount"?: 10 } ]`

| Field / case | Rule | Exact message (field `serviceLevels[i].*`) | HTTP |
|---|---|---|---|
| `serviceType` empty | required | `The service type of the level is required.` | 400 |
| Repeated type (case-insensitive) | one per type | `The service type 'STANDARD' is repeated: there can only be one service level per type.` | 400 |
| `maxTransitHours` ≤ 0 | — | `Maximum transit hours must be greater than zero.` | 400 |
| `pickupWindowMin` < 0 | — | `The pickup window (minutes) cannot be negative.` | 400 |
| `onTimeTargetPct` outside 0..100 | — | `The on-time target must be between 0 and 100.` | 400 |
| `penaltyAmount` < 0 | — | `The penalty cannot be negative.` | 400 |
| Nonexistent service type | `ServiceType` list | `Unknown service type: 'X'.` | 400 |
| Last-moment duplicate | unique index | `There can only be one active service level per service type.` | 409 |

### 4.7 Contract status and its effects

Statuses (`ContractStatus`): **DRAFT** (initial) → **ACTIVE**; **EXPIRED** and **CANCELLED** are terminal (reached from
any stage; there is no leaving them). How it is used: `POST /api/v1/contracts/{publicId}/status` `{ "toCode": "ACTIVE", "comment"? }` (`contracts.update`).

Effect when moving to **ACTIVE** (`ContractStatusEffect`): it checks that the client is not SUSPENDED and does not already have
**another ACTIVE contract** (a single active contract per client at a time; to replace it the previous one must be cancelled or
expired). The end date plays no part: a contract with a past `endDate` can be activated and remains current. On reaching
EXPIRED/CANCELLED nothing else happens: rates and special services remain as history.

Capability **EDIT_CONTRACT**: by default it is not allowed in EXPIRED or CANCELLED, so every write (general
data, billing model, dispatch, COD, SLA, rates, tiers, special services) responds 422 in those statuses;
the record anticipates it with `canEdit: false`. The administrator can change the rule in `PUT /api/v1/status/capabilities/CONTRACT`.

| Case | Exact message | HTTP |
|---|---|---|
| `toCode` empty | `Specify the target status.` | 400 |
| Activating with the client suspended | `The contract cannot be activated: the client is suspended. Reactivate the client first.` | 422 |
| Activating when there is already another ACTIVE one | `The client already has a current contract. Cancel or expire the previous contract before activating this one.` | 422 |
| Going back to DRAFT, leaving a terminal, etc. | status engine messages (chapter 01, section 5), e.g. `'CANCELLED' is terminal: it allows no further transitions.` | 422 |
| Writing in EXPIRED/CANCELLED | `The current status does not allow the action 'EDIT_CONTRACT'.` | 422 |

---

## 5. Contract rates

### 5.1 Model and history

- **Per service**: a fixed amount per shipment for each pair (service type, package type).
- **Extra piece**: for each pair, **tiers** by piece number (2–5 at $1.00, 6+ at $0.75) with **marginal** calculation: piece 1
  is never extra; each piece 2..n pays the rate of the tier it falls in (8 pieces with those tiers = 4 × 1.00 + 3 × 0.75 = 6.25).
- **Effective-dated history**: each row has `effectiveFrom` (inclusive) and `effectiveTo` (exclusive; empty = open).
  **Editing** = closing the current row on the new date and opening another one (an amount is never overwritten); **removing** = closing. Two
  edits on the same day leave a zero-length row that is not in force but remains as history. `isCurrent` indicates whether
  the row is in force on the queried date.
- **Service and package are immutable**: to change them the row is closed and another one is created.
- **A single row in force per pair and date**: there cannot be two rows of the same service+package in force on the same day (neither
  open nor closed with a future date); the tiers of the same component cannot overlap.
- If the corresponding component is **off** in the billing model, rows cannot be created or edited (409),
  but existing ones are kept and still shown.

Who can: `contracts.read` (view), `contracts.update` (write; also requires EDIT_CONTRACT in the current status).

### 5.2 Query

`GET /api/v1/contracts/{publicId}/rate-components?asOf=2026-06-01&includeHistory=false` → `perService[]` (id, service,
package, rate, validity, `isCurrent`) and `extraPiece[]` (component with its `tiers[]`). Without `asOf` = today; `includeHistory`
also returns the closed ones.

### 5.3 Rate per service: create, new version, close

- `POST /api/v1/contracts/{publicId}/rate-components` `{ "kind": "PER_SERVICE", "serviceType": "STANDARD", "packageType": "BOX", "rate": 6.5, "effectiveFrom"?: "2026-01-01" }`
- `PATCH /api/v1/contracts/{publicId}/rate-components/{id}` `{ "rate": 7, "effectiveFrom"?: "hoy" }` → returns the **new row** (different id)
- `POST /api/v1/contracts/{publicId}/rate-components/{id}/close` `{ "effectiveTo"?: "hoy" }`

As many PER_SERVICE rows can be created as distinct service+package pairs the contract has (STANDARD/BOX 6.50,
STANDARD/ENVELOPE 5.00, EXPRESS/BOX 8.50…); the 409 "a rate in force already exists" applies only to the **same pair**.

| Case | Exact message | HTTP |
|---|---|---|
| `kind` other than PER_SERVICE/EXTRA_PIECE | `Specify the component type: PER_SERVICE (rate per service) or EXTRA_PIECE (extra piece).` | 400 |
| `serviceType` / `packageType` empty | `The service type is required.` / `The package type is required.` | 400 |
| Unknown list code | `Unknown code 'X' in the ServiceType list.` (or `PackageType`) | 400 |
| `rate` missing or negative (PER_SERVICE) | `The rate per service is required.` / `The rate cannot be negative.` | 400 |
| "Per service" component off | `The 'Per service' component is off in the contract's billing model; turn it on before working on its rates.` | 409 |
| There is already a row of the same pair in force on that date | `A rate in force already exists on that date for that service and package type in the contract; edit it or close it before creating another one.` | 409 |
| Editing an extra piece component through this route | `Only rates per service are edited here; extra piece tiers are edited in /tiers.` | 400 |
| Editing an already closed row | `The rate is already closed; create a new one instead of editing it.` | 409 |
| New version with a date earlier than the row's start | `The new effective date (yyyy-MM-dd) cannot be earlier than the start of the current row (yyyy-MM-dd).` | 400 |
| Closing with a date earlier than the start | `The date (yyyy-MM-dd) cannot be earlier than the row's effective start (yyyy-MM-dd).` | 400 |
| Closing an already closed row | `The component is already closed.` | 409 |
| Foreign or nonexistent component | `Rate component '<id>' not found.` | 404 |
| Contract from another company | `Contract not found.` | 404 |

**Date rule (principle #8, history that is not rewritten).** A new version and a closure are only dated today or later.
If `effectiveFrom`/`effectiveTo` is earlier than today, the API responds `400` with the message
`The date cannot be earlier than today: the rate history is not rewritten.` The same rule applies to extra piece
tiers (5.4) and to special service rates (6). The creation date of a new rate can be in the past (loading
history when setting up the contract).

### 5.4 Extra piece: component and tiers

- `POST /api/v1/contracts/{publicId}/rate-components` `{ "kind": "EXTRA_PIECE", "serviceType": "STANDARD", "packageType": "BOX" }` (no amount)
- `POST /api/v1/contracts/{publicId}/rate-components/{id}/tiers` `{ "fromUnit": 2, "toUnit": 5, "rate": 1.0, "effectiveFrom"? }` (`toUnit` empty = open: "6+")
- `PATCH /api/v1/contracts/{publicId}/rate-components/{id}/tiers/{tierId}` `{ "fromUnit"?, "toUnit"?, "clearToUnit"?, "rate"?, "effectiveFrom"? }` → new tier
- `POST /api/v1/contracts/{publicId}/rate-components/{id}/tiers/{tierId}/close` `{ "effectiveTo"? }`
- `POST /api/v1/contracts/{publicId}/rate-components/{id}/close` closes the component **and its open tiers** with the same date
  (a tier born after that date is left with zero length).

| Case | Exact message | HTTP |
|---|---|---|
| "Extra piece" component off | `The 'Extra piece' component is off in the contract's billing model; turn it on before working on its tiers.` | 409 |
| `fromUnit` < 2 or `toUnit` < `fromUnit` (create or edit) | `Invalid range: 'from' must be at least 2 (piece 1 goes in the per-service rate) and 'to' must be greater than or equal to 'from' or left empty (open).` | 400 |
| `rate` negative | `The rate per piece cannot be negative.` | 400 |
| Overlap with a tier in force (create or edit of From/To) | `Tier 4–7 overlaps with the tier in force 2–5.` (field `fromUnit`) | 400 |
| Tiers in a component that is not extra piece | `Tiers only apply to extra piece components.` | 400 |
| Closed component | `The extra piece component is closed; create a new one to add tiers.` | 409 |
| Editing a closed tier | `The tier is already closed; create a new one instead of editing it.` | 409 |
| Closing a closed tier | `The tier is already closed.` | 409 |
| Foreign or nonexistent tier | `Tier '<id>' not found.` | 404 |

### 5.5 Quote

What it does: quotes a shipment against the client's **current contract** on `asOf` (today by default): base rate per
service+package, marginal extra pieces, dispatch fee (once per order) and COD fee, with the company's **generic rates**
as a fallback when the component is off or there is no row (`baseSource`/`extraSource`: CONTRACT | GENERIC |
NONE). Dispatch and COD are added to the **first line**. Without a current contract everything comes from the generic ones and dispatch/COD are 0.
Amounts to 4 decimals. Example from the log: base 7.00 + 8 pieces (2–5 at 1.00, 6+ at 0.75 = 6.25) + dispatch 3 + COD 2.5 % of 200 (5.00) = **21.25**;
if the shipment also brings an envelope (STANDARD/ENVELOPE with a contract rate of 5.00), a second line comes out with its own base
(`baseSource: CONTRACT`) and the total is **26.25** (dispatch and COD stay only on the first line).

Who can: `contracts.read`. How it is used: `POST /api/v1/billing/contract-rate-quote`
`{ "clientPublicId", "lines": [ { "serviceType": "STANDARD", "packageType": "BOX", "pieces": 8 } ], "codAmount": 200, "asOf"?: "2026-06-01" }`
→ `{ contractPublicId, contractStatus, asOf, lines[ { baseRate, baseSource, extraPieces, extraSource, lineTotal } ], dispatchFee, codFee, total, rateComponentIds }`.

| Case | Exact message | HTTP |
|---|---|---|
| No lines | `Enter at least one line (service, package type, pieces).` | 400 |
| `pieces` < 1 | `The number of pieces must be at least 1.` (field `lines[i].pieces`) | 400 |
| Two lines with the same service and package type | `The service/package type pair is repeated; send a single line per (service, package type) with the total pieces.` (field `lines[i]`) | 400 |
| `codAmount` negative | `The COD amount cannot be negative.` | 400 |
| Deactivated client | `The client has been deactivated; only its history can be viewed.` | 409 |
| Nonexistent client | `Client not found.` | 404 |

A line without a base rate (neither contract nor generic) **is not an error**: it comes out with `baseSource: NONE` and `lineTotal` 0; in the web app the Quoter warns about it with “There is no rate in force for X / Y: that line is quoted at 0.”. The server does not hide these prices behind `orders.view_price`: they are **contract** prices, not a shipment's, and they are visible with `contracts.read`. Screens: [chapter on the Quoter and portal users](frontend/fa3-portal-cotizador.md).
| Service/package empty or unknown | `The service type is required.` / `Unknown code 'X' in the PackageType list.` | 400 |

---

## 6. Special services

What it does: component 5 of the billing model. Each client has an effective-dated rate per **special service
type**; the **types belong to the company** (shared by all its clients): when one is created from a client it becomes
available to the others instantly, and names are compared without case, accents or double spaces ("Vagón del
muelle" and "VAGON DEL MUELLE" are the same type). The type of a row is immutable. Rows are shown even if the component
is off (`componentEnabled: false`); only writes are blocked. A type created by mistake is **deactivated** (never
deleted): it leaves the selector of all clients and keeps the history; it is reactivated when needed. It can only be
deactivated when no client has an open rate of that type.

Who can: `contracts.read` (view), `contracts.update` (write; requires a current contract with "Special" on and EDIT_CONTRACT).

How it is used:
- `GET /api/v1/clients/{publicId}/special-services?asOf=&includeHistory=false` · `GET /api/v1/special-service-types?includeInactive=false` (with `clientsUsing`)
- `POST /api/v1/clients/{publicId}/special-services` `{ "typeId": 5 }` **or** `{ "newTypeName": "Vagón del muelle" }`, plus `"rate": 150, "effectiveFrom"?`
- `PATCH /api/v1/clients/{publicId}/special-services/{id}` `{ "rate": 160, "effectiveFrom"? }` → new row
- `POST /api/v1/clients/{publicId}/special-services/{id}/close` `{ "effectiveTo"? }`
- `POST /api/v1/special-service-types/{id}/deactivate` · `POST /api/v1/special-service-types/{id}/reactivate` (`contracts.update`) → the type with `isActive`

| Case | Exact message | HTTP |
|---|---|---|
| No current contract | `The client has no current contract; create or activate a contract before setting up special services.` | 409 |
| "Special" component off | `The 'Special services' component is off in the current contract; turn it on in the billing model to add special services.` | 409 |
| `typeId` and `newTypeName` both or neither | `Specify the special service type: an existing type (typeId) or the name of a new one (newTypeName), not both.` | 400 |
| `newTypeName` empty / > 120 | `The special service type name is required.` / `The special service type name cannot exceed 120 characters.` | 400 |
| `rate` negative | `The special service rate cannot be negative.` | 400 |
| There is already a row of that type in force on that date | `The client already has a rate in force on that date for the type 'X'; edit that rate or close it before adding another one.` | 409 |
| Nonexistent `typeId` | `Special service type '<id>' not found.` | 404 |
| Inactive `typeId` | `The special service type is inactive; reactivate it or choose another one.` | 400 |
| Last-moment duplicate name | `A special service type with the name 'X' already exists.` | 409 |
| Editing or closing a closed row | `The rate is already closed; add a new special service if you need to charge it again.` | 409 |
| New effective date earlier than the start | `The new effective date cannot be earlier than the start of the current rate (yyyy-MM-dd).` | 400 |
| Closure earlier than the start | `The closing date cannot be earlier than the start of the rate (yyyy-MM-dd).` | 400 |
| Foreign or nonexistent row | `Special service '<id>' not found.` | 404 |
| Deactivating a type with open rates | `The type has rates in force for N client(s); close them before deactivating it.` | 409 |
| Deactivating an already inactive type / reactivating an active one | `The type is already inactive.` / `The type is already active.` | 409 |
| Nonexistent type or from another company (deactivation/reactivation) | `Special service type '<id>' not found.` | 404 |

---

## 6b. Invoice check (Batch 126)

In the contract tab, section **Invoice check**: “Charge for checking the client's invoice” (off by default) and “Amount per checked invoice”. When the client's invoice is processed against its delivered order (sheet uploaded or number typed in Process deliveries), the billing run adds an “Invoice check” line with that amount, once per order. Permission `contracts.update`. See [chapter 21, §7](21-cod.md).

## 7. Client portal users

What it does: from the client file, the client's people who will use the portal are **invited** by email. Nobody
types another person's password: the account is born **without a password** and the invitee sets it when accepting the link (a single-use
token that expires after `Portal:InviteHours` hours, 48 by default, floor 1; **resending** issues a new one and the previous one stops
being valid). The invitation response announces `expiresAtUtc` with that same window; the actual expiration is applied by the Identity
token provider and is not checked in the smoke test (only that the announced window matches the configured one).
An email cannot be both an internal user and a portal user, **in both directions**: inviting an internal email to the portal
responds 409, and creating a portal email as an internal user (`POST /api/v1/users`) does too (see 3.3 of chapter 1). Every
email clash when inviting responds with the **same** neutral 409, whether it belongs to the tenant itself, to another one or to an internal user, so as not to
reveal where it is registered. Portal users do not appear in the company's user list (`/api/v1/users`) and
do not sign in through the internal application (the portal login comes in its own module). It requires the **CLIENT_PORTAL** module to be on.
A deactivated person (DISABLED) can be **invited again with the same email** from the same client: they are reborn as INVITED
keeping their history.

> **Updated by Batch 3 (multi-client portal)**: the same account (email) can belong to **several clients**
> of the company, each with its own row (independent role and status). Inviting the same email from another client
> of the same tenant no longer responds 409: it adds a new row to that account (active immediately if it already had a password, or
> `INVITED` with a new link if not). The neutral 409 still applies to an internal user's email or one from another tenant, or
> one already invited/active/suspended **in that same client**. See [chapter 03, section 6](03-ordenes-de-transporte.md#6-multi-client-portal-changes-chapter-02-section-7).

Who can: `portalusers.manage` (different from `admin.users`). The platform administrator can too, and their
actions are attributed to them in the tenant's log.

Statuses (`PortalUserStatus`): **INVITED** (initial) → **ACTIVE** (on acceptance); **SUSPENDED** (lateral, reversible);
**DISABLED** (terminal = final deactivation; never deleted). Each row is per client. Effects (`PortalUserStatusEffect`):
on reaching SUSPENDED or DISABLED, the **account** (password, sessions and refresh tokens, `SecurityEvent` TOKEN_REVOKED) is
deactivated only when **it has no other ACTIVE row left** in the company; when going back to ACTIVE from SUSPENDED, the
account is reactivated keeping the password.

How it is used:
- `GET /api/v1/clients/{publicId}/portal-users`
- `POST /api/v1/clients/{publicId}/portal-users/invite` `{ "email", "fullName"?, "role": "CLIENT_ADMIN" | "CLIENT_OPERATOR" | "CLIENT_READONLY" }` → user, `expiresAtUtc` and `inviteToken` **only** in Development (`Portal:ReturnInviteTokenInResponse`); in production the link is sent through `IInvitationSender` (today, a log entry without the token).
- `POST /api/v1/clients/{publicId}/portal-users/{id}/resend-invite` (only in INVITED; new token, the previous link stops being valid)
- `POST /api/v1/clients/{publicId}/portal-users/{id}/suspend` (only from ACTIVE) · `.../reactivate` (only from SUSPENDED) · `.../remove` (from any non-terminal status) → 204
- `POST /api/v1/portal-users/accept-invite` `{ "email", "token", "password" }` **without Authorization** → 204. Password: minimum 12 characters, no composition rules; the check against known breaches (HIBP) is wired in the code but is a no-op today: it will be turned on when the environment has Internet access (see 1.6).

| Case | Exact message | HTTP |
|---|---|---|
| `email` empty / invalid | `The email is required.` / `Invalid email.` | 400 |
| `role` empty / unknown | `The portal role is required.` / `List PortalRole 'X' not found.` | 400 / 404 |
| The email is not available (already invited, active or suspended **in this same client**; portal account of another company; or internal user) | `That email is not available for this company's portal.` (same message in every case, on purpose; a `SecurityEvent` ROLE_CHANGE/FAILURE `portal_invite_conflict` is recorded) | 409 |
| Inviting from **another** client of the company an email that is already a portal account (even if it is deactivated in the first client) | Does **not** respond 409 (corrected 2026-10-12: it used to say the opposite): it adds a new row to that account, active immediately if the account already has a password (without `expiresAtUtc`) or `INVITED` with a link if not (see the Batch 3 box and chapter 03, section 6). The neutral 409 remains for the same client, an internal user and an account of another company | 200 |
| Deactivated client: inviting or resending | `The client has been deactivated; only its history can be viewed.` (the list, suspending, reactivating and deactivating are allowed) | 409 |
| Resending when it is no longer INVITED | `The invitation can only be resent to a user who has not yet accepted it (status INVITED).` | 409 |
| Suspending without being ACTIVE | `Only a portal user in status ACTIVE can be suspended.` | 409 |
| Reactivating without being SUSPENDED | `Only a portal user in status SUSPENDED can be reactivated.` | 409 |
| Deactivating one already DISABLED | `The portal user has already been deactivated.` | 409 |
| User of another client or nonexistent | `Portal user '<id>' not found.` | 404 |
| Accepting: any failure (unknown email, not a portal one, not INVITED, invalid token, expired, already used or earlier than a resend, short password (or, when the check is turned on, breached), module off, inactive account) | `Invalid or expired invitation.` (same response in every case, on purpose) | 400 |
| CLIENT_PORTAL module off (authenticated routes) | `The module 'CLIENT_PORTAL' is not enabled for this company.` (`code: module_disabled`) | 403 |
| Portal login through the internal app | `Portal users sign in at the client portal.` | 401 |

Frequent cases: the invitee says the link "does not work" → resend the invitation (only if still INVITED; the old
link stops being valid, they should use the new one); if they already accepted, it is not resent. A suspended user keeps their password and signs
in again when reactivated. A deactivated one (DISABLED) is not "reactivated": they are **invited again** with the same email from
`.../invite` (same record, status INVITED, full history) and set a new password when accepting.

---

**Deactivated company.** If the platform administrator deactivates the company, the invitations issued before can no longer
be accepted: `accept-invite` responds with the same neutral `400` (`Invalid or expired invitation.`) and records the attempt in the
security log without identifying the user.

## 8. Permissions, modules and roles

| Permission | What it allows |
|---|---|
| `clients.read` / `clients.create` / `clients.update` | view / create / edit clients (profile, numbering, contacts, status, deactivation) |
| `locations.read` / `locations.create` / `locations.update` | consignees and locations |
| `contracts.read` | contracts, rates, quote, special services and types (read) |
| `contracts.create` / `contracts.update` | create contracts / edit contracts, rates, tiers and special services |
| `portalusers.manage` | the client's portal users (CLIENT_PORTAL module) |

The cross-cutting routes of chapter 1 apply the permission of the owning entity: `GET /api/v1/contacts/CLIENT|CLIENT_CONTACT/{id}`,
`GET /api/v1/status/history/CLIENT/{id}` and `GET /api/v1/custom-fields/values/CLIENT/{id}` require `clients.read`
(CONTRACT → `contracts.read`, LOCATION → `locations.read`, PORTAL_USER → `portalusers.manage`); `PUT /api/v1/custom-fields/values/CLIENT/{id}`
requires `clients.update` (CONTRACT → `contracts.update`, LOCATION → `locations.update`). Without the permission: `Missing permission '<code>'.` (403).

Role templates: **TenantAdmin** everything; **Dispatcher** `clients.read`, `locations.read`, `locations.create`; **Billing**
`clients.read`, `contracts.read`; **ReadOnly** `clients.read`, `locations.read`, `contracts.read`. When the platform is updated,
a template's new permissions are also added to the roles already cloned by each company (only added, never removed).

Modules: the whole chapter lives under **CATALOG**; portal users under **CLIENT_PORTAL**. With the module off the
routes respond `403` with `code: module_disabled`, and `accept-invite` responds with the generic 400.

---

## 9. Analytics: data sources and system content

New sources for views, indicators and charts (chapter 01, section 8): **CLIENT** (code, name, status, payment
term, currency, credit limit, active, numbering, `BillingSummary` of the current contract, `ContractsCount`,
`PickupLocationName`; supports CLIENT custom fields), **CONTRACT** (number, title, client, dates, status,
trigger, 5 flags, dispatch, COD; relation `Client`; date range by `StartDate`) and **LOCATION** (code, name,
owner, shared, type, city, country, minutes, `AllowDupInvoice`; relation `Client`). System content seeded for each
company: view **"Clients"**, indicator **"Active clients"** (in the Pulse) and chart **"Contracts by status"**.

---

## 10. Audit

All the entities of the chapter generate a change log (`GET /api/v1/audit/changes?entityType=CLIENT|CLIENT_CONTACT|
LOCATION|CONTRACT|RATE_COMPONENT|SPECIAL_SERVICE|PORTAL_USER`). Status changes are kept in the status history
(`/api/v1/status/history/{CLIENT|CONTRACT|PORTAL_USER}/{id}`). Actions on portal users also generate
`SecurityEvent` ROLE_CHANGE (`portal_invite`, `portal_invite_resent`, `portal_user_suspended`, `portal_user_reactivated`,
`portal_user_disabled`), PASSWORD_CHANGE (`portal_invite_accepted` / `portal_invite_accept_failed`) and TOKEN_REVOKED. The invitation
token never appears in the log or in the server records.
