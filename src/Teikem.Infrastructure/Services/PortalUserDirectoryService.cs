using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 121 — la lista de TODAS las personas de portal de la compañía (las de los clientes y los accesos de los consignatarios), para que el administrador de la compañía (y el de la plataforma, dentro de la compañía que tiene
/// activa) las controle desde un solo lugar (regla del dueño, 2026-10-18). Solo LECTURA: las acciones usan los endpoints por cliente de <c>PortalUserService</c> (suspender, reactivar, dar de baja, reenviar), que ya dejan en la
/// bitácora de seguridad quién las hizo. La compañía sale del principal (filtro global del contexto); las dadas de baja no salen salvo que se pidan.
/// </summary>
public sealed class PortalUserDirectoryService(TeikemDbContext db, ITenantContext tenant)
{
    public const string KindClient = "CLIENT";
    public const string KindConsignee = "CONSIGNEE";
    public const string InvalidKindMessage = "El tipo debe ser CLIENT o CONSIGNEE.";

    public async Task<IReadOnlyList<PortalUserAdminDto>> ListAsync(string? kind, bool includeRemoved, CancellationToken ct)
    {
        var k = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim().ToUpperInvariant();
        if (k is not null && k != KindClient && k != KindConsignee) throw new Exceptions.ValidationException("kind", InvalidKindMessage);
        var q = db.PortalUsers.AsNoTracking().Include(p => p.Role).Include(p => p.Status).AsQueryable();
        if (k == KindClient) q = q.Where(p => p.LocationId == null);
        else if (k == KindConsignee) q = q.Where(p => p.LocationId != null);
        if (!includeRemoved) q = q.Where(p => p.IsActive && p.Status!.InternalCode != PortalUserStatuses.Disabled);
        var rows = await q.OrderBy(p => p.ClientId).ThenBy(p => p.Email).ToListAsync(ct);
        var clientIds = rows.Select(r => r.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => new { c.PublicId, c.Name, c.Code }, ct);
        var locIds = rows.Where(r => r.LocationId != null).Select(r => r.LocationId!.Value).Distinct().ToList();
        var locs = locIds.Count == 0 ? new Dictionary<int, string>() : await db.Set<Location>().AsNoTracking().Where(l => locIds.Contains(l.LocationId)).ToDictionaryAsync(l => l.LocationId, l => l.Name, ct);
        return rows.Select(p =>
        {
            var c = clients.GetValueOrDefault(p.ClientId);
            return new PortalUserAdminDto(p.PortalUserId, c?.PublicId ?? Guid.Empty, c?.Name ?? string.Empty, c?.Code ?? string.Empty, p.LocationId is null ? KindClient : KindConsignee,
                p.LocationId is int l ? locs.GetValueOrDefault(l) : null, p.Email, p.FullName, p.Role?.InternalCode ?? string.Empty, MultilingualText.Resolve(p.Role?.LabelJson, tenant.Lang) ?? p.Role?.InternalCode ?? string.Empty,
                p.Status?.InternalCode ?? string.Empty, MultilingualText.Resolve(p.Status?.LabelJson, tenant.Lang) ?? p.Status?.InternalCode ?? string.Empty, p.LastLoginUtc, p.IsActive);
        }).ToList();
    }
}
