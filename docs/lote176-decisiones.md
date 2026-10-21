# Lote 176 — Recolección maestra: varios documentos, un envío (C3 + C4 del despacho; servidor; pedido de Luis, 2026-10-21)

## Qué se pidió (palabras de Luis, resumidas)
Ver en el sistema, después de despachar, de qué posiciones salió todo, desde el aviso de salida o la recolección; la **recolección** es el documento maestro y puede atender **varios documentos** (órdenes de venta o avisos de salida); al terminar se le crea **un envío** (el empaque). Se arma **al recolectar** (no se suman documentos después); un solo cliente y un solo destino (otro destino = otra recolección).

## Qué se construyó
- **Estructura** (sección «Lote 176» del script de actualización, idempotente): tabla `PickGroup` (la maestra: número EMP-##### del contador PACKBATCH, almacén, cliente, nota, quién/cuándo, envío), `PickBatch.PickGroupId`, y el índice `UX_PickBatch_Order` se recrea sin las recolecciones del grupo (que comparten el envío); el envío único del grupo es `UX_PickGroup_Order`. Nada cambia para lo existente.
- **Diseño:** la maestra **envuelve** las recolecciones de siempre: recolectar = el despacho de cada documento (los mismos `SalesOrderService.DispatchAsync` / `ExitNoticeService.DispatchAsync`, con su ledger, reservas, rotación y estatus) dentro de UNA transacción + el registro de la maestra. Así no se tocó el núcleo de inventario ni las reversas por documento.
- **Servicio** `PickGroupService` y 5 endpoints en `/pick-batches/groups`: recolectar (2–20 documentos del mismo cliente y almacén; sin líneas = todo lo reservado), lista, ficha (documentos + todos los movimientos con posición y lote), empacar (UN envío con número de empaque = el de la maestra; también para avisos de salida) y eliminar (deshace todas las recolecciones; borra el envío si sigue en etapa inicial). Una recolección del grupo no se empaca ni se elimina empacada suelta (422); `PickBatchDto` trae `groupPublicId/groupNumber` y `canPack` falso.
- Reglas puras en `PickGroupRules` (selección, mensajes, estatus derivado).

## Cómo se probó
- xunit `PickGroupTests` (reglas puras; recolectar dos avisos del mismo cliente: una maestra con sus 9 unidades y movimientos de dos posiciones, la recolección suelta ya no se empaca; otro cliente / menos de dos / documento inexistente se rechazan sin recolectar; eliminar la maestra devuelve el inventario y la reserva) más recolección, despacho manual, `WmsControllerSecurityTests` (179 acciones) y `FrozenSqlTests`. **Contra SQL Server real** (`PickGroupSqlTests`): lista, búsqueda por número de un documento y ficha se traducen y devuelven lo mismo que en memoria. La sección SQL se aplicó dos veces a SQL Server local.

## Qué NO se probó (a verificar en el recorrido)
- **El empaque de la maestra con el envío real** (`PackAsync`, crea la orden con `OrderService`) y **eliminar una maestra empacada**: el código copia las reglas del empaque de una recolección suelta pero no se ejercitó de punta a punta (las pruebas del repo no montan `OrderService`). Es lo primero a revisar en el recorrido con Playwright/smoke.

## Decisiones a revisar
1. La maestra reúne las recolecciones de cada documento (no es una sola recolección con líneas de varios documentos): cada documento sigue con su avance y su reversa propios.
2. Mismo cliente = el cliente del documento (orden de venta o aviso de salida); el destino lo da el envío al empacar.
3. El número de la maestra es del mismo contador de empaque; consume un número más que las recolecciones que reúne.
4. Los avisos de salida ahora pueden terminar en un envío, pero solo dentro de una maestra (suelto siguen sin empacarse).

## Qué quedó fuera
- Pantallas web (Lote 177) y app (Lote 178): elegir varios documentos y «Recolectar juntos», ficha de la maestra con sus movimientos, empacar y eliminar.
- Mostrar la maestra en «Por documento» (Lote 173) y en el aviso de salida.
