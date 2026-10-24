# Chapter 06 — Inventory and warehouse (Batch 6; Warehouses and locations extended in Batch 11; Products and Purchases extended in Batch 12; Receiving, Tasks and Picking & packing extended in Batch 13; Inventory and Cycle count extended in Batch 14; Warehouses, Receiving and Tasks extended in Batch 16: receipt direct to bin; Cycle count extended in Batch 21: count by product, correction, preview, bulk close and provisional bins; Warehouses and locations extended in Batch 23: bin sheets; Products extended in Batch 26: convert to serial)

This chapter describes Warehouses and locations, Products and categories, Inventory (balances, movement ledger with summary and detail, adjustments,
transfers, automatic reconciliation with discrepancies, genealogy and serial trail), Receiving (arrival notices and receipts, including
receiving against a purchase order), the warehouse Task queue (directed putaway and replenishment), Cycle count,
ad hoc Picking & packing, minimal Purchases (suppliers, purchase orders and shortages) and Cross-dock (appointments
and plans, in demo mode). Each section states **what it does**, **who can**, **how it is used**, the **validations**
with the exact message and the HTTP code, and the **statuses** with their transitions and effects. The messages are
verified against the code (`src/Teikem.Domain/Wms/*.cs`, `src/Teikem.Infrastructure/Wms/*.cs`,
`src/Teikem.Domain/Catalogs/PostalLocality.cs`, `src/Teikem.Infrastructure/Services/PostalLocalityService.cs`,
`src/Teikem.Infrastructure/Wms/{InventoryLedger,InventoryReconciler,InventoryReconciliationWorker}.cs`,
`src/Teikem.Api/Controllers/PostalLocalitiesController.cs`,
`src/Teikem.Infrastructure/Services/{Warehouse,WarehouseLayout,Product,ProductCategory,InventoryRead,
InventoryAdjustment,Traceability,InventoryReconciliation,Asn,Receipt,WarehouseTask,Replenishment,CycleCount,PickBatch,Supplier,
PurchaseOrder,PurchaseOrderReceiving,PurchaseShortage,DockAppointment,CrossDock}Service.cs`,
`src/Teikem.Infrastructure/Services/{PutawayTaskHandler,ReplenishTaskHandler,CountTaskHandler,CrossDockTaskHandler,
CrossDockReceiptParticipant,WarehouseTaskStatusEffect,DockAppointmentStatusEffect}.cs`,
`src/Teikem.Api/Controllers/{Warehouses,Products,ProductCategories,Inventory,InventoryDiscrepancies,Receipts,Asns,WarehouseTasks,
Attention,CycleCounts,PickBatches,Suppliers,PurchaseOrders,DockAppointments,CrossDockPlans}Controller.cs`). The questions and
answers for each message are in [faq.md](faq.md).

Conventions of the chapter:

- Headers with their own history (`Warehouse`, `Product`, `Asn`, `ReceiptHeader`, `PurchaseOrder`, `PickBatch`)
  are identified by `publicId` (GUID) in the URL. Children without `TenantId` (`WarehouseZone`, `WarehouseBin`,
  `WarehouseDock`, `InventoryLot`, `PurchaseOrderLine`, `ReceiptLine`, `CycleCountLine`, `PickBatchLine`,
  `CrossDockAllocation`) are exposed by integer id **always under their already filtered parent**: a bin of another
  warehouse, a line of another receipt or an allocation of another plan respond **404** as if they did not exist (BOLA
  by child id, no oracle). `WarehouseTask`, `CycleCount` and `DockAppointment` have no `PublicId`: they are exposed by
  integer id under the tenant filter.
- No request carries `TenantId`: it always comes from the session (the JWT `tid`). Nor does `InventoryScope` exist in
  the body of any request: in this batch all internal reads use the "all inventory" scope
  (`InventoryScope.Any`); the scope by owner client (`OwnerClientId`) is left ready as a seam for the Portal
  of Batch 8.
- `InventoryLedger` is the **only** write path for `StockBalance`, `InventoryTransaction` and the status/
  location of `InventorySerial`. `InventoryTransaction.Quantity` is stored **signed**: a receipt enters as
  positive, a dispatch or a cross-dock leaves as negative, an adjustment enters or leaves according to what was captured, and a
  transfer is a single row with different origin and destination. The services always pass the **magnitude**
  (greater than zero); the ledger decides the sign.
- A validation error responds `400` with `title` and, when applicable, `errors` (`{ field: [message] }`); a
  conflict (duplicate, concurrency clash, number already taken) `409` `conflict`; insufficient inventory `409`
  `insufficient_stock`; a status, capacity or "not yet applicable" rule `422` `status_rule`; a foreign
  or nonexistent resource `404` `not_found`; missing permission `403` `forbidden` (`Missing permission '{code}'.`); a
  module that is off `403` `module_disabled` (`The '{key}' module is not enabled for this company.`).
- Modules: **WMS_LOTSERIAL** (on by default) covers warehouses, products, inventory, receiving, tasks,
  count and picking. **PURCHASING** (on by default) covers suppliers, purchase orders and shortages.
  **CROSSDOCK** (off by default, demo) covers dock appointments and cross-dock plans; the tenant turns it
  on from Tenant settings (sensitive action, requires recent reauthentication).
