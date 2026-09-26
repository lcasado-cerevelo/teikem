# Lote 4 — Flota, choferes y mantenimiento: plan de implementación (aprobado por Luis el 2026-09-26)

Resultado del workflow `lote-diseno` (3 lectores, crítico de completitud, 3 arquitectos, 2 jueces y síntesis).
Puntajes de los jueces por diseño: mínimo viable 13.5, riesgo primero 14, extensibilidad 16.

## Ratificado por Luis (2026-09-26)

- Choferes y vehículos: 'Activo' reversible y 'Eliminar' definitivo sin borrado físico; códigos inmutables que no se reutilizan
- Entrega especial con chofer: se confirma, avanza a IN_TRANSIT y crea el DriverTrip con monto congelado; sin tarifa → $0 marcado RateMissing
- Disponibilidad para despacho: bloquean inactivo, estatus no inicial, licencia no vigente, documento de vehículo vencido y OT en proceso; avisan certificación vencida, documento por vencer (30 días) y vehículo sin documentos
- Intentos: niveles por compañía (tope 20), nivel intermedio sin tarifa paga $0 con nota, fallback al nivel más alto configurado
- OT en proceso pone el vehículo en MAINTENANCE y al cerrar/cancelar vuelve a ACTIVE; cerrar exige tareas completas y odómetro si el programa es por km; número OT-##### automático
- Umbrales: 30 días documentos, 10 % del intervalo preventivo; documento renovado supera al anterior automáticamente
- Permisos fleet.view, driverpay.view, driverpay.manage (52); plantilla Driver sin permisos de flota
- 'Área' = zona de despacho primaria con CRUD mínimo de zonas; fuera del lote almacén base, FleetAssignment y adjuntos

## Enfoque

EXTENSIBILIDAD sin sobre-diseño, sobre el plan ganador (índice 2) con los injertos de los jueces. El Lote 4 construye el módulo 4 (Flota, choferes y mantenimiento, con sus cinco paneles) y el maestro 'Choferes y tarifas' del grupo Catálogo. Deja tres costuras explícitas para que Órdenes/Despacho, Liquidación/Facturación y Portal/App no tengan que rehacer tablas ni servicios de este lote.

(1) ÓRDENES / DESPACHO. La entrega especial del Lote 3 se completa aquí. El chofer, su disponibilidad y su tarifa se preparan ANTES de sacar números (PrepareAsync, sin consumir NumberSequence) y el despacho se repite dentro de cada reintento por colisión de OrderService. La orden avanza hasta IN_TRANSIT etapa por etapa vía StatusService y se crea un DriverTrip con el monto congelado. La BD garantiza un solo viaje vigente por orden (UX_DriverTrip_Order filtrado por IsActive; CANCELLED implica IsActive=0). Además se verifica CATALOG encendido aunque el endpoint viva en LTL_GROUND. La disponibilidad (R7) vive en un único IFleetAvailabilityService con reglas puras, que el planificador de Despacho consumirá tal cual. Los documentos se leen con un helper compartido, FleetQueries.LoadFleetDocumentsAsync: parte siempre de Vehicles/Drivers filtrados por tenant y aplica la regla 'documento vigente por tipo', así que un documento vencido que ya se renovó no bloquea ni aparece por vencer.

(2) LIQUIDACIÓN (Lote 9). Las tarifas efectivo-fechadas (entrega, intento, viaje) siguen el patrón de RateComponent/SpecialService. Hay un resolvedor IDriverRateResolver y el motor puro de las tres fórmulas (DriverPayoutRules). La política vigente por tenant está en DriverPayPolicy. DriverTrip lleva monto congelado, RateMissing (CHECK de coherencia en BD) y estatus OPEN→SETTLED/CANCELLED.

(3) PORTAL (Lote 8) / APP (Lote 7). La orden expone solo la identidad del chofer asignado, nunca montos. Driver.UserId es único por compañía y exige admin.users más un usuario INTERNAL con membresía ACTIVE. DriverDevice tiene token único.

Defensa en profundidad reforzada:
- FKs compuestas (Id, TenantId) en las tablas nuevas, FuelLog y MaintenanceWorkOrder.
- Resolvers de pertenencia para TODOS los EntityTypes con permiso de dueño, incluido el hueco PORTAL_USER del Lote 2, porque CustomFieldService.cs:193 omite la verificación si no hay resolver.
- Pruebas de modelo (TenantIsolationModelTests) y de cobertura de resolvers.
- BOLA por id hijo en el smoke.

Integridad numérica:
- Validación previa de precisión DECIMAL: nunca un 500 por desbordamiento.
- Odómetro con bloqueo de fila (UPDLOCK, ROWLOCK, TenantId), máximo monotónico y piso para la corrección manual.
- Cerrar una OT exige tareas completas y odómetro si el programa es por kilometraje.

Se reutiliza el EntityType WORK_ORDER ya sembrado (seed:140) en lugar de crear MAINTENANCE_WORK_ORDER. Se reutilizan sin excepción los patrones de los Lotes 1-3: TenantId del principal, PublicId en rutas, StatusService.TransitionAsync con efectos (StatusService perezoso cuando el efecto transiciona), EnsureAllowedAsync(EDIT_WORK_ORDER), [RequirePermission] + [RequireModule], IOwnedEntityResolver + Owner*Permission, IDataSource, ILookupCache, NumberSequence, RunInTransactionAsync/SaveGuardedAsync/ApplyRowVersion y excepciones de dominio con mensajes exactos.

Separación flota vs. compensación (R8) por permisos: fleet.view (nuevo), fleet.manage, fleet.maintenance, driverpay.view y driverpay.manage (nuevos); 52 en total.

Organización:
- P0 es la ÚNICA pieza con archivos compartidos. Entrega todo el SQL/seed, entidades, configuraciones, constantes, permisos, DTOs con firma posicional completa (el contrato real entre piezas), costuras implementadas (FleetQueries), DI, contenido de análisis y esqueletos compilables.
- P1-P8 se implementan en paralelo con archivos disjuntos.
- P9 cierra con smoke, decisiones, manual y FAQ.

El plan sale de la especificación consolidada. P9 contrasta el capítulo del manual con Diseño/logistica-funcionalidades-maestro.md (módulos 4 y 11A y la bitácora) y anota las discrepancias en docs/lote4-decisiones.md.

## Resumen

El Lote 4 entrega los cinco paneles de Flota y mantenimiento:
- **Vehículos**, con buscador libre.
- **Documentos por vencer**, de vehículo y de chofer unidos, contando solo el documento vigente de cada tipo.
- **Mantenimiento preventivo**, con estatus Al día / Por vencer / Vencido / Sin historial.
- **Órdenes de trabajo**, con número automático OT-#####, tareas, costos sumados y efecto en el estatus del vehículo. Cerrar exige tareas completas y odómetro en programas por km.
- **Bitácora de combustible**, con km/L y costo/km, y el odómetro protegido por bloqueo de fila.

También entrega el maestro-detalle de Choferes y tarifas:
- Alta con código fijo, zona/área y tope de paradas.
- Licencias y certificaciones.
- Vínculo con un usuario interno, que exige admin.users.
- Inactivar o eliminar sin borrar historial.
- Tarifas efectivo-fechadas por entrega (servicio+paquete), por intento (niveles del tenant con fallback) y por viaje (tipo del catálogo de servicios especiales).
- Fórmula de pago configurable con vista previa.

Cierra el pendiente del Lote 3: la entrega especial con chofer se prepara antes de numerar, se confirma, llega a IN_TRANSIT y crea un DriverTrip con monto congelado. La BD garantiza un solo viaje vigente por orden.

En esquema:
- Vehicle y Driver reciben PublicId, auditoría, RowVersion y UQ (Id, TenantId).
- 5 tablas nuevas: DriverPayPolicy, DriverDeliveryRate, DriverAttemptRate, DriverTripRate y DriverTrip, con FKs compuestas por tenant.
- IsActive en FuelLog y MaintenanceTask; CHECKs e índices de vencimiento.
- Estatus DriverTripStatus, capacidad EDIT_WORK_ORDER sobre el EntityType WORK_ORDER ya sembrado y 3 permisos (52 en total).

Deja listas las costuras para:
- **Despacho:** disponibilidad y helper de documentos.
- **Liquidación:** resolvedor, motor de fórmulas y viajes congelados con estatus.
- **App/Portal:** usuario y dispositivo del chofer; chofer asignado visible sin montos.

Suma pruebas de modelo multi-tenant, de cobertura de resolvers y de contratos por reflexión, y un smoke con concurrencia (8 OT simultáneas) y BOLA por id hijo.

## Piezas

### P0 — Base compartida: SQL, seed, constantes, permisos, excepciones, entidades, configuraciones EF, DbContext, DTOs con firma posicional completa, costuras implementadas (FleetQueries), DI, contenido de análisis, esqueletos compilables y pruebas de modelo

- Toca archivos compartidos: sí. Depende de: nada.

Es la ÚNICA pieza con archivos compartidos. Se integra primero y debe dejar:
- la solución compilando;
- las 261 pruebas previas y las nuevas de P0 en verde;
- los smokes de los Lotes 1-3 en verde sobre una BD recreada.

Como el script de estructura se aplica una sola vez por hash, P0 entrega TODO el SQL del lote.

**(1) SQL y seed.** Aplicar completo 'cambiosSql', inline, con el comentario '-- Lote 4'. Respetar capas y orden de FKs: las UQ (Id, TenantId) de Vehicle, Driver, SpecialServiceType, TransportOrder y DriverTripRate se declaran en su CREATE TABLE antes de que otra tabla las referencie.

**(2) CatalogDomains.cs.**
- LookupDomains: VehicleType, Ownership, FuelType, VehicleDocType, MaintenanceTrigger, MaintenanceType, LicenseClass, CertificationType, DevicePlatform y DriverPayoutFormula.
- StatusDomains: VehicleStatus, DriverStatus, WorkOrderStatus y DriverTripStatus.
- Clases de códigos:
  - VehicleStatuses (ACTIVE, MAINTENANCE, INACTIVE)
  - DriverStatuses (ACTIVE, UNAVAILABLE, INACTIVE)
  - WorkOrderStatuses (OPEN, IN_PROGRESS, CLOSED, CANCELLED)
  - DriverTripStatuses (OPEN, SETTLED, CANCELLED)
  - MaintenanceTriggers (MILEAGE, TIME, BOTH)
  - MaintenanceTypes (PREVENTIVE, CORRECTIVE)
  - DriverPayoutFormulas (DELIVERY_PLUS_ATTEMPTS, DELIVERY_INCLUDES_FIRST, FAILED_REPLACES_DELIVERY)
  - FleetDocumentKinds (REGISTRATION, INSURANCE, INSPECTION, PERMIT, LICENSE, CERTIFICATION)
  - FleetOwnerKinds (VEHICLE, DRIVER)
  - ExpiryStates (EXPIRED, EXPIRING, OK, NO_EXPIRY)
  - MaintenanceDueStates (OK, DUE_SOON, OVERDUE, NO_BASELINE)
- Capabilities.EditWorkOrder='EDIT_WORK_ORDER'.
- EntityTypes:
  - WorkOrder='WORK_ORDER': reutiliza el EntityType YA sembrado en logistica-db-seed.sql:140 ('Orden de trabajo'); NO se crea MAINTENANCE_WORK_ORDER.
  - Nuevos: MaintenanceSchedule='MAINTENANCE_SCHEDULE', FuelLog='FUEL_LOG', FleetDocument='FLEET_DOCUMENT', DriverRate='DRIVER_RATE', DriverTrip='DRIVER_TRIP' y DispatchZone='DISPATCH_ZONE'.
- NumberKinds.WorkOrder='WORKORDER'.

**(3) PermissionCatalog.cs.** Pasa de 49 a 52 permisos; se actualiza el comentario de cabecera.
- Nuevos, todos en categoría FLEET:
  - FleetView='fleet.view' ('Ver flota y choferes'/'View fleet & drivers')
  - DriverPayView='driverpay.view' ('Ver tarifas y viajes de choferes'/'View driver rates & trips')
  - DriverPayManage='driverpay.manage' ('Gestionar tarifas y viajes de choferes'/'Manage driver rates & trips')
- OwnerReadPermission:
  - VEHICLE, DRIVER, DISPATCH_ZONE, MAINTENANCE_SCHEDULE, WORK_ORDER, FUEL_LOG y FLEET_DOCUMENT → fleet.view
  - DRIVER_RATE y DRIVER_TRIP → driverpay.view
- OwnerWritePermission:
  - VEHICLE, DRIVER, DISPATCH_ZONE y FLEET_DOCUMENT → fleet.manage
  - MAINTENANCE_SCHEDULE, WORK_ORDER y FUEL_LOG → fleet.maintenance
  - DRIVER_RATE y DRIVER_TRIP → driverpay.manage
- RoleTemplates: Dispatcher += FleetView; Billing += DriverPayView; ReadOnly += FleetView. Driver sin cambios.

**(4) NumberingRules.IsKnownKind** acepta WORKORDER. Es el único cambio en ese archivo.

**(5) Exceptions.cs.** NotFoundException(string what, object? key = null, bool feminine = false): con feminine el mensaje es '{what} no encontrada.'. Las llamadas existentes no cambian. Mensajes 404 del lote:
- 'Vehículo no encontrado.'
- 'Chofer no encontrado.'
- 'Zona de despacho no encontrada.'
- 'Orden de trabajo no encontrada.'
- 'Programa de mantenimiento no encontrado.'
- 'Carga de combustible no encontrada.'
- 'Documento no encontrado.'
- 'Licencia no encontrada.'
- 'Certificación no encontrada.'
- 'Tarea no encontrada.'
- 'Tarifa no encontrada.'
- 'Viaje no encontrado.'
- 'Dispositivo no encontrado.'
- 'Usuario no encontrado.'

**(6) Entidades.** Namespace Teikem.Domain.Fleet, mapeadas 1:1 con el SQL.
- **Vehicle** ([AuditEntity(VEHICLE)]; ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped): VehicleId, [NotAudited] PublicId, TenantId, Code, PlateNumber?, MaxWeightKg?, MaxVolumeM3?, MaxStops?, VehicleTypeLookupId?, OwnershipLookupId?, FuelTypeLookupId?, Make?, Model?, ModelYear?, Vin?, CurrentOdometerKm?, HomeWarehouseId? (sin navegación), StatusCodeId, IsActive, CreatedAtUtc/CreatedBy/UpdatedAtUtc/UpdatedBy (timestamps [NotAudited]) y [NotAudited] RowVersion. Navegaciones: VehicleType, Ownership, FuelType, Status y Documents.
- **VehicleDocument** ([AuditEntity(VEHICLE)]; ISoftDeletable; sin TenantId): VehicleDocumentId, VehicleId, DocTypeLookupId, DocNumber?, IssuedDate?/ExpiryDate? (DateOnly), FileName?/StoragePath? (mapeados, no expuestos) e IsActive.
- **Driver** ([AuditEntity(DRIVER)]; ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped): DriverId, PublicId, TenantId, EmployeeCode, FullName, UserId?, HireDate?, HomeWarehouseId?, StatusCodeId, MaxStopsPerRoute?, IsActive, auditoría y RowVersion. Navegaciones: Status, User, Licenses, Certifications, Zones y Devices.
- **DriverLicense**: LicenseClassLookupId, LicenseNumber. **DriverCertification**: CertTypeLookupId, CertNumber. Ambas con [AuditEntity(DRIVER)], ISoftDeletable y sin TenantId.
- **DriverDevice** ([AuditEntity(DRIVER)]; ITenantScoped, ISoftDeletable): PlatformLookupId, [SensitiveData] PushToken, AppVersion y [NotAudited] LastSeenUtc.
- **DispatchZone** ([AuditEntity(DISPATCH_ZONE)]; ITenantScoped, ISoftDeletable).
- **DriverZone** ([AuditEntity(DRIVER)]; PK DriverId+DispatchZoneId; IsPrimary).
- **MaintenanceSchedule** ([AuditEntity(MAINTENANCE_SCHEDULE)]; ITenantScoped, ISoftDeletable; CreatedAtUtc).
- **MaintenanceWorkOrder** ([AuditEntity(WORK_ORDER)]; ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped): WorkOrderId, PublicId, VehicleId, MaintenanceScheduleId?, Number, MaintenanceTypeLookupId, StatusCodeId, OdometerKm?, ScheduledDate?, CompletedDate?, Vendor?, LaborCost?, PartsCost?, TotalCost ([NotAudited], solo lectura), CurrencyLookupId?, Notes?, IsActive, auditoría, RowVersion y Tasks.
- **MaintenanceTask** ([AuditEntity(WORK_ORDER)]; ISoftDeletable; sin TenantId): MaintenanceTaskId, WorkOrderId, Description, PartCost?, LaborCost?, IsCompleted (columna existente) e IsActive.
- **FuelLog** ([AuditEntity(FUEL_LOG)]; ITenantScoped, ISoftDeletable, IAuditStamped).
- **DriverPayPolicy** ([AuditEntity(DRIVER_RATE)]; ITenantScoped; PK TenantId): AttemptLevels, PayoutFormulaLookupId, UpdatedAtUtc y UpdatedBy.
- **DriverDeliveryRate / DriverAttemptRate / DriverTripRate** ([AuditEntity(DRIVER_RATE)]; ITenantScoped, ISoftDeletable, IEffectiveDated; CreatedAtUtc y CreatedBy).
- **DriverTrip** ([AuditEntity(DRIVER_TRIP)]; ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped): DriverTripId, PublicId, DriverId, SpecialServiceTypeId, DriverTripRateId?, TransportOrderId?, TripDate, Amount, RateMissing, StatusCodeId, Notes?, IsActive, auditoría y RowVersion. Navegaciones: Driver, TripType, Rate, Order y Status.

**(7) FleetConfigurations.cs y DriverPayConfigurations.cs.** Una IEntityTypeConfiguration por entidad con ToTable/HasKey.
- Claves: DriverZone compuesta; DriverPayPolicy con HasKey(TenantId) y ValueGeneratedNever; MaintenanceWorkOrder con WorkOrderId.
- PublicId con HasDefaultValueSql('NEWID()'); RowVersion con IsRowVersion.
- Precisiones: (12,3) MaxWeightKg, (12,4) MaxVolumeM3, (12,1) odómetros e intervalos km, (10,3) Liters y (18,4) dinero.
- TotalCost: HasComputedColumnSql('ISNULL([LaborCost],0) + ISNULL([PartsCost],0)', stored: true).
- Las FKs compuestas (Id, TenantId) del SQL NO se mapean en EF: las relaciones siguen por columna simple y EF no genera esquema.
- Índices únicos espejo con el mismo nombre y filtro: UQ_Vehicle_Code, UQ_Driver_EmployeeCode, UX_Driver_User, UQ_DispatchZone, UQ_WorkOrder_Number, UQ_DriverDeliveryRate_Open, UQ_DriverAttemptRate_Open, UQ_DriverTripRate_Open, UX_DriverTrip_Order y UX_DriverDevice_Token.

**(8) TeikemDbContext.** DbSets Vehicles, VehicleDocuments, Drivers, DriverLicenses, DriverCertifications, DriverDevices, DispatchZones, DriverZones, MaintenanceSchedules, MaintenanceWorkOrders, MaintenanceTasks, FuelLogs, DriverPayPolicies, DriverDeliveryRates, DriverAttemptRates, DriverTripRates y DriverTrips.

**(9) Contracts.** Firma posicional COMPLETA; es el contrato entre piezas paralelas y ninguna pieza la cambia. Son records sealed. Donde se indica '+Extra', el record lleva la propiedad [JsonExtensionData] IDictionary<string, JsonElement>? Extra, como OrderCreateRequest.

*FleetContracts:*
- VehicleListQuery(string? Search=null, string[]? VehicleType=null, string[]? Ownership=null, string[]? FuelType=null, string[]? Status=null, bool IncludeInactive=false)
- VehicleListItemDto(int Id, Guid PublicId, string Code, string? PlateNumber, string? VehicleTypeCode, string? VehicleType, string? OwnershipCode, string? Ownership, string? FuelTypeCode, string? FuelType, string? Make, string? Model, int? ModelYear, string? Vin, decimal? CurrentOdometerKm, string StatusCode, string Status, string? StatusColor, bool IsActive, DateOnly? NextDocumentExpiry)
- VehicleDocumentDto(int Id, string DocTypeCode, string DocType, string? DocNumber, DateOnly? IssuedDate, DateOnly? ExpiryDate, string ExpiryState, int? DaysToExpiry, bool IsSuperseded, bool IsActive)
- VehicleDetailDto(int Id, Guid PublicId, string Code, string? PlateNumber, decimal? MaxWeightKg, decimal? MaxVolumeM3, int? MaxStops, string? VehicleTypeCode, string? VehicleType, string? OwnershipCode, string? Ownership, string? FuelTypeCode, string? FuelType, string? Make, string? Model, int? ModelYear, string? Vin, decimal? CurrentOdometerKm, string StatusCode, string Status, bool IsTerminal, bool IsActive, IReadOnlyList<VehicleDocumentDto> Documents, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, string RowVersion)
- VehicleCreateRequest(string? Code, string? PlateNumber=null, string? VehicleType=null, string? Ownership=null, string? FuelType=null, string? Make=null, string? Model=null, int? ModelYear=null, string? Vin=null, decimal? MaxWeightKg=null, decimal? MaxVolumeM3=null, int? MaxStops=null, decimal? CurrentOdometerKm=null)
- VehiclePatchRequest(string? PlateNumber=null, string? VehicleType=null, string? Ownership=null, string? FuelType=null, string? Make=null, string? Model=null, int? ModelYear=null, string? Vin=null, decimal? MaxWeightKg=null, decimal? MaxVolumeM3=null, int? MaxStops=null, decimal? CurrentOdometerKm=null, string? RowVersion=null) +Extra
- VehicleDocumentRequest(string? DocType=null, string? DocNumber=null, DateOnly? IssuedDate=null, DateOnly? ExpiryDate=null, bool? ClearIssuedDate=null, bool? ClearExpiryDate=null)
- ExpiringDocumentsQuery(int WithinDays=30, string[]? DocType=null, string[]? Entity=null, bool IncludeExpired=true)
- ExpiringDocumentDto(string RowKey, string OwnerKind, Guid OwnerPublicId, string OwnerCode, string OwnerName, string DocumentKind, string DocumentTypeCode, string DocumentType, string? DocNumber, DateOnly? IssuedDate, DateOnly ExpiryDate, int DaysToExpiry, string ExpiryState)
- AvailabilityIssueDto(string Code, string Message, bool Blocking)
- DriverAvailabilityDto(int Id, Guid PublicId, string Code, string FullName, string? ZoneCode, bool Available, IReadOnlyList<AvailabilityIssueDto> Issues)
- VehicleAvailabilityDto(int Id, Guid PublicId, string Code, string? PlateNumber, string? VehicleTypeCode, bool Available, IReadOnlyList<AvailabilityIssueDto> Issues)
- FleetAvailabilityDto(DateOnly Date, IReadOnlyList<DriverAvailabilityDto> Drivers, IReadOnlyList<VehicleAvailabilityDto> Vehicles)

*DriverContracts:*
- DriverListQuery(string? Search=null, string[]? Status=null, int? DispatchZoneId=null, bool IncludeInactive=false)
- DriverListItemDto(int Id, Guid PublicId, string Code, string FullName, int? DispatchZoneId, string? ZoneCode, string? Area, int? MaxStopsPerRoute, int? EffectiveMaxStops, DateOnly? HireDate, string StatusCode, string Status, string? StatusColor, bool IsActive, DateOnly? LicenseExpiry, bool HasUser)
- DriverUserDto(int UserId, string Email, string? FullName)
- DriverLicenseDto(int Id, string LicenseClassCode, string LicenseClass, string LicenseNumber, DateOnly? IssuedDate, DateOnly? ExpiryDate, string ExpiryState, int? DaysToExpiry, bool IsSuperseded, bool IsActive)
- DriverLicenseRequest(string? LicenseClass=null, string? LicenseNumber=null, DateOnly? IssuedDate=null, DateOnly? ExpiryDate=null, bool? ClearIssuedDate=null, bool? ClearExpiryDate=null)
- DriverCertificationDto(int Id, string CertTypeCode, string CertType, string? CertNumber, DateOnly? IssuedDate, DateOnly? ExpiryDate, string ExpiryState, int? DaysToExpiry, bool IsSuperseded, bool IsActive)
- DriverCertificationRequest(string? CertType=null, string? CertNumber=null, DateOnly? IssuedDate=null, DateOnly? ExpiryDate=null, bool? ClearIssuedDate=null, bool? ClearExpiryDate=null)
- DriverDeviceDto(int Id, string PlatformCode, string Platform, string? AppVersion, bool HasPushToken, DateTime? LastSeenUtc, bool IsActive)
- DriverDetailDto(int Id, Guid PublicId, string Code, string FullName, int? DispatchZoneId, string? ZoneCode, string? Area, int? MaxStopsPerRoute, int? EffectiveMaxStops, DateOnly? HireDate, DriverUserDto? User, string StatusCode, string Status, bool IsTerminal, bool IsActive, IReadOnlyList<DriverLicenseDto> Licenses, IReadOnlyList<DriverCertificationDto> Certifications, IReadOnlyList<DriverDeviceDto> Devices, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, string RowVersion)
- DriverCreateRequest(string? Code, string? FullName, int? DispatchZoneId=null, int? MaxStopsPerRoute=null, DateOnly? HireDate=null, int? UserId=null)
- DriverPatchRequest(string? FullName=null, int? DispatchZoneId=null, bool? ClearZone=null, int? MaxStopsPerRoute=null, bool? ClearMaxStopsPerRoute=null, DateOnly? HireDate=null, int? UserId=null, bool? ClearUser=null, string? RowVersion=null) +Extra
- DriverRetireRequest(string? Comment=null)
- DispatchZoneDto(int Id, string Code, string? Name, int DriverCount, bool IsActive)
- DispatchZoneRequest(string? Code=null, string? Name=null)

