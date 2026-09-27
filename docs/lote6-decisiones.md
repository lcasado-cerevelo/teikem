# Lote 6 — Inventario y almacén: qué se construyó y decisiones a revisar

Fecha: 2026-09-26. Plan aprobado: `docs/lote6-plan.md` (y su espejo `docs/lote6-plan.json`). Autorización general de
Luis (2026-09-26): implementar las 49 decisiones del plan tal como están, con 18 marcadas "pendientes de ratificación"
(sección siguiente). Este documento cierra el lote: mapa de lo construido, cómo se probó, las decisiones que Luis
dejó abiertas y lo que queda fuera.

> **Hallazgos corregidos en verificación: 54.** A diferencia del Lote 4 (donde cada hallazgo quedó marcado en el
> código con el literal `Hallazgo de revisión` y se pudo enumerar 1 a 1), el árbol de trabajo del Lote 6 **no trae esa
> marca** para este lote — se buscó el literal y las 42 apariciones existentes son todas de `scripts/smoke.sh` y
> `tests/Teikem.Tests/FleetContracts*`/`FleetControllerSecurityTests` del Lote 4, no del Lote 6. No se afirma aquí el
> detalle línea por línea de esos 54 hallazgos porque no hay evidencia propia que los enumere; lo que sí se verificó
> de forma independiente en esta sesión, sobre el estado real del repositorio (commit `6836b8e`), es lo siguiente.
>
> **Verificación de este cierre (2026-09-26, sobre BD recreada desde cero, esta sesión):** `dotnet build Teikem.sln`
> sin errores ni advertencias nuevas. `dotnet test Teikem.sln` → **1731/1731** en verde (incluye todas las suites
> `Wms*`, `Warehouse*`, `Inventory*`, `Receipt*`, `PickBatch*`, `PurchaseOrder*`, `CycleCount*`, `CrossDock*`,
> `DockAppointment*`, `Product*` y las de lotes previos ampliadas — `TenantIsolationModelTests`,
> `RawSqlConfinementTests`, `OwnedEntityResolverCoverageTests`, `AnalyticsSeedFieldsTests`, `FleetCatalogTests`,
> `OrderCatalogTests`, `TripCatalogTests` — con el conteo de permisos ajustado a 58). SQL Server 2022 ya estaba
> corriendo en el contenedor de trabajo (`scripts/dev-sqlserver.sh`); se recreó la base `Teikem` desde cero y se corrió
> `dotnet run --project src/Teikem.Api -- db-init` **dos veces**: la primera aplicó estructura (~136 tablas, 149
> lotes) y seed (17 lotes: 13 módulos, dominios, lookups, estatus, capacidades por defecto de `CONTRACT`/
> `TRANSPORT_ORDER`/`WORK_ORDER`/`TRIP`/`PURCHASE_ORDER`, entradas laterales de `TRIP`/`ROUTE`/`PICK_BATCH`/
> `WAREHOUSE_TASK`/`DOCK_APPOINTMENT`/`CROSSDOCK_ALLOCATION`/`ASN`, **58 permisos**, plantillas de rol y zonas de
> despacho demo, con el almacén demo `ALM-01` sembrado aparte por `DemoTenantSeeder`); la segunda omitió ambos scripts
> por hash (0 permisos nuevos propagados, tenant demo "ya existe"). Con el API levantado, `scripts/smoke.sh
> http://localhost:5000` completo — **110 pasos, exit 0, `SMOKE OK`**, sin fallos, de punta a punta sobre los Lotes 1
> a 6 (los 15 pasos de Lote 6 recorren catálogos/permisos/almacén demo, almacenes y ubicaciones, productos/ajustes/547,
> concurrencia de inventario, recepción/putaway/reabasto, recolección y empaque, lotes/series/baja de posición, ASN de
> cliente, compras y faltantes, conteo cíclico, cruce de muelle, RBAC/módulos/aislamiento, análisis/auditoría,
> conciliación sin descuadre y la baja definitiva de un almacén vacío — este último paso se agregó en esta sesión, ver
> abajo). No se hizo push en esta sesión, así que no hay una corrida de CI de GitHub Actions que
> citar para este cierre puntual; el árbitro formal sigue siendo `.github/workflows/ci.yml` en el primer push.
>
> **Manual funcional: pendiente.** Esta pasada solo cerró el documento de decisiones y el arreglo de
> `scripts/smoke.sh` que pedía el encargo; `docs/manual/06-inventario-y-almacen.md` y la ampliación de
> `docs/manual/faq.md` con los mensajes de este lote quedan para una pasada posterior, antes de dar el lote por
> cerrado del todo (CLAUDE.md lo exige).

