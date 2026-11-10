# Lote 231 — Kit de oferta con biblioteca (backend, 2026-11-10)

Especificación: [ofertas-kits-fotos-diseno.md](ofertas-kits-fotos-diseno.md) §10, §11 y sobre todo §12 (acordado con Luis). Cambia lo construido en el
[lote 229](lote229-decisiones.md) (kit atado a UNA oferta por `Product.OfferId`) y toca el [lote 230](lote230-decisiones.md) (sustitución y «ajustable al pedir») y los
[lotes 68](lote68-decisiones.md)/[69](lote69-decisiones.md) (convertir en kit desde Productos). Solo backend: la pantalla (biblioteca en Precios y ofertas, quitar la pestaña
«Kit» y «Convertir en kit» de Productos) y el manual van después con este documento. En paralelo, el lote 232 hace el **kit armado** (tipo de producto nuevo
`ASSEMBLED_KIT`, con inventario): aquí no se toca.

## Qué se construyó
| Pieza | Dónde |
|---|---|
| `Product.IsOfferKit` (kit de la biblioteca de kits de oferta; sigue siendo tipo `KIT` sin existencia, se arma al pedir). `Product.OfferId` **deja de usarse** (la columna se queda; nada lo lee ni lo escribe) | `Domain/Wms/Product.cs` |
| Reglas puras `OfferKitRules` (vendible, último uso, cambio de composición, mensajes) | `Domain/Sales/OfferKits.cs` (nuevo) |
| Se quitaron de `OfferRules` las reglas del kit atado a una oferta (`KitSellable`, `KitNotSellable`, `ItemKitOfOtherOffer`, `KitOfAnotherOffer`, `OfferKitNotInLists`) y `PriceListQueries.OwnerOffersAsync` | `Domain/Sales/Offers.cs`, `Services/PriceListQueries.cs` |
| Biblioteca: listar, leer, crear y cambiar kits de oferta; los renglones aceptan cualquier kit de oferta (varias ofertas a la vez) y, sin precio, toman el último | `Services/OfferService.cs`, `Api/Controllers/OffersController.cs`, `Contracts/OfferContracts.cs` |
| Composición inmutable una vez usado (en `KitService.SetAsync`, para todo el que lo llame) y `KitService.SetFromProductsAsync` (la ruta de Productos) | `Services/KitService.cs`, `Api/Controllers/ProductsController.cs` |
| Vendible solo en una oferta publicada y vigente para el cliente (orden de venta y catálogo) | `Services/SalesOrderService.cs`, `Services/SalesCatalogService.cs` |
| Sustituir y ajustar piezas solo en kits de oferta | `Services/SalesOrderService.cs` (`KitSnapshotAsync`), `Services/SalesOrderService.Substitution.cs` |
| Fuera de Productos e inventario: lista, marcas, salida a integraciones, fuente de datos de productos, importación | `ProductService.ListAsync`/`ListBrandsAsync`, `EntityOutput` (`ProductExportRows`), `Analytics/ProductDataSource`, `ProductImportRows` |
| Listas de precios: rechazan el kit de oferta por `IsOfferKit` | `Services/PriceListService.cs` |
| SQL idempotente: `Product.IsOfferKit BIT NOT NULL DEFAULT 0`, marca los que tenían `OfferId`, índice filtrado `IX_Product_OfferKit`. Sin permisos nuevos | sección «2026-11-10 — Lote 231» al final de `Diseño/logistica-db-update.sql` |
| Scripts de Solutions | `02-kit-oferta1.sql` (kit de la biblioteca + oferta OFERTA1 sin fecha de fin a $41.94), `06` y `00` (leen `IsOfferKit`), `LEEME.md` |

## Contrato final para el frontend
Todo bajo `RequireModule(SALES_ORDERS)` salvo `/products` (módulo `WMS_LOTSERIAL`).