- "Product owner" (`Product.ClientId`, a 3PL client or `NULL` = the tenant's own) is a concept distinct
  from the tenant (`TenantId`, who owns the **data**). "Receipt" is `ReceiptHeader`/`ReceiptLine`; "Location" or
  "Bin" is `WarehouseBin`.
- **Number format (Batch 16).** On screen, quantities and money use a **thousands comma and decimal point** (Puerto
  Rico format): `61,023` and `1,250.5`; money carries `$` (`$1,234.50`). Before, in Spanish, a figure like `61.023` read as a decimal and 4-digit ones
  had no separator. It is presentation only: the API keeps returning JSON numbers (`61023`, `1250.5`) and accepts them the same way. Exports to
  Excel keep the number as a number.

---

## 1. Warehouses and locations

What it does: maintains the warehouse and its physical hierarchy — typed zones, bins (aisle-rack-level-position) and
docks. The warehouse is born **ACTIVE** and its deactivation is **final** (terminal status): it only proceeds when empty and with no
open documents. With a single active warehouse, the other screens use it by default; with more than one, it must be
indicated. Since Batch 11 (Warehouse changes, round 1) also: each bin can have a **maximum capacity** in
units and, with it, an **occupancy** state; each zone shows its occupancy (calculated, not stored); the **code of
a zone can be edited**; the bin list is **paginated** and is searched by code, zone, aisle, rack, level or
position; and there is a **postal locality list** to fill in the warehouse's city, state, postal code and
country. Since Batch 16, each warehouse also has a **receiving mode** (with putaway or direct to bin) and a **default receiving
bin** (section 1.4); the direct receipt is described in section 4.1.

Who can: `inventory.view` (list and look up warehouses, zones, bins and docks); `warehouse.manage`
(create, edit, deactivate/reactivate a warehouse, zones, bins and docks, and the dock's manual status). Module
**WMS_LOTSERIAL**. The postal locality list (`GET /api/v1/postal-localities`) only asks for a signed-in
session: it requires no permission or module.

How it is used:
- `GET /api/v1/warehouses?includeInactive=` — by default only the active ones, with counts of zones/bins/
  docks, stock and `zoneTypeCodes` (**distinct** zone type codes of its active zones, sorted; a zone
  without a type contributes no code).
- `POST /api/v1/warehouses` — `{ "code": "ALM-02", "name": "Almacén secundario", "line1": "...", "city": "Toa Baja",
  "state": "PR", "postalCode": "00949", "country": "PR" }`. The default country is `PR`. The city, the state and the
  postal code are free text for the API: the locality list is an entry aid of the screen, not a
  validation.
- `GET /api/v1/warehouses/{publicId}`, `PATCH /api/v1/warehouses/{publicId}` (inline edit; `null` = no
  change; `rowVersion` optional).
- `POST /api/v1/warehouses/{publicId}/deactivate` — final deactivation.
- Zones: `GET/POST /api/v1/warehouses/{publicId}/zones` (`GET ?includeInactive=`), `PATCH .../zones/{zoneId}`
  (`{ "code": "PCK-2", "name": "Picking 2", "zoneType": "PICKING" }`; all optional, `null` = no change,
  `"zoneType": ""` removes the type), `POST .../zones/{zoneId}/deactivate|reactivate`.
- Bins: `GET/POST /api/v1/warehouses/{publicId}/bins` (filters in section 1.2),
  `PATCH .../bins/{binId}` (`{ "aisle": "A9", "maxCapacityQty": 40 }`; `"clearMaxCapacity": true` removes the capacity),
  `POST .../bins/{binId}/deactivate|reactivate`.
- Docks: `GET/POST /api/v1/warehouses/{publicId}/docks`, `PATCH .../docks/{dockId}`, `POST .../docks/{dockId}/
  status` (manual status), `POST .../docks/{dockId}/deactivate|reactivate`.
- Postal localities: `GET /api/v1/postal-localities?search=&take=` (section 1.3).

### 1.1 Zones: editable code and occupancy

**Editable code.** `PATCH .../zones/{zoneId}` accepts `code`. It is trimmed and stored in uppercase, with the same
rules as on creation (letters, numbers, hyphen and underscore; maximum 30) and remains **unique within the warehouse**. Bins
and balances point to the zone by its id, so changing the code does not move bins or stock. What
cannot be changed is the zone's warehouse: sending `warehouseId` in the `PATCH` responds 400. The message
`El código de la zona no se puede cambiar.` **no longer exists**. Sending the same code the zone already has changes
nothing. Editing a zone of a deactivated warehouse responds 422 (`The warehouse is deactivated; it can only be viewed.`).

**Zone occupancy.** `GET .../zones` (and the warehouse record, `GET .../{publicId}`) returns per zone figures
calculated in a single query; the zone's capacity is **not stored**:

| Field | What it is |
|---|---|
| `binCount` | **Active** bins of the zone |
| `occupiedBinCount` | Active bins with on-hand stock greater than zero |
| `capacityQty` | Sum of the capacity of the active bins **that have a capacity** |
| `qtyOnHandInCapacityBins` | On-hand stock of those same bins (the numerator of the percentage) |
| `qtyOnHand` | On-hand stock of **all** the bins of the zone |
| `binsWithoutCapacity` | Active bins with no capacity set (they are left out of the percentage) |

Zone occupancy percentage = `qtyOnHandInCapacityBins / capacityQty`. With `capacityQty = 0` there is no percentage: the
screen shows the total stock and "no capacity set". The stock used is the **on-hand** one (it includes what is
reserved).

### 1.2 Bins: maximum capacity, occupancy and paginated list

**Maximum capacity.** `maxCapacityQty` is an integer greater than zero (product units that fit in the bin) or empty
(`null` = no capacity set). It is sent on creation (`POST .../bins`) and on edit (`PATCH .../bins/{binId}`); in the
`PATCH`, `null` leaves the capacity as it is and `"clearMaxCapacity": true` removes it (if both come, removing wins). In the database
it is protected by `CK_WarehouseBin_MaxCapacityQty` (null or greater than zero).

**Provisional bin (Batch 21).** A bin created from a count by the counter (`POST /api/v1/cycle-counts/{id}/bins`, see the
Cycle count chapter) is born with `isProvisional = true` (and `provisionalCycleCountId`, `provisionalCreatedAtUtc`): it is used right away, but it remains
visible for review. The list accepts `?isProvisional=true|false`; the supervisor confirms it with
`POST .../bins/{binId}/confirm-provisional` (`warehouse.manage`; if it is not provisional, 409 `The bin is not pending review.`),
edits it with `PATCH` or deactivates it. The database stores it in `WarehouseBin.IsProvisional`, `ProvisionalCreatedBy`, `ProvisionalCreatedAtUtc` and
`ProvisionalCycleCountId`.

**Occupancy state** (`occupancy`). It is a calculation, not a stored status: it is not in a list and has no
transitions. It compares the bin's on-hand stock with its capacity:

| Code | Label on screen | When |
|---|---|---|
| `EMPTY` | Empty | On-hand stock equal to zero |
| `PARTIAL` | Partial | There is stock and it is less than the capacity |
| `FULL` | Full | There is stock and it is equal to or greater than the capacity |
| `NO_CAPACITY` | In use, no capacity | There is stock and the bin has no capacity |

**List** — `GET /api/v1/warehouses/{publicId}/bins` responds `{ "total": 3029, "skip": 0, "take": 100, "items": [...] }`
(before Batch 11 it was an array). `skip` minimum 0; `take` 100 by default, maximum 200 (0 or less = 100; more than 200 =
200). It always sorts by code and, for equal codes, by id. Filters (all optional, combined with "and"):

| Parameter | What it does |
|---|---|
| `search` | Text **contained** in the code, the zone code, the aisle, the rack, the level or the position (case-insensitive). Before, it only looked at code and zone |
| `zoneId` | One zone; if it is not of this warehouse it responds 404 |
| `zoneIds` | Several zones (repeat the parameter); a foreign id simply returns no bins |
| `aisle`, `rack`, `level`, `position` | Each part **contains** the text |
| `productPublicIds` | Bins with stock of any of those products |
| `occupancy` | One or several of `EMPTY`, `PARTIAL`, `FULL`, `NO_CAPACITY` (repeat the parameter or separate them with commas) |
| `binIds` | Only those bins |
| `includeInactive` | Includes the deactivated ones (not by default) |
| `onlyWithStock` | Only with on-hand stock greater than zero |

Example: `GET /api/v1/warehouses/{publicId}/bins?search=01-A&occupancy=PARTIAL&occupancy=FULL&skip=0&take=50`.

Each bin brings `maxCapacityQty`, `occupancy`, `qtyOnHand`, `productCount` and, when `productCount` is exactly 1,
`singleProductPublicId`, `singleProductSku` and `singleProductName`.

**Bulk capacity** — `POST /api/v1/warehouses/{publicId}/bins/capacity` (permission `warehouse.manage`, module
`WMS_LOTSERIAL`, same as editing a bin). Sets or removes the capacity of **all** the bins of the warehouse that match
the filters (without paging), in a single transaction: either all change or none. Body:

| Field | What it does |
|---|---|
| `zoneIds`, `aisle`, `rack`, `level`, `position`, `search`, `binIds`, `includeInactive` | The same filters as the list, with the same meaning (the parts and `search` "contain"; without `includeInactive` the deactivated ones are left out; a zone or bin id of another warehouse matches nothing) |
| `onlyWithoutCapacity` | Only the bins that currently have no capacity: it does not overwrite any capacity already captured |
| `allBins` | Mandatory (`true`) when no bin filter is sent: avoids changing the whole warehouse by accident |
| `maxCapacityQty` | Capacity to set (integer greater than zero) |
| `clear` | `true` = remove the capacity (they are left "without capacity") |

Exactly one of `maxCapacityQty` or `clear: true`. It responds `{ "matched": 12, "changed": 9 }`: how many bins
match the filters and how many actually changed (those that already had that capacity are not touched). Each changed bin is
recorded in the audit log as a warehouse change, the same as when editing them one by one. **To know in advance
how many will be affected**, request the list with the same filters and `take=1`: its `total` is exactly `matched`
(`GET .../bins?zoneIds=3&aisle=01&take=1`). Examples: `{ "zoneIds": [3], "aisle": "01", "maxCapacityQty": 40 }`;
`{ "allBins": true, "onlyWithoutCapacity": true, "maxCapacityQty": 50 }`; `{ "binIds": [101, 102], "clear": true }`.
Warehouse of another company → 404; deactivated warehouse → 422 `The warehouse is deactivated; it can only be viewed.`

The body is validated **before** looking up the warehouse: an invalid body responds 400 even if the warehouse does not exist or is
deactivated. If there are several errors at once (for example, no value and no filters), the 400 brings them all, each in its field.
The messages are in the validations table of this chapter. On screen, the **Set capacity** button of Bins and of
the Bins tab of the warehouse record builds this same body (see the screen manual, Bins section).

**Estimated capacity from the migration (Batch 12).** Advance Depot's bins arrive from the migration with an estimated capacity
from the history of the previous WMS: the highest total each bin ever had, rounded up to the ten (minimum 10); without
history, the median of its aisle, zone or warehouse (only if that median comes from at least 5 bins with history).
Advance Solutions receives no capacity. The migration never overwrites a capacity already captured. It is an **estimate**: correct it with bulk
capacity or by editing the bin. The complete rule, the `-cupos.csv` report and the behavior of `--update` are in
[chapter 10, section 4](10-migracion-de-datos.md).

### 1.3 Postal locality list

`GET /api/v1/postal-localities?search=toa%20baja&take=30` returns an array of
`{ id, city, postalCode, state, countryCode, country, municipality }`. Read-only, global (it does not depend on the company).

- **Content.** 42,522 postal codes from the USPS list: 42,346 from the United States and its territories (`countryCode`
  `US`) and 176 from Puerto Rico (`PR`). `city` is the official postal name of the ZIP, in uppercase and without accents
  (`SABANA SECA`, `NEW YORK`). Only in Puerto Rico does `municipality` come (with accents): 00952 is `SABANA SECA` of the
  municipality `Toa Baja`; 00631 is `CASTANER` of the municipality `Lares`. `country` is the name of the country in the user's language.
- **Search.** The text is compared ignoring case and accents (`mayaguez` finds `Mayagüez`). If it only has digits
  (and hyphen) it searches by **postal code prefix** (`0094` → 00949, 00950…). If it has letters it searches in the postal
  city **and** in the municipality: first those that start with the text, then those that have a word that starts with
  it, then those that contain it. For equal matches, Puerto Rico goes first. Without `search` it returns the first ones by
  city.
- **Size.** `take` 20 by default, maximum 100; the screen requests 30.
- **Memory.** The list is kept in the API's memory for one hour: a direct change in the table can take up to an
  hour to be seen.
- **How the screen uses it.** When choosing a locality, the warehouse form stores `city` (the **municipality** if there
  is one —Puerto Rico—, otherwise the USPS postal city), `postalCode`, `state` and `country`. The warehouse stores text: there is no
  key to the locality table.

### 1.4 Receiving mode and default receiving bin (Batch 16)

What it does: decides **where the goods go in** when a warehouse receipt is confirmed.

| Code | Label | Where the goods go in on confirmation | Putaway tasks |
|---|---|---|---|
| `PUTAWAY` | With putaway | In the **receiving bin** (`STAGING` zone) | One per line; put away afterwards |
| `DIRECT` | Direct to bin | In the **destination bin of each line** | None |

The default mode is `PUTAWAY` (a warehouse with no value is treated this way). **Advance Solutions** (`ALM-SOL`), which only has the `GENERAL` bin and
no receiving zone, was migrated in `DIRECT`; **Advance Depot** (`ALM-DEPOT`) and the demo (`ALM-01`), in `PUTAWAY`. The warehouse's mode is the one for
**new** receipts: each receipt stores the mode it was opened with (section 4.1). **Changing it does not touch open receipts or pending putaways**: the
receipts that were already open continue as they were opened and the putaway tasks run their course.

**Default receiving bin.** A bin of a `STAGING` or `CROSSDOCK` zone of the warehouse. It is used by receipts **with putaway** (when the
header does not indicate another one) and by cross-dock lines of a direct receipt (section 4.1). If the warehouse does not have one, or the one it has stopped being valid
(the bin or its zone was deactivated), **the first active bin of a `STAGING` zone** applies, sorting by zone code and bin code. It is set
after creating the warehouse (on creation there are no bins yet) and can be removed. In Advance Depot it is `R1` (zone `STG`), not the first by zone code
(`S1` of "Shipping", which was what the system chose before Batch 16).

Who can: `warehouse.manage` (set the mode and the default bin); `inventory.view` (read them). Module **WMS_LOTSERIAL**.

How it is used:
- Create: `POST /api/v1/warehouses` with `"receivingMode": "DIRECT"`. Without `receivingMode` it is left as `PUTAWAY`.
- Edit: `PATCH /api/v1/warehouses/{publicId}` with `{ "receivingMode": "DIRECT", "defaultReceivingBinId": 12, "rowVersion": "…" }`. `receivingMode`
  absent or `null` = no change (`PUTAWAY` or `DIRECT` are valid, case-insensitive). `"clearDefaultReceivingBin": true` removes the default bin.
- Read: `GET /api/v1/warehouses` and `GET /api/v1/warehouses/{publicId}` bring `receivingModeCode` (`PUTAWAY` or `DIRECT`), `receivingMode` (the label
  in the user's language), `defaultReceivingBinId` and `defaultReceivingBinCode` (`null` = the first `STAGING`).
- Devices: the device's enrollment and heartbeat bring `defaultWarehouseReceivingMode` (the mode of the device's default warehouse; chapter 9, section 4).
- Migration (`import-legacy`): the keys `warehouse.receivingMode` (`PUTAWAY` or `DIRECT`) and `warehouse.defaultReceivingBin` (code of a receiving bin of the
  warehouse) **are only applied when creating the warehouse**: `--update` does not overwrite them.
- Analysis: the warehouse and receipt sources expose the fields "Receiving mode" (`ReceivingMode`) and "Receiving mode code" (`ReceivingModeCode`).

Screen: warehouse record → **Details** → **Receiving** section ([F6 — Warehouses](frontend/f6-almacen-e-inventario.md#warehouse-record)).

### 1.5 Products by bin (report)

What it is: a list on **letter-size paper**, with the **same format as the “Barcodes” report** (branded header and footer, 3-column grid with several codes
per page; 2026-10-10): each bin opens a group with its title (“Bin A-01 · Zone A · Aisle 01 — 3 product(s)”) and below, in cells, the barcode of each
product it holds (SKU in bold, name and, if the product has its own barcode, that one; otherwise the SKU). It is meant to be printed so you can
**scan the codes from the paper** (for example, on a 20 to 30 foot rack where the product or its label cannot be read). It is a
list of what there is **at the moment it is generated**: it stores no state (it does not record when it was printed nor warn when a list changes).
It replaces the "bin sheets" of Batch 23 (see `docs/lote24-decisiones.md`), which were removed.

Who can: `inventory.view`. Module **WMS_LOTSERIAL**, like the rest of Warehouses and locations.

**How it is used:**

- `GET /api/v1/warehouses/{publicId}/bin-products` accepts **the same filters** as the bin list (`search`, `zoneId`, `zoneIds`,
  `aisle`, `rack`, `level`, `position`, `productPublicIds`, `occupancy`, `binIds`, `includeInactive`, `onlyWithStock`, `isProvisional`) and pages with
  `skip` and `take` (50 by default, maximum **200**: more than 200 is 400). It sorts by bin code. It responds
  `{ total, skip, take, generatedAtUtc, items }`; each bin brings `binId`, `code`, `zoneId`, `zoneCode`, `aisle`, `rack`, `level`, `position`,
  `isActive` and `products`: one row **per product** (lots are not repeated) with on-hand stock greater than zero, sorted by SKU, with
  `productPublicId`, `sku`, `name` and `barcode` (empty if the product has none). The report carries no quantities. A bin without products brings
  an empty `products`. `generatedAtUtc` is the instant the data was read.
- To print a whole aisle: `GET .../bin-products?aisle=A&take=200` and, if `total` is greater than 200, continue with `skip=200`, `skip=400`…

Stored data: nothing new. The columns `WarehouseBin.SheetPrintedAtUtc` and `WarehouseBin.SheetContentChangedAtUtc` of Batch 23 were removed from the
schema and the model; a database that already has them can keep them (they are nullable and nobody reads them) or remove them with `ALTER TABLE dbo.WarehouseBin DROP COLUMN
SheetPrintedAtUtc, SheetContentChangedAtUtc;`.

Frequent cases:
- *"A bin does not appear in the report."* If it has no products with stock it carries no codes: with **Include empty bins** it is NAMED in a notice of the
  PDF (“Bins without products (N): …”) and without it, it is only counted. If everything chosen is empty nothing is generated. A product whose total in the bin, adding lots, is 0 does not count.
- *"I moved goods and the paper is outdated."* The report does not warn: generate it again when you need it.

**No printing limit (2026-10-10):** all the bins (or labels, in bin labels and product labels) that are requested are printed. If there are more than 500, the screen only
warns “That is N: the PDF will be large and may take a moment. All of them are printed; there is no limit.” and does not block the button. The read for exporting and for reports **has no row limit**: it reads all the rows of the filter and, if
it were ever cut off, it says so (“Only the first N rows were exported…”).

**“N products” in the Bins table (2026-10-10):** when a bin has more than one product, the *Product* column says “N products” and that text is a link that opens
the **Products in {bin}** window with SKU, product, lot, on hand and available (`GET /inventory/balances?binIds=`).

**Export Bins (2026-10-10):** a bin with several products comes out in **several lines, one per product** (with a *SKU* column; the *On hand* column carries what that
product has in that bin, lots added up). Those with 0 or 1 product come out in a single line as always.

Screen: Warehouse → **Bins** — **Products by bin** button, checkboxes and **Aisle** filter
([F15 — Products by bin](frontend/f15-productos-por-posicion.md)).

### 1.6 Inventory exit order (2026-10-05)

`GET /api/v1/inventory/exit-options?warehousePublicId=…&productPublicIds=…&skip=&take=` (`inventory.view`, module WMS_LOTSERIAL) returns, **per product**, where it should leave from in the SAME order in which picking assigns it
(`PickBatchRules.Eligible`, FEFO D14 by default —or FIFO if the company or the product configures it, §2.2—: expires first —those without expiration at the end—, then zone type picking < reserve < refrigerated < preparation and bin code). Each row: `productPublicId`, `binId`, `binCode`, `zoneCode`,
`zoneTypeCode`, `lotId`, `lotNumber`, `expiryDate`, `available` (on hand − reserved) and `rank` (1 = leaves first, restarts per product). Only **active** and pickable bins (no quarantine or cross-dock) with available greater than
zero, of active products. `warehousePublicId` is mandatory (400 `Choose the warehouse.`; 404 `Warehouse not found.` if it does not exist or belongs to another company); `productPublicIds` empty = all; page `{ total, skip, take, serverTimeUtc, items }`,
`take` ≤ 500 (500 by default). **It is the only implementation of that rule**: the warehouse app downloads it to the device and queries it online; no screen sorts again.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| `code` empty (creating a warehouse, zone or dock; also zone `PATCH` with `"code": ""`) | `The code is required.` | 400 |
| `code` invalid or > 30 | `Code only allows letters, numbers, hyphen and underscore (max 30).` | 400 |
| `name` empty | `The name is required.` | 400 |
| Repeated warehouse code | `A warehouse with that code already exists.` | 409 |
| Repeated zone code in the warehouse (on creation **or when editing the code**) | `A zone with that code already exists in the warehouse.` | 409 |
| Repeated bin code in the warehouse | `A bin with that code already exists in the warehouse.` | 409 |
| Repeated dock code in the warehouse | `A dock with that code already exists in the warehouse.` | 409 |
| Warehouse `PATCH` with `code` | `The warehouse code cannot be changed.` | 400 |
| Dock `PATCH` with `code` | `The dock code cannot be changed.` | 400 |
| Zone `PATCH` with `warehouseId` | `The zone cannot be moved to another warehouse.` | 400 |
| Bin `PATCH` with `code` or `zoneId` | `The bin's code and zone cannot be changed.` | 400 |
| Bin without code or parts | `Enter the bin's code or its aisle/rack/level/position.` | 400 |
| Bin part invalid or > 20 | `Aisle, rack, level and position only allow letters, numbers, hyphen and underscore (maximum 20 each).` | 400 |
| Bin code > 40 or invalid | `The bin code only allows letters, numbers, hyphen and underscore (maximum 40).` | 400 |
| No zone when creating a bin | `Choose the bin's zone.` | 400 |
| `maxCapacityQty` ≤ 0 (creating or editing a bin, or bulk capacity; the error goes in `errors.maxCapacityQty`) | `The bin's max capacity must be greater than zero.` | 400 |
| Bulk capacity without `maxCapacityQty` or `clear: true` (`errors.maxCapacityQty`) | `Enter the max capacity (maxCapacityQty) or clear: true to remove it.` | 400 |
| Bulk capacity with `maxCapacityQty` and `clear: true` at the same time (`errors.clear`) | `Enter the max capacity or clear: true, not both.` | 400 |
| Bulk capacity with no bin filter and without `allBins: true` (`errors.allBins`) | `Enter at least one bin filter (zoneIds, aisle, rack, level, position, search or binIds) or allBins: true to apply it to the whole warehouse.` | 400 |
| Unknown `occupancy` in the bin list (the error goes in `errors.occupancy`) | `Unknown occupancy state: 'X'. Use EMPTY, PARTIAL, FULL or NO_CAPACITY.` | 400 |
| List `zoneId` that is not of this warehouse | `Zone not found.` | 404 |
| Unknown `zoneType` | `Unknown zone type: 'X'.` | 400 |
| `dockType` empty/unknown | `Enter the dock type (INBOUND, OUTBOUND or BOTH).` / `Unknown dock type: 'X'.` | 400 |
| Non-manual dock status | `Dock status not allowed: 'X'. Use FREE, OCCUPIED or MAINTENANCE.` | 400 |
| `maxWeightKg` ≤ 0 | `The weight capacity must be greater than zero.` | 400 |
| `maxWeightKg` with more than 3 decimals or ≥ 10⁹ | `The weight capacity allows at most 9 integer digits and 3 decimals.` | 400 |
| Unknown country | `Unknown country: 'X'.` | 400 |
| Deactivating a warehouse with inventory or open documents | `Warehouse {code} has inventory or open documents; it cannot be deactivated.` (`errors` per type: `inventory`, `receipts`, `cycleCounts`, `tasks`, `pickBatches`, `crossDockPlans`, `appointments`) | 409 |
| Warehouse already deactivated | `The warehouse is deactivated; it can only be viewed.` | 422 |
| Deactivating a bin with inventory | `Bin {code} has inventory; it cannot be deactivated.` | 409 |
| Deactivating a bin with open tasks | `The bin has open warehouse tasks; complete or cancel them before deactivating it.` | 409 |
| Deactivating a zone with active bins | `The zone has active bins; deactivate them first.` | 409 |
| Deactivating a dock with current appointments | `The dock has scheduled or ongoing appointments.` | 409 |
| Inactive zone/dock when using it | `The zone is inactive; reactivate it first.` / `The dock is inactive; reactivate it first.` | 422 |
| Warehouse of another tenant or nonexistent | `Warehouse not found.` | 404 |
| Zone/bin/dock of another warehouse (or of another tenant) | `Zone not found.` / `Bin not found.` / `Dock not found.` | 404 |
| Operation without `warehousePublicId` with more than one active warehouse | `Choose the warehouse: the company has more than one.` | 400 |
| No active warehouse at all | `The company has no active warehouses.` | 422 |
| Unknown `receivingMode` (creating or `PATCH` of a warehouse; the error goes in `errors.receivingMode`) | `Unknown receiving mode: 'X'. Use PUTAWAY or DIRECT.` | 400 |
| `defaultReceivingBinId` of another warehouse or nonexistent | `Bin not found.` | 404 |
| `defaultReceivingBinId` in a zone that is neither `STAGING` nor `CROSSDOCK` (the error goes in `errors.defaultReceivingBinId`) | `The receiving bin must be in a STAGING or CROSSDOCK zone.` | 400 |
| `defaultReceivingBinId` of a deactivated bin | `The receiving bin is deactivated.` | 422 |
| `take` greater than 200 in `GET .../bin-products` (in `errors.take`) | `At most 200 bins can be requested per query; use skip to request the next ones.` | 400 |
| Warehouse of another company or nonexistent in `bin-products` | `Warehouse not found.` | 404 |

A `maxCapacityQty` greater than 2,147,483,647 (the maximum of a 32-bit integer) is rejected by the screen; sent
directly to the API, what response it gives was not tested.

### Statuses and transitions

`WarehouseStatus`: **ACTIVE** (initial) → **INACTIVE** (terminal, final deactivation). There is no way back: an
inactive warehouse can only be viewed.

`DockStatus`: **FREE** (initial) ↔ **OCCUPIED** ↔ **MAINTENANCE** (all lateral to each other; `warehouse.manage`
sets them by hand). The arrival of a dock appointment (status `ARRIVED`) also occupies the dock (`FREE→OCCUPIED`,
effect `DockAppointmentStatusEffect`, section 9); closing it (`COMPLETED`, `NO_SHOW` or `CANCELLED`) frees it
(`OCCUPIED→FREE`) only if no other `ARRIVED` appointment remains on that dock. `MAINTENANCE` is never touched by the
automatic effect: only the warehouse manager by hand.

Zones and bins: active ↔ inactive (`IsActive`, no status domain of their own). They are deactivated with `warehouse.manage`;
the zone only without active bins and the bin only without inventory or open tasks (see the table above). The
**occupancy** of a bin (`EMPTY`, `PARTIAL`, `FULL`, `NO_CAPACITY`) is not a status: it changes on its own when the
stock or the capacity changes, has no transitions or effects, and blocks no action (deactivating the bin depends on
whether it has inventory, not on its occupancy).

---

### 1b. Import warehouses and bins with a template (2026-10-21)

3rd and 4th entities of the generic template engine (see §2b and §8b). **Who can:** `warehouse.manage` (Inventory with lots and serials module); to see the templates, `inventory.view`. It only **creates**: what already exists is rejected per row.

- **Warehouses:** Warehouses → **Import** (`POST /api/v1/warehouses/import`; templates in `/api/v1/warehouses/import-templates`, Kind `WAREHOUSE`). Fields: `code` and `name` (mandatory, always as a column), `line1`, `city`, `state`, `postalCode`, `country`, `receivingMode` (country, receiving mode, town, state and postal code accept a default value). Rules for each row: those of manual warehouse creation (§1).
- **Bins:** Locations (with a warehouse chosen) → **Import** (`POST /api/v1/bins/import`; templates in `/api/v1/bins/import-templates`, Kind `BIN`). Fields: `warehouseCode` and `zoneCode` (as a column or as a default value: a single-zone file), `code` or the parts `aisle`, `rack`, `level`, `position` (from which the code is derived), `maxWeightKg`, `maxCapacityQty`. The warehouse and the zone are looked up by their code (active). Rules for each row: those of manual bin creation (§1).

| Message | HTTP / where | What to do |
|---|---|---|
| “The template must include the code column.” · “…the name column.” | 400, warehouse template | Add those columns (they do not accept a default value). |
| “The template must include the warehouseCode column or a default value for warehouseCode.” · “…zoneCode…” | 400, bin template | Add the column or the default value. |
| “The template must include the code column or the bin part columns (aisle, rack, level, position).” | 400, bin template | How the bin code is built is missing. |
| “The warehouse code is repeated in the file.” · “The bin is repeated in the file.” | row | The same code twice in the file. |
| “Warehouse not found by that code.” · “Zone not found by that code in that warehouse.” | row (bins) | Check the warehouse or zone code (they must be active). |
| “Must be a number.” | row (bins) | Weight or max capacity not numeric. |
| The other rules of manual creation (repeated code, invalid code, inactive zone…) | row | See §1. |

## 2. Products and categories

What it does: maintains the SKU master (owned by the tenant or by a 3PL client), its tracking type (no lot/
serial, by lot or by serial), cost and price, inventory and picking minimums, and its hierarchical category.
It also exposes the lots and serials of each product.

Who can: `inventory.view` (list, record, lots, serials, categories); `inventory.manage` (create, edit, deactivate/
reactivate products and categories). Module **WMS_LOTSERIAL**.

How it is used:
- `GET /api/v1/products?search=&categoryIds=&ownerClientPublicId=&ownOnly=&activeOnly=&warehousePublicId=&
  onlyAvailable=&skip=&take=` (`take` ≤ 200). Batch 12 adds `warehousePublicIds`, `productPublicIds`, `name`, `brands`,
  `serialOnly` and `serialMissing` (see "Brand, model and list filters", below); since the 2026-09-30 adjustment, also
  `onlyOnHand`.
- `GET /api/v1/products/brands?search=` (Batch 12, `inventory.view`) — distinct brands of the company, for the Brand filter.
- `POST /api/v1/products` — `{ "sku": "PN", "name": "Producto normal", "trackingType": "NONE", "purchaseCost": 12.3456,
  "brand": "Acme", "model": "X-200" }` (`brand` and `model` are optional).
- `GET /api/v1/products/{publicId}`, `PATCH /api/v1/products/{publicId}`.
- `GET /api/v1/products/{publicId}/lots`, `GET /api/v1/products/{publicId}/serials?status=&search=`.
- `POST /api/v1/products/{publicId}/deactivate|reactivate`.
- `GET/POST /api/v1/product-categories`, `PATCH /api/v1/product-categories/{id}` (move/rename), `POST .../{id}/
  deactivate|reactivate`.

### Product packaging (2026-10-09)

**What it is.** Each product can have **one** pack: a packaging unit (Box, Barrel, Package, Bundle, Drum, Roll… from the editable
*Unit of measure* list) and how many base units it holds (Box = 12; Barrel = 50 gallons). **Inventory is always kept in the base unit**: the pack
is only used for counting and for reading quantities. Nothing that is stored changes.

**Who and where.** `inventory.manage`, inventory module: Products & inventory → *New/Edit product* → fields **Packaging (optional)** and
**Units per pack** (API: `packUom` and `packQty` in `POST/PATCH /api/v1/products`; `clearPack = true` to remove it). It can be changed even if the
product already has movements.

**Validations** (HTTP 400, field `packUom` or `packQty`):

| Case | Message |
|---|---|
| Units per pack ≤ 0 or with more than 3 decimals | `Units per pack must be a number greater than 0 (up to 3 decimals).` |
| Quantity without a unit | `Enter the pack unit (box, barrel, bundle…).` |
| Unit without a quantity | `Enter how many units the pack holds.` |
| Pack equal to the base unit | `The pack unit cannot be the same as the base unit.` |
| Nonexistent unit code | `Unknown pack unit: '{code}'.` |

**Where it shows.** **Pack** column of the list (“Box of 12”); *Available* shows “= 24 Box + 2” on hover; the count **calculator**
(web and app) and the calculator and the **Receive** entry in the app. The products data source brings `PackUom` and `PackQty` for
views and indicators. The app downloads the pack with the sync (the first sync after updating downloads the full products).

**Calculator with pack.** If the product has a pack, each block (rows × columns × depth) is counted in **Units** or in **Box (of 12)**,
and the **Loose boxes** field appears in addition to **Loose**. Example: 3 × 4 × 2 boxes = 24 boxes × 12 = 288 units, + 2 loose boxes (24) + 5
loose = **317**. What is saved is the total quantity in units; the breakdown is not saved.

### “Exclude unavailable” filter (2026-10-09)

In Products & inventory, a toggle next to the other filters. **On**, the list shows only the products with **available greater than 0** (on hand − reserved across all bins, or across the chosen warehouses): the same sum as the *Available* column. Off, it does not filter. It goes into *Export*, into the PDF reports and into the barcode report; *Clear* turns it off. API: `GET /api/v1/products?hasAvailable=true` (`inventory.view`). It is the complement of the “Unavailable” tile.

### Brand, model and list filters (Batch 12)

**Brand and model.** Two optional free-text fields of the product, up to 100 characters each. Leading and trailing
spaces are trimmed and an empty value is saved as "no value". There is no brand list: the brand is the text that
is typed, and `GET /api/v1/products/brands` returns the ones already used by the company's products (active and inactive),
without repeats even if they differ in case, sorted case-insensitively and up to 500. `?search=` filters the brands that
contain the text. Each list row and the record bring `brand` and `model`.
- Create (`POST`): `brand` and `model` are optional.
- Edit (`PATCH`): `brand` or `model` absent or `null` = **no change**; `""` (or only spaces) = **remove it**; any
  other text replaces it. They are audited like any other product field.

**Filters of `GET /api/v1/products`.** All are optional and combine with "and":

| Parameter | What it does |
|---|---|
| `warehousePublicIds` (several) and `warehousePublicId` | They are merged without repeats. **They narrow the quantities** (on hand, reserved, available, below minimum and serials) of each row to those warehouses; **they do not remove products** from the list. The exceptions are `onlyAvailable=true` and `onlyOnHand=true`, which do leave out the products with no available (or no stock on hand) in those warehouses. If any is not from your company or does not exist, it responds 404 `Warehouse not found.` |
| `productPublicIds` (several) | Only those products (the "SKU" filter of the screen) |
| `name` | The product name **contains** the text, case-insensitive |
| `brands` (several) | The brand is **equal** to one of those given, case-insensitive |
| `categoryIds` | No change: includes subcategories |
| `serialOnly=true` | Serial-tracked products **or** products that already have serial numbers registered |
| `serialMissing=true` | **Active** serial-tracked products whose stock on hand is **greater** than the count of their `AVAILABLE` or `RESERVED` serials (they have serials left to capture). With warehouses given, both quantities are measured in those warehouses. The screen counts it with `take=1` and reads `total` |
| `onlyOnHand=true` (2026-09-30 adjustment) | Only products with **stock on hand greater than zero**: the sum of `QtyOnHand` across all their bins, **including** quarantine, cross-dock and what is reserved. With warehouses given, the stock is measured in those warehouses and, like `onlyAvailable`, it **does remove** from the list the products with no stock there. With `activeOnly=true` it gives "active products with stock on hand": this is what the table shows when the "Total units" indicator is tapped |
| `unavailable=true` (2026-10-05, *Unavailable* tile) | Only **active** products whose available (on hand − reserved across all their bins, or across the given warehouses; no balance = 0) **is not greater than zero**. It is read with `take=1` and `total` |
| `activeOnly`, `onlyAvailable`, `belowMin` | No change. `onlyAvailable` does not count the stock in quarantine or cross-dock zones and subtracts what is reserved; that is why a product can appear with `onlyOnHand` and not with `onlyAvailable` |

`GET /api/v1/inventory/transactions` (ledger) also accepts `brands` and `name` with the same meaning; they combine with
`productPublicIds` and `categoryIds`. The Adjustment report of the Products & inventory screen uses them to honor the
table filters.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty `sku` | `SKU is required.` | 400 |
| `sku` > 60 | `SKU cannot exceed 60 characters.` | 400 |
| `sku` with spaces or control characters | `SKU cannot contain spaces or control characters.` | 400 |
| `PATCH` with `sku` | `The product SKU cannot be changed.` | 400 |
| Empty `name` / > 200 | `Product name is required.` / `Name cannot exceed 200 characters.` | 400 |
| `barcode` > 60 | `Barcode cannot exceed 60 characters.` | 400 |
| `brand` > 100 characters, already trimmed (create or edit; the error goes in `errors.brand`) | `Brand cannot exceed 100 characters.` | 400 |
| `model` > 100 characters, already trimmed (create or edit; the error goes in `errors.model`) | `Model cannot exceed 100 characters.` | 400 |
| `warehousePublicId` or any `warehousePublicIds` of the product list that does not exist or belongs to another company | `Warehouse not found.` | 404 |
| Repeated SKU for the same owner | `A product with that SKU already exists for that owner.` | 409 |
| Repeated barcode (active product) | `An active product with that barcode already exists.` | 409 |
| Negative cost/price | `Cost and price cannot be negative.` | 400 |
| Cost/price with more than 4 decimals | `{Field} admits at most 4 decimals.` | 400 |
| Cost/price out of range | `Cost or price exceeds the maximum allowed.` | 400 |
| Negative weight/volume | `Weight and volume cannot be negative.` | 400 |
| Weight with more than 3 decimals or ≥ 10⁹ | `Weight admits at most 3 decimals and must be less than 1,000,000,000.` | 400 |
| Volume with more than 4 decimals or ≥ 10⁸ | `Volume admits at most 4 decimals and must be less than 100,000,000.` | 400 |
| Negative minimums | `Minimums cannot be negative.` | 400 |
| `maxPickQty` < `minPickQty` | `The pick bin maximum must be greater than or equal to the minimum.` | 400 |
| `minPickQty` without a preferred bin in a PICKING zone | `A minimum pick quantity requires a preferred bin in a PICKING zone.` | 400 |
| Preferred bin from a warehouse other than the preferred one | `The preferred bin must belong to the preferred warehouse.` | 400 |
| Changing tracking, base unit or owner with movements (to move an untracked product to **serial** use "Convert to serial", section 2.1) | `Cannot change {the tracking type / the base unit of measure / the owner} of a product that already has movements.` | 409 |
| Deactivation with inventory on hand | `Product {sku} has inventory on hand ({qty}); it cannot be deactivated.` | 409 |
| Deactivation with open documents | `Product {sku} is in open receipts, pending tasks, pick batches or open counts; close them before deactivating it.` | 409 |
| Category: empty name / > 150 | `Category name is required.` / `Category name cannot exceed 150 characters.` | 400 |
| Category as its own ancestor | `A category cannot be its own ancestor.` | 400 |
| More than 5 levels | `Categories allow at most 5 levels.` | 400 |
| Repeated category name at the same level | `A category with that name already exists at that level.` | 409 |
| Deactivating a category with active products | `The category has active products.` | 409 |
| Deactivating a category with active subcategories | `The category has active subcategories; deactivate them first.` | 409 |
| Product from another tenant, nonexistent, or from another owner with `InventoryScope` set | `Product not found.` | 404 |

### Status

`Product.IsActive` (soft delete, no status domain of its own): active ↔ inactive, with the guards above.
`InventoryLot.IsActive` and `InventorySerial` (see section 3) have no manual deactivation: they are governed by the ledger.

### 2.1 Convert to serial (Batch 26, Rentals R0)

What it does: moves an **untracked** product (`NONE`) to **serial** tracking (`SERIAL`) even if it already has movements, giving
a serial number to **each unit on hand**. It is the **only** way to change the tracking of a product with movements: the
normal edit (`PATCH`) keeps rejecting it with 409 (rule D25). Meant for the Advance Depot units that came from the
migration without serials and that are going to be **rented** (rentals only work with serialized units).

Who can: **`inventory.manage`** (changes the product master) **and `inventory.adjust`** (moves inventory) are required. Without
`inventory.manage` the API responds 403 before starting; with `inventory.manage` but without `inventory.adjust`, 403 `Missing permission
'inventory.adjust'.` (and a `PERMISSION_DENIED` event is recorded). Module **WMS_LOTSERIAL**. The company comes from the session: a product or a
bin from another company gives 404.

How it is used (server; on the web, the "Convert to serial" button of the product record, screens chapter [F17](frontend/f17-rentas.md) §2):
- `POST /api/v1/products/{publicId}/convert-to-serial`
  ```json
  { "positions": [
      { "binId": 12, "serialNumbers": ["SN-0001", "SN-0002"] },
      { "binId": 15, "serialNumbers": ["SN-0003"] } ],
    "notes": "Equipos de renta", "rowVersion": "AAAAAAAAB9E=" }
  ```
  One row for **each bin where the product has stock**, with **as many serials as units on hand** in that bin
  (check the bins in `GET /api/v1/inventory/balances?productPublicIds=…`). `notes` is optional (if not written, the
  movements say "Serial conversion"); `rowVersion` is optional (the one from the record: if another user changed the product, 409). If the
  same bin comes in two rows, their serials are added together.
- Response 200: `{ product, serialCount, movements: { transactions, balances } }` — the product record already with `trackingTypeCode =
  "SERIAL"`, how many serials were created and the movements and balances it touched.
- Product **without stock** (with or without movements): send `{}` or `positions: []` and only the tracking changes.

What happens inside (all in **a single transaction**: if anything fails nothing is written):
1. The product and all its balances are locked (no other movement of the product gets in while it is converted).
2. It is verified: the product is untracked; **nothing reserved**; no open documents; each bin with stock has
   whole units, no lot, and the number of serials entered equals its units on hand.
3. For each bin, in the ledger: an **outbound adjustment** (`ADJUSTMENT −`) for the whole untracked balance and an **inbound adjustment**
   (`ADJUSTMENT +1`) per serial, all with the system reason **`TRACKING_CONVERSION` ("Serial conversion")**. The net is 0:
   **on hand and available of each bin do not change**. Each serial is born `AVAILABLE` in its bin (serial status
   history: created as Available).
4. The product tracking moves to `SERIAL` (recorded in the product **audit**: change of `TrackingTypeLookupId`).

After converting, the product is handled like any serial-tracked product: adjustments, transfers, pick batches and receipts
ask for the serials. The `TRACKING_CONVERSION` reason **cannot** be used in a manual adjustment (400 `The reason TRACKING_CONVERSION is
assigned by the system.`) and the adjustment screen does not offer it.

#### Validations (convert to serial)

| Case | Exact message | HTTP |
|---|---|---|
| A bin with stock brings a number of serials other than its units on hand, or is missing from the request (`{n}` = units on hand in the bin; `{m}` = serials entered). A bin **without** stock with serials gives the same message with `{n}` = 0 | `Enter {n} serial number(s) for {bin} (there are {m}).` | 400 |
| Row without `binId` (`errors["positions[i].binId"]`) | `Enter the bin.` | 400 |
| Serial repeated in the request (case-insensitive, even in another bin) | `Serial number {s} is repeated.` | 400 |
| Serial longer than 80 characters / more than 500 serials in one row | `Serial number {s} exceeds 80 characters.` / `A line allows at most 500 serial numbers.` | 400 |
| `notes` longer than 300 characters | `Notes allow at most 300 characters.` | 400 |
| `rowVersion` that is not base64 | `Invalid rowVersion: the base64 value returned by the record is expected.` | 400 |
| Product from another company or nonexistent | `Product not found.` | 404 |
| `binId` from another company or nonexistent | `Bin not found.` | 404 |
| Without `inventory.adjust` (with `inventory.manage`) | `Missing permission 'inventory.adjust'.` | 403 |
| Some bin of the product has reserved units (pick batch, cross-dock or another reservation) | `Product {sku} has reserved units; release them before converting it.` | 409 |
| The product is in an open receipt, a pending or in-progress task, a pick batch that can still be deleted or an open count (same criterion as deactivating the product) | `Product {sku} has open receipts, tasks, pick batches or counts; finish them before converting it.` | 409 |
| Another user changed the product (with `rowVersion`) | `The record was modified by another user; reload and try again.` | 409 |
| An entered serial is already in inventory / was scrapped (serials the product already had registered) | `Serial {s} is already in inventory.` / `Serial {s} was scrapped; it does not come back to inventory.` | 409 |
| The product is already serial-tracked | `Product {sku} is already serial-tracked.` | 422 |
| The product is lot-tracked | `Only untracked products are converted to serial; {sku} is lot-tracked.` | 422 |
| A bin has fractional stock (for example 1.5) | `The stock of {sku} in {bin} is {qty}; adjust it to whole units before converting it.` | 422 |
| Stock without a bin (only at warehouse level) or with a lot | `The stock of {sku} in {bin or warehouse} is not in a bin without lot; move or adjust it before converting it.` | 422 |
| Inactive bin or warehouse with stock | `Bin {bin} is inactive; it does not allow inventory movements.` / `Warehouse {code} is inactive; it does not allow inventory movements.` | 422 |

#### Status and effects

There is no status of its own: the product keeps its `IsActive`. `TrackingType` changes from `NONE` to `SERIAL` (one way only: a serial-tracked
product does not go back to `NONE` if it has movements). Each new serial enters `SerialStatus` as **Available** (`AVAILABLE`). It blocks:
reservations, open documents and non-convertible stock (table above).

#### Common cases

- **Depot product with 3 units in `01-A-24` and 1 in `02-B-10`**: enter 3 serials for `01-A-24` and 1 for `02-B-10` in the same
  request. If one is missing, the API says exactly how many are missing in which bin and converts nothing.
- **It has units reserved by a pick batch**: pack or delete the pick batch (or wait for it to finish) and try again.
- **The stock is wrong** (it does not match the physical count): first do the cycle count or the adjustment, then convert.
- **Reports**: the conversion adjustments appear in the ledger and in the Adjustment report with the reason "Serial conversion"
  (outbound and inbound for the same total, net 0).

---

### 2.2 Inventory rotation: FEFO or FIFO (2026-10-11, Batch 34)

What it does: defines **which balance the goods leave from first** when the system assigns an issue (pick batch, manual issue, replenishment of
the pick bin and the exit order that goes down to the device). There are two methods:

- **FEFO** (the usual one and the one for **all existing companies**): first to expire, first out; what does not expire goes last.
- **FIFO**: **first in, first out**: the balance whose **first entry** is oldest leaves first; a balance with no known date goes last.

In both, the tiebreaker is the same as before: zone type (picking, reserve, refrigerated, staging), bin code and lot. Quarantine, cross-dock
and the On rent zone are never assigned, with either method. An **explicit** bin or lot in the issue still prevails.

Where it is configured (owner's decision, 2026-10-11):

- **Per company:** `PUT /api/v1/tenant/settings` with `{ "rotationMethod": "FIFO" }` (`admin.tenant`; without the field it does not change; `GET` returns it in
  `rotationMethod`, always with a value, `FEFO` by default).
- **Per-product exception:** `rotationMethod` on create (`POST /api/v1/products`) and in `PATCH /api/v1/products/{publicId}`; in the `PATCH`, an empty
  field (`""`) **removes** the exception (the product goes back to the company method) and without the field it does not change. The product record returns it in
  `rotationMethod` (`null` = follows the company). **The product exception** prevails; if there is none, the company's; otherwise, FEFO.
- It is written case-insensitively (`fifo` = `FIFO`). Changing it is recorded in the audit log (company and product) and applies **from the next
  assignment**; it does not move anything already picked nor the replenishment tasks already created.

**The first-entry date of a balance** (the one FIFO uses) is set by the system, it is not entered: it is the moment when the balance (product, bin, lot)
received its first stock, or when it **was filled again from zero**. If more units arrive at a balance that already has stock, it **keeps the oldest
date** (a balance is one row: rotation is by balance, not unit by unit). A **transfer between bins inherits the date of the source**: moving
goods around does not make them younger. For inventory that already existed, the date was computed once with the first entry the ledger records in that bin
and lot (if there is none, with the last update of the balance); a balance with no date at all goes **last** in the FIFO order.

With FIFO, for a product with lot and expiration, **the entry date prevails over the expiration** (the expiration only breaks ties). If that mix is not
right for a product that expires, leave it as FEFO with its exception.

| Case | Exact message | HTTP |
|---|---|---|
| `rotationMethod` other than FEFO or FIFO (company, product create or edit) | `The rotation method must be FEFO or FIFO.` | 400 (field `rotationMethod`) |
| Changing the company's without `admin.tenant` | standard permission-denied response | 403 |

Where it is changed on the screens (2026-10-11): **web, Tenant settings → Operations → “Inventory rotation”** (FEFO / FIFO selector; requires `admin.tenant`, read-only without it) and, for a product's exception,
**Products & inventory → product record → “Rotation”** (options “Same as the company”, FEFO and FIFO; going back to “Same as the company” removes the exception). The warehouse app has **no** settings or product-edit
screens, so there is no selector there: its issue screens (bin selector of Picking, “Dispatch”) already use the order computed by the server and **honor the method** without changes.

### 2.2b Two rotation methods: with lot and without lot (2026-10-20, Batch 142)

What changes: the company rotation method (§2.2) is now **two fields**, because FEFO needs an expiration date and only lot-tracked products have one.

| Field (Tenant settings → Operations → “Inventory rotation”) | Applies to | Options | Default |
|---|---|---|---|
| **Company rotation method (lot-tracked products)** — `rotationMethod` | Lot-tracked products | **FEFO** (first to expire) or **FIFO** (first in) | FEFO (no value changed) |
| **Company rotation method (products without lot or serials)** — `rotationMethodNoLot` | Untracked and serial-tracked products (they do not expire) | **FIFO** (first in) or **By bin** (`POSITION`) | **FIFO** |

- **By bin:** without looking at dates, it leaves from the bin that is due by its zone (picking, then reserve, refrigerated, staging) and the bin code.
- **Serials are treated as products without lot** (they do not expire).
- **Per-product exception** (kept): on the record, “Rotation”. The options depend on the product type: with lot, FEFO or FIFO; without lot or serials, FIFO or by bin; “Same as the company” uses the company field that applies to it. An exception that does not apply to the type (for example FEFO on a product that became lot-less) is **ignored** and the company's prevails.
- **Behavior change:** products **without lot** go from leaving by bin code (the FEFO tiebreaker without expiration) to leaving by **age** (FIFO). Whoever wants the old behavior chooses “By bin”.
- “Suggest / Enforce” (§2.3) applies the same with any of the methods. It is used by picking, manual issue, the reservation of sales orders, replenishment and the device's exit order.

| Case | Exact message | HTTP |
|---|---|---|
| `rotationMethod` (lot-tracked products) other than FEFO or FIFO | `The rotation method must be FEFO or FIFO.` | 400 (field `rotationMethod`) |
| `rotationMethodNoLot` other than FIFO or POSITION | `The rotation method of products without lot must be FIFO or POSITION (by bin).` | 400 (field `rotationMethodNoLot`) |
| Exception of a lot-tracked product that is neither FEFO nor FIFO | `A lot-tracked product only allows FEFO or FIFO rotation.` | 400 (field `rotationMethod`) |
| Exception of a product without lot or serials that is neither FIFO nor POSITION | `A product without lot or serials only allows FIFO or POSITION (by bin) rotation.` | 400 (field `rotationMethod`) |

### 2.3 Rotation: suggest or enforce on issues (2026-10-20, Batch 139)

What it does: the rotation method (§2.2) says **which lot and which bin are due first**; this setting says **what happens if the person issuing wants another**. It applies to
**picking** and to **manual issue**, on the web and on the warehouse device. It does **not** apply to transfers or adjustments.

| Mode | What the system does | What the person issuing can do |
|---|---|---|
| **Suggest** (`SUGGEST`, the one for **all existing companies**) | Proposes the bin and lot that correspond according to FEFO or FIFO. | Change them. The screen **warns** that the rotation is being skipped and says where it should have come from. |
| **Enforce** (`ENFORCE`) | Picks the bin and lot alone and **splits the quantity** across them in rotation order. | Nothing: there is no bin or lot selector. |

Where it is configured: **web, Tenant settings → Operations → “Inventory rotation” → “How it is applied on issues”** (requires `admin.tenant`) or
`PUT /api/v1/tenant/settings` with `{ "rotationEnforcement": "ENFORCE" }`. `GET /api/v1/tenant/settings` returns it in `rotationEnforcement`
(`SUGGEST` by default). It applies from the next issue; it does not change what was already picked.

How it looks:
- **Web, Picking and Manual issue.** In *suggest*, the proposed bin (the server's rank 1) appears as “FEFO: A-01 · L-7”; if another
  bin or lot is chosen, “This skips the rotation: it should come from A-01 · L-7” appears in red and you can continue. In *enforce*, the Bin and Lot columns
  say “Automatic (rotation)”, “Suggest bins” is hidden and the web sends no bin or lot (except for serial-tracked products, which are chosen one by one).
- **Warehouse app, Dispatch.** In *suggest*, scanning a bin other than the proposed one opens “This skips the rotation” with **Cancel / Continue**
  (before, with a lot-tracked product, it was simply rejected; now it warns and lets you decide). In *enforce*, the bin field disappears: you
  type the quantity, see “5 from A-01 · 2 from B-02” and a single **“Take it this way”** button adds all the parts. Without signal it uses the server's last response
  and the device's copy of stock; the server decides anyway when it receives the dispatch.
- **Server (API).** In *enforce*, a line with explicit `binId` or `lotId` is accepted only if it is **exactly** what the rotation assigns (whoever sends
  the same thing proposed to them passes); otherwise, **409**. Lines without bin or lot are assigned automatically. Serial-tracked products do not change.
  `GET /api/v1/inventory/exit-options` brings `rotationEnforcement` so the screens know it.

| Case | Exact message | HTTP |
|---|---|---|
| `rotationEnforcement` other than SUGGEST or ENFORCE | `Rotation enforcement must be SUGGEST or ENFORCE.` | 400 (field `rotationEnforcement`) |
| *Enforce* and the line asks for a bin or lot the rotation does not assign | `Rotation is mandatory: {SKU} must come from {bins}. Do not choose a bin or lot; the system assigns them.` | 409 |
| Changing it without `admin.tenant` | standard permission-denied response | 403 |

## 2b. Import products with a template (2026-10-21)

First entity of the generic template engine (dictated by Luis, 2026-10-19). **Who can:** `inventory.manage` (Inventory with lots and serials module); to see the templates, `inventory.view`.

**How it is used:** Products → **Import**. Choose (or create with “New template” / “Create standard format”) the template, choose the file (CSV or .xlsx, up to 5,000 rows / 2 MB) and press **Review**: it saves nothing and says per row whether it is fine or why it is rejected. **Import N** creates the valid rows; a bad row does not stop the others. **It only creates**: a product that already exists is rejected, never updated.

**Template (Kind `PRODUCT`):** `GET/POST /api/v1/products/import-templates`, `GET/PATCH …/{publicId}`, `POST …/{publicId}/deactivate|reactivate`, `GET …/fields`. It is also managed in Integrations → Templates (Products tab). Fields: `sku` (required, always as a column), `name` (as a column or as a default value), `ownerClientCode`, `baseUom`, `trackingType`, `weightKg`, `volumeM3`, `barcode`, `purchaseCost`, `salePrice`, `minQty`, `minPickQty`, `maxPickQty`, `brand`, `model`, `packUom`, `packQty`, `rotationMethod`. `sku` and `barcode` do not accept a default value.

**Import:** `POST /api/v1/products/import` (multipart: `templatePublicId`, `file`, `commit` false = review / true = create). Each row brings `key` (the SKU), `label` (the name), `status` OK, CREATED or ERROR and its errors per field (`errors`).

**Generic engine:** products and suppliers use the same engine (`ImportEntities` + `EntityImportService`; see §8b for suppliers). A new entity only declares its fields and group rules and how to review and create a row.

| Message | HTTP / where | What to do |
|---|---|---|
| “Enter at least one column.” | 400, template | The template has no columns. |
| “The template must include the sku column.” | 400, template | Add the sku column. |
| “The template must include the name column or a default value for name.” | 400, template | Add the name column or a default name. |
| “Unknown field: X.” · “Position N is repeated.” · “The field X is repeated.” | 400, template | Fix the template. |
| “The field X does not accept a default value.” | 400, template | sku and barcode bring their value in each row. |
| “Choose the file to import.” | 400 | No file was sent. |
| “The file has no rows.” | 400 | It only has the header. |
| “A product with that SKU already exists for that owner.” | row | The SKU already exists (compared case-insensitively); it is not updated. |
| “The SKU is repeated in the file.” | row | The same SKU and owner twice in the file. |
| “An active product with that barcode already exists.” | row | Repeated barcode (in the system or in the file). |
| “Must be a number.” | row | Cost, price, weight, volume, minimums or pack quantity not numeric. |
| “Owner client not found by that code.” | row | `ownerClientCode` is not the code of an active client. |
| “Unknown unit of measure: 'X'.” · “Unknown tracking type: 'X'.” · “Unknown pack unit: 'X'.” | row | Use a catalog code. |
| (the manual create rules: SKU, name, barcode, brand, model, costs, measures…) | row | They are the same as the manual product create. |

## 3. Inventory: balances, ledger, adjustments, transfers, reconciliation and discrepancies, genealogy and serial trail

What it does: exposes the balance by warehouse/bin/lot, the read-only ledger (with summary, filters and detail of each
movement), the manual adjustment with a reason from a list, the transfer between bins or warehouses, the genealogy of a lot, the trail
of a serial and the ledger ↔ balance **reconciliation**, which now runs by itself in the background and saves the **discrepancies** it finds
(Batch 14). `InventoryLedger` is the only write path: **nobody** does an `UPDATE` on `InventoryTransaction` (a reversal is always
a new movement) nor writes `StockBalance` outside it.

The ledger quantity is stored **with sign** (master L331: "every dispatch writes a negative movement; every
receipt, a positive one"): `RECEIPT` comes in (+), `ISSUE`/`CROSSDOCK` go out (−), `ADJUSTMENT` comes in or goes out as
entered, `TRANSFER` is a single row with source and destination. Available is always "on hand − reserved"
computed in code (never the computed column `QtyAvailable`).

Who can (module **WMS_LOTSERIAL**):

| Action | Permission |
|---|---|
| Balances, ledger, summary, detail of a movement, owners, bin search, genealogy, serial trail and **viewing** discrepancies | `inventory.view` |
| Adjust, transfer, "Run reconciliation", status of the automatic check and **resolving** a discrepancy (fix or dismiss) | `inventory.adjust` |
| "Create a count for that bin" after fixing a discrepancy (it is a new count, section 6) | `warehouse.count.capture` |
| Seeing the "Needs your attention" section of the Pulse (the pending discrepancies) | `pulse.attention` and `inventory.view` (chapter 07, section 4) |

**Local day.** Since Batch 14, the date filters `from` and `to` (day `YYYY-MM-DD`, `to` inclusive) are **company days in Puerto Rico
time** (America/Puerto_Rico, UTC−4), not UTC days. It applies to the ledger, its summary, the discrepancies and the count list.

### 3.1 Ledger: filters, summary and movement detail

**“Putaway” type (2026-10-07).** A movement that comes from a **putaway task** (PUTAWAY) is shown in the ledger as **“Putaway”** (`typeCode = PUTAWAY`), not as “Transfer”: this way it is not confused with a move between warehouses. Only what is shown changes: in the ledger the movement is still a TRANSFER with a reference to the task (`refEntity = WAREHOUSE_TASK`, “Task #n”), so balances, reconciliation and traceability do not change. The **Type** filter offers “Putaway”; **“Transfer”** brings only the transfers that are not putaways (those from Transfers & adjustments, count, rentals…). Transfers from a replenishment task remain as Transfer.

How it is used:
- `GET /api/v1/inventory/balances?warehousePublicIds=&binIds=&productPublicIds=&categoryIds=&lotNumber=&
  includeZero=&onlyAvailable=&search=&skip=&take=` (`take` ≤ 200). The response brings, besides the rows, `totalOnHand` and
  `totalAvailable` (the "On hand" and "Available" figures of the Balances summary).
- `GET /api/v1/inventory/transactions?from=&to=&types=&warehousePublicIds=&binIds=&productPublicIds=&
  categoryIds=&lotNumber=&serialNumber=&refEntity=&refId=&search=&brands=&name=&ownerClientPublicIds=&includeOwn=&reasons=&
  direction=&fromWarehousePublicIds=&toWarehousePublicIds=&manualOnly=&skip=&take=` (most recent first).
- `GET /api/v1/inventory/transactions/summary?…` — **the same filters** as the list (without `skip` or `take`).
- `GET /api/v1/inventory/transactions/{id}` — detail of a movement.
- `GET /api/v1/inventory/owners` — owners for the filter.
- `GET /api/v1/warehouses/bins/search?search=&warehousePublicIds=&includeInactive=&take=` — bins of all warehouses (or of the
  given ones) for the Bin filter: `search` looks in the bin code or its zone code; `take` ≤ 50 (20 by default).

List filters (all combine with "and"; multi-value ones, with "or"):

| Filter | What it does |
|---|---|
| `from`, `to` | Puerto Rico local days, `to` inclusive |
| `types` | Movement type (`RECEIPT`, `ISSUE`, `TRANSFER`, `ADJUSTMENT`, `CROSSDOCK`) |
| `warehousePublicIds`, `binIds` | Movements that touch those warehouses or bins (on either side) |
| `productPublicIds`, `categoryIds` | Product or category (with its subcategories) |
| `lotNumber`, `serialNumber` | Lot or serial |
| `refEntity` + `refId` | Movements of a source document (for example `CYCLE_COUNT` and the count id) |
| `brands`, `name` | Product brand and name text (Batch 12) |
| `ownerClientPublicIds`, `includeOwn` | Product owner: one or several clients; `includeOwn=true` adds the company's own products ("Own") |
| `reasons` | Adjustment reason (one or several) |
| `direction` | `IN` (inbound) or `OUT` (outbound), with the same perspective as `signedQuantity` |
| `fromWarehousePublicIds`, `toWarehousePublicIds` | Source and destination warehouse: each is compared against its side of the movement |
| `manualOnly` | Only movements without a reference document (made by hand) |
| `search` | Free text |

The ledger row brings `quantity` (the ledger's, with sign), `signedQuantity` (the same quantity from the filter's perspective),
`ownerName` (the owner client or "Own") and `categoryName`, in addition to bin from → to, lot, serial, reason, note, user and source.

**Summary.** Counts, with the same filters, the **movements**, the **inbound** and **outbound** ones (how many movements and how many units)
and the **internal** ones. Without a location filter, a transfer is internal (neither in nor out); with a warehouse or bin filter, it is
inbound if it arrives from outside the filter, outbound if it leaves, and internal if both sides are inside. Without a location filter, `IN` is
a quantity greater than zero that is not a transfer, and `OUT`, a quantity less than zero.

```
GET /api/v1/inventory/transactions/summary?from=2026-09-30&to=2026-09-30&productPublicIds=…
→ { "movements": 3, "inCount": 1, "inQty": 10, "outCount": 1, "outQty": 2, "internalCount": 1 }
```

**Movement detail.** Returns the full row with owner, category and lot expiration; the **source document** with its
number, status, date and party (client or supplier) and, to open it, its `publicId` (receipt, pick batch, purchase order, transport
order and product) or its id (count); and the **related movements**: those of the same document or, if the movement has no
document, those of the same entry (same instant, user and product). Cap of 200 (`relatedTruncated`). Documents it resolves:
receipt (`RECEIPT`), pick batch (`PICK_BATCH`), count (`CYCLE_COUNT`), purchase order (`PURCHASE_ORDER`), transport order
(`TRANSPORT_ORDER`), warehouse task (`WAREHOUSE_TASK`, with its parent document), product (`PRODUCT`, replenishment) and cross-dock.

```
GET /api/v1/inventory/transactions/1658
→ { "transaction": { "id": 1658, "typeCode": "TRANSFER", "quantity": 3, … },
    "ownerName": "Propio", "categoryName": null, "lotExpiryDate": null,
    "document": null, "related": [ { "id": 1658, … } ], "relatedTruncated": false }
```

### 3.2 Adjustments and transfers

How it is used:
- `POST /api/v1/inventory/adjustments` — `{ "productPublicId": "...", "warehousePublicId": "...", "binId": 5,
  "quantity": -2, "reason": "DAMAGE", "notes": "Caja aplastada en la plataforma" }` (positive comes in, negative goes out).
  **The note is required** (2026-09-30 adjustment): the API demands a non-empty `notes` (after trimming spaces), of up to
  300 characters, for every reason entered by a person (`DAMAGE`, `LOSS`, `FOUND`, `EXPIRED`, `PO_SHORTAGE`, `OTHER` and
  any reason the company adds). It applies to the screens, the mobile app and the integrations. It is **not** required by
  the adjustments the system writes, because they do not go through this endpoint: the variance of a receipt (`RECEIPT_VARIANCE`), that
  of a count (`COUNT_VARIANCE`), the reversal when deleting a pick batch (`PICK_BATCH_REVERSAL`) and the opening balance of the
  migration (`OPENING_BALANCE`) and the serial conversion (`TRACKING_CONVERSION`, Batch 26, section 2.1). Nor is it required by the transfer or the "manual adjustment" used to resolve a purchase
  shortage (section 8): there the note is still optional, because the movement stays linked to the order and its line.
- `POST /api/v1/inventory/transfers` — `{ "productPublicId": "...", "fromBinId": 5, "toBinId": 8, "quantity": 3 }`
  (between warehouses: add `fromWarehousePublicId`/`toWarehousePublicId`; with lot or serial: `lotId` and `serialNumbers`).

**How the screen enters it (Batch 14, D11).** The API did not change: it still receives the signed quantity. The screen asks for **Adjustment type:
Increase or Decrease** (required) and a **positive quantity**, and sets the sign. The **reasons depend on the direction** (a rule of the
**screen only**; the API accepts any non-system reason with either sign):

| Reason | On increase | On decrease |
|---|---|---|
| Found (`FOUND`) | Yes | No |
| Damage (`DAMAGE`), Loss (`LOSS`), Expired (`EXPIRED`) | No | Yes |
| The other list reasons that are not system ones (for example `PO_SHORTAGE`, `OTHER`) | Yes | Yes |
| `RECEIPT_VARIANCE`, `COUNT_VARIANCE`, `PICK_BATCH_REVERSAL`, `OPENING_BALANCE`, `TRACKING_CONVERSION` (Batch 26) | Never (assigned by the system) | Never |

When the direction changes, a reason that is no longer valid is removed. The reason is chosen with a search box. The screen shows **"Available in the
bin: N"** and, on decrease, **does not allow decreasing more than what is available**. A **lot-tracked** product asks for the lot number on increase and picks a
lot from those in the bin on decrease; a **serial-tracked** one asks for the serials on increase and picks the serials that leave on decrease. The same
modal is used in Transfers & adjustments, in the ledger and in the product record ("Add adjustment").

The **transfer** is entered in this order: **source** warehouse and bin → **item** (product and lot, from what is in that
bin) → **serials** (if the product has them; the quantity is the number of serials) → **destination** warehouse and bin (by default,
the source warehouse) → quantity (capped at what is available) → note (optional). Before, the screen did not allow transferring products with
lot or serial; now it does.

A transfer is a single `TRANSFER` row with source and destination; an adjustment is an `ADJUSTMENT` row. Both appear in the ledger
instantly and trigger the automatic check of their product (3.3).

### 3.3 Automatic reconciliation and discrepancies

**What a discrepancy is.** The ledger (the list of movements) and the balance (`StockBalance`) are written together on every movement, so
they must match. A **discrepancy** is that, for a key (product, warehouse, bin, lot), the balance is not equal to what the movements
add up to; or, in the **product total**, that the sum of its balances is not equal to that of its movements excluding transfers.
**It is not a physical difference on the shelf** (a cycle count solves that). In normal operation it almost never happens; it appears
after a fix made directly in the database, a restore, the migration or a system error. **An adjustment or a count
does not fix it**, because they move the ledger and the balance by the same amount: the discrepancy is fixed by setting the balance to what the
ledger says (the ledger rules) or dismissed with a note.

**When it is checked.**
- **By itself, in the background** (source `EVENT`, "Automatic (movement)"): about 1.5 seconds after each ledger movement, the
  system checks the touched product (several consecutive movements are grouped; a product checked less than 10 seconds ago waits).
  Nobody waits for it. If the server restarts, a check that was pending **is lost**; it is covered by the next movement
  of that product and by the "Run reconciliation" button.
- **On demand** (source `MANUAL`): `POST /api/v1/inventory/reconciliation/run`, with `{ "productPublicIds": ["…"] }` (up to 200 products)
  or without a body to check **all** of the company's. Returns `{ checkedAtUtc, productsChecked, balancesChecked, opened,
  stillOpen, selfCorrected, mismatches }`: how many it opened, how many are still pending and how many closed by themselves.
- **On migration** (source `MIGRATION`): `import-legacy` reconciles at the end. Console commands (`db-init`, `import-legacy`) do not start the
  background check.
- The `SCHEDULED` source ("Scheduled") already exists in the list, but there is no scheduled sweep yet.
- `GET /api/v1/inventory/reconciliation/status` (`inventory.adjust`) says whether the automatic check is on, whether it is consuming,
  how many products are waiting (`pending`; 0 = up to date), how many have been checked (`processed`), how many notices were dropped because the queue was full
  (`dropped`), how many checks failed (`failed`), the time of the last one (`lastProcessedAtUtc`) and the last error (`lastError`). The
  counters are per company and live in memory since the server started. The configuration is in `Inventory:Reconciliation`
  (`Enabled`, `DebounceMs` 1500, `MaxBatchProducts` 500, `Capacity` 10000, `MinRecheckSeconds` 10).
- `GET /api/v1/inventory/reconciliation?productPublicId=` (`inventory.adjust`) keeps its shape: compares and shows the differences,
  **without saving them**; `mismatches: []` means the invariant holds.

**Check rules.** A discrepancy is saved only if it is confirmed **under a lock** on the product (so an in-flight movement does not give
false positives). There is **at most one open discrepancy per key** (a unique index guarantees it). A later check updates the
figures and adds one check (`checkCount`); if the balance already matches, it closes the discrepancy as "Self-corrected". A
**dismissed** discrepancy is not reopened while the figures stay the same; with different figures a new one is opened. A **resolved** or "Self-corrected"
one that happens again opens a new discrepancy. If the discrepancy is of the product total, it is not opened when the same product already
has per-bin discrepancies (the total would be noise).

How they are viewed and resolved:
- `GET /api/v1/inventory/discrepancies?status=&warehousePublicIds=&productPublicIds=&categoryIds=&binIds=&kinds=&from=&to=&skip=&take=` —
  paginated (`take` ≤ 200, 50 by default; open ones first). `status` accepts `OPEN`, `RESOLVED`, `DISMISSED` and `SELF_CORRECTED`;
  `kinds`, `BALANCE` (balance per bin) and `PRODUCT_TOTAL` (product total); `from` and `to`, local days of detection. The response
  brings `total` and `openCount` (pending with the same filters except status). Each row: product, warehouse, bin, lot, `ledgerQty`
  (what the ledger gives), `balanceQty` (the balance), `difference` (**balance − ledger**; positive = the balance says more than the
  movements give), status, source, when it was detected, how many times it was checked, and if it is closed, who, when, the note and "corrected from → to".
- `GET /api/v1/inventory/discrepancies/{publicId}` — the record: the discrepancy, what is reserved today, the last 20 movements of that key and the
  status history.
- `POST /api/v1/inventory/discrepancies/{publicId}/resolve` — `{ "action": "REBUILD_BALANCE", "notes": "Corregido tras la restauración",
  "rowVersion": "<the one from the record>" }` or `{ "action": "DISMISS", "notes": "Conteo físico correcto; el saldo se ajustará en el cierre",
  "rowVersion": "…" }`. `notes` is required to dismiss (up to 500 characters); `rowVersion` is optional but protects against a simultaneous
  change.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Quantity ≤ 0 | `The quantity must be greater than zero.` | 400 |
| Quantity with more than 3 decimals | `The quantity allows at most 3 decimals.` | 400 |
| Quantity out of range | `The quantity exceeds the maximum allowed.` | 400 |
| Adjustment without quantity / quantity = 0 | `Enter the adjustment quantity.` / `The adjustment quantity cannot be zero.` | 400 |
| Adjustment without reason | `Enter the adjustment reason.` | 400 |
| Reason reserved to the system (`RECEIPT_VARIANCE`, `COUNT_VARIANCE`, `PICK_BATCH_REVERSAL`, `OPENING_BALANCE`, `TRACKING_CONVERSION`) | `The reason {code} is assigned by the system.` | 400 |
| Adjustment without a note, or with only spaces (2026-09-30 adjustment; the error goes in `errors.notes`) | `Write a note explaining the adjustment.` | 400 |
| Adjustment or transfer note longer than 300 characters (`errors.notes`) | `Notes allow at most 300 characters.` | 400 |
| Unknown reason (also in the `reasons` filter of the ledger) | `Unknown adjustment reason: 'X'.` | 400 |
| Lot-tracked product without lot | `The product is lot-tracked: enter the lot.` (or, in the adjustment, `Product {sku} is lot-tracked; enter the lot.`) | 400 |
| Product without lot/serial with a lot given | `The product is not tracked by lot or serial: do not enter a lot.` | 400 |
| Serial-tracked product: non-whole quantity | `For serial-tracked products the quantity must be a whole number.` | 400 |
| Serials entered ≠ quantity | `The quantity must equal the number of serials entered.` | 400 |
| Product without serial with serials given | `The product is not serial-tracked: do not enter serial numbers.` | 400 |
| Transfer to the same source and destination | `The source and destination cannot be the same bin.` (or `The source bin and the destination bin are the same.`) | 400 |
| Transfer without source/destination | `Enter the source bin.` / `Enter the destination bin.` | 400 |
| Inactive product on an inbound | `Product {sku} is inactive; it does not allow inventory inbounds.` | 422 |
| Inactive bin on a movement | `Bin {bin} is inactive; it does not allow inventory movements.` | 422 |
| Inactive warehouse on a movement | `Warehouse {code} is inactive; it does not allow inventory movements.` | 422 |
| Outbound above what is available | `Insufficient inventory of {sku} in {bin}: available {x}, requested {y}.` | 409 (`insufficient_stock`) |
| Releasing more reservation than there is | `The reservation to release exceeds what is reserved.` | 409 |
| Existing lot with other dates | `Lot {n} already exists with other dates; correct the dates or use another lot number.` | 409 |
| Serial not available in the bin | `Serial {s} is not available in {bin}.` | 409 |
| Serial already in inventory | `Serial {s} is already in inventory.` | 409 |
| Scrapped serial | `Serial {s} was scrapped; it does not come back to inventory.` | 409 |
| `from` later than `to` (ledger and discrepancies) | `The 'from' date cannot be later than the 'to' date.` | 400 |
| Unknown movement type in `types` | `Unknown movement type: 'X'.` | 400 |
| `direction` other than `IN` and `OUT` (ledger and summary; `errors.direction`) | `The direction must be IN (inbound) or OUT (outbound).` | 400 |
| Owner (`ownerClientPublicIds`) that does not exist or belongs to another company | `Client not found.` | 404 |
| Detail of a movement that does not exist or belongs to another company | `Movement not found.` | 404 |
| Lot/product from another tenant or another owner (with `InventoryScope`) | `Lot not found.` / `Product not found.` | 404 |
| "Run reconciliation" with more than 200 products (`errors.productPublicIds`) | `Manual reconciliation allows at most 200 products at a time.` | 400 |
| "Run reconciliation" with a product that does not exist | `Product not found.` | 404 |
| Unknown discrepancy `kinds` filter (`errors.kinds`) | `Unknown discrepancy kind: 'X'.` | 400 |
| Discrepancy that does not exist or belongs to another company | `Discrepancy not found.` | 404 |
| Resolving without an action or with one other than the two (`errors.action`) | `Enter the action: REBUILD_BALANCE (fix the balance) or DISMISS (dismiss).` | 400 |
| Dismissing without a note (`errors.notes`) | `Write a note explaining why the discrepancy is dismissed.` | 400 |
| Resolution note longer than 500 characters (`errors.notes`) | `The note allows at most 500 characters.` | 400 |
| `rowVersion` that is not base64 | `Invalid rowVersion: the base64 value returned by the record is expected.` | 400 |
| `rowVersion` that is no longer current | `The record was modified by another user; reload and try again.` | 409 |
| Resolving an already closed discrepancy (Resolved, Dismissed or Self-corrected) | `The discrepancy is already closed; it is read-only.` | 422 |
| Fixing the balance of a product-total discrepancy | `This discrepancy is for the product total; it is not fixed per bin. Fix the per-bin discrepancies or dismiss it with a note.` | 422 |
| Fixing when the ledger gives less than what is reserved | `The ledger gives {ledger} for {sku} in {bin}, less than what is reserved ({reserved}); release the reservation before fixing the balance.` | 409 |
| Fixing when the ledger gives a negative balance | `The ledger gives a negative balance ({ledger}) for {sku} in {bin}; review the movements before fixing the balance.` | 409 |
| Two checks opened the same discrepancy at the same time | `The reconciliation collided with another simultaneous check; try again.` | 409 |

When a `400` carries a single error, the same message also travels in `detail`, in addition to `errors`.

Messages seen only on the adjustment and transfer screen (no HTTP): `Choose whether the adjustment increases or decreases
stock.`, `The quantity must be greater than zero.`, `You cannot decrease more than what is available in the bin ({qty}).`, `You cannot transfer
more than what is available in the bin ({qty}).`, `Choose the lot.`, `Choose the item to transfer.` and `Choose at least one serial.`; and, when
fixing a discrepancy that already matched, the notice `The balance already matched; the discrepancy closed itself.`

### Discrepancy statuses and transitions

`InventoryDiscrepancyStatus`: **Pending** (`OPEN`, initial) → **Resolved** (`RESOLVED`), **Dismissed** (`DISMISSED`) or **Self-corrected**
(`SELF_CORRECTED`); the last three are terminal. All transitions go through `StatusService.TransitionAsync` and are recorded in the
history (`/api/v1/status/history/INVENTORY_DISCREPANCY/{id}`); creation and status changes are also recorded in the audit.

| From → to | Who | What it validates | Effects |
|---|---|---|---|
| (new) → Pending | The system (automatic check, migration) or whoever runs the reconciliation (`inventory.adjust`) | The check **confirms it under a lock**; there is no other open one for the same key; there is no dismissed one with the same figures | It is saved with the ledger and balance figures, the source and the movement that uncovered it; the history says "Ledger {l}, balance {b}."; it appears in "Needs your attention" |
| Pending → Resolved | `inventory.adjust`, action `REBUILD_BALANCE` | It is a per-bin discrepancy; current `rowVersion`; the ledger does not give a negative figure nor less than what is reserved | The balance takes what the ledger gives, **without writing a movement**; "corrected from → to", who, when and the note are saved; it leaves "Needs your attention"; the screen offers "Create a count for that bin" |
| Pending → Dismissed | `inventory.adjust`, action `DISMISS` | Required note of up to 500 characters; current `rowVersion` | It does not move inventory; it is not reopened while the figures stay the same |
| Pending → Self-corrected | The system (a check finds the balance matching) or `inventory.adjust` when asking to "fix" when it already matched (responds 200) | — | It closes without touching the balance. The history says "The check found the balance matching the ledger." or, when fixing, the note written or "The balance already matched the ledger when fixing." |

What is blocked in each status:

| Status | Allowed | Not allowed |
|---|---|---|
| Pending | Fix the balance (per bin only), dismiss, let another check update the figures or close it by itself | Fix the product total (422); resolve with a negative ledger or one lower than what is reserved (409) |
| Resolved, Dismissed, Self-corrected | View the record and the history | Resolve again (422 "The discrepancy is already closed; it is read-only."). If the problem reappears, a new discrepancy is opened (except the dismissed one with the same figures) |

### Reading notes

- The ledger exposes `quantity` (the ledger's, with sign) and `signedQuantity` (the same perspective applied to a
  warehouse/bin filter: what leaves the filter is negative, what enters is positive, and the internal or unfiltered
  part of a `TRANSFER` is 0).
- The genealogy of a lot builds its destinations (order, client, consignee) by reading the `Ref` of each movement;
  the reconciliation rebuilds `StockBalance` by adding the "To" side and subtracting the "From" side of each ledger
  row (per key) and, per product, by comparing the sum of `Quantity` without `TRANSFER` against `QtyOnHand`.
- **"Inventory adjustments" left the menu.** The old `/warehouse/inventory-adjustments` screen did not show adjustments: it showed the
  purchase orders received incompletely. Since Batch 14 that address leads to Purchases, and shortages are resolved in the
  **Shortages** tab of each purchase order's record (section 8). Inventory adjustments are now in **Transfers &
  adjustments** and in the ledger.

---

## 4. Receiving: arrival notices (ASN) and receipts

What it does: receives goods against an arrival notice from a 3PL client, against an own purchase order, in
blind mode or as a return. The received quantity starts equal to the expected one (it can be corrected before
confirming). On confirm, the ledger is posted (`RECEIPT` for the expected quantity + `ADJUSTMENT RECEIPT_VARIANCE` for the
difference), the purchase order advances to `PARTIAL`/`RECEIVED`, the assigned cross-dock is split and the
`PUTAWAY` tasks for the remainder are created with a suggested bin.

### Assigning the arrival notice and the exit notice (Batch 171, 2026-10-21)

Like tasks, the **arrival notice** (to whoever is going to **receive** it) and the **exit notice** (to whoever is going to **fill** it) can be assigned to a person of the company. It is the basis of “My tasks” in the warehouse app (next batch). Only an **open** document is assigned: *expected* arrival notice; *requested* or *partial* exit notice.

- **Assign / reassign / remove** (supervision, `warehouse.manage`): web → Receiving → Arrival notices, **Assign** button of the row; Exit notices, **Assign** button of the record. Choosing “Unassigned” removes it. The person must be an active member of the company. API: `POST /api/v1/asns/{id}/assign` and `POST /api/v1/exit-notices/{publicId}/assign` with `{ "userId": 5 }` (`null` = remove).
- **Take** (`warehouse.receive` for the arrival notice, `warehouse.pick` for the exit notice): **Take** button when nobody has it assigned; it stays assigned to whoever takes it. API: `POST …/take`. One assigned to another person **cannot be taken**: a supervisor reassigns it.
- **View:** **Assigned to** column in both lists, the field in the exit notice record, and the **“Only mine”** filter (`assignedToMe=true`; also `assignedUserId=`).
- Nothing is blocked: a document assigned to another person **can still be received or dispatched** by whoever has the permission (the assignment organizes the work, it does not lock it).

| Case | Exact message | HTTP |
|---|---|---|
| Person who is not an active member | `The user is not an active member of the company.` (field `userId`) | 400 |
| Arrival notice that is no longer expected | `Only an arrival notice that is still expected can be assigned.` | 422 |
| Closed or cancelled exit notice | `Only an open exit notice (requested or partial) can be assigned.` | 422 |
| Taking one assigned to another person | `That document is already assigned to another person; ask a supervisor to reassign it.` | 409 |
| Nonexistent notice (or from another client) | `Arrival notice not found.` / `Exit notice not found.` | 404 |

### Arrival notice number: LLE-##### (Batch 170, 2026-10-21)

Every arrival notice has a **number of its own per company**, `LLE-00001`, `LLE-00002`… (`ASN` counter, like `SAL-#####` of the exit notice). The system assigns it on creation —whether staff, the client portal or receiving against a purchase order creates it— and **it is not typed or changed**. It is unique per company.

- **Where it shows:** Arrival notices tab (Notice column: `LLE-00012 · reference`), the notice selector when opening a receipt and the dock appointment (web); “My arrival notices” of the client portal; the list and the scanner of **Receive** in the warehouse app (`LLE-00012` can be typed or scanned, in addition to the reference); and the notification notices (`{number}` is now `LLE-00012`, no longer `#12`).
- **How it is searched:** the **“Reference or number”** filter of the tab and the app search box find it by a piece of the number or of the reference.
- **Notices that already existed:** when the database update (`db-update`) is applied they are numbered **in order of creation, within each company** (`LLE-00001` to the oldest); the counter is left ready to continue. The warehouse app downloads the notices again once to bring the number.
- **API:** `AsnDto.number`; in the portal `PortalArrivalDto.number`; in the device sync `SyncAsnDto.number`.

Batch 13 (Batch 3 of the change plan): the receipt has a six-status cycle that shows where it stands (Expected,
Receiving, Discrepancy, Completed, Completed with variance and Put away; see "Statuses and transitions"). A blind
or return receipt can be created **with the header only** (no lines) and filled in later; in those receipts the
**expected quantity** of each line can also be entered to see the variance. The header also carries **Carrier**
(`carrier`), **Reference** (`reference`) and the **default receiving bin**, and it is edited while the receipt is
open (`PATCH`). In blind and return receipts the variance is informational: **what was received** goes into the ledger (a single
`RECEIPT`, no adjustment) and the receipt ends as "Completed with variance". The screen (Receiving in master-detail, with the tabs
Receipts, Arrival notices and Pending putaway) is described in the screens manual:
[F6 — Receiving](frontend/f6-almacen-e-inventario.md#receiving-receipts-and-arrival-notices).

Who can: `inventory.view` (list and view); `warehouse.receive` (create ASN and receipt, enter
lines, confirm, cancel/delete). Receiving against a purchase order **also** requires `purchasing.receive` and
the **PURCHASING** module turned on. Module **WMS_LOTSERIAL**.

How it is used:
- `GET/POST /api/v1/asns`, `POST /api/v1/asns/{id}/cancel`.
- `GET /api/v1/receipts?warehousePublicId=&status=&types=&from=&to=&productPublicIds=&hasVariance=&search=&variance=&phase=`.
- `GET /api/v1/receipts/summary-by-warehouse` (2026-10-11, `inventory.view`): **open and pending-putaway receipts of ALL warehouses in a single call** (inactive ones too): `[ { warehousePublicId, openCount, pendingPutawayCount } ]`.
  `openCount` = receipts in Expected, Receiving or Discrepancy; `pendingPutawayCount` = Completed or Completed with variance; they are the same totals as `GET /receipts?phase=OPEN` and `?phase=PENDING_PUTAWAY` with `take=1`,
  warehouse by warehouse (only active receipts of the company). A warehouse without receipts comes out with 0 and 0. It is used by the Tenant settings → Operations → “Receiving by warehouse” screen (before, it made two queries per warehouse).
  Batch 13:
  - `phase` = receipt phase: `OPEN` (Expected, Receiving or Discrepancy), `PENDING_PUTAWAY` (Completed or
    Completed with variance: confirmed with putaway still to close) or `DONE` (Put away). It is the basis of the
    "Pending putaway" tab.
  - `variance` (can be repeated; several combine with "or"): `SHORT` = shortage (some line received less than
    expected), `OVER` = overage (some line received more, or an extra line of a receipt with notice or purchase order received
    something), `NONE` = no variance (no line differs).
  - `status` accepts the six new codes (`EXPECTED`, `RECEIVING`, `DISCREPANCY`, `RECEIVED`, `RECEIVED_VARIANCE`,
    `PUTAWAY`); `OPEN` no longer exists.
  - `search` also looks in the carrier and the reference of the receipt.
  - Each row also brings `carrier`, `reference`, `expectedDate` (the expected arrival of the notice; if it has none, that of the
    purchase order), `defaultStagingBinId`/`defaultStagingBinCode`, `dockId`, `isOpen` and `pendingPutawayCount` (open putaway
    tasks). The record brings `canDelete` (open and with no cross-dock assigned).
- `POST /api/v1/receipts` — against ASN (`asnId`), against PO (`purchaseOrderPublicId`), blind (`type: "BLIND"`,
  by default) or return (`type: "RETURN"`), with optional `stagingBinId` (if not, the first active bin
  of a `STAGING` zone of the warehouse). Batch 13: `stagingBinId` becomes the **default receiving bin** of the
  header (taken by the lines added without a bin); `carrier` and `reference` (up to 80 characters; empty = no
  data). A blind or return receipt **without `lines`** creates only the header in **Expected** (before it responded 400); against a notice or
  purchase order it is born in **Receiving** (or **Discrepancy** if what is entered in `lines` differs from what is expected). In the `lines`
  of a blind or return receipt `expectedQty` can be sent.
- `PATCH /api/v1/receipts/{publicId}` (Batch 13, `warehouse.receive`) — edits the header of an **open** receipt
  (Expected, Receiving or Discrepancy). `null` or absent = do not change:
  - `type`: `BLIND` ↔ `RETURN`, only in receipts without notice or purchase order;
  - `warehousePublicId`: only without notice or purchase order and **without lines**; when changed, the default bin
    and the dock are cleared (the new warehouse must be active and have a `STAGING` zone, as on create);
  - `stagingBinId` (`STAGING` or `CROSSDOCK` bin of the receipt's warehouse) or `clearStagingBin: true`;
  - `dockId` (dock of the receipt's warehouse) or `clearDock: true`;
  - `carrier` and `reference`: text of up to 80; `""` clears them;
  - `rowVersion`: the one from the record; if the receipt changed since it was read → 409.
- `PUT /api/v1/receipts/{publicId}/lines/{lineId}` — enters received quantity, lot and serials. Batch 13: `expectedQty`
  (or `clearExpected: true`) only in blind and return receipts; `productPublicId` changes the product of a line that does **not**
  come from the notice or purchase order (same owner as required when adding a line; the lot and serials of the previous product are
  cleared if new ones are not sent).
- **Lot, expiration and serials on the same line (web, 2026-10-20, Batch 140).** In a receipt without a document (blind or return), when choosing a **lot-tracked** product
  the line itself shows, below the product, **Lot\* · Manufactured · Expiry**; with a **serial-tracked** product it shows the **“Serial numbers, one per line”** box
  (what is received is the number of serials). The line is saved once the received quantity and the lot (or the serials) are in and you leave those fields; going from the lot to the dates
  does **not** save it early. It goes in the same `POST …/lines` (`lot` and `serialNumbers`); the API did not change. While the lot (or the serials) is missing the line stays
  “unsaved” and Confirm warns about it. A line **already saved** (or one coming from a notice or purchase order) keeps using the **“Lot and serials”** icon.
  The messages are the usual ones (`The product … is lot-tracked…`, `The expiry date cannot be earlier than the manufacture date.`, repeated serials, etc.).
- `POST/DELETE /api/v1/receipts/{publicId}/lines` (extra line, open receipts only). Batch 13: `expectedQty` only in
  blind and return receipts; a line without `stagingBinId` takes the header's default bin (if there is none, that of another
  line or the first `STAGING`).
- Every line create, change or delete **syncs the open status**: **Unloading** (formerly “Receiving”) if everything matches or only receiving is pending (the shortage is what is still being unloaded), **Discrepancy** only if some
  line brings MORE than expected (overage) (with history of each change).
- `POST /api/v1/receipts/{publicId}/confirm` — confirms the **whole** receipt (there is no per-line confirmation). Batch 13:
  it ends in **Completed** (`RECEIVED`) or, if some line has a variance, in **Completed with variance**
  (`RECEIVED_VARIANCE`); the comment goes in that last step of the history.
- Batch 8A (device queue) — `POST /api/v1/receipts` with `confirm: true` creates, enters and confirms in a single
  transaction. Against a notice (`asnId`) or a purchase order (`purchaseOrderPublicId`), the `lines` of the request
  are applied over the document lines: **what was scanned prevails** (by product and, if it has a lot, by lot); a line
  of the document that the request does not mention is received as 0 (visible shortage) and a product that is not in the
  document comes in as an extra line (it does not count against the purchase order). Without `lines` the expected quantity is received, as in the
  step-by-step create. Same validations and messages as the step-by-step entry, with the key `lines[i]`.
- `DELETE /api/v1/receipts/{publicId}` — open ones only (Expected, Receiving or Discrepancy) and with no cross-dock
  assigned; it is a soft delete (the receipt is no longer listed, its lines are kept).
- `GET /api/v1/asns` (Batch 13) — in addition to `warehousePublicId`, `status`, `clientPublicId` and `search`: `reference` (the
  reference contains the text, case-insensitive) and `expectedFrom`/`expectedTo` (expected arrival, both
  ends included; a notice without a date does not fall in the range). It still returns up to 200 notices (the mobile app
  downloads them that way).
- Batch 16 (direct-to-bin receipt): `receivingMode` on create and in the receipt `PATCH`; `targetBinId` or `targetBinCode` on each line;
  `GET /api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions`; `POST /api/v1/receipts/{publicId}/targets/suggest`; and `includeLines=true` in the
  list. All in section 4.1.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Unknown type | `Unknown receiving type: 'X'. Use ASN, BLIND or RETURN.` | 400 |
| Type `ASN` without `asnId` or `purchaseOrderPublicId` | `A receipt with an arrival notice is created by giving the notice (asnId) or the purchase order (purchaseOrderPublicId).` | 400 |
| `asnId` and `purchaseOrderPublicId` at the same time | `Enter the arrival notice or the purchase order, not both.` | 400 |
| No receiving bin (no `STAGING` zone; **does not apply to a direct receipt**, section 4.1) | `The warehouse has no receiving bin (STAGING zone); enter one.` | 422 |
| Receiving bin outside a STAGING/CROSSDOCK zone | `The receiving bin must be in a STAGING or CROSSDOCK zone.` | 400 |
| ASN/PO warehouse other than the one given | `The arrival notice belongs to another warehouse.` / `The purchase order belongs to another warehouse.` | 400 |
| ASN not `EXPECTED` | `The arrival notice is not pending receipt.` | 422 |
| ASN with an open or confirmed receipt | `The arrival notice already has an open or confirmed receipt.` | 409 |
| PO not `SENT`/`PARTIAL` | `The purchase order must be sent or partially received to receive against it.` | 422 |
| PO with nothing pending | `The purchase order has no quantities pending receipt.` | 422 |
| PO with a receipt already open | `The purchase order already has an open receipt; confirm or delete it before receiving again.` | 409 |
| Product from a client other than the ASN's | `Product {sku} does not belong to the client of the arrival notice.` | 400 |
| PO with a client's product | `The purchase order only allows own products; {sku} belongs to a client.` | 400 |
| Receipt already confirmed (edit) | `Receipt {n} was already confirmed; it cannot be modified.` | 422 |
| Notice line deleted | `Arrival notice lines are not deleted; enter 0 as received.` | 400 |
| Line with cross-dock assigned | `The line has cross-dock assignments; cancel them before deleting it.` | 409 |
| Receipt with cross-dock assigned (delete) | `The receipt has cross-dock assignments; cancel them before deleting it.` | 409 |
| More than 200 lines | `The receipt allows at most 200 lines.` (notice: `An advance shipping notice allows at most 200 lines.`) | 400 |
| No lines on confirm | `The receipt has no lines; add at least one before confirming.` | 422 |
| `receivedQty` absent / negative | `Enter the received quantity.` / `The received quantity cannot be negative.` | 400 |
| Expected quantity ≤ 0 (ASN) | `The expected quantity must be greater than zero.` | 400 |
| Lot required / not allowed according to tracking | `Product {sku} is lot-tracked: enter the lot.` / `Product {sku} is not lot-tracked; do not enter a lot.` | 400 |
| Serial: non-whole quantity / does not match what was entered | `Product {sku} is serial-tracked: the quantity must be a whole number.` / `Product {sku} is serial-tracked: enter {qty} serial number(s) (there are {n}).` | 400 |
| Repeated serial | `Serial number 'X' is repeated.` | 400 |
| Inactive warehouse | `The warehouse is deactivated; it does not allow receipts.` | 422 |
| Inactive product | `Product {sku} is deactivated; it cannot be received.` | 422 |
| Receipt from another tenant or nonexistent | `Receipt not found.` | 404 |
| Second confirmation (receipt already confirmed) | same `Receipt {n} was already confirmed; it cannot be modified.` | 422 |
| `confirm: true` in a blind or return receipt without `lines` (Batch 13: without `confirm` it is no longer an error) | `Enter at least one line.` | 400 |
| `expectedQty` in a receipt with a notice or purchase order (Batch 13) | `The expected quantity is only entered in blind or return receipts; in one with an arrival notice or purchase order it comes from the document.` (key `expectedQty`, `line.expectedQty` when adding a line or `lines[i].expectedQty` on create) | 400 |
| Negative `expectedQty` (Batch 13) | `The expected quantity cannot be negative.` (more than 3 decimals: `The quantity allows at most 3 decimals.`; too large: `The quantity exceeds the maximum allowed.`; serial-tracked product: `Product {sku} is serial-tracked: the quantity must be a whole number.`) | 400 |
| Changing the product of a notice or purchase order line (Batch 13) | `The product of an arrival notice or purchase order line cannot be changed.` | 400 |
| Changing the product of a line with cross-dock assigned (Batch 13) | `The line has cross-dock assignments; cancel them before changing the product.` | 409 |
| `PATCH` of the type of a receipt with a notice or purchase order (Batch 13) | `The type of a receipt with an arrival notice or purchase order cannot be changed.` | 400 |
| `PATCH` of the warehouse with a document or with lines (Batch 13) | `The warehouse can only be changed in a receipt without an arrival notice or purchase order and without lines.` | 400 |
| Carrier or reference longer than 80 characters (Batch 13) | `The carrier allows at most 80 characters.` / `The reference allows at most 80 characters.` | 400 |
| `PATCH` of a confirmed receipt (Batch 13) | `Receipt {n} was already confirmed; it cannot be modified.` | 422 |
| `PATCH` with an old `rowVersion` (Batch 13) | `The record was modified by another user; reload and try again.` | 409 |
| Dock from another warehouse (create or `PATCH`) | `Dock not found.` | 404 |
| Unknown `variance` filter (Batch 13) | `Unknown variance: 'X'. Use SHORT, OVER or NONE.` | 400 |
| Unknown `phase` filter (Batch 13) | `Unknown phase: 'X'. Use OPEN, PENDING_PUTAWAY or DONE.` | 400 |
| Empty lot number / longer than 60 characters | `Enter the lot number.` / `The lot number allows at most 60 characters.` | 400 |
| Expiration earlier than manufacture | `The expiry date cannot be earlier than the manufacture date.` | 400 |
| Lot that already exists with other dates | `Lot {n} already exists with other dates; correct the dates or use another lot number.` | 409 |
| Empty serial / longer than 80 characters / more than 500 per line | `Serial numbers cannot be empty.` / `Each serial number allows at most 80 characters.` / `A line allows at most 500 serial numbers.` | 400 |
| Product without serial with serials entered | `Product {sku} is not serial-tracked; do not enter serial numbers.` | 400 |
| Quantity (received or expected) with more than 3 decimals / too large | `The quantity allows at most 3 decimals.` / `The quantity exceeds the maximum allowed.` | 400 |
| Confirming with incomplete tracking on a line | the lot and serial messages above, in `errors["lines[i]"]` (title `Invalid data.`) | 400 |
| Confirming with an old `rowVersion` | `The record was modified by another user; reload and try again.` | 409 |
| `rowVersion` that is not base64 (confirm or `PATCH`) | `Invalid rowVersion: the base64 value returned by the record is expected.` | 400 |
| Line from another receipt or nonexistent | `Receipt line not found.` | 404 |
| Nonexistent product (new line or product change) | `Product not found.` | 404 |
| Nonexistent warehouse or from another company (create or `PATCH`) | `Warehouse not found.` | 404 |
| Blind or return receipt without `warehousePublicId` with more than one active warehouse / no active warehouses | `Enter the warehouse: the company has more than one.` (400, `errors.warehousePublicId`) / `The company has no active warehouses.` (422) | 400 / 422 |
| Receiving bin from another warehouse or nonexistent (create, line or `PATCH`) | `Bin not found.` | 404 |
| Deactivated receiving bin | `The receiving bin is deactivated.` | 422 |
| Nonexistent arrival notice | `Arrival notice not found.` | 404 |
| Arrival notice without a client | `Enter the client that owns the goods of the notice.` | 400 |
| Arrival notice without lines / with more than 200 | `Enter at least one line.` / `An advance shipping notice allows at most 200 lines.` (`errors.lines`) | 400 |
| Notice reference longer than 80 characters | `The reference allows at most 80 characters.` | 400 |
| Cancelling a notice that is no longer pending | `The arrival notice is not pending receipt.` | 422 |
| Cancelling a notice that has a receipt | `The arrival notice has an open receipt; delete it before cancelling.` | 409 |
| Receiving against a PO without `purchasing.receive` | `Missing permission 'purchasing.receive'.` | 403 |
| Receiving against a PO with the PURCHASING module off | `The 'PURCHASING' module is not enabled for this company.` | 403 |

### Statuses and transitions

`AsnStatus`: **EXPECTED** (initial) → **RECEIVED** (when its receipt is confirmed); **CANCELLED** (lateral from
`EXPECTED`, only if it has no open receipt — if it does, `409` `The arrival notice has an open receipt;
delete it before cancelling.`).

`ReceiptStatus` (Batch 13; before `OPEN` → `RECEIVED` → `PUTAWAY`):

| Code | Label | Kind | Meaning |
|---|---|---|---|
| `EXPECTED` | Expected | initial | Header only (blind or return receipt without lines). |
| `RECEIVING` | Unloading | stage | With lines and everything matches, or only receiving is pending (it is being unloaded and put away in parts), or nothing to compare. |
| `DISCREPANCY` | Discrepancy | lateral | Some line received MORE than expected. A shortage with the receipt open is not one. |
| `RECEIVED` | Completed | stage | Confirmed without variance; putaway pending. |
| `RECEIVED_VARIANCE` | Completed with variance | lateral | Confirmed with variance; putaway pending. |
| `PUTAWAY` | Put away | terminal | The last putaway was closed (or there was nothing to put away). |

Transitions (all through the status engine, with history in `/api/v1/status/history/RECEIPT/{id}`; each step leaves a row):

| From → to | Who | What it validates | Effects |
|---|---|---|---|
| (create) → **Expected** | `warehouse.receive` | Blind or return receipt without `lines` and without `confirm`. The warehouse must be active and have a `STAGING` zone (or bring `stagingBinId`). | Creates the header with number `REC-#####`, without lines. |
| (create) → **Receiving** | `warehouse.receive`; against a purchase order, also `purchasing.receive` and the PURCHASING module | Against a notice: notice `EXPECTED` and without a receipt. Against an order: `SENT` or `PARTIAL`, with pending quantity and no open receipt. Blind or return receipt with `lines`: all lines match. | Creates one line per document line (received = expected); a purchase order also generates its notice. |
| (create) → Receiving → **Discrepancy** | same | `lines` with overage: received greater than expected. | Two steps in the history. |
| Expected → **Receiving** | whoever saves the line (`warehouse.receive`) | The line passes the entry validations. | With overage, Expected → Unloading → **Discrepancy** (two steps). |
| Receiving → **Discrepancy** | whoever saves the line | Some line with received GREATER than expected. | — |
| Discrepancy → **Receiving** | whoever saves the line | No line with overage. | — |
| Receiving → **Completed** | `warehouse.receive` (`POST .../confirm`) | Open receipt (otherwise 422); current `rowVersion` if sent (409); at least one line (422); tracking of each line (400 per line: lot with received greater than 0, serials equal to the quantity); with a purchase order, that it is still receivable. | See "Effects of confirming". |
| Unloading or Discrepancy → **Completed with variance** (closing with shortage or overage) | same | same | See "Effects of confirming". |
| Completed → **Put away** | the system (effect of the tasks) | The last `PUTAWAY` of the receipt reaches `DONE` or `CANCELLED` and no other one is left open. Starting and completing require `warehouse.receive`; assigning and cancelling, `warehouse.manage`. | — |
| Completed with variance → **Put away** | the system | same | — |
| Completed or Completed with variance → **Put away** (direct) | the system, on confirm | No `PUTAWAY` was created: everything went to cross-dock, 0 was received or the receipt is **direct to bin** (section 4.1). | — |
| Expected, Receiving or Discrepancy → deletion (`DELETE`) | `warehouse.receive` | Open receipt (otherwise 422) and no cross-dock assigned (409). | Soft delete (the receipt is no longer listed, the lines are kept). A notice born from a purchase order is cancelled; a client's goes back to pending. |

**Effects of confirming** (a single transaction):
- Ledger: with a notice or purchase order, `RECEIPT` for the expected quantity plus `ADJUSTMENT RECEIPT_VARIANCE` for the variance (D4; the
  system adjustment does not ask for a note). In blind and return receipts, a single `RECEIPT` for what was **received**: the variance only marks the
  status and `adjustmentTxnId` stays empty.
- The purchase order advances to `PARTIAL` or `RECEIVED`; the notice moves to `RECEIVED`.
- The assigned cross-dock is split and the `PUTAWAY` tasks for the remainder are created with a suggested bin. In a **direct** receipt there is no `PUTAWAY`: the
  goods go into the target bin of each line (section 4.1).
- The receipt saves the confirmation date and user (`receivedAtUtc`, `receivedBy`) and Recent activity records
  `RECEIPT_CONFIRMED` (for both Completed and Completed with variance).
- The destination is **Completed with variance** if some line has a variance and that status is turned on for the company; otherwise,
  **Completed**. The comment in the body goes in the last step of the history.

**What can be done in each status:**

| Status | Allowed | Blocked |
|---|---|---|
| Expected | Edit the header (`PATCH`), add lines and delete the receipt. | Confirm: without lines it responds `422` `The receipt has no lines; add at least one before confirming.` |
| Receiving | All of the above, change or remove lines, confirm and assign cross-dock against what a line received. | — |
| Discrepancy | Same as Receiving. The variance is corrected by saving the lines or it is confirmed as is. | — |
| Completed | View, work its putaway tasks and assign cross-dock against the remainder with putaway pending. | `PATCH`, adding, changing and deleting lines, confirm and `DELETE`: `422` `Receipt {n} was already confirmed; it cannot be modified.` |
| Completed with variance | Same as Completed. | Same as Completed. |
| Put away | View. | Same as Completed. |

An open receipt also blocks actions on other documents: cancelling, deleting or changing the lines of the purchase order it
was born from (`409` `The purchase order has an open receipt; confirm or delete it before cancelling.` / `... delete it before
deleting the purchase order.` / `... confirm or delete it before changing its lines.`), cancelling its notice (`409`), deactivating
the product of one of its lines or the warehouse.

Per company and in existing databases:

- Seeded lateral entries (the company changes them in `/status/lateral-entries/RECEIPT`): Discrepancy from
  Receiving; Completed with variance from Receiving, Discrepancy and Expected; Put away from Completed with
  variance. If the company **turns off** a status: without Discrepancy the variance stays in Receiving; without Completed
  with variance it is confirmed as Completed; without Receiving the receipt stays in Expected until confirmed.
- Existing databases: the seed moves the receipts that were in `OPEN` to Receiving (with lines) or Expected (without lines),
  with the history line "Lote 13: nuevo ciclo de estatus del recibo.", and retires `OPEN`.

Deleting an open receipt (with no cross-dock assigned): if it was born from a PO, its ASN is **cancelled**; if it is a
client's, the ASN goes back to `EXPECTED` (pending receipt again). A receipt in Expected is deleted the same way.

Common cases (Batch 13):
- *A truck arrived without a notice*: create a blind receipt with the header only (warehouse, carrier, reference), add the
  lines as you count and, if you know what is expected (for example, the supplier's invoice), enter it on each line
  to see the variance before confirming.
- *The receipt ended in Discrepancy because of an entry error*: correct the quantity; when the overage is removed it goes back to Unloading by itself.
- *The receipt says “Unloading” and units are still missing (for example, they are still in the trailer)*: that is normal. Keep entering what comes down; use “Confirm batch” to put away what was already received without closing the receipt, and “Confirm receipt” when you finish. If you close with units still to arrive, what is missing stays as a variance and the receipt moves to Completed with variance (it can be closed with a shortage; a warning appears).
- *Finding receipts with a shortage that are still open*: `GET /api/v1/receipts?phase=OPEN&variance=SHORT`.
- *Finding what is left to put away*: `GET /api/v1/receipts?phase=PENDING_PUTAWAY`; each row brings `pendingPutawayCount`.
- *Less arrived than the purchase order said*: enter what was received on the document line (0 if it did not arrive); the receipt
  stays in Unloading (the shortage is not a discrepancy while the receipt is open) and, on confirm, moves to Completed with variance. The shortage against the order is resolved later in Purchases
  (section 8), because the receipt confirmed with variance counts as a confirmed receipt for shortages.
- *Something arrived that was not in the notice or the order*: the line cannot be added from the web; through the API it is an extra line
  (`POST .../lines`) that comes in as a `RECEIPT_VARIANCE` adjustment and does not count against the order.
- *Correcting the carrier or the reference*: `PATCH /api/v1/receipts/{publicId}` with the record's `rowVersion`; while the receipt
  is open it can be repeated as many times as needed.

### 4.1 Receipt direct to bin (Batch 16)

What it does: in a **direct to bin** receipt the goods **do not pass through the receiving bin**. Each line that receives something carries its **target bin** and,
on confirming, goes in there (ledger `RECEIPT` to that bin; with an ASN or purchase order and a variance, also the `RECEIPT_VARIANCE` adjustment in that same
bin), **with no putaway tasks**. The receipt goes from Completed to **Put away** at the same moment. A warehouse without a `STAGING` zone (Advance Solutions)
can receive this way. The warehouse mode and the default receiving bin are in section 1.4.

**The receipt keeps the mode it was opened with.** The list and the record carry `receivingModeCode` (`PUTAWAY` or `DIRECT`) and `receivingMode` (the label). Changing the
warehouse mode **does not change receipts already open**. While the receipt is open (Expected, Receiving or Discrepancy), whoever can receive
(`warehouse.receive`) changes the mode **of that receipt only**.

Who can: `warehouse.receive` (receipt mode, target bin of each line, "Use suggested bins" and confirm); `inventory.view` (see the
suggestions of a line). Module **WMS_LOTSERIAL**. Receiving against a purchase order also requires `purchasing.receive` and the **PURCHASING** module.

How it is used:
- **Open a direct receipt.** `POST /api/v1/receipts` with `receivingMode` (`PUTAWAY` or `DIRECT`, case-insensitive). Without it, the receipt takes the
  warehouse mode. In direct mode the receiving bin **is not requested**.
  ```json
  { "warehousePublicId": "…", "type": "BLIND", "receivingMode": "DIRECT",
    "lines": [ { "productPublicId": "…", "receivedQty": 8, "targetBinCode": "R-02" } ] }
  ```
- **Target bin of a line.** By id (`targetBinId`) or by code (`targetBinCode`, the one the app scans; compared case-insensitively), **not both**.
  It goes in `lines[]` on creation, in `POST …/lines` and in `PUT …/lines/{lineId}` (with `targetBinId`; `"clearTargetBin": true` removes it).
- **Change the mode of an open receipt.** `PATCH /api/v1/receipts/{publicId}` with `{ "receivingMode": "PUTAWAY", "rowVersion": "…" }`. Switching to "With putaway"
  requires a receiving bin (the one in the header or the warehouse's). If the receipt already had targets per line, the putaway task of each line goes to its target.
- **Suggestions for a line.** `GET /api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions?take=3` (`take` from 1 to 10; 5 by default; a larger one is
  taken as 10). Returns an array:
  ```json
  [ { "binId": 41, "binCode": "R-02", "zoneCode": "RSV", "zoneTypeCode": "RESERVE", "reasonCode": "RESERVE_EMPTY",
      "reason": "Reserva vacía", "maxCapacityQty": 40, "qtyOnHand": 0, "claimedQty": 0, "freeQty": 40, "fits": true } ]
  ```
- **Use suggested bins.** `POST /api/v1/receipts/{publicId}/targets/suggest` with `{ "rowVersion": "…" }` (optional). Returns `{ "receipt": {…}, "assigned": 2,
  "withoutSuggestion": 1 }`: it assigns the first suggested bin **where it fits** to each line without a target that needs one. **Nothing is filled in automatically**: only when requested.
- **Confirm.** `POST /api/v1/receipts/{publicId}/confirm`, as always.
- **List with lines.** `GET /api/v1/receipts?includeLines=true` returns in `lines` the lines of each receipt on the page (the same ones as the record), in a single
  query; without the parameter, `lines` is `null`. The Receiving export uses it (screens manual).

Fields and rules:

| Field | Rule |
|---|---|
| `receivingMode` (creation and `PATCH` of the receipt) | `PUTAWAY` or `DIRECT`. Empty or absent = the warehouse's (in the `PATCH`, no change) |
| `targetBinId` / `targetBinCode` (line) | Bin **of the receipt's warehouse**, **a storage bin** (not `STAGING` nor `CROSSDOCK`; **quarantine is allowed**), active and with its zone active |
| `clearTargetBin` (`PUT` of the line) | `true` removes the target |
| Target required | On confirming, in each line that receives something (received greater than 0). Not required for a **lot-tracked product without a lot** (it moves no inventory) nor for a line with cross-dock assigned |
| Line quantity | A line goes **entirely to a single bin**. To split it, transfer afterwards (section 3.2) |

**Suggestions and capacity.**
- The suggestion is the usual directed putaway: the product's preferred bin, same lot, same product, empty picking bin if the product rotates a lot (by the
  outbound movements of the last 30 days), empty reserve and reserve with space. Reasons: `PREFERRED`, `CONSOLIDATE_LOT`, `CONSOLIDATE`, `PICKING_FAST`, `RESERVE_EMPTY`,
  `RESERVE` (and, in returns, `QUARANTINE_RETURN`). The quantity is the line's received quantity (if it is 0, the expected one; otherwise, 1).
- **Capacity counts units.** Free space = `capacity − stock in the bin (all products) − what other lines of the same receipt already target to that bin`,
  never negative; with no capacity configured, `freeQty` is `null` and it never exceeds. `fits` says whether the line quantity fits. Those that fit come **first**; those that
  exceed, at the end with `fits: false`. Consolidation also looks at the other lines of the same product in the receipt.
- **Capacity only warns** (D4). You can choose a bin where not everything fits: the line record carries `targetFreeQty` (the free space; only with the receipt
  open and a target with capacity; it does not deduct the line itself) and the screen shows "Exceeds the capacity of {bin}: {free} fit". It does not block saving or confirming; whatever is left over
  is transferred afterwards. Two receipts open at the same time do not see each other: both can exceed the same capacity.
- "Use suggested bins" **only assigns where it fits**. A line with no bin where it fits is left without a target and is counted in `withoutSuggestion`.
- **Since Batch 16, the directed putaway of the usual tasks also respects capacity in units**: it skips the bin where the quantity does not fit. The bins of Advance
  Depot have estimated capacities (section 1.2), so putaway suggestions may change.

**Returns and quarantine.** In a **return** receipt, the first suggestion is a **quarantine** bin if one exists (`QUARANTINE_RETURN`, "Quarantine (return)").
In the other receipts quarantine is not suggested, but it can be chosen manually.

**Cross-dock.** A line with cross-dock assigned **goes into the receiving bin** as always and does not require a target; its remainder generates a putaway
task to its target bin (or the suggested one). The receipt stays **Completed** until that task is closed (section 9).

**Receipts from the previous app.** The new app scans the target bin of each line (chapter 9, section 4). A receipt from a previous app, sent all at once (`confirm: true`)
without `receivingMode` and without any target bin in a **direct** warehouse, is received **"With putaway"** (with tasks) and is not lost.

### Validations (direct receipt)

| Field / case | Exact message | HTTP |
|---|---|---|
| Unknown `receivingMode` (creation or `PATCH` of the receipt; `errors.receivingMode`) | `Unknown receiving mode: 'X'. Use PUTAWAY or DIRECT.` | 400 |
| Confirming a direct receipt with a line that receives something and has no target (`errors["lines[i].targetBinId"]`; `i` is the line position, from 0) | `Specify the target bin for {sku}: the receipt goes direct to bin.` | 400 |
| Target bin in a receiving or cross-dock zone (when saving the line or confirming) | `Bin {code} is in a {zoneType} zone; the target bin must be a storage bin.` | 400 |
| `targetBinCode` that does not exist in the receipt's warehouse (`errors.targetBinCode`; on creation, `errors["lines[i].targetBinCode"]`) | `Bin {code} does not exist in the receipt's warehouse.` | 400 |
| `targetBinId` and `targetBinCode` at the same time (`errors.targetBinId`) | `Specify the target bin by id or by code, not both.` | 400 |
| `targetBinId` of another warehouse, another company or nonexistent | `Bin not found.` | 404 |
| Target bin deactivated (when saving the line or confirming) | `Target bin {code} is deactivated.` | 422 |
| Target bin's zone inactive (when saving the line or confirming) | `The zone of target bin {code} is inactive.` | 422 |
| Atomic creation with an ASN or purchase order: the same product with two different targets (`errors["lines[i].targetBinCode"]`) | `{sku} was already captured with destination {code}; on a receipt with an ASN or purchase order each line goes to a single bin.` | 400 |
| `PATCH` of the receipt to `PUTAWAY` without a receiving bin (the warehouse has no `STAGING` zone and the header does not specify one) | `The warehouse has no receiving bin (STAGING zone); specify one.` | 422 |
| Saving a line, changing the mode or "Use suggested bins" on an already confirmed receipt | `Receipt {n} was already confirmed; it cannot be modified.` | 422 |
| "Use suggested bins" with an old `rowVersion` | `The record was modified by another user; reload and try again.` | 409 |
| Suggestions for a line that does not belong to this receipt | `Receipt line not found.` | 404 |

The error keys of a line follow the shape of the request: `targetBinId` or `targetBinCode` in the `PUT`, `line.…` when adding a line and `lines[i].…`
on creation or when confirming. The other receipt messages (lot, serials, quantities) are those of the table in section 4.

### Statuses and transitions (direct receipt)

The statuses are the six of section 4: **there are no new statuses**. What changes is the ending.

| From → to | Who | What it validates | Effects |
|---|---|---|---|
| (creation) → **Expected** or **Receiving** / **Discrepancy** | `warehouse.receive` | Active warehouse. Does **not** require a receiving bin. If the creation carries targets, the rules of the table above | Creates the header with the mode it was opened with; same as with putaway |
| Expected → **Receiving** ↔ **Discrepancy** | `warehouse.receive` | Each saved line (the target does not change the status) | — |
| Receiving → **Completed** → **Put away** (a single `confirm`) | `warehouse.receive` | Target in each line that receives something (400 per line); target and zone active (422); lot and serial tracking | `RECEIPT` to each target; no task; the history records **Receiving → Completed → Put away** (D7); Recent activity logs a single "Receipt … confirmed" (D13) |
| Discrepancy → **Completed with variance** → **Put away** | `warehouse.receive` | Same | Same, plus the `RECEIPT_VARIANCE` adjustment in the target if there is an ASN or purchase order. Uses the seeded lateral entry `RECEIVED_VARIANCE → PUTAWAY`; if the company removed it, the status engine rejects the step (not tested) |
| Receiving or Discrepancy → **Completed** or **Completed with variance** (**stays there**) | the system | Some line has cross-dock assigned | That line goes into the receiving bin. If there is a remainder (received minus what was assigned to the cross-dock), a putaway task to its target is created and the receipt **stays** there until it is closed; if everything went to the cross-dock, it goes to Put away at the same moment |

What is blocked in each status (direct receipt):

| Status | Allowed | Blocked |
|---|---|---|
| Expected, Receiving, Discrepancy | Set, change or remove the target of each line; "Use suggested bins"; change the mode **of this receipt**; confirm | Confirming without a target in a line that receives something: `400` with the message in the table |
| Completed, Completed with variance (only if there was cross-dock) | View and work the remainder tasks | Target, mode, "Use suggested bins", adding and removing lines: `422` `Receipt {n} was already confirmed; it cannot be modified.` |
| Put away | View | Same as the previous one |

Frequent cases (Batch 16):
- *I want a warehouse to receive direct*: change its **Receiving mode** (section 1.4). New receipts are born direct; open ones keep their mode.
- *An open receipt must be put away afterwards*: switch **only that receipt** to "With putaway" before confirming (`PATCH` of the receipt). The putaway tasks are created, to the target of each
  line if it has one.
- *More arrived than fits in the bin*: choose the bin anyway (the screen warns, it does not block) and transfer the excess afterwards (section 3.2), or split the goods into two lines of the
  same product if the receipt is blind or a return.
- *A line has no suggested bin*: no storage bin has space. Choose one manually.
- *A direct receipt does not appear in "Pending putaway"*: that is expected; only the receipts prior to the mode change and those that had cross-dock appear there (section 5).

---

## 4b. Damage: what arrived damaged or was damaged in the warehouse (2026-10-08)

**What it does.** Records a damage with its cause and sends the damaged goods to **quarantine** (by default to the first bin of a Quarantine-type zone; you can also specify **any other active bin** of the warehouse) or **releases them right away**. What remains in quarantine is **taken out** later (**Release**: thrown away, returned, donated…) or **recovered** to a storage bin. Every inventory movement is made by the ledger (adjustment with reason *Damage* or transfer): it is recorded in the movement ledger with the note `DAN-00012 · {cause} · {note}`. There are no photos or claims: the **cause** is informational only.

**Who and where.** Permission **Report and resolve damage** (`warehouse.damage`; included in “Warehouse operator”; WMS module). Web: **Warehouse → Products & inventory → Damage tab** (`/warehouse/products?tab=damage`; the previous address `/warehouse/damage` redirects there) and the **Report damage** button on a receipt record. App: **Damage** tile on Home (only with that permission).

**Two origins.**
- **Damaged in the warehouse** (the goods are already in inventory): you specify the **bin where they were**. *Quarantine* = transfer from that bin to the quarantine bin. *Release* = negative adjustment with reason Damage.
- **Arrived damaged in a receipt** (those units **do not go in as good**): you specify the **receipt**. *Quarantine* = those units go directly into the quarantine bin (positive adjustment, reason Damage). *Release right away* = they do not enter inventory; only the report remains. When confirming the receipt, receive only the good ones; the damaged ones are reported separately (you see the variance against what was expected as always). **Better: declare the damage on the receipt line itself** (see “Damage declared on the receipt line”, below): that way nothing has to be reported separately.

**Damaged goods are not sold, but they are counted.** Damaged goods that remain IN QUARANTINE are still stock of that bin: they **are seen and counted** in the cycle count (the counter sees the boxes and the system expects them; there is no false variance). What cannot happen is that they get dispatched:
- In a bin of a **Quarantine** zone (also *cross-dock* or *rental*) they are already out of allocation: dispatch, outbound suggestions and replenishment do not take them.
- In **any other bin** (storage, receiving…), where they are mixed with good goods, the system **reserves** those units (`QtyReserved`): the stock does not change, but the **available** quantity drops and they are not allocated or dispatched. The Damage list marks it with “reserved (not dispatched)”. The reservation is released automatically on **discarding** or **recovering**.

**Cause:** *Came that way*, *Accident in transit*, *Warehouse accident*, *Other* (`DamageCause` list). By default, a receipt damage proposes *Came that way* and a warehouse damage *Warehouse accident*.

**Status** (`DamageStatus`): Reported (initial) → **In quarantine** → **Released** (formerly “Discarded”) or **Recovered** (terminal); also Reported → Released when it is released right away. A resolved damage is not resolved again.

**How it is used.**
1. *Report:* web (Products & inventory → **Damage** tab → **Report damage**, or from the receipt record) or app (Home → **Damage**). You choose origin, product (and lot if the product is lot-tracked), quantity, cause and **Send to quarantine / Release right away** (the latter accepts an optional final destination). When sending to quarantine the web asks **“Bin where it stays”** (optional): empty, it is the first active quarantine bin of the warehouse by code; it can be any active bin.
2. *Release or recover what is in quarantine:* in the list of the Damage tab, **Release** icons (**final destination required** and optional note) and **Recover** (you must choose the storage bin; never quarantine, receiving, cross-dock or rental). The app only reports; resolving is done on the web.
3. *Look up:* the list filters by status, origin and free-text search (SKU, product or `DAN-00012`). The `DAMAGE_REPORT` data source is available for views, indicators and charts (by cause, origin, product or status).

**Validations (exact message and code).**
| Situation | Message | HTTP |
|---|---|---|
| No quantity or ≤ 0 | `The damaged quantity must be greater than 0.` | 400 |
| No product | `Specify the damaged product.` | 400 |
| No cause | `Say how the damage happened.` | 400 |
| No destination | `Specify whether the damaged goods go to quarantine or are released.` | 400 |
| Unknown destination | `Unknown destination: '{value}'. Use QUARANTINE or DISCARD.` | 400 |
| Receipt origin without a receipt | `Say the receipt it arrived damaged in.` | 400 |
| Warehouse origin without a bin | `Say the position where the damaged goods are.` | 400 |
| Note longer than 300 characters | `The note cannot exceed 300 characters.` | 400 |
| Specified bin deactivated | `Bin {code} is deactivated.` | 400 |
| Release without a final destination | `Say where the goods that leave go (thrown away, returned to supplier, donated…).` | 400 |
| Final destination not in the list | `Unknown final destination: '{value}'.` | 400 |
| Recover without a bin | `Say the position the recovered goods return to.` | 400 |
| Recover to a zone not allowed | `Bin {code} is in a {type} zone; recovered goods return to a storage bin.` | 400 |
| More than what is in the bin | `Insufficient inventory of {sku} in {bin}: available {x}, requested {y}.` | 409 |
| The warehouse has no quarantine bin | `The warehouse has no active quarantine bin; create one in a Quarantine-type zone.` | 422 |
| Serial-tracked product | `Serial-tracked products are not reported here yet; use an inventory adjustment with reason Damage.` | 422 |
| Release or recover something that is not in quarantine | `Only a damage that is in quarantine can be resolved.` | 422 |
| Nonexistent damage, receipt, product, bin or lot | `… not found.` | 404 |

**Damage declared on the receipt line (2026-10-08).** When capturing a receipt line, the app and the web let you say *“of what was received, N units came damaged”*: damaged quantity, **reason** (list *Came that way*, *Accident in transit*, *Warehouse accident*, *Other*; with *Other* you must write the reason) and **where they stay** (a bin — by default the quarantine one if it exists, otherwise where the line stays —) or **release right away**. The damaged ones **count within what was received** (I received 100, 10 damaged → received 100, good 90). When the receipt is **confirmed**, the server creates a `DAN-00012` report per line with damage (origin *Arrived damaged*, linked to the receipt): the units go in with the rest of the line and leave from there to the specified bin (transfer) or are discarded (negative adjustment, reason Damage); the **putaway task counts only the good ones**. If the specified bin is the same one where the line landed there is no movement: the report stays *In quarantine* there. On the web: open receipt record → **Damaged units** icon on the row (and **Remove damage**); the row shows “3 damaged · Q-01” and, once confirmed, the `DAN-…`. Serial-tracked products: not declared here. If the receipt is against an ASN or purchase order and the same product is captured on several lines, the damaged quantity is added to the document line.

Line validations (`400`, under the field `damagedQty` / `damageCause` / `damageNote`):
| Situation | Message | HTTP |
|---|---|---|
| More damaged than received | `The damaged quantity cannot be greater than the received quantity.` | 400 |
| More than 3 decimals | `The damaged quantity allows up to 3 decimals.` | 400 |
| Negative damaged quantity | `The damaged quantity must be greater than 0.` | 400 |
| No reason | `Say how the damage happened.` | 400 |
| Reason *Other* without a comment | `Write the reason for the damage when the cause is Other.` | 400 |
| Serial-tracked product | `Serial-tracked products are not reported here yet; use an inventory adjustment with reason Damage.` | 400 |
| Lowering the received quantity below the damaged one | `The damaged quantity cannot be greater than the received quantity.` | 400 |
| Damage bin nonexistent / deactivated | `… not found.` (404) / `Bin {code} is deactivated.` | 404 / 422 |

**Final destination on release (when they come to pick it up).** Taking damaged goods out of quarantine **is not a dispatch** (there is no outbound order): it is recorded with **Release** (formerly “Discard”: that word is not used because what leaves is not necessarily thrown away), which lowers inventory with an adjustment of reason *Damage* and asks for the **final destination** from the editable `DamageFinalDestination` list: *Thrown away*, *Returned to supplier*, *Donated*, *Sold as salvage* (System → Lists can add or rename). It is recorded in the movement ledger note (`DAN-00012 · Salida: Devuelto al proveedor · …`), in the Damage list (**Final destination** column) and in the `DAMAGE_REPORT` data source. **It is also asked when releasing right away**: when reporting a damage (web and app) and when declaring the damage on a receipt line (web and app), “Release” asks **where it goes** before confirming (on the web it is required; the app offers the four factory destinations —Thrown away, Returned to supplier, Donated, Sold as salvage—; destinations the company adds to the list are chosen on the web). The server accepts the report without a destination (devices with the previous app): it stays empty = thrown away. In a receipt, the destination is kept in the `DAN-…` report created on confirming.

**Frequent cases.** *100 arrived and 10 came broken:* on the line mark 10 damaged (received 100) with their reason; on confirming, 90 good remain and 10 in quarantine. You can also receive 90 and report 10 separately with origin “Arrived damaged” (to quarantine or release). *A pallet fell in A-01:* report with origin “Damaged in the warehouse” and bin A-01. *I sent it to quarantine and it can be sold:* in the Damage tab, **Recover** to a storage bin. *Create the quarantine zone:* Warehouse → Bins → Quarantine-type zone with at least one bin.

## 5. Warehouse tasks: unified queue, directed putaway and replenishment

What it does: a single task queue by type (`PUTAWAY`, `REPLENISH`, `COUNT`, `CROSSDOCK`, and the `PICK`/
`PACK`/`LOAD` types that this batch does not complete from here). Each type with a handler declares its own permission and whether it is
completed from this screen or from its own (`COUNT` is only completed by reconciling the count). Putaway
suggests a bin according to consolidation rules and an approximation of rotation (high/low) by the outbound movements of
the last 30 days. Replenishment (on demand) moves inventory from `RESERVE` to the preferred picking bin
when its available quantity falls below the minimum.

Who can: `inventory.view` (list, record, putaway suggestions, and start/complete together with the permission of the
type's handler); `warehouse.manage` (assign and cancel); `warehouse.pick` (run replenishment). The permission to
start/complete depends on the type: `PUTAWAY` → `warehouse.receive`, `REPLENISH` → `warehouse.pick`, `COUNT` →
`warehouse.count`, `CROSSDOCK` → `warehouse.crossdock`. Module **WMS_LOTSERIAL**.

How it is used:
- `GET /api/v1/warehouse-tasks?warehousePublicId=&types=&status=&assignedToMe=&assignedUserId=&includeClosed=`
  — queue order: priority ascending, then oldest first.
- `GET /api/v1/warehouse-tasks/putaway-suggestions?taskId=` or `?productPublicId=&warehousePublicId=&lotId=&
  quantity=&fromBinId=`.
- `POST /api/v1/warehouse-tasks/{id}/assign` — `{ "userId": 12 }` (null unassigns).
- `POST /api/v1/warehouse-tasks/{id}/start`.
- `POST /api/v1/warehouse-tasks/{id}/complete` — `{ "toBinId": 8, "quantity": 5, "serialNumbers": [...] }` (all
  optional: by default the task's target/quantity).
- `POST /api/v1/warehouse-tasks/{id}/cancel` — only `PUTAWAY`/`REPLENISH`.
- `POST /api/v1/warehouse-tasks/replenishment/run` — `{ "warehousePublicId": "..." }` (or the only active one).

Batch 13 (screen): in the web application there is no longer a single task queue. Each type is worked on the screen it
belongs to: `PUTAWAY` in Receiving ("Pending putaway" tab and the receipt detail), `REPLENISH` in Picking & packing
("Replenishment" tab), `COUNT` in Cycle count and `CROSSDOCK` in Cross-dock. In all of them, the actions of each row are icons
with a tooltip: Assign, Start, Complete and Cancel. The permissions and endpoints above do not change. See
[F6 — Warehouse tasks](frontend/f6-almacen-e-inventario.md#warehouse-tasks).

**Detail of a split putaway task (2026-10-07).** The task keeps a single target bin, but a split putaway (the quantity per bin from the device or `POST /warehouse-tasks/{id}/distribute`) moved the inventory to several. `WarehouseTaskDto.moves` carries **one line per bin** taken from the ledger (`transactionId`, `fromBinCode`, `toBinCode`, `quantity`, `lotNumber`, `atUtc`); empty while the task moves nothing. In the task list and in the receipt detail the “From → to” column says **“R1 → 5 bins”** and below it each bin with its quantity (`01-E-03 · 20, 01-E-04 · 20…`); with a single bin, “From → to” looks as always.

**Pending putaway in direct warehouses (Batch 16).** A **direct to bin** receipt (section 4.1) generates no `PUTAWAY` tasks: the goods go into their target bin on
confirming and the receipt goes to Put away. Therefore, in a direct warehouse, the "Pending putaway" tab only shows (a) the receipts **prior to the mode change** that still had tasks,
(b) the open receipts that were switched to "With putaway" and (c) those that had **cross-dock**. Tasks that already existed **continue** their course. `GET /api/v1/warehouse-tasks/putaway-suggestions`
now respects the bin's **capacity in units** (it skips where it does not fit) and carries `maxCapacityQty` and `freeQty` (`capacity − stock`; `null` without capacity). The screen warns when the filtered warehouse is
direct.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Type without a handler (`PICK`, `PACK`, `LOAD`) | `Tasks of type {type} are not completed from the queue.` | 422 |
| Task already closed | `The task was already completed or cancelled.` | 422 |
| Task already in progress (when starting again) | `The task is already in progress.` | 422 |
| Assignee is not an active member | `The user is not an active member of the company.` | 400 |
| Quantity to complete > the task's | `The quantity exceeds the task's.` | 400 |
| Quantity ≤ 0 / with more than 3 decimals | `The quantity must be greater than zero.` / `The quantity allows at most 3 decimals.` | 400 |
| No target bin | `Specify the target bin.` | 400 |
| Target equal to source | `The target bin must be different from the source bin.` | 400 |
| Task without a source bin | `The task has no source bin; it cannot be completed.` | 422 |
| Task without a quantity | `The task has no quantity; it cannot be completed from the queue.` | 422 |
| Serial-tracked product: non-integer quantity | `For serial-tracked products the quantity must be a whole number.` | 400 |
| Serials captured ≠ quantity | `Specify exactly {n} serial number(s).` | 400 |
| Repeated serial / from another task | `There are repeated serial numbers.` / `The task is for a specific serial; do not specify other serials.` | 400 |
| `COUNT` completed from the queue | `Count tasks are completed from Cycle count.` | 422 |
| Cancelling a type that is not `PUTAWAY`/`REPLENISH` | `Tasks of type {type} are cancelled from their own screen, not from the queue.` | 422 |
| Start/complete without the handler's permission | `Missing permission '{warehouse.receive\|warehouse.pick\|warehouse.count\|warehouse.crossdock}'.` | 403 |
| Task of another tenant or nonexistent | `Warehouse not found.` (if the warehouse filter does not exist) or generic 404 of the task | 404 |

### Statuses and transitions

`WarehouseTaskStatus`: **PENDING** (initial) → **IN_PROGRESS** → **DONE**; **CANCELLED** (lateral from `PENDING`/
`IN_PROGRESS`, only `PUTAWAY`/`REPLENISH` from the queue). Completing for less than the quantity given in the task
leaves a remainder as a **new task** with the same `Ref` (same source receipt/plan). Effect
`WarehouseTaskStatusEffect`: when the last `PUTAWAY` with `Ref RECEIPT` of a receipt reaches `DONE` or
`CANCELLED`, the receipt goes `RECEIVED` or `RECEIVED_VARIANCE` → `PUTAWAY` (see section 4).

Replenishment: idempotent per warehouse — a second run creates no new tasks if there is already an open `REPLENISH`
to the same bin (`skippedWithOpenTask`), if there is no reserve available (`skippedNoReserve` — informational, it does not
appear as an error) or if the available quantity is no longer below the minimum.

---

## 6. Cycle count (informed mode)

What it does: takes a snapshot of the system (on-hand balance by bin/product/lot), captures what was counted (quantity or
serial numbers) and, on **confirming**, adjusts against the **current locked balance** (not against the snapshot): if the balance
moved since the snapshot was taken, the line is flagged `systemQtyChanged` for review, but the adjustment is still
correct because it is calculated on the real balance at that instant. Since Batch 14:

- **Confirming is a single step.** "Confirm count & adjust" takes a **Pending** (or **Counted**) count directly to its final status:
  **Matched** if there was nothing to adjust, or **Variance** if at least one movement was posted.
- **"Count what changed".** Creates at once one count for each bin that had movements in a time window.
- The count list carries **the total** (it is no longer cut at 200) and each count says its bin, its zone, its origin and who it is assigned to.

Who can (module **WMS_LOTSERIAL**):

| Action | Permission |
|---|---|
| See the list and the record (blind without `warehouse.count`, see Batch 8A below) | `inventory.view` |
| Manual creation, capture, add what was found and finish (blind count from the app) | `warehouse.count.capture` (implicit for whoever has `warehouse.count`) |
| **Confirm** (reconcile), refresh the snapshot, delete, and **"Count what changed"** (preview and creation) | `warehouse.count` |
| Assign the count to a user (`POST /api/v1/warehouse-tasks/{taskId}/assign`) | `warehouse.manage` |

"What changed" requires `warehouse.count` and not `warehouse.count.capture` because it creates many counts at once. The web also requires
`admin.users` to list who to assign to.

How it is used:
- `GET /api/v1/cycle-counts?warehousePublicIds=&status=&from=&to=&binIds=&productPublicIds=&categoryIds=&search=` — the 200 most
  recent (kept for compatibility; **`from` and `to` are Puerto Rico local days** since Batch 14).
- `GET /api/v1/cycle-counts/page?…&zoneIds=&origins=&skip=&take=` — **the page with the total**: same filters, plus `zoneIds` (some
  line in those zones) and `origins` (`MANUAL` "Selection", `CHANGES` "What changed"; `MANUAL` includes the counts prior to Batch 14, which
  had no origin). `take` from 1 to 200 (50 by default; out of range it is adjusted, no error). Most recent first. `status` accepts `OPEN`, `COUNTED`, `RECONCILED` and `RECONCILED_VARIANCE`, one or
  several. Each row carries `binCount` (distinct bins) and, if the count is of **one** bin, `binCode` and `zoneCode`; `originCode` and
  `origin`; the window `changesFromUtc` and `changesToUtc` (only "what changed"); `taskId` (the COUNT task, to assign it) and
  `assignedToName`. Response: `{ total, skip, take, items }`.
- `POST /api/v1/cycle-counts` — `{ "warehousePublicId": "...", "zoneIds": [...], "binIds": [...] }` (all
  optional: without filters it takes the whole on-hand balance of the warehouse). Maximum 1000 lines. Origin `MANUAL`.
  **`resumeOpen` (2026-10-07, used by the app):** with **a single** bin in `binIds` and `resumeOpen: true`, if that bin already has an **open** count (Pending) whose lines are all of that bin
  (not the multi-bin one nor the by-product one), **that count is returned** with `resumed: true` instead of creating another; if it is assigned to someone else → 409
  `That bin is being counted by {Name} ({CC-#####}).`; if it has no assignee, it is assigned to whoever resumes it. Each line now carries `checkState` (MATCH | RECOUNT | FINAL | null) so the device knows which ones
  were already verified. Without `resumeOpen` (the web) everything stays the same.
- `GET /api/v1/cycle-counts/{id}` — record in informed mode (snapshot, counted, informational variance, expected/counted
  serials, current balance). Each line now carries `barcode` (the product's barcode, which the web scanner uses to
  reach the line; it does not reveal what is expected, so it also arrives in blind mode).
- `PUT /api/v1/cycle-counts/{id}/lines` — capture per line (`countedQty` in NONE/LOT, `serialNumbers` in SERIAL).
- `POST /api/v1/cycle-counts/{id}/lines` — line added manually (what was found without a prior snapshot).
- `POST /api/v1/cycle-counts/{id}/finish` — `OPEN → COUNTED`, requires all lines captured (used by the blind warehouse app).
- `POST /api/v1/cycle-counts/{id}/refresh` — re-snapshots the lines with an old snapshot and clears their capture.
- `POST /api/v1/cycle-counts/{id}/reconcile` — **"Confirm count & adjust"**: from `OPEN` or `COUNTED`, posts the adjustments and goes to
  `RECONCILED_VARIANCE` ("Variance") or `RECONCILED` ("Matched").
- `DELETE /api/v1/cycle-counts/{id}` — only `OPEN`. Cancels its COUNT task (`CANCELLED`, with a closing date: it stops accumulating age and the device deletes it on its next sync).

### Count what changed

Creates **one Pending count per bin** of the warehouse with movements in a time window, each with its COUNT task and with
origin `CHANGES` ("What changed").

- `GET /api/v1/cycle-counts/changes-preview?warehousePublicId=&fromUtc=&toUtc=&zoneIds=&includeEmpty=` — preview: the same the
  creation would do, **without writing anything**.
- `POST /api/v1/cycle-counts/from-changes` — `{ "warehousePublicId": "...", "fromUtc": "2026-09-30T04:00:00Z", "toUtc": null,
  "zoneIds": [ … ], "includeEmpty": true }`. Everything is optional: without a warehouse the only active one is used; without dates, the default window; without
  zones, the whole warehouse; `includeEmpty` is `true` by default.

Rules (owner decisions D2, D3 and D4):

| Rule | Detail |
|---|---|
| Default window | From the `changesToUtc` of the last "what changed" **of that warehouse**; **the first time, from 00:00 today in Puerto Rico time**; until now. The dates can be changed before creating. "To" does not go past now and the range does not exceed **31 days** (if the last generation is older, the window starts 31 days back) |
| Which movements count | Those that touch the warehouse, on either side, **excluding those that come from a count** (a count does not generate another count) |
| What is counted in a bin | **Everything in it** (balance greater than zero) and, with `includeEmpty`, the keys that moved in the window and are at zero today (`SystemQty` 0, to confirm that it really is empty) |
| Which bins are skipped | The **inactive** ones and those that already have a **Pending or Counted** count. The preview says how many of each kind, and how many have nothing to count |
| Limit | **200 bins at a time.** With more, nothing is created and shorter dates or zones are requested |
| All or nothing | Everything is created in **a single transaction**: if something fails, no half-done count is left. Measured in the demo: 120 bins in 1.5 seconds |

The preview returns `fromUtc`, `toUtc`, `movements`, `positions` (the counts that would be created), `positionsWithOpenCount`,
`positionsInactive`, `positionsEmpty`, `lines`, `maxPositions`, `lastChangesToUtc` (the previous generation; null the first time) and
**`problem`**: the exact message with which the creation would respond 400 (null if it can be created). The creation returns `{ window, counts }`, the
window used and the counts created.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| More than 1000 lines selected | `The count allows at most 1000 lines; narrow the filters.` | 400 |
| Filters with no on-hand inventory | `The filters select no on-hand inventory to count; widen the filters or add lines manually.` | 400 |
| Count already confirmed (Matched or Variance): edit, capture, finish, refresh, add a line, confirm again or delete | `The count was already reconciled; it is read-only.` | 422 |
| Finishing a count that is already Counted | `The count was already finished; you can correct the capture or reconcile it.` | 422 |
| Count without lines (finish or confirm) | `The count has no lines.` | 422 |
| Serial-tracked product: loose quantity instead of serials | `For serial-tracked products the serial numbers are captured, not the quantity.` | 400 |
| Non-serial product with serials captured | `Product {sku} is not serial-tracked; do not enter serial numbers.` | 400 |
| Repeated line (bin, product, lot) | `That bin, product and lot are already in the count.` | 409 |
| Negative counted quantity | `The counted quantity cannot be negative.` | 400 |
| Finishing or confirming without all lines captured | `{n} line(s) left to count.` | 422 |
| Confirming with counted < reserved | `The count of {sku} in {bin} ({counted}) is less than the reserved quantity ({reserved}); release the reservation before reconciling.` (without writing anything) | 409 |
| `rowVersion` that is no longer current | `The record was modified by another user; reload and try again.` | 409 |
| Deleting a count already Counted | `Only an open count can be deleted; this one has already been counted.` | 422 |
| Inactive warehouse (creation and "what changed") | `The warehouse is inactive.` | 422 |
| With more than one active warehouse, without specifying which | `Specify the warehouse: the company has more than one.` | 400 |
| Zone or bin that does not belong to the warehouse | `Zone not found.` / `Bin not found.` | 404 |
| Serial captured in two lines of the same count | `Serial {s} is captured in more than one line of the count.` | 409 |
| Completing the `COUNT` task from the queue | `Count tasks are completed from Cycle count.` | 422 |
| "What changed": `from` later than `to` (`errors.fromUtc`) | `The 'from' date cannot be later than the 'to' date.` | 400 |
| "What changed": range of more than 31 days (`errors.fromUtc`) | `The "what changed" range allows at most 31 days.` | 400 |
| "What changed": no active bin with movements (`errors.filters`) | `There were no movements in {warehouse} between {from} and {to}; there are no bins to count.` (local dates and times, `dd/MM/yyyy HH:mm`) | 400 |
| "What changed": all bins already have a Pending or Counted count (`errors.filters`) | `The {n} bins with changes already have a pending count.` | 400 |
| "What changed": more than 200 bins (`errors.filters`) | `There are {n} bins with changes; at most 200 are generated at a time. Narrow the date range or the zones.` | 400 |

In "what changed", if the remaining bins have nothing to count, the creation responds with the same message "The filters select no
on-hand inventory…" (`errors.filters`, 400).

Messages that are only seen on the screen (no HTTP): `That code is not in this count. Use "Add what was found" if the product
is in the bin.`, `{n} lines match: choose the bin and lot.`, `Some quantities could not be saved; fix them before
confirming.`, `Choose the warehouse.` (in "Count what changed"), `The count was already confirmed; it is read-only.` and `The web does not confirm
blind counts.` (the reasons why the **Confirm count & adjust** button is disabled, together with `{n} line(s) left to
count.` and `The count has no lines.`).

### Statuses and transitions

`CycleCountStatus`: **Pending** (`OPEN`, initial) → **Counted** (`COUNTED`, only when counting blind) → **Matched**
(`RECONCILED`) or **Variance** (`RECONCILED_VARIANCE`), both terminal. From Pending you also get directly to Matched or
Variance (D8). The labels "Pending" and "Matched" replace the former "Open" and "Reconciled"; counts already closed
with some adjustment became **Variance** (with a history row "Batch 14: count with variance.").

- **Matched**: on confirming, no movement was posted (what was counted matches the current balance in all lines).
- **Variance**: on confirming, **at least one movement** was posted (a `COUNT_VARIANCE` adjustment or, in serial-tracked products, a removal, an addition
  or a transfer). It does not mean the same as a line with variance against the snapshot: if the balance changed since the snapshot and what was counted
  matches the current balance, there is no movement and the count ends in Matched.

| From → to | Who | What it validates | Effects |
|---|---|---|---|
| (new) → Pending | `warehouse.count.capture` (manual creation) or `warehouse.count` ("what changed") | Active warehouse; at most 1000 lines per count; in "what changed", up to 200 bins | The snapshot of each line is taken; one COUNT task is created per count; the origin is set to `MANUAL` or `CHANGES` |
| Pending → Counted | `warehouse.count.capture` (`POST /finish`; the blind app) | All lines captured and at least one line | Only the status changes; the web does not use this step |
| Pending or Counted → Matched | `warehouse.count` (`POST /reconcile`) | All lines counted; what was counted cannot be less than the reserved quantity; a serial cannot be in two lines; current `rowVersion` | No movement is posted; the balance at confirmation per line, the date and the user are saved; the COUNT task goes to `DONE` |
| Pending or Counted → Variance | `warehouse.count` (`POST /reconcile`) | The same | The `COUNT_VARIANCE` adjustments (counted − current balance) linked to the count are posted, with their movement in the ledger; each line keeps its adjustment; the COUNT task goes to `DONE`; the "Counts with variance" indicator and the Activity event `COUNT_RECONCILED` pick it up |

What is blocked in each status:

| Status | Allowed | Not allowed |
|---|---|---|
| Pending | Capture, add what was found, refresh the snapshot, finish, confirm, delete, assign | — |
| Counted | Capture, add what was found, refresh, confirm | Finishing again (422 "The count was already finished; you can correct the capture or reconcile it.") and deleting (422) |
| Matched, Variance | View and see the history | Any change (422 "The count was already reconciled; it is read-only.") |

While a count is Pending or Counted, **its bin is occupied**: "what changed" skips it. Deleting (`DELETE`, only `OPEN`) cancels
its `COUNT` task.

### Batch 8A — blind count (warehouse app)

The warehouse worker counts **without seeing what is expected** and the confirmation is done on the web with `warehouse.count`.

- New permission `warehouse.count.capture` (WAREHOUSE category, "Capture count (blind)"): creation
  (`POST /api/v1/cycle-counts`), capture per line (`PUT .../lines`), batch capture (`PUT .../lines/batch`),
  add what was found (`POST .../lines`) and finish (`POST .../finish`). Whoever has `warehouse.count` has it
  implicitly (custom roles that already counted lose nothing); the Warehouse operator has it in its template.
- Refresh, **confirm** and delete still require `warehouse.count` (without it: 403 `Missing permission ...`).
- Blind record and responses: whoever does not have `warehouse.count` receives `isBlind = true` and the expected quantities
  of the lines as `null` (`systemQty`, `varianceQty`, `currentQty`, `reconciledSystemQty`, `adjustedQty`;
  `expectedSerials` empty), both in `GET /api/v1/cycle-counts/{id}` and in the response of creation, capture and
  finish. `onlyVariance` is ignored in blind mode.
- Read-only (`inventory.view` without `warehouse.count.capture`) sees the blind record but does not capture (403).
- In blind mode, the header does not reveal what is expected either: `count.varianceLines` and `count.netVariance` arrive as `null` in the
  record, in the responses of creation, capture, batch capture and finish, and in the lists `GET /api/v1/cycle-counts` and
  `GET /api/v1/cycle-counts/page` (together with what was counted they would allow deducing what is expected). With `warehouse.count` they are never `null`.
- On the web (Warehouse → Cycle count) this changes what roles without `warehouse.count` (Read-only,
  Billing) see: the "Net variance" column is no longer shown, because the API no longer sends it. Before, they saw the real
  variance. In the record, a `null` value is drawn as "—", never as 0. **The web does not confirm blind counts**: the button is
  disabled with "The web does not confirm blind counts.".
- Device sync (`/api/v1/sync/*`, WMS_LOTSERIAL module, `inventory.view`): `GET /api/v1/sync/purchase-orders`
  also requires the **PURCHASING** module and `purchasing.view`, same as `/api/v1/purchase-orders` (without the permission 403;
  with the module off 403 `The 'PURCHASING' module is not enabled for this company.`).

### Batch 21 — Count by product, supervisor correction, preview, bulk close and provisional bins

Warehouse bins are not always well labeled, so in addition to counting **by bin** you can count **by product**: the
product is scanned, the system lists the bins where it says there is stock and whoever counts writes down what they find in each one.
The supervisor reviews what was counted on the web, corrects if necessary, sees the effect before confirming and closes at once the counts that
match. What does **not** change: the unit of the count is still the line (bin × product × lot); the adjustment is calculated against the
**current** balance on confirming and is posted through the ledger; confirming is still all or nothing and requires `warehouse.count`; who sees the system
quantities still depends on the permission of whoever queries (blind count without `warehouse.count`).

Who can (module **WMS_LOTSERIAL**):

| Action | Endpoint | Permission |
|---|---|---|
| Create a count by product | `POST /api/v1/cycle-counts` with `productPublicIds` (without `binIds` or `zoneIds`); with `allowEmpty: true` and a single product also if it has no stock | `warehouse.count.capture` |
| Capture, recapture or **correct** a line | `PUT /api/v1/cycle-counts/{id}/lines` and `.../lines/batch` | `warehouse.count.capture` |
| Create a **provisional bin** from the count | `POST /api/v1/cycle-counts/{id}/bins` | `warehouse.count.capture` |
| **Preview** of the reconciliation | `GET /api/v1/cycle-counts/{id}/reconcile-preview` | `warehouse.count` |
| **To review** list | `GET /api/v1/cycle-counts/review` | `warehouse.count` |
| **Close the ones that match** | `POST /api/v1/cycle-counts/reconcile-matching` | `warehouse.count` |
| **Confirm** a provisional bin | `POST /api/v1/warehouses/{publicId}/bins/{binId}/confirm-provisional` | `warehouse.manage` |
| See the provisional bins | `GET /api/v1/warehouses/{publicId}/bins?isProvisional=true` | `inventory.view` |

**Count by product.** It is created with the warehouse and the products; the server builds one line for each bin and lot with stock of the
product (the count origin is set to `PRODUCT`, "By product"; with bins or zones specified it is still `MANUAL`). Each line carries
the bin (`binId`, `binCode`, `zoneCode`), the lot (`lotId`, `lotNumber`), the tracking type (`trackingTypeCode`: `NONE`, `LOT` or
`SERIAL`) and the product barcode. A product with no stock in any bin responds 400 with the message "the filters
select no inventory", **unless** the request carries `allowEmpty: true` with **exactly one product** (without `binIds`, `zoneIds` or
`categoryIds`): then the count is created **empty** (origin `PRODUCT`, no lines, with its COUNT task) to record what was found where the
system had nothing (that is how the warehouse app opens it). An empty count accepts new lines (`PUT .../lines/batch` and `POST .../lines`, with
`binId` + product + lot if it has one, also in a provisional bin), can be deleted while it is open, does not appear in "To
review" until it has lines and **cannot be finished or reconciled while it is still empty** (`The count has no lines.`, 422). With
`allowEmpty: false` (default) nothing changes, and if the product does have stock `allowEmpty` does not change the result. What was found where the system had nothing is added as a new line (`PUT .../lines/batch` with `binId`,
`productPublicId` and, if the product is lot-tracked, `lotId` or `lot.number`). Serial-tracked products are counted by serial number (the warehouse
app does not capture them yet; the server does accept them).

**Original capture and correction.** Each line keeps what was originally counted and who and when (`capturedQty`, `capturedByName`,
`capturedAtUtc`) and, if someone changed it afterwards, who and when (`correctedByName`, `correctedAtUtc`, `wasCorrected`). `countedQty`
is always the **current** value, the one that is reconciled. Rules:

- The **first capture** of a line sets what was originally captured.
- The **same person** can capture again while the count is **Pending**: it replaces their capture (it is not a correction).
- Any change made by **another person**, or by anyone once the count is **Counted**, is a **correction**: `countedQty`
  takes the new value, what was originally captured is kept and who corrected and when is recorded.
- If the corrected value becomes equal to the captured one again, the correction is erased. Sending the same value again changes nothing.
- **A corrected line is protected** (owner decision, 2026-10-03): only whoever corrected it, or anyone with `warehouse.count`,
  can change it again (it is still a correction and updates who and when). Anyone else with only `warehouse.count.capture`
  (including the operator who originally captured it) receives **409** `The line was already corrected by the supervisor; it cannot be captured again.`
  and the correction is not touched. Resending the same current value changes nothing and is not rejected. In the batch capture
  (`PUT .../lines/batch`) corrected lines **are skipped** and **free ones are saved** (owner decision, second block,
  2026-10-03): the response is **200** with the already updated count and an extra field `skippedLines` (empty/absent if none
  was skipped) with, for each skipped line, `lineId`, `binCode`, `sku`, `lotNumber`, `sentQty` (what was sent), `currentQty` (the current
  value: the one the supervisor left; **never** the expected quantity, also in blind mode), `reasonCode` = `CORRECTED_BY_SUPERVISOR` and
  `message` = `The line was already corrected by the supervisor; it cannot be captured again.` If a free line of the batch carries a
  validation error (400), nothing of the batch is saved, as in every capture. Only when **all** the lines of the batch are corrected (nothing
  could be saved) does it respond **409** `The line was already corrected by the supervisor; it cannot be captured again. Batch row(s):
  2 (SKU-1). Nothing was saved.` (the list carries the row number, 1 is the first, and the SKU). Resending the current value of a corrected
  line changes nothing and does not count as skipped; whoever corrected and whoever has `warehouse.count` can still do it. The capture of ONE
  line (`PUT .../lines`) and adding a line do not change: the corrected line still returns 409. Lines without a correction are recaptured as
  always. On finishing the count (Counted) the rule is the same.
- Clearing the capture (no quantity or serials) returns the line to pending and also erases its evidence.
- A correction **is not an adjustment or a transfer** and moves no inventory: it only changes the quantity that is reconciled. Every capture and
  correction is recorded in the change log (entity `CYCLE_COUNT`).
- The evidence also reaches whoever queries in blind mode: it does not reveal what the system says (`systemQty`, `currentQty` and the rest stay `null`).
- On confirming, the reason of the movement of a corrected line carries the evidence, for example: `Conteo CC-00001 · contó 3 (Ana Pérez,
  2026-10-03 14:05) · corregido de 3 a 5 por Beto Ruiz (2026-10-03 15:00)` (dates in the company's time; without a correction the reason is
  the usual one, `Conteo CC-00001`).

**Preview of the reconciliation.** Calculates, with the same code that reconciles and **without writing or locking**, what would happen if it were
confirmed now. Per line: bin and zone, product, lot, **current** stock (`currentQty`) and reserved (`reservedQty`), what was
counted with its evidence, the adjustment that would be posted (`adjustmentQty`, signed), the resulting balance (`resultingQty`), how many
movements it would generate, `systemQtyChanged` (the stock moved since the snapshot) and the error the confirmation would give (`error`: counted
less than reserved). For serial-tracked products it carries the plan: `serials.removals` (removals), `serials.additions` (additions) and `serials.transfers`
(moves). Uncounted lines come out with `isPending = true` (they are data, not an error). Totals (`totals`): lines, pending, with
variance, movements, lines with error, `matches` and the status it would end in (`resultStatusCode`: `RECONCILED` or
`RECONCILED_VARIANCE`; `null` if it cannot be confirmed yet). **`matches` = the count matches**: it has lines, none pending, none with
error and against the current stock it would post no movement. An already confirmed count has no preview (422).

**To review.** The list of **Counted** counts (with `includeOpen=true`, also the Pending ones with all their lines captured), most
recent first. Per count it carries the header and: who counted (`countedByUserId`, `countedByName`; whoever captured the most lines, and
`countedByCount` people in total), the first product by SKU (`firstProductSku`, `firstProductName`) and how many more (`otherProducts`),
`positions`, `lines`, `pendingLines`, `differingLines` (lines that would post something against the **current** stock), `errorLines`, `movements`,
`correctedLines` and `matches`. Filters: `warehousePublicId`, `countedByUserId`, `search` (number, SKU or name), `skip` and `take` (1 to 200, by
default 50). It is calculated in batches (one query of lines, balances, products and users for the whole page).

**Close the ones that match.** `POST /api/v1/cycle-counts/reconcile-matching` with `warehousePublicId`, `ids`, `comment` and `includeOpen`
(all optional). It looks at the Counted counts (or those specified in `ids`) and **confirms only those that match**: they end in
**Matched** without posting any movement, each in its own transaction and checking again with the locked balances. "Matches"
is measured against the **current** stock: a line that moved since the snapshot and would now post something is **not** closed automatically. It responds
`examined`, `closed` (`id`, `number`, `statusCode`, `lines`), `skipped` and `truncated`. Each skipped one carries `reasonCode`, `reason` (text in
Spanish) and `count`:

| `reasonCode` | Meaning | `count` |
|---|---|---|
| `WouldPost` | It would post movements: it stays for review | movements |
| `Errors` | Some line would give an error on confirming (counted less than reserved, repeated serial) | lines with error |
| `Pending` | It has uncounted lines | pending lines |
| `Stale` | It matched when looked at but the stock changed before closing it | — |
| `NotCounted` | It is Pending and `includeOpen` was not requested (only with `ids`) | — |
| `AlreadyReconciled` | It was already confirmed | — |
| `NotFound` | The id does not exist or does not belong to the company | — |
| `NoLines` | The count has no lines | — |
| `Failed` | Another error of the count (the reason comes in `reason`) | — |

Up to 200 counts are looked at per call (`truncated = true` if there were more). Counts of another company are never seen or closed.

**Provisional bin.** If whoever counts finds product where the system had nothing and the bin does not exist, they create it from the count
(`POST /api/v1/cycle-counts/{id}/bins` with `zoneId` and `code`, or `aisle`/`rack`/`level`/`position` to compose the code; same
validations as bin creation) without stopping the count. The bin is active and can be used immediately in the count and in the
inventory, but marked **provisional** (`isProvisional = true`, with `provisionalCycleCountId` and `provisionalCreatedAtUtc`) so that the
supervisor reviews it: they **confirm** it (`confirm-provisional`, removes the mark), correct it (`PATCH`) or deactivate it (`deactivate`). The bin
list, the bin search, the device sync (`sync/bins`) and the count lines (`binIsProvisional`) show
it.

#### Validations (count by product)

| Field / case | Exact message | HTTP |
|---|---|---|
| `allowEmpty` with bins, zones, categories, no product or more than one product | `Creating an empty count (allowEmpty) only applies to a single product, without bins, zones or categories.` (field `allowEmpty`) | 400 |
| `allowEmpty` with a product that does not exist (or belongs to another company) | `Product not found.` | 404 |
| Finishing or reconciling a count that is still empty | `The count has no lines.` | 422 |
| Preview of an already confirmed count | `The count was already reconciled; it is read-only.` | 422 |
| Preview, to review or close of a count of another company | `Count not found.` (in the bulk close: skipped `NotFound`) | 404 |
| Creating a provisional bin in an already confirmed count | `The count was already reconciled; it does not accept new bins.` | 422 |
| Provisional bin without a zone | `Specify the bin's zone.` | 400 |
| Provisional bin without a code or parts | `Enter the bin's code or its aisle/rack/level/position.` | 400 |
| Code with invalid characters or longer than 40 | `The bin code only allows letters, numbers, hyphen and underscore (maximum 40).` | 400 |
| Code repeated in the warehouse (also against a definitive bin) | `A bin with that code already exists in the warehouse.` | 409 |
| Zone of another warehouse or nonexistent | `Zone not found.` | 404 |
| Inactive zone | `The zone is inactive; reactivate it first.` | 422 |
| Confirming a bin that is not provisional | `The bin is not pending review.` | 409 |
| Bulk close with more than 200 `ids` | `At most 200 counts are reviewed at a time; narrow by warehouse or by ids.` | 400 |
| Confirming with lines in error (counted less than reserved) | `The count of {sku} in {bin} ({counted}) is less than the reserved quantity ({reserved}); release the reservation before reconciling.` | 409 |
| A serial captured in two lines | `Serial {serial} is captured in more than one line of the count.` | 400 |
| Without the permission | `Missing permission '{code}'.` | 403 |

The skipped ones of the bulk close carry these texts in `reason`: `Would post {n} movement(s); review it.`, `{n} line(s) left to
count.`, `The count has not finished being counted yet.`, `The stock changed while closing; review it.`, `The count does not exist.`,
`The count was already reconciled; it is read-only.` and `The count has no lines.`

#### Statuses and effects (count by product)

There are no new statuses. Correcting a quantity does not change the status or move inventory; the bulk close takes each count that matches from
**Counted** (or **Pending** with `includeOpen`) to **Matched** with the same transition as "Confirm count & adjust" (history,
`COUNT` task in `DONE`, `reconciledSystemQty` per line). A count that does not match does not change. Creating a provisional bin is only
allowed with the count unconfirmed (Pending or Counted).

---

### 6.x Open count with several products and optional bin (Batch 25)

What it is: a count (origin **By product**, `PRODUCT`) that is **opened empty** and to which products are added, each with its quantity and
its bin. The bin is still part of the line (neither the schema nor the adjustment on reconciling changes); what is new is that **whoever counts does not
have to choose it**: the server proposes the only bin where the system has the product.

Who can: `warehouse.count.capture` (open, look up where a product is, add and capture); `warehouse.count` to reconcile, as always.
Module **WMS_LOTSERIAL**.

**Open an empty count.** `POST /api/v1/cycle-counts` with `{ "warehousePublicId": …, "allowEmpty": true }` and **without products, bins, zones or
categories** creates an `OPEN` count of origin `PRODUCT` **with no lines** (it is not "the whole warehouse") and its COUNT task. With **one** product and `allowEmpty`
it works as before (Batch 21, addendum). With more than one product, or with other filters, it is 400 `allowEmpty`:
`Creating an empty count (allowEmpty) only applies to one or no product, or to a single bin, without other bins, zones or categories.`

**Where the product is.** `GET /api/v1/cycle-counts/{id}/product-bins?productPublicId=…` (`warehouse.count.capture`) returns one row per
**bin and lot with stock** in an active bin of the count's warehouse: `binId`, `binCode`, `zoneCode`, `lotId`, `lotNumber` and `lineId` (the
line the count already has there; `null` if not yet). **It never carries quantities**, so it serves the blind count just as well. 404 `Product not
found.` / `Count not found.`

**Add a line without a bin.** `POST /api/v1/cycle-counts/{id}/lines` no longer requires `binId`:
- a single bin with stock of the product (with the requested lot, if one was requested) → that one is used;
- none → 400 in `binId`: `Product {sku} has no stock in any bin of the warehouse; specify the bin where you found it.`;
- several → 400 in `binId`: `Product {sku} is in several bins ({codes}); specify which one you counted it in.`
With `binId` everything stays the same (404 bin, 422 inactive bin, 409 if that bin, product and lot are already in the count). The capture batch
`PUT /lines/batch` **does** still require `binId` per new row (the app resolves it before queuing).

Frequent cases:
- *"I scanned the same product twice."* Same bin and lot: 409 `That bin, product and lot are already in the count.` (the screens open the
  already counted line to correct the quantity; they do not add or duplicate).
- *"The product is in two bins and I counted in one."* That line is added; the other does not count: the adjustment on reconciling only touches the lines of the count.

Screens: warehouse app ([09 §7.2](09-app-almacen.md#72-counting-several-products-in-one-count-batch-25)) and web (Cycle count → New count → By product without
choosing a product; detail scanner).

### 6.w Quantity calculator in the web count (2026-10-07)

In the count panel (Cycle count), next to the quantity of each line (not serials) there is a **∑** button (“Calculator for line {n}”) and, inside the **Counted quantity** window, the **Calculator** button. It opens the same way of counting stacks as the app:
each **block** is **Rows × Columns × Depth** (depth is how many there are one behind another; blank counts as 1) and the **Loose** ones are added; **+ another block** adds another stack (✕ removes it).
Below come the calculation (`(5 × 3 × 2) + 10`) and the **Total**, which fills in the quantity as you type; **Direct quantity** returns to the field with that number and **Save** (or Enter) saves the quantity as always (`PUT /cycle-counts/{id}/lines`): only the quantity is saved, not the formula.

| Case | Exact message |
|---|---|
| A block with rows or columns but not both | `Each block needs rows and columns.` |
| Something that is not an integer in rows, columns or depth (depth, 1 or more), or that is not a number in loose | `Enter whole numbers in rows, columns and depth (depth 1 or more) and a number in loose.` |
| Total greater than 9,999,999 | `The total is too large.` |

Who can: whoever can capture the count on the web (`warehouse.count`, open count). Module **WMS_LOTSERIAL**.

### 6.z Informed count on capture: who sees the expected quantity while counting (task 25)

What it does: separates **seeing the expected quantity** from the **Count** permission. The supervisor (permission `warehouse.count`) sees it from the start, as always. A **counter**
(only `warehouse.count.capture`) can see, **after accepting the quantity of each line**, whether it matched what was expected; the expected quantity is never shown to them
before counting. Outside the margin they are asked to **recount** without being told the expected quantity; after recounting (or on matching) the line is **closed** and the counter can no
longer change that figure.

Who configures it: `admin.tenant` (the company) and `admin.users` (each person); module **WMS_LOTSERIAL**.

**Company setting** (System → Settings → **Operations** → “Cycle count: expected quantity while counting”; `PUT /api/v1/tenant/settings`):

| Field | Values | What it does |
|---|---|---|
| Who sees the expected quantity while counting? (`countExpectedReveal`) | **Nobody** (`NONE`) · **Only marked users** (`MARKED`, default value) · **Everybody** (`ALL`) | Nobody: nobody while counting in the app, **not even the supervisor** (the reconciliation on the web still shows the expected quantity). Only marked users: the counters with “Yes” in Users. Everybody: all counters except those marked “No”. |
| Margin before asking for a recount (%) (`countRecountTolerancePct`) | 0 to 100 (default 0) | 0 = any difference asks for a recount. With 5, a quantity within 5% of the expected one is accepted as good. |
| Show the expected number (`countRevealShowsNumber`) | Yes (default) / No | With No it only says “Matches” or “Does not match”. |
| Close matching counts automatically (`countAutoCloseMatching`, 2026-10-10) | No (default) / Yes | With Yes, finishing a count that matches leaves it in Matched without confirming it on the web (§6.9). |

**Per-person mark** (System → Users → row actions → “Sees expected while counting: Yes / No / unmarked”; `PUT /api/v1/users/{id}/count-see-expected`
with `{ "value": true | false | null }`): the “Sees expected while counting” column shows Yes / No / —. It only counts when the company is in “Only marked users” or “Everybody”.

| Company \ User | unmarked | No | Yes |
|---|---|---|---|
| **Nobody** | does not see | does not see | does not see |
| **Only marked users** | does not see | does not see | sees on capture |
| **Everybody** | sees on capture | does not see | sees on capture |

How it works (server): `POST /api/v1/cycle-counts/{id}/lines/{lineId}/check` with `{ "countedQty": n }` (permission `warehouse.count.capture`). It responds `state`:
**MATCH** (within the margin; line closed), **RECOUNT** (outside the margin: recount; it does **not** carry the expected quantity) or **FINAL** (already recounted; closed). `expectedQty` only
arrives with MATCH/FINAL and if the company shows the number. The first and last verified figures are saved (`firstCheckQty`, `lastCheckQty`; they are recorded in the change
log). After verifying, the capture of that line (`PUT …/lines` and `PUT …/lines/batch`) **only accepts the last verified figure** (409 if it is another); whoever has
`warehouse.count` can always correct. The count record carries `reveal`: `FULL` (supervisor), `AT_CAPTURE` (enabled counter) or `NONE`.

| Case | Exact message | Code |
|---|---|---|
| The counter is not enabled (company in “Nobody” or unmarked/marked “No”) | `Seeing the expected quantity while counting is not enabled.` | 403 |
| The line is already closed (it matched or was already recounted) | `The line was already verified; its quantity cannot be changed.` | 409 |
| The line was already corrected by the supervisor | `The line was already corrected by the supervisor; it cannot be captured again.` | 409 |
| Trying to save a quantity other than the verified one | `The quantity is not the one verified on the line; it cannot be changed after seeing the result.` | 409 |
| Serial-tracked product | `Serial-tracked products are not verified against the expected quantity.` | 400 (`lineId`) |
| Negative quantity or with more than 3 decimals | `The counted quantity cannot be negative.` / `The quantity allows at most 3 decimals.` | 400 (`countedQty`) |
| Margin outside 0–100 | `The recount margin must be between 0 and 100%.` | 400 (`countRecountTolerancePct`) |
| Unknown mode | `Unknown mode: '{value}'. Use NONE, MARKED or ALL.` | 400 (`countExpectedReveal`) |
| Count already finished or reconciled | (the usual count messages) | 422 |

Notes: with “Only marked users” or “Everybody”, a supervisor who also counts sees the expected quantity from the start (to count blind they must use a capture-only person or role). With “Nobody”
the count from the app arrives blind for the supervisor too (the app sends `forCounting=true` in `POST /cycle-counts` and `GET /cycle-counts/{id}`; the web does not, and still sees everything to reconcile) and `…/check` responds 403 to everyone. The app steps are in [09 §7.4](09-app-almacen.md#74-informed-count-when-capturing-task-25).

### 4.2 Split by bin on the web: direct receipt, Putaway and dispatch (2026-10-07)

The web offers the same as the warehouse app to split large quantities among bins.

**Split rule** (the same on server, app and web): with *N per bin* full bins are filled and, if there is a remainder, **one more bin** receives the rest; no more bins are accepted after that one (`With {per} per bin, {max} bin(s) fit for {pending}; there are no more units to split.`, 400). A fixed on-screen notice (it can be closed) warns when the rest falls in the extra bin; each row has ✕ to remove it. If a bin does not have enough free space it only **warns**, it does not block.

- **Putaway (warehouse tasks)** → when completing a task, “Split by bin” switch: quantity per bin + bins (with a search box). It uses the task's `distribute` endpoint. Permission: the same as completing the task.
- **Direct receipt** → on the line of the open receipt, **Split** action (grid icon): the first line keeps what was received and its bin; the others are added as new lines. Only blind or return receipts, non-serial products. It is done with consecutive calls (it is not a single transaction): if one fails, the message says so and the previous ones remain saved.
- **Dispatch / picking** → on a line with product and quantity and no bin, **Suggest bins** action: it proposes which bins to take from (by the warehouse's outbound order, respecting the lot if one was specified and what is already assigned in other lines) and leaves one line per bin. Errors: `Pick the product and enter the quantity to suggest where to take it from.`; `Not enough stock: {short} short. Lower the quantity.` It does not apply to serial-tracked products.
- **Duplicate line** (direct to bin receipt without a document, and picking & packing): **Duplicate** action on each line with a product (in picking it goes between *Suggest bins* and *Remove*). It creates below a line with the same product and quantity and **no bin** (nor lot nor serials): the bin of the copy is chosen (in the receipt, choosing it saves it). It does not apply to serial-tracked products nor to the empty row at the end.

### 6.s Count of a bin done without signal, applied when the signal returns (2026-10-11)

What it does: `POST /api/v1/cycle-counts/offline-bin` (`warehouse.count.capture`, WMS_LOTSERIAL module) applies at once the count of ONE bin that the device did without signal: **it opens the count of the bin or resumes the one already open,
captures what was counted and, if it is theirs, finishes it**. The app sends it (outbound queue, with `Idempotency-Key`); with the same key it returns the saved response and duplicates nothing.

Body: `{ "warehousePublicId": "…", "binId": 7, "lines": [ { "binId": 7, "productPublicId": "…", "countedQty": 4, "lot": { "number": "L-9" } } ], "finish": true, "countedAtUtc": "2026-10-11T14:05:00Z" }`.
`lines` = what was counted (each row with the bin and the product, and the lot by number if there is one; `lineId` is not accepted: the device did not know the count); `finish` defaults to `true`; `countedAtUtc` = when it was counted (device time, only to warn about movements).
`?forCounting=true` leaves the response blind if the company is in “Nobody” sees the expected quantity (like the rest of the app's count).

Rules (owner decisions):
- If the bin already has an open count **by another counter**, the lines are **added to that count** and **it is not finished** (`joined: true`, `finished: false`): its owner continues it. If it is theirs or it is free, it is resumed; if there is none, a new one is opened (`created: true`).
- What was counted is compared with the **server's** stock **on reconciling**, as always. If there were movements in the bin between `countedAtUtc` and now, `movedSinceCounted: true` and, on finishing it, the count history carries the comment `Contado sin señal; hubo movimientos en la posición desde que se contó.` (without movements: `Contado sin señal.`). A device time ahead of the server is capped to the server's.
- Not being able to finish (e.g. the server has lines the device did not count) **does not fail**: what was counted is saved, the count stays open and the response carries `finishMessage` (e.g. `1 line(s) left to count.`).
- Response: `{ count, created, joined, finished, movedSinceCounted, finishMessage }`.

| Case | Exact message | HTTP |
|---|---|---|
| No `binId` | `Enter the bin.` (field `binId`) | 400 |
| No lines | `Specify the lines to capture.` (field `lines`) | 400 |
| A row from another bin or with `lineId` | `Each row of the count must belong to the specified bin.` (field `lines[i].binId`) | 400 |
| Inactive warehouse | `The warehouse is inactive.` | 422 |
| Bin, product or warehouse that do not exist (or belong to another company) | `Bin not found.` / `Product not found.` / `Warehouse not found.` | 404 |
| Without the capture permission | standard permission denied response | 403 |
| The capture rules of the batch (required lot, serial, invalid quantity…) | the same as `PUT /cycle-counts/{id}/lines/batch` | 400 / 422 |

### 6.v Cancel a count: capture permission (2026-10-08)

Cancelling an open count (app: Cancel on the count screen; `DELETE /api/v1/cycle-counts/{id}`, 204) requires the same permission as capturing and finishing: **Capture count (blind)** (`warehouse.count.capture`, which `warehouse.count` includes). A counter without `warehouse.count` —who therefore does not see the expected quantity— can cancel the count just as they capture and finish it. Regardless of who opened it.
- A count already finished or reconciled is not cancelled, with any permission → 422 `Only an open count can be deleted; this one has already been counted.`
- Cancelling frees the bin and closes the count task, as always. It needs signal.

### 6.y Rented units and the cycle count (Batch 28, Rentals R2, decision D7)

Rented units remain in inventory, in the **EN-RENTA** bin (RENT zone, "On rent" type; chapter 11). That bin
**is not counted** and a rented serial found in a count **blocks** the reconciliation until its return is recorded:

| Case | What happens | Message | HTTP |
|---|---|---|---|
| Creating a count requesting the EN-RENTA bin or the RENT zone (`binIds`/`zoneIds`), or a "what changed" count with the RENT zone, or adding a line in EN-RENTA | It is not created / not added | `Bin {bin} is for rented units; it is not counted.` | 422 |
| Count of the whole warehouse, by product or "what changed" without a zone | The EN-RENTA bin is skipped without error; `product-bins` and the default bin do not offer it either | — | — |
| A serial that is **On rent** is captured in another bin | The preview flags the line with the error and **reconcile** responds 409 without moving anything | `Serial {s} is on rent ({n}); record its return before reconciling the count.` ({n} = REN number of the open rental) | 409 |

What to do: if the unit really came back, record its **rental return** (chapter 11 §5) and capture the count again; if it was
captured by mistake, remove the serial from the capture and reconcile.

### 6.9 Automatic close on finishing if the count matches (2026-10-10)

What it does: with the setting on, **finishing** a count that **matches** confirms it on the spot and leaves it in **Matched**, without anyone having to confirm it on the web. Designed for the massive inventory (thousands of bins, one count per bin): only those with a variance wait in “To review”.

Who configures it: `admin.tenant`; module **WMS_LOTSERIAL**. System → Settings → **Operations** → “Cycle count: expected quantity while counting” → **Close matching counts automatically** (`countAutoCloseMatching`, `PUT /api/v1/tenant/settings`). **Off by default**: existing companies do not change until it is turned on.

How it works: on finishing (`POST /api/v1/cycle-counts/{id}/finish`, from the app or the web) the count goes to Counted and, right away, the server does the same check and the same reconciliation as “Close the ones that match” (with the locked balances):
- **Matches** (all lines counted, none with error and, against the **current** stock, no movement would be posted) → Matched, with date, the COUNT task in `DONE` and the history comment `Cierre automático: el conteo cuadra.`. No inventory movement is posted. “Confirmed by” is set to whoever finished the count.
- **Does not match** (there is a variance, a line would fall below the reserved quantity, or the stock changed right then) → it stays **Counted**, same as with the setting off, in “To review”. Finishing **never fails** because of this and no error is shown.
- The permission of whoever finishes is still `warehouse.count.capture`: the automatic close does not give them the power to adjust, because it only closes what adjusts nothing.
- “Close the ones that match” is still available for those that remained Counted.


## 7. Ad hoc picking and packing (Pick & Pack)

What it does: picks inventory (by FEFO or with explicit bin/lot/serial) into a pick batch with number
`EMP-#####`, and "packing" that pick batch creates a real transport order with that same number as the packing
number. Deleting a pick batch reverts the inventory to its original bin.

The `EMP-#####` number is taken **before** posting the issues: the pick batch header is inserted
first (with its number), and then the `ISSUE`s are posted with the reference already set in the `INSERT` — there is never an
`UPDATE` on the ledger. A shortage also reverts the number: the `EMP`s remain consecutive and without gaps.

Who can: `inventory.view` (list and look up); `warehouse.pick` (pick, pack, delete). Packing
**also** requires `orders.create`; deleting an already packed pick batch **also** requires `orders.cancel`. Module
**WMS_LOTSERIAL**.

How it is used:
- `GET /api/v1/pick-batches?from=&to=&productPublicIds=&status=&orderNumber=&invoiceNumber=&search=&
  includeDeleted=` (deleted ones are excluded by default; the `search` filter is applied last, over what was already
  filtered by `orderNumber`/`invoiceNumber`).
- `POST /api/v1/pick-batches` — `{ "warehousePublicId": "...", "lines": [{ "productPublicId": "...", "quantity": 5 }] }`.
- `POST /api/v1/pick-batches/{publicId}/pack` — `{ "order": { ... order data ... } }`. Without `serviceType` (or without
  `packageType` in a package) the order takes the **company default**; if the company does not have one, 400 `The service
  type is required.` (`errors.serviceType`) or `The package type is required.` (`errors["packages[i].packageType"]`).
  To know beforehand whether there are defaults (adjustment of 2026-09-30: the screen only offers "Company default
  (…)" when it exists), read `GET /api/v1/tenant/settings`: `defaultServiceType` and `defaultPackageType` carry the
  code or `null`. That endpoint only requires a session (no permission or module), so whoever packs reads it; the label comes
  from the list (`GET /api/v1/catalogs/ServiceType` and `.../PackageType`).
- Batch 8A (device queue) — `POST /api/v1/pick-batches/collect-and-pack` — the same body as picking plus
  `"pack": { "order": { ... } }`: picks and packs in **one** transaction and responds `{ batch, order }`
  (`PickBatchPackResultDto`), with the same rules, permissions and messages as the two steps. If the packing fails (for
  example `Client not found.`, 404) neither the pick batch remains nor does inventory leave, and the `EMP` number is not consumed.
  It respects `Idempotency-Key`. Sending `pack` to `POST /api/v1/pick-batches` responds 400 `To pick and pack in
  one call use POST /api/v1/pick-batches/collect-and-pack.`; without `pack` in `collect-and-pack` → 400 `Specify the
  data of the order created on packing.`.
- `DELETE /api/v1/pick-batches/{publicId}`.
- Batch 13 (screen): the list and the capture share a screen in two panels (see
  [F6 — Picking & packing](frontend/f6-almacen-e-inventario.md#picking--packing)). The capture sends **a single** call
  `POST /api/v1/pick-batches` with all the lines (up to 100). In the list, `orderNumber` and `invoiceNumber` search by content, case-insensitively
  (the "Shipment no." filter suggests numbers with `GET /api/v1/pick-batches?orderNumber=<text>&take=20`). Each
  row carries `canPack` and `canDelete`; the screen offers Pack and Delete only when they are true (and with `orders.cancel`, to
  delete a packed one).
- Batch 16 (Bin selector of the capture): with a product chosen, the list offers **only the bins where that product has available stock** (and, if a lot was chosen, only
  those of that lot), each with its available quantity (`P-01 · PCK · 9 avail.`), in the same FEFO order the server uses when picking; the first one is marked "Suggested". Without a product, the field is
  off. It is a screen aid: the API did not change and still accepts any bin (if it is not enough, it responds `Insufficient inventory…`, 409).
- From the **warehouse app** (Dispatch screen, [chapter 9 §6](09-app-almacen.md#6-dispatch-pick-and-pack)) the same
  two endpoints are used, for **own** inventory or a **3PL client's** (a single owner per dispatch):
  - **Complete dispatch** (main button) sends `POST /api/v1/pick-batches` with the lines: the inventory leaves and a pick batch
    `EMP-#####` remains in **Collected**, **without order, consignee or packing** (it can be packed later from the web).
  - **Pack** (optional) sends `POST /api/v1/pick-batches/collect-and-pack` with `serviceType` `STANDARD`, a `BOX` package with the
    packages indicated and `confirmNow: true`. With a 3PL client's inventory the order belongs to that client; with **own** inventory the app
    first asks “Which customer is it for?” (`GET /api/v1/clients`) and then the consignee of that client
    (`GET /api/v1/locations?clientId=`).
  - Without signal, either of the two is saved in the device queue and sent when the signal returns (with `Idempotency-Key`).

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Products of more than one owner in a pick batch | `A pick batch can only contain products of a single owner.` | 400 |
| Packing with special delivery or driver | `A packing cannot be a special delivery or carry a driver.` | 400 |
| No lines | `Enter at least one line to pick.` | 400 |
| Line without a product | `Enter the product.` (`errors["lines[i].productPublicId"]`) | 400 |
| Serial longer than 80 characters / more than 500 serials in a line | `Each serial number allows at most 80 characters.` / `A line allows at most 500 serials.` | 400 |
| Warehouse not specified with more than one active / no active warehouses | `Specify the warehouse: the company has more than one.` (400) / `The company has no active warehouses.` (422) | 400 / 422 |
| Inactive warehouse | `The warehouse is inactive; it cannot be picked from.` | 422 |
| More than 100 lines | `A pick batch allows at most 100 lines.` | 400 |
| Quantity ≤ 0 / with more than 3 decimals / out of range | `The quantity must be greater than zero.` / `The quantity allows at most 3 decimals.` / `The quantity exceeds the allowed maximum.` | 400 |
| Serial-tracked product: quantity ≠ scanned serials | `For serial-tracked products the quantity must equal the number of scanned serials.` | 400 |
| Serial repeated in the pick batch | `Serial {s} is repeated in the pick batch.` | 400 |
| Serial not available | `Serial {s} is not available in the warehouse (it does not exist, already left or is in another bin or lot).` | 409 |
| Serial-tracked product without serials specified | `Product {sku} is serial-tracked: scan the serials to pick.` | 400 |
| Non-serial product with serials specified | `Product {sku} is not serial-tracked.` | 400 |
| Non-lot product with a lot specified | `Product {sku} is not lot-tracked.` | 400 |
| Inactive product | `Product {sku} is inactive; it cannot be picked.` | 422 |
| Inactive bin or in a non-pickable zone | `Bin {bin} is inactive.` / `Bin {bin} is in a {type} zone; picking is not done from there.` | 422 / 400 |
| Insufficient inventory | `Insufficient inventory of {sku} in {where}: available {x}, requested {y}.` (no partial effect; the EMP number is not consumed either) | 409 (`insufficient_stock`) |
| Packing order of a client other than the owner | `The order must belong to the client that owns the inventory ({client}).` | 400 |
| Packing an already packed pick batch | `Pick batch {n} was already packed.` | 422 |
| Deleting a pick batch whose order already moved on | `The order of pick batch {n} already moved on to '{status}'; the pick batch can no longer be deleted.` | 422 |
| Deleting an already deleted pick batch | `Pick batch {n} was deleted; it is read-only.` | 422 |
| Inactive reversal bin (delete) | `Bin {bin} of the pick batch is inactive; reactivate it to delete the pick batch and restore the inventory.` | 422 |
| Packing without order data (or `collect-and-pack` without `pack`) | `Specify the data of the shipment created on packing.` | 400 |
| Packing without a service type, in a company without a default | `The service type is required.` (`errors.serviceType`) | 400 |
| Packing a package without a type, in a company without a default | `The package type is required.` (`errors["packages[i].packageType"]`) | 400 |
| Pick batch or order number already taken (concurrency) | `A pick batch with that number already exists; try again.` / `The shipment is already linked to another pick batch.` | 409 |
| Packing or deleting with an old `rowVersion` | `The record was modified by another user; reload and try again.` | 409 |
| Packing or deleting with a `rowVersion` that is not base64 | `Invalid rowVersion: the base64 value returned by the record is expected.` | 400 |
| Packing/deleting without `orders.create`/`orders.cancel` | `Missing permission 'orders.create'.` / `Missing permission 'orders.cancel'.` | 403 |
| Deleting from **Orders** an order born from a packing | `This order was born from pick batch {n}; delete it from Picking & packing to restore the inventory.` | 409 |

### Statuses and transitions

`PickBatchStatus`: **COLLECTED** (initial, after picking) → **PACKED** (on packing: creates the real order and copies the
final invoice to the batch) → **CANCELLED** (terminal, on deleting: reverts each line with `ADJUSTMENT
PICK_BATCH_REVERSAL` to its original bin and the serial returns to `AVAILABLE`). It is always deleted from `COLLECTED`;
from `PACKED` only if its order is still **active** and in the **initial stage** (otherwise, `422` with the message above).
Cancelling the shipment born from a packing does **not** restore the inventory by itself (the goods come back with a
separate `RETURN` receipt).

Batch 13 — the same cycle, by transition (Pack and Delete are icons in the list row, and also buttons in the detail):

| From → to | Who | What it validates | Effects |
|---|---|---|---|
| (pick) → Collected | `warehouse.pick` | Active warehouse, a single owner, up to 100 lines, available stock (409 with no partial effect), lot and serials according to the product. | `EMP-#####` number; one `ISSUE` per line (FEFO if no bin or lot is specified). |
| Collected → Packed | `warehouse.pick` + `orders.create` | The pick batch is still Collected (422 `Pick batch {n} was already packed.`); order client = inventory owner; order data; service and package type (or the company default); `rowVersion` if sent. | Creates the real transport order with the pick batch number as packing number and copies the final invoice. |
| Collected → Cancelled | `warehouse.pick` | Active reversal bins; `rowVersion` if sent. | Reverts each line with `ADJUSTMENT PICK_BATCH_REVERSAL` to its original bin; the serial returns to `AVAILABLE`. |
| Packed → Cancelled | `warehouse.pick` + `orders.cancel` | The created order is still active and in its initial stage (422 with the message above). | Also deletes the order and does the same reversal. |

What it blocks: a Packed pick batch is not packed again; a Cancelled one is read-only (`422` `Pick batch {n} was
deleted; it is read-only.`) and only appears in the list with `includeDeleted`. The list does not offer Pack (`canPack`) or Delete
(`canDelete`) when the server marks them as false.

---

## 7a. Picking and packing by document (Batch 173, 2026-10-21)

The right-hand list of **Picking & packing** is seen in two ways (selector above the list; it goes in the URL, `?view=moves` for the second):

- **By document** (default): **one row per exit notice, sales order or packed shipment**, with its progress and how many pick batches it has. When opening a document, each pick batch that came out of a **master pick batch** carries a **“Master RMA-…”** tag. It is the same information as always, grouped: no data changes.
- **By movement:** the usual list of pick batches and manual issues, with all its filters (range, status, product, shipment number, invoice, deleted, Kind).

**What each row carries (by document):** number and type (Exit notice / Sales order / Shipment), client, document status, **progress** (“Picked 5 of 10”, bar, “To collect 4” = reserved that is still to be picked, “Not reserved 1” = ordered that is waiting for inventory), number of **movements** and date of the **last collection**. A **shipment** (the one born on packing) has no “ordered” quantity: it shows only what was picked. Filters: **Kind**, **“Only with pending to collect”** and the search box (number or client). Sorting and exporting use the whole filter.

**Opening a document** shows the progress and its **pick batches as movements** (most recent first: number and status, date, who and quantity); tapping one opens the record of that pick batch with its actions (Pack, Delete).

**Which documents appear:** the open ones with something reserved to pick (even if they have no pick batches yet) and those that already have some pick batch; a cancelled one without pick batches does not. Each pick batch counts for **one** document, with this priority: exit notice, then sales order, then shipment. **Deleted** (reverted) pick batches do not count. A **manual issue** has no document: it is seen in “By movement”.

| Method and route | Permission | What it does |
|---|---|---|
| `GET /api/v1/pick-batches/documents?kind=&status=&search=&pendingOnly=&skip=&take=` | `inventory.view` | Documents with their progress and their pick batches. `kind`: `EXIT_NOTICE`, `SALES_ORDER` or `ORDER`. `take` up to 200. 400 `Unknown document type: “X”. Use EXIT_NOTICE, SALES_ORDER or ORDER.` |

The list of **receipts** by notice comes with the multi-batch receipt (C2): today each notice has a single receipt, so grouping it would change nothing.

## 7b. Manual issue (DMA-#####, 2026-10-11)

What it does: takes inventory out of the warehouse **without a delivery** — a sample, internal use, a client who comes to pick up, a counter sale,
etc. — with a **required reason** and a **free note**. It is its own document with `DMA-#####` numbering (its own counter per
company; it does **not** consume the `EMP` numbers of the pick batch) and a record you can look up. Internally it is the **same issue** as the pick batch
(§7): same FEFO outbound order or explicit bin/lot, same scanned serials, same `ISSUE` in the movement ledger, same 409 with no partial
effect and the same reversal on deleting it. It asks for no client or consignee; it serves **own** inventory or **one client's** (a single
owner per document, same as the pick batch). **It is not blind**: the stock is still shown as always. The web and the app use
the same operation.

Who can: `warehouse.issue` (new; included in the **Warehouse operator** and **Company admin** templates, and added to those two
roles in each existing company) to issue, delete and read the reasons; `inventory.view` for the list and the record. Module
**WMS_LOTSERIAL** (the same as Picking & packing).

How it is used:
- `GET /api/v1/manual-issues/reasons` — active reasons enabled for the company, in the user's language (the generic
  list `GET /api/v1/catalogs/ManualIssueReason` also works). System ones: `SAMPLE` Sample, `INTERNAL_USE` Internal use,
  `OWNER_PICKUP` Owner pickup, `CUSTOMER_PICKUP` Customer pickup, `SALE` Sale, `OTHER` Other.
- `POST /api/v1/manual-issues` — issues. Body: the pick batch one plus `reasonCode` (required) and `note` (optional, up to 500):

  ```json
  {
    "warehousePublicId": "9c8ff3d1-ea5c-430e-a7b0-2a7319a1c092",
    "lines": [
      { "productPublicId": "3b4e80e7-deb7-47e7-9a39-1c7bc0ab492b", "quantity": 2 },
      { "productPublicId": "…", "quantity": 1, "binId": 1002, "lotId": 77 },
      { "productPublicId": "…", "quantity": 2, "serialNumbers": ["SN-1", "SN-2"] }
    ],
    "reasonCode": "SAMPLE",
    "note": "Feria de salud"
  }
  ```

  It responds 200 with the record (`PickBatchDto`, the same as the pick batch): `number` `DMA-00012`, `statusCode` `COLLECTED`,
  `isManual: true`, `reasonCode`, `reasonLabel` (with the name the company gave it), `note`, `ownerClientName` (client that owns
  the inventory; `null` = own), `canPack: false`, `canDelete`, `lines` (bin, lot, serial, frozen cost and movement) and
  `rowVersion`. `reasonCode` is case-insensitive (`sample` is valid). `warehousePublicId` can be omitted if the company has a single
  active warehouse. It respects `Idempotency-Key`: the retry with the same key returns the same document without taking inventory out again.
- `GET /api/v1/manual-issues?from=&to=&productPublicIds=&status=&search=&includeDeleted=&skip=&take=` — paginated list of manual
  issues only (the same filters as the pick batch list).
- `GET /api/v1/manual-issues/{publicId}` — record (also of a deleted one).
- `DELETE /api/v1/manual-issues/{publicId}` — optional body `{ "comment": "…", "rowVersion": "…" }`; 204. Returns the inventory to its
  original bin (`ADJUSTMENT PICK_BATCH_REVERSAL`, the serial becomes available again) and goes to **Cancelled**.
- Pick batch list: `GET /api/v1/pick-batches` gains `kind` = `MANUAL` (manual issues only), `PACK` (`EMP` pick batches only)
  or `ALL` (**default**: both). Each row carries `isManual`, `reasonCode`, `reasonLabel` and `note`, and the `search` filter also
  finds the reason and the note. From `/pick-batches/{publicId}` the record of a manual issue is also seen, and deleting it through that route
  also requires `warehouse.issue`.
- Movement ledger: each `ISSUE` carries the note `DMA-00012 · Muestra` (number · reason) and the reference reads **Manual issue DMA-00012**; the
  movement detail shows the document with its reason and its note. The free note stays in the record, not in the movement note.
- Analytics: the pick batch data source (`PICK_BATCH`) includes manual issues with the fields **Manual issue**
  (`IsManual`), **Reason code**, **Manual issue reason** and **Note**.
- Editable reasons: in **System → Lists**, “Manual issue reason” list, the company (`admin.catalogs`) changes the name of
  a reason or disables it with its override (same as “Final destination of damaged goods”). A disabled reason is no longer accepted when
  issuing, but the documents that already use it keep showing it. Adding a new reason is done by the platform administrator.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| No reason (`reasonCode` empty or absent) | `Enter the reason for the manual issue.` (`errors.reasonCode`) | 400 |
| Reason that does not exist, is inactive or disabled by the company | `Reason {CODE} does not exist or is inactive.` (`errors.reasonCode`) | 400 |
| Note longer than 500 characters | `The note allows at most 500 characters.` (`errors.note`) | 400 |
| Products of more than one owner | `A manual issue can only have products from the same owner.` (`errors.lines`) | 400 |
| Insufficient inventory | `Insufficient inventory of {sku} in {where}: available {x}, requested {y}.` (no partial effect; the DMA number is not consumed) | 409 (`insufficient_stock`) |
| Number taken (concurrency) | `A manual issue with that number already exists; try again.` | 409 |
| Packing a manual issue (`POST /pick-batches/{id}/pack`) | `Manual issue {n} is not packed: it is an inventory issue without a delivery.` | 422 |
| `kind` other than MANUAL, PACK or ALL | `The kind must be MANUAL, PACK or ALL.` (`errors.kind`) | 400 |
| Record or delete through `/manual-issues` with the id of an `EMP` pick batch, nonexistent or already deleted | `Manual issue not found.` | 404 |
| Issuing, deleting or reading the reasons through `/manual-issues` without `warehouse.issue` (or seeing the list or the record without `inventory.view`) | (no application body: the controller policy rejects it; the `PERMISSION_DENIED` event remains) | 403 |
| Deleting a manual issue through `DELETE /pick-batches/{id}` with `warehouse.pick` but without `warehouse.issue` | `Missing permission 'warehouse.issue'.` | 403 |
| Lines: no lines, more than 100, quantity, serials, lot, bin, warehouse or inactive product | The same messages and codes as the pick batch (§7, Validations) | 400 / 404 / 409 / 422 |
| Deleting with the source bin or the warehouse inactive, or the product inactive | `Bin {bin} of the pick batch is inactive; reactivate it to delete the pick batch and restore the inventory.` / the movement ledger message for inactive warehouse or product | 422 |
| Deleting with an old `rowVersion` | `The record was modified by another user; reload and try again.` | 409 |

### Statuses and transitions

It uses the same `PickBatchStatus` as picking, without the Packed stage:

| From → to | Who | What it validates | Effects |
|---|---|---|---|
| (issue) → Picked (`COLLECTED`) | `warehouse.issue` | Reason and note first (before touching inventory); then everything picking checks: active warehouse, a single owner, up to 100 lines, stock (409 with no partial effect), lot and serials. | Number `DMA-#####`; one `ISSUE` per portion with the note `DMA-… · {reason}`; cost frozen per line. |
| Picked → Cancelled (`CANCELLED`) | `warehouse.issue` (via `/manual-issues`; via `/pick-batches` also `warehouse.pick`) | Active reversal bins; `rowVersion` if sent. It does **not** require `orders.cancel` nor look at orders (it has none). | Reversal of each line to its original bin; the serial becomes available again; it remains available for lookup with `includeDeleted`. |

What it blocks: a manual issue is **never** packed (`canPack` is always false; `collect-and-pack` never creates one). A deleted one can only
be looked up. A manual issue does **not** count as an open document: it does not prevent deactivating the product or the warehouse (it stays in
Picked forever); after that deactivation it can no longer be deleted (the movement ledger refuses to return inventory to an inactive product or
warehouse, 422).

### Frequent cases

- **Why don't I use “Adjust”?** An adjustment corrects an inventory difference; a manual issue is a real exit (with reason,
  number and record) and shows up as `ISSUE` in the movement ledger, just like what leaves through a pick batch.
- **Can I turn it into an order?** No: if the goods go to a consignee, use Picking & packing (§7).
- **I got the quantity wrong.** Delete it (it returns everything to its bin) and issue it again.

## 7c. Default reason for the manual issue (2026-10-11 b)

What it does: so the issue on the device is not complicated, the company chooses **one reason that the app and the web app preselect** when making a
manual issue (§7b). The user can change it before issuing. **The server still requires a reason** on every issue: the default
only preselects; it is not applied on its own if the reason arrives empty. The note is still optional and has **no text** by default. By default
no company has a default reason (nothing preselected, as before).

Who can: change it, `admin.tenant` (Tenant settings); see it, any user of the company in the settings and anyone with
`warehouse.issue` in the list of reasons. The change is recorded in the company's **audit log** (field
`DefaultManualIssueReasonLookupId`, from → to).

How it is used:
- `PUT /api/v1/tenant/settings` with `{ "defaultManualIssueReason": "SALE" }` — saves the code from the “Manual issue reason” list
  (case-insensitive). `""` removes it; without the field (or `null`) it does not change. `GET /api/v1/tenant/settings` returns it in
  `defaultManualIssueReason` (code or `null`).
- `GET /api/v1/manual-issues/reasons` — each reason now carries `isDefault`; at most one comes back `true`. The response is still the
  same list (only the field was added):

  ```json
  [
    { "id": 1446, "entity": "ManualIssueReason", "code": "SAMPLE", "label": "Muestra", "sortOrder": 1, "isEnabled": true, "isActive": true, "isDefault": false },
    { "id": 1449, "entity": "ManualIssueReason", "code": "SALE", "label": "Venta", "sortOrder": 4, "isEnabled": true, "isActive": true, "isDefault": true }
  ]
  ```

**If the company disables the reason afterwards** (System → Lists) or it is deactivated: the default **is ignored** — no reason comes back with
`isDefault`, the settings show `defaultManualIssueReason: null` and the device preselects nothing — with no error and without erasing what was saved:
if it is enabled again, it becomes the default again. Saving the rest of the settings (even if the screen resends the same saved code)
does not fail because of it.

| Field / case | Exact message | HTTP |
|---|---|---|
| `defaultManualIssueReason` with a code that does not exist, is inactive or is disabled by the company | `The reason {CODE} does not exist or is inactive.` (`errors.defaultManualIssueReason`; no setting of the request is saved) | 400 |
| Changing the settings without `admin.tenant` | (no application body: the policy rejects it; event `PERMISSION_DENIED`) | 403 |
| Issuing without a reason even if there is a default | `Enter the reason for the manual issue.` (`errors.reasonCode`) | 400 |

---

## 7d. Default category for new products (2026-10-11 c)

What it does: the company chooses **one product category that the Products screen preselects when creating a new product** (at Advance
Depot, **AxisCare**). The user can change or remove it on the same form before saving. **It only preselects**: it is not applied on its
own to a product created without a category through the API or an import, nor does it change products that already exist. By default no
company has a default category (the form starts empty, as before).

Who can: change it, `admin.tenant` (Tenant settings → Operations → “New products: default category”, module
**WMS_LOTSERIAL**); see it, any user with `inventory.view` (it arrives in the category list as `isDefault`). The change is recorded in the
company's **audit log** (field `DefaultProductCategoryId`, from → to).

How it is used:
- `PUT /api/v1/tenant/settings` with `{ "defaultProductCategoryId": 12 }` — the id of an **active category of the company**. `0` removes it;
  without the field (or `null`) it does not change. `GET /api/v1/tenant/settings` returns it in `defaultProductCategoryId` (id or `null`).
- `GET /api/v1/product-categories` (and a category's record) now carries `isDefault`; at most one comes back `true`.
- On the Products screen, **New product** opens with that category chosen; **editing** a product does not touch it.

**If the default category is deactivated afterwards:** the default **is ignored** (the settings show `defaultProductCategoryId: null` and the
screen preselects nothing), with no error and without erasing what was saved. Saving the rest of the settings, even if the screen resends the same
saved id, does not fail because of it.

**Advance Depot.** Section `2026-10-11 (c)` of `Diseño/logistica-db-update.sql` adds the column `Tenant.DefaultProductCategoryId` (with its FK
to `ProductCategory`) and, **only for the company “Advance Depot” and only if it is still empty**, sets it to the **AxisCare** category. The Depot
migration created that category as `AxisCare` (one word); the SQL looks it up ignoring spaces and case (`Axis Care` works too). If the
category does not exist or is inactive, **it does nothing**: it is chosen in Settings → Operations. It never overrides what the administrator already chose.

| Field / case | Exact message | HTTP |
|---|---|---|
| `defaultProductCategoryId` with an id that does not exist, is inactive or belongs to another company | `The default category does not exist or is inactive.` (`errors.defaultProductCategoryId`; nothing else is saved) | 400 |
| Changing the settings without `admin.tenant` | (no application body: the policy rejects it; event `PERMISSION_DENIED`) | 403 |

---

## 8. Purchases: suppliers, purchase orders and shortages

What it does: maintains suppliers, purchase orders (with cost frozen per line) and, after each receiving, lets you
resolve the pending shortage of a line (close, reorder or make a manual inventory adjustment). Since Batch 14,
shortages are resolved **only from the Shortages tab of the purchase order record**: the "Inventory adjustments" screen
(`/warehouse/inventory-adjustments`, which showed purchases with a partial receipt) left the menu and that address leads to Purchases. The
inventory adjustments themselves are in section 3 (Transfers & adjustments).

Who can: `purchasing.view` (list, record, shortages); `purchasing.manage` (create, edit, send, cancel,
delete the purchase order, and suppliers); `inventory.adjust` (resolve a line's shortage — `REORDER`
**also** requires `purchasing.manage`; `MANUAL_ADJUSTMENT` **also** requires the **WMS_LOTSERIAL** module). Module
**PURCHASING**.

How it is used:
- `GET/POST /api/v1/suppliers`, `PATCH /api/v1/suppliers/{id}`, `POST .../{id}/deactivate|reactivate`.
- `GET /api/v1/purchase-orders?status=&supplierId=&warehousePublicId=&supplierIds=&warehousePublicIds=&from=&to=&search=`.
  Batch 12: `supplierIds` and `warehousePublicIds` accept several values and are merged with the singular ones without repeats; the order
  appears if its supplier is **any** of those given and its warehouse is **any** of those given (different filters
  are combined with "and"). An id that does not exist gives no error: it simply matches no order.
- `POST /api/v1/purchase-orders` — `{ "supplierId": 1, "warehousePublicId": "...", "lines": [{ "productPublicId": "...",
  "qtyOrdered": 10, "unitCost": 2.5 }] }`. It is born `DRAFT`. At least one line with a quantity greater than zero. The warehouse is
  **optional in the API** only if the company has a single active warehouse (that one is used); with more than one it must be given. The
  screen always asks for it.
- `PATCH /api/v1/purchase-orders/{publicId}` (expected date, notes, line replacement; controlled by the
  `EDIT_PURCHASE_ORDER` capability). Since the 2026-09-30 adjustment it also accepts `supplierId` and `warehousePublicId` (the
  same identifiers as on creation), **only while the order is in `DRAFT`** (see below).
- `POST /api/v1/purchase-orders/{publicId}/send` — `DRAFT → SENT`.
- `POST /api/v1/purchase-orders/{publicId}/cancel` — from `DRAFT`, `SENT` or `PARTIAL`, with a comment.
- `DELETE /api/v1/purchase-orders/{publicId}` — with no receivings and no open receipt.
- `GET /api/v1/purchase-orders/shortages`, `GET .../{publicId}/shortage-lines`.
- `POST /api/v1/purchase-orders/{publicId}/lines/{lineId}/resolve` — `{ "action": "MANUAL_ADJUSTMENT", "quantity":
  1, "reason": "FOUND", "binId": 5 }` (or `"action": "CLOSE"` / `"REORDER"` without a quantity: they resolve the whole
  pending amount).

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Repeated supplier name (active) | `An active supplier with that name already exists.` | 409 |
| Empty supplier name | `The supplier name is required.` | 400 |
| Invalid supplier email | `The email address is not valid.` | 400 |
| Supplier phone that is not digits (fewer than 7, or more than 15 with the prefix) (`errors.phone`) | `Invalid phone.` | 400 |
| Supplier phone prefix outside 1 to 4 digits (`errors.phoneCountryCallingCode`) | `Invalid country prefix: use 1 to 4 digits, for example 1.` | 400 |
| Supplier from another tenant or nonexistent | `Supplier not found.` | 404 |
| No lines / more than 200 (create and edit) | `The purchase order must have at least one line.` / `The purchase order admits at most 200 lines.` | 400 |
| Creating without a supplier (`errors.supplierId`) | `Enter the supplier.` | 400 |
| Deactivated supplier | `The supplier is deactivated; it does not admit new purchase orders.` | 422 |
| Warehouse omitted and the company has more than one active warehouse (`errors.warehousePublicId`) | `Enter the warehouse: the company has more than one.` | 400 |
| Warehouse omitted and the company has no active warehouses | `The company has no active warehouses.` | 422 |
| Order warehouse deactivated | `The warehouse is deactivated; it does not admit new purchase orders.` | 422 |
| `PATCH` with `number`, `orderDate`, `currency`, `status` or `statusCode` | `The purchase order field number cannot be changed.` (with the name of the field sent) | 400 |
| `PATCH` with a `supplierId` or `warehousePublicId` **different** from the current one on an order that is not in `DRAFT` (2026-09-30 adjustment; checked before the `EDIT_PURCHASE_ORDER` capability) | `The supplier and the warehouse can only be changed while the purchase order is in draft.` | 409 |
| `PATCH` in `DRAFT` with a deactivated supplier / a deactivated warehouse | `The supplier is deactivated; it does not admit new purchase orders.` / `The warehouse is deactivated; it does not admit new purchase orders.` | 422 |
| `PATCH` with a `supplierId` or `warehousePublicId` from another company or nonexistent | `Supplier not found.` / `Warehouse not found.` | 404 |
| A client's product in the order | `The purchase order only admits own products; {sku} belongs to a client.` | 400 |
| Repeated product in the order | `The product {sku} is repeated in the purchase order.` | 400 |
| Inactive product in the order | `The product {sku} is inactive; it does not admit purchase orders.` | 400 |
| `qtyOrdered` ≤ 0 / > 3 decimals | `The ordered quantity must be greater than zero.` / `The quantity admits at most 3 decimals.` | 400 |
| Serial-tracked product: non-whole quantity | `The quantity of {sku} must be a whole number: the product is serial-tracked.` | 400 |
| Negative cost / > 4 decimals | `The unit cost cannot be negative.` / `The unit cost admits at most 4 decimals.` | 400 |
| Cost omitted and no default purchase cost | `Enter the unit cost of {sku}.` | 400 |
| Editing outside `DRAFT` without the capability enabled | `The current status does not allow the action 'EDIT_PURCHASE_ORDER'.` | 422 |
| Line with receivings: it is removed, goes below what was received or changes cost | `The line of {sku} already has receivings: it cannot be removed, cannot go below what was received ({x}) and its cost cannot change.` | 400/409 |
| Line without receivings but already used in an arrival notice | `The line of {sku} was already used in an arrival notice; it cannot be removed.` | 400/409 |
| Sending an order that is not in `DRAFT` | `Only a draft purchase order can be sent.` | 422 |
| Cancelling a `RECEIVED` order | `A fully received purchase order cannot be cancelled.` | 422 |
| Cancelling an already cancelled order | `The purchase order is already cancelled.` | 422 |
| Cancelling with an open receipt | `The purchase order has an open receipt; confirm it or delete it before cancelling.` | 409 |
| Deleting with receivings | `A purchase order with receivings cannot be deleted; cancel it.` | 409 |
| Deleting a `CANCELLED` order with receivings | `A cancelled purchase order with receivings is kept with its log; it cannot be deleted.` | 409 |
| Deleting a `RECEIVED` order | `A fully received purchase order is kept with its log; it cannot be deleted.` | 409 |
| Deleting with an open receipt | `The purchase order has an open receipt; delete it before deleting the purchase order.` | 409 |
| Shortage: unknown action | `Unknown action: use CLOSE, REORDER or MANUAL_ADJUSTMENT.` | 400 |
| Shortage: nothing pending | `The line no longer has a pending shortage.` | 409 |
| `CLOSE`/`REORDER` with a quantity different from the pending amount | `Close and Reorder resolve the whole shortage ({pending}); for a part use the manual adjustment.` | 400 |
| `MANUAL_ADJUSTMENT` without a quantity / ≤ 0 | `Enter the adjustment quantity.` / `The adjustment quantity must be greater than zero.` | 400 |
| `MANUAL_ADJUSTMENT` above the pending amount | `The adjustment quantity exceeds the pending shortage ({pending}).` | 400 |
| `MANUAL_ADJUSTMENT` without a note (or only spaces) | `Write a note explaining the adjustment.` (in `errors.notes`) | 400 |
| Resolving a shortage of a cancelled PO | `The purchase order is cancelled; its shortage is no longer resolved.` | 422 |
| Resolving a shortage with no confirmed receivings | `The purchase order has no confirmed receivings yet.` | 422 |
| Purchase order from another tenant or nonexistent | `Purchase order` (generic 404) | 404 |

**Supplier and warehouse of an order (2026-09-30 adjustment; before, in Batch 12, they never changed).** They are chosen when creating
the order and **can be changed while it is in Draft (`DRAFT`)** with the `PATCH`: `supplierId` and `warehousePublicId` are
optional (absent or `null` = no change) and are validated the same as on creation (active supplier and warehouse of its company). A
value **equal** to the current one does not count as a change: it is accepted in any status and is not validated again (so a screen
can send the full form). From Sent (`SENT`) on they are **fixed**: a different value responds 409. If the order was already
sent and the supplier or the warehouse is wrong, cancel it and create another. In Draft the order has no arrival notices
or receipts yet (they are created on receiving, from `SENT`), so changing the warehouse affects nothing else: the lines do not depend on the
warehouse. The change is recorded in the order's audit (`SupplierId` / `WarehouseId`), and respects `rowVersion` like the rest of the
`PATCH`. The supplier's phone is stored with digits only (`phone`) and with its international prefix (`phoneCountryCallingCode`, digits only and without “+”; the company's if not given): the API strips whatever format it receives and the screen shows the number with the company's mask (or “+593 991234567” if the prefix is a different one). Suppliers that already existed were cleaned up with the Batch 162 script.

### Statuses and transitions

`PurchaseOrderStatus`: **DRAFT** (initial) → **SENT** → **PARTIAL** → **RECEIVED** (terminal); **CANCELLED**
(terminal, allowed from `DRAFT`, `SENT` **and** `PARTIAL` — no lateral rule seeded, so the
status engine allows it from any of those three, with the comment in the history). `RECEIVED` is
terminal: it cannot be cancelled. Receiving against the order advances `SENT/PARTIAL → PARTIAL` (if something remains pending) or `→
RECEIVED` (if nothing remains). Resolving the last shortage of a `PARTIAL` also moves it to `RECEIVED`.

**Editing** (dates, notes, lines) is not a fixed rule: it is controlled by the `EDIT_PURCHASE_ORDER` capability
(`StatusCapability`), denied by default in `SENT`, `PARTIAL`, `RECEIVED` and `CANCELLED`. The tenant can
enable it in `SENT`/`PARTIAL` from `/api/v1/status/capabilities/PURCHASE_ORDER`; even so, a line with
receivings remains protected (it cannot be removed, cannot go below what was received, cannot change cost).

---

## 8b. Kits (Batch 68, 2026-10-16)

A **kit** is a product that is assembled when ordered from other products (for example, “Cleaning kit” = 2 disinfectants + 1 mop). **It has no stock or movements
of its own**: everything happens in its components. This batch leaves the catalog ready; its use in sales orders is in [18 §17](18-ordenes-de-venta.md#17-kits-in-sales-orders-batch-69) and in picking, in the next batch.

### 8b.1 Who and where

- See a product's kit: `inventory.view`. Make it a kit, change components or go back to a normal product: `inventory.manage`. Inventory module (`WMS_LOTSERIAL`).
- Screen: **Products & inventory → the product → “Kit” tab**. The product list marks kits with a “Kit” badge.
- API: `GET /api/v1/products/{publicId}/kit` and `PUT /api/v1/products/{publicId}/kit`.

### 8b.2 How it is used

1. Open the product → **Kit** tab → **Make it a kit**.
2. Choose the components (product and **quantity per kit**; decimals allowed) and, if you want, check **Adjustable when ordering** (in an order its pieces can be changed without touching the catalog).
3. **Save kit.** The tab shows the components and, per warehouse, **“N can be assembled”**: the smallest quotient between what is *available to sell* of each component and its quantity
   per kit, rounded down. “Available to sell” is the same count as in sales orders (without quarantine, cross-dock or rental).
4. **Edit components** replaces the list; **Back to a normal product** removes the components.

### 8b.3 Rules and messages

| Situation | Message | HTTP |
|---|---|---|
| No components | `A kit needs at least one component.` | 400 |
| More than 50 components | `A kit has at most 50 components.` | 400 |
| Component quantity 0 or empty | `Each component quantity must be greater than zero.` | 400 |
| Repeated product | `The component {SKU} is repeated.` | 400 |
| The kit as a component of itself | `The kit {SKU} cannot be a component of itself.` | 400 |
| A component that is already a kit | `{SKU} is a kit: a kit cannot have another kit as a component.` | 400 |
| Inactive component | `The component {SKU} is inactive.` | 400 |
| Component of another inventory owner | `The component {SKU} belongs to a different inventory owner than the kit.` | 400 |
| Lot- or serial-tracked product | `A kit is not tracked by lot or serial.` | 400 |
| Component that does not exist | `Component not found.` | 404 |
| Converting a product with stock or reservations | `{SKU} has stock: it cannot be made a kit while it has stock or reservations.` | 409 |
| Converting a product that is a component of another kit | `{SKU} is a component of another kit: it cannot be a kit.` | 409 |
| Turning a product that is not a kit back to normal | `The product {SKU} is not a kit.` | 409 |
| Turning back to normal a kit already used in sales orders | `The kit {SKU} was already used in sales orders: it cannot go back to a normal product.` | 409 |
| Receiving, adjusting or moving stock of the kit | `{SKU} is a kit: it has no stock of its own. Movements are made on its components.` | 400 |

Also: a kit never appears in counts (it has no balances) and cannot have stock; its components are normal, active products **of the same** inventory owner.

### 8b.4 For support

- Table `ProductKitComponent` (kit → component → quantity, unique per pair) and columns `Product.ProductTypeLookupId` (catalog `ProductType`: STANDARD / KIT; NULL = normal) and
  `Product.KitAdjustable`, section **2026-10-16 (p)** of `Diseño/logistica-db-update.sql`.
- The change of product type is recorded in the product's log; component changes too (with the product's entity type).

### 8b.5 Picking a kit (Batch 70)

`POST /api/v1/pick-batches` (and `collect-and-pack`, and the manual issue) accept a line with a **kit**: it is **exploded** into one line per component (quantity per kit × kits),
each with its bin by FEFO/FIFO like any product; all in the same pick batch and the same transaction.

- In the movement ledger, each exit carries the note `{KIT} × n · EMP-#####` (or `DMA-…` in a manual issue).
- The pick batch lines note the kit (`KitProductId`) and how many kits it represents (`KitQuantity`, only once per kit).
- A kit is picked in **whole pieces** and **does not admit bin, lot or serials**; if some component is not enough, 409 `insufficient_stock` with the shortage of that component and nothing leaves.
- Deleting the pick batch returns all the components to their bin (as always).
- A kit and one of its components in the same pick batch **share the same balance** (it is not counted twice).

| Situation | Message | HTTP |
|---|---|---|
| Bin, lot or serials on a kit line | `A kit is picked through its components: no bin, lot or serials are chosen.` | 400 |
| Quantity with decimals | `The kit {SKU} is ordered in whole pieces.` | 400 |
| Kit without components | `The kit {SKU} has no components: define them before selling it.` | 400 |
| A component is not enough | the insufficient inventory message of that component | 409 |

The Picking screen does not offer kits in its selector yet (it shows products with stock): they are used from sales orders or through the API.

### 8b. Importing suppliers with a template (2026-10-21)

Second entity of the generic template engine. **Who can:** `purchasing.manage` (Purchases module); see the templates, `purchasing.view`.

**How it is used:** Purchases → Suppliers → **Import**: the same as in Products (§2b): template, CSV or .xlsx file, **Review** (saves nothing) and **Import N** (creates the valid rows). **It only creates**: an active supplier with the same name is rejected.

**Template (Kind `SUPPLIER`):** `GET/POST /api/v1/suppliers/import-templates`, `GET/PATCH …/{publicId}`, `POST …/{publicId}/deactivate|reactivate`, `GET …/fields`; also in Integrations → Templates (Suppliers tab). Fields: `name` (required, always as a column), `contactName`, `phone`, `phoneCountryCallingCode`, `email`, `paymentTerm`, `notes`. `name`, `contactName`, `phone` and `email` do not accept a default value. **Import:** `POST /api/v1/suppliers/import` (multipart: `templatePublicId`, `file`, `commit`).

| Message | HTTP / where | What to do |
|---|---|---|
| “The template must include the name column.” | 400, template | Add the name column. |
| “The field name does not accept a default value.” (and contactName, phone, email) | 400, template | Each row brings that data. |
| “The supplier is repeated in the file.” | row | The same name twice in the file. |
| “An active supplier with that name already exists.” (and the other rules of manual creation: email, phone, payment term, lengths) | row | They are the same as for creating suppliers manually (§8). |

## 9. Cross-dock (demo): appointments and plans

What it does: schedules dock appointments (arrival/departure) and builds cross-dock plans that allocate lines of **open** receipts
(they are distributed when the receipt is confirmed, with the outbound shortage visible per allocation) or **confirmed** ones
(it directly reduces the pending putaway). What is allocated in the staging zone remains **reserved** in the ledger
(no pick batch, putaway or transfer takes it) until it is moved or cancelled.

This module is a **functional demonstration**: it comes **off** by default for the tenant.

**Direct receipts (Batch 16).** Allocation against an **already confirmed** receipt (mode "b" of allocations) depends on the pending putaway: it reduces the `PUTAWAY` task that remains to be done. A confirmed
**direct** receipt has no putaway tasks (the goods are already in their destination bin), so it **does not admit** cross-dock allocations after confirming. By the code, the server
responds 422 `The line's receipt does not admit allocations (deleted or with no pending putaway).` if the line has no receiving bin, or 409 `The quantity exceeds what is available for cross-dock (0).`
(not tested on a real receipt). To cross goods in a direct warehouse, allocate the cross-dock **while the receipt is open** (mode "a" of allocations): the line with cross-dock enters the receiving bin, requires no destination and its remainder generates the
putaway task toward its destination (section 4.1).

Who can: `inventory.view` (list and look up); `warehouse.crossdock` (schedule/reschedule/change status of
appointments; create plan, allocate, cancel allocation, move, complete). Module **CROSSDOCK** (off by default).

How it is used:
- `GET /api/v1/dock-appointments?warehousePublicId=&dockId=&fromUtc=&toUtc=&status=`.
- `POST /api/v1/dock-appointments` — `{ "warehousePublicId": "...", "dockId": 1, "direction": "INBOUND",
  "scheduledStartUtc": "...", "asnId": 3 }` (or `tripPublicId`, never both).
- `PATCH /api/v1/dock-appointments/{id}`, `POST .../{id}/status`.
- `GET/POST /api/v1/cross-dock-plans`, `GET .../{id}/candidates`.
- `POST /api/v1/cross-dock-plans/{id}/allocations` — `{ "receiptLineId": 10, "orderPublicId": "...", "quantity": 4 }`.
- `DELETE /api/v1/cross-dock-plans/{id}/allocations/{allocationId}?comment=`.
- `POST /api/v1/cross-dock-plans/{id}/allocations/{allocationId}/move`.
- `POST /api/v1/cross-dock-plans/{id}/complete`.
- The move is also triggered from the queue: `POST /api/v1/warehouse-tasks/{id}/complete` on the
  `CROSSDOCK` task (it requires the completed quantity to be exactly the confirmed one).

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Module off | `The 'CROSSDOCK' module is not enabled for this company.` | 403 |
| Overlap on the dock | `The dock already has an appointment that overlaps that time.` | 409 |
| End not after the start | `The appointment end must be after its start.` | 400 |
| Outside the horizon (yesterday to +90 days) | `The appointment must be scheduled between yesterday and the next 90 days.` | 400 |
| Direction incompatible with the dock type | `The dock is of type {type}; it does not admit {direction} appointments.` | 400 |
| Arrival notice and trip at the same time | `An appointment links to an arrival notice or to a trip, never both.` | 400 |
| Arrival notice with outbound direction | `An advance ship notice appointment must be inbound (INBOUND).` | 400 |
| Rescheduling an appointment that is not scheduled | `The appointment can only be rescheduled while it is scheduled.` | 422 |
| Allocation quantity ≤ 0 / > 3 decimals / non-whole with serial | `The quantity must be greater than zero.` / `The quantity admits at most 3 decimals.` / `For serial-tracked products the quantity must be a whole number.` | 400 |
| Allocation above what is available | `The quantity exceeds what is available for cross-dock ({available}).` | 409 |
| Order not eligible for allocation (cancelled, delivered, deactivated) | `The order does not admit allocations (cancelled, delivered or deactivated).` | 400 |
| Staging zone not of type CROSSDOCK/STAGING | `The plan's staging zone must be of type CROSSDOCK or STAGING.` | 400 |
| Moving with the receipt still open | `The line's receiving is not confirmed yet; the goods are moved after confirming.` | 422 |
| Moving an allocation with nothing confirmed | `The allocation received no goods on confirmation; cancel it.` | 422 |
| Completing the `CROSSDOCK` task with a quantity different from the confirmed one | `The cross-dock task is completed for the confirmed quantity ({qty}).` | 400 |
| Completing the plan with pending allocations | `Move or cancel the pending allocations before completing the plan.` | 422 |
| Plan already completed | `The plan was already completed.` | 422 |
| Inactive dock | `The dock is inactive; reactivate it or choose another.` | 422 |
| Appointment/plan/allocation from another tenant or nonexistent | `Appointment not found.` / `Cross-dock plan not found.` / `Allocation not found.` | 404 |

### Statuses and transitions

`AppointmentStatus`: **SCHEDULED** (initial) → **ARRIVED** → **COMPLETED** (terminal); **NO_SHOW** (lateral,
reason in the history comment) and **CANCELLED** (terminal) from `SCHEDULED`. Effect
`DockAppointmentStatusEffect`: `ARRIVED` occupies the dock (`FREE→OCCUPIED`); `COMPLETED`/`NO_SHOW`/`CANCELLED`
release it (`OCCUPIED→FREE`) if no other `ARRIVED` appointment remains on it.

`CrossDockStatus`: **OPEN** (initial) → **ALLOCATED** (with the first allocation) → **COMPLETED** (terminal, only
if no allocation is still `PLANNED`).

`AllocationStatus`: **PLANNED** (initial) → **MOVED** (terminal, after the `CROSSDOCK` that takes the goods out of
inventory) or **CANCELLED** (terminal, releases the reservation; if it already had something confirmed, that remainder goes back to
`PUTAWAY`).

---

## 10. Permissions and modules (summary)

| Permission | Category | What it enables |
|---|---|---|
| `inventory.view` | WAREHOUSE | See warehouses, products, inventory, receipts, tasks, counts, pick batches, cross-dock appointments and plans |
| `inventory.manage` | WAREHOUSE | Manage products and categories |
| `inventory.adjust` | WAREHOUSE | Adjust/transfer inventory, reconcile ("Run reconciliation", state of the automatic review), resolve discrepancies, resolve purchase shortages |
| `warehouse.manage` | WAREHOUSE | Manage warehouses, zones, bins, docks; assign/cancel tasks |
| `warehouse.receive` | WAREHOUSE | Receive goods (ASN and receipts); complete `PUTAWAY` tasks |
| `warehouse.pick` | WAREHOUSE | Pick and pack; complete `REPLENISH` tasks; run replenishment |
| `warehouse.count` | WAREHOUSE | Full cycle count, including confirmation ("Confirm count & adjust"), "Count what changed" and, since Batch 21, the reconciliation preview, "To review" and "Close the ones that match" |
| `warehouse.count.capture` | WAREHOUSE | Batch 8A: blind counting (create, capture, what was found and finish) without seeing what is expected or reconciling; implied by `warehouse.count`. Batch 21: also create a provisional bin from the count |
| `warehouse.crossdock` | WAREHOUSE | Cross-dock appointments and plans; complete `CROSSDOCK` tasks |
| `purchasing.view` | PURCHASING | See suppliers and purchase orders |
| `purchasing.manage` | PURCHASING | Manage suppliers and purchase orders |
| `purchasing.receive` | PURCHASING | Receive goods against a purchase order (existed before this batch) |

Role templates: `inventory.view` is also given to Warehouse operator, Read-only and Billing; the
other three new `WAREHOUSE` permissions (`inventory.manage`, `inventory.adjust`, `warehouse.manage`) are held only by
the tenant administrator by default.

Modules: **WMS_LOTSERIAL** and **PURCHASING** come on by default; **CROSSDOCK** off (demo).

Batch 14: one new permission, `pulse.attention` (category PULSE), to see "Needs your attention" in the Pulse (chapter 07, section 4);
the templates of Warehouse operator, Billing, Read-only and the administrator include it, and it was propagated once to the company
roles that already had `inventory.view`. Discrepancies are seen by `inventory.view` and resolved by `inventory.adjust`; "what changed" requires
`warehouse.count`. `GET /api/v1/warehouses/bins/search` requires `inventory.view`. The total is 66 permissions.

Batch 16: no new permissions or modules (the total number of permissions does not change). `GET /api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions` requires `inventory.view`; `POST
/api/v1/receipts/{publicId}/targets/suggest`, `warehouse.receive`; the warehouse's receiving mode and its default bin, `warehouse.manage`; the receipt's mode and the
destination of each line, `warehouse.receive`. The controller security test goes from 121 to 123 actions.

Products by bin report: no new permissions or modules. `GET /api/v1/warehouses/{publicId}/bin-products` requires `inventory.view`, module
**WMS_LOTSERIAL** (it replaces `bin-sheets` and `bin-sheets/mark-printed` from Batch 23). The controller security test goes from 130 to 129 actions.

Batch 26 (Rentals R0): `POST /api/v1/products/{publicId}/convert-to-serial` requires `inventory.manage` in the controller and `inventory.adjust` in the
service, module **WMS_LOTSERIAL**. No new permissions or modules; the controller security test goes from 130 to 131 actions.

Batch 12: `GET /api/v1/products/brands` requires `inventory.view` (like the product list) and `POST
/api/v1/warehouses/{publicId}/bins/capacity` requires `warehouse.manage` (like editing a bin). No new permissions.

Exception (Batch 11): `GET /api/v1/postal-localities` (list of cities and postal codes) requires no permission or module, only a
signed-in session. The screen only queries it when someone with `warehouse.manage` opens the city selector when creating or
editing a warehouse.

## Receipt cost layer (Batch 147, 2026-10-20)
When a receipt is **confirmed**, each line with a received quantity freezes its **unit cost** and where it came from: **“Purchase order cost”** (if the receipt comes from a purchase order), **“Arrival notice price”** (if the notice carries a price) or **“Product cost”** (the catalog purchase cost). It is shown in the **Unit cost** column of a confirmed receipt (the source, on hover). Changing the product cost, the notice price or the order cost afterwards does **not** change the cost already frozen.
- **Price on the arrival notice (optional):** in *New arrival notice*, column “Unit price (optional)” per line; only notices of a client. Error 400: “The unit price must be zero or greater and have at most 4 decimals.” / “The unit price exceeds the maximum allowed.”
- Receipts confirmed before this version have no frozen cost.

## Inventory valuation (Batch 148, 2026-10-20)
- **Method (Tenant settings → Operations → “Inventory valuation”):** *Average cost* (default) or *By layers (first in)*. It is based on the cost frozen on each receipt (see “Receipt cost layer”). Changing it affects **new** exits and reports; what is already recorded is not rewritten.
- **Cost of exits:** when picking, issuing manually or dispatching a sale or an exit notice, the unit cost frozen on the line is the average of what was received or that of the oldest layer still remaining, depending on the method. Without layers (earlier inventory), it is the product cost.
- **“Inventory value” report** (Products & inventory → **Inventory value** tab; permission `inventory.value`, held by Administrator and Billing): per product with stock, what is on hand, its unit cost and its value; totals of the whole filter (value, units, **units without cost**, method). Filters: warehouse and search box (SKU or name). It can be exported in full. A row with “No cost” has units with no cost anywhere: enter the product's purchase cost.

## Receiving requires a document (Batch 149, 2026-10-20)
Tenant settings → Operations → **Inbound** → “Receiving requires a document”: *Free* (default), *Arrival notice or purchase order*, *Arrival notice only* or *Purchase order only*. With any value other than Free, a **blind** receipt or a **manual return** cannot be created (HTTP 409): “The company requires receiving against a document (arrival notice or purchase order). Choose it; receiving without a document is not allowed.” (or the variant “…against an arrival notice…” / “…against a purchase order…”). If the document is not of the required type: “The company requires receiving against a client's arrival notice, not a purchase order.” / “The company requires receiving against a purchase order, not a client's arrival notice.” Receipts of a sales return are not affected.

## Product price on the arrival notice (2026-10-20)

When choosing the product on an arrival notice line, the **unit price** is preloaded with the product's cost (if no other had been typed). When saving the notice (`POST /asns`, fields `unitPrice` and `updateProductPrice` per line):
- If the product **has no cost** (empty or zero) and the line carries a price greater than zero, the product takes that price (if several lines are for the same product, the first one wins). The change is recorded in the product's audit.
- If the product already has a cost **different** from the one typed, the screen shows “ⓘ The product has X on file; this price is different.” (it does not block). Whoever has `inventory.manage` sees the checkbox “Update the product price with this one”; when checked, the product takes the typed price.
- If it is the same, nothing is touched. A cost layer already frozen never changes.

Error: 403 “Updating the product price requires the manage inventory permission.” (an update was requested without `inventory.manage`).

### The same on the purchase order

The purchase order line (create and edit, `POST/PATCH /purchase-orders`, `unitCost` and `updateProductPrice`) works the same way: the cost is preloaded with that of the chosen product; a product without cost takes the one typed on the first line that carries it; with a different cost on file the notice “ⓘ The product has X on file; this price is different.” appears and, with `inventory.manage`, the checkbox “Update the product price with this one”. If the cost is omitted, the line uses the product's and nothing changes. Same 403 without the permission.

## Arrival notice: lot, expiration and editing (Batch 157, 2026-10-20)

**What the lot on the line is for.** The client announces *which lot* comes on that line and *when it expires*, so the receiver can compare against what arrives and so the lot is born with its expiration when received (before, the receiver entered it by hand). That is why **it only appears on lot-tracked products**: on a product without lot neither the lot nor the expiration appears. The expiration is only accepted together with the lot number (400 “The expiration is only given together with the lot number.”).

**Editing before it arrives** (`PATCH /asns/{id}`, `warehouse.receive`; “Edit” button in the notice list): the warehouse (in case it is received at another one), the reference, the expected arrival and/or the lines (all are replaced, with the same rules as on creation) can be changed. The client does not change. Only while the notice is still expected and has no open receipt; with an open receipt, 409 “The arrival notice has an open receipt; delete it before editing the notice.”; cancelled or already received, 422 “The arrival notice is not pending receipt.”; one born from a purchase order, 409 “An arrival notice born from a purchase order is changed on the purchase order.”

**Notices (Notification rules):** Arrival notice requested, modified, received and cancelled (manual 14).

## Movement ledger: zone filter and even bar (Batch 159)

The movement ledger has a **Zone** filter (between Warehouse and Bin; multi-select with a search box; it offers the zones of the chosen warehouses or of all of them). `GET /inventory/transactions` and `/transactions/summary` accept `zoneIds` and resolve it to the bins of those zones (together with chosen bins, both must be met). The filter bar spreads the 14 filters in equal columns (5 on a wide screen) so the rows stay even. In Balances and Reconciliation the zone filter **does apply** since 2026-10-21: `GET /inventory/balances` and `GET /inventory/discrepancies` accept `zoneIds` (the bins of those zones; in Reconciliation, the total of a product without bin is left out).

**Putaway tasks (Batch 160):** they are no longer cancelled from the screen. The goods of a confirmed receipt are already received (the notice or the order remain as received): a putaway task is only assigned, started and completed (or split across several bins); to move it somewhere else afterwards, a transfer is made. They can be done at the receipt level (“Assign all”, “Start all”, “Complete all”). Replenishment can still be cancelled.

## Receipt in batches (Batch 174, 2026-10-21)

**What it is.** When the truck arrives in parts, the receipt can be **confirmed in batches**: each batch puts into inventory (receiving bin) what was received up to that moment and generates its putaway tasks, **and the receipt stays open** for what is missing. The usual **final confirmation** posts the rest, closes the receipt and marks the notice as received. A receipt that never uses batches behaves exactly as before.

**Who can.** Permission `warehouse.receive`, Warehouse module. Screen: Receiving → receipt record → **“Confirm batch”** button (above “Confirm receipt”). Endpoint: `POST /api/v1/receipts/{id}/confirm-batch` (optional body with `comment` and `rowVersion`).

**How it is used.**
1. Capture what was received on each line as always.
2. Press “Confirm batch”: what was received and not yet confirmed on each line enters inventory; one putaway task per line is created with those units. The batch is recorded (number, who, when, lines and units) in “Confirmed batches”.
3. Keep capturing (raise the line's received quantity) and confirm another batch, or close with “Confirm receipt”.

**What goes in and what waits.** Lines with new units go into the batch. **Batch 187:** products with a **serial number** go in when the line has **one serial for each received unit** (only new serials are posted), and **declared damage is reported with the batch** (DAN report; those units are not put away). **They wait for the final confirmation**: incomplete serials and lines with assigned **cross-dock** (the screen indicates it: “wait for the final confirmation”).

**How it is posted.** A batch records the entry (RECEIPT) up to the line's expected quantity and, if more is received, the excess as a receipt variance adjustment. **The shortage is not posted in a batch**; it is posted, if it remains, at the final confirmation (as before). What is posted between batches and closing always adds up to exactly what was received. The purchase order receives each batch separately (it stays “Partially received” until it is completed). The line cost is frozen on the first batch.

**What is blocked with confirmed batches.**
- A line's received quantity cannot go below what was already confirmed in batches, nor can its product or lot be changed, nor the line removed, nor the receipt deleted.
- A line's declared damage (total) cannot go below what was already reported in batches nor add up to more than what was received; serials already posted are not changed or removed.
- A **direct**-to-bin receipt does not admit batches (each line already enters its bin on confirmation).

**Status.** The batch does not change the status (the receipt stays Receiving or Discrepancy). At the final confirmation the receipt goes to Received (or Received with variance); if no putaway task remains open it goes to Put away immediately, and if batch tasks remain open it goes to Put away when the last one is completed.

**Messages and codes.**
| Situation | Message | HTTP |
|---|---|---|
| Direct receipt | `A direct receipt does not admit batches: each line already enters its destination bin on confirmation.` | 422 |
| No new units | `There are no new units to confirm: capture what was received on at least one line before confirming a batch.` | 422 |
| Lowering the received quantity below what was confirmed | `The received quantity cannot be left below what was already confirmed in batches.` (`receivedQty`) | 400 |
| Changing product/lot or removing the line | `The line already has units confirmed in batches: its product or lot cannot be changed, nor can it be removed from the receipt.` | 409 |
| Deleting a receipt with batches | `The receipt already has confirmed batches: it cannot be deleted.` | 409 |
| Missing lot on a batch line | the same per-line error (`lines[i]`) as the final confirmation | 400 |

**Frequent cases.** See the “Receipt in batches” questions in the FAQ.

## Master pick batch: several documents, one shipment (Batch 176, 2026-10-21)

**What it is.** The **master pick batch** gathers several documents —sales orders and/or exit notices— **of the same client and warehouse** that are picked **together** and leave in **a single shipment**. Each document keeps its own pick batch (its movements, its reservation, its status and its progress, as always); the master is the document that groups them, with an EMP-##### number (the shipment's), and shows **all the movements**: what left from which bin, with its lot and for which document.

**Rules decided by the owner.**
- It is built **when picking**: the documents are chosen and everything is picked together. **Documents are not added afterwards**; if another one arrives, another pick batch is created.
- **A single client and a single destination**: documents of different clients are not mixed. If different destinations are wanted, separate pick batches are made.
- From **2 to 20 documents**. Without specifying lines, **everything reserved** of each document is picked; lines, quantities, bin and lot can also be specified (the same as dispatching a document).

**Who can.** Pick, pack and delete: permission `warehouse.pick` (packing also `orders.create`; deleting a packed one, `orders.cancel`), Warehouse module (with sales orders, that module too). View: `inventory.view`.

**Endpoints.** `POST /api/v1/pick-batches/groups` (pick), `GET /api/v1/pick-batches/groups` (list; filters `search`, `status`), `GET /api/v1/pick-batches/groups/{id}` (record with documents and movements), `POST /api/v1/pick-batches/groups/{id}/pack` (pack: ONE shipment), `DELETE /api/v1/pick-batches/groups/{id}` (delete).

**Status.** COLLECTED (picked) → PACKED (packed, with its shipment) and CANCELLED (deleted). It is derived from its pick batches.

**What changes in the individual pick batches.** A pick batch that belongs to a master **is not packed nor deleted once packed on its own**: it is done from the master (packing creates a single shipment for all of them; deleting undoes all of them). Deleting one pick batch of the master **before packing** is allowed (only that one).

**Deleting the master.** It undoes **all** its pick batches: the inventory goes back to its original bin and each document recovers its reservation (and its status). If it was already packed, its shipment is also deleted, **only if it is still in the initial stage**.

**Messages and codes.**
| Situation | Message | HTTP |
|---|---|---|
| Fewer than 2 documents | `Choose at least two documents to pick together; for a single one use the normal dispatch.` | 400 |
| More than 20 | `A master pick batch admits at most 20 documents.` | 400 |
| Repeated document | `The same document is repeated.` | 400 |
| Unknown type | `Unknown document type: “X”. Use SALES_ORDER or EXIT_NOTICE.` | 400 |
| Another client | `The documents must belong to the same client: “A” and “B” belong to different clients. Make separate pick batches.` | 400 |
| Another warehouse | `The documents must belong to the same warehouse: “A” and “B” belong to different warehouses.` | 400 |
| Nonexistent document | `Sales order not found.` / `Exit notice not found.` | 404 |
| Nothing reserved | `“X” has nothing reserved to take out.` | 422 |
| Insufficient inventory | the usual one (`insufficient_stock`), with no partial effect | 409 |
| Packing one already packed or deleted | `Pick batch X is already packed or deleted.` | 422 |
| Packing/deleting a pick batch of the master on its own | `Pick batch X belongs to master pick batch Y: pack it or delete it from the master.` | 422 |
| Deleting with the shipment already advanced | `Pick batch X cannot be deleted: its shipment is already in “status”.` | 422 |

### Master pick batch screens (Batch 177)

**Where.** Warehouse → Picking & packing → list on the right. It now has three views: **By document**, **Masters** (new) and **By movement**; the view goes in the address (`?view=groups`).

**Collect together** (permission `warehouse.pick`). In “By document”, **“Collect together”** button: it opens the list of exit notices and sales orders with reserved inventory to take out. Check the ones that leave together (minimum 2, maximum 20) and press **“Collect together (N)”**. When the first one is checked, those of **another client are disabled** (a master is of a single client and a single destination). **Everything reserved** of each one is picked; the note is optional. When finished, the record of the created master opens. Server errors (different client, nothing reserved, insufficient inventory) appear in the same dialog and nothing is picked.

**“Masters” view.** Number, client, documents it gathers, quantity, status (Picked / Packed / Deleted), shipment and date. Filter by status and search box (number, client or a document's number). Sort and export use the whole filter.

**Master record** (click on the row). Summary (client, warehouse, picked by/when, quantity, shipment, note), the **documents** it gathers and **all the movements grouped by document**: product, **which bin it left from**, lot and quantity. Actions: **Pack** (`warehouse.pick` + `orders.create`; opens the usual packing form and creates **a single shipment** for the whole master) and **Delete** (`warehouse.pick`; undoes all the pick batches and, if it was packed, its shipment while it is still in the initial stage; requires `orders.cancel`).

**What you will see in the individual pick batches.** The record of a pick batch that belongs to a master no longer offers Pack (it is done from the master) and shows its master's number.

### Exit notices in Views, indicators and charts (2026-10-21)

Exit notices are now one more **data source** (`EXIT_NOTICE`, “Exit notices”) for building views, indicators and charts (Analytics). One row per notice with: number, client, warehouse, reference, destination, status, **Open** (requested or partial), **With backorder** (some line is waiting for inventory), **Overdue** (open and with a required date before today), **Assigned**, dates, lines, units requested / reserved / dispatched / pending and **Fulfillment (%)** (only on fully dispatched notices). It is read with `inventory.view` and requires the warehouse module with lots/serials. No system view comes prebuilt: each company creates its own (for example, “Overdue notices” = Open and Overdue).
