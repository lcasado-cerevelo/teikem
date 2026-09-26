# Capítulo 04 — Flota, choferes y mantenimiento (Lote 4)

Este capítulo describe los cinco paneles del módulo de Flota (Vehículos, Documentos por vencer, Mantenimiento
preventivo, Órdenes de trabajo y Bitácora de combustible) y el maestro-detalle **Choferes y tarifas** del grupo
Catálogo (identidad del chofer, licencias, certificaciones, dispositivos, zonas de despacho y las tres tarifas de
pago). Cierra también el pendiente del [capítulo 03](03-ordenes-de-transporte.md): la entrega especial con chofer.
Cada sección indica **qué hace**, **quién puede**, **cómo se usa**, las **validaciones** con el mensaje exacto y el
código HTTP, y los **estatus** con sus transiciones y efectos. Los mensajes están verificados contra el código
(`src/Teikem.Domain/Fleet/*`, `src/Teikem.Infrastructure/Fleet/*`, `src/Teikem.Infrastructure/Services/{Vehicle,Driver,
DispatchZone,Fleet,Maintenance,FuelLog,DriverRate,DriverPayPolicy,DriverTrip,SpecialDelivery}*.cs`,
`src/Teikem.Api/Controllers/{Vehicles,Drivers,DispatchZones,Fleet,MaintenanceSchedules,MaintenanceWorkOrders,
FuelLogs,DriverRates,DriverPayPolicy,DriverTrips,SpecialDelivery}Controller.cs`). Las preguntas y respuestas de cada
mensaje están en [faq.md](faq.md).

Convenciones del capítulo:

- Los recursos con historial de estatus (vehículo, chofer, orden de trabajo, viaje pagado) se identifican por
  `publicId` (GUID) en la URL. Un `publicId` de otra compañía responde **404** como si no existiera (sin oráculo).
- Los hijos sin identidad propia (documentos de vehículo, licencias, certificaciones, dispositivos, tareas de OT) se
  exponen con **id entero solo bajo la ruta de su padre**: un id que pertenece a otro padre (u otro tenant) también
  responde **404** (protección contra BOLA por id hijo).
- Todo el módulo vive bajo el módulo **CATALOG** ("Clientes y contratos, choferes y tarifas, flota"): si el tenant lo
  apaga, cualquier endpoint de este capítulo responde `403` `code: "module_disabled"`,
  `El módulo 'CATALOG' no está habilitado para esta compañía.` — salvo el propio asignar/reasignar chofer de una
  entrega especial, que vive bajo **LTL_GROUND** (sección 12) pero de todos modos exige CATALOG encendido por dentro.
- Flota y compensación de choferes van separadas por permiso (R8): `fleet.view`/`fleet.manage`/`fleet.maintenance`
  para vehículos, documentos y mantenimiento; `driverpay.view`/`driverpay.manage` para tarifas y viajes pagados.
- Un error de validación responde `400` con `title` y `errors` (`{ campo: [mensaje] }`); un conflicto `409`; una
  regla de estatus o de negocio `422`; un recurso ajeno o inexistente `404`; falta de permiso `403`.
- Nada se borra físicamente: los catálogos y las hijas usan `IsActive` (reversible); vehículo, chofer, orden de
  trabajo y viaje pagado usan además un ciclo de estatus con una etapa terminal (baja definitiva).

---

## 1. Vehículos

### 1.1 Lista y ficha

Qué hace: lista los vehículos del tenant con buscador libre (código, placa, VIN, marca, modelo y las etiquetas o
códigos internos de tipo/propiedad/combustible, sin distinguir acentos ni mayúsculas) y filtros multi-valor por
código de catálogo (`vehicleType`, `ownership`, `fuelType`, `status`; acepta `?x=A&x=B` o `?x=A,B`). Cada fila trae
`nextDocumentExpiry`: el próximo vencimiento entre los documentos **activos y no superados** del vehículo (ver
sección 3). La ficha agrega capacidades, volumen, odómetro actual y sus documentos.

Quién puede: `fleet.view`.

Cómo se usa:
- `GET /api/v1/vehicles?search=&vehicleType=&ownership=&fuelType=&status=&includeInactive=false`
- `GET /api/v1/vehicles/{publicId}`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Filtro `vehicleType`/`ownership`/`fuelType` con código desconocido | `Tipo de vehículo desconocido: 'X'.` / `Propiedad desconocida: 'X'.` / `Tipo de combustible desconocido: 'X'.` | 400 |
| Filtro `status` con código desconocido | `Estatus desconocido: 'X'.` (campo `status`) | 400 |
| `publicId` de otro tenant o inexistente | `Vehículo no encontrado.` | 404 |

### 1.2 Alta

Qué hace: crea un vehículo. El código (`code`) es obligatorio, se guarda en mayúsculas y es **inmutable**; el
vehículo nace en el estatus inicial (**ACTIVE**) con `isActive: true`. El VIN, si se captura, se normaliza a
mayúsculas sin espacios.

Quién puede: `fleet.manage`.

Cómo se usa: `POST /api/v1/vehicles`

```json
{
  "code": "V-001",
  "plateNumber": "ABC-1234",
  "vehicleType": "VAN",
  "ownership": "OWNED",
  "fuelType": "DIESEL",
  "make": "Ford",
  "model": "Transit",
  "modelYear": 2023,
  "vin": "1FTBW2CM0NKA00000",
  "maxWeightKg": 1500,
  "maxVolumeM3": 12.5,
  "maxStops": 40,
  "currentOdometerKm": 1000
}
```

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `code` vacío | `El código del vehículo es obligatorio.` (campo `code`) | 400 |
| `code` de más de 30 caracteres | `El código no puede exceder 30 caracteres.` (campo `code`) | 400 |
| `code` repetido (aunque el otro esté dado de baja) | `Ya existe un vehículo con ese código.` | 409 |
| `vehicleType`/`ownership`/`fuelType` desconocidos | `Tipo de vehículo desconocido: 'X'.` / `Propiedad desconocida: 'X'.` / `Tipo de combustible desconocido: 'X'.` | 400 |
| `vin` de más de 40 caracteres | `El VIN admite como máximo 40 caracteres.` (campo `vin`) | 400 |
| `vin` repetido entre vehículos **activos** | `Ya existe un vehículo activo con ese VIN.` | 409 |
| `plateNumber`/`make`/`model` de más de 20/60/60 caracteres | `No puede exceder {N} caracteres.` | 400 |
| `maxWeightKg`/`maxVolumeM3` negativos | `La capacidad no puede ser negativa.` | 400 |
| `maxStops` &lt; 1 | `El tope de paradas debe ser mayor o igual a 1.` | 400 |
| `currentOdometerKm` negativo | `El odómetro no puede ser negativo.` | 400 |
| `modelYear` fuera de 1900..(año en curso + 1) | `El año del modelo debe estar entre 1900 y {año+1}.` | 400 |
| `maxWeightKg`/`maxVolumeM3`/`currentOdometerKm` con demasiados decimales o fuera de rango DECIMAL(12,3)/(12,4)/(12,1) | `El valor admite como máximo {N} decimales y debe ser menor que {límite}.` | 400 |

### 1.3 Edición en línea

Qué hace: `PATCH` con `null` = sin cambio; acepta `rowVersion` opcional para detectar ediciones concurrentes. El
código no se edita nunca. Si llega `currentOdometerKm`, la fila del vehículo se bloquea (`UPDLOCK, ROWLOCK`) y la
lectura debe ser **mayor o igual** a la última registrada por una carga de combustible activa o una orden de
trabajo **CLOSED** (secciones 9 y 8): así una corrección manual nunca deja el odómetro por debajo de un hecho ya
registrado, ni compite con una carga o un cierre de OT concurrentes.

Quién puede: `fleet.manage`.

