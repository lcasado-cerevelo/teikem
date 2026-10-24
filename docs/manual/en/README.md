# Teikem functional manual

> **English edition.** Translation of the Spanish manual in `docs/manual/` (source: commit 808124ef plus the pending edits of 2026-10-24 to chapter 41 and the FAQ). File names are the same as in Spanish so links and the help index map one to one; the screenshots in `frontend/img/` were taken with the interface in English where a Playwright walkthrough could regenerate them (see [translation-notes.md](translation-notes.md) and [captures-pending.md](captures-pending.md)). The Spanish manual stays the reference: when a chapter changes there, this edition must be updated.

## What Teikem is

Teikem is a logistics platform for several companies (multi-tenant): each business that uses it (each
"tenant") sees only its own data, with its own configuration of lists, statuses, users and roles. The
same user can belong to more than one company and choose which one to work with when signing in.

The platform is built in batches. Each batch adds a business module (orders, warehouse, fleet, equipment
rental, purchases, client portal, etc.) on top of a common base of security, lists, statuses,
audit, custom fields and analytics (views, indicators, charts). This manual documents, chapter
by chapter, what each batch makes available to the end user and to support.

## Chapters

1. [01 — Platform and security](01-plataforma-y-seguridad.md): access and session, MFA, my profile and permissions,
   tenant users and roles, lists, statuses, contact points, custom fields, views,
   indicators, charts and Day pulse, audit and security, modules per tenant, company
   configuration, platform administration.
   Region and formats (2026-10, section 11.1): Puerto Rico or United States region with its set of values, the company's
   time zone (the one "today" is counted with), currency and date, time, number and phone formats, each one changeable separately,
   with their validations and messages.
   Batch F10 (sections 1.5, 9 and 9.1): **company-wide sessions** (list with `admin.audit`, revoke one or the others with
   `admin.users`, session IP) and the **unified activity** with real total, search in the database and type and result codes.
   Batch 19 (sections 11 and 11.2): **company branding on the server** — colors are validated with the same rules as the
   screen (themes, WCAG contrast, hue separation, fields and size) and the **four logos** (lockup and square mark, each one
   with its variant for dark background; SVG, PNG, JPG or WebP up to 512 KB) are stored in the database and validated by their real content
   (SVG without active content or external references), with their 400/413/415 messages; and the **409 when repeating a holiday**.
   Batch 42 (section 3.6): **isolation between companies** — how it is guaranteed that each company sees only its own data, why since
   2026-10-12 large lists are faster without changing what is seen, and what to do if the order list is still slow (`db-update`).
2. [02 — Clients and contracts](02-clientes-y-contratos.md): client file (composite creation, profile, numbering,
   contacts, status, deactivation), consignees and locations (physical/postal addresses, warehouses, shared ones; import
  of consignees by CSV with template, validate → confirm, Batch 38; Excel, “(Do not use)”, joined columns, default client and contact person, Batch 45, §2.2; town, state and ZIP with the postal code list and mapping variants, Batch 46, §2.3), contracts
   (current contract, billing model with 5 components, dispatch, COD, SLA, statuses and effects), rates per service and
   extra piece with effective-dated history, quote, special services and shared types, client portal
   users (includes the multi-client portal of Batch 3), permissions and modules, analytics sources and audit.
3. [03 — Transport orders](03-ordenes-de-transporte.md): order capture (quick entry, detailed entry and special
   delivery), numbering of the four identifiers, quote and credit check when confirming (with authorization by
   permission when exceeded), statuses and transitions, order importer by template (validate → confirm), the
   blocking of anything new for a deactivated client, permissions and modules, analytics sources and audit.
   **Batch 106 (2026-10-17, section 4 and sections 1, 3, 7 and 8): new status flow.** The order is born **Submitted** (there is no longer
   Draft or Confirmed), it is quoted and the credit is measured when created, “Confirm” became **Mark as picked up**, cancelling
   requires a reason and there is a new action, **Reopen** (permission `orders.reopen`); the statuses `DRAFT`, `CONFIRMED`, `INBOUND`,
   `PLANNED`, `ARRIVED`, `ON_HOLD` and `PARTIAL` no longer exist. Chapters 04, 05, 07, 19, 28 and 30 and the screen
   chapters fc1, fc2, fc3, fe1, fe2a, l89 and l103 were also updated; the questions are in the section “Shipment status (Batch 106)” of
   [faq.md](faq.md).
4. [04 — Fleet, drivers and maintenance](04-flota-choferes-mantenimiento.md): vehicles and their documents, drivers
   (identity, licenses, certifications, devices, dispatch zones), documents about to expire and availability
   for dispatch, preventive maintenance and work orders, fuel log, driver rates (per
   delivery, per attempt, per trip) and pay policy with preview, trips paid to the driver, the special
   delivery with driver (extends Batch 3), statuses and transitions, permissions and modules, analytics sources and
   audit.
5. [05 — Trips and routes](05-trips-y-rutas.md): daily planning (creation with numbering and default driver/departure,
   record, header editing, delete route, bulk reassignment), consolidation of orders on the
   route and "Unassigned" list, optimization in three phases (HEURISTIC engine), manual reordering and pin per
   stop, dispatch and departure (extends the seam of the future Batch 7), dispatch zones by postal code/range/
   municipality (extends Batch 4), Outbound scan station, "Plan the day", route monitor, statuses and
   transitions, permissions and modules, analytics sources and audit.
