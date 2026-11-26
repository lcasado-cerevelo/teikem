# 55 — Special prices and offers by client and by chain

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter [`../55-precios-especiales-y-ofertas.md`](../55-precios-especiales-y-ofertas.md) (Batch 247, 2026-11-26). The Spanish manual is the reference. **The API returns its error messages in Spanish only**, so the messages below are quoted in Spanish (exactly as the software shows them) with their meaning in English. Interface labels use the English text of the screens.

Batch 247 (Batch C of the Solutions plan). Three new things in **Sales orders → prices**:

1. **«Special prices»**: a price of its own for a **client** or for a **chain** (Walgreens, CVS…), with a «From» date and, optionally, a «To» date. It lives in a panel of the client's page and of the chain's page.
2. **Chains join price lists and offers**: a list or an offer can apply to a whole chain, and an offer can **include** or **exclude** clients and chains.
3. **The price rule changes: the lowest price always wins** among the client's price, its chain's price, the offer and the general list. Before, the client's negotiated price won over the general list even when it was higher.

> **A special price is a price BEFORE IVU.** The client's exemptions (state certificate, municipal certificate, both, or full exemption) are applied **afterwards**, on the price that is charged. This batch does not change the tax calculation. To be confirmed with the accountant.

> **The lowest price always wins.** If a special price is **higher** than the general list, the general list price is charged and the system warns («Higher than the general list»). Nothing is blocked: fix the special price.

Screens: [L247](../frontend/l247-precios-especiales.md) (Spanish edition; the captures `img/l247-*-en-*.png` in that folder were taken in English). Every error message is in the [FAQ](faq.md). This chapter completes [47 — Price lists and offers](47-precios-y-ofertas.md) (its section 2 describes the previous rule) and chapter 53 (Client chains, Spanish only so far).

## 1. Where it is and who can

- **Where:**
  - **Lists → Clients and contracts** → the client's page → **«Special prices»** panel (`/catalog/clients`).
  - **Lists → Chains** → the chain's page → **«Special prices»** and **«Offers»** panels (`/catalog/chains`).
  - **Accounting → Prices and offers** (`/money/prices`): the «Chains» section of a price list, the **«Also show special prices»** switch and the **«Include»** / **«Exclude»** boxes of the offer.
  - The sales order and the guided order: the **source** of each price.
- **Module:** Sales orders (`SALES_ORDERS`). With the module off, the routes answer 403 with `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` (the module is not enabled for this company).
- **Permissions** (no new permissions):

| What | Permission | Module |
|---|---|---|
| See the «Special prices» panel (client and chain) and the chain's «Offers» panel | `sales.view` or `sales.pricelist` | `SALES_ORDERS` |
| Add, change and remove special prices | `sales.pricelist` | `SALES_ORDERS` |
| Put or remove chains in a price list or an offer; change an offer's audience (include / exclude) | `sales.pricelist` | `SALES_ORDERS` |
| See lists and offers through the API (`includeSpecial`, `chainPublicId`) | `sales.view` | `SALES_ORDERS` |
| Read the «Special prices» data source (`SPECIAL_PRICE`) in views, indicators and charts | `sales.view` | `SALES_ORDERS` |
| Charge a price different from the resolved one on an order (manual price) | `sales.price` (unchanged) | `SALES_ORDERS` |

- Someone with `sales.view` but without `sales.pricelist` **sees** the «Special prices» panel; the add, change and remove buttons do not show. Through the API, `POST`, `PATCH` and `DELETE` give 403 `Falta el permiso 'sales.pricelist'.` (missing permission) and leave a `PERMISSION_DENIED` security event.
- The «Prices and offers» screen still requires `sales.pricelist` (see chapter 47).
- `TenantId` comes from the session: a client, chain, list, offer or special price of another company answers 404.
- **Neither the portal nor the client app show the chain or special prices as such.** The client only sees the price that applies.

## 2. Where the price comes from: the lowest wins

When a line is created or edited (sales order, guided order, catalog), the server compares up to **four candidates** and keeps the **lowest**:

| # | Candidate | Applies if… |
|---|---|---|
| 1 | **The client's price**: from a published, in-effect list that includes the client (for example its special price) | the list has the product |
| 2 | **Its chain's price**: from a published, in-effect list that includes the client's chain (for example the chain's special price) | the client has an **active** chain and the list has the product |
| 3 | **Offer**: the cheapest in-effect offer that applies to the client or its chain | the offer has the product or kit |
| 4 | **General list**: the published, in-effect list for all clients | it has the product |

Rules:
- **The lowest price wins**, wherever it comes from. There is no hierarchy: the client's price does **not** win just for being the client's.
- **On a tie**, the order of preference is: client, chain, offer, general list.
- **Inside the same level the lowest also wins.** If a client has two lists with the same product in effect at once (say its special price and a group list), the lower is charged; on a tie, the list that started later and, on the same date, the newer one.
- **The product's sale price does not compete.** It is used only if none of the four has the product. A special price higher than the product's price, with no general list, is charged anyway.
- **A deactivated chain counts as «no chain»:** its clients stop getting its prices and offers.
- **A client that changes chain** gets the new chain's prices from that moment. Drafts keep their lines' price (they are re-priced only if the draft's client is changed); orders already requested keep their source.
- **Validity** is evaluated with **today's** date when the line is written (the draft's or the request's), not the delivery date.

**Example.** A client of the Walgreens chain; product SPX-A sells at 12. General list: 10. Chain special price: 8. Client special price: 7.50. In-effect offer for everyone: 9. **7.50** is charged (source «Special price»). If the client's is removed, **8** is charged («Chain Walgreens»). If the chain's goes up to 11, **9** is charged («Offer …») and the row warns that the chain's is higher than the general list.

The source (`priceSource`) can be `CLIENT_LIST` (client's price), `CHAIN_LIST` (its chain's), `OFFER`, `GENERAL_LIST`, `PRODUCT` or `NONE`.

### Notices (not errors)

They arrive with code 200 and do not prevent saving, publishing or sending.

| Notice (code) | When | Where | Text |
|---|---|---|---|
| Special price higher than the general list, in the list (`SPECIAL_ABOVE_GENERAL`) | A line of a list that is not general costs more than the same product in the in-effect general list | The list's page («List warnings») and the «Special prices» panel («Special price warnings») | `Precio especial más caro que la lista general: SPX-B a $15.00 y en la lista general a $14.00. Se cobra el más bajo; corrija el precio especial.` |
| Special price higher than the general list, in the order (`SPECIAL_ABOVE_GENERAL`) | When pricing a product whose client or chain price is higher than the general list | Catalog, guided order and draft (next to the product) | `El precio especial «Precio especial — Walgreens SPX (desde el 26/10/2026, sin fecha de fin)» pone SPX-B a $15.00, más caro que la lista general ($14.00): se aplica el de la lista general. Corrija el precio especial.` |

The offer notices of chapter 47 (`TWO_OFFERS`, `OFFER_ABOVE_GENERAL`, `CHEAPER_OFFER`) stay the same.

> **Language of the server's texts.** The name of the dedicated list («Precio especial — … (del 26/10/2026 al 30/11/2026)»), the notice above (which quotes that name) and an offer's validity («desde el …, sin fecha de fin») are built by the server **in Spanish and as day/month/year**, even when the screen is in English or shows dates as month/day/year. It is a known pending item.

## 3. Special prices (client's and chain's page)

**What it does.** It stores the price that a client, or all the clients of a chain, pay for a product or kit, with a «From» date and an optional «To» date. While it is valid it enters the «lowest wins» comparison.

**Who can.** See: `sales.view` or `sales.pricelist`. Add, change and remove: `sales.pricelist`. Module `SALES_ORDERS`.

**How to use it (screen).**
- **Client's:** Lists → Clients and contracts → the client → **«Special prices»** panel. The page starts with the panels collapsed: open it by its title.
- **Chain's:** Lists → Chains → the chain → **«Special prices»** panel. **Every client of the chain that has no lower price of its own** gets it.
- At the top of the panel there is an add row: **Product or kit** (search by SKU or name), **Price**, **From** (today by default) and **To** (optional), and the **«Add special price»** button. Below it, the help: «The price is before IVU. The client's exemptions are applied afterwards, on the price charged.»
- The panel's list has: **Product**, **Price**, **From**, **To**, **Status**, **General list** and **Charged today**. On a narrow page or on the phone every row becomes a card. It is sorted by brand, SKU and «from»; **«Sort by»** changes the order.
- **Status:** In effect, Scheduled (not started yet), Expired or Draft (see section 8). Expired ones **do not show** until **«Also show expired»** is on. When 14 days or fewer are left, the «To» date shows in amber with «Expires in N d».
- **General list** is the in-effect general list's price for that product (or the one it will have when the special price starts). If the special one is higher, a warning chip (amber triangle) shows next to the price, plus the «Special price warnings» box.
- **Charged today** is what is really charged today for that product and **where it comes from**: chip «Special price» (the client's), «Chain {name}», «Offer {name}», «General list», «Product price» or «No price». On the chain's page it is what **a client of the chain with no price of its own** would pay.
- Every row has **Change price** (pencil) and **Remove** (trash); removing asks for confirmation: «The special price of {SKU} · {name} is removed. From then on the applicable price is charged (general list or offer).»
- **Only on the client's page**, below its prices:
  - **«From its chain «{name}»»** (with the «Open the chain» link): the chain's in-effect and scheduled special prices, **read only**.
  - **«Price lists that include it»**: the names of the normal, published, in-effect lists that reach it, «by the client» or «by its chain». Only name, state and number of lines.
- An **inactive** client or a **deactivated** chain does not accept new prices: the panel says «It is inactive: it does not accept new prices.»

**How to use it (API).** Every route answers the full view (`SpecialPricesDto`); `includeExpired=true` adds the expired ones.

| What | Route | Permission |
|---|---|---|
| See the client's | `GET /api/v1/special-prices/clients/{clientPublicId}?includeExpired=` | `sales.view` or `sales.pricelist` |
| Add | `POST /api/v1/special-prices/clients/{clientPublicId}` | `sales.pricelist` |
| Change | `PATCH /api/v1/special-prices/clients/{clientPublicId}/{itemId}` | `sales.pricelist` |
| Remove | `DELETE /api/v1/special-prices/clients/{clientPublicId}/{itemId}` | `sales.pricelist` |
| The same for the chain | `…/special-prices/chains/{chainPublicId}` and `…/{itemId}` | same |

Add:

```json
POST /api/v1/special-prices/chains/{chainPublicId}
{ "productPublicId": "7f0c…", "price": 8.00, "validFrom": "2026-11-26", "validTo": null }
```

Change only the price, move the dates or remove the «To»:

```json
PATCH /api/v1/special-prices/clients/{clientPublicId}/{itemId}
{ "price": 7.50 }
{ "validFrom": "2026-12-01", "validTo": "2026-12-31" }
{ "clearValidTo": true }
```

`itemId` is the row's `itemId` returned by the `GET`. Response: `ownerKind`, `ownerPublicId`, `ownerName`, `ownerIsActive`, `chainPublicId` / `chainName` (the client's chain), `chainRowCount`, `rows` (its own), `chainRows` (its chain's, read only), `clientLists` and `warnings`. Every row has `price`, `validFrom`, `validTo`, `state`, `daysToExpire`, `generalListPrice`, `aboveGeneral`, `chargedPrice`, `chargedSource` and `chargedSourceName`.

**Fields and rules.**

| Field | Rule |
|---|---|
| `productPublicId` | Required. A product of the company itself (not a client's), active, and not an offer kit (an offer kit is changed in an offer). A normal kit is fine. |
| `price` | Required. Before IVU. Up to 4 decimals, not negative. **The screen asks for a price greater than zero; the API accepts 0.** |
| `validFrom` | Required. |
| `validTo` | Optional (no «To» = does not expire). Cannot be before «From». **When creating**, it cannot be before today; when editing it can (that is how a running price is closed). |
| Same product, same owner, same dates | Not an error: it **changes the price** of that row. |
| Same product, same owner, dates that overlap another validity | 409 (section 9). A chain price and a client price do **not** clash: the lowest wins. |
| Limit | 2000 lines per dedicated list. |

What happens inside (useful for support):
- Each **owner** (client or chain) has **one dedicated list per validity** (a «from / to» pair), published. It is named «Precio especial — {owner} ({validity})» and never exceeds 120 characters.
- Adding a price with new dates **creates** the dedicated list for those dates; with the same dates it **adds the line** to the existing one.
- **Changing only the price** changes it in its list. **Changing the dates** moves the line to the dedicated list of the new dates (created if missing); a list left without active lines is **deactivated by itself**.
- **Removing** deactivates the line (it stays in the history); a list without active lines is deactivated.
- The price engine sees the dedicated list like any published list: that is why the lowest wins just like with the others.

## 4. Chains in price lists

**What it does.** A price list that is **not** for everybody can apply to **chosen clients**, to **chains** or to both. A chain list reaches the chain's clients **while the chain is active**.

**Who can.** `sales.pricelist`. Module `SALES_ORDERS`.

**How to use it (screen).** Accounting → Prices and offers → open the list → **«Chains»** section (next to «Clients»): «Add a chain» and the **×** of each chain. With no chains it says «No chains.».

**How to use it (API).**

```json
POST /api/v1/price-lists/{publicId}/chains
{ "chainPublicIds": ["3b1e…"] }
DELETE /api/v1/price-lists/{publicId}/chains/{chainPublicId}
```

Rules:
- Up to **500 chains** per list. Repeated ones are ignored. A **general** list (applies to all) carries no chains: 422.
- Publishing requires at least one client **or** one chain (if not general). A published list **cannot be left without a client or chain**: put it back to draft first.
- Two normal lists that share a client **or a chain**, with the same product and overlapping dates, give 409. A client list and a chain list do **not** clash, even if the client belongs to that chain: the lowest wins.

### Dedicated lists («Special price of …»)

They are the lists created by the «Special prices» screen. **They are hidden by default** in the price lists (there can be hundreds). With the **«Also show special prices»** switch (`GET /api/v1/price-lists?includeSpecial=true`; remembered) they show with the chip **«Special price of {owner}»** and the columns «Only that client» / «Only that chain».

When opening a dedicated list:
- A note says: «This list is the special price of «{owner}»: its name, dates and who it applies to are changed in «Special prices» on its page. Here you can change prices, publish or deactivate.», with the **«Open the page»** link.
- You **can** change prices, add or remove lines, set photos, change notes, **publish, put back to draft and deactivate**.
- You **cannot** change the name, the dates, «applies to all», or add or remove clients or chains: 422 (section 9).
- A dedicated list **does not clash** with a normal list that includes the same client: they coexist and the lowest wins.

## 5. Offers: include and exclude clients and chains

**What it does.** An offer's **audience** (who it applies to) has three modes, for the whole offer:

| Mode (`audienceMode`) | Who it applies to | Screen |
|---|---|---|
| `ALL` | All clients | Chip «All clients»; both boxes empty |
| `INCLUDE` | **Only** the chosen clients and chains | **«Include»** box; chip «Chosen only: N» |
| `EXCLUDE` | Everybody **except** the chosen clients and chains | **«Exclude»** box; chip «Everyone except N» |

Before the batch only «all» and «chosen only» existed. Offers that were «chosen only» became `INCLUDE` when the update was applied.

**Who can.** See: `sales.view`. Create, change and choose clients and chains: `sales.pricelist`. Module `SALES_ORDERS`.

**How to use it (screen).** Accounting → Prices and offers → open the offer → **«Who it applies to»** section: two boxes, **«Include»** («Only these clients and chains get the offer.») and **«Exclude»** («Everybody gets the offer except these clients and chains.»). Each admits clients (**«Add client»**) and chains (**«Add a chain»**).
- **Filling one box disables the other** («Remove what is chosen in the other box to use this one.»). With both empty, the offer is for everybody.
- When adding to an empty box, the screen changes the audience and adds.
- **Removing the last** client or chain asks for confirmation (**«Remove the last one?» / «The offer will be for all clients.»**, button «Make it for everyone»). If you confirm, the offer becomes `ALL`. **Careful:** an offer «only for chain X» from which the chain is removed ends up **for all clients**, not for nobody.
- An `INCLUDE` offer with nobody chosen is published but **applies to nobody**; the screen says so.
- The chain in the offer shows as «Chain {name}»; if it is deactivated, in amber.

**How to use it (API).**

```json
POST /api/v1/offers
{ "name": "Summer", "validFrom": "2026-11-26", "audienceMode": "INCLUDE" }
PATCH /api/v1/offers/{publicId}
{ "audienceMode": "EXCLUDE" }
POST /api/v1/offers/{publicId}/chains
{ "chainPublicIds": ["3b1e…"] }
DELETE /api/v1/offers/{publicId}/chains/{chainPublicId}
GET /api/v1/offers?chainPublicId={chainPublicId}
```

Rules:
- `audienceMode` is `ALL`, `INCLUDE` or `EXCLUDE` (lowercase is accepted). If only `appliesToAll` comes: `true` is `ALL`, `false` is `INCLUDE`; with `false` on an `EXCLUDE` offer it stays `EXCLUDE`. If both come and disagree: 400.
- **Switching to `ALL` with chosen clients or chains** gives 422; **switching between `INCLUDE` and `EXCLUDE` with chosen ones** gives 422: remove them first.
- **Publishing:** an `INCLUDE` with nobody chosen is not published (422). An `EXCLUDE` with nobody chosen **is**: it applies to everybody.
- Adding clients or chains to an `ALL` offer gives 422. Up to **500 chains** and 2000 clients per offer. Repeated ones are ignored.
- **Without a client** (generic catalog, no client chosen): `ALL` offers, `INCLUDE` offers that have the chain and `EXCLUDE` offers that do not exclude it apply.
- `GET /offers?chainPublicId=` returns only the **active, published, not expired** offers that apply to the chain: for everybody, including it, or excluding others without excluding it.
- The «clash with a cheaper offer» notices (`CHEAPER_OFFER`) now consider chains and exclusions. It is a conservative estimate: when in doubt it warns. It prevents nothing.

### The chain page's «Offers» panel

Lists → Chains → the chain → **«Offers»**, read only: **Offer**, **Validity** («From … no end date» or «From … to …»), **How it applies** (**Everyone**, **Included**, **Everyone except the excluded**) and **Lines**. It says «No current offers» if none applies today. The «See all offers in Prices and offers» link opens the full screen.

## 6. The price's source on the order

- Each order line stores **where its price came from** when it is written: the source, the list or offer, the chain and the **resolved price**. Codes are stored, not names: the screen reads the **current** name of the list, offer or chain (if a list is renamed, the order shows the new name).
- On the order and in the guided order every line shows a **source chip**: «Chain {name}», «Special price» (the client's), «Offer «{name}»» or none when it comes from the general list or the product's price. An order **already requested** keeps saying its source even if the list changes later.
- If someone with `sales.price` typed a price different from the resolved one, the line is a **manual price**: the server returns `isManualPrice = true`, the screen shows «Manual price» and, next to it, «List: {price}».
- The source is **recalculated** when the draft's client is changed (a manual price is kept) and when the price of a **requested** order is changed (with today's resolution). Requesting the order does not rewrite it.
- **Lines of orders created before the batch have no source** (empty columns): they show no chip and do not count as manual.
- In the API, the order's `lines[]` carry `priceSource`, `priceListPublicId` / `priceListName`, `offerPublicId` / `offerName`, `priceChainPublicId` / `priceChainName`, `resolvedUnitPrice` and `isManualPrice`; `linePrices[]` and the catalog carry `chainPublicId` / `chainName`.

## 7. Taxes: the special price is before IVU

Tax is calculated on the price **that is charged**, wherever it comes from (special price, chain, offer or list). The client's exemptions are applied **afterwards**. Example from the tests: special price 8.00, state IVU 10.5 % and municipal 1 %:

| Client's situation | Line tax |
|---|---|
| No exemption | 0.92 |
| Municipal certificate (does not pay the municipal part) | 0.84 |
| State certificate (does not pay the state part) | 0.08 |
| Both certificates | 0.00 |
| Full exemption | 0.00 |
| Full exemption and a partial certificate | 0.00 (the full one wins) |
| Expired full exemption and a valid municipal certificate | 0.84, with the notice of the expired exemption |

Pending: the owner will confirm with the accountant whether any case needs different treatment (for example, prices with IVU included). The document types (state, municipal and full) and tying the exemption to its document arrive in a later batch.

## 8. Statuses and transitions

**There are no new statuses.** No action in this chapter changes an order status or uses the status service. A list or a special price has a **computed state** from whether it is published and its dates:

| State | When | Charged? |
|---|---|---|
| Draft | The list is not published | No |
| Scheduled | Published and its «from» has not arrived | No, until it starts |
| In effect | Published and today is between «from» and «to» (ends included; no «to» = does not expire) | Yes |
| Expired | Published and its «to» has passed | No |

Actions and what each one triggers:

| Action | Who can | What it validates | Effect |
|---|---|---|---|
| Add a special price | `sales.pricelist` | Active owner, valid product, dates, no overlap with another validity of the same product and owner | Creates the dedicated list of those dates (published) or adds the line to the existing one |
| Change the price | `sales.pricelist` | Valid price; no overlap | Changes the line |
| Change the dates | `sales.pricelist` | «to» not before «from»; no overlap | Moves the line to the dedicated list of the new dates; the emptied one is deactivated |
| Remove | `sales.pricelist` | The line is an active special price of that owner | Deactivates the line (stays in the history); the empty list is deactivated |
| Publish / Back to draft / Deactivate the dedicated list (in Prices and offers) | `sales.pricelist` | The usual list rules | Changes whether it is charged |
| Deactivate the chain | `sales.chain` | Zero active clients and **no published, in-effect list or `INCLUDE` offer with the chain** (409 in either case) | The chain leaves the pick lists; its clients become «no chain» for prices |

What stays blocked:
- **Inactive client or deactivated chain:** accepts no new special prices, nor changes, nor removals (422). Prices it already had are shown and still exist; those of a deactivated chain **stop being charged** to its clients (they count as no chain).
- **Dedicated list:** its name, dates and audience are not changed from Prices and offers (422).
- **Expired special price:** it is not charged. To use it again, add it with new dates.
- **`ALL` offer:** accepts no chosen clients or chains (422).

## 9. Validations and messages

The message comes as-is in the error's `title` field (and in red on the screen, except those the screen warns about before sending). **Messages are in Spanish; the English meaning is in the last column.**

### Special prices (`/api/v1/special-prices/…`)

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| (body) | Must come | `El cuerpo de la solicitud es obligatorio.` | 400 | The request body is required |
| `productPublicId` | Required | `Escoja el producto o kit de cada renglón.` | 400 | Choose the product or kit |
| `price` | Required | `Indique el precio de cada renglón.` | 400 | Enter the price |
| `price` | Not negative, up to 9,999,999,999 | `El precio no es válido.` | 400 | The price is not valid |
| `price` | Up to 4 decimals | `El precio admite como máximo 4 decimales.` | 400 | At most 4 decimals |
| `validFrom` | Required | `Indique desde qué fecha vale el precio especial.` | 400 | Enter the «from» date |
| `validTo` | Not before «from» | `La fecha «hasta» no puede ser anterior a la fecha «desde».` | 400 | «To» before «from» |
| `validTo` | When **creating**, today or later | `La fecha «hasta» ya pasó: un precio especial nuevo tiene que valer hoy o después.` | 400 | The «to» date already passed |
| `productPublicId` | At most 2000 lines per list | `Una lista admite como máximo 2000 renglones.` | 400 | List line limit |
| Client | Exists in the company | `Cliente no encontrado.` | 404 | Client not found |
| Chain | Exists in the company | `Cadena no encontrada.` | 404 | Chain not found |
| Product | Exists | `Producto no encontrado.` | 404 | Product not found |
| `itemId` | Is an active special price of that owner | `Precio especial no encontrado.` | 404 | Special price not found |
| Client | Active (to write) | `El cliente está inactivo; no se le puede vender.` | 422 | Client inactive |
| Chain | Active (to write) | `La cadena está desactivada: actívela o escoja otra.` | 422 | Chain deactivated: reactivate it or choose another |
| Product | Company's own | `Solo se ponen en una lista productos propios de la compañía.` | 422 | Only the company's own products |
| Product | Active | `El producto {SKU} está inactivo: no se puede poner en la lista.` | 422 | Product inactive |
| Product | Not an offer kit | `El kit {SKU} es un kit de oferta: su precio va en una oferta, no en una lista de precios.` | 422 | Offer kits are priced in an offer |
| Dates | Do not overlap another price of the same product and owner | `La lista se cruza con «{lista}» en las mismas fechas y para los mismos clientes o cadenas en: {SKU, …}. Cambie las fechas o quite esos productos de una de las dos.` (up to 5 SKUs and «y N más») | 409 | Overlap with another list |
| Simultaneous add | Two adds at once to the same list | `La lista cambió al mismo tiempo. Recárguela e intente de nuevo.` | 409 | The list changed at the same time |
| Simultaneous change | Someone changed the list at once | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 | Modified by another user |
| Permission | `sales.pricelist` to write; `sales.view` or `sales.pricelist` to see | `Falta el permiso '…'.` | 403 | Missing permission |
| Module | `SALES_ORDERS` on | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 | Module not enabled |

Screen warnings before sending (not from the server): «Choose the product or kit.», «Enter a price greater than zero (up to 4 decimals).», «Choose the «from» date.», «The «to» date cannot be before «from».» and the «to»-already-passed text.

### Chains in price lists (`/api/v1/price-lists/…`)

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `chainPublicIds` | At least one | `Escoja las cadenas de la lista.` | 400 | Choose the list's chains |
| `chainPublicIds` | At most 500 | `Una lista admite como máximo 500 cadenas.` | 400 | At most 500 chains |
| Chain | Exists in the company | `Cadena no encontrada.` | 404 | Chain not found |
| Chain in the list | Is in the list (when removing) | `Cadena de la lista no encontrada.` | 404 | The list's chain not found |
| Chain | Active | `La cadena está desactivada: actívela o escoja otra.` | 422 | Chain deactivated |
| List | Not general | `La lista aplica a todos los clientes: no lleva cadenas escogidas. Desmarque «aplica a todos» para escogerlas.` | 422 | General lists carry no chains |
| List | Not dedicated (clients and chains, name, dates, «applies to all») | `Esta lista es el precio especial de «{dueño}»: el nombre, las fechas y a quién aplica se cambian en «Precios especiales» de su ficha.` | 422 | Dedicated list: change it on the owner's page |
| Publish / remove the last | A client or a chain if not general | `Escoja al menos un cliente o una cadena, o marque que la lista aplica a todos los clientes.` | 422 | Choose a client or chain, or make it general |
| Make it general | No chosen clients or chains | `La lista tiene clientes o cadenas escogidos: quítelos antes de hacerla general (aplica a todos).` | 422 | Remove them before making it general |
| List overlap | Same type, same product, overlapping dates | `La lista se cruza con «{lista}» en las mismas fechas y para los mismos clientes o cadenas en: …` | 409 | Overlap with another list |

### Offers: audience and chains (`/api/v1/offers/…`)

| Field | Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|---|
| `audienceMode` | `ALL`, `INCLUDE` or `EXCLUDE` | `El público de la oferta debe ser ALL (todos), INCLUDE (solo los escogidos) o EXCLUDE (todos menos los escogidos).` | 400 | Invalid audience |
| `appliesToAll` and `audienceMode` | Agree if both come | `“Aplica a todos” no coincide con el público escogido: indique solo el público (todos, solo los escogidos o todos menos los escogidos).` | 400 | Contradictory fields |
| `audienceMode` | Switching between `INCLUDE` and `EXCLUDE` with chosen ones | `Quite los clientes y cadenas de la oferta antes de cambiar entre incluir y excluir.` | 422 | Remove them before switching |
| `audienceMode` | Switching to `ALL` with chosen ones | `La oferta tiene clientes o cadenas escogidos: quítelos antes de hacerla para todos.` | 422 | Remove them before making it for all |
| Publish | `INCLUDE` with at least one client or chain | `Escoja al menos un cliente o una cadena, o marque que la oferta aplica a todos los clientes.` | 422 | Choose someone or make it for all |
| `chainPublicIds` | At least one | `Escoja las cadenas de la oferta.` | 400 | Choose the offer's chains |
| `chainPublicIds` | At most 500 | `Una oferta admite como máximo 500 cadenas.` | 400 | At most 500 chains |
| Chain | Exists in the company (also in `?chainPublicId=`) | `Cadena no encontrada.` | 404 | Chain not found |
| Offer's chain | Is in the offer (when removing) | `Cadena de la oferta no encontrada.` | 404 | The offer's chain not found |
| Chain | Active | `La cadena está desactivada: actívela o escoja otra.` | 422 | Chain deactivated |
| Offer | Not for all (chains) | `La oferta aplica a todos los clientes: no lleva cadenas escogidas. Desmarque «aplica a todos» para escogerlas.` | 422 | `ALL` offers carry no chains |
| Offer | Not for all (clients) | `La oferta aplica a todos los clientes: no lleva clientes escogidos. Desmarque «aplica a todos» para escogerlos.` | 422 | `ALL` offers carry no clients |

### Deactivating a chain

| Rule | Message (Spanish) | HTTP | Meaning |
|---|---|---|---|
| With active clients (checked first) | `La cadena tiene 1 cliente activo: quítelo de la cadena antes de desactivarla.` (or `… N clientes activos: quítelos …`) | 409 | The chain has active clients |
| With published, in-effect price lists (including special prices) or `INCLUDE` offers | `La cadena tiene precios especiales u ofertas publicados y vigentes ({N} listas de precios y {M} ofertas): quítela de ellos antes de desactivarla.` | 409 | The chain still has special prices or offers |

«In effect» here means: active, published and with an empty «to» or one of today or later (a **scheduled** one also blocks). `EXCLUDE` offers do not block; expired, draft or deactivated lists do not either. To clear the block: on the chain's page remove its special prices; in Prices and offers remove it from the lists and from the `INCLUDE` offers (or deactivate them).

## 10. Data source «Special prices» (`SPECIAL_PRICE`)

For Views, Indicators and Charts (analytics). One row per active special price, with its state **as of today**: `OwnerKind` (`CLIENT` or `CHAIN`), `OwnerName`, `ChainName`, `Sku`, `ProductName`, `Brand`, `Price`, `ValidFrom`, `ValidTo`, `State`, `DaysToExpire` (empty = no «to»; 0 = expires today; negative = already expired), `GeneralListPrice`, `AboveGeneral`, `PriceListName` and `IsActive` (owner active). It is read with `sales.view` and the `SALES_ORDERS` module on. It includes expired and draft ones (the `State` field says so): filter by `State` to see only those in effect. There is **no scheduled «special price about to expire» notice yet**: it arrives with Batch D.

## 11. Frequently asked questions of the module

**The client has a special price but is charged the general list. Why?**
Because the general list is **lower or equal**. The lowest price always wins. The row marks it («Higher than the general list») and «Charged today» says «General list». Lower the special price or remove it.

**The general list is charged and the special price is cheaper.**
Check the **Status**: a special price is charged only when **In effect** (published and within its dates). «Scheduled» has not started, «Expired» has passed and **«Draft»** means someone put its dedicated list back to draft (Prices and offers → «Also show special prices» → «Publish»). Also check that the client, or its chain, is **active**: a deactivated chain counts as no chain.

**The offer does not show for an excluded client (or does show for one I did not choose).**
An **`EXCLUDE`** offer applies to everybody **except** the chosen clients and chains; if the client, or its **chain**, is excluded, it does not get it. An `INCLUDE` offer applies only to the chosen ones or to the clients of the chosen chains. See the offer's «Who it applies to» section.

**I removed the last chain of an offer and now it applies to everybody.**
That is what the confirmation says («The offer will be for all clients.»). An offer with nobody chosen does not stay «chosen only». If you do not want it for everybody, deactivate it, or choose again who it applies to before removing the last one.

**How do I change only a special price's date?**
In the panel, **Change price** (pencil) and change «From» or «To». The line moves to the list of those dates by itself; if it overlaps another validity of the same product for the same owner, it gives 409. To remove the «To», leave it empty.

**Why don't I see the «Special price of …» lists in Prices and offers?**
They are hidden by default. Turn on **«Also show special prices»**. They are changed from the client's or the chain's page; in the list you can only change prices, publish or deactivate.

**It will not let me deactivate the chain.**
It has active clients, or published, in-effect special prices, lists or `INCLUDE` offers. The message says how many. Remove them (section 9, «Deactivating a chain») and try again.

**Does the special price carry IVU?**
The special price is **before** IVU: tax is added afterwards, on the price charged, and the client's exemptions (state, municipal or full) are applied on that tax. To be confirmed with the accountant.

**The client changed chain. Which price does it get?**
From that moment, the new chain's. Drafts that already had a price keep it (change the draft's client to re-price) and requested orders keep their source.

**An old order does not show where the price came from.**
Lines from before Batch 247 did not store the source. Only lines written or changed since the batch have it.

**The dedicated list's date shows differently from the screen's (26/10/2026 vs 10/26/2026).**
The dedicated list's name and some notices are built by the server as day/month/year and in Spanish. It is a known pending item; the data is the same.

## 12. For support

- **Tables:** `PriceListChain` and `OfferChain` (chains of lists and offers; remove = `IsActive = 0`), `PriceList.SpecialClientId` / `SpecialChainId` (the «dedicated list» mark), `Offer.AudienceMode` (`ALL`/`INCLUDE`/`EXCLUDE`; `AppliesToAll = 1` only with `ALL`) and `SalesOrderLine.PriceSource`, `PriceListId`, `OfferId`, `PriceChainId`, `ResolvedUnitPrice` (empty in old lines).
- **A single active dedicated list** per owner and «from / to» pair (indexes `UX_PriceList_SpecialClient` and `UX_PriceList_SpecialChain`). If two people add the same price at once, one gets 409.
- **«Why was X charged?»:** on the order, the source chip and the line (`priceSource`, `resolvedUnitPrice`, `isManualPrice`). If missing, the line predates the batch.
- **Audit:** changes to a list's chains are logged with type `PRICE_LIST` and an offer's with `OFFER`.
- Without permission, or with the module off, the 403s leave a `PERMISSION_DENIED` security event.