Cómo se usa: `PATCH /api/v1/vehicles/{publicId}`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `code`/`homeWarehouseId` en el cuerpo | `El código del vehículo se fija al crearlo; no se puede cambiar.` | 400 |
| `vin` repetido entre activos (excluyendo el propio) | `Ya existe un vehículo activo con ese VIN.` | 409 |
| `currentOdometerKm` menor que la última lectura registrada | `El odómetro no puede ser menor que la última lectura registrada ({km} km el yyyy-MM-dd).` (campo `currentOdometerKm`) | 400 |
| `rowVersion` no coincide (alguien más lo editó) | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Mismas validaciones de capacidades, año, VIN y precisión que el alta | (igual que 1.2) | 400 |

### 1.4 Estatus y baja lógica

Qué hace: cambia el estatus del vehículo (`VehicleStatus`: **ACTIVE** ↔ **MAINTENANCE**, lateral y reversible;
**INACTIVE**, terminal = baja definitiva) y el checkbox **Activo** (`IsActive`, reversible, no borra nada). Ver la
tabla de transiciones en la sección 11.1.

Quién puede: `fleet.manage`.

Cómo se usa:
- `POST /api/v1/vehicles/{publicId}/status` — `{ "toCode": "MAINTENANCE", "comment": "…" }`
- `POST /api/v1/vehicles/{publicId}/deactivate` / `.../reactivate` — 204, sin cuerpo

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `toCode` vacío | `El estatus destino es obligatorio.` (campo `toCode`) | 400 |
| Reactivar un vehículo en estatus terminal (INACTIVE) | `El vehículo está dado de baja definitiva; no se puede reactivar.` | 409 |
| Reactivar y el VIN ya lo usa otro vehículo activo | `Ya existe un vehículo activo con ese VIN.` | 409 |

### 1.5 Documentos del vehículo (registro, seguro, inspección, permiso)

Qué hace: alta, edición y baja lógica de documentos del vehículo (`VehicleDocType`: `REGISTRATION`, `INSURANCE`,
`INSPECTION`, `PERMIT`). Cada documento trae su estado de vencimiento (`OK` / `EXPIRING` ≤ 30 días / `EXPIRED` /
`NO_EXPIRY` sin fecha) y `isSuperseded`: **true** si el mismo vehículo tiene otro documento **activo del mismo
tipo** con vencimiento posterior — un documento superado no bloquea, no cuenta para `nextDocumentExpiry` y no
aparece en "Documentos por vencer" (sección 3), pero sigue visible en la ficha. Los adjuntos (archivo) no se
exponen en este lote: no hay proveedor de archivos.

Quién puede: `fleet.view` para listar, `fleet.manage` para escribir.

Cómo se usa:
- `GET /api/v1/vehicles/{publicId}/documents?includeInactive=false`
- `POST /api/v1/vehicles/{publicId}/documents` — `{ "docType": "INSURANCE", "docNumber": "...", "issuedDate": "2026-01-01", "expiryDate": "2027-01-01" }`
- `PATCH /api/v1/vehicles/{publicId}/documents/{id}` — `null` = sin cambio; `clearIssuedDate`/`clearExpiryDate` vacían la fecha
- `POST /api/v1/vehicles/{publicId}/documents/{id}/deactivate`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `docType` vacío | `El tipo de documento es obligatorio.` (campo `docType`) | 400 |
| `docType` desconocido | `Tipo de documento de vehículo desconocido: 'X'.` (campo `docType`) | 400 |
| `docNumber` de más de 80 caracteres | `No puede exceder 80 caracteres.` (campo `docNumber`) | 400 |
| `expiryDate` anterior a `issuedDate` | `La fecha de vencimiento no puede ser anterior a la de emisión.` (campo `expiryDate`) | 400 |
| `id` de un documento de otro vehículo o de otro tenant | `Documento no encontrado.` | 404 |

---

## 2. Choferes (identidad, licencias, certificaciones, dispositivos)

### 2.1 Alta y ficha

Qué hace: crea un chofer. El código (`code` = `EmployeeCode`) es obligatorio, se guarda en mayúsculas y es
**inmutable**; el nombre es obligatorio (máximo 150 caracteres). El chofer nace en el estatus inicial (**ACTIVE**)
**sin ninguna fila de tarifa** (la "ficha vacía" es la ausencia de filas, no una fila en $0). Opcionalmente admite
zona de despacho primaria, tope de paradas propio y el vínculo con un usuario interno (sección 2.3).
`EffectiveMaxStops` en la lista y la ficha es el tope propio o, si no tiene, el default del tenant
(`Tenant.MaxStopsPerRouteDefault`).

Quién puede: `fleet.view` para listar y ver la ficha; `fleet.manage` para crear.

Cómo se usa:
- `GET /api/v1/drivers?search=&status=&dispatchZoneId=&includeInactive=false`
- `GET /api/v1/drivers/{publicId}`
- `POST /api/v1/drivers`

```json
{ "code": "D-001", "fullName": "Juan Pérez", "dispatchZoneId": 1, "maxStopsPerRoute": null, "hireDate": "2024-03-01" }
```

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `code` vacío | `El código del chofer es obligatorio.` (campo `code`) | 400 |
| `code` de más de 30 caracteres | `El código no puede exceder 30 caracteres.` (campo `code`) | 400 |
| `code` repetido (aunque el otro esté eliminado) | `Ya existe un chofer con ese código.` | 409 |
| `fullName` vacío | `El nombre del chofer es obligatorio.` (campo `fullName`) | 400 |
| `fullName` de más de 150 caracteres | `El nombre del chofer admite como máximo 150 caracteres.` (campo `fullName`) | 400 |
| `maxStopsPerRoute` &lt; 1 | `El tope de paradas debe ser mayor o igual a 1.` (campo `maxStopsPerRoute`) | 400 |
| `dispatchZoneId` de otro tenant o inexistente | `Zona de despacho no encontrada.` | 404 |
| `dispatchZoneId` inactivo | `La zona de despacho está inactiva.` (campo `dispatchZoneId`) | 400 |
| `publicId`/`code` de un chofer que no existe en el tenant | `Chofer no encontrado.` | 404 |
| Filtro `status` con código desconocido | `Estatus de chofer desconocido: 'X'.` (campo `status`) | 400 |

### 2.2 Edición en línea

Qué hace: `PATCH` con `null` = sin cambio; `clearZone`/`clearMaxStopsPerRoute`/`clearUser` vacían el dato
correspondiente (ganan si llegan junto con el valor nuevo). El código nunca se edita. Editar sobre un chofer en
estatus terminal (eliminado) responde 409 antes de tocar nada.

Quién puede: `fleet.manage` (cambiar o quitar el usuario vinculado exige además `admin.users`, sección 2.3).

Cómo se usa: `PATCH /api/v1/drivers/{publicId}`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `code`/`employeeCode` en el cuerpo | `El código del chofer se fija al crearlo; no se puede cambiar.` | 400 |
| Chofer eliminado (estatus terminal) | `El chofer fue eliminado; solo se consulta su historial.` | 409 |
| Mismas validaciones de nombre, tope de paradas y zona que el alta | (igual que 2.1) | 400 / 404 |
| `rowVersion` no coincide | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |

### 2.3 Vínculo con un usuario interno (para la futura app del chofer)

Qué hace: liga el chofer a un usuario de la compañía (`Driver.UserId`, único por tenant). El usuario debe: existir
con membresía en el tenant, ser de tipo **interno** (no de portal), tener membresía **ACTIVE**, y no estar ya
vinculado a otro chofer. El servicio exige el permiso `admin.users` ANTES de tocar nada, tanto para vincular como
para desvincular (`clearUser`). Solo se revalida el vínculo cuando realmente cambia (reenviar el mismo `userId` no
exige `admin.users` de nuevo).

Quién puede: `fleet.manage` + `admin.users`.