| Método y ruta | Permiso | Cuerpo → respuesta | Notas |
|---|---|---|---|
| `GET /api/v1/offers/kits?q&includeInactive` | `sales.view` | → `OfferKitLibraryItemDto[]` | **Nuevo.** Biblioteca por SKU (hasta 500). `q` = el SKU o el nombre contiene. |
| `GET /api/v1/offers/kits/{productPublicId}` | `sales.view` | → `OfferKitLibraryItemDto` | **Nuevo.** 404 si no es kit de oferta. |
| `POST /api/v1/offers/kits` | `sales.pricelist` | `OfferKitLibraryRequest` (sku, name, components[] con `notSubstitutable`, adjustable?, brand?, categoryId?, salePrice?) → `OfferKitLibraryItemDto` | **Nuevo.** «Crear un kit nuevo» sin ponerlo en una oferta. |
| `PATCH /api/v1/offers/kits/{productPublicId}` | `sales.pricelist` | `OfferKitUpdateRequest` (name?, components?, adjustable?) → `OfferKitLibraryItemDto` | **Nuevo.** Nombre siempre; piezas y ajustable solo si nunca se usó. |
| `POST /api/v1/offers/{id}/items` | `sales.pricelist` | `OfferItemsRequest` → `OfferDto` | «Usar un kit anterior» = un renglón con el `productPublicId` del kit y su precio. **Cambio:** con un kit de oferta el `price` puede ir vacío (null) y se pone el último (`lastPrice`); si nunca se usó → 400. Ya no hay «kit de otra oferta». |
| `POST /api/v1/offers/{id}/kits` | `sales.pricelist` | `OfferKitRequest` (+ `adjustable?`) → `OfferKitCreatedDto` | Se queda: «Crear un kit nuevo» y ponerlo ya en esta oferta con su precio. El kit queda en la biblioteca. |
| `PUT /api/v1/offers/{id}/kits/{productPublicId}` | `sales.pricelist` | `OfferKitUpdateRequest` → `OfferDto` | Se queda por compatibilidad (= el PATCH de la biblioteca, exigiendo que el kit esté en esta oferta). Preferir el PATCH. |
| `GET /api/v1/products?…&includeOfferKits` | `inventory.view` | → `ProductPageDto` | **Cambio:** los kits de oferta ya NO salen por omisión; `includeOfferKits=true` los incluye (el `ProductPicker` del `OrderModal` de ventas debe mandarlo para poder pedirlos). `GET /products/{id}` sigue respondiendo. |
| `PUT /api/v1/products/{id}/kit` | `inventory.manage` | `KitSetRequest` → `KitDto` | **Cambio:** 422 al convertir un producto normal en kit y 422 con un kit de oferta. Un kit viejo (no de oferta) se sigue editando. |

DTO nuevos: `OfferKitLibraryRequest`, `OfferKitLibraryItemDto`. Cambiados (campos opcionales al final): `OfferKitRequest` (+ `adjustable`), `OfferKitUpdateRequest`
(+ `adjustable`), `KitDto` (+ `isOfferKit`), `KitSubstitutesDto` (+ `isOfferKit`), `ProductListQuery` (+ `includeOfferKits`).

`OfferKitLibraryItemDto`: `productPublicId`, `sku`, `name`, `brand`, `categoryId`, `adjustable`, `isActive`, `salePrice`, `effectiveImagePublicId` (la del kit o la de su
primera pieza con foto), `components[]` (`KitComponentDto` con `notSubstitutable`), `lastPrice` y `lastOfferPublicId`/`lastOfferName` (el **último uso**: el precio que
se sugiere), `offerCount` (en cuántas ofertas activas está hoy, renglones activos), `compositionLocked` (true = ya se usó en una oferta o en un pedido: la pantalla debe
deshabilitar las piezas y ofrecer «crear un kit nuevo»; el nombre sí se cambia).

Claves para la pantalla:
- Al agregar un renglón a una oferta: «Usar un kit anterior» (`GET /offers/kits`, mostrar piezas, última oferta y `lastPrice` como precio sugerido; luego
  `POST /offers/{id}/items`) o «Crear un kit nuevo» (`POST /offers/{id}/kits` con precio, o `POST /offers/kits` + `POST …/items`).
