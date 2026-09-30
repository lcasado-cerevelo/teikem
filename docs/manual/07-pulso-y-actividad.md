# Capítulo 07 — Pulso del día y Actividad reciente (Lote 7A: Almacén; Lote F8a: paneles, permisos y orden)

Este capítulo describe la parte de **Almacén** de "Actividad reciente" (el panel de eventos recientes del Pulso del
día), los indicadores/gráfico de Almacén que se agregan al Pulso en el Lote 7A y, desde el Lote F8a, **cómo se arma el
Pulso de cada usuario**: paneles con su propio permiso, qué indicadores y gráficos puede leer cada quien, y el orden en dos
niveles (compañía y usuario) — sección 3. Operación (7B) y Contabilidad (7C)
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
| `RECEIPT_CONFIRMED` | Recibo confirmado | Sí | Sí | Recibo pasa a `RECEIVED` (Completado) o `RECEIVED_VARIANCE` (Completado con diferencia) |
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

Quién puede: se ven en el Pulso del día con la misma regla que cualquier indicador/gráfico (sección 3): el permiso del
panel (`pulse.indicators` / `pulse.charts`) y, **desde el Lote F8a**, poder leer su fuente de datos — todas son de
almacén (`INVENTORY_TRANSACTION`, `CYCLE_COUNT`, `PRODUCT`, `STOCK_BALANCE`), así que exigen `inventory.view` — con el
módulo `WMS_LOTSERIAL` encendido (módulo de negocio `WAREHOUSE`). Hasta el Lote 7A bastaba `analytics.view`.

Cómo se usa: no hay endpoint propio; se leen con el resto del Pulso, `GET /api/v1/analytics/pulse`, y se listan como
cualquier indicador con `GET /api/v1/analytics/indicators` / `GET /api/v1/analytics/charts`.

### Validaciones

No hay validaciones propias de esta sección: son datos de sistema sembrados por el proveedor, sin captura del
usuario.

---

## 3. Pulso del día: paneles, permisos y orden (Lote F8a)

Qué hace: el Pulso del día (la pantalla de inicio) se arma por **paneles**. Cada panel tiene su propio permiso
`pulse.*`, los permisos de lectura de los datos que muestra y el módulo del tenant que debe estar encendido; el servidor
solo devuelve los paneles que el usuario puede ver, y dentro de "Tus indicadores" y "Tus gráficos" solo los elementos cuya
fuente de datos puede leer. Así se configura, por ejemplo, un usuario de almacén que **solo vea lo de almacén**. Además, el
orden y los ocultos se guardan en dos niveles: **el de la compañía** (lo ve todo el que no tenga uno propio) y **el mío**.

### Permisos del Pulso (categoría `PULSE`, "Pulso del día")

| Código | Etiqueta (es / en) | Qué abre |
|---|---|---|
| `pulse.indicators` | Ver indicadores en el Pulso / See indicators on the Pulse | Sección "Tus indicadores" (cada indicador exige además leer su fuente) |
| `pulse.charts` | Ver gráficos en el Pulso / See charts on the Pulse | Sección "Tus gráficos" (ídem) |
| `pulse.warehouse` | Ver el panel Almacén del Pulso / See the Warehouse panel | Panel Almacén. Datos: `inventory.view`; módulo `WMS_LOTSERIAL` |
| `pulse.activity` | Ver Actividad reciente en el Pulso / See Recent activity | Panel Actividad reciente. Datos: `analytics.view`; módulo `ANALYTICS` |
| `pulse.organize_company` | Organizar el Pulso de la compañía / Organize the company Pulse | "Organizar el de la compañía" (`PUT …/layout?scope=company`) |

Organizar **mi** Pulso no exige permiso (son preferencias propias). No existe `pulse.view`: la pantalla de inicio siempre
existe; si el usuario no tiene ningún panel, ve la bienvenida.

Plantillas de rol: **Admin de compañía** (TenantAdmin) los 5; **Operador de almacén** `pulse.warehouse`,
`pulse.indicators`, `pulse.charts`, `pulse.activity`; **Despachador**, **Facturación** y **Solo lectura**
`pulse.indicators`, `pulse.charts`, `pulse.activity`; **Chofer** ninguno. Ojo: el Operador de almacén tiene
`pulse.activity` pero no `analytics.view` (dato del panel), así que **no ve Actividad reciente** hasta que se le dé
`analytics.view`.

Roles que ya existían antes de este lote: al actualizar la plataforma (db-init), cada rol de compañía con nombre de
plantilla recibe los `pulse.*` de su plantilla, y cada rol propio recibe `pulse.indicators`, `pulse.charts` y
`pulse.activity` si tenía `analytics.view`, y `pulse.warehouse` si tenía `inventory.view` (lo que ya veía en el Pulso). Esto
se hace una sola vez, en la actualización que crea los permisos; después, lo que un administrador quite no vuelve.

