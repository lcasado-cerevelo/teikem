# Lote 14 — Cambios de Almacén, tanda 4: Transferencias y ajustes, Conteo cíclico, Kárdex, conciliación y "Necesita tu atención"; qué se construyó y decisiones a revisar

> **Equivalencia:** este documento = Lote 4 del plan de cambios (plan 0+1 = 11, plan 2 = 12, plan 3 = 13, plan 4 = 14, plan 5 = 15).
> El repositorio ya tenía lote1…lote13, así que el cierre de cada lote del plan usa el siguiente número libre.

Fecha: 2026-09-30. Origen: el documento de cambios del dueño del producto sobre las pantallas de Almacén
(`F:\Download\Cambios.pdf`, páginas 12 a 16: Transferencias y ajustes, Conteo cíclico, Kárdex y el Pulso) y las decisiones del dueño
del 2026-09-30 (D1 a D14 y la hora de Puerto Rico; ver más abajo). Cierra el backend y el frontend web de estas piezas. El Lote 5 del
plan (Pulso: indicadores y minigráficos) sigue pendiente y será el Lote 15 del repositorio.

Como los lotes 11 a 13, este documento cubre también el frontend web: no hay un `docs/frontend/loteF…-decisiones.md` aparte. El
manual funcional está en [`docs/manual/06-inventario-y-almacen.md`](manual/06-inventario-y-almacen.md) (secciones 3 y 6, con un aviso
en la 8) y en [`docs/manual/07-pulso-y-actividad.md`](manual/07-pulso-y-actividad.md) (sección 4, "Necesita tu atención"); el de
pantallas, en [`docs/manual/frontend/f6-almacen-e-inventario.md`](manual/frontend/f6-almacen-e-inventario.md) (Transferencias y
ajustes, Kárdex de movimientos y Conteo cíclico) y en
[`docs/manual/frontend/f7a-pulso-almacen-y-actividad.md`](manual/frontend/f7a-pulso-almacen-y-actividad.md) ("Necesita tu atención");
los mensajes de error, en [`docs/manual/faq.md`](manual/faq.md) (sección "Lote 14").

## Aviso al dueño: Advance Depot

Durante la prueba de la pieza P4 (conteo cíclico en el backend), un agente **cambió temporalmente el filtro del indicador "Conteos con
diferencia" en Advance Depot** para probar que el seed corrige el filtro viejo en las compañías ya creadas. **El seed lo dejó en el
valor correcto** (`StatusCode eq RECONCILED_VARIANCE`). **No se crearon datos de prueba en Depot** (ni conteos, ni movimientos, ni
descuadres). Los datos de prueba de este lote están en la demo Advance Logistics (ver "Cómo se prueba").

## Mapa de lo construido

