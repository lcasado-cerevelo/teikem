# Lote 241 — Análisis de ventas: rutas, representantes, cuotas, líneas, historial de QuickBooks y API (2026-11-12)

Backend del «Análisis de ventas» pedido por Luis: estructura nueva (no campos personalizados), importador del historial de ventas y compras de QuickBooks (CLI y por el
API), las dos vistas que hoy salen de sus Excel («Por representante» = `Analisis_Ventas_Octubre_2026.xlsx`; «Movimiento de productos» = `Movimiento_de_productos.xlsx`),
series para gráficos, estado del historial y fuente de datos para Vistas / Indicadores / Gráficos. **Sin frontend ni manual** (los hace otro lote).

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Base (`logistica-db-update.sql`, sección «2026-11-12 — Lote 241») | Tablas `SalesRoute`, `SalesRep` (nombre, ruta, usuario opcional), `SalesQuota` (representante + mes → $), `ProductLine` + `ProductLineBrand` (marca → línea, una marca en UNA línea), `SalesAnalysisSetting` (fecha de corte, días de entrega, nivel de servicio z), `SalesHistoryBatch`, `SalesHistoryLine`, `PurchaseHistoryLine`, `SalesHistoryAlias`; `Client.SalesRepId`; índice `IX_Invoice_IssueDate`; EntityType `SALES_ROUTE`, `SALES_REP`, `SALES_QUOTA`, `PRODUCT_LINE`, `SALES_ANALYSIS`, `SALES_ANALYSIS_LINE`; permisos `sales.analysis.view` / `sales.analysis.manage` a los roles YA clonados (marca `L241_SALES_ANALYSIS_PERMS`); migración ÚNICA del campo personalizado «Representante» (`sales_rep`) a `SalesRep` + `Client.SalesRepId` (marca `L241_SALES_REP_FROM_FIELD`; el campo se deja). Idempotente (corrida varias veces seguidas sobre la misma base, EXIT 0). |
| Dominio (puro) | `SalesAnalysisRules` (ABC, estado, mensual, semanal, cobertura, saldo proyectado, punto de reorden sugerido, tendencia, cuota vigente, conciliación, prorrateo de kits con la regla del IIF del lote 225, duplicados Bill / Item Receipt, nombres y clases, mensajes exactos), `QuickBooksDetailReportParser` (CSV de QuickBooks: comillas, secciones anidadas, Windows-1252, totales que no cuadran), entidades en `SalesAnalysisEntities.cs`. |
| Lector de líneas (`SalesAnalysisLineReader`) | Historial antes de la fecha de corte + facturas de Teikem desde ella (kits repartidos, notas de crédito restan, envío aparte). Compras: historial + recibos contra órdenes de compra. |
| Servicios | `SalesAnalysisService` (vistas, gráficos, estado, panel del Pulso), `SalesAnalysisSetupService` (lo que se administra), `SalesHistoryImportService` + `SalesHistoryImportRunner` (verbo `import-sales-history`). |
| API | `SalesAnalysisController` (`/api/v1/sales-analysis/*`, módulo `SALES_ORDERS`), 29 rutas. `ClientDetailDto` + `salesRepId`, `salesRepName`, `salesRouteName`. Panel Ventas del Pulso (`GET /sales-orders/pulse`) + `monthSales`, `monthQuota`, `routes.monthSales`. |
| Fuente de datos | `SALES_ANALYSIS_LINE` «Líneas de venta para análisis» (campo de fecha `Date`, lectura `sales.analysis.view`, módulo `SALES_ORDERS`). Se probó una Vista agrupada por ruta sobre octubre: total 141,902.65. |
| Permisos | `sales.analysis.view` (TenantAdmin, SalesSupervisor, SalesOffice) y `sales.analysis.manage` (TenantAdmin, SalesSupervisor), categoría BILLING, los dos implican `sales.view` (la pantalla vive en Órdenes de venta). Catálogo 159 → 161 (conteos de 5 pruebas existentes actualizados). |
| Contrato | `web-app/openapi.json`: solo lo nuevo (29 rutas `/api/v1/sales-analysis/*`, 49 esquemas nuevos + `ClientDetailDto` y `PulseSalesPanelDto`) fusionado del swagger del API en Development sin llave de Brevo (`merge_openapi_238.py`); `schema.d.ts` regenerado en web-app y copiado igual a app-cliente y app-almacen (eran idénticos). |

## Contrato para el frontend

Montos en dólares (2 decimales); proporciones como fracción (`0.25` = 25 %); meses `AAAA-MM`; fechas `AAAA-MM-DD`. **Las propiedades `null` no vienen en el JSON** (opción
global del API). Ver: `sales.analysis.view` o `.manage`; administrar: `sales.analysis.manage`. Errores en ProblemDetails con el mensaje en `title`.

### Vistas
| Método y ruta | Qué devuelve |
|---|---|
| `GET by-rep?month=2026-10&compareMonth=2026-09` | `SalesByRepDto` (por omisión: el último mes completo contra el anterior). |
| `GET transactions?month=&repId=&q=&skip=0&take=100&all=false` | `PagedResult<SalesTransactionDto>` (`items,total,skip,take`). `repId=0` = sin representante. `all=true` = todo el mes (exportar). |
| `GET product-movement?fromMonth=&toMonth=&asOf=&leadTimeDays=&serviceLevelZ=&topClients=5` | `ProductMovementDto` (por omisión los 12 meses que terminan en el del día de corte; `asOf` = hoy; días de entrega y z = los de Ajustes). |
| `GET charts?fromMonth=&toMonth=&month=&topProducts=6` | `SalesChartsDto` (por omisión los 12 meses que terminan en el último completo). |
| `GET status` | `SalesAnalysisStatusDto` (fecha de corte, lotes, sin representante, marcas sin línea, sin emparejar, traslape). |

