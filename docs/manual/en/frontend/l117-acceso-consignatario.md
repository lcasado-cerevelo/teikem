# L117 — Consignee access (client app)

Batch 117 screens in the client app (`app-cliente`, web and mobile). Screenshots taken by the `web-app/e2e/lote117-consignatario.spec.ts` walkthrough against the real API (desktop; the same walkthrough passes on mobile at 412 px with no horizontal scroll). Rules and messages: [chapter 35](../35-acceso-del-consignatario.md).

## On the client's side (administrator or operator)

### Giving access to a consignee
**Settings → Notices to my consignees → Set up notices**, on the consignee's card: **Access** button. You type the invitation e-mail (empty = the consignee's main one) and the name.

![Invite](img/l117-acceso-invitar.png)

When inviting, “Invitation sent to …” appears and the person becomes **Invitation pending**, with **Resend invitation** and **Remove access**.

![Invitation pending](img/l117-acceso-pendiente.png)

### Messages from my consignees
In the **Messages** tab, at the top, the access to the conversations with how many are **unread** (it also adds to the tab's badge).

![Client messages](img/l117-cliente-mensajes.png)

The list (most recent first, with search and “Unread only”) and, when tapping one, the thread where you reply.

![Conversations](img/l117-cliente-conversaciones.png)
![Conversation](img/l117-cliente-conversacion.png)

## On the consignee's side
They sign in with their e-mail and password (without two-step verification) and only have **four tabs**: Shipments, Notices, Messages and Settings. There is no Home and no way to create orders.

### Notices (the inbox)
![Notices](img/l117-consignatario-avisos.png)

### Shipments (read-only)
Their shipments with the progress bar and search by waybill; when tapping one, its file (address, packages, timeline), without prices.

![Shipments](img/l117-consignatario-envios.png)
![Shipment](img/l117-consignatario-envio.png)

### Messages with their client
![Messages](img/l117-consignatario-mensajes.png)

### Settings
Their account and **How I receive notices**: they can turn off e-mail or text and turn back on only what they themselves turned off.

![Settings](img/l117-consignatario-ajustes.png)
![Settings in dark](img/l117-consignatario-ajustes-oscuro.png)
