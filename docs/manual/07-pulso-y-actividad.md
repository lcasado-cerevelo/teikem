# Capítulo 07 — Pulso del día y Actividad reciente (Lote 7A: Almacén; Lote F8a: paneles, permisos y orden; Lote 14: Necesita tu atención; Lote 15: Pulso del día)

Este capítulo describe la parte de **Almacén** de "Actividad reciente" (el panel de eventos recientes del Pulso del
día), los indicadores/gráfico de Almacén que se agregan al Pulso en el Lote 7A y, desde el Lote F8a, **cómo se arma el
Pulso de cada usuario**: paneles con su propio permiso, qué indicadores y gráficos puede leer cada quien, y el orden en dos
niveles (compañía y usuario) — sección 3; desde el Lote 14, el panel **Necesita tu atención** (sección 4); y, desde el Lote 15, la franja **Almacén hoy**, las filas fijas, los indicadores por fila, los gráficos siempre dibujados, los 2 gráficos de la compañía y los **días de Puerto Rico** (sección 5). Operación (7B) y Contabilidad (7C)
agregan su propia pestaña de Actividad reciente y sus propios indicadores más adelante, sobre la misma
infraestructura. Los mensajes están verificados contra el código (`src/Teikem.Infrastructure/Services/Activity/
ActivityRules.cs`, `src/Teikem.Infrastructure/Services/Activity/WarehouseActivityProvider.cs`,
`src/Teikem.Infrastructure/Services/ActivityFeedService.cs`, `src/Teikem.Api/Controllers/ActivityController.cs`,
`src/Teikem.Infrastructure/Services/ProductService.cs`, `src/Teikem.Infrastructure/Seeding/
SystemAnalyticsSeeder.cs`, y desde el Lote 15 `src/Teikem.Infrastructure/Services/WarehousePulseService.cs`, `src/Teikem.Domain/Wms/WarehousePulseRules.cs`, `src/Teikem.Infrastructure/Services/AnalyticsService.cs` y `src/Teikem.Infrastructure/Analytics/AnalyticsEngine.cs`). Las preguntas y respuestas de cada mensaje están en [faq.md](faq.md). El Pulso del día en
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
- `window` — `24h` (por defecto), `48h` o `today` (desde la medianoche de **hoy en hora de Puerto Rico**, 04:00 UTC; desde el
  Lote 15. Antes era la medianoche UTC. La compañía todavía no guarda su zona: es la de Puerto Rico para todas).
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
| `COUNT_RECONCILED` | Conteo reconciliado | Sí | Sí | Conteo cíclico pasa a `RECONCILED` (Concordancia) o `RECONCILED_VARIANCE` (Diferencia, desde el Lote 14) |
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
- **Conteos con diferencia** — cantidad de conteos cíclicos en estatus **Diferencia** (`RECONCILED_VARIANCE`) en los últimos 30 días.
  Desde el Lote 14 el filtro del indicador es `StatusCode eq RECONCILED_VARIANCE` (antes era `RECONCILED` con "tiene diferencia"): un
  conteo queda en Diferencia cuando al confirmarlo se asentó al menos un movimiento (capítulo 06, sección 6). El seed corrige el filtro
  en las compañías ya creadas, solo si tenían el filtro anterior exacto (uno personalizado no se toca).
- **Productos bajo mínimo** y **Productos activos** — ya existían desde el Lote 6 (mismo filtro que la vista
  "Inventario bajo mínimo"); no se duplican en este lote.
- **Movimientos de inventario por tipo** (gráfico de barras) — desde el Lote 15 mide **unidades movidas en positivo** (campo
  `Units`, el valor absoluto de la cantidad; antes sumaba la cantidad con signo y el despacho salía negativo) de los
  últimos 7 días, agrupada por tipo de movimiento (recepción, despacho, ajuste, transferencia, cruce de muelle). Ya no es de
  sistema: es un **gráfico de la compañía** que se edita (sección 5.5).

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
| `pulse.attention` (Lote 14) | Ver la sección Necesita tu atención del Pulso / See the Needs your attention section | Panel Necesita tu atención (sección 4). Sin permiso de datos ni módulo en el panel: cada tipo de aviso decide el suyo |

