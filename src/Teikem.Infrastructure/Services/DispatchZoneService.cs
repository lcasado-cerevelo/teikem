using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P2) — CRUD mínimo de zonas de despacho (código, nombre, activo). El nombre de la zona primaria es el 'Área'
/// del chofer. Los miembros de la zona (CP/municipios) y las zonas secundarias quedan para Despacho.
/// - Código obligatorio, en mayúsculas, máximo 20, único por compañía (UQ_DispatchZone) e inmutable.
/// - Nunca DELETE: inactivar (IsActive = 0) exige que no tenga choferes activos asignados (409).
/// </summary>
public sealed class DispatchZoneService(TeikemDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<DispatchZoneDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.DispatchZones.AsNoTracking();
        if (!includeInactive) q = q.Where(z => z.IsActive);
        var zones = await q.OrderBy(z => z.Code).ToListAsync(ct);
        if (zones.Count == 0) return Array.Empty<DispatchZoneDto>();
        var counts = await DriverCountsAsync(zones.Select(z => z.DispatchZoneId).ToList(), ct);
        return zones.Select(z => ToDto(z, counts.GetValueOrDefault(z.DispatchZoneId))).ToList();
    }

    public async Task<DispatchZoneDto> CreateAsync(DispatchZoneRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var (code, codeError) = FleetRules.NormalizeCode(req.Code, DriverRules.ZoneCodeMaxLength, DriverRules.ZoneCodeRequiredMessage);
        if (codeError is not null) throw new ValidationException("code", codeError);
        var (name, nameError) = DriverRules.ValidateZoneName(req.Name);
        if (nameError is not null) throw new ValidationException("name", nameError);
        if (await db.DispatchZones.AnyAsync(z => z.Code == code, ct)) throw new ConflictException(DriverRules.DuplicateZoneMessage);

        var zone = new DispatchZone { TenantId = tenantId, Code = code!, Name = name, IsActive = true };
        db.DispatchZones.Add(zone);
        await db.SaveGuardedAsync(DriverRules.DuplicateZoneMessage, ct); // UQ_DispatchZone (TenantId, Code) es la segunda barrera
        return ToDto(zone, 0);
    }

    /// <summary>Solo el nombre es editable ("" lo vacía). El código se fija al crear la zona: si llega, 400.</summary>
    public async Task<DispatchZoneDto> UpdateAsync(int id, DispatchZoneRequest req, CancellationToken ct)
    {
        if (req.Code is not null) throw new ValidationException("code", DriverRules.ZoneCodeImmutableMessage);
        var zone = await LoadAsync(id, ct);
        if (req.Name is not null)
        {
            var (name, nameError) = DriverRules.ValidateZoneName(req.Name);
            if (nameError is not null) throw new ValidationException("name", nameError);
            zone.Name = name;
            await db.SaveGuardedAsync(DriverRules.DuplicateZoneMessage, ct);
        }
        return ToDto(zone, (await DriverCountsAsync(new List<int> { zone.DispatchZoneId }, ct)).GetValueOrDefault(zone.DispatchZoneId));
    }

    /// <summary>Baja lógica o reactivación. Inactivar con choferes activos asignados (zona primaria) es 409.</summary>
    public async Task<DispatchZoneDto> SetActiveAsync(int id, bool active, CancellationToken ct)
    {
        var zone = await LoadAsync(id, ct);
        var count = (await DriverCountsAsync(new List<int> { zone.DispatchZoneId }, ct)).GetValueOrDefault(zone.DispatchZoneId);
        if (zone.IsActive != active)
        {
            if (!active && count > 0) throw new ConflictException(DriverRules.ZoneHasDriversMessage);
            zone.IsActive = active;
            await db.SaveGuardedAsync(DriverRules.DuplicateZoneMessage, ct);
        }
        return ToDto(zone, count);
    }

    // ---------------- Helpers ----------------

    private async Task<DispatchZone> LoadAsync(int id, CancellationToken ct)
        => await db.DispatchZones.FirstOrDefaultAsync(z => z.DispatchZoneId == id, ct)
           ?? throw new NotFoundException("Zona de despacho", null, true);

    /// <summary>Choferes activos asignados a cada zona (cualquier fila DriverZone; el chofer bajo el filtro de tenant).</summary>
    private async Task<Dictionary<int, int>> DriverCountsAsync(List<int> zoneIds, CancellationToken ct)
        => await (from dz in db.DriverZones.AsNoTracking()
                  join d in db.Drivers.AsNoTracking() on dz.DriverId equals d.DriverId
                  where zoneIds.Contains(dz.DispatchZoneId) && d.IsActive
                  group dz by dz.DispatchZoneId into g
                  select new { ZoneId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ZoneId, x => x.Count, ct);

    private static DispatchZoneDto ToDto(DispatchZone z, int driverCount) => new(z.DispatchZoneId, z.Code, z.Name, driverCount, z.IsActive);
}
