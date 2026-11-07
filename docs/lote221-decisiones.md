# Lote 221 — P1 del pedido de vendedores: listas de precios y ofertas (backend) (2026-11-07)

Plan: [pedido-vendedores-plan-desarrollo.md](pedido-vendedores-plan-desarrollo.md) §1.2, §2, §3 (P1) y §4.1 (P1). La pantalla («Precios y ofertas» en Productos e
inventario y el editor del kit con foto y vigencia) la hace otra pieza; aquí va el servidor.

## Qué se construyó
| Pieza | Dónde |
|---|---|
| Entidades `PriceList`, `PriceListItem`, `PriceListClient` (+ reglas puras `PriceListRules`, `KitPromoRules`) | `src/Teikem.Domain/Sales/PriceLists.cs`, `Persistence/Configurations/PriceListConfigurations.cs` |
| `Product.IsPromotion`, `PromoFrom`, `PromoTo`, `PromoImageFileId` | `Domain/Wms/Product.cs`, `WarehouseConfigurations.cs` |
| SQL idempotente: 3 tablas, 4 columnas de `Product`, EntityType `PRICE_LIST`, permiso `sales.pricelist` → roles `TenantAdmin` (plantilla y clonados, una vez: `L221_SALES_PRICELIST`) | sección «2026-11-07 — Lote 221» al final de `Diseño/logistica-db-update.sql` |
| Permiso `sales.pricelist` (BILLING; editar ⇒ `sales.view`) | `PermissionCatalog`, `PermissionDisplayOrder`, `MenuPermissions.EditImplies` |
| Servicio de listas (CRUD, publicar/borrador, desactivar, renglones, fotos, clientes, cruces) | `Services/PriceListService.cs` |
| Ofertas y catálogo del pedido | `Services/SalesCatalogService.cs` |
| Resolución del precio en la orden (crear, editar borrador, cambiar de cliente, modificar línea solicitada) | `SalesOrderService.cs` / `.Request.cs` + `Services/PriceListQueries.cs` |
| Endpoints | `Api/Controllers/PriceListsController.cs`: `/api/v1/price-lists` (+ `/offers`, `/{id}/publish|unpublish|deactivate|reactivate`, `/{id}/items`, `/{id}/items/{itemId}` PATCH/DELETE, `/{id}/items/{itemId}/image`, `/{id}/clients`, `/{id}/clients/{clientPublicId}`), `/api/v1/products/{id}/promo` (GET/PUT, `image` POST/DELETE), `GET /api/v1/sales-orders/catalog?clientPublicId=` |
| Contratos | `Contracts/PriceListContracts.cs`; `web-app/openapi.json` (solo adiciones) |

## Reglas
- **Precio al pedir**: lista PUBLICADA y vigente del cliente que traiga el producto > lista general publicada y vigente > `Product.SalePrice` > sin precio (400 «no tiene precio»).
  Un precio distinto del resuelto exige `sales.price` (403), salvo que sea el que la línea ya tenía en el borrador. Kits (y ofertas) se ponen en una lista como cualquier producto.
- **Cruces**: dos listas publicadas de la misma audiencia (dos generales, o dos de clientes con un cliente en común) no pueden dar precio al mismo producto en fechas que
  se cruzan → 409 con los SKU. Una general y una del cliente sí (gana la del cliente). Se comprueba al publicar y en cada cambio de una lista publicada (fechas, renglones, clientes, reactivar).
- **Ofertas**: solo un kit puede ser oferta (422); fuera de vigencia no se puede poner en una línea nueva ni en un borrador que se vuelve a guardar (422 con las fechas).
  Las órdenes ya hechas no cambian (la línea guarda la copia del precio y de los componentes).

## Decisiones tomadas (revisar)
1. **Estado de la lista** = `IsPublished` (borrador / publicada) + fechas; «vigente», «programada» y «vencida» se calculan, no se guardan (no es un estatus del motor de estatus: no hay transiciones que auditar más que publicar/borrador, que quedan en la bitácora).
2. **Cruces por producto**, no por lista: se permite una lista de temporada encima de la base si no repite productos. Con datos viejos que se crucen, gana la que empezó más tarde.
3. **Fecha del precio = hoy** al crear o editar la línea (no la fecha de la orden). Al cambiar el cliente de un borrador sin mandar líneas, la línea que tenía el precio resuelto
   del cliente anterior toma el del nuevo; la que tenía una excepción con `sales.price` la conserva.
4. **Modificar una línea solicitada**: «de lista» = el precio resuelto hoy para el cliente; la oferta vencida NO bloquea modificar una orden ya solicitada (ya está hecha).
   Solicitar un borrador con una oferta que venció después de guardarlo tampoco se bloquea (la validación es al crear/editar la línea, como dice el plan).
5. **Fotos** (oferta y renglón) = `StoredFile` del **producto** (clase DOCUMENT, solo imágenes): se ven con `GET /files/{id}` con permiso de ver productos (`inventory.view`, que el rol Ventas trae); se suben con `sales.pricelist`. Quitar la foto solo la desliga (el archivo no se borra).
6. **Catálogo**: productos propios, activos y sin serie; marcas agrupadas sin distinguir mayúsculas (se muestra la escritura más común); los sin marca van al final con `brand = null`; las ofertas solo en «Offers» (no repetidas en la lista); los productos sin precio salen con `priceSource = NONE`. El parámetro es `clientPublicId` (el plan decía `clientId`; el API expone PublicId).
7. `sales.pricelist` solo se dio a **TenantAdmin** (plantilla y clonados); el rol «Supervisor de ventas» es de P2.
8. Volver un kit a producto normal le quita la marca de oferta (la vigencia y la foto se quedan por si vuelve a ser kit).

## Cómo se probó
- `dotnet build Teikem.sln`: sin errores. `dotnet test --filter` de las tres clases: **25 pasan, 0 fallan**; con la red más amplia (Sales*, Kit*, MenuPermissions, FleetCatalog, WmsContracts, FrozenSql, PermissionSeeder): **414 pasan, 0 fallan**.
- `web-app/openapi.json`: fusión SOLO de lo nuevo (15 rutas, 14 esquemas) tomado del swagger del API levantado en Development, sin base y sin llave de Brevo; nada existente cambió.
- xunit: `PriceListRulesTests` (vigencia, estado, cruces, quién gana, kit en lista, permiso de precio, sección SQL), `SalesPriceResolutionTests` (crear/editar línea, cambio de cliente, línea solicitada, vendedor sin `sales.price`, publicar y cruces, renglones), `KitPromoTests` (solo kits, fuera de vigencia no se vende, orden hecha no cambia, catálogo por marca).
- **No probado**: la sección SQL contra un SQL Server real (no había uno arriba), la subida de fotos (necesita el almacén de archivos), `schema.d.ts` (sin `node_modules` aquí: los regenera `npm run check`).
