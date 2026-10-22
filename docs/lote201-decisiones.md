# Lote 201 — Bitácora legible y respuestas de Kemi (2026-10-22)

## Qué se construyó
- `AuditChangesHumanizer` + `AuditQueryService.BuildResolverAsync`: el detalle de la bitácora cambia los campos-llave (`*LookupId`, `StatusCodeId`, `*UserId`, `WarehouseId`, `ClientId`, `ProductId`, `*BinId`, `SupplierId`) por su nombre y su etiqueta legible; `$key` no se muestra, `$reason` sale como «Motivo»; cualquier otro `*Id` sin resolver sale «(sin nombre)». La web ya pintaba «campo: antes → después», así que no cambió.
- `help:index` descarta bloques de código, JSON y llamadas a la API al armar las respuestas de Kemi.

## Cómo se probó
`AuditChangesHumanizerTests` (3) y las pruebas de `features/notifications` (71).

## Decisiones a revisar
- Otros tipos que antes mostraban llaves: usuarios, almacenes, clientes, productos, posiciones y proveedores (ahora con nombre). Una llave de otra tabla se oculta como «(sin nombre)»; si se quiere nombrar más tipos, se agrega a `KindOf`/`BuildResolverAsync`.
- Las bitácoras ya guardadas no se reescriben: se humanizan al leerlas.
