# 12 — Printers and labels

This chapter covers **Batch 43 (F-G1, 2026-10-12)**: the **company printers**, the **label templates** (the layout
of the shipping label), the **print options of each station**, the company setting **“Print labels directly to the
printer”**, the client flag **“Print the invoice automatically”** and **direct printing with QZ Tray** (with the
support section for the certificate). The screen is in chapter [FG1 — Printers and labels](frontend/fg1-impresoras.md).
**Batch 44 (F-G2)** adds section 10: the labels printed from orders (one per piece), the waybill summary, the packing list,
the missing report and the search by package number in scanning.

## 1. How printing works in Teikem

- **The server never connects to a printer.** The user's computer prints:
  - **Direct (QZ Tray)**: QZ Tray is a free, open-source program installed on each computer. The page
    talks to it through a local channel (websocket to `localhost`) and QZ Tray sends the PDF to the system printer by its **exact name**
    (the Windows or macOS name), **with no dialog**.
  - **Browser PDF (fallback)**: if the printer is not a QZ Tray one, or QZ Tray is not installed, not running or fails, the
    browser print dialog opens with the PDF (and on a phone or tablet the PDF is downloaded). It never breaks: the
    screen tells you why it did not print directly.
- The **label** is a black-and-white vector PDF, **one page the exact size of the label** (4×6, 4×4 or 4×2 inches) per
  **piece**: the quantity of each cargo line (whatever the unit) is the number of labels of the line. The **barcode is
  the package number of the line** (the same on all its pieces); below it go the readable number and “Package i of N”. A line without a package
  number gets no labels (a warning is shown).
- The **layout** (what is shown, where and at what size) comes from the **template**; the **size** comes from the **printer**. That is why one
  template works for any label size.
- Each **station** (this browser on this computer) keeps its own configuration: label printer, template, printer on
  this computer (QZ Tray), “Print on save”, “Confirm first” and document printer. The server does not receive it.
- Order in which the printer and template to use are decided: **the station's → the company default → the
  built-in one** (“Browser PDF” 4×6 or Letter and the **built-in label**).

## 2. Who can and where

| Action | Permission | Endpoint |
|---|---|---|
| View, create and edit printers; make default, deactivate and reactivate | `printers.manage` | `GET/POST /api/v1/printers`, `GET/PATCH /api/v1/printers/{publicId}`, `POST …/make-default`, `…/deactivate`, `…/reactivate` |
| View, create and edit label templates; make default, deactivate and reactivate | `printers.manage` | `GET/POST /api/v1/label-templates`, `GET/PATCH /api/v1/label-templates/{publicId}`, `POST …/make-default`, `…/deactivate`, `…/reactivate` |
| Read and change “Print labels directly to the printer” | `printers.manage` | `GET/PUT /api/v1/printing/settings` |
| Read the station's print options (active printers and templates, built-in label, direct setting, whether QZ Tray signing exists) | any of `orders.view`, `trips.scan`, `trips.view`, `inventory.view`, `printers.manage` | `GET /api/v1/printing/station-options` |
| Public certificate and signature for QZ Tray | the same as the station options | `GET /api/v1/printing/qz/certificate`, `POST /api/v1/printing/qz/sign` |
| The client's “Print the invoice automatically” | `clients.update` | `PATCH /api/v1/clients/{publicId}/profile` (`autoPrintInvoice`) |

- **Module**: SYSTEM (core, always on).
- **`printers.manage`** (Administration category, “Manage printers & label templates”) comes only with the **Company admin** role
  (also the roles already created with that name in each company). To give it to a counter supervisor, add it to their role in
  System → Roles & users.
- Routes use the `PublicId`; a printer or template of another company responds 404. They are never deleted: they are deactivated.
- Every change is recorded in the log with the types **Printer** (`PRINTER`) and **Label template** (`LABEL_TEMPLATE`).

## 3. Printers

A printer is a **print profile** of the company:

| Field | Rules |
|---|---|
| Name | Required, up to 80 characters, **unique among active ones** (case-insensitive). It should match the Windows name. The name of an inactive one can be reused. |
| System name | Optional, up to 200. The **exact** name of the printer on the computer; QZ Tray prints by it. Empty = the Name. Each station can choose another (the one on its computer). |
| Model | Optional, up to 80. |
| Use | `LABEL` (labels) or `DOCUMENT` (documents). Required when creating it and **cannot be changed afterwards**. |
| Connection | `QZ_TRAY` (direct with QZ Tray) or `BROWSER` (browser PDF). Empty = `BROWSER`. |
| Size | Labels: `4X6`, `4X4` or `4X2` (empty = `4X6`; “4x6” or “4×6” are also accepted). Documents: `LETTER` (letter; empty = `LETTER`). |
| Rotate 90° | For printers that feed the label sideways: the PDF comes out with the page rotated (with QZ Tray, QZ Tray rotates it). |
| Note | Optional, up to 300. Not shown to the stations. |
| Default | One per use among the active ones. The **first** active printer of a use is born as default. |

- **Make default** removes the previous one of the same use (the other one is unmarked) and sets this one.
- **Deactivating the default** is allowed: the use is left **without a default** and the stations that did not choose another print
  with the browser PDF until another one is chosen. **Reactivating** does not make it default again.
- In an edit (PATCH) what is not sent does not change; `""` in Model, Note or System name clears them. With `rowVersion`, if
  another user saved first it responds 409.
- Creation responds **200** with the printer (Teikem API convention).

## 4. Label templates (label layout)

The layout has the **9 elements** of the builder: Logo, Service level, Sender information, Consignee, Address,
Barcode, Waybill number, Invoice and COD amount. Each one has:
- **Show** (yes/no);
- **Position** in a 9-zone grid: `tl` (top left), `tc` (top center), `tr` (top right), `ml`, `mc`, `mr` (middle),
  `bl`, `bc`, `br` (bottom);
- **Size**: `sm` (small), `md` (medium), `lg` (large);
- **As barcode**: only **Waybill number** and **Invoice**. The **Barcode** always is one (the package number).

| Element | What it prints |
|---|---|
| Logo | The company name in bold (the graphic logo is not printed in P1). |
| Service level | The order's service in capitals (e.g. NEXT DAY). |
| Sender information | “From: client that sends”. |
| Consignee | “To: consignee”, in bold. |
| Address | The lines of the delivery address. |
| Barcode | Code 128 of the **package number**, full width, with the readable number and “Package i of N” below. |
| Waybill number | “Waybill …” (or as a barcode). |
| Invoice | “Invoice …” (or as a barcode). |
| COD amount | “COD $…” in a **black box with white letters**, only if COD is greater than 0. The price is never printed. |

Drawing rules: barcodes take **the full width of their row** (in a one-third column they do not fit legibly); the
text is pure black and never smaller than 6 pt; if the content does not fit on the label it shrinks and, if it still does not fit, the warning “The label
does not fit on 4×2: remove elements or use a bigger label.” is shown.

Templates:
- Name required, up to 80, **unique among active ones**. The **first** active template is born as default; “Make
  default”, deactivate and reactivate work the same as for printers.
- When creating, the layout is required; an element that is not sent takes the one from the **built-in label**; position and size are
  accepted in capitals. In the PATCH what is not sent does not change.
- The label needs **at least one visible barcode** (the package one, or waybill/invoice as a barcode).
- **Built-in label** (the one that applies without a template): Logo top left medium; Service level top right small; Sender hidden;
  Consignee and Address middle left; Barcode center medium; Waybill hidden; Invoice bottom left small; COD amount
  bottom right medium.

## 5. Company setting: “Print labels directly to the printer”

- **Off by default** (no company changes its behavior until it is turned on). `PUT /api/v1/printing/settings`
  `{ "printLabelsDirect": true }` with `printers.manage`; also on the screen.
