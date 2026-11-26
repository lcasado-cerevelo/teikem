using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 221 (P1 del pedido de vendedores) — listas de precios. Las establece el supervisor de ventas (sales.pricelist); verlas usa sales.view.
/// - Una lista tiene nombre, vigencia (desde obligatorio, hasta opcional), aplica a todos los clientes o a los escogidos, y se PUBLICA: en borrador no se aplica.
/// - Renglones: producto o kit (también una oferta) → precio, con foto opcional (StoredFile del producto). Solo productos propios y activos.
/// - Dos listas publicadas de la misma audiencia (dos generales, o dos de clientes que comparten un cliente) no pueden dar precio al mismo producto en fechas que se
///   cruzan (409). Una lista del cliente y una general sí: gana el precio más bajo (Lote 247).
/// - Las órdenes ya hechas no cambian: la línea guarda una copia del precio. Nunca DELETE: la lista se desactiva y renglones y clientes se quitan en lógico.
/// - Lote 247 (Lote C): una lista que no es general se escoge también por CADENAS (PriceListChain, máx. 500). Las listas DEDICADAS (el precio especial de un
///   cliente o de una cadena, PriceList.SpecialClientId / SpecialChainId) las crea «Precios especiales» (<c>PriceListService.SpecialPrices.cs</c>): aquí se
///   ocultan por omisión en la lista de listas, y su nombre, fechas y a quién aplica dan 422 DedicatedLocked (precios, renglones, publicar y desactivar sí).
/// </summary>
public sealed partial class PriceListService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ILookupCache lookups, StoredFileService files)
{
    private TenantContext T => (TenantContext)tenant;

    // ---------------------------------------------------------------- consulta

