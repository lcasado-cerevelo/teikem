# 47 — Listas de precios y ofertas

<!-- kemi:staff -->

Lote 221 (2026-11-07) — pedido de vendedores, parte P1; **rehecho en el Lote 229 (2026-11-09)**. El **precio de lo que se vende** deja de teclearse: sale de una **lista de precios** que arma el supervisor de ventas o de una **oferta** vigente. Desde el Lote 229 la **oferta es una cosa aparte** de las listas y de los kits, como la circular de una tienda («válido del … al …»): tiene nombre, foto, vigencia y su propio listado de productos o kits con precio de oferta. Ya **no** es un kit marcado como promoción. Desde el **Lote 231 (2026-11-10)** los kits que se arman al pedir viven en una **biblioteca de kits de oferta**: se crean una vez y se **reutilizan** de una oferta a otra («Usar un kit anterior»). Este capítulo es para el supervisor de ventas, la oficina y soporte. El pedido que usa estos precios se describe en [48 — Pedido guiado](48-pedido-guiado.md) y la orden en [18 — Órdenes de venta](18-ordenes-de-venta.md).

Los tres conceptos, separados:

| Concepto | Qué es | Lleva ofertas |
|---|---|---|
| **Producto** | Lo que se vende, con su precio de venta. Un **kit es un producto más**, formado por otros productos, con su precio normal; se vende también suelto. | No |
| **Lista de precios** | Los precios de siempre (o negociados por cliente), con vigencia. Se puede manejar una o varias. Al agregar un renglón se escoge un producto o un kit. | **No** |
| **Oferta** | Nombre, foto, vigencia y un listado de productos o kits, cada uno con su precio de oferta. Aplica a todos los clientes o a clientes escogidos. | — |

La pantalla está en **Contabilidad → Precios y ofertas** (`/money/prices`), justo antes de «Ventas». El ítem solo se ve con `sales.pricelist` (Admin de compañía y Supervisor de ventas; el vendedor con solo `sales.view` no la ve). Tiene **dos secciones**: **Ofertas** (arriba, con el botón **Nueva oferta**) y **Listas de precios** (con el botón **Nueva lista de precios**). El enlace viejo `/warehouse/products?tab=prices` lleva a esta pantalla.

> **Capturas pendientes (Lote 229).** Las capturas de abajo son del recorrido del lote 228 (2026-11-07, datos de demostración), cuando la oferta era un kit marcado como promoción. La pantalla ahora tiene las dos secciones, y la ventana de la oferta (datos, foto, clientes, renglones, «Crear kit para esta oferta» y avisos) todavía no tiene captura. Se retomarán con el recorrido de Playwright; la captura `l228-kit-promocion.png` (el antiguo editor del kit con la marca de promoción) ya no corresponde a nada y se dejó de citar. **Lote 231:** la ventana del kit de oferta («Usar un kit anterior» con la biblioteca y «Crear un kit nuevo») tampoco tiene captura todavía.

![Pantalla «Precios y ofertas» (captura anterior al Lote 229): la oferta como tarjeta con su foto y la lista de listas](frontend/img/l228-precios-ofertas.png)
![La misma pantalla en el tema oscuro (captura anterior al Lote 229)](frontend/img/l228-precios-ofertas-oscuro.png)

## 1. Quién puede y dónde

