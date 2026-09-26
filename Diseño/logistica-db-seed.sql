/* ============================================================================
   PLATAFORMA DE LOGÍSTICA — SEED DE CATÁLOGOS, ESTATUS, PERMISOS Y ROLES
   Correr DESPUÉS de logistica-db-estructura.sql.
   Idempotente: usa MERGE/guards para poder recorrerse de nuevo.
   Etiquetas en JSON multilingüe {"es":"...","en":"..."}.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* -------------------------------------------------------------------------
   0) MÓDULOS DE PLATAFORMA — catálogo completo (activo o no) + encendido
      para el tenant demo de Advance Logistics (@DemoTenantId).
      Advance usa: última milla, COD, WMS con lote/serie, Equipos en alquiler
      (solo tracking). NO usa: Cross-dock formal, Marítimo, Facturación de
      alquiler — quedan definidos pero apagados, listos para otro tenant.
   ------------------------------------------------------------------------- */
MERGE dbo.ModuleDefinition AS t
USING (VALUES
 ('LTL_GROUND','Última milla terrestre','Ground last-mile','Recogido y entrega terrestre, cualquier tamaño','Operacion',NULL,10),
 ('COD','Cash on Delivery','Cash on Delivery','Cobro contra entrega, cuadre y remesa al cliente','Dinero',NULL,20),
 ('WMS_LOTSERIAL','Inventario y trazabilidad','Inventory & traceability','Almacén con lote/serie, ubicaciones, kárdex','Almacen',NULL,30),
 ('CROSSDOCK','Cross-docking','Cross-docking','Muelle a muelle con citas, sin guardar','Almacen',NULL,40),
 ('RENTAL_EQUIPMENT','Equipos en alquiler','Rental equipment','Activos serializados: ubicación, lease, mantenimiento','Equipos',NULL,50),
 ('RENTAL_BILLING','Facturación de alquiler','Rental billing','Cargos recurrentes configurables por contrato','Equipos','RENTAL_EQUIPMENT',60),
 ('MARITIME','Transporte marítimo','Maritime transport','Consolidación, viajes y manifiesto por barco','Operacion',NULL,70),
 ('CLIENT_PORTAL','Portal de clientes','Client portal','Booking, tracking y documentos para el cliente final','Catalogo',NULL,80),
 ('CUSTOM_FIELDS','Campos personalizados','Custom fields','Campos e informes por entidad, sin deploy','Analisis','ANALYTICS',90),
 ('PURCHASING','Compras','Purchasing','Proveedores, órdenes de compra e inventario propio para reventa','Almacen',NULL,35),
 ('CATALOG','Catálogo','Catalog','Clientes y contratos, choferes y tarifas, flota','Catalogo',NULL,85),
 ('ANALYTICS','Análisis','Analytics','Vistas, campos personalizados, indicadores y gráficos','Analisis',NULL,88),
 ('SYSTEM','Sistema','System','Roles y usuarios, integraciones, seguridad y ajustes (núcleo)','Sistema',NULL,95)
) AS s(ModuleKey,Name,NameEn,Description,Category,DependsOnModuleKey,SortOrder)
ON t.ModuleKey = s.ModuleKey
WHEN MATCHED THEN UPDATE SET Name=s.Name,NameEn=s.NameEn,Description=s.Description,Category=s.Category,DependsOnModuleKey=s.DependsOnModuleKey,SortOrder=s.SortOrder,IsActive=1
WHEN NOT MATCHED THEN INSERT (ModuleKey,Name,NameEn,Description,Category,DependsOnModuleKey,SortOrder) VALUES (s.ModuleKey,s.Name,s.NameEn,s.Description,s.Category,s.DependsOnModuleKey,s.SortOrder);
-- Módulos núcleo: no se pueden apagar (dependencias entre módulos: RENTAL_BILLING→RENTAL_EQUIPMENT, CUSTOM_FIELDS→ANALYTICS)
UPDATE dbo.ModuleDefinition SET IsCore = CASE WHEN ModuleKey IN ('LTL_GROUND','SYSTEM') THEN 1 ELSE 0 END;

DECLARE @DemoTenantId INT = (SELECT TOP 1 TenantId FROM dbo.Tenant ORDER BY TenantId);
IF @DemoTenantId IS NOT NULL
BEGIN
    MERGE dbo.TenantModule AS t
    USING (VALUES ('LTL_GROUND',1),('COD',1),('WMS_LOTSERIAL',1),('CROSSDOCK',0),
                   ('RENTAL_EQUIPMENT',1),('RENTAL_BILLING',0),('MARITIME',0),
                   ('CLIENT_PORTAL',1),('CUSTOM_FIELDS',1),('PURCHASING',1),
                   ('CATALOG',1),('ANALYTICS',1),('SYSTEM',1)
    ) AS s(ModuleKey,IsEnabled)
    ON t.TenantId=@DemoTenantId AND t.ModuleKey=s.ModuleKey
    WHEN MATCHED THEN UPDATE SET IsEnabled=s.IsEnabled
    WHEN NOT MATCHED THEN INSERT (TenantId,ModuleKey,IsEnabled) VALUES (@DemoTenantId,s.ModuleKey,s.IsEnabled);
END
GO

/* -------------------------------------------------------------------------
   1) CATALOG DOMAINS  (Scope 1=Lookup, 2=Status)
   ------------------------------------------------------------------------- */
;WITH d(DomainKey, Scope, Es, En) AS (
    SELECT v.* FROM (VALUES
    -- Lookup
    ('Currency',1,'Moneda','Currency'),('PaymentTerm',1,'Término de pago','Payment term'),
    ('LocationType',1,'Tipo de ubicación','Location type'),('ServiceType',1,'Tipo de servicio','Service type'),
    ('StopType',1,'Tipo de parada','Stop type'),('OrderPriority',1,'Prioridad','Priority'),
    ('OrderRefType',1,'Tipo de referencia','Reference type'),('DocType',1,'Tipo de documento','Document type'),
    ('ContactType',1,'Tipo de contacto','Contact type'),('EntityType',1,'Tipo de entidad','Entity type'),
    ('Country',1,'País','Country'),('PricingType',1,'Tipo de precio','Pricing type'),
    ('SurchargeType',1,'Tipo de recargo','Surcharge type'),('ZoneMatchType',1,'Criterio de zona','Zone match type'),
    ('BillingModel',1,'Modelo de cobro','Billing model'),('UnitOfMeasure',1,'Unidad de medida','Unit of measure'),
    ('TrackingType',1,'Tipo de seguimiento','Tracking type'),('ZoneType',1,'Tipo de zona','Zone type'),
    ('InventoryTxnType',1,'Tipo de movimiento','Inventory txn type'),('OptimizerEngine',1,'Motor de optimización','Optimizer engine'),
    ('StageKind',1,'Clase de etapa','Stage kind'),('Capability',1,'Capacidad','Capability'),
    ('RateComponentType',1,'Componente de tarifa','Rate component type'),('RateBasis',1,'Base de tarifa','Rate basis'),
    ('PricingMode',1,'Modo de precio','Pricing mode'),('TierMode',1,'Modo de escalón','Tier mode'),
    ('VehicleType',1,'Tipo de vehículo','Vehicle type'),('Ownership',1,'Propiedad','Ownership'),
    ('FuelType',1,'Tipo de combustible','Fuel type'),('VehicleDocType',1,'Documento de vehículo','Vehicle doc type'),
    ('MaintenanceTrigger',1,'Disparador de mant.','Maintenance trigger'),('MaintenanceType',1,'Tipo de mant.','Maintenance type'),
    ('LicenseClass',1,'Clase de licencia','License class'),('CertificationType',1,'Certificación','Certification type'),
    ('AssignmentType',1,'Tipo de asignación','Assignment type'),('ReceiptType',1,'Tipo de recepción','Receipt type'),
    ('WarehouseTaskType',1,'Tipo de tarea','Task type'),('CartonType',1,'Tipo de caja','Carton type'),
    ('DockType',1,'Tipo de muelle','Dock type'),('DockDirection',1,'Dirección de muelle','Dock direction'),
    ('DevicePlatform',1,'Plataforma','Platform'),('PodOutcome',1,'Resultado de entrega','POD outcome'),
    ('DeliveryFailureReason',1,'Razón de fallo','Failure reason'),('PortalRole',1,'Rol de portal','Portal role'),
    ('PortalEventType',1,'Evento de portal','Portal event'),('ChargeType',1,'Tipo de cargo','Charge type'),
    ('PaymentMethod',1,'Método de pago','Payment method'),('SettlementLineType',1,'Tipo de línea de liq.','Settlement line type'),
    ('KpiKey',1,'Indicador','KPI'),('IntegrationType',1,'Tipo de integración','Integration type'),
    ('WebhookEventType',1,'Evento de webhook','Webhook event'),('MessageDirection',1,'Dirección de mensaje','Message direction'),
    ('UserKind',1,'Tipo de usuario','User kind'),('PermissionCategory',1,'Categoría de permiso','Permission category'),
    ('MfaFactorType',1,'Factor MFA','MFA factor'),('AuditAction',1,'Acción de auditoría','Audit action'),
    ('SecurityEventType',1,'Evento de seguridad','Security event'),('SecurityOutcome',1,'Resultado','Outcome'),
    ('CustomFieldDataType',1,'Tipo de campo','Field data type'),('ReportVisibility',1,'Visibilidad de informe','Report visibility'),('ReportChartType',1,'Tipo de gráfico','Chart type'),
    ('CodType',1,'Tipo de COD','COD type'),('CodPaymentMethod',1,'Método de cobro COD','COD payment method'),
    ('RentalAssetType',1,'Tipo de equipo en alquiler','Rental asset type'),('RentalBillingFrequency',1,'Frecuencia de cobro','Billing frequency'),
    ('GeocodeAccuracy',1,'Precisión de geocodificación','Geocode accuracy'),
    ('AggregateFn',1,'Función de agregación','Aggregate function'),('BusinessModule',1,'Módulo de negocio','Business module'),
    ('DateRangeMode',1,'Rango de fecha','Date range'),('PackageType',1,'Tipo de paquete','Package type'),
    -- Lote 4 — Flota, choferes y mantenimiento (pago a choferes)
    ('DriverPayoutFormula',1,'Fórmula de pago a choferes','Driver payout formula'),
    -- Status
    ('ClientStatus',2,'Estatus de cliente','Client status'),('ContractStatus',2,'Estatus de contrato','Contract status'),
    ('OrderStatus',2,'Estatus de orden','Order status'),('StopStatus',2,'Estatus de parada','Stop status'),
    ('TripStatus',2,'Estatus de trip','Trip status'),('RouteStatus',2,'Estatus de ruta','Route status'),
    ('RouteStopStatus',2,'Estatus de parada de ruta','Route stop status'),('VehicleStatus',2,'Estatus de vehículo','Vehicle status'),
    ('DriverStatus',2,'Estatus de chofer','Driver status'),('WarehouseStatus',2,'Estatus de almacén','Warehouse status'),
    ('SerialStatus',2,'Estatus de serie','Serial status'),('OptimizationRunStatus',2,'Estatus de corrida','Run status'),
    ('WorkOrderStatus',2,'Estatus de orden de trabajo','Work order status'),('AsnStatus',2,'Estatus de ASN','ASN status'),
    ('ReceiptStatus',2,'Estatus de recepción','Receipt status'),('WarehouseTaskStatus',2,'Estatus de tarea','Task status'),
    ('PickWaveStatus',2,'Estatus de ola','Wave status'),('PickTaskStatus',2,'Estatus de pick','Pick status'),
    ('CartonStatus',2,'Estatus de caja','Carton status'),('CycleCountStatus',2,'Estatus de conteo','Count status'),
    ('DockStatus',2,'Estatus de muelle','Dock status'),('AppointmentStatus',2,'Estatus de cita','Appointment status'),
    ('CrossDockStatus',2,'Estatus de cross-dock','Cross-dock status'),('AllocationStatus',2,'Estatus de asignación','Allocation status'),
    ('PortalUserStatus',2,'Estatus de usuario portal','Portal user status'),('InvoiceStatus',2,'Estatus de factura','Invoice status'),
    ('BillingRunStatus',2,'Estatus de corrida de fact.','Billing run status'),('SettlementStatus',2,'Estatus de liquidación','Settlement status'),
    ('ApiCredentialStatus',2,'Estatus de credencial','Credential status'),('WebhookDeliveryStatus',2,'Estatus de entrega webhook','Webhook delivery status'),
    ('MembershipStatus',2,'Estatus de membresía','Membership status'),
    ('CodStatus',2,'Estatus COD de la orden','Order COD status'),('CodCollectionStatus',2,'Estatus de cobro COD','COD collection status'),('RemittanceStatus',2,'Estatus de remesa','Remittance status'),
    ('RentalAssetStatus',2,'Estatus del equipo','Asset status'),('RentalContractStatus',2,'Estatus del contrato de alquiler','Rental contract status'),('RentalChargeStatus',2,'Estatus del cargo de alquiler','Rental charge status'),
    ('PurchaseOrderStatus',2,'Estatus de la orden de compra','Purchase order status'),
    -- Lote 3 — Órdenes de transporte (importador)
    ('ImportBatchStatus',2,'Estatus de lote de importación','Import batch status'),
    -- Lote 4 — Flota, choferes y mantenimiento (viaje pagado al chofer)
    ('DriverTripStatus',2,'Estatus de viaje de chofer','Driver trip status')
    ) v(DomainKey,Scope,Es,En)
)
MERGE dbo.CatalogDomain AS t
USING d ON t.DomainKey = d.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (d.DomainKey, d.Scope, N'{"es":"'+d.Es+'","en":"'+d.En+'"}', 1, 1);
GO

