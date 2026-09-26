# Capítulo 05 — Trips y rutas (Lote 5)

Este capítulo describe la planificación diaria de rutas (Trip), la consolidación de órdenes en una ruta, la
optimización automática, la edición manual (secuencia y pin por parada), el despacho y la salida, las zonas de
despacho (extiende el [capítulo 04](04-flota-choferes-mantenimiento.md), sección 2.7), la estación de escaneo
Outbound, "Planificar el día" y el monitor de rutas. Cada sección indica **qué hace**, **quién puede**, **cómo se
usa**, las **validaciones** con el mensaje exacto y el código HTTP, y los **estatus** con sus transiciones y
efectos. Los mensajes están verificados contra el código (`src/Teikem.Domain/Trips/*`,
`src/Teikem.Infrastructure/Trips/*`, `src/Teikem.Infrastructure/Services/{Trip,TripOrder,RouteOptimization,
TripDispatch,TripLifecycle,TripStatusEffect,TripOrderReleaseEffect,TripDayPlanning,OutboundScan,TripMonitor,
DispatchZone}*.cs`, `src/Teikem.Api/Controllers/{Trips,TripOrders,TripRoutes,TripDispatch,TripPlanning,TripMonitor,
Scan,DispatchZones}Controller.cs`). Las preguntas y respuestas de cada mensaje están en [faq.md](faq.md).

Convenciones del capítulo:

- Una ruta (`Trip`) se identifica por `publicId` (GUID) en la URL; su número (`code`, `AAAA-####`) es solo para
  mostrar. Un `publicId` de otra compañía responde **404** `Ruta no encontrada.` como si no existiera (sin oráculo).
- Las paradas (`RouteStop`) no tienen identidad propia fuera de su ruta: se exponen con id entero **solo bajo la
  ruta de su padre**; un `routeStopId` de otra ruta (u otro tenant) también responde **404** (BOLA por id hijo).
- Ninguna solicitud lleva `TenantId` ni ids internos (`TripId`, `RouteId`); el tenant sale siempre de la sesión.
- Todo el módulo vive bajo el módulo **LTL_GROUND** (núcleo, siempre encendido): si por algún motivo estuviera
  apagado, cualquier endpoint de este capítulo respondería `403` `code: "module_disabled"`. Los servicios exigen
  además **CATALOG** encendido cuando la operación toca un chofer o un vehículo (alta/edición de ruta, reasignación
  en bloque, despacho); las zonas y sus miembros siguen bajo **CATALOG** puro, igual que en el capítulo 04.
- Un error de validación responde `400` con `title` y, cuando aplica, `errors` (`{ campo: [mensaje] }`); un
  conflicto (algo ya asignado, ya existe, choque de concurrencia) `409`; una regla de estatus o de negocio `422`
  (a veces con `errors` por orden o por bloqueante); un recurso ajeno o inexistente `404`; falta de permiso o de
  módulo `403`.
- **Sin dinero**: despachar una ruta no crea `DriverTrip`; ningún campo de este capítulo expone montos ni tarifas.
- **Sin motor de ruteo real** (VROOM/OSRM/OR-Tools): el motor de este lote (`HEURISTIC`) y las ETAs son cálculos
  propios, deterministas y sin llamadas externas.

---

## 1. Planificación diaria (Trip): alta, ficha y listado

Qué hace: crea y consulta rutas. El número (`code`, `AAAA-####`) lo asigna el sistema (consecutivo por compañía,
sin reiniciarse por año, inmutable). La ruta nace en **DRAFT**. Si se indica una zona de despacho y no un chofer,
se usa el chofer **estándar** de esa zona: el único chofer activo con esa zona como primaria, si está disponible en
la fecha y el módulo **CATALOG** está encendido (si está apagado, se omite sin error). Si no se indica hora de
salida, se usa **12:00 UTC (08:00 AST) de la fecha del plan**, para que la ficha siempre tenga ETA.

Quién puede: `trips.view` (listar y ver la ficha), `trips.plan` (crear).

Cómo se usa:
- `GET /api/v1/trips?date=&from=&to=&status=&dispatchZoneId=&driverPublicId=&search=&includeCancelled=false`
  (sin `date`/`from`/`to` = hoy UTC; `status` acepta varios; la búsqueda libre corre después de los filtros)
- `POST /api/v1/trips`
- `GET /api/v1/trips/{publicId}` — ficha completa: paradas de la versión vigente con ETA y zona, avisos y
  bloqueantes, "no cupieron" de la última optimización y último ping del chofer.

```json
{ "planDate": "2026-09-28", "dispatchZoneId": 3, "vehiclePublicId": "…", "driverPublicId": null, "plannedStartUtc": null }
```

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `planDate` ausente | `Indique la fecha de la ruta.` (campo `planDate`) | 400 |
| `planDate` anterior a ayer o posterior a hoy + 60 días | `La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.` | 400 |
| `plannedStartUtc` fuera de ±12 h de la fecha del plan | `La hora de salida debe caer en la fecha de la ruta (±12 h por zona horaria).` | 400 |
| `dispatchZoneId` de otro tenant o inexistente | `Zona de despacho no encontrada.` | 404 |
| `dispatchZoneId` inactivo | `La zona de despacho está inactiva.` (campo `dispatchZoneId`) | 400 |
| Módulo `CATALOG` apagado, con `driverPublicId`/`vehiclePublicId` indicado | `El módulo 'CATALOG' no está habilitado para esta compañía.` (`code: module_disabled`) | 403 |
| `driverPublicId`/`vehiclePublicId` de otro tenant o inexistente | `Chofer no encontrado.` / `Vehículo no encontrado.` | 404 |
| Chofer o vehículo no disponible en la fecha | `El chofer no está disponible para despacho: {motivos}.` / `El vehículo no está disponible para despacho: {motivos}.` (misma regla del [capítulo 04](04-flota-choferes-mantenimiento.md), sección 4) | 409 |
| Choque de número por alta simultánea (agotó reintentos internos) | `Ya existe una ruta con ese número.` | 409 |
| Filtro `status` con código desconocido | `Estatus de ruta desconocido: 'X'.` (campo `status`) | 400 |
| `driverPublicId` del filtro, de otro tenant | `Chofer no encontrado.` | 404 |
| `publicId` de otro tenant o inexistente | `Ruta no encontrada.` | 404 |