    /// <summary>
    /// Las listas (las desactivadas solo con includeInactive), la más reciente primero; q = el nombre contiene; state = DRAFT/SCHEDULED/ACTIVE/EXPIRED. Lote 247:
    /// las listas dedicadas (precios especiales) solo con <paramref name="includeSpecial"/> (DECISIÓN 3); cada lista lleva sus cadenas y, si es dedicada, su dueño.
    /// </summary>
    public async Task<IReadOnlyList<PriceListListItemDto>> ListAsync(string? q, string? state, bool includeInactive, CancellationToken ct, bool includeSpecial = false)
    {
        var query = db.Set<PriceList>().AsNoTracking();
        if (!includeInactive) query = query.Where(l => l.IsActive);
        if (!includeSpecial) query = query.Where(l => l.SpecialClientId == null && l.SpecialChainId == null);
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
                Chains = db.Set<PriceListChain>().Count(c => c.PriceListId == l.PriceListId && c.IsActive),
                SpecialClientName = db.Clients.Where(c => c.ClientId == l.SpecialClientId).Select(c => c.Name).FirstOrDefault(),
                SpecialChainName = db.Set<ClientChain>().Where(c => c.ClientChainId == l.SpecialChainId).Select(c => c.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        var today = clock.Today;
        var result = rows.Select(r => new PriceListListItemDto(r.List.PublicId, r.List.Name, r.List.ValidFrom, r.List.ValidTo, r.List.IsPublished,
            PriceListRules.StateOf(r.List.IsPublished, r.List.ValidFrom, r.List.ValidTo, today), r.List.AppliesToAll, r.Items, r.Clients, r.List.IsActive,
            r.List.CreatedAtUtc, r.List.UpdatedAtUtc, r.Chains,
            r.List.SpecialClientId is not null ? SpecialPriceOwnerKinds.Client : r.List.SpecialChainId is not null ? SpecialPriceOwnerKinds.Chain : null,
            r.List.SpecialClientId is not null ? r.SpecialClientName : r.List.SpecialChainId is not null ? r.SpecialChainName : null));
        if (!string.IsNullOrWhiteSpace(state))
        {
            var st = state.Trim().ToUpperInvariant();
            result = result.Where(r => r.State == st);
        }
        return result.ToList();
    }

    public async Task<PriceListDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var list = await db.Set<PriceList>().AsNoTracking().Include(l => l.Items).Include(l => l.Clients).Include(l => l.Chains)
            .FirstOrDefaultAsync(l => l.PublicId == publicId, ct) ?? throw NotFound();
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

    /// <summary>
    /// PATCH: lo que no viene no cambia. En una lista publicada se vuelve a comprobar que no se cruce con otra (409). Lote 247: en una lista dedicada (precio
    /// especial), cambiar el nombre, las fechas o «aplica a todos» da 422 DedicatedLocked (las notas sí se cambian); hacerla general con cadenas da 422.
    /// </summary>
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
        return MutateAsync(publicId, async (list, ct2) =>
        {
            if (list.IsSpecial)
            {
                var changesName = name is not null && name != list.Name;
                var changesFrom = req.ValidFrom is DateOnly nf && nf != list.ValidFrom;
                var changesTo = req.ClearValidTo == true ? list.ValidTo is not null : req.ValidTo is DateOnly nt && nt != list.ValidTo;
                var changesAudience = req.AppliesToAll is bool na && na != list.AppliesToAll;
                if (changesName || changesFrom || changesTo || changesAudience) throw new StatusRuleException(PriceListRules.DedicatedLocked(await SpecialOwnerNameAsync(list, ct2)));
            }
            if (name is not null) list.Name = name;
            if (req.ValidFrom is DateOnly f) list.ValidFrom = f;
            if (req.ClearValidTo == true) list.ValidTo = null;
            else if (req.ValidTo is DateOnly t) list.ValidTo = t;
            var dateError = PriceListRules.DateRangeError(list.ValidFrom, list.ValidTo);
            if (dateError is not null) throw new ValidationException("validTo", dateError);
            if (req.Notes is not null) list.Notes = notes;
            if (req.AppliesToAll is bool all && all != list.AppliesToAll)
            {
                if (all && HasAudienceRows(list)) throw new StatusRuleException(PriceListRules.GeneralWithClients);
                if (!all && list.IsPublished) throw new StatusRuleException(PriceListRules.PublishNeedsClients);   // publicada sin clientes no aplicaría a nadie
                list.AppliesToAll = all;
            }
        }, ct);
    }

