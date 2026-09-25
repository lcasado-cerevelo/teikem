using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 2 — fuentes de datos de Clientes y contratos para vistas, indicadores y gráficos (módulos G/H/I).
/// Siguen el patrón de BuiltInDataSources: lectura AsNoTracking, tope de 20 000 filas, etiquetas resueltas con
/// ILookupCache/MultilingualText y db.StatusCodes, y respeto de q.Ids para resolver relaciones muchos-a-uno.
/// Los nombres de campo son los que usa SystemAnalyticsSeeder (vista "Clientes", indicador "Clientes activos",
/// gráfico "Contratos por estatus"): no cambiarlos sin cambiar el seeder.
/// </summary>
internal static class ClientDataSourceHelpers
{
    public const int MaxRows = 20000;

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    public static Task<Dictionary<int, StatusCode>> StatusMapAsync(TeikemDbContext db, string domain, CancellationToken ct)
        => db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToDictionaryAsync(s => s.StatusCodeId, ct);

    public static string? StatusLabel(IReadOnlyDictionary<int, StatusCode> map, int statusCodeId, string lang)
        => map.TryGetValue(statusCodeId, out var s) ? MultilingualText.Resolve(s.LabelJson, lang) : null;

    public static string? StatusCodeOf(IReadOnlyDictionary<int, StatusCode> map, int statusCodeId)
        => map.TryGetValue(statusCodeId, out var s) ? s.InternalCode : null;

    /// <summary>Etiqueta multilingüe de un LookupCode opcional (NULL si no hay id o no está en caché).</summary>
    public static async Task<string?> LookupLabelAsync(ILookupCache lookups, int? lookupCodeId, string lang, CancellationToken ct)
    {
        if (!lookupCodeId.HasValue) return null;
        var lc = await lookups.GetAsync(lookupCodeId.Value, ct);
        return lc is null ? null : MultilingualText.Resolve(lc.LabelJson, lang);
    }

    public static async Task<string?> LookupCodeAsync(ILookupCache lookups, int? lookupCodeId, CancellationToken ct)
    {
        if (!lookupCodeId.HasValue) return null;
        var lc = await lookups.GetAsync(lookupCodeId.Value, ct);
        return lc?.InternalCode;
    }

    /// <summary>
    /// Misma regla que ClientQueries.CurrentContractAsync, evaluada en memoria sobre los contratos ya cargados
    /// (evita N+1 en una fuente de hasta 20 000 clientes): ACTIVE con StartDate &lt;= hoy (la fecha fin NO se evalúa,
    /// decisión de Luis); si no hay, el DRAFT más reciente por StartDate; nunca EXPIRED/CANCELLED; null si no existe.
    /// </summary>
    public static Contract? PickCurrent(IEnumerable<Contract> own, IReadOnlyDictionary<int, StatusCode> statusMap, DateOnly asOf)
        => CurrentContractRule.Pick(own, c => statusMap.TryGetValue(c.StatusCodeId, out var s) ? s.InternalCode : null, asOf);

    public static string Summary(Contract c, string lang)
        => BillingModelSummary.Build(c.BillPerService, c.BillExtraPiece, c.BillDispatchFee, c.BillCodFee, c.BillSpecialServices, lang);
}

