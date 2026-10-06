# Rentas (submódulo de Almacén) — informe de avance

Plan: `docs/rentas-plan-de-ejecucion.md`. Una sección por bloque, en el orden en que se cierran; al final, las **decisiones por defecto a
confirmar** por el dueño (lista numerada que se acumula bloque a bloque). El "Informe final" (sección 7 del plan) se agrega al terminar todos
los bloques.

## Bloque R0 — Convertir un producto a serie (Lote 26)

### Qué se hizo

- `POST /api/v1/products/{publicId}/convert-to-serial` (`ProductsController.ConvertToSerial`, `inventory.manage`, módulo `WMS_LOTSERIAL`) y
  `ProductSerialConversionService` (exige además `inventory.adjust`): en **una transacción**, bloquea el producto y sus saldos, verifica y, por cada
  posición con existencia, asienta por el **ledger** un `ADJUSTMENT −` del saldo sin serie y un `ADJUSTMENT +1` por cada serie capturada (motivo de
  sistema `TRACKING_CONVERSION`, neto cero), y cambia el seguimiento `NONE → SERIAL` (auditado). Es la única vía para cambiar el seguimiento de un
  producto con movimientos; el `PATCH` sigue en 409 (D25).
- Mensajes del plan tal cual: 400 `Capture {n} número(s) de serie para {bin} (hay {m}).` y 409 `El producto {sku} tiene unidades reservadas;
  libérelas antes de convertirlo.`; más los nuevos de la tabla del manual (documentos abiertos 409; ya es serie, es por lote, existencia fraccionaria,
  existencia sin posición o con lote 422).
- Seed: `AdjustmentReason` `TRACKING_CONVERSION` "Conversión a serie" (solo del sistema). Contratos regenerados (`web-app/openapi.json`, `schema.d.ts`
  de la web y de la app). La web oculta el motivo en el selector de ajuste.
- Documentación: `docs/lote26-decisiones.md`, capítulo 06 §2.1 (`docs/manual/06-inventario-y-almacen.md`), FAQ (sección "Lote 26") e índice del manual.
- Archivos principales: `src/Teikem.Infrastructure/Services/ProductSerialConversionService.cs`, `src/Teikem.Domain/Wms/SerialConversionRules.cs`,
  `src/Teikem.Infrastructure/Services/ProductOpenDocuments.cs` (criterio de documentos abiertos, compartido con la baja del producto),
  `tests/Teikem.Tests/ProductSerialConversionTests.cs`, paso nuevo en `scripts/smoke.sh`.

**Verificación de la sección 3 del plan contra el código** (todo coincide, con estos matices):
- D25: el texto exacto es `No se puede cambiar el tipo de seguimiento de un producto que ya tiene movimientos.` (el plan lo abrevia).
- `InventoryLedger` es el único que escribe saldos, Kárdex y estatus/ubicación de series (`WmsWriteConfinementTests`); `SerialRules.AlreadyInStock`,
  `CycleCountRules.IsInStock` (AVAILABLE/RESERVED) y el KPI "series por capturar" de `ProductService` (AVAILABLE/RESERVED) existen como dice el plan.
- El ledger **no** valida el seguimiento del producto en los asientos (solo en las reservas): la conversión puede asentar la salida sin serie y las
  entradas con serie en el mismo lote de asientos. Dato útil para R1.
- `[RequirePermission]`: las pruebas de seguridad de WMS exigen **exactamente uno** por acción, por eso el segundo permiso va en el servicio.
- Capa 16C vieja (`RentalAsset`, `RentalContract`, `RentalAssetMaintenance`, `RentalBillingRule`, `RentalCharge`) desde la línea ~2714 del SQL y FK
  diferida a `Invoice` (~2850): confirmado. Permisos `rental.*` en `PermissionCatalog` y en la plantilla WarehouseOperator: confirmado.