Ejemplo `by-rep` (recortado; datos reales de octubre en la base local):
```json
{ "month": "2026-10", "compareMonth": "2026-09", "cutoffDate": "2026-11-06", "warnings": [],
  "reps": [ { "routeId": 2, "routeName": "Norte", "repId": 2, "repName": "CINDY OLIVAS DAVILA", "compareSales": 11348.22, "sales": 16744.75,
              "change": 5396.53, "changePct": 0.4755, "quota": 16500.00, "compliance": 1.0148 } ],
  "totals": { "compareSales": 109057.44, "sales": 141902.65, "change": 32845.21, "changePct": 0.3012, "quota": 140700.00, "compliance": 1.0085 },
  "reconciliation": { "routesTotal": 141902.65, "unassigned": 0, "shippingAndOther": 0, "monthTotal": 141902.65, "difference": 0.00,
                      "productSales": 147359.82, "discounts": -5457.17, "shipping": 0, "other": 0, "unassignedClients": 0, "unassignedCompare": 33734.60 },
  "lines": [ { "line": "Prodigy", "isDiscounts": false, "sales": 134341.20, "shareOfNet": 0.9467, "shareOfGross": 0.9117,
               "leaderRepId": 1, "leaderName": "ADVANCE SOLUTIONS GROUP LLC", "leaderRoute": "Cadenas", "leaderSales": 58866.41 },
             { "line": "Descuentos sin línea", "isDiscounts": true, "sales": -5457.17, "shareOfNet": -0.0385, "shareOfGross": -0.0370 } ],
  "repLines": [ { "routeName": "Cadenas", "repId": 1, "repName": "ADVANCE SOLUTIONS GROUP LLC", "line": "Padimont", "isDiscounts": false, "sales": 0, "shareOfRep": 0 } ],
  "repProducts": [ { "routeName": "Norte", "repId": 2, "repName": "CINDY OLIVAS DAVILA", "line": "Global Imports", "productId": 3, "sku": "00614-1",
                     "product": "GLOBAL COTTON ROUND 80CT.", "lineClass": "PRODUCT", "units": 60.0000, "sales": 65.40 } ],
  "routeProducts": [ { "routeId": 2, "routeName": "Norte", "repNames": "CINDY OLIVAS DAVILA", "line": "Global Imports", "productId": 21, "sku": "ARC609517",
                       "product": "AMERICAN RED CROSS FIRST AID BANDAGES 30CT.", "units": 12.0000, "sales": 16.92 } ] }
```
- `reps`: una fila por representante activo (y los inactivos con venta), en el orden de la ruta (`sortOrder`). Venta = producto + descuentos del historial.
- `reconciliation`: `routesTotal + unassigned + shippingAndOther = monthTotal`; `difference` debería ser 0. Lo de clientes sin representante (o del historial sin emparejar) va en `unassigned`, nunca en una fila.
- `lines`: las líneas por venta (la marca sin línea es su propia línea; sin marca = «Sin marca»; ítem del historial sin emparejar = «Producto sin emparejar») y, aparte, «Descuentos sin línea» (`isDiscounts`). `shareOfNet` = sobre la venta neta (con descuentos), `shareOfGross` = sobre la venta de producto.
- `repLines`: cada representante (más «Sin representante» si vendió) × cada línea de `lines`, con 0 donde no vendió; `shareOfRep` sobre su total.
- `repProducts`: representante × producto (también los ítems de descuento del historial, `lineClass = DISCOUNT`).
- `routeProducts`: cada ruta (agrupa sus representantes en `repNames`; «Sin representante» con `routeId` ausente) × cada producto vendido en el mes, con 0 donde no vendió.

Ejemplo de un producto de `product-movement` (`products[]`):
```json
{ "productId": 39, "sku": "NECH1003", "name": "Padimont Gloves Large 1/100ct.", "category": "GLOVES", "brand": "PADIMONT", "line": "Padimont", "matched": true,
  "abc": "B", "status": "STABLE", "price": 5.0000, "cost": 3.5000, "marginPct": 0.3, "onHand": 835.000, "reserved": 0.000, "available": 835.000,
  "units": 2765.0000, "sales": 14125.85, "shareOfSales": 0.0104, "monthlyAverage": 530.00, "monthlyMin": 440.0000, "monthlyMax": 690.0000, "bestMonth": "2026-08",
  "monthsUsed": 5, "weeklyAverage": 106.25, "weeklyStdDev": 39.67, "weeksUsed": 8, "weeklyConfidence": "HIGH", "coverageWeeks": 7.9, "coverageMonths": 1.8,
  "projected1": 373, "projected2": 0, "projected3": 0, "reorderPoint": 140.000, "suggestedReorderPoint": 132, "trend": -0.2348, "clients": 152, "invoices": 215,
  "topClient": "Farmacia Deborah #2", "topClientUnits": 240.0000, "lastSale": "2026-11-05", "pendingPurchase": 0, "lastMonthUnits": 440.0000 }
```
- `status`: `NO_SALES` (Sin ventas), `STALE` (Parado, +45 días), `NEW` (Nuevo), `RISING` (Subiendo), `FALLING` (Bajando), `STABLE` (Estable). `abc`: `A`/`B`/`C`/`-`.
- `weeklyConfidence`: `NONE` (sin venta en las 8 semanas: sin base), `LOW` (menos de 3 semanas de historia), `SPORADIC` (vende la mitad o menos de las semanas), `HIGH`.
- `reorderPoint` = el mínimo del producto (`Product.MinQty`); `suggestedReorderPoint` = la fórmula de la hoja «Reorder point» con los días de entrega y z de la respuesta.
- `matched = false`: ítem del historial que no emparejó (sin `productId`; enlazarlo con `unmatched/link`).
- Matrices: `months` (encabezados), `unitsByMonth` / `salesByMonth` / `purchasesByMonth` (una serie por producto: `values` en el orden de `months`, `total`; en unidades también `min`/`max`/`average` de los meses completos), `topClients` (los N primeros por producto: `rank`, `client`, `units` facturadas, `shareOfProduct`). Además `weeksFrom`/`weeksTo` (la ventana de 8 semanas) y `lastCompleteMonth`.

