# Lote 5 — Trips y rutas: plan de implementación (implementado con los defaults del panel; decisiones pendientes de ratificación)

Resultado del workflow `lote-diseno` (3 lectores, crítico de completitud, 3 arquitectos, 2 jueces y síntesis).
Puntajes de los jueces por diseño: mínimo viable 14, riesgo primero 17, extensibilidad 15.

## Decisiones pendientes de ratificación por Luis

- D4: despachar es irreversible; lo 'reversible' del documento se interpreta como la selección en el modal de despacho.
- D2: asignar una orden a una ruta no cambia su estatus; despachar la lleva a PLANNED y la salida (POST /trips/{id}/start) a IN_TRANSIT; liberar nunca retrocede.
- D6: la ruta secuencia solo paradas de ENTREGA; las paradas de recogido quedan fuera de la ruta en este lote.
- D14: las órdenes que no caben al optimizar (capacidad del vehículo) vuelven a 'sin asignar' con su motivo visible.
- D20/D22: capacidad del vehículo, máximo de paradas del chofer y 'chofer o vehículo en dos rutas el mismo día' solo AVISAN; la disponibilidad del Lote 4 sí bloquea al asignar y al despachar.
- D23: chofer por defecto de una ruta nueva = el que tiene esa zona como primaria, solo si es único y está disponible; no se asigna vehículo por defecto.
- D16/D42: sin hora de salida se asume 08:00 AST (12:00 UTC) para calcular ETAs; la fecha del plan puede ir de ayer a +60 días.
- D33/D34: eliminar una ruta = CANCELLED con baja lógica y libera sus órdenes (solo antes de despachar); cancelar una orden que va en una ruta despachada responde 422.
- D37: despachar no genera dinero (no crea DriverTrip); el pago por entrega lo calculará Liquidación (Lote 9) a partir de las entregas.
- D28: dos permisos nuevos trips.view y trips.scan (54 en total); Operador de almacén puede ver y escanear pero no planificar.

## Enfoque

RIESGO PRIMERO. El Lote 5 construye el módulo 3 del documento maestro, Trips y rutas: planificación diaria, consolidación, optimización, edición manual, despacho, salida, zonas de despacho, escaneo Outbound y monitoreo. El plan parte del plan ganador (índice 1) y le injerta las ideas que señalaron los jueces (lista al final). Todo gira alrededor de cuatro riesgos. Cada uno lleva una guarda en código, una guarda de última línea en SQL cuando existe, y una prueba o un paso de smoke.

(1) INTEGRIDAD MULTI-TENANT
- El TenantId sale del principal. Ninguna solicitud lleva TenantId ni ids internos de Trip.
- FKs compuestas (Id, TenantId):
  - TripOrder → Trip y TransportOrder;
  - Trip → Driver, Vehicle y DispatchZone;
  - OptimizationRun → Trip.
- UQ_Trip_IdTenant y UQ_DispatchZone_IdTenant son nuevas.
- Route, RouteStop y DispatchZoneMember no tienen TenantId. Solo se alcanzan por un Trip o una DispatchZone ya filtrados.
- Todo el SQL crudo va confinado en TripQueries con 'TenantId =' explícito: GEOGRAPHY, pings, bloqueos UPDLOCK y pin manual. RawSqlConfinementTests lo prueba.
- Resolvers de pertenencia para TRIP, ROUTE, ROUTE_STOP y OPTIMIZATION_RUN.
- El smoke prueba BOLA por id hijo: RouteStopIds, órdenes y paradas de otra ruta u otro tenant en secuencia, pin, quitar y despachar.

(2) TRANSICIONES DE ESTATUS
- StatusService no retrocede etapas. Por eso asignar una orden a una ruta NO cambia su OrderStatus, y liberarla (R15) nunca retrocede.
- Despachar (Trip → DISPATCHED):
  - congela la ruta (Route → ACTIVE);
  - avanza las órdenes etapa por etapa hasta PLANNED.
- La salida (Trip → IN_PROGRESS) lleva las órdenes a IN_TRANSIT. La ejecuta TripLifecycleService.StartTrackedAsync, que reutilizará la app del Lote 7. Es fiel a L267: 'el estado lo mueven eventos (salida, llegada, POD)'.
- TripStatusEffect aplica esos efectos venga de donde venga la transición.
- Eliminar una ruta solo es posible desde DRAFT o PLANNED: entrada lateral sembrada más guarda dura en el efecto.
- Ruta despachada:
  - el CONTENIDO (paradas y secuencia) queda congelado por regla dura en RouteWriter;
  - la CABECERA (chofer, vehículo, hora de salida) la gobierna la capacidad EDIT_TRIP sembrada en StatusCapability, negada desde DISPATCHED. Es el patrón configurable por tenant de L65/L206.
- Si el tenant deshabilita DISPATCHED o ACTIVE en su pipeline, la respuesta es un 422 explícito.
- Cancelar o mandar a lateral una orden la libera de una ruta abierta. Cancelar una orden que va en una ruta despachada se bloquea.

(3) CONCURRENCIA
- Toda mutación de ruta pasa por RouteWriter, con transacción y el Trip bloqueado (UPDLOCK, ROWLOCK, TenantId).
- Hay un ORDEN DE BLOQUEO ÚNICO en todo el lote:
  1. la fila Tenant, solo en 'Planificar el día';
  2. los Trips, por TripId ascendente;
  3. las órdenes, por TransportOrderId ascendente, en una sola sentencia (TripQueries.LockOrdersAsync);
  4. después se escribe.
- Los efectos de OrderStatus respetan ese orden porque la orden solo se escribe al guardar, después de bloquear el Trip.
- Hay re-verificación dentro de la transacción después de bloquear ruta y órdenes: al agregar, al escanear y al despachar.
- Optimización en TRES FASES:
  1. instantánea y corrida PENDING dentro de una transacción;
  2. el motor corre FUERA de la transacción, sin retener UPDLOCK;
  3. el resultado se aplica solo si la ruta vigente y el RowVersion del Trip no cambiaron. Si cambiaron, la corrida queda en ERROR y se responde 409.
  El ERROR siempre se escribe en una transacción aparte.
- Últimas líneas en BD: UX_TripOrder_Current, UX_Route_Trip_Active, UQ_Route_Version, UQ_DispatchZoneMember y UQ_Trip_Code.
- El RowVersion del Trip cambia en cada mutación. SaveGuardedAsync traduce los choques a 409.
- El smoke dispara en paralelo:
  - 2 altas de la misma orden en rutas distintas;
  - 2 optimizaciones;
  - 8 escaneos;
  - 8 POST /trips (deben dar códigos distintos y consecutivos);
  - 2 'Planificar el día' (debe quedar una sola ruta por zona).

(4) DINERO Y NÚMEROS
- Sin dinero: despachar no crea DriverTrip. Ningún DTO de trips ni la fuente TRIP expone Amount ni Rate; lo verifica una prueba de contratos.
- Distancias en DECIMAL(12,3), validadas con FleetRules.DecimalError, más CHECKs de no negatividad.
- Topes técnicos:
  - 300 paradas por ruta;
  - 200 órdenes por solicitud;
  - 50 rutas por despacho en lote;
  - 50 zonas por reasignación o planificación.
- La ETA es una fórmula pura y probada. La hora de salida por defecto es las 12:00 UTC (08:00 AST) de PlanDate, para que siempre haya ETA.

Toda regla de negocio es pura y se prueba sin BD: elegibilidad, alertas, ETA, zona, heurística, validación del optimizador, secuencia, decisión del escaneo, planificación del día y progreso del monitor.

ORGANIZACIÓN
- P0 es la ÚNICA pieza con archivos compartidos y se integra primero. Es más liviana que en el plan ganador: la extensión de la fuente Órdenes pasa a P7.
- P0 entrega:
  - SQL y seed completos;
  - constantes y permisos;
  - entidades y configuraciones;
  - DTOs con firma posicional completa;
  - reglas puras;
  - costuras implementadas: TripQueries, RouteWriter, TripIssueBuilder, IRouteOptimizer y TripSeams;
  - resolvers, DI, contenido de análisis y esqueletos compilables.
- Los esqueletos los crea P0 solo para que DI compile. Cada archivo pertenece a su pieza y aparece únicamente en la lista de esa pieza.
- P1 a P7 y P9 se implementan en paralelo con archivos disjuntos. P9 se programa contra las firmas de TripSeams y se integra después de P1 y P2.
- P8 cierra el lote.

CONVENCIONES DEL LOTE
- Controladores y pruebas NO importan Teikem.Domain.Trips. El SDK Web trae 'using Microsoft.AspNetCore.Routing' implícito y su clase Route choca con la entidad. Donde haga falta: 'using TripRoute = Teikem.Domain.Trips.Route;'.
- Módulo LTL_GROUND (núcleo), como Órdenes. Los servicios exigen además CATALOG cuando tocan choferes o vehículos. Zonas y miembros siguen en CATALOG.
- Los mensajes exactos viven como 'public const' en las reglas puras, porque el manual y la FAQ los citan.

HALLAZGO AL VERIFICAR EL CÓDIGO
FleetContractsTests.Order_detail_exposes_the_assigned_driver_without_amounts fija que los TRES últimos parámetros de OrderDetailDto sean AssignedDriver*. Agregar AssignedTripPublicId/AssignedTripCode al final lo rompe. Por eso P0 actualiza esa prueba: el trío AssignedDriver* seguido de AssignedTrip*, sin Amount ni Rate.

INJERTOS (idea → pieza)
1. Planificar el día por zona, idempotente, con CreateEmptyTrips (L262) → P9.
2. Despachar lleva las órdenes a PLANNED; la salida, a IN_TRANSIT, con TripLifecycleService → P4.
3. Pin manual por parada (GeocodeAccuracy MANUAL) con BOLA y recálculo de ETA → P3, más SetStopPointAsync en P0.
4. Capacidad EDIT_TRIP para la cabecera → seed en P0 y regla en P1.
5. Optimización en tres fases → P3.
6. OptimizationRunId pasa a INT → P0.
7. Escaneo con OrderReadService.LookupAsync (número > empaque > factura) más OutboundScanRules.Decide y re-verificación → P6.
8. Alias TripRoute → convención.
9. P0 aligerado: la fuente Órdenes pasa a P7.
10. Smoke de bloqueantes de despacho vistos desde fuera → P8. Se adaptó: como ON_HOLD libera la orden, la orden no elegible se provoca apagando ASSIGN_TRIP.
11. Sin Amount/Rate en contratos ni en la fuente TRIP → P0 y P7.
12. Monitor con totales sobre la fecha, búsqueda después de los filtros y ping de respaldo del chofer → P7, más LastPingsAsync en P0.
13. 8 POST /trips simultáneos y pruebas de liberación → P8 y P0.
14. Orden de bloqueo global → P0.
15. RoutePlanJson tolerante, para mostrar en la ficha el motivo de las paradas que no entraron → P0, P1 y P3.
16. Hora de salida por defecto 12:00 UTC → P1 y P9.

## Resumen

El Lote 5 entrega el módulo de Trips y rutas.

**Planificación diaria**
- Rutas (Trip) por fecha y zona.
- Número automático AAAA-####.
- Chofer por defecto: el que tiene esa zona como primaria, si es único y está disponible.
- Vehículo y hora de salida. Si no se indica, la salida es a las 12:00 UTC de la fecha.
- La disponibilidad del Lote 4 se valida al asignar y al despachar.
- 'Planificar el día' (POST /trips/plan-day) agrupa por zona las órdenes sin asignar de la fecha:
  - usa la ruta abierta de la zona o crea una con el chofer estándar;
  - es idempotente;
  - con CreateEmptyTrips deja una ruta destino para el escaneo aunque la zona no tenga órdenes.

**Consolidación multi-cliente**
- Agregar órdenes confirmadas a una ruta es atómico.
- La lista 'Sin asignar' trae la zona resuelta por CP o pueblo.
- Quitar una orden, o eliminar la ruta, la libera sin tocar su estatus.

**Optimización**
- Pasa por la costura IRouteOptimizer. El motor de este lote es HEURISTIC, determinista: ordena por zona y ventana y respeta la capacidad del vehículo en paradas, peso y volumen.
- Corre en tres fases: el motor trabaja fuera de la transacción y el resultado se aplica solo si la ruta no cambió.
- Cada corrida crea una versión de Route, archiva la anterior y deja un OptimizationRun.
- Lo que no cabe vuelve a 'sin asignar', con su motivo visible en la ficha.

**Edición manual**
- Reordenar paradas recalcula las ETAs sin re-optimizar. Fórmula: haversine × 1.3 a 35 km/h, o 15 min por tramo; más espera de ventana y tiempo de servicio.
- Pin manual por parada (MANUAL), que también recalcula las ETAs.

**Alertas no bloqueantes**
- Máximo de paradas del chofer.
- Capacidad del vehículo.
- Ventanas tardías.
- Pines aproximados.
- Chofer o vehículo en dos rutas el mismo día.

**Despacho**
- Selector de rutas no despachadas, con los motivos que bloquean.
- Despacho individual o en lote (cada ruta por separado).
- Al despachar, la ruta se congela (ACTIVE) y las órdenes avanzan hasta PLANNED.
- La 'Salida' (POST /trips/{id}/start, la misma costura que usará el Lote 7) pasa la ruta a IN_PROGRESS y las órdenes a IN_TRANSIT.
- La cabecera de una ruta despachada se puede editar solo si el tenant habilita EDIT_TRIP.

**Zonas de despacho**
- Miembros por CP, rango postal o municipio.
- Sin solapamiento entre zonas activas.
- Resolución por precedencia: CP > rango > municipio.
- Reasignación en bloque a otro chofer de las rutas abiertas de una o varias zonas.

**Estación de escaneo Outbound**
- Usa el mismo lookup que /orders/lookup: número > empaque > factura.
- Asigna la orden a la ruta abierta del día de su zona.
- Devuelve un resultado tipado y la palabra de voz: found, dup o notfound.

**Monitoreo**
- Rutas despachadas o en curso, con progreso, próxima ETA y último ping. Si el ping no trae TripId, se usa el del chofer desde la salida.
- Los totales se calculan sobre la fecha; la búsqueda solo filtra la lista.
- La ficha expone paradas con coordenadas y precisión de geocodificación. El mapa lo dibuja el front.

**Esquema**
- Trip: DispatchZoneId, FKs compuestas y auditoría.
- TripOrder: TenantId, IsCurrent y UX_TripOrder_Current.
- Route: UX_Route_Trip_Active y UQ_Route_Version.
- CHECKs en Trip, Route, RouteStop y OptimizationRun.
- UQ_DispatchZoneMember.
- OptimizationRunId pasa a INT.
- IX_LocationPing_Driver.
- NumberSequence TRIP.

**Seed**
- Motor HEURISTIC.
- EntityType ROUTE_STOP y OPTIMIZATION_RUN.
- Capacidad EDIT_TRIP, negada desde DISPATCHED.
- Entradas laterales de TRIP y ROUTE.
- Permisos trips.view y trips.scan (54 en total).

**Análisis**
- Fuente TRIP.
- La fuente Órdenes gana ruta, chofer asignado, zona y excepción.
- Contenido de sistema:
  - vista 'Rutas';
  - indicadores 'Órdenes sin chofer asignado', 'Órdenes en excepción' y 'Rutas sobre el máximo de paradas';
  - gráfico 'Rutas por estatus'.

**Fuera del lote**
- Cierre de ruta, eventos por parada, POD y pings: Lote 7.
- Geocodificador.
- VROOM, OSRM y OR-Tools.
- Cross-dock y DockAppointment.
- Dinero.

## Piezas

### P0 — Base compartida: SQL, seed, constantes, permisos, entidades, configuraciones, DbContext, DTOs con firma posicional completa, reglas puras, costuras implementadas (TripQueries, RouteWriter, TripIssueBuilder, IRouteOptimizer, TripSeams), resolvers, DI, contenido de análisis, esqueletos y pruebas de modelo

- Toca archivos compartidos: sí. Depende de: nada.

Es la ÚNICA pieza con archivos compartidos y se integra primero. Al terminar debe quedar:
- la solución compilando;
- las 495 pruebas previas y las nuevas de P0 en verde;
- los smokes de los Lotes 1 a 4 en verde sobre una BD recreada.

El script de estructura se aplica una sola vez por hash, así que P0 entrega TODO el SQL y el seed del lote.

Los esqueletos de (14) los crea P0 solo para que DI compile. Esos archivos NO están en esta lista: pertenecen a la pieza que los reemplaza.

**(1) SQL y seed.** Se aplica 'cambiosSql' completo, inline, con '-- Lote 5'.
- UQ_Trip_IdTenant va en el CREATE de Trip, antes de TripOrder y OptimizationRun.
- La FK Trip→DispatchZone se agrega con ALTER TABLE después del CREATE de DispatchZone.
- Las UQ (Id, TenantId) de Vehicle, Driver y TransportOrder ya existen.

**(2) CatalogDomains.cs**
- LookupDomains: ZoneMatchType, OptimizerEngine. GeocodeAccuracy ya existe.
- StatusDomains: TripStatus, RouteStatus, RouteStopStatus, OptimizationRunStatus.
- Clases de códigos:
  - TripStatuses (DRAFT, PLANNED, DISPATCHED, IN_PROGRESS, COMPLETED, CANCELLED)
  - RouteStatuses (DRAFT, OPTIMIZED, ACTIVE, ARCHIVED)
  - RouteStopStatuses (PENDING, ON_THE_WAY, ARRIVED, COMPLETED, FAILED)
  - OptimizationRunStatuses (PENDING, OK, ERROR)
  - ZoneMatchTypes (POSTAL_CODE, POSTAL_RANGE, MUNICIPALITY, POLYGON)
  - OptimizerEngines (VROOM, ORTOOLS, MANUAL, HEURISTIC)
  - GeocodeAccuracies (EXACT, ZIP_CENTROID, CITY_CENTROID, MANUAL)
- Capabilities.EditTrip = 'EDIT_TRIP'.
- EntityTypes: Route='ROUTE', RouteStop='ROUTE_STOP', OptimizationRun='OPTIMIZATION_RUN'. Trip ya existe.
- NumberKinds.Trip = 'TRIP'.

**(3) PermissionCatalog.cs** pasa de 52 a 54 permisos; se actualiza el comentario de cabecera.
- Nuevos, en categoría TRIPS:
  - TripsView = 'trips.view' ('Ver rutas y despacho' / 'View trips & dispatch')
  - TripsScan = 'trips.scan' ('Escanear salida (Outbound)' / 'Scan outbound')
- OwnerReadPermission: TRIP, ROUTE, ROUTE_STOP y OPTIMIZATION_RUN → trips.view.
- OwnerWritePermission: TRIP, ROUTE y ROUTE_STOP → trips.plan.
- RoleTemplates:
  - Dispatcher += TripsView, TripsScan
  - WarehouseOperator += TripsView, TripsScan
  - ReadOnly += TripsView

**(4) NumberingRules.IsKnownKind** acepta TRIP.

**(5) Entidades**, mapeadas 1:1 con el SQL. Van en el namespace Teikem.Domain.Trips; los controladores no lo importan (convención TripRoute).

Trip ([AuditEntity(TRIP)]; ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped):
- TripId, [NotAudited] PublicId, TenantId, Code, PlanDate (DateOnly), DispatchZoneId?
- OriginWarehouseId?: mapeado, nunca se escribe en este lote.
- VehicleId?, DriverId?, StatusCodeId
- PlannedStartUtc?, [NotAudited] PlannedEndUtc?, ActualStartUtc?, ActualEndUtc?
- [NotAudited] TotalDistanceKm?, [NotAudited] TotalDurationMin?
- IsActive; CreatedAtUtc/CreatedBy/UpdatedAtUtc/UpdatedBy (los timestamps [NotAudited]); [NotAudited] RowVersion
- Navegaciones: Status, DispatchZone, Driver, Vehicle, Orders, Routes.

TripOrder ([AuditEntity(TRIP)]; ITenantScoped):
- TripOrderId, TenantId, TripId, TransportOrderId, SortHint?, IsCurrent, [NotAudited] AssignedAtUtc, AssignedBy?

Route ([AuditEntity(ROUTE)]; ISoftDeletable = versión vigente, IHasStatus; sin TenantId):
- RouteId, TripId, Version, IsActive, StatusCodeId
- [NotAudited] TotalDistanceKm?, TotalDurationMin?, StopCount, CreatedAtUtc, RowVersion

RouteStop ([AuditEntity(ROUTE_STOP)]; IHasStatus; sin TenantId):
- RouteStopId, RouteId, OrderStopId, Sequence, StatusCodeId, ActualArrivalUtc?, ActualDepartureUtc?
- [NotAudited]: PlannedArrivalUtc?, PlannedDepartureUtc?, DistanceFromPrevKm?, DurationFromPrevMin?

OptimizationRun (ITenantScoped, IHasStatus; sin [AuditEntity], porque ya es bitácora):
- **int** OptimizationRunId, TenantId, TripId?, RouteId?, EngineLookupId, StatusCodeId
- RequestJson, ResponseJson?, ErrorMessage?, TotalDistanceKm?, TotalDurationMin?, UnassignedCount?
- StartedAtUtc, CompletedAtUtc?, CreatedBy?

DispatchZoneMember (en DispatchZone.cs; [AuditEntity(DISPATCH_ZONE)]; sin TenantId):
- DispatchZoneMemberId, DispatchZoneId, MatchTypeLookupId, MatchValue.
- DispatchZone gana Members.

**(6) Configuraciones.** TripConfigurations.cs (Trip, TripOrder, Route, RouteStop, OptimizationRun) y FleetConfigurations.cs (+DispatchZoneMember).
- ToTable con el nombre exacto de la tabla.
- PublicId con HasDefaultValueSql('NEWID()'); RowVersion con IsRowVersion en Trip y Route.
- DateOnly en PlanDate; decimal(12,3) en todas las distancias.
- Relaciones por columna simple con NoAction. Las FKs compuestas del SQL no se mapean.
- Índices espejo, con el mismo nombre y filtro:
  - UQ_Trip_Code
  - UQ_TripOrder
  - UX_TripOrder_Current '[IsCurrent] = 1'
  - UX_Route_Trip_Active '[IsActive] = 1'
  - UQ_Route_Version
  - UQ_RouteStop
  - UQ_DispatchZoneMember
- No se mapean GeoPoint ni DriverLocationPing.

**(7) TeikemDbContext.** DbSets Trips, TripOrders, Routes, RouteStops, OptimizationRuns y DispatchZoneMembers. El filtro de tenant es automático para Trip, TripOrder y OptimizationRun.

**(8) Contracts.** Firma posicional COMPLETA: son records sealed y ninguna pieza la cambia. '+Extra' significa [JsonExtensionData] IDictionary<string, JsonElement>? Extra.

TripContracts.cs (comunes y ficha):
- TripIssueDto(string Code, string Message, bool Blocking)
- GeoPointDto(double Lat, double Lng)
- DriverPingDto(GeoPointDto Point, decimal? SpeedKmh, int? HeadingDeg, DateTime CapturedAtUtc, DateTime ReceivedAtUtc, bool LinkedToTrip)
- TripListQuery(DateOnly? Date=null, DateOnly? From=null, DateOnly? To=null, string[]? Status=null, int? DispatchZoneId=null, Guid? DriverPublicId=null, string? Search=null, bool IncludeCancelled=false)
- TripListItemDto(int Id, Guid PublicId, string Code, DateOnly PlanDate, int? DispatchZoneId, string? ZoneCode, string? ZoneName, Guid? DriverPublicId, string? DriverCode, string? DriverName, Guid? VehiclePublicId, string? VehicleCode, string StatusCode, string Status, string? StatusColor, bool IsEditable, int? RouteVersion, string? RouteStatusCode, int StopCount, int? EffectiveMaxStops, bool OverStopLimit, decimal? TotalDistanceKm, int? TotalDurationMin, DateTime? PlannedStartUtc, DateTime? PlannedEndUtc, bool IsActive)
- RouteStopDto(int Id, int Sequence, int OrderStopId, Guid OrderPublicId, string OrderNumber, string PackBatchNumber, string ClientName, string? ConsigneeName, string Line1, string? Line2, string City, string? PostalCode, string? ZoneCode, GeoPointDto? Point, string? GeocodeAccuracyCode, string? GeocodeAccuracy, bool IsApproximate, DateTime? WindowStartUtc, DateTime? WindowEndUtc, int ServiceMinutes, DateTime? PlannedArrivalUtc, DateTime? PlannedDepartureUtc, decimal? DistanceFromPrevKm, int? DurationFromPrevMin, bool LateForWindow, int? Pieces, decimal? WeightKg, decimal? VolumeM3, string StatusCode, string Status, DateTime? ActualArrivalUtc, DateTime? ActualDepartureUtc)
- UnassignedStopDto(Guid OrderPublicId, string OrderNumber, string ReasonCode, string Reason)
- TripDetailDto(int Id, Guid PublicId, string Code, DateOnly PlanDate, int? DispatchZoneId, string? ZoneCode, string? ZoneName, Guid? DriverPublicId, string? DriverCode, string? DriverName, Guid? VehiclePublicId, string? VehicleCode, string? VehiclePlate, string StatusCode, string Status, bool IsEditable, bool CanEditHeader, bool IsTerminal, int? RouteId, int? RouteVersion, string? RouteStatusCode, string? RouteStatus, int StopCount, int? EffectiveMaxStops, bool OverStopLimit, decimal TotalWeightKg, decimal TotalVolumeM3, decimal? TotalDistanceKm, int? TotalDurationMin, DateTime? PlannedStartUtc, DateTime? PlannedEndUtc, DateTime? ActualStartUtc, DateTime? ActualEndUtc, IReadOnlyList<RouteStopDto> Stops, IReadOnlyList<TripIssueDto> Issues, IReadOnlyList<UnassignedStopDto> LastRunUnassigned, DriverPingDto? LastPing, bool IsActive, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, string RowVersion)

