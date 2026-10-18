# Lote 106 — Nuevo flujo de estatus de la orden de transporte: qué se construyó y decisiones a revisar (2026-10-17)

Luis dictó el flujo de la orden de transporte; el del seed original era solo un ejemplo. Este lote lo cambia en la base, el servidor, la web y la app del cliente. El escaneo por acción, «pick up», Reclamaciones, Devuelta y el acceso del consignatario quedan para los lotes siguientes (ver «Qué quedó fuera»).

**Flujo nuevo:** Sometida (inicial) → Recogida → Sin asignar → Asignada → En tránsito → Entregada (terminal). Laterales y finales: Intento fallido (lateral), Cancelada (terminal, con razón) y En almacén (terminal, para servicios de solo recogida).

## Mapa de lo construido

| Capa | Tablas | Código principal | Endpoints |
|---|---|---|---|
| Catálogo de estatus | `StatusCode` (`OrderStatus`), `StatusCapability`, `StatusLateralEntry`, `StatusCodeOverride`, `EntityStatusHistory`, `TransportOrder` | `OrderStatuses` en `CatalogDomains.cs`: `SUBMITTED`, `PICKUP`, `UNASSIGNED`, `ASSIGNED`, `IN_TRANSIT`, `DELIVERED`, `FAILED`, `CANCELLED`, `IN_WAREHOUSE`. Se borraron `DRAFT`, `CONFIRMED`, `INBOUND`, `PLANNED`, `ARRIVED`, `ON_HOLD`, `PARTIAL` | `GET /api/v1/status/OrderStatus`, `GET /api/v1/catalogs/OrderStatus` |
| Base de datos | los de la fila anterior; `IndicatorDefinition`, `ChartDefinition`, `ReportDefinition` (filtros), `Permission`, `RolePermission` | `Diseño/logistica-db-update.sql`, secciones **(an)** (flujo) y **(ao)** (permiso `orders.reopen`). El seed y la estructura no se tocaron | `db-update` |
| Cotizar y medir crédito al crear | `TransportOrder` (`QuotedAmount`, `QuotedAtUtc`, contrato), `EntityStatusHistory`, `SecurityEvent` | `OrderStatusService.QuoteOnSubmissionAsync`, llamado desde `OrderService.CreateAsync`; `QuotedOrdersBalanceProvider` (Sometida ya cuenta en el saldo en curso) | `POST /api/v1/orders` |
| Marcar como recogida | `TransportOrder`, `EntityStatusHistory` | `OrderStatusService.ConfirmTrackedAsync` (Sometida → Recogida, cotización estricta, congela precio, crédito) | `POST /api/v1/orders/{id}/confirm` (mismo endpoint de antes) |
| Cancelar con razón | `EntityStatusHistory` | `OrderStatusService.CancelAsync` (la razón es obligatoria) | `POST /api/v1/orders/{id}/cancel` |
| Reabrir | `EntityStatusHistory`, `SecurityEvent` | `OrderStatusService.ReopenAsync`, `StatusService.TransitionAsync(..., reopen: true)`, `OrderReopenRequest`, `OrderCapabilitiesDto.CanReopen` | `POST /api/v1/orders/{id}/reopen` (`orders.reopen`) |
| Importador | `ImportBatch` | `OrderImportService`: ya no exige `confirmNow` para `overrideCredit`; lo pasa a cada orden creada | `POST /api/v1/orders/import/{batch}/confirm` |
| Despacho y rutas | `TransportOrder`, `Trip`, `TripOrder` | `TripStatusEffect` (lleva las órdenes hasta Asignada), `TripRules.DraftOrderMessage`, `OrderDispatchFlags`, `DispatchBatchRules` | `POST /api/v1/trips/{publicId}/dispatch` y los de Sala de despacho |
| Pulso, Radar, indicadores | `TransportOrder`, `IndicatorDefinition` | `PulseRiverService`, `RadarRules.OpenStatuses`, `SystemAnalyticsSeeder`, `OrderDataSources` | `GET /api/v1/pulse/*`, `GET /api/v1/pulse/radar` |
| Facturación y COD | `TransportOrder`, `CodCollection` | `FreightRules.PickedUpOrLater`, `CodService` (no se cobra en Sometida ni Cancelada) | `/api/v1/cod/*` |
| Permiso | `Permission`, `RolePermission` | `PermissionCatalog.OrdersReopen`; `TenantAdmin` lo recibe | — |
| Web (`web-app`) | — | `OrderEntryForm` (un solo «Guardar» / «Autorizar y guardar»), `OrderImportModal` (sin «Confirmar al importar»), `OrderDetailScreen` («Marcar como recogida», cancelar con razón, «Reabrir»), `OrderActionDialogs`, `orderRules`, `orderImportRules`, `orderQuickRules`, `es.json`/`en.json` | las mismas |
| App del cliente | — | las órdenes del portal nacen Sometidas (no cambia código propio; ver Lote 105) | `POST /api/v1/portal/orders` |

