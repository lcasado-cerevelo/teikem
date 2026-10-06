# 11 — Rentas (submódulo de Almacén)

Rentas permite **rentar equipos propios con número de serie** (camas de hospital, concentradores de oxígeno, sillas de ruedas…) a una
**localidad de un cliente**, con su contrato, su fecha de recogido (fin de la renta) y sus condiciones de cobro. Este capítulo cubre el
**bloque R1 (Lote 27)**: la renta desde que se crea hasta que se despacha, las **extensiones** de la fecha de recogido y la
**cancelación**; y el **bloque R2 (Lote 28)**: la **devolución de renta** (con su motivo, total o parcial, al término o anticipada), el
**proceso configurable** del equipo devuelto (inspección, limpieza, pruebas, reparación… hasta "Lista" o "Dada de baja") y los equipos
en renta en el **conteo cíclico** (D7); y el **bloque R3 (Lote 29)**: los **reportes, indicadores y el gráfico** de rentas en Análisis y
el aviso de **rentas vencidas o por vencer** en "Necesita tu atención" del Pulso (sección 10). Todo lo de este capítulo es del servidor
(API); las pantallas de la renta (lista, ficha, alta, programar, despachar, extender, cancelar) y "Convertir a serie" están en el capítulo
de pantallas [F17](frontend/f17-rentas.md); las de devoluciones y procesos llegan con F-R2.

Decisiones del dueño que gobiernan el módulo:
- **El equipo rentado sigue siendo nuestro** (D1): no sale del inventario. Queda en la posición **EN-RENTA** del almacén, con estatus de
  serie **"En renta"** y **reservado**: cuenta en *en mano* pero **no** en *disponible*, en ninguna pantalla.
- El despacho es un **registro propio de la renta** (fechas, equipos, series, costo de transporte estimado) con un enlace **vacío** al
  envío futuro (D2). Funciona hoy sin el módulo de envíos.
- **Facturación: solo se guardan las condiciones** (fija o por tiempo, tarifa, moneda por equipo) con un enlace vacío a la factura (D3).
  No se calcula ni se cobra nada.
- **Extender** una renta es un registro con permiso y bitácora (nueva fecha, motivo, tarifa si cambia), sin aprobación de un segundo
  usuario (D4).
- El **contrato** es el número y la fecha del contrato dentro de la renta (D6).
- La **devolución** es también un registro propio de la renta (D2), con enlace **vacío** al envío futuro de recogido.
- Una serie en renta que aparece en un **conteo cíclico** se bloquea: primero se registra la devolución; la posición EN-RENTA no se
  cuenta (D7).
- **Series de los equipos de Advance Depot** (D5): los productos llegaron de la migración sin seguimiento y **ninguna fuente de la
  migración trae números de serie**; se ponen con serie con **"Convertir a serie"** (capítulo 6 §2.1). Reimportar no las trae: la
  migración solo puede crear con serie productos **nuevos sin existencia** (Lote 30; capítulo 10 §6 y
  `docs/migracion/depot-series-y-rentas.md`).

## 1. Quién puede y dónde

| Acción | Permiso | Endpoint |
|---|---|---|
| Ver la lista, la ficha y la bitácora de extensiones | `rental.view` | `GET /api/v1/rentals`, `GET /api/v1/rentals/{publicId}`, `GET /api/v1/rentals/{publicId}/extensions` |
| Crear, editar, agregar o quitar equipos, poner la tarifa de un equipo, programar, despachar y cancelar | `rental.manage` | `POST /api/v1/rentals`, `PATCH /api/v1/rentals/{publicId}`, `POST /api/v1/rentals/{publicId}/lines`, `DELETE /api/v1/rentals/{publicId}/lines/{lineId}`, `PUT /api/v1/rentals/{publicId}/lines/{lineId}/rate`, `POST …/schedule`, `POST …/dispatch`, `POST …/cancel` |
| Extender la fecha de recogido | `rental.extend` (nuevo) | `POST /api/v1/rentals/{publicId}/extensions` |
| Registrar la devolución de una renta | `rental.return` | `POST /api/v1/rentals/{publicId}/returns` |
| Ver las devoluciones (lista y ficha) | `rental.view` | `GET /api/v1/rental-returns`, `GET /api/v1/rental-returns/{publicId}` |
| Ver la cola de procesos de equipos devueltos | `rental.view` | `GET /api/v1/rental-processes` |
| Avanzar el proceso y terminarlo ("Lista") | `rental.maintenance` | `POST /api/v1/rental-processes/{id}/advance`, `POST /api/v1/rental-processes/{id}/complete` |
| Dar de baja el equipo en su proceso | `rental.maintenance` **y** `inventory.adjust` | `POST /api/v1/rental-processes/{id}/scrap` |
| Historial de estatus de la renta / del proceso | `rental.view` | `GET /api/v1/status/history/RENTAL/{id}`, `GET /api/v1/status/history/RENTAL_PROCESS/{id}` |
| Reportes, indicadores y gráfico de rentas (Análisis) | `analytics.view` **y** `rental.view` | `GET /api/v1/analytics/reports`, `POST /api/v1/analytics/reports/{id}/run`, `GET /api/v1/analytics/indicators/{id}/value`, `GET /api/v1/analytics/charts/{id}/data`, `POST /api/v1/analytics/reports/RENTAL/preview` (sección 10) |
| Aviso de rentas vencidas o por vencer en "Necesita tu atención" | `pulse.attention` **y** `rental.view` | `GET /api/v1/analytics/attention` (sección 10.5) |

