# Screenshots of the apps (warehouse and client portal)

Screenshots taken on 2026-10-22 with Playwright, without a device or emulator: the app runs as a web page in the browser against a **simulated** API (sample data, not from any real company).
They are repeated with `npm run export:visual` and `npm run visual` inside `app-almacen/` and `app-cliente/` (see each one's `scripts/visual-check.cjs`). The warehouse one uses a 390×844 phone.

## Warehouse app (device)

| Screen | Screenshot |
|---|---|
| Register this device (first time) | ![](img/almacen-app-01-registrar.png) |
| Who are you? | ![](img/almacen-app-02-quien-eres.png) |
| PIN | ![](img/almacen-app-03-pin.png) |
| Main menu (the blue tiles have something assigned to the person) | ![](img/almacen-app-04-menu.png) |
| My tasks | ![](img/almacen-app-05-mis-tareas.png) |
| Receive: pending notices and orders | ![](img/almacen-app-06-recibir.png) |
| Receive against a notice (what the document says) | ![](img/almacen-app-07-recibir-aviso.png) |
| Receive a part: “Receive a part”, then “Confirm receipt” | ![](img/almacen-app-09-recibir-parte.png) |
| Putaway | ![](img/almacen-app-10-acomodar.png) |
| Dispatch of exit notices | ![](img/almacen-app-11-despacho.png) |

## Client portal (desktop web and phone)

| Screen | Screenshot |
|---|---|
| Sign in | ![](img/portal-14-login.png) |
| Choose password (invitation) | ![](img/portal-11-accept-invite.png) |
| Home (dashboard) | ![](img/portal-01-home.png) |
| Home with another company's branding | ![](img/portal-08-home-brand-carretera.png) |
| Shipments | ![](img/portal-02-orders.png) |
| New shipment, quick entry | ![](img/portal-04-new-order-quick-filled.png) |
| Invoices | ![](img/portal-05-invoices.png) |
| Warehouse | ![](img/portal-06-warehouse.png) |
| Reports | ![](img/portal-13-reports-bystatus.png) |
| Home on the phone | ![](img/portal-09-mobile-home.png) |
| New shipment on the phone | ![](img/portal-10-mobile-new-order.png) |
