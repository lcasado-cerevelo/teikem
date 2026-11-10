# 48 — Guided order (web and mobile app)

<!-- kemi:staff -->

> **English edition.** Translation of the Spanish chapter `docs/manual/48-pedido-guiado.md`. The server only answers in Spanish, so every "Exact message" column keeps the **exact Spanish text** the screen shows, followed by its English meaning.

Batches 223 and 226 (2026-11-07) — salespeople's order, parts P3 (web) and P4 (mobile app). The **guided order** is another door to the **same sales order** of [chapter 18](18-ordenes-de-venta.md): the salesperson builds the order in **four steps** (client, products, delivery, authorization), it is saved as a **draft** at every step and at the end they **send** it confirming who they are. The «New order» window stays as it always was, for the office. Prices come from the lists and the offers ([47](47-precios-y-ofertas.md)); the new client is born as a prospect ([02 §1.8](02-clientes-y-contratos.md#18-prospect-client-minimal-sign-up-and-approval-batch-222)).

> **Screenshots pending (Batches 229, 230, 231 and 232).** Step 2 changed: offer cards with the offer's name and validity, a photo square on every product that has a photo, price warnings and «Change» on the pieces of an offer kit in the cart (Batches 231 and 232: the offer kit shows only as a card and only if it is in an offer in effect; the assembled kit shows as one more product). The step 2, cart and step 3 screenshots below are older (batch 228) and do not show that; they must be retaken with the Playwright walkthrough.
>
> Screenshots from the batch 228 walkthrough (2026-11-07, demo data): web on desktop (light and dark) and at 360 px. Still pending: the **mobile app** ones (Orders → My orders, the steps and the fingerprint; the Playwright walkthrough does not run the app) and the **shortage dialog** (it is the same as in [18 §6](18-ordenes-de-venta.md)).

## 1. Who can and where

