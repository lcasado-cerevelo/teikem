# 45 — Mobile app (driver and staff)

> Batch 213 (2026-10-24). The driver app (chapter 04, Batches 203-212) is now **the Teikem mobile app**: the same app for drivers and staff. What each person sees is decided by their permissions.

## 1. Who signs in
- Whoever has the **“Mobile app: use it”** permission (`mobile.use`) **or is an active driver** in some company signs in. With neither of the two: “Your user has no access to the mobile app in any company.” (403).
- `mobile.use` comes with the **Driver** and **Dispatcher** templates (and TenantAdmin). The administrator gives it to whomever they want in System → Roles.
- **One session, all their companies**: just like the driver, the person signs in once and the app works with every company where they have access (a company that requires MFA requires it at sign-in).
- **“Driver (app only)” user**: sees only the driver's things, even if their role had other permissions. An employee who also drives is a normal user with driver permissions: they see both.

## 2. What the staff sees (v1)
- **Home**: “**Needs your attention**” (the same notices as the web Pulse, per company, with `pulse.attention`) and “**Day pulse**” (the indicators they can see, with `analytics.view`). If they are also a driver, below it “My driver work” with their usual cards.
- **Messages** (chat with photos, order cards, suggestions), **Notices** and **Profile**: the same as the driver (chapter 15 §8-9 and chapter 04).
- **Manifests**: drivers only.
- **Orders** (Batch 214): see §2a.
- **Today's routes** and **street assignment** (Batch 215): see §2b and §2c.
- **Scan** (Batch 216): scan station for the Android with a trigger, **with offline work**: see §2d.
- **Clients, Consignees and Warehouse (lookup)** (Batch 217): see §2e. Each one appears on the home screen only if the person has the permission.

## 2a. Orders (Batch 214)
- **Who**: the “Orders” entry appears with `orders.view` in some company; completing also requires `orders.deliver` **in that company**.
- **Search**: type or **scan** the label. The exact match is searched first (waybill, order number, packing, invoice, package number; the list says “Exact match”); if there is none, free-text search (up to 30). With more than one company, a selector at the top. The search is live: **with no signal there are no results**.
- **Record**: the same as the driver's (map, consignee, phone as a link, “Talk about this shipment”, status, order, waybill, pieces, COD and notes) plus **packing, invoice, service type, current route and its driver**, the **proof of delivery** (who received, when, how many meters away, with signature, photos) and the **attempts**.
- **Actions** (Actions tab): according to the status and type of the order:
  | Order | Buttons |
  |---|---|
  | In transit (delivery) | Order exception, Complete order |
  | Failed | Complete order (retry the delivery) |
  | Submitted without route or on a pickup route | Not picked up, Pick up |
  | Other (delivered, cancelled, assigned…) | “This order is “X”: there is nothing to complete from here.” |
  Without `orders.deliver` in that company: “You do not have permission to complete orders in this company.”
- **What is left**: the same as when the driver delivers (proof of delivery with origin DRIVER_APP, signature and photos, GPS and distance to the address, collections, attempt), with the history **in the person's name** (“Delivered from the app by Ana Oficina.”). If the order was on a route, its stop is closed and the driver stops seeing it.
- **“Credit the driver”** (switch, **yes by default**): yes = the attempt and the collection count toward the settlement of the route's driver; no = they remain without driver. The audit always says who closed it.
- **No signal**: the staff does **not** work offline. Searching, opening the record and completing require a connection; if it fails, the screen says so and nothing is saved. Only the driver's things (their manifests and shipments) are downloaded and closed offline.
- Endpoints: `GET /api/v1/mobile/orders?q=`, `GET /api/v1/mobile/orders/{id}`, `GET /api/v1/mobile/failure-reasons`, `POST /api/v1/mobile/orders/{id}/deliver|fail|pickup`.