*MaintenanceContracts:*
- MaintenanceScheduleDto(int Id, string Name, Guid? VehiclePublicId, string? VehicleCode, string? VehicleTypeCode, string? VehicleType, string TriggerCode, string Trigger, decimal? IntervalKm, int? IntervalDays, decimal? LastServiceKm, DateOnly? LastServiceDate, bool IsActive, DateTime CreatedAtUtc)
- MaintenanceScheduleRequest(string? Name=null, Guid? VehiclePublicId=null, string? VehicleType=null, string? Trigger=null, decimal? IntervalKm=null, int? IntervalDays=null, decimal? LastServiceKm=null, DateOnly? LastServiceDate=null, bool? ClearIntervalKm=null, bool? ClearIntervalDays=null)
- MaintenanceDueQuery(Guid? VehiclePublicId=null, string[]? Status=null)
- MaintenanceDueDto(int ScheduleId, string ScheduleName, Guid VehiclePublicId, string VehicleCode, string TriggerCode, decimal? IntervalKm, int? IntervalDays, decimal? LastServiceKm, DateOnly? LastServiceDate, string? LastWorkOrderNumber, decimal? CurrentOdometerKm, decimal? NextDueKm, DateOnly? NextDueDate, decimal? KmRemaining, int? DaysRemaining, string State)
- WorkOrderListQuery(Guid? VehiclePublicId=null, string[]? Status=null, string[]? MaintenanceType=null, int? ScheduleId=null, DateOnly? From=null, DateOnly? To=null, bool IncludeInactive=false, int Skip=0, int Take=100)
- WorkOrderListItemDto(int Id, Guid PublicId, string Number, Guid VehiclePublicId, string VehicleCode, string MaintenanceTypeCode, string MaintenanceType, int? ScheduleId, string? ScheduleName, string StatusCode, string Status, string? StatusColor, DateOnly? ScheduledDate, DateOnly? CompletedDate, decimal? OdometerKm, string? Vendor, decimal TotalCost, bool IsActive)
- MaintenanceTaskDto(int Id, string Description, decimal? PartCost, decimal? LaborCost, bool IsCompleted, bool IsActive)
- MaintenanceTaskRequest(string? Description=null, decimal? PartCost=null, decimal? LaborCost=null, bool? IsCompleted=null)
- WorkOrderDetailDto(int Id, Guid PublicId, string Number, Guid VehiclePublicId, string VehicleCode, string MaintenanceTypeCode, string MaintenanceType, int? ScheduleId, string? ScheduleName, string? ScheduleTriggerCode, string StatusCode, string Status, bool IsTerminal, DateOnly? ScheduledDate, DateOnly? CompletedDate, decimal? OdometerKm, string? Vendor, decimal? LaborCost, decimal? PartsCost, decimal TotalCost, string? CurrencyCode, string? Notes, bool CostsFromTasks, bool CanEdit, IReadOnlyList<MaintenanceTaskDto> Tasks, bool IsActive, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, string RowVersion)
- WorkOrderCreateRequest(Guid VehiclePublicId, string? MaintenanceType=null, int? ScheduleId=null, DateOnly? ScheduledDate=null, decimal? OdometerKm=null, string? Vendor=null, decimal? LaborCost=null, decimal? PartsCost=null, string? Currency=null, string? Notes=null)
- WorkOrderPatchRequest(DateOnly? ScheduledDate=null, decimal? OdometerKm=null, string? Vendor=null, decimal? LaborCost=null, decimal? PartsCost=null, string? Currency=null, string? Notes=null, string? RowVersion=null) +Extra
- WorkOrderStatusRequest(string ToCode, string? Comment=null, DateOnly? CompletedDate=null, decimal? OdometerKm=null, string? RowVersion=null)
- FuelLogQuery(Guid? VehiclePublicId=null, Guid? DriverPublicId=null, DateTime? FromUtc=null, DateTime? ToUtc=null, bool IncludeInactive=false, int Skip=0, int Take=100)
- FuelLogDto(int Id, Guid VehiclePublicId, string VehicleCode, Guid? DriverPublicId, string? DriverName, DateTime FillDateUtc, decimal? OdometerKm, decimal Liters, decimal TotalCost, string? CurrencyCode, string? Station, decimal? DistanceKm, decimal? KmPerLiter, decimal? CostPerKm, bool IsActive)
- FuelVehicleSummaryDto(Guid VehiclePublicId, string VehicleCode, int Fills, decimal TotalLiters, decimal TotalCost, decimal? DistanceKm, decimal? KmPerLiter, decimal? CostPerKm)
- FuelLogPageDto(int Total, int Skip, int Take, IReadOnlyList<FuelLogDto> Items, IReadOnlyList<FuelVehicleSummaryDto> Summary)
- FuelLogCreateRequest(Guid VehiclePublicId, DateTime FillDateUtc, decimal Liters, decimal TotalCost, decimal? OdometerKm=null, Guid? DriverPublicId=null, string? Currency=null, string? Station=null)
- FuelLogPatchRequest(DateTime? FillDateUtc=null, decimal? Liters=null, decimal? TotalCost=null, decimal? OdometerKm=null, bool? ClearOdometer=null, Guid? DriverPublicId=null, bool? ClearDriver=null, string? Currency=null, string? Station=null) +Extra

*DriverPayContracts:*
- DriverDeliveryRateDto(int Id, string ServiceTypeCode, string ServiceType, string PackageTypeCode, string PackageType, decimal Rate, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsCurrent)
- DriverAttemptRateDto(int AttemptNumber, int? Id, decimal? Rate, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsCurrent)
- DriverTripRateDto(int Id, int SpecialServiceTypeId, string TripType, bool TripTypeActive, decimal Rate, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsCurrent)
- DriverRatesDto(Guid DriverPublicId, string DriverCode, string DriverName, bool ReadOnly, DateOnly AsOf, int AttemptLevels, string PayoutFormulaCode, IReadOnlyList<DriverDeliveryRateDto> DeliveryRates, IReadOnlyList<DriverAttemptRateDto> AttemptRates, IReadOnlyList<DriverTripRateDto> TripRates)
- DriverDeliveryRateCreateRequest(string? ServiceType, string? PackageType, decimal Rate, DateOnly? EffectiveFrom=null)
- DriverRateUpdateRequest(decimal Rate, DateOnly? EffectiveFrom=null) +Extra
- DriverRateCloseRequest(DateOnly? EffectiveTo=null)
- DriverTripRateCreateRequest(int? SpecialServiceTypeId, decimal Rate, DateOnly? EffectiveFrom=null)
- PayoutFormulaOptionDto(string Code, string Label)
- DriverPayPolicyDto(int AttemptLevels, string PayoutFormulaCode, string PayoutFormula, IReadOnlyList<PayoutFormulaOptionDto> Formulas, bool IsDefault, DateTime? UpdatedAtUtc)
- DriverPayPolicyPatchRequest(string? PayoutFormula=null)
- PayoutAttemptInput(int Number, bool Delivered)
- PayoutAttemptRateInput(int Number, decimal Rate)
- PayoutPreviewRequest(IReadOnlyList<PayoutAttemptInput> Attempts, decimal? DeliveryRate=null, IReadOnlyList<PayoutAttemptRateInput>? AttemptRates=null, string? Formula=null)
- PayoutLineDto(string Kind, int? AttemptNumber, decimal Amount, string? Note)
- PayoutPreviewDto(string FormulaCode, decimal Total, IReadOnlyList<PayoutLineDto> Lines)
- DriverTripListQuery(DateOnly? From=null, DateOnly? To=null, string[]? Status=null, bool IncludeCancelled=false)
- DriverTripDto(int Id, Guid PublicId, Guid DriverPublicId, string DriverCode, int SpecialServiceTypeId, string TripType, DateOnly TripDate, decimal Amount, bool RateMissing, string? Note, Guid? OrderPublicId, string? OrderNumber, string StatusCode, string Status, string? Notes, bool IsActive, DateTime CreatedAtUtc)
- DriverTripCreateRequest(int? SpecialServiceTypeId, DateOnly? TripDate=null, string? Notes=null)
- DriverTripCancelRequest(string? Comment=null)
- SpecialDeliveryAssignRequest(Guid DriverPublicId, bool OverrideCredit=false, string? Comment=null, string? RowVersion=null)

Ninguna solicitud de tarifa ni de viaje lleva DriverId ni DriverPublicId: el chofer sale de la ruta (R31).

*OrderContracts (antes en P7; se mueve aquí para que el contrato quede fijo):*
- OrderCreateRequest agrega al final 'Guid? DriverPublicId = null', después de OverrideCredit. El importador (OrderImportService.cs:621 usa argumentos con nombre) no cambia.
- OrderDetailDto agrega al final 'Guid? AssignedDriverPublicId = null, string? AssignedDriverCode = null, string? AssignedDriverName = null', sin montos.

**(10) Costuras y consultas compartidas** (IMPLEMENTADAS en P0, no esqueleto).

FleetQueries (extensiones sobre TeikemDbContext):
- ResolveVehicleAsync(Guid publicId, bool track, ct) → 404 'Vehículo no encontrado.'.
- ResolveDriverAsync(Guid publicId, bool track, ct) → 404 'Chofer no encontrado.'.
- IsTerminalAsync(int statusCodeId, ct).
- LockVehicleAsync(int vehicleId, ct): db.Vehicles.FromSqlInterpolated('SELECT * FROM dbo.Vehicle WITH (UPDLOCK, ROWLOCK) WHERE VehicleId = {id} AND TenantId = {db.CurrentTenantId}'), tracked. Exige transacción ambiente; sin ella lanza InvalidOperationException. Es la ÚNICA forma de cargar el vehículo en escrituras que tocan el odómetro.
- LastRecordedOdometerAsync(int vehicleId, ct) → (decimal Km, DateOnly Date, string Source)?: la mayor lectura entre FuelLog activos y OT CLOSED activas.
- LoadFleetDocumentsAsync(DateOnly today, FleetDocumentScope scope, ct) → IReadOnlyList<FleetDocumentRow>.
  - FleetDocumentScope(bool IncludeVehicles=true, bool IncludeDrivers=true, bool OnlyActiveOwners=true, IReadOnlyCollection<int>? VehicleIds=null, IReadOnlyCollection<int>? DriverIds=null).
  - Hace tres consultas que parten SIEMPRE de db.Vehicles/db.Drivers (filtro de tenant): documentos de vehículo, licencias y certificaciones activos. Marca owner activo/terminal y aplica FleetDocuments.MarkSuperseded.
  - Lo reutilizan el panel, la disponibilidad y la fuente FLEET_DOCUMENT; la regla no se triplica.
- Constantes:
  - VehicleInactiveMessage='El vehículo está inactivo; reactívelo para registrar órdenes de trabajo o cargas de combustible.'
  - DriverRetiredMessage='El chofer fue eliminado; sus tarifas y viajes solo se consultan.'

IDriverRateResolver:
- DeliveryRateAsync(int driverId, int serviceTypeLookupId, int packageTypeLookupId, DateOnly asOf, ct) → DriverRateResolution
- AttemptRatesAsync(int driverId, DateOnly asOf, ct) → IReadOnlyDictionary<int, decimal>
- TripRateAsync(int driverId, int specialServiceTypeId, DateOnly asOf, ct) → DriverRateResolution
- PolicyAsync(ct) → DriverPayPolicySnapshot
- Records: DriverRateResolution(decimal Amount, int? RateId, bool Missing) y DriverPayPolicySnapshot(int AttemptLevels, string FormulaCode).

IFleetAvailabilityService:
- GetAsync(DateOnly date, bool onlyAvailable, ct) → FleetAvailabilityDto
- CheckDriverAsync(int driverId, DateOnly date, ct) → AvailabilityResult
- CheckVehicleAsync(int vehicleId, DateOnly date, ct) → AvailabilityResult

Domain/Fleet/FleetAvailability.cs:
- AvailabilityIssue(string Code, string Message, bool Blocking)
- AvailabilityResult(bool Available, IReadOnlyList<AvailabilityIssue> Issues)
- AvailabilityDocSnapshot(string TypeCode, string TypeLabel, DateOnly? ExpiryDate, bool IsSuperseded)
- DriverAvailabilitySnapshot(int DriverId, bool IsActive, bool IsInitialStatus, bool IsTerminal, string StatusLabel, IReadOnlyList<AvailabilityDocSnapshot> Licenses, IReadOnlyList<AvailabilityDocSnapshot> Certifications)
- VehicleAvailabilitySnapshot(int VehicleId, bool IsActive, bool IsInitialStatus, bool IsTerminal, string StatusLabel, IReadOnlyList<AvailabilityDocSnapshot> Documents, IReadOnlyList<string> InProgressWorkOrderNumbers)
- AvailabilityIssueCodes: DRIVER_INACTIVE, DRIVER_STATUS, NO_VALID_LICENSE, CERT_EXPIRED, VEHICLE_INACTIVE, VEHICLE_STATUS, VEHICLE_DOC_EXPIRED, WORK_ORDER_IN_PROGRESS, DOC_EXPIRING y VEHICLE_NO_DOCUMENTS.

Domain/Fleet/FleetDocuments.cs (puro):
- FleetDocumentRow(string RowKey, string OwnerKind, int OwnerId, Guid OwnerPublicId, string OwnerCode, string OwnerName, bool OwnerIsActive, bool OwnerIsTerminal, string DocumentKind, int DocTypeLookupId, string DocTypeCode, string? DocNumber, DateOnly? IssuedDate, DateOnly? ExpiryDate, bool IsSuperseded=false).
- MarkSuperseded(rows): agrupa por (OwnerKind, OwnerId, DocumentKind, DocTypeCode). Un documento con ExpiryDate queda IsSuperseded=true si en su grupo existe otro activo con ExpiryDate no nula y posterior. Los documentos sin vencimiento nunca superan ni quedan superados.

Domain/Fleet/FleetRules.cs (puro):
- NormalizeCode(string? raw, int maxLen, string requiredMessage) → (string? Code, string? Error). Recorta, pasa a mayúsculas; si excede: 'El código no puede exceder {n} caracteres.'
- MatchesSearch(string? query, params string?[] fields): sin distinguir acentos ni mayúsculas.
- EffectiveMaxStops(int? own, int? tenantDefault).
- ValidateDocumentDates(issued, expiry) → 'La fecha de vencimiento no puede ser anterior a la de emisión.'
- ExpiryState(DateOnly? expiry, DateOnly today, int withinDays=30).
- DaysTo(expiry, today).
- DecimalError(decimal? value, int precision, int scale) → null o 'El valor admite como máximo {scale} decimales y debe ser menor que {10^(precision-scale):N0}.' (cultura invariante). Todas las piezas lo aplican antes de guardar: ningún desbordamiento termina en 500.
- RaiseOdometer(decimal? current, decimal? reading) → máximo monotónico.

**(11) DI.**
- Servicios: VehicleService, VehicleDocumentService, DriverService, DriverDocumentService, DispatchZoneService, FleetDocumentService, IFleetAvailabilityService→FleetAvailabilityService, MaintenanceScheduleService, MaintenanceWorkOrderService, FuelLogService, DriverRateService, DriverPayPolicyService, IDriverRateResolver→DriverRateResolver, DriverTripService y SpecialDeliveryDispatchService.
- IStatusTransitionEffect: VehicleStatusEffect, DriverStatusEffect, DriverRatesRetirementEffect, WorkOrderStatusEffect, DriverTripOrderEffect y DriverTripStatusEffect.
- IDataSource: VehicleDataSource, DriverDataSource, WorkOrderDataSource, FuelLogDataSource y FleetDocumentDataSource.
- IOwnedEntityResolver: VehicleOwnedEntityResolver, DriverOwnedEntityResolver, DispatchZoneOwnedEntityResolver, MaintenanceScheduleOwnedEntityResolver, WorkOrderOwnedEntityResolver, FuelLogOwnedEntityResolver, DriverTripOwnedEntityResolver, ClosedOwnedEntityResolver (dos instancias: DRIVER_RATE y FLEET_DOCUMENT, registradas con factory) y PortalUserOwnedEntityResolver (hueco del Lote 2).

**(12) SystemAnalyticsSeeder**, bloque '// Lote 4'.
- Vistas:
  - 'Vehículos' (VEHICLE: Code, PlateNumber, VehicleType, Ownership, FuelType, CurrentOdometerKm, Status, IsActive; orden Code asc).
  - 'Documentos por vencer' (FLEET_DOCUMENT: ExpiryDate, OwnerKind, OwnerCode, OwnerName, DocumentType, DocNumber, DaysToExpiry, ExpiryState; filtro ExpiryState in [EXPIRED, EXPIRING]; orden ExpiryDate asc).
- Indicadores:
  - 'Documentos por vencer' (FLEET_DOCUMENT, COUNT, mismo filtro, sin rango, Pulso, orden 80, OPERATIONS).
  - 'Órdenes de trabajo abiertas' (WORK_ORDER, COUNT, IsActive y StatusCode in [OPEN, IN_PROGRESS], rango ALL, Pulso, orden 81).

**(13) Esqueletos compilables.** Clases referenciadas por DI con su firma pública (definida en P1-P8) y cuerpo throw new NotImplementedException(), salvo:
- EntityTypeCode de cada resolver y Key/EntityTypeCode de cada IDataSource, que se implementan ya (las pruebas de cobertura de P0 los leen).

Cada pieza reemplaza su archivo completo.

**(14) Pruebas.** FleetCatalogTests, FleetContractsTests, FleetRulesTests, TenantIsolationModelTests y OwnedEntityResolverCoverageTests. En OrderCatalogTests, 49 pasa a 52 conservando las aserciones de orders.credit_override.

Archivos:

- `Diseño/logistica-db-estructura.sql`
- `Diseño/logistica-db-seed.sql`
- `src/Teikem.Domain/Constants/CatalogDomains.cs`
- `src/Teikem.Domain/Constants/PermissionCatalog.cs`
- `src/Teikem.Domain/Orders/NumberingRules.cs`
- `src/Teikem.Domain/Fleet/Vehicle.cs`
- `src/Teikem.Domain/Fleet/Driver.cs`
- `src/Teikem.Domain/Fleet/DispatchZone.cs`
- `src/Teikem.Domain/Fleet/Maintenance.cs`
- `src/Teikem.Domain/Fleet/FuelLog.cs`
- `src/Teikem.Domain/Fleet/DriverPay.cs`
- `src/Teikem.Domain/Fleet/FleetRules.cs`
- `src/Teikem.Domain/Fleet/FleetAvailability.cs`
- `src/Teikem.Domain/Fleet/FleetDocuments.cs`
- `src/Teikem.Infrastructure/Abstractions/Exceptions.cs`
- `src/Teikem.Infrastructure/Persistence/Configurations/FleetConfigurations.cs`
- `src/Teikem.Infrastructure/Persistence/Configurations/DriverPayConfigurations.cs`
- `src/Teikem.Infrastructure/Persistence/TeikemDbContext.cs`
- `src/Teikem.Infrastructure/DependencyInjection.cs`
- `src/Teikem.Infrastructure/Seeding/SystemAnalyticsSeeder.cs`
- `src/Teikem.Infrastructure/Contracts/FleetContracts.cs`
- `src/Teikem.Infrastructure/Contracts/DriverContracts.cs`
- `src/Teikem.Infrastructure/Contracts/MaintenanceContracts.cs`
- `src/Teikem.Infrastructure/Contracts/DriverPayContracts.cs`
- `src/Teikem.Infrastructure/Contracts/OrderContracts.cs`
- `src/Teikem.Infrastructure/Fleet/FleetQueries.cs`
- `src/Teikem.Infrastructure/Fleet/IDriverRateResolver.cs`
- `src/Teikem.Infrastructure/Fleet/IFleetAvailabilityService.cs`
- `tests/Teikem.Tests/FleetCatalogTests.cs`
- `tests/Teikem.Tests/FleetContractsTests.cs`
- `tests/Teikem.Tests/FleetRulesTests.cs`
- `tests/Teikem.Tests/OrderCatalogTests.cs`
- `tests/Teikem.Tests/TenantIsolationModelTests.cs`
- `tests/Teikem.Tests/OwnedEntityResolverCoverageTests.cs`

### P1 — Vehículos (panel Vehículos) y documentos de vehículo

- Toca archivos compartidos: no. Depende de: P0.

**VehicleRules (puro)**
- NormalizeVin: mayúsculas, sin espacios, máximo 40 ('El VIN admite como máximo 40 caracteres.').
- ValidateCapacities: 'La capacidad no puede ser negativa.'; MaxStops ≥ 1 ('El tope de paradas debe ser mayor o igual a 1.').
- ValidateOdometer: 'El odómetro no puede ser negativo.'
- ValidateModelYear: 1900..año actual+1 ('El año del modelo debe estar entre 1900 y {año+1}.').
- ValidateManualOdometer(decimal newKm, (decimal Km, DateOnly Date)? lastRecorded) → 'El odómetro no puede ser menor que la última lectura registrada ({km} km el yyyy-MM-dd).'

Además se aplica FleetRules.DecimalError a MaxWeightKg (12,3), MaxVolumeM3 (12,4) y CurrentOdometerKm (12,1). El error va en errors del campo.

**VehicleService(db, tenant, lookups, StatusService statuses)**

(a) ListAsync(VehicleListQuery)
- Filtro de tenant; IsActive salvo includeInactive.
- Filtros multi-valor vehicleType[]/ownership[]/fuelType[]/status[] por código; un código desconocido da 400.
- qbox 'search' en memoria con FleetRules.MatchesSearch sobre código, placa, etiquetas e InternalCode de tipo/propiedad/combustible, VIN, marca y modelo.
- NextDocumentExpiry = mínima ExpiryDate de los documentos activos NO superados (FleetDocuments.MarkSuperseded).
- Orden por Code.

(b) GetAsync(publicId): 404 'Vehículo no encontrado.'

(c) CreateAsync
- Code con FleetRules.NormalizeCode: 400 'El código del vehículo es obligatorio.'; 400 'El código no puede exceder 30 caracteres.'
- Duplicado, aunque el otro esté dado de baja: 409 'Ya existe un vehículo con ese código.' Chequeo previo más UQ_Vehicle_Code vía SaveGuardedAsync.
- Lookups por código: 400 'Tipo de vehículo desconocido: 'X'.', 'Propiedad desconocida: 'X'.', 'Tipo de combustible desconocido: 'X'.'
- VIN repetido entre activos: 409 'Ya existe un vehículo activo con ese VIN.'
- Dentro de RunInTransactionAsync, el vehículo nace en el estatus inicial: GetInitialAsync + TransitionAsync(VehicleStatus, VEHICLE, id, null, inicial).

(d) UpdateAsync (PATCH)
- null = sin cambio; rowVersion opcional con ApplyRowVersion (409).
- 'code' u 'homeWarehouseId' en Extra: 400 'El código del vehículo se fija al crearlo; no se puede cambiar.'
- Si llega CurrentOdometerKm, dentro de RunInTransactionAsync se carga con FleetQueries.LockVehicleAsync y se valida con ValidateManualOdometer contra FleetQueries.LastRecordedOdometerAsync. La corrección queda auditada y puede bajar un error manual, pero no por debajo de la última lectura de combustible u OT cerrada.

(e) TransitionStatusAsync(StatusChangeRequest)
- 400 si falta toCode.
- ACTIVE↔MAINTENANCE (MAINTENANCE es lateral) y → INACTIVE (terminal, baja definitiva), solo vía TransitionAsync.

(f) SetActiveAsync(publicId, active)
- Checkbox Activo, sin DELETE.
- Reactivar un terminal: 409 'El vehículo está dado de baja definitiva; no se puede reactivar.'

**VehicleDocumentService**
- ListAsync(includeInactive), AddAsync, UpdateAsync y DeactivateAsync, siempre a través del vehículo del tenant.
- El id de documento debe pertenecer a ESE vehículo; si no (otro vehículo u otro tenant, BOLA por id hijo): 404 'Documento no encontrado.'
- DocType por código VehicleDocType: 400 'Tipo de documento de vehículo desconocido: 'X'.'
- Fechas validadas con FleetRules.ValidateDocumentDates.
- ExpiryState/DaysToExpiry con FleetRules (30 días) e IsSuperseded con FleetDocuments.MarkSuperseded sobre los documentos del vehículo.
- Los adjuntos no se exponen.

**VehicleStatusEffect (VehicleStatus)**
- Al entrar a una etapa TERMINAL pone IsActive=0 en la entidad tracked; no transiciona nada.

**VehiclesController** [Route('api/v1/vehicles')][Authorize][RequireModule(ModuleKeys.Catalog)]
- GET '' (fleet.view; search, vehicleType, ownership, fuelType, status, includeInactive)
- POST '' (fleet.manage)
- GET '{publicId:guid}' (fleet.view)
- PATCH '{publicId:guid}' (fleet.manage)
- POST '{publicId:guid}/status' (fleet.manage)
- POST '{publicId:guid}/deactivate' y '/reactivate' (fleet.manage, 204)
- GET '{publicId:guid}/documents' (fleet.view, includeInactive)
- POST '{publicId:guid}/documents', PATCH '{publicId:guid}/documents/{id:int}' y POST '{publicId:guid}/documents/{id:int}/deactivate' (fleet.manage)

Archivos:

- `src/Teikem.Domain/Fleet/VehicleRules.cs`
- `src/Teikem.Infrastructure/Services/VehicleService.cs`
- `src/Teikem.Infrastructure/Services/VehicleDocumentService.cs`
- `src/Teikem.Infrastructure/Services/VehicleStatusEffect.cs`
- `src/Teikem.Api/Controllers/VehiclesController.cs`
- `tests/Teikem.Tests/VehicleRulesTests.cs`

### P2 — Choferes (identidad, ficha maestro-detalle, vínculo con usuario), licencias, certificaciones, dispositivos y zonas de despacho

- Toca archivos compartidos: no. Depende de: P0.

**DriverRules (puro)**
- ValidateFullName: obligatorio, máximo 150 ('El nombre del chofer es obligatorio.' / 'El nombre del chofer admite como máximo 150 caracteres.').
- ValidateMaxStops: 'El tope de paradas debe ser mayor o igual a 1.'
- ValidateLicenseNumber: obligatorio, máximo 60 ('El número de licencia es obligatorio.').

**DriverService(db, tenant, lookups, statuses, PermissionService permissions)**

(a) ListAsync(DriverListQuery)
- qbox sobre código, nombre, zona y área.
- ZoneCode/Area salen de la zona primaria (Area = DispatchZone.Name).
- EffectiveMaxStops = FleetRules.EffectiveMaxStops(MaxStopsPerRoute, Tenant.MaxStopsPerRouteDefault) (R4).
- LicenseExpiry = mínima ExpiryDate de las licencias activas NO superadas.

(b) GetAsync → DriverDetailDto con licencias, certificaciones, dispositivos y usuario vinculado.

(c) CreateAsync
- Code (EmployeeCode) normalizado y obligatorio: 400 'El código del chofer es obligatorio.'; máximo 30.
- Duplicado aunque el otro esté dado de baja: 409 'Ya existe un chofer con ese código.'
- dispatchZoneId opcional: zona del tenant (404 'Zona de despacho no encontrada.') y activa (400 'La zona de despacho está inactiva.'). Se guarda DriverZone(IsPrimary=1).
- MaxStopsPerRoute ≥ 1.
- userId opcional, vía ValidateUserLinkAsync: exige admin.users (403 'Falta el permiso 'admin.users'.'), membresía UserTenant en el tenant (404 'Usuario no encontrado.'), usuario de tipo UserKinds.Internal (400 'Solo un usuario interno se puede vincular a un chofer.'), membresía ACTIVE (409 'El usuario no tiene una membresía activa en esta compañía.') y que no esté vinculado a otro chofer (409 'El usuario ya está vinculado a otro chofer.', también por UX_Driver_User vía SaveGuardedAsync).
- El chofer nace en el estatus inicial vía TransitionAsync.
- No crea filas de tarifa.

(d) UpdateAsync (PATCH en línea)
- Campos: fullName; dispatchZoneId o clearZone (reemplaza la fila primaria); maxStopsPerRoute o clearMaxStopsPerRoute; hireDate; userId (ValidateUserLinkAsync) o clearUser (también exige admin.users); rowVersion.
- 'code'/'employeeCode' en Extra: 400 'El código del chofer se fija al crearlo; no se puede cambiar.'

(e) TransitionStatusAsync: ACTIVE↔UNAVAILABLE (lateral).

(f) SetActiveAsync: reactivar un terminal da 409 'El chofer fue eliminado; no se puede reactivar.'

(g) RetireAsync(publicId, DriverRetireRequest?) = DELETE: TransitionAsync(DriverStatus → INACTIVE, comentario), sin borrado físico; cascada por efectos.

(h) GetDevicesAsync y DeactivateDeviceAsync
- Nunca devuelven el PushToken.
- Dispositivo de otro chofer: 404 'Dispositivo no encontrado.'

