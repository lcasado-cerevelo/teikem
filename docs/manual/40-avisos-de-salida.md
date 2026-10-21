# 40 — Avisos de salida

Lote 143 (2026-10-20). Un **aviso de salida** (`SAL-#####`) es el documento con el que el **dueño de la mercancía** (un cliente de almacén, p. ej. ASICS Care en el almacén que
administra Advance Logistics) le avisa al almacén **qué quiere que saque**. Es el espejo del **aviso de llegada** (ASN): el de llegada dice «me va a llegar esto», el de salida dice
«sáquenme esto». **No es una venta**: no lleva precio, impuesto, crédito ni factura (para vender se usa la orden de venta, capítulo 18).

Este capítulo cubre el servidor (API, reglas, trabajos y avisos). Las pantallas de la web y del aparato llegan en los lotes 144 a 146.

## 1. Quién puede

- Módulo **Inventario por lote y serie (`WMS_LOTSERIAL`)**. Con el módulo apagado todo el API responde 403.
- Permisos:
  - **`inventory.view`** — ver los avisos y el pendiente.
  - **`exitnotice.create`** (nuevo, categoría Almacén) — crear, modificar la cantidad de una línea, cancelar, reservar el pendiente y cerrar con faltante. Lo traen el Admin de compañía y el
    Operador de almacén (también los roles ya existentes: el script de base de datos se los agrega).
  - **`warehouse.pick`** — despachar contra el aviso.

## 2. Qué hace

- **Solo productos del dueño.** Cada línea es un producto **activo** del cliente del aviso; uno de otro dueño, o propio de la compañía, se rechaza.
- **Reserva en el acto.** Al crearse, reserva lo que haya con la **rotación de la compañía** (capítulo 6 §2.2: con lote FEFO o FIFO; sin lote FIFO o por posición). La reserva es **por
  producto y lote**; por dentro el ledger reserva por posición, y al despachar se puede sacar el mismo lote de otra posición (la reserva se mueve sola) salvo que la rotación esté en
  **Obligar**.
- **Nunca se rechaza por falta de existencia.** Lo que no alcance queda **pendiente** (backorder) en esa línea. El trabajo `EXIT_NOTICE_BACKORDER_SCAN` (cada 10 minutos) lo va reservando
  cuando entra mercancía, **el aviso más antiguo primero**, y avisa.
- **Sin series ni kits** en esta primera versión (mismo criterio que las órdenes de venta con series). Un producto por lote puede pedir un lote concreto o dejarlo a la rotación.
- **Sin precio.** El destino es texto libre (retiro, dirección o nota).

## 3. Funcionalidades

| Qué | Endpoint | Permiso |
|---|---|---|
| Lista (filtros `status`, `clientPublicId`, `q`, `backorderOnly`, `from`, `to`; paginada) | `GET /api/v1/exit-notices` | `inventory.view` |
| Ficha con líneas y despachos | `GET /api/v1/exit-notices/{publicId}` | `inventory.view` |
| Crear (reserva lo que haya; el resto pendiente) | `POST /api/v1/exit-notices` | `exitnotice.create` |
| Cambiar la cantidad de una línea | `PATCH /api/v1/exit-notices/{publicId}/lines/{lineId}` | `exitnotice.create` |
| Cancelar | `POST /api/v1/exit-notices/{publicId}/cancel` | `exitnotice.create` |
| Ver el pendiente (el más antiguo primero) | `GET /api/v1/exit-notices/backorders` | `inventory.view` |
| Reservar el pendiente de una línea | `POST /api/v1/exit-notices/{publicId}/lines/{lineId}/fulfill` | `exitnotice.create` |
| Reservar todo el pendiente que se pueda | `POST /api/v1/exit-notices/backorders/fulfill` | `exitnotice.create` |
| Cerrar una línea con faltante | `POST /api/v1/exit-notices/{publicId}/lines/{lineId}/close` | `exitnotice.create` |
| Despachar | `POST /api/v1/exit-notices/{publicId}/dispatch` | `warehouse.pick` |

**Crear** — cuerpo: `warehousePublicId` (opcional si hay un solo almacén activo), `clientPublicId` (obligatorio), `reference` (≤ 80), `destination` (≤ 200), `requiredDate`, `notes` (≤ 500) y
`lines` (1 a 200: `productPublicId`, `quantity` y, si se quiere, `lotId`). Devuelve la ficha con lo reservado y lo pendiente de cada línea.

**Despachar** — cuerpo: `lines` (`lineId`, `quantity`, y opcionalmente `binId` y `lotId`) y `note`. Sin posición sale de lo que el aviso tiene reservado (que ya sigue la rotación). Con
posición, la reserva se mueve a esa posición. Crea una **recolección** enlazada al aviso (número `EMP-#####`, que **no se empaca**: es una salida de inventario sin entrega); si esa
recolección se elimina, lo despachado vuelve a reservarse y el aviso se reabre.

