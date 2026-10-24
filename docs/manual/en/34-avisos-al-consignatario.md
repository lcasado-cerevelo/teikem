# 34 — Consignee notices

Batch 116 (2026-10-18, dictated by Luis; design in `docs/p12-avisos-consignatario-diseno.md`). What **the consignee** (the one who receives the package) **receives** about their shipments. **The client turns it on and marks it** (not the company), from the portal.

## 1. Who and where
- **Client (portal):** an administrator or operator of the client **turns on** the notice, chooses its **language** (that of the consignee notices) and **marks, per consignee**, whether they agree to receive notices by **e-mail** and/or by **text**. Whoever can only view cannot change it. (Module CLIENT_PORTAL.)
- **Consignee (no session):** opens the link in the e-mail to see the progress of their package or to **unsubscribe**.

## 1.1 The screens (client app)
- **Settings → Notices to my consignees → Set up notices** (`/consignee-notices`): **“Notify my consignees”** checkbox, **Language of the notices** (Español / English) and the list of consignees with search by name or city. Each consignee has two checkboxes: **Notify by email: <e-mail>** and **Notify by text: <phone>** (disabled if that medium is not registered or the notice is off; text is saved but does not go out yet). Checking shows the privacy **warning** with **Continue / Cancel**; unchecking does not ask for it. Whoever can only view sees it without being able to change it.
- **Tracking by link** (`/track/<token>`): public; the shipment's progress bar and the last movement. See chapter 28.
- **Unsubscribe** (`/unsubscribe/<token>`): public; opening the link does **not** unsubscribe: **“Yes, unsubscribe me”** must be pressed (so an e-mail filter does not do it for you).

## 2. When notices are sent
| What happened to the shipment | Is a notice sent? |
|---|---|
| **Goes out On route** (In transit) — the tracking link goes out | Yes |
| **Failed attempt**, another On route departure, another attempt… | Yes |
| **Final status**: Delivered, Returned, Cancelled, In warehouse | Yes |
| Pickup, Unassigned, Assigned (movements inside the warehouse) | **No** |
| A status **skipped** by the scan | **No** |
Only if the client has the notice **on**, the consignee is **marked** for that medium, has that medium **registered** (e-mail or phone in their record) and **did not unsubscribe**. The e-mail carries the **client's language**, the shipment's tracking link and the unsubscribe link.

## 3. Marking a consignee
On marking them a **warning** appears (part of the privacy policy): *“By continuing you confirm that the consignee agreed to receive notices…”*; it must be accepted. Then:
- it is kept in the **log** (who, when, which medium, with which e-mail or phone and that the warning was accepted);
- **the consignee is told**, through the marked medium, that the client marked them (the e-mail includes how to unsubscribe). The text (SMS) is saved but **does not go out** until the provider is hired.
Unmarking is also kept in the log and sends no notice. Marking or unmarking the same thing again does nothing.

## 3b. Marking several at once (Batch 213, 2026-10-23)
On the same screen each consignee has a **selection checkbox**; at the top, **“Select the N shown”** and, with something selected, the bar **Notify by email · Notify by text · Remove email · Remove text** (and “Clear selection”). Marking shows the **warning only once for the whole group** (“This applies to N consignees (email)…”); removing does not ask for it. The server marks each one the same as the individual action (log and notice per consignee) and responds with how many were marked and **which ones could not be and why** (no e-mail or phone registered, unsubscribed); those appear on screen with their reason and the rest stay marked. In the wide web the list is a **table** (checkbox, consignee, town, e-mail, text, Access) with its total “N of M”; on the phone, cards with the checkbox at the top right.

`PUT /api/v1/portal/consignee-notices/consignees/bulk` with `publicIds` (up to 200), `channel`, `accepted`, `warningAccepted`. Responds `done`, `failed` and `items[]` (`publicId`, `name`, `ok`, `message`).

| Situation | Code | Message |
|---|---|---|
| No consignees | 400 | “Choose at least one consignee.” |
| More than 200 | 400 | “Mark up to 200 consignees at a time.” |
| Marking without accepting the warning | 400 | the same as the individual one (`warningAccepted`); nobody is touched |
| Marking with the notice off | 422 | the same as the individual one; nobody is touched |
| View-only role | 403 | “Your role in the portal is view only: you cannot change the consignee notices.” |
| A consignee without that medium, unsubscribed or nonexistent | 200 | that item comes with `ok=false` and its message; the others are marked |

## 4. Tracking and unsubscribe link
- **Tracking:** `…/track/<token>`. The token belongs to each shipment, is long and **impossible to guess**; the page only shows the progress (it receives no data and does not allow searching by number). API: `GET /api/v1/public/track/link/{token}` (no session; 404 *“We could not find that tracking link.”* if it does not exist).
- **Unsubscribe:** `…/unsubscribe/<token>` (each notice carries its own). API: `POST /api/v1/public/consignee-unsubscribe/{token}` (no session; idempotent; 404 *“The unsubscribe link is not valid.”*). The unsubscription is **permanent** for that e-mail or phone in the company: it is kept in the log, the consignee stops being marked and **the client cannot mark them again** (409).
- Every communication to the consignee carries the option to unsubscribe (link in the e-mail; “STOP” in the text when it exists).

## 5. Sending
A job every minute (`CONSIGNEE_NOTICE_DISPATCH`, see Jobs) sends the pending notices and retries the failed ones at **1, 5 and 30 minutes**; on the fourth attempt it becomes **failed**. E-mail must be configured (Brevo) and the client app address in `ConsigneeNotices:ClientAppBaseUrl` (without it or without e-mail, the notice is left *skipped* with the reason).

## 6. Messages
| Message | HTTP | When |
|---|---|---|
| Your role in the portal is view only: you cannot change the consignee notices. | 403 | A view-only user changes the notice or marks someone. |
| First turn on the consignee notice. | 422 | Marking without having the notice on. |
| To mark that the consignee receives notices you must accept the privacy warning. | 400 | Marking without `warningAccepted`. |
| Enter the notice medium: email or text. | 400 | Unknown medium. |
| The consignee has no e-mail registered: add it before marking them. / … a phone registered … | 400 | The e-mail or phone is missing from the consignee's record. |
| The consignee unsubscribed from notices through that medium: they cannot be marked again. | 409 | Marking someone who unsubscribed. |
| Enter the language of the notices: Spanish (es) or English (en). | 400 | Language other than es/en. |
| Consignee not found. | 404 | The consignee does not belong to this client. |

## 7. Frequent cases
- *The consignee received nothing:* check that the client has the notice on, that they are marked for that medium, that the e-mail is in their record, that they did not unsubscribe and that the status is one of those that are notified (§2).
- *The consignee wants to stop receiving notices:* have them use the “unsubscribe” link in any e-mail; it cannot be undone from the portal.
