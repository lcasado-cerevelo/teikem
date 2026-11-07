# 47 — Listas de precios y ofertas

<!-- kemi:staff -->

Lote 221 (2026-11-07) — pedido de vendedores, parte P1. El **precio de lo que se vende** deja de teclearse: sale de una **lista de precios** que arma el supervisor de ventas, y las **ofertas** son kits en promoción con foto y vigencia. Este capítulo es para el supervisor de ventas, la oficina y soporte. El pedido que usa estos precios se describe en [48 — Pedido guiado](48-pedido-guiado.md) y la orden en [18 — Órdenes de venta](18-ordenes-de-venta.md).

La pantalla está en **Almacén → Productos e inventario → pestaña «Precios y ofertas»** (`/warehouse/products?tab=prices`). La pestaña solo se ve con `sales.view`.

> Capturas pendientes (P8): la pestaña «Precios y ofertas» (ofertas como tarjetas y lista de listas), la ventana de una lista de precios y el editor del kit con la marca «Es promoción».

## 1. Quién puede y dónde

- Módulo **Órdenes de venta (`SALES_ORDERS`)**. Con el módulo apagado todo el API responde 403.
- Permisos:
  - **`sales.view`** — ver las listas, las ofertas y el catálogo del pedido.
  - **`sales.pricelist`** — crear, publicar, cambiar y desactivar listas; poner o quitar la foto; marcar un kit como oferta y fijar su vigencia. Implica `sales.view`. Lo trae el rol **Supervisor de ventas** (`SalesSupervisor`) y el **Admin de compañía**.
  - **`sales.price`** — sigue siendo la excepción: escribir en una línea de la orden un precio distinto del que salió de la lista. Solo el Admin de compañía, o quien él designe.
- Ver la foto de una oferta o de un renglón exige poder ver productos (`inventory.view`, que el rol Ventas trae); subirla exige `sales.pricelist`.
- Permiso nuevo en compañías que ya existían: se agregó a los roles ya clonados del Admin de compañía; el rol «Supervisor de ventas» lo crea el administrador en Sistema → Roles si la compañía no lo tiene.

## 2. De dónde sale el precio de una línea

Al crear o editar una línea de una orden de venta (y en el pedido guiado), el servidor busca el precio en este orden y se queda con el primero que encuentra:

1. **Lista de precios del cliente**: una lista **publicada**, **vigente hoy**, que aplica a ese cliente y trae el producto (o el kit).
2. **Lista general**: una lista publicada y vigente que aplica a todos los clientes y trae el producto.
3. **Precio de venta del producto** (`Product.SalePrice`).
4. Ninguno: el producto **no tiene precio** y la línea se rechaza (ver sección 6).

Reglas que conviene saber:
- El vendedor **no teclea precios**. Un precio distinto del resuelto exige `sales.price` (403), salvo que sea el que la línea ya tenía en el borrador.
- La fecha del precio es **hoy**, al crear o editar la línea (no la fecha de la orden). Una orden ya hecha **no cambia** si después cambia la lista: la línea guarda su precio.
- Si en un borrador se cambia el cliente sin tocar las líneas, la línea que tenía el precio resuelto del cliente anterior toma el del nuevo; la que tenía un precio excepcional (con `sales.price`) lo conserva.
- Si por datos viejos dos listas del mismo nivel traen el mismo producto en la misma fecha, gana la que empezó más tarde.
- Un **kit** (y una oferta) se pone en una lista como cualquier producto: su precio es el del kit completo.

## 3. Listas de precios

**Qué hace.** Una lista tiene nombre, vigencia (**desde** obligatorio, **hasta** opcional: vacío = sin fin), notas (hasta 500 caracteres), los **renglones** (producto o kit → precio, con foto opcional) y a quién **aplica**: a todos los clientes (lista general) o solo a clientes escogidos (precios negociados: guantes, cajas, monitor, tirillas).

**Quién puede.** Ver: `sales.view`. Todo lo demás: `sales.pricelist`. Módulo `SALES_ORDERS`.

