# 31 — Missing pieces and claims

Batch 111 (2026-10-18, dictated by Luis). What happens with a piece that did not arrive: it follows a four-step path and, when it is truly lost, it is claimed in its own document, the **claim `RCL-#####`**.

**Who can:** view, `claims.view`; mark lost, create and resolve claims, `claims.manage` (company administrators already have them). Module **LTL_GROUND**. **Where:** Operations menu → **Claims** (`/ops/claims`).

## 1. The path of a missing piece
| Step | How it gets there | What it means |
|---|---|---|
| **To verify** | The scan station marks it (the shipment moved to the next action without the piece, or “Close count” was pressed) — see chapter 05 | It is missing, but it may turn up |
| **Lost** | Someone with `claims.manage` marks it lost | It is given up as lost; there is no claim yet |
| **Claimed** | The claim is created with it | It is inside `RCL-#####`; nothing happens with the piece outside the claim anymore |
| **Resolved** | The claim is resolved | Closed: a piece that gets scanned responds “No” |

## 2. “To verify” tab
- Lists the **shipments with missing pieces** (To verify or Lost, without a claim): shipment, waybill, client, shipment status and “N of M” (missing of total). Free-text search by **shipment number, waybill or piece number**; sort columns and export the whole filter.
- On opening a shipment: the table of its missing pieces and, with `claims.manage`:
  - **Mark N as lost:** moves **all** the shipment's “To verify” pieces to “Lost” (through the API individual pieces can be indicated).
  - **New claim:** *declared value* (optional, non-negative) and *notes* (up to 1000 characters); **Create claim (N)** gathers that shipment's “Lost” pieces into a claim `RCL-#####` and opens it.
- The claim carries: shipment, line(s), pieces, description (for example “2 × Boxes; 1 × Envelope”), declared value and notes.

## 3. “Claims” tab
- List with number, shipment, client, description, pieces, declared value, status (**Claimed** / **Resolved**) and date; search by claim, shipment, waybill or piece; filter by status.
- **Resolve** (only `claims.manage`, only if it is Claimed): *Paid* (asks for the **amount paid**), *Rejected* or *Closed*; optional notes. The claim becomes **Resolved** and its pieces “Resolved”. The amount is stored only if it was paid.

## 4. When a piece turns up (scan station)
If a piece that was **To verify, Lost or Claimed** is scanned:
- **Claim not closed:** the piece is **removed from the claim**; if the claim is left with no pieces, **it is deleted**. The piece stops being missing, it is scanned like any other (result “reappeared”, audio “Yes”) and it is noted so that, on closing the count, its new order is created (see §4.1).
- **Closed claim (Resolved):** the scan responds **“No”**: *The piece belongs to a closed claim.*
- **4.1 Closing the count and the new shipment (Batch 111b).** On pressing **Close count** the station shows **“Close the count of {shipment}”** with (a) the **list of the pieces that were not scanned** (they will become “To verify”) and (b) if lost or claimed pieces turned up, the **new shipment** that will be created with them. Nothing happens until **“Confirm and close the count”** is pressed; “Cancel” leaves everything as it was. (If nothing is missing or turned up, it closes without asking.) The warehouse app will do exactly the same when it is built.
  - **The new shipment** is a delivery shipment with the **same client and consignee** (same pickup and delivery stops, service type and priority), **new waybill** and the **same pieces with their same numbers** (the labels remain valid). One is created for each original order.
  - **Its statuses:** first it repeats what the original had —**Submitted** and, if the original reached **Pickup**, **Pickup**— with **the same dates**; then the status of the action the pieces were scanned with is applied to it (with the date of that scan), the same as any other shipment. **Route** also assigns it to an open route of its zone (if there is nobody to receive it, the closing is undone with the route's message).
  - **Charging:** if the contract **charges per pickup**, the original is already charged in full and the new shipment **is not charged again**. If it **charges per delivery** (or mixed), the new shipment is **quoted with its pieces and charged when it is delivered**, and **those pieces are removed** from the original (and it is quoted again) so as not to charge them twice; if the original was already invoiced in full, the new one is not charged.
- Even if the shipment is already in a final status (delivered incomplete), the missing piece that turns up is accepted; it is not rejected as “final”.

## 4.2 Charging for what is missing
If the contract charges **per delivery** (or mixed), on marking pieces as **Lost** the shipment is quoted again only with the pieces that remain chargeable (what is not delivered is not charged); if it charges **per pickup**, everything is charged and the client claims (the claim). A shipment already invoiced is not recalculated.

## 5. Error messages
| Message | HTTP | When |
|---|---|---|
| Enter the shipment. | 400 | The shipment is missing when marking lost or claiming. |
| Enter at least one piece. | 400 / 422 | 400: an empty list of pieces was sent; 422: the shipment has no “To verify” pieces to mark. |
| Piece {n} does not belong to that shipment. | 400 | A piece number foreign to the shipment. |
| Piece {n} is not “To verify”. | 422 | Marking lost a piece that is not in that step. |
| Piece {n} is not “Lost”: mark it as lost before claiming it. | 422 | Claiming a piece that is not lost. |
| The shipment has no lost pieces to claim. | 422 | There are no “Lost” pieces without a claim in that shipment. |
| The declared value cannot be negative. | 400 | Negative declared value (or amount paid). |
| Notes cannot exceed 1000 characters. | 400 | Notes too long. |
| Choose the resolution: paid, rejected or closed. | 400 | Resolving without choosing a valid resolution. |
| Enter the amount paid. | 400 | “Paid” resolution without an amount. |
| The claim is already resolved. | 409 | Resolving twice. |
| The piece belongs to a closed claim. | 200 (result “No”) | Scanning the piece of a Resolved claim. |
| Claim not found. / Shipment not found. | 404 | Nonexistent identifier (or from another company). |
| You are about to close the count of shipment {n}… (`needsConfirmation`) | 200 | There are pieces not scanned or that turned up: nothing was done yet; resend `POST /api/v1/scan/close-count` with `confirm: true` (and `planDate` if the new shipment is routed). |

## 6. Endpoints
`GET /api/v1/claims/to-verify` (`search`, `skip`, `take`), `POST /api/v1/claims/lost`, `POST /api/v1/claims`, `GET /api/v1/claims`, `GET /api/v1/claims/{publicId}`, `POST /api/v1/claims/{publicId}/resolve`. The company always comes from the session.

## 7. Frequent cases
- *I marked a piece lost by mistake:* there is no manual step back; if it turns up and is scanned, it stops being missing (and it is removed from its claim if it had one and it is not closed).
- *The claim disappeared from the list:* all its pieces turned up; it was deleted (it remains in the audit history).
- *I want to claim only some pieces:* through the API the numbers are sent in `pieceNumbers`; on screen all the shipment's “Lost” pieces are claimed.

**Prefix change (2026-10-20, Batch 164):** the claim number went from `REC-#####` to `RCL-#####` so as not to confuse it with the receipt's (`REC-#####`). The claims that already existed were renamed with the script in `Diseño/logistica-db-update.sql`.