Ocho altas simultáneas sin zona reciben ocho números consecutivos distintos del mismo año, sin huecos (el contador
se asegura antes de abrir la transacción y se bloquea internamente hasta el commit).

---

## 2. Edición de la cabecera y "Eliminar ruta"

Qué hace: edita fecha, zona, chofer, vehículo y hora de salida (`null` = sin cambio; `clearZone`/`clearDriver`/
`clearVehicle`/`clearPlannedStart` quitan el dato). El **contenido** de la ruta (órdenes, paradas, secuencia) nunca
se toca desde aquí. Una ruta ya despachada solo admite tocar la cabecera si el tenant habilitó la capacidad
`EDIT_TRIP` para ese estatus (por defecto viene **denegada** en `DISPATCHED`, `IN_PROGRESS`, `COMPLETED` y
`CANCELLED`); aun así, la fecha y la zona de una ruta despachada nunca cambian, y el chofer/vehículo/hora de salida
se **cambian**, nunca se quitan. "Eliminar ruta" pasa la ruta a `CANCELLED`, libera sus órdenes (sin tocar su
estatus) y solo es posible desde `DRAFT`/`PLANNED`.

Quién puede: `trips.plan`.

Cómo se usa:
- `PATCH /api/v1/trips/{publicId}` — acepta `rowVersion` para detectar ediciones concurrentes
- `DELETE /api/v1/trips/{publicId}` — cuerpo opcional `{ "comment": "…", "rowVersion": "…" }`, responde `204`
- `POST /api/v1/trips/reassign-zone` — reasignación en bloque

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `code`/`tripCode` en el cuerpo | `El número de la ruta se fija al crearlo; no se puede cambiar.` | 400 |
| `status`/`statusCode`/`toCode` en el cuerpo | `El estatus de la ruta cambia con sus acciones (optimizar, despachar, eliminar).` | 400 |
| `tenantId`/`version`/`routeVersion`/`originWarehouseId` en el cuerpo | `Ese campo no se puede modificar.` | 400 |
| `driverPublicId` + `clearDriver` juntos | `Indique el chofer o quítelo, no ambos.` (campo `driverPublicId`) | 400 |
| `vehiclePublicId` + `clearVehicle` juntos | `Indique el vehículo o quítelo, no ambos.` (campo `vehiclePublicId`) | 400 |
| `dispatchZoneId` + `clearZone` juntos | `Indique la zona o quítela, no ambas.` (campo `dispatchZoneId`) | 400 |
| `plannedStartUtc` + `clearPlannedStart` juntos | `Indique la hora de salida o quítela, no ambas.` (campo `plannedStartUtc`) | 400 |
| `rowVersion` no coincide | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Ruta cerrada (`COMPLETED`/`CANCELLED`) | `La ruta {código} está cerrada; solo se consulta.` | 422 |
| Ruta ya despachada, sin `EDIT_TRIP` habilitada para ese estatus | `La ruta {código} ya fue despachada; no se puede editar ni eliminar.` | 422 |
| Ruta despachada (con `EDIT_TRIP`), cambia `planDate` o `dispatchZoneId` | `La fecha y la zona de una ruta despachada no se cambian.` | 422 |
| Ruta despachada (con `EDIT_TRIP`), quita chofer, vehículo o la hora de salida que ya tenía | `Una ruta despachada debe conservar chofer, vehículo y hora de salida; cámbielos en lugar de quitarlos.` | 422 |
| Nueva fecha fuera de rango | `La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.` (campo `planDate`) | 400 |
| Nueva hora de salida fuera de rango | `La hora de salida debe caer en la fecha de la ruta (±12 h por zona horaria).` (campo `plannedStartUtc`) | 400 |
| Nuevo chofer/vehículo (o el actual, si cambia la fecha) no disponible | mismos 409 de disponibilidad de la sección 1 | 409 |
| `comment` de `DELETE` de más de 500 caracteres | `El comentario admite como máximo 500 caracteres.` | 400 |
| `DELETE` de una ruta que no está en `DRAFT`/`PLANNED` | mismos mensajes de "ruta cerrada" o "ya fue despachada" de arriba | 422 |

### Reasignación en bloque por zona

Qué hace: cambia el chofer de **todas** las rutas abiertas (activas, `DRAFT`/`PLANNED`, con `EDIT_TRIP` habilitada
para su estatus) de una o varias zonas en una fecha. No toca la zona primaria del chofer ni las rutas despachadas.

```json
{ "planDate": "2026-09-28", "dispatchZoneIds": [1, 2], "driverPublicId": "…" }
```

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `planDate` ausente | `Indique la fecha de la ruta.` | 400 |
| `dispatchZoneIds` vacío | `Indique al menos una zona.` (campo `dispatchZoneIds`) | 400 |
| Más de 50 zonas | `Máximo 50 zonas por reasignación.` (campo `dispatchZoneIds`) | 400 |
| `driverPublicId` ausente | `Indique el chofer.` (campo `driverPublicId`) | 400 |
| Alguna zona de otro tenant o inexistente | `Zona de despacho no encontrada.` | 404 |
| `driverPublicId` de otro tenant o inexistente | `Chofer no encontrado.` | 404 |
| Chofer no disponible en la fecha | `El chofer no está disponible para despacho: {motivos}.` | 409 |
| Módulo `CATALOG` apagado | `El módulo 'CATALOG' no está habilitado para esta compañía.` (`code: module_disabled`) | 403 |

