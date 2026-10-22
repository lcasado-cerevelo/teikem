# Capturas de las apps (almacén y portal del cliente)

Capturas tomadas el 2026-10-22 con Playwright, sin aparato ni emulador: la app corre como web en el navegador contra un API **simulado** (datos de ejemplo, no son de ninguna compañía real).
Se repiten con `npm run export:visual` y `npm run visual` dentro de `app-almacen/` y de `app-cliente/` (ver `scripts/visual-check.cjs` de cada una). El de almacén usa un teléfono de 390×844.

## App de almacén (aparato)

| Pantalla | Captura |
|---|---|
| Registrar este aparato (primera vez) | ![](img/almacen-app-01-registrar.png) |
| ¿Quién eres? | ![](img/almacen-app-02-quien-eres.png) |
| PIN | ![](img/almacen-app-03-pin.png) |
| Menú principal (los mosaicos en azul tienen algo asignado a la persona) | ![](img/almacen-app-04-menu.png) |
| Mis tareas | ![](img/almacen-app-05-mis-tareas.png) |
| Recibir: avisos y órdenes pendientes | ![](img/almacen-app-06-recibir.png) |
| Recibir contra un aviso (lo que dice el documento) | ![](img/almacen-app-07-recibir-aviso.png) |
| Recibir una parte: «Recibir una parte», luego «Confirmar recibo» | ![](img/almacen-app-09-recibir-parte.png) |
| Acomodar | ![](img/almacen-app-10-acomodar.png) |
| Despacho de avisos de salida | ![](img/almacen-app-11-despacho.png) |

## Portal del cliente (web de escritorio y celular)

| Pantalla | Captura |
|---|---|
| Entrada | ![](img/portal-14-login.png) |
| Elegir contraseña (invitación) | ![](img/portal-11-accept-invite.png) |
| Inicio (tablero) | ![](img/portal-01-home.png) |
| Inicio con otra marca de compañía | ![](img/portal-08-home-brand-carretera.png) |
| Envíos | ![](img/portal-02-orders.png) |
| Nuevo envío, captura rápida | ![](img/portal-04-new-order-quick-filled.png) |
| Facturas | ![](img/portal-05-invoices.png) |
| Almacén | ![](img/portal-06-warehouse.png) |
| Informes | ![](img/portal-13-reports-bystatus.png) |
| Inicio en el celular | ![](img/portal-09-mobile-home.png) |
| Nuevo envío en el celular | ![](img/portal-10-mobile-new-order.png) |