TripContracts.cs (planificación y órdenes):
- TripCreateRequest(DateOnly? PlanDate, int? DispatchZoneId=null, Guid? DriverPublicId=null, Guid? VehiclePublicId=null, DateTime? PlannedStartUtc=null)
- TripPatchRequest(DateOnly? PlanDate=null, int? DispatchZoneId=null, bool? ClearZone=null, Guid? DriverPublicId=null, bool? ClearDriver=null, Guid? VehiclePublicId=null, bool? ClearVehicle=null, DateTime? PlannedStartUtc=null, bool? ClearPlannedStart=null, string? RowVersion=null) +Extra
- TripCancelRequest(string? Comment=null, string? RowVersion=null)
- ZoneReassignRequest(DateOnly? PlanDate, IReadOnlyList<int>? DispatchZoneIds, Guid? DriverPublicId)
- ZoneReassignResultDto(Guid DriverPublicId, string DriverCode, string DriverName, int TripsUpdated, IReadOnlyList<TripListItemDto> Trips, IReadOnlyList<TripIssueDto> Issues)
- PlanDayRequest(DateOnly? PlanDate, IReadOnlyList<int>? DispatchZoneIds=null, bool CreateEmptyTrips=false)
- PlanDayZoneResultDto(int DispatchZoneId, string ZoneCode, Guid? TripPublicId, string? TripCode, bool TripCreated, int OrdersAssigned, int OrdersSkipped, IReadOnlyList<TripIssueDto> Issues)
- PlanDayResultDto(DateOnly PlanDate, int TripsCreated, int OrdersAssigned, int OrdersSkipped, int OrdersWithoutZone, IReadOnlyList<PlanDayZoneResultDto> Zones)
- TripOrdersAddRequest(IReadOnlyList<Guid>? OrderPublicIds, string? RowVersion=null)
- TripOrderRemoveRequest(string? RowVersion=null)
- UnassignedOrdersQuery(int? DispatchZoneId=null, bool NoZone=false, string? PostalCode=null, string? City=null, Guid? ClientPublicId=null, DateOnly? RequestedFrom=null, DateOnly? RequestedTo=null, string? Search=null, int Skip=0, int Take=100)
- UnassignedOrderDto(int Id, Guid PublicId, string OrderNumber, string PackBatchNumber, string ClientInvoiceNumber, string ClientName, string? ConsigneeName, string City, string? PostalCode, int? DispatchZoneId, string? ZoneCode, bool ZoneAmbiguous, string StatusCode, string Status, DateTime? RequestedDate, DateTime? WindowStartUtc, DateTime? WindowEndUtc, int? Pieces, decimal? WeightKg, decimal? VolumeM3, GeoPointDto? Point, string? GeocodeAccuracyCode)
- UnassignedOrderPageDto(int Total, int Skip, int Take, IReadOnlyList<UnassignedOrderDto> Items)

TripContracts.cs (optimización, secuencia y pin):
- RouteSequenceRequest(IReadOnlyList<int>? RouteStopIds, string? RowVersion=null)
- StopLocationRequest(double? Lat, double? Lng, string? RowVersion=null)
- OptimizeRequest(string? RowVersion=null)
- OptimizationResultDto(int RunId, string EngineCode, string StatusCode, int RouteVersion, int AssignedCount, int UnassignedCount, IReadOnlyList<UnassignedStopDto> Unassigned, string? Polyline, TripDetailDto Trip)
- OptimizationRunDto(int Id, string EngineCode, string Engine, string StatusCode, string Status, int? RouteId, int? RouteVersion, int? UnassignedCount, decimal? TotalDistanceKm, int? TotalDurationMin, string? ErrorMessage, DateTime StartedAtUtc, DateTime? CompletedAtUtc, string? StartedBy)

TripContracts.cs (despacho, salida, monitoreo y escaneo):
- DispatchableTripDto(TripListItemDto Trip, bool CanDispatch, IReadOnlyList<TripIssueDto> Issues)
- TripDispatchRequest(string? Comment=null, string? RowVersion=null)
- TripBatchDispatchRequest(IReadOnlyList<Guid>? TripPublicIds, string? Comment=null)
- TripDispatchResultItemDto(Guid TripPublicId, string? Code, bool Dispatched, string? Error, IReadOnlyList<TripIssueDto> Issues)
- TripBatchDispatchResultDto(int Requested, int Dispatched, IReadOnlyList<TripDispatchResultItemDto> Items)
- TripStartRequest(string? Comment=null, string? RowVersion=null)
- MonitorQuery(DateOnly? Date=null, int? DispatchZoneId=null, string? Search=null, bool IncludeCompleted=true)
- MonitorTripDto(Guid PublicId, string Code, DateOnly PlanDate, string? ZoneCode, string? DriverCode, string? DriverName, string? VehicleCode, string StatusCode, string Status, int TotalStops, int CompletedStops, int FailedStops, int PendingStops, int ApproximateStops, DateTime? NextEtaUtc, DateTime? PlannedEndUtc, bool OverStopLimit, DriverPingDto? LastPing)
- MonitorTotalsDto(int Trips, int TotalStops, int CompletedStops, int FailedStops, int PendingStops, int OverStopLimitTrips, int TripsWithoutPing)
- MonitorDto(DateOnly Date, MonitorTotalsDto Totals, IReadOnlyList<MonitorTripDto> Trips)
- OutboundScanRequest(string? Code, DateOnly? PlanDate=null)
- OutboundScanResultDto(string Outcome, string Voice, string Message, string? MatchedBy, Guid? OrderPublicId, string? OrderNumber, string? PackBatchNumber, string? ZoneCode, Guid? TripPublicId, string? TripCode, string? ReasonCode)

DriverContracts.cs (agrega; DispatchZoneDto no cambia):
- DispatchZoneMemberDto(int Id, string MatchTypeCode, string MatchType, string MatchValue)
- DispatchZoneMembersDto(int ZoneId, string Code, string? Name, bool IsActive, IReadOnlyList<DispatchZoneMemberDto> Members)
- DispatchZoneMemberRequest(string? MatchType, string? MatchValue)
- ZoneResolutionDto(string? PostalCode, string? City, int? DispatchZoneId, string? ZoneCode, string? MatchedBy, bool Ambiguous, IReadOnlyList<string> Candidates)

OrderContracts.cs:
- OrderDetailDto agrega al final 'Guid? AssignedTripPublicId = null, string? AssignedTripCode = null'.
- FleetContractsTests.Order_detail_exposes_the_assigned_driver_without_amounts se actualiza: los últimos 5 parámetros son AssignedDriverPublicId, AssignedDriverCode, AssignedDriverName, AssignedTripPublicId y AssignedTripCode, todos con default; siguen prohibidos Amount y Rate. Sin este ajuste la prueba del Lote 4 falla.

Ninguna solicitud lleva TenantId ni ids internos de Trip.

**(9) Reglas puras (Domain/Trips).**

TripRules:
- EditableStatuses = {DRAFT, PLANNED}; IsEditable(code).
- NotEditableMessage(tripCode, statusCode):
  - DISPATCHED/IN_PROGRESS → 'La ruta {código} ya fue despachada; no se puede editar ni eliminar.'
  - COMPLETED/CANCELLED → 'La ruta {código} está cerrada; solo se consulta.'
- Mensajes de órdenes:
  - NotInTripMessage = 'La orden no está en esta ruta.'
  - AlreadyInTripMessage = 'La orden ya está en esta ruta.'
  - OrderInOtherTripMessage(code) = 'La orden ya está asignada a la ruta {código}.'
  - OrderTakenMessage = 'La orden ya está asignada a otra ruta.'
  - EligibilityHeader = 'Hay órdenes que no se pueden asignar a la ruta.'
- Límites y secuencia:
  - MaxStopsHardCap = 300, con 'Una ruta admite como máximo 300 paradas.'
  - SequenceMessage = 'La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.'
  - StopNotInTripMessage = 'Parada no encontrada en esta ruta.'
- CheckEligibility(OrderEligibilityInput(string OrderNumber, bool IsActive, bool IsSpecialDelivery, string StatusCode, string StatusLabel, string StageKind, bool IsInitial, int SortOrder, int InTransitSortOrder, bool AssignTripAllowed, bool HasPendingDelivery)) → null o uno de:
  - 'La orden está en Entrada; confírmela antes de asignarla a una ruta.'
  - 'Las entregas especiales se asignan al chofer desde la orden; no pasan por Sala de despacho.'
  - 'La orden está en '{estatus}'; regrésela al pipeline antes de asignarla a una ruta.'
  - 'La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.'
  - 'El estatus actual no permite la acción 'ASSIGN_TRIP'.'
  - 'La orden no tiene una parada de entrega pendiente.'
- ValidateSequence(current, requested): permutación exacta.
- OverStopLimit(stopCount, effective): estrictamente mayor.
- PickOpenTrip(candidatos) → menor TripId. La comparten P6 y P9.
- BuildIssues(TripIssueInput(...)): los mismos campos y mensajes del plan ganador.
  - Bloqueantes: NO_DRIVER, NO_VEHICLE, NO_STOPS, DRIVER_UNAVAILABLE, VEHICLE_UNAVAILABLE y ORDER_NOT_ELIGIBLE 'Orden {n}: {motivo}'.
  - Avisos: OVER_STOP_LIMIT, OVER_VEHICLE_STOPS, OVER_WEIGHT, OVER_VOLUME, LATE_WINDOWS, NO_PLANNED_START, APPROXIMATE_PINS, DRIVER_DOUBLE_BOOKED y VEHICLE_DOUBLE_BOOKED. Los avisos de disponibilidad no bloqueantes pasan tal cual.
- DispatchBlockingMessage(code, issues) = 'La ruta {código} no se puede despachar: {m1}; {m2}.'
- Números en cultura invariante.

EtaCalculator: igual que en el plan ganador.
- Calculate(DateTime? startUtc, IReadOnlyList<EtaStopInput>, EtaSettings(35, 1.3, 15)) → EtaResult.
- Tramo con puntos en ambos extremos: haversine × 1.3, 3 decimales; minutos = max(1, ceil(km / 35 × 60)).
- Primer tramo, o tramo sin puntos: 15 min y distancia null.
- Espera hasta el inicio de la ventana. Salida = llegada + servicio. Tardía si la llegada pasa el fin de la ventana.
- Sin startUtc: horas null.
- Valida DECIMAL(12,3).

DispatchZoneMatcher: NormalizeMember, Resolve (precedencia CP > rango > municipio sin acentos; empate = ambigua) y FindConflict, con los mensajes exactos del plan ganador. POLYGON → 'Las zonas por polígono todavía no se soportan; use código postal, rango postal o municipio.'

RouteOptimization:
- PlanStopInput, VehicleCapacity, RouteOptimizationRequest, UnassignedPlanStop, RouteOptimizationResult.
- UnassignedReasons:
  - CAPACITY_STOPS 'Excede el máximo de paradas del vehículo.'
  - CAPACITY_WEIGHT 'Excede la capacidad de peso del vehículo.'
  - CAPACITY_VOLUME 'Excede la capacidad de volumen del vehículo.'
  - Message(code) devuelve la etiqueta.
- RouteOptimizationRules.ValidateResult: ids de la entrada, sin repetidos, asignados ∪ sin asignar = entrada.

RoutePlanJson (NUEVO):
- Serialize(RoutePlanResponse(IReadOnlyList<int> OrderedStopIds, IReadOnlyList<RoutePlanUnassigned(Guid OrderPublicId, string OrderNumber, string ReasonCode)> Unassigned, string? Polyline)) → JSON camelCase.
- ParseUnassigned(string? json) → lista, vacía ante null, texto vacío, JSON inválido o forma inesperada. NUNCA lanza.

OrderDispatchFlags:
- IsException(statusCode) = ON_HOLD, PARTIAL o FAILED.
- PendingDispatchStatuses = CONFIRMED, PICKUP, INBOUND, PLANNED.

**(10) Costuras IMPLEMENTADAS.**

Trips/TripQueries.cs: extensiones sobre TeikemDbContext. Es el ÚNICO lugar con SQL crudo del lote y cada sentencia lleva 'TenantId ='.
- TripLabel = 'Ruta' (404 'Ruta no encontrada.').
- ResolveTripAsync(Guid publicId, bool track).
- LockTripAsync(int tripId) y LockTripAsync(Guid publicId):
  - FromSqlInterpolated 'SELECT * FROM dbo.Trip WITH (UPDLOCK, ROWLOCK) WHERE TripId = {id} AND TenantId = {tenant}', tracked.
  - Con proveedor relacional sin transacción → InvalidOperationException.
  - Con InMemory carga tracked sin bloqueo.
- LockTripsAsync(IEnumerable<int> ids): uno por uno, en orden ascendente.
- LockOrdersAsync(IReadOnlyCollection<int> orderIds, bool includeStops):
  - 'SELECT * FROM dbo.TransportOrder WITH (UPDLOCK, ROWLOCK) WHERE TenantId = {t} AND TransportOrderId IN (SELECT CAST(value AS INT) FROM OPENJSON({json}))', en una sola sentencia sobre el PK.
  - Exige transacción. Es el paso (c) del orden de bloqueo.
- LockTenantPlanningAsync(): 'SELECT TenantId AS Value FROM dbo.Tenant WITH (UPDLOCK, ROWLOCK) WHERE TenantId = {t}'. Serializa 'Planificar el día' por tenant. El U lock es compatible con lecturas S, así que no frena el resto de la app.
- ActiveRouteAsync(int tripId, bool track).
- CurrentTripsOfOrdersAsync(orderIds) → CurrentTripRef(int TripId, Guid TripPublicId, string Code, string StatusCode, int? DriverId).
- ActiveZoneMembersAsync(): miembros de las zonas ACTIVAS del tenant.
- PendingDeliveryStopsAsync(orderIds): la parada DELIVERY no terminal de menor Sequence.
- StopPointsAsync(orderStopIds): GeoPoint.Lat/.Long y accuracy, con JOIN a TransportOrder por TenantId.
- SetStopPointAsync(int orderStopId, double lat, double lng): ExecuteSqlInterpolated 'UPDATE os SET GeoPoint = geography::Point({lat}, {lng}, 4326) FROM dbo.OrderStop os JOIN dbo.TransportOrder o ON o.TransportOrderId = os.TransportOrderId WHERE os.OrderStopId = {id} AND o.TenantId = {t}'.
  - Si no afecta una fila → NotFoundException.
  - Corre en la transacción del llamador.
- LastPingsAsync(IReadOnlyCollection<TripPingKey(int TripId, int? DriverId, DateTime? ActualStartUtc)>) → Dictionary<int, DriverPingRow(double Lat, double Lng, decimal? SpeedKmh, int? HeadingDeg, DateTime CapturedAtUtc, DateTime ReceivedAtUtc, bool LinkedToTrip)>:
  - primero, ROW_NUMBER por TripId ordenado por CapturedAtUtc desc (con 'TenantId =');
  - para los trips sin ping y con DriverId y ActualStartUtc, el último ping del chofer con TripId NULL y CapturedAtUtc ≥ ActualStartUtc (LinkedToTrip=false).
  - Con InMemory o lista vacía devuelve un diccionario vacío.

Trips/RouteWriter.cs (scoped; ctor TeikemDbContext, StatusService, ILookupCache, ITenantContext):
- Es la única vía de escritura de TripOrder, Route y RouteStop.
- Exige el Trip tracked obtenido con LockTripAsync y una transacción abierta.
- Cabecera documental: el orden de bloqueo del lote.
- Operaciones de CONTENIDO: exigen EnsureEditableAsync → 422 NotEditableMessage.
  - CheckEligibilityAsync(orders): pipeline con includeDisabled, ASSIGN_TRIP vía IsAllowedAsync y parada pendiente.
  - GetOrCreateActiveRouteAsync(trip): versión max+1, nace DRAFT con historial ROUTE.
  - AddOrdersAsync(trip, IReadOnlyCollection<int> orderIds):
    1. LockOrdersAsync de esos ids (re-lee bajo bloqueo).
    2. Elegibilidad atómica → 422 status_rule con Errors {número: [motivo]}.
    3. Ya en esta ruta → 409; en otra vigente → 409 con el código.
    4. Tope 300 → 400.
    5. Crea TripOrder (TenantId, IsCurrent, AssignedBy, SortHint) y RouteStop PENDING al final, sin fila de historial.
    6. SaveGuardedAsync(OrderTakenMessage) y RecomputeAsync.
  - ReleaseOrderAsync(trip, orderId): TripOrder vigente de ESE trip, o 404. Borra el TripOrder y las RouteStop de la versión VIGENTE (nunca de las archivadas), resecuencia 1..N y recalcula.
  - ReleaseAllAsync(trip).
  - ApplySequenceAsync(trip, routeStopIds) → 400 SequenceMessage.
  - ReplaceActiveRouteAsync(trip, orderedOrderStopIds, finalStatus, comment): archiva y guarda ANTES de insertar la nueva versión.
- RecomputeAsync(trip): NO exige que sea editable, porque solo reescribe tiempos y distancias planificados, no la membresía ni la secuencia. Lo usan la hora de salida de una cabecera despachada (EDIT_TRIP) y el pin. En un Trip terminal lanza InvalidOperationException.
  - Escribe RouteStop.Planned* y *FromPrev*, Route.StopCount y sus totales, y Trip.TotalDistanceKm, TotalDurationMin y PlannedEndUtc.
  - Marca el Trip como modificado: su RowVersion cambia.

Trips/TripIssueBuilder.cs (scoped; ctor TeikemDbContext, IFleetAvailabilityService, RouteWriter, ILookupCache):
- BuildAsync(IReadOnlyCollection<int> tripIds, bool includeOrderEligibility) → Dictionary<int, IReadOnlyList<TripIssue>>.
- Por lotes, sin N+1: disponibilidad en PlanDate, máximo efectivo, capacidad, tardías, pines aproximados, doble asignación y elegibilidad.

Trips/IRouteOptimizer.cs:
- interface IRouteOptimizer { string EngineCode { get; } Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken ct); }
- Contrato: la implementación NO toca la BD. Se llama fuera de transacción.

Trips/TripSeams.cs (NUEVO; tipos de servicio que no son HTTP):
- TripCreateSpec(DateOnly PlanDate, int? DispatchZoneId, int? DriverId, int? VehicleId, DateTime? PlannedStartUtc, bool UseDefaultDriver)
- UnassignedPoolFilter(DateOnly? RequestedUpTo, IReadOnlyCollection<int>? ZoneIds, bool IncludeNoZone)
- UnassignedCandidate(int TransportOrderId, Guid PublicId, string OrderNumber, int? ZoneId, string? ZoneCode, bool ZoneAmbiguous, string StatusCode, DateTime? RequestedDate, string? IneligibleReason)
- Firmas fijadas en los esqueletos:
  - TripService.PrepareNumberingAsync(ct)
  - TripService.CreateTrackedAsync(TripCreateSpec, ct) → Trip
  - TripOrderService.PoolAsync(UnassignedPoolFilter, ct) → IReadOnlyList<UnassignedCandidate>
- P9 se programa contra ellas.

Services/TripOwnedEntityResolvers.cs, completos:
- TRIP → db.Trips
- ROUTE → Trips.SelectMany(Routes)
- ROUTE_STOP → Trips.Routes.Stops
- OPTIMIZATION_RUN → db.OptimizationRuns

**(11) DI (bloque '// Lote 5').**
- Servicios: RouteWriter, TripIssueBuilder, IRouteOptimizer→HeuristicRouteOptimizer, TripReadService, TripService, TripOrderService, RouteOptimizationService, TripDispatchService, TripLifecycleService, TripDayPlanningService, OutboundScanService y TripMonitorService.
- IStatusTransitionEffect: TripStatusEffect y TripOrderReleaseEffect. Resuelven sus dependencias de forma perezosa con IServiceProvider para evitar el ciclo con StatusService.
- IDataSource: TripDataSource.
- IOwnedEntityResolver: los 4 nuevos.

**(12) SystemAnalyticsSeeder, bloque '// Lote 5'.** Los filtros son constantes public const.
- Vista 'Rutas' (TRIP): PlanDate, Code, ZoneCode, DriverName, VehicleCode, Status, StopCount, OverStopLimit, TotalDistanceKm; orden PlanDate desc.
- Indicadores:
  - 'Órdenes sin chofer asignado' (orden 72)
  - 'Órdenes en excepción' (orden 73)
  - 'Rutas sobre el máximo de paradas' (orden 82)
- Gráfico 'Rutas por estatus' (donut, LAST7, orden 80).
- Los campos de la fuente Órdenes que usan los indicadores los implementa P7. El seeder no valida campos al sembrar; AnalyticsSeedFieldsTests (P7) lo cubre.

**(13) Convención.** Ningún archivo de src/Teikem.Api ni de tests importa Teikem.Domain.Trips. Se usa 'using TripRoute = Teikem.Domain.Trips.Route;'.

**(14) Esqueletos compilables**, con firma pública completa y cuerpo NotImplementedException. Cada pieza reemplaza su archivo:
- TripService, TripReadService
- TripOrderService (incluye PoolAsync)
- RouteOptimizationService
- TripDispatchService, TripLifecycleService, TripStatusEffect, TripOrderReleaseEffect
- TripDayPlanningService
- OutboundScanService, TripMonitorService
- HeuristicRouteOptimizer (EngineCode ya implementado)
- Analytics/TripDataSource.cs: Fields completos, sin Amount ni Rate.

**(15) Pruebas de P0.** Ver 'pruebas'.

Archivos:

- `Diseño/logistica-db-estructura.sql`
- `Diseño/logistica-db-seed.sql`
- `src/Teikem.Domain/Constants/CatalogDomains.cs`
- `src/Teikem.Domain/Constants/PermissionCatalog.cs`
- `src/Teikem.Domain/Orders/NumberingRules.cs`
- `src/Teikem.Domain/Fleet/DispatchZone.cs`
- `src/Teikem.Domain/Trips/Trip.cs`
- `src/Teikem.Domain/Trips/Route.cs`
- `src/Teikem.Domain/Trips/OptimizationRun.cs`
- `src/Teikem.Domain/Trips/TripRules.cs`
- `src/Teikem.Domain/Trips/EtaCalculator.cs`
- `src/Teikem.Domain/Trips/DispatchZoneMatcher.cs`
- `src/Teikem.Domain/Trips/RouteOptimization.cs`
- `src/Teikem.Domain/Trips/RoutePlanJson.cs`
- `src/Teikem.Domain/Trips/OrderDispatchFlags.cs`
- `src/Teikem.Infrastructure/Persistence/Configurations/TripConfigurations.cs`
- `src/Teikem.Infrastructure/Persistence/Configurations/FleetConfigurations.cs`
- `src/Teikem.Infrastructure/Persistence/TeikemDbContext.cs`
- `src/Teikem.Infrastructure/DependencyInjection.cs`
- `src/Teikem.Infrastructure/Seeding/SystemAnalyticsSeeder.cs`
- `src/Teikem.Infrastructure/Contracts/TripContracts.cs`
- `src/Teikem.Infrastructure/Contracts/DriverContracts.cs`
- `src/Teikem.Infrastructure/Contracts/OrderContracts.cs`
- `src/Teikem.Infrastructure/Trips/TripQueries.cs`
- `src/Teikem.Infrastructure/Trips/RouteWriter.cs`
- `src/Teikem.Infrastructure/Trips/TripIssueBuilder.cs`
- `src/Teikem.Infrastructure/Trips/IRouteOptimizer.cs`
- `src/Teikem.Infrastructure/Trips/TripSeams.cs`
- `src/Teikem.Infrastructure/Services/TripOwnedEntityResolvers.cs`
- `tests/Teikem.Tests/TripCatalogTests.cs`
- `tests/Teikem.Tests/TripContractsTests.cs`
- `tests/Teikem.Tests/TripRulesTests.cs`
- `tests/Teikem.Tests/EtaCalculatorTests.cs`
- `tests/Teikem.Tests/DispatchZoneMatcherTests.cs`
- `tests/Teikem.Tests/RouteOptimizationRulesTests.cs`
- `tests/Teikem.Tests/RoutePlanJsonTests.cs`
- `tests/Teikem.Tests/RouteWriterTests.cs`
- `tests/Teikem.Tests/RawSqlConfinementTests.cs`
- `tests/Teikem.Tests/OrderDispatchFlagsTests.cs`
- `tests/Teikem.Tests/TenantIsolationModelTests.cs`
- `tests/Teikem.Tests/OrderCatalogTests.cs`
- `tests/Teikem.Tests/FleetContractsTests.cs`

