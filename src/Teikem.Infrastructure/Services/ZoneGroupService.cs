using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 129 — niveles de zona (Isla/Metro…): catálogo por compañía con orden y chofer por omisión del nivel. Lectura con fleet.view, escritura con fleet.manage (el controlador). Baja lógica;
/// no se inactiva con zonas activas que lo usen.
/// </summary>
public sealed class ZoneGroupService(TeikemDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<ZoneGroupDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.ZoneGroups.AsNoTracking().Include(g => g.DefaultDriver);
        var rows = await (includeInactive ? q : q.Where(g => g.IsActive)).OrderBy(g => g.SortOrder).ThenBy(g => g.Code).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<ZoneGroupDto>();
        var ids = rows.Select(r => r.ZoneGroupId).ToList();
        var counts = await db.DispatchZones.AsNoTracking().Where(z => z.IsActive && z.ZoneGroupId != null && ids.Contains(z.ZoneGroupId.Value))
            .GroupBy(z => z.ZoneGroupId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return rows.Select(r => ToDto(r, counts.GetValueOrDefault(r.ZoneGroupId))).ToList();
    }

    public async Task<ZoneGroupDto> CreateAsync(ZoneGroupRequest? req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var (code, codeError) = ZoneGroupRules.NormalizeCode(req?.Code);
        if (codeError is not null) throw new ValidationException("code", codeError);
        var (name, nameError) = ZoneGroupRules.ValidateName(req?.Name);
        if (nameError is not null) throw new ValidationException("name", nameError);
        if (await db.ZoneGroups.AnyAsync(g => g.Code == code, ct)) throw new ConflictException(ZoneGroupRules.DuplicateMessage);
        var group = new ZoneGroup { TenantId = tenantId, Code = code!, Name = name, SortOrder = req?.SortOrder ?? 0, IsActive = true };
        if (req?.DefaultDriverPublicId is Guid d) group.DefaultDriverId = await DriverIdAsync(d, ct);
        db.ZoneGroups.Add(group);
        await db.SaveGuardedAsync(ZoneGroupRules.DuplicateMessage, ct);
        return await GetAsync(group.ZoneGroupId, ct);
    }

    /// <summary>Nombre, orden y chofer por omisión; el código no cambia (400).</summary>
    public async Task<ZoneGroupDto> UpdateAsync(int id, ZoneGroupRequest? req, CancellationToken ct)
    {
        if (req?.Code is not null) throw new ValidationException("code", ZoneGroupRules.CodeImmutableMessage);
        var group = await LoadAsync(id, ct);
        if (req?.Name is not null)
        {
            var (name, nameError) = ZoneGroupRules.ValidateName(req.Name);
            if (nameError is not null) throw new ValidationException("name", nameError);
            group.Name = name;
        }
        if (req?.SortOrder is int so) group.SortOrder = so;
        if (req?.DefaultDriverPublicId is Guid d) group.DefaultDriverId = await DriverIdAsync(d, ct);
        else if (req?.ClearDefaultDriver == true) group.DefaultDriverId = null;
        await db.SaveGuardedAsync(ZoneGroupRules.DuplicateMessage, ct);
        return await GetAsync(group.ZoneGroupId, ct);
    }

    public async Task<ZoneGroupDto> SetActiveAsync(int id, bool active, CancellationToken ct)
    {
        var group = await LoadAsync(id, ct);
        if (!active && group.IsActive && await db.DispatchZones.AnyAsync(z => z.IsActive && z.ZoneGroupId == id, ct)) throw new ConflictException(ZoneGroupRules.InUseMessage);
        group.IsActive = active;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ZoneGroupDto> GetAsync(int id, CancellationToken ct)
    {
        var g = await db.ZoneGroups.AsNoTracking().Include(x => x.DefaultDriver).FirstOrDefaultAsync(x => x.ZoneGroupId == id, ct) ?? throw new NotFoundException("Nivel de zona", id);
        var count = await db.DispatchZones.AsNoTracking().CountAsync(z => z.IsActive && z.ZoneGroupId == id, ct);
        return ToDto(g, count);
    }

    /// <summary>El nivel activo por id (para la zona): 400 si no existe o está inactivo.</summary>
    public async Task<ZoneGroup> RequireActiveAsync(int id, CancellationToken ct)
        => await db.ZoneGroups.AsNoTracking().FirstOrDefaultAsync(g => g.ZoneGroupId == id && g.IsActive, ct) ?? throw new ValidationException("zoneGroupId", ZoneGroupRules.NotFoundMessage);

    private async Task<ZoneGroup> LoadAsync(int id, CancellationToken ct)
        => await db.ZoneGroups.FirstOrDefaultAsync(g => g.ZoneGroupId == id, ct) ?? throw new NotFoundException("Nivel de zona", id);

    private async Task<int> DriverIdAsync(Guid publicId, CancellationToken ct)
        => await db.Drivers.AsNoTracking().Where(d => d.PublicId == publicId && d.IsActive).Select(d => (int?)d.DriverId).FirstOrDefaultAsync(ct) ?? throw new ValidationException("defaultDriverPublicId", ZoneGroupRules.DriverNotFoundMessage);

    private static ZoneGroupDto ToDto(ZoneGroup g, int zoneCount)
        => new(g.ZoneGroupId, g.Code, g.Name, g.SortOrder, g.DefaultDriver?.PublicId, g.DefaultDriver?.EmployeeCode, g.DefaultDriver?.FullName, zoneCount, g.IsActive);
}
