# Capítulo 06 — Inventario y almacén (Lote 6; Almacenes y ubicaciones ampliado en el Lote 11; Productos y Compras ampliados en el Lote 12; Recibo, Tareas y Recolección y empaque ampliados en el Lote 13)

Este capítulo describe Almacenes y ubicaciones, Productos y categorías, Inventario (saldos, Kárdex, ajustes,
transferencias, genealogía, rastro de serie y conciliación), Recepción (avisos de llegada y recibos, incluida la
recepción contra una orden de compra), la cola de Tareas de almacén (putaway dirigido y reabasto), Conteo cíclico,
Recolección y empaque ad hoc, Compras mínimas (proveedores, órdenes de compra y faltantes) y Cruce de muelle (citas
y planes, en modo demo). Cada sección indica **qué hace**, **quién puede**, **cómo se usa**, las **validaciones**
con el mensaje exacto y el código HTTP, y los **estatus** con sus transiciones y efectos. Los mensajes están
verificados contra el código (`src/Teikem.Domain/Wms/*.cs`, `src/Teikem.Infrastructure/Wms/*.cs`,
`src/Teikem.Domain/Catalogs/PostalLocality.cs`, `src/Teikem.Infrastructure/Services/PostalLocalityService.cs`,
`src/Teikem.Api/Controllers/PostalLocalitiesController.cs`,
`src/Teikem.Infrastructure/Services/{Warehouse,WarehouseLayout,Product,ProductCategory,InventoryRead,
InventoryAdjustment,Traceability,Asn,Receipt,WarehouseTask,Replenishment,CycleCount,PickBatch,Supplier,
PurchaseOrder,PurchaseOrderReceiving,PurchaseShortage,DockAppointment,CrossDock}Service.cs`,
`src/Teikem.Infrastructure/Services/{PutawayTaskHandler,ReplenishTaskHandler,CountTaskHandler,CrossDockTaskHandler,
CrossDockReceiptParticipant,WarehouseTaskStatusEffect,DockAppointmentStatusEffect}.cs`,
`src/Teikem.Api/Controllers/{Warehouses,Products,ProductCategories,Inventory,Receipts,Asns,WarehouseTasks,
CycleCounts,PickBatches,Suppliers,PurchaseOrders,DockAppointments,CrossDockPlans}Controller.cs`). Las preguntas y
respuestas de cada mensaje están en [faq.md](faq.md).

Convenciones del capítulo:

- Los encabezados con historial propio (`Warehouse`, `Product`, `Asn`, `ReceiptHeader`, `PurchaseOrder`, `PickBatch`)
  se identifican por `publicId` (GUID) en la URL. Las hijas sin `TenantId` (`WarehouseZone`, `WarehouseBin`,
  `WarehouseDock`, `InventoryLot`, `PurchaseOrderLine`, `ReceiptLine`, `CycleCountLine`, `PickBatchLine`,
  `CrossDockAllocation`) se exponen por id entero **siempre bajo su padre ya filtrado**: una posición de otro
  almacén, una línea de otro recibo o una asignación de otro plan responden **404** como si no existieran (BOLA
  por id hijo, sin oráculo). `WarehouseTask`, `CycleCount` y `DockAppointment` no tienen `PublicId`: se exponen por
  id entero bajo el filtro de tenant.
- Ninguna solicitud lleva `TenantId`: sale siempre de la sesión (el JWT `tid`). Tampoco existe `InventoryScope` en
  el cuerpo de ninguna solicitud: en este lote todas las lecturas internas usan el alcance "todo el inventario"
  (`InventoryScope.Any`); el alcance por cliente dueño (`OwnerClientId`) queda listo como costura para el Portal
  del Lote 8.
- `InventoryLedger` es la **única** vía de escritura de `StockBalance`, `InventoryTransaction` y del estatus/
  ubicación de `InventorySerial`. `InventoryTransaction.Quantity` se guarda **con signo**: una recepción entra en
  positivo, un despacho o un cruce de muelle salen en negativo, un ajuste entra o sale según lo capturado, y una
  transferencia es una sola fila con origen y destino distintos. Los servicios siempre pasan la **magnitud**
  (mayor que cero); el ledger decide el signo.
- Un error de validación responde `400` con `title` y, cuando aplica, `errors` (`{ campo: [mensaje] }`); un
  conflicto (duplicado, choque de concurrencia, número ya tomado) `409` `conflict`; inventario insuficiente `409`
  `insufficient_stock`; una regla de estatus, de capacidad o "todavía no procede" `422` `status_rule`; un recurso
  ajeno o inexistente `404` `not_found`; falta de permiso `403` `forbidden` (`Falta el permiso '{código}'.`); un
  módulo apagado `403` `module_disabled` (`El módulo '{clave}' no está habilitado para esta compañía.`).
- Módulos: **WMS_LOTSERIAL** (encendido por defecto) cubre almacenes, productos, inventario, recepción, tareas,
  conteo y recolección. **PURCHASING** (encendido por defecto) cubre proveedores, órdenes de compra y faltantes.
  **CROSSDOCK** (apagado por defecto, demo) cubre citas de muelle y planes de cruce de muelle; el tenant lo
  enciende desde Configuración de la compañía (acción sensible, exige reautenticación reciente).
- "Dueño del producto" (`Product.ClientId`, un cliente 3PL o `NULL` = propio del tenant) es un concepto distinto
  del tenant (`TenantId`, quién es dueño de los **datos**). "Recibo" es `ReceiptHeader`/`ReceiptLine`; "Ubicación" o
  "Posición" es `WarehouseBin`.

---

## 1. Almacenes y ubicaciones

Qué hace: mantiene el almacén y su jerarquía física — zonas tipadas, posiciones (pasillo-rack-nivel-posición) y
muelles. El almacén nace **ACTIVE** y su baja es **definitiva** (estatus terminal): solo procede vacío y sin
documentos abiertos. Con un único almacén activo, las demás pantallas lo usan por defecto; con más de uno, hay que
indicarlo. Desde el Lote 11 (cambios de Almacén, tanda 1) además: cada posición puede tener un **cupo máximo** en
unidades y, con él, un estado de **ocupación**; cada zona muestra su ocupación (calculada, no guardada); el **código de
una zona se puede editar**; el listado de posiciones es **paginado** y se busca por código, zona, pasillo, rack, nivel o
posición; y hay un **catálogo de localidades postales** para llenar la ciudad, el estado, el código postal y el país
del almacén.

Quién puede: `inventory.view` (listar y consultar almacenes, zonas, posiciones y muelles); `warehouse.manage`
(alta, edición, baja/reactivación de almacén, zonas, posiciones y muelles, y el estatus manual del muelle). Módulo
**WMS_LOTSERIAL**. El catálogo de localidades postales (`GET /api/v1/postal-localities`) solo pide una sesión
iniciada: no exige permiso ni módulo.

Cómo se usa:
- `GET /api/v1/warehouses?includeInactive=` — por defecto solo los activos, con conteos de zonas/posiciones/
  muelles, existencia y `zoneTypeCodes` (códigos de tipo de zona **distintos** de sus zonas activas, ordenados; una zona
  sin tipo no aporta código).
- `POST /api/v1/warehouses` — `{ "code": "ALM-02", "name": "Almacén secundario", "line1": "...", "city": "Toa Baja",
  "state": "PR", "postalCode": "00949", "country": "PR" }`. El país por defecto es `PR`. La ciudad, el estado y el
  código postal son texto libre para el API: el catálogo de localidades es una ayuda de captura de la pantalla, no una
  validación.
- `GET /api/v1/warehouses/{publicId}`, `PATCH /api/v1/warehouses/{publicId}` (edición en línea; `null` = sin
  cambio; `rowVersion` opcional).
- `POST /api/v1/warehouses/{publicId}/deactivate` — baja definitiva.
- Zonas: `GET/POST /api/v1/warehouses/{publicId}/zones` (`GET ?includeInactive=`), `PATCH .../zones/{zoneId}`
  (`{ "code": "PCK-2", "name": "Picking 2", "zoneType": "PICKING" }`; todo opcional, `null` = sin cambio,
  `"zoneType": ""` quita el tipo), `POST .../zones/{zoneId}/deactivate|reactivate`.
- Posiciones: `GET/POST /api/v1/warehouses/{publicId}/bins` (filtros en la sección 1.2),
  `PATCH .../bins/{binId}` (`{ "aisle": "A9", "maxCapacityQty": 40 }`; `"clearMaxCapacity": true` quita el cupo),
  `POST .../bins/{binId}/deactivate|reactivate`.
- Muelles: `GET/POST /api/v1/warehouses/{publicId}/docks`, `PATCH .../docks/{dockId}`, `POST .../docks/{dockId}/
  status` (estatus manual), `POST .../docks/{dockId}/deactivate|reactivate`.
- Localidades postales: `GET /api/v1/postal-localities?search=&take=` (sección 1.3).

### 1.1 Zonas: código editable y ocupación

**Código editable.** `PATCH .../zones/{zoneId}` acepta `code`. Se recorta y se guarda en mayúsculas, con las mismas
reglas que al crear (letras, números, guion y guion bajo; máximo 30) y sigue siendo **único dentro del almacén**. Las
posiciones y los saldos apuntan a la zona por su id, así que cambiar el código no mueve posiciones ni existencias. Lo
que no se puede cambiar es el almacén de la zona: mandar `warehouseId` en el `PATCH` responde 400. El mensaje
`El código de la zona no se puede cambiar.` **ya no existe**. Mandar el mismo código que ya tiene la zona no cambia
nada. Editar una zona de un almacén dado de baja responde 422 (`El almacén está dado de baja; solo se consulta.`).

**Ocupación de la zona.** `GET .../zones` (y la ficha del almacén, `GET .../{publicId}`) devuelve por zona cifras
calculadas en una sola consulta; la capacidad de la zona **no se guarda**:

| Campo | Qué es |
|---|---|
| `binCount` | Posiciones **activas** de la zona |
| `occupiedBinCount` | Posiciones activas con existencia en mano mayor que cero |
| `capacityQty` | Suma del cupo de las posiciones activas **que tienen cupo** |
| `qtyOnHandInCapacityBins` | Existencia en mano de esas mismas posiciones (el numerador del porcentaje) |
| `qtyOnHand` | Existencia en mano de **todas** las posiciones de la zona |
| `binsWithoutCapacity` | Posiciones activas sin cupo configurado (quedan fuera del porcentaje) |

Porcentaje de ocupación de la zona = `qtyOnHandInCapacityBins / capacityQty`. Con `capacityQty = 0` no hay porcentaje: la
pantalla muestra la existencia total y "sin cupo configurado". La existencia usada es la **en mano** (incluye lo
reservado).

### 1.2 Posiciones: cupo máximo, ocupación y listado paginado

**Cupo máximo.** `maxCapacityQty` es un entero mayor que cero (unidades de producto que caben en la posición) o vacío
(`null` = sin cupo configurado). Se envía al crear (`POST .../bins`) y al editar (`PATCH .../bins/{binId}`); en el
`PATCH`, `null` deja el cupo como está y `"clearMaxCapacity": true` lo quita (si vienen los dos, gana quitar). En la base
lo protege `CK_WarehouseBin_MaxCapacityQty` (nulo o mayor que cero).

**Estado de ocupación** (`occupancy`). Es un cálculo, no un estatus guardado: no está en un catálogo ni tiene
transiciones. Compara la existencia en mano de la posición con su cupo:

| Código | Etiqueta en pantalla | Cuándo |
|---|---|---|
| `EMPTY` | Vacía | Existencia en mano igual a cero |
| `PARTIAL` | Parcial | Hay existencia y es menor que el cupo |
| `FULL` | Llena | Hay existencia y es igual o mayor que el cupo |
| `NO_CAPACITY` | Ocupada sin cupo | Hay existencia y la posición no tiene cupo |

**Listado** — `GET /api/v1/warehouses/{publicId}/bins` responde `{ "total": 3029, "skip": 0, "take": 100, "items": [...] }`
(antes del Lote 11 era un arreglo). `skip` mínimo 0; `take` por defecto 100, máximo 200 (0 o menos = 100; más de 200 =
200). Siempre ordena por código y, a igual código, por id. Filtros (todos opcionales, se combinan con "y"):

| Parámetro | Qué hace |
|---|---|
| `search` | Texto que **contiene** el código, el código de la zona, el pasillo, el rack, el nivel o la posición (sin distinguir mayúsculas). Antes solo miraba código y zona |
| `zoneId` | Una zona; si no es de este almacén responde 404 |
| `zoneIds` | Varias zonas (repetir el parámetro); un id ajeno simplemente no devuelve posiciones |
| `aisle`, `rack`, `level`, `position` | Cada parte **contiene** el texto |
| `productPublicIds` | Posiciones con existencia de alguno de esos productos |
| `occupancy` | Uno o varios de `EMPTY`, `PARTIAL`, `FULL`, `NO_CAPACITY` (repetir el parámetro o separarlos por comas) |
| `binIds` | Solo esas posiciones |
| `includeInactive` | Incluye las dadas de baja (por defecto no) |
| `onlyWithStock` | Solo con existencia en mano mayor que cero |

Ejemplo: `GET /api/v1/warehouses/{publicId}/bins?search=01-A&occupancy=PARTIAL&occupancy=FULL&skip=0&take=50`.

Cada posición trae `maxCapacityQty`, `occupancy`, `qtyOnHand`, `productCount` y, cuando `productCount` es exactamente 1,
`singleProductPublicId`, `singleProductSku` y `singleProductName`.

**Cupo en bloque** — `POST /api/v1/warehouses/{publicId}/bins/capacity` (permiso `warehouse.manage`, módulo
`WMS_LOTSERIAL`, igual que editar una posición). Fija o quita el cupo de **todas** las posiciones del almacén que cumplen
los filtros (sin paginar), en una sola transacción: o cambian todas o ninguna. Cuerpo:

| Campo | Qué hace |
|---|---|
| `zoneIds`, `aisle`, `rack`, `level`, `position`, `search`, `binIds`, `includeInactive` | Los mismos filtros del listado, con el mismo significado (las partes y `search` "contienen"; sin `includeInactive` quedan fuera las dadas de baja; un id de zona o de posición de otro almacén no coincide con nada) |
| `onlyWithoutCapacity` | Solo las posiciones que hoy no tienen cupo: no pisa ningún cupo ya capturado |
| `allBins` | Obligatorio (`true`) cuando no se envía ningún filtro de posiciones: evita cambiar todo el almacén por accidente |
| `maxCapacityQty` | Cupo a fijar (entero mayor que cero) |
| `clear` | `true` = quitar el cupo (quedan "sin cupo") |

Exactamente uno de `maxCapacityQty` o `clear: true`. Responde `{ "matched": 12, "changed": 9 }`: cuántas posiciones
cumplen los filtros y cuántas cambiaron de verdad (las que ya tenían ese cupo no se tocan). Cada posición cambiada queda
en la bitácora de auditoría como un cambio de almacén, igual que al editarla una por una. **Para saber de antemano
cuántas se afectarán**, pida el listado con los mismos filtros y `take=1`: su `total` es exactamente `matched`
(`GET .../bins?zoneIds=3&aisle=01&take=1`). Ejemplos: `{ "zoneIds": [3], "aisle": "01", "maxCapacityQty": 40 }`;
`{ "allBins": true, "onlyWithoutCapacity": true, "maxCapacityQty": 50 }`; `{ "binIds": [101, 102], "clear": true }`.
Almacén de otra compañía → 404; almacén dado de baja → 422 `El almacén está dado de baja; solo se consulta.`

El cuerpo se valida **antes** de buscar el almacén: un cuerpo inválido responde 400 aunque el almacén no exista o esté dado
de baja. Si hay varios errores a la vez (por ejemplo, sin valor y sin filtros), el 400 los trae todos, cada uno en su campo.
Los mensajes están en la tabla de validaciones de este capítulo. En la pantalla, el botón **Asignar cupo** de Posiciones y de
la pestaña Posiciones de la ficha del almacén arma este mismo cuerpo (ver el manual de pantallas, sección Posiciones).

**Cupo estimado desde la migración (Lote 12).** Las posiciones de Advance Depot llegan de la migración con un cupo estimado
del historial del WMS anterior: el mayor total que tuvo cada posición, redondeado hacia arriba a la decena (mínimo 10); sin
historial, la mediana de su pasillo, zona o almacén (solo si esa mediana sale de al menos 5 posiciones con historial).
Advance Solutions no recibe cupo. La migración nunca pisa un cupo ya capturado. Es una **estimación**: corríjala con el cupo
en bloque o editando la posición. La regla completa, el reporte `-cupos.csv` y el comportamiento de `--update` están en el
[capítulo 10, sección 4](10-migracion-de-datos.md).

### 1.3 Catálogo de localidades postales

`GET /api/v1/postal-localities?search=toa%20baja&take=30` devuelve un arreglo de
`{ id, city, postalCode, state, countryCode, country, municipality }`. Solo lectura, global (no depende de la compañía).

- **Contenido.** 42.522 códigos postales del catálogo USPS: 42.346 de Estados Unidos y sus territorios (`countryCode`
  `US`) y 176 de Puerto Rico (`PR`). `city` es el nombre postal oficial del ZIP, en mayúsculas y sin acentos
  (`SABANA SECA`, `NEW YORK`). Solo en Puerto Rico viene `municipality` (con acentos): 00952 es `SABANA SECA` del
  municipio `Toa Baja`; 00631 es `CASTANER` del municipio `Lares`. `country` es el nombre del país en el idioma del usuario.
- **Búsqueda.** El texto se compara sin mayúsculas ni acentos (`mayaguez` encuentra `Mayagüez`). Si solo trae dígitos
  (y guion) se busca por **prefijo de código postal** (`0094` → 00949, 00950…). Si trae letras se busca en la ciudad
  postal **y** en el municipio: primero las que empiezan por el texto, luego las que tienen una palabra que empieza por
  él, luego las que lo contienen. A igual coincidencia, Puerto Rico va primero. Sin `search` devuelve las primeras por
  ciudad.
- **Tamaño.** `take` por defecto 20, máximo 100; la pantalla pide 30.
- **Memoria.** El catálogo se guarda en memoria del API una hora: un cambio directo en la tabla puede tardar hasta una
  hora en verse.
- **Cómo lo usa la pantalla.** Al elegir una localidad, el formulario del almacén guarda `city` (el **municipio** si lo
  hay —Puerto Rico—, si no la ciudad postal USPS), `postalCode`, `state` y `country`. El almacén guarda texto: no hay
  llave hacia la tabla de localidades.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `code` vacío (alta de almacén, zona o muelle; también `PATCH` de zona con `"code": ""`) | `El código es obligatorio.` | 400 |
| `code` inválido o > 30 | `El código solo admite letras, números, guion y guion bajo (máximo 30).` | 400 |
| `name` vacío | `El nombre es obligatorio.` | 400 |
| Código de almacén repetido | `Ya existe un almacén con ese código.` | 409 |
| Código de zona repetido en el almacén (al crear **o al editar el código**) | `Ya existe una zona con ese código en el almacén.` | 409 |
| Código de posición repetido en el almacén | `Ya existe una posición con ese código en el almacén.` | 409 |
| Código de muelle repetido en el almacén | `Ya existe un muelle con ese código en el almacén.` | 409 |
| `PATCH` de almacén con `code` | `El código del almacén no se puede cambiar.` | 400 |
| `PATCH` de muelle con `code` | `El código del muelle no se puede cambiar.` | 400 |
| `PATCH` de zona con `warehouseId` | `La zona no se puede mover a otro almacén.` | 400 |
| `PATCH` de posición con `code` o `zoneId` | `El código y la zona de la posición no se pueden cambiar.` | 400 |
| Posición sin código ni partes | `Indique el código de la posición o su pasillo/rack/nivel/posición.` | 400 |
| Parte de posición inválida o > 20 | `Pasillo, rack, nivel y posición solo admiten letras, números, guion y guion bajo (máximo 20 cada uno).` | 400 |
| Código de posición > 40 o inválido | `El código de la posición solo admite letras, números, guion y guion bajo (máximo 40).` | 400 |
| Sin zona al crear posición | `Indique la zona de la posición.` | 400 |
| `maxCapacityQty` ≤ 0 (alta o edición de posición, o cupo en bloque; el error va en `errors.maxCapacityQty`) | `El cupo máximo de la posición debe ser mayor que cero.` | 400 |
| Cupo en bloque sin `maxCapacityQty` ni `clear: true` (`errors.maxCapacityQty`) | `Indique el cupo máximo (maxCapacityQty) o clear: true para quitarlo.` | 400 |
| Cupo en bloque con `maxCapacityQty` y `clear: true` a la vez (`errors.clear`) | `Indique el cupo máximo o clear: true, no ambos.` | 400 |
| Cupo en bloque sin ningún filtro de posiciones y sin `allBins: true` (`errors.allBins`) | `Indique al menos un filtro de posiciones (zoneIds, aisle, rack, level, position, search o binIds) o allBins: true para aplicarlo a todo el almacén.` | 400 |
| `occupancy` desconocido en el listado de posiciones (el error va en `errors.occupancy`) | `Estado de ocupación desconocido: 'X'. Use EMPTY, PARTIAL, FULL o NO_CAPACITY.` | 400 |
| `zoneId` del listado que no es de este almacén | `Zona no encontrada.` | 404 |
| `zoneType` desconocido | `Tipo de zona desconocido: 'X'.` | 400 |
| `dockType` vacío/desconocido | `Indique el tipo de muelle (INBOUND, OUTBOUND o BOTH).` / `Tipo de muelle desconocido: 'X'.` | 400 |
| Estatus de muelle no manual | `Estatus de muelle no permitido: 'X'. Use FREE, OCCUPIED o MAINTENANCE.` | 400 |
| `maxWeightKg` ≤ 0 | `La capacidad de peso debe ser mayor que cero.` | 400 |
| `maxWeightKg` con más de 3 decimales o ≥ 10⁹ | `La capacidad de peso admite como máximo 9 enteros y 3 decimales.` | 400 |
| País desconocido | `País desconocido: 'X'.` | 400 |
| Baja de almacén con inventario o documentos abiertos | `El almacén {code} tiene inventario o documentos abiertos; no se puede dar de baja.` (`errors` por tipo: `inventory`, `receipts`, `cycleCounts`, `tasks`, `pickBatches`, `crossDockPlans`, `appointments`) | 409 |
| Almacén ya dado de baja | `El almacén está dado de baja; solo se consulta.` | 422 |
| Baja de posición con inventario | `La posición {code} tiene inventario; no se puede desactivar.` | 409 |
| Baja de posición con tareas abiertas | `La posición tiene tareas de almacén abiertas; complételas o cancélelas antes de desactivarla.` | 409 |
| Baja de zona con posiciones activas | `La zona tiene posiciones activas; desactívelas primero.` | 409 |
| Baja de muelle con citas vigentes | `El muelle tiene citas agendadas o en curso.` | 409 |
| Zona/muelle inactivo al usarlo | `La zona está inactiva; reactívela primero.` / `El muelle está inactivo; reactívelo primero.` | 422 |
| Almacén de otro tenant o inexistente | `Almacén no encontrado.` | 404 |
| Zona/posición/muelle de otro almacén (o de otro tenant) | `Zona no encontrada.` / `Posición no encontrada.` / `Muelle no encontrado.` | 404 |
| Operación sin `warehousePublicId` con más de un almacén activo | `Indique el almacén: la compañía tiene más de uno.` | 400 |
| Sin ningún almacén activo | `La compañía no tiene almacenes activos.` | 422 |

Un `maxCapacityQty` mayor que 2.147.483.647 (el máximo de un entero de 32 bits) lo rechaza la pantalla; enviado
directo al API no se probó qué respuesta da.

### Estatus y transiciones

`WarehouseStatus`: **ACTIVE** (inicial) → **INACTIVE** (terminal, baja definitiva). No hay vuelta atrás: un
almacén inactivo solo se consulta.

`DockStatus`: **FREE** (inicial) ↔ **OCCUPIED** ↔ **MAINTENANCE** (todos laterales entre sí; `warehouse.manage`
los fija a mano). La llegada de una cita de muelle (estatus `ARRIVED`) también ocupa el muelle (`FREE→OCCUPIED`,
efecto `DockAppointmentStatusEffect`, sección 9); cerrarla (`COMPLETED`, `NO_SHOW` o `CANCELLED`) lo libera
(`OCCUPIED→FREE`) solo si no queda otra cita `ARRIVED` en ese muelle. `MAINTENANCE` nunca lo toca el efecto
automático: solo el jefe de almacén a mano.

Zonas y posiciones: activa ↔ inactiva (`IsActive`, sin dominio de estatus propio). Se dan de baja con `warehouse.manage`;
la zona solo sin posiciones activas y la posición solo sin inventario ni tareas abiertas (ver la tabla de arriba). La
**ocupación** de una posición (`EMPTY`, `PARTIAL`, `FULL`, `NO_CAPACITY`) no es un estatus: cambia sola cuando cambia la
existencia o el cupo, no tiene transiciones ni efectos, y no bloquea ninguna acción (la baja de la posición depende de
que tenga inventario, no de su ocupación).

---

## 2. Productos y categorías

Qué hace: mantiene el maestro de SKU (propio del tenant o de un cliente 3PL), su tipo de seguimiento (sin lote/
serie, por lote o por serie), costo y precio, mínimos de inventario y de picking, y su categoría jerárquica.
También expone los lotes y las series de cada producto.

Quién puede: `inventory.view` (listar, ficha, lotes, series, categorías); `inventory.manage` (alta, edición, baja/
reactivación de producto y de categoría). Módulo **WMS_LOTSERIAL**.

Cómo se usa:
- `GET /api/v1/products?search=&categoryIds=&ownerClientPublicId=&ownOnly=&activeOnly=&warehousePublicId=&
  onlyAvailable=&skip=&take=` (`take` ≤ 200). Lote 12 agrega `warehousePublicIds`, `productPublicIds`, `name`, `brands`,
  `serialOnly` y `serialMissing` (ver "Marca, modelo y filtros de la lista", abajo); desde el ajuste del 2026-09-30, también
  `onlyOnHand`.
- `GET /api/v1/products/brands?search=` (Lote 12, `inventory.view`) — marcas distintas de la compañía, para el filtro Marca.
- `POST /api/v1/products` — `{ "sku": "PN", "name": "Producto normal", "trackingType": "NONE", "purchaseCost": 12.3456,
  "brand": "Acme", "model": "X-200" }` (`brand` y `model` son opcionales).
- `GET /api/v1/products/{publicId}`, `PATCH /api/v1/products/{publicId}`.
- `GET /api/v1/products/{publicId}/lots`, `GET /api/v1/products/{publicId}/serials?status=&search=`.
- `POST /api/v1/products/{publicId}/deactivate|reactivate`.
- `GET/POST /api/v1/product-categories`, `PATCH /api/v1/product-categories/{id}` (mover/renombrar), `POST .../{id}/
  deactivate|reactivate`.

### Marca, modelo y filtros de la lista (Lote 12)

**Marca y modelo.** Dos campos de texto libre del producto, opcionales, de hasta 100 caracteres cada uno. Se recortan los
espacios de los extremos y un valor vacío se guarda como "sin valor". No hay catálogo de marcas: la marca es el texto que
se escribe, y `GET /api/v1/products/brands` devuelve las que ya usan los productos de la compañía (activos e inactivos),
sin repetir aunque difieran en mayúsculas, ordenadas sin distinguir mayúsculas y hasta 500. `?search=` filtra las marcas que
contienen el texto. Cada fila de la lista y la ficha traen `brand` y `model`.
- Alta (`POST`): `brand` y `model` son opcionales.
- Edición (`PATCH`): `brand` o `model` ausente o `null` = **sin cambio**; `""` (o solo espacios) = **quitarlo**; cualquier
  otro texto lo reemplaza. Se auditan como cualquier otro campo del producto.

