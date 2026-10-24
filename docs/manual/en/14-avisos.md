# 14 — Notices (notifications)

Batch 51 (P2.1, 2026-10-14). **Notices** tell the right people that something happened (“a receipt with a variance arrived”, “a
damage was reported”…) through the channel each company chooses. This chapter describes the **engine**: rules, channels, queue, log and
each user's inbox. The screen to build the rules and the bell of the web app arrive in Batch 52; the notices of each module
(receipts, damage, counts…) are connected in separate batches, one per module. Today there is a **test event** to try everything out.

## 1. How it works

1. A module **records** the notice in a queue **within the same operation** (the same transaction). If the operation is rolled back, nothing
   is notified; if the notice fails afterwards, the operation is **never** undone.
2. A background service (every 10 seconds) picks up what is pending, looks for the **active rules** of that company for that event, builds the
   recipients and the text of each channel and sends it.
3. Each send is recorded in the **log** (event × rule × channel × recipient). The same thing is never notified twice: a retry only
   repeats what failed.

## 2. Permissions and where

- **Rules, test send and log**: permission **`notifications.manage`** (“Manage notification rules”; it comes with the **Company
  admin** role and was added to the administrator roles that already existed). Module **System** (always on; notices are not a module).
- **Inbox** (the bell): no special permission; each user sees and touches **only their own**.
- API: `/api/v1/notifications/*` (events, channels, rules, test, log) and `/api/v1/inbox/*` (inbox). There is no DELETE: a rule is turned off.

## 3. The events

Each module defines its own in code (with their variables and default texts). Today: **`TEST` — Test notice**, with the variables
`{message}` (message) and `{user}` (who triggered it). It is triggered with `POST /api/v1/notifications/test-event` and is received by the
**active** rules of that event. An **optional** event can be muted in each user's inbox.

### 3.1 Sales order events (Batch 66)

Module **Sales orders** (the events always exist; they are only triggered if the company uses the module). **No rule comes preset**: the administrator
creates them in System → Notification rules, choosing whom and through which channel. All can be muted in each user's inbox **except the credit one**.

| Event (code) | When it is triggered | Variables |
|---|---|---|
| **Sales order requested** (`SALES_ORDER_REQUESTED`) | When an order is requested (validated and reserved). | `{orderNumber}` `{client}` `{warehouse}` `{lineCount}` `{detail}` (if something was left on backorder) |
| **Sales order shipped** (`SALES_ORDER_SHIPPED`) | When the order is **fully** dispatched (a partial dispatch does not notify). | `{orderNumber}` `{client}` `{warehouse}` `{shipped}` (units) |
| **Sales order cancelled** (`SALES_ORDER_CANCELLED`) | When it is cancelled, and also when it is cancelled by itself because all its lines were closed with a shortage without dispatching anything. | `{orderNumber}` `{client}` `{warehouse}` `{reason}` |
| **Sales order needs credit authorization** (`SALES_CREDIT_OVERRIDE_NEEDED`) | When someone tries to request an order that exceeds the client's credit and does not authorize it. Once per situation (the same repeated attempt does not notify again; if the limit, the open amount or the amount changes, it does). | `{orderNumber}` `{client}` `{warehouse}` `{user}` `{limit}` `{exposure}` `{amount}` |
| **Backorder that can now be fulfilled** (`SALES_BACKORDER_AVAILABLE`) | When there is inventory available to sell that is enough to fulfill lines with backorder. Once per order and situation (if more comes in, or what can be fulfilled changes, it notifies again). | `{orderNumber}` `{client}` `{warehouse}` `{lines}` (e.g. “MED × 3, VIT × 2”) `{lineCount}` |

The first four go out **in the same operation** that causes them (if the operation is rolled back, nothing is notified). The backorder one is detected by a scheduled job
(`SALES_BACKORDER_SCAN`, [chapter 13](13-trabajos-programados.md)) every 10 minutes: it covers **any way** inventory comes in (receipt, adjustment,
transfer, releasing quarantine, cancelling another order), without touching those modules.

### 3.2 Inventory events (Batch 141, 2026-10-20)

