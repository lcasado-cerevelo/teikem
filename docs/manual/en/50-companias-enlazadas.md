# 50 — Linked companies (one company dispatches through another)

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter `docs/manual/50-companias-enlazadas.md`. The server only answers in Spanish, so every "Exact message" column keeps the **exact Spanish text** the screen shows, followed by its English meaning.

Batch 227 (2026-11-07) — salespeople's order, part P7. A Teikem company (for example **Advance Solutions**) can be a **client of another** (for example **Advance Logistics**) and make its deliveries through it. When both accept the link, the delivery of a Solutions dispatch **does not create the transport order in Solutions: it creates it in Logistics**, in the name of a Logistics client that represents Solutions, and brings back the number, the status and the proof of delivery. **Without a link accepted by both, nothing crosses.** The screen is in **System → Integrations / API → «Linked companies» tab** (`/system/integrations?tab=links`); the delivery is requested from the sales order ([18](18-ordenes-de-venta.md#24-delivery-by-the-linked-company-batch-227)).

> Screenshots pending (P8): the «Linked companies» tab (list of links and deliveries), the «Propose a link with another company» window, «Accept the link with …» (with the client that represents it), «End the link», the «Could not be created» delivery with its message and «Retry», and the sales order dispatch with the other company's number and status and «View proof».

## 1. Who can and where

- Module **Linked companies (`INTERCOMPANY`)**: it depends on **Integrations (`INTEGRATIONS`)** and comes **off**. It must be turned on in **both companies**. The one that provides the transport also needs the **Transport (`LTL_GROUND`)** module. With the module off, the whole bridge API answers 403.
- Two new permissions, Administration category. The **Company admin** has them (also in companies that already existed); the administrator assigns them to any other role:
  - **`intercompany.view`** — see the links and their deliveries. Implies `integrations.view`.
  - **`intercompany.manage`** — propose, accept, reject and end links. Implies `intercompany.view`.
- **Proposing and accepting also require a recent re-authentication (AAL2)**: they give another company the power to create orders in yours or to receive your data.
- **Deliveries**: seen with `intercompany.view` or `sales.view`; **Retry** and **Check now** are done by whoever has `intercompany.manage` or `warehouse.pick`.
- Whoever proposes can only link with **other companies they also belong to** (the same owner). The platform sees all.

## 2. How it works, in short