- `OfferItemDto.isOfferKit` = es kit de oferta (antes: «creado para ESTA oferta»); un mismo kit puede estar en varias ofertas.
- Catálogo (`GET /sales-orders/catalog`): sin cambios de forma; `isOfferKit` sale de la marca nueva y un kit de oferta solo aparece (como tarjeta) si está en alguna oferta
  vigente para el cliente. `kitAdjustable` solo es true en un kit de oferta.
- `GET /sales-orders/kits/{kit}/substitutes`: `isOfferKit = false` ⇒ no mostrar «Cambiar» (sustitutos vacíos y `adjustable = false`).
- Un kit de venta permanente = una oferta sin fecha de fin (§11).

## Reglas
- **Biblioteca:** el kit de oferta es un producto tipo `KIT` sin existencia con `IsOfferKit = 1`. Nace **no ajustable** salvo que se pida; categoría por omisión y categoría
  de impuesto de la primera pieza; precio de venta = el que se dé (al crearlo desde una oferta, el de la oferta si no se da otro). No se borra ni se desactiva solo.
- **Vendible:** solo mientras esté en alguna oferta **publicada, activa y vigente** (extremos incluidos; sin «hasta» no vence) **que aplique al cliente** (para todos o
  con el cliente escogido) con un renglón activo (`OfferKitRules.IsOnSale`). Si no: no sale en el catálogo y pedirlo da 422. Su precio sale del renglón (regla del más
  bajo de siempre: dos ofertas con el mismo kit → la más barata y aviso `TWO_OFFERS`). Las órdenes y facturas viejas lo siguen viendo (la línea guarda su copia; el producto
  no se toca). Como en el 229, modificar una línea ya solicitada o cambiar el cliente de un borrador no revalida (se revalida al crear o editar las líneas del borrador).
- **Último precio** (`OfferKitRules.LastUse`): el renglón más reciente que sigue puesto en una oferta activa; si no hay, el más reciente de todos (también los quitados).
- **Composición inmutable** (piezas, cantidades, «no sustituible» y «ajustable al pedir»): una vez que el kit se usó —un renglón en cualquier oferta, **aunque se haya
  quitado**, o una línea en cualquier orden de venta— no cambia (422). Mientras nunca se usó, sí se puede corregir. Mandar la misma composición (aunque en otro orden) no
  es un cambio. El nombre siempre se cambia.
- **Sustitución y «ajustable al pedir»: solo en el kit de oferta.** Cualquier otro kit (el armado del lote 232 es otro tipo y no llega aquí; un kit viejo tipo `KIT` sin
  marca) va con sus piezas: mandar piezas en su línea da 400, y su lista de sustitutos sale vacía.
- **Fuera de Productos e inventario:** la lista de productos (y con ella sus filtros, tabletas/conteos, exportación de la tabla y la cuenta del Pulso), las marcas del filtro,
  la salida de productos a integraciones, la fuente de datos `PRODUCT` sin ids (vistas/indicadores), y la importación (no actualiza un kit de oferta: error por fila). El
  valor del inventario, los conteos cíclicos y el kárdex parten de saldos y un kit de oferta no tiene existencia: no aparece. La ficha por id, las órdenes, facturas y la
  fuente de datos por id lo siguen nombrando.
- **Convertir en kit desde Productos:** `PUT /products/{id}/kit` ya no convierte un producto normal en kit (422) ni toca un kit de oferta (422). Un kit viejo de tipo `KIT`
  sin marca de oferta se sigue editando o volviendo normal como antes (no hay datos de kits en ninguna base; se dejó por si acaso). El lote 232 no reutiliza esta ruta
  (su kit armado se crea con `POST /products` + `isAssembledKit` y tiene su propio servicio).

