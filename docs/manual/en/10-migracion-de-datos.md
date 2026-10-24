# Chapter 10 — Legacy data migration (Batch 10)

What it does: a command-line command, outside the website, that provisions a new company and populates it from the
**QuickBooks Desktop** exports (CSV) and, when applicable, from the database of the legacy WMS **MSWM**.
It always writes through the same services the site uses (products, clients, consignees, contacts,
suppliers, warehouses and the inventory ledger), so there is an audit trail, status history and inventory
reconciliation just like any creation from a screen. It adds no new permissions, endpoints or screens: it is operated by whoever
has access to the server, not by a site user.

Who can: it is run from the server command line, not from the site; there is no associated permission. The
migration plan and the decisions about what is brought from each company are in
[`docs/migracion-depot-solutions-plan.md`](../migracion-depot-solutions-plan.md).

How to use it:

```
dotnet run --project src/Teikem.Api -- import-legacy <config.json> [--dry-run] [--update]
dotnet run --project src/Teikem.Api -- db-reset --yes [--allow-remote]
```

## 1. Simulate and load

- `import-legacy <config.json> --dry-run`: reads all the sources, builds the plan with the same rules the
  services would use and writes a report (`.md` + one CSV per section) **without writing anything to the database**. Reviewing this report
  before the real load is mandatory.
- `import-legacy <config.json>`: the real load. Order: company → categories → warehouse, zones and bins →
  suppliers → products → custom fields → clients with consignees and contacts → opening balance
  (`ADJUSTMENT` with reason `OPENING_BALANCE`) → deactivation of the products that are inactive in QuickBooks → inventory
  reconciliation.
- From the QuickBooks product the **brand** (column `Brand`) is also imported: it goes to the product brand (up to 100
  characters; empty = no brand). `--update` does not change the brand of products that already exist. In Depot only 25 items have a brand and 24
  belong to the "SOLUTIONS" category, which the Depot configuration excludes; 1 product with a brand remains. Model does not come from the
  migration.
- It is **idempotent**: it can be repeated. The company is looked up by name, the category by name, the product by
  SKU, the supplier by name, the client by its "QuickBooks Code", the bin by code; what already exists is
  counted as "Already existed" and is not touched. The opening balance is not repeated if there is already an `OPENING_BALANCE` entry into
  that warehouse.
- A rejection of a row (duplicate SKU, address without city, unknown payment term) is recorded with its exact
  message in the report and the load continues with the next row. A **serious rejection** (configuration error, a
  source that could not be read, the company that could not be provisioned, or the inventory reconciliation with
  differences) makes the command end with exit code `1`.

## 2. Refresh without starting from scratch (`--update`)

What it does: besides adding what is missing, it updates from QuickBooks the products, suppliers, clients and
consignees that already exist in Teikem. Meant for when the QuickBooks data changes after the initial
load (a price, an address, a new client) and there is no need to repeat everything.

- `import-legacy <config.json> --update`: adds what is new and updates what exists.
- `import-legacy <config.json> --dry-run --update`: simulates the refresh (what would change) without writing anything.
- **The opening balance is never touched in `--update`.** Once the company operates in Teikem, the inventory is moved by
  Teikem's receipts, dispatches and adjustments, not by QuickBooks; the report says *"The opening balance is not touched in
  --update mode: the inventory is moved by Teikem."* and counts those rows as skipped.
- An empty value in QuickBooks **never erases** data already captured in Teikem: a field is only updated when
  QuickBooks brings a value and that value is different from the current one.
- Fields that are updated: in products, name (unless it stays equal to the SKU for lack of a description),
  category, cost, price and barcode — never the SKU, the base unit of measure or the tracking type
  (they are immutable once the product has movements); in suppliers, contact, phone and notes; in
  clients, legal name, payment term and credit limit — the client name and code do not change; in
  consignees, the address of the already existing delivery or billing location.
- The report adds an **Updated** column to the summary and an **Updates** section (entity, key, field,
  previous and new value).
- For a typical refresh it is enough to export again from QuickBooks the **Items** and **Customers** files of the
  company (and **Vendors** only if there are new suppliers); there is no need to touch the WMS.