/* -------------------------------------------------------------------------
   2) LOOKUP CODES
   Helper: inserta (Entity, Code, Es, En) si no existe.
   ------------------------------------------------------------------------- */
IF OBJECT_ID('tempdb..#L') IS NOT NULL DROP TABLE #L;
CREATE TABLE #L (Entity NVARCHAR(60), Code NVARCHAR(40), Es NVARCHAR(120), En NVARCHAR(120), Srt INT);
INSERT INTO #L (Entity, Code, Es, En, Srt) VALUES
-- StageKind (crítico: lo usa StatusCode)
('StageKind','PIPELINE','Pipeline','Pipeline',1),('StageKind','LATERAL','Lateral','Lateral',2),('StageKind','TERMINAL','Terminal','Terminal',3),
-- EntityType
('EntityType','CLIENT','Cliente','Client',1),('EntityType','CLIENT_CONTACT','Contacto','Contact',2),
('EntityType','DRIVER','Chofer','Driver',3),('EntityType','LOCATION','Ubicación','Location',4),
('EntityType','WAREHOUSE','Almacén','Warehouse',5),('EntityType','VEHICLE','Vehículo','Vehicle',6),
('EntityType','TRANSPORT_ORDER','Orden','Order',7),('EntityType','TRIP','Trip','Trip',8),
('EntityType','ROUTE','Ruta','Route',9),('EntityType','WORK_ORDER','Orden de trabajo','Work order',10),
('EntityType','RECEIPT_LINE','Línea de recepción','Receipt line',11),('EntityType','PICK_TASK','Tarea de pick','Pick task',12),
('EntityType','CARTON','Caja','Carton',13),('EntityType','CYCLE_COUNT','Conteo','Cycle count',14),
('EntityType','CROSSDOCK_PLAN','Plan cross-dock','Cross-dock plan',15),('EntityType','PROOF_OF_DELIVERY','POD','POD',16),
('EntityType','INVOICE','Factura','Invoice',17),('EntityType','PORTAL_USER','Usuario portal','Portal user',18),
('EntityType','API_CREDENTIAL','Credencial API','API credential',19),
('EntityType','CONTRACT','Contrato','Contract',20),('EntityType','PRODUCT','Producto','Product',21),
('EntityType','COD_COLLECTION','Cobro COD','COD collection',22),('EntityType','COD_REMITTANCE','Remesa COD','COD remittance',23),
('EntityType','RENTAL_ASSET','Equipo en alquiler','Rental asset',24),('EntityType','RENTAL_CONTRACT','Contrato de alquiler','Rental contract',25),
('EntityType','PURCHASE_ORDER','Orden de compra','Purchase order',26),('EntityType','SUPPLIER','Proveedor','Supplier',27),
-- Country
('Country','PR','Puerto Rico','Puerto Rico',1),('Country','US','Estados Unidos','United States',2),('Country','DO','Rep. Dominicana','Dominican Republic',3),
-- Currency
('Currency','USD','Dólar','US Dollar',1),
-- PaymentTerm
('PaymentTerm','COD','Contra entrega','Cash on delivery',1),('PaymentTerm','NET15','15 días','Net 15',2),('PaymentTerm','NET30','30 días','Net 30',3),('PaymentTerm','NET60','60 días','Net 60',4),
-- ContactType
('ContactType','PHONE','Teléfono','Phone',1),('ContactType','MOBILE','Móvil','Mobile',2),('ContactType','EMAIL','Correo','Email',3),('ContactType','FAX','Fax','Fax',4),('ContactType','WHATSAPP','WhatsApp','WhatsApp',5),
-- LocationType
('LocationType','PICKUP','Recogido','Pickup',1),('LocationType','DELIVERY','Entrega','Delivery',2),('LocationType','BOTH','Ambos','Both',3),('LocationType','BILLING','Facturación','Billing',4),('LocationType','CORPORATE','Corporativa','Corporate',5),
-- ServiceType
('ServiceType','STANDARD','Estándar','Standard',1),('ServiceType','EXPRESS','Express','Express',2),('ServiceType','SAMEDAY','Mismo día','Same day',3),('ServiceType','SCHEDULED','Programado','Scheduled',4),
-- StopType
('StopType','PICKUP','Recogido','Pickup',1),('StopType','DELIVERY','Entrega','Delivery',2),
-- OrderPriority
('OrderPriority','HIGH','Alta','High',1),('OrderPriority','NORMAL','Normal','Normal',2),('OrderPriority','LOW','Baja','Low',3),
-- OrderRefType
('OrderRefType','CLIENT_PO','PO Cliente','Client PO',1),('OrderRefType','ECOMMERCE','E-commerce','E-commerce',2),('OrderRefType','CARRIER','Transportista','Carrier',3),('OrderRefType','OTHER','Otro','Other',4),
-- DocType
('DocType','BOL','Conocimiento','BOL',1),('DocType','INVOICE','Factura','Invoice',2),('DocType','PHOTO','Foto','Photo',3),('DocType','SIGNATURE','Firma','Signature',4),('DocType','CONTRACT','Contrato','Contract',5),('DocType','OTHER','Otro','Other',6),
-- BillingModel
('BillingModel','BY_PICKUP','Por recogido','By pickup',1),('BillingModel','BY_DELIVERY','Por entrega','By delivery',2),('BillingModel','MIXED','Mixto','Mixed',3),
-- PricingType / PricingMode / TierMode / RateBasis / RateComponentType / SurchargeType / ZoneMatchType
('PricingType','FIXED','Fijo','Fixed',1),('PricingType','PERCENT','Porcentaje','Percent',2),('PricingType','PER_KG','Por kg','Per kg',3),('PricingType','PER_M3','Por m³','Per m³',4),('PricingType','PER_STOP','Por parada','Per stop',5),
('PricingMode','FIXED','Fijo','Fixed',1),('PricingMode','PER_UNIT','Por unidad','Per unit',2),('PricingMode','TIERED','Escalonado','Tiered',3),
('TierMode','GRADUATED','Marginal','Graduated',1),('TierMode','VOLUME','Por volumen','Volume',2),
('RateBasis','PER_SHIPMENT','Por envío','Per shipment',1),('RateBasis','PER_PIECE','Por pieza','Per piece',2),('RateBasis','PER_KG','Por kg','Per kg',3),('RateBasis','PER_M3','Por m³','Per m³',4),('RateBasis','PER_STOP','Por parada','Per stop',5),('RateBasis','PER_KM','Por km','Per km',6),
('RateComponentType','BASE_FREIGHT','Flete base','Base freight',1),('RateComponentType','PICKUP_FEE','Cargo recogido','Pickup fee',2),('RateComponentType','SURCHARGE','Recargo','Surcharge',3),
('SurchargeType','FUEL','Combustible','Fuel',1),('SurchargeType','OVERWEIGHT','Sobrepeso','Overweight',2),('SurchargeType','SPECIAL_HANDLING','Manejo especial','Special handling',3),('SurchargeType','WAIT_TIME','Tiempo de espera','Wait time',4),
('ZoneMatchType','POSTAL_CODE','Código postal','Postal code',1),('ZoneMatchType','POSTAL_RANGE','Rango postal','Postal range',2),('ZoneMatchType','MUNICIPALITY','Municipio','Municipality',3),('ZoneMatchType','POLYGON','Polígono','Polygon',4),
-- UnitOfMeasure / TrackingType / ZoneType / InventoryTxnType / OptimizerEngine
('UnitOfMeasure','UN','Unidad','Unit',1),('UnitOfMeasure','BOX','Caja','Box',2),('UnitOfMeasure','PALLET','Tarima','Pallet',3),('UnitOfMeasure','KG','Kilogramo','Kilogram',4),('UnitOfMeasure','L','Litro','Liter',5),
('TrackingType','NONE','Ninguno','None',1),('TrackingType','LOT','Lote','Lot',2),('TrackingType','SERIAL','Serie','Serial',3),
('ZoneType','PICKING','Picking','Picking',1),('ZoneType','RESERVE','Reserva','Reserve',2),('ZoneType','REFRIGERATED','Refrigerado','Refrigerated',3),('ZoneType','QUARANTINE','Cuarentena','Quarantine',4),('ZoneType','CROSSDOCK','Cross-dock','Cross-dock',5),
('InventoryTxnType','RECEIPT','Recepción','Receipt',1),('InventoryTxnType','ISSUE','Despacho','Issue',2),('InventoryTxnType','TRANSFER','Transferencia','Transfer',3),('InventoryTxnType','ADJUSTMENT','Ajuste','Adjustment',4),('InventoryTxnType','CROSSDOCK','Cross-dock','Cross-dock',5),
('OptimizerEngine','VROOM','VROOM','VROOM',1),('OptimizerEngine','ORTOOLS','OR-Tools','OR-Tools',2),('OptimizerEngine','MANUAL','Manual','Manual',3),
-- Capability
('Capability','EDIT_CARGO','Editar carga','Edit cargo',1),('Capability','ASSIGN_TRIP','Asignar a trip','Assign trip',2),('Capability','CANCEL','Cancelar','Cancel',3),('Capability','REPRICE','Reprecio','Reprice',4),('Capability','ADD_DOCUMENT','Añadir documento','Add document',5),
-- Fleet
('VehicleType','VAN','Van','Van',1),('VehicleType','TRUCK','Camión','Truck',2),('VehicleType','TRACTOR','Tractor','Tractor',3),('VehicleType','REEFER','Refrigerado','Reefer',4),
('Ownership','OWNED','Propio','Owned',1),('Ownership','LEASED','Arrendado','Leased',2),('Ownership','THIRD_PARTY','Tercero','Third party',3),
('FuelType','DIESEL','Diésel','Diesel',1),('FuelType','GASOLINE','Gasolina','Gasoline',2),('FuelType','ELECTRIC','Eléctrico','Electric',3),('FuelType','LPG','GLP','LPG',4),
('VehicleDocType','REGISTRATION','Registro','Registration',1),('VehicleDocType','INSURANCE','Seguro','Insurance',2),('VehicleDocType','INSPECTION','Inspección','Inspection',3),('VehicleDocType','PERMIT','Permiso','Permit',4),
('MaintenanceTrigger','MILEAGE','Kilometraje','Mileage',1),('MaintenanceTrigger','TIME','Tiempo','Time',2),('MaintenanceTrigger','BOTH','Ambos','Both',3),
('MaintenanceType','PREVENTIVE','Preventivo','Preventive',1),('MaintenanceType','CORRECTIVE','Correctivo','Corrective',2),
('LicenseClass','CDL_A','CDL-A','CDL-A',1),('LicenseClass','CDL_B','CDL-B','CDL-B',2),('LicenseClass','REGULAR','Regular','Regular',3),
('CertificationType','HAZMAT','HazMat','HazMat',1),('CertificationType','REEFER','Refrigerado','Reefer',2),('CertificationType','FORKLIFT','Montacargas','Forklift',3),
('AssignmentType','PERMANENT','Permanente','Permanent',1),('AssignmentType','TEMPORARY','Temporal','Temporary',2),
-- WMS / Cross-dock
('ReceiptType','ASN','Con ASN','ASN',1),('ReceiptType','BLIND','Ciega','Blind',2),('ReceiptType','RETURN','Devolución','Return',3),
('WarehouseTaskType','PUTAWAY','Putaway','Putaway',1),('WarehouseTaskType','PICK','Pick','Pick',2),('WarehouseTaskType','PACK','Pack','Pack',3),('WarehouseTaskType','REPLENISH','Reabasto','Replenish',4),('WarehouseTaskType','COUNT','Conteo','Count',5),('WarehouseTaskType','LOAD','Carga','Load',6),('WarehouseTaskType','CROSSDOCK','Cross-dock','Cross-dock',7),
('CartonType','SMALL','Pequeña','Small',1),('CartonType','MEDIUM','Mediana','Medium',2),('CartonType','LARGE','Grande','Large',3),
('DockType','INBOUND','Inbound','Inbound',1),('DockType','OUTBOUND','Outbound','Outbound',2),('DockType','BOTH','Ambos','Both',3),
('DockDirection','INBOUND','Inbound','Inbound',1),('DockDirection','OUTBOUND','Outbound','Outbound',2),
-- App / POD
('DevicePlatform','IOS','iOS','iOS',1),('DevicePlatform','ANDROID','Android','Android',2),
('PodOutcome','DELIVERED','Entregado','Delivered',1),('PodOutcome','PARTIAL','Parcial','Partial',2),('PodOutcome','REJECTED','Rechazado','Rejected',3),('PodOutcome','FAILED','Fallido','Failed',4),
('DeliveryFailureReason','ABSENT','Ausente','Absent',1),('DeliveryFailureReason','WRONG_ADDRESS','Dirección incorrecta','Wrong address',2),('DeliveryFailureReason','DAMAGED','Daño','Damaged',3),('DeliveryFailureReason','REJECTED','Rechazo','Rejected',4),
-- Portal
('PortalRole','CLIENT_ADMIN','Admin cliente','Client admin',1),('PortalRole','CLIENT_OPERATOR','Operador','Operator',2),('PortalRole','CLIENT_READONLY','Solo lectura','Read only',3),
('PortalEventType','ORDER_CREATED','Orden creada','Order created',1),('PortalEventType','IN_TRANSIT','En tránsito','In transit',2),('PortalEventType','DELIVERED','Entregada','Delivered',3),('PortalEventType','INVOICED','Facturada','Invoiced',4),
-- Billing
('ChargeType','FREIGHT','Flete','Freight',1),('ChargeType','SURCHARGE','Recargo','Surcharge',2),('ChargeType','PICKUP','Recogido','Pickup',3),('ChargeType','HANDLING','Manejo','Handling',4),('ChargeType','PRODUCT_SALE','Venta de producto','Product sale',5),
('PaymentMethod','ACH','ACH','ACH',1),('PaymentMethod','CHECK','Cheque','Check',2),('PaymentMethod','CARD','Tarjeta','Card',3),('PaymentMethod','CASH','Efectivo','Cash',4),
('SettlementLineType','PAYMENT','Pago','Payment',1),('SettlementLineType','DEDUCTION','Deducción','Deduction',2),('SettlementLineType','BONUS','Bono','Bonus',3),
-- KPI / Integraciones
('KpiKey','ON_TIME_PCT','% A tiempo','On-time %',1),('KpiKey','COST_PER_KM','Costo/km','Cost/km',2),('KpiKey','FLEET_UTIL','Utilización flota','Fleet utilization',3),('KpiKey','FAILED_DELIV','Entregas fallidas','Failed deliveries',4),
('KpiKey','COD_PENDING','COD pendiente','COD pending',5),('KpiKey','COD_COLLECTED','COD cobrado','COD collected',6),('KpiKey','COD_REMITTED','COD remitido','COD remitted',7),
('CodType','NONE','Sin COD','No COD',1),('CodType','CASH','Efectivo','Cash',2),('CodType','CHECK','Cheque','Check',3),('CodType','COMPANY_CHECK','Company check','Company check',4),
('CodPaymentMethod','CASH','Efectivo','Cash',1),('CodPaymentMethod','CHECK','Cheque','Check',2),('CodPaymentMethod','COMPANY_CHECK','Company check','Company check',3),('CodPaymentMethod','ATH_MOVIL','ATH Móvil','ATH Móvil',4),('CodPaymentMethod','CARD','Tarjeta','Card',5),
('RentalAssetType','GLUCOMETER','Medidor de glucosa','Glucometer',1),('RentalAssetType','WHEELCHAIR','Silla de ruedas','Wheelchair',2),('RentalAssetType','O2_CONCENTRATOR','Concentrador de oxígeno','O2 concentrator',3),('RentalAssetType','HOSPITAL_BED','Cama de hospital','Hospital bed',4),('RentalAssetType','OTHER','Otro','Other',9),
('RentalBillingFrequency','WEEKLY','Semanal','Weekly',1),('RentalBillingFrequency','MONTHLY','Mensual','Monthly',2),('RentalBillingFrequency','ONE_TIME','Único','One-time',3),
('GeocodeAccuracy','EXACT','Dirección exacta','Exact address',1),('GeocodeAccuracy','ZIP_CENTROID','Aproximado por código postal','Approximate by ZIP',2),('GeocodeAccuracy','CITY_CENTROID','Aproximado por pueblo','Approximate by city',3),('GeocodeAccuracy','MANUAL','Ajustado a mano','Manually adjusted',4),
('IntegrationType','ERP','ERP','ERP',1),('IntegrationType','ECOMMERCE','E-commerce','E-commerce',2),('IntegrationType','CARRIER','Transportista','Carrier',3),
('WebhookEventType','ORDER_CREATED','orden.creada','order.created',1),('WebhookEventType','ORDER_DELIVERED','orden.entregada','order.delivered',2),('WebhookEventType','INVOICE_ISSUED','factura.emitida','invoice.issued',3),
('MessageDirection','INBOUND','Inbound','Inbound',1),('MessageDirection','OUTBOUND','Outbound','Outbound',2),
-- Seguridad
('UserKind','INTERNAL','Interno','Internal',1),('UserKind','PORTAL','Portal','Portal',2),('UserKind','SERVICE','Servicio','Service',3),
('PermissionCategory','ORDERS','Órdenes','Orders',1),('PermissionCategory','TRIPS','Trips','Trips',2),('PermissionCategory','WAREHOUSE','Almacén','Warehouse',3),('PermissionCategory','BILLING','Facturación','Billing',4),('PermissionCategory','FLEET','Flota','Fleet',5),('PermissionCategory','ADMIN','Administración','Admin',6),('PermissionCategory','COD','COD','COD',7),('PermissionCategory','RENTAL','Equipos','Rental',8),('PermissionCategory','PURCHASING','Compras','Purchasing',9),
('MfaFactorType','TOTP','TOTP','TOTP',1),('MfaFactorType','SMS','SMS','SMS',2),('MfaFactorType','BIOMETRIC','Biométrico','Biometric',3),('MfaFactorType','RECOVERY','Recuperación','Recovery',4),
('AuditAction','CREATE','Crear','Create',1),('AuditAction','UPDATE','Actualizar','Update',2),('AuditAction','DELETE','Borrar','Delete',3),('AuditAction','RESTORE','Restaurar','Restore',4),
('SecurityEventType','LOGIN','Login','Login',1),('SecurityEventType','LOGOUT','Logout','Logout',2),('SecurityEventType','MFA','MFA','MFA',3),('SecurityEventType','REAUTH','Reauth','Reauth',4),('SecurityEventType','PERMISSION_DENIED','Permiso denegado','Permission denied',5),('SecurityEventType','TOKEN_REVOKED','Token revocado','Token revoked',6),('SecurityEventType','ROLE_CHANGE','Cambio de rol','Role change',7),
('SecurityOutcome','SUCCESS','Éxito','Success',1),('SecurityOutcome','FAILURE','Fallo','Failure',2),('SecurityOutcome','BLOCKED','Bloqueado','Blocked',3),
-- Campos personalizados e informes
('CustomFieldDataType','TEXT','Texto','Text',1),('CustomFieldDataType','NUMBER','Número','Number',2),('CustomFieldDataType','DATE','Fecha','Date',3),('CustomFieldDataType','DATETIME','Fecha y hora','Date-time',4),('CustomFieldDataType','BOOL','Sí/No','Boolean',5),('CustomFieldDataType','SELECT','Selección','Select',6),('CustomFieldDataType','MULTISELECT','Multi-selección','Multi-select',7),('CustomFieldDataType','LOOKUP_REF','Referencia a catálogo','Lookup reference',8),
('ReportVisibility','PRIVATE','Privado','Private',1),('ReportVisibility','TENANT','Todo el tenant','Whole tenant',2),('ReportVisibility','SHARED','Compartido','Shared',3),
('ReportChartType','TABLE','Tabla','Table',1),('ReportChartType','BAR','Barras','Bar',2),('ReportChartType','LINE','Líneas','Line',3),('ReportChartType','PIE','Pastel','Pie',4),('ReportChartType','DONUT','Dona','Donut',5),
-- Indicadores y gráficos (módulos H e I)
('AggregateFn','COUNT','Cantidad','Count',1),('AggregateFn','SUM','Suma','Sum',2),('AggregateFn','AVG','Promedio','Average',3),('AggregateFn','MIN','Mínimo','Minimum',4),('AggregateFn','MAX','Máximo','Maximum',5),
('BusinessModule','OPERATIONS','Operación','Operations',1),('BusinessModule','WAREHOUSE','Almacén','Warehouse',2),('BusinessModule','ACCOUNTING','Contabilidad','Accounting',3),
('DateRangeMode','LAST7','Últimos 7 días','Last 7 days',1),('DateRangeMode','LAST30','Últimos 30 días','Last 30 days',2),('DateRangeMode','THIS_MONTH','Este mes','This month',3),('DateRangeMode','CUSTOM','Rango personalizado','Custom range',4),('DateRangeMode','ALL','Todo el tiempo','All time',5),
-- Tipo de paquete (módulo 2: default de captura por tenant)
('PackageType','BOX','Caja','Box',1),('PackageType','ENVELOPE','Sobre','Envelope',2),('PackageType','PALLET','Tarima','Pallet',3),
-- Categoría de permisos de análisis y eventos de seguridad adicionales
('PermissionCategory','ANALYTICS','Análisis','Analytics',10),
('SecurityEventType','PASSWORD_CHANGE','Cambio de contraseña','Password change',8),('SecurityEventType','LOCKOUT','Bloqueo de cuenta','Account lockout',9),
('SecurityEventType','API_CREDENTIAL','Credencial de API','API credential',10),('SecurityEventType','TENANT_SWITCH','Cambio de compañía','Tenant switch',11),
-- EntityType de las capas transversales (auditoría, campos personalizados y contactos sobre entidades de plataforma)
('EntityType','TENANT','Compañía','Tenant',40),('EntityType','USER','Usuario','User',41),('EntityType','ROLE','Rol','Role',42),
('EntityType','LOOKUP_CODE','Valor de catálogo','Lookup value',43),('EntityType','CATALOG_DOMAIN','Lista de catálogo','Catalog list',44),
('EntityType','STATUS_CODE','Estatus','Status',45),('EntityType','STATUS_CONFIG','Configuración de estatus','Status configuration',46),
('EntityType','CONTACT_POINT','Contacto','Contact point',47),('EntityType','CUSTOM_FIELD_DEFINITION','Campo personalizado','Custom field',48),
('EntityType','REPORT_DEFINITION','Vista / informe','Report',49),('EntityType','INDICATOR_DEFINITION','Indicador','Indicator',50),
('EntityType','CHART_DEFINITION','Gráfico','Chart',51),('EntityType','TENANT_MODULE','Módulo de compañía','Tenant module',52),
('EntityType','AUDIT_LOG','Bitácora de cambios','Audit log',53),('EntityType','SECURITY_EVENT','Evento de seguridad','Security event',54),
-- Lote 2 — Clientes y contratos: entidades auditables nuevas, componente de pieza extra, categoría de permisos y capacidad
('EntityType','RATE_COMPONENT','Componente de tarifa','Rate component',55),('EntityType','SPECIAL_SERVICE','Servicio especial','Special service',56),
('RateComponentType','EXTRA_PIECE','Pieza extra','Extra piece',4),
('PermissionCategory','CLIENTS','Clientes y contratos','Clients & contracts',11),
('Capability','EDIT_CONTRACT','Editar contrato','Edit contract',6),
-- Lote 3 — Órdenes de transporte: historial del ciclo COD (separado del de OrderStatus) y de las paradas; importador de órdenes
('EntityType','ORDER_COD','COD de la orden','Order COD',57),('EntityType','ORDER_STOP','Parada de orden','Order stop',58),
('EntityType','IMPORT_TEMPLATE','Plantilla de importación','Import template',59),('EntityType','IMPORT_BATCH','Lote de importación','Import batch',60),
-- Lote 4 — Flota, choferes y mantenimiento: fórmulas de pago a choferes, entidades auditables nuevas (la orden de trabajo
-- reutiliza 'WORK_ORDER', ya sembrado arriba; no se crea MAINTENANCE_WORK_ORDER) y capacidad de edición de la OT
('DriverPayoutFormula','DELIVERY_PLUS_ATTEMPTS','Entrega + cada intento','Delivery + each attempt',1),
('DriverPayoutFormula','DELIVERY_INCLUDES_FIRST','La entrega incluye el 1er intento','Delivery includes 1st attempt',2),
('DriverPayoutFormula','FAILED_REPLACES_DELIVERY','Intento fallido reemplaza a entrega','Failed attempt replaces delivery',3),
('EntityType','MAINTENANCE_SCHEDULE','Programa de mantenimiento','Maintenance schedule',61),('EntityType','FUEL_LOG','Carga de combustible','Fuel log',62),
('EntityType','FLEET_DOCUMENT','Documento de flota','Fleet document',63),('EntityType','DRIVER_RATE','Tarifa de chofer','Driver rate',64),
('EntityType','DRIVER_TRIP','Viaje de chofer','Driver trip',65),('EntityType','DISPATCH_ZONE','Zona de despacho','Dispatch zone',66),
('Capability','EDIT_WORK_ORDER','Editar orden de trabajo','Edit work order',7),
-- Lote 5 — Trips y rutas: motor HEURISTIC (determinista, sin llamadas externas), entidades con historial/auditoría nuevas
-- (TRIP y ROUTE ya estaban sembradas arriba) y capacidad de edición de la cabecera de la ruta
('OptimizerEngine','HEURISTIC','Heurística (zona y ventana)','Heuristic (zone & window)',4),
('EntityType','ROUTE_STOP','Parada de ruta','Route stop',67),('EntityType','OPTIMIZATION_RUN','Corrida de optimización','Optimization run',68),
('Capability','EDIT_TRIP','Editar ruta','Edit trip',8);

