# Lote 12 — Cambios de Almacén, tanda 2: qué se construyó y decisiones a revisar

> **Equivalencia:** este documento = Lote 2 del plan de cambios (plan 0+1 = 11, plan 2 = 12, plan 3 = 13, plan 4 = 14, plan 5 = 15).
> El repositorio ya tenía lote1…lote11, así que el cierre de cada lote del plan usa el siguiente número libre.

Fecha: 2026-09-30. Origen: el documento de cambios del dueño del producto sobre las pantallas de Almacén
(`F:\Download\Cambios.pdf`). Plan: `C:\Users\Luis\.claude\plans\fuzzy-sleeping-cocoa.md`, sección "Lote 2 — Productos e
inventario, Compras, Proveedores". Cierra el backend y el frontend web de esa tanda, y trae dos piezas que quedaron
pendientes del cierre anterior: la **asignación de cupo en bloque** y el **cupo estimado en `import-legacy`** (ver la nota
más abajo). Los lotes 3 a 5 del plan (Recibo y Recolección; Transferencias, Conteo y Kárdex; Pulso) siguen pendientes.

Como el Lote 11, este documento cubre también el frontend web: no hay un `docs/frontend/loteF…-decisiones.md` aparte. El
manual funcional está en [`docs/manual/06-inventario-y-almacen.md`](manual/06-inventario-y-almacen.md) (secciones 1.2, 2 y 8) y
[`docs/manual/10-migracion-de-datos.md`](manual/10-migracion-de-datos.md) (sección 4); el de pantallas, en
[`docs/manual/frontend/f6-almacen-e-inventario.md`](manual/frontend/f6-almacen-e-inventario.md); los mensajes de error, en
[`docs/manual/faq.md`](manual/faq.md) (sección "Lote 12").

**Nota de numeración del cupo en bloque.** El endpoint de cupo en bloque, sus reglas y el cupo estimado se construyeron al
final de la tanda 1 y por eso sus comentarios en el código dicen "Lote 11" (`WarehousesController.SetBinsCapacity`,
`WarehouseRules.ValidateBulkCapacity`, `WarehouseLayoutService.SetBinsCapacityAsync`, `BinCapacityRules`, `BinCapacityModal`).
Ningún cierre anterior los recoge (no están en `docs/lote11-decisiones.md`, porque están sin commitear sobre `7077253`): se
cierran aquí. El FAQ los tenía como "Lote 11 (complemento)" y ahora están dentro de "Lote 12".

## Mapa de lo construido

