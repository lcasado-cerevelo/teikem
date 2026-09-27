# Lote 7A — Pulso de almacén y Actividad reciente (backend): qué se construyó y decisiones a revisar

Fecha: 2026-09-27. Plan aprobado: `docs/lote7A-plan.md` (y su espejo `docs/lote7A-plan.json`). Diseño:
`Diseño/logistica-funcionalidades-maestro.md`, módulo 12, subsección "Pulso del día — diseño consolidado". Este documento
cierra solo la parte de **backend** de 7A (P0, P1, P2); el frontend (`docs/frontend/loteF7A-plan.json`) queda pendiente
y cierra aparte con `docs/frontend/loteF7A-decisiones.md`.

## Mapa de lo construido

| Capa | Tablas / columnas | Código principal | Endpoints |
|---|---|---|---|
| P0 — catálogo e índice | Sin tablas nuevas. Índice `IX_EntityStatusHistory_TenantDate (TenantId, ChangedAtUtc)`; `LookupCode.Entity = 'ActivityEventType'` (23 filas de Almacén, `ExtraJson` con `module`/`mandatory`/`defaultOn`); `EntityType.PRODUCT_CATEGORY` (79) | `ActivityEvents`, `LookupDomains.ActivityEventType`, `BusinessModules`, `ActivityContracts.cs` (`ActivityQuery`, `ActivityEventDto`, `ActivityPageDto`), `IActivityEventProvider`/`ActivityScope` | — |
| P1 — feed de actividad | (usa las tablas existentes de Almacén, Compras, ledger y auditoría; ninguna tabla de eventos) | `ActivityRules` (ventana, tope de página, mapeos, orden — puro), `ActivityFeedService` (módulos visibles, 403, unión, paginado), `WarehouseActivityProvider` (historial de estatus, tareas, ledger, auditoría de baja de producto y de faltantes resueltos) | `GET /api/v1/analytics/activity` |
| P1 — filtro de productos | (sin cambio de esquema) | `ProductListQuery.BelowMin`, `ProductService.ListAsync` (mismo cálculo que `IsBelowMin`) | `GET /api/v1/products?belowMin=true` |
| Auditoría de categoría | `ProductCategory` ahora audita bajo `PRODUCT_CATEGORY` (antes `PRODUCT`) | `[AuditEntity(EntityTypes.ProductCategory)]`, `PermissionCatalog.OwnerReadPermission/OwnerWritePermission[ProductCategory]`, `ClosedOwnedEntityResolver(ProductCategory)` | (recurso polimórfico: 404 siempre) |
| P2 — indicadores y gráfico | Sin tablas nuevas (usa `IndicatorDefinition`/`ChartDefinition` existentes) | `CycleCountDataSource` (fuente `CYCLE_COUNT` nueva), `SystemAnalyticsSeeder` (2 indicadores + 1 gráfico nuevos, módulo `WAREHOUSE`, `ShowInPulse=1`) | (Pulso ya existente: `GET /api/v1/analytics/pulse`) |

Todo bajo el filtro de tenant existente (`ITenantScoped`/`TeikemDbContext`); el feed no agrega una fuente de datos nueva
al motor de análisis, es un servicio propio con su propio contrato.

## Cómo se prueba

1. `dotnet build Teikem.sln` — compiló sin errores ni advertencias (verificado en este entorno).
2. `dotnet test Teikem.sln` — **1807 pruebas, 0 fallidas** (verificado en este entorno; no incluye `db-init` ni `smoke.sh`
   porque este contenedor no tiene salida a SQL Server). Del lote: `ActivityRulesTests` (28 casos: ventana 24h/48h/today con
   zona horaria, tope de página, mapeo de motivo de ajuste → código, misma/distinta bodega, orden, `ApplyOverride` con
   obligatorio/opcional, resolución de módulo visible/403), `ActivityControllerSecurityTests` (por reflexión: el controlador
   sigue exigiendo `analytics.view` + módulo `ANALYTICS`, sin `[AllowAnonymous]` ni permiso de acción que lo reemplace),
   `AnalyticsSeedFieldsTests` (+5 pruebas: indicadores/gráfico nuevos del Pulso de almacén con su filtro, fuente `CYCLE_COUNT`
   con sus campos), `WmsDataSourceTests` (+1: el filtro de "Unidades recibidas" suma 8, no 10, con 10 esperados y 8
   recibidos), y las coberturas de catálogo/contratos/resolvers (`WmsCatalogTests`, `WmsContractsTests`,
   `OwnedEntityResolverCoverageTests`) actualizadas para `PRODUCT_CATEGORY`.
