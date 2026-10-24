# 40 — Exit notices

Batch 143 (2026-10-20). An **exit notice** (`SAL-#####`) is the document with which the **owner of the goods** (a warehouse client, e.g. ASICS Care in the warehouse that
Advance Logistics manages) tells the warehouse **what it wants taken out**. It is the mirror of the **arrival notice** (ASN): the arrival notice says "this is coming to me", the exit notice says
"take this out for me". **It is not a sale**: it carries no price, tax, credit or invoice (to sell, the sales order is used, chapter 18).

This chapter covers the server (API, rules, jobs and notices). The web and device screens arrive in batches 144 to 146.

## 1. Who can

- Module **Lot and serial inventory (`WMS_LOTSERIAL`)**. With the module turned off the whole API responds 403.
- Permissions:
  - **`inventory.view`** — see the notices and the backorder.
  - **`exitnotice.create`** (new, Warehouse category) — create, change the quantity of a line, cancel, reserve the backorder and close with shortage. The Company admin and the
    Warehouse operator have it (the existing roles too: the database script adds it to them).
  - **`warehouse.pick`** — dispatch against the notice.

## 2. What it does

- **Only the owner's products.** Each line is an **active** product of the notice's client; one from another owner, or the company's own, is rejected.
- **Reserves on the spot.** When created, it reserves whatever there is using the **company's rotation** (chapter 6 §2.2: with lot FEFO or FIFO; without lot FIFO or by bin). The reservation is **per
  product and lot**; internally the ledger reserves per bin, and when dispatching the same lot can be taken from another bin (the reservation moves by itself) unless the rotation is set to
  **Enforce**.
- **Never rejected for lack of stock.** Whatever is short stays **pending** (backorder) on that line. The job `EXIT_NOTICE_BACKORDER_SCAN` (every 10 minutes) keeps reserving it
  as goods come in, **the oldest notice first**, and sends a notice.
- **No serials or kits** in this first version (same criterion as sales orders with serials). A lot-tracked product can request a specific lot or leave it to the rotation.
- **No price.** The destination is free text (pickup, address or note).

## 3. Features

| What | Endpoint | Permission |
|---|---|---|
| List (filters `status`, `clientPublicId`, `q`, `backorderOnly`, `from`, `to`; paginated) | `GET /api/v1/exit-notices` | `inventory.view` |
| Record with lines and dispatches | `GET /api/v1/exit-notices/{publicId}` | `inventory.view` |
| Create (reserves whatever there is; the rest pending) | `POST /api/v1/exit-notices` | `exitnotice.create` |
| Change the quantity of a line | `PATCH /api/v1/exit-notices/{publicId}/lines/{lineId}` | `exitnotice.create` |
| Cancel | `POST /api/v1/exit-notices/{publicId}/cancel` | `exitnotice.create` |
| See the backorder (oldest first) | `GET /api/v1/exit-notices/backorders` | `inventory.view` |
| Reserve the backorder of a line | `POST /api/v1/exit-notices/{publicId}/lines/{lineId}/fulfill` | `exitnotice.create` |
| Reserve all the backorder that can be reserved | `POST /api/v1/exit-notices/backorders/fulfill` | `exitnotice.create` |
| Close a line with shortage | `POST /api/v1/exit-notices/{publicId}/lines/{lineId}/close` | `exitnotice.create` |
| Dispatch | `POST /api/v1/exit-notices/{publicId}/dispatch` | `warehouse.pick` |

**Create** — body: `warehousePublicId` (optional if there is a single active warehouse), `clientPublicId` (required), `reference` (≤ 80), `destination` (≤ 200), `requiredDate`, `notes` (≤ 500) and
`lines` (1 to 200: `productPublicId`, `quantity` and, if desired, `lotId`). Returns the record with what was reserved and what is pending on each line.

**Dispatch** — body: `lines` (`lineId`, `quantity`, and optionally `binId` and `lotId`) and `note`. Without a bin it comes out of what the notice has reserved (which already follows the rotation). With
a bin, the reservation moves to that bin. It creates a **pick batch** linked to the notice (number `EMP-#####`, which **is not packed**: it is an inventory exit without delivery); if that
pick batch is deleted, what was dispatched is reserved again and the notice reopens.

