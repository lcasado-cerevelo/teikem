# L213 — Client portal: fixed footer, date filter, New shipment same as the staff's, desktop Messages and selecting several consignees

Screenshots from the Playwright verification of 2026-10-23 (`app-cliente`, `npm run visual`; simulated API). Functionality: chapters [27](../27-app-cliente.md), [30](../30-nueva-orden-del-cliente.md), [34](../34-avisos-al-consignatario.md), [35](../35-acceso-del-consignatario.md) and [37](../37-portal-facturas-avisos-comprobante.md); decisions in [lote213-portal-decisiones.md](../../lote213-portal-decisiones.md).

## Home with “Today” and the footer stuck to the bottom
![Home](img/l213-inicio-hoy-pie.png)
The indicators' period offers **Today** in addition to 7, 30 and 90 days. The footer (Privacy · Terms, rights, Powered by) is a real page footer: full width and stuck to the bottom edge when the content does not fill the window.

## My shipments with date filter and total
![My shipments](img/l213-envios-filtro-fecha.png)
Range dropdown (here on **Custom**, with From/To from the browser's calendar) next to the search box; the table ends with **“9 of 9”**.

## New shipment — Quick (wide web), all with the keyboard
![Quick](img/l213-rapida.png)
![Quick walked through with the keyboard](img/l213-rapida-teclado.png)
The staff's columns (Date, Invoice if the client assigns it, Consignee, Address · Town, Package, COD, Service, Comment). The second screenshot was made **without a mouse**: Tab to the consignee, “farm” + Enter (picks it and the focus moves to the Package dropdown), ↓ (switches to “Envelope”), Tab, “40” in COD, Tab to the Service dropdown, ↓ (“Express”) and **Enter on the dropdown saves the row**: it was saved (green, with Envelope, $40.00 and Express) and the new one waits with the same date, service and package; in the search box the list is shown with the consignee's number and “+ New”.

## New shipment — Detailed (wide web and phone)
![Detailed](img/l213-detallada.png)
![Detailed on the phone](img/l213-detallada-celular.png)
Steps 01 Service and dates, 02 Consignee (search box with autocomplete and read-only Number, Address, Town, ZIP code and Phone) and 03 Packages and COD, with the **Shipment ticket** on the right. On the phone it is the only way to capture.

### What was captured: From/To range, search and summary in the footer
![Quick with range and summary](img/l213-portal-rapida-rango-resumen.png)
The table below the quick row shows a **From/To range** (both “today” when opening; a captured date further ahead or older widens it by itself) and you can search in any data; the footer summarizes “N × package · service”, **Total packages**, **Distinct invoices** (each comma in an invoice field is another number) and the total COD of what is visible. Same in the staff web (`img/l213-personal-rapida-rango-resumen.png`). Verified with the real API in `e2e/lote213-capturado-filtros-resumen.spec.ts`.

### My shipments: failed attempts pill
![My shipments with failed attempts](img/l213-portal-envios-intentos-fallidos.png)
A shipment that went out on the street, was not delivered and came back to the warehouse no longer looks like a “delayed” one: under its status it carries an **amber pill with “1 failed attempt” / “2 failed attempts”**. It shows in the My shipments and Home tables, and in the progress bar of the phone cards, the shipment file and the consignee space. Finished orders (delivered, returned, cancelled) do not carry it, and neither do those that never failed. In the table's Excel/CSV/PDF the status comes out with the count (“Unassigned · 1 failed attempt”).

## One view per device (2026-10-24)
| Computer (web) | Tablet (1194 px, touch) | Phone |
|---|---|---|
| ![Computer](img/l213-computadora-nuevo-envio.png) | ![Tablet](img/l213-tableta-nuevo-envio.png) | ![Phone](img/l213-celular-nuevo-envio.png) |

The **web on a computer** (fine pointer, 900 px or more) sees the wide form: top bar, tables and “New shipment” with the Quick entry. The **installed app, tablets and phones** (also in the browser) see the mobile form: tabs, cards and “New shipment” detailed only, even if the tablet measures more than 900 px. Verified with Playwright emulating a touch tablet (`e2e/lote213-vista-por-dispositivo.spec.ts`).

**Export is the same everywhere: Excel, CSV and PDF** (web, tablet, phone, installed app and Reports). In the browser the file is downloaded; in the installed app it is saved and “Share” opens (requires the new APK with `expo-print`, `expo-sharing` and `expo-file-system`).

## Exporting what is filtered
![Export in My shipments](img/l213-envios-exportar.png)
![Export in My warehouse](img/l213-almacen-exportar.png)
Every table ends with its total (“9 of 9”) and **Export**: it downloads a CSV with all the rows that meet the current filter, as in the staff web. On the phone it is a button under the search box.

## Exporting to Excel, CSV and PDF — with the company's logo or Teikem's
![PDF with the Teikem logo](img/l213-pdf-con-logo-teikem.png)
![PDF with the company's logo](img/l213-pdf-con-logo-compania.png)
![Filtered PDF](img/l213-pdf-filtrado.png)
A company with no logo uploaded comes out with Teikem's (top); with a logo uploaded, with its own (second). Below: the company, the title, “Generated on …” and the filters. The Excel carries the same header. The third image is a PDF exported with the search box filled in: only the filtered rows.

### With the company's branding
![Portal PDF with the “Vino” branding](img/l213-pdf-portal-marca-compania.png)
![Company web PDF with the same branding](img/l213-pdf-personal-marca-compania.png)
The table header, the title and the bottom line come out in the colors the company defined (here the “Vino” branding); without its own logo, Teikem's. The same in the portal and in the company's web app.

## New consignee on the same screen, without a mouse
![Quick: new consignee](img/l213-consignatario-en-linea-rapida.png)
![Quick: saved, the consignee now exists in the list](img/l213-rapida-guardada.png)
![Detailed: new consignee](img/l213-consignatario-en-linea-detallada.png)
Typing a name that does not exist + Enter opens the block (the cursor lands on what is missing); Enter saves the consignee on the server, it stays chosen and the cursor continues in the shipment. Second image: after saving the shipment, typing the name again already finds it in the list.

### The same in the staff web
![Staff, quick](img/l213-personal-rapida-consignatario-en-linea.png)
![Staff, quick saved](img/l213-personal-rapida-guardada.png)
![Staff, detailed](img/l213-personal-detallada-consignatario-en-linea.png)
![Staff, detailed saved](img/l213-personal-detallada-guardado.png)
Name, address, town (the town and ZIP selector) and optional phone; Enter saves; the focus continues on the Package (quick) or on the type of the first package (detailed).

## Messages on the wide web
![Messages](img/l213-mensajes.png)
Two columns: conversations on the left (Customer service and the consignees with what is unread) and the open conversation on the right, the same width as its header.

## My consignees — selecting several
![Select several](img/l213-consignatarios-varios.png)
Table with a checkbox per row, “Select the 4 shown”, action bar and the warning **only once** for the group (“This applies to 4 consignees (email)…”).
