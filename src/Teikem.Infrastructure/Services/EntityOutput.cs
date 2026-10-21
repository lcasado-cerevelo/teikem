using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-21 — La parte de una entidad de catálogo (productos…) para la SALIDA por Integración con su plantilla: los campos que sabe dar, todos sus ids (para «enviar todo») y las filas de unos ids como
/// campo → valor. Una por entidad, registrada en DependencyInjection; el espejo de <see cref="IImportEntityRows"/>.
/// </summary>
public interface IExportEntityRows
{
    string Kind { get; }
    Task<IReadOnlyList<int>> AllIdsAsync(CancellationToken ct);
    Task<List<IReadOnlyDictionary<string, object?>>> BuildAsync(IReadOnlyCollection<int> ids, CancellationToken ct);
}

/// <summary>
/// Anota los registros de un catálogo que cambiaron en la cola de cada integración de salida (encendida) de esa entidad: al cambiar un producto, sale en el siguiente envío (al instante o por horario).
/// Un fallo aquí nunca deshace el cambio del producto: la cola es un aviso, el cambio ya está guardado.
/// </summary>
public sealed class OutputEntityRecorder(TeikemDbContext db)
{
    public async Task RecordAsync(string kind, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        try
        {
            var integrations = await db.Integrations.AsNoTracking().Where(i => i.IsActive && i.Direction == IntegrationDirections.Outbound && i.EntityKind == kind).Select(i => new { i.IntegrationId, i.TenantId }).ToListAsync(ct);
            foreach (var integration in integrations)
            {
                var open = await db.IntegrationOutputEntities.Where(q => q.IntegrationId == integration.IntegrationId && q.IntegrationOutputId == null && ids.Contains(q.EntityId)).Select(q => q.EntityId).ToListAsync(ct);
                foreach (var id in ids.Except(open))
                    db.IntegrationOutputEntities.Add(new IntegrationOutputEntity { TenantId = integration.TenantId, IntegrationId = integration.IntegrationId, EntityId = id, QueuedAtUtc = DateTime.UtcNow });
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
        }
    }
}

/// <summary>Productos para la salida: una fila por producto con los campos de la plantilla de productos (los mismos de la importación) como campo → valor.</summary>
public sealed class ProductExportRows(TeikemDbContext db) : IExportEntityRows
{
    public string Kind => ImportKinds.Product;

    public async Task<IReadOnlyList<int>> AllIdsAsync(CancellationToken ct)
        => await db.Set<Product>().AsNoTracking().OrderBy(p => p.ProductId).Select(p => p.ProductId).ToListAsync(ct);

    public async Task<List<IReadOnlyDictionary<string, object?>>> BuildAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var rows = await (from p in db.Set<Product>().AsNoTracking()
                          where ids.Contains(p.ProductId)
                          join c in db.Clients.AsNoTracking() on p.ClientId equals c.ClientId into owners
                          from owner in owners.DefaultIfEmpty()
                          join u in db.LookupCodes.AsNoTracking() on p.BaseUomLookupId equals u.LookupCodeId
                          join t in db.LookupCodes.AsNoTracking() on p.TrackingTypeLookupId equals t.LookupCodeId
                          join pu in db.LookupCodes.AsNoTracking() on p.PackUomLookupId equals (int?)pu.LookupCodeId into packs
                          from pack in packs.DefaultIfEmpty()
                          orderby p.ProductId
                          select new
                          {
                              p.Sku, p.Name, OwnerCode = owner != null ? owner.Code : null, BaseUom = u.InternalCode, Tracking = t.InternalCode, p.WeightKg, p.VolumeM3, p.Barcode, p.PurchaseCost, p.SalePrice,
                              p.MinQty, p.MinPickQty, p.MaxPickQty, p.Brand, p.Model, PackUom = pack != null ? pack.InternalCode : null, p.PackQty, p.RotationMethod,
                          }).ToListAsync(ct);
        return rows.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            [ProductImportFields.Sku] = r.Sku, [ProductImportFields.Name] = r.Name, [ProductImportFields.OwnerClientCode] = r.OwnerCode, [ProductImportFields.BaseUom] = r.BaseUom,
            [ProductImportFields.TrackingType] = r.Tracking, [ProductImportFields.WeightKg] = r.WeightKg, [ProductImportFields.VolumeM3] = r.VolumeM3, [ProductImportFields.Barcode] = r.Barcode,
            [ProductImportFields.PurchaseCost] = r.PurchaseCost, [ProductImportFields.SalePrice] = r.SalePrice, [ProductImportFields.MinQty] = r.MinQty, [ProductImportFields.MinPickQty] = r.MinPickQty,
            [ProductImportFields.MaxPickQty] = r.MaxPickQty, [ProductImportFields.Brand] = r.Brand, [ProductImportFields.Model] = r.Model, [ProductImportFields.PackUom] = r.PackUom,
            [ProductImportFields.PackQty] = r.PackQty, [ProductImportFields.RotationMethod] = r.RotationMethod,
        }).ToList();
    }
}
