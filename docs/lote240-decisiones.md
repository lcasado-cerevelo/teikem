# Lote 240 — Fotos de productos: ajuste de la compañía, miniatura en la lista y foto que se amplía con un clic (2026-11-12)

Pedido de Luis (2026-11-12): no todos los almacenes quieren manejar fotos; debe ser un ajuste de la compañía (sí o no) y, apagado, ni se muestran ni se pueden subir. En la lista de productos de inventario, la foto en la misma línea (del alto de la fila); un clic en la foto la amplía ahí mismo; igual en la ficha del producto, en ventas y donde se muestre una foto.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Ajuste «La compañía maneja fotos de productos» | `Tenant.ProductPhotosEnabled` (BIT, **1 por omisión** = como era; sección «2026-11-12 (Lote 240)» de `logistica-db-update.sql`, idempotente). Ajustes → Operación → **Fotos** (`admin.tenant`), se guarda al instante. `TenantSettingsDto.ProductPhotosEnabled` / request. |
| Apagado: no se suben | `ProductImageService.SetAsync`, `OfferService` (foto de la oferta y del renglón) y `PriceListService` (foto del renglón) → **422** «La compañía no maneja fotos de productos; actívelo en Ajustes → Operación para subirlas.». |
| Apagado: no se muestran | Un solo punto del servidor: `PriceListQueries.FilePublicIdsAsync` (y el equivalente de `SalesCatalogService`) devuelve vacío → ningún DTO (producto, ficha, ofertas, listas, catálogo de venta) trae identificador de foto. Web: `useProductPhotosEnabled()`; sin fotos desaparecen el bloque «Foto» del producto, la sección de la oferta, las columnas de foto y los recuadros del asistente. App móvil: `SalesCatalogDto.PhotosEnabled` → sin recuadro. |
| Miniatura en la lista de productos | `ProductListItemDto.ImagePublicId` (la que se muestra: la propia o, en un kit sin foto, la de su primera pieza) y **primera columna** «foto» en Productos e inventario (32 px, del alto de la fila; recuadro vacío si no hay). `ProductRowPhoto.tsx`. |
| Foto ampliable con un clic | Componente único `kernel/ui/ZoomPhoto.tsx` (clic/toque → grande ahí mismo; X, clic fuera o Esc cierran; el clic no llega a la fila). Lo usan `StoredImage` (ficha del producto, ofertas, listas de precios), la lista de productos y el asistente (que antes ampliaba con el mouse encima). App móvil: toque en la foto de la oferta → ventana con la foto grande. |
| Contrato | `web-app/openapi.json` y tipos regenerados (+`productPhotosEnabled`, `ImagePublicId`, `photosEnabled`). |

## Cómo se probó
- Servidor (Release, `RunAnalyzers=false`): nueva prueba `ProductImageTests.A_company_without_photos_cannot_upload_and_the_api_returns_none_but_keeps_them` (sube, apaga → 422 al subir y el API no devuelve la foto, enciende → reaparece); `WmsContractsTests` (firma de `ProductListItemDto`); `FrozenSqlTests` y OpenAPI.
- Web: `ZoomPhoto.test.tsx` (el mouse encima no amplía; clic sí; Esc/X/clic fuera cierran; el clic no llega a la fila), `ProductListScreen.test.tsx` actualizado (columna de foto), `tsc` y `oxlint` limpios.

## Decisiones a revisar
1. **Encendido por omisión** (como era; Advance Solutions ya usa fotos). Las compañías que no quieran fotos lo apagan. Si prefieres lo contrario para compañías nuevas, es cambiar el `DEFAULT` y el valor inicial del alta.
2. **Apagado oculta, no borra**: las fotos subidas se conservan y reaparecen al encender.
3. La foto **se amplía con clic** (también en el asistente, que antes usaba el mouse encima). En la app móvil, con un toque.
4. La app de almacén, la app de choferes y los portales no muestran fotos de producto hoy; no se tocaron.
5. La miniatura de la lista cuesta dos consultas más por página (foto efectiva y su archivo); solo con fotos encendidas se devuelven.

## Cómo se verificó en pantalla (Playwright, API sin correo)
`web-app/e2e/lote240-fotos-de-productos.spec.ts` (2/2) mide y mira: la miniatura es de 24 px, la fila con foto y la sin foto miden lo mismo que cualquier fila (≤ 64 px y casi iguales), el mouse encima no amplía y el clic sí (Esc cierra), con el ajuste apagado desaparece la columna y el bloque «Foto», subir es 422 y al encender vuelve. Capturas `l240-fila-con-foto`, `l240-lista-miniatura`, `l240-foto-ampliada` y `l240-lista-sin-fotos` en `docs/manual/frontend/img/`.

**Error que dejó pasar la primera entrega (corregido):** el recuadro de «sin foto» llevaba la clase `empty`, que hereda el `min-height: 220px` global de `base.css`, y cada fila quedó de ~230 px. Se corrigió con una clase propia (`pl-rowphoto-none`) y por eso el dueño dejó una regla: todo cambio de pantalla se cierra con Playwright mirando la captura (ver `CLAUDE.md`).

## Qué quedó fuera
Manual en inglés y sus capturas, y la prueba en la app móvil real (solo tipos y lógica; los errores de `tsc` de `app-movil` son de rutas de expo-router y ya estaban).