| Capa | Tablas / columnas | Código principal | Endpoints |
|---|---|---|---|
| Esquema | `Product.Brand` y `Product.Model`, `NVARCHAR(100) NULL`. El bloque de `Product` quedó guardado (`IF OBJECT_ID` / `COL_LENGTH`: crea la tabla o agrega las dos columnas a una existente). `UX_Product_Barcode` pasó a su propio lote SQL con guarda `IF NOT EXISTS` | `Diseño/logistica-db-estructura.sql`, `Product.cs`, `WarehouseConfigurations.cs` (`ProductConfiguration`, `HasMaxLength(100)`). El seed no cambió | — |
| API — productos | (usa `Product`, `StockBalance`, `InventorySerial`) | `ProductRules` (`NormalizeBrand`, `NormalizeModel`, `NormalizeTextFilter`, `DistinctBrands`, `IsSerialMissing`, `BrandTooLong`, `ModelTooLong`), `ProductService` (`ListAsync` con los filtros nuevos, `ListBrandsAsync`, `ResolveWarehouseIdsAsync`, `StockIn`), `ProductContracts.cs` (`brand` y `model` en alta, edición y fila de lista) | `GET /api/v1/products` (`warehousePublicIds[]`, `productPublicIds[]`, `name`, `brands[]`, `serialOnly`, `serialMissing`), `GET /api/v1/products/brands?search=`, `POST` y `PATCH /api/v1/products` (`brand`, `model`) |
| API — Kárdex | (usa `InventoryTransaction`, `Product`) | `InventoryReadService.FilteredProductIdsAsync` (marca y nombre), `KardexQuery` (`Brands`, `Name`) | `GET /api/v1/inventory/transactions` (`brands[]`, `name`) |
| API — compras | (usa `PurchaseOrder`) | `PurchaseOrderService.ListAsync`, `PurchaseOrderQuery` (`SupplierIds`, `WarehousePublicIds`) | `GET /api/v1/purchase-orders` (`supplierIds[]`, `warehousePublicIds[]`) |
| API — cupo en bloque | (usa `WarehouseBin`) | `WarehouseRules` (`HasBinFilter`, `ValidateBulkCapacity`, tres mensajes), `WarehouseLayoutService.SetBinsCapacityAsync` (misma consulta del listado, transacción, posiciones con seguimiento para auditar cada una, lotes de 1.000), `WarehouseContracts.cs` (`WarehouseBinCapacityRequest`, `WarehouseBinCapacityResultDto`) | `POST /api/v1/warehouses/{publicId}/bins/capacity` (`warehouse.manage`) |
| Migración — cupo estimado | (usa `WarehouseBin.MaxCapacityQty`) | `Teikem.Domain/Migration/BinCapacityRules.cs` (pura), `MswmReader` (`BinHistoryQueries`, `ReadBinHistoryAsync`, cinco consultas de solo lectura), `LegacyImportService` (estimación, escritura y advertencia), `LegacyImportReport` (CSV `-cupos` y sección del `.md`) | Comando `import-legacy` (no es endpoint) |
| Contratos web y móvil | Sin tablas | `web-app/openapi.json`, `web-app/src/kernel/api/schema.d.ts` y `app-almacen/src/kernel/api/schema.d.ts` regenerados | — |
| Kit (web) | Sin tablas | `kernel/ui/reportPdf.ts` (PDF de presentación con jsPDF), `PhoneInput.tsx` y `phone.ts` (máscara `(xxx)xxx-xxxx`), `KIT.md` | — |
| Frontend — Productos e inventario | — | `ProductListScreen.tsx` (KPIs clicables `?kpi=`, filtros, columnas), `productFilters.ts`, `ProductEditorModal.tsx` (Marca, Modelo, bloque "Añadir ajuste"), `productRules.ts`, `inventoryReports.ts`, `InventoryReportButtons.tsx`, `api.ts` (`useProductBrands`, `useProductInventoryKpis` con `serialMissing`) | — |
| Frontend — Proveedores | — | `SupplierListScreen.tsx` (filtros, clic en fila, baja con ícono, chip de estatus, teléfono con máscara, término de pago con buscador) | — |
| Frontend — Compras | — | `PurchaseOrderListScreen.tsx` (filtros múltiples, alta), `PurchaseOrderDetailScreen.tsx` (regla de líneas al editar) | — |
| Frontend — Posiciones | — | `routes.tsx` y `es.json`/`en.json` (menú "Posiciones"/"Bins"; Proveedores antes de Compras), `LocationsScreen.tsx` (clic en fila abre `BinModal`, botón "Asignar cupo"), `WarehouseDetailScreen.tsx` (botón en la pestaña Posiciones), `BinCapacityModal.tsx`, `binCapacity.ts` | — |
| Humo | — | `scripts/smoke.sh`: cupo en bloque (vista previa, por zona, repetido sin cambios, sin coincidencias, los tres 400, cupo 0, quitar) | — |

Todo bajo el filtro de tenant existente: `Product`, `PurchaseOrder` y `WarehouseBin` (por su almacén ya filtrado) no
cambian su aislamiento. `GET /products/brands` lista solo marcas de la compañía. No hay permisos ni módulos nuevos:
`brands` usa `inventory.view`; el cupo en bloque, `warehouse.manage`. La prueba de seguridad de controladores
(`WmsControllerSecurityTests`) ya declara los dos endpoints nuevos (de 105 a 107 acciones).