### Reglas que quedaron
- **La orden nace Sometida.** Ya no hay Borrador ni Confirmada. Es la única etapa donde se edita la carga (`EDIT_CARGO`). «Planificar» no es un estatus: es la fecha pedida de una orden Sometida.
- **Cotiza y mide crédito al crear.** Si el cliente tiene contrato y tarifa, la orden queda cotizada; si no, queda sin cotizar y **no da error**. Crédito excedido: 422 `credit_exceeded` y la orden no se crea, salvo `overrideCredit: true` con el permiso `orders.credit_override` (queda un `SecurityEvent` `credit_override`). Mientras siga Sometida y cotizada, editarla la re-cotiza.
- **«Marcar como recogida»** es la acción que antes se llamaba «Confirmar» (mismo `POST /orders/{id}/confirm`): Sometida → Recogida, cotización estricta (falla si falta tarifa), congela el precio y vuelve a medir el crédito. `confirmNow` sigue en la API, pero la web ya no lo usa. Ya no existe el error `overrideCredit requiere confirmNow=true.`
- **Cancelar exige razón** (`comment`). **Reabrir** una orden en estatus final exige el permiso nuevo `orders.reopen` y una razón.
- **Capacidades por defecto:** `EDIT_CARGO` solo en Sometida; `REPRICE` solo en Sometida; `CANCEL` apagada en Entregada, Cancelada y En almacén; `ASSIGN_TRIP` apagada en Sometida y en los finales; En almacén bloquea las cuatro.
- **Radar:** cuenta las Sometidas como abiertas (antes el borrador no). **Pulso:** «Por despachar» = Sin asignar o Asignada sin ruta con chofer; «En ruta» = En tránsito; «COD por cobrar» solo de Entregadas. **Facturación:** «recogida» incluye Recogida, Sin asignar, Asignada, En tránsito, Entregada y En almacén.
- **Despacho de la ruta:** sigue llevando las órdenes hasta Asignada. Mensaje de elegibilidad nuevo: `La orden está en Sometida; márquela como recogida antes de asignarla a una ruta.`
- **Intento fallido** es lateral. Se documenta con un `DeliveryAttempt` (el efecto `DeliveryAttemptOrderEffect` lo anota al pasar a Fallida). En la barra de progreso se queda en el nivel de la última etapa normal del historial (por ejemplo En tránsito), en color de alerta.

## Cómo se prueba

