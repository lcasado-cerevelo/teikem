using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 229 — ofertas (cosa aparte de las listas de precios, como la circular de una tienda). Las arma el supervisor de ventas (sales.pricelist); verlas usa sales.view.
/// - Nombre, foto, vigencia (desde obligatorio; sin «hasta» la oferta no vence y la respuesta lo dice: HasEndDate = false), publicada o borrador (en borrador no se
///   aplica), aplica a todos los clientes o a los escogidos.
/// - Renglones: producto o kit → precio de oferta, con foto opcional; o un kit creado PARA la oferta (Product.OfferId, nace no ajustable) que solo se vende mientras
///   la oferta esté publicada y vigente.
/// - Al pedir gana el precio más bajo entre el negociado y la oferta (OfferRules.Resolve). Choques con otras ofertas o con la lista general no son errores: la
///   respuesta los trae en Warnings.
/// - Las órdenes ya hechas no cambian (la línea guarda la copia del precio). Nunca DELETE: la oferta se desactiva y renglones y clientes se quitan en lógico.
/// </summary>
public sealed class OfferService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ILookupCache lookups, StoredFileService files, ProductService products,
    KitService kits)
{
    private TenantContext T => (TenantContext)tenant;

    // ---------------------------------------------------------------- consulta

    /// <summary>Las ofertas (las desactivadas solo con includeInactive), la de vigencia más reciente primero; q = el nombre contiene; state = DRAFT/SCHEDULED/ACTIVE/EXPIRED.</summary>
    public async Task<IReadOnlyList<OfferListItemDto>> ListAsync(string? q, string? state, bool includeInactive, CancellationToken ct)
    {
        var query = db.Set<Offer>().AsNoTracking();
        if (!includeInactive) query = query.Where(o => o.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(o => o.Name.Contains(s));
        }
        var rows = await query.OrderByDescending(o => o.ValidFrom).ThenByDescending(o => o.OfferId)
            .Select(o => new
            {
                Offer = o,
                Items = db.Set<OfferItem>().Count(i => i.OfferId == o.OfferId && i.IsActive),
                Clients = db.Set<OfferClient>().Count(c => c.OfferId == o.OfferId && c.IsActive),
            }).ToListAsync(ct);
        var images = await PriceListQueries.FilePublicIdsAsync(db, rows.Select(r => r.Offer.ImageFileId), ct);
        var today = clock.Today;
        var result = rows.Select(r =>
        {
            var o = r.Offer;
            return new OfferListItemDto(o.PublicId, o.Name, o.ValidFrom, o.ValidTo, o.ValidTo is not null, OfferRules.RangeText(o.ValidFrom, o.ValidTo), o.IsPublished,
                PriceListRules.StateOf(o.IsPublished, o.ValidFrom, o.ValidTo, today), InEffect(o, today), o.AppliesToAll, r.Items, r.Clients,
                o.ImageFileId is int f && images.TryGetValue(f, out var g) ? g : null, o.IsActive, o.CreatedAtUtc, o.UpdatedAtUtc);
        });
        if (!string.IsNullOrWhiteSpace(state))
        {
            var st = state.Trim().ToUpperInvariant();
            result = result.Where(r => r.State == st);
        }
        return result.ToList();
    }

    public async Task<OfferDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var offer = await db.Set<Offer>().AsNoTracking().Include(o => o.Items).Include(o => o.Clients).FirstOrDefaultAsync(o => o.PublicId == publicId, ct) ?? throw NotFound();
        return await ToDtoAsync(offer, ct);
    }

    // ---------------------------------------------------------------- alta y edición

    /// <summary>Crea la oferta en borrador. 400: sin nombre, sin «desde», «hasta» antes de «desde».</summary>
    public async Task<OfferDto> CreateAsync(OfferRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var (name, nameError) = OfferRules.NormalizeName(req.Name);
        if (nameError is not null) throw new ValidationException("name", nameError);
        if (req.ValidFrom is not DateOnly from) throw new ValidationException("validFrom", OfferRules.ValidFromRequired);
        var to = req.ClearValidTo == true ? null : req.ValidTo;
        var dateError = OfferRules.DateRangeError(from, to);
        if (dateError is not null) throw new ValidationException("validTo", dateError);
        var (notes, notesError) = OfferRules.NormalizeNotes(req.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);

        var offer = new Offer
        {
            PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), Name = name!, ValidFrom = from, ValidTo = to, AppliesToAll = req.AppliesToAll ?? true,
            Notes = notes, IsPublished = false, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        db.Set<Offer>().Add(offer);
        await db.SaveGuardedAsync(OfferRules.Concurrency, ct);
        return await GetAsync(offer.PublicId, ct);
    }

    /// <summary>PATCH: lo que no viene no cambia. Extender la fecha «hasta» (o quitarla) vuelve a poner a la venta los kits de la oferta.</summary>
    public Task<OfferDto> UpdateAsync(Guid publicId, OfferRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        string? name = null;
        if (req.Name is not null)
        {
            var (n, e) = OfferRules.NormalizeName(req.Name);
            if (e is not null) throw new ValidationException("name", e);
            name = n;
        }
        var (notes, notesError) = OfferRules.NormalizeNotes(req.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        return MutateAsync(publicId, offer =>
        {
            if (name is not null) offer.Name = name;
            if (req.ValidFrom is DateOnly f) offer.ValidFrom = f;
            if (req.ClearValidTo == true) offer.ValidTo = null;
            else if (req.ValidTo is DateOnly t) offer.ValidTo = t;
            var dateError = OfferRules.DateRangeError(offer.ValidFrom, offer.ValidTo);
            if (dateError is not null) throw new ValidationException("validTo", dateError);
            if (req.Notes is not null) offer.Notes = notes;
            if (req.AppliesToAll is bool all && all != offer.AppliesToAll)
            {
                if (all && offer.Clients.Any(c => c.IsActive)) throw new StatusRuleException(OfferRules.GeneralWithClients);
                if (!all && offer.IsPublished) throw new StatusRuleException(OfferRules.PublishNeedsClients);   // publicada sin clientes no aplicaría a nadie
                offer.AppliesToAll = all;
            }
        }, ct);
    }

    /// <summary>Publica la oferta (borrador → se aplica en su vigencia). Exige renglones y clientes si no es para todos. Los choques salen en Warnings.</summary>
    public Task<OfferDto> PublishAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, offer =>
    {
        if (offer.IsPublished) throw new StatusRuleException(OfferRules.AlreadyPublished);
        if (!offer.Items.Any(i => i.IsActive)) throw new StatusRuleException(OfferRules.PublishNeedsItems);
        if (!offer.AppliesToAll && !offer.Clients.Any(c => c.IsActive)) throw new StatusRuleException(OfferRules.PublishNeedsClients);
        offer.IsPublished = true;
    }, ct);

    /// <summary>La devuelve a borrador: deja de aplicarse a los pedidos nuevos y sus kits dejan de venderse (los pedidos ya hechos no cambian).</summary>
    public Task<OfferDto> UnpublishAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, offer =>
    {
        if (!offer.IsPublished) throw new StatusRuleException(OfferRules.NotPublished);
        offer.IsPublished = false;
    }, ct);

    /// <summary>Baja lógica: deja de aplicarse y sus kits dejan de venderse. Los pedidos ya hechos no cambian.</summary>
    public Task<OfferDto> DeactivateAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, offer =>
    {
        if (!offer.IsActive) throw new StatusRuleException(OfferRules.AlreadyInactive);
        offer.IsActive = false;
    }, ct, allowInactive: true);

    public Task<OfferDto> ReactivateAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, offer =>
    {
        if (offer.IsActive) throw new StatusRuleException(OfferRules.AlreadyActive);
        offer.IsActive = true;
    }, ct, allowInactive: true);

    // ---------------------------------------------------------------- renglones

    /// <summary>Agrega renglones o cambia el precio de los que ya están (por producto). Solo productos propios y activos; un kit de otra oferta no (422).</summary>
    public async Task<OfferDto> UpsertItemsAsync(Guid publicId, OfferItemsRequest? req, CancellationToken ct)
    {
        var items = req?.Items;
        if (items is null || items.Count == 0) throw new ValidationException("items", OfferRules.ItemsRequired);
        if (items.Count > OfferCodes.MaxItems) throw new ValidationException("items", OfferRules.TooManyItems);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].ProductPublicId is null) throw new ValidationException($"items[{i}].productPublicId", OfferRules.ItemProductRequired);
            if (items[i].Price is null) throw new ValidationException($"items[{i}].price", OfferRules.ItemPriceRequired);
            var pe = SalesRules.PriceError(items[i].Price);
            if (pe is not null) throw new ValidationException($"items[{i}].price", pe);
        }
        var ids = items.Select(i => i.ProductPublicId!.Value).Distinct().ToList();
        var found = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
        var offerId = await db.Set<Offer>().AsNoTracking().Where(o => o.PublicId == publicId).Select(o => (int?)o.OfferId).FirstOrDefaultAsync(ct) ?? throw NotFound();
        var otherOfferIds = found.Values.Where(p => p.OfferId is not null && p.OfferId != offerId).Select(p => p.OfferId!.Value).Distinct().ToList();
        var otherNames = otherOfferIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Set<Offer>().AsNoTracking().Where(o => otherOfferIds.Contains(o.OfferId)).ToDictionaryAsync(o => o.OfferId, o => o.Name, ct);
        var seen = new HashSet<Guid>();
        for (var i = 0; i < items.Count; i++)
        {
            var pid = items[i].ProductPublicId!.Value;
            var p = found.GetValueOrDefault(pid) ?? throw new NotFoundException("Producto");
            if (!seen.Add(pid)) throw new ValidationException($"items[{i}].productPublicId", OfferRules.ItemRepeated(p.Sku));
            if (p.ClientId is not null) throw new StatusRuleException(OfferRules.ItemProductNotOwn);
            if (!p.IsActive) throw new StatusRuleException(OfferRules.ItemProductInactive(p.Sku));
            if (p.OfferId is int other && other != offerId) throw new StatusRuleException(OfferRules.ItemKitOfOtherOffer(p.Sku, otherNames.GetValueOrDefault(other) ?? "?"));
        }
        return await MutateAsync(publicId, offer =>
        {
            var active = offer.Items.Where(x => x.IsActive).ToDictionary(x => x.ProductId);
            foreach (var r in items)
            {
                var product = found[r.ProductPublicId!.Value];
                if (active.TryGetValue(product.ProductId, out var existing)) existing.Price = r.Price!.Value;
                else
                {
                    var row = new OfferItem { TenantId = offer.TenantId, ProductId = product.ProductId, Price = r.Price!.Value, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
                    offer.Items.Add(row);
                    active[product.ProductId] = row;
                }
            }
            if (active.Count > OfferCodes.MaxItems) throw new ValidationException("items", OfferRules.TooManyItems);
        }, ct);
    }

    public Task<OfferDto> SetItemPriceAsync(Guid publicId, int itemId, OfferItemPriceRequest? req, CancellationToken ct)
    {
        if (req?.Price is null) throw new ValidationException("price", OfferRules.ItemPriceRequired);
        var pe = SalesRules.PriceError(req.Price);
        if (pe is not null) throw new ValidationException("price", pe);
        return MutateAsync(publicId, offer => RequireItem(offer, itemId).Price = req.Price.Value, ct);
    }

    /// <summary>Quita el renglón (en lógico). Si era un kit de la oferta, el kit sigue atado a ella (y sin precio de oferta se vende a su precio de venta mientras la oferta valga).</summary>
    public Task<OfferDto> RemoveItemAsync(Guid publicId, int itemId, CancellationToken ct)
        => MutateAsync(publicId, offer => RequireItem(offer, itemId).IsActive = false, ct);

    /// <summary>Pone (o cambia) la foto del renglón: JPG, PNG o WEBP, guardada como archivo de la oferta (se ve con sales.view). Sin foto propia se muestra la del producto.</summary>
    public async Task<OfferDto> SetItemImageAsync(Guid publicId, int itemId, string? fileName, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var bytes = ProductImageService.PreparePhoto(content);
        var row = await db.Set<Offer>().AsNoTracking().Where(o => o.PublicId == publicId)
            .Select(o => new { o.OfferId, o.IsActive, Item = db.Set<OfferItem>().Any(i => i.OfferId == o.OfferId && i.OfferItemId == itemId && i.IsActive) })
            .FirstOrDefaultAsync(ct) ?? throw NotFound();
        if (!row.IsActive) throw new StatusRuleException(OfferRules.Inactive);
        if (!row.Item) throw new NotFoundException(OfferRules.ItemNotFound);
        var fileId = await AttachAsync(row.OfferId, fileName, bytes, ct);
        return await MutateAsync(publicId, offer => RequireItem(offer, itemId).ImageFileId = fileId, ct);
    }

    /// <summary>Quita la foto propia del renglón: vuelve a mostrarse la del producto.</summary>
    public Task<OfferDto> ClearItemImageAsync(Guid publicId, int itemId, CancellationToken ct)
        => MutateAsync(publicId, offer => RequireItem(offer, itemId).ImageFileId = null, ct);

    /// <summary>Pone (o cambia) la foto de la oferta: JPG, PNG o WEBP (se ve con sales.view).</summary>
    public async Task<OfferDto> SetImageAsync(Guid publicId, string? fileName, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var bytes = ProductImageService.PreparePhoto(content);
        var row = await db.Set<Offer>().AsNoTracking().Where(o => o.PublicId == publicId).Select(o => new { o.OfferId, o.IsActive }).FirstOrDefaultAsync(ct) ?? throw NotFound();
        if (!row.IsActive) throw new StatusRuleException(OfferRules.Inactive);
        var fileId = await AttachAsync(row.OfferId, fileName, bytes, ct);
        return await MutateAsync(publicId, offer => offer.ImageFileId = fileId, ct);
    }

    public Task<OfferDto> ClearImageAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, offer => offer.ImageFileId = null, ct);

    // ---------------------------------------------------------------- clientes

    /// <summary>Agrega clientes a una oferta que no es para todos (los que ya están se ignoran). Solo clientes activos de la compañía.</summary>
    public async Task<OfferDto> AddClientsAsync(Guid publicId, OfferClientsRequest? req, CancellationToken ct)
    {
        var ids = req?.ClientPublicIds?.Distinct().ToList();
        if (ids is null || ids.Count == 0) throw new ValidationException("clientPublicIds", OfferRules.ClientsRequired);
        if (ids.Count > OfferCodes.MaxClients) throw new ValidationException("clientPublicIds", OfferRules.TooManyClients);
        var clients = await db.Set<Client>().AsNoTracking().Where(c => ids.Contains(c.PublicId)).Select(c => new { c.PublicId, c.ClientId, c.IsActive }).ToListAsync(ct);
        if (clients.Count != ids.Count) throw new NotFoundException("Cliente");
        if (clients.Any(c => !c.IsActive)) throw new StatusRuleException(SalesRules.ClientInactive);
        return await MutateAsync(publicId, offer =>
        {
            if (offer.AppliesToAll) throw new StatusRuleException(OfferRules.ClientsOnGeneral);
            var active = offer.Clients.Where(c => c.IsActive).Select(c => c.ClientId).ToHashSet();
            foreach (var c in clients)
                if (active.Add(c.ClientId))
                    offer.Clients.Add(new OfferClient { TenantId = offer.TenantId, ClientId = c.ClientId, IsActive = true, CreatedAtUtc = DateTime.UtcNow });
            if (active.Count > OfferCodes.MaxClients) throw new ValidationException("clientPublicIds", OfferRules.TooManyClients);
        }, ct);
    }

    /// <summary>Quita un cliente de la oferta (en lógico). Una oferta publicada no se queda sin clientes: se devuelve a borrador antes.</summary>
    public async Task<OfferDto> RemoveClientAsync(Guid publicId, Guid clientPublicId, CancellationToken ct)
    {
        var clientId = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == clientPublicId).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Cliente");
        return await MutateAsync(publicId, offer =>
        {
            var row = offer.Clients.FirstOrDefault(c => c.IsActive && c.ClientId == clientId) ?? throw new NotFoundException(OfferRules.ClientNotInOffer);
            row.IsActive = false;
            if (offer.IsPublished && !offer.Clients.Any(c => c.IsActive)) throw new StatusRuleException(OfferRules.PublishNeedsClients);
        }, ct);
    }

    // ---------------------------------------------------------------- kits de la oferta

    /// <summary>
    /// Crea un kit PARA la oferta: el producto nuevo (SKU, nombre, marca, categoría —por omisión la de la primera pieza— y la categoría de impuesto de la primera
    /// pieza), sus piezas con cantidades (y la marca «no sustituible» si viene) por KitService, NO ajustable, atado a la oferta (Product.OfferId) y su renglón con el
    /// precio de oferta. Todo o nada. 400/404/409 con los mensajes del producto y del kit; 422 si la oferta está desactivada.
    /// </summary>
    public async Task<OfferKitCreatedDto> CreateKitAsync(Guid publicId, OfferKitRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Price is null) throw new ValidationException("price", OfferRules.KitPriceRequired);
        var pe = SalesRules.PriceError(req.Price);
        if (pe is not null) throw new ValidationException("price", pe);
        if (req.Components is null || req.Components.Count == 0) throw new ValidationException("components", KitRules.NeedComponents);
        var offer = await db.Set<Offer>().AsNoTracking().Where(o => o.PublicId == publicId).Select(o => new { o.OfferId, o.IsActive, o.TenantId }).FirstOrDefaultAsync(ct) ?? throw NotFound();
        if (!offer.IsActive) throw new StatusRuleException(OfferRules.Inactive);
        var firstPiece = req.Components[0].ProductPublicId is Guid fp
            ? await db.Set<Product>().AsNoTracking().Where(p => p.PublicId == fp).Select(p => new { p.ProductCategoryId, p.TaxCategoryLookupId }).FirstOrDefaultAsync(ct)
            : null;

        Guid productPublicId = Guid.Empty;
        string sku = string.Empty;
        try
        {
            await db.RunInTransactionAsync(async ct2 =>
            {
                var created = await products.CreateAsync(new ProductCreateRequest(req.Sku, req.Name, CategoryId: req.CategoryId, SalePrice: req.SalePrice ?? req.Price,
                    Brand: req.Brand), ct2);
                productPublicId = created.Product.PublicId;
                sku = created.Product.Sku;
                await kits.SetAsync(productPublicId, new KitSetRequest(true, false, req.Components), ct2);
                var product = await db.Set<Product>().AsTracking().FirstAsync(p => p.PublicId == productPublicId, ct2);
                product.OfferId = offer.OfferId;
                product.KitAdjustable = false;   // el kit de una oferta nace no ajustable
                if (req.CategoryId is null && product.ProductCategoryId is null) product.ProductCategoryId = firstPiece?.ProductCategoryId;
                product.TaxCategoryLookupId ??= firstPiece?.TaxCategoryLookupId;
                db.Set<OfferItem>().Add(new OfferItem
                {
                    TenantId = offer.TenantId, OfferId = offer.OfferId, ProductId = product.ProductId, Price = req.Price.Value, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
                });
                await db.SaveGuardedAsync(OfferRules.Concurrency, ct2);
            }, ct);
        }
        catch
        {
            db.ChangeTracker.Clear();   // lo rechazado no se queda pendiente para el próximo guardado del mismo contexto
            throw;
        }
        return new OfferKitCreatedDto(productPublicId, sku, await GetAsync(publicId, ct));
    }

    /// <summary>Cambia un kit de la oferta: nombre y/o piezas (las piezas reemplazan a las anteriores; «ajustable» no cambia). 422 si el kit no es de esta oferta.</summary>
    public async Task<OfferDto> UpdateKitAsync(Guid publicId, Guid productPublicId, OfferKitUpdateRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var offer = await db.Set<Offer>().AsNoTracking().Where(o => o.PublicId == publicId).Select(o => new { o.OfferId, o.IsActive }).FirstOrDefaultAsync(ct) ?? throw NotFound();
        if (!offer.IsActive) throw new StatusRuleException(OfferRules.Inactive);
        var product = await db.Set<Product>().AsNoTracking().Where(p => p.PublicId == productPublicId).Select(p => new { p.OfferId }).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException(OfferRules.KitNotFound);
        if (product.OfferId != offer.OfferId) throw new StatusRuleException(OfferRules.KitOfAnotherOffer);
        try
        {
            await db.RunInTransactionAsync(async ct2 =>
            {
                if (req.Name is not null) await products.UpdateAsync(productPublicId, new ProductPatchRequest(Name: req.Name), ct2);
                if (req.Components is not null) await kits.SetAsync(productPublicId, new KitSetRequest(true, null, req.Components), ct2);
            }, ct);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- piezas

    private static NotFoundException NotFound() => new(OfferRules.NotFound, feminine: true);

    private static bool InEffect(Offer o, DateOnly today) => o.IsActive && o.IsPublished && PriceListRules.InEffect(o.ValidFrom, o.ValidTo, today);

    private static OfferItem RequireItem(Offer offer, int itemId)
        => offer.Items.FirstOrDefault(i => i.OfferItemId == itemId && i.IsActive) ?? throw new NotFoundException(OfferRules.ItemNotFound);

    private async Task<int> AttachAsync(int offerId, string? fileName, byte[] bytes, CancellationToken ct)
    {
        var file = await files.AttachAsync(OfferCodes.EntityType, offerId, StoredFileCodes.KindDocument, fileName, null, bytes, ct, enforceOwnerWrite: false);
        return await db.Set<StoredFile>().AsNoTracking().Where(f => f.PublicId == file.PublicId).Select(f => f.StoredFileId).FirstAsync(ct);
    }

    /// <summary>Carga la oferta (con renglones y clientes) para cambiarla, aplica el cambio y guarda. Una oferta desactivada no se cambia (salvo reactivarla).</summary>
    private async Task<OfferDto> MutateAsync(Guid publicId, Action<Offer> change, CancellationToken ct, bool allowInactive = false)
    {
        try
        {
            await db.RunInTransactionAsync(async ct2 =>
            {
                var offer = await db.Set<Offer>().AsTracking().Include(o => o.Items).Include(o => o.Clients).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
                if (!offer.IsActive && !allowInactive) throw new StatusRuleException(OfferRules.Inactive);
                change(offer);
                offer.UpdatedAtUtc = DateTime.UtcNow;
                offer.UpdatedBy = tenant.UserId;
                await db.SaveGuardedAsync(OfferRules.Concurrency, ct2);
            }, ct);
        }
        catch
        {
            db.ChangeTracker.Clear();   // el cambio rechazado no se queda pendiente para el próximo guardado del mismo contexto
            throw;
        }
        return await GetAsync(publicId, ct);
    }

    /// <summary>La oferta vista por las reglas puras: solo renglones y clientes activos.</summary>
    internal static OfferView View(Offer o) => new(o.OfferId, o.Name, o.IsPublished, o.IsActive, o.AppliesToAll, o.ValidFrom, o.ValidTo,
        o.Clients.Where(c => c.IsActive).Select(c => c.ClientId).ToList(),
        o.Items.Where(i => i.IsActive).Select(i => new OfferItemView(i.ProductId, i.Price, i.ImageFileId)).ToList());

    private async Task<OfferDto> ToDtoAsync(Offer offer, CancellationToken ct)
    {
        var today = clock.Today;
        var items = offer.Items.Where(i => i.IsActive).ToList();
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var prods = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var productImages = await PriceListQueries.ProductImagesAsync(db, prods.Values, kitTypeId, ct);
        var effective = items.ToDictionary(i => i.OfferItemId, i => ProductImageRules.Resolve(i.ImageFileId, productImages.GetValueOrDefault(i.ProductId), false, null));
        var filePublic = await PriceListQueries.FilePublicIdsAsync(db, items.Select(i => i.ImageFileId).Concat(effective.Values).Append(offer.ImageFileId), ct);

        // el precio de la lista general vigente (en la fecha en que la oferta empieza a valer, o hoy si ya empezó), para comparar y avisar
        var refDate = offer.ValidFrom > today ? offer.ValidFrom : today;
        var generalLists = productIds.Count == 0 ? Array.Empty<PriceListView>() : await PriceListQueries.InEffectAsync(db, null, refDate, productIds, ct);
        var general = productIds.ToDictionary(id => id, id => PriceListRules.Resolve(id, null, refDate, generalLists, null) is { Source: PriceSources.GeneralList } r ? r.Price : null);

        var kitDtos = new Dictionary<int, KitDto>();
        foreach (var p in prods.Values.Where(p => p.OfferId == offer.OfferId))
            kitDtos[p.ProductId] = await kits.GetAsync(p.PublicId, ct);

        var itemDtos = items.Where(i => prods.ContainsKey(i.ProductId)).Select(i =>
        {
            var p = prods[i.ProductId];
            var isKit = kitTypeId is not null && p.ProductTypeLookupId == kitTypeId;
            return new OfferItemDto(i.OfferItemId, p.PublicId, p.Sku, p.Name, p.Brand, isKit, p.OfferId == offer.OfferId, isKit && p.KitAdjustable, i.Price, p.SalePrice,
                general.GetValueOrDefault(i.ProductId), i.ImageFileId is int fid && filePublic.TryGetValue(fid, out var g) ? g : null,
                effective[i.OfferItemId] is int eid && filePublic.TryGetValue(eid, out var ge) ? ge : null, p.IsActive, kitDtos.GetValueOrDefault(p.ProductId));
        }).OrderBy(i => i.IsOfferKit ? 0 : 1).ThenBy(i => i.Brand ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Sku, StringComparer.OrdinalIgnoreCase).ToList();

        var warnings = await WarningsAsync(offer, prods, general, ct);

        var clientIds = offer.Clients.Where(c => c.IsActive).Select(c => c.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).Select(c => new { c.ClientId, c.PublicId, c.Code, c.Name }).ToListAsync(ct);
        var userIds = new[] { offer.CreatedBy, offer.UpdatedBy }.Where(u => u is not null).Select(u => u!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var clientDtos = clients.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => new OfferClientDto(c.PublicId, c.Code, c.Name)).ToList();

        return new OfferDto(offer.PublicId, offer.Name, offer.ValidFrom, offer.ValidTo, offer.ValidTo is not null, OfferRules.RangeText(offer.ValidFrom, offer.ValidTo),
            offer.IsPublished, PriceListRules.StateOf(offer.IsPublished, offer.ValidFrom, offer.ValidTo, today), InEffect(offer, today), offer.AppliesToAll, offer.Notes,
            offer.ImageFileId is int img && filePublic.TryGetValue(img, out var gi) ? gi : null, offer.IsActive, itemDtos, clientDtos, warnings,
            offer.CreatedAtUtc, offer.CreatedBy is int cb ? users.GetValueOrDefault(cb) : null, offer.UpdatedAtUtc, offer.UpdatedBy is int ub ? users.GetValueOrDefault(ub) : null);
    }

    /// <summary>
    /// Los avisos de la oferta (no son errores): otra oferta publicada y activa, en fechas y clientes que se cruzan, con el mismo producto igual o más barato
    /// (CHEAPER_OFFER), y los renglones más caros que la lista general (OFFER_ABOVE_GENERAL).
    /// </summary>
    private async Task<IReadOnlyList<OfferWarningDto>> WarningsAsync(Offer offer, IReadOnlyDictionary<int, Product> prods, IReadOnlyDictionary<int, decimal?> general, CancellationToken ct)
    {
        var result = new List<OfferWarningDto>();
        var mine = View(offer);
        if (mine.Items.Count == 0) return result;
        var productIds = mine.Items.Select(i => i.ProductId).ToList();
        var query = db.Set<Offer>().AsNoTracking()
            .Where(o => o.OfferId != offer.OfferId && o.IsActive && o.IsPublished && (o.ValidTo == null || o.ValidTo >= offer.ValidFrom)
                        && db.Set<OfferItem>().Any(i => i.OfferId == o.OfferId && i.IsActive && productIds.Contains(i.ProductId)));
        if (offer.ValidTo is DateOnly to) query = query.Where(o => o.ValidFrom <= to);
        var others = await query.Include(o => o.Items).Include(o => o.Clients).ToListAsync(ct);
        var publicIds = others.ToDictionary(o => o.OfferId, o => o.PublicId);
        foreach (var o in OfferRules.FindCheaperOverlaps(mine, others.Select(View)))
        {
            if (!prods.TryGetValue(o.ProductId, out var p)) continue;
            result.Add(new OfferWarningDto(PriceNoticeCodes.CheaperOffer, OfferRules.CheaperOfferMessage(p.Sku, o), p.PublicId, p.Sku, publicIds.GetValueOrDefault(o.OtherOfferId),
                o.OtherName, o.OtherPrice));
        }
        foreach (var i in mine.Items)
        {
            if (general.GetValueOrDefault(i.ProductId) is not decimal g || i.Price <= g || !prods.TryGetValue(i.ProductId, out var p)) continue;
            result.Add(new OfferWarningDto(PriceNoticeCodes.OfferAboveGeneral, OfferRules.AboveGeneralMessage(p.Sku, i.Price, g), p.PublicId, p.Sku, null, null, g));
        }
        return result;
    }
}
