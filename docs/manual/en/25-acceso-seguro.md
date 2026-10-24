# 25 — Secure access: account lockout and trusted computers
<!-- kemi:portal -->

Batch 100 (client portal, 2026-10-17). Two protections that apply to **every access** (staff today; clients and consignees once their portals exist).

## 1. Account lockout with a growing wait
- Every **5 consecutive failed attempts** lock the account. The first lockout lasts **1 minute**, the second **5**, the third **15** and from the fourth on **60**. If **24 hours** go by without another lockout, it starts over. It is never forever, so nobody can lock a real user out on purpose.
- The attempts that count: signing in with the password, the second-step code (MFA), re-authentication, the password change, the e-mail code of the first sign-in and the MFA code when resetting the password.
- With the account locked, **not even the correct password gets in**, and the message is the usual one: `Invalid credentials.` (401). A user that does not exist, an inactive one and a locked one respond **exactly the same** and take the same time (it is not possible to find out which users exist).
- On lockout, the account owner receives an **e-mail** (“Your account was temporarily locked”: for how long and from which address) so they change the password if it was not them. If e-mail is not configured, it is not sent (the login does not break because of that).
- **Limit per IP address:** `POST /auth/login` and `/auth/mfa/verify` accept **60 per minute per IP** (`RateLimiting:LoginPerMinute`); past the limit, 429 `Too many attempts; wait a minute and try again.` It slows down whoever tries the same password on many accounts.
- They are kept in the security log (`LOCKOUT` events, with the stage —`login`, `mfa`, `reauth`, `password_change`— and the minutes) and are seen in **Security & audit → Activity**.
- **Not included yet:** the anti-robot challenge (Turnstile type) after several failures; it requires hiring a provider.

## 2. Trusted computers (30 days)
**How it works.** The web app creates a random **secret mark** in the browser (`teikem.browser`) and sends it on every sign-in; the server stores only its hash. The first time someone signs in correctly with their password from a computer (browser) that is not recognized:
1. No tokens are issued; the login responds **`browser_confirmation_required`** and the screen says “Confirm this computer”.
2. An **e-mail** arrives with the browser, the address and the time of the attempt, and a “Confirm this computer” link (valid for **24 hours**, used only once). The link can be opened **from the phone**: it confirms the computer where the sign-in was attempted.
3. With the link confirmed, the user goes back to the sign-in screen and signs in; that computer stays trusted for **30 days without use** (each sign-in extends the window another 30 days).
4. If it was not that person, the e-mail says not to click and to change their password.

**Rules.**
- Another browser, incognito mode or clearing the browser data count as a **new computer**.
- No more than one e-mail is sent every **2 minutes** per computer nor more than **10 a day** per user (nobody can flood someone else's mailbox).
- **Changing or resetting the password removes the trust from all computers** (they are confirmed again).
- Each person sees and removes their own in **My account → Sessions → My trusted computers** (the current one appears as “This computer”). Removing one sends it to confirmation the next time.
- It is requested after the password and before the second step (MFA); it is not requested during the first sign-in by invitation (the e-mail is already verified there).
- A client that does not send the mark (an integration, for example) does not get in when the feature is on: `This client does not identify the computer; sign in from the web app.`
- **Device** sessions (warehouse app with PIN) do not go through this.

**Who uses it.** Decided with Luis (2026-10-17): **the staff stays as is** (their login does not change: password, MFA and “preferred IP”). Trusted computers are for the **portals** (client and consignee) and there they are **always on**; the option `Auth:TrustedBrowsers:Enabled` stays off and only serves if one day it were to be applied to the staff too. In the portals, in addition: the **MFA code is remembered for 30 days** per trusted computer and there is **“Do not trust this computer”** (see chapter 26).

**Turning it on for the staff (not recommended today).** It comes **off** (`Auth:TrustedBrowsers:Enabled = false`) because, when on, everyone who signs in from a new computer needs **to receive the e-mail**: it must be turned on only with e-mail sending (Brevo) running and `Auth:PasswordReset:WebBaseUrl` pointing to the web app. Other options: `Auth:TrustedBrowsers:Days` (30), `LinkHours` (24) and, **only in development**, `ReturnLinkInResponse` (returns the link on the screen to test without e-mail).

## 3. Endpoints
| Endpoint | What for | Who |
|---|---|---|
| `POST /api/v1/auth/login` `{ email, password, tenantId?, deviceInfo?, trustedBrowserToken? }` | May respond `status = browser_confirmation_required` with `browser { email (masked), sent, message, devLink }` | Public (60/min per IP) |
| `POST /api/v1/auth/confirm-browser` `{ token }` | Confirms the computer with the link from the e-mail | Public |
| `GET /api/v1/auth/trusted-browsers` (header `X-Trusted-Browser`) | My trusted computers (marks the current one) | Session |
| `DELETE /api/v1/auth/trusted-browsers/{id}` | Removes one of my computers | Session |

## 4. Messages and errors
| Situation | Response |
|---|---|
| Wrong password, nonexistent/inactive user or locked account | 401 `Invalid credentials.` |
| More than 60 attempts per minute from one IP | 429 `Too many attempts; wait a minute and try again.` |
| Confirmation link expired, used or made up | 400 `The link is not valid or has expired. Try signing in again to receive a new one.` |
| Removing a computer that does not exist or is not mine | 404 `Computer not found.` |
| 10 confirmation e-mails were already sent today | `browser_confirmation_required` with `Several confirmation e-mails were already sent today; check your e-mail or try again later.` |
| The e-mail could not be sent | `browser_confirmation_required` with `The confirmation email could not be sent.` (can retry after 2 minutes) |

## Frequent cases
- **“It asks me to confirm again”:** the user signed in from another browser, in incognito mode, cleared the browser data or 30 days went by without using that computer, or changed their password.
- **The e-mail does not arrive:** check the spam folder; wait 2 minutes and try signing in again (it is resent). If it still does not arrive, have the administrator check that the platform's e-mail is configured.
- **I am on a shared computer:** when done, go to My account → Sessions → My trusted computers and remove that computer.
- **My account got locked:** wait the time the e-mail says (1, 5, 15 or 60 minutes) and sign in with the correct password; if you do not remember it, “Forgot your password?” (changing it resets the trusted computers).