- There are two roles. The **client** company (`CLIENT`, the one that dispatches) and the one that **provides the transport** (`PROVIDER`). In the example: Solutions is a client of Logistics.
- Either one can propose. The proposal counts as the acceptance of whoever proposes; **the other accepts**. If the one that provides the transport accepts, it **chooses its client** (the Logistics client in whose name Solutions' orders will be created).
- With the link **Active**: when the delivery of a dispatch is created in the client company, the pick batch becomes packed, a «to send» record is left and the transport order is created in the company that provides the transport. If that is not possible at that moment, the scheduled job sends it within 2 minutes or less.
- A client company has **at most one proposed or active link** (it dispatches through a single one). A client of the company that provides the transport represents **a single** linked company.
- **Ended or rejected, a link is not reopened**: another one is proposed.

## 3. Features

### 3.1 Propose a link

- **Who:** `intercompany.manage` + AAL2. Module `INTERCOMPANY`.
- **Screen:** **Propose link** (top right of the list). **Our role**, **Company**, **Client of this company that represents it** (only if our role is «We provide the transport») and **Note** (optional, up to 250 characters). The company list brings only the other ones you belong to; those without the module appear as «{name} (without the linked companies module)».
- **API:** `GET /api/v1/integrations/tenant-links/options` to fill the form and then `POST /api/v1/integrations/tenant-links`:

```json
{ "companyPublicId": "…", "role": "CLIENT", "clientPublicId": null, "note": "For the deliveries to pharmacies." }
```

- `role` is the role of **our** company. With `PROVIDER`, `clientPublicId` (one of our clients) is required.
- **Effect:** the link is left **Proposed** and the other company receives the `INTEGRATION_LINK_CHANGED` notice (if it has the module).

### 3.2 Accept a link

- **Who:** the **other** company (not the one that proposed), with `intercompany.manage` + AAL2.
- **Screen:** **Accept** action on the row (only visible on a link proposed by the other) → «Accept the link with {name}». If our company provides the transport, the client that represents the other must be chosen; the note is optional.
- **API:** `POST /api/v1/integrations/tenant-links/{id}/accept` with `{ "clientPublicId": "…", "note": "…" }` (`clientPublicId` only when whoever accepts provides the transport).
- **Effect:** the link becomes **Active**; from then on deliveries cross. It notifies the other company.

### 3.3 Reject or withdraw a proposal

- **Who:** `intercompany.manage`. The other company **rejects**; whoever proposed **withdraws** it (it is left as Rejected).
- **Screen:** **Reject** action (or «Withdraw proposal» if we proposed it), with the **reason**. **The server requires it** (up to 250 characters): if left empty it answers 400 «Escriba el motivo.» (Enter the reason.) even though the screen does not mark it as required.
- **API:** `POST …/{id}/reject` with `{ "reason": "…" }`.

### 3.4 End an active link

- **Who:** either of the two, with `intercompany.manage`. The **reason is required**.
- **Screen:** **End** action → «End the link with {name}».
- **API:** `POST …/{id}/end` with `{ "reason": "…" }`.
- **Effect:** from then on **nothing is sent or fetched**. What was already fetched stays as it was. Pending deliveries become «Could not be created» with «El enlace con «X» ya no está activo: la orden no se mandó.» (The link with «X» is no longer active: the order was not sent.). It notifies the other company.

### 3.5 What crosses to the other company

The order created in the company that provides the transport carries:

- **Consignee**: the one on the sales order (or a new one given in the delivery), as a **new consignee of the client over there**; there it is recognized by name and line 1. The code from here does not travel.
- **Packages**: type, description, pieces, weight and volume. No piece number (it is numbered over there). A package type that does not exist over there is replaced by the default one over there; the description stays.
- **COD**, dates (requested and promised), «confirm on creation» and **priority** (an **urgent** sales order is born with high priority).
- **Notes**: «Company · sales order OV-… · dispatch EMP-…» and the delivery notes (up to 500 characters).
- **Does not cross** the service type: over there the company's default is used.
- If the client over there types its own numbers, the order number is the dispatch's (EMP-…) and the invoice number is the sales order's (OV-…).
- The order is created «as System» (no user) and with the bridge's origin mark. It is the same path as `POST /orders` of the clients API: **the other company's own rules** apply (suspended client, current rate, packages…).

### 3.6 Deliveries by linked company

- **Who:** view, `intercompany.view` or `sales.view`; **Retry** and **Check now**, `intercompany.manage` or `warehouse.pick`. Only the **client** company sees them (they are its own).
- **Screen:** the second panel of the tab, **«Deliveries by linked company»**: sales order, dispatch, **Delivery** (state, company, number and status over there, who received and when, «View proof»), attempts, last check and creation date. Search by sales order, dispatch or number over there, and filter by state. The same information appears on the dispatch of the sales order.
- **Retry** (only on «Could not be created»): sends again at once, after fixing what the other company said. **Check now** (only on «Sent»): brings the status and delivery without waiting for the job.
- **API:** `GET /api/v1/integrations/tenant-links/deliveries?state=&q=&skip=&take=` · `GET …/deliveries/{id}` · `POST …/deliveries/{id}/retry` · `POST …/deliveries/{id}/sync`.
- **Automatic check:** the scheduled job `INTERCOMPANY_SYNC` (every 2 minutes, per company; changed in System → Scheduled jobs) sends what is pending and checks the deliveries that have not finished.
- **Automatic retries:** a transient failure (no network, the other company busy) retries on its own waiting 1, 2, 4… minutes (up to 60) and up to **10 attempts**; after that it is left «Could not be created». If the other company **rejects** the order (validation, suspended client, no rate), it is left «Could not be created» **right away** with the message from over there and **is not retried on its own**.

### 3.7 Proof of delivery

- **Who:** `intercompany.view` or `sales.view`.
- **How:** on the delivery, **View proof** (who received, when, notes, signature and photos). The full proof **is read live from the other company**, only for that order; the system only stores who received and when.
- **API:** `GET …/deliveries/{id}/proof` (answers **204** if there is none yet) and `GET …/deliveries/{id}/proof/files/{fileId}` for the signature and photos.

### 3.8 Billing «on delivery»

With sales billing set to «on delivery» ([19](19-facturacion.md)), a dispatch delivered by the linked company counts as **delivered** when the delivery over there is **Delivered** or has a proof. Before that it is **not invoiced**.

### 3.9 Notices

The system publishes them; **to whom and through which channel** is decided by a **notification rule** that the administrator builds (System → Notification rules): none is seeded. They are grouped under Integrations.

| Notice | Code | For whom | When |
|---|---|---|---|
| Linked company: link changed | `INTEGRATION_LINK_CHANGED` | the **other** company | they proposed, accepted, rejected or ended the link (cannot be silenced) |
| Delivery by linked company: status change | `INTEGRATION_LINK_DELIVERY_STATUS` | the client company | the order over there changed status (optional) |
| Delivery by linked company: could not be created | `INTEGRATION_LINK_DELIVERY_FAILED` | the client company | the other did not accept the order (cannot be silenced) |
| Urgent order from a linked company | `INTEGRATION_LINK_URGENT_ORDER` | the one that provides the transport | an urgent order came in (cannot be silenced) |

## 4. Fields and rules

| Field | Where | Rule |
|---|---|---|
| Our role (`role`) | Propose | `CLIENT` (we are the client) or `PROVIDER` (the other is our client); required |
| Company (`companyPublicId`) | Propose | required; one of the others you belong to, with the module on; not your own |
| Client (`clientPublicId`) | Propose with `PROVIDER`; accept as provider | required in those cases; a client of **your** company, existing, active and in Active status (neither prospect nor suspended); one per linked company |
| Note (`note`) | Propose, accept | optional, up to 250 characters |
| Reason (`reason`) | Reject, end | up to 250 characters; **required when rejecting and when ending** (the server requires it in both) |
| `state` | Deliveries list | `PENDING`, `CREATED` or `FAILED` |

## 5. Validations and messages

| Situation | Rule | Exact message (Spanish, as sent) | Meaning | HTTP |
|---|---|---|---|---|
| Propose without a body | — | `El cuerpo de la solicitud es obligatorio.` | The request body is required. | 400 (`body`) |
| Invalid role | `CLIENT` or `PROVIDER` | `Indique el papel de su compañía: CLIENT (somos el cliente de la otra) o PROVIDER (la otra es nuestro cliente).` | Give your company's role: CLIENT (we are the other's client) or PROVIDER (the other is our client). | 400 (`role`) |
| No company | Required | `Indique la compañía con la que se quiere enlazar.` | Give the company you want to link with. | 400 (`companyPublicId`) |
| Company does not exist, lacks the module or is not yours | It does not reveal which | `No se encontró esa compañía, no tiene encendido el módulo de compañías enlazadas o usted no pertenece a ella.` | That company was not found, it does not have the linked companies module on, or you do not belong to it. | 400 (`companyPublicId`) |
| With itself | — | `Una compañía no se puede enlazar consigo misma.` | A company cannot be linked with itself. | 400 (`companyPublicId`) |
| The provider without Transport | `LTL_GROUND` | `La compañía que da el transporte necesita el módulo de Transporte encendido.` | The company that provides the transport needs the Transport module on. | 422 |
| Missing client | Required for the provider | `Indique el cliente de su compañía a cuyo nombre nacerán las órdenes de la compañía enlazada.` | Give the client of your company in whose name the linked company's orders will be created. | 400 (`clientPublicId`) |
| Invalid client | Existing, active, Active status | `El cliente indicado no existe, está dado de baja o no está activo.` | The given client does not exist, was deactivated or is not active. | 400 (`clientPublicId`) |
| Client already used | One company per client | `Ese cliente ya representa a otra compañía enlazada.` | That client already represents another linked company. | 409 |
| There is already an open link | One proposed or active per client company | `Ya hay un enlace propuesto o activo entre estas compañías (o la compañía cliente ya despacha por otra). Termínelo antes de proponer otro.` | There is already a proposed or active link between these companies (or the client company already dispatches through another). End it before proposing another. | 409 |
| Long note | Up to 250 | `La nota no puede pasar de 250 caracteres.` | The note cannot exceed 250 characters. | 400 (`note` or `reason`) |
| No reason | Required when rejecting and ending | `Escriba el motivo.` | Enter the reason. | 400 (`reason`) |
| Accept or reject one that is no longer proposed | Proposed status | `El enlace ya no está propuesto: no se puede aceptar ni rechazar.` | The link is no longer proposed: it cannot be accepted or rejected. | 422 (409 if someone else changed it at the same time) |
| Accept your own | The other accepts | `Su compañía propuso este enlace: lo acepta o lo rechaza la otra.` | Your company proposed this link: the other one accepts or rejects it. | 422 |
| End one that is not active | Active status | `Solo se termina un enlace activo.` | Only an active link is ended. | 422 (409 if someone else changed it at the same time) |
| Link of another company or nonexistent | The link only exists for its two companies | `Enlace no encontrado.` | Link not found. | 404 |
| Propose or accept without re-authentication | Recent AAL2 | `Esta acción requiere reautenticación reciente (AAL2).` | This action requires a recent re-authentication (AAL2). | 403 |
| Without the permission | `intercompany.view` / `.manage` | `Falta el permiso 'intercompany.manage'.` (or the one that applies) | The permission is missing. | 403 |
| Module off | `INTERCOMPANY` | `El módulo 'INTERCOMPANY' no está habilitado para esta compañía.` | The module 'INTERCOMPANY' is not enabled for this company. | 403 |
| Delivery without a consignee | The other company requires it | `La entrega por la compañía enlazada necesita el consignatario (de la orden de venta o uno nuevo).` | The delivery by the linked company needs the consignee (from the sales order or a new one). | 400 (`order.consigneeLocationPublicId`) |
| Delivery without packages | At least one line | `Indique al menos una línea de paquete.` (and the usual package messages) | Give at least one package line. | 400 |
| The other company does not receive | Active, with `INTERCOMPANY` and `LTL_GROUND` | `La compañía enlazada «X» no está recibiendo órdenes ahora (está desactivada o apagó el módulo de compañías enlazadas o el de Transporte). Revise el enlace en Integraciones.` | The linked company «X» is not receiving orders now (it is deactivated or turned off the linked companies module or the Transport one). Check the link in Integrations. | 422 when creating the delivery; on an already saved delivery it is a transient error and retries on its own |
| Pending with the link ended | — | `El enlace con «X» ya no está activo: la orden no se mandó.` | The link with «X» is no longer active: the order was not sent. | — (left «Could not be created») |
| The client over there can no longer receive | Deactivated or inactive | `El cliente que representa a esta compañía en «X» ya no puede recibir órdenes (dado de baja o inactivo).` | The client that represents this company in «X» can no longer receive orders (deactivated or inactive). | — (left «Could not be created») |
| The other company rejected the order | Its rules | The message from over there, as is; for example `El cliente está suspendido; no se pueden crear ni confirmar envíos.` or `No hay tarifa vigente para STANDARD/BOX; configure la tarifa en el contrato del cliente antes de confirmar.` | The client is suspended; shipments cannot be created or confirmed. / There is no current rate for STANDARD/BOX; set the rate in the client's contract before confirming. | — (left «Could not be created») |
| Retry one that is not «Could not be created» | `FAILED` state | `Solo se reintenta una entrega que no se pudo crear en la compañía enlazada.` | Only a delivery that could not be created in the linked company is retried. | 422 |
| Check or view proof without an order over there | `CREATED` state | `La orden todavía no existe en la compañía enlazada.` | The order does not exist in the linked company yet. | 422 |
| Check or view proof with the link ended | Active link | `El enlace con «X» ya no está activo: no se puede consultar la orden de allá.` | The link with «X» is no longer active: the order over there cannot be consulted. | 422 |
| Deliveries filter | `PENDING`, `CREATED` or `FAILED` | `El estado debe ser PENDING, CREATED o FAILED.` | The state must be PENDING, CREATED or FAILED. | 400 (`state`) |
| Delivery of another company | — | `Entrega por compañía enlazada no encontrada.` | Delivery by linked company not found. | 404 |
| Proof file that is not from that delivery | — | `Archivo no encontrado.` | File not found. | 404 |
| Undo a dispatch delivered by the other company | It can no longer be done from here | `El envío de la recolección EMP-… ya avanzó a '—'; la recolección ya no se puede eliminar.` (from reading the code; not tested) | The shipment of pick batch EMP-… already advanced to '—'; the pick batch can no longer be deleted. | 422 |