### P1 — Rutas (Trip): alta con número automático, chofer por defecto, salida por defecto y disponibilidad; ficha y listado; edición con capacidad EDIT_TRIP; eliminar ruta; reasignación en bloque por zona

- Toca archivos compartidos: no. Depende de: P0.

**TripPlanningRules (puro)**
- FormatCode(DateOnly planDate, long seq) = NumberFormat.Resolve($"{planDate:yyyy}-####", seq). Ejemplo: '2026-0001'.
- ValidatePlanDate(DateOnly? d, DateOnly today):
  - 'Indique la fecha de la ruta.'
  - 'La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.'
- ValidatePlannedStart(startUtc, planDate), rango [planDate −12 h, planDate+1 +12 h):
  - 'La hora de salida debe caer en la fecha de la ruta (±12 h por zona horaria).'
- DefaultPlannedStart(planDate) = planDate 12:00 UTC (08:00 AST).
- PickDefaultDriver(candidatos) → el id solo si hay exactamente un candidato y está disponible.
- ForbiddenPatchKeys:
  - code/tripCode → 'El número de la ruta se fija al crearlo; no se puede cambiar.'
  - status/statusCode/toCode → 'El estatus de la ruta cambia con sus acciones (optimizar, despachar, eliminar).'
  - tenantId/version/routeVersion/originWarehouseId → 'Ese campo no se puede modificar.'
- Flags en conflicto:
  - 'Indique el chofer o quítelo, no ambos.'
  - 'Indique el vehículo o quítelo, no ambos.'
  - 'Indique la zona o quítela, no ambas.'
  - 'Indique la hora de salida o quítela, no ambas.'
- DispatchedHeaderMessage = 'La fecha y la zona de una ruta despachada no se cambian.'
- ValidateDispatchedPatch(patch): con la ruta en DISPATCHED/IN_PROGRESS solo se aceptan chofer, vehículo y hora de salida.

**TripReadService(db, tenant, lookups, statuses, TripIssueBuilder)**

(a) GetAsync(Guid) / GetByIdAsync(int) → TripDetailDto
- 404 'Ruta no encontrada.' Las rutas canceladas también se leen.
- Paradas de la versión vigente, con zona (DispatchZoneMatcher), Point (StopPointsAsync), accuracy e IsApproximate.
- LateForWindow, totales, EffectiveMaxStops y OverStopLimit.
- Issues = TripIssueBuilder, con includeOrderEligibility cuando la ruta es editable.
- LastRunUnassigned:
  - RoutePlanJson.ParseUnassigned sobre el ResponseJson de la última corrida OK de la ruta vigente;
  - solo las órdenes que siguen sin ruta vigente;
  - etiqueta de UnassignedReasons.Message.
  - Si el JSON es nulo o está dañado, la lista queda vacía; no hay error.
- LastPing: LastPingsAsync con TripPingKey (TripId, DriverId, ActualStartUtc), incluye el respaldo del chofer.
- IsEditable = DRAFT o PLANNED, y activa.
- CanEditHeader = no terminal, activa y StatusService.IsAllowedAsync(TRIP, statusId, EDIT_TRIP).
- RowVersion en base64.

(b) ListAsync(TripListQuery)
- Sin fecha = hoy (UTC).
- Estatus desconocido → 400 'Estatus de ruta desconocido: 'X'.'
- Chofer de otro tenant → 404 'Chofer no encontrado.'
- search en memoria con FleetRules.MatchesSearch, DESPUÉS de los filtros.
- Consultas por lote.

(c) ItemsAsync(tripIds).

**TripService(db, tenant, statuses, lookups, INumberSequenceService, IFleetAvailabilityService, ModuleService, RouteWriter, TripReadService)**

(a) PrepareNumberingAsync(ct): EnsureAsync(TRIP, null) en autocommit. Se llama siempre ANTES de la transacción.

(b) CreateTrackedAsync(TripCreateSpec, ct): corre dentro de la transacción del llamador.
- Zona: de otro tenant → 404 'Zona de despacho no encontrada.'; inactiva → 400 'La zona de despacho está inactiva.'
- Chofer o vehículo explícito:
  - CATALOG (403 module_disabled);
  - Resolve → 404;
  - disponibilidad en PlanDate → 409 'El chofer no está disponible para despacho: …' / 'El vehículo no está disponible para despacho: …'.
- UseDefaultDriver, con zona, sin chofer y CATALOG encendido (IsEnabledAsync; si está apagado se omite sin error):
  - candidatos = DriverZone.IsPrimary de la zona, choferes activos;
  - se evalúa su disponibilidad y se aplica PickDefaultDriver.
- PlannedStartUtc ?? DefaultPlannedStart.
- NextAsync más FormatCode.
- El Trip nace con el estatus inicial. SaveGuardedAsync('Ya existe una ruta con ese número.').
- Historial de nacimiento: TransitionAsync(TripStatus, TRIP, id, null, inicial).
- Lo usa P9.

(c) CreateAsync(TripCreateRequest)
- Valida la solicitud y llama PrepareNumberingAsync.
- Dentro de RunInTransactionAsync: CreateTrackedAsync(UseDefaultDriver=true).
- Devuelve la ficha.

(d) UpdateAsync(Guid, TripPatchRequest)
- Primero se revisan las llaves de Extra → 400.
- Dentro de RunInTransactionAsync:
  1. LockTripAsync y ApplyRowVersion.
  2. Terminal o inactiva → 422 'La ruta {código} está cerrada; solo se consulta.'
  3. Sin EDIT_TRIP en su estatus → 422 NotEditableMessage.
  4. Si la ruta está en DISPATCHED/IN_PROGRESS (el tenant habilitó EDIT_TRIP): PlanDate o zona → 422 DispatchedHeaderMessage.
  5. PlanDate: revalida la disponibilidad del chofer y el vehículo actuales.
  6. Chofer/vehículo: CATALOG más disponibilidad → 409.
  7. PlannedStart: RouteWriter.RecomputeAsync.
  8. SaveGuardedAsync(DbExtensions.ConcurrencyMessage).
- El contenido de la ruta nunca se toca desde aquí.

(e) CancelAsync(Guid, TripCancelRequest?), es decir 'Eliminar ruta'
- Comentario ≤ 500.
- Dentro de RunInTransactionAsync: LockTripAsync, ApplyRowVersion y TransitionAsync(→ CANCELLED, comment ?? 'Ruta eliminada').
- TripStatusEffect libera, archiva y deja IsActive=0.
- Desde DISPATCHED → 422 del motor 'No se permite pasar a 'CANCELLED' desde 'DISPATCHED' según el modelo de esta compañía.'

(f) ReassignZoneAsync(ZoneReassignRequest)
- CATALOG.
- Validaciones:
  - 'Indique la fecha de la ruta.'
  - 'Indique al menos una zona.' / 'Máximo 50 zonas por reasignación.'
  - Zonas del tenant → 404.
  - 'Indique el chofer.'; chofer → 404; disponibilidad → 409.
- Dentro de RunInTransactionAsync: LockTripsAsync de las rutas DRAFT/PLANNED de esas zonas y fecha (ascendente); DriverId = chofer; SaveGuarded.
- Respuesta: ítems más avisos DRIVER_DOUBLE_BOOKED. Sin rutas → 200 con TripsUpdated 0.
- No toca DriverZone.

**TripsController** [Route('api/v1/trips')][Authorize][RequireModule(ModuleKeys.LtlGround)], sin importar Teikem.Domain.Trips:
- GET '' (trips.view)
- POST '' (trips.plan)
- GET '{publicId:guid}' (trips.view)
- PATCH '{publicId:guid}' (trips.plan)
- DELETE '{publicId:guid}' (trips.plan; cuerpo opcional; 204)
- POST 'reassign-zone' (trips.plan)

Archivos:

- `src/Teikem.Domain/Trips/TripPlanningRules.cs`
- `src/Teikem.Infrastructure/Services/TripService.cs`
- `src/Teikem.Infrastructure/Services/TripReadService.cs`
- `src/Teikem.Api/Controllers/TripsController.cs`
- `tests/Teikem.Tests/TripPlanningRulesTests.cs`

### P2 — Órdenes en la ruta: agregar (atómico, con órdenes bloqueadas), quitar/liberar, lista 'Sin asignar' con zona resuelta y pool reutilizable

- Toca archivos compartidos: no. Depende de: P0.

**TripOrderRequestRules (puro)**
- ValidateAdd(ids): colapsa duplicados.
  - 'Indique al menos una orden.'
  - 'Agregue como máximo 200 órdenes por solicitud.'
- NormalizeUnassignedQuery(q):
  - Take 1..500 (default 100); Skip ≥ 0.
  - 'Use dispatchZoneId o noZone, no ambos.'
  - 'El rango de fechas es inválido.'

**TripOrderService(db, tenant, lookups, statuses, RouteWriter, TripReadService)**

(a) AddOrdersAsync(Guid tripPublicId, TripOrdersAddRequest)
- Valida la solicitud.
- Resuelve los PublicIds con db.TransportOrders (AsNoTracking). Una orden inexistente o de otro tenant → 404 'Orden no encontrado.'
- Dentro de RunInTransactionAsync:
  1. LockTripAsync → 404 'Ruta no encontrada.'
  2. ApplyRowVersion y EnsureEditable → 422.
  3. RouteWriter.AddOrdersAsync(trip, ids): bloquea las órdenes por id ascendente, re-verifica bajo bloqueo y es atómico.
- Respuestas:
  - 422 status_rule 'Hay órdenes que no se pueden asignar a la ruta.' con errors {número: [motivo]}.
  - 409 'La orden ya está en esta ruta.'
  - 409 'La orden ya está asignada a la ruta {código}.'
  - Carrera → 409 'La orden ya está asignada a otra ruta.'
  - 400 'Una ruta admite como máximo 300 paradas.'
- NO cambia el OrderStatus. Devuelve la ficha.

(b) RemoveOrderAsync(Guid tripPublicId, Guid orderPublicId, TripOrderRemoveRequest?)
- Dentro de RunInTransactionAsync: LockTripAsync, ApplyRowVersion y EnsureEditable.
- Orden del tenant → 404 'Orden no encontrado.'
- ReleaseOrderAsync → 404 'La orden no está en esta ruta.' (BOLA por id hijo).
- Resecuencia 1..N y recalcula. Devuelve la ficha.

(c) PoolAsync(UnassignedPoolFilter, ct) → IReadOnlyList<UnassignedCandidate>. Es el núcleo compartido con P9.
- Órdenes activas del tenant, no especiales, sin TripOrder vigente (NOT EXISTS) y con parada DELIVERY pendiente.
- Estatus PIPELINE posterior al inicial y anterior a IN_TRANSIT, según el pipeline del tenant (includeDisabled).
- RequestedUpTo: RequestedDate ≤ fecha, o nula.
- Zona resuelta con DispatchZoneMatcher sobre ActiveZoneMembersAsync. Filtro por ZoneIds / IncludeNoZone.
- IneligibleReason: el motivo de ASSIGN_TRIP (IsAllowedAsync por estatus, en caché por llamada), o null.
- Orden: RequestedDate, número. Consultas por lote, sin N+1.

(d) ListUnassignedAsync(UnassignedOrdersQuery) → UnassignedOrderPageDto
- Llama PoolAsync y aplica los filtros:
  - postalCode por prefijo;
  - city sin acentos;
  - clientPublicId (404 si es de otro tenant);
  - rango RequestedFrom/To (hasta exclusivo +1 día);
  - search sobre número, empaque, factura y consignatario, DESPUÉS de los filtros.
- Total antes de paginar. Point solo para la página.
- ASSIGN_TRIP no esconde órdenes de la lista; se valida al agregar.

**TripOrdersController** [Route('api/v1/trips')][Authorize][RequireModule(LtlGround)]:
- GET 'unassigned-orders' (trips.view)
- POST '{publicId:guid}/orders' (trips.plan)
- DELETE '{publicId:guid}/orders/{orderPublicId:guid}' (trips.plan; cuerpo opcional; 200 con la ficha)

Archivos:

- `src/Teikem.Domain/Trips/TripOrderRequestRules.cs`
- `src/Teikem.Infrastructure/Services/TripOrderService.cs`
- `src/Teikem.Api/Controllers/TripOrdersController.cs`
- `tests/Teikem.Tests/TripOrderRequestRulesTests.cs`

### P3 — Optimización en tres fases (HEURISTIC detrás de IRouteOptimizer), versiones, OptimizationRun, reordenamiento manual con ETAs y pin manual por parada

- Toca archivos compartidos: no. Depende de: P0.

**HeuristicRoutePlanner (puro, determinista)** — igual que en el plan ganador.
- Orden total: zona, fin de ventana, inicio de ventana, CP, pueblo normalizado, número de orden, OrderStopId. Los nulos van al final.
- Asignación greedy por paradas, peso y volumen:
  - una capacidad NULL no tiene límite;
  - el peso o volumen NULL de una orden cuenta 0;
  - si no cabe, queda con el primer motivo que falle y se sigue con la siguiente.
- Polyline = null.

**RouteEditRules (puro)**
- ValidateLocation(lat, lng):
  - 'Indique la latitud y la longitud.'
  - 'La latitud debe estar entre -90 y 90.'
  - 'La longitud debe estar entre -180 y 180.'
- IsStale(OptimizationSnapshot(int TripId, int? RouteId, byte[] TripRowVersion), currentRouteId, currentRowVersion, isEditable) → true si cambió algo.
- StaleMessage = 'La ruta cambió mientras se optimizaba; vuelva a optimizar.'
- EngineErrorMessage = 'El optimizador no pudo calcular la ruta; la corrida quedó registrada con error.'
- NoStopsMessage = 'La ruta no tiene paradas que optimizar.'
- EngineTimeout = 60 s.

**HeuristicRouteOptimizer : IRouteOptimizer**
- EngineCode = HEURISTIC.
- No toca la BD.

**RouteOptimizationService(db, tenant, lookups, statuses, IRouteOptimizer, RouteWriter, TripReadService, ILogger)**

(a) OptimizeAsync(Guid, OptimizeRequest?) — trips.optimize, en TRES FASES.

FASE 1, RunInTransactionAsync #1:
- LockTripAsync (404), ApplyRowVersion (409) y EnsureEditable (422).
- Sin paradas → 422 NoStopsMessage.
- Arma RouteOptimizationRequest: ventanas, servicio, CP, pueblo, zona, peso y volumen de la orden, puntos y capacidad del vehículo (sin límites si no hay vehículo). StartUtc = PlannedStartUtc.
- Crea el OptimizationRun (TenantId, TripId, RouteId previa, EngineLookupId, PENDING, RequestJson sin nombres de personas, StartedAtUtc, CreatedBy) y su historial de nacimiento (OPTIMIZATION_RUN, id int).
- Guarda SIN modificar el Trip.
- Devuelve la instantánea: TripId, RouteId y RowVersion.
- Las validaciones 404/409/422 no crean corrida.

FASE 2, sin transacción ni bloqueos:
- result = optimizer.OptimizeAsync(request), con un token combinado con EngineTimeout.
- Después, RouteOptimizationRules.ValidateResult.
- Excepción, timeout o resultado inválido → FASE E.

FASE 3, RunInTransactionAsync #2:
- LockTripAsync y RouteEditRules.IsStale.
- Si la ruta cambió: la corrida pasa PENDING→ERROR con ErrorMessage 'Descartada: la ruta cambió durante el cálculo.', se guarda, y FUERA de la lambda se responde 409 StaleMessage.
- Si no cambió:
  1. ReplaceActiveRouteAsync(ordenado + sin asignar al final, OPTIMIZED, 'Optimización #{runId}') y se guarda: la versión n se archiva INTACTA (sus paradas, secuencias y ETAs son el plan anterior);
  2. cada parada sin asignar → RouteWriter.ReleaseOrderAsync sobre la versión n+1 (ya vigente), guardar y RecomputeAsync;
  3. si el Trip estaba en DRAFT → PLANNED ('Ruta optimizada (versión n)');
  4. la corrida pasa a OK con RouteId, ResponseJson = RoutePlanJson.Serialize(...), totales, UnassignedCount y CompletedAtUtc;
  5. SaveGuardedAsync.

FASE E, transacción NUEVA:
- La corrida pasa PENDING→ERROR con ErrorMessage recortado a 4000.
- Responde 409 EngineErrorMessage.
- Si el cliente cancela (OperationCanceledException), también se registra ERROR con CancellationToken.None.

Dos optimizaciones simultáneas: ambas crean su corrida y la primera en aplicar gana. La otra termina en ERROR con 409, salvo que no se hayan solapado, en cuyo caso ambas dan 200 con versiones n+1 y n+2. Siempre queda una sola versión vigente.

(b) ReorderAsync(Guid, RouteSequenceRequest) — trips.plan
- Dentro de RunInTransactionAsync: LockTripAsync, ApplyRowVersion y EnsureEditable.
- ApplySequenceAsync → 400 SequenceMessage, también con ids de otra ruta u otro tenant.
- Recalcula las ETAs. Misma versión, sin corrida.

(c) SetStopLocationAsync(Guid tripPublicId, int routeStopId, StopLocationRequest) — trips.plan
- ValidateLocation → 400.
- Dentro de RunInTransactionAsync:
  1. LockTripAsync, ApplyRowVersion y EnsureEditable (422 si está despachada).
  2. La RouteStop debe pertenecer a la versión VIGENTE de ESE trip → 404 'Parada no encontrada en esta ruta.' (BOLA por id hijo).
  3. TripQueries.SetStopPointAsync(orderStopId, lat, lng).
  4. OrderStop.GeocodeAccuracyLookupId = MANUAL (ILookupCache), por EF: el AuditLog de TRANSPORT_ORDER registra el cambio de precisión.
  5. RecomputeAsync y SaveGuarded.
- Devuelve la ficha: Point con la coordenada, GeocodeAccuracyCode MANUAL, IsApproximate false y ETAs recalculadas.

(d) ListRunsAsync(Guid) — trips.view
- Corridas en orden descendente, con motor, estatus, versión resultante y quién la lanzó.

**TripRoutesController** [Route('api/v1/trips')][Authorize][RequireModule(LtlGround)]:
- POST '{publicId:guid}/optimize' (trips.optimize)
- PUT '{publicId:guid}/route/sequence' (trips.plan)
- PUT '{publicId:guid}/stops/{routeStopId:int}/location' (trips.plan)
- GET '{publicId:guid}/optimization-runs' (trips.view)

Archivos:

- `src/Teikem.Domain/Trips/HeuristicRoutePlanner.cs`
- `src/Teikem.Domain/Trips/RouteEditRules.cs`
- `src/Teikem.Infrastructure/Trips/HeuristicRouteOptimizer.cs`
- `src/Teikem.Infrastructure/Services/RouteOptimizationService.cs`
- `src/Teikem.Api/Controllers/TripRoutesController.cs`
- `tests/Teikem.Tests/HeuristicRoutePlannerTests.cs`
- `tests/Teikem.Tests/RouteEditRulesTests.cs`

### P4 — Despacho y salida: selector, despacho individual y en lote (órdenes a PLANNED), salida con TripLifecycleService (órdenes a IN_TRANSIT), efectos de TripStatus y efecto de OrderStatus

- Toca archivos compartidos: no. Depende de: P0.

**DispatchBatchRules (puro)**
- Validate(ids):
  - 'Seleccione al menos una ruta.'
  - 'Máximo 50 rutas por despacho.'
  - Distinct.
- Summarize(items).
- Mensajes de salida:
  - NotDispatchedForStart(code) = 'La ruta {código} no está despachada; despáchela antes de registrar su salida.'
  - AlreadyStarted(code) = 'La ruta {código} ya salió.'
  - PipelineMissing(stage) = 'El pipeline de rutas de esta compañía no tiene habilitada la etapa {stage}.'

**TripDispatchService(db, tenant, statuses, ModuleService, TripIssueBuilder, TripReadService)**

(a) ListDispatchableAsync(DateOnly? date)
- Solo rutas activas en DRAFT o PLANNED del día.
- Issues con elegibilidad; CanDispatch = sin bloqueantes.
- Orden por zona y código.

(b) DispatchAsync(Guid, TripDispatchRequest?)
- CATALOG; comentario ≤ 500.
- Dentro de RunInTransactionAsync:
  1. LockTripAsync (404) y ApplyRowVersion.
  2. Estatus DRAFT o PLANNED; si no, 422 NotEditableMessage.
  3. Con bloqueantes → 422 status_rule 'La ruta {código} no se puede despachar: …' con errors {códigoIssue: [mensaje]}.
  4. PipelinePath de TripStatus hasta DISPATCHED, una TransitionAsync por paso.
  5. Si el tenant no tiene DISPATCHED → 422 PipelineMissing('DISPATCHED').
  6. SaveGuardedAsync.

(c) DispatchBatchAsync(TripBatchDispatchRequest)
- Ascendente por TripId, cada ruta en su propia transacción.
- Un id ajeno → item con 'Ruta no encontrada.'
- Siempre 200.

**TripLifecycleService(db, statuses, TripReadService)**. Es la costura de la salida que reutiliza el Lote 7.
- StartTrackedAsync(Trip trip, string? comment, ct): corre dentro de la transacción del llamador, con el Trip bloqueado.
  - IN_PROGRESS → 422 AlreadyStarted.
  - No DISPATCHED → 422 NotDispatchedForStart.
  - TransitionAsync(TripStatus → IN_PROGRESS, comment ?? 'Salida registrada'). Si el tenant no tiene IN_PROGRESS → 422 del motor.
- StartAsync(Guid, TripStartRequest?): RunInTransactionAsync, LockTripAsync, ApplyRowVersion, StartTrackedAsync y SaveGuarded. Devuelve la ficha.

**TripStatusEffect (dominio TripStatus)**

Sus dependencias se resuelven de forma perezosa. Ignora el nacimiento y las entidades distintas de TRIP.

→ DISPATCHED
- From debe ser DRAFT o PLANNED.
- Invariantes: chofer, vehículo y ruta vigente con al menos una parada; si falla, 422 DispatchBlockingMessage.
- Ruta: PipelinePath de RouteStatus hasta ACTIVE. Comentario 'Despachada', o 'Secuencia manual confirmada al despachar' si venía de DRAFT.
- Si la ruta no llega a ACTIVE → 422 PipelineMissing('ACTIVE').
- Órdenes vigentes:
  - LockOrdersAsync, respetando el orden de bloqueo;
  - CheckEligibilityAsync; si falla, 422 'Orden {n}: {motivo}';
  - PipelinePath de OrderStatus hasta **PLANNED**. Si el tenant deshabilitó PLANNED, hasta la última etapa habilitada anterior. Una transición por paso, con comentario 'Despachada en la ruta {código}'.
- Las paradas siguen en PENDING.

→ IN_PROGRESS
- ActualStartUtc ??= ahora.
- Órdenes vigentes: LockOrdersAsync y PipelinePath hasta IN_TRANSIT, con comentario 'Salió en la ruta {código}'.
- Las órdenes que ya no estén en PIPELINE se omiten: un lateral en una ruta despachada lo decide el Lote 7.

→ COMPLETED
- ActualEndUtc ??= ahora.
- TripOrder.IsCurrent = 0.

→ CANCELLED
- From debe ser DRAFT o PLANNED; si no, 422 'La ruta {código} ya fue despachada; no se puede eliminar.'
- ReleaseAllAsync.
- Ruta a ARCHIVED con IsActive=0; trip.IsActive=0.

No guarda nada: todo queda en la unidad de trabajo del llamador.

**TripOrderReleaseEffect (dominio OrderStatus)**
- Solo actúa sobre TRANSPORT_ORDER con To = CANCELLED o LATERAL, y con TripOrder vigente.
- Ruta en DRAFT/PLANNED: LockTripAsync y ReleaseOrderAsync.
- Ruta en DISPATCHED/IN_PROGRESS y To = CANCELLED → 422 'La orden va en la ruta {código} ya despachada; no se puede cancelar mientras la ruta esté en curso.'
- Lateral en una ruta despachada: sin cambios.

**TripDispatchController** [Route('api/v1/trips')][Authorize][RequireModule(LtlGround)]:
- GET 'dispatchable' (trips.dispatch)
- POST '{publicId:guid}/dispatch' (trips.dispatch)
- POST 'dispatch' (trips.dispatch; lote)
- POST '{publicId:guid}/start' (trips.dispatch; salida)

Archivos:

- `src/Teikem.Domain/Trips/DispatchBatchRules.cs`
- `src/Teikem.Infrastructure/Services/TripDispatchService.cs`
- `src/Teikem.Infrastructure/Services/TripLifecycleService.cs`
- `src/Teikem.Infrastructure/Services/TripStatusEffect.cs`
- `src/Teikem.Infrastructure/Services/TripOrderReleaseEffect.cs`
- `src/Teikem.Api/Controllers/TripDispatchController.cs`
- `tests/Teikem.Tests/TripStatusEffectTests.cs`
- `tests/Teikem.Tests/DispatchBatchRulesTests.cs`

### P5 — Zonas de despacho: miembros (CP, rango postal, municipio) sin solapamiento, resolución ZIP/pueblo → zona y guardas de inactivación y reactivación

- Toca archivos compartidos: no. Depende de: P0.

Sin cambios respecto del plan ganador.

**DispatchZoneService** (se extiende el del Lote 4; el constructor recibe además ILookupCache):

(a) ListMembersAsync(int zoneId)
- 404 'Zona de despacho no encontrada.'

(b) AddMemberAsync(int zoneId, DispatchZoneMemberRequest)
- Zona del tenant → 404; zona inactiva → 400 'La zona de despacho está inactiva.'
- Criterio desconocido → 400 'Criterio de zona desconocido: 'X'.'
- NormalizeMember → 400. POLYGON también es 400.
- Repetido en la misma zona → 409 'La zona ya tiene ese criterio.'
- Choca con otra zona ACTIVA → 409 'El valor '{v}' ya pertenece a la zona {código}.'
- UQ_DispatchZoneMember es la segunda barrera.
- Se audita bajo DISPATCH_ZONE.

(c) RemoveMemberAsync(int zoneId, int memberId)
- El miembro debe ser de ESA zona del tenant → 404 'Criterio de zona no encontrado.'
- DELETE físico auditado.

(d) ResolveAsync(postalCode, city)
- Sin parámetros → 400 'Indique el código postal o el pueblo.'

(e) SetActiveAsync
- Inactivar con rutas abiertas → 409 'La zona tiene rutas abiertas; ciérrelas o cámbielas de zona antes de inactivarla.'
- Reactivar con miembros que chocan con otra zona activa → 409 con el mensaje de conflicto.

**DispatchZonesController** (CATALOG):
- GET '{id:int}/members' (fleet.view)
- POST '{id:int}/members' (fleet.manage)
- DELETE '{id:int}/members/{memberId:int}' (fleet.manage; 204)
- GET 'resolve' (fleet.view)

FleetControllerSecurityTests sigue pasando sin cambios.

Archivos:

- `src/Teikem.Infrastructure/Services/DispatchZoneService.cs`
- `src/Teikem.Api/Controllers/DispatchZonesController.cs`
- `tests/Teikem.Tests/DispatchZoneMembershipTests.cs`

### P6 — Estación de escaneo Outbound: lookup del Lote 3, decisión pura con precedencia, re-verificación bajo bloqueo y resultado tipado para la voz

- Toca archivos compartidos: no. Depende de: P0.

**OutboundScanRules (puro)**

NormalizeCode:
- Recorta espacios.
- Vacío → 'Escanee o escriba un código.'
- Más de 40 caracteres → 'El código no puede exceder 40 caracteres.'

Decide(ScanFacts(int MatchCount, string? MatchedBy, string? OrderNumber, string? CurrentTripCode, string? IneligibleReason, ZoneResolution? Zone, string? OpenTripCode, DateOnly PlanDate)) → ScanDecision(Outcome, ReasonCode, Message, Voice). Precedencia fija, probada:
1. MatchCount 0 → NOT_FOUND 'No se encontró la orden.'
2. MatchCount > 1 → NOT_FOUND 'Hay varias órdenes con ese código; escanee el empaque.'
3. CurrentTripCode → ALREADY_ASSIGNED 'Ya estaba en la ruta {código}.' Gana a no elegible: una orden ya despachada dice 'Ya'.
4. IneligibleReason → NOT_ELIGIBLE con el motivo de TripRules.
5. Sin zona → FOUND_UNASSIGNED NO_ZONE 'No se pudo resolver la zona de despacho por código postal ni pueblo; queda sin asignar.'
6. Zona ambigua → FOUND_UNASSIGNED AMBIGUOUS_ZONE 'El código postal o pueblo pertenece a varias zonas ({códigos}); queda sin asignar.'
7. Sin ruta abierta → FOUND_UNASSIGNED NO_OPEN_ROUTE 'No hay ruta abierta para la zona {código} en la fecha {yyyy-MM-dd}; queda sin asignar.'
8. En otro caso → FOUND_ASSIGNED 'Asignada a la ruta {código}.'

Voice(outcome):
- FOUND_* → found
- ALREADY_ASSIGNED → dup
- NOT_FOUND y NOT_ELIGIBLE → notfound

**OutboundScanService(db, tenant, lookups, statuses, OrderReadService, RouteWriter)**

ScanAsync(OutboundScanRequest). Siempre 200, salvo 400 por el código y 401/403.
1. NormalizeCode.
2. OrderReadService.LookupAsync(code, OrderScope.Any): el mismo lookup exacto que /orders/lookup, con precedencia número > empaque > factura y la misma ambigüedad. Se reutiliza tal cual, sin reimplementar la búsqueda.
3. Hechos para una sola coincidencia:
   - CurrentTripsOfOrdersAsync;
   - CheckEligibilityAsync;
   - zona de la parada pendiente;
   - rutas activas DRAFT/PLANNED de esa zona en PlanDate ?? hoy (UTC), con PickOpenTrip.
4. Decide.
5. Si el resultado es FOUND_ASSIGNED, dentro de RunInTransactionAsync:
   - LockTripAsync. Si ya no es editable, se prueba la siguiente ruta abierta; si no hay, NO_OPEN_ROUTE.
   - RouteWriter.AddOrdersAsync(trip, [id]): bloquea la orden y RE-VERIFICA dentro de la transacción la elegibilidad y la ruta vigente.
6. Fuera de la transacción:
   - ConflictException (carrera) → se relee y se responde ALREADY_ASSIGNED;
   - StatusRuleException → NOT_ELIGIBLE con su motivo.

Límites:
- El escaneo nunca crea rutas.
- No cambia chofer, vehículo ni OrderStatus.
- No usa CROSSDOCK ni DockAppointment.

**ScanController** [Route('api/v1/scan')][Authorize][RequireModule(LtlGround)]:
- POST 'outbound' (trips.scan)

Archivos:

- `src/Teikem.Domain/Trips/OutboundScanRules.cs`
- `src/Teikem.Infrastructure/Services/OutboundScanService.cs`
- `src/Teikem.Api/Controllers/ScanController.cs`
- `tests/Teikem.Tests/OutboundScanRulesTests.cs`

### P7 — Monitoreo (totales por fecha, ping de respaldo), fuente de datos TRIP, extensión de la fuente Órdenes y ruta/chofer asignado en la ficha de la orden

- Toca archivos compartidos: no. Depende de: P0.

**MonitorRules (puro)**
- Progress(códigos de parada) → Total, Completed, Failed, Pending.
- NextEta = menor PlannedArrivalUtc entre las paradas no terminales.
- ApproximateCount.
- Totals(trips): se calculan SOBRE LA FECHA Y ZONA, antes de la búsqueda (L650: buscar nunca cambia los contadores).
- ApplySearch(trips, q): se aplica DESPUÉS de los filtros, sobre los textos visibles (código, zona, chofer, vehículo, estatus).

**TripMonitorService(db, tenant, lookups)**

ListAsync(MonitorQuery) → MonitorDto
- Rutas activas del día en DISPATCHED o IN_PROGRESS, más COMPLETED si IncludeCompleted.
- Progreso sobre la versión vigente.
- LastPingsAsync con TripPingKey, en UNA llamada. Si no hay ping con TripId, se usa el del chofer desde ActualStartUtc (LinkedToTrip=false).
- Totals, incluido TripsWithoutPing, y luego la búsqueda.
- Consultas por lote.

El mapa lo dibuja el front con GET /trips/{id}.

**TripDataSource** (reemplaza el esqueleto)
- AsNoTracking, MaxRows y q.Ids.
- PlanDate como rango.
- Etiquetas, conteos por lote, EffectiveMaxStops y OverStopLimit.
- Sin campos Amount ni Rate.

**Analytics/OrderDataSources.cs** (se movió aquí desde P0 para aligerar la base)
- TransportOrderDataSource gana:
  - TripId (Number), TripCode, TripStatusCode;
  - AssignedDriverCode, AssignedDriverName, HasAssignedDriver (Bool);
  - DispatchZoneCode, IsException (Bool);
  - la relación Trip (TRIP, TripId).
- Se calculan con dos consultas por lote: TripOrder vigente → Trip → Driver, y DriverTrip activo → Driver. La zona sale de DispatchZoneMatcher.

**OrderReadService** (cambio acotado a la ficha; LookupAsync NO cambia porque P6 lo reutiliza)
- Solo para órdenes no especiales y con OrderScope.Any: AssignedTripPublicId/AssignedTripCode = la ruta vigente, y AssignedDriver* = el chofer de esa ruta.
- El portal no recibe nada nuevo.

**TripMonitorController** [Route('api/v1/trips')][Authorize][RequireModule(LtlGround)]:
- GET 'monitor' (trips.view)

Archivos:

- `src/Teikem.Domain/Trips/MonitorRules.cs`
- `src/Teikem.Infrastructure/Services/TripMonitorService.cs`
- `src/Teikem.Infrastructure/Analytics/TripDataSource.cs`
- `src/Teikem.Infrastructure/Analytics/OrderDataSources.cs`
- `src/Teikem.Infrastructure/Services/OrderReadService.cs`
- `src/Teikem.Api/Controllers/TripMonitorController.cs`
- `tests/Teikem.Tests/MonitorRulesTests.cs`
- `tests/Teikem.Tests/AnalyticsSeedFieldsTests.cs`

### P9 — Planificar el día: agrupar por zona las órdenes sin asignar de la fecha en la ruta abierta de la zona (o una nueva con el chofer estándar), idempotente

- Toca archivos compartidos: no. Depende de: P0, P1, P2.

Cubre literalmente L262: 'agrupar órdenes confirmadas por fecha/zona en trips, asignando vehículo y chofer'. Se programa en paralelo contra las firmas de TripSeams (P0) y se integra después de P1 (CreateTrackedAsync) y P2 (PoolAsync).

**DayPlanningRules (puro)**
- ValidateRequest(req, today):
  - la fecha con TripPlanningRules.ValidatePlanDate;
  - DispatchZoneIds distintos, como máximo 50: 'Máximo 50 zonas por planificación.'
- Allocate(IReadOnlyList<UnassignedCandidate> candidates, IReadOnlyDictionary<int zoneId, int currentStops> openTrips, IReadOnlyCollection<int> targetZones, bool createEmptyTrips) → por zona:
  - Assigned: ids en orden RequestedDate, número;
  - Skipped (id, reason): IneligibleReason, o 'CAPACITY_HARD_CAP' si pasaría de 300 paradas, con el texto 'La ruta llegó al máximo de 300 paradas; la orden queda sin asignar.';
  - CreateTrip: true si no hay ruta abierta y hay órdenes asignables, o si createEmptyTrips.
- Las órdenes sin zona o ambiguas se cuentan en OrdersWithoutZone y no se tocan.

**TripDayPlanningService(db, tenant, TripService, TripOrderService, RouteWriter, TripIssueBuilder, TripReadService)**

PlanDayAsync(PlanDayRequest):
1. Validación. Zonas del tenant → 404 'Zona de despacho no encontrada.'; zona inactiva → 400 'La zona de despacho está inactiva.' Sin lista: todas las zonas activas.
2. TripService.PrepareNumberingAsync, fuera de la transacción.
3. RunInTransactionAsync, con el orden de bloqueo global:
   1. LockTenantPlanningAsync: dos planificaciones simultáneas se serializan y la segunda ve lo que hizo la primera.
   2. Rutas abiertas (activas, DRAFT/PLANNED) de la fecha y de esas zonas → LockTripsAsync ascendente; por zona, PickOpenTrip.
   3. TripOrderService.PoolAsync(RequestedUpTo = PlanDate, ZoneIds).
   4. DayPlanningRules.Allocate.
   5. Para las zonas con CreateTrip: TripService.CreateTrackedAsync(PlanDate, zona, chofer por defecto, sin vehículo, salida por defecto 12:00 UTC).
   6. RouteWriter.AddOrdersAsync(trip, asignadas) por zona. Las órdenes se bloquean por id dentro de cada llamada; como las rutas ya están todas bloqueadas, el orden global se respeta.
   7. SaveGuarded.
4. Respuesta:
   - PlanDayResultDto con, por zona, la ruta (creada o reutilizada), asignadas, omitidas e Issues de TripIssueBuilder (NO_VEHICLE, OVER_STOP_LIMIT, etc.; son informativos);
   - TripsCreated, OrdersAssigned, OrdersSkipped y OrdersWithoutZone.

Reglas:
- Idempotente: repetirla no crea rutas nuevas si ya hay una abierta y solo agrega las órdenes que siguen sin asignar.
- No optimiza, no despacha, no asigna vehículo y no cambia OrderStatus.
- No reasigna órdenes que ya tienen ruta.

**TripPlanningController** [Route('api/v1/trips')][Authorize][RequireModule(LtlGround)]:
- POST 'plan-day' (trips.plan)

Archivos:

- `src/Teikem.Domain/Trips/DayPlanningRules.cs`
- `src/Teikem.Infrastructure/Services/TripDayPlanningService.cs`
- `src/Teikem.Api/Controllers/TripPlanningController.cs`
- `tests/Teikem.Tests/DayPlanningRulesTests.cs`

### P8 — Cierre: smoke del Lote 5, prueba de seguridad de controladores, docs/lote5-decisiones.md, manual funcional 05 y FAQ

- Toca archivos compartidos: no. Depende de: P0, P1, P2, P3, P4, P5, P6, P7, P9.

**(1) scripts/smoke.sh**
- La cabecera pasa a 'Lotes 1-5'.
- Se agrega el bloque '# Lote 5 — Trips y rutas' con los pasos de 'smoke', antes del paso final de sesiones.
- Re-ejecutable:
  - zonas 'Z$TS…';
  - municipios 'Pueblo X $TS';
  - ZIPs derivados de TS que evitan los de R-01..R-04;
  - fechas relativas a hoy (UTC).
- Reutiliza los helpers de los Lotes 3 y 4.
- Cada hallazgo se marca 'Hallazgo de revisión'.
- Los pasos que cambian la configuración del tenant (capacidad EDIT_TRIP, ASSIGN_TRIP, estatus del chofer, módulo CATALOG) la restauran al terminar.

**(2) TripControllerSecurityTests.cs**, por reflexión. Cada acción lleva exactamente un [RequirePermission] con el permiso de este mapa:
- TripsController: GET → trips.view; POST, PATCH, DELETE y reassign-zone → trips.plan.
- TripOrdersController: unassigned-orders → trips.view; POST y DELETE → trips.plan.
- TripRoutesController: optimize → trips.optimize; route/sequence y stops/location → trips.plan; optimization-runs → trips.view.
- TripDispatchController: dispatchable, dispatch, lote y start → trips.dispatch.
- TripMonitorController: monitor → trips.view.
- TripPlanningController: plan-day → trips.plan.
- ScanController: outbound → trips.scan.

Además:
- cada controlador lleva [RequireModule(LTL_GROUND)];
- ninguna acción recibe un parámetro tenantId;
- ningún controlador de src/Teikem.Api contiene 'using Teikem.Domain.Trips;' (convención TripRoute).

**(3) docs/lote5-decisiones.md**, con el formato del Lote 4:
- mapa de lo construido;
- cómo se probó, con el conteo de pruebas y db-init dos veces con 54 permisos;
- las decisiones con su ratificación;
- hallazgos, incluido el ajuste de FleetContractsTests por la cola de OrderDetailDto;
- lo que queda fuera;
- el enlace a la corrida de CI.
- Contrastar con el maestro (L258-274, L157, L525, L650) y anotar las discrepancias.

**(4) docs/manual/05-trips-y-rutas.md**, escrito leyendo el código real.
- Funcionalidades:
  - Sala de despacho;
  - Planificar el día;
  - número de ruta;
  - chofer y salida por defecto;
  - disponibilidad;
  - zonas;
  - reasignación en bloque;
  - optimizar, en tres fases y con sus 409;
  - versiones y paradas que no caben;
  - reordenar, pin manual y ETAs;
  - alertas;
  - despachar;
  - salida;
  - cabecera editable con EDIT_TRIP;
  - escaneo;
  - monitoreo;
  - fuentes e indicadores.
- Mensajes exactos con su código HTTP.
- Estatus y transiciones: TripStatus, RouteStatus, RouteStopStatus, OptimizationRunStatus y el efecto sobre OrderStatus (despacho → PLANNED, salida → IN_TRANSIT).
- Casos frecuentes.

**(5) faq.md**, sección 'Lote 5': cada mensaje de error con qué hacer.

**(6) README.md** enlaza el capítulo 05.

El lote no se cierra sin CI verde.

Archivos:

- `scripts/smoke.sh`
- `tests/Teikem.Tests/TripControllerSecurityTests.cs`
- `docs/lote5-decisiones.md`
- `docs/manual/05-trips-y-rutas.md`
- `docs/manual/faq.md`
- `docs/manual/README.md`

## Cambios SQL

- Diseño/logistica-db-estructura.sql · dbo.NumberSequence (CK_NumberSequence_Kind, inline '-- Lote 5'):
- El CHECK queda Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP').
- TRIP es el número de ruta AAAA-#### por tenant (ClientId NULL).
- Diseño/logistica-db-estructura.sql · CAPA 12 dbo.Trip:
- VehicleId y DriverId pierden el REFERENCES simple y ganan FKs compuestas:
  - CONSTRAINT FK_Trip_Vehicle FOREIGN KEY (VehicleId, TenantId) REFERENCES dbo.Vehicle(VehicleId, TenantId)
  - CONSTRAINT FK_Trip_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId)
- + DispatchZoneId INT NULL (su FK se agrega después de DispatchZone).
- + CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), CreatedBy INT NULL REFERENCES dbo.AspNetUsers(Id), UpdatedAtUtc DATETIME2 NULL, UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id).
- + CONSTRAINT UQ_Trip_IdTenant UNIQUE (TripId, TenantId).
- + CONSTRAINT CK_Trip_Numbers CHECK ((TotalDistanceKm IS NULL OR TotalDistanceKm >= 0) AND (TotalDurationMin IS NULL OR TotalDurationMin >= 0) AND (PlannedEndUtc IS NULL OR PlannedStartUtc IS NULL OR PlannedEndUtc >= PlannedStartUtc) AND (ActualEndUtc IS NULL OR ActualStartUtc IS NULL OR ActualEndUtc >= ActualStartUtc)).
- UQ_Trip_Code queda sin filtro.
- Índices nuevos:
  - CREATE INDEX IX_Trip_Zone_Date ON dbo.Trip(TenantId, PlanDate, DispatchZoneId) WHERE IsActive = 1
  - CREATE INDEX IX_Trip_Driver_Date ON dbo.Trip(TenantId, DriverId, PlanDate) WHERE IsActive = 1 AND DriverId IS NOT NULL
- Diseño/logistica-db-estructura.sql · CAPA 12 dbo.TripOrder:
- + TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId).
- FKs compuestas:
  - CONSTRAINT FK_TripOrder_Trip FOREIGN KEY (TripId, TenantId) REFERENCES dbo.Trip(TripId, TenantId)
  - CONSTRAINT FK_TripOrder_Order FOREIGN KEY (TransportOrderId, TenantId) REFERENCES dbo.TransportOrder(TransportOrderId, TenantId)
- + IsCurrent BIT NOT NULL CONSTRAINT DF_TripOrder_IsCurrent DEFAULT 1.
- + AssignedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), AssignedBy INT NULL REFERENCES dbo.AspNetUsers(Id).
- Se conserva UQ_TripOrder.
- CREATE UNIQUE INDEX UX_TripOrder_Current ON dbo.TripOrder(TransportOrderId) WHERE IsCurrent = 1.
- CREATE INDEX IX_TripOrder_Trip ON dbo.TripOrder(TripId) WHERE IsCurrent = 1.
- Diseño/logistica-db-estructura.sql · CAPA 12 dbo.Route:
- IX_Route_Trip se reemplaza por CREATE UNIQUE INDEX UX_Route_Trip_Active ON dbo.Route(TripId) WHERE IsActive = 1.
- + CONSTRAINT UQ_Route_Version UNIQUE (TripId, Version).
- + CONSTRAINT CK_Route_Numbers CHECK (Version >= 1 AND StopCount >= 0 AND (TotalDistanceKm IS NULL OR TotalDistanceKm >= 0) AND (TotalDurationMin IS NULL OR TotalDurationMin >= 0)).
- Los totales y StopCount los escribe la aplicación (RouteWriter.RecomputeAsync); no hay columnas computadas ni triggers.
- Diseño/logistica-db-estructura.sql · CAPA 12 dbo.RouteStop:
- + CONSTRAINT CK_RouteStop_Numbers CHECK (Sequence >= 1 AND (DistanceFromPrevKm IS NULL OR DistanceFromPrevKm >= 0) AND (DurationFromPrevMin IS NULL OR DurationFromPrevMin >= 0)).
- A propósito, no hay UNIQUE sobre (RouteId, Sequence): el reordenamiento reescribe bajo el bloqueo del trip.
- Diseño/logistica-db-estructura.sql · CAPA 12B dbo.DispatchZone:
- + CONSTRAINT UQ_DispatchZone_IdTenant UNIQUE (DispatchZoneId, TenantId).
- Justo después de su GO: ALTER TABLE dbo.Trip ADD CONSTRAINT FK_Trip_DispatchZone FOREIGN KEY (DispatchZoneId, TenantId) REFERENCES dbo.DispatchZone(DispatchZoneId, TenantId); GO
- Diseño/logistica-db-estructura.sql · CAPA 12B dbo.DispatchZoneMember:
- + CONSTRAINT UQ_DispatchZoneMember UNIQUE (DispatchZoneId, MatchTypeLookupId, MatchValue). MatchValue se guarda normalizado.
- El solapamiento entre zonas lo impide el servicio.
- Diseño/logistica-db-estructura.sql · CAPA 12B dbo.OptimizationRun:
- OptimizationRunId pasa de BIGINT a **INT** IDENTITY(1,1) PRIMARY KEY. Nadie la referencia todavía, y EntityStatusHistory.EntityId e IOwnedEntityResolver trabajan con int.
- TripId gana CONSTRAINT FK_OptimizationRun_Trip FOREIGN KEY (TripId, TenantId) REFERENCES dbo.Trip(TripId, TenantId).
- + CreatedBy INT NULL REFERENCES dbo.AspNetUsers(Id).
- + CONSTRAINT CK_OptimizationRun_Numbers CHECK ((UnassignedCount IS NULL OR UnassignedCount >= 0) AND (TotalDistanceKm IS NULL OR TotalDistanceKm >= 0) AND (TotalDurationMin IS NULL OR TotalDurationMin >= 0)).
- CREATE INDEX IX_OptimizationRun_Trip ON dbo.OptimizationRun(TenantId, TripId, StartedAtUtc).
- Diseño/logistica-db-estructura.sql · dbo.DriverLocationPing (inline '-- Lote 5'):
- + CREATE INDEX IX_LocationPing_Driver ON dbo.DriverLocationPing(TenantId, DriverId, CapturedAtUtc).
- Sirve al ping de respaldo del chofer en el monitor.
- Diseño/logistica-db-seed.sql · LookupCode, en el MERGE existente:
- ('OptimizerEngine','HEURISTIC','Heurística (zona y ventana)','Heuristic (zone & window)',4)
- ('EntityType','ROUTE_STOP','Parada de ruta','Route stop',67)
- ('EntityType','OPTIMIZATION_RUN','Corrida de optimización','Optimization run',68)
- ('Capability','EDIT_TRIP','Editar ruta','Edit trip',8)
- Diseño/logistica-db-seed.sql · 4) PERMISOS:
- #P agrega ('trips.view','TRIPS','Ver rutas y despacho','View trips & dispatch') y ('trips.scan','TRIPS','Escanear salida (Outbound)','Scan outbound').
- #RP agrega Dispatcher → trips.view y trips.scan; WarehouseOperator → trips.view y trips.scan; ReadOnly → trips.view.
- El PRINT final dice 54 permisos.
- Diseño/logistica-db-seed.sql · nuevo bloque '3E) STATUS CAPABILITY por defecto (TenantId NULL) — Lote 5, TRIP', con la misma forma que 3B/3C/3D:
- EntityType TRIP, TripStatus DISPATCHED, IN_PROGRESS, COMPLETED y CANCELLED, Capability EDIT_TRIP, IsAllowed=0.
- El tenant lo cambia desde /status/capabilities/TRIP.
- Diseño/logistica-db-seed.sql · nuevo bloque '3F) STATUS LATERAL ENTRY por defecto (TenantId NULL) — Lote 5', MERGE idempotente sobre dbo.StatusLateralEntry:
- TRIP: CANCELLED desde DRAFT y PLANNED (IsAllowed=1).
- ROUTE: ARCHIVED desde DRAFT y OPTIMIZED (IsAllowed=1).

