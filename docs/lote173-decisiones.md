# Lote 173 — Recolección y empaque por documento (C1; pedido de Luis, 2026-10-21)

## Qué se construyó
- **Servidor:** `GET /api/v1/pick-batches/documents` (`PickBatchDocumentService`): una fila por aviso de salida, orden de venta o envío empacado con su avance (pedido, reservado, sacado) y sus recolecciones como movimientos. Una sola consulta unida (UNION) paginada y ordenada en la base, con filtros `kind`, `status`, `search`, `pendingOnly`. No cambia datos ni estructura (sin SQL nuevo).
- **Web:** selector «Por documento / Por movimiento» en la lista de Recolección y empaque (por omisión, por documento; `?view=moves`). Fila con tipo, cliente, estatus, avance (texto, barra, «Por sacar», «Sin reservar»), movimientos y última recolección; al abrirla, sus recolecciones, y cada una abre su ficha con Empacar/Eliminar. Tabla paginada con `exportRows`.

## Cómo se probó
- xunit `PickBatchDocumentTests` (6: avance y movimientos más reciente primero, abierto con reservado aunque no tenga movimientos, orden de venta y envío en su fila, un despacho manual sin documento, prioridad aviso de salida > orden de venta y las eliminadas no cuentan, filtros y paginación, 400 de tipo) y `WmsControllerSecurityTests` (172 acciones). **Contra SQL Server real** (`PickBatchDocumentSqlTests`, base desechable, se omite sin `TEIKEM_TEST_SQLSERVER`): la unión y sus subconsultas se traducen y devuelven lo mismo que en memoria.
- vitest `PickDocumentsPanel.test.tsx` (4) y `pickDocumentView.test.ts` (5); las pruebas de la pantalla de recolecciones pasan ahora a `?view=moves`; tipos y lint.

## Decisiones a revisar
1. **Por omisión se ve «Por documento»** (lo que se pidió); «Por movimiento» queda a un clic y conserva todos los filtros.
2. **Una recolección cuenta para un solo documento** (aviso de salida > orden de venta > envío), para no contarla dos veces.
3. **«Sin reservar»** = pedido − sacado − reservado (espera inventario); un envío no tiene pedido y muestra solo lo recolectado.
4. Un documento cancelado sin recolecciones o abierto sin nada reservado **no sale** (no hay nada que sacar).

## Qué quedó fuera
- **La lista de recibos agrupada por aviso:** hoy un aviso tiene un solo recibo (1:1), así que agrupar no cambia nada; llega con C2 (recibo con tandas).
- Filtro por estatus del documento en la pantalla (el API ya lo acepta) y por almacén.
