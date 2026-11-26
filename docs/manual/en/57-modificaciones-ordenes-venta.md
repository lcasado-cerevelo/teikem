# 57 — Sales order modifications: changing an already requested order with the client's authorization

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter [`../57-modificaciones-ordenes-venta.md`](../57-modificaciones-ordenes-venta.md) (Batch 249, 2026-11-26). The Spanish manual is the reference. **The API returns its error messages in Spanish only**, so the messages below are quoted in Spanish with their English meaning next to them. Interface labels use the English texts of the software.

Batch 249 (2026-11-26; Batch E of the Solutions plan). Six new things:

1. **The «Order modification».** Every change to an already requested order (quantity, price, adding or removing lines, manual IVU, payment method, delivery or pickup, consignee and notes) goes through a modification that is stored with **who registered it, who authorized it at the client, how, when, the reason, the evidence, the before-and-after snapshot and the totals**.
2. **What is allowed depends on the real stage of the goods:** not collected (everything), collected (**only** the necessary pick is undone), packed (the pack is reopened with a special permission) and already left or invoiced (it can only be added to).
3. **The internal addition order:** what can no longer be changed is added in a separate order that hangs from the original and can leave in the same shipment.
4. **Second approval** (per company or per client): another person approves before anything is applied.
5. **Manual IVU per line** (separate permission, with a reason): it affects only that line; the client's exemption keeps applying to the others.
6. **Billing «when the user decides»** (`ON_REQUEST`): a new billing basis, besides on dispatch and on delivery.

> **This touches inventory and money.** A modification can undo picks (the inventory goes back to its bin), reopen packs (the shipment is voided) and change an order's total. Read section 3 (what is possible at each stage) before giving someone the `sales.modify` permission.

> **No action in this chapter sends an e-mail by itself.** Notices (section 10) go out through the **notification rules**; with no rule, the system records the notice and delivers it to nobody.

Screens with captures: [L249](../frontend/l249-modificar-orden.md) (Spanish edition; the captures `img/l249-*-en-*.png` in that folder were taken in English). Every error message is in the [FAQ](faq.md). It completes chapters [18 — Sales orders](18-ordenes-de-venta.md), [19 — Billing](19-facturacion.md) and [56 — Documents with validity, IVU exemption](56-documentos-vigencia-ivu.md).

## 1. Where it is and who can

- **Where:**
  - **Operations → Sales orders** → open an order → **«Modify order»** button, **«Modifications»** panel (the history) and **«Internal addition order»** button (`/sales-orders`).
  - **Operations → Sales orders → «To approve» tab**: the second-approval inbox (only with `sales.modify.approve`).
  - **Warehouse → Picking & packing** → a pick: **«Reopen the pack»** (packed ones) and **«Join the shipment of OV-…»** (collected picks of a sales order).
  - **System → Settings → Operation → «Order modifications»** (company) and **client record → Profile → «Order modifications»** (client).
  - **Guided order** (creating an order): whoever has `sales.price` can now type the price of **any** line, also of products with a list price.
- **Modules:** Sales orders (`SALES_ORDERS`) for everything; Taxes (`TAX`) for manual IVU; Warehouse for reopening and joining. With a module off the route answers 403 with `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` (with the name of the module concerned).
- **Permissions.** There are **four new permissions** (the company now has 167):

