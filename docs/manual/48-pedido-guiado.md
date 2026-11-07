# 48 — Pedido guiado (web y app móvil)

<!-- kemi:staff -->

Lotes 223 y 226 (2026-11-07) — pedido de vendedores, partes P3 (web) y P4 (app móvil). El **pedido guiado** es otra puerta a la **misma orden de venta** del [capítulo 18](18-ordenes-de-venta.md): el vendedor arma el pedido en **cuatro pasos** (cliente, productos, entrega, autorización), se guarda como **borrador** en cada paso y al final lo **envía** confirmando quién es. La ventana «Nueva orden» sigue como siempre, para la oficina. Los precios salen de las listas ([47](47-precios-y-ofertas.md)); el cliente nuevo nace como prospecto ([02 §1.8](02-clientes-y-contratos.md#18-cliente-prospecto-alta-mínima-y-aprobación-lote-222)).

> Capturas pendientes (P8): los cuatro pasos en la web (escritorio y 360 px), el alta de «Cliente nuevo», el carrito, la firma, la ventana «Confirme con su contraseña», el diálogo de faltante y la pantalla final; en la app, Pedidos → Mis pedidos, los cuatro pasos y la confirmación con la huella.

## 1. Quién puede y dónde

- Módulo **Órdenes de venta (`SALES_ORDERS`)**.
- Permisos: **`sales.create`** arma, guarda y envía el pedido (rol **Ventas**, **Oficina de ventas** y Admin de compañía). Ver el catálogo y la lista usa `sales.view`. Dar de alta un cliente nuevo desde el paso 1 usa **`clients.create_basic`** (rol Ventas) o `clients.create`. Escoger otra dirección de entrega del cliente en la app usa `locations.read`.
- **Web**: Contabilidad → **Órdenes de venta** → botón **Nuevo pedido** (junto a «Nueva orden»), ruta `/sales-orders/new-order`. Sin ítem de menú propio.
- **App móvil**: **Inicio → Pedidos** (visible con `sales.create`; una persona «solo chofer» no lo ve). Dentro: **Mis pedidos** y **Nuevo pedido**. Detalles de la app en la [sección 6](#6-en-la-app-móvil).

## 2. Los cuatro pasos (web y app: la misma secuencia)

Barra de progreso arriba («Paso 2 de 4: Productos»). **Siguiente** guarda el borrador del paso que se deja; **Atrás** no guarda y la barra solo deja volver a pasos anteriores. Lo que se cambia al volver se guarda con el siguiente «Siguiente».

### Paso 1 — Cliente
- **Buscar cliente** por número, nombre o teléfono; la lista muestra nombre, pueblo y número. Un cliente prospecto sale marcado «Prospecto: espera aprobación».
- **Cliente nuevo** abre el alta mínima (negocio, DBA, contacto, teléfono de 10 dígitos y extensión, teléfono 2, correo, SURI, dirección física, dirección postal opcional, forma de pago del cliente y fotos del registro de comerciante y del certificado de revendedor, con cámara o archivo). El cliente **nace prospecto**: el pedido se envía igual, pero no se despacha hasta que lo aprueben. Reglas y mensajes en [02 §1.8](02-clientes-y-contratos.md#18-cliente-prospecto-alta-mínima-y-aprobación-lote-222).
- **Almacén**: se pide solo si la compañía tiene más de uno activo.
- **Datos del pedido**: **Pedido urgente** y **Nota del pedido** (hasta 500 caracteres); **Forma de pago** prellenada con la del cliente y editable (si cambia queda la marca «Cambió la forma de pago»); **Término del crédito** si la forma es Crédito. Detalle de las formas de pago en [18 §19](18-ordenes-de-venta.md#19-forma-de-pago-y-consignación-lote-222).
- «Siguiente» crea el borrador (`POST /sales-orders` con `step = CLIENT`): todavía **sin productos**.

### Paso 2 — Productos
- Pestañas por **marca**; marcas sin producto activo no salen; los sin marca van al final («Sin marca»).
- Arriba de cada marca, sus **ofertas vigentes** como tarjetas con foto y precio, con **+** y **−**. Debajo, la **lista completa** de la marca: SKU, nombre, precio (de la lista vigente del cliente o general) y casilla de cantidad; teclear una cantidad agrega la línea. Un buscador por SKU o nombre filtra la marca.
- **Carrito** fijo abajo con el conteo y el subtotal; tocarlo muestra las líneas para corregir o quitar.
- El vendedor **no teclea precios**: las líneas viajan sin precio y el servidor pone el resuelto (capítulo [47](47-precios-y-ofertas.md)). Un producto «Sin precio» no se puede pedir.
- Un kit se pide en piezas enteras. En la web, un kit ajustable permite cambiar las piezas de esta orden; en la app todavía no (se piden con las del catálogo).
- «Siguiente» guarda con `step = PRODUCTS`: desde aquí la orden necesita al menos un producto.

### Paso 3 — Entrega
- **Entrega** (se lleva al cliente) o **Recogido** (lo recoge: no lleva cargo de envío). Con «Entrega», el destino por omisión es la **dirección física del cliente**; se puede escoger otra dirección (consignatario) del cliente o crear una (en la web).
- **En consignación**: solo si la forma de pago es Consignación.
- **Fecha requerida** (opcional). En la app se escribe `AAAA-MM-DD`.
- **Resumen**: subtotal, IVU, **cargo de envío** (con su «Por qué») y **total**. Lo calcula el servidor con la regla de la compañía y del cliente ([18 §20](18-ordenes-de-venta.md#20-cargo-de-envío-lote-222)); cambiar entrega/recogido o el destino guarda el borrador al momento para recalcular.

### Paso 4 — Autorización y envío
- **Nombre de quien autoriza** (hasta 150 caracteres) y **firma dibujada** (con el mouse o el dedo; se sube como imagen PNG). Son **obligatorios solo si el cliente exige la firma de quien autoriza el pedido**; si no, son opcionales.
- **Enviar pedido** pide confirmar quién envía:
  - **Web**: una ventana pide la **contraseña** (y el código de verificación, MFA, si la cuenta lo tiene).
  - **App**: pide **huella o rostro**; si el teléfono no tiene o no se confirma, pide la **contraseña** (más el código MFA si hace falta).
- Luego el servidor **solicita** la orden: valida inventario y crédito y reserva (igual que «Solicitar» en [18 §6](18-ordenes-de-venta.md#6-solicitar-la-orden-validar-y-reservar-lote-58)). Si falta inventario o se excede el crédito aparece el **diálogo de faltante** de siempre: se repite el envío con backorder, o con la autorización de crédito (`orders.credit_override`).
- **Pantalla final**: número `OV-#####`, total, y **Nuevo pedido**. Si el cliente es prospecto: «El cliente es prospecto: el pedido quedó reservado pero espera la aprobación del cliente y no se puede despachar todavía.»

## 3. El borrador

- Cada «Siguiente» deja la orden como **borrador** en Órdenes de venta (`DRAFT`). Si el navegador se cierra o el celular se apaga, el borrador está ahí; **no hay modo sin señal** (sin señal no se avanza y la app avisa).
- **Web**: tras el primer guardado la dirección queda `?order=<publicId>`; abrirla retoma el borrador en el primer paso pendiente. Una orden que ya no es borrador no se edita ahí: «La orden {número} ya no es un borrador: ábrala en Órdenes de venta.»
- **App**: un borrador de «Mis pedidos» se abre en el pedido donde se quedó; lo demás se ve en una hoja de solo lectura.
- El paso **no se guarda** en la base: solo dice qué validar al guardar.

## 4. Cómo se usa (API)

| Acción | Llamada | Permiso |
|---|---|---|
| Catálogo del cliente | `GET /api/v1/sales-orders/catalog?clientPublicId=…` | `sales.view` |
| Crear el borrador (paso 1) | `POST /api/v1/sales-orders` `{ "clientPublicId": "…", "isUrgent": true, "notes": "…", "paymentMethod": "CREDIT", "paymentTerm": "NET30", "step": "CLIENT" }` | `sales.create` |
| Guardar otro paso | `PATCH /api/v1/sales-orders/{publicId}` `{ "lines": [ { "productPublicId": "…", "quantity": 2 } ], "step": "PRODUCTS" }`; `{ "deliveryMode": "DELIVERY", "consigneePublicId": "…", "step": "DELIVERY" }`; `{ "authorizedByName": "Ana Pérez", "step": "AUTHORIZATION" }` | `sales.create` |
| Subir la firma (PNG o JPG) y el nombre | `POST /api/v1/sales-orders/{publicId}/signature` (multipart: `file`, `authorizedByName` opcional) | `sales.create` |
| Quitar la firma | `POST /api/v1/sales-orders/{publicId}/signature/remove` | `sales.create` |
| Enviar el pedido | `POST /api/v1/sales-orders/{publicId}/submit` `{ "authMethod": "PASSWORD", "password": "…", "mfaCode": "123456", "allowBackorder": false }` | `sales.create` |

- La firma se ve con `GET /files/{terms.signatureFilePublicId}` (permiso `sales.view`). Subir otra **reemplaza** la anterior; quitar o reemplazar la **desliga**, el archivo no se borra. `authorizedByName` ausente = sin cambio; `""` = quitarlo.
- `authMethod`: **`PASSWORD`** (la contraseña del propio usuario, y `mfaCode` si tiene MFA confirmado; renueva la verificación reciente de la sesión) o **`BIOMETRIC`** (la app ya validó huella o rostro; el servidor exige que la sesión tenga una verificación reciente —contraseña y MFA al entrar o al reautenticarse— dentro de la ventana de la compañía, 30 minutos si no hay otra; si no, 403 `aal2_required` y la app pide la contraseña). La huella nunca se salta el MFA.
- El envío valida la orden **antes** de la contraseña (borrador, productos, destino, firma exigida): no se gasta un intento de contraseña en un pedido que no se puede enviar. Una contraseña o código MFA incorrectos responden **400** (no 401) y **cuentan para el bloqueo de la cuenta**.
- Deja `terms.submittedWithAuth` (`PASSWORD` o `BIOMETRIC`) y un evento REAUTH en la bitácora de seguridad (método, orden y sesión).
- `POST /sales-orders/{id}/request` sigue existiendo **sin** reautenticación (la oficina con «Nueva orden»): el pedido guiado usa `/submit`.

## 5. Validaciones y mensajes

| Campo / paso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `step` | CLIENT, PRODUCTS, DELIVERY o AUTHORIZATION (sin distinguir mayúsculas) | `El paso del pedido debe ser CLIENT, PRODUCTS, DELIVERY o AUTHORIZATION.` | 400 (`step`) |
| Productos (desde PRODUCTS; y al enviar) | Al menos uno | `Agregue al menos un producto a la orden.` | 400 (`lines`) |
| Entrega (DELIVERY y AUTHORIZATION, y al enviar) | «Entrega» necesita a dónde | `Escoja a dónde se entrega el pedido o márquelo como recogido.` | 400 (`consigneePublicId`) |
| Entrega | DELIVERY o PICKUP | `La entrega debe ser DELIVERY (entrega) o PICKUP (recogido).` | 400 (`deliveryMode`) |
| Cliente | Obligatorio | `Escoja el cliente que compra.` | 400 (`clientPublicId`) |
| Cliente | Activo | `El cliente está inactivo; no se le puede vender.` | 422 |
| Cliente suspendido | No puede solicitar | `El cliente está suspendido: no se le puede solicitar una orden.` | 422 |
| Almacén | Varios y no se indicó | `Indique el almacén: la compañía tiene más de uno.` | 400 (`warehousePublicId`) |
| Nota | Hasta 500 | `Las notas no pueden pasar de 500 caracteres.` | 400 |
| Forma de pago, término, consignación | Ver [18 §19](18-ordenes-de-venta.md#19-forma-de-pago-y-consignación-lote-222) | — | 400 |
| Firma | Archivo obligatorio | `Dibuje la firma de quien autoriza el pedido.` | 400 (`file`) |
| Firma | Solo PNG o JPG (por su contenido) | `La firma debe ser una imagen PNG o JPG.` | 400 (`file`) |
| Firma o nombre en una orden que ya no es borrador | Solo borrador | `Solo se puede editar una orden en borrador.` | 422 |
| Nombre de quien autoriza | Hasta 150 | `El nombre de quien autoriza no puede pasar de 150 caracteres.` | 400 (`authorizedByName`) |
| Enviar: el cliente exige firma | Nombre y firma | `Este cliente exige el nombre y la firma de quien autoriza el pedido.` | 400 (`signature`) |
| Enviar: método | PASSWORD o BIOMETRIC | `La reautenticación debe ser BIOMETRIC o PASSWORD.` | 400 (`authMethod`) |
| Enviar: sin método | Obligatorio | `Indique cómo se confirmó el envío del pedido: PASSWORD (contraseña) o BIOMETRIC (huella o rostro en la app).` | 400 (`authMethod`) |
| Enviar: sin contraseña | Obligatoria con PASSWORD | `Escriba su contraseña para enviar el pedido.` | 400 (`password`) |
| Enviar: contraseña mala | — | `Contraseña incorrecta.` | 400 (`password`) |
| Enviar: código MFA malo | — | `Código MFA inválido.` | 400 (`mfaCode`) |
| Enviar con BIOMETRIC sin verificación reciente | — | `Esta acción requiere reautenticación reciente (AAL2).` | 403 (`aal2_required`) |
| Enviar una orden que no es borrador | — | `Solo se puede solicitar una orden en borrador.` | 422 |
| Falta inventario | Ver [18 §8](18-ordenes-de-venta.md#8-mensajes-nuevos-lote-58) | `No alcanza el inventario de {SKU}: pedido {n}, disponible {n}, faltante {n}.` | 409 |
| Crédito excedido | Ver [18 §8](18-ordenes-de-venta.md#8-mensajes-nuevos-lote-58) | `El cliente excede su límite de crédito: límite {n}, en curso {n}, esta orden {n}.` | 422 |
| Orden inexistente | — | `Orden de venta no encontrada.` | 404 |
| Sin el permiso | — | `Falta el permiso 'sales.create'.` | 403 |

Mensajes del alta de un cliente nuevo: [02 §1.8](02-clientes-y-contratos.md#18-cliente-prospecto-alta-mínima-y-aprobación-lote-222). Mensajes de precio y oferta: [47 §6](47-precios-y-ofertas.md#6-validaciones-y-mensajes). En la pantalla, los avisos que aparecen junto al campo repiten estos mismos textos.

## 6. En la app móvil

- **Pedidos** aparece en el Inicio solo con `sales.create`. Con varias compañías, la pantalla usa la primera donde la persona tiene `sales.create` (o la que escoja en los botones de compañía, solo antes del primer guardado).
- **Mis pedidos**: estatus (Todos, Borradores, Solicitados, Parciales, Despachados, Cancelados), buscador por número, cliente o producto, y distintivos (Urgente, Cliente por aprobar, En consignación, Recogido, Con backorder). Ver la lista exige además `sales.view`; sin él la pantalla lo dice y deja hacer el pedido nuevo. La lista muestra los pedidos cuya persona que los pidió coincide con el nombre o el correo de la sesión, sobre los 200 más nuevos del filtro; dos personas con el mismo nombre se verían juntas.
- **Nuevo pedido**: los cuatro pasos de la sección 2, con la firma hecha con el dedo en el recuadro («Firmar de nuevo» si ya hay una guardada) y fotos con cámara o galería en el alta de cliente nuevo (si una foto falla, el cliente queda creado: «El cliente quedó creado, pero alguna foto no se pudo subir. Súbela después desde la ficha del cliente en la web.»).
- **Enviar pedido**: huella o rostro («Confirma el envío del pedido»); se pide la contraseña si el teléfono no tiene huella («Este teléfono no tiene huella ni rostro registrados: escribe tu contraseña para enviar el pedido.»), si no se confirmó («No se confirmó la huella o el rostro: escribe tu contraseña para enviar el pedido.») o si la verificación de la sesión venció («Por seguridad hace falta tu contraseña (la verificación de la sesión venció).»).
- **Sin señal**: no hay modo sin señal. Sin conexión no se avanza («No se pudo consultar. Se necesita señal.»); lo ya guardado queda como borrador en el servidor.
- **Lo que la app todavía no hace** (se hace en la web): ajustar las piezas de un kit ajustable, crear una dirección de entrega nueva, escoger el pueblo de una lista (se escribe) y modificar una orden ya solicitada.
- El texto de la app va en «tú»; los mensajes que repiten al servidor van en «usted», tal cual los de la sección 5.

## 7. Estatus y transiciones del pedido

El pedido guiado usa los estatus de la orden de venta (`SalesOrderStatuses`) con el motor de estatus.

| De → a | Quién | Qué valida | Efectos | Qué queda bloqueado después |
|---|---|---|---|---|
| (nuevo) → Borrador | `sales.create` | Cliente activo, almacén, nota ≤ 500, forma de pago coherente | Número `OV-#####`; precios resueltos; envío calculado | — |
| Borrador → Borrador (cada paso) | `sales.create` | Lo del paso (productos, destino) | Se recalculan precios, impuesto y envío | — |
| Borrador → Solicitada («Enviar pedido») | `sales.create` + contraseña o huella | Orden completa, firma exigida, inventario, crédito | Reserva inventario; `submittedWithAuth`; aviso «Pedido urgente» si es urgente; prospecto → `awaitingClientApproval` | Ya no se edita ni se cambia la firma («Solo se puede editar una orden en borrador.»); solo se modifican líneas ([18 §7](18-ordenes-de-venta.md#7-cancelar-y-modificar-una-orden-solicitada-lote-58)) |
| Borrador o Solicitada → Cancelada | `sales.cancel` | Que no tenga nada despachado | Libera lo reservado | Todo |
| Solicitada → Despachada parcial / Despachada | `warehouse.pick` | Que el cliente no esté «esperando aprobación» | Baja el inventario | Con el cliente sin aprobar: `La orden espera la aprobación del cliente nuevo: no se puede despachar hasta que lo aprueben.` (422) |

## 8. Preguntas frecuentes del módulo

- **No veo «Nuevo pedido» o «Pedidos».** Hace falta `sales.create` y el módulo Órdenes de venta encendido.
- **«Enviar pedido» me pide la contraseña de nuevo.** Es a propósito: es la firma del vendedor. Con la huella (app) solo la pide si la verificación de la sesión venció.
- **Me equivoqué de contraseña varias veces.** Cuenta para el bloqueo de la cuenta, como al iniciar sesión.
- **El cliente es nuevo y el pedido «espera aprobación».** Está reservado, no despachado. Quien tenga `sales.approve_client` lo aprueba en la ficha del cliente.
- **Quedó un borrador sin terminar.** Está en Órdenes de venta (o «Mis pedidos»); ábralo para seguirlo o cancélelo.

Los mensajes están en la [FAQ](faq.md#lotes-223-y-226--pedido-guiado-web-y-app-móvil).

## 9. Para soporte

- No hay tablas nuevas para el paso: es la misma `SalesOrder`. Las columnas `AuthorizedByName`, `SignatureFileId` y `SubmittedWithAuth` las puso el Lote 222; la firma es un `StoredFile` de clase SIGNATURE (dueño `SALES_ORDER`), cifrado.
- El evento REAUTH con `stage = sales_submit` (método, número de orden, sesión) dice quién envió y desde qué sesión o teléfono (Sistema → Auditoría y seguridad).
- Web: `web-app/src/features/sales/wizard/*`. App: `app-movil/src/app/(auth)/NewSalesOrder` y `SalesOrders`. Las reglas del carrito y de los pasos están **copiadas** en la app (`salesOrderRules.ts`): un cambio en la web hay que repetirlo allá.
