# 55 — Precios especiales y ofertas por cliente y por cadena

<!-- kemi:staff -->

Lote 247 (2026-11-26; Lote C del plan de Solutions). Tres cosas nuevas en **Órdenes de venta → precios**:

1. **«Precios especiales»**: un precio propio de un **cliente** o de una **cadena** (Walgreens, CVS…), con fecha «desde» y, si se quiere, «hasta». Vive en un panel de la ficha del cliente y de la ficha de la cadena.
2. **Las cadenas entran a las listas de precios y a las ofertas**: una lista o una oferta puede aplicar a una cadena entera, y una oferta puede **incluir** o **excluir** clientes y cadenas.
3. **La regla del precio cambia: gana siempre el más bajo** entre el precio del cliente, el de su cadena, la oferta y la lista general. Antes, el precio negociado del cliente ganaba sobre la lista general aunque fuera más caro.

> **El precio especial es un precio ANTES del IVU.** Las exenciones del cliente (certificado estatal, municipal, los dos, o exención total) se aplican **después**, sobre el precio que se cobra. Este lote no cambia el cálculo del impuesto. Pendiente de confirmar con el contable.

> **Gana siempre el precio más bajo.** Si un precio especial sale **más caro** que la lista general, se cobra el de la lista general y el sistema avisa («Más caro que la lista general»). No bloquea nada: corrija el precio especial.

Capturas: [L247](frontend/l247-precios-especiales.md). Cada mensaje de error está en la [FAQ](faq.md). Este capítulo completa el [47 — Listas de precios y ofertas](47-precios-y-ofertas.md) (que explica listas, ofertas y kits, y ya resume la regla de «gana el más bajo») y el [53 — Cadenas de clientes](53-cadenas.md).

## 1. Dónde está y quién puede

- **Dónde:**
  - **Listas → Clientes y contratos** → ficha del cliente → panel **«Precios especiales»** (`/catalog/clients`).
  - **Listas → Cadenas** → ficha de la cadena → paneles **«Precios especiales»** y **«Ofertas»** (`/catalog/chains`).
  - **Contabilidad → Precios y ofertas** (`/money/prices`): la sección «Cadenas» de la lista de precios, el interruptor **«Ver también los precios especiales»** y las cajas **«Incluir»** / **«Excluir»** de la oferta.
  - La orden de venta y el pedido guiado: el **origen** de cada precio.
- **Módulo:** Órdenes de venta (`SALES_ORDERS`). Con el módulo apagado, las rutas responden 403 con `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.`
- **Permisos** (no hay permisos nuevos):

| Qué | Permiso | Módulo |
|---|---|---|
| Ver el panel «Precios especiales» (cliente y cadena) y el panel «Ofertas» de la cadena | `sales.view` o `sales.pricelist` | `SALES_ORDERS` |
| Agregar, cambiar y quitar precios especiales | `sales.pricelist` | `SALES_ORDERS` |
| Poner o quitar cadenas en una lista de precios o en una oferta; cambiar el público de una oferta (incluir / excluir) | `sales.pricelist` | `SALES_ORDERS` |
| Ver las listas y ofertas por API (`includeSpecial`, `chainPublicId`) | `sales.view` | `SALES_ORDERS` |
| Leer la fuente de datos «Precios especiales» (`SPECIAL_PRICE`) en vistas, indicadores y gráficos | `sales.view` | `SALES_ORDERS` |
| Cobrar en una orden un precio distinto del que salió (precio manual) | `sales.price` (sin cambio) | `SALES_ORDERS` |

- Quien tiene `sales.view` sin `sales.pricelist` **ve** el panel «Precios especiales»; los botones de agregar, cambiar y quitar no salen. Por API, `POST`, `PATCH` y `DELETE` dan 403 con `Falta el permiso 'sales.pricelist'.` y queda un evento `PERMISSION_DENIED` en los eventos de seguridad.
- La pantalla «Precios y ofertas» sigue exigiendo `sales.pricelist` (ver el capítulo 47).
- El `TenantId` sale de la sesión: un cliente, una cadena, una lista, una oferta o un precio especial de otra compañía responde 404.
- **Ni el portal ni la app del cliente muestran la cadena ni los precios especiales como tales.** El cliente solo ve el precio que le toca.

## 2. De dónde sale el precio: gana el más bajo

Al crear o editar una línea (orden de venta, pedido guiado, catálogo), el servidor compara hasta **cuatro candidatos** y se queda con el **más bajo**:

| # | Candidato | Aplica si… |
|---|---|---|
| 1 | **Precio del cliente**: el de una lista publicada y vigente que incluye al cliente (por ejemplo su precio especial) | la lista trae el producto |
| 2 | **Precio de su cadena**: el de una lista publicada y vigente que incluye a la cadena del cliente (por ejemplo el precio especial de la cadena) | el cliente tiene cadena **activa** y la lista trae el producto |
| 3 | **Oferta**: la oferta vigente más barata que le aplica al cliente o a su cadena | la oferta trae el producto o el kit |
| 4 | **Lista general**: la lista publicada y vigente para todos los clientes | trae el producto |

Reglas:
- **Gana el precio más bajo**, venga de donde venga. No hay jerarquía: el precio del cliente **no** gana solo por ser del cliente.
- **A igual precio** se prefiere, en este orden: cliente, cadena, oferta, lista general. Es lo que dice el origen («Precio especial» antes que «Cadena X»).
- **Dentro de un mismo nivel también gana el más bajo.** Si un cliente tiene dos listas con el mismo producto vigentes a la vez (por ejemplo su precio especial y una lista de grupo), se cobra el menor; a igual precio, la lista que empezó más tarde y, a igual fecha, la más nueva.
- **El precio de venta del producto no compite.** Solo se usa si ninguno de los cuatro trae el producto. Un precio especial más caro que el precio del producto, sin lista general, se cobra igual.
- **Una cadena desactivada cuenta como «sin cadena»:** sus clientes dejan de recibir sus precios y ofertas.
- **Un cliente que cambia de cadena** recibe los precios de la cadena nueva desde ese momento. Los borradores conservan el precio de sus líneas (se recotizan solo si se cambia el cliente del borrador); las órdenes ya solicitadas conservan su origen.
- La **vigencia** se evalúa con la fecha de **hoy** al escribir la línea (la del borrador o la solicitud), no con la fecha de entrega.

