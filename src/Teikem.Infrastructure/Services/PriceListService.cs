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

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 221 (P1 del pedido de vendedores) — listas de precios. Las establece el supervisor de ventas (sales.pricelist); verlas usa sales.view.
/// - Una lista tiene nombre, vigencia (desde obligatorio, hasta opcional), aplica a todos los clientes o a los escogidos, y se PUBLICA: en borrador no se aplica.
/// - Renglones: producto o kit (también una oferta) → precio, con foto opcional (StoredFile del producto). Solo productos propios y activos.
/// - Dos listas publicadas de la misma audiencia (dos generales, o dos de clientes que comparten un cliente) no pueden dar precio al mismo producto en fechas que se
///   cruzan (409). Una lista del cliente y una general sí: la del cliente gana.
/// - Las órdenes ya hechas no cambian: la línea guarda una copia del precio. Nunca DELETE: la lista se desactiva y renglones y clientes se quitan en lógico.
/// </summary>
public sealed class PriceListService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ILookupCache lookups, StoredFileService files)
{
    private TenantContext T => (TenantContext)tenant;

    // ---------------------------------------------------------------- consulta

    /// <summary>Las listas (las desactivadas solo con includeInactive), la más reciente primero; q = el nombre contiene; state = DRAFT/SCHEDULED/ACTIVE/EXPIRED.</summary>
    public async Task<IReadOnlyList<PriceListListItemDto>> ListAsync(string? q, string? state, bool includeInactive, CancellationToken ct)
    {
        var query = db.Set<PriceList>().AsNoTracking();
        if (!includeInactive) query = query.Where(l => l.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(l => l.Name.Contains(s));
        }
        var rows = await query.OrderByDescending(l => l.ValidFrom).ThenByDescending(l => l.PriceListId)
            .Select(l => new
            {
                List = l,
                Items = db.Set<PriceListItem>().Count(i => i.PriceListId == l.PriceListId && i.IsActive),
                Clients = db.Set<PriceListClient>().Count(c => c.PriceListId == l.PriceListId && c.IsActive),
            }).ToListAsync(ct);
        var today = clock.Today;
        var result = rows.Select(r => new PriceListListItemDto(r.List.PublicId, r.List.Name, r.List.ValidFrom, r.List.ValidTo, r.List.IsPublished,
            PriceListRules.StateOf(r.List.IsPublished, r.List.ValidFrom, r.List.ValidTo, today), r.List.AppliesToAll, r.Items, r.Clients, r.List.IsActive,
            r.List.CreatedAtUtc, r.List.UpdatedAtUtc));
        if (!string.IsNullOrWhiteSpace(state))
        {
            var st = state.Trim().ToUpperInvariant();
            result = result.Where(r => r.State == st);
        }
        return result.ToList();
    }