MERGE dbo.LookupCode AS t
USING #L AS s ON t.Entity = s.Entity AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (s.Entity, s.Code, N'{"es":"'+s.Es+'","en":"'+s.En+'"}', s.Srt, 1, 1);
GO

/* -------------------------------------------------------------------------
   3) STATUS CODES  (con StageKind y color). Resuelve StageKindLookupId.
   ------------------------------------------------------------------------- */
DECLARE @PIPE INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity='StageKind' AND InternalCode='PIPELINE');
DECLARE @LAT  INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity='StageKind' AND InternalCode='LATERAL');
DECLARE @TERM INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity='StageKind' AND InternalCode='TERMINAL');

IF OBJECT_ID('tempdb..#S') IS NOT NULL DROP TABLE #S;
CREATE TABLE #S (Entity NVARCHAR(60), Code NVARCHAR(40), Es NVARCHAR(120), En NVARCHAR(120), Stage INT, Srt INT, Color CHAR(7), IsInitial BIT);
INSERT INTO #S VALUES
-- OrderStatus (ejemplo completo: pipeline + laterales + terminales)
('OrderStatus','DRAFT','Borrador','Draft',@PIPE,1,'#9CA3AF',1),
('OrderStatus','CONFIRMED','Confirmada','Confirmed',@PIPE,2,'#3B82F6',0),
('OrderStatus','PICKUP','Recogido','Pickup',@PIPE,3,'#6366F1',0),
('OrderStatus','INBOUND','Inbound','Inbound',@PIPE,4,'#8B5CF6',0),
('OrderStatus','PLANNED','Planificada','Planned',@PIPE,5,'#0EA5E9',0),
('OrderStatus','IN_TRANSIT','En tránsito','In transit',@PIPE,6,'#F59E0B',0),
('OrderStatus','ARRIVED','En sitio','Arrived',@PIPE,7,'#10B981',0),
('OrderStatus','DELIVERED','Entregada','Delivered',@TERM,8,'#059669',0),
('OrderStatus','ON_HOLD','En espera','On hold',@LAT,20,'#FBBF24',0),
('OrderStatus','PARTIAL','Parcial','Partial',@LAT,21,'#F97316',0),
('OrderStatus','FAILED','Fallida','Failed',@LAT,22,'#EF4444',0),
('OrderStatus','CANCELLED','Cancelada','Cancelled',@TERM,30,'#6B7280',0),
-- StopStatus
('StopStatus','PENDING','Pendiente','Pending',@PIPE,1,'#9CA3AF',1),
('StopStatus','EN_ROUTE','En camino','En route',@PIPE,2,'#F59E0B',0),
('StopStatus','COMPLETED','Completada','Completed',@TERM,3,'#059669',0),
('StopStatus','FAILED','Fallida','Failed',@LAT,4,'#EF4444',0),
-- TripStatus
('TripStatus','DRAFT','Borrador','Draft',@PIPE,1,'#9CA3AF',1),
('TripStatus','PLANNED','Planificado','Planned',@PIPE,2,'#0EA5E9',0),
('TripStatus','DISPATCHED','Despachado','Dispatched',@PIPE,3,'#3B82F6',0),
('TripStatus','IN_PROGRESS','En curso','In progress',@PIPE,4,'#F59E0B',0),
('TripStatus','COMPLETED','Completado','Completed',@TERM,5,'#059669',0),
('TripStatus','CANCELLED','Cancelado','Cancelled',@TERM,6,'#6B7280',0);