**Ejemplo.** Cliente de la cadena Walgreens; el producto SPX-A vale 12 de venta. Lista general: 10. Precio especial de la cadena: 8. Precio especial del cliente: 7.50. Oferta vigente para todos: 9. Se cobra **7.50** (origen «Precio especial»). Si se quita el del cliente, se cobra **8** («Cadena Walgreens»). Si el de la cadena sube a 11, se cobra **9** («Oferta …») y la fila avisa que el de la cadena sale más caro que la lista general.

El origen (`priceSource`) puede ser `CLIENT_LIST` (precio del cliente), `CHAIN_LIST` (de su cadena), `OFFER`, `GENERAL_LIST`, `PRODUCT` o `NONE`.

### Avisos (no son errores)

Llegan con código 200 y no impiden guardar, publicar ni enviar.

| Aviso (código) | Cuándo | Dónde se ve | Texto (ejemplo) |
|---|---|---|---|
| Precio especial más caro que la lista general, en la lista (`SPECIAL_ABOVE_GENERAL`) | Un renglón de una lista que no es general (la dedicada, o una de clientes o cadenas) cuesta más que el de la lista general vigente | Ficha de la lista («Avisos de la lista») y panel «Precios especiales» («Avisos de precios especiales») | `Precio especial más caro que la lista general: SPX-B a $15.00 y en la lista general a $14.00. Se cobra el más bajo; corrija el precio especial.` |
| Precio especial más caro que la lista general, en el pedido (`SPECIAL_ABOVE_GENERAL`) | Al cotizar un producto cuyo precio del cliente o de su cadena sale más caro que la lista general | Catálogo, pedido guiado y, en el escritorio, la ventana de la orden (junto a cada línea del borrador) | `El precio especial «Precio especial — Walgreens SPX (desde el 26/10/2026, sin fecha de fin)» pone SPX-B a $15.00, más caro que la lista general ($14.00): se aplica el de la lista general. Corrija el precio especial.` (con la interfaz en inglés y las fechas de la compañía en mes/día/año, la web lo arma en inglés y con esas fechas; ver abajo) |

Los avisos de ofertas del capítulo 47 (`TWO_OFFERS`, `OFFER_ABOVE_GENERAL`, `CHEAPER_OFFER`) siguen igual. Los de este lote y esos viven juntos en el catálogo y el borrador.

> **Idioma y fechas de los textos de precios (corregido después del cierre del lote).** El servidor sigue mandando estos textos en español y con fecha día/mes/año, pero **como respaldo**: la web del personal los arma **en el idioma de la interfaz y con el formato de fecha de la compañía** a partir de los datos que el servidor manda junto al texto (código, SKU, precios, y para la lista dedicada su dueño y sus fechas). Se arman así: el **aviso de precio** (`TWO_OFFERS`, `OFFER_ABOVE_GENERAL` y `SPECIAL_ABOVE_GENERAL`; en el pedido guiado, en el borrador y en la ventana de la orden), el **aviso de la lista**, el **nombre de la lista dedicada** («Precio especial — {dueño} ({vigencia})», en la tabla de listas, en la ficha de la lista y en el aviso) y la **vigencia de una oferta** («del … al …» / «desde el …, sin fecha de fin»). Si falta algún dato, la web muestra el texto del servidor. **Siguen saliendo tal cual del servidor, en español:** los mensajes de error, los avisos de la ventana de la oferta («otra oferta más barata», «oferta más cara que la lista general» al armarla), el nombre guardado de la lista dedicada (el de la base y el del campo `PriceListName` de la fuente `SPECIAL_PRICE`) y cualquier otra pantalla que no sea la web del personal.

## 3. Precios especiales (ficha del cliente y de la cadena)

**Qué hace.** Guarda el precio que un cliente, o todos los clientes de una cadena, pagan por un producto o kit, con fecha «desde» y «hasta» opcional. Mientras vale, entra a la comparación «gana el más bajo».

**Quién puede.** Ver: `sales.view` o `sales.pricelist`. Agregar, cambiar y quitar: `sales.pricelist`. Módulo `SALES_ORDERS`.

**Cómo se usa (pantalla).**
- **Del cliente:** Listas → Clientes y contratos → el cliente → panel **«Precios especiales»**. La ficha arranca con los paneles plegados: ábralo con su título.
- **De la cadena:** Listas → Cadenas → la cadena → panel **«Precios especiales»**. Lo reciben **todos sus clientes que no tengan un precio propio más bajo**.
- Arriba del panel hay una fila para agregar: **Producto o kit** (buscador por SKU o nombre), **Precio**, **Desde** (hoy por omisión) y **Hasta** (opcional) y el botón **«Agregar precio especial»**. Debajo, la ayuda: «El precio es antes del IVU. Las exenciones del cliente se aplican después, sobre el precio que se cobra.»
- La lista de precios del panel tiene: **Producto**, **Precio**, **Desde**, **Hasta**, **Estado**, **Lista general** y **Se cobra hoy**. En una ficha angosta o en el celular, cada fila pasa a tarjeta. Se ordena por marca, SKU y «desde», y el botón **«Ordenar por»** cambia el orden.
- **Estado:** Vigente, Programada (todavía no empieza), Vencida o Borrador (ver sección 8). Las vencidas **no salen** hasta que se enciende **«Ver también los vencidos»**. Cuando faltan 14 días o menos, la fecha «hasta» sale en ámbar con «Vence en N d».
- **Lista general** es el precio de la lista general vigente para ese producto (o el que valdrá cuando empiece el precio especial). Si el especial es más caro, junto al precio sale un chip de aviso (triángulo ámbar) y el recuadro «Avisos de precios especiales».
- **Se cobra hoy** es lo que de verdad se cobra hoy por ese producto y **de dónde sale**: chip «Precio especial» (el del cliente), «Cadena {nombre}», «Oferta {nombre}», «Lista general», «Precio del producto» o «Sin precio». En la ficha de la cadena es lo que pagaría **un cliente de la cadena sin precio propio**.
- Cada fila tiene **Cambiar precio** (lápiz) y **Quitar** (papelera); quitar pide confirmar: «Se quita el precio especial de {SKU} · {nombre}. Desde ese momento se cobra el que corresponda (lista general u oferta).»
- **Solo en la ficha del cliente**, debajo de sus precios:
  - **«De su cadena «{nombre}»»** (con el enlace «Abrir la cadena»): los precios especiales vigentes y programados de su cadena, en **solo lectura** («Solo lectura: se cambian en la ficha de la cadena. Un precio propio más bajo gana sobre el de la cadena.»).
  - **«Listas de precios que lo incluyen»**: los nombres de las listas normales publicadas y vigentes que lo alcanzan, «por el cliente» o «por su cadena». Solo nombre, estado y número de renglones.
