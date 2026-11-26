# 53 — Client chains (grouping clients, bulk add by name, filters and analysis by chain)

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter [`../53-cadenas.md`](../53-cadenas.md) (Batch 245, 2026-11-25; updated by Batch 247). The Spanish manual is the reference. **The API returns its error messages in Spanish only**, so the messages below are quoted in Spanish (exactly as the software shows them) with their meaning in English. Interface labels use the English text of the screens.

Batch 245 (2026-11-25). A **chain** groups the clients that belong to the same commercial group (Walgreens, CVS…). It is used to filter clients and orders by chain, to measure each chain's sales and to give it prices and offers of its own (Batch 247: see [chapter 55](55-precios-especiales-y-ofertas.md); the lowest price always wins among the client's price, its chain's, the offer and the general list, and every price is before IVU).

> **A chain is not a sales route.** It does not change the client's route or whom its sale is credited to. Changing a client's chain does not touch its route, its sales rep or its seller.

> **A client belongs to one chain only, or to none.** Changing it is **moving** it.

Screens: [L245](../frontend/l245-cadenas.md) (Spanish edition). Every error message is explained in the [FAQ](faq.md) (section «Batch 245»). This chapter continues [51 — Sales analysis](51-analisis-de-ventas.md) and [52 — Sales routes](52-rutas-de-venta.md).

## 1. What it is, where it is and who can

- **Where:**
  - Menu **Lists → Chains** (`/catalog/chains`): the list and, when a chain is chosen, its record in a panel on the right (`?id=` in the address).
  - **Lists → Clients and contracts:** the list's **«Chain»** filter, the chain's tag on each row and the **«Chain»** panel of the client record.
  - **Accounting → Sales (Sales orders):** the **«Chain»** column and filter.
  - **Analysis → Sales analysis:** the **«By chain»** tab.
- **Module:** **Sales orders (`SALES_ORDERS`)**. With the module off, the Chains screen, its endpoints and the «By chain» tab answer 403 with `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` (the module is not enabled for this company). The chain filter and tag in Clients only show with the module on.
- **Permissions:**

| What | Permission | Module |
|---|---|---|
| See the chain list, its record and its clients (and have the filters of Clients and Orders work) | `sales.chain` **or** `clients.read` **or** `sales.view` | `SALES_ORDERS` |
| Create, edit, deactivate and reactivate chains; assign and remove clients; change a client's chain from its record; bulk add by name (simulate and apply) | `sales.chain` | `SALES_ORDERS` |
| See the «By chain» tab and the «Sales» section of the chain record | `sales.analysis.view` or `sales.analysis.manage` | `SALES_ORDERS` |
| See the «Client chains» data source (`CLIENT_CHAIN`) in Views, indicators and charts | `clients.read` | `SALES_ORDERS` |

- `sales.chain` is in the Billing permission category (`BILLING`) and brings `clients.read` with it (whoever administers chains sees the clients).
- The roles that receive it: **company administrator** and **sales supervisor**. The **Sales** and **sales office** roles do not receive it; the administrator can give it in Roles.
- Without permission, the API answers 403 and a `PERMISSION_DENIED` event is left in the security events.
- `TenantId` comes from the session: each company sees only its own data. A chain or client of another company answers 404, without saying whether it exists.
- **There is no status or transition flow:** the chain is **active** or **inactive** (section 8).
- **Neither the portal nor the client app show the chain.**

## 2. Chain list and record

**What it does.** The list shows the chains with **Name**, **Clients** (only the **active** ones), **Notes** and **Active**. It has a search box by name and the **«Show inactive»** switch. The record opens in the right-hand panel and carries, in independent panels: **Details**, **Chain clients**, **Add by name** and **Sales**.

**How to use it (screen).** Lists → Chains. Choose a chain from the list. With `sales.chain` the **«+ New chain»** button appears; without it the record is read-only.

**API.**

| What | Call | Permission |
|---|---|---|
| List | `GET /api/v1/client-chains?includeInactive=false&q=walg` | See |
| Record | `GET /api/v1/client-chains/{publicId}` | See |
| Chain clients | `GET /api/v1/client-chains/{publicId}/clients?q=&includeInactive=false&skip=0&take=50&all=false` | See |