**DriverDocumentService**
- Licencias: LicenseClass por código (400 'Clase de licencia desconocida: 'X'.').
- Certificaciones: CertificationType por código (400 'Tipo de certificación desconocido: 'X'.').
- list/add/update/deactivate a través del chofer del tenant. El id hijo debe pertenecer a ese chofer: 404 'Licencia no encontrada.' / 'Certificación no encontrada.' (BOLA por id hijo).
- Fechas con FleetRules, ExpiryState e IsSuperseded.

**DispatchZoneService**
- ListAsync(includeInactive) con DriverCount.
- CreateAsync: Code obligatorio, máximo 20, en mayúsculas. Duplicado: 409 'Ya existe una zona de despacho con ese código.' Name máximo 120.
- UpdateAsync: code en el body → 400 'El código de la zona se fija al crearla; no se puede cambiar.'
- SetActiveAsync: inactivar con choferes activos asignados → 409 'La zona tiene choferes asignados; reasígnelos antes de inactivarla.'

**DriverStatusEffect (DriverStatus)**
- Al entrar a TERMINAL: IsActive=0, UserId=NULL, desactiva los DriverDevice y borra sus filas DriverZone (asociación de estado actual).

**Controladores** con [RequireModule(Catalog)].

DriversController [Route('api/v1/drivers')]:
- GET '' (fleet.view)
- POST '' (fleet.manage)
- GET '{publicId:guid}' (fleet.view)
- PATCH '{publicId:guid}' (fleet.manage; admin.users adicional lo exige el servicio si toca el usuario)
- POST '{publicId:guid}/status' (fleet.manage)
- POST '/deactivate' y '/reactivate' (fleet.manage, 204)
- DELETE '{publicId:guid}' (fleet.manage, 204, cuerpo opcional {comment})
- GET/POST '{publicId:guid}/licenses', PATCH '/licenses/{id:int}' y POST '/licenses/{id:int}/deactivate'. Las mismas rutas para '/certifications'. Lectura fleet.view, escritura fleet.manage.
- GET '{publicId:guid}/devices' (fleet.view)
- POST '/devices/{id:int}/deactivate' (fleet.manage)

DispatchZonesController [Route('api/v1/dispatch-zones')]:
- GET (fleet.view)
- POST, PATCH '{id:int}' y POST '{id:int}/deactivate'|'/reactivate' (fleet.manage)

Archivos:

- `src/Teikem.Domain/Fleet/DriverRules.cs`
- `src/Teikem.Infrastructure/Services/DriverService.cs`
- `src/Teikem.Infrastructure/Services/DriverDocumentService.cs`
- `src/Teikem.Infrastructure/Services/DispatchZoneService.cs`
- `src/Teikem.Infrastructure/Services/DriverStatusEffect.cs`
- `src/Teikem.Api/Controllers/DriversController.cs`
- `src/Teikem.Api/Controllers/DispatchZonesController.cs`
- `tests/Teikem.Tests/DriverRulesTests.cs`

### P3 — Documentos por vencer y disponibilidad para despacho

- Toca archivos compartidos: no. Depende de: P0.

Los tres consumidores de documentos usan EXCLUSIVAMENTE FleetQueries.LoadFleetDocumentsAsync (P0); ninguno reescribe la unión ni la regla de 'vigente por tipo'.

**FleetDocumentService.GetExpiringAsync(ExpiringDocumentsQuery)**
- Toma las filas del helper con dueños activos no terminales y excluye las IsSuperseded.
- ExpiryDate no nula y ≤ hoy+withinDays; con includeExpired=false excluye vencidos.
- docType[] ∈ FleetDocumentKinds: REGISTRATION/INSURANCE/INSPECTION/PERMIT, LICENSE y CERTIFICATION. Desconocido: 400 'Tipo de documento desconocido: 'X'.'
- entity[] ∈ {VEHICLE, DRIVER}. Desconocido: 400 'Entidad desconocida: 'X'; use VEHICLE o DRIVER.'
- withinDays 0..365: 400 'withinDays debe estar entre 0 y 365.'
- Orden: ExpiryDate asc y luego código del dueño.
- RowKey 'VD-{id}'|'DL-{id}'|'DC-{id}'.
- Etiquetas con ILookupCache/MultilingualText.

**FleetAvailabilityRules (puro)**
- EvaluateDriver(snapshot, date) y EvaluateVehicle(snapshot, date) → AvailabilityResult. IGNORAN los documentos IsSuperseded.
- Bloquean:
  - DRIVER_INACTIVE
  - DRIVER_STATUS (estatus distinto del inicial)
  - NO_VALID_LICENSE: sin licencias vigentes no superadas ('Sin licencia vigente.') o todas vencidas ('Licencia vencida el yyyy-MM-dd.')
  - VEHICLE_INACTIVE
  - VEHICLE_STATUS
  - VEHICLE_DOC_EXPIRED ('{tipo} vencido el yyyy-MM-dd.')
  - WORK_ORDER_IN_PROGRESS ('Orden de trabajo {número} en proceso.')
- Solo avisan: CERT_EXPIRED, DOC_EXPIRING (≤ 30 días) y VEHICLE_NO_DOCUMENTS.
- El preventivo vencido no se evalúa aquí.

**FleetAvailabilityService : IFleetAvailabilityService**
- Arma los snapshots por lote, sin N+1: LoadFleetDocumentsAsync con los ids del lote, OT IN_PROGRESS activas por vehículo, estatus iniciales/terminales y zona primaria.
- GetAsync(date, onlyAvailable).
- CheckDriverAsync y CheckVehicleAsync evalúan un id; los usan P7 y el futuro planificador de Despacho.

**FleetController** [Route('api/v1/fleet')][RequireModule(Catalog)]
- GET 'expiring-documents' (fleet.view; withinDays=30, docType[], entity[], includeExpired=true)
- GET 'availability' (fleet.view; date = hoy por defecto, onlyAvailable)

**FleetDocumentDataSource**
- key FLEET_DOCUMENT; EntityTypeCode null; DateField null; IdField 'RowKey'; DefaultBusinessModule OPERATIONS.
- Filas del helper no superadas, de dueños activos no terminales.
- Campos: RowKey, OwnerKind, OwnerCode, OwnerName, DocumentKind, DocumentType, DocNumber, IssuedDate, ExpiryDate, DaysToExpiry y ExpiryState (ventana fija de 30 días). Son los nombres que usa SystemAnalyticsSeeder.
- Respeta q.Ids y el tope MaxRows.

Archivos:

- `src/Teikem.Domain/Fleet/FleetAvailabilityRules.cs`
- `src/Teikem.Infrastructure/Services/FleetDocumentService.cs`
- `src/Teikem.Infrastructure/Services/FleetAvailabilityService.cs`
- `src/Teikem.Infrastructure/Analytics/FleetDocumentDataSource.cs`
- `src/Teikem.Api/Controllers/FleetController.cs`
- `tests/Teikem.Tests/FleetAvailabilityRulesTests.cs`

### P4 — Mantenimiento preventivo y órdenes de trabajo

- Toca archivos compartidos: no. Depende de: P0.

**MaintenanceDue (puro)**

Evaluate(trigger, intervalKm, intervalDays, lastServiceKm, lastServiceDate, currentOdometerKm, today) → NextDueKm, NextDueDate, KmRemaining, DaysRemaining y State.
- MILEAGE usa km, TIME usa días y BOTH toma el peor.
- OVERDUE si lo restante ≤ 0; DUE_SOON si ≤ 10 % del intervalo; OK en otro caso.
- NO_BASELINE si falta el último servicio de la dimensión requerida, o el odómetro actual en MILEAGE.

ValidateClose(int incompleteTasks, string? scheduleTriggerCode, decimal? odometerKm) → (int Status, string Message)?:
- Tareas pendientes → 422 'La orden tiene {n} tarea(s) sin completar; márquelas como completadas o quítelas antes de cerrarla.'
- Programa MILEAGE/BOTH sin odómetro → 400 'Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.'

**MaintenanceScheduleService**

(a) ListAsync(includeInactive).

(b) GetDueAsync(MaintenanceDueQuery)
- Expande cada programa activo a sus vehículos activos no terminales: el VehicleId, o todos los del VehicleTypeLookupId.
- Último servicio por (programa, vehículo) = última OT CLOSED activa con ese MaintenanceScheduleId y VehicleId. Si el programa es por vehículo, respaldo con LastServiceKm/LastServiceDate.
- Filtros vehiclePublicId y status[].

(c) CreateAsync/UpdateAsync
- Exactamente uno de vehiclePublicId/vehicleType: 400 'Indique el vehículo o el tipo de vehículo del programa, no ambos.'
- Name obligatorio, máximo 150: 'El nombre del programa es obligatorio.'
- Trigger por código: 400 'Disparador desconocido: 'X'.'
- Coherencia del intervalo:
  - MILEAGE: 'Un programa por kilometraje exige un intervalo en km mayor que 0.'
  - TIME: 'Un programa por tiempo exige un intervalo en días mayor que 0.'
  - BOTH exige ambos.
- DecimalError sobre IntervalKm/LastServiceKm (12,1); LastServiceKm ≥ 0; LastServiceDate no futura.
- Programa inexistente: 404 'Programa de mantenimiento no encontrado.'

(d) SetActiveAsync.

**MaintenanceWorkOrderService**

(a) ListAsync(WorkOrderListQuery) y GetAsync(publicId): 404 'Orden de trabajo no encontrada.' Devuelve CanEdit (capacidad) y CostsFromTasks.

(b) CreateAsync
- Vehículo del tenant (404) activo y no terminal (409 FleetQueries.VehicleInactiveMessage).
- MaintenanceType por código. PREVENTIVE por defecto con programa; sin programa es obligatorio (400 'El tipo de mantenimiento es obligatorio.').
- Programa opcional:
  - debe aplicar al vehículo (400 'El programa de mantenimiento no aplica a este vehículo.');
  - solo con PREVENTIVE (400 'Una orden correctiva no se asocia a un programa preventivo.').
- Costos ≥ 0 ('Los costos no pueden ser negativos.') y DecimalError (18,4); odómetro (12,1).
- Número 'OT-#####' con NumberSequence Kind WORKORDER por tenant (ClientId null):
  - EnsureAsync antes de la transacción, NextAsync dentro y NumberFormat.Resolve.
  - SaveGuardedAsync con UQ_WorkOrder_Number.
  - El bloqueo de fila de NextAsync serializa altas concurrentes: números consecutivos sin 409 ni 500.
- La OT nace OPEN vía TransitionAsync(WorkOrderStatus, WORK_ORDER, id, null, inicial).

(c) UpdateAsync (PATCH)
- 'number'/'vehiclePublicId' en Extra: 400 'El número y el vehículo de la orden de trabajo se fijan al crearla.'
- EnsureAllowedAsync(WORK_ORDER, StatusCodeId, EDIT_WORK_ORDER): 422 en CLOSED/CANCELLED por defecto.
- Con tareas activas se rechazan los costos del encabezado: 400 'La orden tiene tareas: los costos de labor y partes se calculan con la suma de sus tareas.'

(d) AddTaskAsync, UpdateTaskAsync (incluye IsCompleted) y DeactivateTaskAsync
- Misma capacidad.
- El taskId debe pertenecer a esa OT: 404 'Tarea no encontrada.' (BOLA por id hijo).
- Description obligatoria, máximo 250: 'La descripción de la tarea es obligatoria.'
- Costos ≥ 0 y DecimalError.
- Recalcula LaborCost = Σ LaborCost y PartsCost = Σ PartCost de las tareas activas.

(e) TransitionStatusAsync(WorkOrderStatusRequest)
- Para CLOSED:
  - fija CompletedDate (hoy por defecto; futura → 400 'La fecha de cierre no puede ser futura.');
  - fija OdometerKm ≥ 0 (con DecimalError) en la entidad tracked;
  - aplica MaintenanceDue.ValidateClose (tareas activas incompletas y odómetro si el programa es MILEAGE/BOTH) antes de TransitionAsync.
- IN_PROGRESS y CANCELLED van directo. OPEN→CLOSED directo también es válido: el terminal fuera de orden lo permite StatusService.

**WorkOrderStatusEffect (WorkOrderStatus)**

StatusService perezoso vía IServiceProvider, como OrderStatusEffect. Carga el vehículo SIEMPRE con FleetQueries.LockVehicleAsync.
- IN_PROGRESS: si el vehículo está ACTIVE lo transiciona a MAINTENANCE ('{número} en proceso').
- Al salir a CLOSED/CANCELLED: si el vehículo está en MAINTENANCE y no queda otra OT IN_PROGRESS activa, lo regresa a ACTIVE ('{número} cerrada/cancelada').
- En CLOSED además:
  - actualiza LastServiceKm/LastServiceDate si el programa es por vehículo;
  - aplica Vehicle.CurrentOdometerKm = FleetRules.RaiseOdometer(actual, lectura).
- Un vehículo terminal no se toca.

**Controladores** con [RequireModule(Catalog)].

MaintenanceSchedulesController [Route('api/v1/maintenance-schedules')]:
- GET '' y GET 'due' (fleet.view)
- POST '', PATCH '{id:int}' y POST '{id:int}/deactivate'|'/reactivate' (fleet.maintenance)

MaintenanceWorkOrdersController [Route('api/v1/maintenance-work-orders')]:
- GET '' y GET '{publicId:guid}' (fleet.view)
- POST '', PATCH '{publicId:guid}', POST '{publicId:guid}/status', POST '{publicId:guid}/tasks', PATCH '{publicId:guid}/tasks/{taskId:int}' y POST '{publicId:guid}/tasks/{taskId:int}/deactivate' (fleet.maintenance)

El historial se consulta con /status/history/WORK_ORDER/{id} y las capacidades con /status/capabilities/WORK_ORDER.

Archivos:

- `src/Teikem.Domain/Fleet/MaintenanceDue.cs`
- `src/Teikem.Infrastructure/Services/MaintenanceScheduleService.cs`
- `src/Teikem.Infrastructure/Services/MaintenanceWorkOrderService.cs`
- `src/Teikem.Infrastructure/Services/WorkOrderStatusEffect.cs`
- `src/Teikem.Api/Controllers/MaintenanceSchedulesController.cs`
- `src/Teikem.Api/Controllers/MaintenanceWorkOrdersController.cs`
- `tests/Teikem.Tests/MaintenanceDueTests.cs`

### P5 — Bitácora de combustible (km/L y costo/km)

- Toca archivos compartidos: no. Depende de: P0.

**FuelEfficiency (puro)**

Compute(IEnumerable<FuelReading(Id, FillDateUtc, OdometerKm?, Liters, TotalCost)>)
- Ordena por fecha e id.
- Para cada carga con odómetro que tenga una anterior con odómetro: DistanceKm = O_i − O_prev, KmPerLiter = Distance / Liters_i y CostPerKm = TotalCost_i / Distance, redondeados a 2 y 4 decimales.
- null si no hay anterior, si falta el odómetro o si la distancia ≤ 0.
- Summary por vehículo = Σ distancia / Σ litros de las cargas con distancia, y el costo/km equivalente.

ValidateOdometer(readings, fillDate, odometer, excludeId) → mensaje si la lectura es menor que la de una carga anterior o mayor que la de una posterior del mismo vehículo. Ejemplo: 'La lectura de odómetro (5,100 km) es menor que la de una carga anterior del mismo vehículo (5,200 km el 2026-09-20).'

**FuelLogService**

(a) ListAsync(FuelLogQuery)
- Paginado: skip/take 1..500, default 100.
- Filtros vehículo, chofer, rango (FromUtc inclusivo, ToUtc exclusivo) e includeInactive.
- La eficiencia se calcula sobre la serie activa COMPLETA de cada vehículo del resultado, no sobre la página.
- Devuelve Summary.

(b) CreateAsync
- Dentro de RunInTransactionAsync, el vehículo se carga con FleetQueries.LockVehicleAsync: serializa las cargas concurrentes del mismo vehículo, así que la validación monótona y la subida del odómetro no compiten.
- Vehículo del tenant activo y no terminal: 409 FleetQueries.VehicleInactiveMessage.
- Chofer opcional del tenant: 404 'Chofer no encontrado.'
- Liters > 0 ('Los litros deben ser mayores que 0.') y DecimalError (10,3).
- TotalCost ≥ 0 ('El costo total no puede ser negativo.') y DecimalError (18,4).
- OdometerKm ≥ 0 y DecimalError (12,1).
- FillDateUtc no futura: 'La fecha de la carga no puede ser futura.'
- Odómetro monótono por fecha: 400.
- Currency por código opcional.
- Vehicle.CurrentOdometerKm = FleetRules.RaiseOdometer(actual, lectura).

(c) UpdateAsync
- Mismas validaciones y bloqueo, excluyendo la propia fila.
- Carga de otro tenant: 404 'Carga de combustible no encontrada.'
- El vehículo no se cambia: 400 'El vehículo de una carga no se cambia; desactívela y registre otra.'

(d) DeactivateAsync: IsActive=0; deja de contar en la eficiencia. El odómetro del vehículo no baja.

**FuelLogsController** [Route('api/v1/fuel-logs')][RequireModule(Catalog)]
- GET '' (fleet.view)
- POST '', PATCH '{id:int}' y POST '{id:int}/deactivate' (fleet.maintenance)

**FuelLogDataSource**
- key FUEL_LOG; EntityTypeCode FUEL_LOG; DateField 'FillDateUtc'.
- Relaciones Vehicle→VEHICLE (VehicleId) y Driver→DRIVER (DriverId).
- Campos: Id, VehicleId, VehicleCode, DriverId, DriverName, FillDateUtc, OdometerKm, Liters, TotalCost (IsMoney), Station, DistanceKm, KmPerLiter, CostPerKm (IsMoney) e IsActive.
- Mismo patrón que TransportOrderDataSource: AsNoTracking, MaxRows, q.Ids y rango.

Archivos:

- `src/Teikem.Domain/Fleet/FuelEfficiency.cs`
- `src/Teikem.Infrastructure/Services/FuelLogService.cs`
- `src/Teikem.Infrastructure/Analytics/FuelLogDataSource.cs`
- `src/Teikem.Api/Controllers/FuelLogsController.cs`
- `tests/Teikem.Tests/FuelEfficiencyTests.cs`

### P6 — Tarifas del chofer (entrega, intento, viaje), política de pago del tenant y resolvedor para Liquidación

- Toca archivos compartidos: no. Depende de: P0.

**DriverPayoutRules (puro)**: el motor que reutilizará el Lote 9.
- PickCurrent<T>(IEnumerable<T> rows, DateOnly asOf) where T: IEffectiveDated: la fila con EffectiveDated.IsCurrentOn. EffectiveTo es exclusivo; las filas de longitud cero nunca son vigentes; ante empate gana la de EffectiveFrom mayor.
- AttemptRate(ratesByLevel, n):
  - si n supera el nivel más alto con tarifa, usa ese nivel (fallback R16);
  - un nivel intermedio sin tarifa o la ausencia total de niveles da 0 con NoRateNote='sin tarifa configurada'.
- Compute(formula, deliveryRate?, attemptRates, attempts[(Number, Delivered)]) → PayoutResult(Total, Lines):
  - DELIVERY_PLUS_ATTEMPTS: la entrega una vez si hubo entrega, más cada intento, incluido el exitoso.
  - DELIVERY_INCLUDES_FIRST: la entrega si hubo entrega, más los intentos desde el 2º.
  - FAILED_REPLACES_DELIVERY: cada fallido paga su tarifa y el exitoso solo la entrega.
  - Una entrega sin tarifa genera una línea de 0 con la nota (R15).
- ValidateAttemptNumber(n, levels), MaxAttemptLevels=20 e IsKnownFormula.

**DriverRateService(db, tenant, lookups, SpecialServiceService specials)**

Reglas generales:
- Todas las filas se alcanzan a través del chofer del tenant (FleetQueries.ResolveDriverAsync + DriverId); nunca por id suelto.
- Un id de tarifa que no pertenece a ese chofer (otro chofer u otro tenant) da 404 'Tarifa no encontrada.'
- Chofer terminal: cualquier escritura da 409 FleetQueries.DriverRetiredMessage.
- Efectivo-fechadas con EffectiveDated: editar = cerrar y abrir. effectiveFrom/effectiveTo anteriores a hoy dan 400 RateService.PastDateMessage.
- Primero se cierra y se guarda, luego se inserta, con SaveGuardedAsync contra UQ_*_Open.
- Rate ≥ 0 ('La tarifa no puede ser negativa.') y FleetRules.DecimalError (18,4) antes de guardar.

(a) GetAsync(driverPublicId, asOf?, includeHistory) → DriverRatesDto. AttemptRates trae una entrada por nivel 1..AttemptLevels (Rate null = sin tarifa) más los niveles superiores con historial.

(b) Entregas
- AddDeliveryRateAsync:
  - serviceType y packageType obligatorios, por código: 400 'Indique el servicio y el tipo de paquete de la tarifa.'; 'Tipo de servicio desconocido: 'X'.' / 'Tipo de paquete desconocido: 'X'.'
  - Fila viva: 409 'El chofer ya tiene una tarifa vigente para {Servicio} + {Paquete}; edite esa tarifa o ciérrela antes de agregar otra.'
- UpdateDeliveryRateAsync:
  - solo Rate y effectiveFrom;
  - serviceType/packageType en Extra: 400 'El servicio y el paquete se fijan al crear la tarifa; quite la fila y cree una nueva.';
  - fila cerrada: 409 'La tarifa ya está cerrada; agregue una nueva si necesita volver a pagarla.'
- CloseDeliveryRateAsync.

(c) Intentos
- SetAttemptRateAsync(attemptNumber): nivel 1..AttemptLevels (400 'El intento {n} no existe; los niveles configurados van de 1 a {N}.'). Crea o versiona.
- CloseAttemptRateAsync.

(d) Viajes
- GetTripTypesAsync() delega en SpecialServiceService.GetTypesAsync(includeInactive:false) (R21).
- AddTripRateAsync:
  - specialServiceTypeId obligatorio;
  - tenant sin tipos activos: 400 'Sin servicios especiales: agréguelos en Clientes y contratos antes de configurar tarifas por viaje.';
  - inexistente: 404 'Tipo de servicio especial no encontrado.';
  - inactivo: 400 'El tipo de servicio especial está inactivo; reactívelo o elija otro.';
  - fila viva: 409 'El chofer ya tiene una tarifa vigente para el tipo de viaje '{tipo}'; edite esa tarifa o ciérrela antes de agregar otra.'
- UpdateTripRateAsync: el tipo en Extra da 400 'El tipo de viaje se fija al crear la tarifa; quite la fila y agregue una con el tipo correcto.'
- CloseTripRateAsync.

**DriverPayPolicyService**
- GetAsync: sin fila devuelve los defaults AttemptLevels=2 y DELIVERY_PLUS_ATTEMPTS, sin escribir.
- UpdateAsync: payoutFormula por código DriverPayoutFormula (400 'Fórmula de pago desconocida: 'X'.').
- AddAttemptLevelAsync: upsert con AttemptLevels+1 hasta 20 (409 'Se alcanzó el máximo de 20 niveles de intento.'). No crea filas de tarifa.
- Preview(PayoutPreviewRequest) → DriverPayoutRules.Compute, sin BD; usa la fórmula vigente si no se indica.

**DriverRateResolver : IDriverRateResolver**
- Consulta las filas del chofer y elige con DriverPayoutRules.PickCurrent en asOf.
- Sin fila: Missing=true, Amount=0, RateId=null.

**DriverRatesRetirementEffect (DriverStatus)**
- Al entrar a TERMINAL cierra con EffectiveDated.CloseNotBefore(hoy) todas las tarifas abiertas del chofer (R17, sin borrar historial).

**Cambio en el Lote 2, SpecialServiceService.DeactivateTypeAsync**: 409 si hay DriverTripRate vigentes de ese tipo: 'El tipo tiene tarifas por viaje vigentes en {n} chofer(es); ciérrelas antes de inactivarlo.'

**Controladores** con [RequireModule(Catalog)].

DriverRatesController [Route('api/v1')]:
- GET 'drivers/{publicId:guid}/rates' (driverpay.view; asOf, includeHistory)
- POST 'drivers/{publicId:guid}/delivery-rates', PATCH '.../delivery-rates/{id:int}' y POST '.../delivery-rates/{id:int}/close'
- PUT '.../attempt-rates/{attemptNumber:int}' y POST '.../attempt-rates/{attemptNumber:int}/close'
- POST '.../trip-rates', PATCH '.../trip-rates/{id:int}' y POST '.../trip-rates/{id:int}/close'
- Todas las escrituras exigen driverpay.manage.
- GET 'driver-trip-types' (driverpay.view)

DriverPayPolicyController [Route('api/v1/driver-pay-policy')]:
- GET '' (driverpay.view)
- PATCH '' (driverpay.manage)
- POST 'attempt-levels' (driverpay.manage)
- POST 'preview' (driverpay.view)

Archivos:

- `src/Teikem.Domain/Fleet/DriverPayoutRules.cs`
- `src/Teikem.Infrastructure/Services/DriverRateService.cs`
- `src/Teikem.Infrastructure/Services/DriverPayPolicyService.cs`
- `src/Teikem.Infrastructure/Services/DriverRateResolver.cs`
- `src/Teikem.Infrastructure/Services/DriverRatesRetirementEffect.cs`
- `src/Teikem.Infrastructure/Services/SpecialServiceService.cs`
- `src/Teikem.Api/Controllers/DriverRatesController.cs`
- `src/Teikem.Api/Controllers/DriverPayPolicyController.cs`
- `tests/Teikem.Tests/DriverPayoutRulesTests.cs`

### P7 — Viajes pagados al chofer (DriverTrip) y entrega especial con chofer (extensión del Lote 3)

- Toca archivos compartidos: no. Depende de: P0.

En ejecución usa IDriverRateResolver (P6) e IFleetAvailabilityService (P3) por sus interfaces de P0; compila en paralelo con los esqueletos. Los cambios de DTO de orden (DriverPublicId, AssignedDriver*) ya los dejó P0 en OrderContracts.cs.

**PipelinePath (puro)**

StepsTo(enabledStages[(Code, SortOrder, Kind, IsInitial)], fromCode, targetCode):
- Devuelve los códigos a recorrer, siempre la siguiente etapa no lateral por SortOrder, igual que StatusService.
- Destino deshabilitado: termina en la última PIPELINE habilitada anterior.
- Vacío si ya está en el destino o después.
- InvalidOperationException si el origen es lateral o terminal.

**DriverTripRules (puro)**
- Freeze(decimal? rate, int? rateId) → (Amount, RateId, RateMissing, Note): sin tarifa da 0, null, true, 'sin tarifa configurada'. Cumple CK_DriverTrip_Rate.
- ValidateTripDate(date, today): 'La fecha del viaje no puede ser futura.'
- ValidateNotes: máximo 500.

**DriverTripService(db, tenant, statuses, IDriverRateResolver rates)**

(a) CreateTrackedAsync(driver, specialServiceTypeId, tripDate, transportOrderId?, notes, DriverRateResolution? preResolved = null)
- Dentro de la transacción del llamador congela con DriverTripRules.Freeze la tarifa preResolved o rates.TripRateAsync(asOf: tripDate).
- Guarda DriverTripRateId.
- Nace OPEN vía TransitionAsync(DriverTripStatus, DRIVER_TRIP, id, null, inicial).