| Capa | Tablas / columnas | Código principal | Endpoints |
|---|---|---|---|
| Esquema | **`dbo.InventoryDiscrepancy`** (nueva, CAPA 14 con guarda `IF OBJECT_ID`): clave (producto, almacén, posición, lote; sin almacén = total del producto), `KindLookupId`, `TriggerLookupId`, `LedgerQty`, `BalanceQty`, `DetectedAtUtc`, `LastCheckedAtUtc`, `CheckCount`, `LastTxnId`, `StatusCodeId`, `ClosedAtUtc`, `ResolvedBy`, `ResolutionNotes`, `CorrectedFromQty`, `CorrectedToQty`, `CycleCountId`, `RowVersion`, FK compuestas por tenant, almacén y producto, e índice único filtrado `UX_InvDiscrepancy_OpenKey` (un solo descuadre abierto por clave). **`dbo.CycleCount`** pasa a bloque con guarda: `OriginLookupId`, `ChangesFromUtc`, `ChangesToUtc`, `FK_CycleCount_Origin`, `CK_CycleCount_Changes` e `IX_CycleCount_Origin`. Índice nuevo `IX_CycleCountLine_Bin` | `Diseño/logistica-db-estructura.sql`, `InventoryDiscrepancy.cs`, `CycleCount.cs`, `WmsDocumentConfigurations.cs`, `TeikemDbContext.cs` (`InventoryDiscrepancies`) | — |
| Seed | Dominios `InventoryDiscrepancyStatus`, `InventoryDiscrepancyKind` (`BALANCE`, `PRODUCT_TOTAL`), `ReconciliationTrigger` (`EVENT`, `MANUAL`, `SCHEDULED`, `MIGRATION`) y `CycleCountOrigin` (`MANUAL` "Selección", `CHANGES` "Lo cambiado"). `EntityType` 82 `INVENTORY_DISCREPANCY`. Estatus del descuadre: `OPEN` "Pendiente", `RESOLVED` "Resuelto", `DISMISSED` "Descartado", `SELF_CORRECTED` "Se corrigió solo". Estatus del conteo: `OPEN` "Pendiente", `COUNTED` "Contado", `RECONCILED` "Concordancia", `RECONCILED_VARIANCE` "Diferencia" (terminal, `#F59E0B`) con entrada lateral desde `OPEN` y `COUNTED`. Permiso `pulse.attention` (65 → 66 códigos). Bloques idempotentes para bases ya sembradas: reetiqueta `OPEN` y `RECONCILED` del conteo; pasa los conteos `RECONCILED` con algún ajuste enlazado a `RECONCILED_VARIANCE` con una fila de historial ("Lote 14: conteo con diferencia."); pone origen `MANUAL` a los conteos sin origen; corrige el filtro del indicador "Conteos con diferencia" (solo si tenía el filtro viejo exacto); **5b2** propaga `pulse.attention` una sola vez a los roles de compañía ya clonados que tengan `inventory.view` (el 5c ya lo usaba el diagnóstico de PIN de F8a) | `Diseño/logistica-db-seed.sql`, `CatalogDomains.cs`, `PermissionCatalog.cs` | — |
| Dominio | (sin tablas) | `ReconciliationRules` (nueva, pura: comparación, plan de una revisión, validación de resolver, mensajes), `ChangedCountRules` (nueva, pura: ventana, posiciones, líneas, topes de 200 posiciones y 31 días), `CycleCountRules.ReconcileTarget`, `CycleCountStatuses` (`ReconciledVariance`, `OpenCodes`, `ClosedCodes`, `IsReconciled`), `KardexRules` (dirección, resumen, `OwnerLabel`), `LocalDay` (días locales), `PulsePanels.Attention` | — |
| Reloj de la compañía | (sin tablas) | `LocalDay` (Dominio) e `ITenantClock`/`TenantClock` (America/Puerto_Rico → "SA Western Standard Time" → UTC−4 fija). Punto único de "hoy" y de los días locales; la zona por compañía queda lista para configurarse | — |
| API — Kárdex | (usa `InventoryTransaction`) | `InventoryReadService`: consulta única `BuildKardexQueryAsync` (lista, resumen y exportación), filtros nuevos (dueño con "Propio", motivos, dirección, almacén de origen y de destino, "solo manuales"), `KardexSummaryAsync` (SQL agrupa y `KardexRules.Summarize` aplica la perspectiva), `TransactionDetailAsync` (documento de origen y movimientos relacionados, tope 200), `OwnersAsync`; fila con `OwnerName` y `CategoryName`. `WarehouseLayoutService.SearchBinsAsync` | `GET /api/v1/inventory/transactions` (filtros nuevos), `GET .../transactions/summary`, `GET .../transactions/{id}`, `GET .../owners`, `GET /api/v1/warehouses/bins/search` |
| API — descuadres y conciliación | (usa `InventoryDiscrepancy`) | `InventoryReconciler` (extraído de `TraceabilityService`, que ahora delega), `InventoryReconciliationService` (`RunAsync` ≤ 200 productos, `CheckProductsAsync`, `SweepAsync`, `ListAsync`, `GetAsync`, `ResolveAsync` con `REBUILD_BALANCE` o `DISMISS`, autocierre), `InventoryLedger.RebuildBalanceAsync` (pone el saldo igual al Kárdex sin escribir un movimiento; 409 si el Kárdex da negativo o menos que lo reservado), `InventoryDiscrepanciesController`, `InventoryDiscrepancyDataSource` (fuente `INVENTORY_DISCREPANCY`). `import-legacy` concilia al final con `SweepAsync(MIGRATION)` | `POST /api/v1/inventory/reconciliation/run`, `GET .../reconciliation/status`, `GET /api/v1/inventory/discrepancies`, `GET` y `POST .../discrepancies/{publicId}` (`/resolve`); `GET .../reconciliation` (sin cambios de contrato) |
| Revisión en segundo plano | (sin tablas) | `IInventoryChangeSink` (bandeja por petición, anotada en `InventoryLedger.PostAsync`), `InventoryChangeCommitInterceptor` (vacía al empezar, revertir o fallar; envía solo en el commit real), `InventoryReconciliationQueue` (en memoria, con tope y contador de descartados), `InventoryReconciliationWorker` (`BackgroundService`: agrupa por compañía, corre `CheckProductsAsync(EVENT)` bajo bloqueo con el alcance "Sistema"). Configuración `Inventory:Reconciliation` (`Enabled`, `DebounceMs` 1500, `MaxBatchProducts` 500, `Capacity` 10000, `MinRecheckSeconds` 10) en `appsettings.json`. La consola (`db-init`, `import-legacy`) no arranca el worker | `GET /api/v1/inventory/reconciliation/status` (contadores por compañía: `Pending`, `Processed`, `Dropped`, `Failed`, `LastError`) |
| API — conteo cíclico | (usa `CycleCount`, `CycleCountLine`) | `CycleCountService`: `ListPageAsync` (total y página), `PreviewChangesAsync` y `CreateFromChangesAsync` (una transacción, tope 200), `ReconcileAsync` en un paso desde Pendiente o Contado (destino Concordancia o Diferencia), filtros `zoneIds` y `origins`, filas con posición, zona, origen, tarea y asignado, línea con `Barcode`; los filtros `from`/`to` pasan a días locales | `GET /api/v1/cycle-counts/page`, `GET .../changes-preview`, `POST .../from-changes`, `POST .../{id}/reconcile` (un paso); asignar con `POST /api/v1/warehouse-tasks/{taskId}/assign` (sin cambios) |
| API — "Necesita tu atención" | (sin tablas) | `AttentionFeedService`, `IAttentionItemProvider`, `InventoryDiscrepancyAttentionProvider`, `AttentionController`, panel `ATTENTION` (orden 5) | `GET /api/v1/analytics/attention` (`pulse.attention`) |
| Actividad y analítica | (usa `CycleCount`) | `ActivityRules` y `WarehouseActivityProvider` (Diferencia = `COUNT_RECONCILED`), `SystemAnalyticsSeeder` (indicador "Conteos con diferencia" cuenta `RECONCILED_VARIANCE`) | — |
| Contratos web y móvil | Sin tablas | `web-app/openapi.json`, `web-app/src/kernel/api/schema.d.ts` y `app-almacen/src/kernel/api/schema.d.ts` regenerados | — |
| Kit (web) | Sin tablas | `SummaryBar`, `BinMultiFilter` (búsqueda de posiciones entre almacenes, con lector), `OwnerFilter`, `KardexTransactionModal`, `kardexView.ts`, `adjustmentReasons.ts` (motivos por dirección), `KIT.md` | — |
| Frontend — Transferencias y ajustes | — | `TransfersAdjustmentsScreen.tsx` (pestañas Ajustes y Transferencias), `movementFilters.ts`, `InventoryAdjustModal.tsx` (Subir/Bajar, compartido con el Kárdex y la ficha del producto), `InventoryTransferModal.tsx` (origen → ítem → series → destino), `InventoryReportButtons.tsx` y `inventoryReports.ts` (Reporte de ajustes con los filtros de la pantalla) | — |
| Frontend — Kárdex | — | `InventoryScreen.tsx` y `InventoryFilterBar.tsx` (filtros compartidos por Kárdex, Saldos y Conciliación), `DiscrepanciesPanel.tsx`, `DiscrepancyModal.tsx`, resumen con `SummaryBar` | — |
| Frontend — Conteo cíclico | — | `CycleCountListScreen.tsx` (dos paneles), `CycleCountFilterBar.tsx`, `CountTaskList.tsx`, `CountDetailPanel.tsx`, `CountScanBox.tsx`, `CountLineModals.tsx`, `useCountDrafts.ts`, `CreateCountModal.tsx`, `ChangedCountModal.tsx`, `countView.ts`. Se borraron `CycleCountDetailScreen.tsx` y la pestaña "Tareas de conteo" | — |
| Frontend — Pulso | — | `AttentionPanel.tsx`, `attention.ts`, `pulsePanels.tsx` y `pulseLayout.ts` (clave `ATTENTION`), `pulse.css` (`.inbox` y `.work` de la maqueta), `analytics/api.ts` ("Conteos abiertos" usa el total de la página) | — |
| Frontend — rutas, menú y enlaces | — | `routes.tsx`: `/warehouse/transfers-adjustments` (después de Recolección y empaque), `/warehouse/inventory-adjustments` redirige a `/warehouse/purchase-orders`, `/warehouse/cycle-counts/:id` redirige a `?count=:id`; `activity.ts` enlaza a `?count=`. Se borraron `InventoryAdjustmentsScreen.tsx` y su prueba | — |
| Humo | — | `scripts/smoke.sh`: bloque "Lote 14" al final (Kárdex, resumen, detalle, dueños, posiciones, "lo cambiado", conciliación manual, descuadre real con `SMOKE_SQL`, "Necesita tu atención") y CC7 espera `RECONCILED_VARIANCE`; la lista de paneles del administrador pasa a 5 | — |

