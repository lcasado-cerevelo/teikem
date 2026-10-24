# 32 — Shipment return

Batch 112 (2026-10-18, dictated by Luis). An order that could not be delivered comes back to the warehouse and someone with permission **decides to return it**: the shipment becomes **“Returned”** (final status) and the **return shipment** (round trip) is created with the pieces being returned.

**Who can:** `orders.return` (company administrators already have it) with the **LTL_GROUND** module. **Where:** the shipment record (**Return** button) and the scan station (**Return** action, seen only by those who have the permission).

## 1. What happens on returning
- The shipment becomes **Returned** (final: it is no longer edited, cancelled or assigned). Reopening it requires `orders.reopen` **and that its return order be cancelled**: an order that already went out for return is not reopened while the return is still alive (the return is cancelled first).
- The **return shipment** is created: a new delivery shipment of the **same client** and the same service type; its **pickup is the consignee** (the original's delivery) and its **delivery is the address of the client that placed the shipment**: the original's pickup or, if it had none, the client's pickup and, failing that too, the address the client has on record (pickup, corporate or billing). New waybill and shipment number.
- **Only the pieces being returned** travel: the same pieces with **their same numbers** (the labels remain valid); they leave the original and enter the return shipment. Those not returned continue their flow (if they are missing, in Claims; see chapter 31).
- **No freight billing:** the return shipment is neither quoted nor charged. If the contract charges **per delivery**, the returned pieces are removed from the original (what was not delivered is not charged); if it charges **per pickup**, everything is charged (chapter 31, §4.2).
- A shipment that is **Pickup, Unassigned, Assigned, In transit or with a Failed attempt** can be returned. A **Submitted** one is not returned (it has not been picked up yet: it is cancelled). A shipment that is already final (Delivered, Cancelled, In warehouse…) is not either.

## 2. From the record: “Return” button
The pieces being returned are checked (all by default; missing ones cannot be returned) and an optional note. The return shipment **is born Submitted** and is worked like any order (pick up, arrived, route, depart). Endpoint: `POST /api/v1/orders/{publicId}/return` with `{ pieceNumbers, notes }` (without numbers, all those that are not missing).

## 3. From the station: “Return” action
The guard chooses **Return** and scans **the pieces being returned**, one by one:
- The **first** piece leaves the shipment **Returned** and creates the return shipment, which **is born Submitted but “skipped”** up to **Pickup** (the status of the scanned one: we already have it in the warehouse). The **following** pieces of the same shipment are added to that return shipment while it is still Submitted or Pickup.
- Repeating a piece is a **duplicate** (“Ok”). A missing piece responds “No”.
- On **Close count** of the returned shipment, the station shows the **list of the pieces that were not scanned** (not returned) and asks for **“Confirm and close the count”**; on confirming they become “To verify” (from there they continue in Claims).

## 4. Return fee (optional, per contract)
Each contract can charge a **return fee** (**Client → Contract → Return fee**, `contracts.update`; by default **no fee**). Three modes:
| Mode | What it charges |
|---|---|
| **Per order** | One amount per returned order (the same for all package types); it is charged only once even if more pieces are returned later. |
| **Per package type** | The amount of each package type being returned (a type without an amount does not charge). |
| **Per returned piece** | One amount for each piece traveling in the return shipment. |
- The fee is **added to the returned shipment** on returning it (button or scan; with the per-piece or per-type mode it grows as pieces are added) and is **invoiced apart from the freight** as its own line **“ORD-… · Return fee”** (surcharge, with its tax), when the shipment is Returned; it enters the freight billing run (**Include freight**). Cancelling the run or voiding the invoice releases it.
- The **return shipment** still has no freight or fee. A shipment without a contract or with a contract without a fee charges nothing.
- API: `PATCH /api/v1/contracts/{publicId}/return-fee` with `{ mode, amount, byType:[{packageType, amount}] }` (empty mode = no fee; the per-type list is replaced in full).

## 5. “Return after N attempts” (suggestion)
Each company defines with how many **failed attempts** returning a shipment is **suggested** (**Tenant settings → General → Return**, `admin.tenant`; by default **2**; **0** = never suggest; from 0 to 20). It is **only a suggestion and a mark**: it never blocks and the shipment can keep being retried.
- **In the shipment record** “Returning it is suggested: N failed attempt(s)” appears next to the status, and in a return shipment it reads “Return of {shipment}”.
- **At the scan station** the scan message of a shipment with those attempts ends with “It has N failed attempt(s): returning it is suggested.” (the scan is done anyway).
- The **current and undelivered** delivery attempts count (voided ones do not). It is not suggested for a Submitted shipment or for one that already ended (Delivered, Cancelled, In warehouse, Returned).
- API: `GET/PUT /api/v1/tenant/return-settings` (`{ suggestAfterAttempts }`; 400 “Enter after how many failed attempts returning is suggested: from 0 (no suggestion) to 20.”).

## 6. Error messages
| Message | HTTP | When |
|---|---|---|
| A shipment in “Submitted” is not returned: it has not been picked up yet. Cancel it. | 422 | Returning a shipment in Submitted. |
| Shipment {n} is in a final status ({status}): it is not returned. | 422 | Returning a Delivered, Cancelled, In warehouse… shipment (by scan the result is “final”). |
| Enter at least one piece being returned. | 400 | No piece was left to return. |
| Piece {n} does not belong to that shipment. | 400 | A piece number foreign to the shipment. |
| Piece {n} appears as missing: it cannot be returned. | 422 (record) / “No” (scan) | The piece is To verify, Lost or Claimed. |
| The client has no address on record (pickup, corporate or billing) to return the shipment to: register it in its record. | 422 | The shipment has no pickup and the client has no address on record. |
| The shipment already went out for return: cancel the return shipment {n} first to be able to reopen it. | 422 | Reopening a Returned shipment whose return is still alive. |
| You do not have permission for this action. | 403 | `orders.return` is missing. |
| Shipment not found. | 404 | Nonexistent identifier (or from another company). |
| Enter the return fee: per shipment, per package type or per piece. | 400 | Unknown mode in the contract. |
| Enter the return fee amount. | 400 | Per-order or per-piece mode without an amount. |
| The return fee cannot be negative. | 400 | Negative amount. |
| Enter the amount of at least one package type. | 400 | Per-package-type mode without any amount. |
| Package type {t} is repeated. / Unknown package type: '{t}'. | 400 | Per-type list with repeated types or with a type that does not exist. |

## 7. Frequent cases
- *I returned by mistake:* first the **return shipment is cancelled**; then the Returned shipment is reopened with `orders.reopen`. While the return is still alive it is not reopened.
- *The shipment had 4 pieces and I only scanned 3 with “Return”:* on closing the count, the 4th becomes “To verify” and is handled in Claims.