- Un cliente **inactivo** o una cadena **desactivada** no admite precios nuevos: el panel dice «Está inactivo: no admite precios nuevos.»

**Cómo se usa (API).** Todas las rutas responden la vista completa (`SpecialPricesDto`); `includeExpired=true` agrega los vencidos.

| Qué | Ruta | Permiso |
|---|---|---|
| Ver los del cliente | `GET /api/v1/special-prices/clients/{clientPublicId}?includeExpired=` | `sales.view` o `sales.pricelist` |
| Agregar | `POST /api/v1/special-prices/clients/{clientPublicId}` | `sales.pricelist` |
| Cambiar | `PATCH /api/v1/special-prices/clients/{clientPublicId}/{itemId}` | `sales.pricelist` |
| Quitar | `DELETE /api/v1/special-prices/clients/{clientPublicId}/{itemId}` | `sales.pricelist` |
| Lo mismo para la cadena | `…/special-prices/chains/{chainPublicId}` y `…/{itemId}` | igual |

Alta:

```json
POST /api/v1/special-prices/chains/{chainPublicId}
{ "productPublicId": "7f0c…", "price": 8.00, "validFrom": "2026-11-26", "validTo": null }
```

Cambiar solo el precio, mover las fechas o quitar el «hasta»:

```json
PATCH /api/v1/special-prices/clients/{clientPublicId}/{itemId}
{ "price": 7.50 }
{ "validFrom": "2026-12-01", "validTo": "2026-12-31" }
{ "clearValidTo": true }
```

`itemId` es el `itemId` de la fila que devolvió el `GET`. Respuesta: `ownerKind`, `ownerPublicId`, `ownerName`, `ownerIsActive`, `chainPublicId` / `chainName` (la cadena del cliente), `chainRowCount`, `rows` (los suyos), `chainRows` (los de su cadena, solo lectura), `clientLists` y `warnings`. Cada fila trae `price`, `validFrom`, `validTo`, `state`, `daysToExpire`, `generalListPrice`, `aboveGeneral`, `chargedPrice`, `chargedSource` y `chargedSourceName`.

**Campos y reglas.**

| Campo | Regla |
|---|---|
| `productPublicId` | Obligatorio. Producto propio de la compañía (no de un cliente), activo, y no un kit de oferta (el kit de oferta se cambia en una oferta). Un kit normal sí se puede. |
| `price` | Obligatorio. Antes del IVU. **Mayor que cero** (0 o negativo da 400 «El precio especial debe ser mayor que cero (hasta 4 decimales).»), hasta 4 decimales y no más de 9 999 999 999. Es regla **solo de los precios especiales**: las listas normales y las ofertas siguen aceptando 0. Vale al agregar y al cambiar el precio. |
| `validFrom` | Obligatorio. |
| `validTo` | Opcional (sin «hasta» = no vence). No puede ser anterior a «desde». **Al crear** no puede ser anterior a hoy; al editar sí (así se cierra un precio que ya corre). |
| Mismo producto, mismo dueño, mismas fechas | No es un error: **cambia el precio** de ese renglón. |
| Mismo producto, mismo dueño, fechas que se cruzan con otra vigencia | 409 (ver sección 9). Un precio de la cadena y uno del cliente **no** chocan: gana el más bajo. |
| Tope | 2000 renglones por lista dedicada. |

Qué pasa por dentro (útil para soporte):
- Cada **dueño** (cliente o cadena) tiene **una lista dedicada por vigencia** (par «desde / hasta»), publicada. Se llama «Precio especial — {dueño} ({vigencia})» y no pasa de 120 caracteres.
- Agregar un precio con fechas nuevas **crea** la lista dedicada de esas fechas; con las mismas fechas, **agrega el renglón** a la que ya existe.
- **Si esa lista estaba en borrador** (alguien la devolvió a borrador en Precios y ofertas), agregar un precio con esas fechas, o mover un renglón a esas fechas, **la vuelve a publicar**. Antes el precio nuevo caía en el borrador y no se cobraba (no se puede crear otra lista con las mismas fechas para el mismo dueño). Al publicarse, **vuelven a cobrar también los demás renglones** que la lista tenía en borrador. El servidor revisa el cruce de **todos** sus renglones con lo publicado: si alguno choca, 409 con el mensaje de cruce de la sección 9, **no se guarda nada** y la lista sigue en borrador. Cambiar solo el precio de un renglón de una lista en borrador no la publica (así está en el código; no hay prueba aparte).
- **Cambiar solo el precio** lo cambia en su lista. **Cambiar las fechas** pasa el renglón a la lista dedicada de las fechas nuevas (la crea si no existe); la lista que se queda sin renglones activos se **desactiva sola**.
- **Quitar** desactiva el renglón (queda en el historial); la lista sin renglones activos se desactiva sola.
- El motor de precios ve la lista dedicada como cualquier lista publicada: por eso gana el más bajo igual que con las demás.

