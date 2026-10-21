# Lote 175 — Recibir por partes desde el aparato (C2, app; pedido de Luis, 2026-10-21)

## Qué se construyó
- **Dictado de Luis:** varios aparatos u operadores pueden recibir partes del mismo documento; recibir y acomodar son dos cosas (se recibe al staging, se llena, se acomoda); las series «déjalas así por lo pronto».
- **Servidor:** `POST /receipts/part` (`ReceiptService.ReceivePartAsync`): contra un aviso o una orden de compra, busca el recibo abierto del documento (o lo crea), **suma** lo capturado a sus líneas (o lo agrega como extra) y confirma una tanda (Lote 174). `ClientKey` (columna nueva `ReceiptBatch.ClientKey`, índice único donde no es NULL; sección «Lote 175» del script de actualización) hace el reintento idempotente. Sin series ni daño en una parte (400). Además, `POST /receipts` con `confirm` y un documento que **ya tiene tandas** cierra ese recibo (suma las líneas capturadas y confirma el resto) en vez de dar el 409 «el aviso ya tiene recibo»; sin tandas el comportamiento es el de siempre.
- **App de almacén:** botón «Recibir una parte» en Recibir (con documento, almacén con acomodo, sin series ni daño en las líneas), nuevo tipo de cola `receiptPart`, aviso «no cabe en el staging» con el cupo y los saldos locales (`localStagingFree`, solo posiciones STAGING con cupo conocido).

## Cómo se probó
- xunit `ReceiptBatchTests.Parts_from_devices_add_up…` (dos aparatos suman al mismo recibo, reintento con la misma clave no duplica, series/daño rechazados, la confirmación final cierra con 10 de 10 y tres tareas 4/3/3), recibos, `FrozenSqlTests` y `WmsControllerSecurityTests` (174 acciones). Sección SQL aplicada dos veces a SQL Server local.
- jest: `receiveLogic` (partBlock, cuerpo, clave v4, desborde de staging), `stagingRoom`, `outboxKind`, la pantalla `receivePartScreen` y las demás de Recibir; `tsc`.

## Decisiones a revisar
1. Una parte **suma** a lo ya capturado (no reemplaza): cada parte es «lo que llegó en esta vuelta».
2. La app no sabe qué se recibió ya del documento (no se sincroniza el avance parcial); el aviso sigue «pendiente» en la lista hasta el cierre.
3. Si el recibo abierto del documento lo capturaba alguien en la web sin tandas, la parte del aparato se suma a ese mismo recibo.
4. Series y daño siguen en la confirmación final (pendiente de definir con la operación real).
5. El aviso de staging lleno es solo informativo.

## Qué quedó fuera
- Mostrar en el aparato lo ya recibido de un documento y el recibido / en staging / acomodado por línea; avisar al cliente en el portal de «Recibido parcial»; recibo ciego por partes (no tiene documento que lo identifique).
