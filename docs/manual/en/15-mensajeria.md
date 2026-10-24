# 15 — Internal messaging

Batch 53 (P2.1, 2026-10-15). The people of the company write to each other inside Teikem, in **threads**: a message can be **replied to** and the thread
is kept. It is not a chat with many features, but it feels like one: what is written reaches **instantly** whoever has Teikem open, and for whoever does not,
**it stays pending** until they sign in. The screen is described in [L53 — Messages](frontend/l53-mensajes.md).

## 1. Who can use it

- Permission **`messages.use`** (“Use internal messaging”). It was added to **all the roles that already existed** (company staff). **A new role does not get it by itself**:
  the administrator assigns it in System → Roles & users.
- **Client portal users** (when it exists): they do not have it by default. The owner assigns it **only to the managers of each client**; data entry
  users cannot write. A client user will only be able to write to whoever is designated and never to users of other clients (to be reviewed when the portal is built).
- Each person sees and touches **only their threads** of the active company. If they are not part of a thread, **“it does not exist”** for them (404: its existence is not revealed).

## 2. Thread types

| Type | How it is created | Who can reply |
|---|---|---|
| **Between two people** (`DIRECT`) | “New message” to **a single person** | Both. It is **unique per pair**: writing to the same person again goes back to the same thread. It cannot be left and no people are added to it. |
| **Group** (`GROUP`) | “New message” to **several people** or to **a role** | Everyone. Whoever created it adds people; anyone can **leave** (and stops seeing the thread). |

- **Message to a role**: a group is built with the **active people who have that role today** (“snapshot”). Whoever joins the role later **does not get in by themselves**; they can be added by hand.
  The group name is whatever is typed, or the role's name, or (“Ana, Luis, +2”) the names of the other people.
- **Whoever joins a group later** sees the history **only from when they join**.
- Messages are **never edited or deleted**. Up to 2000 characters per message.

## 3. How it arrives

- **Live**: an open Teikem keeps a live connection with the server; each new message is pushed instantly to the other people in the thread, and when it appears in the header
  the **message counter** goes up and **Kemi** says “You got a message from Ana → View”. If the person is already viewing that thread, the message is marked read by itself and there is no notice.
- **Without a session**: the message stays in the thread; when signing in, the person sees their unread counter and Kemi greets them with what is pending.
- **Muted thread**: it still counts as unread, but Kemi does not notify.
- **Fallback**: if the live connection drops (network, proxy), the web app reconnects by itself (at 0, 2, 5, 10 and 30 s) and, meanwhile, asks for what is new every 30 s. Nothing is lost.
- **Unread**: the messages **from other people** newer than the last thing the person read (and than their joining the group). Replying counts as having read what came before.

## 4. Presence (“online”)

An open Teikem reports every 30 s that it is still there. **Online** = seen less than 90 seconds ago; otherwise it shows “Seen 5 min ago”, “Seen 3 h ago” or “Seen on 10/12/2026”. It is shown in the
conversation list (green dot), in the thread and in the people search box. Only the presence of people from **the same company** is shown.

## 5. API (everything with `messages.use`)

`GET /api/v1/messages/people?q=&take=` · `GET /roles` · `GET /threads` · `POST /threads` (to people and/or to a role, with the first message) · `GET /threads/{id}?before=&take=` (the last 50;
`before` asks for the previous ones) · `POST /threads/{id}/messages` · `POST /threads/{id}/read` · `POST /threads/{id}/members` · `POST /threads/{id}/leave` · `PUT /threads/{id}/mute` · `GET /unread`.
Presence: `POST /api/v1/presence/ping` (anyone with a session). Live connection: `/hubs/messages` (SignalR; the token goes in the `access_token` query). There is no PATCH or DELETE of messages.

## 6. Validations and messages

| Situation | Exact message | HTTP |
|---|---|---|
| Empty message | `Write the message.` | 400 (`text`) |
| More than 2000 characters | `The message cannot exceed 2000 characters.` | 400 (`text`) |
| Group name longer than 100 characters | `The group name cannot exceed 100 characters.` | 400 (`title`) |
| No people or role | `Choose at least one person or a role.` | 400 (`userIds`) |
| Only oneself | `You cannot write to yourself.` | 400 (`userIds`) |
| Person that does not belong to the company (or inactive) | `User not found in this company.` | 400 (`userIds`) |
| Role that does not belong to the company | `Role not found.` | 400 (`roleId`) |
| Role without active people | `That role has no active people to write to.` | 400 (`roleId`) |
| Nonexistent thread, someone else's or one already left | `Conversation not found.` | 404 |
| Adding people without having created the group | `Only whoever created the group can add people.` | 403 |
| Adding people to a thread between two | `No more people can be added to a conversation between two people; create a group.` | 409 |
| Leaving a thread between two | `A conversation between two people cannot be left.` | 409 |
| Adding without saying whom | `Indicate whom to add.` | 400 (`userIds`) |
| Without the permission | “No permission” | 403 |