Cómo se usa: incluir `userId` (o `clearUser: true`) en `POST /api/v1/drivers` o `PATCH /api/v1/drivers/{publicId}`.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Falta `admin.users` | `Falta el permiso 'admin.users'.` | 403 |
| `userId` sin membresía en el tenant (o de otra compañía) | `Usuario no encontrado.` | 404 |
| `userId` es un usuario de **portal**, no interno | `Solo un usuario interno se puede vincular a un chofer.` (campo `userId`) | 400 |
| Membresía del usuario no está **ACTIVE** | `El usuario no tiene una membresía activa en esta compañía.` | 409 |
| `userId` ya vinculado a otro chofer | `El usuario ya está vinculado a otro chofer.` | 409 |

### 2.4 Estatus, checkbox Activo y "Eliminar chofer"

Qué hace: tres acciones distintas.
- Cambio de estatus (`DriverStatus`: **ACTIVE** ↔ **UNAVAILABLE**, lateral).
- Checkbox **Activo** (`IsActive`), reversible: saca al chofer de despacho sin tocar nada más.
- **Eliminar chofer** (`DELETE`): transiciona al estatus terminal **INACTIVE** (baja definitiva) sin borrado físico.
  Sus efectos (sección 11.2): queda `IsActive=0`, se desvincula el usuario, se desactivan sus dispositivos, se le
  quita la zona y se cierran **hoy** sus tarifas abiertas (sección 8). Sus viajes pagados (sección 10) se conservan
  para pagarle lo pendiente. Un chofer eliminado solo se consulta.

Quién puede: `fleet.manage`.

Cómo se usa:
- `POST /api/v1/drivers/{publicId}/status` — `{ "toCode": "UNAVAILABLE" }`
- `POST /api/v1/drivers/{publicId}/deactivate` / `.../reactivate` — 204
- `DELETE /api/v1/drivers/{publicId}` — cuerpo opcional `{ "comment": "…" }`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `toCode` vacío | `El estatus destino es obligatorio.` (campo `toCode`) | 400 |
| Reactivar un chofer eliminado | `El chofer fue eliminado; no se puede reactivar.` | 409 |
| Eliminar un chofer ya eliminado | `El chofer ya fue eliminado.` | 409 |

### 2.5 Licencias y certificaciones

Qué hace: alta, edición y baja lógica de licencias (`LicenseClass`) y certificaciones (`CertificationType`) del
chofer. Igual que los documentos de vehículo: `expiryState`, `daysToExpiry` e `isSuperseded` (vigente por tipo:
misma clase/tipo, vencimiento posterior en otra fila activa del mismo chofer).

Quién puede: `fleet.view` para listar, `fleet.manage` para escribir.

Cómo se usa:
- `GET/POST /api/v1/drivers/{publicId}/licenses`, `PATCH .../licenses/{id}`, `POST .../licenses/{id}/deactivate`
- Mismas rutas bajo `/certifications`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `licenseClass` vacío | `La clase de licencia es obligatoria.` (campo `licenseClass`) | 400 |
| `licenseClass` desconocida | `Clase de licencia desconocida: 'X'.` (campo `licenseClass`) | 400 |
| `licenseNumber` vacío | `El número de licencia es obligatorio.` (campo `licenseNumber`) | 400 |
| `licenseNumber` de más de 60 caracteres | `El número de licencia admite como máximo 60 caracteres.` | 400 |
| `certType` vacío | `El tipo de certificación es obligatorio.` (campo `certType`) | 400 |
| `certType` desconocido | `Tipo de certificación desconocido: 'X'.` (campo `certType`) | 400 |
| `certNumber` de más de 60 caracteres | `El número de certificación admite como máximo 60 caracteres.` | 400 |
| Fecha de vencimiento anterior a la de emisión | `La fecha de vencimiento no puede ser anterior a la de emisión.` (campo `expiryDate`) | 400 |
| `id` de licencia/certificación de otro chofer | `Licencia no encontrada.` / `Certificación no encontrada.` | 404 |
| El chofer está eliminado (terminal) | `El chofer fue eliminado; solo se consulta su historial.` | 409 |

### 2.6 Dispositivos (los registra la app del chofer, Lote 7)

Qué hace: lista y desactiva los dispositivos del chofer (plataforma, versión de app, última conexión). El servicio
**nunca** devuelve el `PushToken`; el DTO solo trae `hasPushToken`.

Quién puede: `fleet.view` para listar, `fleet.manage` para desactivar.

Cómo se usa:
- `GET /api/v1/drivers/{publicId}/devices?includeInactive=false`
- `POST /api/v1/drivers/{publicId}/devices/{id}/deactivate`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `id` de un dispositivo de otro chofer | `Dispositivo no encontrado.` | 404 |

### 2.7 Zonas de despacho

Qué hace: CRUD mínimo de zonas (código, nombre, activo). El nombre de la zona **primaria** de un chofer es su
"Área" en la lista de choferes. Los miembros de zona (municipios/CP) y las zonas secundarias quedan para el
módulo de Despacho de un lote posterior.

Quién puede: `fleet.view` para listar, `fleet.manage` para escribir.

Cómo se usa:
- `GET /api/v1/dispatch-zones?includeInactive=false`
- `POST /api/v1/dispatch-zones` — `{ "code": "Z-01", "name": "Toa Baja · Bayamón" }`
- `PATCH /api/v1/dispatch-zones/{id}` — solo `name`
- `POST /api/v1/dispatch-zones/{id}/deactivate` / `.../reactivate`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `code` vacío | `El código de la zona es obligatorio.` (campo `code`) | 400 |
| `code` de más de 20 caracteres | `El código no puede exceder 20 caracteres.` | 400 |
| `code` repetido | `Ya existe una zona de despacho con ese código.` | 409 |
| `name` de más de 120 caracteres | `El nombre de la zona admite como máximo 120 caracteres.` | 400 |
| `code` en el `PATCH` | `El código de la zona se fija al crearla; no se puede cambiar.` (campo `code`) | 400 |
| Inactivar una zona con choferes activos asignados (como primaria) | `La zona tiene choferes asignados; reasígnelos antes de inactivarla.` | 409 |
| `id` de zona inexistente en el tenant | `Zona de despacho no encontrada.` | 404 |

---

## 3. Documentos por vencer (panel transversal)

Qué hace: une en una sola tabla los documentos de vehículo, las licencias y las certificaciones vencidos o que
vencen dentro de una ventana (`withinDays`, 0..365, 30 por defecto), de dueños **activos y no en estatus
terminal**. Solo cuenta el documento **vigente de cada tipo** (regla "documento vigente por tipo", la misma que
usan la ficha, la disponibilidad para despacho y la fuente de análisis `FLEET_DOCUMENT`): un documento vencido que
ya se renovó (otro activo del mismo tipo con vencimiento posterior) no aparece aquí.

Quién puede: `fleet.view`.

Cómo se usa: `GET /api/v1/fleet/expiring-documents?withinDays=30&docType=&entity=&includeExpired=true`
- `docType` ∈ `REGISTRATION`, `INSURANCE`, `INSPECTION`, `PERMIT` (vehículo), `LICENSE`, `CERTIFICATION` (chofer).
- `entity` ∈ `VEHICLE`, `DRIVER`.
- `includeExpired=false` quita los ya vencidos (deja solo los que vencen dentro de la ventana).

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `withinDays` fuera de 0..365 | `withinDays debe estar entre 0 y 365.` (campo `withinDays`) | 400 |
| `docType` desconocido | `Tipo de documento desconocido: 'X'.` (campo `docType`) | 400 |
| `entity` desconocido | `Entidad desconocida: 'X'; use VEHICLE o DRIVER.` (campo `entity`) | 400 |

---

## 4. Disponibilidad para despacho (R7)

Qué hace: evalúa, para una fecha (hoy UTC por defecto), si cada chofer y cada vehículo del tenant está disponible
para despacho, con la lista de motivos. Es la misma regla que usa internamente la asignación de chofer a una
entrega especial (sección 12) y que usará el futuro planificador de Despacho.

**Bloquean** (el recurso queda `available: false`):

