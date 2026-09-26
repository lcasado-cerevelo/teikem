# Capítulo 03 — Órdenes de transporte (Lote 3)

Este capítulo describe todo lo que el Lote 3 deja disponible: la captura de órdenes de transporte (entrada rápida,
detallada o de entrega especial), la numeración de sus cuatro identificadores, la cotización y el chequeo de crédito al
confirmar, los estatus y transiciones, el importador de órdenes por plantilla, y dos ajustes que cambian el
comportamiento de módulos ya documentados en el [capítulo 02](02-clientes-y-contratos.md): el bloqueo de lo nuevo cuando
un cliente está dado de baja y el portal multi-cliente. Cada sección indica **qué hace**, **quién puede**, **cómo se
usa**, las **validaciones** con el mensaje exacto y el código HTTP, y los **estatus** con sus transiciones y efectos.
Los mensajes están verificados contra el código (`src/Teikem.Domain/Orders/*`, `src/Teikem.Infrastructure/Orders/*`,
`src/Teikem.Infrastructure/Services/Order*.cs`, `src/Teikem.Api/Controllers/Order*.cs`) y ejercitados por
`scripts/smoke.sh`. Las preguntas y respuestas de cada mensaje están en [faq.md](faq.md).

Convenciones del capítulo:

- Los recursos se identifican por `publicId` (GUID) en la URL. Un `publicId` de otra compañía, o de otro cliente cuando
  el que llama está limitado a uno (el futuro portal), responde **404** como si no existiera (sin oráculo).
- Todo el módulo vive bajo **LTL_GROUND** ("Última milla terrestre"), un módulo **núcleo**: ningún tenant lo puede
  apagar, así que las órdenes nunca responden `403 module_disabled`.
- Un error de validación responde `400` con `title` (mensaje) y `errors` (`{ campo: [mensaje] }`); un conflicto `409`;
  una regla de estatus o de negocio `422`; un recurso ajeno o inexistente `404`; falta de permiso `403`.
- Todas las lecturas y escrituras pasan por un "alcance de cliente" interno (`OrderScope`): hoy el operador interno
  siempre ve cualquier cliente de su compañía; el portal de clientes (Lote 8) fijará el cliente del principal y todo lo
  de este capítulo (alta, ficha, edición, confirmar, cancelar, buscar) quedará acotado a ese cliente sin cambios de API.
- Las fechas son UTC; "hoy" es la fecha UTC del servidor.

---

## 1. Captura de una orden

### 1.1 Alta (entrada rápida, detallada o de entrega especial)

Qué hace: crea una orden de transporte. Nace en el estatus inicial (**DRAFT**) con hasta dos paradas — **recogido**
(opcional: la que se indique, o si no se indica el punto de recogido por defecto del cliente, o si tampoco hay la
localización CORPORATE activa del cliente; si nada de eso existe, la orden nace solo con la parada de entrega) y
**entrega** (obligatoria) — y una o más líneas de paquete, salvo que sea una entrega especial. Cada orden recibe cuatro
identificadores siempre poblados: número de orden, número de factura del cliente, número de empaque y, por línea, número
de paquete (ver sección 2). No se cotiza al crear: la cotización y el chequeo de crédito ocurren al confirmar (sección 3),
salvo que se envíe `confirmNow: true`.

Quién puede: `orders.create`.

Cómo se usa: `POST /api/v1/orders`

```json
{
  "clientPublicId": "…",
  "consigneeLocationPublicId": "…",
  "pickupLocationPublicId": null,
  "serviceType": "STANDARD",
  "packages": [ { "packageType": "BOX", "pieces": 2 }, { "packageType": "ENVELOPE", "pieces": 1, "description": "Documentos" } ],
  "codAmount": 50,
  "requestedDate": "2026-10-05T00:00:00",
  "notes": null,
  "confirmNow": false
}
```

**Consignatario** (exactamente uno de los dos):
- `consigneeLocationPublicId`: una `Location` del directorio del cliente o compartida del tenant.
- `newConsignee`: captura libre (`name`, `line1`, `city`, …). Antes de crear nada se compara contra las localizaciones
  **DELIVERY/BOTH activas del propio cliente** por nombre + línea 1 normalizados (sin acentos, sin mayúsculas, espacios
  colapsados); si coincide, se reutiliza esa `Location` sin sobreescribir el directorio; si no, se crea una `Location`
  DELIVERY nueva asignada al cliente de la orden.

**Entrega especial**: `isSpecialDelivery: true` + `specialServiceId` (del catálogo de servicios especiales del cliente,
capítulo 02 sección 6). No lleva `packages` ni `codAmount`; nace con una sola línea con el nombre del servicio y sin tipo
de paquete. Cotiza la tarifa vigente del servicio especial (sección 3).

**Factura repetida (R36)**: si `clientInvoiceNumber` se **teclea** (no si Teikem lo genera) y el consignatario ya tiene
otra orden activa con ese mismo número, la respuesta depende de si el consignatario admite facturas repetidas
(`allowDupInvoice`, capítulo 02 sección 2):

