using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P8) — cierra un hueco del Lote 2: PORTAL_USER tiene OwnerReadPermission/OwnerWritePermission en PermissionCatalog
/// pero no tenía IOwnedEntityResolver, y CustomFieldService omite la verificación de pertenencia cuando no hay resolver
/// (un id de otro tenant podía recibir valores de campos personalizados). PortalUser lleva TenantId: el filtro global de
/// tenant basta; un id ajeno o inexistente responde false → 404.
/// </summary>
public sealed class PortalUserOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.PortalUser;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.PortalUsers.AsNoTracking().AnyAsync(p => p.PortalUserId == id, ct);
}