    /// <summary>Publica la lista (borrador → se aplica en su vigencia). Exige renglones, clientes o cadenas si no es general, y que no se cruce con otra publicada.</summary>
    public Task<PriceListDto> PublishAsync(Guid publicId, CancellationToken ct) => MutateAsync(publicId, (list, _) =>
    {
        if (list.IsPublished) throw new StatusRuleException(PriceListRules.AlreadyPublished);
        if (!list.Items.Any(i => i.IsActive)) throw new StatusRuleException(PriceListRules.PublishNeedsItems);
        if (!list.AppliesToAll && !HasAudienceRows(list)) throw new StatusRuleException(PriceListRules.PublishNeedsClients);
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
            if (p.IsOfferKit) throw new StatusRuleException(OfferKitRules.NotInPriceLists(p.Sku));   // Lote 229/231: el kit de oferta lleva su precio en una oferta
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
        await PriceListQueries.EnsurePhotosEnabledAsync(db, ct);   // Lote 240
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

    /// <summary>Agrega clientes a una lista que no es general (los que ya están se ignoran). Solo clientes activos de la compañía. Lote 247: en una dedicada, 422.</summary>
    public async Task<PriceListDto> AddClientsAsync(Guid publicId, PriceListClientsRequest? req, CancellationToken ct)
    {
        var ids = req?.ClientPublicIds?.Distinct().ToList();
        if (ids is null || ids.Count == 0) throw new ValidationException("clientPublicIds", PriceListRules.ClientsRequired);
        if (ids.Count > PriceListCodes.MaxClients) throw new ValidationException("clientPublicIds", PriceListRules.TooManyClients);
        var clients = await db.Set<Client>().AsNoTracking().Where(c => ids.Contains(c.PublicId)).Select(c => new { c.PublicId, c.ClientId, c.IsActive }).ToListAsync(ct);
        if (clients.Count != ids.Count) throw new NotFoundException("Cliente");
        if (clients.Any(c => !c.IsActive)) throw new StatusRuleException(SalesRules.ClientInactive);
        return await MutateAsync(publicId, async (list, ct2) =>
        {
            await EnsureNotDedicatedAsync(list, ct2);
            if (list.AppliesToAll) throw new StatusRuleException(PriceListRules.ClientsOnGeneralList);
            var active = list.Clients.Where(c => c.IsActive).Select(c => c.ClientId).ToHashSet();
            foreach (var c in clients)
                if (active.Add(c.ClientId))
                    list.Clients.Add(new PriceListClient { TenantId = list.TenantId, ClientId = c.ClientId, IsActive = true, CreatedAtUtc = DateTime.UtcNow });
            if (active.Count > PriceListCodes.MaxClients) throw new ValidationException("clientPublicIds", PriceListRules.TooManyClients);
        }, ct);
    }

    /// <summary>Quita un cliente de la lista (en lógico). Una lista publicada no se queda sin clientes ni cadenas: se devuelve a borrador antes. En una dedicada, 422.</summary>
    public async Task<PriceListDto> RemoveClientAsync(Guid publicId, Guid clientPublicId, CancellationToken ct)
    {
        var clientId = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == clientPublicId).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Cliente");
        return await MutateAsync(publicId, async (list, ct2) =>
        {
            await EnsureNotDedicatedAsync(list, ct2);
            var row = list.Clients.FirstOrDefault(c => c.IsActive && c.ClientId == clientId) ?? throw new NotFoundException(PriceListRules.ClientNotInList);
            row.IsActive = false;
            if (list.IsPublished && !HasAudienceRows(list)) throw new StatusRuleException(PriceListRules.PublishNeedsClients);
        }, ct);
    }

    // ---------------------------------------------------------------- cadenas (Lote 247)

    /// <summary>
    /// Agrega cadenas a una lista que no es general (las que ya están se ignoran; máximo 500). 400 sin cadenas o con más de 500; 404 «Cadena no encontrada.» (también
    /// la de otra compañía); 422 si alguna está desactivada, si la lista aplica a todos o si es una dedicada.
    /// </summary>
    public async Task<PriceListDto> AddChainsAsync(Guid publicId, PriceListChainsRequest? req, CancellationToken ct)
    {
        var ids = req?.ChainPublicIds?.Distinct().ToList();
        if (ids is null || ids.Count == 0) throw new ValidationException("chainPublicIds", PriceListRules.ChainsRequired);
        if (ids.Count > PriceListCodes.MaxChains) throw new ValidationException("chainPublicIds", PriceListRules.TooManyChains);
        var chains = await db.Set<ClientChain>().AsNoTracking().Where(c => ids.Contains(c.PublicId)).Select(c => new { c.ClientChainId, c.IsActive }).ToListAsync(ct);
        if (chains.Count != ids.Count) throw ChainNotFound();
        if (chains.Any(c => !c.IsActive)) throw new StatusRuleException(ClientChainRules.ChainInactive);
        return await MutateAsync(publicId, async (list, ct2) =>
        {
            await EnsureNotDedicatedAsync(list, ct2);
            if (list.AppliesToAll) throw new StatusRuleException(PriceListRules.ChainsOnGeneralList);
            var active = list.Chains.Where(c => c.IsActive).Select(c => c.ChainId).ToHashSet();
            foreach (var c in chains)
                if (active.Add(c.ClientChainId))
                    list.Chains.Add(new PriceListChain { TenantId = list.TenantId, ChainId = c.ClientChainId, IsActive = true, CreatedAtUtc = DateTime.UtcNow });
            if (active.Count > PriceListCodes.MaxChains) throw new ValidationException("chainPublicIds", PriceListRules.TooManyChains);
        }, ct);
    }

    /// <summary>
    /// Quita una cadena de la lista (en lógico). 404 «Cadena no encontrada.» o «Cadena de la lista no encontrada.»; una lista publicada no se queda sin clientes ni
    /// cadenas (422: se devuelve a borrador antes); en una dedicada, 422.
    /// </summary>
    public async Task<PriceListDto> RemoveChainAsync(Guid publicId, Guid chainPublicId, CancellationToken ct)
    {
        var chainId = await ClientChainQueries.ChainIdByPublicIdAsync(db, chainPublicId, ct) ?? throw ChainNotFound();
        return await MutateAsync(publicId, async (list, ct2) =>
        {
            await EnsureNotDedicatedAsync(list, ct2);
            var row = list.Chains.FirstOrDefault(c => c.IsActive && c.ChainId == chainId) ?? throw new NotFoundException(PriceListRules.ChainNotInList, feminine: true);
            row.IsActive = false;
            if (list.IsPublished && !HasAudienceRows(list)) throw new StatusRuleException(PriceListRules.PublishNeedsClients);
        }, ct);
    }

    // ---------------------------------------------------------------- piezas

    private static NotFoundException NotFound() => new(PriceListRules.NotFound, feminine: true);

    /// <summary>«Cadena no encontrada.» (= ClientChainRules.NotFound).</summary>
    private static NotFoundException ChainNotFound() => new("Cadena", feminine: true);

    /// <summary>¿La lista tiene a quién aplicar además de «todos»? (clientes o cadenas activos).</summary>
    private static bool HasAudienceRows(PriceList list) => list.Clients.Any(c => c.IsActive) || list.Chains.Any(c => c.IsActive);

    /// <summary>Lote 247: a quién aplica una lista dedicada no se cambia desde los endpoints genéricos (422 DedicatedLocked).</summary>
    private async Task EnsureNotDedicatedAsync(PriceList list, CancellationToken ct)
    {
        if (list.IsSpecial) throw new StatusRuleException(PriceListRules.DedicatedLocked(await SpecialOwnerNameAsync(list, ct)));
    }

    /// <summary>El nombre del dueño de una lista dedicada (cliente o cadena; vacío si no es dedicada).</summary>
    private async Task<string> SpecialOwnerNameAsync(PriceList list, CancellationToken ct)
    {
        if (list.SpecialClientId is int clientId)
            return await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        if (list.SpecialChainId is int chainId)
            return await db.Set<ClientChain>().AsNoTracking().Where(c => c.ClientChainId == chainId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        return string.Empty;
    }

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
                var list = await db.Set<PriceList>().AsTracking().Include(l => l.Items).Include(l => l.Clients).Include(l => l.Chains)
                    .FirstOrDefaultAsync(l => l.PublicId == publicId, ct2) ?? throw NotFound();
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

    /// <summary>
    /// 409 si la lista (publicada y activa) se cruza con otra publicada (<see cref="PriceListRules.FindConflicts"/>: Lote 247, solo listas del mismo tipo). Lote 247:
    /// las listas que este mismo trabajo ya tiene cargadas y cambiadas (p. ej. la dedicada de la que se acaba de quitar un precio especial que se mueve de fechas)
    /// cuentan como quedan, no como están guardadas.
    /// </summary>
    private async Task EnsureNoConflictsAsync(PriceList list, CancellationToken ct)
    {
        if (!list.IsPublished || !list.IsActive) return;
        var query = db.Set<PriceList>().AsNoTracking()
            .Where(o => o.PriceListId != list.PriceListId && o.IsActive && o.IsPublished && o.AppliesToAll == list.AppliesToAll && (o.ValidTo == null || o.ValidTo >= list.ValidFrom));
        if (list.ValidTo is DateOnly to) query = query.Where(o => o.ValidFrom <= to);
        var others = await query.Include(o => o.Items).Include(o => o.Clients).Include(o => o.Chains).ToListAsync(ct);
        if (others.Count == 0) return;
        var pending = db.ChangeTracker.Entries<PriceList>().Select(e => e.Entity).Where(e => e.PriceListId != 0 && e.PriceListId != list.PriceListId)
            .GroupBy(e => e.PriceListId).ToDictionary(g => g.Key, g => g.First());
        var current = others.Select(o => pending.TryGetValue(o.PriceListId, out var t) ? t : o).Where(o => o.IsActive && o.IsPublished);
        var conflicts = PriceListRules.FindConflicts(View(list), current.Select(View));
        if (conflicts.Count == 0) return;
        var first = conflicts[0];
        var skus = await db.Set<Product>().AsNoTracking().Where(p => first.ProductIds.Contains(p.ProductId)).OrderBy(p => p.Sku).Select(p => p.Sku).ToListAsync(ct);
        throw new ConflictException(PriceListRules.Overlap(first.OtherName, skus));
    }

    /// <summary>La lista vista por las reglas puras: solo renglones, clientes y cadenas activos (Lote 247: más si es dedicada).</summary>
    internal static PriceListView View(PriceList l) => new(l.PriceListId, l.Name, l.IsPublished, l.AppliesToAll, l.ValidFrom, l.ValidTo,
        l.Clients.Where(c => c.IsActive).Select(c => c.ClientId).ToList(),
        l.Items.Where(i => i.IsActive).Select(i => new PriceListItemView(i.ProductId, i.Price, i.ImageFileId)).ToList(),
        l.Chains.Where(c => c.IsActive).Select(c => c.ChainId).ToList(), l.IsSpecial);

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

        // Lote 247: el precio de la lista general para comparar, a la fecha de referencia max(«desde», hoy), y los avisos de un precio negociado más caro
        var refDate = ReferenceDate(list.ValidFrom);
        var general = await GeneralPricesAsync(items.Select(i => (refDate, i.ProductId)), list.AppliesToAll ? list.PriceListId : null, ct);
        var itemDtos = items.Where(i => products.ContainsKey(i.ProductId)).Select(i =>
        {
            var p = products[i.ProductId];
            return new PriceListItemDto(i.PriceListItemId, p.PublicId, p.Sku, p.Name, p.Brand, kitTypeId is not null && p.ProductTypeLookupId == kitTypeId, false, i.Price,
                p.SalePrice, i.ImageFileId is int fid && filePublic.TryGetValue(fid, out var g) ? g : null, p.IsActive,
                effective[i.PriceListItemId] is int eid && filePublic.TryGetValue(eid, out var ge) ? ge : null,
                !list.AppliesToAll && general.TryGetValue((refDate, i.ProductId), out var gp) ? gp : null);
        }).OrderBy(i => i.Brand ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Sku, StringComparer.OrdinalIgnoreCase).ToList();
        var warnings = itemDtos.Where(i => i.GeneralListPrice is decimal g && i.Price > g)
            .Select(i => AboveGeneralWarning(i.ProductPublicId, i.Sku, i.Price, i.GeneralListPrice!.Value)).ToList();
        var clientDtos = clients.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => new PriceListClientDto(c.PublicId, c.Code, c.Name)).ToList();
        var chainDtos = await ChainDtosAsync(list.Chains.Where(c => c.IsActive).Select(c => c.ChainId), ct);

        string? ownerKind = null, ownerName = null;
        Guid? ownerPublicId = null;
        if (list.SpecialClientId is int sc)
        {
            var o = await db.Clients.AsNoTracking().Where(c => c.ClientId == sc).Select(c => new { c.PublicId, c.Name }).FirstOrDefaultAsync(ct);
            (ownerKind, ownerPublicId, ownerName) = (SpecialPriceOwnerKinds.Client, o?.PublicId, o?.Name);
        }
        else if (list.SpecialChainId is int sch)
        {
            var o = await db.Set<ClientChain>().AsNoTracking().Where(c => c.ClientChainId == sch).Select(c => new { c.PublicId, c.Name }).FirstOrDefaultAsync(ct);
            (ownerKind, ownerPublicId, ownerName) = (SpecialPriceOwnerKinds.Chain, o?.PublicId, o?.Name);
        }
        return new PriceListDto(list.PublicId, list.Name, list.ValidFrom, list.ValidTo, list.IsPublished, PriceListRules.StateOf(list.IsPublished, list.ValidFrom, list.ValidTo, clock.Today),
            list.AppliesToAll, list.Notes, list.IsActive, itemDtos, clientDtos, list.CreatedAtUtc, list.CreatedBy is int cb ? users.GetValueOrDefault(cb) : null,
            list.UpdatedAtUtc, list.UpdatedBy is int ub ? users.GetValueOrDefault(ub) : null,
            chainDtos, ownerKind, ownerPublicId, ownerName, warnings);
    }

