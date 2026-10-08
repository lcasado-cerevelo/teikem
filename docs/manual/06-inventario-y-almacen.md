# Capítulo 06 — Inventario y almacén (Lote 6; Almacenes y ubicaciones ampliado en el Lote 11; Productos y Compras ampliados en el Lote 12; Recibo, Tareas y Recolección y empaque ampliados en el Lote 13; Inventario y Conteo cíclico ampliados en el Lote 14; Almacenes, Recibo y Tareas ampliados en el Lote 16: recibo directo a posición; Conteo cíclico ampliado en el Lote 21: conteo por producto, corrección, vista previa, cierre en bloque y posiciones provisionales; Almacenes y ubicaciones ampliado en el Lote 23: hojas de posición; Productos ampliado en el Lote 26: convertir a serie)

Este capítulo describe Almacenes y ubicaciones, Productos y categorías, Inventario (saldos, Kárdex con resumen y detalle, ajustes,
transferencias, conciliación automática con descuadres, genealogía y rastro de serie), Recepción (avisos de llegada y recibos, incluida la
recepción contra una orden de compra), la cola de Tareas de almacén (putaway dirigido y reabasto), Conteo cíclico,
Recolección y empaque ad hoc, Compras mínimas (proveedores, órdenes de compra y faltantes) y Cruce de muelle (citas
y planes, en modo demo). Cada sección indica **qué hace**, **quién puede**, **cómo se usa**, las **validaciones**
con el mensaje exacto y el código HTTP, y los **estatus** con sus transiciones y efectos. Los mensajes están
verificados contra el código (`src/Teikem.Domain/Wms/*.cs`, `src/Teikem.Infrastructure/Wms/*.cs`,
`src/Teikem.Domain/Catalogs/PostalLocality.cs`, `src/Teikem.Infrastructure/Services/PostalLocalityService.cs`,
`src/Teikem.Infrastructure/Wms/{InventoryLedger,InventoryReconciler,InventoryReconciliationWorker}.cs`,
`src/Teikem.Api/Controllers/PostalLocalitiesController.cs`,
`src/Teikem.Infrastructure/Services/{Warehouse,WarehouseLayout,Product,ProductCategory,InventoryRead,
InventoryAdjustment,Traceability,InventoryReconciliation,Asn,Receipt,WarehouseTask,Replenishment,CycleCount,PickBatch,Supplier,
PurchaseOrder,PurchaseOrderReceiving,PurchaseShortage,DockAppointment,CrossDock}Service.cs`,
`src/Teikem.Infrastructure/Services/{PutawayTaskHandler,ReplenishTaskHandler,CountTaskHandler,CrossDockTaskHandler,
CrossDockReceiptParticipant,WarehouseTaskStatusEffect,DockAppointmentStatusEffect}.cs`,
`src/Teikem.Api/Controllers/{Warehouses,Products,ProductCategories,Inventory,InventoryDiscrepancies,Receipts,Asns,WarehouseTasks,
Attention,CycleCounts,PickBatches,Suppliers,PurchaseOrders,DockAppointments,CrossDockPlans}Controller.cs`). Las preguntas y
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
- **Formato de los números (Lote 16).** En la pantalla, las cantidades y el dinero llevan **coma de miles y punto decimal** (formato de Puerto
  Rico): `61,023` y `1,250.5`; el dinero lleva `$` (`$1,234.50`). Antes, en español, una cifra como `61.023` se leía como un decimal y las de 4 dígitos
  no llevaban separador. Es solo de presentación: el API sigue devolviendo números JSON (`61023`, `1250.5`) y los acepta igual. Las exportaciones a
  Excel conservan el número como número.

---

## 1. Almacenes y ubicaciones

Qué hace: mantiene el almacén y su jerarquía física — zonas tipadas, posiciones (pasillo-rack-nivel-posición) y
muelles. El almacén nace **ACTIVE** y su baja es **definitiva** (estatus terminal): solo procede vacío y sin
documentos abiertos. Con un único almacén activo, las demás pantallas lo usan por defecto; con más de uno, hay que
indicarlo. Desde el Lote 11 (cambios de Almacén, tanda 1) además: cada posición puede tener un **cupo máximo** en
unidades y, con él, un estado de **ocupación**; cada zona muestra su ocupación (calculada, no guardada); el **código de
una zona se puede editar**; el listado de posiciones es **paginado** y se busca por código, zona, pasillo, rack, nivel o
posición; y hay un **catálogo de localidades postales** para llenar la ciudad, el estado, el código postal y el país
del almacén. Desde el Lote 16, cada almacén tiene además un **modo de recepción** (con acomodo o directo a posición) y una **posición de recepción
por defecto** (sección 1.4); el recibo directo se describe en la sección 4.1.

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

**Posición provisional (Lote 21).** Una posición creada desde un conteo por quien cuenta (`POST /api/v1/cycle-counts/{id}/bins`, ver el
capítulo de Conteo cíclico) nace con `isProvisional = true` (y `provisionalCycleCountId`, `provisionalCreatedAtUtc`): se usa ya, pero queda
visible para revisión. El listado acepta `?isProvisional=true|false`; el supervisor la confirma con
`POST .../bins/{binId}/confirm-provisional` (`warehouse.manage`; si no es provisional, 409 `La posición no está pendiente de revisión.`),
la edita con `PATCH` o la desactiva. La base lo guarda en `WarehouseBin.IsProvisional`, `ProvisionalCreatedBy`, `ProvisionalCreatedAtUtc` y
`ProvisionalCycleCountId`.

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

### 1.4 Modo de recepción y posición de recepción por defecto (Lote 16)

Qué hace: decide **adónde entra la mercancía** cuando se confirma un recibo del almacén.

| Código | Etiqueta | Dónde entra la mercancía al confirmar | Tareas de acomodo |
|---|---|---|---|
| `PUTAWAY` | Con acomodo | En la **posición de recepción** (zona `STAGING`) | Una por línea; se acomoda después |
| `DIRECT` | Directo a posición | En la **posición destino de cada línea** | Ninguna |

El modo por defecto es `PUTAWAY` (un almacén sin valor se trata así). **Advance Solutions** (`ALM-SOL`), que solo tiene la posición `GENERAL` y
ninguna zona de recepción, se migró en `DIRECT`; **Advance Depot** (`ALM-DEPOT`) y la demo (`ALM-01`), en `PUTAWAY`. El modo del almacén es el de los
recibos **nuevos**: cada recibo guarda el modo con que se abrió (sección 4.1). **Cambiarlo no toca los recibos abiertos ni los acomodos pendientes**: los
recibos que ya estaban abiertos siguen como se abrieron y las tareas de acomodo siguen su curso.

**Posición de recepción por defecto.** Una posición de una zona `STAGING` o `CROSSDOCK` del almacén. La usan los recibos **con acomodo** (cuando el
encabezado no indica otra) y las líneas con cruce de muelle de un recibo directo (sección 4.1). Si el almacén no tiene una, o la que tiene dejó de ser válida
(se dio de baja la posición o su zona), vale **la primera posición activa de una zona `STAGING`**, ordenando por código de zona y de posición. Se fija
después de crear el almacén (al crearlo aún no hay posiciones) y se puede quitar. En Advance Depot es `R1` (zona `STG`), no la primera por código de zona
(`S1` de "Embarque", que era lo que elegía el sistema antes del Lote 16).

Quién puede: `warehouse.manage` (fijar el modo y la posición por defecto); `inventory.view` (leerlos). Módulo **WMS_LOTSERIAL**.

Cómo se usa:
- Alta: `POST /api/v1/warehouses` con `"receivingMode": "DIRECT"`. Sin `receivingMode` queda `PUTAWAY`.
- Edición: `PATCH /api/v1/warehouses/{publicId}` con `{ "receivingMode": "DIRECT", "defaultReceivingBinId": 12, "rowVersion": "…" }`. `receivingMode`
  ausente o `null` = sin cambio (vale `PUTAWAY` o `DIRECT`, sin distinguir mayúsculas). `"clearDefaultReceivingBin": true` quita la posición por defecto.
- Lectura: `GET /api/v1/warehouses` y `GET /api/v1/warehouses/{publicId}` traen `receivingModeCode` (`PUTAWAY` o `DIRECT`), `receivingMode` (la etiqueta
  en el idioma del usuario), `defaultReceivingBinId` y `defaultReceivingBinCode` (`null` = la primera `STAGING`).
- Aparatos: el registro y el heartbeat del aparato traen `defaultWarehouseReceivingMode` (el modo del almacén por defecto del aparato; capítulo 9, sección 4).
- Migración (`import-legacy`): las claves `warehouse.receivingMode` (`PUTAWAY` o `DIRECT`) y `warehouse.defaultReceivingBin` (código de una posición de recepción del
  almacén) **solo se aplican al crear el almacén**: `--update` no las pisa.
- Análisis: las fuentes de almacenes y de recibos exponen los campos "Modo de recepción" (`ReceivingMode`) y "Código de modo de recepción" (`ReceivingModeCode`).