## Decisiones

1. DECISIÓN (V1): 'Ruta' en pantalla es el Trip más su Route vigente.
- 'Planificada/despachada/completada' (L271) se lee de TripStatus.
- RouteStatus (DRAFT/OPTIMIZED/ACTIVE/ARCHIVED) es el ciclo de la VERSIÓN del plan.
   - Alternativa: Resembrar RouteStatus con PLANNED/DISPATCHED/COMPLETED y usarlo como estatus visible.
2. DECISIÓN (V2): asignar una orden a una ruta NO cambia su OrderStatus; 'sin asignar' se deriva de que no tenga TripOrder vigente.
- Despachar avanza las órdenes etapa por etapa solo hasta PLANNED.
- La SALIDA (IN_PROGRESS) las lleva a IN_TRANSIT, como pide L267: 'el estado lo mueven eventos'.
- Liberar una orden nunca retrocede su estatus.
   - Alternativa: (Plan ganador) IN_TRANSIT al despachar, que es lo que hace hoy la entrega especial del Lote 4. O bien asignar = PLANNED, con un retroceso controlado en StatusService.
3. DECISIÓN: la salida se registra desde este lote.
- POST /trips/{id}/start (trips.dispatch) usa TripLifecycleService.StartTrackedAsync, que reutilizará la app del Lote 7.
- El cierre (COMPLETED), los eventos por parada, el POD y los pings son del Lote 7; aquí solo se prueban sus efectos (ActualEnd e IsCurrent=0).
   - Alternativa: Sin endpoint de salida hasta el Lote 7: las órdenes despachadas se quedan en PLANNED hasta entonces. O agregar también un botón 'Cerrar ruta' manual.
4. DECISIÓN: despachar es irreversible. El 'reversible' de L271 se interpreta como la selección del modal.
   - Alternativa: Des-despachar (DISPATCHED → PLANNED) mientras la ruta no haya salido, lo que exige retroceso en StatusService.
5. DECISIÓN: una orden es elegible para una ruta si cumple todo:
- activa y no es entrega especial;
- en etapa PIPELINE posterior a la inicial y anterior a IN_TRANSIT;
- con la capacidad ASSIGN_TRIP;
- con parada DELIVERY pendiente.

Los laterales no se asignan.
   - Alternativa: Permitir FAILED para reintentos desde ya.
6. DECISIÓN: la ruta secuencia solo paradas DELIVERY pendientes; las PICKUP quedan fuera.
   - Alternativa: Incluir la PICKUP de las órdenes aún no recogidas antes de su DELIVERY.
7. DECISIÓN: una orden solo puede estar en una ruta vigente.
- En BD: IsCurrent más UX_TripOrder_Current.
- En código: 409 que nombra la ruta.
- IsCurrent pasa a 0 al completar el trip.
   - Alternativa: Índice único sin filtro, que impediría para siempre reasignar la orden.
8. DECISIÓN: liberar una orden es un DELETE físico de su TripOrder y de sus RouteStop de la versión vigente (L272).
- Solo ocurre en rutas no despachadas.
- Las RouteStop nacen PENDING sin fila de historial.
- Las versiones archivadas nunca se tocan.
   - Alternativa: Soft delete con una columna IsActive nueva, más historial de nacimiento por parada.
9. DECISIÓN: segunda barrera multi-tenant en SQL con FKs compuestas (Id, TenantId) en TripOrder, Trip y OptimizationRun.
   - Alternativa: Confiar solo en el filtro de tenant de EF.
10. DECISIÓN: nueva columna Trip.DispatchZoneId (NULL). La necesitan la 'ruta abierta de la zona' (R16), la reasignación en bloque y 'Planificar el día'.
   - Alternativa: Inferir la zona de la zona primaria del chofer.
11. DECISIÓN: concurrencia con bloqueo pesimista y un ORDEN DE BLOQUEO ÚNICO:
1. fila Tenant (solo 'Planificar el día');
2. Trips por TripId ascendente;
3. órdenes por id ascendente en una sola sentencia.

Además: re-verificación bajo bloqueo; índices únicos como última línea; el RowVersion del Trip cambia en cada mutación.
   - Alternativa: Solo RowVersion optimista: los escaneos simultáneos terminarían en 409 y reintentos manuales.
12. DECISIÓN: optimización en tres fases.
1. Instantánea y corrida PENDING dentro de una transacción.
2. El motor corre fuera de la transacción, con timeout de 60 s.
3. El resultado se aplica solo si la ruta vigente y el RowVersion no cambiaron; si cambiaron, ERROR y 409 'La ruta cambió mientras se optimizaba; vuelva a optimizar.'

Así VROOM/OR-Tools no retendrán bloqueos de fila.
   - Alternativa: (Plan ganador) Una sola transacción con el Trip bloqueado durante el cálculo. Es más simple, y aceptable mientras el motor sea en proceso; dos optimizaciones simultáneas darían siempre 200 y 200.
13. DECISIÓN: el motor del lote es 'HEURISTIC', un LookupCode nuevo: determinista, respeta la capacidad y no hace llamadas externas. Toda respuesta de un motor se valida antes de escribirla.
   - Alternativa: Registrar las corridas como MANUAL, sin código nuevo.
14. DECISIÓN: las paradas que no caben al optimizar se LIBERAN (vuelven a 'sin asignar').
- La respuesta y la ficha (LastRunUnassigned, leída con RoutePlanJson tolerante) muestran su motivo mientras sigan sin ruta.
   - Alternativa: Dejarlas en el trip, fuera de la ruta, en un panel propio.
15. DECISIÓN: optimizar y reordenar se comportan distinto.
- Optimizar crea una versión nueva; si el trip estaba en DRAFT, pasa a PLANNED.
- Reordenar edita la versión vigente y recalcula las ETAs.
   - Alternativa: Cada reordenamiento crea una versión con una corrida MANUAL.
16. DECISIÓN (V8): ETA sin motor de ruteo.
- Con coordenadas: haversine × 1.3 a 35 km/h.
- Sin ellas: 15 min por tramo; el primer tramo siempre usa ese default.
- Se espera hasta el inicio de la ventana y se suma el tiempo de servicio.
- Las constantes viven en código.
- Si no se indica hora de salida, se usa 12:00 UTC (08:00 AST) de PlanDate, para que siempre haya ETA.
   - Alternativa: Constantes configurables por compañía, y sin hora de salida por defecto: sin ETA y con el aviso NO_PLANNED_START.
17. DECISIÓN: coordenadas, pin y pings se leen y escriben con SQL crudo confinado en TripQueries, con TenantId explícito y sin NetTopologySuite. No hay geocodificador en este lote.
   - Alternativa: Agregar NetTopologySuite y mapear GeoPoint.
18. DECISIÓN: pin manual por parada.
- PUT /trips/{id}/stops/{routeStopId}/location (trips.plan) fija la coordenada y GeocodeAccuracy MANUAL, y recalcula las ETAs.
- Solo en rutas DRAFT/PLANNED.
- El AuditLog registra el cambio de precisión, pero NO la coordenada: GEOGRAPHY no está mapeada.
   - Alternativa: Permitir el pin también en rutas despachadas, porque corrige la navegación del chofer, o auditar la coordenada con una entrada manual.
19. DECISIÓN: el último ping de una ruta es el último DriverLocationPing con ese TripId, por CapturedAtUtc. Si no hay, se usa el último ping del chofer sin TripId desde ActualStartUtc, marcado LinkedToTrip=false.
   - Alternativa: Solo pings con TripId.
20. DECISIÓN: la capacidad del vehículo y el máximo de paradas del chofer solo AVISAN en ediciones manuales, en el selector y al despachar. Solo el optimizador respeta la capacidad.
   - Alternativa: Bloquear el despacho o el alta que exceda la capacidad física.
21. DECISIÓN: límites técnicos duros:
- 300 paradas por ruta;
- 200 órdenes por solicitud;
- 50 rutas por despacho en lote;
- 50 zonas por reasignación o planificación.
   - Alternativa: Sin topes técnicos.
22. DECISIÓN (V3): la disponibilidad se valida con FleetAvailabilityService (Lote 4).
- BLOQUEA al asignar (409) y al despachar (422).
- Que el chofer o el vehículo esté en otra ruta ese mismo día solo avisa (DRIVER/VEHICLE_DOUBLE_BOOKED).
- FleetAssignment no se usa.
   - Alternativa: Bloquear la doble asignación del mismo día.
23. DECISIÓN: chofer por defecto al crear una ruta con zona y sin chofer (también en 'Planificar el día'): el que tiene esa zona como primaria, solo si es único, está disponible y CATALOG está encendido. No se asigna vehículo por defecto.
   - Alternativa: Solo sugerirlo, o tomar también un vehículo por defecto (FleetAssignment del chofer, cuando exista en código).
24. DECISIÓN: la reasignación en bloque cambia el chofer de las rutas ABIERTAS de esas zonas en esa fecha.
- No toca DriverZone ni rutas despachadas.
- Su rastro es el AuditLog del cambio de DriverId por ruta; no hay tabla de motivos.
   - Alternativa: Actualizar también la zona primaria del chofer, o una tabla de reasignaciones con motivo.
25. DECISIÓN: el historial de chofer y vehículo de una ruta (planeado frente a real) es el AuditLog de Trip. No hay tabla nueva de asignaciones.
   - Alternativa: Tabla TripAssignmentHistory con quién, cuándo y motivo.
26. DECISIÓN: la cabecera de una ruta (chofer, vehículo, hora de salida) se gobierna con la capacidad EDIT_TRIP en StatusCapability.
- Viene negada desde DISPATCHED, IN_PROGRESS, COMPLETED y CANCELLED.
- El tenant puede habilitarla en DISPATCHED/IN_PROGRESS, por ejemplo para cambiar el chofer si el camión se avería.
- La fecha y la zona nunca cambian después de despachar.
- El contenido (paradas y secuencia) queda congelado por regla fija.
   - Alternativa: (Plan ganador) Regla fija: nada se edita después de despachar.
27. DECISIÓN (V4): los endpoints van bajo [RequireModule(LTL_GROUND)].
- Los servicios exigen además CATALOG cuando tocan choferes o vehículos.
- Zonas y miembros siguen en CATALOG.
   - Alternativa: Un módulo TRIPS/DISPATCH propio, activable por tenant.
28. DECISIÓN (V7): dos permisos nuevos, trips.view y trips.scan (54 en total).
- Plantillas:
  - Dispatcher: view y scan;
  - WarehouseOperator: view y scan, sin plan;
  - ReadOnly: view.
- Permisos por acción:
  - reordenar, pin, 'Planificar el día' → trips.plan;
  - optimizar → trips.optimize;
  - selector, despachar y salida → trips.dispatch.
   - Alternativa: Reutilizar trips.plan para leer y escanear.
29. DECISIÓN: el escaneo Outbound reutiliza OrderReadService.LookupAsync, con la precedencia de /orders/lookup: número > empaque > factura.
- Varias coincidencias → 'no encontrado', con mensaje.
- Responde siempre 200, con resultado tipado y palabra de voz.
- 'Ya escaneado' = la orden ya tiene ruta vigente, y gana a 'no elegible'.
- 'Ruta abierta' = una ruta DRAFT/PLANNED de la zona y la fecha; si hay varias, la de menor id.
   - Alternativa: (Plan ganador) Empaque primero; 404 para 'no encontrado'; una tabla ScanEvent; o contar como abiertas las rutas ya despachadas.
30. DECISIÓN: NOT_ELIGIBLE dice 'No' por voz.
   - Alternativa: Decir 'Sí' y mostrar el aviso solo en pantalla.
31. DECISIÓN (V6): resolución de zona.
- Precedencia: CP exacto > rango postal > municipio (sin acentos).
- Un empate en el mismo nivel = ambigua.
- POLYGON → 400.
- Sin solapamiento entre zonas activas, validado también al reactivar.
- Quitar un miembro es un DELETE físico auditado.
   - Alternativa: Solapamiento con prioridad explícita, y soft delete de miembros.
32. DECISIÓN: número de ruta automático 'AAAA-####' (NumberSequence TRIP).
- No se reinicia por año.
- Es inmutable y nunca se reutiliza.
   - Alternativa: 'RT-#####', o un consecutivo que se reinicia cada año.
33. DECISIÓN: eliminar una ruta la deja en CANCELLED con IsActive=0 y libera sus órdenes. Solo se puede desde DRAFT/PLANNED.
   - Alternativa: Permitir eliminar una ruta despachada, mandando sus órdenes a un lateral.
34. DECISIÓN: la orden y la ruta se afectan así.
- Cancelar una orden, o mandarla a un lateral (ON_HOLD/PARTIAL/FAILED), la libera de una ruta abierta.
- Cancelar una orden que va en una ruta despachada → 422.
- Un lateral en una ruta despachada no cambia nada; decide el Lote 7.
   - Alternativa: No liberar en laterales: la orden se queda en la ruta y bloquea el despacho con 'Orden N: …' hasta que el despachador la quite.
35. DECISIÓN: el despacho en lote es por ruta: cada una en su transacción, con un resumen 200.
   - Alternativa: Todo o nada.
36. DECISIÓN: despachar una ruta nunca optimizada recorre DRAFT → OPTIMIZED → ACTIVE con el comentario 'Secuencia manual confirmada al despachar'.
   - Alternativa: Exigir optimizar antes de despachar.
37. DECISIÓN: sin dinero en este lote.
- Despachar no crea DriverTrip.
- Ningún DTO de trips ni la fuente TRIP expone Amount ni Rate (lo prueban las pruebas de contratos).
   - Alternativa: Un DriverTrip de tipo 'ruta' por despacho.
38. DECISIÓN (V5): definiciones de los indicadores.
- 'Órdenes en excepción' = ON_HOLD, PARTIAL y FAILED; CANCELLED no cuenta.
- 'Sin chofer asignado' = activas en CONFIRMED..PLANNED sin ruta vigente con chofer y sin DriverTrip.
   - Alternativa: Incluir CANCELLED en excepción, o contar también las que ya están en tránsito.
39. DECISIÓN: OptimizationRunId pasa a INT en el SQL.
- Sin conversión de tipo para EntityStatusHistory.EntityId ni para IOwnedEntityResolver.
- La corrida lleva historial bajo OPTIMIZATION_RUN.
- No se audita en AuditLog, porque ya es bitácora.
   - Alternativa: (Plan ganador) Conservar BIGINT y convertir con checked a int.
40. DECISIÓN: 'hoy' se toma en UTC. El escaneo, el monitor, el selector, el listado y 'Planificar el día' aceptan una fecha explícita.
   - Alternativa: Zona horaria por Tenant.
41. DECISIÓN: la ficha de la orden muestra ruta y chofer de la ruta solo a usuarios internos, no al portal.
   - Alternativa: Exponer el chofer también al portal.
42. DECISIÓN: la fecha del plan puede estar entre ayer y +60 días. La hora de salida debe caer en esa fecha, con ±12 h de margen.
   - Alternativa: Sin restricción de fechas.
43. DECISIÓN: 'Planificar el día' (POST /trips/plan-day, trips.plan) está DENTRO del lote (L262).
- Por zona: usa la ruta abierta de la fecha o crea una con el chofer estándar.
- Le agrega las órdenes sin asignar de esa zona cuya RequestedDate es ≤ PlanDate o nula.
- Es idempotente y está serializado por tenant.
- No optimiza, no despacha y no asigna vehículo.
   - Alternativa: Dejarlo fuera (rutas armadas solo a mano, por escaneo o por zona), o tomar solo las órdenes con RequestedDate = PlanDate.
44. DECISIÓN: 'Planificar el día' crea rutas solo para zonas con órdenes, salvo con CreateEmptyTrips=true. En ese caso también las crea vacías, para que el escaneo Outbound tenga ruta destino desde temprano.
   - Alternativa: Crear siempre una ruta por zona activa.
45. DECISIÓN: en el monitor, los totales se calculan sobre la fecha y la zona, y la búsqueda libre solo filtra la lista (analogía con L650: buscar no altera contadores).
   - Alternativa: Que los totales reflejen lo filtrado por la búsqueda.
46. DECISIÓN: los totales de Route (distancia, duración, StopCount) los escribe la aplicación (RouteWriter.RecomputeAsync) en cada mutación. No hay columnas computadas ni triggers.
   - Alternativa: Columnas computadas o una vista sobre RouteStop.
47. DECISIÓN: fuera de alcance explícito.
- Cross-dock y DockAppointment (R20, V10): se anota para el Lote 6.
- Unicidad de FleetAssignment (vacío del Lote 4).
- Almacenes / OriginWarehouse.
- Motores VROOM/OSRM/OR-Tools reales.
- Geocodificador.
   - Alternativa: Adelantar el geocodificador por código postal (centroide) para marcar ZIP_CENTROID automáticamente.

## Pruebas unitarias

- tests/Teikem.Tests/TripCatalogTests.cs (P0):
- Las constantes coinciden con los literales del seed: estatus, ZoneMatchType, OptimizerEngine (con HEURISTIC), GeocodeAccuracy y Capability EDIT_TRIP.
- EntityTypes ROUTE, ROUTE_STOP y OPTIMIZATION_RUN sembrados.
- 54 permisos, con trips.view y trips.scan en #P.
- Plantillas: Dispatcher, WarehouseOperator sin trips.plan, ReadOnly solo con trips.view, Driver sin trips.*.
- OwnerRead/OwnerWrite.
- IsKnownKind('TRIP') y CK_NumberSequence_Kind con 'TRIP'.
- El seed contiene el bloque 3E (EDIT_TRIP negada en DISPATCHED, IN_PROGRESS, COMPLETED y CANCELLED) y el 3F (laterales de TRIP y ROUTE).
- El SQL declara 'OptimizationRunId INT IDENTITY' e IX_LocationPing_Driver.
- tests/Teikem.Tests/TripContractsTests.cs (P0), por reflexión:
- Firma posicional exacta de todos los records de TripContracts (incluidos PlanDay*, StopLocationRequest, TripStartRequest, MonitorDto y MonitorTotalsDto), de los DTOs nuevos de DriverContracts y de la cola de OrderDetailDto.
- Todos son sealed.
- TripPatchRequest tiene Extra.
- Ningún request tiene TenantId ni ids int de Trip/Route.
- NINGUNA propiedad de un DTO de trips contiene 'Amount' ni 'Rate'.
- OptimizationResultDto.RunId y OptimizationRunDto.Id son int.
- Los tipos de TripSeams no viven en el namespace Contracts.
- tests/Teikem.Tests/FleetContractsTests.cs (P0, se AJUSTA): la cola de OrderDetailDto pasa a ser AssignedDriverPublicId, AssignedDriverCode, AssignedDriverName, AssignedTripPublicId, AssignedTripCode, todos con default. Se conservan las prohibiciones de Amount y Rate.
- tests/Teikem.Tests/TripRulesTests.cs (P0):
- IsEditable y NotEditableMessage.
- CheckEligibility con cada mensaje exacto.
- OverStopLimit.
- BuildIssues: bloqueantes y avisos exactos.
- DispatchBlockingMessage.
- ValidateSequence.
- PickOpenTrip devuelve el menor id.
- Cultura invariante.
- tests/Teikem.Tests/EtaCalculatorTests.cs (P0):
- Vacío, sin start, sin coordenadas.
- San Juan→Bayamón ≈ 11.8 km y 21 min.
- Espera de ventana y tardía.
- 3 decimales; total null sin puntos; primer tramo por defecto.
- Determinismo.
- tests/Teikem.Tests/DispatchZoneMatcherTests.cs (P0):
- NormalizeMember, con cada mensaje.
- Resolve: precedencia, rango, municipio sin acentos, ambigua, ZIP+4.
- FindConflict.
- tests/Teikem.Tests/RouteOptimizationRulesTests.cs (P0): ValidateResult.
- Válido.
- Id ajeno.
- Id repetido.
- Parada ausente.
- Parada en ambos grupos.
- tests/Teikem.Tests/RoutePlanJsonTests.cs (P0):
- Serialize → ParseUnassigned hace ida y vuelta.
- null, '', 'basura', '[]', '{}' y un objeto sin 'unassigned' → lista vacía, sin excepción.
- Elementos con campos faltantes se omiten.
- tests/Teikem.Tests/RouteWriterTests.cs (P0; InMemory con StatusService real):
- AddOrders crea TripOrder IsCurrent y RouteStop PENDING 1..N; Route v1 DRAFT con historial y sin historial de paradas.
- Duplicado → 409; orden en otra ruta → 409 con el código; DRAFT o especial → 422 atómico.
- ReleaseOrder resecuencia; una orden ajena → 404.
- Liberar NO toca las RouteStop de versiones ARCHIVED ni los TripOrder de otros trips.
- ReleaseAll deja intactos los demás trips.
- Operaciones de contenido en un Trip DISPATCHED → 422.
- RecomputeAsync SÍ se permite en DISPATCHED y lanza InvalidOperationException en un terminal.
- ReplaceActiveRoute: n+1 y una sola activa.
- ApplySequence inválida → 400.
- Un Trip no tracked → InvalidOperationException.
- tests/Teikem.Tests/RawSqlConfinementTests.cs (P0):
- FromSql*, SqlQuery y ExecuteSql* solo aparecen en FleetQueries.cs, NumberSequenceService.cs, DriverPayPolicyService.cs y TripQueries.cs.
- Cada sentencia de TripQueries contiene 'TenantId =': bloqueos de Trip, órdenes y Tenant; puntos; pin; pings de trip y de chofer.
- tests/Teikem.Tests/OrderDispatchFlagsTests.cs (P0): IsException y PendingDispatchStatuses.
- tests/Teikem.Tests/TenantIsolationModelTests.cs (P0, se amplía):
- Filtro global en Trip, TripOrder y OptimizationRun.
- Las entidades de Trips sin TenantId son exactamente Route y RouteStop; Fleet agrega DispatchZoneMember.
- Índices espejo con nombre y filtro.
- RowVersion como token de concurrencia.
- decimal(12,3).
- Tablas 1:1.
- PlanDate de tipo date.
- OptimizationRunId de tipo int.
- tests/Teikem.Tests/OrderCatalogTests.cs (P0): 52 → 54 permisos.
- tests/Teikem.Tests/OwnedEntityResolverCoverageTests.cs (existente, sin cambios): pasa con los 4 resolvers nuevos.
- tests/Teikem.Tests/TripPlanningRulesTests.cs (P1):
- FormatCode.
- ValidatePlanDate y ValidatePlannedStart.
- DefaultPlannedStart(2026-09-26) = 2026-09-26T12:00Z.
- PickDefaultDriver.
- ForbiddenPatchKeys y flags en conflicto.
- ValidateDispatchedPatch: fecha o zona → DispatchedHeaderMessage; chofer, vehículo u hora → ok.
- tests/Teikem.Tests/TripOrderRequestRulesTests.cs (P2): ValidateAdd y NormalizeUnassignedQuery.
- tests/Teikem.Tests/HeuristicRoutePlannerTests.cs (P3):
- Orden y desempates.
- Determinismo.
- CAPACITY_STOPS, CAPACITY_WEIGHT (con una liviana posterior que sí entra) y CAPACITY_VOLUME.
- Sin límites.
- Polyline null.
- El resultado pasa ValidateResult.
- tests/Teikem.Tests/RouteEditRulesTests.cs (P3):
- ValidateLocation: faltante, lat 91, lng -181 y válido.
- IsStale: mismo RowVersion y ruta → false; RowVersion distinto, RouteId distinto o no editable → true.
- tests/Teikem.Tests/TripStatusEffectTests.cs (P4; InMemory):
- CANCELLED desde PLANNED libera y archiva; desde DISPATCHED → 422.
- DISPATCHED desde PLANNED:
  - ruta ACTIVE;
  - órdenes de CONFIRMED a PLANNED con historial PICKUP, INBOUND, PLANNED y el comentario 'Despachada en la ruta X';
  - ninguna en IN_TRANSIT;
  - paradas PENDING.
- Con PLANNED deshabilitado, las órdenes quedan en INBOUND.
- DISPATCHED sin vehículo → 422.
- IN_PROGRESS: ActualStartUtc y órdenes PLANNED → IN_TRANSIT con 'Salió en la ruta X'.
- COMPLETED: IsCurrent=0.
- Nacimiento: sin efectos.
- TripLifecycleService.StartTrackedAsync: PLANNED → 422 NotDispatchedForStart; IN_PROGRESS → 422 AlreadyStarted.
- TripOrderReleaseEffect:
  - CANCELLED en ruta PLANNED → liberada;
  - en ruta DISPATCHED → 422;
  - ON_HOLD en DRAFT → liberada;
  - sin ruta o de otra entidad → no-op.