## Mapa de lo construido

| Área | Tablas (nuevas o extendidas) | Código principal | Endpoints (módulo · permiso) |
|---|---|---|---|
| Almacenes y ubicaciones | `Warehouse`, `WarehouseZone`, `WarehouseBin` (+`WarehouseId`, `UQ_WarehouseBin_WhCode`), `WarehouseDock` | `WarehouseRules`, `WarehouseService`, `WarehouseLayoutService`, `WarehouseDataSource` | `GET/POST/PATCH /api/v1/warehouses(/{publicId})`, `.../zones`, `.../bins`, `.../docks` (WMS_LOTSERIAL · `inventory.view`/`warehouse.manage`) |
| Productos y categorías | `Product` (+`MinQty`/`MinPickQty`/`MaxPickQty`, dueño `ClientId`), `ProductCategory`, `InventoryLot`, `InventorySerial` (+`CurrentWarehouseId`/`CurrentBinId`) | `ProductRules`, `ProductService`, `ProductCategoryService`, `ProductDataSource` | `GET/POST/PATCH /api/v1/products(/{publicId})`, `.../lots`, `.../serials`, `/api/v1/product-categories` (WMS_LOTSERIAL · `inventory.view`/`inventory.manage`) |
| Inventario (Kárdex, ajustes, trazabilidad) | `StockBalance` (+`CK_StockBalance_Qty`), `InventoryTransaction` (+`Quantity` con signo, `ReasonLookupId`, `CK_InvTxn_Direction`) | `InventoryRules`, `KardexRules`, `AdjustmentRules`, `InventoryLedger` (única vía de escritura), `InventoryReadService`, `InventoryAdjustmentService`, `TraceabilityService`, `StockBalanceDataSource`, `InventoryTransactionDataSource` | `GET /api/v1/inventory/balances\|transactions\|reconciliation`, `POST .../adjustments\|transfers`, `GET .../lots/{lotId}/genealogy`, `GET .../serials/trace` (WMS_LOTSERIAL · `inventory.view`/`inventory.adjust`) |
| Recepción | `Asn`, `AsnLine` (+`PurchaseOrderLineId`), `ReceiptHeader`, `ReceiptLine` (+`ExpectedQty`, `AdjustmentTxnId`) | `ReceiptPostingRules`, `ReceiptRules`, `AsnService`, `ReceiptService`, `ReceiptDataSource` | `GET/POST /api/v1/receipts(/{publicId})`, `.../lines`, `.../confirm`, `/api/v1/asns` (WMS_LOTSERIAL · `inventory.view`/`warehouse.receive`) |
| Tareas de almacén (putaway, reabasto) | `WarehouseTask` (`WarehouseTaskId` INT) | `WarehouseTaskRules`, `ReplenishmentRules`, `WarehouseTaskService`, `ReplenishmentService`, `WarehouseTaskStatusEffect`, `PutawayTaskHandler`, `ReplenishTaskHandler`, `PutawaySuggester`, `WarehouseTaskDataSource` | `GET/POST /api/v1/warehouse-tasks(/{id})`, `.../start\|complete\|assign\|cancel`, `.../putaway-suggestions`, `POST .../replenishment/run` (WMS_LOTSERIAL · `inventory.view` + permiso del handler) |
| Conteo cíclico | `CycleCount` (+`IsActive`, `RowVersion`), `CycleCountLine` (+`ReconciledSystemQty`, `SystemQtyChanged`) | `CycleCountRules`, `CycleCountService`, `CountTaskHandler` | `GET/POST /api/v1/cycle-counts(/{id})`, `.../lines`, `.../finish\|refresh\|reconcile` (WMS_LOTSERIAL · `inventory.view`/`warehouse.count`) |
| Recolección y empaque | `PickBatch`, `PickBatchLine` (`IssueTxnId`, `ReversalTxnId`) | `PickBatchRules`, `PickBatchService`, `OrderInventoryLinesProvider` (`IOrderInventoryLines`), `PickBatchDataSource`; guarda en `OrderRules`/`OrderService` (Lote 3) | `GET/POST /api/v1/pick-batches(/{publicId})`, `.../pack`, `DELETE .../{publicId}` (WMS_LOTSERIAL · `inventory.view`/`warehouse.pick`) |
| Compras mínimas y faltantes | `Supplier`, `PurchaseOrder`, `PurchaseOrderLine`, `PurchaseOrderShortageResolution` (nueva) | `PurchaseOrderRules`, `ShortageRules`, `SupplierService`, `PurchaseOrderService`, `PurchaseShortageService`, `PurchaseOrderReceivingService` (`IPurchaseOrderReceiving`) | `GET/POST/PATCH /api/v1/purchase-orders(/{publicId})`, `.../send\|cancel`, `.../shortages`, `.../lines/{lineId}/resolve`, `/api/v1/suppliers` (PURCHASING · `purchasing.view`/`purchasing.manage`/`inventory.adjust`) |
| Cruce de muelle (demo) | `DockAppointment` (+`WarehouseId`), `CrossDockPlan`, `CrossDockAllocation` (+`ConfirmedQty`, `WarehouseTaskId`, `InventoryTransactionId`) | `DockScheduleRules`, `CrossDockRules`, `DockAppointmentService`, `CrossDockService`, `CrossDockTaskHandler`, `CrossDockReceiptParticipant` (`IReceiptConfirmationParticipant`), `DockAppointmentStatusEffect` | `GET/POST/PATCH /api/v1/dock-appointments(/{id})`, `.../status`, `GET/POST /api/v1/cross-dock-plans(/{id})`, `.../candidates\|allocations\|complete` (CROSSDOCK, apagado por defecto · `inventory.view`/`warehouse.crossdock`) |
| Transversal | seed: `AdjustmentReason` (9), `ShortageAction` (3), `PickBatchStatus`, `ZoneType.STAGING`, `Capability.EDIT_PURCHASE_ORDER`, 10 `EntityType` nuevos, `SerialStatus.SCRAPPED` terminal y `RESERVED`/`SHIPPED` laterales; 4 permisos nuevos (`inventory.view`, `inventory.manage`, `inventory.adjust`, `warehouse.manage`; **58 en total**) | `CatalogDomains`, `PermissionCatalog`, `InventoryQueries` (único SQL crudo, 17 sentencias), `WmsResolve`, `WarehouseTaskWriter`, `WmsOwnedEntityResolvers` (11 reales + 3 cerrados), `SystemAnalyticsSeeder` (7 fuentes, vistas del mock, 7 indicadores, 6 gráficos), `DemoTenantSeeder` (almacén `ALM-01`) | — |