/// <summary>Fuente: clientes del tenant (estado actual → sin rango de fecha). Admite campos personalizados de CLIENT.</summary>
public sealed class ClientDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.Client;
    public string LabelEs => "Clientes";
    public string LabelEn => "Clients";
    public string? EntityTypeCode => EntityTypes.Client;
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;
    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Code", "Código", "Code", DataFieldType.Text),
        new DataField("Name", "Nombre", "Name", DataFieldType.Text),
        new DataField("LegalName", "Razón social", "Legal name", DataFieldType.Text),
        new DataField("TaxId", "Identificación fiscal", "Tax id", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("PaymentTerm", "Término de pago", "Payment term", DataFieldType.Text),
        new DataField("Currency", "Moneda", "Currency", DataFieldType.Text),
        new DataField("CreditLimit", "Límite de crédito", "Credit limit", DataFieldType.Number, IsMoney: true),
        new DataField("IsActive", "Activo", "Active", DataFieldType.Bool),
        new DataField("ClientAssignsOrderNumber", "Cliente asigna número de orden", "Client assigns order number", DataFieldType.Bool),
        new DataField("ClientAssignsInvoiceNumber", "Cliente asigna número de factura", "Client assigns invoice number", DataFieldType.Bool),
        new DataField("BillingSummary", "Modelo de facturación", "Billing model", DataFieldType.Text),
        new DataField("ContractsCount", "Contratos", "Contracts", DataFieldType.Number),
        new DataField("PickupLocationName", "Punto de recogido", "Pickup location", DataFieldType.Text),
        new DataField("CreatedAtUtc", "Creado", "Created", DataFieldType.Date),
    };
    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Clients.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(c => wanted.Contains(c.ClientId));
        }
        var clients = await query.OrderBy(c => c.Name).ThenBy(c => c.ClientId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (clients.Count == 0) return new List<DataRow>();

        var ids = clients.Select(c => c.ClientId).ToList();
        var lang = tenant.Lang;
        var today = ClientDataSourceHelpers.Today();
        var clientStatus = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.ClientStatus, ct);
        var contractStatus = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.ContractStatus, ct);

        // Contratos activos (soft delete) de los clientes cargados → contrato vigente + conteo
        var contracts = await db.Contracts.AsNoTracking().Where(c => ids.Contains(c.ClientId) && c.IsActive).ToListAsync(ct);
        var contractsBy = contracts.ToLookup(c => c.ClientId);

        // Punto de recogido: almacén por defecto (si sigue activo) o, en su defecto, la dirección corporativa activa
        var corporateTypeId = await lookups.TryGetIdAsync(LookupDomains.LocationType, LocationTypes.Corporate, ct);
        var defaultPickupIds = clients.Where(c => c.DefaultPickupLocationId.HasValue).Select(c => c.DefaultPickupLocationId!.Value).Distinct().ToList();
        var defaultPickups = defaultPickupIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Locations.AsNoTracking().Where(l => defaultPickupIds.Contains(l.LocationId) && l.IsActive)
                .Select(l => new { l.LocationId, l.Name }).ToDictionaryAsync(l => l.LocationId, l => l.Name, ct);
        var corporates = corporateTypeId is null
            ? new Dictionary<int, string>()
            : (await db.Locations.AsNoTracking()
                .Where(l => l.ClientId != null && ids.Contains(l.ClientId.Value) && l.IsActive && l.LocationTypeLookupId == corporateTypeId.Value)
                .OrderBy(l => l.LocationId).Select(l => new { ClientId = l.ClientId!.Value, l.Name }).ToListAsync(ct))
                .GroupBy(l => l.ClientId).ToDictionary(g => g.Key, g => g.First().Name);

        var rows = new List<DataRow>(clients.Count);
        foreach (var c in clients)
        {
            var own = contractsBy[c.ClientId].ToList();
            var current = ClientDataSourceHelpers.PickCurrent(own, contractStatus, today);
            string? pickupName = null;
            if (c.DefaultPickupLocationId.HasValue && defaultPickups.TryGetValue(c.DefaultPickupLocationId.Value, out var dp)) pickupName = dp;
            else if (corporates.TryGetValue(c.ClientId, out var corp)) pickupName = corp;

            rows.Add(new DataRow
            {
                ["Id"] = c.ClientId,
                ["PublicId"] = c.PublicId.ToString(),
                ["Code"] = c.Code,
                ["Name"] = c.Name,
                ["LegalName"] = c.LegalName,
                ["TaxId"] = c.TaxId,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(clientStatus, c.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(clientStatus, c.StatusCodeId),
                ["PaymentTerm"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, c.PaymentTermLookupId, lang, ct),
                ["Currency"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, c.CurrencyLookupId, ct),
                ["CreditLimit"] = c.CreditLimit,
                ["IsActive"] = c.IsActive,
                ["ClientAssignsOrderNumber"] = c.ClientAssignsOrderNumber,
                ["ClientAssignsInvoiceNumber"] = c.ClientAssignsInvoiceNumber,
                ["BillingSummary"] = current is null ? null : ClientDataSourceHelpers.Summary(current, lang),
                ["ContractsCount"] = own.Count,
                ["PickupLocationName"] = pickupName,
                ["CreatedAtUtc"] = c.CreatedAtUtc,
            });
        }
        return rows;
    }
}