## Cómo se prueba

Resultados reportados por quien ejecutó el lote. **No se volvieron a ejecutar al escribir este documento.**

1. `dotnet build Teikem.sln && dotnet test Teikem.sln` — 2.465 en verde (el Lote 11 cerró con 2.402). Pruebas nuevas o
   ajustadas:
   - `ProductBrandFilterTests` (nuevo): reglas de marca y modelo, filtro de texto y marcas distintas sin distinguir mayúsculas,
     regla `serialMissing`, alta/edición/quitar marca y modelo, filtros de la lista (nombre, marcas, SKU, varios almacenes),
     `serialOnly` y `serialMissing`, marcas distintas y con búsqueda dentro del tenant, Kárdex por marca y nombre, órdenes de
     compra por varios proveedores y almacenes, orden sin líneas o con cantidad cero, mapeo de `Brand`/`Model` y su declaración
     en el script de estructura, y traducción de los filtros nuevos a SQL Server sin base (`ToQueryString`).
   - `BinCapacityTests` (nuevo): la regla del cupo estimado (redondeo a la decena, tope de `int`, mediana, máximo histórico por
     código, cascada posición → pasillo → zona → almacén, mínimo de 5 datos, `--update` que no pisa), textos del CSV de cupos,
     planificador y reporte; y el cupo en bloque (zona, quitar, un solo valor de los dos, los tres 400, almacén ajeno 404,
     almacén de baja 422, más posiciones que un lote de 1.000, traducción a SQL Server).
   - Ajustadas: `WmsContractsTests`, `WmsControllerSecurityTests`, `LegacyImportReadersTests`.
2. Web (`cd web-app && npm run check`): `tsc -b`, lint, vitest con más de 570 pruebas y build. Archivos de prueba nuevos:
   `productFilters.test.ts`, `inventoryReports.test.ts`, `reportPdf.test.ts`, `PhoneInput.test.tsx`,
   `SupplierListScreen.test.tsx`, `PurchaseOrders.test.tsx`, `binCapacity.test.ts`. Ajustados: `ProductListScreen.test.tsx`,
   `ProductEditorModal.test.tsx`, `productRules.test.ts`, `warehouseScreens.test.tsx`, `navigation.test.ts`.
3. App móvil (`cd app-almacen`): `tsc` con el `schema.d.ts` regenerado.
4. Base desde cero, repetible (dos veces en este lote), con los archivos de configuración de `docs/migracion/`:
   ```
   dotnet run --project src/Teikem.Api -- db-reset --yes
   dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.depot.json
   dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.solutions.json
   ```
   Sin rechazos en Depot ni en Solutions. La corrida de Depot del 2026-09-30 dejó el reporte
   `F:\TeikemData\reporte-depot-20260930-0051-cupos.csv` (3.887 líneas: encabezado y 3.886 posiciones). Resultado real del cupo
   estimado (se contó de ese CSV): `HISTORIAL` 2.709 posiciones (cupo de 10 a 264.600, mediana 50), `PASILLO` 645 (10 a 120,
   mediana 20), `ZONA` 528 (50) y `ALMACEN` 4 (50).
5. En el navegador, con Advance Depot (datos reales):
   - Proveedores: filtros y modal con la máscara del teléfono.
   - Compras: validaciones del alta y filtros múltiples.
   - Productos e inventario: indicadores; `?kpi=available` muestra 75 productos con disponible; el bloque "Añadir ajuste" y sus
     validaciones.
   - Posiciones: un clic en la fila abre la edición.
   - Los dos PDF (inventario y ajustes) generados con datos reales de Depot y revisados a ojo.

### Qué no se probó