## Mensajes exactos (manual y FAQ)
| Caso | HTTP | Mensaje |
|---|---|---|
| Kit de la biblioteca que no existe (o no es kit de oferta) | 404 | «Kit de oferta no encontrado.» |
| Cambiar piezas o «ajustable» de un kit ya usado | 422 | «La composición del kit {SKU} ya no se puede cambiar porque ya se usó en una oferta o en un pedido: cree un kit nuevo con las piezas que quiera.» |
| Pedir un kit de oferta que no está en una oferta vigente para el cliente | 422 | «El kit de oferta {SKU} no está en ninguna oferta publicada y vigente para este cliente: no se puede vender.» |
| Kit de oferta en una lista de precios | 422 | «El kit {SKU} es un kit de oferta: su precio va en una oferta, no en una lista de precios.» |
| `PUT /products/{id}/kit` sobre un kit de oferta | 422 | «El kit {SKU} es un kit de oferta: se cambia en Contabilidad → Precios y ofertas, no en Productos.» |
| `PUT /products/{id}/kit` para convertir un producto normal | 422 | «Un producto ya no se convierte en kit desde Productos: un kit que se arma al pedir se crea como kit de oferta en Contabilidad → Precios y ofertas.» |
| Sustituir o ajustar piezas de un kit que no es de oferta | 400 | «El kit {SKU} no admite cambiar ni ajustar sus piezas al pedir: eso solo se puede en un kit de oferta.» |
| `PUT /offers/{id}/kits/{kit}` con un kit que no está en esa oferta | 422 | «Ese kit no está en esta oferta: cámbielo desde la biblioteca de kits de oferta.» |
| Renglón de un kit de oferta sin precio y nunca usado | 400 | «Indique el precio de oferta de cada renglón.» (el de siempre) |
| Importación de productos con el SKU de un kit de oferta | fila con error (campo `sku`) | «Ese SKU es un kit de oferta: no se cambia por la importación de productos, sino en Contabilidad → Precios y ofertas.» |
| Crear kit: sin piezas, piezas inválidas, SKU repetido | 400 / 404 / 409 | los de siempre (`KitRules.*`, `ProductRules.SkuTaken`) |

Mensajes del lote 229 que **ya no existen**: «El kit {SKU} es de la oferta «…»: no se puede poner en otra.», «Ese kit no es de esta oferta: cámbielo desde su propia oferta
o desde Productos.», «El kit {SKU} es de la oferta «…», que …: no se puede vender.» y «El kit {SKU} es de la oferta «…»: su precio va en la oferta, no en una lista de
precios.». El manual (capítulos 47/48 y la FAQ del 229) debe cambiarlos.

## Decisiones tomadas (revisar con Luis)
1. **Composición de un kit nunca usado: se permite cambiarla** (sirve para corregir un error al crearlo). «Usado» = cualquier renglón en una oferta (aunque se haya quitado
   o la oferta esté en borrador) o cualquier línea de orden de venta. «Ajustable al pedir» y «no sustituible» cuentan como composición.
2. **Reordenar las piezas no es cambiar la composición** (se acepta aunque esté bloqueada); la foto «de la primera pieza» puede cambiar con el orden.
3. **Último precio**: el del renglón más reciente aún puesto en una oferta activa; si no queda ninguno, el más reciente de todos. Un renglón agregado por error y quitado
   solo manda si no hay otro.
4. **Renglón sin precio** solo para kits de oferta ya usados (toma el último); para cualquier otro producto el precio sigue siendo obligatorio.
5. **Kit de oferta sin dar de baja automática**: no hay endpoint para retirarlo de la biblioteca (no se pidió); `includeInactive` muestra los dados de baja por otra vía.
   Marca y categoría no se cambian desde la biblioteca (solo al crearlo).
6. **`includeOfferKits`** en `GET /products` para que el selector de producto de la orden de venta clásica pueda seguir pidiendo kits de oferta (el pedido guiado usa el
   catálogo). La búsqueda por `productPublicIds` también los excluye sin ese parámetro.
7. **Kits viejos tipo `KIT` sin marca de oferta**: se venden como antes (a su precio, con sus piezas, sin sustituir ni ajustar) y se editan desde Productos. No hay datos
   así; no se migraron a kit de oferta (el SQL solo marca los que tenían `OfferId`).
8. **Una pieza de un kit de oferta puede ser un kit armado** (lote 232: es un producto con existencia; `KitService` solo rechaza piezas de tipo `KIT`). Lo decide el lote 232
   si hace falta.
9. **`PUT /offers/{id}/kits/{kit}`** se dejó compatible (exige que el kit esté en esa oferta); el contrato recomendado es el PATCH de la biblioteca.