Organizar **mi** Pulso no exige permiso (son preferencias propias). No existe `pulse.view`: la pantalla de inicio siempre
existe; si el usuario no tiene ningún panel, ve la bienvenida.

Plantillas de rol: **Admin de compañía** (TenantAdmin) todos (los 5 de F8a y `pulse.attention`; la franja "Almacén hoy" usa `pulse.warehouse`, no hay permiso nuevo); **Operador de almacén** `pulse.warehouse`,
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
| `ATTENTION` (Necesita tu atención, Lote 14) | `pulse.attention` | (por tipo de aviso) | (por tipo de aviso) | 5 |
| `WAREHOUSE_DAY` (Almacén hoy, Lote 15) | `pulse.warehouse` | `inventory.view` | `WMS_LOTSERIAL` | −10 |
| `WAREHOUSE` (Almacén) | `pulse.warehouse` | `inventory.view` | `WMS_LOTSERIAL` | 40 |
| `ACTIVITY` (Actividad reciente) | `pulse.activity` | `analytics.view` | `ANALYTICS` | 50 |

Reservados para lotes futuros (no aparecen todavía): `ORDERS_RIVER` (10, "Paquetes en la calle"), `COD_RIVER` (11,
"Dinero COD de regreso"), `DECISIONS` (15, "Necesita tu decisión") y `RADAR` (60). El panel `ATTENTION` (Lote 14) no reemplaza a
`DECISIONS`: usa su propia clave porque el dueño lo llama "Necesita tu atención"; con orden 5 queda arriba de los indicadores y gráficos. Desde el Lote 15, `WAREHOUSE_DAY` (la franja "Almacén hoy", orden −10) va **primero**, incluso en los Pulsos ya organizados (Organizar guarda el orden como índice × 10 desde 0, así que solo un orden negativo lo deja arriba). Orden por defecto: `WAREHOUSE_DAY`, `ATTENTION`, `INDICATORS`, `CHARTS`, `WAREHOUSE`, `ACTIVITY`. Con el módulo `ANALYTICS` apagado el
Pulso solo puede mostrar los paneles de Almacén (la franja y el panel Almacén).

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

## 4. Necesita tu atención (Lote 14)

Qué hace: es el panel de arriba del Pulso del día. Lista lo que **necesita que una persona lo revise**. Hoy tiene un solo tipo de aviso:
un **descuadre Kárdex ↔ saldo** pendiente (capítulo 06, sección 3.3). Cada fila dice "Descuadre en {sku}" (o "en el total de {sku}"), el
producto, dónde (almacén, posición y lote, o "todas las posiciones"), las cifras **Kárdex**, **Saldo** y **Diferencia** (saldo − Kárdex,
con signo) y desde cuándo está pendiente, y trae el botón **Revisar**. Muestra los **5 más antiguos**; **Ver todos (N)** lleva a la lista
completa. Sin nada pendiente dice **"Todo en orden"** ("No hay nada pendiente de revisar."). No hay una tabla de avisos: cada tipo calcula
sus pendientes al leer, así que un descuadre resuelto desaparece del panel al instante. Más adelante se le podrán sumar avisos de
Operación y de COD con sus propios proveedores.

Quién puede: el permiso **`pulse.attention`** (categoría PULSE) abre el panel. Además, **cada tipo de aviso pide el suyo**: los
descuadres, `inventory.view` con el módulo **WMS_LOTSERIAL** encendido. Lo que la persona no puede ver, no suma (no da 403: el panel es la
suma de lo que ve). Plantillas de rol: Admin de compañía, Operador de almacén, Facturación y Solo lectura lo traen; Despachador y Chofer, no.
Al actualizar la plataforma, todo rol de compañía que ya tenía `inventory.view` recibió `pulse.attention` **una sola vez**; lo que un
administrador quite después no vuelve. Como los demás paneles, se puede **ocultar y mover** desde "Organizar mi Pulso" y "Organizar el de
la compañía" (sección 3).

