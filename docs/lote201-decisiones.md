# Lote 201 — Bitácora legible y respuestas de Kemi (2026-10-22)

## Qué se construyó
- `AuditChangesHumanizer` + `AuditQueryService.BuildResolverAsync`: el detalle de la bitácora cambia los campos-llave (`*LookupId`, `StatusCodeId`, `*UserId`, `WarehouseId`, `ClientId`, `ProductId`, `*BinId`, `SupplierId`) por su nombre y su etiqueta legible; `$key` no se muestra, `$reason` sale como «Motivo»; cualquier otro `*Id` sin resolver sale «(sin nombre)». La web ya pintaba «campo: antes → después», así que no cambió.
- `help:index` descarta bloques de código, JSON y llamadas a la API al armar las respuestas de Kemi.

## Cómo se probó
`AuditChangesHumanizerTests` (3) y las pruebas de `features/notifications` (71).

## Decisiones a revisar
- Otros tipos que antes mostraban llaves: usuarios, almacenes, clientes, productos, posiciones y proveedores (ahora con nombre). Una llave de otra tabla se oculta como «(sin nombre)»; si se quiere nombrar más tipos, se agrega a `KindOf`/`BuildResolverAsync`.
- Las bitácoras ya guardadas no se reescriben: se humanizan al leerlas.

## Adenda 2026-10-22 — posiciones y zonas en la bitácora
- Nuevos tipos `WAREHOUSE_ZONE` y `WAREHOUSE_BIN` (sección al final de `logistica-db-update.sql`), con su permiso de lectura (`inventory.view`), de dueño (`warehouse.manage`) y su resolver de pertenencia.
- El humanizador etiqueta los campos comunes (Activo, Eliminado, Eliminado por, Código, Pasillo, Rack, Nivel, Cupo…), trata todo `*By` como usuario y nombra la zona.
- Lo ya guardado como `WAREHOUSE` con el número de una posición no se reescribe; si quieres, se puede reclasificar por los campos que solo tiene una posición (decisión tuya).
- La prueba de la sección `orders.reopen` ahora revisa solo esa sección (fallaba por texto de otra sección posterior).
- Cada renglón de la bitácora nombra QUÉ cambió en vez de «#n»: «Posición de almacén A-01-02 (Zona PK · Almacén Central)», «Zona … (Almacén …)», almacén, plataforma, cliente, producto y proveedor (`AuditLogDto.EntityName`). Lo que no se pueda nombrar sigue con «#n».