`charts`: `months`, `totalByMonth` (`{key:"TOTAL",label,values}` venta neta), `byLineByMonth[]` (una serie por línea, más los descuentos), `month`, `repVsQuota[]`
(`rep, route, sales, quota, compliance`), `lineShare[]` (`line, sales, share` sobre la venta de producto), `topProductUnits[]` (los N principales **por venta $** con sus
unidades por mes), `coverage[]` (`sku, name, abc, available, weeklyAverage, coverageWeeks`, de menor a mayor cobertura, al día de hoy).

### Administración
| Método y ruta | Cuerpo / qué hace |
|---|---|
| `GET/PUT settings` | `{cutoffDate?, clearCutoff?, leadTimeDays?, serviceLevelZ?}` → `{cutoffDate, leadTimeDays, serviceLevelZ}` (5 días y 1.645 por omisión). |
| `GET routes?includeInactive` · `POST routes` · `PATCH routes/{id}` · `POST routes/{id}/deactivate\|reactivate` | `{name, sortOrder?}` → `SalesRouteDto {id,name,sortOrder,isActive,repCount}`. |
| `GET reps?includeInactive` · `POST reps` · `PATCH reps/{id}` · `POST reps/{id}/deactivate\|reactivate` | `{name, routeId?, clearRoute?, userId?, clearUser?}` → `SalesRepDto {id,name,routeId,routeName,userId,userName,isActive,clientCount,currentQuota}`. |
| `POST reps/from-custom-field` | Pasa el campo «Representante» a representantes y enlaza los clientes sin representante → `{repsCreated, clientsLinked, clientsAlreadyLinked}`. |
| `GET reps/{id}/quotas` | Las cuotas propias del representante mes por mes. |
| `GET quotas?month=` · `PUT quotas/{month}` | Mes: `rows[] {repId, repName, routeName, repActive, amount (la propia), effective (la que vale), effectiveFrom}`; edición en bloque `{items:[{repId, amount}]}` (`amount: null` = quitar la del mes). |
| `GET lines` · `POST lines` · `PATCH lines/{id}` · `POST lines/{id}/deactivate\|reactivate` · `PUT lines/{id}/brands` | `{name, sortOrder?}`; marcas `{brands:["ABELLE","GLOBAL"]}` (reemplaza la lista). |
| `GET brands` | Las marcas de los productos con su línea efectiva (`hasLine=false` primero). |
| `GET clients?repId=&unassigned=&q=&skip=&take=` | Clientes activos con su representante (`PagedResult<SalesClientRepDto>`). |
| `PUT clients/{clientId}/rep` · `POST clients/assign-rep` | `{repId}` / `{clientIds:[…], repId}` (`repId: null` = quitar) → `{updated, repId, repName}`. |
| `POST unmatched/link` | `{kind:"CLIENT"\|"PRODUCT", raw, clientId?\|productId?}` → `{kind, raw, relinked, …}`: re-enlaza las líneas y queda para las próximas importaciones. |
| `POST import` (multipart) | `salesFile`, `purchasesFile`, `dryRun` (por omisión **true**), `cutoffDate?`, `repsFromCustomField?` → `SalesHistoryImportReportDto` (lo mismo que imprime el CLI). |

### Mensajes exactos (código HTTP)
| HTTP | Mensaje |
|---|---|
| 400 | «El mes debe tener la forma AAAA-MM (por ejemplo 2026-10).» · «El mes final no puede ser anterior al inicial.» · «El rango no puede pasar de 36 meses.» |
| 400 | «Escriba el nombre de la ruta.» · «Escriba el nombre del representante.» · «Escriba el nombre de la línea.» · «El nombre no puede pasar de 100 caracteres.» |
| 400 | «La ruta está desactivada: actívela o escoja otra.» · «El representante está desactivado: actívelo o escoja otro.» · «La línea está desactivada: actívela antes de cambiar sus marcas.» · «El usuario no pertenece a esta compañía.» |
| 400 | «La cuota debe ser cero o mayor, con hasta 2 decimales.» · «No se pueden mandar más de 2000 renglones en una sola llamada.» · «Indique los clientes (clientIds).» · «Escriba la marca.» · «La marca no puede pasar de 100 caracteres.» |
| 400 | «Los días de entrega deben estar entre 1 y 365.» · «El nivel de servicio (z) debe ser mayor que 0 y hasta 4.» · «La página debe ser 1 o mayor.» · «El tamaño de página debe estar entre 1 y 500.» |
| 400 | «El tipo debe ser CLIENT o PRODUCT.» · «Indique el nombre tal como viene en el historial (raw).» · «Indique el cliente o el producto de Teikem al que se enlaza.» |
| 400 | «Suba al menos uno de los dos archivos (ventas o compras).» · «El archivo no puede pasar de 20 MB.» · «El archivo X no parece un reporte «Sales by Item Detail» o «Purchases by Item Detail» de QuickBooks (faltan las columnas Type, Date, Qty y Amount).» · «El archivo X es un reporte de compras pero se indicó como de ventas.» (y al revés) · «El cuerpo de la solicitud es obligatorio.» |
| 404 | «Ruta no encontrada.» · «Representante no encontrado.» · «Línea no encontrada.» · «Cliente no encontrado.» · «Producto no encontrado.» |
| 409 | «Ya hay una ruta activa con ese nombre.» · «Ya hay un representante activo con ese nombre.» · «Ya hay una línea activa con ese nombre.» · «La ruta tiene representantes activos: páselos a otra ruta antes de desactivarla.» · «El representante tiene clientes asignados: páselos a otro representante antes de desactivarlo.» · «La marca «X» ya está en la línea «Y»: quítela de ahí primero.» |
| 403 | Sin permiso o con el módulo Órdenes de venta apagado (los de siempre). |