## 4. Cadenas en las listas de precios

**Qué hace.** Una lista de precios que **no** es para todos puede aplicar a **clientes escogidos**, a **cadenas** o a las dos cosas. Una lista de cadena alcanza a los clientes de la cadena **mientras la cadena esté activa**.

**Quién puede.** `sales.pricelist`. Módulo `SALES_ORDERS`.

**Cómo se usa (pantalla).** Contabilidad → Precios y ofertas → abrir la lista → sección **«Cadenas»** (junto a «Clientes»): «Agregar una cadena» y la **×** de cada cadena. Sin cadenas dice «Sin cadenas.».

**Cómo se usa (API).**

```json
POST /api/v1/price-lists/{publicId}/chains
{ "chainPublicIds": ["3b1e…"] }
DELETE /api/v1/price-lists/{publicId}/chains/{chainPublicId}
```

Reglas:
- Hasta **500 cadenas** por lista. Las repetidas se ignoran. Una lista **general** (aplica a todos) no lleva cadenas: 422.
- Publicar exige al menos un cliente **o** una cadena (si no es general). Una lista publicada **no se queda sin cliente ni cadena**: primero se vuelve a borrador.
- Dos listas normales que comparten un cliente **o una cadena**, con el mismo producto y fechas que se cruzan, dan 409. Una lista de cliente y otra de cadena **no** chocan, aunque el cliente sea de esa cadena: gana el más bajo.

### Listas dedicadas («Precio especial de …»)

Son las listas que crea la pantalla «Precios especiales». **Se ocultan por omisión** en la lista de precios (pueden ser cientos). Con el interruptor **«Ver también los precios especiales»** (`GET /api/v1/price-lists?includeSpecial=true`; se recuerda) salen con el chip **«Precio especial de {dueño}»** y las columnas «Solo ese cliente» / «Solo esa cadena».

Al abrir una dedicada:
- Una nota dice: «Esta lista es el precio especial de «{dueño}»: su nombre, fechas y a quién aplica se cambian en «Precios especiales» de su ficha. Aquí puede cambiar precios, publicar o desactivar.», con el enlace **«Abrir la ficha»**.
- **Se pueden** cambiar precios, agregar o quitar renglones, poner fotos, cambiar las notas, **publicar, volver a borrador y desactivar**.
- **No se pueden** cambiar el nombre, las fechas, «aplica a todos», ni agregar o quitar clientes o cadenas: 422 (ver sección 9).
- Una dedicada **no choca** con una lista normal que incluya al mismo cliente: conviven y gana el más bajo.

## 5. Ofertas: incluir y excluir clientes y cadenas

**Qué hace.** El **público** de una oferta (a quién aplica) tiene tres modos, para toda la oferta:

| Modo (`audienceMode`) | A quién aplica | Pantalla |
|---|---|---|
| `ALL` | A todos los clientes | Chip «Todos los clientes»; las dos cajas vacías |
| `INCLUDE` | **Solo** a los clientes y cadenas escogidos | Caja **«Incluir»**; chip «Solo escogidos: N» |
| `EXCLUDE` | A todos **menos** los clientes y cadenas escogidos | Caja **«Excluir»**; chip «Todos menos N» |

Antes del lote solo existían «todos» y «solo escogidos». Las ofertas que eran «solo escogidos» pasaron a `INCLUDE` al aplicar la actualización.

**Quién puede.** Ver: `sales.view`. Crear, cambiar y escoger clientes y cadenas: `sales.pricelist`. Módulo `SALES_ORDERS`.

**Cómo se usa (pantalla).** Contabilidad → Precios y ofertas → abrir la oferta → sección **«A quién aplica»**: dos cajas, **«Incluir»** («Solo estos clientes y cadenas reciben la oferta.») y **«Excluir»** («Todos reciben la oferta menos estos clientes y cadenas.»). Cada una admite clientes (**«Agregar cliente»**) y cadenas (**«Agregar una cadena»**).
- **Llenar una caja deshabilita la otra** («Quite lo escogido en la otra caja para usar esta.»). Con las dos vacías, la oferta es para todos.
- Al agregar a una caja vacía, la pantalla cambia el público y agrega.
- **Quitar al último cliente o cadena de «Incluir» no se puede** (corregido después del cierre del lote). La pantalla no muestra la × del último y explica por qué: «Una oferta «solo para» los escogidos necesita al menos un cliente o una cadena: agregue otro antes de quitar el último, o desactive la oferta.» Por API, quitar al último cliente o cadena de una oferta `INCLUDE` da **409** con ese mismo texto y no cambia nada. Para sustituir al único, agregue primero al nuevo y luego quite al anterior.
- Una oferta «Incluir» **se queda «Incluir»**: ya no pasa sola a «para todos». Para convertirla en «todos menos algunos», desactívela y cree otra; mientras «Incluir» tenga a alguien, la caja «Excluir» está deshabilitada y lo dice: «Esta oferta es solo para los escogidos y no se queda sin ellos: para que sea para todos menos algunos, desactívela y cree otra.»
- **Quitar al último de «Excluir» sí se puede** y pide confirmar: **«La oferta quedará para todos los clientes.»** (botón «Dejarla para todos»). Si confirma, la oferta pasa a `ALL`.
- **Cambia una decisión anterior.** En el Lote 234 se había decidido que una oferta de escogidos podía quedarse sin nadie y, en la primera versión de este lote, que al quitar al último la oferta pasaba a «para todos». Las dos cosas se reemplazaron por el 409. Pendiente de que Luis lo revise (decisión 10 de `docs/lote247-decisiones.md`).
- Una oferta `INCLUDE` sin nadie escogido (recién creada, o anterior a este arreglo, que no trae migración) está publicada pero **no aplica a nadie**; la pantalla lo dice.
- La cadena en la oferta aparece como «Cadena {nombre}»; si está desactivada, en ámbar.

**Cómo se usa (API).**