## 6. Statuses and transitions

### 6.1 The link

It does not use the status engine (it belongs to two companies): it is a text with four values. Who and when is kept on the link and in the audit log (`TENANT_LINK`).

| From → to | Who | What it validates | Effects | What it blocks |
|---|---|---|---|---|
| (new) → **Proposed** | `intercompany.manage` + AAL2 | The rules in section 5 | Notice to the other company | Nothing crosses |
| Proposed → **Active** (accept) | The **other** company, `intercompany.manage` + AAL2 | Still proposed; it is not the one that proposed; if it provides the transport, it has Transport and chooses its client | The client company's deliveries cross; notice to the other | It cannot be accepted or rejected again |
| Proposed → **Rejected** (reject or withdraw) | The other (rejects) or whoever proposed (withdraws), `intercompany.manage` | Still proposed; reason required, up to 250 | Notice to the other | It is not reopened: another is proposed |
| Active → **Ended** | Either of the two, `intercompany.manage` | It is active; reason required | Nothing is sent or fetched; pending → «Could not be created»; notice to the other | It is not reopened: another is proposed; the order over there can no longer be checked nor its proofs viewed |

### 6.2 The delivery

| From → to | Who / what does it | What it validates | Effects | What it blocks |
|---|---|---|---|---|
| (new) → **To send** (`PENDING`) | Creating the dispatch's delivery (`warehouse.pick`) with an active link | Consignee and packages; the other company receives | The pick batch is left packed **without an order here**; the record is left (one per dispatch) | Undoing the dispatch from here |
| To send → **Sent** (`CREATED`) | The immediate send, the job or «Retry» | That the link is still active and the other company receives | The order is created over there (only once; if it already exists by its mark, it is not duplicated); number and status are fetched | — |
| To send → **Could not be created** (`FAILED`) | The other company rejected, the link ended or the 10 attempts ran out | — | Stores the message; `INTEGRATION_LINK_DELIVERY_FAILED` notice | Nothing is sent on its own until retried |
| Could not be created → **To send** | «Retry» (`intercompany.manage` or `warehouse.pick`) | That it is in «Could not be created» | Attempts back to zero; sent at once | — |
| Sent → (stays **Sent**, with another status over there) | The job every 2 minutes or «Check now» | Active link | Fetches number, status, who received and when; a status change notifies (`INTEGRATION_LINK_DELIVERY_STATUS`); on reaching a final status it stops being checked | With the link ended, fetching stops |

