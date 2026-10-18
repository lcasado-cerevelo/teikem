# Lote 112 — Devolución (2026-10-18)

Luis dictó (2026-10-17/18): tras un intento fallido la orden vuelve al almacén y el guarda **escoge al escanear**: «Llegó» (reintentar) o «Devolver»; al devolver, la orden pasa a **Devuelta** (estatus final) y se crea la **orden de vuelta** (round trip) con los ítems que salieron, sin facturación de flete. Respuestas de Luis a las preguntas de este lote:
- La orden de vuelta **sin escanear nace «Sometida»**; **escaneando nace Sometida «brincada» y asume el estatus de la escaneada (Recogida)**.
- Devolver exige un **permiso nuevo `orders.return`**.
- Viajan **solo las piezas que se escanean en «Devolver»** (o las que se marcan en el botón de la ficha, que crea la vuelta en Sometida).

## Qué se construyó
- **SQL (update.sql, sección (at))**: estatus `RETURNED` «Devuelta» (terminal, orden 10) con sus capacidades bloqueadas (editar, recotizar, cancelar, asignar a ruta); `TransportOrder.ReturnOfOrderId` (la vuelta apunta a la orden devuelta); permiso `orders.return` propagado a los `TenantAdmin` ya clonados.
- **`ReturnOrderService`**: arma la orden de vuelta (cliente y contrato de la original; paradas invertidas: el recogido de la vuelta es la entrega de la original y su entrega es el recogido de la original o, si no tenía, el recogido del cliente; conduce y números nuevos; sin cotizar), muda las piezas elegidas (mismos números) a líneas de la vuelta, deja la original en Devuelta (una sola vez) y recalcula lo cobrable de la original con `PartialBillingService`. Las piezas siguientes de la misma orden se suman a la vuelta mientras siga en Sometida o Recogida.
- **API**: `POST /api/v1/orders/{publicId}/return` (`OrderReturnController`, `orders.return`, LTL_GROUND); la acción de escaneo `RETURN` en `POST /api/v1/scan/action` (permiso `orders.return` comprobado en el servicio; resultado `RETURNED`, voz «Sí»; repetir la pieza es duplicado; una pieza faltante responde «No»).
- **Cierre del conteo de una orden Devuelta**: lo que sigue en ella y no es faltante se lista y, al confirmar, queda «Por verificar».
- **Web**: botón **Devolver** en la ficha (diálogo con las piezas marcables y una nota; solo con `orders.return` y en una orden recogida que sigue en curso) y acción **Devolver** en la estación (solo con el permiso); es/en, claro/oscuro.

## Cómo se probó (solo el área)
- xunit: `ReturnOrderServiceTests` (6: botón, sin números, validaciones, escaneo que crea y suma, rechazos del escaneo, cierre del conteo de la devuelta) y `OrderReturnControllerSecurityTests`; pines de permisos 99 → 100 y `OrderCatalogTests` (el catálogo de estatus de orden cuenta también `RETURNED`). vitest: `OrderDetailScreen` (+4) y `ScanScreen` (+2).
- Contra SQL real (`db-update` dos veces sobre Teikem_fg2, la segunda sin cambios): el botón crea la vuelta (Sometida, sin cotizar, mismas piezas) y la original queda `RETURNED`; por escaneo la primera pieza crea la vuelta, la segunda se suma, la repetida es duplicado y «Cerrar conteo» lista las no devueltas y las deja «Por verificar».

## Decisiones a revisar con Luis
1. **«Devolver tras N intentos» (por omisión 2)** —la sugerencia por compañía— queda para el lote siguiente (marca en el resultado del escaneo y ajuste en Ajustes de la compañía); hoy nada sugiere ni bloquea.
2. **Reabrir una orden Devuelta** (corrección de Luis): no se reabre mientras su orden de vuelta siga viva; hay que **cancelar la vuelta** y entonces se reabre (422 con el número de la vuelta).
3. **El regreso va al cliente que puso la orden** (corrección de Luis): el recogido de la orden; si no tenía, el recogido del cliente y, si tampoco, cualquier dirección que el cliente tenga registrada (recogido, corporativa o facturación). Solo si no tiene ninguna responde 422.
4. La orden de vuelta **hereda el contrato y el tipo de servicio** de la original; sin tarifa propia ni cobro (decisión tuya: «sin facturación de flete»). El **cargo por devolución** opcional (por tipo de paquete o por paquete) es el lote final de esta serie.
5. La app móvil del almacén aún no existe: cuando se construya, «Devolver» y «Cerrar conteo» hacen lo mismo que la web.

## Corrección (2026-10-18, hallada por el recorrido de Playwright)
- **Error real:** devolver **todas** las piezas de una orden cobrada **por entrega** daba **422 «La orden no tiene líneas de paquete activas que cotizar.»** y la devolución no se hacía: al quedar sin piezas cobrables, `PartialBillingService.AdjustAsync` daba de baja todas las líneas y luego intentaba cotizar una orden sin líneas. Ahora, sin ninguna pieza cobrable, el flete por entrega queda en **$0.00** (nada que cobrar). Prueba xunit `A_by_delivery_order_whose_every_piece_left_is_requoted_to_zero_instead_of_failing` (falla sin el arreglo). Las pruebas anteriores no lo veían porque su servicio de cotización estaba apagado (`AdjustAsync` no hacía nada sin él).
- Recorrido Playwright `web-app/e2e/lote112-devolucion.spec.ts` (escritorio y móvil: sugerencia tras 2 intentos, devolver, el caso sin dirección, la orden de vuelta y el ajuste de la compañía), capturas `l112-*.png` y `l113-*.png` y capítulo `docs/manual/frontend/l112-devolucion.md`.