6. [06 — Inventory and warehouse](06-inventario-y-almacen.md): warehouses, zones, bins and docks; products,
   categories, lots and serials; inventory (balances, movement ledger with signed quantity, adjustments, transfers,
   genealogy, serial trail and ledger↔balance reconciliation); receiving (arrival notices and receipts, including
   receiving against a purchase order); warehouse task queue (directed putaway and replenishment); cycle count in
   informed mode; ad hoc picking and packing (Pick & Pack); minimal purchases (suppliers, purchase orders and
   shortage resolution); cross-dock in demo mode (appointments and plans), statuses and transitions, permissions and
   modules.
   Batch 21 (section 6, Cycle count): **count by product**, original capture and **supervisor correction** with evidence,
   **preview** of the reconciliation, **To review** list, **bulk close** of the counts that match and **provisional
   bins** created from the count (server; the web and the app use it in their own batches).
   Batch 25 (section 6.x, Cycle count; app §7.2): **open count with several products** — `allowEmpty` without product opens an empty count,
   `GET /cycle-counts/{id}/product-bins` says where each product is (without quantities) and adding a line **without bin** uses the only one with
   stock (or asks to choose); app and web build ONE count by scanning products.
   Batch 26 (section 2.1, Products; Rentals R0): **convert to serial** — `POST /products/{publicId}/convert-to-serial` turns a product
   without tracking and with movements into serial tracking, capturing one serial per unit on hand in each bin (`−`/`+` adjustments with
   the system reason `TRACKING_CONVERSION`, net zero, in a single transaction), with its 400/403/404/409/422 messages.
   **Products by bin** report (section 1.5, Warehouses and locations; replaces the bin sheets of Batch 23): query
   `GET .../bin-products` with the filters of the bin list, the products with stock in each bin (SKU, name and
   barcode) to print and scan from paper; stateless (server and web).
   Batch 11 (section 1, Warehouses and locations): **maximum capacity** of the bin and its **occupancy state**
   (empty, partial, full, occupied without capacity), occupancy calculated per zone, editable zone code, paginated bin
   list with search by code, zone, aisle, rack, level or bin, and the list of **postal
   localities** (42,522 ZIP codes of the US and Puerto Rico) to fill in city, state, postal code and country.
   Batch 12 (sections 1.2, 2 and 8): product **make and model**, new filters of the product list (several
   warehouses, SKU, name, makes, "with serial" and "incomplete serials") and makes endpoint, movement ledger filters by make and
   name, multiple filters for purchase orders, **bulk capacity assignment** of bins and summary of the **estimated
   capacity** brought by the Advance Depot migration.
   Batch 13 (sections 4, 5 and 7): **receipt status cycle** (Expected, Receiving, Discrepancy, Completed, Completed with
   variance and Put away) with its transitions and what each one blocks, editable receipt header (carrier, reference,
   default receiving bin), blind expected and returns (what was received enters inventory), list filters
   (`variance`, `phase`) and arrival notice filters, where each task type is worked, and picking and packing with their
   transition table, validations and company defaults.
   Batch 14 (sections 3, 6 and a notice in 8): **movement ledger** with new filters (owner, reason, direction, source and destination warehouse, "manual
   only"), **summary** of inbound and outbound, **detail of a movement** with its source document and Puerto Rico local days;
   **Increase/Decrease adjustments** with reasons per direction and transfers with lot and serial; **automatic reconciliation** in the background and
   ledger ↔ balance **discrepancies** (statuses Pending, Resolved, Discarded and Self-corrected, their transitions, what each one blocks and the
   messages); **cycle count** with statuses Pending, Counted, Matched and Variance, confirmation in a single step, list with total,
   assignment and **"Count what changed"** (window, cap of 200 bins and its messages). The "Inventory adjustments" screen (purchase
   shortages) left the menu: shortages are resolved in the purchase order record.
   Batch 16 (sections 1.4, 4.1, 5, 7 and 9): **warehouse receiving mode** (with putaway or direct to bin) and **default receiving bin**; **direct-to-bin receipt**
   (destination bin per line, suggestions with capacity, "Use suggested bins", quarantine on returns, capacity that only warns, Completed → Put away at the same moment, no tasks) with its
   validations, statuses and what it blocks; pending putaway in direct warehouses; the Picking bin selector (only where there is stock); the cross-dock that does not apply to
   direct receipts already confirmed; and the number format (thousands comma and `$`).
   2026-10-11 (section 7b): **Manual issue** `DMA-#####` — inventory exit without delivery with mandatory reason (editable list
   “Manual issue reason”) and free note, new permission `warehouse.issue`, viewable record, `kind` filter of the pick batch
   list, movement ledger `DMA-… · reason`, and its 400/403/404/409/422 messages.
   2026-10-11 (section 7c): **Default reason of the manual issue** — tenant setting (`defaultManualIssueReason`,
   `admin.tenant`) that the app and the web preselect (`isDefault` in `/manual-issues/reasons`); the server still requires a reason; a
   disabled default is ignored.
7. [07 — Day pulse and Recent activity (Batch 7A: Warehouse)](07-pulso-y-actividad.md): panel of recent
   Warehouse events (list of 23 events, mandatory/optional, 24h/48h/today window, filter by module),
   "below minimum" filter of products, and new Warehouse indicators and chart in the Day pulse (Units
   received, Counts with variance, Inventory movements by type). Operations (7B) and Accounting (7C)
   add their own tab later. Batch F8a (section 3): **Day pulse: panels, permissions and order** —
   `pulse.*` permissions per panel, panel registry, which indicators and charts each user sees (readable source and
   module turned on) and order/hidden at two levels (company and user). Batch 14 (section 4): the **Needs your attention** panel
   (`pulse.attention`, the 5 oldest discrepancies, "All in order") and the **Open counts** figure with the real total.
   Batch 15 (section 5): the **Warehouse today** band (received, out and counts with variance of today and of the last 7 days in Puerto
   Rico time, below minimum as of now; `GET /inventory/pulse/days`, panel `WAREHOUSE_DAY`), the **fixed rows**, the indicators in **one row per module**,
   the charts **always drawn** (with "Others" past the 8th group), the **company's 2 warehouse charts** (editable by
   `analytics.manage`, they do not come back if deleted), the "Pending discrepancies" indicator and **Puerto Rico days** in all ranges.
8. [8A — Warehouse app backend](08-aparatos-y-sincronizacion.md): idempotency of writes
   (`Idempotency-Key`), trusted devices (creation with enrollment code, anonymous enroll and heartbeat,
   deactivate/reactivate), PIN per user and login per device (lockout by attempts), sync by
   difference (products, bins, purchase orders, arrival notices, tasks, categories), search by
   barcode, and the atomic operations of the device queue (receipt, picking and packing, count in
   batch and blind). It is only the backend: the installable app is not built in this batch.
9. [9 — Warehouse app](09-app-almacen.md): the installable app itself (`app-almacen/`, Android/Expo) — enroll the
   device and sign in with PIN, how it works offline (the device's own documents vs. shared resources, one
   document at a time), Home, Receive, Putaway, Dispatch (own inventory or a 3PL client's; “Complete dispatch” without packing or “Pack”), Count (blind according to
   permission), Lookup (with cache to answer offline) and Sync (pending, with error, retry).
   Batch 16: Receive in a **direct-to-bin** warehouse ("Scan the destination bin" step, offline validation, block for two destinations of the same product), download of the warehouse
   bins and heartbeat on each sync pass.
   Batch A3: Zebra scanner without keyboard ("scan = OK" on all screens, ⌨ button, "Scanner: …" indicator in
   Sync), margin for the device bar, Home in two columns, bigger letters, tapping a product in the
   count, and the company formats (Region and formats) stored on the device.
   Batch A5: counting by product requires at least one typed quantity (0 is valid); in Dispatch the quantity goes before scanning the
   bin (without quantity, the reading does not add the line and warns).
   Batch A6: Sync explains the count capture rejected because the supervisor already corrected a line (409) and offers
   "Refresh the count".
   Batch A7: if the batch brings corrected lines, the free ones are saved and Sync shows a notice with the unsaved ones (the 409 remains
   only when all are corrected).
   Batch A9: small Zebra screen — the PIN screen scrolls (and compacts the keypad if the device is short), the field being typed
   stays above the on-screen keyboard on all screens with fields, and the count calculator has a clear icon, the title on one
   line and a ← arrow to go back (previously the "Direct quantity" link).
10. [10 — Legacy data migration](10-migracion-de-datos.md): command-line command to provision
    a company from QuickBooks Desktop and the legacy WMS MSWM, simulation (`--dry-run`), refresh of masters without
    touching the opening balance (`--update`) and recreating the blank database to repeat the migration (`db-reset`). Batch 12
    (section 4): **estimated capacity of the bins** of Advance Depot from the history of the previous WMS (rule, report
    `-cupos.csv` and behavior of `--update`).
    The QuickBooks brand (`Brand` column) becomes the product's make (section 1).
    Batch 30 (Rentals RM, section 6): **tracking when migrating** — option `products.trackingFromColumns` (QuickBooks `Serial`/`Lot`
    checkboxes, off by default), a product with opening balance is created without tracking and remains as "Candidate for Convert to serial",
    an existing one never changes; evaluation of the Depot serials in `docs/migracion/depot-series-y-rentas.md`.
11. [11 — Rentals (Warehouse submodule)](11-rentas.md): Batch 27 (Rentals R1) — rentals of **own units with serial number** to a
    client location: creation in Draft (REN-#####, contract, estimated transport, fixed or time-based rate per unit, as data only),
    editing, units by serial, **schedule** (reserves the serials), **dispatch** (transfer to the **EN-RENTA** bin, serial "On rent":
    on hand the same, available 0), **extend** the pickup date with log and new rate (`rental.extend`), **cancel** (only before
    dispatch), "about to expire" and "overdue", statuses and transitions, permissions (`rental.extend`, `rental.return`) and the "Rentals" module that
    depends on Inventory and traceability.
    Batch 28 (Rentals R2, sections 5, 6 and 7): **rental return** DRN-##### (`rental.return`; total or partial, at term or early,
    with reason, condition, destination bin —also another warehouse— and whether it goes through processing), **configurable processing** of the returned unit
    (Pending → Inspection → Cleaning → Testing → Ready, with Repair and Awaiting parts; `rental.maintenance`; "Ready" releases the unit
    and "Scrap" takes it out of inventory with reason Damage and also requires `inventory.adjust`) and the units on rent in the **cycle
    count** (D7: EN-RENTA is not counted, 422; a serial on rent in a count, 409).
    Batch 29 (Rentals R3, section 10): **reports, indicators and notices** — Analytics sources Rentals, Rental returns and Returned
    equipment processing (fields, dates and relations), system views "Units on rent by client", "Rentals about to expire (7 days)",
    "Overdue rentals", "Rental returns by reason" and "Units in processing", indicators "Rentals about to expire (7 days)" and "Overdue
    rentals", chart "Rental returns by reason" and the notice of overdue or about-to-expire rentals in "Needs your attention"; all with
    `rental.view` and hidden with the Rentals module turned off.
