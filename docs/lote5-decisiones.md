# Lote 5 — Trips y rutas: qué se construyó y decisiones a revisar

Fecha: 2026-09-26. Plan aprobado: `docs/lote5-plan.md` (y su espejo `docs/lote5-plan.json`). Autorización general de
Luis (2026-09-26): implementar las 47 decisiones del plan tal como están, con 13 marcadas "pendientes de ratificación"
(sección siguiente). Este documento cierra el lote: mapa de lo construido, cómo se probó, las decisiones que Luis
dejó abiertas y lo que queda fuera.

> Manual funcional del lote: **completo**, en una pasada posterior a la de arriba. `docs/manual/05-trips-y-rutas.md`
> (capítulo nuevo, indexado en `docs/manual/README.md`) documenta las 13 secciones del módulo (planificación diaria,
> cabecera y "Eliminar ruta", reasignación en bloque, órdenes en la ruta y "Sin asignar", optimización en tres fases,
> reordenamiento y pin manual, ETA, zonas de despacho, despacho y salida, escaneo Outbound, "Planificar el día",
> monitoreo, estatus/transiciones, permisos/módulos y fuentes de análisis), con el mensaje exacto y el código HTTP de
> cada validación citados del código. `docs/manual/faq.md`, sección "Lote 5 — Trips y rutas", se amplió de 2 a 12
> temas (números y alta, cabecera/reasignación/planificación, órdenes en la ruta y elegibilidad, optimización/
> secuencia/pin, despacho y salida, zonas de despacho, escaneo Outbound, planificar el día, monitoreo, permisos/RBAC)
> para cubrir cada mensaje de error citado en el capítulo.
>
> **Verificación de este cierre (2026-09-26, sobre BD recreada desde cero, esta sesión):** `dotnet build Teikem.sln -c
> Release` sin errores (4 advertencias preexistentes/de estilo, ninguna nueva de lógica); `dotnet test Teikem.sln`
> **979/979** en verde; `db-init` dos veces sobre una base `Teikem` recién creada (primera vez: ~133 tablas en 146
> lotes, seed en 15 lotes, **54 permisos**, tenant demo aprovisionado; segunda vez: ambos scripts se omiten por hash y
> 0 permisos nuevos — idempotente); `scripts/smoke.sh http://localhost:5000` completo, **exit 0, `SMOKE OK`**, sin
> fallos, sobre el árbol de trabajo tal como quedó del commit `532bfc9` ("Lote 5 (en curso): trips y rutas, punto de
> control"). No se hizo push en esta sesión, así que no hay una corrida de CI de GitHub Actions que citar para este
> cierre puntual; el árbitro formal sigue siendo `.github/workflows/ci.yml` en el primer push de estos cambios.
>
> **Sobre los "37 hallazgos corregidos en verificación":** este cierre reporta ese número porque así se indicó al
> encargar el documento, pero a diferencia del Lote 4 (donde cada hallazgo quedó marcado en el código con el comentario
> literal `Hallazgo de revisión` y así se pudo enumerar 1 a 1) **el código y `scripts/smoke.sh` del Lote 5 no traen esa
> marca** — se buscó el literal en todo el árbol y las 40 apariciones existentes son todas de rondas de verificación de
> lotes anteriores (líneas previas a la sección "Lote 5" del script). No se afirma aquí el detalle de esos 37 hallazgos
> porque no hay evidencia en el diff que los enumere; lo que sí se verificó de forma independiente en esta sesión es
> que build, pruebas, `db-init` ×2 y el smoke completo (95 pasos, 25 de ellos del Lote 5) corren en verde sobre el
> estado actual del repositorio.

## Mapa de lo construido

| Área | Tablas (nuevas o extendidas) | Código principal | Endpoints (módulo · permiso) |
|---|---|---|---|
| Planificación diaria y ficha de ruta | `Trip` (+`DispatchZoneId`, FKs compuestas a `Vehicle`/`Driver`, `RowVersion`, `UQ_Trip_IdTenant`, `UQ_Trip_Code`, `CK_Trip_Numbers`), `NumberSequence` (Kind `TRIP`) | `TripPlanningRules`, `TripService`, `TripReadService`, `TripSeams` | `GET/POST /api/v1/trips`, `GET/PATCH/DELETE .../{publicId}`, `POST .../reassign-zone` (LTL_GROUND · `trips.view`/`trips.plan`) |
| Consolidación de órdenes en la ruta | `TripOrder` (+`TenantId`, `IsCurrent`, `UX_TripOrder_Current`, `UQ_TripOrder`) | `TripOrderRequestRules`, `TripOrderService`, `RouteWriter.AddOrdersAsync`/`ReleaseOrderAsync` | `GET /api/v1/trips/unassigned-orders`, `POST/DELETE .../{publicId}/orders(/{orderPublicId})` (LTL_GROUND · `trips.view`/`trips.plan`) |
| Optimización, secuencia y pin manual | `Route` (+`UX_Route_Trip_Active`, `UQ_Route_Version`, `CK_Route_Numbers`), `RouteStop` (+`CK_RouteStop_Numbers`), `OptimizationRun` (`OptimizationRunId` **INT**, FK compuesta a `Trip`, `CK_OptimizationRun_Numbers`) | `HeuristicRoutePlanner`, `RouteEditRules`, `HeuristicRouteOptimizer : IRouteOptimizer`, `RouteOptimizationService` (3 fases), `TripQueries.SetStopPointAsync` | `POST .../{publicId}/optimize`, `PUT .../{publicId}/route/sequence`, `PUT .../{publicId}/stops/{routeStopId}/location`, `GET .../{publicId}/optimization-runs` (LTL_GROUND · `trips.optimize`/`trips.plan`/`trips.view`) |
| Despacho y salida | efectos sobre `Trip`/`Route`/`TripOrder`/`TransportOrder` (sin tabla propia) | `DispatchBatchRules`, `TripDispatchService`, `TripLifecycleService.StartTrackedAsync` (costura reutilizada en el Lote 7), `TripStatusEffect`, `TripOrderReleaseEffect` | `GET .../dispatchable`, `POST .../{publicId}/dispatch`, `POST .../dispatch` (lote), `POST .../{publicId}/start` (LTL_GROUND · `trips.dispatch`) |
| Zonas de despacho | `DispatchZone` (+`UQ_DispatchZone_IdTenant`), `DispatchZoneMember` (+`UQ_DispatchZoneMember`) | `DispatchZoneMatcher` (precedencia CP > rango > municipio), `DispatchZoneService` (extendido del Lote 4) | `GET/POST/DELETE /api/v1/dispatch-zones/{id}/members`, `GET .../resolve` (CATALOG · `fleet.view`/`fleet.manage`) |
| Estación de escaneo Outbound | (reutiliza `TransportOrder`/`OrderStop`/`TripOrder`; sin tabla propia) | `OutboundScanRules` (precedencia found/dup/notfound), `OutboundScanService` (reutiliza `OrderReadService.LookupAsync`) | `POST /api/v1/scan/outbound` (LTL_GROUND · `trips.scan`) |
| Planificar el día | (usa `Trip`/`TripOrder`; sin tabla propia) | `DayPlanningRules`, `TripDayPlanningService` (bloqueo de Tenant + Trips + órdenes, idempotente) | `POST /api/v1/trips/plan-day` (LTL_GROUND · `trips.plan`) |
| Monitoreo | (lee `Trip`/`Route`/`RouteStop`/`DriverLocationPing`; +`IX_LocationPing_Driver`) | `MonitorRules`, `TripMonitorService`, `TripQueries.LastPingsAsync` (con respaldo del ping del chofer) | `GET /api/v1/trips/monitor` (LTL_GROUND · `trips.view`) |
| Transversal | seed: `LookupCode` (`OptimizerEngine.HEURISTIC`, `Capability.EDIT_TRIP`), `EntityType` (`ROUTE`, `ROUTE_STOP`, `OPTIMIZATION_RUN`), `StatusCapability`/`StatusLateralEntry` de `TRIP`/`ROUTE`; 2 permisos nuevos (`trips.view`, `trips.scan`; **54 en total**) | `CatalogDomains`, `PermissionCatalog`, `TripOwnedEntityResolvers` (4 resolvers: `TRIP`, `ROUTE`, `ROUTE_STOP`, `OPTIMIZATION_RUN`), `TripDataSource`, `OrderDataSources` (extendida con `TripCode`/`HasAssignedDriver`/`IsException`/`DispatchZoneCode`), `SystemAnalyticsSeeder` (vista "Rutas", indicadores "Órdenes sin chofer asignado"/"Órdenes en excepción"/"Rutas sobre el máximo de paradas", gráfico "Rutas por estatus") | — |

`TenantId` sale del principal; ninguna solicitud lleva `TenantId` ni ids internos de `Trip`/`Route` (lo prueba
`TripContractsTests` por reflexión). Segunda barrera en SQL con FKs compuestas `(Id, TenantId)` en `TripOrder` → `Trip`
y `TransportOrder`, `Trip` → `Driver`/`Vehicle`/`DispatchZone`, `OptimizationRun` → `Trip`. `Route`, `RouteStop` y
`DispatchZoneMember` no tienen `TenantId` propio: solo se alcanzan por un `Trip` o una `DispatchZone` ya filtrados, y
`TripOwnedEntityResolvers` cierra `ROUTE`/`ROUTE_STOP`/`OPTIMIZATION_RUN` con `IOwnedEntityResolver`. Todo el SQL crudo
del lote (bloqueos `UPDLOCK`/`ROWLOCK`, `GEOGRAPHY`, pin manual y pings) vive confinado en `TripQueries.cs` con
`TenantId =` explícito en cada sentencia — lo prueba `RawSqlConfinementTests`. Defensa en profundidad igual que en
lotes previos: filtro de tenant (datos) + `[RequirePermission]` + `[RequireModule(LTL_GROUND)]` (acción, con `CATALOG`
exigido además dentro del servicio cuando se toca chofer/vehículo) + `IOwnedEntityResolver` (recurso).

Sin dinero en este lote: despachar no crea `DriverTrip`; ningún DTO de trips ni la fuente `TRIP` expone `Amount` ni
`Rate` (lo prueba `TripContractsTests` recorriendo por reflexión todos los records de `TripContracts.cs`).

## Cómo se prueba

1. `dotnet build Teikem.sln -c Release` → compila sin errores (4 advertencias de estilo/xUnit, sin relación con lógica
   de negocio: dos parámetros no usados en `PermissionService`/`AuthController` de lotes anteriores, y dos
   `Assert.False` en `RouteWriterTests` que xUnit sugiere cambiar por `Assert.DoesNotContain`).
2. `dotnet test Teikem.sln` → **979 pruebas, 0 fallos** (incluye, del Lote 5: `TripCatalogTests`, `TripContractsTests`,
   `TripRulesTests`, `EtaCalculatorTests`, `DispatchZoneMatcherTests`, `RouteOptimizationRulesTests`,
   `RoutePlanJsonTests`, `RouteWriterTests`, `RawSqlConfinementTests`, `OrderDispatchFlagsTests`,
   `TripPlanningRulesTests`, `TripOrderRequestRulesTests`, `HeuristicRoutePlannerTests`, `RouteEditRulesTests`,
   `RouteOptimizationServiceTests`, `TripStatusEffectTests`, `DispatchBatchRulesTests`,
   `DispatchZoneMembershipTests`, `OutboundScanRulesTests`, `OutboundScanServiceTests`, `MonitorRulesTests`,
   `DayPlanningRulesTests`, `TripControllerSecurityTests`, `TripServiceTests`, `TripDispatchServiceTests`,
   `TripOwnedEntityResolversTests`; y las de lotes previos ampliadas: `TenantIsolationModelTests`,
   `OrderCatalogTests`, `FleetContractsTests` (con la cola de `OrderDetailDto` ajustada a
   `AssignedDriverPublicId/Code/Name, AssignedTripPublicId/Code`), `OwnedEntityResolverCoverageTests`).
3. `dotnet run --project src/Teikem.Api -- db-init` **dos veces** sobre una base `Teikem` recreada desde cero: la
   primera aplica estructura (~133 tablas, 146 lotes) y seed (15 lotes: 13 módulos, dominios, lookups, estatus,
   capacidades por defecto de `CONTRACT`/`TRANSPORT_ORDER`/`WORK_ORDER`/`TRIP`, entradas laterales de `TRIP`/`ROUTE`,
   **54 permisos**, plantillas de rol y zonas de despacho demo); la segunda omite ambos scripts por hash (0 permisos
   nuevos propagados, tenant demo "ya existe").
4. Con el API arriba, `scripts/smoke.sh http://localhost:5000` recorre los Lotes 1-4 y, antes del paso final de
   sesiones, los **25 pasos del Lote 5**: permisos/catálogos/pipelines/capacidades/laterales; zonas (miembros por CP,
   ZIP+4, rango y municipio, sin solapamiento, resolución); prerrequisitos (clientes, consignatarios, choferes,
   vehículos, órdenes); alta de ruta (número `AAAA-####`, chofer por defecto, salida 12:00 UTC); 8 altas simultáneas
   con números consecutivos; "sin asignar" y agregar órdenes (atómico, sin cambiar `OrderStatus`); concurrencia de
   asignación (misma orden a dos rutas); quitar y liberar (resecuencia, `rowVersion`); pin manual y ETAs (`MANUAL`,
   BOLA por id de parada); optimizar en tres fases (versiones, `CAPACITY_STOPS`, corridas OK/ERROR simultáneas,
   reordenar sin re-optimizar); alerta de máximo de paradas; planificar el día (idempotente, rutas vacías, zonas
   simultáneas); escaneo Outbound (número/empaque/factura, voz, 8 escaneos simultáneos); consolidación multi-cliente
   (mismo número de dos clientes); bloqueantes de despacho vistos desde fuera (chofer no disponible, `ASSIGN_TRIP`
   apagada, etapa `DISPATCHED` deshabilitada); despacho y salida (congela la ruta, `PLANNED`→`IN_TRANSIT`, sin
   `DriverTrip`); cabecera con `EDIT_TRIP` (configurable, contenido siempre congelado); reasignación en bloque;
   eliminar ruta (libera sin retroceder estatus); monitoreo (totales sobre la fecha, búsqueda no los cambia); sin
   dinero (ningún JSON expone `amount`/`rate`); RBAC y módulo; aislamiento y BOLA por id hijo (otro tenant); contactos,
   campos personalizados e historial de rutas (resolvers `TRIP`/`ROUTE`/`ROUTE_STOP`/`OPTIMIZATION_RUN`); fuentes de
   datos, contenido de sistema y auditoría. En total el script tiene **95 pasos** y termina en `SMOKE OK`.

> **Ejecución real de este cierre (2026-09-26, esta sesión):** SQL Server 2022 ya estaba corriendo en el contenedor de
> trabajo (`scripts/dev-sqlserver.sh`, sin Docker). Se recreó la base `Teikem` desde cero (`DROP DATABASE` + `CREATE
> DATABASE`), se corrió `db-init` dos veces (54 permisos la primera vez, idempotente la segunda), se levantó
> `dotnet run --project src/Teikem.Api` y se corrió `scripts/smoke.sh http://localhost:5000` completo: **exit 0,
> `SMOKE OK`**, sin fallos. `dotnet build`/`dotnet test` se corrieron aparte y también en verde (979/979).

### `scripts/smoke.sh`

Ya contiene, en el árbol de trabajo (commit `532bfc9`), los 25 pasos del Lote 5 que pedía el arreglo `"smoke"` del
plan aprobado — verificados uno por uno contra la lista del plan (con títulos ligeramente reformulados: p. ej.
"rutas (Lote 5): prerrequisitos…" en vez de "prerrequisitos (Lote 5)", y "números de ruta concurrentes (Lote 5)" en
vez de "números concurrentes (Lote 5)"), todos presentes e insertados antes del paso final "sesiones: refresh con
rotación y logout" (que ya cerraba el archivo desde el Lote 1 y sigue siendo el último). **No fue necesario agregar
nada al final del archivo en esta pasada**: los 24 pasos de negocio del arreglo `"smoke"` del plan, más el de
consolidación multi-cliente (25 en total), ya estaban integrados; correrlos de punta a punta (ver arriba) confirma
que siguen en verde sobre el estado actual del código.

## Decisiones a revisar

Las 47 decisiones del plan (`docs/lote5-plan.md`, sección "Decisiones") fueron ratificadas por Luis sin cambios el
2026-09-26, **salvo estas 13 que quedaron marcadas "pendientes de ratificación"** (se listan con el número de
decisión del plan y un resumen; el texto completo y su alternativa descartada están en `docs/lote5-plan.md`):

1. **D2 — Asignar una orden a una ruta NO cambia su `OrderStatus`.** "Sin asignar" se deriva de que la orden no tenga
   `TripOrder` vigente. Despachar avanza las órdenes etapa por etapa solo hasta `PLANNED`; la **salida** (`IN_PROGRESS`)
   las lleva a `IN_TRANSIT` (L267: "el estado lo mueven eventos"). Alternativa descartada: `IN_TRANSIT` al despachar
   (como hoy hace la entrega especial del Lote 4).
2. **D4 — Despachar es irreversible.** El "reversible" de L271 se interpreta como la selección del modal antes de
   confirmar, no como un des-despacho posterior. Alternativa descartada: permitir `DISPATCHED → PLANNED` mientras la
   ruta no haya salido.
3. **D6 — La ruta solo secuencia paradas `DELIVERY` pendientes;** las `PICKUP` quedan fuera de la ruta de reparto.
   Alternativa descartada: incluir también la `PICKUP` de órdenes aún no recogidas antes de su entrega.
4. **D14 — Las paradas que no caben al optimizar se LIBERAN** (vuelven a "sin asignar"), con su motivo visible en la
   ficha mientras sigan sin ruta (`LastRunUnassigned`, vía `RoutePlanJson` tolerante). Alternativa descartada: dejarlas
   en el trip, fuera de la ruta, en un panel propio.
5. **D16 — ETA sin motor de ruteo (V8).** Haversine × 1.3 a 35 km/h con coordenadas; 15 min por tramo sin ellas;
   espera hasta el inicio de la ventana; salida por defecto 12:00 UTC de `PlanDate` para que siempre haya ETA.
   Alternativa descartada: constantes configurables por compañía y sin salida por defecto (sin ETA + aviso
   `NO_PLANNED_START`).
6. **D20 — La capacidad del vehículo y el máximo de paradas del chofer solo AVISAN**, nunca bloquean, en ediciones
   manuales, el selector y al despachar; solo el optimizador respeta la capacidad de verdad. Alternativa descartada:
   bloquear el despacho o el alta que exceda la capacidad física.
7. **D22 — La disponibilidad se valida con `FleetAvailabilityService` (V3, Lote 4)**: bloquea (409/422) al asignar y
   al despachar; la doble asignación del mismo día (chofer/vehículo en otra ruta) solo avisa
   (`DRIVER_DOUBLE_BOOKED`/`VEHICLE_DOUBLE_BOOKED`). Alternativa descartada: bloquear también la doble asignación.
8. **D23 — Chofer por defecto al crear una ruta con zona y sin chofer** (también en "Planificar el día"): el que
   tiene esa zona como primaria, solo si es único, está disponible y `CATALOG` está encendido; nunca se asigna
   vehículo por defecto. Alternativa descartada: solo sugerirlo, o tomar también un vehículo por defecto.
9. **D28 — Dos permisos nuevos, `trips.view` y `trips.scan` (54 en total)**, con plantillas Dispatcher/WarehouseOperator
   (view+scan) y ReadOnly (solo view). Alternativa descartada: reutilizar `trips.plan` para leer y escanear.
10. **D33 — Eliminar una ruta la deja en `CANCELLED` con `IsActive=0` y libera sus órdenes**; solo desde
    `DRAFT`/`PLANNED`. Alternativa descartada: permitir eliminar una ruta despachada, mandando sus órdenes a un
    lateral.
11. **D34 — Cancelar una orden, o mandarla a un lateral (`ON_HOLD`/`PARTIAL`/`FAILED`), la libera de una ruta
    abierta**; cancelar una orden que va en una ruta despachada se bloquea (422); un lateral en una ruta despachada no
    cambia nada (decide el Lote 7). Alternativa descartada: no liberar en laterales, dejando la orden en la ruta como
    bloqueante del despacho hasta que el despachador la quite a mano.
12. **D37 — Sin dinero en este lote.** Despachar no crea `DriverTrip`; ningún DTO de trips ni la fuente `TRIP` expone
    `Amount`/`Rate`. Alternativa descartada: un `DriverTrip` de tipo "ruta" por despacho.
13. **D42 — La fecha del plan puede estar entre ayer y +60 días;** la hora de salida debe caer en esa fecha con ±12 h
    de margen. Alternativa descartada: sin restricción de fechas.

Las 34 decisiones restantes del plan (numeración, motor `HEURISTIC`, zonas por CP/rango/municipio, orden de bloqueo
único, optimización en tres fases, `OptimizationRunId` a `INT`, capacidad `EDIT_TRIP`, límites técnicos duros,
definiciones de los indicadores, etc.) quedaron ratificadas sin cambios y no se repiten aquí; el texto completo de
cada una vive en `docs/lote5-plan.md`.

## Lo que queda fuera de este lote

- (Resuelto en una pasada posterior a este cierre, ver la nota al inicio del documento) El capítulo del manual
  funcional y la ampliación de la FAQ ya no quedan fuera del lote.
- Cierre de ruta (`Trip`→`COMPLETED`), eventos por parada, POD y pings en vivo: quedan para el Lote 7, que también
  reutilizará `TripLifecycleService.StartTrackedAsync` para lo que sigue después de la salida.
- Geocodificador (ZIP → centroide u otro) que resuelva `GeocodeAccuracy` automáticamente; en este lote solo hay pin
  manual (`MANUAL`).
- Motores de ruteo reales (VROOM, OSRM, OR-Tools) detrás de `IRouteOptimizer`; el único motor de este lote es
  `HEURISTIC`, determinista y sin llamadas externas.
- Cross-dock y `DockAppointment` (anotado para el Lote 6 en el plan).
- Unicidad de `FleetAssignment` (hueco heredado del Lote 4, no tocado aquí).
- Almacenes / `OriginWarehouseId` de `Trip`: la columna está mapeada pero nunca se escribe en este lote.
- Dinero: sin `DriverTrip` por despacho, sin tarifas ni montos en ningún contrato o fuente de trips (decisión D37,
  ver arriba).
- `StatusService` resolviendo el regreso lateral por dominio en vez de solo por `(EntityType, EntityId)`: riesgo
  heredado de lotes anteriores, no tocado en este lote; `TripStatus`/`RouteStatus`/`RouteStopStatus`/
  `OptimizationRunStatus` comparten esa superficie con `OrderStatus`/`DriverTripStatus`/`WorkOrderStatus`.
- **Detalle línea por línea de los "37 hallazgos corregidos en verificación"**: como se explica al inicio de este
  documento, el código no trae la marca `Hallazgo de revisión` para el Lote 5 (a diferencia del Lote 4), así que no se
  pudo enumerar cada uno con evidencia propia en esta pasada; lo verificado de forma independiente fue que build,
  pruebas (979/979), `db-init` ×2 y el smoke completo (95 pasos) corren en verde sobre el estado actual del repositorio.

---

**Verificación del cierre (2026-09-26, sobre BD recreada desde cero):** `dotnet build -c Release` sin errores, `dotnet test` 979/979, `db-init` dos veces (54 permisos, idempotente) y `scripts/smoke.sh` completo de los Lotes 1 a 5 en `SMOKE OK` (97 pasos). Corrida de CI: ver enlace al final.

**Corrida de CI del cierre del Lote 5:** https://github.com/lcasado-cerevelo/teikem/actions/runs/36252312568 (run 29, verde: build Release, 979 pruebas, db-init ×2 sobre BD limpia y smoke de los Lotes 1 a 5).