Pantalla: ficha del almacén → **Datos** → sección **Recepción** ([F6 — Almacenes](frontend/f6-almacen-e-inventario.md#ficha-del-almacén)).

### 1.5 Productos por posición (informe)

Qué es: un listado, **una posición por página**, con los productos que hay en cada posición (SKU, nombre y código de barras), para imprimirlo y
**escanear los códigos desde el papel** (por ejemplo, en un rack de 20 a 30 pies donde el producto o su etiqueta no se alcanzan a leer). Es un
listado de lo que hay **en el momento de generarlo**: no guarda ningún estado (no registra cuándo se imprimió ni avisa cuando una lista cambia).
Reemplaza a las "hojas de posición" del Lote 23 (ver `docs/lote24-decisiones.md`), que se quitaron.

Quién puede: `inventory.view`. Módulo **WMS_LOTSERIAL**, como el resto de Almacenes y ubicaciones.

**Cómo se usa:**

- `GET /api/v1/warehouses/{publicId}/bin-products` acepta **los mismos filtros** que el listado de posiciones (`search`, `zoneId`, `zoneIds`,
  `aisle`, `rack`, `level`, `position`, `productPublicIds`, `occupancy`, `binIds`, `includeInactive`, `onlyWithStock`, `isProvisional`) y pagina con
  `skip` y `take` (por defecto 50, máximo **200**: más de 200 es 400). Ordena por código de posición. Responde
  `{ total, skip, take, generatedAtUtc, items }`; cada posición trae `binId`, `code`, `zoneId`, `zoneCode`, `aisle`, `rack`, `level`, `position`,
  `isActive` y `products`: un renglón **por producto** (los lotes no se repiten) con existencia en mano mayor que cero, ordenados por SKU, con
  `productPublicId`, `sku`, `name` y `barcode` (vacío si el producto no tiene). El informe no lleva cantidades. Una posición sin productos trae
  `products` vacío. `generatedAtUtc` es el instante en que se leyeron los datos.
- Para imprimir todo un pasillo: `GET .../bin-products?aisle=A&take=200` y, si `total` es mayor que 200, seguir con `skip=200`, `skip=400`…

Datos guardados: ninguno nuevo. Las columnas `WarehouseBin.SheetPrintedAtUtc` y `WarehouseBin.SheetContentChangedAtUtc` del Lote 23 se quitaron del
esquema y del modelo; una base que ya las tenga puede dejarlas (son nulables y nadie las lee) o quitarlas con `ALTER TABLE dbo.WarehouseBin DROP COLUMN
SheetPrintedAtUtc, SheetContentChangedAtUtc;`.

Casos frecuentes:
- *"Una posición no sale en el informe."* Si no tiene productos con existencia no sale (a menos que se pida **Incluir posiciones vacías** en la
  pantalla). Un producto cuyo total en la posición, sumando lotes, es 0 no cuenta.
- *"Moví mercancía y el papel quedó viejo."* El informe no avisa: vuelva a generarlo cuando lo necesite.

Pantalla: Almacén → **Posiciones** — botón **Productos por posición**, casillas y filtro **Pasillo**
([F15 — Productos por posición](frontend/f15-productos-por-posicion.md)).

### 1.6 Orden de salida del inventario (2026-10-05)

`GET /api/v1/inventory/exit-options?warehousePublicId=…&productPublicIds=…&skip=&take=` (`inventory.view`, módulo WMS_LOTSERIAL) devuelve, **por producto**, de dónde debe salir en el MISMO orden en que lo asigna la recolección
(`PickBatchRules.Eligible`, FEFO D14: vence primero —los sin vencimiento al final—, luego tipo de zona picking < reserva < refrigerada < preparación y código de posición). Cada fila: `productPublicId`, `binId`, `binCode`, `zoneCode`,
`zoneTypeCode`, `lotId`, `lotNumber`, `expiryDate`, `available` (en mano − reservado) y `rank` (1 = sale primero, reinicia por producto). Solo posiciones **activas** y recolectables (sin cuarentena ni cruce de muelle) con disponible mayor que
cero, de productos activos. `warehousePublicId` es obligatorio (400 `Indique el almacén.`; 404 `Almacén no encontrado.` si no existe o es de otra compañía); `productPublicIds` vacío = todos; página `{ total, skip, take, serverTimeUtc, items }`,
`take` ≤ 500 (por defecto 500). **Es la única implementación de esa regla**: la app de almacén la baja al aparato y la consulta en línea; ninguna pantalla vuelve a ordenar.

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
| `receivingMode` desconocido (alta o `PATCH` de almacén; el error va en `errors.receivingMode`) | `Modo de recepción desconocido: 'X'. Use PUTAWAY o DIRECT.` | 400 |
| `defaultReceivingBinId` de otro almacén o inexistente | `Posición no encontrada.` | 404 |
| `defaultReceivingBinId` en una zona que no es `STAGING` ni `CROSSDOCK` (el error va en `errors.defaultReceivingBinId`) | `La posición de recepción debe estar en una zona STAGING o CROSSDOCK.` | 400 |
| `defaultReceivingBinId` de una posición desactivada | `La posición de recepción está desactivada.` | 422 |
| `take` mayor que 200 en `GET .../bin-products` (en `errors.take`) | `Se pueden pedir como máximo 200 posiciones por consulta; use skip para pedir las siguientes.` | 400 |
| Almacén de otra compañía o inexistente en `bin-products` | `Almacén no encontrado.` | 404 |

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
| `unavailable=true` (2026-10-05, tableta *No disponibles*) | Solo productos **activos** cuyo disponible (en mano − reservado de todas sus posiciones, o de los almacenes indicados; sin saldo = 0) **no es mayor que cero**. Se lee con `take=1` y `total` |
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
| Cambiar seguimiento, unidad base o dueño con movimientos (para pasar un producto sin seguimiento a **serie** use "Convertir a serie", sección 2.1) | `No se puede cambiar {el tipo de seguimiento / la unidad de medida base / el dueño} de un producto que ya tiene movimientos.` | 409 |
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

### 2.1 Convertir a serie (Lote 26, Rentas R0)

Qué hace: pasa un producto **sin seguimiento** (`NONE`) a seguimiento **por serie** (`SERIAL`) aunque ya tenga movimientos, dándole
un número de serie a **cada unidad en mano**. Es la **única** forma de cambiar el seguimiento de un producto con movimientos: la
edición normal (`PATCH`) lo sigue rechazando con 409 (regla D25). Pensado para los equipos de Advance Depot que llegaron de la
migración sin series y que se van a **rentar** (las rentas solo trabajan con equipos con serie).

Quién puede: hace falta **`inventory.manage`** (cambia el maestro del producto) **y `inventory.adjust`** (mueve inventario). Sin
`inventory.manage` el API responde 403 antes de empezar; con `inventory.manage` pero sin `inventory.adjust`, 403 `Falta el permiso
'inventory.adjust'.` (y queda un evento `PERMISSION_DENIED`). Módulo **WMS_LOTSERIAL**. La compañía sale de la sesión: un producto o una
posición de otra compañía dan 404.

Cómo se usa (servidor; en la web, el botón "Convertir a serie" de la ficha del producto, capítulo de pantallas [F17](frontend/f17-rentas.md) §2):
- `POST /api/v1/products/{publicId}/convert-to-serial`
  ```json
  { "positions": [
      { "binId": 12, "serialNumbers": ["SN-0001", "SN-0002"] },
      { "binId": 15, "serialNumbers": ["SN-0003"] } ],
    "notes": "Equipos de renta", "rowVersion": "AAAAAAAAB9E=" }
  ```
  Un renglón por **cada posición donde hay existencia** del producto, con **tantas series como unidades en mano** en esa posición
  (consulte las posiciones en `GET /api/v1/inventory/balances?productPublicIds=…`). `notes` es opcional (si no se escribe, los
  movimientos dicen "Conversión a serie"); `rowVersion` es opcional (el de la ficha: si otro usuario cambió el producto, 409). Si la
  misma posición viene en dos renglones, sus series se suman.
- Respuesta 200: `{ product, serialCount, movements: { transactions, balances } }` — la ficha del producto ya con `trackingTypeCode =
  "SERIAL"`, cuántas series se dieron de alta y los movimientos y saldos que tocó.
- Producto **sin existencia** (con o sin movimientos): se manda `{}` o `positions: []` y solo cambia el seguimiento.

Qué pasa por dentro (todo en **una sola transacción**: si algo falla no se escribe nada):
1. Se bloquea el producto y todos sus saldos (ningún otro movimiento del producto entra mientras se convierte).
2. Se verifica: el producto es sin seguimiento; **nada reservado**; sin documentos abiertos; cada posición con existencia tiene
   unidades enteras, sin lote, y el número de series capturadas es igual a sus unidades en mano.
3. Por cada posición, en el Kárdex: un **ajuste de salida** (`ADJUSTMENT −`) por todo el saldo sin serie y un **ajuste de entrada**
   (`ADJUSTMENT +1`) por cada serie, todos con el motivo de sistema **`TRACKING_CONVERSION` ("Conversión a serie")**. El neto es 0:
   **el en mano y el disponible de cada posición no cambian**. Cada serie nace `AVAILABLE` en su posición (historial de estatus de la
   serie: alta en Disponible).
4. El seguimiento del producto pasa a `SERIAL` (queda en la **auditoría** del producto: cambio de `TrackingTypeLookupId`).

Después de convertir, el producto se trabaja como cualquier producto con serie: ajustes, transferencias, recolecciones y recibos
piden las series. El motivo `TRACKING_CONVERSION` **no** se puede usar en un ajuste manual (400 `El motivo TRACKING_CONVERSION lo
asigna el sistema.`) y la pantalla de ajuste no lo ofrece.

#### Validaciones (convertir a serie)

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Una posición con existencia trae otro número de series que sus unidades en mano, o no viene en la solicitud (`{n}` = unidades en mano de la posición; `{m}` = series capturadas). Una posición **sin** existencia con series da el mismo mensaje con `{n}` = 0 | `Capture {n} número(s) de serie para {bin} (hay {m}).` | 400 |
| Renglón sin `binId` (`errors["positions[i].binId"]`) | `Indique la posición.` | 400 |
| Serie repetida en la solicitud (sin distinguir mayúsculas, aunque sea en otra posición) | `El número de serie {s} está repetido.` | 400 |
| Serie de más de 80 caracteres / más de 500 series en un renglón | `El número de serie {s} excede 80 caracteres.` / `Una línea admite como máximo 500 números de serie.` | 400 |
| `notes` de más de 300 caracteres | `Las notas admiten como máximo 300 caracteres.` | 400 |
| `rowVersion` que no es base64 | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 |
| Producto de otra compañía o inexistente | `Producto no encontrado.` | 404 |
| `binId` de otra compañía o inexistente | `Posición no encontrada.` | 404 |
| Sin `inventory.adjust` (con `inventory.manage`) | `Falta el permiso 'inventory.adjust'.` | 403 |
| Alguna posición del producto tiene unidades reservadas (recolección, cruce de muelle u otra reserva) | `El producto {sku} tiene unidades reservadas; libérelas antes de convertirlo.` | 409 |
| El producto está en un recibo abierto, una tarea pendiente o en curso, una recolección que aún se puede eliminar o un conteo abierto (el mismo criterio que la baja del producto) | `El producto {sku} tiene recibos, tareas, recolecciones o conteos abiertos; termínelos antes de convertirlo.` | 409 |
| Otro usuario cambió el producto (con `rowVersion`) | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Una serie capturada ya está en inventario / fue dada de baja (series que el producto ya tenía registradas) | `La serie {s} ya está en inventario.` / `La serie {s} fue dada de baja; no vuelve al inventario.` | 409 |
| El producto ya es por serie | `El producto {sku} ya se controla por serie.` | 422 |
| El producto es por lote | `Solo se convierten a serie productos sin seguimiento; {sku} se controla por lote.` | 422 |
| Una posición tiene existencia fraccionaria (por ejemplo 1.5) | `La existencia de {sku} en {bin} es {cantidad}; ajústela a unidades enteras antes de convertirlo.` | 422 |
| Existencia sin posición (solo en el almacén) o con lote | `La existencia de {sku} en {posición o almacén} no está en una posición sin lote; muévala o ajústela antes de convertirlo.` | 422 |
| Posición o almacén inactivos con existencia | `La posición {bin} está inactiva; no admite movimientos de inventario.` / `El almacén {código} está inactivo; no admite movimientos de inventario.` | 422 |

#### Estatus y efectos

No hay estatus propio: el producto conserva su `IsActive`. Cambia `TrackingType` de `NONE` a `SERIAL` (de una sola vía: un producto
por serie no vuelve a `NONE` si tiene movimientos). Cada serie nueva entra a `SerialStatus` en **Disponible** (`AVAILABLE`). Bloquea:
reservas, documentos abiertos y existencias no convertibles (tabla de arriba).

#### Casos frecuentes

- **Producto de Depot con 3 unidades en `01-A-24` y 1 en `02-B-10`**: capture 3 series para `01-A-24` y 1 para `02-B-10` en la misma
  solicitud. Si falta una, el API dice exactamente cuántas faltan en qué posición y no convierte nada.
- **Tiene unidades reservadas por una recolección**: empaque o elimine la recolección (o espere a que termine) y vuelva a intentar.
- **La existencia está mal** (no coincide con lo físico): primero haga el conteo cíclico o el ajuste, luego convierta.
- **Reportes**: los ajustes de la conversión aparecen en el Kárdex y en el Reporte de ajustes con el motivo "Conversión a serie"
  (salida y entradas por el mismo total, neto 0).

---

## 3. Inventario: saldos, Kárdex, ajustes, transferencias, conciliación y descuadres, genealogía y rastro de serie

Qué hace: expone el saldo por almacén/posición/lote, el Kárdex de solo lectura del ledger (con resumen, filtros y detalle de cada
movimiento), el ajuste manual con motivo de catálogo, la transferencia entre posiciones o almacenes, la genealogía de un lote, el rastro
de una serie y la **conciliación** Kárdex ↔ saldo, que ahora corre sola en segundo plano y guarda los **descuadres** que encuentra
(Lote 14). `InventoryLedger` es la única vía de escritura: **nadie** hace `UPDATE` sobre `InventoryTransaction` (una reversa siempre es
un movimiento nuevo) ni escribe `StockBalance` fuera de él.

La cantidad del ledger se guarda **con signo** (maestro L331: "cada despacho escribe movimiento negativo; cada
recepción, positivo"): `RECEIPT` entra (+), `ISSUE`/`CROSSDOCK` salen (−), `ADJUSTMENT` entra o sale según lo
capturado, `TRANSFER` es una sola fila con origen y destino. El disponible siempre es "en mano − reservado"
calculado en código (nunca la columna computada `QtyAvailable`).

Quién puede (módulo **WMS_LOTSERIAL**):

| Acción | Permiso |
|---|---|
| Saldos, Kárdex, resumen, detalle de un movimiento, dueños, búsqueda de posiciones, genealogía, rastro de serie y **ver** descuadres | `inventory.view` |
| Ajustar, transferir, "Ejecutar conciliación", estado de la revisión automática y **resolver** un descuadre (corregir o descartar) | `inventory.adjust` |
| "Crear conteo de esa posición" tras corregir un descuadre (es un conteo nuevo, sección 6) | `warehouse.count.capture` |
| Ver la sección "Necesita tu atención" del Pulso (los descuadres pendientes) | `pulse.attention` y `inventory.view` (capítulo 07, sección 4) |

**Día local.** Desde el Lote 14, los filtros de fecha `from` y `to` (día `AAAA-MM-DD`, `to` inclusivo) son **días de la compañía en hora de
Puerto Rico** (America/Puerto_Rico, UTC−4), no días UTC. Aplica al Kárdex, a su resumen, a los descuadres y a la lista de conteos.

### 3.1 Kárdex: filtros, resumen y detalle del movimiento

**Tipo «Acomodo» (2026-10-07).** Un movimiento que viene de una **tarea de acomodo** (PUTAWAY) se ve en el Kárdex como **«Acomodo»** (`typeCode = PUTAWAY`), no como «Transferencia»: así no se confunde con un traslado entre almacenes. Solo cambia lo que se ve: en el ledger el movimiento sigue siendo una TRANSFER con referencia a la tarea (`refEntity = WAREHOUSE_TASK`, «Tarea #n»), así que los saldos, la conciliación y la trazabilidad no cambian. El filtro **Tipo** ofrece «Acomodo»; **«Transferencia»** trae solo las transferencias que no son acomodos (las de Transferencias y ajustes, conteo, rentas…). Las transferencias de una tarea de reabasto siguen como Transferencia.

Cómo se usa:
- `GET /api/v1/inventory/balances?warehousePublicIds=&binIds=&productPublicIds=&categoryIds=&lotNumber=&
  includeZero=&onlyAvailable=&search=&skip=&take=` (`take` ≤ 200). La respuesta trae, además de las filas, `totalOnHand` y
  `totalAvailable` (las cifras "En mano" y "Disponible" del resumen de Saldos).
- `GET /api/v1/inventory/transactions?from=&to=&types=&warehousePublicIds=&binIds=&productPublicIds=&
  categoryIds=&lotNumber=&serialNumber=&refEntity=&refId=&search=&brands=&name=&ownerClientPublicIds=&includeOwn=&reasons=&
  direction=&fromWarehousePublicIds=&toWarehousePublicIds=&manualOnly=&skip=&take=` (más recientes primero).
- `GET /api/v1/inventory/transactions/summary?…` — **los mismos filtros** que la lista (sin `skip` ni `take`).
- `GET /api/v1/inventory/transactions/{id}` — detalle de un movimiento.
- `GET /api/v1/inventory/owners` — dueños para el filtro.
- `GET /api/v1/warehouses/bins/search?search=&warehousePublicIds=&includeInactive=&take=` — posiciones de todos los almacenes (o de los
  indicados) para el filtro Posición: `search` busca en el código de la posición o de su zona; `take` ≤ 50 (20 por defecto).

Filtros de la lista (todos se combinan con "y"; los de varios valores, con "o"):

| Filtro | Qué hace |
|---|---|
| `from`, `to` | Días locales de Puerto Rico, `to` inclusivo |
| `types` | Tipo de movimiento (`RECEIPT`, `ISSUE`, `TRANSFER`, `ADJUSTMENT`, `CROSSDOCK`) |
| `warehousePublicIds`, `binIds` | Movimientos que tocan esos almacenes o posiciones (de cualquiera de los dos lados) |
| `productPublicIds`, `categoryIds` | Producto o categoría (con sus subcategorías) |
| `lotNumber`, `serialNumber` | Lote o serie |
| `refEntity` + `refId` | Movimientos de un documento de origen (por ejemplo `CYCLE_COUNT` y el id del conteo) |
| `brands`, `name` | Marca del producto y texto del nombre (Lote 12) |
| `ownerClientPublicIds`, `includeOwn` | Dueño del producto: uno o varios clientes; `includeOwn=true` agrega los productos propios ("Propio") |
| `reasons` | Motivo del ajuste (uno o varios) |
| `direction` | `IN` (entradas) u `OUT` (salidas), con la misma perspectiva que `signedQuantity` |
| `fromWarehousePublicIds`, `toWarehousePublicIds` | Almacén de origen y de destino: cada uno se compara contra su lado del movimiento |
| `manualOnly` | Solo movimientos sin documento de referencia (hechos a mano) |
| `search` | Texto libre |

La fila del Kárdex trae `quantity` (la del ledger, con signo), `signedQuantity` (la misma cantidad desde la perspectiva del filtro),
`ownerName` (el cliente dueño o "Propio") y `categoryName`, además de posición de → a, lote, serie, motivo, nota, usuario y origen.

**Resumen.** Cuenta, con los mismos filtros, los **movimientos**, las **entradas** y **salidas** (cuántos movimientos y cuántas unidades)
y los **internos**. Sin filtro de ubicación, una transferencia es interna (no entra ni sale); con filtro de almacén o posición, es
entrada si llega desde fuera del filtro, salida si sale, e interna si los dos lados están dentro. Sin filtro de ubicación, `IN` es
cantidad mayor que cero que no sea transferencia, y `OUT`, cantidad menor que cero.

```
GET /api/v1/inventory/transactions/summary?from=2026-09-30&to=2026-09-30&productPublicIds=…
→ { "movements": 3, "inCount": 1, "inQty": 10, "outCount": 1, "outQty": 2, "internalCount": 1 }
```

**Detalle de un movimiento.** Devuelve la fila completa con dueño, categoría y vencimiento del lote; el **documento de origen** con su
número, estatus, fecha y parte (cliente o proveedor) y, para abrirlo, su `publicId` (recibo, recolección, orden de compra, orden de
transporte y producto) o su id (conteo); y los **movimientos relacionados**: los del mismo documento o, si el movimiento no tiene
documento, los del mismo asiento (mismo instante, usuario y producto). Tope de 200 (`relatedTruncated`). Documentos que resuelve:
recibo (`RECEIPT`), recolección (`PICK_BATCH`), conteo (`CYCLE_COUNT`), orden de compra (`PURCHASE_ORDER`), orden de transporte
(`TRANSPORT_ORDER`), tarea de almacén (`WAREHOUSE_TASK`, con su documento padre), producto (`PRODUCT`, el reabasto) y cruce de muelle.

```
GET /api/v1/inventory/transactions/1658
→ { "transaction": { "id": 1658, "typeCode": "TRANSFER", "quantity": 3, … },
    "ownerName": "Propio", "categoryName": null, "lotExpiryDate": null,
    "document": null, "related": [ { "id": 1658, … } ], "relatedTruncated": false }
```

### 3.2 Ajustes y transferencias

Cómo se usa:
- `POST /api/v1/inventory/adjustments` — `{ "productPublicId": "...", "warehousePublicId": "...", "binId": 5,
  "quantity": -2, "reason": "DAMAGE", "notes": "Caja aplastada en el muelle" }` (positivo entra, negativo sale).
  **La nota es obligatoria** (ajuste del 2026-09-30): el API exige `notes` no vacía (después de quitar espacios), de hasta
  300 caracteres, para todo motivo que captura una persona (`DAMAGE`, `LOSS`, `FOUND`, `EXPIRED`, `PO_SHORTAGE`, `OTHER` y
  cualquier motivo que agregue la compañía). Aplica a las pantallas, a la app móvil y a las integraciones. **No la exigen**
  los ajustes que escribe el sistema, porque no pasan por este endpoint: la diferencia de un recibo (`RECEIPT_VARIANCE`), la
  de un conteo (`COUNT_VARIANCE`), la reversa al eliminar una recolección (`PICK_BATCH_REVERSAL`) y el saldo inicial de la
  migración (`OPENING_BALANCE`) y la conversión a serie (`TRACKING_CONVERSION`, Lote 26, sección 2.1). Tampoco la exigen la transferencia ni el "ajuste manual" con que se resuelve un faltante de
  compra (sección 8): ahí la nota sigue siendo opcional, porque el movimiento queda ligado a la orden y a su línea.
- `POST /api/v1/inventory/transfers` — `{ "productPublicId": "...", "fromBinId": 5, "toBinId": 8, "quantity": 3 }`
  (entre almacenes: agrega `fromWarehousePublicId`/`toWarehousePublicId`; con lote o serie: `lotId` y `serialNumbers`).

**Cómo captura la pantalla (Lote 14, D11).** El API no cambió: sigue recibiendo la cantidad con signo. La pantalla pide **Tipo de ajuste:
Subir o Bajar** (obligatorio) y una **cantidad positiva**, y pone el signo. Los **motivos dependen de la dirección** (regla **solo de la
pantalla**; el API acepta cualquier motivo que no sea de sistema con cualquiera de los dos signos):

| Motivo | Al subir | Al bajar |
|---|---|---|
| Encontrado (`FOUND`) | Sí | No |
| Daño (`DAMAGE`), Pérdida (`LOSS`), Vencido (`EXPIRED`) | No | Sí |
| Los demás motivos de catálogo que no son de sistema (por ejemplo `PO_SHORTAGE`, `OTHER`) | Sí | Sí |
| `RECEIPT_VARIANCE`, `COUNT_VARIANCE`, `PICK_BATCH_REVERSAL`, `OPENING_BALANCE`, `TRACKING_CONVERSION` (Lote 26) | Nunca (los asigna el sistema) | Nunca |

Al cambiar de dirección, un motivo que ya no vale se quita. El motivo se elige con un buscador. La pantalla muestra **"Disponible en la
posición: N"** y, al bajar, **no deja bajar más de lo disponible**. Un producto **por lote** pide el número de lote al subir y elige un
lote de los que hay en la posición al bajar; uno **por serie** pide las series al subir y elige las series que salen al bajar. El mismo
modal se usa en Transferencias y ajustes, en el Kárdex y en la ficha del producto ("Añadir ajuste").

La **transferencia** se captura en este orden: almacén y posición de **origen** → **ítem** (producto y lote, de lo que hay en esa
posición) → **series** (si el producto las lleva; la cantidad es el número de series) → almacén y posición de **destino** (por defecto,
el almacén de origen) → cantidad (con tope en lo disponible) → nota (opcional). Antes la pantalla no permitía transferir productos con
lote o con serie; ahora sí.

Una transferencia es una sola fila `TRANSFER` con origen y destino; un ajuste es una fila `ADJUSTMENT`. Los dos aparecen en el Kárdex
al instante y disparan la revisión automática de su producto (3.3).

### 3.3 Conciliación automática y descuadres

**Qué es un descuadre.** El Kárdex (la lista de movimientos) y el saldo (`StockBalance`) se escriben juntos en cada movimiento, así que
deben coincidir. Un **descuadre** es que, para una clave (producto, almacén, posición, lote), el saldo no es igual a lo que suman los
movimientos; o, en el **total del producto**, que la suma de sus saldos no es igual a la de sus movimientos sin contar transferencias.
**No es una diferencia física en el estante** (eso lo resuelve un conteo cíclico). En la operación normal casi nunca ocurre; aparece
tras un arreglo hecho directamente en la base de datos, una restauración, la migración o un error del sistema. **Un ajuste o un conteo
no lo arreglan**, porque mueven el Kárdex y el saldo en la misma cantidad: el descuadre se corrige llevando el saldo a lo que da el
Kárdex (el Kárdex manda) o se descarta con una nota.

**Cuándo se revisa.**
- **Sola, en segundo plano** (origen `EVENT`, "Automática (movimiento)"): unos 1,5 segundos después de cada movimiento del Kárdex, el
  sistema revisa el producto tocado (varios movimientos seguidos se agrupan; un producto revisado hace menos de 10 segundos espera).
  Nadie espera por ella. Si el servidor se reinicia, una revisión que estaba pendiente **se pierde**; la cubren el siguiente movimiento
  de ese producto y el botón "Ejecutar conciliación".
- **A pedido** (origen `MANUAL`): `POST /api/v1/inventory/reconciliation/run`, con `{ "productPublicIds": ["…"] }` (hasta 200 productos)
  o sin cuerpo para revisar **todos** los de la compañía. Devuelve `{ checkedAtUtc, productsChecked, balancesChecked, opened,
  stillOpen, selfCorrected, mismatches }`: cuántos abrió, cuántos siguen pendientes y cuántos se cerraron solos.
- **Al migrar** (origen `MIGRATION`): `import-legacy` concilia al final. Las órdenes de consola (`db-init`, `import-legacy`) no arrancan la
  revisión en segundo plano.
- El origen `SCHEDULED` ("Programada") ya existe en el catálogo, pero no hay barrido programado todavía.
- `GET /api/v1/inventory/reconciliation/status` (`inventory.adjust`) dice si la revisión automática está encendida, si está consumiendo,
  cuántos productos esperan (`pending`; 0 = al día), cuántos se han revisado (`processed`), cuántos avisos se descartaron por cola llena
  (`dropped`), cuántas revisiones fallaron (`failed`), la hora de la última (`lastProcessedAtUtc`) y el último error (`lastError`). Los
  contadores son por compañía y viven en memoria desde que arrancó el servidor. La configuración está en `Inventory:Reconciliation`
  (`Enabled`, `DebounceMs` 1500, `MaxBatchProducts` 500, `Capacity` 10000, `MinRecheckSeconds` 10).
- `GET /api/v1/inventory/reconciliation?productPublicId=` (`inventory.adjust`) conserva su forma: compara y muestra las diferencias,
  **sin guardarlas**; `mismatches: []` significa que el invariante se cumple.

**Reglas de la revisión.** Solo se guarda un descuadre que se confirma **bajo bloqueo** del producto (así un movimiento en vuelo no da
falsos positivos). Hay **a lo sumo un descuadre abierto por clave** (un índice único lo garantiza). Una revisión posterior actualiza las
cifras y suma una revisión (`checkCount`); si el saldo ya cuadra, cierra el descuadre como "Se corrigió solo". Un descuadre
**descartado** no se reabre mientras las cifras sean las mismas; con cifras distintas se abre uno nuevo. Uno **resuelto** o "Se corrigió
solo" que vuelve a ocurrir abre un descuadre nuevo. Si el descuadre es del total del producto, no se abre cuando el mismo producto ya
tiene descuadres por posición (el total sería ruido).

Cómo se ven y se resuelven:
- `GET /api/v1/inventory/discrepancies?status=&warehousePublicIds=&productPublicIds=&categoryIds=&binIds=&kinds=&from=&to=&skip=&take=` —
  paginado (`take` ≤ 200, 50 por defecto; los abiertos primero). `status` acepta `OPEN`, `RESOLVED`, `DISMISSED` y `SELF_CORRECTED`;
  `kinds`, `BALANCE` (saldo por posición) y `PRODUCT_TOTAL` (total del producto); `from` y `to`, días locales de detección. La respuesta
  trae `total` y `openCount` (pendientes con los mismos filtros salvo el estatus). Cada fila: producto, almacén, posición, lote, `ledgerQty`
  (lo que da el Kárdex), `balanceQty` (el saldo), `difference` (**saldo − Kárdex**; positiva = el saldo dice más de lo que dan los
  movimientos), estatus, origen, cuándo se detectó, cuántas veces se revisó, y si está cerrado, quién, cuándo, la nota y "corregido de → a".
- `GET /api/v1/inventory/discrepancies/{publicId}` — la ficha: el descuadre, lo reservado hoy, los últimos 20 movimientos de esa clave y el
  historial de estatus.
- `POST /api/v1/inventory/discrepancies/{publicId}/resolve` — `{ "action": "REBUILD_BALANCE", "notes": "Corregido tras la restauración",
  "rowVersion": "<el de la ficha>" }` o `{ "action": "DISMISS", "notes": "Conteo físico correcto; el saldo se ajustará en el cierre",
  "rowVersion": "…" }`. `notes` es obligatoria para descartar (hasta 500 caracteres); `rowVersion` es opcional pero protege contra un cambio
  simultáneo.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Cantidad ≤ 0 | `La cantidad debe ser mayor que cero.` | 400 |
| Cantidad con más de 3 decimales | `La cantidad admite como máximo 3 decimales.` | 400 |
| Cantidad fuera de rango | `La cantidad excede el máximo permitido.` | 400 |
| Ajuste sin cantidad / cantidad = 0 | `Indique la cantidad del ajuste.` / `La cantidad del ajuste no puede ser cero.` | 400 |
| Ajuste sin motivo | `Indique el motivo del ajuste.` | 400 |
| Motivo reservado al sistema (`RECEIPT_VARIANCE`, `COUNT_VARIANCE`, `PICK_BATCH_REVERSAL`, `OPENING_BALANCE`, `TRACKING_CONVERSION`) | `El motivo {código} lo asigna el sistema.` | 400 |
| Ajuste sin nota, o con solo espacios (ajuste del 2026-09-30; el error va en `errors.notes`) | `Escriba una nota que explique el ajuste.` | 400 |
| Nota del ajuste o de la transferencia de más de 300 caracteres (`errors.notes`) | `Las notas admiten como máximo 300 caracteres.` | 400 |
| Motivo desconocido (también en el filtro `reasons` del Kárdex) | `Motivo de ajuste desconocido: 'X'.` | 400 |
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
| `desde` posterior a `hasta` (Kárdex y descuadres) | `La fecha 'desde' no puede ser posterior a la fecha 'hasta'.` | 400 |
| Tipo de movimiento desconocido en `types` | `Tipo de movimiento desconocido: 'X'.` | 400 |
| `direction` distinto de `IN` y `OUT` (Kárdex y resumen; `errors.direction`) | `La dirección debe ser IN (entradas) u OUT (salidas).` | 400 |
| Dueño (`ownerClientPublicIds`) que no existe o es de otra compañía | `Cliente no encontrado.` | 404 |
| Detalle de un movimiento que no existe o es de otra compañía | `Movimiento no encontrado.` | 404 |
| Lote/producto de otro tenant o de otro dueño (con `InventoryScope`) | `Lote no encontrado.` / `Producto no encontrado.` | 404 |
| "Ejecutar conciliación" con más de 200 productos (`errors.productPublicIds`) | `La conciliación manual admite como máximo 200 productos a la vez.` | 400 |
| "Ejecutar conciliación" con un producto que no existe | `Producto no encontrado.` | 404 |
| Filtro `kinds` de descuadres desconocido (`errors.kinds`) | `Tipo de descuadre desconocido: 'X'.` | 400 |
| Descuadre que no existe o es de otra compañía | `Descuadre no encontrado.` | 404 |
| Resolver sin acción o con otra distinta de las dos (`errors.action`) | `Indique la acción: REBUILD_BALANCE (corregir el saldo) o DISMISS (descartar).` | 400 |
| Descartar sin nota (`errors.notes`) | `Escriba una nota que explique por qué se descarta el descuadre.` | 400 |
| Nota de resolución de más de 500 caracteres (`errors.notes`) | `La nota admite como máximo 500 caracteres.` | 400 |
| `rowVersion` que no es base64 | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 |
| `rowVersion` que ya no es el vigente | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Resolver un descuadre ya cerrado (Resuelto, Descartado o Se corrigió solo) | `El descuadre ya está cerrado; solo se consulta.` | 422 |
| Corregir el saldo de un descuadre del total del producto | `Este descuadre es del total del producto; no se corrige por posición. Corrija los descuadres por posición o descártelo con una nota.` | 422 |
| Corregir cuando el Kárdex da menos que lo reservado | `El Kárdex da {ledger} para {sku} en {bin}, menos que lo reservado ({reserved}); libere la reserva antes de corregir el saldo.` | 409 |
| Corregir cuando el Kárdex da un saldo negativo | `El Kárdex da un saldo negativo ({ledger}) para {sku} en {bin}; revise los movimientos antes de corregir el saldo.` | 409 |
| Dos revisiones abrieron el mismo descuadre a la vez | `La conciliación chocó con otra revisión simultánea; intente de nuevo.` | 409 |

Cuando un `400` trae un solo error, el mismo mensaje también viaja en `detail`, además de en `errors`.

Mensajes que solo se ven en la pantalla del ajuste y de la transferencia (no hay HTTP): `Elija si el ajuste sube o baja el
inventario.`, `La cantidad debe ser mayor que cero.`, `No puede bajar más de lo disponible en la posición ({qty}).`, `No puede transferir
más de lo disponible en la posición ({qty}).`, `Seleccione el lote.`, `Elija el ítem a transferir.` y `Elija al menos una serie.`; y, al
corregir un descuadre que ya cuadraba, el aviso `El saldo ya cuadraba; el descuadre se cerró solo.`

### Estatus y transiciones del descuadre

`InventoryDiscrepancyStatus`: **Pendiente** (`OPEN`, inicial) → **Resuelto** (`RESOLVED`), **Descartado** (`DISMISSED`) o **Se corrigió
solo** (`SELF_CORRECTED`); los tres últimos son terminales. Todas las transiciones pasan por `StatusService.TransitionAsync` y quedan en el
historial (`/api/v1/status/history/INVENTORY_DISCREPANCY/{id}`); el alta y los cambios de estatus también quedan en la auditoría.

| De → a | Quién | Qué valida | Efectos |
|---|---|---|---|
| (nuevo) → Pendiente | El sistema (revisión automática, migración) o quien ejecuta la conciliación (`inventory.adjust`) | La revisión lo **confirma bajo bloqueo**; no hay otro abierto de la misma clave; no hay uno descartado con las mismas cifras | Se guarda con las cifras del Kárdex y del saldo, el origen y el movimiento que lo destapó; el historial dice "Kárdex {l}, saldo {b}."; aparece en "Necesita tu atención" |
| Pendiente → Resuelto | `inventory.adjust`, acción `REBUILD_BALANCE` | Es un descuadre por posición; `rowVersion` vigente; el Kárdex no da negativo ni menos que lo reservado | El saldo toma lo que da el Kárdex, **sin escribir un movimiento**; se guardan "corregido de → a", quién, cuándo y la nota; sale de "Necesita tu atención"; la pantalla ofrece "Crear conteo de esa posición" |
| Pendiente → Descartado | `inventory.adjust`, acción `DISMISS` | Nota obligatoria de hasta 500 caracteres; `rowVersion` vigente | No mueve inventario; no se reabre mientras las cifras sean las mismas |
| Pendiente → Se corrigió solo | El sistema (una revisión encuentra el saldo cuadrado) o `inventory.adjust` al pedir "corregir" cuando ya cuadraba (responde 200) | — | Se cierra sin tocar el saldo. El historial dice "La revisión encontró el saldo cuadrado con el Kárdex." o, al corregir, la nota escrita o "El saldo ya cuadraba con el Kárdex al corregir." |

Qué queda bloqueado en cada estatus:

| Estatus | Se puede | No se puede |
|---|---|---|
| Pendiente | Corregir el saldo (solo por posición), descartar, dejar que otra revisión actualice las cifras o lo cierre solo | Corregir el total del producto (422); resolver con Kárdex negativo o menor que lo reservado (409) |
| Resuelto, Descartado, Se corrigió solo | Consultar la ficha y el historial | Resolver otra vez (422 "El descuadre ya está cerrado; solo se consulta."). Si el problema reaparece, se abre un descuadre nuevo (salvo el descartado con las mismas cifras) |

### Notas de lectura

- El Kárdex expone `quantity` (la del ledger, con signo) y `signedQuantity` (la misma perspectiva aplicada a un
  filtro de almacenes/posiciones: lo que sale del filtro es negativo, lo que entra es positivo, y lo interno o sin
  filtro de una `TRANSFER` vale 0).
- La genealogía de un lote arma sus destinos (orden, cliente, consignatario) leyendo el `Ref` de cada movimiento;
  la conciliación reconstruye `StockBalance` sumando el lado "To" y restando el lado "From" de cada fila del
  ledger (por clave) y, por producto, comparando la suma de `Quantity` sin `TRANSFER` contra `QtyOnHand`.
- **"Ajustes de inventario" salió del menú.** La pantalla antigua `/warehouse/inventory-adjustments` no mostraba ajustes: mostraba las
  órdenes de compra recibidas de forma incompleta. Desde el Lote 14 esa dirección lleva a Compras, y los faltantes se resuelven en la
  pestaña **Faltantes** de la ficha de cada orden de compra (sección 8). Los ajustes de inventario están ahora en **Transferencias y
  ajustes** y en el Kárdex.

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
- Lote 16 (recibo directo a posición): `receivingMode` en el alta y en el `PATCH` del recibo; `targetBinId` o `targetBinCode` en cada línea;
  `GET /api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions`; `POST /api/v1/receipts/{publicId}/targets/suggest`; y `includeLines=true` en la
  lista. Todo en la sección 4.1.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Tipo desconocido | `Tipo de recepción desconocido: 'X'. Use ASN, BLIND o RETURN.` | 400 |
| Tipo `ASN` sin `asnId` ni `purchaseOrderPublicId` | `Un recibo con aviso de llegada se crea indicando el aviso (asnId) o la orden de compra (purchaseOrderPublicId).` | 400 |
| `asnId` y `purchaseOrderPublicId` a la vez | `Indique el aviso de llegada o la orden de compra, no ambos.` | 400 |
| Sin posición de recepción (sin zona `STAGING`; **no aplica a un recibo directo**, sección 4.1) | `El almacén no tiene una posición de recepción (zona STAGING); indíquela.` | 422 |
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
| Completado o Completado con diferencia → **Acomodado** (directo) | el sistema, al confirmar | Ninguna `PUTAWAY` se creó: todo fue a cruce de muelle, se recibió 0 o el recibo es **directo a posición** (sección 4.1). | — |
| Esperado, Recibiendo o Discrepancia → baja (`DELETE`) | `warehouse.receive` | Recibo abierto (si no, 422) y sin cruce de muelle asignado (409). | Baja lógica (el recibo deja de listarse, las líneas se conservan). Un aviso nacido de una orden de compra se cancela; el de un cliente vuelve a quedar pendiente. |

**Efectos de confirmar** (una sola transacción):
- Kárdex: con aviso u orden de compra, `RECEIPT` por lo esperado más `ADJUSTMENT RECEIPT_VARIANCE` por la diferencia (D4; el
  ajuste del sistema no pide nota). En ciegos y devoluciones, un solo `RECEIPT` por lo **recibido**: la diferencia solo marca el
  estatus y `adjustmentTxnId` queda vacío.
- La orden de compra avanza a `PARTIAL` o `RECEIVED`; el aviso pasa a `RECEIVED`.
- Se reparte el cruce de muelle asignado y se crean las `PUTAWAY` del remanente con una posición sugerida. En un recibo **directo** no hay `PUTAWAY`: la
  mercancía entra a la posición destino de cada línea (sección 4.1).
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

### 4.1 Recibo directo a posición (Lote 16)

Qué hace: en un recibo **directo a posición** la mercancía **no pasa por la posición de recepción**. Cada línea que recibe algo lleva su **posición destino** y,
al confirmar, entra ahí (Kárdex `RECEIPT` a esa posición; con aviso u orden de compra y diferencia, también el ajuste `RECEIPT_VARIANCE` en esa misma
posición), **sin tareas de acomodo**. El recibo pasa de Completado a **Acomodado** en el mismo momento. Un almacén sin zona `STAGING` (Advance Solutions)
puede recibir así. El modo del almacén y la posición de recepción por defecto están en la sección 1.4.

**El recibo guarda el modo con que se abrió.** La lista y la ficha traen `receivingModeCode` (`PUTAWAY` o `DIRECT`) y `receivingMode` (la etiqueta). Cambiar el
modo del almacén **no cambia los recibos ya abiertos**. Mientras el recibo está abierto (Esperado, Recibiendo o Discrepancia), quien puede recibir
(`warehouse.receive`) cambia el modo **solo de ese recibo**.

Quién puede: `warehouse.receive` (modo del recibo, posición destino de cada línea, "Usar posiciones sugeridas" y confirmar); `inventory.view` (ver las
sugerencias de una línea). Módulo **WMS_LOTSERIAL**. Recibir contra una orden de compra exige además `purchasing.receive` y el módulo **PURCHASING**.

Cómo se usa:
- **Abrir un recibo directo.** `POST /api/v1/receipts` con `receivingMode` (`PUTAWAY` o `DIRECT`, sin distinguir mayúsculas). Sin él, el recibo toma el modo del
  almacén. En directo **no se pide** la posición de recepción.
  ```json
  { "warehousePublicId": "…", "type": "BLIND", "receivingMode": "DIRECT",
    "lines": [ { "productPublicId": "…", "receivedQty": 8, "targetBinCode": "R-02" } ] }
  ```
- **Posición destino de una línea.** Por id (`targetBinId`) o por código (`targetBinCode`, el que escanea la app; se compara sin distinguir mayúsculas), **no ambos**.
  Va en `lines[]` del alta, en `POST …/lines` y en `PUT …/lines/{lineId}` (con `targetBinId`; `"clearTargetBin": true` la quita).
- **Cambiar el modo de un recibo abierto.** `PATCH /api/v1/receipts/{publicId}` con `{ "receivingMode": "PUTAWAY", "rowVersion": "…" }`. Pasar a "Con acomodo"
  exige una posición de recepción (la del encabezado o la del almacén). Si el recibo ya tenía destinos por línea, la tarea de acomodo de cada línea va a su destino.
- **Sugerencias de una línea.** `GET /api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions?take=3` (`take` de 1 a 10; por defecto 5; uno mayor se
  toma como 10). Responde un arreglo:
  ```json
  [ { "binId": 41, "binCode": "R-02", "zoneCode": "RSV", "zoneTypeCode": "RESERVE", "reasonCode": "RESERVE_EMPTY",
      "reason": "Reserva vacía", "maxCapacityQty": 40, "qtyOnHand": 0, "claimedQty": 0, "freeQty": 40, "fits": true } ]
  ```
- **Usar posiciones sugeridas.** `POST /api/v1/receipts/{publicId}/targets/suggest` con `{ "rowVersion": "…" }` (opcional). Responde `{ "receipt": {…}, "assigned": 2,
  "withoutSuggestion": 1 }`: asigna la primera sugerida **donde cabe** a cada línea sin destino que lo necesita. **Nada se llena solo**: solo cuando se pide.
- **Confirmar.** `POST /api/v1/receipts/{publicId}/confirm`, igual que siempre.
- **Lista con líneas.** `GET /api/v1/receipts?includeLines=true` trae en `lines` las líneas de cada recibo de la página (las mismas de la ficha), en una sola
  consulta; sin el parámetro, `lines` es `null`. Lo usa la exportación de Recibos (manual de pantallas).

Campos y reglas:

| Campo | Regla |
|---|---|
| `receivingMode` (alta y `PATCH` del recibo) | `PUTAWAY` o `DIRECT`. Vacío o ausente = el del almacén (en el `PATCH`, sin cambio) |
| `targetBinId` / `targetBinCode` (línea) | Posición **del almacén del recibo**, **de guardado** (no `STAGING` ni `CROSSDOCK`; la **cuarentena sí**), activa y con su zona activa |
| `clearTargetBin` (`PUT` de la línea) | `true` quita el destino |
| Destino obligatorio | Al confirmar, en cada línea que recibe algo (recibido mayor que 0). No lo pide un producto **por lote sin lote** (no mueve inventario) ni una línea con cruce de muelle asignado |
| Cantidad de la línea | Una línea entra **entera a una sola posición**. Para repartirla, se transfiere después (sección 3.2) |

**Sugerencias y cupo.**
- La sugerencia es el acomodo dirigido de siempre: posición preferida del producto, mismo lote, mismo producto, picking vacía si el producto rota mucho (por las
  salidas de los últimos 30 días), reserva vacía y reserva con espacio. Razones: `PREFERRED`, `CONSOLIDATE_LOT`, `CONSOLIDATE`, `PICKING_FAST`, `RESERVE_EMPTY`,
  `RESERVE` (y, en devoluciones, `QUARANTINE_RETURN`). La cantidad es la recibida de la línea (si es 0, la esperada; si no, 1).
- **El cupo cuenta unidades.** Espacio libre = `cupo − existencia de la posición (todos los productos) − lo que otras líneas del mismo recibo ya destinan a esa posición`,
  nunca negativo; sin cupo configurado, `freeQty` es `null` y nunca excede. `fits` dice si la cantidad de la línea cabe. Las que caben van **primero**; las que
  exceden, al final con `fits: false`. La consolidación también mira las otras líneas del mismo producto en el recibo.
- **El cupo solo avisa** (D4). Se puede elegir una posición donde no cabe todo: la ficha de la línea trae `targetFreeQty` (el espacio libre; solo con el recibo
  abierto y un destino con cupo; no descuenta la propia línea) y la pantalla muestra "Excede el cupo de {bin}: caben {free}". No bloquea guardar ni confirmar; lo que sobre
  se transfiere después. Dos recibos abiertos a la vez no se ven entre sí: ambos pueden pasar el mismo cupo.
- "Usar posiciones sugeridas" **solo asigna donde cabe**. Una línea sin ninguna posición donde quepa queda sin destino y se cuenta en `withoutSuggestion`.
- **Desde el Lote 16, el acomodo dirigido de las tareas de siempre también respeta el cupo en unidades**: salta la posición donde la cantidad no cabe. Las posiciones de Advance
  Depot tienen cupos estimados (sección 1.2), así que las sugerencias de acomodo pueden cambiar.

**Devoluciones y cuarentena.** En un recibo de **devolución**, la primera sugerida es una posición de **cuarentena** si existe (`QUARANTINE_RETURN`, "Cuarentena (devolución)").
En los demás recibos la cuarentena no se sugiere, pero se puede elegir a mano.

**Cruce de muelle.** Una línea con cruce de muelle asignado **entra a la posición de recepción** como siempre y no exige destino; su remanente genera una tarea de
acomodo hacia su posición destino (o la sugerida). El recibo queda **Completado** hasta cerrar esa tarea (sección 9).

**Recibos de la app anterior.** La app nueva escanea la posición destino de cada línea (capítulo 9, sección 4). Un recibo de una app anterior, enviado de una vez (`confirm: true`)
sin `receivingMode` y sin ninguna posición destino en un almacén **directo**, se recibe **"Con acomodo"** (con tareas) y no se pierde.

### Validaciones (recibo directo)

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `receivingMode` desconocido (alta o `PATCH` del recibo; `errors.receivingMode`) | `Modo de recepción desconocido: 'X'. Use PUTAWAY o DIRECT.` | 400 |
| Confirmar un recibo directo con una línea que recibe algo y no tiene destino (`errors["lines[i].targetBinId"]`; `i` es la posición de la línea, desde 0) | `Indique la posición destino de {sku}: el recibo entra directo a posición.` | 400 |
| Posición destino en una zona de recepción o de cruce de muelle (al guardar la línea o al confirmar) | `La posición {code} está en una zona {zoneType}; la posición destino debe ser de guardado.` | 400 |
| `targetBinCode` que no existe en el almacén del recibo (`errors.targetBinCode`; en el alta, `errors["lines[i].targetBinCode"]`) | `La posición {code} no existe en el almacén del recibo.` | 400 |
| `targetBinId` y `targetBinCode` a la vez (`errors.targetBinId`) | `Indique la posición destino por id o por código, no ambos.` | 400 |
| `targetBinId` de otro almacén, de otra compañía o inexistente | `Posición no encontrada.` | 404 |
| Posición destino desactivada (al guardar la línea o al confirmar) | `La posición destino {code} está desactivada.` | 422 |
| Zona de la posición destino inactiva (al guardar la línea o al confirmar) | `La zona de la posición destino {code} está inactiva.` | 422 |
| Alta atómica con aviso u orden de compra: el mismo producto con dos destinos distintos (`errors["lines[i].targetBinCode"]`) | `Ya se capturó {sku} con destino {code}; en un recibo con aviso u orden de compra cada línea entra a una sola posición.` | 400 |
| `PATCH` del recibo a `PUTAWAY` sin posición de recepción (el almacén no tiene zona `STAGING` y el encabezado no la indica) | `El almacén no tiene una posición de recepción (zona STAGING); indíquela.` | 422 |
| Guardar una línea, cambiar el modo o "Usar posiciones sugeridas" en un recibo ya confirmado | `El recibo {n} ya fue confirmado; no se puede modificar.` | 422 |
| "Usar posiciones sugeridas" con un `rowVersion` viejo | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Sugerencias de una línea que no es de este recibo | `Línea del recibo no encontrada.` | 404 |

Las claves de los errores de una línea siguen la forma de la solicitud: `targetBinId` o `targetBinCode` en el `PUT`, `line.…` al agregar una línea y `lines[i].…`
en el alta o al confirmar. Los demás mensajes de recibo (lote, series, cantidades) son los de la tabla de la sección 4.

### Estatus y transiciones (recibo directo)

Los estatus son los seis de la sección 4: **no hay estatus nuevos**. Lo que cambia es el final.

| De → a | Quién | Qué valida | Efectos |
|---|---|---|---|
| (alta) → **Esperado** o **Recibiendo** / **Discrepancia** | `warehouse.receive` | Almacén activo. **No** exige posición de recepción. Si el alta trae destinos, las reglas de la tabla de arriba | Crea el encabezado con el modo con que se abrió; igual que con acomodo |
| Esperado → **Recibiendo** ↔ **Discrepancia** | `warehouse.receive` | Cada línea guardada (el destino no cambia el estatus) | — |
| Recibiendo → **Completado** → **Acomodado** (un solo `confirm`) | `warehouse.receive` | Destino en cada línea que recibe algo (400 por línea); destino y zona activos (422); seguimiento de lote y serie | `RECEIPT` a cada destino; ninguna tarea; el historial deja **Recibiendo → Completado → Acomodado** (D7); Actividad reciente registra un solo "Recibo … confirmado" (D13) |
| Discrepancia → **Completado con diferencia** → **Acomodado** | `warehouse.receive` | Igual | Igual, más el ajuste `RECEIPT_VARIANCE` en el destino si hay aviso u orden de compra. Usa la entrada lateral sembrada `RECEIVED_VARIANCE → PUTAWAY`; si la compañía la quitó, el motor de estatus rechaza el paso (no se probó) |
| Recibiendo o Discrepancia → **Completado** o **Completado con diferencia** (**se queda ahí**) | el sistema | Alguna línea tiene cruce de muelle asignado | Esa línea entra a la posición de recepción. Si queda remanente (lo recibido menos lo asignado al cruce), se crea una tarea de acomodo hacia su destino y el recibo **se queda** ahí hasta cerrarla; si todo fue al cruce, pasa a Acomodado en el mismo momento |

Qué queda bloqueado en cada estatus (recibo directo):

| Estatus | Se puede | Queda bloqueado |
|---|---|---|
| Esperado, Recibiendo, Discrepancia | Fijar, cambiar o quitar el destino de cada línea; "Usar posiciones sugeridas"; cambiar el modo **de este recibo**; confirmar | Confirmar sin destino en una línea que recibe algo: `400` con el mensaje de la tabla |
| Completado, Completado con diferencia (solo si hubo cruce de muelle) | Consultar y trabajar las tareas del remanente | Destino, modo, "Usar posiciones sugeridas", alta y baja de líneas: `422` `El recibo {n} ya fue confirmado; no se puede modificar.` |
| Acomodado | Consultar | Igual que el anterior |

Casos frecuentes (Lote 16):
- *Quiero que un almacén reciba directo*: cambie su **Modo de recepción** (sección 1.4). Los recibos nuevos nacen directos; los abiertos conservan su modo.
- *Un recibo abierto debe acomodarse después*: cambie **solo ese recibo** a "Con acomodo" antes de confirmar (`PATCH` del recibo). Se crean las tareas de acomodo, hacia el destino de cada
  línea si lo tiene.
- *Llegó más de lo que cabe en la posición*: elija la posición igual (la pantalla avisa, no bloquea) y transfiera lo que sobre después (sección 3.2), o reparta la mercancía en dos líneas del
  mismo producto si el recibo es ciego o de devolución.
- *Una línea no tiene posición sugerida*: ninguna posición de guardado tiene espacio. Elija una a mano.
- *Un recibo directo no aparece en "Acomodo pendiente"*: es lo esperado; ahí solo aparecen los recibos anteriores al cambio de modo y los que tuvieron cruce de muelle (sección 5).

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

**Detalle de una tarea de acomodo repartido (2026-10-07).** La tarea guarda una sola posición destino, pero un acomodo repartido (la cantidad por posición del aparato o `POST /warehouse-tasks/{id}/distribute`) movió el inventario a varias. `WarehouseTaskDto.moves` trae **una línea por posición** tomada del ledger (`transactionId`, `fromBinCode`, `toBinCode`, `quantity`, `lotNumber`, `atUtc`); vacío mientras la tarea no mueva nada. En la lista de tareas y en el detalle del recibo la columna «De → a» dice **«R1 → 5 posiciones»** y debajo cada posición con su cantidad (`01-E-03 · 20, 01-E-04 · 20…`); con una sola posición se ve «De → a» como siempre.

**Acomodo pendiente en los almacenes directos (Lote 16).** Un recibo **directo a posición** (sección 4.1) no genera tareas `PUTAWAY`: la mercancía entra a su posición destino al
confirmar y el recibo pasa a Acomodado. Por eso, en un almacén directo, la pestaña "Acomodo pendiente" solo muestra (a) los recibos **anteriores al cambio de modo** que seguían con tareas,
(b) los recibos abiertos que se pasaron a "Con acomodo" y (c) los que tuvieron **cruce de muelle**. Las tareas que ya existían **siguen** su curso. `GET /api/v1/warehouse-tasks/putaway-suggestions`
respeta ahora el **cupo en unidades** de la posición (salta donde no cabe) y trae `maxCapacityQty` y `freeQty` (`cupo − existencia`; `null` sin cupo). La pantalla avisa cuando el almacén filtrado es
directo.

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
números de serie) y, al **confirmar**, ajusta contra el **saldo actual bloqueado** (no contra la foto): si el saldo
se movió desde que se tomó la foto, la línea queda marcada `systemQtyChanged` para revisión, pero el ajuste sigue
siendo correcto porque se calcula sobre el saldo real en ese instante. Desde el Lote 14:

- **Confirmar es un solo paso.** "Confirmar conteo y ajustar" lleva un conteo **Pendiente** (o **Contado**) directo a su estatus final:
  **Concordancia** si no hubo nada que ajustar, o **Diferencia** si se asentó al menos un movimiento.
- **"Conteo de lo cambiado".** Crea de una vez un conteo por cada posición que tuvo movimientos en una ventana de tiempo.
- La lista de conteos trae **el total** (ya no se corta en 200) y cada conteo dice su posición, su zona, su origen y a quién está asignado.

Quién puede (módulo **WMS_LOTSERIAL**):

| Acción | Permiso |
|---|---|
| Ver la lista y la ficha (a ciegas sin `warehouse.count`, ver el Lote 8A más abajo) | `inventory.view` |
| Alta manual, captura, agregar lo encontrado y terminar (conteo a ciegas de la app) | `warehouse.count.capture` (lo tiene implícito quien tiene `warehouse.count`) |
| **Confirmar** (reconciliar), refrescar la foto, eliminar, y **"Conteo de lo cambiado"** (vista previa y alta) | `warehouse.count` |
| Asignar el conteo a un usuario (`POST /api/v1/warehouse-tasks/{taskId}/assign`) | `warehouse.manage` |

"Lo cambiado" pide `warehouse.count` y no `warehouse.count.capture` porque crea muchos conteos de una vez. La web pide además
`admin.users` para listar a quién asignar.

Cómo se usa:
- `GET /api/v1/cycle-counts?warehousePublicIds=&status=&from=&to=&binIds=&productPublicIds=&categoryIds=&search=` — los 200 más
  recientes (se conserva por compatibilidad; **`from` y `to` son días locales de Puerto Rico** desde el Lote 14).
- `GET /api/v1/cycle-counts/page?…&zoneIds=&origins=&skip=&take=` — **la página con el total**: mismos filtros, más `zoneIds` (alguna
  línea en esas zonas) y `origins` (`MANUAL` "Selección", `CHANGES` "Lo cambiado"; `MANUAL` incluye los conteos anteriores al Lote 14, que
  no tenían origen). `take` de 1 a 200 (50 por defecto; fuera de rango se ajusta, no da error). Los más recientes primero. `status` acepta `OPEN`, `COUNTED`, `RECONCILED` y `RECONCILED_VARIANCE`, uno o
  varios. Cada fila trae `binCount` (posiciones distintas) y, si el conteo es de **una** posición, `binCode` y `zoneCode`; `originCode` y
  `origin`; la ventana `changesFromUtc` y `changesToUtc` (solo "lo cambiado"); `taskId` (la tarea COUNT, para asignarla) y
  `assignedToName`. Respuesta: `{ total, skip, take, items }`.
- `POST /api/v1/cycle-counts` — `{ "warehousePublicId": "...", "zoneIds": [...], "binIds": [...] }` (todo
  opcional: sin filtros toma todo el saldo en mano del almacén). Máximo 1000 líneas. Origen `MANUAL`.
  **`resumeOpen` (2026-10-07, lo usa la app):** con **una sola** posición en `binIds` y `resumeOpen: true`, si esa posición ya tiene un conteo **abierto** (Pendiente) cuyas líneas son todas de esa posición
  (no el de varias posiciones ni el de por producto), se **devuelve ese conteo** con `resumed: true` en vez de crear otro; si tiene asignado a otra persona → 409
  `Esa posición la está contando {Nombre} ({CC-#####}).`; si no tiene asignado, queda asignado a quien lo retoma. Cada línea trae ahora `checkState` (MATCH | RECOUNT | FINAL | null) para que el aparato sepa cuáles
  ya se verificaron. Sin `resumeOpen` (la web) todo sigue igual.
- `GET /api/v1/cycle-counts/{id}` — ficha en modo informado (foto, contado, diferencia informativa, series
  esperadas/contadas, saldo actual). Cada línea trae ahora `barcode` (el código de barras del producto, que el escáner de la web usa para
  llegar a la línea; no revela lo esperado, así que también llega a ciegas).
- `PUT /api/v1/cycle-counts/{id}/lines` — captura por línea (`countedQty` en NONE/LOT, `serialNumbers` en SERIAL).
- `POST /api/v1/cycle-counts/{id}/lines` — línea agregada a mano (lo encontrado sin foto previa).
- `POST /api/v1/cycle-counts/{id}/finish` — `OPEN → COUNTED`, exige todas las líneas capturadas (lo usa la app de almacén a ciegas).
- `POST /api/v1/cycle-counts/{id}/refresh` — re-fotografía las líneas con foto vieja y borra su captura.
- `POST /api/v1/cycle-counts/{id}/reconcile` — **"Confirmar conteo y ajustar"**: desde `OPEN` o `COUNTED`, asienta los ajustes y pasa a
  `RECONCILED_VARIANCE` ("Diferencia") o `RECONCILED` ("Concordancia").
- `DELETE /api/v1/cycle-counts/{id}` — solo `OPEN`. Cancela su tarea COUNT (`CANCELLED`, con fecha de cierre: deja de sumar antigüedad y el aparato la borra en su siguiente sincronización).

### Conteo de lo cambiado

Crea **un conteo Pendiente por cada posición** del almacén con movimientos en una ventana de tiempo, cada uno con su tarea COUNT y con
origen `CHANGES` ("Lo cambiado").

- `GET /api/v1/cycle-counts/changes-preview?warehousePublicId=&fromUtc=&toUtc=&zoneIds=&includeEmpty=` — vista previa: lo mismo que haría el
  alta, **sin escribir nada**.
- `POST /api/v1/cycle-counts/from-changes` — `{ "warehousePublicId": "...", "fromUtc": "2026-09-30T04:00:00Z", "toUtc": null,
  "zoneIds": [ … ], "includeEmpty": true }`. Todo es opcional: sin almacén se usa el único activo; sin fechas, la ventana por defecto; sin
  zonas, todo el almacén; `includeEmpty` es `true` por defecto.

Reglas (decisiones del dueño D2, D3 y D4):

| Regla | Detalle |
|---|---|
| Ventana por defecto | Desde el `changesToUtc` del último "lo cambiado" **de ese almacén**; **la primera vez, desde las 00:00 de hoy en hora de Puerto Rico**; hasta ahora. Las fechas se pueden cambiar antes de crear. "Hasta" no pasa de ahora y el rango no pasa de **31 días** (si la última generación es más vieja, la ventana empieza 31 días atrás) |
| Qué movimientos cuentan | Los que tocan el almacén, de cualquiera de los dos lados, **sin los que salen de un conteo** (un conteo no genera otro conteo) |
| Qué se cuenta en una posición | **Todo lo que hay en ella** (saldo mayor que cero) y, con `includeEmpty`, las claves que se movieron en la ventana y hoy están en cero (`SystemQty` 0, para confirmar que de verdad está vacía) |
| Qué posiciones se saltan | Las **inactivas** y las que ya tienen un conteo **Pendiente o Contado**. La vista previa dice cuántas de cada tipo, y cuántas no tienen nada que contar |
| Tope | **200 posiciones por vez.** Con más, no se crea nada y se piden fechas o zonas más cortas |
| Todo o nada | Todo se crea en **una sola transacción**: si algo falla, no queda ningún conteo a medias. Medido en la demo: 120 posiciones en 1,5 segundos |

La vista previa devuelve `fromUtc`, `toUtc`, `movements`, `positions` (los conteos que se crearían), `positionsWithOpenCount`,
`positionsInactive`, `positionsEmpty`, `lines`, `maxPositions`, `lastChangesToUtc` (la generación anterior; nulo la primera vez) y
**`problem`**: el mensaje exacto con el que el alta respondería 400 (nulo si se puede crear). El alta devuelve `{ window, counts }`, la
ventana usada y los conteos creados.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Más de 1000 líneas seleccionadas | `El conteo admite como máximo 1000 líneas; acote los filtros.` | 400 |
| Filtros sin inventario en mano | `Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano.` | 400 |
| Conteo ya confirmado (Concordancia o Diferencia): editar, capturar, terminar, refrescar, agregar una línea, confirmar otra vez o eliminar | `El conteo ya fue reconciliado; solo se consulta.` | 422 |
| Terminar un conteo que ya está Contado | `El conteo ya se terminó; puede corregir la captura o reconciliarlo.` | 422 |
| Conteo sin líneas (terminar o confirmar) | `El conteo no tiene líneas.` | 422 |
| Producto con serie: cantidad suelta en vez de series | `En productos con serie se capturan los números de serie, no la cantidad.` | 400 |
| Producto sin serie con series capturadas | `El producto {sku} no se controla por serie; no capture números de serie.` | 400 |
| Línea repetida (posición, producto, lote) | `Esa posición, producto y lote ya están en el conteo.` | 409 |
| Cantidad contada negativa | `La cantidad contada no puede ser negativa.` | 400 |
| Terminar o confirmar sin todas las líneas capturadas | `Faltan {n} línea(s) por contar.` | 422 |
| Confirmar con lo contado < lo reservado | `El conteo de {sku} en {bin} ({contado}) es menor que lo reservado ({reservado}); libere la reserva antes de reconciliar.` (sin escribir nada) | 409 |
| `rowVersion` que ya no es el vigente | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Eliminar un conteo ya Contado | `Solo se elimina un conteo abierto; este ya se terminó de contar.` | 422 |
| Almacén inactivo (alta y "lo cambiado") | `El almacén está inactivo.` | 422 |
| Con más de un almacén activo, sin indicar cuál | `Indique el almacén: la compañía tiene más de uno.` | 400 |
| Zona o posición que no es del almacén | `Zona no encontrada.` / `Posición no encontrada.` | 404 |
| Serie capturada en dos líneas del mismo conteo | `La serie {s} está capturada en más de una línea del conteo.` | 409 |
| Completar la tarea `COUNT` desde la cola | `Las tareas de conteo se completan desde Conteo cíclico.` | 422 |
| "Lo cambiado": `desde` posterior a `hasta` (`errors.fromUtc`) | `La fecha 'desde' no puede ser posterior a la fecha 'hasta'.` | 400 |
| "Lo cambiado": rango de más de 31 días (`errors.fromUtc`) | `El rango de "lo cambiado" admite como máximo 31 días.` | 400 |
| "Lo cambiado": ninguna posición activa con movimientos (`errors.filters`) | `No hubo movimientos en {almacén} entre {desde} y {hasta}; no hay posiciones que contar.` (fechas y horas locales, `dd/MM/aaaa HH:mm`) | 400 |
| "Lo cambiado": todas las posiciones ya tienen un conteo Pendiente o Contado (`errors.filters`) | `Las {n} posiciones con cambios ya tienen un conteo pendiente.` | 400 |
| "Lo cambiado": más de 200 posiciones (`errors.filters`) | `Hay {n} posiciones con cambios; se generan como máximo 200 a la vez. Acote el rango de fechas o las zonas.` | 400 |

En "lo cambiado", si las posiciones que quedan no tienen nada que contar, el alta responde con el mismo mensaje de "Los filtros no
seleccionan inventario en mano…" (`errors.filters`, 400).

Mensajes que solo se ven en la pantalla (no hay HTTP): `Ese código no está en este conteo. Use "Agregar lo encontrado" si el producto
está en la posición.`, `{n} líneas coinciden: elija la posición y el lote.`, `Hay cantidades que no se pudieron guardar; corríjalas antes de
confirmar.`, `Elija el almacén.` (en "Conteo de lo cambiado"), `El conteo ya fue confirmado; solo se consulta.` y `La web no confirma
conteos a ciegas.` (las razones por las que el botón **Confirmar conteo y ajustar** está deshabilitado, junto con `Faltan {n} línea(s) por
contar.` y `El conteo no tiene líneas.`).

### Estatus y transiciones

`CycleCountStatus`: **Pendiente** (`OPEN`, inicial) → **Contado** (`COUNTED`, solo cuando se cuenta a ciegas) → **Concordancia**
(`RECONCILED`) o **Diferencia** (`RECONCILED_VARIANCE`), los dos terminales. Desde Pendiente también se llega directo a Concordancia o
Diferencia (D8). Las etiquetas "Pendiente" y "Concordancia" reemplazan a "Abierto" y "Reconciliado" de antes; los conteos ya cerrados
con algún ajuste pasaron a **Diferencia** (con una fila de historial "Lote 14: conteo con diferencia.").

- **Concordancia**: al confirmar no se asentó ningún movimiento (lo contado coincide con el saldo actual en todas las líneas).
- **Diferencia**: al confirmar se asentó **al menos un movimiento** (un ajuste `COUNT_VARIANCE` o, en productos con serie, una baja, un alta
  o una transferencia). No significa lo mismo que una línea con varianza contra la foto: si el saldo cambió desde la foto y lo contado
  coincide con el saldo actual, no hay movimiento y el conteo queda en Concordancia.

| De → a | Quién | Qué valida | Efectos |
|---|---|---|---|
| (nuevo) → Pendiente | `warehouse.count.capture` (alta manual) o `warehouse.count` ("lo cambiado") | Almacén activo; a lo sumo 1000 líneas por conteo; en "lo cambiado", hasta 200 posiciones | Se toma la foto de cada línea; se crea una tarea COUNT por conteo; el origen queda en `MANUAL` o `CHANGES` |
| Pendiente → Contado | `warehouse.count.capture` (`POST /finish`; la app a ciegas) | Todas las líneas capturadas y al menos una línea | Solo cambia el estatus; la web no usa este paso |
| Pendiente o Contado → Concordancia | `warehouse.count` (`POST /reconcile`) | Todas las líneas contadas; lo contado no puede ser menor que lo reservado; una serie no puede estar en dos líneas; `rowVersion` vigente | No se asienta ningún movimiento; se guardan el saldo al confirmar por línea, la fecha y el usuario; la tarea COUNT pasa a `DONE` |
| Pendiente o Contado → Diferencia | `warehouse.count` (`POST /reconcile`) | Las mismas | Se asientan los ajustes `COUNT_VARIANCE` (contado − saldo actual) ligados al conteo, con su movimiento en el Kárdex; cada línea queda con su ajuste; la tarea COUNT pasa a `DONE`; el indicador "Conteos con diferencia" y el evento de Actividad `COUNT_RECONCILED` lo toman |

Qué queda bloqueado en cada estatus:

| Estatus | Se puede | No se puede |
|---|---|---|
| Pendiente | Capturar, agregar lo encontrado, refrescar la foto, terminar, confirmar, eliminar, asignar | — |
| Contado | Capturar, agregar lo encontrado, refrescar, confirmar | Terminar otra vez (422 "El conteo ya se terminó; puede corregir la captura o reconciliarlo.") y eliminar (422) |
| Concordancia, Diferencia | Consultar y ver el historial | Cualquier cambio (422 "El conteo ya fue reconciliado; solo se consulta.") |

Mientras un conteo está Pendiente o Contado, **su posición está ocupada**: "lo cambiado" la salta. Eliminar (`DELETE`, solo `OPEN`) cancela
su tarea `COUNT`.

### Lote 8A — conteo a ciegas (app de almacén)

El almacenista cuenta **sin ver lo esperado** y la confirmación se hace en la web con `warehouse.count`.

- Permiso nuevo `warehouse.count.capture` (categoría WAREHOUSE, "Capturar conteo (a ciegas)"): alta
  (`POST /api/v1/cycle-counts`), captura por línea (`PUT .../lines`), captura en lote (`PUT .../lines/batch`),
  agregar lo encontrado (`POST .../lines`) y terminar (`POST .../finish`). Quien tiene `warehouse.count` lo tiene
  implícito (los roles propios que ya contaban no pierden nada); el Operador de almacén lo trae en su plantilla.
- Refrescar, **confirmar** y eliminar siguen exigiendo `warehouse.count` (sin él: 403 `Falta el permiso ...`).
- Ficha y respuestas a ciegas: quien no tiene `warehouse.count` recibe `isBlind = true` y las cantidades esperadas
  de las líneas en `null` (`systemQty`, `varianceQty`, `currentQty`, `reconciledSystemQty`, `adjustedQty`;
  `expectedSerials` vacío), tanto en `GET /api/v1/cycle-counts/{id}` como en la respuesta de alta, captura y
  terminar. `onlyVariance` se ignora a ciegas.
- Solo lectura (`inventory.view` sin `warehouse.count.capture`) ve la ficha a ciegas pero no captura (403).
- A ciegas, el encabezado tampoco revela lo esperado: `count.varianceLines` y `count.netVariance` llegan en `null` en la
  ficha, en las respuestas de alta, captura, captura en lote y terminar, y en la lista `GET /api/v1/cycle-counts` y
  `GET /api/v1/cycle-counts/page` (con lo contado permitirían deducir lo esperado). Con `warehouse.count` nunca son `null`.
- En la web (Almacén → Conteo cíclico) esto cambia lo que ven los roles sin `warehouse.count` (Solo lectura,
  Facturación): la columna "Diferencia neta" ya no se muestra, porque el API ya no la envía. Antes veían la diferencia
  real. En la ficha, un valor `null` se pinta como "—", nunca como 0. **La web no confirma conteos a ciegas**: el botón queda
  deshabilitado con "La web no confirma conteos a ciegas.".
- Sincronización del aparato (`/api/v1/sync/*`, módulo WMS_LOTSERIAL, `inventory.view`): `GET /api/v1/sync/purchase-orders`
  exige además el módulo **PURCHASING** y `purchasing.view`, igual que `/api/v1/purchase-orders` (sin el permiso 403;
  con el módulo apagado 403 `El módulo 'PURCHASING' no está habilitado para esta compañía.`).

### Lote 21 — Conteo por producto, corrección del supervisor, vista previa, cierre en bloque y posiciones provisionales

Las posiciones del almacén no siempre están bien etiquetadas, así que además de contar **por posición** se cuenta **por producto**: se
escanea el producto, el sistema lista las posiciones donde dice que hay existencia y quien cuenta anota lo que encuentra en cada una.
El supervisor revisa en la web lo contado, corrige si hace falta, ve el efecto antes de confirmar y cierra de una vez los conteos que
cuadran. Lo que **no** cambia: la unidad del conteo sigue siendo la línea (posición × producto × lote); el ajuste se calcula contra el saldo
**actual** al confirmar y se asienta por el ledger; confirmar sigue siendo todo o nada y exige `warehouse.count`; quién ve las cantidades
del sistema sigue dependiendo del permiso de quien consulta (conteo a ciegas sin `warehouse.count`).

Quién puede (módulo **WMS_LOTSERIAL**):

| Acción | Endpoint | Permiso |
|---|---|---|
| Crear un conteo por producto | `POST /api/v1/cycle-counts` con `productPublicIds` (sin `binIds` ni `zoneIds`); con `allowEmpty: true` y un solo producto también si no tiene existencia | `warehouse.count.capture` |
| Capturar, recapturar o **corregir** una línea | `PUT /api/v1/cycle-counts/{id}/lines` y `.../lines/batch` | `warehouse.count.capture` |
| Crear una **posición provisional** desde el conteo | `POST /api/v1/cycle-counts/{id}/bins` | `warehouse.count.capture` |
| **Vista previa** de la reconciliación | `GET /api/v1/cycle-counts/{id}/reconcile-preview` | `warehouse.count` |
| Lista **Por revisar** | `GET /api/v1/cycle-counts/review` | `warehouse.count` |
| **Cerrar los que cuadran** | `POST /api/v1/cycle-counts/reconcile-matching` | `warehouse.count` |
| **Confirmar** una posición provisional | `POST /api/v1/warehouses/{publicId}/bins/{binId}/confirm-provisional` | `warehouse.manage` |
| Ver las posiciones provisionales | `GET /api/v1/warehouses/{publicId}/bins?isProvisional=true` | `inventory.view` |

**Conteo por producto.** Se crea con el almacén y los productos; el servidor arma una línea por cada posición y lote con existencia del
producto (el origen del conteo queda en `PRODUCT`, "Por producto"; con posiciones o zonas indicadas sigue siendo `MANUAL`). Cada línea trae
la posición (`binId`, `binCode`, `zoneCode`), el lote (`lotId`, `lotNumber`), el tipo de seguimiento (`trackingTypeCode`: `NONE`, `LOT` o
`SERIAL`) y el código de barras del producto. Un producto sin existencia en ninguna posición responde 400 con el mensaje de "los filtros
no seleccionan inventario", **salvo** que la solicitud lleve `allowEmpty: true` con **exactamente un producto** (sin `binIds`, `zoneIds` ni
`categoryIds`): entonces el conteo se crea **vacío** (origen `PRODUCT`, sin líneas, con su tarea COUNT) para registrar lo hallado donde el
sistema no tenía nada (así lo abre la app de almacén). Un conteo vacío acepta líneas nuevas (`PUT .../lines/batch` y `POST .../lines`, con
`binId` + producto + lote si lo lleva, también en una posición provisional), se puede borrar mientras esté abierto, no aparece en "Por
revisar" hasta tener líneas y **no se puede terminar ni reconciliar mientras siga vacío** (`El conteo no tiene líneas.`, 422). Con
`allowEmpty: false` (por defecto) nada cambia, y si el producto sí tiene existencia `allowEmpty` no cambia el resultado. Lo hallado donde el sistema no tenía nada se agrega como línea nueva (`PUT .../lines/batch` con `binId`,
`productPublicId` y, si el producto lleva lote, `lotId` o `lot.number`). Los productos con serie se cuentan por número de serie (la app
de almacén todavía no los captura; el servidor sí los admite).

**Captura original y corrección.** Cada línea guarda lo que se contó originalmente y quién y cuándo (`capturedQty`, `capturedByName`,
`capturedAtUtc`) y, si alguien la cambió después, quién y cuándo (`correctedByName`, `correctedAtUtc`, `wasCorrected`). `countedQty`
es siempre el valor **vigente**, el que se reconcilia. Reglas:

- La **primera captura** de una línea fija lo capturado originalmente.
- La **misma persona** puede volver a capturar mientras el conteo está **Pendiente**: reemplaza su captura (no es una corrección).
- Cualquier cambio hecho por **otra persona**, o por cualquiera una vez que el conteo está **Contado**, es una **corrección**: `countedQty`
  toma el valor nuevo, lo capturado originalmente se conserva y se anota quién corrigió y cuándo.
- Si el valor corregido vuelve a ser igual al capturado, la corrección se borra. Mandar otra vez el mismo valor no cambia nada.
- **Una línea corregida queda protegida** (decisión del dueño, 2026-10-03): solo quien la corrigió, o cualquier persona con `warehouse.count`,
  puede volver a cambiarla (sigue siendo una corrección y actualiza quién y cuándo). Cualquier otra persona con solo `warehouse.count.capture`
  (incluido el operario que la capturó originalmente) recibe **409** `La línea ya fue corregida por el supervisor; no se puede volver a capturar.`
  y la corrección no se toca. Reenviar el mismo valor vigente no cambia nada y no se rechaza. En la captura en lote
  (`PUT .../lines/batch`) las líneas corregidas **se omiten** y **las libres se guardan** (decisión del dueño, segundo bloque,
  2026-10-03): la respuesta es **200** con el conteo ya actualizado y un campo extra `skippedLines` (vacío/ausente si no se omitió
  ninguna) con, por cada línea omitida, `lineId`, `binCode`, `sku`, `lotNumber`, `sentQty` (lo que se mandó), `currentQty` (el valor
  vigente: el que dejó el supervisor; **nunca** la cantidad esperada, también a ciegas), `reasonCode` = `CORRECTED_BY_SUPERVISOR` y
  `message` = `La línea ya fue corregida por el supervisor; no se puede volver a capturar.` Si una línea libre del lote trae un error de
  validación (400), no se guarda nada del lote, como en toda captura. Solo cuando **todas** las líneas del lote están corregidas (no se
  pudo guardar nada) responde **409** `La línea ya fue corregida por el supervisor; no se puede volver a capturar. Renglón(es) del lote:
  2 (SKU-1). No se guardó nada.` (la lista trae el número de renglón, 1 es el primero, y el SKU). Reenviar el valor vigente de una línea
  corregida no cambia nada y no cuenta como omitida; quien corrigió y quien tiene `warehouse.count` siguen pudiendo. La captura de UNA
  línea (`PUT .../lines`) y agregar una línea no cambian: la línea corregida sigue dando 409. Las líneas sin corrección se recapturan como
  siempre. Al terminar el conteo (Contado) la regla es la misma.
- Borrar la captura (sin cantidad ni series) devuelve la línea a pendiente y borra también su evidencia.
- Una corrección **no es un ajuste ni una transferencia** y no mueve inventario: solo cambia la cantidad que se reconcilia. Toda captura y
  corrección queda en la bitácora de cambios (entidad `CYCLE_COUNT`).
- La evidencia llega también a quien consulta a ciegas: no revela lo que dice el sistema (`systemQty`, `currentQty` y los demás siguen en `null`).
- Al confirmar, el motivo del movimiento de una línea corregida lleva la evidencia, por ejemplo: `Conteo CC-00001 · contó 3 (Ana Pérez,
  2026-10-03 14:05) · corregido de 3 a 5 por Beto Ruiz (2026-10-03 15:00)` (fechas en la hora de la compañía; sin corrección el motivo es
  el de siempre, `Conteo CC-00001`).

**Vista previa de la reconciliación.** Calcula, con el mismo código que reconcilia y **sin escribir ni bloquear**, lo que pasaría si se
confirmara ahora. Por línea: posición y zona, producto, lote, existencia **actual** (`currentQty`) y reservada (`reservedQty`), lo
contado con su evidencia, el ajuste que se asentaría (`adjustmentQty`, con signo), el saldo resultante (`resultingQty`), cuántos
movimientos generaría, `systemQtyChanged` (la existencia se movió desde la foto) y el error que daría la confirmación (`error`: contado
menor que lo reservado). En productos con serie trae el plan: `serials.removals` (bajas), `serials.additions` (altas) y `serials.transfers`
(traslados). Las líneas sin contar salen con `isPending = true` (son un dato, no un error). Totales (`totals`): líneas, pendientes, con
diferencia, movimientos, líneas con error, `matches` y el estatus en que terminaría (`resultStatusCode`: `RECONCILED` o
`RECONCILED_VARIANCE`; `null` si todavía no se puede confirmar). **`matches` = el conteo cuadra**: tiene líneas, ninguna pendiente, ninguna con
error y contra la existencia actual no asentaría ningún movimiento. Un conteo ya confirmado no tiene vista previa (422).

**Por revisar.** La lista de conteos **Contados** (con `includeOpen=true`, también los Pendientes con todas sus líneas capturadas), más
recientes primero. Por conteo trae el encabezado y: quién contó (`countedByUserId`, `countedByName`; quien capturó más líneas, y
`countedByCount` personas en total), el primer producto por SKU (`firstProductSku`, `firstProductName`) y cuántos más (`otherProducts`),
`positions`, `lines`, `pendingLines`, `differingLines` (líneas que asentarían algo contra la existencia **actual**), `errorLines`, `movements`,
`correctedLines` y `matches`. Filtros: `warehousePublicId`, `countedByUserId`, `search` (número, SKU o nombre), `skip` y `take` (1 a 200, por
defecto 50). Se calcula por lotes (una consulta de líneas, saldos, productos y usuarios para toda la página).

**Cerrar los que cuadran.** `POST /api/v1/cycle-counts/reconcile-matching` con `warehousePublicId`, `ids`, `comment` e `includeOpen`
(todos opcionales). Mira los conteos Contados (o los indicados en `ids`) y **confirma solo los que cuadran**: terminan en
**Concordancia** sin asentar ningún movimiento, cada uno en su propia transacción y comprobando otra vez con los saldos bloqueados. "Cuadra"
se mide contra la existencia **actual**: una línea que se movió desde la foto y ahora asentaría algo **no** se cierra sola. Responde
`examined`, `closed` (`id`, `number`, `statusCode`, `lines`), `skipped` y `truncated`. Cada omitido trae `reasonCode`, `reason` (texto en
español) y `count`:

| `reasonCode` | Significa | `count` |
|---|---|---|
| `WouldPost` | Asentaría movimientos: queda para revisar | movimientos |
| `Errors` | Alguna línea daría error al confirmar (contado menor que lo reservado, serie repetida) | líneas con error |
| `Pending` | Tiene líneas sin contar | líneas pendientes |
| `Stale` | Cuadraba al mirarlo pero la existencia cambió antes de cerrarlo | — |
| `NotCounted` | Está Pendiente y no se pidió `includeOpen` (solo con `ids`) | — |
| `AlreadyReconciled` | Ya estaba confirmado | — |
| `NotFound` | El id no existe o no es de la compañía | — |
| `NoLines` | El conteo no tiene líneas | — |
| `Failed` | Otro error del conteo (el motivo viene en `reason`) | — |

Se miran hasta 200 conteos por llamada (`truncated = true` si había más). Los conteos de otra compañía nunca se ven ni se cierran.

**Posición provisional.** Si quien cuenta halla producto donde el sistema no tenía nada y la posición no existe, la crea desde el conteo
(`POST /api/v1/cycle-counts/{id}/bins` con `zoneId` y `code`, o `aisle`/`rack`/`level`/`position` para componer el código; mismas
validaciones que el alta de posición) sin detener el conteo. La posición queda activa y se puede usar de inmediato en el conteo y en el
inventario, pero marcada **provisional** (`isProvisional = true`, con `provisionalCycleCountId` y `provisionalCreatedAtUtc`) para que el
supervisor la revise: la **confirma** (`confirm-provisional`, quita la marca), la corrige (`PATCH`) o la desactiva (`deactivate`). El listado
de posiciones, la búsqueda de posiciones, la sincronización del aparato (`sync/bins`) y las líneas del conteo (`binIsProvisional`) la
muestran.

#### Validaciones (conteo por producto)

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `allowEmpty` con posiciones, zonas, categorías, ningún producto o más de un producto | `Crear un conteo vacío (allowEmpty) solo aplica a un único producto, sin posiciones, zonas ni categorías.` (campo `allowEmpty`) | 400 |
| `allowEmpty` con un producto que no existe (o de otra compañía) | `Producto no encontrado.` | 404 |
| Terminar o reconciliar un conteo que sigue vacío | `El conteo no tiene líneas.` | 422 |
| Vista previa de un conteo ya confirmado | `El conteo ya fue reconciliado; solo se consulta.` | 422 |
| Vista previa, por revisar o cierre de un conteo de otra compañía | `Conteo no encontrado.` (en el cierre en bloque: omitido `NotFound`) | 404 |
| Crear posición provisional en un conteo ya confirmado | `El conteo ya fue reconciliado; no admite posiciones nuevas.` | 422 |
| Posición provisional sin zona | `Indique la zona de la posición.` | 400 |
| Posición provisional sin código ni partes | `Indique el código de la posición o su pasillo/rack/nivel/posición.` | 400 |
| Código con caracteres inválidos o de más de 40 | `El código de la posición solo admite letras, números, guion y guion bajo (máximo 40).` | 400 |
| Código repetido en el almacén (también contra una posición definitiva) | `Ya existe una posición con ese código en el almacén.` | 409 |
| Zona de otro almacén o inexistente | `Zona no encontrada.` | 404 |
| Zona inactiva | `La zona está inactiva; reactívela primero.` | 422 |
| Confirmar una posición que no es provisional | `La posición no está pendiente de revisión.` | 409 |
| Cierre en bloque con más de 200 `ids` | `Se revisan como máximo 200 conteos por vez; acote por almacén o por ids.` | 400 |
| Confirmar con líneas en error (contado menor que lo reservado) | `El conteo de {sku} en {posición} ({contado}) es menor que lo reservado ({reservado}); libere la reserva antes de reconciliar.` | 409 |
| Una serie capturada en dos líneas | `La serie {serie} está capturada en más de una línea del conteo.` | 400 |
| Sin el permiso | `Falta el permiso '{código}'.` | 403 |

Los omitidos del cierre en bloque llevan estos textos en `reason`: `Asentaría {n} movimiento(s); revíselo.`, `Faltan {n} línea(s) por
contar.`, `El conteo todavía no se termina de contar.`, `La existencia cambió mientras se cerraba; revíselo.`, `El conteo no existe.`,
`El conteo ya fue reconciliado; solo se consulta.` y `El conteo no tiene líneas.`

#### Estatus y efectos (conteo por producto)

No hay estatus nuevos. Corregir una cantidad no cambia el estatus ni mueve inventario; el cierre en bloque lleva cada conteo que cuadra de
**Contado** (o **Pendiente** con `includeOpen`) a **Concordancia** con la misma transición que "Confirmar conteo y ajustar" (historial,
tarea `COUNT` en `DONE`, `reconciledSystemQty` por línea). Un conteo que no cuadra no cambia. Crear una posición provisional solo se
permite con el conteo sin confirmar (Pendiente o Contado).

---

### 6.x Conteo abierto con varios productos y posición opcional (Lote 25)

Qué es: un conteo (origen **Por producto**, `PRODUCT`) que se **abre vacío** y al que se le van agregando productos, cada uno con su cantidad y
su posición. La posición sigue siendo parte de la línea (no cambia el esquema ni el ajuste al reconciliar); lo nuevo es que **quien cuenta no
tiene que elegirla**: el servidor propone la única posición donde el sistema tiene el producto.

Quién puede: `warehouse.count.capture` (abrir, consultar dónde está un producto, agregar y capturar); `warehouse.count` para reconciliar, como siempre.
Módulo **WMS_LOTSERIAL**.

**Abrir un conteo vacío.** `POST /api/v1/cycle-counts` con `{ "warehousePublicId": …, "allowEmpty": true }` y **sin productos, posiciones, zonas ni
categorías** crea un conteo `OPEN` de origen `PRODUCT` **sin líneas** (no es "todo el almacén") y su tarea COUNT. Con **un** producto y `allowEmpty`
funciona como antes (Lote 21, adenda). Con más de un producto, o con otros filtros, es 400 `allowEmpty`:
`Crear un conteo vacío (allowEmpty) solo aplica a uno o ningún producto, sin posiciones, zonas ni categorías.`

**Dónde está el producto.** `GET /api/v1/cycle-counts/{id}/product-bins?productPublicId=…` (`warehouse.count.capture`) devuelve una fila por
**posición y lote con existencia** en una posición activa del almacén del conteo: `binId`, `binCode`, `zoneCode`, `lotId`, `lotNumber` y `lineId` (la
línea que el conteo ya tiene ahí; `null` si todavía no). **Nunca lleva cantidades**, así que sirve igual al conteo a ciegas. 404 `Producto no
encontrado.` / `Conteo no encontrado.`

**Agregar una línea sin posición.** `POST /api/v1/cycle-counts/{id}/lines` ya no exige `binId`:
- una sola posición con existencia del producto (con el lote pedido, si se pidió) → se usa esa;
- ninguna → 400 en `binId`: `El producto {sku} no tiene existencia en ninguna posición del almacén; indique la posición donde lo encontró.`;
- varias → 400 en `binId`: `El producto {sku} está en varias posiciones ({códigos}); indique en cuál lo contó.`
Con `binId` todo sigue igual (404 posición, 422 posición inactiva, 409 si esa posición, producto y lote ya están en el conteo). El lote de captura
`PUT /lines/batch` **sí** sigue pidiendo `binId` por renglón nuevo (la app lo resuelve antes de encolar).

Casos frecuentes:
- *"Escanee dos veces el mismo producto."* Misma posición y lote: 409 `Esa posición, producto y lote ya están en el conteo.` (las pantallas abren la
  línea ya contada para corregir la cantidad; no suman ni duplican).
- *"El producto está en dos posiciones y conté en una."* Se agrega esa línea; la otra no cuenta: el ajuste al reconciliar solo toca las líneas del conteo.

Pantallas: app de almacén ([09 §7.2](09-app-almacen.md#72-contar-varios-productos-en-un-conteo-lote-25)) y web (Conteo cíclico → Nuevo conteo → Por producto sin
elegir producto; escáner del detalle).

### 6.w Calculadora de cantidad en el conteo de la web (2026-10-07)

En el panel del conteo (Conteo cíclico), junto a la cantidad de cada línea (no series) hay un botón **∑** («Calculadora de la línea {n}») y, dentro de la ventana de **Cantidad contada**, el botón **Calculadora**. Abre la misma forma de contar estibas que la app:
cada **bloque** es **Filas × Columnas × Fondo** (el fondo es cuántas hay una detrás de otra; en blanco vale 1) y se suman las **Sueltas**; **+ otro bloque** agrega otra estiba (✕ la quita).
Debajo salen la cuenta (`(5 × 3 × 2) + 10`) y el **Total**, que va llenando la cantidad a medida que se escribe; **Cantidad directa** vuelve al campo con ese número y **Guardar** (o Enter) guarda la cantidad como siempre (`PUT /cycle-counts/{id}/lines`): solo se guarda la cantidad, no la fórmula.

| Caso | Mensaje exacto |
|---|---|
| Un bloque con filas o columnas pero no ambas | `Cada bloque necesita filas y columnas.` |
| Algo que no es entero en filas, columnas o fondo (el fondo, 1 o más), o que no es número en sueltas | `Escriba números enteros en filas, columnas y fondo (el fondo, 1 o más) y un número en sueltas.` |
| Total mayor que 9 999 999 | `El total es demasiado grande.` |

Quién puede: quien puede capturar el conteo en la web (`warehouse.count`, conteo abierto). Módulo **WMS_LOTSERIAL**.

### 6.z Conteo informado al capturar: quién ve lo esperado al contar (tarea 25)

Qué hace: separa **ver lo esperado** del permiso **Contar**. El supervisor (permiso `warehouse.count`) lo ve desde el inicio, como siempre. Un **contador**
(solo `warehouse.count.capture`) puede ver, **después de aceptar la cantidad de cada línea**, si coincidió con lo esperado; lo esperado nunca se le muestra
antes de contar. Fuera del margen se le pide **recontar** sin decirle el esperado; tras recontar (o al coincidir) la línea se **cierra** y el contador ya no
puede cambiar esa cifra.

Quién lo configura: `admin.tenant` (la compañía) y `admin.users` (cada persona); módulo **WMS_LOTSERIAL**.

**Ajuste de la compañía** (Sistema → Ajustes → **Operación** → «Conteo cíclico: lo esperado al contar»; `PUT /api/v1/tenant/settings`):

| Campo | Valores | Qué hace |
|---|---|---|
| ¿Quién ve lo esperado al contar? (`countExpectedReveal`) | **Nadie** (`NONE`) · **Solo los marcados** (`MARKED`, valor por defecto) · **Todos** (`ALL`) | Nadie: nadie al contar en la app, **tampoco el supervisor** (la reconciliación en la web sigue mostrando lo esperado). Solo los marcados: los contadores con «Sí» en Usuarios. Todos: todos los contadores salvo los marcados con «No». |
| Margen para no pedir reconteo (%) (`countRecountTolerancePct`) | 0 a 100 (por defecto 0) | 0 = cualquier diferencia pide reconteo. Con 5, una cantidad dentro del 5 % de lo esperado se da por buena. |
| Mostrar el número esperado (`countRevealShowsNumber`) | Sí (por defecto) / No | Con No solo se dice «Coincide» o «No coincide». |

**Marca por persona** (Sistema → Usuarios → acciones de la fila → «Ve lo esperado al contar: Sí / No / sin marcar»; `PUT /api/v1/users/{id}/count-see-expected`
con `{ "value": true | false | null }`): la columna «Ve lo esperado al contar» muestra Sí / No / —. Solo cuenta cuando la compañía está en «Solo los marcados» o «Todos».

| Compañía \ Usuario | sin marcar | No | Sí |
|---|---|---|---|
| **Nadie** | no ve | no ve | no ve |
| **Solo los marcados** | no ve | no ve | ve al capturar |
| **Todos** | ve al capturar | no ve | ve al capturar |

Cómo funciona (servidor): `POST /api/v1/cycle-counts/{id}/lines/{lineId}/check` con `{ "countedQty": n }` (permiso `warehouse.count.capture`). Responde `state`:
**MATCH** (dentro del margen; línea cerrada), **RECOUNT** (fuera del margen: recontar; **no** trae lo esperado) o **FINAL** (ya recontó; cerrada). `expectedQty` solo
llega con MATCH/FINAL y si la compañía muestra el número. Se guardan la primera y la última cifra verificadas (`firstCheckQty`, `lastCheckQty`; quedan en la bitácora de
cambios). Después de verificar, la captura de esa línea (`PUT …/lines` y `PUT …/lines/batch`) **solo acepta la última cifra verificada** (409 si es otra); quien tiene
`warehouse.count` siempre puede corregir. La ficha del conteo trae `reveal`: `FULL` (supervisor), `AT_CAPTURE` (contador habilitado) o `NONE`.

| Caso | Mensaje exacto | Código |
|---|---|---|
| El contador no está habilitado (compañía en «Nadie» o sin marcar/marcado «No») | `No está habilitado ver lo esperado al contar.` | 403 |
| La línea ya está cerrada (coincidió o ya se recontó) | `La línea ya se verificó; no se puede cambiar su cantidad.` | 409 |
| La línea ya la corrigió el supervisor | `La línea ya fue corregida por el supervisor; no se puede volver a capturar.` | 409 |
| Se intenta guardar otra cantidad distinta de la verificada | `La cantidad no es la que se verificó en la línea; no se puede cambiar después de ver el resultado.` | 409 |
| Producto con serie | `Los productos con serie no se verifican contra lo esperado.` | 400 (`lineId`) |
| Cantidad negativa o con más de 3 decimales | `La cantidad contada no puede ser negativa.` / `La cantidad admite como máximo 3 decimales.` | 400 (`countedQty`) |
| Margen fuera de 0–100 | `El margen de reconteo debe estar entre 0 y 100 %.` | 400 (`countRecountTolerancePct`) |
| Modo desconocido | `Modo desconocido: '{valor}'. Use NONE, MARKED o ALL.` | 400 (`countExpectedReveal`) |
| Conteo ya terminado o reconciliado | (los de siempre del conteo) | 422 |

Notas: con «Solo los marcados» o «Todos», un supervisor que también cuenta ve lo esperado desde el inicio (para contar a ciegas debe usar una persona o rol de solo captura). Con «Nadie»
el conteo desde la app llega a ciegas también para el supervisor (la app manda `forCounting=true` en `POST /cycle-counts` y `GET /cycle-counts/{id}`; la web no, y sigue viendo todo para reconciliar) y `…/check` responde 403 a todos. Los pasos de la app están en [09 §7.4](09-app-almacen.md#74-conteo-informado-al-capturar-tarea-25).

### 4.2 Reparto por posición en la web: recibo directo, Acomodar y despacho (2026-10-07)

La web ofrece lo mismo que la app de almacén para repartir cantidades grandes entre posiciones.

**Regla del reparto** (igual en servidor, app y web): con *N por posición* se llenan posiciones completas y, si sobra, **una posición más** recibe el resto; no se admiten más posiciones después de esa (`Con {per} por posición caben {max} posición(es) para {pending}; no hay más unidades por repartir.`, 400). Un aviso fijo en pantalla (se puede cerrar) avisa cuando el resto cae en la posición extra; cada fila tiene ✕ para quitarla. Si una posición no tiene espacio libre suficiente solo **avisa**, no bloquea.

- **Acomodar (tareas de almacén)** → al completar una tarea, interruptor «Repartir por posición»: cantidad por posición + posiciones (con buscador). Usa el endpoint `distribute` de la tarea. Permiso: el mismo de completar la tarea.
- **Recibo directo** → en la línea del recibo abierto, acción **Repartir** (icono de cuadrícula): la primera línea conserva lo recibido y su posición; las demás se agregan como líneas nuevas. Solo recibos ciegos o de devolución, productos sin serie. Se hace con llamadas seguidas (no es una sola transacción): si una falla, el mensaje lo indica y las anteriores quedan guardadas.
- **Despacho / recolección** → en una línea con producto y cantidad y sin posición, acción **Sugerir posiciones**: propone de qué posiciones sacar (por el orden de salida del almacén, respetando el lote si se indicó y lo ya asignado en otras líneas) y deja una línea por posición. Errores: `Elija el producto y escriba la cantidad para sugerir de dónde sacarla.`; `No alcanza la existencia: faltan {short}. Baje la cantidad.` No aplica a productos con serie.

### 6.y Equipos en renta y el conteo cíclico (Lote 28, Rentas R2, decisión D7)

Los equipos rentados siguen en el inventario, en la posición **EN-RENTA** (zona RENT, tipo "En renta"; capítulo 11). Esa posición
**no se cuenta** y una serie en renta encontrada en un conteo **bloquea** la reconciliación hasta registrar su devolución:

| Caso | Qué pasa | Mensaje | HTTP |
|---|---|---|---|
| Crear un conteo pidiendo la posición EN-RENTA o la zona RENT (`binIds`/`zoneIds`), o un conteo de "lo cambiado" con la zona RENT, o agregar una línea en EN-RENTA | No se crea / no se agrega | `La posición {bin} es de equipos en renta; no se cuenta.` | 422 |
| Conteo de todo el almacén, por producto o de "lo cambiado" sin zona | La posición EN-RENTA se salta sin error; `product-bins` y la posición por defecto tampoco la ofrecen | — | — |
| Se captura en otra posición una serie que está **En renta** | La vista previa marca la línea con el error y **reconciliar** responde 409 sin mover nada | `La serie {s} está en renta ({n}); registre su devolución antes de reconciliar el conteo.` ({n} = número REN de la renta abierta) | 409 |

Qué hacer: si el equipo de verdad volvió, registre su **devolución de renta** (capítulo 11 §5) y vuelva a capturar el conteo; si se
capturó por error, quite la serie de la captura y reconcilie.

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
- Lote 16 (selector de Posición de la captura): con un producto elegido, la lista ofrece **solo las posiciones donde ese producto tiene existencia disponible** (y, si se eligió lote, solo
  las de ese lote), cada una con lo disponible (`P-01 · PCK · 9 disp.`), en el mismo orden FEFO que usa el servidor al recolectar; la primera va marcada "Sugerida". Sin producto, el campo está
  apagado. Es una ayuda de pantalla: el API no cambió y sigue aceptando cualquier posición (si no alcanza, responde `Inventario insuficiente…`, 409).

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
resolver el faltante pendiente de una línea (cerrar, reordenar o hacer un ajuste manual de inventario). Desde el Lote 14 los
faltantes se resuelven **solo desde la pestaña Faltantes de la ficha de la orden de compra**: la pantalla "Ajustes de inventario"
(`/warehouse/inventory-adjustments`, que mostraba las compras con recibo parcial) salió del menú y esa dirección lleva a Compras. Los
ajustes de inventario en sí están en la sección 3 (Transferencias y ajustes).

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

**Recibos directos (Lote 16).** La asignación contra un recibo **ya confirmado** (el modo "b" de las asignaciones) depende del acomodo pendiente: reduce la tarea `PUTAWAY` que queda por hacer. Un recibo
**directo** confirmado no tiene tareas de acomodo (la mercancía ya está en su posición destino), así que **no admite** asignaciones de cruce de muelle después de confirmar. Por el código, el servidor
responde 422 `El recibo de la línea no admite asignaciones (eliminado o sin putaway pendiente).` si la línea no tiene posición de recepción, o 409 `La cantidad excede lo disponible para cruce de muelle (0).`
(no se probó en un recibo real). Para cruzar mercancía en un almacén directo, asigne el cruce **mientras el recibo está abierto** (el modo "a" de las asignaciones): la línea con cruce entra a la posición de recepción, no exige destino y su remanente genera la
tarea de acomodo hacia su destino (sección 4.1).

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
| `inventory.adjust` | WAREHOUSE | Ajustar/transferir inventario, conciliar ("Ejecutar conciliación", estado de la revisión automática), resolver descuadres, resolver faltantes de compra |
| `warehouse.manage` | WAREHOUSE | Gestionar almacenes, zonas, posiciones, muelles; asignar/cancelar tareas |
| `warehouse.receive` | WAREHOUSE | Recibir mercancía (ASN y recibos); completar tareas `PUTAWAY` |
| `warehouse.pick` | WAREHOUSE | Recolectar y empacar; completar tareas `REPLENISH`; correr el reabasto |
| `warehouse.count` | WAREHOUSE | Conteo cíclico completo, incluidas la confirmación ("Confirmar conteo y ajustar"), "Conteo de lo cambiado" y, desde el Lote 21, la vista previa de la reconciliación, "Por revisar" y "Cerrar los que cuadran" |
| `warehouse.count.capture` | WAREHOUSE | Lote 8A: contar a ciegas (alta, captura, lo encontrado y terminar) sin ver lo esperado ni reconciliar; implícito en `warehouse.count`. Lote 21: también crear una posición provisional desde el conteo |
| `warehouse.crossdock` | WAREHOUSE | Citas y planes de cruce de muelle; completar tareas `CROSSDOCK` |
| `purchasing.view` | PURCHASING | Ver proveedores y órdenes de compra |
| `purchasing.manage` | PURCHASING | Gestionar proveedores y órdenes de compra |
| `purchasing.receive` | PURCHASING | Recibir mercancía contra una orden de compra (existía desde antes de este lote) |

Plantillas de rol: `inventory.view` lo reciben además el Operador de almacén, Solo lectura y Facturación; los
otros tres permisos `WAREHOUSE` nuevos (`inventory.manage`, `inventory.adjust`, `warehouse.manage`) solo los tiene
el administrador del tenant por defecto.

Módulos: **WMS_LOTSERIAL** y **PURCHASING** vienen encendidos por defecto; **CROSSDOCK** apagado (demo).

Lote 14: un permiso nuevo, `pulse.attention` (categoría PULSE), para ver "Necesita tu atención" en el Pulso (capítulo 07, sección 4);
la plantilla del Operador de almacén, Facturación, Solo lectura y el administrador lo traen, y una sola vez se propagó a los roles
de compañía que ya tenían `inventory.view`. Los descuadres los ve `inventory.view` y los resuelve `inventory.adjust`; "lo cambiado" pide
`warehouse.count`. `GET /api/v1/warehouses/bins/search` pide `inventory.view`. El total es de 66 permisos.

Lote 16: no hay permisos ni módulos nuevos (el total de permisos no cambia). `GET /api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions` pide `inventory.view`; `POST
/api/v1/receipts/{publicId}/targets/suggest`, `warehouse.receive`; el modo de recepción del almacén y su posición por defecto, `warehouse.manage`; el modo del recibo y el
destino de cada línea, `warehouse.receive`. La prueba de seguridad de controladores pasa de 121 a 123 acciones.

Informe Productos por posición: no hay permisos ni módulos nuevos. `GET /api/v1/warehouses/{publicId}/bin-products` pide `inventory.view`, módulo
**WMS_LOTSERIAL** (reemplaza a `bin-sheets` y `bin-sheets/mark-printed` del Lote 23). La prueba de seguridad de controladores pasa de 130 a 129 acciones.

Lote 26 (Rentas R0): `POST /api/v1/products/{publicId}/convert-to-serial` pide `inventory.manage` en el controlador e `inventory.adjust` en el
servicio, módulo **WMS_LOTSERIAL**. No hay permisos ni módulos nuevos; la prueba de seguridad de controladores pasa de 130 a 131 acciones.

Lote 12: `GET /api/v1/products/brands` pide `inventory.view` (como la lista de productos) y `POST
/api/v1/warehouses/{publicId}/bins/capacity` pide `warehouse.manage` (como editar una posición). No hay permisos nuevos.

Excepción (Lote 11): `GET /api/v1/postal-localities` (catálogo de ciudades y códigos postales) no pide permiso ni módulo, solo una
sesión iniciada. La pantalla solo lo consulta cuando alguien con `warehouse.manage` abre el selector de ciudad al crear o
editar un almacén.
