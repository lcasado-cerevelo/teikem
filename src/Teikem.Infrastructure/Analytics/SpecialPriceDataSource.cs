using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 247 (Lote C, DECISIÓN 18) — fuente SPECIAL_PRICE, «Precios especiales»: una fila por renglón ACTIVO de una lista DEDICADA activa (el precio especial de un
/// cliente o de una cadena) de la compañía, con su estado de HOY (sin rango de fecha: DateField null): vigencia, estado, días para vencer, precio de la lista general
/// vigente y si el especial sale más caro (se cobra el más bajo). La deja lista para el aviso «por vencer» del Lote D. Sin campos personalizados (EntityTypeCode null).
/// AsNoTracking bajo el filtro global de tenant, tope ClientDataSourceHelpers.MaxRows y respeto de q.Ids (PriceListItemId). Lectura: sales.view
/// (DataSourceExtraReadPermission) y el módulo Órdenes de venta encendido.
/// OwnerKind es la etiqueta en el idioma de la sesión («Cliente»/«Cadena», «Client»/«Chain») y OwnerKindCode el código (CLIENT/CHAIN), como Kind/KindCode de
/// SALES_REVENUE; OwnerIsActive = el dueño (cliente o cadena) está activo (los renglones que salen siempre lo están). Corregido después del cierre (DECISIÓN 33).
/// </summary>
public sealed class SpecialPriceDataSource(TeikemDbContext db, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public string Key => SpecialPriceCodes.DataSourceKey;
    public string LabelEs => "Precios especiales";
    public string LabelEn => "Special prices";
    public string? EntityTypeCode => null;
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("OwnerKind", "De (cliente o cadena)", "Owner kind (client or chain)", DataFieldType.Text),
        new DataField("OwnerKindCode", "De (código)", "Owner kind (code)", DataFieldType.Text),
        new DataField("OwnerName", "Cliente o cadena", "Client or chain", DataFieldType.Text),
        new DataField("ChainName", "Cadena", "Chain", DataFieldType.Text),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("ProductName", "Producto", "Product", DataFieldType.Text),
        new DataField("Brand", "Marca", "Brand", DataFieldType.Text),
        new DataField("Price", "Precio especial", "Special price", DataFieldType.Number, IsMoney: true),
        new DataField("ValidFrom", "Desde", "From", DataFieldType.Date),
        new DataField("ValidTo", "Hasta", "To", DataFieldType.Date),
        new DataField("State", "Estado", "State", DataFieldType.Text),
        new DataField("DaysToExpire", "Días para vencer", "Days to expire", DataFieldType.Number),
        new DataField("GeneralListPrice", "Precio en la lista general", "General list price", DataFieldType.Number, IsMoney: true),
        new DataField("AboveGeneral", "Más caro que la lista general", "Above the general list", DataFieldType.Bool),
        new DataField("PriceListName", "Lista", "Price list", DataFieldType.Text),
        new DataField("OwnerIsActive", "Dueño activo", "Owner active", DataFieldType.Bool),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    /// <summary>Etiqueta de quién es el precio especial (CLIENT/CHAIN) en el idioma de la sesión; un código desconocido sale tal cual.</summary>
    public static string OwnerKindLabel(string ownerKind, string? lang)
    {
        var en = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
        return ownerKind switch
        {
            SpecialPriceOwnerKinds.Client => en ? "Client" : "Cliente",
            SpecialPriceOwnerKinds.Chain => en ? "Chain" : "Cadena",
            _ => ownerKind,
        };
    }

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var today = ClientDataSourceHelpers.Today(clock);
        var query =
            from i in db.Set<PriceListItem>().AsNoTracking()
            join l in db.Set<PriceList>().AsNoTracking() on i.PriceListId equals l.PriceListId
            join p in db.Products.AsNoTracking() on i.ProductId equals p.ProductId
            where i.IsActive && l.IsActive && (l.SpecialClientId != null || l.SpecialChainId != null)
            select new
            {
                i.PriceListItemId, i.Price, l.Name, l.ValidFrom, l.ValidTo, l.IsPublished, l.SpecialClientId, l.SpecialChainId,
                p.ProductId, p.Sku, ProductName = p.Name, p.Brand,
            };
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(x => wanted.Contains(x.PriceListItemId));
        }
        var items = await query.OrderBy(x => x.PriceListItemId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (items.Count == 0) return new List<DataRow>();

        // dueños: clientes (con su cadena actual) y cadenas
        var clientIds = items.Where(x => x.SpecialClientId != null).Select(x => x.SpecialClientId!.Value).Distinct().ToList();
        var clients = clientIds.Count == 0
            ? new Dictionary<int, (string Name, bool IsActive, int? ChainId)>()
            : (await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
                    .Select(c => new { c.ClientId, c.Name, c.IsActive, c.ChainId }).ToListAsync(ct))
                .ToDictionary(c => c.ClientId, c => (c.Name, c.IsActive, c.ChainId));
        var chainIds = items.Where(x => x.SpecialChainId != null).Select(x => x.SpecialChainId!.Value)
            .Concat(clients.Values.Where(c => c.ChainId != null).Select(c => c.ChainId!.Value)).Distinct().ToList();
        var chains = await ClientChainQueries.ChainsByIdAsync(db, chainIds, ct);

        // la lista general vigente hoy, para comparar
        var productIds = items.Select(x => x.ProductId).Distinct().ToList();
        var generalLists = await PriceListQueries.InEffectAsync(db, null, today, productIds, ct);
        var lang = tenant.Lang;

        var rows = new List<DataRow>(items.Count);
        foreach (var x in items)
        {
            string ownerKind, ownerName;
            bool ownerActive;
            string? chainName;
            if (x.SpecialClientId is int clientId)
            {
                var c = clients.GetValueOrDefault(clientId);
                (ownerKind, ownerName, ownerActive) = (SpecialPriceOwnerKinds.Client, c.Name ?? string.Empty, c.IsActive);
                chainName = c.ChainId is int cc && chains.TryGetValue(cc, out var cch) ? cch.Name : null;
            }
            else
            {
                var ch = chains.GetValueOrDefault(x.SpecialChainId!.Value);
                (ownerKind, ownerName, ownerActive) = (SpecialPriceOwnerKinds.Chain, ch?.Name ?? string.Empty, ch?.IsActive ?? false);
                chainName = ch?.Name;
            }
            var general = PriceListRules.Resolve(x.ProductId, null, today, generalLists, null);
            decimal? generalPrice = general.Source == PriceSources.GeneralList ? general.Price : null;
            rows.Add(new DataRow
            {
                ["Id"] = x.PriceListItemId,
                ["OwnerKind"] = OwnerKindLabel(ownerKind, lang),
                ["OwnerKindCode"] = ownerKind,
                ["OwnerName"] = ownerName,
                ["ChainName"] = chainName,
                ["Sku"] = x.Sku,
                ["ProductName"] = x.ProductName,
                ["Brand"] = x.Brand,
                ["Price"] = x.Price,
                ["ValidFrom"] = x.ValidFrom,
                ["ValidTo"] = x.ValidTo,
                ["State"] = PriceListRules.StateOf(x.IsPublished, x.ValidFrom, x.ValidTo, today),
                ["DaysToExpire"] = SpecialPriceRules.DaysToExpire(x.ValidTo, today),
                ["GeneralListPrice"] = generalPrice,
                ["AboveGeneral"] = generalPrice is decimal g && x.Price > g,
                ["PriceListName"] = x.Name,
                ["OwnerIsActive"] = ownerActive,
            });
        }
        return rows;
    }
}
