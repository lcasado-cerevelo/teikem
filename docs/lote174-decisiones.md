# Lote 174 — Recibo con tandas (C2, servidor + web; pedido de Luis, 2026-10-21)

## Qué se construyó
- **Estructura** (sección «Lote 174» al final de `Diseño/logistica-db-update.sql`, idempotente): `ReceiptLine.PostedQty` (lo ya asentado por tandas, por omisión 0) y la tabla `ReceiptBatch` (una fila por tanda: número consecutivo por recibo, quién, cuándo, líneas y unidades). Los recibos existentes quedan sin tandas y se comportan como siempre.
- **Dominio:** `ReceiptBatchRules` (reglas puras): `PlanBatch` (RECEIPT hasta lo esperado pendiente + exceso como ajuste; el faltante no se asienta en una tanda), `PlanClose` (el plan de siempre sobre el esperado pendiente; sin tandas es idéntico a `ReceiptPostingRules.Plan`), `NotBatchableReason` (serie, daño, cruce de muelle) y mensajes.
- **Servidor:** `POST /receipts/{id}/confirm-batch` (`ReceiptService.ConfirmBatchAsync`): bloquea recibo → aviso → orden de compra → saldos (mismo orden que la confirmación final), valida seguimiento, aplica a la orden de compra solo lo nuevo, congela el costo en la primera tanda, asienta, crea una tarea de acomodo por línea con las unidades nuevas y registra la tanda. La confirmación final (`ConfirmCoreAsync`) asienta solo el resto (`PlanClose`), aplica a la orden solo el resto, acomoda solo el resto y pasa a Acomodado solo si no quedan tareas de tandas abiertas. Guardas en editar/quitar línea y eliminar recibo. La ficha trae `batches` y cada línea `postedQty`.
- **Web:** en la ficha del recibo, botón «Confirmar tanda» (con confirmación y aviso de cuántas unidades entran y cuántas esperan la final), lista «Tandas confirmadas» y las tareas de acomodo visibles también con el recibo abierto. `receiptBatches.ts` (unidades por tanda).

## Cómo se probó
- xunit `ReceiptBatchTests` (10: neto de tandas + cierre = lo recibido en 5 escenarios, el cierre sin tandas es el plan legado, el exceso/faltante, líneas que no entran, y el servicio: dos tandas + cierre asientan cada unidad una vez con 2 tareas, guardas 400/409/422, tanda + cierre del resto) más las pruebas de recibo, daño, orden de compra y `FrozenSqlTests` (231 en total, verdes). La sección del SQL se aplicó dos veces contra SQL Server local (idempotente).
- vitest `receiptBatches.test.ts` y las pruebas de pantalla de recibo; tipos y lint.

## Decisiones a revisar
1. **Una tanda no asienta el faltante** (nada se «da por perdido» hasta cerrar); el cierre lo asienta como siempre.
2. **Series, daño y cruce de muelle esperan la confirmación final** (el daño y el cruce se reparten sobre el total; las series exigen sus números completos). Es la parte más sencilla y segura; se puede ampliar después.
3. **Recibo directo no admite tandas.**
4. **La orden de compra recibe cada tanda**; si una tanda la completa y luego llega más, la orden ya no es recibible (mensaje de la orden de compra), igual que ya pasaba al sobre-recibir.
5. El aviso de llegada pasa a «Recibido» solo en la confirmación final (no en la primera tanda).

## Qué quedó fuera
- **Lote 175:** la app de almacén (recibir por tandas y el aviso cuando el staging se llena por la capacidad máxima de la posición).
- Mostrar en cada línea recibido / en staging / acomodado (hoy: recibido y confirmado en tandas) y avisar al cliente en el portal de «Recibido parcial».