INSERT INTO #S VALUES
-- Estatus simples (un solo conjunto), todos como pipeline/terminal sencillos
('ClientStatus','ACTIVE','Activo','Active',@PIPE,1,'#059669',1),('ClientStatus','SUSPENDED','Suspendido','Suspended',@LAT,2,'#EF4444',0),('ClientStatus','PROSPECT','Prospecto','Prospect',@PIPE,3,'#9CA3AF',0),
('ContractStatus','DRAFT','Borrador','Draft',@PIPE,1,'#9CA3AF',1),('ContractStatus','ACTIVE','Vigente','Active',@PIPE,2,'#059669',0),('ContractStatus','EXPIRED','Vencido','Expired',@TERM,3,'#6B7280',0),('ContractStatus','CANCELLED','Cancelado','Cancelled',@TERM,4,'#EF4444',0),
('RouteStatus','DRAFT','Borrador','Draft',@PIPE,1,'#9CA3AF',1),('RouteStatus','OPTIMIZED','Optimizada','Optimized',@PIPE,2,'#3B82F6',0),('RouteStatus','ACTIVE','Activa','Active',@PIPE,3,'#059669',0),('RouteStatus','ARCHIVED','Archivada','Archived',@TERM,4,'#6B7280',0),
('RouteStopStatus','PENDING','Pendiente','Pending',@PIPE,1,'#9CA3AF',1),('RouteStopStatus','ON_THE_WAY','En camino','On the way',@PIPE,2,'#F59E0B',0),('RouteStopStatus','ARRIVED','Llegó','Arrived',@PIPE,3,'#10B981',0),('RouteStopStatus','COMPLETED','Completada','Completed',@TERM,4,'#059669',0),('RouteStopStatus','FAILED','Fallida','Failed',@LAT,5,'#EF4444',0),
('VehicleStatus','ACTIVE','Activo','Active',@PIPE,1,'#059669',1),('VehicleStatus','MAINTENANCE','En mant.','Maintenance',@LAT,2,'#F59E0B',0),('VehicleStatus','INACTIVE','Inactivo','Inactive',@TERM,3,'#6B7280',0),
('DriverStatus','ACTIVE','Activo','Active',@PIPE,1,'#059669',1),('DriverStatus','UNAVAILABLE','No disponible','Unavailable',@LAT,2,'#F59E0B',0),('DriverStatus','INACTIVE','Inactivo','Inactive',@TERM,3,'#6B7280',0),
('WarehouseStatus','ACTIVE','Activo','Active',@PIPE,1,'#059669',1),('WarehouseStatus','INACTIVE','Inactivo','Inactive',@TERM,2,'#6B7280',0),
('SerialStatus','AVAILABLE','Disponible','Available',@PIPE,1,'#059669',1),('SerialStatus','RESERVED','Reservado','Reserved',@PIPE,2,'#F59E0B',0),('SerialStatus','SHIPPED','Despachado','Shipped',@TERM,3,'#6B7280',0),
('OptimizationRunStatus','PENDING','Pendiente','Pending',@PIPE,1,'#9CA3AF',1),('OptimizationRunStatus','OK','OK','OK',@TERM,2,'#059669',0),('OptimizationRunStatus','ERROR','Error','Error',@TERM,3,'#EF4444',0),
('WorkOrderStatus','OPEN','Abierta','Open',@PIPE,1,'#9CA3AF',1),('WorkOrderStatus','IN_PROGRESS','En proceso','In progress',@PIPE,2,'#F59E0B',0),('WorkOrderStatus','CLOSED','Cerrada','Closed',@TERM,3,'#059669',0),('WorkOrderStatus','CANCELLED','Cancelada','Cancelled',@TERM,4,'#6B7280',0),
('AsnStatus','EXPECTED','Esperada','Expected',@PIPE,1,'#9CA3AF',1),('AsnStatus','RECEIVED','Recibida','Received',@TERM,2,'#059669',0),('AsnStatus','CANCELLED','Cancelada','Cancelled',@TERM,3,'#6B7280',0),
('ReceiptStatus','OPEN','Abierta','Open',@PIPE,1,'#9CA3AF',1),('ReceiptStatus','RECEIVED','Recibida','Received',@PIPE,2,'#10B981',0),('ReceiptStatus','PUTAWAY','En putaway','Putaway',@TERM,3,'#059669',0),
('WarehouseTaskStatus','PENDING','Pendiente','Pending',@PIPE,1,'#9CA3AF',1),('WarehouseTaskStatus','IN_PROGRESS','En proceso','In progress',@PIPE,2,'#F59E0B',0),('WarehouseTaskStatus','DONE','Completada','Done',@TERM,3,'#059669',0),
('PickWaveStatus','OPEN','Abierta','Open',@PIPE,1,'#9CA3AF',1),('PickWaveStatus','PICKING','En picking','Picking',@PIPE,2,'#F59E0B',0),('PickWaveStatus','PACKED','Empacada','Packed',@PIPE,3,'#10B981',0),('PickWaveStatus','SHIPPED','Despachada','Shipped',@TERM,4,'#059669',0),
('PickTaskStatus','PENDING','Pendiente','Pending',@PIPE,1,'#9CA3AF',1),('PickTaskStatus','PICKED','Pickeada','Picked',@TERM,2,'#059669',0),('PickTaskStatus','SHORT','Faltante','Short',@LAT,3,'#EF4444',0),
('CartonStatus','OPEN','Abierta','Open',@PIPE,1,'#9CA3AF',1),('CartonStatus','CLOSED','Cerrada','Closed',@PIPE,2,'#10B981',0),('CartonStatus','SHIPPED','Despachada','Shipped',@TERM,3,'#059669',0),
('CycleCountStatus','OPEN','Abierto','Open',@PIPE,1,'#9CA3AF',1),('CycleCountStatus','COUNTED','Contado','Counted',@PIPE,2,'#F59E0B',0),('CycleCountStatus','RECONCILED','Reconciliado','Reconciled',@TERM,3,'#059669',0),
('DockStatus','FREE','Libre','Free',@PIPE,1,'#059669',1),('DockStatus','OCCUPIED','Ocupado','Occupied',@LAT,2,'#F59E0B',0),('DockStatus','MAINTENANCE','Mant.','Maintenance',@LAT,3,'#6B7280',0),
('AppointmentStatus','SCHEDULED','Agendada','Scheduled',@PIPE,1,'#9CA3AF',1),('AppointmentStatus','ARRIVED','Llegó','Arrived',@PIPE,2,'#10B981',0),('AppointmentStatus','COMPLETED','Completada','Completed',@TERM,3,'#059669',0),('AppointmentStatus','NO_SHOW','No llegó','No show',@LAT,4,'#EF4444',0),
('CrossDockStatus','OPEN','Abierto','Open',@PIPE,1,'#9CA3AF',1),('CrossDockStatus','ALLOCATED','Asignado','Allocated',@PIPE,2,'#F59E0B',0),('CrossDockStatus','COMPLETED','Completado','Completed',@TERM,3,'#059669',0),
('AllocationStatus','PLANNED','Planificada','Planned',@PIPE,1,'#9CA3AF',1),('AllocationStatus','MOVED','Movida','Moved',@TERM,2,'#059669',0),
-- Lote 2: INVITED es la etapa inicial (el flujo de invitación pasa por StatusService), SUSPENDED es lateral reversible y DISABLED terminal
('PortalUserStatus','INVITED','Invitado','Invited',@PIPE,1,'#F59E0B',1),('PortalUserStatus','ACTIVE','Activo','Active',@PIPE,2,'#059669',0),('PortalUserStatus','SUSPENDED','Suspendido','Suspended',@LAT,3,'#EF4444',0),('PortalUserStatus','DISABLED','Inhabilitado','Disabled',@TERM,4,'#6B7280',0),
('InvoiceStatus','DRAFT','Borrador','Draft',@PIPE,1,'#9CA3AF',1),('InvoiceStatus','ISSUED','Emitida','Issued',@PIPE,2,'#3B82F6',0),('InvoiceStatus','PAID','Pagada','Paid',@TERM,3,'#059669',0),('InvoiceStatus','OVERDUE','Vencida','Overdue',@LAT,4,'#EF4444',0),('InvoiceStatus','VOID','Anulada','Void',@TERM,5,'#6B7280',0),
('BillingRunStatus','GENERATED','Generada','Generated',@PIPE,1,'#9CA3AF',1),('BillingRunStatus','REVIEWED','Revisada','Reviewed',@PIPE,2,'#F59E0B',0),('BillingRunStatus','APPROVED','Aprobada','Approved',@PIPE,3,'#3B82F6',0),('BillingRunStatus','EXPORTED','Exportada','Exported',@TERM,4,'#059669',0),
('SettlementStatus','DRAFT','Borrador','Draft',@PIPE,1,'#9CA3AF',1),('SettlementStatus','APPROVED','Aprobada','Approved',@PIPE,2,'#3B82F6',0),('SettlementStatus','PAID','Pagada','Paid',@TERM,3,'#059669',0),
('ApiCredentialStatus','ACTIVE','Activa','Active',@PIPE,1,'#059669',1),('ApiCredentialStatus','REVOKED','Revocada','Revoked',@TERM,2,'#6B7280',0),
('WebhookDeliveryStatus','PENDING','Pendiente','Pending',@PIPE,1,'#9CA3AF',1),('WebhookDeliveryStatus','DELIVERED','Entregado','Delivered',@TERM,2,'#059669',0),('WebhookDeliveryStatus','FAILED','Fallido','Failed',@LAT,3,'#EF4444',0),
('MembershipStatus','ACTIVE','Activa','Active',@PIPE,1,'#059669',1),('MembershipStatus','SUSPENDED','Suspendida','Suspended',@LAT,2,'#EF4444',0),('MembershipStatus','INVITED','Invitada','Invited',@PIPE,3,'#F59E0B',0),
-- COD: estatus de la orden
('CodStatus','PENDING','Por cobrar','To collect',@PIPE,1,'#B97400',1),('CodStatus','PARTIAL','Parcial','Partial',@PIPE,2,'#F59E0B',0),('CodStatus','COLLECTED','Cobrado','Collected',@PIPE,3,'#10B981',0),('CodStatus','RECONCILED','Cuadrado','Reconciled',@PIPE,4,'#0EA5E9',0),('CodStatus','REMITTED','Remitido','Remitted',@TERM,5,'#059669',0),
-- COD: estatus del cobro individual
('CodCollectionStatus','COLLECTED','Cobrado','Collected',@PIPE,1,'#10B981',1),('CodCollectionStatus','RECONCILED','Cuadrado','Reconciled',@PIPE,2,'#0EA5E9',0),('CodCollectionStatus','REMITTED','Remitido','Remitted',@TERM,3,'#059669',0),('CodCollectionStatus','VOID','Anulado','Void',@LAT,4,'#6B7280',0),
-- COD: estatus de la remesa al cliente
('RemittanceStatus','OPEN','Abierta','Open',@PIPE,1,'#9CA3AF',1),('RemittanceStatus','RECONCILED','Cuadrada','Reconciled',@PIPE,2,'#F59E0B',0),('RemittanceStatus','APPROVED','Aprobada','Approved',@PIPE,3,'#3B82F6',0),('RemittanceStatus','REMITTED','Remitida','Remitted',@TERM,4,'#059669',0),
-- Equipos en alquiler: estatus del activo
('RentalAssetStatus','AVAILABLE','Disponible','Available',@PIPE,1,'#10B981',1),('RentalAssetStatus','ON_LEASE','En alquiler','On lease',@PIPE,2,'#3B82F6',0),('RentalAssetStatus','MAINTENANCE','En mantenimiento','In maintenance',@LAT,3,'#F59E0B',0),('RentalAssetStatus','LOST','Perdido','Lost',@LAT,4,'#EF4444',0),('RentalAssetStatus','RETIRED','Retirado','Retired',@TERM,5,'#6B7280',0),
-- Equipos: estatus del contrato de alquiler
('RentalContractStatus','ACTIVE','Activo','Active',@PIPE,1,'#3B82F6',1),('RentalContractStatus','OVERDUE','Vencido','Overdue',@LAT,2,'#EF4444',0),('RentalContractStatus','RETURNED','Devuelto','Returned',@TERM,3,'#10B981',0),('RentalContractStatus','CANCELLED','Cancelado','Cancelled',@TERM,4,'#6B7280',0),
-- Equipos: estatus del cargo recurrente
('RentalChargeStatus','PENDING','Pendiente','Pending',@PIPE,1,'#9CA3AF',1),('RentalChargeStatus','INVOICED','Facturado','Invoiced',@PIPE,2,'#3B82F6',0),('RentalChargeStatus','PAID','Pagado','Paid',@TERM,3,'#10B981',0),
-- Compras: estatus de la orden de compra al proveedor
('PurchaseOrderStatus','DRAFT','Borrador','Draft',@PIPE,1,'#9CA3AF',1),('PurchaseOrderStatus','SENT','Enviada','Sent',@PIPE,2,'#3B82F6',0),('PurchaseOrderStatus','PARTIAL','Recibida parcial','Partially received',@PIPE,3,'#F59E0B',0),('PurchaseOrderStatus','RECEIVED','Recibida','Received',@TERM,4,'#10B981',0),('PurchaseOrderStatus','CANCELLED','Cancelada','Cancelled',@TERM,5,'#6B7280',0),
-- Lote 3 — Órdenes de transporte: lote del importador (validar → confirmar; descartar)
('ImportBatchStatus','VALIDATED','Validado','Validated',@PIPE,1,'#9CA3AF',1),('ImportBatchStatus','CONFIRMED','Confirmado','Confirmed',@PIPE,2,'#059669',0),('ImportBatchStatus','DISCARDED','Descartado','Discarded',@TERM,3,'#6B7280',0),
-- Lote 4 — viaje pagado al chofer: OPEN (por liquidar) → SETTLED (Lote 9) | CANCELLED
('DriverTripStatus','OPEN','Por liquidar','Open',@PIPE,1,'#9CA3AF',1),('DriverTripStatus','SETTLED','Liquidado','Settled',@TERM,2,'#059669',0),('DriverTripStatus','CANCELLED','Cancelado','Cancelled',@TERM,3,'#6B7280',0);

