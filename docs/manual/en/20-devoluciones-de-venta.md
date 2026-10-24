# 20 — Sales returns

Batch 81 (P6, 2026-10-16). The client returns part of what was sold to them. The **return** (`DV-#####`, one counter per company) asks for the goods back, a supervisor authorizes it, the warehouse **receives** it (in parts, with the good and the damaged) and, if it had already been invoiced, it is **credited** with a credit note ([chapter 19 §4d](19-facturacion.md)). It is part of the **Sales orders** module ([chapter 18](18-ordenes-de-venta.md)).

## 1. Who can and where

- **Sales orders** module (`SALES_ORDERS`). API: `/api/v1/sales-returns`.
- **View** (list, record, what can be returned, the window): `sales.view`.
- **Request and cancel:** `sales.return` (new permission; it was given to the roles that already create sales orders and to the company administrator, also in the roles already cloned).
- **Authorize, reject and change the return window:** `sales.return.authorize` (new permission; company administrator and roles that already cancel sales orders). Also the only one who can request a return **outside the window**.
- **Receive:** `warehouse.receive` (the same as receiving). **Credit:** `billing.credit`.

## 2. The flow

**Requested → Authorized → Received → Credited**, or **Rejected** / **Cancelled**.

1. **Request** (`POST /sales-returns`): the sales order, the **reason** (damaged, wrong item, not needed or surplus, expired, other) and the lines with the quantity. Per line, up to **dispatched − already returned − what other open returns already ask for** can be returned (`GET /sales-returns/returnable?salesOrderPublicId=` shows it). **Kits** and **serialized** products are not returned in this version.
2. **Return window:** `SalesReturnWindowDays` (30 by default; 0 = no window; `GET/PUT /sales-returns/settings`). It is counted from the order's **last dispatch**. Outside the window only someone with `sales.return.authorize` can request it and must confirm it (`confirmOutsideWindow`); the return is flagged “outside the window”.
3. **Authorize** (`POST .../authorize`, with a note: how the goods come back) or **Reject** (`POST .../reject`, with a reason). **Cancel** (`POST .../cancel`) a requested or authorized return that has not been received yet; what it asked for becomes available again.
4. **Receive** (`POST .../receive`): per line **GoodQty** (good: returns to inventory) and **DamagedQty** (damaged; the damage description is mandatory; `damageDiscard` discards it right away; otherwise it stays in quarantine). It can be received **in parts**. It reuses receiving: it creates and **confirms a RETURN-type receipt** (in a single transaction) with the return as reference: the inventory is moved by that receipt (putaway or target bin `targetBinId`, and `lot` if the product has lots); the damaged part generates its **damage report** with the new cause **“Customer return”** (quarantine and then issue or recover). What is received (good + damaged) counts as **returned** on the sales order line (`QtyReturned`) and stops being billable. With `closeRest` what did not arrive is deemed not returned (it stops counting as pending).
5. **Credit** (`POST .../credit`): creates a **draft credit note per invoice** with what was received that **was** invoiced (reason Return, at the invoiced unit amount; only issued, overdue or paid invoices: a draft invoice is adjusted in its run) and moves the return to **Credited**. What was received that had **not** been invoiced simply will no longer be invoiced. The note is approved and applied like any other (chapter 19).

## 3. Validations and messages

| Situation | Message | HTTP |
|---|---|---|
| No order / no reason / nonexistent reason | `Specify the sales order of the return.` / `Specify the reason for the return.` / `The return reason does not exist.` | 400 |
| No lines / more than 200 / long note | `Add at least one line to the return.` / `The return cannot have more than 200 lines.` / `The note cannot exceed 500 characters.` | 400 |
| Line that does not belong to the order / repeated / invalid quantity | `The line does not belong to that sales order.` / `A line of the order is repeated in the return.` / `The quantity must be greater than zero (up to 3 decimals).` | 400 |
| Order with nothing dispatched | `Returns are only made from a dispatched sales order.` | 422 |
| More than what can be returned | `Only Y more of X can be returned.` | 422 |
| Kit / serialized product | `Kit lines are not returned in this version.` / `A serialized product is not returned in this version.` | 422 |
| Outside the window without permission or without confirming | `The return is outside the return window: it is authorized by someone with permission to do so.` | 403 |
| Authorizing / rejecting one that is not requested | `Only a requested return can be authorized.` / `Only a requested return can be rejected.` | 422 |
| Rejecting without a reason | `Enter the reason for rejecting the return.` | 400 |
| Cancelling one already received or closed | `Only a requested or authorized return that has not been received can be cancelled.` | 422 |
| Receiving one that is not authorized | `Only an authorized return can be received.` | 422 |
| Receiving without lines / foreign line / repeated | `Specify which line is received and how much.` / `The line does not belong to that return.` / `A line of the return is repeated in what is received.` | 400 |
| Invalid quantities | `Good and damaged quantities must be zero or more (up to 3 decimals) and at least one greater than zero.` | 400 |
| Damaged without description | `Describe the damage of what arrived damaged.` | 400 |
| Receiving more than what is missing | `Only Y of X are left to receive.` | 422 |
| Crediting one that is not received | `Only a received return can be credited.` | 422 |
| Nothing to credit | `There is nothing to credit: what was returned had not been invoiced (it will no longer be invoiced) or was already credited.` | 422 |
| Window days outside 0–3650 | `The return window days must be between 0 and 3650.` | 400 |
| Return or order does not exist | `Return not found.` / `Sales order not found.` | 404 |

## 4. For support

- Tables `SalesReturn` and `SalesReturnLine`; columns `Tenant.SalesReturnWindowDays`, `ReceiptHeader.SalesReturnId` (the RETURN-type receipt of each receiving) and `CreditNote.SalesReturnId`; section **2026-10-16 (w)** of `Diseño/logistica-db-update.sql` (also the `SalesReturnStatus` status domain, the `ReturnReason` catalog, the `CUSTOMER_RETURN` damage cause, the `SALES_RETURN` EntityType, the `SALESRETURN` counter and the permissions).
- The inventory is moved by the **receipt** (movement ledger with the source receipt); the damaged part, by the `DAN-#####` report. The return only records what was returned (`SalesOrderLine.QtyReturned`) and what was credited (`QtyCredited`).
- In the tests with the in-memory database (not real SQL) the nested services clear what is tracked: the service reloads its own data after creating the receipt and each note.

## 5. On the device: receiving a return (Batch 83)

The warehouse receives the return with the app: **Home → Returns** (see [09 §13](09-app-almacen.md#13-recibir-una-devolucion-de-venta-lote-83)). The device downloads the **authorized** returns (or partially received ones with something pending) with `GET /api/v1/sync/sales-returns` (`SALES_ORDERS` module and `warehouse.receive`; all the open ones arrive and, with `since`, the fully received, credited or cancelled ones with `isActive = false` so the device deletes them). Per line, the **good** and the **damaged** quantities are recorded (with what happened to it, and whether to discard or not), and the **lot** if the product has one; when sending, it calls the same `POST /sales-returns/{id}/receive` above, with the queue's idempotency key (a double send does not duplicate the receipt). From the web it can be received the same way (L82).

What the device does **not** do yet: choose the target bin of the good part (the server decides it according to the warehouse's receiving mode), capture the lot's expiration date or receive kits or serials (they are not returned either).
