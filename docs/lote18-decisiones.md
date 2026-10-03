# Lote 18 — Región y formatos de la compañía: base de datos y backend (fase 2) (2026-10-03)

Pedido de Luis (bitácora del documento maestro, "Ajustes de la compañía: pestañas, región y formatos, calendario, y modo de
recibo por almacén"): la zona horaria, la moneda y los formatos de fecha, hora, números y teléfono quedan **en la base de datos**
por compañía y "hoy" se cuenta en la zona de la compañía. Este lote es la **fase 2** (base de datos + backend). La fase 1
(diseño: mock y columnas de `dbo.Tenant` en el script de estructura) ya estaba hecha; la pantalla web y la app del Zebra son
las fases 3 y siguientes. No tiene número propio en el plan: se tomó el siguiente libre (18).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Reglas puras | Dos regiones (`PR`, `US`) con su juego completo de valores (`Defaults`), valores permitidos (los mismos `CHECK` de `dbo.Tenant`), normalización, validación campo a campo y de la combinación (separadores distintos), zona reconocida por la plataforma (IANA o Windows → IANA de la región), regla de región (`Apply`) y mensajes exactos del 400 | `src/Teikem.Domain/Tenancy/TenantFormatRules.cs` |
| Entidad y mapeo | 14 columnas nuevas en `Tenant` con los mismos defaults del SQL (Puerto Rico); mapeo 1:1 con tipos de columna | `Tenant.cs`, `TenancyConfigurations.cs` |
| Ajustes de la compañía | `GET/PUT /api/v1/tenant/settings` con los 14 campos y `isRegionCustomized`; PUT parcial (null = sin cambio); 400 `ValidationException` con el mensaje exacto y el campo; auditoría automática (`[AuditEntity]` de `Tenant`) | `TenantContracts.cs`, `TenantService.cs`, `TenantControllers.cs` |
| Opciones de la pantalla | `GET /api/v1/tenant/format-options` (solo sesión): regiones con sus valores por defecto y valores permitidos de cada campo | `TenantService.GetFormatOptions`, `TenantController.FormatOptions` |
| Aprovisionamiento | `TenantProvisionRequest` acepta `regionCode` (sin valor = `PR`) y los campos de formato; se valida antes de crear nada | `ProvisioningService.cs` |
| Reloj de la compañía | `ContextTenantClock` (scoped, el registrado en DI): la zona sale de `Tenant.TimeZoneId` del tenant del contexto (JWT `tid` o `TenantContext.As` de jobs/seeders); sin tenant, `LocalDay.DefaultZone`. `TenantZoneCache` (singleton sobre `IMemoryCache`, 10 min) guarda la zona por compañía y `TenantService` la invalida al guardar | `src/Teikem.Infrastructure/Abstractions/TenantClock.cs`, `DependencyInjection.cs` |
| "Hoy" de las fuentes de análisis y días hábiles | `ClientDataSourceHelpers.Today` usa el reloj de la compañía (fuentes de clientes, vehículos, choferes, documentos de flota y existencias); `GET /tenant/work-days` termina en el "hoy" local | `Analytics/*DataSource*.cs`, `TenantService.LastWorkDaysAsync` |
| Contrato web y app | `web-app/openapi.json`, `web-app/src/kernel/api/schema.d.ts` y `app-almacen/src/kernel/api/schema.d.ts` regenerados (Swagger del API + `openapi-typescript` 7.13.0, la versión del lock) | — |
| Humo | Bloque "región y formatos de la compañía" en `scripts/smoke.sh` | — |
| Manual | Capítulo 01, sección 11.1 (y sección 12, aprovisionamiento); FAQ "Región y formatos (2026-10)"; índice | `docs/manual/` |

### Uso del reloj: qué pasó a leer la zona de la compañía y qué no

Búsqueda de `LocalDay.DefaultZone` y `TenantClock.Default` en `src/`:

- **Pasan a la zona del tenant** (sin cambiar su código, porque reciben `ITenantClock` por DI y ahora el registrado es
  `ContextTenantClock`): Kárdex y su resumen (`InventoryReadService`), descuadres (`InventoryReconciliationService`, también desde
  el `InventoryReconciliationWorker`, que trabaja con `TenantContext.AsAnonymous(tenantId)`), lo cambiado del conteo
  (`CycleCountService`), franja del Pulso (`WarehousePulseService`, devuelve la zona en `timeZone`), motor y servicio de análisis
  (`AnalyticsEngine`, `AnalyticsService`, `DateRangeResolver`), Actividad reciente (`ActivityFeedService` → `ActivityRules`),
  `InventoryTransactionDataSource`.
- **Cambiados en este lote**: `ClientDataSourceHelpers.Today()` leía `TenantClock.Default` fijo → ahora recibe el reloj (nuevo
  parámetro opcional en `ClientDataSource`, `VehicleDataSource`, `DriverDataSource`, `FleetDocumentDataSource`,
  `StockBalanceDataSource`); `TenantService.LastWorkDaysAsync` contaba con el día UTC → reloj de la compañía.
- **Se quedan como están, a propósito**: `TenantClock.Default` como respaldo `clock ?? TenantClock.Default` de los parámetros
  opcionales (solo aplica cuando una prueba construye la clase a mano); `LocalDay.DefaultZone` dentro de `LocalDay`,
  `TenantClock` y `ContextTenantClock` (zona sin tenant y respaldo de una zona desconocida) y en `WarehousePulseService.ZoneName`
  (solo traduce la zona por defecto a su nombre IANA). Las pruebas existentes siguen registrando `TenantClock.Default` (zona fija):
  no cambian.
- **Pendiente (fuera de este encargo)**: servicios que todavía toman "hoy" como `DateOnly.FromDateTime(DateTime.UtcNow)` sin pasar
  por el reloj: `OrderService`, `OrderStatusService`, `OrderStatusEffect`, `ClientService` (contrato vigente), `RateService`,
  `DriverRateService`, `DriverRatesRetirementEffect`, `DriverService`, `DriverDocumentService`, `VehicleDocumentService`,
  `FleetDocumentService`, `MaintenanceScheduleService`, `MaintenanceWorkOrderService`, `TripService`, `TripReadService`,
  `TripDispatchService`, `TripDayPlanningService`, `TripMonitorService`, `DriverTripService`, `SpecialServiceService`,
  `SpecialDeliveryDispatchService`, `OutboundScanService`, `ProductService`, `PurchaseOrderService` (fecha de la orden por
  defecto), `PurchaseShortageService`, `FleetController` (disponibilidad sin fecha) y `RuleEvaluator` (mínimo/máximo de fechas
  del DSL con `DateTime.UtcNow.Date`). Entre las 8 p. m. y la medianoche de Puerto Rico ya es "mañana" para ellos. Pasarlos al
  reloj es mecánico (inyectar `ITenantClock` y usar `clock.Today`), pero toca muchos lotes y sus pruebas: queda para un lote
  propio.

## Cómo se probó (ejecutado en este entorno)

- `dotnet build Teikem.sln`: 0 errores (SDK 8.0.425 instalado con `dotnet-install.sh`).
- `dotnet test Teikem.sln`: **2806 pruebas, 0 fallas** (incluidas las nuevas). Nuevas: `TenantFormatRulesTests` (defaults por región, defaults de la entidad = los del SQL, cada `CHECK` con su
  mensaje exacto, separadores iguales en el mismo pedido y contra el valor guardado, regla de región sin/con campos, misma
  región, normalización, espacio como separador de miles, zonas IANA y de Windows → IANA de la región, largo de la zona) y
  `TenantFormatServiceTests` (GET con los defaults de Puerto Rico, PUT con la regla de región, 400 sin guardar nada, opciones de
  la pantalla, permisos de los endpoints y auditoría de la entidad, aprovisionamiento que rechaza antes de crear, y
  `ContextTenantClock`: Puerto Rico vs Honolulú a la 1:30 UTC del 3 de octubre → 2 de octubre en ambas, medianoches locales
  distintas, sin tenant → zona por defecto, caché por compañía invalidada por el servicio, días hábiles en el día local).
  Las pruebas existentes de los lotes que usan el reloj (Kárdex, resumen, descuadres, Pulso, Actividad, análisis) siguen igual.
- `db-init` dos veces sobre un SQL Server 2022 limpio (`scripts/dev-sqlserver.sh`): ambas terminan bien; la demo queda con
  `PR`, `America/Puerto_Rico`, `USD`, `$`, `B`, 2, `MDY`, `/`, 12, 0, `,`, `.`, `+1`, `(###) ###-####`.
- `scripts/smoke.sh` contra el API (Development, `Auth__Onboarding__Enabled=false`, con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN`
  como en el CI): **SMOKE OK** de punta a punta (130 pasos, incluidas la migración de muestra y `db-reset`). El bloque nuevo lee los ajustes y las opciones, cambia a `US` (zona `America/New_York` y
  demás valores), comprueba que la franja del Pulso cuenta con la zona nueva y que la bitácora de cambios registró la zona,
  cambia un campo suelto (Personalizada), cambia región + campo explícito, prueba el 400 de separadores iguales (en el mismo
  pedido y contra el valor guardado), de zona desconocida y de región desconocida, comprueba que un 400 no cambia nada y
  restaura Puerto Rico con el juego de `format-options`.
- Contrato: `openapi.json` se regeneró con el Swagger del API (antes del cambio, el archivo del repositorio era idéntico byte a
  byte al que genera el API) y los dos `schema.d.ts` con `openapi-typescript` 7.13.0 (con el `openapi.json` anterior reproduce
  exactamente los archivos del repositorio). `npm run check` de web y app no se corrió aquí (sin `node_modules`): lo corre el CI.

## Decisiones a revisar

1. **Orden de fecha de Puerto Rico = MM/DD/AAAA (`MDY`), POR CONFIRMAR con el dueño.** Se escogió por la práctica comercial local
   (bitácora del maestro); hay que confirmarlo contra lo que hoy muestra la aplicación. Cambiarlo es una constante
   (`TenantFormatRules.PuertoRicoDateOrder`), el default de la entidad que la usa y el `DEFAULT 'MDY'` de `dbo.Tenant.DateOrder`
   en `Diseño/logistica-db-estructura.sql` (más un `UPDATE` de las compañías ya creadas si se decide después de sembrarlas).
2. **Regla de región con campos explícitos:** región distinta de la actual → se parte de los valores de la región nueva y los
   campos enviados mandan sobre ellos (no sobre los valores anteriores). Región igual a la actual → no es un cambio de región:
   solo se aplican los campos enviados; para "Restaurar valores de la región" la pantalla manda el juego completo que trae
   `GET /tenant/format-options`.
3. **Agregados que no estaban pedidos explícitamente:** `GET /api/v1/tenant/format-options` (para que la web y la app no
   copien a mano los valores de cada región ni las listas permitidas) e `isRegionCustomized` en `TenantSettingsDto` (la marca
   *Personalizada* sale del servidor). Si no se quieren, se quitan sin tocar lo demás.
4. **Primer error, no todos:** la validación devuelve el primer campo con error (en el orden de la pantalla) con su mensaje en
   `title` y `errors.<campo>`, como el resto de `PUT /tenant/settings`. El de separadores iguales sale en `errors.decimalSeparator`.
5. **Zona por compañía con caché:** la primera petición de una compañía lee su zona con el `DbContext` del alcance (misma conexión
   y transacción: no espera bloqueos) y la guarda 10 minutos en `IMemoryCache`; `PUT /tenant/settings` la invalida en la misma
   instancia. Con varias instancias del API, las demás ven la zona nueva a más tardar en 10 minutos. Una zona guardada que el
   servidor no conoce (no debería pasar: el servicio la valida) cae en la zona por defecto, igual que `LocalDay.ResolveZone`.
6. **Id de Windows → IANA de la región:** `SA Western Standard Time` se guarda como `America/Puerto_Rico` en una compañía `PR`
   (sin región sería `America/La_Paz`, la principal de esa zona de Windows), `Eastern Standard Time` como `America/New_York`.
7. **Bases existentes:** el script de estructura cambió en la fase 1 (columnas nuevas de `dbo.Tenant`) y el runner lo aplica una
   sola vez sobre base limpia; una base ya creada se recrea con `db-reset --yes` / `scripts/recrear-base.ps1` como en los lotes
   16 y 17 (no hay scripts `ALTER` por convención). Los defaults dejan a toda compañía nueva o recreada en Puerto Rico, sin
   cambios de comportamiento.
8. **`CHECK` de separadores:** en SQL Server la comparación de `NVARCHAR` ignora los espacios finales, así que `N''` también
   cumple `ThousandsSeparator IN (N',',N'.',N' ')`. El servicio lo rechaza (`El separador de miles debe ser…`); el `CHECK` no se
   endureció para no tocar el script de la fase 1 sin necesidad.
9. **Seed:** no hizo falta tocar `Diseño/logistica-db-seed.sql`; los defaults de la tabla cubren la demo. El importador de Advance
   (`import-legacy`) y `DemoTenantSeeder` aprovisionan sin región: quedan en Puerto Rico.

## Qué quedó fuera

- **Fase 3 — Web:** la pantalla `/system/settings` en pestañas con *Región y formatos* (vista previa en vivo, *Personalizada*,
  *Restaurar valores de la región*) y el proveedor único de formatos que alimente `numberFormat.ts` (hoy `es-PR`/`USD` fijos),
  `tenantZone.ts` (hoy `America/Puerto_Rico` fija), `analytics/format.ts` (`en-US` fijo), `phone.ts` (máscara fija) y las llamadas
  sueltas a `Intl`/`toLocale`. Los tipos ya están en `schema.d.ts`.
- **Fase siguiente — App del Zebra:** recibir los mismos valores al sincronizar (hoy puede leerlos de `GET /tenant/settings`).
- Calendario, pestañas restantes y el resumen de recibo por almacén del mismo pedido (otras piezas del plan).
- Pasar al reloj de la compañía los servicios listados como pendientes arriba.