1. Aplicar `db-update` sobre una base de desarrollo (nunca recrear en producción): `dotnet run --project src/Teikem.Api -- db-update`. Las secciones (an) y (ao) son idempotentes; aplicarlas dos veces no cambia nada.
2. `GET /api/v1/status/OrderStatus`: deben salir `SUBMITTED`, `PICKUP`, `UNASSIGNED`, `ASSIGNED`, `IN_TRANSIT`, `DELIVERED`, `FAILED`, `CANCELLED`, `IN_WAREHOUSE`, y ninguno de los siete viejos.
3. Crear una orden (`POST /api/v1/orders` sin `confirmNow`) de un cliente con contrato y tarifa: nace `SUBMITTED` y trae `quotedAmount`. De un cliente sin contrato: nace `SUBMITTED` sin cotizar, sin error.
4. Un cliente con límite de crédito bajo: la creación responde 422 `credit_exceeded`; con `overrideCredit: true` y `orders.credit_override` se crea.
5. `POST /api/v1/orders/{id}/confirm`: pasa a `PICKUP`. Sin tarifa, 422.
6. `POST /api/v1/orders/{id}/cancel` sin `comment`: 400 `Indique la razón de la cancelación.`; con `comment`: pasa a `CANCELLED`.
7. `POST /api/v1/orders/{id}/reopen` sobre una orden Entregada o Cancelada: sin `orders.reopen`, 403; sin `comment`, 400; con `comment` y sin `toCode`, vuelve al último estatus en curso que tuvo; `GET /api/v1/status/history/TRANSPORT_ORDER/{id}` muestra el cambio y `/audit/security-events` el evento `order_reopen`.
8. Web: Órdenes → **Nueva orden** (un solo **Guardar**), el expediente (**Marcar como recogida**, **Cancelar la orden** con razón, **Reabrir**) y el importador (sin «Confirmar las órdenes al importar»).

### Lo que se corrió de verdad
- **xunit:** suite completa, **4312 pasan, 0 fallan**. Cambiaron o se agregaron pruebas de catálogo (`OrderCatalogTests.Order_statuses_are_the_catalog_of_seed_plus_update`, que aplica el seed más la sección (an) y compara con `OrderStatuses`), recorrido del pipeline (`PipelinePathTests`), efecto de ruta (`TripStatusEffectTests`), rutas (`RouteWriterTests`), despacho, Pulso y Radar, seguimiento, COD, facturación de flete, escaneo de salida, entre otras.
- **vitest de la web:** pruebas de las pantallas y reglas de órdenes, importador, despacho y escaneo ajustadas al flujo nuevo.
- **jest de app-cliente:** corrido.
- **SQL real:** `db-update` contra la base `Teikem_fg2`, **dos veces** (la segunda sin cambios).
- **Capturas del manual de pantallas:** se regeneraron con Playwright el 2026-10-17 (las imágenes de `docs/manual/frontend/img` de los capítulos fc1, fc2, fc3, fe1, fe2a, fe2b, fe3, fg2, l102, l103 y l105 cambiaron en el árbol de trabajo; las que abrí —fc1, fc2, fc3 y l103— muestran el flujo nuevo: «Guardar», «Marcar como recogida», «Autorizar y guardar», Intento fallido, barra de 6 etapas; el resto no las revisé). El resultado de los specs de Playwright no me lo pasaron; no lo afirmo.

## Decisiones a revisar con Luis