Sin rutas que reasignar, la respuesta es `200` con `tripsUpdated: 0` (no es un error). La respuesta trae además los
avisos `DRIVER_DOUBLE_BOOKED` de las rutas actualizadas (el chofer queda en más de una ruta ese día; no bloquea).

---

## 3. Órdenes en la ruta (consolidación multi-cliente)

Qué hace: agrega y quita órdenes confirmadas de la ruta, y lista las órdenes **sin ruta vigente** ("Sin asignar")
con su zona resuelta. Agregar es **atómico** (todo o nada) y **no cambia el estatus de la orden**; quitar la
libera sin tocarlo tampoco. Una orden solo puede estar en una ruta vigente a la vez.

Quién puede: `trips.view` (lista "Sin asignar"), `trips.plan` (agregar/quitar).

Cómo se usa:
- `GET /api/v1/trips/unassigned-orders?dispatchZoneId=&noZone=&postalCode=&city=&clientPublicId=&requestedFrom=&requestedTo=&search=&skip=0&take=100`
- `POST /api/v1/trips/{publicId}/orders` — `{ "orderPublicIds": ["…", "…"], "rowVersion": "…" }`
- `DELETE /api/v1/trips/{publicId}/orders/{orderPublicId}` — cuerpo opcional `{ "rowVersion": "…" }`

### 3.1 Elegibilidad de una orden para una ruta

Una orden es elegible si está **activa**, **no** es una entrega especial, está en una etapa **PIPELINE** posterior
a la inicial y anterior a `IN_TRANSIT` del pipeline del tenant, su estatus tiene la capacidad `ASSIGN_TRIP` y tiene
una parada `DELIVERY` pendiente. En orden de precedencia:

| Motivo | Mensaje exacto |
|---|---|
| Inactiva, o ya en una etapa terminal | `La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.` |
| Es una entrega especial | `Las entregas especiales se asignan al chofer desde la orden; no pasan por Sala de despacho.` |
| Está en la etapa inicial (Entrada) | `La orden está en Entrada; confírmela antes de asignarla a una ruta.` |
| Está en un estatus lateral (`ON_HOLD`, `PARTIAL`, `FAILED`, …) | `La orden está en '{estatus}'; regrésela al pipeline antes de asignarla a una ruta.` |
| Ya está en `IN_TRANSIT` o después | `La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.` |
| Su estatus no tiene la capacidad `ASSIGN_TRIP` | `El estatus actual no permite la acción 'ASSIGN_TRIP'.` |
| No tiene una parada `DELIVERY` pendiente | `La orden no tiene una parada de entrega pendiente.` |

### 3.2 Agregar y quitar

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `orderPublicIds` vacío | `Indique al menos una orden.` (campo `orderPublicIds`) | 400 |
| Más de 200 órdenes distintas | `Agregue como máximo 200 órdenes por solicitud.` (campo `orderPublicIds`) | 400 |
| Algún id ajeno, inexistente o inactivo | `Orden no encontrado.` | 404 |
| Ruta no editable (despachada o cerrada) | mismos mensajes de la sección 2 (`NotEditableMessage`) | 422 |
| Una o más órdenes no elegibles | `Hay órdenes que no se pueden asignar a la ruta.` con `errors: { "{número}": ["{motivo}"] }` (rechaza la solicitud completa) | 422 |
| La orden ya está en ESTA ruta | `La orden ya está en esta ruta.` | 409 |
| La orden ya está en OTRA ruta vigente | `La orden ya está asignada a la ruta {código}.` | 409 |
| Excede el tope técnico de la ruta | `Una ruta admite como máximo 300 paradas.` (campo `orderPublicIds`) | 400 |
| Carrera (dos altas simultáneas a la misma orden en rutas distintas) | `La orden ya está asignada a otra ruta.` | 409 |
| `orderPublicId` de `DELETE`, ajeno o inexistente | `Orden no encontrado.` | 404 |
| La orden no está en ESA ruta | `La orden no está en esta ruta.` | 404 |

### 3.3 Lista "Sin asignar"

Órdenes activas, no especiales, sin ruta vigente y con una parada `DELIVERY` pendiente, con la zona resuelta por
código postal o pueblo (ver sección 5). `ASSIGN_TRIP` no esconde nada de la lista: se valida solo al agregar.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `dispatchZoneId` y `noZone` juntos | `Use dispatchZoneId o noZone, no ambos.` (campo `noZone`) | 400 |
| `requestedFrom` posterior a `requestedTo` | `El rango de fechas es inválido.` (campo `requestedTo`) | 400 |
| `dispatchZoneId` de otro tenant o inexistente | `Zona de despacho no encontrada.` | 404 |
| `clientPublicId` de otro tenant o inexistente | `Cliente no encontrado.` | 404 |

`skip`/`take` se normalizan igual que el listado de órdenes (`take` 1..500, 100 por defecto; `skip` ≥ 0, sin
error). La búsqueda libre (número, empaque, factura, consignatario) corre **después** de los demás filtros.

---

## 4. Optimización, secuencia manual y pin por parada

### 4.1 Optimización automática

Qué hace: reordena la ruta con el motor `HEURISTIC` (determinista: ordena por zona, ventana, código postal y
pueblo, y respeta paradas/peso/volumen del vehículo). Crea una **versión nueva** de la ruta (`n+1`, `OPTIMIZED`),
archiva la anterior intacta y, si la ruta estaba en `DRAFT`, la pasa a `PLANNED`. Lo que no cabe vuelve a "sin
asignar", con su motivo visible en la ficha (`lastRunUnassigned`) mientras siga sin ruta.