Module **Lot and serial inventory** (`WMS_LOTSERIAL`). They are published by the scheduled job `INVENTORY_ALERT_SCAN` ([chapter 13](13-trabajos-programados.md)), **every
day at 6:00** (company time; “Run now” repeats it). **No rule comes preset**: the administrator creates them in System → Notification rules (whom and through which
channel). All can be muted in each user's inbox. Each event goes out as **a single notice with the complete set** (not one per lot); the variables `{count}` (how many)
and `{items}` (one line for each, up to 10 and “… +N”) are in all of them.

| Event (code) | What it notifies | Extra variables and format of `{items}` |
|---|---|---|
| **Lots about to expire** (`INVENTORY_LOT_EXPIRING`) | Lots **with stock** that expire today or within the next N days (N = company setting, 30 by default). | `{days}`; `product · lot · expiration · quantity · warehouse` |
| **Expired lots with stock** (`INVENTORY_LOT_EXPIRED`) | Lots whose expiration is **before today** and still have stock. | `product · lot · expiration · quantity · warehouse` |
| **Products below minimum** (`INVENTORY_STOCK_LOW`) | **Active** products with a minimum (`minQty`) whose total available (on hand − reserved, all warehouses) is less than the minimum. | `product · available · minimum` |
| **Products with no stock** (`INVENTORY_STOCK_OUT`) | Active products that **did have movements** and today have nothing on hand (a newly created product that was never received does not count). | `product` |
| **Products with no movement** (`INVENTORY_NO_MOVEMENT`) | Active products **with stock** whose last movement in the movement ledger is N days old or more (N = setting, 90 by default). | `{days}`; `product · days · on hand` |
| **Products with stock and no cost** (`INVENTORY_NO_COST`) | Active products **with stock** and no purchase cost (empty or zero): without a cost the inventory cannot be valued. | `product · on hand` |

**The same thing is not repeated:** the non-repetition key is *event + week + fingerprint of the set*. If the set did not change during the week, it does not notify again; if a new lot enters the
window or another product drops below minimum, a new notice goes out with the complete set; and while the problem stays alive there is a **weekly reminder**.

Settings (web: **Tenant settings → Operations → “Inventory alerts”**, `admin.tenant`; API: `PUT /api/v1/tenant/settings`):
`inventoryExpiryWarnDays` (1 to 365, 30) and `inventoryNoMovementDays` (1 to 3650, 90).

| Case | Exact message | HTTP |
|---|---|---|
| `inventoryExpiryWarnDays` outside 1 to 365 | `The expiry warning days must be between 1 and 365.` | 400 (field `inventoryExpiryWarnDays`) |
| `inventoryNoMovementDays` outside 1 to 3650 | `The days without movement must be between 1 and 3650.` | 400 (field `inventoryNoMovementDays`) |

**Adding another inventory notice later** (e.g. excess inventory, count with variances): a code in `InventoryEventCodes`, its definition in `InventoryEventProvider` and its
detection in `InventoryAlertService`; the notification engine, the rules and the inbox do not change.

### 3.2 bis Fleet events (Batch 168, 2026-10-21)

Module **Catalogs** (where vehicles, drivers and maintenance live). They are published by the scheduled job `FLEET_ALERT_SCAN` ([chapter 13](13-trabajos-programados.md)), **every day at 6:15** (company time; “Run now” repeats it). Same as the inventory ones: **no rule comes preset** (the administrator creates them in System → Notification rules), all can be muted in the inbox, and each event goes out as **a single notice with the complete set** (`{count}` and `{items}`, up to 10 lines and “… +N”), without repeating in the same week while the set does not change (with a weekly reminder if the problem stays alive).