- When on, **saving an order** in the quick or detailed entry prints **one label per piece** on the station's label
  printer (with no dialog if the station prints with QZ Tray). The station can choose not to print by turning off its “Print on save”.
  Since F-G2 the “Print label on save” switch of the quick and detailed entry comes on when the setting is on and the
  station did not exclude itself (section 10).

## 6. Invoice label (client flag “Invoice label”)

- `Client.AutoPrintInvoice`, per client (the one that sends, *sender*), off by default. It is shown in the client record (`autoPrintInvoice`)
  and changed in the “Printing & portal” panel of the client record (it used to be in the Tax profile) (`PATCH …/profile` with `autoPrintInvoice: true|false`; without the field it does not change). Since 2026-10-21
  it turns on the **invoice label**.
- **What prints**: when saving a shipment of that client that has an **invoice number**, **after** the package labels, **one** invoice label
  prints (one per shipment, not per piece) on the **same label printer** of the station. It prints **by itself, with no preview or question**, even if the
  station is on “Confirm before printing”. If the shipment has no invoice number the warning is “{N} shipments without an invoice number get no invoice label.”.
- **How it looks**: it is defined by the template of **class Invoice** (`kind = INVOICE`). By default (without a template) it is the **barcode of the invoice
  number, centered and large, with the number right below** (without “Package i of N”). In *System → Printers & labels* there is a second builder,
  “Build the invoice label”, the same as the one for pieces (New, Duplicate, Make default, Deactivate, Refresh): there you can add whatever is
  needed (client, consignee, address, COD, waybill…), move it and change its size. In that builder “Barcode” **is the invoice
  number** and the “Invoice” element does not appear. Invoice templates and piece templates are separate lists: each class has its own default and its own
  names (a piece template and an invoice template can have the same name). The class cannot be changed after creation.
- It prints from the quick entry and the detailed entry. Shipments of clients without the flag get no invoice label.

## 7. Messages and HTTP codes

| Code | Message | When |
|---|---|---|
| 400 | “Type the printer name.” | Creation without a name or PATCH with an empty name. |
| 400 | “The name cannot exceed 80 characters.” | Printer or template name longer than 80. |
| 400 | “The model cannot exceed 80 characters.” | |
| 400 | “The note cannot exceed 300 characters.” | |
| 400 | “The system name cannot exceed 200 characters.” | |
| 400 | “Choose the use of the printer: LABEL (labels) or DOCUMENT (documents).” | Creation without a use. |
| 400 | “Unknown printer use: 'X'.” | Use that is neither LABEL nor DOCUMENT. |
| 400 | “The use of the printer cannot be changed; create another printer.” | PATCH with another use. |
| 400 | “Unknown printer connection: 'X'. Use BROWSER (browser PDF) or QZ_TRAY (direct with QZ Tray).” | |
| 400 | “A label printer uses 4X6, 4X4 or 4X2 labels.” | Size that is not a label size. |
| 400 | “A document printer uses letter paper (LETTER).” | Size that is not letter. |
| 409 | “An active printer with that name already exists.” | Creation, rename or reactivation with the name of another active one. |
| 409 | “The printer is inactive; reactivate it before making it default.” | |
| 404 | “Printer not found.” | It does not exist or belongs to another company. |
| 400 | “Type the template name.” | |
| 400 | “The label layout is missing.” | Template creation without `layout`. |
| 400 | “Unknown position in “COD amount”: 'X'. Use tl, tc, tr, ml, mc, mr, bl, bc or br.” | The element name changes with the element. |
| 400 | “Unknown size in “Consignee”: 'X'. Use sm, md or lg.” | Same. |
| 400 | “Only the waybill number and the invoice can be shown as a barcode.” | |
| 400 | “The label needs at least one barcode so it can be scanned.” | |
| 400 | “The template type must be PIECE (piece label) or INVOICE (invoice label).” | Unknown `kind` on creation or in `GET label-templates?kind=`. |
| 400 | “The template type cannot be changed; create another one.” | PATCH with a `kind` different from the template's. |
| 409 | “An active label template with that name already exists.” | Within the same class (piece or invoice). |
| 409 | “The template is inactive; reactivate it before making it default.” | |
| 404 | “Label template not found.” | |
| 409 | “The record was modified by another user; reload and try again.” | PATCH with an old `rowVersion`. |
| 409 | “Direct printing with QZ Tray has no certificate configured on this server; QZ Tray will ask “Allow” on each computer (support: manual 12, “QZ Tray certificate”).” | `qz/certificate` or `qz/sign` without a certificate or without a key. |
| 400 | “The text to sign is missing.” / “The text to sign cannot exceed 8192 characters.” | `qz/sign`. |
| 403 | `forbidden` | Without the permission for the action. |

