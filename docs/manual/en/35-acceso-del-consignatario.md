# 35 — Consignee access

Batch 117 (117a access, profile and inbox; 117b messages with their client; 117c screens; 117d shipments; 117e e-mail notice; 2026-10-18, dictated by Luis; design in `docs/p10-portal-diseno.md`). A **consignee** (whoever receives the package) can have **their own access** to the portal. **Only the client gives it**, by **invitation to an e-mail**; most consignees will not have access and will keep receiving the notices with the tracking link (chapter 34).

## 1. Who and where
- **Client (portal):** an administrator or operator of the client **invites**, **resends** or **removes** the access of one of their consignees (module CLIENT_PORTAL). Whoever can only view sees who has access, without changing it.
- **Consignee:** signs in to the same portal with their e-mail and password, but to **their own space**: they only see and change **what is theirs**.
- **Company (staff):** does not invite consignees. If it tries to give the “Consignee” role to a person of the client from the file, it responds **400 `The consignee role is only given from the consignee.`**

## 2. The invitation
1. The client types the consignee's e-mail (if left empty, the main one the consignee already has registered is used) and, optionally, the name.
2. An e-mail arrives with a **link that expires in 48 hours (configurable) and is used only once**. **Resend** voids the previous link.
3. With the link, the consignee **chooses their password**. Their access becomes **Active**.
4. On the first sign-in they confirm the **computer** by e-mail (it stays trusted for 30 days without use, the same as the rest of the portal), accept the **legal document** and sign in. **The consignee does not use two-step verification (MFA)**: password + trusted computer.
- The same person can be a consignee of several clients (one invitation per client). An e-mail cannot belong to the staff and the portal at the same time.

| Situation | Response |
|---|---|
| No e-mail typed and the consignee has none registered | **400 `Enter the consignee's e-mail: they have none registered.`** |
| That e-mail is not available (already has access, or belongs to the staff, or to another company) | **409 `That e-mail is not available for this company's portal.`** (the same message in every case: it does not reveal where it is) |
| Whoever invites can only view | **403 `Your role in the portal is view only: you cannot give access to a consignee.`** |
| Consignee of another client or that does not exist | **404** (the same for both) |
| Resending to someone who already accepted | **409 `The invitation can only be resent to a user who has not accepted it yet (status INVITED).`** |
| Link expired, used or from a resent invitation | **400 `Invalid or expired invitation.`** |

**Removing the access** is the permanent deactivation of that person: their session stops renewing and they no longer get in. The consignee still exists and can keep receiving notices by link.

## 3. The consignee's space
Everything comes from their session; never from what they type on the screen. Of the portal routes they only reach their own (`/api/v1/portal/consignee/*`); the client's (shipments, messages with the team, consignee notices, giving access) respond **403 `This route is not available for portal users.`** And a person of the client does not get into their space: **403 `This space is only for consignees with access.`**

### 3.1 Their profile and how they receive notices
Shows their name, the client that serves them and, by **e-mail** and by **text**, the contact, whether they receive it today and whether they can turn it back on.
- **The client turns on** the notices and the medium (chapter 34) and the consignee is notified through that medium.
- **The consignee can turn it off whenever they want** (it is kept in the log, origin “consignee”).
- **Turning it back on:** only the medium **they turned off themselves**, and only if the client still has the notice on and they did not unsubscribe with the link in a notice. What the client never turned on, or the client turned off, **only the client turns on**.

| Situation | Response |
|---|---|
| Turning on a medium they cannot | **409 `Only your client can turn on this notice: you can only turn back on the one you turned off.`** |
| Turning on a medium without a registered contact | **400 `You do not have that contact medium registered: ask your client to add it.`** |
| Medium that is neither e-mail nor text | **400 `Enter the notice medium: email or text.`** |
| Not stating whether they want it or not | **400 `State whether you want to receive notices through that medium or not.`** |

### 3.2 Their inbox
The notices that **were sent to them** (newest first, up to 100 per page): “Your package is on its way”, “was delivered”, “had a failed delivery attempt”…, the waybill, the sending state of the notice (sent, skipped, failed) and the shipment's tracking link. They only see their own.

### 3.2b Their shipments (Batch 117d) — view only, no creating shipments
The consignee **sees their client's shipments that are delivered at their consignee** (list with progress bar, search by **waybill**, most recent first; and the record with its delivery address, the packages —without weights—, the progress and the timeline). **They do not create shipments** (dictated by Luis, 2026-10-18): the client (portal) and the company's staff (intake) create them; if they try, the client's route responds 403. They do not see prices, invoice, COD or the client's shipment number either.

| Situation | Response |
|---|---|
| Shipment of another consignee of the same client, of another client or nonexistent | **404** (the same for all) |
| A person of the client calls their routes | **403 `This space is only for consignees with access.`** |
| The consignee calls `POST /api/v1/portal/orders` or the shipment form | **403 `This route is not available for portal users.`** |