```json
POST /api/v1/offers
{ "name": "Verano", "validFrom": "2026-11-26", "audienceMode": "INCLUDE" }
PATCH /api/v1/offers/{publicId}
{ "audienceMode": "EXCLUDE" }
POST /api/v1/offers/{publicId}/chains
{ "chainPublicIds": ["3b1e…"] }
DELETE /api/v1/offers/{publicId}/chains/{chainPublicId}
GET /api/v1/offers?chainPublicId={chainPublicId}
```

Reglas:
- `audienceMode` es `ALL`, `INCLUDE` o `EXCLUDE` (se acepta en minúsculas). Si solo viene `appliesToAll`: `true` es `ALL`, `false` es `INCLUDE`; con `false` sobre una oferta `EXCLUDE`, se queda en `EXCLUDE`. Si vienen los dos y no coinciden: 400.
- **Pasar a `ALL` con clientes o cadenas escogidos** da 422; **pasar entre `INCLUDE` y `EXCLUDE` con escogidos** da 422: quítelos primero.
- **Publicar:** una `INCLUDE` sin escogidos no se publica (422). Una `EXCLUDE` sin escogidos **sí**: vale para todos.
- Agregar clientes o cadenas a una oferta `ALL` da 422. Hasta **500 cadenas** y 2000 clientes por oferta. Las repetidas se ignoran.
- Quitar un cliente (`DELETE /api/v1/offers/{publicId}/clients/{clientPublicId}`) o una cadena (`…/chains/{chainPublicId}`) de una oferta `INCLUDE` a la que **no le quedaría ningún cliente ni cadena** da 409 y no cambia nada. En una `EXCLUDE` se puede vaciar: vale para todos.
- **Sin cliente** (catálogo genérico, sin escoger cliente): aplican las ofertas `ALL`, las `INCLUDE` que traen la cadena y las `EXCLUDE` que no la excluyen.
- `GET /offers?chainPublicId=` devuelve solo las ofertas **activas, publicadas y no vencidas** que le aplican a la cadena: para todos, incluyéndola, o excluyendo a otros sin excluirla a ella.
- Los avisos «choque con otra oferta más barata» (`CHEAPER_OFFER`) ahora consideran cadenas y exclusiones. Es una estimación conservadora: ante la duda avisa. No impide nada.

### El panel «Ofertas» de la ficha de la cadena

Listas → Cadenas → la cadena → **«Ofertas»**, solo lectura: **Oferta**, **Vigencia** («Desde el … sin fecha final» o «Del … al …»), **Cómo aplica** (**Todos**, **Incluida**, **Todos menos los excluidos**) y **Renglones**. Dice «Sin ofertas vigentes» si ninguna le aplica hoy. El enlace «Ver todas las ofertas en Precios y ofertas» abre la pantalla completa.

## 6. El origen del precio en la orden

- Cada línea de una orden guarda **de dónde salió su precio** cuando se escribe: el origen, la lista o la oferta, la cadena y el **precio resuelto**. Se guardan códigos, no nombres: la pantalla lee el nombre **actual** de la lista, la oferta o la cadena (si se renombra una lista, la orden muestra el nombre nuevo).
- En la orden y en el pedido guiado, cada línea muestra un **chip de origen**: «Cadena {nombre}», «Precio especial» (el del cliente), «Oferta «{nombre}»» o ninguno cuando sale de la lista general o del precio del producto. Una orden **ya solicitada** sigue diciendo su origen aunque la lista cambie después.
- Si alguien con `sales.price` escribió un precio distinto del resuelto, la línea es de **precio manual**: el servidor devuelve `isManualPrice = true`, la pantalla muestra «Precio manual» y, al lado, «Lista: {precio}».
- El origen se **vuelve a calcular** al cambiar el cliente del borrador (aunque el precio manual se conserva) y al cambiar el precio de una orden **solicitada** (con la resolución de hoy). Solicitar la orden no lo reescribe.
- **Las líneas de órdenes anteriores al lote no tienen origen** (columnas vacías): no muestran chip y no cuentan como manuales.
- En la API, `lines[]` de la orden trae `priceSource`, `priceListPublicId` / `priceListName`, `offerPublicId` / `offerName`, `priceChainPublicId` / `priceChainName`, `resolvedUnitPrice` e `isManualPrice`; `linePrices[]` y el catálogo traen `chainPublicId` / `chainName`.

## 7. Impuestos: el precio especial es antes del IVU

El impuesto se calcula sobre el precio **que se cobra**, venga de donde venga (precio especial, de cadena, oferta o lista). Las exenciones del cliente se aplican **después**. Ejemplo de las pruebas: precio especial 8.00, IVU estatal 10.5 % y municipal 1 %:

| Situación del cliente | Impuesto de la línea |
|---|---|
| Sin exención | 0.92 |
| Certificado municipal (no paga el municipal) | 0.84 |
| Certificado estatal (no paga el estatal) | 0.08 |
| Los dos certificados | 0.00 |
| Exención total | 0.00 |
| Exención total y un certificado parcial | 0.00 (gana la total) |
| Exención total vencida y un certificado municipal vigente | 0.84, con el aviso de la exención vencida |

Pendiente: el dueño va a confirmar con el contable si algún caso necesita otro trato (por ejemplo, precios con el IVU incluido). Los tipos de documento (estatal, municipal y total) y atar la exención a su documento llegan en un lote posterior.

## 8. Estatus y transiciones

**No hay estatus nuevos.** Ninguna acción de este capítulo cambia un estatus de orden ni usa el servicio de estatus. Una lista o un precio especial tiene un **estado que se calcula** a partir de si está publicada y de sus fechas:

| Estado | Cuándo | ¿Se cobra? |
|---|---|---|
| Borrador | La lista no está publicada | No |
| Programada | Publicada y todavía no llega su «desde» | No, hasta que empiece |
| Vigente | Publicada y hoy está entre «desde» y «hasta» (extremos incluidos; sin «hasta» = no vence) | Sí |
| Vencida | Publicada y ya pasó su «hasta» | No |

