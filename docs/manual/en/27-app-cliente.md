# 27 — Client app (Teikem Clientes)

Batch 102 (client portal, 2026-10-17). The client's **separate** app: **web** (installable on the desktop or the home screen) and **mobile** (Android and iOS), built with Expo. The client signs in with their e-mail; they see **their** shipments and **their** notices. The access and isolation rules are those of [chapter 26](26-portal-acceso.md); the screens with their screenshots, in [the L102 screens manual](frontend/l102-app-cliente.md).

## 1. What the client can do
| Screen | What it does |
|---|---|
| **Sign in** | E-mail and password; the “Do not trust this computer” checkbox (remembered); “Put it on my desktop” (web) |
| **Confirm the computer** | The first time from a new computer: an e-mail arrives with a link (24 h, single use); the user opens it (also from the phone) and signs in again |
| **Second step (MFA)** | The first time they enroll their authenticator app (key or link, code and **recovery codes**); afterwards they type the code (remembered for 30 days on the trusted computer) |
| **Legal document** | Privacy policy and terms of use: the “I accept” button is enabled only on reaching the end of the text |
| **Home** | Greeting, the current client, how many shipments they have in total and per status, and the 5 most recent |
| **Shipments** | Their shipments with search by waybill, shipment, invoice or consignee, **date filter** with ranges (Today, Yesterday, Last 7 days, This week, Last week, Last 30 days, This month, Last month, This year, Custom with From/To; Batch 213) and “Load more” (20 per page); the table says **“N of M”**; each one with its **progress bar** (chapter 28). **New shipment**: chapter 30 (in the installed app, the tablet and the phone, only the detailed entry) |
| **Shipment record** | The **progress bar** with the list of stages, waybill, status, service, delivery, packages, COD and the **tracking** (each status change with its date) |
| **Messages** | Their conversation with the customer service team (chapter 29) and those of their consignees (chapter 35). In the wide web (Batch 213) it is a two-column inbox: list of conversations on the left and the open one on the right |
| **Notices** | The summaries their company configured to send them through the portal inbox (picked up, out for delivery, delivered, COD processed, invoice issued) |
| **Settings** | The account; **switch client** (if they have access to several); language (es/en); **Open with fingerprint** (phone only); “Do not trust this computer”; **my trusted computers** (with “Remove”); sign out |

