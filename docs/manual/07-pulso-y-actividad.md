# Capítulo 07 — Pulso del día y Actividad reciente (Lote 7A: Almacén)

Este capítulo describe la parte de **Almacén** de "Actividad reciente" (el panel de eventos recientes del Pulso del
día) y los indicadores/gráfico de Almacén que se agregan al Pulso en este lote. Operación (7B) y Contabilidad (7C)
agregan su propia pestaña de Actividad reciente y sus propios indicadores más adelante, sobre la misma
infraestructura. Los mensajes están verificados contra el código (`src/Teikem.Infrastructure/Services/Activity/
ActivityRules.cs`, `src/Teikem.Infrastructure/Services/Activity/WarehouseActivityProvider.cs`,
`src/Teikem.Infrastructure/Services/ActivityFeedService.cs`, `src/Teikem.Api/Controllers/ActivityController.cs`,
`src/Teikem.Infrastructure/Services/ProductService.cs`, `src/Teikem.Infrastructure/Seeding/
SystemAnalyticsSeeder.cs`). Las preguntas y respuestas de cada mensaje están en [faq.md](faq.md). El Pulso del día en
sí (indicadores, gráficos, mi rango de fecha) se describe en el capítulo
[F1 — frontend](frontend/f1-nucleo-y-mi-cuenta.md); este capítulo es la parte de backend que ese panel consume.

---

## 1. Actividad reciente (Almacén)

Qué hace: muestra, en una ventana corta, los eventos de negocio de Almacén que acaban de pasar — recepciones,
tareas de almacén, conteos cíclicos, recolecciones, órdenes de compra, cruce de muelle, ajustes y transferencias de
inventario, baja de producto o de almacén — sin ser la auditoría técnica (`AuditLog`, que exige `admin.audit`). No
hay una tabla de eventos: cada consulta calcula los eventos al leer, desde el historial de estatus, el ledger de
inventario y la bitácora, en la ventana pedida. Cada evento tiene un código fijo (catálogo `ActivityEventType`), una
etiqueta en el idioma del usuario, una bandera de **obligatorio** (siempre visible) u **opcional** (se puede ocultar
con "Solo obligatorios" o apagar por defecto), y queda enlazado al documento que lo originó.

Quién puede: la pestaña de un módulo de negocio se ve con el permiso de ese módulo — Almacén: `inventory.view` (y el
módulo del tenant `WMS_LOTSERIAL` encendido) — además de `analytics.view` y el módulo `ANALYTICS`, que exige el
endpoint completo. Sin `inventory.view` (o con `WMS_LOTSERIAL` apagado) la pestaña Almacén no aparece; pedirla de
todos modos responde 403. Sin `analytics.view` (o con `ANALYTICS` apagado) no se llega ni a eso: la política del
controlador responde 403 antes de correr el servicio.

Cómo se usa:
- `GET /api/v1/analytics/activity?module=WAREHOUSE&window=24h&onlyMandatory=false&skip=0&take=50` — respuesta:
  `{ "total": 12, "visibleModules": ["WAREHOUSE"], "items": [ { "occurredAtUtc": "...", "code":
  "RECEIPT_CONFIRMED", "module": "WAREHOUSE", "mandatory": true, "label": "Recibo confirmado", "entityType":
  "RECEIPT", "entityId": 41, "publicId": "...", "reference": "REC-000041", "detail": "ALM-01 · Cliente X · 3
  líneas", "userId": 7, "userName": "Ana Ruiz" } ] }`.
- `module` — pestaña a leer (por ahora solo `WAREHOUSE`); si se omite, se usa el primer módulo visible para el
  usuario. Si el usuario no ve ningún módulo, la respuesta es `{ "total": 0, "visibleModules": [], "items": [] }`
  (200, no 403).
