# Lote 229 — Ofertas como entidad aparte, kits de oferta y foto del producto (backend) (2026-11-09)

Especificación: [ofertas-kits-fotos-diseno.md](ofertas-kits-fotos-diseno.md) §1–§4, §7 y lo de precios de §8 (acordado con Luis). Reemplaza el diseño del
[lote 221](lote221-decisiones.md), donde una oferta era un kit con la marca «Es promoción». Solo backend: la pantalla (Contabilidad → Precios y ofertas → «Ofertas», el
bloque «Foto» del editor de producto y el pedido guiado) y el manual los hacen otras piezas con este documento. La sustitución de piezas y las dimensiones son del lote 230
(en paralelo): aquí solo se reutiliza su marca «no sustituible» de la pieza del kit.

## Qué se construyó
| Pieza | Dónde |
|---|---|
| Entidades `Offer` (`[AuditEntity("OFFER")]`), `OfferItem`, `OfferClient` + reglas puras `OfferRules` (vigencia, precio, avisos, mensajes) | `src/Teikem.Domain/Sales/Offers.cs`, `Persistence/Configurations/OfferConfigurations.cs` |
| `Product.ImageFileId` (foto del producto) y `Product.OfferId` (kit creado para una oferta); `ProductImageRules` (qué foto se muestra, tipos admitidos) | `Domain/Wms/Product.cs`, `Domain/Wms/ProductImageRules.cs`, `WarehouseConfigurations.cs` |
| `PriceSources.Offer = "OFFER"`; `ResolvedPrice` + `OfferId` y `Notices` | `Domain/Sales/PriceLists.cs` |
| SQL idempotente: tablas `Offer`, `OfferItem`, `OfferClient`; `Product.ImageFileId`, `Product.OfferId` (+ índice filtrado); copia `PromoImageFileId → ImageFileId` donde falte; EntityType `OFFER`. Sin permisos nuevos | sección «2026-11-09 — Lote 229» al final de `Diseño/logistica-db-update.sql` |
| Servicio de ofertas (CRUD, publicar/borrador, desactivar/reactivar, renglones, fotos, clientes, kits de oferta, avisos) | `Services/OfferService.cs` |
| Foto del producto (subir/quitar/leer, reducción a 1200 px) + la oferta como dueña de archivos | `Services/ProductImageService.cs` (`OfferOwnedEntityResolver` al final) |
| Lecturas compartidas: ofertas vigentes del cliente, ofertas dueñas de kits, foto efectiva del producto | `Services/PriceListQueries.cs` |
| Catálogo del pedido con ofertas nuevas, precio más bajo, foto resuelta y avisos | `Services/SalesCatalogService.cs` (`CatalogAsync`) |
| Orden de venta: kit de oferta fuera de vigencia = 422; precio con ofertas; `LinePrices` (de dónde salió cada precio + avisos) | `SalesOrderService.cs` (puntual), `SalesOrderService.Offers.cs` |
| Listas de precios: rechazan kits de oferta; `EffectiveImagePublicId` por renglón; `IsPromotion` siempre false | `Services/PriceListService.cs` |
| Lectura de fotos con `sales.view` sin `inventory.view` | `Services/StoredFileService.cs` (`EnsureReadAsync`) |
| Endpoints | `Api/Controllers/OffersController.cs`, `Api/Controllers/ProductImageController.cs` |
| Contratos | `Contracts/OfferContracts.cs`; campos nuevos (al final, opcionales) en `SalesCatalogItemDto`, `PriceListItemDto`, `SalesOrderDto` |
| Scripts de Solutions | `02-kit-oferta1.sql` (rehecho: oferta «OFERTA1» con su kit), `06-lista-precios-inicial.sql` (sin OFERTA1; salta kits de oferta), `00-verificar.sql` (revisa `Offer` y `Product.OfferId`), `LEEME.md` |
| Registro | `DependencyInjection`: `OfferService`, `ProductImageService`, `IOwnedEntityResolver → OfferOwnedEntityResolver` |

## Endpoints nuevos (para el frontend)
Todos bajo `RequireModule(SALES_ORDERS)` salvo la foto del producto (módulo `WMS_LOTSERIAL`, como `/products`).

