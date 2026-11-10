# Lote 236 — Disponible del kit desglosado por pieza y carrito fijo del pedido guiado (2026-11-10)

Dos pedidos de Luis. Backend (contrato), web, app de vendedores, manual y FAQ. **Las pruebas unitarias quedaron escritas y NO se corrieron** (orden del dueño); lo único que se corrió es el recorrido Playwright del lote.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Contrato | `GET /api/v1/sales-orders/stock` trae además `kits[]` (`kitPublicId`, `components[]`: `productPublicId`, `sku`, `name`, `qtyPerKit`, en el orden del kit): solo los kits que se arman al pedir (tipo KIT), aunque no tengan existencia. El disponible de cada pieza ya viajaba en `products[]`. Sin SQL nuevo. `web-app/openapi.json`: 2 esquemas nuevos + `kits` en `SalesStockDto`, fusionados del swagger del API local (Development, sin Brevo); `schema.d.ts` regenerado y copiado a app-almacen y app-cliente. |
| Regla pura | `kitStock` / `orderKitPieces` / `lineStock(…, pieces)` en `web-app/src/features/sales/stockRules.ts` (copia idéntica en `app-movil/src/utils/salesStockRules.ts`): por pieza, cantidad por kit, disponible, kits que da y si limita; el disponible del kit = lo que da la pieza que limita (misma cuenta que `KitRules.CanBuild`). |
| Pantallas | `StockInfo` (web y app): el kit de oferta dice «Se pueden armar: N» y lista sus piezas («52800 Tirillas × 6 — 14,022 disp.», «Limita» en la que limita); con más de 3 piezas, plegado («Ver piezas (N) · limita {SKU}»). Tarjeta de oferta, carrito del pedido guiado, línea de «Nueva orden» y app de vendedores. El kit armado no se desglosa («Disponible: N»). |
| Carrito fijo | `wizard.css` (`.wz-foot`, < 720 px) y dos variables en `base.css` (`--stage-px`, `--stage-pb` en `.stage`; los valores no cambian). |

## Causa del defecto del carrito fijo
`.stage` (el área con scroll) tiene relleno: 12 px a los lados y **80 px abajo**. Un `position: sticky; bottom: 0` se pega al borde del **contenido** del contenedor, no al de lo visible: el bloque quedaba 80 px por encima del borde inferior y 12 px corto a cada lado, y por esa franja (y los márgenes) se veía pasar la lista. Medido antes: `gapBelow 80, gapLeft 12, gapRight 12`, con renglones, casillas y botones de la lista visibles por debajo. Arreglo: `bottom: -80px` (se pega a lo visible), márgenes negativos a los lados y abajo y el relleno lateral por dentro, fondo opaco del tema, sombra hacia arriba. Al final del scroll el bloque queda en su sitio, después del último renglón.

## Decisiones (para ratificar)
1. **Kemi y la cinta roja de TEST quedan por encima del bloque fijo** (aclaración de Luis durante el lote: la cinta se ve siempre): no se les reserva espacio ni se sube el bloque. A 360–390 px la cinta tapa parte de Atrás / Siguiente en TEST; en producción no hay cinta y Kemi se puede arrastrar.
2. **Se amplió el contrato** en vez de pedir `kits/{id}/substitutes` por cada tarjeta: una consulta por kit en una marca con muchas ofertas, y la app y «Nueva orden» necesitan lo mismo.
3. **Con una pieza cambiada en la orden, también el número del kit se cuenta con la pieza real** (antes, decisión 6 del Lote 235, se contaba con el catálogo): «Se pueden armar», backorder, nota de otros almacenes y el `allowBackorder` del pedido guiado. Solicitar sigue haciendo la cuenta definitiva.
4. **Empate**: si varias piezas dan el mismo mínimo, todas llevan «Limita».
5. **Más de 3 piezas = plegado** (`KIT_PIECES_UNFOLDED`).
6. El resumen del paso de entrega (solo backorder) no pinta el desglose.

## Lo que sí se comprobó
- `dotnet build` (API Release y proyecto de pruebas): 0 errores. `tsc -b` (web-app) y `tsc --noEmit` (app-movil): limpios. `npm run help:index`.
- **Playwright** `web-app/e2e/lote236-carrito-fijo.spec.ts` contra base nueva `TeikemL236` (db-init), API local en Development **sin llave de Brevo**, web con vite: 7 de 7. A 390×844 y 360×740, claro y oscuro, a medio scroll (dos puntos), al final y con el carrito abierto: `gapBelow ≤ 0.5`, `gapLeft = gapRight = 0`, fondo opaco, ningún elemento de la lista por debajo ni a los lados (rectángulos + `elementFromPoint`), último renglón sin tapar al final, sin scroll horizontal. Desglose: «Se pueden armar: 2,337», piezas, «Limita», plegado con 6 piezas, backorder 63 al pedir 2,400, carrito y «Nueva orden». El mismo recorrido en modo `L236_PHASE=antes` reprodujo el defecto. La base se borró al terminar.

## Pruebas ESCRITAS, sin correr
- xunit `tests/Teikem.Tests/Lote236Tests.cs` (3).
- vitest `features/sales/stockRules.test.ts` (bloque nuevo, 7 casos) y `features/sales/StockInfo.kit.test.tsx` (7).
- App móvil: bloque nuevo al final de `app-movil/scripts/test-sales-order.mjs`.
- Las del Lote 235 siguen sin correr; `lineStock` solo agrega `kit` cuando es un kit de oferta, así que sus comparaciones exactas no cambian.

## No comprobado
- La app de vendedores en un teléfono (solo tipos).
- La sustitución de una pieza en pantalla con el desglose (cubierta por pruebas sin correr; el recorrido no sustituye).
- La nota «Hay disponible en {otro almacén}» para un kit con datos reales (la base del recorrido tiene un almacén; cubierta por pruebas sin correr).
- Capturas del manual (`docs/manual/frontend/img`): pendientes.