| Código | Motivo | Mensaje |
|---|---|---|
| `DRIVER_INACTIVE` | Chofer con el checkbox Activo apagado | `El chofer está inactivo.` |
| `DRIVER_STATUS` | Chofer en un estatus distinto del inicial (p. ej. `UNAVAILABLE`) | `Chofer en estatus '{estatus}'.` |
| `NO_VALID_LICENSE` | Sin ninguna licencia registrada, o todas vencidas | `Sin licencia vigente.` / `Licencia vencida el yyyy-MM-dd.` |
| `VEHICLE_INACTIVE` | Vehículo con el checkbox Activo apagado | `El vehículo está inactivo.` |
| `VEHICLE_STATUS` | Vehículo en un estatus distinto del inicial (p. ej. `MAINTENANCE`) | `Vehículo en estatus '{estatus}'.` |
| `VEHICLE_DOC_EXPIRED` | Documento del vehículo vencido (no superado) | `{Tipo de documento} vencido el yyyy-MM-dd.` |
| `WORK_ORDER_IN_PROGRESS` | Vehículo con una orden de trabajo en `IN_PROGRESS` | `Orden de trabajo {número} en proceso.` |

**Solo avisan** (no bloquean; `available` puede seguir en `true`): `CERT_EXPIRED` (certificación vencida),
`DOC_EXPIRING` (documento o licencia que vence en 30 días o menos), `VEHICLE_NO_DOCUMENTS` (`El vehículo no tiene
documentos registrados.`, vehículo sin ningún documento capturado).

El **mantenimiento preventivo vencido no se evalúa aquí** (es un panel aparte, sección 6).

Quién puede: `fleet.view`.

Cómo se usa: `GET /api/v1/fleet/availability?date=&onlyAvailable=false` (`date` = hoy UTC por defecto).

---

## 5. Mantenimiento preventivo — programas

Qué hace: define programas de mantenimiento por **un vehículo** o por **un tipo de vehículo** (no ambos), con
disparador por kilometraje, por tiempo o ambos (el peor de los dos manda). Un programa por tipo se evalúa
vehículo por vehículo contra su última orden de trabajo **CLOSED** ligada a ese programa; sin ninguna, queda "Sin
historial". No hay generación automática de órdenes de trabajo (no hay job): el programa solo informa el estado.

Quién puede: `fleet.view` para listar y para el panel "due"; `fleet.maintenance` para escribir.

Cómo se usa:
- `GET /api/v1/maintenance-schedules?includeInactive=false`
- `GET /api/v1/maintenance-schedules/due?vehiclePublicId=&status=` — estado por (programa, vehículo):
  `OK` (Al día), `DUE_SOON` (Por vencer, ≤ 10 % del intervalo restante), `OVERDUE` (Vencido) o `NO_BASELINE`
  (Sin historial: falta el último servicio o el odómetro actual, si el disparador usa kilometraje).
- `POST /api/v1/maintenance-schedules` — `{ "name": "...", "vehiclePublicId": "...", "trigger": "MILEAGE", "intervalKm": 5000 }`
- `PATCH /api/v1/maintenance-schedules/{id}` — `null` = sin cambio; `clearIntervalKm`/`clearIntervalDays` quitan un intervalo
- `POST /api/v1/maintenance-schedules/{id}/deactivate` / `.../reactivate`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `vehiclePublicId` y `vehicleType` juntos, o ninguno | `Indique el vehículo o el tipo de vehículo del programa, no ambos.` (campo `vehiclePublicId`) | 400 |
| `name` vacío | `El nombre del programa es obligatorio.` (campo `name`) | 400 |
| `name` de más de 150 caracteres | `El nombre del programa admite como máximo 150 caracteres.` | 400 |
| `trigger` vacío | `El disparador del programa es obligatorio.` (campo `trigger`) | 400 |
| `trigger` desconocido | `Disparador desconocido: 'X'.` (campo `trigger`) | 400 |
| `vehicleType` desconocido | `Tipo de vehículo desconocido: 'X'.` (campo `vehicleType`) | 400 |
| `MILEAGE`/`BOTH` sin `intervalKm` &gt; 0 | `Un programa por kilometraje exige un intervalo en km mayor que 0.` (campo `intervalKm`) | 400 |
| `TIME`/`BOTH` sin `intervalDays` &gt; 0 | `Un programa por tiempo exige un intervalo en días mayor que 0.` (campo `intervalDays`) | 400 |
| `intervalDays` mayor que 36500 | `El intervalo en días admite como máximo 36500.` | 400 |
| `lastServiceKm`/`lastServiceDate` en un programa **por tipo** | `El último servicio solo se captura en programas de un vehículo; en los de tipo se toma de sus órdenes de trabajo cerradas.` (campo `lastServiceKm`) | 400 |
| `lastServiceKm` negativo | `La lectura del último servicio no puede ser negativa.` | 400 |
| `lastServiceDate` futura | `La fecha del último servicio no puede ser futura.` | 400 |
| `intervalKm`/`lastServiceKm` con demasiados decimales o fuera de rango DECIMAL(12,1) | `El valor admite como máximo 1 decimal y debe ser menor que {límite}.` | 400 |
| `id` de programa inexistente en el tenant | `Programa de mantenimiento no encontrado.` | 404 |
| Filtro `status` desconocido en `due` | `Estatus desconocido: 'X'.` (campo `status`) | 400 |

---

## 6. Órdenes de trabajo

### 6.1 Alta y numeración automática

Qué hace: crea una orden de trabajo (OT) sobre un vehículo activo y no dado de baja. El número (`OT-#####`) lo
asigna el sistema, consecutivo por compañía; nunca se teclea. Si se indica un programa, el tipo de mantenimiento es
**PREVENTIVE** por defecto y el programa debe aplicar a ese vehículo (por vehículo o por su tipo). La OT nace en
**OPEN**.

Quién puede: `fleet.maintenance`.

Cómo se usa: `POST /api/v1/maintenance-work-orders`

```json
{ "vehiclePublicId": "...", "maintenanceType": "PREVENTIVE", "scheduleId": 3, "scheduledDate": "2026-10-01" }
```

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Vehículo inactivo o dado de baja definitiva | `El vehículo está inactivo; reactívelo para registrar órdenes de trabajo o cargas de combustible.` | 409 |
| Sin `maintenanceType` ni `scheduleId` | `El tipo de mantenimiento es obligatorio.` (campo `maintenanceType`) | 400 |
| `maintenanceType` desconocido | `Tipo de mantenimiento desconocido: 'X'.` (campo `maintenanceType`) | 400 |
| `scheduleId` inexistente en el tenant | `Programa de mantenimiento no encontrado.` | 404 |
| `scheduleId` inactivo | `El programa de mantenimiento está inactivo.` (campo `scheduleId`) | 400 |
| `scheduleId` no aplica a ese vehículo (ni por vehículo ni por su tipo) | `El programa de mantenimiento no aplica a este vehículo.` (campo `scheduleId`) | 400 |
| `scheduleId` (preventivo) con `maintenanceType` distinto de PREVENTIVE | `Una orden correctiva no se asocia a un programa preventivo.` (campo `maintenanceType`) | 400 |
| `laborCost`/`partsCost` negativos | `Los costos no pueden ser negativos.` | 400 |
| `odometerKm` negativo | `El odómetro no puede ser negativo.` | 400 |
| `currency` desconocida | `Moneda desconocida: 'X'.` (campo `currency`) | 400 |
| Costos/odómetro con demasiados decimales o fuera de rango DECIMAL(18,4)/(12,1) | `El valor admite como máximo {N} decimales y debe ser menor que {límite}.` | 400 |
| Choque de número por alta concurrente (agotó reintentos internos) | `Ya existe una orden de trabajo con ese número; intente de nuevo.` | 409 |

Ocho altas simultáneas sobre el mismo vehículo reciben ocho números consecutivos distintos, sin 409 ni 500 (el
contador se bloquea internamente hasta el commit).

