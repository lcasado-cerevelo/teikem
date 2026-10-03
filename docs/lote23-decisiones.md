# Lote 23 — Hojas de posición: el dato y el rastro de la hoja desactualizada (servidor, 2026-10-03)

Pedido del dueño: por posición (`WarehouseBin`) se imprimirá una **hoja de posición** en papel con los productos que hay en ella (SKU, nombre y
código de barras) para pegarla en el rack. Este lote es **solo servidor**: da el dato de las hojas y el rastro de si la hoja de una posición está
desactualizada. La hoja en PDF, la insignia, el contador y la selección en la web, y la app, van en lotes siguientes. Rama
`claude/company-settings-screen-plan-uajc2i`. Sin permisos ni módulos nuevos.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Esquema | `WarehouseBin.SheetPrintedAtUtc` (última impresión) y `SheetContentChangedAtUtc` (último cambio del **conjunto** de productos), `DATETIME2 NULL`, en el `CREATE TABLE` y con `ALTER` guardados por `COL_LENGTH`. Sin cambios en el seed. Sin backfill | `Diseño/logistica-db-estructura.sql` |
| Entidad y mapeo | Dos propiedades `[NotAudited]` (marcas técnicas: no van a la bitácora) mapeadas 1:1 como `datetime2` | `Warehouse.cs`, `WarehouseConfigurations.cs` |
| Regla pura | `BinSheetRules.Status(hasProducts, printedAt, changedAt)` → `STALE` (impresa y cambio posterior, también si quedó vacía) › `EMPTY` (sin productos) › `NEVER_PRINTED` (con productos sin impresión) › `CURRENT`. `ContentChanged(antes, después)` = el total cruza el cero. `PrintedMark` (instante de los datos acotado a ahora; nunca retrocede). `ParseStatuses` del filtro. Mensajes exactos | `Domain/Wms/BinSheetRules.cs` (`BinSheetStatuses`) |
| Detección en el ledger | `InventoryLedger.PostAsync` y `RebuildBalanceAsync`: agrupa las claves tocadas por (posición, producto); si la suma cruza el cero, UNA consulta de los **otros lotes** del producto en la posición con existencia (si alguno tiene, el producto sigue y no hay cambio) y UNA carga de las posiciones que cambiaron; fija `SheetContentChangedAtUtc = now` y se guarda en el mismo `SaveChanges` / transacción. Sin candidatos no hay consultas. Cubre recibo, acomodo, transferencia (origen y destino), reabasto, recolección, cruce, ajuste, conteo, corrección del saldo e importación legada: todos pasan por el ledger | `Wms/InventoryLedger.cs` (`MarkSheetContentChangesAsync`) |
| Confinamiento | Nueva prueba: `SheetContentChangedAtUtc` solo lo escribe `InventoryLedger.cs` (y el ledger sí lo escribe) | `WmsWriteConfinementTests` |
| Listado de posiciones | `WarehouseBinDto` gana al final `SheetStatus`, `SheetPrintedAtUtc`, `SheetContentChangedAtUtc`; `WarehouseBinPageDto` gana `StaleCount` (STALE + NEVER_PRINTED del filtro, todas las páginas); `WarehouseBinQuery` gana `SheetStatus` (varios, 400 si otro). Filtro en SQL con el mismo criterio que la regla (la prueba lo compara estado por estado). Todo aditivo: la web no cambia | `WarehouseLayoutService` (`FilteredBinRowsAsync`, `NeedsSheetPrinting`, `BinRowsQuery`), `WarehousesController.Bins` |
| Hojas | `GET /api/v1/warehouses/{publicId}/bin-sheets`: mismos filtros que el listado (misma validación y 404 vía `FilteredBinRowsAsync`), por código; `skip`/`take` 1..200 (por defecto 50; > 200 → 400). Por hoja: datos de la posición, estado y `products` (uno por producto, sin repetir lotes, en mano > 0, por SKU ordinal y luego id) con `productPublicId`, `sku`, `name`, `barcode`. Página con `total`, `staleCount` y `generatedAtUtc`. Dos consultas para toda la página (pares GROUP BY … HAVING + productos) | `Services/BinSheetService.cs` |
| Marcar impresas | `POST .../bin-sheets/mark-printed` `{ binIds (1..500), generatedAtUtc? }`: todo o nada (404 `Posición no encontrada.` si alguna no es del almacén, sin escribir); `SheetPrintedAtUtc = PrintedMark(...)`; devuelve `[{ binId, code, sheetStatus, sheetPrintedAtUtc, sheetContentChangedAtUtc }]` por código | `BinSheetService.MarkPrintedAsync` |
| Seguridad | Ambos endpoints `inventory.view` + módulo `WMS_LOTSERIAL` (decisión del dueño). Tenant: la posición se alcanza solo por su almacén resuelto con el filtro global | `WarehousesController`, `WmsControllerSecurityTests` (128 → 130 acciones) |
| Contrato | `web-app/openapi.json` regenerado del Swagger del API en marcha (solo agrega: 2 rutas, 5 esquemas, 3 campos en `WarehouseBinDto`, `staleCount` y el parámetro `sheetStatus`); `schema.d.ts` de web y app con `npm run api:types` (openapi-typescript 7.13.0) | — |
| Docs | Manual cap. 06 §1.5 (y tabla de validaciones, filtro del listado, permisos), FAQ "Lote 23", índice del manual | `docs/manual/` |

