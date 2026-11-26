using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 247 (Lote C) — «Precios especiales» en la ficha del cliente y de la cadena. Por dentro, cada precio especial es un renglón de una lista DEDICADA del dueño
/// (PriceList.SpecialClientId o SpecialChainId), una lista por dueño y por vigencia («desde» / «hasta»), publicada, que no aplica a todos y lleva su fila
/// PriceListClient o PriceListChain: el motor de precios la ve como cualquier lista (DECISIONES 1 y 2). El precio especial es un precio ANTES del IVU: las
/// exenciones del cliente se aplican después, sin cambiar el cálculo (DECISIÓN 20).
/// <list type="bullet">
/// <item>Ver: sales.view o sales.pricelist. Escribir: sales.pricelist. El dueño de otra compañía da 404; para escribir, el dueño inactivo da 422.</item>
/// <item>Alta: busca la dedicada ACTIVA del dueño con esas mismas fechas (o la crea) y agrega el renglón o le cambia el precio; el mismo producto para el mismo
/// dueño en fechas que se cruzan da 409 (Overlap); dos altas a la vez de la misma dedicada, 409 (Concurrency, por el índice único filtrado).</item>
/// <item>Cambio: solo el precio se cambia ahí; con fechas, el renglón pasa a la dedicada de esas fechas. Quitar: en lógico. La dedicada que se queda sin renglones
/// activos se desactiva sola (DECISIÓN 17).</item>
/// </list>
/// </summary>
public sealed partial class PriceListService
{
    /// <summary>El dueño de los precios especiales: un cliente o una cadena de la compañía.</summary>
    private sealed record SpecialOwner(string Kind, int Id, Guid PublicId, string Name, bool IsActive);

    // ---------------------------------------------------------------- consulta

    /// <summary>«Precios especiales» del cliente: los suyos, los de su cadena activa (solo lectura), las listas normales que lo alcanzan y lo que se cobra hoy.</summary>
    public async Task<SpecialPricesDto> GetClientSpecialPricesAsync(Guid clientPublicId, bool includeExpired, CancellationToken ct)
        => await SpecialPricesDtoAsync(await OwnerAsync(SpecialPriceOwnerKinds.Client, clientPublicId, ct), includeExpired, ct);

    /// <summary>«Precios especiales» de la cadena: los suyos, las listas normales que la incluyen y lo que se cobra hoy a un miembro sin precio propio.</summary>
    public async Task<SpecialPricesDto> GetChainSpecialPricesAsync(Guid chainPublicId, bool includeExpired, CancellationToken ct)
        => await SpecialPricesDtoAsync(await OwnerAsync(SpecialPriceOwnerKinds.Chain, chainPublicId, ct), includeExpired, ct);

    // ---------------------------------------------------------------- escritura

    public Task<SpecialPricesDto> AddClientSpecialPriceAsync(Guid clientPublicId, SpecialPriceRequest? req, CancellationToken ct, bool includeExpired = false)
        => AddSpecialPriceAsync(SpecialPriceOwnerKinds.Client, clientPublicId, req, includeExpired, ct);

    public Task<SpecialPricesDto> AddChainSpecialPriceAsync(Guid chainPublicId, SpecialPriceRequest? req, CancellationToken ct, bool includeExpired = false)
        => AddSpecialPriceAsync(SpecialPriceOwnerKinds.Chain, chainPublicId, req, includeExpired, ct);

    public Task<SpecialPricesDto> UpdateClientSpecialPriceAsync(Guid clientPublicId, int itemId, SpecialPricePatchRequest? req, CancellationToken ct, bool includeExpired = false)
        => UpdateSpecialPriceAsync(SpecialPriceOwnerKinds.Client, clientPublicId, itemId, req, includeExpired, ct);

    public Task<SpecialPricesDto> UpdateChainSpecialPriceAsync(Guid chainPublicId, int itemId, SpecialPricePatchRequest? req, CancellationToken ct, bool includeExpired = false)
        => UpdateSpecialPriceAsync(SpecialPriceOwnerKinds.Chain, chainPublicId, itemId, req, includeExpired, ct);

