# Chapter 01 — Platform and security (Batch 1)

This chapter covers what Batch 1 leaves built: access and session, two-factor authentication (MFA), my profile, tenant users
and roles, lists, statuses, contact points, custom fields, views/indicators/charts
(analytics), audit and security, modules per tenant, company settings and platform
administration.

Conventions of this guide:
- Each action states the exact **permission** it requires (format `recurso.accion`) and, where applicable, the **module**
  that must be turned on for the tenant.
- The error messages quoted are the exact text the API returns (the error's `title`). The HTTP code goes
  next to the message.
- Every error response has this shape (JSON):
  ```json
  { "type": "https://teikem.app/errors/<code>", "title": "<mensaje>", "status": <http>, "code": "<code>", "errors": null, "correlationId": "<guid>" }
  ```
  The `correlationId` also comes in the `X-Correlation-Id` response header; it is used to find the action
  in audit/security.

---

## 1. Access and session

### 1.1 Sign in

What it does: validates email and password; if the user has more than one active company with none marked as
default, or if the call does not include `tenantId`, it may ask the user to choose a company; if the tenant requires MFA or the
user already has it active, it asks for the second factor before issuing tokens.

Who can: any active internal user (`UserKind = INTERNAL`). Portal users cannot sign in
here.

How it is used: `POST /api/v1/auth/login`
```json
{ "email": "admin@teikem.local", "password": "Teikem_Admin_2026!", "deviceInfo": "smoke" }
```
Response with `status`: `ok` (includes `tokens`), `mfa_required` (includes `mfaChallengeToken` as Bearer for
step 2). If the user belongs to several companies and does not send `tenantId`, **they are not asked** (since 2026-09-30): they enter
their default company; if they are no longer a member of it, the membership marked as default; otherwise, the first one by
name. They then switch company with `POST /api/v1/auth/switch-tenant` (the selector in the header). The value
`tenant_selection` is no longer issued.

**Mandatory first sign-in (2026-09-30).** If the user is missing the first sign-in (column `AspNetUsers.OnboardingRequired`
plus: unverified email `EmailVerifiedUtc IS NULL`, administrator-set password `MustChangePassword = 1` or no confirmed
MFA), the login responds `status = "onboarding_required"`, without `tokens`, with `mfaChallengeToken` (lifetime
`Auth:Onboarding:ChallengeMinutes`, 30 min) and `onboarding = { email (masked), emailVerified, passwordChangeRequired,
mfaConfigured }`. With that token as Bearer, in order:

| Endpoint | What it does | Errors |
|---|---|---|
| `GET /api/v1/auth/onboarding` | What is missing | — |
| `POST /api/v1/auth/onboarding/email/send` | Sends the 6-digit code through Brevo (`Brevo__ApiKey`, `Brevo__FromEmail`; optional `Brevo__FromName`); the sender is shown with the company's name (2026-10-08; see FAQ) | 409 `Your email is already verified.`; 409 `The email could not be sent. Try again in a few minutes.` |
| `POST /api/v1/auth/onboarding/email/verify` `{ code }` | Confirms the email (a wrong code counts toward the account lockout) | 400 `The code is not valid or has expired.` |
| `POST /api/v1/auth/onboarding/password` `{ newPassword }` | Own password; returns a new `mfaChallengeToken` (the stamp rotated) | 409 `Verify your email first.`; 409 `You already changed your password.`; 400 `The new password must be different from the one you were given.` and the policy ones |
| `POST /api/v1/auth/mfa/totp/enroll` → `/confirm` → `/mfa/verify` | Sets up MFA and signs in; the verify turns off `OnboardingRequired` | 403 `Complete the email verification and the password change first.` |

The PIN login of a user with the first sign-in pending responds 403 `Complete your first sign-in on the web first
(email, password and two-step verification).`. New and existing users start pending, including the demo ones and the platform
admin (2026-10-01: the email is not considered verified when the user is created); only `Seed:Demo:CompleteOnboarding=true` (local
automated tests) marks them complete. `Auth:Onboarding:Enabled=false` turns it off
(only for the smoke/CI); `Auth:Onboarding:ReturnEmailCodeInResponse=true` (Development) returns the code in the response.

Validations:

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| User does not exist or is inactive | which one failed is not revealed | `Invalid credentials.` | 401 |
| Account locked (5 failed attempts) | 15-minute lockout | `Invalid credentials.` | 401 |
| Wrong password | — | `Invalid credentials.` | 401 |
| Portal user | `UserKind = PORTAL` | `Portal users authenticate in the client portal.` | 401 |
| `tenantId` requested but no active membership there | — | `You do not belong to that company.` | 403 |
| User without any active company | — | `The user has no active company.` | 403 |
| Inactive company | `Tenant.IsActive = false` | `The company is inactive.` | 403 |
| Wrong second factor (`mfa/verify` step) | — | `Invalid MFA code.` | 401 |

FAQ:
- **Why doesn't it tell me whether the email or the password was wrong?** For security: revealing which of the two
  failed makes it easier to guess valid accounts. The message is always the same (`Invalid credentials.`).
- **Every failed attempt is recorded.** Yes, in the security event `LOGIN` (failure) or `LOCKOUT`
  (lockout), visible in `/api/v1/audit/security-events` to whoever has `admin.audit`.

### 1.1b Recover the password (2026-10-07)

**Whoever forgot it — “Forgot your password?” on the login** (no session, no permission required)
1. On the sign-in screen tap **Forgot your password?** and type your email (`/forgot-password`, `POST /api/v1/auth/forgot-password`).
2. It always responds the same, whether the email exists or not: `If the email is registered, we sent you a link to reset your password.` (it is not possible to find out which emails exist).
3. The email carries a link that **is valid for 60 minutes and can be used only once**. The link leads to `/reset-password`: type the new password twice (`POST /api/v1/auth/reset-password`, 204).
4. When it is changed, all open sessions of that account are closed, a lockout for failed attempts is lifted and the second factor (MFA) is requested as always when signing in. Receiving the email counts as a verified email.
- Errors: `The link is not valid or has expired. Ask for a new one.` (400: expired, already used, tampered link or one belonging to another email); password shorter than 12 characters or found in known breaches (400, with the reason); `Too many attempts; wait a minute and try again.` (429: 5 per minute per IP).
- Configuration (system administrator): `Auth:PasswordReset:WebBaseUrl` (address of the web app, required to send the email; the link is never built from request data), `LinkMinutes` (60) and, only in development, `ReturnLinkInResponse`.

**User administration — “Temporary password”** (permission `admin.users`, requires AAL2 reauthentication; `POST /api/v1/users/{id}/temporary-password`)
1. In **System → Users**, on the person's row, action **Temporary password**. You can leave the field blank (the system generates one) or type one of at least 12 characters.
2. The window shows the password **only once** and its expiration; hand it to the person in person or through a secure channel. It is not sent by email.
3. **It is valid for 10 minutes.** If nobody signs in with it in that time, on the next sign-in attempt **the previous password comes back** and everything stays as it was. While it is valid, the previous one does not work.
4. If the person signs in with the temporary one, the system takes them to the **mandatory change**: they must set their own password (different from the temporary one) before receiving a session, and then go through their second factor as always.
5. The person's open sessions are closed. Setting another temporary password on top of a valid one keeps the password from before the first one.
- Errors: `You cannot set a temporary password for yourself.` (409); user who is not from your company (404); short password or one found in known breaches (400, with the reason).
- It is recorded in the security log (password change: who set it, and whether it expired and was restored).

### 1.2 MFA verification (step 2 of the login)

What it does: completes the login when step 1 returned `mfa_required`, with the TOTP code or a single-use
recovery code.

Who can: the user who received the `mfaChallengeToken` (sent as `Authorization: Bearer`).

How it is used: `POST /api/v1/auth/mfa/verify` with `{ "code": "123456", "deviceInfo": "..." }`.

Validations: empty or wrong code → `Invalid MFA code.` (401).

### 1.3 Session refresh, company switch and sign-out

- Refresh tokens: `POST /api/v1/auth/refresh` `{ "refreshToken": "..." }`. The refresh token is single
  use: each call rotates it (revokes the previous one and hands out a new one). If an already **rotated** one is reused,
  the person's whole session chain is revoked (protection against token theft). On the other hand, a token that was **revoked on purpose**
  (an administrator revoked the session, the person closed it from My account, “Sign out other sessions” or logout) only fails with
  `401 Invalid refresh token.`: only that session is closed and the person's other sessions stay alive.
- Switch the active company without entering the password again: `POST /api/v1/auth/switch-tenant`
  `{ "refreshToken": "...", "tenantId": 2 }`. Reloads menu, permissions and data with the new tenant.
- Close this session: `POST /api/v1/auth/logout` `{ "refreshToken": "..." }` (204, does not require being
  authenticated because the refresh token itself identifies the session to close).
- Close all of your own sessions: `POST /api/v1/auth/logout-all` (authenticated). It also invalidates the
  access tokens already issued (rotates the `SecurityStamp`).

Validations:

| Case | Exact message | HTTP |
|---|---|---|
| Empty refresh | `Invalid refresh token.` | 401 |
| Refresh does not exist / already used / revoked | `Invalid refresh token.` | 401 |
| Expired refresh | `Session expired.` | 401 |
| Membership no longer active in the refresh's tenant | `The membership is no longer active.` | 403 |
| `switch-tenant` to a tenant without membership | `You do not belong to that company.` | 403 |
| `switch-tenant` with the refresh token of a device session (Batch 8A) | `A device session does not switch company.` | 403 |

### 1.4 Recent reauthentication (AAL2 / step-up)

What it does: some sensitive actions (changing a role's permissions, turning modules on/off, provisioning a
tenant, etc.) require having reauthenticated recently. This endpoint confirms the password (and the MFA if the
user has it) and marks the session as “AAL2 verified” for the tenant's configured window
(`Aal2WindowMinutes`: 15, 30 or 60 minutes, 30 by default; a company that already had another value keeps it until it changes it).

How it is used: `POST /api/v1/auth/reauth` `{ "password": "...", "mfaCode": "123456" }` (authenticated). Returns
a new access token with the AAL2 stamp.

Validations: wrong password → `Incorrect password.` (401); with MFA active and code omitted or
wrong → `Invalid MFA code.` (401).

If an action marked `[RequireAal2]` is attempted without having done this step (or if the window already expired), the API
responds **403** with `This action requires recent reauthentication (AAL2).` (`code: aal2_required`). The
client must call `reauth` and retry with the new token.

### 1.5 Active sessions and their revocation

What it does: lists the user's open devices/sessions (live refresh tokens) and allows closing a specific
one (for example, a lost device).

How it is used:
- `GET /api/v1/auth/sessions` (authenticated): each row carries `deviceInfo`, `issuedAtUtc`, `expiresAtUtc`,
  `aal2VerifiedAtUtc` and `isCurrent`.
- `DELETE /api/v1/auth/sessions/{id}` (authenticated): revokes that session.

Validations: revoking the current session from this list → `The current session is not revoked from the list; use logout.`
(409). Nonexistent session or one belonging to another user → `Session '<id>' not found.` (404).

An administrator can also close **all** the sessions of another user: `DELETE /api/v1/users/{id}/sessions`
with `admin.users`.

Batch F10: the administrator also sees and revokes the sessions of **the whole company** (one by one or “the others”) in
Security & audit → Sessions & MFA; see section 9.1. Since Batch F10 each session stores the **IP** from which it was
opened or last renewed (shown as “Location”; there is no geolocation).

### 1.6 Password change

What it does: changes your own password, verifying the current one.

How it is used: `PUT /api/v1/auth/password` `{ "currentPassword": "...", "newPassword": "..." }` (authenticated).

Password rules (there are no composition rules, following NIST 800-63B):

| Rule | Value |
|---|---|
| Minimum length | 12 characters |
| Minimum distinct characters | 4 |
| Required uppercase/lowercase/digits/symbols | None required |
| Failed attempts before lockout | 5 |
| Lockout duration | 15 minutes |

Validations:

| Case | Exact message | HTTP |
|---|---|---|
| Wrong current password | `Incorrect password.` | 401 |
| New password too short / with too few distinct characters | Identity message in the person's language (e.g. “The password must be at least 12 characters long.”) | 400 |
| New password found in a known breach | `This password appears in known breaches; choose another one.` | 400 |

Note: the check against known breaches (HIBP) is implemented as an interface (`IPasswordBreachChecker`)
whose current version **does nothing** (it always says the password is not compromised) because the environment has no
Internet access; the message above exists in the code but today it is not triggered in practice.

### 1.7 MFA by app (TOTP): enable, confirm, recovery codes, disable

What it does: second factor through an authenticator app (Google Authenticator, Authy, etc.), with single-use
recovery codes in case the device is lost.

How it is used:
1. `POST /api/v1/auth/mfa/totp/enroll` (authenticated, or with the `mfaChallengeToken` if the tenant requires MFA and the
   user does not have it yet) → returns `secret` and `otpAuthUri` to scan in the app.
2. `POST /api/v1/auth/mfa/totp/confirm` `{ "code": "123456" }` with the code the app shows → returns
   `recoveryCodes` (show them only once; they cannot be viewed again).
3. From the next login on, the user receives `mfa_required` and must go through `POST /api/v1/auth/mfa/verify`.
4. Disable: `DELETE /api/v1/auth/mfa/totp` (authenticated, **requires AAL2**).

Validations:

| Case | Exact message | HTTP |
|---|---|---|
| TOTP already confirmed and enrollment is attempted again | `TOTP is already enrolled and confirmed; disable it first (requires AAL2).` | 409 |
| Confirming without having enrolled before | `Start the TOTP enrollment first.` | 409 |
| Wrong confirmation code | `Invalid code.` | 400 |
| Disabling without recent AAL2 | `This action requires recent reauthentication (AAL2).` | 403 |

FAQ:
- **I lost my phone with the MFA app. What do I do?** Use one of the recovery codes that were shown
  when you confirmed MFA (each one works only once) in `POST /auth/mfa/verify`. If you lost those too, ask
  an administrator of your company (permission `admin.users`) to review your account; Batch 1 does not have its own
  “reset MFA” flow: today the way out is to disable TOTP from a session already authenticated with current
  AAL2, or to have support investigate case by case.
- **Is there SMS or biometrics?** Not in this batch; only TOTP (authenticator app) and recovery codes.

---

## 2. My profile (`/me`) and effective permissions

What it does: returns, for the current session, the active tenant, the companies the user belongs to (with status and
which one is the default), the **effective permissions** (those of the assigned role or roles plus the extra permissions
granted directly to the user), the **enabled modules** (the menu is built from that), the data
scopes (`UserDataScope`) and whether MFA is active.

Who can: any authenticated user, about themselves.

How it is used: `GET /api/v1/me`.

FAQ:
- **I don't see a menu option I should have.** It may be due to permission (check `permissions` in `/me`) or
  because the module that screen depends on is turned off for your company (check `enabledModules`).

---

## 3. Tenant roles and users

### 3.1 Permission list (read-only)

`GET /api/v1/permissions` (authenticated): lists the permissions available on the platform (code, category,
label). The list is defined in code (`PermissionCatalog`) and the tenant does not edit it.

### 3.2 Roles: list, create, edit, delete

What it does: a role groups permissions; assigning the role to a user gives them all those permissions. There are 6 clonable
system templates (`TenantAdmin`, `Dispatcher`, `Billing`, `WarehouseOperator`, `Driver`, `ReadOnly`) that are
copied when a tenant is provisioned.

Since 2026-10-01 the **Warehouse operator** template (`WarehouseOperator`, 18 permissions) includes `inventory.adjust`,
`inventory.manage` and `warehouse.manage` (adjust and manage inventory, manage warehouses) and **no longer** includes
`cod.reconcile`, nor route permissions (`trips.*`: it does not see routes, the Route monitor or the Scan station, nor their indicators)
nor purchasing permissions (`purchasing.*`: it does not see purchase orders nor receive against them; it does receive without an order). That is how Luis left it in Advance
Logistics and that is how it stays for every new or recreated company.
Existing companies do not change on their own (permission propagation only adds new platform codes).

Since 2026-10-12 (Batch 39) the permission **`orders.view_price`** exists (“View order prices”, category Orders; the
list grows to 74 permissions). The **TenantAdmin** and **Billing** templates carry it; the database update
(`Diseño/logistica-db-update.sql`, section “2026-10-12 (a)”) also adds it to each company's already cloned roles
with those two names (it only adds). Without it, the user sees the orders but not their prices: see
[chapter 03, section 3.5](03-ordenes-de-transporte.md#35-who-sees-the-prices-permission-ordersview_price-batch-39).

Who can: view roles, any authenticated user (`GET /api/v1/roles?includeTemplates=`); create, `admin.roles`;
edit/delete, `admin.roles` **+ AAL2** (changing a role's permissions affects everyone who has it).

How it is used:
- `POST /api/v1/roles` `{ "name": "Supervisor", "permissions": ["orders.view","orders.edit"] }`
- `PUT /api/v1/roles/{id}` (same body)
- `DELETE /api/v1/roles/{id}`

Validations:

| Case | Exact message | HTTP |
|---|---|---|
| Empty name | `The name is required.` | 400 |
| Name repeated in the tenant | `The role '<name>' already exists.` | 409 |
| Unknown permission in the list | `Unknown permissions: <list>.` | 400 |
| Deleting a role with assigned users | `The role has assigned users; reassign them before deleting it.` | 409 |
| Without `admin.roles` | `Missing permission 'admin.roles'.` | 403 |

### 3.3 Users: list, create/invite, edit, roles, extra permissions, membership, data scopes, sessions

What it does: creation and administration of the tenant's internal users. When creating, if no password is sent, the API
generates a temporary one and returns it **only once** in the response (so that support/the admin shares it through
a secure channel; it cannot be queried again).

Who can: this whole block requires `admin.users`; changing roles and extra permissions also requires **AAL2**.

How it is used:
- `GET /api/v1/users`, `GET /api/v1/users/{id}`
- `GET /api/v1/users/assignable-companies` (2026-10-01) → `[{ tenantId, name }]`: other active companies to which whoever creates the
  user can add them (active member with `admin.users` there; the platform administrator, all of them).
- `POST /api/v1/users` also accepts `alsoTenantIds: [..]`: adds the person to those companies with a membership
  (not default) and the same roles **by name** as `roles`. Everything is validated before creating anything: 403
  `You cannot add users to that company.` and 400 `In {compañía} the roles do not exist: {roles}.`. The response carries
  `alsoAddedTo` and `alreadyMemberOf` (names; nothing is touched in the ones they were already a member of).
- `POST /api/v1/users` `{ "email": "...", "fullName": "...", "roles": ["Dispatcher"] }` → the response includes
  `temporaryPassword` if `password` was not sent.
  Batch 8A: `/api/v1/users` (and its subroutes) stays **outside** idempotency (`Idempotency-Key` is ignored): the
  creation response carries the temporary password in the clear and must not be saved in the idempotency log. For the
  same reason the portal invitations stay outside (`POST /api/v1/clients/{id}/portal-users/invite` and
  `.../portal-users/{id}/resend-invite`, which return the invitation token).
- Batch 8A — PIN of other users for the warehouse devices (module **WMS_LOTSERIAL**; `devices.manage` or
  `admin.users`): `PUT /api/v1/users/{id}/pin` `{ "pin": "4826" }` (requires **AAL2**) and `DELETE /api/v1/users/{id}/pin`.
  The PIN of a user with **more permissions** than the one doing it cannot be assigned or removed (403 `You cannot assign or
  remove the PIN of a user with more permissions than you.`) nor that of the platform administrator (404 `User not
  found.`); the platform administrator never signs in through a device (401 `Incorrect PIN.`). The user list
  exposes `hasPin`.
- Batch 8A — PIN lockout proof against parallel attempts: each failed attempt is counted with a single atomic
  statement in the database; on the 5th failure the PIN is locked for 15 minutes (423 `PIN locked for 15 minutes.`)
  even if the attempts all arrive at once (those arriving after the lockout also receive 423, even with the
  correct PIN). A success resets the counter.
- Batch 8A — attempt limit of the device's anonymous endpoints (per IP address and route, 1-minute window):
  `POST /api/v1/devices/enroll` 10 per minute; `POST /api/v1/auth/device/users`, `POST /api/v1/auth/device/login` and
  `POST /api/v1/devices/heartbeat` 60 per minute (`RateLimiting:DeviceEnrollPerMinute` and
  `RateLimiting:DeviceAuthPerMinute`). When exceeded: 429 `Too many attempts; wait a minute and try again.`
  (code `rate_limited`, header `Retry-After: 60`). The password login keeps its per-account lockout.
- Batch 8A — creating devices (`POST /api/v1/devices`, `devices.manage`): the code is required (400 `The device
  code is required.`, also if it comes empty or with spaces only), allows up to 30 characters (400 `The device
  code allows up to 30 characters.`) and is unique in the company (409 `A device with that code already exists.`). Both
  400s arrive in `errors.code`.
- Batch 8A — the session opened with a PIN on a device stays tied to the device's company: `POST /api/v1/auth/switch-tenant`
  with its refresh token responds 403 `A device session does not switch company.` (without revoking the session).
- Batch 8A — the device (`USER_DEVICE`) exists only for the log: it does not allow custom fields. The polymorphic
  route `/api/v1/custom-fields/values/USER_DEVICE/{id}` requires `devices.manage` (403 without it) and always responds
  404 (closed resolver, like `PRODUCT_CATEGORY`).
- `PUT /api/v1/users/{id}` `{ "fullName": "...", "isActive": true }`
- `PUT /api/v1/users/{id}/roles` `{ "roles": ["Dispatcher","Billing"] }` (AAL2)
- `PUT /api/v1/users/{id}/permissions` `{ "permissions": ["analytics.dates"] }` — specific permission for the user,
  in addition to what the role gives them (AAL2)
- `PUT /api/v1/users/{id}/membership` `{ "status": "SUSPENDED" }` — status of the membership in the tenant
- `GET /PUT /api/v1/users/{id}/data-scopes` — additional data scopes (see 3.4)
- `DELETE /api/v1/users/{id}/sessions` — closes all of that user's sessions

Validations:

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty email | `The email is required.` | 400 |
| Creating a portal user | `Portal users are managed from the client's record (Batch 2).` | 400 |
| Invalid password (Identity) | Identity message in the person's language, e.g. “The password must be at least 12 characters long.” | 400 |
| User already belongs to the tenant | `The user already belongs to this company.` | 409 |
| The email already belongs to a portal user (of this or another company) | `That email already belongs to a portal user; a portal user cannot be an internal user at the same time.` (Batch 2: the portal account is not attached to the company nor can it be edited from `/users`, which responds 404 for it) | 409 |
| Deactivating yourself | `You cannot deactivate yourself.` | 409 |
| Unknown roles | `Unknown roles: <list>.` | 400 |
| Unknown permissions | `Unknown permissions: <list>.` | 400 |
| Changing your own membership | `You cannot change your own membership.` | 409 |
| User does not exist | `User '<id>' not found.` | 404 |

Two different mechanics for granting permissions: **by role** (affects everyone who has that role) vs. **extra
permission per user** (specific exception, only for that person). Always review both when auditing access.

### 3.4 Membership and its statuses

Each user has, per tenant, a membership with status `ACTIVE`, `SUSPENDED` or `INVITED`. Only with
`ACTIVE` can they operate in that tenant. The change is made with `PUT /api/v1/users/{id}/membership` and does not go through
the generic status transition engine (Chapter 5): it is a simple field, it has no `EntityStatusHistory`
history nor does it validate a pipeline.

### 3.5 Data scopes (`UserDataScope`)

What it does (since Batch 32, 2026-10-11): restricts a user to seeing only certain **warehouses** (`ScopeEntity` = `WAREHOUSE`) and/or
**clients** (`CLIENT`). Who can: `admin.users` (`GET`/`PUT /api/v1/users/{id}/data-scopes`; the `PUT` replaces the whole list).
It is exposed in `/me`.

How it is applied (on the server, so it holds for the web app, the app and the device synchronization):

- **No scope of a type = no restriction of that type.** A user without any row sees everything in their company, as always.
- **With a warehouse scope**, they only see the indicated warehouses and what depends on them: stock, receipts and ASNs, pick batches and
  dispatches, counts, damage, discrepancies, dock appointments, cross-dock, tasks, purchase orders, rentals, and the warehouse's zones, bins
  and docks. **With a client scope**, they only see the indicated clients and what belongs to them: contacts, contracts, special
  services, locations, transport orders, import batches, templates, portal users, client products and rentals.
- What does not belong to any warehouse or client (row with the field empty, e.g. a shared product) **is always visible**; the
  lists are not filtered.
- With **both types** at once, **both** are required in what has a warehouse and a client (e.g. an ASN).
- Requesting something outside the scope by id responds **404** with the “not found” message (it does not reveal that it exists), just like across companies.
- **Nobody is exempt**: an administrator with an assigned scope is limited just the same; for them to see everything, their scope rows are removed.
- The scope is read on every request: a change holds from the next one (the user does not have to sign in again).
- Internal processes (seeders, background jobs) have no user and are not limited.

Frequent cases: a user with a scope sees lists with fewer rows than expected → review their scopes
(`GET /api/v1/users/{id}/data-scopes`); a report shows them different totals than another user → that is expected.

### 3.6 Isolation between companies (how it is guaranteed; Batch 42, 2026-10-12)

For support. Every server query on a company's data automatically carries the condition “only rows of the
session's company” (the company comes from the access token, never from the request); a user with a data scope (§3.5) also carries
that of their warehouses or clients. Nobody writes it by hand on each screen, so it cannot be forgotten.

- Since **2026-10-12** that condition reaches the database in a form that is resolved with the company's own indexes:
  the large lists (orders, movement ledger, balances, searches) no longer read the other companies' rows in order to discard them. **What
  each user sees did not change**: same rows, same order, same totals. Only the speed changed, and that one company no longer makes
  another wait.
- Requesting something from another company by id responds **404** (“not found”), as always: it does not reveal that it exists.
- Only platform processes (provisioning a company, seeders, migration, sign-in) read without that condition, and
  only within their step.
- If a server update were ever unable to apply the fast form, the condition is still applied in full (slower,
  just as safe). The automated tests go through all the company data tables and fail if a query went out without it.

Frequent cases: “the order list now loads much faster” → expected after applying `db-update` (index
`IX_Order_Tenant_Created`); “the free-text search of orders still takes long in the large company” → expected (it searches text inside
numbers, invoices, packages and consignee names of all its orders); to find a specific order the exact search by code
is faster (scan the package or type the full number: `GET /api/v1/orders/lookup`).

---

## 4. Lists (LookupCode / CatalogDomain)

What it does: reusable classification values (service types, contact types, etc.), grouped into
“domains”. Each value has a label in Spanish/English (`labels`), can be disabled and can have a
per-tenant override (change label, extra color, order or enablement without touching the global list). A
tenant can also create **its own lists** (for example, to use them in a list-type custom
field).

Who can: view, any authenticated user; create/edit/override/deactivate/own lists, `admin.catalogs`.

How it is used:
- `GET /api/v1/catalogs/domains` — available domains (`?scope=` filters system/tenant).
- `GET /api/v1/catalogs/{entity}` — values resolved for the tenant (header `X-Lang: en` changes the language).
- `PUT /api/v1/catalogs/{entity}/{code}/override` `{ "labels": {"es": "Exprés 24h"} }`
- `DELETE /api/v1/catalogs/{entity}/{code}/override` — removes the override
- `POST /api/v1/catalogs/{entity}` — new value in a domain (not a system one, or being a platform
  administrator)
- `PUT /api/v1/catalogs/{entity}/{code}` — edit value
- `DELETE /api/v1/catalogs/{entity}/{code}` — deactivate (does not delete; it stays in the history, it is no longer offered)
- `POST /api/v1/catalogs/{entity}/{code}/restore` — reactivate
- `POST /api/v1/catalogs/lists` `{ "name": "Zona de entrega", "values": [{"code":"NORTE","labels":{"es":"Norte","en":"North"}}] }`
- `DELETE /api/v1/catalogs/lists/{domainKey}`

Validations:

| Field / case | Exact message | HTTP |
|---|---|---|
| Nonexistent domain | `List domain '<domainKey>' not found.` | 404 |
| Nonexistent value | `Value <entity> '<code>' not found.` | 404 |
| Code repeated in the domain | `The value '<code>' already exists in <entity>.` | 409 |
| Empty label | `The label is required.` | 400 |
| Deactivating a system value globally | `A system value is not deactivated globally; use the tenant override (IsEnabled=false).` | 409 |
| Empty list name | `The name is required.` | 400 |
| Invalid list name (does not produce a key) | `Invalid name.` | 400 |
| Repeated list key | `A list with the key '<key>' already exists.` | 409 |
| Deleting a system list | `System lists are not deleted.` | 409 |
| Deleting a list in use by a custom field | `The list is in use by a custom field; deactivate it instead of deleting it.` | 409 |
| Editing a global list without being a platform admin | `Global lists are edited only by the platform administrator; use overrides or create your own list.` | 403 |
| Empty value code | `The code is required.` | 400 |
| Invalid value code (>40 or not alphanumeric) | `Invalid code (max. 40 alphanumeric characters).` | 400 |

FAQ:
- **I changed a value's label and I don't see the change.** Check that you are using the expected language
  (`X-Lang`); the override only applies in your tenant, it does not change the global list.

---

## 5. Statuses and transitions (StatusCode)

What it does: each business entity with a lifecycle (for example, an order) advances through a **pipeline** of
stages (`PIPELINE`), can have **lateral** or **terminal** exits (`LATERAL`/`TERMINAL`, e.g. cancelled) and
each company can enable/disable stages, change their color/order/label, and define which actions
(`StatusCapability`) are allowed in each status. Every real status change (once the business entities exist
in upcoming batches) is made solely through `StatusService.TransitionAsync`, which validates the
pipeline and writes history.

Who can: view pipeline/capabilities/laterals, any authenticated user; view the **history** of a record, the read
permission of the owning entity (CLIENT/CLIENT_CONTACT → `clients.read`, LOCATION → `locations.read`, CONTRACT →
`contracts.read`, PORTAL_USER → `portalusers.manage`; USER and unregistered entities → any authenticated user; without it,
`Missing permission '<código>'.` 403); configure (overrides, capabilities, lateral entries), `admin.statusconfig`.

How it is used:
- `GET /api/v1/status/{entity}` — stages resolved for the tenant (e.g. `entity=OrderStatus`).
- `GET /api/v1/status/{entity}/validate` — validates the tenant's current pipeline.
- `PUT /api/v1/status/{entity}/{code}/override` `{ "isEnabled": false }`
- `GET /PUT /api/v1/status/capabilities/{entityType}?statusDomain=OrderStatus`
  `[{"statusCode":"IN_TRANSIT","capability":"EDIT_CARGO","isAllowed":false}]`
- `GET /PUT /api/v1/status/lateral-entries/{entityType}?statusDomain=OrderStatus`
  `[{"lateralStatusCode":"CANCELLED","fromStatusCode":"PICKUP","isAllowed":true}]`
- `GET /api/v1/status/history/{entityType}/{entityId}` — status change history of a record.

Transition rules (`StatusService.TransitionAsync`, they apply when a business batch invokes them):
- A new record must be born in an **initial** stage of the pipeline.
- From a pipeline stage you can only advance to the next enabled one by order (`SortOrder`), unless the
  destination is a lateral/terminal allowed by `StatusLateralEntry` (the tenant rule overrides the default; with no
  rules defined, it is allowed).
- A **terminal** status allows no further transitions.
- From a **lateral** status you can only go back to the stage it detoured from, or advance to the
  next stage after that one.
- Before executing a business action, `StatusCapability` is validated: if none is configured for that
  status×action combination, it is allowed by default; if there is one and it says `isAllowed=false`, it is blocked.

Validations:

| Case | Exact message | HTTP |
|---|---|---|
| Nonexistent status domain | `Status domain '<entity>' not found.` | 404 |
| Nonexistent status | `Status <entity> '<code>' not found.` | 404 |
| Override leaves the pipeline without an enabled initial stage | `Invalid pipeline: <detalle>` | 422 |
| Action blocked by `StatusCapability` | `The current status does not allow the action '<capability>'.` | 422 |
| Lateral entry toward a stage that is a pipeline stage | `'<code>' is a pipeline stage, not a lateral/terminal.` | 400 |
| Pipeline without an initial stage when transitioning | `The pipeline '<statusDomain>' has no enabled initial stage.` | 422 |
| Destination status does not exist or is disabled in the tenant | `The status '<code>' does not exist or is not enabled for this company.` | 422 |
| Creation that does not start in an initial stage | `A new record must start in an initial stage; '<code>' is not one.` | 422 |
| Transition to the same status | `The record is already in that status.` | 422 |
| Transition from a terminal | `'<code>' is terminal: it allows no further transitions.` | 422 |
| Pipeline jump out of order without a lateral rule | `Illegal jump: from '<from>' you can only advance to '<next>'.` | 422 |
| Transition not allowed by the tenant's model | `Moving to '<to>' from '<from>' is not allowed according to this company's model.` | 422 |
| From a lateral, destination not allowed | `From the lateral '<from>' you can only go back to '<last>' or advance to '<next>'.` | 422 |

FAQ:
- **I disabled a stage and now nothing advances.** Check `GET /api/v1/status/{entity}/validate`: if the pipeline
  was left without an enabled initial stage, the API itself rejected the change with 422 before saving it (it should not
  be left in that state).

---

### 5.1 “Statuses” screen (System → Catalogs → Statuses, 2026-10-21)

Until now there was nowhere to see or change the stages from the application (only the API). The **Statuses** screen (“Statuses” button at the top of **System → Catalogs**, route `/system/statuses`; whoever has `catalogs.view` can open it, whoever has `admin.statusconfig` can change it) shows, for the domain you choose (client, shipment, route, receipt…):

| Column | What it is |
|---|---|
| **Order** | The stage's place along the path. |
| **Code** and **Name** | The fixed code and the name people see (with a “custom name” if the company changed it). |
| **Type** | **Regular stage** (`PIPELINE`: the path walked in order), **Side step** (`LATERAL`: a reversible step aside, e.g. Suspended) or **Final** (`TERMINAL`: where it ends, e.g. Delivered); and **Initial** (where new records start). |
| **In use** | Switch: turning a stage off stops offering it (`PUT /api/v1/status/{entity}/{code}/override` with `isEnabled`). |

Actions per row: **Move up** and **Move down** (swap the order with the neighboring stage; `sortOverride`) and **Rename** (Spanish, and optional English; `labels`). On save, the server validates the whole flow and, if it would end up invalid, responds with the reason and does not save (“Invalid pipeline: …”). Above the table the validator's result appears with the current errors.

**What the validator requires:** an enabled **initial** stage, always; an enabled **terminal** stage and an enabled **pipeline** stage **only if the domain defines them** (since 2026-10-21; before, the client status, which has no terminal stage, always showed “At least one terminal stage must remain enabled” and nobody could fix it).

| Message | HTTP | What to do |
|---|---|---|
| “Invalid pipeline: At least one initial stage must remain enabled.” | 422 | Do not turn off the domain's initial stage. |
| “Invalid pipeline: At least one terminal stage must remain enabled.” | 422 | The domain has a terminal stage and an attempt was made to turn off the last one; leave one on. |
| “Invalid pipeline: At least one pipeline stage must remain enabled.” | 422 | Leave at least one regular stage on. |
| “Write the name in Spanish.” | (screen) | The name in Spanish is required. |

## 6. Contact points (ContactPoint)

What it does: stores several contacts (phone, email, etc.) for any system entity that needs them
(today: users; each business module registers its own). Ownership (that the `ownerId` exists and belongs to the
active tenant) is validated against a “resolver” per entity type, not through a direct foreign key.

Who can: view, the read permission of the owning entity (CLIENT/CLIENT_CONTACT → `clients.read`, LOCATION →
`locations.read`, CONTRACT → `contracts.read`, PORTAL_USER → `portalusers.manage`, TRANSPORT_ORDER → `orders.view`; USER →
any authenticated user); without it, `Missing permission '<código>'.` (403, a PERMISSION_DENIED is recorded in the security log).
Create/edit/deactivate, `contacts.manage` **and** the edit permission of the owning entity (CLIENT/CLIENT_CONTACT →
`clients.update`, LOCATION → `locations.update`, CONTRACT → `contracts.update`, PORTAL_USER → `portalusers.manage`,
TRANSPORT_ORDER → `orders.edit`; USER → only `contacts.manage`). When adding, that permission is checked before looking up the
owner: without it the response is 403 whether the record exists or not (no 200/404 oracle).

How it is used:
- `GET /api/v1/contacts/{ownerEntity}/{ownerId}` (e.g. `ownerEntity=USER`)
- `POST /api/v1/contacts/{ownerEntity}/{ownerId}` `{ "contactType": "EMAIL", "value": "a@b.com", "isPrimary": true }`
- Phone: `{ "contactType": "PHONE", "value": "(787) 555-1234", "countryCallingCode": "1", "isPrimary": true }`
- `PUT /api/v1/contacts/{id}`
- `DELETE /api/v1/contacts/{id}` (deactivates, does not delete)

**Phones (2026-10-20): digits only and one international prefix per number.** The types `PHONE`, `MOBILE`, `FAX` and `WHATSAPP` are stored
with **only the digits** of the national number in `value` (no parentheses, hyphens or spaces) and with their **international prefix** in `countryCallingCode`
(digits only and without “+”: `1` for the United States and Puerto Rico, `593` for Ecuador). On save, the server strips the formatting and always fills in the prefix:
the one indicated; if the number starts with “+”, the one it carries; and otherwise, the **company's default prefix** (`phoneCountryCode` from Settings → Region &
formats). With prefix `1`, an 11-digit number starting with 1 loses that 1. The **mask** with which the number is displayed (`phoneMask`) belongs to the company and
is applied only when displaying; a number with another prefix is shown as “+593 991234567”. The extension is stored separately. To send an SMS the number is
built in E.164 (“+” + prefix + digits). The numbers that already existed were cleaned with an idempotent script in `Diseño/logistica-db-update.sql`
(Batch 162): those that carried text (for example “ext. 5”) or a “+” with a country other than the company's were left as they were, without a prefix, to be reviewed by hand.

Validations:

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| `ownerEntity` with no registered resolver | owning module not built yet | `There is no ownership resolver for '<ownerEntity>' (the owning module is not built yet).` | 400 |
| `ownerId` does not exist in the tenant | — | `<ownerEntity> '<ownerId>' not found.` | 404 |
| Empty value | — | `The value is required.` | 400 |
| Malformed email (type EMAIL) | must have a valid `@` and `.` | `Invalid email.` | 400 |
| Malformed phone (PHONE, MOBILE, FAX, WHATSAPP) | digits and formatting only (spaces, `-`, `(`, `)`, `.`), “+” only at the start; 7 national digits and up to 15 in total with the prefix | `Invalid phone.` | 400 |
| `countryCallingCode` outside 1 to 4 digits (or starting with 0) | — | `Invalid country prefix: use 1 to 4 digits, for example 1.` | 400 |
| Phone with “+” and `countryCallingCode` different from the one it carries | — | `The phone does not start with the indicated country prefix.` | 400 |
| Phone with “+” without a recognizable prefix | — | `A phone with “+” needs the country prefix in its field.` | 400 |
| Nonexistent contact | — | `Contact '<id>' not found.` | 404 |

---

## 7. Custom fields (Custom Fields)

Requires the **CUSTOM_FIELDS** module turned on (`[RequireModule]`); that module in turn depends on **ANALYTICS**.

What it does: allows adding your own fields to an entity (text, number, date, date and time, yes/no, single/multiple
selection list, or reference to a value list) without development. Each field can
be required, unique, have a default value, validation rules (JSON with `regex`, `min`, `max`,
`minLength`, `maxLength`) and be shown in the search (`showInList`: “Show in search”, see 7.x).

Who can: view definitions, any authenticated user; create/edit/deactivate/reactivate definitions,
`admin.customfields`. Viewing the **values** of a record requires the read permission of the owning entity and setting values
requires its edit permission: CLIENT/CLIENT_CONTACT → `clients.read` / `clients.update`, LOCATION → `locations.read` /
`locations.update`, CONTRACT → `contracts.read` / `contracts.update`, PORTAL_USER → `portalusers.manage`; without it,
`Missing permission '<código>'.` (403). For USER (and entities whose module does not exist yet) any authenticated user is enough.

How it is used:
- `GET /api/v1/custom-fields/definitions?entityType=USER`
- `POST /api/v1/custom-fields/definitions/{entityType}`
  ```json
  { "fieldKey":"cost_center","labels":{"es":"Centro de costo","en":"Cost center"},"dataType":"TEXT",
    "isRequired":false,"isUnique":true,"showInList":true,"validationJson":"{\"regex\":\"^CC-\\\\d{3}$\"}" }
  ```
- `PUT /api/v1/custom-fields/definitions/{id}`
- `DELETE /api/v1/custom-fields/definitions/{id}` (deactivates) / `POST .../restore`
- `GET /api/v1/custom-fields/values/{entityType}/{entityId}`
- `PUT /api/v1/custom-fields/values/{entityType}/{entityId}` `{ "values": { "cost_center": "CC-100" } }`

Data types (`dataType`): `TEXT`, `NUMBER`, `DATE`, `DATETIME`, `BOOL`, `SELECT`, `MULTISELECT`, `LOOKUP_REF`.

Validations:

| Field / case | Rule | Exact message | HTTP |
|---|---|---|---|
| `fieldKey` with invalid format | lowercase, digits, `_`, 2-60, starts with a letter | `Invalid key: lowercase letters, digits and '_' (2-60), starting with a letter.` | 400 |
| `fieldKey` repeated in the entity | — | `The field '<key>' already exists in <entityType>.` | 409 |
| Changing the type of a field with saved values | — | `The type of a field that already has saved values cannot be changed.` | 409 |
| Empty label | — | `The label is required.` | 400 |
| `LOOKUP_REF` without `refEntity` | — | `LOOKUP_REF requires the referenced list domain.` | 400 |
| `refEntity` does not exist | — | `The list '<refEntity>' does not exist.` | 400 |
| `SELECT`/`MULTISELECT` without options or `refEntity` | — | `A selection field needs options or a value list (refEntity).` | 400 |
| `validationJson` is not valid JSON | — | `ValidationJson is not valid JSON.` | 400 |
| Nonexistent definition | — | `Custom field '<id>' not found.` | 404 |
| Owning record does not exist in the tenant | — | `<entityType> '<entityId>' not found.` | 404 |
| Non-numeric NUMBER value | — | `A number was expected.` (per field) | 400 |
| Non-boolean BOOL value | — | `Yes/no was expected.` (per field) | 400 |
| DATE value with wrong format | — | `A date (yyyy-MM-dd) was expected.` (per field) | 400 |
| DATETIME value with wrong format | — | `An ISO date and time was expected.` (per field) | 400 |
| MULTISELECT value that is not a list | — | `A list of values was expected.` (per field) | 400 |
| Validation rule not met (regex/min/max/etc.), missing required, duplicate in a unique field | — | message generated per field, grouped in `errors` | 400 |

Changes of batch F-F1 (2026-10-12) and clarifications about the above:

| Case | Rule | Exact message | HTTP |
|---|---|---|---|
| Defining fields for an entity without an ownership resolver (e.g. `INVOICE`) | there is nowhere to store values | `The entity '<entityType>' does not support custom fields yet (its module is not built).` | 400 |
| Setting values on an entity without a resolver | before, they were accepted for any id, whether it existed or not; now it is rejected | the same message | 400 |
| `refEntity` that is a status domain (Scope=Status) | only value lists (Scope=Lookup) work | `The list '<refEntity>' does not exist.` | 400 |

Clarifications: the key is trimmed and lowercased before being validated; **the key of a deactivated field remains taken** (the 409 `The field … already exists` also applies); `defaultValue` is text (MULTISELECT: comma-separated codes; BOOL: `true`; DATETIME: ISO UTC instant) and is not validated against the list; `validationJson` only evaluates `regex`/`pattern`, `minLength`/`maxLength` (text) and `min`/`max` (number; for dates, days relative to today); `DELETE` deactivates (204) and the values are kept; the administration screen is Analytics → Custom fields (screen manual `frontend/ff1-campos-personalizados.md`).

FAQ:
- **I saved a value and it returned 400 with several fields.** The `errors` body carries, for each `fieldKey` that
  failed, the specific message (validation rule, required, or duplicate if the field is unique).

---

### 7.x Custom fields in the search and in Views (Batch 130)

> Dictated by Luis (2026-10-18). The old “Show in lists” (`showInList`) was renamed **“Show in search”**: it is the ONLY option of the field for this.

**With “Show in search” turned on**, the entity's list offers a filter with that field, **of the field's type**:

| Field type | Filter on the screen | API parameter |
|---|---|---|
| Text | text box (contains) | `cf.clave=texto` |
| Number | “X (from)” and “X (to)”, both inclusive | `cf.clave.from=` · `cf.clave.to=` (equal: `cf.clave=`) |
| Date and date/time | suggested ranges (Today, This week, This month…) or Custom; the end day counts | `cf.clave.from=AAAA-MM-DD` · `cf.clave.to=` |
| Yes/No | Yes / No list | `cf.clave=true` or `false` |
| List, catalog list | list of options (active ones only) | `cf.clave=CODIGO` |
| Multiple selection | list of options; returns the records that have that option (whole, not by pieces) | `cf.clave=CODIGO` |

- **Where:** Orders, Clients, Consignees (locations), Drivers and Vehicles (Fleet). “Clear” removes them. Between different filters, all must be met.
- **API:** the same lists (`GET /orders`, `/clients`, `/locations`, `/drivers`, `/vehicles`) accept `cf.<clave del campo>` (the key in lowercase). Permission: the one to read that list; no changes.
- **Errors (400):** “The custom field 'X' does not exist.” · “The custom field 'X' is not enabled for the search.” (the field exists but does not have “Show in search”) · “The value of the filter 'X' is not valid.” (badly written number or date; yes/no other than `true`/`false`).
- **Changing the option:** turning “Show in search” on or off in Analytics → Custom fields shows or hides the filter instantly (the field list is kept for 10 minutes in the browser: reload the screen).

**In Views (custom reports):** EVERY data source of an entity with custom fields offers them as **columns and filters** (`cf.clave`), **with no need for “Show in search”**. The column comes out with the field's label (in the viewer's language) and its real type (number, yes/no, date or text), so it sorts, sums and filters as such.

## 8. Views, indicators, charts and Day pulse (Analytics)

Requires the **ANALYTICS** module. This whole block also requires the base permission `analytics.view` (since the
controller declares it at class level); creating or editing requires `analytics.manage`.

### 8.1 How the engine works

Data sources (`IDataSource`) are registered per module with an `EntityType` key (today about 26: `AUDIT_LOG`,
`SECURITY_EVENT`, `USER`, `CLIENT`, `TRANSPORT_ORDER`, `TRIP`, `PRODUCT`, `STOCK_BALANCE`, `RENTAL`…; `GET /data-sources` returns only the
ones the user can read: module turned on and permission to view that module). Each source can declare a `DateField` (to allow filtering by
date range) and relations toward other sources (“secondary”, to combine many-to-one, e.g. bring
`User.FullName` into an `AUDIT_LOG` view). Filters use a **DSL of their own** (not SQL) with operators
`eq, ne, gt, gte, lt, lte, contains, startsWith, endsWith, in, notIn, between, isNull, notNull, isTrue, isFalse`
combinable with `and/or/not`. The engine evaluates everything in memory, over a cap of 20,000 rows per source.

`GET /api/v1/analytics/data-sources` lists the available sources with their fields, relations and custom
fields.

### 8.2 Views (reports)

What it does: a configurable table (columns, grouping, sort, filters, combination with another source) that can
be saved, shared and run.

How it is used:
- `GET /api/v1/analytics/reports?baseEntityType=AUDIT_LOG`, `GET .../reports/{id}`
- `POST /api/v1/analytics/reports/{baseEntityType}` (`analytics.manage`)
  ```json
  { "name": "Actividad diaria", "columns": ["CreatedAtUtc","Action","User.FullName"], "secondary": ["User"] }
  ```
- `PUT /api/v1/analytics/reports/{id}` / `DELETE /api/v1/analytics/reports/{id}` (`analytics.manage`)
- `POST /api/v1/analytics/reports/{id}/run` — runs the saved view
- `POST /api/v1/analytics/reports/{baseEntityType}/preview` — preview without saving, useful in the builder

Validations:

| Field / case | Exact message | HTTP |
|---|---|---|
| Date range with invalid mode | `Invalid range mode.` | 400 |
| `CUSTOM` mode without both dates | `The custom range needs From and To.` | 400 |
| `dateFrom` > `dateTo` | `From cannot be later than To.` | 400 |
| Base source not registered | `The source '<key>' is not registered as an EntityType.` | 400 |
| Empty name | `The name is required.` | 400 |
| View name repeated in the source | `A view '<name>' already exists for <source>.` | 409 |
| No columns and no grouping | `A view needs at least one column or a grouping.` | 400 |
| Secondary source not combinable | `The source <source> does not combine with '<sec>'.` | 400 |
| `filterJson` is not valid JSON | `FilterJson is not valid JSON.` | 400 |
| Nonexistent view or not visible to the user | `View '<id>' not found.` | 404 |
| Editing/deleting a system element (`IsSystem`) | `The platform's default elements are not edited or deleted.` | 403 |
| Editing/deleting without being the owner or having it shared with edit rights | `Only the owner can edit or delete this element.` | 403 |
| Sharing with a user/role that is not from the tenant | `The user <id> does not belong to this company.` / `The role <id> does not belong to this company.` | 400 |

Format of what a view stores (built by the **Views & reports** screen, screen manual `frontend/ff2-vistas.md`):

| Field | Format |
|---|---|
| `columns` | keys `"Campo"` (main source), `"REL.Campo"` (combined source; `REL` is the relation key, e.g. `Client.Name`) or `"cf.<clave>"` (custom field of the main source). With grouping it may be empty |
| `secondary` | relation keys of the source (`["Client"]`); only those that `GET /data-sources` lists in `relations` |
| `groupJson` | `{"by":["Campo",…],"aggregates":[{"fn":"COUNT"|"SUM"|"AVG"|"MIN"|"MAX","field":"Campo"}],"totals":true}`. `COUNT` may go without `field`; the others require a numeric field (`The SUM function requires a field.` when running the view, not when saving it). The column of each aggregate is named `<fn in lowercase>_<field>` (`count_rows` without a field) |
| `sortJson` | `[{"field":"Campo","dir":"asc"|"desc"}]`; with grouping, the field is a group or aggregate field (`sum_TotalPieces`) |
| `filterJson` | DSL of 16 operators (the server is case-insensitive on the operator: system views carry `notnull`) |
| `/run` | body `{dateRangeMode,dateFrom,dateTo,skip,take}` (`take` 500 by default, maximum 5000); responds `{columns,rows,totals,total}`; `rows` also carries the source's other columns (the screen only uses those in `columns`) |

Changes of batch F-F2 (2026-10-12), found while building the screen:

| Case | Before | Now | HTTP |
|---|---|---|---|
| Creating a view with the name of a **deleted** one (deletion is logical and `UQ_ReportDefinition` also counts the deleted ones) | `Internal error.` | it is created; the deleted one is left as `nombre (eliminada #id)` | 200 |
| **Renaming** a view to the name of another active one of the same source | `Internal error.` | `A view '<name>' already exists for <source>.` | 409 |
| `chartType` of a view | was ignored (always null) | it is saved and returned; an unknown one gives `Chart type: BAR, DONUT or LINE.` | 200 / 400 |

Clarifications: the email schedule (`ScheduleCron`, `DeliveryEmails`, `DeliveryRangeMode`) is described in **§8.2.1** and is sent by the `REPORT_DELIVERY` job; the `PUT` replaces those three fields, so whoever edits through the API must resend them (or `null` to remove the schedule). The only export routes are the browser's: there is no view export endpoint.

#### 8.2.1 Send a view by email (Batch 92)

A view of your own (not a system one) can be **scheduled** to arrive by email to one or more people: in the view builder, block **“Send by email”** (permission `analytics.manage`).

| Field | What it is | Validation (message and code) |
|---|---|---|
| Frequency, day and time | Every day, once a week (day 0 Sunday to 6 Saturday) or once a month (day 1 to 28), at a **company** time. It is stored in `scheduleCron` as a restricted cron: `M H * * *`, `M H * * D`, `M H D * *`. | 400 `The schedule must be daily, weekly (day 0 to 6) or monthly (day 1 to 28) with a valid time.` |
| Recipients (`deliveryEmails`) | Up to 10 emails separated by comma or semicolon; duplicates are removed. | 400 `The email '{correo}' is not valid.` · 400 `At most 10 recipient emails.` |
| Schedule and recipients | They go together: there is no one without the other. | 400 `A schedule needs at least one recipient email.` · 400 `The recipient emails need a schedule.` |
| Period (`deliveryRangeMode`) | Last 7 days (by default), Last 30 days, This month or All history. | 400 `The period of the sent report must be Last 7 days, Last 30 days, This month or All.` |

**How it is sent.** The scheduled job `REPORT_DELIVERY` (System → Scheduled jobs, every 5 minutes, active by default) checks the views with a schedule. When a view's time passes, it **runs it as its owner** —with the owner's permissions, visibility and warehouse and client data scope— and sends each recipient an email titled `{Compañía} · {Vista} · {fecha}`, the period, the table (up to **200 rows**, with a note if there are more) and the totals row. A view is sent **once per occurrence**: if the server was off and several were missed, a single one goes out with the data as of now. When saving or changing the schedule, the counter starts at that moment (a time that already passed is not triggered).

**Result.** Each view stores its last delivery: **Sent** (to how many and how many rows), **Skipped** (email is not configured on the platform, or the owner is no longer active) or **With errors** (who it could not be sent to, or why it could not be run). It is shown in the same block. A view that fails does not prevent the others.

**Send now.** On a saved view, the **Send now** button (`POST /api/v1/analytics/reports/{id}/deliver`, `analytics.manage`) sends the view at that moment to its recipients and leaves the result; it **does not move** the schedule. The owner or whoever it is shared with edit rights can use it. Errors: 400 `System reports are not scheduled; save a copy of your own.` · 400 `The report has no owner to run it.` · 400 `The report has no schedule and no recipient emails.` · 403 `You do not have permission to send this report.` · 404 if the view does not exist.

### 8.3 Indicators

What it does: an aggregated number (count, sum, average, etc.) over a source, with its own date range.

How it is used: `GET /api/v1/analytics/indicators`, `GET .../{id}`, `GET .../{id}/value`,
`POST /api/v1/analytics/indicators` (`analytics.manage`), `PUT`/`DELETE` likewise.

Date range and Pulse, two levels:
- **Default range/Pulse of the definition** (affects everyone): `PUT .../{id}/default-date-range`. The owner
  can change it freely; if it belongs to someone else or is a system one, it requires the permission `analytics.dates`.
- **Personal preference** (does not touch the definition): `PUT .../{id}/my-date-range` and `PUT .../{id}/my-pulse` —
  any user can set their own range or whether they want to see it in their Pulse, without a special permission.

Additional validations: empty name → `The name is required.` (400); repeated name →
`The indicator '<name>' already exists.` (409); changing the default range without being the owner/system and without the permission →
`Changing the default range of someone else's or a system indicator requires 'analytics.dates'.` (403); nonexistent
indicator/not visible → `Indicator '<id>' not found.` (404).

### 8.4 Charts

What it does: same as an indicator but grouped, shown as bar/donut (top 8 categories) or line (chronological
series, last 30 points).

How it is used: same verbs as indicators at `/api/v1/analytics/charts`, plus `GET .../{id}/data` for the
points to plot.

Validations: `chartType` must be `BAR`, `DONUT` or `LINE` (`Chart type: BAR, DONUT or LINE.`, 400);
`groupByField` required (`The grouping field is required.`, 400); aggregation function other than
`COUNT` without a field (`The <fn> function requires a field.`, 400); field that does not exist in the source
(`The source <source> does not have the field '<field>'.`, 400); empty/repeated name, same as indicators but with
`chart` (`The chart '<name>' already exists.`, 409).

### 8.5 Day pulse

What it does: `GET /api/v1/analytics/pulse` brings the indicators and charts the user marked to see daily
(or that come that way by default) and that they can see according to their visibility (TENANT by default, PRIVATE or explicitly
shared).

---

## 9. Audit and security

This whole block requires the permission `admin.audit`.

What it does: automatically records changes to entities marked `[AuditEntity]` (change log,
`AuditLog`), access/security events (`SecurityEvent`: login, lockout, MFA, permission denied, etc.), and
offers an “activity” view that combines both with text search, as well as exporting it to CSV.

How it is used:
- `GET /api/v1/audit/changes?entityType=&entityId=&userId=&from=&to=&skip=&take=`
- `GET /api/v1/audit/security-events?eventType=&userId=&from=&to=&skip=&take=`
- `GET /api/v1/audit/activity?kind=all|changes|security&text=&from=&to=&skip=&take=`
- `GET /api/v1/audit/activity/export.csv` (same filters) — downloads `actividad-YYYYMMDD-HHmm.csv`

Each change row carries `correlationId`: the same value the `X-Correlation-Id` header carried in the
call that produced the change, useful to reconstruct “what happened in that request”.

Entities that **do not** generate a change log by design (they have no `[AuditEntity]`): `RefreshToken`,
`MfaRecoveryCode`, `AuditLog`, `SecurityEvent`, `EntityStatusHistory`, `CustomFieldValue`,
`UserAnalyticsPreference`.

Batch 8A — warehouse devices in the log:
- A device login or a query of its users rejected because the device does not exist, its secret is not valid, it is
  deactivated or the company lost the WMS module is recorded as `LOGIN` with result `FAILURE` and detail `stage = device`,
  `action` (`login` or `users`) and `reason` (`device_invalid`, `device_inactive` or `tenant_unusable`). If the device
  exists, the event is recorded in its company; if not, with no company (just like a login with a nonexistent email).
- Enrolling a device (`POST /api/v1/devices/enroll`) is recorded as `API_CREDENTIAL`: `SUCCESS` with `action =
  device_enrolled` if the code was valid, `FAILURE` with `action = device_enroll` if the code was not valid, expired or
  lost the race against another simultaneous enrollment with the same code (same event in the three cases, without
  distinguishing them, so as not to give clues).
- Deactivating, reactivating or re-enrolling a device that had live sessions writes `TOKEN_REVOKED` with the reason
  (`device_deactivated`, `device_reactivated` — always, even if there was no live session —, `device_enrolled` or
  `pin_changed`/`pin_removed` when what is revoked is due to a new PIN).
- The device's anonymous flows (enroll, user list, login and heartbeat) never take the user from a token that
  comes in the request: the device's log (`USER_DEVICE` change of the enrollment, `TOKEN_REVOKED`, enrollment event)
  is recorded with no user.

Batch F10 — the “Activity” view (`GET /api/v1/audit/activity`), used by the Security & audit screen:
- `total` is the **real count** of the two logs with the applied filters (before, it came from a window of `skip + take`
  rows of each one and grew when paging). Pages continue without repeating or skipping rows; the order is by date,
  most recent first. `take` goes up to 2000.
- `text` is searched in the database, case-insensitively: in the detail (the JSON of the log or of the event), the IP, the name or
  the email of the user, the label or the code of the event type, of the result, of the action and of the entity (for example
  `Permiso`, `LOGIN`, `Compañía`), and the entity number (`#12` or `12`). It no longer searches the combined text
  “Action · Entity #id” (for example, `Login · Éxito` finds nothing; `Login` does).
- Each row also carries `typeCode` (code of the change action —`CREATE`, `UPDATE`, `DELETE`, `RESTORE`— or of the event
  type —`LOGIN`, `PERMISSION_DENIED`…—) and `outcomeCode` (result of the event: `SUCCESS`, `FAILURE`, `BLOCKED`; empty in a
  change). `/audit/changes` carries `actionCode` and `/audit/security-events` carries `eventTypeCode` and `outcomeCode`. The screen
  marks a failed or blocked event, a denied permission or an account lockout as “Alert”; the rest as “Event”; the
  change log as “Change”.
- The server's `export.csv` does not change (up to 2000 rows, columns `Cuando,Tipo,Usuario,Detalle,IP,Correlacion`); the screen
  exports its own CSV with what is filtered (see the F10 screens chapter).

### 9.1 Sessions of the whole company (Batch F10)

What it does: lists the active sessions (live refresh tokens of the active company) of **all** users —not just
your own like `GET /auth/sessions`— and allows revoking one or all the others. It is the “Active sessions” table in Security &
audit → Sessions & MFA (master document, module D: “revocable sessions… revocation per device or global”).

Who can: list, `admin.audit`; revoke, also `admin.users`. “Sign out other sessions” also requires
**recent reauthentication** (AAL2): without it, 403 `aal2_required` and the web app asks for the password and retries.

How it is used:
- `GET /api/v1/audit/sessions` → array of `{ id, userId, userName, userEmail, deviceInfo, deviceName, isDevice, ipAddress,
  lastActivityUtc, expiresAtUtc, isCurrent }`, from the most recent to the oldest. `lastActivityUtc` is the last renewal
  of the session (it happens every time the access token expires, 15 minutes, while in use). `isDevice`/`deviceName`: session of
  a warehouse device (“ZEBRA-01 · Muelle 1”). `isCurrent`: the session of whoever is querying.
- `DELETE /api/v1/audit/sessions/{id}` → 204: revokes that session (of any user in the company).
- `POST /api/v1/audit/sessions/revoke-others` → `{ "revoked": N }`: revokes all the company's active sessions except
  that of whoever requests it.

Effects: the revoked session can no longer be renewed; the access token it already had stays alive until it expires (15 minutes at
most; the user's security stamp is not changed, which would also close their sessions in other companies). Each revocation
writes `TOKEN_REVOKED` (one: attributed to the session's owner, detail `scope = company_session`, `session` and `by` = whoever
revoked it; the others: attributed to whoever requested it, `scope = company_others`, `count` and `users`). **Careful:** if the device or browser
of the revoked session tries to renew it, the server treats it as token reuse and revokes **all** the sessions of
that user (previous behavior from batch 1, same as when revoking from My account): the user signs in again on all their
devices.

| Case | Exact message | HTTP |
|---|---|---|
| Revoking your own session from the list | `The current session is not revoked from the list; use Sign out.` | 409 |
| Session that does not exist, already revoked, expired or from another company | `Session '<id>' not found.` | 404 |
| Without `admin.audit` (list) or without `admin.users` (revoke) | no body (the web app treats it as `forbidden`); the `PERMISSION_DENIED` event is recorded | 403 |
| “Sign out other sessions” without recent reauthentication | `This action requires recent reauthentication (AAL2).` (`code: aal2_required`) | 403 |

FAQ:
- **A user tried something without permission, is there a record?** Yes: every failed attempt due to a missing permission
  generates a `PERMISSION_DENIED` event (visible with `admin.audit` in `/audit/security-events` or `/audit/activity`).

---

## 10. Modules per tenant

What it does: each company turns on only the business modules it uses (for example, Advance uses last mile,
COD, warehouse with lot/serial and rental units, but not cross-dock or maritime). The menu and the endpoints
protected with `[RequireModule]` only work if the module is turned on.

List of 16 modules (`ModuleKeys`): `LTL_GROUND` (core), `COD`, `WMS_LOTSERIAL`, `CROSSDOCK`,
`RENTAL_EQUIPMENT`, `RENTAL_BILLING` (depends on `RENTAL_EQUIPMENT`), `MARITIME`, `CLIENT_PORTAL`,
`CUSTOM_FIELDS` (depends on `ANALYTICS`), `PURCHASING`, `CATALOG`, `ANALYTICS`, `SYSTEM` (core) and, since Batch 49,
`SALES_ORDERS` (Sales orders; depends on `WMS_LOTSERIAL`), `TAX` (Taxes / IVU; no dependencies) and `BILLING`
(Billing; depends on `SALES_ORDERS`). The three new ones **are only defined**: no company has them turned on
yet and they do not open any screen until they are built (taxes and sales). The **core** modules (`LTL_GROUND`,
`SYSTEM`) cannot be turned off. Notices (notifications) are not a module: they are always available under `SYSTEM`.

Who can: view the list, any authenticated user; turn on/off, `admin.tenant` **+ AAL2**.

How it is used:
- `GET /api/v1/modules`
- `PUT /api/v1/modules/{moduleKey}` `{ "isEnabled": true }`

Rules: turning on requires that the dependency (if any) is already on; turning off a module cascades and turns off
those that depend on it; a module without a row for the tenant is considered off (safe default).

Validations:

| Case | Exact message | HTTP |
|---|---|---|
| Unknown module | `Module '<moduleKey>' not found.` | 404 |
| Turning on without the dependency on | `'<módulo>' depends on '<dependencia>', which must be turned on first.` | 409 |
| Turning off a core module | `'<módulo>' is a core module and cannot be turned off.` | 409 |
| Endpoint of a module that is off | `The module '<moduleKey>' is not enabled for this company.` | 403 |
| Without recent AAL2 | `This action requires recent reauthentication (AAL2).` | 403 |

FAQ:
- **I don't see the Custom fields / Analytics screen.** Check that the `ANALYTICS` module (and if applicable,
  `CUSTOM_FIELDS`) is turned on for your company in `GET /api/v1/modules`, and that your user has
  `analytics.view`.

---

## 11. Company settings (Tenant)

What it does: general tenant settings: name/legal name/tax ID, default language, work-day
mask, default cap of stops per route, default service/package types, whether MFA is
mandatory, AAL2 window, session duration in days, branding and holidays/work calendar.

Who can: view, any authenticated user; edit, `admin.tenant`.

How it is used:
- `GET /api/v1/tenant/settings`
- `PUT /api/v1/tenant/settings` (partial fields, e.g. `{ "aal2WindowMinutes": 60 }`)
- `GET /api/v1/tenant/holidays?year=2026`
- `POST /api/v1/tenant/holidays` `{ "date": "2026-01-01", "name": "Año Nuevo", "isRecurring": true }` — a date that already has an
  active holiday is **409** (`There is already a holiday on that date.`, also in `errors.date`) and does not overwrite the existing one; a holiday
  removed earlier (`DELETE`) can be added again on that date (it is reactivated with the new data).
- `DELETE /api/v1/tenant/holidays/{id}`
- `GET /api/v1/tenant/work-days?n=7` — last N business days (without weekends or holidays), for
  trends/SLA

Validations:

| Field | Rule | Exact message | HTTP |
|---|---|---|---|
| `defaultLangCode` | 2 letters | `2-letter language code.` | 400 |
| `workDaysMask` | 1-127 | `Invalid mask (1-127).` | 400 |
| `maxStopsPerRouteDefault` | ≥ 1 | `Must be ≥ 1.` | 400 |
| `aal2WindowMinutes` | 15, 30 or 60 (a different value already saved is kept if it is resent unchanged) | `The reauthentication window must be 15, 30 or 60 minutes.` | 400 |
| `sessionMinutes` / `deviceSessionMinutes` / `driverSessionMinutes` (session lifetime is **in minutes only**; unset means 30 days; since 2026-10-26 `sessionDays` and `deviceSessionDays` no longer exist) | 1 to 525600 minutes (365 days) | `The duration must be between 1 minute and 365 days (8760 hours).` | 400 |
| `phoneNumber` (2026-10-26; `""` removes it) | 7 to 15 digits; only digits, spaces, hyphens, dots, parentheses, `+` and `ext`; up to 30 characters | `The phone must have between 7 and 15 digits (it may contain spaces, hyphens, parentheses, “+” and “ext”).` | 400 |
| `address` / `postalAddress` (2026-10-26: address and mailing address; `postalSameAsAddress: true` removes the mailing one) | `line1` and `city` required; line up to 200, city and state up to 100, postal code up to 20; 2-letter `country` (`PR` if omitted) | `The address (line 1) is required.` / `The city is required.` / `It cannot exceed {max} characters.` (the error carries the field: `address.line1`, `postalAddress.city`…) | 400 |
| `name` (2026-10-26) | only the platform administrator changes it; it cannot repeat another company's; up to 200 characters | `Only the platform administrator changes the company name.` (403) / `A company with that name already exists.` (409) | 403 / 409 |
| `brandingJson` | the branding rules (section 11.2: size, JSON, fields, colors, contrast, hue) | those in the table of section 11.2 | 400 |
| `defaultManualIssueReason` (2026-10-11 b: “Default reason for manual issues”; `""` = no default; see [06 §7c](06-inventario-y-almacen.md#7c-default-reason-for-the-manual-issue-2026-10-11-b)) | code from the `ManualIssueReason` list, active and enabled for the company | `The reason {CÓDIGO} does not exist or is inactive.` (`errors.defaultManualIssueReason`) | 400 |
| Empty holiday name | — | `The name is required.` | 400 |
| Holiday date already registered | — | `There is already a holiday on that date.` | 409 |
| Nonexistent holiday | — | `Holiday '<id>' not found.` | 404 |

### 11.1 Region and formats of the company (2026-10)

What it does: stores **in the database** (`Tenant` table) the company's time zone, its currency and the formats used to
display dates, times, money, numbers and phones. There are only two regions, **Puerto Rico (`PR`)** and **United States (`US`)**;
the region brings a full set of default values and **each value can be changed separately**. If any value differs
from its region's set, the company is marked as *Custom* (`isRegionCustomized = true`).

- **The time zone decides “today”.** Dates are stored in UTC; “today”, local days and business days are counted in the company's
  zone. It is used by: per-day filters of the movement ledger and its summary, detection date of discrepancies, “what changed” of the cycle
  count, the “Warehouse today” band of the Pulse (which returns the zone it counted the days with in `timeZone`), the date ranges
  of views, indicators and charts, the “today” window of Recent activity, the “today” of the analytics sources and
  `GET /api/v1/tenant/work-days`. Since Batch 20 it is also used by **all screens and services with a business date**:
  orders (quote and current contract), routes and trips (route day, monitor, dispatch, departure scan), fleet and
  maintenance (documents about to expire, availability, work orders, programs), rates and contracts (current today, “cannot
  be earlier than today”), special services, drivers, products, purchases (default order date) and the date rules
  of custom fields (`min`/`max` in days relative to today). **What “today” means**: the day shown by the company's wall
  clock. In Puerto Rico at 9:00 p.m. on October 2 it is still October 2 even though in UTC it is already
  the 3rd; the day changes at local midnight. **Instants** (creation time, confirmation time, expiration of a session or of
  a code) do not change: they stay in UTC and the screen shows them in the company's zone. A zone change applies from the
  next request (with several server instances, within 10 minutes at most). Without a company in context (automatic
  processes, initial load) “today” is counted in the default zone, `America/Puerto_Rico`.
- **The language is not the region.** The interface language belongs to each user and only decides the language in which the names of
  days and months appear; the region decides the order, the separators, the time and the currency. `defaultLangCode` is only the language a
  new user starts with.
- **Phones:** they are stored with digits only and displayed with the company's mask (each `#` is a digit).

Who can: view, any user with a session (requires neither permission nor module); change, permission **`admin.tenant`** (core
module, always on). Changes are recorded in the company's change log (Security & audit → Changes,
entity `TENANT`, with the previous and the new value of each field).

How it is used (Tenant settings screen → **Region & formats** tab; the web screen arrives in the frontend phase):
- `GET /api/v1/tenant/settings` — brings the 14 fields and `isRegionCustomized`.
- `GET /api/v1/tenant/format-options` — the two regions with their default values and the allowed values of each field
  (for the screen's lists and for “Restore region values”).
- `PUT /api/v1/tenant/settings` — partial, like the rest of the settings (a field that is not sent does not change):
  - **Changing region without other fields** loads that region's full set: `{ "regionCode": "US" }` leaves the zone at
    `America/New_York` and everything else with the United States values.
  - **Changing region with fields** starts from the new region's values and the fields sent take precedence:
    `{ "regionCode": "PR", "timeFormat": 24 }` → Puerto Rico values with 24-hour time.
  - **A single field** (without `regionCode`, or with the same region) changes only that field: `{ "dateOrder": "DMY" }`.
  - **Restore region values:** send the region's set that `format-options` brings (sending only the current region does not
    change anything, because it is not a region change).
- `POST /api/v1/platform/tenants` accepts the same fields (section 12): without `regionCode` the company is born with Puerto Rico.

Values per region:

| Field | Meaning | Puerto Rico (`PR`) | United States (`US`) | Allowed values |
|---|---|---|---|---|
| `regionCode` | Region | `PR` | `US` | `PR`, `US` |
| `timeZoneId` | Time zone (IANA name) | `America/Puerto_Rico` | `America/New_York` | any zone the platform knows |
| `currencyCode` | Base currency (ISO 4217) | `USD` | `USD` | 3 letters |
| `currencySymbol` | Currency symbol | `$` | `$` | 1 to 3 characters |
| `currencySymbolPosition` | Symbol position | `B` (before: $1,234.50) | `B` | `B` before, `A` after |
| `currencyDecimals` | Money decimals | 2 | 2 | 0, 2, 3 |
| `dateOrder` | Date order | `MDY` (10/02/2026)* | `MDY` | `MDY`, `DMY`, `YMD` |
| `dateSeparator` | Date separator | `/` | `/` | `/`, `-`, `.` |
| `timeFormat` | Time | 12 (3:30 p.m.) | 12 | 12, 24 |
| `weekStartDay` | First day of the week | 0 (Sunday) | 0 | 0 Sunday, 1 Monday |
| `thousandsSeparator` | Thousands separator | `,` | `,` | `,`, `.`, space |
| `decimalSeparator` | Decimal separator | `.` | `.` | `.`, `,` (never the same as the thousands one) |
| `phoneCountryCode` | Phone country code | `+1` | `+1` | `+` and 1 to 4 digits |
| `phoneMask` | Phone mask | `(###) ###-####` | `(###) ###-####` | up to 30 characters with at least one `#` |

\* The MM/DD/YYYY order for Puerto Rico is a default value pending confirmation with the product owner.

Normalization: the region, the currency code, the symbol position and the date order are accepted in lowercase and
stored in uppercase; leading and trailing spaces are removed (except in the separators, where the space is a valid value).
The time zone is also accepted with its Windows name (`SA Western Standard Time`, `Eastern Standard Time`) and stored with
the IANA name of the company's region (`America/Puerto_Rico`, `America/New_York`).

Validations (400, `ValidationException`; the message comes in `title` and in `errors.<campo>`; if there is an error **nothing is saved**
from the request, not even the other settings that came along):

| Field (`errors.<campo>`) | Rule | Exact message | HTTP |
|---|---|---|---|
| `regionCode` | `PR` or `US` | `Unknown region: '<valor>'. Use PR (Puerto Rico) or US (United States).` | 400 |
| `timeZoneId` | not empty | `Indicate the company's time zone, for example America/Puerto_Rico.` | 400 |
| `timeZoneId` | known to the platform (max. 64) | `The time zone '<valor>' is not recognized by the platform. Use an IANA name, for example America/Puerto_Rico or America/New_York.` | 400 |
| `currencyCode` | 3 letters | `The currency code must be 3 uppercase letters (ISO 4217), for example USD.` | 400 |
| `currencySymbol` | 1 to 3 characters | `The currency symbol is required and 1 to 3 characters long, for example $.` | 400 |
| `currencySymbolPosition` | `B` or `A` | `The currency symbol position must be B (before the amount) or A (after).` | 400 |
| `currencyDecimals` | 0, 2 or 3 | `The currency decimals must be 0, 2 or 3.` | 400 |
| `dateOrder` | `MDY`, `DMY`, `YMD` | `The date order must be MDY (month/day/year), DMY (day/month/year) or YMD (year/month/day).` | 400 |
| `dateSeparator` | `/`, `-`, `.` | `The date separator must be '/', '-' or '.'.` | 400 |
| `timeFormat` | 12 or 24 | `The time format must be 12 or 24.` | 400 |
| `weekStartDay` | 0 or 1 | `The first day of the week must be 0 (Sunday) or 1 (Monday).` | 400 |
| `thousandsSeparator` | `,`, `.` or space | `The thousands separator must be comma (','), period ('.') or space (' ').` | 400 |
| `decimalSeparator` | `.` or `,` | `The decimal separator must be period ('.') or comma (',').` | 400 |
| `decimalSeparator` | different from the thousands one (the final result, counting the saved value) | `The thousands and decimal separators cannot be the same` | 400 |
| `phoneCountryCode` | `+` and 1 to 4 digits | `The phone country code must be '+' followed by 1 to 4 digits, for example +1.` | 400 |
| `phoneMask` | 1 to 30 characters without accents, with at least one `#` | `The phone mask must be 1 to 30 characters without accents and at least one '#' (each # is a digit), for example (###) ###-####.` | 400 |
| PUT without `admin.tenant` | — | `Missing permission 'admin.tenant'.` | 403 |

Frequent cases:
- *I want a decimal comma and a thousands period* (1.234,50): send both in the same request,
  `{ "thousandsSeparator": ".", "decimalSeparator": "," }`. Sending only one clashes with the other saved value and gives the 400 for
  equal separators (the screen swaps the other one by itself).
- *I changed the zone and “today” is still the same*: “today” only changes if in the new zone it is already another day; the zone applies from the
  next request (on a server with several instances, within 10 minutes at most).
- *The company appears as Custom*: some field differs from its region; “Restore region values” brings it back.

---

**How a date or a time is typed (2026-10-21).** All date fields in the web app are the same control and follow this format (**Region & formats**):
- **When entering the field** (with the mouse, a finger or the keyboard) **the calendar opens by itself**, and you can keep typing. The calendar button on the right opens and closes it; **Esc** closes it; the ↓ arrow enters the calendar and the arrows, **PgDn** and **PgUp** move the days and the months; the **Today** and **Clear** shortcuts are at the bottom. The calendar starts the week where the company says and the days outside the field's limits (for example “not before today”) cannot be chosen.
- **Numbers only:** no need to type slashes or hyphens; the field adds them by itself with the **company's order and separator** (Puerto Rico: `10212026` → `10/21/2026`). A day that does not exist (February 30) is marked in red and is not accepted.
- **Pasting a date:** if it is understood (the company's order with any separator, or the `2026-10-21` format), it is changed to the company's format; if it is not understood (2-digit year, text, a month that does not exist…), it is **rejected**: the field stays as it was and warns `That date is not understood: use MM/DD/YYYY.`.
- **Date and time:** the date goes with its calendar and, when completed, the cursor moves to the **time**, which is typed separately with numbers only (`0930` → `09:30`; a `9` is completed to `09`). With 12-hour time there is an **AM/PM** selector. A pasted time (`9:30 pm`) is changed to the format; if it is not understood, it is rejected.
- A date that exists but falls outside the field's limits is **marked in red** and the screen shows its own message (for example “The date cannot be in the future”).
- The mobile apps will use this same control (same format rules) when they have date fields.

### 11.2 Company branding: colors and logos (Batch 19)

What it does: the company chooses the **color theme** of its interface (one of 14 predefined ones —the **Advance Logistics** one, navy blue, orange and gray, is the default— or its own colors) and uploads its **logos**.
The colors are stored in `Tenant.BrandingJson`; the four logos, in the `TenantBrandLogo` table of the database. The server **validates
the branding with the same rules and the same numbers as the screen** (an invalid theme entered through the API would leave the interface
unreadable for the whole company) and validates the **real content** of each logo.

Who can: **view** the branding and the logos, any user with a session (it is the interface's branding; requires neither permission nor module);
**change them**, permission **`admin.tenant`** (core module, always on). Changes are recorded in the change log
(entity `TENANT` for the branding and `TENANT_LOGO` for the logos, with who, when, type and size; **never the file**).

#### Colors (`brandingJson`)

Shape: `{ "preset": "bosque", "useCustom": false, "custom": { "flow": "#F37123", "action": "#F37123", "money": "#6C757D", "neutral": "#03204B" } }`.
The fields are optional (by default: `advance` theme, no custom colors); with `useCustom: true` **three roles per element** take over
(Batch 138): `custom.flow` = **primary** (bars, navigation, tabs), `custom.action` = **action** (main buttons, links and
focus; optional: without it, it is the same as the primary), `custom.money` = **accent** (chips, marks, counters and money: COD,
billing, settlement) and `custom.neutral` (base tone, from which backgrounds, panels, lines and text come; its saturation is
capped at 0.45). The text over each brand color is chosen by the system: white if it gives AA contrast (4.5:1) and, if not, dark navy
blue (an orange button does not get white letters). Themes: `advance` (default), `teikem` (blue and orange), `marino`, `acero`, `carretera`, `granate`, `vino`, `bosque`, `selva`,
`oliva`, `turquesa`, `indigo`, `violeta`, `grafito`. State colors (green, amber, red) are **not** customizable. An
empty `brandingJson` (`""`) removes the branding (the default one, Advance Logistics, comes back).

It is checked in this order and **the first failure** is reported (400, field `brandingJson` in `errors`, the message also in `title`):

| Rule | Exact message |
|---|---|
| More than 4096 characters | `The branding is too large (maximum 4096 characters); logos are uploaded separately.` |
| Not JSON | `The branding is not valid JSON.` |
| The root is not an object (array, text, number, `null`) | `The branding must be a JSON object.` |
| Field that does not belong to the branding (`<campo>` or `custom.<campo>`) | `The branding carries an unknown field: '<campo>'.` |
| State color (`ok`, `warn`, `danger`, `info`, `status`, `statusColors`, in any casing) | `State colors (ok, warn, danger, info) cannot be customized: '<campo>'.` |
| Wrong type (`preset` is not text, `useCustom` is not true/false, `custom` is not an object, a color is not text) | `The field '<campo>' has an invalid type.` |
| Color that is not hexadecimal `#RGB` or `#RRGGBB` (the `#` is optional; no spaces) | `The color 'custom.<flow\|money\|neutral>' is not hexadecimal (use #RGB or #RRGGBB).` |
| Predefined theme that does not exist (case-sensitive) | `The predefined theme '<id>' does not exist.` |
| Insufficient contrast against the theme's own panel: text ≥ 7:1; dimmed text, operation color and money color ≥ 4.5:1; in dark mode and then in light | `The contrast <of the text\|of the dimmed text\|of the operation color\|of the money color> in <dark\|light> mode is <r>:1; the minimum is <m>:1.` |
| The two accents (primary and accent) less than 40° of hue apart | `The operation and money colors are too similar: <d>° apart and the minimum is 40°.` |

The 14 predefined themes pass all the checks. The custom action color is checked like the others (≥ 4.5:1 against the panel, in both modes: `The contrast of the action color in <dark|light> mode is <r>:1; the minimum is 4.5:1.`) and only when it differs from the primary. With `useCustom: false` the `custom` colors are checked for their format
but do not enter the contrast check (the theme takes over).

#### Logos (`/api/v1/tenant/brand/logos`)

Four **slots**: `lockup` (full lockup for light background), `lockup-inverted` (for dark background), `mark` (square mark
for light background) and `mark-inverted`. The interface uses the inverted variant in the dark theme and the normal one in the light theme; if the
company only uploaded one of the two, it is used in both themes; with none, the Teikem logo. The square mark does not replace the
lockup (nor the other way around): without `mark`, the collapsed sidebar shows the Teikem symbol.

- `GET /api/v1/tenant/brand/logos` — the logos the company has (without the file): `slot`, `contentType`, `sizeBytes`, `eTag`
  (SHA-256 of the file) and `updatedAtUtc`. Session only.
- `GET /api/v1/tenant/brand/logos/{slot}` — the file. Session only (that is why the web app downloads it with the token and shows it as an
  image). Headers: `ETag` and `Last-Modified` (with `If-None-Match` it responds **304**), `Cache-Control: private, no-cache`,
  `X-Content-Type-Options: nosniff` and `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox`
  (the file executes nothing, even when opened directly).
- `PUT /api/v1/tenant/brand/logos/{slot}` — uploads or replaces (`admin.tenant`). `multipart/form-data` with the file in the
  `file` field. Responds with the saved logo.
- `DELETE /api/v1/tenant/brand/logos/{slot}` — removes the logo (`admin.tenant`; logical deletion, the binary is freed). 204.

Formats **SVG, PNG, JPG and WebP, up to 512 KB (524,288 bytes)**. The type is decided by the **content** (header and structure),
never by the extension nor by the type the browser declares; if an image of another type is declared, it is rejected.

| Case | Exact message | HTTP |
|---|---|---|
| Slot that does not exist | `Logo slot '<slot>' not found.` | 404 |
| Reading or removing a slot without a logo | `Logo '<slot>' not found.` | 404 |
| No file (`file` field missing or empty) | `Select a logo file.` | 400 |
| More than 512 KB (rejected by the declared length, or while reading if it is not declared, without receiving the rest) | `The logo exceeds the 512 KB maximum size.` | 413 |
| Not SVG, PNG, JPG or WebP (text, PDF, GIF, HTML, UTF-16…) | `Unsupported format: the logo must be SVG, PNG, JPG or WebP.` | 415 |
| Declares an image type different from the real one | `The file content (<real>) does not match the declared type (<declarado>).` | 415 |
| The request is not `multipart/form-data` | (no body; routing rejects it) | 415 |
| Truncated or damaged PNG, JPG or WebP | `The file is damaged or incomplete and cannot be used as a logo.` | 400 |
| Broken or cut-off multipart upload | `The logo upload arrived incomplete or malformed. Try again.` | 400 |
| SVG that is not valid XML | `The SVG is not valid XML.` | 400 |
| SVG with `DOCTYPE` or entities | `The SVG is not accepted: it declares DOCTYPE or entities.` | 400 |
| SVG with an active element: `script`, `foreignObject`, `iframe`, `frame`, `frameset`, `object`, `embed`, `applet`, `link`, `meta`, `base`, `audio`, `video`, `canvas`, `set`, `animate` | `The SVG is not accepted: it contains the element <nombre>, which can execute code or load external content.` | 400 |
| SVG with an `on…` attribute (`onload`, `onclick`…) | `The SVG is not accepted: the attribute '<atributo>' executes code.` | 400 |
| SVG with `javascript:` or `vbscript:` in an attribute (even if disguised with spaces or line breaks) | `The SVG is not accepted: the attribute '<atributo>' contains a script link (javascript:).` | 400 |
| SVG whose `href`/`src` points outside the file (only `#id` and `data:image/png\|jpeg\|webp\|gif;base64,…` are allowed) | `The SVG is not accepted: the attribute '<atributo>' points outside the file (only internal #id references are allowed).` | 400 |
| SVG with a style sheet (`<style>` or `style=""`) that imports or references external content (`@import`, `url()` other than `#id`/embedded image, `expression()`) | `The SVG is not accepted: a style sheet imports or references external content.` | 400 |

Without permission, `PUT` and `DELETE` respond 403; without a session, everything responds 401. Each company only sees, reads and removes **its own** logos (the
`TenantId` comes from the session; another company's respond as if they did not exist).

Frequent cases:
- **I uploaded a PNG and it says the format is not supported.** The content is not a PNG (for example, a renamed file, or a badly converted
  GIF/WebP). Open it and save it again as PNG.
- **My designer's SVG is not accepted.** SVGs from Illustrator, Figma or Inkscape almost never carry active content; the message
  says which element or attribute caused it (for example an `<image>` with an external link, or a `<style>` with an `@import` of fonts).
  Embed the image (`data:image/png;base64`) or convert the texts to outlines.
- **I changed the logo and another user sees the old one.** Each screen checks the logos when it opens and when returning to the window; reloading
  the page brings it right away.


**Logo on the sign-in screen (2026-10-22).** Before signing in, the sign-in screen shows a company's logo, in this order: **(1)** the **last company that computer signed in with** (by login or when switching it in the header dropdown; it is remembered in the browser), **(2)** the company that has as **custom domain** the address the page was opened with (`logistica.miempresa.com`), **(3)** the one that has its first part as **subdomain** (`depot` in `depot.teikem.com`) and **(4)** the Teikem logo. Only the name and the logos are shown (nothing else about the company). The domain and the subdomain are set **only by the platform administrator**: `PUT /api/v1/platform/tenants/{publicId}/domains` with `{ "customDomain": "...", "subdomain": "..." }` (empty = remove it; see `GET /api/v1/platform/tenants`). Errors: **400** “The domain is not valid. Write it as logistica.miempresa.com, without https:// or paths.” / “The subdomain is not valid. Use only lowercase letters, numbers and hyphens (for example depot).”; **409** “That domain is already used by another company.” / “That subdomain is already used by another company.”; **404** if the company does not exist. Public read (no session, 120 per minute per IP): `GET /api/v1/public/brand` and `GET /api/v1/public/brand/logo/{slot}`; an inactive company does not show its branding.

## 12. Platform administration (Teikem administrators only)

What it does: a user marked `IsPlatformAdmin` operates any tenant without needing a membership, has all
the permissions in any company, and their actions are attributed to their user in that company's audit/events
(visible to the tenant itself). Only they edit global lists and create new companies
(provisioning).

Who can: requires the `PlatformAdmin` policy (claim `IsPlatformAdmin=1` in the JWT), not a tenant permission.

How it is used:
- `GET /api/v1/platform/tenants` — list of companies
- `POST /api/v1/platform/tenants` (**requires AAL2**)
  ```json
  { "name": "Nueva Compañía", "adminEmail": "admin@nueva.com", "adminFullName": "Admin" }
  ```
  Creates the tenant, turns on the core and default modules (`LTL_GROUND`, `SYSTEM`, `CATALOG`, `ANALYTICS`,
  `CUSTOM_FIELDS`) plus those explicitly requested (with their dependencies), clones the 6 role templates, creates
  the administrator user with the `TenantAdmin` role, and clones the system views/indicators/charts.
  Region and formats (2026-10): accepts `regionCode` (`PR` or `US`; without a value, `PR`) and the same format fields of
  section 11.1; the company is born with its region's values and the fields sent take precedence over them. They are validated before
  creating anything, with the same messages (400) of section 11.1. Example: `{ "name": "Nueva Compañía", "regionCode": "US",
  "adminEmail": "admin@nueva.com", "adminFullName": "Admin" }`.

Validations:

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty name | `The name is required.` | 400 |
| Empty or invalid phone (2026-10-26) | `The company phone is required.` / the phone message in the settings table | 400 |
| Address without line 1 or city (2026-10-26) | `The address (line 1) is required.` / `The city is required.` (field `address.line1` / `address.city`; same for `postalAddress`) | 400 |
| `adminEmail` present but `adminFullName` missing | `The administrator's name is required.` | 400 |
| Invalid domain or subdomain, or already used by another company | the messages of the domains section | 400 / 409 |
| Repeated company name | `The company '<name>' already exists.` | 409 |
| Unknown requested module | `Unknown module: <key>.` | 400 |
| Invalid region or format | the messages of section 11.1 (e.g. `Unknown region: 'MX'. Use PR (Puerto Rico) or US (United States).`) | 400 |
| Invalid admin password (Identity) | Identity message | 400 |

## Company data and creating a company from Settings (2026-10-26)

**What changed.** Everything the company has can be seen and changed in **Company settings → General** (and a new company is created with the **New company** button on that same screen). Before, the name, domain, subdomain and status could only be changed through the API.

**Who.** The company administrator (`admin.tenant`) changes legal name, tax ID, language, **phone, address and mailing address**. The **platform administrator** also changes the **name**, the **domain**, the **subdomain**, **activates or deactivates** the company, and is the only one who sees **New company**.

- **New company** (`POST /api/v1/platform/tenants`, requires recent MFA): a window with the basics: **name, phone, address** (required), **mailing address** (if different), **domain and subdomain** (optional). On accept, the company is born with the **base modules** (System, Catalog, Analytics and Custom fields; **no** transport, warehouse or billing: they are turned on later in Settings → Modules) and the system **enters it directly** so you can finish the setup. The initial administrator (`adminEmail`) is now **optional**: the platform operates any company and users are added afterwards.
- **Address and mailing address:** they are locations of the company like any other (`Location` without a client, types `COMPANY` and `COMPANY_POSTAL`), with country. With no mailing address of its own, the address is used (as with a client). Removing the mailing address is a soft delete, never a deletion.
- **Phone:** for letters and documents. Required when creating; companies older than this date may have none until someone writes it.
- **Deactivate** (`PUT /api/v1/platform/tenants/{publicId}/active` with `{ "isActive": false }`): nobody gets in (login and company switch answer 403 `The user has no active company.` / `The company is inactive.`), the API cuts off whoever was already in with 403 `The company is inactive.` and **everyone else's sessions are closed**; the platform person who did it stays in so they can reactivate it. Nothing is deleted. In the platform's company selector it shows as “(deactivated)”. **Activate** returns it as it was.
- **What was removed because it was unused:** `sessionDays`, `deviceSessionDays` (session lifetime lives in minutes; when the database was updated, where the minutes were unset the days value was copied, so no company changed duration) and `trackingSlug` (public tracking travels in each notice's link).

## Session setting: `Auth:Sessions:SingleSessionPerBrowser`
By default (`true`) when signing in, the previous sessions of the same person, with the same browser and the same IP, are closed. Only in **tests** is it set to `false` (environment variable `Auth__Sessions__SingleSessionPerBrowser=false`, as the CI already does): the Playwright runs sign in in parallel with the same administrator from the same browser and, with the rule active, some closed the session of others (“Invalid session.” when reauthenticating). **It is never turned off in production.**

## Clients and Consignees in “Roles & users” (Batch 121)

> Owner's rule (2026-10-18): **all users —staff, portal and consignee— are controlled by the platform administrator and the company administrator** (those of their company).

**System → Roles & users** has four tabs: **Roles**, **Users** (the company's staff), **Clients** (the portal people of ALL the company's clients) and **Consignees** (the consignees' own accesses). The last two are visible with the permission **`portalusers.manage`** and the **CLIENT_PORTAL** module turned on.

Each row states the **type** (Client / Consignee), the client (name and code), the consignee (in the Consignees tab), the role, the status (Invited, Active, Suspended, Deactivated) and the last access. Actions per row (the same as in the client record, with the same permission):

- **Open the client:** goes to their record.
- **Resend invitation** (if Invited).
- **Suspend** (if Active; asks for confirmation): they cannot enter the portal until reactivated and **their open session is cut off instantly**.
- **Reactivate** (if Suspended).
- **Deactivate** (asks for confirmation): they lose access; to give it back they must be invited again. Deactivated people appear with **Include removed**.

**No inviting from here:** a client's people are invited by the company from the client record and consignees are invited by the client from their portal. Everything done is recorded in the security log with who did it (and whether it was the platform administrator).

The **platform administrator** works inside the company they have active (switching it as always); from there they see and control all the portal people and consignees of that company.

| Method and route | Permission | What it does |
|---|---|---|
| `GET /api/v1/portal-users?kind=CLIENT\|CONSIGNEE&includeRemoved=false` | `portalusers.manage` + CLIENT_PORTAL module | The company's list (read-only). 400 “The kind must be CLIENT or CONSIGNEE.”; a portal person gets 403. |
| `POST /api/v1/clients/{publicId}/portal-users/{id}/suspend` · `reactivate` · `remove` · `resend-invite` | `portalusers.manage` | The usual actions, with the row's client. |

## Menu access (Batch 152, 2026-10-20)
In Roles & users, the permissions start with the **“Menu access”** category: one per menu option, in the menu's order. Granting only the access permission lets the user **enter the screen and view it**, without creating or editing anything; to act, the action's permission is also needed (for example, `warehouse.pick` to pick). Whoever has an action permission enters its screen alone without being granted it separately, and entering a screen includes reading the data it shows. Day pulse also has its permission (`pulse.view`). On update, each role kept the screens it already saw.

## Permissions by section and in logical order (2026-10-22)

In System → Roles, permissions are shown **by section, in menu order** (Pulse, Transport, Warehouse, Rentals, Billing, Clients, Fleet, Analytics, System, Security) and, within each one, **by screen**: first “open and view” and then whatever creates, edits or runs on it (not in alphabetical order). The separate group “Menu access” no longer exists: each access is in its section. **Transport** (formerly “Orders” and “Trips”) brings together shipments, scan station, routes (plan, optimize, dispatch, close, monitoring) and claims. **Billing** brings together purchases, purchase accounting, sales (formerly under Orders), dispatch accounting, billing, process deliveries (COD), driver settlement and taxes.

**“Open and view” does not edit (2026-10-22).** Each screen has its **open and view** permission and, separately, the one to **do** things on it (create, edit, generate, approve…); having only the first one does not allow changing anything. Two that were missing were added: **Suppliers: create and edit** (`suppliers.manage`) and **Purchase accounting: generate, accept, cancel and export** (`purchaseaccounting.manage`); `purchasing.manage` is still accepted for both, and the roles that had it received both. The following remain without an edit permission of their own, either because there is nothing to edit or because they share the one of another screen: Ledger, Route monitor, Security & audit, Scan station (its action is scanning), and Views, Indicators and Charts (they share “Create views, indicators and charts”).

**More edit permissions (2026-10-22, b).** These were separated from “open and view”: **Warehouses: create and edit (includes docks)** (`warehouses.manage`), **Bins: create, edit and remove (includes zones)** (`bins.manage`) and **Dispatch accounting: generate, accept, cancel and export** (`dispatchaccounting.manage`). The previous ones (`warehouse.manage`, `billing.generate`, `billing.export`) are still accepted, and the roles that had them received the new ones. **Notification rules:** “open and view” now allows **only looking at** the rules and the log; **creating, changing and testing** requires “Manage notification rules” (`notifications.manage`), and the buttons do not appear without it. **Route monitor:** the link to the Dispatch room requires the permission to open the Dispatch room (`dispatch.view`). **Views, indicators and charts:** they are created and edited with a single permission, “Create and edit views, indicators and charts” (`analytics.manage`), which now appears right below “Views & reports”.
