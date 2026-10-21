# Lotes 182-183 (C4) — Recepción maestra («Recibir juntos») (trabajo autónomo, 2026-10-21)

Pedido de Luis (2026-10-21): hacer C4 «sin mi supervisión» con la misma regla del despacho (un solo dueño, se arma al abrir, no se suman documentos después). Servidor y pruebas: Lote 182; pantallas web: Lote 183.

## Qué se construyó
- **Recepción maestra `ReceiptGroup`** (`RCM-#####`, contador `RECEIPTGROUP`; tabla nueva y columna `ReceiptHeader.ReceiptGroupId` en `Diseño/logistica-db-update.sql`, sección 2026-10-21 Lote 182). Reúne de 2 a 20 documentos —avisos de llegada y/o órdenes de compra— **del mismo dueño y almacén** que llegan juntos. **Cada documento conserva su recibo de siempre** (`REC-#####`: líneas, tandas, estatus, acomodo, costo, cruce de muelle): la maestra los abre juntos con el mismo transporte y referencia, muestra el avance y los **confirma juntos** (todo o nada). Mismo patrón que la recolección maestra (`PickGroup`, Lote 176): un envoltorio sobre lo que ya existe, sin tocar el modelo del recibo.
- **`ReceiptGroupService`** + 5 endpoints en `ReceiptsController` (`/receipts/groups`: lista, ficha, abrir, confirmar todo, eliminar). Dueño distinto (un cliente, o lo propio) u otro almacén → 400; un documento que no se puede recibir → el error de siempre de ese recibo y **nada queda abierto**. Eliminar: solo con ningún recibo confirmado (cada recibo se elimina con sus reglas de siempre).
- **Web:** pestaña «Maestros» en Recibo (lista con filtros y exportación), «Recibir juntos» (avisos esperando; bloquea otro dueño u otro almacén), ficha con sus recibos («Abrir recibo» lleva a la pantalla de siempre), «Confirmar todo» y «Eliminar».
- Manual 44 + L183 con capturas + FAQ.

## Cómo se probó
- xunit `ReceiptGroupTests` (5: validación de la selección y estatus derivado; abrir con transporte compartido y confirmar juntos con existencia correcta y 422 de repetir/eliminar; eliminar una abierta libera los avisos y se puede abrir otra; otro dueño/repetido/uno solo/nota larga sin dejar nada; lista por estatus y búsqueda por número de un documento) y `ReceiptGroupSqlTests` contra **SQL Server real** (las subconsultas anidadas de la búsqueda). `WmsControllerSecurityTests` (184 acciones) y el mapa de auditoría actualizados.
- `scripts/smoke-recepcion-maestra.sh` contra el **API real** (agregado al CI): mezcla de dueños, uno solo y repetido → 400; abrir A1 + A2 con transporte y referencia compartidos; un aviso ya en una maestra → 409; confirmar juntos (existencia 10); confirmar o eliminar de nuevo → 422; eliminar una abierta y volver a abrir. De paso `db-update` aplicó la sección nueva sobre una base con datos.
- vitest (8: reglas de selección y la pestaña: lista, «Recibir juntos» que pide dos y bloquea otro dueño/almacén, ficha, abrir recibo, confirmar todo, eliminar, sin permiso); recorrido Playwright `lote182-recepcion-maestra.spec.ts` (escritorio y móvil) con capturas.

## Decisiones a revisar con Luis
1. **Es un envoltorio, no un recibo con varios documentos dentro.** Se descartó cambiar `ReceiptHeader.AsnId` (único por aviso en 100+ lugares: sincronización de aparatos, citas de muelle, actividad, analítica, avisos) por el riesgo; a cambio, **se captura recibo por recibo** (la maestra no reparte una captura única entre documentos). Si se quiere capturar «por producto» para todo el camión y que el sistema reparta entre los avisos, es un lote aparte.
2. **Solo la web.** La app de almacén recibe documento por documento; «recibir juntos» desde el aparato no está. Tampoco hay aún enlace de la maestra en «Llegadas y salidas» ni en la lista de recibos (el recibo no muestra a qué maestra pertenece).
3. La pantalla ofrece **avisos de llegada**; las **órdenes de compra** se pueden mandar por la API (`purchaseOrderPublicId`) pero no se eligen en la pantalla todavía (una orden de compra crea su aviso al recibirse).
4. «Confirmar todo» es **todo o nada** y el mensaje de error es el del recibo que falló (sin decir cuál; se corrige desde «Abrir recibo»).
5. La maestra **no tiene bitácora propia** (la de cada recibo es la de siempre), como la recolección maestra.
