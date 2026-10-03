# Lote 20 — "Hoy" en la zona horaria de la compañía en los servicios que lo calculaban en UTC (2026-10-03)

Cierra el pendiente del Lote 18 (parte 4 del cierre de "Región y formatos de la compañía"): unos 25 servicios calculaban "hoy"
con `DateOnly.FromDateTime(DateTime.UtcNow)` o `DateTime.UtcNow.Date`; entre las 8 p. m. y la medianoche de Puerto Rico ya era
"mañana" para ellos. Ahora toman el día local de la compañía con `ITenantClock` (Lote 14/18). Es un cambio de backend sin
cambio de contrato HTTP (no se regeneró `openapi.json`: solo cambiaron comentarios XML, que no entran al esquema).

## Qué se construyó

- **Patrón.** Cada servicio recibe `ITenantClock? clock = null` como **último** parámetro del constructor primario y guarda
  `_clock = clock ?? TenantClock.Default` (el mismo patrón de las fuentes de análisis del Lote 18). En DI llega
  `ContextTenantClock` (zona de la compañía del contexto); sin reloj (pruebas que construyen a mano) y sin tenant en contexto
  (jobs, efectos, seeders) el respaldo es Puerto Rico. Los helpers `Today()` privados pasaron de `static` a instancia y
  devuelven `_clock.Today`. Ningún constructor existente cambió de firma para quien ya lo llamaba (parámetro opcional).
- **Familias:** órdenes; rutas y trips; flota y mantenimiento (+ `FleetController`); tarifas y contratos; compras y producto;
  DSL (`RuleEvaluator.Validate` con reloj opcional; `CustomFieldService` se lo pasa y avisa si el campo es de fecha y hora).
- **`VehicleService`:** el año de modelo máximo (`ValidateModelYear(modelYear, año)`) usaba `DateTime.UtcNow.Year`; ahora el año del
  día local (el 31 de diciembre por la noche en Puerto Rico ya era el año siguiente en UTC).
- **`VehicleDocumentService.ToDtosAsync`** (interno, estático): recibe `DateOnly today` explícito; lo llaman `VehicleDocumentService`
  y `VehicleService` con su reloj.
- Comentarios XML/documentación en código que decían "hoy (UTC)" se corrigieron (contratos y controladores de trips, compras, flota).

## Tabla de cada uso revisado

`grep -rnE "UtcNow|DateTime\.(Today|Now)|DateTimeOffset\.(Now|UtcNow)" src --include=*.cs` dio 208 líneas. Las que no estampan
ni comparan un día de negocio (instantes) se listan al final. Resultado de las que sí:

| Archivo (línea aprox., antes) | Uso | Decisión |
|---|---|---|
| `Services/OrderService.cs` 847 | `Today()` → contrato vigente (`CurrentContractAsync`) al crear/editar/cotizar, fecha de la tarifa especial, `IsCurrentOn` | fecha de negocio → reloj de la compañía |
| `Services/OrderStatusService.cs` 268 | `Today()` → cotización al confirmar, transiciones que dependen del contrato | fecha de negocio → reloj |
| `Services/OrderStatusEffect.cs` 80 | `ApplyQuoteAsync(..., hoy)` en el efecto de confirmación | fecha de negocio → reloj (sin tenant cae a Puerto Rico; el efecto corre dentro de la petición, con tenant) |
| `Services/ClientService.cs` 471 | contrato vigente hoy en el listado y la ficha | fecha de negocio → reloj |
| `Services/RateService.cs` 499 | tarifas vigentes a la fecha, `EffectiveFrom ?? hoy`, `EnsureNotPast` ("no anterior a hoy"), tramos | fecha de negocio → reloj (`EnsureNotPast` pasó a instancia) |
| `Services/DriverRateService.cs` 443 | igual que RateService para entregas, intentos y viajes del chofer | fecha de negocio → reloj |
| `Services/DriverRatesRetirementEffect.cs` 26 | cierra tarifas del chofer "hoy" al retirarlo | fecha de negocio → reloj (efecto de estatus; sin tenant → Puerto Rico) |
| `Services/SpecialServiceService.cs` 298 | vigencia de tarifas de servicios especiales, `EffectiveFrom ?? hoy`, contrato vigente | fecha de negocio → reloj |
| `Services/DriverService.cs` 437 | estado de licencias/certificaciones en la ficha del chofer | fecha de negocio → reloj |
| `Services/DriverDocumentService.cs` 272 | estado de vencimiento de licencias y certificaciones (`DaysTo`, `ExpiryState`) | fecha de negocio → reloj |
| `Services/VehicleDocumentService.cs` 107 | `IsSuperseded`/vencimiento de documentos del vehículo (`ToDtosAsync`) | fecha de negocio → reloj (parámetro `today`) |
| `Services/FleetDocumentService.cs` 49 | panel "documentos por vencer": hoy, `hoy + withinDays`, `includeExpired` | fecha de negocio → reloj |
| `Services/MaintenanceScheduleService.cs` 107, 278 | vencimiento de programas por días y validación de `lastServiceDate` no futura | fecha de negocio → reloj (`ValidateNumbers` pasó a instancia) |
| `Services/MaintenanceWorkOrderService.cs` 390 | día de hoy en la transición de estatus de la orden de trabajo (cierre y validaciones de fecha) | fecha de negocio → reloj |
| `Services/VehicleService.cs` 293 | tope del año de modelo | fecha de negocio (año local) → reloj |
| `Services/TripService.cs` 430 | `ValidatePlanDate` (ayer .. hoy + 60) | fecha de negocio → reloj |
| `Services/TripReadService.cs` 490 | listado "del día" sin fecha (`PlanDate == hoy`) | fecha de negocio → reloj |
| `Services/TripDispatchService.cs` 38 | rutas despachables del día sin fecha | fecha de negocio → reloj |
| `Services/TripDayPlanningService.cs` 209 | validación de `planDate` del plan del día | fecha de negocio → reloj |
| `Services/TripMonitorService.cs` 31 | monitor del día sin fecha (`MonitorDto.Date`) | fecha de negocio → reloj |
| `Services/DriverTripService.cs` 241 | fecha del viaje del chofer (`TripDate ?? hoy`, no futura) | fecha de negocio → reloj |
| `Services/OutboundScanService.cs` 50 | `PlanDate ?? hoy` en el escaneo de salida | fecha de negocio → reloj |
| `Services/SpecialDeliveryDispatchService.cs` 164 | fecha del viaje y tarifa del chofer en entregas especiales | fecha de negocio → reloj |
| `Services/PurchaseOrderService.cs` 119 | `OrderDate ?? hoy` (y `expectedDate >= orderDate`) | fecha de negocio → reloj |
| `Services/PurchaseShortageService.cs` 233 | fecha de la OC de reposición creada desde faltantes | fecha de negocio → reloj |
| `Services/ProductService.cs` 262 | estado de vencimiento de los lotes del producto (días para vencer) | fecha de negocio → reloj |
| `Api/Controllers/FleetController.cs` 37 | `GET /fleet/availability` sin fecha | fecha de negocio → reloj (el controlador recibe `ITenantClock`) |
| `Dsl/RuleEvaluator.cs` 87-88 | `min`/`max` de validación de fechas "días relativos a hoy" con `DateTime.UtcNow.Date` | fecha de negocio → reloj; ver decisión 1 |
| `Services/CustomFieldService.cs` (llamada a `Validate`) | pasa el reloj y si el campo es FECHA o FECHA Y HORA | adaptador del cambio anterior |
| `Migration/LegacyImportReport.cs` 73 | `GeneratedAt = DateTime.Now` en el reporte del importador (hora del servidor, línea de comandos) | **se queda**: marca informativa de un reporte de consola, sin tenant ni regla de negocio |
| `Services/FuelLogService.cs` 304 | `fillDateUtc` no puede estar más de la tolerancia en el futuro | instante → se queda en UTC (compara un instante) |
| `Services/DockAppointmentService.cs` 95, 155 | `ValidateWindow(inicio, fin, ahora)` de una cita | instante → se queda en UTC (ventana de instantes) |
| `Wms/PutawaySuggester.cs` 58 | rotación = salidas de los últimos 30 días móviles | instante → se queda (ventana móvil de 30×24 h, no de días de calendario) |
| `Analytics/WarehouseTaskDataSource.cs` 68 | edad de la tarea en horas | instante → se queda (diferencia de instantes) |
| `Services/OrderImportService.cs` 171 | `ReservedAtUtc` | instante → se queda en UTC |
| `Services/TripStatusEffect.cs` 145, 173 | `ActualStartUtc`/`ActualEndUtc` | instante → se queda en UTC |
| `Trips/RouteWriter.cs` 172 | `CreatedAtUtc` de paradas/versiones de ruta | instante → se queda en UTC |
| `Services/TraceabilityService.cs` 240, `InventoryReconciliationService.cs` 107/130/306 | `RunAtUtc`, `DetectedAtUtc` (esta última ya usaba el reloj desde el Lote 14) | instante → se queda en UTC |
| `Wms/InventoryLedger.cs` 172/272/300, `InventoryReconciliationWorker.cs`, `SyncService.cs` 389, `CycleCountService.cs` 467 | `CreatedAtUtc`, `ReconciledAtUtc`, cola del worker, hora del servidor para sincronización | instante → se queda en UTC |
| `Services/DeviceService.cs`, `PinService.cs`, `AuthService*.cs`, `JwtTokenService.cs`, `TotpService.cs`, `CompanySessionService.cs`, `PortalUserService.cs`, `Api/Auth/Policies.cs`, `Api/Middleware/IdempotencyMiddleware.cs`, `Program.cs` (/health), `SecurityControllers.cs` (nombre del archivo CSV) | expiraciones y emisión de tokens, códigos, PIN, sesiones, ventana AAL2, TTL de idempotencia, nombre de archivo | instante → se queda en UTC (nunca "día de negocio") |
| `Persistence/Interceptors/AuditSaveChangesInterceptor.cs` 93 | `AuditLog.CreatedAtUtc` | instante → se queda en UTC |
| `Seeding/DemoTenantSeeder.cs` 114 | `EmailVerifiedUtc` | instante → se queda en UTC |
| `Domain/*.cs` (`GrantedAtUtc`, `IssuedAtUtc`, `RegisteredAtUtc`, `ChangedAtUtc`, `EnabledAtUtc`) | valores por defecto de propiedades técnicas | instante → se queda en UTC |
| `*Service.cs` con `CreatedAtUtc = DateTime.UtcNow` (tarifas, programas, OC…) | sello de creación | instante → se queda en UTC |
| `Analytics/ClientDataSources.cs` 216, `Analytics/TripDataSource.cs` 89-91 | `DateOnly.FromDateTime(q.ToUtc)` como respaldo cuando la consulta se armó a mano sin días locales | no es "hoy" ni usa `UtcNow`; el motor ya pasa `FromDay/ToDayExclusive` locales desde el Lote 14. Sin cambio |