## Cómo se probó (resultados reales de esta sesión)

| Comando | Resultado |
|---|---|
| `dotnet build Teikem.sln` (Debug y `-c Release`) | **pasó**, 0 errores (3 avisos que ya existían: `PermissionService.cs`, `CycleCountByProductTests.cs`, `PurchaseShortageServiceTests.cs`) |
| `dotnet test Teikem.sln` | **pasó**: 3039 pruebas, 0 fallas. Nuevas en `BinSheetTests` (35 casos): tabla del estado, cruce del cero, filtro y mensajes; ledger: producto nuevo marca, más cantidad no, baja sin llegar a 0 no, otro producto marca, a 0 marca; lotes sumados (un lote a 0 con otro en existencia no marca; el último sí; dos lotes en un asiento); transferencia parcial (solo destino), total (solo origen) y con origen y destino a la vez; ajuste por `InventoryAdjustmentService` y diferencias de conteo (`COUNT_VARIANCE`) faltante y sobrante; varias líneas y posiciones en un asiento (un instante); asiento fallido no marca; `RebuildBalanceAsync`; listado con `sheetStatus`/`staleCount` (filtro SQL = regla estado por estado; varios estados; staleCount de todas las páginas; 400); arranque sin backfill = `NEVER_PRINTED`; hojas agregadas por producto sin lotes repetidos, solo en mano, por SKU, con código de barras; filtros, paginación, por defecto 50, 200 sí y 201 → 400; mark-printed idempotente, nunca retrocede, cambio posterior → STALE y reimpresión → CURRENT; cambio entre la consulta y la marca → STALE; 400/404 todo o nada; otra compañía 404 (también nombrando la posición ajena desde un almacén propio); traducción a SQL Server sin BD del filtro, del `staleCount` y de los pares (GROUP BY/HAVING); mapeo `datetime2` nulo `[NotAudited]` y columnas en el script. Ajustadas: `WmsContractsTests` (firmas), `WmsControllerSecurityTests` (130 acciones), `WmsWriteConfinementTests` (nueva) |
| `scripts/dev-sqlserver.sh` | SQL Server no estaba corriendo; se levantó (queda corriendo) |
| `db-reset --yes` + `db-init` dos veces | **pasaron** (base recreada; columnas presentes, `COL_LENGTH` = 8) |
| Rama `ALTER` sobre una base vieja | Se borraron las dos columnas y el registro del hash en `__SchemaVersion` para forzar la re-aplicación: el script falla **antes** de llegar a la tabla (`There is already an object named 'Tenant'`, lote #10). Es **preexistente**: el script de estructura no es re-aplicable sobre una base ya creada; la vía soportada es recrear la base (`db-reset`). Los `ALTER` guardados quedan por consistencia con los de lotes anteriores. Se recreó la base después |
| API Release en Development (`Auth__Onboarding__Enabled=false`, `ASPNETCORE_URLS=http://localhost:5000`) + `scripts/smoke.sh` con `SMOKE_SQL` (sqlcmd local) y `SMOKE_MIGRATION_RUN` | **SMOKE OK** (exit 0), con el paso nuevo "hojas de posición (Lote 23)": EMPTY → NEVER_PRINTED al entrar un producto por ajuste; hojas con sku/código de barras y `generatedAtUtc` leídas por un usuario de Solo lectura; marcar impresa con `inventory.view` → CURRENT e idempotente; subir cantidad no desactualiza; transferir todo deja el origen STALE y el destino NEVER_PRINTED; filtro `sheetStatus` y `staleCount`; 400 de `take`, estado y lista vacía; 404 sin marcar nada; otra compañía 403/404. El paso de migración (importación legada por el ledger) también pasó |
| `cd web-app && npm run api:types && npx tsc -b`; `cd app-almacen && npm run api:types && npm run typecheck` | **pasaron** (los cambios del contrato son aditivos) |
| `cd web-app && npm run check` | **pasó**: api:types, tsc -b, oxlint (solo avisos que ya existían), vitest 117 archivos / 1183 pruebas, build |

**No se pudo / no se hizo**: probar la detección bajo concurrencia real en SQL Server (dos movimientos simultáneos de lotes distintos del
mismo producto en la misma posición; ver decisión 4); medir el costo de la importación completa de Advance con la detección encendida (el smoke
corre una importación chica); ver el CI de GitHub Actions (no se hizo push).

## Decisiones para el dueño (valor más seguro)

1. **`generatedAtUtc` opcional en mark-printed** (agregado al pedido): sin él, una hoja que se imprime mientras entra o sale un producto quedaría
   `CURRENT` aunque el papel ya salió viejo. Con él, la marca es el instante de los datos y ese cambio la deja `STALE`. Si llega en el futuro se
   toma "ahora"; la marca nunca retrocede (marcar una hoja vieja no pisa una impresión más reciente). La web debería mandarlo siempre.
2. **Las hojas no llevan cantidades** (el pedido pide SKU, nombre y código de barras). Por eso subir o bajar cantidades no las desactualiza.
3. **`staleCount` respeta TODOS los filtros, incluido `sheetStatus`** ("del filtro actual"): filtrando por `CURRENT` da 0. La insignia de la web
   que quiera "cuántas faltan en todo el almacén" debe pedirlo sin `sheetStatus`.
4. **Concurrencia entre lotes del mismo producto**: la consulta de "otros lotes" lee sin bloqueo. Si dos transacciones simultáneas sacan a 0
   **lotes distintos** del mismo producto en la misma posición, cada una ve el otro lote todavía con existencia y ninguna marca el cambio (el
   producto salió y la hoja seguiría `CURRENT`). Es raro (dos salidas totales simultáneas de lotes distintos en la misma posición); el siguiente
   movimiento que cruce el cero o una reimpresión lo corrige. Bloquear el rango por posición en cada movimiento costaría más que el riesgo.
5. **Bloqueo de la fila de la posición**: el ledger ahora actualiza `WarehouseBin` (solo cuando el conjunto cambia), después de bloquear los
   saldos. Dos transacciones que tocan la misma posición con productos distintos se serializan en esa fila hasta el commit. Un interbloqueo solo
   es posible si una transacción llama a `PostAsync` dos veces con claves cruzadas; los 1205 los reintenta la estrategia de ejecución, como el
   resto del inventario.
6. **Posiciones a nivel almacén** (saldos sin posición) no tienen hoja; `RebuildBalanceAsync` también marca (corregir el saldo según el Kárdex
   puede meter o sacar el producto). Reservar y liberar no cambian lo que hay en mano: no marcan.
7. **Marcar una posición inactiva o de un almacén dado de baja se permite** (no toca inventario). El 404 es todo o nada.
8. **Topes**: 200 hojas por consulta (igual que el listado de posiciones; aquí > 200 es **400** con mensaje, no se recorta en silencio, para que
   nadie imprima menos de lo que cree) y 500 posiciones por marca.
9. **Sin auditoría** de las marcas (`[NotAudited]`, como pidió el pedido): quién imprimió no queda registrado. Si hace falta, sería un evento de
   actividad en un lote posterior.
10. **Orden de los productos**: por SKU con comparación ordinal (mayúsculas antes que minúsculas, dígitos antes que letras), luego por id; estable
    entre llamadas.

## Qué quedó fuera (lotes siguientes)

- Web: hoja en PDF, insignia/estado por posición, contador "por imprimir", selección y marcar impresas desde la pantalla.
- App de almacén: ver/avisar la hoja desactualizada.
- Fuente de datos para análisis con el estado de la hoja (no se registró un `IDataSource` nuevo).