- Module **Sales orders (`SALES_ORDERS`)**.
- Permissions: **`sales.create`** builds, saves and sends the order (**Sales** role, **Sales office** and Company admin). Seeing the catalog and the list uses `sales.view`. Creating a new client from step 1 uses **`clients.create_basic`** (Sales role) or `clients.create`. Choosing another delivery address of the client in the app uses `locations.read`.
- **Web**: Accounting → **Sales orders** → **Guided order** button (next to «New order»), route `/sales-orders/new-order`. No menu item of its own.
- **Mobile app**: **Home → Orders** (Spanish «Pedidos»; shown with `sales.create`; a «driver only» person does not see it). Inside: **My orders** and **New order**. App details in [section 6](#6-in-the-mobile-app).

## 2. The four steps (web and app: the same sequence)

Progress bar on top («Step 2 of 4: Products»). **Next** saves the draft of the step being left; **Back** does not save, and the bar only lets you go back to earlier steps. What is changed when going back is saved with the next «Next».

### Step 1 — Client
- **Search client** by number, name or phone; the list shows name, town and number. A prospect client shows marked «Prospect: awaiting approval». The search box takes the focus on entry and the client list opens when it is focused (even with nothing typed); it closes when leaving the field.
- **A single client:** if the company has just one active client, the order brings it chosen by default (only once; if «Change» is tapped it is not chosen again by itself).
- **New client** opens the minimal sign-up (business, DBA, contact, 10-digit phone and extension, phone 2, email, SURI, physical address, optional mailing address, the client's payment method and photos of the merchant registration and the reseller certificate, with camera or file). The client **is born as a prospect**: the order is sent anyway, but it is not dispatched until they approve it. Rules and messages in [02 §1.8](02-clientes-y-contratos.md#18-prospect-client-minimal-sign-up-and-approval-batch-222).
- **Warehouse**: asked only if the company has more than one active.
- **Order data**: **Urgent order** and **Order note** (up to 500 characters); **Payment method** prefilled with the client's and editable (if it changes, the «Payment method changed» mark remains); **Credit term** if the method is Credit. Payment methods in detail in [18 §19](18-ordenes-de-venta.md#19-payment-method-and-consignment-batch-222).
- «Next» creates the draft (`POST /sales-orders` with `step = CLIENT`): still **without products**.

![Step 1: the chosen new client (prospect), urgent order with a note and the prefilled payment method](frontend/img/l228-paso1-cliente.png)
![Step 1 in the dark theme](frontend/img/l228-paso1-cliente-oscuro.png)

![«New client»: the minimal sign-up with physical address, payment method and the document photos](frontend/img/l228-cliente-nuevo.png)

### Step 2 — Products
- Tabs by **brand**; brands without an active product do not show; those without a brand go last («No brand»).
- Above each brand, its **offers in effect** as **cards with a photo**, with **+** and **−**. Each card carries the **offer's name**, until when it is valid («Valid until {date}») or, if the offer has no end date, **«No end date»**, and the price with the **Offer** chip (hovering says «Price from the offer «{name}»»). Only offers that are **published**, active, in effect today and apply to that client show ([47 §4](47-precios-y-ofertas.md#4-offers)). An **offer kit** shows **only as a card** (not in the brand's list) and only while it is in some published offer in effect for that client ([47 §4.4](47-precios-y-ofertas.md#44-offer-kits-and-the-library-batch-231)); an **assembled kit** (with stock) shows in its brand's list like any product.
- If the price to be charged is the **client's negotiated** one because it is lower than the offer's, the card says so: «Offer: $9.00. The client's negotiated price is charged because it is lower.» (with the offer's price). Between the negotiated price and the offer the lowest always wins ([47 §2](47-precios-y-ofertas.md#2-where-the-price-of-a-line-comes-from)).
- Below, the brand's **full list**: a **photo square** of the product (44 px, the height of the row; hovering, focusing or tapping it shows it large), SKU, name, a **quantity** box (whole numbers only; typing it adds the line) and the **price**. **The square shows for every product that has a photo**, not only offers: the one shown is that of the offer or list line, if it has one; otherwise the product's; if it is a kit without a photo, that of its first piece with a photo; if there is none, it stays empty ([47 §5](47-precios-y-ofertas.md#5-photos-of-the-product-of-the-line-and-of-the-kit)). A salesperson with `sales.view` sees the photos even without `inventory.view`. A **regular product that is in an offer** also shows in its brand's list, with the offer price and the **Offer** chip.
- **Price warnings** (small text next to the product, on the cart line with «Offer «{name}»» and in the «Price warnings» box of step 3): «Product … is in two offers at once…» and «Offer … puts … higher than the general list…» (the server sends them in Spanish; exact texts in [47 §2](47-precios-y-ofertas.md#2-where-the-price-of-a-line-comes-from)). They only inform: **the lowest price is charged** and they do not stop continuing or sending the order.
- The **search box** by SKU or name takes the focus when the step opens. With text it searches **all brands** (products and offers); without text it shows the chosen brand.
- **Cart** fixed at the bottom with the count and the subtotal; tapping it shows the lines to fix or remove.
- The salesperson **does not type prices**: lines travel without a price and the server puts the resolved one (chapter [47](47-precios-y-ofertas.md)). A product shown «No price» cannot be ordered, **except by whoever has `sales.price`** (the company Admin): that row shows a **Price** box (with the `$` sign, up to 4 decimals), to the right of the quantity, enabled once a quantity is typed. That price travels as an exception in every save of the order (steps 2, 3 and 4) and is not stored on the product or in any list. Without `sales.price`: `El producto {SKU} no tiene precio de venta: escriba el precio.` (400).
- An offer kit is ordered in whole pieces. On the web, an offer kit that is **«Adjustable when ordering»** lets you change the **quantities** of the pieces of this order (unchanged: the kit price does not change); in the app not yet (they are ordered with the catalog's pieces).
- **Swapping a kit piece (Batch 230; offer kits only since Batch 231).** In the cart, an offer kit shows its pieces (any salesperson with `sales.create` sees them, even without `inventory.view`); each piece with valid substitutes has **«Change»**: the list offers only products of the **exact same category** and the **same sale price**, in the same quantity. Picking one makes the piece say «instead of {SKU}» and it can be **«Undone»**. The kit price does not change, there is no separate permission and «Not substitutable» pieces have no button. Rules and messages in [18 §17.1](18-ordenes-de-venta.md#171-swapping-a-kit-piece-when-ordering-batch-230).
- **Assembled kit (Batch 232).** It is a product with stock: it is ordered like any product (quantity, price, photo), it **shows no pieces, no «Change» and no «Adjustable»** and the kit itself is reserved and dispatched. If there are not enough kits, the same happens as with any product: when sending, the system warns about the shortage (`No alcanza el inventario de {SKU}: pedido {n}, disponible {n}, faltante {n}.`, 409) and, if you decide to leave it, the shortage goes to **backorder** ([18 §6](18-ordenes-de-venta.md#6-requesting-the-order-validate-and-reserve-batch-58)). How the kit is assembled: [06 §8b.2](06-inventario-y-almacen.md#8b2-assembled-kit-how-it-is-used-batch-232).
- «Next» saves with `step = PRODUCTS`: from here the order needs at least one product.

![Step 2: tabs by brand, the offer with its photo and the brand list with the list price](frontend/img/l228-paso2-productos.png)
![Step 2 in the dark theme](frontend/img/l228-paso2-productos-oscuro.png)

![The open cart: the lines to correct or remove, with the subtotal](frontend/img/l228-carrito.png)

### Step 3 — Delivery
- **Delivery** (taken to the client) or **Pickup** (the client picks it up: no shipping charge). With «Delivery», the default destination is the client's **physical address**; another client address (consignee) can be chosen or created (on the web).
- **On consignment**: only if the payment method is Consignment.
- **Required date** (optional). In the app it is typed as `YYYY-MM-DD`.
- **Price warnings**: if any product in the cart has a warning (two offers at once, or an offer more expensive than the general list), the step shows the «Price warnings» box with the SKU and the text, and the note «The lowest price is always charged; these warnings are for your information, they do not stop sending the order.» Those of the saved order are only in the response of saving the draft; when it is reopened the catalog's remain.
- **Summary**: subtotal, IVU, **shipping charge** (with its «Why») and **total**. The server calculates it with the company's and the client's rule ([18 §20](18-ordenes-de-venta.md#20-shipping-charge-batch-222)); changing delivery/pickup or the destination saves the draft at once to recalculate.

![Step 3: delivery to the physical address and the summary with the $12 shipping charge and its reason](frontend/img/l228-paso3-entrega.png)
![Step 3 in the dark theme](frontend/img/l228-paso3-entrega-oscuro.png)

### Step 4 — Authorization and sending
- **Name of who authorizes** (up to 150 characters) and **drawn signature** (with the mouse or the finger; uploaded as a PNG image). They are **required only if the client requires the signature of whoever authorizes the order**; otherwise they are optional.
- **Send order** asks to confirm who is sending:
  - **Web**: a window asks for the **password** (and the verification code, MFA, if the account has one).
  - **App**: asks for **fingerprint or face**; if the phone has none or it is not confirmed, it asks for the **password** (plus the MFA code if needed).
- Then the server **requests** the order: it checks stock and credit and reserves (same as «Request» in [18 §6](18-ordenes-de-venta.md#6-requesting-the-order-validate-and-reserve-batch-58)). If there is a shortage or the credit is exceeded, the usual **shortage dialog** appears: the sending is repeated with backorder, or with the credit authorization (`orders.credit_override`).
- **Final screen**: number `OV-#####`, total, and **New guided order**. If the client is a prospect: «The client is a prospect: the order is reserved but awaits the client's approval and cannot be dispatched yet.»

![Step 4: name of who authorizes and the drawn signature](frontend/img/l228-paso4-firma.png)
![Step 4 in the dark theme](frontend/img/l228-paso4-firma-oscuro.png)

![«Confirm with your password» when sending the order](frontend/img/l228-contrasena.png)

![Final screen for a prospect client: the number, the total and «awaits the client approval»](frontend/img/l228-enviado.png)
![Final screen in the dark theme](frontend/img/l228-enviado-oscuro.png)

**At 360 px** (phone browser): the same steps, with no horizontal page scroll.

![Step 1 at 360 px](frontend/img/l228-movil-paso1.png) ![Step 2 at 360 px](frontend/img/l228-movil-paso2.png) ![Cart at 360 px](frontend/img/l228-movil-carrito.png) ![Step 3 at 360 px](frontend/img/l228-movil-paso3.png) ![Step 4 at 360 px](frontend/img/l228-movil-paso4.png) ![Order sent at 360 px](frontend/img/l228-movil-enviado.png)

**After sending** (full walkthrough): the sales supervisor approves the client in its record ([02 §1.8](02-clientes-y-contratos.md)) and the office dispatches from the order, invoices ([19](19-facturacion.md)) and passes it to QuickBooks ([49](49-quickbooks.md)).

![The sent order: urgent, delivery, 30-day credit, who authorizes with the signature, the kit with its pieces and the shipping charge](frontend/img/l228-orden.png)

**Consignment order**: with the Consignment payment method and «Pickup», no shipping charge; the client negotiated price wins over the general list.

![Step 3 of a consignment order: Pickup, «On consignment» and no shipping charge](frontend/img/l228-consignacion-entrega.png)

## 3. The draft

- Every «Next» leaves the order as a **draft** in Sales orders (`DRAFT`). If the browser closes or the phone dies, the draft is there; there is **no offline mode** (without signal you cannot move on and the app warns you).
- **Web**: after the first save the address becomes `?order=<publicId>`; opening it resumes the draft at the first pending step. An order that is no longer a draft is not edited there: «Order {number} is no longer a draft: open it in Sales orders.»
- **App**: a draft in «My orders» opens in the order where it was left; anything else shows in a read-only sheet.
- The step is **not stored** in the database: it only says what to validate when saving.

## 4. How to use it (API)

| Action | Call | Permission |
|---|---|---|
| Client catalog | `GET /api/v1/sales-orders/catalog?clientPublicId=…` | `sales.view` |
| Create the draft (step 1) | `POST /api/v1/sales-orders` `{ "clientPublicId": "…", "isUrgent": true, "notes": "…", "paymentMethod": "CREDIT", "paymentTerm": "NET30", "step": "CLIENT" }` | `sales.create` |
| Save another step | `PATCH /api/v1/sales-orders/{publicId}` `{ "lines": [ { "productPublicId": "…", "quantity": 2 } ], "step": "PRODUCTS" }`; `{ "deliveryMode": "DELIVERY", "consigneePublicId": "…", "step": "DELIVERY" }`; `{ "authorizedByName": "Ana Pérez", "step": "AUTHORIZATION" }` | `sales.create` |
| Upload the signature (PNG or JPG) and the name | `POST /api/v1/sales-orders/{publicId}/signature` (multipart: `file`, optional `authorizedByName`) | `sales.create` |
| Remove the signature | `POST /api/v1/sales-orders/{publicId}/signature/remove` | `sales.create` |
| Send the order | `POST /api/v1/sales-orders/{publicId}/submit` `{ "authMethod": "PASSWORD", "password": "…", "mfaCode": "123456", "allowBackorder": false }` | `sales.create` |

- The signature is seen with `GET /files/{terms.signatureFilePublicId}` (permission `sales.view`). Uploading another **replaces** the previous one; removing or replacing **unlinks** it, the file is not deleted. `authorizedByName` absent = no change; `""` = remove it.
- `authMethod`: **`PASSWORD`** (the user's own password, and `mfaCode` if they have a confirmed MFA; renews the session's recent verification) or **`BIOMETRIC`** (the app already validated fingerprint or face; the server requires the session to have a recent verification —password and MFA at sign-in or when re-authenticating— within the company window, 30 minutes if none other; otherwise 403 `aal2_required` and the app asks for the password). The fingerprint never skips MFA.
- Sending validates the order **before** the password (draft, products, destination, required signature): a password attempt is not spent on an order that cannot be sent. A wrong password or MFA code answers **400** (not 401) and **counts toward the account lockout**.
- It leaves `terms.submittedWithAuth` (`PASSWORD` or `BIOMETRIC`) and a REAUTH event in the security log (method, order and session).
- `POST /sales-orders/{id}/request` still exists **without** re-authentication (the office with «New order»): the guided order uses `/submit`.

## 5. Validations and messages

| Field / step | Rule | Exact message (Spanish, as sent) | Meaning | HTTP |
|---|---|---|---|---|
| `step` | CLIENT, PRODUCTS, DELIVERY or AUTHORIZATION (case-insensitive) | `El paso del pedido debe ser CLIENT, PRODUCTS, DELIVERY o AUTHORIZATION.` | The order step must be CLIENT, PRODUCTS, DELIVERY or AUTHORIZATION. | 400 (`step`) |
| Products (from PRODUCTS; and when sending) | At least one | `Agregue al menos un producto a la orden.` | Add at least one product to the order. | 400 (`lines`) |
| Delivery (DELIVERY and AUTHORIZATION, and when sending) | «Delivery» needs a destination | `Escoja a dónde se entrega el pedido o márquelo como recogido.` | Choose where the order is delivered or mark it as pickup. | 400 (`consigneePublicId`) |
| Delivery | DELIVERY or PICKUP | `La entrega debe ser DELIVERY (entrega) o PICKUP (recogido).` | Delivery must be DELIVERY or PICKUP. | 400 (`deliveryMode`) |
| Client | Required | `Escoja el cliente que compra.` | Choose the buying client. | 400 (`clientPublicId`) |
| Client | Active | `El cliente está inactivo; no se le puede vender.` | The client is inactive; nothing can be sold to it. | 422 |
| Suspended client | Cannot request | `El cliente está suspendido: no se le puede solicitar una orden.` | The client is suspended: an order cannot be requested for it. | 422 |
| Warehouse | Several and none given | `Indique el almacén: la compañía tiene más de uno.` | Specify the warehouse: the company has more than one. | 400 (`warehousePublicId`) |
| Note | Up to 500 | `Las notas no pueden pasar de 500 caracteres.` | Notes cannot exceed 500 characters. | 400 |
| Payment method, term, consignment | See [18 §19](18-ordenes-de-venta.md#19-payment-method-and-consignment-batch-222) | — | — | 400 |
| Signature | File required | `Dibuje la firma de quien autoriza el pedido.` | Draw the signature of whoever authorizes the order. | 400 (`file`) |
| Signature | PNG or JPG only (by content) | `La firma debe ser una imagen PNG o JPG.` | The signature must be a PNG or JPG image. | 400 (`file`) |
| Signature or name on an order that is no longer a draft | Drafts only | `Solo se puede editar una orden en borrador.` | Only a draft order can be edited. | 422 |
| Name of who authorizes | Up to 150 | `El nombre de quien autoriza no puede pasar de 150 caracteres.` | The name of who authorizes cannot exceed 150 characters. | 400 (`authorizedByName`) |
| Sending: client requires signature | Name and signature | `Este cliente exige el nombre y la firma de quien autoriza el pedido.` | This client requires the name and signature of whoever authorizes the order. | 400 (`signature`) |
| Sending: method | PASSWORD or BIOMETRIC | `La reautenticación debe ser BIOMETRIC o PASSWORD.` | Re-authentication must be BIOMETRIC or PASSWORD. | 400 (`authMethod`) |
| Sending: no method | Required | `Indique cómo se confirmó el envío del pedido: PASSWORD (contraseña) o BIOMETRIC (huella o rostro en la app).` | Say how the sending was confirmed: PASSWORD or BIOMETRIC (fingerprint or face in the app). | 400 (`authMethod`) |
| Sending: no password | Required with PASSWORD | `Escriba su contraseña para enviar el pedido.` | Type your password to send the order. | 400 (`password`) |
| Sending: wrong password | — | `Contraseña incorrecta.` | Wrong password. | 400 (`password`) |
| Sending: wrong MFA code | — | `Código MFA inválido.` | Invalid MFA code. | 400 (`mfaCode`) |
| Sending with BIOMETRIC without a recent verification | — | `Esta acción requiere reautenticación reciente (AAL2).` | This action requires recent re-authentication (AAL2). | 403 (`aal2_required`) |
| Sending an order that is not a draft | — | `Solo se puede solicitar una orden en borrador.` | Only a draft order can be requested. | 422 |
| Stock shortage | See [18 §8](18-ordenes-de-venta.md#8-new-messages-batch-58) | `No alcanza el inventario de {SKU}: pedido {n}, disponible {n}, faltante {n}.` | Not enough inventory of {SKU}: ordered {n}, available {n}, short {n}. | 409 |
| Credit exceeded | See [18 §8](18-ordenes-de-venta.md#8-new-messages-batch-58) | `El cliente excede su límite de crédito: límite {n}, en curso {n}, esta orden {n}.` | The client exceeds its credit limit: limit {n}, outstanding {n}, this order {n}. | 422 |
| Offer kit no longer in an offer in effect | Only sold while it is in some published, active offer in effect that applies to the client | `El kit de oferta {SKU} no está en ninguna oferta publicada y vigente para este cliente: no se puede vender.` ([47 §7](47-precios-y-ofertas.md#7-validations-and-messages)) | The offer kit {SKU} is in no published offer in effect for this client: it cannot be sold. | 422 |
| Swapping a kit piece | Offer kit only; same category, same price, same quantity, substitutable piece | See [18 §17.1](18-ordenes-de-venta.md#171-swapping-a-kit-piece-when-ordering-batch-230) (e.g. `GXL no tiene el mismo precio de venta que GM: solo se sustituye por un producto del mismo precio.`) | GXL does not have the same sale price as GM: it is only replaced by a product of the same price. | 400 |
| Order does not exist | — | `Orden de venta no encontrada.` | Sales order not found. | 404 |
| Without the permission | — | `Falta el permiso 'sales.create'.` | Permission 'sales.create' is missing. | 403 |

Messages of a new client's sign-up: [02 §1.8](02-clientes-y-contratos.md#18-prospect-client-minimal-sign-up-and-approval-batch-222). Price and offer messages: [47 §7](47-precios-y-ofertas.md#7-validations-and-messages). Messages of swapping pieces: [18 §17.1](18-ordenes-de-venta.md#171-swapping-a-kit-piece-when-ordering-batch-230). On screen, the notices next to the field repeat these same texts.

## 6. In the mobile app

- **Orders** (Spanish «Pedidos») shows on Home only with `sales.create`. With several companies, the screen uses the first one where the person has `sales.create` (or the one chosen with the company buttons, only before the first save).
- **My orders**: status (All, Drafts, Requested, Partial, Shipped, Cancelled), search by number, client or product, and badges (Urgent, Client pending approval, On consignment, Pickup, Backordered). Seeing the list also requires `sales.view`; without it the screen says so and still lets you place the new order. The list shows the orders whose requester matches the session's name or email, over the 200 newest of the filter; two people with the same name would appear together.
- **New order**: the four steps of section 2, with the signature made with the finger in the box («Sign again» if one is already saved) and photos with camera or gallery in the new-client sign-up (if a photo fails, the client is still created: «The client was created, but a photo could not be uploaded. Upload it later from the client record on the web.»).
- **Send order**: fingerprint or face («Confirm sending the order»); the password is asked if the phone has no fingerprint («This phone has no fingerprint or face enrolled: enter your password to send the order.»), if it was not confirmed («The fingerprint or face was not confirmed: enter your password to send the order.») or if the session verification expired («For security your password is needed (the session verification expired).»).
- **Without signal**: there is no offline mode. Without a connection you cannot move on («Could not load. A connection is needed.»); what is already saved stays as a draft on the server.
- **Batches 229 to 232 in the app.** The mobile app was not changed in those batches: it still reads the offers by brand and the price the server resolves (what is charged is correct), but it **does not show** the offer's name, «No end date», the price warnings or «Change» on kit pieces; those are web only. How the offer cards look on a phone with the new data was not tested.
- **What the app does not do yet** (done on the web): adjust the pieces of an adjustable kit, create a new delivery address, choose the town from a list (it is typed) and modify an order already requested.
- The app's own text is informal; the messages that repeat the server's are formal, as in the Spanish messages of section 5.

## 7. Statuses and transitions of the order

The guided order uses the sales order statuses (`SalesOrderStatuses`) with the status engine.

| From → to | Who | What it validates | Effects | What stays blocked afterwards |
|---|---|---|---|---|
| (new) → Draft | `sales.create` | Active client, warehouse, note ≤ 500, coherent payment method | Number `OV-#####`; prices resolved; shipping calculated | — |
| Draft → Draft (each step) | `sales.create` | What belongs to the step (products, destination) | Prices, tax and shipping are recalculated | — |
| Draft → Requested («Send order») | `sales.create` + password or fingerprint | Complete order, required signature, stock, credit | Reserves inventory; `submittedWithAuth`; «Urgent order» notice if urgent; prospect → `awaitingClientApproval` | It can no longer be edited nor the signature changed («Solo se puede editar una orden en borrador.»); only lines are modified ([18 §7](18-ordenes-de-venta.md#7-cancelling-and-modifying-a-requested-order-batch-58)) |
| Draft or Requested → Cancelled | `sales.cancel` | That nothing has been dispatched | Releases what was reserved | Everything |
| Requested → Partially dispatched / Dispatched | `warehouse.pick` | That the client is not «awaiting approval» | Lowers inventory | With the client not approved: `La orden espera la aprobación del cliente nuevo: no se puede despachar hasta que lo aprueben.` (422) |

## 8. Frequently asked questions of the module

- **I do not see «Guided order» or «Orders».** `sales.create` and the Sales orders module turned on are needed.
- **«Send order» asks for my password again.** On purpose: it is the salesperson's signature. With the fingerprint (app) it only asks if the session verification expired.
- **I typed the wrong password several times.** It counts toward the account lockout, as when signing in.
- **The client is new and the order «awaits approval».** It is reserved, not dispatched. Whoever has `sales.approve_client` approves it on the client's record.
- **«Change» does not show on a kit piece.** The piece is marked «Not substitutable», has no category or sale price, or there is no other active product of the exact same category and same sale price (or it is already another piece of the kit). Whoever manages the products (category and price) or the kit (the mark) fixes it.
- **The offer card says the negotiated price is charged.** The client has a list price lower than the offer's: between the two the lowest wins.
- **A price warning showed in the cart.** Two offers at once, or an offer more expensive than the general list. It is informational: the lowest price is charged and the order can be sent.
- **The offer's kit no longer shows.** It is no longer in any published offer in effect for that client: its offer ended, was deactivated or went back to draft ([47 §4.4](47-precios-y-ofertas.md#44-offer-kits-and-the-library-batch-231)); if extended, it returns. Ordering it outside its validity gives the 422 error of [47 §7](47-precios-y-ofertas.md#7-validations-and-messages).
- **I ordered an assembled kit and it says there is not enough.** There are not enough assembled kits (stock minus reserved). Whoever has `inventory.adjust` can assemble more from the kit record (“Assemblies” tab); otherwise the shortage is left in backorder until there are kits.
- **The assembled kit does not let me change pieces.** That is normal: an assembled kit already has its pieces inside. Changing pieces only exists on offer kits.
- **A draft was left unfinished.** It is in Sales orders (or «My orders»); open it to continue or cancel it.

The messages are in the [FAQ](faq.md#batches-223-and-226--guided-order-web-and-mobile-app), in [Batch 229](faq.md#batch-229--offers-as-a-separate-entity-offer-kits-and-the-product-photo), in [Batch 230](faq.md#batch-230--product-dimensions-and-weight-not-substitutable-and-swapping-kit-pieces), in [Batch 231](faq.md#batch-231--offer-kit-with-library) and in [Batch 232](faq.md#batch-232--assembled-kit-with-inventory-assemble-and-disassemble).

## 9. For support

- There are no new tables for the step: it is the same `SalesOrder`. The columns `AuthorizedByName`, `SignatureFileId` and `SubmittedWithAuth` were added by Batch 222; the signature is a `StoredFile` of class SIGNATURE (owner `SALES_ORDER`), encrypted.
- The REAUTH event with `stage = sales_submit` (method, order number, session) says who sent and from which session or phone (System → Audit and security).
- Web: `web-app/src/features/sales/wizard/*`. App: `app-movil/src/app/(auth)/NewSalesOrder` and `SalesOrders`. The cart and step rules are **copied** in the app (`salesOrderRules.ts`): a change on the web must be repeated there.