### Registro de paneles

| Clave | Permiso | Permisos de datos | Módulo | Orden por defecto |
|---|---|---|---|---|
| `INDICATORS` ("Tus indicadores") | `pulse.indicators` | (por elemento) | `ANALYTICS` | 20 |
| `CHARTS` ("Tus gráficos") | `pulse.charts` | (por elemento) | `ANALYTICS` | 30 |
| `WAREHOUSE` (Almacén) | `pulse.warehouse` | `inventory.view` | `WMS_LOTSERIAL` | 40 |
| `ACTIVITY` (Actividad reciente) | `pulse.activity` | `analytics.view` | `ANALYTICS` | 50 |

Reservados para lotes futuros (no aparecen todavía): `ORDERS_RIVER` (10, "Paquetes en la calle"), `COD_RIVER` (11,
"Dinero COD de regreso"), `DECISIONS` (15, "Necesita tu decisión") y `RADAR` (60). Con el módulo `ANALYTICS` apagado el
Pulso solo puede mostrar el panel Almacén.

### Qué indicadores y gráficos ve cada usuario

Un indicador o gráfico aparece en el Pulso (y en las listas de Indicadores y Gráficos) solo si, **todas a la vez**:

1. el usuario tiene `pulse.indicators` (o `pulse.charts`) — solo para el Pulso;
2. puede verlo por su visibilidad (privado suyo, compartido con él o su rol, o de toda la compañía);
3. **puede leer su fuente de datos**: `STOCK_BALANCE`, `INVENTORY_TRANSACTION`, `PRODUCT`, `RECEIPT`, `WAREHOUSE`,
   `WAREHOUSE_TASK`, `PICK_BATCH`, `CYCLE_COUNT` → `inventory.view`; `TRANSPORT_ORDER` → `orders.view`; `TRIP` →
   `trips.view`; `VEHICLE`, `DRIVER`, `WORK_ORDER`, `FUEL_LOG`, `FLEET_DOCUMENT` → `fleet.view`; `CLIENT` → `clients.read`;
   `CONTRACT` → `contracts.read`; `LOCATION` → `locations.read`; `PURCHASE_ORDER` → `purchasing.view`; `AUDIT_LOG` y
   `SECURITY_EVENT` → `admin.audit`; `USER` → `admin.users`;
4. su **módulo de negocio** está encendido para la compañía: Almacén (`WAREHOUSE`) → `WMS_LOTSERIAL`; Operación
   (`OPERATIONS`) → `LTL_GROUND`; Contabilidad (`ACCOUNTING`) → `COD` o `LTL_GROUND`.

Lo que no cumple 3 o 4 no se lista y, pedido por id (`GET …/indicators/{id}`, `…/value`, `…/charts/{id}/data`, "mi rango",
"mi Pulso"), responde 404 "Indicador '…' no encontrado." / "Gráfico '…' no encontrado." (no se revela que existe). Crear o
editar un indicador o gráfico sobre una fuente que el usuario no puede leer responde 404 "Fuente de datos '…' no
encontrado.".

### Orden y visibilidad en dos niveles

- **Panel**: fila propia del usuario → fila de la compañía → registro (orden por defecto y visible). El origen viaja en
  `source`: `user`, `company` o `default`.
- **Indicador o gráfico** dentro de su panel: la preferencia propia (orden y "mostrar en Pulso", campo por campo) → la
  definición, que es el nivel compañía (orden `SortOrder` y "Mostrar en Pulso" de la definición). `source` es `user` si el
  usuario tiene algo propio y `company` si no.
- Orden de la lista: por el orden efectivo y, a igualdad, por nombre.
- `hasPersonalLayout` = el usuario tiene algún orden u oculto propio (incluye el interruptor "Mostrar en Pulso" de un
  indicador o gráfico). `canOrganizeCompany` = tiene `pulse.organize_company`.
- Los ocultos **vienen en la respuesta** (con `isVisible: false`) para que "Organizar" pueda mostrarlos de nuevo; no se
  calculan (valor `null`, gráfico sin puntos) y la pantalla no los pinta.

Cómo se usa:
- `GET /api/v1/analytics/pulse` — `{ "indicators": [ { "id", "name", "value", "isMoney", "dateRangeMode", "fromUtc",
  "toUtc", "businessModule", "sortOrder", "isVisible", "source" } ], "charts": [ …igual + "chartType", "points" ],
  "panels": [ { "key": "INDICATORS", "isVisible": true, "sortOrder": 20, "source": "default" } ], "hasPersonalLayout":
  false, "canOrganizeCompany": true }`. Sin permiso ni módulo propio: cualquier usuario autenticado.
