# Chapter 8A — Warehouse app backend (devices, PIN, idempotency, synchronization, barcode)

This chapter covers what Batch 8A leaves built in the API so that, later on, an installable warehouse app can
exist: trusted devices enrolled by code, a short PIN to sign in without a password, synchronization
by difference of lists and tasks, product search by barcode, and "all in one" calls for the
device's work queue. **The installable app itself (screens, scanner, local database, outbound queue) does not exist
yet**; this batch is only its backend. Batch 8B (driver app) reuses this same base but is not touched
here; the `DriverDevice` table from Batch 4 stays the same.

The "all in one call" operations for receipts, picking and counting (receipt with `confirm: true`,
`collect-and-pack`, batch capture and blind count) are documented together with their source feature in
[chapter 06 — Inventory and warehouse](06-inventario-y-almacen.md) (sections 4, 6 and 7); here they are only summarized with a
link. The PIN of other users also appears in [chapter 01](01-plataforma-y-seguridad.md) (section 3.3,
next to user administration).

Conventions: same error format (ProblemDetails) and `correlationId` as chapter 01. Everything in this chapter
lives under the **WMS_LOTSERIAL** module unless stated otherwise.

---

## 1. Idempotency of writes (`Idempotency-Key`)

What it does: prevents a write (create, edit, deactivate) from being repeated by accident when the caller retries the
same operation (for example, the device's outbound queue retrying after losing the signal). It is activated
by sending the `Idempotency-Key` header in authenticated `POST`, `PUT`, `PATCH` or `DELETE`; without the header, everything
works exactly as before.

Who can: any authenticated user, in addition to the permission of the operation being called (idempotency
neither adds nor removes permissions).

How it is used:
```
POST /api/v1/receipts
Idempotency-Key: 6f2b6b0e-6e2a-4e9b-9c7e-9a8f7b6c5d4e
Content-Type: application/json

{ "purchaseOrderPublicId": "...", "confirm": true, "lines": [...] }
```
If the call is repeated with the **same key and the same body** (same method, same route with its query, same
JSON), the API does not execute the operation again: it responds with the same code and body as the first time, with the
`Idempotent-Replayed: true` header. The key is **per company and per user**: two users can use the
same key without colliding with each other. The key is **case-sensitive**: `abc-1` and `ABC-1` are two different keys.

Before replaying a saved response, the API again requires what the route always requires, replay or not
(idempotency does not add this): the route's own permission (`[RequirePermission]`, already checked by the
authorization of each request), the module (`[RequireModule]`) and the reauthentication window (`[RequireAal2]`)
when the route requires it; if any of this is no longer met, the 403 (or `module_disabled`/`aal2_required`) is the same as
for a new call. In addition, the original operation may have checked on its own, inside the service or the
controller, other modules or permissions that are not the route's (for example `purchasing.receive` and the
**PURCHASING** module when receiving against a purchase order, `orders.create` when picking and packing, or `warehouse.count`,
which decides whether the count is blind): before replaying they are evaluated again and, if **any** of them changed,
in **either direction** (lost or gained), the saved response can no longer be replayed: 409 `The operation with
this key can no longer be replayed with the current permissions.`, without returning it. It does not matter whether the change made the
operation more permissive or less: either case gives this same 409, never the saved
response.

It does not apply (the header is ignored, even if sent) on routes that return a secret in clear text, so as not to leave it 7
days in the idempotency log: `/api/v1/auth`, `/api/v1/me`, `/api/v1/devices`, `/api/v1/platform`,
`/api/v1/users` (creating a user returns the temporary password) and the portal invitations
(`/api/v1/clients/{id}/portal-users/invite` and `.../portal-users/{id}/resend-invite`).
Since 2026-10-11 that exclusion is not a list of routes but the **`[SkipIdempotency("reason")]`** attribute that each controller or action declares (the ones above are the only ones marked);
a test pins the exact set, so a new endpoint that returns credentials must be marked on purpose.

### What the API decides according to what already exists with that key