MERGE dbo.StatusCode AS t
USING #S AS s ON t.Entity = s.Entity AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (s.Entity, s.Code, N'{"es":"'+s.Es+'","en":"'+s.En+'"}', s.Color, s.Srt, s.Stage, s.IsInitial, 1);

-- Lote 2: reorden idempotente de PortalUserStatus en BD ya sembradas (el MERGE solo inserta; SUSPENDED entra por el MERGE)
UPDATE dbo.StatusCode
SET IsInitial = CASE InternalCode WHEN 'INVITED' THEN 1 ELSE 0 END,
    SortOrder = CASE InternalCode WHEN 'INVITED' THEN 1 WHEN 'ACTIVE' THEN 2 WHEN 'SUSPENDED' THEN 3 WHEN 'DISABLED' THEN 4 ELSE SortOrder END
WHERE Entity = 'PortalUserStatus' AND InternalCode IN ('INVITED','ACTIVE','SUSPENDED','DISABLED');
GO

/* -------------------------------------------------------------------------
   3B) STATUS CAPABILITY por defecto (TenantId NULL) — Lote 2
       EDIT_CONTRACT no permitido en contratos EXPIRED/CANCELLED (el tenant lo
       puede cambiar desde /status/capabilities/CONTRACT).
   ------------------------------------------------------------------------- */
