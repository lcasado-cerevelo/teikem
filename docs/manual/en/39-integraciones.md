# 39 — Integrations (Batch 132, P11)

> Dictated by Luis (2026-10-19). An integration moves data between Teikem and another system **by configuration, without programming each time**. This batch brings the **inbound** integrations by **folder** and by **manual upload**; email, SFTP, API and webhooks arrive in the following batches (see `docs/p11-diseno-integraciones.md`).

## 1. What it is and who can
- **Module** `INTEGRATIONS` (off; turned on per company in System → Modules). Screen: **System → Integrations / API** (`/system/integrations`).
- **Permissions:** `integrations.view` (view and the history) · `integrations.create` (create; base) **plus** the one for each scope family: `integrations.create.files` (folder, SFTP, email), `.transport` and `.warehouse` (they arrive with the API) · `integrations.manage` (edit, turn off, run, upload file) · `integrations.inbox` (the inbox). TenantAdmin has them all; the administrator grants them to whatever roles they want.
- **Own service account per integration:** when the integration is created, a user of class SERVICE is created (it never signs in) with a role “Integration: name” that only carries the permissions of the chosen scopes. Whatever comes in is **attributed to that account**, not to whoever configured it. **Whoever creates cannot grant more than they have:** they are only offered scopes of families they can create, of modules the company has and whose permissions they hold.

## 2. Creating and editing an integration
Screen: **New integration** (name, description, client, import template, connector, schedule, scopes, who to notify and retention) · API: `POST /api/v1/integrations` (`integrations.create`), `PATCH /{publicId}` (`integrations.manage`), `GET /options`, `GET`, `GET /{publicId}`, `POST /{publicId}/deactivate` and `/reactivate` (turn off and on: the service account, its role and the notification rule follow the integration; it is never deleted).
- **Client and template:** the incoming shipments are created for that client with the chosen import template (the same one as Shipments → Import; with **several lines per order** and the “no piece” rules of chapter 03 §7.5). Since the file numbers the shipment, the client must be marked as numbering their own shipments.
- **Connector:** **Input folder** (the folder name, only letters, numbers, hyphen and underscore; the `.csv` and `.txt` files dropped there are processed and moved to `procesados/` or `errores/`; **they are never deleted**) or **Manual upload** (the file is uploaded from the screen). The folder lives in `Integrations:FolderRoot/{company}/{folder}`.
- **Schedule:** manual only or every N minutes (1 to 10080; the folder is checked on its own; a manual upload is not scheduled).
- **Who to notify:** company users + **“Include me”**. Each run with something to report (shipments created, rows to the inbox or a failure) sends a **system notice** (bell and email) to those people; the creator is not notified unless they checked it.
- **Retention:** days the raw inbox data is kept (30 by default, 1 to 365); once the period passes, the detail is purged and the reason stays.

| Message (400 unless stated) | When |
|---|---|
| “The integration name is required.” · “Maximum 120 characters.” | empty or long name |
| “An integration with that name already exists.” (409) | duplicate name |
| “Choose at least one scope for the integration.” · “The scope 'X' does not exist.” | scopes |
| “You cannot grant the scope “X”: you are missing the 'integrations.create.files' permission.” (400) · “…you do not have the permissions …” · “The company does not have the module that “X” needs.” | no privilege escalation |
| “Enter the client for this integration's shipments.” · “Choose the import template used to read the files.” | client / template |
| “That connector type is not available yet.” · “Unknown connector type: X.” | connector |
| “Enter the input folder name.” · “The folder name only allows letters, numbers, hyphen and underscore (no paths).” | folder |
| “The interval must be between 60 and 604800 seconds.” · “A manual upload integration is not scheduled.” | schedule |
| “Retention days must be between 1 and 365.” · “One of the users to notify does not exist or is inactive.” | retention / notices |

