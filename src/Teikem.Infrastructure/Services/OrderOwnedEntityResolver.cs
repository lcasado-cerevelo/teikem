using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 3 (P5) — resolver de pertenencia de la orden de transporte para las asociaciones polimórficas
/// (ContactPoint, CustomFieldValue) y el historial de estatus: responde si el id existe en el tenant activo.
/// TransportOrder lleva TenantId y queda bajo el filtro global; junto con OwnerReadPermission/OwnerWritePermission
/// (orders.view / orders.edit, PermissionCatalog) cierra contactos, valores de campos personalizados e historial
/// de la orden a esos permisos.
/// </summary>
public sealed class TransportOrderOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.TransportOrder;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.TransportOrders.AsNoTracking().AnyAsync(o => o.TransportOrderId == id, ct);
}