Acciones y qué dispara cada una:

| Acción | Quién puede | Qué valida | Efecto |
|---|---|---|---|
| Agregar un precio especial | `sales.pricelist` | Dueño activo, producto válido, precio mayor que cero, fechas, que no se cruce con otra vigencia del mismo producto y dueño (si la lista de esas fechas estaba en borrador, el cruce de todos sus renglones) | Crea la lista dedicada de esas fechas (publicada) o agrega el renglón a la que existe; si estaba en borrador, la vuelve a publicar |
| Cambiar el precio | `sales.pricelist` | Precio mayor que cero; que no se cruce | Cambia el renglón |
| Cambiar las fechas | `sales.pricelist` | «hasta» no anterior a «desde»; que no se cruce | Mueve el renglón a la lista dedicada de las fechas nuevas (si estaba en borrador, la publica); la que se queda vacía se desactiva |
| Quitar | `sales.pricelist` | El renglón es un precio especial vigente de ese dueño | Desactiva el renglón (queda en el historial); la lista vacía se desactiva |
| Publicar / Volver a borrador / Desactivar la lista dedicada (en Precios y ofertas) | `sales.pricelist` | Las de siempre de las listas | Cambia si se cobra o no |
| Desactivar la cadena | `sales.chain` | Cero clientes activos y **ninguna lista u oferta `INCLUDE` publicada y vigente con la cadena** (409 en cualquiera de los dos casos) | La cadena sale de las listas de elegir; sus clientes quedan «sin cadena» para los precios |

Qué queda bloqueado:
- **Cliente inactivo o cadena desactivada:** no admite precios especiales nuevos, ni cambios, ni quitar (422). Los precios que ya tenía se ven y siguen existiendo; los de una cadena desactivada **dejan de cobrarse** a sus clientes (cuentan como sin cadena).
- **Lista dedicada:** no se cambian su nombre, fechas ni a quién aplica desde Precios y ofertas (422).
- **Precio especial vencido:** no se cobra. Para volver a usarlo, agréguelo con fechas nuevas.
- **Oferta `ALL`:** no admite clientes ni cadenas escogidos (422).

## 9. Validaciones y mensajes

El mensaje sale tal cual en el campo `title` del error (y en rojo en la pantalla, salvo los que la pantalla avisa antes de enviar).

### Precios especiales (`/api/v1/special-prices/…`)

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| (cuerpo) | Debe venir | `El cuerpo de la solicitud es obligatorio.` | 400 |
| `productPublicId` | Obligatorio | `Escoja el producto o kit de cada renglón.` | 400 |
| `price` | Obligatorio | `Indique el precio de cada renglón.` | 400 |
| `price` | Mayor que cero (alta y cambio de precio) | `El precio especial debe ser mayor que cero (hasta 4 decimales).` | 400 |
| `price` | Hasta 9 999 999 999 | `El precio no es válido.` | 400 |
| `price` | Hasta 4 decimales | `El precio admite como máximo 4 decimales.` | 400 |
| `validFrom` | Obligatorio | `Indique desde qué fecha vale el precio especial.` | 400 |
| `validTo` | No anterior a «desde» | `La fecha «hasta» no puede ser anterior a la fecha «desde».` | 400 |
| `validTo` | Al **crear**, hoy o después | `La fecha «hasta» ya pasó: un precio especial nuevo tiene que valer hoy o después.` | 400 |
| `productPublicId` | Máximo 2000 renglones por lista | `Una lista admite como máximo 2000 renglones.` | 400 |
| Cliente | Existe en la compañía | `Cliente no encontrado.` | 404 |
| Cadena | Existe en la compañía | `Cadena no encontrada.` | 404 |
| Producto | Existe | `Producto no encontrado.` | 404 |
| `itemId` | Es un precio especial vigente de ese dueño | `Precio especial no encontrado.` | 404 |
| Cliente | Activo (para escribir) | `El cliente está inactivo; no se le puede vender.` | 422 |
| Cadena | Activa (para escribir) | `La cadena está desactivada: actívela o escoja otra.` | 422 |
| Producto | Propio de la compañía | `Solo se ponen en una lista productos propios de la compañía.` | 422 |
| Producto | Activo | `El producto {SKU} está inactivo: no se puede poner en la lista.` | 422 |
| Producto | No es kit de oferta | `El kit {SKU} es un kit de oferta: su precio va en una oferta, no en una lista de precios.` | 422 |
| Fechas | No se cruzan con otro precio del mismo producto y dueño | `La lista se cruza con «{lista}» en las mismas fechas y para los mismos clientes o cadenas en: {SKU, …}. Cambie las fechas o quite esos productos de una de las dos.` (cita hasta 5 SKU y «y N más») | 409 |
| Lista en borrador que se vuelve a publicar | Los renglones de la lista no se cruzan con lo publicado del mismo dueño | El mismo mensaje del cruce (arriba); no se guarda nada | 409 |
| Alta simultánea | Dos altas a la vez de la misma lista | `La lista cambió al mismo tiempo. Recárguela e intente de nuevo.` | 409 |
| Cambio simultáneo | Alguien cambió la lista a la vez | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Permiso | `sales.pricelist` para escribir; `sales.view` o `sales.pricelist` para ver | `Falta el permiso '…'.` | 403 |
| Módulo | `SALES_ORDERS` encendido | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 |

Avisos de la pantalla antes de enviar (no son del servidor): «Escoja el producto o kit.», «Escriba un precio mayor que cero (hasta 4 decimales).», «Escoja la fecha «desde».», «La fecha «hasta» no puede ser anterior a «desde».» y «La fecha «hasta» ya pasó: un precio especial nuevo tiene que valer hoy o después.».