## 3. Recreate the database from scratch (`db-reset`)

What it does: completely deletes the configured database and creates it again exactly like `db-init` on a
clean server (structure, seed, seeders and the demo tenant). Meant to repeat the migration from scratch as many
times as needed during testing, without dragging data from a previous load.

```
dotnet run --project src/Teikem.Api -- db-reset --yes
```

- **It is irreversible**, which is why it requires `--yes`. Without that flag, the command refuses with the exact message
  *"db-reset deletes the entire database; confirm with --yes."* and ends with code `2`, without touching anything.
- **It only acts on a local server** (`localhost`, `127.0.0.1`, `(local)`, `.`, `::1`, `(localdb)\…`, with or without
  port or instance). Against any other server it refuses with *"The connection string does not point to a local
  server; use --allow-remote if you really want to delete that database."*; it is forced with `--allow-remote`.
- **It never deletes a database whose name starts with `MSWM`**: the legacy WMS is protected separately, with the message
  *"db-reset does not touch the legacy WMS database."*, even if someone points `ConnectionStrings:Teikem` there by mistake.

The full cycle to migrate again from scratch:

```
dotnet run --project src/Teikem.Api -- db-reset --yes
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.depot.json
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.solutions.json
```

## 4. Estimated capacity of the bins (Advance Depot)

What it does: when migrating a warehouse that comes from the MSWM WMS (Advance Depot), each bin receives a **maximum capacity** (product
units, the same "Max capacity" field of the warehouse record) estimated from its history in the WMS. Advance
Solutions has no WMS: its bins do not receive a capacity.

- **History that is looked at** (only the configured warehouse, `Main`): the current inventory (`Inventory`), the previous
  inventory (`Inventory_Old`), the cycle counts (`CycleCountInventory`, what was counted per request), the count
  history (`CycleCountHistory`, the resulting quantity per request) and the putaways (`PutAwayHistory`, what was put away
  into the bin added up per day). In each "snapshot" everything that was in the bin is added up.
- **Bin with history** (origin `HISTORIAL`): the highest total of its snapshots, rounded **up to the ten**
  (minimum 10). Example: 1,212 units → capacity 1,220; 3 units → 10.
- **Bin without history**: the **median** of the capacities with history of its same **aisle** (`PASILLO`; the aisle is
  the first number of the code, `01-A-24` → `01`); if the aisle does not have enough, that of its **zone** (`ZONA`); if
  not either, that of the **warehouse** (`ALMACEN`). The median is also rounded up to the ten. **An aisle or zone median
  is only used if it comes from at least 5 bins with history**; with less data it moves to the next
  level (with two data points, 30 and 264,600, the "median" was 132,320, which represents nobody). The special bins
  without aisle (`PISO`, `R1`…) go straight to their zone.
- **An existing capacity is never overwritten.** A bin that is created in the run is born with its capacity. One that already existed and
  has a capacity (captured by hand or from a previous load) always keeps it. One that already existed **without** capacity is only
  filled with `--update`; without `--update` it is not touched. `--dry-run` calculates and reports without writing.
- **Report**: one more CSV, `{prefijo}-{fecha}-cupos.csv` (bin, zone, aisle, historical maximum, capacity, origin and
  result: *Assigned when creating the bin*, *Would be assigned when creating the bin*, *Assigned (--update: the bin had
  no capacity)*, *The current capacity is kept (N)*, *No change: the bin already existed without capacity (use --update to
  fill it)*), and in the `.md` the section **Estimated bin capacities** with how many bins came out of each origin
  (and their minimum, median and maximum capacity).
- **Actual result in Advance Depot** (run of 2026-09-30, 3,886 bins): `HISTORIAL` 2,709 (capacity from 10 to 264,600,
  median 50), `PASILLO` 645 (10 to 120, median 20), `ZONA` 528 (50) and `ALMACEN` 4 (50). It is an estimate: for example, the
  `CARTONES` bin comes out with 264,600 because of its history and the preparation bins (`R1`, `S1`) also receive a capacity;
  it is advisable to review them.