Corre en **tres fases** para no bloquear la ruta mientras calcula: (1) toma una instantánea y crea la corrida en
`PENDING`; (2) el motor corre sin bloqueos, con un tiempo máximo de 60 segundos; (3) el resultado se aplica solo si
la ruta vigente y su `rowVersion` no cambiaron desde la instantánea; si cambiaron, la corrida queda en `ERROR` y se
responde `409`.

Quién puede: `trips.optimize`.

Cómo se usa: `POST /api/v1/trips/{publicId}/optimize` — cuerpo opcional `{ "rowVersion": "…" }`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Ruta no editable (despachada o cerrada) | mismos mensajes de la sección 2 | 422 |
| La ruta no tiene paradas | `La ruta no tiene paradas que optimizar.` (no crea corrida) | 422 |
| La ruta cambió (versión o `rowVersion`) mientras el motor calculaba | `La ruta cambió mientras se optimizaba; vuelva a optimizar.` (la corrida queda `ERROR`) | 409 |
| El motor lanzó una excepción, agotó los 60 s o devolvió un resultado inválido | `El optimizador no pudo calcular la ruta; la corrida quedó registrada con error.` (la corrida queda `ERROR`) | 409 |
| `rowVersion` no coincide | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |

Dos optimizaciones simultáneas sobre la misma ruta: ambas crean su corrida; la primera en aplicar gana (`200` con
la versión `n+1`) y la otra recibe el `409` de arriba (o, si no se solaparon, ambas ganan con `n+1` y `n+2`).
Siempre queda una sola versión vigente.

`GET /api/v1/trips/{publicId}/optimization-runs` (`trips.view`) lista las corridas de la ruta, de la más reciente
a la más antigua, con motor, estatus, versión resultante y quién la lanzó.

### 4.2 Reordenamiento manual

Qué hace: cambia a mano la secuencia de la versión vigente (**misma** versión, sin crear una corrida) y recalcula
las ETAs.

Quién puede: `trips.plan`.

Cómo se usa: `PUT /api/v1/trips/{publicId}/route/sequence` — `{ "routeStopIds": [12, 10, 11], "rowVersion": "…" }`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `routeStopIds` no es exactamente la versión vigente (falta, sobra, repite o trae ids de otra ruta) | `La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.` (campo `routeStopIds`) | 400 |
| Ruta no editable | mismos mensajes de la sección 2 | 422 |

### 4.3 Pin manual por parada

Qué hace: fija la coordenada de una parada a mano (por ejemplo, cuando el CP o el pueblo no da un pin exacto),
marca su precisión como `MANUAL` y recalcula las ETAs. Solo en rutas `DRAFT`/`PLANNED`.

Quién puede: `trips.plan`.

Cómo se usa: `PUT /api/v1/trips/{publicId}/stops/{routeStopId}/location` — `{ "lat": 18.4655, "lng": -66.1057, "rowVersion": "…" }`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Falta `lat` o `lng` | `Indique la latitud y la longitud.` | 400 |
| `lat` fuera de -90..90 | `La latitud debe estar entre -90 y 90.` | 400 |
| `lng` fuera de -180..180 | `La longitud debe estar entre -180 y 180.` | 400 |
| `routeStopId` de otra ruta o de una versión archivada | `Parada no encontrada en esta ruta.` | 404 |
| Ruta no editable (ya despachada) | mismos mensajes de la sección 2 | 422 |

El cambio de precisión geográfica (`GeocodeAccuracy` → `MANUAL`) queda en `AuditLog` bajo `TRANSPORT_ORDER`; la
coordenada en sí (`GEOGRAPHY`) no se audita porque no está mapeada como propiedad de la entidad.

### 4.4 Cómo se calcula la ETA

Sin motor de ruteo: por cada tramo con coordenadas en ambos extremos, distancia = línea recta (haversine) × 1.3
(factor de desvío), redondeada a 3 decimales; minutos = el mayor entre 1 y el techo de (km / 35 km/h × 60). El
primer tramo, o un tramo sin coordenadas en algún extremo, usa 15 minutos fijos y distancia nula. Si se llega antes
del inicio de la ventana de la parada, se espera hasta ese inicio; la salida es la llegada más el tiempo de
servicio; la parada queda "tardía" si la llegada pasa el fin de la ventana. Sin hora de salida planificada, las
horas quedan nulas (por eso el alta usa 12:00 UTC por defecto: siempre hay ETA).

---

## 5. Zonas de despacho: miembros y resolución (extiende el capítulo 04, sección 2.7)

Qué hace: agrega y quita los criterios que definen el territorio de una zona (código postal exacto, rango postal o
municipio) y resuelve "código postal o pueblo → zona" con precedencia **código postal exacto > rango postal >
municipio** (sin acentos ni mayúsculas). Un empate entre dos zonas en el mismo nivel deja la dirección **ambigua**
(sin zona). No hay solapamiento entre zonas **activas**: agregar o reactivar un criterio que ya pertenece a otra
zona activa es `409`. Los polígonos todavía no se soportan.

Quién puede: `fleet.view` (listar/resolver), `fleet.manage` (agregar/quitar). Módulo `CATALOG`.