| Caso | Respuesta |
|---|---|
| No admite repetidas | `409` `code: "duplicate_invoice"`, `title`: `El consignatario 'X' no permite facturas repetidas: la orden N ya usa el número F.` |
| Admite repetidas, sin confirmar | `409` `code: "duplicate_invoice_confirmable"`, mismo estilo de mensaje terminando en "…confirme si desea crear la orden de todos modos." |
| Admite repetidas, con `confirmDuplicateInvoice: true` | se crea normalmente |

Ambos 409 traen en `errors`: `clientInvoiceNumber` (el mensaje), `existingOrderNumber` y `existingOrderPublicId` (para que
el front arme el modal "Crear de todos modos" sin parsear el título). El chequeo corre **antes** de dibujar números: un
409 no gasta ningún consecutivo.

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| `clientPublicId` ausente (sin alcance de cliente fijado) | `El cliente es obligatorio.` | 400 |
| Cliente de otra compañía | `Cliente no encontrado.` | 404 |
| Cliente **dado de baja** | `El cliente está dado de baja; solo se consulta su historial.` | 409 |
| Cliente **suspendido** | `El cliente está suspendido; no se pueden crear ni confirmar órdenes.` | 422 |
| Ni `consigneeLocationPublicId` ni `newConsignee` | `El consignatario es obligatorio: elija uno del directorio o capture uno nuevo.` (campo `consignee`) | 400 |
| Los dos a la vez | `Indique un consignatario del directorio o uno nuevo, no ambos.` (campo `consignee`) | 400 |
| Consignatario de otro cliente o inexistente | `Consignatario no encontrado.` | 404 |
| Consignatario inactivo | `Consignatario está inactivo; reactívelo o elija otro.` (campo `consigneeLocationPublicId`) | 400 |
| Recogido de otro cliente o inexistente | `Localización de recogido no encontrado.` | 404 |
| `newConsignee.name` / `.line1` / `.city` vacíos | obligatorios | `El nombre del consignatario es obligatorio.` / `La dirección (línea 1) del consignatario es obligatoria.` / `La ciudad del consignatario es obligatoria.` | 400 |
| `newConsignee.country` no ISO alfa-2 | 2 letras | `El código de país debe ser ISO 3166-1 alfa-2 (2 letras).` | 400 |
| Sin líneas de paquete (orden normal) | `Indique al menos una línea de paquete.` (campo `packages`) | 400 |
| Entrega especial con `packages` o `codAmount` | `Una entrega especial no lleva líneas de paquete ni COD.` | 400 |
| `packages[i].pieces` &lt; 1 | `La cantidad de piezas debe ser al menos 1.` | 400 |
| `packages[i].packageType` desconocido / ausente sin default del tenant | `Tipo de paquete desconocido: X.` / `El tipo de paquete es obligatorio.` | 400 |
| `serviceType` desconocido / ausente sin default del tenant | `Tipo de servicio desconocido: X.` / `El tipo de servicio es obligatorio.` | 400 |
| `codAmount` negativo | `El monto COD no puede ser negativo.` | 400 |
| `isSpecialDelivery` sin `specialServiceId` | `El servicio especial es obligatorio en una entrega especial.` (campo `specialServiceId`) | 400 |
| Servicio especial no vigente / de otro cliente | `El servicio especial no está vigente para este cliente.` | 400 |
| Componente "Servicios especiales" apagado en el contrato del cliente | `El componente 'Servicios especiales' está apagado en el contrato vigente del cliente; enciéndalo en el modelo de facturación o capture una orden normal.` | 409 |
| Se envía `codType` o `packBatchNumber` | `El tipo de COD se registra al entregar, no en la captura.` / `El número de empaque siempre lo genera Teikem.` | 400 |
| Choque de número de orden / empaque tras agotar reintentos | `Ya existe una orden con ese número para este cliente.` / `Ya existe una orden con ese número de empaque.` / `No se pudo asignar un número de orden libre; intente de nuevo.` | 409 |

`confirmNow: true` crea y confirma en la misma transacción (ver sección 3.2); si la cotización o el crédito fallan, la
creación también se revierte (la orden no llega a existir) y el front puede reintentar sin `confirmNow`.
`overrideCredit: true` solo tiene efecto junto con `confirmNow: true` (si no, `400` `overrideCredit requiere confirmNow=true.`
en el campo `overrideCredit`) y exige el permiso `orders.credit_override` (403 si falta; ver sección 3.3).

### 1.2 Ficha de la orden

Qué hace: devuelve la orden completa: cabecera (números, cliente, tipo de servicio, prioridad, estatus, moneda, monto
cotizado y fecha, COD y su estatus, totales, resumen de paquetes), parada de recogido (si tiene) y de entrega con su
`snapshot` de dirección y ventana horaria, líneas de paquete activas, referencias, datos de la entrega especial si
aplica, el origen (`sourceEntityType`/`sourceEntityId`, para lotes de otros módulos) y las **capacidades** disponibles
según el estatus actual (`canEditCargo`, `canReprice`, `canCancel`, `canDelete`, `canConfirm`). Incluye `rowVersion` para
la edición concurrente.

