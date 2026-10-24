# 18 — Sales orders

Batches 57 to 64 (2026-10-15) — **S1: draft, request and reserve, backorders; S2: dispatch**. A **sales order** (OV-#####) is the document that travels from **Sales to Warehouse**: Sales asks the warehouse to dispatch something for a client. So far you can **build the order as a draft, request it (validate and reserve inventory), modify it and cancel it**; view, fill and close **backorders**; **dispatch against the order**, pickup / delivery and undo a dispatch; the web and the app come in the following batches.
The full design is in `Diseño/logistica-funcionalidades-maestro.md`, section 13C. The screen is described in [L60 — Sales orders](frontend/l60-ordenes-de-venta.md) and [L63 — Dispatch](frontend/l63-despachar-orden-de-venta.md).

## 1. Who can

- Module **Sales orders (`SALES_ORDERS`)**: it comes **off**; it is turned on per company (System → Modules). With the module off, the whole API answers 403.
- Permissions (Orders category):
  - **`sales.view`** — view orders. Held by the Company admin, the **Sales** role and the Warehouse operator.
  - **`sales.create`** — create and edit the draft (and, in the next batch, request). Company admin and Sales role.
  - **`sales.cancel`** — cancel. Company admin and Sales role.
  - **`sales.price`** — write a price other than the product's sale price. **Only the Company admin**; the administrator assigns it to whoever they want.
  - **`sales.backorder`** — fill and close backorders (following batches). Company admin and Warehouse operator.
- The **Sales** role template (`sales.view`, `sales.create`, `sales.cancel`, `inventory.view`, `clients.read`) is created by the system for new companies; in existing ones the administrator creates the role and assigns those permissions to it.

## 2. The order

- **Number** `OV-#####`, unique per company, consecutive and without gaps (a failed attempt does not use up a number).
- **Client** (required): the buyer. **Consignee** (optional): only if there will be a delivery. **Warehouse**: the one that serves it; if the company has a single active warehouse it is used on its own, if it has several it must be specified.
- **Request date** (today, in the company's time zone), **required date** (optional), **notes** (up to 500 characters) and **who requested it**.
- **Lines**: product, quantity and price. Only **the company's own products** are sold (not those of a pass-through client), active, **without repeating** in the same order. Up to 200 lines.
- **Price**: if none is written, the **product's sale price** is taken. Writing a different price requires `sales.price` (if it equals the list price it is not needed). A product **without a sale price** forces you to write the price.
- **Tax**: with the **Taxes** module on, each line computes its tax with the company's rules (chapter [17](17-impuestos.md)) and **keeps a copy**: the rate used, its percentage, the amount and whether it came out exempt (exempt product or client with a current exemption). The header stores the **subtotal** and the **tax**; the **total** is their sum. Without the Taxes module the tax is zero. If the draft's client is changed, the tax is recalculated.
- **Status**: `DRAFT` → `REQUESTED` → `PARTIAL` → `SHIPPED`; `CANCELLED`. In use so far: draft, requested and cancelled. The **with backorder** flag is not a status: the order has it while any line has a pending quantity.

## 3. What can be done

| Action | How | Permission |
|---|---|---|
| View the list (most recent first; filters: status, client, search box by number / client / product, only with backorder, dates) | `GET /api/v1/sales-orders` | `sales.view` |
| View an order | `GET /api/v1/sales-orders/{publicId}` | `sales.view` |
| Create the draft | `POST /api/v1/sales-orders` | `sales.create` (+ `sales.price` if a price changes) |
| Edit the draft (what is not sent does not change; the **lines, if sent, replace** the current ones; `clearConsignee` / `clearRequiredDate` remove those values) | `PATCH /api/v1/sales-orders/{publicId}` | `sales.create` |
| Check availability and credit (does not save) | `GET /api/v1/sales-orders/{publicId}/availability` | `sales.create` |
| Request (validate and reserve) | `POST /api/v1/sales-orders/{publicId}/request` | `sales.create` |
| Modify a requested line | `PATCH /api/v1/sales-orders/{publicId}/lines/{lineId}` | `sales.create` |
| View backorders (oldest first) | `GET /api/v1/sales-orders/backorders` | `sales.view` |
| Fill a line / fill all / close with shortage | `POST …/lines/{lineId}/fulfill` · `POST /sales-orders/backorders/fulfill` · `POST …/lines/{lineId}/close` | `sales.backorder` |
| Dispatch against the order (in parts; consumes the reservation and lowers inventory) | `POST /api/v1/sales-orders/{publicId}/dispatch` | `warehouse.pick` |
| Create the delivery of a dispatch (transport order) | `POST /api/v1/sales-orders/{publicId}/delivery` | `warehouse.pick` |
| Cancel a draft or a requested order (optional comment in the history; releases what was reserved) | `POST /api/v1/sales-orders/{publicId}/cancel` | `sales.cancel` |

Orders are **never deleted**: they are cancelled. They stay in the log (creations, changes) and in the status history.

## 4. Validations and messages

| Situation | Exact message | HTTP |
|---|---|---|
| No client | `Choose the buying client.` | 400 (`clientPublicId`) |
| Client does not exist (or belongs to another company) | `Client not found.` | 404 |
| Inactive client | `The client is inactive; nothing can be sold to it.` | 422 |
| Consignee does not exist | `Consignee not found.` | 404 |
| Several warehouses and none specified | `Specify the warehouse: the company has more than one.` | 400 (`warehousePublicId`) |
| Warehouse does not exist / inactive / the company has none | `Warehouse not found.` / `The warehouse is inactive.` / `The company has no active warehouses.` | 404 / 422 / 422 |
| No lines | `Add at least one product to the order.` | 400 (`lines`) |
| More than 200 lines | `The order allows at most 200 lines.` | 400 |
| Line without product | `Choose the product of each line.` | 400 |
| Product does not exist | `Product not found.` | 404 |
| Inactive product | `There is an inactive product in the order.` | 422 |
| Product of a pass-through client | `Only the company's own products can be sold.` | 422 |
| Repeated product | `A product cannot be repeated in the order.` | 400 |
| Empty or zero quantity | `The quantity must be greater than zero.` | 400 |
| More than 3 decimals / too large | `The quantity allows at most 3 decimals.` / `The quantity exceeds the allowed maximum.` | 400 |
| Negative or invalid price / more than 4 decimals | `The price is not valid.` / `The price allows at most 4 decimals.` | 400 |
| Product without a sale price | `Product {SKU} has no sale price: enter the price.` | 400 |
| Price other than the list price without `sales.price` | `Changing the price requires the pricing permission (sales.price).` | 403 |
| Lot does not exist | `Lot not found.` | 404 |
| Notes longer than 500 characters | `Notes cannot exceed 500 characters.` | 400 |
| Order does not exist (or belongs to another company) | `Sales order not found.` | 404 |
| Editing an order that is no longer a draft | `Only a draft order can be edited.` | 422 |
| Cancelling an already cancelled order | `The order is already cancelled.` | 409 |
| With Taxes on and no default rate / no current rate | `Choose the default rate in Taxes → Rules.` / `There is no tax rate in force on that date. Set one up in Taxes → Rates.` | 400 |
| Without the permission or with the module off | “No permission” | 403 |

## 6. Requesting the order: validate and reserve (Batch 58)

`POST /api/v1/sales-orders/{publicId}/request` (permission `sales.create`) moves the order from **draft** to **requested** (`REQUESTED`): the warehouse now sees it and **the inventory is reserved for it**. Validation and reservation happen **in the same transaction, under the inventory lock**: two salespeople requesting the same thing at the same time cannot sell more than there is (one gets it, the other receives the shortage notice). More than what exists is never reserved.

- **Available to sell** = what is on hand minus what is already reserved, in the **active** bins of the order's warehouse (and of the **lot**, if the line asked for one). It **does not count** quarantine, cross-dock or rented units, nor other warehouses. It is the same thing the warehouse can pick; it differs from the “Available” in the inventory table, which does add quarantine.
- **Which lot and which bin**: the system reserves following the company's rotation order (first expired / first in) and stores **exactly** which balance each line reserved (`SalesOrderReservation`) so it can release exactly that later.
- **Check first**: `GET /sales-orders/{publicId}/availability` (saves nothing) returns per line **ordered / available / short** and the **credit** status. The screen uses it to show the shortage dialog.
- **If inventory is short**: the request answers **409** with the message per line and **reserves nothing** (not even for the lines that did have enough). Sales decides: **order only what is available** (edit the quantity), **reserve what is available and leave the rest on backorder** (`allowBackorder = true`) or **remove the line**. With `allowBackorder` each line reserves what there is and the pending part stays on **backorder**; the order is flagged **with backorder** while any line has a pending quantity.
- **Credit**: the client's **credit limit** is compared with what it already has in progress (outstanding balance of its transport orders **plus** what is requested and not dispatched in its other sales orders) and this order. If it exceeds it, the response is **422** `credit_exceeded` with the notice and the figures; with `overrideCredit = true` and the `orders.credit_override` permission it is authorized: the comment “Credit exceeded authorized by …” stays in the status history and an event in the security log.
- A product **with a serial number** cannot be sold with a sales order yet (message in the table).

## 7. Cancelling and modifying a requested order (Batch 58)

- **Cancel** (`POST /sales-orders/{publicId}/cancel`, permission `sales.cancel`): a requested order **releases exactly what it had reserved** and deletes its backorder; the inventory becomes available again for other sales. An order that already had something dispatched cannot be cancelled.
- **Modify a line** (`PATCH /sales-orders/{publicId}/lines/{lineId}`, permission `sales.create`; price other than the list price, also `sales.price`): the **quantity** and the **price** of a line of a requested order can be changed as long as **nothing from that line has been dispatched**.
  - **Raising** the quantity reserves whatever there is; if it is not enough it answers 409 (or, with `allowBackorder`, the rest stays on backorder).
  - **Lowering** the quantity takes first from the backorder and then releases reservation.
  - The tax and the order totals are recalculated.

## 8. New messages (Batch 58)

| Situation | Exact message | HTTP |
|---|---|---|
| Requesting or checking an order that is not a draft | `Only a draft order can be requested.` | 422 |
| Inventory short when requesting | `Not enough inventory of {SKU}: ordered {n}, available {n}, short {n}.` | 409 (`insufficient_stock`, one message per line) |
| Credit exceeded | `The client exceeds its credit limit: limit {n}, in progress {n}, this order {n}.` | 422 (`credit_exceeded`) |
| Authorizing credit without the permission | `Missing permission 'orders.credit_override'.` | 403 |
| Modifying a line of an order that is not requested | `Only the lines of a requested order can be modified.` | 422 |
| Line does not exist | `Line not found.` | 404 |
| Closed line | `The line is closed.` | 422 |
| Product already dispatched from the line | `Product has already been dispatched from that line: it cannot be modified.` | 422 |
| Raising a quantity that is not available | `Not enough inventory of {SKU} to raise the quantity: adding {n}, available {n}, short {n}.` | 409 |
| Invalid quantity or price when modifying | `The quantity must be greater than zero.` / `The price is not valid.` | 400 |
| Price other than the list price without `sales.price` | `Changing the price requires the pricing permission (sales.price).` | 403 |
| Serialized product in a sales order | `Products with a serial number cannot be sold with a sales order yet.` | 422 |
| Cancelling an order with something dispatched | `Only a draft or requested order with nothing dispatched can be cancelled.` | 422 |

## 9. Backorders: view, fill and close with shortage (Batch 59)

A **backorder** is what was ordered and is not there yet: the line keeps its pending quantity (`QtyBackordered`) while the order remains requested.

- **View the backorders** (`GET /api/v1/sales-orders/backorders`, permission `sales.view`): all the lines with a pending quantity, **the oldest first** (first to order, first served). Per line: order, client, warehouse, product, **ordered / reserved / pending**, what is **available today** to sell and what could be **filled now** (the lesser of pending and available), and how many days it has been waiting. Search box by shipment number, client or product.
- **Fill a line** (`POST /api/v1/sales-orders/{publicId}/lines/{lineId}/fulfill`, permission `sales.backorder`): reserves for the line **whatever there is** (all of its pending quantity or part of it). It uses the same inventory lock as requesting: it never reserves more than what exists nor takes from another order. If there is nothing new, it answers 409.
- **Fill all** (`POST /api/v1/sales-orders/backorders/fulfill`, permission `sales.backorder`): goes through the backorders **from the oldest to the most recent** and fills what it can; what one line takes, the next one no longer sees. It returns what was reserved per line and how much is still pending.
- **When it can be filled**: when inventory comes in (receipt, inbound adjustment, transfer, releasing quarantine) or another order that had it reserved is cancelled. Today the automatic notice “there is stock for this backorder now” does not exist yet (it comes with the sales notices); the view always shows what can be filled.
- **Close with shortage** (`POST /api/v1/sales-orders/{publicId}/lines/{lineId}/close`, permission `sales.backorder`): marks the line as **finished without waiting any longer**: its backorder is deleted and the line is closed. **What was already reserved stays reserved** to be dispatched and the **ordered quantity is kept** (it is used to measure fulfillment). A closed line is neither filled nor modified.
- **Cancelling the shipment** deletes its backorder and releases what was reserved (section 7).

| Situation | Exact message | HTTP |
|---|---|---|
| Filling a line with no available inventory | `There is no inventory available to fill that line.` | 409 |
| Filling or closing a line without backorder | `That line has no backorder.` | 409 |
| Filling or closing in an order that is not requested | `Only the lines of a requested order can be modified.` | 422 |
| Filling or closing a closed line | `The line is closed.` | 422 |
| Line or order does not exist | `Line not found.` / `Sales order not found.` | 404 |

## 10. Dispatching against the order (Batch 61)

`POST /api/v1/sales-orders/{publicId}/dispatch` (permission `warehouse.pick`, the same as picking) takes what is reserved for the order out of the warehouse. **Dispatching always lowers inventory**, whether it is delivered or picked up. Everything happens in **a single transaction**:

1. The **reservation is consumed** for the dispatched lines.
2. The inventory **leaves through the ledger** (outbound movement with the pick batch as reference: it stays in the movement ledger).
3. The **pick batch** (`EMP-#####`) is created **linked to the order** (`PickBatch.SalesOrderId` and, per line, `PickBatchLine.SalesOrderLineId`), with the product cost frozen.
4. The **dispatched** quantity of each line goes up and the reserved quantity goes down.
5. The order moves to **partially shipped** (`PARTIAL`) or **shipped** (`SHIPPED`).

- **In parts**: several lines and less quantity than what is reserved can be sent; each dispatch creates its own pick batch. **More than what is reserved is never dispatched.**
- **Guided bin and lot, with changes allowed**: without `binId` it comes out of what the system reserved. If the operator took from **another bin** (`binId`, and `lotId` if there are lots), the reservation **moves** to that bin —if it has sellable balance— and it comes out of there; without balance it answers 409 with the inventory notice. The bin must belong to the order's warehouse, be active and sellable (no quarantine, cross-dock or rental).
- **When a line and the order are finished**: a line is **finished** when it has no reservation or backorder left. The order is `SHIPPED` when all its lines are finished and something was dispatched; with a pending backorder it stays in `PARTIAL` until it is filled or **closed with shortage** (closing the last pending line moves it to shipped).
- **Order with nothing dispatched and all lines closed with shortage**: **it cancels itself**, with the note “All lines were closed with shortage and nothing was dispatched.” in the history.
- Pickup, **delivery** (creates the transport order) and undoing a dispatch are described in section 11.

| Situation | Exact message | HTTP |
|---|---|---|
| No lines | `Specify which lines are dispatched.` | 400 |
| Unidentified line | `Specify the line being dispatched.` | 400 |
| Repeated line | `A line cannot be repeated in the dispatch.` | 400 |
| Empty or zero quantity | `The quantity must be greater than zero.` | 400 |
| Order that is neither requested nor partially shipped | `Only a requested or partially shipped order can be dispatched.` | 422 |
| More than what is reserved | `You cannot dispatch more than what is reserved: {SKU} has {n} reserved.` | 409 |
| Line or order does not exist | `Line not found.` / `Sales order not found.` | 404 |
| Bin does not exist or belongs to another warehouse | `Bin not found.` | 404 |
| Inactive bin or quarantine / cross-dock / rental bin | `Bin {code} is not sellable: it is inactive or it is a quarantine, cross-dock or rental bin.` | 422 |
| No balance in the chosen bin | `Insufficient inventory: …` (the inventory one) | 409 |
| Without the `warehouse.pick` permission | “No permission” | 403 |

## 11. Delivery and undoing a dispatch (Batch 62)

- **What happens after dispatching**: the dispatch remains as a **pick batch** (`EMP-#####`) linked to the order and the order shows its list of **dispatches** (`dispatches`): number, status (`COLLECTED` = dispatched without delivery, `PACKED` = with delivery, `CANCELLED` = undone), quantity and, if it already has one, the **transport order** of the delivery.
  - **Pickup** (the client collects): nothing else is needed; the dispatch stays as `COLLECTED` and it ends there.
  - **Delivery**: `POST /api/v1/sales-orders/{publicId}/delivery` (permission `warehouse.pick`, and `orders.create` to create the transport order) with the dispatch (`pickBatchPublicId`) and the delivery data (`order`: service, packages, COD…, the same as today's packing). **The client and the consignee come from the sales order** if they are not sent (or a new consignee is sent). It creates the real transport order, linked to the sales order (`TransportOrder.SalesOrderId`), and the dispatch moves to `PACKED`. It can also be created **later**, on a dispatch already made.
- **Undoing a dispatch** (deleting its pick batch, as today): the stock returns to its bin, the dispatched quantity of each line goes down and, if the order is not cancelled, that quantity is **reserved again** in the same bin and lot. A shipped or partial order **is reopened** (partial or requested). If the dispatch had a delivery (packed), it is deleted together with its transport order under the usual rules.

| Situation | Exact message | HTTP |
|---|---|---|
| Delivery without specifying the dispatch | `Specify the dispatch being delivered.` | 400 |
| Delivery without data | `Specify the delivery data.` | 400 |
| Dispatch that does not belong to this order | `Dispatch of this order not found.` | 404 |
| Dispatch that already has a delivery or was undone | `Dispatch {EMP-#####} already has a delivery or was undone: another one cannot be created.` | 422 |

## 12. For the device: orders to dispatch and dispatch with delivery (Batch 64)

- **`GET /api/v1/sync/sales-orders`** (SALES_ORDERS module and `sales.view` permission; the Warehouse operator has it): the orders **requested or partially shipped with something reserved**, with their lines (ordered, reserved, dispatched, backorder) and, per line, **from which bin and lot the system reserved each part** (`reservations`): it is the dispatch guide on the device. **All the open ones** always arrive (they are few and their lines change without the header changing); with `since` the ones that were closed, shipped or cancelled (`isActive = false`) also arrive so the device deletes them. Optional filter `warehousePublicId`.
- **Dispatch with delivery in a single call**: `POST /sales-orders/{publicId}/dispatch` accepts `delivery` (the same packing data: service, packages, new consignee or the order's, COD). It dispatches **and** creates the transport order in **one transaction**: if the delivery fails (for example, “Unknown service type”), the inventory does not leave and the order stays as it was. This is what the device sends, since it dispatches without signal and does not know the pick batch number until it syncs.

## 13. For support

- Tables `SalesOrder`, `SalesOrderLine` and `SalesOrderReservation` (the one that stores which balance each line reserved) and the columns `PickBatch.SalesOrderId` / `PickBatchLine.SalesOrderLineId` (section **2026-10-15 (l)**), `TransportOrder.SalesOrderId` (**(m)**) and the status adjustment of **(n)**: “Partially shipped” and “Shipped” are lateral stages so the order can be reopened when undoing a dispatch, `SALESORDER` counter, `SalesOrderStatus` status domain and `SALES_ORDER` EntityType (section **2026-10-15 (k)** of `Diseño/logistica-db-update.sql`).
- Each line stores `TaxRateId`, `TaxRatePct`, `TaxAmount` and `IsTaxExempt`: a later change of the rate does **not** alter an order already saved.
- A skipped `OV-` number does not happen: the counter is taken inside the same transaction that creates the order.

## 14. On the device: dispatching a sales order (Batch 65)

**Sales orders** screen of the device (the app's technical sheet is in [09 — Warehouse app](09-app-almacen.md#12-dispatch-a-sales-order-batch-65)).

- **Who sees it:** the “Sales orders” button on Home appears only if the device has already downloaded orders to dispatch from its warehouse (SALES_ORDERS module and `sales.view` permission in the company; the Warehouse operator has it). Without them the server answers 403 and the app skips it without reporting an error.
- **How it is used:** the `OV-#####` number is scanned (or typed), or the order is tapped in the list. The app guides **bin by bin** according to what the system reserved: “Go to A-01”, the reserved quantity is already filled in (it can be lowered, never raised), the bin is scanned and it is marked ✓. Tapping a ✓ undoes it. **Skip** leaves that part reserved for another dispatch.
- **Taking from another bin:** the other bin is scanned; the reservation moves there if it has enough stock (the server confirms it when sending and, if it is not enough, rejects it with `There is not enough stock…`).
- **When finishing:** *Pickup* (the client collects), *Delivery* (asks for the packages; only if the order has a consignee; the client and the consignee come from the order, the service and the charge from the company's values) or *Leave for later* (it stays collected; the delivery is created later from the web, section 11).
- **Without signal:** the dispatch is saved in the outbound queue (“Sales order dispatch” in Sync) and the local stock goes down immediately; the server validates it when syncing. If it rejects it, the device shows the server's exact message, removes the row from the queue, returns the stock and downloads the order again.
- **Dispatch + delivery** travel in **a single call** (section 12): if the delivery fails, the inventory does not leave.

## 15. Sales notices (Batch 66)

Sales orders publish five notices (requested, shipped, cancelled, credit to authorize and backorder that can be filled) on the notification engine: see the
table in [14 — Notifications §3.1](14-avisos.md#31-sales-order-events-batch-66). To receive them you must create the rule in **System → Notification rules**
(permission `notifications.manage`) and choose recipients and channel; without a rule nothing arrives (none is seeded).

- **Credit:** the authorization request goes out when the request fails with `The client exceeds its credit limit…` (422); whoever has the permission to authorize
  credit (`orders.credit_override`) requests again checking the authorization. The notice is there so that person finds out without being asked in person.
- **Backorder that can be filled:** the notice **does not fill**; whoever has `sales.backorder` fills from the Backorders view (section 9). The automatic mode is left for later.
- **A notice never undoes the operation:** if the e-mail fails, the order stays the same and the notice is retried (1, 5 and 30 minutes).
- **Support:** the notices stay in the delivery log (System → Notification rules → Log) per event and recipient; the `SALES_BACKORDER_SCAN` job and its history are seen in System → Scheduled jobs.

## 16. Sales order analytics (Batch 67)

With the **Sales orders** module on and the `sales.view` permission, Analytics (Pulse, Indicators, Charts and Views) offers two new data sources.
With the module off or without the permission, the sources and everything built with them do not appear.

**Source “Sales orders” (`SALES_ORDER`)**, one row per order, with the date range by creation date:

| Field | What it is |
|---|---|
| Number, Client, Warehouse, Status | Those of the order. |
| Open | Requested or partially shipped (there is still something to dispatch or waiting for inventory). |
| With backorder / Backordered units | Some line is waiting for inventory (not counting those closed with shortage). |
| Overdue | Open with a required date earlier than today (company day). |
| Order date, Required date, Requested on, Shipped on | Dates. “Shipped on” is the last change to Shipped. |
| Days to ship | From requesting it to shipping it completely; only in shipped orders. |
| Lines, Units ordered / reserved / shipped | Totals of the lines. |
| Fulfillment (%) | Shipped over ordered (maximum 100); only in shipped orders (with a closed shortage it stays below 100). |
| Subtotal, Tax, Total | Money of the order. |

**Source “Sales order lines” (`SALES_ORDER_LINE`)**, one row per line: order, client, warehouse, order status, SKU, product, ordered, reserved,
shipped, backordered, closed with shortage, **days waiting** (of the pending backorder), price, line total and tax.

**System content** (seeded automatically in each company; with the module off, it stays hidden):

| What | Name | Detail |
|---|---|---|
| Indicator (on the Pulse) | **Open sales orders** | How many are left to dispatch. |
| Indicator | **Backordered units** | Units waiting for inventory in open orders. |
| Indicator | **Sales order fulfillment (%)** | Average of shipped over ordered, orders shipped in 30 days. |
| Indicator | **Average days to ship** | From requesting to shipping completely, 30 days. |
| Chart | **Sales orders by status** | 30-day donut. |
| Views | **Sales orders**, **Sales orders by client**, **Sales order backorders** | The last one lists the lines with a pending backorder, the ones waiting the longest first. |

**Pulse → Needs your attention:** one row for each open order with a backorder that **can already be filled** (there is stock available to sell), for whoever has `sales.backorder`.
It shows fillable units, lines and days waiting (in red if over 7). **Review** opens the order; **View all** opens Sales orders. It is the same count as the
Backorders tab: what it reports is what **Fill** is going to reserve.

Database: section **2026-10-16 (o)** of `Diseño/logistica-db-update.sql` (EntityType `SALES_ORDER_LINE`); the rest is seeded by the code.

## 17. Kits in sales orders (Batch 69)

A **kit** ([06 §8b](06-inventario-y-almacen.md#8b-kits-batch-68-2026-10-16)) is sold as one more line of the order: the quantity is in **whole kits** and the price is the kit's.
Internally, everything moves in its **components**.

- **Line components.** When saving the draft, a **snapshot** of the kit's components (quantity per kit) is taken. A later change of the catalog does **not** alter the order; if
  you save the draft again, the snapshot is kept (including the adjusted pieces). To take the new catalog, remove the line and add it again.
- **Kit adjustable when ordering.** If the kit was marked “Adjustable when ordering”, the **quantities per kit can be changed for this order only** in the draft (the body carries `components` in the line).
  In a fixed kit, sending `components` gives `Kit {SKU} cannot be adjusted when ordering.` (400).
- **Available and short in kits.** As many kits can be assembled as the component that yields the least allows (available to sell ÷ quantity per kit, rounded down). The shortage
  dialog, the order and the backorder count **kits**: ordered 5, available 3, short 2.
- **Reserve and release.** Requesting reserves **the components of each complete kit** (by FEFO/FIFO of each component); never half a kit. Changing the quantity, cancelling, filling a
  backorder or closing with shortage act on the components in the same way. The reservation stores the reserved product (`SalesOrderReservation.ProductId`).
- **Dispatch.** Whole kits are dispatched, **from what is reserved of their components** (no bin or lot is chosen). The pieces of each component leave through the ledger, with the note
  `{KIT} × n · EMP-… · OV-…` in the movement ledger; the pick batch carries one line per component and records the kits it represents (`PickBatchLine.KitProductId` / `KitQuantity`).
  The quantity of a dispatch in the order's dispatch list is in **kits**.
- **Undoing a dispatch** returns the components to their bin, lowers the dispatched kits and **reserves each component again**, the same as with a regular product.
- **On the web** (Operations → Sales orders): a kit line carries the “Kit” badge and shows its components underneath; in the requested order, one table per kit with ordered, reserved,
  backordered and dispatched for each component.
- **Device** (Batch 71): it dispatches kit lines **piece by piece** — see the “Kits” section of [09 §12](09-app-almacen.md#12-dispatch-a-sales-order-batch-65). The server sends the kit lines with their components and the reservation of each component (`GET /sync/sales-orders`: `isKit`, `components` and, per reservation, the product).

| Situation | Message | HTTP |
|---|---|---|
| Kit quantity with decimals | `Kit {SKU} is ordered in whole pieces.` | 400 |
| Adjusted pieces in a fixed kit | `Kit {SKU} cannot be adjusted when ordering.` | 400 |
| Pieces in a line that is not a kit | `Product {SKU} is not a kit.` | 400 |
| Invalid adjusted pieces | the same as in [06 §8b.3](06-inventario-y-almacen.md#8b3-rules-and-messages) (quantity, repeated, kit inside a kit, inactive, another owner) | 400 |
| Kit without components | `Kit {SKU} has no components: define them before selling it.` | 400 |
| Dispatching a kit choosing a bin or lot | `A kit is dispatched from what is reserved of its components: no bin or lot is chosen.` | 400 |
| Dispatching kits with decimals | `Kit {SKU} is ordered in whole pieces.` | 400 |
| Dispatching more kits than reserved | `You cannot dispatch more than what is reserved: {SKU} has {n} reserved.` | 409 |

For support: table `SalesOrderLineComponent` (the snapshot, deleted with the line) and columns `SalesOrderReservation.ProductId`, `PickBatchLine.KitProductId` and `KitQuantity`
(section **2026-10-16 (q)** of `Diseño/logistica-db-update.sql`).

## Seeing the dispatch from the sale and the sale from the dispatch (2026-10-21)

- In the sales order record, the **Order dispatches** table shows the dispatch number (pick & pack) **as a link**: it opens the dispatch record (needs `pickbatches.view`; without it the number is shown without a link). The shipment created when packing is still shown in its column.
- In the dispatch record (Picking & packing) the **Sales order** field appears with its number as a link to the sale (needs `sales.view`; without it, only the number). It shows only when the dispatch was born from a sales order (API: `salesOrderPublicId` and `salesOrderNumber` in the dispatch).
