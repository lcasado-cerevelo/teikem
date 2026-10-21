using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-21 — Almacenes en el motor genérico de importación (3.ª entidad). Solo CREA: un código que ya existe (aunque el almacén esté dado de baja) o repetido en el archivo se rechaza. Revisar usa las reglas del
/// alta manual (<see cref="WarehouseService.CheckCreateAsync"/>, sin guardar); crear usa <see cref="WarehouseService.CreateAsync"/>.
/// </summary>
public sealed class WarehouseImportRows(WarehouseService warehouses) : IImportEntityRows
{
    public const string RepeatedInFileMessage = "El código del almacén está repetido en el archivo.";

    public string Kind => ImportKinds.Warehouse;

    private sealed class Context
    {
        public HashSet<string> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public Task<object> LoadContextAsync(CancellationToken ct) => Task.FromResult<object>(new Context());

    public async Task<ImportRowCheck> CheckRowAsync(object context, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        var ctx = (Context)context;
        string? Get(string f) => values.TryGetValue(f, out var v) && v.Length > 0 ? v : null;
        var request = new WarehouseCreateRequest(Get(WarehouseImportFields.Code), Get(WarehouseImportFields.Name), Get(WarehouseImportFields.Line1), Get(WarehouseImportFields.City), Get(WarehouseImportFields.State),
            Get(WarehouseImportFields.PostalCode), Get(WarehouseImportFields.Country), Get(WarehouseImportFields.ReceivingMode));
        var errors = new Dictionary<string, string>(await warehouses.CheckCreateAsync(request, ct), StringComparer.Ordinal);
        if (errors.Count == 0 && !ctx.Seen.Add(request.Code!.Trim())) errors[WarehouseImportFields.Code] = RepeatedInFileMessage;
        return new ImportRowCheck(errors, request.Code?.Trim().ToUpperInvariant(), request.Name, request);
    }

    public Task CommitRowAsync(object payload, CancellationToken ct) => warehouses.CreateAsync((WarehouseCreateRequest)payload, ct);
}

/// <summary>
/// 2026-10-21 — Posiciones de un almacén en el motor genérico de importación (4.ª entidad). El almacén y la zona se buscan por su código (activos); la posición trae su código o sus partes. Solo CREA: una posición cuyo código
/// ya existe en el almacén (activa o inactiva) o repetida en el archivo se rechaza. Revisar usa <see cref="WarehouseLayoutService.CheckCreateBinAsync"/> (las reglas del alta manual, sin guardar).
/// </summary>
public sealed class BinImportRows(TeikemDbContext db, WarehouseLayoutService layout) : IImportEntityRows
{
    public const string UnknownWarehouseMessage = "Almacén no encontrado por ese código.";
    public const string UnknownZoneMessage = "Zona no encontrada por ese código en ese almacén.";
    public const string RepeatedInFileMessage = "La posición está repetida en el archivo.";

    public string Kind => ImportKinds.Bin;

    private sealed record Payload(Guid WarehousePublicId, WarehouseBinRequest Request);

    private sealed class Context
    {
        public Dictionary<string, (int Id, Guid PublicId)> Warehouses { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<(int, string), int> Zones { get; init; } = new();
        public HashSet<(int, string)> Seen { get; } = new();
    }

    public async Task<object> LoadContextAsync(CancellationToken ct)
    {
        var whs = await db.Warehouses.AsNoTracking().Where(w => w.IsActive).Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToListAsync(ct);
        var zones = await db.WarehouseZones.AsNoTracking().Where(z => z.IsActive).Select(z => new { z.WarehouseId, z.Code, z.WarehouseZoneId }).ToListAsync(ct);
        return new Context
        {
            Warehouses = whs.ToDictionary(w => w.Code.Trim(), w => (w.WarehouseId, w.PublicId), StringComparer.OrdinalIgnoreCase),
            Zones = zones.ToDictionary(z => (z.WarehouseId, z.Code.Trim().ToUpperInvariant()), z => z.WarehouseZoneId),
        };
    }

    public async Task<ImportRowCheck> CheckRowAsync(object context, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        var ctx = (Context)context;
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        string? Get(string f) => values.TryGetValue(f, out var v) && v.Length > 0 ? v : null;

        decimal? weight = null;
        if (!ImportEntityMeta.TryDecimal(Get(BinImportFields.MaxWeightKg), out weight)) errors[BinImportFields.MaxWeightKg] = EntityImportService.NotANumberMessage;
        int? capacity = null;
        if (Get(BinImportFields.MaxCapacityQty) is { } capText)
        {
            if (int.TryParse(capText, out var c)) capacity = c;
            else errors[BinImportFields.MaxCapacityQty] = EntityImportService.NotANumberMessage;
        }

        var whCode = Get(BinImportFields.WarehouseCode);
        var zoneCode = Get(BinImportFields.ZoneCode);
        (int Id, Guid PublicId) wh = default;
        var whFound = whCode is not null && ctx.Warehouses.TryGetValue(whCode.Trim(), out wh);
        if (!whFound) errors[BinImportFields.WarehouseCode] = UnknownWarehouseMessage;
        int? zoneId = null;
        if (whFound && zoneCode is not null && ctx.Zones.TryGetValue((wh.Id, zoneCode.Trim().ToUpperInvariant()), out var z)) zoneId = z;
        else if (whFound) errors[BinImportFields.ZoneCode] = UnknownZoneMessage;

        var request = new WarehouseBinRequest(zoneId, Get(BinImportFields.Code), Get(BinImportFields.Aisle), Get(BinImportFields.Rack), Get(BinImportFields.Level), Get(BinImportFields.Position), weight, capacity);
        var resolved = WarehouseRules.ResolveBinCode(request.Code, request.Aisle, request.Rack, request.Level, request.Position).Code;
        if (errors.Count == 0)
        {
            foreach (var (field, message) in await layout.CheckCreateBinAsync(wh.PublicId, request, ct)) errors[field == "zoneId" ? BinImportFields.ZoneCode : field] = message;
            if (errors.Count == 0 && resolved is not null && !ctx.Seen.Add((wh.Id, resolved))) errors[BinImportFields.Code] = RepeatedInFileMessage;
        }
        return new ImportRowCheck(errors, resolved ?? request.Code, $"{whCode} / {zoneCode}", new Payload(wh.PublicId, request));
    }

    public Task CommitRowAsync(object payload, CancellationToken ct)
    {
        var p = (Payload)payload;
        return layout.CreateBinAsync(p.WarehousePublicId, p.Request, ct);
    }
}