`TenantId` sale del principal; ninguna solicitud lo lleva ni lleva `InventoryScope` (lo prueba `WmsContractsTests` por
reflexión). Defensa en profundidad: filtro de tenant (datos) + `[RequirePermission]` + `[RequireModule]` (acción) +
`IOwnedEntityResolver` (recurso), con `InventoryScope(OwnerClientId)` como cuarta capa de lectura por dueño (D44),
lista para el Portal del Lote 8 (en este lote todos los controladores pasan `InventoryScope.Any`). Segunda barrera en
SQL con FKs compuestas `(Id, TenantId)`, `(Posición, Almacén)` y `(Lote/Serie, Producto)` — lo prueba
`TenantIsolationModelTests`. Todo el SQL crudo del lote vive confinado en `InventoryQueries.cs` con `TenantId =`
explícito — lo prueba `RawSqlConfinementTests`. `InventoryLedger` es la única vía de escritura de `StockBalance`,
`InventoryTransaction` y el estado/ubicación de `InventorySerial` — lo prueba `WmsWriteConfinementTests` por texto
sobre `src/`. `InventoryTransaction.Quantity` guarda el signo (D3, maestro L331): RECEIPT +, ISSUE/CROSSDOCK −,
ADJUSTMENT ± y TRANSFER + en una sola fila; `CK_InvTxn_Quantity`/`CK_InvTxn_Direction` lo blindan en SQL Server, y el
547 de `CK_StockBalance_Qty` lo traduce el ledger a 409 `insufficient_stock`.