Quién puede: `orders.view`.

Cómo se usa: `GET /api/v1/orders/{publicId}`.

### 1.3 Listado y búsqueda / escaneo

Qué hace: dos formas de encontrar órdenes. El **listado** es paginado y admite filtros parciales; su primera columna de
negocio es siempre el **número de empaque** (`packBatchNumber`, único por tenant y siempre con valor). La **búsqueda
exacta** (`lookup`) es para el escaneo: compara el código tal cual contra los tres identificadores, sin coincidencias
parciales.

Quién puede: `orders.view`.

Cómo se usa:
- `GET /api/v1/orders?clientId=&status=&orderNumber=&invoice=&packBatch=&consignee=&search=&from=&to=&includeInactive=false&skip=0&take=100`
  — `orderNumber`/`invoice`/`packBatch`/`consignee`/`search` son parciales (`LIKE`); `from` inclusivo y `to` exclusivo
  sobre la fecha de creación; `take` se acota entre 1 y 500 (por defecto 100).
- `GET /api/v1/orders/lookup?code=T$TS-00001` — coincidencia **exacta** (sin distinguir mayúsculas), primero por número de
  orden, si no hay por número de empaque, si no hay por factura; devuelve todas las coincidencias de ese grupo
  (`matchedBy`: `ORDER_NUMBER` | `PACK_BATCH` | `INVOICE`, o `null` sin coincidencias) — el número de orden puede
  repetirse entre clientes y la factura entre órdenes, el empaque es único.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `status` desconocido | `Estatus desconocido: X.` (campo `status`) | 400 |
| `code` vacío en `lookup` | `Indique el código a buscar.` (campo `code`) | 400 |
| `clientId` de otra compañía | `Cliente no encontrado.` | 404 |

### 1.4 Edición

Qué hace: edita la orden mientras está en un estatus que lo permita (por defecto, solo **DRAFT**; ver sección 4). Se
puede cambiar el consignatario (con la misma resolución de la sección 1.1 y re-chequeo de R36 si la orden **fue creada
con factura tecleada**), el recogido, el tipo de servicio, la prioridad, las líneas de paquete (reemplazo completo: las
anteriores quedan inactivas como rastro), el COD (agregar, aumentar o quitar con `clearCod: true`), fechas, notas y
referencias. El **cliente, el número de orden, el número de factura y el número de empaque se fijan al crear** y no se
editan. Si la orden ya estaba cotizada, editarla la **re-cotiza** (exige además la capacidad `REPRICE`) para no dejar un
monto viejo.

Quién puede: `orders.edit`.

Cómo se usa: `PATCH /api/v1/orders/{publicId}` con solo los campos que cambian; `rowVersion` opcional (el de la ficha)
para detectar edición concurrente.

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Se envía `clientPublicId`, `orderNumber`, `clientInvoiceNumber` o `packBatchNumber` | `El campo 'X' se fija al crear la orden y no se edita.` / `El número de empaque siempre lo genera Teikem.` | 400 |
| Se envía `codType` | `El tipo de COD se registra al entregar, no en la captura.` | 400 |
| `consigneeLocationPublicId` y `newConsignee` a la vez | `Indique un consignatario del directorio o uno nuevo, no ambos.` (campo `consignee`) | 400 |
| `codAmount` y `clearCod: true` a la vez | `Indique un monto COD o clearCod, no ambos.` (campo `codAmount`) | 400 |
| Orden de entrega especial con `packages` o `codAmount` | `Una entrega especial no lleva líneas de paquete ni COD.` | 400 |
| El estatus actual no permite `EDIT_CARGO` | `El estatus actual no permite la acción 'EDIT_CARGO'.` | 422 |
| Ya cotizada y el estatus no permite `REPRICE` | `El estatus actual no permite la acción 'REPRICE'.` | 422 |
| `rowVersion` desactualizado | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Orden de otra compañía, de otro cliente del alcance, o eliminada en captura | `Orden no encontrado.` | 404 |

### 1.5 Eliminar (solo en la etapa inicial)

Qué hace: **baja lógica** (`isActive = false`), nunca `DELETE`. Solo se permite mientras la orden sigue en su **estatus
inicial** (DRAFT); libera de inmediato su número de orden y su número de empaque (ambos son índices únicos filtrados por
`isActive = 1`), así que se pueden volver a capturar. Después de eliminada, ninguna acción vuelve a funcionar sobre ella
(editar, confirmar, repreciar, cancelar, estatus): la ficha y el listado (`includeInactive=true`) la siguen mostrando,
pero como registro histórico.

Quién puede: `orders.cancel`.