| Event (code) | What it notifies | Extra variables and format of `{items}` |
|---|---|---|
| **Fleet documents about to expire** (`FLEET_DOC_EXPIRING`) | Documents of **vehicles** (registration, insurance, inspection, permit) and of **drivers** (licenses and certifications) that expire today or within the next N days (N = company setting, 30 by default). | `{days}`; `V code · document · expiration · days left` (vehicle) or `C code · …` (driver) |
| **Fleet documents expired** (`FLEET_DOC_EXPIRED`) | Documents whose expiration is **before today**. | `V/C code · document · expiration · days overdue` |
| **Maintenance due soon** (`FLEET_MAINTENANCE_DUE_SOON`) | Preventive maintenance programs “Due soon” (10 % or less of the interval remains). | `vehicle · program · N km left · N days left` (only what the program uses) |
| **Maintenance overdue** (`FLEET_MAINTENANCE_OVERDUE`) | “Overdue” programs (what remains is 0 or negative). | `vehicle · program · N km past · N days past` |
| **Work orders open too long** (`FLEET_WORKORDER_STALE`, Batch 179) | Maintenance orders in OPEN or IN_PROGRESS created **more than 14 days** ago. | `order · vehicle · open for N days` |
| **Fuel out of range** (`FLEET_FUEL_OUT_OF_RANGE`, Batch 179) | Vehicles whose **last fill-up** (within the last 30 days) yields a km/L that deviates **more than 30 %** from the average of the 5 previous fill-ups with mileage (at least 3 are needed). | `vehicle · km/L · average · difference %` |
| **Vehicles without a trip** (`FLEET_VEHICLE_IDLE`, Batch 179) | **Active** vehicles with no trip (not cancelled) in **more than 14 days**; those that never had a trip count from their creation. | `vehicle · no trip since date (or never had a trip) · days` |
| **Drivers without a trip** (`FLEET_DRIVER_IDLE`, Batch 179) | The same for active drivers. | `code name · no trip since date · days` |

**Thresholds of the new notices (Batch 179).** They are fixed for now —14 days of an open order, 14 days without a trip, 30 % fuel deviation, history of 3 to 5 fill-ups, 30-day window—; they cannot be adjusted per company yet (the document days can, above). Same as the others: one notice per event with the set, which is not repeated if the set is the same within the week, optional (they can be muted) and with no seeded rules: the administrator sets up who receives them in System → Notification rules.

**What counts:** only vehicles and drivers that are **active and not deactivated**; of each document type only the **current** one (if it was already renewed, the old one does not notify); a document **without an expiration date does not notify**. Maintenance uses exactly the same calculation as the “Preventive maintenance” panel (Fleet → Maintenance).

Setting (web: **Tenant settings → Operations → “Fleet alerts”**, `admin.tenant`; API: `PUT /api/v1/tenant/settings`): `fleetDocWarnDays` (1 to 365, 30).

| Case | Exact message | HTTP |
|---|---|---|
| `fleetDocWarnDays` outside 1 to 365 | `The fleet document warning days must be between 1 and 365.` | 400 (field `fleetDocWarnDays`) |

The notices link to Fleet → **Expiring documents** or **Maintenance**. More fleet notices (work order open too long, fuel out of range, vehicle or driver without a route for N days) are left for when there is real data (plan G2).

### 3.3 Exit notice events (Batch 143)

Module **Lot and serial inventory**. All optional (muted in the inbox); **no rule comes preset**. Common variables: `{number}` `{client}` `{warehouse}`. In arrival notices `{number}` is the notice number (`LLE-00012`, since Batch 170; before it was `#12`).

| Event (code) | When it is triggered | Extra variables |
|---|---|---|
| **Exit notice requested** (`EXIT_NOTICE_REQUESTED`) | When the notice is created (in the same operation). | `{lineCount}` `{detail}` (if something was left pending for lack of stock) |
| **Exit notice backorder now reserved** (`EXIT_NOTICE_BACKORDER_RESERVED`) | When the job `EXIT_NOTICE_BACKORDER_SCAN` reserves pending quantity. Once per situation. | `{lines}` `{units}` |
| **Exit notice shipped** (`EXIT_NOTICE_SHIPPED`) | When it is **fully** dispatched (a partial dispatch does not notify). | `{shipped}` |
| **Exit notice cancelled** (`EXIT_NOTICE_CANCELLED`) | When it is cancelled, and also when it is cancelled by itself because all its lines were closed with a shortage without dispatching anything. | `{reason}` |
| **Arrival notice requested** (`ASN_REQUESTED`) | When an arrival notice of a client is created (by staff or from the portal). | `{number}`, `{reference}`, `{client}`, `{warehouse}`, `{expected}`, `{lineCount}` |
| **Arrival notice updated** (`ASN_UPDATED`) | When an expected arrival notice is edited (warehouse, reference, date or lines). | the same |
| **Arrival notice received** (`ASN_RECEIVED`) | When the receipt against that notice is confirmed. | `{number}`, `{reference}`, `{client}`, `{warehouse}`, `{expected}` |
| **Arrival notice cancelled** (`ASN_CANCELLED`) | When the notice is cancelled. | the same |