### 6.2 Tareas y costos

Qué hace: agrega, edita y quita (baja lógica) tareas de la OT. **Con** tareas activas, los costos del encabezado
(`laborCost`, `partsCost`) son la **suma** de las tareas activas y no se capturan a mano; **sin** tareas, se
capturan directo en el encabezado. `totalCost` siempre es `laborCost + partsCost` (columna calculada en BD).
Editar tareas exige la misma capacidad que editar el encabezado (`EDIT_WORK_ORDER`, sección 6.3).

Quién puede: `fleet.maintenance`.

Cómo se usa:
- `POST /api/v1/maintenance-work-orders/{publicId}/tasks` — `{ "description": "Cambio de aceite", "partCost": 40, "laborCost": 20 }`
- `PATCH /api/v1/maintenance-work-orders/{publicId}/tasks/{taskId}` — incluye `isCompleted`
- `POST /api/v1/maintenance-work-orders/{publicId}/tasks/{taskId}/deactivate`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `description` vacía (alta) | `La descripción de la tarea es obligatoria.` (campo `description`) | 400 |
| `description` de más de 250 caracteres | `La descripción de la tarea admite como máximo 250 caracteres.` | 400 |
| `partCost`/`laborCost` negativos | `Los costos no pueden ser negativos.` | 400 |
| `taskId` de otra OT (u otro tenant) | `Tarea no encontrada.` | 404 |
| Intentar capturar `laborCost`/`partsCost` en el **encabezado** cuando la OT ya tiene tareas activas | `La orden tiene tareas: los costos de labor y partes se calculan con la suma de sus tareas.` | 400 |

### 6.3 Edición del encabezado y capacidad EDIT_WORK_ORDER

Qué hace: edita fecha programada, odómetro, proveedor, costos (solo sin tareas), moneda y notas. El número y el
vehículo son inmutables. Editar exige la capacidad `EDIT_WORK_ORDER` sobre el estatus actual de la OT: por defecto
está **denegada en CLOSED y CANCELLED** (el tenant puede relajarla desde
`PUT /api/v1/status/capabilities/WORK_ORDER`, capítulo 01).

Quién puede: `fleet.maintenance`.

Cómo se usa: `PATCH /api/v1/maintenance-work-orders/{publicId}`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `number`/`vehiclePublicId` en el cuerpo | `El número y el vehículo de la orden de trabajo se fijan al crearla.` | 400 |
| OT en CLOSED/CANCELLED (capacidad denegada) | `El estatus actual no permite la acción 'EDIT_WORK_ORDER'.` | 422 |
| `vendor` de más de 150 caracteres | `El proveedor admite como máximo 150 caracteres.` | 400 |
| `rowVersion` no coincide | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |

### 6.4 Estatus y efecto en el vehículo

Qué hace: mueve la OT por `WorkOrderStatus` (**OPEN** → **IN_PROGRESS** → **CLOSED**; **CANCELLED** terminal). Al
**cerrar**, exige que no queden tareas activas incompletas y, si el programa asociado es por kilometraje (o
ambos), la lectura de odómetro (del cuerpo o ya capturada en el encabezado). Los efectos automáticos sobre el
vehículo y el programa están en la sección 11.3.

Quién puede: `fleet.maintenance`.

Cómo se usa: `POST /api/v1/maintenance-work-orders/{publicId}/status`

```json
{ "toCode": "CLOSED", "completedDate": "2026-10-05", "odometerKm": 5200 }
```

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `toCode` vacío | `El estatus destino es obligatorio.` (campo `toCode`) | 400 |
| `completedDate` futura (al cerrar) | `La fecha de cierre no puede ser futura.` (campo `completedDate`) | 400 |
| Tareas activas incompletas (al cerrar) | `La orden tiene {n} tarea(s) sin completar; márquelas como completadas o quítelas antes de cerrarla.` | 422 |
| Sin lectura de odómetro y el programa es por kilometraje o ambos (al cerrar) | `Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.` (campo `odometerKm`) | 400 |

`OPEN` → `CLOSED` directo también es válido (una OT sin pasar por `IN_PROGRESS`): el motor de estatus permite
llegar a un terminal fuera de orden. Historial: `GET /api/v1/status/history/WORK_ORDER/{id}`.

---

## 7. Bitácora de combustible

Qué hace: registra cargas de combustible por vehículo (litros, costo, odómetro opcional, chofer opcional,
estación). El km/L y el costo/km **se calculan al leer**, no se guardan: por cada carga con odómetro se compara
contra la carga anterior con odómetro del mismo vehículo (litros/costo de las cargas sin odómetro intermedias se
atribuyen al tramo siguiente). El resumen por vehículo agrega la serie completa. El odómetro de una carga debe ser
**monótono por fecha** dentro del mismo vehículo (ni menor que una anterior ni mayor que una posterior). Cada carga
activa sube `Vehicle.CurrentOdometerKm` (máximo monotónico); una carga desactivada deja de contar mas no baja el
odómetro del vehículo.

Quién puede: `fleet.view` para listar, `fleet.maintenance` para escribir.

Cómo se usa:
- `GET /api/v1/fuel-logs?vehiclePublicId=&driverPublicId=&fromUtc=&toUtc=&includeInactive=false&skip=0&take=100`
  (`fromUtc` inclusivo, `toUtc` exclusivo; `take` 1..500)
- `POST /api/v1/fuel-logs` — `{ "vehiclePublicId": "...", "fillDateUtc": "2026-09-20T10:00:00Z", "liters": 40, "totalCost": 60, "odometerKm": 5200 }`
- `PATCH /api/v1/fuel-logs/{id}` — `null` = sin cambio; `clearOdometer`/`clearDriver` quitan el valor
- `POST /api/v1/fuel-logs/{id}/deactivate`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Vehículo inactivo o dado de baja definitiva | `El vehículo está inactivo; reactívelo para registrar órdenes de trabajo o cargas de combustible.` | 409 |
| `liters` ≤ 0 | `Los litros deben ser mayores que 0.` (campo `liters`) | 400 |
| `totalCost` negativo | `El costo total no puede ser negativo.` (campo `totalCost`) | 400 |
| `odometerKm` negativo | `El odómetro no puede ser negativo.` (campo `odometerKm`) | 400 |
| `fillDateUtc` futura (más de 5 minutos) | `La fecha de la carga no puede ser futura.` (campo `fillDateUtc`) | 400 |
| `fillDateUtc` vacía/por defecto | `La fecha de la carga es obligatoria.` (campo `fillDateUtc`) | 400 |
| `vehiclePublicId` vacío (alta) | `El vehículo de la carga es obligatorio.` (campo `vehiclePublicId`) | 400 |
| Intentar cambiar el vehículo de una carga existente | `El vehículo de una carga no se cambia; desactívela y registre otra.` (campo `vehiclePublicId`) | 400 |
| Corregir una carga ya desactivada | `La carga de combustible está inactiva; no se puede corregir.` | 409 |
| `odometerKm` menor que el de una carga anterior del mismo vehículo | `La lectura de odómetro ({km} km) es menor que la de una carga anterior del mismo vehículo ({km} km el yyyy-MM-dd).` (campo `odometerKm`) | 400 |
| `odometerKm` mayor que el de una carga posterior del mismo vehículo | `La lectura de odómetro ({km} km) es mayor que la de una carga posterior del mismo vehículo ({km} km el yyyy-MM-dd).` (campo `odometerKm`) | 400 |
| `station` de más de 150 caracteres | `La estación admite como máximo 150 caracteres.` | 400 |
| `currency` desconocida | `Moneda desconocida: 'X'.` (campo `currency`) | 400 |
| `skip` negativo | `skip no puede ser negativo.` | 400 |
| `take` fuera de 1..500 | `take debe estar entre 1 y 500.` | 400 |
| Carga de otro tenant (o inexistente) | `Carga de combustible no encontrada.` | 404 |
| Choque de concurrencia al guardar | `La carga de combustible cambió mientras se guardaba; recargue e intente de nuevo.` | 409 |
| Choque de precisión DECIMAL en litros/costo/odómetro | `El valor admite como máximo {N} decimales y debe ser menor que {límite}.` | 400 |