Cómo se usa:
- `GET /api/v1/dispatch-zones/{id}/members`
- `POST /api/v1/dispatch-zones/{id}/members` — `{ "matchType": "POSTAL_CODE", "matchValue": "00949" }`
- `DELETE /api/v1/dispatch-zones/{id}/members/{memberId}` — DELETE físico auditado, `204`
- `GET /api/v1/dispatch-zones/resolve?postalCode=&city=`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `matchType` vacío | `Indique el criterio de la zona (POSTAL_CODE, POSTAL_RANGE o MUNICIPALITY).` (campo `matchType`) | 400 |
| `matchType` desconocido | `Criterio de zona desconocido: 'X'.` (campo `matchType`) | 400 |
| `matchType` = `POLYGON` | `Las zonas por polígono todavía no se soportan; use código postal, rango postal o municipio.` (campo `matchValue`) | 400 |
| `matchValue` vacío (`POSTAL_CODE`/`POSTAL_RANGE`) | `Indique el valor del criterio.` (campo `matchValue`) | 400 |
| `matchValue` vacío (`MUNICIPALITY`) | `Indique el municipio.` (campo `matchValue`) | 400 |
| Código postal inválido | `El código postal debe tener 5 dígitos (ej. 00949).` (campo `matchValue`; se acepta ZIP+4 y se recorta a 5) | 400 |
| Rango postal inválido | `El rango postal debe tener la forma 00900-00999 (inicio menor o igual que el fin).` (campo `matchValue`) | 400 |
| Municipio de más de 120 caracteres | `El municipio admite como máximo 120 caracteres.` (campo `matchValue`) | 400 |
| `id` de zona ajena o inexistente | `Zona de despacho no encontrada.` | 404 |
| Zona inactiva | `La zona de despacho está inactiva.` | 400 |
| El criterio ya existe en ESTA zona | `La zona ya tiene ese criterio.` | 409 |
| El criterio ya pertenece a OTRA zona activa | `El valor '{v}' ya pertenece a la zona {código}.` | 409 |
| `memberId` de otra zona (BOLA por id hijo) | `Criterio de zona no encontrado.` | 404 |
| `resolve` sin `postalCode` ni `city` | `Indique el código postal o el pueblo.` | 400 |
| Inactivar una zona con rutas abiertas (`DRAFT`/`PLANNED`) | `La zona tiene rutas abiertas; ciérrelas o cámbielas de zona antes de inactivarla.` | 409 |
| Reactivar una zona con un miembro que choca con otra zona activa | `El valor '{v}' ya pertenece a la zona {código}.` | 409 |

`resolve` sin coincidencia devuelve `200` con `dispatchZoneId: null` (no es un error); con empate devuelve
`ambiguous: true` y los códigos candidatos.

---

## 6. Despacho y salida

### 6.1 Selector y despacho

Qué hace: lista las rutas activas en `DRAFT`/`PLANNED` de un día con sus avisos y bloqueantes, y despacha una ruta
(individual o en lote). Al despachar: la ruta se congela (`Route` pasa a `ACTIVE`) y las órdenes vigentes avanzan
**etapa por etapa** hasta `PLANNED` (o hasta la última etapa habilitada anterior, si el tenant deshabilitó
`PLANNED`). Despachar es **irreversible**.

Quién puede: `trips.dispatch`.

Cómo se usa:
- `GET /api/v1/trips/dispatchable?date=` — sin fecha = hoy UTC
- `POST /api/v1/trips/{publicId}/dispatch` — cuerpo opcional `{ "comment": "…", "rowVersion": "…" }`
- `POST /api/v1/trips/dispatch` — lote: `{ "tripPublicIds": ["…", "…"], "comment": "…" }` (máximo 50; cada ruta en
  su propia transacción; la respuesta siempre es `200`, con el resultado de cada una)

Avisos y bloqueantes de una ruta (`code` → `message`; **bloquea** el despacho solo si `blocking: true`):

| Código | Bloquea | Mensaje |
|---|---|---|
| `NO_DRIVER` | sí | `La ruta no tiene chofer asignado.` |
| `DRIVER_UNAVAILABLE` | sí | `El chofer no está disponible para despacho: {motivos}.` |
| `NO_VEHICLE` | sí | `La ruta no tiene vehículo asignado.` |
| `VEHICLE_UNAVAILABLE` | sí | `El vehículo no está disponible para despacho: {motivos}.` |
| `NO_STOPS` | sí | `La ruta no tiene paradas.` |
| `ORDER_NOT_ELIGIBLE` | sí | `Orden {número}: {motivo}` (uno por orden no elegible; ver sección 3.1) |
| `OVER_STOP_LIMIT` | no | `La ruta tiene {n} paradas y el máximo del chofer es {m}.` |
| `OVER_VEHICLE_STOPS` | no | `La ruta tiene {n} paradas y el vehículo admite {m}.` |
| `OVER_WEIGHT` | no | `La carga ({x} kg) excede la capacidad de peso del vehículo ({y} kg).` |
| `OVER_VOLUME` | no | `El volumen ({x} m³) excede la capacidad de volumen del vehículo ({y} m³).` |
| `LATE_WINDOWS` | no | `1 parada llega después del fin de su ventana.` / `{n} paradas llegan después del fin de su ventana.` |
| `NO_PLANNED_START` | no | `La ruta no tiene hora de salida; no se calculan las ETAs.` |
| `APPROXIMATE_PINS` | no | `1 parada tiene ubicación aproximada o sin coordenadas.` / `{n} paradas tienen ubicación aproximada o sin coordenadas.` |
| `DRIVER_DOUBLE_BOOKED` | no | `El chofer también está en la ruta {códigos} ese mismo día.` |
| `VEHICLE_DOUBLE_BOOKED` | no | `El vehículo también está en la ruta {códigos} ese mismo día.` |

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Ruta no en `DRAFT`/`PLANNED` | mismos mensajes de la sección 2 | 422 |
| Uno o más bloqueantes | `La ruta {código} no se puede despachar: {m1}; {m2}.` con `errors: { "{código de aviso}": ["{mensaje}"] }` | 422 |
| El tenant deshabilitó `DISPATCHED` en el pipeline de `TripStatus` | `El pipeline de rutas de esta compañía no tiene habilitada la etapa DISPATCHED.` | 422 |
| El tenant deshabilitó `ACTIVE` en el pipeline de `RouteStatus` | `El pipeline de rutas de esta compañía no tiene habilitada la etapa ACTIVE.` | 422 |
| Una orden dejó de ser elegible entre el selector y el despacho | `Orden {número}: {motivo}` (sección 3.1) | 422 |
| `comment` de más de 500 caracteres | `El comentario admite como máximo 500 caracteres.` | 400 |
| Módulo `CATALOG` apagado | `El módulo 'CATALOG' no está habilitado para esta compañía.` (`code: module_disabled`) | 403 |
| `tripPublicIds` del lote, vacío | `Seleccione al menos una ruta.` (campo `tripPublicIds`) | 400 |
| `tripPublicIds` del lote, más de 50 | `Máximo 50 rutas por despacho.` (campo `tripPublicIds`) | 400 |
| Un id del lote que no es una ruta del tenant | queda en el ítem con `dispatched: false` y `error: "Ruta no encontrada."` (la respuesta general sigue `200`) | — |
| `rowVersion` no coincide | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |

