using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 245 (Lote B del plan) — fuente CLIENT_CHAIN, «Cadenas de clientes»: una fila por cadena de la compañía (activas e inactivas) con su estado ACTUAL (sin rango de
/// fecha): cuántos clientes activos e inactivos tiene. Sin campos personalizados (EntityTypeCode null). Destino de la relación «Chain» de la fuente CLIENT (y, a través de
/// ella, de las fuentes con relación Client). AsNoTracking bajo el filtro global de tenant, tope ClientDataSourceHelpers.MaxRows y respeto de q.Ids. Lectura: clients.read
/// (DataSourceExtraReadPermission) y el módulo Órdenes de venta encendido.
/// </summary>
public sealed class ClientChainDataSource(TeikemDbContext db) : IDataSource
{
    public string Key => ClientChainCodes.DataSourceKey;
    public string LabelEs => "Cadenas de clientes";
    public string LabelEn => "Client chains";
    public string? EntityTypeCode => null;
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Name", "Cadena", "Chain", DataFieldType.Text),
        new DataField("Notes", "Notas", "Notes", DataFieldType.Text),
        new DataField("IsActive", "Activa", "Active", DataFieldType.Bool),
        new DataField("ClientCount", "Clientes activos", "Active clients", DataFieldType.Number),
        new DataField("InactiveClientCount", "Clientes inactivos", "Inactive clients", DataFieldType.Number),
        new DataField("CreatedAtUtc", "Creada", "Created", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<ClientChain>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(c => wanted.Contains(c.ClientChainId));
        }
        var chains = await query.OrderBy(c => c.Name).ThenBy(c => c.ClientChainId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (chains.Count == 0) return new List<DataRow>();

        var ids = chains.Select(c => c.ClientChainId).ToList();
        var counts = (await db.Clients.AsNoTracking().Where(c => c.ChainId != null && ids.Contains(c.ChainId.Value))
                .GroupBy(c => new { ChainId = c.ChainId!.Value, c.IsActive })
                .Select(g => new { g.Key.ChainId, g.Key.IsActive, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => (x.ChainId, x.IsActive), x => x.Count);

        var rows = new List<DataRow>(chains.Count);
        foreach (var c in chains)
        {
            rows.Add(new DataRow
            {
                ["Id"] = c.ClientChainId,
                ["PublicId"] = c.PublicId.ToString(),
                ["Name"] = c.Name,
                ["Notes"] = c.Notes,
                ["IsActive"] = c.IsActive,
                ["ClientCount"] = counts.GetValueOrDefault((c.ClientChainId, true)),
                ["InactiveClientCount"] = counts.GetValueOrDefault((c.ClientChainId, false)),
                ["CreatedAtUtc"] = c.CreatedAtUtc,
            });
        }
        return rows;
    }
}