## 8. Frequent cases

- **“When testing, the browser dialog opened instead of printing directly”**: the printer does not have the QZ Tray connection, or on that
  computer QZ Tray is not installed or running. The screen says so (“QZ Tray not detected”). Install QZ Tray, open it and click
  “Detect printers on this computer”.
- **“QZ Tray asks me "Allow" every time”**: the server does not have the Teikem certificate configured or that computer does not
  have the `override.crt` installed (section 9).
- **“I deactivated the default printer and now the PDF comes out”**: the use was left without a default (section 3). Make another one default.
- **“The station says the chosen printer is no longer active”**: someone deactivated it; the station uses the company's one until
  you choose another and click Save.
- **“The label comes out cut off on 4×2”**: too many large elements; remove elements or use Size Small (or a bigger label).

## 9. Support: QZ Tray and the Teikem certificate

This section is for **support** (not for the end user).

### 9.1 Install QZ Tray on each computer that prints directly
1. Download QZ Tray from <https://qz.io/download/> and install it (Windows or macOS). It must stay open (icon next to the clock).
2. **Page library**: the web app loads `/vendor/qz-tray.js` (it is not downloaded from the internet by the page). Copy the file
   `qz-tray.js` of the installed version (in the npm package `qz-tray`, or in the `demo/js` folder of the QZ Tray installation) to
   `web-app/public/vendor/qz-tray.js` before publishing the web app (it ends up at the site root). Without that file the screen says “Direct
   printing is not available on this computer…” and everything prints with the browser PDF.
3. In Teikem → System → Printers & labels: “Detect printers on this computer”, choose the station's printer and Save.

### 9.2 Generate the platform's own certificate (self-signed)
Only once, on a support machine with OpenSSL:
```
openssl req -x509 -newkey rsa:2048 -keyout teikem-qz.key -out teikem-qz.crt -days 3650 -nodes -subj "/CN=Teikem QZ Tray/O=Advance"
```
- `teikem-qz.crt` is the **public certificate** (it can be distributed). `teikem-qz.key` is the **private key**: it does not go in the repository,
  in an e-mail, or in an API response.

### 9.3 Configure the server
One of these two ways (environment variables on the API server, or files outside the repository):
```
Printing__Qz__Certificate="<contenido PEM del .crt>"      o   Printing__Qz__CertificatePath=/etc/teikem/teikem-qz.crt
Printing__Qz__PrivateKey="<contenido PEM del .key>"       o   Printing__Qz__PrivateKeyPath=/etc/teikem/teikem-qz.key
Printing__Qz__PrivateKeyPassword=<solo si el .key está cifrado>
```
In single-line variables the line breaks can be written as `\n`. Restart the API. `GET /api/v1/printing/station-options`
must return `"qzSigningConfigured": true` and `GET /api/v1/printing/qz/certificate` the certificate (without the key). If the key is
damaged or the password does not match, the server treats it as “not configured” (a clear 409, never 500).

### 9.4 Install the certificate on each computer (override.crt)
So that QZ Tray trusts Teikem without asking:
1. Copy `teikem-qz.crt` to the QZ Tray installation folder with the name **`override.crt`**
   (Windows: `C:\Program Files\QZ Tray\override.crt`; macOS: `/Applications/QZ Tray.app/Contents/Resources/override.crt`).
