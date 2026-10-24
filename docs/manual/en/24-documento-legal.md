# 24 — Legal document: privacy policy and terms of use

Batch 99 (client portal, 2026-10-17). There is **a single document** —“Privacy policy and terms of use”— with **a single version for the whole platform** (staff, portal clients and consignees). It is in Spanish and English, in two parts: **Part 1** (privacy) and **Part 2** (terms of use). **It is a draft to be reviewed by a lawyer before production.**

## 1. How it works for the user
- On sign-in, if the user **has not accepted the current version**, the screen with the full document appears instead of the application. **“I accept…” is enabled only when the user reaches the end of the text** (until then it says “Scroll to the end of the text to be able to accept.”). “I do not accept, sign out” closes the session.
- It works the same for **those who already have an account**: when this goes to production, existing users have no saved acceptance and will see it on their next sign-in.
- **When the document changes** (new version), everyone accepts it again on their next sign-in.
- The document can also be read **without a session** at `/legal` (invitations and portals will link to it).
- While acceptance is pending, the rest of the API responds **403 `legal_acceptance_required`** (“You must accept the privacy policy and the terms of use to continue.”). Sign-in and sign-out, `/me` and the document itself remain open.
- The **device sessions** of the warehouse app are left out for now (they sign in with a PIN, with no web screen).

## 2. Who accepted and who did not
**Security & audit → Legal acceptance** (permission `admin.audit`): the company's users with their state —**Accepted** (with date), **Pending** or **Earlier version (YYYY-MM-DD)**—, pending ones first. Search by name or e-mail, filter by state, sort columns and export everything the filter says. Each acceptance stores **version, date, language, IP address and browser**, and an event is left in the security log (`LOGIN`, stage `legal_accepted`).

## 2b. Cookies and browser storage
Teikem **does not use advertising or tracking cookies**: it stores in the browser (local storage) only what is strictly necessary —session, the computer's mark, the language and the “Do not trust this computer” checkbox—, which is exempt from consent, and **it says so** in a section of the privacy policy (“Cookies and browser storage”, including that Cloudflare Turnstile may store technical data to check that you are not a robot). **That is why there is no cookie banner.** If analytics or advertising is ever added, that day consent does need to be requested first (and the banner put up). This section raised the version to `2026-10-17.2`: everyone accepts again.

## 3. Endpoints
| Endpoint | What for | Who |
|---|---|---|
| `GET /api/v1/legal/current?lang=es\|en` | The current text (version, title, sections) | Public |
| `GET /api/v1/legal/status` | `currentVersion`, `required`, `accepted`, when and which version was accepted | Session |
| `POST /api/v1/legal/accept` `{ version, lang }` | Accepts the current version | Session |
| `GET /api/v1/legal/acceptances?status=accepted\|pending&q=&skip=&take=` | Who accepted and who did not | `admin.audit` |

## 4. Messages and errors
| Situation | Response |
|---|---|
| Acceptance is pending and any other part of the API is called | 403 `legal_acceptance_required`: `You must accept the privacy policy and the terms of use to continue.` |
| A version that is no longer the current one is accepted | 409: `The document version is no longer the current one. Read it again.` |
| Accepting the same version twice | Does not duplicate; responds with the state |

## 5. Operator data and text changes (for the owner)
The text carries placeholders that **must be filled in before production** in the API configuration (`Legal:*`); the ones not filled in appear as they are, in square brackets:
- `Legal:OperatorName` — legal name of the operator (today `[RAZÓN SOCIAL DEL OPERADOR]`).
- `Legal:OperatorAddress` — postal address.
- `Legal:ContactEmail` — privacy e-mail.
- `Legal:Jurisdiction` — applicable law and courts (by default “Puerto Rico”).
- `Legal:RequireAcceptance` — `true` by default; **it is turned off only in development**.

**To change the text:** edit `src/Teikem.Infrastructure/Legal/LegalText.cs` and change `LegalRules.CurrentVersion` (the date) in `src/Teikem.Domain/Security/LegalAcceptance.cs`: that makes everyone accept again.

## Frequent cases
- **A user says it “won't let them through”:** they must reach the end of the text by scrolling (on a phone, swipe to the bottom of the box) so the button is enabled.
- **An integrator or an app receives 403 `legal_acceptance_required`:** the user of that session must sign in to the web app and accept the document.
- **We changed the document and everyone became pending:** that is expected; the Legal acceptance list shows “Earlier version”.

**Version `2026-10-21.1` (Batch 166/167):** the text says “shipments” where it said “delivery orders” (the transport order was renamed shipment). The version went up, so **all users accept again** the next time they sign in.

**Where it is seen once inside (2026-10-21):** at the foot of the web app's side menu there are, small and dimmed, the links **Privacy** and **Terms of use** (underlined so it is clear they are links; they open the document in another tab, already at the corresponding part), “© year Cerevelo LLC.” and, on another line, “All rights reserved.”, and “Powered by Teikem”. With the menu collapsed the footer is hidden; on the phone it is in the menu. The owner and the product name are in `web-app/src/app/AppLegalFooter.tsx`.

**In the client portal (2026-10-21):** at the end of **Settings**, in small print, **Privacy** and **Terms of use** (they open the document read-only, already at that part, with the **Back** button), the Cerevelo LLC rights reserved and “Powered by Teikem”. It has no accept button: acceptance is still the mandatory screen of the first sign-in.