## 7. Before using it (start-up)

1. Turn on **Integrations** and **Linked companies** in both companies (Logistics also with **Transport**).
2. In the company that provides the transport, create the **client** that represents the other (for example «Advance Solutions») with a **current contract and rate**. Without a rate the order is rejected.
3. Propose the link from the client company and accept it in the other, **choosing that client**.
4. Build the **notification rules**: deliveries in the client company, urgent ones in the one that provides the transport.
5. Try it with a small sales order before opening it to the salespeople.

## 8. Frequently asked questions of the module

- **I do not see the «Linked companies» tab.** `intercompany.view` and the Linked companies module on in your company are needed.
- **The company does not appear when proposing.** Only the other companies you belong to appear. If it shows as «(without the linked companies module)», it must be turned on there first.
- **I proposed and nothing happens.** The other company still has to accept; until then the link is «Proposed» and deliveries are created in your company as usual.
- **The delivery was left «Could not be created».** Read the message on the delivery: it is the other company's (rate, suspended client, service…). Fix it **over there** and press **Retry**.
- **It stays «To send» and does not move.** The job retries it every 2 minutes up to 10 times. If the other company is off or without the module, see the «is not receiving orders now» message.
- **Why is there no transport order in my company?** By design: the delivery belongs to the other company. It is cancelled over there.
- **Can an ended link be reopened?** No. Another one is proposed.
- **Why is the dispatch not invoiced?** With billing «on delivery», it waits for the delivery over there to be Delivered or to have a proof. Use **Check now**.
- **The proof says there is none.** It answers 204: the other company has not registered the delivery yet.