- `PUT /api/v1/analytics/pulse/layout?scope=mine|company` — cuerpo `{ "items": [ { "kind": "indicator", "id": 12,
  "sortOrder": 10, "isVisible": true } ], "panels": [ { "key": "ACTIVITY", "sortOrder": 5, "isVisible": true } ] }`.
  Solo se escribe lo que viene (lo ausente no se toca); repetirlo no cambia nada. `mine` guarda mi orden; `company` exige
  `pulse.organize_company` y guarda el de la compañía (orden y "Mostrar en Pulso" de cada definición y filas de panel de la
  compañía). Devuelve el Pulso ya actualizado.
- `DELETE /api/v1/analytics/pulse/layout/mine` — "Volver al de la compañía": borra mi orden y mis ocultos (paneles,
  indicadores y gráficos); **conserva mis rangos de fecha**. 204.
- `PUT …/indicators/{id}/my-pulse` y `PUT …/charts/{id}/my-pulse` (ya existían) siguen como atajo de un solo elemento:
  equivalen a un item de `PUT …/layout?scope=mine` con solo la visibilidad.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `scope` ausente o distinto de `mine` / `company` (campo `scope`) | `Alcance inválido: use mine o company.` | 400 |
| `panels[i].key` que no está en el registro | `Panel de Pulso desconocido: {clave}.` (p. ej. `Panel de Pulso desconocido: RADAR.`) | 400 |
| `items[i].kind` distinto de `indicator` / `chart` | `Tipo inválido: use indicator o chart.` | 400 |
| `scope=company` sin `pulse.organize_company` | `Falta el permiso 'pulse.organize_company'.` (y `SecurityEvent` `PERMISSION_DENIED`) | 403 |
| Panel del registro cuyo permiso, dato o módulo no tiene el usuario | `Panel de Pulso '{clave}' no encontrado.` (no se revela) | 404 |
| Indicador o gráfico que el usuario no ve o no puede leer (o sin `pulse.indicators` / `pulse.charts`) | `Indicador '{id}' no encontrado.` / `Gráfico '{id}' no encontrado.` | 404 |

Primero se valida la forma de todo el cuerpo (400) y después el alcance (403/404); si algo falla, no se guarda nada.

Estatus y auditoría: no hay estatus. Cada fila de orden de panel (`PulsePanelSetting`, tanto la de la compañía como las
de cada usuario) queda en la bitácora (`AuditLog`, tipo `PULSE_PANEL_SETTING`: alta, cambio y, al volver al de la compañía,
baja); el orden de compañía de un indicador o gráfico queda como cambio de su definición (`INDICATOR_DEFINITION` /
`CHART_DEFINITION`). No genera `SecurityEvent` salvo el 403.

### Casos frecuentes

- **Usuario que solo ve almacén**: rol con `inventory.view`, `warehouse.*` y `pulse.warehouse`, `pulse.indicators`,
  `pulse.charts` (y `analytics.view` + `pulse.activity` si debe ver Actividad reciente), sin `orders.view`, `trips.*`,
  `cod.*`. Su Pulso muestra el panel Almacén y solo los indicadores y gráficos de fuentes de almacén; uno sobre órdenes no
  aparece aunque esté en el Pulso de la compañía.
- **Reordenar el inicio para todos**: quien tiene `pulse.organize_company` usa "Organizar el de la compañía"; lo ven
  todos los que no tengan un Pulso propio. Quien ya organizó el suyo sigue viendo el suyo hasta que pulse "Volver al de la
  compañía".

---

## Preguntas frecuentes de este capítulo

Ver [faq.md](faq.md), sección "Lote 7A — Pulso de almacén y Actividad reciente", para el detalle de cada mensaje
(qué significa y qué hacer), incluidos por qué un ajuste `FOUND` aparece dos veces, qué pasa al renombrar o
deshabilitar un evento del catálogo desde el override del tenant, por qué dar de baja una categoría no aparece como
"Producto dado de baja", y por qué alguien sin `inventory.view` puede ver conteos cíclicos en Análisis (decisión a
revisar, no un defecto de este capítulo). Las del Pulso por paneles (sección 3) están en la sección "Lote F8a — Pulso del
día: paneles, permisos y orden" de la FAQ: por qué no veo el panel Almacén, por qué mi Pulso no se ve como el de otro
usuario, cómo vuelvo al Pulso de la compañía y por qué un indicador marcado para el Pulso no le sale a un usuario.
