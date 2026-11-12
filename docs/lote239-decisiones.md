# Lote 239 — Importar órdenes de compra (genérico y QuickBooks) y borrar borradores vacíos (2026-11-12)

Pedidos de Luis en la sesión del 2026-11-12. API, web, manual y FAQ.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Importador de órdenes de compra | Botón **«Importar»** en Almacén → Órdenes de compra. Un solo importador con **origen**: archivo genérico (CSV/Excel) o **QuickBooks**. `POST /api/v1/purchase-orders/import` (multipart: `file`, `source`, `status`, `warehousePublicId`, `commit`). Código: `PurchaseOrderImportRules` (reglas puras, Domain), `PurchaseOrderImportService`, `PurchaseOrderImportController`, `PurchaseOrderImportModal.tsx`. |
| La orden de compra no mueve inventario | «Completada» al importar = **enviada** (`SENT`, lista para recibir). El inventario sigue subiendo solo con el recibo. |
| SKU de QuickBooks = SKU de Teikem | Clave de comparación sin guiones, espacios ni signos («NECH 1001» = «NECH-1001»; «PD-23*36/15/10» = «PD-23-36/15/10»); se avisa en la fila. |
| Producto o proveedor que no existe | **La orden se bloquea** (no se crea), con el motivo y SKU parecidos; el resultado trae la lista de lo que falta (botón «Descargar lo que falta», CSV). No se da de alta ningún producto a medias. |
| No duplica | La orden creada guarda en sus notas `[QB:proveedor|número]`; el mismo archivo no la repite. |
| Borrar un borrador sin productos | `DELETE /api/v1/sales-orders/{publicId}` (`sales.create`): borra la orden y su historial de estatus. Con productos o ya solicitada → 409. Botón **«Borrar orden»** en la ventana y en el asistente (en vez de «Cancelar» cuando no hay productos). |
| Contrato | `web-app/openapi.json` (+2 rutas, +4 esquemas) y tipos regenerados. |

## Cómo se probó
- Servidor (Release, sin analizadores por un cierre del compilador de esta máquina): `PurchaseOrderImportTests` (13: reglas puras + servicio contra InMemory: bloqueo por producto/proveedor, alias de SKU, recibo omitido, estatus enviada sin mover inventario, no duplica, formato genérico, errores de origen/estatus), `SalesOrderTests` (nueva: el borrador vacío se borra y el que tiene productos o está cancelado no), `FrozenSqlTests`, contratos y OpenAPI: 400/400.
- **Contra el archivo real** `Solutions Purchases.CSV` (con los productos del export de Teikem del 5 nov y los proveedores de QuickBooks): 19 documentos → 9 listos, 2 bloqueados (`101010` y `53310/53350`, que no existen en Teikem) y 8 omitidos (7 recibos y 1 crédito). Una factura con costos de más de 4 decimales se importa redondeando y avisando.
- Web: `tsc` y `oxlint` limpios; Vitest de ventas, compras e i18n 188/188.

## Decisiones a revisar
1. **Facturas (Bill) se importan como orden de compra** además de las órdenes (Purchase Order): el archivo que hay son facturas y recibos. Con un archivo de órdenes reales de QuickBooks («Open Purchase Order Detail») habría que afinar el adaptador (número de PO, fecha de entrega).
2. **Las bloqueadas no se guardan**: quedan en el resultado y se vuelven a importar con el mismo archivo cuando exista lo que falta. No hay una lista persistente de «bloqueadas» ni botón para desbloquear (habría que guardar la orden incompleta).
3. **La identidad para no duplicar va en las notas de la orden** (sin columna nueva). Si el usuario edita las notas y borra la marca, el mismo archivo la vuelve a crear.
4. **`53310/53350`** (un renglón de QuickBooks para dos productos) se bloquea: no se puede repartir la cantidad. Hay que separarlo en el archivo.
5. **El borrado es físico** (el borrador vacío no tiene reservas, recolecciones ni facturas); queda la bitácora de auditoría. El número OV no se reutiliza.
6. Los costos con más de 4 decimales se redondean (aviso), no se bloquean.
7. Manual en inglés y capturas del importador: pendientes (no se tomaron capturas).

## Qué quedó fuera
Importar desde otros orígenes (solo QuickBooks y genérico), alta de productos faltantes desde el importador, lista persistente de órdenes bloqueadas, prueba de Playwright del importador y del botón «Borrar orden».