### 6.2 Salida

Qué hace: registra la salida de una ruta ya despachada (`DISPATCHED` → `IN_PROGRESS`): fija `actualStartUtc` y
lleva las órdenes vigentes a `IN_TRANSIT`. Es la misma costura (`TripLifecycleService.StartTrackedAsync`) que
reutilizará la app del chofer del Lote 7 ("el estado lo mueven eventos: salida, llegada, POD"); el cierre de la
ruta, los eventos por parada y el POD llegan con ese lote.

Quién puede: `trips.dispatch`.

Cómo se usa: `POST /api/v1/trips/{publicId}/start` — cuerpo opcional `{ "comment": "…", "rowVersion": "…" }`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Ruta que no está `DISPATCHED` (p. ej. `DRAFT`/`PLANNED`) | `La ruta {código} no está despachada; despáchela antes de registrar su salida.` | 422 |
| Ruta que ya está `IN_PROGRESS` | `La ruta {código} ya salió.` | 422 |
| Ruta sin chofer o sin vehículo (defensa; no debería ocurrir tras el despacho) | `La ruta {código} no se puede despachar: La ruta no tiene chofer asignado.; La ruta no tiene vehículo asignado.` | 422 |
| `comment` de más de 500 caracteres | `El comentario admite como máximo 500 caracteres.` | 400 |
| `rowVersion` no coincide | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |

### 6.3 Cancelar una orden que va en una ruta

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Cancelar una orden que va en una ruta `DRAFT`/`PLANNED` | se libera de la ruta automáticamente (`204`, sin error) | — |
| Cancelar una orden que va en una ruta `DISPATCHED`/`IN_PROGRESS` | `La orden va en la ruta {código} ya despachada; no se puede cancelar mientras la ruta esté en curso.` | 422 |
| Mandar a un lateral (`ON_HOLD`/`PARTIAL`/`FAILED`) una orden de una ruta abierta | se libera de la ruta automáticamente | — |
| Mandar a un lateral una orden de una ruta ya despachada | no cambia nada (lo decide el Lote 7) | — |

---

## 7. Estación de escaneo Outbound

Qué hace: escanea el número de orden, el número de empaque o el número de factura del cliente (mismo criterio y
precedencia que `/orders/lookup`: número de orden primero, luego empaque, luego factura) y asigna la orden a la
ruta abierta del día de su zona. Responde siempre `200` con un resultado tipado y la palabra que debe pronunciar
la estación (`found`/`dup`/`notfound`). Nunca crea rutas, ni cambia chofer, vehículo o el estatus de la orden.

Quién puede: `trips.scan`.

Cómo se usa: `POST /api/v1/scan/outbound` — `{ "code": "PKG-000123", "planDate": null }` (sin fecha = hoy UTC)

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `code` vacío | `Escanee o escriba un código.` (campo `code`) | 400 |
| `code` de más de 40 caracteres | `El código no puede exceder 40 caracteres.` (campo `code`) | 400 |

Resultados (`outcome` / `voice` / mensaje), en orden de precedencia:

| `outcome` | `reasonCode` | `voice` | Mensaje |
|---|---|---|---|
| `NOT_FOUND` | `NO_MATCH` | `notfound` | `No se encontró la orden.` |
| `NOT_FOUND` | `MULTIPLE_MATCHES` | `notfound` | `Hay varias órdenes con ese código; escanee el empaque.` |
| `ALREADY_ASSIGNED` | `IN_TRIP` | `dup` | `Ya estaba en la ruta {código}.` (gana a "no elegible": una orden ya despachada dice "ya") |
| `NOT_ELIGIBLE` | `NOT_ELIGIBLE` | `notfound` | el motivo de la sección 3.1 (p. ej. "El estatus actual no permite la acción 'ASSIGN_TRIP'.") |
| `FOUND_UNASSIGNED` | `NO_ZONE` | `found` | `No se pudo resolver la zona de despacho por código postal ni pueblo; queda sin asignar.` |
| `FOUND_UNASSIGNED` | `AMBIGUOUS_ZONE` | `found` | `El código postal o pueblo pertenece a varias zonas ({códigos}); queda sin asignar.` |
| `FOUND_UNASSIGNED` | `NO_OPEN_ROUTE` | `found` | `No hay ruta abierta para la zona {código} en la fecha {yyyy-MM-dd}; queda sin asignar.` |
| `FOUND_ASSIGNED` | — | `found` | `Asignada a la ruta {código}.` |

El escaneo nunca inventa rutas: si la única ruta abierta de la zona dejó de estarlo entre el escaneo y el bloqueo
(por ejemplo, alguien la acaba de despachar), prueba la siguiente ruta abierta de menor id; si no queda ninguna,
el resultado es `NO_OPEN_ROUTE`. Ocho escaneos simultáneos de órdenes distintas hacia la misma ruta quedan todos
`FOUND_ASSIGNED`, con secuencias 1..N sin huecos ni duplicados.

---

## 8. Planificar el día

