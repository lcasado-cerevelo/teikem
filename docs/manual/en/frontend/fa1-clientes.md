# F-A1 — Clients: list, creation and client record

Screen **Lists → Clients** (`/catalog/clients`). In a single screen: on the left the list of clients and on the right the record
(file) of the chosen client. Backend: chapter 02 of the manual (Clients and contracts). Frontend decisions:
`docs/frontend/loteFA1-decisiones.md`.

Screenshots (run `web-app/e2e/loteFA2-clientes.spec.ts`): `img/fa1-lista.png` (list), `img/fa1-alta.png` (New client modal) and `img/fa1-ficha.png` (record).

**Who can.** See the screen: permission **`clients.read`** with the **Lists** module (`CATALOG`) turned on (without the permission the menu does not show it and the
address leads to “No permission”; with the module off, to “Module turned off”). Each action requires its permission and, without it, the button is not drawn and the fields
remain read-only:

| Action | Permission |
|---|---|
| Create client | `clients.create` |
| Edit profile, numbering, contact people, change status, deactivate and reactivate, save custom fields | `clients.update` |
| Add, edit or remove phones and emails (of the client or of a person) | `contacts.manage` **and** `clients.update` |

## 1. The list (left)

- **Clients** panel with the counter of clients shown. Each row shows the name, the code, the status (color chip from the company's list), the
  **Inactive** chip if the client is deactivated and the billing summary of the current contract.
- **Search box**: type part of the code, the name or the legal name; the search is done by the server (it waits a moment after the last keystroke).
- **Show inactive**: by default only active clients are shown; turn the switch on to also see the deactivated ones (they appear dimmed).
- Tapping a row opens its record on the right and leaves the client in the address (`?client=…`): the link can be copied. With no client in the address the first one opens.
- The bar between the list and the record can be dragged (or moved with ← →) to change the width; the proportion is remembered. On the phone (under 720 px) the record goes
  below the list.

## 2. New client

**New client** button (`clients.create`). Fields:

| Field | Rule | Message (HTTP) |
|---|---|---|
| Name | required, max. 200 | `The name is required.` (400) · `It cannot exceed 200 characters.` (400) |
| Code | optional, max. 30; empty = generated from the name (`FARMACIA-LAS-MARIAS`; if it already exists, `-2`, `-3`…); stored in uppercase | `The code cannot exceed 30 characters.` (400) · `A client with that code already exists.` (409) |
| Legal name | optional, max. 250 | `It cannot exceed 250 characters.` (400) |
| Tax ID | optional, max. 50 | `It cannot exceed 50 characters.` (400) |
| Payment term, Currency | company lists | — |
| Credit limit | number, non-negative | `The credit limit cannot be negative.` (400) |
| Create initial contract | on by default: opens a contract in **draft** | `A contract with that number already exists.` (409) |
| Client since | start date of the contract (today in the company's zone) | `Enter since when they are a client.` (on screen) |
| Contract title | optional; empty = “Master contract”; max. 200 | `The title cannot exceed 200 characters.` (400) |

The SLA is not asked for: it is per service type and in hours, and it is configured in the contract (next block). On saving, the client is born in the initial stage of its
status, the list is refreshed and the new client's record stays open. Server messages appear below the field; the 409 for a duplicate code also appears
below **Code** and the modal stays open.

## 3. The record (right), panel by panel

### Header: status and deactivation
- Name and code of the client, and the **stages of its status** (pipeline). The buttons only offer the transitions the server would accept (“Advance to …”,
  “Move to …”); they ask for an optional comment (max. 500). Requires `clients.update`. If the server rejects (422), the message stays in the dialog, e.g.
  `Illegal jump: from 'ACTIVE' you can only advance to 'REVIEW'.`
- **Deactivate** (`clients.update`): confirms “{name} will become inactive and can no longer be picked on new shipments…”. It is a soft deactivation; the history is kept.
  **Reactivate** returns it to the active list. A deactivated client shows the notice “This client is deactivated…”.

### Tax profile
- (Previously “Client profile”.) The **name** is shown but is not changed here. Legal name (the entity's legal name, the one on invoices), tax ID, credit limit, payment term and currency are edited. The **addresses** and **where pickup happens** are no longer here: they go in the **Addresses** panel ([fb5](fb5-direcciones-y-perfil-fiscal.md)).
- **Labels → “Print labels automatically”** (Batch 48): per-client switch, off by default, **before** the invoice one. When saving an order of this client, its labels (one per package) print by themselves even if “Print label on save” is off at the station (with the station's label printer).
- **Invoice → “Print the invoice automatically”** (Batch F-G1): per-client switch, off by default. Today **only the
  preference is saved**: Teikem does not print invoices yet; when billing arrives, this client's will print by themselves to the document printer.
- **Save** is only enabled if there are changes. The credit limit cannot be left empty once it has a value (type 0 if it does not apply).
- Messages: `The record was modified by another user; reload and try again.` (409: someone else saved first; the record is read again and what you
  typed is kept, press **Save** again) · `The pickup point must be an active address of the client: the physical, the postal or a pickup point.` (400) ·
  `The credit limit cannot be negative.` (400) · `Client not found.` (404).

### Phones & emails
The contact details **of the client itself**. Phones are shown with the company's mask (`(787) 555-0142`) and stored with digits only; emails, in
lowercase. **Add phone / Add email**, pencil to edit and trash can to remove (asks for confirmation). **Primary**: only one per type; marking another removes the mark
from the previous one, and the first one added of a type becomes primary.
Messages: `Invalid phone.` · `Invalid email.` · `The value is required.` (400).

### Contact people
Table **Name · Role · Phone · Email · Primary**. **Add contact** and the pencil open a modal (name, role, phone, email, primary). **Remove** asks for
confirmation and deactivates the person (it is not deleted). There is only **one active primary person**: marking another removes the mark from the previous one.
Messages: `The contact name is required.` (400) · `It cannot exceed 150 characters.` / `It cannot exceed 80 characters.` (400) ·
`There is already an active primary contact for this client.` (409, if two people mark the primary at the same time; retry).
Without `contacts.manage` the modal saves name, role and primary, and warns that phones and emails cannot be changed.

### Numbering
- **Who assigns the shipment / invoice number?**: *The client* (types it when capturing) or *Teikem (automatic)*.
- The three patterns (**order, invoice, package**) define how the number Teikem generates looks: `#` = one digit of the sequence, `@` = letter, the rest is copied as is
  (`AX-#####` generates `AX-00001`, `AX-00002`…). Under each pattern the **Example** with sequence 1 is shown, computed by the server as you type. **Empty = the system's
  default pattern** (`ORD-#####`, `FAC-#####`, `PQT-#####`). The package number is always generated by Teikem.
- Rules (400, under the field): `The pattern cannot exceed 40 characters.` · `The pattern must include at least one '#' for the sequence.` ·
  `Character not allowed in the pattern: 'x'. Use letters, digits and - _ / . # @.` (letters without accents, digits and `- _ / . # @`).

### Custom fields
It only appears if the custom fields module is turned on and the company defined fields for clients. They are saved with the panel's **Save** (`clients.update`).

### Contracts
Since batch F-A2 this panel is a section with tabs **Contract · Rates · SLA · Special services**: see [F-A2 — Client contracts](fa2-contratos.md).
Without the `contracts.read` permission only the billing summary and a notice are shown.

### Status history
Each status change of the client: from what to what, who, when and the comment.

## Common cases
- **I don't see the New client / Save / Deactivate button**: the permission is missing (`clients.create` / `clients.update`); with only `clients.read` the record is view-only.
- **I don't see Add phone**: it requires `contacts.manage` and `clients.update` at the same time.
- **I deactivated a client and it disappeared from the list**: the list hides inactive ones; turn on **Show inactive** and use **Reactivate**.
- **The numbering example does not change**: wait a moment after the last keystroke; if the pattern is invalid the reason appears in red instead of the example.
- **Where do I put the SLA or the client since after creation?**: they belong to the contract; they are edited in the Contracts section of the record ([F-A2](fa2-contratos.md)).
- **I can't change the address**: addresses are managed in Locations; in the profile they are read-only.
