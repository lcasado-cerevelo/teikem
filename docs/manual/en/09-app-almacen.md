# Chapter 9 — Warehouse app (`app-almacen/`)

Installable app (Android, Expo) to operate the warehouse from a gun or tablet: enroll the device, sign in with
a short PIN, and capture Receive, Putaway, Dispatch, Count and Lookup without depending on having a signal all the time.
It covers the two deliveries of Batch 8A-app (first: core + Receive; second: Putaway, Dispatch, Count, Lookup and
Sync). The backend that supports it (devices, PIN, synchronization by difference, barcode,
atomic operations) is documented in [chapter 8A](08-aparatos-y-sincronizacion.md); this chapter covers the
app's real screens.

Decisions of each delivery, with more technical detail: `docs/lote8A-app-decisiones.md`.

Batch 16: **Receive in a "Direct to bin" warehouse** (section 4: "Scan the destination bin" step), download of the warehouse's **bins** and **heartbeat**
on every sync pass (section 9).

Batch A3 (2026-10-03, `docs/mobile/loteA3-decisiones.md`): **Zebra usability improvements** (scanner without keyboard and "scan = OK"
on all screens, margin for the navigation bar, Home in two columns, larger letters, tapping a product in the
count) and **company formats** (numbers, dates, times and time zone from Region & formats, as on the web): sections
1.1 and 1.2. Since this batch, numbers no longer carry a fixed thousands comma: they carry the company's separators (Puerto Rico:
`61,023`; `1,250.5`).

Batch A4 (2026-10-03, `docs/mobile/loteA4-decisiones.md`): **Count by product** in Count (section 7.1): the product is scanned,
the app lists the bins (and lots) where the system says it is, with a space for the quantity (blank = 0), and "Another
bin" for what was found where the system had nothing.