Cómo se usa: `DELETE /api/v1/orders/{publicId}` → `204`.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| La orden ya no está en su etapa inicial | `Solo se puede eliminar una orden en su estatus inicial; use la cancelación.` | 422 |

---

## 2. Numeración de los cuatro identificadores

Qué hace: toda orden recibe cuatro identificadores, cada uno con su propio contador atómico (tabla `NumberSequence`,
`UPDATE … OUTPUT` dentro de la transacción del alta: sin huecos ni choques entre altas simultáneas):

| Identificador | Ámbito del contador | ¿Quién lo asigna? | Patrón por defecto |
|---|---|---|---|
| Número de orden (`orderNumber`) | por cliente | Teikem, o el cliente si `clientAssignsOrderNumber: true` (capítulo 02, 1.4) | `ORD-#####` |
| Número de factura (`clientInvoiceNumber`) | por cliente | Teikem, o el cliente si `clientAssignsInvoiceNumber: true` | `FAC-#####` |
| Número de empaque (`packBatchNumber`) | por tenant | **siempre Teikem**, nunca configurable | `EMP-#####` (fijo) |
| Número de paquete (`packages[i].packageNumber`) | por cliente | siempre se puede teclear por línea | `PQT-#####` |

El número de orden es único **por cliente** (dos clientes pueden compartir "PO-1001"); el número de empaque es único
**por tenant** y es el identificador inequívoco para escanear. Un número que el cliente "asigna" pero se deja en blanco
lo genera Teikem igual.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Se teclea `orderNumber` y el cliente no lo asigna | `El número de orden lo asigna Teikem para este cliente; déjelo en blanco.` (campo `orderNumber`) | 400 |
| Se teclea `clientInvoiceNumber` y el cliente no lo asigna | `El número de factura lo asigna Teikem para este cliente; déjelo en blanco.` (campo `clientInvoiceNumber`) | 400 |
| Se teclea `packBatchNumber` | `El número de empaque siempre lo genera Teikem.` | 400 |
| Un número tecleado excede 40 caracteres | `El número no puede exceder 40 caracteres.` | 400 |
| Un número automático, con el patrón del cliente, excede 40 caracteres | `El número {de orden\|de factura\|de paquete\|de empaque} generado con el patrón del cliente excede 40 caracteres; acorte el patrón en la ficha del cliente.` | 409 |
| Choque de un número automático con uno tecleado por otro usuario | se reintenta solo (hasta 5 veces), sin que el usuario lo note; agotados los reintentos: `No se pudo asignar un número de orden libre; intente de nuevo.` | 409 |

---

## 3. Cotización y crédito

### 3.1 Vista previa (sin persistir nada)

Qué hace: cotiza la orden y evalúa el crédito del cliente **sin guardar nada**, para que la interfaz muestre el aviso
antes de confirmar. Devuelve las líneas cotizadas (consolidadas por tipo de paquete), la tarifa de despacho y de COD, el
total, el contrato usado, y el chequeo de crédito (`creditLimit`, `pendingBalance` — la suma de `quotedAmount` de las
órdenes del cliente que están en curso, sin contar DRAFT, DELIVERED ni CANCELLED —, `orderTotal`, `available` y
`exceeds`). Los mismos errores que daría confirmar (tarifa faltante, servicio especial cerrado) se ven aquí antes.

Quién puede: `orders.view`.

Cómo se usa: `GET /api/v1/orders/{publicId}/quote`.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Falta tarifa vigente para alguna línea (servicio/tipo de paquete) | `No hay tarifa vigente para X/Y; configure la tarifa en el contrato del cliente antes de confirmar.` | 422 |
| Servicio especial ya no vigente (se cerró) | `El servicio especial ya no está vigente; elija otro antes de confirmar.` | 409 |
| Componente "Servicios especiales" apagado en el contrato vigente | `El componente 'Servicios especiales' está apagado en el contrato vigente del cliente; enciéndalo en el modelo de facturación o capture una orden normal.` | 409 |

### 3.2 Confirmar

Qué hace: saca la orden de su etapa inicial hacia la primera etapa del **pipeline** habilitada después de ella
(normalmente **CONFIRMED**; si el tenant la deshabilita, la que siga, p. ej. **PICKUP**). Al confirmar: (1) cotiza la
orden con el motor de tarifas del cliente (o la tarifa vigente del servicio especial) y **congela** `quotedAmount`,
`quotedAtUtc` y el contrato usado; (2) verifica que el cliente no esté suspendido; (3) verifica el crédito (sección
3.3). Si algo falla, la orden **no cambia de estatus** y no queda cotizada (se revierte todo). Una vez confirmada, la
edición de carga queda apagada por defecto (ver sección 4) y solo se puede re-cotizar (`REPRICE`) si el tenant lo
habilita.

Quién puede: `orders.edit` (o `orders.credit_override` cuando el crédito de verdad se excede; ver 3.3).

