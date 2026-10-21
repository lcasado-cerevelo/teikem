# Capítulo 02 — Clientes y contratos (Lote 2)

Este capítulo describe, tal como lo vive el usuario, todo lo que el Lote 2 deja disponible: el expediente del cliente
(perfil, direcciones, numeración, contactos), sus consignatarios y localizaciones, los contratos con su modelo de
facturación, las tarifas por servicio y por pieza extra, la cotización, los servicios especiales y los usuarios de portal
del cliente. Cada sección indica **qué hace**, **quién puede**, **cómo se usa**, las **validaciones** con el mensaje exacto
y el código HTTP, y los **estatus** con sus transiciones y efectos. Los mensajes están verificados contra el código
(`src/Teikem.Infrastructure/Services/*`, `src/Teikem.Infrastructure/Clients/*`, `src/Teikem.Domain/Clients/*`) y
ejercitados por `scripts/smoke.sh`. Las preguntas y respuestas de cada mensaje están en [faq.md](faq.md).

Convenciones del capítulo:

- Los recursos se identifican por su `publicId` (GUID) en la URL; los hijos (contactos, tramos, servicios especiales) por su `id` numérico
  **siempre dentro de la URL del padre**. Un `publicId` de otra compañía responde `404` como si no existiera.
- Todo el módulo requiere el módulo **CATALOG** encendido; los usuarios de portal requieren **CLIENT_PORTAL** (ver 8).
- Un error de validación responde `400` con `title` (mensaje) y `errors` (`{ campo: [mensaje] }`); un conflicto `409`; una
  regla de estatus `422`; un recurso ajeno o inexistente `404`.
- Las fechas son `yyyy-MM-dd`; "hoy" es la fecha UTC del servidor.

---

## 1. Clientes

### 1.1 Alta de cliente (modal "+ Nuevo cliente")

Qué hace: crea el cliente y, opcionalmente, su **contrato inicial** en una sola acción. El contrato inicial nace en
estatus **DRAFT**, con el componente "Por servicio" encendido y los otros cuatro apagados, número `{Código}-C1`, título
"Contrato marco" si no se indica, y los niveles de servicio (SLA) que se envíen. El cliente nace en el estatus inicial
del tenant (**ACTIVE** en el seed) y su historial de estatus queda registrado desde el primer momento.

Quién puede: `clients.create`.

Cómo se usa: `POST /api/v1/clients`

```json
{ "name": "Farmacia Las Marías", "code": null, "legalName": null, "taxId": null, "paymentTerm": "NET30", "currency": "USD", "creditLimit": 5000,
  "contract": { "startDate": "2026-01-01", "title": null, "endDate": null, "autoRenew": false,
                "serviceLevels": [ { "serviceType": "STANDARD", "maxTransitHours": 48 } ] } }
```

Responde la ficha completa (ver 1.2) con `currentContract` si se creó contrato.

El **código** es opcional: si no se envía se genera desde el nombre (mayúsculas, sin acentos, todo lo no alfanumérico se
vuelve `-`, máximo 20 caracteres: "Farmacia Las Marías" → `FARMACIA-LAS-MARIAS`). Si ya existe, se agrega el sufijo `-2`,
`-3`… recortando la base si hace falta para no pasar de 20 (`FARMACIA-LAS-MARIA-2`). Un código explícito que ya exista
responde 409.

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `name` vacío | obligatorio, ≤ 200 | `El nombre es obligatorio.` / `No puede exceder 200 caracteres.` | 400 |
| `name` sin letras ni dígitos | no se puede generar código | `El nombre del cliente no contiene letras ni dígitos para generar el código.` | 400 |
| `code` > 30 caracteres | — | `El código no puede exceder 30 caracteres.` | 400 |
| `code` ya existe (o colisión de último momento) | único por compañía | `Ya existe un cliente con ese código.` | 409 |
| `legalName` / `taxId` largos | ≤ 250 / ≤ 50 | `No puede exceder 250 caracteres.` / `No puede exceder 50 caracteres.` | 400 |
| `creditLimit` negativo | ≥ 0 | `El límite de crédito no puede ser negativo.` | 400 |
| `paymentTerm` / `currency` desconocidos | código de lista | `Lista PaymentTerm 'X' no encontrada.` / `Lista Currency 'X' no encontrada.` | 404 |
| `contract.endDate` < `contract.startDate` | vigencia | `La fecha fin no puede ser anterior a la fecha de inicio.` (campo `contract.endDate`) | 400 |
| `contract.title` > 200 | — | `El título no puede exceder 200 caracteres.` | 400 |
| SLA repetido / inválido | mismas reglas que 4.6, campo `contract.serviceLevels[i].*` | ver tabla de 4.6 | 400 |
| SLA con tipo de servicio inexistente | — | `Tipo de servicio desconocido: 'X'.` | 400 |

### 1.2 Ficha del cliente

Qué hace: devuelve el expediente completo: perfil, estatus, **dirección física** (localización activa tipo CORPORATE),
**dirección postal** (tipo BILLING; `null` significa "misma que la física"), **punto de recogido** (el almacén por defecto
o, si no hay, la corporativa con `isDefaultFromCorporate: true`), sus **almacenes** (`pickupLocations`, tipo PICKUP o BOTH),
la configuración de numeración con vista previa, sus **personas de contacto** con teléfonos/correos, los **teléfonos/correos
del propio cliente**, sus **contratos** (resumen con el modelo de facturación) y el **contrato vigente** (`currentContract`,
ver 4.1) con su resumen (`billingSummary`, p. ej. "Por servicio, COD"). Incluye `rowVersion` para la edición concurrente.

Quién puede: `clients.read`.

Cómo se usa: `GET /api/v1/clients/{publicId}`; lista: `GET /api/v1/clients?search=&includeInactive=false` (busca por
código, nombre o razón social; por defecto oculta los dados de baja).

### 1.3 Perfil (sin nombre)

Qué hace: edita razón social, identificación fiscal, límite de crédito, término de pago, moneda y el **punto de recogido
habitual**. El **nombre no se edita** (es la identidad del cliente): para corregirlo hay que dar de baja el cliente y crear otro.

Quién puede: `clients.update`.

Cómo se usa: `PATCH /api/v1/clients/{publicId}/profile` con solo los campos que cambian; `rowVersion` opcional (el de la
ficha) para detectar que otro usuario editó antes. `defaultPickupLocationPublicId` fija el almacén por defecto;
`clearDefaultPickup: true` vuelve a "se recoge en la corporativa".
Lote 43 (2026-10-12): `autoPrintInvoice: true|false` guarda «Imprimir la factura automáticamente» de este cliente (el que manda);
sin el campo no cambia y la ficha lo devuelve en `autoPrintInvoice` (apagado por defecto). **Solo se guarda la preferencia: todavía no
se imprime ninguna factura** (llega con facturación). Ver el capítulo [12 — Impresoras y labels](12-impresoras-y-labels.md) §6.

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `defaultPickupLocationPublicId` no es una dirección activa del cliente | debe ser una localización propia y activa: la física (CORPORATE), la postal (BILLING) o un punto de recogida (PICKUP o BOTH) | `El punto de recogido debe ser una dirección activa del cliente: la física, la postal o un punto de recogida.` | 400 |
| `rowVersion` distinto del actual | edición concurrente | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| `creditLimit` negativo | — | `El límite de crédito no puede ser negativo.` | 400 |

**Servicio y paquete por omisión (2026-10-21).** En el perfil del cliente se pueden elegir su **servicio por omisión** y su **paquete por omisión** (`defaultServiceType` y `defaultPackageType` en `PATCH /clients/{id}/profile`; cadena vacía = vuelve a los de la compañía; un código que no es de ese catálogo da 400). La entrada rápida de envíos los trae puestos al elegir el cliente.

### 1.4 Numeración por cliente

Qué hace: dos preguntas independientes — **¿quién asigna el número de envío?** y **¿quién asigna el número de factura?**
(`clientAssignsOrderNumber`, `clientAssignsInvoiceNumber`; `false` = Teikem) — y tres patrones (envío, factura, paquete)
donde `#` es un dígito del consecutivo y `@` la letra de serie. Cadena vacía en un patrón = volver al patrón por defecto
del sistema: `ORD-#####`, `FAC-#####`, `PQT-#####` (vista previa `ORD-00001`, `FAC-00001`, `PQT-00001`). Si el consecutivo
tiene más dígitos que `#`, no se trunca (`AX-##` con 123 → `AX-123`). La ficha muestra el patrón efectivo y su vista previa.

Quién puede: `clients.update` (editar), `clients.read` (vista previa).

Cómo se usa: `PATCH /api/v1/clients/{publicId}/number-settings`
`{ "clientAssignsInvoiceNumber": true, "orderNumberFormat": "AX-#####", "invoiceNumberFormat": "FAC-####" }`;
vista previa en vivo: `GET /api/v1/clients/number-format/preview?pattern=AX-%23%23%23%23%23&seq=1` → `{ "value": "AX-00001" }`.

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| Patrón sin `#` | al menos un dígito | `El patrón debe incluir al menos un '#' para el consecutivo.` | 400 |
| Patrón > 40 caracteres | — | `El patrón no puede exceder 40 caracteres.` | 400 |
| Carácter no permitido | letras, dígitos y `- _ / . # @` | `Carácter no permitido en el patrón: 'x'. Use letras, dígitos y - _ / . # @.` | 400 |
| Vista previa sin patrón | — | `El patrón es obligatorio.` | 400 |
| Vista previa con `seq` negativo | — | `El consecutivo no puede ser negativo.` | 400 |

Los errores de `number-settings` llegan todos juntos en `errors` con la clave del campo (`orderNumberFormat`, …).

### 1.5 Personas de contacto

Qué hace: personas del cliente (nombre, rol, principal) con sus propios teléfonos y correos. Solo puede haber **un contacto
principal activo** por cliente: al marcar otro como principal, el anterior deja de serlo automáticamente. Un contacto
**inactivo nunca es el principal**. Los teléfonos/correos del **cliente** van por `/api/v1/contacts/CLIENT/{id}` y los de
cada **persona** por `/api/v1/contacts/CLIENT_CONTACT/{id}` (capítulo 01, sección 6).

Quién puede: `clients.read` (ver), `clients.update` (crear/editar); los puntos de contacto sueltos, `contacts.manage`.

Cómo se usa:
- `GET /api/v1/clients/{publicId}/contacts?includeInactive=false`
- `POST /api/v1/clients/{publicId}/contacts` `{ "fullName": "Ana Pérez", "role": "Compras", "isPrimary": true, "contactPoints": [ { "contactType": "EMAIL", "value": "ana@cliente.pr", "isPrimary": true } ] }`
- `PATCH /api/v1/clients/{publicId}/contacts/{id}` `{ "fullName"?, "role"?, "isPrimary"?, "isActive"? }`

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `fullName` vacío / largo | obligatorio, ≤ 150 | `El nombre del contacto es obligatorio.` / `No puede exceder 150 caracteres.` | 400 |
| `role` > 80 | — | `No puede exceder 80 caracteres.` | 400 |
| Punto de contacto sin tipo | — | `Cada medio de contacto necesita contactType.` | 400 |
| Correo / teléfono mal formados | — | `Correo inválido.` / `Teléfono inválido.` | 400 |
| Contacto de otro cliente o inexistente | se busca a través del cliente | `Contacto '<id>' no encontrado.` | 404 |
| Dos principales activos (carrera) | índice único | `Ya existe un contacto principal activo para este cliente.` | 409 |
| Cliente inexistente (o de otra compañía) | — | `Cliente no encontrado.` | 404 |

