# 14 — Avisos (notificaciones)

Lote 51 (P2.1, 2026-10-14). Los **avisos** le dicen a las personas correctas que pasó algo («llegó un recibo con diferencia», «se
reportó un daño»…) por el canal que cada compañía elija. Este capítulo describe el **motor**: reglas, canales, cola, registro y la
bandeja de cada usuario. La pantalla para armar las reglas y la campanita de la web llegan en el Lote 52; los avisos de cada módulo
(recibos, daños, conteos…) se conectan en lotes aparte, uno por módulo. Hoy existe un **evento de prueba** para probarlo todo.

## 1. Cómo funciona

1. Un módulo **anota** el aviso en una cola **dentro de la misma operación** (la misma transacción). Si la operación se revierte, no
   se avisa nada; si el aviso falla después, la operación **nunca** se deshace.
2. Un servicio de fondo (cada 10 segundos) toma lo pendiente, busca las **reglas activas** de esa compañía para ese evento, arma los
   destinatarios y el texto de cada canal y lo envía.
3. Cada envío queda en el **registro** (evento × regla × canal × destinatario). Nunca se avisa dos veces lo mismo: un reintento solo
   repite lo que falló.

## 2. Permisos y dónde

- **Reglas, envío de prueba y registro**: permiso **`notifications.manage`** («Gestionar las reglas de aviso»; lo trae el rol **Admin de
  compañía** y se agregó a los roles de administrador que ya existían). Módulo **Sistema** (siempre encendido; los avisos no son un módulo).
- **Bandeja** (la campanita): sin permiso especial; cada usuario ve y toca **solo la suya**.
- API: `/api/v1/notifications/*` (eventos, canales, reglas, prueba, registro) y `/api/v1/inbox/*` (bandeja). No existe DELETE: una regla se apaga.

## 3. Los eventos

Cada módulo define los suyos en código (con sus variables y textos por omisión). Hoy: **`TEST` — Aviso de prueba**, con las variables
`{message}` (mensaje) y `{user}` (quién lo disparó). Se dispara con `POST /api/v1/notifications/test-event` y lo reciben las reglas
**activas** de ese evento. Un evento **opcional** puede silenciarse en la bandeja de cada usuario.

### 3.1 Eventos de las órdenes de venta (Lote 66)

Módulo **Órdenes de venta** (los eventos existen siempre; solo se disparan si la compañía usa el módulo). **No viene ninguna regla armada**: el administrador
las crea en Sistema → Reglas de aviso, eligiendo a quién y por qué canal. Todos se pueden silenciar en la bandeja de cada usuario **salvo el de crédito**.