## 2b. Today's routes (Batch 215)
- **Who**: `trips.view` in some company (transport module). “Today's routes” entry on the home screen.
- List of the day's routes (without the cancelled ones) with driver, vehicle, status, type (delivery or pickup) and number of stops; pull down to refresh. Tapping a route shows its **stops in order** and the driver's **last location** (“N min ago”); tapping a stop opens the order record.
- Live: with no signal there is no list.

## 2c. Street assignment (Batch 215)
- **Who**: `trips.streetassign` in that company (its own permission, different from `trips.dispatch`; the administrator gives it in System → Roles). Entered through **Actions → Assign to a route** in the order record.
- **What is shown**: the order (type, status and current route) and the **open routes of today of the same type** (planned, dispatched or in progress; an order in its initial stage is a pickup one and the rest are delivery ones), the closest to the address first, with the straight-line distance from the driver's last location (only if the route already departed and sent a location).
- **Actions** (each one asks for confirmation and leaves a notice to the driver in their inbox, which reaches their phone):
  | Action | Effect |
  |---|---|
  | Send to this route (the order has no route) | Goes in at the end of the route. Dispatched route: the order becomes “Assigned”. Route in progress: “In transit”. Pickup route: the order does not change. |
  | Move to this route (the order is on another one) | Leaves the previous one (goes back to “Unassigned” if it was a delivery) and enters the new one; both drivers are notified. |
  | Remove from the route | Goes back to “Unassigned” (delivery) and the driver is notified. |
  | Quick route (driver + vehicle) | Creates today's route with the order and dispatches it: the driver receives “New manifest” and accepts it in their app. If it fails, the route is cancelled. |
- **Not possible with no signal**: everything is applied instantly on the server.
- Endpoints: `GET /api/v1/mobile/street/orders/{id}/targets`, `POST /api/v1/mobile/street/assign|unassign|quick-route`.
- **What the driver sees**: a notice “New stop on {route}” or “Stop removed from {route}”. The new stop appears in their manifest when refreshing.

## 2d. Scan (Batch 216) — with offline work
- **Who**: `trips.scan` in some company (transport module). “Scan” entry on the home screen. **Return** also requires `orders.return`.
- **Two steps before scanning** (decided by Luis). The screen does not let you scan anything (the field and the camera do not even appear) until they are completed, and they are repeated every time you enter:
  1. **What are you going to scan?** The process: Pick up, Arrived, Route, Depart or Return. There is no default.
  2. **How are you going to scan it?** **Everything** (without choosing anything else: “I am going to scan”), **By route**, **By city** or **By driver**. With route, city or driver the list of **all** the options appears with a **typed search box** and **multiple selection** (several can be checked). At least one must be checked.
  Then **Start scanning**. At the top stays the summary (“Route · By driver: Juan +1”) and **Change** goes back to the steps.
- **What for**: it is the scan station of chapter 05 §7b, on the phone. The **number of each piece** (the one on the label) is scanned with the trigger reader —which types like a keyboard— or with the camera. Each action takes the shipment to its status:
  | Action | Status | Notes |
  |---|---|---|
  | Pick up | Pickup | |
  | Arrived | Unassigned (In warehouse if it is a pick up) | |
  | Route | Assigned | also puts it on a route: the one you choose at the top, or the one of its zone |
  | Depart | In transit | |
  | Return | Returned | only with signal and `orders.return` |
  The first piece of an action moves the shipment (it skips the statuses in between); the following ones only count (“4 of 5 pieces”). A final status is not touched.