    /// <summary>Lote 247: la fecha con la que se compara contra la lista general: el «desde» si todavía no empieza; si no, hoy.</summary>
    private DateOnly ReferenceDate(DateOnly validFrom) => validFrom > clock.Today ? validFrom : clock.Today;

    /// <summary>Lote 247: aviso SPECIAL_ABOVE_GENERAL (no bloquea: se cobra el más bajo).</summary>
    private static PriceListWarningDto AboveGeneralWarning(Guid productPublicId, string sku, decimal price, decimal general)
        => new(PriceNoticeCodes.SpecialAboveGeneral, PriceListRules.SpecialAboveGeneral(sku, price, general), productPublicId, sku, price, general);

    /// <summary>
    /// Lote 247: el precio de la lista GENERAL (publicada, activa y vigente en la fecha) de cada (fecha, producto); el producto que no está en ninguna general no sale.
    /// <paramref name="excludeListId"/> = no contar esa lista (la general que se está mirando).
    /// </summary>
    private async Task<Dictionary<(DateOnly Date, int ProductId), decimal>> GeneralPricesAsync(IEnumerable<(DateOnly Date, int ProductId)> wanted, int? excludeListId,
        CancellationToken ct)
    {
        var result = new Dictionary<(DateOnly, int), decimal>();
        foreach (var byDate in wanted.Distinct().GroupBy(w => w.Date))
        {
            var productIds = byDate.Select(w => w.ProductId).ToList();
            var lists = (await PriceListQueries.InEffectAsync(db, null, byDate.Key, productIds, ct)).Where(l => l.PriceListId != excludeListId).ToList();
            if (lists.Count == 0) continue;
            foreach (var pid in productIds)
            {
                var r = PriceListRules.Resolve(pid, null, byDate.Key, lists, null);
                if (r.Source == PriceSources.GeneralList && r.Price is decimal price) result[(byDate.Key, pid)] = price;
            }
        }
        return result;
    }

    /// <summary>Lote 247: las cadenas de una lista, por nombre, con sus clientes ACTIVOS.</summary>
    private async Task<IReadOnlyList<PriceListChainDto>> ChainDtosAsync(IEnumerable<int> chainIds, CancellationToken ct)
    {
        var ids = chainIds.Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<PriceListChainDto>();
        var refs = await ClientChainQueries.ChainsByIdAsync(db, ids, ct);
        var counts = (await db.Clients.AsNoTracking().Where(c => c.IsActive && c.ChainId != null && ids.Contains(c.ChainId.Value)).Select(c => c.ChainId!.Value).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        return refs.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new PriceListChainDto(c.PublicId, c.Name, c.IsActive, counts.GetValueOrDefault(c.Id))).ToList();
    }
}