12. [12 — Printers and labels](12-impresoras-y-labels.md): Batch 43 (F-G1, 2026-10-12) — **company printers** (profile: fixed use labels or
    documents, size 4×6/4×4/4×2 or letter, connection **direct with QZ Tray** or browser PDF, name in the system, rotate 90°,
    default per use), **label templates** (the 9 elements of the builder with position and size; the barcode is the
    **number of each package**, one label per package), print options of the **station**, tenant setting **“Print labels
    directly to the printer”**, client flag **“Print the invoice automatically”** (only the preference), permission
    `printers.manage`, 400/404/409 messages and, for support, **QZ Tray and the Teikem certificate** (generate, configure `Printing:Qz` and
    install `override.crt`). The server never connects to a printer.
    Batch 44 (F-G2, section 10): **order labels** (one per piece with the package number of its line; cancelled ones and pieces without number
    flagged; no cap), **waybill summary**, **packing list** and **shortage report** in letter size, and **scanning and searching by package
    number** (fourth precedence, its own message if repeated; new index).

## Screens manual (web frontend)

Chapters written for the end user and support about the real screens of the web frontend (`web-app/`), with
screenshots. Format and decisions of each frontend batch in `docs/frontend/loteFN-decisiones.md`.

1. [F1 — Access, menu, Day pulse and My account](frontend/f1-nucleo-y-mi-cuenta.md): signing in, two-step
   verification, choosing a company, menu and header (groups by permissions and modules, language switch), Day pulse
   (indicators, charts, my date range), My account (profile, password, two-step verification, active
   sessions), reauthentication (AAL2), the "No permission" and "Module turned off" screens.