Cómo se usa: `POST /api/v1/orders/{publicId}/confirm` `{ "rowVersion"?, "overrideCredit"?: false }`.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| La orden ya no está en su etapa inicial | `Solo se confirma una orden en su estatus inicial.` | 422 |
| El pipeline no tiene una etapa siguiente a la inicial (tenant deshabilitó todo el resto) | `El pipeline de órdenes no tiene una etapa siguiente a la inicial.` | 422 |
| Cliente suspendido | `El cliente está suspendido; no se pueden crear ni confirmar órdenes.` | 422 |
| Falta tarifa / servicio especial cerrado | igual que 3.1 | 422 / 409 |
| Crédito excedido, sin `overrideCredit` | ver 3.3 | 422 |
| `rowVersion` desactualizado | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |

### 3.3 Crédito excedido: aviso y autorización con permiso

Qué hace: si el crédito del cliente se excede al confirmar, **no bloquea de forma definitiva**: avisa con `422` y
`code: "credit_exceeded"` (`errors`: `limit`, `exposure`, `newAmount`, `available`, todos formateados con dos
decimales), y se puede autorizar con `overrideCredit: true` si quien confirma tiene el permiso `orders.credit_override`.
Al autorizar: la orden confirma igual, queda un comentario en su historial de estatus
(`Crédito excedido autorizado por {usuario} (límite X, en curso Y, esta orden Z)`) y un evento de seguridad
(`ROLE_CHANGE`/`SUCCESS`, `action: "credit_override"`).

Quién puede: confirmar sin `overrideCredit` exige `orders.edit`; con `overrideCredit: true` exige
`orders.credit_override` y, si el crédito **no** se excedía de verdad, exige además `orders.edit` (el permiso de
autorización no sirve para confirmar órdenes normales). Esto permite que la plantilla **Billing** (que tiene
`orders.credit_override` pero no `orders.edit`) autorice justo los casos de exceso.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Crédito excedido, sin `overrideCredit` | `El cliente excede su límite de crédito: límite X.XX, en curso Y.YY, esta orden Z.ZZ.` | 422 |
| `overrideCredit: true` sin el permiso `orders.credit_override` | `Falta el permiso 'orders.credit_override'.` | 403 |
| `overrideCredit: true` con el permiso, pero el crédito no se excedía (o sin `orders.edit`) | `Falta el permiso 'orders.edit'.` | 403 |

### 3.4 Reprecio

Qué hace: re-cotiza una orden ya confirmada con las tarifas vigentes (por si el contrato o el tipo de cambio cambiaron).
No cambia de estatus. Solo disponible cuando el estatus actual permite la capacidad `REPRICE` (por defecto, solo
**CONFIRMED**).

Quién puede: `orders.edit`.

Cómo se usa: `POST /api/v1/orders/{publicId}/reprice`.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| El estatus actual no permite `REPRICE` | `El estatus actual no permite la acción 'REPRICE'.` | 422 |

---

## 4. Estatus y transiciones

Dominio `OrderStatus`: **DRAFT** (inicial) → pipeline **CONFIRMED → PICKUP → INBOUND → PLANNED → IN_TRANSIT → ARRIVED →
DELIVERED** (terminal); laterales **ON_HOLD**, **PARTIAL**, **FAILED** (reversibles, regresan al último pipeline);
terminal **CANCELLED**. Todo cambio pasa por el motor de estatus del capítulo 01 (sección 5): valida la etapa, escribe
`GET /api/v1/status/history/TRANSPORT_ORDER/{id}` y dispara efectos.

| De → a | Quién / cómo | Qué valida | Efectos |
|---|---|---|---|
| (nace) → DRAFT | automático al crear | — | Historial de nacimiento; paradas y COD nacen con su propio historial (ver nota) |
| DRAFT → CONFIRMED (o la siguiente etapa de pipeline habilitada) | `orders.edit` (o `orders.credit_override`), `POST /confirm` | tarifa vigente, servicio especial vigente, cliente no suspendido, crédito | Cotiza y congela `quotedAmount`/`quotedAtUtc`/contrato; verifica crédito (422/aviso, sección 3.3) |
| Cualquier no terminal → CANCELLED | `orders.cancel`, `POST /cancel` | capacidad `CANCEL` (por defecto apagada en DELIVERED y CANCELLED); respeta las entradas laterales que el tenant configure | Bitácora con el comentario enviado |
| Pipeline → ON_HOLD / PARTIAL / FAILED (laterales) | `orders.edit`, `POST /status {"toCode": "ON_HOLD"}` | no aplica a CANCELLED ni a la confirmación (tienen su propia acción) | — |
| Lateral → el mismo pipeline del que salió | `orders.edit`, `POST /status` | solo regresa a la etapa exacta desde la que se desvió (no salta a la siguiente) | — |
| Pipeline → PICKUP…DELIVERED como avance directo | — | **no disponible aquí** | `422`: `El avance a 'X' lo realiza el módulo correspondiente (trips/entregas); desde aquí solo se registran estatus laterales.` (reservado a los lotes de rutas/almacén/POD) |