### 3.3 Messages with their client (Batch 117b)
The consignee **writes to their client** and the client answers. It is separate from the client's messages with the company's team (chapter 29).
- **On the consignee's side:** a single conversation with their client, which **opens with their first message**; they see what the client answers, how many new messages they have and mark them read. Nobody else from their company sees theirs.
- **On the client's side:** the **administrators and operators** see the conversations of their consignees (the most recent first; “unread” filter; search by consignee, person, e-mail or text of the last message) and **answer**; whoever **can only view** reads them, without answering. Reading is **shared**: when one opens or answers it, it is read for everyone of their client. They never see another client's conversations.
- **E-mail notice to the client (Batch 117e, requested by Luis):** when a consignee writes to them, the client's **administrators and operators** receive an e-mail (“<consignee> wrote to you”) with the link to their messages and **without the text of the message**. It is **a single e-mail while there are unread messages from the consignee** (several in a row do not send several); when the client reads them, the next one notifies again. The client answering sends no e-mail. If e-mail is not configured (or fails), the message is sent anyway and the “unread” counter remains.
- Messages **are not edited or deleted**. Limit of **2000 characters** and **30 messages per hour** per person.
- **In the wide web (Batch 213):** the client's Messages tab is a two-column inbox: on the left “Conversations” (Customer service first and below “Messages from my consignees” with the unread ones, search box and “Unread only”, with its total “N of M”); on the right the open conversation with its name at the top. On the phone it is still the list screen and the conversation apart.

| Situation | Response |
|---|---|
| Empty message | **400 `Write the message.`** |
| More than 2000 characters | **400 `The message cannot exceed 2000 characters.`** |
| More than 30 in one hour | **429 `Too many messages in a row; please wait a while before writing again.`** |
| Someone who can only view answers | **403 `Your role in the portal is view only: you can read the consignees' messages, not answer them.`** |
| Conversation of another client or that does not exist | **404** (the same for both) |
| A consignee gets into the client's inbox, or a person of the client writes as a consignee | **403** |

## 4. Endpoints
| Endpoint | What for | Who |
|---|---|---|
| `GET /api/v1/portal/consignee-access/{locationId}` | Who has access to that consignee | Person of the client |
| `POST /api/v1/portal/consignee-access/{locationId}` | Invite (`email`, `fullName`) | Client admin/operator |
| `POST /api/v1/portal/consignee-access/{locationId}/{id}/resend` | Resend the invitation | Client admin/operator |
| `DELETE /api/v1/portal/consignee-access/{locationId}/{id}` | Remove the access | Client admin/operator |
| `GET /api/v1/portal/consignee/me` | Their profile and their notice media | Consignee |
| `PUT /api/v1/portal/consignee/me/notices/{EMAIL or SMS}` | Turn off or turn back on (`enabled`) | Consignee |
| `GET /api/v1/portal/consignee/notices` | Their inbox | Consignee |
| `GET /api/v1/portal/consignee/orders?search=&skip=&take=` · `GET .../orders/{id}` | My shipments (read-only) · the record of one | Consignee |
| `GET /api/v1/portal/consignee/messages?before=&take=` | My conversation with my client | Consignee |
| `POST /api/v1/portal/consignee/messages` | Write to my client (`body`) | Consignee |
| `POST /api/v1/portal/consignee/messages/read` · `GET .../messages/unread` | Mark read · how many unread | Consignee |
| `GET /api/v1/portal/consignee-messages?unreadOnly=&q=&skip=&take=` | The conversations of my consignees | Person of the client |
| `GET /api/v1/portal/consignee-messages/unread` | How many conversations have unread messages | Person of the client |
| `GET /api/v1/portal/consignee-messages/{id}?before=&take=` | Open a conversation | Person of the client |
| `POST /api/v1/portal/consignee-messages/{id}` · `POST .../{id}/read` | Answer · mark read | Admin/operator (answer) |

## 5. The screens (client app, Batch 117c)
The client app (`app-cliente`, web and mobile) serves both: according to the session token (claim `loc`) it shows the space of the client's person or the consignee's. It is only an **interface orientation**: the server is what limits (403).

**On the client's side** (administrator or operator; whoever can only view sees without changing):
- **Settings → Set up notices → “Access”** on each consignee's card: the list of who has access (with their state: Invitation pending, Has access…), **Give access** (e-mail for the invitation —if left empty the consignee's main one is used— and optional name), **Resend invitation** (only to those who have not accepted) and **Remove access**. It says “Invitation sent to …”.
- **Messages → “Messages from my consignees”** (with the unread conversations): list with search and “Unread only”; tapping one opens the thread (bubbles, “Load earlier messages”, writing up to 2000 characters) and marks it read. Whoever can only view reads without a box to write (`Your role is read-only: you can read the messages, not answer them.`). The Messages tab adds up the team's and the consignees'.

**On the consignee's side** (four tabs, no Home; goes straight to Notices):
- **Shipments:** their shipments with the progress bar, search by waybill and the record of each one; **read-only**.
- **Notices:** their inbox (title, waybill, sending state, medium and date, and “View tracking” if the shipment has a link).
- **Messages:** their conversation with their client (opened with their first message).
- **Settings:** their account (name, e-mail, consignee and client), **“How I receive notices”** with one checkbox for e-mail and another for text (turning off always; turning back on only what they turned off themselves, and why they cannot is explained in each case: “Only your client can turn it on.”, “You unsubscribed with the link in a notice.”), language, light/dark theme, trusted computers and sign out.
- Everything in **Spanish and English** and in **light or dark** theme like the rest of the app.

## Frequent cases
- **“The consignee does not receive the invitation”:** check the e-mail typed and the spam folder; **Resend** sends a new link (the previous one stops working).
- **“My link says it is invalid or expired”:** the link lasts 48 hours and works only once; ask the client to resend it.
- **“They signed in but see nothing about the shipments”:** correct: the consignee only has their shipments, their notices inbox, the messages with their client and their profile; they do not create shipments.
- **“I cannot turn the text notices back on”:** only the client can turn on a medium the consignee did not turn off themselves.