3. Con el API arriba y BD limpia (no ejecutado en este entorno; recorrido esperado del `step "actividad reciente"` agregado a
   `scripts/smoke.sh`, después del paso de recepción del Lote 6):
   - `GET /api/v1/analytics/activity?module=WAREHOUSE` trae `RECEIPT_CONFIRMED` (obligatorio, con quién lo hizo),
     `PO_SENT` (opcional), `RECEIPT_PUTAWAY_DONE`, `PO_RECEIVED`, `PUTAWAY_DONE`/`REPLENISH_DONE`/`TASK_CANCELLED` de las
     tareas, `ASN_CANCELLED`, `PICK_CANCELLED`, 3 `PO_SHORTAGE_RESOLVED` de una orden con tres resoluciones, y 2+
     `PRODUCT_DEACTIVATED` de un producto dado de baja y reactivado.
   - Sin `module` → primer módulo visible (`WAREHOUSE`) con `total` y `take=1`.
   - `?onlyMandatory=true` incluye `PO_CANCELLED`/`PICK_CANCELLED` y excluye `PO_SENT`.
   - `?take=51` → 400 `El máximo por página es 50.`; `?window=7d` → 400 `La ventana debe ser 24h, 48h o today.`.
   - Un despachador (sin `inventory.view`) pidiendo `module=WAREHOUSE` → 403
     `No tiene permiso para ver la actividad del módulo WAREHOUSE.` con `PERMISSION_DENIED` en seguridad; sin `module` →
     `visibleModules: []`, `total: 0`. Un operador de almacén (con `inventory.view` pero sin `analytics.view`) → 403 desde la
     política del controlador (antes de llegar al servicio).
   - `GET /api/v1/products?belowMin=true` cuenta un producto con mínimo y sin saldo (el `SUM` de un conjunto vacío es `NULL`
     en SQL Server, no en el proveedor `InMemory`: la prueba que importa es la del smoke contra SQL Server real) y no cuenta
     uno con saldo pero sin mínimo; el `total` no cambia con `take=1` frente a `take=200`.
   - Un conteo cíclico conciliado con diferencia aparece como `COUNT_VARIANCE` y `COUNT_RECONCILED`; dar de baja un almacén
     vacío aparece como `WAREHOUSE_DEACTIVATED`.

> Cómo se ejecutó realmente en este cierre: el contenedor de trabajo no tiene salida a SQL Server, así que solo se corrieron
> `dotnet build` y `dotnet test` (ambos verdes, arriba). `db-init` y `scripts/smoke.sh` (con el paso `activity` ya agregado)
> quedan para correr en GitHub Actions o en un entorno con Docker, como en los lotes anteriores. La base local debe
> recrearse: el índice nuevo cambia el hash de `logistica-db-estructura.sql`.

## Decisiones del plan (ya aprobadas, se dejan registradas)

1. Sin tabla de eventos: el feed se calcula al leer (ventana corta e índice por fecha). Si el volumen lo pide más adelante,
   se puede materializar con un `IStatusTransitionEffect` sin cambiar el contrato del endpoint.
2. Bandera obligatorio/opcional y módulo en `ExtraJson` del `LookupCode` (sin columna nueva).
3. El permiso `billing.view` no existe todavía; este lote solo construye el proveedor de Almacén. Operación y
   Contabilidad (7B y 7C) solo agregan su proveedor y su seed sobre la misma infraestructura.
4. El endpoint vive bajo `/api/v1/analytics` con `analytics.view` más el permiso del módulo pedido (Almacén:
   `inventory.view`), sin permiso nuevo.

## Decisiones a revisar (que salieron de construir y verificar)

1. **La fuente `CYCLE_COUNT` (como las demás fuentes de almacén del Lote 6) se consulta en Análisis con solo
   `analytics.view` y el módulo `ANALYTICS`**, sin `inventory.view`. Un Despachador con `analytics.view` puede
   previsualizar un reporte de `CYCLE_COUNT` y ver número, almacén, líneas con diferencia y diferencia neta de cada conteo,
   aunque `GET /api/v1/cycle-counts` le responda 403. El maestro solo declara visibles a toda la organización los
   **agregados** de Pulso (el indicador *Conteos con diferencia*), no el detalle. Precedente: Lote 4, decisión #37.
   Alternativa si se quiere cerrar: exigir en `AnalyticsService` (preview, ejecución, alta/edición de reportes)
   `PermissionCatalog.OwnerReadPermission[fuente]` cuando exista, sin tocar indicadores ni gráficos.