**Filtros de `GET /api/v1/products`.** Todos son opcionales y se combinan con "y":

| Parámetro | Qué hace |
|---|---|
| `warehousePublicIds` (varios) y `warehousePublicId` | Se juntan sin repetir. **Acotan las cantidades** (en mano, reservado, disponible, bajo mínimo y series) de cada fila a esos almacenes; **no quitan productos** de la lista. Las excepciones son `onlyAvailable=true` y `onlyOnHand=true`, que sí dejan fuera a los productos sin disponible (o sin existencia en mano) en esos almacenes. Si alguno no es de su compañía o no existe, responde 404 `Almacén no encontrado.` |
| `productPublicIds` (varios) | Solo esos productos (el filtro "SKU" de la pantalla) |
| `name` | El nombre del producto **contiene** el texto, sin distinguir mayúsculas |
| `brands` (varios) | La marca es **igual** a alguna de las indicadas, sin distinguir mayúsculas |
| `categoryIds` | Sin cambio: incluye las subcategorías |
| `serialOnly=true` | Productos con rastreo por serie **o** que ya tienen números de serie registrados |
| `serialMissing=true` | Productos **activos** con rastreo por serie cuya existencia en mano es **mayor** que la cantidad de sus series `AVAILABLE` o `RESERVED` (les faltan series por capturar). Con almacenes indicados, ambas cantidades se miden en esos almacenes. La pantalla lo cuenta con `take=1` y lee `total` |
| `onlyOnHand=true` (ajuste del 2026-09-30) | Solo productos con **existencia en mano mayor que cero**: la suma de `QtyOnHand` de todas sus posiciones, **incluidas** cuarentena, cruce de muelle y lo reservado. Con almacenes indicados, la existencia se mide en esos almacenes y, como `onlyAvailable`, **sí quita** de la lista a los productos sin existencia allí. Con `activeOnly=true` da "productos activos con existencia en mano": es lo que muestra la tabla al tocar el indicador "Unidades totales" |
| `activeOnly`, `onlyAvailable`, `belowMin` | Sin cambio. `onlyAvailable` no cuenta la existencia en zonas de cuarentena ni de cruce de muelle y resta lo reservado; por eso un producto puede aparecer con `onlyOnHand` y no con `onlyAvailable` |

`GET /api/v1/inventory/transactions` (Kárdex) acepta además `brands` y `name` con el mismo significado; se combinan con
`productPublicIds` y `categoryIds`. El Reporte de ajustes de la pantalla Productos e inventario los usa para respetar los
filtros de la tabla.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `sku` vacío | `El SKU es obligatorio.` | 400 |
| `sku` > 60 | `El SKU no puede exceder 60 caracteres.` | 400 |
| `sku` con espacios o caracteres de control | `El SKU no admite espacios ni caracteres de control.` | 400 |
| `PATCH` con `sku` | `El SKU del producto no se puede cambiar.` | 400 |
| `name` vacío / > 200 | `El nombre del producto es obligatorio.` / `El nombre no puede exceder 200 caracteres.` | 400 |
| `barcode` > 60 | `El código de barras no puede exceder 60 caracteres.` | 400 |
| `brand` > 100 caracteres, ya recortada (alta o edición; el error va en `errors.brand`) | `La marca no puede exceder 100 caracteres.` | 400 |
| `model` > 100 caracteres, ya recortado (alta o edición; el error va en `errors.model`) | `El modelo no puede exceder 100 caracteres.` | 400 |
| `warehousePublicId` o algún `warehousePublicIds` de la lista de productos que no existe o es de otra compañía | `Almacén no encontrado.` | 404 |
| SKU repetido para el mismo dueño | `Ya existe un producto con ese SKU para ese dueño.` | 409 |
| Código de barras repetido (producto activo) | `Ya existe un producto activo con ese código de barras.` | 409 |
| Costo/precio negativo | `El costo y el precio no pueden ser negativos.` | 400 |
| Costo/precio con más de 4 decimales | `El {campo} admite como máximo 4 decimales.` | 400 |
| Costo/precio fuera de rango | `El costo o el precio excede el máximo permitido.` | 400 |
| Peso/volumen negativo | `El peso y el volumen no pueden ser negativos.` | 400 |
| Peso con más de 3 decimales o ≥ 10⁹ | `El peso admite como máximo 3 decimales y debe ser menor que 1,000,000,000.` | 400 |
| Volumen con más de 4 decimales o ≥ 10⁸ | `El volumen admite como máximo 4 decimales y debe ser menor que 100,000,000.` | 400 |
| Mínimos negativos | `Los mínimos no pueden ser negativos.` | 400 |
| `maxPickQty` < `minPickQty` | `El máximo de la posición de picking debe ser mayor o igual al mínimo.` | 400 |
| `minPickQty` sin posición preferida en zona PICKING | `El mínimo de picking requiere una posición preferida en una zona PICKING.` | 400 |
| Posición preferida de otro almacén que el preferido | `La posición preferida debe pertenecer al almacén preferido.` | 400 |
| Cambiar seguimiento, unidad base o dueño con movimientos | `No se puede cambiar {el tipo de seguimiento / la unidad de medida base / el dueño} de un producto que ya tiene movimientos.` | 409 |
| Baja con inventario en mano | `El producto {sku} tiene inventario en mano ({qty}); no se puede desactivar.` | 409 |
| Baja con documentos abiertos | `El producto {sku} está en recibos abiertos, tareas pendientes, recolecciones o conteos abiertos; ciérrelos antes de desactivarlo.` | 409 |
| Categoría: nombre vacío / > 150 | `El nombre de la categoría es obligatorio.` / `El nombre de la categoría no puede exceder 150 caracteres.` | 400 |
| Categoría como su propia ascendente | `Una categoría no puede ser su propia ascendente.` | 400 |
| Más de 5 niveles | `Las categorías admiten como máximo 5 niveles.` | 400 |
| Nombre de categoría repetido en el mismo nivel | `Ya existe una categoría con ese nombre en ese nivel.` | 409 |
| Baja de categoría con productos activos | `La categoría tiene productos activos.` | 409 |
| Baja de categoría con subcategorías activas | `La categoría tiene subcategorías activas; desactívelas primero.` | 409 |
| Producto de otro tenant, inexistente, o de otro dueño con `InventoryScope` fijado | `Producto no encontrado.` | 404 |

### Estatus

`Product.IsActive` (baja lógica, sin dominio de estatus propio): activo ↔ inactivo, con las guardas de arriba.
`InventoryLot.IsActive` y `InventorySerial` (ver sección 3) no tienen baja manual: se gobiernan por el ledger.

---

## 3. Inventario: saldos, Kárdex, ajustes, transferencias, genealogía, rastro de serie y conciliación

Qué hace: expone el saldo por almacén/posición/lote, el Kárdex de solo lectura del ledger, el ajuste manual con
motivo de catálogo, la transferencia entre posiciones o almacenes, la genealogía de un lote, el rastro de una
serie y la conciliación ledger ↔ saldo. `InventoryLedger` es la única vía de escritura: **nadie** hace `UPDATE`
sobre `InventoryTransaction` (una reversa siempre es un movimiento nuevo) ni escribe `StockBalance` fuera de él.