(b) CreateAsync(driverPublicId, DriverTripCreateRequest)
- Tipo obligatorio: 400 'El tipo de viaje es obligatorio.'
- Tipo inexistente o inactivo: mismos mensajes que P6.
- tripDate hoy por defecto; ValidateTripDate.
- Chofer eliminado: 409 DriverRetiredMessage.

(c) ListAsync(driverPublicId, DriverTripListQuery): por defecto solo vigentes (IsActive=1); includeCancelled=true agrega los cancelados.

(d) CancelAsync(driverPublicId, tripPublicId, comment)
- Viaje de otro chofer o tenant: 404 'Viaje no encontrado.'
- TransitionAsync → CANCELLED; SETTLED/CANCELLED son terminales (422).
- Viaje ligado a una orden: 409 'El viaje nace de una entrega especial; cancele la orden o reasigne el chofer.'

(e) CancelTrackedAsync(trip, comment): uso interno; transiciona y hace SaveChanges ANTES de que el llamador inserte otro viaje, para no chocar con UX_DriverTrip_Order.

**DriverTripStatusEffect (DriverTripStatus)**
- Al entrar a CANCELLED pone IsActive=0 en la entidad tracked. Con eso UX_DriverTrip_Order (TransportOrderId WHERE IsActive=1) garantiza en BD un solo viaje vigente por orden.

**SpecialDeliveryDispatchService(db, tenant, statuses, OrderStatusService orderStatuses, DriverTripService trips, IFleetAvailabilityService availability, IDriverRateResolver rates, PermissionService permissions, ModuleService modules, OrderReadService reader)**

(a) PrepareAsync(Guid driverPublicId, int specialServiceTypeId, DateOnly date, ct) → PreparedSpecialDelivery(int DriverId, string DriverCode, string DriverName, DriverRateResolution TripRate). Es un record público de este archivo, en namespace Services, NO en Contracts. Hace, sin escribir nada y ANTES de sacar números:
- modules.EnsureEnabledAsync(CATALOG): 403 module_disabled; el endpoint vive en LTL_GROUND pero usa choferes y tarifas de CATALOG;
- permissions.EnsureAsync(trips.dispatch): 403 'Falta el permiso 'trips.dispatch'.';
- chofer del tenant: 404 'Chofer no encontrado.';
- disponibilidad: 409 'El chofer no está disponible para despacho: {mensajes bloqueantes separados por '; '}.';
- resolución de la tarifa por viaje.

(b) AssignTrackedAsync(order tracked, PreparedSpecialDelivery p, bool overrideCredit, string? comment), paso a paso:
1. La orden debe ser entrega especial: 422 'Solo las entregas especiales se asignan a un chofer desde aquí; las demás órdenes pasan por Sala de despacho.'
2. En la etapa inicial llama a orderStatuses.ConfirmTrackedAsync(order, overrideCredit): 422 credit_exceeded igual que el Lote 3.
3. EnsureAllowedAsync(TRANSPORT_ORDER, estatus, ASSIGN_TRIP).
4. Lateral, terminal o posterior a IN_TRANSIT: 422 'La entrega especial ya llegó a destino o terminó; no se puede asignar ni reasignar el chofer.'
5. Mismo chofer con viaje vigente: 409 'La orden ya está asignada a ese chofer.'
6. Otro chofer: CancelTrackedAsync del anterior ('Reasignada a {código} {nombre}').
7. Avanza con PipelinePath.StepsTo(…, IN_TRANSIT), una TransitionAsync por paso ('Entrega especial asignada a {código} {nombre}').
8. CreateTrackedAsync(tipo = SpecialService.SpecialServiceTypeId de la orden, TripDate = hoy, TransportOrderId, preResolved = p.TripRate).
9. SaveGuardedAsync('La orden ya tiene un viaje vigente; recargue e intente de nuevo.'): dos asignaciones concurrentes chocan en UX_DriverTrip_Order.

(c) AssignAsync(orderPublicId, SpecialDeliveryAssignRequest, OrderScope)
- Antes de la transacción: resolución de la orden y su tipo, más PrepareAsync.
- overrideCredit exige orders.credit_override.
- Luego RunInTransactionAsync con ResolveOrderForWriteAsync, ApplyRowVersion y AssignTrackedAsync.
- Devuelve OrderDetailDto.

**DriverTripOrderEffect (OrderStatus; StatusService perezoso)**
- Cuando la orden pasa a CANCELLED, cancela su DriverTrip OPEN vigente ('Orden {número} cancelada'). DriverTripStatusEffect lo deja IsActive=0.

**Cambios en archivos del Lote 3**

OrderService.CreateAsync:
- La guarda de OrderService.cs:77 pasa a 'if (req.OverrideCredit && !req.ConfirmNow && req.DriverPublicId is null)'. overrideCredit se acepta con confirmNow o con driverPublicId; el EnsureAsync de credit_override se conserva.
- driverPublicId sin isSpecialDelivery: 400 (campo driverPublicId) 'El chofer solo se asigna en una entrega especial.'
- Con chofer, tras resolver el servicio especial y ANTES del bucle de reintentos (sin sacar números), prepared = PrepareAsync(…, special.SpecialServiceTypeId, hoy).
- Dentro de CADA intento del bucle, tras SaveGuardedAsync, AssignTrackedAsync(order, prepared, req.OverrideCredit, null) reemplaza a ConfirmTrackedAsync. El savepoint del reintento revierte también la asignación, y cualquier 4xx revierte todo.
- El importador no cambia.

OrderReadService: llena AssignedDriver* desde el DriverTrip vigente (IsActive=1) de la orden.

**Controladores**

DriverTripsController [Route('api/v1/drivers/{publicId:guid}/trips')][RequireModule(Catalog)]:
- GET '' (driverpay.view; from, to, status, includeCancelled)
- POST '' y POST '{tripPublicId:guid}/cancel' (driverpay.manage)

SpecialDeliveryController [Route('api/v1/orders/{publicId:guid}/driver')][RequireModule(LtlGround)]:
- POST '' (trips.dispatch; cuerpo SpecialDeliveryAssignRequest)

Archivos:

- `src/Teikem.Domain/Orders/PipelinePath.cs`
- `src/Teikem.Domain/Fleet/DriverTripRules.cs`
- `src/Teikem.Infrastructure/Services/DriverTripService.cs`
- `src/Teikem.Infrastructure/Services/SpecialDeliveryDispatchService.cs`
- `src/Teikem.Infrastructure/Services/DriverTripOrderEffect.cs`
- `src/Teikem.Infrastructure/Services/DriverTripStatusEffect.cs`
- `src/Teikem.Infrastructure/Services/OrderService.cs`
- `src/Teikem.Infrastructure/Services/OrderReadService.cs`
- `src/Teikem.Api/Controllers/DriverTripsController.cs`
- `src/Teikem.Api/Controllers/SpecialDeliveryController.cs`
- `tests/Teikem.Tests/PipelinePathTests.cs`
- `tests/Teikem.Tests/DriverTripRulesTests.cs`

### P8 — Fuentes de datos de identidad de flota y resolvers de pertenencia (todos los EntityTypes con permiso de dueño)

- Toca archivos compartidos: no. Depende de: P0.

**FleetDataSources.cs** sigue el patrón de ClientDataSources/TransportOrderDataSource: AsNoTracking, tope ClientDataSourceHelpers.MaxRows, etiquetas con ILookupCache/MultilingualText y StatusMapAsync, respetando q.Ids y el rango.

(1) VehicleDataSource
- key VEHICLE; EntityTypeCode VEHICLE; DateField null; IdField 'Id'; OPERATIONS.
- Campos: Id, PublicId, Code, PlateNumber, VehicleType, VehicleTypeCode, Ownership, OwnershipCode, FuelType, FuelTypeCode, Make, Model, ModelYear, Vin, CurrentOdometerKm, MaxWeightKg, MaxVolumeM3, MaxStops, Status, StatusCode, NextDocumentExpiry (solo documentos no superados, con FleetDocuments.MarkSuperseded), IsActive y CreatedAtUtc.

(2) DriverDataSource
- key DRIVER; EntityTypeCode DRIVER; DateField null.
- Campos: Id, PublicId, Code, FullName, ZoneCode, Area, MaxStopsPerRoute, EffectiveMaxStops, HireDate, Status, StatusCode, LicenseExpiry (no superadas), HasUser, IsActive y CreatedAtUtc.

(3) WorkOrderDataSource
- key WORK_ORDER; EntityTypeCode WORK_ORDER; DateField 'CreatedAtUtc'; relación Vehicle→VEHICLE (VehicleId).
- Campos: Id, PublicId, Number, VehicleId, VehicleCode, MaintenanceType, MaintenanceTypeCode, ScheduleName, Status, StatusCode, ScheduledDate, CompletedDate, OdometerKm, Vendor, LaborCost, PartsCost, TotalCost (IsMoney), IsActive y CreatedAtUtc.

No hay fuentes de tarifas ni de DriverTrip.

**FleetOwnedEntityResolvers.cs**
- VEHICLE, DRIVER, DISPATCH_ZONE, MAINTENANCE_SCHEDULE, WORK_ORDER, FUEL_LOG y DRIVER_TRIP responden con AnyAsync bajo el filtro de tenant.
- ClosedOwnedEntityResolver(string entityTypeCode) siempre devuelve false (404). Se registra para DRIVER_RATE y FLEET_DOCUMENT, así sus rutas de contactos y campos personalizados no quedan abiertas.

**PortalUserOwnedEntityResolver.cs** (hueco del Lote 2)
- PORTAL_USER tiene OwnerRead/WritePermission pero no tenía resolver, y CustomFieldService.cs:193 omite la verificación de pertenencia si no hay resolver.
- Responde con db.PortalUsers.AnyAsync(p => p.PortalUserId == id) bajo el filtro de tenant.

La cobertura la verifican OwnedEntityResolverCoverageTests (P0) y el smoke.

Archivos:

- `src/Teikem.Infrastructure/Analytics/FleetDataSources.cs`
- `src/Teikem.Infrastructure/Services/FleetOwnedEntityResolvers.cs`
- `src/Teikem.Infrastructure/Services/PortalUserOwnedEntityResolver.cs`

### P9 — Cierre: smoke del Lote 4, docs/lote4-decisiones.md, manual funcional 04 y FAQ

- Toca archivos compartidos: no. Depende de: P0, P1, P2, P3, P4, P5, P6, P7, P8.

**scripts/smoke.sh**
- Agrega el bloque 'Lote 4 — Flota, choferes y mantenimiento' antes del paso de sesiones.
- Es re-ejecutable: usa $TS en los códigos.
- Reutiliza datos reales del smoke existente:
  - CLIENT_O_PID;
  - el tipo 'Vagón $TS';
  - ob();
  - T2 (despachador), T3 (otro tenant) y T7 (contacts.manage + orders.view, sin fleet.*; smoke.sh:956);
  - maxStopsPerRouteDefault=30 fijado en smoke.sh:68;
  - un servicio especial NUEVO para el cliente de órdenes, porque SS_O_ID quedó cerrado en smoke.sh:917.
- La concurrencia usa 8 curl en segundo plano con wait.
- Los pasos están en 'smoke'. La cabecera pasa a 'Lotes 1, 2, 3 y 4'.

**docs/lote4-decisiones.md**, formato del Lote 3:
- mapa de lo construido: tablas, código y endpoints con módulo y permiso;
- cómo se probó, con ejecución real: build, test, db-init ×2 con 52 permisos, smoke y enlace de CI;
- decisiones tomadas y a revisar: las de este plan más las de la verificación;
- lo que queda fuera;
- contraste con Diseño/logistica-funcionalidades-maestro.md (módulos 4 y 11A y la bitácora, incluida la línea 598 sobre el flujo de liquidación).

**docs/manual/04-flota-choferes-y-mantenimiento.md**, por funcionalidad:
- qué hace;
- quién puede (permiso y módulo);
- cómo se usa (pantalla y endpoint);
- campos y validaciones con el mensaje exacto y el código HTTP, incluidos los de precisión decimal, odómetro, cierre de OT y vínculo de usuario;
- estatus y transiciones de VehicleStatus, DriverStatus, WorkOrderStatus y DriverTripStatus (de → a, quién, efectos, qué bloquea);
- casos frecuentes:
  - 'eliminar vs. inactivar un chofer'
  - 'por qué el vehículo pasó a En mant.'
  - 'viaje con $0 sin tarifa configurada'
  - 'no me deja asignar el chofer a la entrega especial'
  - 'Por vencer vs. Vencido'
  - 'renové el registro y el viejo ya no aparece'
  - 'no me deja bajar el odómetro'
  - 'no me deja cerrar la OT'
  - 'km/L vacío en la primera carga'

**docs/manual/faq.md**: sección 'Lote 4 — Flota, choferes y mantenimiento' con cada mensaje de error del lote y qué hacer.

**docs/manual/README.md**: capítulo 4 en el índice.

**Verificación**
- dotnet build y dotnet test;
- db-init dos veces sobre BD limpia (52 permisos, idempotente);
- smoke completo en SMOKE OK;
- CI verde.

Archivos:

- `scripts/smoke.sh`
- `docs/lote4-decisiones.md`
- `docs/manual/04-flota-choferes-y-mantenimiento.md`
- `docs/manual/faq.md`
- `docs/manual/README.md`

## Cambios SQL

- Diseño/logistica-db-estructura.sql · CAPA 10 dbo.Vehicle (inline '-- Lote 4'):
- PublicId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() tras VehicleId.
- StatusCodeId pasa a NOT NULL (-- Entity='VehicleStatus').
- Nuevas columnas: CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), CreatedBy INT NULL REFERENCES dbo.AspNetUsers(Id), UpdatedAtUtc DATETIME2 NULL, UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id) y RowVersion ROWVERSION.
- CONSTRAINT UQ_Vehicle_IdTenant UNIQUE (VehicleId, TenantId): destino de las FKs compuestas.
- CONSTRAINT CK_Vehicle_Numbers CHECK ((MaxWeightKg IS NULL OR MaxWeightKg >= 0) AND (MaxVolumeM3 IS NULL OR MaxVolumeM3 >= 0) AND (MaxStops IS NULL OR MaxStops >= 1) AND (CurrentOdometerKm IS NULL OR CurrentOdometerKm >= 0) AND (ModelYear IS NULL OR ModelYear BETWEEN 1900 AND 2100)).
- Se conservan UQ_Vehicle_Code y HomeWarehouseId.
- Diseño/logistica-db-estructura.sql · CAPA 10 dbo.Driver:
- PublicId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID().
- EmployeeCode pasa a NOT NULL (es el 'Código', inmutable).
- StatusCodeId pasa a NOT NULL (-- Entity='DriverStatus').
- CreatedAtUtc/CreatedBy/UpdatedAtUtc/UpdatedBy y RowVersion ROWVERSION.
- CONSTRAINT UQ_Driver_EmployeeCode UNIQUE (TenantId, EmployeeCode).
- CONSTRAINT UQ_Driver_IdTenant UNIQUE (DriverId, TenantId).
- CONSTRAINT CK_Driver_MaxStops CHECK (MaxStopsPerRoute IS NULL OR MaxStopsPerRoute >= 1).
- Tras la tabla: CREATE UNIQUE INDEX UX_Driver_User ON dbo.Driver(TenantId, UserId) WHERE UserId IS NOT NULL.
- Diseño/logistica-db-estructura.sql · Destinos de FKs compuestas en tablas de lotes previos:
- dbo.SpecialServiceType (Lote 2): CONSTRAINT UQ_SpecialServiceType_IdTenant UNIQUE (SpecialServiceTypeId, TenantId).
- dbo.TransportOrder (CAPA 11, Lote 3): CONSTRAINT UQ_TransportOrder_IdTenant UNIQUE (TransportOrderId, TenantId).
- Solo agregan restricciones únicas sobre la PK más TenantId; no cambian datos ni código.
- Diseño/logistica-db-estructura.sql · CAPA 10, tras dbo.Driver, bloque '-- Lote 4: pago a choferes (maestro del módulo 11A; el chofer es el agregado raíz, sin tabla DriverRateAgreement)':
- CREATE TABLE dbo.DriverPayPolicy (TenantId INT NOT NULL PRIMARY KEY REFERENCES dbo.Tenant(TenantId), AttemptLevels INT NOT NULL DEFAULT 2, PayoutFormulaLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId) -- Entity='DriverPayoutFormula', UpdatedAtUtc DATETIME2 NULL, UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id), CONSTRAINT CK_DriverPayPolicy_Levels CHECK (AttemptLevels BETWEEN 1 AND 20)).
- Diseño/logistica-db-estructura.sql · CAPA 10: CREATE TABLE dbo.DriverDeliveryRate.
- Columnas: DriverDeliveryRateId INT IDENTITY(1,1) PRIMARY KEY, TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId), DriverId INT NOT NULL, ServiceTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId) -- Entity='ServiceType', PackageTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId) -- Entity='PackageType', Rate DECIMAL(18,4) NOT NULL, EffectiveFrom DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE), EffectiveTo DATE NULL -- exclusivo, IsActive BIT NOT NULL DEFAULT 1, CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), CreatedBy INT NULL REFERENCES dbo.AspNetUsers(Id).
- CONSTRAINT FK_DriverDeliveryRate_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId).
- CONSTRAINT CK_DriverDeliveryRate_Rate CHECK (Rate >= 0).
- CONSTRAINT CK_DriverDeliveryRate_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom).
- Índices: CREATE UNIQUE INDEX UQ_DriverDeliveryRate_Open ON dbo.DriverDeliveryRate(DriverId, ServiceTypeLookupId, PackageTypeLookupId) WHERE EffectiveTo IS NULL AND IsActive = 1; CREATE INDEX IX_DriverDeliveryRate_Driver ON dbo.DriverDeliveryRate(TenantId, DriverId).
- Diseño/logistica-db-estructura.sql · CAPA 10: CREATE TABLE dbo.DriverAttemptRate.
- Columnas: DriverAttemptRateId INT IDENTITY(1,1) PRIMARY KEY, TenantId, DriverId, AttemptNumber INT NOT NULL, Rate DECIMAL(18,4) NOT NULL, EffectiveFrom, EffectiveTo, IsActive, CreatedAtUtc y CreatedBy, con los mismos tipos y defaults que DriverDeliveryRate.
- CONSTRAINT FK_DriverAttemptRate_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId).
- CONSTRAINT CK_DriverAttemptRate_Number CHECK (AttemptNumber >= 1), CK_DriverAttemptRate_Rate CHECK (Rate >= 0) y CK_DriverAttemptRate_Dates.
- Índices: CREATE UNIQUE INDEX UQ_DriverAttemptRate_Open ON dbo.DriverAttemptRate(DriverId, AttemptNumber) WHERE EffectiveTo IS NULL AND IsActive = 1; CREATE INDEX IX_DriverAttemptRate_Driver ON dbo.DriverAttemptRate(TenantId, DriverId).
- Diseño/logistica-db-estructura.sql · CAPA 10: CREATE TABLE dbo.DriverTripRate.
- Columnas: DriverTripRateId INT IDENTITY(1,1) PRIMARY KEY, TenantId, DriverId, SpecialServiceTypeId INT NOT NULL -- tipo de viaje = catálogo de servicios especiales del tenant (R21), Rate DECIMAL(18,4) NOT NULL, EffectiveFrom, EffectiveTo, IsActive, CreatedAtUtc y CreatedBy.
- CONSTRAINT FK_DriverTripRate_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId).
- CONSTRAINT FK_DriverTripRate_Type FOREIGN KEY (SpecialServiceTypeId, TenantId) REFERENCES dbo.SpecialServiceType(SpecialServiceTypeId, TenantId).
- CONSTRAINT UQ_DriverTripRate_IdTenant UNIQUE (DriverTripRateId, TenantId).
- CK_DriverTripRate_Rate CHECK (Rate >= 0) y CK_DriverTripRate_Dates.
- Índices: CREATE UNIQUE INDEX UQ_DriverTripRate_Open ON dbo.DriverTripRate(DriverId, SpecialServiceTypeId) WHERE EffectiveTo IS NULL AND IsActive = 1; CREATE INDEX IX_DriverTripRate_Driver ON dbo.DriverTripRate(TenantId, DriverId).
- Diseño/logistica-db-estructura.sql · CAPA 13 dbo.VehicleDocument:
- CONSTRAINT CK_VehicleDocument_Dates CHECK (IssuedDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate >= IssuedDate).
- CREATE INDEX IX_VehicleDocument_Vehicle ON dbo.VehicleDocument(VehicleId, DocTypeLookupId): es el grupo de 'vigente por tipo'.
- Se conserva IX_VehicleDocument_Expiry.
- Diseño/logistica-db-estructura.sql · CAPA 13 dbo.MaintenanceSchedule:
- Nueva columna CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME().
- CONSTRAINT CK_MaintSchedule_Target CHECK ((VehicleId IS NULL AND VehicleTypeLookupId IS NOT NULL) OR (VehicleId IS NOT NULL AND VehicleTypeLookupId IS NULL)).
- CONSTRAINT CK_MaintSchedule_Interval CHECK ((IntervalKm IS NULL OR IntervalKm > 0) AND (IntervalDays IS NULL OR IntervalDays > 0) AND (IntervalKm IS NOT NULL OR IntervalDays IS NOT NULL)).
- CREATE INDEX IX_MaintSchedule_Vehicle ON dbo.MaintenanceSchedule(VehicleId) WHERE VehicleId IS NOT NULL.
- Diseño/logistica-db-estructura.sql · CAPA 13 dbo.MaintenanceWorkOrder:
- VehicleId deja el REFERENCES inline y pasa a CONSTRAINT FK_WorkOrder_Vehicle FOREIGN KEY (VehicleId, TenantId) REFERENCES dbo.Vehicle(VehicleId, TenantId).
- Nuevas columnas: CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), CreatedBy INT NULL REFERENCES dbo.AspNetUsers(Id), UpdatedAtUtc DATETIME2 NULL y UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id).
- CONSTRAINT CK_WorkOrder_Costs CHECK ((LaborCost IS NULL OR LaborCost >= 0) AND (PartsCost IS NULL OR PartsCost >= 0) AND (OdometerKm IS NULL OR OdometerKm >= 0)).
- Índices: CREATE INDEX IX_WorkOrder_Vehicle ON dbo.MaintenanceWorkOrder(VehicleId, StatusCodeId) WHERE IsActive = 1; CREATE INDEX IX_WorkOrder_Schedule ON dbo.MaintenanceWorkOrder(MaintenanceScheduleId) WHERE MaintenanceScheduleId IS NOT NULL.
- TotalCost PERSISTED y UQ_WorkOrder_Number sin cambios.
- Diseño/logistica-db-estructura.sql · CAPA 13 dbo.MaintenanceTask:
- Nueva columna IsActive BIT NOT NULL DEFAULT 1. IsCompleted ya existe y se usa para la regla de cierre.
- CONSTRAINT CK_MaintTask_Costs CHECK ((PartCost IS NULL OR PartCost >= 0) AND (LaborCost IS NULL OR LaborCost >= 0)).
- CREATE INDEX IX_MaintenanceTask_WorkOrder ON dbo.MaintenanceTask(WorkOrderId).
- Diseño/logistica-db-estructura.sql · CAPA 13 dbo.FuelLog:
- VehicleId/DriverId dejan el REFERENCES inline y pasan a CONSTRAINT FK_FuelLog_Vehicle FOREIGN KEY (VehicleId, TenantId) REFERENCES dbo.Vehicle(VehicleId, TenantId) y CONSTRAINT FK_FuelLog_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId). Con DriverId NULL la FK no se evalúa.
- Nuevas columnas: IsActive BIT NOT NULL DEFAULT 1, CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), CreatedBy INT NULL REFERENCES dbo.AspNetUsers(Id), UpdatedAtUtc DATETIME2 NULL y UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id).
- CONSTRAINT CK_FuelLog_Amounts CHECK (Liters > 0 AND TotalCost >= 0 AND (OdometerKm IS NULL OR OdometerKm >= 0)).
- Se conserva IX_FuelLog_Vehicle.
- Diseño/logistica-db-estructura.sql · CAPA 13 dbo.DriverLicense y dbo.DriverCertification:
- DriverLicense: CONSTRAINT CK_DriverLicense_Dates CHECK (IssuedDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate >= IssuedDate); CREATE INDEX IX_DriverLicense_Driver ON dbo.DriverLicense(DriverId, LicenseClassLookupId).
- DriverCertification: CONSTRAINT CK_DriverCertification_Dates (mismo CHECK); CREATE INDEX IX_DriverCertification_Expiry ON dbo.DriverCertification(ExpiryDate) WHERE IsActive = 1 (paridad con licencias y documentos de vehículo); CREATE INDEX IX_DriverCertification_Driver ON dbo.DriverCertification(DriverId, CertTypeLookupId).
- Diseño/logistica-db-estructura.sql · CAPA 13, tras dbo.FleetAssignment, bloque '-- Lote 4: viaje pagado al chofer (monto congelado; insumo de la liquidación del Lote 9)'. CREATE TABLE dbo.DriverTrip:
- Columnas: DriverTripId INT IDENTITY(1,1) PRIMARY KEY, PublicId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId), DriverId INT NOT NULL, SpecialServiceTypeId INT NOT NULL, DriverTripRateId INT NULL -- tarifa usada; NULL = sin tarifa, TransportOrderId INT NULL -- entrega especial de origen; NULL = alta manual, TripDate DATE NOT NULL, Amount DECIMAL(18,4) NOT NULL, RateMissing BIT NOT NULL DEFAULT 0, StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId) -- Entity='DriverTripStatus', Notes NVARCHAR(500) NULL, IsActive BIT NOT NULL DEFAULT 1 -- CANCELLED implica 0, CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), CreatedBy, UpdatedAtUtc, UpdatedBy y RowVersion ROWVERSION.
- FKs compuestas: FK_DriverTrip_Driver (DriverId, TenantId) → Driver; FK_DriverTrip_Type (SpecialServiceTypeId, TenantId) → SpecialServiceType; FK_DriverTrip_Rate (DriverTripRateId, TenantId) → DriverTripRate(DriverTripRateId, TenantId); FK_DriverTrip_Order (TransportOrderId, TenantId) → TransportOrder(TransportOrderId, TenantId).
- CONSTRAINT CK_DriverTrip_Amount CHECK (Amount >= 0).
- CONSTRAINT CK_DriverTrip_Rate CHECK ((RateMissing = 1 AND DriverTripRateId IS NULL AND Amount = 0) OR (RateMissing = 0 AND DriverTripRateId IS NOT NULL)).
- Índices:
  - CREATE UNIQUE INDEX UX_DriverTrip_Order ON dbo.DriverTrip(TransportOrderId) WHERE TransportOrderId IS NOT NULL AND IsActive = 1: un solo viaje vigente por orden, garantizado en BD.
  - CREATE INDEX IX_DriverTrip_Driver_Date ON dbo.DriverTrip(TenantId, DriverId, TripDate).