/// <summary>Fuente: contratos del tenant. Actividad de período por StartDate (fecha de inicio del contrato).</summary>
public sealed class ContractDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.Contract;
    public string LabelEs => "Contratos";
    public string LabelEn => "Contracts";
    public string? EntityTypeCode => EntityTypes.Contract;
    public string? DateField => "StartDate";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;
    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("ContractNumber", "Número de contrato", "Contract number", DataFieldType.Text),
        new DataField("Title", "Título", "Title", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("StartDate", "Fecha de inicio", "Start date", DataFieldType.Date),
        new DataField("EndDate", "Fecha fin", "End date", DataFieldType.Date),
        new DataField("AutoRenew", "Renovación automática", "Auto-renew", DataFieldType.Bool),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("BillingTrigger", "Se factura al", "Billing trigger", DataFieldType.Text),
        new DataField("BillPerService", "Por servicio", "Per service", DataFieldType.Bool),
        new DataField("BillExtraPiece", "Pieza extra", "Extra piece", DataFieldType.Bool),
        new DataField("BillDispatchFee", "Despacho", "Dispatch", DataFieldType.Bool),
        new DataField("BillCodFee", "COD", "COD", DataFieldType.Bool),
        new DataField("BillSpecialServices", "Especiales", "Special", DataFieldType.Bool),
        new DataField("DispatchFee", "Cargo por despacho", "Dispatch fee", DataFieldType.Number, IsMoney: true),
        new DataField("CodFeeType", "Tipo de cargo COD", "COD fee type", DataFieldType.Text),
        new DataField("CodFeeValue", "Valor del cargo COD", "COD fee value", DataFieldType.Number),
        new DataField("IsActive", "Activo", "Active", DataFieldType.Bool),
    };
    public IReadOnlyList<DataRelation> Relations { get; } = new[] { new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client") };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Contracts.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(c => wanted.Contains(c.ContractId));
        }
        // Rango de fecha sobre StartDate (DATE): desde inclusivo, hasta exclusivo (el resolutor de rangos entrega "mañana 00:00")
        if (q.FromUtc.HasValue)
        {
            var from = DateOnly.FromDateTime(q.FromUtc.Value);
            query = query.Where(c => c.StartDate >= from);
        }
        if (q.ToUtc.HasValue)
        {
            var to = DateOnly.FromDateTime(q.ToUtc.Value);
            query = query.Where(c => c.StartDate < to);
        }
        var contracts = await query.OrderByDescending(c => c.StartDate).ThenByDescending(c => c.ContractId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (contracts.Count == 0) return new List<DataRow>();

        var lang = tenant.Lang;
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.ContractStatus, ct);
        var clientIds = contracts.Select(c => c.ClientId).Distinct().ToList();
        var clientNames = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);

        var rows = new List<DataRow>(contracts.Count);
        foreach (var c in contracts)
        {
            rows.Add(new DataRow
            {
                ["Id"] = c.ContractId,
                ["PublicId"] = c.PublicId.ToString(),
                ["ContractNumber"] = c.ContractNumber,
                ["Title"] = c.Title,
                ["ClientId"] = c.ClientId,
                ["ClientName"] = clientNames.GetValueOrDefault(c.ClientId),
                ["StartDate"] = c.StartDate,
                ["EndDate"] = c.EndDate,
                ["AutoRenew"] = c.AutoRenew,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, c.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, c.StatusCodeId),
                ["BillingTrigger"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, c.BillingModelLookupId, lang, ct),
                ["BillPerService"] = c.BillPerService,
                ["BillExtraPiece"] = c.BillExtraPiece,
                ["BillDispatchFee"] = c.BillDispatchFee,
                ["BillCodFee"] = c.BillCodFee,
                ["BillSpecialServices"] = c.BillSpecialServices,
                ["DispatchFee"] = c.DispatchFee,
                ["CodFeeType"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, c.CodFeeTypeLookupId, ct),
                ["CodFeeValue"] = c.CodFeeValue,
                ["IsActive"] = c.IsActive,
            });
        }
        return rows;
    }
}