## 4. Statuses

| Status | Meaning |
|---|---|
| **Requested** (`REQUESTED`) | Initial state: whatever there was is reserved; it can be changed and cancelled as long as nothing has been dispatched. |
| **Partially shipped** (`PARTIAL`) | Part was dispatched and some remains reserved or pending. It can only be dispatched, have its backorder reserved or be closed with shortage. |
| **Shipped** (`SHIPPED`) | Everything dispatched or closed with shortage. It reopens if a pick batch is undone. |
| **Cancelled** (`CANCELLED`) | Cancelled by hand, or automatically when all the lines were closed with shortage without dispatching anything. Releases everything. |

The **backorder is not a status**: it is the mark that some line has a pending quantity (`hasBackorder`).

## 5. Validations and messages

| Case | Exact message | HTTP |
|---|---|---|
| No client | `Choose the customer that owns the goods.` | 400 (`clientPublicId`) |
| Client does not exist | `Client not found.` | 404 |
| Inactive client | `The client is inactive; an exit notice cannot be made for it.` | 422 |
| Several warehouses and none indicated | `Indicate the warehouse: the company has more than one.` | 400 (`warehousePublicId`) |
| Inactive warehouse | `The warehouse is inactive.` | 422 |
| No lines | `Add at least one product to the exit notice.` | 400 (`lines`) |
| More than 200 lines | `An exit notice admits at most 200 lines.` | 400 (`lines`) |
| Line without product | `Choose the product of each line.` | 400 (`lines[i].productPublicId`) |
| Product of another owner or the company's own | `Product {SKU} does not belong to the client of the exit notice.` | 400 (`lines[i].productPublicId`) |
| Repeated product | `A product cannot be repeated in the exit notice.` | 400 (`lines[i].productPublicId`) |
| Inactive product | `There is an inactive product in the exit notice.` | 422 |
| Serial-tracked product | `Products with a serial number cannot be requested with an exit notice yet.` | 422 |
| Kit | `Kits cannot be requested with an exit notice yet.` | 422 |
| Lot on a product without lot | `Product {SKU} is not lot-controlled: do not indicate a lot.` | 400 (`lines[i].lotId`) |
| Quantity ≤ 0 | `The quantity must be greater than zero.` | 400 (`quantity`) |
| Quantity with more than 3 decimals | `The quantity admits at most 3 decimals.` | 400 (`quantity`) |
| Huge quantity | `The quantity exceeds the maximum allowed.` | 400 (`quantity`) |
| Reference / destination / notes too long | `The reference cannot exceed 80 characters.` / `The destination cannot exceed 200 characters.` / `The notes cannot exceed 500 characters.` | 400 |
| Notice does not exist (or belongs to another owner in the portal) | `Exit notice not found.` | 404 |
| Changing a notice that is not requested | `Only the lines of a requested exit notice can be changed.` | 422 |
| Changing a closed line | `The line is closed.` | 422 |
| Changing a line with product already dispatched | `Product has already been dispatched for that line: it cannot be changed.` | 422 |
| Cancelling a dispatched one (partial or total) | `Only a requested exit notice that has not been dispatched can be cancelled.` | 422 |
| Cancelling one already cancelled | `The exit notice is already cancelled.` | 409 |
| Reserving the backorder without stock | `There is no inventory available to reserve the backorder of that line.` | 409 |
| Line without backorder | `That line has no backorder.` | 409 |
| Backorder/close of a notice that is not open | `The backorder is only handled for a requested or partially shipped exit notice.` | 422 |
| Dispatching without lines / without line / repeated line | `Indicate which lines are dispatched.` / `Indicate the line being dispatched.` / `A line cannot be repeated in the dispatch.` | 400 |
| Dispatching a notice that is not open | `Only a requested or partially shipped exit notice can be dispatched.` | 422 |
| Dispatching more than reserved | `You cannot dispatch more than what is reserved of {SKU} ({n}). Reserve the backorder first.` | 409 |
| Lot without bin in the dispatch | `If you indicate the lot it comes from, also indicate the bin.` | 400 (`lines[i].lotId`) |
| Unusable bin | `Bin {code} cannot be used: it is inactive or it is a quarantine, cross-dock or rental bin.` | 422 |
| Rotation set to "Enforce" and another bin was requested | `Rotation is mandatory: {SKU} must come out of {bins}. Do not choose a bin or lot; the system assigns them.` | 409 |
| Packing the dispatch of a notice | `Dispatch {number} belongs to an exit notice and is not packed: it is an inventory exit without delivery.` | 422 |
| Two people at the same time | `The exit notice changed at the same time. Reload it and try again.` | 409 |

