# Lote 152 — Permiso de acceso por ítem del menú (pedido de Luis, 2026-10-20)

## Qué se construyó
- **44 permisos de acceso, uno por ítem del menú, en la categoría nueva «Acceso al menú» (`MENU`)**, que aparece **primero** en la pantalla de roles y de permisos extra de un usuario, **en el orden del menú**. 16 ya existían y servían (`orders.view`, `trips.scan`, `trips.view`, `claims.view`, `rental.view`, `sales.view`, `purchasing.view`, `billing.view`, `cod.view`, `driverpay.view`, `clients.read`, `locations.read`, `fleet.view`, `integrations.view`, `notifications.send`, `admin.audit`) y 28 son nuevos (`pulse.view`, `dispatch.view`, `warehouses.view`, `bins.view`, `products.view`, `suppliers.view`, `notices.view`, `receipts.view`, `pickbatches.view`, `adjustments.view`, `counts.view`, `crossdock.view`, `kardex.view`, `purchaseaccounting.view`, `dispatchaccounting.view`, `tax.view`, `drivers.view`, `reports.view`, `customfields.view`, `indicators.view`, `charts.view`, `printers.view`, `users.view`, `devices.view`, `catalogs.view`, `notifications.view`, `jobs.view`, `settings.view`). Cada etiqueta dice «<Ítem>: entrar y ver».
- **Reglas** (`MenuPermissions`, lógica pura): *editar ⇒ ver* (quien crea, edita o ejecuta en una pantalla entra a ella; p. ej. `printers.manage ⇒ printers.view`), *ver ⇒ leer los datos* (p. ej. `kardex.view ⇒ inventory.view`, para que la pantalla no salga vacía ni con 403) y *ver no ⇒ editar*. `ExpandImplied` ahora itera hasta que no cambie nada.
- **Nadie pierde un ítem:** las plantillas de rol y, en el SQL, **todos los roles ya existentes** reciben los permisos nuevos que reemplazan al que antes daba la entrada (`inventory.view` ⇒ los 10 de Almacén; `purchasing.view` ⇒ Proveedores y Contabilización de compras; `printers.manage` ⇒ `printers.view`; etc.). `pulse.view` lo reciben todos los roles. Solo agrega.
- **Web:** el menú y las rutas usan el permiso de acceso de cada ítem (y sus fichas). Las consultas del servidor siguen con sus permisos de datos.
- **Contabilidad** quedó: Compras, Contabilización de compras, Ventas, Contabilización de despachos (la baja de inventario, el costeo y la exportación de lo que sale por despacho, ventas y avisos de salida), Facturación, Procesar entregas, Liquidación a choferes, Impuestos.

## Cómo se probó
- xunit `MenuPermissionsTests` (44 permisos primeros y en orden, orden de Contabilidad, reglas que solo mencionan códigos existentes, editar⇒ver⇒leer sin dar editar, plantillas sin perder pantallas), conteo del catálogo (138), espejo en el SQL y plantillas; suite .NET completa verde.
- vitest: menú completo por ítem y permiso, ruta de Usuarios, pantalla de inicio con `pulse.view`; `npm run check` verde.

## Decisiones a revisar
1. **Ver ⇒ leer los datos:** `products.view` (y los demás de Almacén) incluyen `inventory.view`, con sus costos. Si se quiere un acceso que no lea datos, hay que separar la lectura.
2. Los endpoints **no** exigen todavía el permiso de acceso (siguen con los de datos): el permiso gobierna menú y ruta de la web. Endurecer cada endpoint es un paso aparte.
3. «Mensajes de clientes» quedó sin ítem de menú; conserva `clientmessages.reply`.
4. El SQL no se probó contra SQL Server (simulación de `db-update` pendiente).
