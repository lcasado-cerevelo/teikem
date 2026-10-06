using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Seeding;

/// <summary>
/// Vistas, indicadores y gráficos por default (IsSystem=1) que un tenant trae de fábrica. En el Lote 1 usan las fuentes
/// transversales (AUDIT_LOG, SECURITY_EVENT, USER); el Lote 2 agrega Clientes y contratos (CLIENT, CONTRACT); el Lote 3
/// agrega Órdenes (TRANSPORT_ORDER); el Lote 4 agrega Flota (VEHICLE, FLEET_DOCUMENT, WORK_ORDER); el Lote 5 agrega Rutas (TRIP) e
/// indicadores de despacho sobre TRANSPORT_ORDER; el Lote 6 agrega Inventario y almacén (STOCK_BALANCE, PRODUCT,
/// INVENTORY_TRANSACTION, RECEIPT, WAREHOUSE_TASK) en el módulo WAREHOUSE; el Lote 7A agrega los indicadores y el gráfico
/// de almacén del Pulso (INVENTORY_TRANSACTION, CYCLE_COUNT); cada lote de negocio agrega los suyos.
/// Idempotente por nombre. Lote 15: los 2 gráficos de almacén del Pulso son DE LA COMPAÑÍA (no de sistema, sin dueño) y además
/// idempotentes por clave de siembra (ChartDefinition.SeedKey): ni renombrados ni borrados se vuelven a crear; se agrega el
/// indicador 'Descuadres pendientes' (apagado en el Pulso). Lote 29 (Rentas R3): reportes, indicadores y el gráfico de rentas
/// (RENTAL, RENTAL_RETURN, RENTAL_PROCESS); se siembran en toda compañía y se ocultan con el módulo Rentas apagado.
/// </summary>
public sealed class SystemAnalyticsSeeder(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    /// <summary>Filtro vigente de 'COD por cobrar': COD PENDING de órdenes activas que no estén canceladas.</summary>
    public const string CodPendingFilter =
        "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"CodStatusCode\",\"op\":\"eq\",\"value\":\"PENDING\"},{\"field\":\"StatusCode\",\"op\":\"neq\",\"value\":\"CANCELLED\"}]}";

    /// <summary>Filtro sembrado por la primera versión del Lote 3 (sumaba el COD de órdenes canceladas); se corrige al resembrar.</summary>
    public const string CodPendingFilterV1 =
        "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"CodStatusCode\",\"op\":\"eq\",\"value\":\"PENDING\"}]}";

    /// <summary>Lote 4: documentos vencidos o por vencer (ventana fija de 30 días de la fuente FLEET_DOCUMENT).</summary>
    public const string FleetDocumentsDueFilter =
        "{\"and\":[{\"field\":\"ExpiryState\",\"op\":\"in\",\"value\":[\"EXPIRED\",\"EXPIRING\"]}]}";

    /// <summary>Lote 4: órdenes de trabajo activas abiertas o en proceso.</summary>
    public const string OpenWorkOrdersFilter =
        "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"StatusCode\",\"op\":\"in\",\"value\":[\"OPEN\",\"IN_PROGRESS\"]}]}";

    /// <summary>
    /// Lote 5: órdenes activas pendientes de despacho (CONFIRMED..PLANNED) sin chofer asignado: ni ruta vigente con chofer ni
    /// DriverTrip activo (campo HasAssignedDriver de TransportOrderDataSource).
    /// </summary>
    public const string OrdersWithoutDriverFilter =
        "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"StatusCode\",\"op\":\"in\",\"value\":[\"CONFIRMED\",\"PICKUP\",\"INBOUND\",\"PLANNED\"]},{\"field\":\"HasAssignedDriver\",\"op\":\"isFalse\"}]}";

    /// <summary>Lote 5: órdenes activas en excepción (ON_HOLD, PARTIAL, FAILED; CANCELLED no cuenta).</summary>
    public const string OrdersInExceptionFilter =
        "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"IsException\",\"op\":\"isTrue\"}]}";

    /// <summary>Lote 5: rutas activas no cerradas cuyo número de paradas pasa el máximo del chofer.</summary>
    public const string TripsOverStopLimitFilter =
        "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"StatusCode\",\"op\":\"in\",\"value\":[\"DRAFT\",\"PLANNED\",\"DISPATCHED\",\"IN_PROGRESS\"]},{\"field\":\"OverStopLimit\",\"op\":\"isTrue\"}]}";

    /// <summary>Lote 6: productos activos por debajo de su mínimo (IsBelowMin de ProductDataSource, como el mock).</summary>
    public const string ProductsBelowMinFilter = "{\"and\":[{\"field\":\"IsBelowMin\",\"op\":\"isTrue\"}]}";

    /// <summary>Lote 6: productos activos.</summary>
    public const string ActiveProductsFilter = "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"}]}";

    /// <summary>Lote 6: movimientos de ajuste del ledger ('Ajustes de inventario').</summary>
    public const string AdjustmentsFilter = "{\"and\":[{\"field\":\"TxnTypeCode\",\"op\":\"eq\",\"value\":\"ADJUSTMENT\"}]}";

    /// <summary>Lote 6: saldos con existencia de lotes que vencen en 30 días o menos (o ya vencidos).</summary>
    public const string ExpiringSoonFilter =
        "{\"and\":[{\"field\":\"ExpiryDate\",\"op\":\"notnull\"},{\"field\":\"DaysToExpiry\",\"op\":\"lte\",\"value\":30},{\"field\":\"QtyOnHand\",\"op\":\"gt\",\"value\":0}]}";

    /// <summary>Lote 6: recibos con diferencia entre lo esperado y lo recibido.</summary>
    public const string ReceiptsWithVarianceFilter = "{\"and\":[{\"field\":\"HasVariance\",\"op\":\"isTrue\"}]}";

    /// <summary>Lote 6: tareas de almacén abiertas (pendientes o en proceso).</summary>
    public const string OpenWarehouseTasksFilter = "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"in\",\"value\":[\"PENDING\",\"IN_PROGRESS\"]}]}";

    /// <summary>
    /// Lote 7A: 'Unidades recibidas' = RECEIPT más los ajustes RECEIPT_VARIANCE del ledger. Por D4 una línea con esperado E se
    /// asienta como RECEIPT por E más un ADJUSTMENT RECEIPT_VARIANCE por la diferencia (y una línea extra solo como ajuste):
    /// el neto con signo de ambos es lo recibido.
    /// </summary>
    public const string ReceiptMovementsFilter =
        "{\"or\":[{\"field\":\"TxnTypeCode\",\"op\":\"eq\",\"value\":\"RECEIPT\"},{\"and\":[{\"field\":\"TxnTypeCode\",\"op\":\"eq\",\"value\":\"ADJUSTMENT\"},{\"field\":\"ReasonCode\",\"op\":\"eq\",\"value\":\"RECEIPT_VARIANCE\"}]}]}";

    /// <summary>Primera versión del filtro de 'Unidades recibidas' (solo RECEIPT: sumaba lo esperado); se corrige al resembrar.</summary>
    public const string ReceiptMovementsFilterV1 = "{\"and\":[{\"field\":\"TxnTypeCode\",\"op\":\"eq\",\"value\":\"RECEIPT\"}]}";

    /// <summary>
    /// Lote 14 (D7): conteos cerrados con diferencia ('Conteos con diferencia') = estatus RECONCILED_VARIANCE 'Diferencia' (alguna
    /// línea asentó un ajuste; los históricos se reetiquetaron en el seed). No se usa NetVariance ≠ 0 porque sobrantes y
    /// faltantes de un mismo conteo pueden compensarse y el conteo sí tuvo diferencia.
    /// </summary>
    public const string ReconciledCountsWithVarianceFilter =
        "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"RECONCILED_VARIANCE\"}]}";

    /// <summary>
    /// Lote 7A: primera versión (RECONCILED + HasVariance de CycleCountDataSource); desde el Lote 14 esos conteos son
    /// RECONCILED_VARIANCE y el filtro viejo no contaría ninguno. Se corrige al resembrar (y en el seed SQL para las compañías
    /// ya creadas).
    /// </summary>
    public const string ReconciledCountsWithVarianceFilterV1 =
        "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"RECONCILED\"},{\"field\":\"HasVariance\",\"op\":\"isTrue\"}]}";

    public const string ReceivedUnitsIndicatorName = "Unidades recibidas";
    public const string CountsWithVarianceIndicatorName = "Conteos con diferencia";
    public const string MovementsByTypeChartName = "Movimientos de inventario por tipo";

    /// <summary>Lote 15 (D9): nombre del gráfico de valor de inventario (se conserva al convertirlo).</summary>
    public const string InventoryValueChartName = "Valor de inventario por categoría";

    /// <summary>Lote 15 (D12): descripción del gráfico de movimientos en unidades positivas (la anterior hablaba de cantidad con signo).</summary>
    public const string MovementsByTypeChartDescriptionEs = "Unidades movidas en el período por tipo de movimiento (siempre en positivo)";
    public const string MovementsByTypeChartDescriptionEn = "Units moved in the period by movement type (always positive)";

    /// <summary>Lote 15 (D16): indicador 'Descuadres pendientes' (apagado en el Pulso: "Necesita tu atención" ya los muestra).</summary>
    public const string PendingDiscrepanciesIndicatorName = "Descuadres pendientes";
    public const string PendingDiscrepanciesFilter = "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"OPEN\"}]}";

    /// <summary>Lote 15 (D13): orden de los 2 gráficos de la compañía en "Tus gráficos" (primera fila: valor a la izquierda).</summary>
    public const int InventoryValueChartSortOrder = 1;
    public const int MovementsByTypeChartSortOrder = 2;
    /// <summary>Órdenes con los que se sembraron de sistema (Lote 6 y 7A): solo se mueven a 1 y 2 si siguen ahí.</summary>
    public const int InventoryValueChartSortOrderV1 = 90;
    public const int MovementsByTypeChartSortOrderV1 = 96;

    /// <summary>Lote 6 (bitácora del maestro L887, que amplía la L874): 'Movimientos por tipo y producto', agrupada por tipo y
    /// SKU (dos campos de agrupación) con conteo, suma de cantidad (con signo del ledger) y fila de totales.</summary>
    public const string MovementsByTypeGroup = "{\"by\":[\"TxnType\",\"Sku\"],\"aggregates\":[{\"fn\":\"COUNT\"},{\"fn\":\"SUM\",\"field\":\"Quantity\"}],\"totals\":true}";

    public const string MovementsByTypeDescriptionEs = "Conteo y suma de cantidad (con signo) de los movimientos por tipo y producto";
    public const string MovementsByTypeDescriptionEn = "Count and signed quantity sum of movements by type and product";

    /// <summary>Nombre vigente de la vista agrupada de movimientos (maestro L887).</summary>
    public const string MovementsByTypeReportName = "Movimientos por tipo y producto";

    /// <summary>Versión anterior (L874) sembrada en tenants ya aprovisionados: solo se corrige la vista de sistema que la conserve exacta.</summary>
    public const string MovementsByTypeReportNameV1 = "Movimientos por tipo";
    public const string MovementsByTypeGroupV1 = "{\"by\":[\"TxnType\"],\"aggregates\":[{\"fn\":\"COUNT\"},{\"fn\":\"SUM\",\"field\":\"Quantity\"}],\"totals\":true}";

    public async Task SeedForTenantAsync(int tenantId, CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        using var _ = tc.As(tenantId);
        db.SuppressAudit = true;

        var visTenant = await lookups.GetIdAsync(LookupDomains.ReportVisibility, ReportVisibilities.Tenant, ct);
        var count = await lookups.GetIdAsync(LookupDomains.AggregateFn, AggregateFns.Count, ct);
        var sum = await lookups.GetIdAsync(LookupDomains.AggregateFn, AggregateFns.Sum, ct);
        var ops = await lookups.GetIdAsync(LookupDomains.BusinessModule, BusinessModules.Operations, ct);
        var acct = await lookups.GetIdAsync(LookupDomains.BusinessModule, BusinessModules.Accounting, ct);
        var wh = await lookups.GetIdAsync(LookupDomains.BusinessModule, BusinessModules.Warehouse, ct);
        var last7 = await lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.Last7, ct);
        var last30 = await lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.Last30, ct);
        var all = await lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.All, ct);
        var bar = await lookups.GetIdAsync(LookupDomains.ReportChartType, ChartTypes.Bar, ct);
        var donut = await lookups.GetIdAsync(LookupDomains.ReportChartType, ChartTypes.Donut, ct);
        var line = await lookups.GetIdAsync(LookupDomains.ReportChartType, ChartTypes.Line, ct);

        // ---- Vistas ----
        // Lote 6: corrección idempotente de tenants sembrados con la vista 'Movimientos por tipo' (L874, un solo campo): se renombra
        // a 'Movimientos por tipo y producto' (L887) con dos campos de agrupación. Solo la vista de sistema que conserve el nombre
        // y el GroupJson originales exactos, y solo si aún no existe la nueva (así nunca quedan las dos).
        var hasNewMovements = await db.ReportDefinitions.AnyAsync(r => r.TenantId == tenantId && r.Name == MovementsByTypeReportName, ct);
        if (!hasNewMovements)
        {
            var oldMovements = await db.ReportDefinitions
                .Where(r => r.TenantId == tenantId && r.IsSystem && r.Name == MovementsByTypeReportNameV1 && r.GroupJson == MovementsByTypeGroupV1)
                .ToListAsync(ct);
            foreach (var r in oldMovements.Take(1))
            {
                r.Name = MovementsByTypeReportName;
                r.GroupJson = MovementsByTypeGroup;
                r.DescriptionJson = MultilingualText.Build(MovementsByTypeDescriptionEs, MovementsByTypeDescriptionEn);
            }
            if (oldMovements.Count > 0) await db.SaveChangesAsync(ct);
        }
        var existingReports = await db.ReportDefinitions.Where(r => r.TenantId == tenantId).Select(r => r.Name).ToListAsync(ct);
        async Task Report(string entityType, string name, string es, string en, string[] columns, string? filter, string? group, string? sort)
        {
            if (existingReports.Contains(name)) return;
            db.ReportDefinitions.Add(new ReportDefinition
            {
                TenantId = tenantId, BaseEntityTypeLookupId = await lookups.GetIdAsync(LookupDomains.EntityType, entityType, ct), Name = name,
                DescriptionJson = MultilingualText.Build(es, en), VisibilityLookupId = visTenant, IsSystem = true, OwnerUserId = null,
                ColumnsJson = JsonSerializer.Serialize(columns), FilterJson = filter, GroupJson = group, SortJson = sort,
            });
        }
        await Report(EntityTypes.AuditLog, "Actividad reciente", "Últimos cambios registrados en la compañía", "Latest recorded changes",
            new[] { "CreatedAtUtc", "Action", "EntityType", "EntityId", "UserName", "CorrelationId" }, null, null, "[{\"field\":\"CreatedAtUtc\",\"dir\":\"desc\"}]");
        await Report(EntityTypes.SecurityEvent, "Accesos fallidos", "Intentos de acceso fallidos o bloqueados", "Failed or blocked access attempts",
            new[] { "CreatedAtUtc", "EventType", "Outcome", "UserName", "IpAddress" }, "{\"and\":[{\"field\":\"OutcomeCode\",\"op\":\"in\",\"value\":[\"FAILURE\",\"BLOCKED\"]}]}", null, "[{\"field\":\"CreatedAtUtc\",\"dir\":\"desc\"}]");
        await Report(EntityTypes.User, "Usuarios de la compañía", "Directorio de usuarios con roles y membresía", "User directory with roles and membership",
            new[] { "FullName", "Email", "Roles", "MembershipStatus", "IsActive", "MfaEnabled", "LastLoginUtc" }, null, null, "[{\"field\":\"FullName\",\"dir\":\"asc\"}]");
        await Report(EntityTypes.AuditLog, "Cambios por usuario y acción", "Conteo de cambios agrupado por usuario y acción", "Change count grouped by user and action",
            Array.Empty<string>(), null, "{\"by\":[\"UserName\",\"Action\"],\"aggregates\":[{\"fn\":\"COUNT\"}],\"totals\":true}", null);
        // Lote 2 — Clientes y contratos
        await Report(EntityTypes.Client, "Clientes", "Directorio de clientes con estatus y modelo de facturación vigente", "Client directory with status and current billing model",
            new[] { "Code", "Name", "Status", "BillingSummary", "CreditLimit", "IsActive" }, null, null, "[{\"field\":\"Name\",\"dir\":\"asc\"}]");
        // Lote 3 — Órdenes de transporte (los nombres de campo son los de TransportOrderDataSource; no cambiarlos sin cambiar ambos)
        await Report(EntityTypes.TransportOrder, "Órdenes", "Órdenes de transporte con empaque, consignatario, estatus y COD", "Transport orders with pack batch, consignee, status and COD",
            new[] { "PackBatchNumber", "OrderNumber", "ClientInvoiceNumber", "ClientName", "ConsigneeName", "Status", "TotalPieces", "CodAmount", "CreatedAtUtc" }, null, null, "[{\"field\":\"CreatedAtUtc\",\"dir\":\"desc\"}]");
        // Lote 4 — Flota (los nombres de campo son los de VehicleDataSource y FleetDocumentDataSource; no cambiarlos sin cambiar ambos)
        await Report(EntityTypes.Vehicle, "Vehículos", "Flota con tipo, propiedad, combustible, odómetro y estatus", "Fleet with type, ownership, fuel, odometer and status",
            new[] { "Code", "PlateNumber", "VehicleType", "Ownership", "FuelType", "CurrentOdometerKm", "Status", "IsActive" }, null, null, "[{\"field\":\"Code\",\"dir\":\"asc\"}]");
        await Report(EntityTypes.FleetDocument, "Documentos por vencer", "Documentos de vehículos y choferes vencidos o que vencen en 30 días", "Vehicle and driver documents expired or expiring within 30 days",
            new[] { "ExpiryDate", "OwnerKind", "OwnerCode", "OwnerName", "DocumentType", "DocNumber", "DaysToExpiry", "ExpiryState" }, FleetDocumentsDueFilter, null, "[{\"field\":\"ExpiryDate\",\"dir\":\"asc\"}]");
        // Lote 5 — Rutas (los nombres de campo son los de TripDataSource; no cambiarlos sin cambiar ambos)
        await Report(EntityTypes.Trip, "Rutas", "Rutas por fecha con zona, chofer, vehículo, estatus, paradas y distancia", "Trips by date with zone, driver, vehicle, status, stops and distance",
            new[] { "PlanDate", "Code", "ZoneCode", "DriverName", "VehicleCode", "Status", "StopCount", "OverStopLimit", "TotalDistanceKm" }, null, null, "[{\"field\":\"PlanDate\",\"dir\":\"desc\"}]");
        // Lote 6 — Inventario y almacén (los nombres de campo son los de las fuentes STOCK_BALANCE, PRODUCT, INVENTORY_TRANSACTION y
        // RECEIPT; no cambiarlos sin cambiar ambos). Quantity del Kárdex viene CON signo (D3).
        await Report(EntityTypes.StockBalance, "Inventario", "Saldo por almacén, posición y lote con disponible y valor a costo", "Stock by warehouse, bin and lot with available and cost value",
            new[] { "WarehouseCode", "BinCode", "Sku", "ProductName", "OwnerName", "LotNumber", "ExpiryDate", "QtyOnHand", "QtyReserved", "QtyAvailable", "CostValue" }, null, null, "[{\"field\":\"Sku\",\"dir\":\"asc\"}]");
        await Report(EntityTypes.Product, "Inventario bajo mínimo", "Productos activos con disponible por debajo de su mínimo", "Active products with available below their minimum",
            new[] { "Sku", "Name", "OwnerName", "Category", "QtyOnHand", "QtyAvailable", "MinQty" }, ProductsBelowMinFilter, null, "[{\"field\":\"Sku\",\"dir\":\"asc\"}]");
        await Report(EntityTypes.Product, "Productos por cliente dueño", "Productos activos por dueño del inventario (propio o cliente 3PL)", "Active products by inventory owner (own or 3PL client)",
            new[] { "OwnerName", "Sku", "Name", "QtyOnHand", "QtyAvailable" }, ActiveProductsFilter, null, "[{\"field\":\"OwnerName\",\"dir\":\"asc\"}]");
        await Report(EntityTypes.InventoryTransaction, "Kárdex de movimientos", "Movimientos del ledger con cantidad con signo, origen y usuario", "Ledger movements with signed quantity, source and user",
            new[] { "CreatedAtUtc", "TxnType", "Sku", "ProductName", "Quantity", "Position", "LotNumber", "SerialNumber", "RefLabel", "Reason", "UserName" }, null, null, "[{\"field\":\"CreatedAtUtc\",\"dir\":\"desc\"}]");
        await Report(EntityTypes.InventoryTransaction, MovementsByTypeReportName, MovementsByTypeDescriptionEs, MovementsByTypeDescriptionEn,
            Array.Empty<string>(), null, MovementsByTypeGroup, null);
        await Report(EntityTypes.InventoryTransaction, "Ajustes de inventario", "Ajustes manuales y de sistema con motivo y usuario", "Manual and system adjustments with reason and user",
            new[] { "CreatedAtUtc", "Sku", "ProductName", "Quantity", "Position", "LotNumber", "Reason", "RefLabel", "UserName" }, AdjustmentsFilter, null, "[{\"field\":\"CreatedAtUtc\",\"dir\":\"desc\"}]");
        await Report(EntityTypes.StockBalance, "Próximos a vencer", "Existencias de lotes vencidos o que vencen en 30 días", "Stock of lots expired or expiring within 30 days",
            new[] { "ExpiryDate", "DaysToExpiry", "Sku", "ProductName", "LotNumber", "WarehouseCode", "BinCode", "QtyOnHand" }, ExpiringSoonFilter, null, "[{\"field\":\"ExpiryDate\",\"dir\":\"asc\"}]");
        await Report(EntityTypes.Receipt, "Recepciones con diferencia", "Recibos con diferencia entre lo esperado y lo recibido", "Receipts with a difference between expected and received",
            new[] { "Number", "Type", "Origin", "WarehouseCode", "ExpectedQty", "ReceivedQty", "VarianceQty", "Status", "ReceivedAtUtc" }, ReceiptsWithVarianceFilter, null, "[{\"field\":\"ReceivedAtUtc\",\"dir\":\"desc\"}]");
        // Lote 29 (Rentas R3) — reportes mínimos de rentas (los nombres de campo son los de RentalDataSource, RentalReturnDataSource y
        // RentalProcessDataSource; no cambiarlos sin cambiar ambos). Se siembran en toda compañía y se ocultan con el módulo Rentas
        // apagado (IDataSource.TenantModule), como el contenido de almacén se oculta sin WMS_LOTSERIAL. Estado actual: las vistas
        // corren con rango ALL por defecto.
        var rentalColumns = new[] { "Number", "ClientName", "LocationName", "WarehouseCode", "Status", "PickupDate", "DaysToPickup", "Units", "UnitsOnRent", "ContractNumber" };
        await Report(EntityTypes.Rental, RentalAnalyticsRules.OnRentByClientReportName, "Rentas En renta y equipos en el cliente, por cliente",
            "Rentals on rent and units at the client, by client", Array.Empty<string>(), RentalAnalyticsRules.OnRentFilter, RentalAnalyticsRules.OnRentByClientGroup, null);
        await Report(EntityTypes.Rental, RentalAnalyticsRules.DueSoonReportName, "Rentas abiertas cuya fecha de recogido es hoy o en los próximos 7 días",
            "Open rentals due for pickup today or within the next 7 days", rentalColumns, RentalAnalyticsRules.DueSoonFilter, null, "[{\"field\":\"PickupDate\",\"dir\":\"asc\"}]");
        await Report(EntityTypes.Rental, RentalAnalyticsRules.OverdueReportName, "Rentas abiertas cuya fecha de recogido ya pasó",
            "Open rentals whose pickup date has passed", rentalColumns, RentalAnalyticsRules.OverdueFilter, null, "[{\"field\":\"PickupDate\",\"dir\":\"asc\"}]");
        await Report(EntityTypes.RentalReturn, RentalAnalyticsRules.ReturnsByReasonReportName, "Devoluciones de renta y equipos devueltos por motivo",
            "Rental returns and returned units by reason", Array.Empty<string>(), null, RentalAnalyticsRules.ReturnsByReasonGroup, null);
        await Report(EntityTypes.RentalProcess, RentalAnalyticsRules.InProcessReportName, "Equipos devueltos con su proceso abierto (inspección, limpieza, pruebas, reparación…)",
            "Returned equipment with an open process (inspection, cleaning, testing, repair…)",
            new[] { "SerialNumber", "Sku", "ProductName", "WarehouseCode", "BinCode", "Status", "Condition", "ReturnNumber", "RentalNumber", "ClientName", "StartedAtUtc", "DaysInProcess" },
            RentalAnalyticsRules.OpenProcessFilter, null, "[{\"field\":\"StartedAtUtc\",\"dir\":\"asc\"}]");

        // ---- Indicadores ----
        var existingInd = await db.IndicatorDefinitions.Where(i => i.TenantId == tenantId).Select(i => i.Name).ToListAsync(ct);
        void Indicator(string name, string es, string en, string source, string? field, int fn, string? filter, int? range, bool pulse, int sort, bool isMoney = false, int? module = null)
        {
            if (existingInd.Contains(name)) return;
            db.IndicatorDefinitions.Add(new IndicatorDefinition
            {
                TenantId = tenantId, Name = name, DescriptionJson = MultilingualText.Build(es, en), DataSourceKey = source, FieldKey = field, AggregateFnLookupId = fn,
                FilterJson = filter, BusinessModuleLookupId = module ?? ops, IsMoney = isMoney, IsSystem = true, VisibilityLookupId = visTenant, DateRangeModeLookupId = range, ShowInPulse = pulse, SortOrder = sort,
            });
        }
        Indicator("Cambios registrados", "Cambios auditados en el período", "Audited changes in the period", EntityTypes.AuditLog, null, count, null, last7, true, 10);
        Indicator("Accesos fallidos", "Intentos de acceso fallidos o bloqueados en el período", "Failed/blocked access attempts in the period", EntityTypes.SecurityEvent, null, count,
            "{\"and\":[{\"field\":\"OutcomeCode\",\"op\":\"in\",\"value\":[\"FAILURE\",\"BLOCKED\"]}]}", last7, true, 20);
        Indicator("Permisos denegados", "Acciones bloqueadas por RBAC en el período", "Actions blocked by RBAC in the period", EntityTypes.SecurityEvent, null, count,
            "{\"and\":[{\"field\":\"EventTypeCode\",\"op\":\"eq\",\"value\":\"PERMISSION_DENIED\"}]}", last30, false, 30);
        Indicator("Usuarios activos", "Usuarios activos con membresía activa", "Active users with active membership", EntityTypes.User, null, count,
            "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"MembershipStatus\",\"op\":\"eq\",\"value\":\"ACTIVE\"}]}", null, true, 40);
        Indicator("Usuarios sin MFA", "Usuarios activos sin segundo factor", "Active users without MFA", EntityTypes.User, null, count,
            "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"MfaEnabled\",\"op\":\"isFalse\"}]}", null, false, 50);
        // Lote 2 — Clientes y contratos (estado actual: sin rango de fecha)
        Indicator("Clientes activos", "Clientes activos con estatus ACTIVE", "Active clients with ACTIVE status", EntityTypes.Client, null, count,
            "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"ACTIVE\"}]}", null, true, 60);
        // Lote 3 — Órdenes de transporte (estado actual: rango ALL, porque la fuente TRANSPORT_ORDER tiene DateField y con
        // rango null el motor aplicaría LAST7 por defecto). 'COD por cobrar' es de Contabilidad (L155/L159: el ciclo COD vive ahí).
        Indicator("Órdenes en curso", "Órdenes activas confirmadas y aún no entregadas", "Active orders confirmed and not yet delivered", EntityTypes.TransportOrder, null, count,
            "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"StatusCode\",\"op\":\"in\",\"value\":[\"CONFIRMED\",\"PICKUP\",\"INBOUND\",\"PLANNED\",\"IN_TRANSIT\",\"ARRIVED\"]}]}", all, true, 70);
        Indicator("COD por cobrar", "Suma del COD pendiente de cobro de las órdenes activas no canceladas", "Sum of pending COD of active, non-cancelled orders", EntityTypes.TransportOrder, "CodAmount", sum,
            CodPendingFilter, all, true, 71, isMoney: true, module: acct);
        // Lote 4 — Flota. FLEET_DOCUMENT es un estado actual sin DateField (sin rango); WORK_ORDER tiene DateField, así que
        // su estado actual va con rango ALL (con null el motor aplicaría LAST7).
        Indicator("Documentos por vencer", "Documentos de vehículos y choferes vencidos o que vencen en 30 días", "Vehicle and driver documents expired or expiring within 30 days",
            EntityTypes.FleetDocument, null, count, FleetDocumentsDueFilter, null, true, 80, module: ops);
        Indicator("Órdenes de trabajo abiertas", "Órdenes de trabajo de mantenimiento abiertas o en proceso", "Maintenance work orders open or in progress",
            EntityTypes.WorkOrder, null, count, OpenWorkOrdersFilter, all, true, 81, module: ops);
        // Lote 5 — Despacho (estado actual: rango ALL; los campos son los de TransportOrderDataSource y TripDataSource).
        Indicator("Órdenes sin chofer asignado", "Órdenes activas pendientes de despacho sin ruta con chofer ni entrega especial asignada", "Active orders pending dispatch without a driver",
            EntityTypes.TransportOrder, null, count, OrdersWithoutDriverFilter, all, true, 72, module: ops);
        Indicator("Órdenes en excepción", "Órdenes activas en espera, entrega parcial o fallida", "Active orders on hold, partial or failed",
            EntityTypes.TransportOrder, null, count, OrdersInExceptionFilter, all, true, 73, module: ops);
        Indicator("Rutas sobre el máximo de paradas", "Rutas abiertas o en curso con más paradas que el máximo del chofer", "Open or running trips over the driver's stop limit",
            EntityTypes.Trip, null, count, TripsOverStopLimitFilter, all, true, 82, module: ops);
        // Lote 6 — Inventario y almacén (módulo WAREHOUSE). PRODUCT y STOCK_BALANCE son estado actual sin DateField (sin rango);
        // INVENTORY_TRANSACTION y WAREHOUSE_TASK tienen DateField (el estado actual de las tareas va con rango ALL).
        Indicator("Productos activos", "Productos activos del catálogo", "Active products in the catalog",
            EntityTypes.Product, null, count, ActiveProductsFilter, null, true, 90, module: wh);
        Indicator("Inventario disponible", "Unidades disponibles (en mano − reservado) en todos los almacenes", "Available units (on hand − reserved) across warehouses",
            EntityTypes.StockBalance, "QtyAvailable", sum, null, null, true, 91, module: wh);
        Indicator("Movimientos registrados", "Movimientos de inventario en el período", "Inventory movements in the period",
            EntityTypes.InventoryTransaction, null, count, null, last7, false, 92, module: wh);   // maestro L948: LAST7 por defecto
        Indicator("Valor de inventario a costo", "Existencia en mano valorada al costo de compra", "On-hand stock valued at purchase cost",
            EntityTypes.StockBalance, "CostValue", sum, null, null, true, 93, isMoney: true, module: wh);
        Indicator("Productos bajo mínimo", "Productos activos con disponible por debajo de su mínimo", "Active products with available below their minimum",
            EntityTypes.Product, null, count, ProductsBelowMinFilter, null, true, 94, module: wh);
        Indicator("Valor de inventario a venta", "Existencia en mano valorada al precio de venta", "On-hand stock valued at sale price",
            EntityTypes.StockBalance, "SaleValue", sum, null, null, false, 95, isMoney: true, module: wh);
        Indicator("Tareas de almacén pendientes", "Tareas de la cola pendientes o en proceso", "Queue tasks pending or in progress",
            EntityTypes.WarehouseTask, null, count, OpenWarehouseTasksFilter, all, true, 96, module: wh);

        // Lote 7A — Pulso de almacén (maestro, módulo 12 'Pulso del día'): visibles a toda la organización y en Pulso.
        // 'Productos activos' y 'Productos bajo mínimo' ya vienen del Lote 6 (mismo filtro que la vista 'Inventario bajo mínimo').
        Indicator(ReceivedUnitsIndicatorName, "Unidades recibidas en el período (recepciones más diferencias de recepción: neto = lo recibido)", "Units received in the period (receipts plus receipt variances: net = received)",
            EntityTypes.InventoryTransaction, "Quantity", sum, ReceiptMovementsFilter, last7, true, 97, module: wh);
        Indicator(CountsWithVarianceIndicatorName, "Conteos cíclicos reconciliados en el período con alguna diferencia", "Cycle counts reconciled in the period with a variance",
            EntityTypes.CycleCount, null, count, ReconciledCountsWithVarianceFilter, last30, true, 98, module: wh);
        // Lote 15 (D16): descuadres Kárdex ↔ saldo abiertos (fuente INVENTORY_DISCREPANCY del Lote 14). Estado actual: rango ALL (la
        // fuente tiene DateField y con null el motor aplicaría LAST7). Apagado en el Pulso ("Necesita tu atención" ya los muestra).
        Indicator(PendingDiscrepanciesIndicatorName, "Descuadres entre el Kárdex y el saldo pendientes de revisar", "Ledger vs. balance discrepancies pending review",
            EntityTypes.InventoryDiscrepancy, null, count, PendingDiscrepanciesFilter, all, false, 99, module: wh);
        // Lote 29 (Rentas R3): rentas por vencer y vencidas (fuente RENTAL, día de la compañía). Estado actual: rango ALL (la fuente
        // tiene DateField StartDate y con null el motor aplicaría LAST7). Apagados en el Pulso, como 'Descuadres pendientes' (D16):
        // "Necesita tu atención" ya muestra cada renta vencida o por vencer.
        Indicator(RentalAnalyticsRules.DueSoonIndicatorName, "Rentas abiertas (Programadas o En renta) cuya fecha de recogido es hoy o en los próximos 7 días",
            "Open rentals (scheduled or on rent) due for pickup today or within the next 7 days",
            EntityTypes.Rental, null, count, RentalAnalyticsRules.DueSoonFilter, all, false, 100, module: wh);
        Indicator(RentalAnalyticsRules.OverdueIndicatorName, "Rentas abiertas (Programadas o En renta) cuya fecha de recogido ya pasó",
            "Open rentals (scheduled or on rent) whose pickup date has passed",
            EntityTypes.Rental, null, count, RentalAnalyticsRules.OverdueFilter, all, false, 101, module: wh);

        // Corrección idempotente de tenants ya sembrados con la versión anterior (rango null / COD en Operación).
        var orderIndicators = await db.IndicatorDefinitions
            .Where(i => i.TenantId == tenantId && i.IsSystem && (i.Name == "Órdenes en curso" || i.Name == "COD por cobrar"))
            .ToListAsync(ct);
        foreach (var ind in orderIndicators)
        {
            ind.DateRangeModeLookupId ??= all;
            if (ind.Name == "COD por cobrar" && ind.BusinessModuleLookupId == ops) ind.BusinessModuleLookupId = acct;
            // Una orden cancelada nunca se entrega: su COD no está por cobrar. Solo se reemplaza el filtro original exacto
            // (no se pisa un filtro que el tenant haya personalizado).
            if (ind.Name == "COD por cobrar" && ind.FilterJson == CodPendingFilterV1) ind.FilterJson = CodPendingFilter;
        }

        // Lote 6: 'Movimientos registrados' se sembró antes con LAST30; el maestro (L945-L948) fija LAST7. Solo el indicador de
        // sistema que conserve LAST30 (no se pisa un rango que el tenant haya cambiado a otro valor).
        var movementsIndicator = await db.IndicatorDefinitions
            .Where(i => i.TenantId == tenantId && i.IsSystem && i.Name == "Movimientos registrados" && i.DateRangeModeLookupId == last30)
            .ToListAsync(ct);
        foreach (var ind in movementsIndicator) ind.DateRangeModeLookupId = last7;

        // Lote 7A: 'Unidades recibidas' se sembró primero solo con RECEIPT (sumaba lo esperado, D4). Solo se reemplaza el filtro
        // original exacto del indicador de sistema (no se pisa uno personalizado).
        var receivedIndicator = await db.IndicatorDefinitions
            .Where(i => i.TenantId == tenantId && i.IsSystem && i.Name == ReceivedUnitsIndicatorName && i.FilterJson == ReceiptMovementsFilterV1)
            .ToListAsync(ct);
        foreach (var ind in receivedIndicator) ind.FilterJson = ReceiptMovementsFilter;

        // Lote 14 (D7): 'Conteos con diferencia' pasa de RECONCILED + HasVariance a RECONCILED_VARIANCE. Solo el filtro original
        // exacto del indicador de sistema (no se pisa uno personalizado).
        var varianceIndicator = await db.IndicatorDefinitions
            .Where(i => i.TenantId == tenantId && i.IsSystem && i.Name == CountsWithVarianceIndicatorName && i.FilterJson == ReconciledCountsWithVarianceFilterV1)
            .ToListAsync(ct);
        foreach (var ind in varianceIndicator) ind.FilterJson = ReconciledCountsWithVarianceFilter;

        // ---- Gráficos ----
        // Lote 15 (D9, D13): los 2 gráficos de almacén de fábrica ("Valor de inventario por categoría" y "Movimientos de inventario
        // por tipo") dejan de ser de sistema: pasan a ser DE LA COMPAÑÍA (sin dueño, editables con analytics.manage) con su clave
        // de siembra. Se convierte solo la fila de sistema que sigue exactamente como se sembró y solo si la compañía aún no tiene
        // esa clave (activa o borrada: si se borra, no vuelve). Mismo criterio que el bloque "Lote 15" de logistica-db-seed.sql,
        // que convierte las compañías ya creadas en cada db-init. Van a la primera fila de "Tus gráficos" (1 y 2) solo si su orden
        // sigue en el de fábrica y la compañía no organizó sus gráficos (Organizar guarda índice × 10 desde 0).
        var companyOrganized = await db.ChartDefinitions.AnyAsync(c => c.TenantId == tenantId && c.IsActive && c.SortOrder == 0, ct);
        var seededKeys = await db.ChartDefinitions.Where(c => c.TenantId == tenantId && c.SeedKey != null).Select(c => c.SeedKey!).ToListAsync(ct);
        async Task ConvertAsync(string seedKey, string name, string source, string groupBy, string field, int sortV1, int sortNew, int newType,
            string? newField, string? es, string? en)
        {
            if (seededKeys.Contains(seedKey)) return;
            var legacy = await db.ChartDefinitions
                .Where(c => c.TenantId == tenantId && c.IsSystem && c.SeedKey == null && c.Name == name && c.DataSourceKey == source
                            && c.GroupByField == groupBy && c.FieldKey == field && c.ChartTypeLookupId == bar)
                .FirstOrDefaultAsync(ct);
            if (legacy is null) return;
            legacy.IsSystem = false;
            legacy.OwnerUserId = null;
            legacy.SeedKey = seedKey;
            legacy.ChartTypeLookupId = newType;
            if (newField is not null) legacy.FieldKey = newField;
            if (es is not null) legacy.DescriptionJson = MultilingualText.Build(es, en ?? es);
            if (legacy.SortOrder == sortV1 && !companyOrganized) legacy.SortOrder = sortNew;
            seededKeys.Add(seedKey);
        }
        await ConvertAsync(ChartSeedKeys.InventoryValue, InventoryValueChartName, EntityTypes.StockBalance, "Category", "CostValue",
            InventoryValueChartSortOrderV1, InventoryValueChartSortOrder, donut, null, null, null);
        await ConvertAsync(ChartSeedKeys.MovementsByType, MovementsByTypeChartName, EntityTypes.InventoryTransaction, "TxnType", "Quantity",
            MovementsByTypeChartSortOrderV1, MovementsByTypeChartSortOrder, bar, "Units", MovementsByTypeChartDescriptionEs, MovementsByTypeChartDescriptionEn);

        var existingCharts = await db.ChartDefinitions.Where(c => c.TenantId == tenantId).Select(c => c.Name).ToListAsync(ct);
        // Lote 6: el helper gana campo, función, dinero y módulo opcionales (los gráficos anteriores siguen con COUNT en Operación).
        // Lote 15: gana isSystem y seedKey (gráfico de la compañía): con clave no se crea si la clave ya existe (activa o borrada).
        void Chart(string name, string es, string en, string source, string groupBy, int type, string? filter, int? range, bool pulse, int sort,
            string? field = null, int? fn = null, bool isMoney = false, int? module = null, bool isSystem = true, string? seedKey = null)
        {
            if (existingCharts.Contains(name)) return;
            if (seedKey is not null && seededKeys.Contains(seedKey)) return;
            db.ChartDefinitions.Add(new ChartDefinition
            {
                TenantId = tenantId, Name = name, DescriptionJson = MultilingualText.Build(es, en), DataSourceKey = source, GroupByField = groupBy, FieldKey = field,
                AggregateFnLookupId = fn ?? count, ChartTypeLookupId = type, FilterJson = filter, BusinessModuleLookupId = module ?? ops, IsMoney = isMoney, IsSystem = isSystem,
                OwnerUserId = null, SeedKey = seedKey,
                VisibilityLookupId = visTenant, DateRangeModeLookupId = range, ShowInPulse = pulse, SortOrder = sort,
            });
        }
        Chart("Cambios por acción", "Distribución de cambios por tipo de acción", "Changes by action type", EntityTypes.AuditLog, "Action", donut, null, last7, true, 10);
        Chart("Cambios por usuario", "Quién registró más cambios", "Who recorded the most changes", EntityTypes.AuditLog, "UserName", bar, null, last30, false, 20);
        Chart("Eventos de seguridad por día", "Tendencia diaria de eventos de seguridad", "Daily trend of security events", EntityTypes.SecurityEvent, "CreatedAtUtc", line, null, last30, true, 30);
        Chart("Usuarios por rol", "Distribución de usuarios por rol", "Users by role", EntityTypes.User, "Roles", donut, null, null, false, 40);
        // Lote 2 — Clientes y contratos
        Chart("Contratos por estatus", "Distribución de contratos por estatus", "Contracts by status", EntityTypes.Contract, "Status", donut, null, all, false, 60);
        // Lote 3 — Órdenes de transporte
        Chart("Órdenes por estatus", "Distribución de las órdenes de los últimos 30 días por estatus", "Orders of the last 30 days by status", EntityTypes.TransportOrder, "Status", donut, null, last30, true, 70);
        // Lote 5 — Rutas (por fecha de la ruta, PlanDate)
        Chart("Rutas por estatus", "Distribución de las rutas de los últimos 7 días por estatus", "Trips of the last 7 days by status", EntityTypes.Trip, "Status", donut, null, last7, true, 80);
        // Lote 6 — Inventario y almacén (módulo WAREHOUSE)
        // Lote 15 (D9–D11, D13, D14): de la compañía (sin dueño), DONA con el total al centro, por categoría del producto (7 mayores +
        // "Otras" en el motor), primera fila de "Tus gráficos" a la izquierda. Estado actual: sin rango.
        Chart(InventoryValueChartName, "Existencia valorada a costo por categoría", "Stock cost value by category", EntityTypes.StockBalance, "Category", donut, null, null, true,
            InventoryValueChartSortOrder, field: "CostValue", fn: sum, isMoney: true, module: wh, isSystem: false, seedKey: ChartSeedKeys.InventoryValue);
        Chart("Disponible por categoría", "Unidades disponibles por categoría", "Available units by category", EntityTypes.StockBalance, "Category", bar, null, null, false, 91,
            field: "QtyAvailable", fn: sum, module: wh);
        Chart("Movimientos por tipo", "Movimientos de inventario de los últimos 30 días por tipo", "Inventory movements of the last 30 days by type", EntityTypes.InventoryTransaction, "TxnType", donut, null, last30, true, 92,
            module: wh);
        Chart("Movimientos por usuario", "Quién registró más movimientos de inventario", "Who recorded the most inventory movements", EntityTypes.InventoryTransaction, "UserName", bar, null, last30, false, 93,
            module: wh);
        Chart("Productos por categoría", "Productos activos por categoría", "Active products by category", EntityTypes.Product, "Category", donut, ActiveProductsFilter, null, false, 94,
            module: wh);
        Chart("Movimientos por día", "Tendencia diaria de movimientos de inventario", "Daily trend of inventory movements", EntityTypes.InventoryTransaction, "Date", line, null, last30, true, 95,
            module: wh);

        // Lote 7A — gráfico de barras del Pulso de almacén por tipo. Lote 15 (D9, D12–D14): de la compañía (sin dueño), en UNIDADES
        // POSITIVAS (campo Units = |Quantity|: el despacho ya no sale negativo), últimos 7 días (cada usuario lo cambia con "Rango"),
        // primera fila de "Tus gráficos" a la derecha.
        Chart(MovementsByTypeChartName, MovementsByTypeChartDescriptionEs, MovementsByTypeChartDescriptionEn,
            EntityTypes.InventoryTransaction, "TxnType", bar, null, last7, true, MovementsByTypeChartSortOrder, field: "Units", fn: sum, module: wh,
            isSystem: false, seedKey: ChartSeedKeys.MovementsByType);

        // Lote 29 (Rentas R3): devoluciones de renta por motivo (fuente RENTAL_RETURN, por fecha de devolución), dona de 30 días, de
        // sistema y apagada en el Pulso (cada usuario la enciende); oculta con el módulo Rentas apagado.
        Chart(RentalAnalyticsRules.ReturnsByReasonChartName, "Devoluciones de renta de los últimos 30 días por motivo", "Rental returns of the last 30 days by reason",
            EntityTypes.RentalReturn, "Reason", donut, null, last30, false, 100, module: wh);

        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
    }
}