Cómo se usa: `GET /api/v1/analytics/attention` (permiso `pulse.attention`; sin módulo en la política). Respuesta:

```
{ "total": 7,
  "items": [ { "code": "INVENTORY_DISCREPANCY", "module": "WAREHOUSE", "tone": "danger", "count": 1,
               "params": { "publicId": "…", "kind": "BALANCE", "sku": "PROD-100", "productName": "…", "warehouse": "ALM-01",
                           "bin": "A01-R01-N1-P01", "lot": "", "where": "A01-R01-N1-P01", "ledgerQty": "10",
                           "balanceQty": "11", "difference": "1", "detectedAtUtc": "2026-09-30T13:41:02Z", "checkCount": "1" },
               "route": "/warehouse/kardex", "query": { "tab": "reconciliation", "discrepancy": "…" },
               "sinceUtc": "2026-09-30T13:41:02Z" } ],
  "groups": [ { "code": "INVENTORY_DISCREPANCY", "module": "WAREHOUSE", "total": 7, "route": "/warehouse/kardex",
                "query": { "tab": "reconciliation", "status": "OPEN" } } ] }
```

`total` es la suma de pendientes de todos los tipos que la persona puede ver ("Ver todos (N)"); `items`, los 5 más antiguos; `groups`, el
total y la ruta de "Ver todos" por tipo. Los números van como cadenas con punto decimal y las fechas en ISO 8601 UTC; la web arma el texto
con el idioma de la persona. "Revisar" abre el Kárdex en la pestaña **Conciliación** con ese descuadre abierto
(`/warehouse/kardex?tab=reconciliation&discrepancy=<publicId>`).

### Conteos abiertos (panel Almacén)

La tarjeta **Conteos abiertos** del panel Almacén (capítulo F7A) cuenta ahora los conteos cíclicos **Pendientes y Contados** con el
**total real**: usa `GET /api/v1/cycle-counts/page?status=OPEN&status=COUNTED&take=1` y lee `total`. Antes contaba el largo de la lista
de `GET /api/v1/cycle-counts`, que se corta en 200, así que con muchos conteos ("lo cambiado" crea uno por posición) la cifra se
quedaba en 200. Los conteos Concordancia y Diferencia ya no cuentan como abiertos.

### Validaciones

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `GET /api/v1/analytics/attention` sin el permiso `pulse.attention` | `Falta el permiso 'pulse.attention'.` | 403 |
| Sin sesión | (sin cuerpo de la aplicación) | 401 |

No hay otra validación: el panel es de solo lectura. Un tipo de aviso cuyo módulo está apagado o cuyo permiso falta simplemente no aparece.

### Casos frecuentes

- **No veo "Necesita tu atención".** Le falta `pulse.attention` (pídalo a su administrador) o el administrador ocultó el panel en el Pulso
  de la compañía o en el suyo ("Organizar"). Si tiene el panel pero no `inventory.view`, o el módulo WMS_LOTSERIAL está apagado, el panel
  aparece siempre en "Todo en orden": los descuadres no suman.
- **Dice "Todo en orden" pero sé que hay un descuadre.** La revisión automática tarda unos segundos después de cada movimiento; el panel
  se vuelve a pedir cada vez que se abre el Pulso. Si el descuadre se provocó directamente en la base de datos, pulse **Ejecutar
  conciliación** en Kárdex › Conciliación.
- **El operador no puede resolver un descuadre.** Ve la fila y **Revisar**, pero **Corregir** y **Descartar** exigen `inventory.adjust`,
  que el Operador de almacén no trae en su plantilla.

---

## 5. Pulso del día (Lote 15)