2. **Se creó `CycleCountDataSource` (`CYCLE_COUNT`, `DateField = ReconciledAtUtc`)** aunque el plan decía "solo seed, sin
   código nuevo": no existía. El indicador *Conteos con diferencia* filtra `StatusCode = RECONCILED` y `HasVariance`
   (alguna línea con diferencia o con ajuste enlazado), no `NetVariance ≠ 0`: sobrantes y faltantes de un mismo conteo se
   compensan y el conteo sí tuvo diferencia.
3. ***Unidades recibidas* suma RECEIPT + ADJUSTMENT `RECEIPT_VARIANCE`** del ledger, no solo RECEIPT: por la decisión D4
   del Lote 6, una línea con esperado E se asienta como RECEIPT por E más un ajuste por la diferencia, así que sumar solo
   RECEIPT daba lo esperado (10 esperados y 8 recibidos daban 10, no 8). El neto con signo de ambos es lo recibido
   (`WmsDataSourceTests.Lote7A_received_units_filter_sums_what_was_received`). El seeder corrige de forma idempotente el
   filtro de la primera versión (`ReceiptMovementsFilterV1`) si un tenant ya sembrado lo conserva intacto.
4. **`INVENTORY_ADJUSTED` cubre todo cambio manual de saldo**: un motivo manual (DAMAGE, LOSS, FOUND, EXPIRED, OTHER) con o
   sin documento de origen, y cualquier otro motivo que no asigne el sistema (p. ej. `PO_SHORTAGE` capturado a mano) sin
   documento. Consecuencia: el FOUND con que se resuelve un faltante de compra aparece dos veces, como
   `PO_SHORTAGE_RESOLVED` (opcional) y como `INVENTORY_ADJUSTED` (obligatorio). `PICK_BATCH_REVERSAL` (motivo de sistema) y
   el `PO_SHORTAGE` de un faltante (con `Ref`) no generan `INVENTORY_ADJUSTED`: ya los cuentan `PICK_CANCELLED` y
   `PO_SHORTAGE_RESOLVED`.
5. **La categoría de producto se audita con su propio `EntityType` (`PRODUCT_CATEGORY`)**, sembrado en
   `logistica-db-seed.sql`. Antes compartía `PRODUCT` y su baja escribía un `AuditLog` PRODUCT DELETE indistinguible de la
   baja de un producto con el mismo id (los `IDENTITY` coinciden): el feed mostraba `PRODUCT_DEACTIVATED` falsos. Ahora
   cada fila PRODUCT DELETE con `IsActive` es la baja de un producto real (un lote nunca genera DELETE), y el evento se
   muestra aunque el producto se haya reactivado después. Las filas de bitácora anteriores al cambio siguen ambiguas. Como
   el código ya existía en el catálogo, la ruta polimórfica (campos personalizados, contactos) lo aceptaba sin validar
   pertenencia ni permiso: ahora `PRODUCT_CATEGORY` está en `OwnerReadPermission` (`inventory.view`) y
   `OwnerWritePermission` (`inventory.manage`) y se registra con `ClosedOwnedEntityResolver` (siempre 404, sin oráculo),
   igual que las bitácoras del Lote 6. No hay campos personalizados, contactos ni historial de estatus de categorías; si se
   quisieran, se sustituye el resolver cerrado por uno sobre `db.ProductCategories`. Cubierto por
   `OwnedEntityResolverCoverageTests`.