- The list carries `publicId`, `name`, `notes`, `isActive` and `clientCount` (active clients).
- The record also carries `inactiveClientCount`, `createdAtUtc`, `updatedAtUtc` and `rowVersion`. The screen summarizes it: «N active clients · N inactive · N in total».
- Chain clients: by name; `take` from 1 to 500 (50 by default); `all=true` brings them all (it is what «Export» uses). Each row carries `code`, `name`, `status`, `statusLabel`, `isActive` and `salesRepName`. By default it does not include inactive clients.

## 3. Create and edit a chain

**What it does.** It creates a chain with a name and notes, or changes them. **«Save»** is off while there are no changes.

**Who can.** `sales.chain`, module `SALES_ORDERS`.

**How to use it.** «+ New chain» (window with Name and Notes) or, on the record, the **Details** panel.

**API.**

| What | Call | Body |
|---|---|---|
| Create | `POST /api/v1/client-chains` | `{ "name": "Walgreens", "notes": "Farmacias de la cadena" }` |
| Edit | `PATCH /api/v1/client-chains/{publicId}` | `{ "name": "Walgreens PR", "notes": "…", "clearNotes": false, "rowVersion": "…" }` (all optional) |

- In the `PATCH`, a field that is not sent **does not change**. `clearNotes: true` clears the notes. `rowVersion` (the record's) makes a change made by someone else give 409 instead of being overwritten.
- Every change of name, notes or active leaves a log entry with the type `CLIENT_CHAIN`.

**Fields and rules.**

| Field | Rule |
|---|---|
| `name` | Required when creating; trimmed; up to 100 characters. **Unique among the company's active chains**: it ignores case, accents, periods and commas («Walgreens.» and «WALGREENS» are the same name). An inactive chain can repeat the name of an active one |
| `notes` | Optional; trimmed; up to 500 characters; empty = no notes |
| `rowVersion` | Optional; if it does not match the current one, 409 |

## 4. Assign and remove clients

**What it does.** It adds clients to the chain or takes them out. A client that **is already in another chain is not moved by itself**: it is skipped and reported, unless you ask to **move** it.

**Who can.** `sales.chain`, module `SALES_ORDERS`.

**How to use it (screen).** Chain record → **Chain clients**:
1. **Add clients:** pick clients one by one in «Pick clients…» (they accumulate) and press **«Add N»**. If some are already in another chain, the screen asks **«N clients are already in another chain: move them to this one?»**; **«Move them»** repeats the call with `moveFromOtherChains`.
2. **Remove:** tick boxes in the table and press **«Remove N»** (asks for confirmation). The clients are left **without a chain**.
3. **«Show inactive»** also shows inactive clients; **«Export»** downloads the whole query.

**API.**

| What | Call | Body |
|---|---|---|
| Add | `POST /api/v1/client-chains/{publicId}/clients` | `{ "clientPublicIds": ["…"], "moveFromOtherChains": false }` |
| Remove | `POST /api/v1/client-chains/{publicId}/clients/remove` | `{ "clientPublicIds": ["…"] }` |

The response carries the counts `assigned`, `moved`, `alreadyInChain`, `removed`, `notInChain` and `skippedOtherChain` (a list with the client and the chain it is in).

- Repeating the same call is safe: those already there count in `alreadyInChain`; when removing, those that are not in that chain count in `notInChain`.
- Up to **2000** clients per call. If any does not exist (or belongs to another company), the whole call fails with 404 and nothing is changed.
- **Adding** requires the chain to be **active**; **removing** does not.
- A client's soft delete **keeps** its chain; client status changes (prospect, active, suspended) do not touch it either.
- Each changed client leaves its `CLIENT` log entry with `ChainId`.

## 5. The client's chain (client record)

**What it does.** On the client record (Lists → Clients and contracts → the client → **«Chain»** panel) the current chain is seen (or the tag **«No chain»**; «Inactive» if the chain was deactivated). With `sales.chain` it is **changed** (even from one chain to another, in a single step) or **removed**. The help text says: «A client belongs to one chain only. Changing it does not change its sales route.»

**Who can.** See: `sales.chain`, `clients.read` or `sales.view`. Change: `sales.chain`. Module `SALES_ORDERS`.

**API.** `PUT /api/v1/client-chains/by-client/{clientPublicId}` with `{ "chainPublicId": "…" }`; with `{ "chainPublicId": null }` it removes the chain. It answers `clientPublicId`, `chainPublicId`, `chainName` and `chainIsActive`.

- The chain is **not** changed in the client's «Edit profile» (`PATCH /clients/{id}/profile`): it is done here with `sales.chain`, because the chain is going to decide prices.
- If the client is already in that chain, or already has no chain, nothing happens.
- The target chain must be active.
- The **«+ New client»** window does not ask for a chain: it is assigned afterwards.
- `GET /clients/{id}` carries `chainPublicId`, `chainName` and `chainIsActive`.

## 6. Bulk add by name («Add by name»)

**What it does.** It adds **at once** all the clients whose name starts with a text (for example «Walgreens»: «Walgreens 1», «Walgreens #2», «Walgreens-3»). It is always **simulated first**: the simulation writes nothing and returns a **fingerprint**; **applying** requires that fingerprint.

**Who can.** `sales.chain`, module `SALES_ORDERS`.

**Rules.**
- The text is compared **ignoring case, accents, periods and commas** («Farmacia Caño» and «FARMACIA CANO» are the same; «Farmacia Caño» does not take «FARMACIA CAÑÓN» as a whole word).
- At least **3 letters or digits**; up to 100 characters.
- **«Whole word»** (on by default): what follows the text cannot be a letter or a digit. «Walgreens» takes «WALGREENS #1», «walgreens 2» and «Walgreens-3», but **not** «Walgreensboro» or «Walgreens1». Off, it also takes those two. If the text ends in a character that is neither a letter nor a digit (for example «Walg-»), it is respected as is. A space at the end of the text is ignored.
- Only **active** clients enter, in any status.
- Each client receives an action:

| Action | Means | What happens when applied |
|---|---|---|
| **Assign** (`ASSIGN`) | It has no chain | It ends up in this chain |
| **Move** (`MOVE`) | It is in another chain and «Also move those in another chain» was turned on | It moves to this chain |
| **Already in** (`ALREADY`) | It is already in this chain | Nothing |
| **Skipped (other chain)** (`SKIP_OTHER_CHAIN`) | It is in another chain and moving it was not requested | Nothing: it is reported |
| **Excluded** (`EXCLUDED`) | You unticked it | Nothing |

- **Up to 2000 clients** are applied. The simulation warns when there are more (`tooMany`) and shows the first 2000 rows.
- **Excluding does not force a new simulation:** untick the box and the row becomes «Excluded».
- **The fingerprint changes** if the text, «Whole word», «Move…», the target chain, the matching clients or the chain of any of them change. If it changes between the simulation and applying, **nothing is written** and you must simulate again.
- It is **idempotent**: simulating and applying again leaves 0 assigned and 0 moved.

**How to use it (screen).** Chain record → **Add by name**:
1. Type the text in **«The name starts with»**.
2. Set **«Whole word»** and **«Also move those in another chain»**.
3. Press **«Simulate»**. The summary appears («N matches · N to assign · N to move · N already in · N in another chain (skipped) · N excluded») and the table with **Include**, **Code**, **Client**, **Current chain** and **Action**.
4. Untick the clients you do not want.
5. Press **«Apply to N clients»** and confirm. If you change the text or a switch, the simulation stops being valid: **simulate again** (the «Apply» button turns off). If the server answers 409 because of the fingerprint, the screen simulates again keeping your exclusions and warns «It was simulated again with the current clients; review and apply again.».

**API.**

| What | Call | Body |
|---|---|---|
| Simulate | `POST /api/v1/client-chains/{publicId}/bulk-assign/preview` | `{ "prefix": "Walgreens", "wholeWord": true, "moveFromOtherChains": false, "excludeClientPublicIds": ["…"] }` |
| Apply | `POST /api/v1/client-chains/{publicId}/bulk-assign/apply` | the same body plus `"fingerprint": "…"` (the simulation's fingerprint) |

- The simulation answers `prefix` (the text already normalized), `matched`, `toAssign`, `toMove`, `alreadyInChain`, `skippedOtherChain`, `excluded`, `tooMany`, `maxBulk`, `fingerprint` and `rows` (with `action`, `currentChainName` per client).
- Apply answers `assigned`, `moved`, `alreadyInChain`, `skippedOtherChain` and `excluded`.
- The target chain must be **active** (in simulate and in apply).

## 7. «Chain» filter and column in Clients and in Sales orders

**Clients** (`GET /api/v1/clients`, module `CATALOG`; the screen shows it with `SALES_ORDERS` on):
- `chainPublicId=…`: only that chain's clients. If the chain does not exist or belongs to another company, the list comes out **empty** (no 404).
- `noChain=true`: only the clients **«No chain»**.
- Both at once: 400. On screen, the **«Chain»** select offers «All chains», «No chain» and each active chain.
- Each client in the list carries `chainPublicId` and `chainName` (the list shows the chain as a tag next to the status).

**Sales orders** (`GET /api/v1/sales-orders?chainPublicId=…&chainPublicId=…`, module `SALES_ORDERS`):
- The **«Chain»** filter accepts several; any of them matches. If none belongs to the company, the result is empty.
- The **«Chain»** column (`chainName`) shows «—» if the client has no chain.
- The chain shown is the **client's current one**, calculated at that moment: the order **keeps no copy**. If you move a client to another chain, **its old orders are shown with the new chain**.

## 8. Active and inactive (there is no status)

The chain has no status: it is only **active** or **inactive**. It is never deleted.

| From → to | Who can | What it validates | Effects | What stays blocked |
|---|---|---|---|---|
| Active → Inactive (**«Deactivate»**, `POST …/deactivate`) | `sales.chain` | **Zero active clients** in the chain (409 with the count, singular or plural). Inactive clients do not count and keep their chain. The count is done inside the same transaction: if someone assigned a client at the same time, one of the two gets 409 «El registro fue modificado por otro usuario; recargue e intente de nuevo.» | The chain leaves the pick lists in clients and «Add by name»; it stops showing in the list except with «Show inactive». It is logged | Adding clients (400), assigning from the client record (400), bulk add (400). Still allowed: seeing, editing name and notes, removing clients, reactivating |
| Inactive → Active (**«Reactivate»**, `POST …/reactivate`) | `sales.chain` | That **no other active chain** already has that name (409) | It can receive clients again | — |

- Asking for the state the chain already has changes nothing and gives no error.
- An **inactive chain with sales** in the month still shows in «By chain».
- An inactive chain counts as «no chain» for prices: its clients stop receiving the chain's prices and offers (chapter 55).
- Deactivating a chain also gives 409 if it has published, not-expired **price lists** (including its special prices) or published, not-expired **«Include» offers** with it: «La cadena tiene precios especiales u ofertas publicados y vigentes ({N} listas de precios y {M} ofertas): quítela de ellos antes de desactivarla.» (the chain has published, in-effect special prices or offers: remove it from them before deactivating). The clients are checked first. «Exclude» offers do not block. The chain record now has the «Special prices» and «Offers» panels: see [chapter 55](55-precios-especiales-y-ofertas.md).

## 9. Sales analysis → «By chain»

**What it does.** It says how much **each chain** sold in a month against another month, with the breakdown by client. It is the **«By chain»** tab of Analysis → Sales analysis (next to «By route» and «By sales rep»).

**Who can.** `sales.analysis.view` or `sales.analysis.manage`, module `SALES_ORDERS`.

**What is seen.**
- **Month** and **Compare with** (by default the last full month against the previous one).
- Three cards: month sales, change against the comparison month and how many chains there are (with what «No chain» sold).
- **Comparison by chain:** one row per chain with its sales in the two months, **Change $**, **Change %** and **Share**; each row's triangle unfolds its clients; **«Unfold all» / «Fold all»**. The **«No chain»** row groups the clients without a chain and the history sales that were not matched to any client (by name).
- **Reconciliation:** *Sum of the chains + «No chain» = Net sales of the month*, with **Difference** (must be $0.00).
- A **Sales by chain** chart.

**Rules.**
- The sale is credited to the client's **current chain**, also the history imported from QuickBooks: **when a client is moved to another chain, its history is regrouped** under the new chain.
- **Sale = product + discounts.** Shipping and other charges do not count. Credit memos subtract. The cutoff date of [chapter 51](51-analisis-de-ventas.md) is respected.
- It is summed without rounding and rounded to 2 decimals (away from zero) at the end.
- All active chains show (even if they did not sell) and the inactive ones that sold in either of the two months.

**API.**

| What | Call |
|---|---|
| View | `GET /api/v1/sales-analysis/by-chain?month=2026-10&compareMonth=2026-09&chainPublicId=…` (all optional; with `chainPublicId` it brings only that chain, without «No chain», and the reconciliation is still the whole month's) |
| A chain's series | `GET /api/v1/sales-analysis/by-chain/series?chainPublicId=…&fromMonth=2025-11&toMonth=2026-10` (`chainPublicId` required; by default, the 12 months ending at the last full month; up to 36) |

The series carries the net sales per month and the **5 main clients** with their share. The chain record uses it in its **Sales** section («Net sales by month» and «Chain clients in the month»).

**Views, indicators and charts.** They can be grouped and filtered by chain with the **«Chain»** field (`ChainName`) of the **Clients**, **Sales analysis lines** (with «No chain» when there is none) and **Sales orders** sources (the client's current chain, live). There is also the **«Client chains»** source (`CLIENT_CHAIN`) with name, notes, active, active and inactive clients and creation date; in Views, the other sources that carry the client can use the «Chain» relation.

## 10. Validations

Messages come from the server as they are. In 400 error responses the message travels in the title and in `errors.<field>`. **Messages are in Spanish; the English meaning is in the last column.**

| Where | Field | Rule | Exact error message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|---|
| Create / edit | `name` | Required when creating; only spaces counts as empty | `Escriba el nombre de la cadena.` | 400 | Type the chain's name |
| Create / edit | `name` | Up to 100 characters | `El nombre no puede pasar de 100 caracteres.` | 400 | The name cannot exceed 100 characters |
| Create / edit | `notes` | Up to 500 characters | `Las notas no pueden pasar de 500 caracteres.` | 400 | The notes cannot exceed 500 characters |
| Create / edit / reactivate | `name` | Unique among the active ones (ignoring case, accents, periods and commas) | `Ya hay una cadena activa con ese nombre.` | 409 | There is already an active chain with that name |
| All that receive a body | body | Cannot be missing | `El cuerpo de la solicitud es obligatorio.` | 400 | The request body is required |
| Record, edit, deactivate, reactivate, clients, bulk add, analysis | `publicId` / `chainPublicId` | Must be a chain of the company | `Cadena no encontrada.` | 404 | Chain not found |
| Add / remove / change a client's chain | `clientPublicIds` / client | All must exist in the company | `Cliente no encontrado.` | 404 | Client not found |
| Edit / deactivate / reactivate / assign / remove / bulk add | — | Another person changed the chain or its clients at the same time | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 | The record was modified by another user; reload and try again |
| Deactivate | — | With 1 active client | `La cadena tiene 1 cliente activo: quítelo de la cadena antes de desactivarla.` | 409 | The chain has 1 active client: remove it before deactivating |
| Deactivate | — | With N active clients | `La cadena tiene N clientes activos: quítelos de la cadena antes de desactivarla.` (N is the number) | 409 | The chain has N active clients: remove them before deactivating |
| Deactivate | — | No active clients, but published, in-effect price lists (including special prices) or published, in-effect «Include» offers (checked **after** the clients) | `La cadena tiene precios especiales u ofertas publicados y vigentes ({N} listas de precios y {M} ofertas): quítela de ellos antes de desactivarla.` (N and M are the numbers) | 409 | The chain has published, in-effect special prices or offers (N price lists and M offers): remove it from them before deactivating |
| Add clients / change a client's chain / bulk add | target chain | Must be active | `La cadena está desactivada: actívela o escoja otra.` | 400 | The chain is deactivated: activate it or choose another |
| Special prices, price lists and offers of the chain | chain | Must be active to write | `La cadena está desactivada: actívela o escoja otra.` | 422 (the messages of chapter 55) | Same meaning |
| Add / remove | `clientPublicIds` | At least one | `Indique los clientes (clientPublicIds).` | 400 | Give the clients (clientPublicIds) |
| Add / remove | `clientPublicIds` | Up to 2000 per call | `No se pueden mandar más de 2000 clientes en una sola llamada.` | 400 | No more than 2000 clients in one call |
| Bulk add | `prefix` | At least 3 letters or digits | `Escriba al menos 3 letras del inicio del nombre.` | 400 | Type at least 3 letters of the start of the name |
| Bulk add | `prefix` | Up to 100 characters | `El prefijo no puede pasar de 100 caracteres.` | 400 | The prefix cannot exceed 100 characters |
| Bulk add (simulate and apply) | `excludeClientPublicIds` | Each excluded client must be among those the simulation found | `Un cliente excluido no está entre los que encontró la simulación.` | 400 | An excluded client is not among those the simulation found |
| Bulk add (apply) | `fingerprint` | Required | `Falta la huella de la simulación (fingerprint): simule antes de aplicar.` | 400 | The simulation's fingerprint is missing: simulate before applying |
| Bulk add (apply) | `fingerprint` | Must be that of the current clients | `Los clientes que coinciden cambiaron desde la simulación: vuelva a simular antes de aplicar.` | 409 | The matching clients changed since the simulation: simulate again before applying |
| Bulk add (apply) | `prefix` | At most 2000 matches | `La simulación encontró N clientes y el máximo por aplicación es 2000: use un prefijo más largo.` (N is the number) | 400 | The simulation found N clients and the maximum per application is 2000: use a longer prefix |
| `GET /clients` | `chainPublicId`, `noChain` | Not both at once | `Escoja una cadena o «Sin cadena», no las dos.` | 400 | Choose a chain or «No chain», not both |
| `GET /sales-analysis/by-chain` | `month`, `compareMonth` | Format `YYYY-MM` | `El mes debe tener la forma AAAA-MM (por ejemplo 2026-10).` | 400 | The month must have the form YYYY-MM (for example 2026-10) |
| `GET /sales-analysis/by-chain/series` | `chainPublicId` | Required | `Indique la cadena (chainPublicId).` | 400 | Give the chain (chainPublicId) |
| `GET /sales-analysis/by-chain/series` | `fromMonth`, `toMonth` | `toMonth` not before `fromMonth` | `El mes final no puede ser anterior al inicial.` | 400 | The final month cannot be before the initial one |
| `GET /sales-analysis/by-chain/series` | range | Up to 36 months | `El rango no puede pasar de 36 meses.` | 400 | The range cannot exceed 36 months |
| Any endpoint | module | `SALES_ORDERS` on | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 | The module is not enabled for this company |
| Any endpoint | permission | See each section's permission | 403 response (no text of its own; `PERMISSION_DENIED` is left) | 403 | — |

**Screen warnings** (validated before calling the server, no HTTP): `El nombre de la cadena es obligatorio.` (the chain's name is required), `El nombre admite hasta 100 caracteres.` (the name allows up to 100 characters) and `Las notas admiten hasta 500 caracteres.` (the notes allow up to 500 characters). And in «Add by name»: `Hay N coincidencias y el máximo es 2000: use un texto más específico.` (there are N matches and the maximum is 2000: use a more specific text), `Cambió el texto o un interruptor: vuelva a simular para poder aplicar.` (the text or a switch changed: simulate again to be able to apply) and `Ningún cliente empieza con ese texto.` (no client starts with that text).

## 11. Frequent cases

**«Walgreens 1, 2 and 3» and «Walgreensboro».** Type «Walgreens» in «Add by name» with «Whole word» on and simulate: «Walgreens 1», «Walgreens 2» and «Walgreens #3» enter; «Walgreensboro» does not appear. If you really wanted «Walgreensboro», turn «Whole word» off.

**The route «Cadenas» is not a chain.** In Solutions there is a **sales route** called «Cadenas» (chapter [52](52-rutas-de-venta.md)). It is a route like the others and this batch **does not touch it or create chains by itself**. To group clients by chain you have to create the chains here and assign clients to them.

**The simulation changed: simulate again.** Between «Simulate» and «Apply» someone renamed a client, deactivated it, moved it to another chain or created a new one that matches. Simulate again, review and apply.

**Exclude before applying.** Untick the client's box: it becomes «Excluded» and is not touched. There is no need to simulate again.

**It will not let me deactivate.** The chain still has **active** clients (remove them or move them to another chain; inactive ones do not get in the way) or has **special prices, price lists or «Include» offers published and in effect** (the message says how many: remove them on the chain record or in Accounting → Prices and offers). The clients are checked first.

**What price does a client of the chain get?** The lowest among its own, its chain's, the offer that applies to it and the general list, always **before IVU** (chapter 55 §2). If you change its chain, it gets the new chain's prices from that moment; if the chain is deactivated, it counts as «no chain».

**I moved a client and its history changed chain.** It is as expected: the sale, also the one imported from QuickBooks, is counted in the client's **current** chain. Old orders are also shown with the new chain.

**A client did not enter the chain.** Either it was already in another chain («Skipped (other chain)» shows; turn on «Move…» if you want to change it), or it is inactive (bulk add only takes active clients; add it by hand).

**I do not see «Chains» in the menu.** The permission is missing (`sales.chain` or `clients.read`) or the Sales orders module is off.

**The chain does not add up in «By chain».** Look at the reconciliation's **Difference**: it must be $0.00. History sales not matched fall into «No chain»; match the names in Sales analysis → Setup.