Este lote reordena el Pulso: pone arriba una franja de números de Almacén, deja fijas las filas de arriba al desplazarse, agrupa los
indicadores en una fila por módulo, dibuja **siempre** los gráficos como gráfico y convierte dos gráficos de Almacén en gráficos de la
compañía que se pueden editar. Además, **todo el análisis pasa a contar los días en hora de Puerto Rico** (sección 5.6). Las pantallas
están en [F7A — Pulso: Almacén hoy](frontend/f7a-pulso-almacen-y-actividad.md).

### 5.1 Franja "Almacén hoy · últimos 7 días"

Qué hace: es la fila de cuatro tarjetas que va justo debajo de la fecha. Muestra lo que pasó **hoy** en el almacén y cómo vienen los
últimos 7 días (hoy incluido). Es el panel `WAREHOUSE_DAY`, con orden −10: siempre queda primero, también en los Pulsos que alguien ya
organizó.

Quién puede: el permiso **`pulse.warehouse`** con **`inventory.view`** y el módulo **`WMS_LOTSERIAL`** encendido (los mismos del panel
Almacén; no hay permiso nuevo). Lo traen el Admin de compañía y el Operador de almacén. El endpoint que la alimenta pide `inventory.view`.

Cómo se usa: en la pantalla de inicio no hay nada que configurar. Por API:
`GET /api/v1/inventory/pulse/days?warehousePublicIds=<guid>&days=7` (`warehousePublicIds` se puede repetir; sin él son todos los almacenes;
`days` es de 1 a 14 y por defecto 7; la pantalla siempre pide 7). Respuesta abreviada:

```
{ "timeZone": "America/Puerto_Rico", "today": "2026-09-30",
  "fromUtc": "2026-09-24T04:00:00Z", "toUtc": "2026-10-01T04:00:00Z",
  "days": [ { "date": "2026-09-24", "receivedUnits": 0, "receivedMovements": 0, "outboundUnits": 0,
              "outboundMovements": 0, "countsWithVariance": 0 },
            … 7 filas, de la más vieja a hoy … ],
  "receivedToday": 12, "receivedTotal": 72, "outboundToday": 5, "outboundTotal": 11,
  "countsWithVarianceToday": 1, "countsWithVarianceTotal": 3, "belowMinProducts": 1,
  "countsAlert": true, "belowMinAlert": true }
```

Siempre vienen tantas filas como `days`: un día sin movimiento trae ceros.

**Qué cuenta cada tarjeta**

| Tarjeta | Número grande | Texto pequeño | Barritas | Qué cuenta | Naranja |
|---|---|---|---|---|---|
| Unidades recibidas | Lo de hoy | "7 días: N" | 7 | Recepciones (`RECEIPT`) más los ajustes por diferencia de recibo (`RECEIPT_VARIANCE`): lo que **de verdad llegó**. Si se esperaban 10 y llegaron 8, cuenta 8 | Nunca |
| Unidades de salida | Lo de hoy | "7 días: N" | 7 | Recolección (`ISSUE`) más cruce de muelle (`CROSSDOCK`). **Eliminar una recolección resta** esas unidades el día en que se elimina. No cuenta daños, pérdidas, vencidos, conteos ni transferencias | Nunca |
| Conteos con diferencia | Los conteos cíclicos cerrados hoy en estatus **Diferencia** | "7 días: N" | 7 | Conteos activos en `RECONCILED_VARIANCE`, por el día en que se cerraron | Si hoy hubo alguno |
| Productos bajo mínimo | Los que están bajo su mínimo **ahora** | "en este momento" | Ninguna | La misma consulta que el panel Almacén (`GET /products?belowMin=true`): la cifra es idéntica | Si hay alguno |

- El inventario baja al **recolectar**, no al despachar el camión: por eso "salida" cuenta recolecciones (capítulo 06, sección 7).
- Las barritas son 7, una por día, la de la derecha es **hoy** y crece durante el día. Un día sin movimiento es una barrita vacía (una
  línea). Pasar el mouse sobre una barrita muestra la fecha completa y la cantidad ("Hoy, miércoles, 30 de septiembre de 2026 · Unidades: 72").
  Las barritas solo muestran: no se hace clic en ellas.