- **El 409 del ajuste** ("Inventario insuficiente…") no se provocó en vivo; solo lo cubren las pruebas de pantalla.
- **Recorridos de Playwright** (`web-app/e2e`): **pasan completos** (37 pasan, 0 fallan; las 37 saltadas son las de
  escritorio en el perfil móvil y viceversa), corridos con el Chrome instalado porque la descarga del navegador de Playwright
  falla en esta red. `f6.spec.ts` se ajustó para Productos (sin buscador, columna Marca, Rastreo como desplegable con
  buscador) y para el Proveedor de la orden de compra (desplegable con buscador, no `<select>`).
- No se corrió `scripts/smoke.sh` ni el CI de GitHub Actions con este código. El humo solo cubre el cupo en bloque; los
  filtros de productos, marcas y órdenes de compra no tienen paso de humo (solo pruebas de xunit).
- **El modal "Asignar cupo"** se revisó en el navegador con Advance Depot **solo con la vista previa** (zona precargada desde
  `?zone=`, "Se aplicará a 3029 posiciones"), sin aplicar. La aplicación la cubren las pruebas. Durante el desarrollo se
  aplicó por error cupo 50 a 4 posiciones de la compañía demo (ALM-01, pasillo A01); quedó revertido al recrear la base.
- El cupo en bloque no se corrió contra SQL Server con las 3.887 posiciones de Depot (ni se midió su tiempo); las pruebas
  del servicio usan una base de prueba y aparte comprueban que las consultas se traducen a SQL Server (`ToQueryString`).
- El bloque `Product` guardado del script de estructura no se probó contra una base ya existente: la recreación de la base
  (`db-reset`) sí.
- Los PDF se revisaron a ojo; no hay prueba automática de su aspecto (`reportPdf.test.ts` prueba la lógica que decide qué se
  pinta). No se probó la generación con más de 10.000 filas.
- Las capturas nuevas del manual (ver "Qué queda fuera") quedaron pendientes.

## Decisiones a revisar

1. **La nota del ajuste es obligatoria solo en la pantalla.** En el modal de producto ("Añadir ajuste") la interfaz la exige
   (máx. 300); el API la deja opcional para no romper otros flujos. El modal de la pantalla Ajustes
   (`InventoryAdjustModal`) no se tocó: su nota sigue opcional. El plan decía unificar; queda para el lote de Ajustes (Lote 14 en
   la numeración del repositorio). El texto de "cantidad vacía" cambió en el esquema compartido (`adjustQuantitySchema`), así
   que ahora también lo ve quien ajusta desde Ajustes de inventario.
2. **El filtro de Almacén de Productos acota cantidades y no quita productos.** Con uno o varios almacenes, cada fila muestra
   en mano, reservado y disponible solo de esos almacenes; un producto sin existencia allí sigue apareciendo con ceros. Es la
   semántica que ya tenía el filtro de un almacén. Excepciones: `onlyAvailable` sí deja fuera a los que no tienen disponible
   en esos almacenes, y `serialMissing` mide existencia y series en esos almacenes. Un id de almacén ajeno o inexistente
   responde 404 `Almacén no encontrado.`
3. **Se quitó el filtro "Estado" y el buscador de la tabla** (Productos, Órdenes de compra). Los reemplazan los indicadores
   clicables y los filtros Nombre y SKU. El parámetro `search` sigue en el API.
4. **Los indicadores y sus filtros.** Corregido: al tocar "Con número de serie" ahora se filtran solo los productos
   **activos** con serie (`activeOnly` + `serialOnly`), igual que su cifra. Queda una diferencia: "Unidades totales" es la suma de la
   existencia en mano de todos los saldos (incluye cuarentena, cruce de muelle y productos inactivos), mientras que su filtro es
   "activos con disponible" en zonas recolectables. Revisar si el dueño quiere que coincidan.
5. **Modelo no tiene columna propia.** Va debajo de la marca, en tono tenue, para no apretar la tabla. En la exportación la
   columna Marca sale como "Marca · Modelo".
6. **Valor del inventario = Total × costo de compra del producto.** El sistema no guarda costo promedio ni costo por lote; el
   reporte lo dice. Un producto con existencia y sin costo sale con "—", no suma y se avisa.