1. **Los estatus viejos se borraron, no se desactivaron.** En producción no hay órdenes. En bases de desarrollo la sección (an) remapea antes de borrar: `CONFIRMED` → `PICKUP`, `ARRIVED` → `IN_TRANSIT`, `ON_HOLD` y `PARTIAL` → `FAILED`. Es una pérdida de detalle: lo que estaba «En espera» o «Parcial» queda «Intento fallido». También se borran sus filas de capacidades, entradas laterales y ajustes por compañía.
2. **Los filtros guardados se reescriben a ciegas.** Indicadores, gráficos e informes de órdenes cambian `"CONFIRMED"` y `"DRAFT"` → `"SUBMITTED"`, `"INBOUND"` → `"UNASSIGNED"`, `"PLANNED"` → `"ASSIGNED"`, `"ARRIVED"` y `"ON_HOLD"` → `"FAILED"`. Un filtro de usuario que pedía «Confirmada» ahora pide «Sometida», y puede dejar de significar lo mismo.
3. **Sin contrato o sin tarifa no es un error al crear.** La orden queda Sometida y sin cotizar; el error aparece al marcarla como recogida. Una orden sin cotizar no cuenta en el saldo en curso del cliente, así que el crédito solo la ve cuando se cotiza.
4. **Sometida ya cuenta en el saldo pendiente.** Antes el borrador no. Un cliente con muchas órdenes Sometidas se acerca a su límite antes.
5. **`confirmNow` sigue en la API.** Crea la orden y la marca como recogida de una vez; lo manda la app de almacén al empacar y al despachar con entrega (manual 09). La web ya no lo manda. Si se quiere retirar, hay que tocar esas llamadas.
6. **Cancelar sin razón ahora es 400.** Rompe a quien llamaba `POST /cancel` sin cuerpo.
7. **Reabrir:** solo `TenantAdmin` trae `orders.reopen` (la sección (ao) lo agrega a los roles ya clonados). No re-cotiza ni vuelve a medir el crédito. El destino puede ser cualquier etapa no final, incluido el lateral Intento fallido y Sometida; no se revalida el orden del pipeline. Sin `toCode` vuelve al último estatus no final que tuvo la orden.
8. **«Re-cotizar» solo existe en Sometida.** Antes se permitía en Confirmada. Después de recogida, un cambio de tarifa ya no se aplica a la orden.
9. **El mensaje de elegibilidad cambió** y la constante sigue llamándose `TripRules.DraftOrderMessage` (nombre viejo, texto nuevo).
10. **Intento fallido → Sin asignar al escanear «llegó»** lo dictó Luis, pero el escaneo que lo hace es del lote 107. Hoy, desde el expediente, un Intento fallido solo regresa a la etapa exacta de la que salió. El comentario de `OrderStatuses` en `CatalogDomains.cs` dice «se puede brincar estatus»; **el motor todavía no deja saltar etapas del pipeline** (`StatusService.EnsureTransitionAllowedAsync`: solo avanza a la siguiente). Eso cambia en el lote 107.
11. **Actividad:** se quitó el mapeo del evento «Orden retenida» (`OrderOnHold`), porque `ON_HOLD` ya no existe. El evento no se genera más.
12. **Capturas del manual de pantallas.** Ya muestran el flujo nuevo (ver «Lo que se corrió de verdad»). Algunas imágenes siguen con datos de prueba de la corrida (nombres de cliente como «Cliente FC2 DEKHAFG»); es solo ilustrativo.
13. **Colores y orden de los estatus nuevos** (sección (an): Sometida `#9CA3AF`, Sin asignar `#8B5CF6`, Asignada `#0EA5E9`, Intento fallido `#EF4444`, En almacén `#14B8A6`; orden 1 a 5, Intento fallido 22, En almacén 9). No consta que Luis los haya dictado; hay que confirmarlos.
14. **El Radar cuenta el Intento fallido como abierto** (`RadarRules.OpenStatuses`: Sometida, Recogida, Sin asignar, Asignada, En tránsito e Intento fallido). Antes el Radar contaba En espera y Parcial pero no Fallida; al desaparecer esos dos, el Intento fallido ocupa su lugar. «Órdenes en curso» también lo cuenta.
15. **Manuales con textos fuera del alcance:** el capítulo 21 (COD) y el mensaje `No se registra un cobro en una orden en borrador o cancelada.` siguen diciendo «borrador» porque el texto del mensaje no se cambió; en el código significa Sometida (se aclara en el FAQ).

## Qué quedó fuera (decidido por Luis, lotes siguientes)
- **Escaneo por acción escogida por el usuario**, con saltos de estatus en silencio y reabrir con permiso (hoy el motor no deja saltar etapas del pipeline). Lote 107.
- **Reclamaciones:** paquetes faltantes (Por verificar → perdido → reclamación). Lote 108.
- **Devuelta** (estatus final), orden de vuelta (round trip) y sugerencia «devolver tras N intentos». Lote 109.
- **Tipo de servicio «pick up»** con su marca y su flujo Sometida → Recogida → En almacén, y **cargo por devolución** opcional por cliente. Lote 110. Hoy `IN_WAREHOUSE` existe como estatus final, pero ningún tipo de servicio lo usa.
- **Acceso del consignatario.** Lote 111.
- **App móvil de entregas.**
