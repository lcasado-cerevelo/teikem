# L212 — Messaging on the web: photos, talking about an order, owners and settings

> Screenshots pending: they are taken with the Playwright walkthrough against the real API (this batch was verified with Vitest tests over a simulated API). The screens are those of [L53](l53-mensajes.md) plus what is added below. The server behavior is in [chapter 15](../15-mensajeria.md) (§8 and §9).

## 1. In the conversation (`/messages`)
- **Phone numbers, e-mails and web addresses are links**: the phone opens the dialer (on a phone, with the number filled in), the e-mail opens the mail app and the web address opens in another tab. The message is saved as is.
- **Order or route card**: if the message talks about an order or route, above its text a card appears with the number (or code), the client (or the zone) and the status. **Tapping it opens the file** of the order or the route in Dispatch. Whoever has no permission to view orders or routes only sees the number, without a link.
- **Photos**: the **📷** button next to the writing box attaches up to **4 photos** (JPG, PNG or WEBP). They are **shrunk in the browser** before uploading and are shown as thumbnails with an **✕** to remove them before sending. A message can be **photos only**. In the thread, tapping a thumbnail opens it in full.
  - If the photo was already **deleted** or **archived** (they live 30 days by default), “Photo deleted” or “Photo archived” is shown in its place; the message text is still there.
  - Error messages: “Only JPG, PNG or WEBP photos are accepted.”, “A message can carry up to 4 photos.”, “The photo could not be read.” If sending fails, **neither the text nor the photos are lost**.

## 2. Talking about an order or a route
- In the **order file**: the **Messages** panel (right column) lists the messages of *your* conversations that talk about that order (tapping one opens the conversation) and its 💬 **Talk about this order** button.
- In **Dispatch → active route**: the 💬 button in the route header.
- It opens **New message** with “About 2026-0001” and, at the top, the **Suggested**: the route's owner, the zone's owner, the dispatchers and the recent conversations (each one says where it comes from). Tapping a suggestion adds it as a recipient; a suggestion that is a **dispatcher group** is sent to the “live” group of that role (only one per company). Below it the usual people search box continues.
- The first message carries the order or route card.

## 3. Owners of a route or a zone
- In **Dispatch → active route**: the “Owners: …” line with the ✎ button (with `trips.plan`). A new route is born with whoever created it.
- In **Fleet → Dispatch zones**: the **Owners** row action (with `fleet.manage`).
- The dialog lists the owners with an ✕ to remove them and a search box to add; **Save** leaves exactly that list. Messages: “Owners saved.”; without permission, “You do not have permission to change the owners (trips.plan).” (or `fleet.manage`).

## 4. Who takes part: “Messaging” column in System → Users
- Shows per person **Full**, **Office only** or **No messaging** (with `admin.users`). The **Messaging: …** row actions switch to the requested mode (they do not repeat the one it already has). Users linked to a driver are left in “Office only”.

## 5. How long photos live: Tenant settings → Operations → “Messaging: chat photos”
- **Days a photo lives** (1 to 3650, 30 by default) and **When it expires**: “Delete the photo” (the content is removed from the server) or “Archive (hide and keep)”. With `admin.tenant`; without it, it is read-only. Validation: “Days must be between 1 and 3650.”
- The text of the messages is never deleted. Evidence (delivery, exception, damage) does not go in the chat.

## 6. Phone numbers that can be called
- The phone numbers of the consignee record, of the client's people and of the contacts are `tel:` links (with the country code). On a computer without calling, the system decides what to do with the link.