---

## 8. Tarifas del chofer

Las tres tarifas cuelgan del chofer (no hay una tabla de cabecera "acuerdo"): la ausencia de filas es la "ficha
vacía". Todas son **efectivo-fechadas**: editar = cerrar la fila abierta (`effectiveTo` exclusivo) y abrir una
versión nueva; quitar = cerrar sin reemplazo. Ninguna fecha nueva (`effectiveFrom`/`effectiveTo`) puede ser
anterior a hoy. Ningún cuerpo de tarifa lleva el chofer: siempre sale de la ruta `/drivers/{publicId}`.

Quién puede: `driverpay.view` para consultar, `driverpay.manage` para escribir (permisos separados de `fleet.*`,
R8). Un chofer **eliminado** (estatus terminal) solo se consulta: toda escritura responde 409.

Cómo se usa (consulta): `GET /api/v1/drivers/{publicId}/rates?asOf=&includeHistory=false` — `asOf` (hoy por
defecto) elige la vigencia mostrada; `includeHistory=true` agrega las filas cerradas o futuras.

| Caso común a las tres tarifas | Mensaje exacto | HTTP |
|---|---|---|
| Chofer eliminado (terminal), cualquier escritura | `El chofer fue eliminado; sus tarifas y viajes solo se consultan.` | 409 |
| `id` de tarifa que no pertenece a ESE chofer | `Tarifa no encontrada.` | 404 |
| `effectiveFrom`/`effectiveTo` anterior a hoy | `La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe.` | 400 |
| `rate` negativa | `La tarifa no puede ser negativa.` (campo `rate`) | 400 |
| `rate` con demasiados decimales o fuera de rango DECIMAL(18,4) | `El valor admite como máximo 4 decimales y debe ser menor que {límite}.` | 400 |

### 8.1 Tarifa por entrega (servicio + tipo de paquete)

Qué hace: paga una cantidad fija por cada entrega de un **servicio + tipo de paquete exactos** (no hay comodín de
"cualquier paquete").

Cómo se usa:
- `POST /api/v1/drivers/{publicId}/delivery-rates` — `{ "serviceType": "STANDARD", "packageType": "BOX", "rate": 3.50 }`
- `PATCH /api/v1/drivers/{publicId}/delivery-rates/{id}` — solo `rate`/`effectiveFrom`
- `POST /api/v1/drivers/{publicId}/delivery-rates/{id}/close`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `serviceType`/`packageType` vacío | `Indique el servicio y el tipo de paquete de la tarifa.` (campo correspondiente) | 400 |
| `serviceType` desconocido | `Tipo de servicio desconocido: 'X'.` | 400 |
| `packageType` desconocido | `Tipo de paquete desconocido: 'X'.` | 400 |
| Ya hay una tarifa vigente para el mismo servicio + paquete | `El chofer ya tiene una tarifa vigente para {Servicio} + {Paquete}; edite esa tarifa o ciérrela antes de agregar otra.` | 409 |
| `serviceType`/`packageType` en el `PATCH` | `El servicio y el paquete se fijan al crear la tarifa; quite la fila y cree una nueva.` | 400 |
| Editar/cerrar una fila ya cerrada | `La tarifa ya está cerrada; agregue una nueva si necesita volver a pagarla.` | 409 |

### 8.2 Tarifa por intento y niveles de intento

Qué hace: paga por número de intento de entrega (1, 2, 3…). Los niveles de intento (`AttemptLevels`, 1..20) son un
entero **por compañía** (`DriverPayPolicy`); "+ Agregar intento" solo sube el contador, no crea filas de $0 por
chofer: un nivel sin fila se ve "sin tarifa", distinguible de un $0 capturado a propósito. El fallback (R16): si
el intento pedido supera el nivel más alto configurado para el chofer, se usa la tarifa de ese nivel más alto; un
nivel intermedio sin fila paga $0 con la nota `sin tarifa configurada`.

Cómo se usa:
- `PUT /api/v1/drivers/{publicId}/attempt-rates/{attemptNumber}` — `{ "rate": 1.50 }`
- `POST /api/v1/drivers/{publicId}/attempt-rates/{attemptNumber}/close`
- `GET /api/v1/driver-pay-policy` / `PATCH` (fórmula) / `POST attempt-levels`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `attemptNumber` fuera de 1..`AttemptLevels` | `El intento {n} no existe; los niveles configurados van de 1 a {N}.` | 400 |
| Ya hay una tarifa vigente para ese intento | `El chofer ya tiene una tarifa vigente para el intento {n}; edite esa tarifa o ciérrela antes de agregar otra.` | 409 |
| Nueva vigencia anterior al cierre de una fila ya cerrada a futuro | `El intento {n} tiene una tarifa vigente hasta el {fecha}; la nueva debe empezar en esa fecha o después.` | 409 |
| `AttemptLevels` ya en el tope (20) al agregar un nivel | `Se alcanzó el máximo de 20 niveles de intento.` | 409 |
| `payoutFormula` desconocida | `Fórmula de pago desconocida: 'X'.` (campo `payoutFormula`) | 400 |

### 8.3 Tarifa por viaje (entrega especial)

Qué hace: paga un monto fijo por tipo de viaje. El "tipo de viaje" **es** el catálogo de servicios especiales del
tenant (capítulo 02, sección 6): se lista con `GET /driver-trip-types`. Inactivar un tipo de servicio especial que
tenga tarifas por viaje vigentes en algún chofer responde 409 (ajuste al Lote 2).

Cómo se usa:
- `POST /api/v1/drivers/{publicId}/trip-rates` — `{ "specialServiceTypeId": 5, "rate": 75 }`
- `PATCH /api/v1/drivers/{publicId}/trip-rates/{id}` — solo `rate`/`effectiveFrom`
- `POST /api/v1/drivers/{publicId}/trip-rates/{id}/close`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Tenant sin ningún tipo de servicio especial activo | `Sin servicios especiales: agréguelos en Clientes y contratos antes de configurar tarifas por viaje.` (campo `specialServiceTypeId`) | 400 |
| `specialServiceTypeId` ausente | `El tipo de viaje es obligatorio.` (campo `specialServiceTypeId`) | 400 |
| `specialServiceTypeId` inexistente | `Tipo de servicio especial no encontrado.` | 404 |
| `specialServiceTypeId` inactivo | `El tipo de servicio especial está inactivo; reactívelo o elija otro.` (campo `specialServiceTypeId`) | 400 |
| Ya hay una tarifa vigente para ese tipo | `El chofer ya tiene una tarifa vigente para el tipo de viaje '{tipo}'; edite esa tarifa o ciérrela antes de agregar otra.` | 409 |
| `specialServiceTypeId` en el `PATCH` | `El tipo de viaje se fija al crear la tarifa; quite la fila y agregue una con el tipo correcto.` | 400 |
| Inactivar un tipo con tarifas por viaje vigentes | `El tipo tiene tarifas por viaje vigentes en {n} chofer(es); ciérrelas antes de inactivarlo.` (capítulo 02) | 409 |

### 8.4 Vista previa del pago (fórmula configurable)

Qué hace: calcula, **sin escribir nada**, el pago de una entrega con sus intentos según la fórmula indicada (o la
vigente de la compañía si no se indica): `DELIVERY_PLUS_ATTEMPTS` (la entrega, si hubo, más cada intento incluido
el exitoso), `DELIVERY_INCLUDES_FIRST` (la entrega más los intentos desde el 2º) o `FAILED_REPLACES_DELIVERY` (cada
intento fallido paga su propia tarifa; el exitoso solo paga la entrega). Una entrega o un intento sin tarifa
generan una línea en $0 con la nota `sin tarifa configurada` (el vacío queda visible, R15).