- Diseño/logistica-db-estructura.sql · CAPA 16 dbo.DriverDevice:
- CREATE UNIQUE INDEX UX_DriverDevice_Token ON dbo.DriverDevice(TenantId, PushToken) WHERE PushToken IS NOT NULL AND IsActive = 1.
- Diseño/logistica-db-estructura.sql · dbo.NumberSequence y cierre del script:
- CK_NumberSequence_Kind pasa a CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER')), para el número de OT por tenant con ClientId NULL.
- El PRINT final se actualiza al nuevo total de tablas (+5).
- Diseño/logistica-db-seed.sql · 1) CATALOG DOMAINS:
- Agregar ('DriverPayoutFormula',1,'Fórmula de pago a choferes','Driver payout formula') y ('DriverTripStatus',2,'Estatus de viaje de chofer','Driver trip status').
- VehicleStatus, DriverStatus y WorkOrderStatus ya existen.
- Diseño/logistica-db-seed.sql · 2) LOOKUP CODES (#L), bloque '-- Lote 4':
- DriverPayoutFormula:
  - ('DriverPayoutFormula','DELIVERY_PLUS_ATTEMPTS','Entrega + cada intento','Delivery + each attempt',1)
  - ('DriverPayoutFormula','DELIVERY_INCLUDES_FIRST','La entrega incluye el 1er intento','Delivery includes 1st attempt',2)
  - ('DriverPayoutFormula','FAILED_REPLACES_DELIVERY','Intento fallido reemplaza a entrega','Failed attempt replaces delivery',3)
- EntityType nuevos:
  - ('MAINTENANCE_SCHEDULE','Programa de mantenimiento','Maintenance schedule',61)
  - ('FUEL_LOG','Carga de combustible','Fuel log',62)
  - ('FLEET_DOCUMENT','Documento de flota','Fleet document',63)
  - ('DRIVER_RATE','Tarifa de chofer','Driver rate',64)
  - ('DRIVER_TRIP','Viaje de chofer','Driver trip',65)
  - ('DISPATCH_ZONE','Zona de despacho','Dispatch zone',66)
- NO se agrega MAINTENANCE_WORK_ORDER: se reutiliza 'WORK_ORDER', ya sembrado en la línea 140.
- ('Capability','EDIT_WORK_ORDER','Editar orden de trabajo','Edit work order',7).
- Diseño/logistica-db-seed.sql · 3) STATUS CODES (#S), '-- Lote 4':
- ('DriverTripStatus','OPEN','Por liquidar','Open',@PIPE,1,'#9CA3AF',1)
- ('DriverTripStatus','SETTLED','Liquidado','Settled',@TERM,2,'#059669',0)
- ('DriverTripStatus','CANCELLED','Cancelado','Cancelled',@TERM,3,'#6B7280',0)
- Diseño/logistica-db-seed.sql · nuevo 3D) STATUS CAPABILITY por defecto (TenantId NULL):
- MERGE idempotente igual a 3B, para EntityType WORK_ORDER + Capability EDIT_WORK_ORDER con IsAllowed = 0 en WorkOrderStatus CLOSED y CANCELLED.
- El tenant lo cambia en /status/capabilities/WORK_ORDER.
- Diseño/logistica-db-seed.sql · 4) PERMISOS (#P), '-- Lote 4':
- Nuevos: ('fleet.view','FLEET','Ver flota y choferes','View fleet & drivers'), ('driverpay.view','FLEET','Ver tarifas y viajes de choferes','View driver rates & trips') y ('driverpay.manage','FLEET','Gestionar tarifas y viajes de choferes','Manage driver rates & trips').
- 5) #RP: ('Dispatcher','fleet.view'), ('Billing','driverpay.view') y ('ReadOnly','fleet.view'); TenantAdmin los recibe por el SELECT sobre #P.
- PRINT final: 'capacidades por defecto (CONTRACT, TRANSPORT_ORDER y WORK_ORDER), permisos (52)'.

## Decisiones

1. DECISIÓN: El maestro de tarifas del chofer (entregas, intentos, viajes, niveles y fórmula) y DriverTrip entran en este lote.
- Viven en la pantalla 'Choferes y tarifas' del grupo Catálogo.
- El Lote 3 dejó pendiente el DriverTrip de la entrega especial.
- La corrida de liquidación (DriverSettlementRun), el ledger DeliveryAttempt y la exportación quedan para los Lotes 7 y 9.
   - Alternativa: Lote 4 solo con identidad y flota (lectura literal de R8/línea 287) y todo 11A en el Lote 9, dejando la entrega especial sin chofer hasta entonces.
2. DECISIÓN: No hay tabla DriverRateAgreement: el chofer es el agregado raíz.
- Las tres tablas de tarifa cuelgan de Driver.
- La 'ficha vacía' de ensureDriverRate es la ausencia de filas.
- Así lo sugiere la bitácora (línea 697), aunque el módulo 11A (línea 430) todavía usa el término.
   - Alternativa: Crear DriverRateAgreement 1:1 con Driver como cabecera (vigencia/moneda), con una tabla y un paso más en cada alta.
3. DECISIÓN: Se sigue la entrada más reciente de la bitácora, el rediseño maestro-detalle (líneas 697-707), sobre la anterior de tres tablas planas con filtros (661-669), por la regla R29.
   - Alternativa: Tres pantallas planas de tarifas con filtros de Chofer/Activo, como la entrada anterior.
4. DECISIÓN: Los niveles de intento son un entero por tenant: DriverPayPolicy.AttemptLevels, niveles contiguos 1..N, tope 20.
- '+ Agregar intento' solo sube N; no crea filas de $0 por chofer.
- Un nivel sin fila se ve como 'sin tarifa', distinguible de un $0 capturado a propósito.
- En este lote no se quitan niveles.
   - Alternativa: Tabla de niveles del tenant y filas de $0 provisionadas a todos los choferes al agregar un nivel. Es fiel al mock, pero se pierde la distinción entre '$0 a propósito' y 'no configurado'.
5. DECISIÓN: Fallback de intentos: se usa la tarifa del nivel más alto que el chofer tenga configurado, vigente en la fecha. Un nivel intermedio sin tarifa paga $0 con la nota 'sin tarifa configurada'.
   - Alternativa: Tomar como fallback el nivel N del tenant aunque el chofer no lo tenga configurado (daría $0 más a menudo).
6. DECISIÓN: La fórmula de pago vigente (tres modelos, catálogo DriverPayoutFormula) y los niveles viven en la tabla nueva DriverPayPolicy.
- Una fila por tenant, creada con el primer cambio; el default es 'Entrega + cada intento'.
- El Lote 9 solo invoca DriverPayoutRules.Compute y congela la fórmula en su corrida.
   - Alternativa: Columnas en Tenant (patrón DefaultServiceTypeLookupId, con FK diferida), editadas desde Ajustes de la compañía con admin.tenant.
7. DECISIÓN: Vehicle y Driver reciben PublicId, CreatedAt/By, UpdatedAt/By y RowVersion, y sus rutas usan {publicId:guid}.
- Los hijos (documentos, licencias, certificaciones, tareas, cargas, tarifas, dispositivos) NO reciben PublicId ni TenantId propio.
- Se exponen con id entero SOLO bajo la ruta de su padre, y el servicio verifica que el hijo pertenece a ese padre (404 si no).
   - Alternativa: Agregar PublicId (y TenantId) a cada hija para exponerlas con rutas planas por GUID.
8. DECISIÓN: El 'Código' del chofer es Driver.EmployeeCode (NOT NULL, único por compañía, inmutable). El código del vehículo también es inmutable. Ninguno se libera tras la baja: UNIQUE sin filtro.
   - Alternativa: Código de vehículo editable con validación de duplicados, y/o índices filtrados por IsActive para reutilizar códigos dados de baja.
9. DECISIÓN: 'Área' no es un campo propio: es el nombre de la zona de despacho primaria del chofer (DispatchZone.Name). En el mock, 'área' siempre es el texto de los pueblos de la zona.
   - Alternativa: Columna Driver.AreaText libre, o un catálogo DriverArea editable por tenant.
10. DECISIÓN: Cada chofer tiene una zona primaria (DriverZone.IsPrimary) y el lote trae un CRUD mínimo de DispatchZone (código, nombre, activo).
- Los miembros de zona (CP/municipios) y las zonas secundarias quedan para Despacho.
- El CRUD hace falta porque el seed de zonas demo no siembra nada en una BD limpia.
   - Alternativa: Sin CRUD: sembrar R-01..R-04 desde DemoTenantSeeder y dejar la zona vacía en los demás tenants.
11. DECISIÓN: 'Activo' e 'Eliminar' son dos cosas distintas.
- El checkbox 'Activo' es IsActive reversible: saca al chofer de despacho y conserva todo.
- El estatus terminal INACTIVE es la baja definitiva. 'Eliminar chofer' (DELETE) transiciona a INACTIVE sin borrado físico:
  - cierra hoy sus tarifas abiertas;
  - desactiva sus dispositivos;
  - quita su zona;
  - desvincula el usuario;
  - conserva sus viajes para pagarle lo pendiente.
   - Alternativa: DELETE físico en cascada cuando el chofer no tiene ningún historial, y baja definitiva en los demás casos.
12. DECISIÓN: Tres permisos nuevos, para un total de 52. Así quedan separadas flota y compensación (R8).
- fleet.view: lectura de flota y choferes; para Despachador y Solo lectura.
- driverpay.view: para Facturación.
- driverpay.manage: solo Admin por plantilla.
   - Alternativa: Todo con fleet.manage/fleet.maintenance, como sugería la especificación: quien edita un vehículo vería y cambiaría pagos, y no habría permiso de solo lectura.
13. DECISIÓN: La plantilla de rol Driver no recibe ningún permiso fleet.* ni driverpay.*. Que el chofer registre su combustible u odómetro desde su propio vehículo es de la app (Lote 7), con un permiso propio o por pertenencia.
   - Alternativa: Dar ya a la plantilla Driver fleet.maintenance limitado a su vehículo asignado (requiere FleetAssignment, que queda fuera).
14. DECISIÓN: Todo Flota/Choferes/Tarifas va bajo [RequireModule(Catalog)]: el seed describe CATALOG como 'Clientes y contratos, choferes y tarifas, flota'.
- Asignar chofer a la entrega especial va bajo LTL_GROUND con trips.dispatch, pero el servicio verifica además ModuleService.EnsureEnabledAsync(CATALOG), porque usa choferes y tarifas.
- Con CATALOG apagado responde 403 module_disabled.
   - Alternativa: Un ModuleKeys.Fleet nuevo apagable por tenant y un permiso orders.dispatch; o no verificar CATALOG en la asignación (permitiría usar choferes con el módulo apagado).
15. DECISIÓN: Disponibilidad para despacho.
- Bloquean:
  - chofer o vehículo inactivo;
  - estatus distinto del inicial;
  - chofer sin licencia vigente;
  - documento activo de vehículo vencido;
  - OT en IN_PROGRESS.
- Solo avisan: certificación vencida, documento que vence en 30 días o menos, y vehículo sin documentos.
- El preventivo vencido no se evalúa aquí.
   - Alternativa: Que el preventivo vencido también bloquee, o que un chofer sin licencia registrada solo genere aviso.
16. DECISIÓN: Documento vigente por tipo. Un documento con vencimiento queda 'superado' si el mismo dueño tiene otro activo del mismo tipo (tipo de documento, clase de licencia o tipo de certificación) con vencimiento posterior.
- Uno superado NO bloquea la disponibilidad, NO aparece en 'Documentos por vencer' ni en Análisis, y NO cuenta para el próximo vencimiento.
- Sigue visible en la ficha con IsSuperseded=true.
- Así, al renovar no hay que desactivar el documento viejo a mano.
   - Alternativa: Que todo documento activo cuente, obligando a desactivar el viejo al renovarlo.
17. DECISIÓN: 'Documentos por vencer' une documentos de vehículo, licencias y certificaciones en código, con un solo helper (FleetQueries.LoadFleetDocumentsAsync) que usan el panel, la disponibilidad y la fuente FLEET_DOCUMENT. No hay vista SQL ni tabla DriverDocument unificada; se agrega a DriverCertification el índice de vencimiento que le faltaba.
   - Alternativa: Una vista SQL vFleetDocument (UNION ALL) mapeada como keyless en EF, o unificar licencias y certificaciones en una tabla DriverDocument.
18. DECISIÓN: Solo una OT en IN_PROGRESS inhabilita el vehículo.
- Al iniciar la OT, el efecto pasa el vehículo a MAINTENANCE.
- Al cerrar o cancelar, lo regresa a ACTIVE si no queda otra en proceso, aunque el MAINTENANCE se hubiera puesto a mano.
- OPEN = programada; el vehículo sigue operando.
   - Alternativa: Que una OT correctiva en OPEN también inhabilite, o no cambiar el estatus del vehículo automáticamente.
19. DECISIÓN: Cerrar una OT exige:
- todas sus tareas activas completadas (422 si no);
- la lectura de odómetro, si su programa es por kilometraje o ambos (400 si no), para que la línea base del preventivo no quede vacía.

Una OT sin programa o de programa por tiempo cierra sin odómetro.
   - Alternativa: Cerrar sin exigir tareas completas ni odómetro; el preventivo quedaría 'Sin historial' si falta la lectura.
20. DECISIÓN: Costos de la OT: con tareas son la suma de sus tareas activas; sin tareas se capturan directo en el encabezado. TotalCost lo calcula SQL (columna computada).
   - Alternativa: Encabezado y tareas independientes (tareas solo informativas).
21. DECISIÓN: El número de OT es automático, 'OT-#####' por compañía, con NumberSequence Kind WORKORDER; no se teclea.
   - Alternativa: Número tecleado libre (p. ej. el del proveedor) con validación de unicidad, o patrón configurable por tenant.
22. DECISIÓN: Se reutiliza el EntityType 'WORK_ORDER' ya sembrado (logistica-db-seed.sql:140) para auditoría, historial de estatus, capacidad EDIT_WORK_ORDER, permisos de dueño, fuente de datos e indicador. No se crea MAINTENANCE_WORK_ORDER.
   - Alternativa: Crear MAINTENANCE_WORK_ORDER y reservar WORK_ORDER para un futuro concepto genérico (quedarían dos códigos para lo mismo).
23. DECISIÓN: Umbrales de 'Por vencer':
- Mantenimiento: queda el 10 % o menos del intervalo, en km o en días.
- Documentos: 30 días. Es el parámetro withinDays en el panel y un valor fijo en Análisis.
   - Alternativa: Umbrales fijos (1,000 km / 15 días) o configurables por tenant.
24. DECISIÓN: Un programa por tipo de vehículo se evalúa vehículo por vehículo con su última OT CERRADA ligada al programa; sin ninguna, el estatus es 'Sin historial'. No hay generación automática de OT (no hay job).
   - Alternativa: Expandir cada programa por tipo en programas por vehículo al crearlo, y/o un job diario que genere las OT preventivas.
25. DECISIÓN: km/L y costo/km se calculan al leer, no se guardan.
- km/L = distancia desde la carga anterior con odómetro / litros de la carga actual (tanque lleno).
- costo/km = costo de la carga / distancia.
- El odómetro debe ser monótono por fecha (400 si no).
   - Alternativa: Guardar DistanceKm/KmPerLiter en FuelLog, o calcular solo promedios por período.
26. DECISIÓN: Protección del odómetro del vehículo.
- Solo sube (máximo monotónico) desde combustible y cierre de OT, con bloqueo de fila del vehículo (UPDLOCK, ROWLOCK, filtrado por TenantId).
- La corrección manual puede bajar un error tecleado, pero nunca por debajo de la última lectura registrada en combustible u OT cerrada (400).
   - Alternativa: Odómetro manual libre (solo auditado) y sin bloqueo, aceptando que dos cargas concurrentes puedan dejar un valor menor.
27. DECISIÓN: FuelLog y MaintenanceTask reciben IsActive (y FuelLog columnas de auditoría) para cumplir 'nunca DELETE'. Solo Vehicle, Driver, MaintenanceWorkOrder y DriverTrip tienen ciclo de estatus vía StatusService; programas, tareas, cargas, documentos, licencias, certificaciones, dispositivos, zonas y DriverZone usan solo IsActive.
   - Alternativa: No permitir quitar cargas ni tareas, solo corregirlas; o dar estatus propio a documentos (p. ej. VIGENTE/RENOVADO).
28. DECISIÓN: Auditoría automática en todas las entidades del lote.
- Las hijas se auditan bajo el EntityType de su padre: documentos bajo VEHICLE; licencias, certificaciones, zonas y dispositivos bajo DRIVER; tareas bajo WORK_ORDER.
- PushToken es [SensitiveData]; LastSeenUtc, timestamps y RowVersion son [NotAudited].
   - Alternativa: Auditar solo Vehicle, Driver, OT y tarifas, sin las hijas.
29. DECISIÓN: El 'estatus route' del mock es IN_TRANSIT.
- La entrega especial con chofer se confirma (cotiza y verifica crédito como en el Lote 3).
- Avanza etapa por etapa, CONFIRMED→PICKUP→INBOUND→PLANNED→IN_TRANSIT, saltando las deshabilitadas, con historial y comentario, porque StatusService no permite saltos.
   - Alternativa: Dejarla en PLANNED y que la app del chofer marque la salida, o agregar a StatusService un 'avance de sistema' con un solo registro (cambio transversal).
30. DECISIÓN: La entrega especial con chofer se prepara ANTES de sacar números:
- módulo CATALOG;
- trips.dispatch;
- chofer;
- disponibilidad;
- tarifa.

Así falla rápido sin consumir NumberSequence. La asignación se repite dentro de cada reintento por colisión de número. overrideCredit se acepta con confirmNow o con driverPublicId (cambia la guarda de OrderService.cs:77).
   - Alternativa: Validar el chofer dentro de la transacción (un 409 de disponibilidad dejaría hueco en la numeración si el número ya se sacó).
31. DECISIÓN: La asignación del chofer a la entrega especial se registra en DriverTrip.TransportOrderId; no se crea Trip/TripOrder, que son de Despacho. DriverTrip es la fila de pago, distinta de dbo.Trip. La ficha de la orden expone solo el chofer asignado, nunca el monto.
   - Alternativa: Crear ya un Trip de una sola parada con el chofer.
32. DECISIÓN: La BD garantiza un solo viaje vigente por orden.
- Índice único filtrado UX_DriverTrip_Order (TransportOrderId WHERE IsActive = 1).
- Todo viaje que pasa a CANCELLED queda además IsActive=0; lo hace DriverTripStatusEffect.
- El listado de viajes muestra por defecto solo los vigentes; los cancelados, con includeCancelled.
   - Alternativa: Confiar solo en el servicio (sin índice), manteniendo IsActive=1 en los cancelados.
33. DECISIÓN: Si el chofer no tiene tarifa por viaje para ese tipo, el DriverTrip se crea igual con $0 y RateMissing, para que el vacío quede visible (R15). La coherencia la garantiza el CHECK CK_DriverTrip_Rate: RateMissing ⇔ sin tarifa y monto 0. Se aplica la tarifa vigente en TripDate.
   - Alternativa: Bloquear el alta o la asignación con 409 hasta que se configure la tarifa.
34. DECISIÓN: DriverTrip tiene estatus propio, DriverTripStatus (OPEN → SETTLED | CANCELLED), vía StatusService.
- El Lote 9 lo pasará a SETTLED y lo referenciará desde su línea de liquidación, sin FK desde DriverTrip.
- Cancelar la orden cancela su viaje OPEN.
- El viaje de una orden no se cancela directo (409).
   - Alternativa: Usar solo IsActive y que el Lote 9 agregue una columna de liquidación en DriverTrip.
35. DECISIÓN: No se crean DeliveryAttempt ni DriverSettlementRun/Line.
- CarrierSettlement y SettlementStatus (DRAFT→APPROVED→PAID) quedan intactos.
- La contradicción con el flujo Generada→Revisada→Aprobada→Exportada queda documentada para que la resuelva el Lote 9. Ese flujo lo exigen el módulo 11A (líneas 420/437) y, más tajante y reciente, la bitácora (línea 598).
   - Alternativa: Crear ya DeliveryAttempt (sin nadie que lo escriba) y renombrar CarrierSettlement.
36. DECISIÓN: Quedan fuera de este lote:
- el almacén base (HomeWarehouseId: Warehouse no está mapeado);
- FleetAssignment: sin CRUD, sin CHECK de chofer/vehículo nulos y sin regla de traslape de fechas, que definirá Despacho;
- los adjuntos de documentos (no hay proveedor de archivos);
- las plantillas de exportación ACCT_TEMPLATES de Liquidación/Facturación (Lotes 9/11). DriverTrip ya lleva lo que esa exportación necesitará: chofer, tipo, fecha, monto, orden y estatus.
   - Alternativa: Mapear un Warehouse mínimo y un CRUD de FleetAssignment con CHECK de traslape ya en este lote.
37. DECISIÓN: No hay fuentes de datos de tarifas ni de DriverTrip, porque Análisis solo exige analytics.view y expondría la compensación. Sí se registran VEHICLE, DRIVER, WORK_ORDER, FUEL_LOG y FLEET_DOCUMENT, con el indicador 'Documentos por vencer' en Pulso.
   - Alternativa: Una fuente DRIVER_TRIP visible para todos los usuarios con analytics.view.
38. DECISIÓN: El selector de 'tipo de viaje' usa un endpoint propio, GET /driver-trip-types (driverpay.view), que delega en SpecialServiceService.GetTypesAsync. Además, inactivar un tipo con tarifas por viaje vigentes responde 409 (cambio en el servicio del Lote 2).
   - Alternativa: Que la pantalla llame a /special-service-types y exigir contracts.read a quien administra tarifas.
39. DECISIÓN: Qué admite cada estado:
- Un vehículo inactivo o dado de baja no admite OT ni cargas nuevas (409).
- Un chofer inactivo sí admite tarifas y viajes, porque se puede reactivar.
- Un chofer eliminado solo se consulta.
   - Alternativa: Permitir OT sobre vehículos inactivos (reparar antes de reactivar).
40. DECISIÓN: Driver.UserId se puede vincular ya (único por compañía, UX_Driver_User), pero vincular o desvincular exige además admin.users y un usuario INTERNAL con membresía ACTIVE en el tenant. DriverDevice solo se lista y se desactiva; el registro del dispositivo es de la app del Lote 7.
   - Alternativa: Vincular solo con fleet.manage y membresía (como el plan original), o dejar el vínculo fuera hasta el Lote 7.
41. DECISIÓN: La tarifa por entrega exige servicio y tipo de paquete exactos; no hay comodín de 'cualquier paquete'.
   - Alternativa: PackageType NULL como comodín, igual que en RateComponent del cliente.
42. DECISIÓN: FKs compuestas (Id, TenantId) como segunda barrera multi-tenant en SQL.
- Dónde: las tablas nuevas (tarifas y DriverTrip), FuelLog y MaintenanceWorkOrder.
- Requiere UQ (Id, TenantId) en Vehicle, Driver, SpecialServiceType, TransportOrder y DriverTripRate.
- EF sigue mapeando las relaciones por columna simple.
   - Alternativa: FK de columna simple como el resto del esquema, confiando solo en el filtro global y en los servicios.
43. DECISIÓN: Se cierra un hueco del Lote 2: PORTAL_USER tiene permiso de dueño pero no tenía IOwnedEntityResolver, y CustomFieldService omite la verificación de pertenencia sin resolver.
- Se agrega el resolver.
- Una prueba exige resolver para TODO EntityType con OwnerRead/WritePermission.
   - Alternativa: Dejar PORTAL_USER como está y limitar la prueba de cobertura a los EntityTypes del Lote 4.

## Pruebas unitarias

- tests/Teikem.Tests/FleetCatalogTests.cs (P0): las constantes coinciden con los literales del seed:
- VehicleStatuses, DriverStatuses, WorkOrderStatuses, DriverTripStatuses, MaintenanceTriggers, MaintenanceTypes, DriverPayoutFormulas y FleetDocumentKinds.
- EntityTypes.WorkOrder == 'WORK_ORDER' (sembrado) y los 6 EntityTypes nuevos.
- Capabilities.EditWorkOrder, NumberKinds.WorkOrder y NumberingRules.IsKnownKind('WORKORDER').

Y PermissionCatalog:
- 52 códigos distintos.
- fleet.view, driverpay.view y driverpay.manage en FLEET, con etiquetas es/en.
- fleet.view solo en TenantAdmin, Dispatcher y ReadOnly; driverpay.view solo en TenantAdmin y Billing; driverpay.manage solo en TenantAdmin.
- La plantilla Driver sin fleet.* ni driverpay.*.
- OwnerRead/WritePermission correctos para los 9 EntityTypes del lote.
- tests/Teikem.Tests/FleetContractsTests.cs (P0), por reflexión como OrderContractsTests:
- VehiclePatchRequest sin Code/HomeWarehouseId y DriverPatchRequest sin Code/EmployeeCode; ambos con Extra [JsonExtensionData].
- DriverRateUpdateRequest sin ServiceType/PackageType/SpecialServiceTypeId y con Extra.
- WorkOrderPatchRequest sin Number/VehiclePublicId.
- DriverDeviceDto sin PushToken.
- Ninguna solicitud de tarifa ni de viaje (DriverDeliveryRateCreateRequest, DriverTripRateCreateRequest, DriverRateUpdateRequest, DriverTripCreateRequest) trae DriverId ni DriverPublicId (R31).
- OrderCreateRequest expone DriverPublicId como último parámetro opcional con default null, así el importador no cambia.
- OrderDetailDto trae AssignedDriver* y ninguna propiedad Amount/Rate de chofer.
- Ningún tipo 'Prepared*' (PreparedSpecialDelivery) vive en el namespace Teikem.Infrastructure.Contracts (comparación por nombre, sin referencia de compilación).
- Ningún DTO de flota es entidad EF.
- tests/Teikem.Tests/FleetRulesTests.cs (P0):
- NormalizeCode: recorta, pasa a mayúsculas; vacío → error; mayor que el máximo → error.
- MatchesSearch sin acentos ni mayúsculas: 'diesel' encuentra 'Diésel'; una parte del VIN en minúsculas lo encuentra.
- EffectiveMaxStops(null, 25)=25 y (18, 25)=18.
- ValidateDocumentDates con el mensaje exacto.
- ExpiryState en los bordes: ayer EXPIRED; hoy EXPIRING con 0 días; hoy+30 EXPIRING; hoy+31 OK; null NO_EXPIRY.
- DecimalError: (18,4) 1.23456 → mensaje con '4 decimales'; 1e14 → error; 99999999999999.9999 → null; (10,3) 10000000 → error.
- RaiseOdometer: (null, 5) = 5; (6000, 5500) = 6000.
- FleetDocuments.MarkSuperseded:
  - REGISTRATION vencido + REGISTRATION posterior del mismo vehículo → el viejo superado;
  - tipos distintos → ninguno superado;
  - dueños distintos → ninguno;
  - documento sin vencimiento → nunca superado ni supera;
  - mismo vencimiento → ninguno superado;
  - licencias de clases distintas → independientes.
