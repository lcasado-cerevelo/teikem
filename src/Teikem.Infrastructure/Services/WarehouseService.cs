using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P1) — pantalla de mantenimiento de almacenes (R6): lista, ficha con zonas y muelles, alta, edición en línea y baja
/// definitiva vigilada (D26). Las zonas, posiciones y muelles viven en WarehouseLayoutService.
/// - El TenantId sale del principal; Warehouse lleva filtro global de tenant y se expone por PublicId.
/// - Código único por compañía (UQ_Warehouse_Code sin filtro: no se libera tras la baja) e inmutable; país por defecto 'PR'.
/// - Nace ACTIVE con historial WAREHOUSE (StatusService); la baja es ACTIVE → INACTIVE (terminal) + IsActive = 0.
/// - Baja, en orden obligatorio (orden de bloqueo del lote: encabezado Warehouse, luego saldos):
///   1. bloqueo del almacén (UPDLOCK); 2. rango de saldos del almacén (HOLDLOCK); 3. 409 si hay en mano o reservado ≠ 0 o
///   documentos abiertos (recibos OPEN, conteos OPEN/COUNTED, tareas PENDING/IN_PROGRESS, recolecciones COLLECTED, planes
///   OPEN/ALLOCATED, citas SCHEDULED/ARRIVED), con Errors por tipo; 4. transición y IsActive = 0. Nada se escribe si falla.
/// </summary>
public sealed class WarehouseService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses)
{
    /// <summary>Campos que el PATCH rechaza aunque lleguen en el cuerpo (van a Extra por no estar en el contrato).</summary>
    private static readonly string[] ImmutableOnPatch = { "code" };

    private sealed record StatusInfo(string Code, string Label, string? Color, bool IsTerminal);

    // ---------------------------------------------------------------- lista

    /// <summary>Almacenes del tenant por código; por defecto solo los activos. Conteos y existencias sin N+1.</summary>
    public async Task<IReadOnlyList<WarehouseDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Warehouses.AsNoTracking();
        if (!includeInactive) q = q.Where(w => w.IsActive);
        var rows = await q.OrderBy(w => w.Code).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<WarehouseDto>();