| Método y ruta | Permiso | Cuerpo → respuesta |
|---|---|---|
| `GET /api/v1/offers?q&state&includeInactive` | `sales.view` | → `OfferListItemDto[]` |
| `GET /api/v1/offers/{id}` | `sales.view` | → `OfferDto` |
| `POST /api/v1/offers` | `sales.pricelist` | `OfferRequest` → `OfferDto` (nace en borrador) |
| `PATCH /api/v1/offers/{id}` | `sales.pricelist` | `OfferRequest` (lo que no viene no cambia; `clearValidTo` = sin fecha de fin) → `OfferDto` |
| `POST /api/v1/offers/{id}/publish` · `/unpublish` · `/deactivate` · `/reactivate` | `sales.pricelist` | → `OfferDto` |
| `POST /api/v1/offers/{id}/image` (multipart `file`) · `DELETE …/image` | `sales.pricelist` | → `OfferDto` |
| `POST /api/v1/offers/{id}/items` | `sales.pricelist` | `OfferItemsRequest` (agrega o cambia precio por producto) → `OfferDto` |
| `PATCH /api/v1/offers/{id}/items/{itemId}` · `DELETE …/items/{itemId}` | `sales.pricelist` | `OfferItemPriceRequest` / — → `OfferDto` |
| `POST /api/v1/offers/{id}/items/{itemId}/image` (multipart) · `DELETE …/image` | `sales.pricelist` | → `OfferDto` |
| `POST /api/v1/offers/{id}/clients` · `DELETE …/clients/{clientPublicId}` | `sales.pricelist` | `OfferClientsRequest` / — → `OfferDto` |
| `POST /api/v1/offers/{id}/kits` | `sales.pricelist` | `OfferKitRequest` (sku, name, price, components[] con `notSubstitutable`, brand?, categoryId?, salePrice?) → `OfferKitCreatedDto` |
| `PUT /api/v1/offers/{id}/kits/{productPublicId}` | `sales.pricelist` | `OfferKitUpdateRequest` (name?, components?) → `OfferDto` |
| `GET /api/v1/products/{id}/image` | `inventory.view` o `sales.view` | → `ProductImageDto` (`imagePublicId` propia, `effectiveImagePublicId` la que se muestra) |
| `POST /api/v1/products/{id}/image` (multipart `file`) · `DELETE …/image` | `inventory.manage` | → `ProductImageDto` |

DTO nuevos: `OfferRequest`, `OfferItemRequest`, `OfferItemsRequest`, `OfferItemPriceRequest`, `OfferClientsRequest`, `OfferKitRequest`, `OfferKitUpdateRequest`,
`OfferListItemDto`, `OfferDto`, `OfferItemDto`, `OfferClientDto`, `OfferWarningDto`, `OfferKitCreatedDto`, `SalesPriceNoticeDto`, `SalesLinePriceDto`, `ProductImageDto`.
Cambiados (solo campos opcionales al final): `SalesCatalogItemDto` (+ `offerPublicId`, `offerName`, `offerValidFrom`, `offerValidTo`, `offerPrice`, `isOfferKit`,
`notices`), `PriceListItemDto` (+ `effectiveImagePublicId`), `SalesOrderDto` (+ `linePrices`).

Claves para la pantalla:
- **«Sin fecha de fin»**: `validTo = null` ⇒ `hasEndDate = false` y `validityText = "desde el 09/11/2026, sin fecha de fin"` (con fin: «del … al …»).
- `state` = DRAFT / SCHEDULED / ACTIVE / EXPIRED (como las listas); `inEffect` = se aplica hoy (publicada, activa y vigente).
- `OfferDto.warnings[]` (`code`, `message`, `sku`, `otherOfferName`, `otherPrice`): `CHEAPER_OFFER` u `OFFER_ABOVE_GENERAL`. Vienen en TODA respuesta de la oferta
  (crear, cambiar, publicar, agregar renglón, leer): no son errores.
- `OfferItemDto`: `isOfferKit` (kit creado para esta oferta; `kit` trae sus piezas con `notSubstitutable`), `generalListPrice` (para comparar), `imagePublicId` (foto propia
  del renglón) y `effectiveImagePublicId` (la que se muestra).
- Catálogo (`GET /sales-orders/catalog`): `brands[].offers[]` = tarjetas (uno por producto, la oferta más barata); `isPromotion` = está en una oferta vigente para el
  cliente; `price`/`priceSource` = lo que se cobrará (`OFFER`, o `CLIENT_LIST` si el negociado es más bajo); `offerPrice` = el de la oferta; `imagePublicId` ya resuelta;
  `notices[]` (`TWO_OFFERS`, `OFFER_ABOVE_GENERAL`) con `message` listo para mostrar.
- Orden: `linePrices[]` SOLO en la respuesta de crear o editar el borrador con líneas (no se guarda): por producto `priceSource`, `offerName`/`priceListName` y `notices[]`.