- tests/Teikem.Tests/TenantIsolationModelTests.cs (P0). Construye TeikemDbContext con UseSqlServer y una cadena ficticia (no se conecta) y un ITenantContext de prueba, e inspecciona db.Model:
(1) Toda entidad Teikem.Domain* con propiedad TenantId tiene query filter, salvo una lista cerrada de excepciones previas, enumerada explícitamente. La prueba falla si aparece una nueva.
(2) En Teikem.Domain.Fleet, las entidades sin TenantId son exactamente {VehicleDocument, DriverLicense, DriverCertification, DriverZone, MaintenanceTask}.
(3) MaintenanceWorkOrder.TotalCost es computada y stored.
(4) RowVersion es token de concurrencia en Vehicle, Driver, MaintenanceWorkOrder y DriverTrip.
(5) Nombres y filtros de los índices únicos coinciden con el SQL: UQ_Vehicle_Code, UQ_Driver_EmployeeCode, UX_Driver_User, UQ_WorkOrder_Number, UQ_DriverDeliveryRate_Open, UQ_DriverAttemptRate_Open, UQ_DriverTripRate_Open, UX_DriverTrip_Order ('[TransportOrderId] IS NOT NULL AND [IsActive] = 1') y UX_DriverDevice_Token.
(6) DriverPayPolicy tiene clave TenantId sin generación de valor.
- tests/Teikem.Tests/OwnedEntityResolverCoverageTests.cs (P0):
- Instancia por reflexión (ActivatorUtilities, con TeikemDbContext InMemory y un ITenantContext de prueba) todas las implementaciones de IOwnedEntityResolver del ensamblado, incluidas las dos instancias del ClosedOwnedEntityResolver.
- Exige que el conjunto de EntityTypeCode cubra todas las llaves de OwnerReadPermission ∪ OwnerWritePermission (incluye PORTAL_USER, hueco del Lote 2).
- Exige que no haya dos resolvers para el mismo código.
- tests/Teikem.Tests/OrderCatalogTests.cs (P0): Permission_catalog_has_49_codes pasa a 52, conservando las aserciones de orders.credit_override.
- tests/Teikem.Tests/VehicleRulesTests.cs (P1):
- NormalizeVin: espacios, mayúsculas; más de 40 → error.
- Capacidades negativas → error.
- MaxStops 0 → error; 1 es válido.
- Odómetro negativo → error.
- ModelYear 1899 y año+2 → error; año+1 es válido.
- ValidateManualOdometer:
  - menor que la última lectura → mensaje exacto con km y fecha;
  - igual → válido;
  - sin lecturas → válido.
- tests/Teikem.Tests/DriverRulesTests.cs (P2): nombre vacío o de más de 150 → mensajes exactos; tope de paradas 0 → error; número de licencia vacío o de más de 60 → error.
- tests/Teikem.Tests/FleetAvailabilityRulesTests.cs (P3):
- Chofer activo con licencia vigente → disponible.
- Sin licencias → NO_VALID_LICENSE bloqueante.
- Todas vencidas → bloqueante con la fecha.
- Una vencida SUPERADA y una vigente → disponible, sin issue de la vencida.
- Certificación vencida → CERT_EXPIRED, solo aviso.
- UNAVAILABLE → DRIVER_STATUS.
- IsActive=0 → DRIVER_INACTIVE.
- Vehículo:
  - con documento vencido no superado → VEHICLE_DOC_EXPIRED;
  - REGISTRATION vencido superado por uno vigente → disponible;
  - OT en proceso → WORK_ORDER_IN_PROGRESS con el número;
  - sin documentos → aviso VEHICLE_NO_DOCUMENTS;
  - documento que vence en 10 días → DOC_EXPIRING, aviso.
- tests/Teikem.Tests/MaintenanceDueTests.cs (P4):
- MILEAGE 5000 con último servicio en 0: odómetro 1000 OK; 4600 DUE_SOON; 5000 OVERDUE; 5200 OVERDUE con KmRemaining -200.
- TIME 30 días: último hace 10 días OK; hace 28 DUE_SOON; hace 40 OVERDUE.
- BOTH toma el peor.
- Sin último servicio → NO_BASELINE; MILEAGE sin odómetro actual → NO_BASELINE.
- NextDueKm y NextDueDate calculados.
- ValidateClose:
  - 2 tareas incompletas → 422 con 'La orden tiene 2 tarea(s) sin completar…';
  - MILEAGE sin odómetro → 400 con el mensaje exacto;
  - TIME sin odómetro → válido;
  - sin programa → válido.
- tests/Teikem.Tests/FuelEfficiencyTests.cs (P5):
- Tres cargas 5200/5600/6000 con 40 L y $60/$60/$64: la primera null; la segunda 400 km, 10 km/L, 0.15/km; la tercera 10 km/L, 0.16/km.
- Entrada desordenada → mismo resultado.
- Carga sin odómetro → null, y la siguiente usa la última con odómetro.
- Distancia ≤ 0 → null.
- Summary = Σ distancia / Σ litros.
- ValidateOdometer: menor que una anterior → mensaje exacto; mayor que una posterior → mensaje; al editar excluye la propia fila.
- tests/Teikem.Tests/DriverPayoutRulesTests.cs (P6):
- Con entrega 4.00, intentos {1: 1.00, 2: 1.50, 3: 2.00} y fallido×3 + entregado:
  - DELIVERY_PLUS_ATTEMPTS = 10.50;
  - DELIVERY_INCLUDES_FIRST = 9.50;
  - FAILED_REPLACES_DELIVERY = 8.50.
- Entrega al 1er intento: 5.00 / 4.00 / 4.00.
- Sin entrega (dos fallidos): 2.50 / 1.50 / 2.50.
- Entrega sin tarifa → línea 0 con 'sin tarifa configurada'.
- Nivel intermedio faltante → 0 con nota; sin niveles → intentos en 0 con nota.
- ValidateAttemptNumber(3, 2) → mensaje exacto; fórmula desconocida → false.
- PickCurrent:
  - EffectiveTo exclusivo: la fila que cierra hoy no es vigente hoy y la nueva sí;
  - fila de longitud cero nunca vigente;
  - sin filas → null;
  - asOf anterior a toda vigencia → null.
- tests/Teikem.Tests/PipelinePathTests.cs (P7), con el pipeline de OrderStatus completo:
- DRAFT→IN_TRANSIT = [CONFIRMED, PICKUP, INBOUND, PLANNED, IN_TRANSIT].
- CONFIRMED→IN_TRANSIT = [PICKUP, INBOUND, PLANNED, IN_TRANSIT].
- PICKUP e INBOUND deshabilitados → [CONFIRMED, PLANNED, IN_TRANSIT].
- IN_TRANSIT deshabilitado → termina en PLANNED.
- Desde IN_TRANSIT o ARRIVED → vacío.
- Desde ON_HOLD o CANCELLED → InvalidOperationException.
- tests/Teikem.Tests/DriverTripRulesTests.cs (P7):
- Freeze(75, 12) → (75, 12, false, null).
- Freeze(null, null) → (0, null, true, 'sin tarifa configurada').
- Freeze(0, 7) → $0 a propósito: RateMissing=false y RateId 7.
- Cada resultado cumple CK_DriverTrip_Rate.
- ValidateTripDate: mañana → 'La fecha del viaje no puede ser futura.'; hoy y ayer → válidas.
- ValidateNotes: 501 caracteres → error.
- Verificación: dotnet build Teikem.sln sin errores ni warnings; dotnet test Teikem.sln con las 261 pruebas previas más las nuevas en verde.

## Pasos de smoke

- **db-init dos veces sobre BD limpia.**
- 52 permisos en el catálogo.
- La segunda corrida es idempotente (0 permisos nuevos propagados).
- Los roles clonados de Despachador y Solo lectura reciben fleet.view; el de Facturación, driverpay.view.
- **Flota (Lote 4): permisos, catálogos, pipelines y capacidades.**
- /me del admin incluye fleet.view, driverpay.view y driverpay.manage.
- GET /api/v1/catalogs/{VehicleType|Ownership|FuelType|VehicleDocType|LicenseClass|CertificationType|MaintenanceTrigger|MaintenanceType|DriverPayoutFormula} traen valores.
- GET /api/v1/status/{VehicleStatus|DriverStatus|WorkOrderStatus|DriverTripStatus} → 200.
- En DriverTripStatus, OPEN es inicial y SETTLED/CANCELLED son terminales.
- GET /api/v1/status/capabilities/WORK_ORDER muestra EDIT_WORK_ORDER denegado en CLOSED y CANCELLED.
- **Vehículos: alta, código único, qbox, código fijo y precisión.**
- POST /api/v1/vehicles V$TS (VAN, OWNED, DIESEL, VIN, odómetro 1000, capacidades) → 200, status ACTIVE e isActive.
- El mismo código → 409 'Ya existe un vehículo con ese código.'
- vehicleType FOO → 400 con errors.vehicleType.
- maxStops 0 → 400.
- maxWeightKg 1.23456 → 400 con 'El valor admite como máximo 3 decimales…'.
- PATCH de la placa → 200; PATCH {code} → 400 'El código del vehículo se fija al crearlo; no se puede cambiar.'
- ?search=<parte del VIN en minúsculas> y ?search=diesel lo encuentran; ?vehicleType=TRUCK no.
- /status/history/VEHICLE/{id} muestra ACTIVE.
- **Vehículos: estatus y baja lógica.**
- status MAINTENANCE → 200; regreso a ACTIVE → 200.
- deactivate → 204: fuera de la lista por defecto, presente con includeInactive; reactivate → 204.
- Un segundo vehículo pasado a INACTIVE queda isActive=false; reactivate → 409 'El vehículo está dado de baja definitiva; no se puede reactivar.'
- **Zonas y choferes: código fijo, zona/área, tope de paradas y usuario.**
- POST /api/v1/dispatch-zones Z$TS 'Toa Baja · Bayamón' → 200; duplicada → 409.
- POST /api/v1/drivers D1$TS con la zona y sin tope → effectiveMaxStops == 30 (el maxStopsPerRouteDefault que fija smoke.sh:68) y area == nombre de la zona.
- Código repetido → 409 'Ya existe un chofer con ese código.'
- PATCH maxStopsPerRoute 18 → effectiveMaxStops 18. PATCH {code} → 400.
- El admin vincula el usuario del despachador → 200.
  - Un rol con fleet.manage sin admin.users que intenta vincular → 403 'Falta el permiso 'admin.users'.'
  - Vincular un usuario de portal del Lote 2 → 400 'Solo un usuario interno se puede vincular a un chofer.'
  - El mismo usuario en otro chofer → 409 'El usuario ya está vinculado a otro chofer.'
- UNAVAILABLE y regreso; deactivate/reactivate → 204.
- Se crean además D2$TS y D3$TS.
- **Documentos: licencias, certificaciones, documentos de vehículo y 'Documentos por vencer'.**
- Datos:
  - V1: INSURANCE que vence en 10 días, REGISTRATION vencido ayer e INSPECTION a 90 días.
  - D1 y D3: CDL_A que vence en 5 días y en 1 año.
  - D2: licencia vencida y HAZMAT vencida.
- Emisión posterior al vencimiento → 400 'La fecha de vencimiento no puede ser anterior a la de emisión.'
- GET /api/v1/fleet/expiring-documents?withinDays=30 trae vencidos y por vencer (no la inspección), ordenados, con expiryState EXPIRED/EXPIRING.
- docType=LICENSE → solo licencias; entity=VEHICLE → solo del vehículo; includeExpired=false → sin vencidos; docType=FOO → 400.
- **Disponibilidad para despacho.** GET /api/v1/fleet/availability:
- D1 disponible con aviso DOC_EXPIRING.
- D2 no disponible con NO_VALID_LICENSE (bloqueante) y CERT_EXPIRED (aviso).
- V1 no disponible por VEHICLE_DOC_EXPIRED.
- Un vehículo sin documentos, disponible con aviso VEHICLE_NO_DOCUMENTS.
- Un chofer en UNAVAILABLE, no disponible (DRIVER_STATUS).
- **Documento vigente por tipo (renovación).**
- Se agrega a V1 un REGISTRATION nuevo que vence en 1 año.
- expiring-documents ya no trae el REGISTRATION vencido.
- GET del vehículo lo muestra con isSuperseded=true.
- availability deja a V1 disponible (solo el aviso DOC_EXPIRING del seguro).
- nextDocumentExpiry de V1 es la del seguro.
- **Contactos, campos personalizados e historial.**
- POST /api/v1/contacts/DRIVER/{id} con un teléfono → 200.
- Definición de campo para VEHICLE y PUT /api/v1/custom-fields/values/VEHICLE/{id} → 200.
- T7 (contacts.manage sin fleet.*): POST /contacts/DRIVER/{id} → 403 'Falta el permiso 'fleet.manage'.'
- Id inexistente → 404 en VEHICLE, DRIVER, DISPATCH_ZONE, MAINTENANCE_SCHEDULE, WORK_ORDER, FUEL_LOG y DRIVER_TRIP.
- DRIVER_RATE y FLEET_DOCUMENT → 404 (resolver cerrado).
- PUT /custom-fields/values/PORTAL_USER/999999 → 404 (resolver nuevo del hueco del Lote 2).
- **Mantenimiento preventivo: Al día / Por vencer / Vencido.** Programa MILEAGE de 5000 km sobre V1, con último servicio en 0 km:
- con odómetro 1000, GET /maintenance-schedules/due → OK;
- PATCH del odómetro a 4600 → DUE_SOON;
- a 5200 → OVERDUE.

Otros casos:
- Programa TIME con último servicio hace 40 días → OVERDUE.
- Programa por tipo VAN sin OT → NO_BASELINE.
- Vehículo y tipo a la vez → 400 'Indique el vehículo o el tipo de vehículo del programa, no ambos.'
- MILEAGE sin intervalo → 400.
- **Órdenes de trabajo: número, tareas, costos, cierre y efecto en el vehículo.**
- POST OT desde el programa MILEAGE → number OT-##### y status OPEN.
- Programa de otro vehículo → 400.
- Dos tareas con costos → laborCost/partsCost son las sumas y totalCost = labor + partes.
- PATCH laborCost con tareas → 400.
- status IN_PROGRESS → el vehículo pasa a MAINTENANCE y availability muestra WORK_ORDER_IN_PROGRESS.
- Cerrar:
  - con una tarea sin completar → 422 'La orden tiene 1 tarea(s) sin completar…';
  - completadas las tareas pero sin odómetro → 400 'Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.';
  - CLOSED con odómetro 5200 → el vehículo vuelve a ACTIVE, el programa queda OK (último servicio 5200) y el odómetro del vehículo es 5200.
- Después del cierre:
  - PATCH sobre la OT cerrada → 422;
  - /status/history/WORK_ORDER/{id} muestra OPEN→IN_PROGRESS→CLOSED;
  - OT sobre un vehículo inactivo → 409.
- **Concurrencia de OT.**
- 8 POST /maintenance-work-orders simultáneos (curl en segundo plano + wait) sobre un vehículo activo.
- Los 8 → 200, sin 409 ni 500.
- Los 8 números son distintos y consecutivos.
- **Bitácora de combustible: km/L, costo/km y odómetro.**
- Cargas en V1 a 5200 (40 L, $60), 5600 (40 L, $60) y 6000 (40 L, $64):
  - la segunda: kmPerLiter 10 y costPerKm 0.15;
  - la tercera: 10 y 0.16;
  - summary kmPerLiter 10.
- Validaciones:
  - una carga de 5500 fechada después de la de 5600 → 400 con el mensaje de odómetro;
  - 0 litros → 400;
  - liters 1.23456 → 400 de precisión;
  - fecha futura → 400.
- CurrentOdometerKm del vehículo = 6000.
- PATCH manual del odómetro a 5000 → 400 'El odómetro no puede ser menor que la última lectura registrada (6000 km el …).'
- Desactivar una carga → 200 y deja de contar.
- **Tarifas por entrega.**
- POST /drivers/{D1}/delivery-rates STANDARD+BOX 3.50 → 200.
- Repetida → 409 con el mensaje exacto.
- rate 1.23456 → 400 de precisión.
- PATCH rate 4.00 → fila nueva y la anterior cerrada (includeHistory trae 2).
- PATCH {packageType} → 400 'El servicio y el paquete se fijan al crear la tarifa; quite la fila y cree una nueva.'
- close → 200; PATCH sobre la fila cerrada → 409.
- Servicio desconocido → 400.
- PATCH /drivers/{D2}/delivery-rates/{id de D1} → 404 'Tarifa no encontrada.'
- **Niveles de intento, fórmula y vista previa.**
- GET /driver-pay-policy → attemptLevels 2 y DELIVERY_PLUS_ATTEMPTS.
- PUT attempt-rates/1 = 1.00 y /2 = 1.50; PUT /3 → 400 'El intento 3 no existe; los niveles configurados van de 1 a 2.'
- POST attempt-levels → 3; GET rates muestra el nivel 3 con rate null; PUT /3 = 2.00.
- POST preview con entrega 4.00, tres fallidos y el 4º entregado → 10.50 / 9.50 / 8.50 según la fórmula.
- Sin tarifa de entrega → línea 0 con 'sin tarifa configurada'.
- PATCH formula FOO → 400.
- **Tarifas por viaje.**
- GET /driver-trip-types incluye 'Vagón $TS'.
- POST /drivers/{D1}/trip-rates con ese tipo a 75 → 200; repetida → 409; PATCH {specialServiceTypeId} → 400.
- En T3 (sin servicios especiales), POST trip-rates → 400 'Sin servicios especiales: agréguelos en Clientes y contratos antes de configurar tarifas por viaje.'
- Inactivar el tipo con la tarifa vigente → 409 'El tipo tiene tarifas por viaje vigentes en 1 chofer(es); ciérrelas antes de inactivarlo.'
- **Viajes del chofer: monto congelado.**
- POST /drivers/{D1}/trips con el tipo → amount 75, OPEN.
- PATCH de la tarifa por viaje a 80 → el viaje existente sigue en 75 y uno nuevo sale en 80.
- Un tipo sin tarifa → amount 0 y rateMissing=true.
- Fecha futura → 400.
- Cancelar el viaje de $0 → CANCELLED e isActive=false; cancelarlo otra vez → 422.
- GET trips?from&to → 2 vigentes; con includeCancelled=true → 3.
- **Entrega especial con chofer (Lote 4 sobre el Lote 3).**

Preparación:
- Se crea un servicio especial NUEVO y vigente del tipo para el cliente de órdenes (SS_O_ID quedó cerrado en smoke.sh:917).
- Se crea una tarifa por viaje de 80 para D3.

Alta con chofer:
- POST /orders especial con driverPublicId=D3 y sin confirmNow → 200, status IN_TRANSIT, assignedDriverName de D3 y quotedAmount congelado.
- /status/history/TRANSPORT_ORDER/{id} muestra DRAFT→CONFIRMED→PICKUP→INBOUND→PLANNED→IN_TRANSIT.
- GET /drivers/{D3}/trips trae el viaje de 80 ligado a la orden.

Rechazos:
- driverPublicId en una orden normal → 400 'El chofer solo se asigna en una entrega especial.'
- overrideCredit=true sin confirmNow ni chofer → 400, como el Lote 3.
- Con D2 → 409 'El chofer no está disponible para despacho: …'. La orden no se crea y el siguiente número de orden no tiene hueco.

Reasignación:
- POST /orders/{id}/driver con D1 → el viaje de D3 queda CANCELLED con isActive=false y hay un único viaje vigente, de D1.
- Con el mismo D1 → 409 'La orden ya está asignada a ese chofer.'
- Sobre una orden normal → 422.
- Usuario sin trips.dispatch → 403.
- Con CATALOG apagado → 403 module_disabled (luego se enciende).

Cancelaciones:
- Cancelar directo el viaje de la orden → 409.
- Cancelar la orden → su viaje OPEN pasa a CANCELLED.
- **Eliminar chofer: baja definitiva sin borrar historial.**
- DELETE /drivers/{D3} → 204.
- GET → status INACTIVE, isActive false, sin zona ni usuario.
- GET rates → sin tarifas vigentes; con includeHistory aparecen cerradas hoy.
- Sus viajes siguen visibles.
- reactivate → 409 'El chofer fue eliminado; no se puede reactivar.'
- POST delivery-rates → 409 'El chofer fue eliminado; sus tarifas y viajes solo se consultan.'
- **RBAC y módulo.**
- Rol con solo fleet.view:
  - GET vehicles/drivers/fleet → 200;
  - POST vehicles → 403 PERMISSION_DENIED;
  - GET /drivers/{id}/rates → 403 'Falta el permiso 'driverpay.view'.'
- Rol con fleet.manage sin fleet.maintenance: POST maintenance-work-orders y fuel-logs → 403.
- Despachador: GET /drivers → 200.
- Sin driverpay.view, /status/history/DRIVER_TRIP → 403.
- Con CATALOG apagado, /api/v1/vehicles → 403 module_disabled; luego se enciende.
- **Aislamiento entre tenants y BOLA por id hijo.**

Desde T3:
- GET vehicle y driver por publicId → 404; listas vacías.
- POST /contacts/DRIVER/{id} → 404.
- expiring-documents y availability sin datos ajenos.
- POST /orders/{orden especial de T3}/driver con un chofer del demo → 404 'Chofer no encontrado.'

T3 crea su propio vehículo con un documento, su chofer con una tarifa y su OT con una tarea. Luego el admin del demo accede a esos ids hijos bajo SUS padres:
- PATCH /vehicles/{V1}/documents/{docT3} → 404;
- PATCH /drivers/{D1}/delivery-rates/{tarifaT3} → 404;
- PATCH /maintenance-work-orders/{OT demo}/tasks/{tareaT3} → 404.

Mismo tenant, otro padre: PATCH /vehicles/{V2}/documents/{doc de V1} → 404.
- **Fuentes de datos, contenido de sistema y auditoría.**
- /analytics/data-sources incluye VEHICLE, DRIVER, WORK_ORDER, FUEL_LOG y FLEET_DOCUMENT, y NO incluye DRIVER_TRIP.
- El preview de FUEL_LOG trae KmPerLiter.
- Pulso muestra 'Documentos por vencer' ≥ 3 y 'Órdenes de trabajo abiertas'.
- Las vistas 'Vehículos' y 'Documentos por vencer' corren; la segunda no trae el REGISTRATION superado.
- /audit/changes para VEHICLE, DRIVER, DRIVER_RATE, DRIVER_TRIP y WORK_ORDER trae al menos un cambio, y ningún diff contiene rowVersion ni pushToken.

## Anexo: especificación extraída

### entidades