Todo bajo el filtro de tenant existente: `InventoryDiscrepancy` lleva `TenantId` (`ITenantScoped`) y es `[AuditEntity]`; un descuadre de otra
compañía responde 404 "Descuadre no encontrado.". Permisos: los descuadres los ve `inventory.view` y los resuelve `inventory.adjust`; "lo
cambiado" pide `warehouse.count` (no `warehouse.count.capture`); "Necesita tu atención" pide `pulse.attention` y cada tipo de aviso decide
su módulo y su permiso. Todos los endpoints nuevos de inventario y conteo llevan `[RequireModule(ModuleKeys.WmsLotSerial)]`. La prueba de
seguridad de controladores (`WmsControllerSecurityTests`) ya declara las acciones nuevas (de 108 a 120).

## Decisiones del dueño del producto (2026-09-30)

Mandan sobre las opciones que había propuesto el arquitecto.

| # | Tema | Elegido | Cómo quedó |
|---|---|---|---|
| D1 | Pantalla vieja "Ajustes de inventario" (faltantes de compra) | **C:** se quita del menú | La ruta `/warehouse/inventory-adjustments` redirige a Compras (`/warehouse/purchase-orders`); se borró `InventoryAdjustmentsScreen`. Los faltantes se resuelven solo desde la pestaña Faltantes de la ficha de cada orden de compra. Ningún otro enlace de la web apunta a la ruta vieja (se buscó en `web-app/src`). "Transferencias y ajustes" ocupa su lugar, después de Recolección y empaque |
| D2 | Ventana de "lo cambiado" | **A:** desde la última generación del almacén | `ChangesToUtc` del último conteo de origen "Lo cambiado" del almacén; la primera vez, desde las 00:00 locales de hoy; nunca más de 31 días atrás; las fechas se pueden editar antes de crear |
| — | Día UTC o día local | **Hora de Puerto Rico** | "Hoy" empieza a medianoche local en "lo cambiado", en los filtros de fecha del Kárdex, del resumen, de los descuadres y de la lista de conteos. Punto único: `ITenantClock`; en la web, `TENANT_TIME_ZONE` en `countView.ts`. Queda listo para configurarse por compañía |
| D3 | Qué se cuenta | **A:** un conteo por posición, con todo lo que hay | Incluye las posiciones y claves que quedaron en 0 (opción "Incluir posiciones vacías", activa por defecto). Los movimientos que salen de un conteo no cuentan para el siguiente |
| D4 | Tope | **A:** hasta 200 posiciones por vez | Más de 200 → 400 con el mensaje del tope; no se crea nada |
| D5 | Descuadre | **A:** lo ven quienes ven inventario; lo resuelve quien puede ajustar | "Corregir el saldo según el Kárdex" o "Descartar" con nota; se cierra solo si vuelve a cuadrar; uno descartado no reaparece con las mismas cifras; tras corregir se ofrece "Crear conteo de esa posición"; todo queda en el historial |
| D6 | Necesita tu atención | **A:** los 5 más antiguos, "Revisar" y "Ver todos (N)" | Vacío = "Todo en orden". La ve quien tiene `pulse.attention` (Operador de almacén, Facturación, Solo lectura y el administrador) y, dentro del panel, solo suma lo que puede ver por permiso y módulo |
| D7 | Estatus del conteo | **A:** Pendiente → Contado → Concordancia o Diferencia | Son estatus reales. Los conteos históricos cerrados con ajuste pasan a Diferencia |
| D8 | Confirmar | **A:** un solo paso desde Pendiente | "Confirmar conteo y ajustar" (`warehouse.count`). Contado queda solo para el conteo a ciegas de la app |
| D9 | Tabla del conteo | **A:** todas las líneas con lo esperado | El escáner lleva a la línea y pide la cantidad |
| D10 | Asignación | **A:** ícono en la lista de la izquierda | Se quitó la pestaña "Tareas de conteo". Asignar usa `POST /api/v1/warehouse-tasks/{taskId}/assign` |
| D11 | Captura de ajustes | **A:** Subir/Bajar con cantidad positiva | En la pantalla nueva, el Kárdex y la ficha del producto. "Encontrado" solo al subir; "Daño", "Pérdida" y "Vencido" solo al bajar. El API no cambió: sigue recibiendo la cantidad con signo |
| D12 | Qué listan las pestañas | **A:** todos los movimientos | También los del sistema, con filtro por motivo y "Solo manuales" |
| D13 | Resumen en Saldos | **A:** resumen de movimientos más En mano y Disponible | En mano y Disponible salen de `BalancePageDto`; sin cambio de backend |
| D14 | Cuándo se concilia | **A:** en segundo plano, segundos después de cada movimiento | El barrido programado fuera de horario queda fuera del lote (ver abajo) |

