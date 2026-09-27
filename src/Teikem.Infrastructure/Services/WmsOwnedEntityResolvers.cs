using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

// Lote 6 (P0) — resolvers de pertenencia de Inventario y almacén para las asociaciones polimórficas (ContactPoint,
// CustomFieldValue). Los encabezados con TenantId se consultan bajo el filtro global; WAREHOUSE_DOCK (sin TenantId) se
// alcanza SIEMPRE por JOIN a su almacén filtrado. INVENTORY_SERIAL, WAREHOUSE_TASK y CROSSDOCK_ALLOCATION no tienen permiso
// de escritura de dueño: se registran con ClosedOwnedEntityResolver en DependencyInjection (siempre 404, sin oráculo).
// Un id de otro tenant o inexistente responde false → 404.

public sealed class WarehouseOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Warehouse;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.Warehouses.AsNoTracking().AnyAsync(w => w.WarehouseId == id, ct);
}

public sealed class WarehouseDockOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.WarehouseDock;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => (from d in db.WarehouseDocks.AsNoTracking()
            join w in db.Warehouses.AsNoTracking() on d.WarehouseId equals w.WarehouseId
            where d.WarehouseDockId == id
            select d.WarehouseDockId).AnyAsync(ct);
}

public sealed class ProductOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Product;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.Products.AsNoTracking().AnyAsync(p => p.ProductId == id, ct);
}

public sealed class ReceiptOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Receipt;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.ReceiptHeaders.AsNoTracking().AnyAsync(r => r.ReceiptHeaderId == id, ct);
}

public sealed class AsnOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Asn;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.Asns.AsNoTracking().AnyAsync(a => a.AsnId == id, ct);
}

public sealed class CycleCountOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.CycleCount;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.CycleCounts.AsNoTracking().AnyAsync(c => c.CycleCountId == id, ct);
}

public sealed class PickBatchOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.PickBatch;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.PickBatches.AsNoTracking().AnyAsync(b => b.PickBatchId == id, ct);
}

public sealed class DockAppointmentOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.DockAppointment;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.DockAppointments.AsNoTracking().AnyAsync(a => a.DockAppointmentId == id, ct);
}

public sealed class CrossDockPlanOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.CrossDockPlan;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.CrossDockPlans.AsNoTracking().AnyAsync(p => p.CrossDockPlanId == id, ct);
}

public sealed class PurchaseOrderOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.PurchaseOrder;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.PurchaseOrders.AsNoTracking().AnyAsync(p => p.PurchaseOrderId == id, ct);
}

public sealed class SupplierOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Supplier;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.Suppliers.AsNoTracking().AnyAsync(s => s.SupplierId == id, ct);
}