Capacidades por estatus (`StatusCapability`, el tenant las cambia desde `/status/capabilities/TRANSPORT_ORDER`):

| Capacidad | Por defecto | Bloquea |
|---|---|---|
| `EDIT_CARGO` | solo permitida en **DRAFT** | `PATCH /orders/{id}` fuera de DRAFT (422) |
| `REPRICE` | solo permitida en **CONFIRMED** | `POST /reprice` en cualquier otro estatus (422) |
| `CANCEL` | apagada en **DELIVERED** y **CANCELLED** | `POST /cancel` en esos estatus (422) |
| `ASSIGN_TRIP` | apagada en **DRAFT** y en los terminales | (la usará el lote de Trips/asignación de chofer) |

Nota sobre el historial: el ciclo del **COD** de la orden (`PENDING`) y el de sus **paradas** (`PENDING` → …) tienen su
propio historial bajo `EntityType` `ORDER_COD` y `ORDER_STOP` respectivamente (`GET /status/history/ORDER_COD/{id}` /
`GET /status/history/ORDER_STOP/{id}`), separado del de la orden misma: así el regreso de un lateral de la orden nunca
se confunde con el estatus del COD o de una parada.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `toCode` vacío | `Indique el estatus destino.` | 400 |
| Estatus destino inexistente o deshabilitado | `El estatus 'X' no existe o no está habilitado para esta compañía.` | 422 |
| `toCode: "CANCELLED"` por `/status` | `Para cancelar use la acción de cancelación.` | 422 |
| `toCode` es la etapa de confirmación, estando en la etapa inicial | `Para confirmar use la acción de confirmación.` | 422 |
| Avance de pipeline no reservado a laterales/regreso | `El avance a 'X' lo realiza el módulo correspondiente (trips/entregas); desde aquí solo se registran estatus laterales.` | 422 |
| Comentario de más de 500 caracteres (`cancel`/`status`) | `El comentario admite como máximo 500 caracteres.` (campo `comment`) | 400 |

---

## 5. Cliente dado de baja: se bloquea solo lo nuevo

Qué hace (ajuste aplicado en este lote, cruza varios módulos): un cliente **dado de baja** (`isActive = false`, capítulo
02 sección 1.7) sigue teniendo su ficha, su historial, sus órdenes y sus contratos y tarifas **consultables**; solo se
bloquea lo que **crea algo nuevo** para él:

| Acción bloqueada | Servicio | Mensaje exacto | HTTP |
|---|---|---|---|
| Crear una orden (`POST /orders`, entrada rápida/detallada/especial y cada fila del importador) | `OrderService.CreateAsync` | `El cliente está dado de baja; solo se consulta su historial.` | 409 |
| Invitar / reenviar invitación a un usuario de portal | `PortalUserService.InviteAsync` / `ResendInviteAsync` | mismo mensaje | 409 |
| Crear un contrato nuevo | `ContractService.CreateAsync` | mismo mensaje | 409 |
| Agregar un componente o un tramo de tarifa | `RateService.AddComponentAsync` / `AddTierAsync` | mismo mensaje | 409 |
| Agregar un servicio especial | `SpecialServiceService.AddAsync` | mismo mensaje | 409 |

Lo que **no** se bloquea: leer la ficha, el historial, los contratos y las tarifas; **cancelar** o **cerrar** algo que ya
existía (una orden en curso, un contrato, un servicio especial); suspender, reactivar o quitar un usuario de portal.
Reactivar el cliente (`POST /clients/{id}/reactivate`) vuelve a permitir todo lo anterior de inmediato.

> Esto reemplaza la nota de la sección 1.7 del capítulo 02 ("un cliente dado de baja todavía no bloquea escrituras"): con
> el Lote 3, sí bloquea la creación de lo nuevo, con el mensaje de esta tabla.

---

## 6. Portal multi-cliente (cambia el capítulo 02, sección 7)