**Cómo se usa (pantalla).** Pestaña **Precios y ofertas** → bloque **Listas de precios**.
1. **Nueva lista de precios**: nombre, «Vigente desde», «Hasta (opcional)», notas y la casilla «Aplica a todos los clientes». **Crear lista** la deja en **borrador**.
2. En la ventana de la lista: **Guardar datos**; **Renglones** (escoger producto o kit, escribir el precio, **Agregar**; cada renglón se puede cambiar de precio, quitar o ponerle foto); **Clientes de la lista** (**Agregar cliente**; solo si no es general).
3. **Publicar**: desde ese momento la lista da precios en sus fechas. **Volver a borrador** la saca de uso. **Desactivar** / **Reactivar** la retiran o la devuelven (nunca se borra).
4. La lista de listas muestra nombre, vigencia, estado (Borrador / Programada / Vigente / Vencida), «Aplica a» y número de renglones; filtra por estado, busca por nombre y puede **Mostrar desactivadas**.

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
| Renglones | Hasta 2000; solo productos o kits **propios** de la compañía y activos; sin repetir; precio de 0 en adelante con hasta 4 decimales. |
| Clientes | Hasta 2000; deben ser clientes activos. |
| Foto | JPG, PNG, WEBP o GIF (se comprueba por el contenido), hasta el tope del almacén de archivos. Quitarla solo la desliga: el archivo no se borra. |

**Cruces de listas.** Dos listas **publicadas** de la misma audiencia (dos generales, o dos de clientes que comparten un cliente) no pueden dar precio al mismo producto en fechas que se cruzan: el servidor lo rechaza con 409 y cita hasta 5 SKU. Una lista general y una de clientes **sí** pueden traer el mismo producto (gana la del cliente). Los cruces se comprueban al publicar y en cada cambio de una lista publicada (fechas, renglones, clientes, reactivar). Una lista de temporada encima de la base se permite si no repite productos.

## 4. Ofertas (kits en promoción)