- `window` — `24h` (por defecto), `48h` o `today` (desde la medianoche de **hoy en UTC**: el tenant todavía no
  guarda su zona horaria, ver decisión #8 de `docs/lote7A-decisiones.md`).
- `onlyMandatory` — `true` deja solo los eventos obligatorios del catálogo (o del override del tenant, sección 3).
- `skip`/`take` — paginación; `take` por defecto y máximo 50 ("Ver más" pide la siguiente página con `skip`).
- `GET /api/v1/products?belowMin=true` (mismo endpoint de siempre, con el filtro nuevo) — solo productos activos con
  mínimo definido y disponible por debajo de él (el mismo cálculo que `isBelowMin` de la lista); con
  `warehousePublicId` además, el disponible es el de ese almacén. Sirve para contar "bajo mínimo" por categoría en
  el panel Almacén del Pulso con `take=1` y el `total` de la respuesta, sin traer la lista completa.

### Catálogo de eventos de Almacén (23, `LookupCode.Entity = 'ActivityEventType'`)

| Código | Etiqueta (es) | Obligatorio | Encendido por defecto | Cuándo se genera |
|---|---|---|---|---|
| `RECEIPT_CONFIRMED` | Recibo confirmado | Sí | Sí | Recibo pasa a `RECEIVED` |
| `RECEIPT_VARIANCE` | Diferencia en recibo | Sí | Sí | Ajuste de ledger con motivo `RECEIPT_VARIANCE` sobre el recibo |
| `RECEIPT_PUTAWAY_DONE` | Recibo acomodado | No | Sí | Recibo pasa a `PUTAWAY` |
| `ASN_CANCELLED` | Aviso de llegada cancelado | No | Sí | Aviso de llegada pasa a `CANCELLED` |
| `PUTAWAY_DONE` | Acomodo completado | No | Sí | Tarea de almacén tipo `PUTAWAY` pasa a `DONE` |
| `REPLENISH_DONE` | Reabasto completado | No | Sí | Tarea de almacén tipo `REPLENISH` pasa a `DONE` |
| `TASK_CANCELLED` | Tarea cancelada | No | Sí | Cualquier tarea de almacén pasa a `CANCELLED` |
| `COUNT_FINISHED` | Conteo terminado | No | Sí | Conteo cíclico pasa a `COUNTED` |
| `COUNT_RECONCILED` | Conteo reconciliado | Sí | Sí | Conteo cíclico pasa a `RECONCILED` |
| `COUNT_VARIANCE` | Diferencia de conteo aplicada | Sí | Sí | Ajuste de ledger con motivo `COUNT_VARIANCE` sobre el conteo |
| `INVENTORY_ADJUSTED` | Ajuste de inventario | Sí | Sí | Ajuste de ledger con motivo manual (DAMAGE, LOSS, FOUND, EXPIRED, OTHER) o cualquier otro sin documento de origen |
| `INVENTORY_TRANSFERRED` | Transferencia entre almacenes | No | Sí | Transferencia entre dos almacenes distintos |
| `BIN_MOVED` | Movimiento de posición | No | **No** | Transferencia dentro del mismo almacén (posición a posición) |
| `PICK_COLLECTED` | Recolección creada | No | Sí | Recolección pasa a `COLLECTED` |
| `PICK_PACKED` | Recolección empacada | No | Sí | Recolección pasa a `PACKED` |
| `PICK_CANCELLED` | Recolección eliminada | Sí | Sí | Recolección pasa a `CANCELLED` |
| `PO_SENT` | Orden de compra enviada | No | Sí | Orden de compra pasa a `SENT` |
| `PO_RECEIVED` | Orden de compra recibida completa | No | Sí | Orden de compra pasa a `RECEIVED` |
| `PO_CANCELLED` | Orden de compra cancelada | Sí | Sí | Orden de compra pasa a `CANCELLED` |
| `PO_SHORTAGE_RESOLVED` | Faltante resuelto | No | Sí | Se registra una resolución de faltante (CLOSE, REORDER o MANUAL_ADJUSTMENT) |
| `CROSSDOCK_COMPLETED` | Cruce de muelle completado | No | Sí | Plan de cruce de muelle pasa a `COMPLETED` (solo con el módulo `CROSSDOCK` encendido) |
| `PRODUCT_DEACTIVATED` | Producto dado de baja | No | Sí | Se audita la baja (`IsActive` 1→0) de un producto |
| `WAREHOUSE_DEACTIVATED` | Almacén dado de baja | Sí | Sí | Almacén pasa a `INACTIVE` |

No hay estatus propio de "Actividad reciente": cada fila del feed refleja una transición o un movimiento que ya
ocurrió en su propia entidad (recibo, tarea, conteo, orden de compra, etc.); ese ciclo de vida está descrito en el
capítulo [06 — Inventario y almacén](06-inventario-y-almacen.md) y en el de Compras. Un evento **obligatorio**
siempre se muestra (con o sin "Solo obligatorios"); un **opcional** se muestra salvo que esté apagado por defecto
(`BIN_MOVED`) o se pida "Solo obligatorios".

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `take` > 50 | `El máximo por página es 50.` | 400 |
| `window` distinto de `24h`, `48h` o `today` | `La ventana debe ser 24h, 48h o today.` | 400 |
| `module` pedido sin ver esa pestaña (sin el permiso del módulo, o con el módulo del tenant apagado) | `No tiene permiso para ver la actividad del módulo {módulo}.` (p. ej. `WAREHOUSE`) | 403 |
| Con el módulo `ANALYTICS` apagado para el tenant | `El módulo 'ANALYTICS' no está habilitado para esta compañía.` (`ModuleDisabledException`, ProblemDetails) | 403 |
| Sin `analytics.view` | Sin cuerpo: lo rechaza la política `[RequirePermission]` del controlador antes de entrar al servicio (403 vacío de ASP.NET Core, no un ProblemDetails del dominio) | 403 |

El 403 por módulo no visible además deja un `SecurityEvent` `PERMISSION_DENIED` (visible en
`GET /api/v1/audit/security-events` para quien tenga `admin.audit`).

---

## 2. Indicadores y gráfico de Almacén en el Pulso

Qué hace: agrega al Pulso del día tres indicadores y un gráfico nuevos, de sistema (`IsSystem`), visibles para toda
la organización (`ShowInPulse = true`), en el módulo `WAREHOUSE`:

- **Unidades recibidas** — suma con signo de los movimientos de recepción (`RECEIPT`) más los ajustes de ledger con
  motivo `RECEIPT_VARIANCE`, de los últimos 7 días. Es el neto de ambos, no solo lo esperado en el recibo: un recibo
  con 10 unidades esperadas y 8 recibidas suma 8, no 10 (una línea de recepción por lo esperado más un ajuste por la
  diferencia; ver la FAQ).
- **Conteos con diferencia** — cantidad de conteos cíclicos reconciliados (`RECONCILED`) en los últimos 30 días con
  al menos una línea con diferencia o con un ajuste de inventario enlazado (aunque sobrantes y faltantes del mismo
  conteo se compensen en el total neto).
- **Productos bajo mínimo** y **Productos activos** — ya existían desde el Lote 6 (mismo filtro que la vista
  "Inventario bajo mínimo"); no se duplican en este lote.
- **Movimientos de inventario por tipo** (gráfico de barras) — suma con signo de la cantidad de los movimientos de
  inventario de los últimos 7 días, agrupada por tipo de movimiento (recepción, despacho, ajuste, transferencia).

Quién puede: se ven en el Pulso del día con el mismo permiso que cualquier indicador/gráfico de sistema
(`analytics.view` y el módulo `ANALYTICS`); son de sistema y visibles a todo el tenant, no requieren `inventory.view`.

Cómo se usa: no hay endpoint propio; se leen con el resto del Pulso, `GET /api/v1/analytics/pulse`, y se listan como
cualquier indicador con `GET /api/v1/analytics/indicators` / `GET /api/v1/analytics/charts`.

### Validaciones

No hay validaciones propias de esta sección: son datos de sistema sembrados por el proveedor, sin captura del
usuario.

---

## Preguntas frecuentes de este capítulo

Ver [faq.md](faq.md), sección "Lote 7A — Pulso de almacén y Actividad reciente", para el detalle de cada mensaje
(qué significa y qué hacer), incluidos por qué un ajuste `FOUND` aparece dos veces, qué pasa al renombrar o
deshabilitar un evento del catálogo desde el override del tenant, por qué dar de baja una categoría no aparece como
"Producto dado de baja", y por qué alguien sin `inventory.view` puede ver conteos cíclicos en Análisis (decisión a
revisar, no un defecto de este capítulo).