### Cadenas en listas de precios (`/api/v1/price-lists/…`)

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `chainPublicIds` | Al menos una | `Escoja las cadenas de la lista.` | 400 |
| `chainPublicIds` | Máximo 500 | `Una lista admite como máximo 500 cadenas.` | 400 |
| Cadena | Existe en la compañía | `Cadena no encontrada.` | 404 |
| Cadena en la lista | Está en la lista (al quitar) | `Cadena de la lista no encontrada.` | 404 |
| Cadena | Activa | `La cadena está desactivada: actívela o escoja otra.` | 422 |
| Lista | No es general | `La lista aplica a todos los clientes: no lleva cadenas escogidas. Desmarque «aplica a todos» para escogerlas.` | 422 |
| Lista | No es dedicada (clientes y cadenas, nombre, fechas, «aplica a todos») | `Esta lista es el precio especial de «{dueño}»: el nombre, las fechas y a quién aplica se cambian en «Precios especiales» de su ficha.` | 422 |
| Publicar / quitar la última | Un cliente o una cadena si no es general | `Escoja al menos un cliente o una cadena, o marque que la lista aplica a todos los clientes.` | 422 |
| Hacerla general | Sin clientes ni cadenas escogidos | `La lista tiene clientes o cadenas escogidos: quítelos antes de hacerla general (aplica a todos).` | 422 |
| Cruce de listas | Mismo tipo, mismo producto, fechas que se cruzan | `La lista se cruza con «{lista}» en las mismas fechas y para los mismos clientes o cadenas en: …` | 409 |

### Ofertas: público y cadenas (`/api/v1/offers/…`)

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `audienceMode` | `ALL`, `INCLUDE` o `EXCLUDE` | `El público de la oferta debe ser ALL (todos), INCLUDE (solo los escogidos) o EXCLUDE (todos menos los escogidos).` | 400 |
| `appliesToAll` y `audienceMode` | Coinciden si vienen los dos | `“Aplica a todos” no coincide con el público escogido: indique solo el público (todos, solo los escogidos o todos menos los escogidos).` | 400 |
| `audienceMode` | Pasar entre `INCLUDE` y `EXCLUDE` sin escogidos | `Quite los clientes y cadenas de la oferta antes de cambiar entre incluir y excluir.` | 422 |
| `audienceMode` | Pasar a `ALL` sin escogidos | `La oferta tiene clientes o cadenas escogidos: quítelos antes de hacerla para todos.` | 422 |
| Publicar | `INCLUDE` con al menos un cliente o cadena | `Escoja al menos un cliente o una cadena, o marque que la oferta aplica a todos los clientes.` | 422 |
| `chainPublicIds` | Al menos una | `Escoja las cadenas de la oferta.` | 400 |
| `chainPublicIds` | Máximo 500 | `Una oferta admite como máximo 500 cadenas.` | 400 |
| Cadena | Existe en la compañía (también en `?chainPublicId=`) | `Cadena no encontrada.` | 404 |
| Cadena de la oferta | Está en la oferta (al quitar) | `Cadena de la oferta no encontrada.` | 404 |
| Quitar el último cliente o cadena | Una oferta `INCLUDE` conserva al menos uno | `Una oferta «solo para» los escogidos necesita al menos un cliente o una cadena: agregue otro antes de quitar el último, o desactive la oferta.` | 409 |
| Cadena | Activa | `La cadena está desactivada: actívela o escoja otra.` | 422 |
| Oferta | No es para todos (cadenas) | `La oferta aplica a todos los clientes: no lleva cadenas escogidas. Desmarque «aplica a todos» para escogerlas.` | 422 |
| Oferta | No es para todos (clientes) | `La oferta aplica a todos los clientes: no lleva clientes escogidos. Desmarque «aplica a todos» para escogerlos.` | 422 |

### Desactivar una cadena

| Regla | Mensaje | HTTP |
|---|---|---|
| Con clientes activos (se revisa primero) | `La cadena tiene 1 cliente activo: quítelo de la cadena antes de desactivarla.` (o `… N clientes activos: quítelos …`) | 409 |
| Con listas de precios (incluidos sus precios especiales) publicadas y vigentes, u ofertas `INCLUDE` publicadas y vigentes | `La cadena tiene precios especiales u ofertas publicados y vigentes ({N} listas de precios y {M} ofertas): quítela de ellos antes de desactivarla.` | 409 |

«Vigente» aquí es: activa, publicada y con «hasta» vacío o de hoy en adelante (una **programada** también bloquea). Las ofertas `EXCLUDE` no bloquean; las listas vencidas, en borrador o desactivadas, tampoco. Para quitar el bloqueo: en la ficha de la cadena, quite sus precios especiales; en Precios y ofertas, quítela de las listas y de las ofertas `INCLUDE` (o desactívelas).

### Avisos que no son errores

`SPECIAL_ABOVE_GENERAL`: ver los dos textos de la sección 2. Código 200.

## 10. Fuente de datos «Precios especiales» (`SPECIAL_PRICE`)

Para Vistas, Indicadores y Gráficos (análisis). Una fila por precio especial activo, con su estado **de hoy**: `OwnerKind` («Cliente» o «Cadena», en el idioma del usuario), `OwnerKindCode` (el código: `CLIENT` o `CHAIN`; úselo para filtrar), `OwnerName`, `ChainName`, `Sku`, `ProductName`, `Brand`, `Price`, `ValidFrom`, `ValidTo`, `State`, `DaysToExpire` (vacío = sin «hasta»; 0 = vence hoy; negativo = ya venció), `GeneralListPrice`, `AboveGeneral`, `PriceListName` y `OwnerIsActive` («Dueño activo»: el cliente o la cadena está activo; los precios que salen siempre están activos). Se lee con `sales.view` y el módulo `SALES_ORDERS` encendido. Incluye las vencidas y las en borrador (el campo `State` lo dice): filtre por `State` para ver solo las vigentes. Todavía **no hay aviso programado de «precio especial por vencer»**: llega con el Lote D.

> **Corregido después del cierre del lote:** el campo que antes se llamaba `IsActive` ahora es `OwnerIsActive`, y `OwnerKind` ya no trae `CLIENT`/`CHAIN` sino la etiqueta en el idioma del usuario (el código pasó a `OwnerKindCode`). Una vista, indicador o gráfico guardado que usara `IsActive`, o que filtrara `OwnerKind` por `CLIENT`/`CHAIN`, hay que editarlo para usar `OwnerIsActive` y `OwnerKindCode`.

