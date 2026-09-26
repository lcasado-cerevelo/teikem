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

/// <summary>
/// Paradas de la orden (EntityType ORDER_STOP: historial de StopStatus, campos personalizados). OrderStop no lleva TenantId:
/// se alcanza SOLO a través de la orden, que sí pasa por el filtro global de tenant.
/// </summary>
public sealed class OrderStopOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.OrderStop;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.TransportOrders.AsNoTracking().SelectMany(o => o.Stops).AnyAsync(s => s.OrderStopId == id, ct);
}

/// <summary>Ciclo COD de la orden (EntityType ORDER_COD): su EntityId es el TransportOrderId de una orden con COD.</summary>
public sealed class OrderCodOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.OrderCod;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.TransportOrders.AsNoTracking().AnyAsync(o => o.TransportOrderId == id && o.CodStatusCodeId != null, ct);
}

/// <summary>Lote de importación de órdenes (EntityType IMPORT_BATCH), bajo el filtro global de tenant.</summary>
public sealed class ImportBatchOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.ImportBatch;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.ImportBatches.AsNoTracking().AnyAsync(b => b.ImportBatchId == id, ct);
}

/// <summary>Plantilla de importación (EntityType IMPORT_TEMPLATE), bajo el filtro global de tenant.</summary>
public sealed class ImportTemplateOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.ImportTemplate;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.ImportTemplates.AsNoTracking().AnyAsync(t => t.ImportTemplateId == id, ct);
}
