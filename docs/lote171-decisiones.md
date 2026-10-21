# Lote 171 — Asignar el aviso de llegada y el aviso de salida (B2; pedido de Luis, 2026-10-21)

## Qué se construyó
- **Servidor:** `Asn.AssignedToUserId` y `ExitNotice.AssignedToUserId` (FK a usuarios, sección nueva e idempotente al final de `Diseño/logistica-db-update.sql`). `POST /asns/{id}/assign` y `/exit-notices/{publicId}/assign` (`warehouse.manage`, `{ userId }`, `null` = quitar) y `POST …/take` (`warehouse.receive` / `warehouse.pick`). Reglas en `DocumentAssignmentRules`. Los DTO traen `assignedToUserId` y `assignedToName`; las listas aceptan `assignedToMe` y `assignedUserId`; la sincronización del aparato (`SyncAsnDto`, `SyncExitNoticeDto`) lleva `assignedToUserId`.
- **Web:** columna «Asignado a», botones **Asignar** y **Tomar** (fila del aviso de llegada; ficha del aviso de salida) y filtro «Solo los asignados a mí».

## Cómo se probó
- xunit `DocumentAssignmentTests` (8: asignar y quitar, solo miembros activos, solo abiertos, tomar y el 409, filtro «a mí» en las dos listas) + contratos posicionales y mapa de seguridad (171 acciones).
- vitest `ReceiptScreen.test.tsx` y `ExitNoticesTab.test.tsx`; tipos y lint de `features/warehouse`.

## Decisión de diseño a revisar (la importante)
**Lo asignable es el DOCUMENTO por atender, no el recibo ni la recolección.** Un recibo y una recolección **nacen al ejecutarse** (la app crea el recibo al confirmar; la recolección nace al sacar), así que no existen todavía cuando alguien tiene que ser avisado. Por eso se asigna el **aviso de llegada** (para recibir) y el **aviso de salida** (para surtir). Las tareas de acomodo ya se asignaban (y «a nivel del recibo» desde el Lote 159). Quedan fuera por ahora: la orden de compra (se recibe contra ella) y la orden de venta.

## Qué quedó fuera
- Aviso al asignar (notificación a la persona): el motor de avisos es por reglas; queda para cuando «Mis tareas» esté en la app.
- «Mis tareas» en la app de almacén (B1): lote siguiente.
