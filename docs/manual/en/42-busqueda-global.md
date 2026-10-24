# 42 — Global search (“Search or run”)

Batch 167 (2026-10-21, dictated by Luis). The **Search or run…** field in the top bar (shortcut `/` or `Ctrl/⌘+K`) now, besides opening menu screens, **searches records by their number or their name** and takes you to the corresponding screen.

## What it does
- You type a number or a name (from 2 letters). The results appear **at the top**, grouped by type (Shipments, Clients, Warehouses…); below them follow the menu screens that match.
- **If there is a single result and it is exact** (for example, the full number of a shipment was typed, 4 letters or more), **it opens by itself**. If there is one exact and other similar ones, or several, or a single similar one, **they are shown to choose from** (↑↓ and Enter, or click).
- Numbers are searched by **how they start** (`ORD-10` finds `ORD-100` and `ORD-1000`; the exact one comes first). The names of clients, warehouses, products and suppliers are searched **inside** the name. It is fast on purpose: each type returns few results.
- Each type is searched **only if the user can see it** (the permission of the corresponding screen and the module turned on). Whoever lacks `orders.view` does not see shipments in the results, even if the number exists.

## What is searched and where it leads
| You type | Finds | Opens |
|---|---|---|
| Shipment number, waybill, packing (EMP-), client invoice number, package or piece number, client reference | Shipments (`orders.view`, Transport module) | The shipment record |
| Route number (e.g. `2026-0001` or `M-0011…`) | Routes (`trips.view`) | The route monitor |
| Claim number (`RCL-`) | Claims (`claims.view`) | The claims list filtered by that number |
| Client number or name | Clients (`clients.read`) | The client record |
| Warehouse, zone or bin code or name | Warehouses, zones and bins (`inventory.view`, Warehouse module) | The warehouse / the bin list of its zone |
| SKU (part number), barcode, name or part of the product name | Products | The product record |
| Receipt number (`REC-`), pick batch or packing number (`EMP-`, `DMA-`), exit notice number (`SAL-`) | Warehouse documents | Their screen |
| Arrival notice number (`LLE-`, since 2026-10-21) | Arrival notices | “Arrivals & departures” with the list filtered by that number |
| Supplier name | Suppliers (`purchasing.view`) | The supplier list filtered by that name |
| Purchase order (`PO-`) / sales order (`OV-`) / invoice (`FAC-`) / rental (`REN-`) number | Purchases, sales, billing, rentals | Its record (the invoice, the filtered list) |
| **Phone** (any format: `(787) 555-0142`, `+1 787 555 0142`, `7875550142`) | The owner of the contact: client, client person, consignee, shipment, or supplier | Its screen (if the user can see it) |

The lists that open **filtered** (claims, invoices, suppliers, consignees) read the filter from the address (`?q=`).

## How it works (support)
- `GET /api/v1/search?q=text` (any authenticated user; no permission of its own). Responds `{ query, hits[] }`: each result carries `type`, `id`, `title`, `subtitle`, `matchedBy` (which field it matched on), `exact` and extra `keys`. Where each type leads is decided by the web app (`kernel/search/searchRules.ts`).
- Fewer than 2 letters: empty response. At most 40 results, 6 per type, the exact ones first.
- Indexes that make it fast: the ones on each number (already existed) and two new ones in `Diseño/logistica-db-update.sql` (Batch 167): `IX_ContactPoint_Value` (phones) and `IX_WarehouseBin_Code` (bins).
- The phone is compared by **digits** (phones are stored without formatting, Batch 162); with a “+prefix” or 11 digits starting with 1 the prefix is removed.

## What it does not search yet
- **Arrival notices** (their `Number` does not exist yet; another session is doing it) and **serial numbers / lots** (left out for now, at Luis's request).
- **Part number** is the product SKU (there is no other field).

## Frequent cases
| Question | Answer |
|---|---|
| “I typed a shipment number and it does not show up.” | Check that it starts the same way (it is not searched by the end of the number) and that your user has `orders.view` and the Transport module. |
| “I typed the full number and it did not open by itself.” | It opens by itself only with a single exact result. If there is another shipment (from another client) with the same number, both are shown to choose from. |
| “I searched for a phone and it does not appear.” | Type it in full (7 digits or more) and check that the contact is active and that your user can see the owner (client, consignee, shipment). |