- Los 7 días son **días de Puerto Rico**: cada uno empieza a las 00:00 de Puerto Rico (04:00 UTC). Un recibo hecho a las 23:59 de Puerto Rico
  cuenta en ese día, no en el siguiente.
- La tarjeta pasa a **naranja** (borde y número, y la barrita de hoy en Conteos) según las banderas `countsAlert` y `belowMinAlert` que
  manda el servidor. La franja es violeta el resto del tiempo.
- La salida de un día puede salir **negativa** si ese día solo se eliminaron recolecciones (la reversa resta).
- Mientras carga, cada número dice "…". Si la consulta falla (por ejemplo, un 403 al cambiar de compañía), dice "—" y no saca al
  usuario del Pulso. La franja se vuelve a pedir cada 5 minutos, para que cambie de día a medianoche, y se refresca sola al hacer
  movimientos de inventario en la sesión.

**A dónde lleva el clic** (toda la tarjeta es un enlace; con un almacén elegido se agrega `warehousePublicIds`)

| Tarjeta | Pantalla | Filtros que lleva |
|---|---|---|
| Unidades recibidas | Kárdex de movimientos (`/warehouse/kardex`) | Tipo Recepción, y Desde y Hasta = los 7 días (de hace 6 días a hoy) |
| Unidades de salida | Kárdex de movimientos | Tipos Despacho y Cruce de muelle, y los 7 días |
| Conteos con diferencia | Conteo cíclico (`/warehouse/cycle-counts`) | Estatus Diferencia (sin fechas: ver FAQ) |
| Productos bajo mínimo | Productos e inventario (`/warehouse/products`) | Recuadro "Bajo mínimo" marcado |

Conteo cíclico y Productos e inventario leen esos filtros de la dirección una vez, al abrirse. Ojo con la suma del Kárdex: ver la FAQ.

**Almacén de la franja.** Un selector chico junto al título ("Todos los almacenes" o el **código** del almacén; el nombre aparece al pasar
el mouse). Es el **mismo** almacén del panel "Almacén" de más abajo: cambiarlo en uno lo cambia en el otro, y se recuerda por usuario en
ese navegador. Las cuatro tarjetas se calculan con él.

**Validaciones**

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `days` | Entero de 1 a 14 | `Los días deben estar entre 1 y 14.` (viene en `errors.days`) | 400 |
| Sin `inventory.view` | Lo rechaza la política del controlador | (sin cuerpo de la aplicación) | 403 |
| Módulo `WMS_LOTSERIAL` apagado | El módulo de la clase | `El módulo 'WMS_LOTSERIAL' no está habilitado para esta compañía.` | 403 |
| Sin sesión | — | (sin cuerpo de la aplicación) | 401 |
| `warehousePublicIds` con un almacén que no existe o es de otra compañía | No es error: da todo en cero (nunca cae en "todos") | — | 200 |

**Estatus y transiciones.** La franja no tiene estatus propios ni cambia ningún estatus: solo lee. Qué mueve cada tarjeta:

| Tarjeta | Lo que la mueve | Quién | Qué valida | Efecto | Qué bloquea |
|---|---|---|---|---|---|
| Unidades recibidas | Confirmar un recibo (capítulo 06, sección 4) | Quien confirma el recibo | Lo del recibo | Un movimiento `RECEIPT` (y un ajuste `RECEIPT_VARIANCE` si hay diferencia) | Nada |
| Unidades de salida | Crear una recolección o un cruce de muelle; eliminar una recolección resta | Quien recolecta o elimina | Lo de la recolección | Un movimiento `ISSUE` o `CROSSDOCK`; al eliminar, un ajuste `PICK_BATCH_REVERSAL` | Nada |
| Conteos con diferencia | Confirmar un conteo cíclico que asienta algún movimiento: Pendiente o Contado → **Diferencia** (capítulo 06, sección 6) | Quien tenga `warehouse.count` | Lo del conteo | Estatus `RECONCILED_VARIANCE` con su fecha de cierre | Un conteo Diferencia ya no se edita |
| Productos bajo mínimo | Cualquier movimiento que cambie lo disponible, o cambiar el mínimo de un producto | Según la acción | — | — | Nada |