## Qué se construyó

### Backend

- **Kárdex.** Una sola consulta para la lista, el resumen y la exportación. Filtros nuevos: dueño (`ownerClientPublicIds` y `includeOwn`
  para "Propio"), `reasons`, `direction` (`IN` entradas, `OUT` salidas, con la perspectiva de `signedQuantity`), almacén de origen
  (`fromWarehousePublicIds`), de destino (`toWarehousePublicIds`) y `manualOnly` (sin documento de referencia). La fila trae `ownerName`
  y `categoryName`. El **resumen** da movimientos, entradas y salidas (número y unidades) e internos, con los mismos filtros. El
  **detalle** de un movimiento resuelve el documento de origen (recibo, recolección, conteo, orden de compra, orden de transporte, tarea
  con su documento padre, producto, cruce de muelle) y los movimientos relacionados (los del mismo documento o, sin documento, los del
  mismo asiento; tope 200).
- **Descuadres.** Un descuadre es que el saldo de una clave no coincide con lo que suman los movimientos del Kárdex (por posición, o en
  el total del producto). Se abre solo, con origen `EVENT`, `MANUAL` (botón "Ejecutar conciliación") o `MIGRATION`. Hay a lo sumo un
  descuadre abierto por clave (índice único filtrado). Se resuelve con `REBUILD_BALANCE` (el saldo toma el valor del Kárdex, sin escribir
  un movimiento; guarda "de" y "a") o `DISMISS` (nota obligatoria). Si al resolver ya cuadraba, se cierra como "Se corrigió solo". Los
  totales por producto no se corrigen: solo se descartan. Todo pasa por `StatusService.TransitionAsync` y queda en el historial.
