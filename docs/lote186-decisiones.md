# Lote 186 — Pendiente menor: maestras visibles y avance del recibo por partes (2026-10-21)

Pedido de Luis: los cuatro puntos de «pendiente menor». Este lote cubre los puntos 1, 2 (parcial) y 3; el punto 4 se cubre a medias (órdenes de venta sí; empacar desde el aparato, pendiente de una decisión).

## Qué se construyó
- **Punto 1 — «Por documento» marca la maestra:** `PickBatchSummaryDto.GroupNumber`; cada recolección de la ficha del documento lleva «Maestra RMA-…».
- **Punto 3 — recepción maestra visible:** `ReceiptListItemDto.GroupNumber`; etiqueta «Maestra RCM-…» en la lista y la ficha del recibo; botón «Recepciones maestras» en Llegadas y salidas.
- **Punto 2 — avance por partes:** `AsnDto.PostedQty` / `PartiallyReceived` y `AsnLineDto.PostedQty` (lo asentado por tandas en los recibos activos del aviso). Web: etiqueta «Recibido parcial» y columna «Recibido» en Avisos de llegada (el cliente lo ve en su portal web). Sincronización: `SyncAsnLineDto.PostedQty`; un aviso se re-envía al aparato cuando su recibo confirma una tanda; base local v21 (`asn_line.posted_qty`, reinicia el avance de avisos). Aparato: «Ya recibido de este documento» en Recibir.

- **Punto 4a — órdenes de venta en «Recolectar juntos» (aparato):** el selector muestra también las órdenes de venta con algo reservado; se mezclan con avisos del mismo cliente en una sola ruta y un solo `POST /pick-batches/groups` (`kind: SALES_ORDER`). Las líneas de una orden viajan en la ruta con su id en negativo (no chocan con las de un aviso) y se devuelven al id real al armar el cuerpo; kits por lo reservado de sus componentes; sin señal se refleja al instante y se reaplica al bajar cada tipo de documento.

## Cómo se probó
- xunit `ReceiptBatchTests` (el aviso queda parcial con 7 de 10), `WmsContractsTests` (firmas), vitest (`PickDocumentsPanel`, `ReceiptScreen`), jest (`exitGroupLogic`, `localExit` con grupo mixto, recorrido `exitGroupScreen` aviso+orden, `localLookup`, `receivePartScreen`, base y descarga).

## Decisiones a revisar
1. «Recibido parcial» = aviso en EXPECTED con algo asentado por tandas; no es un estatus nuevo del catálogo.
2. En el aparato solo se muestra para avisos (no órdenes de compra).
3. El esquema de tipos de `app-almacen` se editó a mano (su `schema.d.ts` no se regenera igual que el de la web).

## Qué quedó fuera
- **Empacar la maestra desde el aparato:** el envío exige un pedido completo (consignatario, servicio, bultos) y el aparato no conoce las maestras (no se sincronizan); pendiente de decisión del dueño.
- **Series y daño por partes:** requieren que la tanda asiente series y reportes de daño (hoy esas líneas se confirman en el recibo final); queda para cuando la operación real lo defina.
- **app-cliente:** no tiene pantalla de avisos de llegada, por eso «Recibido parcial» solo se ve en el portal web.
