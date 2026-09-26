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
/// Lote 6 (P1) — jerarquía física del almacén (R2, R3): zonas tipadas, posiciones pasillo-rack-nivel-posición y muelles con
/// estatus. Toda hija (sin TenantId) se resuelve SIEMPRE a través de su almacén ya filtrado por tenant (WmsResolve): una
/// zona, posición o muelle de otro almacén u otro tenant es 404, sin oráculo.
/// - Zonas: código único por almacén e inmutable; tipo por catálogo ZoneType ('Tipo de zona desconocido: 'X'.'); baja
///   solo sin posiciones activas (409).
/// - Posiciones: código explícito o compuesto ('A01-R02-N3-P04'), único POR ALMACÉN (UQ_WarehouseBin_WhCode; 409);
///   WarehouseId sale de la zona; código y zona inmutables. Baja, en orden: bloqueo del almacén, rango de saldos de la
///   posición (HOLDLOCK), 409 con inventario (en mano o reservado) o con tareas abiertas que la usan, y solo entonces
///   IsActive = 0.
/// - Muelles: tipo por catálogo DockType; nacen FREE con historial WAREHOUSE_DOCK; estatus manual FREE/OCCUPIED/MAINTENANCE
///   vía StatusService; baja solo sin citas SCHEDULED/ARRIVED (409), con el muelle bloqueado.
/// - Nada se agrega ni se reactiva en un almacén dado de baja (422).
/// </summary>
public sealed class WarehouseLayoutService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses)
{
    private sealed record StatusInfo(string Code, string Label, string? Color);

    // ================================================================ zonas

    public async Task<IReadOnlyList<WarehouseZoneDto>> ListZonesAsync(Guid warehousePublicId, bool includeInactive, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var q = db.WarehouseZones.AsNoTracking().Where(z => z.WarehouseId == w.WarehouseId);
        if (!includeInactive) q = q.Where(z => z.IsActive);
        var zones = await q.OrderBy(z => z.Code).ToListAsync(ct);
        var binCounts = await ActiveBinCountsAsync(w.WarehouseId, ct);
        var list = new List<WarehouseZoneDto>(zones.Count);
        foreach (var z in zones) list.Add(await ZoneDtoAsync(z, binCounts.GetValueOrDefault(z.WarehouseZoneId), ct));
        return list;
    }