Qué hace: agrupa por zona de despacho las órdenes confirmadas **sin ruta vigente** de una fecha: usa la ruta
abierta de la zona (la de menor id si hay varias) o crea una con el chofer estándar (sección 1). Es **idempotente**
(repetirla no crea rutas de más y solo agrega lo que sigue sin asignar), está serializada por compañía y **no**
optimiza, no despacha, no asigna vehículo ni cambia el estatus de las órdenes.

Quién puede: `trips.plan`.

Cómo se usa: `POST /api/v1/trips/plan-day` — `{ "planDate": "2026-09-28", "dispatchZoneIds": null, "createEmptyTrips": false }`
(`dispatchZoneIds` nulo o vacío = todas las zonas activas del tenant; `createEmptyTrips: true` crea la ruta de una
zona aunque no tenga órdenes, para que el escaneo Outbound tenga destino desde temprano)

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `planDate` ausente o fuera de rango | mismos mensajes de la sección 1 (campo `planDate`) | 400 |
| Más de 50 zonas en `dispatchZoneIds` | `Máximo 50 zonas por planificación.` (campo `dispatchZoneIds`) | 400 |
| Alguna zona de otro tenant o inexistente | `Zona de despacho no encontrada.` | 404 |
| Alguna zona inactiva | `La zona de despacho está inactiva.` (campo `dispatchZoneIds`) | 400 |

Por zona, cada orden omitida deja un aviso `NOT_ELIGIBLE` (`Orden {número}: {motivo}`, sección 3.1) o
`CAPACITY_HARD_CAP` (`La ruta llegó al máximo de 300 paradas; la orden queda sin asignar.`) en el resultado; no son
errores HTTP, son parte de la respuesta `200` (`zones[].issues`). Las órdenes sin zona resuelta o con zona ambigua
no se tocan: solo se cuentan en `ordersWithoutZone`.

---

## 9. Monitoreo de rutas

Qué hace: lista las rutas `DISPATCHED`/`IN_PROGRESS` del día (y las `COMPLETED`, salvo `includeCompleted=false`)
con su progreso, próxima ETA, paradas con pin aproximado, si excede el máximo de paradas y el último ping del
chofer (si el ping no trae la ruta, se usa el último ping del chofer desde la salida, marcado `linkedToTrip:
false`). Los **totales** se calculan sobre la fecha y la zona **antes** de aplicar la búsqueda libre: buscar nunca
cambia los contadores, solo filtra la lista visible.

Quién puede: `trips.view`.

Cómo se usa: `GET /api/v1/trips/monitor?date=&dispatchZoneId=&search=&includeCompleted=true` (sin `date` = hoy UTC)

Sin validaciones propias más allá de las compartidas (permiso y módulo). La ficha de cada ruta
(`GET /api/v1/trips/{publicId}`) trae las coordenadas y la precisión de cada parada; el mapa lo dibuja el front.

---

## 10. Estatus y transiciones

### 10.1 Trip (`TripStatus`)

| De | A | Quién | Qué valida | Efectos | Qué bloquea |
|---|---|---|---|---|---|
| — | `DRAFT` (inicial) | Sistema, al crear | Número único `AAAA-####` | Historial de nacimiento | — |
| `DRAFT`/`PLANNED` | `PLANNED` (solo se llega optimizando, sección 4.1) | `trips.optimize`, automático al aplicar una optimización | — | Ninguno propio | — |
| `DRAFT`/`PLANNED` | `DISPATCHED` | `trips.dispatch` (`POST .../dispatch`) | Sin bloqueantes (sección 6.1); pipeline con `DISPATCHED` habilitado | `TripStatusEffect`: congela la ruta (etapa por etapa hasta `Route.ACTIVE`) y avanza las órdenes vigentes hasta `PLANNED` | Contenido de la ruta congelado; cabecera solo editable con `EDIT_TRIP` |
| `DISPATCHED` | `IN_PROGRESS` | `trips.dispatch` (`POST .../start`) | Debe estar `DISPATCHED` | `TripStatusEffect`: `actualStartUtc`, órdenes vigentes en pipeline avanzan hasta `IN_TRANSIT` | — |
| `IN_PROGRESS` | `COMPLETED` (fuera de este lote) | Cierre de ruta (Lote 7) | — | `TripStatusEffect`: `actualEndUtc`, `TripOrder.IsCurrent = 0` | Ruta cerrada, solo consulta |
| `DRAFT`/`PLANNED` | `CANCELLED` ("Eliminar ruta", lateral) | `trips.plan` (`DELETE`) | Solo desde `DRAFT`/`PLANNED` | `TripStatusEffect`: libera todas las órdenes vigentes, archiva la versión vigente de la ruta, `Trip.IsActive = 0` | Reintentar `DELETE`, editar, agregar órdenes, optimizar, despachar (422 "cerrada") |

