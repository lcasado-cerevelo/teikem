# Frequently asked questions (FAQ)

Each entry quotes the exact message the user may see (the error's `title`) and explains the probable cause and
what to do. The messages are verified against the Batch 1 code (`src/Teikem.Infrastructure/Services`,
`src/Teikem.Infrastructure/Abstractions/Exceptions.cs`, `src/Teikem.Api/Auth/Policies.cs`).

## Batch 1 — Platform and security

### Access and session

**What does "Invalid credentials." mean?**
The e-mail does not exist (or the user is inactive), the account is temporarily locked because of failed
attempts, or the password is wrong. The message is the same in all three cases on purpose, so as not to reveal
which of the two pieces of data failed. Wait a few minutes if you suspect your account was locked (5 failed attempts
lock it for 15 minutes) and try again; if it keeps failing, ask an administrator to review your account.

**What does "Portal users authenticate in the client portal." mean?**
Your account is a portal account (end client), not for internal use. This access (`/api/v1/auth/login`) is only
for company staff; the client portal comes in a later module.

**What does "Does not belong to that company." mean?**
You asked to sign in to (or switch to) a `tenantId` where you have no active membership. Check which
company you have an account with, or ask to be added as a user there.

**I belong to several companies: why am I not asked which one to enter?**
Since 2026-09-30 the system goes straight into your default company (or the first one by name). Switch companies with the
selector in the header, between the clock and the theme; it only shows the companies you have access to.

**Why does it ask me to verify my e-mail, change my password and set up two-step verification when signing in?**
It is the mandatory first sign-in (since 2026-09-30, for everyone). The password was given by an administrator: you have to confirm that the
e-mail is yours, set a password only you know and turn on the second factor. It is done only once.

**What does "The code is not valid or has expired." (400) mean on the first sign-in?**
The 6-digit code from the e-mail is mistyped or more than a few minutes have passed. Tap "Send another code" and use the most
recent one. Several failed attempts lock the account for 15 minutes, like the password.

**What does "The email could not be sent. Try again in a few minutes." (409) mean?**
The e-mail provider (Brevo) did not accept the delivery or is not configured (`Brevo__ApiKey`, `Brevo__FromEmail`). If it persists,
notify support.

**What does "The new password must be different from the one you were given." (400) mean?**
You typed the same temporary password the administrator gave you. Choose a new one.

**What does "Verify your email first." (409) or "Complete the email verification and the password change first." (403) mean?**
The first sign-in steps go in order: e-mail → password → two-step verification. Sign in again and the screen
takes you to the missing step.

**What does "Complete your first sign-in on the web first (email, password and two-step verification)." (403) mean in the warehouse app?**
That user has not finished their first sign-in yet. Have them sign in to the web app once, complete the three steps and then use their PIN.

**I lost the phone with the authenticator app, how do I sign in?**
Sign in with one of your recovery codes instead of the 6-digit code and set up MFA on the new phone from My
account. If you also lost the codes, ask an administrator of your company to reset your MFA (System → Users);
you will receive a notice by e-mail and you set it up again when signing in.

**What does "Turn on two-step verification first." (409) mean?**
You requested new recovery codes without having MFA active. Turn it on first in My account.

**I received an e-mail saying my two-step verification was reset and I did not request it.**
Notify your administrator immediately and change your password: someone with administrator permission reset your second factor.

**What does "The user has no active company." mean?**
Your user exists but has no active membership in any company. Ask an administrator to
add you to their tenant or reactivate your membership.

**What does "The company is inactive." mean?**
The company you belong to was deactivated on the platform. Contact Teikem support.

**What does "Invalid MFA code." mean?**
The code from the authenticator app expired (it lasts 30 seconds, check the phone's time) or the recovery
code was already used or is mistyped. Try the current code from the app or another unused recovery
code.

**I lost my phone with the MFA app, what do I do?**
Use a single-use recovery code (they were shown to you when turning on MFA) at the verification step.
If you lost those too, contact an administrator of your company (`admin.users` permission); Batch 1 does not
have a self-service flow to reset MFA without those codes.

**What does "Invalid refresh token." mean?**
The token to renew your session does not exist, was already used (refresh tokens are single-use and are rotated on
every call) or was revoked (for example, someone closed that session). Sign in again.

**What does "Session expired." mean?**
Your session exceeded the days configured by your company (`SessionDays`). Sign in again.

**What does "The membership is no longer active." mean?**
When renewing your session, the system detected that you no longer have an active membership in that tenant (you were suspended or
removed). Contact your administrator.

**What does "This action requires recent reauthentication (AAL2)." mean?**
You are attempting a sensitive action (changing a role's permissions, turning a module on/off, creating a
company, etc.) and your session has no recent reauthentication. Call `POST /api/v1/auth/reauth` with your
password (and your MFA code if you have one) and retry the action with the new token it returns.

**What does "The current session is not revoked from the list; use logout." mean?**
You tried to close, from the session list, the same session you are connected with right now. Use
`POST /api/v1/auth/logout` instead of the session list.

**What does "Incorrect password." mean?**
When changing your password or reauthenticating, the current password you typed does not match. Check it.

**What does "This password appears in known breaches; choose another." mean?**
The new password matches one that was publicly leaked. Choose a different one. (In this batch this
check is not really active because the environment has no Internet access, but the message exists in
the code for when it is connected.)

**What does "TOTP is already enrolled and confirmed; disable it first (requires AAL2)." mean?**
You already have the second factor active; you cannot enroll again without disabling the current one first (and to
disable it you need recent AAL2).

**What does "Start the TOTP enrollment first." mean?**
You tried to confirm a code without having called enroll first (`POST /auth/mfa/totp/enroll`).

**What does "Invalid code." mean?**
The code you typed when confirming the TOTP does not match the one your app generates at that moment. Check the
device's time and try again.

### Permissions and modules

**What does "Missing permission '<code>'." mean?**
Your user (neither by role nor by extra permission) does not have the indicated permission. Ask an administrator of your
company (`admin.users`/`admin.roles`) to assign it to you, if it corresponds to your job. It also appears on the cross-cutting
routes (contacts, status history and custom field values) when the record belongs to an
entity with its own module: reading requires the read permission of that entity (`clients.read`, `locations.read`, `contracts.read`,
`portalusers.manage`) and writing custom fields requires the edit one (`clients.update`, `locations.update`,
`contracts.update`). Each rejection is recorded as PERMISSION_DENIED in the security log.

**I do not have permission for something I should be able to do.**
Check `GET /api/v1/me`: the `permissions` array lists your effective permissions (those of your role plus the ones you
were given extra). If the permission is missing, ask for it to be added by role (affects everyone with that role) or as
a one-off extra permission for your user.

**I do not see a module/screen.**
Two possible causes: (1) the module that screen depends on is turned off for your company —check it in
`GET /api/v1/modules` or in `enabledModules` of `/me`—; (2) you do not have the permission associated with that screen.

**What does "Module '<key>' is not enabled for this company." mean?**
You tried to use an endpoint of a module your company has turned off. Ask an administrator
(`admin.tenant`) to turn it on, if it corresponds to what your company needs.

**What does "'<module>' depends on '<dependency>', which must be turned on first." mean?**
That module needs another one turned on first (for example, Rental billing needs Rental equipment).
Turn on the dependency first.

**What does "'<module>' is a core module and cannot be turned off." mean?**
Core modules (last mile and system) are mandatory in every company; they cannot be turned off.

### Users and roles

**What does "Role '<name>' already exists."? / "Company '<name>' already exists."? / "Value '<code>' already exists in <entity>."? / "A view '<name>' already exists for <source>."? / "Indicator/chart '<name>' already exists."? / "Field '<key>' already exists in <entity>." mean?**
In all these cases you tried to create something with a name/code/key that already exists in your company (or, for
companies, on the platform) or in that context. Use another name or edit the existing one instead of creating a
new one.

**What does "The role has users assigned; reassign them before deleting it." mean?**
A role cannot be deleted while someone has it assigned. Change the role of those users first.

**What does "Unknown roles: <list>." / "Unknown permissions: <list>." mean?**
One or more of the names/codes you sent do not exist in the list. Check `GET /api/v1/roles` or
`GET /api/v1/permissions` to see the valid ones.

**What does "The user already belongs to this company." mean?**
You tried to create someone who already has a membership in your tenant. Look them up in `GET /api/v1/users` instead of
creating them again.

**What does "That email already belongs to a portal user; a portal user cannot be an internal user at the same time." mean?**
You tried to create as an internal user (`POST /api/v1/users`) an e-mail that is already a portal user of a client (of your
company or another). An e-mail is either internal or portal, never both: use another e-mail for the internal person. That portal
account cannot be edited from `/users` either (it answers 404): it is managed from its client's record.

**What does "You cannot deactivate yourself."? / "You cannot change your own membership." mean?**
For security, you cannot remove your own access from these screens. Ask another administrator to do
it if it is really necessary.

**What does "Portal users are managed from the client's record (Batch 2)." mean?**
You are trying to create a portal-type user from the internal users screen. That management comes with
the clients module (batch 2); that screen does not exist yet.

### Lists and lists

**What does "List domain '<key>' not found." / "<entity> value '<code>' not found." mean?**
The domain or code you requested does not exist. Check `GET /api/v1/catalogs/domains` or
`GET /api/v1/catalogs/{entity}` to see the valid ones.

**What does "A system value is not deactivated globally; use the tenant override (IsEnabled=false)." mean?**
Values that come from the factory (system values) are not turned off for the whole platform from your company; use the
override (`PUT .../override` with `isEnabled:false`) to turn it off only in your tenant.

**What does "System lists are not deleted."? / "The list is in use by a custom field; deactivate it instead of deleting it." mean?**
You cannot delete lists that come from the factory, nor an own list that a custom field is using.
In the latter case, deactivate the list instead of deleting it.

**What does "Global lists are only edited by the platform administrator; use overrides or create your own list." mean?**
You cannot directly edit a list shared by the whole platform. Use an override (changes only your
tenant) or create your own list.

**What does "The country code must be ISO 3166-1 alpha-2 (2 letters)." mean? (HTTP 400)**
Countries are identified by their two-letter ISO code (`PR`, `US`, `DO`…). You will see it when adding a value to the
global `Country` list (`POST /api/v1/catalogs/Country`, `code` field), when creating or editing a location (`country`), when capturing
an order with a new consignee (`newConsignee.country`) or in a row of the order importer (`country`). Use the two-letter
code; the order stops store the country in that format.

### Status

**What does "Invalid pipeline: ..." mean?**
The change you attempted (for example, disabling a stage) would leave the pipeline without a valid initial
stage. The system rejected the change before saving it; check which stages you have enabled.

**What does "The current status does not allow the action '<action>'." mean?**
Your company configured that, in the status that record is in, that action cannot be done. Check the
capabilities matrix (`GET /api/v1/status/capabilities/{entityType}`).

**What does "'<code>' is a pipeline stage, not a lateral/terminal." mean?**
You tried to define a lateral entry towards a stage that is actually part of the normal flow (pipeline),
not a lateral or terminal exit (like cancelled).

**What does "Pipeline '<domain>' has no enabled initial stage." mean?**
Your company disabled all the possible initial stages; no new record can be created
until at least one is enabled.

**What does "A new record must start in an initial stage; '<code>' is not one." mean?**
An attempt was made to create a record directly in a stage that is not a starting one.

**What does "The record is already in that status." mean?**
An attempt was made to "change" a record to the same status it is already in.

**What does "'<code>' is terminal: it does not allow further transitions." mean?**
That record reached a final status (for example, closed or cancelled) and cannot change
status again.

**What does "Illegal jump: from '<source>' you can only advance to '<next>'."? / "Moving to '<target>' from '<source>' is not allowed according to this company's model."? / "From the lateral '<source>' you can only go back to '<...>' or advance to '<...>'." mean?**
The status change that was attempted does not respect the pipeline order or the lateral rules configured
for your company. Check the pipeline and the configured lateral entries.

### Contacts

**What does "There is no ownership resolver for '<entity>' (the owner module is not built yet)." mean?**
You tried to add a contact to an entity type that does not exist on the platform yet (it comes in a later
batch). It is not your error; wait for that module to be available.

**What does "Invalid email."? / "Invalid phone." mean?**
The value does not have the expected format for that contact type. Check that the e-mail has `@` and a domain, and
that the phone uses only digits, spaces, hyphens or parentheses.

### Custom fields

**What does "Invalid key: lowercase letters, digits and '_' (2-60), starting with a letter." mean?**
The field's technical key (`fieldKey`) does not meet the format. Use only lowercase letters, numbers and underscore,
starting with a letter, between 2 and 60 characters (e.g. `cost_center`).

**What does "The type of a field that already has saved values cannot be changed." mean?**
There are already records with data in that field; changing its type (for example from text to number) would break that data.
Create a new field if you need another type.

**What does "LOOKUP_REF requires the referenced list domain."? / "A selection field needs options or a value list (refEntity)." mean?**
When creating a list/selection type field, it is missing where the options come from: either you define `options`
directly, or you point to an existing list with `refEntity`.

**What does "A number was expected." / "Yes/no was expected." / "A date (yyyy-MM-dd) was expected." / "An ISO date and time was expected." / "A list of values was expected." mean?**
The value you sent for that field does not have the data type the field expects. Check the `dataType` of the
definition (`GET /api/v1/custom-fields/definitions/{id}`) and the exact format.

### Analytics (views, indicators, charts)

**What does "Invalid range mode."? / "The custom range needs From and To."? / "From cannot be later than To." mean?**
The date range you requested is not valid: the mode does not exist, or you requested a custom range without both
dates, or the start date is after the end date.

**What does "A view needs at least one column or one grouping." mean?**
A completely empty view cannot be saved; add at least one column or one grouping.

**What does "Source <source> does not combine with '<secondary>'."? / "Source <source> does not have the field '<field>'." mean?**
You asked to combine with a related source, or to use a field, that this source does not have defined. Check
`GET /api/v1/analytics/data-sources` to see the valid relations and fields of each source.

**What does "The platform's default elements are not edited or deleted." mean?**
That view/indicator/chart comes from the factory (`IsSystem`); it cannot be modified or deleted. Create your own
if you need a variant.

**What does "Only the owner can edit or delete this element." mean?**
You are not the owner of that view/indicator/chart and you do not have a shared edit permission on it.

**What does "Changing the default range of someone else's or a system indicator/chart requires 'analytics.dates'." mean?**
You can freely change the date range of what is yours; to change the default range of something that belongs to someone else
or to the system (affects everyone who sees it) you need the `analytics.dates` permission. If you only want to change
your own view of that indicator/chart, use `my-date-range` instead of `default-date-range`.

**What does "The grouping field is required."? / "Chart type: BAR, DONUT or LINE." mean?**
When creating a chart, the field it is grouped by was missing, or the indicated type is not one of the three supported.

### Audit

**Is an attempt without permission recorded?**
Yes. Every time someone lacks a permission for a protected action, a `PERMISSION_DENIED` event is recorded
in security, visible (with `admin.audit`) in `GET /api/v1/audit/security-events` or
`GET /api/v1/audit/activity`.

**How do I trace what produced a change?**
Each row of `GET /api/v1/audit/changes` (or of "activity") carries a `correlationId`, the same value as the
`X-Correlation-Id` header of the request that originated the change.

### Company settings

**What does "2-letter language code."? / "Invalid mask (1-127)."? / "Must be ≥ 1."? / "The reauthentication window must be 15, 30 or 60 minutes."? / "Between 1 and 365 days." mean?**
The value you sent for that setting is outside the allowed range. Check the limit indicated in the message.

**What does "BrandingJson is not valid JSON."? / "BrandingJson too large (logos go to blob storage)." mean?**
The branding block you sent is not valid JSON, or weighs more than 200,000 characters. Do not upload
base64 images inside this field; logos go to separate file storage.

#### Region and formats (2026-10)

Messages verified against `src/Teikem.Domain/Tenancy/TenantFormatRules.cs` (all are 400 with the field in `errors`; if there is
an error nothing from the request is saved). Detail in chapter 01, section 11.1.

**What does "The thousands and decimal separators cannot be the same" (400) mean?**
The thousands and decimal separators would end up the same (for example, period and period) and numbers would be ambiguous. The
final result is compared: if you only change one, it clashes with the other saved value. For 1.234,50 send both in the same request:
`{ "thousandsSeparator": ".", "decimalSeparator": "," }`. On the screen the other one is swapped by itself.

**What does "The time zone '<value>' is not recognized by the platform. Use an IANA name, for example America/Puerto_Rico or America/New_York." (400) mean?**
The zone does not exist on the server or is misspelled (case matters: `America/Puerto_Rico`, not `america/puerto_rico`).
Use the IANA name of the zone (Continent/City). The Windows name (`SA Western Standard Time`) is also accepted and is
saved with its IANA name.

**What does "Enter the company's time zone, for example America/Puerto_Rico." (400) mean?**
You sent an empty zone. The zone is mandatory: if you do not want to change it, do not send the field.

**What does "Unknown region: '<value>'. Use PR (Puerto Rico) or US (United States)." (400) mean?**
There are only two regions, Puerto Rico and United States. For another country choose the closest one and change the fields that are needed
(zone, currency, date order, separators, phone).

**What does "The currency code must be 3 uppercase letters (ISO 4217), for example USD." (400) mean?**
The currency is indicated by its 3-letter ISO code (USD, EUR, DOP). Lowercase letters are accepted and saved in uppercase; numbers
or symbols are not.

**What does "The currency symbol is required and 1 to 3 characters long, for example $." (400) mean?**
The symbol came empty or with more than 3 characters. Valid examples: `$`, `US$`, `€`.

**What do "The currency symbol position must be B (before the amount) or A (after).", "The currency decimals must be 0, 2 or 3.", "The date order must be MDY (month/day/year), DMY (day/month/year) or YMD (year/month/day).", "The date separator must be '/', '-' or '.'.", "The time format must be 12 or 24." and "The first day of the week must be 0 (Sunday) or 1 (Monday)." (400) mean?**
The value is not one of those allowed for that field. The full list is given by `GET /api/v1/tenant/format-options` and the table in
chapter 01, section 11.1.

**What do "The thousands separator must be comma (','), period ('.') or space (' ')." and "The decimal separator must be period ('.') or comma (',')." (400) mean?**
The separator is not one of those allowed (for example, an apostrophe or an empty text). The space does count as a thousands
separator (1 234,50), not as a decimal one.

**What does "The phone country code must be '+' followed by 1 to 4 digits, for example +1." (400) mean?**
The country code carries the `+` sign and 1 to 4 digits: `+1` (Puerto Rico and the US) or `+34` (Spain) are valid; `1` (without
the `+`) or `+12345` are not.

**What does "The phone mask must have 1 to 30 characters without accents and at least one '#' (each # is a digit), for example (###) ###-####." (400) mean?**
The mask says how to display a phone stored only as digits: each `#` is a digit and the rest is copied as is. It needs
at least one `#`, no more than 30 characters and no accented letters or other special characters.

**I changed the region to United States: why did the time zone and the other formats change too?**
Changing the region without sending other fields loads the full set of that region (the zone becomes `America/New_York`). If you want
the region but with some different value, send that field in the same request (it overrides the region's value) or change it
afterwards separately; the company will be marked as *Custom*.

**I sent `{ "regionCode": "PR" }` and the Puerto Rico values were not restored. Why?**
If the company is already Puerto Rico it is not a region change, so nothing is touched. To restore, send the set of the
region that `GET /api/v1/tenant/format-options` returns (that is what the *Restore region values* button does).

**I changed the time zone: from when does "today" count with the new zone? Do the saved dates change?**
From the next request (with several server instances, within 10 minutes at the latest). The saved dates do not change
(they are in UTC); only which local day each one falls on changes. Since Batch 20 all screens (orders, routes, fleet,
maintenance, rates, purchases) count "today" in the company's zone.

**At 8 p.m. (or later) the screen tells me the date is "tomorrow" / does not let me use today's date. What is going on?**
It was the behavior before Batch 20: the server counted "today" in UTC and in Puerto Rico, between 8:00 p.m. and
midnight, it was already "tomorrow" for it (examples: the route of the day, the monitor, the rate "not earlier than today", the document that
"expires today"). Now "today" is the company's day and changes at local midnight. If you still see it, check the zone in Tenant
settings → Region & formats: if it is a zone east of UTC (e.g. Europe), your "today" moves ahead of local midnight.

**A date rule of a custom field (`min`/`max`) accepts or rejects differently near midnight. Why?**
`min` and `max` of a date field are days relative to today (`-1` = yesterday, `0` = today) and "today" is the day of the company's
zone, not the UTC day. A *date* type field compares the calendar day; a *date and time* one compares the instant with
today's local midnight. The messages do not change: `Fecha anterior a la mínima permitida.` and `Fecha posterior a la máxima
permitida.` (400 with the field).

**Does the user's language change the formats?**
No. The language (of each user) only decides in which language the names of days and months appear; the date order, the
separators, the time and the currency are decided by the company's region.

### Generic

**What does "<Resource> '<id>' not found." mean?**
The record you requested does not exist, was already deleted/deactivated, or does not belong to your company (the tenant
filter makes other companies "not exist" for you).

**What does "Invalid data." with a list of `errors` mean?**
You sent several fields with problems at once; check the `errors` list in the response body, each
key is the field and the value is the specific message for that field.

**What does "Not authenticated." mean?**
Your request did not carry a valid token, or the token is no longer valid (session closed, password changed,
`logout-all`). Sign in again.

**What does "You do not have permission for this action." mean?**
It is the generic forbidden access message when no more specific permission message applies.

**What does an error with `"code": "internal"` and `status: 500` mean?**
An unhandled error on the server. Save the `correlationId` of the response and share it with support
so they can look it up in the server logs.

## Batch 2 — Clients and contracts

Messages verified against `src/Teikem.Infrastructure/Services/{Client,Location,Contract,Rate,SpecialService,PortalUser}Service.cs`,
`src/Teikem.Infrastructure/Clients/*` and `src/Teikem.Domain/Clients/*`. The full chapter is in
[02-clientes-y-contratos.md](02-clientes-y-contratos.md).

### Clients

**What does "A client with that code already exists." mean?**
You sent an explicit `code` that another client of your company already uses (or two simultaneous creations clashed). Leave the code
empty so it is generated from the name (with suffix `-2`, `-3`… if needed) or choose another.

**Why is the generated code not the same as the name, or ends in `-2`?**
The code is formed from the name in uppercase, without accents, with `-` instead of spaces and symbols, and is trimmed to 20
characters. If an identical one already existed, `-2`, `-3`… is added, trimming the base if it does not fit (`FARMACIA-LAS-MARIA-2`).

**What does "The client name contains no letters or digits to generate the code." mean?**
The name only has symbols or spaces. Write a name with letters or numbers, or send an explicit `code`.

**Can I change a client's name?**
No. The name is the client's identity and there is no way to edit it (`PATCH .../profile` does not accept it). If there is a
capture error, deactivate the client (`POST .../deactivate`) and create a new one; the legal name (`legalName`) can be edited.

**What does "The record was modified by another user; reload and try again." mean?**
You sent `rowVersion` in the PATCH and another user saved before you. Open the record again and repeat the change.

**What does "The pickup point must be an active address of the client: the physical one, the postal one or a pickup point." mean?**
The address you chose as “Picked up at” is not the client's, is inactive or is not one of their addresses (for example it is a
DELIVERY consignee). Choose in the **Addresses** panel the physical address, the postal one or one of the pickup points.

**What does "The pattern must include at least one '#' for the sequence." / "Character not allowed in the pattern: 'x'…" mean?**
The numbering pattern needs at least one `#` (each `#` is a digit) and only allows letters, digits and `- _ / . # @`.
Valid example: `AX-#####` → `AX-00001`. Leave the field empty to go back to the default pattern (`ORD-#####`, `FAC-#####`, `PQT-#####`).

**Why does the preview show `FAC-00001` if the mock said `FAC-0001`?**
The system's default patterns have five digits, uniform with the order one (Batch 2 decision). If your client
wants four, set `FAC-####` in their numbering.

**What does "There is already an active primary contact for this client." mean?**
Two simultaneous creations or edits tried to leave two primary contacts. It normally does not happen: when marking one as
primary the previous one stops being primary by itself. Reload and repeat.

**Why did my contact stop being the primary one when I deactivated it?**
An inactive contact is never the primary one. Mark another active contact as primary if you need it.

**What does "Contact '<id>' not found." mean?**
That contact does not exist or does not belong to the client indicated in the URL (contacts are always reached through their client).

**What does "Unknown service type: 'X'." mean?**
In the service levels (SLA) of the creation or the contract you used a code that is not in the `ServiceType` list. Check
`GET /api/v1/catalogs/ServiceType`.

**I deactivated a client and it does not appear in the list, was it deleted?**
No. It is a soft deactivation: it appears with `GET /api/v1/clients?includeInactive=true`, its record is still available and `POST .../reactivate`
brings it back to the list.

### Consignees and locations

**What does "A corporate or postal (billing) address must belong to a client; it cannot be shared." mean?**
You tried to create (or make shared) a CORPORATE or BILLING type location without a client. Those two are always
addresses of a client; indicate `clientPublicId`.

**What does "The client already has an active corporate address; deactivate or edit it." (or "…active postal (billing) address…") mean?**
There can only be one active physical address and one active postal address per client. Edit the existing one, or deactivate it before creating
another. Reactivating a deactivated one when there is already another active one gives the same message.

**What does "Indicate a client or mark the location as shared, not both." mean?**
In the PATCH you sent `makeShared: true` and `clientPublicId` at the same time. Send only one.

**What does "The time window needs both a start and an end time." / "The window end time must be after the start time." mean?**
The delivery window is saved complete or empty (`clearWindow: true` clears it), and the end must be after the start
(format `HH:mm:ss`).

**What does "Service minutes cannot be negative." mean?**
`defaultServiceMinutes` must be 0 or greater.

**Why did the client's pickup point go back to "same as the corporate one"?**
The warehouse that was its default pickup was deactivated, changed owner or changed to a type that is not PICKUP/BOTH. Choose
a warehouse again in the client's profile.

**What does "The location was modified by another user; reload and try again." mean?**
Same as with clients: outdated `rowVersion`. Reload the record.

**What does "Location '<guid>' not found." mean?**
It does not exist or belongs to another company.

### Contracts

**What does "The end date cannot be earlier than the start date." mean?**
`endDate` is earlier than `startDate` (or you changed "client since" to a date later than the end date already saved). Fix
one of the two; `clearEndDate: true` clears the end date.

**What does "Enter an end date or mark it to be cleared, not both." mean?**
You sent `endDate` and `clearEndDate: true` together. Send only one.

**What does "A contract with number 'X' already exists." mean?**
You sent a `contractNumber` that already exists in your company. Leave it empty so it is numbered automatically (`{CODE}-C2`, `-C3`…).

**What does "The current status does not allow the action 'EDIT_CONTRACT'." mean?**
The contract is in a status where it cannot be edited (by default EXPIRED and CANCELLED): neither general data, nor billing
model, nor rates, nor special services. If your company needs to edit cancelled contracts, an administrator can
change that in `PUT /api/v1/status/capabilities/CONTRACT`. The record anticipates it with `canEdit: false`.

**What does "The contract cannot be activated: the client is suspended. Reactivate the client first." mean?**
The client is SUSPENDED. Return it to ACTIVE (`POST /api/v1/clients/{publicId}/status {"toCode":"ACTIVE"}`) and activate the contract again.

**What does "The client already has a current contract. Cancel or expire the previous contract before activating this one." mean?**
There can only be one ACTIVE contract per client. Move the previous one to EXPIRED or CANCELLED and then activate the new one.

**My contract has a past end date and still shows as current, is that a bug?**
No. The end date is informational: there is no automatic expiration. If the contract really ended, move it to EXPIRED (or
CANCELLED) manually; if it was not renewed, work continues with what is there.

**I unchecked "Dispatch" (or COD, Special, Extra piece) and the amount is still in the record, wasn't it saved?**
It was saved: unchecking a component does not delete what was configured, it only stops being charged. When you check it again, it reappears.

**What does "The COD fee percentage must be between 0 and 100." / "The fixed COD fee cannot be negative." mean?**
With `type: PERCENT` the value is a percentage of the COD amount collected (0..100); with `type: FIXED` it is an amount per order (≥ 0).
If you do not want to charge COD, turn off the `billCodFee` checkbox in the billing model.

**What does "The service type 'STANDARD' is repeated: there can only be one service level per type." mean?**
The SLA list contains the same type twice (case-insensitive). Keep one per type; `PUT` replaces the whole list.

**What does "There can only be one active service level per service type." mean?**
Two simultaneous writes collided on the unique index. Reload and repeat the `PUT`.

### Rates and quotes

**What does "The 'Per service' component is off in the contract's billing model; turn it on before working on its rates." (or "…'Extra piece'…tiers") mean?**
The corresponding checkbox of the contract is unchecked. Turn it on in `PATCH /api/v1/contracts/{publicId}/billing-model`
and try again. The rows that already existed were not lost.

**What does "There is already a rate in force on that date for that service and package type in the contract; edit it or close it before creating another." mean?**
For that service+package pair there is already a row that will be in force on the start date you sent (open, or closed
with a later date). There cannot be two rates in force on the same day: to change the amount use `PATCH` (it closes and opens
a new one); to stop charging it use `.../close`.

**Why does editing the rate return a different `id`?**
Editing never overwrites the amount: it closes the row in force on the new date and opens another one. The full history is seen with
`?includeHistory=true`, and the rate in force on a past date with `?asOf=yyyy-MM-dd`.

**What does "The rate is already closed; create a new one instead of editing it." / "The component is already closed." / "The tier is already closed…" mean?**
The row you are trying to edit or close already has a closing date. Create a new row (the same combination is allowed once the previous one is closed).

**What does "The new effective date (…) cannot be earlier than the start of the current row (…)." / "The date (…) cannot be earlier than the row's effective start (…)." mean?**
The date you sent in `effectiveFrom`/`effectiveTo` is earlier than the start of the row. Use an equal or later date (the same day is allowed and leaves a zero-length row as history).

**What does "Invalid range: 'from' must be at least 2 (piece 1 goes in the per-service rate) and 'to' must be greater than or equal to 'from' or left empty (open)." mean?**
Extra piece tiers start at piece 2 (piece 1 is paid by the per-service rate) and `toUnit` cannot be less than
`fromUnit`; leave it empty for "6+".

**What does "Tier 4–7 overlaps the tier in force 2–5." mean?**
The tiers of the same component cannot cross (neither when creating nor when editing From/To). Adjust the range or close the tier that is in the way.

**What does "Only per-service rates are edited here; extra piece tiers are edited in /tiers." / "Tiers only apply to extra piece components." mean?**
You used the wrong route: `PATCH .../rate-components/{id}` is for per-service rates; tiers go in `.../rate-components/{id}/tiers`.

**What does "The extra piece component is closed; create a new one to add tiers." mean?**
The component already has a closing date. Create another EXTRA_PIECE component for that service and package.

**What does "The service/package type pair is repeated; send a single line per (service, package type) with the total pieces." mean?**
In the quote you sent two lines with the same service and package type. Group them into one by adding up the pieces: that way
the first piece pays the base rate and the rest pay the extra piece.

**What does "The number of pieces must be at least 1." / "Enter at least one line (service, package type, pieces)." mean?**
Each line needs at least one piece and the quote at least one line.

**The quote says `baseSource: "NONE"` or `extraSource: "NONE"`, what does that mean?**
There is no rate for that service and package either in the contract or among the company's generic rates (or the component
is off and there is no generic one). The base comes out `null` and the extra piece 0. Set up the rate in the contract.

**What does "The date cannot be earlier than today: the rate history is not rewritten." mean?**
You tried to open a new version or close a rate, a tier or a special service with a past date. The rate history
is what supports what has already been quoted and invoiced, so it cannot be changed backwards. Use today (or leave the date
empty) or a future date. If you need to load historical rates when setting up a contract, do it when creating the rate,
which does accept a past `effectiveFrom`. The same rule and the same message apply, in Batch 4, to the three driver
rates (section "Drivers and rates" below).

### Special services

**What does "The client has no current contract; create or activate a contract before setting up special services." mean?**
The client has no ACTIVE or DRAFT contract (only EXPIRED/CANCELLED, or none). Create a new contract.

**What does "The 'Special services' component is off in the current contract; turn it on in the billing model to add special services." mean?**
The "Special" checkbox of the current contract is unchecked. The existing rows are still shown (`componentEnabled: false`), but they cannot be created or edited until you turn it on.

**What does "Enter the special service type: an existing type (typeId) or the name of a new one (newTypeName), not both." mean?**
Send only `typeId` (a type from the list `GET /api/v1/special-service-types`) or only `newTypeName`.

**I typed "VAGON DEL MUELLE" and it says "The client already has a rate in force on that date for the type 'Vagón de la plataforma'…"**
Names are compared ignoring case, accents and double spaces: it is the same type, and the client already has a rate
in force of that type. Edit it (`PATCH`) or close it.

**What does "The special service type is inactive; reactivate it or choose another." mean?**
The `typeId`/`specialServiceTypeId` points to a deactivated type (`POST /api/v1/special-service-types/{id}/deactivate`).
Reactivate it with `.../reactivate`, choose another type, or create the service with `newTypeName` (if it matches the inactive type,
it is reactivated automatically). The same message appears in Batch 4 when adding a driver's trip rate with an inactive type.

**What does "The type has rates in force for N client(s); close them before deactivating it." mean?**
A special service type cannot be deactivated while any client has an open rate of that type. Close those
rates (`.../special-services/{id}/close`) and try again; the history is kept.

**What does "The type has trip rates in force for N driver(s); close them before deactivating it." mean?**
It is the same lock as the previous one, added in Batch 4: besides clients, that special service type is also used
as a "trip type" in the trip rates of one or more drivers (chapter 04, section 8.3). Close those rates
(`POST /api/v1/drivers/{publicId}/trip-rates/{id}/close`) before deactivating the type.

**What does "The type is already inactive." / "The type is already active." mean?**
You asked to deactivate a type that was already inactive, or to reactivate one that was already active. There is nothing to do.

**What does "The rate is already closed; add a new special service if you need to charge it again." mean?**
The row already has a closing date. Create another one of the same type.

### Portal users

**What does "Invalid or expired invitation." mean?**
It is the only response of `accept-invite` when something fails, on purpose (it does not reveal whether the e-mail exists: an e-mail that does not
exist or that belongs to an internal user receives exactly the same response). Possible causes:
the e-mail does not match the invitation, the link expired (`expiresAtUtc` of the invitation, 48 hours by default) or was already used, the company **resent** the
invitation and this is the old link (only the latest one is valid), the password has fewer than 12 characters (the check against
known breaches exists in the code but today is a no-op, as in the password change of Batch 1), the user is no longer in
INVITED (accepted, was suspended or deactivated), or the company's portal module is off. Ask the company to
resend the invitation (`.../resend-invite`) and use the most recent link.

**What does "That e-mail is not available for this company's portal." mean?**
That e-mail is already registered on the platform: as a portal user of your company (in any client and in any
status except deactivated), as a portal user of another company, or as an internal user. The message is the same in all
cases on purpose (it does not reveal where the e-mail is). If it is a portal user of yours, look for it in the corresponding client;
if it was deactivated (DISABLED) in **that same client**, the invitation does not return 409: it reinvites them. If it is deactivated in another
client, reinvite them from that client (moving a person between clients is a business decision). In any other case use
another e-mail. Each of these 409s leaves a ROLE_CHANGE/FAILURE `portal_invite_conflict` `SecurityEvent` in the security log.

**What does "The invitation can only be resent to a user who has not yet accepted it (status INVITED)." mean?**
The user already accepted (or was suspended/deactivated). There is nothing to resend; if they forgot their password, that flow comes with the portal module.

**What does "A portal user can only be suspended in status ACTIVE." / "A portal user can only be reactivated in status SUSPENDED." mean?**
Suspending is only from ACTIVE and reactivating only from SUSPENDED. An INVITED user is not suspended (deactivate them with `.../remove` if they
should not get in); a DISABLED user is not reactivated: they are invited again with the same e-mail (`.../invite`).

**What does "The portal user is already deactivated." mean?**
It is already DISABLED (terminal). There are no more transitions from there; if the person must come back, invite them again with the same
e-mail from the same client (`.../invite`): they are reborn in INVITED with their history and set a new password when accepting.

**What does "Portal users sign in on the client portal." mean when signing in?**
The account is a portal account and the internal application does not accept it. The portal login comes in its own module.

**What does "The module 'CLIENT_PORTAL' is not enabled for this company." mean?**
The company has the client portal turned off. An administrator can turn it on in `PUT /api/v1/modules/CLIENT_PORTAL` (requires recent re-authentication).

**Why don't I see the portal user in `/api/v1/users`?**
Portal users are not company users: they are managed from the client's file (`.../portal-users`) with the `portalusers.manage` permission.

**What does "List <Domain> 'X' not found." mean?**
The list code you sent (`paymentTerm`, `currency`, `locationType`, `country`, `role`, COD `type`…) does not exist.
Check `GET /api/v1/catalogs/<Domain>`.

**I invited a portal user and now they cannot accept the invitation, even though the link is recent.**
Besides an expired or already used token, acceptance fails if the company is deactivated by the platform administrator
or if the Client portal module is off. The response is always the same (`Invalid or expired invitation.`) so as not to
reveal the cause; check the status of the company and of the module and resend the invitation.

## Batch 3 — Transport orders

**What does "The shipment number generated with the client's pattern exceeds 40 characters; shorten the pattern in the client record." mean? (HTTP 409)**
It also appears as "invoice number" or "package number". Teikem generates those numbers with the pattern in the client
record (`PATCH /api/v1/clients/{id}/number-settings`) and **never truncates** the sequence number: if the sequence number has more digits
than the `#` in the pattern, it prepends them. With a very long pattern, the resulting number no longer fits in the 40 characters of the field.
The order is not created (neither in the entry, nor in the PATCH that adds packages, nor in the importer row) and no
sequence number is consumed. Shorten the pattern (or leave it blank to use the default pattern) and try again.

**I deleted an order in entry and now marking it as picked up, cancelling, repricing or changing its status responds "Order not found." (HTTP 404). Why?**
Deleting an order (only in its initial status) is a soft delete: the order is still visible in its record and in the list with
`includeInactive=true`, but it no longer allows any action (edit, delete, mark as picked up, reprice, cancel or status). If you
need it, enter it again: its shipment and packaging numbers were freed.

**I changed the consignee of an order and it did not warn me about a duplicate invoice. Is that correct?**
Yes, if the invoice number of that order was generated by Teikem. The duplicate invoice check per consignee only applies to
invoices **typed** when creating the order; the order remembers whether its invoice was typed, so the result does not change even if
who assigns the invoices is later modified in the client record.

**What does "The comment allows at most 500 characters." mean? (HTTP 400, field `comment`)**
Every status change saves its comment in the status log, which allows up to 500 characters. It applies to
`POST /api/v1/orders/{id}/cancel`, `POST /api/v1/orders/{id}/status` and to the status changes of clients, contracts and
portal users. The status does not change; shorten the comment and try again.

**I have the `contacts.manage` permission but adding, editing or deactivating a contact of an order responds "Missing permission 'orders.edit'." (HTTP 403). Why?**
Contacts belong to an owner record and writing them requires, besides `contacts.manage`, the edit permission of the
owner module: `orders.edit` for an order, `clients.update` for a client or a client contact, `locations.update`
for a consignee, `contracts.update` for a contract. The permission is checked before looking up the record, so the
response is the same whether or not the order exists. Reading the contacts requires the owner's read permission (`orders.view`).
The phone numbers that the importer saves in the order when confirming a batch do not require `orders.edit` (they are created by the import,
which already requires `orders.create`). In Batch 4, the same rule applies to `fleet.manage` (VEHICLE/DRIVER/DISPATCH_ZONE contacts)
and `fleet.maintenance` (MAINTENANCE_SCHEDULE/WORK_ORDER/FUEL_LOG contacts).

**I save custom fields of a stop, of an order's COD, of a batch or of an import template and it responds "ORDER_STOP '…' not found." (HTTP 404). Why?**
The record does not exist in your company. For `ORDER_STOP` the id is that of a stop of an order; for `ORDER_COD`, the id of
an order **with COD**; for `IMPORT_BATCH` and `IMPORT_TEMPLATE`, the id of the batch or of the template. Writing them requires `orders.edit`.

**What does "Maximum 150 characters." mean (HTTP 400, field `email`) when inviting a portal user?**
The portal user's e-mail allows up to 150 characters. No account was created; use a shorter e-mail. In
`accept-invite` such an e-mail receives the neutral response "Invalid or expired invitation.".

**I am in Billing: how do I authorize an order that exceeds the credit limit? Why "Missing permission 'orders.edit'." (HTTP 403)?**
From the order record (Submitted), `GET /api/v1/orders/{id}/quote` shows the warning (`credit.exceeds=true`) and
`POST /api/v1/orders/{id}/confirm` with `{"overrideCredit": true}` marks it as picked up (Batch 106: it used to be called “confirm”). With the Billing template (`orders.view`
+ `orders.credit_override`, without `orders.edit`) that only works when the credit is **really** exceeded: confirming without
`overrideCredit`, or an order that fits within the credit, still requires `orders.edit` (403 "Missing permission 'orders.edit'.").
Without `orders.credit_override`, `overrideCredit: true` responds 403 "Missing permission 'orders.credit_override'.". The
authorization is kept in the status history ("Credit excess authorized by …") and in the security log
(ROLE_CHANGE `credit_override`). Creating an order with `overrideCredit` and the import still require `orders.create` (since Batch 106 the excess is measured when creating).

**The "COD to collect" indicator went down when cancelling an order. Is that correct?**
Yes. It adds up the PENDING COD of the active orders that are **not** cancelled: a cancelled order is never delivered, so
its COD is not to be collected. If your company customized the indicator's filter, it is not touched; the original filter is corrected automatically.

**How do I build the "Orders by package type" chart?**
The Orders source (`TRANSPORT_ORDER`) has the fields `PackageType` (label) and `PackageTypeCode` (code) with the

**main package type** of each order: the type with the most pieces among its lines (if tied, the one of the first line).
A mixed order counts only once, under its main type; a special delivery has no type. Create a donut chart
grouped by `PackageType`.

**When validating an import: "The file exceeds the limit of 5,000 rows or 2 MB." (HTTP 400, field `content` or `file`)**
The file has more than 5,000 data rows (not counting the header or the empty rows) or weighs more than 2 MB. Split it into
several files. The field is `content` when the CSV travels in JSON (`/import/validate`) and `file` when it is uploaded as a multipart file (`/import/validate-file`).

**An import row to a consignee that does not allow duplicate invoices comes out with an error in `clientInvoiceNumber` and I cannot select it (HTTP 400 in `rows[n]`).**
The consignee does not allow repeating the invoice number and another order with that number already exists: the row cannot be
created. If the consignee does allow duplicates, the row comes out with a warning and is only created if you confirm the batch with
`confirmDuplicateInvoice: true` ("Create anyway"); without it, the row is left failed with the duplicate invoice message
and the rest of the batch continues.

**What does "The client has been deactivated; only its history can be viewed." mean? (HTTP 409)**
The client was deactivated (`POST /clients/{id}/deactivate`). Its record, history, orders, contracts and rates can
still be viewed, but nothing new can be created for it: orders (entry or import), contracts, rate components or
tiers, special services, nor inviting/resending a portal user. Cancelling or closing something that already existed
is still allowed. Reactivate the client (`POST /clients/{id}/reactivate`) to create again.

**When creating or changing an order to a consignee, "The consignee 'X' does not allow duplicate invoices: order N already uses number F." or the version "…confirm whether you want to create the order anyway." (HTTP 409)**
You typed a client invoice number (`clientInvoiceNumber`) that this same consignee already uses in another active
order. If the consignee has `allowDupInvoice: false`, it cannot be created with that number (use another or correct the
invoice). If it has `allowDupInvoice: true`, repeat the request adding `confirmDuplicateInvoice: true` ("Create
anyway"). The error carries `errors.existingOrderNumber` and `errors.existingOrderPublicId` with the order that already uses it.
It only applies to **typed** invoices; if Teikem generates the invoice number, it is never triggered.

**What does "The client is suspended; shipments cannot be created or confirmed." mean? (HTTP 422)**
The client is in status `SUSPENDED` (chapter 02, section 1.6). No new orders can be created for it, nor can one that was
already Submitted be marked as picked up. Reactivate it (`ACTIVE`) to continue.

**What does "The consignee is required: choose one from the directory or enter a new one." or "Enter a consignee from the directory or a new one, not both." mean? (HTTP 400, field `consignee`)**
Every order needs exactly one consignee: either the `publicId` of a location from the directory
(`consigneeLocationPublicId`), or the data of a new one (`newConsignee`), never both at once nor neither.

**What does "The client exceeds its credit limit: limit X.XX, in progress Y.YY, this shipment Z.ZZ." mean (HTTP 422, `code: "credit_exceeded"`)?**
When creating the order or marking it as picked up, the sum of what the client has in progress (already quoted orders that are not terminal; since Batch 106 Submitted ones count) plus
the amount of this order exceeds its credit limit. It is not a definitive block: the order stays as it was (when creating, it is not created; when marking it as picked up, it stays Submitted) and whoever has the `orders.credit_override` permission can repeat the action with `overrideCredit: true`
to authorize it (see chapter 03, section 3.3; in Batch 4, `overrideCredit` is also accepted together with
`driverPublicId` when creating a special delivery with a driver, chapter 04, section 11).

**What does "There is no rate in force for X/Y; set up the rate in the client's contract before confirming." mean? (HTTP 422)**
When quoting, some line (combination of service type and package type) has no rate in force either in the client's
contract or in the tenant's generic rates. Since Batch 106 creation does **not** give this error (the order is saved Submitted and unquoted); it comes out when marking it as picked up. The order does not move to Pickup nor is it frozen with an amount of $0: add the
missing rate component (chapter 02, section 5) and try again.

**What does "The special service is no longer in force; choose another before confirming." mean? (HTTP 409)**
The special service of a special delivery was closed (or its validity expired) after creating the order but before
marking it as picked up. Edit the order with another special service of the client that is in force and mark it as picked up again.

**I edit an order already picked up and it responds "The current status does not allow the action 'EDIT_CARGO'." (HTTP 422). How do I fix it?**
By default, the cargo of an order (consignee, packages, COD) is only edited while it is **Submitted** (`SUBMITTED`).
An order already picked up is not edited unless an administrator enables `EDIT_CARGO` for the `PICKUP` status from
`PUT /api/v1/status/capabilities/TRANSPORT_ORDER`; in that case, editing it also requires the `REPRICE` capability (it re-quotes
so as not to leave an old amount). If you do not want to relax that rule, cancel the order and enter a new one.

**What does "Advancing to 'X' is done by the corresponding module (trips/deliveries); from here only lateral statuses are recorded." mean? (HTTP 422)**
`POST /api/v1/orders/{id}/status` only moves the order to the lateral status Failed attempt (`FAILED`) or returns it
to the pipeline it came from. Marking it as picked up and cancelling it have their own action. Advancing the pipeline itself (in transit, delivered…) is done, since Batch 4, by
assigning a driver to a special delivery (chapter 04, section 11), or will be done by the routes and deliveries module of a
later batch for the other orders.

**I invite the same e-mail from another client of my company and it was already a portal user: why is it sometimes ACTIVE immediately and other times it asks me for an invitation?**
If the account already had a password (it had already accepted a previous invitation in any client) and is still enabled,
the new client is added directly in status `ACTIVE`, without a link (the `portal_client_added` event is recorded). If the
account never set a password, or was deactivated because it had no live client left, it is born `INVITED` with an
invitation link as always. See chapter 03, section 6.

**I removed a portal user from a client and they can still sign in with the same account in another client. Is that a bug?**
No: since Batch 3, a portal account can belong to several clients of the company (one row per client). Removing
or suspending acts only on the row of that client; the account (password, sessions) is deactivated only when it has **no other** active row
left in the company, and is reactivated as soon as there is one again.

## Batch 4 — Fleet, drivers and maintenance

Messages verified against `src/Teikem.Domain/Fleet/*`, `src/Teikem.Infrastructure/Fleet/*` and
`src/Teikem.Infrastructure/Services/{Vehicle,Driver,DispatchZone,Fleet,Maintenance,FuelLog,DriverRate,
DriverPayPolicy,DriverTrip,SpecialDelivery}*.cs`. The full chapter is in
[04-flota-choferes-mantenimiento.md](04-flota-choferes-mantenimiento.md).

### Vehicles and their documents

**What does "A vehicle with that code already exists." mean?**
The `code` you sent is already used by another vehicle of your company, even if it is retired: the code is not freed when
deleting a vehicle. Choose another code.

**What does "An active vehicle with that VIN already exists." mean?**
Another **active** vehicle already has that VIN. If the other vehicle is retired (not active), the VIN can indeed be
reused.

**What does "The vehicle code is set when creating it; it cannot be changed." mean?**
You sent `code` (or `homeWarehouseId`) in a `PATCH /vehicles/{publicId}`. Neither of the two is edited after creation.

**What does "The VIN allows at most 40 characters." mean?**
The VIN is saved in uppercase without spaces; it cannot exceed 40 characters.

**What does "The model year must be between 1900 and {year+1}." mean?**
`modelYear` is out of range: it is accepted up to the year after the current one (next year's model is already on sale).

**What does "The odometer cannot be less than the last recorded reading ({km} km on yyyy-MM-dd)." mean?**
You tried to manually correct the vehicle's `currentOdometerKm` below an already recorded fact: an active fuel
fill-up or a closed work order. You can lower an entry error, but never below that
reading. Check the fuel fill-ups (section "Fuel log") and the vehicle's work orders.

**What does "The vehicle has been permanently retired; it cannot be reactivated." mean?**
The vehicle reached the terminal status `INACTIVE` (permanent retirement); it is no longer reactivated, not even with the Active checkbox. If
it was a mistake, create a new vehicle.

**What does "The vehicle is inactive; reactivate it to record work orders or fuel fill-ups." mean?**
The vehicle has the Active checkbox off or is in the terminal status `INACTIVE`. Reactivate it
(`POST /vehicles/{id}/reactivate`, if it is not permanently retired) before recording a new work order
or a fuel fill-up.

**What does "Unknown vehicle type: 'X'." / "Unknown ownership: 'X'." / "Unknown fuel type: 'X'." mean?**
The code you sent (`vehicleType`, `ownership` or `fuelType`) does not exist in the tenant's list. Check
`GET /api/v1/catalogs/{VehicleType|Ownership|FuelType}` to see the valid codes.

**What does "Unknown vehicle document type: 'X'." mean?**
The `docType` is not one of `REGISTRATION`, `INSURANCE`, `INSPECTION`, `PERMIT` (or another that your company has added to
that list).

**What does "Document not found." mean? (vehicle document)**
The document `id` does not belong to the vehicle in the URL (it belongs to another vehicle, or to another tenant). Documents are
always looked up under their vehicle (`/vehicles/{publicId}/documents/{id}`), never by a loose id.

**I renewed an expired document and did not deactivate it: why did it stop appearing in "Expiring documents"?**
A document with an expiration becomes "superseded" when the same owner has another **active** one of the same type with a
later expiration (`isSuperseded: true` in its record). A superseded document does not block availability, does not
count for the next expiration and does not appear in the "Expiring documents" panel: there is no need to deactivate the
old one by hand when renewing.

### Drivers, licenses, certifications, devices and zones

**What does "A driver with that code already exists." mean?**
The `code` (employee) is already used by another driver of your company, even if deleted: the code is not freed. Choose another.

**What does "The driver code is set when creating it; it cannot be changed." mean?**
You sent `code`/`employeeCode` in a `PATCH /drivers/{publicId}`. A driver's code is immutable.

**What does "The driver was deleted; only their history can be viewed." mean?**
The driver reached the terminal status `INACTIVE` (action "Delete driver"). Their profile, licenses, certifications and
trips can still be viewed, but no edits or new rates are allowed.

**What does "The driver was deleted; they cannot be reactivated." mean?**
"Delete driver" is a **permanent** deactivation (terminal status): unlike the Active checkbox, there is no going
back. If the driver returns, create a new one.

**What does "The driver was already deleted." mean?**
You tried `DELETE /drivers/{publicId}` on a driver that was already in the terminal status. There is nothing to do.

**What does "Missing permission 'admin.users'." mean? (when linking or removing a driver's user)**
Linking or unlinking a driver's internal user (`userId`/`clearUser`) requires, besides `fleet.manage`, the
`admin.users` permission. Ask an administrator to add it to your role, or to make the link themselves.

**What does "Only an internal user can be linked to a driver." mean?**
The `userId` you sent corresponds to a **portal** user (end client), not to an internal user of the
company. Only internal users are linked to a driver.

**What does "The user has no active membership in this company." mean?**
The user exists, but their membership in your tenant is suspended or inactive. Reactivate it first
(`PATCH /api/v1/users/{id}/membership`, chapter 01) or choose another user.

**What does "The user is already linked to another driver." mean?**
Each internal user is linked to **only one** driver per company. Unlink them from the other driver first
(`clearUser: true`) if they really should move to this one.

**What does "The dispatch zone is inactive." mean?**
The `dispatchZoneId` you sent as the driver's primary zone is deactivated. Reactivate it or choose another zone.

**What does "The zone has drivers assigned; reassign them before deactivating it." mean?**
A zone cannot be deactivated while any active driver has it as their primary zone. Change their zone
(`PATCH /drivers/{id} {"dispatchZoneId": ...}` or `clearZone: true`) and try again.

**What does "A dispatch zone with that code already exists." mean?**
The zone `code` is already used by another zone of your company. Choose another.

**What does "Unknown license class: 'X'." / "Unknown certification type: 'X'." mean?**
The code you sent does not exist in your company's `LicenseClass`/`CertificationType` list. Check
`GET /api/v1/catalogs/{LicenseClass|CertificationType}`.

**What does "License not found." / "Certification not found." / "Device not found." mean?**
The `id` does not belong to the driver in the URL (it belongs to another driver, or to another tenant). These resources are always
looked up under their driver (`/drivers/{publicId}/licenses/{id}`, etc.), never by a loose id.

**A device stopped receiving notifications after deleting the driver, is that correct?**
Yes. On "Delete driver" (terminal status), all their devices are automatically deactivated, in addition to
unlinking the user, removing the zone and closing their open rates.

### Expiring documents and availability for dispatch

**What does "withinDays must be between 0 and 365." mean?**
The `withinDays` parameter of the "Expiring documents" panel is out of range. Use a value between 0 and 365 (30 by
default).

**What does "Unknown document type: 'X'." mean? ("Expiring documents" panel)**
The filter's `docType` is not one of `REGISTRATION`, `INSURANCE`, `INSPECTION`, `PERMIT`, `LICENSE`, `CERTIFICATION`.

**What does "Unknown entity: 'X'; use VEHICLE or DRIVER." mean?**
The `entity` filter only accepts `VEHICLE` or `DRIVER`.

**The driver/vehicle shows as "not available" in the availability panel and I don't understand why.**
Check the `issues` array of the response: each reason carries `code`, `message` and `blocking`. The ones that really
block (`blocking: true`) are: driver or vehicle with the Active checkbox off, driver or vehicle in a status
other than the initial one, driver without a valid license, expired vehicle document (not superseded), or a work
order of the vehicle in `IN_PROGRESS`. The others (expired certification, document expiring soon, vehicle without
documents) are only warnings: the resource can still be `available: true`.

### Preventive maintenance and work orders

**What does "Choose the vehicle or the vehicle type of the program, not both." mean?**
A maintenance program applies to **one** vehicle or to **one** vehicle type, never to both nor to neither.
Send exactly one of `vehiclePublicId`/`vehicleType`.

**What does "A mileage program needs an interval in km greater than 0." / "A time program needs an interval in days greater than 0." mean?**
The trigger (`trigger`) you chose (`MILEAGE`, `TIME` or `BOTH`) requires the corresponding interval
(`intervalKm`/`intervalDays`), greater than zero.

**What does "The last service is only entered in programs of a vehicle; in type programs it is taken from their closed work orders." mean?**
A program **by vehicle type** has no "last service" of its own: it is calculated, vehicle by vehicle, from its
last closed work order linked to that program. `lastServiceKm`/`lastServiceDate` are only entered in a
program **by vehicle**.

**What does "Maintenance program not found." mean?**
The `scheduleId` does not exist in your company.

**What does "The maintenance program does not apply to this vehicle." mean?**
The program you indicated in the work order belongs to another vehicle, or to a vehicle type different from that of the
vehicle of the order.

**What does "A corrective order is not linked to a preventive program." mean?**
You sent `scheduleId` (which is always that of a preventive program) together with `maintenanceType: CORRECTIVE`. Remove the
program or change the type to `PREVENTIVE`.

**What does "The current status does not allow the action 'EDIT_WORK_ORDER'." mean?**
The work order is in a status where it cannot be edited (by default `CLOSED` and `CANCELLED`): neither the header nor
its tasks. An administrator can relax it in `PUT /api/v1/status/capabilities/WORK_ORDER` if your company
needs it; the record anticipates it with `canEdit: false`.

**What does "The order has tasks: labor and parts costs are calculated from the sum of its tasks." mean?**
With active tasks, the header's `laborCost`/`partsCost` are the **sum** of the tasks: they are not entered by hand.
Edit the costs of each task, or deactivate them all if you prefer to enter the direct cost in the header.

**What does "The order has N task(s) not completed; mark them as completed or remove them before closing it." mean? (HTTP 422)**
The work order cannot be closed with pending active tasks. Mark `isCompleted: true` on each task, or
deactivate it if it no longer applies.

**What does "Enter the odometer reading to close an order of a mileage-based program." mean?**
The order is linked to a mileage program (or both) and is missing the closing odometer. Send `odometerKm` in
the body of the status change, or enter it beforehand in the header.

**What does "The closing date cannot be in the future." mean?**
`completedDate` (when closing the order) cannot be a date yet to come.

**I closed the work order and the vehicle went back to "Active" on its own. Is that correct?**
Yes. When starting a WO (`IN_PROGRESS`) the vehicle moves to `MAINTENANCE` if it was `ACTIVE`; when closing or cancelling it,
if no other WO of that vehicle remains `IN_PROGRESS`, it returns to `ACTIVE` automatically (even if `MAINTENANCE`
had been set by hand).

**What does "A work order with that number already exists; try again." mean?**
Two simultaneous creations collided when drawing the `OT-#####` number. Retry the request: the number is automatic and
sequential, so there is no need to choose one.

### Fuel log

**What does "Liters must be greater than 0." mean?**
`liters` must be a positive number.

**What does "The vehicle of a fill cannot be changed; deactivate it and register another one." mean?**
The vehicle of a fuel fill-up is immutable. If you picked the wrong vehicle, deactivate the fill-up
(`POST /fuel-logs/{id}/deactivate`) and register a new one on the correct vehicle.

**What does "The fuel fill-up is inactive; it cannot be corrected." mean?**
You tried to edit (`PATCH`) a fill-up that is already soft-deleted. Reactivating it is not available in this
batch; register a new fill-up if needed.

**What does "The odometer reading (…) is less than that of a previous fill-up of the same vehicle (…)." / "…is greater than that of a later fill-up of the same vehicle (…)." mean?**
The odometer of a vehicle's fill-ups must be monotonic by date: it cannot be below a previous fill-up
nor above a later one. Check the date and the reading you entered.

**What does "The fuel fill-up changed while it was being saved; reload and try again." mean?**
Two corrections to the same fill-up collided (row lock of the vehicle). Reload the fill-up and repeat the correction.

**Why is the km/L of a fill-up empty (`null`)?**
The odometer of that fill-up is missing, or that of the previous fill-up with odometer of the same vehicle (the first fill-up with
an odometer in the series never has its own km/L), or the calculated distance is not positive. The km/L and the cost/km are
calculated on read, over the vehicle's complete active series; they are not stored.

### Driver rates and pay policy

**What does "The driver was deleted; their rates and trips can only be viewed." mean?**
The driver is in the terminal status `INACTIVE` ("Delete driver"). No new rates or trips can be added, edited or closed
for them anymore; their history and existing trips can still be viewed.

**What does "Rate not found." mean?**
The rate `id` does not belong to the driver in the URL (it belongs to another driver, or to another tenant). The three rates
(delivery, attempt, trip) are always looked up under `/drivers/{publicId}/...`, never by a loose id.

**What does "Enter the service and the package type of the rate." mean?**
When adding a rate per delivery, `serviceType` or `packageType` was missing; both are required and exact (there is no
"any package" wildcard).

**What does "The driver already has a rate in force for {Service} + {Package}; edit that rate or close it before adding another." mean?**
There is already an open rate per delivery (or one in force on the given date) for that same service+package pair.
Edit it (`PATCH`, closes and opens a new version) or close it (`.../close`) before adding another.

**What does "The service and the package are set when creating the rate; remove the row and create a new one." mean?**
You sent `serviceType`/`packageType` in a `PATCH` of a rate per delivery. Those two fields are immutable: close the
row and create a new one with the correct combination.

**What does "The rate is already closed; add a new one if you need to pay it again." mean?**
You tried to edit or close a rate row (delivery, attempt or trip) that already has a closing date. Add a
new row.

**What does "Attempt {n} does not exist; the configured levels go from 1 to {N}." mean?**
The attempt number you sent exceeds the levels configured in the company's policy
(`DriverPayPolicy.AttemptLevels`, cap 20). Raise the number of levels with `POST /driver-pay-policy/attempt-levels`
before setting the rate of a higher attempt.

**What does "The driver already has a rate in force for attempt {n}; edit that rate or close it before adding another." mean?**
Same as the delivery one, but for the given attempt level.

**What does "Attempt {n} has a rate in force until {date}; the new one must start on that date or later." mean?**
That attempt level has a row **already closed in the future**; the new effective period you are trying to open cannot start
before that row ends. Use that date or a later one.

**What does "The maximum of 20 attempt levels has been reached." mean?**
The company's `AttemptLevels` is already at the cap (20). No more attempt levels can be added (nor are they
removed in this batch).

**What does "Unknown pay formula: 'X'." mean?**
The formula code (`payoutFormula`/`formula`) is not one of `DELIVERY_PLUS_ATTEMPTS`, `DELIVERY_INCLUDES_FIRST` or
`FAILED_REPLACES_DELIVERY`.

**What does "No special services: add them in Clients and contracts before setting up trip rates." mean?**
Your company has no active special service type (chapter 02, section 6): the "trip type" of the
trip rates is that same list. Create at least one before setting up trip rates.

**What does "The trip type is required." mean?**
When adding a trip rate (or a manual trip), `specialServiceTypeId` was missing.

**What does "The trip type is set when creating the rate; remove the row and add one with the correct type." mean?**
You sent `specialServiceTypeId` in a `PATCH` of a trip rate. That field is immutable: close the row and add
a new one with the correct type.

**What does "The driver already has a rate in force for trip type '{type}'; edit that rate or close it before adding another." mean?**
Same as the delivery/attempt one, but for that trip type.

**I made a pay preview and one line comes out at $0 with the note "no rate configured". Is that a bug?**
No: it is intentional (R15). When the rate of the delivery or of an attempt is missing, that line is shown at $0 with the
note, instead of being omitted, so that the gap is visible and can be fixed by setting up the missing rate.

### Trips paid to the driver and special delivery with a driver

**What does "Trip not found." mean?**
The `tripPublicId` does not belong to the driver in the URL (it belongs to another driver or to another tenant).

**What does "The trip comes from a special delivery; cancel the shipment or reassign the driver." mean?**
A trip linked to a current order (special delivery) is not cancelled directly from
`/drivers/{id}/trips/{tripId}/cancel`. Cancel the transport order, or assign the delivery to another driver
(`POST /orders/{id}/driver`): the previous trip is cancelled automatically.

**What does "The driver is not available for dispatch: {reasons}." mean?**
When assigning a driver to a special delivery, the availability check (the same as the panel in the
"Dispatch availability" section) found at least one blocking reason: driver/vehicle inactive or in a status
other than the initial one, driver without a valid license, etc. Check `GET /api/v1/fleet/availability` to see the detail
for that driver and resolve it (renew the license, reactivate them…) before retrying.

**What does "The driver is only assigned on a special delivery." mean?**
You sent `driverPublicId` when creating an order that is **not** a special delivery (`isSpecialDelivery: false`). The
driver is only indicated on special deliveries; the other orders are dispatched from the Dispatch room (a later
module).

**What does "Only special deliveries are assigned to a driver from here; the other shipments go through the Dispatch room." mean?**
You called `POST /orders/{publicId}/driver` on a normal order (not a special delivery). That endpoint is
exclusive to special deliveries.

**What does "The special delivery already reached its destination or finished; the driver cannot be assigned or reassigned." mean?**
The order already went past `IN_TRANSIT`, or is in a lateral or terminal status (cancelled, on hold, etc.). The driver can
no longer be assigned or reassigned at that point.

**What does "The shipment is already assigned to that driver." mean?**
You tried to assign the same driver who already has the current trip of that order. There is nothing to change.

**What does "The shipment already has a current trip; reload and try again." mean?**
Two simultaneous assignments (or reassignments) to the same order collided. Reload the order record and repeat.

**I reassigned the delivery to another driver, what happens to the previous driver's trip?**
It is cancelled automatically (it is left `CANCELLED` and `isActive: false`) before creating the new driver's trip: the
database guarantees that there is only one current trip per order at all times.

**Why does the order history show several status changes in a row when assigning a driver to it?**
The assignment advances the order **stage by stage** (without skipping) from where it was up to `IN_TRANSIT`, respecting the
pipeline configured by your company (it skips the stages you have disabled). Each stage is left as its own
history record, with the same comment ("Special delivery assigned to …" or "Reassigned to …").

## Batch 5 — Trips and routes

### Numbers and creating the route

**What does "Enter the route date." mean? (400)**
`POST /api/v1/trips`, `PATCH /api/v1/trips/{publicId}` (when the date changes), `POST /api/v1/trips/reassign-zone`
and `POST /api/v1/trips/plan-day` require `planDate`. Send it in date format (`"2026-09-28"`).

**What does "The route date cannot be earlier than yesterday or later than 60 days from today." mean? (400)**
The plan date is only accepted between yesterday and today + 60 days. Routes cannot be created or moved to dates too
far in the past or in the future. Fix `planDate` (or `dispatchZoneIds`/`planDate` of "Plan the day").

**What does "The departure time must fall on the route date (±12 h for time zones)." mean? (400)**
`plannedStartUtc` must fall within the plan date, with a margin of 12 hours backwards and forwards
(to cover time zones). If you send nothing, the route uses 12:00 UTC (08:00 Puerto Rico time) of that date.

**What does "A route with that number already exists." mean? (409)**
Two simultaneous creations collided on the sequential number (very rare: the counter is locked internally until the
commit). Retry the request; the system will give it the next available number.

**Why does my `POST /api/v1/trips` with a zone but without a driver not bring any assigned driver?**
The zone's "standard" driver is only assigned if there is **exactly one** active driver with that zone as primary
zone and they are available that day, and the `CATALOG` module is on. If there are zero, two or more drivers with that
primary zone, or the only candidate is not available, the route is born without a driver (it is not an error) and you assign one
by hand with `PATCH`.

### Route header, reassignment and planning the day

**What does "The route number is set when creating it; it cannot be changed." mean? (400)**
The `PATCH /api/v1/trips/{publicId}` does not accept `code` or `tripCode` in the body. Remove that key from the JSON; the
number is never edited.

**What does "The route status changes with its actions (optimize, dispatch, delete)." mean? (400)**
There is no `PATCH` that changes the status directly (`status`/`statusCode`/`toCode`): the status moves by
optimizing, dispatching, recording the departure or deleting the route. Remove those keys from the body.

**What does "That field cannot be modified." mean? (400)**
The body of the `PATCH` carries `tenantId`, `version`, `routeVersion` or `originWarehouseId`: none of them is edited this
way. The tenant comes from your session; the route version changes only when optimizing.

**What do "Choose the driver or remove them, not both.", "Choose the vehicle or remove it, not both.", "Choose
the zone or remove it, not both." and "Choose the departure time or remove it, not both." mean? (400)**
You sent at the same time the new value (`driverPublicId`, `vehiclePublicId`, `dispatchZoneId`, `plannedStartUtc`) and its
"remove" flag (`clearDriver`, `clearVehicle`, `clearZone`, `clearPlannedStart`). Send only one of the two.

**What does "A dispatched route must keep its driver, vehicle and departure time; change them instead of
removing them." mean? (422)**
Your company enabled the `EDIT_TRIP` capability in `DISPATCHED` or `IN_PROGRESS`, so the header of a dispatched
route can be corrected (for example, changing the driver if the truck broke down). But a dispatched route

**cannot be left without a driver, without a vehicle or without a departure time**: the driver already sees it in their app and the ETAs
depend on the departure. In the `PATCH /api/v1/trips/{publicId}` send the **new** driver, vehicle or time
(`driverPublicId`, `vehiclePublicId`, `plannedStartUtc`) instead of `clearDriver`, `clearVehicle` or
`clearPlannedStart`. For the same reason, recording the departure (`POST /trips/{publicId}/start`) of a route without a
driver or without a vehicle responds 422 "Route … cannot be dispatched: …".

**What does "The date and the zone of a dispatched route cannot be changed." mean? (422)**
Even if your company enabled `EDIT_TRIP` for the route's current status, the plan date and the dispatch zone
of an **already dispatched** route are never edited (they would completely change the meaning of the route). If you need to move
the deliveries to another day or zone, create or use another route.

**Why did the bulk reassignment (`POST /api/v1/trips/reassign-zone`) not change the driver of some open
routes?**
The reassignment only touches the **open** routes (`DRAFT` or `PLANNED`, active) of those zones and that date, and it also
respects the `EDIT_TRIP` capability: if your company denied it for a route's status
(`/status/capabilities/TRIP`), that route is skipped, just like the already dispatched ones, because its driver could not be changed
with a `PATCH` either. The response lists in `trips` only the updated routes and `tripsUpdated` counts them
(it can be 0, with 200). If you need to reassign them, allow `EDIT_TRIP` again in that status.

**What do "Choose at least one zone.", "At most 50 zones per reassignment." and "Choose the driver." mean? (400)**
`POST /api/v1/trips/reassign-zone` requires `dispatchZoneIds` with at least one zone (maximum 50) and `driverPublicId`.
Fill in the one that is missing.

**What does "Route {code} is closed; it can only be viewed." mean? (422)**
The route was deleted (`CANCELLED`, `isActive: false`) or already finished (`COMPLETED`). It does not allow `PATCH`, adding
orders or a second `DELETE`; its record remains available for viewing. In the list `GET /api/v1/trips` the
deleted routes do not appear unless you request `includeCancelled=true` or filter `status=CANCELLED`.

**What does "Route {code} was already dispatched; it cannot be edited or deleted." mean? (422)**
The route is `DISPATCHED` or `IN_PROGRESS` and its status does not have the `EDIT_TRIP` capability enabled (the factory
value). It does not allow header `PATCH`, adding/removing orders, optimizing, reordering, manual pin or `DELETE`.
If you need to correct the driver, the vehicle or the departure time of an already dispatched route, ask an
administrator to enable `EDIT_TRIP` for that status in `PUT /api/v1/status/capabilities/TRIP`.

**What does "Unknown route status: 'X'." mean? (400)**
The `status` filter of `GET /api/v1/trips` carries a code that does not exist in `TripStatus` (`DRAFT`, `PLANNED`,
`DISPATCHED`, `IN_PROGRESS`, `COMPLETED`, `CANCELLED`). Fix the code; you can repeat `status` to filter
by several at once.

**What does "The dispatch zone is inactive." mean when planning the day or when creating/editing a route? (400)**
One of the given zones is deactivated. Remove it from the list or reactivate it first in `dispatch-zones`. In
"Plan the day", if you do not send `dispatchZoneIds`, **all the active zones** of your company are planned and
the inactive ones are simply ignored, without error.

**What does "Route not found." mean? (404)**
The `publicId` does not correspond to any route **of your company**, either because it does not exist or because it belongs to another
tenant. For security, both cases give the same `404` (it is never revealed whether the route exists in another company).

### Orders on the route and the "Unassigned" list

**What do "Enter at least one shipment." and "Add at most 200 shipments per request." mean? (400)**
`POST /api/v1/trips/{publicId}/orders` requires at least one `orderPublicIds` and allows at most 200 distinct ones per
request (duplicates are collapsed automatically). If you need to add more, do it in several calls.

**What does "Order not found." mean? (404)**
The `orderPublicId` is not from your company, does not exist, is inactive, or (when removing) that order is not registered at
all. Check the id; if the order exists but is not on THAT route, the message is different (see below).

**What does "There are shipments that cannot be assigned to the route." mean? (422)**
When adding several orders at once, **a single** one that is not eligible rejects the **whole** request (nothing is
added): check `errors`, which carries the number of each problem order and its exact reason (see the eligibility
table below). Remove those orders from the request, or resolve their cause (confirm them, take them out of a
lateral, etc.) and try again.

**What are the exact reasons why an order is not eligible for a route?**
- `The shipment is in “Submitted”; mark it as picked up before assigning it to a route.` — it is still in `SUBMITTED` (Submitted, initial stage).
- `Special deliveries are assigned to the driver from the shipment; they do not go through the Dispatch room.` — use
  `POST /api/v1/orders/{publicId}/driver` (chapter 04).
- `The order is in '{status}'; return it to the pipeline before assigning it to a route.` — it is in a lateral
  (`ON_HOLD`, `PARTIAL`, `FAILED`, …); first return it to the normal pipeline.
- `The shipment already left on a route or finished; it cannot be assigned to another route.` — it is inactive, in a terminal stage, or
  already reached `IN_TRANSIT` or later.
- `The current status does not allow the action 'ASSIGN_TRIP'.` — your company turned off that capability for that status
  (`PUT /api/v1/status/capabilities/TRANSPORT_ORDER`).
- `The shipment has no pending delivery stop.` — it has no unfinished `DELIVERY` stop left.

**What does "The shipment is already on this route." mean? (409)**
You tried to add an order that already has a current stop on the same route. There is nothing to do; it is already there.

**What does "The order is already assigned to route {code}." mean? (409)**
The order already has a current route **different** from the one you are editing. Remove it from that route first
(`DELETE .../orders/{orderPublicId}`) if you want to move it to another.

**What does "The shipment is already assigned to another route." mean when adding an order? (409)**
Two simultaneous additions of the same order (on different routes, or from the Outbound scan and the Dispatch room at the
same time) collided; the one that arrived first won. Reload the "Unassigned" list and repeat if you still need it on your route.

**What does "A route allows at most 300 stops." mean? (400)**
It is the hard technical cap of this batch. Split the load into two routes if you need more than 300 stops.

**What does "The shipment is not on this route." mean? (404)**
You tried to remove (`DELETE .../orders/{orderPublicId}`) an order that does not have a current stop on THAT route
(it may be on another route, or on none). Check which route it is really on before removing it.

**What does "Use dispatchZoneId or noZone, not both." mean? (400)**
The "Unassigned" filter (`GET /api/v1/trips/unassigned-orders`) does not accept `dispatchZoneId` and `noZone=true` at the
same time: use one or the other.

**What does "The date range is invalid." mean in "Unassigned"? (400)**
`requestedFrom` is later than `requestedTo`. Fix the order of the range.

### Optimization, manual sequence and pin per stop

**What does "The route has no stops to optimize." mean? (422)**
The current version of the route has no order assigned. Add at least one order before optimizing; no
run is created when this happens.

**What does "The route changed while it was being optimized; optimize again." mean? (409)**
The optimization runs in three phases and the engine calculates **without locking the route**: between when it started and when it finished,
someone else modified it (added/removed an order, reordered it by hand, edited it, or another optimization arrived first).
The run is recorded with `ERROR`. Simply request `POST .../optimize` again: since the route is now in
its most recent state, the new run should indeed apply.

**What does "The optimizer could not calculate the route; the run was recorded with an error." mean? (409)**
The engine (`HEURISTIC`) threw an internal error, took more than 60 seconds, or returned a result that did not pass the
validations (for example, a repeated stop or one foreign to the request). Check
`GET /api/v1/trips/{publicId}/optimization-runs` to see the technical detail (`errorMessage`) and try again;
if it persists, it is a case for support.

**What does "The sequence must include exactly the stops of the current route, without repeating." mean? (400)**
`PUT /api/v1/trips/{publicId}/route/sequence` requires `routeStopIds` to be an **exact permutation** of the
stops of the current version of that route: no more, no less, no repeats, none from another route (or another already
archived version). First request the record (`GET .../{publicId}`) to take the correct `id`s from `stops`.

**What do "Enter the latitude and the longitude.", "The latitude must be between -90 and 90." and "The longitude
must be between -180 and 180." mean? (400)**
`PUT /api/v1/trips/{publicId}/stops/{routeStopId}/location` requires numeric `lat` and `lng` within range. Check
that they are not swapped (latitude and longitude reversed is the most common mistake).

**What does "Stop not found on this route." mean? (404)**
The `routeStopId` does not belong to the **current** version of that route (it belongs to another route, to another tenant, or to a
version already archived by a later optimization). Request the record again to take the current ids.

**Why did the ETAs of the following stops change after setting the manual pin of a stop?**
The manual pin recalculates the whole route with the new coordinate (same formula as the optimization: haversine × 1.3 at
35 km/h), so the legs and times after that stop are adjusted. It is the expected behavior.

### Dispatch and departure

**What does "Route {code} cannot be dispatched: {reasons}." mean? (422)**
The dispatch has one or more active **blockers**; `errors` carries the code of each one (`NO_DRIVER`,
`NO_VEHICLE`, `NO_STOPS`, `DRIVER_UNAVAILABLE`, `VEHICLE_UNAVAILABLE`, `ORDER_NOT_ELIGIBLE`) with its message. Before
dispatching, check `GET /api/v1/trips/dispatchable` or the route record (`issues`) to see exactly what is
missing: assign driver/vehicle, resolve the availability, add stops or resolve the non-eligible order
(eligibility section above).

**What does "This company's route pipeline does not have the DISPATCHED stage enabled." (or "ACTIVE") mean? (422)**
Your company disabled that stage in `PUT /api/v1/status/capabilities` or in the `TripStatus`/
`RouteStatus` pipeline. Without `DISPATCHED` enabled no route can be dispatched; without `ACTIVE` the route version
cannot be frozen when dispatching. Enable the stage again if you need to use dispatch.

**What does "Order {number}: {reason}" mean when dispatching? (422)**
An order that was on the route stopped being eligible between when the selector was built and the moment of dispatch (for
example, someone cancelled it, sent it to a lateral, or your company turned off its `ASSIGN_TRIP`). The reason is the same
list from the eligibility section. Remove that order from the route or resolve the cause, and dispatch again.

**What do "Select at least one route." and "At most 50 routes per dispatch." mean? (400)**
Batch dispatch (`POST /api/v1/trips/dispatch`) requires `tripPublicIds` with at least one route, up to a maximum
of 50 per request. Split the selection if you need to dispatch more.

**Does a foreign or non-existent id in the batch dispatch make the whole request fail?**
No. Batch dispatch always responds `200`; each route is processed in its own transaction. An id that does not
correspond to any route of yours is left in the results list with `dispatched: false` and
`error: "Route not found."`; the other routes of the batch are dispatched normally.

**What does "Route {code} is not dispatched; dispatch it before recording its departure." mean? (422)**
`POST /api/v1/trips/{publicId}/start` requires the route to be `DISPATCHED`. If it is still in `DRAFT`/`PLANNED`,
dispatch it first with `POST .../dispatch`.

**What does "Route {code} already departed." mean? (422)**
The route is already `IN_PROGRESS`: the departure was already recorded once and is not repeated. Check the record to see
`actualStartUtc`.

**What does "The order is on the already dispatched route {code}; it cannot be cancelled while the route is in
progress." mean? (422)**
You tried to cancel an order that is on a `DISPATCHED`/`IN_PROGRESS` route. Once dispatched, the route and its
orders are committed to the driver; to resolve it, coordinate with Dispatch (removing it from the route requires
the route to be open, or waiting for the closure in Batch 7). If the order is on a `DRAFT`/`PLANNED` route
(not yet dispatched), cancelling it releases it automatically without this error.

### Dispatch zones: members and "postal code/town → zone" resolution

**What do "Enter the zone criterion (POSTAL_CODE, POSTAL_RANGE or MUNICIPALITY)." and "Unknown zone
criterion: 'X'." mean? (400)**
`POST /api/v1/dispatch-zones/{id}/members` requires `matchType` with one of those three exact values (in uppercase).
`POLYGON` exists as a list value but is not yet supported (see the next message).

**What does "Polygon zones are not yet supported; use postal code, postal range or municipality." mean? (400)**
This batch only resolves zones by exact postal code, postal range or municipality. Define the territory with one of
those three criteria instead.

**What do "Enter the criterion value." and "Enter the municipality." mean? (400)**
`matchValue` is missing when creating the zone member. For `MUNICIPALITY` the message names the municipality
specifically; for `POSTAL_CODE`/`POSTAL_RANGE` it is the generic message.

**What does "The postal code must have 5 digits (e.g. 00949)." mean? (400)**
The `matchValue` of a `POSTAL_CODE` criterion does not have the 5-digit form. The ZIP+4 format is accepted
(`"00949-1234"`), which is automatically trimmed to the first 5 digits.

**What does "The postal range must have the form 00900-00999 (start less than or equal to the end)." mean? (400)**
The `matchValue` of a `POSTAL_RANGE` criterion must be two 5-digit postal codes separated by a hyphen, with
the first less than or equal to the second.

**What does "The municipality allows at most 120 characters." mean? (400)**
The municipality name in `matchValue` is too long. Municipalities are compared ignoring accents and case
("Bayamón" = "bayamon" = "BAYAMON"), so there is no need to write them in a particular way.

**What does "The zone already has that criterion." mean? (409)**
Exactly that criterion (same type and same normalized value) already exists in the same zone. There is no need
to add it again.

**What does "The value '{v}' already belongs to zone {code}." mean? (409)**
Two **active** zones cannot share territory: the postal code, range or municipality you are trying to
add (or that a member of a zone you are reactivating has) is already covered by another active zone. Remove the criterion
from the other zone first, or deactivate the owning zone, if the territory really changed zones.

**What does "Zone criterion not found." mean? (404)**
The `memberId` you are trying to delete does not belong to THAT zone (`{id}` in the URL): it belongs to another zone, or to another tenant.
Check that you are using the `id` of the correct zone.

**What does "Enter the postal code or the town." mean? (400)**
`GET /api/v1/dispatch-zones/resolve` requires at least one of `postalCode` or `city`.

**Why does `resolve` return `dispatchZoneId: null` without giving an error?**
It is not an error: it means that no active zone has a member that covers that postal code or that town (or
that the result was `ambiguous: true` because two zones tie at the same precedence level — exact postal code,
then postal range, then municipality). In that case, add the missing criterion to the corresponding zone, or
resolve the tie by removing the criterion from one of the two zones.

**What does "The zone has open routes; close them or change their zone before deactivating it." mean? (409)**
A dispatch zone cannot be deactivated while it has routes in `DRAFT`/`PLANNED` (in addition to the rule already
existing in chapter 04: it cannot be deactivated with active drivers assigned to it as primary). Delete or
reassign those routes to another zone first.

### Outbound scan station

**What do "Scan or type a code." and "The code cannot exceed 40 characters." mean? (400)**
`POST /api/v1/scan/outbound` requires a non-empty `code` of up to 40 characters. Scan again or type the code
by hand.

**What does it mean when the scan responds `NOT_FOUND` with "The shipment was not found."?**
The scanned code (shipment, packaging or invoice number) does not match any order of your company. Check that
it is the correct code; the scan never gives `404`, always `200` with this typed result.

**What does `NOT_FOUND` "There are several shipments with that code; scan the packaging." mean?**
The scanned code (for example, a client's invoice number) matches **more than one** order. Scan
the packaging number instead, which does identify a single order.

**What does `ALREADY_ASSIGNED` "It was already on route {code}." mean?**
The order already has a current route (the same one you just scanned it for, or another). It is not assigned again; the
result only confirms which its current route is. The station says "dup" so the operator knows there is no
need to scan it again.

**What does `NOT_ELIGIBLE` mean when scanning an order?**
The order does not meet the eligibility for routes (see the table of reasons in the "Orders on the route" section): for
example, it is still Submitted (it has to be marked as picked up), it is in a lateral status, or its status does not have `ASSIGN_TRIP`
enabled. The message carries the exact reason and the station says "notfound".

**What does `FOUND_UNASSIGNED` "The dispatch zone could not be resolved by postal code or town; it is left
unassigned." mean?**
The order's delivery address does not match any criterion of any active zone. Add a member
(postal code, range or municipality) to the corresponding zone; in the meantime, the order is left "unassigned" and can
be added by hand from the Dispatch room.

**What does `FOUND_UNASSIGNED` "The postal code or town belongs to several zones ({codes}); it is left
unassigned." mean?**
Two active zones tie at the same precedence level for that address (for example, the same municipality
is registered in two zones). Resolve the tie by removing the criterion from one of the two zones.

**What does `FOUND_UNASSIGNED` "There is no open route for zone {code} on date {date}; it is left
unassigned." mean?**
The order's zone was resolved correctly, but there is no `DRAFT`/`PLANNED` route for that zone on that
date. Create a route for that zone and date (by hand, or with "Plan the day") and scan again: the scan
never creates routes on its own.

### Plan the day

**What does "At most 50 zones per planning run." mean? (400)**
`POST /api/v1/trips/plan-day` accepts at most 50 zones in `dispatchZoneIds` per request. Split the
planning into several calls, or do not send the list to plan all the active zones at once.

**Does "Plan the day" create a new route per zone every time I run it?**
No: it is **idempotent**. If there is already an open route (`DRAFT`/`PLANNED`, active) for that zone and that date, it
reuses it (the one with the lowest number if there were more than one) and only adds the orders that still have no current route.
Repeating it several times in a row does not create extra routes or duplicate orders.

**Why do some zones without orders not generate any route when planning the day?**
By default, "Plan the day" only creates a new route if there is at least one assignable order in that zone. If
you need the Outbound scan to have a destination route from early on even if there are no orders yet, send
`createEmptyTrips: true`.

**What does the `CAPACITY_HARD_CAP` warning "The route reached the maximum of 300 stops; the order is left
unassigned." mean in the result of "Plan the day"?**
It is not an HTTP error: it is an informational warning per zone. That particular order would have pushed the route past
300 stops (the technical cap), so it was left unassigned; add it by hand to another route of the same zone.

### Monitoring

**Why don't the totals change when searching in the monitor (`GET /api/v1/trips/monitor?search=…`)?**
It is on purpose: `totals` always reflects **all** the routes of the filtered date and zone, regardless of the
search text; `search` only narrows the `trips` list that is shown. That way the day's summary does not "flicker" while
typing in the search box.

### Permissions and RBAC

**What is the difference between `trips.view`, `trips.plan`, `trips.optimize`, `trips.dispatch` and `trips.scan`?**
`trips.view` is read-only (list, record, "Unassigned", optimization runs, monitor).
`trips.plan` covers creating/editing/deleting the route, adding/removing orders, reordering, the manual pin, the
bulk reassignment and "Plan the day". `trips.optimize` is exclusively for launching the automatic
optimization. `trips.dispatch` covers the dispatch selector, dispatching and recording the departure (and also assigning the
driver of a special delivery, chapter 04). `trips.scan` is only for the Outbound scan station. A role
can have any combination; for example, the Warehouse operator comes with `trips.view` and `trips.scan` but not
`trips.plan` or `trips.dispatch`.

**Why do `POST /api/v1/contacts/OPTIMIZATION_RUN/{id}` or `PUT /api/v1/custom-fields/values/OPTIMIZATION_RUN/{id}`
respond 404 even though the run exists?**
Optimization runs are a **read-only** log: they are viewed with
`GET /api/v1/trips/{publicId}/optimization-runs` (permission `trips.view`), but they do not accept contacts or custom
field values by loose id. For those endpoints every run responds **404**
(`OPTIMIZATION_RUN` uses the closed resolver, just like `DRIVER_RATE` and `FLEET_DOCUMENT`), whether it exists or not: that way nobody
can write on a run or find out which ids exist. In custom fields, whoever does not have
`trips.view` receives **403** first ("Missing permission 'trips.view'."), also without revealing whether the run exists.
If you need to note something about an optimization, do it on the route (`TRIP`), which does accept contacts and custom
fields with the `trips.plan` permission.

## Batch 6 — Inventory and warehouse

### Warehouses and locations

**What does "Code only allows letters, numbers, hyphen and underscore (max 30)." mean? (400)**
The code of a warehouse, zone or dock only accepts `A-Z`, `0-9`, `-` and `_`, up to 30 characters (it is saved in
uppercase). Fix the code in the request.

**What does "Enter the bin's code or its aisle/rack/level/position." mean? (400)**
When creating a bin you have to give an explicit code or at least one of its parts (aisle, rack, level,
position): with the parts, the system builds the composite code (`A01-R02-N3-P04`).

**What does "A warehouse with that code already exists." mean? (409)**
The warehouse code is unique per company. Choose another code; the existing one cannot be reused even if it is
deactivated.

**What does "The warehouse code cannot be changed." mean? (400)**
The code of a warehouse (or of a dock: `The dock code cannot be changed.`) is immutable once created. If you
typed it wrong, deactivate the warehouse and create a new one with the correct code (only if it is still empty). The code of a

**zone** can be edited since Batch 11 (see the "Batch 11" section at the end).

**What does "Warehouse {code} has inventory or open documents; it cannot be deactivated." mean? (409)**
Deactivating a warehouse is **permanent**: it only proceeds if it has no balance (on hand or reserved) and has no open receipts,
counts, tasks, pick batches, cross-dock plans or appointments. The response carries `errors` with the
detail by type (`inventory`, `receipts`, `cycleCounts`, `tasks`, `pickBatches`, `crossDockPlans`,
`appointments`): close or empty whatever appears there before retrying.

**What does "The warehouse is deactivated; it can only be viewed." mean? (422)**
You tried to edit or deactivate again an already inactive warehouse. Warehouse deactivation has no reverse: if you need
to operate in it, create a new warehouse.

**What does "Bin {code} has inventory; it cannot be deactivated." mean? (409)**
Move or remove the inventory from that bin (transfer or outbound adjustment) before deactivating it.

**What does "Specify the warehouse: the company has more than one." mean? (400)**
Several actions (receiving, picking, counting, creating a purchase order or a cross-dock plan) use the
tenant's only active warehouse if none is specified. With more than one active warehouse you have to send
`warehousePublicId` explicitly.

### Products and categories

**What does "A product with that SKU already exists for that owner." mean? (409)**
The SKU is unique **per owner**: the same SKU can be repeated between a product owned by the tenant and one of a
different 3PL client, but not twice for the same owner. Change the SKU or check whether the product already exists.

**What does "{The tracking type / The base unit of measure / The owner} of a product that already has movements cannot be
changed." mean? (409)**
The tracking type (NONE/LOT/SERIAL), the base unit of measure and the owner (`ClientId`) of the product are fixed
in practice from the first movement of the ledger (a receipt, an adjustment, etc.). If the product still has
no movement, those fields can indeed be edited.

**What does "Product {sku} has inventory on hand ({qty}); it cannot be deactivated." mean? (409)**
Take out or transfer the product's inventory on hand (to zero) before deactivating it.

**What does "A minimum pick quantity requires a preferred bin in a PICKING zone." mean? (400)**
`MinPickQty` triggers the automatic replenishment to the product's preferred bin; that bin has to
exist and be in a zone of type `PICKING`. First assign `preferredBinPublicId`/`preferredWarehousePublicId`
to a picking bin, or remove `minPickQty`.

### Inventory, movement ledger and adjustments

**What does "Insufficient stock of {sku} in {bin}: available {x}, requested {y}." mean? (409,
`insufficient_stock`)**
A dispatch, transfer, outbound adjustment or pick requested more than what is available (on hand minus reserved)
in that bin. The operation is rolled back completely (no partial movement is left). Reduce the quantity,
pick from another bin or correct the balance with a cycle count.

**What does "Reason {code} is assigned by the system." mean? (400)**
`RECEIPT_VARIANCE`, `COUNT_VARIANCE` and `PICK_BATCH_REVERSAL` are reasons that only the system uses (receiving,
count and reversal of a pick batch, respectively); also `OPENING_BALANCE` (opening balance of the migration, Batch 10) and
`TRACKING_CONVERSION` (conversion to serial, Batch 26). A manual adjustment entered by a user must use another
reason from the `AdjustmentReason` list (`DAMAGE`, `LOSS`, `FOUND`, `EXPIRED`, `PO_SHORTAGE`, `OTHER`).

**What does "The source and destination cannot be the same bin." mean? (400)**
A transfer moves inventory between two different bins (they can be in different warehouses). If
you need to "move" within the same bin, there is nothing to do: it is already there.

**What does "Lot {n} already exists with other dates; correct the dates or use another lot number." mean? (409)**
The lot number already exists for that product with manufacturing/expiration dates different from the ones you
entered. The system never overwrites the dates of an existing lot: use the same number without entering dates
(it is reused as is) or create a different lot number.

**What does "The reservation to release exceeds what is reserved." mean? (409)**
An attempt was made to release more reserved quantity than is really reserved in that bin/lot (for example,
when cancelling a cross-dock assignment twice). It is an internal guard; if you see it as a user, check
whether the operation was already done before.

### Receiving (ASN and receipts)

**What does "The warehouse has no receiving bin (STAGING zone); specify one." mean? (422)**
The warehouse needs at least one active bin in a zone of type `STAGING` to be able to receive goods (that is where they
"land" before putaway). Create a `STAGING` zone with at least one bin, or specify `stagingBinId`
explicitly if one already exists.

**What does "The arrival notice already has an open or confirmed receipt." mean? (409)**
The same arrival notice (ASN) allows only one active receipt at a time. If the previous receipt was wrong,
delete it (only if it is still open: Expected, Receiving or Discrepancy) before creating a new one against the same notice.

**What does "The purchase order must be sent or partially received to receive against it." mean? (422)**
Receiving is only done against a PO in `SENT` or `PARTIAL`. A `DRAFT` must first be sent (`POST .../send`); a
`RECEIVED` or `CANCELLED` one no longer accepts more receipts.

**What does "Receipt {n} was already confirmed; it cannot be modified." mean? (422)**
Receipts are confirmed **as a whole**, not line by line, and once confirmed they are frozen (no more
lines are entered nor are quantities changed). If something went wrong after confirming, use a manual inventory
adjustment or, if applicable, a return (`RETURN`).

**What does "Arrival notice lines are not deleted; enter 0 as received." mean? (400)**
A line that comes from the arrival notice (or from the purchase order) is not deleted from the receipt, because it documents what
was expected: if in the end nothing arrived, enter `receivedQty: 0` on that line instead of trying to delete it.

**What does "Product {sku} does not belong to the client of the arrival notice." mean? (400)**
An arrival notice of a 3PL client only accepts products whose owner (`ClientId`) is that same client.
Check the product's owner or the notice's client.

### Warehouse tasks (queue, putaway, replenishment)

**What does "Tasks of type {type} are not completed from the queue." mean? (422)**
This batch only brings handlers for `PUTAWAY`, `REPLENISH`, `COUNT` and `CROSSDOCK`. The types `PICK`, `PACK` and `LOAD`
do not yet have a screen or a handler that completes them from the queue (they are left for later batches).

**What does "Count tasks are completed from Cycle count." mean? (422)**
A `COUNT` task can be **started** from the task queue, but is only **completed** by reconciling its count
(`POST /api/v1/cycle-counts/{id}/reconcile`), because that is where the adjustments are calculated and posted.

**What does "The quantity exceeds that of the task." mean? (400)**
When completing a task from the queue, the given quantity cannot be greater than that of the task. If there was
product left over, complete it for the task quantity; the remainder (if any, for less than requested) is left as a
new task automatically.

### Cycle count

> Batch 14: "reconcile" is now **Confirm count & adjust** (`POST …/reconcile`, in one step from Pending or Counted) and the final
> status is Matched or Variance; the 409 message below did not change.

**What does "The count of {sku} in {bin} ({counted}) is less than what is reserved ({reserved}); release the
reservation before reconciling." mean? (409)**
When reconciling, the counted quantity cannot be below what is already reserved in that bin (for example,
for a cross-dock): the adjustment would leave the reserved balance above the on-hand balance, which the system
never allows. Release that reservation (or wait for it to move) before reconciling that line.

**What does "{n} line(s) still to count." mean? (422)**
"Finish counting" (`OPEN → COUNTED`) requires all the lines of the count to have an entry (quantity or
serials). Complete the missing ones or delete them if they do not apply.

**Why does a count line appear marked `systemQtyChanged`?**
It means that the on-hand balance moved (by another ledger movement) between the moment the system
snapshot was taken and the moment it was reconciled. The adjustment that was posted is correct (it was calculated against the

**current** balance, not against the old snapshot), but the mark is there so someone can check whether that intermediate movement
makes sense.

### Picking & packing

**What does "A pick batch can only contain products of a single owner." mean? (400)**
Products owned by the tenant are not mixed in the same pick batch with products of a 3PL client, nor those of two
different clients. Make separate pick batches per owner.

**What does "Insufficient stock of {sku} in {where}: available {x}, requested {y}." mean? (409,
`insufficient_stock`) when picking**
There is not enough available of the product (FEFO exhausted or explicit bin/lot/serial with no stock) to
cover the requested quantity. The whole pick batch is cancelled, including the `EMP-#####` number (no
gap is left in the numbering). Reduce the quantity or check the available balance before retrying.

**What does "The order of pick batch {n} already advanced to '{status}'; the pick batch can no longer be
deleted." mean? (422)**
An already packed pick batch (`PACKED`) is only deleted if its real order is still in the **initial stage** (Submitted). If the order already advanced, you have to work on the order directly (see the
next message).

**What does "This order was born from pick batch {n}; delete it from Picking & packing to restore the
inventory." mean? (409)**
An order created when "packing" a pick batch is only deleted from `DELETE /api/v1/pick-batches/{publicId}`, not
from `DELETE /api/v1/orders/{publicId}`, because deleting it from there does not reverse the inventory that left when
picking. Go to Picking & packing to delete it correctly.

### Purchases and shortages

**What does "The current status does not allow the action 'EDIT_PURCHASE_ORDER'." mean? (422)**
By default, a purchase order is only edited in `DRAFT`. Outside that status (sent, partially received,
received or cancelled) editing is denied, unless the tenant administrator enables it for `SENT` or
`PARTIAL` from `/api/v1/status/capabilities/PURCHASE_ORDER`.

**What does "The line of {sku} already has receipts: it cannot be removed, its quantity cannot go below what was received ({x}) and its cost
cannot change." mean? (400/409)**
Even if your company enables editing a purchase order outside `DRAFT`, a line that already received goods is
protected: it cannot be removed from the order, less than what was already received cannot be ordered, and its unit cost does not
change (it was already used to calculate the value of what was received).

**What does "A fully received purchase order is not cancelled." mean? (422)**
A `RECEIVED` order is terminal: everything ordered was already received (or the shortage was resolved). There is nothing to
cancel; if something arrived wrong, correct it with an inventory adjustment.

**What does "The purchase order has an open receipt; confirm it or delete it before cancelling." mean? (409)**
A purchase order cannot be cancelled while it has an open receipt (Expected, Receiving or Discrepancy: a half-entered receipt) against
it. Confirm that receipt or delete it first.

**What does "A purchase order with receipts is not deleted; cancel it." mean? (409)**
Once a purchase order has any confirmed receipt, it is no longer deleted (it is kept for its inventory
history): instead it is cancelled (possible from `DRAFT`, `SENT` or `PARTIAL`).

**What does "The adjustment quantity exceeds the pending shortage ({pending})." mean? (400)**
A manual adjustment on a line's shortage cannot request more than what is really left to receive on that
line (ordered − received − already resolved). Check the current pending amount (`GET .../shortage-lines`) before resolving.

**What does "Close and Reorder resolve the whole shortage ({pending}); for a part use the manual
adjustment." mean? (400)**
The `CLOSE` and `REORDER` actions always resolve **all** of the line's pending amount; they do not accept a partial
quantity. If you only want to resolve part of the shortage, use `MANUAL_ADJUSTMENT` with the exact quantity.

### Cross-dock

**What does "The module 'CROSSDOCK' is not enabled for this company." mean? (403, `module_disabled`)**
Cross-dock is a functional demonstration, off by default. An administrator turns it on from
Tenant settings (sensitive action: requires recent re-authentication, `POST /api/v1/auth/reauth`).

**What does "The dock already has an appointment that overlaps that time slot." mean? (409)**
Two appointments cannot occupy the same dock at the same time (half-open window: an appointment that ends at 10:00
does not clash with another that starts at 10:00). Choose another time slot or another dock.

**What does "The quantity exceeds what is available for cross-dock ({available})." mean? (409)**
The quantity you are trying to assign to an order exceeds what can still be assigned from that receipt line (what was
received/expected minus what was already assigned to other orders, or the pending putaway if the receipt is already confirmed).
Check `GET /api/v1/cross-dock-plans/{id}/candidates` to see what is really assignable.

**What does "The line's receipt is not yet confirmed; the goods are moved after confirming." mean?
(422)**
When the assignment is on a receipt that is still **open**, the physical goods are not moved (the `CROSSDOCK`)
until that receipt is confirmed: when confirming it, the system distributes what was really received among the assignments
and only then is it ready to be moved.

**What does "The cross-dock task is completed for the confirmed quantity ({qty})." mean? (400)**
When completing the `CROSSDOCK` task from the queue, the quantity has to be exactly the one that was **confirmed**
in the assignment (not the one originally planned, if there was a shortage when confirming the receipt).

**What does "Move or cancel the pending assignments before completing the plan." mean? (422)**
A cross-dock plan is only completed (terminal status) when none of its assignments is still
`PLANNED`: move them (so they leave the inventory) or cancel them first.

## Batch 7A — Warehouse Pulse and Recent activity

**What does "You do not have permission to view the activity of the WAREHOUSE module." mean? (403)**
You requested the Warehouse tab of "Recent activity" (`GET /api/v1/analytics/activity?module=WAREHOUSE`) without the
`inventory.view` permission, or with the `WMS_LOTSERIAL` module off. Ask an administrator for the permission; without `module`, the panel opens
the first tab you can see. If you also do not have `analytics.view` (e.g. the Warehouse operator), the 403 arrives earlier,
from the endpoint's policy.

**What does "The maximum per page is 50." mean? (400)**
The panel requests at most 50 events per page (`take` ≤ 50); use "Show more" (`skip`) for the next ones.

**What does "The window must be 24h, 48h or today." mean? (400)**
The `window` parameter only accepts `24h` (default), `48h` or `today` (from today's midnight in UTC; the tenant does not yet have a time
zone configured, so in Puerto Rico "today" starts at 20:00 of the previous day).

**Why does a FOUND adjustment of a purchase shortage appear twice in Recent activity?**
Because they are two facts: a shortage of the purchase order was resolved (`PO_SHORTAGE_RESOLVED`, optional, links to the order)
and the inventory balance changed with a manual reason (`INVENTORY_ADJUSTED`, mandatory, links to the product). With "Mandatory
only" only the adjustment remains.

**I renamed or disabled an event in the `ActivityEventType` list; why does a mandatory one keep appearing?**
Recent activity respects the tenant's override of the list (`PUT /api/v1/catalogs/ActivityEventType/{code}/override`,
permission `admin.catalogs`): the renamed label is shown in the feed (in the language the override carries; the other
languages keep the base label) and a disabled **optional** event stops appearing. A **mandatory** event
(for example `PO_CANCELLED` or `RECEIPT_CONFIRMED`) cannot be hidden or made optional from the override: it keeps
appearing, also with "Mandatory only". With `"extraJson": "{\"defaultOn\":true}"` in the override, an
optional event that is off by default (`BIN_MOVED`) is turned on for the whole tenant.

**Why does deactivating a category not appear as "Product deactivated"?**
The category is recorded in the log with its own type (`PRODUCT_CATEGORY`); `PRODUCT_DEACTIVATED` is only for
products. A product deactivation appears even if the product was reactivated afterwards within the window.

**What does the "Units received" indicator add up?**
What really came in during the last 7 days: receiving movements plus the receiving variances (with their sign). A
receipt with 10 expected and 8 received adds 8.

**What does the "Counts with variance" indicator count?**
Cycle counts reconciled in the last 30 days with at least one line with a variance or with an adjustment, even if overages and
shortages cancel out in the total.

**Why can someone without `inventory.view` see cycle counts in Analytics?**
The Analytics data sources (among them `CYCLE_COUNT` and the warehouse ones from Batch 6) are queried with `analytics.view`
and the ANALYTICS module, without the business module's permission. It is a decision to review (`docs/lote7A-decisiones.md`); if
they should not see them, remove their `analytics.view` or do not share reports from those sources with them. Since Batch F8a the **indicators and
charts** do require being able to read their source (`CYCLE_COUNT` → `inventory.view`, see chapter 07, section 3); the **views and
reports** keep the previous rule.

## Batch 8A — Warehouse app backend: devices, PIN, idempotency, sync and atomic operations

**What does "This phone is already enrolled in {company} as {code}. Ask the administrator for a code from another company." (409) mean?**
The phone already has an enrollment in the company of that code (a phone can be in several companies, but only once
in each one). The code was not consumed. If you wanted to work in that company, use the enrollment you already have: in "Who
are you?" choose that company. If you wanted another company, ask the administrator of that other company for the code.

**How do I add a person to several companies when creating them?**
In System → Roles & users → "New user", check the companies under "Also add to these companies" (only those where you
manage users are shown). They receive the same roles, by name, in each one. If a role is missing in some company you get
`In {company} the following roles do not exist: {roles}.` (400) and the user is not created in any of them: create the role in that company or change the roles.

**What does "You cannot add users to that company." (403) mean?**
You do not manage users (`admin.users`) in that company, or you are not an active member of it. Ask an administrator of that
company to add the person from there.

**How do I use the same phone in two companies?**
In "Who are you?" tap "Register another company" and type the code the administrator of the other company gave you
(System → Devices). From then on, when signing in the app asks "Which company are you working for?".

**What does "The enrollment code is not valid or has expired." (401) mean?**
The 8-character code the device types is **single-use** and expires in 24 hours; it also stops working if the
administrator generated a new one (`POST /api/v1/devices/{id}/enroll-code`) or deactivated the device. Ask the administrator
for a new code from the devices screen.

**What does "The device is not enrolled or was deactivated." (401) mean?**
The device was deactivated (or its secret is no longer valid because it was enrolled again). Deactivating it closes
its sessions immediately: the token the device holds stops working (401 in sync as well) and cannot be renewed.
Reactivate it and sign in again with the PIN, or enroll the device again. Every rejected attempt of the device login or of
the user list is recorded in Audit → security events as `LOGIN` / `FAILURE` with `stage = device` and the reason
(`device_invalid`, `device_inactive` or `tenant_unusable`); if the device exists, in its company. It is useful to see whether a
lost and deactivated device keeps trying to sign in.

**What does "Wrong PIN." (401) mean?**
The PIN does not match, the user has no PIN in this company, is not an active member or is the platform administrator
(who never signs in through a device). Every failure counts; on the 5th in a row the PIN is locked. A success resets the counter.

**What does "PIN locked for 15 minutes." (423) mean?**
There were 5 wrong PINs in a row. While the lock lasts, not even the correct PIN gets in. Wait 15 minutes, or set
the PIN again (My account → `PUT /api/v1/me/pin`, or an administrator with `PUT /api/v1/users/{id}/pin`), which removes the
lock.

**What does "The current password is incorrect." (400, in `currentPassword`) mean?**
To set or change your own PIN (`PUT /api/v1/me/pin`) you must type your current sign-in password. Check the
password; if you forgot it, change it first. Every wrong password counts toward the account lock (the same one as the
web login): after 5 in a row the account is locked for 15 minutes and, meanwhile, this same message appears even if you
type the correct password (and you cannot sign in to the web either). Wait the 15 minutes and try again with the
correct password.

**Why do I get "Incorrect password." or "The current password is incorrect." with the correct password?**
Since Batch 8A, reauthentication (`POST /api/v1/auth/reauth`), the password change (`PUT /api/v1/auth/password`)
and your own PIN (`PUT /api/v1/me/pin`) count wrong passwords toward the same login lock: 5 in a row
lock the account for 15 minutes. While it lasts, those routes respond as if the password were wrong (401
`Incorrect password.` on reauthentication; 400 incorrect current password on the other two) and the web login
responds `Invalid credentials.`. Wait 15 minutes. If it was not you who failed, tell your administrator: someone
may have tried to guess your password, for example from a device where you signed in with your PIN.

**What does "A device session does not manage the second factor." (403) mean?**
Someone tried to enable or confirm two-step verification (`POST /api/v1/auth/mfa/totp/enroll` or `/confirm`) with a
device session, which is opened with the PIN only. If it were allowed, whoever saw the PIN on a shared device (or
whoever assigned it) would end up with the secret and the recovery codes, and the account owner could no longer sign in to
the web with their password. Enable two-step verification from the web (My account), with your password.

**Why don't I appear in the device's user list?**
The list shows only active internal users, with active membership in the device's company, with a **PIN set** and
with `inventory.view`, sorted by name. Set your PIN in My account and ask for the permission if you lack it. If you have a PIN but
not `inventory.view`, the device login responds 403 `Missing permission 'inventory.view'.`.

**In the device's user list someone appears as "User" instead of their name.**
That person has no full name loaded in their account. The list never shows the e-mail (the device is usually
shared), so without a name the generic text `User` is shown. Ask an administrator to complete the name
(`PUT /api/v1/users/{id}`).

**What does "You cannot assign or remove the PIN of a user with more permissions than you." (403) mean?**
With `devices.manage` (or `admin.users`) you can assign other people's PINs, but only for users whose permissions are a
subset of yours: with that PIN someone signs in as that person on a device with no password or MFA. Ask an
administrator with those permissions to do it, or have the person set their PIN in My account.

**What does "Your PIN was assigned by someone who no longer has your permissions; set your own PIN in My account." (403 on the device login, 401 when renewing the session) mean?**
You did not set your PIN yourself: another person assigned it (with `devices.manage` or `admin.users`), and that person no longer has
all the permissions you have today (you were given more permissions or a new role, or theirs were removed, they were
deactivated or left the company). Since the PIN opens your account with no password or MFA, it stops working until you
set it in My account (`PUT /api/v1/me/pin`, with your password) or someone with at least your permissions reassigns it. If
you had an open session on the device, the next refresh closes it with this same message (401). The security log
records `LOGIN` / `BLOCKED` (or `TOKEN_REVOKED`) with `reason = pin_assigner_lower_privileges`.

**When assigning another user's PIN I get 403 `aal2_required`.**
Assigning someone else's PIN requires a recent reauthentication (AAL2), just like changing roles or permissions: confirm your
password (`POST /api/v1/auth/reauth`) and try again. Only someone who **does** have `devices.manage` or
`admin.users` receives this 403: without either of the two, the permission is rejected first (permission 403, event `PERMISSION_DENIED`) and
reauthenticating does nothing.

**What does "The idempotency key was already used with different content." (409) mean?**
The `Idempotency-Key` header identifies ONE operation: repeating it with the same body returns the saved response
(with `Idempotent-Replayed: true`); with another body or on another route it is an error. Generate a new key per operation.
The key is case-sensitive: `abc-1` and `ABC-1` are different keys and do not collide.

**What does "The operation with this key can no longer be replayed with the current permissions." (409) mean?**
The original operation checked, on its own, a module or a permission that is not the one the route requires (for example
`warehouse.count`, which decides whether the count is blind), and it has changed since: it was gained **or** lost, the
direction does not matter. The API does not replay the old response (it would no longer match today's permissions). Query the
record directly (for example `GET /api/v1/cycle-counts/{id}`) instead of resending the operation; do not resend it
with another key, because it would run again. This does not change what the route itself requires (`[RequirePermission]`,
`[RequireModule]`, `[RequireAal2]`): those are checked on every request, replay or not, and if they are no longer met they give their
usual 403 (or `module_disabled`/`aal2_required`), not this 409.

**What does "The operation with this key is still being processed." (409) mean?**
Another request with the same key is still in progress. Wait and retry with the same key: you will receive its response.

**What does "The idempotency key is not valid." (400) mean?**
The key is empty, repeated or longer than 80 characters. Use a short unique identifier (for example a GUID).

**Why does `Idempotency-Key` have no effect on `/api/v1/users`?**
Routes that return credentials in clear text (`/api/v1/auth`, `/api/v1/me`, `/api/v1/devices`, `/api/v1/platform` and
`/api/v1/users`, whose create returns the temporary password) are excluded from idempotency so secrets are not stored in
the log. They are administration operations, not part of the device queue. The same applies to inviting and resending the
invitation of a portal user (`/api/v1/clients/{id}/portal-users/invite` and `.../resend-invite`), which return the
invitation token. Since 2026-10-11 each of those routes carries the `[SkipIdempotency("reason")]` attribute on its controller (it is no longer a fixed list of prefixes).

**Does the same `Idempotency-Key` work for two different users?**
Yes: the key is per company and user. If another user uses the same key, their operation runs as new (they do not
receive the first one's response or a 409).

**I resent a rejected operation (400) with the same key and got the same 400 again.**
That is expected: business rejections (below 500, except 401, 403, 408, 423 and 429) are saved and replayed with
`Idempotent-Replayed: true`. Fix the request and send it with a new key.

**What does "Too many attempts; wait a minute and try again." (429) mean?**
The device (or the warehouse network) sent too many anonymous requests in one minute: enrolling the device allows 10 per
minute per IP address; the user list, the device login and the heartbeat, 60 per minute each. Wait the
`Retry-After` time (60 s) and retry. If many devices go out through the same IP and hit the limit, support
can raise it (`RateLimiting:DeviceAuthPerMinute`).

**I sent 5 wrong PINs at the same time and the PIN got locked.**
The lock counts every attempt, whether they arrive in series or in parallel: on the 5th failure the PIN is locked for 15 minutes (423
`PIN locked for 15 minutes.`) and the attempts that arrive afterwards also receive 423, even if they carry the correct PIN.

**The device heartbeat responds `isActive: false`.**
The device was deactivated (or the company lost the WMS module). The heartbeat does not return an error so the app can block
access; ask the administrator to reactivate it. With a secret that does not match, the heartbeat responds 401
`The device is not enrolled or was deactivated.`.

**What do "The theme is not valid; use LIGHT or DARK." (400), "The default warehouse is deactivated." (422) and
"The device is deactivated; reactivate it before generating an enrollment code." (422) mean?**
When editing the device (`PATCH /api/v1/devices/{id}`) the theme only accepts `LIGHT` or `DARK` and the default warehouse must
be active. To generate a new enrollment code, first reactivate the device (`POST /api/v1/devices/{id}/reactivate`).

**I save custom fields on `USER_DEVICE` and it responds 404 (or 403).**
The device does not support custom fields: its polymorphic route uses the closed resolver (always 404, like
`PRODUCT_CATEGORY`) and requires `devices.manage` (without it, 403).

**What do "The maximum per page is 500." and "The cursor is not valid." (400) mean?**
Sync (`/api/v1/sync/*`) pages at most 500 rows with the opaque cursor returned in `nextCursor`; do not
build it by hand. For the next pass use `since` = `serverTimeUtc` of the previous pass minus 5 minutes.

**I sync purchase orders on the device and get 403.**
`GET /api/v1/sync/purchase-orders` requires, besides `inventory.view`, the `purchasing.view` permission and the
PURCHASING module enabled (403 `The 'PURCHASING' module is not enabled for this company.`), just like the purchase order
query in the web app. The Warehouse operator role already includes `purchasing.view`.

**I received against a purchase order from the device and the receipt ended up with less than what was ordered.**
With `lines` in `POST /api/v1/receipts` (against a notice or purchase order) what was scanned overrides what was expected: what was not
scanned stays at 0 and the order moves to PARTIAL with its shortage. Without `lines` the full expected amount is received.

**What does "There is no product with that code." (404) mean?**
`GET /api/v1/products/by-barcode/{code}` looks for the exact barcode and, if not found, the exact SKU, only among
active products. Check the label or create the product's barcode.

**What does "To pick and pack in one call use POST /api/v1/pick-batches/collect-and-pack." (400) mean?**
`POST /api/v1/pick-batches` only picks. The variant that picks and packs in one transaction has its own route
(`/api/v1/pick-batches/collect-and-pack`) and responds `{ batch, order }`.

**Counting from the device gives me 403, but I can see the count.**
Viewing the record (blind) requires `inventory.view`; counting (create, capture, what was found and finish) requires
`warehouse.count.capture`, and reconciling, refreshing and deleting require `warehouse.count`. Ask for the missing permission.

**Why don't I see the expected quantities of the count?**
Without `warehouse.count` the count is **blind** (`isBlind = true`): the expected quantities arrive empty so as not to
influence what is counted, and so do `varianceLines` and `netVariance` in the header (in the record and in the list). The
variance is reviewed when reconciling in the web app.

**What does "A device session does not switch companies." (403) mean?**
Someone tried `POST /api/v1/auth/switch-tenant` with the refresh token of a session opened with a PIN on a device. That session
has no password or MFA, so it stays tied to the device's company. To work in another company, sign in with a
password (and MFA) from the web, or enroll a device in that company. The device session stays alive.

**What do "The device code is required." and "The device code allows up to 30 characters." (400) mean?**
When creating a device (`POST /api/v1/devices`) the code is required (empty or only spaces counts as empty) and
allows 1 to 30 characters. The error arrives in `errors.code`. Give a short code, for example the one on the device's label
(`ZB-01`).

**With the Read-only role, the cycle count list no longer shows "Net variance".**
Since Batch 8A, without `warehouse.count` the count is blind in the list as well: the API does not send the variance and the
screen hides that column. Before, these roles saw the real variance. If the person must see it, give them `warehouse.count`.

**What does "Between 1 and 365 days." in `deviceSessionDays` (400) mean?**
The device session lifetime (`PUT /api/v1/tenant/settings`, `admin.tenant`) goes from 1 to 365 days (30 by
default); every device renewal extends it from that moment.

**What does "A device with that code already exists." (409) mean?**
The device code (`POST /api/v1/devices`, field `code`) is unique per company, even if the device that already
uses it is deactivated (that code is not reused). Choose another code.

**What do "The name allows up to 100 characters." and "The model allows up to 80 characters." (400) mean?**
Length limits of the device's name and model when creating or editing it (`POST`/`PATCH
/api/v1/devices`).

**What does "Device not found." (404) mean?**
The device's `publicId` does not exist, or belongs to another company. Check the link or the device list.

**Assigning or removing another user's PIN gives me 403 (without `aal2_required`), or "Missing permission 'devices.manage' or 'admin.users'."**
To assign or remove another user's PIN (`PUT`/`DELETE /api/v1/users/{id}/pin`) you need one of those two
permissions. The permission policy rejects it before any other check (it records `PERMISSION_DENIED` with
`devices.manage|admin.users` in Audit → security events); the text "Missing permission …" is the same rule
checked again by the service. Ask to have it assigned to you or have another administrator make the change.

**I tried to assign a user's PIN and get 404 "User not found." even though the user exists.**
The user belongs to another company, is a portal account, or is the platform administrator: none of the three
has a device PIN (the platform administrator never signs in this way, so they are not even visible
here).

**What does "The PIN must have 4 to 6 digits." (400, in `pin`) mean?**
When setting the PIN (`PUT /api/v1/me/pin` or `PUT /api/v1/users/{id}/pin`), use only 4 to 6 numbers. Leading and
trailing spaces do not count.

**What does "The PIN cannot be a trivial sequence." (400, in `pin`) mean?**
PINs with all digits the same or consecutive sequences such as `1234` or `9876` are not allowed. Choose another one.

**What does "The line is repeated in the request." (400, in `lines[i]`) mean?**
In `PUT /api/v1/cycle-counts/{id}/lines/batch` the same line comes twice and nothing is saved. Leave a single
row per line and resend.

**What does "The user's PIN changed at the same time in another session; try again." (409) mean?**
Another session set that user's PIN at the same moment. Repeat the operation.

**I changed (or reset) the PIN and the device signed me out.**
That is expected: changing a PIN that already existed (My account or `PUT /api/v1/users/{id}/pin`), just like removing it, closes
that user's sessions on the company's devices (event `TOKEN_REVOKED` with reason `pin_changed`). That way,
whoever signed in with the old PIN does not keep the session. Sign in again on the device with the new PIN.

**The WMS_LOTSERIAL module was turned off and the device responds "The device is not enrolled or was deactivated." (401) when
renewing the session.**
Without the module nobody can sign in through a device, nor renew a device session that is already open: the refresh responds
401 and the session is revoked. When the module is turned on again, each user signs in again with their PIN.

**The refresh of a device session responds "Invalid refresh token." (401) instead of "The device is not
enrolled or was deactivated."**
They are two different checks. The second message is about the **device** (deactivated, or its company without the
module); the first is about the **user**: every refresh of a device session also requires the user to still be
active, with active membership in this company and with `inventory.view` (the same condition as `device/login`). If the
user's permission was removed, they were deactivated or left the company, the refresh responds 401 `Invalid refresh token.` and
revokes the session (event `TOKEN_REVOKED`); the device itself may still be active. Ask to have the permission or
membership restored and sign in again with the PIN.

**Why does a device login attempt appear in the security log without a user?**
If the requested `userId` is not (and never was) a member of the device's company, the `LOGIN` / `FAILURE` event is recorded without
a user (the requested id goes in the detail as `requestedUserId`) so as not to reveal the name or e-mail of users from
other companies. Such an attempt usually means someone is trying random ids with an enrolled device: check that
device and deactivate it if you do not recognize it.

**I edited a device and received "The record was modified by another user; reload and try again." (409).**
Another administrator edited, deactivated, reactivated or regenerated the code of that device after you opened it.
Reload the device (`GET /api/v1/devices/{publicId}`) and repeat the change with the new `rowVersion`. The heartbeat and the
PIN logins of the device do **not** cause this 409: "last seen", "last user" and "app version" are
saved separately and do not change the `rowVersion`.

**I sent `POST /api/v1/devices/enroll` without `enrollCode` (or `device/login` without `deviceSecret`) and it responded 401, not 400.**
That is expected: a missing enrollment code is an invalid code (401 `The enrollment code is not valid or has
expired.`) and a device without a secret is not authenticated (401 `The device is not enrolled or was deactivated.`). In the
same way, `PUT /api/v1/me/pin` without `currentPassword` responds 400 `The current password is incorrect.` in
`currentPassword`. The generic framework message ("The … field is required.") never appears.

**Two people enrolled the same device at the same time with the same code and one received 401 instead of 409.**
That is expected: the code is single-use, so when two enrollments compete for the same code, whoever loses
the race receives the same 401 `The enrollment code is not valid or has expired.` as an invalid code (never a 409
conflict), with the same security event (`API_CREDENTIAL` / `FAILURE`, `device_enroll`) so as not to distinguish
"code already used" from "invalid code". Whoever won is enrolled successfully. Ask for a new code and enroll it
only once.

**I reactivated (or re-enrolled) a device and a token I had from before no longer works, even though the device is active.**
That is expected: deactivating, reactivating or re-enrolling the device sets a stamp (`SessionsNotBeforeUtc`)
with that moment. Any device session access token issued **before** that stamp is no longer accepted
(compared to the second, with no margin), even if the device is active again. Sign in again on the device with the
PIN to get a new token.

**The device lost signal in the middle of an operation with `Idempotency-Key`. Is it duplicated when retrying?**
No. The operation is not cancelled when the device disconnects: it finishes, its response is saved and the retry with the
same key and the same body receives that response with `Idempotent-Replayed: true` (the same receipt, with no other inventory
entry or other number). If the retry arrives while the first one is still in progress, it responds 409 `The operation with this
key is still being processed.`: wait a few seconds and retry with the same key. Two simultaneous retries with
the same key do not duplicate either: one runs and the others receive the replay or that 409.

**I sync with `modifiedSinceUtc` and the device never receives the deactivations.**
The sync parameter is called `since` (UTC). A parameter with another name is ignored without error and the
response is a full load of what is current, which does not include the rows with `isActive: false`. Use `since` = the
`serverTimeUtc` of the previous pass minus 5 minutes, with `cursor` and `take` (up to 500).

**I deleted a cycle count and its task is still on the device.**
When an open count is deleted its COUNT task moves to `CANCELLED` (with a closing date). In the next delta
sync (`GET /api/v1/sync/warehouse-tasks?since=...`) it arrives with `isActive: false` and the device removes it. If it
keeps appearing, the device is not sending `since` (see the previous question).

**I filter the count by variance (`onlyVariance=true`) and I get all the lines.**
Without `warehouse.count` the count is blind: filtering by variance would reveal which lines do not match what is expected,
so that filter is ignored. The other filters (bin, product, category, pending) do apply.

## Batch 8A-app — Warehouse app (screens): Receive, Putaway, Dispatch, Count, Lookup, Sync

See [chapter 9 — Warehouse app](09-app-almacen.md) for every screen in full (what it does, who can, fields
and validations). This section gathers the questions of daily use.

**I closed the app in the middle of a receipt (or a dispatch, or a count). Is it lost?**
No. As long as it is not confirmed (or explicitly cancelled), the document stays saved on the device. When you open
the app again and sign in with the same or another user, the corresponding screen picks it up as it was left (same
captured lines); it never starts a new one on top. In Count, the list of expected lines is requested again from the
server when reopening (it is not saved locally, only what was already captured), so signal is needed at that moment.

**I tap Putaway (or Dispatch, or Count, or Lookup) and it says "Finish or cancel the receipt in progress before using
this."**
It is the "one document at a time per device" rule: while there is a receipt, a dispatch or a count open, the
other actions stay blocked with this notice until the one in progress is confirmed or cancelled. The button of
the action that remains open (not the others) always picks it up instead of blocking.

**Why do Receive/Dispatch/Count sometimes work without signal and Putaway/Lookup don't?**
Receiving, picking in Dispatch and capturing in Count are documents owned by this device: they are captured without signal and
sent on their own when there is one (outbound queue). Putaway (the task list of the whole warehouse), packing a
dispatch (choosing a consignee) and opening a count (claiming the bin) touch something another device or the web app could
be viewing or taking at the same time, so those calls are always direct and need signal at that moment.

**An operation ended up "failed" in Sync. What do I do?**
It means the server rejected it for something that retrying it as is will not fix (invalid data, a document
that no longer exists, a permission that changed) — never for lack of signal, which leaves it "pending" instead, and it is sent
on its own. Read the row's message: if the problem no longer applies, tap "Retry"; if it no longer corresponds (for example,
that operation was cancelled from the web app), tap "Discard" so it stops being attempted.

**I scan a product in Dispatch and it says "A dispatch carries products from a single owner. Finish or cancel this dispatch to dispatch products from another owner."**
(Corrected on 2026-10-11: it used to say the app only dispatched 3PL client inventory; that is no longer the case.) Dispatch accepts own
inventory and 3PL client inventory, but each dispatch belongs to **a single owner**: the first scanned product sets it (a client, or “Own
inventory”). The notice appears if the product belongs to another client, or to a client when the dispatch is of own inventory (and vice versa).
Finish the dispatch in progress (**Complete dispatch** or **Pack**) or cancel it, and start another one for the other owner.

**In Dispatch, what is the difference between "Complete dispatch" and "Pack"?**
(Updated on 2026-10-11.) **Complete dispatch** is the **manual issue**: it takes the inventory out **without a delivery**, with a required
reason and an optional note, and generates a `DMA-#####` document (it is never packed afterwards); it requires the `warehouse.issue` permission (see
“Manual issue in the app” below). **Pack** creates the order: with own inventory it first asks “Which customer is it for?”
and then the consignee; with a 3PL client's inventory it goes straight to its consignees. Both work without signal: they are saved in
the queue and sent when the signal returns.

**I tap "Cancel count" and get a permission error.**
Cancelling a count (`DELETE /api/v1/cycle-counts/{id}`) requires the full `warehouse.count` permission, not just
`warehouse.count.capture` (which is enough to count blind). Finishing the count with what was already counted keeps
working without `warehouse.count`; to be able to cancel it you need that full permission.

**In Dispatch, "Pack" fails when searching for consignees even though I can pick without problems.**
Picking and packing (the final shipment) require `warehouse.pick`; searching the client's consignees to choose one
requires, separately, `locations.read`. With `warehouse.pick` but without `locations.read` you can pick the whole
dispatch, but "Pack" returns the server's permission error when requesting the list. Ask for the device's role to
have both permissions.

**I look up a product without signal and get "Data from {time} (N min ago, no signal now)" (before "Data from N min ago"), but I had not looked that up before.**
Yes, you had: the app saves the last response for each exact code looked up (per warehouse), even if it
was days ago. If the search was by free text and by product at different moments, they count as two different
lookups (two cache keys), even if the final result looks alike.

## Batch A3 — Warehouse app: Zebra improvements and company formats

Details in [chapter 9](09-app-almacen.md) (§1.1 scanner, §1.2 formats, §9 Sync) and in
`docs/mobile/loteA3-decisiones.md`. None of these messages comes from the server (they have no HTTP code): they belong to the app.

### New messages

**What does "The date is not valid. Write it like this: MM/DD/YYYY" in Receive mean?**
The lot expiration is written in the company's date order (Region & formats; in Puerto Rico month/day/year) and what
was written is not a date that exists (for example `31/01/2027` with month 31, or a two-digit year). "Add" stays disabled until
you correct it or clear it (the expiration is optional). Year-month-day (`2027-01-31`) is also accepted. The format in the example changes
if the company uses another order (`DD/MM/YYYY`, `YYYY-MM-DD`).

**What does "Scanner: no profile" in Sync mean?**
DataWedge (the Zebra scanner program) refused to create the `TeikemAlmacen` profile or the app is using another profile (the
"Details" says which, for example `active: Profile0`). With another profile, the scanner may write each read as keystrokes
(the keyboard appears and it does not advance on its own). Tap **Set up the scanner again** with the app open and check again. If it persists:
in the device's DataWedge app → Profiles, make sure `TeikemAlmacen` exists associated with `com.teikem.almacen` (and that no other profile
is associated with the app), with "Keystroke output" off and "Intent output" on (action `com.teikem.almacen.SCAN`,
broadcast). If not, contact support with the text of the "Details".

**What does "Scanner: not confirmed" mean?**
The app requested the profile from DataWedge but DataWedge has not answered (yet). It does not mean the scanner fails: try scanning.
If it does not advance on its own, tap **Set up the scanner again**, go back to Home and return; if it is still not confirmed, contact support.

**What does "Scanner: not a Zebra (keyboard in use)" mean?**
The device has no DataWedge (a regular phone or an emulator). The app works the same: you type the code (⌨ button) and
tap OK.

**What do the green blocks "Added: … from …" (Dispatch) and "Done: … is now in …" (Putaway) mean?**
They are the confirmation of the read: in Dispatch, the line was already added when scanning the bin; in Putaway, the task was already
completed. They stay until the next read.

### Frequently asked questions

**When scanning, the on-screen keyboard appears, or the code is written in the quantity field, and it does not advance on its own.**
The scanner is delivering the read "as keystrokes" (the DataWedge profile was not applied). See "Scanner: no profile" above.
With the Batch A3 version the scan field no longer opens the keyboard when focused, and the app turns off the profile's
keystroke output.

**How do I type a code by hand if the keyboard no longer appears?**
Tap the **⌨** button next to the field: the keyboard appears. Type the code and tap **OK** (or Enter). Tap ⌨ again to
hide it. The Zebra's physical keyboard writes in the field without opening the on-screen one.

**In the count, a product has no label. Do I have to type the code?**
No: tap the product in "What's expected here" and its code goes into the field; tap **OK**.

**In Dispatch I scanned the bin and the line came in with quantity 1.**
It happened with the Batch A3 version (the quantity came as 1). Since **Batch A5** the quantity comes empty and scanning the bin without a
quantity adds nothing: it shows `Type the quantity first, then scan the bin.` (see the Batch A5 section). If you were left with a line
with 1 from an earlier version, remove it with ✕ and scan the product again.

**I changed the region or the formats in the web app and the app stays the same.**
The app fetches them in every sync with signal: tap **Sync now** (or wait a minute with Home open). If the
device has no signal, it keeps the last ones it received.

**The app's time does not match the device's clock.**
The app shows times in the company's time zone (Region & formats), not the device's. If the device's clock
is in another zone, the difference is expected; if the company's zone is wrong, an administrator fixes it in the web app.

**Numbers come out with a thousands dot (1.250) instead of a comma.**
It is the company's separator (Region & formats). Quantities typed by hand accept a dot or a comma as the decimal,
as before.


## Batch F1 — Frontend: access, menu, Pulse and My account

Messages verified against `web-app/src/kernel/i18n/es.json` (screen validations, in Spanish, client side) and against
`src/Teikem.Infrastructure/Services/AuthService.cs` (server messages the screen shows under the field). Chapter:
`docs/manual/frontend/f1-nucleo-y-mi-cuenta.md`.

**What does "Enter a valid email." mean?**
When signing in, what you typed in "Email" does not look like an e-mail address. It is a screen
validation (it never calls the server); correct the e-mail.

**What does "Enter your password." mean?**
You left the "Password" field empty when signing in. Type your password.

**What does "Enter your current password." mean?**
In "My account › Password" you left "Current password" empty. Type it before saving the new one.

**What does "The new password must be at least 12 characters long." mean?**
The "New password" in "My account › Password" does not reach the minimum the platform requires (12 characters).
Choose a longer one.

**What does "Passwords do not match." mean?**
In "My account › Password", what you typed in "Repeat the new password" is not the same as "New password".
Type them again so they match.

**What does “The current password is incorrect.” under "New password" when changing my password mean?**
That the "Current password" you typed is not the right one. (Until Batch 120 this message came out in English, “Incorrect password.”; now every password message comes out in the person's language.) The message stays under the "New password" field because the service groups any password rejection there, whether because of the current one or the policy for the new one. Check that you typed your current password correctly and try again.

**What does "Enter the 6 digits shown by your app." mean?**
When confirming the activation of two-step verification (My account › Two-step verification, or when enrolling
during login), the code does not have 6 digits. Type the full code exactly as your app shows it.

**What does "Choose a range." mean? (Pulse, "My date range")**
In the "Range" dialog of a Pulse card you did not select any mode. Choose one from the list.

**What does "Enter the From date." / "Enter the To date." mean?**
In "My date range" you chose the custom mode and left one of the two dates empty. Fill in both.

**What does "From cannot be later than To." mean?**
In the custom range of a Pulse card, the "From" date is after "To". Correct the order of
the dates.

**What does "Your user is not allowed to see this screen. Ask your company administrator to grant it." mean?**
You tried to enter a screen (by direct URL or a link) for which you lack the permission it requires. Ask an
administrator of your company to grant it, if it corresponds to your role.

**What does "This module is not enabled for your company." mean?**
The screen you entered belongs to a module your company has turned off. Ask an administrator to
enable it in Administration › Modules (outside Batch F1).

**What does "Your user is not allowed to see the indicators and charts of the Day pulse..." mean?**
You lack the `analytics.view` permission; the home screen (Day pulse) looks the same, but without indicators or
charts. Ask an administrator to grant you the permission.

**What does "The indicators and charts of the Day pulse are part of the Analytics module, which is not active..." mean?**
The `ANALYTICS` module is turned off in your company. Ask an administrator to enable it.

**What does "Could not copy; select it and copy it manually." mean?**
When copying the MFA key or the recovery codes, the browser did not allow using the clipboard
(for example, because of browser permissions). Select the text by hand and copy it with your keyboard.

**What does "Could not reach the server. Check your connection and try again." mean?**
The screen could not communicate with the API (no network, or the server is not responding). Check your Internet
connection and try again; if it persists, contact support.

**What does "Something went wrong. Please try again." mean?**
It is the generic message when the server responded with an error the screen cannot describe better
(for example, an unexpected internal error, 500). Try again; if it persists, tell support the time
it happened.

## Batch F6 — Frontend: warehouse and inventory (minimum) + order lookup

Messages verified against `web-app/src/kernel/i18n/es.json` (sections `warehouse.*` and `orders.*`, screen
validations on the client) and against the code in `web-app/src/features/warehouse/` and `web-app/src/features/orders/`.
Chapter: `docs/manual/frontend/f6-almacen-e-inventario.md`. The messages **sent by the server** (409/422 of business
rules) are already documented in more detail in "Batch 6 — Inventory and warehouse" above; here only those
added by the screen (validation before calling the API) or shown by the screen literally under a field are listed.

### Warehouses, zones, bins and docks

**What does "Code is required." mean?**
You left the "Code" field empty when creating a warehouse, a zone or a dock. Type a code.

**What does "Code only allows letters, numbers, hyphen and underscore (max 30)." mean?**
A warehouse code does not accept spaces or other symbols. Use only `A-Z`, `0-9`, `-` or `_`, up to 30 characters.

**What does "Name is required." mean?**
You left "Name" empty when creating or editing a warehouse or a zone. Type a name.

**What does "Enter the bin's code or its aisle/rack/level/position." mean?**
When creating a bin you must type its code directly, or at least one of "Aisle", "Rack", "Level"
or "Position" so the system builds the code.

**What does "Max weight must be greater than zero." / "...allows up to 3 decimals." mean?**
The "Max weight (kg)" of a bin, if you enter it, must be a positive number with up to 3 decimals.

### Products and categories

**What does "SKU is required." mean?**
You left the "SKU" field empty when creating a product. It is the item's identifier; type it before
saving.

**What does "SKU cannot exceed 60 characters." / "SKU cannot contain spaces or control characters." mean?**
The SKU has a limit of 60 characters and does not accept spaces or invisible characters (tabs, line
breaks). Remove the spaces or shorten it.

**What does "Product name is required." / "...cannot exceed 200 characters." mean?**
The product's "Name" field is required and has a maximum of 200 characters.

**What does "Select the tracking type." mean?**
The product needs to say whether it is tracked "No tracking", "By lot" or "By serial"; choose an option.

**What does "Cost and price cannot be negative." / "Cost admits at most 4 decimals." /
"Price admits at most 4 decimals." / "Cost or price exceeds the maximum allowed." mean?**
The product's "Cost" and "Price" (or the unit cost of a purchase order line) must be zero or positive,
with up to 4 decimals, and must not exceed the cap the system allows.

**What does "Minimums cannot be negative." mean?**
"Minimum inventory", "Minimum pick quantity" or "Maximum pick quantity" do not accept negative values.

**What does "The pick bin maximum must be greater than or equal to the minimum." mean?**
"Maximum pick quantity" must be equal to or greater than "Minimum pick quantity".

**What does "A minimum pick quantity requires a preferred bin in a PICKING zone." mean?**
If you enter "Minimum pick quantity" you must also choose a "Preferred bin" that is in a zone of type
PICKING; without it, the system would not know where to replenish.

**What does "Weight and volume cannot be negative." / "Weight admits at most 3 decimals..." /
"Volume admits at most 4 decimals..." mean?**
The product's "Weight (kg)" and "Volume (m³)" do not accept negatives and have a limit of decimals and magnitude.

**What does "Category name is required." / "...cannot exceed 150 characters." mean?**
The name of a product category is required and has a maximum of 150 characters.

### Inventory: balances, movement ledger, adjustments, transfers and reconciliation

> Since Batch 14 the screen is called **Movement ledger** (`/warehouse/kardex`), adjustments and transfers have their own screen
> **Transfers & adjustments**, the adjustment is captured with **Increase/Decrease** and a positive quantity, and reconciliation saves discrepancies. The
> new messages and the questions are in the “Batch 14” section further down. "The adjustment quantity cannot be zero." and "Enter the
> adjustment quantity (a number, positive to add or negative to subtract)." no longer appear in the adjustment modal: now it shows "Choose whether the
> adjustment increases or decreases stock.", "Enter the adjustment quantity." and "The quantity must be greater than zero.".

**What does "The 'from' date cannot be later than the 'to' date." mean?**
In the range filter of the movement ledger, the "From" date ended up after "To". Correct the order of the dates.

**What does "Select a product." / "Select a warehouse." / "Select a bin." / "Select
a reason." mean?**
They are required fields of the "Adjust" form (or of "Transfer", for the source and destination warehouse/bin):
one was not chosen.

**What does "The adjustment quantity cannot be zero." mean?**
An inventory adjustment has to add or subtract something; a quantity of 0 would have no effect.

**What does "The quantity allows at most 3 decimals." mean?**
It applies to the quantity of an adjustment, a transfer, a purchase order line, a receipt, a count or a
pick batch: at most 3 decimals.

**What does "Enter the lot number." / "Enter at least one serial number." mean?**
When adjusting (or transferring) a product tracked by lot or by serial, you must capture the lot or at least one
serial: the system cannot post the movement without knowing which lot/serial it affects.

**What does "The source and destination cannot be the same bin." mean?**
In "Transfer", the source bin and the destination bin must be different.

### Receiving (receipts and arrival notices)

> Since Batch 13 the screen is a master-detail with tabs (Receipts, Arrival notices, Pending putaway). The new messages and
> the usage questions are in the “Batch 13” section further down.

**What does "Choose the warehouse." / "Choose the advance shipping notice." / "Choose the purchase order." / "Enter the
product." mean?**
Required fields when creating a receipt or a line: depending on what you chose in "Receive" (Blind, Return, against
Arrival notice or against Purchase order), the screen requires the corresponding data.

**What does "The receipt allows at most 200 lines."? / "An advance shipping notice allows at most 200
lines." mean?**
It is the cap of lines per document in this batch; if you need more, split the receiving into several receipts or notices.

**What does "Enter the client that owns the goods of the notice." mean?**
An arrival notice (ASN) always belongs to a 3PL client; you must choose it before saving.

**What does "Product {sku} is lot-tracked: enter the lot." / "...is serial-tracked: enter {qty}
serial number(s) (there are {n})." mean?**
When capturing a receipt line (or a count/pick line) of a product with lot or serial tracking, you
must give the lot, or exactly as many serials as the quantity received/counted/picked.

**What does "The serial number '{serial}' is repeated." mean?**
You typed the same serial number more than once in the same line; each serial must appear only once.

### Warehouse tasks

> Since Batch 13 there is no single queue: putaway is in Receiving › Pending putaway and the actions of each row are icons (see “Batch 13”).

**What does "Your user cannot look up the list of users." mean?**
When trying to **Assign** a task, your account has no permission to see the company's user list; ask
an administrator to grant it or to assign the task for you.

**What does "Empty uses the suggested bin." / "Empty completes the task's full quantity; the remainder
becomes a new task." mean?**
They are hints of the "Complete task" form: they are not required: leaving them blank uses the default value the
text describes.

**What does "There is no suggested bin for this task." mean?**
The system did not calculate a recommended bin for that task (normal outside Putaway); choose the destination bin
by hand.

### Cycle count

> Since Batch 14 the count is worked in two panels, it is confirmed in a single step (**Confirm count & adjust**) and its statuses are
> Pending, Counted, Matched and Variance. The **Finish count** and **Reconcile** buttons and the **Count tasks** tab no longer exist
> in the web app. See the “Batch 14” section further down.

**What does "The count allows at most 1000 lines; narrow the filters." mean?**
A cycle count with no zones or bins takes the whole on-hand balance of the warehouse; if it exceeds 1000 lines, you must
narrow it by choosing specific zones or bins before creating it.

**What does "Enter the bin." / "Enter the counted quantity." mean?**
When adding or capturing a count line by hand, the bin or the counted quantity was missing.

**What does "Give the lot by its id or by its number, not both." mean?**
When adding a count line with a lot, you must choose an existing lot **or** type a new number, not
both at once.

**What does "{n} line(s) still to count." mean?**
(Since Batch 14 the button is called **Confirm count & adjust**, and it appears as the reason under the disabled button.) "Finish count" required every line to already have a capture (quantity or serials); check which ones are missing in the
table (those that say "Pending").

### Picking and packing

> Since Batch 13 the capture is in the “Picking” panel of the same screen (multi-line grid) and Pack and Delete are row icons (see “Batch 13”).

**What does "Enter at least one line to pick." / "A pick batch allows at most 100 lines." mean?**
When creating a pick batch you need at least one line, and at most 100.

**What does "A pick batch can only contain products of a single owner." mean?**
You cannot mix own products with a client's products in the same pick batch, nor products of two different
clients. Create separate pick batches per owner.

**What does "Product {sku} is serial-tracked: scan the serials to pick." / "For serial-tracked products the
quantity must equal the number of scanned serials." mean?**
A product tracked by serial requires capturing as many serials as the quantity being picked.

**What does "Enter the shipment client." / "The consignee is required: choose one from the directory or
enter a new one." mean?**
When you **Pack** a pick batch, you need to choose the client that will own the order and a consignee (from the
directory or a new one).

**What does "The consignee name is required." / "The consignee address (line 1) is
required." / "The consignee city is required." mean?**
If you choose a "New" consignee when packing, those three fields are required.

**What does "Use the 2-letter ISO country code." mean?**
The "Country" field of the new consignee expects the 2-letter code (for example, `PR`, `US`), not the full
name.

**What does "Enter at least one package line." / "The number of pieces must be at least 1." / "The
weight cannot be negative." mean?**
When packing, you need at least one package, with at least 1 piece and a weight that is not negative.

### Suppliers

**What does "The supplier name is required." / "An active supplier with that name already exists." mean?**
A supplier's name is required and unique among the company's active suppliers.

**What does "The email address is not valid." mean?**
The supplier's "Email" field does not look like an e-mail address; correct it or leave it empty.

### Purchase orders

**What does "Enter the supplier." / "Enter the product." mean?**
Required fields when creating a purchase order or one of its lines.

**What does "The ordered quantity must be greater than zero." / "The unit cost cannot be negative." mean?**
Each purchase order line needs a positive quantity; the unit cost, if you enter it, cannot be
negative.

**What does "That product is already in the purchase order." mean?**
The same product cannot be repeated in two lines of the same purchase order; edit the existing line instead
of adding another.

**What does "The purchase order must have at least one line." / "...allows at most 200 lines." mean?**
Line limits per purchase order: at least one, at most 200.

**What does "The quantity cannot exceed the pending shortage." mean?**
When resolving a shortage with "Manual adjustment", the quantity cannot be greater than what is really left to receive
on that line ("Pending" column of the shortages table).

**What does "Enter the bin where the goods will be put away." mean?**
When resolving a shortage with "Manual adjustment", you must say in which bin the inventory enters.

### Dock appointments and cross-dock

**What does "Enter the dock." / "Enter the scheduled start." / "Enter the appointment direction (inbound
or outbound)." mean?**
Required fields when scheduling a dock appointment.

**What does "An advance ship notice appointment must be inbound (INBOUND)." mean?**
If you link the appointment to an arrival notice, the direction has to be "Inbound"; an arrival notice is not associated with
an outbound appointment.

**What does "An appointment links to an arrival notice or to a trip, never both." mean?**
The "Linked to" field of an appointment accepts none, an arrival notice or a trip, but not both at once.

**What does "Choose a shipment." / "No exact matches." mean?**
When you **Assign** a receipt line to an order in a cross-dock plan, you must type the full number
of the order, invoice or pack batch (the search is by exact match) and choose it from the list.

**What does "Searching shipments requires the orders.view permission and the LTL_GROUND module." mean?**
Without that permission and that module active, the screen cannot search orders to assign them in the cross-dock
plan; ask an administrator to grant it or to turn on the module, if it corresponds to your role.

### Order lookup

**What does "The shipment does not exist or does not belong to your company." mean?**
The link to the order record is wrong, the order was deleted or belongs to another company (tenant). Go back to
the "Orders" list and search for it again.

## Batch F8a — Day pulse: panels, permissions and order

Details in chapter [07 — Day pulse](07-pulso-y-actividad.md), section 3.

**Why don't I see the Warehouse panel?**
The Warehouse panel requires, all at once: the `pulse.warehouse` permission, the `inventory.view` data permission and the
`WMS_LOTSERIAL` module enabled for the company. If any of the three is missing, the server does not send it and the screen does not
draw it. It may also be **hidden** in your Pulse (or in the company's): go to "Organize my Pulse" and press "Show"
on the panel. Ask an administrator for the missing permission (System → Roles & users).

**Why doesn't my Pulse look like another user's?**
For three possible reasons: (1) each panel and each indicator or chart depends on each person's permissions (for example,
an indicator about orders is only seen by whoever has `orders.view`); (2) one of the two has a **Personal Pulse** (the chip
"Personal Pulse · Back to the company's" shows it), with its own order and hidden items; (3) the indicator or chart is private
or shared only with some people.

**How do I go back to the company's Pulse?**
With "Back to the company's" (`DELETE /api/v1/analytics/pulse/layout/mine`): your order and your hidden items are deleted (panels,
indicators and charts, including your own "Show in Pulse" switch) and you see the company's order again. Your
own date ranges are kept.

**I marked an indicator for the Pulse and a user does not see it.**
"Show in Pulse" (of the indicator or of the company's Pulse) is not enough: the user also needs `pulse.indicators`
(`pulse.charts` for charts), to be able to see the indicator by its visibility (company-wide, or shared with them), to be able to

**read its data source** (e.g. `orders.view` for `TRANSPORT_ORDER`, `inventory.view` for the warehouse ones,
`admin.audit` for the log) and the business module must be enabled (Warehouse → `WMS_LOTSERIAL`, Operations →
`LTL_GROUND`, Accounting → `COD` or `LTL_GROUND`). Also check that the user has not hidden it in their Personal Pulse.

**What does "Invalid scope: use mine or company." mean? (400)**
When saving the Pulse order, the `scope` parameter must be `mine` (my Pulse) or `company` (the company's). The
screen sends it on its own; if this error arrives from an integration, correct the call.

**What does "Unknown Pulse panel: RADAR." mean? (400)**
The panel key is not in the Pulse registry (today: `INDICATORS`, `CHARTS`, `WAREHOUSE`, `ACTIVITY`). It usually happens with
a screen version newer than the server; refresh the page or wait for the server update.

**What does "Invalid type: use indicator or chart." mean? (400)**
Each element of the order must be `indicator` (indicator) or `chart` (chart).

**What does "Missing permission 'pulse.organize_company'." mean? (403)**
You tried to organize the **company's** Pulse without that permission. Organize your own ("Organize my Pulse", no permission required) or
ask an administrator for the permission.

**What does "Pulse panel 'WAREHOUSE' not found." or "Indicator '12' not found." when saving the order mean? (404)**
You tried to order a panel or an element you cannot see (you lack the permission, the data permission, the module or the read access to the
source, or the element was deleted). Reload the Pulse: only those you can see appear. Nothing of the request is saved.

**What does "Data source 'TRANSPORT_ORDER' not found." when creating an indicator or chart mean? (404)**
You cannot read that data source (in the example you lack `orders.view`), so you cannot create indicators or
charts on it either. Choose another source or ask for the permission.

## Batch F8a — frontend: menu, System and Analytics

Details in chapter [F8a — Full menu, branding, Pulse by panels, System and Analytics](frontend/f8a-menu-sistema-analisis-y-marca.md).

**Why don't I see the "Analytics" group (or any other) in the menu?**
A menu group is drawn only if it has at least one item visible to you: check that you have the permission and that the
module of that item is enabled for your company. An item with no function yet (for example, "Dispatch room")
does appear, but opens a screen that says "This screen arrives in a later batch."

**How do I generate a new temporary password for a user who already has an account?**
It cannot be done from "New user" (that screen is only for creation). Ask them to use "Forgot your password?" on the
sign-in page, or change their password from My account if they have access to the account.

**I created a user without a password and closed the modal before copying the temporary one, how do I recover it?**
You cannot: it is shown only once and is not stored anywhere visible. Edit the user and ask them to use
"Forgot your password?", or assign them a new password if you have that permission.

**Why can't I edit a user's name or status, even though I have `admin.users`?**
Because "This user belongs to more than one company; only the platform administrator can edit their account."
(403): the account's name and status belong to the person, not to a particular company. Their roles and
permissions **in your company** can still be edited the same way; for the rest, ask the platform administrator.

**Why do "Reset PIN"/"Set PIN" ask me to confirm my password (reauthentication) and "Remove PIN" doesn't?**
Assigning or changing another person's PIN is a sensitive action (AAL2); removing it is not (it amounts to blocking their
access, not granting it). If your session was reauthenticated recently for another action, it may not ask you again.

**Why can't the code of a new device be typed by hand anymore?**
Before this batch the screen did not ask for a code, but the server did require it — that inconsistency no longer exists:
the server generates a readable one (`AP-XXXXXX`) automatically. The **enrollment** code (the single-use one used
to enroll the device in Teikem Almacén) is still separate and is shown after creating the device.

**I adjusted the label of a list value and I don't see the change in another screen I already had open.**
The change applies immediately on the server; a screen that already had the data loaded (for example, Pulse
open in another browser tab) updates it on its next query, not retroactively in what it already
drew. Reload it if you need to see it there too.

**"Restore" on a deactivated own value did not seem to do anything, is it fixed?**
Yes: before, the value list always excluded inactive ones even when asked to include them, so "Restore" had
no visible effect. Now a restored own value returns to the active list and is offered again when capturing.

**Why doesn't "Organize the company's" appear in my Pulse?**
It requires the `pulse.organize_company` permission, apart from any other `pulse.*` permission of the panels. "Organize my
Pulse" requires no extra permission: it is always available if you have at least one panel.

**Why are the actions in the System tables now icons and don't say the name?**
It is the standard of the original mockup (an icon matching the action, with its name as a tooltip when hovering the
cursor), not a change in what can be done — each icon does exactly what the text button did before.

**I required MFA for a user, why didn't I activate two-step verification for them myself?**
Requiring MFA does not activate it for that person — each one enrolls their own (scans the QR code with their own
authenticator app). What "Require MFA" does is make their **next** sign-in ask them to enroll
before letting them in; until they do, they cannot sign in.

**A user lost the phone with their two-step verification and got locked out, how do I recover them?**
An administrator with `admin.users` uses "Reset MFA" on their row (users table): it removes their confirmed
verification and their recovery codes. On their next sign-in, if it is still required, they are asked to
enroll a new one from scratch.

**Does "No longer require MFA" remove the person's two-step verification?**
No: it only removes the specific **obligation** you had set for them. If that person already had their own
verification active (on their own, or because the whole company requires it), they keep it.

## Batch 10 — Data migration

**Why is the migration a command-line command and not a screen on the site?**
Because it is a provisioning job (it creates a whole company) that runs once or twice per company, with
files that contain clients' personal data; it makes no sense to expose it as a screen or give it its own
permissions. It writes with the same services as the site, so the result is identical to loading it by hand.

**I ran `import-legacy` twice by mistake, did it duplicate everything?**
No. It is idempotent: the second run finds the company, the categories, the products, the suppliers and the
clients by their natural key (name, SKU, "QuickBooks code") and counts them as "Already existed" instead of creating them
again. The opening balance is not repeated either if there is already an `OPENING_BALANCE` entry into that warehouse.

**I changed a price in QuickBooks after the initial load, do I have to load everything again?**
No. Run `import-legacy <config.json> --update`: it adds what is new and updates the products, suppliers, clients and
consignees that already exist, without touching the opening balance. For that you only need to export Items and
Customers from QuickBooks again.

**Why isn't the opening balance updated with `--update`?**
Because once the company invoices and dispatches in Teikem, the inventory is no longer kept by QuickBooks: it is moved by
Teikem's receipts, dispatches and adjustments. Repeating the opening balance from QuickBooks would overwrite real movements. If
you need to reload stock from scratch, the way is `db-reset` and a full load.

**I want to test the migration from scratch several times, do I have to reinstall the database by hand?**
No: `dotnet run --project src/Teikem.Api -- db-reset --yes` deletes the configured database and initializes it again
exactly like `db-init` on a clean server. It is irreversible, which is why it requires `--yes`; and it only acts on a
local server unless you add `--allow-remote`.

**Can `db-reset` delete the legacy WMS database (MSWM) by mistake?**
No: it expressly refuses any database whose name starts with `MSWM`, with the message *"db-reset does not touch the legacy
WMS database."*, no matter which server the connection string points to.

**The dry-run rejected something with "run db-init before the load", what do I do?**
Run `dotnet run --project src/Teikem.Api -- db-init` on that database (it is additive: it deletes nothing) so the
new lists of Batch 10 are applied (the QuickBooks payment terms and the `OPENING_BALANCE` reason), and run
the dry-run again.

## Batch 11 — Warehouse changes, round 1 (bin capacity, occupancy, zones, bins, locations, cities and export)

Chapters: [06 — Inventory and warehouse, section 1](06-inventario-y-almacen.md#1-warehouses-and-locations) and
[F6 — Warehouse and inventory](frontend/f6-almacen-e-inventario.md). Closure and decisions: `docs/lote11-decisiones.md`.
The messages marked "screen" are shown by the interface before calling the server; the rest are sent by the API (with the
HTTP code indicated).


### New or changed error messages

**What does "The bin's max capacity must be greater than zero." mean? (400, also on screen)**
You typed `0` or a negative number in "Max capacity" when creating or editing a bin. The capacity is the units that fit in
the bin and, if you enter it, it must be a whole number greater than zero. If the bin has no limit, leave the field empty
(empty = no capacity). Through the API the error comes in `errors.maxCapacityQty`.

**What does "Max capacity must be a whole number." mean? (screen)**
You typed decimals or text in "Max capacity". Use a whole number (for example `25`).

**What does "Max capacity is too large." mean? (screen)**
The capacity exceeds 2,147,483,647, the maximum the system allows. Type a realistic quantity or leave the field empty.

**What does "Unknown occupancy status: 'X'. Use EMPTY, PARTIAL, FULL or NO_CAPACITY." mean? (400)**
API only: the bin query (`GET /api/v1/warehouses/{publicId}/bins`) received in `occupancy` a value that does not
exist. The valid values are `EMPTY` (empty), `PARTIAL` (partial), `FULL` (full) and `NO_CAPACITY` (in use with no capacity);
several can be sent by repeating the parameter. The screen never produces it: its filters use those four values.

**What does "The zone cannot be moved to another warehouse." mean? (400)**
API only: the `PATCH` of a zone carried `warehouseId`. A zone's code, name and type can be edited, but the zone
belongs to the warehouse where it was born. If you need the same zone in another warehouse, create it there.

**What does "A zone with that code already exists in the warehouse." mean? (409)**
Another zone of the same warehouse already uses that code. Since Batch 11 it also appears when **editing** a zone's code: on the
screen the message appears under the Code field and the form stays open; choose a different code. The same code can
exist in different warehouses.

**"The zone code cannot be changed." no longer appears, what happened?**
It was removed on purpose: a zone's code can now be edited (Warehouses → click the zone, or Record › Zones). Changing it
does not move the zone's bins or stock. If a manual or an old screenshot mentions it, it no longer applies. What
stays fixed is the zone's warehouse, and the code and zone of a **bin**.

**What does "Code is required." when editing a zone mean? (400 / screen)**
You emptied the zone's Code field. When editing, the code is required just as when creating: leave its current value or type
another (letters, numbers, hyphen and underscore, maximum 30; if it does not comply you get `Code only allows letters, numbers, hyphen and
underscore (max 30).`).

**What does "Zone not found." when listing bins mean? (404)**
The `zoneId` filter of the bin list points to a zone that does not belong to the queried warehouse (or does not exist). Choose a
zone of that warehouse. (`zoneIds`, in plural, does not give this error: a foreign id simply returns no bins.)

**What does "No cities or ZIP codes match." mean? (screen)**
In the "City or ZIP code" selector there is no location in the list matching what you typed. Try with fewer letters,
with the name of the municipality or with the first digits of the ZIP code. The search ignores case and accents.

**What does "Your user cannot view the city list." mean? (screen)**
The server denied the location list query to that session. The list only requires a signed-in session, so the
usual cause is an expired session: sign in again. If it persists, contact support.

**What does "No slot matches the filters." mean? (screen, Locations)**
The combined filters leave no bin (for example, zone A with a zone type that zone A does not have). Remove
some filter or use **Clear**.

**What does "The file could not be generated. Please try again." mean? (screen)**
The generation of the Excel, CSV or PDF failed, or one of the reads of the full query of a server list did.
Try again; if the list is very large, refine the filters. If it persists, contact support with the name of the screen and
the format.

**What does "Only the first {count} rows were exported (export limit). Refine the filters to export the rest." mean? (notice)**
The query has more than 10,000 rows and the file carries only the first 10,000. Filter by warehouse, date or status and
export in parts.

**What does "Only the first {count} bins are listed: choose zones to narrow the list." mean? (screen, New cycle count)**
The warehouse has more bins than the selector can load (10,000). First choose one or several zones and open
the bin list again.

### Frequently asked questions

**Why does the occupancy say "no capacity set"?**
In Locations, a zone's box says "units · no capacity set" when **none** of its active bins
has a "Max capacity": occupancy is calculated against the sum of the capacities and without capacities there is nothing to compare against (which is why
it only shows the units there are). Enter the capacity of the bins (Warehouse record › Bins, or "New
bin"). On a single bin, "In use, no capacity" means the same: it has stock but no capacity.

**Why does a zone's box say "N slots without capacity"?**
Because that zone mixes bins with and without capacity. The percentage only counts those with capacity (sum of capacities against
what is in those same bins); the others are reported separately so they do not distort the number.

**How are "Empty", "Partial" and "Full" calculated on a bin?**
The **on-hand** stock (including what is reserved) is compared with the capacity: with no stock it is Empty; with stock and less than
the capacity, Partial; with stock equal to or greater than the capacity, Full; with stock and no capacity, In use, no capacity. If the
stock exceeds the capacity, the percentage can go over 100%.

**How do I export a table and what does it export?**
Use the **Export** button in the table footer and choose Excel (.xlsx), CSV (.csv) or PDF (.pdf). It exports **all the rows that
match the screen's filters**, not just the page you see, with the visible columns (without the actions column) and the same
text the screen shows; in lists that come from the server it reads the whole result, up to 10,000 rows. The file is
named after the table plus the date (for example `almacenes-2026-09-29.xlsx`) and is generated in your browser.

**In the CSV some texts start with an apostrophe, is it an error?**
No. It is a protection: if a text starts with `=`, `+`, `-` or `@`, Excel could take it as a formula, so the CSV
saves it with an apostrophe in front. Numbers are not touched.

**In the PDF, question marks (?) appear instead of a symbol.**
The PDF uses a standard font that only covers Latin characters: accents and ñ come out fine; other symbols (for example, letters from
other alphabets) are replaced. The Excel and the CSV do keep them.

**Why does my city appear in capitals?**
Because the city comes from the USPS postal list. Outside Puerto Rico the official postal name is stored, which is in
capitals and without accents (`NEW YORK`). In Puerto Rico the **municipality** is stored with its normal spelling (`Toa Baja`), even though
the option list shows the postal name in capitals and the municipality in parentheses (`00952 · SABANA SECA (Toa Baja), PR`).
From the screen the city is chosen from the list, not typed by hand.

**I can't find my city or my ZIP code in the selector.**
The list has 42,522 ZIP codes of the United States, its territories and Puerto Rico. Search by the municipality or by the
first digits of the ZIP code. If it really does not exist, contact support: the list is loaded from the seed and a
direct change in the table can take up to an hour to show.

**Why can't I type the State or the Country of a warehouse?**
Because they are filled in automatically with the city or ZIP code you choose (they appear disabled: "Filled from the chosen
city."). If you choose no location, the country stays as Puerto Rico. A warehouse whose address is not in the list
cannot capture its state from the screen.

**Why can't I change a bin's code or zone?**
It is a system rule: a bin's code and zone stay fixed from creation and the message is `The bin's code and zone
cannot be changed.` (400). You can edit aisle, rack, level, position, capacity and max weight (changing the
parts does **not** recalculate the code). If you need another code or another zone, create a new bin and deactivate the previous one
(only possible if it has no inventory or open tasks).

**Can I change a zone's code? Are its bins lost?**
Yes, and nothing is lost: the bins and the stock stay linked to the zone. Only the new code must differ from
that of the other zones of the warehouse. The bin codes do not change.

**Why is a zone's trash can disabled?**
In "Zones in this warehouse" (Warehouses list) the trash can is disabled when the zone has active bins; the
message when hovering is "Can’t deactivate: this zone has slots created". First deactivate or move its
bins. The server applies the same rule: `The zone has active bins; deactivate them first.` (409).

**Why does the "Code" filter of Bins (or "Position" of Locations) also find by aisle, rack or zone?**
Because that box searches the text within the code, the zone code, the aisle, the rack, the level and the
position (before it only looked at code and zone, and you could not search by what the table calls "location"). To refine, use the
Aisle, Rack, Level and Position filters of the record.

**I sorted by a column and the whole result was not sorted.**
In lists paginated by the server (Bins, Locations, Products, Inventory, Receipts, etc.) the column sort
applies only to the rows of the page you are viewing. To see everything in order, export the table and sort it in Excel, or use
the filters to reduce the result to a single page.

**Why don't I see "Show" (active only / include inactive) in Warehouses anymore?**
The warehouse list now brings all of them and is filtered with **Status** (next to Code, Name, Address and Type). Inactive ones
appear dimmed. To see only the active ones, choose "Active" in the Status filter.

**The already installed warehouse app does not find the bin I scan (Putaway, Dispatch, Count).**
It is expected with the previous version of the app: the server's bin list changed format (it now comes
paginated) and the old app does not understand it. Install the new version of the app. This conclusion comes from comparing the code of
the two versions; it was not tested on a device.

## Batch 12 — Warehouse changes, round 2 (make and model, Products and Purchases filters, PDF reports, Suppliers, Bins, bulk capacity and estimated capacity)

Chapters: [06 — Inventory and warehouse, sections 1.2, 2 and 8](06-inventario-y-almacen.md),
[10 — Data migration, section 4](10-migracion-de-datos.md) and
[F6 — Warehouse and inventory](frontend/f6-almacen-e-inventario.md). Closure and decisions: `docs/lote12-decisiones.md`.
The messages marked "screen" are shown by the interface before calling the server; the rest are sent by the API (with the
HTTP code indicated). The bulk capacity messages were first documented as "Batch 11 (supplement)"; they belong to
this closure.

### New or changed error messages

**What does "Enter the max capacity (maxCapacityQty) or clear: true to remove it." mean? (400)**
You requested bulk capacity (`POST /api/v1/warehouses/{id}/bins/capacity`) without saying what to do. Send `maxCapacityQty` with the
capacity (whole number greater than zero) or `clear: true` to leave the bins without capacity. The error comes in `errors.maxCapacityQty`.

**What does "Enter the max capacity or clear: true, not both." mean? (400)**
The body carries a capacity and also `clear: true`. They are opposites: leave only one. The error comes in `errors.clear`.

**What does "Enter at least one bin filter (zoneIds, aisle, rack, level, position, search or binIds) or allBins: true to apply it to the whole warehouse." mean? (400)**
The bulk capacity carries no bin filter, so it would apply to the **whole** warehouse. To avoid doing it by
accident, it must be confirmed with `allBins: true`; if that was not the idea, add zones, aisle, rack, level, position,
search or bin ids. The error comes in `errors.allBins`.

**What does "No bin of ALM-DEPOT has stock history in the WMS; the bins are left without capacity." mean? (`import-legacy` warning)**
The importer found no historical stock of that warehouse in MSWM (inventory, counts or putaways), so there is
nothing to estimate the capacity from. The load continues normally and the bins are left "without capacity". Check that `sources.mswm.warehouseId`
is the right warehouse; if it is, assign the capacities with bulk capacity.

**What does "Brand cannot exceed 100 characters." mean? (400, also on screen)**
The product's brand has more than 100 characters (not counting leading and trailing spaces). Shorten it. On the screen the
field no longer lets you type more than 100; the message appears mostly when calling the API directly (`errors.brand`).

**What does "Model cannot exceed 100 characters." mean? (400, also on screen)**
The same for the product's model (`errors.model`).

**What does "Warehouse not found." when listing products mean? (404)**
One of the warehouses in the filter (`warehousePublicId` or `warehousePublicIds`) does not exist or belongs to another company. Remove that
warehouse from the filter or choose it again from the list.

**What does "Write a note explaining the adjustment." mean? (400, also on screen)**
The note of the manual adjustment is required. Write why the inventory is adjusted (up to 300 characters; only spaces does not
count). Since the 2026-09-30 adjustment the API also requires it (`POST /api/v1/inventory/adjustments` responds 400 with the
error in `errors.notes`), so it applies to all screens, to the mobile app and to integrations. The adjustments the
system makes on its own (receipt or count variances, pick reversals, migration opening balance) do not ask for it.

**What does "Notes allow at most 300 characters." mean? (400, also on screen)**
The adjustment note has more than 300 characters. Shorten it.

**What does "Enter the adjustment quantity (a number, positive to add or negative to subtract)." mean? (screen)**
In "Add adjustment" the quantity is empty or is not a number. Type a number: positive to add to the inventory, negative
to subtract (it cannot be zero). This text replaces "A number was expected." in adjustments; it is also seen by whoever adjusts
from the Inventory adjustments screen, because they share the rule. (Batch 14: that screen left the menu and the adjustment modal no longer shows this
text; see the “Batch 14” section.)

**What does "Insufficient inventory of {sku} in {bin}: available {x}, requested {y}." inside the adjustment mean? (409)**
Subtracting that quantity would leave the bin's inventory negative. The message appears inside the adjustment block, which
stays open. Correct the quantity (you cannot subtract more than what is available in that bin) or choose another bin.

**What does "The phone must have 10 digits: (###) ###-####." mean? (screen)**
The supplier's phone does not have the digits the company's mask requires (in Puerto Rico, 10; each `#` is a
digit). Type the digits (the field applies the mask on its own; it can start with the country code `+1`) or leave it empty. A
supplier loaded before with another format must be corrected to be able to save. If the company uses another mask (Tenant
settings → Region & formats → Phone), the message states its number of digits and its mask. Before batch F9 it said
"The phone must have 10 digits: (xxx)xxx-xxxx.".

**What does "Choose the warehouse." when creating a purchase order mean? (screen)**
The order modal requires the warehouse even though the API accepts it empty when the company has only one. Choose it from the list.
If the API responds "Enter the warehouse: the company has more than one." (400) it is the same rule for whoever calls without a warehouse.

**What does "Add at least one line with an ordered quantity greater than zero." mean? (screen)**
All the order's lines have the quantity empty or at zero. Capture at least one with a quantity greater than zero, or remove the
extra lines. It applies when creating the order and when saving changes to its lines. The API responds "The purchase order must have at
least one line." or "The ordered quantity must be greater than zero." (400).

**What does "Enter the max capacity." / "Choose at least one filter (zone, aisle, rack, level or position) or tick “Whole warehouse”." in Set capacity mean? (screen)**
In the **Set capacity** modal, the first message appears if you chose "Max capacity" and left the field empty: type the capacity, or choose
"Clear capacity". The second appears in the preview when no filter is set: add a zone, aisle, rack,
level or position, or tick "Whole warehouse" if you really want to change the whole warehouse. While either one is missing, the
apply button is disabled.

**What does "No bin matches the scope: nothing to apply." mean? (screen)**
The filters of the Set capacity modal find no **active** bin (or, with "Only bins without capacity", all of them already
have capacity). Change the filters. Nothing is changed while it is at zero.

**What does "Could not count the affected bins: …" mean? (screen)**
The preview of the Set capacity modal could not query the server (the text that follows is the reason, for example a permission
or a disconnection). The apply button stays disabled; close the modal and open it again, or check your connection and your
`warehouse.manage` permission.

**What does "The report could not be generated. Try again." mean? (screen notice)**
The browser could not read the data or build the PDF of the Inventory report / Adjustment report. Try again; if it persists,
refine the filters (fewer products) and check your connection.

**What do the notices of the PDF reports mean?**
They appear in the notices box on the first page: "Products without stock (0 on hand) not included: N." (the inventory
report does not list products at zero); "Products with stock and no purchase cost: N. Their value shows as «—» and is not
added to the totals." (the purchase cost is missing on those products); "Migration opening balances excluded: N (they are not
operational adjustments)." (the adjustment report does not count the opening balance loaded by the migration); "The «…» view depends
on the product's current state and does not apply to movements…" (an indicator is chosen and it does not filter adjustments); "The report
includes only the first N products / the N most recent adjustments (read limit). Refine the filters to see the rest."
(there are more than 10,000 rows: narrow with the filters). They are not errors: they are data of the report.

### Frequently asked questions

**How do I correct the capacity of many bins at once (or set the same capacity on many)?**
In Bins, or in the Bins tab of the warehouse record, use the **Set capacity** button (requires `warehouse.manage`): choose
the scope, type the capacity or choose "Clear capacity", check "Will apply to N bins" and apply. Through the API it is
`POST /api/v1/warehouses/{id}/bins/capacity` with the same filters as the bin list, for example
`{ "zoneIds": [3], "aisle": "01", "maxCapacityQty": 40 }`. It applies to all that match (not just one page) and responds
how many matched (`matched`) and how many changed (`changed`). To see beforehand how many there will be, request the list with those filters
and `take=1` and look at `total`. It requires the `warehouse.manage` permission.

**How do I fill only the bins that have no capacity, without touching the ones I already captured?**
Add `onlyWithoutCapacity: true`. For example `{ "allBins": true, "onlyWithoutCapacity": true, "maxCapacityQty": 50 }`
fills all the active bins of the warehouse that have no capacity and leaves the rest intact.

**Where did the capacity of my bins come from?**
The migration estimated it from the history of the previous WMS: the largest total each bin ever had (current and
previous inventory, counts and putaways per day) rounded up to the nearest ten. Bins with no history take the median of
their aisle, or failing that of their zone, or failing that of the warehouse; that median is only used if it comes from at least 5 bins with history
(with fewer it moves to the next level). Advance Solutions has no estimated capacity: its bins are left "without capacity". The detail, with the origin of each capacity (`HISTORIAL`, `PASILLO`, `ZONA`,
`ALMACEN`), is in the `-cupos` CSV of the migration report. It is an estimate: correct it with bulk capacity or
by editing the bin.

**Does the migration with `--update` erase the capacities I corrected?**
No. `--update` only fills the capacity of the bins that do **not** have one; a bin with capacity (captured by hand or from a
previous load) always keeps it and the CSV says so: "Current capacity kept (N)".

**Why doesn't the inventory report list products with 0?** Because it is a snapshot of what there is: only products
with non-zero on-hand stock are included, and a notice says how many were left out. If you also need those at zero, use

**Export** on the table.

**Why doesn't the adjustment report include the migration opening balances?** Because the migration recorded them as
adjustments (reason `OPENING_BALANCE`) but they are not operational adjustments; the report excludes them and says how many there are
("Migration opening balances excluded: N"). They remain visible in the inventory ledger.

**Why does the inventory value show «—»?** Because the product has no **Purchase cost**. The value is Total × purchase
cost; without a cost it is not calculated and is not included in the totals (the report says how many products are like that). Capture the cost on
the product and generate the report again. The system does not store average cost or cost per lot.

**Why can't I change the supplier or the warehouse of an order already created?** Since the 2026-09-30 adjustment **they can
be changed while the order is in Draft**. Once sent they stay fixed (the API responds 409 "The supplier and the
warehouse can only be changed while the purchase order is in draft."). If the order was already sent and you made a mistake, cancel it and
create another.

**Why, with a Warehouse filter in Products, do products without stock keep appearing?** The filter narrows the quantities
of each row to that warehouse, it does not remove products. To keep those with stock use the "Total
units" indicator: since the 2026-09-30 adjustment it shows the **active products with on-hand stock** greater than zero (in the API,
`activeOnly=true&onlyOnHand=true`), which is what its figure adds up.

**Why are the "Status" filter and the search box of the Products table gone?** The indicators above (Active SKUs,
Total units, Below minimum, Serial-tracked) replace the Status filter, and to search by text there are the
Name and SKU filters.

**Where did "Locations" go?** It is called **Bins** since Batch 12 (same place in the menu and same address
`/warehouse/locations`).

**Why is "Serial-tracked" or "Below minimum" in orange?** Because there is something to attend to: products below their minimum or
serial-tracked products whose captured serials are fewer than their stock ("N with incomplete serials"). With nothing pending, the
indicator shows in the normal color. A click on the indicator filters the table to see them.

### Adjustments decided after the closure (2026-09-30)

**What does "The supplier and the warehouse can only be changed while the purchase order is in draft." mean? (409)**
You tried to change the supplier or the warehouse of an order that is no longer in Draft (sent, partially or fully received, or
cancelled). From Sent onward they stay fixed. If they must be corrected, cancel the order and create another with the right data. Sending
the **same** supplier or warehouse the order already has does not give this error (it is not a change).

**What does "The purchase order's number field cannot be changed." mean? (400)**
The `PATCH` of the order carries a field that never changes: `number`, `orderDate`, `currency`, `status` or `statusCode` (the
message says which). Remove it from the body. `supplierId` and `warehousePublicId` no longer give this error: they can be changed in Draft.

**What does "The supplier is deactivated; it does not accept new purchase orders." / "The warehouse is deactivated; it does not accept new purchase orders." when editing an order mean? (422)**
You chose, for an order in Draft, a supplier or a warehouse that is deactivated. Choose an active one or reactivate the one
you wanted.

**What does "Supplier not found." / "Warehouse not found." when editing an order mean? (404)**
The supplier or warehouse indicated does not exist or belongs to another company. Choose it again from the list.

**Can I change the supplier or the warehouse of an order in Draft?** Yes. In the order record (or through the API, `PATCH
/api/v1/purchase-orders/{publicId}` with `supplierId` and/or `warehousePublicId`) while the order is in Draft. The lines
do not change. It is recorded in the order's audit.

**Why does a manual adjustment ask for a note and the count's or the receipt's doesn't?** Because the note explains an adjustment decided by a
person. The adjustments the system makes already say where they come from (the count, the receipt, the deleted pick batch or the
migration, with their reference in the ledger) and do not ask for it. Resolving a purchase shortage with "Manual
adjustment" **does** ask for it (it is a manual inventory adjustment): without a note, 400 in `errors.notes`. Closing and Reordering a shortage do not move inventory and
leave it optional.

**Which products does the "Total units" indicator show?** The **active products with on-hand stock greater than zero**,
counting all their bins (also quarantine, cross-dock and what is reserved). Through the API it is `GET /api/v1/products?
activeOnly=true&onlyOnHand=true`; with warehouses in the filter, the stock is measured in those warehouses. It is not the same as
`onlyAvailable` (available to pick: without quarantine or cross-dock and subtracting what is reserved).

**Why doesn't "Company default" sometimes appear when packing?** Because your company has no default service type (or
package type). Then it must be chosen in the form; if it is sent empty, the API responds 400 "The service
type is required." or "The package type is required.". The defaults are configured by whoever administers the
company (`PUT /api/v1/tenant/settings`, `defaultServiceType` and `defaultPackageType`, permission `admin.tenant`) and any
signed-in user reads them in `GET /api/v1/tenant/settings` (code or `null`).

## Batch 13 — Receiving (Batch 3 of the change plan): status cycle, editable header, expected in blind and filters

Chapters: [06 — Inventory and warehouse, sections 4, 5 and 7](06-inventario-y-almacen.md) and
[F6 — Warehouse and inventory (screens)](frontend/f6-almacen-e-inventario.md). API messages carry the HTTP code indicated; those
seen only on screen are grouped further down ("Messages you only see on the screen…"). This batch also covers Picking and
packing (two panels, draggable bar) and the 2026-09-30 adjustments to Batch 12 (required note, "Total units", supplier and
warehouse of the order in Draft, defaults when packing).


### New or changed error messages

**Before, a blind `POST /api/v1/receipts` without lines answered 400 "Enter at least one line."; not anymore?**
Not anymore. A blind or return receipt without `lines` (and without `confirm`) creates **only the header** and stays in **Expected**; the
lines are added later with `POST /api/v1/receipts/{publicId}/lines`. If an integration relied on the 400 to detect an
empty receipt, review it: it now gets 200 with the created receipt. If you created it by mistake, delete it (`DELETE`, 204).

**What does "Enter at least one line." mean? (400)**
It only appears now with `confirm: true` (receipt in a single call, the device queue) on a blind or return receipt without `lines`: an
empty receipt cannot be confirmed. Send the scanned lines or remove `confirm` to create only the header.

**What does "The expected quantity is only captured on blind or return receipts; on one with an arrival notice or a purchase order it comes from the document." mean? (400)**
You sent `expectedQty` (or `clearExpected`) on a receipt against an arrival notice or a purchase order. There, the expected quantity is
the document's and is not touched: capture only what was received. The error comes in `errors.expectedQty` (line capture),
`errors["line.expectedQty"]` (extra line) or `errors["lines[i].expectedQty"]` (creation with lines).

**What does "The expected quantity cannot be negative." mean? (400)**
The expected quantity of a line on a blind or return receipt is less than zero. Use 0 (nothing was expected) or a positive
number; with more than 3 decimals you get "The quantity allows at most 3 decimals." and on serial products it must be a whole number.

**What does "The product of a line from the arrival notice or the purchase order cannot be changed." mean? (400)**
You tried to change the product (`productPublicId`) of a line that comes from the document. If something else arrived, capture 0 on that
line and add an extra line with the product that arrived.

**What does "The line has cross-dock allocations; cancel them before changing the product." mean? (409)**
The line is already allocated to an order in a cross-dock plan. Cancel those allocations in the plan and change the
product again.

**What does "The type of a receipt with an arrival notice or a purchase order cannot be changed." mean? (400)**
The `PATCH` brings a different `type` on a receipt with a document (its type is ASN). Only receipts without a document switch between
blind (`BLIND`) and return (`RETURN`).

**What does "The warehouse can only be changed on a receipt without an arrival notice or purchase order and without lines." mean? (400)**
The warehouse of a receipt with a document is set by the document, and one with lines already has goods captured in its
bins. If you picked the wrong warehouse, delete the lines (or the receipt) and start again in the right warehouse.

**What does "The carrier allows at most 80 characters." mean? (400)** — and **"The reference allows at most 80 characters."? (400)**
The carrier or the reference of the header (creation or `PATCH`) exceed 80 characters (not counting leading and trailing
spaces). Shorten them. To clear them send `""`.

**What does "Receipt REC-… was already confirmed; it cannot be modified." mean? (422)**
Besides the lines, the header `PATCH` now answers it too: a Completed, Completed with variance or
Put away receipt is already in the ledger and is frozen.

**What does "The record was modified by another user; reload and try again." mean? (409, on the receipt `PATCH`)**
The `rowVersion` you sent is from an old read: someone (or you yourself, when saving a line) changed the receipt afterwards.
Read the record again and send the new `rowVersion`.

**What does "Dock not found." mean? (404)**
The dock in the creation or the `PATCH` does not belong to the receipt's warehouse (or does not exist). Choose a dock of that warehouse.

**What does "Unknown variance: 'X'. Use SHORT, OVER or NONE." mean? (400)**
The `variance` filter of `GET /api/v1/receipts` brings a value that does not exist. Use `SHORT` (short), `OVER` (over) or
`NONE` (no variance); the parameter can be repeated to combine them.

**What does "Unknown phase: 'X'. Use OPEN, PENDING_PUTAWAY or DONE." mean? (400)**
The `phase` filter of `GET /api/v1/receipts` brings a value that does not exist. Use `OPEN` (open), `PENDING_PUTAWAY`
(confirmed with pending putaway) or `DONE` (put away).

**What does "The receipt has no lines; add at least one before confirming." mean? (422)**
You tried to confirm a receipt without lines. It is now possible to have one: a blind or return receipt is born with only the header (Expected), and
a receipt whose lines were all deleted stays in Receiving with 0 lines. Add at least one line and confirm; if the
receipt was created by mistake, delete it. On the screen, the **Confirm receipt** button is not even enabled and says "Add at least one
line to confirm.".

**What does "Write a note explaining the adjustment." mean? (400)**
See the Batch 12 question. Since 2026-09-30 it applies to **every manual adjustment**: the product modal, the Adjust button of the ledger,
`POST /api/v1/inventory/adjustments` and also **Resolve** a purchase shortage with "Manual adjustment". Write why the inventory is
being adjusted (up to 300 characters). Close and Reorder a shortage do not move inventory and leave the note optional.

**What does "The service type is required." / "The package type is required." mean? (400) — and on the screen "Choose the service type." / "Choose the package type."**
When you **Pack** a pick batch, your company has no default service type (or package type), so it has to be chosen. If the
default exists, the screen offers "Company default ({label})" and does not ask for it. Whoever administers the company can
configure them (Company settings).

**What do "Bin not found." (404), "The receiving bin must be in a STAGING or CROSSDOCK zone." (400) and "The receiving bin is deactivated." (422) mean?**
The receiving bin you chose on the receipt header (or on a line) does not belong to the receipt's warehouse, is not in a
STAGING or CROSSDOCK zone, or is deactivated. Choose another one, or leave it empty so it uses the first bin of a STAGING zone. If you changed the
warehouse of a receipt, the previous bin is cleared on its own.

**What does "Warehouse not found." mean when editing a receipt header? (404)**
The given warehouse does not exist or belongs to another company. Choose it again from the list.

**What does "Receipt line not found." / "Product not found." mean when capturing a line? (404)**
The line does not belong to that receipt (for example, someone else removed it) or the chosen product does not exist or belongs to another company. Reload
the receipt and choose the product again.

**What does "Invalid rowVersion: the base64 value returned by the record is expected." mean? (400)**
An integration sent `rowVersion` with a value that is not the one returned by the receipt record. Use exactly the `rowVersion` of the
last read or do not send it.

**What does "The arrival notice has an open receipt; delete it before cancelling." mean? (409)**
The notice already has a receipt (Expected, Receiving or Discrepancy). Delete it from Receiving and then cancel the notice.

**Messages you only see on the Receiving screen**

**What does "Add at least one line to confirm." mean?**
It is under the disabled **Confirm receipt** button: the receipt has no saved lines. Capture a line (product and received).

**What does "Some lines are not saved: leave the field or press Enter to save them." mean?**
You typed something in a row and it has not been saved yet (the row is saved when leaving the field or with Enter). Leave the field or press Enter; if
the button stays disabled, check that the row has a product and a received quantity.

**What does "Fix the marked lines before confirming." mean?**
Some row has an error (the message is under its field, for example "Enter the received quantity." or a server error). Fix it
and the button is enabled.

**What does "Saving lines…" under the Confirm receipt button mean?**
A row is being saved; wait a moment and the button is enabled.

**What does "The receipt is already confirmed." / "The receipt is already confirmed: its header cannot be changed." mean?**
The receipt is Completed, Completed with variance or Put away: it is already in inventory and is neither modified nor confirmed again. The
header opens read-only. If something went wrong, fix it with an inventory adjustment.

**What does "Enter a number." mean?**
A quantity in the lines table has text that is not a number. Use digits, with a comma or a period for decimals.

**What does "With an advance ship notice or a purchase order, the origin, the warehouse and the expected quantities come from the document." / "The warehouse can only be changed on a receipt without a document and without lines." mean?**
They are notices in the header modal: on a receipt with a document the origin and the warehouse are not changed, and on a blind or return receipt the
warehouse is only changed while it has no lines. If you picked the wrong warehouse, delete the lines (or the receipt) and start again.

**What does "Choose the warehouse first." / "Your user cannot view purchase orders." mean?**
When creating a receipt against a notice or a purchase order, the list depends on the warehouse: choose it first. If in addition your user has no
permission to view purchase orders (`purchasing.view`) or the Purchases module is off, you cannot choose one; ask for the permission or
receive against a notice or blind.

**What does "No receipts match these filters." / "Select a receipt from the list" / "This receipt has no put-away tasks." mean?**
They are empty-state notices: the filters left no receipt, no receipt is chosen on the right, or the receipt chosen in
Pending putaway has no tasks. Remove filters or choose another receipt.

**What does "Receipt not found." mean on the screen?**
The link (`?receipt=…`) points to a receipt that no longer exists (it was deleted) or that belongs to another company. Choose one from the list.

**Messages you only see in Picking & packing**

**What does "Up to 100 lines per pick batch." mean?**
The grid reached the limit of 100 lines and **Add line** was disabled. Pick what was captured and make another pick batch with the rest.

**What does "Choose the warehouse first." mean in Picking?**
The product search box depends on the pick batch's warehouse. Choose the warehouse in the Picking panel and then the products.

**What does "Type to see shipments" / "No shipment with that number." / "Shipments could not be loaded." mean?**
It is the **Shipment no.** filter of the list: as you type it offers existing order numbers. "No shipment with that number." means there are no
matches; "Shipments could not be loaded." means the list of suggestions could not be requested (for example, for lack of
permission or connection). You can type the number and apply the filter anyway.

**What does "Pick batch not found." mean?**
The detail was requested for a pick batch that no longer exists or belongs to another company. Close the detail and choose another one from the list.

### Frequently asked questions

**What happened to the receipt's `OPEN` status?** It was retired. Now an open receipt is in **Expected** (header only),

**Receiving** (everything matches) or **Discrepancy** (some line differs); confirmed, in **Completed** or **Completed with
variance**; and when the putaway is closed, in **Put away**. When the database was updated, the receipts that were in `OPEN` moved to
Receiving (with lines) or Expected (without lines) with the note "Lote 13: nuevo ciclo de estatus del recibo." in their history.
A `status=OPEN` filter no longer returns anything: use `phase=OPEN`.

**Why did my receipt move on its own from Receiving to Discrepancy?** Because when a line was saved, the received quantity ended up different from the
expected one. When you correct it so that it matches, it goes back to Receiving on its own. Each step is kept in the history.

**I deleted all the lines and the receipt did not go back to Expected.** It is on purpose: a receipt never goes back to Expected. It stays in
Receiving with 0 lines; "Confirm" answers 422 "The receipt has no lines; add at least one before confirming."

**On a blind receipt I captured the expected quantity and confirmed with a variance: was an adjustment made?** No. On blind and return receipts **what was received** enters the
ledger (a single receiving movement) and the receipt stays "Completed with variance" so that it shows.
Only on receipts with a notice or purchase order is the variance posted as an adjustment (`RECEIPT_VARIANCE`).

**How do I see the receipts still to be put away?** `GET /api/v1/receipts?phase=PENDING_PUTAWAY` (Completed and Completed with
variance); each row brings `pendingPutawayCount` with its open putaway tasks. When the last one is closed they move to Put away.

**The company turned off the Discrepancy status (or Completed with variance): what happens?** The receipt skips that status: a
variance leaves the receipt in Receiving and confirmation takes it to Completed. If you turn off Receiving, the receipt stays in
Expected until it is confirmed.

**Can I search notices by reference or by arrival date?** Yes: `GET /api/v1/asns?reference=…&expectedFrom=…&expectedTo=…`
(reference is case-insensitive; dates inclusive; a notice without a date does not fall in a range).

**Why does my receipt say "Discrepancy"?** Because some line has a received quantity different from the expected one. Against an arrival notice or
a purchase order, the expected quantity is the document's; on a blind or return receipt, it is what you wrote in **Expected** (if
you wrote nothing, there is no variance). It is only a signal: it blocks nothing. If it was a capture error, correct the quantity and the receipt
goes back to "Receiving" on its own; if the variance is real, confirm and it will stay "Completed with variance".

**I received less than expected: what enters inventory?** Always **what was received**. With a notice or purchase order, the ledger records the
receipt for the expected quantity and an adjustment (`RECEIPT_VARIANCE`) for the variance, so the net is what was received, and the order's shortage
is resolved later in Purchases. On a blind or return receipt what was received enters directly, with no adjustment; the variance is only marked
in the "Completed with variance" status.

**How do I edit a receipt header (carrier, reference, bin, dock)?** Double-click the receipt in the list or use the
pencil in the detail. It can be done while the receipt is open (Expected, Receiving or Discrepancy); once confirmed, it opens read-only.
The type (Blind ↔ Return) only changes if it has no notice or purchase order, and the warehouse only if in addition it has no lines.

**Why is there no "Add item" on a receipt against a purchase order or a notice?** Because there the expected quantity is the document's and is not
touched from the web: only what was received is captured (0 if it did not arrive). A product that arrived and was not in the document is recorded through the
warehouse app or through the API (`POST /api/v1/receipts/{publicId}/lines`), as an extra line.

**On a blind receipt, the expected quantity was copied on its own and then it stopped doing so. Why?** While you type the received quantity on a new row, the
screen copies the value to **Expected** so that the variance is 0. Once the row is saved, the expected quantity keeps that value and changing the
received one does not move it: the variance shows. If you want them to match again, change the expected quantity too.

**Why does my new receipt appear first even though I have filters that would exclude it?** Because when you create it the screen leaves it first and
selected so that you capture the lines. When you change any filter or the search box, the list goes back to showing only what matches.

**How do I find the receipts with a shortage?** In Receiving, filter **Variance → Short** (or **Over**, **No variance**). For the
ones that can still be corrected, add the Status filter **Discrepancy**.

**Where did the putaway queue go?** To **Receiving › Pending putaway**: a list of the Completed or Completed with
variance receipts that still have tasks, and on the right the tasks of the chosen receipt (assign, start, complete, cancel, as icons). The
old address `/warehouse/tasks` leads there. Replenishment tasks are in Picking & packing › Replenishment.

**Why is the "Confirm receipt" button disabled?** The reason is stated below it: it is already confirmed, it has no lines, there are lines being saved or
unsaved, or some row has an error. See the messages above.

**How do I put the Picking & packing bar back to 60/40?** Focus it (Tab) and press **Enter**, or **double-click** the bar. With the
keyboard, ← and → move it 5 % at a time, and Home and End take it to the ends. The position is remembered in your browser.

**I do not see the bar: the panels are one below the other.** It is on purpose: with the window at 900 px or less (phone), or when the
minimum width of the two panels does not fit, they stack (Picking first and the list below) and there is no bar. When you widen the window it comes back.

**I do not see the "Picking" panel, only the list.** Your user does not have the `warehouse.pick` permission; without it the list takes the whole width and
you cannot pick, pack or delete.

**I opened the detail of a pick batch and I had captured lines: did I lose them?** No. The detail opens in a modal over the screen and
the Picking panel keeps what was captured.

**Where are Pack and Delete?** As icons at the end of each pick batch's row (box and trash can), and also as buttons in the
detail. Pack requires `warehouse.pick` and `orders.create`, and only appears on pick batches that can be packed; Delete requires
`warehouse.pick` (and `orders.cancel` if it is already packed).

**Why does "Total units" only count active products?** Since the 2026-09-30 adjustment the figure adds up the on-hand stock of the **active**
products and, when clicked, the table shows the active ones with on-hand stock greater than zero; this way the figure and the table
match. (Through the API: `GET /api/v1/inventory/balances?activeProductsOnly=true`.)

## Batch 14 — Transfers & adjustments, Cycle count, Ledger, reconciliation and "Needs your attention" (Batch 4 of the change plan)

Chapters: [06 — Inventory and warehouse, sections 3 and 6](06-inventario-y-almacen.md), [07 — Day pulse, section 4](07-pulso-y-actividad.md) and
[F6 — Warehouse and inventory (screens)](frontend/f6-almacen-e-inventario.md). API messages carry the HTTP code indicated; those
seen only on the screen are grouped further down ("Messages you only see on the screen…"). This batch covers the ledger (shared filters,
summary and detail), Transfers & adjustments (Increase/Decrease), automatic reconciliation with discrepancies, Cycle count in two panels with
"what changed" and the Pending / Counted / Matched / Variance statuses, and "Needs your attention" in the Pulse.

### New or changed error messages

**What does "Movement not found." mean? (404)**
The detail of a movement (`GET /api/v1/inventory/transactions/{id}` or the `?txn=` link of the ledger) requested a number that does not exist or
belongs to another company. Go back to the ledger and open the movement from the list.

**What does "Client not found." mean? (404)**
The ledger's owner filter (`ownerClientPublicIds`) brings a client that does not exist or belongs to another company. Choose the owner from the
filter's list (`GET /api/v1/inventory/owners`); "Own" carries no client (`includeOwn=true`).

**What does "The direction must be IN (inbound) or OUT (outbound)." mean? (400)**
The `direction` filter of the ledger (or of its summary) brings another value. Use `IN` for inbound or `OUT` for outbound (case-insensitive),
or remove the filter. The error comes in `errors.direction`.

**What does "Unknown adjustment reason: 'X'." mean in the ledger? (400)**
The `reasons` filter brings a code that is not in the list of adjustment reasons. Use the codes from the list (`DAMAGE`, `LOSS`, `FOUND`,
`EXPIRED`, `PO_SHORTAGE`, `OTHER`, or the ones your company adds). It is the same message as when capturing an adjustment with a nonexistent reason.

**What does "Manual reconciliation allows at most 200 products at a time." mean? (400)**
`POST /api/v1/inventory/reconciliation/run` received more than 200 products in `productPublicIds`. Send fewer, or send none to check

**all** the company's products. The error comes in `errors.productPublicIds`.

**What does "Discrepancy not found." mean? (404)**
The discrepancy does not exist or belongs to another company (no distinction is made). Go back to the Ledger › Reconciliation list: if another user closed it, it is no
longer among the Pending ones; change the status filter to see it.

**What does "Unknown discrepancy kind: 'X'." mean? (400)**
The `kinds` filter of `GET /api/v1/inventory/discrepancies` brings a kind that does not exist. The kinds are `BALANCE` (balance per bin) and
`PRODUCT_TOTAL` (product total).

**What does "Specify the action: REBUILD_BALANCE (fix the balance) or DISMISS (dismiss)." mean? (400)**
`POST …/discrepancies/{publicId}/resolve` arrived without `action` or with a different one. Send `REBUILD_BALANCE` to fix the balance according to the
ledger or `DISMISS` to dismiss it. On the screen, the "Fix the balance from the ledger" and "Dismiss" buttons already send the action.

**What does "Write a note explaining why the discrepancy is dismissed." mean? (400, also on the screen)**
Dismissing requires a note: whoever reads it later must know why it was left that way. Write the reason (up to 500 characters) and press

**Dismiss** again. Fixing the balance does not require it. The error comes in `errors.notes`.

**What does "The note allows at most 500 characters." mean? (400, also on the screen)**
The resolution note of a discrepancy exceeds 500 characters. Shorten it.

**What does "The discrepancy is already closed; it is read-only." mean? (422)**
It was already resolved, dismissed or closed on its own (another person or an automatic check closed it before). A closed discrepancy is not
resolved again. If the problem reappears, the check opens a new one; if you want to check the balance now, press **Run reconciliation**.

**What does "This discrepancy is for the product total; it is not fixed per bin. Fix the per-bin discrepancies or dismiss it with a note." mean? (422)**
The discrepancy is not for a bin but for the **sum of the whole product**: there is no bin whose balance can be fixed. First fix the
per-bin discrepancies of that product (if there are any; a total discrepancy only opens when no bin of the product is
off) or dismiss this one with a note explaining the cause.

**What does "The ledger gives {ledger} for {sku} in {bin}, less than what is reserved ({reserved}); release the reservation before fixing the balance." mean? (409)**
To fix it, the balance would take what the movements add up to, but that number is less than what is already **reserved** in the bin
(for example, for a pick batch or a cross-dock): the balance cannot end up below what is reserved. Release that reservation
(cancel or complete whatever uses it) and try again. Nothing was changed.

**What does "The ledger gives a negative balance ({ledger}) for {sku} in {bin}; review the movements before fixing the balance." mean? (409)**
The sum of the movements of that key is negative: there are more outbound than inbound, which cannot be a balance. Fixing the balance would
copy it from the ledger and would be a mistake. Review the movements of that bin (the discrepancy detail shows the last 20); if the
ledger is wrong, a support review is needed. You can also dismiss the discrepancy with a note.

**What does "The reconciliation collided with another simultaneous check; try again." mean? (409)**
Two checks (for example, the automatic one and your **Run reconciliation** button) tried to open the same discrepancy at the same time; there can
only be one open per key. Nothing bad happened: wait a few seconds and run it again.

**What does "Invalid rowVersion: the base64 value returned by the record is expected." mean? (400)**
The `rowVersion` you sent when resolving a discrepancy or confirming a count is not the text the record returns. Send the value exactly as
you received it, or do not send it (without it there is no control of simultaneous changes).

**What does "The record was modified by another user; reload and try again." mean when resolving a discrepancy or confirming a count? (409)**
The `rowVersion` is from an old read: someone else (or the automatic check, which updates the discrepancy's figures) changed the record
afterwards. Reload the record and repeat the action with the new `rowVersion`.

**What does "The "what changed" range allows at most 31 days." mean? (400)**
In "Count what changed", there are more than 31 days between "from" and "to". Shorten the range. If you did not change the dates and this message appears, the
last generation for this warehouse was more than 31 days ago: the default window narrows itself to 31 days back, so the message only
appears with dates typed by hand. The error comes in `errors.fromUtc`.

**What does "The 'from' date cannot be later than the 'to' date." mean in "what changed"? (400)**
The "Count what changed" window has the "from" after the "to". Fix the order. The same message appears in the ledger and in the
discrepancy list.

**What does "There were no movements in {warehouse} between {from} and {to}; there are no bins to count." mean? (400)**
In that window (Puerto Rico dates and times) the warehouse had no movements in active bins, not counting those that come out of a
count. Widen the window (up to 31 days), remove the zone filter or wait for movement. On the screen, the preview says so
before creating and the button stays disabled.

**What does "The {n} bins with changes already have a pending count." mean? (400)**
All the bins with movements in the window already have a **Pending or Counted** count. Duplicate counts are not created: confirm
or delete the existing ones, or choose another window.

**What does "There are {n} bins with changes; at most 200 are generated at a time. Narrow the date range or the zones." mean? (400)**
"What changed" generates up to 200 counts per run and that window has more bins. **None was created.** Shorten the date range or
choose zones, and repeat: since the default window starts from the last generation, you can generate in parts.

**What does "The filters do not select on-hand inventory to count; widen the filters or add lines by hand." mean? (400)**
Besides normal creation, it now appears in "what changed" when the bins in the window have nothing to count (for example, with
"Include empty bins" off and all of them ended up at zero). Turn on **Include empty bins** or widen the window.

**What does "Creating an empty count (allowEmpty) only applies to a single product, with no bins, zones or categories." mean? (400)**
`allowEmpty: true` was requested in `POST /api/v1/cycle-counts` together with bins, zones, categories, more than one product or no product.
`allowEmpty` is only for opening the **empty count of a single product** (the app uses it for "Another bin"). Remove `allowEmpty` or leave
exactly one product and no other filter.

**What does "The count was already reconciled; it is read-only." mean now that there are Matched and Variance? (422)**
It applies to **both** final statuses. A Matched or Variance count has already posted its adjustments to the ledger and is not edited, not captured,
not confirmed again and not deleted. If you counted wrong, make a manual adjustment or a new count of the bin.

**What does "The count is already finished; you can correct the capture or reconcile it." mean? (422)**
A **finish** (`POST …/finish`) was requested for a count that is already **Counted** (the warehouse app does it blind). On the web there is no such step:
use **Confirm count & adjust**.

**What does "The count has no lines." mean? (422)**
An attempt was made to finish or confirm a count without lines. Add what was found or delete the count if it is not needed.

**What does "Only an open count can be deleted; this one has already been counted." mean? (422)**
Only a **Pending** count is deleted. A **Counted** one has already been counted; confirm it. A **Matched** or **Variance** one answers
"The count was already reconciled; it is read-only.".

**What does "Missing permission 'pulse.attention'." mean? (403)**
`GET /api/v1/analytics/attention` requires it. Ask your administrator for the permission "View the Needs your attention section of the Pulse".

**What does "Specify the warehouse: the company has more than one." / "The warehouse is inactive." mean in the count? (400 / 422)**
It applies to creating a count and to "what changed": with more than one active warehouse you have to choose which; a deactivated one does not admit counts.

### Messages you only see on the screen (no HTTP code)

**What does "Choose whether the adjustment increases or decreases stock." mean?**
In the adjustment modal, **Adjustment type** is missing: choose **Increase** (stock comes in) or **Decrease** (stock goes out). It is required; the screen sets the sign.

**What does "The quantity must be greater than zero." mean in the adjustment?**
The adjustment quantity is **always written as a positive number**; the direction (Increase or Decrease) says whether it adds or subtracts. Write a number greater than zero.

**What does "You cannot decrease more than what is available in the bin ({qty})." / "You cannot transfer more than what is available in the bin ({qty})." mean?**
The quantity exceeds what is available (on hand minus reserved) in that bin, which the screen shows below as "Available in the
bin: N". Write a quantity less than or equal to it. If the product is tracked by lot, the limit is that of the chosen lot.

**What does "Choose the lot." mean?**
When **decreasing** a lot-tracked product you have to choose which of the lots in the bin goes out. (When **increasing**, you type the lot number:
"Enter the lot number.").

**What does "Choose the item to transfer." / "Choose at least one serial." mean?**
In "Transfer", after the source bin you choose the **item** (product and lot of what is there). If the product is tracked by
serial, you have to check at least one serial; the quantity is the number of checked serials.

**What does "The balance already matched; the discrepancy closed itself." mean?**
A notice, not an error: you asked to fix it, but when the system checked, the balance already matched the ledger (a check or a movement
fixed it before). The discrepancy was left as **Self-corrected** and nothing was touched.

**What does "That code is not in this count. Use "Add what was found" if the product is in the bin." mean?**
The count's scanner searches by **exact SKU, barcode, lot or serial** among the lines of **this** count. If the product is in
the bin but not in the count, press **Add what was found**.

**What does "{n} lines match: choose the bin and lot." mean?**
The code matches several lines of the count (the same product in several bins or lots). Choose which one in the list that opens.

**What does "Some quantities could not be saved; fix them before confirming." mean?**
When you press **Confirm count & adjust** the screen first saves what was typed, and some row failed (it appears marked). Fix that
quantity and confirm again.

**Why is the "Confirm count & adjust" button disabled?**
The reason is stated below it: `{n} line(s) still to count.`, `The count has no lines.`, `The count was already confirmed; it is read-only.`
or `The web does not confirm blind counts.` (your user does not have `warehouse.count`, or the count is a blind one from the app).

**What does "Choose the warehouse." mean in "Count what changed"?**
With more than one active warehouse you have to choose which before creating the counts.

**What does "Automatic check: up to date" / "Automatic check: {count} queued" / "Automatic check off" mean?**
It is in Ledger › Reconciliation. **Up to date**: no products are waiting for a check. **N queued**: N products will be checked in a few seconds.

**Off**: the server has it disabled (`Inventory:Reconciliation:Enabled`); only the **Run reconciliation** button works.

**What does "Not applied to the ledger: …" / "Not applied to Balances (they only change the movement summary): …" / "Not applied to Reconciliation: …" mean?**
The ledger filters are the same in the three tabs and are kept when switching tabs, but each one applies only some. The ones it does not
apply are shown dimmed and this note names them: they are still set, and will count again when you return to the tab where they do apply. In Balances, the movement
filters (dates, type, owner, reason, direction, "Manual only") only change the **summary**.

**What does "Your user cannot view bins" mean?**
The **Bin** filter searches in `GET /api/v1/warehouses/bins/search`, which requires `inventory.view`. Without that permission the filter is visible but lists nothing.

### Frequently asked questions

**What is a discrepancy and why doesn't an adjustment fix it?**
A discrepancy is when the **balance** of a bin does not match what the ledger's **movements** add up to. **It is not a physical difference on
the shelf** (a count solves that). Every movement writes the balance and the ledger together, so it almost never happens: it comes from a fix
made directly in the database, from a restore, from the migration or from a bug. An adjustment or a count move the balance **and** the
ledger by the same amount, so the difference stays the same. It is fixed with **Fix the balance from the ledger** (the balance takes what
the movements give, without creating a new movement) or dismissed with a note.

**When does a discrepancy appear and how long does it take?**
A few seconds after each movement of the product (the check runs on its own in the background), or when you press **Run reconciliation**
(it checks the filtered products or all of them), or at the end of a migration. If the server restarts while there are checks queued, they are lost;
the next movement of that product or **Run reconciliation** covers them.

**Can a discrepancy I dismissed come back?**
Only if the figures change: as long as the ledger and the balance have the same numbers as when you dismissed it, it is not reopened. If they change, a
new one opens. A resolved or "Self-corrected" discrepancy that happens again also opens a new one.

**What do the four statuses of a discrepancy mean?**

**Pending**: it needs to be reviewed. **Resolved**: someone fixed the balance from the ledger. **Dismissed**: someone decided to leave it, with a
note. **Self-corrected**: a check found it matching (or it already matched when fixing). The last three are final.

**What do I do if the discrepancy is for the "product total"?**
First fix the per-bin discrepancies of that product; if there are none and the total is still wrong, dismiss it with a note explaining the
cause and let support know. "Fix" is not available for this kind.

**I fixed a discrepancy: how do I verify what is on the shelf?**
After fixing, the record offers **Create a count for that bin** (needs `warehouse.count.capture`): it creates a Pending count of that
bin. The balance already matches the ledger, but that does not prove it matches the physical stock; the count does.

**What does "Count what changed" count?**
One count **for each bin** that had movements in a window: by default from the last time it was generated in that warehouse (the first
time, from 00:00 today in Puerto Rico time) until now; you can change the dates (up to 31 days). Each count brings **everything that is
in the bin** and, with "Include empty bins", also what ended up at zero. Movements that come out of another count **do not count**, nor do
inactive bins or those that already have a Pending or Counted count (they are skipped; the preview says how many). Up to 200 bins
per run and all or nothing.

**I generated "what changed" twice in a row: are the counts duplicated?**
No: the second time the window starts where the first one ended and, besides, a bin with a Pending or Counted count is skipped.

**Why does my count say "Variance"? And why "Matched"?**

**Variance** means that on confirming the system **posted at least one movement** (a `COUNT_VARIANCE` adjustment in the ledger or, on serial
products, a removal, an addition or a transfer): what was counted did not match the current balance. **Matched**: there was nothing to adjust. Note:
it is compared with the **current** balance, not with the snapshot: if the balance changed after the snapshot was taken and you counted what was there, it ends up in
Matched even if the line says the balance changed. Counts already closed with an adjustment before Batch 14 moved to Variance.

**Why do "Open" and "Reconciled" now say "Pending" and "Matched"?**
They were renamed by the owner's decision. The codes did not change (`OPEN` and `RECONCILED`) and `RECONCILED_VARIANCE` ("Variance") was added. An
API filter with `status=RECONCILED` no longer includes the counts with Variance: request both.

**What happened to the "Finish count" step?**
On the web it no longer exists: **Confirm count & adjust** takes the count from Pending to Matched or Variance in a single step. The

**Counted** status remains only for the blind count of the warehouse app (which finishes counting and leaves confirmation to the web).

**Where did the "Count tasks" tab go and how do I assign a count?**
It was removed. Each count in the list on the left has an **Assign** icon (person with "+", needs `warehouse.manage`) that shows
who it is assigned to, and a **Delete** icon (trash can; only Pending counts, needs `warehouse.count`). To choose who, your user
also needs `admin.users`; without it you get "Your user cannot look up the list of users.".

**Where did "Inventory adjustments" go?**
It left the menu (owner's decision). That screen did not show adjustments: it showed the **incompletely received purchases**. Their shortages are
now resolved **from each purchase order's record, Shortages tab** (Purchases), and the address `/warehouse/inventory-adjustments`
leads to Purchases. Inventory adjustments are in **Transfers & adjustments** (Warehouse menu, after Picking & packing) and in the ledger.

**Why can't I choose "Found" when decreasing (or "Damage" when increasing)?**
The reasons depend on the direction: **Found** only when increasing; **Damage**, **Loss** and **Expired** only when decreasing; the rest are valid in
both. It is a screen rule; change the direction to see the reason.

**Why did the ledger dates change?**
The "From" and "To" filters are now **Puerto Rico days** (local midnight), not UTC days. An integration that sent `from` and `to`
thinking in UTC will see movements from the night of the previous or the next day at the edges. The same applies to the discrepancy list and the
count list.

**What does the summary at the top of the ledger and of Balances show?**
With **the same filters** as the table: Movements, Inbound (how many movements and how many units), Outbound and Internal (transfers
that do not leave the filtered warehouses or bins). In **Balances**, **On hand** and **Available** are added. Balances does not filter by Owner (the
balances API does not have it); the owner filter only changes the movement summary.

**What does the detail of a movement show?**
Click a row in the ledger, in Adjustments or in Transfers: date and time, user, type, product, owner, category, quantity, from → to,
lot, serial, reason, origin and note; the **source document** with its status and an **Open** button (only if you have the permission and the module
of that screen; a warehouse task opens its parent's document); and the **related movements** (those of the same document or, if it has
none, those of the same entry; up to 200).

**What does "Manual only" mean?**
Movements without a reference document: those someone made by hand (adjustments and transfers from the screens). Without the filter, the
tabs list **all** movements, including those generated by the system (count, receipt, putaway, replenishment).

**Why don't I see "Needs your attention"?**
Because your user does not have the `pulse.attention` permission, or because that panel was hidden in your Pulse or in the company's ("Organize my Pulse").
If the panel shows and says "All in order", there are no pending discrepancies **that you can see** (you need `inventory.view` and the warehouse
module turned on). The panel shows the 5 oldest discrepancies; **View all (N)** opens the full list in Ledger › Reconciliation.

**Why did the Pulse's "Open counts" change its figure?**
It now counts the real total of **Pending and Counted** counts (before it counted a list cut at 200 and only the "Open" ones). The
Matched and Variance ones no longer count.

**I scanned a code and the count doesn't find it: what do I do?**
The scanner searches for the exact code (SKU, barcode, lot or serial) among the count's lines. If the product is not in that bin's
list, press **Add what was found**. A reader that types the code and presses Enter behaves the same as typing it by hand.

**Can I confirm a count without counting all the lines?**
No: `{n} line(s) still to count.` A line with 0 counted is a valid capture (count zero); an empty line is not.

**Can I count blind from the web?**
No. Blind counting belongs to the warehouse app; the web shows the expected quantity and the confirm button warns "The web does not confirm blind counts.".

## Batch 15 — Day pulse: "Warehouse today" band, company charts, fixed rows and Puerto Rico days (Batch 5 of the change plan)

Chapters: [07 — Day pulse, section 5](07-pulso-y-actividad.md) and [F7A — Pulse: Warehouse today (screens)](frontend/f7a-pulso-almacen-y-actividad.md). The
API messages carry the HTTP code indicated; those seen only on the screen are grouped further down. This batch covers the "Warehouse
today" band, the Pulse's fixed rows, the indicators in one row per module, charts always drawn, the company's 2 warehouse
charts, "Others" and Puerto Rico days throughout analytics.

### New or changed error messages

**What does "Days must be between 1 and 14." mean? (400)**
`GET /api/v1/inventory/pulse/days` received a `days` that is not between 1 and 14 (for example `days=0` or `days=15`). Send a number from 1 to 14 or
do not send it (the default is 7). The Pulse screen always asks for 7, so this message is only seen by whoever calls the API by hand or by an
integration. The error comes in `errors.days`.

**What does "The platform's default elements cannot be edited or deleted." mean with a warehouse chart? (403)**
The two charts "Inventory value by category" and "Inventory movements by type" **are no longer system charts** since Batch 15. If
you still see this message with one of them, your company's database was not updated (`db-init` did not run): ask support to
update it. With any other system chart the message is still correct: they are not edited or deleted; to change it, create your own.

**What does "Only the owner can edit or delete this element." mean with a chart? (403)**
The chart belongs to a person (private or shared) and only its owner edits or deletes it. **Company** charts (with no owner, like the
two warehouse ones) do not give this message: whoever has `analytics.manage` edits them. If you do not have that permission, the server answers 403 without a
message: ask your administrator for it ("Create views, indicators and charts").

**What does "Chart type: BAR, DONUT or LINE." mean? (400)**
An attempt was made to save a chart with another type. There are only bars, donut and line; pie (`PIE`) is not enabled (the owner chose the donut for
the inventory value). Choose one of the three. The error comes in `errors.chartType`.

### Messages you only see on the screen (no HTTP code)

**What does "Delete chart {name}? It belongs to the company: once deleted it is not created again." mean?**
It is the confirmation when deleting one of the company's charts (the two warehouse ones). Confirm only if you are sure: it is not seeded again
in future updates (see "I deleted the company's chart, does it come back?"). With a chart that is not the company's the confirmation is
just "Delete chart {name}?".

**What does "This chart has no data in the configured range." (Pulse) or "This chart has no data with the current filters and range." (Analytics → Charts) mean?**
The chart has no points: the source brings no rows with that date range and those filters. Change the range with **Range** (or the default
range, if you have permission) or check the filter. A list is no longer shown when there are few points: with one, two or three points the
chart is drawn; this notice only appears with no points.

**What does "…" or "—" mean in a number of the "Warehouse today" band?**
"…" means it is still loading. "—" means the query failed (for example, a 403 when switching to a company without permission); the rest of the
Pulse keeps working. Reload the page or enter the company again.

**What does the hidden text "There were counts with variance today." / "Some products are below their minimum." mean?**
It is what a screen reader reads on the card shown in **orange** (border and number): "Counts with variance" if some
count was closed in Variance today, "Products below minimum" if some product is below its minimum.

### Frequently asked questions

**Why does the ledger for "Out" (or "Received") add up differently from the card?**
The card is a **net** and the ledger filters by **movement type**:
- **Units out:** the card adds picking and cross-dock and **subtracts the pick batches that were deleted** that day. In the ledger a
  deleted pick batch is an inbound adjustment (reason "Pick reversal") and the link filters only the Dispatch and Cross-dock
  types, so the ledger can add up to **more** than the card.
- **Units received:** the card adds the receipts **plus the receipt variance adjustments** (what actually arrived: if 10 were expected
  and 8 arrived, it counts 8). The link filters only the Receiving type; the variances show in the ledger as separate adjustments, so it can
  add up to more or less than the card. To see everything, remove the type filter and add the Adjustment one.

**Why does the band say 12 and the "Units received" indicator says another number?**
The **big number** of the band is **today's**. What is compared with the "last 7 days" indicator is the small text "7 days: N": both
count the same Puerto Rico days and the same units, and they match. The "Counts with variance" (30 days) and
"Products below minimum" indicators of the indicators panel have their own period; they were not hidden (owner's decision).

**Why did my chart with many bars change and now says "Others"?**
Since Batch 15, in bars, donut and pie that **add or count**, if there are **more than 8 groups** the 7 largest are shown and a last point

**"Others"** with the sum of the rest, so that the total is exact and the donut does not mislead about the proportions. It also changed in bars
that already existed with more than 8 groups (for example "Changes by user": before it showed only the 8 largest). With average, minimum or maximum nothing
is grouped. If you need to see a group that ended up inside "Others", open the chart in Analytics → Charts and filter.

**Why did a "last 7 days" indicator or chart give a different result than before?**
Because the ranges are now **Puerto Rico days**, not UTC days. "Last 7 days" is today and the 6 previous ones, from 00:00 Puerto Rico
(04:00 UTC). A movement made between 20:00 and 24:00 Puerto Rico now counts in its day (before it counted in the next day, in UTC),
and "today" changes at Puerto Rico midnight, not at 20:00. It also applies to "Last 30 days", "Current month", the custom range, the
charts grouped by date, the exports and the "Today" window of Recent activity. The totals of a long range barely change; those
of a single day may change. Still in UTC are the minimum and maximum date validation of custom fields and the "today" of the client
record and of the driver's documents and rates.

**Why doesn't the "Warehouse today" band stay fixed when scrolling anymore?**
Since 2026-10-05 (owner's decision) only the header stays fixed (date with the Organize buttons, greeting and chip). "Warehouse today" scrolls with the rest
of the Pulse, wherever it is. There is nothing to configure.

**I deleted the company's chart, does it come back? How do I recover it?**

**It does not come back**: it is the owner's rule. Neither renaming it nor deleting it makes a later update recreate it. To have it again, create one by hand in
Analytics → Charts → New chart: "Inventory value by category" = source Inventory (balances), group by Category, sum of
"Value at cost", type Donut, in money; "Inventory movements by type" = source Inventory movements, group by Type, sum of
"Units moved", type Bars, last 7 days. Use a name different from the deleted one (the name of a deleted one stays reserved in the database; what
the API answers if it is repeated was not tested). A **renamed** or **hidden** chart still exists: look for it by its new name or show it with
Organize.

**Who can edit or delete the two warehouse charts?**
Whoever has the `analytics.manage` permission ("Create views, indicators and charts"; today the company administrator). They appear in Analytics →
Charts with the "Company" chip and the Edit and Delete buttons. Whoever has `analytics.dates` changes their default range.

**Why aren't my two warehouse charts in the first row?**
They move to the first row (value on the left, movements on the right) only if they keep the order they came with from the factory **and** the
company never organized its charts. If any active company chart has order 0, it is taken as already organized and the existing
order is respected. Adjust it with Organize.

**Why doesn't the movements-by-type chart show dispatch as negative anymore?**
It now measures **units moved** (always positive): dispatch is one more bar. To see the sign, create a chart with the
"Quantity" field (signed).

**Why are the "Counts with variance" or "Products below minimum" cards orange?**
It is the attention signal: "Counts with variance" if some count was closed in Variance **today** (and today's little bar also goes orange);
"Products below minimum" if some product is below its minimum right now. With nothing to attend to they are violet.

**I tap "Counts with variance" and the list brings more counts than the number. Why?**
The number counts the counts closed **in Variance today** (and the small text, in the 7 days); the link opens the Cycle count with the
Variance status, **without dates** (the date filter of that list is the count's creation date, not its closing date, and it would leave out a count
created earlier and closed in the window). You will also see the older counts in Variance.

**Why does a day's outbound come out negative?**
The reversal of deleting a pick batch **subtracts** on the day it is deleted. If there were no outbound movements that day and a pick batch from another day was deleted,
the net is negative. It is not a data error.

**Why don't I see the "Warehouse today" band?**
Because your user does not have `pulse.warehouse` with `inventory.view`, or the warehouse module (`WMS_LOTSERIAL`) is off in the company, or
someone hid the panel in your Pulse or in the company's ("Organize"). It is the same permission as the "Warehouse" panel: if you see one, you should see the other.

**I changed the band's warehouse and the "Warehouse" panel's changed too. Is it a bug?**
No: it is the **same** warehouse in both, and it is remembered per user in that browser. Changing it in one changes it in the other; the links of the
cards carry the chosen warehouse.

**When does the band change day?**
At Puerto Rico midnight (04:00 UTC). The band is requested again every 5 minutes, so with the screen open the day change
may take up to that long to show; on reload it is immediate.

**Why doesn't the "Accounting" row appear in my Pulse?**
"Your indicators" shows one row per module (Operations, Warehouse, Accounting) and only those that have some visible indicator. Today only "COD
to collect" belongs to Accounting; if that indicator is hidden or its module is off, the row does not show.

**Why can't I move an indicator to another row?**
In **Organize**, indicators are sorted only **within their module row** (the ▲ and ▼ buttons are disabled at the edges of the row). An
indicator's module is changed by editing it in Analytics → Indicators.

**What is "Pending discrepancies" and why isn't it in my Pulse?**
It is a new indicator (Analytics → Indicators) that counts the ledger ↔ balance discrepancies in Pending status. It comes **off in the Pulse**
because the "Needs your attention" panel already shows them; it is turned on with "Show in the day's Pulse".

**Why are my single-point charts now visible when before they were a list?**
Owner's rule: charts are **always** drawn as a chart. A donut with a single slice, a lone bar or a line of a single day
are shown (the line paints each point when there are 12 or fewer). There are never more than 2 charts per row in "Your charts".

## Batch 16 — Direct-to-bin receiving (new request from the owner on 2026-09-30, outside the change plan), suggestion with capacity, app with target bin and additional work of the day

Chapters: [06 — Inventory and warehouse, sections 1.4, 4.1, 5 and 9](06-inventario-y-almacen.md), [09 — Warehouse app, sections 4 and 9](09-app-almacen.md) and
[F6 — Warehouse and inventory (screens)](frontend/f6-almacen-e-inventario.md). API messages carry the HTTP code indicated; those seen only on the screen or in the app are grouped
further down. This batch covers the warehouse's **receiving mode** (with putaway or direct to bin), the **target bin** of each line, suggestions with capacity, the **default receiving
bin**, the app with the target bin step and, the same day, the bin selector of Picking, the number format, exporting Receipts with their lines, the header and the
filters of the exports and dates as dates in Excel and CSV.

### New or changed error messages

**What does "Unknown receiving mode: 'X'. Use PUTAWAY or DIRECT." mean? (400)**
A `receivingMode` that is neither `PUTAWAY` (With putaway) nor `DIRECT` (Direct to bin) was sent when creating or editing a warehouse, when creating a receipt or when editing its header. Send one of the two
(case-insensitive) or do not send the field. The error comes in `errors.receivingMode`. The screen uses a dropdown, so only whoever calls the API sees it.

**What does "Specify the target bin of {sku}: the receipt goes direct to bin." mean? (400)**
An attempt was made to **confirm** a direct receipt and that product's line received something (greater than 0) but has no target bin. Specify where it is stored: choose the bin in the
"Target bin" column (or use "Use suggested bins") and confirm again. It is not required for a lot-tracked product without a lot, nor for a line with cross-dock. In a request, the error comes in
`errors["lines[i].targetBinId"]`, where `i` is the position of the line (from 0).

**What does "Bin {code} is in a {zoneType} zone; the target bin must be a storage bin." mean? (400)**
The bin chosen as target is in a **receiving** (`STAGING`) or **cross-dock** (`CROSSDOCK`) zone. The goods of a direct receipt must end up where they are stored: choose a
bin from another zone (picking, reserve, refrigerated or quarantine). The screen's selector no longer offers them. If a storage bin was later moved to such a zone, the same message appears
on confirming.

**What does "Bin {code} does not exist in the receipt's warehouse." mean? (400)**
A `targetBinCode` was sent that matches no bin of the receipt's warehouse (the app sends it on confirming; on the web the code is not typed by hand). Check the code (upper
and lower case are the same) and that the bin belongs **to that warehouse**. The error comes in `errors.targetBinCode` (on creation, `errors["lines[i].targetBinCode"]`).

**What does "Specify the target bin by id or by code, not both." mean? (400)**
The line brings `targetBinId` **and** `targetBinCode`. Send only one. The error comes in `errors.targetBinId`.

**What does "Bin not found." mean when choosing the target or the default bin? (404)**
The bin id does not exist, belongs to another warehouse or to another company. The target bin and the default receiving bin **have to belong to the receipt's warehouse (or to the warehouse being edited)**.
Choose it again from the list.

**What does "{sku} was already captured with destination {bin}; on a receipt with an ASN or purchase order each line goes to a single bin." mean? (400)**
On a receipt **with an arrival notice or with a purchase order**, each line of the document goes to **a single** bin. The same product arrived with two different bins (for example, from the app).
Leave the whole product in one bin (and transfer later the part that should go to another) or, if the goods are received in two bins, use a **blind** or return receipt, where two
lines of the same product can be entered. The app warns **before** sending (see below). The error comes in `errors["lines[i].targetBinCode"]`.

**What does "Target bin {code} is deactivated." or "The zone of target bin {code} is inactive." mean? (422)**
The bin (or its zone) was deactivated after choosing it, or an attempt was made to choose a deactivated one. Reactivate it in Warehouses → Bins or choose another one. It is checked when saving the line and again **on confirming**: an
open receipt with a deactivated target cannot be confirmed until it is corrected.

**What does "The warehouse has no receiving bin (STAGING zone); specify one." mean when switching a receipt to "With putaway"? (422)**
It is the usual message. It now also appears when an open **direct** receipt is switched to "With putaway" and the warehouse has neither a `STAGING` zone nor a default receiving bin, and the
header does not specify one. Create a receiving zone with a bin, set the warehouse's **Default receiving bin**, or leave the receipt as direct.

**What does "The receiving bin must be in a STAGING or CROSSDOCK zone." or "The receiving bin is deactivated." mean when saving a warehouse? (400 / 422)**
They are the same messages as for a receipt's receiving bin, now also for the warehouse's **Default receiving bin** (Warehouses → Details → Receiving). Choose an active bin from a receiving
or cross-dock zone of the same warehouse. The error comes in `errors.defaultReceivingBinId`.

**What does "Receipt {n} was already confirmed; it cannot be modified." mean when using "Use suggested bins" or changing the mode? (422)**
It is the usual message (receipt already confirmed), now also for the lines' target, the receipt mode and "Use suggested bins": they are only done with the receipt open (Expected, Receiving or Discrepancy).

**What does "The record was modified by another user; reload and try again." mean when using "Use suggested bins"? (409)**
Someone saved the receipt while you had the screen open. The screen reloads the receipt: check what changed and press the button again.

**What does "The quantity exceeds what is available for cross-dock (0)." (409) or "The line's receipt does not admit allocations (deleted or with no pending putaway)." (422) mean on an already confirmed direct receipt?**
A confirmed direct receipt has no pending putaway (the goods are already in their target bin), and cross-dock on a confirmed receipt is done against the pending putaway. Allocate the cross-dock

**while the receipt is open**. (From the code; it was not tested on a real receipt.)

**What does "warehouse.receivingMode must be PUTAWAY or DIRECT." (exit code 1) or "The default receiving bin {code} does not exist in the warehouse." (warehouse rejection in the report) mean in `import-legacy`?**
The first is a configuration error: `warehouse.receivingMode` only admits `PUTAWAY` or `DIRECT`. The second is that `warehouse.defaultReceivingBin` matches no bin created for the warehouse (or is not
from a receiving or cross-dock zone): fix the code and repeat. The two keys are only applied **when creating the warehouse**: `--update` does not overwrite them; on a warehouse that already exists they are changed in Warehouses → Details.

### Messages you only see on the screen or in the app (no HTTP code)

**What does "The target bin is missing on {n} line(s)." mean?**
It is under the (disabled) **Confirm receipt** button of a direct receipt: there are {n} lines that receive something and have no target bin. Choose the bin for each one or press **Use suggested bins**.

**What does "Exceeds the capacity of {bin}: {free} fit" mean?**
It is an **orange notice**, not an error: what was received on that line is more than the bin's free space (`capacity − stock − what other lines of the receipt already assign`). It can be saved and confirmed anyway.
See "Can I go over the capacity?".

**What does "Suggested: {bin} · {reason}" under the target bin mean?**
It is the first bin the system recommends for that line and why ("Consolidate with the same product", "Empty reserve", "Quarantine (return)", etc.). It is only a hint: it is not filled in on its own. It only appears if the
chosen bin is not that one.

**What does "A bin was assigned to {n} line(s); {m} without a suggestion." mean?**
It is the notice of **Use suggested bins**: {n} lines got their suggested bin and {m} had no storage bin where they would fit. Choose the {m} by hand.

**What does "Choose a product first" mean in the bin of Picking & packing?**
The Bin selector only offers the bins where the product has available stock, so it needs to know the product. Choose it and they will appear.

**What does "{code} receives direct to bin: only receipts from before the change or with cross-docking appear here." mean?**
It is a notice in **Receiving → Pending putaway** when you filter by a direct warehouse. Direct receipts do not generate putaway tasks, so they do not appear here. See "Why doesn't a direct receipt appear in Pending putaway?".

**What does "Change the receiving mode?" mean?**
It is the confirmation when saving a mode change in the warehouse record. It says how many open receipts and how many with pending putaway there are: **they keep their mode**; only **new** receipts follow the new mode.

**In the app: "That bin doesn't exist in this warehouse.", "That bin is for receiving or cross-docking; scan where the goods are stored.", "That bin is inactive; scan another one."**
The app validated the scanned bin **without signal**, against the bins it has stored for the warehouse. Scan the bin where the goods are actually left. If a bin was just created and the app does not have
it yet, sync with signal ("Sync now") and scan again.

**In the app: "This device doesn't have this warehouse's bins yet. Sync with a signal and try again."**
The device has not downloaded the bins of the default warehouse (the first time they all download, about 3,886 at Advance Depot). With signal, press "Sync now" and scan again.

**In the app: "Can't send it like this" / "{sku} was already captured with destination {bin}; on a receipt with an ASN or purchase order each line goes to a single bin."**
It is the same case as the 400 above, detected **before sending**. See "Why won't the scanner let me send?".

### Frequently asked questions

**How do I set a warehouse to direct?**
With `warehouse.manage`: Warehouses → choose the warehouse → **Details** → **Receiving** section → **Receiving mode** = "Direct to bin" → **Save** and confirm. (It can also be done when creating the warehouse, or with a `PATCH` of
`receivingMode`.) From that moment **new** receipts are born direct: each line carries its target bin and there are no putaway tasks. To go back, change the mode to "With putaway". The mode of a warehouse
without a receiving zone (like Advance Solutions) must remain direct, or receipts with putaway will give "The warehouse has no receiving bin (STAGING zone); specify one.".

**What happens to open receipts when I change the warehouse mode?**

**Nothing:** each receipt keeps the mode it was opened with, and pending putaway tasks run their course (they have to be put away). Only new receipts follow the new mode. If you want **one** open receipt
to change, edit its header (pencil) and change its "Receiving mode" before confirming it.

**Can I go over the capacity?**
Yes. The bin's capacity **only warns**: the line is marked with "Exceeds the capacity of {bin}: {free} fit" in orange and can be saved and confirmed anyway. What does not fit stays physically in that bin; transfer
the excess to another one later (Transfers & adjustments). Receipts open at the same time do not see each other, so two receipts can exceed the same capacity. "Use suggested bins", on the other hand, **only assigns where it fits**.

**Why doesn't my direct receipt ask for the receiving bin?**
Because the goods do not go through there: they go directly to each line's target bin. A warehouse without a `STAGING` zone can receive this way. The warehouse's default receiving bin is only used by receipts with
putaway and by lines with cross-dock.

**Why doesn't a direct receipt appear in Pending putaway?**
Because it generates no tasks: on confirming, each line stays in its bin and the receipt moves to **Put away** at the same moment (the history leaves Receiving → Completed → Put away). In a direct warehouse, Pending
putaway only shows receipts from before the mode change, those switched to "With putaway" and those that had cross-dock.

**Why doesn't the ledger show a putaway movement after a direct receipt?**
Because there was none: the inbound (`RECEIPT`) was already posted to the final bin. On a receipt with putaway there is an inbound to the receiving bin and then a transfer to the final bin.

**Why does Recent activity show a single event on a direct receipt?**
Because the receipt is confirmed and becomes Put away at the same instant. Only "Receipt … confirmed" is shown (the "put away" event is omitted if there were no tasks). With cross-dock or with putaway, both appear.

**Why doesn't it suggest any bin?**
No active storage bin has room for that quantity (capacity in units), or the product has nowhere to consolidate and the warehouse has no reserve or picking bins. The system **never** suggests receiving or
cross-dock, and suggests **quarantine** only on returns. Choose one by hand; you are warned if it does not fit. Since this batch the capacity in units is also respected in the usual **putaway tasks**: a
full bin is no longer suggested for putaway.

**Why is quarantine the first suggestion on a return?**
It is an owner's decision (D6): returned goods are inspected before being sold again. The first suggestion is a quarantine bin, if the warehouse has one. You can choose another.

**What is the "Default receiving bin" and why does Depot use R1?**
It is the receiving bin used by receipts with putaway (and by lines with cross-dock) when the header does not specify another. Without it, the system takes the first bin of a `STAGING` zone by zone code;
at Advance Depot that gave `S1` from "Embarque". By the owner's decision, at Depot it is `R1` (zone `STG`). It is changed in Warehouses → Details → Receiving (with "The first receiving bin" the automatic rule is restored).

**Can I split a line across several bins?**
No: a line goes whole into **one** bin. If more arrived than fits, receive it anyway and transfer the excess later. On a **blind or return** receipt you can enter two lines of the same product, each with
its bin. On a receipt with a notice or purchase order, the same product with two bins gives 400 (see above).

**Why did my receipt from the app come in "With putaway" in a direct warehouse?**
Because it was started with an app **older** than this batch (it sends neither bins nor mode). The server does not lose it: it receives it with putaway and creates tasks. Update the app on the devices so that they scan the target bin.

**Why won't the scanner let me send?**
There are three causes in a direct warehouse: (1) **Confirm receipt** is disabled because some line has no target bin (lines are added by scanning a valid bin); (2) "Can't send it like this" appears: the same
product of a notice or purchase order would end up in two bins; leave all its lines in one bin (remove a line and add it again), and (3) "This device doesn't have this warehouse's bins yet": sync
with signal. A receipt that the server rejects later, once in the queue, **cannot be edited**: that is why the app prevents it beforehand.

**Why doesn't the "Suggested" hint appear on the scanner?**
It needs signal. Without signal the capture goes on the same: scan the bin where you leave the goods. The hint comes from the server's putaway suggestions (the first one for that product and quantity) and does not deduct what other lines of the
same receipt already occupy.

**Why does the Bin selector of Picking & packing only show some bins?**
Since the 2026-09-30 adjustment it only offers the bins **where the product has available stock**, with their quantity ("P-01 · PCK · 9 avail."), in the system's FEFO order (the first one is marked "Suggested").
If you choose a lot, only the bins of that lot. Without a product the field is disabled.

**Why do numbers now have a comma?**
Because that is how they are written in Puerto Rico: comma for thousands and period for decimals (`61,023`; `1,250.5`). Before, in Spanish, the system wrote `61.023` (thousands period) and a four-digit figure like `1250` with no
separator, and they were confused with decimals. The change is only in presentation: the data did not change. Money carries `$` and two decimals (`$1,234.50`). It applies on the web and in the app; document numbers and identifiers carry no comma. It is the same format in English.

**Why do the PDF or the Excel bring the company and a "Filters:" line?**
Since this batch, every table PDF and every Excel carry at the top the **company**, the title, "Generated on …" and a line with the **filters the screen had** ("Filters: Warehouse ALM-01 (Main warehouse) ·
Status Receiving · Created from 01/09/2026 to 30/09/2026"), to know where the file came from. The CSV does not change.

**Why does the PDF say "No filters"?**
Because the screen has a filter bar and none was chosen when you exported: the file brings **everything** there was. The filters line only names those that are **chosen** when exporting: to leave a record of a filter, choose it before exporting. Tables that
have no filter bar, and those inside a window (modal), do not carry that line.

**How do I open the dates in the Excel?**
They are **real dates**, not text: they can be sorted, filtered by date and subtracted. In Excel they carry the `yyyy-mm-dd` format (or `yyyy-mm-dd hh:mm` if they have a time, in Puerto Rico time). If a cell shows a number
like `46295`, give the column the date format. In the **CSV**, the date comes out as `2026-09-30` or `2026-09-30 14:03:00`: Excel reads it as a date according to your regional settings; if it does not, open the file with
Data → From Text/CSV and mark that column as a date. The PDF shows the date as readable text.

**Why doesn't the ledger export the Time column anymore?**
Because the file's **Date** column now carries the date **and the time** of the movement. Counts and Discrepancies export the real date.

**Why does the Receipts Excel have one row per line and no longer bring the "Lines" column?**
Because the export of Receipts (and of Pending putaway) now brings **each receipt with its lines**: in Excel and CSV, one row per line repeating the receipt data (a receipt without lines takes one row with the line
columns empty); in PDF, one block per receipt with the small table of its lines ("No lines" if it has none). Since the lines are visible, the count is no longer needed.

**Why aren't the Excel headers bold or frozen when scrolling?**
It is a limit of the library that generates the files (community edition of SheetJS): it writes neither bold nor frozen panes. The header row carries an **autofilter** (Excel's filter arrows) to
sort and filter it.

**Why does the Receipts PDF come out landscape in letter size and the others in A4?**
Grouped PDFs (receipt with its lines) use landscape letter; the other tables, portrait A4 (landscape with more than 5 columns).

**How do I switch a warehouse back from "Direct to bin" to "With putaway"?**
Same as the other way round: Warehouses → Details → Receiving → Receiving mode = "With putaway". It needs a receiving bin (the warehouse's or the first `STAGING` zone) to receive with putaway. Open direct
receipts stay direct.

## Batch F9 — Tenant settings (screen) and region and formats across the web

Screen System → Tenant settings (`/system/settings`, `admin.tenant`). Detail in
[frontend/f9-ajustes-de-la-compania.md](frontend/f9-ajustes-de-la-compania.md). The server messages of Region & formats
are above, in "Region and formats (2026-10)" (batch 18).

### New or changed error messages

**What does "The name is required." mean when adding a holiday? (400)**
The holiday reached the server without a name. Write the name (for example "Día de Reyes") and add it again. The screen
warns beforehand with "Enter the holiday date and name".

**What does "'COD' depends on 'LTL_GROUND', which must be enabled first." mean? (409)**
An attempt was made to enable a module whose required module is off. Enable the required one first (the screen indicates it with
"Enable first: …" and leaves the switch disabled).

**What does "Only the platform administrator can enable or disable modules." mean? (403)**
Tenant settings → Modules belongs to the platform administrator only. The company administrator sees the list read-only (without switches). To enable or disable a module, ask the platform administrator.

**What does "'SYSTEM' is a core module and cannot be disabled." mean? (409)**
Core modules are always on; the screen marks them "Core — always on" and does not offer to disable them.

**What does "Must be ≥ 1." mean in the maximum stops per route? (400)**
The default maximum stops per route has to be 1 or more. The screen warns beforehand with "Must be 1 or more.".

**What does "2-letter language code." mean? (400)**
The default language must be a 2-letter code (`es`, `en`). The screen only offers Español and English.

**What does "BrandingJson is not valid JSON." or "BrandingJson too large (logos go to blob storage)." mean?**
They were the messages from before batch 19. Today the branding is validated with its own rules (size of 4096 characters, known fields,
colors, contrast and hue) and each message is in the "Batch 19 — Company branding" section further down.

### Messages you only see on the screen (no HTTP code)

**"At least one working day is required"** — an attempt was made to turn off the last working day; leave at least one.

**"Enter the holiday date and name"** — the date or the name of the new holiday is missing.

**"There is already a holiday on that date."** — a holiday already exists on that same date; delete it first if you want to replace it. Since
batch 19 the server decides this (409) and the screen shows its message (before, the screen detected it on its own and the server
replaced the holiday without warning).

**"The thousands and decimal separators cannot be the same: the other one was changed."** — it is a notice, not an error: when choosing a
separator that collided with the other, the screen changed the other one. Check the preview and save.

**"Enter a hexadecimal color, for example #1F6FE5."** — the custom color typed by hand is not a 3- or 6-digit hexadecimal.

**"Fix the checks before saving."** — the color combination does not pass the minimum contrast or the two accents look
too much alike (less than 40°); the list marked with ✕ says which.

**"Must be 1 or more."** — maximum stops per route empty, decimal or less than 1.

**"The phone must have 10 digits: (###) ###-####."** — see the batch 12 question (the number and the mask come from the
company).

### Frequently asked questions

**Why do dates in Spanish now come out month/day/year and the time with a. m./p. m.?**
Since batch F9 the format is the company's (Region & formats), not the language's. Puerto Rico uses MM/DD/YYYY and 12 hours (the Puerto Rico
date order is pending confirmation with the owner: decision 1 of batch 18). For day/month/year or 24 hours, an administrator changes it in Tenant
settings → Region & formats.

**I changed the formats and another user still sees the old ones.**
Their screen reads the settings again when 10 minutes pass and they return to the window or open another screen, or when they change language
or company. Reloading the page brings them immediately.

**What happens to supplier phones already saved with another mask?**
They are shown with the new mask if they have the digits it requires (for example, 10 in Puerto Rico); those that do not are shown as they are
and have to be corrected when edited. Those saved from now on carry only the digits.

**I changed the region to the United States and the time did not change.**
Puerto Rico (AST) and the US East (New York, with daylight saving time) have the same time from March to November (UTC−4). In
winter New York is one hour behind. The rest of the formats of the two regions are the same.

**Where do I change the MFA policy or the session duration?**
In Security & audit ("Open Security & audit" button on the General tab); it is not repeated in Tenant settings.

**Where do I change a warehouse's receiving mode?**
In the warehouse record (Warehouses → Receiving). The Operations tab only summarizes it and marks in red the warehouses with putaway
and no receiving bin.

**Can I upload my company's logo?**
Yes, since batch 19: Tenant settings → Branding → Logo (see "Batch 19 — Company branding" further down).

## Batch F10 — Security & audit (screen), company-wide sessions and paginated activity

Screen System → Security & audit (`/system/audit`, `admin.audit`). Detail in
[frontend/f10-seguridad-y-auditoria.md](frontend/f10-seguridad-y-auditoria.md); backend in chapter 01, sections 9 and 9.1.

### New or changed error messages

**What does "The current session is not revoked from the list; use Sign out." mean? (409)**
An attempt was made to revoke your own session from Active sessions (`DELETE /api/v1/audit/sessions/{id}`). The screen does not offer that
button on the "This session" row; to close it use **Sign out**. (In My account → Sessions the message is still the batch 1 one:
"…; use logout.")

**What does "Session '<id>' not found." mean? (404)**
The session is no longer active: another administrator revoked it, it expired, the user signed out, or it belongs to another company. Reload the list.

**What does "This action requires recent re-authentication (AAL2)." mean? (403, `aal2_required`)**
"Sign out other sessions" is a sensitive action. The web opens the password (and MFA code) window on its own and repeats it; you will only
see it if you call the API by hand: do `POST /api/v1/auth/reauth` and repeat.

**Revoking a session answers 403 without a message.**
Your user does not have `admin.users` (revoking requires `admin.audit` and `admin.users`). On the screen the buttons do not appear without
that permission. A `PERMISSION_DENIED` event is left.

**"Between 1 and 365 days." / "The re-authentication window must be 15, 30 or 60 minutes." in the policy (400)**
They are the usual messages of `PUT /api/v1/tenant/settings` (chapter 01, section 11); now the screen places them under the
field. The duration must be a whole number of days from 1 to 365. The re-authentication window can only be 15, 30 or 60 minutes (a company that already had another value keeps it as long as it does not change it).

### Messages you only see on the screen (no HTTP code)

**"Between 1 and 365 days."** under a duration — empty, decimal, less than 1 or greater than 365; nothing is sent until it is corrected.

**"The first 10000 rows were downloaded; narrow the date to export the rest."** — (message from an earlier version: today the export **has no limit**; if you see it, the server stopped returning rows and exporting again is enough) before it had a limit of 100,000
rows; set a shorter Date and export in parts.

**"No activity matches these filters."** — no row matches the type, the date and the search; use **Clear**.

**"Sessions could not be loaded."** / **"The tenant settings could not be loaded."** — the read failed (network or
permission); **Retry** or reload.

### Frequently asked questions

**What is the difference between "Event" and "Alert"?**
Both are security events. "Alert" is a failed or blocked attempt (wrong password, account lockout,
reuse of a token), a denied permission or a block; "Event", what went well (sign-ins, sign-outs, MFA, sessions
revoked on purpose). "Change" is the change log of the records.

**Before, the activity total changed when moving between pages.**
It was a server bug (the total came from the first rows read). Since batch F10 it is the real count and the pages go
all the way to the end; the search is also done over the whole log, not over the first rows.

**I searched "Login · Éxito" and found nothing.**
The search looks at each piece of data separately (type, result, user, detail, IP, entity). Search `Login` or `Éxito`, or use the
Security filter.

**Why is a session's "Location" a number (or "—")?**
It is the IP from which the session was opened or renewed. Sessions opened before this batch do not have it ("—") until they are
renewed. It is not converted into a city (an external service would be needed).

**I revoked a session and the user lost all of them.**
That no longer happens (owner's decision, 2026-10-03). Revoking a session (as administrator, from My account, "Sign out other
sessions" or logout) closes only that one: if the revoked device tries to renew it gets `401 Refresh token inválido.` and the person's other
sessions stay alive. Only the reuse of an already rotated token (possible theft) closes all of the person's sessions.
If the user still lost several, check in Activity whether there was a `refresh_reuse` event: it is a possible theft and it is the expected behavior.

**Does "Sign out other sessions" also sign me out of my other devices?**
Yes: it closes all the company's sessions except the one in this browser, including yours on other devices and those of the
warehouse devices.

**I changed the session duration and nobody had to sign in again.**
The new duration applies to the sessions opened or renewed from that moment on; the rest keep their expiration.

## Batch 19 — Company branding on the server: color validation, logos and duplicate holiday

Tab Tenant settings → Branding (`/system/settings?tab=brand`, `admin.tenant`). Detail in
[frontend/f11-marca-por-compania.md](frontend/f11-marca-por-compania.md) and in chapter 01, section 11.2. Branding
errors appear next to the colors; those of each logo, under its slot.

### New or changed error messages

**What does "The branding is too large (maximum 4096 characters); logos are uploaded separately." mean? (400)**
The `brandingJson` exceeds 4096 characters. The branding is only a theme and three colors (less than 200 characters); logos no longer travel
there, they are uploaded with their own button. It only appears if someone calls the API by hand with other content.

**What does "The branding is not valid JSON." or "The branding must be a JSON object." mean? (400)**
The text is not JSON, or it is JSON but not an object (`{ ... }`). The screen always sends a valid object.

**What does "The branding has an unknown field: '<field>'." mean? (400)**
A field that does not belong to the branding was sent. Only `preset`, `useCustom` and `custom` (with `flow`, `money` and `neutral`) exist. If you were
saving the logos inside the branding, that is no longer done: use the logo endpoints.

**What does "State colors (ok, warn, danger, info) cannot be customized: '<field>'." mean? (400)**
Green, amber, red and info blue are deliberately not changed (green = good, red = bad); remove that field.

**What does "Field '<field>' has an invalid type." mean? (400)**
`preset` must be text, `useCustom` true or false, `custom` an object and each color a text.

**What does "Color 'custom.<flow|money|neutral>' is not hexadecimal (use #RGB or #RRGGBB)." mean? (400)**
The color is not a 3- or 6-digit hexadecimal (the `#` is optional; spaces and names like "red" are not accepted). The screen
warns beforehand with "Enter a hexadecimal color, for example #1F6FE5.".

**What does "Preset theme '<id>' does not exist." mean? (400)**
The themes are `teikem`, `marino`, `acero`, `carretera`, `granate`, `vino`, `bosque`, `selva`, `oliva`, `turquesa`, `indigo`,
`violeta` and `grafito`, in lowercase.

**What does "The contrast of the operations color in dark mode is 1.34:1; the minimum is 4.5:1." mean? (400)**
With that color, what is written or painted with it does not read well on the theme's panel. It says which piece (text, dimmed text,
operations color or money color), in which mode (dark or light), the real contrast and the minimum (7:1 for the main text; 4.5:1
for the rest). Choose a lighter color (dark mode) or a darker one (light mode); the screen shows all the contrasts live.

**What does "The operations and money colors are too similar: 4° apart and the minimum is 40°." mean? (400)**
The two accents are “two currents” (what goes out to the street and the money that comes back) and have to be distinguishable: their hues must
be at least 40° apart. Change one of the two.

**What does "Logo slot '<slot>' not found." mean? (404) and "Logo '<slot>' not found."? (404)**
The first: the slot is not one of `lockup`, `lockup-inverted`, `mark`, `mark-inverted`. The second: the company has no logo in that
slot (read or remove). The interface then uses the other variant or the Teikem logo.

**What does "Select a logo file." mean? (400)**
No file arrived (or it arrived empty). Choose one with the Upload button.

**What does "The logo exceeds the 512 KB maximum size." mean? (413)**
The file weighs more than 512 KB (524,288 bytes). Reduce the PNG/JPG/WebP or simplify the SVG. The screen warns before sending it.

**What does "Unsupported format: the logo must be SVG, PNG, JPG or WebP." mean? (415)**
The file content is not one of those formats. The content is checked, not the name: a file renamed to `.png` does not
work. GIF, PDF, HTML and SVG saved in UTF-16 are not accepted either. If the request is not `multipart/form-data` the 415 arrives without a
message.

**What does "The file content (image/png) does not match the declared type (image/jpeg)." mean? (415)**
The browser or the program said it was one image type and the bytes are of another. Save the file again in the format you
want.

**What does "The file is damaged or incomplete and cannot be used as a logo." mean? (400)**
It is a truncated PNG, JPG or WebP (an interrupted download, for example). Export it again.

**What does "The logo upload arrived incomplete or malformed. Try again." mean? (400)**
The request was cut in half (no network, for example). Retry.

**What does "The SVG is not valid XML." mean? (400)**
The SVG has unclosed tags or invalid characters. Open it in an image editor and save it again.

**What does "The SVG is not accepted: it declares a DOCTYPE or entities." mean? (400)**
An SVG with `<!DOCTYPE …>` or `<!ENTITY …>` can read files from the server or make it use up memory; it is rejected. Export the SVG without
DOCTYPE (“SVG 1.1/Tiny” option without DTD, or clean the file with SVGO).

**What does "The SVG is not accepted: it contains the <script> element, which can run code or load external content." mean? (400)**
The SVG has an active element (`script`, `foreignObject`, `iframe`, `object`, `embed`, `link`, `animate`, `set`…). Remove it or
convert the design to simple paths and shapes.

**What does "The SVG is not accepted: the 'onload' attribute runs code." mean? (400)**
Some element has an event attribute (`onload`, `onclick`…). Remove it.

**What does "The SVG is not accepted: the '<attribute>' attribute contains a script link (javascript:)." mean? (400)**
A link in the SVG starts with `javascript:` (or `vbscript:`). Remove it.

**What does "The SVG is not accepted: the '<attribute>' attribute points outside the file (only internal #id references are allowed)." mean? (400)**
An `<image>`, `<use>` or `<a>` points to another address (`https://…`, another file). Embed the image as `data:image/png;base64,…` or
use only references to elements of the same SVG (`#id`).

**What does "The SVG is not accepted: a stylesheet imports or references external content." mean? (400)**
A `<style>` or `style=""` uses `@import`, `url(https://…)` or `expression(…)`. SVGs with web fonts do this: convert the texts
to paths.

**What does "There is already a holiday on that date." mean? (409)**
`POST /tenant/holidays` with a date that already has an active holiday. The existing one does not change; delete it first if you want
to change it. (One deleted earlier can indeed be added again.)

### Messages you only see on the screen (no HTTP code)

**"No logo: the Teikem one is used"** — that slot has no file.

**"Logo saved" / "Logo removed"** — confirmation of uploading/replacing or removing a logo (it applies immediately, without Save changes).

### Frequently asked questions

**Are logos saved with "Save changes"?**
No. Each logo is uploaded or removed instantly with its buttons; "Save changes" is only for the colors.

**I uploaded only the lockup for light background and in the dark theme it looks the same.**
It is on purpose: if a variant is missing the other one is used in both themes. Upload the inverted one if the logo cannot be read on a dark background.

**I uploaded the lockup but the collapsed bar still shows the Teikem symbol.**
The collapsed bar uses the square mark (`mark`); upload that piece. The lockup is not shrunk to replace it.

**A user without `admin.tenant` sees the logos but cannot change them.**
Correct: reading only requires a session (it is the interface's branding) and writing requires `admin.tenant`.

**Where is the logo file kept and what does the log see?**
In the database (`TenantBrandLogo` table). The change log (entity `TENANT_LOGO`) records who uploaded or removed it and when,
and the type and size, but never the file.

---

## Batch 21 — Cycle count by product: correction, preview, "To review", bulk close and provisional bin (server)

Chapter: [06 — Inventory and warehouse](06-inventario-y-almacen.md), section 6, "Batch 21".

### New or changed error messages

**What does "The count was already reconciled; it is read-only." mean when requesting the preview? (422)**
The preview only exists while the count is Pending or Counted. One already confirmed (Matched or Variance) is viewed through its record.

**What does "The count was already reconciled; it does not admit new bins." mean when creating a provisional bin? (422)**
The provisional bin is created from an unconfirmed count. If the count was already confirmed, create the bin from Warehouses (requires `warehouse.manage`).

**What does "A bin with that code already exists in the warehouse." mean when creating a provisional bin? (409)**
The code is unique per warehouse (also against definitive bins and other provisional ones). Use that bin as a count line or write another code.

**What does "The bin is not pending review." mean when confirming? (409)**
That bin is not provisional: it was already confirmed (or never was). There is nothing more to do.

**What does "Zone not found." mean when creating a provisional bin? (404)**
The zone does not belong to the count's warehouse (or does not exist). Choose a zone from the same warehouse as the count.

**What does "The zone is inactive; reactivate it first." mean when creating a provisional bin? (422)**
The zone is deactivated. Choose another one or ask a supervisor (`warehouse.manage`) to reactivate it.

**What does "Specify the bin's zone." / "Enter the bin's code or its aisle/rack/level/position." / "The bin code only admits letters, numbers, hyphen and underscore (maximum 40)." mean? (400)**
They are the same validations as when creating a bin: the zone is missing, the code (or its parts) is missing or the code has characters that are not allowed.

**What does "At most 200 counts are checked per run; narrow by warehouse or by ids." mean in the bulk close? (400)**
You sent more than 200 `ids`. Send fewer or use `warehousePublicId` (the server looks at up to 200 and marks `truncated = true` if there were more).

**What does "The count of {sku} in {bin} ({counted}) is less than what is reserved ({reserved}); release the reservation before reconciling." mean? (409)**
It is the same error as always when confirming, and now it also appears **beforehand**, in the preview (`lines[].error`), and as the `Errors` reason in the bulk close. Release the reservation or correct the counted quantity.

**What does "Serial {serial} is captured on more than one line of the count." mean? (400)**
The same serial of the same product appears on two lines. In the preview it arrives as `blockingError`; in the bulk close as `Errors`. Remove the serial from one of the lines.

### "Skipped" reasons of the bulk close (`skipped[].reasonCode`, HTTP 200)

**`WouldPost` — "Would post {n} movement(s); review it."**
Against the **current** stock the count does not match (even if it matched with the snapshot). Open it, look at the preview and correct or confirm by hand.

**`Pending` — "{n} line(s) still to count."**
It has lines without a quantity (for example, "Refresh" cleared the captures of the lines with an old snapshot). Finish counting them.

**`Errors`**
Some line would give an error on confirming (counted less than reserved or a repeated serial). The `reason` text says which.

**`Stale` — "The stock changed while closing; review it."**
It matched when looked at, but a movement changed the stock before closing it and it no longer matches. Nothing of that count was closed; look at it again.

**`NotCounted` — "The count has not finished being counted yet."**
It is Pending and you requested it by `ids` without `includeOpen`. Finish it or pass `includeOpen: true`.

**`AlreadyReconciled`, `NotFound` ("The count does not exist."), `NoLines` ("The count has no lines."), `Failed`**
It was already confirmed; the id does not exist or belongs to another company; it has no lines; or it failed for another rule (the reason comes in `reason`). A failure of one count does not affect the others.

### Frequently asked questions

**What is the difference between capturing, recapturing and correcting?**
The first time a line is counted it stays as what was "captured" (with who and when). The same person can recapture while the count is Pending and replaces their capture. Any change by another person, or by anyone after finishing the count (Counted), is a **correction**: the current quantity changes, the captured one is kept and who corrected it is noted. If the correction goes back to the captured value, it disappears.

**Does correcting a quantity move inventory?**
No. A correction is not an adjustment or a transfer: it only changes what gets reconciled. Inventory moves only on confirmation, by the difference between the current counted quantity and the current stock.

**Where is the evidence of the correction kept?**
In the line (`capturedQty`, `capturedByName`, `capturedAtUtc`, `correctedByName`, `correctedAtUtc`, `wasCorrected`), in the count's change log and, on confirmation, in the reason of the movement ledger entry: `Conteo CC-00001 · contó 3 (Ana Pérez, 2026-10-03 14:05) · corregido de 3 a 5 por Beto Ruiz (2026-10-03 15:00)`.

**Can someone who counts blind see what the system says through the evidence?**
No. They see what they captured and whether someone corrected it, but `systemQty`, `currentQty`, `varianceQty`, `reconciledSystemQty` and `adjustedQty` stay `null`.

**Does the preview say the same thing that will happen on confirmation?**
Yes: it uses the same calculation as the confirmation, with the current stock. The only thing that can change is that, between the preview and the confirmation, another movement modifies the stock; that is why the confirmation recalculates with the balances locked.

**What does `matches` mean?**
The count matches: it has lines, none uncounted, none with errors and, against the **current** stock, it would not post any movement. If there are still pending lines, `matches` is `false` even if what has been counted so far agrees.

**The count matched the snapshot but "Close the ones that match" did not close it.**
Because it is measured against the current stock, not against the snapshot: if a line moved since the snapshot and would now post something, the count stays for review (`WouldPost`).

**Can "Close the ones that match" leave something half done?**
No. Each count is closed in its own transaction: it either ends as Matched or stays as it was. The others are not affected.

**Why doesn't the "To review" list show a count the operator already finished?**
It only shows the **Counted** ones (the operator pressed Finish). A Pending count appears only with `includeOpen=true` and when all its lines are already captured.

**The bin the operator created was not in the system; can they keep counting?**
Yes. The provisional bin is used immediately as a count line and in inventory. The supervisor sees it with `isProvisional = true`, and confirms it, corrects it or deactivates it.

**Can a provisional bin with inventory be deactivated?**
The usual rule applies: a bin with stock (on hand or reserved) or with open tasks is not deactivated (409 `Bin {code} has inventory; it cannot be deactivated.`). Move it first or confirm it.

**Who sees the "By product" origin?**
Counts created with `productPublicIds` and without bins or zones carry `originCode = PRODUCT` ("By product"); they can be filtered with `origins=PRODUCT` in `GET /api/v1/cycle-counts/page`.

## Batch 25 — Open count with several products (server, app and web)

Details in [chapter 06 §6.x](06-inventario-y-almacen.md), [chapter 09 §7.2](09-app-almacen.md#72-counting-several-products-in-one-count-batch-25) and
`docs/lote25-decisiones.md`.

### New server messages

**400 — "Product {sku} has no stock in any bin of the warehouse; indicate the bin where you found it."** (`errors.binId`)
`POST /cycle-counts/{id}/lines` without `binId` for a product the system does not have in any bin. Send the bin where you found it.

**400 — "Product {sku} is in several bins ({codes}); indicate which one you counted it in."** (`errors.binId`)
Without `binId` and the product is in several bins: choose one (the web chooses it in the Bin field; the app, in the list). `GET /cycle-counts/{id}/product-bins`
says which ones they are.

**400 — "Creating an empty count (allowEmpty) only applies to one or no product, or to a single bin, with no other bins, zones or categories."** (`errors.allowEmpty`)
(It used to say "to a single product".) `allowEmpty` with more than one product, or with zones, bins or categories.

### Frequently asked questions

**How do I count several products in a single count?**
In the app: Count → **By product** and keep scanning; at the end **Finish count**. On the web: **New count** → **By product** → leave it without a product →

**Create count**, and in the detail scan each product (one that is not in the count opens **Add what was found** with the product filled in).

**Do I have to choose the bin?**
No: if the system has the product in **a single** bin, that one is used (it can be changed). With several you have to choose; with none, scan where you found it.

**Does the open count show the system quantities?**
Not when blind (without `warehouse.count`): the list of where the product is carries no quantities.

**I scanned the same product twice.**
In the same bin and lot the already counted line opens to correct the quantity; it is not added or duplicated. In another bin it is another line.

## Batch 26 — Convert a product to serial (Rentals R0)

Details in [chapter 06 §2.1](06-inventario-y-almacen.md#21-convert-to-serial-batch-26-rentals-r0) and `docs/lote26-decisiones.md`.
Endpoint: `POST /api/v1/products/{publicId}/convert-to-serial` (`inventory.manage` + `inventory.adjust`, module WMS_LOTSERIAL).

### New server messages

**400 — "Enter {n} serial number(s) for {bin} (there are {m})."**
In bin `{bin}` there are `{n}` units on hand and the request brings `{m}` serials (with `{m}` = 0 if the bin was not sent). Enter
exactly one serial per unit in each bin with stock; see the bins in Inventory → Balances filtering by the product.
If `{n}` is 0, you removed from the bin a serial that does not belong: that bin has no stock of the product.

**409 — "Product {sku} has reserved units; release them before converting it."**
Some unit is set aside (unpacked pick batch, cross-dock or another reservation). Finish or delete that pick batch (or wait for
it to be dispatched) and convert again.

**409 — "Product {sku} has open receipts, tasks, pick batches or counts; finish them before converting it."**
Those documents would move the product without serials. Confirm or delete the receipt, complete or cancel the task, pack or delete the
pick batch, and reconcile or delete the count; then convert.

**422 — "Product {sku} is already serial-tracked."**
It is already converted (or was created with serial). There is nothing to do; enter the serials in the movements.

**422 — "Only untracked products are converted to serial; {sku} is lot-tracked."**
The tool only converts products without tracking. A lot-tracked product is not converted.

**422 — "The stock of {sku} in {bin} is {qty}; adjust it to whole units before converting it."**
There is a fraction in that bin (for example 1.5) and half a unit cannot be given a serial. Correct the stock with an adjustment or
a count and convert again.

**422 — "The stock of {sku} in {where} is not in a bin without lot; move or adjust it before converting it."**
There is stock without a bin (only at the warehouse) or recorded with a lot. Move it to a bin (transfer) or correct it with an adjustment
before converting.

**403 — "Missing permission 'inventory.adjust'."**
Converting moves inventory: besides `inventory.manage`, `inventory.adjust` is needed. Ask the administrator to grant it to you.

**400 — "The reason TRACKING_CONVERSION is assigned by the system."**
"Serial conversion" is a reason used only by the conversion tool; a manual adjustment must use another reason.

### Frequently asked questions

**Why can't I change the tracking to "Serial" in the product record?**
Because the product already has movements (409 "The tracking type of a product that already has movements cannot be changed.").
Use **Convert to serial**: it changes the tracking and creates the serials through the movement ledger, in a single operation.

**Does the conversion change the quantities?**
No. In each bin the untracked balance goes out and one unit per serial comes in, with the reason "Serial conversion": on hand and
available stay the same. In the movement ledger you will see those adjustments (net zero).

**Can I undo the conversion?**
No: a product with movements does not go back to "no tracking". If you made a mistake in a serial number, correct it with an adjustment (removal of the
wrong serial and creation of the right one, with a note).

**What happens if it fails halfway?**
Nothing is left half done: the conversion is a single transaction; if something fails (a repeated serial, a bin that does not match) no
movement is written and the tracking does not change.

## Batch 27 — Rentals R1: rental until dispatch, extensions and cancellation

Details in [chapter 11](11-rentas.md) and `docs/lote27-decisiones.md`. Endpoints `/api/v1/rentals` (`rental.view` / `rental.manage` /
`rental.extend`, module `RENTAL_EQUIPMENT` "Rentals").

### New server messages

**400 — "Enter the client location where the equipment will be."**
The rental needs the client location (consignee) where the equipment will stay. Choose one of the client's locations; if it does not
exist, create it in the client record.

**400 — "The location does not belong to the rental's client."**
You chose a location of another client. Pick one of the same client as the rental.

**400 — "The pickup date cannot be earlier than the start date."**
The pickup date (end of the rental) must be the same day as the start or later. Correct one of the two.

**400 — "Only own equipment is rented; {sku} belongs to a client."**
That product is a 3PL client's goods. Only the company's equipment (without an owner) is rented.

**400 — "Product {sku} is not serial-tracked; only equipment with a serial number is rented."**
The product has no serial tracking. Convert it with **Convert to serial** (Batch 26) and add the unit again by its serial.

**400 — "The rate cannot be negative."** / **"Enter the rate amount."** / **"Enter the billing frequency: DAILY, WEEKLY, MONTHLY or ONE_TIME."** / **"Unknown billing frequency: '{x}'. Use DAILY, WEEKLY, MONTHLY or ONE_TIME."**
A unit's rate carries a frequency (daily, weekly, monthly or "Fixed") and an amount of 0 or more. It is only data: it is not charged yet.

**400 — "Enter the rental's client."** / **"Enter the rental's start date."** / **"Enter the pickup date."**
Required data for creation is missing.

**400 — "The rental's client cannot be changed; cancel the rental and create another."**
The client is fixed. If you chose the wrong client, cancel the rental (Draft or Scheduled) and create another.

**400 — "The field '{field}' cannot be modified."**
The number, the status, the agreed pickup date, the dispatch and closing dates and the links to shipment and invoice are not edited.

**400 — "Enter the unit's product."** / **"Enter at least one serial number."** / **"A rental allows at most 200 units."**
Each unit is added with its product and its serial number; a rental holds up to 200 units.

**400 — "The new pickup date must be later than the current one ({yyyy-mm-dd})."**
An extension only moves the date forward. The date in parentheses is the current one: choose a later day.

**400 — "Enter the reason for the extension."** / **"Enter the new pickup date."** / **"The reason allows at most 300 characters."** / **"Enter the unit (lineId) of the rate."**
Every extension carries the new date and the reason (it stays in the log); a new rate indicates which unit it applies to.

**400 — "The contract number allows at most 80 characters."** / **"Notes allow at most 1000 characters."** / **"The estimated transport cost cannot be negative."** / **"Unknown currency: '{code}'."** / **"Days must be 0 or more."**
Shorten the text, use a cost of 0 or more, a currency from the list, or a number of days of 0 or more in the "due soon" filter.

**404 — "Rental not found."** / **"Location not found."** / **"Contact not found."** / **"Line not found."**
The rental, the location, the contact or the unit do not exist in your company (or the unit was already removed from the rental). Reload the record.

**409 — "The client has been deactivated; only its history can be viewed."**
Rentals of a deactivated client are not created, scheduled or dispatched. Reactivate the client or use another one.

**409 — "Serial {s} is already in rental {REN-n}."**
The serial is in another open rental (Draft, Scheduled or On rent). Use another serial or remove the serial from that rental (or cancel it) first.

**409 — "Serial {s} is not available in {bin or warehouse}."**
The serial does not exist, is not Available (reserved, on rent, dispatched, scrapped) or is not in a pickable bin of the source
warehouse (for example, in quarantine or in another warehouse). Look it up in the serial trail and move it to a picking or reserve bin
of the rental's warehouse.

**409 — "One of the serials was just added to another rental; reload and try again."**
Someone else added the same serial to another rental at the same time. Reload and choose another serial.

**409 — "Remove the rental's equipment before changing the source warehouse."**
The units leave from the source warehouse: to change it, first remove the units and add them again from the other warehouse.

**409 — "Warehouse {code} already has a RENT zone that is not a rental zone; change its code to be able to dispatch rentals."** / **"Warehouse {code} already has an EN-RENTA bin outside the On rent zone; change its code to be able to dispatch rentals."**
The first dispatch creates the RENT zone and the EN-RENTA bin; if another zone or another bin already uses those codes, change their code in
Warehouses and dispatch again.

**422 — "Rental {n} was already dispatched; it cannot be modified."**
After dispatch the rental is not edited and its units or rates do not change. The date (and the rate) change with an **extension**.

**422 — "Rental {n} is cancelled; it can only be viewed."**
A cancelled rental is not edited or scheduled. Create another one.

**422 — "The rental has no equipment; add at least one unit."**
Add at least one unit before scheduling or dispatching.

**422 — "Only a Draft rental is scheduled; rental {n} is not."** / **"Only a Scheduled rental is dispatched; schedule rental {n} first."**
The order is Draft → Schedule → Dispatch.

**422 — "Only a Draft or Scheduled rental is cancelled; to end it, record the return."**
A dispatched rental (On rent) ends with its return, which arrives with block R2.

**422 — "Only a Scheduled or On rent rental is extended."**
In Draft change the date with normal editing; a returned or cancelled rental is no longer extended.

**422 — "The rental already has extensions; the pickup date changes with an extension."**
Once there are extensions, the dates are not edited: use another extension to move it.

**422 — "The location has been deactivated; choose another one."**
Reactivate the location in the client record or choose another one.

**422 — "The EN-RENTA bin is inactive; it does not accept inventory movements."**
Someone deactivated the warehouse's EN-RENTA bin. Reactivate it in Bins to be able to dispatch.

**403 — without `rental.view`, `rental.manage` or `rental.extend`** (event `PERMISSION_DENIED`)
Viewing rentals requires `rental.view`; creating, scheduling, dispatching or cancelling them, `rental.manage`; extending them, `rental.extend`. Ask the
administrator for the permission (the Warehouse operator already has them).

### Inventory messages with a serial on rent

**409 — "Serial {s} is not available in EN-RENTA."** / **"Serial {s} is not available in the warehouse (it does not exist, already left or is in another bin or lot)."** / **"Serial {s} is already in inventory."**
The serial is **On rent**: it is not transferred, adjusted, picked or received again through the normal screens. It comes back to the
warehouse with the **rental return** (R2).

### Frequently asked questions

**Why does the product still have the same units on hand after dispatching the rental?**
Because the rented unit still belongs to the company (decision D1): it moves to the EN-RENTA bin and stays reserved. That is why **on
hand** does not change and **available** goes down.

**Where do I see the units that are rented?**
In the **EN-RENTA** bin (RENT zone "On rent") of each warehouse, in the product's serials (status "On rent") and in the rentals list
filtering by status "On rent".

**What is an "overdue" rental?**
A Scheduled or On rent rental whose pickup date has already passed (before today, in the company's time). It is not a status: it is calculated
(`isOverdue`). Use `overdue=true` in the list; "due within N days", `dueWithinDays=N`.

**Is the rate charged?**
Not yet: only the conditions are stored (fixed or by time, amount and currency per unit). Charging will arrive with Billing.

**Do I need someone else to approve an extension?**
No (D4): the `rental.extend` permission is enough. It stays in the log with who, when, the previous and the new date, the reason and the new
rate if it changed.

**I disabled "Scheduled" in the rentals pipeline and now I can't schedule.**
Rental statuses can be renamed and reordered, but not disabled: enable it again (422 `Status '{code}' does not exist or
is not enabled for this company.`).

## Batch 28 — Rentals R2: rental return, processing of the returned unit and cycle count (D7)

Details in [chapter 11](11-rentas.md) (sections 5, 6 and 7), chapter 06 §6.y and `docs/lote28-decisiones.md`. Endpoints
`POST /api/v1/rentals/{publicId}/returns` (`rental.return`), `/api/v1/rental-returns` (`rental.view`) and `/api/v1/rental-processes`
(`rental.view` / `rental.maintenance`; scrapping also `inventory.adjust`), module `RENTAL_EQUIPMENT` "Rentals".

### New server messages

**400 — "With the reason 'Other', describe the return in the notes."**
With the reason "Other" the return notes are required: write what happened (or choose another reason).

**400 — "The destination bin cannot be in the On rent zone."**
The returned unit (or the one finishing its processing) cannot stay in EN-RENTA: choose a picking, reserve or quarantine bin. If you do not
indicate one, the return uses the bin the unit left from.

**400 — "Enter the return reason: END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT or OTHER."** / **"Unknown return reason: '{x}'. Use END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT or OTHER."**
Each return carries a reason: end of contract, early due to damage, early at the client's request or other.

**400 — "Enter at least one serial being returned."** / **"Enter the serial number of the returned unit."** / **"Serial number {s} is repeated."**
Units are returned by their serial number, each one only once per return.

**400 — "Unknown condition: '{x}'. Use GOOD, DAMAGED or INCOMPLETE."**
The unit's condition is Good (default), Damaged or Incomplete.

**400 — "The return date cannot be in the future."** / **"The return date cannot be before the rental start ({yyyy-mm-dd})."**
The date is the day the unit came back (today by default, in the company's time) and cannot be before the rental started.

**400 — "The estimated pickup cost cannot be negative."** / **"Unit notes allow at most 500 characters."**
The pickup cost is data (0 or more); the notes of each unit, up to 500 characters.

**400 — "The destination bin must belong to warehouse {code} of the process."**
When finishing a process the unit is only moved within its warehouse. To take it to another warehouse, finish it and then make a
normal transfer.

**400 — "Enter the status the process moves to."**
"Advance" needs the destination status (for example `CLEANING` or `REPAIR`).

**403 — "Missing permission 'inventory.adjust'."** (when scrapping)
Scrapping takes the unit out of inventory: besides `rental.maintenance`, `inventory.adjust` is needed. Ask the administrator for it or ask
someone who has it to scrap the unit. Without `rental.return` returns are not recorded and without `rental.maintenance` the process is not advanced (403
`PERMISSION_DENIED`).

**404 — "Rental return not found."** / **"Process not found."** / **"Bin not found."**
The return, the process or the bin do not exist in your company. Reload the list.

**409 — "Serial {s} is not on rent in {REN-n}."**
That serial is not a dispatched and unreturned unit of that rental: it belongs to another rental, was already returned or never left. Check the rental
record (returned units show their return date).

**409 — "Serial {s} is on rent ({REN-n}); record its return before reconciling the count."**
A count captured a serial the system has at the client. If the unit really came back, record the return of that rental and
recapture the count; if it was captured by mistake, remove it from the capture and confirm.

**409 — "One of the units was just returned in another operation; reload and try again."**
Someone else recorded the return of the same unit at the same time. Reload the rental.

**422 — "Only the return of an On rent rental is recorded; rental {n} is not."**
A Draft or Scheduled rental has not left yet (cancel it if it is no longer going); a Returned or Cancelled one already ended.

**422 — "The process already ended; it can only be viewed."**
The process is in "Ready" or "Scrapped": it is no longer advanced, finished or scrapped again.

**422 — "Bin {bin} is for rented equipment; it is not counted."**
The EN-RENTA bin (RENT zone) is not counted: what is there is at the clients. Count the other bins; whole-warehouse, by-product and
changed-only counts already skip it on their own.

**422 — "Status '{code}' does not exist or is not enabled for this company."** / **"Illegal jump: from '{from}' you can only advance to '{next}'."**
The step is disabled in the process configuration, or the jump is not allowed: advance to the next enabled step, to Repair
/ Awaiting parts, or finish the process.

### Frequently asked questions

**I returned a unit and the product is still "Unavailable". Why?**
Because it went through processing: it stays "In processing", on hand but reserved, until the process reaches **Ready**. If it did not need review,
next time mark it without processing (`requiresProcess: false`).

**Can I return only one of the units of a rental?**
Yes. Record the return with that serial (for example "Early due to damage"). The rental stays On rent until the last unit comes back;
with that return it becomes **Returned**.

**Can I return a unit to another warehouse?**
Yes: indicate a bin of that warehouse (`toBinId`). What you cannot do is leave it in the On rent zone.

**Who decides whether a unit goes through processing?**
Whoever receives the return, unit by unit (yes by default).

**Can I remove or rename process steps?**
Yes, from the status configuration (`RentalProcessStatus`): rename, reorder or disable Inspection, Cleaning, Testing,
Repair or Awaiting parts. Inventory only changes when reaching **Ready** (it is released) or **Scrapped** (it leaves inventory).

**What happens to the scrapped unit?**
It leaves inventory with an outbound adjustment with the reason "Damage" and the process reference; the serial becomes "Scrapped" and cannot be
received or rented again.

**Does an early return change the rate or the charge?**
No: the rental only stores the conditions (D3). The return marks whether it was early (`isEarly`) for the rental reports.

**Why can't I count the EN-RENTA bin?**
Because what is there is physically at the clients (D7). To correct a unit that actually came back, record its return.

## Batch 29 — Rentals R3: reports, indicators and notice of overdue or due rentals

Details in [chapter 11](11-rentas.md) (section 10) and `docs/lote29-decisiones.md`. Analytics sources `RENTAL`, `RENTAL_RETURN` and
`RENTAL_PROCESS`; views "Units on rent by client", "Rentals due (7 days)", "Overdue rentals", "Rental returns by
reason" and "Units in processing"; indicators "Rentals due (7 days)" and "Overdue rentals"; chart "Rental returns by reason";
notice `RENTAL_DUE` in "Needs your attention". Everything requires `rental.view` and the `RENTAL_EQUIPMENT` "Rentals" module turned on.

### New messages or messages with a new cause

**404 — "Indicator '{id}' not found."** / **"Chart '{id}' not found."** / **"View '{id}' not found."** (for rentals)
The indicator, chart or view uses a rentals source and you do not have `rental.view`, or the **Rentals** module is turned off in the company.
Ask the administrator for the permission, or ask the company administrator to turn the module on (Administration › Modules): when it is turned on
everything that was there comes back.

**404 — "Data source 'RENTAL' not found."** (also `RENTAL_RETURN` or `RENTAL_PROCESS`)
When creating or previewing a view, indicator or chart on a rentals source without `rental.view` or with the Rentals module turned off. Same
remedy as the previous one.

### Frequently asked questions

**Where do I see the rentals that are due this week?**
On the Pulse, in "Needs your attention" (one row per rental overdue or due within 7 days, the most overdue at the top), or in Analytics with the
"Rentals due (7 days)" view. The already overdue ones, in the "Overdue rentals" view.

**Why does a Scheduled rental appear as overdue?**
Because its pickup date has already passed and the equipment is still set aside for it. Dispatch it and extend it, or cancel it if it is no longer going.

**I recorded the return and the rental is still in the notice. Why?**
Because it still has some unit not returned (partial return): the rental stays On rent. Record the return of the rest or
extend the rental.

**Does the notice use Puerto Rico time?**
It uses the day of the company's time zone (Tenant settings); by default, Puerto Rico. A rental picked up today is "due
soon" until the company's midnight, not UTC's.

**I turned off the Rentals module and its reports disappeared. Were they deleted?**
No. With the module off they are hidden; when it is turned on they come back, with the views, indicators and charts the company has created on
rentals.

**Why don't the rental indicators show up on my Pulse?**
They come turned off because "Needs your attention" already shows every overdue or due rental. Turn on "show on Pulse" in the indicator.

**On the web the notice says "RENTAL_DUE" and "Review" does not open anything.**
The Rentals screens are not on the web yet (blocks F-R1 and F-R2). The server already delivers the notice with the rental, the client, the
pickup date and the days; the web will show the text and the rental screen when those blocks arrive.

**Does a return count as "early" if the rental was extended afterwards?**
"Early" compares the return date with the rental's **current** pickup date (today's, with its extensions), just like the
returns list.

## Batch 30 — Rentals RM: product tracking when migrating (Advance Depot serials)

Details in [chapter 10](10-migracion-de-datos.md) (section 6), `docs/migracion/depot-series-y-rentas.md` and `docs/lote30-decisiones.md`.
These are messages from the `import-legacy` command (server command line): the configuration error ends with exit code 1; the
warnings stay in the report and the load continues.

### New messages

**"products.trackingType must be NONE, LOT or SERIAL." (exit code 1)**
The configuration JSON brings in `products.trackingType` a value that is not `NONE`, `LOT` or `SERIAL` (for example `"SERIE"`). No
source was read and nothing was written. Correct the value (upper or lower case does not matter) and repeat.

**"Item {sku} is marked as serial but has an initial balance ({n} units) and the source does not bring its serial numbers; it is created with tracking NONE. Convert it afterwards with "Convert to serial"." (warning)**
With `products.trackingFromColumns.serial` turned on (or `trackingType` set to `SERIAL`), the item asked for serial but has stock to load, and
neither QuickBooks nor the WMS bring the numbers. To avoid leaving "serial" stock without serials, it was created without tracking with its balance. If it is a
rental unit, count the units, write down their serial numbers and use "Convert to serial" (chapter 6 §2.1). It also appears in the mapping
"Candidate for Convert to serial".

**"Item {sku} is marked by lot but has an initial balance ({n} units) and the source does not bring its lots; it is created with tracking NONE." (warning)**
Same as the previous one, with the `Lot` checkbox (option `trackingFromColumns.lot`). There is no "Convert to lot" tool: the product stays without
tracking. For Depot it is not recommended to turn on `lot` (the checkbox comes checked by default on almost every item).

**"Product {sku} already exists with tracking {current}; the migration does not change the tracking of an existing product (in the source it is marked {requested}). …" (warning)**
The migration never changes the tracking of a product that already exists (rule D25), not even with `--update`. If the source asks for SERIAL and the product
is untracked, the message ends with "If it is a unit with a serial number, use "Convert to serial"." and the product stays in the mapping
"Candidate for Convert to serial": that is the way.

### Frequently asked questions

**Can I bring the serial numbers of the Depot units from the migration?**
No: no source has them. QuickBooks only marks per product whether it is "serial" (a checkbox), and the previous WMS does not store serials. The
numbers are entered in Teikem with "Convert to serial", reading them from each unit.

**Is it worth recreating the Depot database so the products end up with serial?**
No. Products with stock would be created without tracking just the same (there are no serials to load) and recreating deletes everything done in the database
(rentals, conversions, adjustments). Use "Convert to serial" on the current database. The importer option only serves to create with serial
the **new products without stock**.

**I turned on `trackingFromColumns.serial`, ran the dry-run and nothing changed.**
That is expected on an already loaded database: every product already exists and does not change. The report leaves you the list "Candidate for Convert to
serial" to work with R0.

**Why did a product that says Serial in QuickBooks end up "No tracking"?**
Because it had stock and there were no serial numbers to load (or because the `trackingFromColumns.serial` option is off, which is the
norm in Depot and Solutions). Convert it with "Convert to serial".

## Batch A4 — Warehouse app: count by product

Details in [chapter 9 §7.1](09-app-almacen.md#71-count-by-product-batch-a4) and in `docs/mobile/loteA4-decisiones.md`. The
messages with an HTTP code come from the server (Batch 21) and the app shows them as is; the rest are from the app.

### New messages

**"This product is counted by serial number; count it from the web for now."**
The product is serial-tracked and the app does not capture serials yet. No count was opened. Count it from the web (Cycle
count) or count its bins with a web user.

**"The system has no stock of this product. If you found it in any bin, use "Another bin"."**
It is no longer an error: the system has no stock of that product in the device's warehouse, and the app opens the count **empty** (the server
no longer answers the 400 "the filters select no inventory" in this case). Tap **Another bin**, choose the zone, type the code
of the bin where you found it (and the lot if it has one), type the quantity and **Confirm**. The bin, if new, stays "pending
review" for the supervisor. If you really did not find it anywhere, tap **Cancel count**.

**"An empty count can't be finished: add the bin where you found it with "Another bin" or cancel the count."**
You tapped Confirm on a count by product that has no rows (product without stock and you did not add any bin). Nothing was
sent. Use **Another bin** to record where you found it or **Cancel count** if you did not find it.

**"Couldn't open the count for that product (needs a signal)."**
Opening the count by product is done online. Find a signal and try again; afterwards, writing down and confirming work without a signal.

**"{n} blank bins count as 0."**
It is not an error: it is the notice of the "blank = 0" rule. If you really found nothing in those bins, tap **Confirm**. If
you forgot to write one down, type the quantity before confirming. Since Batch A5 **at least one** typed quantity is required: with
all of them blank, Confirm warns "Type at least one quantity…" (Batch A5 section).

**"Some quantities aren't numbers; fix them to confirm."**
Some box has something that is not a quantity (it is marked in red). Fix it or clear it (blank = 0); Confirm becomes enabled.

**"No bin matches "…"."**
The search box (it appears with more than 6 bins) did not find that text in the bin codes or in the lots. Clear the text
to see the whole list.

**"Bin … is already on the list: type the quantity there."**
In "Another bin" you typed a bin (and lot) that already has its row on the list. Go back (Cancel) and type the quantity in that
row: sending the same line twice would make the server reject the whole count.

**"A bin with that code already exists in the warehouse." (409) and the button "Use …, which already exists"**
The bin you typed already exists in the warehouse (the device did not have it yet). If it is where you found the product, tap **Use …,
which already exists**: it enters the list as one more row (without "pending review", because it is not new). If you got the code wrong,
fix it.

**"This product is lot-tracked: type the lot number."**
For a new bin of a lot-tracked product, the server needs to know which lot what you found belongs to. Type the lot number
from the label; the expiration is optional.

**"No zones to choose from: sync with a signal and try again." / "No answer from the server: showing the zones saved on the device."**
"Another bin" asks the server for the warehouse zones; without an answer it uses those of the bins already downloaded. If there are none,
sync with a signal. Creating the bin also needs a signal.

**"Couldn't create the bin: it needs a signal. Try again when there is one."**
The new bin is created on the server at that moment. What was already written down on the list is not lost.

**"Enter the bin's code or its aisle/rack/level/position." / "The bin code only allows letters, numbers, hyphen and underscore (max 40)." (400)**
Type the code (for example `A-01-02`) or at least one of its parts, without spaces or symbols.

**"The zone is inactive; reactivate it first." / "The count was already reconciled; it takes no new bins." (422), "Zone not found." / "Count not found." (404)**
Choose another zone; if the count was already reconciled or cancelled on the web while you were counting, it no longer takes bins: tell the supervisor.

### Frequently asked questions

**What is the difference between "By bin" and "By product"?**
By bin: you scan a bin and count everything in it. By product: you scan a product and the app lists all the
bins (and lots) where the system says it is; you write down how much there is in each one. The app remembers the last mode you used.

**What happens to the bins I leave blank?**
They count as 0 (you found nothing there). The line above Confirm says how many there are; on confirmation they are sent as 0. Since Batch
A5 you cannot leave **all** of them blank: type at least one quantity (0 if there is nothing).

**Why don't I see how much the system expects?**
Same as in the count by bin: only someone with the `warehouse.count` permission sees it; with `warehouse.count.capture` the count is
blind.

**I found the product in a bin that is not on the list.**
Tap **Another bin** at the end of the list, choose the zone and type the bin (and the lot if it has one). If the bin did not exist,
it is created "pending review" and the supervisor confirms or corrects it on the web; you keep counting.

**I closed the app (or the device turned off) in the middle of a count by product.**
Open Count: the list appears the same, with what you had already typed, even without a signal. Finish with Confirm or cancel it.

**I opened the count for the wrong product.**
Tap **Cancel count** (it needs a signal and the `warehouse.count` permission). Without that permission, tell the supervisor to cancel it
from the web; do not confirm it at 0.

## Batch F12 — Web: "To review", supervisor correction and count preview

Screen: [F12 — Cycle count by product](frontend/f12-conteo-por-producto.md). The messages with an HTTP code come from the server
(Batch 21, previous section) and the screen shows them as is.

### Error messages seen on the screen

**What do I do with "The count of {sku} in {bin} ({counted}) is less than the reserved quantity ({reserved}); release the reservation before reconciling."? (409)**
In the preview it appears in the Error column of that bin and Confirm stays disabled ("{n} line(s) have errors; fix them
before confirming."). Release the reservation (or correct the quantity if it was miscounted) and open the preview again.

**What does "The record was modified by another user; reload and try again." mean on confirmation? (409)**
Someone changed the count (a correction, a new line) after you opened the preview. The preview recalculates
on its own: review it and confirm again.

**What does "The count was already reconciled; it can only be viewed." mean in the preview? (422)**
Another user confirmed it, or "Close the ones that match" closed it, while you were reviewing it. Close the preview: the count is already
in Matched or Difference.

**What does "The bin is not pending review." mean when confirming a bin? (409)**
It was already confirmed (by you in another tab or by another supervisor). There is nothing else to do.

### Messages seen only on the screen (no HTTP code)

**"No count on this page matches."**: the "Close the ones that match" button is disabled because no visible count is
in "Matches". Change page or filters.

**"Only counts that match can be selected."**: the checkbox of a count with a difference, errors or missing lines cannot be checked.

**"The comment allows at most 500 characters."**: the bulk close comment is longer; shorten it.

**"{n} line(s) have errors; fix them before confirming." / "{n} line(s) left to count." / "The count has no lines."**:
what prevents confirming according to the preview.

**"No line fails: everything matches the current stock."**: with "only the failing ones" nothing is left; turn on "Show all".

### Frequently asked questions

**Why didn't "Close the ones that match" close counts from other pages?**
It closes only the matching ones on the visible page (or the ones you checked), so the notice says exactly how many and which ones are closed.
Go to the next page or raise "Rows per page".

**Does correcting a quantity on the web move inventory?**
No. It is a correction (it stays as "Corrected to Y" with your name and the time); inventory moves only when the count is confirmed, by the
difference against the current stock shown in the preview.

**I corrected a line and it no longer fails, but it is still on the list. Is that an error?**
No: a line corrected in the session stays in view so you can see the result. When the count is opened again it no longer appears in
"only the failing ones".

**Why does Variance say −2 and Adjustment say +1?**
Variance compares with the snapshot (what there was when the count was created); Adjustment compares with the **current** stock, which is what
will be posted. If someone moved inventory in that bin after the snapshot, the two numbers differ (the preview marks "Balance
changed").

**Why don't I see the "To review" tab or the preview?**
Your user counts blind (does not have `warehouse.count`). Ask a supervisor to review and confirm.

**Can I create a count by product from the web?**
Yes (Batch F13): **New count** → **By product** tab → warehouse and product → **Create count**. It creates the count with one line per
bin and lot with stock (origin Product) and opens it. It can also be done from the warehouse app ("Count by product").

**Why can't I find a product in "New count > By product"?**
The **Only with stock** switch starts on and the list only brings the products with stock on hand in the chosen warehouse
(`GET /api/v1/products?warehousePublicId=…&onlyOnHand=true`). Turn it off to see all active products; if you choose one without stock,
the server answers 400 `The filters select no on-hand inventory to count; widen the filters or add lines by hand.` next to the
selector. The switch does not save its state: when the modal is reopened it starts on again.

**"The filters select no on-hand inventory to count; widen the filters or add lines by hand." when creating by product (400)**
The product has no stock on hand in that warehouse. The message appears under the product selector and the modal stays open. Choose
another warehouse or product, or receive it first. The web does not open empty counts; to count something the system thinks does not exist use
"Count by product" in the warehouse app.

**"Choose the product." when pressing Create count on the By product tab**
You still have to choose the product from the list (typing the text is not enough). Nothing was sent to the server.

**"The line was already corrected by the supervisor; it cannot be captured again." (409)**
Someone with only `warehouse.count.capture` (for example the operator who counted the line) tried to change a line a supervisor already
corrected. The correction was not touched. What to do: ask the supervisor (whoever corrected it, or someone with `warehouse.count`) to
correct it again from the web. In the app, the operation is marked "rejected" with this message and is not retried on its own. In the batch
capture it **only** answers 409 when ALL the lines of the batch are corrected: the message adds `Row(s) of the batch: n (SKU)` and `Nothing
was saved.` (it is true: nothing was saved). If the batch also brought free lines, there is no error: it answers 200, the free ones are saved and the
response brings `skippedLines` with the skipped ones (`reasonCode` `CORRECTED_BY_SUPERVISOR`, what was sent and the supervisor's value). Resending the same value the line already has gives no error. In the warehouse app (Batch A6), Sync
explains it in large type and offers "Refresh the count": see the **Batch A6** section at the end of this page.

## Batch A5 — Warehouse app: at least one quantity when counting by product; in Dispatch, the quantity first

Owner's decisions 4 and 5 of 2026-10-03 (`docs/decisiones-del-dueno-2026-10-03.md`). Details in
[chapter 9](09-app-almacen.md) (§6 Dispatch, §7.1 Count by product) and in `docs/mobile/loteA5-decisiones.md`. None of these
messages comes from the server (they have no HTTP code): they are from the app and nothing is sent.

### New messages

**"Type at least one quantity. If there is none of this product, type 0 in one bin."**
You tapped **Confirm** on a count by product with **all** the bins blank. Nothing was sent and the count stays open. What
to do: type what you found in each bin; if there really is none of that product in any of them, type **0** in one bin (the
other blank ones count as 0) and tap Confirm. An "Another bin" row with its quantity also counts. If you opened the wrong
product, use **Cancel count** (or tell the supervisor if you do not have `warehouse.count`).

**"Type the quantity first, then scan the bin."**
In **Dispatch** you scanned the bin the product comes from without having typed the quantity. No line was added (not even with 1).
What to do: type the quantity (the cursor is already there) and scan the bin again: the line is added instantly with the green
notice "Added: …".

**"The quantity must be a number greater than 0. Fix it and scan the bin again."**
In **Dispatch** the typed quantity is 0, negative or not a number. Nothing was added. Fix the quantity and scan the
bin again. Comma or period are accepted for decimals.

### Frequently asked questions

**Why can't I confirm a count by product with everything blank anymore?**
So that a count is not closed at 0 by an accidental tap (for example, when opening a product by mistake). If there really is nothing,
typing a 0 leaves a record that it was counted.

**In Dispatch, where did the "Add" button go?**
It is no longer needed: with the quantity typed, scanning the bin adds the line. If you type the bin by hand (⌨ button), **OK**
does the same as scanning.

**I scanned the bin, the quantity notice came up and the bin no longer appears. Do I have to scan it again?**
Yes: the bin is not saved until the line is added, so that a line is never left half done. Type the quantity and scan
it again.

## Batch A6 — Warehouse app: count capture rejected because the supervisor already corrected a line

Pending from change 2 of the owner's decisions of 2026-10-03 (`docs/decisiones-del-dueno-2026-10-03.md`). Details in
[chapter 9 §9.1](09-app-almacen.md) and in `docs/mobile/loteA6-decisiones.md`.

### New messages

**"The rest of the lines of your count were saved." (app, no HTTP code; Batch A7)**
Sync shows it when the server answered 200 to the capture batch but skipped the lines the supervisor had already corrected
(`skippedLines`). The other lines were saved and the submission was marked as sent; it is not an error. Below are the ones not saved:
`• SKU · bin (you sent x → the supervisor left y)`. What to do: nothing with those lines (the supervisor already has their value); if something
is still to be counted, create a new count or ask the supervisor to review it. Tap **Refresh the count** to see where it stands and **Dismiss this notice**
once you have read it (the notice does not go away on its own or when the app is closed).

**"The line was already corrected by the supervisor; it cannot be captured again. Row(s) of the batch: n (SKU). Nothing was saved." (409, in the app)**
Now it only happens when **all** the lines of the batch the device sent were already corrected by the supervisor. In the app, **Sync → With
errors** shows instead the red card `The supervisor already corrected all the lines of this submission.` with `No line of this submission was saved because the supervisor already corrected all of them.` and the rows (`• Row 2: SKU-1 (you sent 6)`). What to do: `If something is still missing, create a new count (scan the position again) or ask the supervisor to review it.` The app does not reopen a count already sent.
Before that, tap **Refresh the count** to see where it stands. When you are done, tap **Discard this submission** (and also discard the closing of the
same count if it ended with an error).

**"The supervisor already corrected all the lines of this submission."** (app, no HTTP code)
It is the title of that card (it used to say "a line of this count"): see the previous message.

**"Closing this same count also failed: the count was not finished from this device."** (app)
Behind the rejected batch the queue sent the closing of the count, and the server rejected it too (422 `{n} line(s) left
to count.` if the count was still Pending, because the batch quantities were not saved, or `The count was already finished; you can correct the
capture or reconcile it.` if it was already Counted). The closing changed nothing on the server. What to do: the same as above;
the closing row is discarded separately with its own "Discard".

**"No signal: the count could not be refreshed. Try again when there is a connection."** (app)
You tapped **Refresh the count** without a signal (or the server did not answer). Nothing changed; tap it again when there is a connection.

**"Count {number} is still open (Pending)."** (app)
Result of **Refresh the count**: the count can still be captured. Below is the summary (`{total} lines: {c} corrected
by the supervisor, {s} not counted.`) and each line with `Counted: n`, `Not counted` or `Corrected by the supervisor (name) · Counted: n`.
Corrected lines are not captured again; ask the supervisor to capture or review the rest.

**"Count {number} was already finished (Counted): ask the supervisor to review it."** (app)
The count is already Counted (someone finished it). From the app there is nothing else to do: the supervisor reviews and reconciles it on the web.

**"Count {number} was already reconciled: it takes no more captures. You can discard this submission."** (app)
The count was already closed and posted; what this device sent can no longer get in. Discard the submission. If you think something was missing, tell the
supervisor to open a new count.

**"The count no longer exists (it was deleted). You can discard this submission."** (app; the server answered 404 `Count not found.`)
Someone deleted the count (or it does not belong to this company). Discard the submission.

**"Could not tell which count this submission belongs to; ask the supervisor to review it."** (app)
The queue row does not have the expected route (it should not happen). There is no refresh button; ask the supervisor to review it.

**"What the server said:"** (app)
If the list of rows in the message does not have the expected format (for example, a SKU with parentheses), the card does not try
to interpret it and shows the server's message as is.

### Frequently asked questions

**Was what I counted lost?**
No. Since Batch A7 the lines the supervisor had not corrected are saved; only the corrected ones are skipped, and the notice tells you which
(with what you sent and what the supervisor left). In the residual 409 case (all corrected) no line of that
submission was saved, but what you counted stays on the card until you discard it.

**Why weren't the lines the supervisor corrected saved?**
To protect the correction: only whoever corrected it or someone with `warehouse.count` can change it again. The other lines of the same
batch are saved (owner's decision, second block, 2026-10-03; before, the whole batch was rejected).

**Can I resume a count I already sent?**
No: the owner decided the app does not have that function. If something is still to be counted, create a new count (scan the bin again) or tell the
supervisor.

**Does "Retry" help?**
It resends exactly the same batch: it fails the same way as long as the supervisor's correction is still there. It would only work if the supervisor
removed their correction or left it with the same number you sent.

**Does "Refresh the count" show me the expected quantities?**
No. Only the current counted quantity and who corrected it, never what the system expected (same as the blind count).

## Batch F14 — Web: product and bin barcodes for counting

Screen: [F14 — Barcodes for counting](frontend/f14-codigos-de-barras.md). The reports are built in the browser: there are no
new server messages (those for reading products and bins are the usual ones).

### Notices seen in the report or on the screen (no HTTP code)

**"The report could not be generated. Try again."** (toast)
Reading the list or building the PDF failed. Try again; if it happens again, tell support with the time and the filters used.

**"The report includes only the first {n} products (read limit). Refine the filters to see the rest."** / **"…the first {n} bins…"**
There is no longer a row limit: all the elements of the filter are read. The message only appears if the server stopped returning rows before the total; generate it again. If the PDF is huge, you can print in parts: by category,
zone, aisle or text.

**"{n} columns were used because some long barcodes do not fit narrower."** / **"A single column was used…"**
Some code does not fit in 3 (or 2) columns without going below 0.25 mm per thin bar. It is informational; to get 3 columns, leave out the
long codes with the filters.

**"Too long for a readable barcode in the page width ({n}); listed without a barcode: …"** and the box **"Does not fit: too long for a readable barcode"**
The value is so long that it does not print legibly even at the full width of the page. That element appears in its group but without a barcode: type the
value by hand on the scanner.

**"Left out because they contain characters the barcode (Code 128) does not support ({n}): VALUE (not supported: É)"**
The value has accents, ñ, tabs or other characters outside printable ASCII (the ones that cannot be seen are written as `U+0009`).
It does not appear in the report; type the value by hand or change the bin code.

**"Some values have no barcode (too long or left out): see the notices at the end of the report."**
Refers to the two previous notices, which go on the last page.

**"No product matches the filters." / "No bin matches the filters."**
The screen filter leaves nothing; change the filters.

### Frequently asked questions

**What does a product's barcode carry: its barcode or its SKU?**
The **exact SKU**. The scanner app looks up the product by barcode or by SKU, and the SKU is unique and always exists.

**Why does the report bring products that have no stock or are inactive?**
Because the report is exactly what the table filters. Use the current's indicators (e.g. *Total units* = active with stock)
or the filters to narrow it down.

**How are bins grouped?**
By the first number in the code (the first sequence of digits), compared as a number: 2 before 10, and 01 and 1 in the same group.
Codes without digits go to "Other bins" at the end.

**How many fit per page?**
With short codes, about 24 bins (3 columns × 8 rows); products with a long description take up a bit more. With
"2 columns", fewer per page but wider barcodes.

**The scanner does not read the printed sheet.**
Print at 100% ("Actual size", without "Fit to page"), in black on white paper, and try with "2 columns".

## Products by bin (report; replaces the bin sheets of Batch 23 and F15)

Details in [chapter 06 §1.5](06-inventario-y-almacen.md#15-products-by-bin-report), the screen in
[F15 — Products by bin](frontend/f15-productos-por-posicion.md) and the decisions in `docs/lote24-decisiones.md`. The PDF is built in the browser.

### Server messages

**400 — "At most 200 bins can be requested per query; use skip to request the next ones."** (`errors.take`)
`GET .../bin-products` with `take` greater than 200. Request 200 at a time: `take=200&skip=0`, then `skip=200`, `skip=400`… until reaching `total`.
The screen never requests more than 200, so it should not be seen.

**404 — "Warehouse not found."** (`GET .../bin-products`)
The warehouse does not exist or belongs to another company.

### Messages seen on the screen (no HTTP code of their own)

**"That is {n}: the PDF will be large and may take a moment. All of them are printed; there is no limit."** (notice, not an error)
Since 2026-10-10 there is **no longer a limit** on bins per print run: the notice only says the PDF will be large. If you prefer to print fewer, filter by zone, by **Aisle** or by text in **Bin**, or check boxes.

**What does the "Products by bin" report look like now?**
On a letter page, like "Barcodes": several cells per page, one group per bin with its title and the barcode of each product below.

**"There are no bins in your choice."**
The chosen option has no bins (e.g. *The checked bins (0)*). Choose another option or change the filters.

**"There is nothing to print: the chosen bins have no products. Turn on "Include empty bins" to print them."**
All the chosen bins are empty and the switch is off: no PDF is generated. Turn it on if you want a *No products* page per bin.

**"Printing cancelled."**
You pressed **Cancel** while the bins were being read. Nothing was downloaded.

**"The products-by-bin PDF could not be generated. {message}"**
Reading or building the PDF failed. If the server answered (e.g. 404 **"Warehouse not found."**), its message appears at the end. Try
again; if it happens again, tell support with the time and what you chose.

**"{n} page(s) were generated from {m} bin(s)."** (plus *"{k} bin(s) without products were not printed."* and *"{j} product(s) without a readable
barcode: see the notice at the foot of their page."*)
Everything went well. The skipped empty ones are those that had no products with the switch off.

**On the page: "No barcode: Code 128 does not support Ñ"** / **"Does not fit: too long for a readable barcode"**
Neither the product's barcode nor its SKU can be printed as Code 128 (accents, ñ…) or they are so long that they do not fit legibly. The product appears
with its SKU and name (no bars) and a notice at the foot of that page lists it; look it up by hand on the scanner.

### Frequently asked questions

**What is it for?**
To print the products of a bin (or of a whole aisle) with their barcode and scan them from the paper, especially on high racks
(up to 30 feet) where the product cannot be reached.

**Which barcode does each product carry?**
The **product's barcode** (the one in its record). If it has none, the **SKU**. The scanner app looks up the product by both.

**How do I print only one aisle?**
Type the aisle in the **Aisle** filter (or in **Bin**), press **Products by bin** → *The bins in the current filter* → **Generate PDF**.

**Does the paper go out of date?**
The system does not track it (there is no longer a "sheet" state): the PDF shows what there was when it was generated. If you move goods, generate it again.

**Does it include quantities?**
No: it says which products there are (SKU, name and barcode), not how many.

**I have more than 10 products in a bin.**
The bin comes out on several pages of 10 (11 products = 10 + 1), each with the bin header and *Sheet 2 of 2*. Stick them together.

**Are the checkbox marks lost when changing page?**
No: they are kept when changing page or filter as long as you do not change warehouse. **Clear marks** erases them.

**Why does the table export bring more rows than the page?**

**Export** outputs **everything filtered** (with no row limit), not just the visible page.

## Batch F16 — Web: bin labels in Bins

Screen: [F16 — Bin labels](frontend/f16-etiquetas-de-posicion.md). The server did not change: the bin list is read
(`GET /api/v1/warehouses/{warehouse}/bins`) and the PDF is built in the browser. If the server answers with an error, its message appears as is at
the end of *"The labels could not be generated."*.

### Messages seen on the screen (no HTTP code of their own)

**"That is {n}: the PDF will be large and may take a moment. All of them are printed; there is no limit."** (notice, not an error)
Since 2026-10-10 there is no longer a limit of 500 labels per PDF: all the ones you request are printed. **Generate PDF** stays enabled.

**"There are no bins in your choice."**
The chosen option has no bins (e.g. *The bins in the current filter (0)*). Change the filters or check bins.

**"There are no locations to print with your choice."**
When reading, there were no bins anymore (someone deactivated them in the meantime). Reload the list and try again. No PDF is generated.

**"Cancelled; the PDF was not generated."**
You pressed **Cancel** while the bins were being read. Nothing was downloaded.

**"The labels could not be generated. {message}"**
Reading or building the PDF failed. If the server answered (e.g. 404 **"Warehouse not found."** or a 400 from a filter), its
message appears at the end. Try again; if it happens again, tell support with the time and what you chose.

**"{n} labels of 4 × 2 inches were generated."** (or *"1 label of … was generated"*)
Everything went well; the PDF was downloaded with one page per bin.

**"{n} came out without a barcode (code in text only):"** followed by *"Too long for a readable barcode in the page
width…"* and/or *"Left out because they contain characters the barcode (Code 128) does not support…"*
The PDF was already downloaded; those labels carry the code in text and, in place of the bars, *"Does not fit: too long for a readable
barcode"* (more than ~30 characters in 4 inches with the minimum bar of 0.25 mm) or *"No barcode: Code 128 does not support Ñ"*. If
you need a barcode, rename the bin with a shorter code or one without accents. Press **Done** to close.

### Frequently asked questions

**How is it different from "Products by bin"?**
The **label** identifies the bin (its code) and does not change with what is in it. **Products by bin** (F15) lists the products
each bin has, with their barcode, to scan them from the paper.

**How do I reprint a label?**
Same as the first time: check it (or filter) → **Location labels** → **Generate PDF**. There is nothing to "uncheck" first.

**Which size do I choose?**
The one of your printer's roll: 4 × 2 inches (10 × 5 cm), 4 × 4 (10 × 10 cm) or 4 × 6 (10 × 15 cm). Each PDF page measures exactly
one label of that size. Set the same size in the driver and print at 100%.

**The label comes out sideways or cut off.**
Check that the driver's label size is the same one you chose and that the scale is 100% ("Actual size"). If it persists, generate the PDF with

**Orientation → Rotate 90°** (it rotates the whole page, for printers that feed the label sideways).

**Why don't all the bins of the warehouse come out?**
The ones in the table's **current filter** (all the filters, including the **Bin** text) or the **checked** ones come out. Remove the filters
(**Clear**) for all of them (no limit).

**Are the checks removed when printing labels?**
No: they stay in case you want to reprint. **Clear marks** erases them.

**Does it remember the size I used?**
Yes, in that browser: next time the modal opens with the last size and the last orientation.

## Batch A8 — Warehouse app: what is in a bin (Lookup)

Details in [chapter 9 §8.1](09-app-almacen.md#81-what-is-in-a-bin-batch-a8) and in `docs/mobile/loteA8-decisiones.md`. None of
these messages carries an HTTP code of its own: the app builds them (if the server answers an error, its message appears as is).

### New or changed messages

**"No product or bin has that code."** (it used to say "Nothing has that code.")
What was scanned is not a synced product, is not a bin of the device's warehouse and the free-text search (SKU, name, barcode,
lot) found nothing. What to do: check that the label belongs to this warehouse (the device looks up its default warehouse) and that
it was read completely; if it is a new product, sync with a signal and scan again.

**"There are no products in this bin."**
The bin exists and the system has nothing in it (no stock on hand or reserved). It is not an error. If you see product there,
tell the supervisor or count it (Count → By bin).

**"Bin {bin} is deactivated."**
The bin exists but is deactivated. Scan another one; if it should be active, tell the supervisor (it is reactivated on the web, Bins tab of the
warehouse record).

**"No product matches “{text}”."**
What you typed in **Search product or lot** does not match the SKU, name or lot of any product on the list. Clear or change the
text.

**"Showing the first 1,000 rows of this bin; see the rest on the web."**
The bin has more than 1,000 balance rows (product × lot): a very rare case. The list shows the first ones; see the complete
bin on the web (Inventory, Balances tab, filtering by the bin).

### Frequently asked questions

**I scan the bin and see no quantities. Is something wrong?**
No: only someone with the `warehouse.count` permission sees the system quantities (the same rule as the blind count). If your job
needs them, ask the administrator to grant it; the app picks it up the next time you open Lookup with a signal.

**I was granted (or lost) `warehouse.count` and the app is still the same.**
The app asks for the permissions when opening **Lookup** with a signal. Go out to Home and enter Lookup again with a signal.

**The list does not match the sheet stuck on the rack.**
The list is what the system says **now**; the sheet is from when it was printed. If the sheet is old, ask for it to be reprinted (web, Warehouse →
Bins → **Print the outdated ones**). If what you see physically does not match the list, count the bin.

**I tap a product on the list and nothing happens.**
That is on purpose: Lookup only displays. To see that product across the whole warehouse, scan (or type with ⌨) its code.

**Does it work without a signal?**
Only with a bin you already looked up before on the device: it shows that list with the notice "Data from {time} ({N} min ago, no signal
now)". The first time needs a signal.

## Batch A9 — Warehouse app: small screen (PIN, on-screen keyboard and calculator)

Details in [chapter 9 §2.2 and §7.3](09-app-almacen.md#73-quantity-calculator-2026-10-05) and in `docs/mobile/loteA9-decisiones.md`. This batch

**does not add or change messages**: it only changes how the screens look and move. The only new text is the name of the calculator's back button
for the screen reader (**"Direct quantity"**, the same text the link had).

**On the PIN screen I can't see "Back" and "Sign in" completely.**
Swipe the screen up: the PIN screen scrolls. On short devices the keys shrink so everything fits; if it still does not
fit (for example with a very large system font), swipe.

**I tap "Loose units" (or any field) with the on-screen keyboard and the keyboard covers it.**
The screen should move up on its own until the field is above the keyboard. If it does not, swipe the screen up with the keyboard open: the
space at the bottom lets you bring the field up. If it always happens on your device, tell support with the model and the Android version (it is a
checklist item of the batch).

**Where did "Direct quantity" go in the calculator?**
It is now the button with the **← arrow**, top left, next to the "Calculator" title. It does the same: it goes back to the quantity field with
the total already filled in. In the single-row calculator (count of one product in all its bins) the arrow closes without changing the row; to
pass the total use **Use {total}**.

**The single-row calculator no longer opens in a window.**
That is on purpose: it now takes up the screen in the same place (like "Another bin"), so the keyboard does not cover the fields. When done
(**Use {total}**, **Cancel** or the ← arrow) it goes back to the list.

**I can't find the calculator button.**
It is the **blue** square with the **white** drawing of a calculator, to the right of the quantity field (and on each row of the count of one
product in all its bins).

## Batch F17 — Web: Rentals (list, record, creation with units by serial, extension) and "Convert to serial"

Screen: [F17 — Rentals (web) and "Convert to serial"](frontend/f17-rentas.md). The server did not change; its messages (chapter 11 §9 and
chapter 06 §2.1) are shown **as is**, under their field or above the form or the dialog. To avoid waiting for the server, the web checks
the following before sending, with **the same text**.

### Messages the web checks before sending (same text as the server)

**"Enter the rental's client."** / **"Enter the client location where the equipment will be."** / **"Enter the rental's start
date."** / **"Enter the pickup date."** (400 from the server)
That data is missing in **New rental**. The location is chosen after the client (only its own locations).

**"The pickup date cannot be earlier than the start date."** (400)
Correct the pickup date (it can be the same day as the start).

**"The estimated transport cost cannot be negative."** / **"The contract number allows at most 80 characters."** / **"Notes
allow at most 1000 characters."** (400)
Correct the marked field.

**"Product {sku} is not serial-tracked; only equipment with a serial number is rented."** (400)
The chosen unit has no serial tracking. Convert it with **Convert to serial** in the product record and choose it again.

**"Only own equipment is rented; {sku} belongs to a client."** (400)
The product belongs to a 3PL client; only the company's equipment is rented.

**"Enter the billing frequency: DAILY, WEEKLY, MONTHLY or ONE_TIME."** / **"Enter the rate amount."** / **"The rate cannot be
negative."** (400)
The rate is half done: choose the frequency (Daily, Weekly, Monthly or Fixed) and an amount of 0 or more, or leave both empty (no rate).

**"The new pickup date must be later than the current one ({yyyy-mm-dd})."** / **"Enter the reason for the extension."** / **"The reason
allows at most 300 characters."** (400)
When you **Extend**: the new date must be later than the current pickup date and the reason is required.

**"Enter {n} serial number(s) for {bin} (there are {m})."** (400)
In **Convert to serial**, the box of that bin has {m} serials and the bin has {n} units on hand. Type one serial per unit.

**"Serial number {s} is repeated."** (400) / **"Serial number {s} exceeds 80 characters."** (400) / **"A line allows at
most 500 serial numbers."** (400) / **"Notes allow at most 300 characters."** (400)
Correct the serials (they are not repeated, neither across bins nor with different capitalization) or the note.

**"Product {sku} has reserved units; release them before converting it."** (409)
It appears when opening **Convert to serial** if some bin has reserved stock. Pack or delete the pick batch (or whatever is reserving) and try
again.

**"The stock of {sku} in {bin} is not in a bin without lot; move or adjust it before converting it."** / **"The stock of
{sku} in {bin} is {qty}; adjust it to whole units before converting it."** (422)
The stock cannot be converted like that: move it to a bin without lot or adjust it to whole units.

### Web-only messages

**"Choose the source warehouse."**
New rental without a source warehouse: the web asks for it to offer the available serials of that warehouse.

**"Serial {s} is already in this rental."**
You scanned (or typed and pressed Enter) a serial you already chose or that is already in the rental.

**"Serial {s} is not available in {warehouse}."**
The scanned serial is not available in the source warehouse (it does not exist, is reserved, on rent, in quarantine or in another warehouse). It is the
same text as the server's 409.

**"A rental allows at most 200 units."**
Adding those units would exceed the limit of 200 per rental.

**"Choose at least one unit for the new rate."**
In **Extend** you set a new rate and removed all the units from "Units with the new rate".

**"The comment allows at most 500 characters."** / **"Enter a valid number."**
Shorten the confirmation comment or correct the transport cost.

**"Your user cannot read locations (locations.read permission and Lists module)."** / **"Your user cannot read the client's
contacts (clients.read permission)."** / **"Your user cannot view clients."**
To create a rental you need to read clients and their locations (Lists module). Ask the administrator for those permissions.

### Frequently asked questions

**Where are the rentals on the web?**
Warehouse → **Rentals** (before the Ledger), with the Rentals module turned on and `rental.view`.

**Why won't it let me choose a unit?**
The selector offers only **own serial-tracked** products that have available stock in the **source warehouse**, and of those the serials

**available** in zones that are rented (not quarantine or cross-dock) that are not already in the rental. If the product has no serial,
convert it first.

**Is "Overdue" a status?**
No: it is a value calculated with the company's day. The list shows it in the **Due** column and it is filtered with "Due within (days)" and
"Overdue only".

**When scheduling or dispatching an error appears. What do I do?**
The dialog stays open with the server's exact message (chapter 11 §9): for example, *"The rental has no equipment; add at least
one unit."* (add units) or *"Serial {s} is not available in {bin}."* (the serial was moved or reserved: remove it and add another).

**Does "Convert to serial" change my inventory?**
Not in quantities: it is a **net-zero movement** (outbound of the untracked units and one inbound per serial, with the reason "Serial
conversion"). What changes is that the product, from then on, is serial-tracked.

## Batch F18 — Web: rental returns, equipment processing and rental reports

Screen: [F18 — Rental returns, equipment processing and rental reports](frontend/f18-devoluciones-y-proceso-de-rentas.md). The
server did not change; its messages (chapter 11 §9, "Return, processing and count") are shown **as is**, under their field or above the
form or the dialog, which does not close.

### Messages the web checks before sending (same text as the server)

**"Enter the return reason: END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT or OTHER."** (400)
Choose the **Reason** in Register return (End of contract, Early due to damage, Early at the client's request or Other).

**"With the reason 'Other', describe the return in the notes."** (400)
With the reason Other, write in **Notes** what happened.

**"The return date cannot be in the future."** / **"The return date cannot be before the rental start ({yyyy-mm-dd})."** (400)
Correct the **Return date** (today by default, in the company's day).

**"The estimated pickup cost cannot be negative."** (400)
Leave the cost empty or at 0 or more.

**"Enter at least one serial being returned."** (400)
Check at least one unit in **Units coming back**.

**"Notes allow at most 1000 characters."** / **"Unit notes allow at most 500 characters."** (400)
Shorten the return notes or the unit notes.

**"Enter the status the process moves to."** (400)
In **Advance**, choose the status in "Moves to".

### Server messages seen on these screens

**"Serial {s} is not on rent in {REN-n}."** (409)
The unit was already returned (someone else recorded it) or does not belong to that rental. Close the dialog: the record reloads with what is left to return.

**"One of the units was just returned in another operation; reload and try again."** / **"The record was modified by another
user; reload and try again."** (409)
Someone changed the rental or the process at the same time. Close the dialog, reload and try again.

**"The destination bin cannot be in the On rent zone."** (400)
Choose another destination bin (the web does not offer the On rent zone; it can appear if the EN-RENTA code is typed).

**"The destination bin must belong to warehouse {code} of the process."** (400)
When you **Complete**, the move is only within the process's warehouse; to take it to another warehouse use a transfer afterwards.

**"Illegal jump: from '{from}' you can only advance to '{next}'."** / **"From the side step '{x}' you can only go back to '{step}' or
advance to '{next}'."** / **"Status '{code}' does not exist or is not enabled for this company."** (422)
In **Advance**, choose the corresponding step (the one marked "(next)", or Repair / Awaiting parts).

**"The process already ended; it can only be viewed."** (422)
The process reached Ready or Scrapped (perhaps in another tab). Reload the queue: the row stays dimmed, with History only.

**"Missing permission 'inventory.adjust'."** (403)
Scrapping is also an inventory adjustment. Ask the administrator for `inventory.adjust`.

**"Only the return of an On rent rental is recorded; rental {n} is not."** (422)
The rental is already Returned (or was never dispatched). Reload the record.

### Web-only messages

**"Type the serial {s} to confirm the scrap."**
In **Scrap**, type the unit's serial (case does not matter) to confirm: the scrap cannot be undone.

**"Enter a valid number."**
The pickup cost is not a number.

**"Scrapping also requires the inventory.adjust permission (inventory adjustments), which your user does not have: ask an administrator."**
Note in the process queue when your role can advance and complete (`rental.maintenance`) but not scrap.

### Frequently asked questions

**Where do I record that a rented unit came back?**
In the rental record (On rent) → **Register return**. Do not use a normal return receipt: the unit never left inventory.

**Can I return only one unit?**
Yes: uncheck the others. The rental stays On rent until the last one comes back.

**I returned a unit and it does not show as available.**
It went through processing. In **Equipment processing** advance its steps and finish it with **Complete** (it becomes Ready and available).

**Where do I see which units have been under review the longest?**
In **Equipment processing** sort by "Days in processing", or in Rental reports the "Units in processing" view.

**Do the rental indicators show up on the Pulse?**
They come turned off (the "Needs your attention" notice already shows every overdue or due rental). Turn them on in Analytics → Indicators
with "Show on my Pulse"; in the meantime they are seen in **Rental reports**.

## Putaway split across several bins (task 24)

**I put away 9 bins of 20 and had 5 loose left. Where are they?**
If you scanned a tenth bin, that one received the 5 (sticky alert `{bin} gets only 5 (what was left), not 20.`). If you confirmed with only the 9, the 5 are a
new putaway task in the Putaway list: open it and put it away in a bin.

**It tells me `No units left to put away: all 185 are split. Confirm the split.` when scanning one more bin.**
That is expected: with 185 and 20 per bin, 9 of 20 fit plus a tenth with 5; there is nothing left to split. Confirm the split.

**A sticky alert appeared at the top, what do I do?**
It is the notice that this bin receives only what was left (for example 5 and not 20). It stays in view even if you scroll the screen; close it with the ✕ once you have read it.

**I scanned a wrong bin in the split.**
Tap the **✕** on that bin's row (`Remove {bin}`); the quantities of the others are recalculated. Nothing has moved until **Confirm split**.

**I confirmed the split and one bin belonged to another warehouse (`Bin not found.`).**
Nothing was moved: the split is all or nothing. Scan the right bins and confirm it again.

**In the direct receipt I split 45 in 20s and had 5 left unplaced.**
Scan a third bin: it receives the 5 (with the sticky alert). If you confirmed with only 2, the capture continues with `5` and the bin field: scan where they go
(or tap Cancel to discard them from this receipt; the 2 lines already added stay).

**Splitting on a receipt with an ASN shows me the bin conflict.**
With an ASN or purchase order each line goes into a single bin (`{sku} was already captured with destination {bin}; on a receipt with an ASN or purchase order each line goes to a single bin.`). To split a product across several bins use a blind or return receipt.

**In Dispatch I asked for 50 and I get "20 from P-01 and 30 from R-02". Can I change where I take it from?**
Yes: tap **Change** on the row and scan the bin you will take it from. It is accepted if it has stock of that product; if not, it says
`{bin} does not have {qty} available of this product ({have} there). Scan another bin.`

**In Dispatch I don't get the outbound plan.**
It only appears when the quantity does not fit in one bin and the product is **not** lot-tracked (with a lot the bin is dictated by the expiration). Without a signal and without
a copy of the outbound order on the device there is no suggestion either.

## Search the list (task 26)

**I tap "Search the list" and it says `No matches on this device. Sync if something is missing.`**
The search box looks at what the device has already downloaded. If the product or the bin is new, tap **Sync now** on Home and search again; if they do not exist in
the system, they are created on the web.

**Is choosing from the list the same as scanning?**
Yes: it delivers the code to the field just like a scan and goes through the same validations (for example, in Dispatch with a lot-tracked product the bin is still
the one of the lot that expires first).

## Informed count when capturing (task 25)

**I count in the app and it says `It does not match what was expected. Count again and accept again.` How much was expected?**
It is not said on purpose: that way the recount is a real count. Count again and accept; on the second attempt the line closes and, if the company shows the number, you will see `You counted {n} and {m} was expected`.

**It says `That line was already verified; it cannot be changed or removed.`**
The line already matched or was already recounted. Your figure is fixed; if there is a real error, the supervisor corrects it on the web (Cycle count → To review).

**The server answers `The quantity is not the one verified on the line; it cannot be changed after seeing the result.` when sending the count.**
An attempt was made to save a quantity different from the one verified. Close that count and tell the supervisor; only someone with the Count permission can correct the line.

**No verification appears when I count.**
Check (1) that there is a signal, (2) that the company is not set to “Nobody” (System → Settings → Operations), (3) that your user is marked “Yes” in Users if the company is set to “Only marked users” (or is not set to “No” if it is set to “Everybody”) and (4) that you do not have the Count permission (the supervisor sees the expected quantity from the start). Without a signal or without permission the app captures without verifying.

**I get `Seeing the expected quantity while counting is not enabled.`**
The company or your user does not have seeing the expected quantity enabled. Ask the administrator to mark it in System → Users (“Sees expected while counting: Yes”) if appropriate.

**How do I make every counter see the result when capturing?**
System → Settings → Operations → “Who sees the expected quantity while counting?” = **Everybody**, and no counter should have the Count permission (only “Capture count”). For a group: **Only marked users** and “Yes” for those people.

**I typed a margin and it does not accept it (`The recount margin must be between 0 and 100%.`).**
The margin is a percentage from 0 to 100 with up to 2 decimals.

**I am a supervisor and with the company set to “Nobody” the app makes me count blind.**
That is expected: “Nobody” closes the expected quantity while counting in the app for everyone, including those with the Count permission (for example, during an audit). To reconcile, the web still shows the expected quantity. Change the setting in System → Settings → Operations if you want the supervisor to see it.

**In the split I get `Room for 15: it will receive 20. You can confirm anyway.`**
That bin has a maximum capacity configured (Locations) and, with what it already has, only 15 fit; the split assigns it 20. It is a notice: you can confirm anyway (the capacity does not block). If you do not want to go over, remove that bin with its ✕ and use another, or lower the quantity per bin. Without a signal or without a configured capacity the notice is not shown.

## Removing lines (receipt and dispatch)

**I added the wrong product on a receipt (or a dispatch) in the app. How do I remove it?**
In the list of lines, tap **Remove** next to that line and confirm with **Remove** in the `Remove this line?` prompt. The other lines do not change. It applies while the receipt or the dispatch is still **open on the device**; once confirmed/sent it can no longer be edited from the app.

**The receipt was already confirmed and something wrong was received.**
It cannot be undone from the app. On the web, an open receipt without a document (blind or return) lets you remove a line with the trash can; lines that come from the ASN or purchase order **are not removed** (0 is captured as received) and extra ones are (`Remove an extra line (open only)`). An already confirmed receipt is corrected with an inventory adjustment.

## Quantity calculator: depth and web count (2026-10-07)

**What is the calculator's “Depth”?**
How many there are one behind another. A stack of 5 rows × 3 columns with 2 deep is 5 × 3 × 2 = 30. It is optional: blank means 1. For different stacks use **+ another block**, each with its own depth.

**The calculator says `Enter whole numbers in rows, columns and depth (depth 1 or more) and a number in loose.`**
Rows, columns and depth only accept whole numbers (depth, 1 or more); loose units can have decimals. Check that there are no letters, decimals or a 0 in depth.

**Where is the calculator in the web count?**
Cycle count → choose the count → on each line the **∑** button (or “Calculator” inside the Counted quantity window). Serial-tracked lines are counted by a list of serial numbers and do not have it.

## Split by bin on the web (2026-10-07)

**How do I split 185 units in 20s on the web?**
Putaway: when completing the task turn on “Split by bin”, type 20 and choose the bins (9 full and a tenth with 5). On an open direct receipt use **Split** on the line. One extra bin receives the remainder and no more are accepted.

**I get `With {per} per bin, {max} bin(s) fit for {pending}; there are no more units to split.`**
You chose more bins than fit. Remove the extra rows (✕) or raise the quantity per bin.

**In the dispatch it says `Pick the product and enter the quantity to suggest where to take it from.`**
The **Suggest bins** action needs a product and quantity on the line and the bin to be empty.

**It says `Not enough stock: {short} short. Lower the quantity.`**
The available stock of the warehouse (and of the lot, if you indicated one) is less than what was requested; check the balance or lower the quantity.

**The receipt split was left half done.**
It is saved line by line; check the receipt lines, correct the one that failed and repeat **Split** on the rest.

## Session, lock and companies in the app (2026-10-07)

**I opened the app and it asks for the PIN even though I had already signed in.**
That is on purpose: when opening the app the PIN of the user who left the session is always requested (so whoever comes next does not use your session). What was captured is not lost. If it is not your user, tap “Sign out”.

**What is the padlock for?**
It locks instantly without signing out: to leave the device for a moment. With the PIN it continues on the same Home screen.

**A company stayed on the list and gives an error; I can't remove it.**
If the server no longer recognizes the device (`The device is not enrolled or was deactivated.`, 401), the app removes it on its own when trying to sign in. If you prefer to remove it before that, swipe its row to the left → “Remove”.

**When removing a company it says it has unsynced captures.**
Those captures live only on this phone; if you confirm, they are lost. Cancel, sync with a signal (“Sync now”) and remove it again.

## Suggested bin and bin list in the app (2026-10-07)

**I tapped “Suggested: X” and the line was not added.**
That is on purpose: it puts the bin in the field and enables **OK**; when you tap OK (or scan the bin) the line is added. Typing the quantity first is required in Dispatch.

**It says `Not enough in this one: N short. Mark more positions below.`**
The quantity is greater than what is in (Dispatch) or fits in (Receiving) the suggested bin. In the list below mark the bins; the “Taken X of Y” counter must reach the total to use the button.

**It won't let me mark another bin.**
The total has already been taken (Taken X of X). Unmark one or raise the quantity.

**I changed the quantity and the marks changed.**
That is expected: the marks are trimmed so as not to exceed the total, starting with those at the end of the list.

**Why doesn't the list appear for a lot-tracked product?**
With a lot the bin is the one of the lot that expires first and it is not chosen.

## Cycle count in the app: resume, finish with missing lines and open counts (2026-10-07)

**I scanned the same bin again and another count did not open.**
That is on purpose: if that bin already has an open count of yours (or unassigned) it is **resumed** with what you already counted (`You resumed count CC-… for …: n of m already counted.`). Before, a new one opened each time.

**`{Name} is counting that bin ({CC-#####}).`**
Someone else has the count of that bin open. Have them finish or save it, or have a supervisor delete it or reassign it on the web (Cycle count).

**Why do I see the complete list of count lines on the web and not only what I counted?**
A count by bin brings **all** the lines the system expects in that bin; what you counted appears as “Counted” and the rest as “Pending”.

**I tapped “Finish this bin” and it asked me “N product(s) still to count”.**
The count only closes with all lines counted. Choose **Keep counting**, **Save and continue later** (the count stays open to resume it) or **Leave at 0 and finish** (what is missing counts as 0 and remains as a difference for the supervisor).

**I saved and continued later, but now it won't let me do anything else.**
That is on purpose: while you have an open count, the other actions warn `Finish or cancel the count in progress before using this.` In **Count → Open counts** tap **Continue** to finish it (or cancel it if you have `warehouse.count`).

**`Count CC-… (…) is still open. Finish or save it before counting anything else.`**
You are trying to open another bin or “By product” with an open count. Resume that one (scanning its same bin also resumes it) and finish it.

**An open count appears on the list but no longer exists / I can't continue it.**
If a supervisor deleted it, tapping **Continue** shows `Count not found.` and it is removed from the list.

**`The server did not accept finishing the count: …`**
The closing was rejected on the server (for example, a line already corrected by the supervisor). The message says the reason; it also stays in Sync.

**“By product”: the bin where I already counted that product no longer appears.**
That is on purpose: only the bins you have not counted yet are offered. To correct an already counted quantity, tap ✎ on its line in the list. To count the same product in another bin, tap the product on the list (it goes into the scan field) and press OK.

## Sender name of the e-mails (2026-10-08)

**Why does the e-mail say “Advance Solutions” and not “Teikem”?** (and where the “via Teikem” that Gmail shows comes from)
The sender name is that of the company the e-mail belongs to (the application adds nothing else; the “via Teikem” that Gmail puts next to it is added by Gmail because the domain that signs the e-mail is not that of the sender address, see below): the first sign-in verification code uses the company being signed into; “Forgot your password” uses the
user's company **if they belong to only one**; the “two-step verification reset” notice uses the company of the administrator who reset it.

**A user who is in several companies receives the e-mail as “Teikem”.**
That is on purpose: without knowing which one to choose, the general name is used (`Brevo__FromName`).

**Can the e-mail address be changed per company?**
No: there is a single address (`Brevo__FromEmail`). Changing it per company would require authenticating a domain for each one in Brevo.

**Gmail shows “via …” next to the sender.**
It is the domain authentication (DKIM/SPF) in Brevo, not the name. It is fixed by adding the sender domain's records in the DNS (see `deploy/README.md`).

## Recovering the password (2026-10-07)

**I forgot my password.**
On the login tap “Forgot your password?”, type your e-mail and use the link you receive (valid for 60 minutes, one time only). If it does not arrive, check your spam; for security the screen answers the same even if the e-mail does not exist.

**It says `The link is not valid or has expired. Ask for a new one.`**
The link was already used, an hour went by, another link was requested afterwards (the latest invalidates the previous one) or it was copied incomplete. Ask for a new one with “Ask for another link”.

**It says `Too many attempts; wait a minute and try again.`**
The limit is 5 requests per minute from the same connection. Wait a minute.

**An administrator set a temporary password for me; how long does it last?**
10 minutes from when it was set. Sign in with it and the system will ask you to choose your own. If the 10 minutes go by without signing in, your usual password is valid again.

**It says `You cannot set a temporary password for yourself.`**
An administrator cannot set the temporary password for themselves; to change your own use My account, or ask another administrator to set it.

**The temporary password was handed over but more than 10 minutes have passed.**
Set it again (System → Users → Temporary password). It is a new one and counts another 10 minutes.

**Why doesn't the temporary password arrive by e-mail?**
That is on purpose: the administrator hands it over in person. The e-mail with a link is only for “Forgot your password”.

## Cancelling a count with capture-only permission (2026-10-08)

**I removed `warehouse.count` from a counter so they would not see the expected quantity and they could no longer cancel.**
Fixed: cancelling requires the capture permission (`warehouse.count.capture`), the same as capturing and finishing. `warehouse.count` is no longer needed.

**It says `Only an open count is deleted; this one was already finished.`**
A finished count is no longer cancelled; the supervisor reconciles it from the web.

## Several warehouses on the same device (2026-10-07)

**Can I use the same device in more than one warehouse of the same company?**
Yes. On Home tap **“Change”** (next to “Warehouse: …”) and choose the warehouse. Every screen switches to working with that warehouse. Each company
enrolled on the phone remembers its own.

**I don't see the “Change” button.**
It only appears if the company has more than one active warehouse and the user has `inventory.view`. If it is a new device, open Home with signal
once so the list of warehouses downloads.

**“Finish or cancel the document in progress before changing warehouse.”**
There is an open receipt, dispatch or count, and that document belongs to your warehouse. Finish or cancel it and change afterwards.

**I changed warehouse and it went back to the previous one.**
The device's default warehouse was changed from the web (the web wins) or the chosen warehouse was deactivated. Choose it again.

**Receive asks me for another mode (direct or with putaway) after changing.**
That is normal: the receiving mode belongs to each warehouse (Warehouses → receiving mode on the web).

## Damage: arrived damaged or damaged in the warehouse (2026-10-08)

**How do I record something that arrived damaged in a receipt?**
The easiest way: **on the receipt line itself**. In the app, on the received-quantity screen check **Some units arrived damaged**, enter how many and the reason (and, if it is *Other*, write it); then say where they are left (the suggested bin is the quarantine one) or tap **Release**. On the web, open receipt record → **Damaged units** icon on the row. Damaged units count within what was received (100 received, 10 damaged → 90 good). When the receipt is confirmed the `DAN-#####` is created and the damaged units leave the good stock. You can also report it afterwards: web (receipt record → **Report damage**, or Products & inventory → Damage tab) or app (Home → Damage → “Arrived damaged in a receipt”); there, receive only the good units.

**It says `The damaged quantity cannot exceed the received quantity.`**
Damaged units are part of what was received: you entered more damaged than received, or lowered the received quantity below what was already declared as damaged. Correct one of the two quantities.

**It says `Write the reason for the damage when the cause is Other.`**
You chose the reason *Other*: write in the box what happened. With the other reasons it is not needed.

**What happens to the damaged units if the receipt is “with putaway”?**
They enter the receiving bin with the rest and, on confirmation, leave it toward the bin you indicated (the quarantine one by default) or are discarded. The putaway task only carries the good units. If there is no quarantine bin and you did not indicate another one, they stay in the receiving bin but are no longer put away: that is damage waiting for a decision.

**Are damaged units counted in the cycle count? Can they be sold?**
They are **counted** (they are physical stock of that bin) but **cannot be dispatched**: in a Quarantine zone they are already out of allocation and, if you left them in another bin (storage, receiving), the system **reserves** them and lowers the available quantity; the Damage list says “reserved (not dispatched)”. When they are discarded or recovered the reservation is released.

**They are coming to pick up the damaged goods from quarantine. What do I do?**
On the Damage tab use **Release** on that damage and choose the **final destination** (in the app, when releasing damaged units while receiving or from the Damage tile, it also asks **Where does it go?**): *Thrown away*, *Returned to supplier*, *Donated* or *Sold as salvage*. It is not a dispatch: it lowers inventory with an adjustment of reason Damage and the destination stays in the movement ledger and in the list. If you need another destination, add it in System → Lists (Final destination of damaged goods).

**It says `Say where the goods that leave go (thrown away, returned to supplier, donated…).`**
When releasing what is in quarantine you have to choose the final destination. Pick it from the list and release again.

**Can I leave damaged units in a bin that is not a quarantine bin?**
Yes, any active bin of the warehouse (in the app: scan it or choose from the list; on the web: “Bin where it stays”). The damage stays “In quarantine” in that bin until you discard or recover it from the Damage tab.

**And if it was damaged in the warehouse?**
Report it with origin “Damaged in the warehouse” and the bin where it was. Quarantine moves it to the quarantine bin; releasing it lowers inventory with an adjustment of reason Damage.

**It says `The warehouse has no active quarantine bin; create one in a zone of type Quarantine.`**
The zone is missing. In Warehouse → Bins create a zone of type Quarantine with at least one active bin, or unblock the existing one. (When reporting damage, or declaring it in a receipt, you can also indicate any other active bin.)

**It says `Insufficient inventory of {sku} in {bin}: available {x}, requested {y}.`**
You reported more units than are available in that bin (reserved units do not count). Check the quantity or the bin.

**It says `Only a damage that is in quarantine can be resolved.`**
That damage was already discarded or recovered (or discarded right away). It is not resolved twice.

**It says `Serial-tracked products are not reported here yet; use an inventory adjustment with reason Damage.`**
For now serial-tracked products do not go through Damage: use Transfers & adjustments → Adjust → Lower, reason Damage, with the serial.

**Where do I see how much has been damaged and why?**
In Products & inventory → **Damage** tab (filters by status, origin and search). For charts or indicators use the “Damage” data source in Views & reports.

**I sent it to quarantine by mistake.**
On the Damage tab, **Recover** (choose a storage bin): the stock comes back and the damage is left as Recovered.

**Why doesn't the Damage tile show in the app?**
Your user does not have the “Report and resolve damage” permission (`warehouse.damage`). Ask the administrator for it (System → Roles & users).

## Product labels (Products & inventory, 2026-10-09)

Details in [Product labels](frontend/f17b-etiquetas-de-producto.md).

**How do I print adhesive labels for my products?**
Products & inventory → filter the table → **Product labels** → size (4 × 2, 4 × 4 or 4 × 6) → **Generate PDF**. You get one label per product with the SKU barcode, the SKU in large type and the name.

**Is there a maximum of product labels per PDF?**
No (since 2026-10-10): all the products of the filter are printed. With many, the PDF takes longer to generate.

**It says `There are no products to print with the current filters.`**
The filter returns no products: remove or change the filters.

**It says `{n} came out without a barcode (code in text only):`**
Those SKUs have characters that the Code 128 barcode does not accept or are too long for the size: the label comes out with the SKU as text. Choose a larger size or correct the SKU.

**The label comes out sideways or cut off.**
Check that the driver's size is the same one you chose and the scale is 100%; if it persists, generate the PDF with **Orientation → Rotate 90°**.

## Product packaging (2026-10-09)

Details in [Product packaging](06-inventario-y-almacen.md#product-packaging-2026-10-09).

**Is inventory kept in boxes?**
No. Always in the product's base unit. The packaging (Box of 12) only helps to count and read quantities.

**How do I count by boxes?**
Define the product's packaging (Edit product → Pack and Units per pack). In the count or Receive calculator, choose **Box** in the block, or use **Loose boxes**; the total comes out in units.

**It says `Units per pack must be a number greater than 0 (up to 3 decimals).`**
Enter a number greater than 0 in *Units per pack*.

**It says `Enter the pack unit (box, barrel, bundle…).`**
You entered the units per pack but did not choose the pack: choose it or clear the quantity.

**It says `Enter how many units the pack holds.`**
You chose the pack but not how many units it holds: enter the quantity (Box = 12).

**It says `The pack unit cannot be the same as the base unit.`**
The pack must be different from the base unit (e.g. base Gallon, pack Barrel).

**It says `Unknown pack unit: '{code}'.`**
That unit does not exist in the *Unit of measure* list. Add it to the list or choose another.

**I don't see the Box selector in the app's calculator.**
The product has no packaging or the app has not synced since the update: sync and retry.

## Transfer from the device (2026-10-10)

Details in [Transfer](09-app-almacen.md#transfer-2026-10-10).

**I don't see the Transfer button (nor “Move” in Lookup).**
Your user does not have the `warehouse.transfer` permission. The *Warehouse operator* and anyone who can already adjust inventory have it; if you use a custom role, ask the administrator to add it (System → Roles).

**It says `Only {qty} can be moved (reserved stock does not move).`**
Part of what is in that bin is reserved for a dispatch. Move only what is available or wait until it is dispatched.

**It says `{bin} is a quarantine, rental or cross-dock bin: it cannot be transferred from here…`**
Those bins have their own flow: damaged goods with *Damage*, rented goods with Rentals and cross-dock with its own screen.

**It says `From the device you can only transfer within the same warehouse.`**
To move between warehouses use the web (Transfers & adjustments).

**It says `{sku} is serial-tracked: for now it is transferred from the web.`**
Serial-tracked products are moved from the web (Transfers & adjustments), choosing the serials.

**It says `Transfer needs a signal…`**
The transfer is done on the server so the stock can be confirmed: retry with signal.

## Adjust quantity from the device (2026-10-10)

Details in [Adjust quantity](09-app-almacen.md#adjust-quantity-2026-10-10).

**I don't see “Adjust” in Lookup.**
Your user does not have the `warehouse.adjust` permission. No role template includes it: the administrator creates a role with that permission (System → Roles) and assigns it.

**Can an adjustment be used to move goods from one bin to another?**
No. The adjustment only changes the quantity of **one** bin. To move, use *Transfer* (or *Move* in Lookup).

**It says `Write a note explaining the adjustment.`**
The note is mandatory in every manual adjustment: explain the reason in a few words.

**It says `Can only be lowered by up to {qty} (reserved stock does not leave).`**
Part of what is there is reserved for a dispatch: only what is available can be lowered.

**It says `{bin} is a quarantine, rental or cross-dock bin: it cannot be adjusted from here…`**
Those bins have their own flow (Damage, Rentals, Cross-dock).

## Weak signal (app, 2026-10-10)

**Why does Lookup show me data and say “Refreshing…”?**
Because the device first shows what it already has stored and, in parallel, asks the server. When the answer arrives, the screen refreshes by itself and the indicator changes to “✓ Up to date”.

**It says `No connection or weak signal: showing the device's data.`**
The server did not answer in time. What you see is the last thing the device synced (the time is below). Move to an area with better signal or tap **Sync now** on Home.

**It says `Device data from {time} ({n} min ago)`.**
These are balances stored on the device, not confirmed right now with the server. To operate with certainty wait for the “✓ Up to date”.

**Nothing shows when I scan, but the product exists.**
If the device has never downloaded the balances of that warehouse, it waits for the server as before. Sync once with good signal (Home → **Sync now**) and next time it will show instantly.

**Can I transfer, adjust or report damage without signal?**
Yes. The operation is saved on the device (“Saved on the device. It will be sent as soon as there is signal.”), the balance the device shows already reflects it and it uploads by itself in the next sync. See how many are left to send in Home → Sync.

**It says `The server did not accept the operation`.**
The server checked the operation when it received it and could not apply it (for example, the stock was no longer there because someone else moved it, or the bin changed zone). The device undid the movement in its balances. Go into **Sync**, read the reason and **retry** (if it is now possible) or **discard**.

**I made a transfer without signal and the balance on another device did not change.**
That is normal: the other device will see it when the operation reaches the server and that device syncs. Until then two devices may show different figures.

## Bins and Products & inventory (2026-10-10)

**What is “3 products” in the Product column of Bins?**
It is a link: the bin has more than one product. Tapping it opens the **Products in {bin}** window with SKU, product, lot, on hand and available.

**I exported Bins and a bin appears several times.**
That is on purpose: a bin with more than one product comes out as one line per product (SKU column, product and what that product has in that bin).

**How do I see which products are in a bin from Products & inventory?**
Use the **Bin** filter (type part of the code, for example `A-01`): the products that have stock in some bin whose code **contains** that text are shown. With the filter on, **On hand, Reserved and Available are ONLY those of the matching bins** (if the text matches several bins, their sum), and the panel counter says how many products are there. Without the filter, the quantities are the product totals. The filter also goes in “Applied filters” of the reports and of Export; it does not apply to the Adjustment report.

## Device: I don't see Transfer or Adjust (2026-10-10)

**I installed the APK and the menu does not show Transfer (or Lookup does not show Move / Adjust).**

**Since 2026-10-11 “Transfer” is no longer in the main menu: you transfer with “Move” in Lookup** (same screen and same permission). **“Adjust” is in the menu** (and also in Lookup). The buttons depend on the user's permissions, not on the APK version: **Move** requires `warehouse.transfer` (included in “Warehouse operator” and “Company admin”; `inventory.adjust` implies it) and **Adjust** requires `warehouse.adjust` (only “Company admin”; for an operator a custom role is created in System → Roles with that permission). **If the user already has the permission (e.g. “Company admin” with all of them), most likely the installed APK is an old build:** since 2026-10-10 Home shows at the bottom “Version 1.0.0 (code) · stamp” (date and commit of the APK; in CI, `ci-<number> <commit>`). If that line does not show, the APK is older: install the new one. Check (1) that the **API is already deployed** with the updated database (`db-update`), (2) that the user's role has the permission and (3) opening the app with signal once: the device reads the permissions from the server when it opens the screen. The 2026-10-10 (b) fix in `Diseño/logistica-db-update.sql` adds the two permissions to the “Warehouse operator” and “Company admin” roles that already existed in each company (they were left out before because the seeder only propagates the codes that are new in its run).

## Cycle count: automatic closure and keep counting (2026-10-10)

**I finished a count on the device and I'm still on the Count screen, not on Home. Is it an error?**
No. Since 2026-10-10 the app stays in Count to count the next bin (or product) with the cursor already in the scan field. To leave, tap **Back**. “Save and continue later” does go back to Home.

**I finished a count that matches and it is still in “To review” (Counted), not in Matched.**
Automatic closure is **off by default**. An administrator (`admin.tenant`) turns it on in System → Settings → Operations → “Close matching counts automatically”. In the meantime use “Close the ones that match”. If the setting is on and it still stayed Counted, it **did not match** when finished: it had a variance, a line below the reserved quantity, or the stock changed at that very moment; open it with the reconciliation preview to see the reason.

**Did an automatically closed count adjust inventory?**
No. It only closes by itself if there is nothing to adjust; the history says `Automatic closure: the count matches.`.

## Batch F-A1 — Clients: list, create and record (2026-10-10)

Messages of the Lists → Clients screen (the server ones appear as is, below the field or in the dialog). Chapter: [fa1-clientes.md](frontend/fa1-clientes.md).

**`A client with that code already exists.` (409) when creating.**
The duplicate is by **code** (not by name). Enter another code or leave it empty so it is generated from the name (adds `-2`, `-3`… if needed). Check with **Show inactive**: a deactivated client keeps its code.

**`The name is required.` / `Cannot exceed 200 characters.` (400).**
The name is the only required field when creating. Legal name accepts 250 and the tax ID 50.

**`The code cannot exceed 30 characters.` (400).**
Shorten it or leave it empty.

**`The credit limit cannot be negative.` (400) / `The credit limit cannot be left empty; enter 0 if it does not apply.` (on screen).**
Use 0 or more. Once the client has a limit, the field cannot be left empty (the server does not know how to clear it; empty would mean “do not change”).

**`The title cannot exceed 200 characters.` (400) / `A contract with that number already exists.` (409) when creating with an initial contract.**
The initial contract title accepts 200 characters (empty = “Master contract”). If the contract 409 appears, repeat the creation; if it persists, notify support (the number is built from the client code).

**`The record was modified by another user; reload and try again.` (409) when saving the profile.**
Someone else saved the same client first. The record reloads by itself and what you typed is kept: review and press **Save** again.

**`The pickup point must be an active address of the client: the physical, the postal or a pickup point.` (400).**
In the **Addresses** panel, “Picked up at” only offers the valid ones; if the point was removed in the meantime, reopen the record. “The physical address” is the usual one.

**`Invalid phone.` / `Invalid email.` / `The value is required.` (400).**
The phone has the digits of the company's mask (e.g. 10 in Puerto Rico); the email, `usuario@dominio.com`.

**`The contact name is required.` (400), `Cannot exceed 150 characters.` (name) / `Cannot exceed 80 characters.` (role).**
Enter the person's name; the role is optional.

**`There is already an active primary contact for this client.` (409).**
Two people tried to become primary at the same time. Repeat: marking a person as primary removes the mark from the previous one.

**`The pattern is required.` / `The pattern cannot exceed 40 characters.` / `The pattern must include at least one '#' for the sequence.` / `Character not allowed in the pattern: 'x'. Use letters, digits and - _ / . # @.` (400).**
A valid pattern has 1 to 40 characters, at least one `#` and only unaccented letters, digits and `- _ / . # @`. To go back to the system one leave the field empty. The same reason is shown in red under the pattern as you type.

**`Illegal jump: from 'X' you can only advance to 'Y'.` / `The record is already in that status.` / `'X' is terminal: it allows no more transitions.` (422) when changing the status.**
The status follows the company's model (Settings → Operations). Use the button the record offers; from a terminal status there is no advancing.

**`Client not found.` (404).**
The client in the address bar (`?client=`) does not exist or belongs to another company. Choose one from the list.

**“The name cannot be changed from here”.**
That is on purpose: the name is shown but the profile does not edit it (the server does not allow it in the profile).

**Why don't I see New client, Save, Deactivate or Add phone?**
Permissions are missing: creating requires `clients.create`; editing, status and deactivation require `clients.update`; phones and emails require `contacts.manage` **and** `clients.update`. With only `clients.read` the record is read-only.

**I deactivated a client and it is no longer in the list.**
The list shows only active ones. Turn on **Show inactive**, open it and use **Reactivate**. A deactivated client cannot be chosen in new orders, but its history is kept.

**Where do I change a client's address, SLA or “Client since”?**
Addresses are managed in the **Addresses** panel of the client record. The SLA (by service type, in hours) and “Client since” (contract start) belong to the contract and will be edited in the contracts block.

**Is the “Active/Inactive” state of the mockup the status?**
They are two different things: the **status** (configurable stages, e.g. Active → Under review) and the **deactivation** (Inactive). The list shows both.

## Batch F-A2 — Client contracts (2026-10-10)

Messages of the Contracts section of the client record (Lists → Clients). The server ones appear as is, below the field or in the dialog. Chapter: [fa2-contratos.md](frontend/fa2-contratos.md).

**`The contract was modified by another user; reload and try again.` (409) when saving the contract data.**
Someone else saved the same contract first. The contract reloads by itself and what you typed is kept: review and press **Save** again.

**`The current status does not allow the action 'EDIT_CONTRACT'.` (422) / notice “The contract's current status does not allow editing…”.**
An Expired or Cancelled contract is no longer edited (data, billing model, SLA, rates, tiers or special services). It can only be viewed and its history remains. If you need to keep working, create a new contract (**New contract**); the administrator can change that rule in System → Statuses.

**`The end date cannot be earlier than the start date.` (400) / `Enter an end date or mark it for clearing, not both.` (400).**
Correct “Client since” or “End date”. To remove the end date leave the field empty and save.

**`The contract title is required.` / `Maximum 200 characters.` (400).**
Enter a title of up to 200 characters. The number accepts 40.

**`A contract with the number 'X' already exists.` (409) when creating a contract.**
Contract numbers are unique in the company. Enter another or leave the number empty so the system generates it (`{Código}-C{n}`).

**`The client has been deactivated; only its history can be viewed.` (409) when creating contracts, rates or special services.**
Reactivate the client (**Reactivate** button in the header) and repeat.

**`List Currency 'X' not found.` / `List BillingModel 'X' not found.` (404).**
The currency or the billing trigger is no longer in the company's list; choose another from the list.

**`The dispatch fee cannot be negative.` / `The fixed COD fee cannot be negative.` / `The COD fee percentage must be between 0 and 100.` (400).**
Use 0 or more; if the type is **Percent (%)** the value goes from 0 to 100 and is calculated on the COD amount collected, not on the order total. On screen the amount is also required when the switch is checked.

**I unchecked a switch of the billing model and the amount/rates are still there.**
That is on purpose: unchecking does not delete anything (dispatch fee, COD, rates and special services are kept and stop being charged). When you check it again they reappear.

**`The contract cannot be activated: the client is suspended. Reactivate the client first.` (422).**
Change the client's status (record header) and repeat.

**`The client already has a current contract. Cancel or expire the previous contract before activating this one.` (422).**
Only one contract can be In effect at a time. Choose the previous one in the selector, move it to Cancelled or Expired and activate the new one.

**`Maximum transit hours must be greater than zero.` / `The pickup window (minutes) cannot be negative.` / `The on-time target must be between 0 and 100.` / `The penalty cannot be negative.` (400).**
They appear under the field of the service type row. For a type without SLA leave its four fields empty.

**`Unknown service type: 'X'.` (400) / `There can only be one active service level per service type.` (409) when saving the SLA.**
The type is no longer in the list, or two people saved at the same time: reload the record and repeat.

**`There is already a rate in effect on that date for that service and package type in the contract; edit it or close it before creating another.` (409).**
There can only be one rate in effect per service and package. Use **Edit** (new version) or **Remove** (close) on the existing one.

**`The 'Per service' component is off in the contract's billing model; turn it on before working on its rates.` (409) (and the “Extra piece” one).**
Check the switch on the Contract tab. While it is off the rows are visible but cannot be created or edited.

**`The date cannot be earlier than today: the rate history is not rewritten.` (400).**
A new version or a closure is dated today or later. Only creating a new rate accepts a past date.

**`The rate is already closed; create a new one instead of editing it.` / `The component is already closed.` / `The tier is already closed.` (409).**
Someone else closed it first. Reopen the tab (or turn on **Show history**) and add a new one if needed.

**`Invalid range: 'from' must be at least 2 …` (400) / `Tier 4–7 overlaps the current tier 2–5.` (400).**
Piece 1 goes in the per-service rate, so the first tier starts at 2. An empty “To” means “onwards”. Two current tiers of the same service and package cannot share pieces: edit or remove the existing one.

**`The extra piece component is closed; create a new one to add tiers.` (409) / `Tiers only apply to extra piece components.` (400).**
Add a new extra piece (service + package) and its tiers.

**`Only per-service rates are edited here; extra piece tiers are edited in /tiers.` (400).**
In extra piece the tiers are edited (pencil of each tier), not the block.

**`The client has no current contract; create or activate a contract before setting up special services.` (409).**
Special services hang from the current contract. Create the contract (**New contract**) or move one to In effect.

**`The 'Special services' component is off in the current contract; turn it on in the billing model to add special services.` (409).**
Check “Special services” on the Contract tab of the current contract.

**`The special service type name is required.` / `…cannot exceed 120 characters.` (400) / `A special service type with the name 'X' already exists.` (409).**
When choosing “+ New special service type…” enter a name of up to 120 characters. Names are compared without case, accents or double spaces: “Vagón de la plataforma” and “VAGON DEL MUELLE” are the same type (if it already exists, choose it from the list).

**`The client already has a rate in effect on that date for type 'X'; edit that rate or close it before adding another.` (409) / `The rate is already closed; add a new special service if you need to charge it again.` (409).**
Use **Edit** (new version) or **Remove** on the current row; if it was already closed, add the service again.

**`The special service rate cannot be negative.` (400) / `The special service type is inactive; reactivate it or choose another.` (400).**
Use 0 or more. Inactive types are not offered in the list; this screen does not reactivate them (support does it with the API).

**Why do I only see a notice in Contracts and not the tabs?**
The `contracts.read` permission is missing. The rest of the record works the same. Saving requires `contracts.update` and creating contracts `contracts.create`.

**Why don't I see the Extra piece rates (or per-service rates) section?**
It is shown only if its billing model switch is checked (or if it already has rows). Check it on the Contract tab.

**Where is an order quoted?**
Not on this screen; quoting will be done together with sales orders.

**What happens with “Client since” from the creation?**
It is the contract start date; it is edited on the Contract tab. The current contract is the active one (or the most recent draft if there is no active one) whose start has already arrived.

## Manual issue (DMA-#####, 2026-10-11)

**What is a manual issue and when do I use it?**
It is a stock out **without delivery**: a sample, internal use, a client who comes to pick up, a counter sale. It carries a mandatory
reason, a free note, a `DMA-#####` number and a viewable record (chapter 06 §7b). If the goods go to a consignee with an order,
use Picking & packing (§7). Do not use “Adjust”: an adjustment corrects inventory; this is a real stock out.

**`Enter the reason for the manual issue.` (400).**
Choose a reason (`reasonCode`): Sample, Internal use, Customer pickup, Sale or Other (or the name your company gave them).

**`The reason {CODE} does not exist or is inactive.` (400).**
The code is not in the list, or your company disabled that reason in System → Lists (“Manual issue reason”). Use one from
`GET /api/v1/manual-issues/reasons`, or ask the administrator to enable it again.

**`The note allows at most 500 characters.` (400).**
Shorten the note.

**`A manual issue can only have products from the same owner.` (400).**
An issue is of own inventory **or** of a single client. Make one issue per owner.

**`Insufficient inventory of {sku} in {warehouse}: available {x}, requested {y}.` (409).**
There was not enough available stock for everything requested. Nothing left and the `DMA` number was not consumed: lower the quantity or choose another
bin and try again.

**`A manual issue with that number already exists; try again.` (409).**
Two issues took the number at the same time (very rare). Repeat the operation.

**`Manual issue {n} is not packed: it is a stock out without delivery.` (422).**
A manual issue is never converted into an order. If it had to be delivered, delete it (it returns the inventory) and make a pick batch
(§7) with its packing.

**`Manual issue not found.` (404).**
The identifier does not belong to a manual issue (for example, it is an `EMP` pick batch), it does not exist, it belongs to another company or it was already
deleted (to delete an `EMP` pick batch use Picking & packing).

**Dispatching answers me 403 (no message), or `Missing permission 'warehouse.issue'.` (403) when deleting from Picking & packing.**
Dispatching, deleting a manual issue or reading its reasons requires `warehouse.issue` (through `/manual-issues` the 403 arrives with no body, the
policy rejects it; through `DELETE /pick-batches/{id}` it arrives with that message). **Warehouse operator** and **Company admin** include it; in
a custom role, the administrator adds it in System → Roles (“Manual issue (stock out without delivery)”).

**`The kind must be MANUAL, PACK or ALL.` (400).**
The `kind` filter of `GET /api/v1/pick-batches` only accepts those three values (without it, the list brings both kinds).

**I want to delete a manual issue and it tells me the bin, the product or the warehouse is inactive (422).**
Deleting returns the goods to their original bin: reactivate the bin (or the product) and try again. If the warehouse was already
deactivated (permanently), the issue can no longer be deleted; make an inbound adjustment where appropriate.

**I deactivated a product that had manual issues: why didn't it stop me?**
A manual issue is a final stock out and does not count as an open document (it does not block deactivating the product or the warehouse). The only
thing you lose is being able to delete it after the deactivation.

**Where do I see manual issues?**
In the pick batch list (`GET /api/v1/pick-batches`, with `kind=MANUAL` only those), in `GET /api/v1/manual-issues` and in the movement ledger:
each stock out says “Manual issue DMA-00012” with the note `DMA-00012 · {motivo}`.

### Manual issue in the app (2026-10-11)

**In Dispatch I don't see “Complete dispatch”; only “Pack”.**
Your user does not have the `warehouse.issue` permission and the app says so: `“Complete dispatch” (manual issue, no delivery) needs the warehouse.issue
permission and your user does not have it: here you can Pack. Ask the administrator if needed.` The administrator adds it in System →
Roles (“Manual issue (stock out without delivery)”); **Warehouse operator** and **Company admin** include it. If it says `Your permissions could not be
read yet (signal is needed once): for now you can only Pack.`, the device has not talked to the server since that user signed in: wait until
you have signal (Home syncs by itself) and open Dispatch again.

**`Choose the reason for the manual issue.`**
Tap one of the reasons (Sample, Internal use, Customer pickup, Sale, Other, or your company's names) before **Dispatch**. The note is
optional.

**`The company has no reasons enabled for manual issues. Ask the administrator to enable one (System → Lists).`**
The company disabled all the reasons of “Manual issue reason”. Without a reason nothing can be issued: the administrator (`admin.catalogs`)
enables at least one; the device downloads it in the next sync (at most 30 minutes).

**The app says `Manual issue queued: …` and does not show me the DMA number.**
There was no signal (or it was weak) when confirming: it was saved on the device and is sent by itself. The `DMA-#####` number is assigned by the server when it receives it;
it is visible on the web (Picking & packing, Kind column). Do not repeat it: it cannot end up twice.

**When dispatching I got a message in red and the issue opened again.**
The server rejected it at that moment (for example `Insufficient inventory of …`) and the app shows its exact message: nothing left, the device's
balance was left as it was and the lines, the reason and the note are still there. Correct it (lower the quantity, remove the line or change bin) and
tap **Complete dispatch** again. If the rejection arrives later (it was sent by itself), it appears in **Sync → With error**.

**When dispatching I get `Something went wrong. Try again.` even though I have the button.**
Most likely `warehouse.issue` was removed from your role and the device has not found out yet (the server answers 403 with no message). Go back to
Home with signal so your permissions refresh; if it persists, check the role.
Since the 2026-10-11 fix that case no longer reads like that but `You do not have permission for this action. Ask your administrator to grant it.`
(see [Warehouse app: fixes after the manual issue](#warehouse-app-fixes-after-the-manual-issue-2026-10-11)).

## Manual issue on the web (screen, 2026-10-11)

**I don't see how to make a manual issue.**
In **Warehouse → Picking & packing**, in the **Pick** panel, turn on **Manual issue (no delivery)**. If the switch does not appear,
your role does not have `warehouse.issue` (if it only has `warehouse.pick`, the panel is the usual picking).

**Where do I see the manual issues already made?**
In the same Picking & packing list: **Kind** column (“Manual issue · reason”) and **Kind** filter → Manual issues. A deleted issue
shows as **Cancelled** (check “Include deleted”).

**When dispatching, the row was marked in red on the quantity.**
The server rejected that line (for example `Insufficient inventory of …`); its exact message is under the quantity. Nothing was deducted and the
DMA number was not used up. Correct it and press **Dispatch** again.

**I don't see “Delete” on a manual issue.**
Deleting a manual issue requires `warehouse.issue` (`warehouse.pick` is not enough) and that the server allows it (`canDelete`); one already cancelled is not deleted again.

## Warehouse app: fixes after the manual issue (2026-10-11)

Chapter: [09 §9 Sync](09-app-almacen.md#9-sync).

**`You do not have permission for this action. Ask your administrator to grant it.`** (in Spanish `No tienes permiso para esta acción. Pide a tu
administrador que te lo asigne.`)
The server answered 403 with no message: your role is missing the permission for what you tried (for example `warehouse.issue` for **Complete dispatch**,
or the permission was removed and the device has not found out yet). Retrying does not help: ask the administrator to add it to your role (System → Roles), go back
to Home with signal so your permissions refresh and try again. If the operation was already in the queue, it stays in **Sync → With
error** with this message: once you have the permission tap **Retry**; if you are not going to have it, **Discard**. Before this version the same case
read `Something went wrong. Try again.`. A 403 that brings its own message (e.g. a module turned off) is still shown as is.

**In Sync a pending operation says `The operation with this key is still being processed.`**
It is not your error nor a rejection: the server is still processing that same submission (the previous attempt was cut off without a response). The operation is still

**pending**, the device's balance is not touched and it is sent again by itself in the next pass (or when tapping **Sync now**) with the same key,
so it cannot end up twice. Do not discard it or repeat it by hand. If it stays like that for more than 10 minutes, notify support.

**In Sync, what are “Manual issue”, “Transfer”, “Inventory adjustment”…?**
It is the kind of each operation saved on the device (before, the technical name was shown, such as `manualIssue` or `adjust`). The full list is in
[chapter 09 §9](09-app-almacen.md#9-sync). If you see a technical name in English that is not in the list, the operation was saved by another version
of the app: it is sent anyway; notify support if it ends up with an error.

## Default reason for manual issues (2026-10-11 b)

Chapter: [06 §7c](06-inventario-y-almacen.md#7c-default-reason-for-the-manual-issue-2026-10-11-b).

**How do I make the device already come with the reason chosen?**
In **Tenant settings**, choose the **Default reason for manual issues** (`admin.tenant` is needed). The app and the web preselect it;
whoever dispatches can change it. The note stays empty by default. The device picks it up the next time it downloads the reasons with signal (the list of
reasons marks which one is the default with `isDefault`).

**`The reason {CODE} does not exist or is inactive.`** (400, when saving the settings, in `defaultManualIssueReason`)
The code is not in the “Manual issue reason” list, it is inactive or your company disabled it. No setting of that submission was saved.
Choose one from the list (`GET /api/v1/manual-issues/reasons`) or enable it first in **System → Lists**.

**I set a default reason and it is no longer preselected.**
The company disabled that reason (or it was inactivated): the default is ignored without error and the settings show it empty. When it is enabled again it is
preselected again by itself; or choose another default.

**With a default reason, can I issue without a reason?**
No: the default only preselects on the screen. If the issue reaches the server without a reason it answers 400 `Enter the reason for the manual issue.`.

**How do I remove the default reason?**
In Tenant settings leave it at “No default reason” (by API, `PUT /api/v1/tenant/settings` with `"defaultManualIssueReason": ""`).
It is recorded in the company's audit.

## Manual issue on the web: the reason is already set (2026-10-11 b)

Chapter: [F19 §2 and §2b](frontend/f19-despacho-manual.md). No new HTTP codes: server errors appear as is.

**I turn on “Manual issue (no delivery)” and the Reason is already chosen.**
That is expected: it comes with the **last reason you used in this browser** (if it still exists and is enabled) or, if there is none, the **company's default
reason** (Settings → Operations → Manual issue). Change it in the selector if it is not the right one; the one you dispatch with becomes your “last used”.

**I always get the same reason even though the administrator changed the default.**
The last used wins over the default. Choose another reason once and from that issue on it will be the new “last used”. Each browser and each user remember their own.

**Where is the note?**
Press **Add a note** (below the Reason): the field appears (optional, up to 500 characters). Without pressing it, the issue goes without a note. After dispatching it is hidden again.

**Where do I change the default?** (Settings → Operations → “Manual issue”; `admin.tenant` is needed; with the Lots and serials module turned on)
Choose the **Default reason for manual issues** (or “No default reason”) and **Save changes**. The help says: *“It comes preselected when issuing; the
reason is still required.”*. If the server answers **`The reason {CODE} does not exist or is inactive.`** (400) it appears under the selector and nothing is saved.
Without `admin.tenant` the selector is visible but cannot be changed.

## Manual issue in the app: the reason is already set (2026-10-11 b)

Chapter: [09 §6.y](09-app-almacen.md#6y-complete-dispatch--manual-issue-dma-2026-10-11). None of these cases has an HTTP code: they belong to the app.

**I tap “Complete dispatch” and it no longer asks me for the reason: it says `Issued for: Sale`.**
That is expected: the app opens the confirmation directly with the **last reason you used on this device** or, if there is none, with the **company's default
reason** (it says so below: `It is the last reason you used on this device.` or `It is the company's default reason.`). If it is the right one, tap

**Dispatch** and done. If not, tap **Change** and choose another.

**I changed the reason once and now I always get that one.**
The one you choose becomes your “last used” and wins over the company's default. Tap **Change** and choose the usual one: from that issue on it comes up again.

**It keeps asking me to choose the reason every time.**
The company has no default reason and you have not yet dispatched with this device (or the one you used last time is already disabled). After the
first issue, the next one already opens with that reason. The administrator can set a default in **Tenant settings** (the device picks it up the
next time it downloads the reasons with signal, at most 30 minutes).

**The reason I used last time no longer appears.**
The company disabled it: the app skips it without notice and opens with the company's default or, if there is none, with the list to choose from.

**Where do I put the note?**
Tap **Add a note** before **Dispatch**: the **Note (optional)** field appears (up to 500 characters). If you do not tap it, the issue goes without a note.

**The “Issue without a delivery?” notice with No / Dispatch no longer shows.**
The confirmation screen replaces it: **Dispatch** sends the issue immediately. To not send it, tap **Back**.

**`Choose the reason for the manual issue.`**
It should no longer be seen: with no reason set, **Dispatch** does not appear until you choose one from the list. If you see it, choose a reason and notify support.

## Default category for new products (2026-10-11)

**I create a new product and the category is already set. Where does it come from?**
From Tenant settings → Operations → “New products: default category” (in Advance Depot, AxisCare). It is only a preselection: you can change it or leave it empty in the same form before saving.

**A new product comes out without a category even though the company has a default one.**
It is almost always one of three things: (1) the product was created through the **API or an import**, which do not apply the default (only the Products screen does); (2) the default category was **deactivated**, and then it is ignored; (3) you removed it in the form. Check in Settings → Operations that it still selects an active category.

**`The default category does not exist or is inactive.` (400) when saving the settings.**
The chosen category no longer exists, is deactivated or belongs to another company. Choose another from the list (only active ones are shown) or “No default category”. Nothing else from the settings was saved in that attempt.

**The Depot default did not end up in AxisCare after `db-update`.**
The `2026-10-11 (c)` section fills it only if the company is named “Advance Depot”, did not have one yet and there is an active category named `AxisCare` or `Axis Care`. If the category has another name or does not exist yet, it does nothing: create it (Products & inventory → Categories) and choose it in Settings → Operations.

**Does it change the categories of products that already exist?**
No. It only preselects the category when creating a new product from the screen.

## Bin and inventory reports: no row limit (2026-10-10)

**I print more than 500 bins, labels or products and only a notice comes out.**
That is on purpose: there is no longer a limit. Past 500 the modal warns “That is N: the PDF will be large and may take a moment. All of them are printed; there is no limit.” and **Generate PDF stays enabled**. If you see a text that still talks about “maximum per print is 500”, it is from an old manual or version.

**A report says “only the first N… (the server returned no more)”.**
There is **no longer any row limit** (neither the 500 nor the previous 100,000): reports and exports read the whole filter, whatever its size. That notice only appears if the server stopped returning rows before reaching the total (a network cut, for example): generate it again.

## Adjust from the device menu (2026-10-11)

**I see a product in a bin but Lookup does not show it (the system says there are 0). How do I adjust it?**
Main menu → **Adjust** (`warehouse.adjust` is needed): scan the bin, then the product, choose **Increase**, the quantity, the reason **Found** and write the note. Before, this could only be done from the web.

**“Transfer” is no longer in the menu.**
That is on purpose: you transfer with **“Move”** in Lookup (look up the bin or the product and tap *Move*). It is the same screen and the same permission (`warehouse.transfer`).

**In Adjust I scanned a lot-tracked product and it tells me it has no lots registered.**
The product is lot-tracked and no lot of it exists yet in the warehouse, and the app does not create lots. Make the adjustment from the web (Products & inventory → Adjust), where the lot can be indicated.

**In Adjust I chose Decrease and it says “Can only be lowered by up to 0”.**
The system has nothing of that product there (or what is there is reserved). Decreasing is only possible down to what is available; if what you found is extra, use **Increase**.

**After adjusting the app stayed in Adjust.**
That is on purpose when entering from the menu: it is ready for the next bin (the notice says what was adjusted). **Back** exits to Home. If you entered from Lookup with “Adjust”, it goes back to Lookup as before.

**The app opens on “Session locked” and there is no signal, how do I get in? What do `Wrong PIN (offline).`, `Too many offline attempts. Connect to unlock.` and `No signal. Connect once to be able to unlock offline.` mean?**
Without signal, the app compares the PIN with a fingerprint stored on the device the last time that user signed in with signal. If you type the PIN wrong the first message appears; after 5 consecutive failures offline the second one appears and signal is needed to unlock (or “Sign out”). The third means that user never signed in with signal on this device: connect it once and sign in with the PIN. What was captured is not lost in any case.

**A user does not see warehouses, clients or orders that others do see, or a link to something gives them “not found” (404). What is going on?**
They almost certainly have a **data scope** (warehouses and/or clients) on their user. With a scope, they only see what belongs to those warehouses/clients and the rest neither appears nor can be opened by id (404, as if it did not exist). Check `GET /api/v1/users/{id}/data-scopes`; to give them more access add them with `PUT` (it replaces the list) and, to remove the restriction of one type, leave no rows of that type. Administrators are not exempt either. They do not need to sign in again.

**Is a record without a warehouse or without a client visible to someone with a scope?**
Yes. What does not belong to any warehouse/client (empty field, such as a shared product or a general template) is seen by everyone in the company; the scope only hides what belongs to a warehouse/client outside theirs.

**What does “Route {code} cannot be closed: there are unfinished orders (…)” mean? (422)**
That route still has orders that did not finish: they are still on the way or arrived without a result. The message names up to 10 (and adds “and N more”). Record the delivery of each one, or mark it Failed attempt (it counts as finished and goes back to the inbox to be rescheduled), or cancel it, and close again. Delivered, Cancelled and Failed attempt orders do not get in the way.

**What do “Route {code} is not in progress; only a route that has already departed can be closed.” and “Route {code} is already closed.” mean? (422)**
Only a route that has already departed (`IN_PROGRESS`) can be closed. A draft, planned or merely dispatched route must first record its departure (`POST /trips/{id}/start`). If it says “already closed”, someone closed it before; there is nothing more to do.

**A dispatcher cannot close routes (403).**
Closing a route requires the `trips.close` permission, different from `trips.dispatch`. The Dispatcher and TenantAdmin templates include it; a custom role needs it added in Roles & users.

**How do I change from FEFO to FIFO? What is the difference?**
FEFO takes out first what expires first; FIFO takes out first what came in first (its oldest first entry). It is changed per company with `PUT /api/v1/tenant/settings` `{ "rotationMethod": "FIFO" }` (`admin.tenant` permission) and, if a product needs the other method, with `rotationMethod` when creating or editing it (empty = remove the exception). All existing companies stay on FEFO. It applies from the next allocation; it does not change what has already been picked. There is no selector on the web or in the app yet: it is done through the API.

**What does “The rotation method must be FEFO or FIFO.” (400) mean?**
You sent a value other than `FEFO` or `FIFO` (for example `LIFO`) in the tenant settings or in a product. It is not case-sensitive. There are no other methods.

**With FIFO the system took out a lot that expires later, is that wrong?**
No: with FIFO the first-entry date of the balance rules and the expiration only breaks ties. If for that product it is better to take out first what expires first, set `rotationMethod` = `FEFO` on its record (exception) and it will stay that way even if the company is FIFO.

**I moved goods from one bin to another and with FIFO they still come out first, why?**
Because a transfer inherits the first-entry date of the origin: moving the goods to another place does not make them younger.

**I scanned the bin and the app said “No signal: you are counting on the device…”, is what I count lost?**
No. It is saved on the device and sent **whole and once** as soon as there is signal (outbound queue, “Count done offline”). If the server rejects it, it stays in Sync with the reason to retry or discard. Only the count by bin opens without signal.

**I counted offline and the app warned “That position already had an open count from another counter…” (or the count stayed open), what happened?**
Another counter already had the count of that bin open: what you counted was **added to that count** and it was not finished (its owner continues it). If the notice says “the count could not be finished: {n} line(s) left to count”, the server had lines the device did not count: what was counted was saved and the count stayed open to be resumed from “Open counts”.

**The app warned “There were movements in that position since you counted offline”, is it an error?**
No: someone moved goods from that bin between your count and the sync. The count is compared with the server's stock when reconciling (as always); the notice, and the comment “Counted offline; there were movements…” in the history, are there so the supervisor takes it into account when reviewing.

**Why does the device say it fits in the bin and the server warned that it does not?**
The device's free space is the bin's capacity minus what the device knows is there. It does not know what other lines of the same receipt already reserved in that bin (the server does). It is only a notice: the server decides and does not block by capacity.

**A client or a consignee does not appear when packing a dispatch offline.**
The device downloads the active clients and the consignees (the client's own and the shared ones) with the sync of every minute. If it was created less than a minute ago, tap “Sync now” on Home. If it still does not appear, it is deactivated or the user does not see it (data scope). They are downloaded by those with the `warehouse.pick` permission; without it, the app requests them from the server with `clients.read` and `locations.read`.

**Where do I change the FEFO/FIFO rotation?**
On the web: Tenant settings → Operations → “Inventory rotation” (for the whole company; `admin.tenant` permission) and, if a product needs the other method, on its record → “Rotation”. The warehouse app does not have that selector but already respects the method.

## Batch 38 — Consignee import (2026-10-11)

See [chapter 02, §2.1](02-clientes-y-contratos.md#21-consignee-import-batch-38). Permissions: `locations.read` (view) and `locations.create` (templates and import); Lists module.

**How do I import many consignees at once?**
Create a template (`POST /api/v1/locations/import-templates`) that says which field goes in each column position (at least `name`, `line1` and `city`, the last one can go as a default). Then validate the file (`POST /api/v1/locations/import/validate` or `/validate-file`), review the preview and confirm (`POST /api/v1/locations/import/{lote}/confirm`). Nothing is created until you confirm.

**Whose are the imported consignees?**
Of the `clientCode` (or `clientName`) column of each row; if the row does not bring it, of the batch's client; if there is no batch client either, they end up **shared** (all clients see them).

**Accents come out broken (“Mar�a”) in the preview.**
The file is not in UTF-8. In Excel use “Save as” → **“CSV UTF-8 (comma delimited)”**, not “CSV (comma delimited)”, and validate again.

**“The template must include the name and line1 columns.” (400)**
The template is missing the name column or the address (line 1) column. Those two fields always go in a column (they do not accept a default). Add them in `columns`.

**“The template must give the city (city) in a column or in the defaults.” (400)**
Add a `city` column or, if all the consignees are from the same town, a default: `"defaults": {"city": "Ponce"}`.

**“Use clientCode or clientName, not both.” (400)**
The template has both client columns. Keep one: `clientCode` is safer (the name can repeat).

**“The field X does not accept a default value.” (400)**
`code`, `name`, `line1`, `clientCode` and `clientName` identify each consignee or its owner, so they must come in each row. Remove it from `defaults`. To set the same client for the whole file, use the batch's client.

**“Enter at least one column.”, “Maximum 100 columns.”, “Position N is repeated.”, “Position must be 1 or greater.”, “Position cannot exceed 500.”, “Unknown field: X.”, “The field X is repeated.” (400)**
Correct the template's column list: each position only once (from 1 to 500), each field only once and only fields from the list (`GET /api/v1/locations/import/fields`). Order fields (e.g. `consigneeName`) do not exist here.

**“An import template with that name already exists.” (409)**
There is already a consignee template with that name (active or inactive). Use another name or reactivate and edit the existing one. Order templates do not count.

**“The template is already inactive.” / “The template is already active.” (409)**
It was already in that state; there is nothing to do.

**“The template is inactive; reactivate it or choose another.” (400)**
The chosen template was deactivated. Reactivate it (`POST /api/v1/locations/import-templates/{id}/reactivate`) or use another.

**“Import template not found.” (404) when validating**
The template does not exist, it is an order template (those live in `/api/v1/import-templates`) or it belongs to a client other than the batch's. Use a general template or one of the same client.

**“Unknown template kind: CONSIGNEE.” (400)**
You requested consignee templates through the orders route (`/api/v1/import-templates?kind=CONSIGNEE`). Use `/api/v1/locations/import-templates`.

**“Enter the content of the CSV file.” / “The file has no data rows.” (400)**
The file arrived empty or only brings the header. Check that you chose the right file; if your file has no header, create the template with `"hasHeader": false`.

**“The file exceeds the limit of 5,000 rows or 2 MB.” (400)**
Split the file into parts of up to 5,000 rows and 2 MB and validate each one.

**“The client has been deactivated; only its history can be viewed.” (409 when validating or confirming, or error of a row)**
The batch's client (or that row's) is deactivated: no consignees are created for it. Reactivate the client if appropriate, or remove those rows. A **suspended** client does accept consignees.

**“Client not found.” (404)**
The batch's client does not exist or belongs to another company. Choose it again.

**“There is no client with the code 'X'.” / “There is no client with the name 'X'.”**
The row names a client that is not in the company. Correct the code (or the exact name, case-insensitive) and validate again.

**“There is more than one client with the name 'X'; use clientCode.”**
There are two or more clients with that name. Change the column to `clientCode` in the template and use the client code.

**“The row indicates client 'A', but the batch belongs to client 'B'.”**
It no longer appears since Batch 45 (2026-10-13): the batch's client is the *default client* and a row that brings its own client keeps that client. If you see it, the server is older than Batch 45.

**“The name is required.” / “Address (line 1) is required.” / “City is required.”**
The cell is empty (and there is no default for the city). Fill it in the file.

**“Maximum N characters.”**
The text is longer than allowed (name 200, code 40, lines 200, city/state 100, postal code 20, notes 500). Shorten it.

**“The country code must be ISO 3166-1 alpha-2 (2 letters).” / “Unknown country: XX.”**
Use the 2-letter code (`PR`, `US`, `DO`…). “Unknown” means the country is not in the list or the company disabled it. If the cell is empty `PR` is used.

**“Service minutes must be a whole number.” / “Service minutes cannot be negative.”**
`defaultServiceMinutes` goes as a whole number from 0 up (`10`, not `10 min`).

**“The time must have the format HH:mm (24 hours).”**
Write the time as `08:00` or `17:30` (also `8:00` or `08:00:00`). Do not use “8am” or “5 PM”.

**“The time window needs both a start and an end time.” / “The window end time must be after the start time.”**
The window carries both times or none, and the end after the start (`08:00`–`17:00`).

**“The allowDupInvoice value must be yes or no (also 1/0 or true/false).”**
Use `sí`/`no` (also `si`, `s`, `n`, `1`, `0`, `true`, `false`, `yes`, `y`). Empty = no.

**“Invalid phone.”**
The row's phone (or the template's default) does not have a phone format. Correct it (e.g. `787-555-0101`) or leave it empty.

**I set a default with a bad value and the template is not saved (400 in `defaults.X`).**
Defaults are checked when saving the template with the same message the row would give (minutes, times, `allowDupInvoice`, country format, lengths and phone). Correct the default's value.

**“Code X is repeated in row N of the file.” / “The name 'X' is repeated in row N of the file; add a distinguishing mark to the name (or to the number).”**
Two rows of the same owner bring the same code, or the same name with the same line 1. The first one is created; correct or remove the repeated one.

**“A consignee with the code 'X' ('Name') already exists.”**
That code is already used by an active consignee of the same client or a shared one. The import only creates: if it is the same consignee, remove the row; if it is another, change its code.

**“An inactive consignee with the code 'X' ('Name') already exists; reactivate it instead of importing it.”**
The consignee exists but is deactivated. Reactivate it from its record (`POST /api/v1/locations/{id}/reactivate`) and remove the row from the file.

**“An active consignee with the same name already exists. Add a distinguishing mark to the name (or to the number) to tell them apart.”**
There is already a consignee with that name for that owner (regardless of accents, case or double spaces). **The address does not count**: with the same name and another address it is also rejected, because when capturing an order it would not be known which one to choose. Change the row's name by adding a distinguishing mark (for example the branch or the number) or use the consignee number, or remove the row.

**“An inactive consignee with the same name already exists; reactivate it instead of importing it, or add a distinguishing mark to the name (or to the number).”**
There is a deactivated consignee with that name. Reactivate it from its record (**Show inactive**) and remove the row, or add a distinguishing mark to the row's name.

**Notice: “Code X is also used by a consignee of a client; for that client it will keep resolving to theirs.”**
The row is shared and its code already belongs to a consignee of a client. It is created anyway; when that client uses the code (for example, in the order importer) theirs will be taken, not the shared one.

**“Row N has errors and cannot be confirmed.” / “Row N does not exist in the batch.” (400)**
In `rows` you chose an invalid row or a number that is not in the batch. Choose only rows with `isValid: true` (or send `{}` for all the valid ones).

**“The batch has no valid rows to create.” (400)**
No row of the batch is free of errors. Correct the file and validate it again.

**“The batch was already confirmed; validate a new file to import more consignees.” (409)**
That batch was already confirmed; the result is in `GET /api/v1/locations/import/{lote}`. To import more, validate another file.

**“The batch was discarded; validate the file again.” (409)**
The batch was discarded and cannot be confirmed. Validate the file again.

**“The batch is already being confirmed or was confirmed; check its result.” (409)**
Someone else (or another tab) is confirming or discarding the same batch. Wait and check the batch. If the connection was cut in the middle of a confirmation and the batch stays like that, validate the same file again: what was already created will come out as a duplicate and the rest is confirmed.

**“Only a batch pending confirmation can be discarded.” (422)**
The batch is already confirmed or discarded; there is nothing to discard.

**“Import batch not found.” (404)**
The batch does not exist, belongs to another company or is an order batch (those are checked in `/api/v1/orders/import/{id}`).

**A row came out with an error when confirming even though it was fine in the preview.**
Between validating and confirming someone created a consignee with that code or that name (or the client was deactivated): on confirmation each row is checked again. The row keeps the message in `rows[i].error` and the others are created anyway. If it says “Unexpected error creating the consignee; check the row and try again.”, validate a file with that row again and, if it repeats, notify support.

**Can two people import consignees at the same time?**
Yes. Confirmations of the same company are created row by row one at a time, so the same new code in two files confirmed at once is created only once; the row of the other file comes out as failed with “A consignee with the code… already exists”. In the meantime, “Plan the day” of that company waits its turn (it uses the same lock).

**I see the batch but the batch's status history gives me 403.**
`GET /api/v1/status/history/IMPORT_BATCH/{id}` requires `orders.view` (the order importer's permission). The batch's state and result are seen just the same in `GET /api/v1/locations/import/{id}` with `locations.read`.

**Confirming takes a long time with a large file.**
Each row is created separately (with its check, audit and phone): 5,000 rows take about half a minute. Wait for the response; do not confirm again.

## Batch F-B — Consignees: list, record, create/edit and CSV import (2026-10-11)

Messages of the Lists → Consignees screen (the server ones appear as is, below the field, in the preview table or in the dialog). Chapter: [fb-consignatarios.md](frontend/fb-consignatarios.md).

**`The name is required.` / `Address (line 1) is required.` / `City is required.` (400) when saving.**
These are the three mandatory fields of the consignee. The city is chosen in **Town** (search by town or by postal code). Until all three have a value, the modal does not call the server.

**`Maximum N characters.` (400).**
Name 200, number 40, lines 200, town and state 100, ZIP 20, notes 500. Shorten the text.

**`Country is required.` / `List Country 'X' not found.` (400/404).**
The country is in **Delivery options** (Puerto Rico by default). Choose one from the list; if the company's `Country` list does not have the code, ask the administrator to add it.

**`The time window needs both a start and an end time.` / `The window end time must be after the start time.` (400).**
In **Delivery options**: fill in “Window: from” and “Window: to” (or leave both empty), and make the end come after the start.

**`Service minutes cannot be negative.` / `Service minutes must be a whole number.` (400).**
Use 0 or a positive whole number.

**`Invalid phone.` (400).**
The phone must have the digits of the company's mask (10 digits by default). Leave it empty if there is none.

**`Enter a client or mark the location as shared, not both.` (400) / `Client not found.` (404).**
In **Owner client** choose a client or “Shared (all clients)”. If the client no longer exists in the company, reload the screen.

**`The location was modified by another user; reload and try again.` (409) when editing.**
Someone else saved this consignee before you. Close the modal, wait for the record to refresh (or reload) and repeat your change.

**“The consignee was saved, but the phone was not: …”.**
The consignee was indeed saved; only the phone failed (for example `Invalid phone.` or a missing permission). Open it with **Edit** and correct the phone. Writing the phone requires `contacts.manage` and `locations.update`.

**I cannot deactivate / reactivate or the switches are off.**
`locations.update` is missing. Deactivation is logical: the consignee stops appearing in the order capture combos and keeps its history; **Show inactive** lets you see it to reactivate it.

**The CSV file: `The file exceeds the limit of 5,000 rows or 2 MB.` (400).**
Split the file into parts of up to 5,000 rows and 2 MB each.

**`Enter the content of the CSV file.` / `The file has no data rows.` (400).**
The file is empty, or only brings the header (with “The first row is the header” turned on in the template, that row does not count). Check the file and the template.

**`The template is inactive; reactivate it or choose another.` (400) / “Template not found” (404) when validating.**
The template was deactivated or belongs to another client. Choose another, or in the modal turn on **Show inactive templates** and use **Reactivate this template**. With an owner client chosen, general templates and that client's templates work; with no owner (shared), only general ones.

**Preview: errors per row (the row is not imported).**
`The name is required.` / `Address (line 1) is required.` / `City is required.`: the data is missing in that row or the column is not in the template (check the mapping).
`Unknown country: X.` or the invalid-country message: use the country code from the list (PR, US…). `Service minutes must be a whole number.` / `…cannot be negative.`.
`The time must have the format HH:mm (24 hours).` and the two time window ones (`The time window needs both a start and an end time.` / `The window end time must be after the start time.`).
`The allowDupInvoice value must be yes or no (also 1/0 or true/false).`
Duplicates: `An active consignee with the same name already exists. Add a distinguishing mark to the name (or to the number) to tell them apart.`, `A consignee with the code 'X' ('Name') already exists.`, `An inactive consignee with the code 'X' ('Name') already exists; reactivate it instead of importing it.`,
`Code X is repeated in row N of the file.` and `The name 'X' is repeated in row N of the file; add a distinguishing mark to the name (or to the number).`: remove the duplicate from the file or reactivate the existing consignee (**Show inactive**).

**Preview: notices (the row is imported).**
`Code X is also used by a consignee of a client; for that client it will keep resolving to theirs.` They are informational; if you do not want the row, remove it from the file and validate again.

**Accents and the ñ come out broken.**
The CSV is not in UTF-8 (Excel on Windows saves in Windows-1252). Save it as “CSV UTF-8” and choose it again; the preview shows it before importing.

**The Import button is off.**
The batch has no valid rows, it is still being validated, or `locations.create` is missing. Correct the rows with errors and choose the file again.

**`The batch was already confirmed; validate a new file to import more consignees.` (409) / `The batch was already discarded…` / “a confirmation is in progress” (409).**
The batch was already imported (or discarded, or someone is confirming it). Close the modal, check the list and, if you need to import more, choose the file again.

**`The batch has no valid rows to create.` / `Row N has errors and cannot be confirmed.` (400).**
There was nothing valid to create. Correct the file and validate again.

**Result with “failed” rows (`A consignee with the code … already exists`, `Unexpected error creating the consignee; check the row and try again.`).**
Between the preview and Import someone created an identical consignee, or the row failed when saving. The other rows were created. Correct only the failed ones in a new file.

**Template: `The template name is required.` / `Enter at least one column.` (400).**
Enter a name and assign a target field to at least one column.

**Template: `The template must include the name and line1 columns.` / `The template must give the city (city) in a column or in the defaults.` (400).**
Assign “Name” and “Address” to columns; the “Town” can go in a column or as a **Default value** (for example “Ponce”).

**Template: `Use clientCode or clientName, not both.` / `The field X is repeated.` / `The field X does not accept a default value.` / `Unknown field: X.` (400).**
Keep only one client column; each field can go in only one column; number, name, address and client do not accept a default value.

**Template: `An import template with that name already exists.` (409).**
Use another name or edit the existing one (it may be inactive: **Show inactive templates**).

**How do I “delete” a template?**
They are not deleted: **Deactivate this template** removes it from the selector (batches already validated keep it). To use it again, **Show inactive templates** and **Reactivate this template**. If you deactivate it and it was already deactivated, `The template is already inactive.` (409).

## Batch 39 — Order prices and list filters (2026-10-12)

Questions about who sees order prices and about the filters of the order list. The related chapters are “03 — Transport orders” (sections “List and search” and “Who sees prices”) and “Orders: search orders and create a new one” (screens manual).

**Why don't I see the price of the orders?**
Your role does not have the `orders.view_price` permission (“View order prices”). Without it, the price comes out empty or does not appear: the **Quoted** column of the list is not shown and in the order record the quoted amount appears as “—”. The order is indeed quoted; you just do not have permission to see how much. The **COD** amount (what is collected on delivery) is always visible. If your work requires seeing prices, ask the administrator to add the permission to your role (System → Roles).

**Who sees prices without having to ask for anything?**
Out of the box, the Company admin (TenantAdmin) role and the Billing role, and the roles with those same names your company already had. Dispatcher, Warehouse operator, Driver and Read-only do not include it. The administrator can add the permission to any custom role.

**What does “Missing permission 'orders.view_price'.” (403) mean when opening an order's quote?**
The full quote (total, lines, dispatch fee, COD fee and client credit) is price information. To see it, the `orders.view` and `orders.view_price` permissions are needed. Ask the administrator or Billing for the second one.

**What do I do if it says “The client exceeds its credit limit.” and shows me no figures?**
It means the client's credit is not enough to create the order or mark it as picked up. You do not see the limit, what the client already owes in orders in progress or the amount of this order because you do not have `orders.view_price`. Notify Billing (or whoever has the `orders.credit_override` permission) so they authorize the excess, or so they raise the client's limit. If you have `orders.view_price`, the message brings the figures: “The client exceeds its credit limit: limit X, in progress Y, this order Z.” (see Batch F-C1, below).

**The order history says “Exceeded credit authorized by …” but without amounts. Why?**
It is the record that someone authorized a credit excess. The figures (limit, in progress, order amount) are only seen by those with `orders.view_price`.

**I imported orders from a file and a row says “The client exceeds its credit limit.”. What happened?**
That order was not created because, when creating it, the client's credit was not enough. Whoever has `orders.view_price` sees the same notice with figures. You can import that row again authorizing the excess (`orders.credit_override` is needed).

**My Analysis indicator or view with “Quoted amount” comes out empty or 0. Why?**
The “Quoted amount” field of the “Orders” source comes out empty for those without `orders.view_price`, and an indicator that sums that field gives 0. Ask for the permission or use another column.

**`Unknown service type: X.` / `Unknown package type: X.` / `Unknown status: X.` (400) when filtering the order list.**
One of the filter values does not exist in the lists. On the Orders screen you will normally not see it, because there the values are chosen from a list; it appears to whoever queries the API directly (for example, an integration). Check the spelling of the code (such as `STANDARD`, `BOX` or `PICKUP`; case does not matter) and remove the value or change it to a valid one.

**`Client not found.` (404) when filtering the list by several clients.**
One of the clients in the filter does not belong to your company or is outside the clients your user can see. Remove it from the filter. If you should be able to see it, ask the administrator to check your user's data scope.

**I filtered by town and no order came out.**
The filter compares the full town of the order's consignee. Case and accents do not matter (“bayamon” finds “Bayamón”), but if the consignee has the town written differently (another name or abbreviated), the order is not included. Try the **ZIP** filter (for example `009`). No orders coming out is not an error: there are simply no matches.

**I checked several values in a filter. Are they added or subtracted?**
Within the same filter they are added: the order is included if it meets **any** of the checked values (for example, Town = Bayamón and Ponce brings those of both towns). Between different filters they are subtracted: the order must meet **all** the filters you filled in.

## Batch F-C1 — Orders screen: list and new order (2026-10-12)

Questions about the **Operations → Orders** screen. Messages appear exactly as the screen shows them. The full chapter is “Orders: search orders and create a new one” (screens manual).

**Why don't I see the “New order” button?**
The `orders.create` permission is missing. Ask your company's administrator for it (System → Roles). Without that permission **Save** does not appear either.

**Why don't I see the “Quoted” column (the price)?**
The `orders.view_price` permission is missing (“View order prices”). COD is visible just the same, with or without the permission, because it is not a price. The Company admin and Billing roles include it out of the box.

**Why does an order take up several rows in the list?**
One row is shown per package type. The first row carries the order data and the following ones only “↳”, the other package type and its pieces. It is a single order: the counter and the pages count orders, and the actions (open and delete) are in the first row.

**Why don't I see the trash can on an order?**
The trash can only appears if the order is still in its initial status (Submitted) and you have the `orders.cancel` permission. An order already picked up is not deleted: it is cancelled.

**What does “A shipment can only be deleted in its initial status; use cancellation.” (422) mean?**
Someone confirmed the order while you had the list open, and it can no longer be deleted. Reload the list. If the order must be voided, it has to be cancelled.

**What does “The client is required.” mean?**
You did not choose the client. Search for it in the **Client** field (by code or name) and choose it from the list.

**What does “The service type is required.” mean?**
The **Service** field is empty and the company has no default service. Choose one.

**What does “The consignee is required: choose one from the directory or capture a new one.” mean?**
The consignee is missing. Search for it in **Name** (the client's and the shared ones appear) or create it with the **+** button (needs `locations.create`). A loose name cannot be typed.

**The consignee field says “Choose a client first” or “The consignee directory could not be read”. What do I do?**
Consignees depend on the client: choose the **Client** first. If it says the directory could not be read, your user does not have the `locations.read` permission (Lists); ask for it.

**Why doesn't the “+” button appear next to the consignee?**
The `locations.create` permission is missing, or you have not chosen the client yet (the button is disabled until then). The consignee you create there gets the order's client as owner.

**Why can't I change the consignee's address, town, ZIP or phone inside the order?**
That data belongs to the directory. To change it, edit the consignee in Lists → Consignees. The phone only appears if you have permission to see contact data.

**What does “Enter at least one package line.” mean?**
A normal order needs at least one package line. Press **Add package** to add one. The last line cannot be removed.

**What does “The package type is required.” mean?**
A line has no package type and the company has no default type. Choose it on that line.

**What does “The number of pieces must be at least 1.” mean?**
**Pieces** must be a whole number from 1 up. Correct it on the marked line.

**What does “The COD amount cannot be negative.” mean?**
The **COD amount** is what is collected on delivery and does not accept negative numbers. Leave it empty or at 0 if there is no collection.

**Why can't I type the invoice number? It says “(automatic)”.**
Teikem assigns that client's numbering. The field is only enabled if the client assigns its own invoices (configured in the client record, under Numbering). If a number is sent anyway, the server answers “The invoice number is assigned by Teikem for this client; leave it blank.” (400): leave the invoice at “(automatic)”.

**What does “The number cannot exceed 40 characters.” mean?**
The invoice number (or order number, in a special delivery) accepts at most 40 characters. Shorten it.

**What does “The due date cannot be before the ready date.” mean?**
In the form, **Due** cannot be a date earlier than **Ready**. Correct one of the two.

**What does “Repeated invoice for this consignee” mean?**
You typed an invoice number that another active order of that consignee already uses, and the consignee record allows repeating it (409). Choose **Do not create the shipment** to check the number (the **See order …** link opens the one that already uses it) or **Create anyway** to create it with the repeated number.

**What does “Consignee 'X' does not allow repeated invoices: order N already uses number F.” (409) mean?**
That consignee does not allow repeated invoices and the number is already on order N. The message appears under **Invoice no.** and the order is not created. Change the invoice number or choose another consignee.

**What do I do if it says the client exceeds its credit limit?**
The client's credit is not enough for this order (422); it is measured when saving. The message is “The client exceeds its credit limit: limit X, in progress Y, this shipment Z.”; if you do not have `orders.view_price`, it only says “The client exceeds its credit limit.”, without figures.
- If you have the `orders.credit_override` permission: a window opens with the notice and the **Authorize and save** button. When you press it, the order is created anyway, and who authorized it is recorded.
- If you do not have it: the order is not created and the screen says “The shipment was not created. Ask someone with the permission to authorize exceeded credit (orders.credit_override) to capture it.” Ask Billing or an administrator, or ask for the client's limit to be raised.

**What does “There is no rate in force for X/Y; set up the rate in the client's contract before confirming.” (422) mean?**
The client's contract has no rate for that service and package type. When **saving** an order this error no longer appears (Batch 106): the order is saved Submitted and unquoted. It appears when marking it as picked up: set up the rate (Clients → Contract → Rates) and try again.

**What do “The client is suspended; shipments cannot be created or confirmed.” (422) or “The client has been deactivated; only its history can be viewed.” (409) mean?**
The client's status prevents creating new orders. Reactivate the client from its record or choose another.

**What does “This client has no special services configured” mean?**
You checked **It is a special delivery**, but the client has no services in its current special services list. Add them in Clients → Contract → Special services. If the notice says the “Special services” component is off, turn it on in the contract's billing model.

**Why is the “It is a special delivery” switch disabled?**
Reading the client's special services requires the `contracts.read` permission (Lists module). Ask for it, or capture a normal order.

**What does “The special service is required for a special delivery.” mean?**
With special delivery enabled you have to choose the service in **Special service**. If the server answers “The special service is not in force for this client.” (400), that service was already closed: choose another.

**Why doesn't a special delivery ask for a driver?**
On the web the driver is assigned afterwards, in the order file with the **Assign driver** button (see Batch F-C2). The order is born without packages, without COD and without a driver.

**The form is still open and shows an error. Was the order saved?**
No. When the server returns an error on creation, the order is not created. Correct what the message says and save again: it is not duplicated.

## Batch F-C2 — Order file (2026-10-12)

Questions about the screen that opens when pressing an order in **Operations → Orders**. Messages appear exactly as the screen shows them. The full chapter is “Orders: the order file” (screens manual).

**Why can't I edit an order's data?**
It is only edited while the order is Submitted (the initial status) and with the `orders.edit` permission. After marking it as picked up, the **Detail** panel says “This shipment has moved past Submitted: its data is shown for reference and is no longer edited here.” and there is no **Save changes** button. If your user does not have `orders.edit`, it says “Your user cannot edit shipments: the data is shown for reference.”

**Why can't I change the client, the shipment number, the invoice or the packing?**
That data is set when the order is created and is not edited. If the order was created wrong, cancel it (or delete it if it is still Submitted) and create another.

**The “Save changes” button is off. What is going on?**
It only turns on when you change something. If you changed nothing, there is nothing to save.

**Why can't I remove a date (Ready or Due)?**
“The date cannot be removed; choose another one.” appears. The system does not allow leaving empty a date that already had a value. Choose another date.

**What does “The record was modified by another user; reload and try again.” mean?**
Someone else saved changes to the same order while you were editing it. The screen reads the order again and **keeps what you had typed**. Review the new data and press **Save changes** again.

**What does “A special delivery has no package lines or COD.” mean?**
A special delivery is a service from the client's list; it has no packages or COD. In its file those fields do not appear; the other data (consignee, dates, comment…) is editable.

**What does “The current status does not allow the action 'EDIT_CARGO'.” mean?**
The order already left the status in which it is edited (someone confirmed it while you had it open). Reload the screen.

**I changed consignee and “Repeated invoice for this consignee” came out. What do I do?**
The order has an invoice number you typed and the new consignee already has another order with the same number. If that consignee allows repeated invoices, press **Save anyway**; if not, choose another consignee.

**Why don't I see the “Mark as picked up” button?** (formerly “Confirm”)
The order is no longer Submitted, or your user does not have `orders.edit` (nor `orders.credit_override`). An order is marked as picked up only once.

**When marking as picked up, “The client exceeds its credit limit” comes out. What do I do?**
The client's credit is not enough for this order. If you have `orders.credit_override`, press **Authorize and mark as picked up**: the order moves to Pickup and who authorized it is recorded. If you do not have it, notify Billing or an administrator, or ask for the client's limit to be raised. Without `orders.view_price` the message does not bring the figures.

**I am from Billing and the button says “Authorize and mark as picked up”. Why?**
Your role has `orders.credit_override` but not `orders.edit`. That permission only serves to authorize a credit excess; if the credit is not exceeded, the server answers “Missing permission 'orders.edit'.”

**What does “There is no rate in force for X/Y; set up the rate in the client's contract before confirming.” mean?**
The client's contract has no rate for that service and package type (X/Y). Set it up in Clients → Contract → Rates and mark the order as picked up again.

**Why don't I see the “Quote” panel?**
The `orders.view_price` permission is missing (“View order prices”). Without it the quoted amount is not visible either. The Company admin and Billing roles include it out of the box.

**What is the “Quote” button for? Does it change the order?**
It shows a preview of how much the order would cost with the current rates and how the client's credit ends up. **It saves nothing.** To save the new amount use **Re-quote** (on a Submitted and quoted order).

**When does the “Re-quote” button appear?**
On an order already quoted and in a status that allows it (out of the box, only **Submitted**; before Batch 106 it was Confirmed), with `orders.edit`. If “The current status does not allow the action 'REPRICE'.” appears, the order is no longer in a status that allows re-quoting.

**How do I cancel an order? Can it be undone?**
Press **Cancel the shipment**, write the **reason** (mandatory, maximum 500 characters) and confirm. To undo it, **Reopen** is needed (`orders.reopen` permission). If it was a special delivery with a driver, its trip is also cancelled. You need `orders.cancel`. A Delivered, Cancelled or In warehouse order can no longer be cancelled.

**Why won't the comment let me save? It says “The comment allows at most 500 characters.”**
The comment for cancelling, changing status or assigning a driver allows up to 500 characters. Shorten it.

**What is the difference between cancelling and deleting?**

**Delete the shipment** is only possible on a Submitted order: it frees the shipment and packing numbers. An order already picked up can only be cancelled. If you try to delete one already picked up, “A shipment can only be deleted in its initial status; use cancellation.” appears.

**What is Failed attempt for? Why is there no button to advance the order?**
It is a detour recorded by hand (On hold and Partial no longer exist since Batch 106). The normal advance of the order (in transit, delivered…) is done by the dispatch, routes and deliveries screens, not by the file. To mark it as picked up, cancel or reopen use the buttons above.

**The order had a Failed attempt. How do I return it?**
Press **Return to** and the name of the stage it left (for example “Return to In transit”). It is the only return: you cannot jump to another stage.

**Why don't I see the “Assign driver” button?**
It only appears on a **special delivery** that is on the path up to **In transit**, and if you have `trips.dispatch` and the Lists module is turned on. On a normal order the driver is assigned in the Dispatch room. When the order already has a driver the button says **Reassign driver**.

**When assigning a driver I cannot choose any / it says I cannot look up the drivers.**
Your user does not have the `fleet.view` permission. Ask the administrator for it. If the list says “There are no active drivers”, create or reactivate a driver in Fleet → Drivers.

**What does “The driver is not available for dispatch: …” mean?**
After the colon the message says the reason (expired license, inactive driver, expired certification…). Choose another driver or resolve the reason in Fleet → Drivers.

**What does “The shipment is already assigned to that driver.” mean?**
You chose the same driver the order already has. Choose another, or press **Back**.

**What does “The special delivery already reached its destination or finished; the driver cannot be assigned or reassigned.” mean?**
The order already moved past **In transit**, is in a detour or finished. The driver can no longer be changed.

**What happens to the previous driver if I reassign?**
Their trip is cancelled and a new one is created for the chosen driver.

**Where do I put who receives and their phone?**
In the **Phones & emails** panel on the right: **Add phone** or **Add email**. The caption (for example “Receives: Carmen Ortiz”) goes in **Label**. You need `contacts.manage` and `orders.edit`; without them it is read-only.

**I don't see the “Custom fields” panel.**
The company did not define custom fields for orders (or the module is off). The administrator defines them in System. Filling them in requires `orders.edit`.

**Why are there no label or proof of delivery (POD) buttons?**
They are not on the web yet; they will arrive in another delivery.

## Batch F-C3 — Quick entry and order import (2026-10-12)

**What is the difference between “Quick” and “Detailed”?**
**Quick** is a grid where each row is an order of 1 package and 1 piece, to capture many in a row with Tab and Enter. **Detailed** is a form for an order with several packages, dates, invoice or special delivery. The screen remembers the last one you used.

**I saved an order in Quick and it stayed Submitted. How do I mark it as picked up?**
Quick saves the order Submitted (quoted if the client has a contract and rate). Open it from the list (**Open the shipment** arrow) and press **Mark as picked up** when it is picked up.

**Why does the Shipment no. say “(automatic)” and I cannot type?**
Because Teikem assigns that number for that client. It can only be typed if the client record has it enabled that the client assigns its own order numbers.

**I pressed Enter and the row was not saved.**
If the consignee list was open, that Enter chose the consignee. Press Enter again. If there is a message in red below a field, correct it: what you typed is not lost.

**“The package type is required.” / “The service type is required.” in quick entry.**
The company has no default package or service. Choose them on the row; the service is kept on the following rows.

**“The client is suspended; shipments cannot be created or confirmed.”**
That client cannot receive orders for now. What was captured stays on the row; choose another client or wait for them to be reactivated.

**The connection was cut while saving. Was the order duplicated?**
No. Press Enter again: it retries with the same key and the server does not create a second order. Check the list if in doubt.

**I don't see the “Import” button.**
You need the `orders.create` permission. To choose or create templates you also need `orders.edit`.

**I cannot choose the CSV file.**
The file selector is enabled when there is an active template **and** a chosen client. Create the template (for example **Create “Standard format”**) and choose the client.

**Accents and the ñ come out broken in the preview.**
The file is not in UTF-8. In Excel use “Save as → CSV UTF-8”.

**“The file exceeds the limit of 5,000 rows or 2 MB.”**
Split the file into smaller parts.

**“The template must include consigneeName or consigneeCode.” / “The template must define the package: packageType or pieces, in a column or in defaults.”**
The template needs to say where the consignee is (name or number) and the package (type or pieces), in a column or as a default value.

**“An import template with that name already exists.”**
Use another name, or edit the existing one with **Edit**.

**How do I delete a template?**
They are not deleted: use **Deactivate this template**. With **Show inactive templates** you can **Reactivate** one.

**In the preview it says “Will be created: …” on the consignee. What does it mean?**
That consignee is not in the client's directory or among the shared ones; on import it is created with the file's data. If it already existed with another name or address, correct it in the file.

**A row says “With errors”. Is it imported?**
No. Rows with errors are skipped and the others are imported. Correct the file and import only those rows again.

**What happened to “Confirm orders on import”?**
It no longer exists (Batch 106). Each imported order is born Submitted, quoted if the client has a contract and rate and with the credit measured. To move it to Pickup, use **Mark as picked up** in its file.

**I don't see “Authorize exceeded credit”.**
You need the `orders.credit_override` permission.

**In the result a row says “The client exceeds its credit limit.”**
That order was not created; the others were. Ask someone with `orders.credit_override` to import with **Authorize exceeded credit**, or correct the client's credit limit.

**“The batch was already confirmed; validate a new file to import more shipments.”**
That file was already imported (for example, from another window). Look at the Orders list before importing it again.

**What does “Discard file” do?**
It clears the review without creating any order and lets you choose another file.

## Batch F-D1 — Drivers and rates (2026-10-12)

Questions about the **Lists → Drivers & rates** screen. The messages appear exactly as the screen shows them. The full chapter is “Drivers & rates” (screens manual).

**What is the difference between “Active”, “Availability” and “Delete driver”?**

**Active** turned off takes the driver out of dispatch without losing anything, and it can be turned back on. **Unavailable** takes them out of dispatch for a while (vacation, leave) and is reverted with **Available**. **Delete driver** is a permanent deactivation that cannot be undone.

**I deactivated a driver and they disappeared from the list. Are they lost?**
No. The list only shows active ones: turn on **Show inactive** to see them with the **Inactive** chip. Their record stays open on the right.

**What happens when I delete a driver?**
Their user is unlinked, their devices become inactive, they lose their zone, their open rates are closed today and their paid trips are kept so you can pay what is pending. They can no longer be edited or reactivated: the screen shows “The driver was deleted; only their history can be viewed.”

**“A driver with that code already exists.”**
Another driver uses that code (even if deleted). Use a different one; the code cannot be changed afterwards.

**Why can't I change the driver's code?**
It is fixed when the driver is created. If you made a mistake, create a new one and delete the wrong one.

**What is the “Area”?**
It is the name of the dispatch zone you chose; it is not typed separately. To change it, choose another **Zone**.

**Why can't I remove the Hire date?**
A driver who already has a hire date cannot leave it empty: the screen shows “The hire date can’t be removed; pick another one.” Choose another date.

**I don't see the “Linked user” field to change it.**
You need the `admin.users` permission in addition to `fleet.manage`. Without it, the user is shown but cannot be changed.

**“The user is already linked to another driver.”**
A user can belong to only one driver. Choose another user or remove the link from the other driver.

**“The record was modified by another user; reload and try again.”**
Someone else saved the driver before you. The screen keeps what you typed: review the new data and press **Save changes** again.

**What do “Expires in N days” and “Superseded” mean in licenses?**

**Expires in N days** warns that 30 days or fewer remain. **Superseded** means there is another license or certification of the same type that expires later: the old one no longer counts.

**“The expiry date can’t be before the issue date.”**
The dates are reversed. Correct them, or leave the expiry date empty if it does not expire.

**What is the difference between “no rate” and $0.00?**

**No rate** means nothing has been configured for that case yet. **$0.00** is a rate you set on purpose (for example, the first attempt is not paid separately).

**How do I change a rate without losing the previous one?**
Press the pencil on the row, type the new rate and **Effective from** (today by default). The previous rate is closed that day and stays in the **history**.

**“The date cannot be earlier than today: the rate history is not rewritten.”**
You chose an effective date that has already passed. Choose today or a future date.

**I edited a rate with a future date and the row no longer has buttons. Why?**
That rate already has a closing date (when the new one starts). Only open rates can be edited or closed. The new one appears with **View history** and has its own buttons.

**How do I remove a rate?**
With **Close rate**. It is never deleted: it stays in the history (**View history**).

**“The driver already has a current rate for … ; edit that rate or close it before adding another.”**
There is already an open rate for that service and package (or attempt, or trip type). Edit it or close it first.

**“The rate is already closed; add a new one if you need to pay it again.”**
You tried to change a rate that already has a closing date. Add a new rate.

**“The value allows at most 4 decimals and must be less than 100,000,000,000,000.”**
The rate has too many decimals or is too large. Use up to 4 decimals.

**What does “+ Add attempt” do?**
It adds an attempt level (Attempt 3, 4…) for **the whole company**, not just this driver. It does not create rates: the level stays “no rate” until you set it. A level cannot be removed.

**“The maximum of 20 attempt levels has been reached.”**
No more levels fit. Use the existing ones.

**The “Trip type” selector says “No special services — add them in Clients & contracts”.**
Trip types come from the company's special services. Create one in Clients & contracts and come back.

**Is the pay formula per driver?**
No: there is a single one for the whole company. Changing it affects how the pay of every driver is calculated.

**Does the preview change my rates?**
No. It only calculates an example (2 attempts: 1 failed and 1 delivered) with the rates of the chosen driver. “(no rate configured)” marks what is missing and adds $0.

**“The trip date can’t be in the future.”**
A trip is recorded on the day it happened or later. Choose today or a past date.

**Why did a trip come out at $0.00 with “No rate”?**
The driver had no rate for that trip type on that day. The amount is frozen when recorded: if you add the rate later, that trip does not change. Cancel it and record it again if needed.

**“The trip comes from a special delivery; cancel the shipment or reassign the driver.”**
That trip was created by an order. Cancel it by cancelling the order, or by reassigning the driver from the order.

**I don't see the rates panels, trips or the formula.**
You need the `driverpay.view` permission. To change them, `driverpay.manage`.

**Why don't I see any device?**
The driver app enrolls them when the driver signs in for the first time. From here they can only be viewed and deactivated.

## Batch F-D2 — Fleet: vehicles, documents and availability (2026-10-12)

Questions about the **Lists → Fleet & maintenance** screen. The messages appear exactly as the screen shows them. The full chapter is “Fleet & maintenance” (screens manual).

**What is the difference between “Active”, “Availability” and “Retire permanently”?**

**Active** turned off takes the vehicle out of dispatch without losing anything, and it can be turned back on. **In maintenance** takes it out of dispatch for a while and is reverted with **Available**. **Retire permanently** cannot be undone.

**I deactivated a vehicle and it disappeared from the list. Is it lost?**
No. The list only shows active ones: turn on **Show inactive** to see it with the **Inactive** chip. Its file stays open on the right.

**What happens when I retire a vehicle?**
It becomes inactive, leaves dispatch availability and no longer counts in “Expiring documents”. Its history and documents are kept, but it can no longer be edited or reactivated: the screen shows “The vehicle is permanently retired; only its history can be viewed.”

**Why can't I change the vehicle's code?**
It is fixed when the vehicle is created. If you made a mistake, create a new one and retire the wrong one.

**“A vehicle with that code already exists.”**
Another vehicle uses that code (even if retired). Use a different one.

**“An active vehicle with that VIN already exists.”**
Two active vehicles cannot have the same VIN. Check the VIN. It also appears when reactivating a vehicle whose VIN is already used by another active one.

**“The record was modified by another user; reload and try again.”**
Someone else saved the vehicle before you. The screen keeps what you typed: review the new data and press **Save changes** again.

**Why can't I leave the year, the weight or the stop limit empty?**
A number that was already saved cannot be removed: the screen shows “This value cannot be left empty once saved; enter another one.” Texts (plate, make, model, VIN) can be cleared.

**“The odometer cannot be lower than the last recorded reading (… km on …).”**
There is already a fuel fill or a closed work order with more kilometers. You can correct a data-entry error, but not below that reading.

**Why does the insurance say “Superseded”?**
Because there is another insurance policy for the same vehicle that expires later. The old one does not block, does not count in “Expiring documents” and stays in the list as history. There is no need to remove it.

**Can I attach the insurance PDF?**
No: for now documents only carry type, number and dates.

**“The expiry date can’t be before the issue date.”**
The dates are reversed. Correct them, or leave the expiry date empty if it does not expire.

**What does “Expiring documents” show?**
The documents of active vehicles and the licenses and certifications of active drivers that have already expired or expire within the window (30 days by default). A document that has already been renewed does not appear. Use **Within (days)** to widen it (up to 365) and **Include expired** to hide the ones that have already expired.

**“Enter a whole number between 0 and 365.”**
The “Within (days)” window only accepts 0 to 365. While the value is invalid, the list keeps the last valid window.

**An expired document does not appear in “Expiring documents”.**
Check that the vehicle or driver is active and not retired, that **Include expired** is turned on and that there is no newer document of the same type (the old one is superseded).

**“The zone has assigned drivers; reassign them before deactivating it.”**
The zone is the primary zone of some driver. Change the zone of those drivers in **Drivers & rates** and deactivate it again.

**“The zone has open routes; close them or move them to another zone before deactivating it.”**
There are unclosed routes in that zone. Close them or move them to another zone.

**Where are the municipalities and postal codes of a zone set up?**
In Dispatch (a later batch). Here only the code, the name and whether the zone is active are kept.

**What do “Blocks” and “Warning” mean in Availability?**

**Blocks** prevents dispatching the driver or vehicle (it becomes “Unavailable”). **Warning** is worth reviewing but does not prevent dispatching (for example, a license that expires in 12 days).

**My driver shows “Unavailable” with “No valid license.”**
They have no license registered, or all of them are expired on the chosen date. Add or renew the license in **Drivers & rates**.

**My vehicle shows “Unavailable” with “… expired on …”.**
It has an expired document (one not superseded by a newer one). Renew it in the vehicle's file.

**My vehicle does not appear in Availability.**
Permanently retired vehicles and drivers are not shown. Also check the search box and **Only available**.

**I don't see “New vehicle” or the buttons of the file.**
You need the `fleet.manage` permission. With `fleet.view` everything can be viewed but nothing can be changed.

## Batch F-D3 — Maintenance and fuel (2026-10-12)

Screen: [Maintenance, work orders and fuel](frontend/fd3-mantenimiento-combustible.md) (Lists → Fleet & maintenance). Server reference: [chapter 04](04-flota-choferes-mantenimiento.md), sections 5 to 7.

**What does “No history” mean in Maintenance?**
The program's last service or the vehicle's odometer reading is missing, so there is nothing to calculate with. In a program for one vehicle, enter the **Last service**; in one by type, close a work order of that program for that vehicle. Also check that the vehicle has an odometer.

**When does “Due soon” appear?**
When 10 % or less of the interval remains (for example, 500 km in a 5,000 km program). With basis **Both**, the worse of the two counts rules.

**My program by type shows one row per vehicle of that type. Is that normal?**
Yes: a program by type is evaluated vehicle by vehicle. Use the **Vehicle** filter or **Search…** to see one.

**“Choose the vehicle or the vehicle type of the program, not both.”**
A program applies to one vehicle or to one type, never to both nor to neither. In **Applies to** choose an option and fill in the field that appears.

**“A mileage program needs an interval in km greater than 0.” / “A time program needs an interval in days greater than 0.”**
The trigger needs its interval: enter **Every (km)** with Mileage or Both and **Every (days)** with Time or Both.

**“The interval in days allows at most 36500.” / “The interval in days must be a whole number.”**
Enter whole days, from 1 to 36500.

**“The last service date cannot be in the future.” / “The last service reading cannot be negative.”**
The last service already happened: use today's date or an earlier one and a reading of 0 or more.

**“The value allows at most 1 decimals and must be less than 100,000,000,000.”**
Kilometers allow a single decimal (the message says “1 decimals”; that is how the server writes it). Costs allow up to 4 decimals and liters up to 3.

**“This value cannot be left empty once saved; enter another one.”**
A date, a reading or a cost that already has a value cannot be cleared, only changed. Texts (supplier, notes, station) can be left empty.

**How do I create the work order for an overdue program?**
In the **Preventive maintenance** panel press the **Create work order** icon on its row. The order comes out as preventive, with the vehicle and program chosen.

**“A corrective order is not linked to a preventive program.”**
If you choose a **Preventive program**, the **Maintenance type** must be Preventive. Change the type or remove the program.

**“The maintenance program does not apply to this vehicle.” / “The maintenance program is inactive.”**
The program belongs to another vehicle or another type, or it was deactivated. Choose another one or reactivate it in **Maintenance programs**.

**I can't create an order or register a fill: “The vehicle is inactive; reactivate it to register work orders or fuel fills.”**
The vehicle is inactive or permanently retired. Reactivate it in its file (if it is permanently retired, that is not possible).

**I added a task and the order's costs changed.**
With tasks, the order's **Labor** and **Parts** are the sum of the tasks; the costs you had typed by hand are replaced. Without tasks they are entered on the order.

**“The order has tasks: labor and parts costs are calculated from the sum of its tasks.”**
Do not type Labor or Parts on the order while it has tasks: change them in the tasks.

**“The current status does not allow the action 'EDIT_WORK_ORDER'.”**
The order is **Closed** or **Cancelled** and can no longer be edited (nor its tasks). If more work is needed, create a new order.

**“The order has N incomplete task(s); mark them as completed or remove them before closing it.”**
Mark the pending tasks as **Completed** or **Remove** them and press **Close order** again.

**“Enter the odometer reading to close an order of a mileage-based program.”**
When the order's program is by mileage (or Both), enter the **Odometer at closing (km)**.

**“The closing date cannot be in the future.”**
Use today or an earlier date.

**I started an order and my vehicle no longer appears in Availability.**
That is expected: when started, the vehicle moves to **In maintenance** and Availability says “Work order OT-… is in progress.” It goes back to **Available** when the order is closed or cancelled (if it has no other one in progress).

**Can I reopen a closed or cancelled order?**
No. They are final and read-only.

**“The record was modified by another user; reload and try again.”**
Someone else saved the order before you. The screen reads it again and keeps what you typed: review and save again.

**Km driven, Km/L and Cost/km show “—”.**
A vehicle's first fill has no previous fill with an odometer to compare against, and a fill without an odometer is not measured. Record the **Odometer** on fills.

**“The odometer reading (… km) is lower than that of a previous fill of the same vehicle (… km on …).”**
The odometer must grow with the date. Check the reading or the date of the fill: it cannot be lower than that of a previous fill nor higher than that of a later one.

**“Liters must be greater than 0.” / “Enter the total cost of the fill.” / “The total cost cannot be negative.”**
Enter the liters filled and the cost (0 if it was free).

**“The fill date cannot be in the future.”**
Up to 5 minutes of clock drift ahead are allowed; beyond that, correct the date or time.

**I removed a fill by mistake.**
Register the fill again. Removing it does not erase the history nor lower the vehicle's odometer.

**The Summary by vehicle does not match what I see in the table.**
The summary adds up **the whole filter**, not just the page you are viewing. Its Km/L uses the kilometers and liters of the fills that could be measured.

**I filled up fuel and my maintenance moved to “Overdue”.**
The fill's odometer reading raises the vehicle's odometer, and the mileage due date is calculated with that odometer.

**I don't see the “New program”, “New work order”, “Register fill” buttons or the actions of the file.**
You need the `fleet.maintenance` permission. With `fleet.view` everything can be viewed but nothing can be changed.

## Batch F-E1 — Scan station (2026-10-12)

Screen: Operations → **Scan station** (needs `trips.scan`). Manual: [fe1-escaneo.md](frontend/fe1-escaneo.md).

**I scanned an order and it says “Assigned to route …”. What changed?**
It was only placed inside that route. Scanning does not change the order's status, nor the route's driver or vehicle.

**“It was already on route …”.**
The order already had a route (you scanned it before or someone else assigned it). Nothing needs to be done.

**“The shipment was not found.”**
No shipment number, package number or client invoice number matches what was scanned. Check the code or scan the package number.

**“There are several shipments with that code; scan the package.”**
Two clients can have the same shipment number. Scan the package number, which is unique.

**“There is no open route for zone … on date …; it stays unassigned.”**
There is no open route (draft or planned) for the order's zone that day. Plan the day or create the route in **Dispatch room** and scan again. If you are working the routes of another day, change **Route day**. Scanning never creates routes.

**“The dispatch zone could not be resolved by postal code or town; it stays unassigned.”**
The delivery address is not in any zone. Add its postal code or town to a zone (Lists → Fleet & maintenance → Zones) or assign the order by hand.

**“The postal code or town belongs to several zones (…); it stays unassigned.”**
The address falls in more than one zone. Leave it in a single one or assign the order by hand.

**It shows “Not eligible” with a reason.**
The order cannot go on a route yet. The reasons and what to do are in the screen's manual (for example, “The shipment is in “Submitted”; mark it as picked up before assigning it to a route.”: confirm the order in **Orders**).

**“Scan or type a code.” / “The code cannot exceed 40 characters.”**
The box was empty or the code is too long. Nothing was recorded.

**“Not registered” with a server or connection message.**
The read did not reach the server. Check the connection and scan again; it did not go into the list.

**I scanned the same thing twice and the counter did not go up.**
The counter counts distinct assigned orders. The second read appears in the list as “Already there”.

**I reloaded the page and the list of scanned items disappeared.**
That is normal: the list lives only in the browser. The orders already assigned stay on their route. Opening an order and coming back with “Back” does not clear it.

**I don't see “Pending to scan”, the filters or “N of M”.**
You need the `trips.view` permission. Without it you can still scan. The **Zone** filter needs `fleet.view` and the **Client** filter, `clients.read`.

**I don't see the “Open” button in the order window.**
You need `orders.view`.

**I can't hear “Yes”, “No” or “Already”.**
Check that the button says **Voice on**, the volume of the computer and that the browser has voices installed. If it says **Voice unavailable**, that browser cannot speak: the results are shown on screen.

**Where are the inbound and collection reconciliation modes, and the missing report?**
The modes do not exist yet: the station only scans the outbound. The **Missing report** does exist since F-G2 (2026-10-12): a button next to
the voice; it reads all the pending ones with the screen's filters, grouped by zone, and comes out on the station's document printer.

## Batch F-E2a — Dispatch room (2026-10-12)

Screen manual: [fe2a-despacho.md](frontend/fe2a-despacho.md). The route messages are in chapter [05](05-trips-y-rutas.md).

**I don't see the “Dispatch room” menu.**
You need the `trips.dispatch` permission. Each button requires its own: `trips.plan` (Plan the day, New route, Reassign by zone, add/remove/sort orders, change driver or vehicle, Delete route), `trips.optimize` (Optimize) and `trips.close` (Close route).

**“Your user cannot view routes.”**
You are missing `trips.view`. Without it the screen cannot show routes or unassigned orders.

**The driver and vehicle appear as text and it says I can't change them.**
You are missing `fleet.view` (or the Lists module is turned off). Ask for access; in the meantime, someone else can assign them.

**“There are shipments that cannot be assigned to the route.”**
At least one selected order is not eligible; below, each number appears with its reason (for example `The shipment is in “Submitted”; mark it as picked up before assigning it to a route.` or `The current status does not allow the action 'ASSIGN_TRIP'.`). None was added. Correct the order or unselect it and press again.

**“The order is already assigned to route {código}.”**
Someone else put it on that route before. The list refreshes by itself; choose another order.

**“Add at most 200 shipments per request.” / “A route allows at most 300 stops.”**
Add in rounds of up to 200 and use another route if the current one reached 300.

**The “Add to route” button is disabled.**
No orders are selected, or the active route is not in Draft or Planned (a dispatched route does not receive orders). The screen explains it under the button.

**I don't see ▲, ▼, the trash can or “Optimize” enabled.**
The route was already dispatched (its contents are frozen) or you are missing `trips.plan`/`trips.optimize`. “Optimize” is also disabled if the route has no stops: `The route has no stops to optimize.`

**“The driver is not available for dispatch: …” / “The vehicle is not available for dispatch: …”**
The chosen one has an impediment on that date (expired license, vehicle in maintenance…). Choose another or resolve it in Drivers or Fleet. In the lists they appear as “unavailable”.

**“The record was modified by another user; reload and try again.”**
Someone else changed the route while you were editing it. The screen has already refreshed: review and repeat the action.

**“Route {código} was already dispatched; it cannot be edited or deleted.”**
After dispatching, the route cannot be edited or deleted (except driver and vehicle if your company allows it).

**“Route {código} cannot be dispatched: …”**
The route has a **Blocks** item (no driver, no vehicle, no stops, driver or vehicle unavailable, an order that is no longer eligible). Correct it on the route; in **Routes to dispatch** those routes cannot be selected.

**I dispatched and “Dispatch with notices” appeared.**
Routes with a **Warning** (amber) can be dispatched, but the system asks for confirmation once: review the warnings and press the button again. Dispatching cannot be undone.

**In the batch one route came out “dispatched” and another with an error.**
Each route is dispatched separately: the one that fails does not stop the others. Correct the one that failed and dispatch it again.

**“Route {código} cannot be closed: there are unfinished orders (…).”**
There are orders still on the way, held or without a result. Record their delivery or failed attempt (or release them) and close again.

**“Route {código} is not dispatched; dispatch it before recording its departure.”**
“Start route” only works with the route in Dispatched.

**I optimized and “Did not fit (N)” appeared.**
Those orders did not fit because of the vehicle's stops, weight or volume and went back to **Unassigned shipments**. Change the vehicle or assign them to another route.

**The optimization does not show “-12 %” of distance.**
It is only compared with a previous optimization of the same route, and distance only exists if the addresses have coordinates; otherwise, only the duration is compared. The first time it says there is nothing to compare against.

**“Reassign by zone” said nothing changed.**
There were no open routes (Draft or Planned) in those zones and date. It is not an error; dispatched routes are not touched.

**“Plan the day” left orders unassigned.**
Orders without a zone or with an ambiguous zone are not touched (**Shipments without a zone**), and those that are not eligible stay in **Shipments skipped** with their reason in the zone's notice. Fix the zone or the order and repeat: nothing is duplicated.

**Where is the COD of each order?**
It does not appear on this screen: the routing system does not handle amounts. The route map, dragging stops and the manual pin are explained in the “Batch F-E2b” section below.

## Batch F-E2b — Route map, dragging and manual pin (2026-10-12)

Screen manual: [fe2a-despacho.md](frontend/fe2a-despacho.md) (sections “The route map”, “Sort and remove orders” and “Set a manual pin”). The route messages are in chapter [05](05-trips-y-rutas.md).

**Is the map a street map?**
No. It is a schematic map: it shows where each stop is relative to the others (with its scale and north), without streets, place names or satellite imagery, and it has no 2D / Satellite switch. The dotted line joins the stops in order in a straight line; it is not the path along the streets. Seeing real streets would require an external map service, which this screen does not use.

**The map says “No stop has coordinates yet”.**
No stop on the route has a location (the addresses could not be located). Set a manual pin on each stop with **Set pin** (the pin button on each order in **Shipments on this route**). If you don't see the button, your user is missing the `trips.plan` permission or the route is no longer in Draft or Planned: ask whoever plans the route.

**A stop does not appear on the map.**
It has no location. It is in the **Without location: N** line below the map and also in **Shipments on this route** with the **No pin** mark. Press its shipment number to select it and use **Set pin**.

**What is the difference between an exact, approximate and manual pin?**

**Exact** (filled circle): the address was located precisely. **Approximate** (dotted circle with halo): only the center of the postal code or town is known, so the stop may be somewhere else in that town. **Manual** (diamond): someone set the location by hand with **Set pin**. If the arrival time or the order of an approximate stop matters to you, set a manual pin on it.

**Two pins are on top of each other.**
Several orders go to the same address. The map fans them out around that point so they can be seen and selected; each one carries its number.

**I pressed a pin and an order in the list was highlighted.**
That is expected: the pin and the order are linked. Pressing an order in the list highlights its pin, and the summary of the selected stop appears below the map.

**I don't see the drag handle, nor ▲ ▼, nor the pin button.**
They only appear with the `trips.plan` permission and while the route is in **Draft** or **Planned**. On a dispatched, in-progress or closed route, the map and the list are view-only.

**How do I reorder the stops?**
Hold the handle (the six dots to the left of the order), drag to the position you want and release. You can also use ▲ and ▼, or move the focus to the handle with Tab and press ↑ or ↓. The change shows instantly in the list and on the map.

**I dragged an order and it went back to its place.**
The system did not accept the change; the message says why (it appears above the route). It is almost always because someone else changed the route while you were sorting it. The screen has already refreshed: drag again.

**“The sequence must include exactly the stops of the current route, without repeats.”**
Someone added, removed or optimized the route while you were sorting, and the list you moved no longer matches the route. The list goes back to its previous order and refreshes; sort again.

**“The record was modified by another user; reload and try again.” (when dragging or setting a pin)**
Someone else changed the route. The screen refreshes by itself; review and repeat.

**Can I drag with my finger?**
Yes: the handle responds to touch. If it is hard on a phone, use ▲ and ▼.

**“Enter the latitude and the longitude.”**
One of the two numbers of the pin (or both) is missing. Type them in **Latitude** and **Longitude**.

**“The latitude must be between -90 and 90.” / “The longitude must be between -180 and 180.”**
The number is outside the possible range. It is common to put the longitude in the latitude field: in Puerto Rico the latitude is around 18 and the longitude around -66.

**“The latitude must be a number, e.g. 18.4655.” / “The longitude must be a number, e.g. -66.1057.”**
You typed something that is not a number (letters, “18°”, two dots). Use only digits, the minus sign and a dot or a comma for decimals (18.4655 or 18,4655).

**Where do I get the latitude and longitude?**
From any map or location application: when you mark a place, it shows its coordinates (latitude comes first). If there are already other stops with a location, you can also tap the reference map in the window to take an approximate position.

**I tapped the reference map and the position came out odd.**
The map has no streets, so that position is only approximate (the notice says so). Check the latitude and longitude before saving, or type the exact numbers.

**“Stop not found on this route.”**
The stop is no longer on the route (it was removed or the route was optimized while you had the window open). Close the window: the screen has refreshed with the current stops.

**“Route {código} was already dispatched; it cannot be edited or deleted.” (when setting a pin or reordering)**
Someone dispatched the route while you were working: the order and location of its stops can no longer be changed.

**I set a pin and the arrival time changed.**
That is expected: the system recalculates arrivals with the new location.

**I set a pin but the route order did not change.**
Setting a pin does not reorder the route. If it was already optimized, press **Optimize** again so the order takes advantage of the new location.

**The stop with a manual pin still says “Approximate location”.**
It shouldn't: when the pin is saved the mark changes to **Manual pin**. If you see it, refresh the screen; if it persists, contact support.

## Batch F-E3 — Route monitor (2026-10-12)

Screen manual: [fe3-monitoreo.md](frontend/fe3-monitoreo.md). The data comes from the monitor in chapter [05](05-trips-y-rutas.md) (section 9).

**Every route says “No ping” and the badge says “No driver signal”. Is it broken?**
No. There is no driver app sending its location yet, so there are no pings to show. When that app is in use, the last ping, the **LIVE** badge, the driver's dot on the map, the speed and the heading will appear on their own.

**The stops always say “Pending” and the progress is at 0 %.**
That is expected today: the one that marks a stop as on the way, delivered or failed is the driver app, which is not in use yet. The progress only goes up when that happens.

**Why can't I see the GPS signal or the phone battery?**
Because the system does not receive that data yet. When it does, they will be added; for now the screen only shows what exists (speed, heading and last ping).

**What does LIVE mean?**
That at least one of the routes shown has a ping from less than 2 minutes ago. If none has one, the badge says **No driver signal**. A ping from more than 2 minutes ago appears as **No recent signal**.

**“Could not refresh. Showing the last data received, at {hora}; it will try again.”**
The query to the server failed (connection down, server busy). What you see is the last data received, with its time; nothing was lost. The screen retries by itself every 30 seconds; you can also press **Refresh**.

**“Could not load the routes.”**
The first query failed and there is nothing to show yet. Press **Retry**; if it persists, check the connection or contact support.

**“There are no routes for this date”.**
That date has no dispatched or in-progress routes (draft or planned routes are not monitored yet). Change the date, turn on **Include completed** if you are looking for a route that is already closed, or dispatch the route from the **Dispatch room**.

**“No route matches the search.”**
The search looks at route code, driver, zone, vehicle and status. Clear the text to see them all.

**I searched and the numbers at the top did not change.**
That is on purpose: the totals count all the routes of the date and zone; the search only shortens the list. (The **Next to complete** card does use the routes being shown.)

**How often does it refresh? Can I stop it?**
Every 30 seconds while the tab is visible and **Following live** is on. Turn that switch off to stop it (the screen remembers it in this browser) and use **Refresh** whenever you want. With the tab hidden nothing is queried.

**A route with a red ⚠.**
The route has more stops than its driver allows. Change the driver or spread the stops from the **Dispatch room** (the **Open in Dispatch room** link takes you to that route).

**“No exact pin: N”.**
N stops have an approximate pin (center of the postal code or town) or have no location yet. In the Dispatch room they can be given a manual pin as long as the route is not dispatched.

**I don't see the “Open in Dispatch room” link or the zone filter.**
The link requires the `trips.dispatch` permission and the zone filter requires `fleet.view` (with the Lists module). Ask your administrator for them; without them the screen keeps working.

**Can I call the driver from here?**
No: the screen does not show phone numbers or contact details of the driver.

**Is the map a street map?**
No, it is schematic (the same as the one in the Dispatch room): it shows the relative position of the stops and the driver's dot, without streets or satellite imagery.

## Batch F-F1 — Custom fields (2026-10-12)

Screen Analytics → **Custom fields**. Chapter: [ff1-campos-personalizados.md](frontend/ff1-campos-personalizados.md).

**I don't see “Custom fields” in the menu.**
You need the `admin.customfields` permission and the Custom fields module turned on for your company. Ask your administrator.

**I created a field and it does not appear in the client record.**
Check that the field is **Active** (turn on **Include inactive** to see the deactivated ones) and that you created it in the right entity. If everything is fine, reload the record.

**“The field '…' already exists in …” and I don't see it in the table.**
It is probably **deactivated**: turn on **Include inactive** and press **Reactivate**, or use another key.

**Can I change the key of a field?**
No. The key is the internal name. Change the **Label**, which is what is shown; if you need another key, deactivate the field and create a new one.

**“The type of a field that already has saved values cannot be changed.”**
Some record already has a value in that field. Keep the type, or deactivate the field and create a new one with another key (the values of the old one are kept but do not move to the new one).

**Are the values lost if I deactivate a field?**
No. The field is no longer requested or shown, but its values are kept and are shown again on **Reactivate**.

**Can I delete a field forever?**
No: it can only be deactivated.

**I don't see the “New list…” button.**
Creating lists requires the `admin.catalogs` permission. You can choose a list that already exists or ask an administrator to create the one you need.

**I renamed an option of the list and the records changed.**
That is expected: selection fields point to the list's option, they do not store copies of the text.

**“Choose or create a value list”.**
Select, Multi-select and List reference fields need a list. Choose one or press **New list…**.

**“The list '…' does not exist.”**
The list was deleted or is not a value list (status lists do not work for fields). Choose another one.

**“Give the list a name and at least one option” / “A code cannot be built from: …”.**
When creating the list, type a name and options with letters or numbers separated by commas, of at most 40 characters each. Repeated ones are ignored.

**What do I type in “Format (regular expression)”?**
It is optional. For example `^CC-\d{3}$` accepts “CC-” followed by three digits. If you don't know how to use it, leave it empty and use only the lengths.

**What do the minimum and maximum of a Date mean?**
They are **days relative to today**: −30 is 30 days ago, 0 is today, 90 is 90 days from now.

**“The entity '…' does not support custom fields yet (its module is not built).”**
That is the case of entities such as Invoice: there are no records where values could be stored. Fields cannot be defined for them.

**Why aren't Warehouse and Contract in the list of entities?**
They do not have a record where the values could be entered yet. They will be added when they have one.

## Batch 41 — Routes edited by several people at once (2026-10-12)

**“Someone else is modifying this route at the same time; try again.” (409)**
You and someone else (or another process, such as the outbound scan) changed routes of the company at the same instant and the system could not apply your change. **Nothing was saved** from that action: wait a moment and repeat it (setting the pin, reordering, optimizing, adding or removing orders, dispatching…). If it keeps reappearing, contact support with the correlation number in the message. On other screens the same case says “Someone else is modifying this data at the same time; try again.” and is resolved the same way.

**Can “Someone else is modifying this data at the same time” appear when creating or confirming an order even if nobody else touches my data?**
Before 2026-10-12 it could: when several companies created orders at the same time, the credit balance check of one collided with the creations of the others. With the indexes of the “2026-10-12 (b)” section of the database (`db-update` applies them) that collision no longer happens. If the message appears when creating orders on an updated database, repeat the action (nothing was saved) and contact support with the correlation number.

## Batch F-A3 — Portal users and client quoter (2026-10-12)

**I don't see the “Portal users” panel in the client record.**
Your user does not have the `portalusers.manage` permission or your company does not have the **Client portal** module turned on. Ask your company's administrator. Without those two requirements the panel does not appear.

**“That email is not available for this company's portal.” (409)**
When inviting, that email is already invited, active or suspended **in this same client**, or it belongs to an internal user of your company or to an account of another company. The message is the same in every case, on purpose, so as not to reveal where it is registered. Use another email; if the person is already in this client, find them in the list and use **Resend invitation**, **Reactivate** or **Invite again** according to their status.

**I invited a person who is already in another client and it gave no error. Is that normal?**
Yes. The same person can belong to several clients of your company: they become **Active** instantly in this client (if they already had a password) and the list shows “Also in N other client(s)”. No email is sent to them.

**“The client has been deactivated; only its history can be viewed.” (409) when inviting, resending or quoting.**
A deactivated client does not accept anything new: no invitations, no resends, no quotes. Reactivate it from the header of the record. You can still **Suspend**, **Reactivate** and **Deactivate** its portal users and view the list.

**The person says the invitation link does not work.**
Press **Resend invitation** on their row (it only appears while they are **Invited**). The new link is valid until the date in the notice and only works once; the previous one stops working. If they already accepted, it is not resent: use **Deactivate** and **Invite again** if you need them to set a new password.

**“The invitation can only be resent to a user who has not accepted it yet (status INVITED).” (409)**
The person already accepted the invitation (or changed status) while you had the list open. Open the record again and look at their current status.

**“Only a portal user in status ACTIVE can be suspended.” / “Only a portal user in status SUSPENDED can be reactivated.” (409)**
Someone else changed the status before you. Open the record again and use the button that matches the current status.

**“The portal user is already deactivated.” (409)**
They were already **Disabled**. If you need them back, press **Invite again** on their row.

**Can I see or change the password of a portal user?**
No. Each person sets their own with the link in the email. If they lost it, **Deactivate** and then **Invite again**.

**I invited someone to the portal (or a consignee) and the email did not arrive.**
Check three things: (1) that the email is spelled correctly; (2) that the environment has the portal address configured (`Portal:WebBaseUrl`; without it the invitation is created but the email does not go out: the system administrator adds it and you press **Resend invitation**); (3) that it is not in the spam folder. In the test environment every email arrives in the test mailbox with the subject “[TEST → recipient]…”.

**I sign in to the portal and the “Confirm this computer” email sends me to the staff web instead of the portal.**
Since 2026-10-22 the link in that email goes to the **portal** (`Portal:WebBaseUrl`) when the person signing in is a portal user (client or consignee). If it still sends you to the staff web, the environment is missing that address: the system administrator adds it (see the deployment README) and the person tries to sign in again.

**In the portal, when enabling two-step verification, I only saw a long key and not a QR code.**
Now the **QR code** appears to scan it with the authenticator app (Google Authenticator, Microsoft Authenticator, Authy…). The written key is still there below in case it cannot be scanned. The new portal must be published.

**I invite a person to the portal and it says “That person already has a pending invitation in this client…”.**
They are already invited and have not chosen their password yet: there is no need (and no way) to invite them again. On their row press **Resend invitation** so the link reaches them again. If it says “That person already has access to this client's portal.”, they can already sign in; if it says they are suspended, **Reactivate** them on their row. When the email belongs to another company, to a staff member or to another client, it still says “That email is not available for this company's portal.” on purpose (who is registered is not revealed).

**How do I create shipments quickly in the portal?**
On the desktop web, **New shipment** opens the **Quick** entry by default: one row per shipment. Type the date, the consignee (searched by name, address or town; Enter takes the first one in the list and “+ New consignee” creates it in the same row), the description, the pieces, the weight, the COD and the notes. **Enter** moves to the next field and, on the last one, saves; you can also press “Save row”. The saved row turns green with its number and below it the new row appears with the same date (entry is done in batches of the day). If the server rejects a row (for example, credit), the message appears on the row without losing what was typed. At the top you can switch to **Detailed** (the card with all the fields and several packages); the choice is remembered. On the phone the Detailed entry always appears.

**Where are the portal reports?**
In the **Reports** tab (client only; the consignee does not have it): *Shipments of the period*, *Shipments by status*, *Cash on delivery (COD)*, *Invoices and balance* and, if your company offers warehouse, *Stock*, *Inventory movements* and *Inventory value*. Choose the report, set the dates (or a shortcut: this month, last month, last 30 days, this year), press **Generate** and, if you want to take it to Excel, **Export to Excel (CSV)**. Up to 5,000 rows are fetched per report; if there are more, shorten the dates. What you see is only from your client.

**The portal looks different from the staff web: can I set my colors and my logo?**
Yes, they are the same as **Tenant settings → Branding** (the staff web): on sign-in, the portal takes the colors of that branding (bar, buttons, links and surfaces; the state colors —green, amber, red— do not change) and, on the desktop web, shows your logo in the top bar (if there is no logo, the company name). The logo for a dark background (“inverted lockup”) is preferred; on the phone SVG logos are not drawn: also upload a PNG, JPG or WebP version. Before signing in (access screen) the usual branding is shown, because it is not yet known which company the person signing in belongs to.

**The portal on the computer looked like a narrow phone.**
Since 2026-10-22 on the desktop web (screen of 900 pixels or more) the menu goes on the left and the lists and details use a wide column; on the phone and in a narrow window the bottom tabs remain. Forms (sign in, create an order, profile) stay in a narrow column on purpose.

**When enabling the portal's two-step verification, what happens with the recovery codes?**
The same as on the staff web: on the **same screen** the codes appear (Copy, Download .txt or, on the phone, Save or share), the “I saved these codes somewhere safe” checkbox and the field for the code from your app. The “Verify” button is not enabled until you check the box and type a new 6-digit code from the app.

**I received the invitation: what do I do?**
Press “Choose my password”, type a password of at least 12 characters twice and save it. If it says “Invalid or expired invitation.”, the link was already used or more than 48 hours have passed: ask for the invitation to be resent.

**Why can't I see the invitation link on screen?**
It is a single-use secret: only the invited person receives it by email. The screen only tells you who it was sent to and until when it is valid.

**The Quoter gives 0 on a line.**
There is no current rate for that service and package, neither from the contract nor generic (the screen warns “There is no rate in force for X / Y: that line is quoted at 0.”). It is not an error: add the rate in the **Rates** tab of the contract.

**The Quoter total is not the sum of the subtotals.**
The **Dispatch fee** and the **COD fee**, which are charged once per order, are also added to the total.

**“The service/package type pair is repeated; send a single line per (service, package type) with the total pieces.” (400)**
Two lines of the Quoter have the same service and package. Combine the pieces into a single line.

**“The number of pieces must be at least 1.” / “The COD amount cannot be negative.” (400)**
Correct the marked field: pieces from 1 upwards (whole numbers) and COD from 0 upwards (or empty).

**The Quoter says there is no current contract.**
On the chosen date the client had no current contract: it is quoted with the company's generic rates and without dispatch or COD fees. To use a contract, activate it in the **Contract** tab.

**What prices does the Quoter show and who sees them?**
Prices from the client's **contract**; whoever has `contracts.read` sees them. The permission to see order prices does not change this.

## Batch F-F2 — Views & reports (2026-10-12)

Screen: Analytics → **Views & reports**. Chapter: [ff2-vistas.md](frontend/ff2-vistas.md). Server: [chapter 01 §8](01-plataforma-y-seguridad.md).

**“Give the view a name” / “Pick at least one column”**
The name is missing, or the view has neither columns nor a grouping field. Fill in what is missing and press **Save view** again.

**“The Sum function requires a field.” (also Average, Minimum and Maximum)**
An aggregate other than **Count** needs a numeric field. Choose it in that row of **Aggregates**.

**“A view '…' already exists for ….” (409)**
Another view of the same source already has that name (it may be a private one of someone else that you cannot see). Use another name. Since 2026-10-12 the name of a **deleted** view becomes free again and renaming to a repeated name gives this same message (before it gave “Internal error.”).

**“A view needs at least one column or one grouping.” (400) / “The name is required.” (400) / “FilterJson is not valid JSON.” (400) / “Source … does not combine with '…'.” (400)**
The server rejected what was sent. On the normal screen they should not appear (the screen already validates and only offers valid combinations); if they appear, review the indicated section and save again.

**“The platform's default items cannot be edited or deleted.” (403) / “Only the owner can edit or delete this item.” (403)**
**Default** views are not touched; to change them create your own view. Someone else's views can only be changed by whoever created them or whoever received edit permission.

**“View '…' not found.” (404)**
The view was deleted or is no longer shared with you. Go back to **Views**.

**“From cannot be later than To.” / “The custom range needs From and To.”**
Fill in or correct the dates of the **Date range** of the open view.

**Why don't I see the + button, Edit or Delete?**
The `analytics.manage` permission is needed. Without it you can open, search, sort and export.

**Why can't I choose people when sharing a view?**
Listing people requires the `admin.users` permission; without it you can share with **roles**.

**Why is the result cut off at 5000 rows?**
That is the maximum the screen loads. Narrow the view with filters or a date range; the export only includes what was loaded.

**Can a view be scheduled to be sent by email?**
Not yet (it is left for a later phase). Today it is opened and exported by hand.

## Batch 42 — Isolation between companies and faster lists (2026-10-12)

Chapter: [01 §3.6](01-plataforma-y-seguridad.md).

**Can a company see or change another company's data?**
No. Every query automatically carries the condition of the session's company (it comes from the token, never from the request). Requesting something from another company by id answers 404 “not found”, without revealing that it exists. Since 2026-10-12 that condition is resolved with indexes (faster) and the automated tests go through every company table to check that no query goes out without it.

**After updating, did what I see in the lists change?**
No: same rows, same order and same totals. What changed is the speed (order list, filters, “Sort everything filtered”, movement ledger, balances) and that one company's work no longer makes another wait.

**The order list is still slow after updating.**
`db-update` has not been applied on that database (it creates the `IX_Order_Tenant_Created` index of the “2026-10-12 (c)” section). Verify with the simulation (`db-update --dry-run`): if it lists “+ Crear índice IX_Order_Tenant_Created”, apply it (on a large database it takes a few seconds and locks the orders table while it is created: do it outside peak hours).

**The free-text search of orders is slow in the company with the most orders.**
That is expected: it searches the text within the shipment number, the invoice, the package and the consignee name of all its orders. For a specific order use the exact search by code (scan the package or type the full number: `GET /api/v1/orders/lookup`, immediate) or narrow the list by dates.

## Batch 43 / F-G1 — Printers & labels (2026-10-12)

Chapters: [12 — Printers & labels](12-impresoras-y-labels.md) and [FG1 — screen](frontend/fg1-impresoras.md).

**Does the Teikem server connect to my printers?**
No, never. The user's computer prints: directly with QZ Tray (a free program installed on each computer) or, as a fallback, with the PDF and the browser dialog. The server only signs, with the Teikem certificate, what QZ Tray requests.

**What does “QZ Tray not detected” mean?**
On this computer QZ Tray is not installed, is not open or its library is missing from the site (`/vendor/qz-tray.js`, support puts it there). Everything still prints, with the browser PDF. Install QZ Tray (“Install QZ Tray” link), open it and press “Detect printers on this computer”.

**I pressed Test and the browser dialog opened instead of printing directly.**
The chosen printer has the “browser PDF” connection, or QZ Tray is not connected on this computer (the notice says why: “QZ Tray is not running on this computer.”, “Direct printing is not available on this computer (QZ Tray or its library is missing).” or “QZ Tray could not print (…).”). Change the printer to “Direct (QZ Tray)” with its “System name”, or open QZ Tray.

**QZ Tray asks “Allow / Permitir” every time I print.**
The server does not have the Teikem certificate configured (the screen says so: “The server has no QZ Tray certificate…”) or that computer does not have the `override.crt`. Support: chapter 12 §9 (generate the certificate, configure `Printing:Qz` on the server and install `override.crt` on each computer).

**What does “Direct printing with QZ Tray has no certificate configured on this server; …” mean? (409)**
`GET /printing/qz/certificate` (or `/qz/sign`) answers it when the server does not have the certificate or the key (or the key is damaged). It does not prevent printing: QZ Tray asks “Allow” or the browser PDF is used. Support configures it (chapter 12 §9.3).

**“Type the printer name.” / “The name cannot exceed 80 characters.” (400)**
The name is required and up to 80 characters. Use the same name the printer has in Windows.

**“An active printer with that name already exists.” (409)**
Another active printer already uses that name (case-insensitive). Change the name, or deactivate the other one. When reactivating a printer the same happens if its name is already used by another active one.

**“Choose the use of the printer: LABEL (labels) or DOCUMENT (documents).” / “Unknown printer use: 'X'.” (400)**
When creating a printer you must say whether it is for labels or for documents.

**“The use of the printer cannot be changed; create another printer.” (400)**
The use is fixed when the printer is created. For a printer with another use, create another one (and deactivate the old one if it is no longer useful).

**“Unknown printer connection: 'X'. Use BROWSER (browser PDF) or QZ_TRAY (direct with QZ Tray).” (400)**
There are only two connections. Network printing from the server does not exist on purpose (the server never connects to printers).

**“A label printer uses 4X6, 4X4 or 4X2 labels.” / “A document printer uses letter paper (LETTER).” (400)**
The size has to match the use. For 2×4 “envelope-style” labels use 4×2 with “Rotate 90°” if the printer feeds it sideways.

**“The model cannot exceed 80 characters.” / “The note cannot exceed 300 characters.” / “The system name cannot exceed 200 characters.” (400)**
Shorten the text.

**“The printer is inactive; reactivate it before making it the default.” / “The template is inactive; reactivate it before making it the default.” (409)**
Reactivate it first (or choose another active one).

**“Printer not found.” / “Label template not found.” (404)**
It does not exist or belongs to another company. Go back to the list and reload.

**I deactivated the default printer and now the stations print with the PDF.**
That is expected: its use is left without a default. Mark another one as default (or choose one at each station). Reactivating the old one does not make it the default again by itself.

**“Enter the template name.” / “The label layout is missing.” (400)**
A template needs a name and its layout (the 9 elements; from the screen it always goes complete).

**“Unknown position in “…”: 'X'. Use tl, tc, tr, ml, mc, mr, bl, bc or br.” / “Unknown size in “…”: 'X'. Use sm, md or lg.” (400)**
A position or a size that does not exist for that element came in through the API. Use one of those listed.

**“Only the waybill number and the invoice can be shown as a barcode.” (400)**
“As barcode” is only valid for the waybill number and the invoice (the “Barcode” already is one).

**“The label needs at least one barcode so it can be scanned.” (400)**
Leave the “Barcode” (package number) visible or show the waybill or the invoice as a barcode.

**“An active label template with that name already exists.” (409)**
Another active template already uses that name. Change it or deactivate the other one.

**“The label does not fit in 4×2: remove elements or use a larger label.”**
With so many large elements it does not fit even by shrinking the font. Turn off elements, use the Small size or a printer with a larger label.

**Why is the label barcode the package number and not the order number?**
Each package (line) of the order has its own number; one label comes out per PIECE of the line with that number (repeated on the pieces of the same line) and “Package i of N”. A line without a number gets no labels and a warning is shown. Since F-G2 the scan station and the exact order search also find the order by the package number.

**I configured the station and on another computer it does not appear.**
The print configuration is per station (this browser on this computer). Configure it and press Save on each one.

**What does “Print labels directly to the printer” do?**
It is a company setting (off by default, `printers.manage` permission). When on, when an order is saved (quick or detailed entry) the “Print label on save” switch comes on and one label per piece comes out on the station's label printer; a station can opt out of printing by turning off its “Print on save” (since F-G2).

**I turned on “Invoice label” for a client and the label does not come out.**
It comes out when saving a shipment of that client **with an invoice number**, after the package labels, on the station's label printer (it needs one configured). If the shipment has no invoice, “{N} shipments without an invoice number get no invoice label.” appears. It does not ask or open a preview. How it looks is changed in System → Printers & labels → “Build the invoice label”.

## Batch 44 / F-G2 — Order labels and documents (2026-10-12)

Chapters: [12 §10](12-impresoras-y-labels.md) and [F-G2 — screen](frontend/fg2-labels-y-documentos.md).

**How many labels come out of an order?**
One per piece: the quantity of each line (in whatever unit) is the number of labels of that line. Each one carries the package number of its line (the same on its pieces) and “Package i of N”.

**“{N} pieces without a package number were not printed.”**
Those lines of the order have no package number (a special delivery, for example). Give the line a number in the order file (or let Teikem generate it when editing the packages) and print again.

**“{N} cancelled orders were not printed.”**
Cancelled or deleted orders get no labels. Remove them from the selection or from the filter if you do not want to see the warning.

**“There are no labels to print.”**
What was chosen has no piece with a package number (or everything is cancelled).

**“That is {N} labels: the PDF will be large and may take a moment. All are printed; there is no limit.”**
It is only a warning from 500 labels onwards: wait for the PDF to finish being built.

**“Print label” is greyed out.**
It prints only the selected orders: “To print only some, mark them in the list with their checkboxes.”. For all those in the search use “All labels”.

**“The order was saved, but the label could not be printed: {motivo}. Use “Print label” in the record.”**
The order was saved; only the printing failed (printer off, QZ Tray closed, browser without permission). Check the station's printer (System → Printers & labels) and use “Print label” in the order file.

**“Could not print: {motivo}”**
It failed when building or sending the PDF. Try “Confirm first” → “Download PDF” and contact support with the reason.

**Why does “Print label on save” come on (or off)?**
It comes on if the company has “Print labels directly to the printer” and the station has not turned off its “Print on save”; it can be changed while entering (it applies to that entry session).

**Which orders go into the Waybill summary and the Packing list?**
The selected ones; with no selection, all those in the current search and filters (all pages). Cancelled ones are not included (the document says so). They never show the price.

**“The package number is in {n} orders (from different clients or repeated); scan the package or the shipment number.”**
Several orders have the scanned package number (each client numbers its packages from PQT-00001, and a typed number can be repeated). The station does not guess which one it is: scan the package (EMP-…) or the shipment number.

**What does the Missing report bring?**
The orders that are still pending to scan with the “Pending” filters (all of them, with no limit), grouped by zone, and how many have been scanned in this session. It comes out on the station's document printer.

## Preferred IP: sign in without a code from the company network (2026-10-12)

**I am at the office and it does not ask me for the code. Is that normal?**
Yes, if the office IP is in Security & audit → Sessions & MFA → “Preferred IPs” (and the feature is turned on on the server). Only the sign-in code is skipped; the password is always requested, and sensitive actions (permissions, roles…) still ask for the code.

**I am at the office and it keeps asking me for the code.**
It can be for any of these reasons: your IP is not in the list (the panel says “Your current IP is…” and whether it is covered), the entry is **deactivated or expired**, the feature is turned off on the server, your user was given the individual “always MFA” flag, you are a platform administrator, or you have not enrolled your MFA yet (you have to enroll it first). Use “Test an IP” with the office IP.

**What is the “/24” when adding a range?**
How many addresses it covers: /32 is a single one, /29 is 8, /24 is 256. For the public IP of a location's firewall **a single IP** is enough (the recommended option).

**“The range is too wide”.**
For security, ranges covering more than 65,536 addresses (IPv4 /16) or, in IPv6, wider than /48 are not accepted. Use a narrower one.

**“That IP or range is already in the list” / “This company reached its maximum of N entries”.**
The IP is already loaded (even if you write it differently) or the company reached the maximum; the maximum is changed in the same panel (empty = no limit).

**My company has a guest Wi-Fi. What do I do?**
Guests must go out to the internet through **another public IP** (another exit or VLAN with another IP) and only the IP of the internal network goes in the list. The system only sees the public IP: it does not tell one Wi-Fi network from another behind the same firewall.

**I forgot my password and it asks me for a code.**
That is on purpose: if you have two-step verification, resetting the password by email also requires the code from your app (or a recovery code), wherever you are, even on the preferred network. If the code is wrong or missing, the email link **is not used up**; failed attempts count towards the account lockout.

## Multi-adjustment (2026-10-12)

**I don't see the “Multi-adjustment” button.**
The `inventory.adjust.multi` permission is needed, which does not come in any role: the administrator grants it to a role from Roles. The button is in *Transfers & adjustments*, after “Adjust”.

**“The stock of … changed while you were counting”.**
Goods came into or left that bin after the screen was opened. Nothing was applied. “In system” is already updated and what you typed was kept: review the marked lines and press “Apply adjustment” again.

**I left “On hand now” empty in a bin. What happens?**
That line is not touched. If the value equals the system's, it does not generate a movement either. Only lines with a difference create movements.

**My product has serials and it is not accepted.**
Products with serials are adjusted with the normal adjustment (counting serials by quantity makes no sense).

**“… has N reserved: the stock cannot go below that”.**
That bin has goods set aside for orders; you cannot leave less than what is reserved. Release the reservation or count at least that quantity.

**How do I see what was moved by multi-adjustment?**
In *Transfers & adjustments* → Adjustments, filter by the reason “Physical inventory” (the Origin column says “Multi-adjustment AJM-…”), or open the History of the multi-adjustment screen.

## Delete bins (2026-10-12)

**What is the difference between Deactivating and Deleting a bin?**
Deactivating is temporary: the bin still exists, keeps its code and can be reactivated. Deleting is permanent: it does not come back, it does not appear in any list and its code becomes free for a new bin. In both cases the history (movement ledger, balances, old documents) is kept; a deleted one is shown with the “(deleted)” mark.

**“Bin {código} has inventory; it cannot be deleted”.**
It has stock on hand or reserved. Move it or adjust it to zero first.

**“…is the default receiving bin of {almacén}; change it first” / “…is the preferred bin of product {SKU}…”.**
First change the warehouse's default bin or the product's preferred bin, and delete again.

**“Bin {código} is in an open cycle count”.**
Close the count (reconcile or cancel) and delete again.

**I selected several and not all of them were deleted.**
That is on purpose: deleting several is partial. The summary says why each one was skipped; the skipped ones stay selected so you can correct them and repeat.

**“The bin was deleted and cannot be reactivated”.**
Deleting is permanent. Create a new bin (you can use the same code).

**“An inactive bin with that code already exists: reactivate it or delete it”.**
A deactivated bin still keeps its code. Reactivate it, or delete it to free the code.

## Session lifetime in hours (2026-10-12)

**How do I set the session to last 15 minutes?**
In Security & audit → Sessions & MFA → “Session lifetime (hours)” type `0.25` (15 minutes); `0.5` is 30 minutes, `1.5` an hour and a half and `24` a day. The same for “Warehouse device session lifetime (hours)”. Internally it is stored in minutes; the minimum is 1 minute (`0.02`) and the maximum 365 days (`8760`).

**It used to say “days”. Was what I had lost?**
No: what was in days is shown in hours (30 days = 720 hours). As long as it is not changed, the usual value applies.

**The “MFA” column says “Yes” but the button says “Always require MFA”. Is that wrong?**
No. The “MFA” column says whether the person **already has** MFA enrolled. The button is something else: the individual **“always MFA”** flag (2026-10-12). The company can require MFA from everyone (switch in Security & audit → Sessions & MFA) and the **preferred IPs** remove the code for whoever signs in from them; the “always MFA” flag makes an exception per person: that person is asked for the code even from a preferred IP. Without the flag, from a preferred IP they sign in without a code (with their password).

## Active sessions: one per person and browser (2026-10-12)

**I see many sessions of the same person in “Active sessions”.**
Before, each sign-in left the previous one alive until it expired (30 days by default). Since this version, **on sign-in the previous sessions of that same person with the same browser/system and the same IP are closed** (and the same device, if it is a warehouse device): a single one remains. Those of other people, of another browser (e.g. Chrome · Android), of the same person from another IP, or those without browser data are not touched. The previous ones are **revoked** (not deleted) and the security log records it (`TOKEN_REVOKED`, reason `session_replaced`). The ones that were already accumulated are cleaned up when that person signs in again, or with **“Sign out other sessions”**.

**I changed the session lifetime and the old ones are still there.**
The lifetime is set **at sign-in**: sessions already open keep their original expiration; new ones use the new lifetime.

## Batch 45 / F-B2 — Import consignees: Excel, “(Do not use)”, joined columns, default client and contact person (2026-10-13)

Screen: [frontend/fb2-importacion-consignatarios.md](frontend/fb2-importacion-consignatarios.md). Server: chapter 02, §2.2.

**Can I import an Excel file directly?**
Yes, an **.xlsx** (Excel 2007 onwards). If the workbook has several sheets, choose the one with the data in **Workbook sheet**. The old .xls is not supported.

**The Puerto Rico ZIP arrives as 901 instead of 00901.**
Teikem corrects it by itself: a Puerto Rico or United States ZIP with 3 or 4 digits is padded with zeros to 5. If the consignee's country is another, it stays as it comes.

**My CSV comes from Excel on Windows and the ñ and accents used to get corrupted.**
Not anymore: the encoding (UTF-8 or Windows-1252) is detected automatically.

**My file has empty columns or “Column2…Column11”.**
Leave them as **(Do not use)**. The column is ignored and the row is imported anyway. The template remembers those columns.

**I want the consignee name to be “Number - Name”.**
In the editor, put the number column in **Number** and, with **Also use in another field**, also in **Name**; put the name column in **Name**. In

**Fields with several columns** move the number column up with **↑** and type “ - ” in **Separator**. **Result** shows how it turns out.

**Why did a row come out with a client other than the one I chose?**
Because that row brings its own client in the template's client column: the row rules. Rows with that column empty get the **Default client**.

**“Remember this client in the template”, what does it do?**
When importing, the template keeps the chosen client as its default client: next time it is proposed by itself. Turn it off if it is just for this time.

**“Unsupported file format: use CSV or Excel (.xlsx).”** (400)
It is an .xls (Excel 97-2003) or another format. Open it in Excel and save it as “Excel Workbook (.xlsx)” or as “CSV”.

**“The Excel file could not be read; open it and save it again as .xlsx.”** (400)
The file claims to be .xlsx but is damaged or is something else with that extension. Open it and use “Save as” .xlsx.

**“The workbook does not have the sheet 'X'.”** (400)
The chosen sheet is no longer in the workbook (it was changed in the meantime). Choose the file again.

**“The file exceeds the limit of 5,000 rows or 2 MB.”** (400)
It also applies to Excel (and to a workbook that, uncompressed, exceeds 64 MB). Split it into several files.

**“Column N is marked “Do not use” and also has a field assigned.”** (400)
A “(Do not use)” column cannot fill any field. Remove the “(Do not use)” or the fields.

**“The field X accepts at most 5 joined columns.”** (400)
A field can join up to 5 columns. Remove one.

**“The field X is repeated.”** (400)
Country, client, service minutes, window hours and “Allow repeated invoice” go in ONE single column (text fields can join several).

**“Position N is repeated.”** (400)
The same column is twice in the same field. Remove the repeated one.

**“The separator accepts at most 10 characters.”** (400)
Shorten the separator of the joined columns.

**“The default client must be the same client as the template's.”** (400)
The template belongs to a client (it only works for that client): its default client can only be that one or none.

**“Give a default client or remove it, not both.”** (400)
Error from a direct API call (`defaultClientPublicId` and `clearDefaultClient` at the same time). The screen does not send it.

**“The client has been deactivated; only its history can be viewed.”** (409, default client)
The client chosen as the template's default client is deactivated. Choose another or reactivate the client.

**“The phone cell has more than one number; the first one was taken (7875550101).”** (warning)
The cell had two phone numbers. The first one was saved; if you need the other, add it in the consignee record. The row is imported.

**The phone came as “(787)555-0101” or “787 555-0101” and now it looks different.**
The 10 digits are saved and the screen shows them with the company's phone format.

**“Code X is repeated in row N of the file.”** (row error)
Consignee number X already appeared in row N of the file. Correct one of the two and validate again.

**“Maximum 150 characters.”** (contact person)
The contact person accepts up to 150 characters, in the file and in the record.

**Where do I see the contact person?**
In the consignee record (“Contact person”), in the list row and in the search box. It is changed with **Edit**.

## Batch 46 / F-B3 — Import consignees: town, state and ZIP with the postal code list, and mapping variants (2026-10-14)

Screen: [frontend/fb3-variantes-y-codigos-postales.md](frontend/fb3-variantes-y-codigos-postales.md). Server: chapter 02, §2.3.

**My file has the ZIP but not the town (or the other way around). Do I have to fill it in?**
No, in Puerto Rico and the United States. With the ZIP, Teikem fills in the town and the state; with the town, the first delivery ZIP of that town. The preview marks what
was filled in with **from list** and explains it in Remarks.

**Why did the ZIP of a row change?**
It was a **P.O. Box** or **military** ZIP: a consignee is delivered to at a physical address, so Teikem used a delivery ZIP of the
same city. The preview marks it **Postal code adjusted** with the original ZIP struck through. If you prefer another delivery ZIP, correct it in the file.

**The ZIP and the town of a row do not match. Which one stays?**
Both, as they come; there is only a warning. Check which one is right before importing.

**Can Teikem change the state I brought?**
No. If it comes empty, it fills in the one from the list; if it comes filled and does not match the ZIP, it only warns.

**My file mixes rows with the columns in a different order.**
Use **variants**: in the template editor, **+ Add variant**, give each one its condition in **Used when** (for example, “Column 9 equals PUERTO
RICO”) and its own mapping. The first variant that is met is applied to each row.

**Do variants have their own default values?**
No: the default values belong to the template and apply to all variants.

**I used to be able to import without a ZIP and without the town being in the list.**
Since Batch 46, in Puerto Rico and the United States the town or the ZIP must be in the postal code list (to have a valid delivery address). Other
countries remain as before.

**Every ZIP comes out without a type and I never see “Postal code adjusted”.**
The ZIP types have not been loaded: the database administrator runs `Diseño/logistica-db-zip-catalog.sql` (after `db-update`) and restarts the API. In the meantime the
ZIPs are accepted as delivery ones.

**Where do the ZIP types and coordinates come from?**
From the U.S. postal service (USPS “Zip Locale Detail”) and from **GeoNames** (www.geonames.org, Creative Commons Attribution 4.0 license). The coordinates are not copied to the
consignee.

**When creating a consignee I can't find a ZIP that does exist.**
The consignee's **Town** selector only offers delivery ZIPs: P.O. Box and military ones do not appear (they do appear in the client's postal address and
in warehouses).

**“Postal code X is not in the postal code list; correct it, or leave it empty and give the town.”** (row error)
The ZIP does not exist in the list. Correct it, or clear it and give the town (Teikem assigns the ZIP).

**“Postal code X is not valid: use 5 digits (or ZIP+4, such as 00901-1234).”** (row error)
The ZIP has letters or a length that is not a ZIP's. Correct it.

**“Town 'X' is not in the postal code list; correct it or give the postal code.”** (row error)
The town is misspelled or is not a town (sometimes the neighborhood comes instead). Correct it or give the ZIP.

**“Town 'X' has no delivery postal code (only P.O. Box or military); give the postal code of the physical address.”** (row error)
That town only has P.O. Box ZIPs. Give the ZIP of the consignee's physical address.

**“Town 'X' exists in several states (IL, MA); give the state or the postal code.”** (row error)
There are towns with the same name in several U.S. states. Fill in the state or the ZIP.

**“Postal code X is a P.O. Box code and Y has no delivery postal code to replace it with; give the postal code of the physical address.”** (row error; also “…is military mail (APO/FPO)…”)
The city of that ZIP has no delivery ZIP. Give the ZIP of the physical address.

**“No variant of the template applies to this row; review the conditions of the variants.”** (row error)
The row meets no condition. The preview shows its cells: adjust the conditions or add a variant for that format.

**“Postal code adjusted: 00734 is a P.O. Box code; 00715, a delivery code in Ponce, was used.”** (warning)
See “Why did the ZIP of a row change?”. The row is imported with the new ZIP.

**“Town taken from postal code 00952: Toa Baja.” / “State taken from the postal code list: PR.” / “Postal code assigned: 00956, the first delivery one of Bayamón.”** (warnings)
Teikem filled in that data with the list. The row is imported.

**“Postal code 00716 belongs to Ponce, not Caguas; both are saved as they come.”** (warning)
See “The ZIP and the town of a row do not match”.

**“State NY does not match the postal code's (PR); it is saved as it comes.”** (warning)
Check the state of the row; Teikem does not change it.

**“The template must give the city or the postal code, in a column or in the defaults.”** (400)
The template maps neither the town nor the ZIP. Map one of the two (or give it a default value). It replaces the previous message “The template must give the city in
a column or in the defaults.”, which no longer appears.

**“Variant N has no condition and leaves the following ones unused; move it to the end or give it a condition.”** (400; on screen, the tab says “with errors”)
A variant without a condition applies to every row, so the ones after it would never be used. Move it to the end (**Move earlier** on the other one) or give it a condition.

**“The «equals» or «contains» condition needs a value.”** (400)
Type the text the column must have, or change the condition to “is not empty” / “is empty”.

**“Choose the column of the condition.”** (screen only)
Choose in **Used when** which column of the file is checked.

**“Unknown condition: X. Use EMPTY, NOT_EMPTY, EQUALS or CONTAINS.”** (400)
Error from a direct API call with another operator. The screen only offers those four.

**“A template accepts at most 10 variants.” / “A variant accepts at most 5 conditions.”** (400)
Remove variants or conditions (the screen does not let you go past those limits).

**“This template uses variants: the columns go inside each variant (variants), not in columns.”** (400)
Error from a direct API call that sends `columns` with variants. Send the columns inside each variant (or `variants: []` to go back to a single one).

**“Mapping variants only exist in consignee templates.”** (400)
Order templates have no variants.

**“Unknown postal code type: X. Use STANDARD, PO_BOX, UNIQUE, MILITARY or DELIVERY.”** (400, `GET /postal-localities`)
The `zipClass` filter has a type that does not exist. Use one of those.

## Batch F-B4 — Consignees in the client record and Export

Questions about the **Consignees** panel of the client record and the **Export** button of the Consignees screen. Chapter: [fb4-consignatarios-en-la-ficha-del-cliente.md](frontend/fb4-consignatarios-en-la-ficha-del-cliente.md).

**“This client has no consignees yet. Use "New consignee" or "Import".”**
The client has no active consignees of its own. Create one with **New consignee** or load a file with **Import**. Those shared with all clients and the inactive ones do not appear in this panel; look for them in **Lists → Consignees**.

**“No consignee of this client matches the search.”**
The search box found nothing among the client's consignees. Clear or change what you typed.

**The table of the Consignees panel only shows 10 rows.**
It is one page: the table footer has the pagination (previous, next and “Rows per page”). The number next to the title and the exported file count **all** those matching the search box.

**What does the Export file contain?**
Exactly the rows that are filtered, not just the 10 visible ones: in the client record, the client's own active consignees with the search box applied; in Consignees (**Export** button in the header, before Import and New consignee), what the list shows with the **Client** filter, **Show inactive** and the search box. Columns: Number, Name, Client, Address 1, Address 2, Town, State, Postal code, Country, Contact person and Record status. The phone is not exported.

**“The file could not be generated. Please try again.” (red notice when exporting)**
The browser or the file failed while being generated. Try again with another format (CSV is the simplest). If it happens again, contact support.

**I don't see the Consignees panel in the client record.**
Your user does not have `locations.read` or the Lists module is turned off. The panel is not an error: it simply is not drawn without that permission.

**I don't see the Import or New consignee buttons in the panel.**
The `locations.create` permission is missing. **Export** is shown with just `locations.read`.

**How do I open and close a panel of the client record? Is what I had typed lost?**
Press the arrow to the left of the title (or click the header). Closed, the panel shows a one-line summary, for example “Active consignees: 12”. What you typed is not lost when closing a panel as long as you do not change client or reload the page.

**The panels of the client record appear closed when I enter.**
That is normal: all panels start **closed**, each with its summary, every time you open a client or reload the page. Press the title arrow of the one you want to see.

**I can't close the box at the top with the client's name.**
You can: the client header (name, status and “Deactivate”) is also a collapsible panel. Closed, it says only the status; to deactivate or change the status, open it.

## Batch F-B5 — Client addresses and Tax profile

Questions about the **Addresses** panel, the **Tax profile** and the summaries of closed panels. Chapter: [fb5-direcciones-y-perfil-fiscal.md](frontend/fb5-direcciones-y-perfil-fiscal.md).

**Where do I type the client's address?**
In the **Addresses** panel of the record (right below the header; open it with the arrow). There is **a single button**, **Add address**: the window asks for the **type** (physical address, postal address or pickup point). If you don't see the button, the `locations.create` permission is missing (or `locations.update` for **Edit** and **Remove**).

**“No address on file”**
The client does not have that address yet. Press **Add address** and choose the type.

**“Same as the physical address”**
There is no postal address of its own: the postal one is the physical one. If it is different, **Add address** and choose “Postal address”; if you later remove it with **Remove**, it goes back to this state.

**“Enter the point name.”**
Every pickup point has a name to recognize it in dispatch (for example “Almacén Norte”). The physical and postal ones do not ask for a name.

**“The client already has an active corporate address; deactivate it or edit it.” (409)**
There is only one physical address per client. Edit it with **Edit**. (In the add window, “Physical address” is no longer offered when one exists.)

**“The client already has an active postal (billing) address; deactivate it or edit it.” (409)**
There is only one postal address of its own per client. Edit it, or **Remove** it to go back to “same as the physical”.

**What is “Picked up at”?**
Where the driver picks up this client's orders: the physical address (by default), the postal one or any of the pickup points. It is saved when chosen (“Pickup point saved.”). If you remove the point that was chosen, it goes back to the physical address.

**What is the legal name and how does it differ from the name?**
The **legal name** is the legal name of the entity (the one that goes on invoices), together with the tax ID. The **name** in the header is the trade name and is not changed from the Tax profile.

**Why is “Client profile” now called “Tax profile”?**
Because it is about the tax entity: legal name, tax ID, credit, payment term and currency. The addresses moved to their own panel.

**What does “Print labels automatically” do?**
When an order of that client is saved on the orders screen, its labels (one per package) print by themselves, even if “Print label on save” is off at the station. It needs a label printer at the station; if it fails, the order stays saved and the notice says the reason. It is off by default.

**I closed a panel and I don't know what it says.**
Closed, each panel shows a line with the main points (status, address and where it is picked up, main phone and email, contact persons, numbering examples…). The table in section 5 of the chapter lists them all.

## Batch 49 — New modules: sales, taxes and billing

**I see three new modules (Sales orders, Taxes (IVU) and Billing) and they are turned off.**
That is expected: they were defined so they can be turned on per company when they are built; today they open no screen. Nothing needs to be done.

**“Sales orders” (or “Billing”) cannot be turned on.**
Each one requires its dependency to be turned on: **Sales orders** needs **Inventory and traceability**; **Billing** needs **Sales orders**. Turn on the dependency first. **Taxes (IVU)** depends on none.

## Batch 50 — Scheduled jobs

**I don't see “Scheduled jobs” in the System menu.**
You need the **Manage scheduled jobs** permission (`jobs.manage`); the Company admin role includes it. Ask an administrator to add it to your role.

**I pressed “Run now” and nothing happened.**
It is normal for it to take a few seconds: the system picks up the request on its next pass (every 30 seconds at most) and the row changes by itself to “Running” and then to “OK” or “Failed”. While it is requested or running the button is not offered. If several minutes go by, contact support (the jobs service may be turned off: `Jobs:Enabled`).

**“The job is already running; wait for it to finish.”**
That job is running right now (you or someone else requested it, or it was due by schedule). Wait for it to finish and try again.

**“The interval must be between 60 seconds and 7 days (604800).”**
The “Every so often” schedule accepts from 1 minute to 7 days. Correct the number or the unit.

**“Enter the daily time (HH:mm).” / “The daily time is not valid; use HH:mm (24 hours).”**
In “Every day at a time” type the time in 24 hours, for example 03:30 or 14:00.

**“Unknown schedule type: 'X'. Use INTERVAL or DAILY.”**
It only happens if the API is called by hand with a type that does not exist. It does not happen from the screen.

**“Job not found.”**
The job key does not exist in this version of the system. Reload the screen.

**“The record was modified by another user; reload and try again.”**
The job changed while you were editing it (someone else, or a run finished). Close the dialog, look at the current state and make the change again.

**A job came out “Failed”.**
The error is shown on the same row and in the history. The system retries it by itself at 1, 2 and 5 minutes; afterwards it goes back to its schedule. If it keeps failing with the same error, pass the message to support.

**I turned off a job and I want to run it once.**
Use “Run now”: it runs once even if it is turned off and does not turn it back on.

**At what time does “Daily inventory review” run?**
At 2:00 a.m. Puerto Rico time (the history cleanup, at 3:00 a.m.). You can change them in “Change schedule”.

**The system was off at the job's time.**
It runs when it comes back on; it is not lost.

## Batch 51 — Notices

**What are notification rules for?**
They say who should find out when something happens (an event) and through which channel: email, the web bell, SMS or push. Each company sets up its own. Today the engine is ready and there is only one test event; the notices of each module will be connected over time.

**I can't turn on the SMS (or PUSH) channel.**
“The SMS channel is not configured in Teikem yet.” Those channels have no provider yet; they can be turned on when one is contracted. In the meantime use email or the bell.

**I can't turn on the EMAIL channel.**
Same message with EMAIL: Teikem's email is not configured on the server. Contact support (the Brevo configuration is needed).

**“The rule needs at least one recipient to be activated.” / “…at least one channel turned on…”**
To activate a rule add who to notify (a role, a user, an email) and turn on at least one channel. Until you do, leave it off.

**“The template uses a variable the event does not have: {x}.”**
You typed a {variable} that this event does not offer. Use only those that appear in the event's list (for example {message} and {user} in the test notice).

**“The EMAIL channel needs a subject.” / “The APP channel needs a text.”**
A channel that is turned on needs its text, and email and the bell also need a subject (in Spanish, English or both).

**“User not found in this company.” / “Role not found.”**
The recipient does not belong to this company or is inactive. Choose from the company's list of users and roles.

**“Invalid email” / “Invalid phone”.**
Check the format: an email with @ and a domain; a phone with 7 to 15 digits.

**“A rule with that name already exists.”**
Rule names are not repeated in the company (even if one is turned off). Change the name.

**Why didn't a person receive the notice?**
Look at the notice log: `SKIPPED` says why it was not due (channel not configured, they have no email, they muted that notice); `FAILED` means the sending failed and is being retried (at 1, 5 and 30 minutes). An inactive person, or one who is not from the company, never receives.

**Can I be notified twice about the same thing?**
No: every sending is recorded and a retry only repeats what failed. If you are in a role and also by email, you receive just one.

**The email was down and it did not go out.**
The system retries at 1, 5 and 30 minutes. If the fourth attempt also fails, it stays failed in the log with the error; the operation that caused it is never undone.

**“That notice cannot be muted.”**
Only notices marked as optional can be muted in the personal inbox. The administrator decides the rest.

**“Notice not found.”**
That notice does not exist in your inbox (it may belong to someone else). Reload the bell.

## Batch 52 — Notification rules, bell and Kemi

**I do not see “Notification rules” in the System menu.**
You need the **Manage notification rules** permission (`notifications.manage`); the Company Admin role includes it. Ask an administrator to add it to your role.

**The bell shows nothing.**
Each person sees only their own notices. “You have no notices.” means none has reached you yet. Notices reach you when there is an active rule that includes you (by your role, by your user) with the **Bell** channel turned on.

**I get notices I do not want.**
Open the bell → **Notices I can mute** and turn off the ones you do not want. Only notices marked as optional can be muted.

**Kemi keeps popping up / gets in my way.**
Open her with a click and choose **Put Kemi to sleep**: she hides in the header, next to the bell, and no longer shows bubbles. One click on her wakes her up. It is remembered in that browser.

**Kemi showed me “You got a notice” and disappeared.**
The bubble hides by itself after 15 seconds. The notices stay in the bell (with their counter) until you mark them as read.

**Does Kemi replace the bell?**
No. The bell is always there; Kemi only notifies and opens it with **View**.

**I cannot turn on Email, SMS or Push in a rule.**
“This channel is not configured in Teikem yet.” SMS and Push have no provider yet. Email needs the Teikem mail service to be configured on the server; contact support. The **Bell** can always be turned on.

**The people search box does not find someone.**
Type part of the name or the e-mail. Only **active** users of this company appear, and only those you have not yet added to the rule.

**I want to notify someone who is not a user.**
Add them as **Email** (or **Phone**, which is saved but not used until SMS exists).

**I saved the rule and nothing happens when the event occurs.**
Check that the rule is **active** (switch), has recipients and at least one channel turned on. Then look at the **Log** tab: it says whether it went out, was skipped (and why) or failed.

**“Send test” did not arrive.**
The test goes out through the channel you chose, to you. If it says it “did not go out”, the reason is in the message (channel not configured, you have no e-mail, you muted that notice). The test uses the **saved** text: save your changes first.

**What does “Fire a test notice” do?**
It puts the test event in the real queue; the **active** rules for that event receive it within seconds and it is recorded in the Log. It is used to check that a rule works end to end.

## Batch 53 — Internal messaging

**I do not see the chat bubble in the header.**
You need the **Use internal messaging** permission (`messages.use`). The staff roles that already existed include it; a new role does not get it on its own. Ask an administrator to add it to your role (System → Roles & users).

**How do I write to someone?**
Chat bubble in the header (or Kemi → “Send a message to…”) → **New message** → type part of the name or the e-mail, choose the person, write the message and **Send**. A single person opens (or reopens) their conversation; if you had already talked, it is the same thread.

**How do I write to several people, a group or a whole role?**
In **New message**, type in “To” and choose each person (as many as you want, one after another); with two or more, a group is formed where everyone can reply. For a role, type its name in the same field and choose it: the people who have it **today** are included; whoever joins the role later is not included automatically, add them with **People → Add people** (only the group creator can).

**The other person was not online: are they notified when they come in?**
Yes. When they open Teikem they see the unread-message counter on the chat bubble in the header and Kemi greets them with “You got a message from…” (once per browser session).

**“Choose at least one person or a role.” / “Write the message.”**
Either the recipient or the text is missing. Add someone (or a role) and write the text.

**“The message cannot exceed 2000 characters.”**
Split it into two messages. The box tells you how many characters you have typed.

**“User not found in this company.”**
That person does not belong to the active company or is inactive. You can only write to active people of the same company.

**“That role has no active people to write to.”**
Nobody active has that role today (or only you). Choose another role or add people.

**“You cannot write to yourself.”**
You chose only your own name. Choose another person.

**“Conversation not found.”**
The conversation does not exist, is not yours, or you already left the group. Go back to the list.

**Why can't I add people to a thread? “Only the group creator can add people.”**
Only the person who created the group adds people. In a thread between two people nobody can be added: create a new group.

**“A conversation between two people cannot be left.”**
You can only leave groups. If you do not want notices from a two-person thread, use **Mute**.

**I left a group and want to come back.**
Ask the person who created it to add you again. You will see the messages from the moment you are added.

**Can messages be edited or deleted?**
No: messages stay as they were sent (it is the conversation history).

**How do I know whether the other person is online?**
You will see a green dot in the list and “Online” in the conversation when they have Teikem open. Otherwise, “Seen 5 min ago”, “Seen 3 h ago” or the date. Which screen they are on is not shown.

**The other person is not online, do they receive the message?**
Yes: it stays pending in the thread and, when they come in, they see their unread-message counter and Kemi greets them with what is pending.

**The message did not reach me instantly.**
It almost always arrives on the spot. If the live connection dropped (network, VPN, an intermediate server that does not let WebSockets through), Teikem reconnects by itself and, in the meantime, checks every 30 seconds. If it happens often, contact support (see chapter 15 §7).

**Kemi notified me of a message and I do not see it in the list.**
Reload the list (it arrives on its own within seconds). If the thread is muted, Kemi does not notify, but the thread stays in the list with its counter.

**I do not want Kemi to notify me about a thread.**
Open the conversation and use **Mute**: the messages still count as unread, but without a bubble.

**A data-entry user of a client should not be able to write to me.**
Correct: the messaging permission is not given by default to the client portal roles; you assign it only to each client's management roles.

## Batch 54 — Send notices

**I do not see “Send notices” in the System menu.**
You need the **Send notices to other people** permission (`notifications.send`). The Company Admin includes it; an administrator assigns it to the other roles (System → Roles & users).

**What is the difference between a notice and a message?**
A notice is a one-way notification: it reaches each person's bell (“From Ana Rivera”) and **cannot be replied to**. A message opens a thread where everyone replies. To have a conversation, use Messages.

**How do I send a notice to everyone?**
System → Send notices → turn on **Send to everyone in the company**, enter the title and **Send notice**. It asks you to confirm with the number of people. You do not notify yourself.

**What does “Select all” do?**
In the “To” field, **Select all (N)** chooses **only what matches what you typed**: if you typed “operator”, it checks the N operators; if you typed nothing, it checks everyone. It does not choose anything that is not shown in the filter.

**The search says there are more people than fit.**
The search returns up to 500 people. Refine what you typed (for example, part of the name or the e-mail) to see the rest, or use “Send to everyone”.

**“Enter the notice title.” / “Choose at least one person, a role or everyone.”**
The title or the recipients are missing. Enter the title and choose recipients (or turn on “everyone”).

**“There are no people to send the notice to.”**
The role you chose has no active people today, or the only recipient is you. Choose other people.

**“User not found in this company.” / “Role not found.”**
The person or the role does not belong to the active company (or the person is inactive). Choose from the list.

**How do I know whether they read my notice?**
In **Sent notices**, the **Read** column says how many people have read it (“2 of 5”).

**The person who received the notice was not online.**
It stays pending in their bell; when they open Teikem they see the counter (and Kemi greets them). If they were online, it arrives instantly.

**Can a notice that was already sent be deleted or corrected?**
No: it stays as it was sent, and the people who already received it keep it in their bell. Send another notice if you need to correct it.

**A manual notice cannot be muted.**
That is expected: only the optional notices from rules can be muted; those sent by a person with permission always arrive.

## Batch 55 — Taxes (IVU)

**I do not see “Taxes” in the menu.**
You need the **Manage taxes** permission (`tax.manage`; the Company Admin includes it) and the **Taxes** module must be turned on for your company (System → Modules). With the module off, the API responds 403.

**Where do I enter the IVU percentage?**
In Taxes → Rates → New rate: enter the code (for example IVU), the name, the date it takes effect and the **components** (state, municipal…). The rate percentage is the sum of the components. The figures are defined by your company's accountant: Teikem does not seed any.

**How do I change a rate that went up or down?**
It is not edited. **Close** the validity of the old rate (last day it applies) and **create another** with the same code starting the next day. Documents already issued keep the rate they were calculated with.

**“There is already a rate with that code covering those dates. Close the validity of the previous one first.”**
Two rates with the same code cannot apply on the same day. Close the previous one (or set the start date after its last day).

**“The rate needs at least one component (for example, State).” / “The components cannot add up to more than 100 %.” / “There are two components with the same code.”**
Each rate needs at least one component, with a distinct code and a percentage between 0 and 100; the sum cannot exceed 100.

**“The rate already has an end date.”**
Only an open rate can be closed. For another validity period, create a new rate.

**“There is no tax rate in effect on that date. Set one up in Taxes → Rates.”**
The default rate has no validity period covering the document date. Create the rate (or adjust the dates) in Taxes → Rates.

**“Choose the default rate in Taxes → Rules.”**
There are taxable lines and no rate has been chosen. In Taxes → Rules choose the rate code.

**What is the difference between rounding “per line” and “per document”?**
Per line, the tax of each line is rounded to cents and added up (3 lines of $0.33 at 11.5 % give $0.12). Per document it is added up without rounding and rounded only once ($0.11). Ask your accountant which one to use.

**What does “prices include tax” mean?**
That the amount of each line already includes the tax: Teikem extracts it (111.50 at 11.5 % = 100.00 + 11.50) and the total does not change. If it is off, the tax is added on top.

**Are freight and handling taxed?**
It depends on your company: in Taxes → Rules check the charge types that are taxable. Those you do not check come out without tax. Product sales are decided by the product category.

**How do I make a product tax-free?**
Give it the **Exempt** category (or **Zero rate** if it must appear taxed at 0 %). Taxable is the norm.

**“Invalid charge type.”**
The charge type does not exist in the catalog or it is the product sale (that one is decided per product, not by a charge rule).

**A client has an exemption certificate: what happens with their sales?**
While the exemption is **in effect** (and active), no tax is charged to them and the line is marked as exempt with the certificate number. If it expired, the system **charges the tax and warns** which certificate expired.

**“Enter the certificate number.” / “Enter the exemption type.”**
Both are required in an exemption.

**“Client not found.” / “Product not found.” / “Exemption not found.” / “Rate not found.”**
The record does not exist or belongs to another company. Choose it from the list.

**Can I delete an exemption?**
No. It is deactivated (it stays in the history) and can be reactivated.

**Where is the tax report by period?**
It is connected when sales orders and invoices exist (P4 and billing): today there are no documents to report on. The calculation and the per-line copy are already in place.

**In Taxes → Products only 500 products appear.**
The list returns up to 500 at a time. Refine with the search box (code, name or barcode) or with the category filter to see the rest.

**I chose a product's category and nothing happened.**
It is saved when you choose it and a notice “SKU: category.” appears. If an error appears, the product changed at the same time or no longer exists: reload the list. Taxable is saved as “no category” (that is the norm).

**The simulator says “Choose the default rate in Taxes → Rules.”**
The calculation needs to know which rate to use for taxable items. In the **Rules** tab choose the rate code and save.

**Does the simulator save anything?**
No. It calculates with the company rules and does not create or change any document.

## Batch 57 — Sales orders (draft)

**I do not see “Sales orders” / the API responds 403.**
You need the **Sales orders** module turned on for your company (System → Modules; it comes off) and the `sales.view` (view), `sales.create` (create and edit) or `sales.cancel` (cancel) permission.

**“Choose the buying client.” / “Client not found.” / “The client is inactive; you cannot sell to it.”**
The order needs an active client of your company. Choose it from the list; if it is inactive, reactivate it first.

**“Choose the warehouse: the company has more than one.”**
With several active warehouses you have to say which one serves the order. With only one it is used automatically.

**“Add at least one product to the order.” / “Choose the product of each line.” / “A product cannot be repeated in the order.”**
The order has at least one line, each with its product, and a product goes only once (add the quantities up in one line).

**“Only the company's own products can be sold.”**
Products of a pass-through client (3PL) are not sold from here. Choose an own product.

**“Product XXX has no sale price: enter the price.”**
The product has no sale price in its record. Enter the price on the line (requires the pricing permission) or set the sale price on the product.

**“Changing the price requires the pricing permission (sales.price).”**
Only the Company Admin (and whoever they designate) can sell at a price other than the list price. Ask the administrator for the permission or to adjust the price.

**“The quantity must be greater than zero.” / “The quantity allows at most 3 decimals.”**
Enter a positive quantity with up to 3 decimals.

**“Only a draft order can be edited.”**
A requested, shipped or cancelled order can no longer be edited. To change it you have to cancel it and create another (requested orders are modified with “Modify”, in the next batch).

**“The order is already cancelled.” / “Only a draft order can be cancelled…”**
A cancelled order is not cancelled again; and in this batch only drafts are cancelled (requested ones, with reservations, are cancelled from their screen once “Request” exists).

**Why didn't the tax of my order change when the rate changed?**
Each line keeps a copy of the tax it was calculated with. If the client of the draft changes or the lines are sent again, it is recalculated with the rate in effect that day.

**Why does the order come out with zero tax?**
Because the **Taxes** module is off in your company, or the client has an exemption in effect, or the products are exempt. With the module on and no rate configured, the system warns with the corresponding message.

**Can an order be deleted?**
No. It is cancelled and stays in the history.

## Batch 58 — Request and reserve a sales order

**What happens when I request an order?**
It goes from draft to **requested**: the warehouse sees it and the inventory is **reserved** for that order at that very instant. If two salespeople ask for the same thing at the same time, only one gets it; the other receives the shortage notice.

**“There is not enough stock of XXX: ordered 10, available 6, short 4.”**
There is less to sell than what was ordered. Nothing was reserved. Choose: **order only what is available** (lower the quantity), **reserve what is available and leave the rest on backorder**, or remove the line.

**Why is the “available to sell” lower than the one in the inventory table?**
Because it does not count what is in **quarantine**, in cross-dock or on rental, nor what is already reserved by other orders, nor what is in other warehouses. Only what the warehouse can pick is sold.

**What is the backorder?**
What was ordered and is not yet available. The order stays requested with the quantities that were reserved and the rest pending; the order is marked “with backorder” until it is filled or cancelled.

**“The client exceeds its credit limit…”**
The client already has more in progress than its limit allows (pending transport orders plus what is requested and not dispatched from its other sales orders). Whoever has the **Authorize shipments over the credit limit** permission can request it anyway, and who authorized it is recorded.

**I cancelled a requested order: what happens with the inventory?**
It releases **exactly what that order had reserved** and deletes its backorder; it becomes available again for other sales. An order that has already had something dispatched cannot be cancelled.

**Can I change the quantity of a line already requested?**
Yes, as long as nothing from that line has been dispatched: when you raise it, whatever is available is reserved (the rest gives a shortage or goes to backorder); when you lower it, the backorder is released first and then the reservation. The tax and totals are recalculated.

**“Product has already been dispatched from that line: it cannot be modified.” / “The line is closed.”**
A line with product already dispatched or closed with shortage is not modified. For what is missing, create another order.

**“Products with a serial number cannot be sold with a sales order yet.”**
For now sales orders do not handle serialized products. Use another way for those products.

**“Only a draft order can be requested.”**
The order is already requested, shipped or cancelled.

## Batch 59 — Sales order backorders

**Where do I see what is pending to be filled?**
In the **Backorders** view: all the lines with something pending, the oldest first (first ordered, first served), with what is available today and what could be filled now.

**How do I fill a backorder?**
When there is inventory, **Fill** on the line reserves whatever is available (all or part). **Fill all** goes through all the backorders from the oldest to the most recent. It requires the `sales.backorder` permission (Company Admin and Warehouse Operator).

**“There is no stock available to fill that line.”**
No sellable inventory has come in yet (or another older order already reserved it). It is filled when a receipt, an adjustment or a transfer comes in, or quarantine is released.

**What does “Close with shortage” do?**
It ends the line without waiting any longer: the backorder is deleted and the line is closed. What was already reserved is dispatched anyway; the ordered quantity is kept in the order. It cannot be undone: for what is missing, create another order.

**“That line has no backorder.”**
The line is already complete (or was already filled). There is nothing to fill or close.

**Am I notified when there is inventory for a backorder?**
Not yet: the automatic notice comes with the sales notices. In the meantime, the view always shows what can be filled now.

## Batch 61 — Dispatch against a sales order

**What does “dispatching” a sales order do?**
It takes out of the warehouse what was reserved: the inventory goes down (always, whether it is delivered or picked up), the `EMP-#####` pick batch linked to the order is created and the order becomes partially shipped or shipped.

**“You cannot dispatch more than what is reserved: XXX has n reserved.”**
Only what the order has reserved is dispatched. If the client asks for more, you have to raise the quantity of the line (it is reserved) and then dispatch.

**Can I dispatch only a part?**
Yes. Each dispatch creates its pick batch; the order stays in “Partially shipped” until everything reserved goes out (and, if there is a backorder, until it is filled or closed with shortage).

**The product is in a different bin from the reserved one.**
Enter the bin you took it from: if it has sellable balance, the reservation moves there and it goes out of that bin. If it has no balance it responds “Insufficient inventory…”.

**“Bin XXX is not sellable…”**
The bin is inactive or is a quarantine, cross-dock or rental bin: nothing is sold from there.

**“Only a requested or partially shipped order can be dispatched.”**
The order is a draft, already shipped or cancelled.

**Why did my order cancel itself?**
All its lines were closed with shortage without anything having been dispatched: there is nothing to dispatch or invoice. The note stays in the history.

## Batch 62 — Delivery and undoing a sales order dispatch

**How is what was dispatched delivered?**
If the client picks it up, nothing needs to be done: the dispatch stays as “dispatched without delivery”. If it is delivered, create the **delivery** of the dispatch with the usual data (service, packages, COD): the client and the consignee come from the sales order. It can be done when dispatching or later.

**“Dispatch EMP-… already has a delivery or was undone: another one cannot be created.”**
Each dispatch is delivered only once, and an undone one is no longer delivered.

**I made a mistake when dispatching: can it be undone?**
Yes: delete the dispatch's pick batch. The stock returns to its bin, the dispatched amount goes down and it **becomes reserved again** for the order, which is reopened (partial or requested). If it already had a delivery, the transport order is deleted with it according to the usual rules.

**Why is “Shipped” no longer a final status?**
So that the order can be reopened if a dispatch is undone. It is still the status of a shipped order.

## Batch 64 — Orders to dispatch on the device (server)

**Which orders reach the device?**
The requested or partially shipped ones with something reserved, from your warehouse, with the bin and the lot from which the system reserved each part. Those that are fully dispatched, cancelled or closed disappear on the next sync.

**The device dispatched with delivery and the server rejected it.**
The dispatch and the delivery go together: if the delivery data is not valid (unknown service, incomplete consignee…), the inventory does not go out and the order stays as it was; the device shows the message in its pending list.

## Batch 65 — Dispatch a sales order on the device

**The “Sales orders” button does not appear on Home.**
It appears only when the device has orders to dispatch from its warehouse. Tap **Sync now**; if it still does not appear, the company does not have the Sales orders module, the user does not have `sales.view` or there are no requested orders with something reserved in that warehouse.

**I scanned the number and it says “That order is not waiting to be dispatched in this warehouse”.**
The order belongs to another warehouse, was already dispatched, is cancelled, has nothing reserved (it is on backorder) or is new and the device has not downloaded it yet. Sync and try again.

**I took from a different bin than the one the device said.**
You can: scan the other bin. The reservation moves there if it has stock; if there is not enough, the server rejects the dispatch with “There is not enough stock…” and nothing goes out.

**I dispatched without signal and later the server rejected it.**
The device shows the exact server message, returns the local stock and downloads the order again: it can be dispatched again with what is actually there.

**I chose “Delivery” and it does not let me create the delivery.**
The delivery needs the order to have a consignee; if it does not have one, it only offers Pickup or Leave for later. Create the delivery later from the web app (Sales orders → the dispatch → Delivery).

**What is the difference between “Pickup” and “Leave for later”?**
The server does the same thing (picks and lowers the inventory, without delivery); “Pickup” means the client collects it and that is the end; “Leave for later” means the delivery will be created later from the web app.

## Batch 66 — Sales notices

**I created an order and nobody received a notice.**
No rule comes set up. The administrator creates it in System → Notification rules: event “Sales order requested”, recipients (role, user or e-mail) and the channel turned on (Email or App). Also check the send log: a recipient without e-mail is left as “Skipped” with the reason.

**It notified me about the credit but I already authorized the order.**
The notice goes out when someone tries to request the order and exceeds the credit; it was already recorded. It is not repeated as long as the limit, the amount in progress and the amount do not change.

**“Backorder ready to fill” took a few minutes.**
It is detected by a job that runs every 10 minutes (System → Scheduled jobs → “Notify backorders ready to fill”; “Run now” can be used). If the job is off or the Sales orders module is off, it does not notify.

**The notice says there is inventory, but Fill did not reserve everything.**
The notice uses what is available to sell at that moment; another older order may have taken it first. Fill reserves whatever is available, from the oldest order to the most recent.

**I want to stop receiving the notices of shipped orders.**
Mute the event in your inbox (the sales ones are optional, except the credit-to-authorize one).

## Batch 67 — Sales order analytics

**I do not see the sales order indicators or views in Analytics.**
The Sales orders module must be on for the company and the `sales.view` permission is required. Without either, the sources (and whatever is built with them) do not appear.

**The fulfillment (%) is empty on an order.**
Only **fully shipped** orders have fulfillment and days to dispatch. An open, cancelled or draft order does not have it (it is not counted as zero).

**The fulfillment comes out below 100 even though the order is “Shipped”.**
Lines were closed with shortage: the fulfillment is what was dispatched over what was ordered.

**“Needs your attention” says there is inventory, but Fill does not reserve everything.**
The notice uses what is available to sell at that moment; an older order may take it first. Fill reserves from the oldest order to the most recent.

**The backorder row does not appear in “Needs your attention”.**
It is shown only with `sales.backorder` and the Sales orders module; and only if there is inventory available to fill (an order waiting without inventory does not appear).

## Batch 68 — Kits

**It does not let me convert a product into a kit: “has stock”.**
A kit has no stock of its own. Take the stock out of the product (adjustment or transfer to another product) until it is at zero and without reservations, or create a new product for the kit.

**It does not let me add a product as a component.**
It must be a normal, active product, of the same inventory owner as the kit, and it cannot be another kit nor the kit itself. Read the message: it says which SKU and why.

**“0 can be assembled” but there is stock of all the components.**
The count uses what is *available to sell*: what is reserved is deducted and what is in quarantine, cross-dock or rental does not count; and it is limited by the component that falls shortest (available ÷ quantity per kit, rounded down).

**I cannot turn the kit back into a normal product.**
If any sales order already used it, it cannot be done (the history of those orders depends on it remaining a kit). Leave it as a kit or deactivate it if it is no longer sold.

**I tried to receive the kit in a receipt and it failed.**
A kit is not received: its components are received. The message says `… es un kit: no tiene existencia propia. Los movimientos se hacen sobre sus componentes.`

## Batch 69 — Kits in sales orders

**I sold a kit and the system reserved loose pieces.**
That is normal: the line is for the kit, but what is reserved are the pieces of its components (per whole kit). In the order, the “Kit components” table shows how much of each is reserved.

**I changed the kit components in the catalog and my order still has the previous ones.**
The order keeps a snapshot of the components when the draft is saved; it does not change on its own. Remove the line from the draft and add it again to take the new catalog.

**I ordered 5 kits and it says “available 3”.**
Complete kits are counted: the component that falls shortest limits it (available to sell ÷ quantity per kit, rounded down). With “Leave the rest on backorder” 3 kits are reserved and 2 are left waiting.

**I cannot choose the bin when dispatching a kit.**
A kit goes out of what its components have reserved; no bin or lot is chosen. If you need to take from another bin, adjust the component's reservation or dispatch by another way.

**Does the device dispatch kits?**
Yes (since Batch 71): see “Kits on the device” in this same section of questions.

**I undid a kit dispatch.**
The components return to their bin, the dispatched kits go down and become reserved again; the order is reopened.

## Batch 70 — Picking kits

**I picked a kit and in the movement ledger I see the loose pieces.**
That is normal: the kit has no stock, so each component goes out (with the note “KIT-1 × 2 · EMP-…”). Deleting the pick batch returns them all.

**I cannot choose the bin when picking a kit.**
A kit goes out of its components by the rotation (FEFO/FIFO) of each one; it does not accept a bin, lot or serials. To take from a specific bin, pick the components as loose products.

## Batch 71 — Kits on the device

**On the device, my kit order asks me to pick loose pieces.**
That is right: the device shows the pieces of each component and, when finished, sends the complete kits that can be made. Pick all the pieces of each kit.

**I picked the pieces but it tells me no kit is completed.**
Some piece of some component is missing (for example, 3 screws for a kit that takes 2 per kit and 2 nuts: enough for 1 kit, and 1 screw left over). Pick what is missing or remove what is left over with “Undo”.

**I picked extra pieces.**
What does not complete another kit stays reserved in its bin; it does not go out and is not deducted.

## Batch 72 — Billing

**I generated the run and it says “There is nothing to bill with that data”.**
Only what was **dispatched** from sales orders and not yet billed is billed. Check that the order has dispatches, that the client (if you chose one) is the right one, and the company's billing basis: with “on delivery” the
deliveries not yet delivered are not included. With “only within the period” the dispatch must fall between the dates.

**Can the same thing be billed twice?**
No. When generating, each sales order line records what was billed; a second run does not take it again. If you cancel the run (generated or reviewed) or void the invoice, it becomes billable again.

**It does not let me approve.**
The run must be **reviewed** and whoever approves needs `billing.approve` and a recent re-authentication (AAL2): confirm your password again and retry.

**I want to change an amount already calculated.**
Only while the run is generated or reviewed: adjust the line with the new unit amount and the reason (required). The calculated amount is kept. An approved run is corrected by voiding the invoice and generating again.

**The invoice came out with a tax different from today's.**
That is correct: the invoice keeps the rate the sales order was calculated with (per-line copy), not the one in effect today.

**I voided an invoice and the order appears again as billable.**
Yes: voiding releases exactly what that invoice had claimed.

## Batch 74 — Freight in billing

**A delivered order does not appear for billing.**
It must be **active**, with a **quoted amount** (it is quoted when created; it is frozen when marked as picked up) greater than zero and not billed. With the **Per pickup** contract it counts from the moment it is picked up; with **Per delivery**, **Mixed** or no contract, only when it is **Delivered**. Submitted, Failed attempt and Cancelled orders are not billed. And the run must include **freight** (checkbox “Freight of the transport shipments”).

**`Choose what to bill: product sales, freight or both.` (400)**
You unchecked both checkboxes of the new run. Leave at least one checked.

**The invoice has a single “Freight (quoted amount)” line instead of the breakdown.**
Today's breakdown does not match what was quoted for that order (the contract's billing model changed, the client was deactivated or the special service was closed). The quoted amount is billed, which is what the client was told. If it needs to be corrected, adjust the line with its reason.

**How do I know which rate each charge came from?**
Each freight line keeps the rate that produced it (`RateComponentId`); the dispatch and the COD come from the contract.

**Can the freight of an order be billed twice?**
No: when generating, it is locked and the order is marked. If you cancel the run or void the invoice, it becomes billable again.

## Batch 75 — Payments and statement

**`The payment (X) is greater than the invoice balance (Y).` (422)**
The amount exceeds what remains to be paid (the total minus the non-voided payments). Record only the balance or less; an overpayment is not recorded against the invoice.

**`Payments are only recorded for an issued or overdue invoice.` (422)**
The invoice is still a draft (the run has not been approved), is already paid or is void.

**I recorded a payment by mistake.**
If the invoice is still Issued or Overdue: void the payment with its reason (billing approval permission and re-authentication). If it is already fully **Paid** it cannot be voided from the screen: it is corrected with a credit note or with support.

**The invoice is still “Issued” even though its due date has passed.**
The `BILLING_OVERDUE_SCAN` job runs every hour (System → Scheduled jobs) and requires the Billing module to be on. After it runs, it appears as Overdue.

**It does not let me void an invoice with payments.**
Void its payments first (each with a reason); with no active payments the invoice can be voided.

**The statement does not show an invoice.**
Only issued, overdue and paid invoices count, from their issue date; drafts and void ones do not. Check the date range (by default, the last 90 days; anything earlier is included as the opening balance).

**What do the aging columns mean?**
Due soon (the due date has not arrived yet) and the days overdue: 1–30, 31–60, 61–90 and more than 90.

## Batch 77 — Export to accounting

**`Only an approved or exported run can be exported.` (422)**
The run is still generated or reviewed (or it is cancelled). Approve it (with re-authentication) and export; the preview is not built before approving either.

**What happens with the status when exporting?**
The run goes from **Approved** to **Exported** the first time it is exported, with who and when. Exporting again (with another template, for example) builds the file again and changes nothing else.

**The accounting program does not accept the file.**
Adjust the template, not the program: the delimiter, the date format, whether it has a header, the columns it requires and their order, and the header of each one. Test with the preview (it does not change the status) until it matches a sample of the real format.

**`A template with that name already exists.` (409)**
The name is compared ignoring case and accents. Change the name or deactivate the other one.

**It does not let me deactivate a template.**
The company needs at least one. Create the new one and deactivate the old one.

**The void invoice does not come out in the file.**
Correct: only the invoices of the run that are not void come out.

**Strange characters or shifted fields appear.**
In CSV, whatever contains the delimiter goes between quotes; in TXT there are no quotes and a delimiter inside a value is replaced by a space. If your program needs quotes, use CSV; if it does not tolerate them, use TXT.

## Batch 79 — Credit notes

**`You cannot credit more than what was billed: of “X”, Y remain to be credited.` (422)**
The invoice line already has **approved or applied** notes that credit part of it. Credit only what remains, or void the extra note. Two draft notes may seem to fit, but when **approving** the second one it is checked again.

**I overbilled or the client returned something and the invoice is already Paid.**
Create a **credit note** on that invoice. When approved it remains as the client's **credit balance** (visible in their statement) and is applied to the next open invoice with **Apply**.

**How do I credit only part of the price (a discount)?**
Create the note with the invoice line and a **lower unit amount** (for example 40 of a 100 line). The tax is credited in the same proportion.

**Does the credit note return the inventory?**
No. The goods return to the inventory when **the return is received**; the note only credits the money.

**It does not let me void an invoice.**
If it has **approved or applied credit notes** (or payments) it cannot be voided. Void the note first (if it has not been applied) or correct with another credit note.

**It does not let me void a note.**
Only a draft or approved note that **has not been applied** to any invoice can be voided.

**The note says “Approved” but still has a balance.**
That is the **credit balance**: what has not been applied. Use **Apply** with an open invoice of the same client; when it is used up it becomes “Applied”.

**I do not see the button / the permission to create notes.**
The **Credit notes** permission (`billing.credit`) is required; approving also asks for re-authentication.

## Batch 81 — Sales returns

**`Of X only Y more can be returned.` (422)**
What can be returned from a line is what was dispatched minus what was already returned and minus what other open returns already request (requested, authorized or received with shortage). Cancel the return that is not going ahead or close it with “close what is pending” when receiving.

**`The return is outside the return window…` (403)**
More than the window's days (30 by default) have passed since the last dispatch. Whoever has permission to authorize returns requests it and confirms it; the administrator can change the days (0 = no window).

**The client returned only part of what was requested.**
Receive in parts what arrived (the rest stays pending) and, if the rest is not going to arrive, check “close what is pending”: it stops counting as pending.

**Damaged goods arrived.**
When receiving, put the quantity in **damaged** and describe the damage: its damage report is created (cause “Customer return”) and it stays in quarantine, or is discarded if you check discard. The damaged items count as returned (they are credited) but do not enter the inventory as good.

**Does the inventory move when crediting?**
No. The inventory was moved by the return's **receipt**. The credit note only credits the money.

**`There is nothing to credit…` (422)**
What was received had not been billed (it will no longer be billed: the return deducts it from what is billable) or was already credited. An invoice still in draft is adjusted in its run, not with a note.

**It does not let me return a kit or a serialized product.**
In this version they are not returned; kits would be returned by their pieces and serials serial by serial, and they come later.

## Batch 83 — Receive returns on the device

**Why don't I see the “Returns” button on Home?**
It appears only if the device has already downloaded at least one **authorized** return from its warehouse and the user has `warehouse.receive`. If the return is new, tap **Sync now**; if it still does not appear, check on the web app that it is **Authorized** (a requested one is not received yet) and that it belongs to the device's active warehouse.

**`That return is not waiting to be received in this warehouse. If it is new, tap Sync now.`**
The number is not in the device's copy: it does not exist, belongs to another warehouse, was already fully received or has not been synced yet.

**`{SKU}: you cannot receive more than what is pending ({n}).` / `{SKU}: describe what happened to the damaged items.` / `{SKU}: enter the lot.`**
Fix the flagged line and tap **Receive** again. Good plus damaged cannot exceed what is pending; damaged items require the description; a product with lots requires the lot.

**I received without signal: was it lost?**
No. It stays in the outgoing queue (“Return receipt” in Sync) and is sent on its own when the signal returns. The return disappears from the device's list instantly; if the server rejects it, it appears again on the next sync and the exact reason is shown.

**I received on the device but the bin's stock did not change.**
The receipt enters through the warehouse's receiving mode (receiving zone with **Putaway** tasks, or direct): the stock shows as soon as the next sync arrives or when putting away. The damaged items stay in their damage report (quarantine) and not in the normal bin.

## Batch 84 — COD: collection and reconciliation

**`The collection exceeds what remains to be collected for {order}: {amount}.` (422)**
The sum of the collections cannot exceed the order's COD. Check how much has already been collected in the order detail (`GET /cod/orders/{id}`); a partial collection leaves the COD as “Partial”.

**`A collection is not recorded on a draft or cancelled shipment.` (422)**
The collection is made at delivery. The message says “draft”, but since Batch 106 it refers to a **Submitted** order: mark it as picked up (or reopen the cancelled one, with `orders.reopen`) before collecting.

**I read the sheet and it says `NOTHING_COLLECTED`.**
The order has COD, but nobody recorded the collection. Record it (web app or driver app) and process the sheet again: the same sheet can be processed again without a problem.

**I read the sheet and it says `AMBIGUOUS`.**
The invoice number is counted **per client**: `FAC-00001` exists in every client. Process the sheets indicating the **client** (all the sheets of the run belong to that client) or reconcile that order by hand from its detail.

**`ALREADY_RECONCILED`.**
That sheet had already been reconciled before; it is not reconciled again and nothing is duplicated.

**The preview says it reconciles, but nothing changed.**
That is expected: with `dryRun` it is only simulated. Confirm the run (without `dryRun`) to reconcile.

**The order's COD is still “Partial” even though I reconciled its sheet.**
What was collected was reconciled, but part of the COD is still to be collected. When the rest is collected and the sheet is processed again, the order becomes “Reconciled”.

## Batch 85 — COD screen and reading scanned sheets

**The sheet was uploaded, but “no code was found” on a page.**
The screen warns “no code on pages N”. Frequent causes: the barcode came out too small, blurry or cut off when scanning; the page does not have it (e.g. the second sheet of an invoice). Scan again at 300 dpi (or more) with the complete code and without wrinkles, or type the invoice number by hand.

**Are my files saved on the server?**
No. The browser reads the barcode on your computer and only the codes (invoice number) are sent, with the file name and the page they came from.

**`is not a PDF or an image` / `the file could not be read`.**
Only PDF and PNG, JPG, WEBP, BMP and TIFF images are read. A password-protected or damaged PDF cannot be opened; the other files in the same selection are processed anyway.

**Reading takes a while the first time.**
The pieces that read PDF and barcodes (a few hundred KB) are downloaded only once; afterwards they stay in the browser's memory.

**It read a code that is not the invoice (a tracking number, another number).**
It appears as **Not found** in the preview; it can be **removed** from the list before reconciling, or left: it reconciles nothing.

**“Reconcile” is disabled.**
First you have to do the **Preview** and there must be at least one code that reconciles; if the list or the client changes, the preview is discarded and has to be done again.

## Batch 86 — COD remittance to the client

**`There are no reconciled collections of that client in that period to remit.` (422)**
A remittance only gathers **reconciled** collections (matched against their sheet) of the client whose **collection date** falls in the period. Check that the collections are reconciled (Reconcile tab), that the period includes the collection date and that they are not already in another remittance.

**Which commission is applied?**
The one requested when creating the remittance; otherwise, the one in the client's active contract; otherwise, the company's (8 % factory default). The candidates (`/cod/remittances/candidates`) say which one and where it comes from before creating.

**I got the period or the commission wrong.**
While the remittance is open or reconciled, **cancel it** and create it again: its collections are freed. An approved or remitted one can no longer be cancelled.

**`This action requires recent re-authentication (AAL2).` when approving.**
Approving a remittance asks you to confirm the password (like approving a billing run); the screen asks for it on its own and retries.

**The remittance became Remitted but the COD of an order did not become Remitted.**
The order's COD becomes Remitted when **all** its collections have been remitted. If the order has a collection that was not reconciled (or is in another remittance), it stays in its status until that collection is also remitted.

**Does Teikem pay the client?**
No. Teikem calculates the remittance (gross, commission and net) and exports the file for accounting; the payment is made by the company. “Remit” only marks that it has been paid (with the check or transfer reference).

## Batch 87 — Remittances tab

**“Create remittance” is disabled.**
You need to choose the client, a valid period (from ≤ to), a valid commission (if entered) and there must be **reconciled collections** of that client in the period: the “Collections that would be included” section says so. If it shows 0, check that the collections are reconciled (Reconcile tab) and the collection date.

**I do not see the “New remittance” or “Approve” button.**
Creating, reconciling, approving, remitting, cancelling and exporting require the `cod.remit` permission; with only `cod.view` the tab is read-only.

**“Export file” does not let me choose a template.**
The template list is requested with the billing permissions (and the billing module). Without them the standard template is used: it downloads the same file with all the columns.

**Where does the commission shown on the screen come from?**
Below the candidates it says whether it is the one you entered, the one in the client's contract or the company default. The default is changed in the same tab, below the list.

## Batch 88 — The two currents of the Pulse

**I do not see the “Packages on the street” or “COD money” currents.**
Each one requires its permission (`pulse.orders` / `pulse.cod`), the permission for its data (`orders.view` / `cod.view`) and the module on (`LTL_GROUND` / `COD`). An already organized Pulse shows them in their default order (10 and 11) and they can be moved with “Organize”.

**“To dispatch” still shows an order that already has a driver.**
It only stops being counted when the order is on a route **with a driver** that is not cancelled or completed. Check in the Dispatch room that the route has the driver assigned.

**“Delivered” has fewer orders than the Orders list.**
It counts **today's** deliveries (by the status history, in the company's time zone); the Orders list brings all the delivered ones.

**“To collect” does not include an order with COD.**
It only counts orders with COD that are already **at destination or delivered** and have not been fully collected; an order still on its way is not “to collect”.

**The numbers change from one minute to the next.**
They are snapshots of now and are requested again every minute; they are not accumulated for the day (except Inbound and Delivered, which count today).

## Batch 89 — Radar and operation notices

**The Radar does not show an order I know exists.**
The Radar only counts what is **open** (Submitted, Pickup, Unassigned, Assigned, In transit or with Failed attempt; since Batch 106 Submitted ones count). A delivered, a cancelled, an In warehouse or a deactivated one does not appear: look for it in Orders.

**When I click on a town, Orders opens with filters I did not set.**
That is expected: the link brings the town (or the client) and the open statuses. “Clear filters” in Orders removes them.

**The Radar search does not respond with one letter.**
It searches from 2 letters (invoice, shipment number, packing or consignee).

**“Shipments without a driver” does not match “To dispatch” in the current.**
It is the same count (in warehouse or planned without a route with a driver); the notice only appears if you have `trips.dispatch`.

**The unreconciled COD notice says an amount but Reconcile does not show it per order.**
The notice adds up **everything collected and not reconciled**; in COD → Shipments with COD (“Collected” filter) you see the orders and in Reconcile you upload the sheets.

**Why do the new notices appear at the top and not by age?**
They are summaries (“83 orders…”, “$2,230…”): they go before the per-record rows so that a long list of rentals or discrepancies does not hide them.

## Batch 90 — Daily indicator snapshots

**The small bars of the Pulse currents come out empty.**
They come from the history saved by the `KPI_SNAPSHOT` job at 23:55. On the first day there is no history; in System → Scheduled jobs use “Run now” to save today's snapshot.

**The job says “No module with indicators is on; nothing was saved.”**
Only the indicators of the LTL Orders (`LTL_GROUND`) and COD modules are saved; turn one of them on in the company.

**I ran the job twice, are the figures duplicated?**
No: there is one row per day and indicator and the second run replaces today's.

## Batch 91 — COD, billing and sales indicators and charts; Operation and Accounting activity

**The “COD collected, not reconciled” or “Invoices receivable” indicators do not appear on the Pulse.**
They depend on the module (COD or Billing) and on the read permission of their source (`cod.view` or `billing.view`). Check them in “Organize” in case the user hid them.

**The “Accounting” tab of Recent activity is empty.**
It only shows what happened in the chosen window (24 h, 48 h or today): invoices issued, paid, overdue or void, payments, approved runs, COD collections and remittances.

**Why does “Invoices receivable” differ from the total of my invoice list?**
It adds up the **balance** (total minus non-voided payments) of Issued and Overdue invoices; it does not include drafts, paid or void ones.

**Can I create my own charts with collections or invoices?**
Yes: in Analytics, choose the “COD collections” or “Invoices” source (requires `cod.view` or `billing.view`).

## Batch 92 — Scheduled reports by e-mail

**I scheduled a view and the e-mail does not arrive.**
Check the last send in the “Send by email” block: **Skipped** with “Email sending is not configured.” means the platform has no e-mail provider (Brevo) configured; **Skipped** with “The report owner is no longer active.” means the person who created the view was deactivated; **With errors** says who it could not be sent to. Also confirm that the “Send scheduled reports” job is active in System → Scheduled jobs.

**I scheduled the report and it went out an hour late or did not go out right on time.**
The job runs every 5 minutes and sends what was already due; the time is the company's, not your computer's.

**Why does the e-mail have fewer rows than the screen?**
The e-mail carries up to 200 rows and only those the view's **owner** can see (their permissions and their scope of warehouses and clients). The rest is in Teikem.

**I changed the schedule and today's send did not go out.**
When a new schedule is saved, the counter starts at that moment; if today's time has already passed, the first send is at the next occurrence. Use “Send now” to send it right away.

**“Send now” is off.**
There are unsaved changes in the block (what is saved is sent) or there are no valid recipients.

**I cannot schedule a system view.**
System views are not edited: save your own copy (400 “System reports cannot be scheduled; save your own copy.”).

**Can the report be sent as an attached file?**
Not yet: the e-mail carries the table inside the message (up to 200 rows). The attachment (CSV) is pending because the platform's e-mail sending does not support attachments.

## Batch 93 — Delivery attempts

**I marked an order as delivered and I do not see the attempt.**
The attempt is only recorded if the order is not a special delivery (those are paid per trip) and if it does not yet have a current delivered attempt. If the route had no driver, the attempt exists but “without driver” (`withoutDriver` filter).

**`The shipment already has an attempt number 1.`**
That number is already in use among the current attempts. Leave the number empty (the next one is used) or void the repeated attempt.

**`The delivery must be the last attempt.`**
A failure cannot be recorded with a number higher than the delivered attempt's. Void the delivered one or use a lower number.

**`The attempt is already in a settlement and cannot be voided.`**
A paid attempt is fixed. To correct it you have to cancel the settlement that includes it (while it is open).

## Batch 94 — Driver settlement

**`There is nothing to settle in the period.`**
There are no current unsettled attempts with a driver nor open trips with a date within the period (or they are already in another settlement). Check the period and the attempts (`GET /delivery-attempts?settled=false`).

**A settlement line came out at $0.**
The driver's rate for the order's service and package type is missing, or the one for some attempt level; the line says “no rate configured”. Load the rate, cancel the settlement and generate it again, or adjust the line with its reason.

**I changed a rate and the settlement did not change.**
That is on purpose: the calculation is frozen when generating. Cancel the settlement (if it is generated or reviewed) and generate another.

**`Only a reviewed settlement can be approved.` / `Only a generated settlement can be reviewed.`**
The flow is Generated → Reviewed → Approved. Review first; approving also asks for re-authentication.

**`Enter the adjustment reason.`**
Changing the amount of a line requires the reason (up to 250 characters). If you leave the amount equal to the calculated one, the adjustment is removed and no reason is needed.

**Why doesn't my permission let me approve?**
Approving and exporting use `driverpay.approve` and `driverpay.export`, which by default only the company administrator has; whoever generates (`driverpay.manage`) does not approve.

## Batch 95 — Export the driver settlement

**`Only an approved or exported settlement can be exported.`**
First it has to be reviewed and approved (approval asks for re-authentication). A generated or reviewed one is not exported.

**I do not see templates to export the settlement.**
The first time, “Standard format” is created with all the columns. To have others, create them in the `DRIVER_SETTLEMENT` scope (requires `billing.export`); to see them `billing.view`, `cod.view` or `driverpay.view` is enough.

**Can I download the file again?**
Yes: an exported settlement can be exported again (with another template if desired). The status does not change.

**Does exporting pay the driver?**
No. Teikem calculates and exports; the payment is made by payroll or accounting with the file.

## Batch 96 — Driver settlement screen

**I do not see “Driver settlement” in the menu.**
You need `driverpay.view` and the transport orders module (`LTL_GROUND`) on.

**The driver selector is off.**
Listing the drivers requires `fleet.view`. Without it the settlement is for “All drivers” (a specific driver is chosen with a user who can see the fleet).

**I search for an order to record an attempt and several appear with the same number.**
The shipment number is repeated across clients. Each option brings the invoice and the consignee; choose the one of the right client (the attempt list then shows the client of each order).

**When exporting I cannot choose a template.**
Choosing a template requires viewing billing or driver pay and the Billing module on; without that it downloads with the standard format.

**Approving asked for my password.**
That is the re-authentication (AAL2): it is required to approve just as in Billing and in COD remittances.

## Batch 97 — Waybill number

**What is the waybill number and how does it differ from the shipment number and the packing number?**
The **waybill** (`CND-#######`) is unique per order across the whole company and is generated by Teikem: it is the one given to the client and the consignee. The **shipment number** is entered by the client or generated by Teikem per client and may be repeated across clients. The **packing** number (`EMP-#####`) is the one used for scanning in the warehouse.

**Can the waybill be changed or typed?**
No: Teikem always generates it, so that it is unique.

**The label of an old order does not show the waybill.**
All existing orders received it when the database was updated. If a label still shows the shipment number, the list has not been reloaded with the new API (the record brings `waybillNumber`).

**I search for a waybill and it does not appear.**
Search for it in full or in part (`CND-0000042`) in the Orders search box; the waybill of a deactivated order only appears with “include inactive”.

## Batch 98 — Client notices

**The client does not receive any notice.**
Everything is off until the company turns it on: in the client record, “Client notifications” panel. The events are only recorded from the moment the notice is on.

**`The e-mail '…' is not valid.` / `The phone '…' is not valid.`**
Fix the value: the extra e-mails and phones go separated by commas (up to 10 of each); the phone has 7 to 15 digits.

**I turned on SMS and nothing goes out.**
SMS only saves the preference: it does not go out until the provider is contracted. The log shows the send as “skipped”.

**Why isn't the consignee notified?**
These notices are for the client. Notifying the consignee (with their acceptance, which the client manages from their portal) is built later.

**The log says “No recipients”.**
The client has no active users in the portal and no extra e-mails in that notice. The summary still goes to the portal inbox if that checkbox is on.

<!-- kemi:portal -->
## Batch 99 — Legal document

**`You must accept the privacy policy and the terms of use to continue.` (403 `legal_acceptance_required`)**
The user of that session has not accepted the current version of the document. They must sign in to the web app, read it to the end and press “I accept”. With that, the rest of the API responds again.

**`The document version is no longer the current one. Read it again.` (409)**
The document changed while the person was reading it. Have them reload the screen and accept it again.

**The “I accept” button does not get enabled.**
You have to scroll the text to the end (on the phone, swipe inside the box down to the bottom). The button only gets enabled when you reach it.

<!-- kemi:staff -->
**How do I know who has accepted and who has not?**
Security & audit → “Legal acceptance” tab (`admin.audit` permission). The pending ones come first; “Earlier version” is whoever accepted before a change of the document.

<!-- kemi:staff -->
**Bracketed marks ([RAZÓN SOCIAL DEL OPERADOR]) appear in the document.**
The operator data is missing in the configuration (`Legal:OperatorName`, `Legal:OperatorAddress`, `Legal:ContactEmail`, `Legal:Jurisdiction`). They have to be filled in before production.

<!-- kemi:staff -->
**What happens with the users that already exist when going to production?**
They have no saved acceptance, so they will see it on their next sign-in. When the document changes, everyone will see it again.

<!-- kemi:portal -->
## Batch 100 — Secure access

**“Confirm this computer” — it asks me to confirm by e-mail again.**
You signed in from a browser that is not recognized: another browser, incognito mode, browser data cleared, 30 days without using that computer or a password change. Open the link in the e-mail (it can be from the phone), go back to the sign-in screen and sign in again.

**`The link is not valid or has expired. Try signing in again to receive a new one.` (400)**
The confirmation link lasts 24 hours and is used only once. Try signing in again to receive a new one.

**`Invalid credentials.` even though the password is correct.**
The account is locked because of failed attempts (1, 5, 15 or 60 minutes depending on repeated lockouts). Wait the time stated in the warning e-mail; the message is the same on purpose, so as not to reveal whether the user exists.

**`Too many attempts; wait a minute and try again.` (429) when signing in.**
The limit of 60 attempts per minute from that IP address was exceeded. Wait a minute.

**`Several confirmation e-mails have already been sent today…` / `The confirmation email could not be sent.`**
Limit of 10 confirmation e-mails per user per day, or the platform's e-mail is not configured. Wait or ask the administrator to check the e-mail sending.

**`This client does not identify the computer; sign in from the web.`**
The feature is on and the caller (an integration, for example) does not send the computer's fingerprint. They must sign in from the web app.

**`Computer not found.` (404)**
That computer was already removed or does not belong to the session's user.

**How do I remove a shared computer?**
My account → Sessions → My trusted computers → Remove.

<!-- kemi:portal -->
## Batch 101 — Client portal (access)

**`The user has no active access to the portal.` (403)**
The account exists but its portal user is not ACTIVE in any client (suspended, deactivated or the invitation not yet accepted). Check the client file → Portal users.

**`You have no access to that client.` (403)**
A sign-in or switch was requested to a client that does not belong to the account.

**`This route is not available to portal users.` / `This route is only for portal users.` (403 `portal_scope`)**
A portal session called a staff route, or a staff session called a portal route. Each one uses its own login.

**`Invalid credentials.` on the portal even though the password is correct.**
Account locked because of failed attempts (1, 5, 15 or 60 minutes), staff account (it signs in through its own login) or inactive account. The message is the same on purpose.

**`captcha_required` when signing in.**
After 3 wrong passwords the login asks you to solve the anti-robot challenge (Turnstile). Until it is solved the password is not checked.

**“It keeps asking me for the security code on the portal”.**
The code is remembered for 30 days per trusted computer. It asks again if it is a new computer, if more than 30 days have passed since you entered it, if you checked “Do not trust this computer” or if you changed your password.

**“The invitation expired”.**
They are valid for 48 hours and only once; ask the administrator to resend it (the previous link stops working).

<!-- kemi:portal -->
## Batch 102 — Client app

**“Cannot reach the server. Check your internet and try again.”**
The app did not reach the server (no internet, or the API address is wrong in this build). With internet, contact support: the address comes from `EXPO_PUBLIC_API_URL` / `TEIKEM_API_URL`.

**“Open with fingerprint does not appear.”**
The phone has no fingerprint reader or has no fingerprints enrolled. Enroll one in the phone settings and go back to Settings → Security. On the web it does not exist.

**The app asks for my fingerprint every time I come back.**
That is expected with “Open with fingerprint” on: it locks when opening and when coming back from the background after 2 minutes. It is turned off in Settings → Security.

**“You have N attempts left before the password is asked for.”**
The fingerprint failed. After 5 failures in a row the device session is closed and you have to sign in with the password.

**The “Do not trust this computer” checkbox always appears checked.**
The app remembers the last thing you left. Uncheck it in the login or in Settings if the computer is yours.

**“To continue, sign in to the portal from a web browser.” (anti-robot challenge on the phone)**
The server asked for the anti-robot challenge (after several wrong passwords) and the phone app does not integrate it yet. Sign in from a web browser.

**The notices are empty.**
Your company decides which notices it sends you and through which channel (chapter 23). If the “portal inbox” channel is off, nothing arrives here.

**“Put it on my desktop” does not appear.**
It only appears in browsers that allow installing the page (Chrome, Edge, Android) and when it is not yet installed. On iPhone and iPad use Share → Add to Home Screen.

## Batch 103 — “How is my order doing?” and the progress bar

**`We could not find that waybill number.` (404)**
The number does not exist, is misspelled, the order was deactivated, the company identifier in the link is not the right one, or the company has public tracking off. The page answers the same in every case on purpose.

**`Too many attempts; wait a minute and try again.` (429) on “How is my order doing?”**
More than 20 queries per minute from the same connection. Wait a minute.

**`The identifier must have 3 to 40 characters: lowercase letters, numbers and hyphens…` (400)**
In Settings → Public tracking: use only lowercase letters, numbers and hyphens, without starting or ending with a hyphen.

**`That identifier is not available.` (400) / `That identifier is already used by another company.` (409)**
It is a reserved word (api, login, track…) or another company already uses it: choose another one.

**Why doesn't the public page show a name or address?**
Because anyone with the number can look it up: only the package's progress is shown.

**The bar has more or fewer stages than before.**
The stages are the statuses of the company's normal path: if a status was added, removed or turned off, the bar changes.

**The bar does not advance (order with “Failed attempt”).**
A lateral status (today, Failed attempt) does not advance the bar: it stays where it was and warns. It continues when the order returns to the normal path.

**My company's public tracking does not respond.**
It comes off: the administrator turns it on in Tenant settings → General → Public tracking.

<!-- kemi:portal -->
## Language and theme (client app and public page)

**How do I switch to dark mode?**
In the login, the “How is my order doing?” page, the legal document or Settings: **Automatic / Light / Dark**. Automatic follows the phone or the browser; Light and Dark override the system. It is remembered on that device.

**How do I switch to English?**
In the same places: **Español / English**. Spanish is the default; the choice is remembered.

**The app opened in dark mode and I did not ask for it.**
It is on Automatic and the phone or the browser is in dark mode. Choose “Light” to fix it.

<!-- kemi:portal -->
## Messages with the customer service team (Batch 104)

**“Write the message.” (400)** — The message is empty or contains only spaces. Write the text and send again.

**“The message cannot exceed 2000 characters.” (400)** — Shorten the message or send it in two.

**“Too many messages in a row; please wait a while before writing again.” (429)** — Each person can send 30 messages per hour. Wait a while.

**“The person is not an active portal user of that client.” (400)** — When writing to someone first: that person does not belong to the client or is deactivated. Choose another one from the client's list.

**“Only active users of the company can be on the team.” (400)** — When designating the team, someone inactive or from another company was included. Choose from the candidate list.

**Who handles a client's messages?** Its designated team; if it has none, anyone with the `clientmessages.reply` permission. The administrator sees everything.

**Do they find out about the message by e-mail?** Not yet: the team sees the unread count in the inbox (it refreshes on its own every few seconds). The e-mail to the client when the team replies is not done either.

**Can another person at my company read my messages?** No: each person has their own conversation with the team.

**Can files be attached?** No, for now only text.

<!-- kemi:portal -->
## New client order (Batch 105)

**I do not see “New order” in the app.** Your role is read-only (`CLIENT_READONLY`); through the API it gives 403 “Your user can only view…”. Ask the company to change your role.

**“Choose the consignee or enter a new one (name, address and city).” (400)** — The consignee is missing or the new one has no name, address and city.

**“Choose a consignee or enter a new one, not both.” (400)** — Both were sent; remove one.

**“Add at least one package.” (400)** / **“A shipment can carry up to 50 packages.” (400)** — The order carries 1 to 50 packages.

**“The notes cannot exceed 500 characters.” (400)** — Shorten the notes.

**“The invoice number is assigned by Teikem for this client; leave it blank.” (400)** — Your account does not type invoices: do not enter an invoice number (the field does not appear in the app).

**Does the order I create need the company's approval?** No: it goes straight into the normal flow, like one created by the company.

**Can I set a price or priority on it?** No; only the basics. The company completes the rest.

## Shipment status (Batch 106)

Questions about the new transport order flow: **Submitted → Picked up → Unassigned → Assigned → In transit → Delivered**, with **Failed attempt** (lateral), **Cancelled** and **In warehouse** (final). The details are in [chapter 03, section 4](03-ordenes-de-transporte.md#4-statuses-and-transitions).

**What happened to Draft, Confirmed, On hold, Partial, Inbound, Planned and On site?**
They no longer exist. The order is born **Submitted** (what used to be Draft); “Confirm” is now **Mark as picked up** (it goes to Pickup); Inbound is **Unassigned**; Planned is **Assigned**; On hold, Partial and On site disappeared (what was in those statuses in development databases became Failed attempt or In transit). In production there were no orders to change.

**What does “Enter the reason for the cancellation.” mean? (400, field `comment`)**
A cancellation was attempted without writing the reason. Since Batch 106, cancelling an order requires a reason (up to 500 characters); it stays in the history. On the screen, write it in **Reason for cancelling**. If you call the API, send `comment` in `POST /api/v1/orders/{id}/cancel`.

**What does “Enter the reason for reopening the shipment.” mean? (400, field `comment`)**
A reopen was attempted without a reason. Write why it is being reopened (up to 500 characters) and retry.

**What does “Only a shipment in a final status can be reopened.” mean? (422)**
The order is not Delivered, Cancelled or In warehouse (for example, someone already reopened it). Reload the screen. An order in progress is not reopened: it is changed with its normal actions.

**What does “A reopened shipment must end up in an in-progress stage.” mean? (422)**
In **Target status** (or in `toCode`) a final status was chosen (Delivered, Cancelled or In warehouse). Choose an in-progress one (Submitted, Pickup, Unassigned, Assigned, In transit or Failed attempt), or leave “The last one it was in”.

**What does “The shipment has no previous status to go back to; specify the target status.” mean? (422)**
It was reopened without choosing a target and the order has no in-progress status in its history to go back to. Choose the **Target status**.

**What does “Missing permission 'orders.reopen'.” mean? (403)**
Your user cannot reopen orders. The `orders.reopen` permission (“Reopen shipments in final status”) is included in the Company Admin role; for another role, the administrator adds it in System → Roles. A `PERMISSION_DENIED` event is left in the security log.

**Who can reopen an order and what happens when it is reopened?**
Whoever has `orders.reopen`. The order goes back to an in-progress stage (by default, the last one it was in), the reason stays in the history and the `order_reopen` security event is recorded. It is **not** quoted again nor is the credit measured again.

**What does “The shipment is in “Submitted”; mark it as picked up before assigning it to a route.” mean? (422 when adding to a route or dispatching)**
The order is still Submitted and a Submitted order is not assigned to a route. Open it in Orders and press **Mark as picked up**; then add it to the route.

**Why does my order already have a price when I create it?**
Since Batch 106 the order is quoted when created if the client has a contract and a rate. The price is frozen when it is marked as picked up. While it is still Submitted and quoted, editing it re-quotes it.

**My Submitted order has no price. Is it an error?**
No. If the client has no contract or the rate for that service and package is missing, the order is saved **without a quote and without an error**. The error `No hay tarifa vigente para X/Y; …` appears when marking it as picked up. Set up the rate in Clients → Contract → Rates.

**When saving an order, “The client exceeds its credit limit” appears and it is not created. Didn't this use to happen?**
Before, credit was measured when confirming; now it is measured **when creating** (Submitted already counts toward what the client has in progress). With `orders.credit_override` use **Authorize and save**; otherwise, notify Billing or ask for the limit to be raised. See chapter 03, section 3.3.

**“overrideCredit requires confirmNow=true.” no longer exists. Why?**
Because `overrideCredit` is now applied when creating the order (and when marking it as picked up), with no need for `confirmNow`. If an integration sent `confirmNow` together with `overrideCredit`, it keeps working.

**Can I keep sending `confirmNow: true` when creating an order?**
Yes, the API accepts it: it creates the order and marks it as picked up at once. The web screen no longer uses it; the warehouse app still uses it when packing and dispatching with delivery.

**What does “Illegal jump: from 'X' you can only advance to 'Y'.” mean? (422)**
An attempt was made to move the order to a stage that is not the next one (for example, from Pickup to Assigned). Today the status engine only advances from one stage to the next; routes take it through the intermediate stages on their own. The scan that allows skipping stages comes in a later batch.

**What does “Status OrderStatus 'X' not found.” (404) or “Status 'X' does not exist or is not enabled for this company.” (422) mean?**
A status that does not exist was requested (404 in `POST /orders/{id}/status`; 422 when reopening with a `toCode` that does not exist or that the company turned off). If `X` is `DRAFT`, `CONFIRMED`, `INBOUND`, `PLANNED`, `ARRIVED`, `ON_HOLD` or `PARTIAL`, it is an old code that Batch 106 removed: use `SUBMITTED`, `PICKUP`, `UNASSIGNED`, `ASSIGNED`, `IN_TRANSIT`, `FAILED` or `IN_WAREHOUSE`. Check integrations and saved filters.

**What does “To confirm, use the confirmation action.” mean? (422)**
An attempt was made to move the order from Submitted to Pickup with `POST /orders/{id}/status`. Use **Mark as picked up** (`POST /orders/{id}/confirm`).

**What does “Only a shipment in its initial status can be confirmed.” mean? (422)**
An attempt was made to mark as picked up an order that is no longer Submitted. Reload: probably someone else marked it before.

**What does “The advance to 'X' is performed by the corresponding module (trips/deliveries); only lateral statuses are recorded from here.” mean? (422)**
From the order file only the Failed attempt and its return are recorded. Moving to Unassigned, Assigned, In transit or Delivered is done by routes, dispatch and deliveries.

**Where did “Plan” go?**
Planning is not a status: it is the **requested date** of a Submitted order (the ready-date field on the form).

**What is “In warehouse” and why don't I see it on any order?**
It is a final status for **pickup-only** service types. The “pick up” service type and its flow (Submitted → Picked up → In warehouse) come in a later batch; for now no order gets there. It blocks every action except **Reopen**.

**How does a Failed attempt look on my client's progress bar?**
The bar stays at the level of the last normal stage (for example In transit), in alert color, and warns “Failed attempt”. It stays in the history.

**My order indicators, charts or reports changed their numbers. Why?**
The database update rewrote the saved order filters that named old statuses: `CONFIRMED` and `DRAFT` became `SUBMITTED`, `INBOUND` became `UNASSIGNED`, `PLANNED` became `ASSIGNED`, and `ARRIVED` and `ON_HOLD` became `FAILED`. If a filter of yours no longer says what you wanted, edit it in Analytics. “Shipments in progress” now counts Submitted, Pickup, Unassigned, Assigned, In transit and Failed attempt.

## Scanning with a chosen action (Batch 107)

**What do I do if I scan and nothing happens (“it is already in Assigned or further along”)?** The order was already in that status or further along: the scan does not move it backwards. Only “Arrived” returns an order in Failed attempt to the warehouse.

**“Order … is in a final status (Delivered): it is not changed.”** Delivered, Cancelled and In warehouse are final. To reopen it you need the `orders.reopen` permission (“Reopen” action in the order record), with a reason.

**“Choose the action: pick up, arrived, route or depart.” (400)** The station sent an unknown action; choose one of the four.

**Route said “There is no open route for the zone…” and the order did not change status.** That is on purpose: Assigned means it is on a route. Create or plan the day's route and scan again; in the meantime you can use “Arrived” to leave it Unassigned.

**Why does the history say “(skipped)”?** A scan took the order further along at once; the statuses in between are recorded with the same date and time, and clients are not notified about them.

**Can I create an order for a client without a contract or without a rate?** No (Batch 107): it is checked when submitting. Create the contract and the rate first.

## Pieces and their number (Batch 109)

**Does each piece have its own number?** Yes: when the order is submitted, a line of 3 pieces creates 3 pieces, each with its unique number (the one on the label and the one that is scanned). The order number is a different one.

**“The code cannot exceed 10 characters.” (400)** The client's and the consignee's Codes are at most 10 characters; use a shorter one. A Code that already existed with more than 10 keeps working: its first 10 are used in the piece number.

**The consignee has no Code, what number does it carry?** Its ID (an integer of up to 10 digits). Give it a short Code if you want to recognize it at a glance on the label.

**“A piece with that package number already exists.” (409)** Two active pieces in the company cannot have the same number (for example, two lines with the same typed number). Change the typed number or leave it blank.

**I edited the order's packages and the label numbers changed.** That is expected: when the packages are replaced, new pieces are created with new numbers; reprint the labels.

## Scanning per piece and new waybill (Batch 110)

**“The piece was not found.” when scanning the order number.** At the station you scan the number of **each piece** (the one on the label), not the order number, the packing number or the invoice number.

**What do the sounds say?** “Yes” when what was scanned was found, “No” when it was not found and “Ok” when it is a duplicate.

**“Piece … had already been scanned with this action.”** That piece was already recorded with that action: it is not counted twice. Scan the others.

**The order was left at “4 of 5”, what happens with the missing one?** If the order moves to the next action without that piece, or if “Close count” is pressed, the piece is left missing as “To verify”. If it turns up and is scanned, it stops being missing.

**When do I use “Close count”?** When you finish scanning an order in an action (for example, when you finish receiving it) to leave the pieces that did not arrive recorded, without waiting for the next action.

**How is the order number built now?** Client Code + consignee Code (or its ID) + date (yymmdd) + that consignee's shipment number of the day, with no separators.

## Claims and missing pieces (Batch 111)

**What is the path of a missing piece?** To verify → Lost → Claimed → Resolved. “To verify” is set by the scan station; “Lost” and the claim are done by whoever has `claims.manage` in Operations → Claims; “Resolved” is when the claim is closed.

**“Specify at least one piece.”** (400/422) No piece was sent, or the order has no “To verify” pieces to mark. Check the “To verify” tab.

**“Piece … is not “To verify”.” / “Piece … is not “Lost”: mark it as lost before claiming it.”** (422) The piece is not in the step that action requires. First it is marked as lost, then it is claimed.

**“Piece … does not belong to that order.”** (400) The piece number belongs to another order.

**“The shipment has no lost pieces to claim.”** (422) All the “Lost” pieces of that order are already in a claim (or there is none lost).

**“The declared value cannot be negative.” / “Notes cannot exceed 1000 characters.”** (400) Fix the field.

**“Choose the resolution: paid, rejected or closed.” / “Enter the amount paid.”** (400) When resolving you have to choose the resolution and, if it is “Paid”, enter the amount.

**“The claim is already resolved.”** (409) A resolved claim is not resolved again.

**“The piece belongs to a closed claim.”** (the sound says “No”) A piece whose claim is already resolved was scanned; it is not accepted.

**A piece that was lost or claimed was scanned, what happens?** It turns up: it is removed from its claim (if it is not closed) and, if the claim is left without pieces, it is deleted. Its new order is created when the count is closed: the station shows “This is what is going to happen” and it must be confirmed.

**When pressing “Close count” a message with a list came out, what do I do?** It is what is going to happen when closing: the pieces on the list that were not scanned will be left “To verify” and, if lost or claimed pieces turned up, a new order will be created with them. “Confirm and close the count” does it; “Cancel” changes nothing.

**What status does the new order have?** The same ones as the original up to Pickup (Submitted and Pickup, with their same dates) and then the one of the action the piece was scanned with. If it was “Route”, it is also assigned to a route (if there is no route to receive it, the count is not closed).

**Is the new order charged again?** It depends on the contract: if it charges per pickup, no (the original is already charged in full); if it charges per delivery, it is quoted with its pieces and charged when it is delivered, and those pieces are removed from the original.

## Return (Batch 112)

**What happens when I return an order?** It becomes “Returned” (final) and the return order is created with the pieces being returned: same client, pickup at the consignee and delivery at the client's pickup address, with no freight charge. See chapter 32.

**“A shipment in “Submitted” is not returned: it has not been picked up yet. Cancel it.”** (422) The order has not been picked up yet; it is cancelled instead of returned.

**“Order … is in a final status (…): it is not returned.”** (422) It is already Delivered, Cancelled, In warehouse or Returned with no pieces left to return.

**“Piece … is listed as missing: it cannot be returned.”** (422) The piece is To verify, Lost or Claimed: it is handled in Claims, it is not returned.

**“The client has no registered address … to return the order to”** (422) The return goes back to the client that placed the order: to its pickup address, and if it has none, to whatever address it has registered. This only appears if the client has none.

**“The order already went out for return: cancel the return order first …”** (422) A Returned order is not reopened while its return order is still alive; the return order is cancelled and then it is reopened.

**I do not see the “Return” action at the station.** The `orders.return` permission is required.

**I scanned “Return” on a piece and the order became “Returned”; what about the other pieces?** Keep scanning “Return” on the ones being returned: they are added to the same return order. On “Close count”, the ones not scanned are left “To verify”.

**“Returning it is suggested: N failed attempt(s)” appears, do I have to return it?** No: it is only a per-company suggestion (Settings → General → Return, 2 by default). The order can keep being retried; if you prefer not to see the suggestion, set 0.

**“Specify after how many failed attempts a return is suggested: from 0 (no suggestion) to 20.”** (400) The setting only accepts whole numbers from 0 to 20.

## “Pick up” service type (Batch 114)

**What is a pick up?** A collect-and-bring service type: the shipment goes Submitted → Picked up → **In warehouse** (final) and does not go out on a route. See chapter 33.

**I scanned “Route” or “Depart” and it says “Order … is a pick up: it stays in the warehouse.”** A pick up order does not leave the warehouse; to send out what was collected another order is created (or the client comes to get it).

**“There is no rate in force for PICK_UP/…” when submitting a pick up.** A billable pick up needs a rate in the client's contract. If it is not going to be charged, turn off **Billable** in Lists → Service type → Pick up → Adjust.

**When is a pick up charged?** When it reaches **In warehouse** (with “Arrived”), regardless of the contract's billing model; if “Billable” is off, never.

**How do I charge for the return?** In the client's contract, **Return fee**: per order (one amount), per package type or per returned piece. It is added to the returned order and billed as “Return fee” in the freight billing run. Without setting it up nothing is charged.

**“Enter the return fee amount.” / “The return fee cannot be negative.”** (400) Enter the amount (zero or more) for the per-order or per-piece mode.

**“Enter the amount of at least one package type.”** (400) The per-package-type mode needs at least one type with its amount.

**The return fee did not appear on the invoice.** The order must be Returned with an amount greater than zero, not billed before, and the run must include freight and cover the date on which it became Returned.

<!-- kemi:portal -->
## Consignee notices (Batch 116)

**Who turns on the consignee notices?** The client, from the portal (administrator or operator); not the company. See chapter 34.

**“Turn on the consignee notice first.”** (422) Before marking a consignee, the client's notice must be turned on.

**“To mark that the consignee receives notices you must accept the privacy warning.”** (400) Marking requires accepting the warning (it confirms that the consignee agreed to receive notices through that channel).

**“The consignee has no registered e-mail…”** (400) Add the e-mail (or phone) in the consignee record and mark it again.

**“The consignee unsubscribed from notices through that channel: it cannot be marked again.”** (409) The unsubscription is final; only the consignee themselves can accept again, with a new agreement with the company.

**Which statuses are notified?** When it goes On route, on each failed attempt and departure, and on the final statuses (Delivered, Returned, Cancelled, In warehouse). Never Pickup, Unassigned or Assigned, nor the skipped statuses.

**The notice was left “skipped”.** The configured e-mail or the client app address (`ConsigneeNotices:ClientAppBaseUrl`) is missing, the consignee unsubscribed or was no longer marked; the reason stays in the notice.

**The “How is my package doing?” page no longer asks for the waybill number.** It is by link only: the consignee opens it from the notice they receive (the link carries a token of its own for their order). A link that does not exist responds `No encontramos ese enlace de seguimiento.` (404).

**I opened the unsubscribe link and I keep receiving notices.** Opening the link does not unsubscribe; you have to press “Yes, unsubscribe me” on the page. A link that is not valid responds `El enlace de baja no es válido.` (404).

**Where is “Public tracking” turned on in Settings?** It no longer exists (Batch 116): tracking travels in the link of each consignee notice, which the client turns on in their portal.

**Who gives a consignee access?** Only the client (administrator or operator), by invitation to an e-mail from the portal (chapter 35). The company does not do it: if it tries from the client file it responds `El rol de consignatario solo se da desde el consignatario.` (400).

**The consignee cannot turn the notices back on.** The client turns on the notices and the channel; the consignee can turn them off and turn back on only the one they turned off. If the client did not turn it on for them: `Solo su cliente puede prender este aviso: usted puede volver a prender únicamente el que apagó.` (409). If they unsubscribed with the link of a notice, the unsubscription is final (`El consignatario se dio de baja de los avisos por ese medio: no se puede volver a marcar.`).

**The consignee signs in and the client routes give them 403.** That is correct: their session only reaches their space (`Esta ruta no está disponible para usuarios del portal.`); a person from the client, in turn, does not enter that space (`Este espacio es solo para consignatarios con acceso.`).

**Does the consignee use two-step verification?** No: they sign in with password and the trusted computer (confirmed by e-mail the first time and lasting 30 days without use).

**I invited the consignee and it responds “That e-mail is not available for this company's portal.” (409).** That e-mail already has access in this client, or belongs to a staff user or to another company; the message is the same in every case so as not to reveal where it is registered. Use another e-mail.

**Who does the consignee talk to in messages?** With their client (not with the company): they open the conversation with their first message and the client's administrators and operators reply. Whoever is read-only reads it without being able to reply (`Su rol en el portal es de solo consulta: puede leer los mensajes de los consignatarios, no contestarlos.`, 403).

**The client does not find out that the consignee wrote to them.** For now the notice is the number of unread conversations in their consignee messages inbox; no e-mail is sent.

**Where do I give a consignee access?** In the client app: Settings → Notices to my consignees → Set up notices, on the consignee's card press “Access” and “Give access” (chapter 35 §5). Only administrators and operators.

**The consignee signs in and does not see Home or Orders.** That is as intended: their space has Notices, Messages and Settings. If they open Home or Orders, the app sends them to Notices.

**Can the consignee create orders?** No. They only see their shipments (Shipments tab). Orders are created by the client from their portal and by the company staff (intake). If the consignee tries to create one through the API, it responds 403 `Esta ruta no está disponible para usuarios del portal.`

**The consignee does not see a shipment.** They only see their client's orders whose delivery is at their consignee; one from another consignee (even if from the same client) responds 404.

**The client did not receive the e-mail that a consignee wrote to them.** The e-mail goes to the client's active administrators and operators, it is a single one while there are unread messages (once they read them, the next one notifies again) and it only goes out if the company's e-mail is configured; the app's “unread” counter is always there.

## Record the delivery from the web (Batch 118)

**Where do I record that an order was delivered?** In the order record (Operations → Orders) with the **Register delivery** button. The `orders.deliver` permission is required.

**I do not see the “Register delivery” button.** The `orders.deliver` permission is missing (the administrator grants it), or the order is already in a final status (Delivered, Returned, In warehouse or Cancelled), or it is inactive.

**“Enter who received the delivery.”** The name of the person who received is required (up to 150 characters). Enter it and record again.

**“The delivery date and time cannot be in the future.”** The date and time cannot be more than 5 minutes ahead. Leave it empty to use this moment.

**“Order {n} already has its delivery recorded.”** (409) That order already has its proof; it is not recorded twice. To see the proof, open the order record.

**“Order {n} is in a final status ({status}): its delivery is not recorded.”** (422) A Delivered, Returned, In warehouse or Cancelled order is not delivered again. If it was delivered by mistake, the reopen permission allows returning it to an in-progress stage.

**“Order {n} is Pick up: it ends “In warehouse” and is not delivered.”** (422) Pick up orders (collect and bring) do not go out for delivery.

**Who signs the proof?** The user who records it, automatically (they have the permission, that is why they are authorized). The proof says “Signed automatically by [user]” and “The consignee did not sign”. When the driver app exists, there the consignee does sign.

**The proof says “No location” or “Lookup failed”.** The location is looked up separately with Google, a single attempt of 30 seconds. If Google is off on the server, the address is not found, it takes more than 30 s or the key fails, it is left without location; the delivery is valid anyway. The server administrator turns it on with `GoogleMaps:Enabled` and the key in `GoogleMaps__ApiKey`.

**Can I attach photos to the delivery?** Not yet: where the files are stored is still to be decided.

<!-- kemi:portal -->
## Client portal: invoices, my notices and proof of delivery (Batch 119)

**Can I pay or mark an invoice as paid from the app?** No: invoices are read-only. The company records the payments; as soon as it does, the invoice shows the payment and the balance.

**One of my invoices does not appear in “My invoices”.** Only invoices already issued are shown (not drafts) and only those of your client. If the company does not have the billing module turned on, the list responds “The 'BILLING' module is not enabled for this company.”.

**What is “My notices”?** In Settings → Set up my notices you decide which notices you want to receive by email and which to see in the inbox. The company decides which notices your business receives; you can only turn off the ones you do not want.

**“The company does not have this notice turned on for your business.”** That notice (or that channel) is not turned on for your client: it cannot be turned on from the app. Ask the company to turn it on in the client record (Client notifications).

**I turned off the email for a notice and it keeps arriving.** If the email arrives at an address the company entered as an “extra email” for the notice, it is not yours: carry on as usual. It may also take until the next digest.

**“The notice 'X' does not exist.”** (400) There are only five notices: PICKED_UP, OUT_FOR_DELIVERY, DELIVERED, COD_PROCESSED and INVOICE_ISSUED.

**Why does the proof of delivery say the consignee did not sign?** Because the delivery was recorded by the company's staff from the web (with their permission, the signature is on behalf of the company). When the delivery is recorded with the driver app, the signature will be that of whoever received it.

**Who recorded the delivery?** The client's proof of delivery does not give the user's name: only “the company's staff”. The company does see it in its order record.

<!-- kemi:portal -->
## Client portal: my profile and indicators (Batch 120)

**Can I change my email from the portal?** No: the email is the one you sign in with and changing it requires verifying it. Ask the administrator of your business or of the company to change it. What you can change yourself is your name and your password (Settings → My profile and password).

**“Enter your name.” / “The name cannot exceed 150 characters.”** (400) The name cannot be empty or exceed 150 characters.

**My business's details are wrong.** They are shown read-only in My profile → My company. The company changes them: write to them from Messages.

**“The current password is incorrect.”** The current password you typed is not the right one. Check it and try again. When you change the password, your other sessions are closed.

**“The password must have at least 12 characters.”** / **“The password must have at least 4 different characters.”** The new password must meet the policy (12 characters or more, not just one repeated character).

**“Days must be 7, 30 or 90.”** (400) Indicators can only be requested for the last 7, 30 or 90 days.

**“On-time deliveries” does not appear in Indicators.** None of the orders delivered in the period had a promised date; without a date it is not possible to say whether it arrived on time.

**Indicators does not show the invoice balance or the amount collected pending remittance.** The company does not have the billing or the cash-on-delivery module turned on.

**What does “In progress now” count?** All orders that are not yet in a final status (Delivered, Returned, In warehouse or Cancelled), and how many of them are in transit.

## Clients and Consignees in “Roles & users” (Batch 121)

**Where do I see all the portal people and the consignees with access?** In System → Roles & users, tabs **Clients** and **Consignees**. The `portalusers.manage` permission and the Client portal module turned on are required; otherwise those tabs do not appear.

**Can I invite someone from those tabs?** No: the people of a client are invited by the company from the client record (Portal users) and consignees are invited by the client from their portal. Here they are controlled: suspend, reactivate, deactivate and resend the invitation.

**I suspended someone and they still see the portal.** They should not: when suspending or deactivating, their open session is cut off instantly (up to Batch 120 it could last up to a minute). If it happens, close their sessions and tell support.

**“The type must be CLIENT or CONSIGNEE.”** (400) The `kind` filter of `GET /portal-users` only accepts CLIENT or CONSIGNEE (or none, for all).

**Who controls staff, portal and consignee users?** The platform administrator and the administrator of each company (those of their company): staff in the Users tab, the clients' people in Clients and consignee accesses in Consignees.

**I do not see a deactivated person.** Turn on “Include removed”.

## SLA at risk (Batch 122)

**Where does an order's SLA come from?** From the client's contract: the SLA tab has one row per service type with the “maximum hours”. If the order has no contract, or its service type has no maximum hours, it has no SLA and is not included in the notice.

**From when does the deadline count?** From when the order is picked up (“Pickup”), not from when it is created. An order that has not been picked up yet has no clock running.

**When is it “at risk”?** When less than 25% of the deadline is left (minimum one hour of margin). With a 48-hour SLA, it becomes at risk when 12 hours or less are left.

**I do not see the “SLA at risk” notice.** `orders.view` and the Transport module (`LTL_GROUND`) are required, and there must be some picked-up, open order close to the deadline (or past it) with an SLA in its contract.

**I delivered the order and the notice remains.** The notice only counts open orders; once delivered it disappears on the next read. If it was late, it stays as `MISSED` (Outside SLA) in the `SlaState` field.

## Kemi (Batches 123–124)

**Does Kemi use artificial intelligence?** No. It searches for the words of the question in the manual and in the FAQ and brings up the options; AI comes in a later stage.

**Kemi did not find anything about my question.** Try other words (the name of the screen, the error message in quotes, or a single keyword such as “invoice” or “SLA”). If it says it cannot find anything, nothing in the manual or the FAQ is similar enough: that is better than answering something that is not right.

**I said “No” to “Did that answer your question?” and it showed me other options.** That is expected: it searches elsewhere with what it had not shown yet. If nothing is left, it says so.

**I do not see the option to ask.** Kemi must be awake (if she is asleep, a click on the header icon wakes her up); the option is the first one in her list.

**I typed the question in English.** Kemi understands common English words (order, invoice, password, driver, delivery…) and searches for their equivalent, but the manual and the FAQ are in Spanish: the answers come out in Spanish. If it finds nothing, try the word in Spanish.

**I corrected the manual and Kemi still has the old text.** The index is regenerated with `npm run help:index` (or `npm run check`) and arrives with the next build of the web app.

## Operational alerts and SLA (Batch 125)

**What new notices are there in “Needs your attention”?** Shipments in failed attempt, open claims, fleet documents expired or about to expire and overdue invoices. Each one is seen by whoever has the permission (`orders.view`, `claims.view`, `fleet.view`, `billing.view`) and the module turned on; if there is nothing to report, it does not appear.

**The failed attempt notice remains after rescheduling the order.** The notice counts the orders that are in “Failed attempt”; as soon as the order changes status, it disappears on the next read.

**The overdue invoices do not match the ones I see in Billing.** The notice counts invoices in “Overdue” status (the ones the scheduled job marked when their due date passed with a balance); an invoice that just passed its due date is marked in the next run of the job.

**Where do I see the percentage of deliveries within the SLA?** In the “Deliveries within SLA” indicator (turned on from “Organize” in the Pulse) and in the “Shipments by SLA state” chart. It only counts orders with an SLA in their contract.

## Process the client's invoice (Batch 126)

**In the old system we created an order to process the invoice. How is it done here?** Nothing is created: the invoice already comes in the order. When the order has been delivered, in Process deliveries → Reconcile you upload the sheet (or type the number) and the system checks it against the order. If the order has COD, the same sheet reconciles the collection.

**What do I put in “What are you uploading?”?** If you do not know, leave **Detect**: the system classifies each sheet by itself. Choosing COD, Invoices or Both only serves to warn you if what you uploaded is not what you said; everything is processed the same way.

**I got “Undelivered”.** The order exists but has not been delivered yet; the invoice is processed when it is delivered. Nothing needs to be done now.

**I got “Already processed”.** That order already had its invoice processed in another run (the order record says when and by whom). It is not processed twice.

**“The sheet type must be AUTO, COD, INVOICE or BOTH.”** (400) The API's `mode` field only accepts those values (or empty = AUTO).

**“To process invoices, choose the client of the sheets: invoice numbers repeat across clients.”** Choose the client in “Client of the sheets” (above the sheets and the manual codes) and go back to Reconcile. COD sheets do not require it.

**The client asks whether we already processed their invoice.** They can see it themselves in their portal: the order record says “Invoice processed: date”.

**Does processing the invoice cost anything?** Only if the client's contract has “Charge for checking the client's invoice” turned on with an amount; then the next billing run adds the line “Invoice check for order X”, once per order. Off (the usual), it costs nothing.

**“Enter the amount per checked invoice.” / “The check amount cannot be negative.”** (400) When turning on the charge in the contract, an amount of zero or more must be given.

## File store: signature and photos (Batch 127)

**Where are the delivery signature and photos stored?** As encrypted files on the server; only the reference is in the database. They are seen in the order record → Proof of delivery → “Signature and photos”, always with the session (there is never a link to the disk).

**“Order X does not have its delivery recorded; record the delivery before attaching files.”** (422) First the delivery is recorded (Register delivery); then the signature or photos are attached.

**“Only images (JPG, PNG, WEBP, GIF) and PDF are allowed.” / “The file content does not match its type.”** The type is decided by the actual content of the file, not by its name or by what the browser says.

**“The file cannot exceed 10 MB.”** Reduce the photo (the app compresses it by itself; from the web, use a smaller image).

**“The file store is not configured (Files:MasterKey); contact support.”** The server does not have the store's master key; support configures it (and backs it up).

**I removed a photo by mistake.** It is removed logically: the content remains in the store and support can reactivate it in the database (`StoredFile.IsActive`).

## Order filters by invoice processed (Batch 128)

**How do I see the orders whose invoice has already been processed (or the ones missing)?** In Orders, filter **Invoice processed**: “Processed” or “Not processed”. The “Invoice processed” column shows the date. The **Processing date** filter offers the suggested ranges (Today, This week, This month…) or a custom range, just like the warehouse date filters.

**The processing date filter returns nothing.** It only counts orders with an invoice processed within that range; the ones not yet processed have no date and are left out (use “Not processed” to see them).

## Zone levels: Island and Metro (Batch 129)

**How do I set up Island and Metro?** Fleet → Dispatch zones → “Zone levels” panel: create ISLA and METRO (give Island its default driver) and choose the level for each zone. New orders inherit the level of their delivery zone; for the ones that already existed, press “Assign zone to shipments”.

**An order has no zone.** Its ZIP or town is not in any active zone, or falls in two (ambiguous). Review the zone members and press “Assign zone to shipments” (or edit the order's consignee).

**Do the rates have to change per level?** No. The level in the rate is optional: without a level, the rate is the general one. If you add a rate with a level (Island), that one wins for Island deliveries and the general one continues for the rest.

**“The driver already has a current rate for X + Y at level Z”.** (409) There is already an open rate for that service, package and level; edit it or close it.

**“The level has active zones; remove it from the zones before deactivating it.”** (409) Remove the level from its zones (or deactivate them) and try again.

**The route did not take the Island driver.** The level's default driver applies only if the zone has no driver of its own (primary zone of some driver); if the zone has a driver, that one prevails.

## Custom fields in search and in Views (Batch 130)

**I marked a field and the filter does not appear.** Turn on “Show in search” in the field (Analytics → Custom fields) and reload the screen: the list of fields is remembered for a few minutes. Only active fields are offered; the filter appears in the list of the field's entity (Orders, Clients, Consignees, Drivers, Vehicles).

**“The custom field 'X' is not enabled for search.”** (400) The field exists but does not have “Show in search”. Turn it on, or use a View, which accepts any field.

**“The custom field 'X' does not exist.”** (400) The key in `cf.<key>` is misspelled or the field is inactive.

**“The value of filter 'X' is not valid.”** (400) A misspelled number or date, or a yes/no that is not `true`/`false`.

**And in reports?** All Views allow custom fields as column and filter, without marking anything.

**A multi-select filter does not find “frag”.** It filters by the complete option (`fragil`), not by fragments.

## Files with several lines per order (Batch 131)

**My client sends several lines per order and some are not pieces (description, invoice).** In the order template turn on “The file has several lines per shipment” and add rules: for example, if the “type” column equals `Invoice` the line is Invoice, and if it is one of `Drum | Item` it is Description. Whatever you do not mark is a piece.

**“The shipment has no pieces (all its lines are description or invoice).”** All the lines of that order fell under a description or invoice rule. Review the rules or the file: each order needs at least one piece.

**“Piece number X is repeated in row N of the file.” / “Piece number X already exists in another order.”** Each piece's label is unique in the company. Fix the file (or, if the file was already imported, do not upload it again). Repeated descriptions and invoices do not give this error.

**“The line has no shipment number; in a file with several lines per shipment all of them need it.”** A row without a shipment number cannot be grouped; fill in the column.

**“A file with several lines per shipment needs the orderNumber column in the template.” (400)** Assign a column to the “Shipment no.” field in the template; that is what groups the rows.

**Where does the description (“drone”) go?** In the description of the order's cargo lines, not as a piece. **And the invoice?** In the order's invoice number.

**The shipment or invoice number “is assigned by Teikem”.** With several lines per order the file brings the shipment number: in the client, mark that the client numbers its orders (and its invoices, if the file brings them).

## Integrations and their inbox (Batch 132)

**How do I connect a client that sends me files?** System → Integrations / API → New integration: client, import template, input folder (or manual upload), schedule and whom to notify. Requires the `integrations.create` permission plus `integrations.create.files`.

**“You cannot grant the scope “X”: you lack the permission…” / “…you do not have the permissions…”.** Whoever creates an integration cannot grant it more than they have. Ask the administrator for the permission of that family (`integrations.create.files`, `.transport` or `.warehouse`) or to create it themselves.

**“The company does not have the module that “X” needs.”** The scope depends on a module the company does not have turned on (System → Modules).

**“An integration with that name already exists.” (409)** Use another name.

**“The integration is already running; wait for it to finish.” (409)** There is a run in progress; wait for it to finish.

**“The integration is turned off; turn it on to run it.” (422)** Turn on the integration in the list.

**“That connector type is not available yet.”** Email and SFTP come in the next batches; for now input folder and manual upload.

**I uploaded a file and it ended up as “Repeated”.** The same content was already processed (the file hash is compared); orders are not duplicated.

**An order ended up in the inbox.** Read the reason, fix the cells with “Fix” and use “Save and reprocess”. If the reason is in the catalog or in the client (a missing consignee, a template), fix it and use “Reprocess all”. If it is not needed, discard it with a note.

**“This item's data is no longer kept…” (409).** The integration's retention days have passed (or the file could not be read): discard the item and ask for the file again.

**Who receives the notice of a run?** The users marked in the integration (and whoever created the integration only if they checked “Include me”). It arrives in the bell and by email when the run created orders, sent rows to the inbox or failed.

**Where does the processed file end up?** In the `procesados` subfolder of the input folder (the ones that failed, in `errores`); it is never deleted.

## Email integration (Batch 133)

**How do I read the orders a client sends by email?** New integration with the “Email” connector: IMAP mailbox (one per integration), which emails are taken (subject and allowed original sender) and what is read (attachments by name and type, the body with fields to read, or both). The integration's import template reads whatever comes out.

**“Could not connect to the mailbox: …”.** The server, port, user or password are wrong (or the mailbox does not allow IMAP). Try “Test connection”; in Microsoft 365 use a shared mailbox with IMAP and an app password.

**Where is the mailbox password kept?** Encrypted with the company's key; it is never shown or written to logs. When editing, leave it empty to keep it.

**“Connector passwords cannot be saved: the server's master key (Files:MasterKey) is missing.”** The server does not have the master key configured; ask the server administrator to set it (and back it up).

**“Sender not allowed: x@y.com.”** The ORIGINAL sender of the email is not in the list. If the email arrives forwarded, add the domain of whoever wrote it, not of whoever forwards it.

**“The required field of column N (…) was not found.”** The email body does not contain that label (or the format changed). Fix the field's label or discard the inbox item.

**“The email has no attachment that meets the criteria.”** There are no attachments of the configured type or name (or the email has no attachment and the body is not read).

**An email does not appear anywhere.** If the subject does not meet the integration's filter, it is ignored (it is not for it). If it does and it was not processed, check that it is unread and in the right folder.

**How do I test without touching the mailbox?** Upload an `.eml` file with “Upload file”: it is processed just like a received email.

**Is any email deleted?** Never: the handled ones are marked as read and moved to the processed folder.

## SFTP and FTP integration (Batch 134)

**How do I read the files a client leaves on their server?** New integration with the SFTP (or FTP) connector: server, user, password or key, the remote folder and, for SFTP, the server fingerprint. Teikem downloads the new files, processes them with the template and moves them to “procesados”. Requires `integrations.create` plus `integrations.create.files`.

**“The SFTP server fingerprint must be confirmed…”.** For SFTP the server's SHA-256 fingerprint must be set. Press “Test connection”: it shows you the fingerprint the server presents; compare it with the one the provider gave you and paste it in “Server fingerprint”.

**“The server fingerprint does not match the configured one…”.** The server presents a fingerprint other than the one set: the provider changed it or you are talking to another server. Confirm it with the provider before updating it.

**“Could not connect to the server: …”.** Check server, port, user and password (or the key), and that the server is reachable from the Teikem server. For FTP also check passive mode.

**The test says “0 ready to read” but there are files.** They do not meet the configured type (csv, txt) or name, or they were modified less than 10 seconds ago (they may still be uploading): they wait for the next run.

**Are the files deleted from the server?** Never: they are moved to “procesados” (or to “errores” if they could not be processed). If the user has no write permission, they stay where they are and the content hash prevents creating the orders twice.

**“Warning: unencrypted FTP…”.** The FTP connector without FTPS sends user, password and files in clear text. Enable FTPS or use SFTP if the server supports it.

**Where are the password or the key kept?** Encrypted with the company's key; they are never shown or written to logs. When editing, leave them empty to keep them.

**Client API — “Invalid credentials.” (401).** The key or secret do not match, the key was revoked or the integration (or its service account) is turned off. For security it does not say which. Create a new key in Integrations → API keys.

**Client API — “This API key does not have the scope “X” that this route requires.” (403).** Add that scope to the integration (Edit) and request a new token.

**Client API — “You exceeded the key's usage limit…” (429).** Wait the seconds in `Retry-After` or raise the key's limit (1 to 6000 per minute).

**Client API — Where do I see a key's secret?** Only when creating it; afterwards it cannot be recovered. Revoke the key and create another.

**Client API — “The API's transport scopes need the client…” (400).** Choose the client: the key will only see its orders, consignees and profile.

**Client API — “An integration can have up to 10 active keys; revoke one.” (409).** Revoke the ones no longer in use.

**Webhooks — I am not receiving notices.** Check that the webhook is **Active** (not suspended or turned off), that the integration is turned on, that the event is checked and that the order or invoice belongs to the integration's client. Whatever could not be delivered is in Integrations → **Outbox**.

**Webhooks — “The URL must use https.” / “The URL points to an internal or local address, which is not allowed.” (400).** The client's URL must be https and public; localhost, intranet names and private addresses are not accepted.

**Webhooks — the webhook ended up “Suspended”.** It had 5 consecutive undelivered sends. Fix the cause (the client's URL), try with **Test**, **Reactivate** it and then **Retry** the sends in the Outbox tab.

**Webhooks — “The webhook is suspended: reactivate it before retrying.” (409).** Reactivate the webhook (Integrations → Webhooks → Reactivate) and try again.

**Webhooks — how do I verify the signature?** `X-Teikem-Signature` = `sha256=` + HMAC-SHA256 (hex) of “timestamp.body” with the `whsec_…` secret; use the raw body and reject timestamps older than 5 minutes. See manual 39 §9.

**Webhooks — where do I see the secret?** Only when creating or rotating the webhook. If it was lost, **Rotate secret** (the previous one stops working).

**Webhooks — “An integration can have up to 10 webhooks; remove one.” (409).** Remove the ones you no longer use (they are deactivated; the history remains).

**Webhooks — “Webhooks need an integration with a client…” / “The integration does not have the “webhooks:manage” scope.” (400).** Edit the API integration: it must have a client and the webhooks scope.

**File format — “The file is JSON, but the integration does not have the path mapping…” (inbox).** The integration received a .json or .xml and does not have the mapping. Edit it: File format → JSON or XML, path of the orders and columns; test with a sample file.

**File format — “The orders path “…” did not find any record in the file.”** The path does not match the file. In XML start with the name of the root element (`$.Pedidos.Orden[*]`); an attribute is `@attribute`. Use “Test with a file”.

**File format — “The path “…” is not valid.” (400).** Use `$.campo.otro[*]`: root `$`, `@` or `^`, then `.name`, `[0]` or `[*]`.

**File format — “The workbook does not have the sheet 'X'.” / “The file is not an Excel workbook (.xlsx).”** Type the exact name of the sheet (or leave it empty for the first one) and save the file as .xlsx (the old .xls is not supported).

**File format — the folder, the email or the SFTP does not pick up my .xlsx/.json/.xml.** The folder picks up the extensions of the format (Automatic: csv, txt, xlsx, json, xml). For email, SFTP and FTP add the type to the connector's list of file types.

**File format — “The XML file has a DTD declaration, which is not allowed.”** For security the XML cannot have a DTD or external entities; ask the client for the file without them.

**Output — nothing goes out.** Check that the output integration is turned on, that the status is checked, that the order belongs to the integration's client and that it reached the status **after** the integration was created. “Preview” and “Send now” help with testing.

**Output — send “Undelivered”.** The destination failed in the 6 attempts (1 min, 5, 30, 2 h, 12 h). See the reason in Outbox → “File and API outputs”, fix the destination (URL, credentials, folder, fingerprint) and **Retry**, or **Discard**.

**Output — “The column name “X” must start with a letter or underscore…” (400).** In JSON and XML the column name is the field name: use letters, numbers, hyphen and underscore (no spaces).

**Output — “An output integration only accepts the shipment output scope.” (400).** Output integrations use only “Shipment output by file or API”.

**Output — “Scheduled output needs an interval (minutes).” (400).** Enter every how many minutes the send goes out (1 to 10080).

**Output — “This integration is an output one: it does not receive files.” (400).** “Upload file” is for input integrations.

**Output — the client's API receives the same send twice.** It is a retry (its response did not arrive in time): each call carries `Idempotency-Key: teikem-output-{id}`; ignore the repeated ones.

## Branding: colors and buttons (Batch 138)

**What colors does the application come with by default?** Those of Advance Logistics: navy blue for backgrounds and text, orange on the main buttons, links and navigation, and gray on badges, counters and amounts. Each company can choose another of the 14 themes or its own colors in System → Tenant settings → Branding.

**Which is the primary button and which the secondary?** The primary (Save, Create, Confirm) is **orange**. The secondary (Cancel, Close and the supporting actions) is **navy blue** with white letters; in the dark theme it is a lighter blue with a border so it stands out against the background. The danger (red) and the discreet ones do not change.

**Why are the letters of the orange button dark and not white like on the Advance page?** Because white on that orange gives 2.9:1 contrast and is not readable (the system minimum is 4.5:1). The application picks the text color of each button by itself: white if it reads well and, if not, dark navy blue.

**How do I go back to the previous blue and orange?** Settings → Branding → “Teikem (blue and orange)” theme → Save changes. Companies that had already saved their branding did not change.

**“The contrast of the action color in dark mode is X:1; the minimum is 4.5:1.” (400).** The action color (buttons and links) you typed is not readable on the theme's background. Choose a lighter one for dark mode and a darker one for light mode; the screen validates it before saving.

**What is the action color?** It is optional: if you do not set it, buttons and links use the primary color. A separate one is chosen only if the company wants, for example, bars in one color and buttons in another.

## Required or suggested rotation (Batch 139, 2026-10-20)

**What is the difference between “Suggest” and “Enforce” in the rotation?**
With *suggest* the system proposes where the goods should come from (according to FEFO or FIFO) and another bin or lot can be chosen; the screen warns that the rotation is being skipped. With *enforce* the system picks alone and splits the quantity; there is nothing to choose. It is changed in Tenant settings → Operations → “Inventory rotation” → “How it is applied on issues”. All existing companies are on *suggest*. It does not affect transfers or adjustments.

**What does “Rotation is enforced: {SKU} must come from {bins}. Do not choose a bin or lot; the system assigns them.” (409) mean?**
The company has rotation on *enforce* and the line asked for a bin or lot other than the ones the rotation assigns. Remove the bin and lot from the line (or use the ones the message indicates) and send again. On the web and on the device this should not happen: they no longer offer a choice; if it happens on the device, sync so the new configuration comes down.

**What does “The rotation application must be SUGGEST or ENFORCE.” (400) mean?**
You sent a value other than `SUGGEST` or `ENFORCE` in `rotationEnforcement` of the tenant settings.

**In the warehouse app I get “This skips the rotation”. What do I do?**
You scanned a bin other than the one it should come from according to the rotation. Tap **Cancel** to scan the right one or **Continue** if you really are going to take it from there (the movement is recorded as is). If the company enforces rotation, this question does not appear: the app tells you where it comes from and you tap “Take it this way”.

## Lot and expiration on the receipt line (Batch 140, 2026-10-20)

**I chose a lot-tracked product on the receipt and it will not let me save the line. Why?**
A lot-tracked product needs the **lot number** (expiration and manufacturing are optional). When you choose the product, the line shows the “Lot”, “Manufactured” and “Expiry” fields below the product: type the lot, enter the received quantity and leave the field; then it is saved. With a serial-tracked product, the “Serial numbers, one per line” box replaces the quantity: the received quantity is however many serials you type.

**Where do I enter the lot of a line I already saved or that comes from an arrival notice?**
With the “Lot and serials” icon on the row (it only appears for lot- or serial-tracked products). The fields inside the line are only for new lines of a receipt without a document.

## Inventory alerts (Batch 141, 2026-10-20)

**What inventory alerts are there and how do I receive one?**
Five: lots about to expire, expired lots with stock, products below minimum, no stock and no movement. A job checks them every day at 6:00. No rule comes pre-built: the administrator creates the rule in System → Notification rules, picks the “Inventory…” event and whom it goes to (role, user, email) and through which channel.

**I set up the rule but nothing arrives. Why?**
Check that (1) there is something to report: lots with stock that expire within the window, products below their minimum, etc.; (2) the “Inventory alerts” job is active in System → Scheduled jobs (you can use “Run now”); (3) the rule is active and has a recipient and a channel; and (4) it is not the same set already reported this week (it does not repeat until the set changes or the week passes).

**How do I change how many days in advance it warns about lots about to expire?**
Tenant settings → Operations → “Inventory alerts” → “Warn about lots expiring within (days)” (1 to 365; 30 by default). The same for the days without movement (1 to 3650; 90).

**What does “The expiry warning days must be between 1 and 365.” (400) mean? And “The days without movement must be between 1 and 3650.”?**
You typed a number outside that range (or not a whole number) in the corresponding setting. Fix it and save.

**A new product without stock does not warn me about “no stock”. Is that a bug?**
No: “no stock” only warns about products that did have movements and dropped to zero. A newly created one that was never received does not count.

## Rotation with and without lot (Batch 142, 2026-10-20)

**Why are there now two rotation methods in the settings?**
Because FEFO (expires first) can only be applied to products with lot and expiration. Lot-tracked products use “lot-tracked products” (FEFO or FIFO). Products without lot and serial-tracked ones do not expire, and use “products without lot or serials” (FIFO or by bin). It is in Tenant settings → Operations → “Inventory rotation”.

**My products without lot now come out differently. What happened?**
Before, without expiration, FEFO broke ties by the bin code. Now the “without lot” method is FIFO by default: what came in first goes out first. If you prefer the previous behavior, change that field to “By bin”.

**What does “The rotation method for products without lot must be FIFO or POSITION (by bin).” (400) mean?**
You sent FEFO (or any other value) in `rotationMethodNoLot`. Without lot there is no expiration, so FEFO does not exist there. Use FIFO or POSITION.

**What do “A lot-tracked product only allows FEFO or FIFO rotation.” and “A product without lot or serials only allows FIFO or POSITION (by bin) rotation.” (400) mean?**
A product's rotation exception must be one of those that apply to its type. With lot: FEFO or FIFO. Without lot or serials: FIFO or by bin. On the record, the “Rotation” selector now only shows the ones that apply.

**I changed a product from without lot to lot-tracked and it had “by bin” rotation. What happens?**
The exception stops applying and the product follows the company method for lot-tracked products. On the record, when changing the type, the exception that does not apply goes back to “Same as the company” by itself.

## Exit notices (Batch 143, 2026-10-20)

**What is an exit notice and how is it different from a sales order?**
It is the notice from the owner of the goods (a warehouse client) of what they want the warehouse to take out. It has no price, tax or invoice: it is only the physical movement. The sales order is for selling (price, tax, credit, invoice). A third-party warehouse normally only uses notices; a company that buys and sells its own inventory uses purchase and sales orders.

**I requested more than there is and it did not give an error. Is that normal?**
Yes. The exit notice is never rejected for lack of stock: it reserves what there is and leaves the rest as pending. When goods come in, the “Reserve the backorder of exit notices” job (every 10 minutes) reserves it, oldest notice first; you can also use “Reserve” on the line.

**What does “Product {SKU} does not belong to the exit notice's client.” (400) mean?**
The product on that line belongs to another owner (another client) or is the company's own. In an exit notice only products of the owning client are requested. Check the notice's client or the product's owner on its record.

**What does “You cannot dispatch more than what is reserved of {SKU} ({n}). Reserve the backorder first.” (409) mean?**
Only what the notice has reserved is dispatched. If the quantity you want to take out is larger, first reserve the backorder (if goods have already come in) or dispatch only what is reserved.

**What does “Only a requested exit notice that has not been dispatched can be cancelled.” (422) mean?**
A notice that already had something dispatched is not cancelled. Close what is missing with a shortage (the lines with pending quantity) or, if the dispatch was a mistake, delete the pick batch: the notice reopens and then it can be cancelled.

**Why “Rotation is enforced…” (409) when dispatching?**
The company has rotation on “Enforce”: the dispatch can only come from what the notice has reserved (which already follows the rotation). Dispatch without choosing a bin or lot. See chapter 6 §2.3.

**Can the dispatch of an exit notice be packed?**
No: it is an inventory issue without delivery (`Dispatch {number} belongs to an exit notice and is not packed…`, 422). To deliver with a transport order, the sales order flow or the normal picking flow is used.

## Dispatch requires a document (Batch 145)

**“The company requires dispatching against an exit notice / a sales order / a document…” (409).** The company turned on “Dispatch requires a document”. Create the exit notice (or the sales order) and dispatch from its record with **Dispatch**. An administrator can go back to “Free” in Settings → Operations.

## Cost layer (Batch 147)

**“The unit price must be zero or greater and have at most 4 decimals.” / “The unit price exceeds the maximum allowed.” (400, `lines[i].unitPrice`).** Fix the price of that arrival notice line (0 or more, up to 4 decimals) or leave it empty.

**Why does the receipt show a cost different from the product's?** The receipt froze the cost of the purchase order or the price of the notice, which prevail over the product cost. A later change in the catalog does not modify it.

## Valuation (Batch 148)

**“The valuation method must be AVERAGE (average cost) or LAYERS (by layers).” (400, `valuationMethod`).** Choose one of the two options in the list in Settings → Operations.

**I do not see the “Inventory value” tab.** It requires the `inventory.value` permission (Administrator and Billing come with it); ask your administrator for it.

**Why are there “Units without cost”?** Those units have no cost frozen in a receipt and no purchase cost on the product. Capture the product cost; the report updates by itself.

## Receiving requires a document (Batch 149)

**“The company requires receiving against a document / an arrival notice / a purchase order…” (409).** Choose an arrival notice or a purchase order of the type the company requires; an administrator can go back to “Free” in Settings → Operations → Inbound.

**“The company requires receiving against an arrival notice, not a purchase order.” / “…against a purchase order, not an arrival notice.” (409).** The chosen document is not of the required type; use the right one or change the requirement.

<!-- kemi:portal -->
## Warehouse client portal (Batch 153)

**“Your access does not include viewing the inventory value.” (403).** Your portal access is read-only; the value is seen by the client's administrator and operator accesses. Ask your business's administrator for it.

**I do not see Inventory in the portal.** The company of this access does not have the warehouse module; if you have another access in a company with a warehouse, switch to it with “Switch access”.

<!-- kemi:staff -->
**What does "Updating the product price requires the manage inventory permission." (403) mean?**
You checked “Update the product price with this one” on an arrival notice without having `inventory.manage`. Uncheck the box (the notice is saved with its price and the product keeps its own) or ask your administrator for the permission.

<!-- kemi:staff -->
**The arrival notice shows me “The product has X on file; this price is different.” Is it an error?**
No, it is informational only. The notice's price is used for that receiving; the product cost changes only if the product had no cost or if you check “Update the product price with this one”.

**What does “Your company does not allow you to create products / arrival notices / exit notices from the portal.” (403) mean?**
The company did not turn on that option for your client. The administrator turns it on in Clients → client profile → “What the client creates from its portal”.

**What does “Your access is read-only and cannot create documents.” (403) mean?**
Your portal access is read-only. Ask your client's administrator for an administrator or operator access.

**What are the lot and expiration for on the arrival notice line?**
They only apply to lot-tracked products: the client announces which lot is coming and when it expires, and on receiving, the lot is born with that date. On a product without lot it does not appear.

**What does “The expiration is only entered together with the lot number.” (400) mean?**
An expiration was written without a lot number. Enter the lot or remove the date.

**What does “The arrival notice has an open receipt; delete it before editing it.” (409) mean?**
Receiving against that notice has already started. Delete the open receipt to be able to edit the notice.

**What does “An arrival notice born from a purchase order is changed in the purchase order.” (409) mean?**
That notice was generated by a purchase order; edit the order, not the notice.

<!-- kemi:staff -->
**When saving an exit notice I get “Internal error.” (500)**
It was a defect of the previous version (the SAL-##### counter was not recognized by the API) and it is already fixed. If you see it, update the API and try again; if it persists, tell support with the error's correlation code.

<!-- kemi:staff -->
**How are phone numbers stored and what is the prefix for? (Batch 162)**
Every phone number is stored **with digits only** and with its **international prefix** separately (`1` for Puerto Rico and the United States, `593` for Ecuador…). In every phone form (contacts, client people, consignees, suppliers) there is a **Country code** field to the left of the number, which starts with the company's. The format in which the number is displayed is defined by each company in **Settings → Region & formats** (`Country code` and `Phone mask`); a number with another prefix is shown as “+593 991234567”.

<!-- kemi:staff -->
**What does “Invalid country code: use 1 to 4 digits, for example 1.” (400) mean?**
The prefix must be 1 to 4 digits and not start with 0 (it is accepted with “+”: `+593`). Fix it in the Country code field.

<!-- kemi:staff -->
**What does “The phone number does not start with the given country code.” (400) mean?**
The number was written with “+” and a prefix (for example `+593 …`) but the Country code field has another one (for example `+1`). Leave only one or make them match.

<!-- kemi:staff -->
**What does “A phone number with “+” needs the country code in its field.” (400) mean?**
The number starts with “+” but the country could not be recognized. Type the prefix in its field and the number without the “+”.

<!-- kemi:staff -->
**After the change, an old phone number is shown without format or without prefix. What do I do?**
The ones that had text (for example “787-555-1234 ext. 5”) or a “+” of a country other than the company's were not touched when cleaning the data. Open them with **Edit**, enter the correct number and prefix and save.

<!-- kemi:staff -->
**Why do claims now start with RCL- and not REC-? (Batch 164)**
The receipt already used `REC-#####` and a `REC-00012` could be a receipt or a claim. Since Batch 164 claims are `RCL-#####` (existing ones were renamed automatically); the receipt keeps `REC-#####`.

<!-- kemi:staff -->
**What can I search for in “Search or run”? (Batch 167)**
Besides the menu screens, it searches records by number or name: shipments (number, waybill, packing, client invoice, package), routes, claims (`RCL-`), clients, warehouses, zones, bins, products (SKU and name), receipts, pick batches and packings, exit notices, suppliers, purchase and sales orders, invoices, rentals and phone numbers. A single exact result opens by itself; if there are several, they are shown to choose from. Only the types your user can see appear. See chapter 42.

<!-- kemi:staff -->
**Where do I see the privacy policy and the terms of use while inside the application?**
At the footer of the side menu, in small print: **Privacy** and **Terms of use** (they open the document in a new tab). With the menu collapsed the footer is not visible: expand it. On the phone, open the menu.

**Where do I see the privacy policy and the terms of use in the client portal?**
At the end of **Settings**, in small print: tap **Privacy** or **Terms of use**. The document opens for reading (without an accept button) and **Back** returns to Settings.

## Fleet alerts (Batch 168, 2026-10-21)

### What does the fleet warn about and who receives it?
Vehicle and driver documents about to expire or expired, and preventive maintenance coming up or overdue. **No rule comes pre-built**: in System → Notification rules one is created per event (“Fleet documents about to expire”, “…expired”, “Maintenance coming up”, “Maintenance overdue”) with its recipients and channel. Each user can mute them from the bell.

### I did not receive the alert for a document that expires
Check that (1) the document has an **expiration date** and is the **current** one of its type (if one that expires later has already been uploaded, the old one does not warn); (2) the vehicle or driver is **active** and not deactivated; (3) it falls within the days in advance (Tenant settings → Operations → “Fleet alerts”, 30 by default); (4) the “Fleet alerts” job is active (System → Scheduled jobs, “Run now”); (5) the rule has a recipient and a channel; and (6) it is not the same set already reported this week.

### Change how many days in advance it warns
Tenant settings → Operations → “Fleet alerts” → “Warn about documents expiring within (days)” (1 to 365). If you enter another value: `The fleet document warning days must be between 1 and 365.` (400). It applies to all fleet documents; maintenance uses its own criterion (“Due soon” = 10% of the interval remains).

## Who sees the “Default” indicators and charts (Batch 169, 2026-10-21)

**Can I hide a “Default” indicator from certain users?**
Yes. In Indicators or Charts, card of the “Default” one → **“Who sees it”** → Private, Whole tenant or Shared (people and roles). `analytics.manage` is required. Whoever administers Analytics always keeps seeing it so they can restore it.

**A user says they no longer see an indicator or chart they used to see.**
Someone probably restricted it with “Who sees it”. Sign in with a user with `analytics.manage`, open the card and check: if it is on Shared, that their person or their role is in the list; or set it back to “Whole tenant”. If the visibility is fine, check that they can read the data source and that the module is turned on.

**Can I change the name or the filters of a “Default” one?**
No (`The platform's default items cannot be edited or deleted.`, 403): they are the official metrics. Create your own with the name and filters you need.

**`Visibility must be PRIVATE, TENANT or SHARED.` (400)**
The value sent to `…/visibility` is not one of the three. Fix it and save again.

**`The waybill only allows letters, numbers, hyphen, underscore, period and slash, up to 40 characters.` (400)**
The waybill the client sent has spaces, symbols or other characters, or exceeds 40. Fix it and create the shipment again; if no waybill is sent, Teikem generates it by itself.

**`An active shipment with that waybill already exists in the company.` (409)**
Another active shipment already has that number (a deactivated one does not count). The client must use another number; Teikem does not assign a different one when the client brings its own.

**`Maximum 4000 characters.` (400) in a package description**
The line description exceeded the maximum (previously 250). Shorten the text: the long content of a shipment goes one item per line.

## Arrival notice number (Batch 170, 2026-10-21)

**What is LLE-00012 and can I change it?**
It is the arrival notice number, per company. The system assigns it when the notice is created and it cannot be typed or changed. It is used to type or scan it in Receive (warehouse app), search for it in the Arrival notices tab (“Reference or number”) and see it in notification notices.

**My old notices appeared as #12. What happened to them?**
When the database update was applied they were numbered in order of creation (the oldest is `LLE-00001`). If the notice has a client reference, it is still shown next to the number.

**The warehouse app does not show the notice numbers.**
Open the app with signal and let it sync: it downloads the notices once complete to bring the number. If it still does not appear, check that the app is on the latest version.

## Assign arrival and exit notices (Batch 171, 2026-10-21)

**What is assigning a notice for?**
To tell a person “you receive this one” (arrival notice) or “you fulfill this one” (exit notice). It will appear in “My tasks” of the warehouse app for them. It does not block anyone: another person with permission can receive or dispatch it just the same.

**Who can assign and who can take?**
Whoever has `warehouse.manage` (supervision) assigns. Whoever can receive (`warehouse.receive`) or pick (`warehouse.pick`) takes an unassigned notice. If it is already assigned to another person: `That document is already assigned to another person; ask a supervisor to reassign it.` (409).

**`Only an arrival notice that is still expected can be assigned.` / `Only an open exit notice (requested or partial) can be assigned.` (422)**
The notice was already received, dispatched or cancelled. There is no need to assign it.

**The people selector is empty or says I have no access.**
Listing people requires the manage users permission (`admin.users`). Ask an administrator to assign it, or to give you that permission.

**`That date is not understood: use MM/DD/YYYY.` (the format changes by company)**
The date you pasted in the field was not understood and was rejected (the field stays as it was). Type it with numbers only (the field adds the slashes itself) or paste it as `2026-10-21` or in the company's order. Dates with a 2-digit year, texts (“tomorrow”, “Oct 21, 2026”) and days that do not exist are not accepted.

**The date field turns red**
The date does not exist in the calendar (for example 02/30/2026) or falls outside the limits that screen requires (for example “cannot be earlier than today”). Fix it or pick it in the calendar, where the disallowed days appear dimmed.

**How do I type the time?**
Numbers only: `0930` → `09:30`. With 12-hour time (Puerto Rico) choose **AM** or **PM** in the selector next to it; with 24-hour time there is no selector. A time out of range (25:00, 09:75) is marked in red.

## My tasks in the warehouse app (Batch 172, 2026-10-21)

**Nothing appears in “My tasks”.**
Nobody has assigned you documents, or the device has not synced since they were assigned (tap “Sync now”). Also check that you are in the right warehouse (the one at the top on Home). Exit notices only appear if they already have something reserved.

**A notice assigned to another person, can I receive or dispatch it anyway?**
Yes: the assignment organizes the work, it does not block it. It appears in Receive or Dispatch like any other.

**“Take” says it could not.**
It needs signal. If it says `That document is already assigned to another person; ask a supervisor to reassign it.` (409), someone else took it first; a supervisor can reassign it from the web (Assign button).

**Without signal I only see the putaways from last time.**
That is expected: assigned putaways are requested online and saved; arrival and exit notices come from the device's copy.

**`The shipment date can't be earlier than today.` (400)**
A shipment is not created with a date earlier than today (yesterday is no longer allowed): it warns and blocks, in quick entry, in detailed entry and in import. Change the date to today or a later day; the calendar no longer lets you pick past days. If the screen was left open from one day to the next, the date it had became old: change it.

**`Enter the shipment date.`**
The quick entry row was left without a date. Type one or pick it in the calendar (by default it is today).

**Why is a quick entry row shaded?**
Because its date is different from today (for example, tomorrow). It is only a reminder so it does not go unnoticed; change the date to today to remove it. The new row always comes with the date of the last saved order.

**How do I make a client appear with its service and package in quick entry?**
In Clients → the client record → **Profile** choose “Default service” and “Default package” and save. If you leave them empty, the company's are used.

## Picking and packing by document (Batch 173, 2026-10-21)

**Where did the previous list of pick batches go?**
In the same place, with the **“By movement”** selector above the list. By default **“By document”** is now shown: one row per exit notice, sales order or packed shipment, with its progress.

**What does “Picked 5 of 10 · To collect 4 · Not reserved 1” mean?**
Of the 10 ordered, 5 have already gone out; there are 4 reserved that still need to be picked; and 1 is neither picked nor reserved (waiting for inventory).

**I cannot find a manual issue in “By document”.**
A manual issue does not belong to any document. It is in **“By movement”** (Type → Manual issues).

**A pick batch I deleted no longer appears in the document.**
That is expected: when deleted it is reverted and stops counting toward the progress. In “By movement” it is seen by checking “Include deleted”.

**A shipment's document has no “ordered”.**
The shipment is born when packing what was picked; it only shows how much was picked. The ordered quantity is in the exit notice or the sales order it came from.

**`Unknown document type: “X”. Use EXIT_NOTICE, SALES_ORDER or ORDER.` (400)**
The `kind` value in `GET /pick-batches/documents` is not one of the three. Fix it or remove it.

**What is “In warehouse” in “Packages on the street” and why is it no longer the stock?**
Since 2026-10-21 it counts the shipments that **entered** the warehouse that day (by pickup or by the routing scan in the warehouse), each shipment **only once**. It is no longer how many are inside right now. Its 7-day bar shows each day's entries.

**What happened to “To dispatch” in the Pulse?**
It was removed from “Packages on the street”: the cards are Inbound, In warehouse, Routes, Delivered and On route (with the shipments completed today below). Assigned shipments without a driver are still reported in “Needs your attention” and in the Radar.

**What are the “products on backorder” in the “Products below minimum” card?**
The products that have a pending shortage on some open sales order line. It is the text at the bottom of the card; the big number is still the products below their minimum.
## Receipt with batches (Batch 174)

**What is “Confirm batch” for?**
When the truck arrives in parts: what has been received so far enters inventory and putaway, and the receipt stays open for what is missing. “Confirm receipt” closes the rest.

**`There are no new units to confirm: capture what was received on at least one line before confirming a batch.` (422)**
There are no unconfirmed received units that can go into a batch. Enter what was received on a line (note: serials, lines with damage or with cross-dock wait for the final confirmation).

**`A direct receipt does not allow batches: each line already enters its destination bin on confirmation.` (422)**
Direct-to-bin receipts are confirmed complete. Use a receipt with putaway if you need batches.

**`The received quantity cannot be left below what was already confirmed in batches.` (400)**
Those units are already in inventory. If it was a mistake, fix it with an inventory adjustment, not on the receipt.

**`The line already has units confirmed in batches: its product and lot cannot be changed, nor can it be removed from the receipt.` (409)**
What was confirmed already moved inventory and generated its putaway; the line is fixed.

**`The receipt already has confirmed batches: it cannot be deleted.` (409)**
It already moved inventory. Close it with the final confirmation.

**The batch did not include a line I did capture.**
Lines with serial number, with declared damage or with cross-dock enter in the final confirmation, not in the batches.

**Is the purchase order received with the first batch?**
No: it stays “Partially received” and changes to “Received” when what was received completes what was ordered.

## Receive a part from the device (Batch 175)

**When do I use “Receive a part” and when “Confirm receipt”?**
“Receive a part” when only part of the truck arrived (or staging filled up): what was captured enters and can be put away, and the receipt stays open. “Confirm receipt” when nothing is missing anymore: it closes the document's open receipt with whatever is captured in that last round.

**“Receive a part” does not appear.**
It only appears when receiving against an arrival notice or a purchase order, in a warehouse with putaway (not direct), with at least one line captured. If a line has serials or damage, the part cannot be done (those lines are received on confirmation).

**`Enter the arrival notice or the purchase order (only one): a part is received against a document.` (400)**
The send brought neither or both. Receive the part by scanning the notice or the order.

**`Capture one serial for each unit received before confirming the part.` (400)** (Batch 187)
A part of a serial-tracked product carries one serial per unit. Scan or capture the missing serials and send the part again. (Before, serials and damage were not allowed in parts; they are now.)

**`The serials of {sku} already posted by batches cannot be changed or removed: only new serials can be added.` (400) / `The damage cannot be left below what was already reported in previous batches.` (400)** (Batch 187)
What a batch already posted (serials or reported damage) is not undone. To correct it, make an inventory adjustment or a separate damage report; new serials and damage are added in the next part.

**Two devices received parts of the same notice, is the receipt duplicated?**
No: both parts are added to the same open receipt of the notice, each as its own batch.

**The signal dropped and I do not know whether the part was sent.**
Check Sync: if it already appears as sent, it is on the server. Resending it does not duplicate it (the part carries its key).

**It warns me that it does not fit in staging.**
It is only a warning with the configured capacity of the receiving bin: receive a part and put away (Putaway) to free up space.

**How do I change what the invoice label says?**
System → Printers & labels → “Build the invoice label” (`printers.manage` permission). By default it is the barcode of the invoice number, centered and large, with the number below; there you add, move or shrink whatever you want. “Make default” picks which one is printed. Invoice templates are separate from the package ones.

**“The template type cannot be changed; create another one.”**
A template is born as a package or invoice one and does not change. Create a new one in the builder of the class you need (or duplicate and edit).

**“The template type must be PIECE (piece label) or INVOICE (invoice label).”**
Only in direct API calls: `kind` only accepts `PIECE` or `INVOICE`.

## Master pick batch (Batch 176)

**What is the master pick batch for?**
To pick several exit notices or sales orders of the same client together and send them in a single shipment, and to see all the movements in one place (which bins everything came from).

**Can I add another document to a master pick batch already made?**
No: it is decided when picking. If another document arrives, create another pick batch.

**`The documents must belong to the same client…` (400)**
A master pick batch is for a single client and a single destination. Make one pick batch per client.

**`“X” has nothing reserved to pick.` (422)**
That document has no reserved inventory (all pending or already dispatched). Remove it from the selection or reserve first.

**`Pick batch X belongs to master pick batch Y…` (422)**
Pack it or delete it from master pick batch Y; the shipment is a single one for all of them.

**I deleted the master pick batch, what happened to the documents?**
The inventory went back to its bin and each document recovered its reservation and its previous status.

**Where do I create a client's API key and what do I send them?**
Integrations → in the “Client API” type integration, “API keys” button (below the name) → give it a name → “Create key”. A block appears with the base URL, the key, the secret, the scopes and how to request the token; “Copy all” and paste it to the client. The secret is only visible at that moment: if it is lost, revoke the key and create another.

## Collect together from the device (Batch 178)

**“Collect together” does not appear.**
It is only offered in Dispatch exit notice when there are 2 or more notices to dispatch in the warehouse.

**A notice looks dimmed and says “It belongs to another client”.**
A master pick batch is for a single client and a single destination. Pick the ones of one client first and then make another with the other's.

**“You need to have picked something from at least two notices.”**
When finishing, the route must include something from two or more notices. Go back and complete the route, or dispatch each notice separately.

**Where is the master pick batch packed?**
On the web, Picking & packing → Master → Pack: it creates a single shipment for everything picked.

**I sent the master pick batch without signal, what happens if the server rejects it?**
When syncing, the server's message is shown, the local balance is undone and the notice is downloaded again as it was.

**Where do I see and edit the import templates without going into Import?**
System → Integrations → “Templates” button. There is a tab for Shipments and another for Consignees; there you create, edit, deactivate and reactivate them (you need `orders.edit` for shipments and `locations.create` for consignees). Inactive ones are also shown and can be reactivated.

**My template does not appear in the Import wizard.**
Check in Integrations → Templates that it is **Active** and that it is for “All clients” or for the client you chose.

**How do I see which dispatch came out of a sales order, and the other way around?**
In the sales order, the “Order dispatches” table has each dispatch's number as a link; in the dispatch record the “Sales order” field leads back to the sale. You need `pickbatches.view` to open the dispatch and `sales.view` to open the sale.

## Fleet notices: work orders, fuel and trips (Batch 179)

**Why didn't it warn me about an open work order?**
It only warns if the order is in OPEN or IN_PROGRESS and was created **more than 14 days** ago (exactly 14 does not count yet), and if the set changed since the last notice of the week.

**Why doesn't “fuel out of range” warn about a vehicle with few fill-ups?**
It needs at least 3 previous fill-ups with mileage (with odometer) to compute an average; it evaluates only the last fill-up and only if it is from the last 30 days. It counts as out of range when it differs from the average by more than 30% (for better or for worse).

**A vehicle came out as “no trip” but we used it.**
It counts the last **non-cancelled** trip with that vehicle (the plan date). If it is used without a recorded trip (for example, only special deliveries) it will appear as having no trip. A new vehicle or driver counts from its creation, so there is no notice during the first 14 days.

**Where do I change the 14 days or the 30%?**
They cannot be changed yet (they are fixed); you can mute the notices from the bell or not set up their rule in System → Notification rules.

**Can I correct a product I already created from the portal?**
Yes, if your company allows you to create products: on the “Products” tab tap “Edit” on the product and change its name, barcode, brand or cost. The SKU, lot or serial control and the unit cannot be changed from the portal; if you need to change them, ask the company. If you leave the cost empty, the current one is kept.

**When editing a product I get “There are no changes to save.”**
The edit was sent with no data. Change at least one field and save again.

## Purchase accounting and dispatch accounting (Batch 181)

**What gets posted and what doesn't?**
Only what is the company's **own** (products with no owning client), no matter whether it arrived by purchase order, arrival notice or blind receipt. A client's goods are not posted. Purchases: lines of **confirmed** receipts (not the returns of a sale). Dispatches: lines of current pick batches and dispatches (not the deleted or reverted ones).

**Which cost does each line come out with?**
The one the line already had **frozen**: the receipt's cost layer (purchase order; otherwise the notice price; otherwise the product cost) or the issue's cost. Changing the product cost later does not change a run or a layer.

**“Nothing to account for in the period.”**
There are no free own lines between those dates: either they are already in another run (generated, accepted or exported), or there were no own movements, or the receipt has not been confirmed yet. Change the period or cancel the run that holds them.

**“Enter the period (from and to).” / “The period is not valid: the end date cannot be earlier than the start date.”**
A date is missing or “to” is earlier than “from”. Fix them and generate again.

**“Only a generated run can be accepted.” / “Only a generated or accepted run can be cancelled.” / “Only an accepted or exported run can be exported.”**
Each step has its status: generate → accept → export. An exported run can no longer be cancelled or accepted; a generated run is accepted first. Reload the record: someone else may have changed it.

**“Run not found.”**
The run does not exist or belongs to the other type: a purchase run does not open from Dispatch accounting, nor the other way around.

**“The run changed while saving. Review and try again.”**
Someone else changed it at the same time. Open the record again and repeat the action.

**There are lines “without cost” in the run.**
The receipt's layer (or the issue) had no cost when it was frozen. They are posted anyway, with the cost blank, so that accounting completes them in its system. Setting the cost on the product later does not change what is already frozen.

**I deleted a pick batch that was already in a run.**
The run does not correct itself. If it is not exported, cancel it and generate it again; if it was already exported, the adjustment is made in the accounting system.

**Where do I change the file columns?**
In the run's “Export” window, with **Configure…**: CSV or TXT format, delimiter, date format, header, columns and their order. Each screen (purchases and dispatches) has its own templates.

## Master receiving (Batches 182-183)

**What is “Receive together” for?**
For an arrival (truck, railcar, container…) that brings several documents (arrival notices and/or purchase orders) of the same owner and warehouse: one receipt per document is opened under a master receipt (RCM-#####), with the same carrier and reference, and they are confirmed together. Each document keeps its usual receipt.

**“The documents must belong to the same owner: … Receive documents of different owners separately.”**
A master receipt belongs to a single owner (one client, or the company's own goods). Make one master receipt per owner.

**“The documents must belong to the same warehouse: …”**
A master receipt belongs to a single warehouse. Make one per warehouse.

**“Choose at least two documents to receive together; for a single one use the normal receipt.” / “A master receipt allows at most 20 documents.” / “The same document is repeated.”**
Choose 2 to 20 different documents. Each one carries an arrival notice or a purchase order, only one.

**Can I add a document to a master receipt that is already open?**
No: it is built when the receipts are opened (just like the master pick batch). If one is missing, receive that document separately.

**“All the receipts of this master receipt are already confirmed.”**
There is nothing left to confirm. Each receipt follows its own path (putaway) as always.

**“The master receipt cannot be deleted: one of its receipts is already confirmed.”**
Only a master receipt with no confirmed receipts can be deleted. Confirmed receipts are already in inventory.

**When I pressed “Confirm all” an error came up for one of the receipts.**
It is all or nothing: if one receipt cannot be confirmed (for example, a lot or a serial still to be captured), none is confirmed. Fix it from “Open receipt” and try again.

**A notice does not appear in “Receive together”.**
Only notices that are expected and have no receipt are offered: one already received, cancelled or with an open receipt (standalone or from another master) does not show up.

**How do I load many products at once?**
Products → Import. Choose (or create) a template that says which column is each field (at least sku and name), choose the CSV or Excel file and press “Review”: it saves nothing and tells you per row whether it is fine or why it is rejected. When you are satisfied, “Import N” creates the valid ones.

**The product import rejected rows with “A product with that SKU already exists for that owner.”**
Import only creates: it does not update what already exists. Remove those rows from the file, or edit the product from its screen.

**“The template must include the sku column.” / “…the name column or a default value for name.”**
Every product template needs the sku column and the name (in a column or as a default value). The sku and the barcode cannot have a default value.

**“Owning client not found by that code.”**
The ownerClientCode column must carry the code of an active client (the one you see in Clients); leave it empty if the product is the company's own.

**How do I receive several documents together from the device?**
In Receive tap “Receive together (open load)”, receive one document after another as always and, when done, “Finish load”. The receipts are gathered by themselves into a master receipt. All the documents of the load must belong to the same owner.

**On the device I get “That document belongs to a different owner than the ones in this load”.**
The first document sets the owner of the load. Finish the truck to receive that document (or open it in another load).

**“This document belongs to a different owner than the ones in the master receipt …” / “The master receipt was already deleted; open a new one.”**
The device's submission was rejected: the load already had documents of another owner or the master was deleted on the web. Receive that document in a new truck.

**How do I load many suppliers at once?**
Purchases → Suppliers → Import. Same as with products: choose or create a template (at least the name column), choose the file, “Review” and “Import N”. If the name already exists among the active suppliers, that row is rejected; it is not updated.

**“The template must include the name column.” (suppliers)**
In a supplier template the name always goes in a column: it does not allow a default value, same as contact, phone and email.

**How do I load many warehouses or bins at once?**
Warehouses → Import (code and name in columns) or Locations → Import (warehouse and zone by code, and the bin code or its parts). You choose or create the template, the CSV or Excel file, “Review” and “Import N”. It only creates: what already exists is rejected.

**“Warehouse not found by that code.” / “Zone not found by that code in that warehouse.”**
In the bin import the warehouse and the zone are looked up by their code and must be active. Check the warehouseCode and zoneCode columns (or their default values in the template).

## Receipt in parts: progress in plain sight (Batch 186)

**How do I know how much of an arrival notice has been received so far?**
In Arrivals & departures → Arrival notices, a notice that already has received parts shows the **“Partially received”** tag next to its status and the **“Received”** column (`4 / 10`: what is already recorded out of what is expected). It is the same thing the client sees in their web portal. On the device, when receiving against that notice, **“Already received from this document”** appears with each product. “Partially received” disappears when the final receipt is confirmed (the notice becomes Received).

**Which master receipt does a receipt belong to?**
The receipts of a master receipt carry the **“Master RCM-…”** tag in the list and in their record. The **“Receive together”** button is in both Receiving and Arrivals & departures (it opens the same window); the masters created are seen in Receiving → Masters.

**Can I pick a sales order and an exit notice together from the device?** (Batch 186)
Yes, if they belong to the same client: in Dispatch exit notice, “Collect together” shows the notices and the sales orders that have something reserved. Choose two or more, walk the single route and submit; a single master pick batch is left. The shipment (packing) of the master is still created from the web.

**How do I send my products to another system (ERP) by SFTP or API?**
Integrations → New integration → Direction “Outbound” → “What is sent?” Products → you choose the product template (the same one used for importing), the destination (SFTP, FTP or API), the format and whether it goes out immediately or on a schedule. Whatever you create or change goes out by itself; for the first load use “Send the whole catalog” in the integration's row.

**I changed a product and it did not go out in the product integration.**
Check that the integration is on and was created before the change. With the “Scheduled” trigger it goes out when the time comes; with “Immediately”, in less than a minute. If it does not go out, look at the “Outbound” tab (retries and errors).

**Which columns does the product file carry?**
Those of the product template, in their order; the header is the field name. If one is missing, add it to the template (Integrations → Templates → Products).

## Company logo in PDFs (Batch 188)

**Which logo do reports and PDF exports carry?**
The one the company uploaded in Settings → Branding. Branded reports (inventory, adjustments, barcodes and the documents that are printed, such as those of Orders) carry it in the blue header band, in place of the Teikem symbol and name; PDF exports of tables carry it above the company name and the title. The lockup (symbol + name) is used; if only the square mark was uploaded, that one is used. In the blue band the “for dark background” (inverted) variant is preferred; if only the light-background one exists, the logo comes out on a white plate, and the other way around in tables.

**My PDF comes out without a logo (or with Teikem's).**
Upload the logo in Settings → Branding (lockup and/or square mark). If the company has not uploaded any, branded reports keep Teikem's and tables come out without a logo. If it was uploaded and does not appear, reload the page (logos are downloaded on sign-in) and export again. CSV and Excel files carry no logo.

**How do I set a warehouse's default receiving bin?** (Batch 189)
In Warehouses → the warehouse → “Receiving” section → “Default receiving bin” (a bin in a receiving or cross-dock zone). When creating a receipt with that warehouse, the receiving bin comes already filled with that value (it can be changed in the header). Without a value, the system uses the warehouse's first active receiving bin, as before. On the device, receipts with putaway use that same default bin.

**On the device, when receiving against a notice, do I have to scan every product?** (Batch 191)
Not necessarily: below the scan field, “What the document says” appears with each product, what is expected, what was received and what is missing. Tap the product to capture it; scanning keeps working. With a purchase order the list shows what is left to receive. In a blind receipt there is no document, so there is no list.

**How do I control which dispatches each person sees on the device?** (Batch 193)
With the permission **“Dispatch any document on the device (not only assigned ones)”** (`warehouse.pick.any`, Warehouse category), granted per role in System → Roles. With it, Dispatch, Exit notices and Sales orders show everything in the warehouse, and “My tasks” also offers what nobody has assigned. Without it, the person only sees the exit notices **assigned to them** (sales orders are not assigned, so they are not offered). The permission was granted to every role that could already pick, so as not to change anyone's work; remove it from the roles that should only see what is assigned.

**On the device, when dispatching against an order or a notice, do I have to follow the route order?** (Batch 192)
No: below the current product, “Products to take” appears with each product, its bin and the quantity. Tap one to take it now; when done, the route continues. The ones you skipped stay in the list with the “skipped” mark.

**When is only the pending part (backorder) of an exit notice reserved?** (Batch 194)
As soon as a receipt is confirmed (complete, a batch or a part from the device) the system immediately checks whether there is already inventory for the pending part of the exit notices and reserves it, oldest notice first. In addition, a scheduled job checks it every 10 minutes in case goods came in another way (adjustment, transfer, released quarantine, another notice cancelled). There is no button to force it.

**Where do I see what is still on backorder?** (Batch 194)
Three places: (1) in Arrivals & departures → Exit notices, the “With backorder” tag and the “Only with pending to collect” filter; (2) in the Day pulse, “Needs your attention” shows one row per open exit notice with a pending part (red if there is already inventory to reserve it —the automatic reservation did not happen— or if it is more than 7 days old; yellow if it is only waiting for goods), with permission `exitnotice.create`; (3) indicators and charts can use the “Exit notices” data source (“With backorder” and “Backordered units” fields). When the system does reserve the pending part, it publishes the notice “Backorder of an exit notice now reserved” (it can be muted in notifications).

**How do I assign a purchase order or a sales order to someone?** (Batch 195)
In the purchase order list (Purchases) or the sales order list (Operations), the row's **Assign** icon (permission `warehouse.manage`) chooses the person; the **Take** icon (`purchasing.receive` in purchases, `warehouse.pick` in sales) is taken by whoever is going to work on it. Only a sent or partially received purchase order, or a requested or partially dispatched sales order, can be assigned (400: “Only a sent or partially received purchase order can be assigned.” / “Only a requested or partially dispatched sales order can be assigned.”). An order assigned to someone else cannot be taken (409: “That document is already assigned to someone else; ask a supervisor to reassign it.”). The **“Only mine”** switch filters the list, and the “Assigned to” column says who has it.

**In “New rule”, how are the events in the list grouped?** (Batch 197)
The event dropdown list comes grouped by category: **Warehouse** (arrival and exit notices, inventory), **Sales**, **Transport** (fleet documents, fuel, vehicles and drivers with no trip), **Maintenance** (upcoming and overdue vehicle maintenance, work orders open for too long), **Billing** (when there are billing events), **Integrations** and **Other**. A group with no events does not appear.

**How do I get notified about vehicle maintenance about to come due?** (Batch 197)
Create a rule with the **“Upcoming maintenance”** event (and another with **“Overdue maintenance”**), Maintenance group: the subject and the text already come written by default (with the vehicle, the program and what is left); just choose who gets notified (role, person or email) and through which channel. The daily fleet job publishes them; they are not repeated while the set stays the same in the same week.

**How do I receive several notices or orders in a row on the device?** (Batch 198)
In Receive tap “Select several”, check the documents and “Receive N selected”. Those of the same owner are gathered by themselves into a master receipt; those of different owners end up as separate receipts. Message: “That document belongs to a different owner than this batch. Finish the batch to receive it.” (finish or cancel the batch).

**Where did “Receive together” / “Collect together” / “Sales orders” / “Exit notices” go on the device?** (Batch 198)
Receive and Dispatch use “Select several”. Dispatch merges notices and orders into one list.

**I assigned a document to myself by accident, how can I see why?** (Batch 198)
In the document's log (Audit) the change of “AssignedToUserId” carries `$reason` (“Self-assigned on … from the mobile app/the web”) and the user. A supervisor reassigns it with Assign.

**I have many “No zone” shipments in the Dispatch room. How do I fix them?**
In Fleet → Dispatch zones, at the top, the “Shipments without a zone” panel groups those shipments by postal code and town and says why each group has no zone. The most common is “No zone covers it”: with “Add to a zone” you choose the zone and the postal code or town, and the system assigns the zone to those shipments. If it says “A zone covers it: assign it”, press “Assign zone to shipments”. If it says the address has no postal code or town, the consignee's address must be completed.

**Why does a shipment come out as “No zone”?**
The zone is decided by the postal code or town of its delivery address against the criteria of the active zones. With no match, or with a tie between two zones, it is left without a zone.

**How do I let the warehouse supervisor see what is assigned to other people and reassign from the device?** (Batch 199)
System → Roles → the supervisor's role → check **“See everything on the device…”** (`warehouse.app.viewall`) and **“Reassign from the device…”** (`warehouse.app.reassign`; it does not work without the first one). With that, “Show what is assigned to others” and the Reassign button appear in Receive, Putaway, Dispatch and My tasks. If “You need a connection to reassign.” comes up when reassigning, connect the device; if “To reassign you need to be able to see everything on the device.” comes up, the first permission is missing.

**And `warehouse.pick.any`?** (Batch 199)
It no longer does anything: unassigned work is seen by anyone, and other people's work is seen by whoever has “See everything on the device”.

**Where do I set up printing the label or the invoice automatically when a shipment is created?** (Batch 200)
In the **client** record → Profile (it is a per-client setting). There is no longer a general setting in System → Printers & labels.

**Where did the purchases, sales and accounting permissions go?** (Batch 200)
In System → Roles, **Billing** section: each screen with its “open and view” and, right below, what edits it (e.g. Purchases: view, create and edit, receive; Sales: view, create, price, cancel, backorder, returns). Shipments and routes are under **Transport**.

**In the client profile I get “This company's status setup has errors… At least one terminal stage must remain enabled.”**
It was a defect: the client status has no terminal stage (deactivating is not a status) and the validator always required one. Since 2026-10-21 the terminal stage is only required in the statuses that do have one, and the warning no longer appears in clients.

**Where do I control the statuses (order, names, which ones are used)?**
System → Catalogs → “Statuses” button (you need `admin.statusconfig` to change them). You choose the domain (for example “Client status”), turn off the stages you don't use, move them up or down and rename them.

**In what order do the client statuses go?**
Prospect → Active, with Suspended as a reversible detour. A new client is born Active; “Prospect” sits before it in case you want to use it, and it can be turned off in System → Statuses. “Deactivate” is not a status: it deactivates the client.

## Batch 201 — Readable log and Kemi's answers

**Why does the log no longer show catalog numbers or field names with “Id”?**
Changes of status, catalogs, users, warehouses, clients, products, bins and suppliers are read by their name (“Status: Expected → Received”, “Assigned to: Ana Ruiz”). If something cannot be named, “(no name)” is shown; never the internal number. An assignment made by “take” also shows the “Reason”.

**Why does Kemi no longer show code or API calls?**
Kemi answers with the manual's text for the user; JSON examples and service calls are left out (they remain in the manual for whoever integrates).

### The receipt says “Unloading” (or “Discrepancy”) and units are still missing

“Unloading” is the status of the open receipt while it is unloaded and put away in parts: missing units are NOT a discrepancy, they are what has not been unloaded yet. Keep capturing what comes off, use “Confirm batch” to put away what was already received and “Confirm receipt” when done. “Discrepancy” only appears if MORE than expected was received. It can be closed with a shortage: the receipt becomes “Completed with variance”. If “Confirm batch” is disabled with the message that everything received is already confirmed, raise “Received” with what you just unloaded to make another batch.

**Why did the log say “Warehouse #n updated” when a bin was deleted?**
Until 2026-10-22 zones and bins were saved in the log as “Warehouse” and with the bin's number. Since then they have their own type (“Warehouse zone”, “Warehouse bin”) and their fields read “Active: Yes → No”, “Deleted: No → Yes”, “Deleted by: Ana Ruiz”. The rows before that date keep the old type (they are not rewritten).

## Batch 202 — Logo on the sign-in screen

**Why does the sign-in screen show another company's logo (or Teikem's)?**
The last company that computer signed in with wins (by login or by switching it in the header); if there is none, the one of the domain or subdomain the page was opened with; if neither, Teikem's. On a new computer with no configured domain, Teikem's comes out. The company uploads its logos in Settings → Branding.

**How do I configure a company's domain or subdomain?**
Only the platform administrator: `PUT /api/v1/platform/tenants/{publicId}/domains`. Messages: “The domain is not valid. Write it as logistica.miempresa.com, without https:// or paths.” (400), “The subdomain is not valid. Use only lowercase letters, numbers and hyphens (for example depot).” (400), “Another company already uses that domain.” and “Another company already uses that subdomain.” (409). `db-update` must be run so the columns exist.

## Batch 203 — Verification code in plain sight in development

**On my development machine the system asks for two-step verification. Do I have to open the authenticator?**
No. Only when the API runs in the **Development** environment, the code step shows “Development only: your current code is 123456” with the “Use this code” button. It is valid for a few seconds (the code changes every 30 s); if it expired, sign in again. In staging and production the code is **never** shown nor travels in the response. It applies to staff and portal login, and to the initial MFA setup.

## Round trip in Dispatch (2026-10-22)

**In the Dispatch room, how do I know whether I am going to pick up or to deliver, and what type the shipment is?**
Each stop of the route and each shipment in “Unassigned” carries a **Pick up** or **Deliver** tag and the service type (e.g. NEXT DAY, ROUNDTRIP). Round-trip shipments come out as **Pick up** (in amber): the driver goes to the store; the zone, the stop and the map are those of the pickup.

**A route had many shipments to the same client. Is it an error?**
No: they were round-trip shipments shown by their **delivery** address, which is always the client's warehouse. Since 2026-10-22 the **pickup** address (the store) is shown and routed. Details in chapter 33, section 6.

**How do I mark a service type as round trip?**
Lists → Service type → Adjust (or Edit) → **Round trip** switch. With the mark, new and edited shipments take the zone of their pickup; for the ones already loaded run `import-colavora --solo reparar,paradas`.

**In the Dispatch room, where does the “exceeds the maximum number of stops” warning appear and when?**
Inside the route panel, above the map, and also as a ⚠ icon next to the title (hovering shows the same message). It only appears while the route is being built (Draft or Planned): on a route already dispatched, in progress or closed —for example a migrated manifest— it is not shown because it can no longer be changed. The maximum is the driver's if they have one and, if not, the company's.

## Email integrations: attached PDFs (2026-10-22)

**Can the integration read a PDF that arrives by email?**
Yes. Turn on **“Also read attached PDFs”** in the email integration and define the fields to read (the same ones as the body). A PDF with real text is read directly; a scanned one is read with OCR. Details in chapter 39, section 6.

**The PDF went to the inbox with “The PDF is an image (scanned) and OCR reading is not configured…”. What do I do?**
The server does not have the OCR languages. The platform administrator must set the Tesseract folder (with `spa.traineddata` and `eng.traineddata`) in `Integrations:OcrTessdataPath` and restart the API. Then reprocess the file from the inbox.

**“No data was found in the PDF … Text read: “…””.**
The fields to read did not match the text. Look at the text the message carries: the columns of a table come out separated by **tab**; in a block of lines use the tab as the separator and check the start and end text.

**“PDF …: The file is not a valid PDF or is damaged.” / “The PDF is password-protected: it cannot be read.”**
The attachment could not be opened. Ask the sender to send it again without a password.

**“The PDF is larger than 15 MB.”**
Ask for a lighter PDF or raise the `Integrations:MaxPdfMegabytes` limit.

**The OCR misread a number or a column.**
A scan is read with less accuracy than a PDF with text. Check what went into the inbox; improving the scan quality (300 dpi, no skew) improves the reading.

**My PDF has the table on several pages and only the rows of the first one come in.**
The table header repeats on every page and the block, by itself, is read only once. Turn on **“Repeat the block on every page”** on that field (the start text is the header and the end text, the page footer).

**The rows of my table do not fit into columns (multi-line descriptions, SKU stuck to the description).**
Use a **per-line expression** in the block: each matching line gives one row with the groups you mark; the other lines are ignored. Details in chapter 39, section 6.

**“The per-line regular expression is not valid.” (400) / “To repeat the block, enter the start text (the repeated header).” (400)**
Fix the expression (unclosed parentheses or brackets) or write the block's start text.

## Email integrations: readers (2026-10-22)

**My mailbox receives orders, packing lists and invoices. How do I get each one read differently?**
In the email integration, **Readers** section: one reader per document type, each with its condition (for example, “the document text contains Packing List”) and its fields to read. The first reader that matches takes the document.

**“No reader of the integration applies to this email.”**
No reader took any document from the email. Check the conditions (attachment name, subject or text) and the type each reader reads (body, PDF or data file). To see the text it extracted from a PDF, upload it by hand: if it yields no data, the reason carries the text read.

**“The ATTACHMENT_NAME condition does not apply to a BODY reader.” / “…TEXT does not apply to a FILE reader.” (400)**
An email body has no attachment name and a data file has no text to compare. Use the subject, or change what the reader reads.

**“A body or PDF reader needs at least one field to read.” (400) / “There is already a reader named …” (400) / “Unknown source…” / “Unknown destination…” (400)**
Each reader needs fields to read (body and PDF), a name that is not repeated, a source (BODY, PDF or FILE) and a valid destination (today ORDER).

**Can I mix clients in the same mailbox?**
Yes: each reader can have its own client and its own template; those that don't carry them use the integration's.

## Location of the stops on the map (2026-10-22)

**The Dispatch map says “No stop has coordinates yet”. What is going on?**
The stops do not have latitude and longitude yet. They are looked up with Google when the route is built, in the background, if `GoogleMaps:Enabled` is `true` and there is a key (`GoogleMaps__ApiKey`). Without them, or if the monthly cap was reached, they are left as “No pin”: set it by hand from the stop. Details in chapter 5, “Location of the stops on the map”.

**How much does it cost? How do I avoid going over?**
According to the Google Cloud console, the Geocoding API gives the first 10,000 queries per month for free (after that, 5 dollars per 1,000). The `GoogleMaps:MonthlyCap` cap (9,000 by default) stops the lookup before going past the free tier. Each address is looked up only once.

**I raised the cap and the stops are not filled in.**
The value is read when the API starts: restart it (or the application pool). The stops left without an attempt because of the cap are retried by themselves.

## Batch 203 — Driver app: accept the route

**Message “Your user is not linked to an active driver.” (403).** The user signing in to the driver app is not linked to an active driver. The link is made in Lists → Drivers (user field). The user also needs the `driverapp.use` permission.

**Message “Manifest {code} is not ready to be accepted: it must be dispatched.” (422).** The route has not been dispatched from the Dispatch room yet, or it already departed. Dispatch it first.

**Message “Manifest {code} was already accepted.” (422).** The driver already accepted that route; it is not accepted twice.

**Messages “Enter the reason for each shipment you do not accept.” and “The reason is not valid.” (400).** Each shipment the driver does not accept carries one of the reasons from the “Decline reasons” list (Lists). If it is missing or does not exist, nothing is saved.

**What happens to the shipments the driver does not accept?** They leave the route, go back to “Unassigned” and keep their reason so they can be reassigned. If the driver accepts none, the route does not depart.

**Who records the departure when the driver accepts?** It is the same departure as in the Dispatch room: the route becomes “In progress” and the accepted shipments “In transit”, with the comment “Accepted by the driver”.

## Batch 204 — Driver app: access

**Why does the app ask me for the MFA code if I already use my fingerprint?** The fingerprint only opens the session that is already on the phone; it does not replace the code. The code is requested on password sign-in (every time the session expired or was closed) if your company or your user requires it.

**Why does the app no longer remember my password with the fingerprint?** Before, the password was stored on the phone and resent with the fingerprint, which skipped MFA. Now the fingerprint opens the stored session and the password is not stored.

**How long does the session last in the app?** Whatever your company sets (days or minutes). When it expires, the app asks for the password (and code) even if the fingerprint is active.

**My user is in several companies, how do I switch?** No need to switch: the app shows the routes and shipments of all your companies together, each with its company name. Profile → Companies lists them.

**The app asked me to accept the privacy policy.** It is the first sign-in or the document changed. You must read it to the end; if you do not accept it, you cannot use the app.

## Batch 205 — “App only” driver and several companies

**Message “Drivers sign in with the driver app.” (401/403).** The user is of type “Driver (app only)” and cannot sign in to the web. Use the driver app.

**Message “This session is not a driver app session.” (403).** A web session was used on a driver app endpoint (or the other way around with the MFA code). Each app starts its own session.

**A driver works in two companies and one requires MFA.** With a single app session, the company that requires MFA only grants access if the session passed the code; otherwise it appears blocked (“mfa_required”) and you must sign in again with password and code.

**How long does a driver's session last in several companies?** The longest of the companies where they are a driver (each with its driver value or, without it, its general session), capped by the application's limit.

## Batch 206 — Deliver and mark as not delivered from the driver app

**Message “Accept the manifest first: it has not departed yet.” (422).** An attempt was made to deliver or mark “not delivered” a shipment whose manifest has not been accepted. Accept the manifest first.

**Message “The shipment is not in any of your manifests.” / “Shipment not found.” (404).** The shipment does not belong to a route of this driver.

**Message “Enter who received the delivery.” (400).** The name of the person who received it is missing (maximum 150 characters).

**Message “The signature is not a valid image.” / “A photo is not a valid image.” (400).** The image arrived damaged; take it again. Maximum 3 photos per delivery (“Maximum 3 photos per delivery.”).

**Message “Enter the reason why it was not delivered.” / “The reason is not valid.” (400).** Choose a reason from the “Failure reason” list.

**Message “Shipment … already has its delivery recorded.” (409).** The office already recorded the delivery from the web; it is not repeated.

**I delivered with no signal, is it duplicated?** No: the app sends the shipment when the signal returns and, if the server already had it, it answers “already done” without repeating anything.

## Batch 208 — Route tracking

**Message “Maximum 500 points per submission.” / “Maximum 50 notices per submission.” (400).** The app sends batches of up to 500 points; if you see this message, the app is out of date.

**Message “The notice type is not valid.” (400).** The valid notices are STARTED, STOPPED_BY_DRIVER, PERMISSION_LOST and RESUMED.

**The office cannot see a driver's route.** Check that the route is **in progress** (the driver accepted the manifest), that the driver has not turned off sending in Profile and that the app has the “Allow all the time” location permission. Without signal the points accumulate and go out when it returns.

**How long are the points kept?** 10 days (`DriverApp:Tracking:RetentionDays`); a nightly job deletes the oldest.

**The phone kills tracking when the screen is off.** Exclude the app from battery optimization (Settings → Battery → this app → Unrestricted). Some manufacturers require it.

## Batch 209 — Driver notices

**Message “Enter the notice title.” / “The title cannot exceed 120 characters.” / “The text cannot exceed 1000 characters.” (400).** Fix the notice the office is writing.

**The driver did not get the notice on the phone.** For now there is no push notification: the notice appears on the “Notices” tab when the app is opened (it refreshes every 5 minutes).

**The driver does not see notices from one company.** The inbox gathers the companies with a session; if one is blocked by MFA (“Sign in again with your password and code”) its notices are not shown.

## Collection and delivery requirements (driver app)

**“This client requires the signature of the person who receives.” / “This client requires a photo of the delivery.” (400).** The client has the mandatory signature or photo turned on (client record → Printing & portal → “Delivery with the driver app”). The driver must capture it.

**Collection messages (400/422)**: the same as the web COD ones: “The collection method is not valid.”, invalid amount, check number required, “You cannot collect more than what is outstanding…”. In the app the driver cannot collect more than what is pending.

## Push notifications

**Message “The phone token is missing.” / “The platform must be ANDROID or IOS.” (400).** The app sent an incomplete registration; update the app.

**The notice does not ring on the driver's phone.** Check, in this order: (1) that the server has the Firebase key (`Push:Fcm:ServiceAccountJsonPath`); (2) that the driver accepted the notification permission; (3) that the app is of a brand included in `google-services.json`; (4) that the “Push notifications to drivers” job is active in Scheduled jobs. The notice always stays on the “Notices” tab.

**What does “Driver deliveries far from the address” mean in Needs your attention?** They are deliveries or “not delivered” marks made from the app with the phone more than 300 m from the delivery address (last 7 days). The phone's GPS is where the driver was, not where the address is; if it repeats with one driver, it is worth talking to them. “No GPS” in Attempts means the driver denied the location permission or there was no satellite signal.

## Pickups

**“The shipment was already picked up (or left the pickup flow); it cannot be assigned to a pickup route.” / “The shipment has no pending pickup stop.” (422).** When adding to a pickup route, only shipments still in the initial stage and with the pickup stop pending are accepted. Check the order status; if it was already picked up, it goes on a delivery route.

**“This shipment belongs to a pickup route: it is picked up, not delivered.” / “This shipment belongs to a delivery route: it is delivered, not picked up.”** The app used the action that does not match the route type. On pickups use “Pick up shipment” or “Not picked up”.

**“Shipment … is no longer pending pickup.”** Another user already picked it up or took it out of the flow. Refresh the list.

**“The “Picked up” status is not enabled for this company.”** The company's flow does not have the “Picked up” stage; enable it in Statuses before using pickup routes.

**“The route type must be DELIVERY or PICKUP.” (400).** Only those two types exist.

**“Row N: the quantity “…” is not a number greater than zero.” / “Row N: the item number (SKU) is missing.” (inbox).** The reader read a line of the document without a quantity or without a product (in a scan, a quantity crossed out or circled by hand). Since a half-made notice is worse than none, no notice was created from that document. Fix the document or upload it again; the other lines were not lost: they are in the reason.

**“Product “X” does not exist among this client's active products.” (inbox).** The item number in the document does not match any active SKU of the reader's client (or the reader has another client). Create the product with that SKU for that client, or fix the reader's client, and reprocess by loading the document again.

**“The document has no line with product and quantity.” (inbox).** The reader took the document (it met its conditions) but its per-line expression found no row. Upload the PDF by hand: the reason shows the text that was read, and with it the reader's expression is adjusted.

**Two almost identical documents (dispatch and receipt) and one lands in the wrong reader.** Readers are evaluated in list order and the first one that matches wins. Give each one a condition on the **text** with its full title (“Despacho de Mercancia” / “ENVIO DE MERCANCIA PARA”) and check that no condition is contained in the other's title.

**“No record was found in the document (the record start text does not appear).” (inbox).** The reader is per record (it has “Text that starts each record”) but that text is not in the PDF that was read. Upload the document by hand: the reason shows how the OCR text came out (one misread letter in “Order #:” is enough). Adjust the start text to something the OCR reads well, such as “Order”.

**“Record N: The required field … was not found” (inbox).** In order N of the document a required field was not found (for example the patient's name). No order of the document is created. Fix the document or make that field optional in the reader.

**The route (“ERD Pick Up 1”) comes out cut off or with tabs.** Give the route field the “Collapse spaces and tabs” transformation and mark it “from the header”.

**“The zone or route “X” does not exist or is inactive in the company.” (inbox or 400).** The “Zone or route” column (or `dispatchZoneCode` through the API) carries a text that is neither the code nor the name of any active zone. Create the zone in Fleet → Zones with that exact name (or fix the name in the document) and reprocess the row.

**“To create the pickup point the name / the address (line 1) / the town is missing.” (inbox).** The template sends one of the “Pickup (…)” fields but the row does not carry the three required ones. Complete the document or the reader.

**“Enter either the existing pickup point or a new one, not both.” (400).** Through the API, `pickupLocationPublicId` and `newPickup` do not go together.

**“The shipment number is assigned by Teikem for this client.” in CMS orders.** The client does not have “The client assigns the shipment number” turned on; turn it on so that the CMS Order # becomes the order number.

## Batch 212 — Messaging with the driver, references and people in charge

**“Your user does not have messaging enabled in this company.” (403).** Your messaging setting is `NONE`. A user administrator (`admin.users`) changes it in Messaging → modes (`PUT /messaging-modes/{userId}`).

**The driver does not see other drivers in their list.** That is normal: users linked to a driver are set to “Office only” (`OFFICE`). If the company wants them to message each other, set `FULL` on the ones that apply. Groups created by the office do bring them together.

**“User not found in this company.” when writing to someone who does exist.** That person is not visible to you because of their messaging setting (an `OFFICE` driver does not see another `OFFICE` one; nobody sees a `NONE`).

**“The reference must be an ORDER or a TRIP (route).” / “The order or route the message refers to does not exist in this company.” (400).** The card only accepts orders and routes of the active company. Refresh the screen and try again.

**The order card only shows the number.** Whoever sees it has no permission to view orders (`orders.view`) or routes (`trips.view`); by design they only see the title.

**“You have no permission to change the people in charge (trips.plan).” (403).** Changing the people in charge of a route requires `trips.plan`; those of a zone, `fleet.manage`.

**The “Suggested” do not include the person I expected.** They come, in order: people in charge of the route, of the zone, roles with `trips.dispatch` and recent conversations; whoever you cannot see and yourself are left out. Add the person as in charge of the route or the zone.

**“The proposal was already resolved.” (409).** Someone else (or the automatic reader) already confirmed or rejected that proposal. Refresh the Proposals list.

**A proposal ended up “Failed”.** On confirming, the order did not allow the change; the reason is in the Decision column (for example “Order … is in “Delivered” and cannot be moved to “Picked up” from there.”, or “The “Picked up” status is not enabled for this company.”). Check the order; a failed proposal is not retried: if needed, process the document again.

**“There is no order “X” of client …” (inbox).** The document names an order that does not exist among that client's active orders (or it belongs to another client). Check the number in the document and the reader's client.

**The closing document was read but no proposal appears.** It may be that each order already has a live or applied proposal (the file's reason says “… were already proposed”), or that no line meets the reader's expression (in CMS, only rows of type PU). Upload the PDF by hand to see the text read.

**“The document has no order to close.” (inbox).** The reader took the document but extracted no key (column 1). Check the reader's per-line expression.

**“Unknown mode: …” / “Unknown closing status: …”.** The closing reader only accepts Suggest or Automatic and moving to Pickup or Delivered.

**The integrations script stops with “The company / client / warehouse / service type … does not exist”.** The parameters at the top of the script (`@TenantName`, `@ClientCode`, `@WarehouseName`, `@ServiceTypeCode`) do not match the data of that database. Fix the value and run it again: the script is idempotent and leaves nothing half done.

**I ran the script and the integration does not receive emails.** It is born turned off and with the mailbox set to “CAMBIAR” placeholders. In Integrations → Edit set the IMAP server, the mailbox user and password, the allowed senders and turn it on. Before turning it on, test with *Upload file*.

**“There is no warehouse …” / the arrival notice is not created with several warehouses.** With more than one active warehouse, the reader must have its “Notice warehouse”. Edit it in Integrations → Edit → Readers.

## Batch 212 (B) — Chat photos, phone notification and role groups

**“A message can carry up to 4 photos.” / “Only JPG, PNG or WEBP photos are accepted.” / “Each photo cannot exceed 3 MB…” (400).** These are the limits for chat photos; the app shrinks them by itself. A PDF or a document does not go through the chat: it is attached to the order.

**“The file store is not configured (Files:MasterKey)…” when sending a photo (400).** The store key is missing on the server (`Files:MasterKey`); let support know. Text messages keep working.

**“The photo was deleted on …” / “The photo was archived on …” (409).** Chat photos live 30 days by default (adjustable in `GET/PUT /messaging-settings`, `admin.tenant`). If it had to be kept, it should have been attached to the order as evidence. With the `ARCHIVE` action the content is kept on the server even though it is no longer shown.

**Why doesn't the driver receive the push notification of a message?** Check in this order: (1) the server has the Firebase key (`Push:Fcm:ServiceAccountJsonPath`); (2) the driver accepted the notification permission and has the app installed; (3) they did not mute that thread; (4) their user is not set to “No messaging”. The message always stays in the chat.

**“People are not added to a role group: whoever has the role is in it.” (409).** In the “live” group of a role (for example Dispatchers), whoever has the role is in it. To bring someone in, give them the role; for a custom conversation, create a normal group.

**Someone no longer has the role and is still in the group.** The list is synced when someone writes to or opens the group; if neither has happened, it updates on the next one. Whoever wrote to the group without having the role (a driver) is never removed because of this.

## Batch 212 (C) — Messages in the driver app

**“To start a conversation you need a connection. Try again.”** (app). Starting a new conversation is done live with the server; only replying in an existing conversation works without signal.

**My message says “Not sent”.** It is in the phone's queue; it goes out by itself when there is a connection (it is not duplicated). If hours go by with good signal, open the Messages tab so the app retries, or let support know.

**“The message could not be saved.” (app).** The message could not even be queued (little space on the phone or a storage error). Free up space and try again.

**The driver does not see another driver in “New conversation”.** That is normal (“Office only” setting). If the company wants them to message each other, the administrator sets them to `FULL` (chapter 15 §8).

**The driver gets 403 in Messages.** Their role does not have `messages.use`. Apply the database update (`db-update`, section 2026-10-23 (l)) or add it to the role.

**Tapping the order card says “That shipment is no longer in your routes.”** The shipment is no longer in the driver's open routes (it was delivered, closed or belongs to another company). The card always shows the order number.

**The consignee's phone does not open the dialer.** On a tablet or a phone without calling, the system has nothing to open the `tel:` link with; the number can be copied. If it is a phone, check that the number has at least 7 digits.

**“Doubtful reading (N % < M %): check what was created against the document.”** The OCR read the document's numbers with a confidence below the reader's minimum. What was created (order or notice) exists: compare it with the original document and correct it if needed. If most of your good documents come out doubtful, lower the reader's “Minimum reading confidence” (or set it to 0 to stop watching it); if misread documents get through without warning, raise it. Confidence does not detect lines or columns the OCR did not read: always compare the number of lines.

**A “Completed route” closing was left to confirm even though the reader is Automatic.** The reading was doubtful: closings are not applied by themselves with a doubtful reading. Confirm it in Integrations → Proposals.

**“Doubtful reading: the sum read of “X” is A and the document declares B.” / “the document declares N line(s) of “X” and M were read”.** A completeness check of the reader does not add up: the OCR did not read some line (or misread an amount). What was created exists: compare it with the document and complete what is missing. A SKU misread by the OCR goes separately, to the inbox.

**“… the total “X” was not found in the document.”** The check looks for the total the document declares and it does not appear: the page where it is may be missing (the total goes at the end) or the OCR did not read it. Check the document; if the total is written differently, adjust the check's “Total expression”.

**A check that used to pass now fails on every document.** The total expression no longer matches the text (the document format changed). Upload a PDF by hand and look at the text read to adjust it, or remove the check.

**The quantity of the scanned invoice comes out different from the handwritten one.** If the reader calculates the quantity as amount ÷ unit price, a misread amount or price gives a quantity that is not a whole number; the sum check usually flags it. Compare with the document.

## Batch 212 (D) — Messaging on the web

**“Only JPG, PNG or WEBP photos are accepted.” / “A message can carry up to 4 photos.” / “The photo could not be read.” (web).** These are the limits of the 📷 button: three formats, up to 4 photos per message; the photo is shrunk in the browser and, if the browser cannot read it, it warns. A PDF does not go through the chat.

**I don't see “Talk about this order”.** The button and the order's “Messages” panel only appear with the `messages.use` permission.

**The order card only shows the number and does not open.** Whoever sees it does not have `orders.view` (or `trips.view` for a route). Ask for the permission or open the order from your list.

**I cannot change the people in charge of the route/zone.** You need `trips.plan` (route) or `fleet.manage` (zone); the ✎ button only appears with the permission.

**The “Messaging” column in Users does not appear.** It requires `admin.users`.

**“Days must be between 1 and 3650.” (Settings → Operations).** The days chat photos live range from 1 to 3650.

**“Doubtful reading: the document lists N item(s) of “X” and M were read.”** An “items the document lists” check does not add up: the document's table (for example the stops on the CMS route sheet) has more rows than orders were read. Some order is missing: compare with the document and complete what is missing.

**“… what “X” lists was not found in the document.”** The check's expression found no line of the list (the OCR read the table differently, or the table is not there). Upload the PDF by hand, look at the text read and adjust the expression (it describes ONE line of the list), or remove the check.

## Batch 213 — Mobile app (driver and staff)

**“Your user has no access to the mobile app in any company.” (403) when signing in to the app.** Your role does not have `mobile.use` and you are not an active driver. Ask an administrator to add “Mobile app: use it” to your role (System → Roles). The Driver and Dispatcher roles include it; earlier roles get it with the database update (section 2026-10-24 (m)).

**I am office staff and the app shows me the same as the driver (without “Needs your attention”).** The staff home appears with `pulse.attention` or `analytics.view` in some company. The other staff screens arrive in Batches 214-216.

**A driver sees office cards in their app.** If the user is of type “Driver (app only)” that cannot happen: let support know. If it is a normal user with office and driver permissions, it is expected; for them to see only the driver's part, create their user as “Driver (app only)”.

**I don't get push notifications and I am not a driver.** Check that the app is the Batch 213 version or later (it registers the phone in `mobile/device`), that you accepted the notification permission and that the server has the Firebase key.

## Batch 214 — Mobile app: orders

**“Type or scan an order, waybill, package or invoice number.” (400).** The search arrived empty. Type something or scan the label.

**I search for an order in the app and nothing appears.** The search is live: without signal there are no results. With signal, check the company chosen at the top (the order belongs to the company where it was created) and that the order is active. A partial number searches by “contains”; the scanner searches for an exact match.

**The Actions tab says “This order is “Delivered”: there is nothing to complete from here.”** Only an order In transit (deliver or mark as not delivered), Failed (deliver again) or pending pickup can be completed. For any other change, the web.

**“You have no permission to complete orders in this company.”** Viewing the record requires `orders.view`; completing requires `orders.deliver` in that company. Ask the administrator for it (System → Roles).

**I delivered from the staff app and I want it NOT to count for the route's driver.** Turn off “Credit the driver” before confirming (it comes on). When on, the attempt and the collection count toward the driver's settlement; the history and the audit always say who closed it.

**I completed an order from the staff app and it says it could not.** Staff does not work without signal: nothing is saved. With signal, try again.

**The driver still sees in their manifest an order I delivered from the office.** Their app removes it on the next refresh (when opening the manifest or syncing); if they try to deliver it, the server answers that it is already delivered (409).

## Batch 215 — Mobile app: today's routes and assigning on the street

**“Assign to a route” does not appear in the order's Actions.** You need `trips.streetassign` in that company (and the transport module). The permission is new: administrators have it and it is given to whoever applies in System → Roles. `trips.dispatch` does not include it.

**“Order … is a delivery: choose a delivery route.” (422).** Orders pending pickup (in their initial stage) only go to pickup routes; the rest, to delivery routes. The list only shows routes of the matching type.

**“Order … is “Delivered”: it can no longer be moved to another route.” (422).** Only orders still in the normal flow are moved. A delivered, failed or cancelled one is not moved between routes from the street.

**The route I am looking for is not in the list.** Only today's open routes (planned, dispatched or in progress) and the dispatched or in-progress ones from previous days appear, of the same type as the order. A closed or cancelled route does not appear.

**The distance to a route does not show.** The distance is calculated from the last location the driver sent, and only when the route has already departed; the order's address must also have coordinates. Without that, the route appears without a distance.

**I sent an order to a route and the driver does not see it.** They get a “New stop at …” notice (on the phone in less than a minute if there is Firebase and the app is installed) and the stop appears when they refresh their manifest. If their manifest is already open, have them refresh it.

**I built a quick route and it says it failed.** The route cancels itself and the orders stay as they were. Read the message: the most common cause is that the driver or the vehicle is already busy that day, or that the order is already on another route (take it out first).

**Without signal it does not let me assign or see routes.** That is expected: staff does not work without signal on these screens; only the driver's part works offline.

## Batch 216 — Mobile app: scanning without signal

**“Scan” does not appear on my home screen.** You need `trips.scan` in some company (and the transport module). It is the same permission as the web scan station.

**I scan and it says “The piece was not found.” but the piece exists.** What is scanned is the **piece number** (the one on the label), not the shipment's. If the number is correct, the piece is not in what was downloaded: either it is newer than the last download (tap **Sync** with signal) or the phone has more than 5000 uncompleted shipments and the piece was left out of the 5000 most recent (with signal, the app asks the server).

**“The piece was not found in the downloaded data and there is no signal to ask.”** Without signal only what was downloaded can be found. Sync when you have signal.

**It says “The number is in N shipments: without signal it cannot be confirmed.”** The piece number is repeated across shipments (from different clients). With signal, the server resolves it just like the web; without signal, scan the package on the web or wait until you have signal.

**Something appears under “With issues”.** The phone accepted it without signal, but when syncing the server could not do it (for example, on **Route**: “There is no open route for zone …”, or the chosen route was already closed; or the shipment was already delivered). Read the message, fix it on the web if needed and dismiss the notice with “Remove”.

**I scanned without signal and in the shipment history the time is later.** That is expected: the server stamps the time the phone **synced**, not the time of the scan.

**“To send” does not go down to zero.** The queue goes out by itself with signal. If there is signal and it does not go down, open the Scan screen (it retries) or use Sync; a server failure leaves the scan in the queue for the next attempt.

**Two people scanned the same thing without signal.** The first one to sync moves the shipment; the second one counts the piece (or sees a duplicate “Ok”). Nothing is lost or duplicated.

**Return does not work.** Return creates the return order on the server: it needs signal and the `orders.return` permission.

**The voice does not sound.** The voice uses the phone's. Check the screen's “Voice” switch and the volume; if the phone has no voice installed, vibration and color remain (green = Yes, amber = Ok, red = No).

**The Scan screen does not let me type or scan.** First you have to choose what you are going to scan (Pick up, Arrived, Route, Depart or Return): the screen asks when it opens and there is no default action. Tap one and the field is ready for the trigger.

**I scan a piece that does exist and it says “No” (red) with “does not belong to the chosen routes / cities / drivers”.** You chose to scan **by route, city or driver** and that piece belongs to another. Tap **Change** at the top to choose another selection or **Everything**.

**The screen stopped with “Attention: the shipment is already further along”.** You scanned a process earlier than the status the shipment is already in (for example Route on one that is already In transit). The system stops so you can decide: **Cancel** changes nothing; **Move the status back** makes the shipment **go back** to the status of what you are scanning. It is the way to move a status back by scanning. If the status is earlier than Assigned, the shipment leaves its route.

**It says “Ok”.** “Ok” means **that same piece** had already been scanned with that action. A new piece says “Yes” even if another piece of the same shipment had already moved the status: the scan is per piece and each new piece is counted.

**I scanned the shipment number and it says “The piece was not found.”** The number of **each piece** (the one on the label) is scanned, not the shipment's, the package's or the invoice's. It is the same in the app and on the web.

**No drivers, routes or cities appear for me to choose.** They come from the download: tap **Sync** with signal. The cities are those of the downloaded shipments.

**I scanned and it said “No”: how do I know why?** Use the **magnifier** (next to the scan field, in the app and on the web): type the piece number and you will see what is in the database: cancelled shipment, final status, deactivated, lost piece, under claim or “To verify”, and which route it is on. Searching does not count as a scan. Without signal, the app only shows what was downloaded.

**The magnifier says “No signal: only what is in the downloaded data is shown”.** A cancelled piece or one in a final status is not downloaded to the phone; it appears when there is signal.

**On the web a window came up: “Attention: the shipment is already further along”.** The piece belongs to a shipment whose status comes after the chosen action. The system changed nothing. **Cancel** leaves it as it is; **Move the status back** makes the shipment go back to the action's status (if it is earlier than Assigned, it leaves its route and the driver gets a notice; the later scans of its pieces are deleted). If the shipment was already in that same status the window does not appear.

**On the web, the scan field is locked and asks “Pick at least one option”.** You chose to scan **By route, By city or By driver** and have not checked any yet. Check one or several in the list (you can search by typing) or go back to **Everything** to scan without a filter.

**On the web a piece that exists says “No… does not belong to the chosen drivers”.** You are filtering by route, city or driver and that piece belongs to another. It is not counted and nothing changes. Change the selection or use **Everything**.
<!-- kemi:portal -->

## Batch 213 — Client portal: footer, date filter, New shipment same as the staff's, Messages and selecting several consignees

**On the phone or tablet I don't get the quick entry for shipments.** It is on purpose: in the installed app, the tablet and the phone (also in their browser) only the **detailed** one exists. The quick one belongs to the web on a computer (window of 900 px or more). The form depends on the device, not only on the window width.

**How do I enter shipments without touching the mouse?** In the quick entry: **Tab** moves from field to field, in Consignee type and choose with **↑/↓ and Enter** (the last option is “+ New consignee”), and **Enter in any other field saves the row**; the next one brings the same date, service and package type and the cursor goes back to the date.

**When I press Tab on the date I don't leave the field.** The browser's calendar goes through month, day and year with Tab before moving to the next field; it is the browser's, not the portal's.

**“Choose the service type.” / “Choose the package type.”** The company offers a catalog and there is no default enabled for your account; choose it in the dropdown. If the one you use does not appear, the company turned it off.

**“The due date cannot be before the ready date.” (400 `promisedDate`).** Fix the due date: it must be equal to or later than the ready date.

**“Unknown service type: X.” / “Unknown package type: X.” (400).** The code is no longer enabled for the company; reload the screen to bring the catalog up to date.

**“The order number is assigned by Teikem for this client; leave it blank.” (400).** Your account does not assign its order numbers: the field comes disabled as “(automatic)”; if you see it editable, reload.

**I don't see the Order no. (or Invoice) column in the quick entry.** They only appear if your account assigns its own numbers; otherwise the company sets them.

**Where did the description / pieces / weight go in the quick entry?** The quick entry creates a one-piece package, like the staff's; description, pieces and weight are entered in the **detailed** one.

**What does “Today” count in the indicators?** What was created or delivered since midnight today in the company's time zone (not the last 24 hours). `days=0`; with another number: “Days must be 0 (today), 7, 30 or 90.”

**The date filter in My shipments says “Custom” even though I didn't choose it.** The dates in place do not match any predefined range (for example “This week” left open from one day to the next). The name always describes the actual dates.

**What is “9 of 9” at the foot of the table?** How many rows are shown out of how many match the filter; with “Load more” the first number grows.

**The footer (Privacy, Terms…) no longer “floats” mid-page.** On the desktop web it stays stuck at the bottom when the content is short and at the end when it is long; on the phone, at the end of the content.

**How do I mark several consignees for notices without going one by one?** In My consignees check each one's box (or “Select the N shown”) and use **Notify by email / by text**; the warning is accepted only once for the group. “Remove email / text” asks for no warning.

**“1 marked, 1 could not be. Colmado Sur: The consignee has no registered email.”** The multiple marking marked the ones it could and tells you who it could not and why (without that channel registered, deactivated or nonexistent); register the missing data and mark them again.

**“Choose at least one consignee.” / “Mark up to 200 consignees at a time.” (400).** The multiple marking needs a selection of 1 to 200; search and select in parts.

**On the web, Messages is no longer a single conversation.** It is an inbox: on the left “Customer service” and “Messages from my consignees” (with the unread count, search box and “Unread only”); on the right the conversation you open. On the phone it stays as before.

**How do I export a table from the portal?** “Export” at the foot of the table (web) or the “Export” button under the search box (phone). It downloads a CSV with **everything that matches the filter** (search, dates, status), up to 5000 rows; on the phone the file is shared. It is in My shipments, My invoices, My warehouse and My consignees.

**The exported CSV has more (or fewer) rows than I was seeing.** The whole filtered set is exported, not the page: if the screen said “20 of 130”, the file has 130. If it exceeds 5000, the first 5000 are exported: narrow the filter (dates) and export in parts.

## Batch 213 (staff web) — New consignee inline

**How do I create a consignee that does not exist without using the mouse?** In the quick or the detailed entry, type the name in the consignee field and press **Enter** on **“+ New consignee “name””** (at the end of the list; if nothing matches it is already highlighted). Fill in Address and Town and press **Enter**: it is created, it stays selected and the cursor moves by itself to Package (quick) or to the next control (detailed). Full guide in chapter 03, section 1.0b.

**I typed a name and Enter chose another consignee, not the new one.** If what you typed matches existing consignees, Enter chooses the highlighted one (the first match). Go down with **↓** to **“+ New consignee…”** and press Enter. If the consignee already exists, choose it instead of creating it again.

**I pressed Enter in the form and it did not save: the cursor went to another field.** A required field is missing (Name, Address or Town): the cursor goes to the first missing one and the message appears under the field (`The name is required.`, `Address (line 1) is required.`, `City is required.`). The town is chosen from the list (type the town or the postal code and Enter); typing it without choosing it does not count.

**“Invalid phone.”** The phone does not match the company's mask (Settings → Region & formats). Fix it or leave it empty; it is optional.

**I don't see the Phone field in the inline form.** `contacts.manage` and `locations.update` are needed in addition to `locations.create`. Without them the consignee is created without a phone; it is added later in Catalog → Consignees.

**It does not offer me “+ New consignee” when typing.** The `locations.create` permission is missing (the “+” button will not appear either), or the client has not been chosen yet (the owner of the new consignee is the order's client).

**The server rejected the creation (for example, 400 `Maximum 200 characters.`).** The message appears inside the inline form and **nothing you typed is lost**: fix it and press Enter again. If the cursor did not move, the error belongs to a field that is not visible (it stays in the notice above the form).

**The consignee was created but “The consignee was saved, but the phone was not: …” came up.** The consignee exists and stayed selected; only the phone failed. Fix it in Catalog → Consignees → Phone.

**I pressed Esc and the form closed.** That is intended: Esc cancels, creates nothing and returns the cursor to the consignee field with the name you had typed. (In the Town field, the first Esc closes the town list and the second cancels the form.)

**I changed the client with the form open and it disappeared.** The new consignee belongs to the order's client; when the client changes, the form closes so it is not created with the wrong owner. Type the name again with the right client.

**I want to fill in more data (contact person, time window, notes).** Use the “+” button next to the field (it opens the full window) or edit the consignee later in Catalog → Consignees.
<!-- kemi:portal -->

## Batch 213 (round 2) — Client portal: PDF and Excel with logo, new consignee on the same screen

**Can I export to PDF and Excel from the portal?** Yes, on the web: “Export · Excel · CSV · PDF” at the foot of each table (My shipments, My invoices, My warehouse, My consignees). The whole filtered set is exported. On the phone there is only CSV.

**Which logo does the PDF carry?** The one of the company that serves your account; if the company did not upload any (Settings → Branding, on the company's web), Teikem's. Below go the company, the title, “Generated on …” and the filters applied.

**The PDF or the Excel come out without a logo.** The browser could not read the logo in time (more than 3 seconds) or the file is damaged: the document comes out anyway, without a logo. Export again; if it persists, let the company know so they upload the logo again.

**I typed a consignee that does not exist and pressed Enter, what happens?** The “New consignee” block opens (name, address, town, state, postal code and phone) with the cursor where something is missing; Enter saves the consignee right then, it stays selected and the cursor continues in the shipment. You don't need the mouse.

**“Enter the consignee's name, address and town.”** One of the three required fields of the new consignee was missing; the cursor is already on the first missing one.

**“The consignee name / address / town is required.” (400).** The server received the new consignee without that data; complete it and save again.

**I saved the consignee but the shipment gave an error.** The consignee is already saved (it is neither lost nor duplicated); fix what the shipment's error says and save it again.

**The new consignee does not appear in “My consignees”.** It appears as soon as it is saved; reload the Notices/Consignees screen if you had it open.
<!-- kemi:portal -->

## Batch 213 (round 3) — PDF colors and Teikem logo; quick entry columns

**What color does the PDF come out in?** The company's color: the table header and the title use the color of the bar the company defined in Settings → Branding, and the line under the header, its accent color (with the default branding, navy blue and orange). It applies equally to the portal's and the company web's tables, grouped tables and reports.

**The company web's PDF comes out with the Teikem logo.** That is expected when the company has not uploaded a logo (Settings → Branding): Teikem's is used. When you upload yours, the company's comes out.

**In the portal's quick entry I see “Shipment no.” and “Invoice no.” in gray with “(automatic)”.** Your company assigns those numbers; they are not typed here. If the company lets you assign your own numbers, those two columns become editable (Tenant settings → client record → numbering).

### Batch 213 — what was entered in the quick entry (2026-10-23)

**I saved a shipment in the quick entry and it does not appear in the table below. Was it lost?**
It was not lost. The table shows the **From/To** range (both “today” when opened) and widens by itself when you save a later or an older date. If it does not show, it is because you shortened the range by hand: move “From” or “To” again; the “N of M” counter tells you how many there are in total. The shipment already exists in “Shipments”.

**What do the “Total COD”, “Total packages” and “Distinct invoices” at the foot of the entered-items table add up?**
Only what is being shown: the chosen range and the search. The figures at the top (“Lines”, “COD total”, “saved”) count everything entered since you opened the screen. “Distinct invoices” counts **numbers**: if you put several separated by commas in the invoice field, each comma is another number, and a repeated one counts once.

**In My shipments, my shipment shows “Assigned” or “Unassigned” and with a “1 failed attempt” pill. What does it mean?**
That the shipment already went out on the street, could not be delivered and went back to the warehouse; it is not delayed for any other reason. It is assigned and goes out again. The pill says how many failed attempts it has (“2 failed attempts”). It disappears when the shipment ends (delivered, returned or cancelled).

**“No saved entry matches the dates and the search.” / “No saved shipment matches the dates and the search.”**
There are saved entries, but none in the chosen range with those words. Clear the search or change the dates. The search looks at any data (number, invoice, consignee, address, package, service, COD, comment) and requires all the words to be present.


## Batch 217 — Mobile app: clients, consignees and warehouse (lookup)

**Clients, Consignees or Warehouse do not appear on the app's home screen.** Each entry appears only with its permission in some company: Clients `clients.read`, Consignees `locations.read`, Warehouse `inventory.view` (and the company's module: Catalog, or Inventory by lot and serial). A “Driver (app only)” user does not see them.

**It says “Could not look up. A signal is needed.”** These lookups do not work without signal (only scanning and the driver's screens work without it). Try again with signal.

**“Nothing was found.”** There is no match for what you typed. In Warehouse the code is first tried as an exact barcode or SKU and, if it does not exist, a text search is made. Only active products appear.

**Why don't I see credit, contracts, costs or prices?** By the owner's decision the app does not show money or billing information; that is seen on the web with your permissions.

**I tapped the phone, the email or the address and nothing happened.** They are system links: they open the phone's dialer, email or map. On a device without that application there is nowhere to open it. A number of fewer than 7 digits is not offered as a phone.

**In Warehouse the stock does not match what I see on the web.** The app shows “on hand” and “available” (on hand minus reserved) per warehouse and bin, from the same stock as the web; zero rows are not listed. Refresh by leaving and re-entering the record.

**In the installed app, how do I export a table to Excel, CSV or PDF?** Same as on the web: at the foot of the table (or of the card list) are “Export · Excel · CSV · PDF”. In the app the file is saved on the device and the share menu opens: from there you save it to Files or Drive, send it by email or WhatsApp, or print it. All three formats carry the whole filtered set (up to 5000 rows); Excel and PDF carry the logo, the company, the title, the date and the filters at the top, with your company's colors. Reports export the same way (the totals row is included).

**“This device cannot share files.”** The phone or tablet has no app that receives files (or the system blocked it). Install or enable a file manager and try again; from the web on a computer the file downloads directly.

## Warehouse client portal: create, edit and cancel (Batches 154–159)
<!-- kemi:portal -->

**How do I create a product?**
In **Warehouse → Products** tap **New product**. The SKU and the name are required; the barcode, the make and the purchase cost are optional. Check “Tracked by lot” if the product carries a lot number and expiration date. The product is created under your client company.

**How do I change a product I already created?**
In the **Products** list, tap **Edit** on the row. You can change the name, the barcode, the make and the cost; if you leave the cost empty the current one is kept. The SKU, the lot or serial control and the unit cannot be changed from the portal.

**I don't see “New product”, “Arrivals” or “Exits”.**
The company turns those options on client by client, and only administrator and operator accesses use them; a read-only access does not see them. Ask your contact at the company to turn them on for you.

**How do I let you know that goods are going to arrive?**
In **Warehouse → Arrivals** tap **New arrival notice**: choose the warehouse (if there is only one it is chosen for you), write your reference (for example your purchase order), the date you expect the arrival if you know it, and add one line per product with the quantity. For a lot-tracked product you can enter the lot and its expiration (the expiration is only entered together with the lot).

**Can I change an arrival notice after creating it?**
Yes, with **Edit**, while it is still expected: you can change the reference, the date and the lines. Once the warehouse has started receiving it (it has an open receipt) it can no longer be edited from the portal; let the company know. Editing does not create another notice: it is the same one.

**How do I cancel an arrival notice?**
With **Cancel notice**, while it is still expected and has no open receipt. It stays as “Cancelled” and cannot be reactivated; if the goods are still going to arrive, create a new one.

**How do I request an exit of my goods?**
In **Warehouse → Exits** tap **New exit notice**: warehouse, reference, destination (pickup, address or note), required date if you have one, and one line per product with the quantity. When you save, the system **reserves right away** whatever is available; what is missing stays pending (“with backorder”).

**Can I change an exit notice?**
Yes, with **Edit**, while it is still “Requested”: reference, destination, date, notes and the quantity of each line. When you change a quantity, the reservation readjusts by itself (if you ask for more, more is reserved from what there is; if you ask for less, the difference is released). Once the warehouse has started dispatching it, it can no longer be edited.

**What happens to what was reserved if I cancel an exit?**
It is released: those units become available again. Cancelling (**Cancel notice**) is only possible while the notice is still “Requested”.

**Why is my “Available” lower than what I have “On hand”?**
The difference is what is **reserved** by your open exit notices: it is set aside for them and cannot be promised twice.

**Can I see or change another client's things?**
No. Each client sees, edits and cancels only their own; what belongs to others does not even appear.

## Kemi: the Teikem assistant, with voice (Batch 218)
<!-- kemi:portal -->

**What is Kemi and how do I use her?**
Kemi is Teikem's assistant mascot. Tap Kemi (bottom right) and choose “Ask me how to use the portal”: type or say your question and Kemi looks for the answer in the manual and the frequently asked questions and shows you the options. She also lets you know when a new message arrives, and you can move her somewhere else by dragging her.

**How do I talk to Kemi instead of typing?**
In the question box tap the microphone, say your question and wait: what Kemi understands appears in the field and, when you finish speaking, she asks by herself. Tap the microphone again if you want to stop listening.

**I don't see Kemi's microphone.**
Your browser or your device cannot recognize speech. On the web it works in Chrome, Edge and Safari (Firefox does not include it); on the phone, with the device's speech recognition. In the meantime, type your question.

**Kemi says “I don't have permission to use the microphone”.**
You blocked the microphone. In the browser, tap the padlock next to the address and allow the microphone for the site; on the phone, turn it on in the device Settings (microphone and speech recognition) for this app. Then tap the microphone again.

**Kemi says “I didn't hear you”.**
She did not detect any voice. Speak after tapping the microphone, close to the device and without loud noise, or type the question.

**Does Teikem store my voice?**
No. The audio is processed by the speech service of your browser or your phone and Teikem only receives the text of the question, exactly as if you had typed it.

**Kemi answers in Spanish even though I have the screen in English.**
The manual and the frequently asked questions are written in Spanish: in English Kemi understands your question, but the answers come out in Spanish.

**Kemi can't answer my question.**
Try other words, tap “No” on “Did that answer your question?” so she searches elsewhere, or write to your contact at the company from the Messages tab. The portal's Kemi only knows what the client can do in the portal; she does not know the company staff's side.

**How do I put Kemi to sleep or wake her up?**
In Kemi's panel tap “Put Kemi to sleep”: she hides next to the menu (on the phone, in the top corner) and shows no bubbles. One tap on that button wakes her. She remembers it even if you reload the page.

**I don't see Kemi.**
Kemi appears for the client's people who are signed in, after accepting the legal document. Consignees with their own access do not have her.

**In Warehouse an arrival notice (or exit notice, or a movement) I know exists does not appear.** Look at that list's date filter: “Arrivals” filters by the **expected** date, “Exits” by the **requested** date and “Movements” by the day of the movement. A notice expected next week does not show with “Today”. Set “All dates” and clear the search box.

**Which number can I search by in Warehouse?** Stock: SKU, name, barcode, lot and warehouse. Products: SKU, name, brand and barcode. Movements: product, barcode, lot, serial and notes. Arrivals: notice number, reference and purchase order. Exits: number, reference, destination and product.