6. **`PO_SHORTAGE_RESOLVED` se lee de `PurchaseOrderShortageResolution`, no de `AuditLog`** (el maestro decía "`AuditLog`
   UPDATE de línea de compra al resolver faltante"). Resolver un faltante no modifica la línea: inserta una fila de
   resolución (CLOSE, REORDER o MANUAL_ADJUSTMENT; Lote 6, D8), auditada bajo `PURCHASE_ORDER`
   (`PurchaseShortageService.cs:171`). Cada fila de resolución dentro de la ventana es un evento enlazado a su orden. El
   maestro se corrigió en consecuencia.
7. **Un TRANSFER con documento de origen que cruza de almacén es `INVENTORY_TRANSFERRED`**. La primera versión del
   proveedor excluía todo TRANSFER con `Ref` ("ya lo cubre el evento del documento"), pero la conciliación por serie de un
   conteo asienta un TRANSFER con `Ref = CYCLE_COUNT` desde la ubicación actual de la serie, que puede estar en otro
   almacén: el almacén de origen perdía la serie sin ningún evento. Ahora solo se excluyen los TRANSFER con `Ref` dentro
   del mismo almacén (putaway, reabasto, conteo: ya son `PUTAWAY_DONE`, `REPLENISH_DONE` y `COUNT_RECONCILED`). Prueba:
   `ActivityRulesTests.Transfers_with_a_source_document_count_only_when_they_cross_warehouses`. El maestro se corrigió.
8. **La ventana `today` empieza a la medianoche UTC, no a la del tenant** (el plan pedía "`today` en la zona del tenant").
   `Tenant` no guarda zona horaria, así que `ActivityFeedService` pasa `ActivityRules.TenantZone` (= UTC), el mismo
   criterio que `DateRangeResolver` de los indicadores. En Puerto Rico (UTC−4), entre las 20:00 y las 23:59 locales "hoy"
   ya es el día siguiente en UTC y el feed solo muestra lo ocurrido desde las 20:00 locales. La regla pura ya acepta una
   zona (`ActivityRulesTests.Today_starts_at_midnight_of_the_tenant_zone` la prueba con UTC−4, aunque el servicio todavía
   no la usa). Alternativa: columna `TimeZoneId` en `Tenant` (estructura y seed), resuelta con
   `TimeZoneInfo.FindSystemTimeZoneById` en `ActivityFeedService` y pasada a `FromUtc`.
9. **El feed aplica el `LookupCodeOverride` del tenant al catálogo `ActivityEventType`, pero el módulo y la bandera de
   obligatorio siguen siendo los de la semilla base** (maestro L54). `WarehouseActivityProvider.CatalogAsync` combina la
   etiqueta con `CustomLabelJson` (las llaves del override pisan las de la base, igual que `LookupService`), y
   `ActivityRules.ApplyOverride` decide el resto: `IsEnabled = 0` apaga un evento **opcional**; un **obligatorio** se sigue
   mostrando y sigue obligatorio aunque el override lo deshabilite o traiga `"mandatory":false` en `CustomExtraJson` (un
   tenant no puede ocultar ni degradar lo que el maestro fija como obligatorio). Del `CustomExtraJson` solo se toma
   `defaultOn` (p. ej. encender `BIN_MOVED` para todo el tenant). Consecuencia a revisar: el API de catálogos muestra el
   obligatorio "deshabilitado" y con el `mandatory` del override, mientras el feed lo sigue mostrando como obligatorio.
   Pruebas: `ActivityRulesTests.Tenant_override_disables_optionals_and_switches_default_but_never_degrades_mandatory`
   (regla pura) y `Feed_applies_the_tenant_catalog_override` (renombre en el feed, opcional deshabilitado fuera,
   obligatorio deshabilitado presente, override de otro tenant ignorado).

> Verificación: durante la revisión de las piezas P0–P2 se corrigieron **29 hallazgos** antes de este cierre (entre ellos
> las 9 decisiones documentadas arriba y ajustes de cobertura de pruebas — `WmsCatalogTests`, `WmsContractsTests`,
> `OwnedEntityResolverCoverageTests` — para `PRODUCT_CATEGORY`); no se llevó una lista aparte de los 29 por separado de
> este documento y del historial de commits (`git log`, mensajes "punto de control — correcciones de revisión").

## Lo que queda fuera de este lote (a propósito)

- **7B (Operación)** y **7C (Contabilidad)**: sin proveedor de eventos, sin permiso `billing.view`, sin indicadores propios.
  Quedan en el maestro para después; reutilizan sin cambios el catálogo, el servicio y el endpoint de este lote.
- **Frontend de 7A** (panel Almacén con filtro y panel Actividad reciente): plan listo
  (`docs/frontend/loteF7A-plan.json`), no ejecutado en este cierre. `web-app/openapi.json` no se regeneró todavía.
- **Preferencia por usuario para apagar eventos opcionales**: queda como interruptor de pantalla ("Solo obligatorios"),
  sin guardarse por usuario; se construye junto con las notificaciones, que reutilizan este catálogo.
- **Zona horaria del tenant** (`Tenant.TimeZoneId`): no se agregó columna; la ventana `today` usa UTC (decisión #8).
- **Cierre de la visibilidad de `CYCLE_COUNT` (y las demás fuentes de almacén) en Análisis** sin `inventory.view`
  (decisión #1): no se tocó `AnalyticsService` en este lote.
- **Manual funcional y FAQ**: capítulo `docs/manual/07-pulso-y-actividad.md` (sección Almacén) y entradas nuevas en
  `docs/manual/faq.md` se escriben en este mismo cierre, junto con este documento.