## 7. For support

- Tables `MessageThread`, `MessageThreadMember`, `Message` and `UserPresence`; section **2026-10-15 (h)** of `Diseño/logistica-db-update.sql` (adds `messages.use` to all existing roles).
- **WebSockets**: the live connection uses WebSockets (and, if the server or a proxy does not let them through, SignalR falls back by itself to other transports). In IIS the **WebSocket Protocol** must be enabled;
  behind a reverse proxy (nginx, etc.) the route `/hubs/messages` must allow `Upgrade`/`Connection`. Without a live connection everything keeps working with the 30 s fallback.
- **A single server instance** for now: with several, the live push only reaches those connected to the instance that received the message (the others find out with the 30 s fallback).
  If several instances at once are ever needed, a “backplane” (SQL Server or Redis) is added without changing anything else.
- Live pushes **do not carry message data the person could not request through the API**; they are only “something changed” + a short summary for Kemi's bubble.
- Presence is a per-person value (`UserPresence.LastSeenUtc`); it does not store which screen they are on.

## 8. Messaging with the driver, talking about an order or route, and owners (Batch 212, 2026-10-23)

### 8.1 Who sees whom: each person's “Messaging” setting
Each person has, **per company**, one of three values (changed by whoever manages users, permission `admin.users`):

| Value | What it means |
|---|---|
| `FULL` — Full (default) | Sees everyone in the company, and everyone sees them. |
| `OFFICE` — Office only | Writes and receives, but their list only shows `FULL` people. **Users linked to a driver are set this way** (when the driver is created or the user is linked to it; those that already existed were moved to `OFFICE` in section 2026-10-23 (i) of the SQL). |
| `NONE` — No messaging | Does not appear in any list and cannot open or write threads. |

- For two drivers to write to each other, both are set to `FULL` (or whoever is wanted). The office can create a group with whomever it wants.
- API: `GET /api/v1/messaging-modes` (list) and `PUT /api/v1/messaging-modes/{userId}` with `{"mode":"FULL|OFFICE|NONE"}`. Both with `admin.users`.
- Existing threads keep working if the mode changes; what changes is whom one can write to anew.

### 8.2 Talking about an order or a route
A message can carry a **reference** (`ref`: `{"entity":"ORDER"|"TRIP","publicId":"…"}`) when creating the thread (`POST /messages/threads`) or when sending (`POST /messages/threads/{id}/messages`). In the thread it is shown as a **card** (order number or route code); in the list, the preview says `[2026-0001] texto`.
- The reference goes **in the message**, not in the thread: Ana and Luis have a single thread between two and can talk about one order today and another tomorrow.
- The card shows the detail (client / zone and status) and `canOpen = true` **only if whoever sees it has `orders.view` / `trips.view`**; otherwise they only see the title.
- `GET /messages/about?entity=ORDER|TRIP&publicId=…` lists the messages of **my** threads that talk about that order or route (for the “Messages” tab of the record). It does not reveal threads I am not part of.

### 8.3 Suggested: “whom do I talk to about this?”
`GET /messages/suggested?entity=ORDER|TRIP&publicId=…` proposes, in this order and without repeating: (1) the **owners of the route** (for an order, the active route it goes on), (2) the **owners of the zone** of that route, (3) the **roles that dispatch** (those with the permission `trips.dispatch`) as a group, (4) up to 5 **recent conversations**. Each suggestion says where it comes from. It respects the setting in point 8.1 (it never suggests someone the asker cannot see, nor oneself). **It opens nothing by itself**: the person chooses.

### 8.4 Owners
The owners of a **route** or of a **dispatch zone** (several per entity):
- `GET /messages/responsibles?entity=TRIP|DISPATCH_ZONE&key=…` (`key` = `publicId` of the route, or the zone id). Anyone with `messages.use`.
- `PUT /messages/responsibles` with `{"entity","key","userIds":[…]}` leaves **exactly** those people. A route requires `trips.plan`; a zone, `fleet.manage`.
- A new route is born with **whoever created it** as owner.
- The table is generic (`EntityResponsible`): tomorrow it can hang from a client or an order without changing the schema.

### 8.5 Exact messages and codes
| Situation | Message | HTTP |
|---|---|---|
| Invalid mode | `Messaging must be FULL (full), OFFICE (office only) or NONE (no messaging).` | 400 |
| Writing without messaging (`NONE`) | `Your user does not have messaging enabled in this company.` | 403 |
| Writing to someone who cannot be seen (because of their mode) | `User not found in this company.` | 400 |
| Reference of another type | `The reference must be an ORDER (order) or a TRIP (route).` | 400 |
| Reference that does not exist | `The order or route the message refers to does not exist in this company.` | 400 |
| Owners: invalid entity | `The entity must be TRIP (route) or DISPATCH_ZONE (dispatch zone).` | 400 |
| Owners: invalid key | `The entity key is not valid.` | 400 |
| Owners: user from another company | `User not found in this company.` | 400 |
| Owners without permission | `You do not have permission to change the owners (trips.plan).` (or `fleet.manage`) | 403 |

