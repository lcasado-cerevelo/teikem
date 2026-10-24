# 44 — Master receiving (“Receive together”)

Batches 182 and 183 (2026-10-21). When the same truck (or the same group of arrivals) brings **several documents** —several arrival notices and/or purchase orders— they can be received **together**: a
**master receiving** (`RCM-#####`) is opened that gathers one receipt per document. Each document **keeps its usual receipt** (`REC-#####`, with its lines, batches, status and putaway); the master opens them with the same
carrier and reference, shows the progress of all of them and **confirms them together**.

> **Rules decided (same as the master pick batch, 2026-10-21):** the master is assembled **when opening** the receipts (documents are not added afterwards); it belongs to **a single owner** (one client, or the company's
> own goods) and **a single warehouse**; for another owner or another warehouse, another receiving. The master does nothing new with the inventory: each receipt posts it as always.

## 1. Who can

- Module **Lot and serial inventory (`WMS_LOTSERIAL`)**. A purchase order document also requires **`purchasing.receive`** and the purchases module (like a normal receipt against a purchase order).
- See the masters: **`inventory.view`**. Open, confirm and delete: **`warehouse.receive`**.

## 2. Screen (Warehouse → Receiving → “Masters” tab)

The **Masters** tab lists the master receivings: number, owner, documents, received / expected, status (**Open**, **Confirmed**, **Deleted**) and date; filter by status and search box by the number of the
master, of one of its receipts, notices or purchase orders; it exports in full.

1. **Receive together**: lists the arrival notices that are expected and without receipt. When choosing the first one, those of **another owner** or **another warehouse** are blocked (with the reason on hover). You must choose **at least two** (up to 20). You can type a carrier and a reference (copied to all the receipts) and a note for the master. **Receive together (n)** opens one receipt per notice and the master's record.
2. **The record** shows the owner, the warehouse, who opened it and when, the progress (“n of m receipts confirmed · received / expected”) and the table of receipts with their document, status and quantities. **Open receipt** takes you to the usual screen of that receipt to capture (quantities, lots, serials, damage, bins).
3. **Confirm all**: confirms together the receipts that are not confirmed yet, with what was captured in each one. It is **all or nothing**: if one cannot be confirmed, none is confirmed and its message is shown.
4. **Delete**: deletes the master and its receipts as long as **none is confirmed**; the notices become free to be received again (a receipt with batches already posted is not deleted).

Status of the master (derived from its receipts): **Open** if some receipt is not confirmed, **Confirmed** if all of them are, **Deleted** if it was deleted.

## 3. Endpoints (`/api/v1/receipts/groups`)

| What | Endpoint | Permission |
|---|---|---|
| List (`search`, `status`, `skip`, `take` ≤ 100) | `GET /receipts/groups` | `inventory.view` |
| Record with its receipts | `GET /receipts/groups/{publicId}` | `inventory.view` |
| Open (2 to 20 documents) | `POST /receipts/groups` with `{documents:[{asnId \| purchaseOrderPublicId}], dockId?, stagingBinId?, carrier?, reference?, receivingMode?, note?}` | `warehouse.receive` |
| Confirm all | `POST /receipts/groups/{publicId}/confirm` with `{comment?}` | `warehouse.receive` |
| Delete | `DELETE /receipts/groups/{publicId}` → 204 | `warehouse.receive` |

## 4. Error messages

| Message | HTTP | When |
|---|---|---|
| “Choose at least two documents to receive together; for a single one use the normal receipt.” | 400 (`documents`) | fewer than two |
| “A master receiving admits at most 20 documents.” | 400 (`documents`) | more than twenty |
| “Each document carries an arrival notice or a purchase order, only one.” | 400 (`documents[i]`) | a document without source or with both |
| “The same document is repeated.” | 400 (`documents[i]`) | a document twice |
| “The documents must belong to the same owner: “A” and “B” belong to different owners. Receive separately what belongs to different owners.” | 400 (`documents[i]`) | a different client, or own goods with a client's |
| “The documents must belong to the same warehouse: “A” and “B” belong to different warehouses.” | 400 (`documents[i]`) | another warehouse |
| “The note cannot exceed 500 characters.” | 400 (`note`) | long note |
| those of a normal receipt (notice already received or with an open receipt, purchase order with nothing pending, the receipt requires a document…) | 409 / 422 | the document cannot be received; nothing is left open (no partial effect) |
| “All the receipts of this master receiving are already confirmed.” | 422 | confirming again |
| “The master receiving cannot be deleted: one of its receipts is already confirmed.” | 422 | deleting with some receipt confirmed |
| “Master receiving not found.” | 404 | it does not exist or belongs to another company |

## 5. Frequent cases

- **“A notice does not appear in the Receive together list”.** Only notices that are **expected and without receipt** are offered: one already received, cancelled or with an open receipt (standalone or from another master) does not show up.
- **“A receipt of the master has a variance”.** Each receipt is confirmed as always (with its variance, its notice to the client, its putaway). The master only gathers them.
- **“I want to receive a document of the master in batches”.** Open that receipt and use the usual batches (chapter 6); **Confirm all** closes the ones still open.
- **Purchase orders:** “Receive together” on the web also offers the purchase orders to be received (sent or partially received). They are the company's **own** goods: they are not combined with a client's notices.

## 6. From the warehouse app (“Receive together / load”)

In **Receive**, before choosing the document, **Receive together (open load)** opens a load (a truck, a railcar, a container… whatever arrives). While it is open, you receive **one document after another as always** (scanning or choosing from the list, also in batches) and **each receipt travels to the queue with the truck's key**: the server gathers them in a **master receiving** (`RCM-#####`) that is born with the first one.

- **The first document sets the owner** (the notice's client, or the company's own goods if it is a purchase order or a notice born from one). A document of another owner **does not enter the truck** (the app warns before starting its receipt); to receive it you must **Finish load**.
- The load shows how many documents have been sent. **Finish load** closes it; the receipts already sent stay in their master.
- It works **with no signal**: the key goes with each submission of the queue. If a submission arrives and the load was already deleted on the web (“The master receiving was already deleted; open a new one.”), that submission is rejected and it is received again with a new truck.
- What was already described for the web (progress of all, “Open receipt”, “Confirm all” of those still open) is seen in the **Masters** tab. A blind receipt cannot be put into a truck (documents only).

New API messages (`groupKey` in `POST /receipts` and `POST /receipts/part`): “This document belongs to a different owner than those of master receiving RCM-#####: receive separately what belongs to different owners.” and “This document belongs to a different warehouse than those of master receiving RCM-#####.” (400, field `groupKey`); “Receive together is for documents only: indicate an arrival notice or a purchase order.” (400); “The master receiving was already deleted; open a new one.” (409).

## How the master is seen from the receipt and from “Arrivals & departures” (Batch 186)

- Each receipt that belongs to a master receiving shows a **“Master RCM-…”** tag next to its status, both in the receipt list and in its record.
- In **Arrivals & departures** (screen header) there is the same **“Receive together”** button as in Receiving (permission `warehouse.receive`): it opens the same window and, when the master receiving is created, takes you to its record in Receiving → Masters.