- **Revisión en segundo plano.** Cada asiento del `InventoryLedger` anota los productos tocados; al confirmarse la transacción real
  (nunca en un intento reintentado ni revertido) el aviso pasa a una cola en memoria. Un servicio de fondo agrupa por compañía durante
  1,5 s, revisa sin bloqueos y confirma bajo bloqueo solo lo sospechoso, para no dar falsos positivos por movimientos en vuelo. Prueba en
  vivo en la demo: un descuadre abierto solo en unos 2 s tras un ajuste y cerrado solo ("Se corrigió solo") al revertirlo.
- **Conteo cíclico.** Estatus reales Pendiente → Contado → Concordancia o Diferencia. **Concordancia** es que la confirmación no asentó
  ningún movimiento; **Diferencia**, que asentó al menos uno (ajuste o, en series, baja, alta o transferencia). Confirmar funciona en un
  paso desde Pendiente o Contado. "Lo cambiado" crea un conteo por posición con movimientos en la ventana, con todo lo que hay en ella y
  las claves que quedaron en 0; salta las posiciones inactivas o con un conteo Pendiente o Contado; todo o nada, en una transacción.
  Medido en la demo: 120 posiciones en 1,5 s. La lista trae total y página.
- **Necesita tu atención.** Una fila por descuadre abierto (los 5 más antiguos), con la ruta de "Revisar" y el total para "Ver todos (N)".
  No hay tabla de avisos: cada proveedor calcula sus pendientes al leer.

### Frontend web

- **Transferencias y ajustes** (`/warehouse/transfers-adjustments`): pestañas Ajustes y Transferencias con filtros al servidor, resumen,
  columna Dueño, fila que abre el detalle del movimiento, "Reporte de ajustes" con los filtros de la pestaña, y los botones Ajustar y
  Transferir (`inventory.adjust`).
- **Ajuste con Subir/Bajar.** Cantidad positiva; motivos según la dirección, con buscador; pista "Disponible en la posición"; lote (o
  series) también al bajar; nota obligatoria. El mismo modal lo usan el Kárdex y la ficha del producto.
- **Transferencia** en el orden origen → ítem (producto y lote) → series → destino → cantidad con tope → nota. Antes no permitía lote ni
  series.
- **Kárdex de movimientos**: los mismos filtros en las tres pestañas (los que una pestaña no aplica se atenúan y se nombran bajo la
  barra), resumen con En mano y Disponible en Saldos, columnas Dueño y Categoría, `?txn=` abre el detalle, y la pestaña Conciliación con
  la lista de descuadres, "Ejecutar conciliación", el estado de la revisión automática y el detalle con Corregir, Descartar y "Crear
  conteo de esa posición".
- **Conteo cíclico en dos paneles** (34/66, apilados bajo 900 px): lista a la izquierda con Asignar y Eliminar como íconos; conteo
  elegido a la derecha (`?count=<id>`) con todas las líneas y lo esperado, escáner por SKU, código de barras, lote o serie, cantidad
  editable en la fila, "Agregar lo encontrado", "Refrescar foto", Historial y "Confirmar conteo y ajustar". "Nuevo conteo" y "Conteo de lo
  cambiado" en la cabecera.
- **Pulso**: panel "Necesita tu atención" antes de "Tus indicadores"; "Conteos abiertos" del panel Almacén ahora es el total real de
  conteos Pendientes y Contados.

### Menú

Orden del grupo Almacén: … Recibo → Recolección y empaque → **Transferencias y ajustes** → Conteo cíclico → Cruce de muelle → Kárdex de
movimientos. "Ajustes de inventario" ya no está.

## Cómo se prueba

Corridas del 2026-09-30 sobre el código de este lote.

1. `dotnet build Teikem.sln && dotnet test Teikem.sln` — **2.651 pruebas en verde**; compilación incremental sin avisos (0).
   Pruebas nuevas: `ReconciliationRulesTests`, `InventoryReconciliationServiceTests`, `InventoryReconciliationWorkerTests`,
   `InventoryChangeSinkTests`, `KardexSummaryTests`, `KardexDetailTests`, `CycleCountFromChangesTests`, `ChangedCountRulesTests`,
   `LocalDayTests` (incluye el borde 03:59:59Z contra 04:00Z), `AttentionFeedServiceTests` (incluye la seguridad del controlador nuevo).
   Ajustadas: `WmsControllerSecurityTests` (120 acciones), `WmsCatalogTests`, `WmsContractsTests`, `OwnedEntityResolverCoverageTests`,
   `AnalyticsSeedFieldsTests`, `ActivityRulesTests`, `CycleCountServiceTests`, `CycleCountFilterTests`, `InventoryReadServiceTests`,
   `InventoryLedgerTests`, `KardexRulesTests`, `WmsFixture`, `PulseLayoutTests`, `FleetCatalogTests`, `OrderCatalogTests` y `TripCatalogTests`. `InventoryReconciler` extraído de `TraceabilityService` se comparó con la conciliación anterior en una prueba de
   equivalencia.