## 9. Chat photos, notice to the driver's phone and role groups (Batch 212 B, 2026-10-23)

### 9.1 Photos in the chat
- A message can carry **up to 4 photos** (JPG, PNG or WEBP, up to 3 MB each; the app shrinks them by itself). They are sent in `attachments` (`{fileName, dataBase64}`) when creating the thread or when sending. **A message can be photos only** (no text): in the list it shows “📷 Photo” or “📷 3 photos”.
- Each photo is stored **encrypted on the server**, with a thumbnail (320 px) for the thread. It is downloaded with `GET /api/v1/messages/attachments/{publicId}` (`?thumb=true` = thumbnail). **Only whoever is part of the thread** can download it (404 otherwise); the phone does not save it: it is viewed on screen.
- **Chat photos live a short time** (30 days by default). **Evidence** (delivery, exception, damage) does **not** go in the chat: it is captured on its own screen and stays attached to the order.
- If the server platform cannot generate thumbnails, the message is saved anyway and the thumbnail is served as the full photo (on Windows it always works).

### 9.2 How long they live: company setting
`GET/PUT /api/v1/messaging-settings` (permission `admin.tenant`): `photoDays` (1 to 3650, 30 by default) and `photoAction`:
- `DELETE` (default): when it expires, the **content is deleted** from the server. In the thread it reads “The photo was deleted on 2026-11-22.”.
- `ARCHIVE`: when it expires, the photo **stops showing** in the chat but **the content is kept** (evidence). It is not yet moved to cold storage: that comes with cloud storage.
- The **text of the messages is never deleted**. The scheduled job “Clean up chat photos” (`MESSAGE_ATTACHMENT_PURGE`, every night at 03:45; up to 500 per run) does the work; it is seen and run from Scheduled jobs.

### 9.3 Push notification to the driver's phone
Each new message reaches the driver as a push notification on their phone (title = who writes, or the group name; text = the message, in a group “Ana: …”), **unless they muted the thread**. When tapped, the app opens that thread (`data.kind = MESSAGE`, `data.thread`, `data.tenant`). Without Firebase configured (`Push:Fcm:ServiceAccountJsonPath`) nothing goes out and the message is still in the chat. A token Firebase rejects (app uninstalled) is deactivated by itself. Today the phone notification is for drivers; staff will have it with their app.

### 9.4 “Live” role group
`POST /messages/threads` with `roleId` and `followRole: true` creates (or **reuses**: there is only one per role and company) that role's group. The people who have the role join, and the list **syncs by itself** every time someone writes or opens the group: if someone receives the role, they join (they see the history only from when they join); if they lose it, they leave. **Whoever writes to the group without having the role (a driver) stays.** People are not added by hand to a role group. Without `followRole`, “writing to a role” stays as before (a “snapshot” of those who have it today).

### 9.5 Exact messages and codes
| Situation | Message | HTTP |
|---|---|---|
| More than 4 photos | `A message can carry up to 4 photos.` | 400 |
| Invalid or empty base64 | `The photo is not valid (base64 of a JPG, PNG or WEBP image).` | 400 |
| Type other than JPG/PNG/WEBP | `Only JPG, PNG or WEBP photos are accepted.` | 400 |
| Photo larger than 3 MB | `Each photo cannot exceed 3 MB (the app shrinks them by itself).` | 400 |
| File store not configured | `The file store is not configured (Files:MasterKey); contact support.` | 400 |
| No text and no photos | `Write the message.` | 400 |
| Photo from a thread I am not part of | `Photo not found.` | 404 |
| Photo already deleted | `The photo was deleted on YYYY-MM-DD.` | 409 |
| Photo already archived | `The photo was archived on YYYY-MM-DD and is no longer shown in the chat.` | 409 |
| Photo days out of range | `The chat photo days must be between 1 and 3650.` | 400 |
| Invalid action on expiry | `On expiry, the photo can be DELETE (deleted) or ARCHIVE (archived).` | 400 |
| `followRole` without `roleId` | `For a role group, indicate the role (roleId).` | 400 |
| Adding people to a role group | `People are not added to a role group: whoever has the role joins.` | 409 |

## 10. On the web (Batch 212 D)
Everything above has a screen on the web: photos and cards in the conversation, “Talk about this order/route” with suggestions, owners on the route and the zone, the “Messaging” column in Users and photo retention in Settings → Operations. See [L212 — Messaging on the web](frontend/l212-mensajeria-web.md).