### 1.6 Estatus del cliente

Estatus (`ClientStatus`): **ACTIVE** (inicial) → **PROSPECT** (pipeline); **SUSPENDED** (lateral, reversible). Las
transiciones pasan por el motor de estatus del capítulo 01 (sección 5): se valida la etapa, se escribe el historial
(`GET /api/v1/status/history/CLIENT/{id}`) y se disparan efectos. Un cliente **SUSPENDED** no puede activar contratos (ver 4.7).

Cómo se usa: `POST /api/v1/clients/{publicId}/status` `{ "toCode": "SUSPENDED", "comment": "morosidad" }` (`clients.update`).

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `toCode` vacío | `El estatus destino es obligatorio.` | 400 |
| Estatus inexistente o deshabilitado | `El estatus 'X' no existe o no está habilitado para esta compañía.` | 422 |
| Mismo estatus | `El registro ya está en ese estatus.` | 422 |
| Salto ilegal / lateral no permitido | `Salto ilegal: de 'A' solo se puede avanzar a 'B'.` / `No se permite pasar a 'X' desde 'Y' según el modelo de esta compañía.` / `Desde el lateral 'SUSPENDED' solo se puede regresar a 'ACTIVE'…` | 422 |

### 1.7 Baja y reactivación del cliente

Qué hace: baja lógica (`isActive = false`), nunca se borra: el cliente desaparece de la lista (salvo `includeInactive=true`),
su ficha sigue accesible y se puede reactivar. Es idempotente (204 aunque ya estuviera dado de baja).

Quién puede: `clients.update`. Cómo se usa: `POST /api/v1/clients/{publicId}/deactivate` / `.../reactivate` → 204.