| Situation | Result |
|---|---|
| There is no record with that key | The operation is executed (normal) |
| Same key, same body, with a response already saved | That response is replayed (same code and body), `Idempotent-Replayed: true` header; it is not executed again |
| Same key, same body, **without** a response yet (another request in progress) | 409 `The operation with this key is still being processed.` |
| Same key, **another** body or **another** route | 409 `The idempotency key was already used with other content.` |
| Same key and same body, but the permission, module or reauthentication that **the route requires** are no longer met | The same 403 (or `module_disabled`/`aal2_required`) as for a new call; this does not change with idempotency |
| Same key and same body, and a module or permission that the operation checked **on its own** inside the service or the controller (e.g. the blind count) changed — gained **or** lost, either one | 409 `The operation with this key can no longer be replayed with the current permissions.` (the direction of the change no longer matters) |
| The record is more than 7 days old, or was left "in progress" more than 10 minutes without a response (crashed process) | It is discarded and executed as if it were new |

What is saved to replay: only responses with a code lower than 500, except 401, 403, 408, 423 and 429 (those are
access or rate rejections: retrying with the same key after correcting them must attempt the
operation again, not replay the old rejection). 5xx errors are never saved. The request body is **never**
saved (it could carry a PIN or a password), only its fingerprint.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Header empty, repeated, longer than 80 characters or with control characters | `The idempotency key is not valid.` | 400 |
| Same key with another body or route | `The idempotency key was already used with other content.` | 409 |
| Same key without a response yet | `The operation with this key is still being processed.` | 409 |
| Same key and body, with a module or permission checked **inside the operation** (not the route's) that changed since the first time, lost or gained | `The operation with this key can no longer be replayed with the current permissions.` | 409 |

The length (1 to 80) is measured on the value **as it arrives** in the header, before trimming the spaces at
the ends: an 81-character key with spaces around it is still invalid even if, once trimmed, it came
to 80 or fewer.

### Frequently asked questions

See [FAQ — Batch 8A](faq.md#batch-8a--warehouse-app-backend-devices-pin-idempotency-sync-and-atomic-operations)
for more detail on each message, why `/api/v1/users` is left out and what happens if an operation
that already failed is resent.

---

## 2. Trusted devices (`UserDevice`)

What it does: registers each physical terminal (RF gun, tablet) that will use the warehouse app,
so that a user can sign in to it with a **short PIN** instead of their password. The device is created
from administration (with a single-use enrollment code) and then "installed" on the device with that
code, which generates its own secret.

### 2.1 Device administration

Who can: `devices.manage`. Module **WMS_LOTSERIAL**.

How it is used:
- `GET /api/v1/devices` (`?includeInactive=true` to also see the deactivated ones), `GET /api/v1/devices/{publicId}`.
- `POST /api/v1/devices` — `{ "code": "ZB-01", "name": "Pistola almacén 1", "defaultWarehousePublicId": "...", "theme": "DARK" }`.
  Returns the device and, **only once**, `enrollCode` (8 characters, expires in 24 h): write it down or share it with
  whoever is going to install the app, because it cannot be looked up again.
- `PATCH /api/v1/devices/{publicId}` — edits name, default warehouse (or removes it) and theme; the code does not change.
  Accepts the `rowVersion` returned by the `GET` (optimistic concurrency): it responds 409 only if another administrator edited,
  deactivated, reactivated or regenerated the device's code in the meantime. The heartbeat and PIN logins do **not** change the
  `rowVersion` (the technical data "last seen", "last user" and "app version" are saved separately), so
  a device that is online does not make the edit fail.
- `POST /api/v1/devices/{publicId}/enroll-code` — generates a new enrollment code (the previous one stops working).
- `POST /api/v1/devices/{publicId}/deactivate` / `.../reactivate`.

### Validations (create and edit)

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty code (or only spaces) | `The device code is required.` | 400 |
| Code longer than 30 characters | `The device code allows up to 30 characters.` | 400 |
| Code already used by another device of the company (active or not) | `A device with that code already exists.` | 409 |
| Name longer than 100 characters | `The name allows up to 100 characters.` | 400 |
| Model longer than 80 characters | `The model allows up to 80 characters.` | 400 |
| Theme other than `LIGHT`/`DARK` | `The theme is not valid; use LIGHT or DARK.` | 400 |
| Default warehouse deactivated | `The default warehouse is deactivated.` | 422 |
| Regenerate the code of a deactivated device | `The device is deactivated; reactivate it before generating an enrollment code.` | 422 |
| Device of another company or nonexistent | `Device not found.` | 404 |
| Old `rowVersion`: another administrator changed the device after you opened it | `The record was modified by another user; reload and try again.` | 409 |

### 2.2 Enrollment on the device and heartbeat (anonymous, no session)

These two endpoints do not carry `Authorization`: the device itself identifies with its enrollment code or with
its secret. They are limited to 10 (`enroll`) and 60 (`heartbeat`) requests per minute and per IP address (the route is
normalized to lowercase and without the trailing slash before counting: `/Enroll/` and `/enroll` share the same quota).

How it is used:
- `POST /api/v1/devices/enroll` — `{ "enrollCode": "AB12CD34", "model": "Zebra TC21", "appVersion": "1.0.0" }`.
  Returns, **only once**, `deviceSecret` (the app stores it encrypted on the device; without it, it cannot sign in again) and
  the `defaultWarehousePublicId`/`theme` to start with and, since Batch 16, `defaultWarehouseReceivingMode` (the receiving mode of the default warehouse, `PUTAWAY` or
  `DIRECT`; `null` if the device has no default warehouse). Installing again with a valid code revokes the
  sessions the device had open and sets the device's session stamp again (like deactivating or
  reactivating): an access token with `did` issued before this reinstallation stops working even if the device is still
  active. Enrolling and deactivating run in a single transaction. Two enrollments at the same time with the same code
  (race): the loser receives the same 401 `The enrollment code is not valid or has expired.` (never 409), with the
  same security event as an invalid code (`API_CREDENTIAL` / `FAILURE`, `action = device_enroll`); the
  winner enrolls it successfully (`API_CREDENTIAL` / `SUCCESS`, `action = device_enrolled`).
- `POST /api/v1/devices/heartbeat` — `{ "devicePublicId": "...", "deviceSecret": "...", "appVersion": "1.0.1" }`.
  Updates "last seen" and the app version; responds `{ isActive, defaultWarehousePublicId, theme,
  serverTimeUtc, defaultWarehouseReceivingMode }` (the last one, from Batch 16, as in enrollment). A deactivated device (or one whose company lost the module) receives `isActive: false` **without
  error**, so that the app itself blocks the sign-in.

### Validations

| Case | Exact message | HTTP |
|---|---|---|
| Enrollment code invalid, expired, **missing**, of a deactivated device or of a company without the module | `The enrollment code is not valid or has expired.` | 401 |
| Secret that does not match the device, or no `deviceSecret` (heartbeat) | `The device is not enrolled or was deactivated.` | 401 |
| Too many requests in one minute (per IP and route) | `Too many attempts; wait a minute and try again.` | 429 |

### Statuses and transitions

The device has no formal status flow; it is active or not (`IsActive`). It also keeps a stamp
(`UserDevice.SessionsNotBeforeUtc`) with the moment of the last deactivation, reactivation or enrollment: any access
token with the `did` (device) claim issued **before** that stamp (compared by its `iat`, to the second, with no
clock tolerance) is rejected when validated, even if the device is active today; a token without `iat` is also
rejected. Each deactivation, reactivation or enrollment that revokes open sessions writes `TOKEN_REVOKED` with the reason
(`device_deactivated`, `device_reactivated` — always, even if there was no live session — or `device_enrolled`).

| From → to | Who | What it does | What it blocks |
|---|---|---|---|
| Active → Deactivated (`.../deactivate`) | `devices.manage` | Invalidates the pending enrollment code; revokes **all** the sessions (refresh tokens) issued to that device and sets the session stamp, all in a single transaction; the cutoff of access tokens already issued is immediate (up to 60 s delay on another API instance) | The device stops synchronizing and receiving `POST /auth/device/login`; the heartbeat responds `isActive: false` instead of an error |
| Deactivated → Active (`.../reactivate`) | `devices.manage` | Keeps the secret already installed; revokes the sessions that may have remained alive and sets the device's session stamp again | Does **not** revive the revoked sessions nor the access tokens issued before the deactivation or the reactivation itself (they stay at 401): each user signs in again with their PIN |

---

## 3. User PIN and device login

What it does: each user defines a short PIN (4 to 6 digits) to sign in on an already enrolled device, without typing
their password or going through MFA. The PIN is **per company**: the same user can have a different PIN (or
none) in each company they belong to. When verified on the device login it is trimmed the same as when
defined: leading or trailing spaces do not count (` 4826 ` signs in the same as `4826`).

### 3.1 My account (your own PIN)

Who can: any internal user, with the **WMS_LOTSERIAL** module turned on.

How it is used:
- `GET /api/v1/me/pin` — state (`hasPin`, `lockedUntilUtc`, `updatedAtUtc`); the PIN is never returned.
- `PUT /api/v1/me/pin` — `{ "currentPassword": "...", "pin": "4826" }`. Resets the attempt counter and the
  lockout if there were any. **Changing** a PIN that already existed closes the user's sessions on devices: their refresh
  responds 401 and they must sign in again with the new PIN (event `TOKEN_REVOKED`, reason `pin_changed`).
- `DELETE /api/v1/me/pin` — removes the PIN and closes the sessions the user had open on devices.

A wrong current password in `PUT /api/v1/me/pin` **counts** toward the account lockout, the same one as the web
login: 5 wrong passwords in a row (adding up login, `PUT /me/pin`, `POST /auth/reauth` and `PUT /auth/password`)
lock the account for 15 minutes, also for signing in to the web. While it lasts, `PUT /me/pin` responds with the same 400
`The current password is incorrect.` even if the password is correct (reauthentication responds 401
`Incorrect password.` and the password change the same 400 of incorrect current password). A correct password
before the lockout resets the counter. That way, a session opened with a PIN on a device is no use for
guessing the web password.

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Incorrect current password or no `currentPassword` | `The current password is incorrect.` (field `currentPassword`) | 400 |
| Account locked by failed password attempts (5 in a row among login, `PUT /me/pin`, reauthentication and password change; lasts 15 minutes), even if the password is correct | `The current password is incorrect.` (field `currentPassword`) | 400 |
| PIN with fewer than 4 or more than 6 digits, or with anything that is not a digit | `The PIN must have 4 to 6 digits.` | 400 |
| Trivial PIN (all digits the same, or a consecutive sequence such as `1234` or `9876`) | `The PIN cannot be a trivial sequence.` | 400 |
| The PIN was defined at the same time in another session | `The user's PIN changed at the same time in another session; try again.` | 409 |

### 3.2 Another user's PIN (administration)

Who can: `devices.manage` **or** `admin.users`; assigning it also requires **AAL2** (recent reauthentication,
same as changing roles). Module **WMS_LOTSERIAL**. The permission is checked **before** the reauthentication: whoever has
neither of the two permissions gets a permission 403 (`PERMISSION_DENIED` is left in the security
log), never "requires reauthentication"; whoever has the permission but did not reauthenticate, 403 `aal2_required`.

How it is used: `PUT /api/v1/users/{id}/pin` — `{ "pin": "4826" }`; `DELETE /api/v1/users/{id}/pin`. Resetting
(or removing) the PIN of a user who already had one closes their sessions on devices: whoever had signed in with the old
PIN loses the session on the next refresh (401).

### Validations

| Field / case | Exact message | HTTP |
|---|---|---|
| Without `devices.manage` or `admin.users` | 403 from the permission policy (code `forbidden`, no body of its own; event `PERMISSION_DENIED` with `devices.manage\|admin.users`) | 403 |
| With the permission but without recent reauthentication (only `PUT`) | `This action requires recent reauthentication (AAL2).` (code `aal2_required`) | 403 |
| User of another company, portal user, or platform administrator | `User not found.` | 404 |
| The target user has permissions the caller does not have | `You cannot assign or remove the PIN of a user with more permissions than you.` | 403 |
| Invalid PIN (format or trivial) | same as in My account | 400 |
| The PIN was defined at the same time in another session | `The user's PIN changed at the same time in another session; try again.` | 409 |

A PIN imposed by an administrator opens a session without a password or MFA; that is why the PIN of someone with more
effective permissions than the person doing it cannot be assigned (or removed), and the platform administrator is never
visible nor signs in this way.

That limit **is not checked only when assigning it**: on every device login and on every refresh of a device session
it is checked again against whoever assigned the PIN (`UserPin.UpdatedBy`). If later the user's permissions were raised
(roles, extra permissions or an edited role), or whoever assigned it lost theirs, was deactivated or left the
company, the PIN stops working: the login responds 403 `Your PIN was assigned by someone else who no longer has your permissions; define your own PIN in My account.` (event `LOGIN` / `BLOCKED` with
`reason = pin_assigner_lower_privileges`) and the refresh of the open session responds 401 with the same message and
revokes it (event `TOKEN_REVOKED`). A PIN assigned by an active platform administrator is exempt. The PIN that
the user defines in My account is never affected; the way out is for them to define it (or for someone with at least their
permissions to reassign it).

### 3.3 Device user list and PIN login (anonymous)

How it is used (both without `Authorization`, authenticated with device + secret; limit of 60 per minute per IP):
- `POST /api/v1/auth/device/users` — `{ "devicePublicId": "...", "deviceSecret": "..." }`. Returns the active
  internal users, with active membership in the device's company, with a **PIN defined** and with `inventory.view`,
  sorted by name (never the platform administrator). Whoever has no full name loaded is shown
  as `User` (never their e-mail: the device is shared).
- `POST /api/v1/auth/device/login` — `{ "devicePublicId": "...", "deviceSecret": "...", "userId": 12, "pin":
  "4826" }`. Returns the same token pair (`access`/`refresh`) as a normal login; the session is tied to the
  device, lasts `Tenant.DeviceSessionDays` days (30 by default, configurable from 1 to 365 in `PUT
  /api/v1/tenant/settings` with `admin.tenant`) and is renewed on every refresh as long as the device is still active and the
  company is still active with the **WMS_LOTSERIAL** module turned on. If the device is deactivated or the module is turned off,
  `POST /api/v1/auth/refresh` of that session responds 401 `The device is not enrolled or was deactivated.` and the
  session is revoked (turning the module on again does not revive it: sign in again with the PIN). Every refresh of
  a device session also revalidates the **user** (the same condition as `device/login` and as the PIN privilege
  limit of 3.2): that they are still active, with active membership in this company and with `inventory.view`. If
  any is no longer met, the refresh responds 401 `Invalid refresh token.` and revokes the session (event
  `TOKEN_REVOKED`); the device still being active is not enough, you have to sign in again with the PIN.

What the device session **cannot** do (it has no password or MFA): switch company (`POST
/auth/switch-tenant` → 403) or activate or confirm the account's two-step verification (`POST
/auth/mfa/totp/enroll` and `/confirm` → 403 `A device session does not manage the second factor.`). Otherwise, whoever saw the
PIN on a shared device, or whoever assigned it, would end up with the TOTP secret and the recovery codes, and
the owner could no longer sign in to the web with their password. The second factor is activated from the web (My account).

### Validations

| Case | Exact message | HTTP |
|---|---|---|
| Invalid or deactivated device, or no `deviceSecret` | `The device is not enrolled or was deactivated.` | 401 |
| Wrong PIN, user without a PIN, without active membership or platform administrator | `Wrong PIN.` | 401 |
| Refresh of a device session with the device deactivated or the WMS_LOTSERIAL module turned off | `The device is not enrolled or was deactivated.` | 401 |
| PIN locked (see below) | `PIN locked for 15 minutes.` | 423 |
| Correct PIN, but it was assigned by someone else who no longer covers the user's current permissions (see 3.2) | `Your PIN was assigned by someone else who no longer has your permissions; define your own PIN in My account.` | 403 |
| Refresh of a device session in that same case (the session is revoked) | `Your PIN was assigned by someone else who no longer has your permissions; define your own PIN in My account.` | 401 |
| Valid user and correct PIN, but without `inventory.view` | `Missing permission 'inventory.view'.` | 403 |
| `deviceSessionDays` outside 1–365 (settings) | `Between 1 and 365 days.` | 400 |
| Switch company with a device session (`POST /auth/switch-tenant`) | `A device session does not switch company.` | 403 |
| Activate or confirm two-step verification with a device session (`POST /auth/mfa/totp/enroll` or `/confirm`) | `A device session does not manage the second factor.` | 403 |
| Refresh of a device session whose user is no longer active, lost active membership in the company, or no longer has `inventory.view` | `Invalid refresh token.` | 401 |

### Statuses and transitions — PIN lockout

| From → to | What triggers it | Effect |
|---|---|---|
| Not locked → Not locked (with one failure) | Wrong PIN (1st to 4th failure) | Counts the attempt; responds 401 `Wrong PIN.` |
| Not locked → Locked 15 minutes | 5th failure in a row (also counts those arriving in parallel) | Responds 423 `PIN locked for 15 minutes.`; the correct PIN **also** receives 423 while it lasts |
| Locked → Not locked | The 15 minutes pass, **or** someone redefines the PIN (My account or an administrator) | Accepts attempts again with the counter at zero |
| Any → Not locked (with a success) | Correct PIN outside a lockout | Resets the counter to zero |

---

## 4. Synchronization by difference

What it does: lets the device keep a local database (to work without network) and bring it up to date by requesting only
what changed since the last time, in pages of up to 500 rows.

Who can: `inventory.view` on all resources; the purchase orders one **also** requires the
**PURCHASING** module and `purchasing.view` (the same rules as the native purchase orders screen). Module
**WMS_LOTSERIAL**.

How it is used: `GET /api/v1/sync/products`, `/sync/bins?warehousePublicId=`, `/sync/purchase-orders`, `/sync/asns`,
`/sync/warehouse-tasks?warehousePublicId=`, `/sync/product-categories`, all with `since`, `cursor` and `take`.

**Clients and consignees to dispatch without signal (2026-10-11): `GET /api/v1/sync/clients` and `GET /api/v1/sync/consignee-locations`** (same `since` / `cursor` / `take` protocol;
module **WMS_LOTSERIAL**, permission **`warehouse.pick`**: whoever dispatches from the device does not need `clients.read` or `locations.read` just to choose whom to). `sync/clients` brings
`{ id, publicId, name, code, isActive }`: without `since`, the **active** clients; with `since`, the changed ones, including the deactivated ones (`isActive: false`, which the device deletes). `sync/consignee-locations`
brings `{ id, publicId, clientPublicId, name, code, city, isActive }`: each client's locations **and the operator's shared ones** (`clientPublicId` = null), of any type, which are the same ones the
order accepts (the client's or shared, and active); those of another company never come out and, if the user has a data scope by client (manual 01 §3.5), only those of their clients and the shared ones.
Protocol:
1. First time: without `since`, it brings everything current.
2. While the response brings `nextCursor`, request the next page with that cursor as is (without changing
   `since` or the filters).
3. When `nextCursor` is `null`, save the `serverTimeUtc` of the **first** page of that pass. In the
   next pass, request `since` = that `serverTimeUtc` **minus 5 minutes** (safety margin).
4. With `since`, besides the new rows, the deactivated, closed or cancelled rows arrive with `isActive: false`:
   the device deletes them from its local database. Examples: the COUNT task of a deleted count arrives `CANCELLED` with
   `isActive: false`; a purchase order that receives a second partial delivery (still `PARTIAL`) arrives again with the
   new pending quantity.

**Balances by bin (2026-10-10, weak signal): `GET /api/v1/sync/balances?warehousePublicId=`.** Brings, per (product, bin, lot):
`binId` (null if the balance is not in a bin), `productId`/`productPublicId`, `lotId`/`lotNumber`/`lotExpiryDate`, `qtyOnHand`,
`qtyReserved` and `updatedAtUtc`. The available quantity is `qtyOnHand − qtyReserved` (the device calculates it). Without `since` only the balances
with stock or reserved quantity arrive; with `since` **all those that changed** arrive, including those left at zero (`isActive: false`: the
device sets them to zero or deletes them). Unlike the other tables, the change is detected by the balance's own mark
(`UpdatedAtUtc`, which the ledger writes on every movement and reservation), not by the log. Same protocol, same errors and same
permissions as the rest. It is the basis for the device to show balances instantly and synchronize in the background.

The parameter is called exactly `since` (UTC). A parameter with another name (for example `modifiedSinceUtc`) is
ignored **without error**: the response would be a full load of what is current, which never brings the deactivations.

### Validations

| Case | Exact message | HTTP |
|---|---|---|
| `take` greater than 500 | `The maximum per page is 500.` | 400 |
| `cursor` that did not come from a response of this API | `The cursor is not valid.` | 400 |
| Synchronize purchase orders without `purchasing.view` | (403 from the permission policy, no body) | 403 |
| Synchronize purchase orders with the PURCHASING module turned off | `The 'PURCHASING' module is not enabled for this company.` | 403 |
| Filtered warehouse of another company or nonexistent | `Warehouse not found.` | 404 |

---

## 5. Barcode search

What it does: finds a product by scanning its barcode (or, if none matches, its exact SKU), only
among active products. Designed for the device's scanner.

Who can: `inventory.view`. Module **WMS_LOTSERIAL**.

How it is used: `GET /api/v1/products/by-barcode/{código}` → same record as `GET /api/v1/products/{publicId}`.

Tie-break (3PL: the same code or SKU for several owners): the company's own product wins; if there is no own product,
among the clients' products the one created first wins (lowest product id), not the one of the client with the lowest id.

### Validations

| Case | Exact message | HTTP |
|---|---|---|
| No active product with that barcode or that SKU | `There is no product with that code.` | 404 |

---

## 6. Atomic operations of the device queue

These calls exist so that the device (without a reliable network) does in **a single request** what on the web is
several steps, all or nothing inside a transaction. Documented in detail in chapter 06:

- **Receipt in one call**: `POST /api/v1/receipts` with `"confirm": true` — creates the receipt, captures its lines and
  confirms it; see [06 §4](06-inventario-y-almacen.md#4-receiving-arrival-notices-asn-and-receipts).
- **Pick and pack in one call**: `POST /api/v1/pick-batches/collect-and-pack` — see
  [06 §7](06-inventario-y-almacen.md#7-ad-hoc-picking-and-packing-pick--pack).
- **Batch count capture**: `PUT /api/v1/cycle-counts/{id}/lines/batch` (several lines in one call; repeated
  line → 400 `The line is repeated in the request.`) — see
  [06 §6](06-inventario-y-almacen.md#6-cycle-count-informed-mode).
- **Blind count**: whoever has `warehouse.count.capture` (implicit in `warehouse.count`) but not
  `warehouse.count` counts without seeing what is expected (`isBlind: true`; expected quantities as `null`) and cannot
  reconcile — see [06 §6 — Batch 8A](06-inventario-y-almacen.md#batch-8a--blind-count-warehouse-app).

All of them honor `Idempotency-Key` (section 1): a retry with the same key and the same body does not create
the receipt, the pick or the count lines again.

---

## 7. Permissions and modules (batch summary)

| Permission | Category | What it enables |
|---|---|---|
| `devices.manage` | SECURITY | Create, edit, deactivate/reactivate and regenerate the code of devices; assign or remove other users' PIN |
| `warehouse.count.capture` | WAREHOUSE | Count blind (create, capture, found items, finish) without reconciling; implicit in `warehouse.count` |

Module **WMS_LOTSERIAL** covers this whole chapter (devices, PIN, device login, synchronization, barcode
and the atomic operations). Synchronizing purchase orders also requires the **PURCHASING** module.

---

## 8. Frequently asked questions

This chapter summarizes the validations with their exact message; for the plain-language explanation of each one
(what to do, why it happens) see the **Batch 8A** section of [`faq.md`](faq.md), which also includes use cases
(why I don't appear in the device list, what happens if I deactivate a device with open sessions, why
`Idempotency-Key` does not apply on `/api/v1/users`, etc.).