La cantidad del ledger se guarda **con signo** (maestro L331: "cada despacho escribe movimiento negativo; cada
recepción, positivo"): `RECEIPT` entra (+), `ISSUE`/`CROSSDOCK` salen (−), `ADJUSTMENT` entra o sale según lo
capturado, `TRANSFER` es una sola fila con origen y destino. El disponible siempre es "en mano − reservado"
calculado en código (nunca la columna computada `QtyAvailable`).

Quién puede: `inventory.view` (saldos, Kárdex, genealogía, rastro de serie); `inventory.adjust` (ajuste,
transferencia, conciliación). Módulo **WMS_LOTSERIAL**.

Cómo se usa:
- `GET /api/v1/inventory/balances?warehousePublicIds=&binIds=&productPublicIds=&categoryIds=&lotNumber=&
  includeZero=&onlyAvailable=&search=&skip=&take=` (`take` ≤ 200).
- `GET /api/v1/inventory/transactions?from=&to=&types=&warehousePublicIds=&binIds=&productPublicIds=&
  categoryIds=&lotNumber=&serialNumber=&refEntity=&refId=&search=&brands=&name=` (`brands` y `name`, desde el Lote 12: marca
  igual a alguna y nombre del producto que contiene el texto; ver la sección 2).
- `POST /api/v1/inventory/adjustments` — `{ "productPublicId": "...", "warehousePublicId": "...", "binId": 5,
  "quantity": -2, "reason": "DAMAGE", "notes": "Caja aplastada en el muelle" }` (positivo entra, negativo sale).
  **La nota es obligatoria** (ajuste del 2026-09-30): el API exige `notes` no vacía (después de quitar espacios), de hasta
  300 caracteres, para todo motivo que captura una persona (`DAMAGE`, `LOSS`, `FOUND`, `EXPIRED`, `PO_SHORTAGE`, `OTHER` y
  cualquier motivo que agregue la compañía). Aplica a las pantallas, a la app móvil y a las integraciones. **No la exigen**
  los ajustes que escribe el sistema, porque no pasan por este endpoint: la diferencia de un recibo (`RECEIPT_VARIANCE`), la
  de un conteo (`COUNT_VARIANCE`), la reversa al eliminar una recolección (`PICK_BATCH_REVERSAL`) y el saldo inicial de la
  migración (`OPENING_BALANCE`). Tampoco la exigen la transferencia ni el "ajuste manual" con que se resuelve un faltante de
  compra (sección 8): ahí la nota sigue siendo opcional, porque el movimiento queda ligado a la orden y a su línea.
- `POST /api/v1/inventory/transfers` — `{ "productPublicId": "...", "fromBinId": 5, "toBinId": 8, "quantity": 3 }`
  (entre almacenes: agrega `fromWarehousePublicId`/`toWarehousePublicId`).
- `GET /api/v1/inventory/lots/{lotId}/genealogy`.
- `GET /api/v1/inventory/serials/trace?productPublicId=&serialNumber=`.
- `GET /api/v1/inventory/reconciliation?productPublicId=` — compara el saldo reconstruido desde el ledger contra
  `StockBalance`; `mismatches: []` significa que el invariante se cumple.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Cantidad ≤ 0 | `La cantidad debe ser mayor que cero.` | 400 |
| Cantidad con más de 3 decimales | `La cantidad admite como máximo 3 decimales.` | 400 |
| Cantidad fuera de rango | `La cantidad excede el máximo permitido.` | 400 |
| Ajuste sin cantidad / cantidad = 0 | `Indique la cantidad del ajuste.` / `La cantidad del ajuste no puede ser cero.` | 400 |
| Ajuste sin motivo | `Indique el motivo del ajuste.` | 400 |
| Motivo reservado al sistema (`RECEIPT_VARIANCE`, `COUNT_VARIANCE`, `PICK_BATCH_REVERSAL`, `OPENING_BALANCE`) | `El motivo {código} lo asigna el sistema.` | 400 |
| Ajuste sin nota, o con solo espacios (ajuste del 2026-09-30; el error va en `errors.notes`) | `Escriba una nota que explique el ajuste.` | 400 |
| Nota del ajuste o de la transferencia de más de 300 caracteres (`errors.notes`) | `Las notas admiten como máximo 300 caracteres.` | 400 |
| Motivo desconocido | `Motivo de ajuste desconocido: 'X'.` | 400 |
| Producto controlado por lote sin lote | `El producto se controla por lote: indique el lote.` (o, en el ajuste, `El producto {sku} se controla por lote; indique el lote.`) | 400 |
| Producto sin lote/serie con lote indicado | `El producto no se controla por lote ni por serie: no indique lote.` | 400 |
| Producto con serie: cantidad no entera | `En productos con serie la cantidad debe ser entera.` | 400 |
| Series capturadas ≠ cantidad | `La cantidad debe ser igual al número de series capturadas.` | 400 |
| Producto sin serie con series indicadas | `El producto no se controla por serie: no indique números de serie.` | 400 |
| Transferencia al mismo origen y destino | `El origen y el destino no pueden ser la misma posición.` (o `La posición de origen y la de destino son la misma.`) | 400 |
| Transferencia sin origen/destino | `Indique la posición de origen.` / `Indique la posición de destino.` | 400 |
| Producto inactivo en una entrada | `El producto {sku} está inactivo; no admite entradas de inventario.` | 422 |
| Posición inactiva en un movimiento | `La posición {bin} está inactiva; no admite movimientos de inventario.` | 422 |
| Almacén inactivo en un movimiento | `El almacén {code} está inactivo; no admite movimientos de inventario.` | 422 |
| Salida por encima del disponible | `Inventario insuficiente de {sku} en {bin}: disponible {x}, solicitado {y}.` | 409 (`insufficient_stock`) |
| Liberar más reserva de la que hay | `La reserva a liberar excede lo reservado.` | 409 |
| Lote existente con otras fechas | `El lote {n} ya existe con otras fechas; corrija las fechas o use otro número de lote.` | 409 |
| Serie no disponible en la posición | `La serie {s} no está disponible en {bin}.` | 409 |
| Serie ya en inventario | `La serie {s} ya está en inventario.` | 409 |
| Serie dada de baja | `La serie {s} fue dada de baja; no vuelve al inventario.` | 409 |
| `desde` posterior a `hasta` (Kárdex) | `La fecha 'desde' no puede ser posterior a la fecha 'hasta'.` | 400 |
| Lote/producto de otro tenant o de otro dueño (con `InventoryScope`) | `Lote no encontrado.` / `Producto no encontrado.` | 404 |

### Notas de lectura

- El Kárdex expone `quantity` (la del ledger, con signo) y `signedQuantity` (la misma perspectiva aplicada a un
  filtro de almacenes/posiciones: lo que sale del filtro es negativo, lo que entra es positivo, y lo interno o sin
  filtro de una `TRANSFER` vale 0).
- La genealogía de un lote arma sus destinos (orden, cliente, consignatario) leyendo el `Ref` de cada movimiento;
  la conciliación reconstruye `StockBalance` sumando el lado "To" y restando el lado "From" de cada fila del
  ledger (por clave) y, por producto, comparando la suma de `Quantity` sin `TRANSFER` contra `QtyOnHand`.

---

## 4. Recepción: avisos de llegada (ASN) y recibos

Qué hace: recibe mercancía contra un aviso de llegada de un cliente 3PL, contra una orden de compra propia, en
modo ciego o como devolución. La cantidad recibida arranca igual a la esperada (se puede corregir antes de
confirmar). Al confirmar, se asienta el ledger (`RECEIPT` por lo esperado + `ADJUSTMENT RECEIPT_VARIANCE` por la
diferencia), la orden de compra avanza a `PARTIAL`/`RECEIVED`, el cruce de muelle asignado se reparte y se crean
las tareas `PUTAWAY` del remanente con una posición sugerida.

Lote 13 (Lote 3 del plan de cambios): el recibo tiene un ciclo de seis estatus que muestra en qué va (Esperado,
Recibiendo, Discrepancia, Completado, Completado con diferencia y Acomodado; ver "Estatus y transiciones"). Un recibo
ciego o de devolución se puede crear **solo con el encabezado** (sin líneas) y llenarse después; en esos recibos se puede
capturar también la **cantidad esperada** de cada línea para ver la diferencia. El encabezado lleva además **Transporte**
(`carrier`), **Referencia** (`reference`) y la **posición de recepción por defecto**, y se edita mientras el recibo está
abierto (`PATCH`). En ciegos y devoluciones la diferencia es informativa: al Kárdex entra **lo recibido** (un solo
`RECEIPT`, sin ajuste) y el recibo queda "Completado con diferencia". La pantalla (Recibo en maestro-detalle, con las pestañas
Recibos, Avisos de llegada y Acomodo pendiente) está descrita en el manual de pantallas:
[F6 — Recepción](frontend/f6-almacen-e-inventario.md#recepción-recibos-y-avisos-de-llegada).

Quién puede: `inventory.view` (listar y consultar); `warehouse.receive` (alta de ASN y de recibo, captura de
líneas, confirmar, cancelar/eliminar). Recibir contra una orden de compra exige **además** `purchasing.receive` y
el módulo **PURCHASING** encendido. Módulo **WMS_LOTSERIAL**.

Cómo se usa:
- `GET/POST /api/v1/asns`, `POST /api/v1/asns/{id}/cancel`.
- `GET /api/v1/receipts?warehousePublicId=&status=&types=&from=&to=&productPublicIds=&hasVariance=&search=&variance=&phase=`.
  Lote 13:
  - `phase` = fase del recibo: `OPEN` (Esperado, Recibiendo o Discrepancia), `PENDING_PUTAWAY` (Completado o
    Completado con diferencia: confirmados con acomodo por cerrar) o `DONE` (Acomodado). Es la base de la pestaña
    "Acomodo pendiente".
  - `variance` (se puede repetir; varias se combinan con "o"): `SHORT` = faltante (alguna línea recibió menos de lo
    esperado), `OVER` = sobrante (alguna recibió más, o una línea extra de un recibo con aviso u orden de compra recibió
    algo), `NONE` = sin diferencia (ninguna línea difiere).
  - `status` admite los seis códigos nuevos (`EXPECTED`, `RECEIVING`, `DISCREPANCY`, `RECEIVED`, `RECEIVED_VARIANCE`,
    `PUTAWAY`); `OPEN` ya no existe.
  - `search` busca además en el transporte y la referencia del recibo.
  - Cada fila trae además `carrier`, `reference`, `expectedDate` (la llegada esperada del aviso; si no tiene, la de la
    orden de compra), `defaultStagingBinId`/`defaultStagingBinCode`, `dockId`, `isOpen` y `pendingPutawayCount` (tareas
    de acomodo abiertas). La ficha trae `canDelete` (abierto y sin cruce de muelle asignado).
- `POST /api/v1/receipts` — contra ASN (`asnId`), contra PO (`purchaseOrderPublicId`), ciego (`type: "BLIND"`,
  por defecto) o de devolución (`type: "RETURN"`), con `stagingBinId` opcional (si no, la primera posición activa
  de una zona `STAGING` del almacén). Lote 13: `stagingBinId` queda como **posición de recepción por defecto** del
  encabezado (la toman las líneas que se agreguen sin posición); `carrier` y `reference` (hasta 80 caracteres; vacío = sin
  dato). Un ciego o devolución **sin `lines`** crea solo el encabezado en **Esperado** (antes respondía 400); contra aviso u
  orden de compra nace en **Recibiendo** (o **Discrepancia** si lo capturado en `lines` difiere de lo esperado). En `lines`
  de un ciego o devolución se puede mandar `expectedQty`.
- `PATCH /api/v1/receipts/{publicId}` (Lote 13, `warehouse.receive`) — edita el encabezado de un recibo **abierto**
  (Esperado, Recibiendo o Discrepancia). `null` o ausente = no cambiar:
  - `type`: `BLIND` ↔ `RETURN`, solo en recibos sin aviso ni orden de compra;
  - `warehousePublicId`: solo sin aviso ni orden de compra y **sin líneas**; al cambiarlo se limpian la posición por
    defecto y el muelle (el almacén nuevo debe estar activo y tener zona `STAGING`, igual que en el alta);
  - `stagingBinId` (posición `STAGING` o `CROSSDOCK` del almacén del recibo) o `clearStagingBin: true`;
  - `dockId` (muelle del almacén del recibo) o `clearDock: true`;
  - `carrier` y `reference`: texto de hasta 80; `""` los borra;
  - `rowVersion`: el de la ficha; si el recibo cambió desde que se leyó → 409.
- `PUT /api/v1/receipts/{publicId}/lines/{lineId}` — captura cantidad recibida, lote y series. Lote 13: `expectedQty`
  (o `clearExpected: true`) solo en ciegos y devoluciones; `productPublicId` cambia el producto de una línea que **no**
  viene del aviso u orden de compra (mismo dueño que exige el alta de línea; el lote y las series del producto anterior se
  limpian si no se mandan nuevos).
- `POST/DELETE /api/v1/receipts/{publicId}/lines` (línea extra, solo recibos abiertos). Lote 13: `expectedQty` solo en
  ciegos y devoluciones; la línea sin `stagingBinId` toma la posición por defecto del encabezado (si no hay, la de otra
  línea o la primera `STAGING`).
- Cada alta, cambio o baja de línea **sincroniza el estatus** abierto: Recibiendo si todo cuadra, Discrepancia si alguna
  línea tiene diferencia (con historial de cada cambio).
- `POST /api/v1/receipts/{publicId}/confirm` — confirma el recibo **completo** (no hay confirmación por línea). Lote 13:
  termina en **Completado** (`RECEIVED`) o, si alguna línea tiene diferencia, en **Completado con diferencia**
  (`RECEIVED_VARIANCE`); el comentario va en ese último paso del historial.
- Lote 8A (cola del aparato) — `POST /api/v1/receipts` con `confirm: true` crea, captura y confirma en una sola
  transacción. Contra un aviso (`asnId`) o una orden de compra (`purchaseOrderPublicId`), las `lines` de la solicitud
  se aplican sobre las líneas del documento: **lo escaneado manda** (por producto y, si trae lote, por lote); una línea
  del documento que la solicitud no menciona queda recibida en 0 (faltante visible) y un producto que no está en el
  documento entra como línea extra (no cuenta contra la orden de compra). Sin `lines` se recibe lo esperado, como en el
  alta por pasos. Mismas validaciones y mensajes que la captura por pasos, con la clave `lines[i]`.
- `DELETE /api/v1/receipts/{publicId}` — solo abiertos (Esperado, Recibiendo o Discrepancia) y sin cruce de muelle
  asignado; es una baja lógica (el recibo deja de listarse, sus líneas se conservan).
- `GET /api/v1/asns` (Lote 13) — además de `warehousePublicId`, `status`, `clientPublicId` y `search`: `reference` (la
  referencia contiene el texto, sin distinguir mayúsculas) y `expectedFrom`/`expectedTo` (llegada esperada, ambos
  extremos incluidos; un aviso sin fecha no entra en el rango). Sigue devolviendo hasta 200 avisos (la app móvil los
  descarga así).

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Tipo desconocido | `Tipo de recepción desconocido: 'X'. Use ASN, BLIND o RETURN.` | 400 |
| Tipo `ASN` sin `asnId` ni `purchaseOrderPublicId` | `Un recibo con aviso de llegada se crea indicando el aviso (asnId) o la orden de compra (purchaseOrderPublicId).` | 400 |
| `asnId` y `purchaseOrderPublicId` a la vez | `Indique el aviso de llegada o la orden de compra, no ambos.` | 400 |
| Sin posición de recepción (sin zona `STAGING`) | `El almacén no tiene una posición de recepción (zona STAGING); indíquela.` | 422 |
| Posición de recepción fuera de zona STAGING/CROSSDOCK | `La posición de recepción debe estar en una zona STAGING o CROSSDOCK.` | 400 |
| Almacén del ASN/PO distinto del indicado | `El aviso de llegada es de otro almacén.` / `La orden de compra es de otro almacén.` | 400 |
| ASN no `EXPECTED` | `El aviso de llegada no está pendiente de recibir.` | 422 |
| ASN con recibo abierto o confirmado | `El aviso de llegada ya tiene un recibo abierto o confirmado.` | 409 |
| PO no `SENT`/`PARTIAL` | `La orden de compra debe estar enviada o recibida parcial para recibir contra ella.` | 422 |
| PO sin pendiente | `La orden de compra no tiene cantidades pendientes de recibir.` | 422 |
| PO con un recibo ya abierto | `La orden de compra ya tiene un recibo abierto; confírmelo o elimínelo antes de recibir de nuevo.` | 409 |
| Producto de un cliente distinto del ASN | `El producto {sku} no pertenece al cliente del aviso de llegada.` | 400 |
| PO con producto de un cliente | `La orden de compra solo admite productos propios; {sku} pertenece a un cliente.` | 400 |
| Recibo ya confirmado (editar) | `El recibo {n} ya fue confirmado; no se puede modificar.` | 422 |
| Línea del aviso eliminada | `Las líneas del aviso de llegada no se eliminan; capture 0 como recibido.` | 400 |
| Línea con cruce de muelle asignado | `La línea tiene asignaciones de cruce de muelle; cancélelas antes de eliminarla.` | 409 |
| Recibo con cruce de muelle asignado (eliminar) | `El recibo tiene asignaciones de cruce de muelle; cancélelas antes de eliminarlo.` | 409 |
| Más de 200 líneas | `El recibo admite como máximo 200 líneas.` (aviso: `El aviso de llegada admite como máximo 200 líneas.`) | 400 |
| Sin líneas al confirmar | `El recibo no tiene líneas; agregue al menos una antes de confirmar.` | 422 |
| `receivedQty` ausente / negativo | `Indique la cantidad recibida.` / `La cantidad recibida no puede ser negativa.` | 400 |
| Cantidad esperada ≤ 0 (ASN) | `La cantidad esperada debe ser mayor que cero.` | 400 |
| Lote requerido / no admitido según seguimiento | `El producto {sku} se controla por lote: indique el lote.` / `El producto {sku} no se controla por lote; no indique lote.` | 400 |
| Serie: cantidad no entera / no coincide con lo capturado | `El producto {sku} se controla por serie: la cantidad debe ser entera.` / `El producto {sku} se controla por serie: capture {qty} número(s) de serie (hay {n}).` | 400 |
| Serie repetida | `El número de serie 'X' está repetido.` | 400 |
| Almacén inactivo | `El almacén está dado de baja; no admite recepciones.` | 422 |
| Producto inactivo | `El producto {sku} está dado de baja; no se puede recibir.` | 422 |
| Recibo de otro tenant o inexistente | `Recibo no encontrado.` | 404 |
| Segunda confirmación (recibo ya confirmado) | mismo `El recibo {n} ya fue confirmado; no se puede modificar.` | 422 |
| `confirm: true` en un ciego o devolución sin `lines` (Lote 13: sin `confirm` ya no es error) | `Indique al menos una línea.` | 400 |
| `expectedQty` en un recibo con aviso u orden de compra (Lote 13) | `La cantidad esperada solo se captura en recibos ciegos o de devolución; en uno con aviso de llegada u orden de compra viene del documento.` (clave `expectedQty`, `line.expectedQty` al agregar una línea o `lines[i].expectedQty` en el alta) | 400 |
| `expectedQty` negativo (Lote 13) | `La cantidad esperada no puede ser negativa.` (más de 3 decimales: `La cantidad admite como máximo 3 decimales.`; demasiado grande: `La cantidad excede el máximo permitido.`; producto por serie: `El producto {sku} se controla por serie: la cantidad debe ser entera.`) | 400 |
| Cambiar el producto de una línea del aviso u orden de compra (Lote 13) | `El producto de una línea del aviso de llegada o de la orden de compra no se puede cambiar.` | 400 |
| Cambiar el producto de una línea con cruce de muelle asignado (Lote 13) | `La línea tiene asignaciones de cruce de muelle; cancélelas antes de cambiar el producto.` | 409 |
| `PATCH` del tipo de un recibo con aviso u orden de compra (Lote 13) | `El tipo de un recibo con aviso de llegada u orden de compra no se puede cambiar.` | 400 |
| `PATCH` del almacén con documento o con líneas (Lote 13) | `El almacén solo se puede cambiar en un recibo sin aviso de llegada ni orden de compra y sin líneas.` | 400 |
| Transporte o referencia de más de 80 caracteres (Lote 13) | `El transporte admite como máximo 80 caracteres.` / `La referencia admite como máximo 80 caracteres.` | 400 |
| `PATCH` de un recibo confirmado (Lote 13) | `El recibo {n} ya fue confirmado; no se puede modificar.` | 422 |
| `PATCH` con un `rowVersion` viejo (Lote 13) | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Muelle de otro almacén (alta o `PATCH`) | `Muelle no encontrado.` | 404 |
| Filtro `variance` desconocido (Lote 13) | `Diferencia desconocida: 'X'. Use SHORT, OVER o NONE.` | 400 |
| Filtro `phase` desconocido (Lote 13) | `Fase desconocida: 'X'. Use OPEN, PENDING_PUTAWAY o DONE.` | 400 |
| Número de lote vacío / de más de 60 caracteres | `Indique el número de lote.` / `El número de lote admite como máximo 60 caracteres.` | 400 |
| Vencimiento anterior a la fabricación | `La fecha de vencimiento no puede ser anterior a la de fabricación.` | 400 |
| Lote que ya existe con otras fechas | `El lote {n} ya existe con otras fechas; corrija las fechas o use otro número de lote.` | 409 |
| Serie vacía / de más de 80 caracteres / más de 500 por línea | `Los números de serie no pueden estar vacíos.` / `Cada número de serie admite como máximo 80 caracteres.` / `Una línea admite como máximo 500 números de serie.` | 400 |
| Producto sin serie con series capturadas | `El producto {sku} no se controla por serie; no capture números de serie.` | 400 |
| Cantidad (recibida o esperada) con más de 3 decimales / demasiado grande | `La cantidad admite como máximo 3 decimales.` / `La cantidad excede el máximo permitido.` | 400 |
| Confirmar con el seguimiento incompleto de una línea | los mensajes de lote y serie de arriba, en `errors["lines[i]"]` (título `Datos inválidos.`) | 400 |
| Confirmar con un `rowVersion` viejo | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| `rowVersion` que no es base64 (confirmar o `PATCH`) | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 |
| Línea de otro recibo o inexistente | `Línea del recibo no encontrada.` | 404 |
| Producto inexistente (línea nueva o cambio de producto) | `Producto no encontrado.` | 404 |
| Almacén inexistente o de otra compañía (alta o `PATCH`) | `Almacén no encontrado.` | 404 |
| Ciego o devolución sin `warehousePublicId` con más de un almacén activo / sin almacenes activos | `Indique el almacén: la compañía tiene más de uno.` (400, `errors.warehousePublicId`) / `La compañía no tiene almacenes activos.` (422) | 400 / 422 |
| Posición de recepción de otro almacén o inexistente (alta, línea o `PATCH`) | `Posición no encontrada.` | 404 |
| Posición de recepción desactivada | `La posición de recepción está desactivada.` | 422 |
| Aviso de llegada inexistente | `Aviso de llegada no encontrado.` | 404 |
| Aviso de llegada sin cliente | `Indique el cliente dueño de la mercancía del aviso de llegada.` | 400 |
| Aviso de llegada sin líneas / con más de 200 | `Indique al menos una línea.` / `El aviso de llegada admite como máximo 200 líneas.` (`errors.lines`) | 400 |
| Referencia del aviso de más de 80 caracteres | `La referencia admite como máximo 80 caracteres.` | 400 |
| Cancelar un aviso que ya no está pendiente | `El aviso de llegada no está pendiente de recibir.` | 422 |
| Cancelar un aviso que tiene un recibo | `El aviso de llegada tiene un recibo abierto; elimínelo antes de cancelar.` | 409 |
| Recibir contra PO sin `purchasing.receive` | `Falta el permiso 'purchasing.receive'.` | 403 |
| Recibir contra PO con módulo PURCHASING apagado | `El módulo 'PURCHASING' no está habilitado para esta compañía.` | 403 |

### Estatus y transiciones

`AsnStatus`: **EXPECTED** (inicial) → **RECEIVED** (al confirmar su recibo); **CANCELLED** (lateral desde
`EXPECTED`, solo si no tiene un recibo abierto — si lo tiene, `409` `El aviso de llegada tiene un recibo abierto;
elimínelo antes de cancelar.`).

`ReceiptStatus` (Lote 13; antes `OPEN` → `RECEIVED` → `PUTAWAY`):

| Código | Etiqueta | Tipo | Significado |
|---|---|---|---|
| `EXPECTED` | Esperado | inicial | Solo encabezado (ciego o devolución sin líneas). |
| `RECEIVING` | Recibiendo | etapa | Con líneas y todo cuadra (o nada que comparar). |
| `DISCREPANCY` | Discrepancia | lateral | Alguna línea recibió distinto de lo esperado. |
| `RECEIVED` | Completado | etapa | Confirmado sin diferencia; acomodo pendiente. |
| `RECEIVED_VARIANCE` | Completado con diferencia | lateral | Confirmado con diferencia; acomodo pendiente. |
| `PUTAWAY` | Acomodado | terminal | Se cerró el último acomodo (o no hubo nada que acomodar). |

Transiciones (todas por el motor de estatus, con historial en `/api/v1/status/history/RECEIPT/{id}`; cada paso deja una fila):

| De → a | Quién | Qué valida | Efectos |
|---|---|---|---|
| (alta) → **Esperado** | `warehouse.receive` | Ciego o devolución sin `lines` y sin `confirm`. El almacén debe estar activo y tener una zona `STAGING` (o traer `stagingBinId`). | Crea el encabezado con número `REC-#####`, sin líneas. |
| (alta) → **Recibiendo** | `warehouse.receive`; contra una orden de compra, además `purchasing.receive` y el módulo PURCHASING | Contra aviso: aviso `EXPECTED` y sin recibo. Contra orden: `SENT` o `PARTIAL`, con pendiente y sin recibo abierto. Ciego o devolución con `lines`: todas las líneas cuadran. | Crea una línea por línea del documento (recibido = esperado); una orden de compra genera además su aviso. |
| (alta) → Recibiendo → **Discrepancia** | igual | `lines` con diferencia: en un ciego, `expectedQty` distinto de lo recibido; con documento, lo escaneado distinto de lo esperado. | Dos pasos en el historial. |
| Esperado → **Recibiendo** | quien guarda la línea (`warehouse.receive`) | La línea pasa las validaciones de captura. | Con diferencia, Esperado → Recibiendo → **Discrepancia** (dos pasos). |
| Recibiendo → **Discrepancia** | quien guarda la línea | Alguna línea con recibido distinto de esperado. | — |
| Discrepancia → **Recibiendo** | quien guarda la línea | Ninguna línea con diferencia. | — |
| Recibiendo → **Completado** | `warehouse.receive` (`POST .../confirm`) | Recibo abierto (si no, 422); `rowVersion` vigente si se manda (409); al menos una línea (422); seguimiento de cada línea (400 por línea: lote con recibido mayor que 0, series iguales a la cantidad); con orden de compra, que siga recibible. | Ver "Efectos de confirmar". |
| Discrepancia → **Completado con diferencia** | igual | igual | Ver "Efectos de confirmar". |
| Completado → **Acomodado** | el sistema (efecto de las tareas) | La última `PUTAWAY` del recibo llega a `DONE` o `CANCELLED` y no queda otra abierta. Iniciar y completar exigen `warehouse.receive`; asignar y cancelar, `warehouse.manage`. | — |
| Completado con diferencia → **Acomodado** | el sistema | igual | — |
| Completado o Completado con diferencia → **Acomodado** (directo) | el sistema, al confirmar | Ninguna `PUTAWAY` se creó: todo fue a cruce de muelle o se recibió 0. | — |
| Esperado, Recibiendo o Discrepancia → baja (`DELETE`) | `warehouse.receive` | Recibo abierto (si no, 422) y sin cruce de muelle asignado (409). | Baja lógica (el recibo deja de listarse, las líneas se conservan). Un aviso nacido de una orden de compra se cancela; el de un cliente vuelve a quedar pendiente. |

**Efectos de confirmar** (una sola transacción):
- Kárdex: con aviso u orden de compra, `RECEIPT` por lo esperado más `ADJUSTMENT RECEIPT_VARIANCE` por la diferencia (D4; el
  ajuste del sistema no pide nota). En ciegos y devoluciones, un solo `RECEIPT` por lo **recibido**: la diferencia solo marca el
  estatus y `adjustmentTxnId` queda vacío.
- La orden de compra avanza a `PARTIAL` o `RECEIVED`; el aviso pasa a `RECEIVED`.
- Se reparte el cruce de muelle asignado y se crean las `PUTAWAY` del remanente con una posición sugerida.
- El recibo guarda la fecha y el usuario de confirmación (`receivedAtUtc`, `receivedBy`) y Actividad reciente registra
  `RECEIPT_CONFIRMED` (tanto para Completado como para Completado con diferencia).
- El destino es **Completado con diferencia** si alguna línea tiene diferencia y ese estatus está encendido para la compañía; si no,
  **Completado**. El comentario del cuerpo va en el último paso del historial.

**Qué se puede hacer en cada estatus:**

| Estatus | Se puede | Queda bloqueado |
|---|---|---|
| Esperado | Editar el encabezado (`PATCH`), agregar líneas y borrar el recibo. | Confirmar: sin líneas responde `422` `El recibo no tiene líneas; agregue al menos una antes de confirmar.` |
| Recibiendo | Todo lo anterior, cambiar o quitar líneas, confirmar y asignar cruce de muelle contra lo recibido de una línea. | — |
| Discrepancia | Igual que Recibiendo. La diferencia se corrige guardando las líneas o se confirma tal cual. | — |
| Completado | Consultar, trabajar sus tareas de acomodo y asignar cruce de muelle contra el remanente con acomodo pendiente. | `PATCH`, alta, cambio y baja de líneas, confirmar y `DELETE`: `422` `El recibo {n} ya fue confirmado; no se puede modificar.` |
| Completado con diferencia | Igual que Completado. | Igual que Completado. |
| Acomodado | Consultar. | Igual que Completado. |

Un recibo abierto también bloquea acciones de otros documentos: cancelar, eliminar o cambiar las líneas de la orden de compra de la
que nació (`409` `La orden de compra tiene un recibo abierto; confírmelo o elimínelo antes de cancelar.` / `... elimínelo antes de
eliminar la orden de compra.` / `... confírmelo o elimínelo antes de cambiar sus líneas.`), cancelar su aviso (`409`), dar de baja
el producto de una de sus líneas o el almacén.

Por compañía y en bases existentes:

- Entradas laterales sembradas (la compañía las cambia en `/status/lateral-entries/RECEIPT`): Discrepancia desde
  Recibiendo; Completado con diferencia desde Recibiendo, Discrepancia y Esperado; Acomodado desde Completado con
  diferencia. Si la compañía **apaga** un estatus: sin Discrepancia la diferencia se queda en Recibiendo; sin Completado
  con diferencia se confirma como Completado; sin Recibiendo el recibo se queda en Esperado hasta confirmarse.
- Bases existentes: el seed pasa los recibos que estaban en `OPEN` a Recibiendo (con líneas) o Esperado (sin líneas),
  con la línea de historial "Lote 13: nuevo ciclo de estatus del recibo.", y retira `OPEN`.

Eliminar un recibo abierto (sin cruce de muelle asignado): si nació de una PO, su ASN se **cancela**; si es de
cliente, el ASN vuelve a quedar `EXPECTED` (pendiente de recibir de nuevo). Un recibo en Esperado se borra igual.

Casos frecuentes (Lote 13):
- *Llegó un camión sin aviso*: cree un recibo ciego solo con encabezado (almacén, transporte, referencia), agregue las
  líneas a medida que cuenta y, si conoce lo esperado (por ejemplo, la factura del proveedor), captúrelo en cada línea
  para ver la diferencia antes de confirmar.
- *El recibo quedó en Discrepancia por error de captura*: corrija la cantidad; al cuadrar vuelve solo a Recibiendo.
- *Buscar recibos con faltante todavía abiertos*: `GET /api/v1/receipts?phase=OPEN&variance=SHORT`.
- *Buscar lo que falta acomodar*: `GET /api/v1/receipts?phase=PENDING_PUTAWAY`; cada fila trae `pendingPutawayCount`.
- *Llegó menos de lo que decía la orden de compra*: capture lo recibido en la línea del documento (0 si no llegó); el recibo pasa
  a Discrepancia y, al confirmar, a Completado con diferencia. El faltante contra la orden se resuelve después en Compras
  (sección 8), porque el recibo confirmado con diferencia cuenta como recibo confirmado para los faltantes.
- *Llegó algo que no venía en el aviso ni en la orden*: por la web no se puede agregar la línea; por el API es una línea extra
  (`POST .../lines`) que entra como ajuste `RECEIPT_VARIANCE` y no cuenta contra la orden.
- *Corregir el transporte o la referencia*: `PATCH /api/v1/receipts/{publicId}` con el `rowVersion` de la ficha; mientras el recibo
  esté abierto se puede repetir las veces que haga falta.

---

## 5. Tareas de almacén: cola unificada, putaway dirigido y reabasto

Qué hace: una cola única de tareas por tipo (`PUTAWAY`, `REPLENISH`, `COUNT`, `CROSSDOCK`, y los tipos `PICK`/
`PACK`/`LOAD` que este lote no completa desde aquí). Cada tipo con handler declara su propio permiso y si se
completa desde esta pantalla o desde la suya (`COUNT` solo se completa reconciliando el conteo). El putaway
sugiere una posición según reglas de consolidación y una aproximación de rotación (alta/baja) por las salidas de
los últimos 30 días. El reabasto (bajo demanda) mueve inventario de `RESERVE` a la posición preferida de picking
cuando su disponible cae bajo el mínimo.

Quién puede: `inventory.view` (listar, ficha, sugerencias de putaway, e iniciar/completar junto con el permiso del
handler del tipo); `warehouse.manage` (asignar y cancelar); `warehouse.pick` (correr el reabasto). El permiso para
iniciar/completar depende del tipo: `PUTAWAY` → `warehouse.receive`, `REPLENISH` → `warehouse.pick`, `COUNT` →
`warehouse.count`, `CROSSDOCK` → `warehouse.crossdock`. Módulo **WMS_LOTSERIAL**.

Cómo se usa:
- `GET /api/v1/warehouse-tasks?warehousePublicId=&types=&status=&assignedToMe=&assignedUserId=&includeClosed=`
  — orden de la cola: prioridad ascendente, luego más antigua primero.
- `GET /api/v1/warehouse-tasks/putaway-suggestions?taskId=` o `?productPublicId=&warehousePublicId=&lotId=&
  quantity=&fromBinId=`.
- `POST /api/v1/warehouse-tasks/{id}/assign` — `{ "userId": 12 }` (null desasigna).
- `POST /api/v1/warehouse-tasks/{id}/start`.
- `POST /api/v1/warehouse-tasks/{id}/complete` — `{ "toBinId": 8, "quantity": 5, "serialNumbers": [...] }` (todo
  opcional: por defecto el destino/cantidad de la tarea).
- `POST /api/v1/warehouse-tasks/{id}/cancel` — solo `PUTAWAY`/`REPLENISH`.
- `POST /api/v1/warehouse-tasks/replenishment/run` — `{ "warehousePublicId": "..." }` (o el único activo).

Lote 13 (pantalla): en la aplicación web ya no hay una cola única de tareas. Cada tipo se trabaja en la pantalla a la que
pertenece: `PUTAWAY` en Recibo (pestaña "Acomodo pendiente" y el detalle del recibo), `REPLENISH` en Recolección y empaque
(pestaña "Reabasto"), `COUNT` en Conteo cíclico y `CROSSDOCK` en Cruce de muelle. En todas, las acciones de cada fila son íconos
con tooltip: Asignar, Iniciar, Completar y Cancelar. Los permisos y endpoints de arriba no cambian. Ver
[F6 — Tareas de almacén](frontend/f6-almacen-e-inventario.md#tareas-de-almacén).

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Tipo sin handler (`PICK`, `PACK`, `LOAD`) | `Las tareas de tipo {tipo} no se completan desde la cola.` | 422 |
| Tarea ya cerrada | `La tarea ya fue completada o cancelada.` | 422 |
| Tarea ya en proceso (al iniciar de nuevo) | `La tarea ya está en proceso.` | 422 |
| Asignado no es miembro activo | `El usuario no es miembro activo de la compañía.` | 400 |
| Cantidad a completar > la de la tarea | `La cantidad excede la de la tarea.` | 400 |
| Cantidad ≤ 0 / con más de 3 decimales | `La cantidad debe ser mayor que cero.` / `La cantidad admite como máximo 3 decimales.` | 400 |
| Sin posición de destino | `Indique la posición de destino.` | 400 |
| Destino igual al origen | `La posición de destino debe ser distinta de la de origen.` | 400 |
| Tarea sin posición de origen | `La tarea no tiene posición de origen; no se puede completar.` | 422 |
| Tarea sin cantidad | `La tarea no tiene cantidad; no se puede completar desde la cola.` | 422 |
| Producto con serie: cantidad no entera | `En productos con serie la cantidad debe ser entera.` | 400 |
| Series capturadas ≠ cantidad | `Indique exactamente {n} número(s) de serie.` | 400 |
| Serie repetida / de otra tarea | `Hay números de serie repetidos.` / `La tarea es de una serie específica; no indique otras series.` | 400 |
| `COUNT` completada desde la cola | `Las tareas de conteo se completan desde Conteo cíclico.` | 422 |
| Cancelar un tipo que no es `PUTAWAY`/`REPLENISH` | `Las tareas de tipo {tipo} se cancelan desde su pantalla, no desde la cola.` | 422 |
| Iniciar/completar sin el permiso del handler | `Falta el permiso '{warehouse.receive\|warehouse.pick\|warehouse.count\|warehouse.crossdock}'.` | 403 |
| Tarea de otro tenant o inexistente | `Almacén no encontrado.` (si el filtro de almacén no existe) o 404 genérico de la tarea | 404 |

### Estatus y transiciones

`WarehouseTaskStatus`: **PENDING** (inicial) → **IN_PROGRESS** → **DONE**; **CANCELLED** (lateral desde `PENDING`/
`IN_PROGRESS`, solo `PUTAWAY`/`REPLENISH` desde la cola). Completar por menos de lo pedido dentro dado en la tarea
deja un remanente como **tarea nueva** con el mismo `Ref` (mismo recibo/plan de origen). Efecto
`WarehouseTaskStatusEffect`: cuando la última `PUTAWAY` con `Ref RECEIPT` de un recibo llega a `DONE` o
`CANCELLED`, el recibo pasa `RECEIVED` o `RECEIVED_VARIANCE` → `PUTAWAY` (ver sección 4).

Reabasto: idempotente por almacén — una segunda corrida no crea tareas nuevas si ya hay una `REPLENISH` abierta
hacia la misma posición (`skippedWithOpenTask`), si no hay reserva disponible (`skippedNoReserve` — informativo, no
aparece como error) o si el disponible ya no está bajo el mínimo.

---

## 6. Conteo cíclico (modo informado)

Qué hace: toma una foto del sistema (saldo en mano por posición/producto/lote), captura lo contado (cantidad o
números de serie) y, al reconciliar, ajusta contra el **saldo actual bloqueado** (no contra la foto): si el saldo
se movió desde que se tomó la foto, la línea queda marcada `systemQtyChanged` para revisión, pero el ajuste sigue
siendo correcto porque se calcula sobre el saldo real en ese instante.

Quién puede: `inventory.view` (solo la lista); `warehouse.count` (ficha, alta, captura, agregar línea, terminar,
refrescar, **reconciliar** y eliminar — el Operador de almacén ya tiene este permiso). Módulo **WMS_LOTSERIAL**.

Cómo se usa:
- `GET /api/v1/cycle-counts?warehousePublicIds=&status=&from=&to=&binIds=&productPublicIds=&categoryIds=&search=`.
- `POST /api/v1/cycle-counts` — `{ "warehousePublicId": "...", "zoneIds": [...], "binIds": [...] }` (todo
  opcional: sin filtros toma todo el saldo en mano del almacén). Máximo 1000 líneas.
- `GET /api/v1/cycle-counts/{id}` — ficha en modo informado (foto, contado, diferencia informativa, series
  esperadas/contadas, saldo actual).
- `PUT /api/v1/cycle-counts/{id}/lines` — captura por línea (`countedQty` en NONE/LOT, `serialNumbers` en SERIAL).
- `POST /api/v1/cycle-counts/{id}/lines` — línea agregada a mano (lo encontrado sin foto previa).
- `POST /api/v1/cycle-counts/{id}/finish` — `OPEN → COUNTED`, exige todas las líneas capturadas.
- `POST /api/v1/cycle-counts/{id}/refresh` — re-fotografía las líneas con foto vieja y borra su captura.
- `POST /api/v1/cycle-counts/{id}/reconcile` — asienta los ajustes y pasa a `RECONCILED`.
- `DELETE /api/v1/cycle-counts/{id}` — solo `OPEN`. Cancela su tarea COUNT (`CANCELLED`, con fecha de cierre: deja de sumar antigüedad y el aparato la borra en su siguiente sincronización).

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Más de 1000 líneas seleccionadas | `El conteo admite como máximo 1000 líneas; acote los filtros.` | 400 |
| Filtros sin inventario en mano | `Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano.` | 400 |
| Conteo ya reconciliado (editar) | `El conteo ya fue reconciliado; solo se consulta.` | 422 |
| Producto con serie: cantidad suelta en vez de series | `En productos con serie se capturan los números de serie, no la cantidad.` | 400 |
| Producto sin serie con series capturadas | `El producto {sku} no se controla por serie; no capture números de serie.` | 400 |
| Línea repetida (posición, producto, lote) | `Esa posición, producto y lote ya están en el conteo.` | 409 |
| Cantidad contada negativa | `La cantidad contada no puede ser negativa.` | 400 |
| Terminar sin todas las líneas capturadas | `Faltan {n} línea(s) por contar.` | 422 |
| Reconciliar con lo contado < lo reservado | `El conteo de {sku} en {bin} ({contado}) es menor que lo reservado ({reservado}); libere la reserva antes de reconciliar.` (sin escribir nada) | 409 |
| Segunda reconciliación | `El conteo ya fue reconciliado; solo se consulta.` | 422 |
| Eliminar un conteo ya terminado/reconciliado | `Solo se elimina un conteo abierto; este ya se terminó de contar.` | 422 |
| Almacén inactivo | `El almacén está inactivo.` | 422 |
| Serie capturada en dos líneas del mismo conteo | `La serie {s} está capturada en más de una línea del conteo.` | 409 |
| Completar la tarea `COUNT` desde la cola | `Las tareas de conteo se completan desde Conteo cíclico.` | 422 |

### Estatus y transiciones

`CycleCountStatus`: **OPEN** (inicial, admite captura) → **COUNTED** (terminar) → **RECONCILED** (terminal). Al
reconciliar: por línea, `ADJUSTMENT COUNT_VARIANCE` = contado − saldo en mano actual bloqueado (`ReconciledSystemQty`
guarda ese saldo; `SystemQtyChanged = true` si es distinto de la foto original); en productos con serie, las
esperadas y no contadas se dan de baja, las contadas y desconocidas o fuera de inventario entran de alta, y las
contadas que el sistema ya tiene en otra posición se transfieren. La tarea `COUNT` de la cola pasa a `DONE` en la
misma transacción. Eliminar (`DELETE`, solo `OPEN`) cancela su tarea `COUNT`.

### Lote 8A — conteo a ciegas (app de almacén)

El almacenista cuenta **sin ver lo esperado** y la reconciliación se hace en la web con `warehouse.count`.

- Permiso nuevo `warehouse.count.capture` (categoría WAREHOUSE, "Capturar conteo (a ciegas)"): alta
  (`POST /api/v1/cycle-counts`), captura por línea (`PUT .../lines`), captura en lote (`PUT .../lines/batch`),
  agregar lo encontrado (`POST .../lines`) y terminar (`POST .../finish`). Quien tiene `warehouse.count` lo tiene
  implícito (los roles propios que ya contaban no pierden nada); el Operador de almacén lo trae en su plantilla.
- Refrescar, **reconciliar** y eliminar siguen exigiendo `warehouse.count` (sin él: 403 `Falta el permiso ...`).
- Ficha y respuestas a ciegas: quien no tiene `warehouse.count` recibe `isBlind = true` y las cantidades esperadas
  de las líneas en `null` (`systemQty`, `varianceQty`, `currentQty`, `reconciledSystemQty`, `adjustedQty`;
  `expectedSerials` vacío), tanto en `GET /api/v1/cycle-counts/{id}` como en la respuesta de alta, captura y
  terminar. `onlyVariance` se ignora a ciegas.
- Solo lectura (`inventory.view` sin `warehouse.count.capture`) ve la ficha a ciegas pero no captura (403).
- A ciegas, el encabezado tampoco revela lo esperado: `count.varianceLines` y `count.netVariance` llegan en `null` en la
  ficha, en las respuestas de alta, captura, captura en lote y terminar, y en la lista `GET /api/v1/cycle-counts`
  (con lo contado permitirían deducir lo esperado). Con `warehouse.count` nunca son `null`.
- En la web (Almacén → Conteos cíclicos) esto cambia lo que ven los roles sin `warehouse.count` (Solo lectura,
  Facturación): la columna "Diferencia neta" ya no se muestra, porque el API ya no la envía. Antes veían la diferencia
  real. En la ficha, un valor `null` se pinta como "—", nunca como 0.
- Sincronización del aparato (`/api/v1/sync/*`, módulo WMS_LOTSERIAL, `inventory.view`): `GET /api/v1/sync/purchase-orders`
  exige además el módulo **PURCHASING** y `purchasing.view`, igual que `/api/v1/purchase-orders` (sin el permiso 403;
  con el módulo apagado 403 `El módulo 'PURCHASING' no está habilitado para esta compañía.`).

---

## 7. Recolección y empaque ad hoc (Pick & Pack)

Qué hace: recolecta inventario (por FEFO o con posición/lote/serie explícitos) hacia una recolección con número
`EMP-#####`, y "empacar" esa recolección crea una orden de transporte real con ese mismo número como número de
empaque. Eliminar una recolección revierte el inventario a su posición original.

El número `EMP-#####` se toma **antes** de contabilizar los despachos: la cabecera de la recolección se inserta
primero (con su número), y luego se asientan los `ISSUE` con la referencia ya puesta en el `INSERT` — nunca hay un
`UPDATE` sobre el ledger. Un faltante revierte también el número: los `EMP` quedan consecutivos y sin huecos.

Quién puede: `inventory.view` (listar y consultar); `warehouse.pick` (recolectar, empacar, eliminar). Empacar
exige **además** `orders.create`; eliminar una recolección ya empacada exige **además** `orders.cancel`. Módulo
**WMS_LOTSERIAL**.

Cómo se usa:
- `GET /api/v1/pick-batches?from=&to=&productPublicIds=&status=&orderNumber=&invoiceNumber=&search=&
  includeDeleted=` (las eliminadas se excluyen por defecto; la búsqueda `search` se aplica al final, sobre lo ya
  filtrado por `orderNumber`/`invoiceNumber`).
- `POST /api/v1/pick-batches` — `{ "warehousePublicId": "...", "lines": [{ "productPublicId": "...", "quantity": 5 }] }`.
- `POST /api/v1/pick-batches/{publicId}/pack` — `{ "order": { ... datos de la orden ... } }`. Sin `serviceType` (o sin
  `packageType` en un paquete) la orden toma el **predeterminado de la compañía**; si la compañía no lo tiene, 400 `El tipo
  de servicio es obligatorio.` (`errors.serviceType`) o `El tipo de paquete es obligatorio.` (`errors["packages[i].packageType"]`).
  Para saber de antemano si hay predeterminados (ajuste del 2026-09-30: la pantalla solo ofrece "Predeterminado de la
  compañía (…)" cuando existe), lea `GET /api/v1/tenant/settings`: `defaultServiceType` y `defaultPackageType` traen el
  código o `null`. Ese endpoint solo exige sesión (ningún permiso ni módulo), así que lo lee quien empaca; la etiqueta sale
  del catálogo (`GET /api/v1/catalogs/ServiceType` y `.../PackageType`).
- Lote 8A (cola del aparato) — `POST /api/v1/pick-batches/collect-and-pack` — el mismo cuerpo de recolectar más
  `"pack": { "order": { ... } }`: recolecta y empaca en **una** transacción y responde `{ batch, order }`
  (`PickBatchPackResultDto`), con las mismas reglas, permisos y mensajes que los dos pasos. Si el empaque falla (por
  ejemplo `Cliente no encontrado.`, 404) no queda la recolección ni sale inventario, y el número `EMP` no se consume.
  Respeta `Idempotency-Key`. Mandar `pack` a `POST /api/v1/pick-batches` responde 400 `Para recolectar y empacar en
  una llamada use POST /api/v1/pick-batches/collect-and-pack.`; sin `pack` en `collect-and-pack` → 400 `Indique los
  datos de la orden que se crea al empacar.`.
- `DELETE /api/v1/pick-batches/{publicId}`.
- Lote 13 (pantalla): la lista y la captura comparten pantalla en dos paneles (ver
  [F6 — Recolección y empaque](frontend/f6-almacen-e-inventario.md#recolección-y-empaque)). La captura manda **una sola** llamada
  `POST /api/v1/pick-batches` con todas las líneas (hasta 100). En la lista, `orderNumber` y `invoiceNumber` buscan por contenido, sin
  distinguir mayúsculas (el filtro "No. de orden" sugiere números con `GET /api/v1/pick-batches?orderNumber=<texto>&take=20`). Cada
  fila trae `canPack` y `canDelete`; la pantalla ofrece Empacar y Eliminar solo cuando son verdaderos (y con `orders.cancel`, para
  eliminar una empacada).

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Productos de más de un dueño en una recolección | `Una recolección solo puede tener productos de un mismo dueño.` | 400 |
| Empaque con entrega especial o chofer | `Un empaque no puede ser una entrega especial ni llevar chofer.` | 400 |
| Sin líneas | `Indique al menos una línea a recolectar.` | 400 |
| Línea sin producto | `Indique el producto.` (`errors["lines[i].productPublicId"]`) | 400 |
| Serie de más de 80 caracteres / más de 500 series en una línea | `Cada número de serie admite como máximo 80 caracteres.` / `Una línea admite como máximo 500 series.` | 400 |
| Almacén sin indicar con más de uno activo / sin almacenes activos | `Indique el almacén: la compañía tiene más de uno.` (400) / `La compañía no tiene almacenes activos.` (422) | 400 / 422 |
| Almacén inactivo | `El almacén está inactivo; no se puede recolectar.` | 422 |
| Más de 100 líneas | `La recolección admite como máximo 100 líneas.` | 400 |
| Cantidad ≤ 0 / con más de 3 decimales / fuera de rango | `La cantidad debe ser mayor que cero.` / `La cantidad admite como máximo 3 decimales.` / `La cantidad excede el máximo permitido.` | 400 |
| Producto con serie: cantidad ≠ series escaneadas | `En productos con serie la cantidad debe ser igual al número de series escaneadas.` | 400 |
| Serie repetida en la recolección | `La serie {s} está repetida en la recolección.` | 400 |
| Serie no disponible | `La serie {s} no está disponible en el almacén (no existe, ya salió o está en otra posición o lote).` | 409 |
| Producto con serie sin series indicadas | `El producto {sku} tiene serie: escanee las series a recolectar.` | 400 |
| Producto sin serie con series indicadas | `El producto {sku} no maneja serie.` | 400 |
| Producto sin lote con lote indicado | `El producto {sku} no maneja lote.` | 400 |
| Producto inactivo | `El producto {sku} está inactivo; no se puede recolectar.` | 422 |
| Posición inactiva o en zona no recolectable | `La posición {bin} está inactiva.` / `La posición {bin} está en una zona {tipo}; de ahí no se recolecta.` | 422 / 400 |
| Inventario insuficiente | `Inventario insuficiente de {sku} en {where}: disponible {x}, solicitado {y}.` (sin efecto parcial; el número EMP tampoco se consume) | 409 (`insufficient_stock`) |
| Orden del empaque de un cliente distinto del dueño | `La orden debe ser del cliente dueño del inventario ({cliente}).` | 400 |
| Empacar una recolección ya empacada | `La recolección {n} ya fue empacada.` | 422 |
| Eliminar una recolección cuya orden ya avanzó | `La orden de la recolección {n} ya avanzó a '{estatus}'; la recolección ya no se puede eliminar.` | 422 |
| Eliminar una recolección ya eliminada | `La recolección {n} fue eliminada; solo se consulta.` | 422 |
| Posición de reversa inactiva (eliminar) | `La posición {bin} de la recolección está inactiva; reactívela para eliminar la recolección y restaurar el inventario.` | 422 |
| Empacar sin datos de la orden (o `collect-and-pack` sin `pack`) | `Indique los datos de la orden que se crea al empacar.` | 400 |
| Empacar sin tipo de servicio, en una compañía sin predeterminado | `El tipo de servicio es obligatorio.` (`errors.serviceType`) | 400 |
| Empacar un paquete sin tipo, en una compañía sin predeterminado | `El tipo de paquete es obligatorio.` (`errors["packages[i].packageType"]`) | 400 |
| Número de recolección o de orden ya tomado (concurrencia) | `Ya existe una recolección con ese número; intente de nuevo.` / `La orden ya está ligada a otra recolección.` | 409 |
| Empacar o eliminar con un `rowVersion` viejo | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Empacar o eliminar con un `rowVersion` que no es base64 | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 |
| Empacar/eliminar sin `orders.create`/`orders.cancel` | `Falta el permiso 'orders.create'.` / `Falta el permiso 'orders.cancel'.` | 403 |
| Borrar desde **Órdenes** una orden nacida de un empaque | `Esta orden nació de la recolección {n}; elimínela desde Recolección y empaque para restaurar el inventario.` | 409 |

### Estatus y transiciones

`PickBatchStatus`: **COLLECTED** (inicial, tras recolectar) → **PACKED** (al empacar: crea la orden real y copia la
factura final al lote) → **CANCELLED** (terminal, al eliminar: revierte cada línea con `ADJUSTMENT
PICK_BATCH_REVERSAL` a su posición original y la serie vuelve a `AVAILABLE`). Se elimina siempre desde `COLLECTED`;
desde `PACKED` solo si su orden sigue **activa** y en la **etapa inicial** (si no, `422` con el mensaje de arriba).
Cancelar la orden nacida de un empaque **no** restaura el inventario por sí sola (la mercancía regresa con un
recibo `RETURN` aparte).

Lote 13 — el mismo ciclo, por transición (Empacar y Eliminar son íconos en la fila de la lista, y también botones del detalle):

| De → a | Quién | Qué valida | Efectos |
|---|---|---|---|
| (recolectar) → Recolectada | `warehouse.pick` | Almacén activo, un solo dueño, hasta 100 líneas, existencia disponible (409 sin efecto parcial), lote y series según el producto. | Número `EMP-#####`; un `ISSUE` por línea (FEFO si no se indica posición ni lote). |
| Recolectada → Empacada | `warehouse.pick` + `orders.create` | La recolección sigue Recolectada (422 `La recolección {n} ya fue empacada.`); cliente de la orden = dueño del inventario; datos de la orden; tipo de servicio y de paquete (o el predeterminado de la compañía); `rowVersion` si se manda. | Crea la orden de transporte real con el número de la recolección como número de empaque y copia la factura final. |
| Recolectada → Cancelada | `warehouse.pick` | Posiciones de reversa activas; `rowVersion` si se manda. | Reversa cada línea con `ADJUSTMENT PICK_BATCH_REVERSAL` a su posición original; la serie vuelve a `AVAILABLE`. |
| Empacada → Cancelada | `warehouse.pick` + `orders.cancel` | La orden creada sigue activa y en su etapa inicial (422 con el mensaje de arriba). | Borra también la orden y hace la misma reversa. |

Qué bloquea: una recolección Empacada no se vuelve a empacar; una Cancelada solo se consulta (`422` `La recolección {n} fue
eliminada; solo se consulta.`) y solo aparece en la lista con `includeDeleted`. La lista no ofrece Empacar (`canPack`) ni Eliminar
(`canDelete`) cuando el servidor los marca como falsos.

---

## 8. Compras: proveedores, órdenes de compra y faltantes

Qué hace: mantiene proveedores, órdenes de compra (con costo congelado por línea) y, tras cada recepción, permite
resolver el faltante pendiente de una línea (cerrar, reordenar o hacer un ajuste manual de inventario). Es la
pantalla "Ajustes de inventario" del maestro para el faltante de compras.

Quién puede: `purchasing.view` (listar, ficha, faltantes); `purchasing.manage` (alta, edición, enviar, cancelar,
eliminar de la orden de compra, y proveedores); `inventory.adjust` (resolver el faltante de una línea — `REORDER`
exige **además** `purchasing.manage`; `MANUAL_ADJUSTMENT` exige **además** el módulo **WMS_LOTSERIAL**). Módulo
**PURCHASING**.

Cómo se usa:
- `GET/POST /api/v1/suppliers`, `PATCH /api/v1/suppliers/{id}`, `POST .../{id}/deactivate|reactivate`.
- `GET /api/v1/purchase-orders?status=&supplierId=&warehousePublicId=&supplierIds=&warehousePublicIds=&from=&to=&search=`.
  Lote 12: `supplierIds` y `warehousePublicIds` aceptan varios valores y se juntan con los singulares sin repetir; la orden
  aparece si su proveedor es **cualquiera** de los indicados y su almacén es **cualquiera** de los indicados (los filtros
  distintos se combinan con "y"). Un id que no existe no da error: simplemente no coincide con ninguna orden.
- `POST /api/v1/purchase-orders` — `{ "supplierId": 1, "warehousePublicId": "...", "lines": [{ "productPublicId": "...",
  "qtyOrdered": 10, "unitCost": 2.5 }] }`. Nace `DRAFT`. Al menos una línea con cantidad mayor que cero. El almacén es
  **opcional en el API** solo si la compañía tiene un único almacén activo (se usa ése); con más de uno hay que indicarlo. La
  pantalla siempre lo pide.
- `PATCH /api/v1/purchase-orders/{publicId}` (fecha esperada, notas, reemplazo de líneas; controlado por la
  capacidad `EDIT_PURCHASE_ORDER`). Desde el ajuste del 2026-09-30 acepta también `supplierId` y `warehousePublicId` (los
  mismos identificadores del alta), **solo mientras la orden está en `DRAFT`** (ver abajo).
- `POST /api/v1/purchase-orders/{publicId}/send` — `DRAFT → SENT`.
- `POST /api/v1/purchase-orders/{publicId}/cancel` — desde `DRAFT`, `SENT` o `PARTIAL`, con comentario.
- `DELETE /api/v1/purchase-orders/{publicId}` — sin recepciones ni recibo abierto.
- `GET /api/v1/purchase-orders/shortages`, `GET .../{publicId}/shortage-lines`.
- `POST /api/v1/purchase-orders/{publicId}/lines/{lineId}/resolve` — `{ "action": "MANUAL_ADJUSTMENT", "quantity":
  1, "reason": "FOUND", "binId": 5 }` (o `"action": "CLOSE"` / `"REORDER"` sin cantidad: resuelven el pendiente
  completo).

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Nombre de proveedor repetido (activo) | `Ya existe un proveedor activo con ese nombre.` | 409 |
| Nombre de proveedor vacío | `El nombre del proveedor es obligatorio.` | 400 |
| Correo de proveedor inválido | `El correo electrónico no es válido.` | 400 |
| Teléfono de proveedor de más de 40 caracteres (`errors.phone`) | `El teléfono admite como máximo 40 caracteres.` | 400 |
| Proveedor de otro tenant o inexistente | `Proveedor no encontrado.` | 404 |
| Sin líneas / más de 200 (alta y edición) | `La orden de compra debe tener al menos una línea.` / `La orden de compra admite como máximo 200 líneas.` | 400 |
| Alta sin proveedor (`errors.supplierId`) | `Indique el proveedor.` | 400 |
| Proveedor dado de baja | `El proveedor está dado de baja; no admite órdenes de compra nuevas.` | 422 |
| Almacén omitido y la compañía tiene más de un almacén activo (`errors.warehousePublicId`) | `Indique el almacén: la compañía tiene más de uno.` | 400 |
| Almacén omitido y la compañía no tiene almacenes activos | `La compañía no tiene almacenes activos.` | 422 |
| Almacén de la orden dado de baja | `El almacén está dado de baja; no admite órdenes de compra nuevas.` | 422 |
| `PATCH` con `number`, `orderDate`, `currency`, `status` o `statusCode` | `El campo number de la orden de compra no se puede cambiar.` (con el nombre del campo enviado) | 400 |
| `PATCH` con un `supplierId` o `warehousePublicId` **distinto** del actual en una orden que no está en `DRAFT` (ajuste del 2026-09-30; se revisa antes que la capacidad `EDIT_PURCHASE_ORDER`) | `El proveedor y el almacén solo se cambian mientras la orden de compra está en borrador.` | 409 |
| `PATCH` en `DRAFT` con un proveedor dado de baja / un almacén dado de baja | `El proveedor está dado de baja; no admite órdenes de compra nuevas.` / `El almacén está dado de baja; no admite órdenes de compra nuevas.` | 422 |
| `PATCH` con un `supplierId` o `warehousePublicId` de otra compañía o inexistente | `Proveedor no encontrado.` / `Almacén no encontrado.` | 404 |
| Producto de un cliente en la orden | `La orden de compra solo admite productos propios; {sku} pertenece a un cliente.` | 400 |
| Producto repetido en la orden | `El producto {sku} está repetido en la orden de compra.` | 400 |
| Producto inactivo en la orden | `El producto {sku} está inactivo; no admite órdenes de compra.` | 400 |
| `qtyOrdered` ≤ 0 / > 3 decimales | `La cantidad ordenada debe ser mayor que cero.` / `La cantidad admite como máximo 3 decimales.` | 400 |
| Producto con serie: cantidad no entera | `La cantidad de {sku} debe ser entera: el producto se controla por serie.` | 400 |
| Costo negativo / > 4 decimales | `El costo unitario no puede ser negativo.` / `El costo unitario admite como máximo 4 decimales.` | 400 |
| Costo omitido y sin costo de compra por defecto | `Indique el costo unitario de {sku}.` | 400 |
| Editar fuera de `DRAFT` sin la capacidad habilitada | `El estatus actual no permite la acción 'EDIT_PURCHASE_ORDER'.` | 422 |
| Línea con recepciones: se elimina, baja de lo recibido o cambia de costo | `La línea de {sku} ya tiene recepciones: no se elimina, no baja de lo recibido ({x}) y su costo no cambia.` | 400/409 |
| Línea sin recepciones pero ya usada en un aviso de llegada | `La línea de {sku} ya se usó en un aviso de llegada; no se elimina.` | 400/409 |
| Enviar una orden que no está en `DRAFT` | `Solo una orden de compra en borrador se envía.` | 422 |
| Cancelar una orden `RECEIVED` | `Una orden de compra recibida completa no se cancela.` | 422 |
| Cancelar una orden ya cancelada | `La orden de compra ya está cancelada.` | 422 |
| Cancelar con un recibo abierto | `La orden de compra tiene un recibo abierto; confírmelo o elimínelo antes de cancelar.` | 409 |
| Eliminar con recepciones | `Una orden de compra con recepciones no se elimina; cancélela.` | 409 |
| Eliminar una `CANCELLED` con recepciones | `Una orden de compra cancelada con recepciones se conserva con su bitácora; no se elimina.` | 409 |
| Eliminar una `RECEIVED` | `Una orden de compra recibida completa se conserva con su bitácora; no se elimina.` | 409 |
| Eliminar con un recibo abierto | `La orden de compra tiene un recibo abierto; elimínelo antes de eliminar la orden de compra.` | 409 |
| Faltante: acción desconocida | `Acción desconocida: use CLOSE, REORDER o MANUAL_ADJUSTMENT.` | 400 |
| Faltante: sin pendiente | `La línea ya no tiene faltante pendiente.` | 409 |
| `CLOSE`/`REORDER` con cantidad distinta del pendiente | `Cerrar y Reordenar resuelven el faltante completo ({pendiente}); para una parte use el ajuste manual.` | 400 |
| `MANUAL_ADJUSTMENT` sin cantidad / ≤ 0 | `Indique la cantidad del ajuste.` / `La cantidad del ajuste debe ser mayor que cero.` | 400 |
| `MANUAL_ADJUSTMENT` por encima del pendiente | `La cantidad del ajuste excede el faltante pendiente ({pendiente}).` | 400 |
| `MANUAL_ADJUSTMENT` sin nota (o solo espacios) | `Escriba una nota que explique el ajuste.` (en `errors.notes`) | 400 |
| Resolver faltante de una PO cancelada | `La orden de compra está cancelada; su faltante ya no se resuelve.` | 422 |
| Resolver faltante sin recepciones confirmadas | `La orden de compra todavía no tiene recepciones confirmadas.` | 422 |
| Orden de compra de otro tenant o inexistente | `Orden de compra` (404 genérico) | 404 |

**Proveedor y almacén de una orden (ajuste del 2026-09-30; antes, en el Lote 12, no cambiaban nunca).** Se eligen al crear
la orden y **se pueden cambiar mientras está en Borrador (`DRAFT`)** con el `PATCH`: `supplierId` y `warehousePublicId` son
opcionales (ausentes o `null` = sin cambio) y se validan igual que en el alta (proveedor y almacén activos de su compañía). Un
valor **igual** al actual no cuenta como cambio: se acepta en cualquier estatus y no se vuelve a validar (así una pantalla
puede mandar el formulario completo). Desde Enviada (`SENT`) quedan **fijos**: un valor distinto responde 409. Si la orden ya
se envió y el proveedor o el almacén están mal, cancélela y cree otra. En Borrador la orden todavía no tiene avisos de llegada
ni recibos (se crean al recibir, desde `SENT`), así que cambiar el almacén no afecta nada más: las líneas no dependen del
almacén. El cambio queda en la auditoría de la orden (`SupplierId` / `WarehouseId`), y respeta `rowVersion` como el resto del
`PATCH`. La máscara del teléfono del proveedor `(xxx)xxx-xxxx` es de la pantalla: el API guarda el texto que recibe (hasta 40
caracteres) y la pantalla lo manda ya con la máscara.

### Estatus y transiciones

`PurchaseOrderStatus`: **DRAFT** (inicial) → **SENT** → **PARTIAL** → **RECEIVED** (terminal); **CANCELLED**
(terminal, permitido desde `DRAFT`, `SENT` **y** `PARTIAL` — sin regla lateral sembrada en el seed, así que el
motor de estatus lo permite desde cualquiera de esas tres, con el comentario en el historial). `RECEIVED` es
terminal: no se cancela. Recibir contra la orden avanza `SENT/PARTIAL → PARTIAL` (si queda pendiente) o `→
RECEIVED` (si no queda ninguna). Resolver el último faltante de una `PARTIAL` también la pasa a `RECEIVED`.

La **edición** (fechas, notas, líneas) no es una regla fija: la controla la capacidad `EDIT_PURCHASE_ORDER`
(`StatusCapability`), negada por defecto en `SENT`, `PARTIAL`, `RECEIVED` y `CANCELLED`. El tenant puede
habilitarla en `SENT`/`PARTIAL` desde `/api/v1/status/capabilities/PURCHASE_ORDER`; aun así, una línea con
recepciones sigue protegida (no se elimina, no baja de lo recibido, no cambia de costo).

---

## 9. Cruce de muelle (demo): citas y planes

Qué hace: agenda citas de muelle (llegada/salida) y arma planes de cruce de muelle que asignan líneas de recibos
**abiertos** (se reparten al confirmar el recibo, con el faltante outbound visible por asignación) o **confirmados**
(reduce directamente el putaway pendiente). Lo asignado en la zona de staging queda **reservado** en el ledger
(no lo toma ninguna recolección, putaway ni transferencia) hasta que se mueve o se cancela.

Este módulo es una **demostración funcional**: viene **apagado** por defecto para el tenant.

Quién puede: `inventory.view` (listar y consultar); `warehouse.crossdock` (agendar/reprogramar/cambiar estatus de
citas; crear plan, asignar, cancelar asignación, mover, completar). Módulo **CROSSDOCK** (apagado por defecto).

Cómo se usa:
- `GET /api/v1/dock-appointments?warehousePublicId=&dockId=&fromUtc=&toUtc=&status=`.
- `POST /api/v1/dock-appointments` — `{ "warehousePublicId": "...", "dockId": 1, "direction": "INBOUND",
  "scheduledStartUtc": "...", "asnId": 3 }` (o `tripPublicId`, nunca ambos).
- `PATCH /api/v1/dock-appointments/{id}`, `POST .../{id}/status`.
- `GET/POST /api/v1/cross-dock-plans`, `GET .../{id}/candidates`.
- `POST /api/v1/cross-dock-plans/{id}/allocations` — `{ "receiptLineId": 10, "orderPublicId": "...", "quantity": 4 }`.
- `DELETE /api/v1/cross-dock-plans/{id}/allocations/{allocationId}?comment=`.
- `POST /api/v1/cross-dock-plans/{id}/allocations/{allocationId}/move`.
- `POST /api/v1/cross-dock-plans/{id}/complete`.
- El movimiento también se dispara desde la cola: `POST /api/v1/warehouse-tasks/{id}/complete` sobre la tarea
  `CROSSDOCK` (exige que la cantidad completada sea exactamente la confirmada).

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Módulo apagado | `El módulo 'CROSSDOCK' no está habilitado para esta compañía.` | 403 |
| Solapamiento en el muelle | `El muelle ya tiene una cita que se solapa con ese horario.` | 409 |
| Fin no posterior al inicio | `El fin de la cita debe ser posterior a su inicio.` | 400 |
| Fuera del horizonte (ayer a +90 días) | `La cita debe agendarse entre ayer y los próximos 90 días.` | 400 |
| Dirección incompatible con el tipo de muelle | `El muelle es de tipo {tipo}; no admite citas de {dirección}.` | 400 |
| Aviso de llegada y viaje a la vez | `Una cita se enlaza a un aviso de llegada o a un viaje, no a ambos.` | 400 |
| Aviso de llegada con dirección de salida | `Una cita de un aviso de llegada debe ser de entrada (INBOUND).` | 400 |
| Reprogramar una cita que no está agendada | `La cita solo se reprograma mientras está agendada.` | 422 |
| Cantidad de asignación ≤ 0 / > 3 decimales / no entera con serie | `La cantidad debe ser mayor que cero.` / `La cantidad admite como máximo 3 decimales.` / `En productos con serie la cantidad debe ser entera.` | 400 |
| Asignación por encima de lo disponible | `La cantidad excede lo disponible para cruce de muelle ({disponible}).` | 409 |
| Orden no apta para asignación (cancelada, entregada, de baja) | `La orden no admite asignaciones (cancelada, entregada o dada de baja).` | 400 |
| Zona de staging fuera de tipo CROSSDOCK/STAGING | `La zona de staging del plan debe ser de tipo CROSSDOCK o STAGING.` | 400 |
| Mover con el recibo todavía abierto | `La recepción de la línea todavía no se confirma; la mercancía se mueve después de confirmar.` | 422 |
| Mover una asignación sin nada confirmado | `La asignación no recibió mercancía al confirmar; cancélela.` | 422 |
| Completar la tarea `CROSSDOCK` con una cantidad distinta de la confirmada | `La tarea de cruce de muelle se completa por la cantidad confirmada ({qty}).` | 400 |
| Completar el plan con asignaciones pendientes | `Mueva o cancele las asignaciones pendientes antes de completar el plan.` | 422 |
| Plan ya completado | `El plan ya fue completado.` | 422 |
| Muelle inactivo | `El muelle está inactivo; reactívelo o elija otro.` | 422 |
| Cita/plan/asignación de otro tenant o inexistente | `Cita no encontrada.` / `Plan de cruce de muelle no encontrado.` / `Asignación no encontrada.` | 404 |

### Estatus y transiciones

`AppointmentStatus`: **SCHEDULED** (inicial) → **ARRIVED** → **COMPLETED** (terminal); **NO_SHOW** (lateral,
motivo en el comentario del historial) y **CANCELLED** (terminal) desde `SCHEDULED`. Efecto
`DockAppointmentStatusEffect`: `ARRIVED` ocupa el muelle (`FREE→OCCUPIED`); `COMPLETED`/`NO_SHOW`/`CANCELLED` lo
liberan (`OCCUPIED→FREE`) si no queda otra cita `ARRIVED` en él.

`CrossDockStatus`: **OPEN** (inicial) → **ALLOCATED** (con la primera asignación) → **COMPLETED** (terminal, solo
si ninguna asignación sigue `PLANNED`).

`AllocationStatus`: **PLANNED** (inicial) → **MOVED** (terminal, tras el `CROSSDOCK` que saca la mercancía del
inventario) o **CANCELLED** (terminal, libera la reserva; si ya tenía algo confirmado, ese remanente vuelve a
`PUTAWAY`).

---

## 10. Permisos y módulos (resumen)

| Permiso | Categoría | Qué habilita |
|---|---|---|
| `inventory.view` | WAREHOUSE | Ver almacenes, productos, inventario, recibos, tareas, conteos, recolecciones, citas y planes de cruce de muelle |
| `inventory.manage` | WAREHOUSE | Gestionar productos y categorías |
| `inventory.adjust` | WAREHOUSE | Ajustar/transferir inventario, conciliar, resolver faltantes de compra |
| `warehouse.manage` | WAREHOUSE | Gestionar almacenes, zonas, posiciones, muelles; asignar/cancelar tareas |
| `warehouse.receive` | WAREHOUSE | Recibir mercancía (ASN y recibos); completar tareas `PUTAWAY` |
| `warehouse.pick` | WAREHOUSE | Recolectar y empacar; completar tareas `REPLENISH`; correr el reabasto |
| `warehouse.count` | WAREHOUSE | Conteo cíclico completo, incluida la reconciliación |
| `warehouse.count.capture` | WAREHOUSE | Lote 8A: contar a ciegas (alta, captura, lo encontrado y terminar) sin ver lo esperado ni reconciliar; implícito en `warehouse.count` |
| `warehouse.crossdock` | WAREHOUSE | Citas y planes de cruce de muelle; completar tareas `CROSSDOCK` |
| `purchasing.view` | PURCHASING | Ver proveedores y órdenes de compra |
| `purchasing.manage` | PURCHASING | Gestionar proveedores y órdenes de compra |
| `purchasing.receive` | PURCHASING | Recibir mercancía contra una orden de compra (existía desde antes de este lote) |

Plantillas de rol: `inventory.view` lo reciben además el Operador de almacén, Solo lectura y Facturación; los
otros tres permisos `WAREHOUSE` nuevos (`inventory.manage`, `inventory.adjust`, `warehouse.manage`) solo los tiene
el administrador del tenant por defecto.

Módulos: **WMS_LOTSERIAL** y **PURCHASING** vienen encendidos por defecto; **CROSSDOCK** apagado (demo).

Lote 12: `GET /api/v1/products/brands` pide `inventory.view` (como la lista de productos) y `POST
/api/v1/warehouses/{publicId}/bins/capacity` pide `warehouse.manage` (como editar una posición). No hay permisos nuevos.

Excepción (Lote 11): `GET /api/v1/postal-localities` (catálogo de ciudades y códigos postales) no pide permiso ni módulo, solo una
sesión iniciada. La pantalla solo lo consulta cuando alguien con `warehouse.manage` abre el selector de ciudad al crear o
editar un almacén.