## Cómo se prueba

1. `dotnet build Teikem.sln` → compila sin errores ni advertencias nuevas.
2. `dotnet test Teikem.sln` → **1731 pruebas, 0 fallos**. Del Lote 6: `WmsCatalogTests`, `WmsContractsTests`,
   `InventoryRulesTests`, `StockAllocatorTests`, `SerialRulesTests`, `PutawayRulesTests`, `PutawaySuggesterTests`,
   `PurchaseStatusRulesTests`, `InventoryLedgerTests`, `WmsWriteConfinementTests`, `WmsOwnedEntityResolversTests`,
   `WarehouseRulesTests`, `WarehouseLayoutServiceTests`, `ProductRulesTests`, `KardexRulesTests`,
   `AdjustmentRulesTests`, `InventoryAdjustmentServiceTests`, `InventoryReadServiceTests`, `ReceiptPostingRulesTests`,
   `ReceiptRulesTests`, `ReceiptServiceTests`, `WarehouseTaskRulesTests`, `ReplenishmentRulesTests`,
   `WarehouseTaskServiceTests`, `WarehouseTaskStatusEffectTests`, `CycleCountRulesTests`, `CycleCountServiceTests`,
   `CycleCountFilterTests`, `PickBatchRulesTests`, `PickBatchServiceTests`, `OrderPickBatchGuardTests`,
   `OrderInventoryLinesTests`, `PurchaseOrderRulesTests`, `ShortageRulesTests`, `ShortageResolveServiceTests`,
   `PurchaseOrderReceivingTests`, `PurchaseShortageServiceTests`, `DockScheduleRulesTests`, `CrossDockRulesTests`,
   `CrossDockServiceTests`, `DockAppointmentStatusEffectTests`, `WmsControllerSecurityTests`,
   `WmsServicePermissionTests`, `WmsDeactivationGuardTests`, `WmsDataSourceTests`; y las de lotes previos ampliadas
   (`TenantIsolationModelTests`, `RawSqlConfinementTests`, `OwnedEntityResolverCoverageTests`,
   `AnalyticsSeedFieldsTests`, `FleetCatalogTests`, `OrderCatalogTests`, `TripCatalogTests`, todas con el conteo de
   permisos en 58).
3. `dotnet run --project src/Teikem.Api -- db-init` **dos veces** sobre una base `Teikem` recreada desde cero: la
   primera aplica estructura (~136 tablas, 149 lotes) y seed (17 lotes: 58 permisos, tenant demo aprovisionado con el
   almacén `ALM-01` y sus zonas STG/PCK/RSV/QUA y muelles D1/D2); la segunda omite ambos scripts por hash (0 permisos
   nuevos propagados, tenant demo "ya existe").
4. Con el API arriba, `scripts/smoke.sh http://localhost:5000` recorre los Lotes 1-5 y, antes del paso final de
   sesiones, los **15 pasos del Lote 6**: catálogos/permisos/almacén demo; almacenes y ubicaciones (alta, zonas,
   posiciones, muelles, PATCH persistido); productos, ajustes y el 547→409 `insufficient_stock`; concurrencia de
   inventario (8 recolecciones sobre 5 unidades, 4 recibos en paralelo, baja de producto contra confirmación);
   recepción ciega, doble confirmación, putaway parcial/total y reabasto; recolección y empaque (doble empaque, bajas
   vigiladas, doble eliminación con reversa); lotes, series, recolección de varias líneas y baja de posición
   (`EnsureLot`, series por `OPENJSON`); aviso de llegada de cliente (dueño, un recibo activo por ASN); compras y
   faltantes (cerrar en paralelo, reordenar, ajuste manual, cancelación desde PARTIAL con bitácora); conteo cíclico
   con filtro de almacenes (D22: contra el saldo actual, `SystemQtyChanged`); cruce de muelle (módulo apagado por
   defecto, citas solapadas, ambos modos de asignación, mover desde la cola y desde el plan); RBAC/módulos/resolvers
   cerrados/aislamiento; análisis y auditoría (7 fuentes, vistas del mock, indicadores de valorización); sin
   descuadre (conciliación ledger↔saldo en cero tras todo lo anterior, incluidos los pasos concurrentes); y la baja
   definitiva de un almacén (D26: vacío → `INACTIVE` terminal, segunda baja → 422). En total el script tiene
   **110 pasos** y termina en `SMOKE OK`.