Intentar `CANCELLED` desde `DISPATCHED`/`IN_PROGRESS` responde el 422 del motor de estatus ("No se permite pasar a
'CANCELLED' desde '{estatus}' según el modelo de esta compañía.") antes de que `TripStatusEffect` la rechace de
nuevo con su propio mensaje.

### 10.2 Route (`RouteStatus`) — el ciclo de la VERSIÓN del plan

Un `Trip` puede tener varias versiones de `Route` a lo largo del tiempo; solo una está vigente (`IsActive = 1`).

| De | A | Quién | Qué valida | Efectos |
|---|---|---|---|---|
| — | `DRAFT` (inicial) | Sistema, al crear la primera versión o al agregar la primera orden | — | Historial de nacimiento |
| `DRAFT`/`PLANNED` | `OPTIMIZED` | `trips.optimize` | — | Nueva versión con la secuencia del motor |
| `DRAFT`/`OPTIMIZED` | `ACTIVE` | Automático al despachar el `Trip` (sección 6.1) | — | Congela la versión (contenido inmutable) |
| `DRAFT`/`OPTIMIZED` | `ARCHIVED` (lateral, terminal) | Automático al reemplazar la versión (nueva optimización) o al "Eliminar ruta" | — | La versión queda intacta, solo de consulta |

### 10.3 OptimizationRun (`OptimizationRunStatus`) — bitácora, sin `[AuditEntity]` propia

| De | A | Quién | Qué valida | Efectos |
|---|---|---|---|---|
| — | `PENDING` (inicial) | Sistema, al iniciar la FASE 1 de una optimización | — | Historial de nacimiento |
| `PENDING` | `OK` | Sistema, si la ruta no cambió y el motor devolvió un resultado válido | — | `RouteId`, plan y totales |
| `PENDING` | `ERROR` | Sistema, si la ruta cambió, el motor falló/agotó el tiempo o el resultado fue inválido | — | `ErrorMessage` (recortado a 4000 caracteres) |

### 10.4 RouteStop (`RouteStopStatus`)

Este lote solo crea paradas en **`PENDING`** (al agregar una orden o al optimizar/reordenar). El resto del ciclo
(`ON_THE_WAY`, `ARRIVED`, `COMPLETED`, `FAILED`) lo mueve la app del chofer del **Lote 7** (eventos de salida,
llegada y POD); el progreso que muestra el monitor (sección 9) ya está preparado para leerlos.

Historial de cualquiera de las cuatro entidades: `GET /api/v1/status/history/{ENTITY}/{id}` con `TRIP`, `ROUTE`,
`ROUTE_STOP` u `OPTIMIZATION_RUN`. Capacidades por estatus: `GET/PUT /api/v1/status/capabilities/TRIP` (solo `TRIP`
trae una capacidad propia en este lote, `EDIT_TRIP`, denegada por defecto en `DISPATCHED`/`IN_PROGRESS`/
`COMPLETED`/`CANCELLED`). Entradas laterales: `GET/PUT /api/v1/status/lateral-entries/TRIP` (`CANCELLED` desde
`DRAFT`/`PLANNED`) y `.../ROUTE` (`ARCHIVED` desde `DRAFT`/`OPTIMIZED`).

---

## 11. Permisos y módulos

| Permiso | Qué habilita |
|---|---|
| `trips.view` | ver rutas (listado, ficha, corridas de optimización), la lista "Sin asignar" y el monitor |
| `trips.plan` | crear/editar/eliminar rutas, reasignar chofer en bloque, agregar/quitar órdenes, reordenar, pin manual y "Planificar el día" |
| `trips.optimize` | optimizar una ruta |
| `trips.dispatch` | ver el selector de despacho, despachar (individual/lote) y registrar la salida; también asignar/reasignar el chofer de una entrega especial (capítulo 04, sección 11) |
| `trips.scan` | usar la estación de escaneo Outbound |
| `fleet.view`/`fleet.manage` | ver/escribir zonas de despacho y sus miembros (capítulo 04) |

Todo el módulo vive bajo **LTL_GROUND** (núcleo); las zonas de despacho siguen bajo **CATALOG**, y los servicios de
este capítulo que tocan chofer o vehículo exigen además **CATALOG** encendido. Plantillas de rol de fábrica:
**Despachador** (`Dispatcher`) trae `trips.plan`, `trips.dispatch`, `trips.optimize`, `trips.view` y `trips.scan`;
**Operador de almacén** (`WarehouseOperator`) trae `trips.view` y `trips.scan` (sin `trips.plan` ni
`trips.dispatch`: no planifica ni despacha, solo consulta y escanea); **Solo lectura** (`ReadOnly`) trae solo
`trips.view`; **Chofer** (`Driver`) no trae ningún permiso `trips.*` (su acceso propio llega con la app del
chofer, Lote 7).

Contactos (`POST /api/v1/contacts/{ENTITY}/{id}`) y campos personalizados (`PUT /api/v1/custom-fields/values/{ENTITY}/{id}`)
están disponibles para `TRIP`, `ROUTE` y `ROUTE_STOP` bajo `trips.plan` (lectura con `trips.view`).
`OPTIMIZATION_RUN` **no** admite contactos ni campos personalizados por id suelto (responde siempre `404`, igual
que `DRIVER_RATE` y `FLEET_DOCUMENT`): es una bitácora de solo lectura, no un recurso editable.

---

## 12. Fuentes de análisis y auditoría

Fuente nueva **`TRIP`** ("Rutas", con `PlanDate` como campo de fecha), sin `Amount` ni `Rate`. La fuente de Órdenes
(`TRANSPORT_ORDER`) gana `TripCode`/`TripStatusCode`, `AssignedDriverCode`/`AssignedDriverName`/
`HasAssignedDriver`, `DispatchZoneCode` e `IsException` (`ON_HOLD`, `PARTIAL` o `FAILED`; `CANCELLED` no cuenta).

El tenant nuevo (y el demo) reciben de fábrica: la vista **"Rutas"** (fecha, número, zona, chofer, vehículo,
estatus, paradas, sobre el máximo, distancia total; orden por fecha descendente); los indicadores **"Órdenes sin
chofer asignado"** (activas en `CONFIRMED`..`PLANNED` sin ruta vigente con chofer y sin viaje pagado propio) y
**"Órdenes en excepción"** (`ON_HOLD`/`PARTIAL`/`FAILED`); el indicador **"Rutas sobre el máximo de paradas"**; y
el gráfico de donut **"Rutas por estatus"** (últimos 7 días). Cada alta o cambio de una ruta, sus órdenes, la
versión de la ruta y los miembros de zona queda en `AuditLog` (`GET /api/v1/audit/changes?entityType=TRIP|ROUTE|
DISPATCH_ZONE|TRANSPORT_ORDER`); las corridas de optimización no generan `AuditLog` (ya son su propia bitácora).

---

## 13. Preguntas frecuentes

Ver [faq.md](faq.md), sección "Lote 5 — Trips y rutas", para cada mensaje de error citado en este capítulo con su
causa y qué hacer.