2. Web (`cd web-app && npm run check`): `tsc` limpio, oxlint sin errores, vitest **89 archivos / 796 pruebas en verde** y build OK.
3. App móvil (`cd app-almacen`): `tsc` limpio con el `schema.d.ts` regenerado.
4. Recorridos Playwright (`web-app/e2e`, contra el API real): **48 pasaron, 0 fallaron, 48 omitidos** (los reportes recibidos desglosan
   escritorio 29 y móvil 7; no desglosan el resto).
   - `lote14.spec.ts` (nuevo, 8 pasos): (1) menú con Transferencias y ajustes después de Recolección y empaque, sin "Ajustes de
     inventario" y con la ruta vieja redirigiendo a Compras; (2) ajuste Bajar con el motivo filtrado por dirección y la nota
     obligatoria; (3) Subir 10 y Bajar 2 con nota, en la pestaña Ajustes con signo y motivo; (4) transferencia origen → ítem → destino en
     la pestaña Transferencias y su detalle; (5) un filtro del Kárdex que se conserva al pasar a Saldos, el resumen y `?txn=`; (6) "Conteo
     de lo cambiado" con vista previa, creación, captura en la fila y confirmación en un paso; (7) Pulso con "Todo en orden"; (8) móvil
     (360 px) sin scroll horizontal en Transferencias y ajustes, en las tres pestañas del Kárdex y en Conteo cíclico. Genera las capturas
     `docs/manual/frontend/img/l14-*.png`.
   - `f6.spec.ts`: pasos 4, 5 y 7 ajustados al modal Subir/Bajar y a las columnas nuevas del Kárdex.
5. **Base desde cero, repetible** (con los archivos de `docs/migracion/`): `db-reset` y luego `import-legacy` de Depot (0 rechazos, 1.310
   asientos de saldo inicial, conciliación sin diferencias) y de Solutions (0 rechazos, 32 asientos). Después, el seed forzado sobre la
   base ya poblada, sin errores.
6. **Verificación en el navegador, por pieza, en la demo Advance Logistics** (a 1440 y a 360 px donde se indica):
   - Kárdex: filtros, resumen, detalle con documento y relacionados.
   - Transferencias y ajustes: ajustes y transferencias hechos y revertidos.
   - Conciliación: un descuadre provocado se abre solo en unos 2 s, se resuelve y otro se cierra solo al revertir; queda un descuadre
     Resuelto.
   - Conteo: "lo cambiado" → CC-00124 escaneado, capturado y confirmado en un paso → Diferencia; asignar y eliminar desde la lista;
     CC-00123 dado de baja; 360 px sin scroll horizontal. Quedan CC-00124 y conteos de prueba en la demo; el inventario se revirtió.
   - Pulso: descuadre provocado → aparece en "Necesita tu atención" → Revisar → Corregir → "Todo en orden" (1440 y 360 px).
7. **Humo (`scripts/smoke.sh`, incluido el bloque "Lote 14"):**

   **`SMOKE OK`, 127 pasos en verde**, corrido con `SMOKE_SQL` (sqlcmd local con autenticación de Windows) sobre una base recién
   recreada (`db-reset` + `import-legacy` de Depot y Solutions, 0 rechazos), así que también corrieron los pasos por SQL, entre ellos
   el descuadre real del Lote 14 (EVENT → Corregir → Resuelto → 422 al repetir). Arreglos al humo en el camino:
   - `sqlcmd` corre con `QUOTED_IDENTIFIER` apagado y el `UPDATE dbo.StockBalance` fallaba (Msg 1934, índices filtrados): las 18
     sentencias de `$SMOKE_SQL` ahora empiezan con `SET QUOTED_IDENTIFIER ON;` (varias nunca se habían corrido en local).
   - La verificación "zonas y muelles de ALM-01" exige la demo recién sembrada: los recorridos y las pruebas manuales habían dejado
     4 zonas extra en ALM-01; por eso el humo final corrió sobre la base recreada, igual que el CI.

8. Las capturas del manual: las nuevas `l14-*.png` (ajuste-modal, ajustes-lista, transferencias-lista, movimiento-detalle, kardex-resumen,
   conciliacion, conteo-dos-paneles, conteo-cambiado-modal y pulso-atencion) y las `f6-inventario-*` y de Pulso regeneradas por los
   recorridos.

### Qué no se probó

Los reportes recibidos no cubren lo siguiente; nada de esto se probó al escribir este documento:

- **El CI de GitHub Actions** con este código. La resolución de la zona horaria (`America/Puerto_Rico`, con respaldo "SA Western Standard
  Time" y, si no, UTC−4 fija) no se ejercitó en Linux.
- **El humo completo** (ver el marcador arriba). El paso del descuadre real necesita `SMOKE_SQL`: en el navegador se provocó a mano en la
  demo, pero el recorrido de Playwright solo comprueba "Todo en orden" y "Sin descuadres pendientes".
- **Un reinicio del servidor con revisiones pendientes** (se pierden; ver la decisión 1) y **dos instancias del API** a la vez (solo las
  protege el índice único).
- **Una generación de "lo cambiado" con 200 posiciones o más.** Lo medido fue 120 posiciones en 1,5 s.
- **El PDF de "Reporte de ajustes" desde la pantalla nueva** y **un lector de código de barras físico** (el escáner se probó tecleando el
  código y pulsando Enter).
- **Compañías que apagan estatus** del conteo o del descuadre desde sus catálogos.
- **El seed sobre una base con conteos históricos cerrados con ajuste.** Los reportes no dicen cuántos conteos pasaron a Diferencia con el
  bloque del seed; el bloque es idempotente y corrió sin error sobre la base poblada, pero no hay una cifra que lo muestre.
- **Etiquetas personalizadas por compañía** de los estatus del conteo que el seed reetiqueta (`OPEN`, `RECONCILED`): no se verificó qué
  pasa si una compañía las había cambiado.
- **Redirección de `/warehouse/inventory-adjustments` para un usuario sin `purchasing.view`**: aterriza en Compras y ve la pantalla de "Sin
  permiso".
- **El arrastre de la barra del Conteo con el ratón o el dedo.** El teclado y el arrastre los cubren las pruebas de `SplitPane`.

## Decisiones a revisar

1. **Las revisiones pendientes se pierden al reiniciar el servidor** (D14, aceptado). Las cubren el siguiente movimiento del mismo
   producto y el botón "Ejecutar conciliación". Los contadores de `GET /inventory/reconciliation/status` son por compañía y viven en
   memoria desde el arranque. Si la cola se llena (10.000 avisos) se descarta y se cuenta en `Dropped`.
2. **Casi nunca aparecerá un descuadre en la operación normal.** Cada movimiento escribe el saldo y el Kárdex en la misma transacción, así
   que un descuadre viene de un arreglo directo en la base, de una restauración, de la migración o de un error de programación. Un ajuste
   o un conteo no lo arreglan (mueven las dos cosas por igual): solo lo corrige "Corregir el saldo según el Kárdex" o se descarta con
   nota. Un descartado con las mismas cifras no se reabre; con cifras distintas, sí. Uno resuelto o "Se corrigió solo" que reaparece abre
   un descuadre nuevo.
3. **El total de un producto no se corrige.** Un descuadre de tipo "Total del producto" solo se descarta (422 con el mensaje que lo dice);
   y una corrección por posición da 409 si el Kárdex sale negativo o menor que lo reservado.
4. **Dos generaciones simultáneas de "lo cambiado" del mismo almacén** podrían tomar la misma posición: el servidor vuelve a mirar dentro de
   la transacción, pero sin bloqueo entre las dos. El riesgo es un conteo repetido de una posición (borrable).
5. **Estatus del conteo: desvío de la entrada lateral.** El plan decía que `RECONCILED_VARIANCE` entraba solo desde `COUNTED`; se sembró
   también desde `OPEN`, porque "Confirmar conteo y ajustar" va de Pendiente a Diferencia en un paso (D8). Concordancia desde Pendiente no
   necesita regla (sin reglas laterales, `StatusService` lo permite).
6. **"Diferencia" significa que la confirmación asentó al menos un movimiento**, no que la línea difiera de la foto. Si el saldo cambió
   desde la foto y lo contado coincide con el saldo actual, no se asienta nada y el conteo queda en Concordancia aunque lo contado no sea
   la foto (la línea sí queda marcada como "saldo cambió").
7. **PIN de operador y `pulse.attention`.** El Operador de almacén recibe `pulse.attention` en su plantilla. Un PIN que asignó alguien que
   no es administrador de plataforma y que no cubre ese permiso deja de servir en el siguiente inicio de sesión del aparato
   (`PinService.AssignerStillCoversAsync`). El diagnóstico de PIN sin cobertura del seed (bloque 5c de F8a) ya avisa también por
   `pulse.attention`, en el log de `db-init`; el bloque 5b2 se lo da a todo rol con `inventory.view`, pero quien haya recibido `inventory.view`
   solo como permiso individual no lo tendría. Revisar los PIN al actualizar.
8. **`db-init` no es idempotente fuera de lo guardado** (decisión 18 del Lote 13). `InventoryDiscrepancy` y el bloque de `CycleCount` llevan
   guarda, pero el resto del script de estructura sigue con `CREATE TABLE` a secas; sobre una base existente que cambió el script, `db-init`
   no basta. Para este lote se usó `db-reset` + `import-legacy` (el dueño aceptó recrear la base) y el seed forzado sobre la base poblada.
9. **`ActivityRules.TenantZone` sigue en UTC.** "Hoy" y la ventana 24 h / 48 h de Actividad reciente todavía se calculan con el día UTC;
   el resto del lote ya usa el reloj de la compañía (`ITenantClock`).
10. **Cambio de comportamiento del API: `from` y `to` son días locales de Puerto Rico**, no UTC, en el Kárdex, su resumen, los descuadres
    y la lista de conteos (`GET /cycle-counts` y `/cycle-counts/page`). Una integración que mandaba fechas pensando en UTC puede ver
    movimientos de la noche del día anterior o del siguiente. La zona por compañía queda para un lote posterior.
11. **`AssignTaskModal` requiere `admin.users`** para listar usuarios. Quien tiene `warehouse.manage` sin `admin.users` ve el ícono Asignar
    pero no puede elegir a quién ("Su usuario no puede consultar el listado de usuarios."). Como en el Lote 13.
12. **Flakiness vista una vez en el paso 6 de `lote14.spec.ts`** ("Conteo de lo cambiado"). La corrida final pasó completa (48 pasaron, 0
    fallaron); los reportes recibidos no dicen cuál fue la causa. Si reaparece en el CI, empezar por ese paso.
13. **El detalle del movimiento no incrusta el cuerpo del documento** (el plan lo pensaba con `PickBatchDetailBody` y un resumen del recibo):
    muestra la cabecera del documento, "Abrir" (según permiso y módulo) y los movimientos relacionados. Una tarea de almacén abre el
    documento de su padre.
14. **El enlace a un conteo usa `?count=<id>`** (el detalle del movimiento, Actividad y "Crear conteo de esa posición"); la ruta vieja
    `/warehouse/cycle-counts/:id` redirige.
15. **Saldos no filtra por Dueño** (el API de saldos no lo tiene): el filtro solo cambia el resumen de movimientos. **Transferencias no
    tiene filtro Motivo** (las transferencias no llevan motivo). Una transferencia interna se muestra sin signo.
16. **`InventoryLedger.ReconcileAsync` se retiró** y `LedgerKey` se unificó con `BalanceKey`. El reporte de `import-legacy` ahora muestra
    códigos y ya no agrega la fila de "total" cuando hay descuadres por posición. `GET /inventory/reconciliation` conserva su contrato.
17. **Dirección sin filtro de ubicación:** `IN` es cantidad > 0 que no sea transferencia y `OUT` es cantidad < 0; una transferencia sin filtro
    de ubicación es "interna" (no entra ni sale). Con filtro de almacén o posición, se mide desde ese filtro.
18. **Resolver un descuadre que ya cuadraba responde 200** y lo cierra como "Se corrigió solo" (no es error). Cuando un `400` trae un solo
    error, el mensaje también va en `detail`.
19. **Panel `ATTENTION` con orden 5** (el plan proponía 15) y clave propia, en lugar de la reservada `DECISIONS` ("Necesita tu decisión"), porque
    el dueño lo llama "Necesita tu atención". Queda sobre `ORDERS_RIVER` (10) y bajo la franja del Lote 15. Operación y COD podrán sumar
    filas con sus proveedores.
20. **Conteos abiertos en el Pulso** = Pendientes + Contados (total real de la página, ya no el largo de una lista cortada en 200).
21. **Los números del panel usan el formato del idioma** y el panel se vuelve a pedir cada vez que se vuelve al Pulso
    (`staleTime: 0`) porque la conciliación corre sola.

## Qué queda fuera de este lote (a propósito)

- **Barrido programado de la conciliación** (fuera de horario). Queda el punto de enganche `InventoryReconciliationService.SweepAsync(SCHEDULED)`
  y el origen `SCHEDULED` ya sembrado; falta el motor de trabajos programados.
- **Foto diaria de ubicaciones.**
- **Avisos por correo** de los descuadres.
- **Configuración de la conciliación por compañía** (hoy es una sola configuración del servidor) y **zona horaria por compañía** (el reloj ya
  está preparado).
- **Otras revisiones de consistencia** (series contra existencia, reservado contra asignaciones); el tipo de descuadre las deja preparadas.
- **Orden por columna en el servidor** de Kárdex y de conteos: el orden por encabezado sigue reacomodando solo la página visible.
- **Cambios a la app móvil** más allá de regenerar `schema.d.ts`: sigue creando conteos por posición y sin leer estatus.
- **Conteo a ciegas desde la web** (decisión D8 opción C descartada): sigue siendo de la app de almacén; la web no confirma conteos a ciegas.
- **Lote 5 del plan (Lote 15 del repositorio)**: indicadores y minigráficos del Pulso, entre ellos "Descuadres pendientes".
- **Corrida del CI** con este código: se confirma al hacer push (el humo local ya pasó completo).