- **Módulo**: `RENTAL_EQUIPMENT`, que ahora se llama **"Rentas"**, está en la categoría **Almacén** y **depende de `WMS_LOTSERIAL`**
  (Inventario y trazabilidad): no se enciende sin él y **apagar `WMS_LOTSERIAL` apaga Rentas** (y su dependiente "Facturación de
  alquiler"). Con el módulo apagado los endpoints responden 403 `module_disabled`.
- **Plantillas de rol**: el **Admin de compañía** tiene todo; el **Operador de almacén** tiene `rental.view`, `rental.manage`,
  `rental.maintenance` y ahora también **`rental.extend`** y **`rental.return`**. Al actualizar la plataforma, todo rol de compañía que
  ya gestionaba rentas (`rental.manage`) recibe una sola vez `rental.extend` y `rental.return`; a los demás se les dan desde Roles.
- La compañía sale de la sesión: una renta, una devolución, un proceso, un cliente, una localidad, un almacén, una posición o un producto
  de otra compañía responden **404**.
- Todo cambio de la renta, sus equipos, tarifas y extensiones queda en la **auditoría** (tipo `RENTAL`); las devoluciones, en el tipo
  `RENTAL_RETURN`; el proceso, en el tipo `RENTAL_PROCESS`. Los cambios de estatus, en el historial de estatus.
- **Dar de baja** pide dos permisos: `rental.maintenance` (el de la acción) y además `inventory.adjust`, porque saca el equipo del
  inventario (un ajuste de salida). Sin `inventory.adjust` responde 403 `Falta el permiso 'inventory.adjust'.`. El Operador de almacén
  tiene los dos.

## 2. La renta

Una renta tiene: número **REN-#####** (automático, por compañía), **cliente** (activo), **localidad del cliente** donde estará el
equipo, contacto del cliente (opcional), **almacén de origen** (de donde salen los equipos), **fecha de inicio**, **fecha de recogido**
(vigente) y **fecha de recogido pactada** (la original, que no cambia con las extensiones), **contrato** (número y fecha de firma,
opcionales), **costo de transporte estimado** y su moneda (solo dato), notas y los **equipos**.

Una renta = **un cliente, una localidad, varios equipos**. **Solo** se rentan equipos **propios** (sin dueño 3PL) que se controlan **por
serie**: cada equipo es **una serie**. Si los equipos llegaron sin serie (Advance Depot), primero se convierten con **Convertir a serie**
(capítulo 06 §2.1).

### 2.1 Crear (Borrador)

`POST /api/v1/rentals` (`rental.manage`)

```json
{ "clientPublicId": "…", "locationPublicId": "…", "startDate": "2026-10-05", "pickupDate": "2026-11-04",
  "warehousePublicId": "…", "clientContactId": 12, "contractNumber": "CT-77", "contractSignedOn": "2026-10-04",
  "estimatedDeliveryCost": 45, "transportCurrency": "USD", "notes": "Llevar cargador",
  "lines": [ { "productPublicId": "…", "serialNumbers": ["SN-0001", "SN-0002"],
               "rate": { "frequency": "MONTHLY", "amount": 150, "currency": "USD" } } ] }
```

- `warehousePublicId` es opcional si la compañía tiene **un solo almacén activo**.
- `lines` es opcional: los equipos se pueden agregar después. Cada renglón es un producto y sus números de serie (una línea por serie);
  `rate` es opcional (la tarifa no es obligatoria para programar).
- La renta nace en **Borrador** y **no reserva** nada: el equipo sigue disponible hasta programarla.
- Cada serie debe estar **Disponible** en una posición **recolectable** del almacén de origen (no en cuarentena, cruce de muelle ni
  En renta) y **no** puede estar en otra renta abierta (Borrador, Programada o En renta).
- Respuesta 200: la ficha (sección 2.6).

### 2.2 Editar

`PATCH /api/v1/rentals/{publicId}` — solo en **Borrador** o **Programada**. Se manda solo lo que cambia (`null` = sin cambio):
`locationPublicId` (otra localidad **del mismo cliente**), `clientContactId` / `clearClientContact`, `warehousePublicId` (solo si la renta
**no tiene equipos**), `startDate` y `pickupDate` (solo si la renta **no tiene extensiones**; la fecha pactada sigue a la vigente y la
tarifa inicial de cada equipo se mueve con la fecha de inicio), `contractNumber` (`""` lo quita), `contractSignedOn` /
`clearContractSignedOn`, `estimatedDeliveryCost` / `clearEstimatedDeliveryCost`, `transportCurrency`, `notes` (`""` las quita) y
`rowVersion` (el de la ficha). **El cliente no se cambia** (cancele la renta y cree otra); tampoco el número, el estatus, la fecha pactada
ni los enlaces a envío y factura.

### 2.3 Equipos y tarifas

- **Agregar**: `POST /api/v1/rentals/{publicId}/lines` con `{ "productPublicId", "serialNumbers": [...], "rate"? }` (Borrador o
  Programada). En una renta **Programada**, las series agregadas **se reservan** en ese momento.
- **Quitar**: `DELETE /api/v1/rentals/{publicId}/lines/{lineId}` (Borrador o Programada). En una renta Programada **libera** la reserva de
  esa serie. El equipo no se borra: queda inactivo en la renta (auditoría). Devuelve la ficha.
- **Tarifa de un equipo**: `PUT /api/v1/rentals/{publicId}/lines/{lineId}/rate` con `{ "frequency", "amount", "currency"?, "rowVersion"? }`
  (solo antes del despacho). Frecuencias: **`DAILY`** (diaria), **`WEEKLY`** (semanal), **`MONTHLY`** (mensual) y **`ONE_TIME`**
  (**"Fija"**, un solo cobro). Moneda: la de la compañía si no se indica. La tarifa es **solo un dato** (D3): no se calcula ni se factura.
  Antes del despacho se corrige la tarifa vigente en su lugar; después, la tarifa solo cambia con una **extensión** (sección 3).
- Máximo **200** equipos activos por renta.

### 2.4 Programar (Borrador → Programada)

`POST /api/v1/rentals/{publicId}/schedule` con `{ "comment"?, "rowVersion"? }` (cuerpo opcional; el comentario va al historial).

- Exige al menos un equipo y el cliente activo.
- **Reserva** cada serie en la posición donde está **hoy** (si se movió desde que se agregó, se toma la nueva posición, siempre que siga
  Disponible y en una posición recolectable del almacén de origen). La serie pasa a **Reservada** y el **disponible** del producto baja;
  el **en mano** no cambia.

### 2.5 Despachar (Programada → En renta)

`POST /api/v1/rentals/{publicId}/dispatch` con `{ "comment"?, "rowVersion"? }`.

- La primera vez que un almacén despacha una renta se crea, sola, la zona **RENT ("En renta", tipo RENTAL)** con la posición
  **EN-RENTA**.
- Por cada equipo se registra en el Kárdex una **transferencia** desde su posición hasta **EN-RENTA**, con la referencia **"Renta
  REN-#####"**. Una transferencia es **neutra** en el total del Kárdex: el producto sigue teniendo las mismas unidades en mano.
- En **EN-RENTA** el equipo queda **en mano y reservado** (disponible 0); la serie pasa a **"En renta"** (`ON_RENT`). La renta pasa a **En
  renta** y se sella la fecha de despacho; cada equipo guarda su movimiento de despacho.
- La zona En renta **no se asigna, no se recolecta, no se acomoda ni se recibe en ella**: ni las recolecciones, ni las sugerencias de
  acomodo, ni el recibo directo a posición la usan, y "solo con disponible" no la cuenta.
- Una serie **En renta no se puede mover, recolectar, ajustar ni recibir de nuevo** por las pantallas normales: vuelve con la
  **devolución de renta** (bloque R2).

### 2.6 La ficha y la lista

Ficha (`GET /api/v1/rentals/{publicId}`): `rental` (número, cliente, localidad, almacén, fechas, **`daysToPickup`** = días hasta el
recogido — negativo si ya pasó —, **`isOverdue`** = "vencida", contrato, estatus con su color, `units` = equipos activos,
`extensionCount`, fechas de despacho y cierre), contacto, fecha de firma, transporte estimado y moneda, **`deliveryShipmentId`** e
**`invoiceId`** (vacíos: se llenarán cuando existan Envíos y Facturación), notas, qué se puede hacer (`canEdit`, `canSchedule`,
`canDispatch`, `canExtend`, `canCancel`), los **equipos** (producto, serie, lote, posición de origen, movimiento y fecha de despacho,
tarifa vigente e **historial de tarifas**) y `rowVersion`. En una renta **cancelada** se ven los equipos que tenía, inactivos.

Lista (`GET /api/v1/rentals`): paginada (`skip`, `take` ≤ 200), más recientes primero. Filtros:
- `status` (uno o varios: `DRAFT`, `SCHEDULED`, `ON_RENT`, `RETURNED`, `CANCELLED`) y `clientPublicId`;
- **`dueWithinDays=N`**: **por vencer** = Programadas o En renta cuya fecha de recogido está entre **hoy y hoy + N días**;
- **`overdue=true`**: **vencidas** = Programadas o En renta cuya fecha de recogido **ya pasó** (antes de hoy);
- los dos juntos: vencidas **o** por vencer; con cualquiera de ellos la lista va por fecha de recogido;
- `search`: número de renta, número de contrato, cliente, localidad o número de serie.

"Hoy" es el día de la **zona horaria de la compañía**. "Vencida" **no es un estatus**: se calcula.

## 3. Extender la fecha de recogido (D4)

`POST /api/v1/rentals/{publicId}/extensions` (**`rental.extend`**) — solo en **Programada** o **En renta**.

```json
{ "newPickupDate": "2026-11-19", "reason": "El hospital pidió dos semanas más",
  "rates": [ { "lineId": 31, "frequency": "MONTHLY", "amount": 165 } ], "rowVersion": "…" }
```

- La nueva fecha debe ser **posterior** a la vigente; el **motivo** es obligatorio (hasta 300 caracteres).
- Se guarda en la **bitácora** (fecha anterior, nueva, motivo, quién y cuándo) y la renta toma la nueva fecha de recogido; la fecha
  **pactada** no cambia. No hace falta la aprobación de otra persona.
- `rates` (opcional): tarifa nueva por equipo. **Solo si cambia** (frecuencia, monto o moneda) se cierra la vigente y se abre una versión
  nueva **desde el día siguiente al recogido anterior**, ligada a la extensión. Una tarifa igual a la vigente no crea versión.
- Respuesta: la ficha. La bitácora: `GET /api/v1/rentals/{publicId}/extensions` (cada extensión con `daysAdded` y las tarifas que abrió).

## 4. Cancelar

`POST /api/v1/rentals/{publicId}/cancel` con `{ "comment"?, "rowVersion"? }` (`rental.manage`) — **solo en Borrador o Programada**.

- En una renta Programada **libera** las reservas: las series vuelven a **Disponible** y el disponible del producto sube.
- Los equipos quedan inactivos (la serie queda libre para otra renta), se sella la fecha de cierre y el comentario va al historial.
- Una renta **despachada no se cancela**: termina con su devolución (R2).

## 5. Devolver una renta (bloque R2)

`POST /api/v1/rentals/{publicId}/returns` (**`rental.return`**) — solo en una renta **En renta**. Registra una **devolución de renta**
DRN-##### (numeración propia por compañía) con los equipos que vuelven:

```json
{ "reason": "EARLY_DAMAGE", "returnedOn": "2026-10-20", "notes": "Pantalla rota en el hospital",
  "toBinId": 412, "estimatedPickupCost": 30, "transportCurrency": "USD", "rowVersion": "…",
  "lines": [ { "serialNumber": "SN-0002", "condition": "DAMAGED", "toBinId": 415, "requiresProcess": true, "notes": "Golpe lateral" } ] }
```

- **Motivo** (uno por devolución, obligatorio): `END_OF_CONTRACT` "Fin del contrato", `EARLY_DAMAGE` "Anticipada por daño",
  `EARLY_CLIENT` "Anticipada a pedido del cliente" u `OTHER` "Otro" (con "Otro" las **notas** son obligatorias).
- **Equipos**: cada uno por su **número de serie**, que debe ser un equipo **despachado y sin devolver de esa renta**. Se puede devolver
  **una parte** (por ejemplo, el equipo que se dañó) y el resto después: la renta sigue **En renta** hasta que vuelve el último equipo.
- **Condición** de cada equipo: `GOOD` "Buena" (por defecto), `DAMAGED` "Dañado" o `INCOMPLETE` "Incompleto".
- **Posición de destino**: la del equipo (`lines[].toBinId`), si no la del encabezado (`toBinId`) y, si no se indica ninguna, **la posición
  de donde salió** el equipo. Puede ser de **otro almacén** de la compañía (por ejemplo, cuarentena de un taller). **Nunca** la zona En
  renta.
- **¿Pasa por proceso?** (`requiresProcess`, **sí por defecto**): lo decide quien recibe. Con proceso, el equipo queda **en mano pero
  reservado** y su serie **"En proceso"** (no cuenta como disponible) y se abre su **proceso** (sección 6) en el primer paso
  ("Pendiente"). Sin proceso, queda **Disponible** de inmediato.
- **Fecha** de la devolución: hoy por defecto (día de la compañía); no puede ser futura ni anterior al inicio de la renta. La devolución
  es **anticipada** (`isEarly`) si la fecha es anterior a la fecha de recogido vigente: es un dato calculado para los reportes.
- **Costo de recogido estimado** y su moneda: solo dato; el enlace al envío futuro de recogido (`pickupShipmentId`) queda vacío (D2).

En el inventario, por cada equipo se registra en el Kárdex una **transferencia** desde **EN-RENTA** hasta la posición de destino, con la
referencia **"Devolución de renta DRN-#####"** (neutra en el total: el equipo siguió siendo de la compañía). Cuando vuelven **todos** los
equipos despachados, la renta pasa a **Devuelta** (terminal) y se sella su fecha de cierre; el comentario del historial es
"Devolución DRN-#####".

Respuesta (la ficha de la devolución): número, renta, cliente, fecha, motivo, `isEarly`, `units`, `openProcesses` (equipos aún en
proceso), el estatus de la renta después de la devolución (`rentalStatusCode`), notas, costo de recogido y los equipos (producto, serie,
condición, almacén y posición de destino, si pasa por proceso, movimiento del Kárdex, notas y el proceso con su estatus).

Lista: `GET /api/v1/rental-returns` (`rental.view`), paginada (`skip`, `take` ≤ 200), más recientes primero. Filtros: `rentalPublicId`,
`clientPublicId`, `reason` (uno o varios), `from`/`to` (fecha de devolución), `early=true|false` y `search` (número DRN, número de renta o
serie). Ficha: `GET /api/v1/rental-returns/{publicId}`. La ficha de la renta muestra en cada equipo su `returnedAtUtc`.

## 6. Proceso del equipo devuelto (configurable)

Cada equipo devuelto "con proceso" tiene su **proceso** (`RentalProcess`) con los estatus del pipeline **RentalProcessStatus**, que la
compañía puede **renombrar, reordenar y desactivar** desde la configuración de estatus:

| Estatus | Código | Tipo | Qué significa |
|---|---|---|---|
| Pendiente | `PENDING` | inicial | recién devuelto, sin revisar |
| Inspección | `INSPECTION` | etapa | se revisa el equipo |
| Limpieza | `CLEANING` | etapa | limpieza / desinfección |
| Pruebas | `TESTING` | etapa | pruebas de funcionamiento |
| Lista | `READY` | terminal | el equipo vuelve a estar **disponible** |
| Reparación | `REPAIR` | lateral | en reparación (desde cualquier paso) |
| Esperando piezas | `AWAITING_PARTS` | lateral | detenido por piezas (desde cualquier paso) |
| Dada de baja | `SCRAPPED` | terminal | el equipo sale del inventario |

- **Cola**: `GET /api/v1/rental-processes` (`rental.view`): abiertos primero (los más viejos arriba), luego los terminados. Filtros
  `status` (uno o varios), `open=true|false`, `warehousePublicId`, `productPublicId` y `search` (serie, SKU, número de devolución o de
  renta). Cada fila: serie, producto, almacén y posición actual, estatus con su color, `isFinished`, devolución y renta de origen, condición
  con que volvió, fechas de inicio y fin, notas (las del equipo en la devolución) y `rowVersion`.
- **Avanzar**: `POST /api/v1/rental-processes/{id}/advance` con `{ "status": "CLEANING", "comment"?, "rowVersion"? }`
  (`rental.maintenance`). Se puede pasar a **cualquier estatus habilitado**, con las reglas de siempre de los pipelines: de un paso al
  **siguiente** habilitado; a **Reparación** o **Esperando piezas** desde cualquier paso, y de ahí de vuelta al paso en que estaba o al
  siguiente; a un terminal (Lista o Dada de baja). Un salto ilegal responde 422 con el mensaje del motor de estatus. Pasar a `SCRAPPED`
  por aquí pide también `inventory.adjust` (es igual que dar de baja).
- **Terminar** ("Lista"): `POST /api/v1/rental-processes/{id}/complete` con `{ "binId"?, "comment"?, "rowVersion"? }` (`rental.maintenance`).
  Con `binId` (posición del **mismo almacén**, no de la zona En renta), primero se **traslada** el equipo (transferencia con la referencia
  "Proceso #id", sigue reservado y "En proceso") y después pasa a Lista. Al llegar a **Lista** se **libera la reserva**: la serie pasa de
  "En proceso" a **Disponible** y el disponible del producto **vuelve**.
- **Dar de baja**: `POST /api/v1/rental-processes/{id}/scrap` con `{ "comment"?, "rowVersion"? }` (`rental.maintenance` **y**
  `inventory.adjust`). Se registra un **ajuste de salida** (`ADJUSTMENT −1`) desde la posición del proceso con el motivo **DAMAGE**
  ("Daño") y la referencia "Proceso #id" (nota "Baja del proceso #id"); la serie queda **Dada de baja** (no vuelve al inventario) y el en
  mano del producto baja en uno.
- Los efectos en el inventario dependen **solo de los terminales** (Lista libera, Dada de baja da de baja): los pasos intermedios no mueven
  inventario, por eso la compañía puede configurarlos libremente. Llegar a Lista por "avanzar" hace lo mismo que "terminar" (sin traslado).
- Un proceso **terminado** (Lista o Dada de baja) solo se consulta: cualquier acción responde 422 `El proceso ya terminó; solo se consulta.`
- Historial: `GET /api/v1/status/history/RENTAL_PROCESS/{id}` (cada paso con su comentario, quién y cuándo).

## 7. Equipos en renta y el conteo cíclico (D7)

- La posición **EN-RENTA no se cuenta**: crear un conteo pidiendo esa posición o la zona RENT, un conteo de "lo cambiado" con la zona
  RENT, o agregar una línea en EN-RENTA responde 422 `La posición {bin} es de equipos en renta; no se cuenta.`. Los conteos amplios (todo el
  almacén, por producto, lo cambiado) la **saltan** sin error.
- Una serie **En renta** capturada en otra posición **bloquea** el conteo: la vista previa marca la línea y "Confirmar conteo y ajustar"
  responde 409 `La serie {s} está en renta ({REN-n}); registre su devolución antes de reconciliar el conteo.` sin mover nada. Si el equipo
  de verdad volvió, registre su devolución (sección 5) y recapture; si fue un error de captura, quítela y confirme.
- Las series "En proceso" están en su posición, en mano y **reservadas**: se cuentan como siempre; si no se encuentran, el conteo
  responde el 409 de "lo contado es menor que lo reservado" (termine o dé de baja el proceso primero).

## 8. Estatus y transiciones

| Estatus | Código | Tipo | Qué permite |
|---|---|---|---|
| Borrador | `DRAFT` | inicial | editar, equipos, tarifas, programar, cancelar |
| Programada | `SCHEDULED` | etapa | editar, equipos (reservan/liberan), tarifas, despachar, extender, cancelar |
| En renta | `ON_RENT` | etapa | extender; devolver (total o parcial) |
| Devuelta | `RETURNED` | terminal | solo consulta (se ven sus devoluciones) |
| Cancelada | `CANCELLED` | terminal | solo consulta |

| De → a | Quién | Efectos | Qué lo bloquea |
|---|---|---|---|
| (nueva) → Borrador | `rental.manage` | número REN-#####; equipos sin reservar | cliente dado de baja, localidad ajena o dada de baja, serie no disponible o en otra renta |
| Borrador → Programada | `rental.manage` | reserva las series (serie Reservada; disponible baja) | sin equipos, cliente dado de baja, serie no disponible |
| Programada → En renta | `rental.manage` | transferencia a EN-RENTA, reservada en el destino; serie En renta; fecha de despacho | cliente dado de baja; reserva perdida; posición EN-RENTA desactivada; estatus de serie `ON_RENT` desactivado por la compañía |
| Borrador / Programada → Cancelada | `rental.manage` | libera reservas; equipos inactivos; fecha de cierre | despachada (también lo impide el sistema aunque se cambie la regla) |
| En renta → Devuelta | `rental.return` | la **última** devolución (cuando ya no queda ningún equipo despachado sin devolver); fecha de cierre; comentario "Devolución DRN-#####" | estatus `RETURNED` desactivado por la compañía (422) |
| (devolución) | `rental.return` | por equipo: transferencia EN-RENTA → destino; serie En proceso y reservada (con proceso) o Disponible; abre el proceso | renta no En renta; serie que no está en renta en esa renta; destino en la zona En renta |

Estatus de la **serie** agregados por Rentas (laterales, del sistema): **En renta** (`ON_RENT`) y **En proceso** (`IN_PROCESS`, mientras
el equipo devuelto está en su proceso). Ambos cuentan como "en inventario" en el conteo cíclico y en el indicador "series por capturar".
Ciclo de la serie de un equipo rentado: Disponible → Reservada (programar) → En renta (despachar) → En proceso (devolver con proceso) →
Disponible (Lista) o Dada de baja; sin proceso, de En renta a Disponible.

Transiciones del **proceso** (sección 6):

| De → a | Quién | Efectos | Qué lo bloquea |
|---|---|---|---|
| (devolución con proceso) → Pendiente | `rental.return` | se abre con la serie En proceso y reservada en la posición de destino | estatus inicial desactivado (422) |
| paso → siguiente paso, paso ↔ Reparación / Esperando piezas | `rental.maintenance` | ninguno en el inventario | salto ilegal o estatus desactivado (422); proceso terminado (422) |
| cualquiera → Lista | `rental.maintenance` | traslado opcional en el almacén y **libera la reserva** (serie Disponible); fecha de fin | posición de otro almacén o de la zona En renta (400); proceso terminado (422) |
| cualquiera → Dada de baja | `rental.maintenance` + `inventory.adjust` | **ajuste de salida** con motivo DAMAGE; serie Dada de baja; fecha de fin | sin `inventory.adjust` (403); proceso terminado (422) |

La compañía puede **renombrar** y **reordenar** los estatus de la renta, pero **no** debe desactivarlos: si desactiva "Programada", "En
renta" (de la renta o de la serie) o "Cancelada", la acción correspondiente responde 422 `El estatus '{código}' no existe o no está
habilitado para esta compañía.`

## 9. Mensajes (servidor)

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Alta sin cliente | `Indique el cliente de la renta.` | 400 |
| Alta sin localidad | `Indique la localidad del cliente donde estará el equipo.` | 400 |
| Alta sin fecha de inicio / sin fecha de recogido | `Indique la fecha de inicio de la renta.` / `Indique la fecha de recogido.` | 400 |
| Recogido antes del inicio (alta o edición) | `La fecha de recogido no puede ser anterior a la de inicio.` | 400 |
| Localidad de otro cliente (alta o edición) | `La localidad no pertenece al cliente de la renta.` | 400 |
| Número de contrato de más de 80 / notas de más de 1000 caracteres | `El número de contrato admite como máximo 80 caracteres.` / `Las notas admiten como máximo 1000 caracteres.` | 400 |
| Costo de transporte negativo | `El costo de transporte estimado no puede ser negativo.` | 400 |
| Moneda que no existe (transporte o tarifa) | `Moneda desconocida: '{código}'.` | 400 |
| Equipo sin producto / sin series | `Indique el producto del equipo.` / `Indique al menos un número de serie.` | 400 |
| Serie vacía, larga, repetida o demasiadas | `Los números de serie no pueden estar vacíos.` / `Un número de serie admite como máximo 80 caracteres.` / `El número de serie {s} está repetido.` / `Una línea admite como máximo 500 números de serie.` | 400 |
| Más de 200 equipos | `Una renta admite como máximo 200 equipos.` | 400 |
| Producto de un cliente 3PL | `Solo se rentan equipos propios; {sku} pertenece a un cliente.` | 400 |
| Producto sin seguimiento por serie | `El producto {sku} no se controla por serie; solo se rentan equipos con número de serie.` | 400 |
| Tarifa sin frecuencia / frecuencia desconocida | `Indique la frecuencia de cobro: DAILY, WEEKLY, MONTHLY o ONE_TIME.` / `Frecuencia de cobro desconocida: '{x}'. Use DAILY, WEEKLY, MONTHLY o ONE_TIME.` | 400 |
| Tarifa sin monto / negativa | `Indique el monto de la tarifa.` / `La tarifa no puede ser negativa.` | 400 |
| Cambiar el cliente en el PATCH | `El cliente de la renta no se cambia; cancele la renta y cree otra.` | 400 |
| Cambiar número, estatus, fecha pactada, fechas de despacho/cierre o enlaces en el PATCH | `El campo '{campo}' no se puede modificar.` | 400 |
| Extensión sin nueva fecha / sin motivo / motivo largo | `Indique la nueva fecha de recogido.` / `Indique el motivo de la extensión.` / `El motivo admite como máximo 300 caracteres.` | 400 |
| Extensión con una fecha igual o anterior a la vigente | `La nueva fecha de recogido debe ser posterior a la actual ({aaaa-mm-dd}).` | 400 |
| Tarifa de extensión sin equipo | `Indique el equipo (lineId) de la tarifa.` | 400 |
| Lista con `dueWithinDays` negativo | `Los días deben ser 0 o más.` | 400 |
| `rowVersion` que no es base64 | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 |
| Sin el permiso de la acción | (403 `PERMISSION_DENIED`) | 403 |
| Módulo Rentas apagado | `El módulo 'RENTAL_EQUIPMENT' no está habilitado para esta compañía.` | 403 |
| Renta / cliente / localidad / contacto / almacén / producto / equipo de otra compañía o inexistente | `Renta no encontrada.` / `Cliente no encontrado.` / `Localidad no encontrada.` / `Contacto no encontrado.` / `Almacén no encontrado.` / `Producto no encontrado.` / `Línea no encontrada.` | 404 |
| Cliente dado de baja (alta, programar, despachar) | `El cliente está dado de baja; solo se consulta su historial.` | 409 |
| La serie ya está en otra renta abierta | `La serie {s} ya está en la renta {REN-n}.` | 409 |
| La serie no está Disponible en una posición recolectable del almacén de origen (no existe, está reservada, rentada, en cuarentena, en otro almacén…) | `La serie {s} no está disponible en {posición o almacén}.` | 409 |
| Otra operación tomó la serie al mismo tiempo | `Una de las series se acaba de agregar a otra renta; recargue e intente de nuevo.` | 409 |
| Cambiar el almacén de una renta con equipos | `Quite los equipos de la renta antes de cambiar el almacén de origen.` | 409 |
| Otro usuario cambió la renta (con `rowVersion`) | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| El almacén ya usa el código RENT o EN-RENTA para otra zona o posición | `El almacén {código} ya tiene una zona RENT que no es de rentas; cámbiele el código para poder despachar rentas.` / `El almacén {código} ya tiene una posición EN-RENTA fuera de la zona En renta; cámbiele el código para poder despachar rentas.` | 409 |
| Editar, agregar/quitar equipos o tarifa de una renta despachada o devuelta | `La renta {n} ya fue despachada; no se puede modificar.` | 422 |
| Editar una renta cancelada | `La renta {n} está cancelada; solo se consulta.` | 422 |
| Programar o despachar sin equipos | `La renta no tiene equipos; agregue al menos uno.` | 422 |
| Programar una renta que no está en Borrador | `Solo se programa una renta en Borrador; la renta {n} no lo está.` | 422 |
| Despachar una renta que no está Programada | `Solo se despacha una renta Programada; programe la renta {n} primero.` | 422 |
| Cancelar una renta En renta, Devuelta o Cancelada | `Solo se cancela una renta en Borrador o Programada; para terminarla registre la devolución.` | 422 |
| Extender una renta en Borrador, Devuelta o Cancelada | `Solo se extiende una renta Programada o En renta.` | 422 |
| Cambiar fechas de una renta con extensiones | `La renta ya tiene extensiones; la fecha de recogido se cambia con una extensión.` | 422 |
| Localidad dada de baja | `La localidad está dada de baja; elija otra.` | 422 |
| Almacén de origen inactivo | `El almacén {código} está inactivo; no admite movimientos de inventario.` | 422 |
| Posición EN-RENTA desactivada a mano | `La posición EN-RENTA está inactiva; no admite movimientos de inventario.` | 422 |

Devolución, proceso y conteo (bloque R2):

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Devolución sin motivo / motivo desconocido | `Indique el motivo de la devolución: END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT u OTHER.` / `Motivo de devolución desconocido: '{x}'. Use END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT u OTHER.` | 400 |
| Motivo "Otro" sin notas | `Con el motivo 'Otro' describa la devolución en las notas.` | 400 |
| Sin equipos / equipo sin serie / serie repetida | `Indique al menos una serie que se devuelve.` / `Indique el número de serie del equipo devuelto.` / `El número de serie {s} está repetido.` | 400 |
| Condición desconocida | `Condición desconocida: '{x}'. Use GOOD, DAMAGED o INCOMPLETE.` | 400 |
| Notas de la devolución de más de 1000 / del equipo de más de 500 caracteres | `Las notas admiten como máximo 1000 caracteres.` / `Las notas del equipo admiten como máximo 500 caracteres.` | 400 |
| Fecha futura / anterior al inicio | `La fecha de devolución no puede ser futura.` / `La fecha de devolución no puede ser anterior al inicio de la renta ({aaaa-mm-dd}).` | 400 |
| Costo de recogido negativo / moneda desconocida | `El costo de recogido estimado no puede ser negativo.` / `Moneda desconocida: '{código}'.` | 400 |
| Posición de destino en la zona En renta (devolución o terminar el proceso) | `La posición de destino no puede ser de la zona En renta.` | 400 |
| Terminar el proceso con una posición de otro almacén | `La posición de destino debe ser del almacén {código} del proceso.` | 400 |
| Avanzar sin estatus | `Indique el estatus al que pasa el proceso.` | 400 |
| Dar de baja (o avanzar a SCRAPPED) sin `inventory.adjust` | `Falta el permiso 'inventory.adjust'.` | 403 |
| Devolución / proceso / posición de otra compañía o inexistente | `Devolución de renta no encontrada.` / `Proceso no encontrado.` / `Posición no encontrada.` / `Renta no encontrada.` | 404 |
| La serie no es un equipo despachado y sin devolver de esa renta | `La serie {s} no está en renta en {REN-n}.` | 409 |
| Otra operación devolvió el mismo equipo al mismo tiempo | `Uno de los equipos se acaba de devolver en otra operación; recargue e intente de nuevo.` | 409 |
| Otro usuario cambió la renta o el proceso (con `rowVersion`) | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Conteo: serie En renta capturada en otra posición | `La serie {s} está en renta ({REN-n}); registre su devolución antes de reconciliar el conteo.` | 409 |
| Devolver una renta que no está En renta (Borrador, Programada, Devuelta, Cancelada) | `Solo se registra la devolución de una renta En renta; la renta {n} no lo está.` | 422 |
| Cualquier acción sobre un proceso Lista o Dado de baja | `El proceso ya terminó; solo se consulta.` | 422 |
| Avanzar a un estatus desactivado o con un salto ilegal | `El estatus '{código}' no existe o no está habilitado para esta compañía.` / `Salto ilegal: de '{de}' solo se puede avanzar a '{siguiente}'.` / `Desde el lateral '{x}' solo se puede regresar a '{paso}' o avanzar a '{siguiente}'.` | 422 |
| Conteo: pedir la posición EN-RENTA o la zona RENT, o agregar una línea en EN-RENTA | `La posición {bin} es de equipos en renta; no se cuenta.` | 422 |
| Destino inactivo (posición o almacén) | `La posición {código} está inactiva; no admite movimientos de inventario.` / `El almacén {código} está inactivo; no admite movimientos de inventario.` | 422 |

Mensajes del inventario que se ven al intentar mover una serie rentada (por las pantallas normales):

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Transferir o ajustar una serie En renta | `La serie {s} no está disponible en EN-RENTA.` | 409 |
| Recolectar una serie En renta | `La serie {s} no está disponible en el almacén (no existe, ya salió o está en otra posición o lote).` | 409 |
| Recibir de nuevo (devolución normal) una serie En renta | `La serie {s} ya está en inventario.` | 409 |

## 10. Reportes, indicadores y avisos (bloque R3)

Las rentas, sus devoluciones y los procesos de los equipos devueltos se pueden consultar en **Análisis** (vistas, indicadores y gráficos,
con los filtros, columnas y agrupaciones de siempre) y en el **Pulso** ("Necesita tu atención"). Reglas que aplican a todo lo de esta
sección:

- **Quién**: hace falta `rental.view` (además de `analytics.view` para Análisis o `pulse.attention` para el Pulso). Sin `rental.view` las
  fuentes de rentas no aparecen en la lista de fuentes y sus vistas, indicadores y gráficos no se listan ni se leen (404, como una fuente
  sin permiso).
- **Módulo**: con el módulo **Rentas** (`RENTAL_EQUIPMENT`) **apagado**, las fuentes de rentas, sus vistas, indicadores y gráficos (los de
  sistema y los que haya creado la compañía) y el aviso **desaparecen**; leer uno por su id responde 404 (`Indicador '{id}' no
  encontrado.`, `Gráfico '{id}' no encontrado.`, `Vista '{id}' no encontrado.`). Al volver a encender el módulo, todo vuelve tal como
  estaba (no se borra nada). Como Rentas depende de Inventario y trazabilidad, apagar `WMS_LOTSERIAL` también las oculta.
- **Compañía**: cada compañía ve solo sus rentas, devoluciones y procesos.
- **"Hoy"** es el día de la **zona horaria de la compañía** (Ajustes de la compañía): "vencida", "por vencer", "días para el recogido" y
  "días en proceso" se calculan al momento de leer, no se guardan. "Vencida" no es un estatus.

### 10.1 Fuentes de datos

| Fuente (clave) | Una fila por | Campo de fecha (rango) | Campos |
|---|---|---|---|
| **Rentas** (`RENTAL`) | renta | **Fecha de inicio** (`StartDate`) | Número, cliente, localidad, almacén de origen, estatus y su código, **Abierta** (Programada o En renta), fecha de inicio, **fecha de recogido** (vigente), recogido pactado, **Días para el recogido** (negativo = ya pasó), **Vencida** (abierta con el recogido antes de hoy), extensiones, días extendidos (recogido vigente − pactado), **Equipos** (activos), **Equipos en el cliente** (despachados y sin devolver), equipos devueltos, número y fecha del contrato, transporte estimado (dinero) y su moneda, fechas de despacho, cierre y alta. Relaciones: Cliente, Localidad, Almacén |
| **Devoluciones de renta** (`RENTAL_RETURN`) | devolución DRN | **Fecha de devolución** (`ReturnedOn`) | Número, renta, cliente, localidad, fecha de devolución, fecha de recogido de la renta, **Motivo** y su código, **Anticipada** (antes de la fecha de recogido vigente), días de anticipación, **Condición** (las condiciones de sus equipos, en orden Buena, Dañado, Incompleto; p. ej. "Buena, Incompleto") y su código, **Con equipo dañado**, equipos (total, en buena condición, dañados, incompletos), equipos con proceso, procesos abiertos, recogido estimado (dinero) y su moneda, fecha de registro. Relaciones: Renta, Cliente |
| **Proceso de equipos devueltos** (`RENTAL_PROCESS`) | proceso de un equipo | **Iniciado el** (`StartedAtUtc`) | Serie, producto (SKU y nombre), almacén, posición actual, estatus y su código, **Abierto** (no llegó a Lista ni a Dada de baja), devolución, renta, cliente, **Condición al volver**, inicio, fin, **Días en proceso** (de la compañía, hasta el fin o hasta hoy). Relaciones: Producto, Almacén, Devolución, Renta |

- Las tres fuentes admiten los **campos personalizados** de su entidad (`RENTAL`, `RENTAL_RETURN`, `RENTAL_PROCESS`) como columnas.
- El **rango de fechas** de un indicador o gráfico filtra por el campo de fecha de la fuente (días de la compañía). Para el estado actual
  (lo que está vencido hoy, lo que está en proceso hoy) se usa el rango **"Todo"** (ALL): así vienen sembrados.
- Tope: 20 000 filas por fuente (las más recientes por su fecha), como el resto de las fuentes.

### 10.2 Vistas (reportes) de sistema

Se siembran en cada compañía (son de sistema: no se editan ni se borran). Corren con el rango "Todo" salvo que se
indique otro al correrlas.

| Vista | Fuente | Qué muestra | Filtro | Columnas / agrupación | Orden |
|---|---|---|---|---|---|
| **Equipos en renta por cliente** | Rentas | Lo que está **en renta hoy**, por cliente | Estatus = En renta | Agrupada por **Cliente**: número de rentas y **suma de equipos en el cliente**, con fila de totales | — |
| **Rentas por vencer (7 días)** | Rentas | Rentas abiertas que se recogen **hoy o en los próximos 7 días** | Abierta y días para el recogido entre 0 y 7 | Número, cliente, localidad, almacén, estatus, fecha de recogido, días para el recogido, equipos, equipos en el cliente, contrato | Fecha de recogido (las más próximas arriba) |
| **Rentas vencidas** | Rentas | Rentas abiertas con la fecha de recogido **ya pasada** | Vencida | Las mismas columnas | Fecha de recogido (las más vencidas arriba) |
| **Devoluciones de renta por motivo** | Devoluciones de renta | Devoluciones por **motivo** | — | Agrupada por **Motivo**: número de devoluciones y suma de equipos devueltos, con totales | — |
| **Equipos en proceso** | Proceso de equipos devueltos | Los equipos devueltos que **siguen en su proceso** | Abierto | Serie, SKU, producto, almacén, posición, estatus, condición, devolución, renta, cliente, iniciado el, días en proceso | Iniciado el (los más viejos arriba) |

"Abiertas" = **Programadas o En renta** (una renta Programada cuyo recogido ya pasó también cuenta como vencida: el equipo está apartado).

### 10.3 Indicadores de sistema

| Indicador | Fuente | Cálculo | Rango | Pulso |
|---|---|---|---|---|
| **Rentas por vencer (7 días)** | Rentas | Conteo de rentas abiertas con el recogido entre hoy y hoy + 7 | Todo | Apagado (cada usuario lo puede encender con "mostrar en Pulso") |
| **Rentas vencidas** | Rentas | Conteo de rentas abiertas con el recogido antes de hoy | Todo | Apagado |

Están en el módulo de negocio **Almacén** y vienen apagados en el Pulso porque "Necesita tu atención" ya muestra cada renta vencida o
por vencer (mismo criterio que "Descuadres pendientes").

### 10.4 Gráfico de sistema

**Devoluciones de renta por motivo**: dona con el número de devoluciones de los **últimos 30 días** (por fecha de devolución) por motivo
(Fin del contrato, Anticipada por daño, Anticipada a pedido del cliente, Otro). Apagado en el Pulso; cada usuario puede cambiar el rango o
encenderlo.

### 10.5 Aviso "Necesita tu atención" (Pulso)

`GET /api/v1/analytics/attention` suma, al lado de los descuadres del inventario, una fila por cada renta **abierta** (Programada o En
renta) que está **vencida** o que **vence en los próximos 7 días** (recogido entre hoy y hoy + 7, ambos incluidos):

- **Quién lo ve**: quien tiene `pulse.attention` y `rental.view`, con el módulo Rentas encendido. Sin `rental.view` o con el módulo
  apagado, el aviso no aporta filas ni cuenta en el total (no da error).
- **Orden**: las de recogido más antiguo primero (las más vencidas arriba); junto con los demás avisos, el panel muestra los 5 más
  antiguos.
- **Tono**: rojo (`danger`) si está vencida; ámbar (`warn`) si está por vencer.
- **Datos de la fila** (`params`): número de renta, cliente, localidad, almacén, fecha de recogido (aaaa-mm-dd), días para el recogido
  (negativo = vencida), `overdue` (true/false), equipos sin devolver y estatus. `sinceUtc` = medianoche (hora de la compañía) del día de
  recogido. Código del aviso: **`RENTAL_DUE`**.
- **"Revisar"** abre `/warehouse/rentals?rental={publicId}` y **"Ver todos (N)"** `/warehouse/rentals?dueWithinDays=7&overdue=true` (la lista
  de rentas con los filtros "por vencer" y "vencidas" juntos, sección 2.6).
- El aviso desaparece solo cuando la renta deja de estar vencida o por vencer: al **extenderla** (si la nueva fecha queda a más de 7
  días), al **devolver** todos sus equipos o al **cancelarla** (si estaba Programada).

Desde la web de Rentas (Lote F17, bloque F-R1) el panel muestra la fila con su texto (*"Renta REN-… vencida"*, *"…: se recoge hoy"* o
*"…: vence en N días"*, cliente · localidad · almacén, recogido, equipos y días vencida) y "Revisar"/"Ver todos" abren la ficha y la lista
de Almacén → Rentas (capítulo de pantallas [F17](frontend/f17-rentas.md)).

### 10.6 Mensajes

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Leer un indicador, gráfico o vista de rentas sin `rental.view` o con el módulo Rentas apagado | `Indicador '{id}' no encontrado.` / `Gráfico '{id}' no encontrado.` / `Vista '{id}' no encontrado.` | 404 |
| Vista previa o alta de una vista, indicador o gráfico sobre una fuente de rentas sin `rental.view` o con el módulo apagado | `Fuente de datos 'RENTAL' no encontrado.` (o `RENTAL_RETURN`, `RENTAL_PROCESS`) | 404 |
| Análisis sin `analytics.view` / panel sin `pulse.attention` | (403 `PERMISSION_DENIED`) | 403 |
| Análisis con el módulo Análisis apagado | `El módulo 'ANALYTICS' no está habilitado para esta compañía.` | 403 |

## 11. Kárdex, auditoría y datos

- Kárdex: el despacho son **transferencias** con origen la posición del equipo y destino **EN-RENTA**, referencia **"Renta REN-#####"**
  (en inglés "Rental REN-#####"); filtrar por `refEntity=RENTAL&refId={id}`. El detalle del movimiento muestra la renta (número, estatus,
  cliente y número de contrato).
- Kárdex de la devolución: **transferencias** desde EN-RENTA a la posición de destino con la referencia **"Devolución de renta DRN-#####"**
  (`refEntity=RENTAL_RETURN&refId={id}`); el detalle muestra la devolución (número, fecha, cliente y número de la renta).
- Kárdex del proceso: el traslado al terminar es una **transferencia** "Proceso #id" y la baja un **ajuste −1** con motivo DAMAGE
  (`refEntity=RENTAL_PROCESS&refId={id}`); el detalle muestra el proceso con su estatus y, como documento padre, su devolución.
- Saldos: la posición EN-RENTA muestra lo rentado **en mano y reservado**. El producto: en mano igual, disponible sin lo rentado.
- Series: `GET /api/v1/products/{publicId}/serials` muestra las rentadas con estatus "En renta" en EN-RENTA.
- Auditoría: `GET /api/v1/audit/changes?entityType=RENTAL&entityId={id}`; devoluciones `entityType=RENTAL_RETURN`, procesos
  `entityType=RENTAL_PROCESS`.

## 12. Casos frecuentes

- **Quiero rentar un equipo de Depot y no me deja**: el producto no se controla por serie (400). Conviértalo con **Convertir a serie**
  (capítulo 06 §2.1) capturando las series de cada unidad, y agréguelo por su serie.
- **Programé una renta y el producto aparece como "No disponible"**: es lo esperado: las series quedan reservadas para la renta.
- **Despaché y el inventario no bajó**: correcto (D1): el equipo sigue siendo nuestro; está en la posición EN-RENTA, en mano y reservado.
- **El cliente pidió más tiempo**: use **Extender** (`rental.extend`) con la nueva fecha y el motivo; si cambia la tarifa, indíquela.
- **Me equivoqué de equipo antes de despachar**: quítelo (DELETE de la línea) y agregue el correcto; en Programada la reserva se libera y
  se toma la nueva.
- **El cliente desistió antes de la entrega**: cancele la renta (solo Borrador o Programada).
- **El equipo se dañó en el cliente**: registre una devolución **solo de ese equipo** con el motivo "Anticipada por daño" y la condición
  "Dañado" (a cuarentena, por ejemplo); pasa por proceso. El resto sigue En renta hasta su devolución.
- **La renta terminó**: devuelva todos los equipos (motivo "Fin del contrato"); la renta pasa a Devuelta. Los que no necesitan revisión,
  con `requiresProcess: false`: quedan disponibles al momento.
- **Devolví un equipo y aparece "No disponible"**: está en su proceso ("En proceso", reservado). Avance el proceso y termínelo ("Lista")
  para que vuelva a estar disponible.
- **El equipo no tiene arreglo**: en su proceso use **Dar de baja** (pide `inventory.adjust`): sale del inventario con motivo "Daño" y la
  serie queda dada de baja.
- **Al reconciliar un conteo dice que una serie está en renta**: el equipo está registrado en el cliente. Si de verdad volvió, registre
  su devolución y recapture; si no, quite la serie de la captura.
- **No quiero el paso "Limpieza"**: desactívelo en la configuración de estatus del proceso; los efectos en el inventario solo dependen de
  "Lista" y "Dada de baja".
- **¿Qué rentas tengo que recoger esta semana?** Corra la vista **"Rentas por vencer (7 días)"** (o mire "Necesita tu atención"):
  salen las abiertas que se recogen de hoy a 7 días, la más próxima arriba.
- **Una renta sale como vencida y el equipo ya volvió**: falta registrar su devolución (sección 5). Si el cliente lo sigue usando, extienda
  la renta (sección 3): con la nueva fecha deja de estar vencida.
- **Una renta Programada sale como vencida**: el recogido ya pasó y nunca se despachó; despáchela y extiéndala, o cancélela si ya no va.
- **¿Cuántos equipos tengo en cada cliente hoy?** Vista **"Equipos en renta por cliente"** (suma de equipos en el cliente por cliente,
  con total).
- **¿Por qué se devuelven los equipos antes de tiempo?** Vista o gráfico **"Devoluciones de renta por motivo"**; filtre "Anticipada" para
  ver solo las anticipadas, o agrupe por "Condición" para ver cuántas volvieron dañadas.
- **¿Qué equipos llevan más días en revisión?** Vista **"Equipos en proceso"** (columna "Días en proceso"; los más viejos arriba).
- **No veo los reportes de rentas**: revise que el módulo **Rentas** esté encendido y que su rol tenga `rental.view` (y `analytics.view`).
- **Quiero ver las rentas vencidas en mi Pulso**: encienda "mostrar en Pulso" en el indicador "Rentas vencidas" (viene apagado porque el
  aviso de "Necesita tu atención" ya las muestra).