Quién puede: `driverpay.view`.

Cómo se usa: `POST /api/v1/driver-pay-policy/preview`

```json
{ "attempts": [ { "number": 1, "delivered": false }, { "number": 2, "delivered": true } ], "deliveryRate": 4.00, "attemptRates": [ { "number": 1, "rate": 1.00 }, { "number": 2, "rate": 1.50 } ] }
```

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Sin intentos | `Indique al menos un intento.` | 400 |
| Más de 50 intentos | `Se admiten como máximo 50 intentos.` | 400 |
| Número de intento fuera de 1..50 | `El número de intento debe estar entre 1 y 50.` | 400 |
| Números de intento repetidos | `Hay números de intento repetidos.` | 400 |
| Más de un intento marcado como entrega | `Solo un intento puede ser la entrega.` | 400 |
| La entrega no es el último intento | `La entrega debe ser el último intento.` | 400 |
| `attemptRates` con el mismo número repetido | `La tarifa del intento {n} está repetida.` (campo `attemptRates`) | 400 |
| `formula` desconocida | `Fórmula de pago desconocida: 'X'.` (campo `formula`) | 400 |

---

## 9. Viajes pagados al chofer (DriverTrip)

Qué hace: registra un viaje pagado al chofer con el **monto congelado** al crearlo (la tarifa por viaje vigente
en la fecha del viaje, sección 8.3); si el chofer no tiene tarifa configurada para ese tipo, el viaje nace igual
en $0 con `rateMissing: true` (el vacío queda visible, R15). Los viajes que nacen de una entrega especial
(sección 12) llevan además `orderPublicId`/`orderNumber` y **no se cancelan directo**: se cancelan cancelando la
orden o reasignando el chofer.

Quién puede: `driverpay.view` para listar, `driverpay.manage` para crear/cancelar.

Cómo se usa:
- `GET /api/v1/drivers/{publicId}/trips?from=&to=&status=&includeCancelled=false` (por defecto solo vigentes)
- `POST /api/v1/drivers/{publicId}/trips` — `{ "specialServiceTypeId": 5, "tripDate": "2026-09-25" }`
- `POST /api/v1/drivers/{publicId}/trips/{tripPublicId}/cancel` — cuerpo opcional `{ "comment": "…" }`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `specialServiceTypeId` ausente | `El tipo de viaje es obligatorio.` (campo `specialServiceTypeId`) | 400 |
| `specialServiceTypeId` inexistente | `Tipo de servicio especial no encontrado.` | 404 |
| `specialServiceTypeId` inactivo | `El tipo de servicio especial está inactivo; reactívelo o elija otro.` (campo `specialServiceTypeId`) | 400 |
| `tripDate` futura | `La fecha del viaje no puede ser futura.` (campo `tripDate`) | 400 |
| `notes`/`comment` de más de 500 caracteres | `Las notas del viaje admiten como máximo 500 caracteres.` | 400 |
| `filtro from` posterior a `to` | `La fecha inicial no puede ser posterior a la final.` (campo `from`) | 400 |
| Filtro `status` desconocido | `Estatus de viaje desconocido: 'X'.` (campo `status`) | 400 |
| `tripPublicId` de otro chofer o de otro tenant | `Viaje no encontrado.` | 404 |
| Cancelar un viaje que nace de una entrega especial vigente | `El viaje nace de una entrega especial; cancele la orden o reasigne el chofer.` | 409 |
| Cancelar un viaje ya `SETTLED`/`CANCELLED` (terminal) | `El registro ya está en ese estatus.` / regla de transición del motor de estatus (capítulo 01) | 422 |

La BD garantiza en todo momento **un solo viaje vigente por orden**: un viaje cancelado queda además `isActive:
false`, así que dos asignaciones concurrentes a la misma orden nunca dejan dos viajes vigentes.

---

## 10. Estatus y transiciones

### 10.1 Vehicle (`VehicleStatus`)

| De | A | Quién | Qué valida | Efectos | Qué bloquea |
|---|---|---|---|---|---|
| — | ACTIVE (inicial) | Sistema, al crear | — | Historial de nacimiento | — |
| ACTIVE | MAINTENANCE (lateral) | `fleet.manage`, o automático al iniciar una OT (sección 11.3) | — | Ninguno propio | — |
| MAINTENANCE | ACTIVE (lateral) | `fleet.manage`, o automático al cerrar/cancelar la última OT en proceso | — | Ninguno propio | — |
| ACTIVE / MAINTENANCE | INACTIVE (terminal) | `fleet.manage` | — | `VehicleStatusEffect`: `IsActive = 0` | Reactivar (409); nuevas OT/cargas de combustible (409, sección 6/7) |

### 10.2 Driver (`DriverStatus`)

| De | A | Quién | Qué valida | Efectos | Qué bloquea |
|---|---|---|---|---|---|
| — | ACTIVE (inicial) | Sistema, al crear | Nace sin filas de tarifa | Historial de nacimiento | — |
| ACTIVE | UNAVAILABLE (lateral) | `fleet.manage` | — | Ninguno propio (deja de estar disponible para despacho, sección 4) | — |
| UNAVAILABLE | ACTIVE (lateral) | `fleet.manage` | — | Ninguno propio | — |
| ACTIVE / UNAVAILABLE | INACTIVE (terminal, "Eliminar chofer") | `fleet.manage` (`DELETE`) | — | `DriverStatusEffect`: `IsActive=0`, `UserId=NULL`, dispositivos inactivos, se quita la zona. `DriverRatesRetirementEffect`: cierra hoy toda tarifa abierta | Reactivar (409); toda escritura de tarifas (409); cancelar/crear viajes normalmente sigue funcionando solo para consulta |

### 10.3 MaintenanceWorkOrder (`WorkOrderStatus`, EntityType `WORK_ORDER`)

| De | A | Quién | Qué valida | Efectos | Qué bloquea |
|---|---|---|---|---|---|
| — | OPEN (inicial) | Sistema, al crear | Número `OT-#####` | Historial de nacimiento | — |
| OPEN | IN_PROGRESS | `fleet.maintenance` | — | `WorkOrderStatusEffect`: si el vehículo está `ACTIVE` lo pasa a `MAINTENANCE` | — |
| IN_PROGRESS / OPEN | CLOSED (terminal) | `fleet.maintenance` | Tareas activas completas; odómetro si el programa es por km/ambos | `WorkOrderStatusEffect`: si no queda otra OT `IN_PROGRESS` del vehículo, lo regresa a `ACTIVE`; sube `CurrentOdometerKm` (máximo monotónico); si el programa es por vehículo, avanza `LastServiceKm`/`LastServiceDate` | `EDIT_WORK_ORDER` denegado por defecto (422 sobre encabezado y tareas) |
| cualquiera | CANCELLED (terminal) | `fleet.maintenance` | — | Igual efecto de vehículo que CLOSED, sin exigir tareas/odómetro | `EDIT_WORK_ORDER` denegado por defecto |

### 10.4 DriverTrip (`DriverTripStatus`)

| De | A | Quién | Qué valida | Efectos | Qué bloquea |
|---|---|---|---|---|---|
| — | OPEN (inicial) | Sistema, al crear (manual o entrega especial) | Congela la tarifa vigente (`DriverTripRules.Freeze`) | Historial de nacimiento | — |
| OPEN | CANCELLED (terminal) | `driverpay.manage`, o automático al cancelar la orden de origen (`DriverTripOrderEffect`) | Un viaje ligado a una orden vigente no se cancela directo (409) | `DriverTripStatusEffect`: `IsActive=0` (garantiza en BD un solo viaje vigente por orden) | Nueva cancelación (422) |
| OPEN | SETTLED (terminal) | Corrida de liquidación (Lote 9, fuera de este lote) | — | — | Nueva cancelación/liquidación (422) |