    public async Task<PriceListDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var list = await db.Set<PriceList>().AsNoTracking().Include(l => l.Items).Include(l => l.Clients).FirstOrDefaultAsync(l => l.PublicId == publicId, ct) ?? throw NotFound();
        return await ToDtoAsync(list, ct);
    }

    // ---------------------------------------------------------------- alta y edición

    public async Task<PriceListDto> CreateAsync(PriceListRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var (name, nameError) = PriceListRules.NormalizeName(req.Name);
        if (nameError is not null) throw new ValidationException("name", nameError);
        if (req.ValidFrom is not DateOnly from) throw new ValidationException("validFrom", PriceListRules.ValidFromRequired);
        var to = req.ClearValidTo == true ? null : req.ValidTo;
        var dateError = PriceListRules.DateRangeError(from, to);
        if (dateError is not null) throw new ValidationException("validTo", dateError);
        var (notes, notesError) = PriceListRules.NormalizeNotes(req.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);

        var list = new PriceList
        {
            PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), Name = name!, ValidFrom = from, ValidTo = to, AppliesToAll = req.AppliesToAll ?? true,
            Notes = notes, IsPublished = false, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        db.Set<PriceList>().Add(list);
        await db.SaveGuardedAsync(PriceListRules.Concurrency, ct);
        return await GetAsync(list.PublicId, ct);
    }

    /// <summary>PATCH: lo que no viene no cambia. En una lista publicada se vuelve a comprobar que no se cruce con otra (409).</summary>
    public Task<PriceListDto> UpdateAsync(Guid publicId, PriceListRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        string? name = null;
        if (req.Name is not null)
        {
            var (n, e) = PriceListRules.NormalizeName(req.Name);
            if (e is not null) throw new ValidationException("name", e);
            name = n;
        }
        var (notes, notesError) = PriceListRules.NormalizeNotes(req.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        return MutateAsync(publicId, (list, _) =>
        {
            if (name is not null) list.Name = name;
            if (req.ValidFrom is DateOnly f) list.ValidFrom = f;
            if (req.ClearValidTo == true) list.ValidTo = null;
            else if (req.ValidTo is DateOnly t) list.ValidTo = t;
            var dateError = PriceListRules.DateRangeError(list.ValidFrom, list.ValidTo);
            if (dateError is not null) throw new ValidationException("validTo", dateError);
            if (req.Notes is not null) list.Notes = notes;
            if (req.AppliesToAll is bool all && all != list.AppliesToAll)
            {
                if (all && list.Clients.Any(c => c.IsActive)) throw new StatusRuleException(PriceListRules.GeneralWithClients);
                if (!all && list.IsPublished) throw new StatusRuleException(PriceListRules.PublishNeedsClients);   // publicada sin clientes no aplicaría a nadie
                list.AppliesToAll = all;
            }
            return Task.CompletedTask;
        }, ct);
    }

    /// <summary>Publica la lista (borrador → se aplica en su vigencia). Exige renglones, clientes si no es general, y que no se cruce con otra publicada.</summary>
    public Task<PriceListDto> PublishAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, (list, _) =>
    {
        if (list.IsPublished) throw new StatusRuleException(PriceListRules.AlreadyPublished);
        if (!list.Items.Any(i => i.IsActive)) throw new StatusRuleException(PriceListRules.PublishNeedsItems);
        if (!list.AppliesToAll && !list.Clients.Any(c => c.IsActive)) throw new StatusRuleException(PriceListRules.PublishNeedsClients);
        list.IsPublished = true;
        return Task.CompletedTask;
    }, ct);

    /// <summary>Devuelve la lista a borrador: deja de aplicarse a los pedidos nuevos (los ya hechos no cambian).</summary>
    public Task<PriceListDto> UnpublishAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, (list, _) =>
    {
        if (!list.IsPublished) throw new StatusRuleException(PriceListRules.NotPublished);
        list.IsPublished = false;
        return Task.CompletedTask;
    }, ct);

    /// <summary>Baja lógica: deja de aplicarse. Los pedidos ya hechos no cambian.</summary>
    public Task<PriceListDto> DeactivateAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, (list, _) =>
    {
        if (!list.IsActive) throw new StatusRuleException(PriceListRules.AlreadyInactive);
        list.IsActive = false;
        return Task.CompletedTask;
    }, ct, allowInactive: true);

    /// <summary>La reactiva tal como estaba (si estaba publicada, se vuelve a comprobar que no se cruce con otra).</summary>
    public Task<PriceListDto> ReactivateAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, (list, _) =>
    {
        if (list.IsActive) throw new StatusRuleException(PriceListRules.AlreadyActive);
        list.IsActive = true;
        return Task.CompletedTask;
    }, ct, allowInactive: true);

    // ---------------------------------------------------------------- renglones

    /// <summary>Agrega renglones o cambia el precio de los que ya están (por producto). Los que no vienen no se tocan.</summary>
    public async Task<PriceListDto> UpsertItemsAsync(Guid publicId, PriceListItemsRequest? req, CancellationToken ct)
    {
        var items = req?.Items;
        if (items is null || items.Count == 0) throw new ValidationException("items", PriceListRules.ItemsRequired);
        if (items.Count > PriceListCodes.MaxItems) throw new ValidationException("items", PriceListRules.TooManyItems);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].ProductPublicId is null) throw new ValidationException($"items[{i}].productPublicId", PriceListRules.ItemProductRequired);
            if (items[i].Price is null) throw new ValidationException($"items[{i}].price", PriceListRules.ItemPriceRequired);
            var pe = SalesRules.PriceError(items[i].Price);
            if (pe is not null) throw new ValidationException($"items[{i}].price", pe);
        }
        var ids = items.Select(i => i.ProductPublicId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
        var seen = new HashSet<Guid>();
        for (var i = 0; i < items.Count; i++)
        {
            var pid = items[i].ProductPublicId!.Value;
            var p = products.GetValueOrDefault(pid) ?? throw new NotFoundException("Producto");
            if (!seen.Add(pid)) throw new ValidationException($"items[{i}].productPublicId", PriceListRules.ItemRepeated(p.Sku));
            if (p.ClientId is not null) throw new StatusRuleException(PriceListRules.ItemProductNotOwn);
            if (!p.IsActive) throw new StatusRuleException(PriceListRules.ItemProductInactive(p.Sku));
            if (p.OfferId is int offerId)   // Lote 229: el kit de una oferta lleva su precio en la oferta
                throw new StatusRuleException(OfferRules.OfferKitNotInLists(p.Sku,
                    await db.Set<Offer>().AsNoTracking().Where(o => o.OfferId == offerId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "?"));
        }
        return await MutateAsync(publicId, (list, _) =>
        {
            var active = list.Items.Where(x => x.IsActive).ToDictionary(x => x.ProductId);
            foreach (var r in items)
            {
                var product = products[r.ProductPublicId!.Value];
                if (active.TryGetValue(product.ProductId, out var existing)) existing.Price = r.Price!.Value;
                else
                {
                    var row = new PriceListItem { TenantId = list.TenantId, ProductId = product.ProductId, Price = r.Price!.Value, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
                    list.Items.Add(row);
                    active[product.ProductId] = row;
                }
            }
            if (active.Count > PriceListCodes.MaxItems) throw new ValidationException("items", PriceListRules.TooManyItems);
            return Task.CompletedTask;
        }, ct);
    }

    public Task<PriceListDto> SetItemPriceAsync(Guid publicId, int itemId, PriceListItemPriceRequest? req, CancellationToken ct)
    {
        if (req?.Price is null) throw new ValidationException("price", PriceListRules.ItemPriceRequired);
        var pe = SalesRules.PriceError(req.Price);
        if (pe is not null) throw new ValidationException("price", pe);
        return MutateAsync(publicId, (list, _) =>
        {
            RequireItem(list, itemId).Price = req.Price.Value;
            return Task.CompletedTask;
        }, ct);
    }

    /// <summary>Quita el renglón (en lógico).</summary>
    public Task<PriceListDto> RemoveItemAsync(Guid publicId, int itemId, CancellationToken ct) => MutateAsync(publicId, (list, _) =>
    {
        RequireItem(list, itemId).IsActive = false;
        return Task.CompletedTask;
    }, ct);

    /// <summary>Pone (o cambia) la foto del renglón: una imagen, guardada como archivo del producto del renglón (se ve con permiso de ver productos).</summary>
    public async Task<PriceListDto> SetItemImageAsync(Guid publicId, int itemId, string? fileName, string? contentType, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        EnsureImage(content);
        var row = await db.Set<PriceList>().AsNoTracking().Where(l => l.PublicId == publicId)
            .Select(l => new { l.IsActive, Item = db.Set<PriceListItem>().Where(i => i.PriceListId == l.PriceListId && i.PriceListItemId == itemId && i.IsActive).Select(i => (int?)i.ProductId).FirstOrDefault() })
            .FirstOrDefaultAsync(ct) ?? throw NotFound();
        if (!row.IsActive) throw new StatusRuleException(PriceListRules.Inactive);
        if (row.Item is not int productId) throw new NotFoundException(PriceListRules.ItemNotFound);
        var file = await files.AttachAsync(EntityTypes.Product, productId, StoredFileCodes.KindDocument, fileName, contentType, content, ct, enforceOwnerWrite: false);
        var fileId = await db.Set<StoredFile>().AsNoTracking().Where(f => f.PublicId == file.PublicId).Select(f => f.StoredFileId).FirstAsync(ct);
        return await MutateAsync(publicId, (list, _) =>
        {
            RequireItem(list, itemId).ImageFileId = fileId;
            return Task.CompletedTask;
        }, ct);
    }

    public Task<PriceListDto> ClearItemImageAsync(Guid publicId, int itemId, CancellationToken ct) => MutateAsync(publicId, (list, _) =>
    {
        RequireItem(list, itemId).ImageFileId = null;
        return Task.CompletedTask;
    }, ct);

    // ---------------------------------------------------------------- clientes

    /// <summary>Agrega clientes a una lista que no es general (los que ya están se ignoran). Solo clientes activos de la compañía.</summary>
    public async Task<PriceListDto> AddClientsAsync(Guid publicId, PriceListClientsRequest? req, CancellationToken ct)
    {
        var ids = req?.ClientPublicIds?.Distinct().ToList();
        if (ids is null || ids.Count == 0) throw new ValidationException("clientPublicIds", PriceListRules.ClientsRequired);
        if (ids.Count > PriceListCodes.MaxClients) throw new ValidationException("clientPublicIds", PriceListRules.TooManyClients);
        var clients = await db.Set<Client>().AsNoTracking().Where(c => ids.Contains(c.PublicId)).Select(c => new { c.PublicId, c.ClientId, c.IsActive }).ToListAsync(ct);
        if (clients.Count != ids.Count) throw new NotFoundException("Cliente");
        if (clients.Any(c => !c.IsActive)) throw new StatusRuleException(SalesRules.ClientInactive);
        return await MutateAsync(publicId, (list, _) =>
        {
            if (list.AppliesToAll) throw new StatusRuleException(PriceListRules.ClientsOnGeneralList);
            var active = list.Clients.Where(c => c.IsActive).Select(c => c.ClientId).ToHashSet();
            foreach (var c in clients)
                if (active.Add(c.ClientId))
                    list.Clients.Add(new PriceListClient { TenantId = list.TenantId, ClientId = c.ClientId, IsActive = true, CreatedAtUtc = DateTime.UtcNow });
            if (active.Count > PriceListCodes.MaxClients) throw new ValidationException("clientPublicIds", PriceListRules.TooManyClients);
            return Task.CompletedTask;
        }, ct);
    }

    /// <summary>Quita un cliente de la lista (en lógico). Una lista publicada no se queda sin clientes: se devuelve a borrador antes.</summary>
    public async Task<PriceListDto> RemoveClientAsync(Guid publicId, Guid clientPublicId, CancellationToken ct)
    {
        var clientId = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == clientPublicId).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Cliente");
        return await MutateAsync(publicId, (list, _) =>
        {
            var row = list.Clients.FirstOrDefault(c => c.IsActive && c.ClientId == clientId) ?? throw new NotFoundException(PriceListRules.ClientNotInList);
            row.IsActive = false;
            if (list.IsPublished && !list.Clients.Any(c => c.IsActive)) throw new StatusRuleException(PriceListRules.PublishNeedsClients);
            return Task.CompletedTask;
        }, ct);
    }

    // ---------------------------------------------------------------- piezas

    private static NotFoundException NotFound() => new(PriceListRules.NotFound, feminine: true);

    private static PriceListItem RequireItem(PriceList list, int itemId)
        => list.Items.FirstOrDefault(i => i.PriceListItemId == itemId && i.IsActive) ?? throw new NotFoundException(PriceListRules.ItemNotFound);

    /// <summary>La foto debe ser una imagen (por su contenido, no por lo que diga el cliente). El tope de tamaño y el resto lo valida el almacén de archivos.</summary>
    internal static void EnsureImage(ReadOnlyMemory<byte> content)
    {
        if (content.Length == 0) throw new ValidationException("file", PriceListRules.ImageRequired);
        var real = StoredFileRules.SniffContentType(content.Span);
        if (real is null || !real.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) throw new ValidationException("file", PriceListRules.ImageNotImage);
    }

    /// <summary>
    /// Carga la lista (con renglones y clientes) para cambiarla, aplica el cambio y, si queda publicada y activa, comprueba que no se cruce con otra publicada antes
    /// de guardar (409). Una lista desactivada no se cambia (salvo reactivarla).
    /// </summary>
    private async Task<PriceListDto> MutateAsync(Guid publicId, Func<PriceList, CancellationToken, Task> change, CancellationToken ct, bool allowInactive = false)
    {
        try
        {
            await db.RunInTransactionAsync(async ct2 =>
            {
                var list = await db.Set<PriceList>().AsTracking().Include(l => l.Items).Include(l => l.Clients).FirstOrDefaultAsync(l => l.PublicId == publicId, ct2) ?? throw NotFound();
                if (!list.IsActive && !allowInactive) throw new StatusRuleException(PriceListRules.Inactive);
                await change(list, ct2);
                await EnsureNoConflictsAsync(list, ct2);
                await db.SaveGuardedAsync(PriceListRules.Concurrency, ct2);
            }, ct);
        }
        catch
        {
            db.ChangeTracker.Clear();   // el cambio rechazado no se queda pendiente para el próximo guardado del mismo contexto
            throw;
        }
        return await GetAsync(publicId, ct);
    }

    private async Task EnsureNoConflictsAsync(PriceList list, CancellationToken ct)
    {
        if (!list.IsPublished || !list.IsActive) return;
        var query = db.Set<PriceList>().AsNoTracking()
            .Where(o => o.PriceListId != list.PriceListId && o.IsActive && o.IsPublished && o.AppliesToAll == list.AppliesToAll && (o.ValidTo == null || o.ValidTo >= list.ValidFrom));
        if (list.ValidTo is DateOnly to) query = query.Where(o => o.ValidFrom <= to);
        var others = await query.Include(o => o.Items).Include(o => o.Clients).ToListAsync(ct);
        if (others.Count == 0) return;
        var conflicts = PriceListRules.FindConflicts(View(list), others.Select(View));
        if (conflicts.Count == 0) return;
        var first = conflicts[0];
        var skus = await db.Set<Product>().AsNoTracking().Where(p => first.ProductIds.Contains(p.ProductId)).OrderBy(p => p.Sku).Select(p => p.Sku).ToListAsync(ct);
        throw new ConflictException(PriceListRules.Overlap(first.OtherName, skus));
    }

    /// <summary>La lista vista por las reglas puras: solo renglones y clientes activos.</summary>
    internal static PriceListView View(PriceList l) => new(l.PriceListId, l.Name, l.IsPublished, l.AppliesToAll, l.ValidFrom, l.ValidTo,
        l.Clients.Where(c => c.IsActive).Select(c => c.ClientId).ToList(),
        l.Items.Where(i => i.IsActive).Select(i => new PriceListItemView(i.ProductId, i.Price, i.ImageFileId)).ToList());

    private async Task<PriceListDto> ToDtoAsync(PriceList list, CancellationToken ct)
    {
        var items = list.Items.Where(i => i.IsActive).ToList();
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        // Lote 229: la foto que se muestra = la del renglón → la del producto → la de la primera pieza con foto si es kit
        var productImages = await PriceListQueries.ProductImagesAsync(db, products.Values, kitTypeId, ct);
        var effective = items.ToDictionary(i => i.PriceListItemId, i => ProductImageRules.Resolve(i.ImageFileId, productImages.GetValueOrDefault(i.ProductId), false, null));
        var filePublic = await PriceListQueries.FilePublicIdsAsync(db, items.Select(i => i.ImageFileId).Concat(effective.Values), ct);
        var clientIds = list.Clients.Where(c => c.IsActive).Select(c => c.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).Select(c => new { c.ClientId, c.PublicId, c.Code, c.Name }).ToListAsync(ct);
        var userIds = new[] { list.CreatedBy, list.UpdatedBy }.Where(u => u is not null).Select(u => u!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);

        var itemDtos = items.Where(i => products.ContainsKey(i.ProductId)).Select(i =>
        {
            var p = products[i.ProductId];
            return new PriceListItemDto(i.PriceListItemId, p.PublicId, p.Sku, p.Name, p.Brand, kitTypeId is not null && p.ProductTypeLookupId == kitTypeId, false, i.Price,
                p.SalePrice, i.ImageFileId is int fid && filePublic.TryGetValue(fid, out var g) ? g : null, p.IsActive,
                effective[i.PriceListItemId] is int eid && filePublic.TryGetValue(eid, out var ge) ? ge : null);
        }).OrderBy(i => i.Brand ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Sku, StringComparer.OrdinalIgnoreCase).ToList();
        var clientDtos = clients.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => new PriceListClientDto(c.PublicId, c.Code, c.Name)).ToList();
        return new PriceListDto(list.PublicId, list.Name, list.ValidFrom, list.ValidTo, list.IsPublished, PriceListRules.StateOf(list.IsPublished, list.ValidFrom, list.ValidTo, clock.Today),
            list.AppliesToAll, list.Notes, list.IsActive, itemDtos, clientDtos, list.CreatedAtUtc, list.CreatedBy is int cb ? users.GetValueOrDefault(cb) : null,
            list.UpdatedAtUtc, list.UpdatedBy is int ub ? users.GetValueOrDefault(ub) : null);
    }
}
