# Lote 58 — Órdenes de venta: solicitar y reservar (P4, S1) (2026-10-15)

Sigue al Lote 57 ([lote57-decisiones.md](lote57-decisiones.md)). Diseño: maestro 13C.

## Qué se construyó
- **Solicitar** (`DRAFT → REQUESTED`) con **validación y reserva en la misma transacción** y el candado del ledger (`InventoryLedger.ReserveAsync`), por el mismo orden de rotación (FEFO/FIFO) que la recolección y guardando **exactamente qué saldo reservó cada línea** (`SalesOrderReservation`).
- **Disponible para vender** = en mano − reservado en posiciones activas del almacén de la orden, **sin** cuarentena, cross-dock ni renta; con lote si la línea lo pide.
- **Faltante**: 409 `insufficient_stock` con «pedido / disponible / faltante» por línea y **nada reservado**; `allowBackorder` reserva lo disponible y deja el resto en `QtyBackordered`. `GET …/availability` lo muestra sin guardar (más el estado del crédito).
- **Crédito** del cliente con la regla de las órdenes de transporte (`CreditRules`): en curso = saldo pendiente de transporte + otras órdenes de venta solicitadas o parciales; excedido = 422 `credit_exceeded`; `overrideCredit` + `orders.credit_override` lo autoriza con comentario en el historial y evento en la bitácora de seguridad.
- **Cancelar una solicitada** libera exactamente lo reservado y borra el backorder; con algo despachado no se cancela.
- **Modificar una línea** solicitada (cantidad y/o precio): subir reserva más (409 o backorder), bajar quita primero del backorder y luego libera; recalcula impuesto y totales en su lugar.
- Los productos **con serie** se rechazan por ahora.
- Manual: capítulo 18 (secciones 6 a 8) y FAQ «Lote 58».

## Pruebas
- xunit (arnés de servicios, InMemory): reserva exacta repartida entre posiciones, faltante sin reservar nada ni siquiera lo de las líneas que alcanzaban, backorder, exclusión de cuarentena / cross-dock / renta / posición inactiva / otro almacén / lo ya reservado, línea con lote, dos vendedores sobre las mismas unidades, cancelar libera exacto (sin tocar la reserva de otra orden), no cancelar con algo despachado, modificar hacia arriba y hacia abajo (reserva, backorder, totales), reglas de modificación, precio con permiso, crédito (aviso, permiso, rastro) y crédito con otras órdenes de venta abiertas, serie, aislamiento entre compañías.
- **Contra SQL Server real** (API): 3 solicitudes **simultáneas** de 7 unidades sobre 10 en existencia → exactamente **una** reservó (200) y las otras dos recibieron 409; el saldo quedó en 10 en mano y 7 reservadas, con su fila en `SalesOrderReservation`. (Esa prueba encontró que el contador `SALESORDER` faltaba en `NumberingRules.IsKnownKind`: lo arregló el Lote 57.)

## Decisiones por omisión (a revisar con Luis)
1. **El faltante sin `allowBackorder` no reserva nada** (todo o nada), aunque otras líneas sí alcancen: Ventas decide con el diálogo.
2. **El crédito cuenta las demás órdenes de venta solicitadas o parciales por su total completo** (aunque ya se haya despachado parte; se afina cuando exista la factura).
3. **Modificar una línea solicitada solo cambia cantidad y precio**; agregar o quitar líneas de una orden ya solicitada se hace cancelando y creando otra (o, para quitar, bajando la cantidad; cerrar con faltante llega con los backorders).
4. **La carrera entre vendedores se reintenta hasta 3 veces** con la foto nueva antes de responder el faltante.
5. **Las reservas no cambian de posición** si luego se mueve la mercancía: la reserva queda en el saldo (posición y lote) que tenía.