- **Scanning is by PIECE, not by shipment** (Luis): the one that says Yes, Ok or No is the piece. The shipment, packing or invoice number **is not scanned** ("Piece not found."). The same applies to the web station, which uses the same service (and also has “Everything / By route / By city / By driver”; chapter 05 §7b).
- **What the screen says** (phone voice, vibration and color):
  | What happens | Voice | Color |
  |---|---|---|
  | The piece is found and is **new** for that action (whether it moves the shipment or not: if another piece had already moved it, it still says Yes and is counted) | **Yes** | green |
  | **That same piece** had already been scanned with that action (the scan is repeated) | **Ok** | amber |
  | The piece is not found; the shipment is **cancelled or in a final status** (delivered…); it is a pick up and the action does not apply to it; or **it is not among what was chosen** (another route, another city or another driver, even if it exists in the system) | **No** | red |
  | The shipment is already **further ahead** (and not just already in that same status) than the process you are scanning (e.g. you scan Route and it is already In transit) | **Attention**, and the system **stops** and says what is going on and what is being scanned; **“Cancel”** does nothing; **“Move the status back”** makes the shipment **go back** to the status of what you are scanning (scanning backwards is the way to move a status back); if that status is earlier than Assigned, the shipment **leaves its route** (and the driver is notified), and the scans of later actions of its pieces are forgotten so it can move forward again | orange |
- **The magnifier** (magnifier icon next to the field): type the number of a piece and it shows **whatever is in the database**: whether it is cancelled, in a final status, written off, lost, “To verify” or in a claim; which route and driver it is on and which actions it was scanned with. It is useful when a scan said “No”: there you see why. **It does not count as a scan.** With signal it queries the full database; **with no signal it only shows what was downloaded** (shipments not completed) and says so.
- **By route / city / driver**: only **pieces** of what was checked are accepted. The route of a shipment is its current route; its driver is the one of that route, of any type (delivery or pickup) and state (planned, dispatched or in progress), so by driver you get **all the packages assigned to that driver** (a shipment without route, or that already left it, has no driver); its city is the one of the delivery stop. With **Route and ONE single route checked**, free shipments are put on that route; with several routes checked, only those already on one of them are accepted.
- **The voice**: it uses the phone's voice (text to speech). If “No” is not heard clearly on the Android, a custom audio can be set for it.
- **No signal**: when opening the screen (and with the **Sync** button) the phone **downloads all the shipments not completed, up to 5000, the most recent ones**, with their pieces. With that it answers each reading **instantly**, with no signal, with color, vibration and voice **Yes / No / Ok** (“Voice” switch). What counts stays in the queue (“To send: N”) and goes out by itself when the signal returns.
- **Piece that is not in what was downloaded**: with signal the server is asked; with no signal, “Piece not found.” (applies to what was left out of the 5000 or is newer than the download).
- **With issues**: if the server does not accept a scan already done on the phone (for example “there is no open route to receive the shipment” when Routing, or the shipment was already delivered), it appears under “With issues: N” with the server's message; it is removed with “Remove”.
- **Close count**: with signal, on the last scanned shipment; it shows which pieces will remain “To verify” and asks for confirmation.
- **Time**: the shipment history marks the time it **synced**, not the time of the scan.
- Endpoints: `GET /api/v1/mobile/scan/snapshot?skip=&take=`, `GET /api/v1/mobile/scan/routes`, and the usual `POST /api/v1/scan/action` (now with optional `tripPublicId` to Route to a chosen route) and `POST /api/v1/scan/close-count`.

## 2e. Clients, Consignees and Warehouse lookup (Batch 217)
Three **read-only** screens, **with signal** (with no signal they say “Could not look it up. A signal is needed.”). The companies with permission appear as cards at the top; one is looked up at a time. All phones, e-mails and addresses are **tappable links** (call, write or open the map with “directions”).

| Screen | Permission and module | What it does |
|---|---|---|
| **Clients** | `clients.read` · Catalog | Search by name or code; the record shows name, code, legal name, status, physical and pickup address (with map), phones/e-mails and the contacts with their position. |
| **Consignees** | `locations.read` · Catalog | Search by name, address or city; the record shows the client, the address (with map), the contact, their phones/e-mails and the **access and delivery notes**. |
| **Warehouse (lookup)** | `inventory.view` · Lot and serial inventory | **By product**: type name, SKU or **scan the barcode** (if there is no exact code it searches by text) and see stock on hand and available, by warehouse and by bin (and lot). **By bin**: search the bin code and see which products it holds. |