## 3. Running an integration
- **Folder:** on schedule (the scheduled job “Scheduled integrations”, every minute) or with **Run now** (`POST /{publicId}/run`, `integrations.manage`): it reads the new files in the folder. **409** “The integration is already running; wait for it to finish.” · **422** “The integration is turned off; turn it on to run it.”
- **Manual upload** (any integration): **Upload file** (`POST /{publicId}/upload`, multipart, up to 4 MB): it is processed the same way.
- **Each file:** its **SHA-256 hash** is computed; if the same content was already processed, it is marked “Repeated” and is not processed again. It is read (UTF-8; otherwise Excel's Latin-1), goes through the importer **on behalf of the service account**, the good shipments are created and the rest go to the **inbox**. An empty file or one larger than the limit (`Integrations:MaxFileMegabytes`, 2) is marked “With errors”.
- **Run result:** *OK*, *With pending items* (something went to the inbox) or *Failed*; with files read, shipments created and sent to the inbox. **History:** `GET /{publicId}/runs` (up to 100) with their files.

## 4. The inbox (`integrations.inbox`)
What came in and could not be processed on its own. **A shipment with problems is never half-created.** Each item carries the **exact reason** (the same message as the import preview: “The shipment has no pieces…”, “Piece number X is repeated…”, “Row 4 · …”) and the **data as it arrived**.
- **Screen:** **Inbox** tab (with the pending counter) with filters by integration and status; **Fix** (the same cells of the file, as they arrived), **Reprocess**, **Discard** (with a note), **Reprocess all** of an integration and **Download pending (CSV)**.
- **API:** `GET /inbox` · `GET /inbox/{id}` · `PATCH /inbox/{id}` (fix cells: same rows and columns) · `POST /inbox/{id}/reprocess` · `POST /inbox/{id}/discard` · `POST /{publicId}/inbox/reprocess-all` (up to 200) · `GET /inbox/export` (CSV).
- **Reprocess** runs the item through the importer again: if it now checks out, the shipment is created and the item becomes *Reprocessed* (with the link to the shipment); if not, it stays *Pending* with the new reason. Errors: **409** “This item was already resolved (reprocessed or discarded).” · **409** “This item's data is no longer kept (retention passed or the file could not be read)…” · **400** “The rows do not match the item's…” · “Each row must have the same number of cells it had.”
- **A file that could not be read** (columns, suspended client…) stays in the inbox without data to reprocess: it is discarded and the file is requested again.

## 5. Frequent cases
- *“The integration was created but it does not run on its own”.* The schedule (“Every…”) is missing or the integration is turned off.
- *“My folder does not find the file”.* It must be `.csv` or `.txt`, directly in the folder (not in subfolders).
- *“I uploaded the same file and nothing was created”.* It is the hash: it was already processed; the history marks it “Repeated”.

## 6. Email integration (Batch 133)

> Dictated by Luis (2026-10-19): an email connector that applies to **any client**. An email arrives at the integration's mailbox and Teikem reads **the attachments**, **the body** or both, and runs them through the integration's **same import template**. One mailbox per integration; the email content is **untrusted input**: nothing is ever executed and HTML is reduced to text.

**How it is configured** (connector **Email** when creating the integration; permissions `integrations.create` + `integrations.create.files`):
- **Mailbox (IMAP):** server, port, encrypted connection, user, **password** (stored **encrypted** with the company's key and never shown again: when editing, empty = it is kept; requires `Files:MasterKey` on the server), folder to read (INBOX) and processed folder. Teikem reads only **unread** emails; when handling them it marks them as read and moves them to “Procesados” (created if missing). **It never deletes.** Up to 50 emails per run.
- **Which emails are taken:** **subject** (contains / equals / starts with / ends with / wildcard / regular expression; the “contains” filter runs on the mail server) and **allowed original senders** (emails or domains; empty = anyone).
- **The original sender:** since the email almost always arrives **forwarded**, the sender that counts is looked up in this order: `X-Original-Sender`, `X-Original-From`, `Resent-From`, `Reply-To`, the first “De:” / “From:” line of the forwarded block in the body and, if none, the `From`. An email from another sender **is not processed**: it goes to the inbox with “Sender not allowed: x@y.com.”
- **Attachments:** all those that match the **type** (extensions, `csv, txt` by default), only the first one, or those that match the **name** (contains / wildcard `ordenes-*.csv` / regular expression…). Each attachment is processed like a file from the folder (hash, importer, inbox). “The email must have an attachment”: if it has none, it goes to the inbox with “The email has no attachment that meets the criteria.”
- **Body:** “Also read the email body” with a list of **fields to read**. Each field goes to a **template column** (1, 2, 3…): the body is converted into a file row (or several, with a block) and the same template reads it, with its rules (e.g. several lines per order). Field types:
  - **Label:** “Reference #” → the text that follows on that line (“Reference #: 123”; regardless of accents or case).
  - **Regular expression:** the first group in parentheses (or everything that matches).
  - **Block of lines** (a table, e.g. “Order Summary”): start text, end text, column separator and which column of the line goes to which column of the template (`6=1, 7=2`); each line is a file row and the loose values are repeated in all of them.
  - **Block with an expression per line (2026-10-22):** for tables the column separator cannot resolve (descriptions split across several cells or several lines, the SKU glued to the description, loose lines with “-”). Write a **regular expression per line**: each line of the block that matches is a row and the **groups in parentheses** (1, 2, 3…) go to the template columns (`6=1` = position 6 of the template takes group 1). Lines that do not match (continuations, dashes, totals, the discount) **are ignored**. With the expression, the start and end texts are not needed. Example for a packing list: `^(T-[\w-]+)\t.*?\t([\d,]+)\t([\d,]+)$` with columns `2=1, 3=3`.
  - **Repeat the block on every page:** a PDF repeats the table header on every page. With **“Repeat the block”** the block is read every time the start text (the header) appears and ends at the end text, and the rows of all pages are joined. Without the option only the first occurrence is read. It needs the start text.
  - **Labels in a table or PDF:** the label is also looked for in **any cell** of the line (`Ship Date ⇥ 10/7/26 ⇥ Order # ⇥ GRP0001 ⇥ PO ⇥ 990011`): the value is the cell that follows (or what follows in the same cell) and it **does not drag along** the other cells.
  - Optional **transformation**: uppercase, digits only (phones) or amount (removes `$` and commas). **Required:** if not found, the email goes to the inbox with “The required field of column N (…) was not found.”
- **Attached PDFs (2026-10-22):** the **“Also read attached PDFs”** switch takes the email's `.pdf` files (without having to write `pdf` in the attachment types), converts them into **text** and reads them with **the same fields to read as the body** (label, regular expression, block of lines; the field list is a single one and applies to the body and to the PDFs).
  - **PDF with real text** (those generated by a system): its text is used. Each visual line is a line and, where there is a column gap, a **tab** goes in: in the block of lines use the **tab** as the column separator.
  - **Scanned PDF** (an image): each page is rendered and run through **OCR** (Tesseract, Spanish and English). OCR runs on our server; the client's documents do not go out to any third party. It needs the languages folder (`Integrations:OcrTessdataPath` with `spa.traineddata` and `eng.traineddata`); without it, PDFs with real text are read anyway, but a scanned one goes to the inbox with “The PDF is an image (scanned) and OCR reading is not configured…”. A scanned PDF is read with less precision than a PDF with text: review what enters the inbox.
  - **Limits:** `Integrations:MaxPdfMegabytes` (15 by default) and `Integrations:MaxPdfPages` (30: the remaining pages are not read). A password-protected PDF cannot be read.
  - **To adjust the fields:** when a PDF yields no data, the reason in the inbox includes **a piece of the text read** (“No data was found in the PDF orden.pdf. Text read: “…””): there you see how the columns came out and you fix the label or the block. You can also **upload the PDF by hand** (Upload file) to the email integration with “Read PDF” to test without waiting for an email.
- **Readers (2026-10-22):** an email integration can have several **readers**; each one says what to do with a type of document that arrives in the same mailbox (the order in the body, the packing list in PDF, the invoice in PDF, an Excel…).
  - **What a reader is:** a name, **what it reads** (the email body, an attached PDF or an attached data file), its **conditions**, its **fields to read** (body and PDF), its **destination**, and if needed its **client** and its **template**.
  - **Conditions** (all must be met; with no conditions the reader takes everything of its source): on the **attachment name**, the **email subject** or the **document text** (that of the PDF already read, with OCR if scanned), with the same operators as the subject (equals, contains, starts with, ends with, wildcard, regular expression). Example: PDF whose text contains “Packing List” → reader “Packing list”; PDF whose text contains “Invoice” → reader “Invoice”.
  - **Who takes each document:** the **first** reader in the list whose conditions it meets (the order of the list matters). “Only the first document” limits a reader to one document per email. If no reader takes anything from the email, it goes to the inbox with “No reader of the integration applies to this email.”; an attachment no reader takes is ignored (signatures, images).
  - **Destination:** what the content read creates in the system. Today: **Transport order**. The schema is built to add more destinations (arrival notice, exit notice, purchase and sales orders, products, invoice processed, COD…) without changing the rest.
  - **Own client and template:** a reader can use a client and a template other than the integration's (one mailbox with documents from several clients). Empty, it uses the integration's.
  - **The usual settings:** if you add readers, they take precedence over the attachment, body and PDF settings above. The **“Turn the settings above into readers”** button creates the readers equivalent to those settings so you can continue from there. An integration **without** readers works exactly as before.
  - **Upload by hand:** a PDF uploaded (Upload file) to an integration with readers is evaluated like an email carrying only that attachment.
- **Repeats:** the same content (hash of the attachment or of the body read) is not processed twice; a repeated shipment number is already rejected by the importer.

**Testing without a mailbox:** in the list, **Test connection** (`POST /{id}/email/test`) checks server, user, password and folder and says how many unread emails there are (without touching them; the message never includes the password). And **Upload file** accepts an **`.eml`**: it is processed EXACTLY like one received through IMAP (same sender, subject, attachment and body rules), so the configuration can be tested before pointing the mailbox.

**Running:** on schedule (every N minutes) or **Run now**. If the mailbox does not respond, the run ends as *Failed* with “Could not connect to the mailbox: …” and nothing else happens (the emails remain unread).

| Message (400) | When |
|---|---|
| “Enter the mailbox server (IMAP).” · “The port must be between 1 and 65535.” · “Enter the mailbox user.” · “Enter the mailbox password.” · “Enter the mailbox folder to read (e.g. INBOX).” | mailbox |
| “Connector passwords cannot be saved: the server master key (Files:MasterKey) is missing.” | the server has no master key |
| “Unknown condition: X. Use EQUALS, CONTAINS, STARTS, ENDS, WILDCARD or REGEX.” · “Enter the condition text.” · “The regular expression 'X' is not valid.” | subject and attachment name |
| “The allowed sender 'X' is not a valid email or domain.” · “Maximum 50 allowed senders.” | senders |
| “Unknown attachment mode: X. Use ALL, FIRST or BY_NAME.” · “With “by name” enter the attachment name condition.” | attachments |
| “With “read the body” enter at least one field to read.” · “The column must be between 1 and 500.” · “Unknown field type: X. Use LABEL, REGEX or BLOCK.” · “Enter the label to look for in the email.” · “Enter the regular expression.” · “A block needs the start and end text.” · “A block needs at least one column (position and line column number).” · “Unknown transformation: X. Use NONE, UPPER, DIGITS, MONEY or SPACES.” | body fields |

**Frequent cases:** *“Could not connect to the mailbox”* → check server, port (993 with SSL), user and the mailbox's **app password** (Microsoft 365: shared mailbox with IMAP enabled). *“Sender not allowed”* with an email that really is theirs → add the domain of the ORIGINAL sender (not the forwarder's). *The email was not read* → it must be **unread** and in the configured folder; the subject must meet the filter (if it does not, it is ignored and leaves no trace).

## 7. SFTP and FTP integration (Batch 134)

> Dictated by Luis (2026-10-19): **SFTP** and **FTP** connectors for any client. Teikem connects to the client's server (or ours), downloads the new files from a folder, runs them through the integration's import template (hash, importer, inbox) and **moves** them to the server's “processed” or “errors” folder. **It never deletes.** Permissions: `integrations.create` + `integrations.create.files`.

**How it is configured** (connector **SFTP** or **FTP** when creating the integration):
- **Server, port** (22 SFTP, 21 FTP) and **user**. **Authentication:** password or, in SFTP, **private key** (PEM text, with its passphrase if it has one). The password or the key is stored **encrypted** with the company's key (`Files:MasterKey`) and **never shown again**: when editing, empty = the stored one is kept.
- **Remote folder** (e.g. `/entrada`), **processed folder** and **errors folder** (inside the remote one, or a full path starting with `/`; created if missing). Processed empty = the files stay where they are and the **hash** prevents processing them twice. Paths do not allow “..”.
- **SFTP: pinned server fingerprint.** Without the server's SHA-256 fingerprint **it does not connect** (so it never talks to an impostor). With the fingerprint empty, **Test connection** (or a run) responds “The SFTP server fingerprint still needs to be confirmed…” and **shows the fingerprint the server presents** (`SHA256:…`): compare it with the one the provider gives and paste it into the field. If the server later presents a different one, the connection is rejected (“The server fingerprint does not match the configured one…”).
- **FTP:** **FTPS** (explicit TLS, with certificate validation) or unencrypted FTP; without FTPS the warning says “Warning: unencrypted FTP; user, password and files travel in clear text.”
- **Which files are taken:** by **type** (`csv`, `txt` by default) and **name** (equals, contains, starts with, ends with, wildcard `ordenes-*.csv` or regular expression), up to 100 per run and in name order; hidden files (`.x`) and those **modified less than 10 seconds ago** (they may still be uploading) are ignored: they wait for the next run.
- **Schedule:** manual or every N minutes (1 to 10080), like the folder and the email. **Run now** and **Test connection** (`POST /{id}/test-connection`, `integrations.manage`) in the list; the test says how many files there are and how many are ready to read, and never shows the password.

**What happens with each file:** it is downloaded, its hash is computed (the same content is not processed twice: “Repeated”), it goes through the importer **on behalf of the service account** and the shipments with problems go to the **inbox**; the file is moved to “processed” (or to “errors” if it could not be processed). If it cannot be moved, it stays where it was and the hash protects it. If the server does not respond, the run ends as *Failed* with “Could not connect to the server: …” and nothing else happens.

| Message (400) | When |
|---|---|
| “Enter the connector server (host).” · “The port must be between 1 and 65535.” · “Enter the connector user.” | server |
| “Enter the connector password.” · “Enter the connector private key.” · “Unknown authentication: X. Use PASSWORD or KEY.” · “The private key is only used with SFTP.” | secret |
| “Enter the remote folder where files arrive.” · “Folders cannot contain ".." or line breaks.” | folders |
| “The fingerprint must be the server SHA-256 (SHA256:… in base64).” | fingerprint |
| “Unknown condition: X. Use EQUALS, CONTAINS, STARTS, ENDS, WILDCARD or REGEX.” · “Enter the condition text.” | name filter |
| “Connector passwords cannot be saved: the server master key (Files:MasterKey) is missing.” | the server has no master key |

**Frequent cases:** *“The SFTP server fingerprint still needs to be confirmed”* → use Test connection, compare and paste the fingerprint. *“The server fingerprint does not match”* → the provider changed it or it is another server; confirm it with the provider before updating it. *“0 ready to read” with files in the folder* → they do not meet the type or the name, or they were modified less than 10 s ago. *FTP that lists but does not download* → passive mode blocked by a firewall; use SFTP or open the passive range. *The file stays in the folder* → it could not be moved (write permissions); the hash prevents duplicating the shipments, but it is advisable to grant write permission.

## 8. API for clients (Batch 135)

> Dictated by Luis (2026-10-19): the client (or their system) can talk to Teikem without a screen, **only about what is theirs**: their shipments, consignees, profile and —depending on the company's modules— their warehouse and their purchase and sales orders. It is activated with a connector of type **Client API** and one or more **keys**.

**How it is configured** (permission `integrations.create` + the one for each family; same form as the other integrations, module `INTEGRATIONS`):
- In **System → Integrations / API → New integration** choose the **Client API** connector. It has no template, folder or schedule. The **scopes** are chosen (only what the creator can grant and what the company has contracted is offered). The integration's service account receives exactly the permissions of those scopes.
- **Client:** required only if a **transport** scope is chosen (shipments, invoices, consignees, profile): the key will only see that client's data (another client “does not exist” for it). The warehouse, sales and purchasing scopes do not ask for it.
- In the list, the **API keys** action (`integrations.manage`) opens the management: **create** (name and optional limit per minute), **change the limit**, **revoke** and view each key's **log**. Up to **10 active keys** per integration.
- **Where it is:** in the Integrations list, the ones of type “Client API” show the **“API keys”** button under the name (also in the row menu). 
- **The secret is shown only once**, when the key is created (with Copy buttons) and, since 2026-10-21, next to a **block ready to copy and paste to the client** with the base URL, the key, the secret, the integration's scopes and the two example commands (request the token and call the API); the “Copy all” button copies it in full. Teikem stores only its SHA-256 hash; afterwards only the `tk_…` key and the last 4 characters are visible. If it is lost, revoke it and create another.

**Scopes and routes** (closed list; everything else responds 403 even if the token is valid; the only `DELETE` is “remove a webhook”):

| Scope | What it opens |
|---|---|
| `orders:read` | `GET /api/v1/orders`, `/orders/{id}`, `/orders/{id}/delivery`, `/files/{id}` |
| `orders:create` | `POST /api/v1/orders` |
| `invoices:read` | `GET /api/v1/billing/invoices`, `/billing/invoices/{id}` |
| `consignees:read` / `consignees:write` | `GET` / `POST` and `PATCH` on `/api/v1/locations` (no delete) |
| `client:read` / `client:write` | `GET /api/v1/clients…` (their own) / `PATCH /clients/{id}/profile` and contacts |
| `warehouse:read` / `warehouse:write` | products, inventory, receipts and manual issues: read / create products, receive, issue, adjust and transfer |
| `sales:read` / `sales:write` | `/api/v1/sales-orders`: read / create, edit and request |
| `purchasing:read` / `purchasing:write` | `/api/v1/purchase-orders`: read / create, edit and send |
| `webhooks:manage` | `/api/v1/webhooks…`: subscribe to their client's notices, test, rotate the secret and view their deliveries (see §9; the API's only `DELETE`: remove a webhook) |

