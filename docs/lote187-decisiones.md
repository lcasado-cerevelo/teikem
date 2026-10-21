# Lote 187 — Series y daño por partes (cierra el punto 2 del pendiente menor, 2026-10-21)

## Qué se construyó
- **Series en tandas:** un producto con serie entra a la tanda cuando la línea tiene una serie por cada unidad recibida; la tanda asienta solo las series NUEVAS (las primeras `PostedQty` de la lista ya están asentadas) y el cierre asienta las que falten (`PlanBatchSerial`, `PlanClose`). Las series ya asentadas no se cambian ni se quitan (400).
- **Daño en tandas:** columna nueva `ReceiptLine.DamageReportedQty` (sección «Lote 187» de `logistica-db-update.sql`; los recibos ya reportados quedan con el total). La tanda que recibe las unidades dañadas crea su reporte DAN (las unidades salen de la posición de recepción) y acomoda solo lo bueno; el cierre solo reporta lo no reportado. Lo declarado es el total acumulado: no puede bajar de lo ya reportado ni sumar más que lo recibido sin asentar.
- **Recibir una parte (`POST /receipts/part`):** ya no rechaza series ni daño; exige una serie por unidad (400). La app de almacén manda series y daño en la parte; la web cuenta esas líneas como entrables a la tanda.

## Cómo se probó
- xunit: `ReceiptBatchTests` (reglas puras de series, recibo con series por tandas, parte sin series rechazada y con series asentada) y `ReceiptDamageTests` (el daño se reporta una vez por tanda y el cierre no lo repite); todos los de recibo, aviso, daño, contratos y SQL congelado en verde (494).
- vitest `receiptBatches`, jest `receiveLogic` (parte con series y daño) y rutas de la app.

## Decisiones a revisar
1. Una línea con serie y series incompletas se salta en la tanda (como antes), salvo en «Recibir una parte», donde es error 400.
2. El daño de una línea de producto con serie sigue sin admitirse (regla existente).
3. Corregir lo ya asentado (serie o daño) se hace con ajuste o reporte aparte, no editando la línea.
4. No se probó contra SQL Server real el cambio de tandas con series (InMemory); la sección SQL es solo una columna con valor por omisión.