7. **El PDF se genera en el cliente** (jsPDF, ya usado en el Lote 0): no hay endpoint de reportes. Consecuencias: fuentes
   estándar (solo caracteres latinos), tope de lectura de 10.000 filas con aviso, y el navegador hace todo el trabajo. El logo
   se rasteriza del SVG de `public/brand/`; si no se puede, el reporte sale solo con texto.
8. **Exclusiones de los reportes.** El de inventario lista solo productos con existencia en mano distinta de cero y avisa
   cuántos quedaron fuera. El de ajustes excluye los saldos iniciales de la migración (motivo `OPENING_BALANCE`) y avisa;
   **esa exclusión se hace en el cliente después de leer los movimientos**, así que los saldos iniciales cuentan contra el tope
   de 10.000. El de ajustes **no tiene rango de fechas** (los filtros de Productos no lo tienen): trae todo el historial de
   ajustes que cumpla los filtros. Con un indicador elegido, el reporte de ajustes lo ignora y lo avisa.
9. **Proveedor y almacén son inmutables en una orden de compra** (regla que ya existía: el `PATCH` responde 400). La ficha los
   muestra de solo lectura. Si hay error, se cancela la orden y se crea otra.
10. **El almacén de la orden es opcional en el API solo con un único almacén activo** (regla D26: con más de uno, 400 "Indique el
    almacén: la compañía tiene más de uno."; sin ninguno, 422). **En la pantalla es obligatorio** ("Indique el almacén.").
11. **El teléfono del proveedor se guarda con la máscara** `(xxx)xxx-xxxx`. El API no la valida (acepta texto de hasta 40
    caracteres); la pantalla exige 10 dígitos o vacío. Un proveedor guardado antes con otro formato se muestra con máscara si
    tiene 10 dígitos y, si no, tal cual: para guardarlo hay que corregirlo. El filtro Teléfono de la lista compara por dígitos.
12. **Cupo estimado en `import-legacy`** (`BinCapacityRules`):
    - Con historial: el mayor total de la posición en MSWM (`Inventory`, `Inventory_Old`, `CycleCountInventory`,
      `CycleCountHistory` con `LocationIdCounted`, `PutAwayHistory` sumado por día), redondeado hacia arriba a la decena, mínimo 10.
    - Sin historial: mediana del pasillo, luego de la zona, luego del almacén, **solo si la mediana sale de al menos 5 datos**
      (`MinSamples`). Sin esa regla, la zona PISO daba 132.320 (mediana de dos datos, 30 y 264.600).
    - `--update` solo llena posiciones **sin** cupo; una posición con cupo no se pisa nunca. `--dry-run` calcula sin escribir.
    - El resultado real en Depot está en la corrida de arriba. Sigue habiendo valores para revisar con el dueño: `CARTONES`
      sale con 264.600 (su historial) y las posiciones de preparación (`R1` con 410, `S1` con 50) reciben cupo aunque no
      sean de almacenaje. Se corrigen con el cupo en bloque.
13. **El cupo en bloque exige `allBins: true` para cambiar todo el almacén.** Sin ningún filtro de posiciones y sin `allBins`,
    400. La pantalla marca `allBins` solo con la casilla "Todo el almacén" y sin filtros, y pide confirmación con más de 100
    posiciones o con "Todo el almacén". La pantalla solo actúa sobre posiciones **activas** (`includeInactive: false`); el API
    sí acepta `includeInactive`.
14. **El cupo en bloque valida el cuerpo antes de buscar el almacén.** Un cuerpo inválido da 400 aunque el almacén sea ajeno o
    esté dado de baja (404 y 422 solo si el cuerpo es válido). Los errores de valor y de filtro salen juntos.
15. **La vista previa del modal "Asignar cupo" a veces es un tope.** El listado de posiciones no tiene el filtro "solo sin
    cupo". Con esa casilla y sin filtros de texto, la cifra sale de la suma de `binsWithoutCapacity` de las zonas; con filtros
    de texto se recorre el listado si hay hasta 1.000 posiciones; con más se muestra "como máximo N".
16. **En MSWM no hay dato utilizable de capacidad en unidades ni de marca.** `Location.PalletCapacity` no sirve (2.193 posiciones
    con el valor por defecto 50 y el resto en 0), y `Item.FullPalletQty` y `Item.Cube` vienen vacíos; por eso el cupo se estima
    del historial. Tampoco hay marca en MSWM. **La marca de QuickBooks (columna `Brand`) ahora pasa a `Product.Brand`** al
    crear el producto (`PlannedProduct.Brand`, recortada a 100). En Depot solo 25 ítems la traen y 24 tienen la categoría
    "SOLUTIONS", que la configuración de Depot excluye (esos productos entran desde el WMS, sin marca): queda 1 producto con
    marca (00050-7, GLOBAL). `--update` no cambia la marca de productos ya existentes.
17. **El script de estructura sigue sin ser idempotente fuera de lo guardado** (ver la decisión 1 del Lote 11). Ahora `Product`
    también lleva guarda y el índice `UX_Product_Barcode` va en su propio lote, pero el resto sigue con `CREATE TABLE` a
    secas. Aplicar el cambio a una base con datos que no salga de la migración exige `db-reset` + `import-legacy` (el dueño
    aceptó recrear la base) o, a mano, los dos `ALTER TABLE dbo.Product ADD Brand/Model NVARCHAR(100) NULL`.
18. **Menú:** Proveedores va antes de Compras (órdenes 40 y 50 en `routes.tsx`) y "Ubicaciones" se llama "Posiciones" (en inglés,
    "Bins"), con el subtítulo "pasillo · rack · posición, por zona". La dirección `/warehouse/locations` no cambió.
19. **Los indicadores de Productos se leen con `take=1`** (cinco consultas: SKUs activos, Unidades totales, Bajo mínimo, Con
    serie y "series incompletas"). Se retiró `useSerialProductCount`, que leía todas las páginas de productos para contar las
    series en el navegador.

## Qué queda fuera de este lote (a propósito)

- **Filtros de fecha en Productos y en el Reporte de ajustes.** No se pidieron; el reporte trae todo el historial.
- **Nota obligatoria en la pantalla Ajustes y botón de Reporte de ajustes allí.** Es del Lote 14 (Transferencias y ajustes);
  `AdjustmentsReportButton` ya está listo para reutilizarse.
- **Costo promedio o costo por lote.** El valor del inventario usa el costo de compra.
- **Marca y modelo en la migración** (decisión 16) y **marca en el filtro de Saldos** (`GET /inventory/balances` no acepta `brands`).
- **Reportes generados en el servidor** y su envío por correo.
- **Lotes 3 a 5 del plan** (Recibo y Recolección; Transferencias, Conteo y Kárdex; Pulso).
- **Orden por columna en el servidor** para las tablas paginadas (decisión 4 del Lote 11: ordena solo la página visible).
- **Capturas de pantalla** nuevas del manual: Posiciones con "Asignar cupo" y su modal, el bloque "Añadir ajuste" abierto y los
  dos PDF (quedan notas "Captura pendiente"). Las de Productos, Proveedores y Órdenes de compra las regenera el recorrido e2e.
- **Corrida del smoke y del CI** con este código (ver "Qué no se probó").

## Ajustes decididos tras el cierre

Decisiones del dueño del producto del 2026-09-30 (plan, sección "Decisiones del dueño del producto (2026-09-30) — ajustes al
Lote 2 que entran con el Lote 3"). Esta tanda cubre **solo el backend** (y revisa la app móvil); la pantalla web la ajusta el
frontend del Lote 13. `web-app/openapi.json` **no** se regeneró aquí: los contratos nuevos (`supplierId`/`warehousePublicId`
en el `PATCH` de la orden y `onlyOnHand` en `GET /products`) aparecen al regenerarlo. Reemplaza a las decisiones 1 y 9 de
arriba y precisa la 4.

1. **Proveedor y almacén de la orden de compra, editables solo en Borrador** (reemplaza la decisión 9).
   `PurchaseOrderPatchRequest` suma `SupplierId` (`int?`) y `WarehousePublicId` (`Guid?`) **al final** (los mismos nombres
   que el alta; `null` = sin cambio) y salieron de `ImmutableOnPatch` (`number`, `orderDate`, `currency`, `status` y
   `statusCode` siguen dando 400 `El campo {campo} de la orden de compra no se puede cambiar.`). Reglas, en
   `PurchaseOrderService.UpdateAsync` dentro de la transacción y con la orden bloqueada:
   - Un valor **igual** al actual no es un cambio: se acepta en cualquier estatus y no se revalida (una pantalla puede
     mandar el formulario completo, y una orden en Borrador con un proveedor dado de baja después sigue editándose).
   - Un valor distinto fuera de `DRAFT` → **409** `El proveedor y el almacén solo se cambian mientras la orden de compra está en
     borrador.` (`PurchaseOrderRules.SupplierWarehouseOnlyDraft`). Se revisa **antes** que la capacidad
     `EDIT_PURCHASE_ORDER`, para que la respuesta sea 409 en cualquier estatus (con la siembra, SENT daría 422 si no).
   - En `DRAFT` se valida como en el alta: proveedor de otra compañía o inexistente 404 `Proveedor no encontrado.`, dado de
     baja 422 `El proveedor está dado de baja; no admite órdenes de compra nuevas.`; almacén 404 `Almacén no encontrado.` o
     422 `El almacén está dado de baja; no admite órdenes de compra nuevas.`. Diferencia con el alta: el almacén se resuelve
     antes que el proveedor (hace falta para saber si cambia), así que con los dos inválidos el 404 del almacén sale primero.
     Nada se asigna hasta que ambos pasan.
   - `rowVersion` y la auditoría (`SupplierId`/`WarehouseId` en el `AuditLog` de `PURCHASE_ORDER`) como el resto del `PATCH`.
   - **Efecto en las líneas: ninguno.** En `DRAFT` la orden no tiene avisos ni recibos (el ASN se crea al recibir, desde
     `SENT` o `PARTIAL`), las líneas no guardan almacén y el costo congelado no depende del proveedor.
2. **Nota obligatoria en el ajuste manual, también en el API** (reemplaza la decisión 1). `POST /api/v1/inventory/adjustments`
   responde 400 con `errors.notes` = `Escriba una nota que explique el ajuste.` (`AdjustmentRules.NotesRequired`, el mismo texto
   de la pantalla) si la nota falta o queda vacía al recortar. Regla: `AdjustmentRules.RequiresNotes(reason)` = el motivo no es
   de sistema (también sin motivo o con uno desconocido; sale junto con los demás errores). Con un motivo de sistema el error
   es el de siempre en `reason` y no se suma el de la nota. Llamadores del ledger con `ADJUSTMENT` revisados, **exentos**
   porque no pasan por este endpoint y su origen queda en la referencia del movimiento:
   - Recibo (`ReceiptService`, `RECEIPT_VARIANCE`), conteo (`CycleCountService`, `COUNT_VARIANCE`), eliminar una recolección
     (`PickBatchService`, `PICK_BATCH_REVERSAL`) y saldo inicial de `import-legacy` (`LegacyImportService`, `OPENING_BALANCE`).
   - **Resolución de faltante de compra con `MANUAL_ADJUSTMENT`** (`PurchaseShortageService`, motivo `PO_SHORTAGE` por defecto):
     **la nota también es obligatoria** (es un ajuste manual de inventario, y la decisión del dueño fue "todo ajuste manual
     exige nota"): sin ella, 400 en `errors.notes` "Escriba una nota que explique el ajuste.". Cerrar y Reordenar no mueven
     inventario y la dejan opcional. El modal web (`ResolveShortageModal`) la pide obligatoria para esa acción.
   - La transferencia (`POST /inventory/transfers`) no es un ajuste: nota opcional.
   - **App móvil (`app-almacen/`)**: no hace ajustes manuales (solo aparece el endpoint en el `schema.d.ts` generado); no hubo
     cambios. Los recorridos e2e web (`f6.spec.ts`, `f7a.spec.ts`) ya mandaban nota.
   - **Pendiente del frontend**: el modal de la pantalla Ajustes (`InventoryAdjustModal`) todavía tiene la nota opcional; hasta
     que se ajuste, un ajuste sin nota desde ahí recibe el 400 en `errors.notes`. **Cerrado en el Lote 13:** el modal ya exige la
     nota (ver [`lote13-decisiones.md`](lote13-decisiones.md)).
   - `scripts/smoke.sh`: los helpers `adjust()` y `serialadj()` mandan `notes`, y un paso nuevo comprueba el 400 sin nota.
3. **KPI "Unidades totales"** (precisa la decisión 4). `GET /api/v1/products` suma `onlyOnHand` (`ProductListQuery.OnlyOnHand`,
   al final): productos con Σ `QtyOnHand` > 0 en **todas** sus posiciones (sin excluir cuarentena ni cruce de muelle y sin
   restar lo reservado, porque la cifra del indicador suma todos los saldos), acotado a los almacenes del filtro y, como
   `onlyAvailable`, quitando de la lista a los que no tienen existencia allí. El indicador debe pedir
   `activeOnly=true&onlyOnHand=true`. Queda una diferencia que el frontend debe decidir: la **cifra** sale de
   `GET /inventory/balances` e incluye productos inactivos con existencia; la tabla filtrada (activos) no los muestra.
   **Cerrado en el Lote 13:** `GET /api/v1/inventory/balances` acepta `activeProductsOnly` y la cifra del indicador suma solo
   productos activos, así que cifra y tabla coinciden.
4. **Empacar: predeterminados de la compañía.** No hizo falta endpoint nuevo: `GET /api/v1/tenant/settings` ya devuelve
   `defaultServiceType` y `defaultPackageType` (código o `null`) y solo exige sesión (`[Authorize]`, sin permiso ni módulo), así
   que lo lee quien empaca (`warehouse.pick` + `orders.create`). La etiqueta ("Estándar") sale del catálogo que la pantalla de
   empaque ya carga (`useLookups('ServiceType'|'PackageType')`). Riesgo menor: si el valor predeterminado se deshabilita en el
   catálogo, la pantalla no encontrará su etiqueta (el API lo sigue usando al crear la orden).

**Cómo se probó:** `dotnet build Teikem.sln` sin errores y `dotnet test Teikem.sln` con **2.483** en verde (antes 2.465).
Pruebas nuevas: `PurchaseOrderSupplierWarehouseEditTests` (cambio en Borrador sin tocar líneas, validaciones 404/422 sin
cambios a medias, 409 en SENT/PARTIAL/RECEIVED/CANCELLED, 409 antes que la capacidad y mismos valores aceptados, campos fijos
restantes), `ProductOnHandFilterTests` (cuarentena, reservado, saldo en cero, inactivo, almacenes y traducción a SQL Server) y
`TenantDefaultsForPackingTests` (endpoint abierto a cualquier usuario con sesión, códigos o `null`, aislamiento por tenant).
Ajustadas: `InventoryAdjustmentServiceTests` (nota obligatoria para los seis motivos, errores juntos, motivo de sistema,
transferencia opcional), `AdjustmentRulesTests` (`NormalizeNotes(required)`, `RequiresNotes`) y `WmsContractsTests` (firmas).
No se corrieron `scripts/smoke.sh` ni el CI; `bash -n scripts/smoke.sh` pasa.