Historial de cualquiera de las cuatro entidades: `GET /api/v1/status/history/{ENTITY}/{id}` con
`VEHICLE`/`DRIVER`/`WORK_ORDER`/`DRIVER_TRIP`. Capacidades por estatus: `GET/PUT /api/v1/status/capabilities/{ENTITY}`
(solo `WORK_ORDER` trae una capacidad propia en este lote, `EDIT_WORK_ORDER`).

---

## 11. Entrega especial con chofer (extensión del Lote 3)

Qué hace: completa el flujo que el Lote 3 dejó pendiente. Asigna (o reasigna) el chofer de una entrega especial ya
creada, o lo indica al crearla (`OrderCreateRequest.driverPublicId`). El chofer, su disponibilidad (sección 4) y su
tarifa por viaje (sección 8.3) se verifican **antes de sacar números** (así un rechazo no deja hueco en la
numeración de la orden); la asignación confirma la orden si sigue en la etapa inicial (cotiza y verifica crédito
como en el capítulo 03, sección 3.2), avanza la orden **etapa por etapa** (sin saltos) hasta **IN_TRANSIT** con un
registro de historial por etapa, y crea el `DriverTrip` con el monto congelado (sección 9). La ficha de la orden
expone solo la identidad del chofer asignado (`assignedDriverPublicId/Code/Name`); **nunca** el monto del viaje.

Quién puede: `trips.dispatch` (el endpoint vive bajo el módulo `LTL_GROUND`, pero el servicio exige además que el
módulo `CATALOG` esté encendido, porque usa choferes y tarifas).

Cómo se usa:
- Al crear la orden: `POST /api/v1/orders` con `"isSpecialDelivery": true`, `"specialServiceId": ...` y
  `"driverPublicId": "..."`.
- Sobre una orden ya creada: `POST /api/v1/orders/{publicId}/driver` — `{ "driverPublicId": "...", "overrideCredit": false, "comment": "..." }`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Módulo `CATALOG` apagado | `El módulo 'CATALOG' no está habilitado para esta compañía.` (`code: module_disabled`) | 403 |
| Falta `trips.dispatch` | `Falta el permiso 'trips.dispatch'.` | 403 |
| `driverPublicId` de otro tenant o inexistente | `Chofer no encontrado.` | 404 |
| Chofer no disponible para despacho | `El chofer no está disponible para despacho: {motivos bloqueantes separados por '; '}.` | 409 |
| `driverPublicId` en una orden que **no** es entrega especial | `El chofer solo se asigna en una entrega especial.` (campo `driverPublicId`) | 400 |
| `overrideCredit: true` sin `confirmNow` ni `driverPublicId` | `overrideCredit requiere confirmNow=true.` (campo `overrideCredit`) | 400 |
| Exceso de crédito al confirmar, sin `orders.credit_override` | (igual que capítulo 03, sección 3.3) `code: "credit_exceeded"` | 422 |
| Reasignar/asignar sobre una orden que **no** es entrega especial | `Solo las entregas especiales se asignan a un chofer desde aquí; las demás órdenes pasan por Sala de despacho.` | 422 |
| La orden ya llegó a `IN_TRANSIT` o más adelante, o está en un lateral/terminal | `La entrega especial ya llegó a destino o terminó; no se puede asignar ni reasignar el chofer.` | 422 |
| Asignar el mismo chofer que ya la tiene | `La orden ya está asignada a ese chofer.` | 409 |
| Dos asignaciones concurrentes a la misma orden | `La orden ya tiene un viaje vigente; recargue e intente de nuevo.` | 409 |

Reasignar a otro chofer cancela automáticamente el viaje del chofer anterior (queda `CANCELLED`/`isActive: false`)
antes de crear el nuevo. Cancelar la **orden** cancela automáticamente su `DriverTrip` vigente (sección 10.4).

---

## 12. Permisos y módulos

| Permiso | Qué habilita |
|---|---|
| `fleet.view` | ver vehículos, choferes, zonas, programas de mantenimiento, órdenes de trabajo, cargas de combustible, documentos por vencer y disponibilidad para despacho |
| `fleet.manage` | crear/editar vehículos, choferes, sus documentos/licencias/certificaciones/dispositivos y zonas de despacho |
| `fleet.maintenance` | crear/editar programas de mantenimiento, órdenes de trabajo (y sus tareas) y cargas de combustible |
| `driverpay.view` | ver tarifas del chofer, política de pago, vista previa y viajes pagados |
| `driverpay.manage` | crear/editar tarifas del chofer, política de pago y viajes pagados |
| `trips.dispatch` | asignar/reasignar el chofer de una entrega especial (capítulo 03) |
| `admin.users` | vincular o desvincular el usuario del chofer (además de `fleet.manage`) |

Todo el módulo vive bajo **CATALOG**; la asignación de chofer a una entrega especial vive bajo **LTL_GROUND**
(núcleo) pero exige además CATALOG encendido. Plantillas de rol de fábrica: **Despachador** (`Dispatcher`) trae
`fleet.view`; **Facturación** (`Billing`) trae `driverpay.view`; **Solo lectura** (`ReadOnly`) trae `fleet.view`;
**Chofer** (`Driver`) no trae ningún permiso `fleet.*` ni `driverpay.*` (su acceso propio llega con la app del
chofer, en un lote posterior). Solo **Admin** trae `driverpay.manage`.

Contactos (`POST /api/v1/contacts/{ENTITY}/{id}`) y campos personalizados (`PUT /api/v1/custom-fields/values/{ENTITY}/{id}`,
capítulo 01) están disponibles para `VEHICLE`, `DRIVER`, `DISPATCH_ZONE`, `MAINTENANCE_SCHEDULE`, `WORK_ORDER` y
`FUEL_LOG` (bajo el permiso de dueño correspondiente de la tabla anterior). `DRIVER_RATE` y `FLEET_DOCUMENT` no
admiten contactos ni campos personalizados por id suelto (responden siempre `404`): las tres tablas de tarifa se
alcanzan solo bajo `/drivers/{publicId}` y "documento de flota" es una fila virtual que une varias tablas.

---

## 13. Fuentes de análisis y auditoría

Quedan disponibles para vistas, indicadores y gráficos las fuentes `VEHICLE` ("Vehículos"), `DRIVER` ("Choferes"),
`WORK_ORDER` ("Órdenes de trabajo"), `FUEL_LOG` ("Cargas de combustible") y `FLEET_DOCUMENT` ("Documentos por
vencer", sin `DateField`: es un estado actual). **No** hay fuente de tarifas ni de viajes pagados (`DRIVER_RATE`,
`DRIVER_TRIP`): expondrían la compensación a cualquiera con `analytics.view`.

El tenant nuevo (y el demo) recibe de fábrica: la vista **"Vehículos"** (código, placa, tipo, propiedad,
combustible, odómetro, estatus, activo; orden por código) y la vista **"Documentos por vencer"** (dueño, tipo de
documento, número, vencimiento, días restantes, estado; filtrada a `EXPIRED`/`EXPIRING`, orden por vencimiento — no
trae un documento superado por otro más nuevo del mismo tipo); el indicador **"Documentos por vencer"** (cuenta,
mismo filtro, en Pulso) y el indicador **"Órdenes de trabajo abiertas"** (cuenta las `OPEN`/`IN_PROGRESS` activas,
en Pulso). Cada alta o cambio de un vehículo, chofer, tarifa, viaje pagado u orden de trabajo queda en `AuditLog`
(`GET /api/v1/audit/changes?entityType=VEHICLE|DRIVER|DRIVER_RATE|DRIVER_TRIP|WORK_ORDER`); el `PushToken` del
dispositivo del chofer nunca aparece en el diff (es un dato sensible).

---

## 14. Preguntas frecuentes

Ver [faq.md](faq.md), sección "Lote 4 — Flota, choferes y mantenimiento", para cada mensaje de error citado en este
capítulo con su causa y qué hacer.