MERGE dbo.StatusCapability AS t
USING (
    SELECT et.LookupCodeId AS EntityTypeLookupId, s.StatusCodeId, c.LookupCodeId AS CapabilityLookupId
    FROM dbo.LookupCode et
    CROSS JOIN dbo.StatusCode s
    CROSS JOIN dbo.LookupCode c
    WHERE et.Entity='EntityType' AND et.InternalCode='CONTRACT'
      AND s.Entity='ContractStatus' AND s.InternalCode IN ('EXPIRED','CANCELLED')
      AND c.Entity='Capability' AND c.InternalCode='EDIT_CONTRACT'
) AS s
ON t.TenantId IS NULL AND t.EntityTypeLookupId = s.EntityTypeLookupId AND t.StatusCodeId = s.StatusCodeId AND t.CapabilityLookupId = s.CapabilityLookupId
WHEN NOT MATCHED THEN
    INSERT (TenantId, EntityTypeLookupId, StatusCodeId, CapabilityLookupId, IsAllowed)
    VALUES (NULL, s.EntityTypeLookupId, s.StatusCodeId, s.CapabilityLookupId, 0);
GO

/* -------------------------------------------------------------------------
   3C) STATUS CAPABILITY por defecto (TenantId NULL) — Lote 3, TRANSPORT_ORDER
       EDIT_CARGO solo en DRAFT ('editable solo en Entrada', L1063/L1088); REPRICE solo en CONFIRMED;
       CANCEL apagado en DELIVERED/CANCELLED; ASSIGN_TRIP apagado en DRAFT y terminales.
       El tenant lo cambia desde /status/capabilities/TRANSPORT_ORDER.
   ------------------------------------------------------------------------- */
