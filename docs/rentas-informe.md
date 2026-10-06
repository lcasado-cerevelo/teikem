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