- tests/Teikem.Tests/DispatchBatchRulesTests.cs (P4): Validate, Summarize y mensajes de salida.
- tests/Teikem.Tests/DispatchZoneMembershipTests.cs (P5; InMemory):
- Conflicto con zona activa; una inactiva no choca.
- Reactivación con choque → 409.
- Miembro ajeno → 404.
- Criterio desconocido y POLYGON → 400.
- Inactivar con ruta abierta → 409.
- tests/Teikem.Tests/OutboundScanRulesTests.cs (P6):
- NormalizeCode.
- Voice.
- Decide, con la precedencia completa:
  - 0 coincidencias → NOT_FOUND;
  - 2 → NOT_FOUND ambiguo;
  - ruta vigente + no elegible → ALREADY_ASSIGNED (gana);
  - no elegible sin ruta → NOT_ELIGIBLE;
  - sin zona → NO_ZONE;
  - ambigua → AMBIGUOUS_ZONE con los códigos;
  - sin ruta abierta → NO_OPEN_ROUTE con zona y fecha;
  - todo ok → FOUND_ASSIGNED.
- Mensajes exactos.
- tests/Teikem.Tests/MonitorRulesTests.cs (P7):
- Progress, NextEta y ApproximateCount.
- Totals no cambia al aplicar una búsqueda.
- ApplySearch actúa sobre la lista ya filtrada y compara sin mayúsculas.
- tests/Teikem.Tests/AnalyticsSeedFieldsTests.cs (P7, antes en P0):
- Cada campo del bloque '// Lote 5' del seeder existe en TripDataSource o en TransportOrderDataSource.
- TripDataSource: Key TRIP, DateField PlanDate, relaciones, sin campos Amount ni Rate.
- TransportOrderDataSource: TripCode, HasAssignedDriver, IsException, DispatchZoneCode y la relación Trip.
- tests/Teikem.Tests/DayPlanningRulesTests.cs (P9):
- ValidateRequest: 51 zonas y duplicados.
- Allocate:
  - agrupa por zona;
  - reutiliza la ruta abierta (CreateTrip=false);
  - crea solo con órdenes, salvo createEmptyTrips;
  - IneligibleReason → Skipped;
  - tope 300 con 295 paradas previas → 5 asignadas y el resto CAPACITY_HARD_CAP;
  - ignora zonas fuera de la lista;
  - cuenta las sin zona;
  - misma entrada → misma salida.
- tests/Teikem.Tests/TripControllerSecurityTests.cs (P8):
- Mapa (controlador, acción) → permiso, exactamente uno por acción, incluidos start, stops/location y plan-day.
- [RequireModule(LTL_GROUND)] en cada controlador.
- Sin parámetro tenantId.
- Ningún archivo de src/Teikem.Api contiene 'using Teikem.Domain.Trips;'.

## Pasos de smoke

- db-init dos veces sobre una BD limpia.
- 54 permisos; la segunda corrida es idempotente.
- Propagación a roles clonados: Despachador y Operador de almacén con trips.view y trips.scan; Solo lectura con trips.view.
- trips (Lote 5): permisos, catálogos, pipelines, capacidades y laterales.
- /me: admin con trips.view, plan, optimize, dispatch y scan.
- T2, el operador de almacén (sin trips.plan) y 'lectura$TS' tienen lo esperado.
- GET /status/TripStatus, RouteStatus, RouteStopStatus y OptimizationRunStatus → 200.
- /catalogs/OptimizerEngine contiene HEURISTIC.
- GET /status/capabilities/TRIP: EDIT_TRIP negada en DISPATCHED.
- GET /status/lateral-entries/TRIP: CANCELLED desde DRAFT y PLANNED.
- zonas (Lote 5): el mismo bloque del plan ganador.
- Z1$TS/Z2$TS con miembros POSTAL_CODE, ZIP+4, POSTAL_RANGE y MUNICIPALITY.
- Errores:
  - 400 'El código postal debe tener 5 dígitos (ej. 00949).'
  - POLYGON → 400
  - 400 'Criterio de zona desconocido: 'FOO'.'
  - 409 'El valor 'ZIP1' ya pertenece a la zona Z1$TS.'
  - 409 'La zona ya tiene ese criterio.'
- resolve por CP, rango y municipio en minúsculas; 99999 → null; sin parámetros → 400.
- DELETE de un miembro con la zona equivocada → 404 'Criterio de zona no encontrado.'
- DELETE correcto → 204.
- prerrequisitos (Lote 5).
- Consignatarios en 'Pueblo A $TS' (ZIP1), 'Pueblo B $TS', 'Pueblo C $TS' (Z3$TS) y 'Sin Zona $TS'.
- Órdenes confirmadas:
  - OA1..OA4 y OC1..OC8 en Z1;
  - OB1..OB2 en Z2;
  - OP1..OP3 en Z3;
  - ON1 sin zona.
- OD en DRAFT.
- Choferes con licencia vigente:
  - D1 (primaria Z1, maxStopsPerRoute 1);
  - D2;
  - D3 (primaria Z3).
- DX con licencia vencida.
- Vehículos V1 (maxStops 2) y V2.
- alta de ruta (Lote 5).
- POST /trips {planDate: hoy, dispatchZoneId: Z1, vehiclePublicId: V1} → 200:
  - code ^\d{4}-\d{4,}$;
  - DRAFT;
  - driverCode D1 (chofer por defecto);
  - plannedStartUtc = hoy 12:00Z (salida por defecto);
  - stopCount 0.
- Historial TRIP: 1 fila.
- Errores:
  - fecha −3 → 400;
  - zona inactiva → 400;
  - DX → 409 'El chofer no está disponible para despacho…';
  - PATCH {code} → 400;
  - PATCH con rowVersion viejo → 409.
- Crea TR2 (Z2, D2, V2) y TR3 (Z1, sin vehículo).
- números concurrentes (Lote 5).
- 8 POST /trips simultáneos (sin zona, hoy) → 8 respuestas 200 con códigos distintos, del mismo año y con consecutivos sin huecos entre sí.
- Después se eliminan (DELETE → 204) para no ensuciar el selector.
- sin asignar y agregar órdenes (Lote 5).
- unassigned-orders?dispatchZoneId=Z1 incluye OA1..OA4 y excluye OD y la entrega especial.
- ?noZone=true incluye ON1.
- ?search= filtra después de dispatchZoneId.
- POST /trips/{TR1}/orders [OA1,OA2] → 200: sequence 1..2, v1 DRAFT, plannedArrivalUtc no nulo.
- Errores:
  - [OA3, OD] → 422 atómico con 'La orden está en Entrada; confírmela antes de asignarla a una ruta.'
  - Entrega especial → 422.
  - [OA1] otra vez → 409 'La orden ya está en esta ruta.'
- GET /orders/{OA1}: sigue CONFIRMED, con assignedTripCode TR1 y assignedDriverCode D1.
- concurrencia de asignación (Lote 5). OA3 a TR1 y OA3 a TR3 en paralelo:
- un 200 y un 409 'ya está asignada a';
- OA3 aparece en una sola ruta.
- quitar y liberar (Lote 5).
- DELETE /trips/{TR1}/orders/{OA1} → 200: secuencias contiguas; OA1 vuelve a sin asignar con su estatus.
- OB1, que no está en TR1 → 404 'La orden no está en esta ruta.'
- AuditLog tiene el DELETE del TripOrder.
- pin manual y ETAs (Lote 5).
- PUT /trips/{TR1}/stops/{stop1}/location {lat:18.4655,lng:-66.1057} y stop2 {18.3985,-66.1553} → 200:
  - geocodeAccuracyCode MANUAL, isApproximate false, point presente;
  - distanceFromPrevKm de la parada 2 entre 11.6 y 12.0.
- Errores:
  - {lat:91,lng:0} → 400 'La latitud debe estar entre -90 y 90.'
  - {} → 400 'Indique la latitud y la longitud.'
  - routeStopId de TR2 por la URL de TR1 → 404 'Parada no encontrada en esta ruta.'
- AuditLog TRANSPORT_ORDER registra el cambio de GeocodeAccuracy.
- optimizar, versiones y secuencia (Lote 5).
- TR1 con 3 paradas y V1 maxStops 2 → optimize 200:
  - HEURISTIC OK, routeVersion 2;
  - unassigned 1 con CAPACITY_STOPS; la orden vuelve a sin asignar;
  - trip PLANNED, ruta OPTIMIZED.
- GET /trips/{TR1}: lastRunUnassigned lista esa orden con reason 'Excede el máximo de paradas del vehículo.'
- La v1 queda ARCHIVED; optimization-runs tiene 1 OK.
- PUT route/sequence con los ids invertidos → 200: misma versión, ETAs crecientes.
- Secuencia con un id de TR2 o con uno faltante → 400 'La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.'
- Dos optimize simultáneos:
  - cada respuesta es 200 o 409 'La ruta cambió mientras se optimizaba; vuelva a optimizar.', con al menos un 200;
  - corridas OK = número de 200; corridas ERROR = número de 409;
  - una sola ruta vigente, con routeVersion = 2 + número de 200.
- Optimizar TR3 sin paradas → 422 'La ruta no tiene paradas que optimizar.' y no crea corrida.
- alerta de máximo de paradas (Lote 5).
- TR1 con D1 (máximo 1) y 2 paradas → overStopLimit true e issue OVER_STOP_LIMIT 'La ruta tiene 2 paradas y el máximo del chofer es 1.'
- El indicador 'Rutas sobre el máximo de paradas' vale ≥ 1.
- planificar el día (Lote 5).
- POST /trips/plan-day {planDate: hoy, dispatchZoneIds:[Z3]} → 200: tripsCreated 1, ordersAssigned 3; la ruta tiene driverCode D3, plannedStartUtc hoy 12:00Z y 3 paradas.
- Repetir → tripsCreated 0, ordersAssigned 0 (idempotente).
- Crear OP4 en Z3 y repetir → ordersAssigned 1, en la MISMA ruta.
- Z4$TS sin órdenes:
  - {dispatchZoneIds:[Z4]} → tripsCreated 0;
  - con createEmptyTrips:true → tripsCreated 1 y 0 paradas.
- Dos plan-day simultáneos {[Z5], createEmptyTrips:true} → GET /trips?dispatchZoneId=Z5&date=hoy devuelve UNA sola ruta.
- Errores: 51 zonas → 400 'Máximo 50 zonas por planificación.'; zona de otro tenant → 404.
- 'lectura$TS' → 403.
- escaneo Outbound (Lote 5).
- Empaque de OA4 → FOUND_ASSIGNED a TR1 (found), matchedBy PACK_BATCH.
- Otra vez → ALREADY_ASSIGNED (dup).
- 'NOEXISTE$TS' → NOT_FOUND.
- OD → NOT_ELIGIBLE (notfound).
- OB2 → FOUND_ASSIGNED a TR2.
- ON1 → FOUND_UNASSIGNED NO_ZONE.
- Número de orden de OB1 → FOUND_ASSIGNED, matchedBy ORDER_NUMBER.
- Orden nueva de Z4 → FOUND_ASSIGNED a la ruta vacía creada por plan-day.
- '' → 400 'Escanee o escriba un código.'
- Operador de almacén → 200; 'lectura$TS' → 403.
- 8 escaneos simultáneos de OC1..OC8 → los 8 FOUND_ASSIGNED; secuencias 1..N sin huecos ni duplicados.
- reasignación en bloque (Lote 5).
- {hoy, [Z1,Z2], D2} → 200: tripsUpdated = rutas abiertas, todas con D2, e issues DRIVER_DOUBLE_BOOKED.
- DX → 409.
- [] → 400 'Indique al menos una zona.'
- bloqueantes de despacho vistos desde fuera (Lote 5).
- Chofer no disponible:
  - TR2 PLANNED con D2; el chofer pasa a UNAVAILABLE (Lote 4);
  - dispatchable muestra TR2 con canDispatch false y DRIVER_UNAVAILABLE;
  - POST /trips/{TR2}/dispatch → 422 que contiene 'no se puede despachar' y 'El chofer no está disponible para despacho:';
  - TR2 sigue PLANNED; se restaura D2.
- Orden no elegible:
  - PUT /status/capabilities/TRANSPORT_ORDER apaga ASSIGN_TRIP en CONFIRMED;
  - dispatch de TR2 → 422 que cita 'Orden {número de OB2}: El estatus actual no permite la acción 'ASSIGN_TRIP'.';
  - se restaura la capacidad.
- despacho y salida (Lote 5).
- dispatchable?date=hoy: TR3 con canDispatch false y NO_VEHICLE. Dispatch de TR3 → 422.
- Dispatch de TR1 → 200: DISPATCHED, ruta ACTIVE, isEditable false, canEditHeader false.
- GET /orders/{OA2}: status PLANNED (NO IN_TRANSIT), con historial PICKUP, INBOUND, PLANNED y 'Despachada en la ruta …'.
- dispatchable ya no incluye TR1.
- Sobre TR1, todas → 422 'La ruta … ya fue despachada; no se puede editar ni eliminar.':
  - PATCH;
  - POST orders;
  - optimize;
  - sequence;
  - stops/location;
  - DELETE.
- POST /trips/{TR1}/start → 200: IN_PROGRESS y actualStartUtc. OA2 queda IN_TRANSIT, con 'Salió en la ruta …'.
- start otra vez → 422 'La ruta … ya salió.'
- start de TR3 (DRAFT) → 422 'La ruta … no está despachada; despáchela antes de registrar su salida.'
- POST /orders/{una orden de TR1}/cancel → 422 'La orden va en la ruta … ya despachada; no se puede cancelar mientras la ruta esté en curso.'
- Lote {TR2, TR3, guid ajeno} → 200: requested 3, dispatched 1, el ajeno con 'Ruta no encontrada.'
- cabecera con EDIT_TRIP (Lote 5).
- PATCH /trips/{TR2} (DISPATCHED) {driverPublicId: D1} → 422.
- El tenant habilita EDIT_TRIP en DISPATCHED (PUT /status/capabilities/TRIP):
  - el mismo PATCH → 200 con D1;
  - PATCH {planDate: mañana} → 422 'La fecha y la zona de una ruta despachada no se cambian.';
  - POST /trips/{TR2}/orders → sigue 422 (contenido congelado).
- Se restaura la capacidad.
- eliminar ruta y cancelar órdenes (Lote 5).
- TR4 abierta con 2 órdenes; cancelar una → TR4 stopCount 1.
- DELETE TR4 → 204: CANCELLED, isActive false, la orden liberada.
- DELETE TR1 → 422.
- Escanear una orden de Z1 sin ruta abierta → FOUND_UNASSIGNED NO_OPEN_ROUTE.
- monitoreo (Lote 5).
- GET /trips/monitor?date=hoy:
  - trips incluye TR1 (IN_PROGRESS) y TR2;
  - completedStops 0, nextEtaUtc no nulo, lastPing null;
  - totals.trips ≥ 2.
- ?search=<código de TR1> → trips.length 1 y totals IGUAL al de la llamada sin búsqueda.
- No incluye rutas DRAFT/PLANNED.
- sin dinero (Lote 5).
- Los JSON de GET /trips/{TR1}, /trips, /trips/monitor y /trips/dispatchable no tienen ninguna clave que contenga 'amount' ni 'rate' (jq paths, sin distinguir mayúsculas).
- Despachar no creó DriverTrip.
- RBAC y módulo (Lote 5).
- 'lectura$TS': GET /trips 200; POST /trips, optimize, dispatch, start, plan-day y location → 403.
- Operador de almacén: POST /trips 403; unassigned-orders 200.
- T7 → 403.
- T2 puede crear, optimizar, despachar y registrar la salida.
- Con CATALOG apagado: POST /trips con chofer → 403 module_disabled; con zona y sin chofer → 200 sin chofer por defecto. Se vuelve a encender.
- aislamiento y BOLA por id hijo (Lote 5). Con T3:
- GET/PATCH/DELETE/dispatch/start de TR1 → 404 'Ruta no encontrada.'
- DELETE /trips/{TR2}/orders/{OB2} → 404.
- En su ruta T3R:
  - orders [OA3] → 404 'Orden no encontrado.';
  - sequence con RouteStopIds de TR1 → 400;
  - PUT /trips/{T3R}/stops/{routeStopId de TR1}/location → 404.
- plan-day con Z1 → 404.
- Escaneo del empaque de OA4 → NOT_FOUND.
- El monitor no lista TR1.
- /dispatch-zones/{Z1}/members → 404.
- Historial ROUTE de TR1 → vacío.
- fuentes, contenido de sistema y auditoría (Lote 5).
- /analytics/data-sources: TRIP con dateField PlanDate; TRANSPORT_ORDER con TripCode, HasAssignedDriver, IsException y DispatchZoneCode.
- La vista 'Rutas' devuelve TR1.
- 'Órdenes sin chofer asignado' ≥ 1 (ON1).
- 'Órdenes en excepción' ≥ 0.
- El gráfico 'Rutas por estatus' devuelve datos.
- AuditLog tiene TRIP (alta, cambio de chofer, DELETE de TripOrder), ROUTE (archivo), DISPATCH_ZONE (miembros) y TRANSPORT_ORDER (precisión MANUAL).

## Anexo: especificación extraída

### entidades

- **tabla**: Trip; **existeEnSql**: True; **notas**: L1380-1396. Dueño de vehículo/chofer/órdenes del día (Code, PlanDate, OriginWarehouseId, VehicleId, DriverId, StatusCodeId=TripStatus, PlannedStart/EndUtc, ActualStart/EndUtc, TotalDistanceKm/TotalDurationMin). EntityTypes.Trip='TRIP' ya existe en código; no hay [AuditEntity] confirmado todavía para Trip (verificar al implementar).
- **tabla**: TripOrder; **existeEnSql**: True; **notas**: L1398-1405. Puente Trip↔TransportOrder (SortHint, único por Trip+Order). Se borra al liberar la orden de la ruta (L272) — no se pierde la orden.
- **tabla**: Route; **existeEnSql**: True; **notas**: L1407-1416. Versionada (Version/IsActive) por Trip; StatusCodeId=RouteStatus. OJO: el dominio sembrado es DRAFT/OPTIMIZED/ACTIVE/ARCHIVED (seed L324), NO 'planificada/despachada/completada' como dice el texto de L271 — hay que decidir el mapeo (ver vacíos).
- **tabla**: RouteStop; **existeEnSql**: True; **notas**: L1418-1429. Secuencia sobre OrderStop, con distancia/duración desde la parada previa (insumo de ETA) y StatusCodeId=RouteStopStatus (PENDING/EN_ROUTE/COMPLETED/FAILED según seed — el doc en el módulo 8 App móvil usa 'en camino→llegada→completar/fallo', consistente).
- **tabla**: DispatchZone; **existeEnSql**: True; **notas**: L1438-1445. Ya CRUD mínimo del Lote 4 (DispatchZonesController + DispatchZoneService), con permiso fleet.view/fleet.manage. Reutilizar tal cual.
- **tabla**: DispatchZoneMember; **existeEnSql**: True; **notas**: L1448-1455. Match por ZIP o municipio (MatchTypeLookupId/MatchValue) — motor de resolución 'ZIP/pueblo → zona' es lo que debe construir Lote 5 para el escaneo Outbound (L273); no hay servicio de resolución todavía en src/.
- **tabla**: DriverZone; **existeEnSql**: True; **notas**: L1458-1463. Asignación estándar chofer↔zona (IsPrimary). Ya la usa FleetAvailabilityService.PrimaryZoneCodesAsync (Lote 4) — reutilizable para 'reasignar en bloque'.
- **tabla**: OptimizationRun; **existeEnSql**: True; **notas**: L1466-1477. Registro de cada corrida del optimizador (EngineLookupId=OptimizerEngine: VROOM/ORTOOLS/MANUAL, RequestJson/ResponseJson, UnassignedCount). Nada en src/ implementa IRouteOptimizer todavía.
- **tabla**: Tenant.MaxStopsPerRouteDefault; **existeEnSql**: True; **notas**: Columna en Tenant (L155 del SQL), default 25. Es el default de ALERTA, no un límite duro del vehículo (no confundir con Vehicle.MaxStops, que es capacidad física para el optimizador).
- **tabla**: Driver.MaxStopsPerRoute; **existeEnSql**: True; **notas**: Columna en Driver (L1129 del SQL), override NULL=usa el del tenant. CHECK >=1.
- **tabla**: Vehicle.MaxStops/MaxWeightKg/MaxVolumeM3; **existeEnSql**: True; **notas**: Vehicle L1092-1116. Capacidad física para el optimizador (respetar en IRouteOptimizer), concepto distinto de la alerta de paradas del chofer.
- **tabla**: OrderStop.GeocodeAccuracyLookupId; **existeEnSql**: True; **notas**: L1312-1334 (LookupDomains.GeocodeAccuracy ya existe en CatalogDomains.cs). EXACT/ZIP_CENTROID/CITY_CENTROID/MANUAL, NULL hasta geocodificar; alimenta el mapa (front) para distinguir pin exacto de aproximado — el backend solo expone el valor.
- **tabla**: DriverLocationPing; **existeEnSql**: True; **notas**: L1883-1892. GeoPoint+Speed+Heading+CapturedAtUtc, TripId opcional. Es del Lote 7 (app móvil) generarlo; Lote 5 solo debe exponer el último ping por parada/trip en el endpoint de monitoreo.
- **tabla**: FleetAssignment; **existeEnSql**: True; **notas**: L1598-1607. Existe en SQL pero NO es lo mismo que 'FleetAvailability' citado en la tarea: es asignación de propiedad chofer↔vehículo por rango de fechas (AssignmentTypeLookupId), no disponibilidad para despachar. La disponibilidad real ya se resuelve en código (ver reutilizar) sin tabla propia.
- **tabla**: FleetAvailability (tabla); **notas**: No existe como tabla — es una capacidad calculada en tiempo real por FleetAvailabilityService (Lote 4, src/Teikem.Infrastructure/Services/FleetAvailabilityService.cs), no persistida. El planificador de trips debe llamarla, no reinventar una tabla.
- **tabla**: DockAppointment / CrossDockPlan; **notas**: Fuera de alcance de este lote por instrucción explícita de la tarea (quedan para Lote 6/módulo 6). DockAppointment sí existe nombrada en el doc (L317) pero no se toca aquí; no busqué su tabla en el SQL a propósito.
- **tabla**: DriverTrip; **existeEnSql**: True; **notas**: L1611-1639. Ojo: es la fila de PAGO por viaje del módulo 11A (liquidación), NO la ruta/trip operativa de este lote — el comentario del propio SQL lo aclara ('distinta de dbo.Trip'). Se cita en la tarea como 'preparado por lotes anteriores' pero no aporta nada al dominio de rutas salvo la entrega especial que puede ATAR una TransportOrder a un DriverTrip de pago.
- **tabla**: IRouteOptimizer; **notas**: No existe ninguna interfaz/clase en src/ (grep sin resultados). Es 100% nuevo para este lote: la costura descrita en el doc (L264) y la implementación determinista por defecto (orden por zona y ventana, L de Principios de la tarea) hay que crearlas.
- **tabla**: Trip; **existeEnSql**: True; **notas**: Capa 12 (línea 1380). PK TripId, PublicId (exposición externa). FK: TenantId->Tenant, OriginWarehouseId->Warehouse (NULL), VehicleId->Vehicle (NULL), DriverId->Driver (NULL). StatusCodeId NOT NULL, Entity='TripStatus'. Code NVARCHAR(40) NOT NULL, UQ_Trip_Code (TenantId, Code). PlanDate DATE NOT NULL. Planned/Actual Start/End Utc. TotalDistanceKm, TotalDurationMin (probablemente calculados, sin CHECK ni computed column). IsActive BIT default 1, RowVersion. Índice IX_Trip_Tenant_Date (TenantId, PlanDate) WHERE IsActive=1.
- **tabla**: TripOrder; **existeEnSql**: True; **notas**: Línea 1398. Tabla puente Trip-TransportOrder. PK propio TripOrderId, FK TripId->Trip, TransportOrderId->TransportOrder. SortHint INT NULL (orden manual sugerido). UQ_TripOrder (TripId, TransportOrderId): una orden no se repite en el mismo trip. No tiene TenantId propio (se infiere via Trip/TransportOrder).
- **tabla**: Route; **existeEnSql**: True; **notas**: Línea 1407. FK TripId->Trip (NOT NULL, un trip puede tener varias rutas por Version). Version INT default 1, IsActive BIT default 1 (permite versionado: nueva versión reemplaza a la anterior). StatusCodeId Entity='RouteStatus'. TotalDistanceKm, TotalDurationMin, StopCount (NOT NULL default 0) probablemente derivados de RouteStop pero sin trigger/computed column visible en el SQL: se asume que la capa de aplicación los mantiene. CreatedAtUtc default SYSUTCDATETIME(). RowVersion. Índice IX_Route_Trip (TripId) WHERE IsActive=1 -> sugiere 'ruta activa por trip' pero NO hay índice único filtrado que fuerce una sola Route activa por Trip (solo índice normal, no UNIQUE).
- **tabla**: RouteStop; **existeEnSql**: True; **notas**: Línea 1418. FK RouteId->Route, OrderStopId->OrderStop (NOT NULL). Sequence INT NOT NULL. Planned/Actual Arrival/Departure Utc. DistanceFromPrevKm, DurationFromPrevMin (segmento respecto a la parada anterior). StatusCodeId Entity='RouteStopStatus'. UQ_RouteStop (RouteId, OrderStopId): una OrderStop no se repite en la misma ruta. Índice IX_RouteStop_Route (RouteId, Sequence) para lectura ordenada.
- **tabla**: DispatchZone; **existeEnSql**: True; **notas**: Línea 1438 (Capa 12B). Comentario de capa (líneas 1432-1437) aclara diseño: 'Territorio fijo (ej. R-01) que un chofer cubre habitualmente. Sirve para reasignar en bloque y filtrar en Despacho/Escaneo. Independiente de RateZone (esa es para tarifas)'. Code NVARCHAR(20) NOT NULL, UQ_DispatchZone (TenantId, Code). Name opcional. IsActive default 1.
- **tabla**: DispatchZoneMember; **existeEnSql**: True; **notas**: Línea 1448. FK DispatchZoneId->DispatchZone. MatchTypeLookupId Entity='ZoneMatchType' (reutiliza el catálogo, valores seed: POSTAL_CODE, POSTAL_RANGE, MUNICIPALITY, POLYGON). MatchValue NVARCHAR(120) NOT NULL, ejemplo comentado '00949' o 'Toa Baja'. Sin restricción de unicidad (una zona puede repetir criterios, o el mismo valor podría estar en varias zonas: sin CHECK que lo impida en SQL).
- **tabla**: DriverZone; **existeEnSql**: True; **notas**: Línea 1458. Comentario: 'Asignación estándar: qué chofer cubre qué zona(s) por defecto (no impide reasignar ese día)'. PK compuesta (DriverId, DispatchZoneId) -> un chofer no se repite en la misma zona. IsPrimary BIT default 1 (sin CHECK ni índice único que garantice una sola zona primaria por chofer: sería posible marcar IsPrimary=1 en varias filas sin que el SQL lo impida).
- **tabla**: FleetAssignment; **existeEnSql**: True; **notas**: Línea 1598. DriverId y VehicleId ambos NULL-ables (columna puede tener uno, otro, o ambos; el SQL no tiene CHECK que exija al menos uno no nulo). AssignmentTypeLookupId Entity='AssignmentType' (seed: PERMANENT, TEMPORARY). StartDate NOT NULL, EndDate NULL (abierta). Sin UNIQUE/índice filtrado que impida solapamiento de asignaciones vigentes para el mismo Driver o Vehicle (a diferencia de las tarifas de chofer que sí usan UQ_..._Open); es un vacío frente al patrón usado en otras tablas efectivo-fechadas del mismo archivo.
- **tabla**: TransportOrder; **existeEnSql**: True; **notas**: Línea 1262 (Capa 11, no Capa 12, pero referenciada por TripOrder/DriverTrip). StatusCodeId Entity='OrderStatus' con valores PLANNED/IN_TRANSIT/ARRIVED/DELIVERED relevantes a Trips. UQ_TransportOrder_IdTenant (TransportOrderId, TenantId) es el destino de FK_DriverTrip_Order (comentario explícito Lote 4).
- **tabla**: OrderStop; **existeEnSql**: True; **notas**: Línea 1312. StopTypeLookupId Entity='StopType' (PICKUP/DELIVERY). GeocodeAccuracyLookupId Entity='GeocodeAccuracy' (EXACT/ZIP_CENTROID/CITY_CENTROID/MANUAL), NULL hasta geocodificar (comentario explícito). StatusCodeId Entity='StopStatus'. Es la parada de la orden; RouteStop referencia OrderStopId, es decir la ruta secuencia OrderStops existentes, no crea paradas propias.
- **tabla**: Vehicle; **existeEnSql**: True; **notas**: Línea 1092 (Capa 10, fuera de la Capa 12 pero referenciada por Trip/FleetAssignment). StatusCodeId Entity='VehicleStatus' NOT NULL (comentario 'nace en la etapa inicial'). UQ_Vehicle_Code (TenantId, Code), UQ_Vehicle_IdTenant para FKs compuestas.
- **tabla**: Driver; **existeEnSql**: True; **notas**: Línea 1119. StatusCodeId Entity='DriverStatus' NOT NULL. UX_Driver_User índice único filtrado (TenantId, UserId) WHERE UserId IS NOT NULL: un usuario INTERNAL se vincula a un solo chofer por compañía. MaxStopsPerRoute NULL = usa Tenant.MaxStopsPerRouteDefault (regla de default a nivel tenant no visible en esta tabla, requiere columna en Tenant no incluida en el recorte leído).
- **tabla**: DriverLocationPing; **existeEnSql**: True; **notas**: Línea 1883 (Capa 16, app móvil). FK TripId->Trip NULL-able (un ping puede no estar asociado a un trip activo). GeoPoint GEOGRAPHY NOT NULL. CapturedAtUtc (hora del dispositivo) vs ReceivedAtUtc (hora de recepción del servidor, default SYSUTCDATETIME) son campos distintos. Índice IX_LocationPing_Trip (TripId, CapturedAtUtc) para reconstrucción de trayectoria por trip.
- **tabla**: DriverTrip; **existeEnSql**: True; **notas**: Línea 1611. Comentario explícito: 'viaje pagado al chofer (monto congelado; insumo de la liquidación del Lote 9). Es la fila de pago, DISTINTA de dbo.Trip (Despacho)'. Es decir: DriverTrip no es lo mismo que Trip pese al nombre similar — DriverTrip es un cargo de servicio especial a liquidar, Trip es el despacho/ruta operativa. UX_DriverTrip_Order único filtrado (TransportOrderId) WHERE IsActive=1: un solo DriverTrip vigente por orden. CK_DriverTrip_Rate liga RateMissing=1 a Amount=0 y DriverTripRateId NULL, o RateMissing=0 con tarifa obligatoria.

