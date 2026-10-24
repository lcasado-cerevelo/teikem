# 23 — Client notices

Batch 98 (P10, 2026-10-17). Teikem notifies the **client** (the sender, who ships) about what is happening with their shipments, their COD and their invoices, with **summaries** —not the detail of each shipment—. **The consignee is not notified** from here (consignee notices, with their acceptance, are managed by the client from the portal; they come in a later batch).

## 1. Who decides what

**The company decides, client by client, which notices they receive and through which channel.** Without configuration, everything is **off**: a client receives nothing until the company turns it on. The configuration lives in the client record (**Clients & contracts → “Client notifications” panel**). Viewing requires `clients.read`; changing, `clients.update`. Each change stays in the log.

## 2. The five notices

| Notice (`kind`) | When | What the summary says |
|---|---|---|
| **Picked up** (`PICKED_UP`) | The client's shipments move to Picked up | “Picked up: N box(es) from M shipment(s)” (with the route if there is one) |
| **Out for delivery** (`OUT_FOR_DELIVERY`) | The route goes out to deliver (the shipments move to In transit) | “Out for delivery: N box(es) from M shipment(s) on route X” |
| **Delivered** (`DELIVERED`) | The shipments move to Delivered | “Delivered: N box(es) from M shipment(s) on route X. COD collected: $Y” |
| **COD processed** (`COD_PROCESSED`) | A COD remittance of the client is **remitted** | “COD remittance REM-#####: collected, commission, net (N shipments)” |
| **Invoice issued** (`INVOICE_ISSUED`) | An invoice of the client is **issued** | “Invoice FAC-##### for $T, due on YYYY-MM-DD” |

**One summary per client and route** (the first three), per remittance and per invoice. The summary goes out when the group has had **3 minutes without new events**, so a route that keeps delivering sends a single notice. The text goes in the company's language.

## 3. Channels

| Channel | What it does |
|---|---|
| **Email** | It reaches the client's **active portal users** (invited users who have not signed in do not receive it) and the **extra emails** of that notice (up to 10). Sender with the company name. |
| **Portal inbox** | The summary stays in the client's portal inbox (they will see it when the portal exists, following batches). |
| **SMS** | The preference and the **extra phones** (up to 10) are saved; **it does not go out yet** until the provider (Twilio) is contracted: the delivery is left as “skipped” with that reason. |

A failed delivery is **retried** at 1, 5 and 30 minutes and then is left as “failed”. Without email configured on the platform, the delivery is left as “skipped” with the reason. If there are no recipients (no portal users or extra emails), the summary still stays in the inbox and the log explains why there was no email.

## 4. Validations (400)

| Situation | Message |
|---|---|
| Notice that does not exist | `The notice does not exist.` |
| A notice repeated in the same request | `Each notice can only come once.` |
| Misspelled email | `The email '{email}' is not valid.` |
| Misspelled phone | `The phone '{phone}' is not valid.` |
| More than 10 emails or 10 phones | `At most 10 extra emails and 10 extra phones per notice.` |
| List longer than 400 characters | `The extra emails cannot exceed 400 characters.` / `The extra phones cannot exceed 400 characters.` |
| Client does not exist (or belongs to another company) | 404 |

## 5. Endpoints

| Endpoint | What for | Permission |
|---|---|---|
| `GET /api/v1/clients/{id}/notifications/settings` | The five notices with their configuration (off if there is none), how many portal users would receive the email and whether email is configured | `clients.read` |
| `PUT /api/v1/clients/{id}/notifications/settings` | Saves the configuration (validates everything before saving anything) | `clients.update` |
| `GET /api/v1/clients/{id}/notifications/log` | The summaries sent, with the result of each delivery | `clients.read` |

The scheduled job `CLIENT_NOTIFICATION_DIGEST` (“Client notices”, every 2 minutes, active) builds the summaries and the deliveries. Events are only recorded for clients that have that notice turned on (the others generate nothing).

## 6. The screen

See [the screen manual, L98](frontend/l98-avisos-cliente.md).

## Frequent cases

- **The client did not receive the email:** look at the panel's log (each summary says sent, skipped or failed and why); check that the client has **active** portal users or extra emails, and that email is configured on the platform.
- **Two “Delivered” notices went out:** they are two different routes (one summary per route) or more than 3 minutes passed between one delivery and the next.
- **I turned on a notice and nothing that happened before arrived:** events are only recorded from the moment the notice is on.