`src/Teikem.Domain` no usa `DateTime.UtcNow` para "hoy" (solo valores por defecto de instantes); las reglas puras reciben el día
como parámetro (`ValidatePlanDate(plan, today)`, `DriverTripRules.ValidateTripDate`, `MaintenanceDue`), y ya no hay uso de
`DateTime.Today`/`DateTime.Now` para negocio en `src`.

## Cómo se probó (ejecutado en este entorno)

- `dotnet build Teikem.sln -c Release`: 0 errores.
- `dotnet test Teikem.sln -c Release`: **2940 pruebas, 0 fallas** (11 nuevas en `TenantTodayTests`; las 2.806+ previas siguen pasando).
- `db-reset --yes` y luego `db-init` dos veces sobre SQL Server 2022 (`scripts/dev-sqlserver.sh`): las tres terminan bien.
- API en Development (`Auth__Onboarding__Enabled=false`, puerto 5000) y `scripts/smoke.sh` con `SMOKE_SQL` (sqlcmd local) y
  `SMOKE_MIGRATION_RUN` como en el CI: **SMOKE OK** de punta a punta. El API se cerró al terminar (verificado con `ps`).

Pruebas nuevas en `tests/Teikem.Tests/TenantTodayTests.cs` (reloj fijo: 1:30 UTC del 3 de octubre de 2026 = 2 de octubre en
Puerto Rico, Honolulú y Nueva York; 3 de octubre en Madrid):