### reglas

- **id**: R1; **regla**: Trip es la capa de consolidación (dueño de vehículo/chofer/órdenes); Route es el plan secuenciado, re-optimizable y versionado.; **fuente**: L260; **esRequisitoReal**: True
- **id**: R2; **regla**: Planificación diaria: agrupar órdenes confirmadas por fecha/zona en trips, asignando vehículo y chofer.; **fuente**: L262; **esRequisitoReal**: True
- **id**: R3; **regla**: Consolidación de varias órdenes de distintos clientes en un mismo viaje físico vía TripOrder.; **fuente**: L263; **esRequisitoReal**: True
- **id**: R4; **regla**: Optimización vía IRouteOptimizer (VROOM+OSRM self-hosted u OR-Tools, sin APIs pagas) respetando capacidad del vehículo, ventanas de tiempo y tiempo de servicio; cada corrida genera una Route nueva versionada y archiva la anterior, con registro en OptimizationRun.; **fuente**: L264; **esRequisitoReal**: True
- **id**: R5; **regla**: Para este lote la implementación de IRouteOptimizer debe ser determinista (orden por zona y ventana) y sin llamadas externas — VROOM/OSRM/OR-Tools quedan como costura para después.; **fuente**: Instrucción de la tarea (harness), sección 'Principios' — no es texto literal del documento maestro sino un límite de alcance impuesto para este lote; no contradice L264, solo acota qué se construye ahora.; **esRequisitoReal**: True
- **id**: R6; **regla**: Edición manual: reordenar paradas (drag-and-drop, del front) y recalcular ETAs sin re-optimizar todo.; **fuente**: L265; **esRequisitoReal**: True
- **id**: R7; **regla**: ETA se recalcula al reordenar paradas.; **fuente**: Tarea (harness), consistente con L265 ('recalcular ETAs'); **esRequisitoReal**: True
- **id**: R8; **regla**: Paradas no asignadas (por exceder capacidad) deben quedar visibles para reasignar a otro trip.; **fuente**: L266; **esRequisitoReal**: True
- **id**: R9; **regla**: Despacho: al pasar el trip a despachado, la ruta se congela y se vuelve visible para el chofer en la app móvil; el estado subsiguiente lo mueven eventos (salida, llegada, POD), no el planificador.; **fuente**: L267; **esRequisitoReal**: True
- **id**: R10; **regla**: El mapa (2D/Satélite, puntos de entrega, posición en vivo del chofer) es responsabilidad del front; no se dibuja una polyline sintética, solo la real que devuelve el motor de ruteo. El backend solo expone paradas con coordenadas y el último DriverLocationPing.; **fuente**: L268 + instrucción de la tarea ('mapa y posición en vivo son del front y de la app (Lote 7): el backend expone paradas con coordenadas y el último DriverLocationPing'); **esRequisitoReal**: True
- **id**: R11; **regla**: Cuando una dirección no se geocodifica exacta, la parada se plotea al centroide del código postal como respaldo; OrderStop.GeocodeAccuracyLookupId marca el caso (EXACT/ZIP_CENTROID/CITY_CENTROID/MANUAL).; **fuente**: L268; **esRequisitoReal**: True
- **id**: R12; **regla**: DispatchZone/DispatchZoneMember (por código postal o municipio) es el territorio fijo que cubre un chofer; DriverZone guarda esa asignación estándar. Sirve para reasignar en bloque ('todo lo de R-01 pasa a Ana') sin tocar trip por trip, y para filtrar en Despacho/Escaneo. Es independiente de RateZone.; **fuente**: L269; **esRequisitoReal**: True
- **id**: R13; **regla**: Alerta de máximo de paradas por ruta: Tenant.MaxStopsPerRouteDefault es el default; Driver.MaxStopsPerRoute permite override por chofer (NULL = usa el del tenant). Si una ruta sobrepasa el límite efectivo del chofer asignado, el dashboard de despacho lo marca con una alerta — NO bloquea, solo avisa.; **fuente**: L270; **esRequisitoReal**: True
- **id**: R14; **regla**: 'Despachar' es una acción explícita y reversible de selección: el botón abre un selector con SOLO las rutas que aún no se han despachado (RouteStatus distingue planificada/despachada/completada), con opción de marcar todas; las ya salidas no vuelven a aparecer ahí.; **fuente**: L271; **esRequisitoReal**: True
- **id**: R15; **regla**: Editar o eliminar una ruta libera sus órdenes: quitar una orden de una ruta, o eliminar la ruta completa, las devuelve a 'sin asignar' (se borra TripOrder/RouteStop, la orden NO se pierde, solo su asignación de ese día).; **fuente**: L272; **esRequisitoReal**: True
- **id**: R16; **regla**: El escaneo alimenta la ruta, no al revés: al escanear una orden en modo Outbound, el sistema resuelve su DispatchZone (por pueblo/ZIP contra DispatchZoneMember) y la asigna automáticamente (TripOrder) a la ruta abierta de ese día para esa zona, sin arrastre manual en Despacho. Si no hay ruta abierta para esa zona, la orden queda 'sin asignar'. Es una regla de backend obligatoria en el sistema real (el mock solo la simula de forma liviana).; **fuente**: L273 (texto completo omitido en grep pero leído íntegro en el Read de L258-357); **esRequisitoReal**: True
- **id**: R17; **regla**: La estación de escaneo confirma por voz (texto-a-voz): encontrado/ya escaneado/no encontrado en el idioma activo, con opción de silenciar.; **fuente**: L274; **esRequisitoRealNota**: es explícitamente responsabilidad del front (texto-a-voz en el navegador/dispositivo); el backend solo necesita devolver el resultado tipado del escaneo (encontrado/ya escaneado/no encontrado) para que el front elija la frase.
- **id**: R18; **regla**: Disponibilidad para despacho: el planificador de trips solo ofrece vehículos/choferes activos, con documentos vigentes y sin mantenimiento abierto que los inhabilite.; **fuente**: L286 (módulo 4, Flota) — aplica como precondición de este lote al construir el planificador de Trips; **esRequisitoReal**: True
- **id**: R19; **regla**: TransportOrder avanza DRAFT→CONFIRMED→PICKUP→INBOUND→PLANNED→IN_TRANSIT→ARRIVED→DELIVERED; los avances desde PLANNED en adelante (PLANNED, IN_TRANSIT, ARRIVED, DELIVERED) quedan reservados a Trips/POD (este lote y el 6/7).; **fuente**: Instrucción de la tarea ('Lo que dejaron preparado los lotes anteriores'), verificado contra CatalogDomains.cs OrderStatuses y seed L295-306; **esRequisitoReal**: True
- **id**: R20; **regla**: Cross-docking (DockAppointment/CrossDockAllocation) está apagado para el tenant Advance y queda diseñado pero no construido; el único acoplamiento con Trips es conceptual (agenda de citas de muelle) — no hay nada que Trips deba escribir ni leer de Cross-dock en este lote.; **fuente**: L311-321 (nota de alcance L313); **notaBitacora**: confirmar explícitamente que ningún endpoint de Trips depende de DockAppointment; si aparece dependencia, es error de alcance.
- **id**: R21; **regla**: Indicadores 'Órdenes sin chofer asignado' y 'Órdenes en excepción' se calculan filtrando la fuente de datos Órdenes por su campo de estatus/chofer (st/dr) del pipeline entrada→almacén→despacho→ruta→entregada/excepción, sin necesitar una fuente nueva — mecanismo del mock (IndicatorDefinition, módulo H).; **fuente**: L157; **esRequisitoReal**: True; **notaAlcance**: el requisito real para Lote 5 es que TransportOrderDataSource EXPONGA los campos necesarios (chofer asignado, marca de excepción); el propio mecanismo IndicatorDefinition es de otro módulo/lote.
- **id**: R22; **regla**: Nace DeliveryAttempt del evento físico (chofer marca entrega o fallo en la app / Sala de despacho).; **fuente**: L432; **esRequisitoRealNota**: DeliveryAttempt es entidad del módulo 11A (liquidación), fuera de este lote; se cita solo porque menciona 'Sala de despacho' como el lugar donde también se puede registrar el evento manualmente (backoffice) además de la app del chofer — confirma que Despacho necesita poder registrar entrega/fallo manualmente, no solo la app.
- **id**: R1; **regla**: Trip.Code es único por tenant (UQ_Trip_Code (TenantId, Code)); no se especifica si el código se libera al desactivar (a diferencia de Vehicle.Code y Driver.EmployeeCode, cuyos comentarios dicen explícitamente 'no se libera tras la baja'; Trip no tiene ese comentario ni índice filtrado por IsActive en la constraint única).; **fuente**: Diseño/logistica-db-estructura.sql:1380-1394; **esRequisitoReal**: True
- **id**: R2; **regla**: Una orden de transporte (TransportOrderId) no puede repetirse dentro del mismo Trip (UQ_TripOrder (TripId, TransportOrderId)), pero SÍ podría estar asignada simultáneamente a Trips distintos porque no existe un índice único filtrado por TransportOrderId solo (a diferencia de DriverTrip que sí tiene UX_DriverTrip_Order). Esto es una regla estructural del SQL, no una decisión de negocio documentada.; **fuente**: Diseño/logistica-db-estructura.sql:1398-1404
- **id**: R3; **regla**: Una misma OrderStop no se repite dentro de una Route (UQ_RouteStop (RouteId, OrderStopId)), pero el SQL no impide que la misma OrderStop aparezca en Routes distintas (p.ej. versiones distintas Route.Version del mismo Trip, o incluso Trips distintos).; **fuente**: Diseño/logistica-db-estructura.sql:1418-1427; **esRequisitoReal**: True
- **id**: R4; **regla**: Route tiene versionado (Version INT default 1, IsActive BIT default 1) y un índice IX_Route_Trip (TripId) WHERE IsActive=1, pero este índice NO es UNIQUE: el esquema no impone en base de datos que solo exista una Route activa por Trip a la vez; si esa regla existe, la aplicación debe garantizarla en código.; **fuente**: Diseño/logistica-db-estructura.sql:1407-1416
- **id**: R5; **regla**: GeocodeAccuracyLookupId de OrderStop es NULL hasta que la parada se geocodifica (comentario explícito en la columna).; **fuente**: Diseño/logistica-db-estructura.sql:1323; **esRequisitoReal**: True
- **id**: R6; **regla**: DispatchZone es un territorio fijo independiente de RateZone (zonas de tarifa); sirve para reasignación en bloque de choferes y para filtrar en Despacho/Escaneo (funcionalidad de otros lotes, mencionada solo como propósito en el comentario de capa).; **fuente**: Diseño/logistica-db-estructura.sql:1432-1437; **esRequisitoReal**: True
- **id**: R7; **regla**: DriverZone es la 'asignación estándar' (por defecto) de un chofer a una o más zonas y explícitamente 'no impide reasignar ese día' — es decir, es un valor por defecto/sugerido, no una restricción operativa de a quién se le puede despachar.; **fuente**: Diseño/logistica-db-estructura.sql:1457; **esRequisitoReal**: True
- **id**: R8; **regla**: Un chofer (DriverZone PK (DriverId, DispatchZoneId)) no puede tener la misma zona duplicada, pero puede marcar varias zonas con IsPrimary=1 simultáneamente: el SQL no tiene índice único filtrado que garantice una sola zona primaria por chofer.; **fuente**: Diseño/logistica-db-estructura.sql:1458-1463
- **id**: R9; **regla**: FleetAssignment no exige (por CHECK) que al menos uno de DriverId/VehicleId sea no nulo, ni impide asignaciones de fechas solapadas para el mismo Driver o Vehicle (a diferencia del patrón 'UQ_..._Open' aplicado a las tablas de tarifa de chofer en el mismo archivo). Es una laguna del esquema, no una decisión documentada de que se permita el solapamiento.; **fuente**: Diseño/logistica-db-estructura.sql:1598-1607
- **id**: R10; **regla**: DriverTrip es explícitamente distinto de Trip: 'Es la fila de pago, distinta de dbo.Trip (Despacho)'. Un solo DriverTrip vigente por orden se garantiza en BD vía UX_DriverTrip_Order (TransportOrderId) WHERE IsActive=1, y CANCELLED implica IsActive=0.; **fuente**: Diseño/logistica-db-estructura.sql:1609-1637; **esRequisitoReal**: True
- **id**: R11; **regla**: El monto de DriverTrip queda congelado al crear (comentario 'Amount ... congelado al crear'); si no hay tarifa vigente, RateMissing=1 obliga Amount=0 y DriverTripRateId NULL (CK_DriverTrip_Rate).; **fuente**: Diseño/logistica-db-estructura.sql:1609,1620,1635; **esRequisitoReal**: True
- **id**: R12; **regla**: Catálogo de estatus TripStatus (seed): DRAFT(inicial) -> PLANNED -> DISPATCHED -> IN_PROGRESS -> COMPLETED(terminal) / CANCELLED(terminal). RouteStatus: DRAFT(inicial) -> OPTIMIZED -> ACTIVE -> ARCHIVED(terminal). RouteStopStatus: PENDING(inicial) -> ON_THE_WAY -> ARRIVED -> COMPLETED(terminal) / FAILED(terminal, marcado @LAT es decir lateral). StopStatus: PENDING(inicial) -> EN_ROUTE -> COMPLETED(terminal) / FAILED(terminal lateral).; **fuente**: Diseño/logistica-db-seed.sql:307-325; **esRequisitoReal**: True
- **id**: R13; **regla**: OptimizationRun referencia opcionalmente Trip y/o Route (TripId, RouteId ambos NULL-ables) y guarda RequestJson/ResponseJson/ErrorMessage de la corrida de optimización, con motor EngineLookupId Entity='OptimizerEngine' (seed: VROOM, ORTOOLS, MANUAL) y estatus OptimizationRunStatus (PENDING/OK/ERROR).; **fuente**: Diseño/logistica-db-estructura.sql:1466-1477 y Diseño/logistica-db-seed.sql:189,330; **esRequisitoReal**: True
- **id**: R14; **regla**: DriverLocationPing distingue CapturedAtUtc (momento real del GPS en el dispositivo) de ReceivedAtUtc (momento en que el servidor lo recibió, default SYSUTCDATETIME); el índice IX_LocationPing_Trip está ordenado por CapturedAtUtc, sugiriendo que la reconstrucción de trayectoria usa la hora de captura, no la de recepción.; **fuente**: Diseño/logistica-db-estructura.sql:1883-1892; **esRequisitoReal**: True
- **id**: C1; **regla**: Verificado línea por línea contra Diseño/logistica-funcionalidades-maestro.md (líneas 258-274): las citas L260 (Trip=consolidación, Route=plan), L262 (planificación diaria por zona/fecha), L263 (TripOrder=consolidación multi-cliente), L264 (IRouteOptimizer VROOM/OSRM/OR-Tools sin APIs pagas, Route versionada + OptimizationRun), L265 (edición manual drag-and-drop + recalcular ETAs), L266 (paradas no asignadas visibles), L267 (despacho congela ruta, visible en app, estado lo mueven eventos), L268 (mapa real 2D/Satélite, sin polyline sintética, centroide ZIP de respaldo + GeocodeAccuracyLookupId), L269 (DispatchZone/DispatchZoneMember/DriverZone, reasignación en bloque, independiente de RateZone), L270 (alerta de máximo de paradas, no bloquea), L271 (Despachar=selector de rutas no despachadas), L272 (editar/eliminar ruta libera órdenes), L273 (escaneo Outbound resuelve DispatchZone y asigna automáticamente a la ruta abierta de esa zona; si no hay ruta abierta queda sin asignar; regla de backend real, el mock la simula liviana), L274 (confirmación por voz, silenciable) están TODAS citadas correctamente en la entrada revisada y coinciden textualmente con el documento. No se encontró contradicción entre estas líneas y ninguna bitácora posterior — no hay bitácora que las reabra.; **fuente**: Diseño/logistica-funcionalidades-maestro.md:258-274; **esRequisitoReal**: True
- **id**: C2; **regla**: CORRECCIÓN al vacío sobre Tenant.MaxStopsPerRouteDefault: la entrada revisada listaba como vacío pendiente de verificar la existencia de esa columna en Tenant. Se confirmó que SÍ existe (Diseño/logistica-db-estructura.sql:155, DEFAULT 25) y que Driver.MaxStopsPerRoute (línea 1129) la referencia explícitamente en su comentario. No es un vacío real; la regla R13/R20 sobre la alerta de máximo de paradas queda completamente respaldada por el esquema, sin nada pendiente de verificar en este punto.; **fuente**: Diseño/logistica-db-estructura.sql:155,1129; **esRequisitoReal**: True
- **id**: C3; **regla**: Confirmado: no existe permiso de 'escaneo'/'scan' en PermissionCatalog.cs (grep sin resultados para 'scan|Scan|escaneo'). El vacío V7 de la entrada (falta permiso para Estación de escaneo modo Outbound) es correcto y sigue abierto: ni siquiera hay un placeholder o categoría reservada como sí la hay para TripsPlan/TripsDispatch/TripsOptimize (líneas 22-24, categoría 'TRIPS').; **fuente**: src/Teikem.Domain/Constants/PermissionCatalog.cs (grep sin resultados); **esRequisitoReal**: True
- **id**: C4; **regla**: Confirmado: LTL_GROUND es el único módulo núcleo (IsCore=1) sembrado junto a SYSTEM (Diseño/logistica-db-seed.sql:37,19); no existe ModuleKeys.Trips ni módulo 'Despacho' propio. El vacío V4 de la entrada (falta decidir si Trips/Route llevan [RequireModule(ModuleKeys.LtlGround)] o si es núcleo sin gating) es correcto y sigue sin resolver en el documento ni en el código.; **fuente**: Diseño/logistica-db-seed.sql:19,37; src/Teikem.Domain/Constants/CatalogDomains.cs:412 (ModuleKeys); **esRequisitoReal**: True
- **id**: C5; **regla**: No se encontró ninguna entrada de bitácora (búsqueda de 'Sala de despacho', 'Monitoreo de rutas', 'reasignación en bloque', 'RouteStatus', 'Despachar' en todo el documento) posterior a la línea 271 que reabra o aclare el mapeo DRAFT/OPTIMIZED/ACTIVE/ARCHIVED (seed) vs. 'planificada/despachada/completada' (texto de L271). El vacío V1 de la entrada (falta decisión de mapeo) es correcto y sigue abierto — no hay bitácora que lo resuelva, contrario a lo que podría sugerir la instrucción de la tarea de que 'la bitácora contiene decisiones posteriores'.; **fuente**: Diseño/logistica-funcionalidades-maestro.md (búsqueda completa, sin resultados adicionales tras L271)

### endpoints