    public async Task<WarehouseZoneDto> CreateZoneAsync(Guid warehousePublicId, WarehouseZoneRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var (code, codeError) = WarehouseRules.NormalizeCode(req.Code);
        if (codeError is not null) errors["code"] = new[] { codeError };
        var name = RequiredText(req.Name, "name", "El nombre", WarehouseRules.ZoneNameMaxLength, errors);
        var typeId = await LookupIdAsync(LookupDomains.ZoneType, req.ZoneType, "zoneType", WarehouseRules.UnknownZoneType, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        if (await db.WarehouseZones.AnyAsync(z => z.WarehouseId == w.WarehouseId && z.Code == code, ct))
            throw new ConflictException(WarehouseRules.DuplicateZoneMessage);

        var zone = new WarehouseZone { WarehouseId = w.WarehouseId, Code = code!, Name = name!, ZoneTypeLookupId = typeId, IsActive = true };
        db.WarehouseZones.Add(zone);
        await db.SaveGuardedAsync(WarehouseRules.DuplicateZoneMessage, ct); // UQ_WarehouseZone (WarehouseId, Code)
        return await ZoneDtoAsync(zone, 0, ct);
    }

    /// <summary>PATCH de zona: nombre y tipo ("" quita el tipo). 'code' en el cuerpo → 400.</summary>
    public async Task<WarehouseZoneDto> UpdateZoneAsync(Guid warehousePublicId, int zoneId, WarehouseZonePatchRequest req, CancellationToken ct)
    {
        RejectImmutable(req.Extra, WarehouseRules.ZoneCodeImmutableMessage, "code", "warehouseId");
        var errors = new Dictionary<string, string[]>();
        string? name = req.Name is null ? null : RequiredText(req.Name, "name", "El nombre", WarehouseRules.ZoneNameMaxLength, errors);
        var typeId = await LookupIdAsync(LookupDomains.ZoneType, req.ZoneType, "zoneType", WarehouseRules.UnknownZoneType, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var zone = await ResolveZoneAsync(w, zoneId, track: true, ct);
        if (req.Name is not null) zone.Name = name!;
        if (req.ZoneType is not null) zone.ZoneTypeLookupId = typeId;
        await db.SaveGuardedAsync(WarehouseRules.DuplicateZoneMessage, ct);
        return await ZoneDtoAsync(zone, (await ActiveBinCountsAsync(w.WarehouseId, ct)).GetValueOrDefault(zone.WarehouseZoneId), ct);
    }

    /// <summary>Baja/reactivación de zona. Desactivar con posiciones activas → 409 'La zona tiene posiciones activas; desactívelas primero.'</summary>
    public async Task<WarehouseZoneDto> SetZoneActiveAsync(Guid warehousePublicId, int zoneId, bool active, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var zoneRef = await ResolveZoneAsync(w, zoneId, track: false, ct);
        if (active) EnsureWarehouseActive(w);

        await db.RunInTransactionAsync(async ct2 =>
        {
            // El almacén bloqueado serializa esta baja contra el alta/reactivación de posiciones de la zona.
            await LockWarehouseAsync(w.WarehouseId, ct2);
            var zone = await db.WarehouseZones.FirstAsync(z => z.WarehouseZoneId == zoneRef.WarehouseZoneId, ct2);
            if (zone.IsActive == active) return;
            if (!active && await db.WarehouseBins.AnyAsync(b => b.WarehouseZoneId == zone.WarehouseZoneId && b.IsActive, ct2))
                throw new ConflictException(WarehouseRules.ZoneHasActiveBins);
            zone.IsActive = active;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateZoneMessage, ct2);
        }, ct);

        var fresh = await ResolveZoneAsync(w, zoneId, track: false, ct);
        return await ZoneDtoAsync(fresh, (await ActiveBinCountsAsync(w.WarehouseId, ct)).GetValueOrDefault(fresh.WarehouseZoneId), ct);
    }

    // ================================================================ posiciones

    /// <summary>
    /// Posiciones del almacén (filtro por zona, búsqueda por código, inactivas opcionales, solo con existencia). Existencia y
    /// número de productos por posición en UNA consulta agrupada (sin N+1).
    /// </summary>
    public async Task<IReadOnlyList<WarehouseBinDto>> ListBinsAsync(Guid warehousePublicId, WarehouseBinQuery query, CancellationToken ct)
    {
        query ??= new WarehouseBinQuery();
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        if (query.ZoneId is int zid) await ResolveZoneAsync(w, zid, track: false, ct); // 404 si la zona no es de este almacén

        var q = from b in db.WarehouseBins.AsNoTracking()
                join z in db.WarehouseZones.AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                where b.WarehouseId == w.WarehouseId
                select new { Bin = b, ZoneCode = z.Code, z.ZoneTypeLookupId };
        if (!query.IncludeInactive) q = q.Where(x => x.Bin.IsActive);
        if (query.ZoneId is int zoneId) q = q.Where(x => x.Bin.WarehouseZoneId == zoneId);
        var rows = await q.OrderBy(x => x.Bin.Code).ToListAsync(ct);

        var stock = await BinStockAsync(w.WarehouseId, ct);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var list = new List<WarehouseBinDto>(rows.Count);
        foreach (var r in rows)
        {
            var s = stock.GetValueOrDefault(r.Bin.WarehouseBinId);
            if (query.OnlyWithStock && (s is null || s.OnHand == 0)) continue;
            if (search is not null
                && !r.Bin.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.ZoneCode.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(new WarehouseBinDto(r.Bin.WarehouseBinId, r.Bin.WarehouseZoneId, r.ZoneCode, await LookupCodeAsync(r.ZoneTypeLookupId, ct),
                r.Bin.Code, r.Bin.Aisle, r.Bin.Rack, r.Bin.Level, r.Bin.Position, r.Bin.MaxWeightKg, r.Bin.IsActive,
                s?.OnHand ?? 0, s?.Products ?? 0));
        }
        return list;
    }

    /// <summary>
    /// Alta de posición en una zona ACTIVA del almacén. Código explícito o compuesto de sus partes; único por almacén (409);
    /// capacidad de peso &gt; 0. WarehouseId sale de la zona (FK compuesta (Zona, Almacén) en SQL).
    /// </summary>
    public async Task<WarehouseBinDto> CreateBinAsync(Guid warehousePublicId, WarehouseBinRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (req.ZoneId is null) errors["zoneId"] = new[] { WarehouseRules.ZoneRequiredMessage };
        var (code, codeError) = WarehouseRules.ResolveBinCode(req.Code, req.Aisle, req.Rack, req.Level, req.Position);
        if (codeError is not null) errors["code"] = new[] { codeError };
        if (WarehouseRules.ValidateMaxWeight(req.MaxWeightKg) is string weightError) errors["maxWeightKg"] = new[] { weightError };
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var zone = await ResolveZoneAsync(w, req.ZoneId!.Value, track: false, ct);
        if (!zone.IsActive) throw new StatusRuleException(WarehouseRules.ZoneInactiveMessage);
        if (await db.WarehouseBins.AnyAsync(b => b.WarehouseId == w.WarehouseId && b.Code == code, ct))
            throw new ConflictException(WarehouseRules.DuplicateBinMessage);

        var bin = new WarehouseBin
        {
            WarehouseZoneId = zone.WarehouseZoneId, WarehouseId = zone.WarehouseId, Code = code!,
            Aisle = WarehouseRules.NormalizeBinPart(req.Aisle).Part, Rack = WarehouseRules.NormalizeBinPart(req.Rack).Part,
            Level = WarehouseRules.NormalizeBinPart(req.Level).Part, Position = WarehouseRules.NormalizeBinPart(req.Position).Part,
            MaxWeightKg = req.MaxWeightKg, IsActive = true,
        };
        db.WarehouseBins.Add(bin);
        await db.SaveGuardedAsync(WarehouseRules.DuplicateBinMessage, ct); // UQ_WarehouseBin_WhCode es la segunda barrera
        return new WarehouseBinDto(bin.WarehouseBinId, zone.WarehouseZoneId, zone.Code, await LookupCodeAsync(zone.ZoneTypeLookupId, ct),
            bin.Code, bin.Aisle, bin.Rack, bin.Level, bin.Position, bin.MaxWeightKg, bin.IsActive, 0, 0);
    }

    /// <summary>
    /// PATCH de posición: partes (null = sin cambio, "" = quitar) y capacidad (clearMaxWeight = sin límite). Código y zona son
    /// inmutables: 'code', 'zoneId' o 'warehouseZoneId' en el cuerpo → 400.
    /// </summary>
    public async Task<WarehouseBinDto> UpdateBinAsync(Guid warehousePublicId, int binId, WarehouseBinPatchRequest req, CancellationToken ct)
    {
        RejectImmutable(req.Extra, WarehouseRules.BinCodeImmutableMessage, "code", "zoneId", "warehouseZoneId", "warehouseId");
        var errors = new Dictionary<string, string[]>();
        var aisle = Part(req.Aisle, "aisle", errors);
        var rack = Part(req.Rack, "rack", errors);
        var level = Part(req.Level, "level", errors);
        var position = Part(req.Position, "position", errors);
        if (req.ClearMaxWeight != true && WarehouseRules.ValidateMaxWeight(req.MaxWeightKg) is string weightError) errors["maxWeightKg"] = new[] { weightError };
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var bin = await ResolveBinAsync(w, binId, track: true, ct);
        if (req.Aisle is not null) bin.Aisle = aisle;
        if (req.Rack is not null) bin.Rack = rack;
        if (req.Level is not null) bin.Level = level;
        if (req.Position is not null) bin.Position = position;
        if (req.ClearMaxWeight == true) bin.MaxWeightKg = null;
        else if (req.MaxWeightKg.HasValue) bin.MaxWeightKg = req.MaxWeightKg;
        await db.SaveGuardedAsync(WarehouseRules.DuplicateBinMessage, ct);
        return await BinDtoAsync(w.WarehouseId, bin.WarehouseBinId, ct);
    }

    /// <summary>
    /// Baja/reactivación de posición.
    /// - Desactivar, en orden: bloqueo del almacén, rango de saldos de la posición (HOLDLOCK), 409 'La posición {code} tiene
    ///   inventario; no se puede desactivar.' si hay en mano o reservado ≠ 0, 409 si alguna tarea abierta la usa (origen o
    ///   destino), y solo entonces IsActive = 0.
    /// - Reactivar exige el almacén y la zona activos (422).
    /// </summary>
    public async Task<WarehouseBinDto> SetBinActiveAsync(Guid warehousePublicId, int binId, bool active, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var binRef = await ResolveBinAsync(w, binId, track: false, ct);
        if (active) EnsureWarehouseActive(w);

        await db.RunInTransactionAsync(async ct2 =>
        {
            await LockWarehouseAsync(w.WarehouseId, ct2);
            var bin = await db.WarehouseBins.FirstAsync(b => b.WarehouseBinId == binRef.WarehouseBinId, ct2);
            if (bin.IsActive == active) return;
            if (active)
            {
                var zoneActive = await db.WarehouseZones.Where(z => z.WarehouseZoneId == bin.WarehouseZoneId).Select(z => z.IsActive).FirstAsync(ct2);
                if (!zoneActive) throw new StatusRuleException(WarehouseRules.ZoneInactiveMessage);
            }
            else
            {
                var balances = await LockBinBalancesAsync(bin.WarehouseBinId, ct2);
                if (balances.Any(b => b.QtyOnHand != 0 || b.QtyReserved != 0)) throw new ConflictException(WarehouseRules.BinNotEmpty(bin.Code));
                var openTaskIds = await StatusIdsAsync(StatusDomains.WarehouseTaskStatus, ct2, WarehouseTaskStatuses.Pending, WarehouseTaskStatuses.InProgress);
                if (await db.WarehouseTasks.AnyAsync(t => t.WarehouseId == w.WarehouseId && openTaskIds.Contains(t.StatusCodeId)
                                                          && (t.FromBinId == bin.WarehouseBinId || t.ToBinId == bin.WarehouseBinId), ct2))
                    throw new ConflictException(WarehouseRules.BinHasOpenTasks);
            }
            bin.IsActive = active;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateBinMessage, ct2);
        }, ct);

        return await BinDtoAsync(w.WarehouseId, binRef.WarehouseBinId, ct);
    }

    // ================================================================ muelles

    public async Task<IReadOnlyList<WarehouseDockDto>> ListDocksAsync(Guid warehousePublicId, bool includeInactive, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var q = db.WarehouseDocks.AsNoTracking().Where(d => d.WarehouseId == w.WarehouseId);
        if (!includeInactive) q = q.Where(d => d.IsActive);
        var docks = await q.OrderBy(d => d.Code).ToListAsync(ct);
        var statusMap = await StatusMapAsync(StatusDomains.DockStatus, ct);
        var list = new List<WarehouseDockDto>(docks.Count);
        foreach (var d in docks) list.Add(await DockDtoAsync(d, statusMap, ct));
        return list;
    }

    /// <summary>Alta de muelle: código único por almacén (409), tipo por catálogo DockType; nace FREE con historial WAREHOUSE_DOCK.</summary>
    public async Task<WarehouseDockDto> CreateDockAsync(Guid warehousePublicId, WarehouseDockRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var (code, codeError) = WarehouseRules.NormalizeCode(req.Code);
        if (codeError is not null) errors["code"] = new[] { codeError };
        int? typeId = null;
        if (string.IsNullOrWhiteSpace(req.DockType)) errors["dockType"] = new[] { WarehouseRules.DockTypeRequiredMessage };
        else typeId = await LookupIdAsync(LookupDomains.DockType, req.DockType, "dockType", WarehouseRules.UnknownDockType, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        if (await db.WarehouseDocks.AnyAsync(d => d.WarehouseId == w.WarehouseId && d.Code == code, ct))
            throw new ConflictException(WarehouseRules.DuplicateDockMessage);

        var initial = await statuses.GetInitialAsync(StatusDomains.DockStatus, ct);
        var dockId = await db.RunInTransactionAsync(async ct2 =>
        {
            var dock = new WarehouseDock
            {
                WarehouseId = w.WarehouseId, Code = code!, DockTypeLookupId = typeId!.Value, StatusCodeId = initial.StatusCodeId, IsActive = true,
            };
            db.WarehouseDocks.Add(dock);
            await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct2); // UQ_WarehouseDock (WarehouseId, Code)
            // Historial desde el inicio: null → FREE (etapa inicial del seed).
            var to = await statuses.TransitionAsync(StatusDomains.DockStatus, EntityTypes.WarehouseDock, dock.WarehouseDockId, null, initial.InternalCode, null, ct2);
            dock.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct2);
            return dock.WarehouseDockId;
        }, ct);

        return await DockDtoAsync(w, dockId, ct);
    }