- `WarehouseTaskStatusEffect` resuelve por `IServiceProvider`: confirmado.

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `dotnet test tests/Teikem.Tests -o .tmp-testout` | **3027 pasan, 0 fallan** (antes 3014: +9 `ProductSerialConversionTests`, +3 firmas en `WmsContractsTests`, +1 acción en `WmsControllerSecurityTests`) |
| `scripts/dev-sqlserver.sh` + `dotnet .tmp-testout/Teikem.Api.dll db-init` ×2 sobre la base nueva `TeikemR0Smoke` | Inicialización completada las dos veces (la segunda, rama "ya existe") |
| `scripts/smoke.sh http://localhost:5180` (con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` del build de `.tmp-testout`) | **SMOKE OK**: 134 pasos, 136 `ok`, incluido el paso nuevo "convertir a serie (Lote 26, Rentas R0)" |
| `cd web-app && npm run check` | tipos, tsc, oxlint, **vitest 1250 pasan** (124 archivos), build |
| `cd app-almacen && npm run check` | tipos, typecheck, lint, **jest 388 pasan** (69 suites) |

Nota: la primera corrida del smoke falló **antes** del paso nuevo, en una aserción vieja del Lote 25 (texto de `allowEmpty`: el servidor ya dice "a uno o
ningún producto"); se corrigió el texto en el smoke y la corrida sobre una base nueva quedó verde.

### Qué NO se probó

- Pantalla: no hay botón "Convertir a serie" todavía (F-R1); sin Playwright.
- Concurrencia real en SQL Server (dos conversiones, o un movimiento del producto durante la conversión): se usa el orden de bloqueo de `ProductService`
  (producto U → saldos HOLDLOCK → ledger), pero el smoke va en serie.
- Reversa de la transacción a mitad del ledger con SQL Server real (InMemory no es transaccional; el ledger valida todo antes de escribir).
- Productos `NONE` que ya tenían filas de serie registradas (los 409 `ya está en inventario` / `fue dada de baja` del ledger).
- Datos reales de Depot (no se tocó la base del dueño).

### Decisiones tomadas por defecto en este bloque

Ver los puntos 1 a 9 de la lista de abajo.

## Bloque R1 — Renta hasta el despacho, extensiones y cancelación (Lote 27)

### Qué se hizo

- **Esquema** (`Diseño/logistica-db-estructura.sql`, capa 16C reescrita con guardas `IF OBJECT_ID … IS NULL`): `Rental`, `RentalLine`
  (`UX_RentalLine_OpenSerial`), `RentalExtension`, `RentalLineRate`, `RentalReturn`, `RentalReturnLine`, `RentalProcess` y `RentalCharge`
  por equipo (sin mapear). El bloque es autosuficiente para una base existente: retira la capa vieja solo si está vacía (si no, `PRINT` de
  aviso), recrea `CK_NumberSequence_Kind` con `RENTAL`/`RENTALRETURN` y agrega las FKs a `Invoice`.
- **Seed**: módulo "Rentas" (Almacén, depende de `WMS_LOTSERIAL`), ZoneType `RENTAL`, SerialStatus `ON_RENT`/`IN_PROCESS`, `RentalStatus`,
  `RentalProcessStatus`, motivos y condiciones de devolución, `DAILY` y "Fija", EntityTypes `RENTAL`/`RENTAL_RETURN`/`RENTAL_PROCESS`
  (los viejos desactivados), regla lateral de CANCELLED, permisos `rental.extend` y `rental.return` (Operador de almacén y, una vez, todo
  rol de compañía con `rental.manage`). Espejo en `PermissionCatalog` (68 permisos).
- **Ledger** (plan 4.1): `ExpectedSerialStatus`, `TargetSerialStatus` y `ReserveAtDestination` en el asiento; estatus esperado y destino en
  las reservas. Zona RENTAL excluida de asignación, recolección, "solo con disponible", acomodo y recibo directo. `IsInStock` y el KPI de
  series cuentan ON_RENT e IN_PROCESS. Kárdex con referencias RENTAL/RENTAL_RETURN/RENTAL_PROCESS.
- **Servicio y API**: `RentalService` (lista con "por vencer"/"vencidas", ficha, alta, edición, equipos, tarifa, programar, despachar,
  cancelar, extender y bitácora), `RentalBinResolver` (zona RENT/posición EN-RENTA a demanda), `RentalStatusEffect`, resolvers de pertenencia
  y `RentalsController` (`/api/v1/rentals`, 12 acciones). Mensajes del plan tal cual (ver el capítulo 11).
- **Contrato** regenerado (`web-app/openapi.json` + `schema.d.ts` de la web y la app; solo adiciones: 9 rutas y 15 esquemas).
- **Documentación**: `docs/lote27-decisiones.md`, capítulo nuevo `docs/manual/11-rentas.md`, sección "Lote 27" de la FAQ e índice del manual.
- Archivos principales: `src/Teikem.Domain/Wms/Rental.cs`, `src/Teikem.Domain/Wms/RentalRules.cs`, `src/Teikem.Infrastructure/Services/RentalService.cs`,
  `RentalBinResolver.cs`, `RentalStatusEffect.cs`, `RentalOwnedEntityResolvers.cs`, `Persistence/Configurations/RentalConfigurations.cs`,
  `Contracts/RentalContracts.cs`, `src/Teikem.Api/Controllers/RentalsController.cs`, `Wms/InventoryLedger.cs`, `Wms/WmsSeams.cs`; pruebas
  `RentalRulesTests`, `RentalLedgerTests`, `RentalServiceTests`; paso nuevo en `scripts/smoke.sh`.

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `dotnet test tests/Teikem.Tests -o .tmp-testout` | **3100 pasan, 0 fallan** (antes 3029: +3 archivos nuevos de rentas y ajustes en 14 archivos de prueba existentes) |
| `scripts/dev-sqlserver.sh` + `db-init` ×2 sobre la base nueva `TeikemR1Smoke` (desde `.tmp-testout`, `ASPNETCORE_ENVIRONMENT=Development`) | Completada las dos veces; la segunda omite los scripts por hash, 68 permisos (0 nuevos), tenant demo "ya existe" |
| Consultas por `sqlcmd` a `TeikemR1Smoke` | 8 tablas `Rental*`; CHECK de contadores con RENTAL/RENTALRETURN; `rental.extend`/`rental.return` en plantillas y clones de Admin y Operador; módulo "Rentas" (Almacen, WMS_LOTSERIAL); FKs a Invoice |
| Bloque 16C sobre una base con la estructura ANTERIOR (`sqlcmd -I`), vacía y con una fila en `RentalAsset`, aplicado dos veces | Vacía: capa vieja retirada y tablas nuevas creadas. Con datos: capa vieja conservada con `AVISO (Lote 27, Rentas)…`, tablas nuevas creadas sin recrear `RentalCharge`. Reaplicar: sin errores |
| `scripts/smoke.sh http://localhost:5180` (con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` del build de `.tmp-testout`) | **SMOKE OK: 135 pasos, 137 `ok`** a la primera, incluido el paso nuevo "rentas (Lote 27, Rentas R1)" (smoke 1–4 del plan) |
| `cd web-app && npm run check` | tipos, tsc, oxlint, **vitest 1251 pasan** (124 archivos), build |
| `cd app-almacen && npm run check` | tipos, typecheck, lint, **jest 388 pasan** (69 suites) |

### Qué NO se probó

- Pantallas (F-R1); sin Playwright.
- Concurrencia real en SQL Server (dos despachos creando EN-RENTA a la vez, dos rentas tomando la misma serie): cubierta por diseño (bloqueo
  del almacén, índice único), el smoke va en serie.
- Conteo cíclico de EN-RENTA o de una serie en renta (D7): bloque R2.
- El esquema nuevo sobre la base REAL de Depot (solo se simuló con la estructura anterior).
- Una compañía que desactive estatus de la renta o de la serie (documentado como 422).

### Decisiones tomadas por defecto en este bloque

Ver los puntos 10 a 27 de la lista de abajo.

## Bloque R2 — Devolución de renta, proceso del equipo devuelto y conteo cíclico (Lote 28)

### Qué se hizo

- **Devolución de renta** (`RentalReturnService`, `POST /api/v1/rentals/{publicId}/returns`, `rental.return`): DRN-#####, motivo por
  devolución, equipos por número de serie (cada uno despachado y sin devolver de esa renta), condición por equipo, destino (por equipo, por
  encabezado o la posición de donde salió; otro almacén permitido; nunca la zona En renta), y si pasa por proceso (sí por defecto). Por el
  **ledger**, una TRANSFER por equipo desde EN-RENTA (`FromReserved`, serie esperada `ON_RENT`) con referencia `RENTAL_RETURN`: con proceso
  queda reservada e `IN_PROCESS`, sin proceso `AVAILABLE`. Abre un `RentalProcess` por equipo con proceso; con el último equipo la renta
  pasa a **RETURNED** por `StatusService.TransitionAsync`. Lista y ficha (`GET /api/v1/rental-returns[/{publicId}]`, `rental.view`).
- **Proceso configurable** (`RentalProcessService`, `/api/v1/rental-processes`): cola (`rental.view`), **Advance** a cualquier estatus
  habilitado, **Complete** a READY con traslado opcional en el almacén, **Scrap** a SCRAPPED (`rental.maintenance`; dar de baja exige además
  `inventory.adjust` en el servicio). **Efecto** `RentalProcessStatusEffect`: READY libera la reserva (IN_PROCESS → AVAILABLE); SCRAPPED hace
  `ADJUSTMENT −` con motivo DAMAGE y la serie queda SCRAPPED; depende de los terminales y resuelve el ledger con `IServiceProvider`.
- **Conteo cíclico (D7)**: EN-RENTA pedida de forma explícita → 422 `La posición {bin} es de equipos en renta; no se cuenta.`; las
  selecciones amplias la saltan; una serie en renta capturada en otra posición → 409 `La serie {s} está en renta ({n}); registre su
  devolución antes de reconciliar el conteo.` (vista previa, reconciliar y cierre en bloque).
- Bloqueo nuevo `LockRentalProcessAsync` (sentencia 19 de `InventoryQueries`); detalle del Kárdex para `RENTAL_RETURN` y `RENTAL_PROCESS`.
- **Contrato** regenerado (`web-app/openapi.json` + `schema.d.ts` de la web y la app; solo adiciones: 7 rutas y 10 esquemas).
- **Documentación**: `docs/lote28-decisiones.md`, capítulo 11 (secciones 5 Devolver, 6 Proceso, 7 Conteo cíclico; estatus, mensajes,
  Kárdex y casos frecuentes ampliados), capítulo 06 §6.y (D7), sección "Lote 28" de la FAQ e índice del manual.
- Sin cambios de esquema, seed ni permisos (el esquema de R2 lo creó R1).
- Archivos principales: `src/Teikem.Infrastructure/Services/RentalReturnService.cs`, `RentalProcessService.cs`,
  `RentalProcessStatusEffect.cs`, `CycleCountService.cs`, `InventoryReadService.cs`, `Wms/InventoryQueries.cs`,
  `src/Teikem.Domain/Wms/RentalRules.cs`, `Contracts/RentalContracts.cs`, `src/Teikem.Api/Controllers/RentalReturnsController.cs`,
  `RentalProcessesController.cs`; pruebas `RentalReturnServiceTests`, `RentalProcessEffectTests`, `RentalReturnWorld`; paso nuevo en
  `scripts/smoke.sh`.

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `dotnet test tests/Teikem.Tests -o .tmp-testout` | **3133 pasan, 0 fallan** (antes 3104 en `9457d00`: +29 — 4 `RentalReturnServiceTests`, 3 `RentalProcessEffectTests`, 1 de D7 en `CycleCountServiceTests`, 1 de resolvers/servicios, 1 de propagación de permisos, 12 firmas de contratos y 7 acciones de seguridad; `WmsControllerSecurityTests` en 151 acciones, `RawSqlConfinementTests` en 19 sentencias) |
| `scripts/dev-sqlserver.sh` (ya corriendo) + `db-init` ×2 sobre la base nueva `TeikemR2Smoke` (desde `.tmp-testout`, `ASPNETCORE_ENVIRONMENT=Development`) | Completada las dos veces; la segunda omite los scripts por hash, 68 permisos (0 nuevos), tenant demo "ya existe". Se borraron `TeikemR0Smoke` y `TeikemR1Smoke` |
| `scripts/smoke.sh http://localhost:5180` (con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` del build de `.tmp-testout`) | **SMOKE OK: 136 pasos, 138 `ok`** a la primera, incluido el paso nuevo "devolución y proceso de rentas (Lote 28, Rentas R2)" (D7 y smoke 5–7 del plan, más la baja con SQL Server real) |
| Consultas por `sqlcmd` a `TeikemR2Smoke` | 3 `RentalReturn`, 3 `RentalReturnLine`, 2 `RentalProcess` (READY y SCRAPPED, con `CompletedAtUtc`) |
| `cd web-app && npm run check` | tipos, tsc, oxlint, **vitest 1252 pasan** (124 archivos), build |
| `cd app-almacen && npm run check` | tipos, typecheck, lint, **jest 410 pasan** (72 suites) |

### Qué NO se probó

- Pantallas de devoluciones y procesos (F-R2); sin Playwright.
- Concurrencia real en SQL Server (dos devoluciones del mismo equipo, dos usuarios con el mismo proceso): cubierta por diseño (bloqueo de
  la renta y del proceso, `UQ_RentalReturnLine_Line`, RowVersion), el smoke va en serie.
- Una compañía que desactive `READY`, `SCRAPPED`, `RETURNED` o el estatus inicial del proceso (documentado: 422).
- "Lo cambiado" con la zona RENT pedida de forma explícita (mismo código que el alta; sin prueba propia).
- Datos reales de Depot.

### Decisiones tomadas por defecto en este bloque

Ver los puntos 28 a 40 de la lista de abajo.

## Bloque R3 — Reportes, indicadores y aviso de rentas vencidas o por vencer (Lote 29)

### Qué se hizo

- **Fuentes de datos** de Análisis (`Analytics/RentalDataSources.cs`, registradas en `DependencyInjection.cs` por su código EntityType):
  `RENTAL` (DateField `StartDate`; recogido vigente y pactado, `DaysToPickup`, `IsOverdue` e `IsOpen` con el día de la compañía
  —`ITenantClock`—, equipos, equipos en el cliente y devueltos, extensiones, cliente, localidad, almacén, estatus, contrato, transporte),
  `RENTAL_RETURN` (DateField `ReturnedOn`; motivo, condición resumida de sus equipos, `IsEarly` y días de anticipación, equipos por
  condición, procesos) y `RENTAL_PROCESS` (DateField `StartedAtUtc`; serie, producto, posición, estatus, `IsOpen`, devolución, renta,
  cliente, condición al volver, días en proceso). Filtro por compañía (filtro global), lectura con `rental.view` (ya estaba en
  `OwnerReadPermission`) y **módulo Rentas**: miembro nuevo `IDataSource.TenantModule` (por defecto `null`) que `AnalyticsService` exige
  encendido; con Rentas apagado no se listan las fuentes ni sus vistas, indicadores y gráficos (404 al leerlos).
- **Aviso "Necesita tu atención"** `RentalDueAttentionProvider` (código `RENTAL_DUE`, `RENTAL_EQUIPMENT` + `rental.view`): una fila por
  renta abierta vencida o que vence en ≤ 7 días, las más vencidas primero, rojo/ámbar, con "Revisar" y "Ver todos" hacia
  `/warehouse/rentals` (pantalla de F-R1).
- **Contenido de sistema** (`SystemAnalyticsSeeder`, idempotente por nombre; nombres y filtros en `Domain/Wms/RentalAnalyticsRules.cs`):
  indicadores "Rentas por vencer (7 días)" y "Rentas vencidas", gráfico "Devoluciones de renta por motivo" y las vistas "Equipos en renta
  por cliente", "Rentas por vencer (7 días)", "Rentas vencidas", "Devoluciones de renta por motivo" y "Equipos en proceso". Bloque "Lote
  29" en `Diseño/logistica-db-seed.sql` con el mismo contenido para las compañías ya creadas (Depot, Solutions), como el del Lote 15.
- Sin cambios de esquema, permisos, endpoints ni contrato (`swagger.json` idéntico a `web-app/openapi.json`): no se tocó la web ni la app.
- **Documentación**: `docs/lote29-decisiones.md`, capítulo 11 sección 10 (fuentes, vistas, indicadores, gráfico, aviso y mensajes; las
  secciones Kárdex y Casos frecuentes pasan a ser 11 y 12, con casos nuevos), sección "Lote 29" de la FAQ e índice del manual.
- Archivos principales: `src/Teikem.Infrastructure/Analytics/RentalDataSources.cs`, `Analytics/DataSources.cs`,
  `Services/Attention/RentalDueAttentionProvider.cs`, `Services/AnalyticsService.cs`, `Seeding/SystemAnalyticsSeeder.cs`,
  `src/Teikem.Domain/Wms/RentalAnalyticsRules.cs`; pruebas `RentalAnalyticsTests`, ampliadas `AnalyticsSeedFieldsTests`,
  `OwnedEntityResolverCoverageTests` y `RentalReturnWorld`; paso nuevo en `scripts/smoke.sh`.

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `dotnet test tests/Teikem.Tests -o .tmp-testout` | **3145 pasan, 0 fallan** (antes 3133 en `0656645`: +12 — 7 `RentalAnalyticsTests`, 4 en `AnalyticsSeedFieldsTests`, 1 en `OwnedEntityResolverCoverageTests`). Sin cambios en `WmsControllerSecurityTests` (151 acciones: no hay endpoints nuevos) ni en `RawSqlConfinementTests` (19 sentencias: sin SQL crudo) |
| `scripts/dev-sqlserver.sh` (ya corriendo) + `db-init` ×2 sobre la base nueva `TeikemR3Smoke` (desde `.tmp-testout`, `ASPNETCORE_ENVIRONMENT=Development`) | Completada las dos veces; la segunda omite los scripts por hash, 68 permisos (0 nuevos), tenant demo "ya existe" y contenido de análisis verificado. Se borró `TeikemR2Smoke` (tenía sesiones huérfanas: `SINGLE_USER WITH ROLLBACK IMMEDIATE`) |
| `scripts/smoke.sh http://localhost:5180` (con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` del build de `.tmp-testout`) | **SMOKE OK: 137 pasos, 139 `ok`** a la primera, incluido el paso nuevo "reportes, indicadores y avisos de rentas (Lote 29, Rentas R3)": el aviso aparece con una renta vencida y una que vence en 3 días, indicadores 1/1, las 5 vistas y el gráfico con datos reales, otra compañía sin nada, módulo apagado sin fuentes/indicadores (404)/aviso y de vuelta al encenderlo |
| Bloque SQL del Lote 29 con `sqlcmd -I` sobre `TeikemR3Smoke`, con el contenido de rentas borrado de la compañía 1 (simula una compañía anterior al lote), aplicado dos veces | Primera vez: 2 indicadores, 1 gráfico y 5 vistas; segunda: 0 filas (la compañía 2 no duplicó). Leído por el API: indicadores 1 y 1, vista por cliente y gráfico por motivo con los 3 motivos |
| Contrato: `GET /swagger/v1/swagger.json` del API nuevo contra `web-app/openapi.json` | Idénticos (no se regeneraron tipos; `npm run check` no hacía falta: ni la web ni la app cambiaron) |

### Qué NO se probó

- Pantallas (F-R1/F-R2): la web aún no tiene `/warehouse/rentals` ni el texto del aviso `RENTAL_DUE` (el panel lo muestra con su código);
  sin Playwright.
- El bloque SQL del Lote 29 sobre la base REAL de Depot (solo la simulación descrita arriba).
- Rendimiento con muchas rentas reales (tope de 20 000 filas por fuente, consultas por lote).
- Una compañía con una zona horaria distinta de Puerto Rico en SQL Server (las pruebas cubren el día local con relojes fijos en InMemory).

### Decisiones tomadas por defecto en este bloque

Ver los puntos 41 a 51 de la lista de abajo.

## Bloque RM — Reimportación de Depot con seguimiento SERIAL (D5-b) (Lote 30)

### Qué se hizo

- **Investigación** (documento para el dueño: `docs/migracion/depot-series-y-rentas.md`): **ninguna fuente de la migración trae números de
  serie**. `Depot Products.csv` solo tiene las casillas `Serial`/`Lot` por ítem (análisis 2026-09-28: `Lot` = TRUE en 500 de 576 y `Serial`
  = TRUE en 3, "parecen valores por defecto"); el MSWM de Depot no tiene series (su lote es `§` o `0`) y el importador no lee tablas de
  series. Antes de RM el importador ni leía las casillas: `products.trackingType` (NONE) valía para todo. **Riesgo latente encontrado**: con
  `trackingType: SERIAL` se habrían creado productos SERIAL con saldo inicial sin series (el ledger no exige series en los asientos).
- **Cambio chico** (`docs/lote30-decisiones.md`): opción `products.trackingFromColumns` `{ serial, lot }` (apagada por defecto) que usa las
  casillas de QuickBooks para **crear** productos con SERIAL o LOT; un producto que pediría SERIAL/LOT y **tiene saldo inicial se crea NONE**
  con su saldo (también si viene de `trackingType`) y el reporte lo lista como **"Candidato a Convertir a serie"**; un producto **existente
  nunca cambia** de seguimiento (advertencia y candidato); `products.trackingType` se valida al leer el JSON. Los JSON de Depot, Solutions y
  la muestra **no cambian** (NONE, opción apagada).
- **Lo que queda para R0 ("Convertir a serie")**: toda serie de un equipo que ya está en existencia (todo Depot hoy), y los equipos de renta
  que QuickBooks no marca como `Serial`. Recomendación: no recrear la base de Depot para esto.
- Documentación: `docs/migracion/depot-series-y-rentas.md` (evaluación y procedimiento paso a paso), `docs/lote30-decisiones.md`, capítulo 10
  §6 del manual (opción, reglas y mensajes exactos), nota en el capítulo 11 (D5), sección "Lote 30" de la FAQ, índice del manual y
  `docs/migracion/README.md`.
- Archivos de código: `src/Teikem.Infrastructure/Migration/QuickBooksCsvReader.cs`, `LegacyImportConfig.cs`, `LegacyImportService.cs`,
  `src/Teikem.Domain/Migration/LegacyImportRules.cs`; prueba nueva `tests/Teikem.Tests/LegacyImportTrackingTests.cs`. Sin cambios de
  esquema, seed, permisos, endpoints ni contrato.

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `dotnet test tests/Teikem.Tests -o .tmp-testout` | **3172 pasan, 0 fallan** (antes 3145 en `caf41ce`: +27, todas en `LegacyImportTrackingTests`). `RawSqlConfinementTests` (19) y `WmsControllerSecurityTests` (151) sin cambios; la confinación de `TrackingTypeLookupId` sigue verde |
| `db-init` ×2 sobre la base nueva `TeikemRMSmoke` (desde `.tmp-testout`, `ASPNETCORE_ENVIRONMENT=Development`) | Completada las dos veces; la segunda omite los scripts por hash, 68 permisos (0 nuevos), tenant demo "ya existe" |
| `import-legacy` (build de `.tmp-testout`) de una copia de la muestra con 3 ítems agregados y `trackingFromColumns.serial` encendido: `--dry-run`, carga real y `--update` sobre `TeikemRMSmoke` | Código 0 las tres. Carga real: 8 productos creados, 0 rechazos, 5 asientos de saldo inicial, conciliación sin diferencias; advertencia y "Candidato a Convertir a serie" para `EQ-RM1` (`3 unidades en 1 posición(es); se crea con NONE`), "Seguimiento" `EQ-RM2` → `SERIAL`. `--update`: `EQ-RM1` sigue NONE con la advertencia "ya existe con seguimiento NONE…" |
| Consultas por `sqlcmd` a `TeikemRMSmoke` | `EQ-RM1` NONE con 3 en mano y 0 series; `EQ-RM2` SERIAL sin existencia; `EQ-RM3` (solo Lot, columna Lot apagada) NONE; **0 productos SERIAL con más unidades en mano que series** |
| `scripts/smoke.sh http://localhost:5180` (API de `.tmp-testout` sobre `TeikemRMSmoke`, `SMOKE_SQL`, `SMOKE_MIGRATION_RUN`) | **No completado**: se corrió a las 00:1x UTC y se detuvo en el paso del Lote 2 "tarifas por servicio" (21 `ok` antes), ajeno a RM: el smoke calcula "ayer" con `date -u -d yesterday` y entre las 00:00 y las 04:00 UTC esa fecha todavía es "hoy" en Puerto Rico, así que el 400 esperado no ocurre. No se repitió después de las 04:00 UTC (el bloque se cerró antes). El paso de migración del smoke solo hace dry-run de la muestra (sin la opción nueva, comportamiento idéntico); la carga real con la opción se probó a mano (filas de arriba) |

### Qué NO se probó

- Datos reales de Depot: no se tocó la base `Teikem` del dueño; el `Depot Products.csv` real y el `MSWM.sql` no están en el repositorio. No
  se sabe cuáles son los 3 ítems marcados `Serial` ni si tienen existencia (los lista el dry-run del procedimiento, sección 5.1).
- Que MSWM no tenga ninguna tabla de series: se apoya en el análisis documentado; la consulta de verificación de solo lectura queda en el
  documento de Depot.
- `scripts\recrear-base.ps1` con la opción encendida (Windows).
- La conversión R0 de un producto importado concreto (el paso R0 del smoke la cubre con un producto creado por el API).

### Decisiones tomadas por defecto en este bloque

Ver los puntos 52 a 60 de la lista de abajo.

## Bloque F-R1 — Web de rentas y "Convertir a serie" (Lote F17)

### Qué se hizo

- **Almacén → Rentas** (`/warehouse/rentals`, `rental.view` + módulo `RENTAL_EQUIPMENT`), antes del Kárdex. **Lista** con filtros al API
  (estatus, cliente, "vencen en N días", "solo vencidas", buscador libre), columnas ordenables, insignia de estatus con color y texto,
  **vencimiento calculado** (no es estatus), paginación y Exportar; los filtros se leen de la URL (así llega "Ver todos" del aviso) y
  `?rental=` abre la ficha ("Revisar" del aviso `RENTAL_DUE`).
- **Ficha** (`/warehouse/rentals/{renta}`): datos (cliente, localidad, contacto, almacén, inicio, recogido vigente y pactado, contrato,
  transporte y moneda, envío y factura vacíos, notas), **equipos por serie** con posición de origen, tarifa vigente y estado (inactivos
  atenuados en una cancelada), **extensiones** e **historial de estatus**. Acciones según el estatus y el permiso, con confirmación y el
  mensaje exacto del servidor: Editar, Agregar equipos, Tarifa y Quitar equipo, **Programar**, **Despachar**, **Cancelar** (`rental.manage`)
  y **Extender** (`rental.extend`: nueva fecha, motivo y tarifa opcional; el 400 de fecha y el de motivo bajo su campo).
- **Alta en Borrador y edición** con el **selector de equipos por serie** (producto propio → series disponibles del almacén de origen en
  zonas que se rentan, sin repetir; buscador y lector de código de barras; los 409 del servidor tal cual) y la **tarifa por equipo** (fija o
  por día/semana/mes, monto, moneda).
- **Convertir a serie** en la ficha del producto (`inventory.manage` + `inventory.adjust`, WMS_LOTSERIAL, producto sin seguimiento con
  existencia): una caja por posición (pegar varias líneas, contador, repetidas y conteo con el mensaje exacto 400; bloqueos 409/422 al
  abrir) y confirmación de **movimiento neto cero** con el motivo "Conversión a serie".
- Texto del aviso **`RENTAL_DUE`** en "Necesita tu atención" (vencida / se recoge hoy / vence en N días). `isPickableZone` de la web excluye
  la zona `RENTAL` (paridad con el servidor desde R1).
- Sin cambios en el servidor ni en el contrato (`schema.d.ts` igual). Documentación: `docs/frontend/loteF17-decisiones.md`, capítulo de
  pantallas `docs/manual/frontend/f17-rentas.md` con capturas `f17-*`, sección "Lote F17" de la FAQ, índice del manual, referencias en los
  capítulos 06 y 11, `web-app/KIT.md`.
- Archivos principales: `web-app/src/features/rentals/` (`RentalListScreen`, `RentalDetailScreen`, `RentalFormModal`, `RentalDialogs`,
  `EquipmentPicker`, `RateFields`, `DueChip`, `rentalRules.ts`, `api.ts`), `web-app/src/features/warehouse/ConvertToSerialModal.tsx`,
  `serialConversion.ts`, `ProductDetailScreen.tsx`, `features/analytics/attention.ts`, `app/routes.tsx`, `e2e/loteF17.spec.ts`,
  `playwright.config.ts`.

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `npm run check` en `web-app/` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: `schema.d.ts` sin cambios, tsc, oxlint (9 avisos que ya existían; ninguno en archivos de este lote), vitest **129 archivos / 1297 pruebas** (antes 124 / 1252: +5 archivos y +45 pruebas: `rentalRules.test.ts` 15, `serialConversion.test.ts` 7, `RentalScreens.test.tsx` 13, `ConvertToSerial.test.tsx` 5, `attentionRental.test.ts` 3, +1 en `collectForm.test.ts` y +1 en `navigation.test.ts`), build |
| `dotnet test tests/Teikem.Tests -o .tmp-testout` (servidor sin cambios, para el API del recorrido) | **3172 pasan, 0 fallan** |
| `db-init` ×2 sobre la base nueva `TeikemF17` (desde `.tmp-testout`, `ASPNETCORE_ENVIRONMENT=Development`) | Completada las dos veces (la segunda, "ya existe") |
| `scripts/smoke.sh http://localhost:5000` (con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` del build de `.tmp-testout`) a las 01:26 UTC | **FALLÓ en el paso 20** ("tarifas por servicio (Lote 2)", 21 `ok` antes): el smoke calcula "ayer" en UTC y entre las 00:00 y las 04:00 UTC ese día es "hoy" en Puerto Rico para el API (falla previa, independiente de este lote; ver decisión 72 del informe). No se repitió fuera de esa ventana: el orquestador pidió cerrar antes |
| `npx playwright test e2e/loteF17.spec.ts --project=escritorio-f17 --no-deps --workers=1` y lo mismo con `--project=movil-f17` (API real en :5000 sobre `TeikemF17`, Vite con `VITE_API_URL=http://localhost:5000`, Chromium de `/opt/pw-browsers`) | **escritorio 1 pasó, móvil 1 pasó** (25.6 s y 19.7 s; cada uno con 1 omitido por proyecto) |
| Suite completa `npx playwright test --workers=1` sobre esa misma base (smoke incompleto y con las rentas de F17 ya creadas) | **46 pasaron, 3 fallaron, 58 omitidos por proyecto, 64 no corrieron** (por las dependencias de los fallidos): `lote14` 7 (espera "Todo en orden" en "Necesita tu atención", pero había rentas por vencer: las de F17 de corridas anteriores; en el CI también las deja el paso R3 del smoke, ver "No verificado"), `lote15` 6 (gráfico "Valor de inventario por categoría" sin total: depende de los datos del smoke, ya documentado en F15) y `lote15` 9 móvil (franja fija; sin investigar). No es la corrida en el orden del CI |

Recorrido `e2e/loteF17.spec.ts` (escritorio y móvil, cada uno con sus datos): siembra por API un cliente con su localidad y un producto sin
seguimiento con 2 + 1 unidades en dos posiciones RSV de ALM-01; **Convertir a serie** (repetida sin distinguir mayúsculas y conteo con el
mensaje exacto, confirmación de neto cero, aviso final; por API queda SERIAL con 3 en mano y 3 disponibles); Almacén → **Rentas** → **Nueva
renta** (cliente, localidad, almacén, fechas, contrato, transporte; equipo por SKU, una serie por casilla y otra escaneada con Enter, tarifa
mensual; escanear una ya elegida avisa) → ficha REN-##### en Borrador → **Programar** con comentario (por API: en mano 3, disponible 1) →
**Despachar** (estatus En renta, sin "Cancelar renta"; por API las dos series ON_RENT en EN-RENTA) → **Extender** (el 400 de fecha
"…posterior a la actual (aaaa-mm-dd)." y el de motivo, luego bien con tarifa nueva; aparece en Extensiones y "Vence en 5 días") → lista
con `?dueWithinDays=7&overdue=true` (filtros leídos, la renta con "Vence en 5 días" y "En renta") → Pulso: "Necesita tu atención" con
"Renta REN-…: vence en 5 días" y "Revisar" abre la ficha. En móvil, sin scroll horizontal en cada paso. Capturas `f17-*` (las de otros
lotes que regeneró la corrida se descartaron con `git checkout --`).

### Qué NO se probó

- Devoluciones y procesos (F-R2).
- El alta con un usuario **Operador de almacén**: no trae `clients.read` ni `locations.read` (ver decisión 70); solo se probó el aviso en
  vitest.
- Recorrido en inglés, lectores de pantalla y concurrencia de dos usuarios sobre la misma renta.
- **La suite completa de Playwright en el orden del CI** con el smoke completo (el smoke falló por la ventana de 00:00–04:00 UTC, decisión 72,
  y se pidió cerrar). Riesgo: `lote14.spec.ts` 7 ("Todo en orden" en "Necesita tu atención") probablemente falla en el CI desde R3, porque el
  smoke deja dos rentas por vencer; no se cambió esa prueba.
- CI de GitHub Actions (no se hizo push).

### Decisiones tomadas por defecto en este bloque

Ver los puntos 61 a 72 de la lista de abajo.

## Bloque F-R2 — Web de devoluciones, proceso de equipos y reportes de rentas (Lote F18)

### Qué se hizo

- **Registrar devolución** desde la ficha de una renta En renta (`rental.return`): fecha (hoy de la compañía, no futura ni anterior al
  inicio), motivo del catálogo (con "Otro" las notas son obligatorias), notas, costo de recogido y moneda, destino común (almacén + posición;
  vacío = la posición de origen de cada equipo; nunca la zona En renta; puede ser otro almacén) y, por equipo pendiente, incluirlo
  (devolución parcial), condición (Buena por defecto), posición propia, "¿Pasa por proceso?" (sí por defecto) y notas. Validación previa con
  el texto exacto del servidor; los errores del servidor salen tal cual.
- Almacén → **Devoluciones de renta** (lista con filtros motivo, cliente, renta, fechas, anticipada y buscador; columnas ordenables;
  Exportar) y su **ficha** (renta de origen, equipos con condición, destino y proceso); enlaces cruzados renta ↔ devolución (panel
  Devoluciones en la ficha de la renta).
- Almacén → **Proceso de equipos**: cola con estatus de la compañía (etiquetas y colores del catálogo), días en proceso, filtros
  (estatus, abiertos/terminados, almacén, buscador) y acciones **Avanzar** (el motor decide; el 422 sale tal cual), **Completar** (Lista,
  traslado opcional en el almacén) y **Dar de baja** (además `inventory.adjust`; oculta sin él, con nota; confirmación escribiendo la serie),
  más Historial.
- **Resumen** en la lista de rentas (En renta hoy, Por vencer 7 días, Vencidas, con el API de rentas) y **Reportes de rentas**: indicadores,
  gráfico y las 5 vistas de sistema de R3 (y las de la compañía sobre esas fuentes) leídos del motor de Análisis, sin un segundo motor
  (`ReportResultTable` genérico). El Pulso no cambió (los indicadores siguen apagados; el aviso `RENTAL_DUE` ya funcionaba).
- Pestañas del submódulo (Rentas · Devoluciones · Proceso de equipos · Reportes) y dos ítems de menú nuevos después de Rentas (el Kárdex
  sigue último).
- **Correcciones del CI (commit aparte)**: `scripts/smoke.sh` calcula los días de calendario en la hora de Puerto Rico (`PRDAY`; los filtros
  de órdenes y recolecciones, que el servidor lee en UTC, siguen con días UTC); el paso 7 de `e2e/lote14.spec.ts` ya no depende de que
  "Necesita tu atención" esté vacío (sigue exigiendo cero descuadres y compara el contador con el API).
- Sin cambios en el servidor ni en el contrato (`schema.d.ts` igual). Documentación: `docs/frontend/loteF18-decisiones.md`, capítulo de
  pantallas `docs/manual/frontend/f18-devoluciones-y-proceso-de-rentas.md` con capturas `f18-*`, sección "Lote F18" de la FAQ, índice del
  manual, referencias en los capítulos 11 y F17, `web-app/KIT.md`.
- Archivos principales: `web-app/src/features/rentals/` (`RentalReturnModal`, `RentalReturnListScreen`, `RentalReturnDetailScreen`,
  `RentalProcessListScreen`, `ProcessDialogs`, `RentalReportsScreen`, `RentalTabs`, `returnRules.ts`, `api.ts`, `RentalListScreen`,
  `RentalDetailScreen`), `web-app/src/features/analytics/ReportResultTable.tsx` y `reportResult.ts`, `app/routes.tsx`, `e2e/loteF18.spec.ts`,
  `playwright.config.ts`; `scripts/smoke.sh`, `e2e/lote14.spec.ts`.

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `dotnet test tests/Teikem.Tests -o .tmp-testout` (servidor sin cambios, para el API de las pruebas) | **3172 pasan, 0 fallan** |
| `scripts/dev-sqlserver.sh` (ya corriendo) + base **nueva** `TeikemF18` + `db-init` ×2 (desde `.tmp-testout`, `ASPNETCORE_ENVIRONMENT=Development`, `Auth__Onboarding__Enabled=false`) | Completada las dos veces |
| `scripts/smoke.sh http://localhost:5000` con el arreglo `PRDAY` (con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` del build de `.tmp-testout`), **dentro de la ventana 00:00–04:00 UTC** | Con el arreglo a medias (solo `TODAY`/`YESTERDAY`), la primera corrida (02:40 UTC) ya pasó "tarifas por servicio (Lote 2)" pero falló en "órdenes (Lote 3)", un filtro que el servidor lee en UTC (se dejó en días UTC, igual que el de recolecciones); con el arreglo completo, **SMOKE OK: 137 pasos, 139 `ok`** dos veces sobre bases nuevas (02:45–02:48 y 03:04–03:07 UTC) |
| `cd web-app && npm run check` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: `schema.d.ts` sin cambios, tsc, oxlint (9 avisos que ya existían; ninguno en archivos de este lote), vitest **132 archivos / 1323 pruebas** (antes 129 / 1297: +3 archivos y +26 pruebas: `returnRules.test.ts` 12, `reportResult.test.ts` 3, `RentalReturnScreens.test.tsx` 10 y +1 en `navigation.test.ts`; se ajustaron `RentalScreens.test.tsx` y `navigation.test.ts` por el resumen y los ítems nuevos), build |
| `npx playwright test e2e/loteF18.spec.ts --project=escritorio-f18 --no-deps --workers=1` y lo mismo con `--project=movil-f18` (API real sobre `TeikemF18`, Vite con `VITE_API_URL=http://localhost:5000`, Chromium de `/opt/pw-browsers`) | **escritorio 1 pasó (25.8 s), móvil 1 pasó (24.4 s)** |
| **Suite completa en el orden del CI**: base nueva → `db-init` ×2 → smoke OK → `npx playwright test` (todos los proyectos, workers por defecto) | Tres corridas sobre bases nuevas (smoke OK las tres). **1.ª y 2.ª: 50 pasaron, 1 falló, 58 omitidos por proyecto, 66 no corrieron**: falla `lote15.spec.ts` paso 9 móvil (*"franja compacta 2×2, fija al desplazar"*: espera la franja "Almacén hoy" fija al desplazar, pero desde el commit `bdc892e` "la franja Almacén hoy ya no se fija" —y `3b50351`, encabezado completo fijo— eso cambió a propósito y la prueba no se actualizó; falla igual sola con `--no-deps`; ajena a Rentas) y, por las dependencias de `movil`, no corren F8a, Lote 16, F12–F18, F9 ni F11. `lote14` paso 7 (corregido) **pasó**; `lote15` paso 6 también (con el smoke completo). **3.ª, con ese paso marcado `test.fixme` solo para la corrida (no se comiteó; `git checkout` después): 90 pasaron, 0 fallaron, 85 omitidos por proyecto** (6.3 min), incluidos `escritorio-f18` (28.9 s) y `movil-f18` (29.3 s). `--grep-invert` no excluyó el paso en Playwright 1.63 (se comprobó con `--list`) |

### Qué NO se probó

- El recorrido con un **Operador de almacén** (solo administrador; las variantes de permiso —sin `rental.return`, sin `inventory.adjust`,
  sin `analytics.view`— están en vitest).
- Recorrido en inglés, lectores de pantalla, concurrencia de dos usuarios sobre la misma renta o proceso y una compañía que renombre o
  desactive pasos del proceso.
- CI de GitHub Actions (no se hizo push). **Riesgo para el CI**: `lote15.spec.ts` paso 9 (móvil) falla por un cambio de diseño anterior a
  Rentas (decisión 87) y, mientras falle, el job no corre los proyectos que dependen de `movil` (entre ellos F17 y F18).

### Decisiones tomadas por defecto en este bloque

Ver los puntos 73 a 87 de la lista de abajo.

## Decisiones por defecto a confirmar

1. **(R0) Segundo permiso en el servicio.** El endpoint pide `inventory.manage` y el servicio exige `inventory.adjust` (403 `Falta el permiso
   'inventory.adjust'.`). Alternativa: dos atributos en el controlador (rompe la regla de "uno por acción" de las pruebas de seguridad). Consecuencia: igual
   para el usuario.
2. **(R0) Lectura del mensaje del plan** `Capture {n} número(s) de serie para {bin} (hay {m}).`: `{n}` = unidades en mano en la posición, `{m}` = series
   capturadas. Alternativa: `{n}` = las que faltan y `{m}` = las unidades. Consecuencia: solo el texto que ve el usuario.
3. **(R0) Conversión de todo el producto en una solicitud**: hay que capturar las series de **todas** las posiciones con existencia a la vez (el seguimiento es del
   producto). Alternativa: convertir posición por posición (dejaría el producto con existencia sin serie mientras tanto). Consecuencia: con muchos equipos repartidos, la
   captura es una sola y grande.
4. **(R0) Solo `NONE` → `SERIAL`**; un producto por lote no se convierte (422). Alternativa: permitir `LOT → SERIAL` conservando el lote en cada serie.
5. **(R0) Bloqueo por documentos abiertos** (recibos, tareas, recolecciones eliminables, conteos abiertos; 409, mismo criterio que la baja del producto), además del
   bloqueo por reservas del plan. Alternativa: permitirlo (esos documentos moverían el producto sin series después de convertido).
6. **(R0) Existencia no convertible → 422**: fraccionaria, sin posición o con lote. Alternativa: redondear o asignar una posición por defecto (no recomendado).
7. **(R0) Producto inactivo**: se permite convertirlo (solo cambia el seguimiento: por la regla de baja no tiene existencia). Alternativa: exigir que esté activo.
8. **(R0) Movimientos sin documento de referencia** y nota por defecto "Conversión a serie". Alternativa: referenciar al producto (el Kárdex mostraría `PRODUCT·id`).
9. **(R0) Reporte de ajustes de la web**: los ajustes de la conversión se **incluyen** (neto 0). Alternativa: excluirlos como el saldo inicial de la migración.
10. **(R1, plan §2) Una renta = un cliente, una localidad, varios equipos**, y **solo equipos propios con serie** (una línea por serie).
    Alternativa: varias localidades por renta o equipos por cantidad. Consecuencia: para dos localidades se hacen dos rentas.
11. **(R1, plan §2) Cancelar con `rental.manage` y solo antes del despacho** (Borrador o Programada); después se termina con la devolución.
    Alternativa: permiso propio de cancelación o cancelar también En renta (habría que devolver el equipo igual).
12. **(R1, plan §2) "Programada" reserva las series**; "Borrador" no reserva. Alternativa: reservar desde el Borrador (el equipo dejaría de
    estar disponible desde el primer borrador).
13. **(R1, plan §2) Tarifa opcional para programar y despachar**. Alternativa: exigir tarifa a cada equipo antes de programar.
14. **(R1, plan §2) "Vencida" es dato calculado, no estatus**, e incluye las rentas **Programadas** además de las En renta (abiertas). Alternativa:
    solo En renta. Consecuencia: una renta programada que no salió a tiempo aparece como vencida.
15. **(R1, plan §2) Una posición EN-RENTA por almacén** (zona RENT, creada a demanda en el primer despacho). Alternativa: una posición por
    cliente o por renta (más posiciones; reportes por posición).
16. **(R1, plan §2) Permiso nuevo `rental.return`** sembrado ya (se usa en R2) y **`rental.extend`** para extender (D4). Ambos en la plantilla
    del Operador de almacén y, una sola vez, en todo rol de compañía con `rental.manage`. Alternativa: dárselos solo a los clones de plantilla.
17. **(R1, plan §2) El enlace a la factura va en el encabezado** (`Rental.InvoiceId`, con FK) y el del envío también (`DeliveryShipmentId`, sin
    FK). Para cobro por tiempo habrá varias facturas: irán en `RentalCharge` (por equipo, sin mapear).
18. **(R1) Cliente dado de baja = 409** con el mensaje reutilizado (el plan decía 422; en toda la plataforma ese mensaje es 409). También bloquea
    programar y despachar.
19. **(R1) Despachar exige Programada** (422 desde Borrador; no hay "programar y despachar" en un paso).
20. **(R1) Tarifa**: antes del despacho se corrige la vigente en su lugar; una extensión con tarifa distinta cierra la vigente y abre otra
    **desde el día siguiente al recogido anterior** (`EffectiveTo` exclusivo, como `RateComponent`); si la tarifa no cambia no hay versión nueva.
    La moneda por defecto es la de la compañía.
21. **(R1) Fechas**: sin extensiones el PATCH mueve inicio y recogido (la fecha pactada sigue a la vigente); con extensiones, solo otra
    extensión mueve la fecha (422). Alternativa: permitir acortar la renta con una "extensión" hacia atrás.
22. **(R1) El cliente de la renta no se cambia** (400; cancelar y crear otra) y el almacén de origen solo sin equipos (409).
23. **(R1) Serie elegible** = AVAILABLE en una posición recolectable (no cuarentena, cruce de muelle ni En renta) del almacén de origen; al
    programar se reserva donde esté en ese momento. Alternativa: permitir rentar desde cuarentena.
24. **(R1) Renta cancelada**: sus equipos quedan inactivos (la serie se libera para otra renta) y la ficha los muestra inactivos.
25. **(R1) Códigos RENT/EN-RENTA ocupados → 409** (hay que renombrar la zona o posición existente); una EN-RENTA desactivada a mano no se
    reactiva sola (422 al despachar).
26. **(R1) Módulo "Rentas" depende de Inventario y trazabilidad**: apagar `WMS_LOTSERIAL` apaga Rentas en cascada y volver a encenderlo no
    reenciende Rentas. Los estatus de la renta se pueden renombrar/reordenar pero **no** desactivar (422).
27. **(R1) Esquema de R2 creado ya** (`RentalReturn`, `RentalReturnLine`, `RentalProcess` mapeados, sin servicio) con una columna extra no
    listada en el plan: `RentalReturn.TransportCurrencyLookupId` (moneda del costo de recogido). La capa vieja se retira **solo si está vacía**.
28. **(R2, plan §2) Quién decide si un equipo pasa por proceso: el receptor, equipo por equipo, "sí" por defecto.** Alternativa: por
    producto o por condición (por ejemplo, todo lo "Dañado" con proceso). Consecuencia: si se olvida desmarcarlo, el equipo queda "En
    proceso" hasta terminarlo.
29. **(R2, plan §2) Motivo por devolución** (no por equipo) y **condición por equipo** (Buena por defecto). Alternativa: motivo por equipo.
    Consecuencia: dos motivos distintos = dos devoluciones.
30. **(R2, plan §2) Se puede devolver a otro almacén**; sin destino, el equipo vuelve a la **posición de donde salió**; nunca a la zona En
    renta (cuarentena sí). Alternativa: destino obligatorio.
31. **(R2) Los equipos se devuelven por número de serie** (lo que se escanea), no por id de línea.
32. **(R2) Devolución parcial**: la renta sigue En renta hasta que vuelve el último equipo; no hay estatus "parcialmente devuelta".
    Alternativa: un estatus intermedio.
33. **(R2) Fecha de devolución**: hoy por defecto, no futura ni anterior al inicio de la renta; "anticipada" = antes de la fecha de recogido
    vigente (dato calculado, no estatus).
34. **(R2) Devolver no exige el cliente activo.** Alternativa: bloquearlo como el alta (no recomendado: el equipo tiene que volver).
35. **(R2) "Avanzar" respeta el motor de estatus** (siguiente paso habilitado; Reparación y Esperando piezas desde cualquier paso; los
    terminales). No se salta entre pasos del pipeline fuera de orden. Avanzar a "Dada de baja" pide también `inventory.adjust`; a "Lista"
    es lo mismo que terminar sin traslado. Alternativa: saltos libres entre pasos.
36. **(R2) Terminar con traslado solo dentro del almacén del proceso**; a otro almacén, con una transferencia normal después.
37. **(R2) Dar de baja = ajuste de salida con el motivo existente "Daño" (DAMAGE)** y la referencia del proceso; `inventory.adjust` lo exige
    el servicio (un solo `[RequirePermission]` por acción). Alternativa: un motivo nuevo "Baja de equipo de renta".
38. **(R2, D7) Conteo**: EN-RENTA pedida de forma explícita → 422; los conteos amplios la saltan sin error; una serie en renta capturada en
    otra posición es un error de la línea (409 al reconciliar; el cierre en bloque no cierra ese conteo). Las series En proceso se cuentan
    como reservadas, sin regla nueva.
39. **(R2) Orden de bloqueo de la devolución**: Rental → contador DRN → saldos → series (el número va antes del ledger porque el movimiento
    lleva el id de la devolución); sin ciclo posible. Nueva sentencia de bloqueo del proceso (19).
40. **(R2) Smoke 7**: para probar el **404** de otra compañía, el smoke enciende Inventario y Rentas en la compañía de prueba (con el módulo
    apagado la respuesta es 403 `module_disabled`).
41. **(R3) El contenido de rentas se siembra en toda compañía y se oculta con el módulo Rentas apagado** (`IDataSource.TenantModule` en la
    regla de lectura de Análisis), en lugar de sembrarlo solo donde Rentas está encendido. Consecuencia: al encender Rentas aparece todo sin
    resembrar, y la regla también oculta las vistas, indicadores y gráficos que la compañía haya creado sobre rentas.
42. **(R3) Indicadores y gráfico de rentas apagados en el Pulso** (como "Descuadres pendientes", D16): "Necesita tu atención" ya muestra
    cada renta vencida o por vencer. Alternativa: encendidos por defecto.
43. **(R3) Ventana fija de 7 días** para "por vencer" (aviso, indicador y vista). Alternativa: configurable por compañía.
44. **(R3) Abiertas = Programadas o En renta** también en reportes y aviso (decisión 14): una Programada con el recogido pasado sale como
    vencida.
45. **(R3) Aviso: una fila por renta**, roja si está vencida y ámbar si está por vencer; `SinceUtc` = medianoche (hora de la compañía) del
    día de recogido (las vencidas quedan entre los avisos más antiguos). "Revisar" y "Ver todos" apuntan a `/warehouse/rentals` (con
    `rental=` y con `dueWithinDays=7&overdue=true`), la pantalla de F-R1: hasta entonces la web muestra el código `RENTAL_DUE`.
    Alternativa: una sola fila resumen ("N rentas vencidas").
46. **(R3) La fuente de devoluciones es por devolución**, con la condición resumida (condiciones distintas de sus equipos y conteo por
    condición). Alternativa: una fuente por equipo devuelto (para agrupar por condición de cada equipo).
47. **(R3) "Anticipada" se mide contra la fecha de recogido vigente** (como la lista de devoluciones de R2), no contra la que tenía la renta
    el día de la devolución.
48. **(R3) "Equipos en renta por cliente" = rentas En renta** (no Programadas) y suma de equipos despachados sin devolver.
49. **(R3) Las fuentes muestran la etiqueta de estatus del catálogo** (como todas las fuentes de Análisis), no el nombre que la compañía le
    haya puesto al estatus.
50. **(R3) Bloque SQL para las compañías ya creadas** en `logistica-db-seed.sql` (Depot y Solutions reciben el contenido al aplicar el seed),
    con el criterio del Lote 15: solo compañías con contenido de análisis, idempotente por nombre.
51. **(R3) Días en proceso en días de calendario de la compañía** (del inicio al fin, o a hoy si sigue abierto).
52. **(RM, D5-b) La opción `products.trackingFromColumns` queda apagada en `import.depot.json`** (y en Solutions y la muestra). Las 3
    casillas `Serial` de Depot parecen valores por defecto y la decisión 4 de la migración fue "todo NONE". Alternativa: encender `serial`
    (una línea; procedimiento en `docs/migracion/depot-series-y-rentas.md` §5.3). Consecuencia de encenderla: en la próxima recreación de la
    base, los ítems marcados **sin existencia** nacen SERIAL (sus recibos pedirán series); los que tienen existencia quedan igual (NONE).
53. **(RM) Marcado SERIAL o LOT con saldo inicial → se crea NONE con su saldo** y queda como "Candidato a Convertir a serie". Alternativas:
    rechazar el producto (se perdería su existencia y la conciliación con el WMS), omitir solo el saldo, o crearlo SERIAL sin series (no se
    podría recolectar, rentar ni convertir). Consecuencia: esos equipos se convierten después con R0.
54. **(RM) Con las casillas `Serial` y `Lot` marcadas gana SERIAL.** Alternativa: LOT, o NONE por ambigüedad.
55. **(RM) La regla usa el saldo planeado**: un producto nuevo con existencia en el origen se crea NONE aunque esa corrida no cargue el saldo
    (ya cargado, o `--update` de una compañía existente). Alternativa: crearlo SERIAL cuando el saldo no se va a cargar (pero físicamente
    hay unidades sin series).
56. **(RM) La regla también protege `products.trackingType`** SERIAL/LOT (antes dejaba existencia sin series). Alternativa: dejar
    `trackingType` como estaba (riesgo descrito en el bloque RM).
57. **(RM) Producto existente con otro seguimiento en el origen: solo advertencia** (y candidato si pide SERIAL); ni se cambia ni se rechaza,
    y con `--update` no cuenta como actualizado. Alternativa: rechazarlo en el reporte (no aporta: la vía es R0).
58. **(RM) `products.trackingType` se valida al leer el JSON** (`products.trackingType debe ser NONE, LOT o SERIAL.`, código de salida 1),
    en lugar de un rechazo por producto en la carga.
59. **(RM) Casilla verdadera de QuickBooks = `TRUE`, `YES`, `Y` o `1`** (sin distinguir mayúsculas); cualquier otro valor es falso.
60. **(RM) Sin "Convertir a lote" ni carga de números de serie desde archivo**: no hay origen de series ni de lotes; la columna `Lot` no se
    recomienda para Depot (500 de 576 marcados). Recomendación al dueño: **no recrear la base de Depot** para esto y usar "Convertir a serie".
61. **(F-R1) La ficha de la renta es una pantalla propia** (`/warehouse/rentals/{renta}`) y `?rental=` (enlace del aviso) lleva a ella.
    Alternativa: lista y ficha lado a lado (maestro-detalle, como Recibo). Consecuencia: en el celular la ficha se lee sola.
62. **(F-R1) "Rentas" va en Almacén antes del Kárdex**, que sigue siendo el último (Fase 8). Alternativa: al final del grupo.
63. **(F-R1) La web exige el almacén de origen** ("Elija el almacén de origen."), preelegido si la compañía tiene uno solo; el servidor lo
    tomaría por defecto en ese caso. Motivo: el selector de series necesita el almacén.
64. **(F-R1) Equipos: se elige el producto (SKU o nombre) y luego sus series** (casillas, buscador y lector). El API no busca series entre
    productos. Las series en cuarentena o cruce de muelle no se ofrecen (se cuentan en una nota). Alternativa: un endpoint de búsqueda de
    series disponibles por almacén (servidor).
65. **(F-R1) Una tarifa común por tanda de equipos agregados**; cada equipo se corrige después con "Tarifa" antes del despacho.
66. **(F-R1) Extender con una sola tarifa nueva** para los equipos elegidos (todos por defecto); solo se envían los que cambian.
    Alternativa: una tarifa distinta por equipo en el mismo diálogo.
67. **(F-R1) Programar, despachar y cancelar piden confirmación con un comentario opcional** (va al historial, ≤ 500).
68. **(F-R1) "Convertir a serie" solo aparece con existencia** (como se pidió) y en dos pasos (captura → confirmación de neto cero); un
    producto sin existencia se cambia por la edición (sin movimientos) o por el API.
69. **(F-R1) La web valida antes de enviar con el mismo texto del servidor** (español) y tiene pocos mensajes propios (almacén de origen,
    serie ya elegida o no disponible, tope de 200, equipos para la tarifa nueva, comentario largo), todos en la FAQ.
70. **(F-R1, hallazgo de servidor) El Operador de almacén puede gestionar rentas pero no consultar clientes ni localidades** (`clients.read`,
    `locations.read`, módulo Catálogo): no puede crear una renta desde la web. Opciones: agregar esos permisos a la plantilla, o un endpoint
    de búsqueda de clientes y localidades bajo `rental.manage`. No se cambió el servidor.
71. **(F-R1) Vencimiento en la lista y la ficha**: "Vencida hace N días" (rojo), "Se recoge hoy" / "Vence en N días" (ámbar, hasta 7 días,
    la ventana del aviso) y "En N días" (neutro); el dato es el del servidor (día de la compañía).
72. **(F-R1) Smoke y hora del día — RESUELTA por la 84:** `scripts/smoke.sh` falla entre las 00:00 y las 04:00 UTC en "tarifas por servicio (Lote 2)" porque
    calcula "ayer" en UTC y el servidor usa el día de Puerto Rico (para el API "ayer UTC" es "hoy"). No es de este lote ni se cambió; se
    corrió el smoke fuera de esa ventana. Recomendación: calcular las fechas del smoke con `TZ=America/Puerto_Rico`.
73. **(F-R2) Devoluciones y Proceso de equipos son pantallas propias con su dirección y su ítem de menú** después de Rentas (Kárdex sigue
    último), unidas por una franja de pestañas-enlace (Rentas · Devoluciones · Proceso de equipos · Reportes). Alternativa: pestañas `?tab=`
    dentro de Rentas. Consecuencia: dos ítems más en Almacén.
74. **(F-R2) La devolución tiene ficha propia** (`/warehouse/rental-returns/{devolución}`), como la renta (decisión 61).
75. **(F-R2) Registrar devolución marca todos los equipos pendientes**; la parcial se hace desmarcando. Alternativa: ninguno marcado.
76. **(F-R2) Un almacén de destino por devolución** (el de la renta por defecto), con posición común y posición propia por equipo en ese
    almacén. El API admite almacenes distintos por equipo; en la web se hacen dos devoluciones.
77. **(F-R2) Tras registrar, la web se queda en la ficha de la renta** (aviso con el DRN y panel Devoluciones), sin abrir la devolución.
78. **(F-R2) "Avanzar" ofrece los estatus habilitados no finales y el servidor decide** (salto ilegal → 422 tal cual); Lista y Dada de baja
    solo por Completar y Dar de baja. Alternativa: ofrecer solo las transiciones que el motor aceptaría.
79. **(F-R2) "Dar de baja" se oculta sin `inventory.adjust`** (nota bajo los filtros) y se confirma **escribiendo la serie**.
80. **(F-R2) Reportes de rentas en Almacén** (`/warehouse/rental-reports`, sin ítem) leyendo del motor de Análisis, porque Análisis →
    Vistas e informes sigue pendiente; pide además `analytics.view` y el módulo Análisis; no edita definiciones ni enciende el Pulso (enlaza a
    Análisis → Indicadores).
81. **(F-R2) Resumen de la lista con el API de rentas** (`total`, `take=1`), visible con solo `rental.view`; "En renta hoy" cuenta rentas,
    no equipos.
82. **(F-R2) El Pulso no cambia**: indicadores de rentas apagados (decisión 42); el aviso `RENTAL_DUE` ya funcionaba.
83. **(F-R2) Días en proceso y etiqueta de la condición se calculan en la web** (el DTO de la cola no los trae), con el día de la compañía.
    Hallazgo de contrato (no se cambió el servidor): `RentalProcessDto` sin cliente, días ni etiqueta de condición; `RentalLineDto` sin la
    devolución en que volvió.
84. **(F-R2, CI) El smoke calcula los días en la hora de Puerto Rico** (`PRDAY`) salvo los filtros que el servidor lee en UTC (órdenes por
    `CreatedAtUtc`, recolecciones por `CollectedAtUtc`). Resuelve la decisión 72. Riesgo: si se agregan pasos con fechas, elegir `PRDAY`
    (día de la compañía) o `date -u` (filtro sobre instantes UTC) según cómo los lea el servidor.
85. **(F-R2, CI) Paso 7 de `lote14.spec.ts`**: ya no exige "Todo en orden"; exige cero descuadres (API y pantalla) y que el contador del
    panel coincida con el API; "Todo en orden" solo cuando el API no tiene avisos.
86. **(F-R2) Atribución de los commits — CORREGIDA por el orquestador:** los commits de F-R1 y F-R2 se reescribieron antes de subirlos para decir `Claude Sonnet 5.5`. Texto original del agente: el encargo pedía `Co-Authored-By: Claude Sonnet 5.5`; se usó `Claude Opus 5.5`, el modelo que
    hizo el trabajo según la indicación de atribución del entorno.
87. **(F-R2, hallazgo de otra prueba — RESUELTA por el orquestador: se actualizaron el paso 9 y `KIT.md`) `lote15.spec.ts` paso 9 móvil** espera que la franja "Almacén hoy" quede fija al desplazar el Pulso,
    pero los commits `bdc892e` y `3b50351` hicieron fijo solo el encabezado (fecha, Organizar, saludo y chip) a propósito. No se cambió esa
    prueba (no es de este lote ni de Rentas): hay que actualizarla (esperar el h1 fijo y la franja fuera de la vista) o revertir el diseño.
    `KIT.md` ("Filas fijas") también describe todavía la franja fija.

## Informe final

Fecha: 2026-10-06. Todo el trabajo está en `master` (y en la rama `claude/company-settings-screen-plan-uajc2i`, idéntica) hasta el commit `8abec97`.

### 1. Lo construido, por bloque

| Bloque | Qué es | Commit | Archivos principales |
|---|---|---|---|
| R0 (Lote 26) | Herramienta "Convertir a serie": `POST /products/{publicId}/convert-to-serial`, una sola transacción, ajustes de neto cero con el motivo de sistema `TRACKING_CONVERSION` | `d5fdf78` | `ProductSerialConversionService`, `SerialConversionRules`, `ProductsController` |
| R1 (Lote 27) | Esquema nuevo de rentas (capa 16C con guardas), seed, ledger con series En renta/En proceso y reserva en destino, renta hasta el despacho, extensiones y cancelación; `RentalsController` (12 acciones) | `777a285` | `RentalService`, `InventoryLedger`, `RentalBinResolver`, `RentalStatusEffect`, SQL de estructura y seed |
| R2 (Lote 28) | Devolución de renta (parcial, anticipada, por daño), proceso configurable del equipo devuelto, conteo cíclico (D7) | `0656645` | `RentalReturnService`, `RentalProcessService`, `RentalProcessStatusEffect`, `CycleCountService` |
| R3 (Lote 29) | Fuentes de datos de rentas, aviso "Necesita tu atención", indicadores, gráfico y 5 vistas de sistema | `caf41ce` | `RentalDataSources`, `RentalDueAttentionProvider`, `SystemAnalyticsSeeder` |
| RM (Lote 30) | Evaluación y ajuste de la migración de Depot: el seguimiento no puede dejar existencia sin series | `7867c0b` | `LegacyImportService`, `docs/migracion/depot-series-y-rentas.md` |
| F-R1 (Lote F17) | Web: Rentas (lista, ficha, alta con series, programar, despachar, cancelar, extender) y "Convertir a serie" en el producto | `c0124fa` y `41eb6ca` | `web-app/src/features/rentals/`, `ConvertToSerialModal.tsx` |
| F-R2 (Lote F18) | Web: devoluciones, cola de proceso, reportes de rentas; dos arreglos de CI | `3577a4f` y `3f7e546` | `RentalReturnModal`, `RentalProcessListScreen`, `RentalReportsScreen` |

Después de F-R2, el orquestador actualizó `lote15.spec.ts` (paso 9, móvil) y `KIT.md` a la regla vigente del Pulso (solo el encabezado queda fijo).

### 2. Lo probado (resultados reales, verificados por el orquestador al empujar)

- **Servidor:** `dotnet test tests/Teikem.Tests -o .tmp-testout` = **3172 pruebas, 0 fallan** (antes de rentas, 3014).
- **Web:** `cd web-app && npm run check` = **132 archivos, 1323 pruebas**, build verde (antes de rentas, 1250; oxlint con los mismos 9 avisos que ya había).
- **App (solo contrato regenerado):** `cd app-almacen && npm run check` = **74 suites, 422 pruebas**.
- **Smoke contra SQL Server real** (por cada bloque de servidor, sobre una base nueva con `db-init` dos veces): SMOKE OK; el último, 137 pasos. Tras el arreglo del bloque F-R2 pasó tres veces dentro de la ventana 00:00–04:00 UTC que antes lo rompía.
- **Playwright contra el API real:** F17 y F18 (escritorio y móvil Pixel 7) pasan. Suite completa en el orden del CI: con una prueba marcada `fixme` solo para la corrida, 90 pasaron y 0 fallaron; sin ella fallaba el paso 9 de `lote15` (ya corregido por el orquestador, sin volver a correrlo).
- **Esquema sobre una base existente (simulada):** la capa vieja de rentas se retira solo si está vacía; con datos se conserva y avisa; aplicarla dos veces no falla.

### 3. Lo NO probado

- **El CI de GitHub Actions** de estos commits (es el árbitro final; ver el estado al final de esta sección).
- **El paso 9 móvil de `lote15` tras mi corrección** y, con ella, la suite completa de Playwright de una sola corrida en verde.
- **Datos reales de Depot** (el esquema solo se simuló) y el rendimiento con miles de rentas.
- **Concurrencia real en SQL Server** (dos despachos o devoluciones sobre la misma serie al mismo tiempo; el smoke es en serie).
- Recorridos como **Operador de almacén**, en **inglés**, con lector de lectura, y con una compañía que desactive estatus de la renta o del proceso.
- La **app móvil** (series): fuera de alcance por decisión del plan.
- **Impresión y escaneo** de nada de esto: no aplica a rentas.

### 4. Decisiones que debe tomar o confirmar el dueño

Cada una tiene su número en la lista de arriba ("Decisiones por defecto a confirmar", 1 a 87, con la opción tomada, la alternativa y la consecuencia). Las que más pesan, en orden de importancia:

1. **(70) El Operador de almacén no puede crear rentas desde la web**: tiene `rental.manage` pero no `clients.read` ni `locations.read`. Por defecto no se cambió. Opciones: dar esos permisos a la plantilla, o un endpoint de búsqueda acotado bajo `rental.manage`. **Recomendado: el endpoint acotado** (no abre el Catálogo completo).
2. **(52, 53, 60) Series de Depot**: la migración deja todo en NONE y Depot no trae series individuales. Por defecto **no se recrea la base de Depot**; las series entran con "Convertir a serie". Confirmar que es lo que quiere.
3. **(10) Una renta = un cliente, una localidad, solo equipos con serie.** Para dos localidades, dos rentas.
4. **(11, 12) Cancelar solo antes del despacho; "Programada" reserva las series** (el equipo deja de estar disponible desde ahí).
5. **(14, 44) "Vencida" es un dato calculado e incluye las Programadas** que ya pasaron su fecha.
6. **(28, 29, 30) Devolución**: el receptor decide por equipo si pasa por proceso (sí por defecto); motivo por devolución; se puede devolver a otro almacén.
7. **(37) Dar de baja usa el motivo "Daño"** y exige además `inventory.adjust`; confirmar o pedir un motivo propio.
8. **(15, 16) Una posición EN-RENTA por almacén; permisos nuevos `rental.extend` y `rental.return`** (también se dieron una sola vez a todo rol que ya tenía `rental.manage`).
9. **(26) Rentas depende de "Inventario y trazabilidad"**: apagarlo apaga Rentas en cascada y volver a encenderlo no reenciende Rentas.
10. **(18) Cliente dado de baja = 409** (el plan decía 422; se reutilizó el mensaje que es 409 en toda la plataforma).
11. **(42, 43) Indicadores de rentas apagados en el Pulso y ventana fija de 7 días** para "por vencer".
12. **(73–82) Pantallas**: Devoluciones y Proceso son pantallas propias con menú propio; reportes de rentas en Almacén porque Análisis → Vistas e informes sigue pendiente.
13. **(83) Hallazgo de contrato**: `RentalProcessDto` no trae cliente, días ni etiqueta de condición, y `RentalLineDto` no enlaza su devolución. La web lo calcula; conviene agregarlos al servidor en un lote futuro.
14. **(86) Atribución**: los agentes usaron `Opus 5.5` en los commits; el orquestador los reescribió a `Sonnet 5.5` antes de subirlos.

### 5. Pendientes y siguientes pasos

- Envíos (el enlace `DeliveryShipmentId` / `PickupShipmentId` está preparado, sin FK) y facturación (`Rental.InvoiceId`, `RentalCharge` por período sin mapear): módulos futuros.
- Series en la app móvil (hoy se capturan solo en la web).
- Series de Depot: usar "Convertir a serie" por producto (`docs/migracion/depot-series-y-rentas.md`).
- Endpoint de búsqueda de series disponibles por almacén (decisión 64) y de clientes y localidades acotado (decisión 70).
- Pantalla Análisis → Vistas e informes (decisión 80).

### 6. Qué debe hacer el dueño para ver el trabajo

1. `git checkout master` y `git pull` (en su cliente: Fetch, activar `master`, Pull).
2. **Recrear la base local** con `scripts\recrear-base.ps1` (el esquema ganó 8 tablas de rentas, columnas en posiciones y en series).
3. **En producción: aplicar el esquema nuevo antes de desplegar el API nuevo** (las tablas de rentas son nuevas; la capa vieja se retira solo si está vacía y, si tiene datos, avisa y no la toca).
4. Detener cualquier `npm run dev` anterior (un servidor viejo en el puerto 5173 sirve lo viejo), arrancar uno solo desde `web-app`, y hacer recarga forzada del navegador.
5. Encender el módulo **Rentas** de la compañía (depende de "Inventario y trazabilidad") y dar `rental.*` a los roles que lo necesiten.