MERGE dbo.StatusCapability AS t
USING (
    SELECT et.LookupCodeId AS EntityTypeLookupId, s.StatusCodeId, c.LookupCodeId AS CapabilityLookupId
    FROM dbo.LookupCode et
    CROSS JOIN dbo.StatusCode s
    CROSS JOIN dbo.LookupCode c
    WHERE et.Entity='EntityType' AND et.InternalCode='TRANSPORT_ORDER'
      AND s.Entity='OrderStatus' AND c.Entity='Capability'
      AND (
           (c.InternalCode='EDIT_CARGO'  AND s.InternalCode IN ('CONFIRMED','PICKUP','INBOUND','PLANNED','IN_TRANSIT','ARRIVED','DELIVERED','ON_HOLD','PARTIAL','FAILED','CANCELLED'))
        OR (c.InternalCode='REPRICE'     AND s.InternalCode IN ('DRAFT','PICKUP','INBOUND','PLANNED','IN_TRANSIT','ARRIVED','DELIVERED','ON_HOLD','PARTIAL','FAILED','CANCELLED'))
        OR (c.InternalCode='CANCEL'      AND s.InternalCode IN ('DELIVERED','CANCELLED'))
        OR (c.InternalCode='ASSIGN_TRIP' AND s.InternalCode IN ('DRAFT','DELIVERED','CANCELLED'))
      )
) AS s
ON t.TenantId IS NULL AND t.EntityTypeLookupId = s.EntityTypeLookupId AND t.StatusCodeId = s.StatusCodeId AND t.CapabilityLookupId = s.CapabilityLookupId
WHEN NOT MATCHED THEN
    INSERT (TenantId, EntityTypeLookupId, StatusCodeId, CapabilityLookupId, IsAllowed)
    VALUES (NULL, s.EntityTypeLookupId, s.StatusCodeId, s.CapabilityLookupId, 0);
GO

/* -------------------------------------------------------------------------
   3D) STATUS CAPABILITY por defecto (TenantId NULL) — Lote 4, WORK_ORDER
       EDIT_WORK_ORDER no permitido en órdenes de trabajo CLOSED/CANCELLED (el
       tenant lo puede cambiar desde /status/capabilities/WORK_ORDER).
   ------------------------------------------------------------------------- */
MERGE dbo.StatusCapability AS t
USING (
    SELECT et.LookupCodeId AS EntityTypeLookupId, s.StatusCodeId, c.LookupCodeId AS CapabilityLookupId
    FROM dbo.LookupCode et
    CROSS JOIN dbo.StatusCode s
    CROSS JOIN dbo.LookupCode c
    WHERE et.Entity='EntityType' AND et.InternalCode='WORK_ORDER'
      AND s.Entity='WorkOrderStatus' AND s.InternalCode IN ('CLOSED','CANCELLED')
      AND c.Entity='Capability' AND c.InternalCode='EDIT_WORK_ORDER'
) AS s
ON t.TenantId IS NULL AND t.EntityTypeLookupId = s.EntityTypeLookupId AND t.StatusCodeId = s.StatusCodeId AND t.CapabilityLookupId = s.CapabilityLookupId
WHEN NOT MATCHED THEN
    INSERT (TenantId, EntityTypeLookupId, StatusCodeId, CapabilityLookupId, IsAllowed)
    VALUES (NULL, s.EntityTypeLookupId, s.StatusCodeId, s.CapabilityLookupId, 0);
GO

/* -------------------------------------------------------------------------
   3E) STATUS CAPABILITY por defecto (TenantId NULL) — Lote 5, TRIP
       EDIT_TRIP (cabecera: chofer, vehículo, hora de salida) no permitido en
       rutas DISPATCHED/IN_PROGRESS/COMPLETED/CANCELLED. El tenant la puede
       habilitar en DISPATCHED/IN_PROGRESS desde /status/capabilities/TRIP
       (la fecha, la zona y el contenido de una ruta despachada nunca cambian).
   ------------------------------------------------------------------------- */
MERGE dbo.StatusCapability AS t
USING (
    SELECT et.LookupCodeId AS EntityTypeLookupId, s.StatusCodeId, c.LookupCodeId AS CapabilityLookupId
    FROM dbo.LookupCode et
    CROSS JOIN dbo.StatusCode s
    CROSS JOIN dbo.LookupCode c
    WHERE et.Entity='EntityType' AND et.InternalCode='TRIP'
      AND s.Entity='TripStatus' AND s.InternalCode IN ('DISPATCHED','IN_PROGRESS','COMPLETED','CANCELLED')
      AND c.Entity='Capability' AND c.InternalCode='EDIT_TRIP'
) AS s
ON t.TenantId IS NULL AND t.EntityTypeLookupId = s.EntityTypeLookupId AND t.StatusCodeId = s.StatusCodeId AND t.CapabilityLookupId = s.CapabilityLookupId
WHEN NOT MATCHED THEN
    INSERT (TenantId, EntityTypeLookupId, StatusCodeId, CapabilityLookupId, IsAllowed)
    VALUES (NULL, s.EntityTypeLookupId, s.StatusCodeId, s.CapabilityLookupId, 0);
GO

/* -------------------------------------------------------------------------
   3F) STATUS LATERAL ENTRY por defecto (TenantId NULL) — Lote 5
       TRIP: CANCELLED ('Eliminar ruta') solo desde DRAFT y PLANNED (una ruta
       despachada no se elimina; TripStatusEffect lo vuelve a exigir).
       ROUTE: ARCHIVED solo desde DRAFT y OPTIMIZED (la versión ACTIVE está
       congelada). El tenant lo cambia desde /status/lateral-entries/{TRIP|ROUTE}.
   ------------------------------------------------------------------------- */
MERGE dbo.StatusLateralEntry AS t
USING (
    SELECT et.LookupCodeId AS EntityTypeLookupId, lat.StatusCodeId AS LateralStatusCodeId, frm.StatusCodeId AS FromStatusCodeId
    FROM dbo.LookupCode et
    CROSS JOIN dbo.StatusCode lat
    CROSS JOIN dbo.StatusCode frm
    WHERE et.Entity='EntityType'
      AND (
           (et.InternalCode='TRIP'  AND lat.Entity='TripStatus'  AND lat.InternalCode='CANCELLED'
                                    AND frm.Entity='TripStatus'  AND frm.InternalCode IN ('DRAFT','PLANNED'))
        OR (et.InternalCode='ROUTE' AND lat.Entity='RouteStatus' AND lat.InternalCode='ARCHIVED'
                                    AND frm.Entity='RouteStatus' AND frm.InternalCode IN ('DRAFT','OPTIMIZED'))
      )
) AS s
ON t.TenantId IS NULL AND t.EntityTypeLookupId = s.EntityTypeLookupId AND t.LateralStatusCodeId = s.LateralStatusCodeId AND t.FromStatusCodeId = s.FromStatusCodeId
WHEN NOT MATCHED THEN
    INSERT (TenantId, EntityTypeLookupId, LateralStatusCodeId, FromStatusCodeId, IsAllowed)
    VALUES (NULL, s.EntityTypeLookupId, s.LateralStatusCodeId, s.FromStatusCodeId, 1);
GO

/* -------------------------------------------------------------------------
   4) PERMISOS  (vocabulario de la app — sembrado desde código)
   ------------------------------------------------------------------------- */