Avisos (`warnings` de las vistas y del estado): «No hay fecha de corte y el historial importado (hasta …) se traslapa con las facturas de Teikem (desde …): las ventas de
esos días se pueden estar sumando dos veces. Ponga la fecha de corte en Ajustes del análisis.»; «N línea(s) del historial importado son del … o después: no cuentan …»;
«N cliente(s) activo(s) sin representante …»; «N marca(s) sin línea …»; «N cliente(s) / ítem(s) del historial sin emparejar …».

## Decisiones (para ratificar)
1. **La venta es del representante asignado HOY al cliente** (`Client.SalesRepId`), también para el historial (QuickBooks no trae representante en este reporte). Si un cliente cambia de representante, sus meses pasados se ven con el nuevo. Fecha de factura, por mes.
2. **«Venta» del representante = producto + descuentos** (la venta neta, como el Comparativo del Excel). Envío y otros cargos no son de nadie: van en la conciliación (`shippingAndOther`).
3. **Fecha de corte**: historial ANTES de ella (exclusiva), facturas de Teikem desde ella. Sin fecha de corte cuentan las dos completas y se avisa si se traslapan. El importador puede guardarla (`cutoffDate` del JSON o del formulario).
4. **Facturas de Teikem que cuentan**: emitidas, vencidas o pagadas (activas), por `IssueDate`; solo sus líneas de venta (de una orden de venta) y el envío de la orden. Cantidad × precio de la línea (el descuento ya va en el precio). Un **kit** se reparte entre sus piezas con la MISMA regla del IIF de QuickBooks (lote 225): la foto de la línea de la orden o, si no la tiene y es kit, la receta del catálogo; peso = cantidad por kit × precio de lista; cierre al centavo. Un kit armado (con existencia propia) se vende como cualquier producto.
5. **Notas de crédito**: aprobadas o aplicadas, en el día (de la compañía) en que se aprobaron; restan sus líneas de producto o las que acreditan una línea de venta o de envío; las de concepto libre sin producto (transporte, almacén) no entran.
6. **Historial**: se guarda TAL CUAL (fecha, tipo, número, memo, cliente crudo, ítem crudo, cantidad, precio, monto) con lo que emparejó. Clase: sección de inventario = `PRODUCT`; sección o ítem de descuento = `DISCOUNT`; envío (shipping/delivery/envío/flete) = `SHIPPING`; lo que empareja con un producto = `PRODUCT`; servicios, «Parts» y otros cargos sin producto = `OTHER`; sección desconocida = `PRODUCT` (si no empareja se reporta). Precio en % (descuento 2 %) se guarda vacío; el monto manda.
7. **Emparejar cliente**: enlace a mano → campo `qb_code` de la migración (nombre de QuickBooks) → nombre normalizado (sin acentos, mayúsculas, sin puntos ni comas) → razón social normalizada. Un nombre que normaliza igual a dos clientes NO empareja. **Ítem**: enlace a mano → SKU con la normalización de la migración (`LegacyImportRules.SkuKey`: sin espacios, mayúsculas; «NECH 1003» = «NECH1003»).
8. **Idempotencia**: reimportar el MISMO archivo (misma huella SHA-256) reemplaza su lote. Un archivo DISTINTO del mismo tipo reemplaza TODAS las líneas ya importadas de su rango de fechas [primera, última] (sirve para mandar un reporte más nuevo). Las líneas del historial se borran de verdad al reemplazarse (son copia de un archivo externo, sin bitácora); el lote reemplazado queda `IsActive = 0` con su fecha.
9. **Compras: Item Receipt y Bill**. En QuickBooks un Item Receipt que se factura se CONVIERTE en Bill (la misma transacción): el reporte real no los repite (117 renglones: 74 Bill, 30 Item Receipt, 3 Credit, 10 en cantidad 0; ningún Bill igual a un Item Receipt, y los totales por mes cuadran con el Excel del dueño). Por seguridad, si un Bill tiene el mismo suplidor, número, ítem y cantidad que un Item Receipt, se descarta el Bill (cuenta la fecha en que llegó la mercancía). Las de cantidad 0 se ignoran; los Credit restan.
10. **ABC** por lo acumulado ANTES de cada producto (A si antes de él va menos de 80 %, B menos de 95 %, C el resto): es como sale el Excel (Prodigy Monitor es A con 79.7 % antes). Se calcula sobre la venta $ de producto del rango (sin descuentos), sin incluir los kits (se reparten).
11. **Promedios**: mensual con los meses completos desde el primer mes con venta (los meses sin venta cuentan 0; el mes del día de corte no cuenta). Semanal: las 8 semanas completas (lunes a domingo) antes de la semana del día de corte, usando las semanas desde la primera con venta (así sale la hoja «Reorder point»); desviación poblacional.
12. **Cobertura** = disponible (existencia − reservado, todos los almacenes) ÷ promedio semanal; en meses = semanas ÷ 4.348 (365.25/12/7). **Saldo proyectado** a 1/2/3 meses con ese mismo ritmo (nunca negativo). El Excel de octubre lo proyectaba con la venta de octubre; aquí se usa el ritmo semanal para que cobertura y proyección digan lo mismo (`lastMonthUnits` viene por si se quiere el otro).
13. **Facturas y clientes del producto**: toda factura (tipo Invoice) que lleva el producto, aunque la línea venga en 0 (como el «Resumen»); cliente principal y «clientes por producto» por unidades FACTURADAS (sin restar notas de crédito). Empates: por nombre.
14. **Pendiente por recibir** = pedido − recibido de las órdenes de compra Enviadas o Recibidas en parte. **Compras de Teikem** (desde el corte) = lo recibido contra líneas de órdenes de compra (recibo con aviso de llegada de la orden), al costo de la línea de la orden.
15. **Línea**: la de la marca del producto en la tabla marca → línea; sin línea = su propia marca; sin marca = «Sin marca». Los descuentos del historial (sin producto) van aparte como «Descuentos sin línea».
16. **Cuotas**: una fila por representante y mes; un mes sin cuota vale la del mes anterior más reciente; quitar la de un mes = `IsActive = 0` (vuelve la anterior).
17. **Migración del campo «Representante»**: la sección SQL lo hace UNA vez (y solo enlaza clientes sin representante). Para compañías importadas después, `POST reps/from-custom-field` o `repsFromCustomField: true` en el importador (idempotente). Los representantes nacen sin ruta: se asignan en la pantalla.
18. **Panel Ventas del Pulso**: «venta del mes vs cuota» = venta neta del mes en curso hasta hoy y la suma de las cuotas vigentes de los representantes activos; solo para quien tiene `sales.analysis.view`/`.manage` (si no, no vienen).
19. Todo se agrega en memoria sobre las líneas proyectadas (13 mil del historial + un año de facturas): `by-rep` 0.04–0.10 s, `product-movement` 0.08–0.13 s, `charts` 0.14–0.35 s, `status` 0.09–0.18 s en la base local (calientes; la primera llamada tras arrancar, ~0.25 s).