### 5.2 Filas fijas al desplazarse

Qué hace: al bajar por el Pulso, quedan **fijas arriba** la fila de la fecha (con los botones "Organizar mi Pulso" y "Organizar el de la
compañía") y, justo debajo, la franja "Almacén hoy". El saludo ("Bienvenido…") y el chip "Pulso de la compañía" se desplazan, y "Necesita
tu atención" y todo lo demás pasan por debajo. Solo pasa en la pantalla de inicio; Indicadores y Gráficos no cambian.

Cuándo deja de estar fija la franja:

| Situación | Qué queda fijo |
|---|---|
| Lo normal (la franja es el primer panel) | La fecha y la franja |
| Con **Organizar** se oculta la franja o se baja de lugar (otro panel queda primero) | Solo la fecha |
| Se está organizando (modo Organizar abierto) | Nada de esto: la barra de Organizar ya es fija |
| Cualquier otro panel que quede primero (Actividad reciente, gráficos…) | Solo la fecha: solo puede quedar fija una franja de números, nunca un panel alto |
| Celular (menos de 720 px de ancho) | La fecha y la franja, compactas: 4 cuadros en 2×2 con el número y las barritas, sin el texto pequeño ni "· últimos 7 días" |
| Celular acostado (menos de 560 px de alto) | Solo la fecha |
| Ancho de 720 a 980 px | La franja va 2×2 sin tuberías (con el texto pequeño) |

Desde Organizar se explica con la nota "La franja que quede justo debajo de la fecha se queda fija al desplazarse; si la oculta o la baja,
solo queda fija la fecha." Para quitar la franja fija: ocúltela o bájela con **Organizar mi Pulso** (solo para usted) o **Organizar el de
la compañía** (para todos los que no tengan un Pulso propio, permiso `pulse.organize_company`).

### 5.3 Indicadores en una fila por módulo

"Tus indicadores" muestra ahora **una fila por módulo de negocio**, cada una con su etiqueta, en el orden del menú: **Operación**,
**Almacén** y **Contabilidad** (esta solo si hay algún indicador de ese módulo, por ejemplo "COD por cobrar"). Dentro de cada fila el orden
es el de siempre. "Cambios registrados", "Accesos fallidos" y "Usuarios activos" están marcados como Operación y salen en esa fila. La
etiqueta pequeña de cada tarjeta muestra solo el rango ("Últimos 7 días"), porque el módulo ya lo dice la fila.

En **Organizar** los indicadores se listan por fila y **solo se mueven dentro de su fila** (con la nota "Los indicadores se muestran en
una línea por módulo; aquí se ordenan dentro de su línea."). Al guardar, el orden se guarda por fila.

### 5.4 Los gráficos siempre se dibujan (y "Otras")

- **Siempre gráfico.** Antes, un gráfico con 3 puntos o menos se mostraba como una lista de "etiqueta · valor". Ahora **siempre** es un
  gráfico, tenga los puntos que tenga: una dona de una sola rebanada, una barra sola y una línea de un solo día se dibujan (la línea con 12
  puntos o menos pinta cada punto). Solo un gráfico **sin ningún punto** muestra el aviso: "Este gráfico no tiene datos en el rango
  configurado." en el Pulso, y "Este gráfico no tiene datos con los filtros y el rango actuales." en Análisis → Gráficos.
- **Tooltip y accesibilidad.** Al pasar el mouse se ve el valor y, si el gráfico agrupa por día, la fecha completa ("miércoles, 30 de
  septiembre de 2026"; el eje la muestra corta, "30 sep"). Cada gráfico lleva su descripción con los valores para lectores de pantalla.
- **Dona.** Lleva el total al centro; si el total es largo, en forma compacta ("$1.2M").
- **2 por fila.** En "Tus gráficos" nunca hay más de 2 gráficos por fila (en celular, uno debajo del otro). Análisis → Gráficos no cambia.
- **"Otras".** En barras, dona y pastel que **suman o cuentan** (SUM o COUNT), si hay más de 8 grupos se muestran los **7 mayores** y un
  último punto **"Otras"** (en inglés "Others") con la suma exacta del resto; la suma de los puntos es el total. Con promedio, mínimo o
  máximo no se junta nada: siguen los 8 mayores. **Ojo:** esto también cambia las barras que ya existían con más de 8 grupos (por ejemplo
  "Cambios por usuario"): antes mostraban los 8 mayores. La vista previa del editor de gráficos hace el mismo cálculo.

### 5.5 Los 2 gráficos de almacén de la compañía

Qué hace: dos gráficos que ya existían de fábrica ("Valor de inventario por categoría" y "Movimientos de inventario por tipo") dejan de
ser de sistema y pasan a ser **gráficos de la compañía**: sin dueño, editables y borrables por quien tenga el permiso, y sembrados en
**todas** las compañías (también en las ya creadas, con la actualización de la base).

| | Valor de inventario por categoría | Movimientos de inventario por tipo |
|---|---|---|
| Tipo | Dona con el total en el centro | Barras verticales |
| Fuente | `STOCK_BALANCE` | `INVENTORY_TRANSACTION` |
| Medida | Suma de `CostValue` ("Valor a costo": existencia en mano por costo de compra), en dinero | Suma de **`Units`** (unidades movidas, siempre positivas) |
| Agrupa por | Categoría del producto: 7 mayores más "Otras" | Tipo de movimiento (Recepción, Despacho, Transferencia, Ajuste, Cruce de muelle) |
| Período | Ninguno: es la foto de ahora (sin botón "Rango") | Últimos 7 días; cada usuario lo cambia con **Rango** (no hay opción de 5 días) |
| Módulo y visibilidad | Almacén, toda la compañía | Almacén, toda la compañía |
| Posición en "Tus gráficos" | Primera fila, a la izquierda (orden 1) | Primera fila, a la derecha (orden 2) |

- **Quién los edita o los borra:** quien tenga el permiso `analytics.manage` ("Crear vistas, indicadores y gráficos"; hoy el administrador
  de la compañía). En Análisis → Gráficos llevan el chip **"De la compañía"** y, con el permiso, los botones Editar y Eliminar. Quien tenga
  `analytics.dates` también cambia su rango por defecto.
- **Si se borran, no vuelven.** Al eliminar uno, la pantalla avisa: "¿Eliminar el gráfico {nombre}? Es de la compañía: una vez eliminado no
  se vuelve a crear." Ni renombrarlo ni borrarlo lo recrea en una actualización futura. Ocultarlos (con Organizar o con "Mostrar en Pulso")
  los deja ocultos como a cualquier otro.
- **Posición.** En una compañía que **ya organizó** sus gráficos (algún gráfico activo con orden 0) se respeta su orden y no se mueven a la
  primera fila.
- **Compañías ya creadas.** Solo se convierte el gráfico de fábrica que sigue **exactamente** como se sembró (mismo nombre, fuente, campo y
  tipo). Uno que la compañía ya tocó no se modifica.
- **Los otros gráficos repetidos no se tocan** ("Movimientos por tipo" en dona de 30 días queda como estaba).

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Editar o borrar (`PUT` o `DELETE /api/v1/analytics/charts/{id}`) sin `analytics.manage` | (sin cuerpo de la aplicación: lo rechaza la política del controlador) | 403 |
| Editar o borrar un gráfico **de sistema** | `Los elementos por default de la plataforma no se editan ni se eliminan.` | 403 |
| Editar o borrar un gráfico que **tiene otro dueño** | `Solo el dueño puede editar o eliminar este elemento.` | 403 |
| Tipo de gráfico distinto de barra, dona o línea (el pastel `PIE` no está habilitado) | `Tipo de gráfico: BAR, DONUT o LINE.` (campo `chartType`) | 400 |
| Crear un gráfico con un nombre que ya usa uno activo | `Ya existe el gráfico '{nombre}'.` | 409 |
| Un gráfico que el usuario no ve o no puede leer | `Gráfico '{id}' no encontrado.` | 404 |

### 5.6 "Hoy" y los días, en hora de Puerto Rico

Desde el Lote 15, todo lo que depende del día usa la hora de Puerto Rico (UTC−4, sin horario de verano), no el día UTC. El día empieza a las
00:00 de Puerto Rico (04:00 UTC). **Qué cambió de comportamiento:**

| Dónde | Qué pasa ahora |
|---|---|
| Rangos de indicadores, gráficos, vistas y exportaciones (**Últimos 7 días, Últimos 30 días, Mes actual y Rango personalizado**) | Son días de Puerto Rico. "Últimos 7 días" = hoy y los 6 anteriores, desde las 00:00 locales del primer día hasta las 00:00 locales de mañana. Un movimiento de las 20:00 a las 24:00 de Puerto Rico cuenta en su día local (antes contaba en el día siguiente) |
| Gráficos agrupados por fecha | Un punto por día de Puerto Rico. En "Movimientos por día", la etiqueta es `2026-09-30` |
| Campo `Date` de la fuente de movimientos | Es el día de Puerto Rico y sale como `"2026-09-30"` (antes `"2026-09-30T00:00:00"` en UTC) |
| Rutas y contratos | Su día de calendario se compara con el rango local |
| Vencimientos (documentos de flota, choferes, contratos vigentes) | "Hoy" es el de Puerto Rico |
| Actividad reciente, ventana **Hoy** | Desde las 00:00 de Puerto Rico |
| Franja "Almacén hoy" | Sus 7 días son los mismos días locales, así que la suma de la franja coincide con el indicador "últimos 7 días" y con un gráfico por día |

**Sigue en UTC (fuera de este lote):** la validación de mínimo y máximo de fechas de los campos personalizados, el "hoy" de la ficha de
cliente y de los documentos y tarifas del chofer. Un dato de esas pantallas puede diferir en horas de un indicador o gráfico. La zona por
compañía queda para un lote posterior (hoy es la de Puerto Rico para todas).

### 5.7 Indicador "Descuadres pendientes"

Cuenta los descuadres Kárdex ↔ saldo en estatus **Pendiente** (`OPEN`, capítulo 06, sección 3.3), en Almacén, sin rango de fecha (es el
estado de ahora). Es un indicador de sistema y **viene apagado en el Pulso**, porque "Necesita tu atención" (sección 4) ya los muestra.
Aparece en Análisis → Indicadores; se puede encender con "Mostrar en Pulso del día". Lo ve quien puede leer la fuente de datos
(`inventory.view` con `WMS_LOTSERIAL`).

---

## Preguntas frecuentes de este capítulo

Las del Lote 15 (franja "Almacén hoy", filas fijas, gráficos de la compañía, "Otras" y días de Puerto Rico) están en [faq.md](faq.md), sección "Lote 15".

Ver [faq.md](faq.md), sección "Lote 7A — Pulso de almacén y Actividad reciente", para el detalle de cada mensaje
(qué significa y qué hacer), incluidos por qué un ajuste `FOUND` aparece dos veces, qué pasa al renombrar o
deshabilitar un evento del catálogo desde el override del tenant, por qué dar de baja una categoría no aparece como
"Producto dado de baja", y por qué alguien sin `inventory.view` puede ver conteos cíclicos en Análisis (decisión a
revisar, no un defecto de este capítulo). Las del Pulso por paneles (sección 3) están en la sección "Lote F8a — Pulso del
día: paneles, permisos y orden" de la FAQ: por qué no veo el panel Almacén, por qué mi Pulso no se ve como el de otro
usuario, cómo vuelvo al Pulso de la compañía y por qué un indicador marcado para el Pulso no le sale a un usuario.
