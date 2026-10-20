# Lote 156 — El cliente crea desde su portal (Lote E, 2026-10-20)

## Qué se construyó
- Tres banderas por cliente (`Client.PortalCanCreateProducts/Arrivals/Exits`, apagadas por omisión; sección idempotente en `logistica-db-update.sql`), editables en la ficha del cliente (web, con el módulo de almacén).
- `PortalCreateService` + `PortalCreateController` (`/portal/warehouse/{warehouses,products,arrivals,exits}`): delegan en `ProductService`, `AsnService` y `ExitNoticeService` (mismas reglas, numeración, reservas, historial y avisos), siempre a nombre del cliente de la sesión; solo ADMIN/OPERADOR; lo ajeno responde 404.
- `capabilities` de `/portal/me` trae `canCreateProducts/Arrivals/Exits` (bandera del cliente + rol + módulo).
- App del cliente: «Nuevo producto» en Productos y pestañas «Llegadas» y «Salidas» (lista, crear, cancelar), con las mismas reglas del servidor.

## Cómo se probó
`PortalCreateTests` (bandera apagada, solo lectura, producto propio, llegada y salida propias, lo ajeno no existe); suite .NET completa; jest de la app (144) y `npm run check` de la web.

## Decisiones a revisar
- Las banderas son por cliente (no hay valor por omisión de la compañía): es lo más simple y lo que pidió el dueño («por cliente/compañía»); si se quiere un valor general de la compañía se agrega después.
- El cliente puede escribir el costo de compra de sus productos (alimenta el valor del inventario/seguro); un precio en un aviso de llegada solo llena el costo del producto si no tenía.
- No hay paginación en la lista de llegadas (máximo 100, las más nuevas).

## Fuera de este lote
Editar un producto o un aviso ya creado; adjuntar documentos; capturas Playwright.