## Reglas
- **Precio al pedir** (`OfferRules.Resolve`): 1) entre el negociado (lista publicada y vigente del cliente) y la oferta vigente que le aplique gana el **más bajo** (a igual
  precio, el negociado); 2) sin negociado, entre la oferta y la lista general el más bajo (sin oferta, la general); 3) si no, el precio de venta del producto; si no, sin
  precio (400 «no tiene precio» como siempre). Un precio distinto del resuelto exige `sales.price` (sin cambio).
- **Dos ofertas** vigentes con el producto para el cliente: gana la **más barata** (a igual precio, la que empezó más tarde; luego la más nueva) y se avisa (`TWO_OFFERS`).
- **Oferta más cara que la general**: gana el precio más bajo y se avisa (`OFFER_ABOVE_GENERAL`).
- **Kit de oferta** (`Product.OfferId`): se vende solo si su oferta está publicada, activa y vigente; si no, no sale en el catálogo y pedirlo da 422. El producto no se
  desactiva (`IsActive` sigue en 1; órdenes y facturas viejas lo ven); si la oferta se extiende o se le quita la fecha de fin, vuelve. No va en listas de precios ni en otra
  oferta (422). Nace **no ajustable**; su categoría (si no se da) y su categoría de impuesto salen de la primera pieza; su precio de venta, del de la oferta (si no se da).
- **Foto que se muestra** (`ProductImageRules.Resolve`): la del renglón (lista u oferta) → la del producto → si es kit sin foto, la de la **primera pieza (por orden) que
  tenga foto** → ninguna. Se aplica en el catálogo, en `/price-lists` (`effectiveImagePublicId`) y en `/offers`.
- **Leer fotos**: `GET /files/{id}` de un archivo del PRODUCTO que es foto (de `Product.ImageFileId`, la vieja `PromoImageFileId`, o de un renglón activo de lista u oferta)
  se permite con `sales.view` aunque no se tenga `inventory.view`; los demás archivos del producto siguen exigiendo `inventory.view`. Las fotos de la oferta y de sus
  renglones son archivos con dueño `OFFER` (se ven con `sales.view`, se suben con `sales.pricelist`).
- **Lo viejo**: el catálogo y las ventas ya no leen `IsPromotion/PromoFrom/PromoTo/PromoImageFileId` ni `KitPromoRules` (una «promoción» vencida ya no bloquea ventas).
  Las columnas se quedan. `/api/v1/price-lists/offers` y `/api/v1/products/{id}/promo` (GET/PUT, `image` POST/DELETE) **siguen funcionando** y están marcados OBSOLETOS
  en el código: se quitan cuando el frontend deje de llamarlos (lo cambia otra pieza). El 409 por cruce de listas no se tocó.