- **tabla**: Vehicle; **existeEnSql**: True; **notas**: Diseño/logistica-db-estructura.sql:1091-1106. Cubre Code, Ownership/FuelType/VehicleType (LookupCode), VIN, odómetro, capacidad MaxWeightKg/MaxVolumeM3/MaxStops, HomeWarehouseId, StatusCodeId, IsActive. Falta PublicId/CreatedAtUtc/RowVersion que sí llevan otras entidades del mismo lote (MaintenanceWorkOrder sí los tiene) — inconsistente con el principio #2/#3 de CLAUDE.md; hay que decidir si se agregan en este lote.
- **tabla**: Driver; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1108-1120. FullName, UserId, EmployeeCode, HireDate, HomeWarehouseId, StatusCodeId, MaxStopsPerRoute (tope de paradas), IsActive. No tiene PublicId/RowVersion/CreatedAtUtc (mismo hueco que Vehicle). No tiene columnas 'Zona'/'Área' como campo simple — el modal del mock (bitácora línea 667) pide 'código, nombre, zona, área y tope de paradas', pero en el esquema la 'zona' real es la tabla puente DriverZone→DispatchZone (capa 12, líneas 1341-1366); 'Área' no tiene equivalente en el SQL (vacío).
- **tabla**: VehicleDocument; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1385-1394. Doc vencible de vehículo (DocTypeLookupId, ExpiryDate indexado) — alimenta el panel 'Documentos por vencer' (línea 675).
- **tabla**: DriverLicense; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1451-1459. Doc vencible de chofer (licencia) con ExpiryDate indexado.
- **tabla**: DriverCertification; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1461-1467. Doc vencible de chofer (certificación), sin índice de ExpiryDate a diferencia de DriverLicense/VehicleDocument — inconsistencia menor a revisar. El mock (bitácora línea 675) une 'VEHICLE_DOCS' y 'DRIVER_DOCS' en una sola lista; el SQL no tiene una tabla 'DriverDocument' unificada — la unificación tendría que hacerse en la consulta (UNION de VehicleDocument + DriverLicense + DriverCertification), no en el esquema.
- **tabla**: MaintenanceSchedule; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1396-1407. Por vehículo o por VehicleTypeLookupId, TriggerLookupId (kilometraje/tiempo/ambos), IntervalKm/IntervalDays, LastServiceKm/LastServiceDate — soporta el cálculo de módulo 4 línea 282.
- **tabla**: MaintenanceWorkOrder; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1409-1427. LaborCost/PartsCost/TotalCost (computada), Vendor, OdometerKm, StatusCodeId (WorkOrderStatus: OPEN→IN_PROGRESS→CLOSED/CANCELLED en el seed). Este sí lleva PublicId/IsActive/RowVersion.
- **tabla**: MaintenanceTask; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1429-1435. Tareas de la orden de trabajo con PartCost/LaborCost/IsCompleted.
- **tabla**: FuelLog; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1438-1448. Litros, TotalCost, OdometerKm por vehículo/chofer — el km/L y costo/km (línea 284, 675) se calculan en servicio a partir de lecturas consecutivas, no hay columna computada en SQL.
- **tabla**: FleetAssignment; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1470-1478. Asignación chofer↔vehículo por tipo/fecha; no mencionada explícitamente en las secciones citadas del maestro/bitácora para este lote, pero ya existe en el esquema y es candidata a reutilizarse para 'disponibilidad para despacho' (línea 286).
- **tabla**: DriverZone / DispatchZone; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1341-1366, ya construidas en un lote anterior (capa 12, despacho). No forman parte de la bitácora de este lote pero son el modelo real detrás del campo 'zona' del modal de chofer.
- **tabla**: DriverDevice; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:1711-1719. PushToken, AppVersion, LastSeenUtc, PlatformLookupId — lo que el módulo 8 (línea 341) pide dejar preparado para la app del Lote 7; no incluye idempotency-key/outbox (eso vive en middleware transversal, no en esta tabla).
- **tabla**: SpecialServiceType / SpecialService; **existeEnSql**: True; **notas**: logistica-db-estructura.sql:735-761, construidas en Lote 2 (R19/R20). Ya son efectivo-fechadas (EffectiveFrom/EffectiveTo) y ya exponen GetTypesAsync (SpecialServiceService, Lote 2) — este lote debe reutilizar ese servicio/endpoint para alimentar el selector de 'tipo de viaje' de Choferes y tarifas (línea 439, 607), no crear un catálogo nuevo.
- **tabla**: DriverRateAgreement; **notas**: Nombrada como 'maestro' en el doc (línea 428, 430) pero la bitácora final (línea 697-707, más reciente) rediseña la pantalla a 'maestro-detalle con el chofer como maestro' y nunca vuelve a mencionar una tabla propia — sugiere que en el esquema real el maestro podría ser simplemente Driver, con DriverDeliveryRate/DriverAttemptRate/DriverTripRate como hijas directas de Driver, sin una tabla intermedia DriverRateAgreement. Queda como decisión de diseño abierta, no crear la tabla por el solo nombre del módulo 11A.
- **tabla**: DriverDeliveryRate; **notas**: Tarifa por Servicio+Paquete+monto por chofer (línea 430, 665-667). Debe seguir el mismo patrón efectivo-fechado que RateComponent (líneas 892-919 del SQL): Servicio/Paquete fijos al crear, UNIQUE por (DriverId, ServiceTypeLookupId, PackageTypeLookupId) acotado a fila vigente, EffectiveFrom/EffectiveTo por principio #8 (línea 518, 695).
- **tabla**: DriverAttemptRate; **notas**: Tarifa por número de intento (línea 430, 595, 704). Lista abierta de niveles ('+Agregar intento' añade un nivel a TODOS los choferes a la vez, línea 595) — esto implica que además de la fila (DriverId, AttemptNumber, Rate) el esquema real necesita algún registro de qué niveles existen en el tenant, para poder provisionar la fila $0 a todos los choferes al agregar un nivel nuevo; no está modelado en el SQL. Fallback: intento > nivel máximo usa la tarifa del nivel más alto (línea 430) — regla de servicio, no de esquema.
- **tabla**: DriverTripRate; **notas**: Tarifa por tipo de viaje (etiqueta+monto) por chofer (línea 438-439, 596). La etiqueta debe salir de SpecialServiceType/SpecialService (unión allServiceLabels), no texto libre (línea 439, 607) — necesita FK a SpecialServiceType (o a la etiqueta compartida), no un campo string.
- **tabla**: DriverTrip; **notas**: Viaje real de un chofer que congela el monto al crearse (línea 439, 609) — 'freeze at generation'. Debe poder nacer (a) manual y (b) automáticamente desde la entrega especial de Entrada de órdenes al asignar chofer (línea 608-609): ese caso crea la orden en estatus 'route' sin pasar por Sala de despacho, con el chofer ya asignado, y un DriverTrip cuyo monto = driverTripRateFor(chofer, servicio) vigente en ese momento. Este es justamente lo que este lote (4) debe dejar preparado, pendiente de la decisión 2 del Lote 3.
- **tabla**: DeliveryAttempt; **notas**: Ledger fuente de verdad de intentos de entrega (orden, número, chofer, fecha, resultado, razón de fallo) — línea 432, 597. Nace del evento físico (chofer marca entrega/fallo en la app o Sala de despacho). Es insumo tanto para Liquidación (Lote 9/11A) como, a futuro, para el cargo opcional por intento en Facturación (línea 422, 597) — este lote solo necesita dejar claro que el ledger no existe todavía en SQL, aunque el consumo real (DriverSettlementRun) es Lote 9.
- **tabla**: DriverSettlementRun / DriverSettlementLine; **notas**: El doc nombra estas entidades (módulo 11A, líneas 426, 437, 440-441) pero el SQL solo tiene dbo.CarrierSettlement/dbo.SettlementLine (líneas 1993-2013), con nombres distintos y con StatusCode 'SettlementStatus' seedeado como DRAFT→APPROVED→PAID (seed:334) — no coincide con el flujo Generada→Revisada→Aprobada→Exportada que el doc exige para DriverSettlementRun (línea 420, 437, 598) y que sí tiene BillingRunStatus (seed:333). Esto es contradicción de esquema a resolver en el Lote 9 (la corrida en sí), pero Lote 4 debe tenerlo presente porque el DriverTrip que congela aquí es el insumo directo de esa corrida futura.
- **tabla**: Vehicle; **existeEnSql**: True; **notas**: Línea 1091. PK VehicleId. UQ_Vehicle_Code (TenantId, Code). Code/PlateNumber, capacidades (MaxWeightKg, MaxVolumeM3, MaxStops), FKs a LookupCode VehicleType/Ownership/FuelType, HomeWarehouseId->Warehouse, StatusCodeId->StatusCode (Entity='VehicleStatus'), IsActive. No tiene PublicId ni RowVersion ni CreatedAtUtc (a diferencia de otras entidades del módulo de flota como TransportOrder/Trip).
- **tabla**: VehicleDocument; **existeEnSql**: True; **notas**: Línea 1385. PK VehicleDocumentId, FK VehicleId, DocTypeLookupId (Entity='VehicleDocType'), DocNumber/IssuedDate/ExpiryDate, FileName/StoragePath, IsActive. Índice IX_VehicleDocument_Expiry filtrado IsActive=1 (para vencimientos). No tiene TenantId propio (se infiere vía Vehicle); no tiene PublicId ni auditoría de subida (no UploadedAtUtc/UploadedBy).
- **tabla**: Driver; **existeEnSql**: True; **notas**: Línea 1108. PK DriverId, FullName, UserId->AspNetUsers (nullable), EmployeeCode, HireDate, HomeWarehouseId, StatusCodeId (Entity='DriverStatus'), MaxStopsPerRoute (override de Tenant.MaxStopsPerRouteDefault), IsActive. Sin UNIQUE en EmployeeCode; sin PublicId.
- **tabla**: DriverLicense; **existeEnSql**: True; **notas**: Línea 1451. PK DriverLicenseId, FK DriverId, LicenseClassLookupId (Entity='LicenseClass'), LicenseNumber NOT NULL, IssuedDate/ExpiryDate, IsActive. Índice IX_DriverLicense_Expiry filtrado IsActive=1. Sin UNIQUE de LicenseNumber; un chofer puede tener múltiples licencias activas (no hay restricción de una vigente por clase).
- **tabla**: DriverCertification; **existeEnSql**: True; **notas**: Línea 1461. PK DriverCertificationId, FK DriverId, CertTypeLookupId (Entity='CertificationType'), CertNumber nullable, IssuedDate/ExpiryDate, IsActive. Sin índice de vencimiento (a diferencia de DriverLicense y VehicleDocument, que sí lo tienen); posible inconsistencia/vacío.
- **tabla**: DriverZone; **existeEnSql**: True; **notas**: Línea 1361, capa 12B. PK compuesta (DriverId, DispatchZoneId), FKs a Driver y DispatchZone, IsPrimary BIT default 1. Comentario: 'asignación estándar, no impide reasignar ese día'. Nota: el nombre coincide con lo pedido en la tarea, pero conceptualmente es zona de DESPACHO (DispatchZone), distinta de RateZone (tarifas); el propio SQL lo aclara en el comentario de capa.
- **tabla**: DriverDevice; **existeEnSql**: True; **notas**: Línea 1711, capa 16 (app móvil). PK DriverDeviceId, TenantId, DriverId, PlatformLookupId (Entity='DevicePlatform'), PushToken, AppVersion, LastSeenUtc, IsActive. Sin UNIQUE sobre PushToken ni sobre (DriverId, PlatformLookupId).
- **tabla**: FleetAssignment; **existeEnSql**: True; **notas**: Línea 1470. PK FleetAssignmentId, TenantId, DriverId nullable, VehicleId nullable (ambos nullable: el CHECK no obliga a que al menos uno esté presente), AssignmentTypeLookupId (Entity='AssignmentType'), StartDate NOT NULL, EndDate nullable, IsActive. No hay CHECK que impida DriverId y VehicleId ambos NULL, ni índice único que evite solapamiento de fechas para el mismo chofer o vehículo.
- **tabla**: MaintenanceSchedule; **existeEnSql**: True; **notas**: Línea 1396. PK MaintenanceScheduleId, TenantId, VehicleId nullable, VehicleTypeLookupId nullable (permite programa por vehículo específico o por tipo de vehículo), Name, TriggerLookupId (Entity='MaintenanceTrigger': MILEAGE/TIME/BOTH), IntervalKm/IntervalDays nullable, LastServiceKm/LastServiceDate, IsActive. Sin CHECK que exija VehicleId o VehicleTypeLookupId, ni que IntervalKm/IntervalDays correspondan al TriggerLookupId elegido.
- **tabla**: MaintenanceWorkOrder; **existeEnSql**: True; **notas**: Línea 1409. PK WorkOrderId, PublicId, TenantId, VehicleId NOT NULL, MaintenanceScheduleId nullable, Number NOT NULL, MaintenanceTypeLookupId (Entity='MaintenanceType'), StatusCodeId (Entity='WorkOrderStatus'), OdometerKm/ScheduledDate/CompletedDate, Vendor, LaborCost/PartsCost, TotalCost columna COMPUTADA PERSISTED = ISNULL(LaborCost,0)+ISNULL(PartsCost,0), CurrencyLookupId, Notes, IsActive, RowVersion. UQ_WorkOrder_Number (TenantId, Number).
- **tabla**: MaintenanceTask; **existeEnSql**: True; **notas**: Línea 1429. PK MaintenanceTaskId, FK WorkOrderId, Description, PartCost/LaborCost nullable, IsCompleted BIT default 0. Sin TenantId propio (hereda de WorkOrder); sin índice explícito por WorkOrderId.
- **tabla**: FuelLog; **existeEnSql**: True; **notas**: Línea 1438. PK FuelLogId, TenantId, VehicleId NOT NULL, DriverId nullable, FillDateUtc NOT NULL, OdometerKm nullable, Liters NOT NULL, TotalCost NOT NULL, CurrencyLookupId, Station. Índice IX_FuelLog_Vehicle(VehicleId, FillDateUtc). Sin CHECK de Liters>0/TotalCost>=0 (a diferencia de otras tablas del esquema que sí usan CHECK, p. ej. SpecialService.Rate).
- **tabla**: DriverTrip; **notas**: No existe ninguna tabla con este nombre en Diseño/logistica-db-estructura.sql (verificado con grep exhaustivo). La tabla de viajes existente es dbo.Trip (capa 12, línea 1283), que referencia VehicleId y DriverId directamente (1:1 chofer-viaje por campo, no tabla puente DriverTrip).
- **tabla**: DriverTripRate; **notas**: No existe ninguna tabla con este nombre ni equivalente evidente (no hay tarifa por chofer/viaje) en el SQL. No se encontró en grep sobre logistica-db-estructura.sql.
- **tabla**: TransportOrder; **existeEnSql**: True; **notas**: Línea 1166, capa 11. Muchas columnas propias del Lote 3 (facturación, COD, entrega especial); ver reglas.
- **tabla**: SpecialService; **existeEnSql**: True; **notas**: Línea 745. PK SpecialServiceId, TenantId, ClientId, SpecialServiceTypeId->SpecialServiceType, Rate DECIMAL(18,4) NOT NULL, EffectiveFrom/EffectiveTo, IsActive. CHECK Rate>=0 y CHECK EffectiveTo>=EffectiveFrom. Índice único filtrado UQ_SpecialService_Open (ClientId, SpecialServiceTypeId) WHERE EffectiveTo IS NULL AND IsActive=1: solo una tarifa 'abierta' por cliente/tipo.
- **tabla**: RateZone; **existeEnSql**: True; **notas**: Línea 763. PK RateZoneId, TenantId, Code, Name, IsActive. UQ_RateZone (TenantId, Code). Tabla relacionada RateZoneMember (línea 773) define el detalle de matching (postal/municipio/polígono) vía MatchTypeLookupId (Entity='ZoneMatchType'), reutilizado también por DispatchZoneMember.

### reglas

- **id**: R1; **regla**: Registro de flota completo: tipo, propiedad (propio/arrendado/tercero), combustible, VIN, odómetro actual, almacén base, capacidad (MaxWeightKg/MaxVolumeM3).; **fuente**: logistica-funcionalidades-maestro.md:280; **esRequisitoReal**: True
- **id**: R2; **regla**: Documentos vencibles de vehículo (registro, seguro, inspección) y de chofer (licencia, certificaciones) con ExpiryDate indexado → alertas de vencimiento para el dashboard operacional.; **fuente**: logistica-funcionalidades-maestro.md:281; **esRequisitoReal**: True
- **id**: R3; **regla**: Mantenimiento preventivo por kilometraje o tiempo (MaintenanceSchedule): el sistema compara odómetro/fecha actual contra el intervalo y genera avisos/órdenes.; **fuente**: logistica-funcionalidades-maestro.md:282; **esRequisitoReal**: True
- **id**: R4; **regla**: Órdenes de trabajo preventivas y correctivas con costo de labor/partes (total computado), proveedor, odómetro y tareas; estatus por catálogo.; **fuente**: logistica-funcionalidades-maestro.md:283; **esRequisitoReal**: True
- **id**: R5; **regla**: Bitácora de combustible (FuelLog) con litros, costo y odómetro → cálculo de rendimiento (km/L) y costo por km.; **fuente**: logistica-funcionalidades-maestro.md:284; **esRequisitoReal**: True
- **id**: R6; **regla**: Choferes con código de empleado, fecha de contratación, licencias y certificaciones vencibles; teléfonos/correos en ContactPoint.; **fuente**: logistica-funcionalidades-maestro.md:285; **esRequisitoReal**: True
- **id**: R7; **regla**: Disponibilidad para despacho: el planificador de trips solo ofrece vehículos/choferes activos, con documentos vigentes y sin mantenimiento abierto que los inhabilite.; **fuente**: logistica-funcionalidades-maestro.md:286; **esRequisitoReal**: True
- **id**: R8; **regla**: Lo que se le paga al chofer (DriverRateAgreement/tarifas, corridas de liquidación) no vive en Flota — este módulo es identidad y flota, no compensación (separación de responsabilidad entre módulo 4 y 11A).; **fuente**: logistica-funcionalidades-maestro.md:287; **esRequisitoReal**: True
- **id**: R9; **regla**: Cinco paneles de Flota y mantenimiento: Documentos por vencer (une vehículo y chofer, filtro por Tipo de documento y por Entidad), Vehículos (alta/edición/baja + buscador libre), Mantenimiento preventivo (estatus Al día/Por vencer/Vencido), Órdenes de trabajo, Bitácora de combustible.; **fuente**: logistica-funcionalidades-maestro.md:675; **esRequisitoReal**: True
- **id**: R10; **regla**: CRUD de vehículos con mismo patrón que Choferes: modal alta/edición, código único validado contra duplicados, checkbox Activo para inactivar sin borrar historial.; **fuente**: logistica-funcionalidades-maestro.md:676; **esRequisitoReal**: True
- **id**: R11; **regla**: Buscador de texto libre (qbox) en Vehículos por código, placa, tipo, propiedad, combustible, VIN — patrón replicado en 13 pantallas del producto.; **fuente**: logistica-funcionalidades-maestro.md:646-651,678; **esRequisitoReal**: True
- **id**: R12; **regla**: Filtro de 'Documentos por vencer' debe usar el contenedor estándar .filters (no .qrow.tight) — corrección de estándar visual aplicable a este lote si se construye pantalla propia (no aplica al backend, pero condiciona los parámetros del endpoint de filtrado).; **fuente**: logistica-funcionalidades-maestro.md:712 y 677
- **id**: R13; **regla**: DriverRateAgreement (maestro conceptual): por chofer, una tabla de tarifas por entrega (DriverDeliveryRate: Servicio+Paquete+monto) + una tabla de tarifas por intento (DriverAttemptRate: número de intento→monto); dos maestros independientes del lado del cliente.; **fuente**: logistica-funcionalidades-maestro.md:430; **esRequisitoReal**: True
- **id**: R14; **regla**: Servicio y Paquete quedan fijos una vez creada la fila de DriverDeliveryRate (solo la Tarifa es editable); para cambiar la combinación hay que quitar la fila y crear una nueva; se bloquean duplicados exactos de chofer+servicio+paquete.; **fuente**: logistica-funcionalidades-maestro.md:430,666; **esRequisitoReal**: True
- **id**: R15; **regla**: Si el chofer no tiene tarifa configurada para el Servicio+Paquete real de la orden, la liquidación deja la línea en $0 con la nota 'sin tarifa configurada' en vez de inventar un monto — el vacío queda visible, no disfrazado.; **fuente**: logistica-funcionalidades-maestro.md:430,665; **esRequisitoReal**: True
- **id**: R16; **regla**: El número de niveles de intento es una lista abierta ('+Agregar intento' suma un nivel en vivo a todos los choferes); un intento con número mayor al máximo configurado usa la tarifa del nivel más alto definido (fallback), para que la corrida nunca se rompa.; **fuente**: logistica-funcionalidades-maestro.md:430,595; **esRequisitoReal**: True
- **id**: R17; **regla**: Alta/edición/baja de choferes desde la misma pantalla de tarifas; eliminar un chofer borra en cascada su DriverRateAgreement completo (entregas, intentos, viajes); inactivar (checkbox Activo) es la alternativa que conserva historial. Crear un chofer nuevo provisiona automáticamente una ficha de tarifas vacía (ensureDriverRate).; **fuente**: logistica-funcionalidades-maestro.md:431,667,703; **esRequisitoReal**: True
- **id**: R18; **regla**: DeliveryAttempt (ledger, fuente de verdad): una fila por intento de entrega con orden, número, chofer, fecha, resultado y razón de fallo; nace del evento físico (app o Sala de despacho). Nada se paga fuera de este registro — se reconstruye de él, mismo principio que InventoryTransaction.; **fuente**: logistica-funcionalidades-maestro.md:432,597; **esRequisitoReal**: True
- **id**: R19; **regla**: Fórmula configurable de cómo combina 'pago por entrega' con 'pago por intento' (tres modelos: entrega+cada intento / entrega incluye 1er intento / intento fallido reemplaza entrega); la corrida congela la fórmula usada (DriverSettlementRun.PayoutFormula) — el motor de cálculo pertenece a Lote 9, pero el selector/valor configurado por tenant debe existir para que la corrida lo pueda leer y congelar.; **fuente**: logistica-funcionalidades-maestro.md:433-437,594; **esRequisitoReal**: True
- **id**: R20; **regla**: DriverTrip: pago por viaje (no por paquete/intento), monto plano vía DriverTripRate (lista abierta por chofer: etiqueta+monto). Cada viaje real referencia una de esas tarifas y entra a la liquidación del período igual que las entregas.; **fuente**: logistica-funcionalidades-maestro.md:438,596; **esRequisitoReal**: True
- **id**: R21; **regla**: La etiqueta de DriverTripRate ya no es texto libre: sale del catálogo SpecialService (unión de etiquetas de servicios especiales de todos los clientes, allServiceLabels()); si el tenant no tiene ningún servicio especial configurado, el selector lo dice explícitamente en vez de dejar escribir libre.; **fuente**: logistica-funcionalidades-maestro.md:439,607; **esRequisitoReal**: True
- **id**: R22; **regla**: El tipo de viaje de una fila ya creada de DriverTripRate no es editable (mismo principio que las demás tarifas 'ya creadas'): se muestra como texto fijo; para cambiar el tipo hay que quitar la fila y agregar una con el tipo correcto.; **fuente**: logistica-funcionalidades-maestro.md:607,669; **esRequisitoReal**: True
- **id**: R23; **regla**: Un DriverTrip creado directamente desde Entrada de órdenes ('entrega especial') congela el monto del chofer al momento de crearse, tomando la tarifa vigente de DriverTripRate para ese chofer y esa etiqueta — mismo principio de 'freeze at generation'.; **fuente**: logistica-funcionalidades-maestro.md:439,609; **esRequisitoReal**: True
- **id**: R24; **regla**: Checkbox 'Es una entrega especial' en Entrada de órdenes (después de Consignatario, antes de la sección de paquete): pide Servicio especial (filtrado por el cliente de la sección 01) y Chofer (de los ya existentes), en vez de tipo de paquete/piezas/COD.; **fuente**: logistica-funcionalidades-maestro.md:608; **esRequisitoReal**: True
- **id**: R25; **regla**: Al guardar una entrega especial se crean dos registros: la orden en estatus 'route' de una vez con el chofer ya asignado (no pasa por Sala de despacho) y un DriverTrip nuevo con el monto congelado con driverTripRateFor(chofer, servicio). Este DriverTrip entra a la próxima liquidación de ese chofer igual que uno creado a mano.; **fuente**: logistica-funcionalidades-maestro.md:609; **esRequisitoReal**: True
- **id**: R26; **regla**: Pendiente explícito del Lote 3 (decisión 2): la orden con IsSpecialDelivery/SpecialServiceId ya existe desde Lote 3, pero la asignación de chofer y la creación de DriverTrip quedaron fuera de ese lote y son responsabilidad del Lote 4 (según el encargo de esta tarea) — el propio doc maestro las asigna al módulo 11A/Lote 9 para la corrida, pero la creación del DriverTrip congelado en el momento de guardar la orden es lo que este lote debe dejar preparado.; **fuente**: docs/lote3-decisiones.md:88-89,204 y logistica-funcionalidades-maestro.md:609; **esRequisitoReal**: True
- **id**: R27; **regla**: Toda tarifa de pago a chofer (DriverDeliveryRate/DriverAttemptRate/DriverTripRate) debe ser efectivo-fechada (EffectiveFrom/EffectiveTo) en el sistema real — editar cierra la fila vieja y abre una nueva, no se sobrescribe el monto en el sitio. El mock actual no lo implementa (simplificación de maqueta) — el esquema real sí debe.; **fuente**: logistica-funcionalidades-maestro.md:518,695; **esRequisitoReal**: True
- **id**: R28; **regla**: Choferes y tarifas es maestro-detalle: el chofer es el maestro (lista a la izquierda), ficha editable en línea a la derecha (Nombre, Zona, Área, Tope de paradas, Estado); el modal 'Nuevo chofer' se reserva solo para el alta (Código fijo, único campo inmutable tras crear).; **fuente**: logistica-funcionalidades-maestro.md:699-707 (bitácora más reciente, sustituye la versión de tabla plana de la entrada anterior de la misma bitácora, línea 661-669); **esRequisitoReal**: True
- **id**: R29; **regla**: Contradicción resuelta por bitácora más reciente: la entrada 'Choferes y tarifas — tarifas por entrega...' (línea 661) describía tres tablas planas con filtro de Chofer redundante; la entrada posterior 'rediseño a maestro-detalle' (línea 697) la reemplaza explícitamente — las tarifas por intento y por entrega vuelven a estar juntas en un solo panel del chofer seleccionado, sin filtro de Chofer ni de Activo en la tabla de intentos.; **fuente**: logistica-funcionalidades-maestro.md:697-707, gana sobre 661-669; **esRequisitoReal**: True
- **id**: R30; **regla**: Filtro de Tarifas por viaje (por Tipo de viaje, limitado a los tipos que ese chofer ya tiene configurados) y filtro de Tarifas por entrega (Servicio/Paquete, sin Chofer) usan el contenedor .filters estándar.; **fuente**: logistica-funcionalidades-maestro.md:705-706
- **id**: R31; **regla**: addDeliveryRate()/addTripRate() reciben el código del chofer como parámetro directo (ya no de un <select> del formulario) porque el maestro-detalle ya fija el chofer seleccionado — detalle de implementación de UI, informa que el backend real debe exponer los endpoints ya scopeados por DriverId de la ruta, no recibir DriverId en el body.; **fuente**: logistica-funcionalidades-maestro.md:707
- **id**: R32; **regla**: App móvil (módulo 8): al despachar un trip, el dispositivo descarga ruta/paradas/contactos a SQLite local; DriverDevice guarda el token de push para notificaciones. Este lote solo debe dejar preparado el modelo Driver/DriverDevice (identidad y registro del dispositivo) — la app en sí (offline-first, outbox, idempotencia, navegación, pings GPS) es del Lote 7.; **fuente**: logistica-funcionalidades-maestro.md:333-342; **esRequisitoReal**: True
- **id**: R33; **regla**: Notificaciones push (token en DriverDevice): nuevo trip asignado, cambio de ruta, mensaje del dispatcher — requisito de que DriverDevice exista con PushToken y esté asociado a un Driver activo, aunque el disparo real de la notificación es de otro lote.; **fuente**: logistica-funcionalidades-maestro.md:341; **esRequisitoReal**: True
- **id**: R34; **regla**: Un chofer también puede inactivarse sin eliminarlo para dejar de ofrecerlo en despacho sin perder su historial de tarifas.; **fuente**: logistica-funcionalidades-maestro.md:431; **esRequisitoReal**: True
- **id**: R35; **regla**: Toda tabla debe poder ordenarse por columnas, todo filtro tipo Producto busca por SKU y nombre, todo dropdown multi-selección trae buscador (msel), eliminar/editar con guardas de estatus, ninguna pantalla genera scroll horizontal — convenciones de interfaz transversales, no reglas de negocio propias del módulo pero aplican a cualquier pantalla que este lote construya.; **fuente**: logistica-funcionalidades-maestro.md:520-531
- **id**: R1; **regla**: Vehicle.Code es único por tenant (UQ_Vehicle_Code (TenantId, Code)).; **fuente**: Diseño/logistica-db-estructura.sql:1104; **esRequisitoReal**: True
- **id**: R2; **regla**: MaintenanceWorkOrder.Number es único por tenant (UQ_WorkOrder_Number).; **fuente**: Diseño/logistica-db-estructura.sql:1425; **esRequisitoReal**: True
- **id**: R3; **regla**: MaintenanceWorkOrder.TotalCost es una columna computada y persistida: ISNULL(LaborCost,0)+ISNULL(PartsCost,0). No debe escribirse directamente, se recalcula por SQL Server.; **fuente**: Diseño/logistica-db-estructura.sql:1421; **esRequisitoReal**: True
- **id**: R4; **regla**: Driver.MaxStopsPerRoute, cuando es NULL, usa el default del tenant (Tenant.MaxStopsPerRouteDefault) — comentario explícito en la columna.; **fuente**: Diseño/logistica-db-estructura.sql:1117; **esRequisitoReal**: True
- **id**: R5; **regla**: VehicleDocument e DriverLicense tienen índices filtrados por IsActive=1 sobre ExpiryDate, indicando que el sistema debe poder consultar vencimientos vigentes (documentos y licencias por vencer).; **fuente**: Diseño/logistica-db-estructura.sql:1393,1458; **esRequisitoReal**: True
- **id**: R6; **regla**: DriverZone es la 'asignación estándar' (no exclusiva del día): el comentario aclara que no impide reasignar un chofer a otra zona ese día en Despacho.; **fuente**: Diseño/logistica-db-estructura.sql:1360; **esRequisitoReal**: True
- **id**: R7; **regla**: DispatchZone (zona de despacho, territorio fijo tipo 'R-01') es conceptualmente distinta de RateZone (zona de tarifas); el módulo de flota usa DispatchZone/DriverZone, no RateZone, para asignación de choferes.; **fuente**: Diseño/logistica-db-estructura.sql:1336-1339; **esRequisitoReal**: True
- **id**: R8; **regla**: FleetAssignment.AssignmentTypeLookupId distingue asignaciones PERMANENT/TEMPORARY (según seed), pero el esquema no impone que DriverId y VehicleId no sean ambos NULL simultáneamente ni evita solapamiento de fechas.; **fuente**: Diseño/logistica-db-estructura.sql:1470-1478
- **id**: R9; **regla**: MaintenanceSchedule puede aplicar a un vehículo específico (VehicleId) o a un tipo de vehículo (VehicleTypeLookupId); TriggerLookupId define si el disparo es por kilometraje, tiempo o ambos.; **fuente**: Diseño/logistica-db-estructura.sql:1396-1406 y seed línea 193 (MaintenanceTrigger MILEAGE/TIME/BOTH); **esRequisitoReal**: True
- **id**: R10; **regla**: SpecialService solo permite una fila 'abierta' (EffectiveTo IS NULL) por combinación ClientId+SpecialServiceTypeId mientras esté activa.; **fuente**: Diseño/logistica-db-estructura.sql:759; **esRequisitoReal**: True
- **id**: R11; **regla**: TransportOrder.OrderNumber es único por cliente (no por tenant) y solo entre filas activas; una orden eliminada en captura libera el número. PackBatchNumber es único por tenant entre filas activas.; **fuente**: Diseño/logistica-db-estructura.sql:1205-1208; **esRequisitoReal**: True
- **id**: R12; **regla**: CodTypeLookupId de TransportOrder 'el Lote 3 nunca lo escribe: se define al entregar' — es decir, en el alcance de Flota/Choferes (Lote 4) sí podría tocarse al capturar la prueba de entrega, pero el SQL no define ninguna tabla de Lote 4 que escriba CodType; el POD (ProofOfDelivery) no tiene columnas de COD.; **fuente**: Diseño/logistica-db-estructura.sql:1185
- **id**: R13; **regla**: El estatus de Vehicle usa el dominio VehicleStatus con pipeline ACTIVE(inicial)→MAINTENANCE(lateral)→INACTIVE(terminal); Driver usa DriverStatus con ACTIVE(inicial)→UNAVAILABLE(lateral)→INACTIVE(terminal); MaintenanceWorkOrder usa WorkOrderStatus OPEN(inicial)→IN_PROGRESS→CLOSED(terminal)/CANCELLED(terminal).; **fuente**: Diseño/logistica-db-seed.sql:313,314,318; **esRequisitoReal**: True
- **id**: R14; **regla**: No existe en el seed un dominio de estatus para FleetAssignment, MaintenanceSchedule, MaintenanceTask, FuelLog, DriverLicense, DriverCertification, VehicleDocument, DriverDevice ni DriverZone: todas usan solo el flag IsActive, sin StatusService/EntityStatusHistory.; **fuente**: Diseño/logistica-db-seed.sql (ausencia de StatusCode Entity correspondiente); Diseño/logistica-db-estructura.sql (ninguna de estas tablas tiene StatusCodeId); **esRequisitoReal**: True
- **id**: C1; **regla**: DriverRateAgreement sigue vigente como término normativo en el módulo 11A (línea 430), no solo residual de bitácora vieja; puede seguir siendo el agregado de dominio aunque en UI el maestro visible sea Driver (rediseño línea 697) — no hay contradicción real, solo decisión de esquema abierta, tal como ya lo trató la consolidación.; **fuente**: logistica-funcionalidades-maestro.md:287,430 vs 697-707; **esRequisitoReal**: True
- **id**: C2; **regla**: El requisito de niveles de intento abiertos ('el sistema debe quedar abierto', línea 590; 'no está limitado a dos intentos en el diseño', línea 430) confirma que se necesita alguna entidad de nivel compartido a nivel tenant para poder iterar 'a todos los choferes' al agregar un nivel (línea 595) y provisionar la fila $0 — el SQL no la tiene; vacío correctamente señalado.; **fuente**: logistica-funcionalidades-maestro.md:430,595; **esRequisitoReal**: True
- **id**: C3; **regla**: El doc es más explícito en la bitácora que en el módulo 11A sobre el uso futuro de DeliveryAttempt para cargo por intento en Facturación: 'ese segundo uso queda documentado... como mecanismo listo pero no activado todavía (falta el RateComponent de tipo intento en el contrato del cliente)' — cita adicional que refuerza R18 pero no la contradice.; **fuente**: logistica-funcionalidades-maestro.md:597
- **id**: C4; **regla**: El doc exige que Liquidación a choferes use 'mismo mecanismo de plantillas de exportación configurables que Facturación... alcance liquidacion del mismo catálogo compartido ACCT_TEMPLATES' — no se encontró tabla ni referencia a ACCT_TEMPLATES en el SQL; alcance Lote 9/11 pero condiciona el destino final del DriverTrip que este lote debe dejar preparado.; **fuente**: logistica-funcionalidades-maestro.md:441
- **id**: C5; **regla**: SettlementStatus sembrado (DRAFT→APPROVED→PAID, seed:334) contradice el flujo Generada→Revisada→Aprobada→Exportada exigido tanto en el módulo 11A (línea 428) como, más explícitamente y con fecha más reciente, en la bitácora (línea 598: 'mismo flujo Generada → Revisada → Aprobada → Exportada'). Refuerza el vacío ya señalado con mejor evidencia documental.; **fuente**: logistica-funcionalidades-maestro.md:428,598 vs Diseño/logistica-db-seed.sql:333-334; **esRequisitoReal**: True

