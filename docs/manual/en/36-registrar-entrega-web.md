# 36 — Record the delivery from the web

> Batch 118 (2026-10-18, dictated by Luis). Permission: **`orders.deliver`** (the administrator grants it to whoever they decide; by default only the TenantAdmin role has it). Module: **LTL_GROUND**.

## What it does
Staff record that a shipment **was delivered** without waiting for the driver app. The shipment moves to **Delivered** and a **proof of delivery** is kept that the client can see later in their portal.

- The shipment skips whatever statuses it is missing (Picked up, Unassigned, Assigned, In transit): they stay in the history as **skipped**, with the same date and time, just like a scan. Those statuses do not notify the client.
- If the shipment was in **Failed attempt**, it can also be delivered.
- On reaching Delivered, the usual effects run (the delivered delivery attempt that feeds the driver settlement, the collection, the notices).
- A **Pick up** shipment is not delivered (it ends “In warehouse”), and neither is a shipment in a **final status** (Delivered, Returned, In warehouse, Cancelled).

## The automatic signature
The proof **is not signed by the consignee**: it is signed **automatically by whoever records it** (because they have the permission, they are authorized). The proof stores and shows:

- **Who received it** (name typed by hand), the optional **note** and the **date and time** of the delivery.
- **Registered by** the user, and the signature “**Signed automatically by [user]**”.
- The notice “**The consignee did not sign: the delivery was registered by staff.**” That way the client never believes the consignee signed when they did not.

*(Photos: not yet. Where the files are stored is still to be decided; once decided, they go into the same proof.)*

## How to use it (screen)
In the shipment record (**Operations → Shipments**), with the permission and the shipment in progress, the **Register delivery** button opens the dialog:

| Field | Rule |
|---|---|
| **Who received it** | Required, up to 150 characters. |
| **Delivery date and time** | Optional. Empty = right now. Cannot be in the future (5 minutes of browser clock drift are tolerated). |
| **Note** | Optional, up to 500 characters. |

After recording, the record shows the shipment as **Delivered** and, in the right column, the **Proof of delivery** panel. See the screens manual: [L118](frontend/l118-registrar-entrega.md).

## The location (Google), without making anyone wait
If the delivery stop **already has coordinates**, the proof uses them (status “From the stop”). If not, and the company has **Google turned on**, the address is looked up **separately, without the user waiting**:

- **A single attempt**, with a **maximum time of 30 seconds**. If Google does not respond, does not find it, finds it with low precision or outside Puerto Rico, the proof is left **without location** and the delivery is recorded anyway. Nobody retries.
- Proof statuses: **No location** (not attempted: Google off or stop without address), **From the stop**, **Searching…** (queued or in progress), **Found** (with coordinates), **Address not found**, **Lookup failed** (error, timeout or quota).
- Google's Geocoding API is used, limited to Puerto Rico. Confidence comes from Google's result type: `ROOFTOP` = high; `RANGE_INTERPOLATED` and `GEOMETRIC_CENTER` = medium; `APPROXIMATE` = low. Coordinates outside the island are rejected.

### Configuration (server administrator)
`GoogleMaps` section (off by default):

| Key | What it is |
|---|---|
| `GoogleMaps:Enabled` | `true` to look up locations. |
| `GoogleMaps:ApiKey` | The key. **Never in the repository**: environment variable `GoogleMaps__ApiKey`. A placeholder (text starting with `<`) is treated as “no key”. |
| `GoogleMaps:MonthlyCap` | **Monthly cap of location lookups for route stops** (2026-10-22), above which no more lookups are made that month. Default 9,000: the Google Cloud console shows the first 10,000 Geocoding API queries per month as free. `0` = nothing is looked up. It belongs to the application (all companies add up to the same cap, the way Google bills the project), not to a screen. |
| `GoogleMaps:MinConfidence` | `LOW` (accepts everything; default), `MEDIUM` or `HIGH`. |
| `GoogleMaps:TimeoutSeconds` | Maximum time of the single query (30). |
| `GoogleMaps:PollSeconds` | How often, in seconds, the background job checks what is pending (5). |

The key and the consignee's address **are not written to the server log** (the standard HTTP client logging of this service was removed).

## API
| Method and route | Permission | What it does |
|---|---|---|
| `POST /api/v1/orders/{publicId}/delivery` | `orders.deliver` + LTL_GROUND module | Records the delivery. Body: `recipientName` (required), `notes`, `deliveredAtUtc`. Returns the proof. |
| `GET /api/v1/orders/{publicId}/delivery` | `orders.view` + LTL_GROUND module | The proof, or **204** if the shipment does not have one yet. |

The proof includes: `recipientName`, `notes`, `deliveredAtUtc`, `source` (`STAFF_WEB`), `signatureKind` (`USER` = automatic signature of the user), `registeredByName`, `signedByName`, `geoStatus` (`NONE`, `FROM_STOP`, `PENDING`, `RUNNING`, `OK`, `NOT_FOUND`, `FAILED`), `geoAccuracy`, `latitude`, `longitude` and the shipment status.

## Error messages
| Message | HTTP | When |
|---|---|---|
| Enter who received the delivery. | 400 | `recipientName` empty. |
| The name of who received it cannot exceed 150 characters. | 400 | Name too long. |
| The note cannot exceed 500 characters. | 400 | Note too long. |
| The delivery date and time cannot be in the future. | 400 | `deliveredAtUtc` more than 5 minutes ahead. |
| Shipment not found. | 404 | The shipment does not exist (or belongs to another company). |
| Shipment {n} already has its delivery recorded. | 409 | There is already a current proof. |
| Shipment {n} is in a final status ({status}): its delivery is not recorded. | 422 | Delivered, Returned, In warehouse or Cancelled. |
| Shipment {n} is Pick up: it ends “In warehouse” and is not delivered. | 422 | Service type Pick up. |
| The “Delivered” status is not enabled for this company. | 422 | The company disabled it in its status model. |
| (no permission) | 403 | `orders.deliver` missing. |

## Frequent cases
- **I don't see “Register delivery”.** The `orders.deliver` permission is missing, or the shipment is already in a final status, or it is inactive.
- **The proof says “No location”.** Google is off on this server, the stop has no address or it could not be found; the delivery is valid anyway.
- **I want the consignee to sign.** That belongs to the driver app (it does not exist yet); from the web the signature is always the user's.

## Signature and photos of the proof (Batch 127)
The proof panel has **“Signature and photos”**: the images and documents attached to the proof (the signature of the person who received it when it comes from the driver app; the delivery photos). With `orders.deliver` you can **Add photo** (JPG, PNG, WEBP, GIF or PDF, up to 10 MB) and **Remove**. They are stored encrypted and can only be seen with permission: see [chapter 38](38-almacen-de-archivos.md).