## Mensajes exactos (manual y FAQ)
| Caso | HTTP | Mensaje |
|---|---|---|
| Oferta sin nombre / nombre largo | 400 | «Indique el nombre de la oferta.» / «El nombre de la oferta no puede pasar de 120 caracteres.» |
| Sin «desde» | 400 | «Indique desde qué fecha vale la oferta.» |
| «Hasta» antes de «desde» | 400 | «La fecha «hasta» de la oferta no puede ser anterior a la fecha «desde».» |
| Notas largas | 400 | «Las notas no pueden pasar de 500 caracteres.» |
| Oferta / renglón / cliente / kit no existe | 404 | «Oferta no encontrada.» · «Renglón de la oferta no encontrado.» · «Cliente de la oferta no encontrado.» · «Kit de la oferta no encontrado.» |
| Oferta desactivada (cambiarla) | 422 | «La oferta está desactivada: reactívela para cambiarla.» |
| Renglones vacíos / demasiados | 400 | «Indique los productos y sus precios de oferta.» / «Una oferta admite como máximo 500 renglones.» |
| Renglón sin producto / sin precio / repetido | 400 | «Escoja el producto o kit de cada renglón.» · «Indique el precio de oferta de cada renglón.» · «El producto {SKU} viene repetido.» |
| Producto de un cliente 3PL / inactivo | 422 | «Solo se ponen en una oferta productos propios de la compañía.» / «El producto {SKU} está inactivo: no se puede poner en la oferta.» |
| Kit de otra oferta | 422 | «El kit {SKU} es de la oferta «{nombre}»: no se puede poner en otra.» |
| Kit de oferta en una lista de precios | 422 | «El kit {SKU} es de la oferta «{nombre}»: su precio va en la oferta, no en una lista de precios.» |
| Clientes vacíos / demasiados | 400 | «Escoja los clientes de la oferta.» / «Una oferta admite como máximo 2000 clientes.» |
| Clientes en una oferta para todos | 422 | «La oferta aplica a todos los clientes: no lleva clientes escogidos. Desmarque «aplica a todos» para escogerlos.» |
| Hacer «para todos» con clientes | 422 | «La oferta tiene clientes escogidos: quítelos antes de hacerla general (aplica a todos).» |
| Publicar sin renglones / sin clientes; quitar el último cliente de una publicada | 422 | «Agregue al menos un renglón antes de publicar la oferta.» / «Escoja al menos un cliente o marque que la oferta aplica a todos los clientes.» |
| Ya publicada / ya en borrador / ya desactivada / ya activa | 422 | «La oferta ya está publicada.» · «La oferta ya está en borrador.» · «La oferta ya está desactivada.» · «La oferta ya está activa.» |
| Concurrencia | 409 | «La oferta cambió al mismo tiempo. Recárguela e intente de nuevo.» |
| Kit de oferta: sin precio / sin piezas | 400 | «Indique el precio de oferta del kit.» / el de kits: «Un kit necesita al menos un componente.» (`KitRules.NeedComponents`) |
| Kit de oferta: SKU repetido, piezas inválidas | 409 / 400 | los de productos y kits de siempre (`ProductRules.SkuTaken`, `KitRules.*`) |
| Cambiar un kit desde otra oferta | 422 | «Ese kit no es de esta oferta: cámbielo desde su propia oferta o desde Productos.» |
| Pedir un kit de oferta fuera de vigencia | 422 | «El kit {SKU} es de la oferta «{nombre}», que {motivo}: no se puede vender.» con motivo = «está desactivada» · «está en borrador» · «todavía no empieza (vale del dd/MM/yyyy al dd/MM/yyyy)» · «ya terminó (valía del … al …)» (sin fin: «desde el dd/MM/yyyy, sin fecha de fin») |
| Foto vacía / no es JPG, PNG o WEBP / muy grande | 400 | «Adjunte la foto.» / «La foto debe ser una imagen JPG, PNG o WEBP.» / el del almacén de archivos (`StoredFileRules.TooLarge`) |
| Almacén de archivos sin configurar | 422 | el de siempre (`StoredFileRules.NotConfigured`) |

Avisos (no errores, código 200):
- `TWO_OFFERS`: «El producto {SKU} está en dos ofertas a la vez: «A» a $x y «B» a $y. Se aplica la más barata («A»).»
- `OFFER_ABOVE_GENERAL` (pedido/catálogo): «La oferta «A» pone {SKU} a $x, más caro que la lista general ($y): se aplica el precio más bajo.»
- `OFFER_ABOVE_GENERAL` (al armar la oferta): «El producto {SKU} sale en esta oferta a $x, más caro que en la lista general ($y): se aplicará el precio más bajo.»
- `CHEAPER_OFFER`: «El producto {SKU} también está en la oferta «B» ({vigencia}) a $y, igual o más barata que aquí ($x): mientras las dos estén vigentes se aplica el precio más bajo.»

## Decisiones tomadas (revisar)
1. **Empate negociado = oferta**: gana el negociado (`CLIENT_LIST`); la oferta solo gana si es estrictamente más barata. La tarjeta de la oferta sale igual en el catálogo, con
   `price` = el que se cobrará y `offerPrice` = el de la oferta.
2. **Dos ofertas**: gana la más barata (Luis, §8); a igual precio, la que empezó más tarde (lo propuesto en §3).
3. **Kit de oferta «inactivo»** = calculado (no se toca `Product.IsActive`): así vuelve solo al extender la oferta, y la oferta en borrador o desactivada también lo saca de la
   venta. Las órdenes ya hechas no cambian; **modificar** una línea solicitada o cambiar el cliente de un borrador **no** revalida la vigencia del kit (como en el lote 221).
4. **Avisos de la oferta en toda respuesta** (no solo al crear/publicar/agregar): se calculan al leer contra las demás ofertas PUBLICADAS y activas que se cruzan en fechas y
   clientes (una «para todos» se cruza con cualquiera), con el mismo producto **igual o más barato**; y contra la lista general vigente en la fecha en que la oferta empieza
   (o hoy, si ya empezó). Una oferta en borrador no genera avisos en otras.