### `scripts/smoke.sh`

El árbol de trabajo de esta sesión (commit `6836b8e`) ya traía 14 de los 15 pasos del Lote 6 del arreglo `"smoke"` del
plan aprobado, insertados antes del paso final "sesiones: refresh con rotación y logout" (que sigue siendo el último,
como en todos los lotes previos). Se verificaron uno por uno contra la lista `"smoke"` del plan; cubren cada escenario
descrito **salvo uno**: el ítem "baja de producto y de almacén (Lote 6)" del plan solo estaba probado en su mitad de
producto (`PD$TS` con inventario → 409, en cero → 200, reactivar) y en la baja de un almacén con una recolección
empacada eliminable (409); faltaba el camino feliz de dar de baja un almacén vacío hasta `INACTIVE` y la segunda baja
sobre uno ya inactivo (422, D26). **Se agregó ese paso al final del archivo** (antes del cierre de sesiones, después de
"sin descuadre"): `step "baja definitiva de almacén (Lote 6, D26): vacío pasa a INACTIVE (terminal); ya dado de baja →
422"` — crea un almacén vacío nuevo (`W9$TS`), lo da de baja (`.warehouse.statusCode=="INACTIVE"`, `isActive=false`),
repite la baja y espera 422 con el mensaje exacto `"El almacén está dado de baja; solo se consulta."`, y confirma que
queda oculto de `GET /warehouses` por defecto y visible con `includeInactive=true`. Se corrió `scripts/smoke.sh` de
punta a punta dos veces más tras el cambio (una con un falso positivo por un corte de medianoche UTC en un chequeo de
fecha del Lote 4 —`licenseExpiry`, no relacionado con este cambio— y una limpia): **110 pasos, 0 fallos, `SMOKE OK`**,
sin romper ninguno de los pasos anteriores.

## Decisiones a revisar

Las 49 decisiones del plan (`docs/lote6-plan.md`, sección "Decisiones") fueron implementadas tal como están, según la
autorización general de Luis (2026-09-26), **salvo estas 18 que quedan marcadas "pendientes de ratificación"** (se
listan con el número de decisión del plan y un resumen; el texto completo y la alternativa descartada están en
`docs/lote6-plan.md`):

1. **D1 — Las olas de picking (`PickWave`/`PickTask`) y el packing en cartones (`Carton`/`CartonLine`) se DIFIEREN**;
   sus tablas no se mapean. Recolección y empaque (`PickBatch`) cubre los dos casos reales del documento maestro; la
   reserva del ledger (`ReserveAsync`/`ReleaseAsync`) ya queda lista para cuando lleguen. Alternativa descartada:
   construir las olas ya, con líneas de producto en la orden y capacidad de rutas.
2. **D4 — La varianza de recepción se asienta como RECEIPT por lo esperado + ADJUSTMENT `RECEIPT_VARIANCE`** por la
   diferencia, enlazado en `ReceiptLine.AdjustmentTxnId`; el neto es igual a lo que entró. Alternativa descartada:
   RECEIPT por lo recibido, con la varianza solo como dato de la línea, sin fila de ajuste.
3. **D5 — Se confirma el recibo completo, no línea por línea.** `ReceiptLine` no tiene estatus propio. Alternativa
   descartada: confirmación por línea con un estatus nuevo en `ReceiptLine`.
4. **D9 — Compras mínimas entran en este lote** (proveedores, PO con costo por línea, solo productos propios). La
   recepción consume la PO por la costura `IPurchaseOrderReceiving`. Alternativa descartada: diferir Compras a su
   propio lote y probar faltantes con PO sembradas por SQL.
5. **D11 — "Empacar" crea la orden con `OrderService.CreateAsync`** (origen `PICK_BATCH`) en la misma transacción que
   marca el lote `PACKED`; las líneas de la orden son paquetes sin `ProductId`. Alternativa descartada: poner
   `ProductId`/lote/serie en las `CargoLine` de la orden.