Batch A5 (2026-10-03, `docs/mobile/loteA5-decisiones.md`, owner's decisions 4 and 5): when **counting by product**, Confirm requires at
least one bin with a number written (0 counts; section 7.1, step 5); in **Dispatch**, the quantity goes first: scanning the bin
without a quantity does not add the line and warns (section 6, step 2).

Batch A6 (2026-10-03, `docs/mobile/loteA6-decisiones.md`, pending change 2 of the owner's decisions): in **Sync**, a
count capture that the server rejected because **the supervisor already corrected a line** (409) is explained separately, in large type, with the
affected lines and SKUs, what to do and the **Refresh the count** button (section 9.1).

Batch A8 (2026-10-03, `docs/mobile/loteA8-decisiones.md`): in **Lookup**, scanning a **bin** shows live the list of what the
system says is in it (large SKU, name, lots; quantities only with `warehouse.count`), a complement to the bin's printed
sheet (section 8.1).

---

## 1. How it works without signal

The app keeps a local copy of what it needs to work (products, purchase orders, arrival notices and, since Batch 16, the bins of the
device's warehouse) and updates itself every 60 seconds and on opening (synchronization by difference, chapter 8A §4). Each screen
behaves in one of these two ways, never mixed:

- **Device's own document** (Receive, Dispatch when picking, Count when capturing what was found): it is captured
  without signal and sent to the **outbound queue** when finished. The queue sends each operation in the order it was created,
  with an idempotency key (chapter 8A §1) so that a retry does not repeat it. If an operation fails because the
  server rejects it (invalid data, no longer applies), it stays "with error" to be reviewed in Sync; if it fails for
  lack of signal, it retries by itself on the next pass.
- **Resource shared with the web or other devices** (the Putaway task list, resolving a scanned bin
  to its real id, the consignee when packing a dispatch, claiming a bin to count it, the balances in
  Lookup): it needs signal at that moment, because someone else could be viewing or touching the same thing. These
  calls are direct (they never go through the queue); without signal, they warn and can be retried.

**One document at a time per device.** While there is a receipt, a dispatch or a count in progress, Home blocks the
other actions with a notice (`Finish or cancel the receipt in progress before using this.` / dispatch / count) — the
button of the action in progress resumes it instead of blocking. Putaway and Lookup do not handle any document of their own, so
any of the other three in progress also blocks them. Closing and reopening the app does not lose the
document in progress: it stays saved as is until it is confirmed or cancelled.

### 1.1 The Zebra scanner: scan = OK (Batch A3)

On every screen with a scan field (Receive, Putaway, Dispatch, Count —bin and product— and Lookup), **scanning
is the same as typing the code and tapping "OK"**: the screen moves on by itself, with no "Continue" afterwards. The **quantity** steps
are still manual; in **Dispatch** the quantity is typed **before** scanning the bin (Batch A5, §6). Detail per screen
in the table of `docs/mobile/loteA3-decisiones.md`.

- **No on-screen keyboard**: the scan field no longer opens the keyboard when focused or when scanning. To type a code by
  hand, the small **"⌨"** button next to the field shows the keyboard (tapping it again hides it); then **OK** or Enter. The
  Zebra's physical keyboard types into the field without opening the on-screen one.
- **Scanner profile**: when opening a scan screen, the app configures in DataWedge (the Zebra scanner program) the
  **`TeikemAlmacen`** profile: scanner on, reads delivered to the app ("intent") and **keystroke output off** (if the
  keystroke output were left on, each read would be typed as keys into the focused field and would open the keyboard). The
  state is shown in Sync (§9: "Scanner: …").
- **Notices when scanning**: "not found" appears in a **large red block**; what was added or finished, in a **green block**.
  They stay on screen until the next read or action.
- **Repeated read**: if the device sends the same read twice in less than 400 ms, it is taken only once.
- **Bottom margin**: the app leaves a margin of at least 40 dp at the bottom so that the device's navigation bar (3 buttons or
  gestures) does not cover the last button. The bar is not hidden.

**“⌨” button on every text field (2026-10-05).** Wherever there is a text field (quantities, lot, expiration, codes, search boxes) there is a **⌨** button next to it: the on-screen keyboard **does not appear on its own** (the Zebra
has a physical keyboard and a scanner) and the button shows and hides it. In quantity lists (count by product) there is **a single** button for all rows. The device enrollment (address and code) does not have it: there you always type.

### 1.2 Company formats (Batch A3)

The app's dates, times, numbers and money come from the company's **Region & formats** (web: Tenant settings,
[F9](frontend/f9-ajustes-de-la-compania.md)), as on the web; the device's **language** only decides the texts (for example
"p. m." or "PM").

- **When they are updated**: when signing in with the PIN and on every sync with signal (`GET /api/v1/tenant/settings`; it does not require a
  fine-grained permission). A change made on the web reaches the app on the **next sync** (at most one minute with Home
  open, or when tapping "Sync now").
- **Without signal**: they are saved on the device (in that company's database), so they remain valid without signal and when reopening the app.
  If the device never received them (just enrolled and without signal), it uses **Puerto Rico's**: MM/DD/YYYY, 12 hours, thousands comma
  and decimal point, `$` before, zone `America/Puerto_Rico`.
- **Time zone**: the times on screen (last sync, time of the data saved in Lookup) and what "today" is
  are counted in the company's zone, not in UTC nor in the device's. For example, at 1:30 UTC on October 3, in
  Puerto Rico it is still October 2 at 9:30 p.m.
- **Several companies**: each company on the phone has its own formats.
- **Phones**: today the app neither shows nor asks for phone numbers; when it does, only the digits will be saved and they will be shown with the
  company's mask.

---

### 1.3 Search the list (task 26)

What it does: in **every product or bin field** (Receive, Putaway, Dispatch, Count —including "another bin"— and Lookup) there is, below the field,
the **☰ Search the list** button. Besides scanning or typing the code, it opens a search box: you type anything and the list filters instantly;
tapping a row is the same as scanning its code (it does the same as the scan, with the same validations).

- **Products:** searches by SKU, name or barcode (active products only).
- **Bins:** searches by code, zone, aisle, rack, level or bin (only active bins of the device's warehouse).
- **Lookup** searches both at once (products first).
- It searches what is **already synced on the device**: it works without signal. It shows the first 50 matches; typing more narrows it down.

| Case | Exact message | What to do |
|---|---|---|
| Nothing matches | `No matches on this device. Sync if something is missing.` | Check what you typed or sync (the product or bin may have been created after the last sync) |
| There are more than 50 | `Showing the first 50; type more to narrow it down.` | Type more |

## 2. Enroll the device and sign in

### 2.1 Enroll this device

Who can: nobody yet (anonymous screen, no session) — it needs the single-use **enrollment code** that
an administrator creates on the web (`devices.manage`, chapter 8A §2.1) and that expires in 24 hours.

How it is used: first screen a new device sees. You type the API address (for example
`http://192.168.1.20:5000/`, same network as the server) and the 8-character code (in uppercase). "Register"
saves the server address and calls `POST /api/v1/devices/enroll` (chapter 8A §2.2); if it works, it moves on to
choosing a user.

### Validations

| Field / case | Exact message | Origin |
|---|---|---|
| Server address without `http://` or `https://` | `Enter a valid address, e.g. http://192.168.1.20:5000/` | Local (before calling the API) |
| Enrollment code invalid, expired or of a deactivated device | `The enrollment code is not valid or has expired.` | API, 401 (chapter 8A §2.2) |
| The phone is already enrolled in that code's company (the code is NOT used up) | `This phone is already enrolled in {company} as {code}. Ask the administrator for a code from another company.` | API, 409 |
| No connection to the server | `Something went wrong. Try again.` | Local (`errors.generic`) |

### 2.2 Choose a user and sign in with PIN

Who can: any internal user with a PIN defined, active membership in the device's company and
`inventory.view` (the same requirements as `POST /api/v1/auth/device/users`, chapter 8A §3.3). The already enrolled
device identifies itself on its own (no password).

How it is used: the device's user list is requested automatically when entering this screen; tapping a name leads to the
PIN numeric keypad (4 to 6 digits). "Sign in" calls `POST /api/v1/auth/device/login`. If the PIN is
wrong, the typed PIN is cleared and you have to type it again (it does not go back to the list).

Small screen (Batch A9, 2026-10-06): the PIN screen **scrolls** (swipe up) if it does not fit entirely, so
"Back" and "Sign in" can always be seen in full; on short devices (less than 640 dp, like a 4" Zebra) the
PIN keys shrink a bit (60 dp instead of 72) so that everything fits without scrolling. The PIN is defined on the web
(My account); the app only asks for it to sign in.

### 2.3 One phone in several companies (2026-09-30)

The same phone can work in several companies (for example Advance Depot and Advance Solutions), **once in each
one**:

- **Register another company**: in "Who are you?", the "Register another company" button opens the enrollment screen; you
  type the code given by the other company's administrator (System → Devices). The enrollment is **added**; the
  previous ones remain. "Back" returns without enrolling.
- **When signing in**: with two or more enrollments, "Which company are you working for?" appears first with the name of each company;
  tapping one shows the users with a PIN of that company (its name appears at the top). With a single enrollment it does not ask.
- **Change company**: "Change company" button in "Who are you?". When a user signs out ("Sign out" on Home), the
  next one chooses the company again.
- **Separate data**: each company has its own database on the phone. What was left unsynced in one company
  (counts, movements) stays in its database and is sent the next time someone signs in to that company with signal; you can
  change company even if there are pending items. The server address, the language and the theme belong to the phone (shared).
- **Repeated**: if the code is from a company in which the phone is already enrolled, the notice in the table
  above appears and the code is **not used up**; ask for a code from another company (or use the enrollment that already exists).
- **Deactivated**: if the administrator deactivates the device in one company, only that enrollment is removed from the phone; those
  of the other companies remain.

### Validations

| Field / case | Exact message | Origin |
|---|---|---|
| There are no users with access to this device | `No users have access to this device.` | Local, empty list |
| Wrong PIN | `Wrong PIN.` | API, 401 (chapter 8A §3.3) |
| PIN locked by failed attempts (5 in a row, 15 minutes) | `PIN locked for 15 minutes.` | API, 423 |
| The PIN was assigned by someone who no longer has those permissions | `Your PIN was assigned by someone else who no longer has your permissions; define your own PIN in My account.` | API, 403 |
| The user list could not be loaded (no signal, server error) | The title of the server error, or `Something went wrong. Try again.` | API / local |

The PIN, lockout and permission messages are explained in more detail (why they happen, what to do) in
[chapter 8A §3](08-aparatos-y-sincronizacion.md#3-user-pin-and-device-login) and in the
[Batch 8A FAQ](faq.md#batch-8a--warehouse-app-backend-devices-pin-idempotency-sync-and-atomic-operations).

---

## 3. Home

What it does: menu of 5 actions (Receive, Putaway, Dispatch, Count, Lookup) in **two columns** of tall buttons (icon
on top, name below; the fifth one full width; Batch A3), the sync status
("N pending to send", "All sent", or, in red, that the **last sync failed**: "Could not sync. Tap 'Sync now'." if there is nothing pending, or "N pending · could not sync"; since 2026-10-01 it no longer says "0 with error", because it does not measure rejected items but the result of the last attempt), a "Sync now" button, and "Sign out" (closes
this user's session on the device, without unenrolling it). The screen scrolls if the device is shorter: Sync and
Sign out are below the actions.

Who can: anyone who signed in with a PIN (§2.2). The menu does not filter by fine-grained permission: each screen checks its own
when calling the real API and, if it is missing, the server error is shown as is (for example, without `warehouse.pick` the
Dispatch screen still opens, but "Pack" fails with the permission error).

How it is used: tapping an action navigates to its screen, unless there is another document in progress (§1): then it warns instead
of navigating. Tapping the sync status text leads to the Sync screen (§8).

### 3.1 Working in more than one warehouse (2026-10-07)

A device enrolled in a company can work in **any active warehouse of that company**, not only in the “default” one
set for it from the web (System → Devices). Below the title, Home shows **“Warehouse: <name>”** and, if the company has more than
one, the **“Change”** button. Tapping it shows the list of warehouses (the current one marked and the “device default” indicated); tapping one
makes it active and the app says “You are now working in <name>.” and syncs right away.

- **What changes with the active warehouse:** Receive (and its mode: “with putaway” or “direct to bin”, which belongs to each warehouse),
  Putaway, Dispatch, Count, Lookup, the list search box and the download of bins and stock in the sync. Each warehouse
  keeps its own local copies, so going back to one already used does not download everything again.
- **Who can:** whoever has `inventory.view` (the permission with which the API delivers the warehouse list). Without that permission or if there was never a signal,
  “Change” does not appear and the device stays in its default warehouse.
- **With a receipt, dispatch or count in progress you cannot change:** “Finish or cancel the document in progress before changing warehouse.”
  (app notice, no server call).
- **Without signal:** the warehouse list is saved on the device (it is refreshed every time Home opens with signal), so you can change without network.
- **The choice belongs to that device and that company** (each company enrolled on the phone remembers its own). It is discarded automatically when
  (a) the device's default warehouse is changed from the web, or (b) the chosen warehouse is deactivated: then the device goes back to the default.
- **Server:** nothing changed. The calls already carried the warehouse in the request; the device is not limited to its default warehouse.

---

## 4. Receive

**"Back" button (2026-10-01).** On the start screens of Receive, Dispatch, Count and Lookup (when there is no
document open yet), "Back" goes **at the end of the screen**: in Receive and Dispatch attached to the bottom edge (below the help
text); in Count and Lookup, after everything on screen (including the result of a lookup).

What it does: captures a complete receipt (document or "blind receipt", without an order or notice) and sends it when confirmed in
a single atomic call (`POST /api/v1/receipts` with `confirm: true`, chapter 8A §6 and
[06 §4](06-inventario-y-almacen.md#4-receiving-arrival-notices-asn-and-receipts)). It works without signal end to
end: the call is sent immediately if there is a connection, or queued for when there is one.

Who can: `inventory.view` (for the device in general); receiving against a purchase order also requires the
**PURCHASING** module and `purchasing.receive`, checked by the server when confirming (not before).

How it is used:
0. **Mode of this receipt** (top of the screen): two buttons, "With putaway" and "Direct to bin". The device's warehouse
   mode comes marked; if the other one is tapped, only that receipt opens in that mode (the warehouse's does not change). It is chosen **before** scanning
   the document or tapping "Blind receipt"; once the receipt is open it cannot be changed from the app (it is changed on the web, receipt header).
   Switching to "With putaway" requires the warehouse to have a receiving bin (if not, the server responds 422 when sending).
1. The purchase order or the arrival notice is scanned (it searches what is already synced locally), or "Blind
   receipt" is tapped to receive without a document.
2. Each product is scanned (by barcode or SKU, from what is already synced); depending on how it is tracked
   (`trackingTypeCode`) it asks for a quantity, or a lot (with optional expiration), or serial numbers one by one.
3. "Add" adds the line to what was captured. Each line has its **Remove** button: it asks for confirmation (`Remove this line?` / `{qty} {sku} will be removed from this receipt. The other lines do not change.`) and only with **Remove** in the notice is it deleted; **No** keeps it. Only lines of the receipt **in progress** are removed (before Confirm receipt); a receipt already confirmed and sent is not edited from the app: it is corrected on the web.
4. "Confirm receipt" closes the receipt and returns to Home. In a **with putaway** warehouse the server creates the putaway tasks; in a **direct to bin** one
   each line stays in its destination bin and there are no tasks (see "Receive in a direct to bin warehouse", below).
   "Cancel receipt" (with confirmation) discards everything captured without sending anything.

### Fields and validations

| Field / case | Exact message | Origin |
|---|---|---|
| Scanned code is not of a known order or notice | `No order or ASN matches that code. You can continue with a blind receipt.` | Local (search in local database) |
| Product code does not match any synced one | `There is no product with that code.` | Local |
| Lot-tracked product, without a lot number or without a quantity | "Add" button disabled (`canAddLine`) | Local, no message |
| Expiration (optional) typed with a date that does not exist (Batch A3: it is typed in the company's date order, the field's example shows it —Puerto Rico `MM/DD/YYYY`—; `YYYY-MM-DD` is also accepted) | `The date is not valid. Write it like this: {format}` and "Add" disabled | Local |
| Serial-tracked product, without any serial number captured | "Add" button disabled | Local, no message |
| There is already a receipt in progress on this device (attempt to start another) | `There is already a receipt in progress; it must be confirmed or cancelled before starting another.` | Local (`startLocalReceipt` throws; the screen always resumes the open one, it never gets to show it) |
| Confirm without signal | It is saved anyway, sent only when there is a connection (`errors.network`) | Outbound queue |
| The server rejects the receipt when confirming (invalid data, order already closed, etc.) | The exact message the server returns, see [06 §4](06-inventario-y-almacen.md#4-receiving-arrival-notices-asn-and-receipts) | API, via queue |

### Damaged units when receiving (2026-10-08)

What it does: on the **received quantity** screen there is a checkbox **“Some units arrived damaged”** (it does not appear on serial products). When checked, the **Damaged quantity** (with focus and the number selected) and the **Reason** (list: *Came that way*, *Accident in transit*, *Warehouse accident*, *Other*; with *Other* a box appears to write the reason) appear **side by side**. The damaged ones are **within** what was received (I received 100, 10 damaged). It works **without signal**: the damage travels inside the receipt line in the outbound queue and the server creates the `DAN-#####` report when the receipt is confirmed (see [06 §4b](06-inventario-y-almacen.md#4b-damage-what-arrived-damaged-or-was-damaged-in-the-warehouse-2026-10-08)).

Who can: whoever receives (the server checks `warehouse.receive` when confirming).

How it is used:
1. Capture the quantity as usual and check **Some units arrived damaged**; type how many and choose the reason (if it is *Other*, write it). **Add / Next** stays off while something is missing.
2. In a **direct receipt**, first scan the bin for the good units (as usual). In a **receipt with putaway** it goes straight to the next step.
3. **Damaged units** step — “{n} damaged of {SKU}. Where do they go?”: at the top the hint **Suggested: {bin}** (the **quarantine** one if the warehouse has one; if not, the bin you chose for the good units —in direct— or the receiving one —with putaway—). Tap it to fill the field, or scan/type another one, or use the bin list button; **OK** adds the line. Any active bin of the warehouse is valid.
4. **Release** (asks for confirmation: `Release the damaged units?` / `The {n} damaged units leave right away: they do not go to quarantine.`) first asks **Where does it go?** (`Thrown away`, `Returned to supplier`, `Donated`, `Sold as salvage`; help `Choose where the goods that leave go.`) and then confirms; it leaves the line marked to be released when the receipt is confirmed, with that destination and no bin. **Back** returns to the capture without adding anything.
5. In the line list you see `{n} damaged` or `{n} damaged (release)`.

With a **split by bin** (quantity per bin) the damaged units are assigned to the first lines of the split without exceeding what each one carries (a line cannot carry more damaged than received); if the split does not cover the whole quantity, the remaining damaged units stay in the capture of what is left.

| Field / case | Exact message | Origin |
|---|---|---|
| Checkbox | `Some units arrived damaged` | Local |
| Fields | `Damaged quantity`, `Reason`, `Choose the reason`, `Write the reason` | Local |
| Damaged quantity empty, 0 or greater than received; no reason; *Other* without text | Add/Next button disabled | Local, no message |
| Release button | `Release`, help `They leave inventory when the receipt is confirmed.` | Local |
| Bin for the damaged units | `Scan the bin for the damaged units`, hint `Suggested: {bin}` | Local |
| The bin does not exist / is inactive | `That bin does not exist in this warehouse.` / `That bin is inactive.` | Local |
| No bins downloaded | `This device doesn't have this warehouse's bins yet. Sync with a signal and try again.` (the same notice as for the destination) | Local |
| The server rejects the damage when confirming | The exact server message (06 §4b) | API, via queue |

### Receive in a direct to bin warehouse (Batch 16)

What it does: if the device's warehouse receives **direct to bin** ([06 §1.4](06-inventario-y-almacen.md#14-receiving-mode-and-default-receiving-bin-batch-16)),
each line carries the **bin where the goods are left** and, when confirming, the server posts it there, with no putaway tasks
([06 §4.1](06-inventario-y-almacen.md#41-receipt-direct-to-bin-batch-16)). The device knows the warehouse's mode because the enrollment and the heartbeat bring it
(`defaultWarehouseReceivingMode`). **The receipt keeps the mode the warehouse had when it was started**: if the mode changes in the middle of the capture, that receipt continues as it started. A
device that does not know the mode yet (has not synced since the app was updated) works as before: **with putaway**.

Who can: `inventory.view`; the "Suggested" hint also uses the server's putaway suggestions. The permission to receive (`warehouse.receive`) is checked by the server when confirming.

How it is used (in a direct warehouse):
1. The product is scanned and the quantity, lot or serials are captured, as usual. The button becomes **Next** (instead of "Add").
2. The **Scan the destination bin** step appears, with the product name and the quantity. The bin where the goods are left is scanned (or typed).
3. If there is signal, above the field the hint **Suggested: {bin}** appears (the first one suggested by the server for that product and that quantity). **Nothing is filled in automatically**: you have to scan the
   bin, whether the suggested one or not. Without signal, the hint does not appear and the capture continues the same.
4. The app **validates without signal** the bin against the warehouse's bins it has saved: that it exists, that it is active and that it is not a receiving (`STAGING`) or cross-dock
   (`CROSSDOCK`) bin. **Quarantine does** count. If it is valid, the line is added and shown with **→ {bin}** (for example, `8 SKU-1` with `LOTE-A · → R-02`).
5. **Back** returns to the quantity; **Cancel** discards the line being captured.
6. **Confirm receipt** stays off while any line has no destination bin. Below the button it says: "Closes the receipt and leaves each line in its destination bin, with no putaway tasks."

**Two destinations for the same product (H11).** In a receipt **with an arrival notice or a purchase order**, each line of the document goes into **a single** bin: the server would reject the whole receipt if the
same product arrives with two different bins, and a rejected submission in the queue **can no longer be edited**. That is why the app **blocks it beforehand**: when scanning the second bin (the line is not added and the message appears) and again
when tapping "Confirm receipt". To leave the same product in two bins, the receipt must be **blind** or a **return** (or the surplus part is transferred afterwards).

Fields and validations (direct receipt):

| Field / case | Exact message | Origin |
|---|---|---|
| Label of the destination step field | `Scan the destination bin` | Local |
| Hint with signal | `Suggested: {bin}` | API (`GET /api/v1/warehouse-tasks/putaway-suggestions`, a single suggestion) |
| Line already added | `→ {bin}` | Local |
| The bin does not exist in the device's warehouse | `That bin doesn't exist in this warehouse.` | Local (downloaded bins) |
| The bin is a receiving or cross-dock one | `That bin is for receiving or cross-docking; scan where the goods are stored.` | Local |
| The bin is inactive | `That bin is inactive; scan another one.` | Local |
| The device has no bin of this warehouse saved | `This device doesn't have this warehouse's bins yet. Sync with a signal and try again.` | Local |
| Same product with two destinations in a receipt with a notice or purchase order (title of the notice when confirming: `Can't send it like this`) | `{sku} was already captured with destination {bin}; on a receipt with an ASN or purchase order each line goes to a single bin.` | Local (repeats the server rule) |
| Help below "Confirm receipt" | `Closes the receipt and leaves each line in its destination bin, with no putaway tasks.` | Local |
| The server rejects the receipt when confirming (bin deactivated in the meantime, etc.) | The exact server message: [06 §4.1](06-inventario-y-almacen.md#41-receipt-direct-to-bin-batch-16) | API, via queue |

**Receipts opened before updating the app.** A receipt started with the previous app is sent without a mode and without bins. The server, in a direct warehouse, receives it **"With putaway"** (with putaway tasks): nothing is lost.

### Split across several bins (direct receipt, task 24c)

In the destination bin step of a **direct** receipt there is a **Quantity per bin (optional)** field (it does not appear on serial products).
With the quantity typed, each bin that is scanned is added to the list (`RSV-A-01 · 20`) and the screen says `Split: 40 · 5 not placed yet`;
Each row has its **✕** (`Remove {bin}`) and the quantities are recalculated. **Confirm split** adds **one line per bin**. The full bins fit plus **one more with the remainder** (45 by 20 → 20, 20 and 5): that last one shows the
**fixed alert** `{bin} gets only {qty} (what was left), not {per}.` (and, if the bin has a capacity and it is not enough, below its row `Room for {free}: it will receive {qty}. You can confirm anyway.`: it only warns, with signal) (it stays at the top even if the screen scrolls; it is closed with the ✕); a fourth one is rejected with
`No units left to place: all {total} are split. Confirm the split.` If it is confirmed with fewer bins than fit, what is missing
**stays in the capture**: the bin where it goes is scanned (a single one).
In a receipt with a notice or purchase order, the same product cannot end up in two bins: if the split would cause it, it shows the usual
conflict notice and adds nothing (use a blind or return receipt to split).

### Statuses and frequent cases

The local receipt has no status of its own: it exists ("in progress") or it does not. When confirmed, it becomes a real receipt on the
server with its own status flow (chapter 6). Cancelling before confirming leaves no trace either on the
server or in the history. Closing the app with a receipt in progress and reopening it resumes it exactly where it
was (same captured lines).

---

## 5. Putaway

What it does: lists the open putaway tasks (`PUTAWAY`) of the warehouse (mine first), suggests up to 3 bins
per task, and completes the task when the destination bin is scanned. **It needs signal end to end**: the task
list and its assignment belong to the whole warehouse (shared resource, other devices or the web may be viewing or
taking the same task), so nothing is queued — each action is a direct call.

Who can: `inventory.view` (list, see suggestions, start and complete a task); assigning or cancelling a task
manually (not available on this screen) requires `warehouse.manage`.

How it is used: tapping a task in the list marks it "in progress" (`POST /warehouse-tasks/{id}/start`) and requests up to 3
suggested bins; the real bin where the product was left is scanned (it does not have to be the suggested one) and the read
closes the task (`POST /warehouse-tasks/{id}/complete`); back in the list the green notice `Done: {sku} is now in {bin}` appears
(Batch A3). "Cancel" on this screen only leaves the task
uncompleted (returns to the list); it does not cancel it on the server.

### Split across several bins (putaway with a quantity per bin)

What it does: puts away a task into **several bins with the same quantity** without typing quantities one by one. Example: 185 units
in groups of 20 → 9 bins of 20 and a tenth one with the 5 that were left.

How to use it:
1. Open the task. At the top it shows `Pending to put away: N`.
2. Type the **Quantity per bin** (optional; with the ⌨ key if needed). Empty = the usual putaway (everything into the bin that is scanned).
3. Scan the bins, one by one. Each one is added to the list (`A-01 · 20`) and the screen says `Split: 180 · 5 not put away yet`.
   **Nothing moves yet.** Each row has its **✕** (`Remove {bin}`) to remove that bin; the quantities are recalculated (if an intermediate one is removed, the one that receives the remainder changes).
4. **Confirm split** sends everything together (`POST /warehouse-tasks/{id}/distribute`): either everything is put away or nothing moves.
5. The bin that is no longer filled (the tenth of 185 in groups of 20) receives **what was left** (5) and the screen shows a **sticky alert** (`{bin} gets only {qty} (what was left), not {per}.`): it stays at the top even when the screen scrolls and it closes with the ✕. If you confirm with fewer bins than fit, what is missing stays as a **new task** in the list.

Rules: the full bins fit **plus one more with the remainder** (185 in groups of 20 → 9 of 20 and a tenth of 5; an eleventh is rejected); if the task is smaller than the quantity per bin
(5 in groups of 20), a single bin receives everything; each bin is used **only once**; **serial**-tracked products are not split (they are put away
by indicating the serials, from the web). Whoever puts away needs `warehouse.receive` (the server checks it).

| Case | Exact message | Code | What to do |
|---|---|---|---|
| The bin is already in the split | `That bin is already in the split.` | Local | Scan another one |
| There are no units left to split | `No units left to put away: all {total} are split. Confirm the split.` | Local (and 400 from the server `With {per} per bin, {max} bin(s) fit for {pending}; there are no more units to split.`) | Confirm the split |
| The bin receives less than the quantity per bin | `{bin} gets only {qty} (what was left), not {per}.` (sticky alert, dismissible) | Local | Informational: check that this bin can receive that remainder |
| The bin has a configured capacity and what it will receive exceeds it | `Room for {free}: it will receive {qty}. You can confirm anyway.` (below the row) | Local, with the capacity and stock that the bin list brings | **It only warns; it does not block.** Without signal or without a configured capacity there is no warning |
| There are repeated bins (direct API call) | `There are repeated bins; each bin is used only once.` | 400 | Remove the repeated one |
| The quantity per bin is missing | `Indicate the quantity per bin.` | 400 | Type it |
| No bins indicated | `Indicate at least one bin.` | 400 | Scan at least one |
| A bin does not belong to this warehouse | `Bin not found.` | 404 | Scan one from the warehouse; nothing was moved |
| The task is not a putaway | `The split per bin only applies to putaway tasks (PUTAWAY).` | 422 | Use that task's screen |
| Serial-tracked product | `Serial-tracked products are not split per bin; complete the task indicating the serials.` | 422 | Complete it from the web with the serials |
| The task was already completed or cancelled | `The task was already completed or cancelled.` | 422 | Go back to the list |

### Fields and validations

| Field / case | Exact message | Source |
|---|---|---|
| There are no pending putaway tasks | `No putaway tasks pending.` | Local, empty list |
| The scanned code is not a warehouse bin | `No bin matches that code.` | API (`GET /warehouses/{id}/bins?search=`), no exact match |
| The task list could not be loaded or the task could not be completed (no signal, server error) | The title of the server error, or `No connection to the server...` | API |

### Frequent cases

See the [FAQ](faq.md) for what to do if a task disappears from the list halfway through putting it away (another device or the web
completed it first).

---

## 6. Dispatch (pick and pack)

What it does: picks lines (product, quantity, source bin) without signal and, when done, you choose one of two outputs
(`app-almacen/src/app/dispatch.tsx` and `features/dispatch/dispatchApi.ts`):

- **Complete dispatch** (main button) = **manual issue** since 2026-10-11 (owner's decision): takes the inventory out **without
  a delivery** with a **mandatory reason** and an **optional note**, and generates its own document `DMA-#####` (`POST /api/v1/manual-issues`,
  [06 §7b](06-inventario-y-almacen.md#7b-manual-issue-dma--2026-10-11)); **with no order, consignee or packing**, and a manual issue
  is **never** packed afterwards. It is only offered with the `warehouse.issue` permission. Below it reads `Manual issue: the stock leaves without a delivery (no order or packing) and with a reason. It is sent when there is signal.` The details are in
  [6.y](#6y-complete-dispatch--manual-issue-dma-2026-10-11). (Before this version, “Complete dispatch” left a pick batch
  `EMP-#####` unpacked with `POST /api/v1/pick-batches`; a device that still has one of those in the queue sends it anyway.)
- **Pack** (optional): choose the consignee and the packages and send picking and packing in **one** atomic call
  (`POST /api/v1/pick-batches/collect-and-pack`, chapter 8A §6). Below it reads `Choose the consignee and confirm; it sends once there is a signal.`

One local dispatch at a time per device, with the same real lock as Receive (§1).

**Own inventory or a 3PL client's inventory.** The screen dispatches both; what it requires is **a single owner per dispatch**: the owner
(the product's 3PL client, or “own” if the product has no client) is set by the first scanned product, and the title of the open
dispatch shows the client's name or `Own inventory`. When you **Pack**:
- with a **3PL client's** inventory, the order belongs to that client: the app goes straight to `Who is this dispatched to?` (its consignees);
- with **own** inventory, the app first asks `Which customer is it for?` (the active clients, `GET /api/v1/clients`) and then
  that client's consignees.

**Complete dispatch** never asks for a client or a consignee: it asks for the **reason** and the **note**.

Who can: `warehouse.pick` to pick and pack (packing also requires `orders.create` on the server); **Complete dispatch**
(manual issue) requires `warehouse.issue` —the **Warehouse operator** and **Company admin** templates include it—. Without `warehouse.issue`
the app does not show the button and instead it reads `“Complete dispatch” (manual issue, no delivery) needs the warehouse.issue permission and your user does not have it: here you can Pack. Ask the administrator if needed.` (if the device could not yet read the user's permissions: `Your permissions could not be read yet (signal is needed once): for now you can only Pack.`). Choosing the
consignee requests the client's location list (`GET /api/v1/locations?clientId=`), which separately requires `locations.read`; with
own inventory, choosing the client reads `GET /api/v1/clients`, which requires `clients.read`. A device role that has
`warehouse.pick` without those permissions can pick, but “Pack” will fail with the server's permission error: the device's role
should have them if it is going to pack.

How to use it:
1. Scan a product; if there is already a dispatch in progress, the product has to belong to the **same owner** as the open dispatch (the
   same client, or own if the dispatch is of own inventory).
2. **First the quantity, then the bin** (Batch A5, owner's decision). The quantity comes **empty** (it no longer defaults to 1) and
   below it reads `Type the quantity, then scan the bin: the line is added on its own.` With the quantity typed (greater than 0),
   **reading the bin adds the line instantly** (scanning = OK; typing it with ⌨ and tapping OK does the same),
   with the green notice `Added: {qty} {sku} from {bin}`. The bin is free text: it is not checked against the server
   in this step, so that you can pick without signal.
   - **Without a quantity**: scanning the bin **adds nothing**; the big red notice `Type the quantity first, then scan the bin.` appears and the cursor moves to the quantity. Type the quantity and scan the bin again.
   - **Quantity 0 or not a number**: it does not add either; notice `The quantity must be a number greater than 0. Fix it and scan the bin again.`
   - There is no longer an "Add" button (the reading adds it); "Cancel" leaves the line unadded. A line added by mistake is removed
     with ✕.
3. **Complete dispatch** (manual issue, with `warehouse.issue`): resolves the scanned bins to their real id (first the device's own,
   without signal; if not there, it asks the server), asks for the **reason** and the **note**, confirms and sends it through the outbound queue
   (`manualIssue`). See [6.y](#6y-complete-dispatch--manual-issue-dma-2026-10-11).
4. **Pack**: resolves the bins the same way, asks for the client (own inventory only) and looks up its consignees; type the
   **Packages** (1 by default) and tap the consignee. That sends the picking and the packing together, with service `STANDARD`, package
   `BOX` and the order confirmed; without signal at that instant, it is queued (`pack`). When done it returns to Home.
5. "Cancel dispatch" (with confirmation `Cancel this dispatch?` / `Everything collected is lost. This can't be undone.`) discards
   everything picked.

### Pick plan with several bins (task 24d)

When the typed quantity **does not fit in the first bin** (product without lot), the screen shows a **Pick plan** with where to take it from,
following the server's exit order and deducting what this dispatch has already taken: with 50 and stock of 20 in `P-01` and 40 in `R-02`,
`20 from P-01` and `30 from R-02`. **Use this plan** adds one line per bin, all at once.

Changing a bin: tap **Change** on the row and scan another one. It is accepted if that bin has available stock of the product for
that quantity (counting what other rows already take from it). If the total stock is not enough it shows `Not enough stock: {n} short. Lower the quantity.`
and **Use this plan** is disabled. With a **lot**-tracked product there is no plan: the bin is dictated by the lot that expires first (FEFO), as before.
Scanning a bin without tapping anything still adds a single line with the whole quantity, as always.

| Case | Exact message | What to do |
|---|---|---|
| The scanned bin does not have enough | `{bin} does not have {qty} available of this product ({have} there). Scan another bin.` | Scan another one or lower the quantity |
| Not enough stock | `Not enough stock: {short} short. Lower the quantity.` | Lower the quantity |

### Fields and validations

| Field / case | Exact message | Source |
|---|---|---|
| Product code does not match any synced product | `No product matches that code.` | Local |
| Product from a different owner than the dispatch in progress (another client, or own with a client's and vice versa) | `A dispatch carries products from a single owner. Finish or cancel this dispatch to dispatch products from another owner.` | Local |
| Scanning the bin without a typed quantity | `Type the quantity first, then scan the bin.` (nothing is added; the cursor moves to the quantity) | Local |
| Scanning the bin with quantity 0, negative or not a number | `The quantity must be a number greater than 0. Fix it and scan the bin again.` (nothing is added) | Local |
| There is already a dispatch in progress (attempt to start another one) | `There is already a dispatch in progress; it has to be confirmed or cancelled before starting another one.` | Local (the screen always resumes the open one) |
| A bin scanned while picking does not exist when resolving it in "Pack" | `No bin matches that code.` + the codes that were not found | API, when packing |
| The client's consignees could not be looked up (no signal, missing `locations.read`, etc.) | `Couldn't look up consignees (needs a signal).`, or the title of the server error | API |
| Own inventory: there are no active clients to dispatch to | `There are no active customers in the system.` | API, empty list |
| Complete dispatch with a scanned bin that does not exist | `No bin matches that code.` + the codes that were not found (the reason is not asked and nothing is sent) | API, when completing |
| **Complete dispatch** (manual issue): reason, note, permission and rejections | See the table in [6.y](#6y-complete-dispatch--manual-issue-dma-2026-10-11) | Local / API |
| The client has no consignees in the system | `This client has no consignees in the system.` | API, empty list |
| Confirming without signal | It is saved anyway and sent when there is a connection | Outbound queue |
| The server rejects the dispatch when confirming it (credit exceeded, etc.) | The exact server message, see [06 §7](06-inventario-y-almacen.md#7-ad-hoc-picking-and-packing-pick--pack) | API, via queue |

### Statuses and frequent cases

Same as Receive: the local dispatch has no status of its own until it is confirmed; cancelling before packing leaves no
trace on the server. Closing the app with a dispatch in progress resumes it as it was, including the lines already
picked (the source bin is still stored as text; it is resolved again only when packing).

---

### 6.x Suggested bin, mandatory lot and completing without packing (2026-10-05)

**Suggested bin.** When the product is chosen, the cursor lands on the **Quantity**. The app uses the **server's exit order** (`GET /api/v1/inventory/exit-options`, `inventory.view`): per product, what is
**available** (on hand − reserved) per bin and lot, with its `rank` (1 = goes out first). It is the **same rule as picking** (expires first, then zone type —picking, reserve, refrigerated,
staging— and bin code; never quarantine, cross-dock or inactive bins): **it lives only on the server**; the app sorts nothing, it only follows the `rank` and deducts what this dispatch has already taken.
- **Product without lot:** box **Suggested: P-01** (with the available quantity) and the button **Use P-01**; it is a help: **any other** bin can be scanned.
- **Lot-tracked product:** the bin **is not optional**: **Must come from A-01** (lot, expiration and available quantity). Scanning another one gives `This product is lot-tracked and the one expiring first goes out first: it must come from {bin} ({detail}).`;
  asking for more than there is there gives `In {bin} there are only {n} available. Type {n} (or less) and then scan the bin; for the rest, the next one.`
- **Also without signal.** The device keeps a **copy** of the exit order of the default warehouse (table `stock_exit`): the sync downloads it in full (500 at a time) when the app opens, **every 5 minutes** and **right away if
  the device just sent movements**. Online, when the product is chosen the app requests the current snapshot of that product (and updates the copy); without signal it uses the copy and says so: `Based on the inventory downloaded to the device (no signal).`
  Only if the copy has **never** been downloaded: `No signal: could not look up where the product can come from. Scan the bin.` (with no suggestion or requirement). The copy may be a few minutes behind: it is a guide; the server
  re-verifies the inventory when picking.

**Completing the dispatch without packing** (2026-10-05; **replaced on 2026-10-11** by the manual issue, [6.y](#6y-complete-dispatch--manual-issue-dma-2026-10-11)). With lines, the screen offered **Complete dispatch** (main) and, separately, **Pack** (optional). **Complete dispatch** picked (`POST /api/v1/pick-batches`): the inventory went out, **with no order,
consignee or packing**; if there was no network it stayed in the outbound queue (`collect`). The `collect` rows that a device still has in the queue are still sent the same way. Pack stays the same (client → consignee → `collect-and-pack`).

### 6.y Complete dispatch = manual issue (DMA, 2026-10-11)

Owner's decision: the **Complete dispatch** button is the server's **manual issue** ([06 §7b](06-inventario-y-almacen.md#7b-manual-issue-dma--2026-10-11)):
the inventory exit **without a delivery** (a sample, internal use, a client who comes to pick up, a counter sale…) with a **mandatory reason**,
an **optional free note** (up to 500 characters) and its `DMA-#####` document. **Pack does not change.** **It is not blind**: the screen keeps showing the
stock and the suggested bin as before. A dispatch belongs to **a single owner** (own or a client's), as always.

Who can: `warehouse.issue` (in addition to what picking already required). Module **WMS_LOTSERIAL**.

How to use it (since version **2026-10-11 (b)**, owner's decision: “the manual issue must not complicate the device”; with a default reason or with
one already used, completing takes **2 taps — Complete dispatch → Issue — and no keyboard**):
1. With picked lines, tap **Complete dispatch**. The app resolves the bins (as before); if any does not exist it says so and does not continue.
2. **With a reason set** (see “Reason it opens with” below) the app goes **straight to the confirmation** `Issue without a delivery?`, without asking you to choose:
   below, `Manual issue: the stock leaves with no order or packing.`, the summary `{owner} · lines: {n} · units: {quantity}` (the owner is the
   client or `Own inventory`) and, in a box, **`Issued for: {reason}`** with a line that says where it came from (`It is the last reason you used on this device.` or `It is the company's default reason.`) and a small **Change** button. Further down, a small **Add a note** button (the note is
   hidden and empty by default), the big **Issue** button and **Back** (returns to the dispatch without sending anything). No text field: the
   keyboard does not open.
   - **Change** opens the list `Why is it leaving without a delivery?` (`Manual issue: the stock leaves with no order or packing. Choose the reason.`) with the set reason
     marked. The **reasons** are big buttons, one per row (like the Adjust reasons and the Damage destination): **Sample**, **Internal use**,
     **Customer pickup**, **Sale**, **Other** — or whichever the company has (see “Reasons” below). Tapping one sets it and returns to the confirmation;
     **Back** returns to the confirmation without changing it.
   - **Add a note** shows **Note (optional)** (with the keyboard) and the counter `{n}/500 characters.` (it does not let you type more than 500).
   - **Without a reason set** (neither last used nor company default): the app first opens the list to choose, as before; when you tap one it goes to the
     confirmation (Complete → reason → Issue). **Back** on the list returns to the dispatch.
3. **Issue** sends the dispatch (the confirmation is this screen: there is no longer a second prompt “Issue without a delivery?” with No/Issue).
4. When issuing, the dispatch is **saved in the outbound queue** (`manualIssue`, `POST /api/v1/manual-issues` with its `Idempotency-Key`, in the order it was
   done, same as Transfer and Adjust), the device's dispatch is closed in that same step and the **local balance already reflects the exit** (Lookup shows
   it). The app waits for the server for a few seconds at most (`Sending the manual issue…`):
   - **sent**: green notice `Done: manual issue DMA-00012.` (with the number the server gave) and the screen is ready for the next dispatch;
   - **no signal** (or weak signal): green notice `Manual issue queued: it is saved on the device and is sent on its own as soon as there is signal.`; the DMA
     number is seen later on the web (Picking & packing, Type column);
   - **rejected by the server**: the **exact server message** in red under the product field (for example `Insufficient inventory of SKU-1 in
     A-01: available 2, requested 3.`), the dispatch **reopens exactly as it was** (same lines; the reason and the note stay chosen), the exit
     is undone in the local balance and **nothing** is left in Sync: fix it and tap **Complete dispatch** again (it opens straight to the
     confirmation with that reason, and with the note visible if one had been written).
   - If the rejection arrives **later** (it was sent on its own, afterwards): it stays in **Sync → With errors** with the server message (Retry or
     Discard, §9), and the exit has already been undone in the local balance.

**Reasons.** The app uses the **company's** reasons (`GET /api/v1/manual-issues/reasons`: the active and enabled ones, with the name the company
gave them in System → Lists, in the app's language). The sync downloads them and stores them on the device, at most every 30 minutes and **only if
the user has `warehouse.issue`** (without the permission the server responds 403 and would leave a security event on every pass). Without signal the stored ones are used;
each reason carries `isDefault` (the company's default) and a copy stored by an earlier version, without that flag, is downloaded again on the next
pass with signal without waiting the 30 minutes. If the device has **never** downloaded them, it offers the six factory ones (`SAMPLE` Sample, `INTERNAL_USE` Internal use, `OWNER_PICKUP` Owner pickup, `CUSTOMER_PICKUP` Customer pickup, `SALE`
Sale, `OTHER` Other). If the company disabled a reason after the last download, the server rejects the issue with its message (see the table).

**Reason it opens with (2026-10-11 b).** When you tap **Complete dispatch** the app sets, in this order, the first one that **is still among the reasons
offered** (a reason the company disabled no longer comes in the downloaded copy and is skipped without notice):
1. the one already chosen on the screen (after a server rejection);
2. the **last reason that operator used on this device** (remembered per user and per company on the device, when issuing —sent or queued—;
   a rejection does not count);
3. the **company's default reason** (Tenant settings, [06 §7c](06-inventario-y-almacen.md#7c-default-reason-for-the-manual-issue-2026-10-11-b));
   the reason list marks it with `isDefault` and the device stores it with the copy;
4. none: you have to choose from the list.
With the five factory ones (the device never downloaded the company's) there is no default; the last used counts if it is one of them. The server **still requires
a reason**: what changes is that the app already has it set.

**Local balance.** When queuing, the device subtracts from each bin what goes out: for lot-tracked products, from the lot that expires first within the bin (as
the server does), and stores that effect with the operation to undo it exactly if the server rejects it. The copy of the **exit order** (`stock_exit`, the
suggested bin) is not deducted: it is downloaded again as soon as the issue reaches the server (same as with Pack).

| Field / case | Exact message | Source |
|---|---|---|
| User without `warehouse.issue` | **Complete dispatch** is not shown; it reads `“Complete dispatch” (manual issue, no delivery) needs the warehouse.issue permission and your user does not have it: here you can Pack. Ask the administrator if needed.` | Local (user's stored permissions, `GET /api/v1/me`) |
| The device has not yet read the user's permissions | `Your permissions could not be read yet (signal is needed once): for now you can only Pack.` | Local |
| No reason set (neither last used nor default) | The list `Why is it leaving without a delivery?` opens; **Issue** does not appear until one is chosen. (The notice `Choose the reason for the manual issue.` remains only as a safeguard and should no longer be seen.) | Local |
| The company has no enabled reasons (the downloaded copy is empty) | `The company has no reasons enabled for manual issues. Ask the administrator to enable one (System → Lists).` | Local |
| Note longer than 500 characters | It does not let you type more than 500; if it ever happened: `The note allows at most 500 characters.` | Local |
| Reason disabled or deleted on the server | `The reason {CODE} does not exist or is inactive.` (400) | API |
| Insufficient inventory | `Insufficient inventory of {sku} in {bin}: available {x}, requested {y}.` (409; nothing goes out and the DMA number is not consumed) | API |
| Products from two owners | `A manual issue can only have products from the same owner.` (400; the app already prevents it when scanning) | API |
| Role without `warehouse.issue` on the server (the permission was removed and the device does not know yet) | 403 with no body: the app shows `You do not have permission for this action. Ask your administrator to grant it.` (before 2026-10-11: `Something went wrong. Try again.`; §9) | API |
| Any other rejection (inactive product, lot, serials…) | The exact server message (same as picking, [06 §7](06-inventario-y-almacen.md#7-ad-hoc-picking-and-packing-pick--pack)) | API |
| No signal | `Manual issue queued: it is saved on the device and is sent on its own as soon as there is signal.` | Outbound queue |

Frequent cases:
- **I got the quantity wrong and it was already sent.** From the web, delete the manual issue (it returns everything to its bin, [06 §7b](06-inventario-y-almacen.md#7b-manual-issue-dma--2026-10-11)) and do it again.
- **It was going to a consignee.** Use **Pack**, not **Complete dispatch**: a manual issue never becomes an order.
- **Today it is a different reason.** Tap **Change** and choose the right one. Note: that one becomes your “last used” and the next dispatch opens with it (not
  with the company default); to go back to the usual one, **Change** again.
- **I want to leave a note.** Tap **Add a note** before **Issue**; if you do not tap it, the dispatch goes without a note.
- **I closed the app while it said “Sending…”.** The dispatch is already in the queue: it is sent on its own; do not repeat it (it cannot end up twice: the device's
  dispatch was closed when it was queued and the queue uses `Idempotency-Key`).

## 7. Count

Since Batch A4 the screen starts with **"How will you count?"**: **By bin** (the usual way, described below) or
**By product** (section 7.1). The app remembers the last choice; the first time, by bin. If in "By bin" you
scan a product, the notice says `That code is a product. To count it that way, tap "By product".`

What it does (by bin): counts a warehouse bin. Scanning the bin **claims the count on the server**
(`POST /api/v1/cycle-counts` with `resumeOpen`, chapter 8A §6): the bin is a shared resource (if there is already an open count of that bin it is **resumed**; if
someone else has it, the server responds with their name; see 7.5), so this first step needs signal. From then on, capturing what is found is
local and "Finish this bin" queues the captured batch and the closure, in that order (two rows in the outbound
queue). One count at a time per device.

Who can: `warehouse.count.capture` to open, capture and finish a count (implied by `warehouse.count`).
**Cancelling a count in progress** (`DELETE /api/v1/cycle-counts/{id}`) requires the full `warehouse.count` permission,
not just `.capture`: someone who only has `warehouse.count.capture` (blind count) can finish it with what they counted,
but not cancel it — tapping "Cancel count" will return the server's permission error. The blind count role
should know this, or be given `warehouse.count` if it needs to be able to cancel.

**Blind count**: if the person who signed in does not have `warehouse.count` (only `.capture`), the server does not send the
expected quantities (`systemQty` arrives empty) and it cannot be reconciled from here; the screen shows the notice
"Blind count: system quantities are not shown." and simply prints nothing where the expected quantity
would go. The reconciliation (comparing what was counted against what was expected and deciding the adjustment) is done on the web
([06 §6](06-inventario-y-almacen.md#6-cycle-count-informed-mode)).

How to use it:
1. Scan the bin to count; if there is no open count on that bin, a new one is opened and it shows what is
   expected (or nothing, if blind).
2. Scan each product found: if it matches an expected line, the quantity is captured for that line
   (it can be scanned again and corrected); if it was not on the list, it is added as an extra line. **Product without a label**
   (Batch A3): tapping the product in "What's expected here" puts its code (SKU) in the product field; confirm with
   **OK** and move on to the quantity. Tapping it does not change what is shown: the expected quantities still appear only for whoever
   has `warehouse.count`.
3. "Finish this bin" queues the captured batch and the closure of the count, and **stays in Count** (since 2026-10-10: see 7.6) with the cursor in the field for the next bin. "Cancel count"
   (with confirmation) releases the bin on the server without saving what was captured.

If the app was closed and reopened with a count by bin in progress, the list of expected lines is requested again from the
server (it is not stored locally, only the count id and what was already captured); this screen already needs signal at that
moment anyway. (A count by product, on the other hand, is resumed without signal: see 7.1.)

### 7.1 Count by product (Batch A4)

What it does: counts **one product in every bin** where the system says it is (meant for when the bins are not
properly labeled). The app creates the count on the server (`POST /api/v1/cycle-counts` with the product, no bins and `allowEmpty: true`;
source "By product") and lists **one row per bin** (and per lot, if the product is lot-tracked: the lot number is shown in the row; it is not
searched or captured by lot). The operator writes down what they find in each one; the system calculates the variance when reconciling
on the web ([06 §6](06-inventario-y-almacen.md#6-cycle-count-informed-mode), quick review "To review").

Who can: `warehouse.count.capture` (open, capture, confirm and create a bin in "Another bin"); `inventory.view` for
the zone list (already needed to get in). Cancelling requires `warehouse.count`, as in the count by bin. Module
**WMS_LOTSERIAL**.

How to use it:
1. Count → **By product** → scan (or type with ⌨ and OK) the product's barcode or SKU. It needs signal.
   - Product with **serial number**: notice `This product is counted by serial number; count it from the web for now.` and **no
     count is created** (the app does not capture serials).
   - Product with **no stock** anywhere in the warehouse: the count opens **empty** and the screen shows the block `The system has no stock of this product. If you found it in any bin, use "Another bin".` with the big button **Another bin**
     (step 4). You continue as with any product: quantity in the added bin and Confirm.
2. The list: each row says the **bin**, the **lot** if it has one, "Pending review" if the bin is provisional and
   "Expected: N" **only if** the person counting has `warehouse.count` (blind, it is not shown, as always). On the right, the space
   for the quantity. **What is left blank counts as 0.** There is no "all here" button.
3. With **more than 6 bins**, **Search bin or lot** appears (it filters the list; it does not change what is sent).
4. **Another bin** (link at the end of the list), for what was found where the system had nothing: choose the **zone**, type the
   bin's **code** or its parts (**aisle, rack, level, position**: joined as `A01-R02-N3-P04`) and, if the product is
   lot-tracked, the **lot number** (mandatory) and its **expiration** (optional, in the company's date order). **Add
   bin** creates it on the server (`POST /api/v1/cycle-counts/{id}/bins`, needs signal) as **provisional**: it stays "pending
   review" until the supervisor confirms it on the web. The row enters the list with its quantity space. If the code already
   exists in the warehouse, that bin is used (without the provisional flag). Scanning a bin label in this step types its
   code. An "Another bin" row can be removed with ✕ before confirming. If the bin already existed and the server has it
   "pending review" (the sync brings the flag), the row shows it too.
5. **Confirm**: above the button, while there are blank spaces, it reads `{n} blank bins count as 0.`; one tap on
   Confirm accepts it and closes (no dialog). **At least one bin with a typed number is required** (Batch A5, owner's
   decision; **0 counts**, and an "Another bin" row with its quantity counts too): with **all** of them blank, Confirm sends nothing and
   the big red notice `Type at least one quantity. If there is none of this product, type 0 in one bin.` appears; the notice goes
   away when any quantity is typed. The batch is queued with **all** the rows (the blank ones as 0, the "Another
   bin" ones as new lines with their bin and lot) and the closure, and it stays in Count (7.6). It works without signal. If the count was left **empty** (product
   with no stock and no row added), Confirm sends nothing and warns `An empty count can't be finished: add the bin where you found it with "Another bin" or cancel the count.`; you leave with **Cancel count** (step 7).
6. **Resume**: what is typed is saved on the device at every change. If the app is closed (or the device is turned off), when returning to Count
   the same list appears with what was already typed, without needing signal. One count at a time per device (by bin or by product).
7. **Cancel count** (with confirmation): same as the bin one (`DELETE`, needs signal and `warehouse.count`).

#### 7.2 Counting several products in one count (Batch 25)

Since 2026-10-05, **By product** opens **a single count** to which products are added (instead of creating one count per product). The
"one product in all its bins" count of section 7.1 is no longer opened from the screen; a count of that type that was left open
on a device is resumed as before.

Who can: `warehouse.count.capture`; cancelling requires `warehouse.count`. Module **WMS_LOTSERIAL**.

How to use it:
1. Count → **By product** → scan the first product. The count opens **empty** on the server (needs signal) and the screen moves to the list
   **Counted**. A **serial**-tracked product is not counted from the app: `This product is counted by serial number; count it from the web for now.`
2. For each scanned product the app asks the server **where it is** (without quantities, also blind):
   - **one bin** with stock: it appears big as **Bin** with the link **Change position**; only the **quantity** and **Add** are missing;
   - **several**: `The product is in several positions: choose where you counted it.` and a list of buttons (bin · lot · zone);
   - **none** (or no signal): `The system has no stock of this product in any position. Scan the position where you found it.` /
     `No signal: the product position could not be looked up. Scan the position where you counted it.` Scan the label (it is looked up first in the device's
     bins, without signal) or use **Another bin** (creates a provisional one, needs signal; for lot-tracked products it asks for the lot number).
   - **Change position**: choose another from the list, scan another or **Another bin**. For lot-tracked products with no known lot, type the **lot number**.
3. **Add** saves the line on the device (`SKU added at A-01.`). The product can be scanned again in **another** bin (another line). In the **same**
   bin and lot **it is no longer offered**: the product's bin list (and the proposal) **does not include the ones you already counted** (2026-10-07). If you already counted them all, it says
   `You already counted this product in every position where the system says it is. If you found it in another one, scan that position; to correct a quantity, tap ✎ on its line in the list.`
   To correct a quantity, ✎ on its line in the list; ✕ removes it.
   **Tapping a product in the list** (2026-10-07) puts it in the scan field (without sending it) and scrolls the screen up to that field: confirm with **OK** and it follows the normal path
   (it proposes the bins you have not counted yet). On-screen help: `Tap a product in the list to count it in another position.` Same in “By bin”: tapping a product in “What's expected here” scrolls the screen to the field.
4. **Finish count** (enabled with at least one line) queues **one batch** with all the lines (bin + product + quantity + lot by number) and the closure;
   it works without signal and stays in Count (7.6). The variance is calculated when reconciling on the web.
5. **Resume**: what was counted is saved on the device; when closing and opening the app the count is still there, without signal. One count at a time per device.
6. **Cancel count** (with confirmation): `DELETE`, needs signal and `warehouse.count`.

| Case | Exact message | Source |
|---|---|---|
| Code with no synced product | `No product matches that code.` | App |
| Serial-tracked product | `This product is counted by serial number; count it from the web for now.` | App |
| Opening the count without signal | `Couldn't open the count for that product (needs a signal).` | App |
| Several bins | `The product is in several positions: choose where you counted it.` | App |
| No stock in any bin | `The system has no stock of this product in any position. Scan the position where you found it.` | App |
| No signal when looking up where it is | `No signal: the product position could not be looked up. Scan the position where you counted it.` | App |
| Scanned bin that does not exist | `No bin matches that code.` | App |
| Same product, bin and lot already counted | (when scanning, that bin is no longer offered; if it was all of them: `You already counted this product in every position where the system says it is. …`) / `SKU is already counted at A-01: tap its line in the list to correct the quantity.` (when changing the bin to one already counted) | App |
| Lot without number (lot-tracked product) | `This product is lot-tracked: type the lot number.` | App |
| No lines | `Nothing counted yet. Scan a product.` (Finish disabled) | App |
| Server errors when looking up where it is | The server message as is (404 `Product not found.`, 404 `Count not found.`) | API |

### Fields and validations (count by product)

| Field / case | Exact message | Source |
|---|---|---|
| Product with serial number | `This product is counted by serial number; count it from the web for now.` | App (does not call the server) |
| Code does not belong to any synced product | `No product matches that code.` | App |
| Product with no stock in the warehouse | Not an error: the count opens empty and it reads `The system has no stock of this product. If you found it in any bin, use "Another bin".` | App |
| Confirming an empty count (with no row at all) | `An empty count can't be finished: add the bin where you found it with "Another bin" or cancel the count.` (nothing is sent) | App |
| Opening without signal | `Couldn't open the count for that product (needs a signal).` | App, after a network error |
| Other errors when opening | The server message (e.g. 422 `The warehouse is inactive.`, 400 `The count allows at most 1000 lines; narrow the filters.`) | API |
| A space with something that is not a quantity | `Some quantities aren't numbers; fix them to confirm.` (Confirm disabled) | App |
| Blank spaces when confirming | `{n} blank bins count as 0.` / `1 blank bin counts as 0.` (notice, it does not block if at least one number is typed) | App |
| Confirming with **all** bins blank (Batch A5) | `Type at least one quantity. If there is none of this product, type 0 in one bin.` (nothing is sent; the count stays open) | App |
| Search box with no matches | `No bin matches "{text}".` | App |
| Another bin: no zones | `No zones to choose from: sync with a signal and try again.` | App |
| Another bin: the device's zones | `No answer from the server: showing the zones saved on the device.` (notice) | App |
| Another bin: lot-tracked product without lot number | `This product is lot-tracked: type the lot number.` ("Add bin" disabled) | App |
| Another bin: badly typed expiration | `That date isn't valid. Type it like this: MM/DD/YYYY` (according to the company) | App |
| Another bin: the bin (and lot) is already on the list | `Bin {bin} is already on the list: type the quantity there.` | App |
| Another bin: code repeated in the warehouse | `A bin with that code already exists in the warehouse.` (409) and button `Use {bin}, which already exists` | API |
| Another bin: bin data | 400 `Indicate the bin's zone.` / `Enter the bin's code or its aisle/rack/level/position.` / `The bin code only allows letters, numbers, hyphen and underscore (maximum 40).` | API |
| Another bin: zone or count | 404 `Zone not found.` / `Count not found.`; 422 `The zone is inactive; reactivate it first.` / `The count was already reconciled; it does not accept new bins.` / `The warehouse is inactive.` | API |
| Another bin without signal | `Couldn't create the bin: it needs a signal. Try again when there is one.` | App, after a network error |
| Green notices | `Bin {bin} added (pending review).` / `Bin {bin} already existed; it was added to the list.` | App |

### Fields and validations

| Field / case | Exact message | Source |
|---|---|---|
| The scanned code is not a warehouse bin | `No bin matches that code.` | API |
| The count for that bin could not be opened (no signal, bin already being counted by someone else) | `Couldn't open the count for that bin (needs a signal).`, or the title of the server error | API |
| Product code does not match any synced product | `No product matches that code.` | Local |
| There is already a count in progress (attempt to start another one) | `There is already a count in progress; it has to be finished or cancelled before starting another one.` | Local |
| The expected lines could not be requested again when reopening the app (no signal) | The title of the server error, with a "Retry" button | API |
| Cancelling without `warehouse.count` (blind count only) | The server's permission error (403) | API |
| Cancelling without signal | `Couldn't cancel (needs a signal); you can try again.` | Local, after a network error |
| Finishing without signal | It is saved anyway and sent when there is a connection (two rows in the queue) | Outbound queue |
| When sent, the server saved the other lines but skipped the ones the supervisor had already corrected (200 with `skippedLines`, Batch A7) | In Sync, block `The rest of the lines of your count were saved.` with the lines not saved (section 9.1). It is not an error: the submission stays as sent |
| When sent, ALL the lines of the batch had already been corrected by the supervisor (409, Batch A6/A7) | In Sync, card `The supervisor already corrected all the lines of this submission.` with the explanation and "Refresh the count" (section 9.1). No line of that submission was saved | API, via queue |

### Statuses and frequent cases

The count exists on the server from the moment the bin is scanned (even if nothing has been captured yet); that is
why "Cancel" is a server call, not just a local discard. Finishing without connection twice in a row does not
duplicate anything: the idempotency key of each queue row protects the retry.

---

### 7.4 Informed count when capturing (task 25)

What it does: if the company and your setting allow it (06 §6.z), after **accepting** the quantity of a server line the app verifies it with the server, **with signal**,
and tells you whether it matches **without having shown you the expected quantity beforehand**. Nothing changes for whoever has the Count permission (they see the expected quantity from the start, as always).

- **Count by bin:** you scan the product, type the quantity and tap **Add**.
  - Matches → the line is captured and `Matches: you counted {n} and {m} was expected.` appears (or `It matches what was expected.` if the company does not show the number).
  - Out of tolerance → it is **not** captured yet: `It does not match what was expected. Count again and accept again.` appears (never the expected quantity), the quantity is left empty and you type it again.
  - Second figure → it is captured and `You counted {n} and {m} was expected. It goes to review.` appears (or `It does not match what was expected. It goes to review.`). The supervisor reconciles with both figures.
  - A line already verified **cannot be edited or removed**: `That line was already verified; it cannot be changed or removed.`
- **Count by product:** when tapping **Confirm**, each server bin is verified and the result appears **below its row**; that tap does **not** confirm. The ones asking for a recount are
  corrected (the note goes away when typing) and Confirm is tapped again; the closed ones can no longer be edited. With everything closed, the next tap confirms. Support message:
  `Review the result of each bin. Those asking for a recount are corrected; tap Confirm again to continue.` Blank bins are verified as 0.
- **Without signal** or with the permission not enabled (403): it is captured **without verifying**, as before; in the latter case the app does not ask again in that count.
- **Out of this version:** the open count with several products (7.2) and the lines added by hand (they do not exist on the server until the batch is sent) are not verified.

Server messages it may show (for example `The line was already verified; its quantity cannot be changed.`): see 06 §6.z.

### 7.3 Quantity calculator (2026-10-05)

In the count, next to the **quantity** there is a **Calculator** button (blue box with the white drawing of a calculator) to count pallets without adding up in your head. When tapped, the single field is **replaced in the same place** by **Rows × Columns** and **Loose units**; below you see the
assembled calculation and the **Total** (for example `(5 × 3) + 10` → `Total: 25`), which fills the quantity as you type. For a pallet with several layers, **+ Another block** adds another rows × columns (and ✕ removes it).
The button with the **← arrow** (top left, next to the “Calculator” title; for the screen reader it is called **Direct quantity**) returns to the normal field with the total already set; what was already typed when opening goes to “loose units”. Only **the quantity** is saved (not the formula).
- Where: the quantity of the count by bin (found and correction), that of the open count with several products and, in the count of one product in all its bins, a calculator button on each row that opens the calculator **on the same screen** (instead of the list; Batch A9: before it was a window on top and the keyboard covered “Loose units”) with **Use {total}** and **Cancel**; the ← arrow also cancels without changing the row.
- **Depth (2026-10-07):** each block has a third field, **Depth** = how many there are **one behind another**: rows × columns × depth. It is optional: blank counts as 1, so what was there before does not change (`(5 × 3) + 10` = 25; with depth 2: `(5 × 3 × 2) + 10` = 40). Each block has its own depth (pallets of different depth). Below the blocks the help `Depth: how many are one behind another (blank = 1).` appears
- Rules: rows, columns and depth are **whole numbers** (depth, 1 or more); loose units allow decimals (comma or period, up to 3). A block needs **both** measures (`Each block needs rows and columns.`); something that is not a number gives
  `Rows and columns are whole numbers; loose units, a number.` (also with a depth that is not a whole number or is 0); more than 9,999,999 gives `That total is too large.` While the calculation is incomplete the quantity stays **blank** (it does not let you save a half-done number).
- The calculator's **⌨** button shows the keyboard for all its fields at once.
- On-screen keyboard (Batch A9): with the on-screen keyboard open, the field being typed (for example **Loose units**) moves up on its own to stay
  **above the keyboard**; if something is covered, the screen can be scrolled. This applies to all screens with fields (Receive, Putaway, Dispatch,
  Count, Lookup and Enroll the device). With the Zebra's reader or physical keyboard the on-screen keyboard does not appear and the screen does not move.

### 7.5 Resuming a count, finishing with uncounted lines and open counts (2026-10-07)

**Resume instead of duplicating.** When scanning a bin, the app sends `POST /api/v1/cycle-counts` with `resumeOpen: true`. If that bin already has an **open** count (Pending)
whose lines are **only of that bin**, the server returns **that same count** (field `resumed: true`) with what was already counted (`countedQty` and `checkState` of each line) and the app
puts it back on screen: `You resumed count CC-00042 for GENERAL: 1 of 3 already counted.` No other count is opened.
- **Who has it:** if the count is assigned to the person scanning, or to nobody (for example, one generated from “what changed” on the web), it is resumed (and the one that had no assignee is put under the name of whoever resumes it).
  If **someone else** has it: 409 `That bin is being counted by {Name} ({CC-#####}).`
- **What is not resumed:** a count of several bins, the count by product and one already finished (Counted) or reconciled: in those cases a new one is opened, as before. The web (without `resumeOpen`) does not change either.
- **Several open counts of the same bin** (duplicates from before this change): one of the user's own is resumed first, the one with the most counted lines. The others are reached from the “Open counts” list (below) or are deactivated on the web (Cycle count → trash).

**Finish this bin with uncounted lines.** The server only closes a count when **all** its lines have a quantity (`{n} line(s) still to count.`, 422). That is why, if lines are missing, the app no longer sends a closure that
would be rejected: it asks **“N product(s) still to count”** with three options:

| Button | What it does |
|---|---|
| **Keep counting** | Closes the notice; you stay on the count screen. |
| **Save and continue later** | Sends what was counted (batch) **without closing** the count. It warns `Saved. Count CC-00042 is still open: resume it from Count.` and returns to Home. The count stays open on the server. |
| **Leave at 0 and finish** | Sets **0** on the missing lines and sends a batch with all the lines and the closure. The 0s remain as a variance for whoever reconciles on the web (they are not verified against the expected quantity). |

With all the lines counted, **Finish this bin** closes directly (without asking).

**Open counts and lock.** A count the user left open on the server (saved and continued later, or closed the app without finishing) is considered **in progress**:
- Home checks it (`GET /api/v1/cycle-counts/page?status=OPEN`, only those assigned to the user, of one bin, in the active warehouse) and stores a copy to know it without signal too.
- While it exists, **the other actions** (Receive, Putaway, Dispatch, Lookup) warn `Finish or cancel the count in progress before using this.` and **Count** shows the **“Open counts”** list at the top
  (`CC-00042 · GENERAL`, `1 of 3 counted`); tapping one (**Continue**) resumes it by its id with what was already counted. The warehouse cannot be changed either.
- **Another** count cannot be opened (another bin or “By product”) while there is one open: `Count CC-00042 (GENERAL) is still open. Finish or save it before counting anything else.`
  Scanning **the same bin** does resume it.
- It leaves the list when it is **finished** (with the closure already in the outbound queue it does not count as open), when it is **cancelled** or when someone deactivates it (when trying to continue it: 404 `Count not found.` and it disappears).
- Cancelling a count still requires `warehouse.count`. Whoever only has `.capture` closes it with **Leave at 0 and finish** or by counting everything.

**If the server rejects the closure.** After sending “Finish”, the app syncs right away and, if the server rejected the batch or the closure, it warns **at that moment**: `The server did not accept finishing the count: {message}`
(besides staying visible in Sync).

| Case | Exact message | Source |
|---|---|---|
| Bin with an open count of someone else | `That bin is being counted by {Name} ({CC-#####}).` (409) | API |
| Count resumed | `You resumed count {CC-#####} for {bin}: {n} of {m} already counted.` | App |
| Lines missing when finishing | `{n} product(s) still to count` + `Keep counting` / `Save and continue later` / `Leave at 0 and finish` | App |
| Saved for later | `Saved. Count {CC-#####} is still open: resume it from Count.` | App |
| Another open count when trying to open one | `Count {CC-#####} ({bin}) is still open. Finish or save it before counting anything else.` | App |
| A different action with an open count | `Finish or cancel the count in progress before using this.` | App |
| Closure rejected by the server | `The server did not accept finishing the count: {message}` | App |
| Closure with uncounted lines (if it were sent anyway) | `{n} line(s) still to count.` (422) | API |

### 7.7 Counting a bin without signal (2026-10-11)

What changed: before, opening a count needed signal (the count lives on the server). Now, when **scanning the bin**, the app gives the server **5 seconds**; if it does not
respond (no signal or very weak signal) it **opens the count on the device** and the operator counts anyway. Only the count **by bin**; the count by product and the “open
count” still need signal to open.

How it looks:
- The notice **“No signal: you are counting on the device. What you count will be sent by itself when the signal returns.”** appears. The count **has no number** yet (the server assigns it when it receives it).
- “What's expected here” comes from **the device's balances** (products and lots the device believes are in that bin, **without quantities**: it is always blind). You count the same way:
  scan the product, type the quantity, **Add**; what was not on the list is added as “what was found”.
- **Finish this bin** (or **Save and continue later**) saves everything **on the device** and puts it in the **outbound queue** as **“Count done offline”**; the device sends it **whole and on its own**
  as soon as there is signal (with its idempotency key: a retry does not duplicate the count). **Cancelling** a count done offline only discards it on the device.
- With **“Save and continue later”** what was counted is sent **without finishing**: when it arrives, the count stays open on the server and appears in “Open counts” to resume it.

What the server does when it receives it (owner's decisions, 2026-10-11): it opens the count of that bin or **resumes the one already open**; if that count belongs **to another counter**, what was counted
is **added to that count** (the count is not finished: its owner continues it) and the app warns **“That position already had an open count from another counter: what you counted was added to that count and its owner continues it.”**
What was counted is compared against the **server's stock when reconciling** (the usual one). If between counting and syncing **there were movements in the bin**, the app warns **“There were movements in that position since you counted offline; the supervisor will take it into account.”** and the count history keeps the comment **“Counted offline; there were movements in the bin since it was counted.”** (without movements: “Counted offline.”).
If it cannot be finished (for example, the server has in that bin a product the device did not know about and it was left uncounted), **what was counted is saved** and the count stays **open**; the app warns
**“What you counted was saved, but the count could not be finished: N line(s) still to count. It stayed open.”**

Frequent cases:
- **I scanned the bin with a slow signal and it opened “offline”:** that is normal after 5 seconds; the server may have created the count anyway, and when sending, **that same count is resumed** (it is not duplicated).
- **“The server did not accept finishing the count: …”:** the server rejected the submission (e.g. inactive warehouse, bin that no longer exists); it stays in Sync to retry or discard, like the others.
- Products and bins the device does not know (created after its last sync) cannot be counted without signal: signal is requested for them as before.

### 5.x Free space of the bin, without the server (2026-10-11)

When putting away or receiving directly to a bin, the app warns if what is being placed **does not fit** (bin capacity). Before, it asked the server; now it **calculates it on the device**: the bin's **capacity**
(downloaded with the bins) **minus what is there** according to the device's balances (which already include what is pending to send). Without a configured capacity it warns nothing. It is only a warning (the server validates and does not block).
Detail: the device **does not know what other lines of the same receipt have already reserved** in that bin (the server does deduct it); that is why the device's free space may be a bit larger and the
server is the one in charge. When the app is updated, the local database migrates on its own to version 12 and **downloads the full bins once**; until it does, a bin without the capacity is still checked with the server as before.

### 6.z Dispatching and packing without signal: clients and consignees on the device (2026-10-11)

Before, when you **Pack** a dispatch of **own** inventory (with no 3PL owner) the app asked the server for the list of clients and consignees, so that step needed signal. Now the device **downloads
those lists with the sync** and reads them from itself:

- **Clients:** the active ones (`GET /api/v1/sync/clients`). **Consignees:** those of the chosen client **plus the operator's shared ones** (`GET /api/v1/sync/consignee-locations`).
- Sorted by name. If a consignee has no name or city, its code is shown.
- They are downloaded with the sync of every minute (by difference). A client or consignee the server deactivates **is deleted from the device**.
- **Permission:** downloading them requires **`warehouse.pick`** (`clients.read` and `locations.read` are no longer needed just to choose who it goes to). If the device **has not downloaded them yet** (first use) or the user does not have `warehouse.pick`,
  the app requests them from the server **as before** (with `clients.read` / `locations.read`). When the app is updated, the local database migrates on its own to version 13; nothing is lost.
- When confirming the dispatch without signal it stays in the **outbound queue** (“Dispatch”) and is sent on its own when the signal returns, as always.

Frequent cases:
- **A new client or consignee does not appear:** it was created less than a minute ago; tap **Sync now** on Home. If it still does not appear, it is deactivated or the user does not see that client (data scope).
- **The consignee I chose was rejected by the server when sending it** (for example, it was deactivated after the last sync): the dispatch stays in Sync as “needs review” with the server's reason.

## 8. Lookup

What it does: a single field to look up balances, by product (barcode or SKU, already synced), by **bin** or by free
text: `GET /api/v1/inventory/balances`. It keeps the last response of each code looked up (`balance_cache`, local) so it can
answer "from N minutes ago" if the same search is repeated without signal.

Who can: `inventory.view` (module **WMS_LOTSERIAL**). The **system quantities in a bin's list** are only seen by whoever
has `warehouse.count` (§8.1). The search by product and the free-text search still show on hand and available to anyone with
`inventory.view`, as before this batch (pending decision of the owner, `docs/mobile/loteA8-decisiones.md`).

How to use it: scan or type a code (scanning = OK, §1.1). The app decides, in this order:
1. **Product** already synced: that product's balance in the whole warehouse, one row per bin and lot, with what is on hand and
   available (as always).
2. Warehouse **bin**: the list of what the system says is in it (§8.1, Batch A8).
3. Neither: it is sent as free text and the server searches by SKU, name, barcode or lot (title `Results for
   “{code}”`); each row shows bin, lot (if applicable), on hand and available.

### 8.1 What is in a bin (Batch A8)

What for: on high racks the products cannot be reached to scan or have no label; the operator scans the **bin
label** (or types it with ⌨) and sees **live** which products it holds according to the system, without depending on the bin's printed sheet
(Batch 23/F15) being up to date.

How it works:
- The bin is recognized first among the **synced bins** of the device's warehouse (no signal is needed for that) and, if it is not
  there, the server is asked (`GET /api/v1/warehouses/{id}/bins`, exact code, case-insensitive). The balances are requested **by
  bin** (`binIds`), not by free text: a bin `A-01` no longer also brings what is in `A-01-01`.
- Title `What's in {bin}` and below `{n} products in this bin` (or `1 product in this bin`).
- **One row per product**: the **SKU in large print**, the name and, if lot-tracked, `Lot A, B` (with more than three: `Lot A, B, C and 2 more`). The
  lots are only displayed. If the product is in several lots, there is a single row (the lots are added up).
- **Quantities**: whoever has `warehouse.count` sees below `On hand: {n} · Available: {n}` (sum of the lots, with the company's
  separators). **Whoever does not have it sees no quantity**, same as in the blind count. The app knows this by asking the server who
  signed in (`GET /api/v1/me`) when opening Lookup; it stores it on the device per user to decide without signal. Until it knows (first time
  without signal), it shows no quantities.
- With **more than 6 products**, **Search product or lot** appears (it filters by SKU, name or lot; the same rule as the "Count by
  product" list). The list scrolls with the screen.
- The rows **are not tappable** (Lookup had no action on a row; to see a product across the whole warehouse, scan its code).
- Without signal: if that same bin was already looked up before on the device, that list is shown with the notice `Data from {time} ({N}
  min ago, no signal now)`.

The other screens that scan a bin **do not change**: in Count by bin, the "What's expected here" list already shows the
bin's products (and tapping one fills the field, §7); in Putaway, direct Receive and Dispatch, scanning the bin is the action itself
(completing the task or adding the line), not a lookup.

### Fields and validations

| Field / case | Exact message | Source |
|---|---|---|
| It is not a product, nor a bin, and the server finds nothing with that text | `No product or bin has that code.` (Batch A8; before `Nothing matches that code.`) | App + API, empty list |
| The bin exists but has nothing | `There are no products in this bin.` (not an error: it appears under `What's in {bin}`) | API, empty list |
| The bin only exists deactivated | `Bin {bin} is deactivated.` | App (synced bins) + API |
| The list's search box with no matches | `No product matches “{text}”.` | App |
| Bin with more than 1,000 balance rows | `Showing the first 1,000 rows of this bin; see the rest on the web.` | App |
| No signal and no earlier lookup of that same code (or that bin) | `No signal and no earlier lookup of this.` | Local (empty cache for that key) |
| No signal, but there is an earlier lookup stored | `Data from {time} ({N} min ago, no signal now)` (Batch A3: with the time in the company's time zone and format; if not from today, with the date), with the data of that earlier lookup | Local (`balance_cache`) |
| Another server error (403, 500…) | The server message as is | API |

The cache key is per warehouse and per exact code (uppercase, no extra spaces): looking up the same product
by two different codes (SKU once, barcode another time) stores two separate entries. A bin's list is stored separately
from the free search of the same text.

---

### Tappable suggested bin and bin list (2026-10-07)

- **Quantities:** in every quantity field the value it comes with (for example the 1) appears **selected** on entry: whatever you type replaces it, without having to delete.
- **List line:** it now says `100 × 00050-7` (quantity, “×”, SKU) so the quantity is not confused with the product number.
- **Dispatch and direct Receiving — the suggested one is tappable:** the “Suggested: X” card is a button. If the quantity fits in that bin, tapping it puts its code in the bin field and the **OK** key is ready (scanning still adds the line instantly). If it does not fit, the card says `Not enough in this one: N short. Mark more positions below.` and tapping it **marks** it in the list.
- **Bin list:** it appears only when the quantity does not fit in the suggested one; with “Show other positions” it also opens when it does fit. Dispatch lists the bins with stock (with lot and expiration); direct Receiving, the bins the system proposes with their free space. The **suggested** ones (those that together complete the quantity) come first and in a different blue. Tapping one marks it with what is missing (up to its stock or free space); the **Taken X of Y** counter goes up; you can never mark more than the total. If the quantity changes, the marks are trimmed on their own. “Mark the suggested ones” marks all the suggested ones at once. **Use these positions** (Dispatch) or **Receive into these positions** (Receiving) is enabled when what is taken equals the total and adds one line per bin.
- **With lot** (Dispatch): no list; the bin is dictated by the lot that expires first. In direct Receiving, serial-tracked products and the “quantity per bin” mode do not show this list.

### Damage (2026-10-08)

**Damage** tile on Home (only with the `warehouse.damage` permission). **Needs signal**: it moves inventory on the server, with no queue.
1. Choose **Damaged in the warehouse** (scan the bin where it is) or **Arrived damaged in a receipt** (type or scan the receipt number, for example `REC-00012`).
2. Scan the product (or look it up in the list), type the **damaged quantity** (and the **lot** if the product is lot-tracked), choose the **cause** (informational) and tap **Send to quarantine** or **Release** (the latter first asks **Where does it go?** —Thrown away, Returned to supplier, Donated or Sold as salvage— and then asks to confirm: `Release right away?` / `{qty} of {sku} will leave inventory. This cannot be undone.`).
3. It shows `DAN-00012: sent to quarantine.` or `DAN-00012: release recorded.`.
- Notices: `I could not find that position in this warehouse.`, `I could not find that receipt in this warehouse.`, `I could not find that product.`, `Type a quantity greater than 0.`, `Type the lot.`, `Serial products are reported with an inventory adjustment (reason Damage) from the web.`, `Reporting damage needs signal. Try again with a connection.`; the server's (insufficient inventory, no quarantine bin…) appear as is (see chapter 06, §4b).
- Releasing what was left in quarantine (with its final destination: thrown away, returned to supplier, donated, sold as salvage) or recovering it is done on the web (Products & inventory → Damage tab).
- If the damage is discovered **when receiving**, this tile is not needed: check “Some units arrived damaged” in the receipt capture (at the top, “Units damaged on receipt”).

### Sign out, lock and remove a company (2026-10-07)

- **Sign out** (before “Switch user”): leaves the user; the next one picks their name and types their PIN. What was captured and is pending to send is kept.
- **Lock** (small padlock next to the name on Home): the session continues, but the screen asks for the same user's PIN. **The PIN is also asked every time the app opens**, even if a session was left saved. Wrong PIN: server message (`Wrong PIN.`, 401; 5 failures in a row → `PIN locked for 15 minutes.`, 423). On that screen, “Sign out” leaves completely.
  **Without signal (2026-10-11):** if the device cannot reach the server, the PIN is checked against a fingerprint stored on the device itself the last time someone signed in with signal (the PIN is not stored as is: only a salted fingerprint, in the system's encrypted store). Messages: `Wrong PIN (offline).` (attempts remain), `Too many offline attempts. Connect to unlock.` (after 5 failures in a row offline; with signal it is validated again on the server) and `No signal. Connect once to be able to unlock offline.` (the device never signed in with signal with that user). If the PIN is changed on the server, offline the previous one still works until the next sign-in with signal.
- **Remove a company from this phone**: in “Which company are you working for?”, swipe the row to the left (or long-press) → “Remove”. It asks for confirmation (`Remove {company} from this phone?`) and, if it has unsent captures, warns how many will be lost. It deletes its enrollment and its local database; the other companies remain.
- **Company the server no longer recognizes** (for example, the database was reset or the device was deactivated): when entering it the server responds 401 `The device is not enrolled or was deactivated.` and the app **removes it on its own, without asking**. With no company enrolled, the app goes to the enrollment screen.

## 9. Sync

**Purchase orders without permission (2026-10-01).** The data download for Receive includes purchase orders, which require
`purchasing.view` and the Purchases module. A user without that permission (for example, the Warehouse operator since the purchasing
permissions were removed from it) gets 403 on that resource: the app **skips it** (it does not receive against purchase orders, but it does against notices and blind
receipt), deletes the orders it had stored and **continues** with notices and bins. Before, that 403 aborted the whole pass and
Home always showed "Could not sync". A network or server error (500) still marks the pass as failed.

What it does: shows what is in the outbound queue, split into **pending** (they will be sent on their own in the next
pass) and **with errors** (the server rejected them; they have to be reviewed), the result of the last pass, and
"Sync now" to force a pass immediately.

Who can: anyone who signed in with a PIN; it does not depend on any fine-grained permission (it only shows what is already stored
on this device).

How to use it: each "with errors" row shows the operation type and the message the server returned; you can
"Retry" (it becomes pending again and is sent in the next pass) or "Discard" (it is deleted without ever being sent).
Since 2026-10-11 the type is shown with its name in the app's language (before, the queue's technical name appeared, e.g.
`manualIssue`), also in the `Retry: {type}` button:

| Technical name (queue) | Spanish | English |
|---|---|---|
| `receipt` | Recibo | Receipt |
| `pack` | Despacho (recolectar y empacar) | Dispatch (pick and pack) |
| `collect` (only rows from earlier versions) | Despacho (solo recolectar) | Dispatch (pick only) |
| `countBatch` | Conteo (líneas capturadas) | Count (captured lines) |
| `countFinish` | Conteo (cierre) | Count (finish) |
| `transfer` | Transferencia | Transfer |
| `adjust` | Ajuste de inventario | Inventory adjustment |
| `damage` | Reporte de daño | Damage report |
| `manualIssue` | Despacho manual | Manual issue |

A type this version does not know (a row stored by another version of the app) is shown with its technical name as is.
The pending ones do not have those actions: they are sent on their own when there is signal (every 60 seconds, or when tapping
"Sync now").

**Last time** (Batch A3): the time of the last pass, in the company's time zone and 12- or 24-hour format (with the date
if it was not today).

**Scanner** (Batch A3): a line says how the Zebra's scanner profile turned out (§1.1), according to what DataWedge answered:

| Indicator | What it means | What to do |
|---|---|---|
| `Scanner: ready` (green) | DataWedge confirmed that the app's active profile is `TeikemAlmacen` | Nothing |
| `Scanner: no profile` (red) | DataWedge refused to create the profile or the app is using another profile (the detail says which, e.g. `active: Profile0`) | Tap `Set up the scanner again`; if it persists, check the profile in DataWedge (see FAQ) |
| `Scanner: not confirmed` | The profile was requested but DataWedge did not answer (yet) | Tap `Set up the scanner again`, exit and come back; if it persists, contact support |
| `Scanner: not a Zebra (keyboard in use)` | The device does not have DataWedge | Nothing: you type or use the keyboard |

**Formats** (Batch A3): on every pass with signal the company's region and formats are also fetched (§1.2).

**What happens on each pass with signal (Batch 16).** In order: (1) the outbound queue is sent; (2) if there was signal, the **heartbeat** is sent (liveness notice, chapter 8A §2.2), which updates the
default warehouse, the theme and the **receiving mode** of the device's warehouse; if the server responds that the device is deactivated, the device identity is erased (it has to be enrolled again); (2b, Batch A3) the company's region and formats are fetched; (3) products, purchase orders, arrival notices and **the bins of the device's default warehouse** are downloaded. Before Batch 16 nobody called the heartbeat.
The bins are used to validate offline the destination bin of the direct receipt: the first time the full list is downloaded (at Advance Depot, about 3,886 bins, in 8 pages) and afterwards only the changes;
deactivated bins are kept flagged as inactive (so the app distinguishes "does not exist" from "is deactivated"). If the device's default warehouse changes, the new one is downloaded in full the first time.

**Manual issue (2026-10-11).** At the end of each pass with signal the **manual issue reasons** (§6.y) are also downloaded, at most every
30 minutes and only if the user has `warehouse.issue` (since 2026-10-11 (b) with the default reason flag; an old copy without it is downloaded
again on the next pass). In addition, since this version a queue operation ends up **with errors** when the server
rejects it with 400, 403, 404, 409 or 422, even if it carries its own code: before, an `Insufficient inventory…` (409 `insufficient_stock`) or a disabled
module (403 `module_disabled`) left the operation **pending forever** (it was retried every minute) and its effect on the device's balance
not undone; this also applied to Transfer, Adjust and Pack. A 500 or lack of signal still leave it pending.

**Fixes after the manual issue (2026-10-11).**
- **409 `The operation with this key is still being processed.`** (idempotency, chapter 8A §1) is **not** a rejection: it means the server
  is still processing that same submission with the same key (e.g. the previous attempt was cut off without a response). The operation **stays pending**, with that
  text as the last error, the attempt is counted, its effect on the device's balance is **not** undone and the queue continues with the rest; on the next
  pass it is sent again with the **same** key and receives the stored response (or it is executed if the server considered it abandoned after 10 minutes),
  without duplicating. Before this version it ended up "with errors" and the balance was undone. The other idempotency 409s
  (`The idempotency key was already used with different content.`, `The operation with this key can no longer be repeated with the current permissions.`) are still
  rejections.
- **403 without a message** (what the API responds when the role lacks the route's permission): throughout the app it reads
  `You do not have permission for this action. Ask your administrator to grant it.` (in Spanish `No tienes permiso para esta acción. Pide a tu
  administrador que te lo asigne.`), also as the message of the "with errors" row in Sync. Before it read `Something went wrong. Try again.`.
  A 403 that carries its own message (e.g. a disabled module, `module_disabled`) still shows the server message as is.

### 9.1 Line already corrected by the supervisor: partial batch notice (Batch A7) and rejection (Batch A6)

What happens: when finishing a count, the app queues **the captured batch** (`countBatch`, `PUT /api/v1/cycle-counts/{id}/lines/batch`) and
**the closure** (`countFinish`). If in the meantime a supervisor **corrected** on the web a line the operator sends again with another
number, since the owner's second block of decisions (2026-10-03) the server **saves the free lines and skips the corrected ones**
(chapter [06](06-inventario-y-almacen.md), protection of the correction): it responds 200 with `skippedLines`. The submission stays **sent** (it is
not an error) and the count closure goes on its way. Only if **all** the lines of the batch are corrected does the server respond **409**
`The line was already corrected by the supervisor; it cannot be captured again. Batch row(s): n (SKU). Nothing was saved.`; then
the row moves to "With errors" and **is not retried on its own** (normally the closure of the same count also ends up with an error right below: 422
`{n} line(s) still to count.` if the count was still Pending, or `The count was already finished; you can correct the capture or reconcile it.` if
it was already Counted).

Who sees it: anyone who signed in with a PIN on that device (Sync does not depend on permissions). "Refresh the count" queries
`GET /api/v1/cycle-counts/{id}` with the permission of whoever is signed in (`warehouse.count.capture` or `warehouse.count`, module
**WMS_LOTSERIAL**).

**Partial batch notice (the common case).** It is stored on the device (it is not lost if the app is closed) and shown in Sync,
above the pending list, until **Dismiss this notice** is tapped:

| Part | Exact text (en) |
|---|---|
| Title | `The rest of the lines of your count were saved.` |
| Introduction | `These were not saved because the supervisor already corrected them:` |
| One per line | `• {SKU} · {bin} (you sent {x} → the supervisor left {y})`; with lot: `• {SKU} · lot {L} · {bin} (…)`; without a current value: `(you sent {x}; the supervisor already corrected it)` |
| Clarification | `You do not need to do anything with them. If something is still missing, create a new count or ask the supervisor to review it.` |

Buttons: **Refresh the count** (the same one as below) and **Dismiss this notice**. It shows the quantity the supervisor left even if the count
is blind; **never** the expected quantity. The app **does not reopen a count already sent**: if something is still missing, a new count is created
(scan the bin again).

**Rejection (residual 409: all the lines of the batch corrected).** What the card shows (instead of the usual short row):

| Part | Exact text (en) |
|---|---|
| Big red notice | `The supervisor already corrected all the lines of this submission.` |
| Below | `No line of this submission was saved because the supervisor already corrected all of them.` |
| Rows (if the server message brings the list in the expected format) | `Lines the supervisor already corrected:` and one per row: `• Row {n}: {SKU} (you sent {quantity})` (the quantity is the one this device sent in that row) |
| If the list cannot be read safely (for example a SKU with parentheses) | `What the server said:` and the message as is |
| What to do | `If something is still missing, create a new count (scan the position again) or ask the supervisor to review it.` |
| Clarification | `This app cannot reopen a count that was already sent. Tap “Refresh the count” to see where it stands.` |
| If the closure of the same count also has an error | `Closing this same count also failed: the count was not finished from this device.` |
| If it cannot tell which count it belongs to | `Could not tell which count this submission belongs to; ask the supervisor to review it.` (no refresh button) |

Buttons: **Refresh the count** (it never runs on its own), **Retry: countBatch** (sends the same batch again; it fails the same
while the correction is still there) and **Discard this submission** (deletes the row from the device without sending it; the closure row is discarded
separately with its own "Discard").

Result of **Refresh the count**:

| Case | Exact text (en) |
|---|---|
| Still open (Pending) | `Count {number} is still open (Pending).` + `{total} lines: {c} corrected by the supervisor, {s} not counted.` + the list of lines |
| Already finished (Counted) | `Count {number} was already finished (Counted): ask the supervisor to review it.` + summary and lines |
| Reconciled | `Count {number} was already reconciled: it takes no more captures. You can discard this submission.` |
| No longer exists (404 or deleted) | `The count no longer exists (it was deleted). You can discard this submission.` |
| No signal | `No signal: the count could not be refreshed. Try again when there is a connection.` |
| Another server error (403, 5xx…) | The server message as is |

Each line says `{SKU} · {bin}` (and the lot if it has one) and its state: `Counted: {n}`, `Not counted`, or `Corrected by the
supervisor ({name}) · Counted: {n}`. **It never shows the expected quantity** (what the system had), even if whoever is signed in has
`warehouse.count`: only the current counted quantity and who corrected it. With more than 30 lines the first 30 are shown and `… and {n} more lines
(see them on the web).`

### Frequent cases

An operation ends up "with errors" when the server rejected it for a reason that retrying it as is does not fix
(invalid data, a document that no longer exists, a permission that changed) — never for lack of signal, that leaves it
"pending" instead. Before retrying, it is worth reviewing the message: if the problem persists (for example, "this
bin no longer has that product"), retrying will fail again with the same error.

---

## 10. Permissions used by the app (summary)

| Permission | What it enables in the app |
|---|---|
| `inventory.view` | Signing in with a PIN (chapter 8A §3.3), Receive (including the "Suggested" hint of the direct receipt and the bin download), Putaway (listing/completing tasks), Count (viewing, not reconciling; zones of "Another bin"), Lookup (including the list of what is in a bin, without quantities) |
| `purchasing.receive` + module **PURCHASING** | Receiving against a purchase order (without this, only blind receipt works) |
| `warehouse.pick` | Picking and packing in Dispatch (packing also requires `orders.create` on the server) |
| `warehouse.issue` | **Complete dispatch** in Dispatch = manual issue `DMA-#####` (exit without a delivery with a reason) and downloading its reasons (2026-10-11) |
| `locations.read` | Looking up a client's consignees when packing a dispatch |
| `clients.read` | Choosing the client when packing a dispatch of own inventory (2026-10-05) |
| `warehouse.count.capture` (implied by `warehouse.count`) | Opening, capturing and finishing a count, by bin or by product (blind if `warehouse.count` is missing), and creating a provisional bin in "Another bin" |
| `warehouse.count` | Seeing the expected quantities of a count (not blind) and **cancelling it**; in Lookup, seeing `On hand` and `Available` in a bin's list (Batch A8) |

All these routes live under the **WMS_LOTSERIAL** module (chapter 8A §7); without the module enabled for the
company, the device cannot even list its users to sign in.

---

## Branding

Since batch F8a the app carries the Teikem branding: icon (with a monochrome version for Android 13+ themes), blue `#0B2C66` splash screen and the logo with the tagline in the active language above Register and Sign in ([icon](frontend/img/f8a-app-icono.png), [splash](frontend/img/f8a-app-splash.png), [Register](frontend/img/f8a-app-registrar.png)); the files are regenerated with `npm run brand:icons` from `Logos/`.

---

## 11. Frequently asked questions

See the **Batch 8A** section of [`faq.md`](faq.md) for the backend (devices, PIN, idempotency, sync) and
the app's own cases added in this batch (what happens if the signal is lost halfway through a receipt, why
Dispatch only works with 3PL clients, what "with errors" means in Sync), and the **Batch A3** section (Zebra
scanner, keyboard, company formats in the app), the **Batch A4** section (count by product) and the **Batch A5** section (at least
one quantity when counting by product; in Dispatch, the quantity before the bin), the **Batch A6** section (count capture rejected
because the supervisor already corrected a line) and the **Batch A8** section (what is in a bin in Lookup).

### Counting a bin the system believes empty (2026-10-09)

When scanning a bin with no stock in the system, the app **no longer rejects** with *“The filters select no on-hand inventory to count; widen the filters or add lines by hand.”*: it opens the empty count for that bin and you scan or choose from the list what you find (the “What's expected here” section says the system expects nothing). It is finished with **Finish this bin** as always; when reconciling on the web, what was found enters as an inbound adjustment in that bin. (API: `POST /api/v1/cycle-counts` with `binIds` of a single bin and `allowEmpty = true`.)

## Weak signal: the screen first shows what the device already has (2026-10-10)

**What changed.** Before, several screens waited for the server's response to show anything, and with a weak signal they hung. Now the device
also stores the **balances per bin of the active warehouse** and the screen **immediately shows what it already knows**; in parallel it queries the server and,
when it arrives, **updates what is shown**. A weak signal only delays the update, not the result.

**The indicator.** While the device catches up, a spinner with **“Refreshing…”** is shown; when done it says **“✓ Up to date”**; if the server could not be reached
(no connection or weak signal) it says **“No connection or weak signal: showing the device's data.”** and below it the time of that data
(**“Device data from {time} ({n} min ago)”**). On **Home** the same indicator appears while the general sync runs.

**Where it applies today.**
- **Lookup:** product, bin and free search come up instantly from local data and update on their own.
- **Transfer, Adjust and Damage (D2b): they are saved on the device and sent on their own.** When confirming, the operation enters the **outbound queue** (like Receive
  or Dispatch): the balance the device shows **already reflects the movement** and the screen does not wait for the server. The device tries to send it immediately (it waits
  up to 3 seconds); if there is signal, it is sent and says so; if not, it stays **“Saved on the device. It will be sent as soon as there is signal.”** and goes up on its own with
  the sync of every minute (with its `Idempotency-Key`, so a retry never duplicates the movement). If the server **rejects** it (for example,
  someone else already moved that stock, or the bin changed zone), the device **undoes the effect** on its balances, warns **“The server did not accept the
  operation”** with the reason and leaves it in **Sync** as “needs review” (retry or discard); it does not stop the rest of the queue. In **Damage** the
  effect is not projected on the device's balances (the server decides the quarantine bin and the reservations): the balance is corrected in the next sync.
  The list of what is in the source bin comes from local data and catches up in the background.
- **Count, Damage, Dispatch:** the scanned bin is recognized from what the device already has (without waiting for the network); if it is not there (for example, it was
  created after the last sync), the server is asked. **Opening a count by bin now works without signal** (section 7.7); the count by product and the “open count” still need signal to open. **Damage that arrived damaged in a receipt** needs signal only to look up the receipt by its number.

**When the balances are downloaded.** In the sync of every minute, together with products and bins: the first time it downloads everything that has stock in
the active warehouse; afterwards only what changed (`GET /api/v1/sync/balances`). If the device has never downloaded balances for that warehouse, the screens
behave as before (they wait for the server). When the app is updated, the local database migrates on its own to version 10; nothing is lost.

**What to do if a value looks stale.** The device's balance may be a few minutes behind. Tap **Sync now** on Home, or wait for the
indicator to say “Up to date”. The server is always in charge: if what is shown no longer matches, the server corrects it when it arrives.

## Transfer (2026-10-10)

**What it does.** Moves inventory from one bin to another **in the same warehouse** from the device. Steps: source bin → product → lot (if the product has one) → quantity → destination bin → *Transfer*.

**Who can.** Permission `warehouse.transfer` (inventory module). The *Warehouse operator* role includes it, as does anyone who already has `inventory.adjust` (it implies it). Without the permission the button does not appear.

**How to use it.**
- **There is no longer a “Transfer” button in the main menu (since 2026-10-11): you transfer with “Move” in Lookup** (it did exactly the same, with the same steps and the same permission).
- **Lookup → “Move”.** When looking up a bin, each product with something available has *Move*; when looking up a product, each row with a bin and available quantity has *Move*. It opens Transfer with the source bin and the product already set.
- Needs signal (it moves inventory on the server, with no queue; like Damage). With the quantity calculator it also counts by cases if the product has packaging.

**Rules and messages.**

| Case | Message (HTTP) |
|---|---|
| Quantity greater than what can be moved | `Only {qty} can be moved (reserved stock does not move).` (screen); the server: 409 `insufficient_stock` |
| Source = destination | `The destination is the same as the source bin.` (screen) / `The source bin and the destination bin are the same.` (400) |
| Another warehouse | `From the device you can only transfer within the same warehouse.` (400) |
| Quarantine, rental or cross-dock bin | `Bin {bin} is a quarantine, rental or cross-dock bin: it is not transferred from here (use Damage, Rentals or Cross-dock).` (422) |
| Nonexistent / inactive bin | `No bin with that code in this warehouse.` / `Bin {bin} is inactive.` |
| Product with serial number | `{sku} is serial-tracked: for now it is transferred from the web.` |
| No signal | `Transfer needs a signal: it moves stock on the server. Try again with signal.` |

API: `POST /api/v1/inventory/transfers/in-warehouse` (`warehouse.transfer`); the web transfer (`POST /inventory/transfers`, `inventory.adjust`) does not change. The movement ledger records it as TRANSFER.

**Main menu.** Button order (2026-10-11): Lookup, **Adjust** (only with `warehouse.adjust`), Receive, Putaway, Dispatch, Count and Damage (only with `warehouse.damage`).

## Adjust quantity (2026-10-10)

**What it does.** Changes **only the quantity** of a bin: raises or lowers the stock of that product there. **It does not move anything from one bin to another**: that is what Transfer is for. In the movement ledger it is recorded as ADJUSTMENT, with the reason, the note and the name of the person who did it.

**Who can.** Permission `warehouse.adjust` (inventory). **No role template includes it**: the administrator grants it to a custom role (System → Roles) and assigns it to whoever must adjust. The Company administrator has it through "all". Without the permission the button does not appear.

**How to use it.** There are two entry points:
- **Main menu → Adjust** (2026-10-11): scan the **bin** (*Scan the bin where it is*) and then the **product** (*Scan the product*). It is useful above all when the product **is seen in a bin and the system says there are 0 there (or it was never there)**: Lookup does not list it and, before, there was no way to adjust it from the device. Here it is **increased** with the reason *Found*. When finished, the screen **stays in Adjust** with the notice *Done: Increase 4 of TOR-1 in A-01-01.* and asks for the next bin; **Start over** returns to the first step and **Back** exits to Home.
- **Lookup → “Adjust”**: when looking up a bin, each product has *Adjust*; when looking up a product, each row with a bin has it. The screen opens with the bin and the product already filled in, and when finished it returns to Lookup.

From there, the same in both entry points:
1. (if the product has several lots) choose the lot;
2. **Increase** (there is more) or **Decrease** (there is less);
3. quantity (with the calculator; also by boxes if the product has packaging);
4. **reason**: when increasing, *Found* or *Other*; when decreasing, *Damage*, *Loss*, *Expired* or *Other* (the reasons for receipt, count, opening balance, etc. are set only by the system);
5. **mandatory note** (up to 300 characters);
6. *Adjust* and confirm. If the user has permission to see system quantities (`warehouse.count`), the confirmation says “from X to Y”.

**Cases of the “Adjust” menu (2026-10-11):**

| Case | What the app says |
|---|---|
| The bin does not exist or does not belong to the warehouse | `No bin matches that code.` |
| The bin is deactivated | `Bin {bin} is inactive.` |
| The product does not exist | `No product matches that code.` |
| Serial-tracked product | `{sku} is serial-tracked: for now it is adjusted from the web.` |
| **Lot-tracked** product with no balance in that bin | Offers the **lots it already has registered** in the warehouse (if there is only one, it uses it); the adjustment is made for that lot |
| Lot-tracked product that **has no lot yet** | `{sku} is lot-tracked and has no lots registered yet: adjust it from the web.` |
| **Decrease** more than there is (with 0 in the bin, any quantity) | `Can only be lowered by up to {n} (reserved stock does not leave).` |
| Quarantine, rental or cross-dock bin | `{bin} is a quarantine, rental or cross-dock bin: it cannot be adjusted from here (use Damage, Rentals or Cross-dock).` If the bin is empty the app cannot know it when scanning it and the **server** rejects it when the adjustment is sent (422), which stays as “needs review” in Sync. |

Needs a signal (it changes the inventory on the server, no queue).

**Rules and messages.**

| Case | Message (HTTP) |
|---|---|
| Decrease more than available | `Can only be lowered by up to {qty} (reserved stock does not leave).` (screen); server: 409 `insufficient_stock` |
| No note | `Write a note explaining the adjustment.` (400) |
| Quantity 0 or empty | `Enter a quantity greater than 0.` (screen) |
| Reason reserved for the system | 400 (not offered on the screen) |
| Quarantine, rental or cross-dock bin | `Bin {bin} is a quarantine, rental or cross-dock bin: it cannot be adjusted from here (use Damage, Rentals or Cross-dock).` (422) |
| Serial-tracked product | `{sku} is serial-tracked: for now it is adjusted from the web.` |
| No signal | `Adjusting needs a signal: it changes stock on the server. Try again with signal.` |

API: `POST /api/v1/inventory/adjustments/quantity` (`warehouse.adjust`); the web adjustment (`POST /inventory/adjustments`, `inventory.adjust`) does not change.

### 7.6 When a count is finished the app stays in Count (2026-10-10)

Before, **Finish this bin** and **Finish count** sent you to Home and you had to enter Count again for the next bin. Now, when finishing:
- The screen **stays in Count**, on its starting screen (“How will you count?”), with the **same way of counting** that was being used (the app remembers it) and the **cursor in the scan field** (the bin, in “By bin”; the product, in “By product”). At the top it says `Count sent. You can count something else; “Back” takes you to Home.`
- To exit to Home, tap **Back** (at the bottom of that screen), as always.
- The closing still goes to the outbound queue and works without signal; if the server rejects it, the notice `The server did not accept finishing the count: …` appears all the same.
- The count just finished **does not block** the next one: it no longer counts as “open” (section 7.5).
- **Save and continue later** does not change: it saves, notifies and returns to Home (the count stays open and blocks everything else until it is resumed).

With the company setting **“Close matching counts automatically”** turned on ([06 §6.9](06-inventario-y-almacen.md#69-automatic-close-on-finishing-if-the-count-matches-2026-10-10)), each bin that matches goes to Matched as soon as it is sent, and only those with a difference wait in “To review”.


## 12. Dispatch a sales order (Batch 65)

Route `/sales-dispatch`; **Sales orders** button on Home (only if the device has orders to dispatch; see [18 §14](18-ordenes-de-venta.md#14-on-the-device-dispatching-a-sales-order-batch-65)).

- **Local data:** schema 14 (`sales_order`, `sales_order_line`, `sales_order_reservation`). Sync downloads `GET /sync/sales-orders` **in full** (all the open ones of the warehouse) and replaces the copy; without the module or without `sales.view` (403) it is skipped and the previous data is deleted. Pending dispatches in the queue are reflected again on the copy that arrives.
- **Outbound queue:** type `salesDispatch`, `POST /api/v1/sales-orders/{publicId}/dispatch` with `{ lines: [{ lineId, quantity, binId, lotId? }], delivery? }`. From another bin the lot is not sent. With delivery: `{ serviceType: "STANDARD", packages: [{ packageType: "BOX", pieces }], confirmNow: true }` (the same standard values of the Dispatch packing). Local effect saved with the row (stock of the bin; FEFO within the bin if it is not lot-tracked) to undo it if the server rejects it.
- **Screen messages:** `That order is not waiting to be dispatched in this warehouse. If it is new, tap Sync now.` · `Enter a quantity greater than 0.` · `You cannot take more than reserved ({qty}).` · `That bin does not exist in this warehouse.` · `Dispatch sent: EMP-#####.` · `Dispatch saved: it is sent when there is signal.` Server rejections are shown with their exact text (see [18 §10](18-ordenes-de-venta.md#10-dispatching-against-the-order-batch-61)).
- **One at a time:** the dispatch on screen does not survive closing the app (nothing has left yet; you start again). What was already sent to the queue is saved.

### 12.1 Kits in the device dispatch (Batch 71)

A **kit** line is ordered in kits, but what is reserved belongs to its **components**. On the device each reservation of a component is a **task** (“Go to A-01”, with the note “Piece of kit KIT-1 (2 per kit)”): you pick the
piece, scan the bin and it gets ✓, just like a normal product.

- **When finishing** the device counts the **complete kits** that can be made with what was picked: the lowest quotient (picked ÷ pieces per kit) among the components, rounded down; the summary says `Kit KIT-1: N complete`.
  **One line per kit** is sent to the server — `{ "lineId": …, "quantity": N }` (kits), **without bin or lot**; it takes the components from what they have reserved.
- If what was picked **does not complete any kit** of a line (for example, one piece is missing), it is not sent and a notice appears: `What you picked does not complete any {KIT} kit. Pick the missing pieces or remove them.`
  The extra pieces picked (that do not complete another kit) stay reserved.
- **No signal:** same as the rest: it stays in the queue; the local stock of each component decreases by what leaves its reservations and the order deducts the dispatched kits; if the server rejects it, everything is restored.
- Local data (schema 15): `sales_order_line.is_kit`, `sales_order_component` (pieces per kit) and, per reservation, the component (`product_public_id`, `sku`, `product_name`).
- Several reservations of the same product in the same bin and lot are **a single task** (they add up).

## 13. Receive a sales return (Batch 83)

Route `/sales-return`; **Returns** button on Home (only if the device has authorized returns to receive **and** the user has `warehouse.receive`; see [20 §5](20-devoluciones-de-venta.md#5-en-el-aparato-recibir-una-devolucion-lote-83)).

- **Local data:** schema 16 (`sales_return`, `sales_return_line`). Sync downloads `GET /sync/sales-returns` **in full** (the authorized ones, or partially received with something pending, of the warehouse) and replaces the copy; without the module or without `warehouse.receive` (403) it is skipped and the previous data is deleted. Pending receipts in the queue are reflected again on the copy that arrives.
- **The screen:** the list of returns (number, client, order, how much is left) or the scan of the `DV-…` number (the final digits also work). When opening one, **each line with a pending quantity** has: **Arrived good** (by default, everything pending), **Arrived damaged**, the **Lot** (only if the product is lot-tracked) and, if there is damage, **What happened** (mandatory) and **Discard the damaged items** (does not go to quarantine). **Close what is pending** treats what did not arrive as not returned. **Receive** sends it.
- **Outbound queue:** type `salesReturnReceive`, `POST /api/v1/sales-returns/{publicId}/receive` with `{ lines: [{ lineId, goodQty, damagedQty, damageNote?, damageDiscard?, lot?: { number } }], closeRest? }`. Immediate local effect: it lowers what is pending in the copy and, with nothing pending, the return leaves the list. **The stock of the bins is not projected**: the server decides where it goes in (receiving zone with putaway tasks, or direct, depending on the warehouse mode) and it arrives with the next sync.
- **Screen messages:** `That return is not waiting to be received in this warehouse. If it is new, tap Sync now.` · `Enter what arrived on at least one line.` · `{SKU}: enter a valid quantity (up to 3 decimals).` · `{SKU}: you cannot receive more than what is pending ({n}).` · `{SKU}: describe what happened to the damaged items.` · `{SKU}: enter the lot.` · `Return received.` · `Receipt saved: it is sent when there is signal.` Server rejections are shown with their exact text (see [20 §3](20-devoluciones-de-venta.md#3-validations-and-messages)) and the row does not stay in Sync.
- **A single send per screen:** there is no draft (the receipt is captured and sent at once), so it does not block the other Home buttons.



## Receiving in the app: mandatory document, pending list and partial search (Batch 161, 2026-10-21)

**If the company requires a document** (Tenant settings → Operations → “Receiving requires a document”), the Receiving screen no longer offers the **blind receipt** and only offers what is allowed: arrival notice, purchase order, or both. The setting reaches the device with the enrollment and with each heartbeat (without signal the last one received is used). If a document of a type other than the required one is scanned: “The company requires receiving against an arrival notice, not a purchase order.” / “…against a purchase order, not an arrival notice.” The server keeps validating the same way (409 when syncing).

**Pending list.** Under the scan field appears “Pending notices and orders” (tabs Arrival notices / Purchase orders depending on what is allowed) with a **partial** search box (a piece of the reference, number, client, supplier or date). Tap a row to start the receipt against that document; “See more” brings another 30. It works without signal with the last data synced (if one is missing: “Sync if one is missing.”).

**Search product or bin.**
- The “Search the list” list **no longer has a limit of 50**: it keeps loading as you scroll and says how many matches there are.
- When **typing by hand** (⌨) in a product or bin field, up to 5 partial suggestions appear (SKU, name or barcode; code, zone, aisle, rack or level); tapping one is the same as scanning it and “See all” opens the list already filtered. There is no need to type the full code.
- In Lookup, stock results bring all the matches (pages of 200).

## My tasks (Batch 172, 2026-10-21)

**“My tasks”** card at the start of Home (with the count in parentheses, e.g. “My tasks (3)”, when there are assigned documents). It gathers what the supervisor **assigned to the signed-in user** in the active warehouse:

- **To receive:** client arrival notices assigned to you (`LLE-00012 · reference`, client and arrival date). Tapping one opens **Receive** with that notice already chosen (as if you had scanned it).
- **To dispatch:** exit notices assigned to you that already have something reserved (the rest do not come down to the device). Tapping one opens **Exit notice dispatch** with that notice.
- **To put away:** putaway tasks assigned to you; tapping opens **Putaway** (which already shows your own first). They are requested online and stay saved: without signal you see the list from the last time.
- **What nobody has been assigned:** button “Show what nobody has been assigned (N)”. Anyone with permission can **Take** one (arrival notices: `warehouse.receive`; exit notices: `warehouse.pick`); it is assigned to you on the spot. Taking needs a signal; if someone else already took the notice, `That document is already assigned to someone else; ask a supervisor to reassign it.` appears.

**What it does not do:** the assignment **orders** the work, it does not block it. Receive and Dispatch keep showing all the documents, and another user with permission can handle one assigned to you. With an open receipt, dispatch or count, “My tasks” tells you which one must be finished before opening another document (same as Home). The data comes from the device's local copy: what is assigned to you after the last sync appears when syncing. The notice when assigning (notification to the person) is left for a separate batch.

Assigning is done on the web: Receiving → Arrival notices / Exit notices, **Assign** button ([chapter 06](06-inventario-y-almacen.md), “Assign the arrival notice and the exit notice”).

## Receive a part (Batch 175, 2026-10-21)

**What for.** When the truck arrives in parts and the staging (the receiving bin) fills up, the operator can **receive a part**: what has been captured so far enters the staging and **can already be put away**, while the receipt stays open for what is missing. They are two separate things: receiving (entering the staging) and putting away (taking it to its bin).

**How to use it (Receive, with an arrival notice or a purchase order, warehouse “with putaway”).**
1. Scan the document and capture the lines of that part as always.
2. Press **“Receive a part”** (below “Confirm receipt”). It is queued just like a receipt (without signal it waits and is sent on its own) and the screen returns to Home.
3. On the server the part is **added** to the open receipt of that document (it creates it if it is the first) and is confirmed as a **batch**: it enters the inventory in the staging and the putaway tasks are created, which appear in **Putaway** on any device.
4. When the rest arrives, **another device (or another operator) can receive another part** of the same document, or close it with **“Confirm receipt”**: that send no longer creates a new receipt, **it closes the open receipt** adding what was captured and recording what is missing (shortage or surplus) as always.

**Staging full notice.** When capturing, if the staging of the receiving zone has a configured capacity and what was captured **does not fit** in its free space (calculated on the device with the synced balances), the screen warns: “What is captured (N) does not fit in staging (free: M). Receive a part and put it away before continuing.” It is only a notice: it does not block.

**The list of what is expected (Batch 191).** When receiving against a notice or a purchase order, below the scan field appears **“What the document says”**: one row per product with `Expected · Received · Left`. **Tap the product** to capture it (same as scanning it) or scan it as always. The ones already complete are dimmed with ✓. With a purchase order it shows what is left to receive. In a blind receipt there is no list. It works the same with putaway and direct to bin.

**What has already been received from the document (Batch 186).** With a notice that already has parts received, the Receive screen shows at the top **“Already received from this document”** with one line per product (`SKU-1: 4 of 10`). The data comes from the server when syncing (the batches confirmed by any device or by the web); without signal you see the data from the last sync. With a purchase order it is not shown (its progress already goes in “pending”).

**What a part does not allow.** There is no “Receive a part” without a document (blind receipt), in a “direct to bin” warehouse, (Batch 187) A part **can bring serials and damage**: serial-tracked products ask for **one serial for each unit** of the part (if any are missing, the server responds 400 `Capture one serial for each unit received before confirming the part.`) and the **declared damage is reported with that part** (DAN report, the damaged units leave the receiving bin and are not put away). The closing (“Confirm receipt”) does not repeat the serials or the damage already recorded.

**Retries.** Each part has its own key: if the send is repeated (the signal dropped while responding), the server recognizes the part and **does not add it twice**.

**Server.** `POST /api/v1/receipts/part` (permission `warehouse.receive`; with a purchase order, also `purchasing.receive`). Body: `asnId` **or** `purchaseOrderPublicId`, `lines` (product, quantity, lot), `clientKey`, `comment`. More in chapter 06 (“Receipt in batches”).

## Pick together: several exit notices, one master (Batch 178, 2026-10-21)

**What for.** When several exit notices **of the same client** leave together, they are picked in **a single route** and are gathered in a **master pick batch** (with a single shipment when packing, from the web). All the movements —from which bin each item left and for which notice— are seen in the master's record.

**How to use it (Dispatch exit notice, permission `warehouse.pick`).**
1. In the list of notices press **“Pick together”** (it appears if there are 2 or more notices to dispatch).
2. Tap the notices that leave together (☐ → ☑). Those of **another client are dimmed** and warn “It belongs to another client”: a master is for a single client. Press **“Pick together (N)”** (minimum 2).
3. The guided route mixes the bins of all the chosen notices (in the chosen order); at each step scan the bin and confirm the quantity, same as in a single notice. You can change bin or skip one.
4. **Finish → Dispatch.** Something must have been picked from **at least two** notices (if not, the device says so). **A single send** is queued (`POST /api/v1/pick-batches/groups`) that works without signal; the local balance and each notice are updated instantly. If the server rejects it (for example, insufficient inventory) its exact message is shown, nothing stays in the queue and the balances are restored; the notice is downloaded again as it is.
5. With signal, the device notifies: “Master pick batch EMP-##### sent.”

**Sales orders (Batch 186).** When choosing, besides the notices the **sales orders** with something reserved appear (labeled “Sales order”); they mix with the notices of the same client in the same route and travel in the same master. Kit lines come out of what is reserved for their components, same as when dispatching a single order.

**What it does not do yet.** Adding documents afterwards is not possible (it is decided when picking). The shipment is created when **packing the master on the web**.


## Dispatch: choose the product and who sees what (Batches 192 and 193, 2026-10-21)

**Choose the product.** When dispatching against a sales order or an exit notice (also in “Pick together”), below the current product appears **“Products to take”**: one row per product with its bin and quantity. **Tap one** to take it now (it is marked with ▶); when confirmed the route continues. Skipped ones can be resumed from there.

**Which documents each person sees.** It is controlled by the permission `warehouse.pick.any` (“Dispatch any document on the device (not only assigned ones)”), which is granted by role. With the permission, the device shows everything in the warehouse. Without it, only the exit notices assigned to the person (and “My tasks” does not offer what is unassigned); sales orders are not assigned, so they are not offered. The migration granted it to all roles that already had `warehouse.pick`.


## Assigned purchase and sales orders (Batch 195, 2026-10-21)

The purchase order (to whoever is going to **receive** it) and the sales order (to whoever is going to **dispatch** it) are also assigned to a person, same as the notices. In **My tasks** appear “Purchase orders to receive” and “Sales orders to dispatch”: tapping one opens Receive or Dispatch with that document. What is unassigned appears separately with **Take** (only with the permission `warehouse.pick.any`). Without that permission, **Dispatch → Sales orders** shows only those assigned to the person (purchase orders remain visible in Receive, like arrival notices: the assignment orders the receiving, it does not block it).

## Receive and dispatch several documents; take = assign to yourself (Batch 198, 2026-10-21)

**Receive.** The list “Receive against what?” shows first what is **assigned to you**, then what is **unassigned** and at the end what is assigned to someone else (dimmed and not openable). The **“Select several”** button lets you mark several notices or orders and tap **“Receive N selected”**: they are received one after another. If there are **two or more of the same owner** (same client, or all your own) a load opens on its own and the server gathers them in a **master receiving**; if they are of different owners they remain separate receipts. “Receive together (open load)” no longer exists: the load is automatic. A document of an owner other than those of the batch is rejected with “That document belongs to a different owner than this batch…”. Cancelling a receipt cancels the rest of the batch.

**Dispatch.** Home no longer has separate entries for “Sales orders” and “Exit notices”: **Dispatch** opens a single list with notices and orders, your own first. **“Select several”** (formerly “Pick together”) builds a master pick batch (same client, minimum 2). A sales order tapped on its own opens its screen (kits, consignee, delivery). “Dispatch without a document” leads to the manual issue.

**Take = assign to yourself.** Starting to receive, dispatch or put away something **unassigned** leaves it in your name (with signal; without signal it does not get in the way). The document's log records the reason (`$reason`), e.g. “Self-assigned when starting to receive it from the mobile app.”. If someone else already had it, it is not taken from them (409, “That document is already assigned to someone else…”).

**My tasks.** When opened with signal it downloads the documents from the server again, the title carries the count and the Home number includes the assigned putaways. **Putaway** shows each task as “Assigned to you”, “Unassigned” or “Assigned to someone else”.

**Web.** The Assign/Take icon of an arrival notice also appears when it already has a partial receipt.

**Adjustment (2026-10-21, same batch).** Receive and Dispatch follow the My tasks pattern: the list shows **only what is assigned to you** and, below, the option **“Show what nobody has been assigned (N)”** which unfolds what has no owner (what belongs to other people is not offered; scanning the code still opens any document). When opening My tasks, Receive or Dispatch with signal, the device downloads again in full the open arrival notices and orders, so what was assigned on the web appears at once. On **Home** a card (My tasks, Receive, Putaway, Dispatch) is painted blue only if there is something assigned to you in that area. In My tasks the order is **Receive, Putaway and Dispatch**.

**Receive — new order of screens (2026-10-21).** When tapping Receive only the number field appears at the top (scan or type, keyboard, OK), then the notices and orders assigned to you, and “Show what nobody has been assigned”. The mode is **no longer** asked when entering: if the warehouse has a **fixed mode** (With putaway or Direct to bin), the receipt opens at once with that mode; if it does not have one, when choosing one or several documents (or the blind receipt) **With putaway / Direct to bin** appear and, when choosing, those buttons disappear and the receipt continues. At the bottom of the receipt there is a summary **Expected · Received · Failed**. Against a notice or order only the products of the document are accepted, and only they appear in “Search the list”.

**Receive — bins (2026-10-21).** *With putaway:* everything enters **a single receiving bin** (staging): the one the warehouse has by default, or the first of the STAGING zone; it is shown in the capture (“Received into the receiving position …”). *Direct to bin:* “Search the list” **no longer offers** receiving, cross-dock or rental bins. You choose one or several bins: you type the **quantity per bin** and each chosen bin takes that quantity; **the last one takes what is left over** (18 in 4 bins with 5: 5, 5, 5 and 3). It is the distribution per bin that already existed.

**Putaway (2026-10-21).** The list starts with **the putaways assigned to you**. Below is **“Show what nobody has been assigned (N)”**: when tapped, all the unassigned putaways appear and you can choose whichever you want; when choosing it and starting it **it is assigned to you** (the log records that it was when starting it from the app). Putaways assigned to other people are not offered.

**Dispatch (2026-10-21).** Screen 1: at the top the number field of the exit notice or sales order (scan or type, keyboard, OK); below, the documents **assigned to you**; below, “Show what nobody has been assigned” (with “Select several” and “Dispatch N selected”). Starting a document with no owner assigns it to you. Screen 2 (notice, order or several together): only the **Product to pick** field; when accepted it is disabled and the **bin** is asked (the suggested one, or the one the lot requires; “Search the list”, keyboard and OK); then the **quantity** (the reserved one comes filled in; OK or Back). When the quantity is accepted only the product field remains again. With several documents the product works for any of them and the lines are taken **in the order in which they were marked**. If the product is not left to pick: “That product is not among what is left to pick in these documents.” (or “…in this order.”). There is no longer “Skip” or the list to choose the line: you scan the product you want to take. What is not taken stays pending and what was picked is sent with **Finish**.

## “See everything” and “Reassign” permissions on the device (Batch 199, 2026-10-21)

Two new permissions (Warehouse category, System → Roles; the administrator has them and grants them to the warehouse supervisor):

| Permission | What it does |
|---|---|
| `warehouse.app.viewall` — “See everything on the device (also what is assigned to others)” | In **Receive, Putaway, Dispatch and My tasks** appears **“Show what is assigned to others (N)”**; each row says “Assigned to: {name}”. Only the active warehouse. |
| `warehouse.app.reassign` — “Reassign from the device (requires see-everything)” | **Reassign** button on each row (mine, unassigned or someone else's): opens the list of people of the company and **“Unassigned”**. It needs a signal: “You need a connection to reassign.” |

**Rule.** Everyone sees their own and what nobody has been assigned; only with `warehouse.app.viewall` do you see what belongs to other people, and `warehouse.app.reassign` does not work without seeing everything (the device does not offer the button and the server responds 403: “To reassign you must be able to see everything on the device.”). **`warehouse.pick.any` is no longer checked** (it stays in the catalog and in the roles; it does nothing). Reassigning something that someone else already started is allowed and does not interrupt what is in progress. The person is not notified.

**Server.** The `assign` endpoints (arrival notice, exit notice, purchase order, sales order and task) accept `warehouse.manage` or `warehouse.app.reassign`; `GET /api/v1/warehouse-tasks/assignees` returns the assignable people (with see everything, reassign or manage). The document's log records who reassigned.

**Receive several documents together (correction, 2026-10-21).** When marking two or more notices or orders in Receive, **they are worked together on a single screen**: the title says “LLE-00001 + LLE-00002”, “What the document says” adds up what is expected from all of them and the products of **all of them** are accepted (and appear in “Search the list”). When confirming, what was captured is distributed among the documents **in the order in which they were marked**, each one up to what it is missing (expected − already received); what is left over from all of them goes to the last document that has that product. **One receipt per document** comes out (with the same load key if they are of the same owner, so the server builds the master receiving). In Dispatch the route already gathered the lines of all the marked documents.


## Adjustments to Receive, Dispatch and Putaway (2026-10-22)

- **When it is assigned.** Choosing or opening a document (or a putaway) with no owner **no longer assigns it**. It is left in your name when **you confirm a quantity**: in Receive when adding a line, in Dispatch when accepting the quantity of a line and in Putaway when completing the putaway. If you cancel before, it is not assigned.
- **No “Search the list” against a document.** In Receive against a notice or order only the field and the list “What the document says” are used. In Dispatch, under the product field appears the list **“Left to pick”** with each product and what is left; tapping one is the same as scanning it.
- **Receipt summary:** “Expected · Received · **Damaged**”.
- **Putaway.** The parts of the same receipt (same product, lot and receiving bin) are **a single task** with the total. Each row says the receipt, the warehouse and the bin it comes from. Opening a task that you already started does not give an error (“The task is already in progress.” only if someone else has it). “Reassign” is a small button at the right of the row.
- **Web.** The “Masters” tab of Receiving (and “Masters” of Picking) is now called **“Loads”**.

## Receive and Dispatch: a single list; “without a document” only if not required (2026-10-22)

- **Receive:** no tabs or search box. A list with the arrival notices and purchase orders **assigned to you** and, below, “Show what nobody has been assigned”. “Select several” only appears if there is **more than one** document in view.
- **Dispatch:** the list shows the exit notices and/or sales orders that the company requires (**Settings → Operations → “Dispatch requires a document”**). **“Dispatch without a document” only appears when a document is NOT required** and goes straight to the product and the bin. With the requirement set, that button does not exist nor does the “Dispatch against what?” screen: Dispatch (notices and orders) is the only entry point.

> Screenshots of the warehouse app screens: see [capturas-apps.md](frontend/capturas-apps.md).
