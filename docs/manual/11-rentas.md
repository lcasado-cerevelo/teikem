# 11 — Rentas (submódulo de Almacén)

Rentas permite **rentar equipos propios con número de serie** (camas de hospital, concentradores de oxígeno, sillas de ruedas…) a una
**localidad de un cliente**, con su contrato, su fecha de recogido (fin de la renta) y sus condiciones de cobro. Este capítulo cubre el
**bloque R1 (Lote 27)**: la renta desde que se crea hasta que se despacha, las **extensiones** de la fecha de recogido y la
**cancelación**. La **devolución** del equipo (con motivo y proceso de inspección, limpieza, reparación…) llega con el bloque R2; los
reportes e indicadores, con el R3. Todo lo de este capítulo es del servidor (API); las pantallas llegan con la web de Rentas (F-R1).

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

## 1. Quién puede y dónde

| Acción | Permiso | Endpoint |
|---|---|---|
| Ver la lista, la ficha y la bitácora de extensiones | `rental.view` | `GET /api/v1/rentals`, `GET /api/v1/rentals/{publicId}`, `GET /api/v1/rentals/{publicId}/extensions` |
| Crear, editar, agregar o quitar equipos, poner la tarifa de un equipo, programar, despachar y cancelar | `rental.manage` | `POST /api/v1/rentals`, `PATCH /api/v1/rentals/{publicId}`, `POST /api/v1/rentals/{publicId}/lines`, `DELETE /api/v1/rentals/{publicId}/lines/{lineId}`, `PUT /api/v1/rentals/{publicId}/lines/{lineId}/rate`, `POST …/schedule`, `POST …/dispatch`, `POST …/cancel` |
| Extender la fecha de recogido | `rental.extend` (nuevo) | `POST /api/v1/rentals/{publicId}/extensions` |
| Registrar devoluciones (bloque R2) | `rental.return` (nuevo; sembrado desde ya) | — |
| Historial de estatus de la renta | `rental.view` | `GET /api/v1/status/history/RENTAL/{id}` |

