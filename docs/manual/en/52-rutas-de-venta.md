# 52 — Sales routes (route people, preferred seller, order seller, quota by route and analysis by route)

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter [`../52-rutas-de-venta.md`](../52-rutas-de-venta.md) (Batch 244, 2026-11-25). The Spanish manual is the reference. **The API returns its error messages in Spanish only**, so the messages below are quoted in Spanish (exactly as the software shows them) with their meaning in English. Interface labels use the English text of the screens.

Batch 244 (2026-11-25). A **sales route** groups the clients and the people who serve them: a **primary** and, if needed, **supports**. With this, the sales analysis says how much **each route** sold (and each person inside it) against its quota, and every sales order keeps **who sold it**.

> **It is not the delivery route or the trip.** The sales route does not move goods: it only decides whose a sale is. Delivery routes and trips are in the Transport chapters.

> **«Cadenas» (Chains) is just another route.** It has no special rules: it is created, given people and measured like any other.

This chapter continues [51 — Sales analysis](51-analisis-de-ventas.md) (which is still valid: «By sales rep» is kept). Screens: [L244](../frontend/l244-rutas-de-venta.md) (Spanish edition). Every error message is explained in the [FAQ](faq.md) (section «Batch 244»).

## 1. What it is, where it is and who can

- **Where:** menu **Analysis → Sales analysis** (`/analytics/sales`). The **«By route»** tab is the one that opens by default; next to it are **By sales rep**, **Product movement**, **Charts** and **Setup**. In Setup: **Routes** (with each route's **«People»** button and **«Pass routes from representatives»**), **Sales reps**, **Quotas**, **Clients without a rep** (on top, **«Clients without a sales route»**) and **Settings** («Quota is measured by»). The client record (Lists → Clients and contracts) has the **Sales** panel; the sales order has the **Seller** field.
- **Module:** **Sales orders (`SALES_ORDERS`)**. With the module off, everything answers 403 with `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` (the module is not enabled for this company).
- **Permissions.** There are no new permissions:

| What | Permission | Module |
|---|---|---|
| See routes, their people, the analysis by route, a client's sales and a route's quotas | `sales.analysis.view` or `sales.analysis.manage` | `SALES_ORDERS` |
| Add, close and void the people of a route; choose a client's route and preferred seller (one by one or in bulk); «Pass routes from representatives»; quotas by route; «Quota is measured by»; create and change sales reps; search for people to put on a route | `sales.analysis.manage` | `SALES_ORDERS` |
| See a draft's «Seller» field and the options it offers | `sales.create` | `SALES_ORDERS` |
| Choose the seller on the draft | The same permissions used to create or edit the draft (`sales.create`) | `SALES_ORDERS` |
| The «Sales route» and «Seller» filters of the order list | Only shown to whoever has `sales.analysis.view` or `manage` (the lists belong to the analysis) | `SALES_ORDERS` |
| See the «Sales route without a primary» row of «Needs your attention» | `sales.analysis.manage` | `SALES_ORDERS` |

- Without permission, the API answers 403 and a `PERMISSION_DENIED` event is left in the security events.
- `TenantId` comes from the session: each company sees only its own data.
- Routes and route people are also **data sources** in Views, indicators and charts: **«Sales routes»** and **«Route people (primary and support)»** (seen with `sales.analysis.view`).

## 2. Routes

They already existed since Batch 241 ([51 §2.1](51-analisis-de-ventas.md#21-routes)): name unique among the active ones, order, deactivate and reactivate. What is new:

- The list (Setup → Routes) shows **Primary** (or the label «No primary»; «Inactive primary» if they are no longer an active person), **Supports**, **Clients** (active ones with that own route or, if they have none, their sales rep's route) and the **current quota** of the current month.
- **Deactivating** a route fails if it has active sales reps or active clients with that **own** route (409). If it goes through, it **closes today** its people in effect. **Reactivating does not reopen them**: they have to be put back.
- API: `GET /api/v1/sales-analysis/routes`, `POST …/routes/{id}/deactivate`, `…/reactivate`, `GET …/routes/{id}/quotas` (the route's own quotas, month by month).

## 3. Route people (primary and support)

**What it does.** It says who serves the route and since when. A person is always an active **Teikem user** in the company.

- **Primary:** only one in effect per route. The route's sale that has no other seller is credited to them.
- **Support:** as many as needed. They can sell (and be the seller of an order) just like the primary.
- **Validity:** from the «From» date to the «To» date **not included** (the closing day no longer counts). No closing date = **in effect**. Future dates are not scheduled.

**How to use it (screen).** Setup → Routes → the route's **«People»** button → window **«People on {route}»**:

1. **Current** lists each person with their role, «From», «To» and state (Current, Closed, Void; «Inactive person» if the user is no longer active).
2. **«Add person»**: choose the **Person** (search by name or e-mail, up to 50 results), the **Role** (Primary or Support) and **From** (today by default). If you add a primary, **the previous one is closed** by itself with the start of the new one; if that person was a support, that row is also closed.
3. **«Close»** (the person leaves the route): asks for the **Closing date** (today by default). The history is kept.
4. **«Void»**: for a record made **by mistake**. It stops counting in the history and in the sale attribution. For someone to leave the route, you close, you do not void.
5. **«Show history»** brings the closed ones; **«Show voided»** (only with `manage`) also the voided ones.

**API.**

| What | Call | Body |
|---|---|---|
| See people | `GET /api/v1/sales-analysis/routes/{id}/people?history=false&includeVoided=false` | — |
| Add | `POST /api/v1/sales-analysis/routes/{id}/people` | `{ "userId": 12, "role": "PRIMARY", "validFrom": "2026-11-01" }` (`role` = `PRIMARY` or `SUPPORT`; `validFrom` optional) |
| Close | `POST …/routes/{id}/people/{assignmentId}/close` | `{ "validTo": "2026-11-24", "rowVersion": "…" }` (all optional) |
| Void | `POST …/routes/{id}/people/{assignmentId}/void` | — |
| Search people | `GET /api/v1/sales-analysis/sellers/candidates?q=ana&take=20` | — (`take` from 1 to 50) |

Every write answers with the route's people already updated (with history).

**Fields and rules.**

| Field | Rule |
|---|---|
| `role` | `PRIMARY` or `SUPPORT` (case-insensitive) |
| `userId` | Required; **active** member of this company (active user and active membership) |
| `validFrom` | Today by default; not in the future; for a new primary, not before the start of the current primary |
| `validTo` | Today by default; not before the start; not in the future |
| The route | Must exist and be active to add people |

## 4. Sales reps: the user is required and the automatic primary

A **sales rep** (representative) now always carries a **Teikem user**: the seller is always a user.

- **Create:** the user is required. A user can only be the user of **one** active sales rep.
- **Change:** the user cannot be removed. A legacy sales rep **without a user** can be renamed or moved to another route without touching the user; one is assigned when possible (the list has the **«Sales reps without a user»** filter and a **«Go to Users to create the user»** link with `users.view`).
- **Automatic primary.** If, when creating the sales rep, giving it a user, changing its route or reactivating it, its route is **active and without a primary**, its user becomes the **primary from today**. The response carries `becamePrimary = true` and the screen says «{name} became the primary of its route.». If the route already has a primary, nothing changes: the administrator decides.
- API: `GET /api/v1/sales-analysis/reps?includeInactive=false&withoutUser=false`, `POST …/reps` with `{ "name": "Ana", "routeId": 3, "userId": 12 }`, `PATCH …/reps/{id}`, `…/deactivate`, `…/reactivate`.

## 5. «Pass routes from representatives»

**What it does.** It is the same rule as the migration that ran only once with the batch, but on request and **with a simulation**:

1. Each **client without a route** receives the route of its sales rep (if the sales rep is active and its route is active).
2. In each **active route without a primary**: if it has **exactly one** active sales rep with a user (an active person), that user becomes the **primary**. If it has **several**, all become **support** and the route **stays without a primary**: the administrator chooses.
3. It lists the **active sales reps without a user** (they cannot be passed) and the routes with several sales reps.

The new assignments start on the **first day of the oldest month** with history, invoices or quotas in the company (without data, today), never in the future and never before the closing of a previous assignment of the same route or person. Running it again changes nothing more.

**Who can.** `sales.analysis.manage`, module `SALES_ORDERS`.

**How to use it.** Setup → Routes → **«Pass routes from representatives»** → **«Simulate»** (shows «Clients with own route: N», «New primaries: N», «New supports: N», «Representatives without a user (cannot be moved)» and «Routes with several representatives»; it says «Simulation: nothing was saved.») → **«Apply»** (asks for confirmation: «Apply the changes?»). API: `POST /api/v1/sales-analysis/routes/from-reps?dryRun=true` (simulates by default; with `dryRun=false` it saves). It answers `{ "dryRun", "clientsRouted", "primariesSet", "supportsSet", "validFrom", "repsWithoutUser": [...], "severalReps": [...] }`.

## 6. Client record: «Sales» panel (route and preferred seller)

**What it does.** On the record (Lists → Clients and contracts → the client → **Sales**) you see and change:

- **Sales route:** the client's **own** (label «Own route») or, if it has none, the one **inherited from the sales rep** («Inherited from representative {rep}»). With neither: «No sales route». «Remove own route» goes back to inheriting the sales rep's.
- **Route team:** the **Primary** and the **Supports** in effect.
- **Preferred seller:** the one proposed first on the client's orders.
- **Proposed seller:** the one that would come out today on a new order and where it comes from («client's preferred», «route primary» or «route support»).
- **Representative:** the inherited one, read-only (changed in Setup).

**Who can.** See: `sales.analysis.view` or `manage`. Change: `sales.analysis.manage`. Module `SALES_ORDERS`.

**Preferred seller rules.**

- It must be an **active person of the client's route**: primary or support **in effect**, with an active user in the company.
- A client **without a route** (own or inherited) **does not accept** a preferred seller.
- It is validated **when assigned**. If that person later leaves the route (is closed or voided) or is deactivated, **nothing is blocked**: the record shows the label **«The preferred seller no longer belongs to the client's route.»** with the help «Nothing is blocked: pick another seller or leave it as it is.».
- Sending the same preferred seller it already has is not validated again. **«Remove the preferred seller»** can always be done.
- If the route and the preferred seller are changed in the same operation, the preferred seller is validated against the **new** route.

**API.**

| What | Call | Body |
|---|---|---|
| See | `GET /api/v1/sales-analysis/clients/{clientId}/sales` | — |
| Change | `PUT /api/v1/sales-analysis/clients/{clientId}/sales` | `{ "routeId": 3, "clearRoute": false, "preferredUserId": 12, "clearPreferred": false, "rowVersion": "…" }` (all optional; `null` = no change) |
| Route of several clients | `POST /api/v1/sales-analysis/clients/assign-route` | `{ "clientIds": [12, 13], "routeId": 3 }` (`routeId: null` removes the own route; up to 2000 clients) |
| Client list | `GET /api/v1/sales-analysis/clients?routeId=3&noRoute=false&q=&skip=0&take=100` | — |

The response of `GET`/`PUT …/sales` carries `routeId`, `routeFromRep`, `directRouteId`, `primary`, `supports`, `preferred`, `preferredOutOfRoute`, `proposedUserId`, `proposedKind` and the inherited sales rep.

**«Clients without a sales route» (in bulk).** Setup → Clients without a rep → **«Clients without a sales route»** panel (clients with neither an own route nor their sales rep's): tick the boxes, choose «Assign to route…» (or «Remove own route») and press **«Assign route»** → «The route was assigned to N clients.». Assigning the route in bulk **does not touch** those clients' preferred seller: if it no longer belongs to the new route, the record points it out.

## 7. Seller of the sales order

**What it does.** Each order keeps **the sales route** and **the seller**. How it is decided:

1. **On a draft** the **Seller** field offers the active people who can be chosen (the primary, the supports of the client's route and, at the end, the preferred seller if not from the route) and **proposes** one: the **active preferred** → the **active primary** → **whoever creates the order**, only if they are a person in effect on the route → none («No seller»). It is labelled «Proposed». Whoever has `sales.create` can choose another from the list.
2. **When requested** (Draft → Requested) a **copy is frozen**: the client's effective route at that moment, the seller (the choice if still valid and an active person; if not, the proposal) and the moment. **That copy does not change afterwards:** neither by changing the client's route or preferred seller, nor by cancelling, nor by returning a dispatch to «Requested».
3. Orders requested **before the batch** have no route or seller (shown «—»); they were not filled in.

**Where it shows.** On the order (draft: «Seller» field and the «Sales route» line; requested: «Sales route · Seller»), in the **Sales orders list** (columns **Sales route** and **Seller**; on a draft they show «—» because the copy is taken when requested) and in the **e-mail** «Sales order requested» (Batch 243): the seller is the copied one and the e-mail reaches them only if they are still an active person; if not, the send stays `SKIPPED`.

**List filters.** **Sales route** (includes «No sales route») and **Seller** (if you have `sales.analysis.view|manage`). API: `GET /api/v1/sales-orders?routeId=3&sellerUserId=12`.

**API.**

| What | Call | Body |
|---|---|---|
| Field options | `GET /api/v1/sales-orders/seller-options?client={publicId}` (permission `sales.create`) | — |
| Choose when creating or editing the draft | `POST`/`PATCH /api/v1/sales-orders` | `{ …, "sellerUserId": 12 }`; `"clearSeller": true` goes back to the proposal |

`seller-options` answers `{ "routeId", "routeName", "routeFromRep", "proposedUserId", "proposedName", "proposedKind", "options": [{ "userId", "name", "kind", "isPreferred", "isProposed" }] }` (`kind`: `PRIMARY`, `SUPPORT` or `PREFERRED`). **It carries no e-mails.**

**Rules.** The choice must be the client's active preferred seller or an active person in effect on its route. If the draft's client changes and the choice is no longer valid, it goes back to the proposal **without an error**. The seller is only changed on a draft.

## 8. Quota by route or by person

**What it does.** The monthly quota can be kept **by route** (one quota for each route, whoever serves it) or **by person (sales rep)**, as before. The two kinds **coexist**: both can be loaded; the setting decides which one **counts** for the analysis. By default the **person** one counts (nothing changes until the setting is changed). Monthly inheritance is the usual one: a month without its own quota is worth the most recent previous month's; removing a month's (empty amount) makes the previous one valid again.

**Who can.** See: `sales.analysis.view|manage`. Change the setting and the quotas: `sales.analysis.manage`.

**How to use it.**
- Setup → **Settings** → **«Quota is measured by»** → «Sales route» or «Person (representative)» → Save («How the quota is measured was saved.»).
- Setup → **Quotas**: choose «Quotas by sales route» or «Quotas by person»; the one that does not count is marked «is not the one counted in the analysis». It shows «Total effective quota», «Total by routes» and «Total by people».
- When a route is opened its own quotas are seen («Route quotas»).

**API.**

| What | Call | Body |
|---|---|---|
| Setting | `PUT /api/v1/sales-analysis/settings` | `{ "quotaBy": "ROUTE" }` (`ROUTE` or `REP`) |
| A month's quotas | `GET /api/v1/sales-analysis/quotas?month=2026-11&by=ROUTE` | — (`by` optional; by default, the setting) |
| Load in bulk | `PUT /api/v1/sales-analysis/quotas/2026-11` | `{ "items": [{ "routeId": 3, "amount": 1500.00 }, { "repId": 5, "amount": 800 }, { "routeId": 4, "amount": null }] }` |
| A route's | `GET /api/v1/sales-analysis/routes/{id}/quotas` | — |

Each line carries `repId` **or** `routeId` (one of the two). `amount: null` removes that month's own quota. Amounts are zero or more, with up to 2 decimals.

## 9. Analysis «By route» and «By sales rep»

**What it does.** The **«By route»** tab (the default one): net sale (product plus discounts; shipping and other charges do not count) of each route in the month against the comparison month. **«By sales rep»** stays as it was (chapter 51).

**Who can.** `sales.analysis.view` or `manage`; module `SALES_ORDERS`. API: `GET /api/v1/sales-analysis/by-route?month=2026-10&compareMonth=2026-09` (by default, the last full month against the previous one).

**What it brings.**
- **Month figures:** month sales, change against the comparison month and **quota fulfilment** (of a quota of …). Below it says whether «Quota is measured per sales route» or per person.
- **Comparison by sales route:** one row per route with **Primary** (or «No primary»), comparison month sales, month sales, Change $, Change %, Quota $ and Fulfilment. Each route **unfolds** with its people (primary or support, and «No seller» when the sale has no person); there are **«Unfold all»** and **«Fold all»**. With quota by **route**, the quota is on the route's row; with quota by **person**, on each person's row (their fulfilment is their **total** sales, from all their routes, over their quota).
- **By person:** each person's total sales across all the routes where they sold.
- **Totals and reconciliation:** total of the routes + «No route or seller» + «Shipping and other charges» = **Month total**; «Difference to review» must be 0.
- **Lines**, **Product by route**, **Route transactions** (`GET …/transactions?month=&routeId=` with `routeId=0` = no route; `repId=0` = the person is not a sales rep) and **«Export to Excel»**.

**Whose sale each one is (attribution).**
- **Route:** the copy stored on the order → the client's own route → its sales rep's route.
- **Person:** the seller copied on the order → the client's sales rep (if active and from **that** route) → the **route's primary on the day of the sale** → «No seller». A user who is also a sales rep counts as that sales rep.
- Lines that carry no copy from the order (for example, the imported history) follow the same rule with today's data: the client's route and its sales rep, or the route's primary on that day.

**Analysis notices** (they are not errors; they show on top in «For the analysis to add up:» and in `warnings`):

| Notice (Spanish, as sent) | What to do |
|---|---|
| `N clientes activos sin ruta: asígneles una ruta.` / `1 cliente activo sin ruta: asígnele una ruta.` | Clients without a sales route: their sale shows under «No route». Assign a route (section 6) |
| `N rutas activas sin titular: A, B y C.` | Put a primary (section 3) |
| `N rutas con el titular inactivo: …` | Their primary is no longer an active person: change it |
| `N representantes sin usuario: …. El vendedor siempre es un usuario de Teikem: el administrador crea el usuario y se le pone al representante.` | Create the user in Users and give it to the sales rep |
| `El usuario «X» está en varios representantes activos (A y B): su venta se cuenta en el primero que se creó.` | Leave the user in only one active sales rep |

(Meanings: N active clients without a route; N active routes without a primary; N routes with an inactive primary; N sales reps without a user — the seller is always a Teikem user; user «X» is in several active sales reps, their sale counts in the first one created.)

**Charts.** «Sales versus quota by sales route» (with quota by route) or by person, according to the setting. Pulse: the «Month sales vs quota» figure adds the quotas of the active routes or of the active sales reps, according to the same setting.

## 10. «Needs your attention»: sales route without a primary

A row **«Sales route without a primary: {route}»** shows in **«Needs your attention»** (Day pulse) for each **active** route without a primary in effect (reason **«No primary in effect»**) or whose primary **is no longer an active person** (reason **«Its primary is no longer active»**).

- Whoever has `sales.analysis.manage` with the module `SALES_ORDERS` sees it.
- Warning tone; **danger** if the route has active clients (the «Clients» figure is shown). Those with clients first. The «since» date is the closing of the last primary, if there was one.
- **«Review the sales route {route}»** opens Setup → Routes (`/analytics/sales?tab=setup&sec=routes`). It is only a notice: it does not block requesting orders (the proposed seller moves to the next person in the chain).
- It is calculated when read; there is nothing to «close»: it disappears when the route has an active primary.

## 11. Statuses and transitions

**A person on a route (assignment).**

| From | To | Who | What it validates | Effects | What stays blocked |
|---|---|---|---|---|---|
| (nothing) | **Current** (primary) | `sales.analysis.manage` | Active route; active person; `validFrom` neither in the future nor before the current primary; not already primary | **Closes the previous primary** with `validTo` = the start of the new one; if they were support, closes that row | — |
| (nothing) | **Current** (support) | `manage` | Active route; active person; not the primary and not already support | Appears as support | — |
| Current | **Closed** | `manage` | `validTo` neither before the start nor in the future | Sets `validTo`, `closedAtUtc` and `closedBy`; from that date it no longer counts as in effect | It cannot be closed again or reopened |
| Current or Closed | **Void** | `manage` | Not already void | `IsActive = 0`; if it had no closing, `validTo` = `validFrom`; it stops counting in history and attribution | It cannot be closed, voided again or «un-voided» |
| Void | (none) | — | — | — | Everything |

**A sales route.**

| From | To | Who | What it validates | Effects | What stays blocked |
|---|---|---|---|---|---|
| Active | **Deactivated** | `manage` | No active sales reps or active clients with that own route | Closes today the people in effect | People cannot be added and it cannot be chosen as a client's route (400 «La ruta está desactivada: actívela o escoja otra.») |
| Deactivated | **Active** | `manage` | The name is not used by another active route | It does not reopen the people | — |

**A sales order, regarding the seller.**

| Status | Seller and route | What can be done |
|---|---|---|
| **Draft** | Choice (or today's proposal); the route is today's effective one and is not stored | Choose or remove the choice (`sellerUserId`, `clearSeller`) |
| **Requested** (Draft → Requested) | The **copy is frozen**: route, seller and moment | None of this changes any more |
| Dispatched, Partly dispatched, Cancelled, or returned to Requested | The copy stays the same | Changing the seller gives 422 «Solo se puede editar una orden en borrador.» |

## 12. Validations and messages

The API answers in Spanish; the message goes in the error's `title`. Validation ones also carry `errors` with the field.

| Field or situation | Rule | Exact message (Spanish, as sent) | HTTP | Meaning |
|---|---|---|---|---|
| Body | Required | `El cuerpo de la solicitud es obligatorio.` | 400 (`body`) | The request body is required |
| Role (`role`) | `PRIMARY` or `SUPPORT` | `El papel debe ser PRIMARY (titular) o SUPPORT (apoyo).` | 400 (`role`) | The role must be PRIMARY or SUPPORT |
| Person (`userId`) | Required | `Escoja el usuario.` | 400 (`userId`) | Choose the user |
| Person | From this company | `El usuario no pertenece a esta compañía.` | 400 (`userId`) | The user does not belong to this company |
| Person | Active (not suspended, invited or inactive) | `El usuario no está activo en esta compañía: actívelo o escoja otro.` | 400 (`userId`) | The user is not active in this company: activate them or choose another |
| Route when adding people, or when chosen for a client | Active | `La ruta está desactivada: actívela o escoja otra.` | 400 (`routeId`) | The route is deactivated: activate it or choose another |
| Start date | Not in the future | `La fecha de inicio no puede ser futura.` | 400 (`validFrom`) | The start date cannot be in the future |
| Start date of a new primary | Not before the current primary's | `La fecha de inicio no puede ser anterior a la del titular actual (AAAA-MM-DD).` | 400 (`validFrom`) | The start date cannot be before the current primary's (YYYY-MM-DD) |
| Closing date | Not before the start | `La fecha de cierre no puede ser anterior a la de inicio.` | 400 (`validTo`) | The closing date cannot be before the start |
| Closing date | Not in the future | `La fecha de cierre no puede ser futura.` | 400 (`validTo`) | The closing date cannot be in the future |
| `rowVersion` | The base64 value from the record | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 (`rowVersion`) | Invalid rowVersion: the base64 value returned by the record is expected |
| Route does not exist | — | `Ruta no encontrada.` | 404 | Route not found |
| Assignment is not from that route | — | `Asignación no encontrada.` | 404 | Assignment not found |
| Client does not exist (client's sales, assign route, seller options) | — | `Cliente no encontrado.` | 404 | Client not found |
| Sales rep does not exist (quotas) | — | `Representante no encontrado.` | 404 | Sales rep not found |
| Person is already primary | — | `La persona ya es titular de esta ruta.` | 409 | The person is already the primary of this route |
| Person is already support | — | `La persona ya es apoyo de esta ruta.` | 409 | The person is already a support of this route |
| Primary who is to be made support | Close first | `La persona es la titular de esta ruta: cierre su asignación antes de ponerla de apoyo.` | 409 | The person is the primary of this route: close their assignment before making them support |
| Closing a closed one | — | `La asignación ya está cerrada.` | 409 | The assignment is already closed |
| Closing or voiding a void one | — | `La asignación ya está anulada.` | 409 | The assignment is already void |
| Two people change the route at once (or an old `rowVersion` on an assignment) | Reload | `Otra persona cambió las personas de la ruta al mismo tiempo: recargue e intente de nuevo.` | 409 | Another person changed the route's people at the same time: reload and try again |
| Client changed by another (old `rowVersion` on the record) | Reload | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 | The record was modified by another user |
| Deactivating a route with clients | Move them first | `La ruta tiene clientes asignados: páselos a otra ruta antes de desactivarla.` | 409 | The route has assigned clients: move them to another route before deactivating it |
| Deactivating a route with sales reps | Move them first | `La ruta tiene representantes activos: páselos a otra ruta antes de desactivarla.` | 409 | The route has active sales reps: move them to another route before deactivating it |
| Duplicate route name (when reactivating) | — | `Ya hay una ruta activa con ese nombre.` | 409 | There is already an active route with that name |
| Sales rep without a user (create, or `clearUser`) | Required | `Escoja el usuario del representante: el vendedor siempre es un usuario de Teikem.` | 400 (`userId`) | Choose the sales rep's user: the seller is always a Teikem user |
| User already belongs to another active sales rep (create, change or reactivate) | One per user | `El usuario ya es el representante «Nombre».` | 409 | The user is already the sales rep «Name» |
| Duplicate sales rep name | — | `Ya hay un representante activo con ese nombre.` | 409 | There is already an active sales rep with that name |
| Deactivating a sales rep with clients | Move them first | `El representante tiene clientes asignados: páselos a otro representante antes de desactivarlo.` | 409 | The sales rep has assigned clients: move them to another sales rep before deactivating |
| Preferred seller on a client without a route | Needs a route | `El cliente necesita una ruta antes de asignarle un vendedor preferido.` | 400 (`preferredUserId`) | The client needs a route before a preferred seller is assigned |
| Preferred seller outside the route | Active person of the route | `El vendedor preferido debe ser una persona activa de la ruta del cliente.` | 400 (`preferredUserId`) | The preferred seller must be an active person of the client's route |
| Assign route to several clients | At least one | `Indique los clientes (clientIds).` | 400 (`clientIds`) | Give the clients (clientIds) |
| Assign route to several clients | Up to 2000 | `No se pueden mandar más de 2000 renglones en una sola llamada.` | 400 (`clientIds`) | No more than 2000 lines in one call |
| Quota: `quotaBy` or `by` | `ROUTE` or `REP` | `La cuota se lleva por ruta (ROUTE) o por persona (REP).` | 400 (`quotaBy`, `by`) | The quota is kept by route (ROUTE) or by person (REP) |
| Quota line | `repId` or `routeId`, one of the two | `Cada renglón lleva el representante (repId) o la ruta (routeId), uno de los dos.` | 400 (`items`) | Each line carries the sales rep (repId) or the route (routeId), one of the two |
| Quota amount | Zero or more, up to 2 decimals | `La cuota debe ser cero o mayor, con hasta 2 decimales.` | 400 (`items`) | The quota must be zero or more, with up to 2 decimals |
| Quota lines | Up to 2000 | `No se pueden mandar más de 2000 renglones en una sola llamada.` | 400 (`items`) | No more than 2000 lines in one call |
| Month (`month`, `compareMonth`) | `YYYY-MM` | `El mes debe tener la forma AAAA-MM (por ejemplo 2026-10).` | 400 | The month must have the form YYYY-MM (for example 2026-10) |
| Seller options without a client | Required | `Escoja el cliente que compra.` | 400 (`client`) | Choose the buying client |
| Order seller | Active preferred of the client or active person in effect on its route | `El vendedor debe ser el vendedor preferido del cliente o una persona vigente de su ruta.` | 400 (`sellerUserId`) | The seller must be the client's preferred seller or a person in effect on its route |
| Editing the order with the seller, outside draft | Draft only | `Solo se puede editar una orden en borrador.` | 422 | Only a draft order can be edited |
| Module off | `SALES_ORDERS` on | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 | The module is not enabled for this company |
| No permission | — | (no message of its own; `PERMISSION_DENIED` is left) | 403 | — |

The screen's own messages for these cases (for example «The user is required.» when saving a sales rep without a user) are shown by the screen itself before calling the server; the server answers with those of the table.

## 13. Frequently asked questions

**How do I change a route's primary?** Setup → Routes → «People» → «Add person» with the role «Primary». The previous one is closed by itself. There is no need to close it first.

**How do I know who the primary was in October?** «People» → «Show history». Each row carries «From» and «To». A month's analysis uses the primary **of each** sale day.

**I put someone by mistake. Close or void?** **Void**: it stays as if it had not been done. Closing is for someone who really left the route.

**The route came out «No primary» after creating the sales rep.** The automatic primary only acts if the sales rep has a user, that user is an active person, the route is active and had no primary. If the route has several sales reps, it stays without a primary on purpose (the others are support): choose one.

**The client record says «The preferred seller no longer belongs to the client's route.»** The client's route changed or the person left the route. It blocks nothing. Choose another preferred seller (from the current route) or remove it. In the meantime, if that person is still active in the company, the new order keeps proposing them.

**It does not let me set a preferred seller.** The client needs a route (own or its sales rep's) and the person must be a primary or support in effect on that route. First put them on the route (section 3).

**Can I sell as someone who is not on the route?** On an order, only the client's preferred seller or a person in effect on its route. If you need another person, add them as a support of the route.

**I changed the client's route and the old order did not change.** It is on purpose: when requested, the order keeps its route and seller for good. Orders before Batch 244 have none.

**A sale shows under «No route or seller».** The client has no route (neither own nor through a sales rep) and there is no seller, or it is imported history that was not matched to a client. Assign a route to the client or match the name ([51 §2.8](51-analisis-de-ventas.md#28-matching-by-hand)).

**A sale shows with the route but «No seller».** The route had no primary that day and the client has no active sales rep from that route. Put a primary; the history uses the primary the route had that day, so it is only corrected backwards with a primary that starts earlier.

**What changes if I move the quota to «Sales route»?** Fulfilment is measured against the route's quota, and the total of quotas is the sum of the routes. The quotas by person are not deleted: they stay stored and can be used again.

**Where are routes and their people seen in Views and indicators?** Sources «Sales routes» and «Route people (primary and support)».

**«Pass routes from representatives» says «0» for everything.** There are no clients without a route with an active sales rep, and all the active routes have a primary or no sales rep with a user. It is normal after the first time.

## 14. For support

- **Tables:** `SalesRouteAssignment` (route people: `IsPrimary`, `ValidFrom`, `ValidTo`, `IsActive`, `ClosedAtUtc/By`, `VoidedAtUtc/By`), `Client.SalesRouteId` and `Client.PreferredSalesUserId`, `SalesOrder.SalesRouteId`, `SellerUserId` and `SalesCopyAtUtc`, `SalesQuota.SalesRouteId`, `SalesAnalysisSetting.QuotaBy`. An assignment **in effect** = `IsActive = 1` and empty `ValidTo`.
- The database guarantees **one primary in effect per route** and **one row in effect per person and route**: a double click by two administrators gives the 409 «Otra persona cambió las personas de la ruta…», never two primaries.
- **Audit:** every add, close and void is logged with the type `SALES_ROUTE_ASSIGNMENT`.
- **Batch migration:** the «Lote 244» section of `Diseño/logistica-db-update.sql` runs once (mark `L244_SALES_ROUTES_FROM_REPS`) and prints how many clients, primaries and supports resulted and which active sales reps have no user. If there are repeated users among active sales reps, it warns and the index `UX_SalesRep_User` is created on the next run.
- **«Active person»** = active user **and** **ACTIVE** membership in the company. A portal user never is one.
- Decisions and what was left out: [lote244-decisiones.md](../../lote244-decisiones.md) (Spanish).
