# Lote 198 — Recibir y despachar varios, tomar = asignarse (2026-10-21)

## Qué se construyó
- Servidor: `AuditNote` en el DbContext → el interceptor anota `$reason` en el AuditLog; `TaskTakeRequest(Origin)` opcional en los `take` de aviso de llegada, aviso de salida, orden de compra y orden de venta; el inicio de una tarea sin dueño también anota el porqué.
- App: Mis tareas refresca desde el servidor, conteos coherentes; Recibir con selección múltiple y carga automática (`receiveQueue.ts`); Despacho con lista única, selección múltiple y tomar al empezar (`dispatchTake.ts`); Acomodar con etiquetas de asignación; se quitan «Recibir juntos», «Recolectar juntos» y las entradas sueltas de Inicio.
- Web: Asignar/Tomar visibles en un aviso de llegada con recibo parcial.

## Cómo se probó
Pruebas xunit (`DocumentAssignmentTests`), jest (rutas de Recibir con tanda, `dispatchTake`, `putawayLogic`), vitest de warehouse.

## Decisiones a revisar
- «Mismo producto en dos documentos se llena en orden»: hoy se reciben los documentos uno tras otro (cada uno con lo suyo), no un solo recibo sobre varios documentos.
- Los «Volver» siguen rojos (convención de Luis).
- Crear avisos de llegada/salida desde el aparato no existe ni se agrega.