Qué hace (ajuste aplicado en este lote sobre `PortalUserService`, documentado en detalle en el
[capítulo 02, sección 7](02-clientes-y-contratos.md#7-usuarios-de-portal-del-cliente)): una misma cuenta de portal
(un correo) puede pertenecer a **varios clientes** de la misma compañía; cada cliente tiene su propia fila (su propio
rol, su propio estatus). Invitar un correo que ya es cuenta de portal del mismo tenant, desde otro cliente, agrega una
fila nueva a esa cuenta en vez de rechazarla:

- Si la cuenta ya tiene contraseña y sigue habilitada, la fila nueva pasa **directo a ACTIVE** (sin token de invitación;
  queda el evento `ROLE_CHANGE` `action: "portal_client_added"`).
- Si no tiene contraseña (o estaba desactivada porque no le quedaba ningún cliente vivo), nace **INVITED** con un enlace
  de invitación, como siempre.

Aceptar la invitación (`POST /portal-users/accept-invite`) activa **todas** las filas `INVITED` de la cuenta a la vez
(un solo enlace sirve para todos los clientes pendientes). Suspender, reactivar o quitar un usuario de portal actúa
**por fila** (por cliente): la cuenta completa (contraseña, sesiones) solo se desactiva cuando **no le queda ninguna
otra fila activa** en la compañía, y se reactiva en cuanto vuelve a haber una. El correo de un usuario **interno** sigue
respondiendo el mismo 409 neutro (`Ese correo no está disponible para el portal de esta compañía.`); una cuenta de
**otro tenant** también.

---

## 7. Importador de órdenes por plantilla

### 7.1 Plantillas de importación

Qué hace: define cómo leer un archivo CSV de órdenes por **posición de columna** (no por nombre de cabecera): cada
columna se mapea a un campo del catálogo (`orderNumber`, `clientInvoiceNumber`, `serviceType`, `consigneeName`,
`consigneeCode`, `line1`, `line2`, `city`, `state`, `postalCode`, `country`, `contactPhone`, `packageType`, `pieces`,
`weightKg`, `volumeM3`, `description`, `codAmount`, `requestedDate`, `notes`, `reference`) y admite valores fijos
(`defaults`) para los campos que no varían fila a fila (p. ej. `serviceType: "STANDARD"`). Una plantilla es general del
tenant (`clientPublicId: null`) o de un cliente específico. Baja lógica, nunca `DELETE`.

Quién puede: `orders.edit`.

Cómo se usa:
- `GET /api/v1/import-templates?kind=ORDER&clientId=&includeInactive=false`
- `POST /api/v1/import-templates` `{ "name": "Plantilla estándar", "columns": [ {"position":1,"field":"consigneeName"}, {"position":2,"field":"line1"}, {"position":3,"field":"city"}, {"position":4,"field":"pieces"} ], "defaults": {"serviceType":"STANDARD","packageType":"BOX"}, "delimiter": ",", "hasHeader": true }`
- `GET /api/v1/import-templates/{publicId}` · `PATCH /api/v1/import-templates/{publicId}`
- `POST /api/v1/import-templates/{publicId}/deactivate` / `.../reactivate`

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Sin columnas | `Indique al menos una columna.` (campo `columns`) | 400 |
| Posición &lt; 1 | `La posición debe ser 1 o mayor.` | 400 |
| Posición repetida | `La posición N está repetida.` | 400 |
| Campo desconocido | `Campo desconocido: X.` | 400 |
| Campo repetido | `El campo X está repetido.` | 400 |
| Sin `consigneeName` ni `consigneeCode` (columna o default) | `La plantilla debe incluir consigneeName o consigneeCode.` (campo `columns`) | 400 |
| Sin `packageType`/`pieces` (columna o default) | `La plantilla debe indicar el paquete: packageType o pieces, en una columna o en defaults.` (campo `columns`) | 400 |
| Nombre repetido (tenant + tipo) | `Ya existe una plantilla de importación con ese nombre.` | 409 |
| Delimitador inválido | `El delimitador debe ser un solo carácter (p. ej. ',' o ';'), distinto de comilla y salto de línea.` | 400 |
| Plantilla inexistente o de otro tenant | `Plantilla de importación no encontrado.` | 404 |

### 7.2 Validar (paso 1 de 2)

Qué hace: sube un CSV y lo valida **fila por fila** con las mismas reglas que la captura manual — **sin crear ninguna
orden todavía**. Para cada fila resuelve el consignatario (por `consigneeCode` contra el directorio, si no por
coincidencia de nombre + línea 1, si no "se creará"), evalúa tipo de servicio/paquete, piezas/peso/volumen/COD, la
numeración tecleada y la factura repetida (informativa: aviso, no bloqueo, salvo que el consignatario no admita
repetidas). Guarda un lote (`ImportBatch`, estatus **VALIDATED**) con el resultado de cada fila.

Quién puede: `orders.create`.

Cómo se usa: `POST /api/v1/orders/import/validate` — JSON `{ "templatePublicId", "clientPublicId", "content": "<CSV>" }`
o `multipart/form-data` con los mismos campos y el archivo en `file`. Límite: **5.000 filas y 2 MB**.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Sin contenido / archivo | `Indique el contenido del archivo CSV.` | 400 |
| Archivo demasiado grande o con demasiadas filas | `El archivo supera el límite de 5.000 filas o 2 MB.` (campo `content` o `file`) | 400 |
| Archivo sin filas de datos | `El archivo no tiene filas de datos.` | 400 |
| Cliente dado de baja / suspendido | igual que la sección 5 / 1.1 | 409 / 422 |
| Fila sin consignatario (ni `consigneeCode` ni `consigneeName`) | `Indique el consignatario: nombre (con dirección) o código del directorio.` (campo `consigneeName`) | (error de esa fila, `200` con la fila marcada inválida) |

La respuesta (`200`) trae `batchPublicId`, `rowCount`, `validRows` y `rows[]` con, por fila: los valores mapeados, el
consignatario resuelto (`EXISTING`/`CREATE`), sus `errors` por campo (impide crear esa fila) y sus `warnings`
(informativos, no impiden crear). `GET /api/v1/orders/import/{batchPublicId}` devuelve la misma vista previa.

### 7.3 Confirmar (paso 2 de 2)

Qué hace: crea una orden por cada fila válida elegida (por defecto, todas las válidas), **en su propia transacción**
(una fila fallida no tumba las demás); cada orden se crea con el mismo comportamiento que `POST /orders` (numeración,
resolución de consignatario, R36). `confirmNow` confirma cada orden creada; `overrideCredit` autoriza el exceso de
crédito de cada una (exige `orders.credit_override`); `confirmDuplicateInvoice` crea también las filas con factura
repetida confirmable. El lote pasa a **CONFIRMED**; un lote ya confirmado no admite una segunda confirmación.

Quién puede: `orders.create`.

Cómo se usa: `POST /api/v1/orders/import/{batchPublicId}/confirm` `{ "rows"?: [1,2], "confirmNow"?: false, "overrideCredit"?: false, "confirmDuplicateInvoice"?: false }`.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| El lote ya fue confirmado | `El lote ya fue confirmado; valide un archivo nuevo para importar más órdenes.` | 409 |
| El lote fue descartado | `El lote fue descartado; valide el archivo de nuevo.` | 409 |
| Dos confirmaciones (o confirmar y descartar) a la vez | `El lote ya se está confirmando o fue confirmado; consulte su resultado.` | 409 |
| `rows` con un ordinal que no existe en el lote | `La fila N no existe en el lote.` | 400 |
| `rows` con una fila que tiene errores | `La fila N tiene errores y no se puede confirmar.` | 400 |
| `overrideCredit` sin `confirmNow` | `overrideCredit requiere confirmNow=true.` | 400 |
| `overrideCredit` sin el permiso | `Falta el permiso 'orders.credit_override'.` | 403 |

La respuesta trae `created`, `failed`, `skipped` (filas no elegidas) y `rows[]` con, por fila, la orden creada
(`orderPublicId`/`orderNumber`/`orderStatus`) o el `error` puntual de esa fila.

### 7.4 Descartar

Qué hace: cierra un lote **pendiente de confirmar** sin crear ninguna orden (estatus **DISCARDED**, terminal).

Quién puede: `orders.create`.

Cómo se usa: `POST /api/v1/orders/import/{batchPublicId}/discard`.

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| El lote ya no está `VALIDATED` (ya confirmado o descartado) | `Solo se descarta un lote pendiente de confirmar.` | 422 |
| Alguien lo confirmó justo antes | `El lote ya se está confirmando o fue confirmado; consulte su resultado.` | 409 |

---

## 8. Permisos y módulos

| Permiso | Qué habilita |
|---|---|
| `orders.view` | ver órdenes (ficha, listado, lookup, vista previa de cotización), plantillas y lotes de importación |
| `orders.create` | crear órdenes (captura y confirmación embebida), validar/confirmar/descartar importaciones |
| `orders.edit` | editar, confirmar (sin exceso de crédito), repreciar y cambiar estatus laterales |
| `orders.cancel` | cancelar y eliminar (baja lógica en la etapa inicial) |
| `orders.credit_override` | autorizar una orden que excede el límite de crédito al confirmar (`overrideCredit: true`) |

Todo el módulo requiere **LTL_GROUND** (núcleo, siempre encendido). Plantillas de rol de fábrica: **Dispatcher** trae
`orders.view/create/edit/cancel`; **Billing** trae `orders.view` y `orders.credit_override` (sin `orders.edit`, para que
solo pueda autorizar el exceso de crédito, no confirmar órdenes normales ni editarlas); **Driver** y **ReadOnly** solo
`orders.view`.

---

## 9. Fuentes de análisis y auditoría

La fuente de datos `TRANSPORT_ORDER` ("Órdenes") queda disponible para vistas, indicadores y gráficos con relaciones a
`Client` y `Consignee` (Location). El tenant nuevo (y el demo) recibe de fábrica: la vista **"Órdenes"** (columnas
Empaque, número de orden, factura, cliente, consignatario, estatus, piezas, COD, fecha), el indicador **"Órdenes en
curso"** (cuenta las que están en el pipeline, sin contar DRAFT ni terminales), el indicador **"COD por cobrar"** (suma
el COD `PENDING` de las órdenes activas, excluyendo las canceladas) y el gráfico **"Órdenes por estatus"** (dona, últimos
30 días). Cada alta o cambio de una orden, sus paradas, líneas y referencias queda en `AuditLog` (`GET /api/v1/audit/changes?entityType=TRANSPORT_ORDER`);
las acciones sobre plantillas y lotes de importación quedan bajo `IMPORT_TEMPLATE` e `IMPORT_BATCH`.

---

## 10. Preguntas frecuentes

Ver [faq.md](faq.md), sección "Lote 3 — Órdenes de transporte", para cada mensaje de error citado en este capítulo con
su causa y qué hacer.