## 1b. Language and theme (light and dark)
Decided with Luis (2026-10-17): **every screen has both themes and both languages, with its option to change them**.
- **Language:** Spanish by default; **Español / English** in the login, on the public page “How is my shipment doing?”, in the legal document and in Settings. It changes instantly, without reloading, and is remembered.
- **Theme:** **Automatic** (by default: follows the phone or the browser), **Light** or **Dark**, in the same places. It changes instantly and is remembered; Light and Dark override the system. All colors (including the labels, notices and the progress bar) come from a palette per theme, with **AA accessibility contrast** watched by tests.
- The texts that come from the server (the company's statuses, the legal document, notices) appear in the chosen language.

## 2. Open with fingerprint (phone)
- In **Settings → Security → Open with fingerprint** (it only appears if the phone has a reader **and** a registered fingerprint; if not, the screen explains why). On activating it, the user is asked to identify themselves once.
- With the fingerprint active, the app **locks** on opening (with a saved session) and on returning from the background after **2 minutes**: it asks for the fingerprint (or the phone's PIN or pattern as a fallback) instead of the password.
- **After 5 consecutive failures**, or with “Use my password”, the device's session is closed and the normal login returns.
- The fingerprint **does not replace** the first sign-in, the confirmation of a new computer or the MFA code: it only opens the session that was already saved on that phone. If the session expired or was revoked, you must sign in with the password.
- On the web there is no fingerprint (a next step would be to use passkeys).

## 3. “Do not trust this computer”
The login checkbox is **remembered** (stored on the device or the browser): on returning it appears checked. Checked: the user signs in **only once**, that computer **is not remembered** (the next time it asks for the confirmation e-mail again) and the MFA code is requested **always**. It can also be changed in Settings. Use it on shared or public equipment.

## 4. Installing the web as an app
The browser (Chrome, Edge, Android) offers to install the portal: in the login **“Put it on my desktop”** appears and, on pressing it, the icon stays on the desktop or the home screen and it opens as an app. On **iPhone and iPad** the browser does not allow doing it with a button: the login shows the instruction “Share → Add to Home Screen”. If it is already installed, nothing is shown.

## 5. Messages and errors
The service messages are the server's (chapters 24 to 26): `Invalid credentials.`, `The user has no active access to the portal.`, `The link is not valid or has expired. Try signing in again to receive a new one.`, etc. The app's own:
| Situation | Text |
|---|---|
| No connection with the server | “Cannot reach the server. Check your internet and try again.” |
| The server asks for the anti-robot challenge | “Confirm you are not a robot to continue.” (on the web the Cloudflare widget appears; **on the phone not yet**: it asks to sign in from the browser) |
| The fingerprint fails | “Could not verify. You have N attempts left before the password is asked for.” |
| Shipment of another client or nonexistent | The same “not found” message from the server |

## Frequent cases
- **“It asks me to confirm the computer again”:** it is a new computer or browser, 30 days went by without using it, the password was changed, “Do not trust” was checked or it was removed in Settings.
- **“Open with fingerprint does not appear”:** the phone has no reader or has no registered fingerprints; register them in the phone's settings.
- **“I do not see an order I know exists”:** the app only shows the shipments of the client it signed in with; if you have access to several, switch it in Settings.
- **“The notices are empty”:** the company decides, client by client, which notices it sends and through which channel (chapter 23); if the “portal inbox” channel is off, nothing arrives here.

**Legal information (2026-10-21):** at the foot of Settings are the links **Privacy** and **Terms of use** (reading of the current document, route `/legal-view`), the rights reserved and “Powered by Teikem”. See chapter 24.

> Client portal screenshots: see [capturas-apps.md](frontend/capturas-apps.md).

## 6. Footer and totals (Batch 213, 2026-10-23)
- On the **desktop web** the footer (Privacy · Terms of use, © rights reserved, Powered by Teikem) is a **real page footer**: stuck to the bottom edge when the content does not fill the window and at the end of the content when it is longer. On the phone it goes at the end of the content.
- **Every table says how many rows it has in total** (“N of M”: the ones shown of those that match the filter), even if it is fetched in parts with “Load more”.
- **Failed attempts (2026-10-24):** a shipment that went out on the street, was not delivered and came back to the warehouse carries under its status an amber pill “1 failed attempt” / “2 failed attempts” (My shipments, Home, record and progress bar), to tell it apart from one that is merely delayed. See the screens chapter `frontend/l213-portal-cliente.md`.
- **View per device (2026-10-24):** installed app, tablets and phones see the mobile form; the web on a computer, the wide one (chapter 30). **Export is the same everywhere: Excel, CSV and PDF**, also in the installed app (the file is saved and “Share” opens).
- **Every table exports** (“Export · Excel · CSV · PDF” at the foot of the table): it downloads the file with **all the rows that match the filter** (search, dates, status), not only the visible ones, up to 5000. My shipments, My invoices, My warehouse (stock, products, movements, value, arrivals and departures) and My consignees have it; Reports have their own “Export to Excel (CSV)”.
  - **Excel (.xlsx)** and **PDF** carry at the top, like those of the company's web app: the **logo**, the company name, the table title, “Generated on …” and the filters sentence (“Filters: Search “farm” · Dates from … to …” or “No filters”). In Excel dates are dates, numbers are numbers (they add up) and the headers come with an automatic filter; the PDF is A4 (landscape with more than 5 columns) with numbers to the right and the page number at the bottom.
  - **The logo of the PDF and the Excel is that of the company** serving the client (the normal one, readable on white; if it only uploaded the inverted one, it goes on a plate in the color of its bar); **if the company uploaded none, Teikem's appears**.
  - **The PDF colors are the company's** (Settings → Branding): the table header, the title and the line under the header come out in the bar color and the accent the company defined; with the default branding, the usual navy blue and orange. No other color is used.
  - **CSV** carries only the table (UTF-8, Excel opens it with accents). **On the phone there is only CSV** and the file is shared instead of downloaded.