> **Actualizado por el Lote 3**: un cliente dado de baja bloquea la creación de lo nuevo (envíos, contratos, tarifas,
> servicios especiales, invitaciones de portal) con `409` `El cliente está dado de baja; solo se consulta su historial.`; **desde 2026-10-11 (D39) también la
> cotización** (`POST /api/v1/billing/contract-rate-quote`) responde ese mismo 409;
> las lecturas y los cierres/cancelaciones siguen permitidos. Ver [capítulo 03, sección 5](03-ordenes-de-transporte.md#5-cliente-dado-de-baja-se-bloquea-solo-lo-nuevo).

---

## 2. Consignatarios y localizaciones

Qué hace: una sola tabla guarda las **direcciones del cliente** (CORPORATE = física, BILLING = postal; a lo sumo una activa
de cada una por cliente), sus **almacenes o puntos de recogido** (PICKUP, BOTH), sus **consignatarios** (DELIVERY) y las
**localizaciones compartidas** del operador (sin dueño, `isShared: true`). El dueño es visible y editable. Nunca se borra:
la baja es lógica y conserva el historial; una localización inactiva no aparece en los combos.

Quién puede: `locations.read` (ver), `locations.create` (crear), `locations.update` (editar, activar/desactivar).

Cómo se usa:
- `GET /api/v1/locations?clientId={publicId}&includeShared=true&includeInactive=false&locationType=&search=` — con `clientId`,
  las propias del cliente más las compartidas; sin `clientId`, todas las de la compañía.
- `POST /api/v1/locations` `{ "clientPublicId": null|guid, "code"?, "name", "locationType": "DELIVERY", "line1", "line2"?, "city", "state"?, "postalCode"?, "country": "PR", "defaultServiceMinutes": 0, "defaultWindowStart"?: "08:00:00", "defaultWindowEnd"?: "12:00:00", "accessNotes"?, "deliveryNotes"?, "allowDupInvoice": false }`
- `GET /api/v1/locations/{publicId}` · `PATCH /api/v1/locations/{publicId}` (solo lo que cambia; `makeShared: true` quita el dueño;
  `clientPublicId` lo cambia; `clearWindow: true` borra la ventana; cadena vacía borra un texto opcional; `rowVersion` opcional)
- `POST /api/v1/locations/{publicId}/deactivate` / `.../reactivate` → 204

Efectos automáticos: si la localización es el **almacén por defecto** de su cliente y deja de servir como tal (se desactiva,
cambia de dueño o de tipo a algo que no sea PICKUP/BOTH), el recogido del cliente vuelve a "misma que la corporativa".
`allowDupInvoice` (permitir facturas repetidas para ese consignatario) se guarda aquí y lo consume el módulo de Envíos.

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `locationType` vacío / desconocido | lista `LocationType` | `El tipo de localización es obligatorio.` / `Lista LocationType 'X' no encontrada.` (lista: `Tipo de localización desconocido.`) | 400 / 404 / 400 |
| `name`, `line1`, `city` vacíos | obligatorios | `El nombre es obligatorio.` / `La dirección (línea 1) es obligatoria.` / `La ciudad es obligatoria.` | 400 |
| Textos largos | name 200, code 40, líneas 200, city/state 100, postal 20, notas 500 | `Máximo N caracteres.` | 400 |
| `country` desconocido | lista `Country` (default PR) | `Lista Country 'X' no encontrada.` | 404 |
| Ventana con solo inicio o solo fin | ambas o ninguna | `La ventana horaria requiere hora de inicio y hora de fin.` (campo `defaultWindowStart`) | 400 |
| Fin no posterior al inicio | — | `La hora de fin de la ventana debe ser posterior a la de inicio.` (campo `defaultWindowEnd`) | 400 |
| `defaultServiceMinutes` negativo | — | `Los minutos de servicio no pueden ser negativos.` | 400 |
| CORPORATE o BILLING sin cliente | siempre pertenecen a un cliente | `Una dirección corporativa o postal (facturación) debe pertenecer a un cliente; no puede ser compartida.` | 400 |
| Segunda CORPORATE activa del cliente | una activa | `El cliente ya tiene una dirección corporativa activa; desactívela o edítela.` | 409 |
| Segunda BILLING activa del cliente | una activa | `El cliente ya tiene una dirección postal (facturación) activa; desactívela o edítela.` | 409 |
| `makeShared` y `clientPublicId` a la vez | excluyentes | `Indique un cliente o marque la localización como compartida, no ambos.` | 400 |
| `clientPublicId` de otra compañía | — | `Cliente no encontrado.` | 404 |
| Localización ajena o inexistente | — | `Localización '<guid>' no encontrado.` | 404 |
| `rowVersion` distinto | edición concurrente | `La localización fue modificada por otro usuario; recargue e intente de nuevo.` | 409 |
| `contactName` largo (Lote 45) | persona de contacto, opcional, máx. 150; cadena vacía la borra en el PATCH | `Máximo 150 caracteres.` (campo `contactName`) | 400 |

Casos frecuentes: reactivar una CORPORATE/BILLING cuando ya hay otra activa responde el mismo 409; cambiar el tipo de un
consignatario a CORPORATE cuando ya existe una activa también.

### 2.1 Importación de consignatarios (Lote 38)

Qué hace: da de alta **muchos consignatarios a la vez** desde un archivo CSV, en dos pasos: **validar** (lee el archivo,
revisa cada fila y muestra una vista previa **sin crear nada**) y **confirmar** (crea las filas válidas) o **descartar**. El
archivo se lee con una **plantilla**: a cada **posición de columna** se le asigna un campo (no importa el texto de la
cabecera). Todo consignatario importado es tipo **DELIVERY**. Solo crea: un código que ya existe es un error, nunca se
actualiza un consignatario existente (para eso, su ficha). Usa las mismas reglas que el alta manual (`POST /locations`) y
deja la misma auditoría.

Quién puede: módulo **Listas** (`CATALOG`). Ver plantillas, la lista de campos y un lote: `locations.read`. Crear y editar
plantillas, validar, confirmar y descartar: `locations.create`. No hay permisos nuevos.

#### Plantillas

Cómo se usa:
- `GET /api/v1/locations/import-templates?clientId=&includeInactive=false` — con `clientId`, las generales y las de ese cliente.
- `POST /api/v1/locations/import-templates` `{ "name": "Consignatarios Farmacias", "columns": [ {"position":1,"field":"code"}, {"position":2,"field":"name"}, {"position":3,"field":"line1"}, {"position":4,"field":"city"}, {"position":5,"field":"clientCode"}, {"position":6,"field":"contactPhone"} ], "defaults": {"country":"PR","defaultServiceMinutes":"10"}, "clientPublicId"?: guid, "delimiter": ",", "hasHeader": true }`
- `GET` / `PATCH /api/v1/locations/import-templates/{publicId}` · `POST .../{publicId}/deactivate` · `POST .../{publicId}/reactivate`
- `GET /api/v1/locations/import/fields` — los 18 campos (17 hasta el Lote 45, que sumó `contactName`) con `required` (obligatorio en la fila) y `defaultAllowed` (admite
  valor por defecto en la plantilla).

Una plantilla es **general** (sin cliente, sirve para cualquier lote) o **de un cliente** (solo sirve para lotes de ese
cliente). Los `defaults` llenan un campo cuando la celda está vacía o la columna no existe; la celda con valor siempre gana.
Las plantillas de consignatarios no se ven por `/api/v1/import-templates` (esa ruta es la de envíos) ni al revés.

| Campo | Obligatorio en la fila | Admite default | Va a |
|---|---|---|---|
| `code` | no | no | código del consignatario (máx. 40) |
| `name` | **sí** (columna) | no | nombre (máx. 200) |
| `line1` | **sí** (columna) | no | dirección línea 1 (máx. 200) |
| `line2` | no | sí | línea 2 (máx. 200) |
| `city` | **sí** (columna o default) | sí | ciudad (máx. 100) |
| `state` | no | sí | estado (máx. 100) |
| `postalCode` | no | sí | código postal (máx. 20) |
| `country` | no (PR si falta) | sí | país ISO de 2 letras, activo y no deshabilitado por la compañía |
| `clientCode` | no | no | dueño: código del cliente |
| `clientName` | no | no | dueño: nombre del cliente |
| `defaultServiceMinutes` | no (0) | sí | minutos de servicio, entero ≥ 0 |
| `defaultWindowStart` / `defaultWindowEnd` | no (las dos o ninguna) | sí | ventana horaria, `HH:mm` de 24 horas (también `H:mm` y `HH:mm:ss`) |
| `accessNotes` / `deliveryNotes` | no | sí | notas (máx. 500) |
| `allowDupInvoice` | no (no) | sí | permitir facturas repetidas: `sí/si/s/yes/y/true/1` o `no/n/false/0` (sin distinguir mayúsculas) |
| `contactPhone` | no | sí | teléfono, se guarda como contacto **PHONE principal** (etiqueta «Teléfono»); desde el Lote 45 se guardan los 10 dígitos (ver §2.2) |
| `contactName` (Lote 45) | no | sí | persona de contacto del consignatario (máx. 150) |

| Caso (plantilla) | Mensaje exacto | HTTP |
|---|---|---|
| Sin columnas | `Indique al menos una columna.` (campo `columns`) | 400 |
| Más de 100 columnas | `Máximo 100 columnas.` | 400 |
| Posición &lt; 1 / &gt; 500 / repetida | `La posición debe ser 1 o mayor.` / `La posición no puede exceder 500.` / `La posición N está repetida.` (campo `columns[i].position`) | 400 |
| Campo desconocido / repetido | `Campo desconocido: X.` / `El campo X está repetido.` (campo `columns[i].field`; en defaults, `defaults.X`) | 400 |
| Falta la columna `name` o `line1` | `La plantilla debe incluir las columnas name y line1.` (campo `columns`) | 400 |
| Sin `city` en columna ni en defaults | `La plantilla debe indicar la ciudad (city) en una columna o en defaults.` | 400 |
| `clientCode` y `clientName` a la vez | `Use clientCode o clientName, no ambos.` | 400 |
| Default de `code`, `name`, `line1`, `clientCode` o `clientName` | `El campo X no admite valor por defecto.` (campo `defaults.X`) | 400 |
| Default con valor inválido (minutos, horas, `allowDupInvoice`, formato de país, largo, teléfono) | el mismo mensaje que daría la fila (tabla de abajo), en `defaults.X` | 400 |
| Nombre vacío / largo | `El nombre de la plantilla es obligatorio.` / `Máximo 120 caracteres.` | 400 |
| Delimitador inválido | `El delimitador debe ser un solo carácter (p. ej. ',' o ';'), distinto de comilla y salto de línea.` | 400 |
| Nombre repetido (en plantillas de consignatarios) | `Ya existe una plantilla de importación con ese nombre.` | 409 |
| `clientPublicId` y `makeGeneral` a la vez (PATCH) | `Indique un cliente o marque la plantilla como general, no ambos.` | 400 |
| `defaults` y `clearDefaults` a la vez (PATCH) | `Indique defaults nuevos o clearDefaults, no ambos.` | 400 |
| Desactivar una inactiva / reactivar una activa | `La plantilla ya está inactiva.` / `La plantilla ya está activa.` | 409 |
| Plantilla inexistente, de otra compañía o de envíos | `Plantilla de importación no encontrado.` | 404 |
| `kind` distinto de `CONSIGNEE` en el cuerpo | `Tipo de plantilla desconocido: X.` | 400 |

#### Validar (paso 1 de 2)

Cómo se usa:
- `POST /api/v1/locations/import/validate` (JSON) `{ "templatePublicId": guid, "clientPublicId"?: guid, "content": "<CSV>", "fileName"?: "archivo.csv" }`
- `POST /api/v1/locations/import/validate-file` (`multipart/form-data`): `templatePublicId`, `clientPublicId` (opcional) y el
  archivo en `file`.
- `GET /api/v1/locations/import/{batchPublicId}` — vuelve a mostrar la vista previa (y, tras confirmar, el resultado).

Límites: **5.000 filas y 2 MB** por archivo (la petición multipart completa, hasta 4 MB). El archivo debe estar en **UTF-8**.

**Cliente del lote** (`clientPublicId`) es opcional. **Dueño de cada fila**, en este orden: la columna `clientCode`; si no, la
columna `clientName`; si no, el cliente del lote; si tampoco hay, la fila queda **compartida** (sin dueño, la ven todos los
clientes). Un cliente **suspendido** sí admite consignatarios; uno **dado de baja**, no.

La respuesta (200) trae `batchPublicId`, `status` (`VALIDATED`), `rowCount`, `validRows` y `rows[]` con, por fila: `values`
(lo leído, ya normalizado: hora `HH:mm`, `allowDupInvoice` `true/false`, país en mayúsculas, teléfono con formato),
`owner` (`clientPublicId`/`clientName`, o `isShared: true`; `null` si no se pudo resolver), `errors` por campo (la fila **no**
se creará), `warnings` (avisos: la fila **sí** se creará) e `isValid`.

| Caso (petición) | Mensaje exacto | HTTP |
|---|---|---|
| Sin contenido o archivo vacío | `Indique el contenido del archivo CSV.` (campo `content` o `file`) | 400 |
| Más de 5.000 filas o 2 MB | `El archivo supera el límite de 5.000 filas o 2 MB.` (campo `content` o `file`) | 400 |
| Petición multipart de más de 4 MB | (lo corta el servidor) | 413 |
| Archivo sin filas de datos | `El archivo no tiene filas de datos.` | 400 |
| Plantilla inexistente, de envíos o de otro cliente | `Plantilla de importación no encontrado.` | 404 |
| Plantilla inactiva | `La plantilla está inactiva; reactívela o elija otra.` (campo `templatePublicId`) | 400 |
| Cliente del lote inexistente o de otra compañía | `Cliente no encontrado.` | 404 |
| Cliente del lote dado de baja | `El cliente está dado de baja; solo se consulta su historial.` | 409 |
| `validate` sin `Content-Type: application/json` | — | 415 |
| Sin el permiso / módulo Listas apagado | `Falta el permiso 'locations.create'.` / `module_disabled` | 403 |

| Caso (fila; la vista previa responde 200 y la fila queda inválida) | Mensaje exacto (campo) |
|---|---|
| Sin nombre / línea 1 / ciudad | `El nombre es obligatorio.` (`name`) / `La dirección (línea 1) es obligatoria.` (`line1`) / `La ciudad es obligatoria.` (`city`) |
| Texto largo | `Máximo N caracteres.` |
| País con formato inválido | `El código de país debe ser ISO 3166-1 alfa-2 (2 letras).` (`country`) |
| País desconocido o deshabilitado por la compañía | `País desconocido: XX.` (`country`) |
| Minutos no enteros / negativos | `Los minutos de servicio deben ser un número entero.` / `Los minutos de servicio no pueden ser negativos.` (`defaultServiceMinutes`) |
| Hora ilegible | `La hora debe tener el formato HH:mm (24 horas).` (`defaultWindowStart` o `defaultWindowEnd`) |
| Solo una hora de la ventana | `La ventana horaria requiere hora de inicio y hora de fin.` (la que falta) |
| Fin no posterior al inicio | `La hora de fin de la ventana debe ser posterior a la de inicio.` (`defaultWindowEnd`) |
| `allowDupInvoice` ilegible | `El valor de allowDupInvoice debe ser sí o no (también 1/0 o true/false).` |
| Teléfono inválido | `Teléfono inválido.` (`contactPhone`) |
| `clientCode` sin coincidencia | `No existe un cliente con el código 'X'.` (`clientCode`) |
| `clientName` sin coincidencia / con varias | `No existe un cliente con el nombre 'X'.` / `Hay más de un cliente con el nombre 'X'; use clientCode.` (`clientName`) |
| Cliente de la fila dado de baja | `El cliente está dado de baja; solo se consulta su historial.` (`clientCode` o `clientName`) |
| ~~La fila nombra un cliente distinto al del lote~~ | Ya **no es error** desde el Lote 45 (2026-10-13): el cliente del lote es el *cliente por defecto* y la fila con su propio cliente válido queda con ESE cliente (ver §2.2). El mensaje `La fila indica el cliente 'A', pero el lote es del cliente 'B'.` ya no sale. |

#### Duplicados

Se comparan por **dueño** (el mismo cliente, o las dos compartidas). El código no distingue mayúsculas; nombre y línea 1 se
comparan sin tildes, sin mayúsculas y sin espacios dobles. Una fila del cliente choca con los consignatarios de ese cliente
**y** con los compartidos; una fila compartida, solo con los compartidos.

| Caso | Mensaje exacto | Tipo |
|---|---|---|
| Código repetido en el archivo (mismo dueño) | `El código X se repite en la fila N del archivo.` (`code`, en la fila posterior) | error |
| Nombre repetido en el archivo (mismo dueño; la dirección no cuenta) | `El nombre 'X' se repite en la fila N del archivo; agregue un distintivo al nombre (o al número).` (`name`, en la fila posterior) | error |
| Código que ya usa un consignatario activo | `Ya existe un consignatario con el código 'X' ('Nombre').` (`code`) | error |
| Código que usa un consignatario inactivo | `Ya existe un consignatario inactivo con el código 'X' ('Nombre'); reactívelo en vez de importarlo.` (`code`) | error |
| Nombre de un consignatario activo del mismo dueño (la dirección no cuenta) | `Ya existe un consignatario activo con el mismo nombre. Agregue un distintivo al nombre (o al número) para distinguirlos.` (`name`) | error |
| Nombre de uno inactivo del mismo dueño | `Ya existe un consignatario inactivo con el mismo nombre; reactívelo en vez de importarlo, o agregue un distintivo al nombre (o al número).` (`name`) | error |
| Fila compartida con un código que usa un consignatario de un cliente | `El código X también lo usa un consignatario de un cliente; para ese cliente seguirá resolviendo al suyo.` | aviso |

#### Confirmar (paso 2 de 2) y descartar

Cómo se usa: `POST /api/v1/locations/import/{batchPublicId}/confirm` con cuerpo vacío, `{}` (todas las filas válidas) o
`{ "rows": [1, 3] }` (solo esas filas; las demás quedan `skipped`). `POST /api/v1/locations/import/{batchPublicId}/discard`
descarta un lote pendiente.

Qué hace al confirmar: cada fila elegida se crea **en su propia transacción** (una que falla no tumba las demás). Antes de
crear, se vuelve a revisar contra lo que exista **en ese momento** (el cliente sigue activo; el código y el nombre + línea 1
no los ocupó nadie desde que se validó): las importaciones de una misma compañía se confirman **de a una fila a la vez**,
así que dos lotes con el mismo código nuevo nunca crean dos consignatarios. El lote pasa a **CONFIRMED** con el comentario
«Consignatarios creados: C; fallidos: F; omitidos: O.». La respuesta trae `created`, `failed`, `skipped` y `rows[]` con
`locationPublicId` (creado) o `error` (falló). Las filas fallidas no se reintentan: valide un archivo nuevo con ellas.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `rows` con una fila que no existe | `La fila N no existe en el lote.` (campo `rows[N]`) | 400 |
| `rows` con una fila con errores | `La fila N tiene errores y no se puede confirmar.` (campo `rows[N]`) | 400 |
| Ninguna fila válida que crear | `El lote no tiene filas válidas para crear.` (campo `rows`) | 400 |
| Lote ya confirmado | `El lote ya fue confirmado; valide un archivo nuevo para importar más consignatarios.` | 409 |
| Lote descartado | `El lote fue descartado; valide el archivo de nuevo.` | 409 |
| Dos confirmaciones a la vez, o descartar mientras se confirma | `El lote ya se está confirmando o fue confirmado; consulte su resultado.` | 409 |
| Cliente del lote dado de baja después de validar | `El cliente está dado de baja; solo se consulta su historial.` | 409 |
| Descartar un lote que no está pendiente | `Solo se descarta un lote pendiente de confirmar.` | 422 |
| Lote inexistente, de otra compañía o de envíos | `Lote de importación no encontrado.` | 404 |
| Fila que choca al confirmar (alguien creó el código entretanto) | el mensaje de duplicado de la tabla anterior, en `rows[i].error` | 200 (fila fallida) |
| Error inesperado en una fila | `Error inesperado al crear el consignatario; revise la fila e intente de nuevo.` en `rows[i].error` | 200 (fila fallida) |

Estatus del lote (`ImportBatchStatus`): `VALIDATED` → `CONFIRMED` (confirmar) o → `DISCARDED` (descartar). El historial
(`GET /api/v1/status/history/IMPORT_BATCH/{id}`) pide el permiso `orders.view` (el del importador de envíos): un usuario que
solo tiene permisos de consignatarios recibe 403 ahí, aunque sí ve el lote por `GET /api/v1/locations/import/{id}`.

### 2.2 Importación de consignatarios: Excel, «(No usar)», columnas unidas, cliente por defecto y persona de contacto (Lote 45, F-B2)

Qué cambia (mismos permisos y módulo que §2.1; no hay permisos nuevos). Pantalla: [frontend/fb2-importacion-consignatarios.md](frontend/fb2-importacion-consignatarios.md).

- **Excel (.xlsx) además de CSV**, con el mismo flujo (validar → vista previa → confirmar). El servidor lee el libro (librería
  ExcelDataReader, licencia MIT): una sola **hoja**, la que se pida en `sheet` o la primera. Cada celda se convierte a texto como
  la ve el usuario: un número entero sin «.0» (`901`), con formato de ceros a la izquierda (`00000`) se rellena (`00901`); una
  fecha con hora cero → `yyyy-MM-dd`, con hora → `yyyy-MM-dd HH:mm`; una celda **solo de hora** (08:00) → `HH:mm`, lista para la
  ventana horaria. El `.xls` viejo (binario) no se admite. Los límites son los del CSV (5.000 filas de datos, 2 MB de archivo;
  además el libro descomprimido no puede pasar de 64 MB).
- **CSV en UTF-8 o en Windows-1252**: la codificación se detecta sola (marca BOM; si no, UTF-8 cuando los bytes lo son; si no,
  Windows-1252, que es lo que guarda Excel en Windows al exportar «CSV»). La ñ y los acentos llegan bien en los dos casos.
- **Revisar el archivo sin guardar nada**: `POST /api/v1/locations/import/inspect-file` (multipart: `file`, `delimiter`,
  `hasHeader`, `sheet` opcional; `locations.create`) responde `format` (`CSV`/`XLSX`), `sheets` (hojas del libro; vacío en CSV),
  `sheet` (la leída), `rowCount`, `columns[]` (`position`, `header`, hasta 3 `samples`) y `sampleRows` (las 3 primeras filas).
  Lo usa la pantalla para el selector de hoja y para mostrar encabezados y muestras en el editor.
- `validate-file` acepta `sheet`. El JSON `validate` sigue siendo solo CSV (texto en `content`).
- **Columna «(No usar)»**: en `columns`, `{ "position": 3, "field": "ignore" }`. Se guarda en la plantilla (el editor la
  recuerda) y al importar la columna se ignora por completo; **la fila no se salta**. Una columna «No usar» no puede tener además
  otro campo.
- **Una columna a varios campos** (p. ej. el código va a `code` y también forma parte del nombre): la misma `position` en dos
  entradas con campos distintos.
- **Un campo con varias columnas unidas**: el campo repetido en varias posiciones, con `part` (envío 1, 2…) y `separator` (texto
  que va ANTES de esa parte; sin él, un espacio). Ejemplo «Nombre = Código + « - » + Nombre»:
  `[{"position":1,"field":"code"},{"position":1,"field":"name","part":1},{"position":2,"field":"name","part":2,"separator":" - "}]`.
  Una celda vacía no deja su separador; si todas las partes vienen vacías, el campo toma su valor por defecto. Pueden unirse los
  campos de texto (número, nombre, direcciones, pueblo, estado, ZIP, notas, teléfono, persona de contacto), hasta 5 columnas; los
  de formato propio (país, cliente, minutos, horas, factura repetida) toman una sola. Al guardar, el servidor numera las partes y
  quita `part`/`separator` donde no aplican.
- **Cliente por defecto**: `clientPublicId` del lote es el cliente de **todo el archivo**; una fila que trae su propio cliente
  válido (`clientCode`/`clientName`) queda con **ese** cliente aunque sea otro (antes era error). La plantilla guarda su cliente
  por defecto: `defaultClientPublicId` en el alta y en el PATCH (`clearDefaultClient: true` lo quita); el DTO trae
  `defaultClientPublicId` y `defaultClientName`. Confirmar con `{ "rememberClient": true }` guarda en la plantilla el cliente del
  lote (o ninguno, si fue «compartidos»).
- **Persona de contacto**: campo nuevo `contactName` del consignatario (`dbo.Location.ContactName`, máx. 150) en `POST`/`PATCH
  /locations`, en el DTO y como destino del mapeo (admite valor por defecto). El teléfono sigue siendo un contacto PHONE.
- **Teléfono**: se aceptan los formatos de los archivos reales (`787-555-0101`, `787 555-0101`, `7875550101`, `(787)555-0101`,
  `+1 787…`) y se guardan los **10 dígitos**. Si la celda trae **dos o más números**, se toma el primero y la fila lleva un aviso.
- **ZIP** de Puerto Rico o Estados Unidos que perdió los ceros (3 o 4 dígitos) se rellena a 5 (`901` → `00901`). Un ZIP vacío no es
  error (el lote siguiente lo deducirá del pueblo con la lista de códigos postales).

| Caso | Mensaje exacto | HTTP / tipo |
|---|---|---|
| Archivo `.xls` (Excel 97-2003) | `Formato de archivo no admitido: use CSV o Excel (.xlsx).` (campo `file`) | 400 |
| Libro dañado o que no es un .xlsx | `No se pudo leer el archivo de Excel; ábralo y guárdelo otra vez como .xlsx.` (campo `file`) | 400 |
| `sheet` que no está en el libro | `El libro no tiene la hoja 'X'.` (campo `sheet`) | 400 |
| Libro de más de 5.000 filas, 2 MB o 64 MB descomprimido | `El archivo supera el límite de 5.000 filas o 2 MB.` | 400 |
| Archivo vacío (también `inspect-file`) | `Indique el contenido del archivo CSV.` (campo `file`) | 400 |
| Columna «No usar» con otro campo | `La columna N está marcada «No usar» y también tiene un campo asignado.` (campo `columns[i].field`) | 400 |
| La misma columna dos veces en el mismo campo | `La posición N está repetida.` (campo `columns[i].position`) | 400 |
| Campo de formato propio en dos columnas | `El campo X está repetido.` | 400 |
| Más de 5 columnas en un campo | `El campo X admite a lo sumo 5 columnas unidas.` | 400 |
| Separador de más de 10 caracteres | `El separador admite a lo sumo 10 caracteres.` (campo `columns[i].separator`) | 400 |
| Cliente por defecto distinto del cliente de una plantilla de cliente | `El cliente por defecto debe ser el mismo cliente de la plantilla.` (campo `defaultClientPublicId`) | 400 |
| `defaultClientPublicId` y `clearDefaultClient` a la vez | `Indique un cliente por defecto o quítelo, no ambos.` | 400 |
| Cliente por defecto dado de baja / de otra compañía | `El cliente está dado de baja; solo se consulta su historial.` / `Cliente no encontrado.` | 409 / 404 |
| Persona de contacto larga | `Máximo 150 caracteres.` (`contactName`) | fila con error / 400 en `/locations` |
| Dos teléfonos en la celda | `La celda del teléfono trae más de un número; se tomó el primero (7875550101).` | aviso (la fila se importa) |
| Número de consignatario repetido en el archivo | `El código X se repite en la fila N del archivo.` (nombra el número y la primera fila donde aparece) | error de fila |

Estructura (sección 2026-10-13 de `Diseño/logistica-db-update.sql`): `dbo.Location.ContactName NVARCHAR(150) NULL` y
`dbo.ImportTemplate.DefaultClientId INT NULL` con llave a `dbo.Client`. Sin permisos nuevos.

Casos frecuentes:
- **Excel**: guarde el archivo como **«CSV UTF-8 (delimitado por comas)»**. Con «CSV (delimitado por comas)» Excel lo guarda en
  Windows-1252 y las tildes llegan dañadas («Mar�a»). Si su Excel usa `;`, cree la plantilla con `"delimiter": ";"`.
- **Consignatarios compartidos** (de todos los clientes): no indique cliente del lote ni columna de cliente.
- **Un cliente con muchos consignatarios**: indique el cliente del lote; no hace falta la columna de cliente.
- **«…reactívelo en vez de importarlo»**: el consignatario ya existía y está inactivo; reactívelo desde su ficha
  (`POST /locations/{id}/reactivate`) y quite esa fila del archivo.
- **Archivos grandes**: confirmar 5.000 filas toma alrededor de medio minuto; no cierre la pantalla. Si la conexión se corta a
  mitad, lo creado hasta ese momento queda creado, pero el lote puede quedar `VALIDATED` y reservado (confirmar o descartar
  responden 409 «El lote ya se está confirmando o fue confirmado…»). En ese caso valide **el mismo archivo otra vez**: las
  filas que ya se crearon salen con «Ya existe un consignatario con el código…» y se confirma solo el resto.

### 2.3 Importación de consignatarios: pueblo, estado y ZIP con la lista de códigos postales, y variantes de mapeo (Lote 46, F-B3)

Mismos permisos y módulo que §2.1 (no hay permisos nuevos). Pantalla: [frontend/fb3-variantes-y-codigos-postales.md](frontend/fb3-variantes-y-codigos-postales.md).

**Lista de códigos postales.** `dbo.PostalLocality` (global) gana `ZipClass` (`STANDARD` normal, `PO_BOX` apartados, `UNIQUE` único de una entidad, `MILITARY`
APO/FPO; NULL = sin clasificar), `LocaleType`, `DistrictName`, `DistrictNo`, `Latitude`, `Longitude` y `GeoAccuracy` (sección 2026-10-14 de
`Diseño/logistica-db-update.sql`, con `CK_PostalLocality_ZipClass` e `IX_PostalLocality_PostalCode`). Los llena el script de **datos** aparte
`Diseño/logistica-db-zip-catalog.sql` (41.486 ZIP; USPS «Zip Locale Detail» y **GeoNames**, licencia CC BY 4.0: hay que darle crédito), que se corre a mano con
`sqlcmd … -I -f 65001 -i "Diseño/logistica-db-zip-catalog.sql"` **después** de `db-update`; es idempotente y, si faltan las columnas, se detiene sin tocar nada
(«logistica-db-zip-catalog.sql: faltan las columnas nuevas de dbo.PostalLocality; aplique primero logistica-db-update.sql (db-update). No se cambió nada.»). El API lee
la lista en memoria por una hora: después de correr el script, reinicie el API (o espere).

**GET /api/v1/postal-localities** (usuario autenticado) devuelve `zipClass` en cada fila y acepta `?zipClass=` (lista separada por comas de `STANDARD`, `PO_BOX`,
`UNIQUE`, `MILITARY`, o `DELIVERY` = los de entrega: normal, único y los sin clasificar). Sin filtro salen todos (direcciones postales). El alta y la edición de
consignatarios usan `DELIVERY`.

**Inferencia en la importación** (filas de país cubierto por la lista: Puerto Rico y Estados Unidos; antes de exigir los obligatorios):

- Solo ZIP → `city` (en Puerto Rico el municipio, con acentos) y `state` de la lista, si faltan.
- Solo pueblo → `postalCode` = el PRIMER ZIP de entrega del pueblo (ciudad postal; si no hay, municipio), en orden fijo: `STANDARD`, luego `UNIQUE`, luego sin
  clasificar; a igual tipo, el menor código. Con el estado de la fila se escoge entre pueblos homónimos de varios estados.
- ZIP y pueblo → se guardan los dos; si no coinciden, aviso. El estado de la fila nunca se cambia (si difiere del ZIP, aviso).
- ZIP `PO_BOX` o `MILITARY` → se sustituye por el primer ZIP de entrega de la MISMA ciudad (ciudad postal del ZIP; si no tiene, su municipio); la fila lleva
  `originalPostalCode` y el aviso «Código postal ajustado…». Sin uno compatible, la fila se rechaza.
- Los ZIP se comparan como texto de 5 dígitos (ZIP+4 vale por sus 5); los pueblos, sin acentos ni mayúsculas.
- Cada fila trae `inferred` (campos completados: `city`, `state`, `postalCode`) y `originalPostalCode` en la vista previa (`validate`, `GET` del lote) y en el
  resultado de `confirm`.
- La plantilla ya no exige `city`: basta `city` **o** `postalCode` (en columna o en defaults). En la lista de campos (`/locations/import/fields`), `city` deja de ser
  `required`.

**Variantes de mapeo** (solo plantillas de consignatarios): `variants: [{ name, conditions: [{ position, operator, value }], columns: [...] }]` en el alta y el PATCH
(`ImportTemplate.VariantsJson`; `ColumnsJson` queda con las columnas de la primera). `operator`: `EMPTY`, `NOT_EMPTY`, `EQUALS`, `CONTAINS` (texto sin acentos ni
mayúsculas). A cada fila se le aplica la PRIMERA variante cuyas condiciones se cumplen todas; sin condiciones aplica siempre (solo puede ir al final). Cada variante se
valida con las reglas de las columnas; los defaults son comunes. PATCH: `variants` ausente = sin cambio; `[]` = vuelve a una sola variante (con `columns` o las de la
primera). Las filas traen `variant` (1..n) y `variantName`; la que no encaja trae `cells` (sus celdas). Una plantilla sin variantes funciona igual que antes.

| Caso | Mensaje exacto | HTTP / tipo |
|---|---|---|
| ZIP con otro formato | `El código postal X no es válido: use 5 dígitos (o ZIP+4, como 00901-1234).` (`postalCode`) | error de fila |
| ZIP que no está en la lista | `El código postal X no está en la lista de códigos postales; corríjalo, o déjelo vacío e indique el pueblo.` (`postalCode`) | error de fila |
| Pueblo que no está en la lista | `El pueblo 'X' no está en la lista de códigos postales; corríjalo o indique el código postal.` (`city`) | error de fila |
| Pueblo sin ZIP de entrega | `El pueblo 'X' no tiene un código postal de entrega (solo de apartados postales o militares); indique el código postal de la dirección física.` (`city`) | error de fila |
| Pueblo en varios estados (EE. UU.) | `El pueblo 'X' existe en varios estados (IL, MA); indique el estado o el código postal.` (`city`) | error de fila |
| ZIP de apartados / militar sin sustituto | `El código postal X es de apartados postales (P.O. Box) y Y no tiene un código postal de entrega con que sustituirlo; indique el código postal de la dirección física.` (o `…es de correo militar (APO/FPO)…`) (`postalCode`) | error de fila |
| Ninguna variante aplica | `Ninguna variante de la plantilla aplica a esta fila; revise las condiciones de las variantes.` (`variant`) | error de fila |
| ZIP de apartados sustituido | `Código postal ajustado: 00734 es de apartados postales (P.O. Box); se usó 00715, de entrega en Ponce.` | aviso |
| Pueblo / estado / ZIP completados | `Pueblo tomado del código postal 00952: Toa Baja.` · `Estado tomado de la lista de códigos postales: PR.` · `Código postal asignado: 00956, el primero de entrega de Bayamón.` | aviso |
| ZIP y pueblo no coinciden | `El código postal 00716 es de Ponce, no de Caguas; se guardan los dos como vienen.` | aviso |
| Estado distinto del del ZIP | `El estado NY no coincide con el del código postal (PR); se guarda como viene.` | aviso |
| Plantilla sin pueblo ni ZIP | `La plantilla debe indicar la ciudad (city) o el código postal (postalCode), en una columna o en defaults.` (`columns`; antes «…la ciudad (city) en una columna o en defaults.») | 400 |
| Variante sin condición que no es la última | `La variante N no tiene condición y deja sin uso a las que le siguen; póngala al final o agréguele una condición.` (`variants[i].conditions`) | 400 |
| «igual a» / «contiene» sin valor | `La condición «igual a» o «contiene» necesita un valor.` (`variants[i].conditions[j].value`) | 400 |
| Operador desconocido | `Condición desconocida: X. Use EMPTY, NOT_EMPTY, EQUALS o CONTAINS.` (`…operator`) | 400 |
| Columna de la condición fuera de rango | `La posición debe ser 1 o mayor.` / `La posición no puede exceder 500.` (`…position`) | 400 |
| Más de 10 variantes / más de 5 condiciones | `Una plantilla admite a lo sumo 10 variantes.` / `Una variante admite a lo sumo 5 condiciones.` | 400 |
| Nombre de variante largo / valor largo | `Máximo 60 caracteres.` / `Máximo 100 caracteres.` | 400 |
| Mapeo de una variante inválido | el mensaje de siempre con la llave de la variante (p. ej. `variants[1].columns`: `La plantilla debe incluir las columnas name y line1.`) | 400 |
| `columns` y `variants` a la vez (o `columns` en el PATCH de una plantilla con variantes) | `Esta plantilla usa variantes: las columnas van dentro de cada variante (variants), no en columns.` (`columns`) | 400 |
| Variantes en una plantilla de envíos | `Las variantes de mapeo solo existen en las plantillas de consignatarios.` (`variants`) | 400 |
| `zipClass` desconocido en `/postal-localities` | `Tipo de código postal desconocido: X. Use STANDARD, PO_BOX, UNIQUE, MILITARY o DELIVERY.` (`zipClass`) | 400 |

Casos frecuentes:
- **Archivo con dos disposiciones de columnas**: una variante por disposición; la condición suele ser una columna que solo una de las dos trae (p. ej. la 9 = «PUERTO
  RICO» en una y vacía en la otra).
- **El pueblo trae el barrio**: si el archivo trae «Bo. Pueblo» en una columna y el municipio en otra, mapee el municipio a **Pueblo** y el barrio a **Dirección (línea 2)**.
- **Todos los ZIP salen «sin clasificar» / no hay «Código postal ajustado»**: falta correr `logistica-db-zip-catalog.sql` (o el API aún tiene la lista vieja en
  memoria: reinícielo).

---

## 3. Direcciones y punto de recogido del cliente (resumen)

- **Física**: la localización activa tipo CORPORATE del cliente (`physicalAddress` en la ficha).
- **Postal**: la activa tipo BILLING (`postalAddress`); si no existe, se entiende "misma que la física".
- **Recogido habitual**: el almacén elegido en el perfil (`defaultPickupLocationPublicId`, tipo PICKUP o BOTH) o, si no hay,
  la corporativa (`pickupAddress.isDefaultFromCorporate: true`). Puede haber tantos almacenes como se quiera (`pickupLocations`).
- **Consignatarios**: direcciones tipo DELIVERY; no son clientes, no aparecen en la lista de clientes ni tienen contrato.

---

## 4. Contratos

### 4.1 Contrato vigente

El "contrato vigente" de un cliente en una fecha es el contrato activo con estatus **ACTIVE** y `startDate` ≤ esa fecha.
**La fecha fin no se evalúa**: un contrato con `endDate` pasada sigue vigente hasta que alguien lo pase a EXPIRED o
CANCELLED (si no se renueva, se continúa con lo que hay). Si no hay ACTIVE, se usa el **DRAFT más reciente** por fecha de
inicio (para configurar tarifas antes de activar). Nunca EXPIRED ni CANCELLED. Es el contrato que usan la cotización, los
servicios especiales y el resumen `billingSummary` de la ficha y de la lista de clientes.

### 4.2 Lista y ficha

Quién puede: `contracts.read`. Cómo se usa: `GET /api/v1/contracts?clientId={publicId}&includeInactive=false`;
`GET /api/v1/contracts/{publicId}` → número, título, fechas, `autoRenew`, estatus, moneda, disparador de cobro
(`billingTrigger`: BY_PICKUP | BY_DELIVERY | MIXED), notas, `billingModel` (5 banderas + `summary`), `dispatchFee`,
`codFee` (`null` si nunca se configuró), `serviceLevels`, `canEdit` (si el estatus actual permite editar) y `rowVersion`.

### 4.3 Contrato adicional

Qué hace: crea otro contrato del cliente, en DRAFT, con "Por servicio" encendido y número automático `{Código}-C{n+1}`
(o el número explícito que se envíe).

Quién puede: `contracts.create`. Cómo se usa: `POST /api/v1/contracts`
`{ "clientPublicId", "contractNumber"?, "title", "startDate", "endDate"?, "autoRenew": false, "currency"?, "billingTrigger"?, "notes"?, "serviceLevels"? }`

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `title` vacío / > 200 | obligatorio | `El título del contrato es obligatorio.` / `Máximo 200 caracteres.` | 400 |
| `endDate` < `startDate` | vigencia | `La fecha fin no puede ser anterior a la fecha de inicio.` | 400 |
| `contractNumber` ya existe | único por compañía | `Ya existe un contrato con el número 'X'.` | 409 |
| `currency` / `billingTrigger` desconocidos | listas `Currency` / `BillingModel` | `Lista Currency 'X' no encontrada.` / `Lista BillingModel 'X' no encontrada.` | 404 |
| SLA inválido | ver 4.6 | — | 400 |

### 4.4 Datos generales (título, "cliente desde", fecha fin, renovación, moneda, disparador, notas)

Qué hace: edición inline de los datos generales. **`startDate` ("cliente desde") es editable** y decide el contrato
vigente; `endDate` y `autoRenew` son informativos (no hay vencimiento ni renovación automáticos). `clearEndDate: true`
borra la fecha fin; `notes: ""` borra las notas.

Quién puede: `contracts.update`. Cómo se usa: `PATCH /api/v1/contracts/{publicId}` `{ "title"?, "startDate"?, "endDate"?, "clearEndDate"?, "autoRenew"?, "currency"?, "billingTrigger"?, "notes"?, "rowVersion"? }`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Fecha fin (nueva o existente) anterior al inicio (nuevo o existente) | `La fecha fin no puede ser anterior a la fecha de inicio.` (campo `startDate` si solo se envió el inicio; `endDate` en otro caso) | 400 |
| `endDate` y `clearEndDate` a la vez | `Indique una fecha fin o márquela para borrar, no ambas.` | 400 |
| `rowVersion` distinto | `El contrato fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Estatus que no permite editar (EXPIRED/CANCELLED por defecto) | `El estatus actual no permite la acción 'EDIT_CONTRACT'.` | 422 |

### 4.5 Modelo de facturación, cargo por despacho y cargo por COD

Qué hace: los **5 checkboxes** del contrato — Por servicio, Pieza extra, Despacho, COD, Especiales — y su resumen legible
("Por servicio, Pieza extra, Despacho, COD, Especiales"; "Sin componentes" si ninguno). **Desmarcar no borra nada**: el cargo
por despacho, el cargo por COD, las tarifas y los servicios especiales se conservan y simplemente no se cobran (al volver a
marcar, reaparecen). El **cargo por despacho** es un monto por orden. El **cargo por COD** es **FIXED** (monto fijo por orden)
o **PERCENT** (por ciento **del monto COD cobrado**, nunca del total del envío); "ninguno" = checkbox COD apagado, que es
el valor por defecto de todo contrato nuevo.

Quién puede: `contracts.update`. Cómo se usa:
- `PATCH /api/v1/contracts/{publicId}/billing-model` `{ "billPerService"?, "billExtraPiece"?, "billDispatchFee"?, "billCodFee"?, "billSpecialServices"? }`
- `PATCH /api/v1/contracts/{publicId}/dispatch-fee` `{ "amount": 3 }`
- `PATCH /api/v1/contracts/{publicId}/cod-fee` `{ "type": "PERCENT", "value": 2.5 }`

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `amount` negativo | ≥ 0 | `El cargo por despacho no puede ser negativo.` | 400 |
| `type` vacío | FIXED o PERCENT | `Indique el tipo del cargo por COD: FIXED (monto fijo por envío) o PERCENT (por ciento del monto COD).` | 400 |
| `type` desconocido | — | `Tipo de cargo por COD desconocido: 'X'. Use FIXED o PERCENT.` | 400 |
| `value` ausente | — | `Indique el valor del cargo por COD.` | 400 |
| FIXED negativo | ≥ 0 | `El cargo fijo por COD no puede ser negativo.` | 400 |
| PERCENT fuera de 0..100 | — | `El por ciento del cargo por COD debe estar entre 0 y 100.` | 400 |

### 4.6 Niveles de servicio (SLA)

Qué hace: un nivel por tipo de servicio (horas máximas de tránsito, ventana de recogido en minutos, meta de puntualidad,
penalidad). `PUT` **reemplaza la lista completa**: los tipos ausentes se dan de baja.

Quién puede: `contracts.update`. Cómo se usa: `PUT /api/v1/contracts/{publicId}/service-levels`
`[ { "serviceType": "STANDARD", "maxTransitHours": 24, "pickupWindowMin"?: 30, "onTimeTargetPct"?: 95, "penaltyAmount"?: 10 } ]`

| Campo / caso | Regla | Mensaje exacto (campo `serviceLevels[i].*`) | HTTP |
|---|---|---|---|
| `serviceType` vacío | obligatorio | `El tipo de servicio del nivel es obligatorio.` | 400 |
| Tipo repetido (sin distinguir mayúsculas) | uno por tipo | `El tipo de servicio 'STANDARD' está repetido: solo puede haber un nivel de servicio por tipo.` | 400 |
| `maxTransitHours` ≤ 0 | — | `Las horas máximas de tránsito deben ser mayores que cero.` | 400 |
| `pickupWindowMin` < 0 | — | `La ventana de recogido (minutos) no puede ser negativa.` | 400 |
| `onTimeTargetPct` fuera de 0..100 | — | `La meta de puntualidad debe estar entre 0 y 100.` | 400 |
| `penaltyAmount` < 0 | — | `La penalidad no puede ser negativa.` | 400 |
| Tipo de servicio inexistente | lista `ServiceType` | `Tipo de servicio desconocido: 'X'.` | 400 |
| Duplicado de último momento | índice único | `Solo puede haber un nivel de servicio activo por tipo de servicio.` | 409 |

### 4.7 Estatus del contrato y sus efectos

Estatus (`ContractStatus`): **DRAFT** (inicial) → **ACTIVE**; **EXPIRED** y **CANCELLED** son terminales (se llega desde
cualquier etapa; no se sale de ellos). Cómo se usa: `POST /api/v1/contracts/{publicId}/status` `{ "toCode": "ACTIVE", "comment"? }` (`contracts.update`).

Efecto al pasar a **ACTIVE** (`ContractStatusEffect`): se comprueba que el cliente no esté SUSPENDED y que no tenga ya
**otro contrato ACTIVE** (un solo contrato activo por cliente a la vez; para reemplazarlo hay que cancelar o expirar el
anterior). La fecha fin no interviene: un contrato con `endDate` pasada se puede activar y sigue vigente. Al llegar a
EXPIRED/CANCELLED no pasa nada más: tarifas y servicios especiales quedan como historial.

Capacidad **EDIT_CONTRACT**: por defecto no está permitida en EXPIRED ni CANCELLED, así que toda escritura (datos
generales, modelo de facturación, despacho, COD, SLA, tarifas, tramos, servicios especiales) responde 422 en esos estatus;
la ficha lo anticipa con `canEdit: false`. El administrador puede cambiar la regla en `PUT /api/v1/status/capabilities/CONTRACT`.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `toCode` vacío | `Indique el estatus destino.` | 400 |
| Activar con el cliente suspendido | `No se puede activar el contrato: el cliente está suspendido. Reactive al cliente primero.` | 422 |
| Activar cuando ya hay otro ACTIVE | `El cliente ya tiene un contrato vigente. Cancele o expire el contrato anterior antes de activar este.` | 422 |
| Volver a DRAFT, salir de un terminal, etc. | mensajes del motor de estatus (capítulo 01, sección 5), p. ej. `'CANCELLED' es terminal: no admite más transiciones.` | 422 |
| Escribir en EXPIRED/CANCELLED | `El estatus actual no permite la acción 'EDIT_CONTRACT'.` | 422 |

---

## 5. Tarifas del contrato

### 5.1 Modelo e historial

- **Por servicio**: un monto fijo por envío para cada par (tipo de servicio, tipo de paquete).
- **Pieza extra**: para cada par, **tramos** por número de pieza (2–5 a $1.00, 6+ a $0.75) con cálculo **marginal**: la pieza 1
  nunca es extra; cada pieza 2..n paga la tarifa del tramo donde cae (8 piezas con esos tramos = 4 × 1.00 + 3 × 0.75 = 6.25).
- **Historial efectivo-fechado**: cada fila tiene `effectiveFrom` (inclusivo) y `effectiveTo` (exclusivo; vacío = abierta).
  **Editar** = cerrar la fila vigente en la fecha nueva y abrir otra (nunca se sobrescribe un monto); **quitar** = cerrar. Dos
  ediciones el mismo día dejan una fila de longitud cero que no está vigente pero queda como historial. `isCurrent` indica si
  la fila está vigente en la fecha consultada.
- **Servicio y paquete son inmutables**: para cambiarlos se cierra la fila y se crea otra.
- **Una sola fila vigente por par y fecha**: no puede haber dos filas del mismo servicio+paquete vigentes el mismo día (ni
  abiertas ni por cierre con fecha futura); los tramos de un mismo componente no pueden traslaparse.
- Si el componente correspondiente está **apagado** en el modelo de facturación, no se pueden crear ni editar filas (409),
  pero las existentes se conservan y se siguen mostrando.

Quién puede: `contracts.read` (ver), `contracts.update` (escribir; exige además EDIT_CONTRACT en el estatus actual).

### 5.2 Consulta

`GET /api/v1/contracts/{publicId}/rate-components?asOf=2026-06-01&includeHistory=false` → `perService[]` (id, servicio,
paquete, tarifa, vigencia, `isCurrent`) y `extraPiece[]` (componente con sus `tiers[]`). Sin `asOf` = hoy; `includeHistory`
devuelve también las cerradas.

### 5.3 Tarifa por servicio: alta, nueva versión, cierre

- `POST /api/v1/contracts/{publicId}/rate-components` `{ "kind": "PER_SERVICE", "serviceType": "STANDARD", "packageType": "BOX", "rate": 6.5, "effectiveFrom"?: "2026-01-01" }`
- `PATCH /api/v1/contracts/{publicId}/rate-components/{id}` `{ "rate": 7, "effectiveFrom"?: "hoy" }` → devuelve la **fila nueva** (id distinto)
- `POST /api/v1/contracts/{publicId}/rate-components/{id}/close` `{ "effectiveTo"?: "hoy" }`

Se pueden dar de alta tantas filas PER_SERVICE como pares servicio+paquete distintos tenga el contrato (STANDARD/BOX 6.50,
STANDARD/ENVELOPE 5.00, EXPRESS/BOX 8.50…); el 409 "ya existe una tarifa vigente" aplica solo al **mismo par**.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `kind` distinto de PER_SERVICE/EXTRA_PIECE | `Indique el tipo de componente: PER_SERVICE (tarifa por servicio) o EXTRA_PIECE (pieza extra).` | 400 |
| `serviceType` / `packageType` vacíos | `El tipo de servicio es obligatorio.` / `El tipo de paquete es obligatorio.` | 400 |
| Código de lista desconocido | `Código 'X' desconocido en la lista ServiceType.` (o `PackageType`) | 400 |
| `rate` ausente o negativo (PER_SERVICE) | `La tarifa por servicio es obligatoria.` / `La tarifa no puede ser negativa.` | 400 |
| Componente "Por servicio" apagado | `El componente 'Por servicio' está apagado en el modelo de facturación del contrato; enciéndalo antes de trabajar sus tarifas.` | 409 |
| Ya hay una fila del mismo par vigente en esa fecha | `Ya existe una tarifa vigente en esa fecha para ese servicio y tipo de paquete en el contrato; edítela o ciérrela antes de crear otra.` | 409 |
| Editar un componente de pieza extra por esta ruta | `Solo las tarifas por servicio se editan aquí; los tramos de pieza extra se editan en /tiers.` | 400 |
| Editar una fila ya cerrada | `La tarifa ya está cerrada; cree una nueva en lugar de editarla.` | 409 |
| Nueva versión con fecha anterior al inicio de la fila | `La nueva vigencia (yyyy-MM-dd) no puede ser anterior al inicio de la fila actual (yyyy-MM-dd).` | 400 |
| Cierre con fecha anterior al inicio | `La fecha (yyyy-MM-dd) no puede ser anterior al inicio de vigencia de la fila (yyyy-MM-dd).` | 400 |
| Cerrar una fila ya cerrada | `El componente ya está cerrado.` | 409 |
| Componente ajeno o inexistente | `Componente de tarifa '<id>' no encontrado.` | 404 |
| Contrato de otra compañía | `Contrato no encontrado.` | 404 |

**Regla de fechas (principio #8, historial que no se reescribe).** Una nueva versión y un cierre solo se fechan hoy o después.
Si `effectiveFrom`/`effectiveTo` es anterior a hoy, la API responde `400` con el mensaje
`La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe.` La misma regla aplica a los tramos de pieza
extra (5.4) y a las tarifas de servicios especiales (6). La fecha de alta de una tarifa nueva sí puede ser pasada (cargar
historial al configurar el contrato).

### 5.4 Pieza extra: componente y tramos

- `POST /api/v1/contracts/{publicId}/rate-components` `{ "kind": "EXTRA_PIECE", "serviceType": "STANDARD", "packageType": "BOX" }` (sin monto)
- `POST /api/v1/contracts/{publicId}/rate-components/{id}/tiers` `{ "fromUnit": 2, "toUnit": 5, "rate": 1.0, "effectiveFrom"? }` (`toUnit` vacío = abierto: "6+")
- `PATCH /api/v1/contracts/{publicId}/rate-components/{id}/tiers/{tierId}` `{ "fromUnit"?, "toUnit"?, "clearToUnit"?, "rate"?, "effectiveFrom"? }` → tramo nuevo
- `POST /api/v1/contracts/{publicId}/rate-components/{id}/tiers/{tierId}/close` `{ "effectiveTo"? }`
- `POST /api/v1/contracts/{publicId}/rate-components/{id}/close` cierra el componente **y sus tramos abiertos** con la misma fecha
  (un tramo nacido después de esa fecha queda de longitud cero).

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Componente "Pieza extra" apagado | `El componente 'Pieza extra' está apagado en el modelo de facturación del contrato; enciéndalo antes de trabajar sus tramos.` | 409 |
| `fromUnit` < 2 o `toUnit` < `fromUnit` (alta o edición) | `Rango inválido: 'desde' debe ser al menos 2 (la pieza 1 va en la tarifa por servicio) y 'hasta' debe ser mayor o igual que 'desde' o quedar vacío (abierto).` | 400 |
| `rate` negativo | `La tarifa por pieza no puede ser negativa.` | 400 |
| Traslape con un tramo vigente (alta o edición de Desde/Hasta) | `El tramo 4–7 se traslapa con el tramo vigente 2–5.` (campo `fromUnit`) | 400 |
| Tramos en un componente que no es de pieza extra | `Los tramos solo aplican a componentes de pieza extra.` | 400 |
| Componente cerrado | `El componente de pieza extra está cerrado; cree uno nuevo para agregar tramos.` | 409 |
| Editar un tramo cerrado | `El tramo ya está cerrado; cree uno nuevo en lugar de editarlo.` | 409 |
| Cerrar un tramo cerrado | `El tramo ya está cerrado.` | 409 |
| Tramo ajeno o inexistente | `Tramo '<id>' no encontrado.` | 404 |

### 5.5 Cotización

Qué hace: cotiza un envío contra el **contrato vigente** del cliente en `asOf` (hoy por defecto): tarifa base por
servicio+paquete, piezas extra marginales, cargo por despacho (una vez por orden) y cargo por COD, con las **tarifas genéricas**
de la compañía como respaldo cuando el componente está apagado o no hay fila (`baseSource`/`extraSource`: CONTRACT | GENERIC |
NONE). Despacho y COD se suman a la **primera línea**. Sin contrato vigente todo sale de las genéricas y despacho/COD son 0.
Montos a 4 decimales. Ejemplo de la bitácora: base 7.00 + 8 piezas (2–5 a 1.00, 6+ a 0.75 = 6.25) + despacho 3 + COD 2.5 % de 200 (5.00) = **21.25**;
si el envío trae además un sobre (STANDARD/ENVELOPE con tarifa de contrato 5.00), sale una segunda línea con su propia base
(`baseSource: CONTRACT`) y el total es **26.25** (despacho y COD siguen solo en la primera línea).

Quién puede: `contracts.read`. Cómo se usa: `POST /api/v1/billing/contract-rate-quote`
`{ "clientPublicId", "lines": [ { "serviceType": "STANDARD", "packageType": "BOX", "pieces": 8 } ], "codAmount": 200, "asOf"?: "2026-06-01" }`
→ `{ contractPublicId, contractStatus, asOf, lines[ { baseRate, baseSource, extraPieces, extraSource, lineTotal } ], dispatchFee, codFee, total, rateComponentIds }`.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Sin líneas | `Indique al menos una línea (servicio, tipo de paquete, piezas).` | 400 |
| `pieces` < 1 | `La cantidad de piezas debe ser al menos 1.` (campo `lines[i].pieces`) | 400 |
| Dos líneas con el mismo servicio y tipo de paquete | `El par servicio/tipo de paquete está repetido; envíe una sola línea por (servicio, tipo de paquete) con el total de piezas.` (campo `lines[i]`) | 400 |
| `codAmount` negativo | `El monto COD no puede ser negativo.` | 400 |
| Cliente dado de baja | `El cliente está dado de baja; solo se consulta su historial.` | 409 |
| Cliente inexistente | `Cliente no encontrado.` | 404 |

Una línea sin tarifa base (ni de contrato ni genérica) **no es un error**: sale con `baseSource: NONE` y `lineTotal` 0; en la web el Cotizador la avisa con «No hay tarifa vigente para X / Y: esa línea se cotiza en 0.». El servidor no oculta estos precios por `orders.view_price`: son precios del **contrato**, no de un envío, y se ven con `contracts.read`. Pantallas: [capítulo del Cotizador y usuarios de portal](frontend/fa3-portal-cotizador.md).
| Servicio/paquete vacíos o desconocidos | `El tipo de servicio es obligatorio.` / `Código 'X' desconocido en la lista PackageType.` | 400 |

---

## 6. Servicios especiales

Qué hace: el componente 5 del modelo de facturación. Cada cliente tiene una tarifa efectivo-fechada por **tipo de servicio
especial**; los **tipos son de la compañía** (compartidos por todos sus clientes): al crear uno desde un cliente queda
disponible para los demás al instante, y los nombres se comparan sin mayúsculas, acentos ni espacios dobles ("Vagón del
muelle" y "VAGON DEL MUELLE" son el mismo tipo). El tipo de una fila es inmutable. Las filas se muestran aunque el componente
esté apagado (`componentEnabled: false`); solo se bloquean las escrituras. Un tipo creado por error se **da de baja** (nunca
se borra): sale del selector de todos los clientes y conserva el historial; se reactiva cuando haga falta. Solo se puede dar de
baja cuando ningún cliente tiene una tarifa abierta de ese tipo.

Quién puede: `contracts.read` (ver), `contracts.update` (escribir; exige contrato vigente con "Especiales" encendido y EDIT_CONTRACT).

Cómo se usa:
- `GET /api/v1/clients/{publicId}/special-services?asOf=&includeHistory=false` · `GET /api/v1/special-service-types?includeInactive=false` (con `clientsUsing`)
- `POST /api/v1/clients/{publicId}/special-services` `{ "typeId": 5 }` **o** `{ "newTypeName": "Vagón del muelle" }`, más `"rate": 150, "effectiveFrom"?`
- `PATCH /api/v1/clients/{publicId}/special-services/{id}` `{ "rate": 160, "effectiveFrom"? }` → fila nueva
- `POST /api/v1/clients/{publicId}/special-services/{id}/close` `{ "effectiveTo"? }`
- `POST /api/v1/special-service-types/{id}/deactivate` · `POST /api/v1/special-service-types/{id}/reactivate` (`contracts.update`) → el tipo con `isActive`

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Sin contrato vigente | `El cliente no tiene un contrato vigente; cree o active un contrato antes de configurar servicios especiales.` | 409 |
| Componente "Especiales" apagado | `El componente 'Servicios especiales' está apagado en el contrato vigente; enciéndalo en el modelo de facturación para agregar servicios especiales.` | 409 |
| `typeId` y `newTypeName` ambos o ninguno | `Indique el tipo de servicio especial: un tipo existente (typeId) o el nombre de uno nuevo (newTypeName), no ambos.` | 400 |
| `newTypeName` vacío / > 120 | `El nombre del tipo de servicio especial es obligatorio.` / `El nombre del tipo de servicio especial no puede exceder 120 caracteres.` | 400 |
| `rate` negativo | `La tarifa del servicio especial no puede ser negativa.` | 400 |
| Ya hay una fila de ese tipo vigente en esa fecha | `El cliente ya tiene una tarifa vigente en esa fecha para el tipo 'X'; edite esa tarifa o ciérrela antes de agregar otra.` | 409 |
| `typeId` inexistente | `Tipo de servicio especial '<id>' no encontrado.` | 404 |
| `typeId` inactivo | `El tipo de servicio especial está inactivo; reactívelo o elija otro.` | 400 |
| Nombre duplicado de último momento | `Ya existe un tipo de servicio especial con el nombre 'X'.` | 409 |
| Editar o cerrar una fila cerrada | `La tarifa ya está cerrada; agregue un servicio especial nuevo si necesita volver a cobrarlo.` | 409 |
| Nueva vigencia anterior al inicio | `La nueva vigencia no puede ser anterior al inicio de la tarifa actual (yyyy-MM-dd).` | 400 |
| Cierre anterior al inicio | `La fecha de cierre no puede ser anterior al inicio de la tarifa (yyyy-MM-dd).` | 400 |
| Fila ajena o inexistente | `Servicio especial '<id>' no encontrado.` | 404 |
| Dar de baja un tipo con tarifas abiertas | `El tipo tiene tarifas vigentes en N cliente(s); ciérrelas antes de inactivarlo.` | 409 |
| Dar de baja un tipo ya inactivo / reactivar uno activo | `El tipo ya está inactivo.` / `El tipo ya está activo.` | 409 |
| Tipo inexistente o de otra compañía (baja/reactivación) | `Tipo de servicio especial '<id>' no encontrado.` | 404 |

---

## 6b. Cotejo de factura (Lote 126)

En la pestaña del contrato, sección **Cotejo de factura**: «Cobrar el cotejo de la factura del cliente» (apagado por omisión) y «Monto por factura cotejada». Cuando se procesa la factura del cliente contra su orden entregada (hoja subida o número tecleado en Procesar entregas), la corrida de facturación agrega una línea «Cotejo de factura» con ese monto, una vez por orden. Permiso `contracts.update`. Ver [capítulo 21, §7](21-cod.md).

## 7. Usuarios de portal del cliente

Qué hace: desde el expediente del cliente se **invita** por correo a las personas del cliente que usarán el portal. Nadie
escribe la contraseña de otro: la cuenta nace **sin contraseña** y el invitado la fija al aceptar el enlace (token de un
solo uso que vence a las `Portal:InviteHours` horas, 48 por defecto, suelo 1; **reenviar** emite uno nuevo y el anterior deja
de valer). La respuesta de la invitación anuncia `expiresAtUtc` con esa misma ventana; la caducidad real la aplica el proveedor
de tokens de Identity y no se comprueba en la prueba de humo (solo que la ventana anunciada coincide con la configurada).
Un correo no puede ser a la vez usuario interno y de portal, **en los dos sentidos**: invitar al portal un correo interno
responde 409 y dar de alta como interno (`POST /api/v1/users`) un correo de portal también (ver 3.3 del capítulo 1). Todo
choque de correo al invitar responde el **mismo** 409 neutro, sea del propio tenant, de otro o de un usuario interno, para no
revelar dónde está registrado. Los usuarios de portal no aparecen en la lista de usuarios de la compañía (`/api/v1/users`) y
no entran por la aplicación interna (el login del portal llega en su módulo). Requiere el módulo **CLIENT_PORTAL** encendido.
Una persona dada de baja (DISABLED) se puede **volver a invitar con el mismo correo** desde el mismo cliente: renace en INVITED
conservando su historial.

> **Actualizado por el Lote 3 (portal multi-cliente)**: una misma cuenta (correo) puede pertenecer a **varios clientes**
> de la compañía, cada uno con su propia fila (rol y estatus independientes). Invitar el mismo correo desde otro cliente
> del mismo tenant ya no responde 409: agrega una fila nueva a esa cuenta (activa de inmediato si ya tenía contraseña, o
> `INVITED` con un enlace nuevo si no). El 409 neutro sigue aplicando a un correo de usuario interno o de otro tenant, o
> ya invitado/activo/suspendido **en ese mismo cliente**. Ver [capítulo 03, sección 6](03-ordenes-de-transporte.md#6-portal-multi-cliente-cambia-el-capítulo-02-sección-7).

Quién puede: `portalusers.manage` (distinto de `admin.users`). El administrador de plataforma también puede, y sus
acciones quedan atribuidas a él en la bitácora del tenant.

Estatus (`PortalUserStatus`): **INVITED** (inicial) → **ACTIVE** (al aceptar); **SUSPENDED** (lateral, reversible);
**DISABLED** (terminal = baja definitiva; nunca se borra). Cada fila es por cliente. Efectos (`PortalUserStatusEffect`):
al llegar a SUSPENDED o DISABLED, la **cuenta** (contraseña, sesiones y refresh tokens, `SecurityEvent` TOKEN_REVOKED) se
desactiva solo cuando **no le queda ninguna otra fila ACTIVE** en la compañía; al volver a ACTIVE desde SUSPENDED, la
cuenta se reactiva conservando la contraseña.

Cómo se usa:
- `GET /api/v1/clients/{publicId}/portal-users`
- `POST /api/v1/clients/{publicId}/portal-users/invite` `{ "email", "fullName"?, "role": "CLIENT_ADMIN" | "CLIENT_OPERATOR" | "CLIENT_READONLY" }` → usuario, `expiresAtUtc` y `inviteToken` **solo** en Development (`Portal:ReturnInviteTokenInResponse`); en producción el enlace se envía por `IInvitationSender` (hoy, registro en el log sin el token).
- `POST /api/v1/clients/{publicId}/portal-users/{id}/resend-invite` (solo en INVITED; token nuevo, el enlace anterior deja de valer)
- `POST /api/v1/clients/{publicId}/portal-users/{id}/suspend` (solo desde ACTIVE) · `.../reactivate` (solo desde SUSPENDED) · `.../remove` (desde cualquier estatus no terminal) → 204
- `POST /api/v1/portal-users/accept-invite` `{ "email", "token", "password" }` **sin Authorization** → 204. Contraseña: mínimo 12 caracteres, sin reglas de composición; el chequeo contra brechas conocidas (HIBP) está conectado en el código pero hoy es no-op: se activará cuando el entorno tenga salida a Internet (ver 1.6).

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `email` vacío / inválido | `El correo es obligatorio.` / `Correo inválido.` | 400 |
| `role` vacío / desconocido | `El rol de portal es obligatorio.` / `Lista PortalRole 'X' no encontrada.` | 400 / 404 |
| El correo no está disponible (ya invitado, activo o suspendido **en este mismo cliente**; cuenta de portal de otra compañía; o usuario interno) | `Ese correo no está disponible para el portal de esta compañía.` (mismo mensaje en todos los casos, a propósito; queda `SecurityEvent` ROLE_CHANGE/FAILURE `portal_invite_conflict`) | 409 |
| Invitar desde **otro** cliente de la compañía un correo que ya es cuenta de portal (aunque en el primer cliente esté dada de baja) | **No** responde 409 (corregido 2026-10-12: antes decía lo contrario): agrega una fila nueva a esa cuenta, activa de inmediato si la cuenta ya tiene contraseña (sin `expiresAtUtc`) o `INVITED` con enlace si no (ver el recuadro del Lote 3 y el capítulo 03, sección 6). El 409 neutro queda para el mismo cliente, un usuario interno y una cuenta de otra compañía | 200 |
| Cliente dado de baja: invitar o reenviar | `El cliente está dado de baja; solo se consulta su historial.` (la lista, suspender, reactivar y dar de baja sí se permiten) | 409 |
| Reenviar cuando ya no está INVITED | `Solo se puede reenviar la invitación a un usuario que todavía no la ha aceptado (estatus INVITED).` | 409 |
| Suspender sin estar ACTIVE | `Solo se puede suspender un usuario de portal en estatus ACTIVE.` | 409 |
| Reactivar sin estar SUSPENDED | `Solo se puede reactivar un usuario de portal en estatus SUSPENDED.` | 409 |
| Dar de baja a uno ya DISABLED | `El usuario de portal ya está dado de baja.` | 409 |
| Usuario de otro cliente o inexistente | `Usuario de portal '<id>' no encontrado.` | 404 |
| Aceptar: cualquier fallo (correo desconocido, no es de portal, no está INVITED, token inválido, vencido, ya usado o anterior a un reenvío, contraseña corta (o, cuando se active el chequeo, en brecha), módulo apagado, cuenta inactiva) | `Invitación inválida o vencida.` (misma respuesta en todos los casos, a propósito) | 400 |
| Módulo CLIENT_PORTAL apagado (rutas autenticadas) | `El módulo 'CLIENT_PORTAL' no está habilitado para esta compañía.` (`code: module_disabled`) | 403 |
| Login del portal por la app interna | `Los usuarios de portal se autentican en el portal de clientes.` | 401 |

Casos frecuentes: el invitado dice que el enlace "no funciona" → reenviar la invitación (solo si sigue en INVITED; el enlace
viejo deja de valer, que use el nuevo); si ya aceptó, no se reenvía. Un usuario suspendido conserva su contraseña y vuelve a
entrar al reactivarlo. Uno dado de baja (DISABLED) no se "reactiva": se **vuelve a invitar** con el mismo correo desde
`.../invite` (mismo registro, estatus INVITED, historial completo) y fija una contraseña nueva al aceptar.

---

**Compañía desactivada.** Si el administrador de plataforma desactiva la compañía, las invitaciones emitidas antes dejan de
poder aceptarse: `accept-invite` responde el mismo `400` neutro (`Invitación inválida o vencida.`) y registra el intento en la
bitácora de seguridad sin identificar al usuario.

## 8. Permisos, módulos y roles

| Permiso | Qué permite |
|---|---|
| `clients.read` / `clients.create` / `clients.update` | ver / crear / editar clientes (perfil, numeración, contactos, estatus, baja) |
| `locations.read` / `locations.create` / `locations.update` | consignatarios y localizaciones |
| `contracts.read` | contratos, tarifas, cotización, servicios especiales y tipos (lectura) |
| `contracts.create` / `contracts.update` | crear contratos / editar contratos, tarifas, tramos y servicios especiales |
| `portalusers.manage` | usuarios de portal del cliente (módulo CLIENT_PORTAL) |

Las rutas transversales del capítulo 1 aplican el permiso de la entidad dueña: `GET /api/v1/contacts/CLIENT|CLIENT_CONTACT/{id}`,
`GET /api/v1/status/history/CLIENT/{id}` y `GET /api/v1/custom-fields/values/CLIENT/{id}` exigen `clients.read`
(CONTRACT → `contracts.read`, LOCATION → `locations.read`, PORTAL_USER → `portalusers.manage`); `PUT /api/v1/custom-fields/values/CLIENT/{id}`
exige `clients.update` (CONTRACT → `contracts.update`, LOCATION → `locations.update`). Sin el permiso: `Falta el permiso '<código>'.` (403).

Plantillas de rol: **TenantAdmin** todo; **Dispatcher** `clients.read`, `locations.read`, `locations.create`; **Billing**
`clients.read`, `contracts.read`; **ReadOnly** `clients.read`, `locations.read`, `contracts.read`. Al actualizar la plataforma,
los permisos nuevos de una plantilla se agregan también a los roles ya clonados de cada compañía (solo se agrega, nunca se quita).

Módulos: todo el capítulo vive bajo **CATALOG**; los usuarios de portal bajo **CLIENT_PORTAL**. Con el módulo apagado las
rutas responden `403` con `code: module_disabled`, y `accept-invite` responde el 400 genérico.

---

## 9. Análisis: fuentes de datos y contenido de sistema

Fuentes nuevas para vistas, indicadores y gráficos (capítulo 01, sección 8): **CLIENT** (código, nombre, estatus, término de
pago, moneda, límite de crédito, activo, numeración, `BillingSummary` del contrato vigente, `ContractsCount`,
`PickupLocationName`; admite campos personalizados de CLIENT), **CONTRACT** (número, título, cliente, fechas, estatus,
disparador, 5 banderas, despacho, COD; relación `Client`; rango de fechas por `StartDate`) y **LOCATION** (código, nombre,
dueño, compartida, tipo, ciudad, país, minutos, `AllowDupInvoice`; relación `Client`). Contenido de sistema sembrado para cada
compañía: vista **"Clientes"**, indicador **"Clientes activos"** (en el Pulso) y gráfico **"Contratos por estatus"**.

---

## 10. Auditoría

Todas las entidades del capítulo generan bitácora de cambios (`GET /api/v1/audit/changes?entityType=CLIENT|CLIENT_CONTACT|
LOCATION|CONTRACT|RATE_COMPONENT|SPECIAL_SERVICE|PORTAL_USER`). Los cambios de estatus quedan en el historial de estatus
(`/api/v1/status/history/{CLIENT|CONTRACT|PORTAL_USER}/{id}`). Las acciones sobre usuarios de portal generan además
`SecurityEvent` ROLE_CHANGE (`portal_invite`, `portal_invite_resent`, `portal_user_suspended`, `portal_user_reactivated`,
`portal_user_disabled`), PASSWORD_CHANGE (`portal_invite_accepted` / `portal_invite_accept_failed`) y TOKEN_REVOKED. El token
de invitación nunca aparece en la bitácora ni en los registros del servidor.
