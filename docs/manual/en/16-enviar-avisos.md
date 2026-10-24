# 16 — Send notices (manual notices)

Batch 54 (2026-10-15). Besides the automatic notices from the rules (chapter [14](14-avisos.md)), a person with permission can **send a notice by hand** to one or several people, to one or several roles, or to all the
people of the company. It is a **notification**: **it cannot be replied to** (to converse there is [messaging](15-mensajeria.md)). The screen is described in [L54 — Send notices](frontend/l54-enviar-avisos.md).

## 1. Who can

- Permission **`notifications.send`** (“Send notices to other people”). It was added to the **Company admin** (template and already cloned roles). **It is not given to other roles by default**: the administrator assigns it to whomever they want.
- Module **System** (always on). The whole API requires the permission; there is no PATCH, PUT or DELETE of notices.
- Whoever **receives** a notice needs no permission: it belongs to their own bell.

## 2. How it is sent

- **To whom**: people (`userIds`), roles (`roleIds`: those who have the role **today**) or **everyone** (`all = true`: all active people of the company; then `userIds` and `roleIds` are ignored). Always **without repeats**, only **active people of the company** and **never notifying oneself**.
- **What**: title (required, up to 150) and text (optional, up to 1000).
- **How it arrives**: each person receives a notice in their **bell** with “From {whoever sent it}”. If they have Teikem open they see it **instantly** (live connection, same as messages) and Kemi notifies; otherwise, **it stays pending**.
- **History** (`Announcement`): to whom it was sent (readable text: “Management, Ana Rivera, +2” or “All people”), how many people received it and **how many have read it**. It is never edited or deleted.

## 3. API (everything with `notifications.send`)

`GET /api/v1/announcements/people?q=&skip=&take=` (people who can be notified, with the total; up to 500 per page) · `GET /announcements/roles` (roles with how many active people they have) · `POST /announcements` · `GET /announcements?skip=&take=` (history).

## 4. Validations and messages

| Situation | Exact message | HTTP |
|---|---|---|
| No title | `Enter the notice title.` | 400 (`title`) |
| Title longer than 150 characters | `The notice title cannot be longer than 150 characters.` | 400 (`title`) |
| Text longer than 1000 characters | `The notice text cannot be longer than 1000 characters.` | 400 (`text`) |
| No people, roles or “everyone” | `Choose at least one person, a role or everyone.` | 400 (`userIds`) |
| Person that does not belong to the company (or inactive) | `User not found in this company.` | 400 (`userIds`) |
| Role that does not belong to the company | `Role not found.` | 400 (`roleIds`) |
| No one to send to (role without active people, or only oneself) | `There are no people to send the notice to.` | 400 (`userIds`) |
| Without the permission | “No permission” | 403 |

## 5. For support

- Table `Announcement` and the column `UserNotification.AnnouncementId` (that is where “From …” comes from); section **2026-10-15 (i)** of `Diseño/logistica-db-update.sql`. Manual notices carry `EventCode = ANNOUNCEMENT` in the inbox and **cannot be muted**.
- The live push (`NoticeReceived`) uses the same connection as messages (`/hubs/messages`); every person with a session has it, whether or not they have the messaging permission. If the connection drops, the bell counter is still updated with its 30 s polling.
- A notice to **all** people creates one inbox row per person (in batches of 500, in a single transaction).
