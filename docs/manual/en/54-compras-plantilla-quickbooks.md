# 54 — Purchases: import with the user's template and pass purchase orders to QuickBooks (IIF file)

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter [`../54-compras-plantilla-quickbooks.md`](../54-compras-plantilla-quickbooks.md) (Batch 246, 2026-11-25). The Spanish manual is the reference. **The API returns its error messages in Spanish only**, so the messages below are quoted in Spanish (exactly as the software shows them) with their meaning in English. Interface labels use the English text of the screens.

Batch 246 (2026-11-25). Two new things in **Purchases**:

1. **Import purchase orders with a template that you define** (which column of your file is the supplier, the document number, the SKU, the quantity and the cost), besides the two sources that already existed (QuickBooks and generic file, Batch 239).
2. **Pass purchase orders to QuickBooks Desktop** with an IIF file: one, several or all that are missing; afterwards you answer whether QuickBooks received them.

> **Important warning: the purchase-order IIF file has NOT yet been tested against a real QuickBooks Desktop.** It was checked that the file is consistent (each order adds up to 0.00 inside), not that QuickBooks accepts it as is. It still has to be tried on a copy of the Solutions company. Until then, see section 10: mark the history with «Mark all» and export without new suppliers or products until the names are confirmed.

> **Exporting marks nothing.** The file comes out, the order stays **«Exported, to confirm»** and you say afterwards whether QuickBooks received it. No QuickBooks action changes the purchase order's status.

> **An order that came from QuickBooks is never exported or marked.** It is already there.