## Cómo correr el importador (para Luis)
1. En QuickBooks Desktop: Reports → Sales → **Sales by Item Detail** (rango: desde el inicio hasta el último día que se quiera traer; todas las columnas por omisión) → Excel → **Create New Worksheet → «Create a comma separated values (.csv) file»**. Igual con Reports → Purchases → **Purchases by Item Detail**. **Siempre el reporte completo (todos los ítems)**: un archivo nuevo reemplaza todo lo ya importado de su rango de fechas.
2. Copiar los dos `.CSV` al servidor, en una carpeta, y al lado un `config.json`:
   ```json
   {
     "company": "Advance Solutions",          // nombre EXACTO de la compañía en Teikem
     "sales": "SalesByItemDetail.CSV",        // ruta del reporte de ventas (relativa a esta carpeta o absoluta)
     "purchases": "PurchasesByItemDetail.CSV",// ruta del reporte de compras (opcional si solo se trae uno)
     "cutoffDate": "2026-11-16",              // opcional: desde este día cuentan las facturas de Teikem (el historial cuenta ANTES)
     "repsFromCustomField": true              // opcional (true por omisión): pasa el campo «Representante» a representantes
   }
   ```
3. Simulación (no escribe nada): en la carpeta del API, `dotnet Teikem.Api.dll import-sales-history C:\ruta\config.json --dry-run` (en desarrollo: `dotnet run --project src/Teikem.Api -- import-sales-history <config.json> --dry-run`). Revisar en la consola (y en el `historial-ventas-…-simulacion.json` que deja junto al config): líneas leídas, por clase, rango de fechas, que «Total» sea igual a «TOTAL del reporte», los totales por mes contra QuickBooks, y la lista de **clientes e ítems SIN emparejar**.
4. Si hay clientes o productos sin emparejar: crearlos/corregirlos en Teikem (o importar igual y enlazarlos después desde la pantalla, «Enlazar»). Volver a simular hasta que la lista esté bien.
5. Importar de verdad: el mismo comando **sin** `--dry-run`. Salida 0 = bien; 1 = error (el mensaje lo dice; no se escribe nada a medias: cada archivo va en una transacción).
6. Volver a correrlo con el mismo archivo no duplica (dice `REPLACE_SAME_FILE`).
7. En Teikem: asignar las rutas a los representantes, las cuotas del mes y las marcas de cada línea (o por el API de arriba). La fecha de corte se puede cambiar en Ajustes del análisis.