- El instante fijo es 2 de octubre en PR, Honolulú y Nueva York y 3 de octubre en Madrid.
- **DSL:** `max: 0` acepta el 2 y rechaza el 3 en Puerto Rico (con UTC aceptaba el 3); en Madrid acepta el 3 y rechaza el 4;
  `min: -1` relativo al día local; campos de fecha y hora comparan el instante con la medianoche local en UTC (04:00 UTC en PR,
  un segundo antes se rechaza); sin reloj usa Puerto Rico y sin reglas no cambia nada.
- **Trips:** monitor y despachables sin fecha devuelven el día 2 (no el 3) y solo la ruta del día 2; rango de `planDate`
  (máximo hoy local + 60: el 61 se rechaza y el 60 se acepta); con Madrid el monitor devuelve el 3.
- **Flota:** `FleetController.Availability(null)` pide el 2 (PR) o el 3 (Madrid); una fecha explícita se respeta.
- **Compras:** la OC sin fecha queda fechada el 2 de octubre y su `CreatedAtUtc` es la hora UTC **real** (entre antes y después
  de la llamada), no el reloj fijo: los instantes siguen en UTC.
- Respaldo sin tenant: zona por defecto, sin excepción.

Pruebas existentes ajustadas (no cambia lo esperado, solo la fuente de "hoy"): `TripServiceFixture.Today`, y las fechas de
orden de compra / plan de ruta que las pruebas sembraban con `DateOnly.FromDateTime(DateTime.UtcNow)` pasaron a
`TenantClock.Default.Today` (`SyncRulesTests`, `PickBatchServiceTests`, `PurchaseOrderReceivingTests`,
`PurchaseShortageServiceTests`, `ReceiptServiceTests`, `ActivityRulesTests`, `TripStatusEffectTests`). Codificaban el error de
UTC: entre las 8 p. m. y la medianoche de PR habrían fallado contra los servicios corregidos. Ninguna aserción cambió.

**Límite de la cobertura (honesto):** las familias de órdenes, tarifas/contratos, clientes, choferes, documentos de flota,
mantenimiento, producto y servicios especiales cambiaron por el mismo patrón mecánico (campo `_clock`, `Today()` = `_clock.Today`)
y las valida el compilador, las pruebas existentes (2.9xx) y el smoke; no tienen una prueba de reloj fija propia porque sus
servicios dependen de SQL crudo/transacciones que el proveedor InMemory no ejecuta. El mecanismo común (`ITenantClock`) está
probado en el Lote 18, y las familias con prueba propia son DSL, trips, compras y flota (controlador).

## Decisiones para el dueño

1. **DSL `min`/`max` de fechas.** Se mantuvo el contrato guardado (`{"min":-1,"max":0}` en días relativos a hoy). "Hoy" es el día
   de la compañía. Un campo FECHA compara días de calendario; un campo FECHA Y HORA compara el instante contra las 00:00 locales
   de hoy (en UTC). Alternativa descartada: comparar en UTC puro, que reproduce el error de las 8 p. m.
2. **Respaldo sin tenant = Puerto Rico** (efectos de estatus, workers, seeders) para no romper procesos sin compañía; si una
   compañía de otra zona dispara un efecto fuera de petición, el efecto contaría en PR. Hoy todos los efectos corren dentro de
   la petición con tenant del principal.
3. **Parámetro opcional `ITenantClock? clock = null`** en vez de obligatorio, igual que las fuentes de análisis, para no tocar
   ~60 construcciones en pruebas. El riesgo es que un servicio construido a mano sin reloj use Puerto Rico en silencio; en
   producción lo resuelve DI siempre.
4. **Año de modelo de vehículo:** se cuenta con el año local (1 de enero local, no UTC).
5. **Ventana de rotación del `PutawaySuggester` (30 días)** se dejó como ventana móvil de instantes, no de días de calendario.

## Pendientes

- Pruebas de reloj fijo por familia para órdenes, tarifas, documentos y mantenimiento requieren un fixture que ejecute su SQL
  crudo (hoy solo lo ejerce el smoke con la zona por defecto).
- El smoke no fuerza el efecto (depende de la hora real del día de la corrida); no se agregó un bloque.
- `web-app`/`app-almacen` no se tocaron: el cliente debe seguir mostrando fechas en la zona de la compañía (Lote F9).