## 11. Preguntas frecuentes del módulo

**El cliente tiene precio especial pero se le cobra la lista general. ¿Por qué?**
Porque el de la lista general es **más bajo o igual**. Gana siempre el precio más bajo. La fila lo marca con «Más caro que la lista general» y «Se cobra hoy» dice «Lista general». Baje el precio especial o quítelo.

**Se cobra la lista general y el precio especial es más barato.**
Revise el **Estado**: el precio especial solo se cobra **Vigente** (publicado y dentro de sus fechas). «Programada» todavía no empieza, «Vencida» ya pasó y **«Borrador»** significa que alguien volvió su lista dedicada a borrador (Precios y ofertas → «Ver también los precios especiales» → «Publicar»). Revise también que el cliente, o su cadena, estén **activos**: una cadena desactivada cuenta como sin cadena.

**La oferta no le sale a un cliente excluido (o sí le sale a uno que no escogí).**
Una oferta **`EXCLUDE`** vale para todos **menos** los clientes y cadenas escogidos; si el cliente está excluido, o su **cadena** está excluida, no le sale. Una oferta `INCLUDE` solo vale para los escogidos o para los clientes de las cadenas escogidas. Mire la sección «A quién aplica» de la oferta.

**No me deja quitar la última cadena (o el último cliente) de una oferta.**
Una oferta «Incluir» («solo para» los escogidos) no se queda sin nadie: el servidor responde 409 «Una oferta «solo para» los escogidos necesita al menos un cliente o una cadena: agregue otro antes de quitar el último, o desactive la oferta.» y la pantalla ya no muestra la × del último. Agregue primero a quien sigue y luego quite al anterior; o desactive la oferta. En una oferta «Excluir» sí se puede quitar al último: la oferta queda para todos, con confirmación.

**Puse precio 0 a un precio especial y no me deja.**
El precio especial debe ser mayor que cero (400 «El precio especial debe ser mayor que cero (hasta 4 decimales).»), en la pantalla y por API. Una lista normal o una oferta sí aceptan 0.

**La lista dedicada de un cliente estaba en «Borrador» y al agregar otro precio especial pasó a «Vigente». ¿Por qué?**
Agregar un precio especial con las mismas fechas de una lista dedicada en borrador la **vuelve a publicar** (y empiezan a cobrar también los renglones que tenía). Si sus renglones se cruzan con otro precio publicado del mismo dueño, no se guarda nada y da 409 (sección 9).

**¿Cómo cambio solo la fecha de un precio especial?**
En el panel, **Cambiar precio** (lápiz) y cambie «Desde» o «Hasta». El renglón pasa solo a la lista de esas fechas; si se cruza con otra vigencia del mismo producto para el mismo dueño, da 409. Para quitar el «hasta», déjelo vacío.

**¿Por qué no veo las listas «Precio especial de …» en Precios y ofertas?**
Están ocultas por omisión. Encienda **«Ver también los precios especiales»**. Se cambian desde la ficha del cliente o de la cadena; en la lista solo se pueden cambiar precios, publicar o desactivar.

**No me deja desactivar la cadena.**
Tiene clientes activos, o precios especiales, listas u ofertas `INCLUDE` publicados y vigentes. El mensaje dice cuántos. Quítelos (sección 9, «Desactivar una cadena») y vuelva a intentar.

**¿El precio especial lleva IVU?**
El precio especial es **antes** del IVU: el impuesto se suma después, sobre el precio que se cobra, y las exenciones del cliente (estatal, municipal o total) se aplican sobre ese impuesto. Pendiente de confirmar con el contable.

**El cliente cambió de cadena. ¿Qué precio recibe?**
Desde ese momento, los de la cadena nueva. Los borradores que ya tenían precio lo conservan (cambie el cliente del borrador para recotizar) y las órdenes solicitadas conservan su origen.

**Una orden vieja no muestra de dónde salió el precio.**
Las líneas anteriores al Lote 247 no guardaron el origen. Solo las líneas escritas o cambiadas desde el lote lo tienen.

**El nombre de la lista dedicada o el aviso salen en español, o con la fecha distinta a la de la pantalla.**
En la web del personal ya salen en el idioma de la interfaz y con las fechas de la compañía (sección 2). Si todavía los ve en español y con día/mes/año es porque falta algún dato en la respuesta (la web muestra entonces el texto del servidor), o porque los mira en otro sitio: la fuente `SPECIAL_PRICE`, el aviso de la ventana de la oferta o una pantalla que no es la web del personal. Los datos son los mismos.

## 12. Para soporte

- **Tablas:** `PriceListChain` y `OfferChain` (cadenas de listas y ofertas; quitar = `IsActive = 0`), `PriceList.SpecialClientId` / `SpecialChainId` (la marca de «lista dedicada»), `Offer.AudienceMode` (`ALL`/`INCLUDE`/`EXCLUDE`; `AppliesToAll = 1` solo con `ALL`) y `SalesOrderLine.PriceSource`, `PriceListId`, `OfferId`, `PriceChainId`, `ResolvedUnitPrice` (vacías en las líneas viejas).
- **Una sola lista dedicada activa** por dueño y par «desde / hasta» (índices `UX_PriceList_SpecialClient` y `UX_PriceList_SpecialChain`). Si dos personas agregan el mismo precio a la vez, una recibe 409.
- **«¿Por qué se cobró X?»:** en la orden, el chip de origen y la línea (`priceSource`, `resolvedUnitPrice`, `isManualPrice`). Si falta, la línea es anterior al lote.
- **Auditoría:** los cambios de cadenas de una lista quedan con el tipo `PRICE_LIST` y los de una oferta con `OFFER`.
- Sin permiso, o con el módulo apagado, los 403 dejan evento de seguridad `PERMISSION_DENIED`.
