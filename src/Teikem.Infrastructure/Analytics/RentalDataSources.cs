using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 29 (Rentas R3) — fuentes de datos de Rentas para vistas, indicadores y gráficos: RENTAL (rentas), RENTAL_RETURN
/// (devoluciones de renta) y RENTAL_PROCESS (proceso del equipo devuelto). Siguen el patrón de las fuentes de almacén
/// (CycleCountDataSource, ReceiptDataSource): AsNoTracking bajo el filtro global de tenant (Rental, RentalReturn y RentalProcess son
/// ITenantScoped), tope ClientDataSourceHelpers.MaxRows, respeto de q.Ids (relaciones) y del rango (desde inclusivo, hasta
/// exclusivo). Las hijas sin TenantId (RentalLine, RentalExtension, RentalReturnLine, InventorySerial) se alcanzan SOLO por los ids
/// de los padres ya filtrados y en consultas por lote (sin N+1).
/// - Lectura: rental.view (PermissionCatalog.OwnerReadPermission de RENTAL, RENTAL_RETURN y RENTAL_PROCESS) y el módulo Rentas
///   encendido (TenantModule = RENTAL_EQUIPMENT; AnalyticsService oculta la fuente y sus definiciones con el módulo apagado).
/// - "Hoy" es el día de la compañía (ITenantClock): DaysToPickup, IsOverdue y DaysInProcess se calculan al leer.
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (RentalAnalyticsRules) y AnalyticsSeedFieldsTests los verifica.
/// </summary>
internal static class RentalDataSourceHelpers
{
    public static async Task<Dictionary<int, string>> ClientNamesAsync(TeikemDbContext db, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, string>();
        var list = ids.ToList();
        return await db.Clients.AsNoTracking().Where(c => list.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
    }

    public static async Task<Dictionary<int, string>> LocationNamesAsync(TeikemDbContext db, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, string>();
        var list = ids.ToList();
        return await db.Locations.AsNoTracking().Where(l => list.Contains(l.LocationId))
            .Select(l => new { l.LocationId, l.Name }).ToDictionaryAsync(l => l.LocationId, l => l.Name, ct);
    }

    public static async Task<Dictionary<int, string>> WarehouseCodesAsync(TeikemDbContext db, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, string>();
        var list = ids.ToList();
        return await db.Warehouses.AsNoTracking().Where(w => list.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
    }

    /// <summary>Rango sobre un campo de día de calendario (DATE): los días locales del motor o, sin ellos, los de FromUtc/ToUtc.</summary>
    public static (DateOnly? From, DateOnly? ToExclusive) DayRange(DataQuery q)
        => (q.FromDay ?? (q.FromUtc.HasValue ? DateOnly.FromDateTime(q.FromUtc.Value) : (DateOnly?)null),
            q.ToDayExclusive ?? (q.ToUtc.HasValue ? DateOnly.FromDateTime(q.ToUtc.Value) : (DateOnly?)null));
}

/// <summary>
/// Fuente RENTAL: una fila por renta. Actividad de período por StartDate (fecha de inicio, día de calendario). Estado actual
/// (por vencer, vencidas, en renta hoy) con rango ALL. Units = equipos activos; UnitsOnRent = despachados y sin devolver (en el
/// cliente); UnitsReturned = devueltos. IsOpen = Programada o En renta; IsOverdue = abierta con el recogido antes de hoy;
/// DaysToPickup = días hasta el recogido (negativo si ya pasó). Admite campos personalizados de RENTAL.
/// </summary>
public sealed class RentalDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public string Key => EntityTypes.Rental;
    public string LabelEs => "Rentas";
    public string LabelEn => "Rentals";
    public string? EntityTypeCode => EntityTypes.Rental;
    public string? DateField => "StartDate";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.RentalEquipment;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Number", "Número de renta", "Rental number", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("LocationId", "Id de localidad", "Location id", DataFieldType.Number),
        new DataField("LocationName", "Localidad", "Location", DataFieldType.Text),
        new DataField("WarehouseId", "Id de almacén", "Warehouse id", DataFieldType.Number),
        new DataField("WarehouseCode", "Almacén de origen", "Source warehouse", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("IsOpen", "Abierta", "Open", DataFieldType.Bool),
        new DataField("StartDate", "Fecha de inicio", "Start date", DataFieldType.Date),
        new DataField("PickupDate", "Fecha de recogido", "Pickup date", DataFieldType.Date),
        new DataField("OriginalPickupDate", "Recogido pactado", "Original pickup date", DataFieldType.Date),
        new DataField("DaysToPickup", "Días para el recogido", "Days to pickup", DataFieldType.Number),
        new DataField("IsOverdue", "Vencida", "Overdue", DataFieldType.Bool),
        new DataField("ExtensionCount", "Extensiones", "Extensions", DataFieldType.Number),
        new DataField("DaysExtended", "Días extendidos", "Days extended", DataFieldType.Number),
        new DataField("Units", "Equipos", "Units", DataFieldType.Number),
        new DataField("UnitsOnRent", "Equipos en el cliente", "Units at the client", DataFieldType.Number),
        new DataField("UnitsReturned", "Equipos devueltos", "Units returned", DataFieldType.Number),
        new DataField("ContractNumber", "Número de contrato", "Contract number", DataFieldType.Text),
        new DataField("ContractSignedOn", "Contrato firmado el", "Contract signed on", DataFieldType.Date),
        new DataField("EstimatedDeliveryCost", "Transporte estimado", "Estimated delivery cost", DataFieldType.Number, IsMoney: true),
        new DataField("TransportCurrency", "Moneda del transporte", "Transport currency", DataFieldType.Text),
        new DataField("DispatchedAtUtc", "Despachada el", "Dispatched at", DataFieldType.Date),
        new DataField("ClosedAtUtc", "Cerrada el", "Closed at", DataFieldType.Date),
        new DataField("CreatedAtUtc", "Creada el", "Created at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
        new DataRelation("Location", EntityTypes.Location, "LocationId", "Localidad", "Location"),
        new DataRelation("Warehouse", EntityTypes.Warehouse, "WarehouseId", "Almacén", "Warehouse"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Rentals.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(r => wanted.Contains(r.RentalId));
        }
        var (from, to) = RentalDataSourceHelpers.DayRange(q);
        if (from is DateOnly f) query = query.Where(r => r.StartDate >= f);
        if (to is DateOnly t) query = query.Where(r => r.StartDate < t);

        var rentals = await query.OrderByDescending(r => r.StartDate).ThenByDescending(r => r.RentalId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (rentals.Count == 0) return new List<DataRow>();

        var ids = rentals.Select(r => r.RentalId).ToList();
        var lines = await db.RentalLines.AsNoTracking().Where(l => ids.Contains(l.RentalId))
            .GroupBy(l => l.RentalId)
            .Select(g => new
            {
                RentalId = g.Key,
                Units = g.Count(l => l.IsActive),
                OnRent = g.Count(l => l.IsActive && l.DispatchedAtUtc != null && l.ReturnedAtUtc == null),
                Returned = g.Count(l => l.ReturnedAtUtc != null),
            })
            .ToDictionaryAsync(x => x.RentalId, ct);
        var extensions = await db.RentalExtensions.AsNoTracking().Where(e => ids.Contains(e.RentalId))
            .GroupBy(e => e.RentalId).Select(g => new { RentalId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RentalId, x => x.Count, ct);
        var clients = await RentalDataSourceHelpers.ClientNamesAsync(db, rentals.Select(r => r.ClientId).Distinct().ToList(), ct);
        var locations = await RentalDataSourceHelpers.LocationNamesAsync(db, rentals.Select(r => r.LocationId).Distinct().ToList(), ct);
        var warehouses = await RentalDataSourceHelpers.WarehouseCodesAsync(db, rentals.Select(r => r.WarehouseId).Distinct().ToList(), ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.RentalStatus, ct);
        var today = ClientDataSourceHelpers.Today(clock);
        var lang = tenant.Lang;

        var rows = new List<DataRow>(rentals.Count);
        foreach (var r in rentals)
        {
            var code = ClientDataSourceHelpers.StatusCodeOf(statusMap, r.StatusCodeId);
            var l = lines.GetValueOrDefault(r.RentalId);
            rows.Add(new DataRow
            {
                ["Id"] = r.RentalId,
                ["PublicId"] = r.PublicId.ToString(),
                ["Number"] = r.Number,
                ["ClientId"] = r.ClientId,
                ["ClientName"] = clients.GetValueOrDefault(r.ClientId),
                ["LocationId"] = r.LocationId,
                ["LocationName"] = locations.GetValueOrDefault(r.LocationId),
                ["WarehouseId"] = r.WarehouseId,
                ["WarehouseCode"] = warehouses.GetValueOrDefault(r.WarehouseId),
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, r.StatusCodeId, lang),
                ["StatusCode"] = code,
                ["IsOpen"] = RentalAnalyticsRules.IsOpen(code),
                ["StartDate"] = r.StartDate,
                ["PickupDate"] = r.PickupDate,
                ["OriginalPickupDate"] = r.OriginalPickupDate,
                ["DaysToPickup"] = RentalRules.DaysToPickup(r.PickupDate, today),
                ["IsOverdue"] = RentalRules.IsOverdue(code, r.PickupDate, today),
                ["ExtensionCount"] = extensions.GetValueOrDefault(r.RentalId),
                ["DaysExtended"] = r.PickupDate.DayNumber - r.OriginalPickupDate.DayNumber,
                ["Units"] = l?.Units ?? 0,
                ["UnitsOnRent"] = l?.OnRent ?? 0,
                ["UnitsReturned"] = l?.Returned ?? 0,
                ["ContractNumber"] = r.ContractNumber,
                ["ContractSignedOn"] = r.ContractSignedOn,
                ["EstimatedDeliveryCost"] = r.EstimatedDeliveryCost,
                ["TransportCurrency"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, r.TransportCurrencyLookupId, ct),
                ["DispatchedAtUtc"] = r.DispatchedAtUtc,
                ["ClosedAtUtc"] = r.ClosedAtUtc,
                ["CreatedAtUtc"] = r.CreatedAtUtc,
            });
        }
        return rows;
    }
}

/// <summary>
/// Fuente RENTAL_RETURN: una fila por devolución de renta (DRN-#####). Actividad de período por ReturnedOn (día de la
/// devolución). Reason = motivo (uno por devolución); la condición es por equipo, así que la fila trae el resumen: Condition
/// (condiciones distintas, en orden Buena, Dañado, Incompleto), los equipos por condición y HasDamage. IsEarly = devuelta antes
/// de la fecha de recogido vigente (mismo cálculo que la lista de devoluciones); DaysEarly = días de anticipación.
/// </summary>
public sealed class RentalReturnDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.RentalReturn;
    public string LabelEs => "Devoluciones de renta";
    public string LabelEn => "Rental returns";
    public string? EntityTypeCode => EntityTypes.RentalReturn;
    public string? DateField => "ReturnedOn";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.RentalEquipment;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Number", "Número de devolución", "Return number", DataFieldType.Text),
        new DataField("RentalId", "Id de renta", "Rental id", DataFieldType.Number),
        new DataField("RentalNumber", "Renta", "Rental", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("LocationName", "Localidad", "Location", DataFieldType.Text),
        new DataField("ReturnedOn", "Fecha de devolución", "Returned on", DataFieldType.Date),
        new DataField("PickupDate", "Fecha de recogido", "Pickup date", DataFieldType.Date),
        new DataField("Reason", "Motivo", "Reason", DataFieldType.Text),
        new DataField("ReasonCode", "Código de motivo", "Reason code", DataFieldType.Text),
        new DataField("IsEarly", "Anticipada", "Early", DataFieldType.Bool),
        new DataField("DaysEarly", "Días de anticipación", "Days early", DataFieldType.Number),
        new DataField("Condition", "Condición", "Condition", DataFieldType.Text),
        new DataField("ConditionCode", "Código de condición", "Condition code", DataFieldType.Text),
        new DataField("HasDamage", "Con equipo dañado", "Has damaged units", DataFieldType.Bool),
        new DataField("Units", "Equipos", "Units", DataFieldType.Number),
        new DataField("GoodUnits", "Equipos en buena condición", "Units in good condition", DataFieldType.Number),
        new DataField("DamagedUnits", "Equipos dañados", "Damaged units", DataFieldType.Number),
        new DataField("IncompleteUnits", "Equipos incompletos", "Incomplete units", DataFieldType.Number),
        new DataField("UnitsWithProcess", "Equipos con proceso", "Units with process", DataFieldType.Number),
        new DataField("OpenProcesses", "Procesos abiertos", "Open processes", DataFieldType.Number),
        new DataField("EstimatedPickupCost", "Recogido estimado", "Estimated pickup cost", DataFieldType.Number, IsMoney: true),
        new DataField("TransportCurrency", "Moneda del transporte", "Transport currency", DataFieldType.Text),
        new DataField("CreatedAtUtc", "Registrada el", "Created at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Rental", EntityTypes.Rental, "RentalId", "Renta", "Rental"),
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.RentalReturns.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(r => wanted.Contains(r.RentalReturnId));
        }
        var (from, to) = RentalDataSourceHelpers.DayRange(q);
        if (from is DateOnly f) query = query.Where(r => r.ReturnedOn >= f);
        if (to is DateOnly t) query = query.Where(r => r.ReturnedOn < t);

        var returns = await query.OrderByDescending(r => r.ReturnedOn).ThenByDescending(r => r.RentalReturnId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (returns.Count == 0) return new List<DataRow>();

        var ids = returns.Select(r => r.RentalReturnId).ToList();
        var rentalIds = returns.Select(r => r.RentalId).Distinct().ToList();
        var rentals = await db.Rentals.AsNoTracking().Where(r => rentalIds.Contains(r.RentalId))
            .Select(r => new { r.RentalId, r.Number, r.ClientId, r.LocationId, r.PickupDate }).ToDictionaryAsync(r => r.RentalId, ct);
        var clients = await RentalDataSourceHelpers.ClientNamesAsync(db, rentals.Values.Select(r => r.ClientId).Distinct().ToList(), ct);
        var locations = await RentalDataSourceHelpers.LocationNamesAsync(db, rentals.Values.Select(r => r.LocationId).Distinct().ToList(), ct);
        var lines = await db.RentalReturnLines.AsNoTracking().Where(l => ids.Contains(l.RentalReturnId))
            .Select(l => new { l.RentalReturnId, l.RentalReturnLineId, l.ConditionLookupId, l.RequiresProcess }).ToListAsync(ct);
        var lineIds = lines.Select(l => (int?)l.RentalReturnLineId).ToList();
        var openLines = (await db.RentalProcesses.AsNoTracking()
                .Where(p => lineIds.Contains(p.RentalReturnLineId) && p.CompletedAtUtc == null)
                .Select(p => p.RentalReturnLineId!.Value).ToListAsync(ct))
            .ToHashSet();
        var linesByReturn = lines.GroupBy(l => l.RentalReturnId).ToDictionary(g => g.Key, g => g.ToList());
        var lang = tenant.Lang;
        // Condiciones (catálogo global en caché): código y etiqueta por id.
        var conditions = new Dictionary<int, (string Code, string Label)>();
        foreach (var id in lines.Select(l => l.ConditionLookupId).Distinct())
            if (await lookups.GetAsync(id, ct) is { } lc) conditions[id] = (lc.InternalCode, MultilingualText.Resolve(lc.LabelJson, lang));

        var rows = new List<DataRow>(returns.Count);
        foreach (var r in returns)
        {
            var rental = rentals.GetValueOrDefault(r.RentalId);
            var own = linesByReturn.GetValueOrDefault(r.RentalReturnId) ?? new();
            var codes = own.Select(l => conditions.TryGetValue(l.ConditionLookupId, out var c) ? c.Code : null).ToList();
            var distinct = RentalAnalyticsRules.DistinctConditions(codes);
            var labels = distinct.Select(code => conditions.Values.First(c => c.Code == code).Label);
            var reason = await lookups.GetAsync(r.ReasonLookupId, ct);
            rows.Add(new DataRow
            {
                ["Id"] = r.RentalReturnId,
                ["PublicId"] = r.PublicId.ToString(),
                ["Number"] = r.Number,
                ["RentalId"] = r.RentalId,
                ["RentalNumber"] = rental?.Number,
                ["ClientId"] = rental?.ClientId,
                ["ClientName"] = rental is null ? null : clients.GetValueOrDefault(rental.ClientId),
                ["LocationName"] = rental is null ? null : locations.GetValueOrDefault(rental.LocationId),
                ["ReturnedOn"] = r.ReturnedOn,
                ["PickupDate"] = rental?.PickupDate,
                ["Reason"] = reason is null ? null : MultilingualText.Resolve(reason.LabelJson, lang),
                ["ReasonCode"] = reason?.InternalCode,
                ["IsEarly"] = rental is not null && RentalRules.IsEarlyReturn(r.ReturnedOn, rental.PickupDate),
                ["DaysEarly"] = rental is null ? 0 : RentalAnalyticsRules.DaysEarly(r.ReturnedOn, rental.PickupDate),
                ["Condition"] = distinct.Count == 0 ? null : string.Join(", ", labels),
                ["ConditionCode"] = distinct.Count == 0 ? null : string.Join(",", distinct),
                ["HasDamage"] = codes.Contains(RentalReturnConditions.Damaged),
                ["Units"] = own.Count,
                ["GoodUnits"] = codes.Count(c => c == RentalReturnConditions.Good),
                ["DamagedUnits"] = codes.Count(c => c == RentalReturnConditions.Damaged),
                ["IncompleteUnits"] = codes.Count(c => c == RentalReturnConditions.Incomplete),
                ["UnitsWithProcess"] = own.Count(l => l.RequiresProcess),
                ["OpenProcesses"] = own.Count(l => openLines.Contains(l.RentalReturnLineId)),
                ["EstimatedPickupCost"] = r.EstimatedPickupCost,
                ["TransportCurrency"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, r.TransportCurrencyLookupId, ct),
                ["CreatedAtUtc"] = r.CreatedAtUtc,
            });
        }
        return rows;
    }
}

/// <summary>
/// Fuente RENTAL_PROCESS: una fila por proceso de un equipo devuelto. Actividad de período por StartedAtUtc (inicio del proceso);
/// la cola actual ('Equipos en proceso') va con rango ALL y IsOpen. IsOpen = el proceso no terminó (ni Lista ni Dada de baja, la
/// misma regla que la cola: RentalRules.IsProcessFinished); DaysInProcess = días de la compañía desde el inicio hasta el fin (o
/// hasta hoy si sigue abierto). Trae la devolución, la renta, el cliente y la condición con que volvió el equipo.
/// </summary>
public sealed class RentalProcessDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public string Key => EntityTypes.RentalProcess;
    public string LabelEs => "Proceso de equipos devueltos";
    public string LabelEn => "Returned equipment process";
    public string? EntityTypeCode => EntityTypes.RentalProcess;
    public string? DateField => "StartedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.RentalEquipment;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("SerialNumber", "Serie", "Serial", DataFieldType.Text),
        new DataField("ProductId", "Id de producto", "Product id", DataFieldType.Number),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("ProductName", "Producto", "Product", DataFieldType.Text),
        new DataField("WarehouseId", "Id de almacén", "Warehouse id", DataFieldType.Number),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("BinCode", "Posición", "Bin", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("IsOpen", "Abierto", "Open", DataFieldType.Bool),
        new DataField("ReturnId", "Id de devolución", "Return id", DataFieldType.Number),
        new DataField("ReturnNumber", "Devolución", "Return", DataFieldType.Text),
        new DataField("RentalId", "Id de renta", "Rental id", DataFieldType.Number),
        new DataField("RentalNumber", "Renta", "Rental", DataFieldType.Text),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("Condition", "Condición al volver", "Condition on return", DataFieldType.Text),
        new DataField("ConditionCode", "Código de condición", "Condition code", DataFieldType.Text),
        new DataField("StartedAtUtc", "Iniciado el", "Started at", DataFieldType.Date),
        new DataField("CompletedAtUtc", "Terminado el", "Completed at", DataFieldType.Date),
        new DataField("DaysInProcess", "Días en proceso", "Days in process", DataFieldType.Number),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Product", EntityTypes.Product, "ProductId", "Producto", "Product"),
        new DataRelation("Warehouse", EntityTypes.Warehouse, "WarehouseId", "Almacén", "Warehouse"),
        new DataRelation("Return", EntityTypes.RentalReturn, "ReturnId", "Devolución", "Return"),
        new DataRelation("Rental", EntityTypes.Rental, "RentalId", "Renta", "Rental"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.RentalProcesses.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(p => wanted.Contains(p.RentalProcessId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(p => p.StartedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(p => p.StartedAtUtc < to);

        var processes = await query.OrderByDescending(p => p.StartedAtUtc).ThenByDescending(p => p.RentalProcessId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (processes.Count == 0) return new List<DataRow>();

        var serialIds = processes.Select(p => p.SerialId).Distinct().ToList();
        var productIds = processes.Select(p => p.ProductId).Distinct().ToList();
        var binIds = processes.Select(p => p.BinId).Distinct().ToList();
        var lineIds = processes.Where(p => p.RentalReturnLineId.HasValue).Select(p => p.RentalReturnLineId!.Value).Distinct().ToList();
        var serials = await db.InventorySerials.AsNoTracking().Where(s => serialIds.Contains(s.SerialId))
            .ToDictionaryAsync(s => s.SerialId, s => s.SerialNumber, ct);
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.Sku, p.Name }).ToDictionaryAsync(p => p.ProductId, ct);
        var warehouses = await RentalDataSourceHelpers.WarehouseCodesAsync(db, processes.Select(p => p.WarehouseId).Distinct().ToList(), ct);
        var bins = await db.WarehouseBins.AsNoTracking().Where(b => binIds.Contains(b.WarehouseBinId))
            .ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var origins = await (from rl in db.RentalReturnLines.AsNoTracking()
                             join r in db.RentalReturns.AsNoTracking() on rl.RentalReturnId equals r.RentalReturnId
                             join rent in db.Rentals.AsNoTracking() on r.RentalId equals rent.RentalId
                             where lineIds.Contains(rl.RentalReturnLineId)
                             select new { rl.RentalReturnLineId, rl.ConditionLookupId, r.RentalReturnId, ReturnNumber = r.Number, rent.RentalId,
                                 RentalNumber = rent.Number, rent.ClientId })
            .ToDictionaryAsync(x => x.RentalReturnLineId, ct);
        var clients = await RentalDataSourceHelpers.ClientNamesAsync(db, origins.Values.Select(o => o.ClientId).Distinct().ToList(), ct);
        var statuses = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind)
            .Where(s => s.Entity == StatusDomains.RentalProcessStatus).ToDictionaryAsync(s => s.StatusCodeId, ct);
        var tc = clock ?? TenantClock.Default;
        var today = tc.Today;
        var lang = tenant.Lang;

        var rows = new List<DataRow>(processes.Count);
        foreach (var p in processes)
        {
            var status = statuses.GetValueOrDefault(p.StatusCodeId);
            var origin = p.RentalReturnLineId is int lid ? origins.GetValueOrDefault(lid) : null;
            var condition = origin is null ? null : await lookups.GetAsync(origin.ConditionLookupId, ct);
            var product = products.GetValueOrDefault(p.ProductId);
            rows.Add(new DataRow
            {
                ["Id"] = p.RentalProcessId,
                ["SerialNumber"] = serials.GetValueOrDefault(p.SerialId),
                ["ProductId"] = p.ProductId,
                ["Sku"] = product?.Sku,
                ["ProductName"] = product?.Name,
                ["WarehouseId"] = p.WarehouseId,
                ["WarehouseCode"] = warehouses.GetValueOrDefault(p.WarehouseId),
                ["BinCode"] = bins.GetValueOrDefault(p.BinId),
                ["Status"] = status is null ? null : MultilingualText.Resolve(status.LabelJson, lang),
                ["StatusCode"] = status?.InternalCode,
                ["IsOpen"] = !RentalRules.IsProcessFinished(status?.StageKind?.InternalCode, p.CompletedAtUtc),
                ["ReturnId"] = origin?.RentalReturnId,
                ["ReturnNumber"] = origin?.ReturnNumber,
                ["RentalId"] = origin?.RentalId,
                ["RentalNumber"] = origin?.RentalNumber,
                ["ClientName"] = origin is null ? null : clients.GetValueOrDefault(origin.ClientId),
                ["Condition"] = condition is null ? null : MultilingualText.Resolve(condition.LabelJson, lang),
                ["ConditionCode"] = condition?.InternalCode,
                ["StartedAtUtc"] = p.StartedAtUtc,
                ["CompletedAtUtc"] = p.CompletedAtUtc,
                ["DaysInProcess"] = RentalAnalyticsRules.DaysInProcess(tc.DayOf(p.StartedAtUtc),
                    p.CompletedAtUtc is DateTime done ? tc.DayOf(done) : null, today),
            });
        }
        return rows;
    }
}