## Cómo se probó
- **Pruebas nuevas:** `OfferKitLibraryTests` (7: crear en la biblioteca sin oferta y corregir mientras no se usa; reusar en otra oferta con otro precio y último precio
  sugerido; composición bloqueada una vez usado y nombre sí; vendible solo en oferta publicada y vigente para el cliente, órdenes viejas intactas; mismo kit en dos ofertas
  = la más barata con aviso; fuera de Productos e inventario pero por id sí; Productos ya no convierte en kit y solo el kit de oferta cambia piezas) y `OfferKitTestData`.
  Ajustadas: `OfferKitTests`, `OfferRulesTests` (reglas puras de `OfferKitRules`), `KitSubstitutionTests`, `SalesKitTests`, `ProductImportTests`, `WmsContractsTests`,
  `WmsControllerSecurityTests`, `LegacyImportCatalogTests`, `WmsCatalogTests`.
- `dotnet build Teikem.sln`: «Build succeeded. 0 Warning(s)». Pruebas del lote (filtro Offer/Kit/PriceList/SalesCatalog/ProductImport/Wms*): «Passed! - Failed: 0, Passed:
  497». `dotnet test Teikem.sln` completo (árbol con el lote 232 a medias): «Failed: 1, Passed: 5787, Skipped: 54, Total: 5842»; el único rojo es
  `TenantIsolationModelTests.Wms_tables_precisions_dates_and_keys_map_one_to_one` (espera 41 tablas WMS y hay 45: las 4 tablas `KitAssembly*`/`KitDisassembly*` del
  lote 232, que ese lote debe sumar a la cuenta). Nada del 231.
- **SQL:** base local `TeikemL231` recreada con `db-init` (838 lotes del update, «Inicialización de BD completada.») y la sección «Lote 231» corrida otra vez dos
  veces (EXIT 0 y 0): columna, `DF_Product_IsOfferKit` e `IX_Product_OfferKit` presentes una sola vez. Base borrada al final.
- **Scripts de Solutions** (contra la compañía demo con las tirillas 52800 y el monitor 070120): `00` «OK: la estructura y los permisos de los lotes 221–225, 229 y
  231 están en la base.»; `02` simulación «se habrían cambiado 5 filas», 1.ª corrida «APLICADO. Filas cambiadas: 5» (oferta sin fecha de fin, kit de la biblioteca
  `IsOfferKit = 1` no ajustable, renglón a 41.94), 2.ª «APLICADO. Filas cambiadas: 0»; `06` 5 y 0 filas, el kit de oferta fuera de la lista.
- **Recorrido del API** (local, Development, sin llave de Brevo, contra `TeikemL231`): biblioteca con OFERTA1 (`lastPrice` 41.94, `offerCount` 1,
  `compositionLocked` true); `GET /products?search=OFERTA1` total 0 y con `includeOfferKits=true` total 1; ficha por id 200; crear OFERTA2 200 y repetido 409;
  cambiar piezas de OFERTA1 422 con el mensaje exacto; renombrar 200; «ajustable» de un kit sin usar 200; 404 «Kit de oferta no encontrado.»; renglón sin precio en
  otra oferta = 41.94 y, con un kit nunca usado, 400 «Indique el precio de oferta de cada renglón.»; `PUT /products/{id}/kit` 422 (convertir) y 422 (kit de oferta);
  catálogo con OFERTA1 como tarjeta de oferta; pedir OFERTA2 (sin oferta) 422 «El kit de oferta OFERTA2 no está en ninguna oferta publicada y vigente para este
  cliente: no se puede vender.»; sustitutos de OFERTA1 `isOfferKit` true; kit de oferta en una lista de precios 422.
- `web-app/openapi.json`: solo lo del 231 (rutas `/api/v1/offers*`, `includeOfferKits` de `/products`, esquemas `Offer*`, `OfferKitLibrary*`, `KitDto`,
  `KitSubstitutesDto`) del swagger de una instantánea HEAD + lote 231 (sin el 232); `schema.d.ts` regenerado y copiado igual a app-cliente y app-almacen.
