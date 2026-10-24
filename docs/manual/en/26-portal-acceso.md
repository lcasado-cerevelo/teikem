# 26 — Client portal: access and isolation

Batch 101 (client portal, 2026-10-17). The portal has its **own login**, separate from the staff's, and each session is tied to **one client**. This chapter is about the server (the `app-cliente` app with its screens is batch 102). Invitations and portal users are managed as always from the client's file (`portalusers.manage`, module CLIENT_PORTAL).

## 1. How a portal user signs in
1. **Invitation** (from the client's file): a link arrives that **expires after 48 hours** (`Portal:InviteHours`), is used **only once** and, if resent, the previous one stops working. With the link the user chooses a password (`POST /api/v1/portal-users/accept-invite`); the account becomes active.
2. **Sign in:** `POST /api/v1/portal/auth/login` `{ email, password, clientPublicId?, trustedBrowserToken, captchaToken?, doNotTrust?, deviceInfo? }`. Only **portal** accounts: a staff user does **not** get in here, nor a portal user through the staff login.
3. **New computer:** responds `browser_confirmation_required` and sends a confirmation **e-mail** (see chapter 25). **In the portal this is always the case**, even if the staff does not use it.
4. **Mandatory second step (MFA):** `mfa_required`. If the user has no MFA enrolled, they are required to enroll it (with the challenge: `POST /auth/mfa/totp/enroll` and `/confirm`); with MFA enrolled they type their code in `POST /auth/mfa/verify` (also sending `trustedBrowserToken`).
5. **Signed in:** receives the tokens. From that computer **the code is not requested again for 30 days** (when it was typed is recorded); on day 31 it is requested again. The computer is valid for 30 days without use.
6. **“Do not trust this computer”** (`doNotTrust = true`, for shared equipment): signs in **once**, the computer **is not remembered** (the next time it asks for the confirmation e-mail again) and the MFA code is requested **always**. The login screen (batch 102) **remembers** that the checkbox was checked and shows it checked.

Possible login responses (`status`): `ok`, `mfa_required`, `browser_confirmation_required`, `captcha_required`.

## 2. One client per session
- The session (and its access token) carries the **client** it signed in with (`cid`) and survives renewal. If the account has access to **several clients**, it signs in with the one in `clientPublicId` (or the first by name) and switches with `POST /api/v1/portal/auth/switch-client` `{ refreshToken, clientPublicId }` (403 `You do not have access to that client.` if it is not theirs).
- **Isolation (two filters):** every portal query is limited to **the company and the client of the session**. It is a data filter of the context (not only a check in the service): even if a query forgot to filter by client, it would not see other clients' data. A shipment of **another client** (of the same company or another) responds **404 just like one that does not exist**.
- The session **dies** (401) on renewal if the account is suspended, deactivated or loses access to that client, or if the client or the company is deactivated.
- **A portal user never reaches the staff routes** (403 `portal_scope`: `This route is not available for portal users.`) even if their token is valid; and a staff session does not get into `/api/v1/portal/*` (403 `portal_scope`: `This route is only for portal users.`). The session (`/auth/*`: renew, sign out, password, MFA and trusted computers), `/portal/*` and the legal document remain open for the portal.
- The portal requires accepting the **legal document** (chapter 24) like everyone.

## 3. Available reads (this batch)
| Endpoint | What it returns |
|---|---|
| `GET /api/v1/portal/me` | The account, the current client and the other clients it has access to |
| `GET /api/v1/portal/orders?search&status&fromUtc&toUtc&skip&take` | My shipments (only those of the session's client): waybill, shipment, client invoice, consignee, town, service, pieces, COD, status. Maximum 100 per page |
| `GET /api/v1/portal/orders/{id}` | One shipment of mine with the delivery address, the packages and its **timeline** (statuses and when) |
| `GET /api/v1/portal/summary` | Home: total of my shipments, how many there are per status and the 5 most recent (Batch 102) |
| `GET /api/v1/portal/notifications?skip&take` | The notices inbox: my client's summaries marked for the portal, newest first; maximum 100 per page (Batch 102) |
| `GET /api/v1/portal/auth/config` | Whether the login asks for the anti-robot challenge and with which site key |

The portal DTOs are **their own and minimal**: they carry no quoted prices, internal notes, routes, drivers or other clients' data. Invoices, COD, notices and the rest of the reads arrive in the following batches.

## 4. Login protection
- Same **account lockout** as the staff (5 failures → 1, 5, 15 and 60 minutes), same message `Invalid credentials.` (401) for a nonexistent, staff, inactive or locked account, with the same response time; limit of **60 attempts per minute per IP**.
- **Anti-robot challenge (Cloudflare Turnstile):** off by default. When on (`Auth:Turnstile:Enabled`, `SiteKey`, `SecretKey`; the secret key goes in the configuration or in an environment variable, never in the repository), it is requested **only after 3 consecutive failures** of that account (`RequireAfterFailures`); until it is solved, the password is not even looked at (`status = captcha_required`, and `captchaToken` is sent with what the widget gives). If Cloudflare does not respond, the challenge **is not skipped**.
- Everything is kept in the security log with `portal: true`.

## 5. Messages and errors
| Situation | Response |
|---|---|
| Nonexistent, staff, inactive or locked account, or wrong password | 401 `Invalid credentials.` |
| Portal account without active access to any client | 403 `The user has no active access to the portal.` |
| `clientPublicId` that does not belong to the account | 403 `You do not have access to that client.` |
| Staff route with a portal session / portal route with a staff session | 403 `portal_scope` |
| Shipment of another client or nonexistent | 404 (same response) |
| CLIENT_PORTAL module off for the company | 403 `module_disabled` |

## Frequent cases
- **“The portal won't let me in” (client):** did they accept the invitation (expires after 48 h)? Is their user ACTIVE in the client's file? Is it a new computer (check the e-mail)? Is the account locked by failed attempts?
- **A staff user cannot sign in through the portal** (nor the other way around): it is on purpose; each has its own login.
- **The client says they do not see a shipment:** it may belong to another client of the company (the portal only shows those of the client they signed in with) or not be created yet; the portal responds the same in both cases.