- **What is NOT shown, by Luis's decision**: credit limit, tax ID, payment terms, contracts and billing of the client; costs, prices and inventory value. The app discards those fields even if the server sends them. There is no accounting or billing in the app.
- **It moves nothing**: nothing is received, put away, adjusted or transferred from here (that belongs to the warehouse app).
- A “driver only” person does not see these entries. Whoever has only one of the permissions sees only that entry.
- Endpoints (the web ones, no new server): `GET /api/v1/clients?search=`, `GET /api/v1/clients/{id}`, `GET /api/v1/locations?search=`, `GET /api/v1/locations/{id}`, `GET /api/v1/contacts/LOCATION/{id}`, `GET /api/v1/products?search=`, `GET /api/v1/products/by-barcode/{código}`, `GET /api/v1/inventory/balances`, `GET /api/v1/warehouses/bins/search` and `GET /api/v1/warehouses/{id}/bin-products`.

## 3. Push notifications
- The app registers the phone in `POST /api/v1/mobile/device` for each company. A driver is recorded in their profile (`DriverDevice`); staff, in their user (`UserPushDevice`). Chat messages and notices arrive through either of the two.

## 4. Exact messages
| Situation | Message | HTTP |
|---|---|---|
| Without `mobile.use` and not a driver | `Your user has no access to the mobile app in any company.` | 403 |
| Session that is not from the app | `This session is not from the mobile app.` | 403 |
| Phone registration without token / invalid platform | `The phone token is missing.` / `The platform must be ANDROID or IOS.` | 400 |
| Searching orders without text | `Type or scan an order, waybill, packing or invoice number.` | 400 |
| Order that does not exist (record or complete) | `Order not found.` | 404 |
| Delivering a pickup order / picking up a delivery order | `This shipment is on a pickup route: it is picked up, not delivered.` / `This shipment is on a delivery route: it is delivered, not picked up.` | 422 |
| Picking up an order that is no longer pending | `Shipment {order} is no longer pending pickup.` | 422 |
| Already delivered from the web | `Shipment {order} already has its delivery recorded.` | 409 |
| Routing to a chosen route that is no longer open | `Route {code} is no longer open; it remains unassigned.` | 200 (`NO_OPEN_ROUTE`) |
| Routing to a chosen route that does not exist | `The chosen route no longer exists; it remains unassigned.` | 200 (`NO_OPEN_ROUTE`) |
| Scanning a piece that is not in what was downloaded, with no signal | `The piece was not found in what was downloaded and there is no signal to ask.` | (on the phone) |
| Assigning without order / without route / without driver / without vehicle | `Indicate the order.` / `Indicate the route.` / `Choose the driver.` / `Indicate the vehicle.` | 400 |
| Sending a delivery order to a pickup route (or the other way around) | `Order {order} is a delivery: choose a delivery route.` / `Order {order} is a pickup: choose a pickup route.` | 422 |
| Moving a delivered, failed, cancelled… order | `Order {order} is “{status}”: it can no longer be moved between routes.` | 422 |
| The order is already on that route | `The shipment is already on this route.` | 409 |
| Removing an order that is not on any route | `Order {order} is not on any route.` | 422 |
| Closed, cancelled or draft route without editing | `Route {code} was already dispatched; it cannot be edited or deleted.` (the text changes according to its status) | 422 |
| Quick route with deliveries and pickups together | `Deliveries and pickups cannot be combined on the same route.` | 422 |
| Order not eligible for the route (status, no pending stop…) | `There are orders that cannot be assigned to the route.` with the reason per order | 422 |
| The other validations of deliver / mark as not delivered | The driver's (chapter 04): name of who received, signature and photo required by the client, reason, note, photos | 400 |