## 4. The rules

A rule = **one event** + **one name** (unique in the company, up to 100 characters) + **channels** + **recipients**.

- **Channels**: `EMAIL`, `SMS`, `APP` (the bell) and `PUSH`. All four always exist in the rule and **are born off** with the event's default
  texts. Each one carries **subject and text in Spanish and English** with the event's `{variables}` (a variable the event does not
  have is rejected; one missing at send time is left empty).
- **Unconfigured channels**: **SMS** and **PUSH** do not have a provider in Teikem yet and **cannot be turned on**. **EMAIL** works with the
  e-mail Teikem already uses (Brevo); if that service is not configured on the server, it cannot be turned on either. An **APP** channel is always
  available.
- **Recipients**: a **role** of the company (all its active users receive it), an active **user** of the company, a **loose e-mail** or a
  **loose phone** (it is saved; without SMS it is not used). No repeats: if someone is there by role and by e-mail, they receive one.
- **Activating** requires at least one recipient and one channel turned on.
- The text is built in the **company's language** (with fallback: Spanish, English).
- **Send test** (`POST /rules/{id}/test`): sends the text of one channel, with sample values, **to whoever requests it** (it is not recorded in the log).

## 5. The send log

`GET /api/v1/notifications/log` (most recent first; filters by event, channel, status and dates). Statuses:

| Status | Meaning |
|---|---|
| `SENT` | It went out. |
| `SKIPPED` | It did not apply or could not be done, **without being a failure**: channel not configured, the recipient has no e-mail, or the user muted the notice. The reason is recorded in the log. |
| `FAILED` | It failed (e.g. the e-mail service was down); it is retried. |
| `PENDING` | Recorded, in process. |

**Retries**: if any send fails, the notice is retried at **1, 5 and 30 minutes**; if the fourth attempt also fails, it stays failed. A
failure for one recipient does not stop the others, and the retry repeats **only what failed**.

## 6. The user's inbox

`GET /api/v1/inbox` (with `unreadOnly`), `GET /inbox/unread-count`, `POST /inbox/{id}/read`, `POST /inbox/read-all`, and the preferences
`GET /inbox/prefs` and `PUT /inbox/prefs/{evento}` (mute an optional event; a muted notice does not reach the inbox and is left `SKIPPED` in the log).

## 7. Validations and messages

| Situation | Exact message | HTTP |
|---|---|---|
| Nonexistent rule (or from another company) | `Notification rule not found.` | 404 |
| No event | `Choose the rule's event.` | 400 (`eventCode`) |
| Event that does not exist | `Unknown notification event: 'X'.` | 400 (`eventCode`) |
| Changing the event of a rule | `The event of a rule cannot be changed; create another rule.` | 400 (`eventCode`) |
| No name | `Enter the rule name.` | 400 (`name`) |
| Name longer than 100 characters | `The rule name cannot be longer than 100 characters.` | 400 (`name`) |
| Repeated name | `A rule with that name already exists.` | 409 |
| Channel without a name / unknown | `Choose the channel.` / `Unknown channel: 'X'. Use EMAIL, SMS, APP or PUSH.` | 400 (`channel(s)`) |
| Turning on an unconfigured channel | `The SMS channel is not configured in Teikem yet.` | 400 (`channels`) |
| EMAIL or APP on without a subject | `The EMAIL channel needs a subject.` | 400 (`channels`) |
| Channel on without a text | `The APP channel needs a text.` | 400 (`channels`) |
| Subject longer than 200 / text longer than 1000 characters | `The subject of the EMAIL channel cannot be longer than 200 characters.` / `The text of the EMAIL channel cannot be longer than 1000 characters.` | 400 (`channels`) |
| Variable the event does not have | `The template uses a variable the event does not have: {zzz}.` | 400 (`channels`) |
| Activating without recipients | `The rule needs at least one recipient to be activated.` | 400 (`recipients`) |
| Activating without a channel on | `The rule needs at least one channel turned on to be activated.` | 400 (`channels`) |
| Unknown recipient type | `Unknown recipient type: 'X'. Use ROLE, USER, EMAIL or PHONE.` | 400 (`recipients`) |
| Invalid e-mail / phone | `Invalid email: 'x'.` / `Invalid phone: 'x'.` | 400 (`recipients`) |
| Role that does not belong to the company | `Role not found.` | 400 (`recipients`) |
| User that does not belong to the company or is inactive | `User not found in this company.` | 400 (`recipients`) |
| The rule changed since you opened it (rowVersion) | `The record was modified by another user; reload and try again.` | 409 |
| Someone else's or nonexistent notice in the inbox | `Notice not found.` | 404 |
| Muting an event that is not optional | `That notice cannot be muted.` | 409 |
| Without the permission | “No permission” | 403 |