- **Módulo**: `RENTAL_EQUIPMENT`, que ahora se llama **"Rentas"**, está en la categoría **Almacén** y **depende de `WMS_LOTSERIAL`**
  (Inventario y trazabilidad): no se enciende sin él y **apagar `WMS_LOTSERIAL` apaga Rentas** (y su dependiente "Facturación de
  alquiler"). Con el módulo apagado los endpoints responden 403 `module_disabled`.
- **Plantillas de rol**: el **Admin de compañía** tiene todo; el **Operador de almacén** tiene `rental.view`, `rental.manage`,
  `rental.maintenance` y ahora también **`rental.extend`** y **`rental.return`**. Al actualizar la plataforma, todo rol de compañía que
  ya gestionaba rentas (`rental.manage`) recibe una sola vez `rental.extend` y `rental.return`; a los demás se les dan desde Roles.
- La compañía sale de la sesión: una renta, un cliente, una localidad, un almacén o un producto de otra compañía responden **404**.
- Todo cambio de la renta, sus equipos, tarifas y extensiones queda en la **auditoría** (tipo `RENTAL`); los cambios de estatus, en el
  historial de estatus.

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

## 5. Estatus y transiciones

| Estatus | Código | Tipo | Qué permite |
|---|---|---|---|
| Borrador | `DRAFT` | inicial | editar, equipos, tarifas, programar, cancelar |
| Programada | `SCHEDULED` | etapa | editar, equipos (reservan/liberan), tarifas, despachar, extender, cancelar |
| En renta | `ON_RENT` | etapa | extender; devolver (R2) |
| Devuelta | `RETURNED` | terminal | solo consulta (R2) |
| Cancelada | `CANCELLED` | terminal | solo consulta |

| De → a | Quién | Efectos | Qué lo bloquea |
|---|---|---|---|
| (nueva) → Borrador | `rental.manage` | número REN-#####; equipos sin reservar | cliente dado de baja, localidad ajena o dada de baja, serie no disponible o en otra renta |
| Borrador → Programada | `rental.manage` | reserva las series (serie Reservada; disponible baja) | sin equipos, cliente dado de baja, serie no disponible |
| Programada → En renta | `rental.manage` | transferencia a EN-RENTA, reservada en el destino; serie En renta; fecha de despacho | cliente dado de baja; reserva perdida; posición EN-RENTA desactivada; estatus de serie `ON_RENT` desactivado por la compañía |
| Borrador / Programada → Cancelada | `rental.manage` | libera reservas; equipos inactivos; fecha de cierre | despachada (también lo impide el sistema aunque se cambie la regla) |
| En renta → Devuelta | `rental.return` (R2) | — | — |

Estatus de la **serie** agregados por Rentas (laterales, del sistema): **En renta** (`ON_RENT`) y **En proceso** (`IN_PROCESS`, para el
proceso del equipo devuelto de R2). Ambos cuentan como "en inventario" en el conteo cíclico y en el indicador "series por capturar".

La compañía puede **renombrar** y **reordenar** los estatus de la renta, pero **no** debe desactivarlos: si desactiva "Programada", "En
renta" (de la renta o de la serie) o "Cancelada", la acción correspondiente responde 422 `El estatus '{código}' no existe o no está
habilitado para esta compañía.`

## 6. Mensajes (servidor)

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

Mensajes del inventario que se ven al intentar mover una serie rentada (por las pantallas normales):

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Transferir o ajustar una serie En renta | `La serie {s} no está disponible en EN-RENTA.` | 409 |
| Recolectar una serie En renta | `La serie {s} no está disponible en el almacén (no existe, ya salió o está en otra posición o lote).` | 409 |
| Recibir de nuevo (devolución normal) una serie En renta | `La serie {s} ya está en inventario.` | 409 |

## 7. Kárdex, auditoría y datos

- Kárdex: el despacho son **transferencias** con origen la posición del equipo y destino **EN-RENTA**, referencia **"Renta REN-#####"**
  (en inglés "Rental REN-#####"); filtrar por `refEntity=RENTAL&refId={id}`. El detalle del movimiento muestra la renta (número, estatus,
  cliente y número de contrato). Las devoluciones (R2) aparecerán como "Devolución de renta DRN-#####" y el proceso como "Proceso #id".
- Saldos: la posición EN-RENTA muestra lo rentado **en mano y reservado**. El producto: en mano igual, disponible sin lo rentado.
- Series: `GET /api/v1/products/{publicId}/serials` muestra las rentadas con estatus "En renta" en EN-RENTA.
- Auditoría: `GET /api/v1/audit/changes?entityType=RENTAL&entityId={id}`.

## 8. Casos frecuentes

- **Quiero rentar un equipo de Depot y no me deja**: el producto no se controla por serie (400). Conviértalo con **Convertir a serie**
  (capítulo 06 §2.1) capturando las series de cada unidad, y agréguelo por su serie.
- **Programé una renta y el producto aparece como "No disponible"**: es lo esperado: las series quedan reservadas para la renta.
- **Despaché y el inventario no bajó**: correcto (D1): el equipo sigue siendo nuestro; está en la posición EN-RENTA, en mano y reservado.
- **El cliente pidió más tiempo**: use **Extender** (`rental.extend`) con la nueva fecha y el motivo; si cambia la tarifa, indíquela.
- **Me equivoqué de equipo antes de despachar**: quítelo (DELETE de la línea) y agregue el correcto; en Programada la reserva se libera y
  se toma la nueva.
- **El cliente desistió antes de la entrega**: cancele la renta (solo Borrador o Programada).
- **El equipo se dañó en el cliente / la renta terminó**: se registra con la **devolución de renta** (bloque R2); por ahora la renta
  queda En renta.