Screens: [L246](../frontend/l246-compras.md) (Spanish edition). Every error message is explained in the [FAQ](faq.md) (section «Batch 246»). This chapter continues [06 — Inventory and warehouse, §8a (Import purchase orders)](../06-inventario-y-almacen.md#8a-importar-órdenes-de-compra-lote-239) (Spanish edition) and is similar to [49 — Invoices to QuickBooks](49-quickbooks.md), which explains the same flow for sales invoices.

## 1. Where it is and who can

- **Where:**
  - **Accounting → Purchases** (`/warehouse/purchase-orders`): the list (with the **«Origin»** and **«QuickBooks»** columns and filters), the **«Import»** button (now with the source **«Template (user-defined)»**) and the **«QuickBooks»** button.
  - The purchase order **record**: the strip with the origin and the QuickBooks state, with who and when, and its actions.
  - **System → Templates**, **«Purchase orders»** tab.
  - **Settings → Operation → QuickBooks:** the **«Purchase orders account»** field.
- **Modules:**
  - **Purchases (`PURCHASING`)** for everything in this chapter. With the module off, the routes answer 403 with `El módulo 'PURCHASING' no está habilitado para esta compañía.` (the module is not enabled for this company).
  - **Integrations (`INTEGRATIONS`)** to open **System → Templates**. On a new database only the platform administrator turns it on. It is **not needed** to create or edit the template from the Purchases importer.
  - **Sales orders (`SALES_ORDERS`)** for the «Purchase orders account» field (it lives in the sales QuickBooks settings).
- **Permissions:**

| What | Permission | Module |
|---|---|---|
| See the list, the record, the origin and the state against QuickBooks; see the purchase-order templates and their fields | `purchasing.view` | `PURCHASING` |
| Import with a template (or with the other sources); create, edit, deactivate and reactivate templates | `purchasing.manage` | `PURCHASING` |
| See what is left to pass, export, «Yes, they are in», «It did not go through», mark and remove the mark | `purchasing.quickbooks` | `PURCHASING` |
| See the purchase orders account in Settings | `sales.quickbooks.view` or `admin.tenant` | `SALES_ORDERS` |
| Change the purchase orders account | `admin.tenant` | `SALES_ORDERS` |

- `purchasing.quickbooks` is in the Billing permission category (`BILLING`) and brings `purchasing.view` with it. The **company administrator** and **Billing** (`Billing`) receive it; it was also added to those two roles in companies that already existed. Other roles do not have it: the administrator can give it in Roles.
- Without permission, the API answers 403 with `Falta el permiso 'purchasing.quickbooks'.` (or the corresponding one) (missing permission) and a `PERMISSION_DENIED` event is left in the security events. On screen, the «QuickBooks» button and the record's QuickBooks actions do not show.
- `TenantId` comes from the session: each company sees only its own data. A template of another company answers 404 and an order of another company answers 400 «Alguna de las órdenes de compra indicadas no existe.» (one of the indicated purchase orders does not exist), without saying which of the two happened.
- **Neither the portal nor the client app show any of this.**

## 2. The purchase-order import template

**What it does.** It says how to read a purchases file that has neither the QuickBooks format nor the generic one: which column is each piece of data. One template is created per supplier or per file format.

**Who can.** See: `purchasing.view`. Create, edit, deactivate and reactivate: `purchasing.manage`. Module `PURCHASING`.

**How to use it (screen).**
- Purchases → **Import** → source **«Template (user-defined)»** → **«New template»** (or **«Edit»** on the chosen one). The window opens right there and, when saved, the template is chosen.
- Or **System → Templates → «Purchase orders» → «New template»**. The tab has no «Client» column: purchasing templates carry no client.
- The window asks for **Name**, **Delimiter** (comma, semicolon, tab…), whether **the first row is the header**, and a table «Column N → Target field». The **default values** are used when the cell comes empty or the field has no column.
- **Window texts (fixed after Batch 246 closed).** The title and the help texts say what the template imports: «New purchase orders import template» / «Edit purchase orders import template», and the «required fields» and «default values» help talk about the order number, the SKU, the quantity and the supplier. Before, they said «of shipments» and named the consignee and the package. The same goes for the **products, suppliers, warehouses and bins** templates (each with its own title and help texts); the **shipments** and **consignees** ones did not change.

**Template fields.**

| Field | What it is | Required? | Fixed value (default)? |
|---|---|---|---|
| `supplier` | Supplier (name as it is in Teikem) | Yes: in a column **or** as a fixed value | **Yes** (the template's fixed supplier) |
| `documentNumber` | Document number: rows with the same number form one order | **Yes, in a column** | No |
| `orderDate` | Order date (`YYYY-MM-DD` or `MM/DD/YYYY`) | No (no date = today) | No |
| `expectedDate` | Expected delivery date | No | No |
| `sku` | Product SKU | **Yes, in a column** | No |
| `description` | Description (only useful for «Download what is missing»; it is not stored on the order) | No | No |
| `quantity` | Quantity | **Yes, in a column** | No |
| `unitCost` | Unit cost (up to 4 decimals) | No (no cost = the product's) | No |

- The **warehouse** is not a template field: it is chosen in the import window and applies to the whole file.
- A purchasing template **does not accept a client** or a default client, nor «several lines» or variants.
- Template names are not repeated (ignoring case) **among those of the same type**: there can be one for purchases and one for shipments with the same name.
- An **inactive** template cannot be used to import; reactivate it or choose another.

**API.**

| What | Call | Permission |
|---|---|---|
| Available fields | `GET /api/v1/purchase-orders/import-templates/fields` | `purchasing.view` |
| List | `GET /api/v1/purchase-orders/import-templates?includeInactive=false` | `purchasing.view` |
| One template | `GET /api/v1/purchase-orders/import-templates/{publicId}` | `purchasing.view` |
| Create | `POST /api/v1/purchase-orders/import-templates` | `purchasing.manage` |
| Edit | `PATCH /api/v1/purchase-orders/import-templates/{publicId}` | `purchasing.manage` |
| Deactivate / reactivate | `POST .../{publicId}/deactivate` and `POST .../{publicId}/reactivate` | `purchasing.manage` |

Example body to create a template with semicolon, no header and a fixed supplier:

```json
{
  "name": "Compras semanales",
  "delimiter": ";",
  "hasHeader": false,
  "columns": [
    { "position": 1, "field": "documentNumber" },
    { "position": 2, "field": "sku" },
    { "position": 3, "field": "quantity" },
    { "position": 4, "field": "unitCost" },
    { "position": 5, "field": "orderDate" }
  ],
  "defaults": { "supplier": "Proveedor Ejemplo" }
}
```

- The shipment-template route (`/api/v1/import-templates`) does **not** accept this type: asking for `kind=PURCHASE_ORDER` there answers 400 «Tipo de plantilla desconocido: PURCHASE_ORDER.» (unknown template type).
- Every creation, change or deactivation of a template is logged like the other templates.

**Validations (field → rule → message → HTTP).** Definition errors come per field in `errors` of the error body. **Messages are in Spanish; the English meaning is in the last column.**

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `columns` | There must be at least one column | `Indique al menos una columna.` | 400 | Give at least one column |
| `columns` | At most 100 columns | `Máximo 100 columnas.` | 400 | At most 100 columns |
| `columns` | The `documentNumber` column is missing | `La plantilla debe incluir la columna documentNumber.` | 400 | The template must include the documentNumber column |
| `columns` | The `sku` column is missing | `La plantilla debe incluir la columna sku.` | 400 | The template must include the sku column |
| `columns` | The `quantity` column is missing | `La plantilla debe incluir la columna quantity.` | 400 | The template must include the quantity column |
| `columns` | No `supplier` column and no fixed supplier | `La plantilla debe indicar el proveedor: en la columna supplier o como valor fijo.` | 400 | The template must say the supplier: in the supplier column or as a fixed value |
| `columns[i].position` | 1 or more | `La posición debe ser 1 o mayor.` | 400 | The position must be 1 or more |
| `columns[i].position` | No more than 500 | `La posición no puede exceder 500.` | 400 | The position cannot exceed 500 |
| `columns[i].position` | Not repeated | `La posición {n} está repetida.` | 400 | Position {n} is repeated |
| `columns[i].field` | One of the 8 fields | `Campo desconocido: {campo}.` | 400 | Unknown field |
| `columns[i].field` | Not repeated | `El campo {campo} está repetido.` | 400 | The field is repeated |
| `defaults.{field}` | Only `supplier` accepts a fixed value | `El campo {campo} no admite valor por defecto.` | 400 | The field does not accept a default value |
| `clientPublicId`, `defaultClientPublicId` | Purchases carries no client | `Este tipo de plantilla no lleva cliente.` | 400 | This template type carries no client |
| `name` | Required | `El nombre de la plantilla es obligatorio.` | 400 | The template name is required |
| `name` | Up to 120 characters | `Máximo 120 caracteres.` | 400 | At most 120 characters |
| `name` | Not repeated among the templates of the type | `Ya existe una plantilla de importación con ese nombre.` | 409 | An import template with that name already exists |
| `delimiter` | A single character, other than quote and line break (`tab` = tab) | `El delimitador debe ser un solo carácter (p. ej. ',' o ';'), distinto de comilla y salto de línea.` | 400 | The delimiter must be a single character (e.g. ',' or ';'), other than a quote and a line break |
| `publicId` | The template exists, is a purchasing one and belongs to your company | `Plantilla de importación no encontrado.` | 404 | Import template not found |
| deactivate | It was already inactive | `La plantilla ya está inactiva.` | 409 | The template is already inactive |
| reactivate | It was already active | `La plantilla ya está activa.` | 409 | The template is already active |

- If several required columns are missing, the `columns` message shows **one at a time**, in this order: `documentNumber`, `sku`, `quantity`, supplier. Fix it and save again.
- «Plantilla de importación no encontrado.» is the system's exact text.

## 3. Import purchase orders with the template

**What it does.** It reads the file with the template, groups the rows that have the same **supplier and document number** into one purchase order and creates them. A purchase order says that something was bought: it **does not move inventory** (the receipt does that).

**Who can.** `purchasing.manage`, module `PURCHASING`.

**How to use it (screen).** Purchases → **Import** → **File source: «Template (user-defined)»** → choose the **Template** → **Status of the imported orders** (Draft or Completed = sent) → **Warehouse** → **File** (CSV or `.xlsx`) → **«Review»** (saves nothing) → **«Import N»**. Blocked ones are not imported; the detail says why. Fix the file and import **the same one** again: what was already imported is not repeated.

**API.** `POST /api/v1/purchase-orders/import`, `multipart/form-data` (up to 4 MB, 5,000 rows or 2 MB of text):

| Field | What it is |
|---|---|
| `file` | The file (required) |
| `source` | `GENERIC` (default), `QUICKBOOKS` or `TEMPLATE`. With `templatePublicId` and no `source`, it is `TEMPLATE` |
| `templatePublicId` | The purchasing template; required with `TEMPLATE` and only with `TEMPLATE` |
| `status` | `DRAFT` (default) or `SENT` |
| `warehousePublicId` | The warehouse (optional if the company has a single active one) |
| `commit` | `false` = preview without saving; `true` = creates the ready orders |

The response carries the summary (`totalDocuments`, `ready`, `blocked`, `duplicates`, `skipped`, `created`, `failed`), each document with its state (`READY`, `CREATED`, `BLOCKED`, `ERROR`, `DUPLICATE`, `SKIPPED`) and its warnings, the missing products and suppliers and, with a template, `templatePublicId` and `templateName`.

**How the template data is read.**
- **Dates** are `YYYY-MM-DD` or `MM/DD/YYYY`. An order without a date takes today.
- **Numbers** use a decimal point; the **comma is a thousands separator** («1,250.50»). `$` and spaces are removed.
- The **SKU** is taken as is («SKU (Description)» is not split); a SKU that differs only in hyphens, spaces or signs is taken as Teikem's and warns with `SKU_ALIAS`.
- An empty cell takes the template's fixed value, if there is one.
- A cost with more than 4 decimals is rounded and warns (`COST_ROUNDED`). A line with quantity 0 is skipped and warns (`ZERO_QTY`). If two lines of the same order carry the **same product at the same cost**, they are added together and it warns (`MERGED_LINES`).

**Result warnings.** Those that **block** stop the order from being created; the others only inform.

| Code | Blocks? | When it appears |
|---|---|---|
| `BAD_VALUE` | **Yes** (only with a template) | An unreadable date or number, or a row without a document number |
| `COST_CONFLICT` | **Yes** (all sources) | The same product on several lines of the order with different costs |
| `MISSING_PRODUCT`, `AMBIGUOUS_PRODUCT` | Yes | The SKU does not exist, or matches more than one product |
| `MISSING_SUPPLIER`, `INACTIVE_SUPPLIER` | Yes | The supplier does not exist, or was deactivated |
| `DUPLICATE` | Does not create | The order was already imported, or it is a Teikem one that came back from QuickBooks |
| `SKU_ALIAS`, `COST_ROUNDED`, `ZERO_QTY`, `MERGED_LINES`, `EXPECTED_IGNORED` | No | Informative warnings |

**An order with a product or supplier that does not exist is not imported**: it is blocked and the reason is reported. «Download what is missing» downloads the list of products and suppliers so they can be created.

**Order created.**
- It is born in the chosen status. With **Completed** (`SENT`), creating it and moving it to «Sent» are done in **a single operation**: if the move fails, no half-done draft is left.
- Its **origin** is **«Excel / file»** (with a template or with the generic format) or **«QuickBooks»**.
- Its notes say «Imported with the template «{name}» {number}.» (in Spanish: «Importada con la plantilla «{nombre}» {número}.») followed by the import mark between square brackets (`[IMP:…]` or `[QB:…]`).

**It does not duplicate.** Each imported order keeps an **import key** (supplier + document number). A key that already exists among the **active** orders comes out `DUPLICATE`, even if someone erased the mark from the notes and even if the document came in earlier through another format (generic and template share the key). If two people import the same new file at once, **one creates and the other receives `DUPLICATE`** with «Esa orden ya se importó (otra importación la creó al mismo tiempo).» (that order was already imported: another import created it at the same time). If the imported order is deactivated, the same file creates it again.

**«Round trip» (only with the QuickBooks source).** If you exported a Teikem order to QuickBooks and later import a QuickBooks file that carries it, the document is **not created again**: it comes out `DUPLICATE` with «La orden {n} nació en Teikem y ya se pasó a QuickBooks; no se vuelve a importar.» (order {n} was born in Teikem and was already passed to QuickBooks; it is not imported again). It is recognized because the `Num` or the «P. O. #» is the number of a Teikem order **from the same supplier, already exported or marked**. A purchase bill (*Bill*) born from an exported order is only recognized if it carries «P. O. #».

**With the QuickBooks source,** the supplier whose name is **exactly** the one in the file (ignoring case) is left as **already sent to QuickBooks**: it does not come out again as a new supplier in the IIF file.

**Import validations (field → rule → message → HTTP).**

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `file` | Required | `Elija el archivo a importar.` | 400 | Choose the file to import |
| `file` | At most 5,000 rows or 2 MB | `El archivo supera el límite de 5.000 filas o 2 MB.` | 400 | The file exceeds the limit of 5,000 rows or 2 MB |
| `file` | Must carry orders | `El archivo no trae órdenes de compra.` | 400 | The file carries no purchase orders |
| `file` | Up to 2000 orders per file | `El archivo trae más de 2000 órdenes; divídalo e impórtelo por partes.` | 400 | The file carries more than 2000 orders; split it and import it in parts |
| `source` | `GENERIC`, `QUICKBOOKS` or `TEMPLATE` | `El origen de la importación no es válido: use GENERIC, QUICKBOOKS o TEMPLATE.` | 400 | The import source is not valid: use GENERIC, QUICKBOOKS or TEMPLATE |
| `templatePublicId` | Required with `TEMPLATE` | `Elija la plantilla de importación.` | 400 | Choose the import template |
| `templatePublicId` | Only with `TEMPLATE` | `La plantilla solo se usa con el origen TEMPLATE.` | 400 | The template is only used with the TEMPLATE source |
| `templatePublicId` | Must be active | `La plantilla está inactiva; reactívela o elija otra.` | 400 | The template is inactive; reactivate it or choose another |
| `templatePublicId` | Exists, is a purchasing one and belongs to your company | `Plantilla de importación no encontrado.` | 404 | Import template not found |
| `status` | `DRAFT` or `SENT` | `El estatus de las órdenes importadas debe ser DRAFT (borrador) o SENT (enviada).` | 400 | The status of the imported orders must be DRAFT or SENT |

Warnings per order (they come inside the result, with HTTP 200):

| Warning | Message (Spanish) and meaning |
|---|---|
| `BAD_VALUE` (date) | `La fecha «{texto}» ({campo}) no es válida: use AAAA-MM-DD o MM/DD/AAAA.` The date is not valid: use YYYY-MM-DD or MM/DD/YYYY |
| `BAD_VALUE` (number) | `«{texto}» ({campo}) no es un número: use punto decimal, sin letras.` It is not a number: use a decimal point, no letters |
| `BAD_VALUE` (no number) | `La fila {línea} no trae número de documento (documentNumber); sin él no se sabe a qué orden va.` Row {line} has no document number; without it there is no way to know which order it belongs to |
| `COST_CONFLICT` | `El producto {sku} viene en varias líneas de la orden con costos distintos ({costos}); corrija el archivo para que todas lleven el mismo costo.` The product comes on several lines of the order with different costs; fix the file so all carry the same cost |
| `DUPLICATE` | `Ya se importó antes (orden de compra {número}); no se vuelve a crear.` It was already imported (purchase order {number}); it is not created again |
| `DUPLICATE` (round trip) | `La orden {n} nació en Teikem y ya se pasó a QuickBooks; no se vuelve a importar.` |
| `DUPLICATE` (at once) | `Esa orden ya se importó (otra importación la creó al mismo tiempo).` |
| `MISSING_PRODUCT` | `Falta el producto {sku}: dé de alta el producto con ese SKU y vuelva a importar.` Product {sku} is missing: create the product with that SKU and import again (with «SKU parecidos en Teikem» — similar SKUs in Teikem — if there are any) |
| `MISSING_SUPPLIER` | `Falta el proveedor «{proveedor}»: dé de alta el proveedor con ese nombre y vuelva a importar.` Supplier «{supplier}» is missing: create the supplier with that name and import again |

> **`COST_CONFLICT` changed in Batch 239.** Before, if a file carried the same product with different costs, they were added together and the first cost was taken without warning. Now the order **is blocked** (also with QuickBooks and with the generic format), because that cost now travels to QuickBooks. Fix the file so all the lines of that product carry the same cost.

## 4. Where the order was born (Origin)

Each purchase order has an **origin**. It is seen in the **«Origin»** column of the list, on the record, and filtered with **«Origin»**:

| Origin | When |
|---|---|
| **Teikem** | Created by hand, by «Reorder» of a shortage or by any other way. An order with no stored origin reads as Teikem |
| **Excel / file** | Imported with the template or with the generic format |
| **QuickBooks** | Imported from a QuickBooks file. **Never exported or marked** |

**The orders that already existed** when this batch was installed were classified **only once** by the mark in their notes: `[QB:…]` → QuickBooks, `[IMP:…]` → Excel / file, the rest → Teikem. Those that are sent, partly received and received stay **«To pass»** (see section 10).

API: `GET /api/v1/purchase-orders?origin=EXCEL&origin=TEIKEM` (repeatable or comma-separated; `TEIKEM` includes those with no stored origin). Any other value: 400 «El origen «{valor}» no es válido: use TEIKEM, EXCEL o QUICKBOOKS.» (the origin is not valid: use TEIKEM, EXCEL or QUICKBOOKS).

## 5. State against QuickBooks and its transitions

Each order is in one of **five states against QuickBooks**, which are independent of the purchase order's status (Draft, Sent, Partly received, Received, Cancelled). They are seen with a chip in the list and the record, and filtered with **«QuickBooks»** (`GET /api/v1/purchase-orders?quickBooks=PENDING`, repeatable or comma-separated).

| State (chip) | Code | What it means |
|---|---|---|
| **To pass** | `PENDING` | It can be passed and has not yet come out in any file or been marked |
| **Exported, to confirm** | `EXPORTED` | It came out in a file and it is still to be said whether QuickBooks received it |
| **In QuickBooks** | `IN_QUICKBOOKS` | Marked as already in QuickBooks (when confirming a file or by hand) |
| **Came from QuickBooks** | `FROM_QUICKBOOKS` | It was imported from QuickBooks |
| **Not passed** | `NOT_EXPORTABLE` | By its status it is not passed (Draft or Cancelled) and it never came out or was marked |

**Orders passed** are the **Sent, Partly received and Received** ones, with the **ordered** quantities. **Draft and Cancelled are not passed.**

**Priority when several conditions apply** (the state is the first that holds): Came from QuickBooks > In QuickBooks > Exported, to confirm > To pass > Not passed. That is why **an order that was exported and then cancelled stays «Exported, to confirm»**: it can be confirmed or sent to «It did not go through», but it can no longer be exported again.

**Transitions.** All need `purchasing.quickbooks`. None changes the order's status or triggers effects on inventory, receipts or payments; they only write who and when.

| From | To | Action | What it validates | What effect it has |
|---|---|---|---|---|
| To pass | Exported, to confirm | **Export** | Active order, not from QuickBooks and in status Sent, Partly received or Received | Stores the file's date and who took it out (does not mark) |
| Exported, to confirm | Exported, to confirm | **Export** again | Same as above; warns «N of these already came out…» | Replaces the date and who of the previous file |
| Exported, to confirm | In QuickBooks | **«Yes, they are in»** (confirm) | The order came out in a file (in any status, also cancelled) | Stores who and when marked; the order's supplier and products are left as already sent |
| Exported, to confirm | To pass | **«It did not go through»** | It is not marked nor from QuickBooks | Clears the file's date and who. If the order was cancelled, it stays «Not passed» |
| Exported, to confirm | Exported, to confirm | **«Not yet»** | — | None: it does not call the API; the date is kept |
| To pass | In QuickBooks | **Mark** (by hand or «Mark all») | Eligible order (see Export) | Stores who and when; supplier and products are left as already sent |
| Exported, to confirm | In QuickBooks | **Mark** | It did not come from QuickBooks | Same as confirm |
| In QuickBooks | Exported, to confirm (or To pass if it never came out) | **Remove the mark** | It did not come from QuickBooks | Clears who and when of the mark. It deletes nothing in QuickBooks and does not «unsend» suppliers and products |

**What stays blocked in each state** (the exact message is in section 9):

| State | Export | «Yes, they are in» | «It did not go through» | Mark | Remove the mark |
|---|---|---|---|---|---|
| **To pass** | Yes | 422 «has not come out in any file» | No change | Yes | No change |
| **Exported, to confirm** | Yes (warns); 422 if it was already cancelled | Yes | Yes | Yes | No change |
| **In QuickBooks** | **409** «is already in QuickBooks» | No change (does not repeat who or when) | **409** «is already marked» | No change | Yes |
| **Came from QuickBooks** | **422** | **422** | **422** | **422** | **422** |
| **Not passed** (Draft or Cancelled) | **422** because of the status | **422** «has not come out…» | No change | **422** because of the status | No change |

«No change» is not an error: the response counts the order as «already marked» or «unchanged» and touches nothing.

**Editing, sending or cancelling the order does not change its QuickBooks state.** An order «In QuickBooks» or «Exported» that is edited or cancelled **stays the same**, and the change **does not go to QuickBooks by itself**. The record warns: «Esta orden ya salió hacia QuickBooks: editarla o cancelarla aquí no cambia nada allá. Ciérrela o corríjala en QuickBooks.» (this order already went to QuickBooks: editing or cancelling it here changes nothing there; close or fix it in QuickBooks).

## 6. Passing the orders to QuickBooks (screen)

**Who can.** `purchasing.quickbooks`, module `PURCHASING`.

### 6.1 The «to pass» list

Purchases → **«QuickBooks»** opens the window **«Pass purchase orders to QuickBooks»**: all the **Sent, Partly received or Received** orders that are to be passed (not the marked ones nor those that came from QuickBooks), from oldest to newest.

- Each row: box, **Date**, **Number**, **Supplier**, **Total**, **New in QuickBooks** (chips «New supplier» and «N new products», with **«see data»**) and **File** (if it already came out in a file: «File of {date}» and «by {who}»).
- On top: the search box by number or supplier, **«All of the view (N)»**, **«Mark all»** and **«Include new suppliers and products»** (on by default).
- **«see data»** shows, for that order, the new supplier (name in QuickBooks, contact, phone, e-mail and payment term) and the new products (item in QuickBooks, description and cost).
- If some already came out in a file, a notice says so: «N of these already came out in a file (the last time on {date}). If you already imported them, confirm or mark them; if not, exporting them again repeats them in QuickBooks.».
- **Warnings (they do not stop the export):** supplier name over 41 characters, SKU over 31, supplier term without a name in QuickBooks, rounding difference, currency different from the company's and characters QuickBooks does not accept. The texts are in section 9.

### 6.2 Export and answer «Did you already import them in QuickBooks?»

1. Choose the orders (by default all of the view go) and press **«Export N»**. A file `teikem-quickbooks-compras-YYYYMMDD-HHMM.iif` is downloaded. **Nothing is marked here.**
2. The window changes to **«Did you already import them in QuickBooks? (N)»**, with the file name, the number of orders and the total, how many new suppliers and products it carried, the warnings, and the list of the file's orders with boxes.
3. Import the file in QuickBooks (**File → Utilities → Import → IIF Files**) and answer. **Only the ticked ones count; the unticked ones do not change:**
   - **«Yes, they are in (N)»:** they become **In QuickBooks**.
   - **«It did not go through (N)»:** QuickBooks did not receive them; they go back to **To pass** and lose the file's date.
   - **«Not yet»:** closes the question without changing anything. They stay **Exported, to confirm** (the list shows «File of {date}») and can be answered later.
   - **«Download again»:** downloads the same file.
4. **«Mark N as in QuickBooks»** (at the bottom, next to «Close»): it leaves them in QuickBooks **without taking out a file**, for those that are already there because they were entered by hand. **«Mark all»** does the same with all the to-pass ones, with a confirmation («N orders will be marked as already passed to QuickBooks, without taking out a file. Use it for historical orders that are already there. Continue?»).

### 6.3 The order record

Under the order's title, a strip shows the **origin** and the **QuickBooks state**, and:
- **In QuickBooks:** «Marked on {date} by {person}» and the **«Remove the mark»** button (asks for confirmation; the order goes back to «to pass», or to «exported» if it came out in a file; nothing is deleted in QuickBooks).
- **Exported, to confirm:** «Exported on {date} by {person}; it is still to be confirmed that it was imported» and the **«Yes, it is in»**, **«It did not go through»** and **«Pass to QuickBooks…»** buttons (the last opens the window with that order).
- **To pass:** **«Pass to QuickBooks…»** and **«Mark as in QuickBooks»** (with confirmation, without taking out a file).
- In the list, an order «In QuickBooks» has the **«Remove the mark»** action on its row.

## 7. Passing the orders to QuickBooks (API)

All carry `purchasing.quickbooks` and the module `PURCHASING`. The bodies use the orders' `publicId`.

| What | Call | Body |
|---|---|---|
| What is left to pass | `GET /api/v1/purchase-orders/quickbooks` | — |
| Export | `POST /api/v1/purchase-orders/quickbooks/export` | `{ "purchaseOrderIds": ["…"], "all": false, "includeNew": true }` |
| «Yes, they are in» | `POST /api/v1/purchase-orders/quickbooks/confirm` | `{ "purchaseOrderIds": ["…"] }` |
| «It did not go through» | `POST /api/v1/purchase-orders/quickbooks/not-passed` | `{ "purchaseOrderIds": ["…"] }` |
| Mark by hand | `POST /api/v1/purchase-orders/quickbooks/mark` | `{ "purchaseOrderIds": ["…"] }` or `{ "all": true }` |
| Remove the mark | `POST /api/v1/purchase-orders/quickbooks/unmark` | `{ "purchaseOrderIds": ["…"] }` |

- **What is left to pass** (`GET`) carries `count`, `total`, `alreadyExportedCount`, `lastExportedAtUtc`, the `orders`, the `newSuppliers`, the `newProducts`, the `warnings` and `purchaseOrderAccount`. It is not paginated: «Export all» takes them all.
- **Export:** up to 1000 orders per call (for more, `"all": true`). `includeNew` (`true` by default) adds the new suppliers and products to the file. It answers `fileName`, `contentType`, `contentBase64` (the file in Windows-1252, in base64), `exportedAtUtc`, `orderCount`, `total`, `alreadyExportedCount`, `lastExportedAtUtc`, `includedNew`, `newSupplierCount`, `newProductCount`, `warnings` and `orders`. It **records** the file's date and who took it out on each order, but **does not mark it**.
- **Confirm, «It did not go through» and remove the mark** require `purchaseOrderIds` (they do not accept `all`). **Mark** and **export** do accept `all`.
- **Results:** confirm and mark answer `marked`, `alreadyMarked`, `suppliersSent`, `productsSent` and `atUtc`. «It did not go through» answers `reset`, `unchanged` and `atUtc`. Remove the mark answers the same shape as mark: `marked` is how many **lost** the mark and `alreadyMarked` how many **did not have it**.
- Each call that writes runs in a single operation and **locks the indicated orders** while it works: if two people export, mark or confirm the same order at once, they work **one after the other** and the second sees the first's result (for example, 409 «is already in QuickBooks» or the «already came out» warning).

## 8. The purchase-order IIF file

The file is text in **Windows-1252**, with tabs and Windows line endings, and `MM/DD/YYYY` dates. It is imported in QuickBooks Desktop at **File → Utilities → Import → IIF Files**. Each block comes out **only if it carries something**, in this order:

1. **New products (`!INVITEM`):** the same block as the invoices (name, type `INVENTORY`, description, sales, inventory and cost accounts, price, cost and tax). Only with «Include new suppliers and products».
2. **New suppliers (`!VEND`):** name, company, contact, phone (with prefix), e-mail, payment term (with the name given in Settings → Operation → QuickBooks) and note. Only with «Include new suppliers and products».
3. **The orders (`!TRNS` / `!SPL` / `!ENDTRNS`),** one transaction per purchase order:
   - **`TRNS` of type `PURCHORD`:** order date, **«Purchase Orders»** account (the one in Settings), supplier, **negative amount** (the sum of the lines, already rounded to cents), `DOCNUM` = the order's number in Teikem (`PO-#####`), `MEMO` = the notes **without** the import mark, and `DUEDATE` = expected date (empty if there is none).
   - **One `SPL` per line:** inventory account, **positive** amount, `DOCNUM`, memo = product name, quantity (up to 4 decimals), cost (up to 4 decimals) and `INVITEM` = the product's SKU.
   - Each order **adds up to 0.00 by construction**.
- **What it does not carry:** currency (QuickBooks uses the company's), taxes, shipping or discounts (Teikem's purchase order does not store them), or each line's own description. **Kits** are bought as the product they are (they are not broken down).
- **Text cleaning:** tabs and line breaks become a space; double quotes become single; and in **names** (supplier, item) colons become a hyphen (in QuickBooks «A:B» would be a sub-supplier or sub-item). Characters Windows-1252 does not have come out as «?» and a warning is given.
- **Rounding:** QuickBooks rounds each line to cents; Teikem rounds at the end. If the file's total differs from Teikem's, a warning comes out. The file sends QuickBooks the rounded figure.

**Purchase orders account.** It is at **Settings → Operation → QuickBooks → «Purchase orders account»** (**«Purchase Orders»** by default, a non-posting QuickBooks account). An empty value goes back to the default. The maximum is 159 characters. API: `GET` and `PATCH /api/v1/sales-orders/quickbooks/settings` with `purchaseOrderAccount` (read: `sales.quickbooks.view` or `admin.tenant`; change: `admin.tenant`; Sales orders module). The change applies from the next file.

## 9. Validations and messages

**Messages are in Spanish; the English meaning is in the last column.**

### 9.1 Export, confirm, «It did not go through», mark and remove the mark

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `purchaseOrderIds` / `all` | Indicate orders or ask for all (export and mark) | `Indique las órdenes de compra (purchaseOrderIds) o pida todas (all = true).` | 400 | Give the purchase orders (purchaseOrderIds) or ask for all (all = true) |
| `purchaseOrderIds` | Up to 1000 per call | `No se pueden marcar más de 1000 órdenes de compra en una sola llamada; use «todas».` | 400 | No more than 1000 purchase orders in one call; use «all» |
| `purchaseOrderIds` | All must exist, be active and belong to your company | `Alguna de las órdenes de compra indicadas no existe.` | 400 | One of the indicated purchase orders does not exist |
| export, mark with `all` | There must be something to pass | `No hay órdenes de compra por pasar a QuickBooks.` | 422 | There are no purchase orders to pass to QuickBooks |
| export, confirm, «It did not go through», mark, remove the mark | The order did not come from QuickBooks | `La orden de compra {n} vino de QuickBooks: no se exporta ni se marca.` | 422 | Purchase order {n} came from QuickBooks: it is neither exported nor marked |
| export, mark | The order is Sent, Partly received or Received (mark also accepts one already exported) | `La orden de compra {n} está en {estatus}: solo se pasan a QuickBooks las enviadas, recibidas en parte o recibidas.` | 422 | Purchase order {n} is in {status}: only sent, partly received or received ones are passed to QuickBooks |
| export | The order is not already marked | `La orden de compra {n} ya está en QuickBooks; para volver a exportarla, quítele la marca.` | 409 | Purchase order {n} is already in QuickBooks; to export it again, remove its mark |
| confirm | The order came out in some file | `La orden de compra {n} no ha salido en ningún archivo de QuickBooks; para marcarla a mano use «Marcar como en QuickBooks».` | 422 | Purchase order {n} has not come out in any QuickBooks file; to mark it by hand use «Mark as in QuickBooks» |
| «It did not go through» | The order is not marked | `La orden de compra {n} ya está marcada como en QuickBooks: quítele la marca en vez de usar «No pasó».` | 409 | Purchase order {n} is already marked as in QuickBooks: remove its mark instead of using «It did not go through» |
| any | Permission `purchasing.quickbooks` | `Falta el permiso 'purchasing.quickbooks'.` | 403 | Missing permission |
| any | Module `PURCHASING` on | `El módulo 'PURCHASING' no está habilitado para esta compañía.` | 403 | The module is not enabled for this company |

`{n}` is the order's number (for example `PO-00012`) and `{estatus}` its status in the user's language. With several orders, the message is that of **the first** that fails and **none is changed**.

### 9.2 Warnings that do not block

They come out in the «to pass» list and in the export result:

| Warning | Text (Spanish) and meaning |
|---|---|
| Long supplier name | `El nombre del proveedor «{nombre}» pasa de 41 caracteres: QuickBooks no lo acepta como nombre de proveedor.` The supplier name exceeds 41 characters: QuickBooks does not accept it as a supplier name |
| Long SKU | `El SKU «{sku}» pasa de 31 caracteres: QuickBooks no lo acepta como nombre de ítem.` The SKU exceeds 31 characters: QuickBooks does not accept it as an item name |
| Term without a name | `El término «{código}» no tiene nombre en QuickBooks (Ajustes → Operación → QuickBooks): el proveedor nuevo sale sin término.` The term has no name in QuickBooks: the new supplier comes out without a term |
| Rounding | `La orden de compra {n} suma {Teikem} en Teikem y {archivo} en el archivo: QuickBooks redondea cada línea a centavos.` Purchase order {n} adds up to {Teikem} in Teikem and {file} in the file: QuickBooks rounds each line to cents |
| Different currency | `La orden de compra {n} está en {moneda} y la compañía en {monedaCompañía}: el archivo no lleva moneda y QuickBooks la toma en la de la compañía.` The order is in {currency} and the company in {company currency}: the file carries no currency and QuickBooks takes the company's |
| Lost characters | `{dónde} tiene caracteres que QuickBooks no admite ({caracteres}); en el archivo salen como «?».` {where} has characters QuickBooks does not accept; in the file they come out as «?» (with «where» = «El memo de la orden de compra {n}» (the memo of purchase order {n}), «El proveedor «{nombre}»» (the supplier), «El SKU «{sku}»» (the SKU) or «La descripción del producto {sku}» (the description of product {sku})) |

### 9.3 List filters

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `origin` | `TEIKEM`, `EXCEL` or `QUICKBOOKS` | `El origen «{valor}» no es válido: use TEIKEM, EXCEL o QUICKBOOKS.` | 400 | The origin is not valid: use TEIKEM, EXCEL or QUICKBOOKS |
| `quickBooks` | `PENDING`, `EXPORTED`, `IN_QUICKBOOKS`, `FROM_QUICKBOOKS` or `NOT_EXPORTABLE` | `El estado de QuickBooks «{valor}» no es válido: use PENDING, EXPORTED, IN_QUICKBOOKS, FROM_QUICKBOOKS o NOT_EXPORTABLE.` | 400 | The QuickBooks state is not valid: use one of the five codes |

### 9.4 Settings

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `purchaseOrderAccount` | Up to 159 characters | `El nombre de una cuenta de QuickBooks no puede pasar de 159 caracteres.` | 400 | A QuickBooks account name cannot exceed 159 characters |

The template and import messages are in sections 2 and 3.

## 10. Before the first export

1. **Test the file on a copy of the QuickBooks company** (Solutions') before using it on the real one. It is the only thing missing to consider it good. If QuickBooks rejects it or posts it differently, tell support: what is adjusted first are the signs, the accounts and the delivery date, and they are adjusted in one single place in the code.
2. **Mark the history.** The orders that already existed were left **«To pass»** if they were Sent, Partly received or Received. The ones that are already in QuickBooks are marked with **«Mark all»** (or by choosing and «Mark N as in QuickBooks»), **without taking out a file**. If not, the first export would send them again and they would be duplicated in QuickBooks.
3. **Turn off «Include new suppliers and products»** until you confirm the names match QuickBooks'. Without that option the file carries only the orders, and QuickBooks rejects them if the supplier or product does not exist there.
4. **Suppliers that are already in QuickBooks.** The system already marked as «already sent» the suppliers that have orders imported from QuickBooks or that appear in the QuickBooks purchase history (Sales analysis), so the first file does not send them as new. Check in **«see data»** who comes out as new.
5. **Give the payment terms a name in QuickBooks** (Settings → Operation → QuickBooks); if not, the new supplier comes out without a term and a warning is given.

## 11. Frequently asked questions of the module

**Does exporting mark the order as passed?**
No. It only records the file's date and who took it out. It stays «Exported, to confirm» until you answer «Yes, they are in» or «It did not go through».

**I answered «Not yet». What happened?**
Nothing. The orders stay «Exported, to confirm» and can be confirmed or sent to «It did not go through» when you know. If you export them again, the system warns «N of these already came out…».

**What is the difference between «It did not go through» and «Not yet»?**
«It did not go through» means QuickBooks did **not** receive them: it clears the file's date and sends them back to «To pass». «Not yet» changes nothing.

**I edited an order that was already in QuickBooks. Does it update there?**
No. The state does not change and the change does not go to QuickBooks by itself; close or fix it there. If you want to export it again, remove its mark and export (in QuickBooks there would be one more order).

**I cancelled an order that was already in QuickBooks.**
It stays «In QuickBooks»; it has to be cancelled there too. If it was «Exported, to confirm», you can confirm it or send it to «It did not go through», but not export it again.

**I re-imported from QuickBooks an order I had sent from Teikem and it said «ya se pasó a QuickBooks; no se vuelve a importar» (already passed to QuickBooks; it is not imported again).**
That is expected («round trip»). The order already exists in Teikem. It is only recognized if the number or the «P. O. #» is the Teikem order's, from the same supplier, and that order was already exported or marked.

**Two people exported the same order at the same time.**
The system queues them. Both receive their file, but the second sees the «already came out» warning (or a 409 if the first already confirmed it). What is imported into QuickBooks is the responsibility of whoever imports it: import **only one** of the two files.

**Why can I not export an order that came from QuickBooks?**
Because it is already there. It is never exported or marked.

**My file says «costos distintos» (different costs) and it used to import.**
Since this batch, the same product with different costs in one order blocks the order (before, the first one was taken silently). Fix the file.

**Where is an order's origin seen?**
In the «Origin» column of the list and in the record's strip. The notes of an imported order also carry the mark between square brackets (`[IMP:…]`); do not erase it on purpose even if it is no longer needed to avoid duplicates.

**I do not see the «QuickBooks» button.**
The `purchasing.quickbooks` permission is missing or the Purchases module is off.

**I do not see System → Templates.**
That screen belongs to the Integrations module, which on a new database only the platform administrator turns on. Meanwhile, create and edit the template from Purchases → Import.

## 12. For support

- **An order's state and data:** `GET /api/v1/purchase-orders/{publicId}` carries `originCode`, `origin`, `quickBooksState`, `quickBooksExportedAtUtc`, `quickBooksExportedByName`, `quickBooksAtUtc` and `quickBooksByName`.
- **What is missing and what came out:** the list with the **«QuickBooks»** filter (`PENDING`, `EXPORTED`, `IN_QUICKBOOKS`). There is no separate history of files: the record says who and when exported or marked each order.
- **«I passed an order by mistake»:** «Remove the mark» (if it was marked) or «It did not go through» (if it was only exported). Nothing is deleted in QuickBooks.
- **A supplier comes out as new and already exists in QuickBooks:** mark one of its orders as «In QuickBooks» (the order's supplier and products are left as already sent) or export without «Include new suppliers and products».
- **A template no longer works:** deactivate it (it is not deleted) and create another. Orders already imported with it keep its name in their notes.
- **The file did not enter QuickBooks:** keep the file and tell support with QuickBooks' message; remember the format **has not yet been tested against a real QuickBooks**.
- The QuickBooks actions are logged on the order (who and when it was exported or marked) and denied permissions in the security events.