## Lo que se comprobó (base local `TeikemL238`, borrada al final)
- `db-init` limpio y la sección 238 corrida seis veces más, antes y después de cargar datos (EXIT 0 todas); permisos en TenantAdmin / SalesSupervisor (ver y administrar) y SalesOffice (ver); la migración del campo «Representante» con datos: 6 representantes y 402 clientes enlazados; segunda corrida sin cambios.
- Compañía de prueba local hecha con los archivos REALES (solo en esa base): 49 productos del Items list (con su marca, categoría, costo, precio y mínimo; existencia del «Resumen»), 700 clientes del reporte de ventas (uno omitido a propósito para ver «sin emparejar», algunos con otra capitalización y uno con otro nombre y `qb_code`), campo «Representante» con el representante de la hoja «Transacciones».
- Importador: simulación (nada escrito), real (13,350 ventas + 107 compras, ~8 s con el arranque), repetido (`REPLACE_SAME_FILE`, sin duplicar) y un archivo distinto del mismo rango por `POST import` (`REPLACE_RANGE`, 13,350 reemplazadas, 4.9 s).
  Resultado literal de la simulación: «Líneas leídas: 13350 · se guardarían: 13350 … Rango: 2025-12-19 a 2026-11-05 · Total: 1285918.96 · TOTAL del reporte: 1285918.96 · PRODUCT 11542 (1354427.03) · DISCOUNT 1795 (-68634.37) · SHIPPING 11 (132.00) · OTHER 2 (-5.70) · Clientes emparejados: 700 · sin emparejar: 1 · Ítems emparejados: 41 · sin emparejar: 0»; compras: «Líneas leídas: 117 · se guardarían: 107 · cantidad 0 omitidas: 10 · duplicadas omitidas: 0 · Total: 684936.65 = TOTAL del reporte · Ítems sin emparejar: 53310/53350 (PRODIGY CONTROL SOLUTION LOW/HIGH 4ML)».
- **Contra los Excel del dueño**:
  - Octubre: total 141,902.65 = «Total contable octubre»; producto 147,359.82 = la base «% antes descuentos»; descuentos −5,457.17 = «Descuentos sin marca confirmada».
  - Por representante (octubre): Cadenas 58,866.41, Norte 16,744.75, Metro 7,175.18, Este 23,734.63, Sur 20,062.75 **iguales**; Oeste 15,318.93 contra 15,342.09 (−23.16): la nota de crédito de un cliente de la ruta Oeste que el Excel pone en «Crédito sin representante» aquí es de su representante actual (decisión 1). Cuotas y cumplimientos con las cuotas del Excel: iguales.
  - Líneas (Marcas): Prodigy 134,341.20, Padimont 7,950.54, Global Imports 3,109.62, MedPride 1,958.46 **iguales**, con los mismos líderes, rutas y montos; marca × representante igual salvo Oeste/Global Imports (481.32 contra 504.48, la misma nota de 23.16).
  - Septiembre (mes de comparación) NO coincide: el Excel tomó septiembre de otro reporte («referencia por rutas misma fecha», total 142,600.89) y en la base de prueba solo los clientes que compraron en octubre tienen representante (lo de septiembre de los demás, 33,734.60, sale en «sin representante»). El total de septiembre del historial (142,792.04) sí es el de QuickBooks.
  - Movimiento de productos (48 productos, 2025-12 a 2026-11, al 5-nov): unidades, venta $, % de la venta, ABC, estado, margen, existencia, promedio/mínimo/máximo mensual, mejor mes, promedio semanal de 8 semanas, semanas de cobertura, tendencia, facturas, clientes distintos, unidades al cliente principal y última venta: **48 de 48 iguales**. Matrices: unidades por mes 576/576, venta $ por mes 576/576 y compras por mes 576/576 celdas iguales (575/576 después de enlazar a mano el ítem combinado «53310/53350» con 53310 en la prueba del enlace: +5 en abril).
  - Diferencias explicadas: «Reorder point» de 7 productos (051885, 00802-2, 00804-6, 00607-3, 53350, 121212, 979) que en el Items list no tienen mínimo: Teikem muestra el mínimo del producto (vacío) y aparte el sugerido; el nombre del cliente principal sale como se llama el cliente en Teikem (p. ej. el renombrado a propósito) y los empates de «clientes por producto» van por nombre (el Excel no tiene un orden fijo); en los productos sin ventas, clientes/facturas = 0 donde el Excel deja vacío.
  - Gráficos: los 6 productos principales son los del gráfico del Excel (52800, 070120, 24-081028, NECH 1003, NECH 1002, NECH 1004) con las mismas unidades por mes.
- API en Development **sin** llave de Brevo: las 29 rutas, los 400/404/409 de la tabla de arriba, la ficha del cliente con su representante, el panel del Pulso con `monthSales`/`monthQuota`, una Vista sobre `SALES_ANALYSIS_LINE` (octubre por ruta: 141,902.65).
- Pruebas NUEVAS corridas con `--filter` (las únicas que se corrieron): `SalesAnalysisRulesTests`, `QuickBooksDetailReportParserTests`, `SalesAnalysisServiceTests`, `SalesAnalysisCatalogTests` → «Passed! - Failed: 0, Passed: 38, Skipped: 0, Total: 38». La suite completa la corre el coordinador (cambian los conteos de permisos en `WmsCatalogTests`, `FleetCatalogTests`, `TripCatalogTests`, `OrderCatalogTests` y `Analytics/PulseLayoutTests`: 159 → 161).