## 6. System notices

Four events (inventory module, optional; **no rule comes preconfigured**, the administrator creates it in System → Notification rules — chapter 14): **Exit notice requested**
(`EXIT_NOTICE_REQUESTED`), **Backorder reserved** (`EXIT_NOTICE_BACKORDER_RESERVED`, published by the job), **Exit notice shipped** (`EXIT_NOTICE_SHIPPED`, when it becomes fully
shipped) and **Exit notice cancelled** (`EXIT_NOTICE_CANCELLED`, by hand or automatically).

## 7. Frequent cases

- **I requested 10 and it only reserved 3.** That is expected: 3 reserved and 7 pending. When goods come in the job reserves the rest (or "Reserve" does it on the spot).
- **I no longer want the backorder.** "Close with shortage" on that line: it stops waiting for inventory and what is already reserved continues to be dispatched.
- **I dispatched by mistake.** Delete the pick batch (Picking → delete): the stock returns to its bin, what was dispatched is reserved again and the notice reopens.
- **Can the notice be seen from the client portal?** Not yet; it arrives with the warehouse portal. The filter by client already exists on the server, so a client will only see its own.

## Screen (Batch 144)
On the web: **Receiving → “Exit notices” tab** (`/warehouse/receipts?tab=exit-notices`). Seeing the list requires `inventory.view`; **New exit notice**, **Reserve the backorder**, changing quantity, closing with shortage and cancelling require `exitnotice.create`. The list has a free-text search box, a status filter and “Only with backorder”; a notice with shortage shows the “With backorder” mark. Dispatching against the notice is done from Picking or Manual issue (batch 145).

## Dispatching and “dispatch requires a document” (Batch 145)
- **Dispatching from the notice:** open the notice (Receiving → Exit notices) and press **Dispatch** (permission `warehouse.pick`, only if something is reserved). Enter how much of each line goes out (by default everything reserved; 0 = that line does not go out). On-screen error: “You cannot dispatch more than what is reserved.”.
- **Tenant setting** (Settings → Operations → “Dispatch requires a document”): Free (default), Exit notice or sales order, Exit notice only, Sales order only. With any value other than Free the **manual issue** is blocked: HTTP 409 “The company requires dispatching against an exit notice. Create the notice and dispatch from it.” (or “…against a sales order…”, or “…against a document (exit notice or sales order)…”). Packing is not affected.

## On the warehouse device (Batch 146)
Home shows **Exit notices** when the device has notices with something reserved (they are downloaded when syncing; requires `inventory.view` and the lot and serial module). Scan or type the SAL-… number (or tap one from the list), go to each bin, scan the bin, confirm the quantity and when done tap **Dispatch**. It works with no signal: it stays in the queue and is sent when back online; if the server rejects it (for example, mandatory rotation or less stock) it shows its exact message. With “Dispatch requires a document” active, **Dispatch** first asks “Dispatch against what?” and offers exit notice and/or sales order.

## Editing the data of a requested notice and availability in the selector (Batch 158)

`PATCH /exit-notices/{publicId}` (`exitnotice.create`) changes the reference, destination, required date and notes of a **requested** notice (empty text removes them; null leaves them unchanged). Quantities are still changed per line. Another status: 422 “Only the lines of a requested exit notice can be changed.”. When choosing a product in “New exit notice” the selector shows in parentheses what is **available** of each product, to know whether what is requested gets reserved or stays pending.

### Dispatch (Batch 160)

“Dispatch” opens a window that says **“Everything reserved will be dispatched: N units on M lines”**, with the list of what goes out; a single click on “Dispatch everything reserved” dispatches it. If the truck does not take everything, “Adjust quantities” shows the table (product, lot, reserved and how much goes out; 0 = that line does not go out) and the button changes to “Dispatch”. The backorder does not appear: there are no goods set aside for it.