- Módulo **Órdenes de venta (`SALES_ORDERS`)**. Con el módulo apagado todo el API de listas y ofertas responde 403. La foto del producto se rige por el módulo de inventario (`WMS_LOTSERIAL`).
- Permisos:
  - **`sales.view`** — ver ofertas, listas, el catálogo del pedido guiado y **las fotos de los productos** (aunque no se tenga `inventory.view`). La pantalla «Precios y ofertas» en sí exige `sales.pricelist`.
  - **`sales.pricelist`** — crear, publicar, cambiar y desactivar listas **y ofertas**; poner o quitar la foto de la oferta y la de sus renglones; escoger los clientes; crear, reutilizar o cambiar los kits de oferta (la biblioteca). Implica `sales.view`. Lo trae el rol **Supervisor de ventas** (`SalesSupervisor`) y el **Admin de compañía**.
  - **`inventory.manage`** — subir, cambiar o quitar la **foto del producto** (en el editor de producto; ver [06 §2.5](06-inventario-y-almacen.md#25-foto-del-producto-y-categoría-con-subcategoría-lote-229-2026-11-09)).
  - **`sales.price`** — sigue siendo la excepción: escribir en una línea de la orden un precio distinto del que salió de la lista o de la oferta. Solo el Admin de compañía, o quien él designe.
- Los Lotes 229 y 231 **no agregaron permisos nuevos**: las ofertas y la biblioteca de kits de oferta usan `sales.view` y `sales.pricelist`.
- Permiso `sales.pricelist` en compañías que ya existían: se agregó a los roles ya clonados del Admin de compañía; el rol «Supervisor de ventas» lo crea el administrador en Sistema → Roles si la compañía no lo tiene.

## 2. De dónde sale el precio de una línea

Al crear o editar una línea de una orden de venta (y en el pedido guiado), el servidor resuelve el precio así:

1. **Entre el precio negociado del cliente y la oferta vigente, gana el más bajo.** El *negociado* es el precio de una lista **publicada**, **vigente hoy**, que aplica a ese cliente y trae el producto (o el kit). La *oferta* es una oferta **publicada, activa y vigente hoy** que aplica al cliente (todos, o él entre los escogidos) y trae el producto. **A igual precio gana el negociado.**
2. Si el cliente **no** tiene precio negociado: entre la oferta y la **lista general** (publicada, vigente, para todos los clientes) gana el más bajo; sin oferta, la lista general.
3. Si tampoco hay lista: el **precio de venta del producto** (`Product.SalePrice`).
4. Ninguno: el producto **no tiene precio** y la línea se rechaza (ver sección 6).

`priceSource` dice de dónde salió: `OFFER`, `CLIENT_LIST`, `GENERAL_LIST`, `PRODUCT` o `NONE`.

Reglas que conviene saber:
- El vendedor **no teclea precios**. Un precio distinto del resuelto exige `sales.price` (403), salvo que sea el que la línea ya tenía en el borrador.
- La fecha del precio es **hoy**, al crear o editar la línea (no la fecha de la orden). Una orden ya hecha **no cambia** si después cambia la lista o la oferta: la línea guarda su precio.
- Si en un borrador se cambia el cliente sin tocar las líneas, la línea que tenía el precio resuelto del cliente anterior toma el del nuevo; la que tenía un precio excepcional (con `sales.price`) lo conserva.
- Si por datos viejos dos listas del mismo nivel traen el mismo producto en la misma fecha, gana la que empezó más tarde.
- Un **producto normal** que está en una oferta vale el precio de la oferta (si es el más bajo) en el pedido, aunque se pida desde la lista de su marca.

### Avisos de precio (no son errores)

Cuando dos precios chocan, **se cobra siempre el más bajo** y el sistema avisa. Los avisos no impiden guardar, publicar ni enviar el pedido: llegan con código 200.

| Aviso (código) | Cuándo | Dónde se ve | Texto (ejemplo con SKU y precios) |
|---|---|---|---|
| Dos ofertas a la vez (`TWO_OFFERS`) | Un producto está en dos ofertas vigentes para el mismo cliente | Pedido guiado y orden (junto al producto, en la línea del carrito y en el recuadro «Avisos de precio» del paso de entrega) | `El producto GM está en dos ofertas a la vez: «A» a $9.00 y «B» a $11.00. Se aplica la más barata («A»).` Gana la más barata; a igual precio, la que empezó más tarde. |
| Oferta más cara que la lista general (`OFFER_ABOVE_GENERAL`, en el pedido) | El precio de oferta es mayor que el de la lista general vigente | Igual que el anterior | `La oferta «A» pone GM a $12.00, más caro que la lista general ($10.00): se aplica el precio más bajo.` |
| Oferta más cara que la lista general (`OFFER_ABOVE_GENERAL`, al armar la oferta) | Al crear, cambiar o publicar la oferta, un renglón sale más caro que la lista general (vigente cuando la oferta empieza, u hoy si ya empezó) | En la ventana de la oferta, recuadro «Avisos (no impiden guardar)» | `El producto GM sale en esta oferta a $12.00, más caro que en la lista general ($10.00): se aplicará el precio más bajo.` |
| Choque con otra oferta más barata (`CHEAPER_OFFER`, al armar la oferta) | Otra oferta **publicada** y activa, en fechas y clientes que se cruzan (una «para todos» se cruza con cualquiera), trae el mismo producto **igual o más barato** | En la ventana de la oferta, en el mismo recuadro | `El producto GM también está en la oferta «B» (del 01/11/2026 al 30/11/2026) a $9.00, igual o más barata que aquí ($10.00): mientras las dos estén vigentes se aplica el precio más bajo.` |

Notas sobre los avisos:
- Los de la oferta (`CHEAPER_OFFER`, `OFFER_ABOVE_GENERAL`) se calculan **al leer** la oferta, en toda respuesta (crear, cambiar, publicar, agregar renglón, abrirla). Una oferta **desactivada** no genera avisos en otras.
- Los del pedido salen en el catálogo (al escoger el producto) y al guardar el borrador. Al volver a abrir un borrador solo quedan los del catálogo, porque los de la orden no se guardan.
- Si lo que cobra el pedido es el precio **negociado** del cliente (más bajo que la oferta), la tarjeta de la oferta lo dice: «Oferta: $9.00. Se cobra su precio negociado, que es más bajo.» (con el precio de la oferta).

## 3. Listas de precios

**Qué hace.** Una lista tiene nombre, vigencia (**desde** obligatorio, **hasta** opcional: vacío = sin fin), notas (hasta 500 caracteres), los **renglones** (producto o kit → precio, con foto opcional) y a quién **aplica**: a todos los clientes (lista general) o solo a clientes escogidos (precios negociados: guantes, cajas, monitor, tirillas). **Una lista no lleva ofertas**: las promociones con vigencia, foto y precio especial van en una oferta (sección 4).

**Quién puede.** Ver: `sales.view`. Todo lo demás: `sales.pricelist`. Módulo `SALES_ORDERS`.

**Cómo se usa (pantalla).** **Contabilidad → Precios y ofertas** → sección **Listas de precios**.
1. **Nueva lista de precios**: nombre, «Vigente desde», «Hasta (opcional)», notas y la casilla «Aplica a todos los clientes». **Crear lista** la deja en **borrador**.
2. En la ventana de la lista: **Guardar datos**; **Renglones** (escoger **producto o kit**, escribir el precio, **Agregar**; cada renglón se puede cambiar de precio, quitar o ponerle foto); **Clientes de la lista** (**Agregar cliente**; solo si no es general).
3. **Publicar**: desde ese momento la lista da precios en sus fechas. **Volver a borrador** la saca de uso. **Desactivar** / **Reactivar** la retiran o la devuelven (nunca se borra).
4. La lista de listas muestra nombre, vigencia, estado (Borrador / Programada / Vigente / Vencida), «Aplica a» y número de renglones; filtra por estado, busca por nombre y puede **Mostrar desactivadas** (el filtro vale para ofertas y listas).

**Cómo se usa (API).** `/api/v1/price-lists`:

| Acción | Llamada | Permiso |
|---|---|---|
| Ver listas (`q`, `state`, `includeInactive`) / una lista | `GET /price-lists` · `GET /price-lists/{publicId}` | `sales.view` |
| Crear en borrador | `POST /price-lists` `{ "name": "Precios negociados: guantes", "validFrom": "2026-11-10", "validTo": null, "appliesToAll": false, "notes": "…" }` | `sales.pricelist` |
| Editar (lo que no viene no cambia; `clearValidTo: true` quita la fecha final) | `PATCH /price-lists/{publicId}` | `sales.pricelist` |
| Publicar / volver a borrador | `POST …/publish` · `POST …/unpublish` | `sales.pricelist` |
| Desactivar / reactivar | `POST …/deactivate` · `POST …/reactivate` | `sales.pricelist` |
| Agregar o cambiar renglones | `POST …/items` `{ "items": [ { "productPublicId": "…", "price": 12.5 } ] }` | `sales.pricelist` |
| Cambiar el precio / quitar un renglón | `PATCH …/items/{itemId}` `{ "price": 11 }` · `DELETE …/items/{itemId}` | `sales.pricelist` |
| Foto del renglón (JPG, PNG, WEBP o GIF) | `POST …/items/{itemId}/image` (multipart `file`) · `DELETE …/items/{itemId}/image` | `sales.pricelist` |
| Agregar / quitar clientes | `POST …/clients` `{ "clientPublicIds": ["…"] }` · `DELETE …/clients/{clientPublicId}` | `sales.pricelist` |

**Campos y reglas.**

| Campo | Regla |
|---|---|
| Nombre | Obligatorio, hasta 120 caracteres. |
| Vigente desde | Obligatorio. |
| Hasta | Opcional; no puede ser anterior al «desde». Los dos extremos cuentan como vigentes. |
| Notas | Hasta 500 caracteres. |
| Aplica a todos los clientes | Una lista general no lleva clientes escogidos; una de clientes necesita al menos uno para publicarse. |
| Renglones | Hasta 2000; solo productos o kits **propios** de la compañía y activos; sin repetir; precio de 0 en adelante con hasta 4 decimales. **Un kit de oferta no entra** en una lista (su precio va en una oferta). |
| Clientes | Hasta 2000; deben ser clientes activos. |
| Foto del renglón | JPG, PNG, WEBP o GIF (se comprueba por el contenido), hasta el tope del almacén de archivos. Por omisión el renglón muestra la foto del producto; si se le pone otra, esa manda. Quitarla solo la desliga: el archivo no se borra. |

**Cruces de listas.** Dos listas **publicadas** de la misma audiencia (dos generales, o dos de clientes que comparten un cliente) no pueden dar precio al mismo producto en fechas que se cruzan: el servidor lo rechaza con 409 y cita hasta 5 SKU. Una lista general y una de clientes **sí** pueden traer el mismo producto (gana la del cliente). Los cruces se comprueban al publicar y en cada cambio de una lista publicada (fechas, renglones, clientes, reactivar). Una lista de temporada encima de la base se permite si no repite productos. (Las **ofertas** no se cruzan con las listas ni entre sí en forma de error: sus choques son avisos, sección 2.)

![Ventana de una lista de precios de clientes escogidos: datos, clientes de la lista y renglones](frontend/img/l228-lista-precios.png)

## 4. Ofertas

### 4.1 Qué es una oferta

Una **oferta** es una entidad aparte con: **nombre** (hasta 120 caracteres), **foto**, **vigencia** («Vale desde» obligatorio, «Hasta» opcional), **notas** (hasta 500), a quién **aplica** (todos los clientes o clientes escogidos) y sus **renglones** (hasta 500). Cada renglón es un **producto o kit que ya existe** con su precio de oferta, un **kit de oferta anterior** de la biblioteca («Usar un kit anterior») o un **kit de oferta nuevo** («Crear un kit nuevo») (4.4). **Nace publicada** (Lote 234: las ofertas no tienen borrador) y da precios mientras esté activa y vigente.

**Sin fecha de fin.** Si «Hasta» queda en blanco, **la oferta no vence**: vale hasta que alguien la desactive. La pantalla lo avisa en dos lugares: en la tarjeta («Sin fecha de fin: vale hasta que la desactive») y en la ventana, debajo del campo («“Hasta” está en blanco: la oferta no tiene fecha de fin y vale hasta que la desactive.»). Revise esto al crear una oferta de temporada.

### 4.2 Quién puede

Ver las ofertas: `sales.view`. Crear, cambiar, publicar, desactivar, poner fotos, escoger clientes y crear, reutilizar o cambiar kits de oferta: `sales.pricelist`. Módulo `SALES_ORDERS`. Los Lotes 229 y 231 no agregaron permisos.

### 4.3 Cómo se usa (pantalla)

**Contabilidad → Precios y ofertas → Ofertas.** Cada oferta es una tarjeta con su foto, nombre, estado, vigencia, a quién aplica y número de renglones. Se abre con un clic.

1. **Nueva oferta** → ventana con **Nombre de la oferta**, **Notas**, **Vale desde**, **Hasta (en blanco = sin fecha de fin)** y la casilla **Aplica a todos los clientes**. **Crear oferta** la deja **publicada desde ese momento** (Lote 234: **las ofertas no tienen borrador**): vale según sus fechas en cuanto tiene renglones («Oferta creada: ya vale según sus fechas. Agregue su foto y sus renglones.»). Si todavía no debe valer, póngale un «Vale desde» futuro.
2. **Foto de la oferta**: subir, cambiar o quitar (sección 5).
3. **Clientes de la oferta**: solo si se desmarcó «Aplica a todos los clientes» (**Agregar cliente**). Mientras no tenga ninguno, la oferta **no aplica a nadie** y la ventana lo avisa: «Esta oferta todavía no aplica a nadie: escoja al menos un cliente.»
4. **Renglones** (hay tres maneras, y la pantalla lo explica: «Agregue un producto que ya existe con su precio de oferta, o un kit de oferta: use uno anterior de la biblioteca (con el precio que quiera) o cree uno nuevo.»):
   - **Escoger un producto**: se busca por SKU o nombre y se escribe el **precio de oferta**; el renglón muestra al lado el precio de la lista general para comparar. El precio se cambia en su celda (al salir del campo) y el renglón se quita con su botón. Este buscador **ya no ofrece kits de oferta**: esos se agregan con los dos botones siguientes.
   - **Usar un kit anterior**: abre la **biblioteca de kits de oferta** (4.4). Se busca por SKU o nombre; cada kit muestra su foto, SKU, nombre, sus piezas y «Última oferta: «…» a $…» (o «Todavía no se ha usado en ninguna oferta»). Al escogerlo, **las piezas salen en solo lectura** («La composición de un kit anterior no se cambia; para otra composición, cree un kit nuevo.») y el **precio de oferta sale prellenado con el último** («Último precio: $41.94 (oferta «Enero»). Puede poner otro.»): **el precio sí se cambia**. **Agregar a la oferta** pone el renglón. Si el kit ya es renglón de esta oferta, sale «Ya está en esta oferta» y no se puede escoger otra vez.
   - **Crear un kit nuevo**: ventana con **SKU del kit**, **Nombre del kit**, **Precio de oferta del kit**, **Piezas del kit** (producto y cantidad; **Agregar pieza**; cada pieza lleva la casilla **No sustituible**) y la casilla **Ajustable al pedir** (nace desmarcada). La ventana muestra la «Suma de las piezas a su precio de venta», el «Ahorro para el cliente» (o «Ojo: la oferta sale … más cara que las piezas sueltas.»). **Crear kit** crea el producto, sus piezas y el renglón de un solo paso, y el kit **queda guardado en la biblioteca**: «Kit … creado, guardado en la biblioteca y agregado a la oferta.». Desde «Usar un kit anterior», **«Crear un kit nuevo con estas piezas»** pasa a esta pestaña con las piezas, el nombre y el precio ya puestos (el SKU en blanco), para el caso «el de noviembre, pero con otra pieza».
   - Las dos maneras de agregar un kit son pestañas de la misma ventana («Cómo agregar el kit»): se puede pasar de una a otra sin cerrarla.
   - Ejemplo: el kit normal es monitor + agujas; el kit de la oferta es monitor + agujas + tirillas, «al precio de las dos» (o las tirillas a mitad de precio). Para repetir la oferta de noviembre en enero: cree la oferta de enero y use **Usar un kit anterior**; no hay que teclear las piezas otra vez.
5. **Foto por renglón**: por omisión el renglón muestra la foto del producto; **Poner otra foto** la cambia y **Volver a la del producto** la quita.
6. **Desactivar** (pide confirmar: «La oferta deja de aplicarse en los pedidos nuevos; un kit de oferta solo se sigue vendiendo si está en otra oferta vigente. Los pedidos ya hechos no cambian y se puede reactivar.») y **Reactivar**. No hay «Publicar» ni «Volver a borrador» (Lote 234): para sacar una oferta de uso se **desactiva**. Solo una oferta que hubiera quedado en borrador antes del Lote 234 muestra un único botón **Publicar** para sacarla de ahí (el script de cambios ya publicó las que estaban activas).
7. Los **avisos** (sección 2) salen en el recuadro «Avisos (no impiden guardar)». No son errores.

El estado se ve en la tarjeta: **Programada (todavía no empieza)**, **Vigente**, **Vencida** o **Desactivada**, y, aparte, el chip **Se aplica hoy** cuando la oferta está dando precios.

### 4.4 Kits de oferta y la biblioteca (Lote 231)

Un **kit de oferta** es un kit que **se arma al pedir**: no tiene existencia propia; cuando un cliente lo pide, el sistema reserva y despacha **sus piezas**. Por dentro es un producto, pero está marcado como kit de oferta: **no sale en Almacén → Productos e inventario** y **no se crea allí** (para un kit con existencia, un **kit armado**, vea [06 §8b](06-inventario-y-almacen.md#8b-kits-armado-y-de-oferta-lotes-68-231-y-232-2026-11-10)).

**La biblioteca de kits de oferta.** Todo kit de oferta que se crea **queda guardado** en la biblioteca (la composición; el precio no, aunque se recuerda el último). **Ya no está atado a una sola oferta**: una oferta lo usa con un renglón normal (su precio), y el mismo kit puede estar en varias ofertas a la vez. No hay una pantalla aparte de la biblioteca: se ve desde la ventana de la oferta, en **Usar un kit anterior**. Los kits que el Lote 229 había atado a una oferta pasaron a la biblioteca.

- **Usar un kit anterior.** La **composición no se cambia** (piezas, cantidades, «No sustituible» y «Ajustable al pedir»): si quiere otra, **cree un kit nuevo** (hay un atajo que copia las piezas). El **precio sí se cambia** en cada oferta, y la pantalla sugiere el **último**: el del renglón más reciente que sigue puesto en una oferta activa; si no queda ninguno, el más reciente de todos. Un kit que nunca se ha usado no tiene precio anterior: hay que escribirlo.
- **Crear un kit nuevo.** SKU, nombre, precio, piezas y «Ajustable al pedir». El SKU no se repite para el mismo dueño. La categoría y la categoría de impuesto salen de la primera pieza (la categoría solo si no se da otra); el precio de venta del kit es el que se dé (si no, el precio de oferta con el que se creó).
- **Se vende solo mientras esté en alguna oferta publicada y vigente** para el cliente: una oferta **publicada, activa y vigente hoy** (los dos extremos cuentan; sin «hasta» no vence) que aplique al cliente (a todos, o a él entre los escogidos) con un renglón activo de ese kit. Fuera de eso no sale en el catálogo del pedido y pedirlo da el error 422 de la sección 7. Si el kit está en dos ofertas vigentes, se cobra la más barata y sale el aviso `TWO_OFFERS`.
- **Un kit de venta permanente es una oferta sin fecha de fin**: ponga el kit en una oferta con «Hasta» en blanco.
- **El kit no se borra ni se da de baja.** Cuando ninguna oferta lo sostiene, simplemente no se vende; las órdenes y facturas que lo usaron lo siguen viendo (la línea guarda su copia). Si una oferta se **extiende** (se cambia o se quita su fecha de fin), se reactiva o se publica otra vez, el kit **vuelve a estar disponible** solo. Al desactivar o volver a borrador una oferta, sus kits salen de la venta salvo que estén en otra oferta vigente.
- **La composición queda fija una vez usado.** El kit cuenta como **usado** desde que tiene un renglón en cualquier oferta (aunque se haya quitado o la oferta esté desactivada) o una línea en cualquier orden de venta. Desde la pantalla, un kit nace con su renglón: por eso **«Cambiar kit»** (en el renglón) cambia **el nombre** y deja las piezas en solo lectura con la nota «La composición de un kit anterior no se cambia…». El SKU no cambia y el precio se cambia en el renglón. Solo por el API se puede crear un kit en la biblioteca sin ponerlo en una oferta y corregir sus piezas mientras nunca se use. Mandar la misma composición (aunque en otro orden) no cuenta como cambio.
- **Sustitución de piezas y «Ajustable al pedir»: solo aquí.** Las piezas de un kit de oferta se pueden **sustituir** al pedir, salvo las marcadas «No sustituible» (ver [18 §17.1](18-ordenes-de-venta.md#171-cambiar-una-pieza-del-kit-al-pedir-lote-230)); la marca se pone al crear el kit en la ventana de la oferta. «Ajustable al pedir» nace desmarcado. Ninguna de las dos cosas existe en un kit armado ni en un producto normal.
- **No se pone en una lista de precios.** Su precio va en una oferta (422).
- **No aparece en Productos e inventario** (ni en sus conteos, filtros o exportación, ni en la importación, ni en lo que se manda a integraciones). Su ficha por identificador sigue respondiendo y las órdenes y facturas lo nombran.

### 4.5 Cómo se usa (API)

`/api/v1/offers` (módulo `SALES_ORDERS`):

| Acción | Llamada | Permiso |
|---|---|---|
| Ver ofertas (`q`, `state`, `includeInactive`) / una oferta | `GET /offers` · `GET /offers/{publicId}` | `sales.view` |
| Crear (nace **publicada**, Lote 234) | `POST /offers` `{ "name": "Circular de noviembre", "validFrom": "2026-11-10", "validTo": null, "appliesToAll": true, "notes": "…" }` | `sales.pricelist` |
| Editar (lo que no viene no cambia; `clearValidTo: true` quita la fecha final) | `PATCH /offers/{publicId}` | `sales.pricelist` |
| Publicar una oferta que quedó en borrador (de antes del Lote 234) · volver a borrador (solo por compatibilidad; la pantalla no lo ofrece) | `POST …/publish` · `POST …/unpublish` | `sales.pricelist` |
| Desactivar / reactivar | `POST …/deactivate` · `POST …/reactivate` | `sales.pricelist` |
| Foto de la oferta (JPG, PNG o WEBP) | `POST …/image` (multipart `file`) · `DELETE …/image` | `sales.pricelist` |
| Agregar o cambiar renglones («Usar un kit anterior» = un renglón con el `productPublicId` del kit; con un kit de oferta ya usado el `price` puede ir vacío y se pone el último) | `POST …/items` `{ "items": [ { "productPublicId": "…", "price": 9.5 } ] }` | `sales.pricelist` |
| Cambiar el precio / quitar un renglón | `PATCH …/items/{itemId}` `{ "price": 9 }` · `DELETE …/items/{itemId}` | `sales.pricelist` |
| Foto del renglón | `POST …/items/{itemId}/image` (multipart) · `DELETE …/items/{itemId}/image` | `sales.pricelist` |
| Agregar / quitar clientes | `POST …/clients` `{ "clientPublicIds": ["…"] }` · `DELETE …/clients/{clientPublicId}` | `sales.pricelist` |
| «Crear un kit nuevo» y ponerlo ya en esta oferta (queda en la biblioteca) | `POST …/kits` `{ "sku": "OFERTA1", "name": "Monitor + agujas + tirillas", "price": 41.94, "adjustable": false, "components": [ { "productPublicId": "…", "quantity": 1, "notSubstitutable": false } ] }` → `{ productPublicId, sku, offer }` | `sales.pricelist` |
| Ver la biblioteca de kits de oferta (`q` = el SKU o el nombre contiene; `includeInactive`; hasta 500) | `GET /offers/kits?q=` → por kit: piezas, `lastPrice`, `lastOfferName`, `offerCount` (en cuántas ofertas activas está hoy), `compositionLocked`, `effectiveImagePublicId` | `sales.view` |
| Ver un kit de la biblioteca | `GET /offers/kits/{productPublicId}` (404 si no es kit de oferta) | `sales.view` |
| Crear un kit en la biblioteca sin ponerlo en una oferta | `POST /offers/kits` `{ "sku": "…", "name": "…", "components": [ … ], "adjustable": false, "salePrice": 41.94 }` | `sales.pricelist` |
| Cambiar un kit de la biblioteca (el nombre siempre; las piezas y «ajustable» solo si nunca se usó) | `PATCH /offers/kits/{productPublicId}` `{ "name": "…" }` | `sales.pricelist` |
| Cambiar un kit desde una oferta (compatibilidad; preferir el `PATCH` de la biblioteca) | `PUT …/kits/{productPublicId}` `{ "name": "…", "components": [ … ] }` (el kit debe estar en esa oferta) | `sales.pricelist` |

Claves de la respuesta de una oferta: `hasEndDate` (false = sin fecha de fin) y `validityText` («desde el 09/11/2026, sin fecha de fin» o «del … al …»); `state` (`DRAFT`, `SCHEDULED`, `ACTIVE`, `EXPIRED`); `inEffect` (se aplica hoy); `warnings[]` (los avisos); y, por renglón, `isOfferKit` (el renglón es un kit de oferta; un mismo kit puede estar en varias ofertas), `generalListPrice`, `imagePublicId` (foto propia) y `effectiveImagePublicId` (la que se muestra).

### 4.6 Campos y reglas de la oferta

| Campo | Regla |
|---|---|
| Nombre | Obligatorio, hasta 120 caracteres. |
| Vale desde | Obligatorio. |
| Hasta | Opcional; en blanco = no vence. No puede ser anterior al «desde». Los dos extremos cuentan como vigentes. |
| Notas | Hasta 500 caracteres. |
| Aplica a todos los clientes | Una oferta para todos no lleva clientes escogidos; una de clientes escogidos no aplica a nadie mientras no tenga al menos uno. |
| Renglones | Hasta 500; productos o kits **propios** de la compañía y activos; sin repetir; precio de 0 en adelante con hasta 4 decimales. Si el producto ya está en la oferta, se le cambia el precio. Un kit de oferta de la biblioteca entra en cualquier oferta (también en varias a la vez); sin precio, toma el último con que se usó. |
| Clientes | Hasta 2000; deben ser clientes activos. |
| Foto | JPG, PNG o WEBP (sección 5). |

## 5. Fotos: del producto, del renglón y del kit

**La foto se guarda en el producto** (kits y kits de oferta incluidos) y se pone en el editor de producto, bloque **Foto** ([06 §2.5](06-inventario-y-almacen.md#25-foto-del-producto-y-categoría-con-subcategoría-lote-229-2026-11-09)). Los renglones de listas y ofertas traen **por omisión la foto del producto**, y se les puede poner otra.

**Cuál se muestra** (en la pantalla de precios y ofertas, en el catálogo del pedido guiado y en el cuadrito del producto), en este orden:
1. La foto del **renglón** (de la lista o de la oferta), si se le puso una.
2. La foto del **producto**.
3. Si es un **kit sin foto**, la de la **primera pieza (por orden) que tenga foto**.
4. Ninguna: el cuadrito sale vacío.

Quitar la foto de un renglón hace que vuelva a mostrarse la del producto.

| Quién | Qué puede |
|---|---|
| `sales.view` | **Ver** la foto de cualquier producto (también la de renglones y ofertas), **aunque no tenga `inventory.view`**. Los demás archivos del producto siguen exigiendo `inventory.view`. |
| `inventory.manage` | Subir, cambiar o quitar la **foto del producto** (la misma persona que edita el producto). |
| `sales.pricelist` | Subir, cambiar o quitar la foto de la **oferta**, la de sus **renglones** y la de los renglones de las **listas**. |

Reglas de la foto: **JPG, PNG o WEBP** (se comprueba por el contenido, no por la extensión; las listas de precios siguen aceptando también GIF en su renglón). El tope de tamaño es el del almacén de archivos (10 MB por omisión). Una foto con el lado mayor de más de **1200 px** se **reduce a 1200 px** en su mismo formato; si no se puede leer o reducir, se guarda tal cual. Quitar una foto solo la desliga: el archivo no se borra. Las fotos de promoción que ya existían (antes del Lote 229) se copiaron a la foto del producto donde faltaba. La importación de productos no tiene columna de foto.

El contenido de la foto se baja con `GET /files/{imagePublicId}`. `GET /api/v1/products/{publicId}/image` (`inventory.view` o `sales.view`) dice cuál es la foto propia (`imagePublicId`) y cuál se muestra (`effectiveImagePublicId`); `POST` / `DELETE` del mismo recurso (`inventory.manage`) la ponen o la quitan.

## 6. El catálogo del pedido

`GET /api/v1/sales-orders/catalog?clientPublicId=<cliente>` (`sales.view`) alimenta el pedido guiado: las **marcas** (`Product.Brand`) con productos activos, y por marca sus **ofertas vigentes** (tarjetas con foto) y la **lista completa** de productos y kits, cada uno con el precio ya resuelto para el cliente. Sin `clientPublicId`, los precios de la lista general y las ofertas para todos.

- Solo productos propios, activos y **sin número de serie**.
- Las marcas se agrupan sin distinguir mayúsculas; los productos sin marca van al final (`brand = null`).
- **Ofertas de una marca** = los renglones de ofertas publicadas y vigentes que aplican al cliente, **uno por producto** (el de la oferta más barata). Un **kit de oferta solo sale como tarjeta**, y solo mientras esté en alguna oferta publicada y vigente para el cliente. Un **producto normal en oferta** sale como tarjeta **y además** en la lista de su marca, con el precio resuelto.
- `isPromotion` = el producto está en una oferta vigente para el cliente; `price` y `priceSource` = lo que se cobrará (`OFFER`, o `CLIENT_LIST` si el negociado es más bajo); `offerPrice` = el precio de la oferta; `offerName`, `offerValidFrom`, `offerValidTo` = de qué oferta y hasta cuándo; `imagePublicId` = la foto ya resuelta (sección 5); `notices[]` = los avisos (sección 2) con su texto listo.
- `priceSource` `NONE` = **sin precio**: no se puede pedir sin `sales.price`.
- **«Nueva orden» ofrece los mismos kits de oferta que el pedido guiado** (Lote 234): en la ventana clásica de la orden, el selector de producto de cada línea trae, además de los productos, los kits de oferta de **este catálogo** para el cliente escogido (los de ofertas vigentes que le aplican hoy), con el precio de la oferta. Sin cliente escogido no ofrece ningún kit de oferta. Antes listaba cualquier kit de oferta y el servidor rechazaba al guardar los que no estaban en venta. Como una oferta nace publicada, un kit recién puesto en una oferta vigente sale en los dos sitios de inmediato.
- Cliente inexistente: 404 «Cliente no encontrado.»; cliente inactivo: 422 «El cliente está inactivo; no se le puede vender.»
- En la orden, la respuesta de crear o editar el borrador con líneas trae `linePrices[]` (de dónde salió cada precio y sus avisos); no se guarda en la base.

## 7. Validaciones y mensajes

**Listas de precios.**

| Campo / situación | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| Cuerpo | Obligatorio | `El cuerpo de la solicitud es obligatorio.` | 400 |
| Nombre de la lista | Obligatorio | `Indique el nombre de la lista de precios.` | 400 (`name`) |
| Nombre de la lista | Hasta 120 | `El nombre de la lista no puede pasar de 120 caracteres.` | 400 |
| Vigente desde | Obligatorio | `Indique desde qué fecha vale la lista.` | 400 (`validFrom`) |
| Hasta | No antes de «desde» | `La fecha «hasta» no puede ser anterior a la fecha «desde».` | 400 (`validTo`) |
| Notas | Hasta 500 | `Las notas no pueden pasar de 500 caracteres.` | 400 |
| Lista inexistente | — | `Lista de precios no encontrada.` | 404 |
| Renglón inexistente | — | `Renglón de la lista no encontrado.` | 404 |
| Lista desactivada | No se cambia | `La lista de precios está desactivada: reactívela para cambiarla.` | 422 |
| Renglones | Al menos uno | `Indique los productos y sus precios.` | 400 (`items`) |
| Renglones | Hasta 2000 | `Una lista admite como máximo 2000 renglones.` | 400 |
| Renglón sin producto | — | `Escoja el producto o kit de cada renglón.` | 400 |
| Renglón sin precio | — | `Indique el precio de cada renglón.` | 400 |
| Precio | 0 o más; hasta 4 decimales | `El precio no es válido.` / `El precio admite como máximo 4 decimales.` | 400 |
| Producto repetido | — | `El producto {SKU} viene repetido.` | 400 |
| Producto de un cliente de paso | Solo propios | `Solo se ponen en una lista productos propios de la compañía.` | 422 |
| Producto inactivo | — | `El producto {SKU} está inactivo: no se puede poner en la lista.` | 422 |
| Kit de oferta | Su precio va en una oferta | `El kit {SKU} es un kit de oferta: su precio va en una oferta, no en una lista de precios.` | 422 |
| Producto inexistente | — | `Producto no encontrado.` | 404 |
| Clientes | Al menos uno al agregar | `Escoja los clientes de la lista.` | 400 (`clientPublicIds`) |
| Clientes | Hasta 2000 | `Una lista admite como máximo 2000 clientes.` | 400 |
| Cliente inexistente | — | `Cliente no encontrado.` | 404 |
| Cliente inactivo | — | `El cliente está inactivo; no se le puede vender.` | 422 |
| Cliente que no está en la lista | — | `Cliente de la lista no encontrado.` | 404 |
| Clientes en una lista general | — | `La lista aplica a todos los clientes: no lleva clientes escogidos. Desmarque «aplica a todos» para escogerlos.` | 422 |
| Hacer general una lista con clientes | — | `La lista tiene clientes escogidos: quítelos antes de hacerla general (aplica a todos).` | 422 |
| Publicar sin renglones | — | `Agregue al menos un renglón antes de publicar la lista.` | 422 |
| Publicar sin clientes (y no general) / quitar el último cliente de una publicada | — | `Escoja al menos un cliente o marque que la lista aplica a todos los clientes.` | 422 |
| Publicar una publicada | — | `La lista ya está publicada.` | 422 |
| Volver a borrador un borrador | — | `La lista ya está en borrador.` | 422 |
| Desactivar / reactivar lo que ya está así | — | `La lista ya está desactivada.` / `La lista ya está activa.` | 422 |
| Cruce con otra lista publicada | Sin precios dobles | `La lista se cruza con «{otra}» en las mismas fechas y para los mismos clientes en: {SKU, …} y N más. Cambie las fechas o quite esos productos de una de las dos.` | 409 |
| Dos personas guardan a la vez | — | `La lista cambió al mismo tiempo. Recárguela e intente de nuevo.` | 409 |
| Foto sin archivo | — | `Adjunte la foto.` | 400 (`file`) |
| Foto del renglón que no es imagen | — | `La foto debe ser una imagen (JPG, PNG, WEBP o GIF).` | 400 |
| Foto demasiado grande | — | `El archivo no puede pasar de {n} MB.` | 400 |

**Ofertas.**

| Campo / situación | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| Cuerpo | Obligatorio | `El cuerpo de la solicitud es obligatorio.` | 400 |
| Nombre de la oferta | Obligatorio | `Indique el nombre de la oferta.` | 400 (`name`) |
| Nombre de la oferta | Hasta 120 | `El nombre de la oferta no puede pasar de 120 caracteres.` | 400 (`name`) |
| Vale desde | Obligatorio | `Indique desde qué fecha vale la oferta.` | 400 (`validFrom`) |
| Hasta | No antes de «desde» | `La fecha «hasta» de la oferta no puede ser anterior a la fecha «desde».` | 400 (`validTo`) |
| Notas | Hasta 500 | `Las notas no pueden pasar de 500 caracteres.` | 400 (`notes`) |
| Oferta inexistente | — | `Oferta no encontrada.` | 404 |
| Renglón inexistente | — | `Renglón de la oferta no encontrado.` | 404 |
| Cliente que no está en la oferta | — | `Cliente de la oferta no encontrado.` | 404 |
| Kit inexistente (al cambiarlo desde una oferta) | — | `Kit de la oferta no encontrado.` | 404 |
| Kit de la biblioteca inexistente, o que no es kit de oferta | — | `Kit de oferta no encontrado.` | 404 |
| Oferta desactivada | No se cambia | `La oferta está desactivada: reactívela para cambiarla.` | 422 |
| Renglones | Al menos uno | `Indique los productos y sus precios de oferta.` | 400 (`items`) |
| Renglones | Hasta 500 | `Una oferta admite como máximo 500 renglones.` | 400 (`items`) |
| Renglón sin producto | — | `Escoja el producto o kit de cada renglón.` | 400 |
| Renglón sin precio | Obligatorio; solo un kit de oferta que ya se usó puede ir sin precio (toma el último) | `Indique el precio de oferta de cada renglón.` | 400 |
| Precio | 0 o más; hasta 4 decimales | `El precio no es válido.` / `El precio admite como máximo 4 decimales.` | 400 (`price`) |
| Producto repetido | — | `El producto {SKU} viene repetido.` | 400 |
| Producto de un cliente de paso | Solo propios | `Solo se ponen en una oferta productos propios de la compañía.` | 422 |
| Producto inactivo | — | `El producto {SKU} está inactivo: no se puede poner en la oferta.` | 422 |
| Producto inexistente | — | `Producto no encontrado.` | 404 |
| Clientes | Al menos uno al agregar | `Escoja los clientes de la oferta.` | 400 (`clientPublicIds`) |
| Clientes | Hasta 2000 | `Una oferta admite como máximo 2000 clientes.` | 400 |
| Cliente inexistente | — | `Cliente no encontrado.` | 404 |
| Cliente inactivo | — | `El cliente está inactivo; no se le puede vender.` | 422 |
| Clientes en una oferta para todos | — | `La oferta aplica a todos los clientes: no lleva clientes escogidos. Desmarque «aplica a todos» para escogerlos.` | 422 |
| Hacer «para todos» una oferta con clientes | — | `La oferta tiene clientes escogidos: quítelos antes de hacerla general (aplica a todos).` | 422 |
| Publicar sin renglones (solo una oferta que quedó en borrador de antes del Lote 234, `POST …/publish`) | — | `Agregue al menos un renglón antes de publicar la oferta.` | 422 |
| Publicar sin clientes y sin ser para todos (solo una oferta que quedó en borrador de antes del Lote 234). Desde el Lote 234 **ya no** se rechaza quitar el último cliente ni desmarcar «aplica a todos»: la oferta queda sin aplicar a nadie hasta que se escoja un cliente | — | `Escoja al menos un cliente o marque que la oferta aplica a todos los clientes.` | 422 |
| Publicar una publicada | — | No es un error (Lote 234): `POST …/publish` responde la oferta tal cual | 200 |
| Volver a borrador un borrador (`POST …/unpublish`, solo por compatibilidad: la pantalla no lo ofrece) | — | `La oferta ya está en borrador.` | 422 |
| Desactivar / reactivar lo que ya está así | — | `La oferta ya está desactivada.` / `La oferta ya está activa.` | 422 |
| Dos personas guardan a la vez | — | `La oferta cambió al mismo tiempo. Recárguela e intente de nuevo.` | 409 |
| Kit de oferta sin precio | — | `Indique el precio de oferta del kit.` | 400 (`price`) |
| Kit de oferta sin piezas | — | `Un kit necesita al menos un componente.` | 400 (`components`) |
| Kit de oferta con SKU repetido | — | `Ya existe un producto con ese SKU para ese dueño.` | 409 |
| Piezas inválidas del kit | Las de siempre de los kits | Ver [06 §8b.3](06-inventario-y-almacen.md#8b3-reglas-y-mensajes) | 400 |
| Cambiar las piezas, las cantidades, «No sustituible» o «Ajustable al pedir» de un kit que ya se usó en una oferta (aunque se haya quitado) o en un pedido | La composición no cambia; el nombre sí | `La composición del kit {SKU} ya no se puede cambiar porque ya se usó en una oferta o en un pedido: cree un kit nuevo con las piezas que quiera.` | 422 |
| Cambiar con `PUT /offers/{id}/kits/{kit}` un kit que no está en esa oferta | — | `Ese kit no está en esta oferta: cámbielo desde la biblioteca de kits de oferta.` | 422 |
| Importar productos con el SKU de un kit de oferta | El kit se cambia en la oferta | `Ese SKU es un kit de oferta: no se cambia por la importación de productos, sino en Contabilidad → Precios y ofertas.` | fila con error (`sku`) |
| Foto de la oferta o del renglón sin archivo | — | `Adjunte la foto.` | 400 (`file`) |
| Foto que no es JPG, PNG o WEBP | — | `La foto debe ser una imagen JPG, PNG o WEBP.` | 400 (`file`) |
| Foto demasiado grande | — | `El archivo no puede pasar de {n} MB.` | 400 |
| Almacén de archivos sin configurar | — | `El almacén de archivos no está configurado (Files:MasterKey); avise a soporte.` | 422 |

**Pedido y permisos.**

| Campo / situación | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| Pedir un kit de oferta que no está en ninguna oferta publicada y vigente para el cliente | Solo se vende mientras esté en alguna oferta publicada, activa y vigente que aplique al cliente | `El kit de oferta {SKU} no está en ninguna oferta publicada y vigente para este cliente: no se puede vender.` | 422 |
| Cambiar o ajustar las piezas de un kit que no es de oferta | La sustitución y «Ajustable al pedir» solo valen en un kit de oferta | `El kit {SKU} no admite cambiar ni ajustar sus piezas al pedir: eso solo se puede en un kit de oferta.` | 400 |
| Producto sin ningún precio | — | `El producto {SKU} no tiene precio de venta: escriba el precio.` | 400 |
| Precio distinto del resuelto sin `sales.price` | — | `Cambiar el precio exige el permiso de precios (sales.price).` | 403 |
| Sin el permiso | — | `Falta el permiso 'sales.pricelist'.` | 403 |
| Módulo apagado | — | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 |

Los mensajes de la foto del producto (`Adjunte la foto.`, `La foto debe ser una imagen JPG, PNG o WEBP.`) también salen al subirla desde el editor del producto.

## 8. Estatus y transiciones

**Lote 234 — las ofertas no tienen borrador.** Una oferta nace publicada y vale según sus fechas; se saca de uso con **Desactivar**. El estado «Borrador» y las transiciones Borrador ↔ Publicada de las tablas de abajo valen para las **listas de precios**; en una oferta solo queda «Borrador» si venía de antes del lote (se publica con su botón **Publicar**).

Ni la lista ni la oferta **usan el motor de estatus**: su estado sale de «publicada» y de las fechas, y solo se auditan publicar, volver a borrador y desactivar (bitácora `PRICE_LIST` y `OFFER`).

| Estado que se ve | Cuándo | ¿Da precios en pedidos nuevos? |
|---|---|---|
| **Borrador** | No está publicada | No |
| **Programada** (en la oferta: «Programada (todavía no empieza)») | Publicada, «desde» es futuro | No (todavía) |
| **Vigente** | Publicada y hoy cae entre «desde» y «hasta» (sin «hasta»: desde «desde» en adelante) | Sí |
| **Vencida** | Publicada y «hasta» ya pasó (una oferta sin «hasta» nunca vence) | No |
| **Desactivada** | `IsActive = 0` (cualquiera de las anteriores) | No; no se puede cambiar |

Transiciones de la **lista** y de la **oferta** (las mismas, con los mensajes de la sección 7):

| De → a | Quién | Qué valida | Efecto |
|---|---|---|---|
| Borrador → Publicada | `sales.pricelist` | Al menos un renglón; clientes escogidos si no es para todos. En la lista, además, sin cruces con otras publicadas | Empieza a dar precios en su vigencia. En la oferta, sus kits de oferta se pueden vender (si el cliente la recibe) |
| Publicada → Borrador | `sales.pricelist` | Que esté publicada | Deja de dar precios a los pedidos nuevos; los ya hechos no cambian. Sus kits de oferta salen de la venta, salvo los que estén en otra oferta vigente |
| Cualquiera → Desactivada | `sales.pricelist` | Que esté activa | Igual que borrador, y queda sin edición |
| Desactivada → Activa | `sales.pricelist` | Que esté desactivada; en la lista, si estaba publicada, que no se cruce con otra | Vuelve a su estado anterior |

Qué bloquea cada estado: una lista u oferta **desactivada** no se edita (ni renglones, ni clientes, ni fotos, ni kits) hasta reactivarla; una **publicada** sí se edita (en la lista, cada cambio vuelve a comprobar los cruces; en la oferta, los choques solo avisan); borrador, programada y vencida no afectan precios. Un **kit de oferta** solo se vende si está en **alguna** oferta publicada, activa y vigente que aplique al cliente.

## 9. Preguntas frecuentes del módulo

- **El vendedor ve el producto «Sin precio» en el pedido.** No hay lista vigente (del cliente ni general) ni oferta que lo traiga, y el producto no tiene precio de venta. Póngalo en una lista publicada y vigente, o dele precio de venta al producto.
- **Publiqué la lista y el pedido sigue con el precio de antes.** Revise la vigencia (¿empieza más adelante?), que no esté en borrador o desactivada, y que el cliente esté en la lista si no es general. Un pedido ya hecho no cambia.
- **Dos precios para el mismo producto.** Entre el precio negociado del cliente y una oferta vigente gana el más bajo; sin oferta, una lista del cliente gana a la general. Dos listas de la misma audiencia no pueden coexistir: el servidor lo rechaza con el 409 de cruce.
- **La oferta no aparece en el pedido.** Debe estar **publicada**, **activa**, vigente hoy y aplicar al cliente (para todos, o él entre los escogidos). Un kit de la oferta solo sale mientras la oferta valga.
- **Dejé «Hasta» en blanco.** La oferta no vence. Desactívela o póngale fecha cuando termine la promoción.
- **Salió un aviso amarillo en la oferta.** Es un aviso de precio, no un error: otra oferta igual o más barata coincide, o el precio sale más caro que la lista general. Se cobra el más bajo; revise si es lo que quería.
- **Quitar un cliente de una lista publicada falla.** Es el último y la lista no es general: agregue otro o pásela a borrador primero. En una **oferta** sí se puede (Lote 234): queda sin aplicar a nadie hasta que escoja otro.
- **No encuentro el kit de la oferta en el pedido.** Revise que el kit esté en una oferta publicada, activa y vigente que aplique al cliente (un kit de oferta no se vende fuera de la vigencia, y tampoco aparece en Productos e inventario).
- **¿Dónde creo un kit?** Si se arma al pedir, en una oferta: **Crear un kit nuevo**. Si debe tener existencia en el almacén, en Productos e inventario → **Nuevo producto** → «¿Lo que va a crear es un kit?» → Sí ([06 §8b](06-inventario-y-almacen.md#8b-kits-armado-y-de-oferta-lotes-68-231-y-232-2026-11-10)).
- **¿Cómo repito en enero la oferta de noviembre?** Cree la oferta de enero y, en **Renglones**, use **Usar un kit anterior**: escoja el kit y confirme el precio (se sugiere el último). Las piezas no se teclean otra vez.
- **¿Puedo cambiar las piezas de un kit anterior?** No: la composición de un kit ya usado no cambia (se perdería lo que vieron las ofertas y los pedidos pasados). Use **Crear un kit nuevo con estas piezas** y cambie lo que necesite. El precio sí se cambia en cada oferta.
- **Quiero vender un kit siempre.** Póngalo en una oferta con «Hasta» en blanco (sin fecha de fin).
- **El kit no aparece en Productos e inventario.** Es normal: un kit de oferta vive en la biblioteca de kits de oferta, no en el inventario. Un kit con existencia es un kit armado.

Todos los mensajes anteriores están en la [FAQ del Lote 221](faq.md#lote-221--listas-de-precios-y-ofertas) (listas) en la [FAQ del Lote 229](faq.md#lote-229--ofertas-como-entidad-aparte-kits-de-oferta-y-foto-del-producto) (ofertas, avisos y fotos) y en la [FAQ del Lote 231](faq.md#lote-231--kit-de-oferta-con-biblioteca) (kits de oferta y su biblioteca).

## 10. Para soporte

- Listas: tablas `PriceList`, `PriceListItem` y `PriceListClient`; EntityType `PRICE_LIST` y el permiso `sales.pricelist` (sección «2026-11-07 — Lote 221» de `Diseño/logistica-db-update.sql`; propagación a roles clonados con la marca `L221_SALES_PRICELIST`).
- Ofertas (Lote 229): tablas `Offer`, `OfferItem` y `OfferClient`; EntityType `OFFER`; columnas `Product.ImageFileId` (foto del producto) y `Product.OfferId` (kit creado para una oferta, con índice filtrado; desde el Lote 231 ya no se usa); sección «2026-11-09 — Lote 229» del mismo archivo (idempotente). Sin permisos nuevos.
- Kits de oferta (Lote 231): columna `Product.IsOfferKit` (BIT, 0 por omisión) e índice filtrado `IX_Product_OfferKit`; la sección «2026-11-10 — Lote 231» de `Diseño/logistica-db-update.sql` (idempotente, sin permisos nuevos) marcó los kits que tenían `OfferId`. `Product.OfferId` **ya no se lee ni se escribe** (la columna se queda). El kit se sigue guardando como producto de tipo `KIT`; su vigencia es **calculada** (ver abajo).
- Las columnas `Product.IsPromotion`, `PromoFrom`, `PromoTo` y `PromoImageFileId` **ya no se leen**: se quedan en la base sin uso (la foto de promoción se copió a `ImageFileId` donde faltaba). Una «promoción» vencida ya no bloquea ventas.
- Las listas y las ofertas se exponen por `PublicId` y nunca se borran. Los cambios quedan en la bitácora (entidades `PRICE_LIST` y `OFFER`).
- Las fotos son archivos (clase DOCUMENT) cifrados en el almacén de archivos; se ven con `GET /files/{publicId}`. La foto del producto es un archivo del producto; la de la oferta y la de sus renglones tienen como dueño la oferta (`OFFER`).
- La reducción a 1200 px usa SkiaSharp; en un servidor Linux sin su librería nativa la foto se guarda tal cual (sin reducir).
- Un kit de oferta «fuera de venta» es **calculado** (el producto sigue con `IsActive = 1`): se vende mientras esté en alguna oferta publicada, activa y vigente para el cliente, y vuelve solo al extender o reactivar la oferta. La biblioteca (`GET /offers/kits`) lista los kits por SKU; `offerCount` dice en cuántas ofertas activas está hoy.
- Scripts de Solutions: `02-kit-oferta1.sql` crea el kit de la biblioteca «OFERTA1» y la oferta «OFERTA1» (publicada, para todos, desde hoy en hora de Puerto Rico y sin fecha de fin, a $41.94); `06-lista-precios-inicial.sql` ya no pone OFERTA1 en la lista general. Detalle en `docs/lote229-decisiones.md`.
