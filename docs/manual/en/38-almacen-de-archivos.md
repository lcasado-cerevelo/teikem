# 38 — File store: signature, photos and documents

> Batch 127 (2026-10-18, dictated by Luis): the recipient's signature, the photos taken in the mobile app and the documents are stored as **encrypted files** on the server, and only the **reference** stays in the database. First use: the proof of delivery.

## 1. What it does
- Each file belongs to an **owning entity** (today the proof of delivery; tomorrow the order, the vehicle, the driver, a damage…), with its **kind**: `SIGNATURE` (signature), `DELIVERY_PHOTO` (delivery photo), `DAMAGE_PHOTO` (damage photo) or `DOCUMENT`.
- The database (`StoredFile`) keeps: owner, kind, name, type, size, **SHA-256 hash**, who uploaded it, when, and the key in the store. The **content** lives in the server's file store (folder `Files:Root`, by company and month), **encrypted with AES-256-GCM** with a per-company key derived from the master key (`Files:MasterKey`): a file copied from the disk is useless, and one company's key does not open another's files.
- **There is never a link to the disk:** the content is served through the API (`GET /api/v1/files/{publicId}`) with the session and the owner's read permission. Without a session, 401.
- The **real type** is checked by content (not by what the file says): JPG, PNG, WEBP, GIF or PDF. Limit: `Files:MaxMegabytes` (10 by default). The name is cleaned (no folders or odd characters) and takes the extension of the real type.
- **Remove** is logical: the file stops being listed and served; the content is not deleted from the store.

## 2. Who can
The permission is decided by the file's **owner**. For the proof of delivery: viewing its files requires `orders.view`; attaching and removing, `orders.deliver`. Module `LTL_GROUND` (the order's).

## 3. How to use it
- **Order record → Proof of delivery → “Signature and photos”:** thumbnails of the images (they open enlarged) and links for the PDFs. With `orders.deliver`: **Add photo** and **Remove**.
- **Driver app (P7):** it will upload the recipient's signature (`SIGNATURE`, and the proof will change to “Signed by the recipient”) and the photos (`DELIVERY_PHOTO`) through the same endpoints.
- **Migration:** migrated proofs can bring their signature and photo as files in the store (they are attached to the migrated proof with the `StoredFileService.AttachAsync` service).

| Method and route | Permission | What it does |
|---|---|---|
| `POST /api/v1/orders/{publicId}/delivery/files` (multipart: `kind`, `file`) | `orders.deliver` | Attaches to the proof. 422 “Order X does not have its delivery recorded; record the delivery before attaching files.” · 400 “Attach a file.” · “Only images (JPG, PNG, WEBP, GIF) and PDF are accepted.” · “The file content does not match its type.” · “The file kind must be SIGNATURE, DELIVERY_PHOTO, DAMAGE_PHOTO or DOCUMENT.” · “The file cannot exceed N MB.” · 422 “The file store is not configured (Files:MasterKey); contact support.” |
| `GET /api/v1/orders/{publicId}/delivery` | `orders.view` | The proof includes `files[]` (publicId, kind, fileName, contentType, sizeBytes, createdAtUtc, uploadedByName). |
| `GET /api/v1/files/{publicId}` | owner's read permission | The content, inline (`Content-Disposition: inline`), private cache of 5 min. 404 if it does not exist, does not belong to the company or was removed. |
| `DELETE /api/v1/files/{publicId}` | owner's write permission | Removes it logically (204). |

## 4. Configuration (support)
`appsettings`: `"Files": { "Root": "App_Data/files", "MasterKey": "<32 bytes en base64>", "MaxMegabytes": 10 }`. In production the master key goes in the server configuration (variable `Files__MasterKey`), **never in the repository**, and it must be **backed up**: without it the files cannot be read. The `Files:Root` folder is included in the server backup together with the database. Without a key, the API starts but rejects uploads with the message above.

## 5. Frequent cases
- **“The file store is not configured”.** `Files:MasterKey` (32 bytes in base64) is missing on the server.
- **The thumbnail does not load.** The content is requested with the session; if it expired, sign in again. If the file was removed, it disappears from the list.
- **I uploaded a .txt renamed to .png.** It is rejected: the type is decided by the content.