The messages are in the [FAQ](faq.md#batch-227--linked-companies).

## 9. For support

- Tables `TenantLink` (no `TenantId`: it belongs to both companies) and `TenantLinkOrder` (of the client company), module `INTERCOMPANY`, permissions and EntityType `TENANT_LINK` / `TENANT_LINK_ORDER`: section «2026-11-07 — Lote 227» of `Diseño/logistica-db-update.sql` (mark `L227_INTERCOMPANY` for the cloned Company admin roles).
- Unique indexes: one proposed or active link per client company (`UX_TenantLink_ClientOpen`), one client per active linked company (`UX_TenantLink_ProviderClient`) and one delivery per dispatch (`UX_TenantLinkOrder_PickBatch`, the one that prevents duplicating the order).
- Code: `TenantLinkService` (links), `TenantLinkBridge` (deliveries), `LinkedTenantScope` (the only place where anything is done in the other company: as that company, without a user and limited to the link's client), `TenantLinksController`, job `INTERCOMPANY_SYNC`.
- The order over there carries the origin mark `TENANT_LINK_ORDER` + the delivery id. Only the order that was born from that delivery is read.
- «Who created the order in Logistics?»: it shows as «Sistema» (no user); there is no named service account.
- Depot (Solutions as a warehouse client of Depot) is **not** included: the bridge only does transport deliveries.
