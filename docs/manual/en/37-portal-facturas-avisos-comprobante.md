# 37 — Client portal: invoices, my notices, proof of delivery and consignees

> Batch 119 (P10, 2026-10-18). This is what the client's person sees in their app (portal). Module **CLIENT_PORTAL** (and **BILLING** for invoices). Everything belongs **to the client of the session**: something from another client responds 404, the same as something that does not exist.

## 1. The proof of delivery in the shipment record
When a shipment has its delivery recorded (see [chapter 36](36-registrar-entrega-web.md)), the shipment record in the portal shows the **Proof of delivery** card:

- **Received by** (name) and **Delivered** (date and time).
- “**The delivery was registered by the company staff; the consignee did not sign.**” (if the signature were the recipient's, it would say “Signed by the recipient.”).
- **View on the map**, only if the location was found.

**What the client does NOT see:** the name of the user who recorded the delivery, the internal notes and the status of the location lookup. `GET /api/v1/portal/orders/{publicId}` returns `deliveryProof` (`deliveredAtUtc`, `recipientName`, `registeredByStaff`, `signedByRecipient`, `latitude`, `longitude`) or nothing.

## 2. My invoices (read-only)
Home → **My invoices**. List with number, status, total, balance and dates; filter **All / Issued / Overdue / Paid**; **Load more**. Tapping an invoice shows the **Details** (lines with each one's shipment), the subtotal, the tax, the total, the amount paid, the amount credited, the **balance** and the **payments received**.

- **Lookup only:** there is no button to pay or to mark as paid; payments are recorded by the company.
- **Not visible:** drafts (an unapproved run), internal notes, the detail of an office adjustment or voided payments.
- If the company does not have the BILLING module turned on, the list responds 403 “The 'BILLING' module is not enabled for this company.”.

| Method and route | What it does |
|---|---|
| `GET /api/v1/portal/invoices?status=&skip=&take=` | The list (`status`: ISSUED, OVERDUE, PAID or VOID; empty = all except drafts). |
| `GET /api/v1/portal/invoices/{publicId}` | One invoice with lines and payments; 404 if it belongs to another client, is a draft or does not exist. |

## 3. My notices (the person's preferences)
Settings → **Set up my notices**. The **company decides** which notices the client receives and through which channel (System/Clients → Client notifications); here each **person** of the portal can only **turn off** what they do not want to receive:

- Each notice (Packages picked up, Out for delivery, Deliveries, Cash on delivery, Invoice issued) has two switches: **Receive by email** and **Show it in the notice inbox**.
- If the company did **not** turn on a notice (or a channel) for the client, it shows “The company does not have this notice turned on for your business.” / “The company has it turned off.” and **it cannot be turned on**.
- By default the person receives everything the company turned on. It is saved when the switch is tapped.
- **Effect:** the notice's **email** is not sent to whoever turned it off (the client's other users and the company's extra emails stay the same); the notice **inbox** does not show that notice to that person.
- SMS is not per person (it goes to the phones the company writes down).
- If the server has no email configured, the screen says so: the preferences are saved but no emails will go out.

| Method and route | What it does |
|---|---|
| `GET /api/v1/portal/notice-prefs` | Per notice: `emailAvailable`/`appAvailable` (the company's ceiling) and `emailEnabled`/`appEnabled` (what the person wants). |
| `PUT /api/v1/portal/notice-prefs` | Saves what the person wants (`prefs`: `kind`, `emailEnabled`, `appEnabled`). 400 “The notice '{x}' does not exist.” if the notice does not exist. |

## 4. My consignees
Settings → **View my consignees**: the list of the client's consignees (name, town, email and phone, with a search box) from which you select who gets notified by email and who is given access to their space (chapters 34 and 35).

## Frequent cases
- **I don't see “My invoices”.** It is a button on the Home screen; if the company does not have the billing module, the list says it is not enabled.
- **An invoice does not appear.** Only the ones already issued (not drafts) and only those of your client are shown.
- **I turned off a notice and I still receive it.** The emails the company wrote down as “extra emails” do not belong to a person: they keep arriving. The notice is also sent to the client's other users.

## 5. My profile (Batch 120)
Settings → **My profile and password**.

- **My details:** my **name** (the only thing I change myself; it is also saved in my account, so it is the same in every client I belong to), my **email** (the one I sign in with; it is not changed here) and my **role** (granted by the administrator of my business or of the company).
- **My company (read-only):** name, code, legal name, tax ID and corporate address. The company changes them; if something is wrong, you write to them from Messages.
- **Change my password:** current password, new and repeated. Changing it closes the other sessions. The server messages come out in the person's language: “The current password is incorrect.”, “The password must be at least 12 characters long.”…

| Method and route | What it does |
|---|---|
| `GET /api/v1/portal/profile` | My name, email, role and my business's details. |
| `PUT /api/v1/portal/profile` | Changes my name. 400 “Enter your name.” if empty; 400 “The name cannot exceed 150 characters.” |
| `PUT /api/v1/auth/password` | Changes my password (the usual one). |

## 6. Client indicators (Batch 120)
On the **Home** screen, for **today** (Batch 213: since midnight today in the company's time zone) or for the last **7, 30 or 90 days**:

- **Shipments created** and **Delivered** (those that moved to Delivered in the period).
- **On-time deliveries:** of the delivered ones that had a promised date, how many were delivered by that day (company day), with their percentage; if none had a date, it is not shown.
- **Average delivery time** (hours from creation to delivery).
- **Failed attempts** of the period.
- **In progress now** (everything not in a final status) and how many are in transit.
- **Invoice balance** (and how many overdue), only if the company has the BILLING module; **Collected, to be remitted**, only if it has COD and there is money collected for its deliveries that has not been remitted to it yet.

`GET /api/v1/portal/indicators?days=30` (0 = today, 7, 30 or 90; 400 “Days must be 0 (today), 7, 30 or 90.” with any other number). They belong only to the client of the session: nothing from other clients or from drivers.

## 7. Invoice processed (Batch 126)

The shipment record in the portal shows **Invoice processed: date** when the company has already processed the client's invoice against that shipment (reconciled COD sheet or check from Process deliveries). If the row does not appear, it has not been processed yet. Read-only; without the name of who processed it. Field `invoiceProcessedAtUtc` of `GET /api/v1/portal/orders/{publicId}`.