## Límites conocidos
- La atribución del historial al representante ACTUAL (decisión 1) cambia los meses pasados si se reasigna un cliente; QuickBooks tiene «Sales by Rep Detail», que no se importa.
- Los underpads (`PD-…`) no tienen marca en QuickBooks: para que cuenten en Padimont hay que ponerles una marca de esa línea (en la prueba se les puso «PADIMONT UNDERPADS»); sin marca caen en «Sin marca».
- El reporte de compras trae un ítem combinado («53310/53350») que no es un producto: queda sin emparejar hasta que alguien lo enlace (a uno solo de los dos).
- Facturas de Teikem: solo sus líneas de venta y el envío de la orden; otros cargos manuales en una factura de venta no entran. Compras de Teikem: solo recibos contra órdenes de compra (con aviso de llegada de la orden).
- Las vistas se calculan en cada llamada (sin caché); la fuente `SALES_ANALYSIS_LINE` corta en 20,000 filas (las más recientes), como las demás.
- La importación por el API es sincrónica (unos 5 s para 13 mil líneas) y acepta hasta 20 MB por archivo.
- No se probó contra facturas reales de Teikem en la base local (no hay); el camino de facturas, kits y notas de crédito está cubierto por la prueba `With_a_cutoff_history_counts_before_it_and_teikem_invoices_from_it_with_kits_split_and_credits_subtracted` y la consulta se tradujo en SQL Server (panel del Pulso y estado).

## Frontend (web-app, 2026-11-12)
Pantalla **Análisis → Análisis de ventas** (`/analytics/sales`, `features/salesAnalysis/`), ítem del menú 5.º de Análisis (después de Gráficos), `perm: 'sales.analysis.view|sales.analysis.manage'`, módulo `SALES_ORDERS`. Pestañas en `?tab=` y sección de Configuración en `?sec=`.

| Pieza | Qué hace |
|---|---|
| Aviso de estado (arriba) | De `GET status`: clientes sin representante, marcas sin línea, nombres sin emparejar, sin historial, sin fecha de corte o traslape; cada uno con enlace a la sección donde se arregla. Solo sale si hay algo. |
| Por representante | Mes y mes de comparación (por omisión el que manda el API y el anterior). Tarjetas (venta del mes, cambio, cumplimiento total con barra). Comparativo (DataTable + «Totales y conciliación»: rutas, sin representante, envío y otros, total del mes y el desglose producto/descuentos/envío/otros) con el gráfico venta contra cuota al lado. Líneas (con «No aplica» en descuentos) + dona. Línea por representante (filtros, «ocultar ceros»), Producto por representante y Producto por ruta (filtros ruta, representante, línea y buscador; las filas en cero atenuadas). Transacciones paginadas por el servidor con `exportRows` (`all=true`). |
| Movimiento de productos | Rango de meses. Tarjetas. Resumen con las 34 columnas del contrato agrupadas (Venta, Inventario y cobertura, Reorden, Precio y margen, Por mes, Clientes, Todas, Elegir… con `SearchSelect`), ordenables, chips de ABC/estado/cobertura, sugerido en rojo si lo disponible ya está en él o debajo, confianza del promedio semanal («pocas semanas», «esporádico»). Gráficos de cobertura (15 de menos cobertura) y unidades por mes de los 6 principales. Matrices Unidades / Venta $ / Compras por mes (escala de color, columna fija, se desplazan dentro del cuadro, total por mes). Clientes por producto. «Qué significa cada columna» plegable (los mismos textos de la hoja Notas). |
| Gráficos | Período, mes de las cuotas y los cinco gráficos para presentar; «Imprimir presentación o guardar PDF» (`window.print()` con hoja de impresión limpia: logo claro de la compañía si tiene, nombre, título, período, un gráfico por bloque, sin shell ni Kemi, tema claro siempre; en la presentación los gráficos van sin animación para que el dibujo salga completo). |
| Configuración | Rutas, Representantes (ruta, usuario opcional de `GET /users` si se tiene permiso, «Pasar el campo Representante»), Cuotas (rejilla representante × 12 meses del año, celdas vacías muestran tenue la cuota heredada, «Copiar anterior» por columna, «Guardar (n)» manda solo lo que cambió con un `PUT quotas/{mes}` por mes; montos inválidos bloquean), Líneas y marcas (marcas de cada línea con `SearchMultiSelect` que solo ofrece las sin línea y las suyas; tabla de marcas con las sin línea resaltadas y «Asignar a…» en la fila), Clientes sin representante (paginado por el servidor, casillas, «Elegir los de esta página», asignar o quitar en bloque), Ajustes (fecha de corte con la explicación «el historial cuenta hasta el día anterior y las facturas de Teikem desde este día», días de entrega, nivel de servicio con 90/95/97.5/99 % o z a mano), Importar historial (pasos, lotes vigentes, dos archivos, fecha de corte opcional, simulación primero y «Importar de verdad» solo tras simular los mismos archivos, con confirmación; reporte por archivo: leídas, guardadas, omitidas, fechas, total contra el TOTAL del reporte con «cuadra / no cuadra», por clase o tipo, totales por mes, sin emparejar) y Emparejar a mano (las tres listas del estado; buscador de clientes por `GET sales-analysis/clients?q=` y de productos por `GET /products?search=`, id numérico). Sin `.manage`: aviso de solo lectura, sin Importar ni Emparejar, sin campos ni botones. |
| Exportar a Excel | Botón por vista. Libro 1 (mes): Comparativo (con TOTAL RUTAS y conciliación), Marcas, Representante producto, Ventas marca por representante (TOTAL REPRESENTANTE y TOTAL del mes), Variedades por ruta, Inventario (existencia de hoy y unidades del mes de `product-movement` del mismo mes; cobertura y saldos con la fórmula del original) y Transacciones (todo el mes). Libro 2: Resumen, Unidades por mes, Venta $ por mes, Compras por mes, Clientes por producto, Gráfico (los datos del gráfico) y Notas. Encabezados, anchos del original, formatos `$#,##0.00;[Red](…);"—"`, `0.00%`, `+0%;-0%`, fechas como fecha de Excel, SKU como texto, autofiltro y fórmulas (`D5-C5`, `IF(C5<>0,E5/C5,"")`, `SUM(…)`, `MAX(0,F4-G4*1)`…) **con el valor ya calculado**. |
| Ficha del cliente | Panel plegable «Representante de ventas» (representante y ruta; con `.manage`, cambiarlo). Se protege solo (módulo y permiso). |
| Pulso, panel Ventas | Primera cifra «Venta del mes vs cuota» (`monthSales`, «de $cuota · n %» y barrita) que abre `/analytics/sales`; no sale si el API no manda `monthSales`. |

