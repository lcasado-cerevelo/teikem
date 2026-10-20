# Lote 143 — Aviso de salida: servidor (4a) (pedido de Luis, 2026-10-20)

## Qué se construyó
- **Documento nuevo `ExitNotice` (SAL-#####)** con líneas (`ExitNoticeLine`: pedido, reservado, pendiente, despachado, cerrada) y reservas (`ExitNoticeReservation`), en una sección nueva al final de `Diseño/logistica-db-update.sql` (idempotente). Estatus `REQUESTED → PARTIAL → SHIPPED` y `CANCELLED` (PARTIAL y SHIPPED laterales, como en ventas, para poder reabrir).
- **Servicio `ExitNoticeService`** (patrón de las órdenes de venta, sin precio/impuesto/crédito/kits/series): alta que reserva con la rotación de la compañía y deja el resto pendiente (nunca se rechaza por falta de existencia); solo productos activos del cliente dueño; modificar la cantidad de una línea; cancelar (libera exactamente lo reservado); pendiente: ver, reservar una línea o todas, cerrar con faltante; **despachar contra el aviso** (consume la reserva, crea la recolección enlazada `PickBatch.ExitNoticeId`/`PickBatchLine.ExitNoticeLineId`, mueve la reserva si se escoge otra posición, salvo con la rotación en «Obligar»); **deshacer una recolección** (gancho `IExitNoticeDispatchReversal`, llamado por `PickBatchService`) reabre el aviso y vuelve a reservar. Una recolección de un aviso **no se empaca**.
- **API** `/api/v1/exit-notices` (9 rutas), módulo `WMS_LOTSERIAL`, permisos `inventory.view` (ver), nuevo `exitnotice.create` (crear/modificar/cancelar/pendiente) y `warehouse.pick` (despachar). `exitnotice.create` va a TenantAdmin y a los roles ya clonados de Operador de almacén en la misma sección SQL.
- **Trabajo `EXIT_NOTICE_BACKORDER_SCAN`** (cada 10 minutos): reserva el pendiente, el aviso más antiguo primero, y publica un aviso por documento.
- **Cuatro eventos de aviso** (`EXIT_NOTICE_REQUESTED`, `…_BACKORDER_RESERVED`, `…_SHIPPED`, `…_CANCELLED`), sin reglas sembradas.
- `web-app/openapi.json` y el `schema.d.ts` de la web traen las rutas y los DTO nuevos (el de `app-almacen` se regenerará en el lote 146).

## Cómo se probó
- xunit `ExitNoticeTests` (30): reglas puras, catálogo/trabajo/permiso, alta que reserva todo o parte o nada, validaciones con mensaje exacto (dueño, serie, repetido, cantidad, cliente, líneas), rotación FEFO por lote, pendiente reservado solo y sin repetir aviso, modificar subiendo y bajando, cancelar y no poder cancelar uno despachado, despacho parcial y total (existencia, reserva, recolección enlazada, estatus), mover la reserva a otra posición y rechazarlo con la rotación obligatoria, cerrar con faltante, reservar el pendiente, deshacer un despacho y la lista con filtros. Más los cuatro catálogos de permisos (109) y la prueba que vigila que solo el ledger escribe saldos.
- Suite completa de .NET: 4862 pasan. No se corrió contra SQL Server (el script nuevo queda por validar con `db-update` en simulación).

## Decisiones a revisar
1. **Reserva por saldo, vista por lote** (decidido con Luis): el aviso muestra producto/lote/cantidad; el ledger reserva por posición; al despachar se puede mover la reserva.
2. **Backorder automático**: el trabajo lo reserva solo (ventas solo avisa). Un aviso nunca se rechaza por falta de existencia.
3. **Series y kits** quedan fuera de la primera versión (mensajes 422 explícitos).
4. **Un despacho de un aviso no se empaca** (no es una entrega).
5. Falta (a propósito, para los lotes 144–146): pantallas web, el selector «Contra» en Recolección/Despacho manual y el ajuste «el despacho exige documento», la app de almacén, y la fuente de datos para Vistas (`EXIT_NOTICE`).
6. En las pruebas con la base en memoria el buscador distingue mayúsculas; en SQL Server no.