**Qué hace.** Una **oferta** es un **kit** (capítulo [06 §8b](06-inventario-y-almacen.md#8b-kits-lote-68-2026-10-16)) marcado «Es promoción», con **foto** y **vigencia** opcionales. En la pestaña se ven como tarjetas con su foto (lo que verá el vendedor en el pedido guiado, arriba de su marca). Fuera de su vigencia **no se vende**: no se puede poner en una línea nueva ni en un borrador que se vuelve a guardar. Las órdenes ya hechas no cambian (la línea guarda su precio y la copia de sus componentes).

**Quién puede.** Ver: `sales.view` o `inventory.view`. Marcar, fijar fechas y poner la foto: `sales.pricelist`.

**Cómo se usa (pantalla).** Abra el kit en el editor de producto (Productos → el kit): sección **Oferta (promoción)** → marque **Es promoción**, escriba «Oferta desde» / «Oferta hasta» (opcionales) y pulse **Guardar oferta**; después **Poner foto** (el botón de la foto se habilita al guardar la marca). La tarjeta del kit en «Precios y ofertas» abre este mismo editor con **Editar oferta**. El **precio** de la oferta no va en el kit: se pone como renglón de una lista de precios (sección 3).

**Cómo se usa (API).** `GET /products/{publicId}/promo` (`sales.view` o `inventory.view`) · `PUT /products/{publicId}/promo` `{ "isPromotion": true, "promoFrom": "2026-11-10", "promoTo": "2026-12-31" }` (`sales.pricelist`; las fechas ausentes o `null` = sin esa fecha) · `POST /products/{publicId}/promo/image` (multipart) · `DELETE /products/{publicId}/promo/image` · `GET /price-lists/offers` (las ofertas con `inEffect` = se venden hoy).

Estado de una oferta en la tarjeta: **En vigor**, **Programada** (empieza más adelante) o **Vencida**.

Volver un kit a producto normal le quita la marca de oferta; la vigencia y la foto se quedan por si vuelve a ser kit.

## 5. El catálogo del pedido

`GET /api/v1/sales-orders/catalog?clientPublicId=<cliente>` (`sales.view`) alimenta el pedido guiado: las **marcas** (`Product.Brand`) con productos activos, y por marca sus **ofertas vigentes** (con foto) y la **lista completa** de productos y kits, cada uno con el precio ya resuelto para el cliente. Sin `clientPublicId`, los precios de la lista general.

- Solo productos propios, activos y **sin número de serie**.
- Las marcas se agrupan sin distinguir mayúsculas; los productos sin marca van al final (`brand = null`).
- `priceSource` dice de dónde salió el precio: `CLIENT_LIST`, `GENERAL_LIST`, `PRODUCT` o `NONE` (**sin precio**: no se puede pedir sin `sales.price`).
- Las ofertas fuera de vigencia no salen. Una oferta no se repite en la lista de su marca.
- Cliente inexistente: 404 «Cliente no encontrado.»; cliente inactivo: 422 «El cliente está inactivo; no se le puede vender.»

## 6. Validaciones y mensajes

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
| Foto que no es imagen | — | `La foto debe ser una imagen (JPG, PNG, WEBP o GIF).` | 400 |
| Foto demasiado grande | — | `El archivo no puede pasar de {n} MB.` | 400 |
| Oferta: no es un kit | — | `Solo un kit puede ser oferta (promoción).` | 422 |
| Oferta: fechas | «hasta» no antes de «desde» | `La fecha «hasta» de la oferta no puede ser anterior a la fecha «desde».` | 400 (`promoTo`) |
| Foto de una oferta sin marca | — | `Marque el kit como oferta antes de ponerle la foto.` | 422 |
| Oferta fuera de vigencia en una línea | — | `La oferta {SKU} no está vigente hoy (vale del dd/MM/aaaa al dd/MM/aaaa): no se puede vender.` (también «desde el …», «hasta el …» o «sin fechas») | 422 |
| Producto sin ningún precio | — | `El producto {SKU} no tiene precio de venta: escriba el precio.` | 400 |
| Precio distinto del resuelto sin `sales.price` | — | `Cambiar el precio exige el permiso de precios (sales.price).` | 403 |
| Sin el permiso | — | `Falta el permiso 'sales.pricelist'.` | 403 |
| Módulo apagado | — | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 |

## 7. Estatus y transiciones

La lista **no usa el motor de estatus**: su estado sale de «publicada» y de las fechas, y solo se auditan publicar, volver a borrador y desactivar.

| Estado que se ve | Cuándo | ¿Da precios en pedidos nuevos? |
|---|---|---|
| **Borrador** | No está publicada | No |
| **Programada** | Publicada, «desde» es futuro | No (todavía) |
| **Vigente** | Publicada y hoy cae entre «desde» y «hasta» | Sí |
| **Vencida** | Publicada y «hasta» ya pasó | No |
| **Desactivada** | `IsActive = 0` (cualquiera de las anteriores) | No; no se puede cambiar |

| De → a | Quién | Qué valida | Efecto |
|---|---|---|---|
| Borrador → Publicada | `sales.pricelist` | Al menos un renglón; clientes escogidos si no es general; sin cruces con otras publicadas | Empieza a dar precios en su vigencia |
| Publicada → Borrador | `sales.pricelist` | Que esté publicada | Deja de dar precios a los pedidos nuevos; los ya hechos no cambian |
| Cualquiera → Desactivada | `sales.pricelist` | Que esté activa | Igual que borrador, y queda sin edición |
| Desactivada → Activa | `sales.pricelist` | Que esté desactivada; si estaba publicada, que no se cruce con otra | Vuelve a su estado anterior |

Qué bloquea cada estado: una lista **desactivada** no se edita (ni renglones, ni clientes, ni fotos) hasta reactivarla; una **publicada** sí se edita, pero cada cambio vuelve a comprobar los cruces (409 si crea uno); borrador, programada y vencida no afectan precios.

## 8. Preguntas frecuentes del módulo

- **El vendedor ve el producto «Sin precio» en el pedido.** No hay lista vigente (del cliente ni general) que lo traiga, y el producto no tiene precio de venta. Póngalo en una lista publicada y vigente, o dele precio de venta al producto.
- **Publiqué la lista y el pedido sigue con el precio de antes.** Revise la vigencia (¿empieza más adelante?), que no esté en borrador o desactivada, y que el cliente esté en la lista si no es general. Un pedido ya hecho no cambia.
- **Dos precios para el mismo producto.** Una lista del cliente gana a la general. Dos de la misma audiencia no pueden coexistir: el servidor lo rechaza con el 409 de cruce.
- **La oferta no aparece en el pedido.** Debe ser un kit con «Es promoción», estar vigente hoy, activo y con precio en una lista (o precio de venta).
- **Quitar un cliente de una lista publicada falla.** Es el último y la lista no es general: agregue otro o pásela a borrador primero.

Todos los mensajes anteriores están en la [FAQ](faq.md#lote-221--listas-de-precios-y-ofertas).

## 9. Para soporte

- Tablas `PriceList`, `PriceListItem` y `PriceListClient`; columnas `Product.IsPromotion`, `PromoFrom`, `PromoTo` y `PromoImageFileId`; EntityType `PRICE_LIST` y el permiso `sales.pricelist` (sección «2026-11-07 — Lote 221» de `Diseño/logistica-db-update.sql`; propagación a roles clonados con la marca `L221_SALES_PRICELIST`).
- Las listas se exponen por `PublicId` y nunca se borran. Los cambios quedan en la bitácora (entidad `PRICE_LIST`).
- Las fotos son archivos del producto (clase DOCUMENT) cifrados en el almacén de archivos; se ven con `GET /files/{publicId}`.