    public Task<SpecialPricesDto> RemoveClientSpecialPriceAsync(Guid clientPublicId, int itemId, CancellationToken ct, bool includeExpired = false)
        => RemoveSpecialPriceAsync(SpecialPriceOwnerKinds.Client, clientPublicId, itemId, includeExpired, ct);

    public Task<SpecialPricesDto> RemoveChainSpecialPriceAsync(Guid chainPublicId, int itemId, CancellationToken ct, bool includeExpired = false)
        => RemoveSpecialPriceAsync(SpecialPriceOwnerKinds.Chain, chainPublicId, itemId, includeExpired, ct);

    /// <summary>
    /// Alta (o cambio de precio, si el producto ya está en la dedicada de esas fechas). 400: sin producto, sin precio o precio inválido, sin «desde», «hasta» antes de
    /// «desde» o ya pasado; 404 dueño o producto; 422 dueño inactivo, producto ajeno, inactivo o kit de oferta; 409 cruce o alta simultánea.
    /// </summary>
    private async Task<SpecialPricesDto> AddSpecialPriceAsync(string kind, Guid ownerPublicId, SpecialPriceRequest? req, bool includeExpired, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (SpecialPriceRules.RowError(req.ProductPublicId, req.Price, req.ValidFrom, req.ValidTo, clock.Today, isCreate: true) is { } error)
            throw new ValidationException(error.Field, error.Message);
        if (SalesRules.PriceError(req.Price) is { } priceError) throw new ValidationException("price", priceError);
        var owner = await OwnerAsync(kind, ownerPublicId, ct);
        EnsureOwnerWritable(owner);
        var product = await SpecialProductAsync(req.ProductPublicId!.Value, ct);
        var from = req.ValidFrom!.Value;
        var to = req.ValidTo;
        var price = req.Price!.Value;

        await WriteSpecialAsync(async c =>
        {
            var list = await DedicatedListAsync(owner, from, to, c);
            PutItem(list, product.ProductId, price, null);
            await EnsureNoConflictsAsync(list, c);
            await db.SaveGuardedAsync(PriceListRules.Concurrency, c);
        }, ct);
        return await SpecialPricesDtoAsync(owner, includeExpired, ct);
    }

    /// <summary>
    /// Cambio (lo que no viene no cambia): solo el precio se cambia en el renglón; con fechas, el renglón pasa a la dedicada de esas fechas (y la que se queda sin
    /// renglones se desactiva). Al editar se permite un «hasta» ya pasado, para cerrar un precio que corre (DECISIÓN 25). 404 si el renglón no es un precio especial
    /// vigente de ESE dueño.
    /// </summary>
    private async Task<SpecialPricesDto> UpdateSpecialPriceAsync(string kind, Guid ownerPublicId, int itemId, SpecialPricePatchRequest? req, bool includeExpired,
        CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Price is not null && SalesRules.PriceError(req.Price) is { } priceError) throw new ValidationException("price", priceError);
        var owner = await OwnerAsync(kind, ownerPublicId, ct);
        EnsureOwnerWritable(owner);