2. [F6 — Warehouse and inventory (minimum) + order lookup](frontend/f6-almacen-e-inventario.md): "Warehouse" panel in
   Day pulse, warehouses (zones, bins and docks), products and categories, inventory (balances, movement ledger, adjustments,
   transfers, genealogy, serial trail and reconciliation), receiving (receipts and arrival notices), warehouse
   tasks, cycle count, picking & packing, suppliers, purchase orders, dock appointments, cross-dock
   (plans) and read-only lookup of transport orders.
   Batch 11: common footer on every table (range, "Rows per page" and **Export** to Excel, CSV and PDF), the
   Warehouses list as master-detail with "Zones in this warehouse", record with filterable Zones and Bins (capacity and
   occupancy) and the **Locations** screen (occupancy boxes per zone). Screenshots pending.
   Batch 12: the screen is now called **Bins** (click the row to edit, **Set capacity** button), **Products &
   inventory** (clickable indicators, filters, make and model, "Add adjustment" block with a mandatory note and PDF reports
   of inventory and of adjustments), **Suppliers** (before Purchases, filters, phone with mask, deactivation with an icon) and
   **Purchases** (multiple filters, creation with mandatory warehouse and line rules). Screenshots pending.
   Batch 13: **Receiving** on a single screen (list of receipts, detail with line capture in the table, header edited in a
   modal, tabs Receipts, Arrival notices and Pending putaway), warehouse task actions as icons (there is no longer a single
   queue), **Picking & packing** in two panels with a draggable bar (capture of several lines, Pack and Delete on the row,
   detail in a modal) and the adjustments of 2026-09-30 (supplier and warehouse of the order in Draft, mandatory note on every manual
   adjustment, "Total units" and defaults when packing). New screenshots of those screens; the Arrival notices one is pending.
   Batch 14: **Transfers & adjustments** (Adjustments and Transfers tabs, Increase/Decrease modal, Adjustment report), **Movement ledger**
   (shared filters, summary, movement detail and Reconciliation with mismatches) and **Cycle count** in two panels (scanner, quantity
   on the row, Confirm count & adjust and Count what changed); "Inventory adjustments" left the menu. Screenshots `l14-*`.
   Batch 16: **receiving mode** in the warehouse record (Data → Receiving) and in the list, **receipt direct to bin** (Destination bin column, "Suggested", capacity notice, "Use
   suggested bins"), notice in Pending putaway, Picking Bin selector and, in "All tables", **header and filters of the exports**, dates as dates and number
   format. Screenshots `l16-*`.
3. [F7A — Pulse: Warehouse panel with filter and Recent activity](frontend/f7a-pulso-almacen-y-actividad.md): warehouse
   filter and category or product filter in the "Warehouse" panel of the Pulse (with links to Inventory already filtered), and new
   "Recent activity" panel (tabs per module, time window, "Mandatory only" switch, free-text search
   and links to the record of each event). Batch 14: **Needs your attention** panel (pending mismatches, Review, View all, "All in
   order") and **Open counts** with the real total. Batch 15: the **Warehouse today** band (cards, small bars, tooltip, click to the detail, shared
   warehouse), the **pinned rows**, indicators per module, charts always drawn and "Company" charts. Screenshots `l15-*`.
4. [F8a — Full menu, branding, Pulse by panels, System and Analytics](frontend/f8a-menu-sistema-analisis-y-marca.md):
   the 7 menu groups of the mockup and the "pending" screen, the Teikem branding, command palette, Day pulse
   organized in two levels ("Organize my Pulse" / "Organize the company's"), Indicators and Charts, Roles &
   users (with the PIN of the warehouse devices and the temporary password), Mobile devices (auto-generated code),
   Value lists (adjust/restore) and My account → App PIN.
5. [F9 — Tenant settings and region and formats](frontend/f9-ajustes-de-la-compania.md): how dates, times,
   numbers, money and phones look with the company's region (not with the language), and the System → Tenant
   settings screen with its tabs General, Region & formats (preview, Custom, Restore), Calendar (working
   days and holidays), Modules (dependencies and reauthentication), Operations (default values, status pipeline,
   receiving per warehouse) and Branding (themes, own colors and validations). Screenshots `f9-*`.
6. [F10 — Security & audit](frontend/f10-seguridad-y-auditoria.md): the System → Security & audit screen with its
   tabs Activity (change log and security events in one table: type Change/Event/Alert, date, search box,
   sort by column and Export CSV of what is filtered) and Sessions & MFA (active sessions of the whole company, revoke one or the
   others, and the policy: mandatory MFA, reauthentication window and session duration). Screenshots `f10-*`.
7. [F11 — Company branding: logos and colors validated by the server](frontend/f11-marca-por-compania.md): the Branding tab of
   Tenant settings connected to the server: upload, replace and remove the four logos, where they are shown (sidebar per
   theme, collapsed), the 400/413/415 errors next to each slot, the server error when saving colors and the 409 of the repeated
   holiday in Calendar. Screenshots `f11-*`.
8. [F12 — Cycle count by product: "To review", correction and preview](frontend/f12-conteo-por-producto.md): the
   To review tab (state Matches / With difference / With errors / Lines missing, filters, "Close the ones that match" with its summary and
   reasons), the detail with only the failing lines, the evidence *Counted X · Corrected to Y* and the correction on the row, the
   preview of the effect before confirming and the bins pending review. Screenshots `f12-*`.
9. [F13 — New "By product" count from the web](frontend/f13-nuevo-conteo-por-producto.md): the two tabs of the "New
   count" modal (By bins / By product), how to create the count of a product on a single screen, the 400 of a product with no
   stock next to the selector and where the user goes after creating it. Screenshots `f13-*`.
10. [F14 — Barcodes for the count](frontend/f14-codigos-de-barras.md): the **Product barcodes** reports
   (Products & inventory) and **Bin barcodes** (Bins and the warehouse record): one Code 128 barcode per element with the SKU or the
   exact code, in a grid of 3/2/1 columns, grouped by category or by the first number of the code, with what the screen
   has filtered; their notices ("does not fit", omitted, truncated) and how to print them so the scanner reads them. Screenshots `f14-*` and a sample PDF.
11. [F15 — Products by bin](frontend/f15-productos-por-posicion.md): in **Bins**, the **Aisle** filter, the checkboxes to select
   bins and the **Products by bin** button: PDF with one bin per page and the barcode of each product (up to 10 per
   page, adaptable size, "Sheet 2 of 2", "No products") to scan from paper on a high rack; the table export brings everything
   filtered; messages and frequent cases. Replaces the bin sheets. Screenshots `f15-pdf-*`.
12b. [Product labels](frontend/f17b-etiquetas-de-producto.md): in **Products & inventory**, the **Product labels** button: one adhesive label per product (barcode of the SKU, SKU in large print and name) in 4 × 2, 4 × 4 or 4 × 6 inches, just the label.
12. [F16 — Bin labels](frontend/f16-etiquetas-de-posicion.md): in **Bins**, the **Location labels** button: one
   label per bin (Code 128 barcode as large as possible, the code in large print and the location data) for
   label printers of **4 × 2, 4 × 4 or 4 × 6 inches** (one PDF page of the exact size per label, Automatic orientation
   or Rotate 90°), from the current filter or the selected ones; stateless (reprint whenever you want); difference from Products by bin;
   messages and frequent cases. Screenshots `f16-*`.
13. [F17 — Rentals (web) and "Convert to serial"](frontend/f17-rentas.md): Warehouse → **Rentals**: the list (filters by status, client,
   "due in N days", "overdue only" and search box; calculated expiration; Export), the record (data, units by serial with their rate and
   state, extensions, history) with Schedule, Dispatch, Extend and Cancel, creation with the **serial unit selector** and the
   rate per unit, the notice of overdue or about to expire rentals in "Needs your attention", and the **Convert to serial** button of the product
   record (capture by bin and net-zero confirmation). Screenshots `f17-*`.
14. [F18 — Rental returns, equipment processing and rental reports](frontend/f18-devoluciones-y-proceso-de-rentas.md): **Register
   return** from the record of an On rent rental (reason, date, destination, partial return, condition and processing per unit), Warehouse →
   **Rental returns** (list with filters and record, rental ↔ return links), Warehouse → **Equipment processing** (queue with Advance,
   Complete and Scrap with strong confirmation), the summary cards of the rentals list and **Rental reports** (views,
   indicators and chart in Analytics). Screenshots `f18-*`.
15. [F-A1 — Clients: list, creation and record](frontend/fa1-clientes.md): Lists → **Clients**: master list with search box and “Show inactive”, creation with an optional initial
   contract (duplicate code 409) and the record with profile (409 of `rowVersion`), phones and e-mails, contact persons, numbering with a live example,
   custom fields, contracts, status and deactivation/reactivation; permissions of each action. Screenshots `fa1-*`.
16. [F-A2 — Client contracts](frontend/fa2-contratos.md): in the client record, the **Contracts** section with tabs **Contract** (data, status with history, the 5 checks of the
   billing model, dispatch and COD fee, contract selector and **New contract**), **Rates** (per service and extra piece by tiers, new version and closure, history),
   **SLA** (per service type, draft and save) and **Special services** (new types, rate, new version, closure); `contracts.*` permissions and all the server messages.
   Screenshots `fa2-*`.
17. [F19 — Manual issue on the web](frontend/f19-despacho-manual.md): in **Picking & packing**, the **Manual issue (no delivery)** switch of the
   Picking panel (mandatory reason, note, lines like a pick, `Idempotency-Key`, 409 for stock), the **Type** column and filter, the
   DMA record (reason, note, movement ledger, no Pack) and **Delete** with reversal (`warehouse.issue`). Screenshots `f19-*`.
18. [F-B — Consignees](frontend/fb-consignatarios.md): Lists → **Consignees**: list and record, creation and edit, phone, deactivation and reactivation, and **Import** consignees from a CSV file with templates
   (row-by-row preview, errors and warnings, confirm or discard); `locations.*` permissions.
F-B2. [F-B2 — Import consignees: Excel, “(Do not use)”, joined columns, default client and contact person](frontend/fb2-importacion-consignatarios.md): **CSV or Excel (.xlsx)** file with **Workbook sheet**,
   ZIP with its zeros, Windows CSV with ñ, **(Do not use)**, one column to two fields, **Fields with several columns** (order and separator, with result), **Default client** that is
   saved in the template (the row with its own client wins) and **Remember this client in the template**, phones in mixed formats and **Contact person** in the record. Screenshots `fb2-*`.
F-B3. [F-B3 — Import consignees: town, state and ZIP with the postal code list, and mapping variants](frontend/fb3-variantes-y-codigos-postales.md): town and state taken
F-B4. [F-B4 — Consignees in the client record and Export](frontend/fb4-consignatarios-en-la-ficha-del-cliente.md): **collapsible** panels with a summary in the client record and a **Consignees** panel at the end (paginated table of 10 rows per page, search box, **Export**, **Import** and **New consignee** without leaving the record, **View all**) and **Export**, **Import** and **New consignee** together in the header of the Consignees screen (Export downloads what the list has filtered).
F-B5. [F-B5 — Addresses, Tax profile and panels with summary](frontend/fb5-direcciones-y-perfil-fiscal.md): **Addresses** panel of the client record (physical, postal, pickup points and **Picked up at**), “Client profile” becomes **Tax profile**, **Print labels automatically** switch and the summary of each panel when it is closed.
   from the ZIP, delivery ZIP assigned by the town, **Postal code adjusted** (PO box or military ZIP → delivery ZIP of the same city), **from list** mark, rejected rows
   with the exact message; **mapping variants** with **Used when** (equals, contains, is not empty, is empty), **Variant** column in the preview; town selector of the
   creation form with delivery ZIPs only; USPS and GeoNames sources (CC BY 4.0). Screenshots `fb3-*`.
19. [F-C1 — Orders: list and creation](frontend/fc1-ordenes-alta.md): Operations → **Orders**: the list with its filters, **New order** (save as draft or confirm, exceeded credit, repeated invoice,
   special delivery) and deleting a draft; `orders.*` permissions and who sees the prices (`orders.view_price`).
20. [F-C2 — Orders: the order file](frontend/fc2-ordenes-expediente.md): when opening an order: **Detail** edited right there, **Confirm** (with exceeded credit), **Re-quote**,
   **Cancel the shipment**, **Delete the shipment**, lateral statuses (On hold, Partial, Failed) and their return, **Assign driver** to a special delivery, **Quote** (preview), COD, custom
   fields and contacts. Screenshots `fc2-*`.
21. [F-C3 — Orders: quick entry and import from CSV](frontend/fc3-ordenes-rapida-importacion.md): the **Quick / Detailed** switch of **New order**, the capture grid
   (Enter saves one order per row) and **Import**: templates, file preview, checkboxes **Confirm the orders when importing**, **Authorize exceeded credit** and **Accept repeated invoices**,
   **Discard file** and result. Screenshots `fc3-*`.
22. [F-D1 — Drivers & rates](frontend/fd1-choferes.md): Lists → **Drivers & rates**: list and profile, **New driver**, **Active** / **Availability** / **Delete driver**, licenses and certifications with their
   expiration chip, rates per delivery, attempt and trip (with validity, **Close rate** and history), pay formula with preview, **Paid trips**, devices and contacts; permissions
   `fleet.*` and `driverpay.*`. Screenshots `fd1-*`.
23. [F-D2 — Fleet & maintenance](frontend/fd2-flota.md): Lists → **Fleet & maintenance**: vehicles (list and file, **New vehicle**, **Active** / **Availability** / **Retire permanently**, documents with their expiration chip), **Expiring documents**, **Dispatch zones** and **Availability** for dispatch with its reasons. Screenshots `fd2-*`.
24. [F-D3 — Maintenance, work orders and fuel](frontend/fd3-mantenimiento-combustible.md): tabs **Maintenance** (state of each program per vehicle: Up to date, Due soon, Overdue, No history; creation, edit, **Deactivate** and **Reactivate** programs), **Work orders** (list, **New work order**, file with tasks and costs, **Start**, **Close order** with odometer and **Cancel order**) and **Fuel** (log with Km/L and Cost/km, **Register fill**, summary per vehicle, consistent odometer). Screenshots `fd3-*`.
25. [F-E1 — Scan station (departure)](frontend/fe1-escaneo.md): Operations → **Scan station**: scan with a scanner or keyboard (shipment, packaging or invoice number), large result with the server message and what to do (**Assigned**, **Already there**, **Not found**, **Not eligible**, **Unassigned**), **Route day**, voice Yes/No/Already, “N of M” meter, **Scanned in this session**, **Pending to scan** with filters Client, Zone, Postal code and Town, and order window. Screenshots `fe1-*`.
26. [F-E2a and F-E2b — Dispatch room (with route map, drag and manual pin)](frontend/fe2a-despacho.md): Operations → **Dispatch room**: **Plan the day**, **New route**, **Reassign by zone**, **Unassigned shipments** with filters and **Add to route**, **Active route** with driver and vehicle (availability and server errors), warnings and blockers, ▲ ▼ and remove, **Optimize** with “Did not fit”, **Routes to dispatch** (blockers, warnings and batch), **Start route**, **Close route**, **Delete route**, **Drivers** and **Today’s routes**; F-E2b: schematic **route map** (exact, approximate and manual pins, no location), **reorder by dragging** the handle, and manual **Set pin** with its errors. Screenshots `fe2a-*` and `fe2b-*`.
27. [F-E3 — Route monitor](frontend/fe3-monitoreo.md): Operations → **Route monitor** (read-only): **LIVE** / **No driver signal** badge, the “current” of totals (**Active routes**, **Stops left**, **Average progress**, **Next to complete**, **No ping**, **Over the stop limit**), the **Active routes** list with search, zone and **Include completed**, the chosen route with its map, **Speed**, **Heading**, last ping and **Route stops**, refresh every 30 s with **Refresh** and **Following live**, **Open in Dispatch room**, and what will come with the driver app.
28. [F-F1 — Custom fields](frontend/ff1-campos-personalizados.md): Analytics → **Custom fields**: entities with a counter, table with **Include inactive**, **New field** (the 8 types, key, es/en labels, validation by type, default value, Required, Unique, Show in lists, Order), **Value list** with **New list…**, **Edit**, **Deactivate** and **Reactivate**, and the server messages. Screenshots `ff1-*`.
29. [F-F2 — Views & reports](frontend/ff2-vistas.md): Analytics → **Views & reports**: gallery with one panel per data source (**Default** reports and users' reports, **+** for **New view**), result with date range, search, sort, totals and **Export**, builder (name, combined sources, columns with custom fields, filters with 16 operators, group by several fields with aggregates and totals, sort, visibility and people it is shared with, preview with real data), **Edit**, **Delete** and the server messages. The “Batch F-F2” section of the FAQ collects every message.
F-A3. [F-A3 — Portal users and client quoter](frontend/fa3-portal-cotizador.md): in the client record, the **Portal users** panel (**Invite user**, **Resend invitation**, **Suspend**,
   **Reactivate**, **Deactivate** and **Invite again**, with statuses Invited, Active, Suspended and Disabled; `portalusers.manage` permission and Client portal module) and the **Quoter** tab of
   Contracts (service, package and pieces lines, COD amount and date; breakdown with base rate, extra pieces, dispatch, COD and total, and where each amount came from; `contracts.read`). All the
   server messages with what to do. Screenshots `fa3-*`.
F-G2. [F-G2 — Labels and documents](frontend/fg2-labels-y-documentos.md): in **Orders** the **Select** column, **Print label** (selected), **All labels**
   (everything filtered), **Waybill summary** and **Packing list**; **Print label** in the record; **Print label on save** in the quick and detailed entry (one label per piece with the
   number of its package); in **Scan** the **Missing report** and scanning the package number. Screenshots `fg2-*`.
F-G1. [F-G1 — Printers & labels](frontend/fg1-impresoras.md): System → **Printers & labels** (`printers.manage`): **This computer** (QZ Tray connected or not
   detected, **Detect printers on this computer**, **Install QZ Tray**), **This station — labels / documents** (printer, printer on this computer,
   template or Letter format, **Print on save**, **Confirm first**, **Test** with view and **Download PDF**, **Save** in this browser), **Build the label**
   (9 elements, position, size, as barcode, live preview, new/duplicate/default/deactivate templates, **Update label construction**),
   **Print labels directly to the printer** of the company and **Company printers** (creation and edit, default, deactivate and reactivate). Screenshots `fg1-*`.

13. [13 — Scheduled jobs](13-trabajos-programados.md): Batch 50 (P2.2, 2026-10-14) — **automatic tasks per company** (own service, no Hangfire): switch, schedule (every so often from 1 minute to 7 days, or every day at a given time), **Run now**, history and retries (1, 2 and 5 minutes); three factory jobs (history cleanup, daily inventory review and a test one), `jobs.manage` permission, 400/404/409 messages and data for support.

14. [14 — Notices (notifications)](14-avisos.md): Batch 51 (P2.1, 2026-10-14) — **notification engine**: rules per company (event + EMAIL/SMS/APP/PUSH channels with es/en template and `{variables}` + recipients by role, user, e-mail or phone), queue written in the same transaction as the operation and processed with retries (1, 5 and 30 minutes), idempotent record of each delivery, user inbox with muting of optional events, SMS and push channels “not configured” (no provider), test event, `notifications.manage` permission, 400/404/409 messages and data for support.

15. [15 — Internal messaging](15-mensajeria.md): Batch 53 (P2.1, 2026-10-15) — **message threads** between people of the company (between two, unique per pair; groups of several people or of a role “as a snapshot”), they are answered and the thread is kept; **live** delivery (SignalR) and, without a session, it stays pending; **unread**, mute, leave a group, **“online” presence**; `messages.use` permission in all existing roles (new roles and client portal roles do not receive it by default), 400/403/404/409 messages and data for support (WebSockets, a single instance).

18. [18 — Sales orders](18-ordenes-de-venta.md): Batches 57 to 62 (2026-10-15) — **parts S1 and S2 (server)**: the sales order (OV-#####) that travels from Sales to Warehouse; draft with sales price (changing it requires `sales.price`) and tax copied on each line; **request = validate and reserve in the same transaction**, shortage requested/available/short, backorder, client credit, cancel releases what was reserved, modify a line and **backorders** (view, fill, close with shortage) and **dispatch against the order** (consumes the reservation, lowers the inventory, linked pick, in parts, order partial/dispatched/cancelled by itself), **delivery** (linked transport order) and **undo a dispatch** (reserves again and reopens the order); `sales.*` permissions, Sales role and SALES_ORDERS module; 400/403/404/409/422 messages and data for support; Batch 65: **dispatch on the device** (list, scan, dispatch guided by bin, Pickup / Delivery / Later, offline queue); Batch 66: **sales notices** (requested, dispatched, cancelled, credit to authorize, backorder that can now be filled) and the `SALES_BACKORDER_SCAN` job; Batch 67: **sales order analytics** (SALES_ORDER and SALES_ORDER_LINE sources, indicators, chart, views and the backorder-to-fill notice in the Pulse).

17. [17 — Taxes (IVU)](17-impuestos.md): Batch 55 (2026-10-15) — full tax mechanism **as configuration**: rates with validity and components (they are not edited: one is closed and another created), company rules (tax-inclusive price, rounding per line or document, default rate, taxable charges), product tax category, client exemptions with certificate and calculation (simulator) with the copy of the rate per line; `tax.manage` permission only for the Company admin and TAX module; 400/403/404/409 messages and data for support.

16. [16 — Send notices](16-enviar-avisos.md): Batch 54 (2026-10-15) — **manual notices**: whoever has `notifications.send` sends a notice to people, to roles or to **all** the people of the company; it reaches the bell (“From Ana”), live if they have Teikem open, and **it is not answered**; history with whom it was sent to and how many people read it; permission only for the Company admin (the administrator assigns it), 400/403 messages and data for support.

L80. [L80 — Credit notes](frontend/l80-notas-de-credito.md): Billing → **Credit notes** and the invoice record: list, **create** (against an invoice with its lines and price adjustment, or free-form), **approve** with reauthentication, **apply** to open invoices with caps and **void** with a reason; credit balance in Collections and the statement; desktop and mobile.

L78. [L78 — Export to accounting](frontend/l78-exportacion.md): on the approved or exported run (`billing.export`): **template** selector, **Configure…** (format, delimiter, date, header and the columns in order with their own header; save, save as new, deactivate), **preview** of the file and **download** that marks the run as exported; desktop and mobile.

L76. [L76 — Collections](frontend/l76-cobros.md): Accounting → **Billing → Collections** and the invoice record: **payments** (register with validation and balance cap, void with a reason and reauthentication), **accounts receivable** per client with aging in five columns and the **statement** with running balance; desktop and mobile.

L73. [L73 — Billing](frontend/l73-facturacion.md): Money → **Billing** (`billing.view`): runs (new, review, approve with reauthentication, cancel), invoices (adjust a line with a reason, void) and adjustments of the billing base and the due date; desktop and mobile.

L63. [L63 — Dispatch against a sales order](frontend/l63-despachar-orden-de-venta.md): in the window of the requested order, **Dispatch** (in parts, up to what was reserved), the “what's next?” question (pickup / delivery / later), the delivery with the packing form (client and consignee of the order) and the dispatches table. Screenshots `l63-*`.

L60. [L60 — Sales orders](frontend/l60-ordenes-de-venta.md): Operations → **Sales orders** (Orders and Backorders tabs): building the draft with price and tax, **Request…** with the inventory and credit check (requested / available / short dialog, reserve what is available and leave the rest on backorder, authorize exceeded credit), requested order (change quantity, cancel releasing what was reserved) and backorders (fill, fill all, close with shortage). Screenshots `l60-*`.

L56. [L56 — Taxes](frontend/l56-impuestos.md): Accounting → **Taxes** (five tabs): rates with validity and components (close and create the new one from the previous one), company rules, product tax category, client exemptions with certificate and calculation **simulator**. Screenshots `l56-*`.

L54. [L54 — Send notices](frontend/l54-enviar-avisos.md): System → **Send notices**: search box of people and roles with **Select all (N)** (only what is filtered), send to everyone with confirmation, history with read counts. Screenshots `l54-*`.

L53. [L53 — Messages](frontend/l53-mensajes.md): chat screen (conversation list, bubbles, type with Enter, people in the group), **New message** (search box by name and e-mail, several people or a role) and **Kemi** (“You got a message from… → View”, “Send a message to…”, “My messages”). Screenshots `l53-*`.

L52. [L52 — Notification rules, bell and Kemi](frontend/l52-avisos-y-kemi.md): System → **Notification rules** (`notifications.manage`): rules with event, channels (Email/SMS/Bell/Push, the unconfigured ones turned off with their explanation) with es/en template and {variables} chips, recipients by role, **user with a name and e-mail search box**, e-mail or phone, **Send test**, **Log** with filters and **Fire a test notice**; the header **bell** (counter, list, mark as read, mute optional ones) and **Kemi** (“You got a notice → View” bubble, options, sleep in the header and wake up). Screenshots `l52-*`.

L50. [L50 — Scheduled jobs](frontend/l50-trabajos-programados.md): System → **Scheduled jobs** (`jobs.manage`): list with schedule, next run and last result, **Run now**, **Change schedule** and **History**. Screenshots `l50-*`.

## Frequently asked questions

See [faq.md](faq.md) for the questions and error messages accumulated from each batch. The "Batch 11" section collects the new Warehouse messages (capacity, occupancy, zone code, list of
cities, export) and the questions about "no capacity configured", exporting tables, cities in uppercase and why the
code and zone of a bin do not change. The "Batch 27" section collects the rental messages (creation, units, rate, schedule, dispatch, extend and cancel) and those of a serial on rent, and the
questions about why on hand does not go down when dispatching, where to see what is rented, what an overdue rental is, whether the rate is charged and whether an extension needs
approval. The "Batch 28" section collects the messages of the rental return, of the processing of the returned unit and of the count with units on rent
(D7), and the questions about why a returned unit is still "Unavailable", returning only a part or to another warehouse, removing steps from the
process and what happens with a written-off unit. The "Batch 29" section collects the 404s of the rental reports (without `rental.view` or with the module
turned off) and the questions about where to see the rentals that expire, why a Scheduled one appears overdue, the company's day and the
`RENTAL_DUE` notice while the screens arrive. The "Batch F17" section collects the messages that the Rentals and "Convert to serial" screens check before sending (the same text as the
server), those of the web itself (source warehouse, serial already chosen or unavailable, cap of 200 units) and questions about the unit
selector, "overdue" and the net-zero conversion. The "Batch F18" section collects the messages of Register return and of the processing queue
(those the web checks before sending, the server ones shown there and the write-off confirmation) and questions about the partial
return, the "Unavailable" unit after returning it and the rental indicators in the Pulse. The "Batch 30" section collects the `products.trackingType` configuration error, the three
tracking warnings of the migration and the questions about whether the Depot serials can be brought over, why a product marked as
serial ended up without tracking and whether it is worth recreating the database for this. The "Batch 12" section collects the messages of make and model, of the adjustment with
a note, of the supplier phone, of the purchase order, of the bulk capacity and of the PDF reports, and the questions about the
inventory report (products with 0, "—" value), the adjustments report (opening balances), the origin of the capacity of the
bins and how to correct it in bulk. The "Batch 13" section collects the messages of the receipt status cycle, of the editable
header, of the blind expected quantity, of the receipt and notice filters, the messages only seen on the Receiving and
Picking & packing screens, and the questions about Discrepancy, what enters the inventory with a difference, how to edit the header, the
60/40 bar, where the putaway queue went and why "Total units" counts only active products. The "Batch 14" section collects the messages of the movement ledger (direction, owner, movement
detail), of the mismatches (action, note, already closed, product total, negative ledger or lower than what is reserved, simultaneous
review), of "Count what changed" (31 days, no movements, pending counts, more than 200 bins), of the count statuses and of the
`pulse.attention` permission; the messages only seen on screen (Increase/Decrease adjustment, count scanner, Confirm button turned off,
automatic review); and the questions about what a mismatch is and why an adjustment does not fix it, what counts as "what changed", why a
count says Difference, where "Inventory adjustments" went and why "Needs your attention" is not shown. The "Batch 15" section collects the new message (`Los días deben estar entre 1 y 14.`), those only seen on screen
(confirmation of deleting a company chart, charts with no data, "…" and "—") and the questions about why the movement ledger adds up differently from the
cards of the band, why "Others" appears, why a "last 7 days" indicator gave a different result (Puerto Rico days), how to remove the
pinned band, whether a deleted company chart comes back, why a day's outbound comes out negative and why the band is not shown. The "Batch 16" section collects the messages of the direct receipt (unknown receiving mode, destination bin missing, receiving
or cross-dock zone, bin that does not exist, id and code at the same time, same product with two destinations, deactivated bin or zone, migration configuration mode), those only seen on screen and in the app
(destination bin missing, exceeds the capacity, suggested, device bins) and the questions about how to put a warehouse in direct mode, what happens with open receipts, whether the capacity can be exceeded, why
the scanner gun does not let you send, why numbers have a comma, why the PDF says "No filters" and how to open the Excel dates. The "Batch F9" section collects the messages of the Tenant
settings screen (holidays, working days, separators, branding colors, maximum stops, phone) and the questions about why
the date and time format changed, how to go back to day/month/year, what happens with the saved phones and why US does not change the time. The "Batch F10" section collects the messages of the company sessions (current
session, session not found, reauthentication, 403 without `admin.users`), those of the policy and the export only seen on screen, and the
questions about Event/Alert, the total that changed when paging, the search, the IP as location, why revoking a session may sign out all of them
and what "Sign out other sessions" does. The "Batch 19" section collects the branding messages (size, JSON, unknown fields, status colors, types,
hexadecimals, nonexistent theme, contrast and hue), those of the logos (slot, file, 413, 415, damaged image and each reason an SVG is rejected) and the 409 of the
repeated holiday, and the questions about when the logos are saved, which variant is used in each theme and why the collapsed bar may still show the Teikem symbol. The "Batch F12" section collects the messages seen on the count review screen (reserved, conflict when confirming,
count already reconciled, bin already confirmed), those only seen on screen and the questions about what "Close the ones that
match" closes, why a correction does not move inventory and the difference between Variance and Adjustment. The "Products by bin" section collects the report messages (more than 200 bins per query, warehouse not found, the modal notices) and the questions about which code each product carries, how to print an aisle, why the paper does not go stale and why Export brings everything filtered. The "Batch F-A2" section collects the messages of the client contracts (data, `rowVersion`, billing model, dispatch and COD, status, SLA, rates per service and extra piece, tiers and special services) and the questions about permissions, `canEdit`, rate history and why a contract cannot be activated. The "Batch F-A1" section collects the Clients messages (duplicate code, name, credit limit, `rowVersion`, pickup point, phones and e-mails, contact persons, numbering patterns and status rules) and the questions about permissions, deactivations, address, SLA and the difference between status and deactivation.
20. [20 — Sales returns](20-devoluciones-de-venta.md): Batch 81 (P6, 2026-10-16) — the **return** (DV-#####) of a dispatched sales order: request (cap of what was dispatched and window of days), authorize, reject, cancel, **receive** in parts with the good and the damaged (RETURN-type receipt, inventory and damage) and **credit** with credit notes; `sales.return` and `sales.return.authorize` permissions.
21. [21 — COD: collection on delivery and reconciliation](21-cod.md): Batch 84 (P8, 2026-10-17) — the COD **collection** (partials; the order goes from To collect to Partial and Collected) and the **reconciliation** by codes (the invoice or order number read from the scanned sheets or typed by hand; preview, typed results, saved runs and disambiguation by client); the **remittance to the client** (Batch 86: gross, commission and net, approval with reauthentication, remit and export with templates); `cod.view`, `cod.collect`, `cod.reconcile` and `cod.remit` permissions.
22. [22 — Driver settlement](22-liquidacion-choferes.md): Batches 93 to 96 (P9, 2026-10-17) — the **delivery attempts** (one row per attempt, with driver, result and failure reason; they come from the order status or are recorded by hand), the source of truth for pay, and the **settlement** (LIQ-#####: deliveries with attempts and trips, frozen formula and amounts, adjustment per line, generate → review → approve with reauthentication; cancel; export with templates of the `DRIVER_SETTLEMENT` scope) and its **screen** (Accounting → Driver settlement); `driverpay.view`, `driverpay.manage`, `driverpay.approve` and `driverpay.export` permissions.
23. [23 — Client notices](23-avisos-al-cliente.md): Batch 98 (P10, 2026-10-17) — summaries to the **client** (not to the consignee): picked up, out for delivery, delivered, COD processed and invoice issued; **the company configures them per client and per channel** (e-mail, portal inbox, SMS), all off by default; one summary per client and route, retries and delivery log; `clients.read` and `clients.update` permissions.
24. [24 — Legal document](24-documento-legal.md): Batch 99 (client portal, 2026-10-17) — **a single document** of privacy policy and terms of use for the whole platform, versioned; it is accepted on the first sign-in (button only when reaching the end of the text) and every time it changes; list of who accepted and who did not in Security & audit; operator data in `Legal:*`.
25. [25 — Secure access](25-acceso-seguro.md): Batch 100 (client portal, 2026-10-17) — **account lockout with a growing wait** (1, 5, 15 and 60 min; no user enumeration; notice by e-mail; cap per IP on login) and **trusted computers** (a new computer is confirmed by e-mail and stays trusted for 30 days without use; they are seen and removed in My account); off by default until e-mail is running.
26. [26 — Client portal: access and isolation](26-portal-acceso.md): Batch 101 (client portal, 2026-10-17) — the portal's own login (separate from staff), trusted computer always, mandatory MFA remembered 30 days per computer, “Do not trust this computer”, anti-bot challenge (Turnstile) after 3 failures, session tied to a client (`cid`) with a second data filter, staff routes closed to the portal, and `/portal/me` and `/portal/orders` reads.
27. [27 — Client app](27-app-cliente.md): Batch 102 (client portal, 2026-10-17) — `app-cliente` (Expo, installable web and mobile): sign in with the remembered “Do not trust” checkbox, confirm the computer, MFA, legal document, home, orders, record with tracking, notices and settings; **Open with fingerprint** on the phone; see also [the screens](frontend/l102-app-cliente.md).
28. [28 — “How is my order doing?” and the progress bar](28-como-va-mi-orden.md): Batch 103 (client portal, 2026-10-17) — the **progress bar** with as many stages as statuses the company's normal path has (in the list, the record and the public page) and the **public page** by waybill number that shows only the progress (no names or addresses); the company turns it on in Settings with its identifier; cap of queries per IP; see also [the screens](frontend/l103-como-va-mi-orden.md).
19. [19 — Billing](19-facturacion.md): Batches 72, 74, 75, 77 (P5) and 79 (P6, 2026-10-16) — **credit notes** (against an invoice with a cap of what was invoiced and what was already credited, free-form for external invoices; draft → approved with AAL2 → applied to invoices; credit balance in the statement), **export to accounting with templates** (CSV or TXT, delimiter, date, header and configurable columns; preview; reproducible; marks the run as exported), **payments** (partial or full, void with a reason), client **statement** with running balance and aging, **accounts receivable** and the overdue invoices job; **freight of the transport orders** according to the contract's charging model (breakdown by concept with its rate, never twice) and, from Batch 72, **billing runs** (generate → review → approve with AAL2 → cancel), invoices of the **product sale** of the sales orders with the copy of the tax, **never twice** (line locking, tested with simultaneous runs), base **on dispatch or on delivery**, manual adjustment with a reason and voiding an invoice; `billing.view` permission and FAC-##### counter.
L82. [L82 — Sales returns](frontend/l82-devoluciones.md): Sales orders → **Returns** and the **Request return** button in the order record: list and filter, **request** (cap of what is returnable, kits and serials blocked, window of days), **authorize/reject/cancel**, **receive** the good and the damaged, **credit** (notes in draft) and the editable window; desktop and mobile.
L85. [L85 — COD: collection and reconciliation with scanned sheets](frontend/l85-cod.md): Accounting → Process deliveries: list of **orders with COD**, record and **collection recording** (partials, check with its number), **Reconcile** by uploading scanned sheets (PDF and images: the browser reads the barcode with the invoice number; the files are not uploaded), codes by hand, **preview**, reconcile and **runs**; desktop and mobile.
L87. [L87 — COD remittances to the client](frontend/l87-remesas.md): Accounting → Process deliveries → **Remittances**: list, **new remittance** (client, period and commission; the reconciled collections that would go in and where the commission comes from are shown), record with **reconcile → approve (reauthentication) → remit**, cancel, **export** with template and the company's **default commission**; desktop and mobile.
L88. [L88 — The two currents of the Pulse](frontend/l88-rios.md): Day pulse → **Packages on the street** (Inbound, In warehouse, To dispatch without a driver, On route, Delivered today) and **COD money** (To collect, Collected, Reconciled, Remittance ready), with 7-day small bars and links to where each card is handled; `pulse.orders` and `pulse.cod` permissions; desktop and mobile.
L89. [L89 — Radar of open shipments and operation notices](frontend/l89-radar.md): Day pulse → **Radar** (how many open orders, groups by town or client with a link to filtered Orders, search by invoice, order, packaging or consignee) and the **orders without a driver** and **COD not reconciled** notices of “Needs your attention”; desktop and mobile.
29. [29 — Messages with the customer service team](29-mensajes-con-el-cliente.md): Batch 104 (client portal, 2026-10-17) — one conversation per person of the client with the customer service team (designated per client or, with no team, whoever has `clientmessages.reply`); the team can write first to a person from the order file; “Messages” tab in the client app and “Client messages” inbox in the web app; cap of 30 per hour.
30. [30 — New order from the client portal](30-nueva-orden-del-cliente.md): Batch 105 (2026-10-17) and **Batch 213 (2026-10-23)** — the client's people with administrator or operator role create shipments from the app with **the same fields as the company's web app** (service, ready and deadline dates, numbers if the client assigns them, consignee with autocomplete and read-only data, package type, COD, notes); **quick** (wide web, all with the keyboard) and **detailed** (web and phone, with ticket); they enter the normal flow directly; the client comes from the session; view-only cannot. Screens with screenshots: [frontend/l213-portal-cliente.md](frontend/l213-portal-cliente.md).
31. [31 — Missing pieces and claims](31-reclamaciones.md): Batch 111 (2026-10-18) — the path of a missing piece (To verify → Lost → Claimed → Resolved), the REC-##### claim with order, pieces, description, declared value and notes, resolving it (paid with amount, rejected or closed) and what happens when a piece shows up at the scan station; `claims.view` and `claims.manage` permissions.
32. [32 — Order return](32-devolucion.md): Batch 112 (2026-10-18) — returning an order that could not be delivered: it becomes “Returned” (final) and the return order is created (pickup and delivery reversed) with the pieces being returned, from the button in the record or by scanning “Return”; `orders.return` permission; count closure with the list of what was not returned.
33. [33 — “Pick up” service type](33-pick-up.md): Batch 114 (2026-10-18) — I pick up and bring it: Submitted → Picked up → In warehouse (final), with no route or driver; “Pick up” and “Billable” flags of the service type (Lists → Adjust); a billable pick up is charged when it reaches In warehouse.
34. [34 — Consignee notices](34-avisos-al-consignatario.md): Batch 116 (2026-10-18) — the client turns the notice on and flags each consignee (warning + log + creation notice); e-mail with a tracking link by token when the order goes On route, on a failed attempt and in the final statuses; permanent opt-out of the consignee.
L111. [L111 — Claims (staff web)](frontend/l111-reclamaciones.md): to verify, mark as lost, create the claim and resolve it; with screenshots.
L112. [L112 and L113 — Return and return after N attempts (staff web)](frontend/l112-devolucion.md): the suggestion in the record, the company setting, returning an order, the case with no address and the return order; with screenshots.
L114. [L114 — “Pick up” service type (staff web)](frontend/l114-pick-up.md): the “Pick up” and “Billable” flags and an order that ends In warehouse with no route; with screenshots.
L115. [L115 — Return fee (staff web)](frontend/l115-cargo-devolucion.md): the three modes in the contract and the fee when returning; with screenshots.
L116. [L116 — Consignee notices (client app)](frontend/l116-avisos-consignatario.md): flagging each consignee with the warning, public tracking by link with token and the opt-out page; with screenshots.
L117. [L117 — Consignee access (client app)](frontend/l117-acceso-consignatario.md): the client gives access to a consignee and reads and answers their messages; the consignee sees their notices, their shipments (read-only), writes to their client and turns notices off; with screenshots.
35. [35 — Consignee access](35-acceso-del-consignatario.md): Batch 117 (2026-10-18) — the client gives a consignee access by invitation to an e-mail (48 h link, single use); the consignee signs in without MFA (password + trusted computer) to their own space: profile (turns off and back on the notices they turned off themselves), inbox of what they were notified about and messages with their client (117b); they only see their own.
L118. [L118 — Record the delivery from the web (staff web)](frontend/l118-registrar-entrega.md): the “Register delivery” button, the dialog with the automatic signature and the proof in the record; with screenshots.
36. [36 — Record the delivery from the web](36-registrar-entrega-web.md): Batch 118 (2026-10-18) — the order becomes Delivered with a proof signed automatically by the user (`orders.deliver` permission); the location is looked up separately with Google, a single 30 s attempt; no photos yet.
L119. [L119 — Client portal: proof, invoices, my notices and consignees (client app)](frontend/l119-portal-facturas-avisos.md): the proof of delivery in the record, the read-only invoices, the notice preferences and the consignee list; with screenshots.
37. [37 — Client portal: invoices, my notices, proof of delivery and consignees](37-portal-facturas-avisos-comprobante.md): Batch 119 (2026-10-18) — the client's invoices in their portal (read-only), notice preferences per person (the company rules; the person turns off), the proof of delivery without the user's name and the consignee list.
L120. [L120 — Client portal: indicators, my profile and my password (client app)](frontend/l120-perfil-indicadores.md): the Home indicators, my profile with my company's data read-only and the password change; with screenshots.
L121. [L121 — Roles & users: Clients and Consignees tabs (staff web)](frontend/l121-usuarios-portal.md): the company's portal people and consignee accesses, with their type and their actions; with screenshots.
L126. [L126 — Process deliveries: the client's invoice (staff web)](frontend/l126-procesar-factura.md): the “What are you uploading?” selector, the preview with the type of each sheet, the mode warning, the run and the order record; with screenshots.
38. [38 — File store: signature, photos and documents](38-almacen-de-archivos.md): Batch 127 (2026-10-18) — files encrypted per company with the reference in the database; first use, the proof of delivery.
L127. [L127 — Proof of delivery: signature and photos (staff web)](frontend/l127-anejos-comprobante.md): thumbnails, add and remove; with screenshots.
L129. [L129 — Zone levels: Island and Metro (staff web)](frontend/l129-nivel-de-zona.md): levels with a default driver, zones with level, record and order filters; with screenshots.
L130. [L130 — Custom fields in the search (staff web)](frontend/l130-campos-en-busqueda.md): filter by the field's type in the lists; with screenshots.
L131. [L131 — Files with several lines per order (staff web)](frontend/l131-lineas-por-orden.md): line rules of the orders template and preview with the lines; with screenshots.
39. [39 — Integrations (folder and manual upload) and their inbox](39-integraciones.md): Batch 132 (2026-10-19) — service account per integration with permissions by family, runs with file hash, inbox with fix/reprocess/discard and system notices.
40. [40 — Exit notices](40-avisos-de-salida.md): Batch 143 (2026-10-20) — the owner of the goods notifies what they want the warehouse to take out; reservation on the spot, pending (backorder) that is reserved only when goods come in, dispatch against the notice and undoing it.
L132. [L132 — Integrations (staff web)](frontend/l132-integraciones.md): list, creation, history, inbox and fix; with screenshots.
L133. [L133 — E-mail integration (staff web)](frontend/l133-correo.md): e-mail connector form; with screenshot.
L134. [L134 — SFTP and FTP integrations (staff web)](frontend/l134-sftp-ftp.md): list and form of the remote connector; with screenshots.
L135. [L135 — API for clients (staff web)](frontend/l135-api-clientes.md): API connector, keys with the secret shown only once and log; with screenshots.
L136. [L136 — Outgoing webhooks and outbound inbox (staff web)](frontend/l136-webhooks.md): webhook management, secret shown only once and undelivered outbound messages; with screenshots.
L137. [L137 — File formats and integration output (staff web)](frontend/l137-formatos.md): Excel/JSON/XML format with mapping by path (137a) and outbound integrations by SFTP/FTP and outgoing API with their inbox (137b); with screenshots.

> **Menu (2026-10-20, Batch 150):** Day pulse goes alone at the top; “Operations” is now **Transport** (Shipments, Scan station, Dispatch room, Route monitor, Claims); **Sales** and **Purchases** are under Accounting; in Warehouse, **Arrivals & departures** gathers the arrival and exit notices (formerly tabs of Receiving). The client portal no longer appears in the staff menu.
41. [41 — Warehouse client portal](41-portal-almacen.md): Batch 153 (2026-10-20) — stock, products, movements and inventory value of the client (read-only); one access per company and client.
42. [42 — Global search (“Search or run”)](42-busqueda-global.md): Batch 167 (2026-10-21) — the palette searches records by number or name (shipments, routes, claims, clients, warehouses, products, documents, invoices, phones), opens only with an exact result and respects permissions and modules.
43. [43 — Purchase and dispatch accounting](43-contabilizacion.md): Batch 181 (2026-10-21) — CMP/DSP runs of the frozen cost of own goods that came in (receipts) and went out (dispatches), generated → accepted → exported with the template; a client's goods are not posted.
L181. [L181 — Purchase and dispatch accounting (staff web)](frontend/l181-contabilizacion.md): the list, “Generate run” with preview, the record, accept, export with the template and cancel; with screenshots.
44. [44 — Master receiving (“Receive together”)](44-recepcion-maestra.md): Batches 182-183 (2026-10-21) — several notices or purchase orders from the same owner and warehouse that arrive together are opened and confirmed together (RCM-#####); each document keeps its receipt.
45. [45 — Mobile app (driver and staff)](45-app-movil.md): Batches 213-217 (2026-10-24) — a single app for drivers and staff; whoever has `mobile.use` or is a driver signs in; the staff home shows “Needs your attention” and the Pulse; “driver only” sees only the driver's part; Orders: search or scan, full record and complete like the driver with `orders.deliver`; Batch 215: today's routes and assigning on the street with `trips.streetassign`; Batch 216: route scan station with the trigger Android device, which works with no signal (up to 5000 uncompleted shipments); Batch 217: Clients, Consignees and Warehouse in lookup mode (read-only, with phones, e-mails and maps as links; no credit, costs or prices).
L183. [L183 — Master receiving (staff web)](frontend/l183-recepcion-maestra.md): the “Masters” tab, “Receive together”, the record with its receipts and “Confirm all”; with screenshots.