- To correct capacities after the load, use the warehouse's bulk assignment (chapter 6, "Bulk capacity").

### Validations and exact messages

| Case | Exact message | Exit code |
|---|---|---|
| `import-legacy` without a configuration path, or with options other than `--dry-run`/`--update` | `Usage: dotnet run --project src/Teikem.Api -- import-legacy <config.json> [--dry-run] [--update]` | 2 |
| `import-legacy` with a configuration file that does not exist or is missing a mandatory value | (validation message, for example `The file does not exist: {ruta}.`) | 1 |
| The reason `OPENING_BALANCE` is not in the list (`db-init` with the batch's seed has not been run) | `The adjustment reason OPENING_BALANCE does not exist in the list; run db-init before the load.` | 1 |
| A payment term mapped from QuickBooks is not in the list | `The payment term {código} does not exist in the list; run db-init before the load.` | 1 |
| Manual inventory adjustment with reason `OPENING_BALANCE` (from the site or the API) | `The reason OPENING_BALANCE is assigned by the system.` | 400 |
| `db-reset` without `--yes` | `db-reset deletes the entire database; confirm with --yes.` | 2 |
| `db-reset --yes` against a server that is not local, without `--allow-remote` | `The connection string does not point to a local server; use --allow-remote if you really want to delete that database.` | 2 |
| `db-reset --yes` against a database whose name starts with `MSWM` | `db-reset does not touch the legacy WMS database.` | 2 |
| `db-reset` with syntax other than `--yes [--allow-remote]` | `Usage: dotnet run --project src/Teikem.Api -- db-reset --yes [--allow-remote]` | 2 |
| Warning (does not stop the load): no bin of the WMS warehouse has history | `No bin of {almacén} has stock history in the WMS; the bins are left without capacity.` | 0 |
| `warehouse.receivingMode` other than `PUTAWAY` and `DIRECT` (Batch 16) | `warehouse.receivingMode must be PUTAWAY or DIRECT.` | 1 |
| `products.trackingType` other than `NONE`, `LOT` and `SERIAL` (Batch 30, section 6) | `products.trackingType must be NONE, LOT or SERIAL.` | 1 |
| `warehouse.defaultReceivingBin` that is not the code of a bin created for the warehouse (Batch 16) | `The default receiving bin {code} does not exist in the warehouse.` (warehouse rejection in the report; the default bin is not set) | — |

## 5. Receiving mode and default receiving bin of the warehouse (Batch 16)

What it does: the warehouse configuration (`warehouse`) accepts two optional keys that set how the warehouse receives goods (details in
[chapter 6, section 1.4](06-inventario-y-almacen.md#14-receiving-mode-and-default-receiving-bin-batch-16)):

| Key | Values | What it does |
|---|---|---|
| `warehouse.receivingMode` | `PUTAWAY` (with putaway) or `DIRECT` (direct to bin). Without the key, `PUTAWAY` | Receiving mode of the warehouse. **Advance Solutions uses `DIRECT`** (it only has the `GENERAL` bin, with no receiving zone) |
| `warehouse.defaultReceivingBin` | Code of a bin of a `STAGING` or `CROSSDOCK` zone of the warehouse | Default receiving bin. **Advance Depot uses `R1`** (zone `STG`), not the first one by zone code (`S1` of "Embarque") |

- **Only when creating the warehouse.** The two keys are applied when the run creates the warehouse. **`--update` does not overwrite them** in a warehouse that already exists: there they are changed in Warehouses → Details → Receiving.
- The default bin is set after creating the warehouse bins. If the code does not exist or does not belong to a receiving or cross-dock zone, the warehouse is rejected in the report with the message from the messages table (section 4) and the default bin is not set.
- The run report shows "Receiving mode" and "Default receiving bin" when they are configured. In `--dry-run` it is only reported; nothing is written.
- Examples: `docs/migracion/import.solutions.json` (`"receivingMode": "DIRECT"`) and `docs/migracion/import.depot.json` (`"defaultReceivingBin": "R1"`).
- Exact messages: `warehouse.receivingMode must be PUTAWAY or DIRECT.` (configuration error, exit code 1) and `The default receiving bin {code} does not exist in the warehouse.` (warehouse rejection); see the table in section 4.

## 6. Product tracking: QuickBooks Serial and Lot checkboxes (Batch 30, Rentals RM)

What it does: decides with which **tracking** (no tracking `NONE`, by lot `LOT` or by serial `SERIAL`) each product is **created**. Meant
for Advance Depot's rental units (rentals only work with serial-tracked units). Full evaluation and procedure for the
owner: [`docs/migracion/depot-series-y-rentas.md`](../migracion/depot-series-y-rentas.md).

Who can: same as the rest of the command (server command line, no site permission).

| JSON key | Values | What it does |
|---|---|---|
| `products.trackingType` | `NONE` (default), `LOT` or `SERIAL` | Tracking with which the products are created. Another value stops the run (table below). |
| `products.trackingFromColumns` | `{ "serial": true/false, "lot": true/false }`; both `false` by default | With `serial: true`, an item whose QuickBooks `Serial` checkbox is `TRUE` is created **SERIAL**; with `lot: true`, one with `Lot` set to `TRUE` is created **LOT**; with both checked SERIAL wins; otherwise, `trackingType` applies. `TRUE`, `YES`, `Y` and `1` are accepted. |

Rules:
- **Only when creating.** A product that already exists **never** changes its tracking through the migration, not even with `--update` (rule D25: with
  movements only "Convert to serial", chapter 6 §2.1, changes it). If the source asks for another, the report warns about it.
- **Opening balance without serials.** No source brings serial numbers or lots (QuickBooks only has the checkboxes; Depot's MSWM WMS
  has no serials). A product that would ask for `SERIAL` or `LOT` and **has an opening balance** is created with **`NONE`** and its balance is loaded all the same;
  the report lists it as **"Candidate for Convert to serial"** (if it asked for SERIAL). It also applies when the SERIAL or LOT comes from
  `products.trackingType`. Without an opening balance it is created with the requested tracking.
- The decision uses the opening balance **of the plan**: if in the run the balance is not loaded (it was already loaded, or `--update` on a company
  that already existed), the new product with stock in the source is still created `NONE`.
- **Advance Depot and Advance Solutions do not change**: their JSON files keep `NONE` and the option turned off. For Depot it is **not** recommended
  to turn on `lot` (500 of 576 items have `Lot = TRUE` by default).
- The report (also in `--dry-run`) adds two types of **mapping**: **Tracking** (`{sku}` → `SERIAL` or `LOT`: product created with that
  tracking) and **Candidate for Convert to serial** (`{sku}` → `{n} units in {m} bin(s); created with NONE`, or `already exists with NONE;
  not changed`).

Exact messages:

| Case | Exact message | Exit code / where |
|---|---|---|
| `products.trackingType` other than `NONE`, `LOT` and `SERIAL` | `products.trackingType must be NONE, LOT or SERIAL.` | 1 |
| Item that asks for SERIAL with an opening balance | `Item {sku} is marked as serial-tracked but has an opening balance ({n} units) and the source does not bring its serial numbers; it is created with tracking NONE. Convert it afterwards with "Convert to serial".` | Warning (0) |
| Item that asks for LOT with an opening balance | `Item {sku} is marked as lot-tracked but has an opening balance ({n} units) and the source does not bring its lots; it is created with tracking NONE.` | Warning (0) |
| Product that already exists with another tracking | `Product {sku} already exists with tracking {actual}; the migration does not change the tracking of an existing product (in the source it is marked {pedido}). If it is a unit with a serial number, use "Convert to serial".` (the last sentence only if the source asks for SERIAL) | Warning (0) |

Frequent cases:
- *"I want to rent Depot units that are already in the warehouse."* → "Convert to serial" (chapter 6 §2.1) with the numbers of each unit;
  there is no need to re-import.
- *"I turned on `trackingFromColumns.serial` and the report says they already exist."* → That is expected on an already loaded database: the
  "Candidate for Convert to serial" list is your work list for R0.

## Frequently asked questions

See the section "Batch 10 — Data migration" in [faq.md](faq.md).