5. **`linePrices` de la orden** solo en la respuesta de crear/editar el borrador con líneas (no se guarda en la base; al volver a abrir el borrador no viene). El catálogo es
   donde el vendedor ve los avisos al escoger.
6. **Foto de un kit sin foto** = la de la primera pieza **que tenga foto** (por orden), no estrictamente la primera pieza: si la primera no tiene, se usa la siguiente.
7. **Fotos**: solo JPG/PNG/WEBP (las listas de precios viejas siguen aceptando GIF en su renglón: no se tocó). Tope = `Files:MaxMegabytes` (10 por omisión), como las demás
   fotos. **Reducción a 1200 px** (lado mayor) con SkiaSharp, que el repo ya tenía (miniaturas del chat), en el mismo formato y respetando la orientación EXIF; si la
   librería nativa no está (Linux sin `SkiaSharp.NativeAssets.Linux`, p. ej. el CI) o la imagen no se lee, se guarda el original. Se comprueba si la librería está **sin
   crear objetos de Skia** (crear uno sin la nativa deja un finalizador que tumba el proceso: pasó en la primera corrida de pruebas).
8. **Foto del renglón de la oferta** = archivo con dueño `OFFER` (la oferta); la del producto = archivo del `PRODUCT`. La foto de una lista de precios sigue como en el 221
   (archivo del producto) y ahora también se ve con `sales.view`.
9. **Kit de oferta**: `salePrice` = el precio de oferta si no se da otro; categoría = la de la primera pieza si no se da; categoría de impuesto = la de la primera pieza.
   Quitar su renglón de la oferta lo deja atado a ella (y sin precio de oferta se vendería a su precio de venta mientras la oferta valga).
10. **Foto del producto en su ficha**: no se agregó a `ProductDetailDto` (el lote 230 trabaja ese contrato a la vez); el editor la lee con `GET /products/{id}/image`.
11. **Scripts de Solutions**: la oferta «OFERTA1» nace publicada, para todos, desde hoy (hora de Puerto Rico) y sin fecha de fin; si ya existe, no se le cambian fechas ni
    estado. 06 ya no pone OFERTA1 en la lista general.
12. No se agregó `ProductImageController` a `WmsControllerSecurityTests` (su conteo lo está cambiando el lote 230 en este momento); el permiso y el módulo están en el
    controlador.

## Cómo se probó
- `dotnet build Teikem.sln`: sin errores.
- `dotnet test --filter` (OfferRulesTests, OfferKitTests, ProductImageTests, KitPromoTests, SalesPriceResolutionTests, PriceList*): **43 pasan, 0 fallan**.
- `dotnet test Teikem.sln` completo (final): **«Passed! - Failed: 0, Passed: 5753, Skipped: 54, Total: 5807»**. (Una corrida intermedia falló en
  `ConsignmentTests…update_script…` por la palabra «DELETE» en un comentario de esta sección: corregido.)
- **SQL real**: `db-init` desde cero sobre `TeikemL229` (estructura 187 lotes, seed 68, update 817) y la sección «Lote 229» corrida **dos veces** más con `sqlcmd -I`
  (sin errores, la segunda sin cambios). Scripts 02 y 06 aplicados **dos veces** (la segunda: «Filas cambiadas: 0»). Recorrido por el API (Development, sin llave de Brevo,
  almacén de archivos en carpeta temporal): ofertas, catálogo con OFERTA1 a $41.94 (`OFFER`), crear oferta y kit de oferta con `notSubstitutable`, publicar, SKU repetido
  409, foto PNG 200 y GIF 400, orden con `linePrices` y aviso de dos ofertas, kit de oferta en borrador 422. La base `TeikemL229` se borró al final.
- La reducción a 1200 px y la orientación EXIF se comprobaron aparte con la librería nativa de Linux (proyecto de prueba en el scratchpad: PNG/JPG/WEBP a 1200×600 y las
  8 orientaciones correctas); en las pruebas xunit de aquí se omite sola si falta la librería.
- `web-app/openapi.json`: fusión SOLO de lo nuevo (15 rutas, 16 esquemas nuevos y 3 cambiados: `PriceListItemDto`, `SalesCatalogItemDto`, `SalesOrderDto`) del swagger del
  API levantado en Development sin llave de Brevo; nada existente se borró. `schema.d.ts` regenerado en web-app y copiado igual a app-cliente y app-almacen.
- Nota: un primer `db-init` se corrió por error contra la base local de desarrollo `Teikem` (no se recrea: solo aplica los scripts idempotentes); sin efecto fuera de la
  máquina de desarrollo.