        await WriteSpecialAsync(async c =>
        {
            var (list, item) = await SpecialItemAsync(owner, itemId, c);
            var from = req.ValidFrom ?? list.ValidFrom;
            var to = req.ClearValidTo == true ? null : req.ValidTo ?? list.ValidTo;
            if (PriceListRules.DateRangeError(from, to) is { } range) throw new ValidationException("validTo", range);
            var price = req.Price ?? item.Price;

            if (from == list.ValidFrom && to == list.ValidTo)
            {
                item.Price = price;
                await EnsureNoConflictsAsync(list, c);
            }
            else
            {
                // pasa a la dedicada de las fechas nuevas: el renglón de aquí se quita (queda en el historial) y se pone allá con su precio y su foto
                item.IsActive = false;
                if (!list.Items.Any(i => i.IsActive)) list.IsActive = false;
                var target = await DedicatedListAsync(owner, from, to, c);
                PutItem(target, item.ProductId, price, item.ImageFileId);
                await EnsureNoConflictsAsync(target, c);
            }
            await db.SaveGuardedAsync(PriceListRules.Concurrency, c);
        }, ct);
        return await SpecialPricesDtoAsync(owner, includeExpired, ct);
    }

    /// <summary>Quita el precio especial (en lógico); la dedicada que se queda sin renglones activos se desactiva. 404 si no es un precio especial de ESE dueño.</summary>
    private async Task<SpecialPricesDto> RemoveSpecialPriceAsync(string kind, Guid ownerPublicId, int itemId, bool includeExpired, CancellationToken ct)
    {
        var owner = await OwnerAsync(kind, ownerPublicId, ct);
        EnsureOwnerWritable(owner);
        await WriteSpecialAsync(async c =>
        {
            var (list, item) = await SpecialItemAsync(owner, itemId, c);
            item.IsActive = false;
            if (!list.Items.Any(i => i.IsActive)) list.IsActive = false;
            await db.SaveGuardedAsync(PriceListRules.Concurrency, c);
        }, ct);
        return await SpecialPricesDtoAsync(owner, includeExpired, ct);
    }

    // ---------------------------------------------------------------- piezas

    /// <summary>El trabajo de escritura en una transacción; si falla, el cambio rechazado no se queda pendiente en el contexto.</summary>
    private async Task WriteSpecialAsync(Func<CancellationToken, Task> work, CancellationToken ct)
    {
        try
        {
            await db.RunInTransactionAsync(work, ct);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>El cliente o la cadena por su PublicId (bajo el filtro de compañía): 404 «Cliente no encontrado.» / «Cadena no encontrada.».</summary>
    private async Task<SpecialOwner> OwnerAsync(string kind, Guid publicId, CancellationToken ct)
    {
        if (kind == SpecialPriceOwnerKinds.Client)
        {
            var c = await db.Clients.AsNoTracking().Where(x => x.PublicId == publicId).Select(x => new { x.ClientId, x.PublicId, x.Name, x.IsActive }).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException("Cliente");
            return new SpecialOwner(kind, c.ClientId, c.PublicId, c.Name, c.IsActive);
        }
        var ch = await db.Set<ClientChain>().AsNoTracking().Where(x => x.PublicId == publicId).Select(x => new { x.ClientChainId, x.PublicId, x.Name, x.IsActive }).FirstOrDefaultAsync(ct)
                 ?? throw ChainNotFound();
        return new SpecialOwner(kind, ch.ClientChainId, ch.PublicId, ch.Name, ch.IsActive);
    }

    /// <summary>Para escribir, el dueño debe estar activo: 422 «El cliente está inactivo…» / «La cadena está desactivada…».</summary>
    private static void EnsureOwnerWritable(SpecialOwner owner)
    {
        if (owner.IsActive) return;
        throw new StatusRuleException(owner.Kind == SpecialPriceOwnerKinds.Client ? SalesRules.ClientInactive : ClientChainRules.ChainInactive);
    }

    /// <summary>El producto del precio especial, con las reglas de los renglones de lista: 404; 422 ajeno, inactivo o kit de oferta.</summary>
    private async Task<Product> SpecialProductAsync(Guid productPublicId, CancellationToken ct)
    {
        var p = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == productPublicId, ct) ?? throw new NotFoundException("Producto");
        if (p.ClientId is not null) throw new StatusRuleException(PriceListRules.ItemProductNotOwn);
        if (!p.IsActive) throw new StatusRuleException(PriceListRules.ItemProductInactive(p.Sku));
        if (p.IsOfferKit) throw new StatusRuleException(OfferKitRules.NotInPriceLists(p.Sku));
        return p;
    }

    /// <summary>Solo las listas dedicadas del dueño (cliente o cadena).</summary>
    private IQueryable<PriceList> DedicatedOf(SpecialOwner owner)
        => owner.Kind == SpecialPriceOwnerKinds.Client
            ? db.Set<PriceList>().Where(l => l.SpecialClientId == owner.Id)
            : db.Set<PriceList>().Where(l => l.SpecialChainId == owner.Id);

    /// <summary>
    /// La dedicada ACTIVA del dueño con esas fechas, rastreada (con renglones, clientes y cadenas); si no hay, la crea publicada, con su nombre y su fila de cliente o
    /// de cadena (se guarda con el trabajo).
    /// </summary>
    private async Task<PriceList> DedicatedListAsync(SpecialOwner owner, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var query = DedicatedOf(owner).AsTracking().Where(l => l.IsActive && l.ValidFrom == from);
        query = to is DateOnly t ? query.Where(l => l.ValidTo == t) : query.Where(l => l.ValidTo == null);
        var list = await query.Include(l => l.Items).Include(l => l.Clients).Include(l => l.Chains).FirstOrDefaultAsync(ct);
        if (list is not null) return list;

        var now = DateTime.UtcNow;
        list = new PriceList
        {
            PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), Name = SpecialPriceRules.DedicatedName(owner.Name, from, to), ValidFrom = from, ValidTo = to,
            AppliesToAll = false, IsPublished = true, IsActive = true, CreatedAtUtc = now, CreatedBy = tenant.UserId,
        };
        if (owner.Kind == SpecialPriceOwnerKinds.Client)
        {
            list.SpecialClientId = owner.Id;
            list.Clients.Add(new PriceListClient { TenantId = list.TenantId, ClientId = owner.Id, IsActive = true, CreatedAtUtc = now });
        }
        else
        {
            list.SpecialChainId = owner.Id;
            list.Chains.Add(new PriceListChain { TenantId = list.TenantId, ChainId = owner.Id, IsActive = true, CreatedAtUtc = now });
        }
        db.Set<PriceList>().Add(list);
        return list;
    }

    /// <summary>Pone el producto en la dedicada: si ya está (activo), le cambia el precio; si no, agrega el renglón. Respeta el tope de renglones por lista.</summary>
    private static void PutItem(PriceList list, int productId, decimal price, int? imageFileId)
    {
        var existing = list.Items.FirstOrDefault(i => i.IsActive && i.ProductId == productId);
        if (existing is not null)
        {
            existing.Price = price;
            return;
        }
        if (list.Items.Count(i => i.IsActive) >= PriceListCodes.MaxItems) throw new ValidationException("productPublicId", PriceListRules.TooManyItems);
        list.Items.Add(new PriceListItem { TenantId = list.TenantId, ProductId = productId, Price = price, ImageFileId = imageFileId, IsActive = true, CreatedAtUtc = DateTime.UtcNow });
    }

    /// <summary>El renglón activo de una dedicada ACTIVA de ESE dueño, con su lista rastreada; si no, 404 «Precio especial no encontrado.».</summary>
    private async Task<(PriceList List, PriceListItem Item)> SpecialItemAsync(SpecialOwner owner, int itemId, CancellationToken ct)
    {
        var listId = await db.Set<PriceListItem>().AsNoTracking().Where(i => i.PriceListItemId == itemId && i.IsActive).Select(i => (int?)i.PriceListId).FirstOrDefaultAsync(ct);
        if (listId is not int id) throw new NotFoundException(SpecialPriceRules.RowNotFound);
        var list = await DedicatedOf(owner).AsTracking().Where(l => l.PriceListId == id && l.IsActive)
            .Include(l => l.Items).Include(l => l.Clients).Include(l => l.Chains).FirstOrDefaultAsync(ct) ?? throw new NotFoundException(SpecialPriceRules.RowNotFound);
        var item = list.Items.FirstOrDefault(i => i.PriceListItemId == itemId && i.IsActive) ?? throw new NotFoundException(SpecialPriceRules.RowNotFound);
        return (list, item);
    }

    /// <summary>Un renglón de una dedicada, leído para mostrar.</summary>
    private sealed record SpecialRowData(int ItemId, Guid ListPublicId, int ProductId, decimal Price, DateOnly ValidFrom, DateOnly? ValidTo, bool IsPublished);

    /// <summary>Los renglones activos de las dedicadas activas del dueño (cliente o cadena).</summary>
    private async Task<List<SpecialRowData>> SpecialRowsAsync(SpecialOwner owner, CancellationToken ct)
        => await (from l in DedicatedOf(owner).AsNoTracking()
                  join i in db.Set<PriceListItem>().AsNoTracking() on l.PriceListId equals i.PriceListId
                  where l.IsActive && i.IsActive
                  select new SpecialRowData(i.PriceListItemId, l.PublicId, i.ProductId, i.Price, l.ValidFrom, l.ValidTo, l.IsPublished)).ToListAsync(ct);

    /// <summary>
    /// La respuesta de «Precios especiales» (DECISIÓN 16):
    /// - Rows: los del dueño, con estado, días para vencer, precio de la lista general y si sale más caro; los vencidos solo con includeExpired; por marca, SKU y
    ///   «desde».
    /// - ChainRows (solo en la ficha del cliente con cadena ACTIVA): los vigentes y programados de su cadena, en solo lectura.
    /// - ClientLists: las listas NORMALES publicadas y no vencidas que alcanzan al cliente (por él o por su cadena activa) o a la cadena.
    /// - ChargedPrice / ChargedSource / ChargedSourceName: lo que se cobra HOY por cada producto (en la ficha de la cadena, a un miembro sin precio propio).
    /// - Warnings: los propios no vencidos más caros que la lista general.
    /// </summary>
    private async Task<SpecialPricesDto> SpecialPricesDtoAsync(SpecialOwner owner, bool includeExpired, CancellationToken ct)
    {
        var today = clock.Today;
        var own = (await SpecialRowsAsync(owner, ct))
            .Where(r => includeExpired || PriceListRules.StateOf(r.IsPublished, r.ValidFrom, r.ValidTo, today) != PriceListStates.Expired).ToList();

        ChainRef? chain = null;
        var chainRows = new List<SpecialRowData>();
        if (owner.Kind == SpecialPriceOwnerKinds.Client && await ClientChainQueries.ActiveChainOfClientAsync(db, owner.Id, ct) is int chainId)
        {
            chain = (await ClientChainQueries.ChainsByIdAsync(db, new[] { chainId }, ct)).GetValueOrDefault(chainId);
            if (chain is not null)
            {
                var chainOwner = new SpecialOwner(SpecialPriceOwnerKinds.Chain, chain.Id, chain.PublicId, chain.Name, chain.IsActive);
                chainRows = (await SpecialRowsAsync(chainOwner, ct)).Where(r => PriceListRules.StateOf(r.IsPublished, r.ValidFrom, r.ValidTo, today)
                    is PriceListStates.Active or PriceListStates.Scheduled).ToList();
            }
        }

        // productos, foto de kit y lista general (a la fecha de referencia de cada renglón)
        var productIds = own.Concat(chainRows).Select(r => r.ProductId).Distinct().ToList();
        var products = productIds.Count == 0 ? new Dictionary<int, Product>()
            : await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var general = await GeneralPricesAsync(own.Concat(chainRows).Select(r => (ReferenceDate(r.ValidFrom), r.ProductId)), null, ct);

        // lo que se cobra hoy: el cliente con su cadena activa; en la ficha de la cadena, un miembro sin precio propio (sin cliente, con la cadena si está activa)
        int? chargedClient = owner.Kind == SpecialPriceOwnerKinds.Client ? owner.Id : null;
        int? chargedChain = owner.Kind == SpecialPriceOwnerKinds.Client ? chain?.Id : owner.IsActive ? owner.Id : null;
        var charged = await PriceListQueries.ResolveAsync(db, products.Values, chargedClient, chargedChain, today, ct);
        var listIds = charged.Values.Where(r => r.PriceListId is not null && r.OfferId is null).Select(r => r.PriceListId!.Value).Distinct().ToList();
        var offerIds = charged.Values.Where(r => r.OfferId is not null).Select(r => r.OfferId!.Value).Distinct().ToList();
        var listNames = listIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Set<PriceList>().AsNoTracking().Where(l => listIds.Contains(l.PriceListId)).ToDictionaryAsync(l => l.PriceListId, l => l.Name, ct);
        var offerNames = offerIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Set<Offer>().AsNoTracking().Where(o => offerIds.Contains(o.OfferId)).ToDictionaryAsync(o => o.OfferId, o => o.Name, ct);

        SpecialPriceRowDto ToRow(SpecialRowData r)
        {
            var p = products[r.ProductId];
            decimal? g = general.TryGetValue((ReferenceDate(r.ValidFrom), r.ProductId), out var gp) ? gp : null;
            var ch = charged.GetValueOrDefault(r.ProductId);
            string? chName = ch is null ? null
                : ch.OfferId is int oid ? offerNames.GetValueOrDefault(oid)
                : ch.PriceListId is int lid ? listNames.GetValueOrDefault(lid)
                : null;
            return new SpecialPriceRowDto(r.ItemId, r.ListPublicId, p.PublicId, p.Sku, p.Name, p.Brand, kitTypeId is not null && p.ProductTypeLookupId == kitTypeId, r.Price,
                r.ValidFrom, r.ValidTo, PriceListRules.StateOf(r.IsPublished, r.ValidFrom, r.ValidTo, today), SpecialPriceRules.DaysToExpire(r.ValidTo, today), g,
                g is decimal gv && r.Price > gv, p.SalePrice, p.IsActive, ch?.Price, ch?.Source, chName);
        }

        static List<SpecialPriceRowDto> Ordered(IEnumerable<SpecialPriceRowDto> rows)
            => rows.OrderBy(r => r.Brand ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Sku, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.ValidFrom).ThenBy(r => r.ItemId).ToList();

        var rowDtos = Ordered(own.Where(r => products.ContainsKey(r.ProductId)).Select(ToRow));
        var chainDtos = Ordered(chainRows.Where(r => products.ContainsKey(r.ProductId)).Select(ToRow));
        var warnings = rowDtos.Where(r => r.AboveGeneral && r.State != PriceListStates.Expired)
            .Select(r => AboveGeneralWarning(r.ProductPublicId, r.Sku, r.Price, r.GeneralListPrice!.Value)).ToList();
        var lists = await ReachingListsAsync(owner, chain?.Id, today, ct);

        return new SpecialPricesDto(owner.Kind, owner.PublicId, owner.Name, owner.IsActive, chain?.PublicId, chain?.Name, chainDtos.Count, rowDtos, chainDtos, lists, warnings);
    }

    /// <summary>
    /// Las listas NORMALES (no dedicadas) activas, publicadas y no vencidas que no aplican a todos y alcanzan al dueño: por el cliente (MatchedBy CLIENT) o por su
    /// cadena activa (CHAIN); en la ficha de la cadena, las que la incluyen (CHAIN). Por nombre.
    /// </summary>
    private async Task<IReadOnlyList<SpecialPriceListRefDto>> ReachingListsAsync(SpecialOwner owner, int? clientChainId, DateOnly today, CancellationToken ct)
    {
        var isClient = owner.Kind == SpecialPriceOwnerKinds.Client;
        var clientId = isClient ? owner.Id : 0;
        var chainId = isClient ? clientChainId ?? 0 : owner.Id;
        var hasChain = !isClient || clientChainId is not null;
        var rows = await db.Set<PriceList>().AsNoTracking()
            .Where(l => l.IsActive && l.IsPublished && !l.AppliesToAll && l.SpecialClientId == null && l.SpecialChainId == null && (l.ValidTo == null || l.ValidTo >= today))
            .Select(l => new
            {
                l.PublicId, l.Name, l.ValidFrom, l.ValidTo, l.IsPublished,
                ByClient = isClient && db.Set<PriceListClient>().Any(c => c.PriceListId == l.PriceListId && c.IsActive && c.ClientId == clientId),
                ByChain = hasChain && db.Set<PriceListChain>().Any(c => c.PriceListId == l.PriceListId && c.IsActive && c.ChainId == chainId),
                Items = db.Set<PriceListItem>().Count(i => i.PriceListId == l.PriceListId && i.IsActive),
            })
            .Where(l => l.ByClient || l.ByChain)
            .ToListAsync(ct);
        return rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new SpecialPriceListRefDto(r.PublicId, r.Name, PriceListRules.StateOf(r.IsPublished, r.ValidFrom, r.ValidTo, today), r.Items,
                r.ByClient ? SpecialPriceOwnerKinds.Client : SpecialPriceOwnerKinds.Chain)).ToList();
    }
}