6. **D12 — Borrar desde Órdenes una orden nacida de una recolección responde 409**; solo "Recolección y empaque" la
   borra y restaura el inventario. Cancelarla NO restaura inventario (la mercancía regresa con un recibo `RETURN`).
   Alternativa descartada: un efecto sobre `OrderStatus` que restaure el inventario al cancelar.
7. **D13 — Eliminar una recolección revierte cada línea con un `ADJUSTMENT` de entrada (`PICK_BATCH_REVERSAL`)** a su
   posición original (422 si está inactiva); la serie vuelve a `AVAILABLE`. Alternativa descartada: revertir con un
   `RECEIPT` de devolución o hacia staging en vez de la posición original.
8. **D14 — La recolección asigna posición y lote por FEFO** (vencimiento, luego PICKING > RESERVE > REFRIGERATED >
   STAGING) sobre el disponible; excluye QUARANTINE y CROSSDOCK. Alternativa descartada: que el usuario escoja
   siempre posición y lote a mano.
9. **D21 — Zona STAGING obligatoria para recibir.** El recibo exige una posición de recepción (422 si no hay una zona
   STAGING activa); no se crean saldos "sin posición". Alternativa descartada: crear automáticamente la zona STG y la
   posición STG-01 en cada alta de almacén, o permitir `WarehouseBinId` NULL.
10. **D22 — El conteo lo confirma quien tiene `warehouse.count`, ajustando contra el SALDO ACTUAL bloqueado**
    (`ReconciledSystemQty`, `SystemQtyChanged` cuando el saldo se movió desde la foto); 409 si lo contado es menor que
    lo reservado. Alternativa descartada: exigir que ningún saldo contado se haya movido desde la foto y reconciliar
    solo con `inventory.adjust`.
11. **D26 — La baja de un almacén es definitiva** (`INACTIVE` terminal) y solo procede vacío, sin reservas y sin
    documentos abiertos. Alternativa descartada: baja reversible (lateral `SUSPENDED`).
12. **D27 — Cuatro permisos nuevos en WAREHOUSE (58 en total)**: `inventory.view`, `inventory.manage`,
    `inventory.adjust` y `warehouse.manage`. Plantillas: `inventory.view` para Operador de almacén, Solo lectura y
    Facturación; el resto solo para el Admin. Alternativa descartada: solo `warehouse.*`, o dar `orders.create` al
    Operador de almacén.
13. **D29 — Cross-dock en dos modos**: sobre un recibo ABIERTO (se reparte FIFO al confirmar, con faltante outbound
    visible por asignación) o sobre uno ya CONFIRMADO (reduce el putaway pendiente). Lo asignado en staging queda
    reservado. Alternativa descartada: solo el modo confirmado, sin reserva.
14. **D30 — Citas de muelle sin solapamiento por muelle**; `ARRIVED` ocupa el muelle y los cierres lo liberan; el
    motivo de un `NO_SHOW` va en el comentario del historial, sin columna propia. Alternativa descartada: solapamiento
    con solo aviso, y una columna de motivo/penalización para `NO_SHOW`.
15. **D31 — Costos y precios se ven con `inventory.view`** (y en las fuentes de análisis). Alternativa descartada:
    ocultarlos sin `inventory.manage`/`purchasing.view`.
16. **D35 — El costo se congela en `PurchaseOrderLine.UnitCost` y en `PickBatchLine.UnitCost`.** El precio de venta NO
    se congela: `IOrderInventoryLines` devuelve el `SalePrice` vigente para que el Lote 10 decida al facturar.
    Alternativa descartada: leer `Product.PurchaseCost` vivo al contabilizar, o congelar también `SalePrice`.
17. **D40 — Las transferencias entre almacenes son instantáneas**: una fila `TRANSFER`, sin "en tránsito". Alternativa
    descartada: transferencia en dos pasos con inventario en tránsito, reutilizando ASN y recibo.
18. **D42 — Se DIFIERE el filtro por almacén de `UserDataScope`** (que `docs/lote1-decisiones.md:59` ya había dejado
    para los Lotes 2 y 6). Un usuario con alcance de almacén ve todos los almacenes del tenant; el alcance por dueño
    para el Portal sí queda implementado (`InventoryScope`, D44). Alternativa descartada: aplicarlo ya en `WmsResolve`
    y en todas las consultas WMS.