- **metodo**: POST; **ruta**: /api/v1/trips; **modulo**: LTL_GROUND (a confirmar, ver vacíos); **permiso**: trips.plan
- **metodo**: GET; **ruta**: /api/v1/trips; **modulo**: LTL_GROUND; **permiso**: trips.plan o fleet.view (a decidir)
- **metodo**: GET; **ruta**: /api/v1/trips/{publicId}; **modulo**: LTL_GROUND; **permiso**: trips.plan
- **metodo**: POST; **ruta**: /api/v1/trips/{id}/orders; **modulo**: LTL_GROUND; **permiso**: trips.plan
- **metodo**: DELETE; **ruta**: /api/v1/trips/{id}/orders/{tripOrderId}; **modulo**: LTL_GROUND; **permiso**: trips.plan
- **metodo**: POST; **ruta**: /api/v1/trips/{id}/optimize; **modulo**: LTL_GROUND; **permiso**: trips.optimize
- **metodo**: POST; **ruta**: /api/v1/routes/{routeId}/stops/reorder; **modulo**: LTL_GROUND; **permiso**: trips.optimize (o trips.plan, a decidir)
- **metodo**: POST; **ruta**: /api/v1/routes/dispatch; **modulo**: LTL_GROUND; **permiso**: trips.dispatch
- **metodo**: GET; **ruta**: /api/v1/routes?status=undispatched; **modulo**: LTL_GROUND; **permiso**: trips.dispatch
- **metodo**: DELETE; **ruta**: /api/v1/routes/{id}; **modulo**: LTL_GROUND; **permiso**: trips.plan
- **metodo**: GET; **ruta**: /api/v1/routes/{id}/stops; **modulo**: LTL_GROUND; **permiso**: trips.plan
- **metodo**: GET; **ruta**: /api/v1/dispatch-zones/resolve?zip=...&city=...; **modulo**: CATALOG o LTL_GROUND; **permiso**: fleet.view (o interno, sin permiso, si lo llama solo el propio backend de escaneo)
- **metodo**: POST; **ruta**: /api/v1/dispatch-zones/bulk-reassign; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: GET; **ruta**: /api/v1/trips/monitor; **modulo**: LTL_GROUND; **permiso**: trips.plan (Monitoreo de rutas)
- **metodo**: GET; **ruta**: /api/v1/trips/{id}/last-location; **modulo**: LTL_GROUND; **permiso**: trips.plan
- **metodo**: POST; **ruta**: /api/v1/scan/outbound; **modulo**: LTL_GROUND; **permiso**: a definir (Estación de escaneo no aparece en PermissionCatalog.cs listado; buscar si ya existe permiso de escaneo en otro lote)
- **metodo**: GET; **ruta**: /api/v1/drivers/{publicId:guid}/trips; **permiso**: driverpay.view; **modulo**: CATALOG
- **metodo**: POST; **ruta**: /api/v1/drivers/{publicId:guid}/trips; **permiso**: driverpay.manage; **modulo**: CATALOG
- **metodo**: POST; **ruta**: /api/v1/drivers/{publicId:guid}/trips/{tripPublicId:guid}/cancel; **permiso**: driverpay.manage; **modulo**: CATALOG
- **metodo**: GET; **ruta**: /api/v1/dispatch-zones; **permiso**: fleet.view; **modulo**: CATALOG
- **metodo**: POST; **ruta**: /api/v1/dispatch-zones; **permiso**: fleet.manage; **modulo**: CATALOG
- **metodo**: PATCH; **ruta**: /api/v1/dispatch-zones/{id:int}; **permiso**: fleet.manage; **modulo**: CATALOG
- **metodo**: POST; **ruta**: /api/v1/dispatch-zones/{id:int}/deactivate; **permiso**: fleet.manage; **modulo**: CATALOG
- **metodo**: POST; **ruta**: /api/v1/dispatch-zones/{id:int}/reactivate; **permiso**: fleet.manage; **modulo**: CATALOG
- **metodo**: GET; **ruta**: /api/v1/fleet/availability; **permiso**: fleet.view; **modulo**: CATALOG
- **metodo**: GET; **ruta**: /api/v1/fleet/expiring-documents; **permiso**: fleet.view; **modulo**: CATALOG
- **metodo**: GET; **ruta**: /api/v1/orders/{publicId:guid}/quote; **permiso**: orders.view; **modulo**: LTL_GROUND
- **metodo**: POST; **ruta**: /api/v1/orders/{publicId:guid}/confirm; **permiso**: orders.edit u orders.credit_override; **modulo**: LTL_GROUND
- **metodo**: POST; **ruta**: /api/v1/orders/{publicId:guid}/status; **permiso**: orders.edit; **modulo**: LTL_GROUND
- **metodo**: GET; **ruta**: /api/v1/status/history/{entity}/{id}; **permiso**: según OwnerReadPermission de la entidad (StatusService.GetHistoryAsync); **modulo**: n/a (transversal)

### estatus

- **dominio**: TripStatus; **efectos**: ["Seed ya define DRAFT(inicial)→PLANNED→DISPATCHED→IN_PROGRESS→COMPLETED, más CANCELLED (terminal). El texto del doc (L267,L271) habla en términos de Route ('planificada/despachada/completada'), no de Trip — aclarar en el lote si el 'Despachar' opera sobre Trip.StatusCodeId, sobre Route.StatusCodeId, o sobre ambos en cascada."]
- **dominio**: RouteStatus; **efectos**: ["Seed define DRAFT(inicial)→OPTIMIZED→ACTIVE→ARCHIVED(terminal), NO 'planificada/despachada/completada' textual del doc L271 — requiere decisión de mapeo: ¿ACTIVE = despachada? ¿el selector de 'Despachar' filtra RouteStatus != ACTIVE/ARCHIVED? Ver vacío V1."]
- **dominio**: RouteStopStatus; **efectos**: ['PENDING(inicial)→ON_THE_WAY→ARRIVED→COMPLETED(terminal), lateral FAILED. Lo mueven eventos del chofer (módulo 8), no el planificador; Trips solo lee/crea en PENDING al generar la ruta.']
- **dominio**: OptimizationRunStatus; **efectos**: ['PENDING(inicial)→OK/ERROR(terminales). Cada corrida de IRouteOptimizer (aunque sea determinista) debe dejar una fila aquí (L264).']
- **dominio**: OrderStatus (TransportOrder); **efectos**: ["PLANNED se alcanza cuando la orden entra a un TripOrder de una Route activa; IN_TRANSIT cuando el Trip se despacha (StatusCodeId TripStatus=DISPATCHED/IN_PROGRESS, vía eventos del chofer L267); ARRIVED/DELIVERED quedan reservados a POD (módulo 9, fuera de este lote salvo el efecto de estatus). Editar/eliminar una ruta (R15) debe REGRESAR la orden de PLANNED a su estatus anterior (probablemente CONFIRMED/INBOUND) — la regla exacta de 'a qué estatus vuelve' no está escrita en el documento, es un vacío (ver V2)."]
- **dominio**: TripStatus; **efectos**: ['DRAFT (inicial, seed IsInitial=1)', 'PLANNED', 'DISPATCHED', 'IN_PROGRESS', 'COMPLETED (terminal)', 'CANCELLED (terminal)']
- **dominio**: RouteStatus; **efectos**: ['DRAFT (inicial)', 'OPTIMIZED', 'ACTIVE', 'ARCHIVED (terminal)']
- **dominio**: RouteStopStatus; **efectos**: ['PENDING (inicial)', 'ON_THE_WAY', 'ARRIVED', 'COMPLETED (terminal)', 'FAILED (terminal, entrada lateral @LAT)']
- **dominio**: StopStatus; **efectos**: ['PENDING (inicial)', 'EN_ROUTE', 'COMPLETED (terminal)', 'FAILED (terminal, entrada lateral @LAT)']
- **dominio**: OptimizationRunStatus; **efectos**: ['PENDING (inicial)', 'OK (terminal)', 'ERROR (terminal)']
- **dominio**: DriverTripStatus; **efectos**: ["OPEN (inicial, 'Por liquidar')", 'SETTLED (terminal)', 'CANCELLED (terminal)']
- **dominio**: VehicleStatus; **efectos**: ['ACTIVE (inicial)', 'MAINTENANCE (entrada lateral)', 'INACTIVE (terminal)']
- **dominio**: DriverStatus; **efectos**: ['ACTIVE (inicial)', 'UNAVAILABLE (entrada lateral)', 'INACTIVE (terminal)']

### vacios

- V1 — RouteStatus: el documento (L271) describe el dominio en términos de 'planificada/despachada/completada' pero el seed real (logistica-db-seed.sql L324) define DRAFT/OPTIMIZED/ACTIVE/ARCHIVED. No hay una decisión explícita de cuál código representa 'despachada' para que el selector de 'Despachar' (R14) sepa qué filtrar. Hay que decidirlo en este lote (o documentarlo si ya se decidió en una bitácora que no cita línea).
- V2 — No se especifica a qué OrderStatus regresa una orden cuando se libera de una ruta editada/eliminada (R15): ¿vuelve a CONFIRMED, a INBOUND, o a un estatus intermedio nuevo tipo 'sin asignar' explícito? El documento solo dice que 'vuelve a sin asignar' a nivel de TripOrder/RouteStop, no de OrderStatus.
- V3 — 'FleetAvailability' citada en el enunciado de la tarea como algo que dejó preparado el Lote 4 no es una tabla ni una interfaz con ese nombre exacto en el código: existe FleetAvailabilityService (clase de servicio, sin persistencia) y la tabla FleetAssignment (que es otra cosa: propiedad chofer↔vehículo por fecha, no disponibilidad para despachar hoy). Falta aclarar si el planificador de Trips debe usar exclusivamente FleetAvailabilityService o si además necesita bloquear un chofer/vehículo ya asignado a OTRO trip abierto el mismo día (ese cruce no lo resuelve hoy ningún servicio).
- V4 — No existe ModuleKeys.Trips ni ModuleKeys.Dispatch: el módulo de Trips y rutas no tiene una clave de módulo propia en CatalogDomains.ModuleKeys (solo existe LtlGround, que no aparece nombrado en el documento maestro ni se explica qué agrupa). Falta decidir si los endpoints de este lote llevan [RequireModule(ModuleKeys.LtlGround)] o si Trips es un núcleo sin gating por módulo.
- V5 — 'Órdenes en excepción' no corresponde a ningún único InternalCode de OrderStatus: el seed tiene ON_HOLD, PARTIAL, FAILED y CANCELLED como estatus laterales/terminales fuera del pipeline feliz — el documento (L157) no dice cuáles de estos cuentan como 'excepción' para el indicador ni para el dashboard de despacho. Ambigüedad de término sin resolver.
- V6 — El motor de resolución DispatchZone por ZIP/municipio (DispatchZoneMember.MatchTypeLookupId/MatchValue) no tiene ninguna implementación en src/: falta decidir el algoritmo de match (exacto por ZIP, prefijo, lista de municipios) y qué pasa si un ZIP cae en dos zonas (el esquema no impide solapamiento).
- V7 — No hay endpoint ni permiso identificado en PermissionCatalog.cs para 'Estación de escaneo' (modo Outbound) fuera de este lote; falta confirmar si ese permiso ya se sembró en otro lote (Almacén) o si Trips debe crearlo/reusarlo — no se encontró 'scan' ni 'escaneo' en PermissionCatalog.cs.
- V8 — El documento pide ETA recalculada al reordenar (Principios, según la tarea) pero no especifica la fórmula (¿usa distancia/duración ya guardadas en RouteStop.DistanceFromPrevKm/DurationFromPrevMin recalculadas con el optimizador determinista, o una heurística simple tipo velocidad promedio?) — sin decisión de diseño en el documento.
- V9 — IRouteOptimizer no existe en el código (ni la interfaz ni ninguna implementación) — es enteramente nuevo; el documento no detalla la firma del contrato (qué recibe: paradas+vehículo+ventanas; qué devuelve: orden+distancias+no asignadas) más allá de mencionar VROOM/OSRM/OR-Tools como 'costura'.
- V10 — DockAppointment (mencionado en L317 y en la nota de la tarea) no se verificó si ya existe como tabla en el SQL (deliberadamente fuera de alcance de esta lectura, según instrucción de la tarea) — si Lote 6 la necesita y no existe, es un vacío de ESE lote, no de este; se deja anotado para que no se pierda.
- El pedido de la tarea menciona explícitamente 'Route' y 'RouteStop' como parte del alcance, pero no pidió leer la sección narrativa del documento maestro (se excluyó a propósito por instrucción de la tarea: 'leyendo SOLO el SQL'); por lo tanto cualquier regla de negocio sobre planificación/despacho/monitoreo que NO esté codificada como constraint, comentario de columna o valor de catálogo en el SQL queda fuera de este extracto y debe buscarse en logistica-funcionalidades-maestro.md en una pasada separada.
- Tenant.MaxStopsPerRouteDefault: Driver.MaxStopsPerRoute comenta 'override del default del tenant; NULL = usa Tenant.MaxStopsPerRouteDefault', pero esa columna no aparece en el fragmento de Tenant revisado en este recorte (no se confirmó su existencia real en la tabla Tenant); queda como vacío a verificar.
- No existe una tabla o columna que registre explícitamente el 'motivo de reasignación en bloque' de DispatchZone a un chofer (la funcionalidad de reasignar 'todo lo de R-01 a Ana' se menciona solo en el comentario de capa, sin tabla de auditoría de reasignaciones de zona más allá del historial genérico EntityStatusHistory/AuditLog del proyecto).
- FleetAssignment no tiene columna de motivo/nota ni referencia a quién autorizó el cambio, ni CHECK que impida asignaciones simultáneas superpuestas del mismo vehículo a dos choferes (o viceversa); si el negocio requiere unicidad de asignación vigente, el SQL no la garantiza (ver regla R9).
- Route.TotalDistanceKm, TotalDurationMin y StopCount no tienen columna computada (AS ...) ni trigger visible que los derive de RouteStop: el SQL no aclara si son escritos por el motor de optimización, por un job, o por la aplicación al confirmar la ruta.
- DispatchZoneMember.MatchValue no tiene FK ni tabla de catálogo geográfico (código postal, municipio) que valide su formato o existencia; es NVARCHAR libre, y el SQL no impone unicidad entre zonas del mismo tenant, por lo que dos DispatchZone del mismo tenant podrían compartir el mismo MatchValue sin que la base de datos lo impida (posible ambigüedad de zona no resuelta en el esquema).
- No se encontró en el recorte de tabla ninguna columna en Trip que registre el chofer/vehículo 'planeado' vs 'real' por separado (Trip.DriverId/VehicleId son una sola asignación); si el negocio requiere reasignar chofer/vehículo tras el despacho conservando el historial de asignación previa, el SQL no tiene una tabla de historial de asignación de Trip (solo EntityStatusHistory genérico para StatusCodeId, no para VehicleId/DriverId).
- No existe ModuleKeys.Trips: el permiso 'TRIPS' visto en PermissionCatalog (trips.plan/trips.dispatch/trips.optimize, líneas 22-24 y 85-87 de PermissionCatalog.cs) es solo un PermissionCategory de agrupación visual, no un módulo activable por tenant (TenantModule). El seed (logistica-db-seed.sql líneas 19-46) no registra un módulo 'TRIPS'; LTL_GROUND es el módulo núcleo (IsCore=1) bajo el que ya operan OrdersController/OrderStatusController. El Lote 5 debe decidir si Trip/Route van bajo [RequireModule(ModuleKeys.LtlGround)] (patrón seguido por Orders) o si falta dar de alta un módulo propio; no hay decisión documentada al respecto.
- EntityTypes.Trip ("TRIP", CatalogDomains.cs:305) existe, pero no tiene entrada en PermissionCatalog.OwnerReadPermission ni OwnerWritePermission (líneas 142-190). Si Trip necesita ContactPointService, CustomFieldService o StatusService.GetHistoryAsync, falta agregar esos mapeos (p.ej. hacia trips.plan/trips.dispatch) — vacío a resolver en el diseño del lote.
- El SQL (Diseño/logistica-db-estructura.sql, CAPA 12, líneas ~1378-1425) ya define Trip, TripOrder, Route, RouteStop con StatusCodeId propios para tres dominios de estatus distintos: TripStatus, RouteStatus y RouteStopStatus. No se encontró seed de StatusCode para esos tres dominios en logistica-db-seed.sql en la revisión rápida realizada aquí (no se confirmó su existencia); si faltan, es un vacío que bloquea usar StatusService.TransitionAsync/GetInitialAsync tal cual.
- No hay ningún IStatusTransitionEffect ni IOwnedEntityResolver registrado para TRIP/ROUTE en el código existente; si el lote requiere efectos de transición (p.ej. al pasar Trip a IN_PROGRESS/COMPLETED) o asociar ContactPoint/CustomFieldValue a Trip, deben crearse — no hay nada que 'reutilizar tal cual' salvo la interfaz.
- No se encontró ningún IDataSource existente para TRIP o ROUTE (los existentes: ClientDataSources, OrderDataSources, FleetDataSources, FuelLogDataSource, FleetDocumentDataSource) — el patrón (TransportOrderDataSource en OrderDataSources.cs) es reutilizable como plantilla, pero la fuente de datos de Trips/Rutas está vacía y debe crearse en el lote.
- V1 sigue vigente (RouteStatus DRAFT/OPTIMIZED/ACTIVE/ARCHIVED vs. texto L271 'planificada/despachada/completada'): confirmado que no hay bitácora posterior que resuelva el mapeo; el lote debe decidirlo.
- V4, V6, V7, V9 de la entrada original se confirman vigentes tras revisión directa del código y el documento (no hay ModuleKeys propio de Trips, no hay motor de resolución de DispatchZoneMember, no hay permiso de escaneo en PermissionCatalog, no existe IRouteOptimizer en src/).
- CORRECCIÓN — eliminar el vacío que decía 'Tenant.MaxStopsPerRouteDefault no se confirmó su existencia real en la tabla Tenant': SÍ existe (Diseño/logistica-db-estructura.sql:155), con comentario que la liga explícitamente a Driver.MaxStopsPerRoute (línea 1129). No es un vacío real, es un dato ya completo en el esquema.
- No se encontró en el documento maestro (búsqueda de 'Sala de despacho' y 'Monitoreo de rutas' como encabezados o bitácoras dedicadas) ninguna sección o entrada de bitácora específica para esas dos pantallas; solo aparecen mencionadas de paso en la regla de búsqueda de texto libre (línea 651) y en DeliveryAttempt (línea 432, 'Sala de despacho' como lugar donde también se puede registrar manualmente entrega/fallo). Esto confirma la nota de la entrada original de que Sala de despacho/Monitoreo de rutas no tienen texto normativo propio en el módulo 3 más allá de lo ya extraído en L267-273 — no hay contenido adicional que agregar ni contradicción que señalar, pero tampoco hay una sección dedicada con más detalle (p.ej. qué columnas exactas muestra el monitoreo) que resolver los vacíos V8/V10 sobre fórmula de ETA o firma exacta de IRouteOptimizer: siguen sin decisión documentada.

### reutilizar

- StatusService.TransitionAsync / EnsureAllowedAsync (src/Teikem.Infrastructure/Services/StatusService.cs) para cualquier cambio de TripStatus/RouteStatus/RouteStopStatus/OrderStatus — no escribir el estatus a mano.
- FleetAvailabilityService (src/Teikem.Infrastructure/Services/FleetAvailabilityService.cs) para decidir qué choferes/vehículos ofrecer al planificar un Trip — ya resuelve documentos vigentes, OT abiertas y estatus terminal/activo (R18).
- DispatchZoneService + DispatchZonesController (Lote 4) para el CRUD de zonas; solo falta el motor de RESOLUCIÓN (ZIP/pueblo → DispatchZoneId) que el escaneo Outbound necesita (R16) — no existe todavía, hay que construirlo sobre DispatchZoneMember.
- DriverZone + FleetAvailabilityService.PrimaryZoneCodesAsync para la reasignación en bloque por zona (ya lee la zona primaria del chofer).
- TransportOrderDataSource (src/Teikem.Infrastructure/Analytics/OrderDataSources.cs) como base de la fuente 'Órdenes' — hay que EXTENDERLA (no reemplazarla) con el/los campos que necesitan los indicadores 'Órdenes sin chofer asignado' y 'Órdenes en excepción' (R21).
- ILookupCache para resolver OptimizerEngine, GeocodeAccuracy, ZoneMatchType (LookupDomains ya declarados en CatalogDomains.cs).
- Permisos ya reservados en PermissionCatalog.cs: TripsPlan='trips.plan', TripsDispatch='trips.dispatch', TripsOptimize='trips.optimize' — sembrados pero sin ningún endpoint que los use todavía; este lote es quien primero los consume.
- Patrón IStatusTransitionEffect para reglas laterales al despachar (ej. 'congelar' la ruta, notificar app móvil) en vez de lógica ad hoc en el controlador.
- IDataSource / IDataSourceRegistry (patrón ya usado por 10+ fuentes) para exponer Trip/Route como fuente de análisis si se decide dar indicadores propios de Trips (no exigido explícitamente por el documento para este lote, pero es el patrón establecido si se necesita).
- src/Teikem.Infrastructure/Persistence (patrón de Configurations 1:1 por entidad; usar como base para Trip, TripOrder, Route, RouteStop, DispatchZone, DispatchZoneMember, DriverZone, FleetAssignment, DriverLocationPing)
- ILookupCache para resolver LookupCode de StopType, GeocodeAccuracy, ZoneMatchType, AssignmentType, OptimizerEngine, VehicleType
- StatusService.TransitionAsync para TripStatus, RouteStatus, RouteStopStatus, StopStatus, OptimizationRunStatus, DriverTripStatus (todas requieren catálogo StatusCode con EntityStatusHistory)
- ITenantScoped / filtro global de TeikemDbContext para todas las entidades con TenantId (Trip, DispatchZone, FleetAssignment, DriverLocationPing, Vehicle, Driver, TransportOrder); TripOrder, Route, RouteStop, OrderStop, DispatchZoneMember, DriverZone no tienen TenantId propio (tenant se infiere vía FK padre) — revisar patrón existente de entidades sin TenantId directo para IOwnedEntityResolver si aplica
- PermissionCatalog y [RequireModule] existentes para módulos de logística/despacho (verificar si ya existe un ModuleKeys para Trips/Despacho en src/Teikem.Domain)
- StatusService (src/Teikem.Infrastructure/Services/StatusService.cs) — motor de transición de estatus por StageKind (GetInitialAsync, TransitionAsync, EnsureAllowedAsync, GetHistoryAsync). Aplicable directo a TripStatus, RouteStatus y RouteStopStatus (Entity=... en StatusCode) igual que se usó para OrderStatus, DriverStatus, VehicleStatus.
- IStatusTransitionEffect (interfaz en StatusService.cs) — patrón para enganchar efectos de negocio en una transición (p.ej. congelar ruta, notificar chofer) sin tocar StatusService.
- ContactPointService (src/Teikem.Infrastructure/Services/ContactPointService.cs) y IOwnedEntityResolver — reutilizables si Trip/Route necesitan contactos polimórficos (requiere registrar resolver + entrada en OwnerReadPermission/OwnerWritePermission).
- CustomFieldService (src/Teikem.Infrastructure/Services/CustomFieldService.cs) — EAV de campos personalizados por EntityType; reutilizable para TRIP si el módulo lo requiere.
- IDataSource / IDataSourceRegistry / DataSourceRegistry (src/Teikem.Infrastructure/Analytics/DataSources.cs) y el patrón de TransportOrderDataSource (src/Teikem.Infrastructure/Analytics/OrderDataSources.cs) — plantilla para una futura TripDataSource/RouteDataSource (AsNoTracking, tope 20000 filas, DateField, EntityTypeCode para custom fields).
- PermissionCatalog (src/Teikem.Domain/Constants/PermissionCatalog.cs) — ya trae TripsPlan='trips.plan', TripsDispatch='trips.dispatch', TripsOptimize='trips.optimize' listos para usar en [RequirePermission(...)]; también FleetView/FleetManage para consultar disponibilidad de choferes/vehículos.
- Patrón de controlador/DTO de Lote 4: DriverTripsController y DispatchZonesController (src/Teikem.Api/Controllers/) — ruta anidada por publicId, [Authorize] + [RequireModule] + [RequirePermission] por acción, DTOs record en Contracts, servicio inyectado por constructor primario, comentario XML resumen de reglas encima de la clase.
- FleetController.Availability (IFleetAvailabilityService.GetAsync) — ya calcula disponibilidad de chofer/vehículo por fecha con motivos bloqueantes (fleet.view); reutilizable para la asignación de Trip a chofer/vehículo sin reimplementar la lógica de bloqueo.
- LocationsController / LocationService — patrón de baja lógica (deactivate/reactivate, nunca DELETE) y de localizaciones/paradas ya asociadas a OrderStop, que Route/RouteStop consumen vía TransportOrder → OrderStop.
- Excepciones de dominio (NotFoundException, ValidationException, ConflictException, StatusRuleException) usadas en todos los servicios anteriores — mismo patrón para Trip/Route.
- ILookupCache y MultilingualText — resolución de catálogos y etiquetas multilingües, usados en StatusService/ContactPointService/CustomFieldService y reutilizables igual en los nuevos servicios de Trips.