## 8. For support

- Tables `NotificationRule`, `NotificationRuleChannel`, `NotificationRuleRecipient`, `NotificationOutbox` (queue), `NotificationLog`, `UserNotification`
  and `UserNotificationPref`; section **2026-10-14 (g)** of `Diseño/logistica-db-update.sql`. The events are NOT in the database: they live in code.
- Configuration: `Notifications:Enabled` (default `true`) and `Notifications:PollSeconds` (default 10). The console commands do not start the service.
- E-mail: it uses the existing Brevo configuration (`Brevo__ApiKey`, `Brevo__FromEmail`). Without it the EMAIL channel is “not configured”: it cannot be turned on and,
  if an old rule had it on, the log says `SKIPPED`.
- A notice left `FAILED` in the queue (`NotificationOutbox.Status`) exhausted its retries; the error is in `LastError` and the detail per recipient in the log.
- Each processing runs **as the company, without a user** (“System”). Several copies of the system never process the same notice at the same time.

## Kemi: “Ask me how to use the system” (Batches 123–124)

Among Kemi's options (one click on the mascot) is **“Ask me how to use the system”**. Kemi **searches**; it does not use artificial intelligence:

1. The person types their question in their own words (for example “how do I change my password” or “it won't let me sign in”) and clicks **Ask**.
2. Kemi searches for those words in the **manual** and in the **FAQ** (ignoring accents and case, singular or plural, and the verb forms together: “registration”, “register”). Rare words weigh more than common ones, the title weighs more than the body and the form of the question decides where to look first (“it won't let me”, “what does it mean” → FAQ; “how do I” → manual). It does not say where each option came from.
3. Kemi shows up to **5 options**; when one is chosen, its **answer** is shown right there.
4. Kemi asks **“Did that answer your question?”**. **Yes** → it thanks you and lets you ask another question. **No** → **it looks elsewhere**: it brings other options, without repeating the ones already shown.
5. If there is nothing similar enough, Kemi says so (“I found nothing about that. Try other words.”) instead of guessing. **Change the question** goes back to the box; **Back** returns to Kemi's options.

It requires no permission or module: anyone with Kemi awake sees it. The index is downloaded **only the first time someone asks** (it is a separate file, it does not weigh on the web app's startup) and works in the browser itself: no service, no cost and without sending the question anywhere.

**Where what Kemi knows comes from:** from `docs/manual/*.md` (chapters and the FAQ), converted into an index with `npm run help:index` (run by `npm run check`); when the manual or the FAQ is written or corrected, the index is regenerated and Kemi knows it as soon as the web app is compiled. The manual is only in Spanish. **In English** Kemi understands the common words (order, invoice, password, delivery, driver…: they are paired with their Spanish equivalent to search) and warns that its answers come out in Spanish.

**The screen the person is on counts:** what is documented about that screen moves up in the options (being in Orders, a question about “cancel” brings the Orders content first; in Rentals, the Rentals content; in Billing, the Billing content…).

Arrival notices born from a purchase order are internal and do not publish these events. Like the others, they are optional and have no seeded rules: they are set up in System → Notification rules.