Las 31 decisiones restantes del plan (una sola vía de escritura del ledger, cantidad con signo D3, motivo de ajuste
por catálogo D7, tabla de resolución de faltantes D8, numeración de la recolección con el contador `PACKBATCH` D10,
reversa por línea D13 ya listada arriba, `SerialStatus` con laterales D16, FKs compuestas D18, `WarehouseTaskId` a
`INT` D19, estatus nuevos del seed D20, mínimos por producto D23, putaway dirigido con rotación de 30 días D24,
inmutables tras movimiento D25, módulos por controlador D28, genealogía ampliada D32, límites técnicos duros D38,
excepción de bloqueo en Recolectar D48, almacén demo sembrado D49, etc.) quedaron implementadas sin cambios y no se
repiten aquí; el texto completo de cada una vive en `docs/lote6-plan.md`.

## Lo que queda fuera de este lote

- **Manual funcional y FAQ del Lote 6** (ver nota al inicio de este documento): `docs/manual/06-inventario-y-almacen.md`
  y la ampliación de `docs/manual/faq.md` con los mensajes exactos de este lote quedan pendientes de una pasada
  posterior; sin ellos el lote no está cerrado del todo según CLAUDE.md.
- Olas de picking (`PickWave`/`PickTask`) y packing en cartones (`Carton`/`CartonLine`): diferidas (D1); sus tablas
  del script de estructura no se tocan.
- Plantillas de exportación (`ACCT_TEMPLATES`) y contabilización de compras y despachos: para el Lote 10; quedan como
  insumos `ReceiptHeader.ReceivedAtUtc`, los costos congelados en `PurchaseOrderLine`/`PickBatchLine`,
  `IOrderInventoryLines` y las fuentes `RECEIPT`/`PICK_BATCH`.
- Conversión de unidades (`ProductUom`): sin modelar; las cantidades son siempre en la unidad base.
- Búsqueda COD por número de orden (13B, punto 11B del maestro): no tocada en este lote.
- `Vehicle`/`Driver.HomeWarehouseId` y `Trip.OriginWarehouseId`: siguen sin escribirse, aunque `Warehouse` ya está
  mapeado desde este lote (hallazgo heredado, D33 del plan).
- Filtro por almacén de `UserDataScope` (D42, arriba): un usuario con alcance de almacén sigue viendo todos los
  almacenes del tenant.
- Cross-dock queda como módulo de demostración, apagado por defecto para el tenant Advance; el backend responde 403
  `module_disabled` hasta que el tenant lo enciende (requiere AAL2).
- **Detalle línea por línea de los "54 hallazgos corregidos en verificación"**: como se explica al inicio de este
  documento, el código no trae la marca `Hallazgo de revisión` para el Lote 6, así que no se pudo enumerar cada uno
  con evidencia propia en esta pasada; lo verificado de forma independiente fue que build, pruebas (1731/1731),
  `db-init` ×2 y el smoke completo (110 pasos) corren en verde sobre el estado actual del repositorio.

---

**Verificación del cierre (2026-09-26/27, sobre BD recreada desde cero, esta sesión):** `dotnet build` sin errores,
`dotnet test` 1731/1731, `db-init` dos veces (58 permisos, idempotente) y `scripts/smoke.sh` completo de los Lotes 1
a 6 en `SMOKE OK` (110 pasos, con el paso de baja definitiva de almacén agregado en esta pasada). No se hizo push en
esta sesión; el árbitro formal para la corrida de CI de GitHub Actions queda pendiente del primer push de este estado.

---

**Manual funcional del lote:** `docs/manual/06-inventario-y-almacen.md` (enlazado en `docs/manual/README.md`) y sección "Lote 6" en `docs/manual/faq.md`.

**Verificación del cierre (2026-09-27, sobre BD recreada desde cero):** `dotnet build -c Release` sin errores, `dotnet test` 1731/1731,
`db-init` dos veces (58 permisos, idempotente, almacén demo ALM-01) y `scripts/smoke.sh` completo de los Lotes 1 a 6 en `SMOKE OK` (112 pasos).
Corrida de CI: ver enlace al final.
