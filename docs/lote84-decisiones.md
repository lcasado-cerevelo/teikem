# Lote 84 — COD: cobro y reconciliación por hojas escaneadas (P8, 1 de 3) (2026-10-17)

Primer lote de P8. Pedido de Luis: el cobro del COD se captura en la app del chofer **y** en la web; la reconciliación se hace con **hojas escaneadas** cuyo **código de barras es el número de la factura** (el número de referencia de la entrega), y se procesan **subiendo los archivos**, además de un procesamiento manual. No hay archivo de muestra todavía: se construye hasta donde se puede sin él.

## Qué se construyó (servidor)
- Entidades `CodCollection` (la tabla ya existía), `CodReconcileRun` y `CodReconcileItem` (nuevas) y la sección **2026-10-17 (x)** de `update.sql` (columnas `CollectedBy`, `ReconciledBy`, `ReconcileRunId`; tablas de la corrida; EntityType `COD_COLLECTION`).
- `CodService` y `CodController` (`/api/v1/cod`, módulo COD): lista de órdenes con COD, detalle, **cobrar** (parciales, tope de lo pendiente, el COD de la orden avanza paso a paso por StatusService), **reconciliar** por códigos (vista previa `dryRun`, resultados tipados por código, corrida guardada con sus códigos) y consulta de corridas.
- **Desambiguación:** el número de factura se cuenta por cliente y se repite entre clientes (`FAC-00001`). Se resuelve con `clientPublicId` (las hojas son de ese cliente) y, sin él, quedándose con las órdenes con COD y con cobros por cuadrar; si aun así hay varias, el resultado es `AMBIGUOUS`.
- Manual 21, FAQ y este documento. Corrección de `SyncRulesTests` (cubre el endpoint `sync/sales-returns` del Lote 83, que no se había actualizado).

## Cómo se probó
- xunit (`CodTests`, 7): reglas, cobrar (parcial, total, validaciones y mensajes), reconciliar (por factura y por orden, vista previa sin efectos, cobro parcial, hoja repetida, ya cuadrado), corridas guardadas, ambiguo y desambiguado por cliente, lista con filtros y otra compañía, permisos y módulo del controlador. Suite completa: verde.
- Recorrido por la API contra SQL Server real: orden con COD, cobros de 60 y 40 (Parcial → Cobrado), vista previa y corrida (RECONCILED, NOT_FOUND, DUPLICATE), cobros cuadrados, orden en Cuadrado, corridas e historial `PENDING → PARTIAL → COLLECTED → RECONCILED`.

## Decisiones a revisar con Luis
1. **La lectura del código de barras de los archivos se hará en el navegador** (lote siguiente: PDF e imágenes), y al API solo llegan los códigos. Evita guardar archivos (la decisión D10 de dónde guardar archivos sigue abierta) y dependencias nativas en el servidor.
2. **El código de barras es el número de la factura del cliente (`ClientInvoiceNumber`) o el de la orden.** Si en las hojas reales va otro número (p. ej. el de una referencia propia del cliente), se agrega ese campo a la búsqueda.
3. **Un cobro no se anula** en este lote; tampoco se registra desde el POD (P7). El cobro que se equivoque se corrige cuando se construya la anulación.
4. **Cuadrar una orden cobrada en parte** cuadra lo cobrado y deja el COD de la orden en «Parcial».

## Siguen (P8)
- **Lote 85:** pantalla web COD: lista y cobro, y **«Reconciliar» con subida de archivos** (lee los códigos de barras de PDF e imágenes en el navegador, vista previa, confirmar, corridas anteriores, y captura manual).
- **Lote 86:** remesa al cliente (`CodRemittanceBatch`, comisión del contrato, neto, aprobación, exportación con plantillas).