/// <summary>Fuente: consignatarios y localizaciones (propias de un cliente o compartidas del tenant). Estado actual → sin rango de fecha.</summary>
public sealed class LocationDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.Location;
    public string LabelEs => "Consignatarios y localizaciones";
    public string LabelEn => "Locations";
    public string? EntityTypeCode => EntityTypes.Location;
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;
    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Code", "Código", "Code", DataFieldType.Text),
        new DataField("Name", "Nombre", "Name", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("IsShared", "Compartida", "Shared", DataFieldType.Bool),
        new DataField("LocationType", "Tipo", "Type", DataFieldType.Text),
        new DataField("LocationTypeCode", "Código de tipo", "Type code", DataFieldType.Text),
        new DataField("City", "Ciudad", "City", DataFieldType.Text),
        new DataField("State", "Estado", "State", DataFieldType.Text),
        new DataField("PostalCode", "Código postal", "Postal code", DataFieldType.Text),
        new DataField("Country", "País", "Country", DataFieldType.Text),
        new DataField("DefaultServiceMinutes", "Minutos de servicio", "Service minutes", DataFieldType.Number),
        new DataField("AllowDupInvoice", "Permite factura repetida", "Allow duplicate invoice", DataFieldType.Bool),
        new DataField("IsActive", "Activo", "Active", DataFieldType.Bool),
        new DataField("CreatedAtUtc", "Creado", "Created", DataFieldType.Date),
    };
    public IReadOnlyList<DataRelation> Relations { get; } = new[] { new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client") };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Locations.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(l => wanted.Contains(l.LocationId));
        }
        var locations = await query.OrderBy(l => l.Name).ThenBy(l => l.LocationId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (locations.Count == 0) return new List<DataRow>();

        var lang = tenant.Lang;
        var clientIds = locations.Where(l => l.ClientId.HasValue).Select(l => l.ClientId!.Value).Distinct().ToList();
        var clientNames = clientIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
                .Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);

        var rows = new List<DataRow>(locations.Count);
        foreach (var l in locations)
        {
            var type = await lookups.GetAsync(l.LocationTypeLookupId, ct);
            rows.Add(new DataRow
            {
                ["Id"] = l.LocationId,
                ["PublicId"] = l.PublicId.ToString(),
                ["Code"] = l.Code,
                ["Name"] = l.Name,
                ["ClientId"] = l.ClientId,
                ["ClientName"] = l.ClientId.HasValue ? clientNames.GetValueOrDefault(l.ClientId.Value) : null,
                ["IsShared"] = !l.ClientId.HasValue,
                ["LocationType"] = MultilingualText.Resolve(type?.LabelJson, lang),
                ["LocationTypeCode"] = type?.InternalCode,
                ["City"] = l.City,
                ["State"] = l.State,
                ["PostalCode"] = l.PostalCode,
                ["Country"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, l.CountryLookupId, lang, ct),
                ["DefaultServiceMinutes"] = l.DefaultServiceMinutes,
                ["AllowDupInvoice"] = l.AllowDupInvoice,
                ["IsActive"] = l.IsActive,
                ["CreatedAtUtc"] = l.CreatedAtUtc,
            });
        }
        return rows;
    }
}
