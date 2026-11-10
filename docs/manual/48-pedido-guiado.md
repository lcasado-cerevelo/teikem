# 48 — Pedido guiado (web y app móvil)

<!-- kemi:staff -->

Lotes 223 y 226 (2026-11-07) — pedido de vendedores, partes P3 (web) y P4 (app móvil). El **pedido guiado** es otra puerta a la **misma orden de venta** del [capítulo 18](18-ordenes-de-venta.md): el vendedor arma el pedido en **cuatro pasos** (cliente, productos, entrega, autorización), se guarda como **borrador** en cada paso y al final lo **envía** confirmando quién es. La ventana «Nueva orden» sigue como siempre, para la oficina. Los precios salen de las listas y de las ofertas ([47](47-precios-y-ofertas.md)); el cliente nuevo nace como prospecto ([02 §1.8](02-clientes-y-contratos.md#18-cliente-prospecto-alta-mínima-y-aprobación-lote-222)).

> **Capturas pendientes (Lotes 229, 230, 231 y 232).** El paso 2 cambió: tarjetas de oferta con el nombre de la oferta y su vigencia, cuadrito de foto en todo producto con foto, avisos de precio y «Cambiar» en las piezas de un kit de oferta del carrito (Lotes 231 y 232: el kit de oferta sale solo como tarjeta y solo si está en una oferta vigente; el kit armado sale como un producto más). Las capturas del paso 2, del carrito y del paso 3 de abajo son de antes (lote 228) y no muestran eso; hay que volver a tomarlas con el recorrido de Playwright.
>
> Capturas del recorrido del lote 228 (2026-11-07, datos de demostración): web en escritorio (claro y oscuro) y a 360 px. Siguen pendientes las de la **app móvil** (Pedidos → Mis pedidos, los pasos y la huella; el recorrido de Playwright no corre la app) y la del **diálogo de faltante** (es el mismo de [18 §6](18-ordenes-de-venta.md#6-solicitar-la-orden-validar-y-reservar-lote-58)).

## 1. Quién puede y dónde

- Módulo **Órdenes de venta (`SALES_ORDERS`)**.
- Permisos: **`sales.create`** arma, guarda y envía el pedido (rol **Ventas**, **Oficina de ventas** y Admin de compañía). Ver el catálogo y la lista usa `sales.view`. Dar de alta un cliente nuevo desde el paso 1 usa **`clients.create_basic`** (rol Ventas) o `clients.create`. Escoger otra dirección de entrega del cliente en la app usa `locations.read`.
- **Web**: Contabilidad → **Órdenes de venta** → botón **Nuevo pedido** (junto a «Nueva orden»), ruta `/sales-orders/new-order`. Sin ítem de menú propio.
- **App móvil**: **Inicio → Pedidos** (visible con `sales.create`; una persona «solo chofer» no lo ve). Dentro: **Mis pedidos** y **Nuevo pedido**. Detalles de la app en la [sección 6](#6-en-la-app-móvil).

## 2. Los cuatro pasos (web y app: la misma secuencia)

Barra de progreso arriba («Paso 2 de 4: Productos»). **Siguiente** guarda el borrador del paso que se deja; **Atrás** no guarda y la barra solo deja volver a pasos anteriores. Lo que se cambia al volver se guarda con el siguiente «Siguiente».

### Paso 1 — Cliente
- **Buscar cliente** por número, nombre o teléfono; la lista muestra nombre, pueblo y número. Un cliente prospecto sale marcado «Prospecto: espera aprobación». El buscador toma el foco al entrar y la lista de clientes se despliega al enfocarlo (aunque no se escriba nada); se cierra al salir del campo.
- **Un solo cliente:** si la compañía tiene un solo cliente activo, el pedido lo trae escogido por omisión (una sola vez; si se toca «Cambiar», no se vuelve a escoger solo).
- **Cliente nuevo** abre el alta mínima (negocio, DBA, contacto, teléfono de 10 dígitos y extensión, teléfono 2, correo, SURI, dirección física, dirección postal opcional, forma de pago del cliente y fotos del registro de comerciante y del certificado de revendedor, con cámara o archivo). El cliente **nace prospecto**: el pedido se envía igual, pero no se despacha hasta que lo aprueben. Reglas y mensajes en [02 §1.8](02-clientes-y-contratos.md#18-cliente-prospecto-alta-mínima-y-aprobación-lote-222).
- **Almacén (Lote 235)**: la orden sale de **un** almacén. Con **un solo almacén activo** no se pregunta ni se muestra (se usa ese). Con **más de uno**, el campo **Almacén** sale **ya escogido con el almacén principal** de la compañía (dice «(principal)»); se puede cambiar, no es un campo vacío obligatorio. El almacén principal se marca en Almacén → Almacenes ([06 §1.7](06-inventario-y-almacen.md#17-almacén-principal-lote-235)). De ese almacén sale el «Disponible» del paso 2.
- **Datos del pedido**: **Pedido urgente** y **Nota del pedido** (hasta 500 caracteres); **Forma de pago** prellenada con la del cliente y editable (si cambia queda la marca «Cambió la forma de pago»); **Término del crédito** si la forma es Crédito. Detalle de las formas de pago en [18 §19](18-ordenes-de-venta.md#19-forma-de-pago-y-consignación-lote-222).
- «Siguiente» crea el borrador (`POST /sales-orders` con `step = CLIENT`): todavía **sin productos**.

![Paso 1: el cliente nuevo escogido (prospecto), pedido urgente con nota y la forma de pago prellenada](frontend/img/l228-paso1-cliente.png)
![Paso 1 en el tema oscuro](frontend/img/l228-paso1-cliente-oscuro.png)

![«Cliente nuevo»: el alta mínima con dirección física, forma de pago y las fotos de los documentos](frontend/img/l228-cliente-nuevo.png)

### Paso 2 — Productos
- Pestañas por **marca**; marcas sin producto activo no salen; los sin marca van al final («Sin marca»).
- Arriba de cada marca, sus **ofertas vigentes** como **tarjetas con foto**, con **+** y **−**. Cada tarjeta lleva el **nombre de la oferta**, hasta cuándo vale («Válida hasta el {fecha}») o, si la oferta no tiene fecha de fin, **«Sin fecha de fin»**, y el precio con el chip **Oferta** (al pasar el mouse dice «Precio de la oferta «{nombre}»»). Solo salen las ofertas **publicadas**, activas, vigentes hoy y que aplican a ese cliente ([47 §4](47-precios-y-ofertas.md#4-ofertas)). Un **kit de oferta** sale **solo como tarjeta** (no en la lista de la marca) y solo mientras esté en alguna oferta publicada y vigente para ese cliente ([47 §4.4](47-precios-y-ofertas.md#44-kits-de-oferta-y-la-biblioteca-lote-231)); un **kit armado** (con existencia) sale en la lista de su marca como cualquier producto.
- Si el precio que se cobrará es el **negociado del cliente** porque es más bajo que el de la oferta, la tarjeta lo dice: «Oferta: $9.00. Se cobra su precio negociado, que es más bajo.» (con el precio de la oferta). Entre el negociado y la oferta siempre gana el más bajo ([47 §2](47-precios-y-ofertas.md#2-de-dónde-sale-el-precio-de-una-línea)).
- Debajo, la **lista completa** de la marca: un **cuadrito con la foto** del producto (44 px, del alto del renglón; al pasar el mouse, enfocarlo o tocarlo se ve grande), SKU, nombre, casilla de **cantidad** (solo números enteros; teclearla agrega la línea) y el **precio**. **El cuadrito sale para todo producto con foto**, no solo para las ofertas: la que se muestra es la del renglón de la oferta o de la lista, si la tiene; si no, la del producto; si es un kit sin foto, la de su primera pieza con foto; si no hay ninguna, queda vacío ([47 §5](47-precios-y-ofertas.md#5-fotos-del-producto-del-renglón-y-del-kit)). El vendedor con `sales.view` ve las fotos aunque no tenga `inventory.view`. Un **producto normal que está en una oferta** sale además en la lista de su marca, con el precio de la oferta y el chip **Oferta**.
- **Avisos de precio** (texto pequeño junto al producto, en la línea del carrito con «Oferta «{nombre}»» y en el recuadro «Avisos de precio» del paso 3): «El producto … está en dos ofertas a la vez…» y «La oferta … pone … más caro que la lista general…». Solo informan: **se cobra el precio más bajo** y no impiden seguir ni enviar el pedido. Los textos exactos están en [47 §2](47-precios-y-ofertas.md#2-de-dónde-sale-el-precio-de-una-línea).
- El **buscador** por SKU o nombre toma el foco al entrar al paso. Con texto, busca en **todas las marcas** (productos y ofertas); sin texto, muestra la marca escogida.
- **Carrito** fijo abajo con el conteo y el subtotal; tocarlo muestra las líneas para corregir o quitar.
- El vendedor **no teclea precios**: las líneas viajan sin precio y el servidor pone el resuelto (capítulo [47](47-precios-y-ofertas.md)). Un producto «Sin precio» no se puede pedir, **salvo para quien tiene `sales.price`** (el Admin de compañía): en ese renglón aparece la casilla **Precio** (con el signo `$`, decimales hasta 4), a la derecha de la cantidad, que se habilita al poner la cantidad. Ese precio viaja como excepción en cada guardado de la orden (pasos 2, 3 y 4) y no se guarda en el producto ni en ninguna lista. Sin `sales.price`: `El producto {SKU} no tiene precio de venta: escriba el precio.` (400).
- Un kit de oferta se pide en piezas enteras. En la web, un kit de oferta **«Ajustable al pedir»** permite cambiar las **cantidades** de las piezas de esta orden (sigue igual: el precio del kit no cambia); en la app todavía no (se piden con las del catálogo).
- **Cambiar una pieza del kit (Lote 230; solo kits de oferta desde el Lote 231).** En el carrito, un kit de oferta muestra sus piezas (las ve cualquier vendedor con `sales.create`, aunque no tenga `inventory.view`); cada pieza con sustitutos válidos lleva **«Cambiar»**: la lista ofrece solo productos de la **misma categoría exacta** y el **mismo precio de venta**, en la misma cantidad. Al escoger uno, la pieza dice «en lugar de {SKU}» y se puede **«Deshacer»**. El precio del kit no cambia, no hay permiso aparte y las piezas «No sustituibles» no llevan el botón. Reglas y mensajes en [18 §17.1](18-ordenes-de-venta.md#171-cambiar-una-pieza-del-kit-al-pedir-lote-230).
- **Kit armado (Lote 232).** Es un producto con existencia: se pide como cualquier producto (cantidad, precio, foto), **no muestra piezas ni «Cambiar» ni «Ajustable»** y se reserva y despacha el kit mismo. Si no hay kits suficientes pasa lo de cualquier producto: al enviar, el sistema avisa del faltante (`No alcanza el inventario de {SKU}: pedido {n}, disponible {n}, faltante {n}.`, 409) y, si se decide dejarlo, el faltante queda en **backorder** ([18 §6](18-ordenes-de-venta.md#6-solicitar-la-orden-validar-y-reservar-lote-58)). Cómo se arma el kit: [06 §8b.2](06-inventario-y-almacen.md#8b2-kit-armado-cómo-se-usa-lote-232).
- **Disponible (Lote 235).** Cada renglón de la lista, cada tarjeta de oferta y cada línea del carrito dicen **«Disponible: N»**: lo disponible para vender **en el almacén de la orden** (la misma cuenta que usa Solicitar: en mano menos reservado, sin cuarentena, cruce de muelle ni renta; [18 §6](18-ordenes-de-venta.md#6-solicitar-la-orden-validar-y-reservar-lote-58)). En un **kit de oferta** es cuántos kits **completos** se pueden armar con sus piezas en ese almacén; en un **kit armado**, su propia existencia.
- **Pedir más de lo disponible se permite (backorder).** No se bloquea: el vendedor puede saber que viene una compra de camino. El renglón queda **marcado en rojo** con el distintivo **«Backorder: N»** y el texto «Solo hay X disponibles en {almacén}; N quedan en backorder.» (con 1: «Solo hay 1 disponible en {almacén}; …»; sin nada: «No hay disponible en {almacén}; N quedan en backorder.»). El mismo aviso sale en el **carrito** y en el **resumen del paso 3**. El paso avanza y el borrador se guarda con la cantidad completa.
- **Nota de otros almacenes.** Si el producto tiene disponible en **otro** almacén activo, debajo del renglón sale una nota informativa: «Hay disponible en {almacén}: N» o, si son varios, «Hay disponible en: {almacén A} N · {almacén B} M». Solo informa (la orden sigue saliendo de su almacén; para venderlo del otro se cambia el almacén en el paso 1). Con un solo almacén no sale nada.
- «Siguiente» guarda con `step = PRODUCTS`: desde aquí la orden necesita al menos un producto.

![Paso 2: pestañas por marca, la oferta con foto y la lista de la marca con el precio de la lista](frontend/img/l228-paso2-productos.png)
![Paso 2 en el tema oscuro](frontend/img/l228-paso2-productos-oscuro.png)

![El carrito abierto: las líneas para corregir o quitar, con el subtotal](frontend/img/l228-carrito.png)

### Paso 3 — Entrega
- **Entrega** (se lleva al cliente) o **Recogido** (lo recoge: no lleva cargo de envío). Con «Entrega», el destino por omisión es la **dirección física del cliente**; se puede escoger otra dirección (consignatario) del cliente o crear una (en la web).
- **En consignación**: solo si la forma de pago es Consignación.
- **Fecha requerida** (opcional). En la app se escribe `AAAA-MM-DD`.
- **Avisos de precio**: si algún producto del carrito tiene un aviso (dos ofertas a la vez, o una oferta más cara que la lista general), el paso muestra el recuadro «Avisos de precio» con el SKU y el texto, y la nota «Se cobra siempre el precio más bajo; estos avisos son para que lo sepa, no impiden enviar el pedido.». Los de la orden guardada solo están en la respuesta de guardar el borrador; al volver a abrirlo quedan los del catálogo.
- **Resumen**: subtotal, IVU, **cargo de envío** (con su «Por qué») y **total**. Lo calcula el servidor con la regla de la compañía y del cliente ([18 §20](18-ordenes-de-venta.md#20-cargo-de-envío-lote-222)); cambiar entrega/recogido o el destino guarda el borrador al momento para recalcular.
- **Productos que quedan en backorder (Lote 235)**: debajo del resumen, la lista de las líneas que piden más de lo disponible en el almacén de la orden, con «Backorder: N» y «Solo hay X disponibles en {almacén}; N quedan en backorder.», y la nota «Puede enviar el pedido así: se reserva lo disponible y lo demás queda en backorder hasta que llegue inventario.». No impide seguir.

![Paso 3: entrega a la dirección física y el resumen con el cargo de envío de $12 y su porqué](frontend/img/l228-paso3-entrega.png)
![Paso 3 en el tema oscuro](frontend/img/l228-paso3-entrega-oscuro.png)

### Paso 4 — Autorización y envío
- **Backorder ya visto (Lote 235).** Si al enviar alguna línea pide más de lo disponible (el vendedor ya vio «Backorder: N» al capturar), el pedido se envía **aceptando el backorder** (`allowBackorder`), sin el diálogo de faltante: se reserva lo disponible y lo demás queda en backorder. El diálogo de faltante de siempre solo sale si el inventario cambió entre la captura y el envío (otra venta se lo llevó).
- **Nombre de quien autoriza** (hasta 150 caracteres) y **firma dibujada** (con el mouse o el dedo; se sube como imagen PNG). Son **obligatorios solo si el cliente exige la firma de quien autoriza el pedido**; si no, son opcionales.
- **Enviar pedido** pide confirmar quién envía:
  - **Web**: una ventana pide la **contraseña** (y el código de verificación, MFA, si la cuenta lo tiene).
  - **App**: pide **huella o rostro**; si el teléfono no tiene o no se confirma, pide la **contraseña** (más el código MFA si hace falta).
- Luego el servidor **solicita** la orden: valida inventario y crédito y reserva (igual que «Solicitar» en [18 §6](18-ordenes-de-venta.md#6-solicitar-la-orden-validar-y-reservar-lote-58)). Si falta inventario o se excede el crédito aparece el **diálogo de faltante** de siempre: se repite el envío con backorder, o con la autorización de crédito (`orders.credit_override`).
- **Pantalla final**: número `OV-#####`, total, y **Nuevo pedido**. Si el cliente es prospecto: «El cliente es prospecto: el pedido quedó reservado pero espera la aprobación del cliente y no se puede despachar todavía.»

![Paso 4: nombre de quien autoriza y la firma dibujada](frontend/img/l228-paso4-firma.png)
![Paso 4 en el tema oscuro](frontend/img/l228-paso4-firma-oscuro.png)

![«Confirme con su contraseña» al enviar el pedido](frontend/img/l228-contrasena.png)

![Pantalla final de un cliente prospecto: el número, el total y «espera la aprobación del cliente»](frontend/img/l228-enviado.png)
![Pantalla final en el tema oscuro](frontend/img/l228-enviado-oscuro.png)

**A 360 px** (celular en el navegador): los mismos pasos, sin desplazamiento horizontal de la página.

![Paso 1 a 360 px](frontend/img/l228-movil-paso1.png) ![Paso 2 a 360 px](frontend/img/l228-movil-paso2.png) ![Carrito a 360 px](frontend/img/l228-movil-carrito.png) ![Paso 3 a 360 px](frontend/img/l228-movil-paso3.png) ![Paso 4 a 360 px](frontend/img/l228-movil-paso4.png) ![Pedido enviado a 360 px](frontend/img/l228-movil-enviado.png)

**Después del envío** (recorrido completo): el supervisor de ventas aprueba al cliente en su ficha ([02 §1.8](02-clientes-y-contratos.md#18-cliente-prospecto-alta-mínima-y-aprobación-lote-222)) y la oficina despacha desde la orden, factura ([19](19-facturacion.md)) y la pasa a QuickBooks ([49](49-quickbooks.md)).

![La orden enviada: urgente, entrega, crédito a 30 días, quien autoriza con su firma, el kit con sus piezas y el cargo de envío](frontend/img/l228-orden.png)

**Pedido en consignación**: con la forma de pago Consignación y «Recogido», sin cargo de envío; el precio negociado del cliente gana a la lista general.

![Paso 3 de un pedido en consignación: Recogido, «En consignación» y sin cargo de envío](frontend/img/l228-consignacion-entrega.png)

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
| Almacén | Varios y no se indicó | Ya no es un error (Lote 235): se toma el **almacén principal**. `Almacén no encontrado.` / `El almacén está inactivo.` siguen saliendo si se manda uno malo. | 404 / 422 |
| Cantidad mayor que lo disponible | — | No es un error: aviso en pantalla «Solo hay X disponibles en {almacén}; N quedan en backorder.» (no bloquea). Al enviar sin aceptar el backorder: `No alcanza el inventario de {SKU}: pedido {n}, disponible {n}, faltante {n}.` | — / 409 |
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
| Kit de oferta que ya no está en una oferta vigente | Solo se vende mientras esté en alguna oferta publicada, activa y vigente que aplique al cliente | `El kit de oferta {SKU} no está en ninguna oferta publicada y vigente para este cliente: no se puede vender.` ([47 §7](47-precios-y-ofertas.md#7-validaciones-y-mensajes)) | 422 |
| Cambiar una pieza de un kit | Solo un kit de oferta; misma categoría, mismo precio, misma cantidad, pieza sustituible | Ver [18 §17.1](18-ordenes-de-venta.md#171-cambiar-una-pieza-del-kit-al-pedir-lote-230) (p. ej. `GXL no tiene el mismo precio de venta que GM: solo se sustituye por un producto del mismo precio.`) | 400 |
| Orden inexistente | — | `Orden de venta no encontrada.` | 404 |
| Sin el permiso | — | `Falta el permiso 'sales.create'.` | 403 |

Mensajes del alta de un cliente nuevo: [02 §1.8](02-clientes-y-contratos.md#18-cliente-prospecto-alta-mínima-y-aprobación-lote-222). Mensajes de precio y oferta: [47 §7](47-precios-y-ofertas.md#7-validaciones-y-mensajes). Mensajes de la sustitución de piezas: [18 §17.1](18-ordenes-de-venta.md#171-cambiar-una-pieza-del-kit-al-pedir-lote-230). En la pantalla, los avisos que aparecen junto al campo repiten estos mismos textos.

## 6. En la app móvil

- **Pedidos** aparece en el Inicio solo con `sales.create`. Con varias compañías, la pantalla usa la primera donde la persona tiene `sales.create` (o la que escoja en los botones de compañía, solo antes del primer guardado).
- **Mis pedidos**: estatus (Todos, Borradores, Solicitados, Parciales, Despachados, Cancelados), buscador por número, cliente o producto, y distintivos (Urgente, Cliente por aprobar, En consignación, Recogido, Con backorder). Ver la lista exige además `sales.view`; sin él la pantalla lo dice y deja hacer el pedido nuevo. La lista muestra los pedidos cuya persona que los pidió coincide con el nombre o el correo de la sesión, sobre los 200 más nuevos del filtro; dos personas con el mismo nombre se verían juntas.
- **Nuevo pedido**: los cuatro pasos de la sección 2, con la firma hecha con el dedo en el recuadro («Firmar de nuevo» si ya hay una guardada) y fotos con cámara o galería en el alta de cliente nuevo (si una foto falla, el cliente queda creado: «El cliente quedó creado, pero alguna foto no se pudo subir. Súbela después desde la ficha del cliente en la web.»).
- **Enviar pedido**: huella o rostro («Confirma el envío del pedido»); se pide la contraseña si el teléfono no tiene huella («Este teléfono no tiene huella ni rostro registrados: escribe tu contraseña para enviar el pedido.»), si no se confirmó («No se confirmó la huella o el rostro: escribe tu contraseña para enviar el pedido.») o si la verificación de la sesión venció («Por seguridad hace falta tu contraseña (la verificación de la sesión venció).»).
- **Sin señal**: no hay modo sin señal. Sin conexión no se avanza («No se pudo consultar. Se necesita señal.»); lo ya guardado queda como borrador en el servidor.
- **Lotes 229 a 232 en la app.** La app móvil no se cambió en esos lotes: sigue leyendo las ofertas por marca y el precio que resuelve el servidor (lo que se cobra es lo correcto), pero **no muestra** el nombre de la oferta, «Sin fecha de fin», los avisos de precio ni «Cambiar» en las piezas de un kit; eso es solo de la web. No se probó en un teléfono cómo se ven las tarjetas de oferta con los datos nuevos.
- **Lote 235 en la app.** Igual que en la web: con varios almacenes el paso 1 trae **marcado el almacén principal** (se puede cambiar); cada producto, oferta y línea del carrito dice «Disponible: N»; pedir de más queda marcado «Backorder: N» con su texto (no bloquea) y sale también en el resumen de la entrega; la nota «Hay disponible en …» de otros almacenes; y al enviar, si alguna línea quedó marcada, el pedido se manda aceptando el backorder sin volver a preguntar.
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
- **No me sale «Cambiar» en una pieza del kit.** La pieza está marcada «No sustituible», no tiene categoría o precio de venta, o no hay otro producto activo de la misma categoría exacta y el mismo precio de venta (ni que ya sea otra pieza del kit). Lo arregla quien administra los productos (categoría y precio) o el kit (la marca).
- **La tarjeta de la oferta dice que se cobra el precio negociado.** El cliente tiene un precio de lista más bajo que el de la oferta: entre los dos gana el más bajo.
- **Salió un aviso de precio en el carrito.** Dos ofertas a la vez, o una oferta más cara que la lista general. Es informativo: se cobra el precio más bajo y se puede enviar el pedido.
- **El kit de la oferta ya no aparece.** Ya no está en ninguna oferta publicada y vigente para ese cliente: su oferta terminó, se desactivó o volvió a borrador ([47 §4.4](47-precios-y-ofertas.md#44-kits-de-oferta-y-la-biblioteca-lote-231)); si se extiende, vuelve. Pedirlo fuera de vigencia da el error 422 de [47 §7](47-precios-y-ofertas.md#7-validaciones-y-mensajes).
- **Pedí un kit armado y dice que no alcanza.** No hay kits armados suficientes (existencia menos reservado). Quien tenga `inventory.adjust` puede armar más desde la ficha del kit (pestaña «Armados»); si no, el faltante se deja en backorder hasta que haya kits.
- **El kit armado no me deja cambiar piezas.** Es lo normal: un kit armado ya tiene sus piezas dentro. Cambiar piezas solo existe en los kits de oferta.
- **Quedó un borrador sin terminar.** Está en Órdenes de venta (o «Mis pedidos»); ábralo para seguirlo o cancélelo.

Los mensajes están en la [FAQ](faq.md#lotes-223-y-226--pedido-guiado-web-y-app-móvil), en [Lote 229](faq.md#lote-229--ofertas-como-entidad-aparte-kits-de-oferta-y-foto-del-producto), en [Lote 230](faq.md#lote-230--medidas-y-peso-del-producto-no-sustituible-y-cambiar-piezas-de-un-kit), en [Lote 231](faq.md#lote-231--kit-de-oferta-con-biblioteca) y en [Lote 232](faq.md#lote-232--kit-armado-con-inventario-armar-y-desarmar).

## 9. Para soporte

- No hay tablas nuevas para el paso: es la misma `SalesOrder`. Las columnas `AuthorizedByName`, `SignatureFileId` y `SubmittedWithAuth` las puso el Lote 222; la firma es un `StoredFile` de clase SIGNATURE (dueño `SALES_ORDER`), cifrado.
- El evento REAUTH con `stage = sales_submit` (método, número de orden, sesión) dice quién envió y desde qué sesión o teléfono (Sistema → Auditoría y seguridad).
- Web: `web-app/src/features/sales/wizard/*`. App: `app-movil/src/app/(auth)/NewSalesOrder` y `SalesOrders`. Las reglas del carrito y de los pasos están **copiadas** en la app (`salesOrderRules.ts`): un cambio en la web hay que repetirlo allá.
