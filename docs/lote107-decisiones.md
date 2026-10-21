# Lote 107 — Escaneo con acción escogida y estatus brincado (2026-10-17)

Luis dictó: el usuario escoge la acción (recoger, llegó, enrutar, salir) y cada escaneo lleva la orden a ese estatus sin tener que escanear varias veces; los estatus brincados se registran con la misma fecha; cuando algo se salta no hay problema ni se interrumpe; la estación de la web que ya existe es la que se extiende.

## Qué se construyó
- **Cernidor al someter** (corrección del Lote 106, dictada por Luis): contrato, tarifa, cliente y crédito se revisan al crear la orden y, si no pasa, no se crea; pasar a Recogida o escanear ya no revisa nada (solo congela el precio). `OrderStatusEffect` quedó sin acciones; `ConfirmTrackedAsync` solo transiciona; `QuoteOnSubmissionAsync` es estricto y corre siempre al crear (también con `confirmNow` o chofer).
- **Estatus brincado:** columna `EntityStatusHistory.IsSkipped` (update.sql (ap)); `StatusService.AdvanceSkippingAsync` recorre las etapas (una fila por etapa, misma fecha y hora, las de en medio brincadas, los efectos corren y los de aviso al cliente ignoran los brincados vía `StatusTransitionContext.Skipped`); el historial (`StatusHistoryDto.IsSkipped`) y la ficha muestran «(brincado)».
- **Escaneo con acción** (`ScanActionService`, `POST /api/v1/scan/action`, permiso `trips.scan`): `PICKUP`→Recogida, `ARRIVE`→Sin asignar, `ROUTE`→Asignada y a la ruta abierta de su zona (reutiliza `OutboundScanService`; sin ruta que la reciba se deshace y responde `NO_ROUTE`), `DEPART`→En tránsito. Ya más adelante → `ALREADY_BEYOND`; estatus final → `FINAL`; desde Intento fallido, «Llegó» la devuelve a Sin asignar (el fallido queda en el historial).
- **Web:** la estación de escaneo (`/ops/scan`) tiene el selector de acción (Recoger, Llegó, Enrutar —por omisión—, Salir) con su ayuda, es/en, y el historial de estatus marca los brincados.

## Cómo se probó
- xunit `ScanActionServiceTests` (11) y mapa de seguridad del controlador; suite completa. vitest (+1, selector de acción; la pantalla de escaneo pasó a `/scan/action`), Playwright FE1 (escritorio y móvil) ajustado: una Sometida ya no se rechaza, se enruta brincando.
- Contra SQL real: Recoger; Salir desde Sometida brinca 2 estatus con la misma hora; ya más adelante; código inexistente; Enrutar sin zona → `NO_ROUTE` y el estatus vuelve a Sometida; Enrutar con ruta real → Asignada en la ruta; desde Intento fallido «Llegó» → Sin asignar; acción inválida 400.

## Decisiones a revisar con Luis
1. **Enrutar sin ruta no cambia nada** (ni a Sin asignar): «Asignada» significa en una ruta. Con «Llegó» queda Sin asignar.
2. **Enrutar escoge la ruta por zona** (la de menor id abierta del día), como el escaneo de salida de siempre; «contra un chofer» o escoger la ruta a mano queda para otro lote.
3. **El mensaje de voz y los textos del resultado** son los del servidor (español).
4. **Pendiente de lotes siguientes:** tipos de servicio «pick up» (Sometida→Recogida→En almacén), Reclamaciones, Devuelta + orden de vuelta, cargo por devolución, sugerencia «devolver tras N intentos», app móvil de entregas; la app de choferes aún no marca Recoger/En tránsito.
5. Quedó código del Lote 106 que ya no se usa: el diálogo «Autorizar y marcar como recogido» de la web (el servidor ya no mide crédito al marcar recogida).
