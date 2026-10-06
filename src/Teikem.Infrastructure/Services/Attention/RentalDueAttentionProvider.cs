using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 29 (Rentas R3) — filas de "Necesita tu atención" por renta abierta (Programada o En renta) VENCIDA (recogido antes de hoy)
/// o POR VENCER en los próximos 7 días (recogido entre hoy y hoy + 7). Módulo Rentas (RENTAL_EQUIPMENT) y rental.view: lo ve quien
/// ve rentas. "Hoy" es el día de la compañía (ITenantClock); vencida/por vencer son datos calculados, no estatus (mismas reglas que
/// GET /rentals?dueWithinDays=7&amp;overdue=true). Una fila por renta, las de recogido más antiguo primero (las más vencidas
/// arriba), con lo necesario para "Revisar": ruta /warehouse/rentals con rental=&lt;publicId&gt; (pantalla de F-R1). Total = rentas
/// vencidas o por vencer de la compañía para "Ver todos (N)" (misma ruta con dueWithinDays=7 y overdue=true, los filtros de la
/// lista). Tono: danger si está vencida, warn si está por vencer. SinceUtc = 00:00 locales del día de recogido. El tenant sale del
/// filtro global (Rental es ITenantScoped).
/// Params (cadenas): publicId, number, client, location, warehouse, pickupDate (aaaa-mm-dd), daysToPickup (negativo = vencida),
/// overdue (true|false), units (equipos activos sin devolver) y status (código de estatus de la renta).
/// </summary>
public sealed class RentalDueAttentionProvider(TeikemDbContext db, ITenantClock? clock = null) : IAttentionItemProvider
{
    public const string ItemCode = "RENTAL_DUE";
    public const string Route = "/warehouse/rentals";
    public const string ToneOverdue = "danger";
    public const string ToneDueSoon = "warn";

    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.RentalEquipment;
    public string RequiredPermission => PermissionCatalog.RentalView;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        var today = _clock.Today;
        var limit = today.AddDays(RentalAnalyticsRules.DueSoonDays);
        var openIds = await db.StatusIdsAsync(StatusDomains.RentalStatus, RentalStatuses.OpenCodes, ct);
        var due = db.Rentals.AsNoTracking().Where(r => openIds.Contains(r.StatusCodeId) && r.PickupDate <= limit);
        var total = await due.CountAsync(ct);
        if (total == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);

        var rows = await due.OrderBy(r => r.PickupDate).ThenBy(r => r.RentalId).Take(Math.Max(0, take))
            .Select(r => new { r.RentalId, r.PublicId, r.Number, r.ClientId, r.LocationId, r.WarehouseId, r.PickupDate, r.StatusCodeId })
            .ToListAsync(ct);
        var rentalIds = rows.Select(r => r.RentalId).ToList();
        var units = await db.RentalLines.AsNoTracking().Where(l => rentalIds.Contains(l.RentalId) && l.IsActive && l.ReturnedAtUtc == null)
            .GroupBy(l => l.RentalId).Select(g => new { RentalId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RentalId, x => x.Count, ct);
        var clientIds = rows.Select(r => r.ClientId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var locationIds = rows.Select(r => r.LocationId).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.LocationId))
            .Select(l => new { l.LocationId, l.Name }).ToDictionaryAsync(l => l.LocationId, l => l.Name, ct);
        var warehouseIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
        var statusIds = rows.Select(r => r.StatusCodeId).Distinct().ToList();
        var statuses = await db.StatusCodes.AsNoTracking().Where(s => statusIds.Contains(s.StatusCodeId))
            .ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);

        var items = new List<AttentionItemDto>(rows.Count);
        foreach (var r in rows)
        {
            var status = statuses.GetValueOrDefault(r.StatusCodeId);
            var overdue = RentalRules.IsOverdue(status, r.PickupDate, today);
            var publicId = r.PublicId.ToString();
            var parameters = new Dictionary<string, string>
            {
                ["publicId"] = publicId,
                ["number"] = r.Number,
                ["client"] = clients.GetValueOrDefault(r.ClientId) ?? string.Empty,
                ["location"] = locations.GetValueOrDefault(r.LocationId) ?? string.Empty,
                ["warehouse"] = warehouses.GetValueOrDefault(r.WarehouseId) ?? string.Empty,
                ["pickupDate"] = r.PickupDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["daysToPickup"] = RentalRules.DaysToPickup(r.PickupDate, today).ToString(CultureInfo.InvariantCulture),
                ["overdue"] = overdue ? "true" : "false",
                ["units"] = units.GetValueOrDefault(r.RentalId).ToString(CultureInfo.InvariantCulture),
                ["status"] = status ?? string.Empty,
            };
            items.Add(new AttentionItemDto(ItemCode, BusinessModules.Warehouse, overdue ? ToneOverdue : ToneDueSoon, 1, parameters, Route,
                new Dictionary<string, string> { ["rental"] = publicId }, _clock.StartOfDayUtc(r.PickupDate)));
        }
        var group = new AttentionGroupDto(ItemCode, BusinessModules.Warehouse, total, Route, GroupQuery);
        return new AttentionProviderResult(total, items, group);
    }

    /// <summary>"Ver todos": la lista de rentas con los filtros del aviso (vencidas o por vencer en 7 días).</summary>
    public static IReadOnlyDictionary<string, string> GroupQuery { get; } = new Dictionary<string, string>
    {
        ["dueWithinDays"] = RentalAnalyticsRules.DueSoonDays.ToString(CultureInfo.InvariantCulture),
        ["overdue"] = "true",
    };
}