### endpoints

- **metodo**: GET; **ruta**: /api/v1/vehicles; **modulo**: CATALOG; **permiso**: fleet.manage; **ruta_nota**: listado con filtros (código, tipo, propiedad, combustible) y qbox de texto libre
- **metodo**: POST; **ruta**: /api/v1/vehicles; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: PATCH; **ruta**: /api/v1/vehicles/{id:int}; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: POST; **ruta**: /api/v1/vehicles/{id:int}/deactivate; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: GET; **ruta**: /api/v1/vehicles/{id:int}/documents; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: POST; **ruta**: /api/v1/vehicles/{id:int}/documents; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: GET; **ruta**: /api/v1/fleet/expiring-documents; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: GET; **ruta**: /api/v1/maintenance-schedules; **modulo**: CATALOG; **permiso**: fleet.maintenance
- **metodo**: POST; **ruta**: /api/v1/maintenance-schedules; **modulo**: CATALOG; **permiso**: fleet.maintenance
- **metodo**: GET; **ruta**: /api/v1/maintenance-work-orders; **modulo**: CATALOG; **permiso**: fleet.maintenance
- **metodo**: POST; **ruta**: /api/v1/maintenance-work-orders; **modulo**: CATALOG; **permiso**: fleet.maintenance
- **metodo**: PATCH; **ruta**: /api/v1/maintenance-work-orders/{id:int}; **modulo**: CATALOG; **permiso**: fleet.maintenance
- **metodo**: GET; **ruta**: /api/v1/fuel-logs; **modulo**: CATALOG; **permiso**: fleet.maintenance
- **metodo**: POST; **ruta**: /api/v1/fuel-logs; **modulo**: CATALOG; **permiso**: fleet.maintenance
- **metodo**: GET; **ruta**: /api/v1/drivers; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: POST; **ruta**: /api/v1/drivers; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: PATCH; **ruta**: /api/v1/drivers/{publicId:guid}; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: DELETE; **ruta**: /api/v1/drivers/{publicId:guid}; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: POST; **ruta**: /api/v1/drivers/{publicId:guid}/deactivate; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: GET; **ruta**: /api/v1/drivers/{publicId:guid}/rates; **modulo**: CATALOG; **permiso**: fleet.manage; **ruta_nota**: ficha completa: delivery+attempt+trip rates del chofer
- **metodo**: POST; **ruta**: /api/v1/drivers/{publicId:guid}/delivery-rates; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: PATCH; **ruta**: /api/v1/drivers/{publicId:guid}/delivery-rates/{id:int}; **modulo**: CATALOG; **permiso**: fleet.manage; **ruta_nota**: solo permite actualizar Rate (efectivo-fechado); Servicio/Paquete inmutables
- **metodo**: POST; **ruta**: /api/v1/drivers/{publicId:guid}/delivery-rates/{id:int}/close; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: POST; **ruta**: /api/v1/drivers/{publicId:guid}/attempt-rates; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: PATCH; **ruta**: /api/v1/drivers/{publicId:guid}/attempt-rates/{attemptNumber:int}; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: POST; **ruta**: /api/v1/tenant/attempt-levels; **modulo**: CATALOG; **permiso**: fleet.manage; **ruta_nota**: añade un nivel de intento nuevo a todos los choferes ('+Agregar intento'); requiere modelar el nivel a nivel de tenant, no solo por chofer
- **metodo**: POST; **ruta**: /api/v1/drivers/{publicId:guid}/trip-rates; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: PATCH; **ruta**: /api/v1/drivers/{publicId:guid}/trip-rates/{id:int}; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: POST; **ruta**: /api/v1/drivers/{publicId:guid}/trip-rates/{id:int}/close; **modulo**: CATALOG; **permiso**: fleet.manage
- **metodo**: GET; **ruta**: /api/v1/special-service-types; **modulo**: CATALOG; **permiso**: contracts.read; **ruta_nota**: YA EXISTE (Lote 2, SpecialServicesController) — reutilizar para alimentar el selector de tipo de viaje, no duplicar
- **metodo**: PATCH; **ruta**: /api/v1/orders/{publicId:guid}/special-delivery; **modulo**: LTL_GROUND; **permiso**: orders.dispatch; **ruta_nota**: extensión de la entrega especial ya construida en Lote 3: asigna Chofer y crea el DriverTrip congelado, mueve la orden a estatus route sin pasar por despacho
- **metodo**: GET; **ruta**: api/v1/contacts/DRIVER/{ownerId}; **permiso**: OwnerReadPermission[DRIVER] (no definido aun); **modulo**: CATALOG
- **metodo**: POST; **ruta**: api/v1/contacts/DRIVER/{ownerId}; **permiso**: OwnerWritePermission[DRIVER] (no definido aun); **modulo**: CATALOG
- **metodo**: GET/POST/PATCH; **ruta**: api/v1/customfields/definitions|values/{entityType}/{entityId}; **permiso**: admin.customfields + OwnerWritePermission[VEHICLE|DRIVER] (no definido aun); **modulo**: CATALOG
- **metodo**: POST; **ruta**: api/v1/{recurso}/{publicId}/status; **permiso**: fleet.manage o fleet.maintenance (a decidir); **modulo**: CATALOG
- **metodo**: *; **ruta**: todas las rutas del modulo 4; **permiso**: fleet.manage / fleet.maintenance (ya sembrados en PermissionCatalog); **modulo**: CATALOG (RequireModule ModuleKeys.Catalog, no existe ModuleKeys.Fleet)

### estatus

- **dominio**: VehicleStatus; **efectos**: ['ACTIVE (inicial, disponible para despacho)', 'MAINTENANCE (excluye al vehículo del planificador de trips por mantenimiento abierto)', 'INACTIVE (terminal, baja lógica, excluye de despacho, no oculta historial)']
- **dominio**: DriverStatus; **efectos**: ['ACTIVE (inicial, disponible para despacho)', 'UNAVAILABLE (excluye del planificador sin ser baja)', 'INACTIVE (terminal, baja lógica, conserva historial de tarifas)']
- **dominio**: WorkOrderStatus; **efectos**: ['OPEN (inicial)', 'IN_PROGRESS', 'CLOSED (terminal)', 'CANCELLED (terminal)']
- **dominio**: DeliveryAttempt.Resultado; **efectos**: ['entregada — alimenta la línea de entrega y cuenta como intento exitoso en la fórmula de pago', 'fallida (con razón por catálogo DeliveryFailureReason) — cuenta como intento en la fórmula, dispara reintento/reprogramación']
- **dominio**: SpecialService (reutilizado, ya Lote 2); **efectos**: ['fila abierta (EffectiveTo NULL) es la vigente para resolver driverTripRateFor()/tarifa del cliente', 'cerrar (EffectiveTo=hoy) conserva historial sin borrar']
- **dominio**: VehicleStatus; **efectos**: ['ACTIVE es el estatus inicial (IsInitial=1, según seed línea 313)', 'MAINTENANCE es lateral (@LAT)', 'INACTIVE es terminal (@TERM)', 'El SQL no define qué IStatusTransitionEffect dispara cada transición; eso es de otra etapa']
- **dominio**: DriverStatus; **efectos**: ['ACTIVE inicial', 'UNAVAILABLE lateral', 'INACTIVE terminal', 'Sin efectos codificados en el SQL']
- **dominio**: WorkOrderStatus; **efectos**: ['OPEN inicial', 'IN_PROGRESS intermedio (pipeline)', 'CLOSED y CANCELLED terminales', 'MaintenanceWorkOrder.CompletedDate y TotalCost sugieren que el cierre (CLOSED) se relaciona con tener CompletedDate, pero el SQL no impone un CHECK que lo obligue']

### vacios

- No existen en el SQL las tablas DriverRateAgreement, DriverDeliveryRate, DriverAttemptRate, DriverTripRate, DriverTrip ni DeliveryAttempt — el módulo 11A y la bitácora las dan por existentes pero este lote debe crearlas desde cero, decidiendo primero si DriverRateAgreement es una tabla real o si el chofer mismo es el agregado raíz (la bitácora más reciente, línea 697, sugiere lo segundo pero nunca lo dice explícitamente a nivel de esquema).
- No hay tabla ni columna para modelar los 'niveles de intento' como concepto de tenant (independiente de cada fila DriverAttemptRate) — el mock añade un nivel 'a todos los choferes a la vez' (línea 430,595), lo que implica un catálogo de niveles compartido que hoy no existe; sin él no está claro cómo provisionar la fila $0 a cada chofer al agregar un nivel.
- No hay dónde persistir la fórmula de pago configurable (PayoutFormula: los 3 modelos) antes de que se congele en una corrida — el doc solo dice que DriverSettlementRun.PayoutFormula la congela (línea 437), pero no dice si el valor 'vigente' vive en Driver, en Tenant, o en una tabla de configuración nueva.
- CarrierSettlement/SettlementLine (SQL) vs. DriverSettlementRun/DriverSettlementLine (doc, módulo 11A) son nombres distintos para lo que parece el mismo concepto, y SettlementStatus (DRAFT→APPROVED→PAID, seed:334) no coincide con el flujo Generada→Revisada→Aprobada→Exportada exigido para DriverSettlementRun (línea 420,437) — contradicción de esquema a resolver antes o durante el Lote 9, pero que condiciona a qué tabla apuntará el DriverTrip que este lote debe dejar listo.
- No existe una tabla 'DriverDocument' unificada — el panel 'Documentos por vencer' del mock une VEHICLE_DOCS y DRIVER_DOCS (línea 675), pero en SQL el lado chofer está repartido en DriverLicense y DriverCertification, dos tablas con forma parecida pero no idéntica (DriverCertification no tiene índice de ExpiryDate). Falta decidir si se consulta como UNION o si conviene una vista.
- El campo 'Área' del chofer, mencionado en el modal del mock (línea 667, 703: 'código, nombre, zona, área y tope de paradas'), no tiene ninguna columna ni catálogo correspondiente en el SQL — ni en Driver ni en DriverZone/DispatchZone. Falta definir qué es 'Área' (¿un lookup nuevo? ¿un campo de texto libre? ¿sinónimo de Zona mal nombrado en el mock?).
- Vehicle y Driver no llevan PublicId/CreatedAtUtc/RowVersion, a diferencia de MaintenanceWorkOrder y del resto de entidades del sistema (principios #2 y #3 de CLAUDE.md) — vacío de consistencia de esquema, no de negocio, pero bloquea exponer estas entidades con el mismo contrato PublicId que usa el resto de la API.
- No hay ningún ModuleKeys específico para Flota/Choferes/Liquidación (existen LtlGround, Cod, WmsLotSerial, CrossDock, RentalEquipment, RentalBilling, Maritime, ClientPortal, CustomFields, Purchasing, Catalog, Analytics, System) — falta decidir si estas pantallas van bajo ModuleKeys.Catalog (grupo de menú 'Catálogo' donde vive la pantalla, línea 428) o si no requieren [RequireModule] por ser funcionalidad core siempre encendida.
- No existe permiso específico para administrar tarifas de chofer (solo fleet.manage/fleet.maintenance, orientados a Vehículos/Mantenimiento) — falta decidir el nombre del permiso para el CRUD de Choferes y tarifas (¿fleet.manage sirve para ambos, o hace falta uno separado tipo 'driver.rate.manage'?).
- El doc no especifica el mecanismo exacto por el cual el planificador de trips excluye vehículos/choferes 'con mantenimiento abierto que los inhabilite' (línea 286) — no dice qué tipos de MaintenanceWorkOrder/estatus bloquean (¿cualquier OPEN/IN_PROGRESS, o solo cierto MaintenanceTypeLookupId?).
- Contradicción de bitácora ya resuelta pero que conviene dejar anotada explícitamente: la entrada 'tarifas por entrega con servicio+paquete, CRUD de choferes, filtros' (línea 661-669) describe tres tablas planas con filtros de Chofer/Activo; la entrada posterior 'rediseño a maestro-detalle' (línea 697-707) la reemplaza — gana esta última por ser más reciente (regla R29).
- DriverTrip: no existe como tabla en logistica-db-estructura.sql. La tarea del Lote 4 la nombra explícitamente pero el SQL no la define; la relación chofer-viaje vive únicamente en dbo.Trip.DriverId (columna, no tabla puente). No hay decisión documentada en el SQL sobre por qué se omitió o si Trip la reemplaza.
- DriverTripRate: no existe como tabla ni columna equivalente en el SQL (no hay tarifa por viaje/chofer en ninguna tabla de la capa 10, 12 ni 13). Vacío total: ni el nombre ni un concepto sustituto aparecen.
- VehicleDocument no tiene TenantId propio ni PublicId; si el módulo requiere exponer documentos de vehículo por API pública, falta el campo de exposición externa (PublicId) que sí tienen otras entidades del esquema (Vehicle tampoco tiene PublicId, a diferencia de TransportOrder, Trip, MaintenanceWorkOrder).
- DriverCertification no tiene índice de vencimiento (IX_..._Expiry) pese a tener ExpiryDate e IsActive, a diferencia de DriverLicense y VehicleDocument que sí lo tienen; posible omisión de paridad en el esquema.
- FleetAssignment no tiene CHECK que impida DriverId y VehicleId ambos NULL simultáneamente, ni restricción de traslape de fechas (StartDate/EndDate) para el mismo chofer o vehículo: el propio SQL no resuelve qué pasa si dos asignaciones activas coinciden en fecha.
- Ninguna tabla de este lote (Vehicle, Driver, FleetAssignment, MaintenanceSchedule, MaintenanceTask, FuelLog, DriverLicense, DriverCertification, VehicleDocument, DriverDevice, DriverZone) tiene AuditEntity marcado en el SQL en sí (eso se define en código); el SQL no dice cuáles de estas requieren auditoría automática ni cuáles campos son [SensitiveData].
- El SQL no incluye StatusCode para FleetAssignment, MaintenanceSchedule, MaintenanceTask, FuelLog, VehicleDocument, DriverLicense, DriverCertification, DriverDevice ni DriverZone: solo tienen IsActive. Falta decisión sobre si alguna de estas necesita un ciclo de estatus real vía StatusService o si el IsActive basta.
- No se pudo consultar el contenido de Diseño/logistica-funcionalidades-maestro.md como la tarea instruyó leer 'SOLO el SQL'; por tanto no se contrastan aquí las reglas del documento maestro con lo hallado en el esquema. Cualquier regla de negocio narrativa (ej. reglas de vencimiento de licencias, bloqueo de despacho por documento vencido, cálculo de tarifa por chofer) queda fuera de este análisis por instrucción explícita de la tarea.
- PermissionCatalog trae fleet.manage y fleet.maintenance (categoria FLEET) pero no fleet.read/view; falta decidir si listar/ver flota exige fleet.manage o si falta un permiso de solo lectura.
- RoleTemplates['Driver'] solo tiene orders.view y cod.collect, ningun permiso fleet.*; falta decidir permisos del chofer sobre su propio vehiculo/odometro/combustible.
- No existen DriverOwnedEntityResolver ni VehicleOwnedEntityResolver en DependencyInjection.cs (solo User, Client, ClientContact, Location, Contract, TransportOrder, OrderStop, OrderCod, ImportBatch, ImportTemplate); sin ellos ContactPointService y CustomFieldService no pueden validar dueno DRIVER/VEHICLE.
- OwnerReadPermission y OwnerWritePermission no tienen entradas para EntityTypes.Driver ni EntityTypes.Vehicle; sin ellas el acceso cae a 'cualquier autenticado' segun el propio comentario del archivo.
- dbo.Vehicle y dbo.Driver (logistica-db-estructura.sql, capa 10) no tienen columna PublicId, a diferencia de las demas entidades expuestas externamente (Client, Contract, ImportTemplate); rompe la convencion PK INT IDENTITY + PublicId si se reutiliza el patron de controlador con rutas {publicId:guid}.
- StatusDomains (CatalogDomains.cs) no tiene constantes VehicleStatus/DriverStatus/WorkOrderStatus aunque esos StatusCode ya estan sembrados en logistica-db-seed.sql (lineas 313,314,318).
- El documento maestro (linea 278) remite a la bitacora del mock (UI) para el detalle de que se implemento, pero no define contrato de API/DTOs para el backend real; deben derivarse de los 5 paneles descritos (Documentos por vencer, Vehiculos, Mantenimiento preventivo, Ordenes de trabajo, Bitacora de combustible).
- No hay servicio existente que module 'disponibilidad para despacho' (linea 286: vehiculos/choferes activos, documentos vigentes, sin mantenimiento abierto) para que el planificador de trips los filtre; es regla real sin implementacion reutilizable ya construida.
- Falta en la consolidación citar explícitamente la bitácora (línea 598), más reciente y más tajante que el módulo 11A narrativo (línea 428), para el vacío de SettlementStatus vs. el flujo Generada→Revisada→Aprobada→Exportada exigido para Liquidación a choferes.
- No se encontró en el esquema ninguna tabla ni referencia a 'ACCT_TEMPLATES' (plantillas de exportación compartidas entre Facturación y Liquidación, alcance 'liquidacion', logistica-funcionalidades-maestro.md:441) — vacío adicional no listado explícitamente en la consolidación entregada; de alcance Lote 9/11, pero Lote 4 debe tenerlo presente porque el DriverTrip que congela aquí es insumo directo de esa exportación futura.
- Confirmado por grep: DriverRateAgreement no existe como CREATE TABLE en el SQL, y el doc normativo (módulo 11A, línea 430) sigue usando el término activamente hoy (no es residuo de bitácora vieja) — la decisión de si es tabla propia o si Driver es el agregado raíz sigue abierta, tal como ya señalaba la consolidación.
- Confirmado por grep en PermissionCatalog.cs: RoleTemplates['Driver'] solo trae OrdersView y CodCollect (línea 169), ningún permiso fleet.* — coincide con el vacío ya señalado sobre permisos del propio chofer sobre vehículo/odómetro/combustible.
- Todas las citas de número de línea de SQL revisadas contra el archivo (Vehicle:1091, Driver:1108, VehicleDocument:1385, DriverLicense:1451, DriverCertification:1461, MaintenanceSchedule:1396, MaintenanceWorkOrder:1409, MaintenanceTask:1429, FuelLog:1438, FleetAssignment:1470, DriverZone:1361, DriverDevice:1711, CarrierSettlement:1993, SettlementLine:2005, SpecialService:745, RateZone:763) coinciden exactamente; no se detectaron errores de cita de línea en la especificación consolidada.

### reutilizar

- src/Teikem.Infrastructure/Services/SpecialServiceService.cs y src/Teikem.Api/Controllers/SpecialServicesController.cs (Lote 2) — GetTypesAsync ya expone la unión de etiquetas de servicios especiales que este lote debe usar para 'tipo de viaje', no reimplementar
- src/Teikem.Domain/Clients/SpecialServiceRules.cs — patrón de normalización de nombre (NormalizeName/SameName) reutilizable para cualquier '+ Nuevo tipo' análogo si aplicara
- Diseño/logistica-db-estructura.sql:892-936 (RateComponent/RateTier) — patrón de tabla efectivo-fechada con UNIQUE índice filtrado por fila vigente; DriverDeliveryRate/DriverAttemptRate/DriverTripRate deben calcar esta estructura
- StatusService.TransitionAsync / EnsureAllowedAsync — para WorkOrderStatus y para el eventual estatus de DriverSettlementRun (Lote 9)
- IOwnedEntityResolver — si ContactPoint se usa para teléfonos/correos de chofer (línea 285) el mismo resolver polimórfico ya existente debe extenderse a 'Driver' como entidad dueña
- ILookupCache — todos los catálogos de este módulo (VehicleType, Ownership, FuelType, MaintenanceTrigger, MaintenanceType, LicenseClass, CertificationType, VehicleDocType, DevicePlatform) ya están sembrados en Diseño/logistica-db-seed.sql:73-83,189-205 — no crear catálogos nuevos, solo resolverlos
- PermissionCatalog: FleetManage ('fleet.manage') y FleetMaintenance ('fleet.maintenance') ya existen (src/Teikem.Domain/Constants/PermissionCatalog.cs:31-32) — reutilizar en vez de inventar permisos nuevos para Vehículos/Mantenimiento; falta un permiso equivalente para el CRUD de choferes y tarifas (no existe 'driver.rate.manage' ni similar)
- Patrón de pantalla maestro-detalle de 'Clientes y contratos' (línea 699, 727) — mismo patrón visual/funcional a replicar en el backend de 'Choferes y tarifas' (lista+ficha, edición en línea sin modal salvo alta)
- dbo.LookupCode / ILookupCache para VehicleType, Ownership, FuelType, VehicleDocType, MaintenanceTrigger, MaintenanceType, LicenseClass, CertificationType, AssignmentType, DevicePlatform (todos ya sembrados en logistica-db-seed.sql líneas 73-80, 189-197, 205).
- dbo.StatusCode / StatusService.TransitionAsync para VehicleStatus, DriverStatus y WorkOrderStatus (dominios ya sembrados con pipeline/lateral/terminal en logistica-db-seed.sql líneas 313,314,318).
- dbo.RateZoneMember / MatchTypeLookupId (Entity='ZoneMatchType') como patrón ya existente que DispatchZoneMember reutiliza literalmente para el mismo tipo de coincidencia geográfica (postal/municipio/polígono).
- Patrón RefEntity/RefId (RefEntityLookupId + RefId) ya usado en InventoryTransaction, WarehouseTask y TransportOrder.SourceEntityTypeLookupId/SourceEntityId para orígenes polimórficos, aplicable si Flota necesita referencias similares.
- dbo.AspNetUsers como FK de Driver.UserId (vínculo chofer-usuario) y de CreatedBy/UpdatedBy en otras tablas del esquema.
- StatusService (src/Teikem.Infrastructure/Services/StatusService.cs): TransitionAsync y EnsureAllowedAsync, mismo patron que ClientsController/OrderStatusController; aplicable a VehicleStatus, DriverStatus, WorkOrderStatus (StatusCode ya sembrados) tras agregar las constantes de StatusDomains que faltan.
- ContactPointService (src/Teikem.Infrastructure/Services/ContactPointService.cs): GetForOwnerAsync/AddAsync para telefonos/correos del chofer (linea 285 del maestro); requiere registrar IOwnedEntityResolver para DRIVER.
- CustomFieldService (src/Teikem.Infrastructure/Services/CustomFieldService.cs): definiciones y valores por entidad; el maestro (linea 123) ya lista Vehiculo y Chofer entre las entidades con custom fields soportadas; requiere IOwnedEntityResolver + entradas Owner*Permission para VEHICLE/DRIVER.
- IDataSource / IDataSourceRegistry (src/Teikem.Infrastructure/Analytics/DataSources.cs), patron ya usado por OrderDataSources.cs y ClientDataSources.cs registrados en DependencyInjection.cs; el modulo 4 deberia registrar sus propias fuentes (Vehiculo, Chofer, MaintenanceWorkOrder, FuelLog) con el mismo contrato.
- PermissionCatalog (src/Teikem.Domain/Constants/PermissionCatalog.cs): FleetManage='fleet.manage' y FleetMaintenance='fleet.maintenance' (categoria FLEET) ya sembrados desde antes de este lote; se reutilizan tal cual en [RequirePermission].
- ModuleKeys.Catalog (src/Teikem.Domain/Constants/CatalogDomains.cs): mismo modulo que ClientsController/ContractsController; la bitacora confirma que Flota y mantenimiento vive dentro del grupo Catalogo, no en un modulo aparte.
- Patron de controlador de ClientsController.cs (src/Teikem.Api/Controllers/ClientsController.cs): [ApiController]/[Route]/[Authorize]/[RequireModule] a nivel de clase, [RequirePermission] por accion, rutas {publicId:guid}, /status via StatusService, /deactivate y /reactivate (SetActiveAsync) en vez de DELETE; reutilizable tal cual para VehiclesController/DriversController.
- Patron de DTOs record devueltos por los servicios (ver Contracts/CustomFieldContracts.cs y los *Dto/*Request de ClientsController), nunca entidades EF expuestas directamente.
- Atributo [AuditEntity(Constants.EntityTypes.X)] (visto en src/Teikem.Domain/Security/Security.cs) a aplicar sobre Vehicle/Driver/MaintenanceWorkOrder/FuelLog para auditoria automatica.