Always allowed with any key: `GET /api/v1/api-auth/me` (who the key is) and `GET /api/v1/catalogs…`.

**How to use it** (3 steps):

```bash
# 1) token (15 minutos): llave y secreto
curl -X POST https://SERVIDOR/api/v1/api-auth/token -H "Content-Type: application/json" \
  -d '{"clientId":"tk_…","clientSecret":"tks_…"}'
# → {"accessToken":"…","tokenType":"Bearer","expiresIn":900,"scopes":["orders:read"],"clientPublicId":"…"}
# 2) llamar con el token
curl https://SERVIDOR/api/v1/orders -H "Authorization: Bearer ACCESS_TOKEN"
# 3) al vencer, pedir otro token con la misma llave
```

- **Usage limit** per key (by default 60 per minute; 1 to 6000): when exceeded, **429** with `Retry-After` (seconds) and headers `X-RateLimit-Limit` / `X-RateLimit-Remaining`. The token exchange is also throttled per address (30 per minute).
- **Log** per key (method, route, code and milliseconds), 30 days.
- **Revoke** stops working immediately (even if the token is still alive) and cannot be reactivated. **Turning off the integration** turns off its service account: live tokens stop working.
- API key sessions do **not** go through the acceptance of legal documents (they are not people).

| Message | Code | When |
|---|---|---|
| “Invalid credentials.” | 401 | wrong key or secret, revoked or expired key, integration or account turned off (it does not say which) |
| “This API key does not have the “X” scope this route requires.” | 403 (`api_scope`) | the route exists but the scope is missing |
| “This API key cannot use this route: it is not part of the integrations API.” | 403 (`api_scope`) | route outside the list |
| “You exceeded the key's usage limit; try again in N s.” | 429 | limit per minute |
| “Enter a name for the key.” · “Maximum 80 characters.” | 400 | key name |
| “The usage limit must be between 1 and 6000 requests per minute.” | 400 | limit |
| “An integration can have up to 10 active keys; revoke one.” | 409 | too many keys |
| “The key is already revoked.” | 409 | revoking or editing a revoked one |
| “Only API-type integrations have keys.” | 400 | keys on another connector |
| “The API's transport scopes need the client: the key only sees that client's data.” | 400 | client missing |
| “An API-type integration only accepts API scopes.” | 400 | file scopes on an API (and vice versa) |

**Frequent cases:** *“My system receives 403”* → the scope for that route is missing: add it in the integration (it applies on the next token). *“I see 429”* → raise the key's limit or space out the calls (`Retry-After`). *“I lost the secret”* → revoke and create another key. *“The key sees nothing”* → it only sees the data of the integration's client.

## 9. Outbound webhooks and outbox (Batch 136)

> Dictated by Luis (2026-10-19): Teikem notifies the client's system over HTTP when something happens with their data, with a signature, retries and an outbox for what could not be delivered. It requires an integration of type **API** with a **client** and the **`webhooks:manage`** scope.

**Who and how** (module `INTEGRATIONS`):
- **Staff** (System → Integrations / API): the integration's **Webhooks** action (`integrations.manage` to create, test, rotate, resume and remove; `integrations.view` to view). It only appears on API integrations with the `webhooks:manage` scope.
- **The client themselves**, with their key: `GET/POST /api/v1/webhooks`, `GET/PATCH/DELETE /api/v1/webhooks/{id}`, `POST /api/v1/webhooks/{id}/test`, `POST /api/v1/webhooks/{id}/rotate-secret`, `GET /api/v1/webhooks/{id}/deliveries`. They only see and touch the webhooks of **their** integration. A person (not a key) receives 403 “This route is for API keys; staff manage webhooks from Integrations.”.
- Up to **10 webhooks** per integration. `DELETE` only **removes** the webhook (it is deactivated and stops appearing; the history stays): it is the API's only `DELETE`.

**Events** (`events`, at least one): `order.status_changed` (each status change of a client shipment; a delivery that skips statuses sends one per status), `order.delivered` (the shipment was delivered) and `invoice.issued` (a client invoice was issued). Only events about **the client's data** of the integration go out. Plus `webhook.ping` (the test), which is not subscribed to.