### Decisiones (para ratificar)
1. **Gráficos en el Excel**: SheetJS comunidad no escribe gráficos ni imágenes y no se agregó ninguna librería. La hoja «Gráfico» lleva los datos (como el original) con una nota de cómo verlo; el gráfico dibujado sale de «Imprimir presentación» (PDF desde el diálogo del navegador). Tampoco se escriben estilos (negritas, rellenos, formato condicional) ni paneles fijos: los colores de ABC/estado/cobertura del original no van en el Excel.
2. **Umbrales de color de cobertura**: rojo menos de 2 semanas, ámbar de 2 a 4, verde 4 o más (`COVERAGE_LOW_WEEKS` / `COVERAGE_HIGH_WEEKS`); «sin base» (gris) si no hay promedio semanal. Cumplimiento: verde ≥ 100 %, ámbar ≥ 90 %, rojo debajo. En la barrita del Pulso (mes en curso) lo que va debajo del 90 % se pinta naranja, no rojo.
3. **Hoja Inventario** del libro mensual: la cobertura y los saldos usan las unidades del MES (fórmula del Excel de octubre), no el ritmo semanal de la pantalla de movimiento (decisión 12 del backend); lo dice la nota de la hoja.
4. **Nombres de hoja y encabezados** salen de `t()`: en español son los del dueño; en inglés van traducidos.
5. **Enlace del Pulso**: el servidor manda `routes.monthSales = "/sales-orders?tab=analysis"` (no existe esa pestaña); la web usa `/analytics/sales` fija. Conviene cambiar la ruta en `SalesProspectAttentionProviders.cs`.
6. El permiso de la ruta es `view|manage` (alguien con solo `.manage` también entra), como el API.

### Arreglo en el SQL (sección del lote 241)
El commit del backend (renumerado 238 → 241) dejó la sección «2026-11-12 — Lote 241» de `Diseño/logistica-db-update.sql` **sin el `GO` ni la apertura `/* ---`** que la separan de la sección 240: `db-init` fallaba con «Error en logistica-db-update.sql, lote #848: Incorrect syntax near '2026'». Se agregaron esas dos líneas (nada más). Con eso `db-init` corrió limpio (EXIT 0) en una base nueva.

### Cómo se probó
- `tsc -b`: limpio. `oxlint` de los archivos tocados: solo avisos (ningún error).
- Vitest, SOLO los archivos nuevos/tocados: `salesAnalysisRules.test.ts` (22), `salesAnalysisExcel.test.ts` (10, escribe y vuelve a leer el libro con SheetJS: hojas, celdas, fórmulas, formatos, anchos), `SalesAnalysisScreen.test.tsx` (9: cada pestaña con API simulado, exportación desde la pantalla, cuotas en bloque, cuota inválida, importación simulación → real, asignación en bloque, solo lectura), `salesAnalysisEmbeds.test.tsx` (5: ficha del cliente y Pulso) y `app/navigation.test.ts` (actualizado: Análisis 4 → 5 ítems, 45 → 46 rutas con menú) → «Test Files  5 passed (5) · Tests  62 passed (62)».
- Con datos reales (base local `TeikemL241`, borrada al final; API en Development **sin** llave de Brevo; compañía de prueba con `scratchpad/l238/seed.sql`, importador por CLI y la configuración del Excel de octubre: rutas, representantes, cuotas desde agosto, líneas): octubre 141,902.65 = el Excel, cuota 140,700.00 y 100.9 %, Prodigy 134,341.20 / Padimont 7,950.54 / Global Imports 3,109.62 / MedPride 1,958.46; Resumen de movimiento con 52800 en 79.7 % y tendencia +15.5 %; simulación de la importación por la pantalla: 13,350 líneas «cuadra», compras 117 → 107 «cuadra», sin emparejar 1 cliente y el ítem «53310/53350». Capturas (claro/oscuro, 1440 px y 390 px, `deviceScaleFactor` 2) sin scroll horizontal de página (la verificación automática lo encontró una vez a 390 px en «Emparejar a mano» por un `.sr-only` absoluto dentro de la tabla con desplazamiento: arreglado con `position: relative` en `.sa-scroll`). Las capturas quedan en el scratchpad de la sesión, no en el repo (traen datos de clientes).

### Límites
- Sin gráficos ni estilos en el Excel (ver decisión 1).
- La rejilla de cuotas lee los 12 meses con 12 llamadas `GET quotas?month=` (no hay una de un año completo).
- Exportar el libro mensual pide `transactions?all=true` y `product-movement` del mes en el momento (unos segundos con 13 mil líneas).
- No se escribió el manual (va en otro paso).