| What | Permission | Module |
|---|---|---|
| See the edit options, the history and a modification | `sales.view` | `SALES_ORDERS` |
| Preview, register, withdraw, upload the evidence and create the addition | **`sales.modify`** (new) | `SALES_ORDERS` |
| Change the price to one different from the list (today's) price | also `sales.price` | `SALES_ORDERS` |
| Manual IVU of a line | **`sales.tax`** (new) besides `sales.modify` | `SALES_ORDERS` and `TAX` |
| Second approval (approve, reject) and the «To approve» inbox | **`sales.modify.approve`** (new) | `SALES_ORDERS` |
| Reopen a pack (in a modification or on its own) | **`warehouse.reopen-pack`** (new) | `SALES_ORDERS` |
| Join a pick to its order's shipment | `warehouse.pick` | `SALES_ORDERS` |
| Authorize an exceeded credit when the total goes up | `orders.credit_override` | `SALES_ORDERS` |
| Company settings | `admin.tenant` | `SALES_ORDERS` |
| A client's settings | `clients.update` | `SALES_ORDERS` |
| See the settings | `sales.view` (company); `sales.view` or `clients.read` (client) | `SALES_ORDERS` |
| Read the data source «Sales order changes» (`SALES_ORDER_CHANGE`) | `sales.view` | `SALES_ORDERS` |

- **Distribution to each company's roles** (also to the roles **already cloned**, once; it only adds, never removes): `sales.modify` to TenantAdmin, SalesSupervisor and SalesOffice; `sales.tax` and `sales.modify.approve` to TenantAdmin and SalesSupervisor; `warehouse.reopen-pack` **only** to TenantAdmin. **The «Sales» role can no longer change quantities of already requested orders** (before, `sales.create` was enough): if needed, give it `sales.modify`.
- `TenantId` comes from the session. An order, modification, file, contact or product of another company answers 404, without saying whether it exists.
- Without the permission, the route answers 403 with `Falta el permiso '…'.` and a `PERMISSION_DENIED` event is left in the security events.

## 2. The order modification

**What it does.** Changes a **requested, partial or shipped** order with the rules of section 3. Before saving you can see the **preview** (the difference, the effects on the warehouse, the credit and whether it asks for a second approval) without writing anything.

**How to use it (screen).** Open the order → **«Modify order»**. The wizard has four steps:
1. **Lines.** Each line shows *Ordered · Collected · Packed · Left · Invoiced*, the **quantity** with its **«Minimum»** (what it can be lowered to), the **price** (with the «List price»), **«Manual IVU»** and **«Remove line»**. At the top the stage of each pick appears: «Collected: can be undone», «Packed: can be reopened» or «Already left». **«Add a product»** and the switch «If stock is short, leave the missing part on backorder».
2. **Terms.** Payment method, term, delivery or pickup, consignee and notes.
3. **Authorization.** «Name of who authorized» (required), «Title», «Client contact (optional)», «How it was authorized» (by phone, by e-mail, in person or by message), «Date and time of the authorization» (by default, now), «Reason for the change» and the **«Evidence»** (image or PDF; required if the client or the company require it).
4. **Preview.** «Total before → Total after», the difference line by line, what happens in the warehouse, the credit, whether it asks for a second approval and the blockers. **«Save the modification»**.

When saving without second approval it says «Modification #N applied.». With second approval it says «Modification #N registered: it is waiting for the second approval and nothing has been applied yet.».

**How to use it (API).** All routes hang from `/api/v1/sales-orders/{orderPublicId}`.

| What | Route | Permission |
|---|---|---|
| Stage, blockers, position per line, picks, invoices, settings, pending one and my permissions | `GET …/edit-options` | `sales.view` |
| Preview (writes nothing) | `POST …/changes/preview` | `sales.modify` |
| Register (and apply, or leave for approval) | `POST …/changes` | `sales.modify` |
| History (most recent first) | `GET …/changes` | `sales.view` |
| A modification with its before, after, difference and effects | `GET …/changes/{changePublicId}` | `sales.view` |
| Approve / reject / withdraw | `POST …/changes/{changePublicId}/approve`, `/reject`, `/withdraw` | `sales.modify.approve` (approve and reject); `sales.modify` or whoever registered it (withdraw) |
| Upload the evidence (multipart, `file`) | `POST …/change-attachments` | `sales.modify` |
| Internal addition order | `POST …/additions` | `sales.modify` |
| Old route of a line | `PATCH /api/v1/sales-orders/{id}/lines/{lineId}` | `sales.modify` |

Register a modification (`POST …/changes`). What is not sent does not change.

```json
{
  "ops": [
    { "kind": "QTY",   "lineId": 41, "quantity": 6 },
    { "kind": "ADD",   "productPublicId": "0f8fad5b-d9cb-469f-a165-70867728950e", "quantity": 2 },
    { "kind": "REMOVE","lineId": 42 },
    { "kind": "TAX",   "lineId": 43, "taxPct": 1, "note": "Cliente con acuerdo de exención" }
  ],
  "authorizedByName": "Marta Ríos",
  "authorizedByTitle": "Compras",
  "authorizationChannel": "PHONE",
  "authorizedAtUtc": "2026-11-26T14:10:00Z",
  "reason": "El cliente llamó para cambiar las cantidades.",
  "attachmentFilePublicId": null,
  "reopenPack": false,
  "allowBackorder": false,
  "overrideCredit": false,
  "rowVersion": "AAAAAAAB9E="
}
```

**Operations (`ops[].kind`).**

| Kind | Fields | What it does |
|---|---|---|
| `ADD` | `productPublicId`, `quantity`, `unitPrice` (optional) | Adds a product at the client's price of today; a different price requires `sales.price`. If the product had been removed, **its line is reactivated** with the same number |
| `QTY` | `lineId`, `quantity` | Changes the ordered quantity: raises (reserves) or lowers (releases, or undoes picks: section 4) |
| `REMOVE` | `lineId` | Removes the line: ordered 0 and closed (never deleted) |
| `PRICE` | `lineId`, `unitPrice` | Changes the price; takes the origin resolved today |
| `TAX` | `lineId`, `taxPct`, `note` | Manual IVU (section 6); requires `sales.tax` |
| `CLEAR_TAX` | `lineId` | Removes the line's manual IVU |
| `TERMS` | values in `terms` | Payment, term, delivery (`DELIVERY` or `PICKUP`), consignee (`consigneePublicId`, `clearConsignee`) and consignment |
| `NOTES` | value in `notes` | Order notes (up to 500) |

Sending `terms` or `notes` adds only the `TERMS` or `NOTES` operation.

**Other fields.** `authorizedByContactId` (active contact **of the order's client**; if chosen, it is recorded), `reopenPack` and `reopenReason` (section 5), `allowBackorder` (leaves on backorder what is short when raising or adding), `overrideCredit` (section 8), `rowVersion` (the one from `edit-options`: if the order changed, 409).

**Field rules.**
- `authorizedByName`: required, up to 150. `authorizedByTitle`: optional, up to 100.
- `authorizationChannel`: `PHONE`, `EMAIL`, `IN_PERSON` or `MESSAGE` (values of the catalog «Modification authorization channel»).
- `authorizedAtUtc`: empty = now; **it cannot be in the future** (5 minutes of clock skew are tolerated) **nor earlier than the creation of the order**.
- `reason`: required, up to 500.
- `attachmentFilePublicId`: a file uploaded before with `change-attachments` (image or PDF, with the sizes and types of the file store). **A file backs a single modification.**
- A modification that changes nothing answers 400 «Indique al menos un cambio.».
- A line cannot come twice with the same operation, nor with `REMOVE` next to another, nor with `TAX` next to `CLEAR_TAX`.
- All lines cannot be removed: for that, the order is cancelled.

## 3. What can be changed depending on the stage

The system looks at the **real stage of each pick** of the order. The screen and `GET …/edit-options` show it before sending (per line: `minQuantity`, `minQuantityWithReopen`, `inCollected`, `inPacked`, `out`, `invoiced`, `lockedReason` and `priceLockedReason`).

| Stage of the goods | What is possible | How |
|---|---|---|
| **Ordered and reserved, not collected** | Everything | It is reserved and released as always |
| **Collected** (COLLECTED, of a delivery) | Everything | Lowering below what is collected **undoes only the necessary picks** (section 4); the warehouse dispatches again |
| **Packed** (PACKED), with its shipment in the initial stage and nothing invoiced | Everything, **reopening the pack** in the same call | `reopenPack: true` with the `warehouse.reopen-pack` permission (section 5) |
| **Already left** | What left is **not touched** | It cannot be lowered or removed below what left; the price and IVU of that line do not change either. It can be **added to** |
| **Invoiced** | **It can only be added to** | Internal addition order (section 7) |

**What counts as «already left»:** what was dispatched and is not in a pick that can be undone or reopened. It includes: **pickup** (`PICKUP`: what was collected is already taken by the client), what **a linked company counts**, a **manual dispatch**, a shipment that passed its initial stage, voided or with the freight invoiced.

**What counts as «invoiced»:** any line with an invoiced quantity, or the shipping charge already invoiced. The **draft** of an open run and an order **only collected** that was already invoiced with the «on dispatch» basis also count. An invoiced order **only admits an addition**, even if it has lines not yet invoiced, and neither its notes nor its terms change. Removing or lowering is done with a credit note.

**Other blockers for the whole order:**
- **Draft:** it is changed from its edit screen, not with a modification.
- **Cancelled:** it is not modified.
- **Left complete:** something left and nothing is left in the warehouse (neither reserved, nor on open backorder, nor collected or packed without leaving): it can only be added to with an internal addition order.
- **Part already left:** payment, delivery and consignee are no longer changed.
- **Line closed with a shortage:** its quantity is no longer changed.

**When the pack has to be reopened:** to change the quantity, price or IVU of a line that is in a pack, to remove it, and to change the terms if a pack exists (the COD and the shipment would be outdated). **Adding new lines does not require it:** they go in another pick.

**Effect on the order's status** (always through the status service, with the comment «Modificación n.º N»): a **shipped** order that receives something to dispatch goes back to **partial** (or to **requested** if nothing was left dispatched); a **partial** one with nothing dispatched goes back to **requested**; a requested or partial one with everything finished becomes **shipped**.

## 4. Lowering what is already collected: the minimal reversal

If the new quantity falls below what is in picks, the system **undoes whole picks, but only the necessary ones**:
- First the **collected** ones, from the **most recent to the oldest**. The **packed** ones only if reopening was requested.
- Each undone pick **returns the inventory to its bin** (it appears in the Kardex as a pick reversal) and the line is **reserved again**; the order stays alive. Then the warehouse dispatches again.
- **There is no partial reversal:** a pick is undone whole. If it carries other lines, those units also go back.
- **Global guard:** **a pick whose goods were already invoiced is not undone** with the «on dispatch» or «on delivery» basis, because an issued invoice is not reopened: it answers **409** «La recolección … ya está facturada (…): no se puede deshacer.». It is resolved with a **return** or a credit note. With the «when the user decides» basis (`ON_REQUEST`) what was invoiced does not count (what was returned does). It also applies when deleting the pick by hand from the Warehouse.

## 5. Reopening the pack

**What it does.** Voids the shipment (transport order) of a pack **while it is still in its initial stage** and the pick goes from **Packed** back to **Collected**. The pack number (EMP) and the shipment's become free and are reused when packing again. No status notice goes to the client and no webhooks.

**Who can.** `warehouse.reopen-pack` (only TenantAdmin by default), module `SALES_ORDERS`.

**How to use it.**
- **Inside a modification:** check **«Reopen the pack …»** (`reopenPack: true`). It is **atomic**: if the modification fails, the pack is not left reopened. If reopening is needed and it was not checked, the modification answers 422 with the pack's name. With second approval, the permission is checked on whoever registers and the pack is reopened **on approval**.
- **On its own:** Warehouse → Picking & packing → the pick → **«Reopen the pack»** (asks for the reason).

| What | Route | Permission |
|---|---|---|
| Reopen a single pick | `POST /api/v1/pick-batches/{id}/reopen-pack` | `warehouse.reopen-pack` |
| Reopen a master (all its picks) | `POST /api/v1/pick-batches/groups/{id}/reopen-pack` | `warehouse.reopen-pack` |

```json
{ "reason": "El cliente cambió la cantidad de la orden.", "rowVersion": "AAAAAAAB9E=" }
```

`reason` is required (up to 500; it goes to the history as «Empaque reabierto: {reason} (envío {ORD} anulado)»). In a modification, if `reopenReason` does not come, the reason is «Modificación n.º N de OV-…: {reason}». `rowVersion` is optional (409 if the record changed).

**It is not reopened** (422, see section 12): a pick of a master (it is reopened from the master); shipment already advanced, voided or missing; linked company; manual dispatch; freight, check or return fee invoiced; client invoice already processed; **products already invoiced** (with any billing basis: the addition leaves in its own shipment).

**Notice.** Reopening sends «Pack reopened» (section 10) for each sales order served.

## 6. Manual IVU per line

**What it does.** Sets the IVU of **one line** to a percentage you type, with a reason, instead of the normal calculation. **It affects only that line:** the client's exemption and the seasonal period's exemption keep applying to the others.

**Who can.** `sales.tax` besides `sales.modify`, with the `TAX` module on (otherwise 422 «El IVU manual exige el módulo de impuestos encendido.»).

**How to use it.** In the «Lines» step: **«Manual IVU»** (percentage and reason). **«Remove manual IVU»** returns the line to its normal calculation, **with the client's exemption if the document is still valid**. By API: `TAX` (with `taxPct` and `note`) and `CLEAR_TAX`.

**Rules.**
- `taxPct`: required, from 0 to 100, up to 4 decimals. **0 leaves the line exempt.**
- `note` (the reason): required, up to 250.
- The amount is **quantity × price × % / 100**, rounded to the cent (half up), and is **recalculated** if the quantity or price changes.
- The line is left with the reason `MANUAL_OVERRIDE`, and keeps **what the system would charge today** (`taxComputedRatePct` and `taxComputedAmount`), who set it and when. The preview shows «IVU reason» and «Manual IVU».
- It **overrides the exemption by document** (Batch 248) on that line; when removed, the normal calculation returns.
- Only in a **modification** of an already requested order: not in the draft or in the guided order.
- The IVU of a line that already partly left or of a removed one is not changed. A line in a pack asks to reopen it.

## 7. The internal addition order

**What it does.** When an order no longer admits changes (invoiced, already left complete, or a pack that is not reopened), what the client asks for on top goes in a **separate order** that hangs from the original: the **addition**.
- It is a **normal order** (with its OV number, its reservation, its pick, its shipment and its invoice) with a reference to its parent order. An addition of an addition hangs from the **original** (the root).
- It is created and requested **in a single transaction**, with the usual creation and request (reservation, credit, «Order requested» notice). **It does not ask for the client's signature**: the authorization stays in the «Addition» modification of the root order.
- It copies from the parent the client, warehouse, consignee, delivery, payment method, consignment, and the **seller and route** (so commissions and analyses group it with the original). Its notes start with «Adición de OV-…».
- You can only add to a **requested, partial or shipped** order.
- **An order with open additions is not cancelled** (neither cancelled nor shipped): first they are cancelled or shipped.

**How to use it.** Order → **«Internal addition order»**: products and quantities, plus the same client authorization. By API: `POST …/additions`.

```json
{
  "lines": [ { "productPublicId": "0f8fad5b-d9cb-469f-a165-70867728950e", "quantity": 2 } ],
  "notes": "Entrega con la orden original",
  "authorizedByName": "Marta Ríos",
  "authorizationChannel": "EMAIL",
  "reason": "El cliente pidió dos cajas más.",
  "allowBackorder": false,
  "overrideCredit": false
}
```

**Leaving in the same shipment.** Warehouse → the addition's pick → **«Join the shipment of OV-…»** (`POST /api/v1/pick-batches/{id}/join-order-shipment`, `warehouse.pick`). It is a **narrow exception** to the rule «documents are not added afterwards»:
- Only with picks of the order's **«family»** (the order itself, its original and the original's additions), of the same client and warehouse.
- The pick being joined must be **Collected**.
- If the destination is already **packed**, it asks for **«Reopen the pack»** (`reopenPack: true`, `reason`, and the `warehouse.reopen-pack` permission). The master that is born **takes the EMP number of the reopened shipment**, so labels the client or driver already saw still work.
- If there are several possible destinations, choose with `targetBatchPublicId`.

```json
{ "targetBatchPublicId": "7c9e6679-7425-40de-944b-e07fc1f90ae7", "reopenPack": true, "reason": "Sale con la orden original", "note": null }
```

With unrelated documents «documents are not added afterwards» still applies.

**Billing of the addition.** It enters the normal runs: the same invoice if it falls in the same run as its parent, and another if the parent was already invoiced. The reference travels in the **line description** («OV-00045 (adición de OV-00040 · complementa FAC-00012) · SKU Name», cut to 250), in the **invoice MEMO of the IIF** («OV-00045 (adición de OV-00040 · FAC-00012)», up to 4000) and in the invoice's `complements`. There is no new column on the invoice.

## 8. Credit

- **It is only checked if the total goes up** (with IVU and shipping). If it goes down or stays the same, it is not.
- **Exceeded:** 422 `credit_exceeded` with the usual notice to whoever authorizes credit. To continue, `overrideCredit: true` with the `orders.credit_override` permission: it is marked on the modification and in the security events.
- On **approval** it is checked again with today's order. If whoever registered already authorized the credit, whoever approves is not asked again.
- In an **addition** the new order is checked before opening the transaction (exceeded = 422 and neither the addition nor the record is left) and again when requesting it.

## 9. Second approval and required evidence

**What it does.** Each company (and each client, who **rules** over the company) decides whether a modification needs **another person** to approve it before being applied, and whether the evidence of the authorization is required.

| Setting | Values | Where |
|---|---|---|
| **Second approval** | `NONE` («Not required»), `ALWAYS` («Always»), `ABOVE_AMOUNT` («Above an amount») | Settings → Operation → «Order modifications» (company); client record → Profile → «Order modifications» (client; «Same as the company» = inherits) |
| **Amount** | Greater than 0, up to 2 decimals (with `ABOVE_AMOUNT`) | same |
| **Evidence required** | yes / no (for the client: «Required», «Not required» or «Same as the company») | same |

`ABOVE_AMOUNT` compares **|new total − previous total|** (with IVU and shipping) against the amount: it asks for approval if it is **strictly greater**, in the company's currency.

| What | Route | Permission |
|---|---|---|
| Company settings | `GET` and `PATCH /api/v1/sales-order-changes/settings` | `sales.view` or `admin.tenant` (see); `admin.tenant` (change) |
| A client's settings (`null` / `""` = inherits) | `GET` and `PATCH /api/v1/sales-order-changes/settings/clients/{clientPublicId}` | `sales.view` or `clients.read` (see); `clients.update` (change) |
| «To approve» inbox | `GET /api/v1/sales-order-changes/pending?skip=&take=&q=&sort=` | `sales.modify.approve` |

```json
{ "approvalMode": "ABOVE_AMOUNT", "approvalAmount": 500.00, "attachmentRequired": true }
```

The inbox is **paged on the server** (`skip`/`take`, up to 200 per page), with free search (order, client, who authorized, reason) and sort by `registeredAt`, `order`, `client` or `delta` (with `-` for descending).

**While the approval is pending:**
- **Nothing is applied:** reservations, picks, packs and totals are not touched. **The warehouse is not blocked** (it can dispatch).
- There is **a single pending one per order**. Another modification (or addition) gives 409 until it is approved, rejected or withdrawn.
- The order shows a «pending second approval» notice and the «Pending modification» tag.
- Cancelling the order **withdraws** the pending one («La orden se canceló»).

**Who decides.**
- **Approve:** `sales.modify.approve` and **another person than whoever registered** (403 if it is the same). On approval the **order fingerprint** is compared (status, quantities, prices, terms and picks): if it changed since the registration (it was dispatched, invoiced, packed or modified), **409**; it is rejected and another one is registered. In an **addition** the fingerprint is not compared; the parent must still be open and the addition is created on approval.
- **Reject:** `sales.modify.approve`, **the note is required**. Nothing is applied.
- **Withdraw:** whoever registered it or whoever has `sales.modify`. Nothing is applied.

## 10. Notices and data source

Four new events (module `SALES_ORDERS`, «Sales» group of Notification rules). **No rule is seeded**: the administrator builds them in System → Notification rules.

| Event | When | Event recipients | Can it be silenced? |
|---|---|---|---|
| **Sales order changed** (`SALES_ORDER_CHANGED`) | A modification was **applied** (on registering or on approving), **always**, not only with a pick or pack; it carries the warehouse impact | The order's seller, the warehouse assignee, and whoever registered it (when applied on approval) | Yes |
| **Addition to a sales order** (`SALES_ORDER_ADDITION_CREATED`) | The addition was created | The order's seller and the warehouse assignee | Yes |
| **Pack reopened** (`SALES_PACK_REOPENED`) | A pack was reopened (one per sales order served) | The warehouse assignee (the seller, available and off) | Yes |
| **Sales order change pending approval** (`SALES_CHANGE_APPROVAL_PENDING`) | A modification stayed pending second approval | Whoever the administrator puts in their rule (for example, the `sales.modify.approve` permission) | **No** |

- **The order's seller** = the seller frozen when it was requested (Batch 244), not the route's helpers. These notices **never** go to the client.
- The addition also triggers the «Order requested» notice of Batch 243 like any order. A modification of the original order does **not** send it again: it sends «Sales order changed».

**Data source.** `SALES_ORDER_CHANGE` («Sales order changes»), one row per modification or addition, with date field `RegisteredAtUtc`: `OrderNumber`, `ChangeNo`, `Kind`, `Status`, `ClientName`, `RegisteredByName`, `AuthorizedByName`, `AuthorizedByTitle`, `Channel`, `AuthorizedAtUtc`, `Reason`, `TotalBefore`, `TotalAfter`, `TotalDelta`, `LinesAdded`, `LinesRemoved`, `LinesChanged`, `HasPriceChange`, `HasTaxOverride`, `HasTermsChange`, `ReopenedPack`, `ReversedBatchCount`, `CreditOverride`, `RequiresApproval`, `DecidedByName`, `DecidedAtUtc` and `AdditionOrderNumber`. `SALES_ORDER` gains `IsAddition` and `ParentOrderNumber`; `SALES_ORDER_LINE` gains `IsRemoved`, `TaxReason` and `HasTaxOverride`.

## 11. Billing «when the user decides» (`ON_REQUEST`)

The billing basis for the company's **sales orders** now has three values: **`ON_DISPATCH`** (on dispatch, the default), **`ON_DELIVERY`** (on delivery) and **`ON_REQUEST`** (when the user decides, once the order is requested). **Transport** billing still follows its own per-contract setting (by pickup, by delivery or mixed).

With `ON_REQUEST`:
- What is billable is **what was ordered, minus what was returned and what was already invoiced**, without waiting for the dispatch. In a line with a closed backorder, what was dispatched plus what was reserved counts.
- Requested, partial or shipped orders that are **not waiting for the new client's approval** enter. Drafts and cancelled ones do not. The period is measured by the date the order was requested.
- What was invoiced can be **greater** than what was dispatched: the check «dispatched ≥ invoiced + returned» only holds with the other two bases.
- The guard against undoing picks (section 4) does not count what was invoiced.

It is changed with `PUT /api/v1/billing/settings` (field `salesBillingTrigger`), like the other bases. The Billing screen currently offers `ON_DISPATCH` and `ON_DELIVERY`.

## 12. Statuses and transitions

### The modification (domain `SalesOrderChangeStatus`)

Every transition goes through the status service and is left in the modification's status history.

| From → to | Who | What it validates | Effects |
|---|---|---|---|
| (nothing) → **Pending approval** (`PENDING_APPROVAL`) | `sales.modify` | Authorization, attachment, stage blockers, extra permissions, credit, a single pending one | Stored with the before snapshot and the projected after. If it does not ask for second approval, it goes to Applied in the same transaction |
| Pending → **Applied** (`APPLIED`) | Who registers (without second approval) or `sales.modify.approve` (another person) | Order fingerprint (409), current plan, credit, line invariants | Reopens packs, undoes the necessary picks, applies the operations, recalculates tax and shipping, moves the order's status, stores the real snapshot, totals and effects, sends «Sales order changed». In an addition it creates and requests the new order |
| Pending → **Rejected** (`REJECTED`) | `sales.modify.approve` | Required note | None (nothing was applied) |
| Pending → **Withdrawn** (`WITHDRAWN`) | Who registered, or `sales.modify`; or the system when the order is cancelled | It must be pending | None |

Applied, Rejected and Withdrawn are terminal. A modification already decided gives 409 «La modificación ya no está pendiente de aprobar.».

**What is blocked in each status of the modification:**
- **Pending approval:** no other modification or addition of that order is registered (409); the warehouse keeps working; nothing is applied.
- **Applied, Rejected, Withdrawn:** they are not decided again.

### The sales order

The modification **adds no status** to the order; it moves the usual ones (lateral PARTIAL ↔ REQUESTED and SHIPPED → PARTIAL or REQUESTED) as in section 3.

| Order status | Is it modified? |
|---|---|
| Draft (`DRAFT`) | No: 422, it is edited from its screen |
| Requested, partial, shipped | Yes, with the rules of section 3 |
| Cancelled | No (422) |
| With something invoiced, or that left complete | Only addition (422 if anything else is tried) |

### The pick

| From → to | Who | What it validates | Effects |
|---|---|---|---|
| **Packed → Collected** (reopen the pack) | `warehouse.reopen-pack` | Shipment active and in its initial stage; no freight, check, return fee or products invoiced; no linked company; reason | Voids the shipment (frees the order number and the EMP), removes shipment, pack date and client invoice from the pick, history «Empaque reabierto: …», «Pack reopened» notice. It only goes back **one** step of the pipeline |
| **Collected → (undone)** | `sales.modify` (through the modification) or `warehouse.pick` (by hand) | Global billing guard (409) | The inventory returns to its bin, it stays in the Kardex, the line is reserved again, a master left empty is deactivated |

## 13. Validations and messages

The message comes as-is in the error's `title` field (and on the screen). **Messages are in Spanish; the English meaning is in the last column.** The screen translates the known ones into the interface language. A 400 error also carries the field in `errors`.

### Authorization and attachment (`POST …/changes`, `POST …/additions`)

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `authorizedByName` | Required | `Indique quién autorizó el cambio en el cliente.` | 400 | Enter who authorized the change at the client |
| `authorizedByName` | Up to 150 | `El nombre de quien autorizó no puede pasar de 150 caracteres.` | 400 | The name cannot exceed 150 characters |
| `authorizedByTitle` | Up to 100 | `El cargo de quien autorizó no puede pasar de 100 caracteres.` | 400 | The title cannot exceed 100 characters |
| `authorizationChannel` | Required | `Indique cómo autorizó el cliente (por teléfono, por correo, en persona o por mensaje).` | 400 | Enter how the client authorized |
| `authorizationChannel` | `PHONE`, `EMAIL`, `IN_PERSON` or `MESSAGE` | `El canal de autorización no es válido: use PHONE, EMAIL, IN_PERSON o MESSAGE.` | 400 | The channel is not valid |
| `authorizedAtUtc` | Not in the future | `La fecha de la autorización no puede ser futura.` | 400 | The authorization date cannot be in the future |
| `authorizedAtUtc` | Not earlier than the order | `La fecha de la autorización no puede ser anterior a la orden.` | 400 | The authorization date cannot be earlier than the order |
| `reason` | Required | `Indique el motivo de la modificación.` | 400 | Enter the reason for the modification |
| `reason` | Up to 500 | `El motivo de la modificación no puede pasar de 500 caracteres.` | 400 | The reason cannot exceed 500 characters |
| `attachmentFilePublicId` | Required if the client or company require it | `Adjunte la evidencia de la autorización del cliente (captura del correo o del mensaje): es obligatoria para este cliente.` | 400 | Attach the evidence (it is required for this client) |
| `file` (`change-attachments`) | Not empty | `Adjunte el archivo de la evidencia (imagen o PDF).` | 400 | Attach the evidence file (image or PDF) |
| `attachmentFilePublicId` | A file backs a single modification | `Ese archivo ya respalda otra modificación: adjunte otro.` | 409 | That file already backs another modification |
| `attachmentFilePublicId` | It is of the order (or of its parent or root, in an addition) | `Archivo no encontrado.` | 404 | File not found |
| `authorizedByContactId` | It is an active contact of the order's client | `Contacto no encontrado.` | 404 | Contact not found |
| `reopenReason` | Up to 500 | `El motivo admite como máximo 500 caracteres.` | 400 | The reason admits 500 characters at most |

The file's size and type are validated by the file store (see the [FAQ](faq.md)).

### Operations (`ops`)

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `ops` | At least one change (and that it changes something) | `Indique al menos un cambio.` | 400 | Enter at least one change |
| `ops[].kind` | `ADD`, `QTY`, `REMOVE`, `PRICE`, `TAX`, `CLEAR_TAX`, `TERMS` or `NOTES` | `Operación no válida: use ADD, QTY, REMOVE, PRICE, TAX, CLEAR_TAX, TERMS o NOTES.` | 400 | Invalid operation |
| `quantity` | Greater than zero | `La cantidad debe ser mayor que cero.` | 400 | The quantity must be greater than zero |
| `quantity` | Up to 3 decimals | `La cantidad admite como máximo 3 decimales.` | 400 | At most 3 decimals |
| `quantity` | Within the maximum | `La cantidad excede el máximo permitido.` | 400 | The quantity exceeds the maximum |
| `unitPrice` | From 0 up | `El precio no es válido.` | 400 | The price is not valid |
| `unitPrice` | Up to 4 decimals | `El precio admite como máximo 4 decimales.` | 400 | At most 4 decimals |
| `unitPrice` (`PRICE`) | Required | `Indique el precio nuevo de la línea.` | 400 | Enter the line's new price |
| `taxPct` | Required | `Indique el porcentaje de IVU de la línea.` | 400 | Enter the line's IVU percentage |
| `taxPct` | From 0 to 100 | `El IVU de la línea debe estar entre 0 y 100 %.` | 400 | IVU between 0 and 100 % |
| `taxPct` | Up to 4 decimals | `El IVU de la línea admite hasta 4 decimales.` | 400 | Up to 4 decimals |
| `note` (`TAX`) | Required | `Indique el motivo del IVU manual.` | 400 | Enter the reason for the manual IVU |
| `note` (`TAX`) | Up to 250 | `El motivo del IVU manual no puede pasar de 250 caracteres.` | 400 | The reason cannot exceed 250 characters |
| `terms` (`TERMS`) | At least one term | `Indique los términos que cambian (forma de pago, entrega o consignatario).` | 400 | Enter the terms that change |
| `notes` | Up to 500 | `Las notas no pueden pasar de 500 caracteres.` | 400 | Notes cannot exceed 500 characters |
| `rowVersion` | Base64 value from `edit-options` | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 | Invalid rowVersion |
| `rowVersion` | The order did not change since it was read | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 | The record was modified by another user |
| Product | It is of the company | `Producto no encontrado.` | 404 | Product not found |
| Order | It is of the company | `Orden de venta no encontrada.` | 404 | Sales order not found |
| Modification | It is of that order | `Modificación no encontrada.` | 404 | Modification not found |

### Extra permissions (403)

| When | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|
| Price different from today's without `sales.price` | `Cambiar el precio exige el permiso de precios (sales.price).` | 403 | Changing the price requires the price permission |
| Manual IVU without `sales.tax` | `Falta el permiso 'sales.tax'.` | 403 | Missing permission |
| Reopening the pack without `warehouse.reopen-pack` | `Reabrir el empaque exige el permiso warehouse.reopen-pack.` | 403 | Reopening the pack requires the permission |
| Exceeded credit with `overrideCredit` without the permission | `Falta el permiso 'orders.credit_override'.` | 403 | Missing permission |
| Any route without its permission | `Falta el permiso '…'.` | 403 | Missing permission |
| Approving a modification you registered | `La segunda aprobación la da otra persona: quien registró la modificación no puede aprobarla.` | 403 | The second approval is given by another person |
| Withdrawing without having registered it or having `sales.modify` | `Solo quien registró la modificación o quien puede modificar órdenes la retira.` | 403 | Only who registered it or who can modify orders withdraws it |

### Stage blockers (422)

| When | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|
| The order has something invoiced | `La orden ya está facturada: solo se le puede añadir. Quitar o bajar se hace con una nota de crédito.` | 422 | Already invoiced: it can only be added to |
| The order left complete | `La orden ya salió completa: solo se le puede añadir con una orden interna de adición.` | 422 | Already left complete: only an internal addition order |
| Draft | `La orden está en borrador: se cambia desde su edición, no con una modificación.` | 422 | A draft is changed from its edit screen |
| Cancelled | `La orden está cancelada: no se puede modificar.` | 422 | A cancelled order cannot be modified |
| Removing all lines | `No se pueden quitar todas las líneas: para eso cancele la orden.` | 422 | Cancel the order instead |
| Terms with part already left | `Ya salió parte de la orden: el pago, la entrega y el consignatario ya no se cambian.` | 422 | Part left: payment, delivery and consignee no longer change |
| Lowering below what left | `No se puede bajar {sku} a {cantidad}: ya salieron {salido}.` | 422 | Cannot lower the SKU: that much already left |
| Removing a line with something left | `No se puede quitar {sku}: ya salieron {salido}.` | 422 | Cannot remove the SKU: that much already left |
| Price of a line with something left | `El precio de {sku} ya no se cambia: parte de la línea ya salió.` | 422 | The price no longer changes |
| IVU of a line with something left | `El IVU de {sku} ya no se cambia: parte de la línea ya salió.` | 422 | The IVU no longer changes |
| Line in a pack without checking reopen | `{sku} está en el empaque {EMP}: para cambiarla marque «Reabrir el empaque» (permiso warehouse.reopen-pack).` | 422 | The SKU is in the pack: check «Reopen the pack» |
| Terms with a pack without checking reopen | `La orden está en el empaque {EMP}: para cambiar el pago, la entrega o el consignatario marque «Reabrir el empaque» (permiso warehouse.reopen-pack).` | 422 | The order is in the pack: check «Reopen the pack» |
| Removed line | `La línea {n} ({sku}) está quitada: para volver a pedirla, agréguela de nuevo.` | 422 | The line is removed: add it again |
| Line closed with a shortage | `La línea {n} ({sku}) está cerrada: su cantidad ya no se cambia.` | 422 | The line is closed: its quantity no longer changes |
| Line repeated in the modification | `La línea {n} ({sku}) viene más de una vez en la modificación.` | 422 | The line comes more than once |
| Line of another order | `La línea {id} no es de esta orden.` | 422 | The line is not of this order |
| Missing the operation's line | `Indique la línea que se cambia.` | 422 | Enter the line |
| Product already in the order | `El producto {sku} ya está en la orden: cambie la cantidad de su línea.` | 422 | The product is already in the order |
| Missing the `ADD` product | `Indique el producto que se agrega.` | 422 | Enter the product |
| Kit with a non-whole quantity | `El kit {sku} se pide en piezas enteras.` | 422 | The kit is ordered in whole pieces |
| `CLEAR_TAX` on a line without manual IVU | `La línea {n} ({sku}) no tiene IVU manual.` | 422 | The line has no manual IVU |
| `TERMS` or `NOTES` repeated | `Los términos vienen dos veces en la modificación.` / `Las notas vienen dos veces en la modificación.` | 422 | Terms / notes come twice |
| Manual IVU with `TAX` off | `El IVU manual exige el módulo de impuestos encendido.` | 422 | Manual IVU requires the tax module |
| The change leaves a line incoherent | `La modificación dejaría la orden incoherente (línea {n}): no se aplicó.` | 422 | The modification would leave the order incoherent: not applied |
| Credit exceeded | `El cliente excede su límite de crédito: límite …, en curso …, esta orden ….` (without figures for whoever lacks `orders.view_price`) | 422 | The client exceeds their credit limit |
| Addition to an order that is not in progress | `Solo se le puede añadir a una orden solicitada, parcial o despachada.` | 422 | Only requested, partial or shipped orders |
| Addition without products | `Indique los productos de la adición.` | 400 | Enter the addition's products |
| Cancelling an order with open additions | `La orden tiene adiciones abiertas ({OV-…}): cancélelas o despáchelas antes de cancelar esta.` | 422 | The order has open additions: cancel or ship them first |

### Conflicts (409)

| When | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|
| Another modification while one is pending | `La orden ya tiene una modificación por aprobar (n.º {n}): apruébela, recházela o retírela antes de registrar otra.` | 409 | The order already has a modification to approve |
| Between the preview and saving the picks changed | `La orden cambió mientras se modificaba; vuelva a intentar.` | 409 | The order changed while being modified; try again |
| The order changed at the same time | `La orden cambió al mismo tiempo. Recárguela e intente de nuevo.` | 409 | The order changed at the same time; reload |
| On approval, the order is no longer the one that was modified | `La orden cambió desde que se registró la modificación (se despachó, se facturó, se empacó o se modificó): recházela y registre otra.` | 409 | The order changed since the modification was registered: reject and register another |
| The modification was already decided | `La modificación ya no está pendiente de aprobar.` | 409 | No longer pending approval |
| Undoing an already invoiced pick | `La recolección {PK} ya está facturada ({FAC-…}): no se puede deshacer.` | 409 | The pick is already invoiced: it cannot be undone |
| Inventory short when raising or adding | The message of the first shortage (`insufficient_stock`) | 409 | Not enough inventory |

### Decision and settings

| When | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|
| Rejecting without a note | `Indique por qué se rechaza la modificación.` | 400 | Enter why it is rejected |
| Decision note too long | `La nota de la decisión no puede pasar de 500 caracteres.` | 400 | The note cannot exceed 500 characters |
| Second-approval mode | `El modo de segunda aprobación debe ser NONE, ALWAYS o ABOVE_AMOUNT.` | 400 | The mode must be NONE, ALWAYS or ABOVE_AMOUNT |
| `ABOVE_AMOUNT` without an amount | `Con «Arriba de un monto» (ABOVE_AMOUNT) indique un monto mayor que 0.` | 400 | Enter an amount greater than 0 |
| Amount with more than 2 decimals | `El monto de la segunda aprobación admite hasta 2 decimales.` | 400 | Up to 2 decimals |
| Invalid billing basis (`PUT /billing/settings`) | `La base de facturación debe ser ON_DISPATCH (al despachar), ON_DELIVERY (al entregar) u ON_REQUEST (cuando el usuario lo decida, ya solicitada la orden).` | 400 | The basis must be ON_DISPATCH, ON_DELIVERY or ON_REQUEST |

### Reopening the pack and joining the shipment (`/api/v1/pick-batches`)

| When | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|
| Reopening without a reason | `Indique el motivo para reabrir el empaque.` | 400 | Enter the reason to reopen the pack |
| Reason too long | `El motivo admite como máximo 500 caracteres.` | 400 | 500 characters at most |
| Not packed (or manual dispatch) | `La recolección {PK} no está empacada.` | 422 | The pick is not packed |
| It belongs to a master | `La recolección {PK} está en la recolección maestra {PK-M}: reabra el empaque desde la maestra.` | 422 | Reopen from the master |
| Sent to the linked company | `La recolección {PK} se mandó a la compañía enlazada: el empaque no se reabre.` | 422 | The pack is not reopened |
| Shipment already advanced, voided or missing | `El envío {ORD} ya está «{estatus}»: el empaque {PK} no se puede reabrir.` | 422 | The shipment is already in that status: the pack cannot be reopened |
| Freight, check or return fee invoiced | `El flete del envío {ORD} ya está facturado: el empaque {PK} no se reabre.` | 422 | The freight is already invoiced |
| Client invoice already processed | `La factura del cliente del envío {ORD} ya se procesó: el empaque {PK} no se reabre.` | 422 | The client's invoice was already processed |
| Products already invoiced | `El empaque {PK} tiene productos ya facturados ({FAC-…}): no se reabre. Para añadir, use una orden interna de adición.` | 422 | The pack has invoiced products: use an addition order |
| Join: not of the order's family | `La recolección {PK} no es de la misma orden ni de una adición de ella: no se une a ese envío.` | 422 | Not of the same order nor of an addition of it |
| Join: not collected | `La recolección {PK} no está recolectada (está «{estatus}»).` | 422 | The pick is not collected |
| Join: already in a master | `La recolección {PK} ya está en la maestra {PK-M}.` | 422 | Already in the master |
| Join: several destinations | `Escoja con qué recolección de {OV} sale (hay varias).` | 400 | Choose which pick it leaves with |
| Join: the destination already left | `El envío de {PK} ya salió («{estatus}»): la recolección sale en su propio envío.` | 422 | The shipment already left: it leaves in its own |
| Join: the destination is packed and reopen was not checked | `{PK} ya está empacada: marque «Reabrir el empaque» (permiso warehouse.reopen-pack) para unir esta recolección a su envío.` | 422 | Already packed: check «Reopen the pack» |
| Join: no other pick | `No hay otra recolección de {OV} con la que pueda salir: empaque esta por separado.` | 422 | No other pick: pack it separately |
| Join: with itself | `Escoja otra recolección: {PK} no se une consigo misma.` | 400 | Choose another pick |
| Join: sent to the linked company | `La recolección {PK} se mandó a la compañía enlazada: no se une a otro envío.` | 422 | Not joined to another shipment |
| Join: another client | `Los documentos deben ser del mismo cliente: «…» y «…» son de clientes distintos. Haga recolecciones separadas.` | 400 | Documents must be of the same client |
| Join: another warehouse | `Los documentos deben ser del mismo almacén: «…» y «…» son de almacenes distintos.` | 400 | Documents must be of the same warehouse |

## 14. Frequent questions of the module

**Why can't I modify the order?**
Read the message. If it says it is **invoiced** or **already left complete**, it can only be added to: use «Internal addition order». If it is a draft, edit it from its screen. If it is a missing permission, ask for `sales.modify` (the Sales role no longer has it).

**I want to lower the quantity and it says «está en el empaque …».**
The goods are already packed. Check **«Reopen the pack»** (needs `warehouse.reopen-pack`, which by default only the administrator has). The shipment is voided and the pick goes back to «Collected»; then it has to be packed again. Without the permission, what you want to add can go in an internal addition order.

**I lowered the quantity and the warehouse says a pick was undone.**
It is the minimal reversal: only the necessary pick, the most recent, was undone. The inventory went back to its bin and the line was reserved; the warehouse must dispatch again. The «Sales order changed» notice says so.

**It says «La recolección … ya está facturada (…): no se puede deshacer.»**
With the «on dispatch» or «on delivery» billing basis, those goods are already on an issued invoice and the invoice is not reopened. Resolve it with a **return** or a credit note. With the «when the user decides» basis it does not happen.

**The modification stayed «pending approval» and the order did not change.**
The company or the client asks for a second approval. Another person with `sales.modify.approve` approves it from **To approve**. Meanwhile nothing was applied and the warehouse can keep working. If the order changed (for example, it was dispatched) before the approval, the system gives 409: reject it and register another.

**It does not let me approve my own modification.**
It is on purpose: the second approval is given by another person.

**I set a manual IVU and the other lines are still exempt. Is it a bug?**
No. Manual IVU only affects that line; the client's exemption keeps applying to the others. With «Remove manual IVU» the line returns to its normal calculation, with the exemption if the document is still valid.

**What is the difference between modifying and the internal addition order?**
Modifying changes the order that already exists (it is applied at once or after the second approval). The addition creates a new order, linked to the original, for what can no longer be changed. It is invoiced with its own runs and its invoice says «complementa FAC-…» when the original was already invoiced.

**The addition left in a separate shipment.**
Use «Join the shipment of OV-…» on its pick (only while the original is in the initial stage). If the original was packed, its pack has to be reopened (`warehouse.reopen-pack`); a pack with already invoiced products is not reopened and the addition leaves in its own shipment.

**I cannot cancel the order.**
It has open additions: cancel or ship them first. A modification pending approval is withdrawn on its own when cancelling.

**The client requires evidence and I do not have it.**
If the setting is «Required» (client or company), without a file the modification is not registered (400). Change the client's setting to «Not required» or «Same as the company» if appropriate (`clients.update`).

**I do not see «To approve» or «Modify order».**
The «To approve» tab needs `sales.modify.approve`; the «Modify order» button needs `sales.modify`.

**I wanted to change the price of a product with a list price and it does not let me.**
Typing a price different from today's requires `sales.price`. Whoever has it can change the price of any line.

## 15. For support

- **Tables:** `SalesOrderChange` (the modification: `ChangeNo` unique per order, `Kind` `MODIFY` or `ADDITION`, `IsPending`, `BeforeJson`, `AfterJson`, `RequestJson`, `EffectsJson`, `BaseFingerprint`, `AdditionSalesOrderId`); `SalesOrder.ParentSalesOrderId` (the parent of an addition; `FK_SalesOrder_Parent` is composite with the company); `SalesOrderLine` (`TaxReason`, `TaxOverrideNote`, `TaxComputedRatePct`, `TaxComputedAmount`, `TaxOverrideBy`, `TaxOverrideAtUtc`); `Tenant` and `Client` (`SalesChangeApprovalMode`, `SalesChangeApprovalAmount`, `SalesChangeAttachmentRequired`; in `Client`, `NULL` = inherits).
- **Indexes that protect:** `UX_SalesOrderChange_Pending` (a single pending one per order, guaranteed in the database) and `UX_SalesOrderChange_Attachment` (a file backs a single modification). A race between two people ends in the 409 above.
- **Removed line:** `QtyOrdered = 0` and `IsClosed = 1` (never deleted). Adding the product again reactivates it with the same number; line numbers can have gaps.
- **«Who authorized this change?»:** `GET …/changes/{id}` (or the order's «Modifications» panel): name, title, contact, channel, date, reason, evidence, before and after. The audit log records `SALES_ORDER_CHANGE`; the status history, the modification's transitions.
- **Authorized credit:** the modification carries the `CreditOverride` flag and a `credit_override` security event is left with the order and the modification.
- **Reopening:** the pick's status history says «Empaque reabierto: {reason} (envío {ORD} anulado)»; the shipment is left with `IsActive = 0`.
- **Notices:** in `NotificationOutbox`, the keys are `CHG-{modification}`, `APPROVAL-{modification}`, `ADD-{modification}` and `REOPEN-{pick or master}-{shipment}`.
- **Billing of an addition:** the invoice line description and the IIF MEMO carry «adición de OV-… · complementa FAC-…».
- **Billing basis:** `Tenant.SalesBillingTrigger` (`ON_DISPATCH`, `ON_DELIVERY`, `ON_REQUEST`).
- Without permission, or with the module off, the 403s leave a `PERMISSION_DENIED` security event.
