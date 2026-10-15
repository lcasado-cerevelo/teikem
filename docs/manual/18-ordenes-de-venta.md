# 18 — Órdenes de venta

Lotes 57 a 61 (2026-10-15) — **S1: borrador, solicitar y reservar, backorders; S2: despachar**. Una **orden de venta** (OV-#####) es el documento que viaja de **Ventas a Almacén**: Ventas le pide al almacén que despache algo para un cliente. Hasta aquí se puede **armar la orden en borrador, solicitarla (validar y reservar inventario), modificarla y cancelarla**; ver, surtir y cerrar los **backorders**; **despachar contra la orden** (Lote 61); el retiro / entrega y deshacer, la web y la app llegan en los lotes siguientes.
El diseño completo está en `Diseño/logistica-funcionalidades-maestro.md`, sección 13C. La pantalla se describe en [L60 — Órdenes de venta](frontend/l60-ordenes-de-venta.md).

## 1. Quién puede

- Módulo **Órdenes de venta (`SALES_ORDERS`)**: viene **apagado**; se enciende por compañía (Sistema → Módulos). Con el módulo apagado todo el API responde 403.
- Permisos (categoría Órdenes):
  - **`sales.view`** — ver órdenes. Lo tienen el Admin de compañía, el rol **Ventas** y el Operador de almacén.
  - **`sales.create`** — crear y editar el borrador (y, en el lote siguiente, solicitar). Admin de compañía y rol Ventas.
  - **`sales.cancel`** — cancelar. Admin de compañía y rol Ventas.
  - **`sales.price`** — escribir un precio distinto del precio de venta del producto. **Solo el Admin de compañía**; el administrador se lo asigna a quien quiera.
  - **`sales.backorder`** — surtir y cerrar backorders (lotes siguientes). Admin de compañía y Operador de almacén.
- La plantilla de rol **Ventas** (`sales.view`, `sales.create`, `sales.cancel`, `inventory.view`, `clients.read`) la crea el sistema para las compañías nuevas; en las que ya existen el administrador crea el rol y le asigna esos permisos.

## 2. La orden

- **Número** `OV-#####`, único por compañía, consecutivo y sin saltos (un intento que falla no gasta número).
- **Cliente** (obligatorio): el comprador. **Consignatario** (opcional): solo si habrá entrega. **Almacén**: el que la atiende; si la compañía tiene un solo almacén activo se usa solo, si tiene varios hay que indicarlo.
- **Fecha de solicitud** (hoy, en la zona de la compañía), **fecha requerida** (opcional), **notas** (hasta 500 caracteres) y **quién la pidió**.
- **Líneas**: producto, cantidad y precio. Solo se venden **productos propios de la compañía** (no los de un cliente de paso), activos, **sin repetir** en la misma orden. Hasta 200 líneas.
- **Precio**: si no se escribe, se toma el **precio de venta del producto**. Escribir otro precio exige `sales.price` (si es igual al de lista no hace falta). Un producto **sin precio de venta** obliga a escribir el precio.
- **Impuesto**: con el módulo **Impuestos** encendido, cada línea calcula su impuesto con las reglas de la compañía (capítulo [17](17-impuestos.md)) y **guarda una copia**: la tasa usada, su porcentaje, el monto y si salió exenta (producto exento o cliente con exención vigente). El encabezado guarda el **subtotal** y el **impuesto**; el **total** es su suma. Sin el módulo Impuestos el impuesto es cero. Si se cambia el cliente del borrador, el impuesto se recalcula.
- **Estatus**: `DRAFT` (borrador) → `REQUESTED` (solicitada) → `PARTIAL` → `SHIPPED`; `CANCELLED`. En uso hasta ahora: borrador, solicitada y cancelada. La marca **con backorder** no es un estatus: la tiene la orden mientras alguna línea tenga cantidad pendiente.

## 3. Qué se puede hacer

| Acción | Cómo | Permiso |
|---|---|---|
| Ver la lista (más reciente primero; filtros: estatus, cliente, buscador por número / cliente / producto, solo con backorder, fechas) | `GET /api/v1/sales-orders` | `sales.view` |
| Ver una orden | `GET /api/v1/sales-orders/{publicId}` | `sales.view` |
| Crear el borrador | `POST /api/v1/sales-orders` | `sales.create` (+ `sales.price` si cambia un precio) |
| Editar el borrador (lo que no se manda no cambia; las **líneas, si se mandan, reemplazan** a las actuales; `clearConsignee` / `clearRequiredDate` quitan esos datos) | `PATCH /api/v1/sales-orders/{publicId}` | `sales.create` |
| Comprobar disponibilidad y crédito (no guarda) | `GET /api/v1/sales-orders/{publicId}/availability` | `sales.create` |
| Solicitar (validar y reservar) | `POST /api/v1/sales-orders/{publicId}/request` | `sales.create` |
| Modificar una línea solicitada | `PATCH /api/v1/sales-orders/{publicId}/lines/{lineId}` | `sales.create` |
| Ver backorders (más antiguo primero) | `GET /api/v1/sales-orders/backorders` | `sales.view` |
| Surtir una línea / surtir todo / cerrar con faltante | `POST …/lines/{lineId}/fulfill` · `POST /sales-orders/backorders/fulfill` · `POST …/lines/{lineId}/close` | `sales.backorder` |
| Despachar contra la orden (por partes; consume la reserva y baja el inventario) | `POST /api/v1/sales-orders/{publicId}/dispatch` | `warehouse.pick` |
| Cancelar un borrador o una solicitada (comentario opcional en el historial; libera lo reservado) | `POST /api/v1/sales-orders/{publicId}/cancel` | `sales.cancel` |

Las órdenes **nunca se borran**: se cancelan. Quedan en la bitácora (altas, cambios) y en el historial de estatus.

## 4. Validaciones y mensajes

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Sin cliente | `Escoja el cliente que compra.` | 400 (`clientPublicId`) |
| Cliente inexistente (o de otra compañía) | `Cliente no encontrado.` | 404 |
| Cliente inactivo | `El cliente está inactivo; no se le puede vender.` | 422 |
| Consignatario inexistente | `Consignatario no encontrado.` | 404 |
| Varios almacenes y no se indicó | `Indique el almacén: la compañía tiene más de uno.` | 400 (`warehousePublicId`) |
| Almacén inexistente / inactivo / la compañía no tiene ninguno | `Almacén no encontrado.` / `El almacén está inactivo.` / `La compañía no tiene almacenes activos.` | 404 / 422 / 422 |
| Sin líneas | `Agregue al menos un producto a la orden.` | 400 (`lines`) |
| Más de 200 líneas | `La orden admite como máximo 200 líneas.` | 400 |
| Línea sin producto | `Escoja el producto de cada línea.` | 400 |
| Producto inexistente | `Producto no encontrado.` | 404 |
| Producto inactivo | `Hay un producto inactivo en la orden.` | 422 |
| Producto de un cliente de paso | `Solo se pueden vender productos propios de la compañía.` | 422 |
| Producto repetido | `Un producto no puede repetirse en la orden.` | 400 |
| Cantidad vacía o cero | `La cantidad debe ser mayor que cero.` | 400 |
| Más de 3 decimales / demasiado grande | `La cantidad admite como máximo 3 decimales.` / `La cantidad excede el máximo permitido.` | 400 |
| Precio negativo o inválido / más de 4 decimales | `El precio no es válido.` / `El precio admite como máximo 4 decimales.` | 400 |
| Producto sin precio de venta | `El producto {SKU} no tiene precio de venta: escriba el precio.` | 400 |
| Precio distinto del de lista sin `sales.price` | `Cambiar el precio exige el permiso de precios (sales.price).` | 403 |
| Lote inexistente | `Lote no encontrado.` | 404 |
| Notas de más de 500 caracteres | `Las notas no pueden pasar de 500 caracteres.` | 400 |
| Orden inexistente (o de otra compañía) | `Orden de venta no encontrada.` | 404 |
| Editar una orden que ya no es borrador | `Solo se puede editar una orden en borrador.` | 422 |
| Cancelar una orden ya cancelada | `La orden ya está cancelada.` | 409 |
| Con Impuestos encendido y sin tasa por omisión / sin tasa vigente | `Escoja la tasa por omisión en Impuestos → Reglas.` / `No hay una tasa de impuesto vigente en esa fecha. Configure una en Impuestos → Tasas.` | 400 |
| Sin el permiso o con el módulo apagado | «Sin permiso» | 403 |

## 6. Solicitar la orden: validar y reservar (Lote 58)

`POST /api/v1/sales-orders/{publicId}/request` (permiso `sales.create`) pasa la orden de **borrador** a **solicitada** (`REQUESTED`): el almacén ya la ve y **el inventario queda reservado para ella**. La validación y la reserva ocurren **en la misma transacción, con el candado del inventario**: dos vendedores que piden lo mismo a la vez no pueden vender más de lo que hay (uno la obtiene, el otro recibe el aviso de faltante). Nunca se reserva más de lo que existe.

- **Disponible para vender** = lo que hay en mano menos lo ya reservado, en las posiciones **activas** del almacén de la orden (y del **lote**, si la línea pidió uno). **No cuenta** la cuarentena, el cruce de muelle ni los equipos en renta, ni otros almacenes. Es lo mismo que puede recolectar el almacén; difiere del «Disponible» de la tabla de inventario, que sí suma la cuarentena.
- **Qué lote y qué posición**: el sistema reserva por el mismo orden de rotación de la compañía (primero vence / primero entró) y guarda **exactamente** qué saldo reservó cada línea (`SalesOrderReservation`) para liberar justo eso después.
- **Comprobar antes**: `GET /sales-orders/{publicId}/availability` (no guarda nada) devuelve por línea **pedido / disponible / faltante** y el estado del **crédito**. La pantalla lo usa para mostrar el diálogo de faltante.
- **Si falta inventario**: la solicitud responde **409** con el mensaje por línea y **no reserva nada** (ni lo de las líneas que sí alcanzaban). Ventas decide: **pedir solo lo disponible** (editar la cantidad), **reservar lo disponible y dejar el resto en backorder** (`allowBackorder = true`) o **quitar la línea**. Con `allowBackorder` cada línea reserva lo que hay y lo pendiente queda en **backorder**; la orden se marca **con backorder** mientras alguna línea tenga pendiente.
- **Crédito**: se compara el **límite de crédito** del cliente con lo que ya tiene en curso (saldo pendiente de sus órdenes de transporte **más** lo solicitado y sin despachar de sus otras órdenes de venta) y esta orden. Si lo excede responde **422** `credit_exceeded` con el aviso y las cifras; con `overrideCredit = true` y el permiso `orders.credit_override` se autoriza: queda el comentario «Crédito excedido autorizado por …» en el historial de estatus y un evento en la bitácora de seguridad.
- Un producto **con número de serie** todavía no se vende con orden de venta (mensaje en la tabla).

## 7. Cancelar y modificar una orden solicitada (Lote 58)

- **Cancelar** (`POST /sales-orders/{publicId}/cancel`, permiso `sales.cancel`): una orden solicitada **libera exactamente lo que tenía reservado** y borra su backorder; el inventario vuelve a estar disponible para otras ventas. Una orden a la que ya se le despachó algo no se cancela.
- **Modificar una línea** (`PATCH /sales-orders/{publicId}/lines/{lineId}`, permiso `sales.create`; precio distinto del de lista, además `sales.price`): se puede cambiar la **cantidad** y el **precio** de una línea de una orden solicitada mientras **no se haya despachado nada de esa línea**.
  - **Subir** la cantidad reserva lo que haya; si no alcanza responde 409 (o, con `allowBackorder`, el resto queda en backorder).
  - **Bajar** la cantidad quita primero del backorder y luego libera reserva.
  - El impuesto y los totales de la orden se recalculan.

## 8. Mensajes nuevos (Lote 58)

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Solicitar o comprobar una orden que no está en borrador | `Solo se puede solicitar una orden en borrador.` | 422 |
| Falta inventario al solicitar | `No alcanza el inventario de {SKU}: pedido {n}, disponible {n}, faltante {n}.` | 409 (`insufficient_stock`, un mensaje por línea) |
| Crédito excedido | `El cliente excede su límite de crédito: límite {n}, en curso {n}, esta orden {n}.` | 422 (`credit_exceeded`) |
| Autorizar el crédito sin el permiso | `Falta el permiso 'orders.credit_override'.` | 403 |
| Modificar una línea de una orden que no está solicitada | `Solo se pueden modificar las líneas de una orden solicitada.` | 422 |
| Línea inexistente | `Línea no encontrada.` | 404 |
| Línea cerrada | `La línea está cerrada.` | 422 |
| A la línea ya se le despachó producto | `A esa línea ya se le despachó producto: no se puede modificar.` | 422 |
| Subir una cantidad que no alcanza | `No alcanza el inventario de {SKU} para subir la cantidad: se agregan {n}, disponible {n}, faltante {n}.` | 409 |
| Cantidad o precio inválidos al modificar | `La cantidad debe ser mayor que cero.` / `El precio no es válido.` | 400 |
| Precio distinto del de lista sin `sales.price` | `Cambiar el precio exige el permiso de precios (sales.price).` | 403 |
| Producto con serie en una orden de venta | `Los productos con número de serie todavía no se venden con orden de venta.` | 422 |
| Cancelar una orden con algo despachado | `Solo se puede cancelar una orden en borrador o solicitada que no se haya despachado.` | 422 |

## 9. Backorders: ver, surtir y cerrar con faltante (Lote 59)

Un **backorder** es lo que se pidió y todavía no hay: la línea queda con su cantidad pendiente (`QtyBackordered`) mientras la orden sigue solicitada.

- **Ver los backorders** (`GET /api/v1/sales-orders/backorders`, permiso `sales.view`): todas las líneas con pendiente, **la más antigua primero** (el primero que pidió, primero). Por línea: orden, cliente, almacén, producto, **pedido / reservado / pendiente**, lo **disponible hoy** para vender y lo que se podría **surtir ahora** (el menor entre lo pendiente y lo disponible), y cuántos días lleva esperando. Buscador por número de orden, cliente o producto.
- **Surtir una línea** (`POST /api/v1/sales-orders/{publicId}/lines/{lineId}/fulfill`, permiso `sales.backorder`): reserva para la línea **lo que haya** (todo su pendiente o una parte). Usa el mismo candado del inventario que solicitar: nunca reserva más de lo que existe ni le quita a otra orden. Si no hay nada nuevo, responde 409.
- **Surtir todo** (`POST /api/v1/sales-orders/backorders/fulfill`, permiso `sales.backorder`): recorre los backorders **de la más antigua a la más reciente** y surte lo que se pueda; lo que una línea toma, la siguiente ya no lo ve. Devuelve qué se reservó por línea y cuánto sigue pendiente.
- **Cuándo se puede surtir**: cuando entra inventario (recibo, ajuste de entrada, transferencia, liberar cuarentena) o se cancela otra orden que lo tenía reservado. Hoy el aviso automático «ya hay para este backorder» no existe todavía (llega con los avisos de ventas); la vista muestra siempre lo surtible.
- **Cerrar con faltante** (`POST /api/v1/sales-orders/{publicId}/lines/{lineId}/close`, permiso `sales.backorder`): da la línea por **terminada sin esperar más**: su backorder se borra y la línea queda cerrada. **Lo ya reservado sigue reservado** para despacharse y la **cantidad pedida se conserva** (sirve para medir el cumplimiento). Una línea cerrada no se surte ni se modifica.
- **Cancelar la orden** borra su backorder y libera lo reservado (sección 7).

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Surtir una línea sin inventario disponible | `No hay inventario disponible para surtir esa línea.` | 409 |
| Surtir o cerrar una línea sin backorder | `Esa línea no tiene backorder.` | 409 |
| Surtir o cerrar en una orden que no está solicitada | `Solo se pueden modificar las líneas de una orden solicitada.` | 422 |
| Surtir o cerrar una línea cerrada | `La línea está cerrada.` | 422 |
| Línea u orden inexistente | `Línea no encontrada.` / `Orden de venta no encontrada.` | 404 |

## 10. Despachar contra la orden (Lote 61)

`POST /api/v1/sales-orders/{publicId}/dispatch` (permiso `warehouse.pick`, el mismo de recolectar) saca del almacén lo reservado para la orden. **Despachar siempre baja el inventario**, lo entreguen o lo vengan a recoger. Todo ocurre en **una sola transacción**:

1. Se **consume la reserva** de las líneas despachadas.
2. El inventario **sale por el ledger** (movimiento de salida con la recolección como referencia: queda en el Kárdex).
3. Se crea la **recolección** (`EMP-#####`) **enlazada a la orden** (`PickBatch.SalesOrderId` y, por línea, `PickBatchLine.SalesOrderLineId`), con el costo del producto congelado.
4. Sube lo **despachado** de cada línea y baja lo reservado.
5. La orden pasa a **despachada parcial** (`PARTIAL`) o **despachada** (`SHIPPED`).

- **Por partes**: se pueden mandar varias líneas y menos cantidad que lo reservado; cada despacho crea su propia recolección. **Nunca se despacha más de lo reservado.**
- **Posición y lote guiados, con cambio permitido**: sin `binId` sale de lo que reservó el sistema. Si el operario tomó de **otra posición** (`binId`, y `lotId` si hay lotes), la reserva **se mueve** a esa posición —si tiene saldo vendible— y de ahí sale; sin saldo responde 409 con el aviso de inventario. La posición debe ser del almacén de la orden, activa y vendible (sin cuarentena, cruce de muelle ni renta).
- **Cuándo está terminada una línea y la orden**: una línea está **terminada** cuando no le queda reserva ni backorder. La orden es `SHIPPED` cuando todas sus líneas están terminadas y se despachó algo; con backorder pendiente se queda en `PARTIAL` hasta que se surta o se **cierre con faltante** (cerrar la última línea pendiente la pasa a despachada).
- **Orden sin nada despachado y todas las líneas cerradas con faltante**: **se cancela sola**, con la nota «Todas las líneas se cerraron con faltante y no se despachó nada.» en el historial.
- El retiro (el cliente recoge) o la **entrega** (crea la orden de transporte) y deshacer una recolección llegan en el lote siguiente.

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Sin líneas | `Indique qué líneas se despachan.` | 400 |
| Línea sin identificar | `Indique la línea que se despacha.` | 400 |
| Línea repetida | `Una línea no puede repetirse en el despacho.` | 400 |
| Cantidad vacía o cero | `La cantidad debe ser mayor que cero.` | 400 |
| Orden que no está solicitada ni despachada parcial | `Solo se puede despachar una orden solicitada o despachada parcial.` | 422 |
| Más de lo reservado | `No se puede despachar más de lo reservado: {SKU} tiene {n} reservadas.` | 409 |
| Línea u orden inexistente | `Línea no encontrada.` / `Orden de venta no encontrada.` | 404 |
| Posición inexistente o de otro almacén | `Posición no encontrada.` | 404 |
| Posición inactiva o de cuarentena / cruce / renta | `La posición {código} no es vendible: está inactiva o es de cuarentena, cruce de muelle o renta.` | 422 |
| Sin saldo en la posición escogida | `Inventario insuficiente: …` (el del inventario) | 409 |
| Sin el permiso `warehouse.pick` | «Sin permiso» | 403 |

## 11. Para soporte

- Tablas `SalesOrder`, `SalesOrderLine` y `SalesOrderReservation` (la que guarda qué saldo reservó cada línea) y las columnas `PickBatch.SalesOrderId` / `PickBatchLine.SalesOrderLineId` (sección **2026-10-15 (l)**), contador `SALESORDER`, dominio de estatus `SalesOrderStatus` y EntityType `SALES_ORDER` (sección **2026-10-15 (k)** de `Diseño/logistica-db-update.sql`).
- Cada línea guarda `TaxRateId`, `TaxRatePct`, `TaxAmount` e `IsTaxExempt`: un cambio posterior de la tasa **no** altera una orden ya guardada.
- Un número `OV-` saltado no ocurre: el contador se toma dentro de la misma transacción que crea la orden.
