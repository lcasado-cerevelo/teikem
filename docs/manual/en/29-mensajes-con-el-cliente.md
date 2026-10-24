# 29 — Portal messages with the customer service team
<!-- kemi:portal -->

Batch 104 (client portal, 2026-10-17). Each person of a client can write to the company's **customer service team** from the client app, and the team answers them —or writes first— from the company's web app. Decided with Luis: **one conversation per person** (nobody from their company sees what someone else wrote) and **the team can open the conversation** by writing first to a person from the client's file.

## 1. Who the team is
- **Team assigned per client:** in the client record, **Customer service team** panel, whoever has `clientmessages.manage` presses **Assign team** and checks the people of the company who serve that client (`PUT /api/v1/client-messages/clients/{id}/team`). Only active users of the company can be in it; otherwise: **400** “Only active users of the company can be on the team.”
- **No team assigned:** it is served by **whoever has the permission `clientmessages.reply`** (the “Customer service” role). The panel warns about it (“This client has no assigned team: it is served by the N people with the permission to handle client messages”).
- **Company administrator:** sees and answers all conversations of all clients.
- A team member sees the conversations of the clients where they are assigned **plus** those of the clients that have no team.

## 2. Permissions and module
<!-- kemi:staff -->
| Permission | What it allows |
|---|---|
| `clientmessages.reply` | See the “Client messages” inbox, reply, write first to a person, see a client's team |
| `clientmessages.manage` | Assign each client's team |

Module **CLIENT_PORTAL**. The **Customer service** role template (for new companies) comes with `clientmessages.reply` and read access to clients, orders, locations and contracts; the company administrator already has both permissions. In an existing company, the administrator can create the role with those permissions.

## 3. On the client's side (client app, “Messages” tab)
- The person sees **their** conversation: their messages on the right, the team's on the left with the name of whoever answered. “Load earlier messages” brings the oldest.
- Maximum **2000 characters** per message (there is a counter); empty → **400** “Write the message.”; more than 2000 → **400** “The message cannot exceed 2000 characters.”
- Limit of **30 messages per hour** per person: past the limit, **429** “Too many messages in a row; please wait a while before writing again.”
- The tab shows the **number of unread messages from the team** (checked every 30 s); on opening the screen they are marked as read. The screen refreshes by itself every 15 s.
- A portal user **cannot** call `/api/v1/client-messages/*` (403): they only see their own conversation through `/api/v1/portal/messages`.

## 4. On the company's side (web)
<!-- kemi:staff -->
- **Client portal → Client messages** (`/portal/messages`): list of conversations (free-text search, “Unread only”, client, person, last sentence and time, unread badge) and, on the right, the conversation with its composer. On mobile one or the other is shown with “Back”.
- On opening a conversation it is marked read **for the whole team**. The list refreshes every ~20 s and the open conversation every ~10 s; there is no live notice or e-mail to the team (see FAQ).
- **Write to a person** (client record → Customer service team): choose the person (active portal user of that client) and write the first message. If the person does not belong to that client or is deactivated: **400** “The person is not an active portal user of that client.” If there was already a conversation, the message is added to it.
- Messages are **never edited or deleted**.

## 5. Endpoints
| Side | Endpoint |
|---|---|
| Portal | `GET /api/v1/portal/messages?before&take`, `POST /api/v1/portal/messages`, `POST /api/v1/portal/messages/read`, `GET /api/v1/portal/messages/unread` |
| Company | `GET /api/v1/client-messages/conversations`, `GET …/unread`, `GET …/conversations/{id}`, `POST …/conversations/{id}/messages`, `POST …/conversations/{id}/read`, `POST …/conversations` (write first), `GET …/clients/{id}/people`, `GET/PUT …/clients/{id}/team`, `GET …/team-candidates` |

## 6. Frequent cases
- **Nobody answers:** check that the client has a team or that someone has `clientmessages.reply`; the team gets no notice, it has to open the inbox.
- **Ana sees Beto's messages (same company):** not possible; each conversation belongs to one person.