| Evento (código) | Cuándo se dispara | Variables |
|---|---|---|
| **Orden de venta solicitada** (`SALES_ORDER_REQUESTED`) | Al solicitar una orden (validada y reservada). | `{orderNumber}` `{client}` `{warehouse}` `{lineCount}` `{detail}` (si quedó algo en backorder); desde el Lote 243 también `{orderDate}` `{seller}` `{total}` `{deliveryMode}` `{paymentMethod}` `{orderLink}` `{orderPublicId}` y el bloque `{orderDetail}` ([§3.1 bis](#31-bis-el-correo-con-la-orden-de-venta-completa-lote-243-2026-11-25)) |
| **Orden de venta despachada** (`SALES_ORDER_SHIPPED`) | Cuando la orden queda despachada **por completo** (un despacho parcial no avisa). | `{orderNumber}` `{client}` `{warehouse}` `{shipped}` (unidades) |
| **Orden de venta cancelada** (`SALES_ORDER_CANCELLED`) | Al cancelarla, y también cuando se cancela sola porque todas sus líneas se cerraron con faltante sin despachar nada. | `{orderNumber}` `{client}` `{warehouse}` `{reason}` |
| **Orden de venta necesita autorizar crédito** (`SALES_CREDIT_OVERRIDE_NEEDED`) | Cuando alguien intenta solicitar una orden que excede el crédito del cliente y no la autoriza. Una vez por situación (el mismo intento repetido no avisa otra vez; si cambia el límite, lo en curso o el monto, sí). | `{orderNumber}` `{client}` `{warehouse}` `{user}` `{limit}` `{exposure}` `{amount}` |
| **Backorder que ya se puede surtir** (`SALES_BACKORDER_AVAILABLE`) | Cuando hay inventario disponible para vender que alcanza para surtir líneas con backorder. Una vez por orden y situación (si entra más, o cambia lo surtible, vuelve a avisar). | `{orderNumber}` `{client}` `{warehouse}` `{lines}` (p. ej. «MED × 3, VIT × 2») `{lineCount}` |

Los cuatro primeros salen **en la misma operación** que los causa (si la operación se revierte, no se avisa). El de backorder lo detecta un trabajo programado
(`SALES_BACKORDER_SCAN`, [capítulo 13](13-trabajos-programados.md)) cada 10 minutos: cubre **cualquier forma** en que entra inventario (recibo, ajuste,
transferencia, liberar cuarentena, cancelar otra orden), sin tocar esos módulos.

### 3.1 bis El correo con la orden de venta completa (Lote 243, 2026-11-25)

**Qué hace.** Al solicitar una orden, el aviso «Orden de venta solicitada» puede mandar por correo la orden completa: encabezado (compañía, número y «URGENTE»), fecha, vendedor, cliente con dirección y teléfono, entrega o recogido, forma de pago, tabla de líneas (con las piezas de cada kit), subtotal, envío, un renglón por componente del IVU (estatal, municipal), total, quién autorizó y la firma como archivo adjunto. Hay dos versiones: la del **personal** y la **copia al cliente**. Se ven en [L243 — pantallas](frontend/l243-correo-orden-venta.md).

**Quién puede.**

| Acción | Permiso | Módulo |
|---|---|---|
| Crear y editar la regla, ver el Registro, «Vista previa» y «Enviar prueba» | `notifications.manage` | Sistema (siempre encendido) |
| «Vista previa» o «Enviar prueba» **con el número de una orden real** | `notifications.manage` **y** `sales.view` | además **Órdenes de venta** (`SALES_ORDERS`) encendido |
| Ver la dirección, el teléfono y el registro de comerciante del cliente en la vista previa | además `clients.read` (sin él esos datos no salen) | — |
| `GET /api/v1/sales-orders/{id}/email-preview` (desde la orden) | `sales.view` | Órdenes de venta |

**Cómo se usa.**

1. Sistema → **Reglas de aviso** → nueva regla del evento «Orden de venta solicitada». La regla nueva ya trae:
   - el destinatario **«Vendedor de la orden»** (tipo «Del evento») puesto;
   - en el correo, el asunto `Orden de venta {orderNumber} — {client}` y el texto con la variable bloque `{orderDetail}`.
   Los demás canales (campanita, SMS, notificación push) traen el texto de siempre.
2. Se puede agregar **«Cliente de la orden»** (copia al cliente; no viene puesto) y cualquier otro destinatario (rol, usuario, correo suelto).
3. Encender el canal **EMAIL** y activar la regla. La regla nace **apagada**; sin regla activa no sale nada.
4. **«Vista previa»** (botón de la ventana de la regla): muestra asunto, HTML y adjuntos tal como llegarían, sin enviar ni escribir en el Registro. Se escoge idioma (Español, Inglés) y para quién (Personal, Cliente). Sin número de orden usa una orden de ejemplo («Farmacia Central», con kit, IVU estatal y municipal y firma); con el número usa los datos reales. Usa el texto que está en pantalla aunque no se haya guardado.
5. Cada vez que se solicita una orden, el correo sale una sola vez. El resultado queda en Reglas de aviso → **Registro**.

Ejemplos de API:

```
POST /api/v1/notifications/rules
{ "eventCode": "SALES_ORDER_REQUESTED", "name": "Correo con la orden",
  "channels": [ { "channel": "EMAIL", "isEnabled": true } ],
  "recipients": [ { "kind": "CONTEXT", "value": "SELLER" }, { "kind": "CONTEXT", "value": "CLIENT" } ],
  "isActive": true }

POST /api/v1/notifications/preview
{ "eventCode": "SALES_ORDER_REQUESTED", "channel": "EMAIL", "lang": "es", "audience": "CUSTOMER", "reference": "OV-00012" }

GET /api/v1/sales-orders/{publicId}/email-preview?audience=STAFF&lang=en
```

La vista previa devuelve `channel`, `subject`, `bodyText`, `bodyHtml`, `attachments` (nombre, tipo y tamaño, sin el contenido), `usedSample` (se armó con el ejemplo) y `templateSource` (`REQUEST` = el texto mandado, `RULE` = la regla activa, `DEFAULT` = la plantilla por omisión del evento). «Enviar prueba» (`POST /api/v1/notifications/rules/{id}/test`) acepta además `reference` y manda el correo **a quien lo pide**, no al cliente.

**Campos y reglas.**

| Campo | Regla |
|---|---|
| Destinatario tipo `CONTEXT` (`value`) | Una clave que el evento declara: `SELLER` (Vendedor de la orden) o `CLIENT` (Cliente de la orden). Se guarda como la declara el evento. |
| Variable bloque `{orderDetail}` | Solo puede ir en el **texto**, nunca en el asunto. En el correo sale como la orden completa; en la campanita y el SMS, como una línea («3 líneas · Total $123.45 · Entrega · Crédito 30 días»). |
| `audience` de la vista previa | `STAFF` (por omisión) o `CUSTOMER`. |
| `lang` de la vista previa | `es` o `en`; sin él, el idioma de la compañía. |
| `reference` | Número (`OV-00012`) o identificador de una orden de la compañía. |

Variables nuevas del evento (además de `{orderNumber}` `{client}` `{warehouse}` `{lineCount}` `{detail}`): `{orderDate}` fecha de la orden, `{seller}` vendedor, `{total}`, `{deliveryMode}` (Entrega o Recogido), `{paymentMethod}` (p. ej. «Crédito 30 días»), `{orderLink}` (vacío en la copia al cliente o si no hay dirección web configurada), `{orderPublicId}` y el bloque `{orderDetail}`.

**Quién recibe cada destinatario del evento.**

| Destinatario | Recibe | Versión |
|---|---|---|
| **Vendedor de la orden** (`SELLER`) | Quien solicitó la orden y el usuario del representante de ventas del cliente, si es otra persona. Solo usuarios **activos** y con **membresía activa** en la compañía. | Personal, en el idioma de la compañía |
| **Cliente de la orden** (`CLIENT`) | El correo principal del contacto principal del cliente (si no tiene, el correo registrado en el propio cliente) y cada **contacto activo marcado «Recibe avisos por correo»** con un correo válido. Sin repetir. Solo por correo o SMS. | Cliente, en el idioma de aviso del cliente (si no tiene, el de la compañía) |

**La copia al cliente no lleva:** el botón «Abrir la orden en Teikem», las notas, la marca «forma de pago cambiada», «Cliente nuevo por aprobar», el faltante en backorder por línea, cómo se reautenticó el vendedor ni el aviso de que la orden cambió.

**Validaciones** (se suman a las de la sección 7):

| Campo | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `recipients` | Tipo de destinatario válido | `Tipo de destinatario desconocido: 'X'. Use ROLE, USER, EMAIL, PHONE o CONTEXT.` | 400 |
| `recipients` | La clave `CONTEXT` la declara el evento | `Este evento no tiene el destinatario 'X'.` | 400 |
| `channels` | `{orderDetail}` no va en el asunto | `La variable {orderDetail} es un bloque y solo puede ir en el texto, no en el asunto.` | 400 |
| `reference` | El evento admite documento real | `Este evento no admite vista previa con un documento real.` | 400 |
| `reference` | La orden no está cancelada ni en borrador | `La orden OV-00012 se canceló antes de enviar el aviso.` / `La orden OV-00012 sigue en borrador.` | 400 |
| `audience` | `STAFF` o `CUSTOMER` | `Destinatario de la vista previa desconocido: 'X'. Use STAFF o CUSTOMER.` | 400 |
| `lang` | `es` o `en` | `Idioma no válido: 'X'. Use es o en.` | 400 |
| `eventCode` | Obligatorio y existente | `Indique el evento de la regla.` / `Evento de aviso desconocido: 'X'.` | 400 |
| `channel` | Obligatorio y existente | `Indique el canal.` / `Canal desconocido: 'X'. Use EMAIL, SMS, APP o PUSH.` | 400 |
| Plantilla | Solo variables del evento | `La plantilla usa una variable que el evento no tiene: {zzz}.` | 400 |
| Plantilla | Asunto ≤ 200 y texto ≤ 1000 caracteres | `El asunto del canal EMAIL no puede pasar de 200 caracteres.` / `El texto del canal EMAIL no puede pasar de 1000 caracteres.` | 400 |
| Orden inexistente, de otra compañía o fuera del alcance de quien consulta | — | `Orden de venta no encontrada.` / `Orden de venta 'OV-X' no encontrada.` | 404 |
| Sin el permiso del documento | `sales.view` | `Falta el permiso 'sales.view'.` | 403 |
| Módulo apagado | `SALES_ORDERS` | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 |

**Estatus: de qué depende que el correo salga.** El correo se arma **al enviar** (hasta unos segundos después de solicitar), con los datos que tenga la orden en ese momento.

| Estatus de la orden al enviar | Qué pasa | Qué queda en el Registro |
|---|---|---|
| Solicitada u otro estatus posterior | Sale el correo con los datos vigentes. Si el total o las líneas cambiaron desde que se solicitó, la versión del personal agrega «Esta orden cambió después de solicitarse; el correo muestra los datos vigentes.» | `SENT` |
| Cancelada | No sale | `SKIPPED`: `La orden OV-00012 se canceló antes de enviar el aviso.` |
| Borrador | No sale | `SKIPPED`: `La orden OV-00012 sigue en borrador.` |
| La orden ya no existe | Sale con el texto y las variables que trae el aviso, sin la tabla | `SENT` |

Estatus del aviso: el correo sale **una sola vez por orden** (clave `REQ-{guid}`); solicitar de nuevo la misma orden no manda otro. Si el proveedor de contenido falla, el aviso se reintenta a 1, 5 y 30 minutos como cualquier otro. Los destinatarios que no se pueden alcanzar quedan `SKIPPED` sin detener a los demás:

| Qué falta | Fila del Registro (destinatario) | Motivo |
|---|---|---|
| La orden no tiene un vendedor con usuario activo | `(Vendedor de la orden)` | `La orden no tiene un vendedor con usuario activo en la compañía.` |
| El cliente no tiene correo | `(Cliente de la orden)` | `El cliente no tiene correo principal ni contactos marcados con correo.` |
| Destinatario del evento sin correo / sin teléfono / sin usuario | `(Vendedor de la orden)` o `(Cliente de la orden)` | `«Vendedor de la orden» no tiene correo.` / `… no tiene teléfono.` / `… no tiene usuario en Teikem.` |

**Qué queda bloqueado o limitado.** Nada se bloquea en la orden: un aviso nunca deshace ni detiene la operación. Límites: la tabla muestra todas las líneas, sin tope (Gmail puede recortar por su cuenta un correo de más de unos 102 KB y ofrece «Ver mensaje completo»); los adjuntos suman como máximo 5 MB (la firma que no cabe o no se puede leer no se adjunta y el correo sale igual); el adjunto solo va si el texto usa `{orderDetail}`.

**Preguntas frecuentes del correo de la orden**

- *¿Por qué no llegó el correo?* Revise el Registro (Reglas de aviso → Registro): el motivo está en cada fila. Las causas más comunes: la regla está apagada, el canal EMAIL no está encendido en la regla, el destinatario no tiene correo, el vendedor está inactivo, o la orden se canceló antes de enviarse.
- *¿El cliente recibe el botón para abrir la orden?* No: la copia al cliente nunca lleva el enlace.
- *¿Cómo hago que un contacto del cliente reciba la copia?* En la ficha del cliente → Personas de contacto, marque «Recibe avisos por correo» y póngale un correo ([02 §1.5](02-clientes-y-contratos.md#15-personas-de-contacto)). Y la regla debe tener «Cliente de la orden».
- *¿Se adjunta la orden en PDF?* No. El correo trae la orden en el cuerpo y, si hay, la firma como imagen adjunta.
- *En TEST el correo llegó a otra dirección.* Es por diseño: ver la FAQ («En TEST los correos se desvían a teikem@cerevelo.com»).

### 3.2 Eventos de inventario (Lote 141, 2026-10-20)

Módulo **Inventario por lote y serie** (`WMS_LOTSERIAL`). Los publica el trabajo programado `INVENTORY_ALERT_SCAN` ([capítulo 13](13-trabajos-programados.md)), **todos los
días a las 6:00** (hora de la compañía; «Ejecutar ahora» lo repite). **No viene ninguna regla armada**: el administrador las crea en Sistema → Reglas de aviso (a quién y por qué
canal). Todos se pueden silenciar en la bandeja de cada usuario. Cada evento sale como **un solo aviso con el conjunto completo** (no uno por lote); las variables `{count}` (cuántos)
e `{items}` (un renglón por cada uno, hasta 10 y «… +N») están en todos.

| Evento (código) | Qué avisa | Variables extra y formato de `{items}` |
|---|---|---|
| **Lotes por vencer** (`INVENTORY_LOT_EXPIRING`) | Lotes **con existencia** que vencen hoy o dentro de los próximos N días (N = ajuste de la compañía, 30 por omisión). | `{days}`; `producto · lote · vencimiento · cantidad · almacén` |
| **Lotes vencidos con existencia** (`INVENTORY_LOT_EXPIRED`) | Lotes cuyo vencimiento es **anterior a hoy** y todavía tienen existencia. | `producto · lote · vencimiento · cantidad · almacén` |
| **Productos bajo el mínimo** (`INVENTORY_STOCK_LOW`) | Productos **activos** con mínimo (`minQty`) cuyo disponible total (en mano − reservado, todos los almacenes) es menor que el mínimo. | `producto · disponible · mínimo` |
| **Productos sin existencia** (`INVENTORY_STOCK_OUT`) | Productos activos que **sí tuvieron movimientos** y hoy no tienen nada en mano (un producto recién creado que nunca se recibió no cuenta). | `producto` |
| **Productos sin movimiento** (`INVENTORY_NO_MOVEMENT`) | Productos activos **con existencia** cuyo último movimiento del Kárdex tiene N días o más (N = ajuste, 90 por omisión). | `{days}`; `producto · días · en mano` |
| **Productos con existencia sin costo** (`INVENTORY_NO_COST`) | Productos activos **con existencia** y sin costo de compra (vacío o cero): sin costo no se puede valorar el inventario. | `producto · en mano` |

**No se repite lo mismo:** la clave de no repetición es *evento + semana + huella del conjunto*. Si el conjunto no cambió en la semana, no vuelve a avisar; si entra un lote nuevo al
plazo o otro producto baja del mínimo, sale un aviso nuevo con el conjunto completo; y mientras el problema siga vivo hay un **recordatorio semanal**.

Ajustes (web: **Ajustes de la compañía → Operación → «Avisos de inventario»**, `admin.tenant`; API: `PUT /api/v1/tenant/settings`):
`inventoryExpiryWarnDays` (1 a 365, 30) e `inventoryNoMovementDays` (1 a 3650, 90).

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `inventoryExpiryWarnDays` fuera de 1 a 365 | `Los días de aviso de vencimiento deben estar entre 1 y 365.` | 400 (campo `inventoryExpiryWarnDays`) |
| `inventoryNoMovementDays` fuera de 1 a 3650 | `Los días sin movimiento deben estar entre 1 y 3650.` | 400 (campo `inventoryNoMovementDays`) |

**Agregar otro aviso de inventario más adelante** (p. ej. exceso de inventario, conteo con diferencias): un código en `InventoryEventCodes`, su definición en `InventoryEventProvider` y su
detección en `InventoryAlertService`; el motor de avisos, las reglas y la bandeja no cambian.

### 3.2 bis Eventos de flota (Lote 168, 2026-10-21)

Módulo **Catálogos** (donde viven vehículos, choferes y mantenimiento). Los publica el trabajo programado `FLEET_ALERT_SCAN` ([capítulo 13](13-trabajos-programados.md)), **todos los días a las 6:15** (hora de la compañía; «Ejecutar ahora» lo repite). Igual que los de inventario: **no viene ninguna regla armada** (el administrador las crea en Sistema → Reglas de aviso), todos se pueden silenciar en la bandeja, y cada evento sale como **un solo aviso con el conjunto completo** (`{count}` e `{items}`, hasta 10 renglones y «… +N»), sin repetirse en la misma semana mientras el conjunto no cambie (con recordatorio semanal si el problema sigue vivo).

| Evento (código) | Qué avisa | Variables extra y formato de `{items}` |
|---|---|---|
| **Documentos de la flota por vencer** (`FLEET_DOC_EXPIRING`) | Documentos de **vehículos** (registro, seguro, inspección, permiso) y de **choferes** (licencias y certificaciones) que vencen hoy o dentro de los próximos N días (N = ajuste de la compañía, 30 por omisión). | `{days}`; `V código · documento · vencimiento · días que faltan` (vehículo) o `C código · …` (chofer) |
| **Documentos de la flota vencidos** (`FLEET_DOC_EXPIRED`) | Documentos cuyo vencimiento es **anterior a hoy**. | `V/C código · documento · vencimiento · días vencido` |
| **Mantenimiento próximo** (`FLEET_MAINTENANCE_DUE_SOON`) | Programas de mantenimiento preventivo «Por vencer» (queda el 10 % o menos del intervalo). | `vehículo · programa · faltan N km · faltan N días` (solo lo que el programa usa) |
| **Mantenimiento vencido** (`FLEET_MAINTENANCE_OVERDUE`) | Programas «Vencidos» (lo restante es 0 o negativo). | `vehículo · programa · pasado por N km · pasado por N días` |
| **Órdenes de trabajo abiertas demasiado tiempo** (`FLEET_WORKORDER_STALE`, Lote 179) | Órdenes de mantenimiento en OPEN o IN_PROGRESS creadas hace **más de 14 días**. | `orden · vehículo · abierta hace N días` |
| **Combustible fuera de rango** (`FLEET_FUEL_OUT_OF_RANGE`, Lote 179) | Vehículos cuya **última carga** (de los últimos 30 días) rinde un km/L que se aparta **más de 30 %** del promedio de las 5 cargas anteriores con rendimiento (se necesitan al menos 3). | `vehículo · km/L · promedio · diferencia %` |
| **Vehículos sin viaje** (`FLEET_VEHICLE_IDLE`, Lote 179) | Vehículos **activos** sin ningún viaje (no cancelado) en **más de 14 días**; los que nunca han tenido viaje cuentan desde su alta. | `vehículo · sin viaje desde fecha (o nunca ha tenido viaje) · días` |
| **Choferes sin viaje** (`FLEET_DRIVER_IDLE`, Lote 179) | Lo mismo para choferes activos. | `código nombre · sin viaje desde fecha · días` |

**Umbrales de los avisos nuevos (Lote 179).** Son fijos por ahora —14 días de orden abierta, 14 días sin viaje, 30 % de desviación de combustible, historia de 3 a 5 cargas, ventana de 30 días—; no se ajustan por compañía todavía (los días de los documentos sí, arriba). Igual que los demás: un aviso por evento con el conjunto, que no se repite si el conjunto es el mismo en la semana, opcionales (se pueden silenciar) y sin reglas sembradas: el administrador arma a quién le llegan en Sistema → Reglas de aviso.

**Qué cuenta:** solo vehículos y choferes **activos y no dados de baja**; de cada tipo de documento solo el **vigente** (si ya se renovó, el viejo no avisa); un documento **sin fecha de vencimiento no avisa**. El mantenimiento usa exactamente el mismo cálculo del panel «Mantenimiento preventivo» (Flota → Mantenimiento).

Ajuste (web: **Ajustes de la compañía → Operación → «Avisos de flota»**, `admin.tenant`; API: `PUT /api/v1/tenant/settings`): `fleetDocWarnDays` (1 a 365, 30).

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `fleetDocWarnDays` fuera de 1 a 365 | `Los días de aviso de documentos de la flota deben estar entre 1 y 365.` | 400 (campo `fleetDocWarnDays`) |

Los avisos enlazan a Flota → **Documentos por vencer** o **Mantenimiento**. Más avisos de flota (orden de trabajo abierta demasiado tiempo, combustible fuera de rango, vehículo o chofer sin ruta N días) quedan para cuando haya datos reales (plan G2).

### 3.3 Eventos del aviso de salida (Lote 143)

Módulo **Inventario por lote y serie**. Todos opcionales (se silencian en la bandeja); **no viene ninguna regla armada**. Variables comunes: `{number}` `{client}` `{warehouse}`. En los avisos de llegada `{number}` es el número del aviso (`LLE-00012`, desde el Lote 170; antes salía `#12`).

| Evento (código) | Cuándo se dispara | Variables extra |
|---|---|---|
| **Aviso de salida solicitado** (`EXIT_NOTICE_REQUESTED`) | Al crearse el aviso (en la misma operación). | `{lineCount}` `{detail}` (si quedó pendiente por falta de existencia) |
| **Pendiente de un aviso de salida ya reservado** (`EXIT_NOTICE_BACKORDER_RESERVED`) | Cuando el trabajo `EXIT_NOTICE_BACKORDER_SCAN` reserva pendiente. Una vez por situación. | `{lines}` `{units}` |
| **Aviso de salida despachado** (`EXIT_NOTICE_SHIPPED`) | Cuando queda despachado **por completo** (un despacho parcial no avisa). | `{shipped}` |
| **Aviso de salida cancelado** (`EXIT_NOTICE_CANCELLED`) | Al cancelarlo, y también cuando se cancela solo porque todas sus líneas se cerraron con faltante sin despachar nada. | `{reason}` |
| **Aviso de llegada solicitado** (`ASN_REQUESTED`) | Al crear un aviso de llegada de un cliente (por el personal o desde el portal). | `{number}`, `{reference}`, `{client}`, `{warehouse}`, `{expected}`, `{lineCount}` |
| **Aviso de llegada modificado** (`ASN_UPDATED`) | Al editar un aviso de llegada esperado (almacén, referencia, fecha o líneas). | las mismas |
| **Aviso de llegada recibido** (`ASN_RECEIVED`) | Al confirmar el recibo contra ese aviso. | `{number}`, `{reference}`, `{client}`, `{warehouse}`, `{expected}` |
| **Aviso de llegada cancelado** (`ASN_CANCELLED`) | Al cancelar el aviso. | las mismas |

## 4. Las reglas

Una regla = **un evento** + **un nombre** (único en la compañía, hasta 100 caracteres) + **canales** + **destinatarios**.

- **Canales**: `EMAIL`, `SMS`, `APP` (la campanita) y `PUSH`. Los cuatro existen siempre en la regla y **nacen apagados** con los textos por
  omisión del evento. Cada uno lleva **asunto y texto en español e inglés** con `{variables}` del evento (una variable que el evento no
  tiene se rechaza; una que falte al enviar se deja vacía).
- **Canales no configurados**: **SMS** y **PUSH** todavía no tienen proveedor en Teikem y **no se pueden encender**. **EMAIL** funciona con el
  correo que ya usa Teikem (Brevo); si ese servicio no está configurado en el servidor, tampoco se puede encender. Un canal **APP** siempre
  está disponible.
- **Destinatarios**: un **rol** de la compañía (le llega a todos sus usuarios activos), un **usuario** activo de la compañía, un **correo suelto**, un
  **teléfono suelto** (se guarda; sin SMS no se usa) o, desde el Lote 243, un destinatario **«del evento»** (`CONTEXT`: sale del propio aviso, como «Vendedor de la orden» o «Cliente de la orden»; ver [§3.1 bis](#31-bis-el-correo-con-la-orden-de-venta-completa-lote-243-2026-11-25)). Sin repetir: si alguien está por rol y por correo, recibe uno.
- **Activar** exige al menos un destinatario y un canal encendido.
- El texto se arma en el **idioma de la compañía** (con respaldo: español, inglés).
- **Enviar prueba** (`POST /rules/{id}/test`): manda el texto de un canal, con valores de ejemplo, **a quien la pide** (no queda en el registro).

## 5. El registro de envíos

`GET /api/v1/notifications/log` (los más recientes primero; filtros por evento, canal, estado y fechas). Estados:

| Estado | Significa |
|---|---|
| `SENT` | Salió. |
| `SKIPPED` | No correspondía o no se pudo, **sin ser una falla**: canal no configurado, el destinatario no tiene correo, o el usuario silenció el aviso. El motivo queda en el registro. |
| `FAILED` | Falló (p. ej. el servicio de correo estaba caído); se reintenta. |
| `PENDING` | Anotado, en proceso. |

**Reintentos**: si algún envío falla, el aviso se reintenta a **1, 5 y 30 minutos**; si el cuarto intento también falla, queda fallido. Una
falla de un destinatario no detiene a los demás, y el reintento repite **solo lo que falló**.

## 6. La bandeja del usuario

`GET /api/v1/inbox` (con `unreadOnly`), `GET /inbox/unread-count`, `POST /inbox/{id}/read`, `POST /inbox/read-all`, y las preferencias
`GET /inbox/prefs` y `PUT /inbox/prefs/{evento}` (silenciar un evento opcional; un aviso silenciado no llega a la bandeja y queda `SKIPPED` en el registro).

## 7. Validaciones y mensajes

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Regla inexistente (o de otra compañía) | `Regla de aviso no encontrada.` | 404 |
| Sin evento | `Indique el evento de la regla.` | 400 (`eventCode`) |
| Evento que no existe | `Evento de aviso desconocido: 'X'.` | 400 (`eventCode`) |
| Cambiar el evento de una regla | `El evento de una regla no se cambia; cree otra regla.` | 400 (`eventCode`) |
| Sin nombre | `Escriba el nombre de la regla.` | 400 (`name`) |
| Nombre de más de 100 caracteres | `El nombre de la regla no puede pasar de 100 caracteres.` | 400 (`name`) |
| Nombre repetido | `Ya existe una regla con ese nombre.` | 409 |
| Canal sin nombre / desconocido | `Indique el canal.` / `Canal desconocido: 'X'. Use EMAIL, SMS, APP o PUSH.` | 400 (`channel(s)`) |
| Encender un canal no configurado | `El canal SMS todavía no está configurado en Teikem.` | 400 (`channels`) |
| EMAIL o APP encendido sin asunto | `El canal EMAIL necesita un asunto.` | 400 (`channels`) |
| Canal encendido sin texto | `El canal APP necesita un texto.` | 400 (`channels`) |
| Asunto de más de 200 / texto de más de 1000 caracteres | `El asunto del canal EMAIL no puede pasar de 200 caracteres.` / `El texto del canal EMAIL no puede pasar de 1000 caracteres.` | 400 (`channels`) |
| Variable que el evento no tiene | `La plantilla usa una variable que el evento no tiene: {zzz}.` | 400 (`channels`) |
| Activar sin destinatarios | `La regla necesita al menos un destinatario para poder activarse.` | 400 (`recipients`) |
| Activar sin canal encendido | `La regla necesita al menos un canal encendido para poder activarse.` | 400 (`channels`) |
| Tipo de destinatario desconocido | `Tipo de destinatario desconocido: 'X'. Use ROLE, USER, EMAIL, PHONE o CONTEXT.` | 400 (`recipients`) |
| Correo / teléfono inválido | `Correo no válido: 'x'.` / `Teléfono no válido: 'x'.` | 400 (`recipients`) |
| Rol que no es de la compañía | `Rol no encontrado.` | 400 (`recipients`) |
| Usuario que no es de la compañía o está inactivo | `Usuario no encontrado en esta compañía.` | 400 (`recipients`) |
| La regla cambió desde que la abrió (rowVersion) | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Aviso ajeno o inexistente en la bandeja | `Aviso no encontrado.` | 404 |
| Silenciar un evento que no es opcional | `Ese aviso no se puede silenciar.` | 409 |
| Sin el permiso | «Sin permiso» | 403 |

## 8. Para soporte

- Tablas `NotificationRule`, `NotificationRuleChannel`, `NotificationRuleRecipient`, `NotificationOutbox` (cola), `NotificationLog`, `UserNotification`
  y `UserNotificationPref`; sección **2026-10-14 (g)** de `Diseño/logistica-db-update.sql`. Los eventos NO están en la base: viven en código.
- Configuración: `Notifications:Enabled` (por omisión `true`) y `Notifications:PollSeconds` (por omisión 10). Los comandos de consola no arrancan el servicio.
- Correo: usa la configuración de Brevo ya existente (`Brevo__ApiKey`, `Brevo__FromEmail`). Sin ella el canal EMAIL queda «no configurado»: no se enciende y,
  si una regla vieja lo tenía encendido, el registro dice `SKIPPED`.
- Un aviso que quedó `FAILED` en la cola (`NotificationOutbox.Status`) agotó sus reintentos; el error está en `LastError` y el detalle por destinatario en el registro.
- Correo en desarrollo (Lote 243): con `Email__Smtp__Host` (y `Email__Smtp__Port`, 1025 por omisión) el API manda el correo a un **buzón local** (Mailpit, página en http://localhost:8025) en vez de a Brevo. Solo funciona en ambiente Development; fuera de él se ignora y se usa Brevo. El canal EMAIL aparece como «local» (`isLocal`) y el correo nunca sale a la calle. El botón del correo usa `Notifications__WebBaseUrl` (o, si falta, `Auth__PasswordReset__WebBaseUrl`); sin ninguna no hay botón.
- Estructura del Lote 243: sección 2026-11-24 de `Diseño/logistica-db-update.sql` (CHECK `CK_NotificationRuleRecipient_Kind` con `CONTEXT` y la columna `ClientContact.ReceivesEmailNotifications`).
- Cada procesamiento corre **como la compañía, sin usuario** («Sistema»). Varias copias del sistema nunca procesan el mismo aviso a la vez.

## Kemi: «Pregúntame cómo usar el sistema» (Lotes 123–124)

Entre las opciones de Kemi (un clic en la mascota) está **«Pregúntame cómo usar el sistema»**. Kemi **busca**; no usa inteligencia artificial:

1. La persona escribe su pregunta con sus propias palabras (por ejemplo «cómo cambio mi contraseña» o «no me deja entrar») y pulsa **Preguntar**.
2. Kemi busca esas palabras en el **manual** y en la **FAQ** (sin acentos ni mayúsculas, singular o plural, y las formas del verbo juntas: «registro», «registrar»). Las palabras raras pesan más que las comunes, el título pesa más que el cuerpo y la forma de la pregunta decide dónde mirar primero («no me deja», «qué significa» → FAQ; «cómo se hace» → manual). No se dice de dónde salió cada opción.
3. Kemi muestra hasta **5 opciones**; al elegir una se ve su **respuesta** ahí mismo.
4. Kemi pregunta **«¿Te contesté?»**. **Sí** → agradece y deja hacer otra pregunta. **No** → **busca por otro lado**: trae otras opciones, sin repetir las ya mostradas.
5. Si no hay nada que se parezca lo bastante, Kemi lo dice («No encontré nada sobre eso. Prueba con otras palabras.») en vez de adivinar. **Cambiar la pregunta** vuelve a la caja; **Volver** regresa a las opciones de Kemi.

No requiere permiso ni módulo: la ve cualquier persona con Kemi despierta. El índice se baja **solo la primera vez que alguien pregunta** (es un archivo aparte, no pesa en el arranque de la web) y trabaja en el propio navegador: sin servicio, sin costo y sin enviar la pregunta a ninguna parte.

**De dónde sale lo que Kemi sabe:** de `docs/manual/*.md` (capítulos y la FAQ), convertidos a un índice con `npm run help:index` (lo corre `npm run check`); al escribir o corregir el manual o la FAQ el índice se regenera y Kemi lo sabe en cuanto se compile la web. El manual está solo en español. **En inglés** Kemi entiende las palabras comunes (order, invoice, password, delivery, driver…: se acompañan de su equivalente en español para buscar) y avisa que sus respuestas salen en español.

**La pantalla donde está la persona cuenta:** lo documentado de esa pantalla sube en las opciones (estando en Órdenes, una pregunta de «cancelar» trae primero lo de Órdenes; en Rentas, lo de Rentas; en Facturación, lo de Facturación…).

Los avisos de llegada nacidos de una orden de compra son internos y no publican estos eventos. Como los demás, son opcionales y sin reglas sembradas: se arman en Sistema → Reglas de aviso.