2. Restart QZ Tray. When printing, “Allow” no longer appears.
The signature QZ Tray asks for (a short digest of each request) is made by the server with SHA512withRSA in `POST /printing/qz/sign`, with the
permission of the station options; it only works on the computers that installed that `override.crt`.

## 10. Order labels and documents (Batch 44, F-G2)

The screen is in [F-G2 — Labels and documents](frontend/fg2-labels-y-documentos.md). Everything prints with the station's
configuration (sections 1 and 3) and never includes the order's price.

### 10.1 Order labels
- **One per piece**: the quantity of each cargo line (whatever the unit) is the number of labels of that line. Each label carries in
  the barcode the **package number of its line** (the same on the pieces of the line) and “Package i of N” (i running across
  all the pieces of the order; N = its pieces).
- Content: logo = company name; service level; “From:” the client; “To:” the consignee; the delivery address; waybill
  number = order number; invoice = the client's invoice; COD only if the order has it.
- Not printed: **cancelled or deactivated** orders (“{N} cancelled orders were not printed.”) nor the pieces of a line **without a
  package number** (“{N} pieces without a package number were not printed.”).
- **No limit**: from 500 labels the warning is “That is {N} labels: the PDF will be large and may take a moment. All are printed; there is no limit.”.
- Where: Orders list (“Print label” for the selected ones and “All labels” for everything filtered), order file (“Print label”) and
  **“Print label on save”** in the quick and detailed entry (it comes on if the company has “Print labels directly to the printer” and the
  station did not turn off its “Print on save”). Permission: `orders.view` to print; `orders.create` to capture.
- **A print failure never undoes the order**: “The order was saved, but its label could not be printed: {reason}. Use “Print label” on the order file.”.

### 10.2 Documents (the station's document printer, letter paper)
- **Waybill summary**: one row per order (Pack, Shipment no., Invoice, Client, Consignee, Town, ZIP, Service, Packages, Pieces,
  COD), grouped by client with a subtotal of orders, pieces and COD, and a grand total. Letter landscape.
- **Packing list**: one section per order (pack, order, invoice, consignee and address) with its packages (type, package no.,
  description, pieces and weight) and subtotal; grand total of pieces and weight. Letter portrait.
- For the **selected** orders or, with nothing selected, for **everything filtered** (all pages). Cancelled ones are not included (the document says so).
- **Missing report** (scan station, `trips.view`): the orders still pending to be scanned with the same filters as
  “Pending” (all pages, no limit), grouped by zone (“No zone” and “Ambiguous zone” at the end), with Pack, Order, Invoice, Client,
  Consignee, Town, ZIP, Pieces and Requested date, and the summary of pending and scanned in this session. Letter landscape.

### 10.3 Scanning the package number
- Exit scanning and `GET /api/v1/orders/lookup` search the **package number** as fourth precedence (after shipment number,
  pack and invoice): the result says “Found by package number.”.
- The package number is per client (PQT-##### of each client) or typed in, so it can repeat: with two or more orders the scan
  responds “The package number is in {n} orders (from different clients or repeated); scan the pack or the shipment number.” and does not assign.
- The search uses the index `IX_CargoLine_PackageNumber` (section “2026-10-12 (e)” of `logistica-db-update.sql`; it is created with `db-update`).

## Printing when the shipment is created: it is per client (2026-10-22)

Printing **the label** and **the invoice** automatically when a shipment is created is a setting **of each client** (client record → Profile → “Print labels automatically” and “Print the invoice label automatically”), not of the company. That is why System → Printers & labels **no longer has** the “Print labels directly to the printer” switch, nor the station its “Print on save” one. In the shipment entry the session switch remains (off by default) to print the labels of that capture even if the client does not have it. Companies that had the general setting on passed that value to all their clients once (migration 2026-10-22).

**Adjustment (2026-10-22, same day).** The company setting comes back, but as the **default value for new clients**: System → Tenant settings → Operations → Default values → “Print labels automatically for new clients”. When a client is created it is born with that value and afterwards it is turned on or off in its record (Profile); it does not change existing clients. The decision to print when a shipment is created is still the client's.