IF OBJECT_ID('tempdb..#P') IS NOT NULL DROP TABLE #P;
CREATE TABLE #P (Code NVARCHAR(80), Cat NVARCHAR(40), Es NVARCHAR(120), En NVARCHAR(120));
INSERT INTO #P VALUES
('orders.view','ORDERS','Ver órdenes','View orders'),('orders.create','ORDERS','Crear órdenes','Create orders'),
('orders.edit','ORDERS','Editar órdenes','Edit orders'),('orders.cancel','ORDERS','Cancelar órdenes','Cancel orders'),
('trips.plan','TRIPS','Planificar trips','Plan trips'),('trips.dispatch','TRIPS','Despachar trips','Dispatch trips'),
('trips.optimize','TRIPS','Optimizar rutas','Optimize routes'),
('warehouse.receive','WAREHOUSE','Recibir','Receive'),('warehouse.pick','WAREHOUSE','Pickear','Pick'),
('warehouse.count','WAREHOUSE','Contar','Count'),('warehouse.crossdock','WAREHOUSE','Cross-dock','Cross-dock'),
('billing.generate','BILLING','Generar facturación','Generate billing'),('billing.approve','BILLING','Aprobar facturación','Approve billing'),
('billing.export','BILLING','Exportar facturación','Export billing'),
('fleet.manage','FLEET','Gestionar flota','Manage fleet'),('fleet.maintenance','FLEET','Mantenimiento','Maintenance'),
('admin.users','ADMIN','Gestionar usuarios','Manage users'),('admin.roles','ADMIN','Gestionar roles','Manage roles'),
('admin.catalogs','ADMIN','Gestionar catálogos','Manage catalogs'),('admin.tenant','ADMIN','Configurar tenant','Configure tenant'),
('cod.collect','COD','Cobrar COD en entrega','Collect COD on delivery'),('cod.reconcile','COD','Reconciliar COD','Reconcile COD'),('cod.remit','COD','Generar remesas COD','Generate COD remittances'),('cod.view','COD','Ver COD','View COD'),
('rental.view','RENTAL','Ver equipos en alquiler','View rental equipment'),('rental.manage','RENTAL','Gestionar contratos de alquiler','Manage rental contracts'),('rental.maintenance','RENTAL','Registrar mantenimiento de equipo','Log equipment maintenance'),('rental.billing','RENTAL','Generar cargos de alquiler','Generate rental charges'),
('purchasing.view','PURCHASING','Ver órdenes de compra','View purchase orders'),('purchasing.manage','PURCHASING','Crear y editar órdenes de compra','Create and edit purchase orders'),('purchasing.receive','PURCHASING','Recibir contra orden de compra','Receive against purchase order'),
-- Capas transversales (E, F, G/H/I, C)
('admin.audit','ADMIN','Ver seguridad y auditoría','View security & audit'),('admin.customfields','ADMIN','Gestionar campos personalizados','Manage custom fields'),
('admin.statusconfig','ADMIN','Configurar pipeline de estatus','Configure status pipeline'),('contacts.manage','ADMIN','Gestionar contactos','Manage contacts'),
('analytics.view','ANALYTICS','Ver vistas, indicadores y gráficos','View reports, indicators & charts'),('analytics.manage','ANALYTICS','Crear vistas, indicadores y gráficos','Create reports, indicators & charts'),
('analytics.dates','ANALYTICS','Cambiar rango de fecha de indicadores/gráficos ajenos','Change date range of others'' indicators/charts'),
-- Lote 2 — Clientes y contratos (categoría CLIENTS; portalusers.manage es distinto de admin.users, R40)
('clients.read','CLIENTS','Ver clientes','View clients'),('clients.create','CLIENTS','Crear clientes','Create clients'),('clients.update','CLIENTS','Editar clientes','Edit clients'),
('locations.read','CLIENTS','Ver consignatarios','View locations'),('locations.create','CLIENTS','Crear consignatarios','Create locations'),('locations.update','CLIENTS','Editar consignatarios','Edit locations'),
('contracts.read','CLIENTS','Ver contratos y tarifas','View contracts & rates'),('contracts.create','CLIENTS','Crear contratos','Create contracts'),('contracts.update','CLIENTS','Editar contratos y tarifas','Edit contracts & rates'),
('portalusers.manage','CLIENTS','Administrar usuarios de portal del cliente','Manage client portal users'),
-- Lote 3 — Órdenes de transporte (ajuste C: crédito excedido = aviso + autorización con permiso)
('orders.credit_override','ORDERS','Autorizar órdenes sobre el límite de crédito','Authorize orders over credit limit'),
-- Lote 4 — Flota, choferes y mantenimiento (flota separada de la compensación de choferes, R8)
('fleet.view','FLEET','Ver flota y choferes','View fleet & drivers'),
('driverpay.view','FLEET','Ver tarifas y viajes de choferes','View driver rates & trips'),
('driverpay.manage','FLEET','Gestionar tarifas y viajes de choferes','Manage driver rates & trips'),
-- Lote 5 — Trips y rutas (leer rutas y escanear la salida sin poder planificar)
('trips.view','TRIPS','Ver rutas y despacho','View trips & dispatch'),
('trips.scan','TRIPS','Escanear salida (Outbound)','Scan outbound');

MERGE dbo.Permission AS t
USING #P AS s ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code,
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity='PermissionCategory' AND InternalCode=s.Cat),
            N'{"es":"'+s.Es+'","en":"'+s.En+'"}', 1);
GO

/* -------------------------------------------------------------------------
   5) ROLES PLANTILLA (TenantId NULL) + permisos por defecto
   ------------------------------------------------------------------------- */
IF OBJECT_ID('tempdb..#R') IS NOT NULL DROP TABLE #R;
CREATE TABLE #R (Name NVARCHAR(80), Es NVARCHAR(120), En NVARCHAR(120));
INSERT INTO #R VALUES
('TenantAdmin','Admin de tenant','Tenant admin'),('Dispatcher','Despachador','Dispatcher'),
('Billing','Facturación','Billing'),('WarehouseOperator','Operador de almacén','Warehouse operator'),
('Driver','Chofer','Driver'),('ReadOnly','Solo lectura','Read only');

MERGE dbo.Role AS t
USING #R AS s ON t.TenantId IS NULL AND t.Name = s.Name
WHEN NOT MATCHED THEN
    INSERT (TenantId, Name, DescriptionJson, IsSystem, IsActive)
    VALUES (NULL, s.Name, N'{"es":"'+s.Es+'","en":"'+s.En+'"}', 1, 1);
GO

-- Mapeo rol→permiso (plantillas)
IF OBJECT_ID('tempdb..#RP') IS NOT NULL DROP TABLE #RP;
CREATE TABLE #RP (RoleName NVARCHAR(80), PermCode NVARCHAR(80));
-- TenantAdmin: todos
INSERT INTO #RP SELECT 'TenantAdmin', Code FROM #P;
-- Dispatcher
INSERT INTO #RP VALUES ('Dispatcher','orders.view'),('Dispatcher','orders.create'),('Dispatcher','orders.edit'),('Dispatcher','orders.cancel'),('Dispatcher','trips.plan'),('Dispatcher','trips.dispatch'),('Dispatcher','trips.optimize'),
('Dispatcher','clients.read'),('Dispatcher','locations.read'),('Dispatcher','locations.create'),   -- Lote 2
('Dispatcher','fleet.view'),   -- Lote 4
('Dispatcher','trips.view'),('Dispatcher','trips.scan');   -- Lote 5
-- Billing
INSERT INTO #RP VALUES ('Billing','orders.view'),('Billing','billing.generate'),('Billing','billing.approve'),('Billing','billing.export'),('Billing','cod.view'),('Billing','cod.reconcile'),('Billing','cod.remit'),('Billing','rental.billing'),('Billing','rental.view'),('Billing','purchasing.view'),('Billing','purchasing.manage'),
('Billing','clients.read'),('Billing','contracts.read'),   -- Lote 2
('Billing','orders.credit_override'),   -- Lote 3
('Billing','driverpay.view');   -- Lote 4
-- WarehouseOperator
INSERT INTO #RP VALUES ('WarehouseOperator','warehouse.receive'),('WarehouseOperator','warehouse.pick'),('WarehouseOperator','warehouse.count'),('WarehouseOperator','warehouse.crossdock'),('WarehouseOperator','cod.reconcile'),('WarehouseOperator','rental.view'),('WarehouseOperator','rental.manage'),('WarehouseOperator','rental.maintenance'),('WarehouseOperator','purchasing.view'),('WarehouseOperator','purchasing.receive'),
('WarehouseOperator','trips.view'),('WarehouseOperator','trips.scan');   -- Lote 5
-- Driver
INSERT INTO #RP VALUES ('Driver','orders.view'),('Driver','cod.collect');
-- ReadOnly
INSERT INTO #RP VALUES ('ReadOnly','orders.view'),('ReadOnly','cod.view'),
('ReadOnly','clients.read'),('ReadOnly','locations.read'),('ReadOnly','contracts.read'),   -- Lote 2
('ReadOnly','fleet.view'),   -- Lote 4
('ReadOnly','trips.view');   -- Lote 5

MERGE dbo.RolePermission AS t
USING (
    SELECT r.RoleId, p.PermissionId
    FROM #RP rp
    JOIN dbo.Role r ON r.TenantId IS NULL AND r.Name = rp.RoleName
    JOIN dbo.Permission p ON p.Code = rp.PermCode
) AS s ON t.RoleId = s.RoleId AND t.PermissionId = s.PermissionId
WHEN NOT MATCHED THEN INSERT (RoleId, PermissionId) VALUES (s.RoleId, s.PermissionId);
GO

/* -------------------------------------------------------------------------
   9) ZONAS DE DESPACHO DEMO (R-01..R-04) para el tenant de Advance
   ------------------------------------------------------------------------- */
DECLARE @DemoTenantId2 INT = (SELECT TOP 1 TenantId FROM dbo.Tenant ORDER BY TenantId);
DECLARE @ZM_ZIP INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity='ZoneMatchType' AND InternalCode='POSTAL_CODE');
IF @DemoTenantId2 IS NOT NULL AND @ZM_ZIP IS NOT NULL
BEGIN
    MERGE dbo.DispatchZone AS t
    USING (VALUES ('R-01','Toa Baja / Bayamón'),('R-02','Caguas / Aguas Buenas'),('R-03','Carolina / Trujillo Alto'),('R-04','Guaynabo / Condado')
    ) AS s(Code,Name)
    ON t.TenantId=@DemoTenantId2 AND t.Code=s.Code
    WHEN MATCHED THEN UPDATE SET Name=s.Name
    WHEN NOT MATCHED THEN INSERT (TenantId,Code,Name) VALUES (@DemoTenantId2,s.Code,s.Name);

    ;WITH zm(Code,Zip) AS (SELECT * FROM (VALUES
        ('R-01','00949'),('R-01','00959'),('R-02','00725'),('R-02','00703'),
        ('R-03','00979'),('R-03','00976'),('R-04','00969'),('R-04','00907')
    ) v(Code,Zip))
    INSERT INTO dbo.DispatchZoneMember (DispatchZoneId, MatchTypeLookupId, MatchValue)
    SELECT z.DispatchZoneId, @ZM_ZIP, zm.Zip
    FROM zm JOIN dbo.DispatchZone z ON z.TenantId=@DemoTenantId2 AND z.Code=zm.Code
    WHERE NOT EXISTS (SELECT 1 FROM dbo.DispatchZoneMember x WHERE x.DispatchZoneId=z.DispatchZoneId AND x.MatchValue=zm.Zip);
END
GO

PRINT 'Seed completado: módulos (13), dominios, lookups, estatus, capacidades por defecto (CONTRACT, TRANSPORT_ORDER, WORK_ORDER y TRIP), entradas laterales (TRIP y ROUTE), permisos (54), roles plantilla y zonas de despacho demo.';
GO