**What the client receives** (POST, `Content-Type: application/json`; minimal body: the detail is queried through the API with the id):
```json
{"id":"7f0c…","type":"order.delivered","createdAt":"2026-10-19T14:03:11Z",
 "data":{"orderId":"3b1e…","orderNumber":"ORD-000123","status":"DELIVERED","deliveredAt":"2026-10-19T14:03:11Z"}}
```
Headers: `X-Teikem-Event` (type), `X-Teikem-Delivery` (the body's `id`: use it to avoid processing a repeat twice), `X-Teikem-Timestamp` (Unix seconds) and `X-Teikem-Signature` = `sha256=` + HMAC-SHA256 in hexadecimal of `“timestamp”.“body”` with the webhook's **secret**.

**How to verify** (receiver): recompute the signature with the secret, compare it in constant time and **reject if the timestamp is older than 5 minutes** (against replay).
```js
const expected = 'sha256=' + crypto.createHmac('sha256', secret).update(`${timestamp}.${rawBody}`).digest('hex')
const ok = crypto.timingSafeEqual(Buffer.from(expected), Buffer.from(signature)) && Math.abs(Date.now()/1000 - Number(timestamp)) < 300
```
Respond **2xx** in less than **10 seconds**; anything else (or not responding) counts as a failure.

**The secret** (`whsec_…`) is shown **only once**, when the webhook is created or when it is **rotated** (the previous one stops working right away). It is stored encrypted; afterwards only its ending is visible.

**Retries and outbox:** a notice that fails is retried at **1 minute, 5, 30, 2 hours and 12 hours** (6 attempts in total). Once exhausted, it stays **Undelivered** in the **Outbox** tab (`integrations.inbox`) with the exact reason (“The server responded 500.”, “Timed out (10 s).”, “Could not connect: …”), the body and the **Retry** (back to pending with attempts at zero) and **Discard** buttons. The tab shows the undelivered counter. Deliveries are kept for 30 days.

**Automatic suspension:** **5** consecutive undelivered deliveries suspend the webhook (it stops generating new notices); the people defined in the integration are notified with a system notice. It is **resumed** (staff: **Resume**; also by editing it and turning it on) and while it is suspended its deliveries cannot be retried.

**Security:** the URL must be **https**, without user or password and **public**: `localhost`, names without a domain, `*.local`/`*.internal` and private, loopback or link-local addresses are rejected (the connection also checks the already-resolved address, against DNS rebinding), and redirects are never followed. (`Integrations:Webhooks:AllowInsecure` is only turned on in development and testing.)

| Message | Code | When |
|---|---|---|
| “Enter the URL the notices will be sent to.” · “The URL cannot exceed 500 characters.” | 400 | URL |
| “The URL is not valid: it must be a full address (https://…) without user or password.” | 400 | malformed URL |
| “The URL must use https.” · “The URL points to an internal or local address, which is not allowed.” | 400 | URL not allowed |
| “Choose at least one event.” · “Unknown event: X. Use order.status_changed, order.delivered, invoice.issued.” | 400 | events |
| “Webhooks need an integration with a client: only that client's data is notified.” | 400 | integration without a client |
| “The integration does not have the “webhooks:manage” scope.” | 400 | scope missing |
| “Only API-type integrations have webhooks.” | 400 | another connector |
| “An integration can have up to 10 webhooks; remove one.” | 409 | too many |
| “The webhook is not suspended.” | 409 | resuming one that is not |
| “Only an undelivered delivery can be retried.” · “The webhook is suspended: resume it before retrying.” | 409 | retry |
| “The delivery was already discarded.” · “The delivery is no longer pending.” | 409 | discard |
| “Webhook secrets cannot be saved: the server master key (Files:MasterKey) is missing.” | 400 | server without master key |
| 404 “Webhook” / “Delivery” | 404 | does not exist (or belongs to another integration, or was already removed) |

**Frequent cases:** *“I don't get notices”* → the webhook is suspended or turned off, the integration is turned off, the event is not checked or the shipment belongs to another client; check the Outbox tab. *“The signature does not match”* → use the **raw** body (without reformatting) and the current secret (was it rotated?). *“I receive the same notice twice”* → it is a retry (the first response did not arrive in time): discard by `X-Teikem-Delivery`. *“Test says it could not connect”* → the URL must be public and https.

**Outside this batch:** the output of **files** by event or by schedule (SFTP, email or the client's API) needs the output mapping; it arrives with Batch 137.

## 10. File format: Excel, JSON and XML (Batch 137)

> Dictated by Luis (2026-10-19): integrate anything **by configuration**. Besides CSV, a file integration (folder, email, SFTP, FTP or manual upload) reads **Excel sheets (.xlsx)** and **JSON** and **XML** files with a path mapping. Everything is converted to the CSV of the integration's **import template** and follows the usual path: hash (the same content is not processed twice), importer on behalf of the service account, several lines per order and “no piece” rules (Batch 131), and the inbox.

**Where:** System → Integrations / API → create or edit an integration → **File format** section (does not apply to API-type ones; `integrations.create` / `integrations.manage`).

| Format | What it reads |
|---|---|
| **Automatic** (default) | By extension: `.xlsx` → Excel, `.json` → JSON, `.xml` → XML, everything else → CSV. (It also recognizes an Excel workbook by its signature.) |
| **CSV** | As always (the template's delimiter and encoding). |
| **Excel (.xlsx)** | One **sheet**: the one typed (case-insensitive) or, if empty, the first one. Each cell is read as the person would see it (integers without decimals, leading zeros according to the format, dates `yyyy-mm-dd`…). The old `.xls` is not accepted. |
| **JSON / XML** | With **path mapping**: where the shipments are, where their lines are (optional) and where each template column comes from. |

**Which files each connector takes:** the **input folder** takes the format's extensions (Automatic: `.csv`, `.txt`, `.xlsx`, `.json`, `.xml`; Excel only `.xlsx`; JSON only `.json`; XML only `.xml`; CSV `.csv` and `.txt`) and leaves the rest where they are. In **email, SFTP and FTP** the connector's list of **file types** rules (by default `.csv` and `.txt`): add `.xlsx`, `.json` or `.xml` there. **Manual upload** accepts any (the format is decided by the configuration or the extension).

**The path mapping (JSON and XML).** A path is `$.a.b[0].c`:
- `$` = the document; `.field` enters a field; `[*]` = all the elements of a list; `[0]` = one. A single element is treated as a list of one (`[*]` works with one or with several).
- **XML:** the path starts with the name of the root element (`$.Pedidos.Orden[*]`); an **attribute** is written `@attribute` and the **text** of an element that also has attributes, `#text`; repeated elements form a list.
- **Shipments path** (required, starts with `$`): where each shipment is. **Lines path** (optional, starts with `@`, relative to the shipment): each line is a row; a shipment without lines yields one row with the line part empty.
- **Columns:** the column **name** and the **value path**, in the **order the template expects** (the first is column 1). With lines, `@.x` reads from the line, `^.x` from its order and `$.x` from the document; without lines, `@.x` reads from the shipment. A value is text, number (`12.5`) or `true`/`false`; an object, a list or a missing value yield empty.

JSON example (shipments with lines): `{"orders":[{"ref":"A-1","to":{"name":"Colmado Uno"},"items":[{"type":"Box","label":"PCS-1"},{"type":"Invoice","label":"INV-1"}]}]}` with shipments path `$.orders[*]`, lines path `@.items[*]` and columns `Order ← ^.ref`, `Type ← @.type`, `Label ← @.label`, `Consignee ← ^.to.name`. XML example: `<Pedidos><Orden ref="A-1"><Item tipo="Box"/></Orden></Pedidos>` with `$.Pedidos.Orden[*]`, `@.Item[*]` and columns `^.@ref`, `@.@tipo`.

**Test with a file** (in the section): a sample file is uploaded and you see **how the template would read it** (detected format, workbook sheets, header and the first 20 rows) without saving anything or creating shipments (`POST /api/v1/integrations/format/preview`, multipart: `file`, `format` as JSON, `delimiter`, `hasHeader`).

**Security and limits:** the file is untrusted input: XML **does not allow DTD or external entities**, JSON has a maximum depth (64), nothing is executed. Same limits as CSV: **2 MB** and **5,000 rows**.

| Message | Code | When |
|---|---|---|
| “Unknown format: X. Use AUTO, CSV, EXCEL, JSON, XML.” | 400 | format |
| “The sheet name cannot exceed 31 characters.” | 400 | sheet |
| “Enter the shipments path (for example $.orders[*]).” · “The shipments path must start with $ (for example $.orders[*]).” | 400 | shipments path |
| “The lines path must be relative to the shipment: start with @ (for example @.lines[*]).” | 400 | lines path |
| “The path “X” is not valid. Use the format $.field.other[*] (also @.field or ^.field).” | 400 | any malformed path |
| “The path “^.x” uses ^ (the shipment), which is only allowed when there is a lines path.” | 400 | `^` without lines |
| “Add at least one column to the mapping.” · “The mapping allows up to 60 columns.” · “Every column needs a name.” · “Two columns are named “X”.” | 400 | columns |
| “An API-type integration has no file format.” | 400 | format on an API integration |

Errors when **processing a file** (the file is marked “With errors” and goes to the **inbox** with the reason “File X: …”; nothing is created):

| Reason | When |
|---|---|
| “The file is JSON, but the integration has no path mapping: configure “File format”.” (same with XML) | a .json/.xml arrived at an integration without mapping |
| “Could not read the JSON file: …” / “Could not read the XML file: …” | broken or malformed file |
| “The XML file contains a DTD declaration, which is not allowed.” | XML with DTD |
| “The shipments path “$.orders[*]” did not find any record in the file.” | the path finds no shipments (wrong mapping or empty file?) |
| “The workbook does not have the sheet 'X'.” | the typed sheet does not exist |
| “The file is not an Excel workbook (.xlsx).” | Excel format with a file that is not one (or an old .xls) |
| “The file exceeds the limit of 5,000 rows or 2 MB.” | limits |

**Frequent cases:** *“It did not find any record”* → test the sample file: in XML the path starts with the name of the root element (`$.Pedidos…`). *“A shipment comes out with an empty description”* → check that the column is in the position the template uses. *“My email/SFTP integration ignores .xlsx files”* → add `.xlsx` to the connector's file types.

## 11. Outbound integrations: files over SFTP/FTP and outbound API (Batch 137b)

> Dictated by Luis (2026-10-19): Teikem also **sends** data, by configuration: the client's shipments that reach a status (e.g. “Delivered”) go out in a file (CSV, JSON or XML) over **SFTP or FTP**, or through the **client's system API** (generic HTTP). It is the file output by event or schedule that was missing (webhooks, §9, cover the minimal notice per event).

**How it is created** (System → Integrations / API → New integration → **Direction: Outbound**; `integrations.create` + `integrations.create.files`, scope **“Shipment output by file or API”**, transport module and a **client**: only that client's shipments go out). Outbound connectors: **Outbound API (HTTP)**, **SFTP** and **FTP** (email is not outbound).

**What is sent** (“What is sent” section):
- **Statuses that trigger the output** (one or several): Submitted, Pickup, Unassigned, Assigned, In transit, Delivered, Failed, Cancelled, In warehouse, Returned. Each order that **reaches** one of them enters the queue (a shipment does not enter twice while waiting).
- **When it is sent:** **Immediately** (each shipment separately, in less than a minute by the “Integration output” job) or **On a schedule** (every N minutes, 1 to 10080, a single send with all those that arrived since the previous one; up to 1,000 shipments per send).
- **Format:** **CSV** (comma, semicolon or tab delimiter; header and `\r\n` line ending), **JSON** (`{"orders":[{…}]}`; numbers come out as numbers, dates in ISO 8601 UTC and what is missing as `null`) or **XML** (`<orders><order><campo>…</campo></order></orders>`, UTF-8 without BOM).
- **Columns:** the **name** (in JSON/XML it must start with a letter or underscore and contain letters, numbers, hyphen or underscore) and the **shipment field**: shipment number, waybill, client invoice, status (code and name), creation date, promised date, pieces, weight, COD amount, quoted freight, notes, consignee, delivery address, town and postal code, delivery date and time, who received it and delivery notes. By default: shipment, status, delivery date and who received it. Up to 40 columns.
- **File name** (SFTP/FTP): supports `{date}` (yyyymmdd), `{time}` (hhmmss), `{id}` (shipment number) and `{ext}`; by default `ordenes-{date}-{time}.{ext}`. The file is uploaded with a provisional `.part` name and renamed when finished (whoever picks it up never sees it half-written) and **never overwrites** an existing one (it prefixes the date). The destination folder is the connector's “remote folder” (created if missing); SFTP requires the pinned server fingerprint, as in the inbound side (§7).
- **Outbound API (HTTP):** URL (**https and public**, like webhooks), method **POST** or **PUT**, authentication **None**, **Token (Bearer)**, **User and password (Basic)** or **Header with a key**, and up to **10 additional headers** (not Authorization, Host or Content-Type). The token, the password or the header value are stored **encrypted** and never shown again. Each call carries `Idempotency-Key: teikem-output-{id}` and `X-Teikem-Output`; respond **2xx in less than 10 s**; redirects are not followed.

**List actions:** **Send now** (builds the queue and sends what is due), **Preview** (example with the client's latest shipments in those statuses —if there are none, the client's latest ones—; sends nothing), **Test connection** (SFTP/FTP: connects and lists the folder; HTTP: sends a test body without shipments and says “The API responded 200.”), **History** and **Edit**. An outbound integration **does not receive files** and when turned off it does not send.

**Retries and outbox:** a send that fails is retried at **1 minute, 5, 30, 2 h and 12 h** (6 attempts); once exhausted it stays **Undelivered** in **Outbox → “File and API outputs”** with the exact reason (“The server responded 500.”, “Timed out (10 s).”, “Could not connect: …”), the content and **Retry** / **Discard**. The tab counter adds up webhooks and sends. They are kept for 30 days. If a destination fails 3 times in a row in one pass, the rest wait for the next one.

| Message | Code | When |
|---|---|---|
| “Enter what is sent (statuses, format and columns).” | 400 | output missing |
| “Choose at least one status that triggers the output.” · “Unknown status: X.” | 400 | statuses |
| “Unknown trigger: X. Use EVENT or SCHEDULE.” · “Scheduled output needs an interval (minutes).” | 400 | when it is sent |
| “Unknown output format: X. Use CSV, JSON or XML.” · “The delimiter must be comma, semicolon or tab.” | 400 | format |
| “Add at least one column to the output.” · “The output allows up to 40 columns.” · “Every column needs a name.” · “Two columns are named “X”.” · “Unknown field: X.” | 400 | columns |
| “The column name “X” must start with a letter or underscore and contain only letters, numbers, hyphen and underscore (for JSON and XML).” | 400 | column name in JSON/XML |
| “The file name cannot contain folders or line breaks.” · “Unknown placeholder in the file name: {x}. Use {date}, {time}, {id} or {ext}.” | 400 | file name |
| “Enter the API configuration (URL, method and authentication).” · “Unknown method: X. Use POST or PUT.” · “Unknown authentication: X. Use NONE, BEARER, BASIC or HEADER.” | 400 | outbound API |
| “Enter the user (BASIC) or the header name (HEADER) of the authentication.” · “Enter the token, password or header value of the authentication.” | 400 | authentication |
| “The header “X” is not valid (name not allowed or value with line breaks or longer than 500 characters).” · “Up to 10 additional headers are allowed.” | 400 | headers |
| “The URL must use https.” · “The URL points to an internal or local address, which is not allowed.” | 400 | URL (same as webhooks) |
| “An outbound integration only accepts the shipment output scope.” · “An inbound integration does not accept the shipment output scope.” | 400 | scopes |
| “An outbound integration uses SFTP, FTP or HTTP.” · “The HTTP connector is only for outbound integrations.” | 400 | connector |
| “Enter the client for this integration's shipments.” | 400 | no client |
| “This integration is outbound: it does not receive files.” | 400 | uploading a file |
| “There are no shipments pending to send.” | — (run message) | “Send now” with an empty queue |
| “Only an undelivered send can be retried.” · “The send was already discarded.” · “The send was already delivered.” | 409 | outbox |
| “The output exceeds the allowed size (2 MB); use a more frequent output or fewer columns.” | (undelivered send) | content too large |

**Frequent cases:** *“Nothing goes out”* → the integration is turned off, the status is not the chosen one, the shipment belongs to another client, or it reached the status **before** the integration was created (only those that arrive afterwards go out). *“Scheduled does not send”* → the scheduled integrations job runs every minute; use “Send now” to test. *“Undelivered: Could not connect”* → check URL, firewall and, in SFTP, the fingerprint. *“The file arrived twice”* → it is a retry after not receiving the response: use the `Idempotency-Key` (API) or the shipment number in the file name.

## 14. “Templates” screen (from Integrations, 2026-10-21)

Dictated by Luis: view, create, edit, deactivate and reactivate import templates **without going through the Import wizard**. The same templates serve that wizard and the integrations.

- **Where:** **Templates** button at the top of System → Integrations (`/system/templates`; it is not a menu item, which stays as in the mockup). Anyone with `integrations.view` and the Integrations module can enter.
- **Tabs per entity:** **Shipments** (transport orders; viewing and changing requires `orders.edit`), **Consignees** (requires `locations.create`), **Products** (`inventory.manage`), **Suppliers** (`purchasing.manage`), **Warehouses** and **Bins** (`warehouse.manage`). Without the entity's permission the tab only says which one is needed: “You need the {perm} permission to view and change these templates.”.
- **List:** name, client (or “All clients”), the “column → field” summary, how many columns and state (Active/Inactive). Active ones come first and then by name; inactive ones are always shown.
- **Actions:** *New template* and *Edit* open the same editor each Import wizard already uses (for consignees with variants, column joins and default client); *Deactivate* and *Reactivate* do not delete anything. The error messages are those of each editor's server (see manual 03 §7 and the consignees one).
- **From the integration:** when creating or editing an inbound integration (folder, email, SFTP/FTP or manual upload) there is a **“New template”** button under the template selector (needs `orders.edit`): it opens the same editor, and when the template is saved it is selected in the integration, without leaving it.
- **Not yet:** templates for the other entities (sales orders, clients, drivers, receipts, dispatches, adjustments). They remain in the P11 plan (products, suppliers, warehouses and bins are already there, see manual 06 §1b, §2b and §8b).

## 15. Product output by Integration with the template (2026-10-21)

Dictated by Luis: the entity's template (column → field) also serves to **send out** products over SFTP, FTP or outbound API, **on a schedule and on change**. It is one more **outbound** integration, with “What is sent?” = **Products (catalog)**.

- **How it is created:** Integrations → New integration → Direction **Outbound** → “What is sent?” **Products (catalog)** → product template (the same one used to import them; “New template” creates it right there) → connector (SFTP, FTP or HTTP), when it is sent, format (CSV, JSON or XML), file name and destination. **It has no client or statuses.** Scope: `files.catalog_export` (“Product output by file or API”, requires `inventory.view` and the Inventory module with lots and serials; the Files family like the other file integrations).
- **What goes out:** the **template's columns, in their order** (the header is the field name: `sku`, `name`, `salePrice`…). All the fields of the product import are available (including owning client code, base unit, tracking, packaging and rotation). JSON: `{"products":[…]}`; XML: `<products><product>…`.
- **On change:** when a product is **created, edited, deactivated or reactivated**, it is noted for each product output integration that is turned on and goes out in its next send. A product that changes several times before going out goes **only once**. With the **Immediately** trigger one send per product goes out (in less than a minute); with **On a schedule** a batch of up to 1,000 products goes out when the schedule is due.
- **Send the whole catalog:** the **“Send the whole catalog”** action (`POST /api/v1/integrations/{publicId}/send-all`, `integrations.manage`) in the integration's row: it notes **all** products and runs the integration now (initial load or to resynchronize). One run sends up to 5,000 products; what is left over goes out in the next pass.
- **Preview and test:** “Preview” (when editing) builds the file with the first 3 products; “Test” of the outbound API works the same as in the shipment ones. Retries, log and “Outbox” are the usual ones (§11).
- **Only changes made after the integration was created go out** (as in the shipment ones); for what already exists, “Send the whole catalog”.
- **Limit:** a send does not exceed 2 MB (error “The output exceeds the allowed size (2 MB); use a more frequent output or fewer columns.”).

| Message | HTTP | What to do |
|---|---|---|
| “Choose the entity's template: it says which column carries each field.” | 400 | The product template is missing (or has no known columns). |
| “Unknown entity for the output: X. Use PRODUCT.” | 400 | Invalid `entityKind` (only PRODUCT for now). |
| “This integration does not send a catalog.” | 400 | “Send all” only exists on product integrations. |
| “An outbound integration only accepts the shipment output scope (or the product output scope if it sends a catalog).” | 400 | Each output accepts its own scope. |
| “An inbound integration does not accept the shipment or product output scope.” | 400 | |
| “The integration is turned off; turn it on to run it.” | 422 | Turn the integration on. |

**Frequent cases:** *“I changed a product and it did not go out”* → the integration must be turned on, be of type Products and have been created before the change; with a schedule, it goes out when due. *“I want everything again”* → “Send the whole catalog”. *“Columns are missing”* → add them to the product template (Integrations → Templates): the output uses the template as it is at each send.

## 16. Arrival and exit notices from a document (2026-10-23)

A reader of an email integration can send what it reads to an **arrival notice** (what enters the warehouse, `LLE-#####`) or to an **exit notice** (what leaves, `SAL-#####`), besides a transport order. It serves for documents the client sends by email, such as Axiscare's “Despacho de Mercancía” or “Envío de Mercancía”. Who can: `integrations.create` or `integrations.manage` to set up the integration; the notices are created on behalf of the integration's service account.

**How it is set up (Integrations screen → email → Readers).**
1. Each document type is **one reader** of the same integration. They are told apart by a condition on the **document text** (for example, that it contains “Despacho de Mercancia”), not by the subject: documents with the same template and a different title each go their own way.
2. The **“Use a model”** buttons (next to “Add reader”) add a ready-made one: *Goods dispatch → exit notice*, *Goods shipment → arrival notice* and *Drive Medical invoice → arrival notice*. Then choose the reader's **client** (the owner of the goods).
3. The integration still asks for an import template: use one without a header row and with comma (the reader delivers the data by column position).
4. To build your own reader, the column of each field is: **1** reference, **2** product (the client's SKU, as it appears in the document), **3** quantity, **4** destination (exit only), **5** date (the required date for exit, the expected date for arrival; MM/dd/yyyy or yyyy-MM-dd) and **6** lot (arrival only).

**What it does.**
- A line of the document is a line of the notice. Lines with the same reference, destination and date form **a single notice** (a dispatch with four destinations creates four exit notices). The same product repeated is added up.
- Quantities are read by their number: “400bx” is 400, “1,430” is 1430. In the Axiscare sheet the quantity is that of “CANTIDAD EACH” (units), not the pallets.
- The warehouse is the company's only active one; if there are several, the notice is not created and the inbox explains it.
- **A notice is never half-created:** if a line of the document cannot be read (product that does not exist for that client, illegible quantity), **no** notice of that document is created and everything stays in the inbox with the row number and the reason.
- The same PDF (same content) is not processed twice.

**Reading scanned PDFs (OCR).** PDFs with real text are read directly; scanned pages go through OCR at 288 dpi (scale 4) and with two page modes, keeping the one that recognizes more. The decision is **per page**: a PDF with some text pages and some scanned ones reads each in its own way. A scan with lines or handwriting may come out with errors; that is why what is illegible goes to the inbox instead of being guessed.

**Known limitations.** (1) In the dispatch, the destination stored is the first line of the address block (the recipient's name), not the full address. (2) If the sheet only has one number after the item, there is no way to know whether it is the “cantidad each” or the “pallets”: it is taken as the quantity. (3) Drive Medical's scanned invoices have hand-circled quantities that OCR does not always read: those lines send the whole document to the inbox.

## 17. Documents with several orders: per-record readers (2026-10-23)

Some documents bring **one order per page** and, before the first one, a common header (the CMS route sheet: route, driver and date; then one order per stop). A reader becomes **per record** with the **“Text that starts each record”** field (for example `Order #:`): each line of the document that contains it starts a new order, and that order is read with the reader's fields until the next one.

- **“From the header” field:** a field marked this way is read only once from what comes before the first record (the route, the driver) and applies to all the orders.
- **“Join to the previous one with”:** if two fields go to the **same column**, the second is appended after the first with that text in between (for example ` | `). It serves to join several notes in the Notes column.
- **“Collapse spaces and tabs” transformation:** an OCR splits “ERD Pick Up 1” into cells; with this transformation it becomes a single text again.
- A record missing a required field says so by its number (“Record 2: …”) and the whole document goes to the inbox; if the start text does not appear in the document, the reason is “No record was found in the document (the record start text does not appear).”.

**“CMS route sheet → pickup orders” model.** Added with **Use a model**. Conditions: the document contains “ROUTE SHEET #” and “Order Entry Date”. Each order in the document is a **pickup at the patient's home that is taken to CMS**:

- The **patient's address is the PICKUP stop** of the order and the **consignee is CMS**, a fixed address configured in the template (“consigneeCode” column with **default** value = the code of the CMS consignee, created beforehand in the client's directory). If the patient already exists in the client's directory (same name and address) it is reused; if not, it is created.
- The **route** (the driver's name on the sheet, for example “ERD Pick Up 1”) is a **dispatch zone**: create in Fleet → Zones a zone whose **code or name** is exactly that text and assign it to the drivers who work it (a driver can cover several zones). The shipment's zone is that one and **not** the one that would come from the ZIP. A route that does not exist as a zone sends the order to the inbox (“The zone or route “…” does not exist or is inactive in the company.”): create the zone and reprocess.
- The **order number and the shipment number** are CMS's **Order #**. For that, the CMS client must have **“The client assigns the shipment number”** turned on; if not, the row says “The shipment number is assigned by Teikem for this client.”.
- The **service type** (the one the pickup carries) is also set as a default value of the template.

Import template columns (no header, comma, **several lines per shipment** turned on): **1** orderNumber, **2** waybillNumber, **3** pickupName (the patient), **4** pickupLine1 (address), **5** pickupCity (town), **6** pickupPostalCode (ZIP), **7** contactPhone (patient's phone), **8** requestedDate (delivery date), **9** notes, **10** zone (the route) and **11** description (the piece). Each “Pick Up” item is a line of the order (one piece); the “New DME … NOTES” line is not a piece: its text is joined to the delivery instructions in the **notes**. The OCR quantity is not used (it reads badly: a “1” comes out as “4”). The document's health data (HIC, date of birth, MT ID) **are not read**: only what the reader declares comes in.

## 18. Closing orders by document: the proposals queue (2026-10-23)

The **ends of a flow do not happen on their own**. A reader with destination **“Order closing (proposal)”** reads from a document which orders were resolved (CMS's “Ruta completada” sheet, for example) and, for each order, leaves a **proposal**: “take order X to Pickup because this document says so”. A person **confirms** or **rejects** it in **Integrations → Proposals**. Permission: **integrations.inbox** (the same as the inbox). The tab shows in parentheses how many are waiting.

**How it is set up.** In the reader: destination “Order closing (proposal)”, **What it does with what it finds** (**Suggest**, by default: a person confirms; **Automatic**: closes the order at once, on behalf of the integration's service account, and is used only when that reader has already proven it is accurate) and **Take the order to** (**Pickup** or **Delivered**). Column **1** of the reader is the key by which the document names the order (in CMS, the **Referral #**, which is the order's **Order #**); the other columns are evidence. There is a ready-made model: **Use a model → CMS completed route → close pickups (proposal)**; it only takes the lines of type **PU** (the note lines do not count).

**How it finds the order.** By its order or shipment number, among the reader's client's **active orders**. A key without an order goes to the **inbox** (“There is no order “X” of client …”). An order with a proposal to confirm, confirmed or applied for that same status **does not generate another** (the same document twice does not duplicate proposals). Several lines of the document with the same key are **one** proposal.

**What happens on confirming.**
- **Pickup:** the order goes through the status pipeline (skipping whatever intermediate statuses are missing) up to “Pickup” and the log entry “Closing by document: …” is kept. If the order was already in “Pickup” or further along, it is not touched and the proposal stays **Confirmed** with “The order was already in that status.”.
- **Delivered:** the delivery is recorded with the proof (“Per document: …”), like the delivery recorded from the web.
- The order changes **on behalf of whoever confirms** and who, when and their note are saved. If the order **no longer allows it** (cancelled, in a final status, the closing status is not enabled…), the proposal stays **Failed** with the reason and can no longer be confirmed again.
- **Reject** leaves the order as it was.
- In **Automatic** mode the proposal is born **Applied** (or **Failed**, and it also goes to the inbox with the reason).

**States:** To confirm (PENDING), Confirmed (CONFIRMED), Applied (APPLIED, done by the automatic reader), Rejected (REJECTED) and Failed (FAILED). API: `GET /api/v1/integrations/proposals?status=`, `POST …/proposals/{id}/confirm` and `…/reject` (optional body `{ "note": "…" }`); 409 “The proposal was already resolved.” if it already had a decision.

## 19. Creating the Axiscare and CMS integrations with SQL scripts (2026-10-23)

The configuration of the email integrations of **Axiscare (Advance Depot)** and **CMS (Advance Logistics)** is created with two separate SQL scripts, **outside `logistica-db-update.sql`**, which are run by hand (`scripts/integraciones/axiscare-depot.sql` and `scripts/integraciones/cms-advance-logistics.sql`; instructions in `scripts/integraciones/README.md`). They are **idempotent** and the integrations are born **turned off**.

- **Parameters** at the beginning of each script (company, client, warehouse or service type): if something does not exist they stop with a clear message (“The company indicated in @TenantName does not exist.”, “There is no active warehouse with the name in @WarehouseName in that company.”, “The service type in @ServiceTypeCode does not exist.”…).
- Each integration is created the same way as from the screen: its **role** and its **service account** (without a password: it does not sign in), with the permissions of the “Shipment input by file” scope. What the SQL **cannot** set is the **mailbox password** (the application stores it encrypted): in **Integrations → Edit** you set the server, the user and the password, the allowed senders, and turn it on.
- **Reader warehouse.** An arrival or exit notice reader has its **Notice warehouse**; without it, the company's only active warehouse is used and, if there are several, the notice is not created (the reason goes to the inbox). The Axiscare scripts leave it set.
- **Axiscare:** the products in the documents must exist with the same SKU as active products of the client. **CMS:** drivers must be assigned to the “ERD Pick Up N” zones and the missing zones must be created.

## 20. Doubtful reading of a scanned PDF: OCR confidence (2026-10-23)

For each scanned document, OCR leaves the **confidence** with which it read its words (0–100). The confidence of the document's **numbers** (order numbers, SKUs, quantities), which are the ones that matter, is stored. Each PDF reader has a **Minimum reading confidence (%)** (empty = **80**; **0** = not monitored). If the confidence falls below it, the reading is **doubtful**:
- What was going to be created **is created anyway** (orders, arrival and exit notices); it is not sent to the inbox.
- The file is marked **“Doubtful reading N %”** in the integration's history and its message says “Doubtful reading (N % < M %): verify what was created against the document.”.
- The run **notifies** (bell and email, to those defined in the integration): “Finished with a doubtful reading: verify what was created”, with the file names and their confidence.
- A **closing** reader (proposals) **does not apply on its own** even if it is Automatic: the proposal stays **to confirm**.
- A real-text document (Axiscare's PDFs) or a CSV does not go through OCR: they have no confidence and are not marked.

**Confidence is a signal, not a guarantee.** Measured with Axiscare's scanned waybills, it does not detect what the OCR **failed to read** (a whole lost column does not lower it: a worse reading scored 86 % and a complete one 82 %), and a well-read document can score 88–91 %. That is why the default minimum is 80 and it is adjusted with real data: each file stores its confidence to calibrate it; the format and totals validations remain the main defense.

## 21. Completeness checks: what was read against the document's totals (2026-10-23)

OCR confidence (section 20) **does not see what the OCR failed to read**. That is why a reader can carry **completeness checks** that compare what was read with the totals the document itself declares. If something does not match, **the reading is doubtful** (the same as with low confidence: it is created anyway, the file is marked, the run notifies and a closing stays to confirm) and the reason appears in the file's message (“Doubtful reading: the read sum of “Total Amount USD” is 92141.13 and the document declares 92975.53. Verify what was created against the document.”).

In the reader, section **Completeness checks → Add check**:
- **Lines read = lines the document declares** (`LINE_COUNT`): for example `INVOICES:\s*(\d+)` against the lines read from the waybill. With **Count distinct values** a key repeated in several lines (an order number) counts once. Default tolerance: 0.
- **Sum of a column = total the document declares** (`SUM`): for example `Total Amount USD\s*([\d,]+\.\d{2})` against the sum of the amounts column. Default tolerance: 0.01.
- **Total expression:** finds in the document text the number it declares (group 1 if any); if there are several matches **the last** one is used (a total goes at the end). **If the total does not appear, the check also fails**: normally the document has it, and its absence may be a page that did not arrive.
- **Column** = the read column (1, 2, 3…) that is counted or summed. In a per-record reader (section 17) each record is checked against **its** text.

**Calculated column (÷).** When a value cannot be read (the quantity handwritten in a circle on a scanned invoice), a column of a block with an expression per line can be **one group ÷ another**: in “Columns” you write `3=4/3` (column 3 = group 4 ÷ group 3). This is what the **Drive Medical invoice** model uses: the quantity comes from **amount ÷ unit price** (for example 10,440.80 ÷ 130.51 = 80, the number that was handwritten) and the sum of the amounts is compared with the invoice's “Total Amount USD”.

**Tested with the real Drive Medical invoices.** 2193 (2 pages): the 7 quantities come out exact (80, 15, 19, 4, 80, 110, 240) and the sum matches the total (39,167.01). 2194 (4 pages): the OCR read 17 lines adding up to 92,141.13 against the document's 92,975.53, **and the check flags it**: it is exactly the failure that confidence does not detect. In addition, two SKUs came out with an OCR error (“12445KD-”, “13031-1S8VY”): those go to the inbox as “Product … does not exist”.

## 22. Which documents declare totals and which check each reader carries (2026-10-23)

Everything is configured in the **reader** (“Completeness checks” section), not in the code: if a document declares a total, the check is set; if not, none is invented.

**Two new mechanisms for the missing cases:**
- **Lines read = items the document lists** (`MATCH_COUNT`): for a document that does not say “there are N” but **lists** what it carries (the stops table of a route sheet). The expression describes **one line** of the list and the lines of the text that match are counted.
- **Scope** of the check: *Each record* (default), *The header against all rows* (what comes before the first record) or *The whole document against all rows*. In a per-record reader, the header's stops table is compared with **all** the orders read.

| Document | Does it declare something verifiable? | Reader check | State |
|---|---|---|---|
| **Drive Medical invoice** (Axiscare, scanned) | “Total Amount USD” at the end | **Sum** of the amounts = the total (`Total Amount USD\s*([\d,]+\.\d{2})`) | **Set** in the model; tested with the real invoices |
| **CMS route sheet** (orders) | Lists the stops on the first page | **MATCH_COUNT** in the header: listed stops = distinct orders read | **Set** in the model; tested with the real sheet |
| **Goods dispatch** (Axiscare) | No: rows numbered 1–30 without a total | None (it is an Excel PDF with exact text) | — |
| **Goods shipment** (the receipt) | A “TOTAL” row that comes empty in the samples | None (a check fails if the total is missing: it would only be enabled if they always fill it; recipe: `SUM` of the quantities column against `TOTAL\s*([\d,.]+)`) | — |
| **CMS completed route** (closings) | Declares no totals | None: each line is the evidence of a proposal a person confirms | — |

**Documents that declare totals but do not have a reader yet** (the waybills have not been integrated yet; this is the recipe for when it is done):
- **Standard Axiscare waybill** (Regular, Oxygen, Pickups, Veterans): the **footer of the last page** of each waybill has “INVOICES: n  CASES: m”. Recipe: `LINE_COUNT` with `INVOICES:\s*(\d+)` against the distinct invoices read, and `SUM` of the cases column with `CASES:?\s*(\d+)`. A **per-record** reader is needed (one record per waybill): the headers repeat on every page, so the record start would be the waybill number that changes.
- **Axis Med waybill:** the footer says “ROUTE BB  12 CASES  3  12” (cases, lines and cases); different format and letters deformed by the scanner: recipe `(\d+)\s+CASES\s+(\d+)`, to be verified with more samples before trusting it.
- **Billing summary** (the first page of each waybill file): the final “TOTAL” is the sum of the totals per division and each total is quantity × rate. It is checkable with `SUM`; what **cannot** be expressed yet is “the quantity of each manifest = the lines of its waybill” (it crosses two sections of the same file).
- **Depot invoice:** subtotal + tax = total; it has no integration.

**What the real tests taught:** the checks work where the document declares something (the Drive invoice flags the lines the OCR failed to read), but a waybill with a blank footer on the middle pages needs the check to be done only on the **last** page of each waybill; that is why those checks wait for their per-record reader.

## 23. Reader cards and the reading assistant (2026-10-24)

The email integration form no longer forces you to write expressions. The window is wider and each reader is a **card** with its own frame (Reader 1, Reader 2…), its name and two clear buttons: tapping the title folds or unfolds it, and **Remove reader N** deletes it.

**The four steps of each card**
1. **What document is it?** The name, where it comes from (body, PDF or data file) and the **conditions** as rows: *what is looked at* (the document text, the subject, the attachment name) + *how* (contains, equals…) + *which text*. The document must meet all of them. There is an **Add condition** button and a trash icon per row.
2. **What is done with it?** The destination (order, arrival notice, exit notice, order closing), the notice warehouse, the closing mode, and the reader's own client and template.
3. **What data is read?** Each datum is a card that says **how it is found** in the document (with a label, a single datum, or a table of rows) and **where it goes**. For notices the destination is chosen **by name** (“Product (SKU)”, “Quantity”, “Date”…), not by a column number.
4. **Checks.** The minimum OCR confidence and the completeness checks (section 22).

**Reading assistant (nobody writes expressions).** A button opens a window that works with a **sample document** (upload the PDF —real text or OCR— or paste its text):
- **Table of rows**: tap a row with data; the system splits it into numbered parts and, for each datum of the destination, you choose **which part it comes from** (and, if needed, **which part it is divided by**: quantity = amount ÷ unit price). The assistant explains the choice in words (“Quantity ← part 4 divided by part 3”), builds the expression and shows **“N rows of M match”** with examples. “Use this” saves it in the datum.
- **Single datum**: tap the row and then the value; what follows the same text as in the sample is read.
- **Total declared by the document** (under Checks): tap the row and the total number; the system works out how to find it.

**The “parts of the row” (formerly “Row columns: 2=2, 3=4/3, 4=4”).** It is the pair “template datum = part of the row”. “3=4/3” reads: datum 3 is part 4 divided by part 3. It is no longer typed: the card shows it as a list in words, and the raw text stays under “See or adjust the expression (advanced)” in case it needs touching up. A table datum still needs at least one assigned part (otherwise the reader is not saved: “A block needs at least one column…”).

**Who can:** `integrations.create` or `integrations.manage` (the assistant only reads the sample PDF, `POST /api/v1/integrations/read-sample`: it saves nothing). An empty PDF answers 400 and a damaged or protected one shows the reason inside the window.

## 24. Microsoft 365 mailboxes (2026-10-24)

Microsoft 365 and Outlook.com **do not accept IMAP with a user and password**, so for those mailboxes the form has the **mailbox type “Microsoft 365 (Entra ID application)”**. It does not use the mailbox password and it also works for **shared mailboxes** (which have no password and no license).

**What the Microsoft 365 administrator does (once):**
1. In Microsoft Entra ID, **register an application** (for example “Teikem mail”). Copy the *Application (client) ID* and the *Directory (tenant) ID*.
2. Under “API permissions”, add the Microsoft Graph **application** permission `Mail.ReadWrite` and grant **admin consent**.
3. Under “Certificates & secrets”, create a **client secret** and copy its **value** (not its ID; it is only shown at creation). It expires: note the date.
4. Recommended: restrict the application to that mailbox with an Exchange **application access policy**; without it the permission reaches every mailbox in the company.

**In Teikem** (Integrations → “New integration” / edit → mailbox section): “Mailbox type” = Microsoft 365; **Tenant ID**, **Client ID**, **Mailbox address** (the mailbox that is read, e.g. `avisos@yourdomain.com`) and **Application secret (Client secret)**, which is stored encrypted and never shown again. Server and port are not asked for. Everything else (folder, processed, filters, readers, inbox) works as with IMAP. “Test connection” says how many unread e-mails there are, or explains what is missing.

**How it reads:** like IMAP (section 25): by default it records what was handled in Teikem's log and **does not touch the mailbox**; it reads from the **oldest to the newest**, 50 per run, and the subject filter is applied before downloading the e-mail (those that do not match are left alone). Only if you choose “In the mailbox” does it mark them as read and move them to “Procesados” (the folder is created if missing). **It never deletes.**

**Messages “Test connection” or the run can give** (and what to do):
- “The application secret (Client secret) is not valid or has expired…” (AADSTS7000215): you pasted the ID instead of the value, or the secret expired. Create a new one.
- “The application does not exist in that tenant…” (AADSTS700016): wrong Client ID, or admin consent is missing.
- “The tenant was not found…” (AADSTS90002): wrong Tenant ID.
- “The application has no permission on this mailbox: the Mail.ReadWrite application permission with admin consent is missing, or an application access policy blocks it” (HTTP 403): step 2 is missing, or the policy of step 4 does not include this mailbox.
- “The mailbox was not found: check the address” (HTTP 404).
- “Folder “X” does not exist in the mailbox”: the “Folder that is read” name does not match (INBOX = the inbox).
- Form validations: “Enter the Microsoft 365 Tenant ID…”, “Enter the application Client ID…”, “Enter the application secret (Client secret).” (HTTP 400).

**Who can:** `integrations.create` / `integrations.manage`, like the rest. **Still to verify with a real tenant**: reading is tested against a fake server; the first real connection may show a message that is not listed here.

## 25. “Already read” in Teikem's log: the mailbox is not touched (2026-10-24)

**By default Teikem does NOT mark e-mails as read or move them in the mailbox.** What it has handled it records in its own **log** (table `IntegrationMailLog`: one row per e-mail and integration) and never reads it again. The mailbox stays exactly as it is and can be used as usual.

- **What it reads:** the e-mails in the folder (“Folder to read”), **read or not**, received since the date in **“Read e-mails received since”** (empty = all the folder's e-mails; with a big mailbox set a date). Always **50 per run**, oldest first; if more are left, the run summary says “E-mails are left to read; they are read in the next run.”
- **How it does not repeat:** each handled e-mail (whether it created something or went to the inbox) stays in the log and is skipped in later runs. The already processed content (file hash) is not duplicated either.
- **The other mode:** “How what was already read is recorded” = “In the mailbox” does what it did before: only UNREAD e-mails, which are marked as read and moved to “Procesados” (and it warns if they cannot be marked). Use it only if you really want the mailbox to reflect what was handled.
- **“Test connection”** says how many e-mails are left to handle (“N e-mail(s) to read… the ones Teikem has not handled yet; the mailbox is not touched”).
- **Database:** needs the new table in `Diseño/logistica-db-update.sql` (section 2026-10-24 (o)); run `db-update` before using this version.
- If an earlier version already marked or moved e-mails to “Procesados”, Teikem's log starts empty: it does not read them again because they are no longer in the folder that is read. To have them read again, move them back to the inbox (they do not need to be marked unread).
