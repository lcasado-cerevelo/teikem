# Lote 140 — Lote y vencimiento en la propia línea del recibo (pedido de Luis, 2026-10-20)

## Qué se construyó
- En el recibo web sin documento (ciego o devolución), al escoger un producto **por lote** la línea pide **Lote · Fabricación · Vencimiento** ahí mismo; con un producto **por serie**, el cuadro de **series** (lo recibido = número de series). Antes se grababa la línea y luego había que buscar el ícono «Lote y series».
- Lógica pura en `receiptLineEdit.ts` (`needsInlineCapture`, `onLotInput`, `onSerialsInput`; `linePayload` espera el lote/las series y manda `lot` / `serialNumbers` en el mismo POST; valida con `receiptLineIssues`, los mismos mensajes del manual). Celda `InlineCapture` en `ReceiptLinesEditor.tsx`.
- **La línea se graba al salir del GRUPO de campos del lote**, no al pasar del lote al vencimiento (una prueba lo cazó: grababa sin la fecha y los campos desaparecían).
- No hubo cambio de API ni de base de datos.

## Cómo se probó
- vitest: `receiptLineEdit.test.ts` (6 casos nuevos: cuándo pide captura, espera del lote, cuerpo del POST, fechas inconsistentes, series, limpieza al cambiar de producto, errores del servidor por campo) y `ReceiptScreen.test.tsx` (recorrido: producto por lote → campos en la línea → lo recibido → sin POST hasta tener el lote → POST con el lote y el vencimiento). `npm run check` completo.

## Decisiones a revisar
1. Solo **filas nuevas** de un recibo sin documento llevan los campos en la línea; las líneas guardadas y las de un aviso/orden de compra siguen con «Lote y series» (el servidor no devuelve la fecha de fabricación, así que no se puede prellenar).
2. Un producto por lote sin número de lote **no se graba** (queda «sin guardar» y Confirmar lo bloquea con su aviso de siempre).
3. Tampoco se tocó la app de almacén: ahí el lote ya se captura en la misma pantalla de Recibir.