    /// <summary>PATCH de muelle: solo el tipo. 'code' en el cuerpo → 400.</summary>
    public async Task<WarehouseDockDto> UpdateDockAsync(Guid warehousePublicId, int dockId, WarehouseDockPatchRequest req, CancellationToken ct)
    {
        RejectImmutable(req.Extra, WarehouseRules.DockCodeImmutableMessage, "code", "warehouseId");
        var errors = new Dictionary<string, string[]>();
        int? typeId = null;
        if (req.DockType is not null)
        {
            if (string.IsNullOrWhiteSpace(req.DockType)) errors["dockType"] = new[] { WarehouseRules.DockTypeRequiredMessage };
            else typeId = await LookupIdAsync(LookupDomains.DockType, req.DockType, "dockType", WarehouseRules.UnknownDockType, errors, ct);
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var dock = await ResolveDockAsync(w, dockId, track: true, ct);
        if (typeId is int tid) dock.DockTypeLookupId = tid;
        await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct);
        return await DockDtoAsync(w, dock.WarehouseDockId, ct);
    }

    /// <summary>
    /// Estatus manual del muelle (FREE, OCCUPIED o MAINTENANCE) vía StatusService, con comentario en el historial
    /// WAREHOUSE_DOCK. Otro código → 400; muelle o almacén inactivo → 422; transición ilegal → 422 del motor.
    /// </summary>
    public async Task<WarehouseDockDto> SetDockStatusAsync(Guid warehousePublicId, int dockId, WarehouseDockStatusRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Status)) throw new ValidationException("status", WarehouseRules.DockStatusRequiredMessage);
        if (!WarehouseRules.IsManualDockStatus(req.Status)) throw new ValidationException("status", WarehouseRules.DockStatusNotManual(req.Status.Trim()));
        var toCode = req.Status.Trim().ToUpperInvariant();
        var comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var dockRef = await ResolveDockAsync(w, dockId, track: false, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            // Muelle bloqueado: serializa contra la llegada/cierre de citas (DockAppointmentStatusEffect) y la baja.
            var dock = await LockDockAsync(w.WarehouseId, dockRef.WarehouseDockId, ct2);
            if (!dock.IsActive) throw new StatusRuleException(WarehouseRules.DockInactiveMessage);
            var to = await statuses.TransitionAsync(StatusDomains.DockStatus, EntityTypes.WarehouseDock, dock.WarehouseDockId, dock.StatusCodeId, toCode, comment, ct2);
            dock.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct2);
        }, ct);

        return await DockDtoAsync(w, dockRef.WarehouseDockId, ct);
    }

    /// <summary>Baja/reactivación de muelle. Desactivar con citas SCHEDULED/ARRIVED → 409 'El muelle tiene citas agendadas o en curso.'</summary>
    public async Task<WarehouseDockDto> SetDockActiveAsync(Guid warehousePublicId, int dockId, bool active, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var dockRef = await ResolveDockAsync(w, dockId, track: false, ct);
        if (active) EnsureWarehouseActive(w);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var dock = await LockDockAsync(w.WarehouseId, dockRef.WarehouseDockId, ct2);
            if (dock.IsActive == active) return;
            if (!active)
            {
                var ids = await StatusIdsAsync(StatusDomains.AppointmentStatus, ct2, AppointmentStatuses.Scheduled, AppointmentStatuses.Arrived);
                if (await db.DockAppointments.AnyAsync(a => a.WarehouseDockId == dock.WarehouseDockId && ids.Contains(a.StatusCodeId), ct2))
                    throw new ConflictException(WarehouseRules.DockHasAppointments);
            }
            dock.IsActive = active;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct2);
        }, ct);

        return await DockDtoAsync(w, dockRef.WarehouseDockId, ct);
    }

    // ================================================================ helpers

    private static void EnsureWarehouseActive(Warehouse w)
    {
        if (!w.IsActive) throw new StatusRuleException(WarehouseRules.WarehouseInactiveMessage);
    }

    /// <summary>Campos inmutables que llegan en Extra ([JsonExtensionData]) → 400 con el mensaje de la entidad.</summary>
    private static void RejectImmutable(IDictionary<string, System.Text.Json.JsonElement>? extra, string message, params string[] fields)
    {
        if (extra is null) return;
        foreach (var key in extra.Keys)
        {
            var hit = fields.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) throw new ValidationException(hit, message);
        }
    }

    private static string? RequiredText(string? value, string field, string label, int max, IDictionary<string, string[]> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { errors[field] = new[] { WarehouseRules.NameRequiredMessage }; return null; }
        if (v.Length > max) { errors[field] = new[] { WarehouseRules.TooLong(label, max) }; return null; }
        return v;
    }

    /// <summary>Parte de la posición en un PATCH: null = sin cambio; "" = quitar; otra = normalizada (400 si es inválida).</summary>
    private static string? Part(string? raw, string field, IDictionary<string, string[]> errors)
    {
        if (raw is null) return null;
        var (part, error) = WarehouseRules.NormalizeBinPart(raw);
        if (error is not null) errors[field] = new[] { error };
        return part;
    }

    /// <summary>Código de catálogo → id. null → null (sin cambio); "" → null (quitar); desconocido → error en el campo.</summary>
    private async Task<int?> LookupIdAsync(string domain, string? code, string field, Func<string, string> unknownMessage,
        IDictionary<string, string[]> errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var id = await lookups.TryGetIdAsync(domain, code.Trim().ToUpperInvariant(), ct);
        if (id is null) errors[field] = new[] { unknownMessage(code.Trim()) };
        return id;
    }

    private async Task<string?> LookupCodeAsync(int? id, CancellationToken ct)
        => id is int v ? (await lookups.GetAsync(v, ct))?.InternalCode : null;

    private async Task<LookupCode?> LookupAsync(int? id, CancellationToken ct) => id is int v ? await lookups.GetAsync(v, ct) : null;

    private async Task<WarehouseZoneDto> ZoneDtoAsync(WarehouseZone z, int binCount, CancellationToken ct)
    {
        var type = await LookupAsync(z.ZoneTypeLookupId, ct);
        return new WarehouseZoneDto(z.WarehouseZoneId, z.Code, z.Name, type?.InternalCode,
            type is null ? null : MultilingualText.Resolve(type.LabelJson, tenant.Lang), z.IsActive, binCount);
    }

    private async Task<Dictionary<int, int>> ActiveBinCountsAsync(int warehouseId, CancellationToken ct)
        => await db.WarehouseBins.AsNoTracking()
            .Where(b => b.WarehouseId == warehouseId && b.IsActive)
            .GroupBy(b => b.WarehouseZoneId)
            .Select(g => new { ZoneId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ZoneId, x => x.Count, ct);

    private sealed record BinStock(decimal OnHand, int Products);

    /// <summary>Existencia en mano y productos distintos con existencia por posición del almacén (una consulta agrupada).</summary>
    private async Task<Dictionary<int, BinStock>> BinStockAsync(int warehouseId, CancellationToken ct)
    {
        var rows = await db.StockBalances.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && s.WarehouseBinId != null && s.QtyOnHand != 0)
            .Select(s => new { BinId = s.WarehouseBinId!.Value, s.ProductId, s.QtyOnHand })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.BinId)
            .ToDictionary(g => g.Key, g => new BinStock(g.Sum(r => r.QtyOnHand), g.Select(r => r.ProductId).Distinct().Count()));
    }

    private async Task<WarehouseBinDto> BinDtoAsync(int warehouseId, int binId, CancellationToken ct)
    {
        var r = await (from b in db.WarehouseBins.AsNoTracking()
                       join z in db.WarehouseZones.AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                       where b.WarehouseId == warehouseId && b.WarehouseBinId == binId
                       select new { Bin = b, ZoneCode = z.Code, z.ZoneTypeLookupId })
            .FirstAsync(ct);
        var stock = await db.StockBalances.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && s.WarehouseBinId == binId && s.QtyOnHand != 0)
            .Select(s => new { s.ProductId, s.QtyOnHand })
            .ToListAsync(ct);
        return new WarehouseBinDto(r.Bin.WarehouseBinId, r.Bin.WarehouseZoneId, r.ZoneCode, await LookupCodeAsync(r.ZoneTypeLookupId, ct),
            r.Bin.Code, r.Bin.Aisle, r.Bin.Rack, r.Bin.Level, r.Bin.Position, r.Bin.MaxWeightKg, r.Bin.IsActive,
            stock.Sum(s => s.QtyOnHand), stock.Select(s => s.ProductId).Distinct().Count());
    }

    private async Task<WarehouseDockDto> DockDtoAsync(Warehouse w, int dockId, CancellationToken ct)
    {
        var dock = await db.WarehouseDocks.AsNoTracking().FirstAsync(d => d.WarehouseId == w.WarehouseId && d.WarehouseDockId == dockId, ct);
        return await DockDtoAsync(dock, await StatusMapAsync(StatusDomains.DockStatus, ct), ct);
    }

    private async Task<WarehouseDockDto> DockDtoAsync(WarehouseDock d, Dictionary<int, StatusInfo> statusMap, CancellationToken ct)
    {
        var type = await lookups.GetAsync(d.DockTypeLookupId, ct);
        var s = statusMap.GetValueOrDefault(d.StatusCodeId);
        return new WarehouseDockDto(d.WarehouseDockId, d.Code, type?.InternalCode ?? "",
            type is null ? "" : MultilingualText.Resolve(type.LabelJson, tenant.Lang), s?.Code ?? "", s?.Label ?? "", s?.Color, d.IsActive);
    }

    private async Task<List<int>> StatusIdsAsync(string domain, CancellationToken ct, params string[] codes)
        => await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == domain && codes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId)
            .ToListAsync(ct);

    /// <summary>Estatus del dominio con la etiqueta y el color personalizados del tenant si existen.</summary>
    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c =>
        {
            var o = overrides.GetValueOrDefault(c.StatusCodeId);
            return new StatusInfo(c.InternalCode,
                MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? c.ColorHex);
        });
    }

    // ================================================================ adaptadores a las costuras de P0 (WmsResolve, InventoryQueries)
    // Toda dependencia de las firmas de WmsResolve e InventoryQueries vive aquí. Las hijas se resuelven con el almacén ya
    // resuelto bajo el filtro de tenant: 404 'Zona no encontrada.' / 'Posición no encontrada.' / 'Muelle no encontrado.'

    private Task<Warehouse> ResolveWarehouseAsync(Guid publicId, CancellationToken ct)
        => db.ResolveWarehouseAsync(publicId, false, ct);

    private Task<WarehouseZone> ResolveZoneAsync(Warehouse w, int zoneId, bool track, CancellationToken ct)
        => db.ResolveZoneAsync(w.WarehouseId, zoneId, track, ct);

    private Task<WarehouseBin> ResolveBinAsync(Warehouse w, int binId, bool track, CancellationToken ct)
        => db.ResolveBinAsync(w.WarehouseId, binId, track, ct);

    private Task<WarehouseDock> ResolveDockAsync(Warehouse w, int dockId, bool track, CancellationToken ct)
        => db.ResolveDockAsync(w.WarehouseId, dockId, track, ct);

    /// <summary>Encabezado Warehouse con UPDLOCK (paso 1 del orden de bloqueo).</summary>
    private Task<Warehouse> LockWarehouseAsync(int warehouseId, CancellationToken ct)
        => db.LockWarehouseAsync(warehouseId, ct);

    /// <summary>Muelle con UPDLOCK, con JOIN al almacén del tenant (último encabezado del orden de bloqueo), tracked.</summary>
    private Task<WarehouseDock> LockDockAsync(int warehouseId, int dockId, CancellationToken ct)
        => db.LockDockAsync(warehouseId, dockId, ct);

    /// <summary>Rango de saldos de la posición con HOLDLOCK (paso 3 del orden de bloqueo).</summary>
    private async Task<IReadOnlyList<StockBalance>> LockBinBalancesAsync(int binId, CancellationToken ct)
        => await db.LockBalancesByBinAsync(binId, ct);
}
