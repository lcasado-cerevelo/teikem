# Translation notes — English edition of the manual

Date: 2026-10-24. Source: the Spanish manual in `docs/manual/` at commit `808124ef` (plus the uncommitted edits of that day to
`41-portal-almacen.md` and `faq.md`). The Spanish manual is the reference; this edition is a translation and is not wired into
the software yet (the help index in `web-app/src/features/notifications/helpIndex.json` still points at the Spanish chapters).

## Layout

- Same folder structure and **same file names** as the Spanish manual (`docs/manual/en/<same path>`), so every link between
  chapters, the FAQ and the screen chapters works unchanged and the future help index can map `docs/manual/X.md` → `docs/manual/en/X.md`.
- Section anchors (`#...`) were rewritten to the English headings (same heading order as the Spanish source).
- `frontend/img/` holds the screenshots: the ones regenerated with the interface in English, and, for the rest, a copy of the
  Spanish capture so no image link is broken. The list of the ones still in Spanish is in [captures-pending.md](captures-pending.md).

## How it was translated

- The text was split by headings into 193 chunks and translated chunk by chunk, with the real English UI texts as the authority:
  the dictionaries `web-app/src/kernel/i18n/en.json`, `app-cliente/src/kernel/i18n/en.json`, `app-almacen/src/kernel/i18n/en.json`
  and the bilingual labels of `Diseño/logistica-db-seed.sql` (statuses, catalog values, permission names).
- Never translated: code, routes, permission codes, status codes, identifiers, file names, product and company names, dates.
- Quoted UI labels and error messages use the English text of the dictionaries when it exists; server messages that only exist
  in Spanish in the API were translated faithfully (same meaning, placeholders and HTTP code kept).
- Reviewed structurally against the source: same number of headings, table rows, code fences, images and links per file.

## Fixed terminology (decisions)

- Lote N (a delivery of the software) → **Batch N**; lote (inventory) → lot. tanda → round (of a batch) / batch (receiving).
- orden de transporte → order; Envíos (screen) → Shipments; pieza → piece; bulto → package; conduce → waybill;
  entrada rápida / detallada → quick / detailed entry; entrega especial → special delivery; recogida → pickup.
- cliente → client (never customer, except where the UI itself says "customer"); consignatario → consignee; chofer → driver;
  compañía → company (the tenant); aparato → device; web del personal → staff web; dueño → owner.
- almacén → warehouse; posición → bin (the UI sometimes says "location"/"slot": quoted labels keep the UI text); plataforma/muelle → dock;
  acomodar → putaway; recolección → picking / pick batch; recibo → receipt (document) / Receiving (screen); aviso de llegada /
  de salida → arrival / exit notice; existencia → stock; cupo → capacity; Kárdex → movement ledger; conteo cíclico → cycle count;
  despacho manual → manual issue; ajuste → adjustment; renta → rental; equipo → unit.
- aviso (a una persona) → notice; Reglas de aviso → Notification rules; campanita → bell; indicador → indicator; gráfico → chart;
  vista → view; informe → report; Pulso del día → Day pulse; Ajustes de la compañía → Tenant settings; marca → branding (company) / make (vehicle).
- cobro → collection; remesa → remittance; liquidación → settlement; comprobante de entrega → proof of delivery; anejo → attachment;
  factura → invoice; nota de crédito → credit note; IVU stays IVU.
- Statuses follow the seed labels: Sometido → Submitted, Recogido → Pickup / Picked up, Sin asignar → Unassigned, Asignado → Assigned,
  En tránsito → In transit, Entregado → Delivered, Intento fallido → Failed attempt, Devuelto → Returned, En almacén → In warehouse.

## Points to decide

- Some exact messages written in code font (`` `…` ``) in the source were left in Spanish by the "never translate code" rule, while
  the same kind of message in quotes was translated. A single rule should be chosen; and note that many API error messages exist
  only in Spanish, so an English-interface user may still see them in Spanish until the API localizes them.
- Chapter 03 title: the source says "Envíos de transporte" and was rendered "Transport shipments"; the README index uses
  "Transport orders". Either is fine; pick one.
- Older screen names that no longer exist in the UI (Ubicaciones, Catálogos, Configuración de la compañía…) were translated
  by hand (Locations, Catalogs, Tenant settings).

## Regenerating the screenshots in English

The Playwright walkthroughs in `web-app/e2e` take the manual's captures. Two environment variables added on 2026-10-24
(`web-app/e2e/manualLang.ts`) make them capture in English without touching the walkthroughs' Spanish locators:

- `MANUAL_LANG=en`: right before each capture the interface is switched to English with the shell's language menu (no reload;
  the SessionProvider refetches the data in English) and switched back to Spanish right after. In the client portal the language
  is stored in localStorage and the page is reloaded.
- `MANUAL_IMG_DIR=<repo>\docs\manual\en\frontend\img\`: the captures go to the English folder instead of the Spanish one.
- `MANUAL_LANG_LOG=<file>`: lists the captures that stayed in Spanish because the screen had no language selector.

Example (API on :5000 against a disposable database, staff web on :3000, portal export served on :8081):

```powershell
$env:MANUAL_LANG='en'; $env:MANUAL_IMG_DIR='F:\Visual Studio 2022\Projects\teikem\docs\manual\en\frontend\img\'
$env:WEB_URL='http://localhost:3000'; $env:API_URL='http://localhost:5000'; $env:CLIENT_APP_URL='http://localhost:8081'
npx playwright test --project=escritorio --workers=2
```

Client portal server for the captures (serves `app-cliente/dist-visual`, built with `npx expo export --platform web --output-dir dist-visual`, and exposes the hook):

```js
// serve-portal.js <dist-visual> 8081
js = js.replace(/(w+).setLang=function((w+)){if(===(w+))return;/, '$1.setLang=window.__setLang=function($2){if($2===$3)return;')
```

The captures of the warehouse app and the mobile apps are taken on a device: they are not regenerated by Playwright.