## 4. Estatus

| Estatus | Significado |
|---|---|
| **Solicitado** (`REQUESTED`) | Estado inicial: reservado lo que había; se puede modificar y cancelar mientras no se haya despachado nada. |
| **Despachado parcial** (`PARTIAL`) | Se despachó parte y queda reservado o pendiente. Solo se despacha, se reserva el pendiente o se cierra con faltante. |
| **Despachado** (`SHIPPED`) | Todo despachado o cerrado con faltante. Se reabre si se deshace una recolección. |
| **Cancelado** (`CANCELLED`) | Cancelado a mano, o solo cuando todas las líneas se cerraron con faltante sin despachar nada. Libera todo. |

El **pendiente no es un estatus**: es la marca de que alguna línea tiene cantidad pendiente (`hasBackorder`).

## 5. Validaciones y mensajes

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Sin cliente | `Escoja el cliente dueño de la mercancía.` | 400 (`clientPublicId`) |
| Cliente inexistente | `Cliente no encontrado.` | 404 |
| Cliente inactivo | `El cliente está inactivo; no se le puede hacer un aviso de salida.` | 422 |
| Varios almacenes y no se indicó | `Indique el almacén: la compañía tiene más de uno.` | 400 (`warehousePublicId`) |
| Almacén inactivo | `El almacén está inactivo.` | 422 |
| Sin líneas | `Agregue al menos un producto al aviso de salida.` | 400 (`lines`) |
| Más de 200 líneas | `El aviso de salida admite como máximo 200 líneas.` | 400 (`lines`) |
| Línea sin producto | `Escoja el producto de cada línea.` | 400 (`lines[i].productPublicId`) |
| Producto de otro dueño o propio | `El producto {SKU} no pertenece al cliente del aviso de salida.` | 400 (`lines[i].productPublicId`) |
| Producto repetido | `Un producto no puede repetirse en el aviso de salida.` | 400 (`lines[i].productPublicId`) |
| Producto inactivo | `Hay un producto inactivo en el aviso de salida.` | 422 |
| Producto con series | `Los productos con número de serie todavía no se pueden pedir con un aviso de salida.` | 422 |
| Kit | `Los kits todavía no se pueden pedir con un aviso de salida.` | 422 |
| Lote en producto sin lote | `El producto {SKU} no se controla por lote: no indique lote.` | 400 (`lines[i].lotId`) |
| Cantidad ≤ 0 | `La cantidad debe ser mayor que cero.` | 400 (`quantity`) |
| Cantidad con más de 3 decimales | `La cantidad admite como máximo 3 decimales.` | 400 (`quantity`) |
| Cantidad enorme | `La cantidad excede el máximo permitido.` | 400 (`quantity`) |
| Referencia / destino / notas muy largos | `La referencia no puede pasar de 80 caracteres.` / `El destino no puede pasar de 200 caracteres.` / `Las notas no pueden pasar de 500 caracteres.` | 400 |
| Aviso inexistente (o de otro dueño en el portal) | `Aviso de salida no encontrado.` | 404 |
| Modificar un aviso que no está solicitado | `Solo se pueden modificar las líneas de un aviso de salida solicitado.` | 422 |
| Modificar una línea cerrada | `La línea está cerrada.` | 422 |
| Modificar una línea con producto ya despachado | `A esa línea ya se le despachó producto: no se puede modificar.` | 422 |
| Cancelar uno despachado (parcial o total) | `Solo se puede cancelar un aviso de salida solicitado que no se haya despachado.` | 422 |
| Cancelar uno ya cancelado | `El aviso de salida ya está cancelado.` | 409 |
| Reservar el pendiente sin existencia | `No hay inventario disponible para reservar el pendiente de esa línea.` | 409 |
| Línea sin pendiente | `Esa línea no tiene pendiente.` | 409 |
| Pendiente/cerrar de un aviso que no está abierto | `Solo se atiende el pendiente de un aviso de salida solicitado o despachado parcial.` | 422 |
| Despachar sin líneas / sin línea / línea repetida | `Indique qué líneas se despachan.` / `Indique la línea que se despacha.` / `Una línea no puede repetirse en el despacho.` | 400 |
| Despachar un aviso que no está abierto | `Solo se puede despachar un aviso de salida solicitado o despachado parcial.` | 422 |
| Despachar más de lo reservado | `No se puede despachar más de lo reservado de {SKU} ({n}). Reserve primero el pendiente.` | 409 |
| Lote sin posición en el despacho | `Si indica el lote de donde sale, indique también la posición.` | 400 (`lines[i].lotId`) |
| Posición no usable | `La posición {código} no se puede usar: está inactiva o es de cuarentena, cruce de muelle o renta.` | 422 |
| Rotación en «Obligar» y se pidió otra posición | `La rotación es obligatoria: {SKU} debe salir de {posiciones}. No escoja posición ni lote; el sistema los asigna.` | 409 |
| Empacar el despacho de un aviso | `El despacho {número} es de un aviso de salida y no se empaca: es una salida de inventario sin entrega.` | 422 |
| Dos personas a la vez | `El aviso de salida cambió al mismo tiempo. Recárguelo e intente de nuevo.` | 409 |