        var ids = rows.Select(w => w.WarehouseId).ToList();
        var counts = await CountsAsync(ids, ct);
        var statusMap = await StatusMapAsync(StatusDomains.WarehouseStatus, ct);
        var list = new List<WarehouseDto>(rows.Count);
        foreach (var w in rows) list.Add(await ToDtoAsync(w, counts, statusMap, ct));
        return list;
    }

    // ---------------------------------------------------------------- ficha

    public async Task<WarehouseDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(publicId, track: false, ct);
        var counts = await CountsAsync(new List<int> { w.WarehouseId }, ct);
        var dto = await ToDtoAsync(w, counts, await StatusMapAsync(StatusDomains.WarehouseStatus, ct), ct);

        // Hijas SIEMPRE a través del almacén ya resuelto bajo el filtro de tenant (zona y muelle no llevan TenantId).
        var zones = await db.WarehouseZones.AsNoTracking().Where(z => z.WarehouseId == w.WarehouseId)
            .OrderBy(z => z.Code).ToListAsync(ct);
        var binCounts = await db.WarehouseBins.AsNoTracking()
            .Where(b => b.WarehouseId == w.WarehouseId && b.IsActive)
            .GroupBy(b => b.WarehouseZoneId)
            .Select(g => new { ZoneId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ZoneId, x => x.Count, ct);
        var zoneDtos = new List<WarehouseZoneDto>(zones.Count);
        foreach (var z in zones)
        {
            var type = z.ZoneTypeLookupId is int tid ? await lookups.GetAsync(tid, ct) : null;
            zoneDtos.Add(new WarehouseZoneDto(z.WarehouseZoneId, z.Code, z.Name, type?.InternalCode,
                type is null ? null : MultilingualText.Resolve(type.LabelJson, tenant.Lang), z.IsActive, binCounts.GetValueOrDefault(z.WarehouseZoneId)));
        }

        var docks = await db.WarehouseDocks.AsNoTracking().Where(d => d.WarehouseId == w.WarehouseId)
            .OrderBy(d => d.Code).ToListAsync(ct);
        var dockStatus = await StatusMapAsync(StatusDomains.DockStatus, ct);
        var dockDtos = new List<WarehouseDockDto>(docks.Count);
        foreach (var d in docks)
        {
            var type = await lookups.GetAsync(d.DockTypeLookupId, ct);
            var s = dockStatus.GetValueOrDefault(d.StatusCodeId);
            dockDtos.Add(new WarehouseDockDto(d.WarehouseDockId, d.Code, type?.InternalCode ?? "",
                type is null ? "" : MultilingualText.Resolve(type.LabelJson, tenant.Lang), s?.Code ?? "", s?.Label ?? "", s?.Color, d.IsActive));
        }

        return new WarehouseDetailDto(dto, zoneDtos, dockDtos);
    }

    // ---------------------------------------------------------------- alta

    /// <summary>Alta: código obligatorio, único e inmutable (409); nombre obligatorio; país por catálogo (default 'PR'); nace ACTIVE.</summary>
    public async Task<WarehouseDetailDto> CreateAsync(WarehouseCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var errors = new Dictionary<string, string[]>();

        var (code, codeError) = WarehouseRules.NormalizeCode(req.Code);
        if (codeError is not null) errors["code"] = new[] { codeError };
        var name = Text(req.Name, "name", "El nombre", WarehouseRules.NameMaxLength, errors);
        if (name is null && !errors.ContainsKey("name")) errors["name"] = new[] { WarehouseRules.NameRequiredMessage };
        var line1 = Text(req.Line1, "line1", "La dirección", WarehouseRules.Line1MaxLength, errors);
        var city = Text(req.City, "city", "El pueblo", WarehouseRules.CityMaxLength, errors);
        var state = Text(req.State, "state", "El estado", WarehouseRules.StateMaxLength, errors);
        var postal = Text(req.PostalCode, "postalCode", "El código postal", WarehouseRules.PostalCodeMaxLength, errors);
        var countryId = await CountryIdAsync(string.IsNullOrWhiteSpace(req.Country) ? WarehouseRules.DefaultCountry : req.Country, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        // UQ_Warehouse_Code (TenantId, Code) sin filtro: el código de un almacén dado de baja tampoco se reutiliza.
        if (await db.Warehouses.AnyAsync(w => w.Code == code, ct)) throw new ConflictException(WarehouseRules.DuplicateWarehouseMessage);

        var initial = await statuses.GetInitialAsync(StatusDomains.WarehouseStatus, ct);
        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var w = new Warehouse
            {
                TenantId = tenantId, Code = code!, Name = name!, Line1 = line1, City = city, State = state, PostalCode = postal,
                CountryLookupId = countryId!.Value, StatusCodeId = initial.StatusCodeId, IsActive = true,
            };
            db.Warehouses.Add(w);
            await db.SaveGuardedAsync(WarehouseRules.DuplicateWarehouseMessage, ct2);
            // Historial desde el inicio: null → etapa inicial habilitada (ACTIVE en el seed).
            var to = await statuses.TransitionAsync(StatusDomains.WarehouseStatus, EntityTypes.Warehouse, w.WarehouseId, null, initial.InternalCode, null, ct2);
            w.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateWarehouseMessage, ct2);
            return w.PublicId;
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- edición en línea

    /// <summary>
    /// PATCH: null = sin cambio; "" = quitar el valor (salvo el nombre, obligatorio). 'code' en el cuerpo → 400
    /// 'El código del almacén no se puede cambiar.' rowVersion opcional (409 si cambió). Un almacén dado de baja → 422.
    /// </summary>
    public async Task<WarehouseDetailDto> UpdateAsync(Guid publicId, WarehousePatchRequest req, CancellationToken ct)
    {
        if (req.Extra is not null)
            foreach (var key in req.Extra.Keys)
            {
                var hit = ImmutableOnPatch.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) throw new ValidationException(hit, WarehouseRules.WarehouseCodeImmutableMessage);
            }

        var errors = new Dictionary<string, string[]>();
        string? name = null;
        if (req.Name is not null)
        {
            name = Text(req.Name, "name", "El nombre", WarehouseRules.NameMaxLength, errors);
            if (name is null && !errors.ContainsKey("name")) errors["name"] = new[] { WarehouseRules.NameRequiredMessage };
        }
        var line1 = Text(req.Line1, "line1", "La dirección", WarehouseRules.Line1MaxLength, errors);
        var city = Text(req.City, "city", "El pueblo", WarehouseRules.CityMaxLength, errors);
        var state = Text(req.State, "state", "El estado", WarehouseRules.StateMaxLength, errors);
        var postal = Text(req.PostalCode, "postalCode", "El código postal", WarehouseRules.PostalCodeMaxLength, errors);
        int? countryId = null;
        if (!string.IsNullOrWhiteSpace(req.Country)) countryId = await CountryIdAsync(req.Country, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(publicId, track: true, ct);
        if (!w.IsActive) throw new StatusRuleException(WarehouseRules.WarehouseInactiveMessage);
        db.ApplyRowVersion(w, req.RowVersion);

        if (req.Name is not null) w.Name = name!;
        if (req.Line1 is not null) w.Line1 = line1;
        if (req.City is not null) w.City = city;
        if (req.State is not null) w.State = state;
        if (req.PostalCode is not null) w.PostalCode = postal;
        if (countryId is int cid) w.CountryLookupId = cid;

        await db.SaveGuardedAsync(WarehouseRules.DuplicateWarehouseMessage, ct);
        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- baja definitiva

    /// <summary>
    /// Baja definitiva (D26): ACTIVE → INACTIVE (terminal) + IsActive = 0, solo con el almacén vacío (en mano y reservado 0)
    /// y sin documentos abiertos; si no, 409 WarehouseNotEmpty con Errors por tipo. Un almacén ya dado de baja → 422.
    /// </summary>
    public async Task<WarehouseDetailDto> DeactivateAsync(Guid publicId, WarehouseDeactivateRequest? req, CancellationToken ct)
    {
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        var current = await ResolveWarehouseAsync(publicId, track: false, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            // 1. Encabezado (UPDLOCK) — orden de bloqueo del lote: Warehouse antes que saldos.
            var w = await LockWarehouseAsync(current.WarehouseId, ct2);
            if (!w.IsActive || await IsTerminalAsync(w.StatusCodeId, ct2))
                throw new StatusRuleException(WarehouseRules.WarehouseInactiveMessage);
            db.ApplyRowVersion(w, req?.RowVersion);

            // 2. Rango de saldos del almacén (HOLDLOCK): nadie inserta ni mueve saldo hasta el commit.
            var balances = await LockWarehouseBalancesAsync(w.WarehouseId, ct2);

            // 3. Verificación bajo bloqueo.
            var usage = new WarehouseUsage(
                balances.Sum(b => b.QtyOnHand),
                balances.Sum(b => b.QtyReserved),
                await OpenReceiptsAsync(w.WarehouseId, ct2),
                await OpenCycleCountsAsync(w.WarehouseId, ct2),
                await OpenTasksAsync(w.WarehouseId, ct2),
                await CollectedPickBatchesAsync(w.WarehouseId, ct2),
                await OpenCrossDockPlansAsync(w.WarehouseId, ct2),
                await ActiveAppointmentsAsync(w.WarehouseId, ct2));
            // CK_StockBalance_Qty garantiza en mano ≥ 0 y reservado ≥ 0: la suma es 0 solo si todas las filas lo son.
            var blockers = WarehouseRules.DeactivationBlockers(usage);
            if (blockers.Count > 0)
                throw new ConflictException(WarehouseRules.WarehouseNotEmpty(w.Code)) { Errors = blockers };

            // 4. Escritura: transición terminal con historial y baja lógica.
            var to = await statuses.TransitionAsync(StatusDomains.WarehouseStatus, EntityTypes.Warehouse, w.WarehouseId, w.StatusCodeId,
                WarehouseStatuses.Inactive, comment, ct2);
            w.StatusCodeId = to.StatusCodeId;
            w.IsActive = false;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateWarehouseMessage, ct2);
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- documentos abiertos (bajo el filtro de tenant)

    private async Task<int> OpenReceiptsAsync(int warehouseId, CancellationToken ct)
    {
        var ids = await StatusIdsAsync(StatusDomains.ReceiptStatus, ct, ReceiptStatuses.Open);
        return await db.ReceiptHeaders.CountAsync(r => r.WarehouseId == warehouseId && r.IsActive && ids.Contains(r.StatusCodeId), ct);
    }

    private async Task<int> OpenCycleCountsAsync(int warehouseId, CancellationToken ct)
    {
        var ids = await StatusIdsAsync(StatusDomains.CycleCountStatus, ct, CycleCountStatuses.Open, CycleCountStatuses.Counted);
        return await db.CycleCounts.CountAsync(c => c.WarehouseId == warehouseId && c.IsActive && ids.Contains(c.StatusCodeId), ct);
    }

    private async Task<int> OpenTasksAsync(int warehouseId, CancellationToken ct)
    {
        var ids = await StatusIdsAsync(StatusDomains.WarehouseTaskStatus, ct, WarehouseTaskStatuses.Pending, WarehouseTaskStatuses.InProgress);
        return await db.WarehouseTasks.CountAsync(t => t.WarehouseId == warehouseId && ids.Contains(t.StatusCodeId), ct);
    }

    private async Task<int> CollectedPickBatchesAsync(int warehouseId, CancellationToken ct)
    {
        var ids = await StatusIdsAsync(StatusDomains.PickBatchStatus, ct, PickBatchStatuses.Collected);
        return await db.PickBatches.CountAsync(p => p.WarehouseId == warehouseId && p.IsActive && ids.Contains(p.StatusCodeId), ct);
    }

    private async Task<int> OpenCrossDockPlansAsync(int warehouseId, CancellationToken ct)
    {
        var ids = await StatusIdsAsync(StatusDomains.CrossDockStatus, ct, CrossDockStatuses.Open, CrossDockStatuses.Allocated);
        return await db.CrossDockPlans.CountAsync(p => p.WarehouseId == warehouseId && ids.Contains(p.StatusCodeId), ct);
    }

    private async Task<int> ActiveAppointmentsAsync(int warehouseId, CancellationToken ct)
    {
        var ids = await StatusIdsAsync(StatusDomains.AppointmentStatus, ct, AppointmentStatuses.Scheduled, AppointmentStatuses.Arrived);
        return await db.DockAppointments.CountAsync(a => a.WarehouseId == warehouseId && ids.Contains(a.StatusCodeId), ct);
    }

    /// <summary>Ids de StatusCode del dominio con esos códigos internos (se consultan; nunca se hardcodean).</summary>
    private async Task<List<int>> StatusIdsAsync(string domain, CancellationToken ct, params string[] codes)
        => await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == domain && codes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId)
            .ToListAsync(ct);

    // ---------------------------------------------------------------- helpers

    private sealed record Counts(Dictionary<int, int> Zones, Dictionary<int, int> Bins, Dictionary<int, int> Docks, Dictionary<int, decimal> OnHand);

    /// <summary>Zonas, posiciones y muelles ACTIVOS y existencia en mano por almacén: cuatro consultas agrupadas (sin N+1).</summary>
    private async Task<Counts> CountsAsync(List<int> ids, CancellationToken ct)
    {
        var zones = await db.WarehouseZones.AsNoTracking().Where(z => ids.Contains(z.WarehouseId) && z.IsActive)
            .GroupBy(z => z.WarehouseId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var bins = await db.WarehouseBins.AsNoTracking().Where(b => ids.Contains(b.WarehouseId) && b.IsActive)
            .GroupBy(b => b.WarehouseId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var docks = await db.WarehouseDocks.AsNoTracking().Where(d => ids.Contains(d.WarehouseId) && d.IsActive)
            .GroupBy(d => d.WarehouseId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var onHand = await db.StockBalances.AsNoTracking().Where(s => ids.Contains(s.WarehouseId))
            .GroupBy(s => s.WarehouseId).Select(g => new { g.Key, Q = g.Sum(s => s.QtyOnHand) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
        return new Counts(zones, bins, docks, onHand);
    }

    private async Task<WarehouseDto> ToDtoAsync(Warehouse w, Counts counts, Dictionary<int, StatusInfo> statusMap, CancellationToken ct)
    {
        var country = await lookups.GetAsync(w.CountryLookupId, ct);
        var s = statusMap.GetValueOrDefault(w.StatusCodeId);
        return new WarehouseDto(w.WarehouseId, w.PublicId, w.Code, w.Name, w.Line1, w.City, w.State, w.PostalCode,
            country?.InternalCode ?? "", s?.Code ?? "", s?.Label ?? "", w.IsActive,
            counts.Zones.GetValueOrDefault(w.WarehouseId), counts.Bins.GetValueOrDefault(w.WarehouseId), counts.Docks.GetValueOrDefault(w.WarehouseId),
            counts.OnHand.GetValueOrDefault(w.WarehouseId), Convert.ToBase64String(w.RowVersion ?? Array.Empty<byte>()));
    }

    private async Task<int?> CountryIdAsync(string? country, IDictionary<string, string[]> errors, CancellationToken ct)
    {
        var code = country?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) return null;
        var id = await lookups.TryGetIdAsync(LookupDomains.Country, code, ct);
        if (id is null) errors["country"] = new[] { WarehouseRules.UnknownCountry(country!.Trim()) };
        return id;
    }

    /// <summary>null = sin valor/sin cambio; "" = NULL; otro = recortado (400 si excede el largo de la columna).</summary>
    private static string? Text(string? value, string field, string label, int max, IDictionary<string, string[]> errors)
    {
        if (value is null) return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (v.Length > max) errors[field] = new[] { WarehouseRules.TooLong(label, max) };
        return v;
    }

    private Task<bool> IsTerminalAsync(int statusCodeId, CancellationToken ct)
        => db.StatusCodes.AsNoTracking()
            .Where(s => s.StatusCodeId == statusCodeId)
            .Select(s => s.StageKind != null && s.StageKind.InternalCode == StageKinds.Terminal)
            .FirstOrDefaultAsync(ct);

    /// <summary>Estatus del dominio con la etiqueta y el color personalizados del tenant si existen.</summary>
    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c =>
        {
            var o = overrides.GetValueOrDefault(c.StatusCodeId);
            return new StatusInfo(c.InternalCode,
                MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? c.ColorHex,
                c.StageKind?.InternalCode == StageKinds.Terminal);
        });
    }

    // ================================================================ adaptadores a las costuras de P0 (WmsResolve, InventoryQueries)
    // Toda dependencia de las firmas de WmsResolve e InventoryQueries vive aquí.

    /// <summary>Almacén del tenant por PublicId (404 'Almacén no encontrado.'). Los dados de baja también se resuelven.</summary>
    private Task<Warehouse> ResolveWarehouseAsync(Guid publicId, bool track, CancellationToken ct)
        => db.ResolveWarehouseAsync(publicId, track, ct);

    /// <summary>Encabezado Warehouse con UPDLOCK (paso 1 del orden de bloqueo), tracked.</summary>
    private Task<Warehouse> LockWarehouseAsync(int warehouseId, CancellationToken ct)
        => db.LockWarehouseAsync(warehouseId, ct);

    /// <summary>Rango de saldos del almacén con HOLDLOCK (paso 3 del orden de bloqueo).</summary>
    private async Task<IReadOnlyList<StockBalance>> LockWarehouseBalancesAsync(int warehouseId, CancellationToken ct)
        => await db.LockBalancesByWarehouseAsync(warehouseId, ct);
}
