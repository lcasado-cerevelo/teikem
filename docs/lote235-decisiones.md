# Lote 235 — Almacén de la orden, almacén principal y disponible al escoger productos (2026-11-10)

Decisión de Luis. Backend, web, app de vendedores, manual y FAQ. **Las pruebas quedaron escritas y NO se corrieron** (orden del dueño): las corre el coordinador.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Base (`logistica-db-update.sql`, sección «2026-11-10 — Lote 235») | `dbo.Warehouse.IsMain` (BIT, 0) + índice único filtrado `UX_Warehouse_Main` (uno por compañía). No marca ninguno: sin marca, el principal es el activo más antiguo (lo de hoy). Solo agrega; idempotente (corrida 3 veces sobre una base local nueva). |
| Almacén principal | `Warehouse.IsMain`; `WarehouseRules.MainWarehouseId` (el marcado entre los activos o el activo más antiguo). `PATCH /warehouses/{id}` acepta `isMain: true` (desmarca el anterior en la misma transacción; `false` → 400 «Para cambiar el almacén principal, marque otro almacén como principal.»). `WarehouseDto.isMain` = principal **efectivo**. Dar de baja el principal le quita la marca. Pantalla: distintivo «Principal» en la lista y en la ficha, botón «Hacer principal» (`warehouse.manage`). |
| Orden sin almacén | `SalesOrderService.ResolveWarehouseAsync`: sin `warehousePublicId` toma el **principal** (antes: el primero activo). |
| Disponible | `GET /api/v1/sales-orders/stock` (`sales.view`, SALES_ORDERS; `SalesStockService`): almacenes activos con `isMain` y disponible para vender por producto y almacén, **en una consulta para todo el catálogo** (una agrupación de saldos + los componentes de los kits). Misma cuenta que Solicitar (en mano − reservado, posición y zona activas, sin cuarentena / cruce de muelle / renta). Kit tipo KIT (de oferta) = kits completos con sus piezas (`SalesStockRules.KitsAvailable`); kit armado = su existencia. Lo que no viene es 0. |
| Pantallas de venta | Pedido guiado y «Nueva orden» (y la app): con un almacén no se muestra el campo; con varios sale **ya escogido con el principal**. «Disponible: N» por renglón / línea (lista, tarjetas de oferta, carrito), distintivo de backorder y nota de otros almacenes (`StockInfo`, reglas en `features/sales/stockRules.ts`; copia en `app-movil/src/utils/salesStockRules.ts`). |
| Contrato | `web-app/openapi.json`: 1 ruta y 6 esquemas (4 nuevos + `isMain` en `WarehouseDto` y `WarehousePatchRequest`), fusionados del swagger del API en Development sin llave de Brevo; `schema.d.ts` regenerado en web-app y copiado igual a app-almacen y app-cliente. |

## Decisiones (para ratificar)
1. **Pedir más de lo disponible se permite (cambio de Luis sobre el pedido original).** No se bloquea: el renglón queda marcado en rojo con «Backorder: N» y «Solo hay X disponibles en {almacén}; N quedan en backorder.» (lista, tarjeta, carrito, resumen del paso de entrega y línea de «Nueva orden»). Motivo: el vendedor puede saber que viene una compra de camino. La regla está en un solo sitio por plataforma (`stockRules.ts` y su copia móvil; espejo en `SalesStockRules` del dominio); el servidor al solicitar valida como siempre.
2. **El pedido guiado manda `allowBackorder` directamente** cuando alguna línea del carrito pasa de lo disponible al enviar (el vendedor ya vio el distintivo): no se pregunta dos veces. El diálogo de faltante de siempre queda para cuando el inventario cambió entre la captura y el envío. **«Nueva orden» no cambia**: «Guardar y solicitar» sigue abriendo el diálogo (ahí ya era una sola pregunta).
3. **Endpoint aparte** (`/sales-orders/stock`) en vez de ampliar el catálogo: no depende del cliente ni del almacén (trae todos los almacenes), así que cambiar el almacén de la orden no vuelve a consultar, sirve igual a «Nueva orden» (que no usa el catálogo por marca) y la nota de otros almacenes sale de la misma respuesta. Se refresca a los 30 s y al guardar/solicitar una orden.
4. **`isMain` del almacén es el principal efectivo** (el marcado o, sin marca, el más antiguo): la lista siempre muestra exactamente un «Principal». Por eso el principal no se «desmarca»: se marca otro.
5. **El campo Almacén de las pantallas de venta sale de `/sales-orders/stock`**, no de `/warehouses`: el vendedor no necesita permiso de almacenes. Si esa consulta falla, «Nueva orden» muestra el buscador de almacén de siempre y el pedido guiado no muestra el campo (el servidor pone el principal).
6. **Disponible de un kit de oferta con piezas cambiadas** en la orden (sustitución o ajuste): la pantalla lo calcula con las piezas del catálogo; la cuenta exacta la hace Solicitar con las piezas de la orden.
7. **Serie**: los productos por serie no se venden por orden de venta; si tienen saldo viajan en la respuesta pero ninguna pantalla los ofrece.

## Pruebas ESCRITAS, sin correr
- xunit `tests/Teikem.Tests/Lote235Tests.cs` (6): regla del principal; reglas del disponible y del aviso de backorder; un solo principal (marcar otro desmarca, `false` → 400, almacén de baja → 422); la orden sin almacén toma el principal; disponible por almacén (reservado, posición inactiva, cuarentena, inventario de un cliente, almacén de baja; igual a `SellableAsync`); kit de oferta = kits completos por almacén. `WmsContractsTests`: firmas de `WarehouseDto` y `WarehousePatchRequest` con `IsMain`.
- vitest: `features/sales/stockRules.test.ts`; `features/sales/wizard/Stock.test.tsx` (campo con el principal por omisión, disponible, nota, backorder marcado sin bloquear en lista / carrito / resumen, recorrido completo con `allowBackorder`, un solo almacén); `features/sales/OrderModal.stock.test.tsx` (3); `features/warehouse/warehouseAdmin.test.tsx` (3 casos nuevos: distintivo y «Hacer principal»); `ClientStep.test.tsx` (texto de ayuda actualizado).
- App móvil: bloque nuevo al final de `app-movil/scripts/test-sales-order.mjs`.

## Lo que sí se comprobó
- `dotnet build` de API y pruebas: 0 errores. `tsc -b` (web-app) y `tsc --noEmit` (app-movil): limpios.
- Base local nueva `TeikemL235` con `db-init` y la sección 235 corrida dos veces más (EXIT 0). Con el API local sin correo, por curl: la lista de almacenes con `isMain`, «hacer principal» (el anterior se desmarca; el índice único no estorba), `isMain: false` → 400 con su mensaje, y `GET /sales-orders/stock` → 200 con los almacenes y su principal (la base no tenía existencias: la consulta agrupada corre en SQL Server, pero las cantidades solo están cubiertas por las pruebas en memoria, sin correr).

## Pendiente
- Correr todas las pruebas de arriba, `npm run check` y el CI.
- Capturas del manual (paso 1 con el campo Almacén, paso 2 con «Disponible» y «Backorder», Almacenes con «Principal»).
- La app de almacén y la app del cliente no cambian (solo reciben el `schema.d.ts`).