## 6. Avisos del sistema

Cuatro eventos (módulo de inventario, opcionales; **no viene ninguna regla armada**, el administrador la crea en Sistema → Reglas de aviso — capítulo 14): **Aviso de salida solicitado**
(`EXIT_NOTICE_REQUESTED`), **Pendiente ya reservado** (`EXIT_NOTICE_BACKORDER_RESERVED`, lo publica el trabajo), **Aviso de salida despachado** (`EXIT_NOTICE_SHIPPED`, cuando queda despachado por
completo) y **Aviso de salida cancelado** (`EXIT_NOTICE_CANCELLED`, a mano o solo).

## 7. Casos frecuentes

- **Pedí 10 y solo reservó 3.** Es lo esperado: 3 reservadas y 7 pendientes. Cuando entre mercancía el trabajo reserva el resto (o «Reservar» lo hace al momento).
- **Ya no quiero el pendiente.** «Cerrar con faltante» en esa línea: deja de esperar inventario y lo ya reservado sigue para despacharse.
- **Despaché por error.** Elimina la recolección (Recolección → eliminar): la existencia vuelve a su posición, lo despachado se vuelve a reservar y el aviso se reabre.
- **¿Se puede ver el aviso desde el portal del cliente?** Todavía no; llega con el portal de almacén. El filtro por cliente ya existe en el servidor, así que un cliente solo verá lo suyo.

## Pantalla (Lote 144)
En la web: **Recibo → pestaña «Avisos de salida»** (`/warehouse/receipts?tab=exit-notices`). Ver la lista requiere `inventory.view`; **Nuevo aviso de salida**, **Reservar el pendiente**, cambiar cantidad, cerrar con faltante y cancelar requieren `exitnotice.create`. La lista tiene buscador libre, filtro de estatus y «Solo con pendiente»; un aviso con faltante muestra la marca «Con pendiente». Despachar contra el aviso se hace desde Recolección o Despacho manual (lote 145).

## Despachar y «el despacho exige documento» (Lote 145)
- **Despachar desde el aviso:** abra el aviso (Recibo → Avisos de salida) y pulse **Despachar** (permiso `warehouse.pick`, solo si hay algo reservado). Escriba cuánto sale de cada línea (por omisión todo lo reservado; 0 = esa línea no sale). Error en pantalla: «No puede despachar más de lo reservado.».
- **Ajuste de la compañía** (Ajustes → Operación → «El despacho exige documento»): Libre (por omisión), Aviso de salida u orden de venta, Solo aviso de salida, Solo orden de venta. Con cualquier valor distinto de Libre el **despacho manual** se bloquea: HTTP 409 «La compañía exige despachar contra un aviso de salida. Cree el aviso y despache desde él.» (o «…contra una orden de venta…», o «…contra un documento (aviso de salida u orden de venta)…»). Empacar no se ve afectado.

## En el aparato de almacén (Lote 146)
Inicio muestra **Avisos de salida** cuando el aparato tiene avisos con algo reservado (se bajan al sincronizar; requiere `inventory.view` y el módulo de lotes y series). Escanee o escriba el número SAL-… (o toque uno de la lista), vaya a cada posición, lea la posición, confirme la cantidad y al terminar toque **Despachar**. Sirve sin señal: queda en la cola y se envía al volver; si el servidor lo rechaza (por ejemplo, la rotación obligatoria o menos existencia) muestra su mensaje exacto. Con «El despacho exige documento» activo, **Despacho** pregunta primero «¿Contra qué despachas?» y ofrece aviso de salida y/o orden de venta.

## Editar los datos de un aviso solicitado y disponible en el selector (Lote 158)

`PATCH /exit-notices/{publicId}` (`exitnotice.create`) cambia referencia, destino, fecha requerida y notas de un aviso **solicitado** (texto vacío las quita; null no cambia). Las cantidades se siguen cambiando por línea. Otro estatus: 422 «Solo se pueden modificar las líneas de un aviso de salida solicitado.». Al elegir producto en «Nuevo aviso de salida» el selector muestra entre paréntesis lo **disponible** de cada producto, para saber si lo pedido se reserva o queda pendiente.
