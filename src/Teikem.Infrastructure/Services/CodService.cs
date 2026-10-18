using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Cod;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 84 (P8) — ciclo COD: el cobro contra entrega y su reconciliación (PWBack).
/// - Cobro (`cod.collect`): uno o varios por orden con COD (parciales); no pasa de lo que falta por cobrar. El COD de la orden pasa de POR COBRAR a PARCIAL o COBRADO (StatusService, historial ORDER_COD).
/// - Reconciliación (`cod.reconcile`): se recibe una lista de CÓDIGOS (lo que dice el código de barras de la hoja escaneada —el número de la factura del cliente o el de la orden— o lo escrito a mano) y
///   cada uno cuadra lo cobrado de su orden: los cobros pasan a CUADRADO y, si la orden ya estaba cobrada del todo, su COD pasa a CUADRADO. Cada código da un resultado tipado (cuadrado, ya cuadrado, no
///   encontrado, ambiguo, sin COD, nada cobrado, repetido); lo que no cuadra no detiene los demás. Con DryRun solo dice qué pasaría. Cada corrida queda guardada con sus códigos.
/// </summary>
public sealed class CodService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, ILookupCache lookups, ITenantClock clock)
{
    public const string Concurrency = "El cobro cambió mientras se guardaba. Revise y vuelva a intentar.";
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;

    // ================================================================ consultas

    private sealed record StatusInfo(string Code, string Label, string Color);

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(string domain, CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct))
            .ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode, MultilingualText.Resolve(s.LabelJson, T.Lang) ?? s.InternalCode, s.ColorHex ?? "#9CA3AF"));

    private async Task<(string Code, string Label)> LookupAsync(int id, CancellationToken ct)
    {
        var l = await lookups.GetAsync(id, ct);
        return (l?.InternalCode ?? string.Empty, MultilingualText.Resolve(l?.LabelJson, T.Lang) ?? l?.InternalCode ?? string.Empty);
    }

    /// <summary>Lo cobrado (sin anulados) de cada orden.</summary>
    private async Task<Dictionary<int, decimal>> CollectedAsync(IEnumerable<int> orderIds, CancellationToken ct)
    {
        var ids = orderIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, decimal>();
        var voidIds = (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == CodCodes.CollectionStatusDomain && s.InternalCode == CodCollectionStatuses.Void).Select(s => s.StatusCodeId).ToListAsync(ct));
        var rows = await db.Set<CodCollection>().AsNoTracking().Where(c => ids.Contains(c.TransportOrderId) && !voidIds.Contains(c.StatusCodeId)).Select(c => new { c.TransportOrderId, c.AmountCollected }).ToListAsync(ct);
        return rows.GroupBy(r => r.TransportOrderId).ToDictionary(g => g.Key, g => g.Sum(r => r.AmountCollected));
    }

    /// <summary>Órdenes con COD: lo pedido, lo cobrado, lo que falta y el estatus del COD.</summary>
    public async Task<PagedResult<CodOrderListItemDto>> OrdersAsync(string? codStatus, Guid? clientPublicId, string? q, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var map = await StatusMapAsync(StatusDomains.CodStatus, ct);
        var query = db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.CodStatusCodeId != null);
        if (!string.IsNullOrWhiteSpace(codStatus))
        {
            var ids = map.Where(kv => kv.Value.Code.Equals(codStatus.Trim(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            query = query.Where(o => ids.Contains(o.CodStatusCodeId!.Value));
        }
        if (clientPublicId is Guid cp)
        {
            var cid = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == cp).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct);
            query = cid is null ? query.Where(o => false) : query.Where(o => o.ClientId == cid);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            var clientIds = await db.Set<Client>().AsNoTracking().Where(c => c.Name.Contains(s)).Select(c => c.ClientId).ToListAsync(ct);
            query = query.Where(o => o.OrderNumber.Contains(s) || o.ClientInvoiceNumber.Contains(s) || clientIds.Contains(o.ClientId));
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.TransportOrderId).Skip(skip).Take(take).ToListAsync(ct);
        var collected = await CollectedAsync(page.Select(o => o.TransportOrderId), ct);
        var clientIdsPage = page.Select(o => o.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIdsPage.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => new { c.PublicId, c.Name }, ct);
        var orderStatuses = await StatusMapAsync(StatusDomains.OrderStatus, ct);
        var items = page.Select(o =>
        {
            var cod = o.CodAmount ?? 0m;
            var got = collected.GetValueOrDefault(o.TransportOrderId);
            var st = map.GetValueOrDefault(o.CodStatusCodeId!.Value) ?? new StatusInfo("", "", "#9CA3AF");
            return new CodOrderListItemDto(o.PublicId, o.OrderNumber, NullIfEmpty(o.ClientInvoiceNumber), clients.GetValueOrDefault(o.ClientId)?.PublicId ?? Guid.Empty, clients.GetValueOrDefault(o.ClientId)?.Name ?? string.Empty,
                orderStatuses.GetValueOrDefault(o.StatusCodeId)?.Label ?? string.Empty, cod, got, CodRules.Pending(cod, got), st.Code, st.Label, st.Color, o.CreatedAtUtc);
        }).ToList();
        return new PagedResult<CodOrderListItemDto>(items, total, skip, take);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public async Task<CodOrderDto> GetOrderAsync(Guid orderPublicId, CancellationToken ct)
    {
        var order = await db.TransportOrders.AsNoTracking().FirstOrDefaultAsync(o => o.PublicId == orderPublicId, ct) ?? throw new NotFoundException("Orden", feminine: true);
        return await ToOrderDtoAsync(order, ct);
    }

    private async Task<CodOrderDto> ToOrderDtoAsync(TransportOrder order, CancellationToken ct)
    {
        if (order.CodStatusCodeId is null) throw new StatusRuleException(CodRules.OrderHasNoCod);
        var codMap = await StatusMapAsync(StatusDomains.CodStatus, ct);
        var colMap = await StatusMapAsync(CodCodes.CollectionStatusDomain, ct);
        var orderStatus = (await StatusMapAsync(StatusDomains.OrderStatus, ct)).GetValueOrDefault(order.StatusCodeId);
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == order.ClientId, ct);
        var rows = await db.Set<CodCollection>().AsNoTracking().Where(c => c.TransportOrderId == order.TransportOrderId).OrderBy(c => c.CodCollectionId).ToListAsync(ct);
        var list = new List<CodCollectionDto>();
        foreach (var c in rows)
        {
            var method = await LookupAsync(c.MethodLookupId, ct);
            var st = colMap.GetValueOrDefault(c.StatusCodeId) ?? new StatusInfo("", "", "#9CA3AF");
            list.Add(new CodCollectionDto(c.CodCollectionId, method.Code, method.Label, c.AmountCollected, c.CheckNumber, c.Reference, c.CollectedAtUtc, st.Code, st.Label, st.Color, c.ReconciledAtUtc, c.Notes));
        }
        var cod = order.CodAmount ?? 0m;
        var got = list.Where(c => c.Status != CodCollectionStatuses.Void).Sum(c => c.Amount);
        var cs = codMap.GetValueOrDefault(order.CodStatusCodeId.Value) ?? new StatusInfo("", "", "#9CA3AF");
        return new CodOrderDto(order.PublicId, order.OrderNumber, NullIfEmpty(order.ClientInvoiceNumber), client.PublicId, client.Name, orderStatus?.Code ?? string.Empty, orderStatus?.Label ?? string.Empty, cod, got,
            CodRules.Pending(cod, got), cs.Code, cs.Label, cs.Color, list);
    }

    private static readonly string[] CodPipeline = { CodStatuses.Pending, CodStatuses.Partial, CodStatuses.Collected, CodStatuses.Reconciled, CodStatuses.Remitted };

    /// <summary>Avanza el COD de la orden hasta `target` paso a paso (el pipeline no permite saltos: cobrar todo de una vez pasa por PARCIAL).</summary>
    private async Task AdvanceCodAsync(TransportOrder order, string current, string target, CancellationToken ct)
    {
        var from = Array.IndexOf(CodPipeline, current);
        var to = Array.IndexOf(CodPipeline, target);
        for (var i = from + 1; i <= to; i++)
        {
            var st = await statuses.TransitionAsync(StatusDomains.CodStatus, EntityTypes.OrderCod, order.TransportOrderId, order.CodStatusCodeId, CodPipeline[i], null, ct);
            order.CodStatusCodeId = st.StatusCodeId;
        }
    }

    // ================================================================ cobrar

    /// <summary>Registra un cobro (parcial o total) de la orden y mueve el COD de la orden a PARCIAL o COBRADO.</summary>
    public async Task<CodOrderDto> CollectAsync(Guid orderPublicId, CodCollectionRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var methodCode = req.Method?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(methodCode)) throw new ValidationException("method", CodRules.MethodRequired);
        if (!CodMethods.All.Contains(methodCode)) throw new ValidationException("method", CodRules.MethodInvalid);
        if (req.Amount is not decimal amount || !CodRules.IsValidAmount(amount)) throw new ValidationException("amount", CodRules.AmountInvalid);
        var check = req.CheckNumber?.Trim();
        if (CodMethods.NeedsCheckNumber(methodCode) && string.IsNullOrEmpty(check)) throw new ValidationException("checkNumber", CodRules.CheckNumberRequired);
        if (check is { Length: > CodRules.MaxCheckNumberLength }) throw new ValidationException("checkNumber", CodRules.CheckNumberTooLong);
        var reference = req.Reference?.Trim();
        if (reference is { Length: > CodRules.MaxReferenceLength }) throw new ValidationException("reference", CodRules.ReferenceTooLong);
        var notes = req.Notes?.Trim();
        if (notes is { Length: > CodRules.MaxNoteLength }) throw new ValidationException("notes", CodRules.NoteTooLong);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.TransportOrders.AsTracking().FirstOrDefaultAsync(o => o.PublicId == orderPublicId, ct2) ?? throw new NotFoundException("Orden", feminine: true);
            if (order.CodStatusCodeId is null) throw new StatusRuleException(CodRules.OrderHasNoCod);
            var orderStatus = (await StatusMapAsync(StatusDomains.OrderStatus, ct2))[order.StatusCodeId].Code;
            if (orderStatus is OrderStatuses.Submitted or OrderStatuses.Cancelled) throw new StatusRuleException(CodRules.OrderNotCollectable);

            var collected = (await CollectedAsync(new[] { order.TransportOrderId }, ct2)).GetValueOrDefault(order.TransportOrderId);
            var pending = CodRules.Pending(order.CodAmount ?? 0m, collected);
            if (amount > pending) throw new StatusRuleException(CodRules.ExceedsPending(order.OrderNumber, pending));

            var methodId = await lookups.GetIdAsync(CodCodes.MethodDomain, methodCode, ct2);
            var collectedStatus = await db.StatusCodes.AsNoTracking().FirstAsync(s => s.Entity == CodCodes.CollectionStatusDomain && s.InternalCode == CodCollectionStatuses.Collected, ct2);
            db.Set<CodCollection>().Add(new CodCollection
            {
                TenantId = T.RequireTenantId(), TransportOrderId = order.TransportOrderId, MethodLookupId = methodId, AmountCollected = amount, CheckNumber = check, Reference = reference, Notes = notes,
                CollectedAtUtc = req.CollectedAtUtc ?? clock.UtcNow, StatusCodeId = collectedStatus.StatusCodeId, CollectedBy = tenant.UserId,
            });

            var target = CodRules.OrderStatusFor(order.CodAmount ?? 0m, collected + amount);
            var current = (await StatusMapAsync(StatusDomains.CodStatus, ct2))[order.CodStatusCodeId.Value].Code;
            await AdvanceCodAsync(order, current, target, ct2);
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetOrderAsync(orderPublicId, ct);
    }

    // ================================================================ reconciliar

    public static string MessageFor(string result, decimal amount, int collections) => result switch
    {
        CodReconcileResults.Reconciled => $"Cuadrado: {collections} cobro(s) por {amount:0.00}.",
        CodReconcileResults.AlreadyReconciled => "Ya estaba cuadrado.",
        CodReconcileResults.NotFound => "No hay ninguna orden con ese número de factura ni de orden.",
        CodReconcileResults.Ambiguous => "El código coincide con más de una orden: cuádrela a mano desde la orden.",
        CodReconcileResults.NoCod => CodRules.OrderHasNoCod,
        CodReconcileResults.NothingCollected => "La orden tiene COD, pero todavía no se registró ningún cobro.",
        CodReconcileResults.Duplicate => "El código ya venía antes en esta corrida.",
        _ => string.Empty,
    };

    /// <summary>
    /// La orden activa a la que apunta el código: por número de orden y, si no, por la factura del cliente (que se repite entre clientes: FAC-##### se cuenta por cliente). Si hay varias se queda con las que
    /// tienen COD y, si todavía hay varias, con las que tienen algo cobrado por cuadrar; si aun así no es una sola, es ambigua (se cuadra a mano o indicando el cliente).
    /// </summary>
    private async Task<(List<TransportOrder> Orders, bool Ambiguous)> ResolveAsync(string code, int? clientId, int collectedStatusId, CancellationToken ct)
    {
        IQueryable<TransportOrder> Scope() => db.TransportOrders.AsNoTracking().Where(o => o.IsActive && (clientId == null || o.ClientId == clientId));
        var found = await Scope().Where(o => o.OrderNumber == code).ToListAsync(ct);
        found = found.Where(o => string.Equals(o.OrderNumber, code, StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0)
        {
            found = await Scope().Where(o => o.ClientInvoiceNumber == code).ToListAsync(ct);
            found = found.Where(o => string.Equals(o.ClientInvoiceNumber, code, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (found.Count > 1)
        {
            var withCod = found.Where(o => o.CodStatusCodeId != null).ToList();
            if (withCod.Count >= 1) found = withCod;
        }
        if (found.Count > 1)
        {
            var ids = found.Select(o => o.TransportOrderId).ToList();
            var open = await db.Set<CodCollection>().AsNoTracking().Where(c => ids.Contains(c.TransportOrderId) && c.StatusCodeId == collectedStatusId).Select(c => c.TransportOrderId).Distinct().ToListAsync(ct);
            if (open.Count >= 1) found = found.Where(o => open.Contains(o.TransportOrderId)).ToList();
        }
        return (found, found.Count > 1);
    }

    /// <summary>Cuadra (o, con DryRun, simula cuadrar) lo cobrado de las órdenes a las que apuntan los códigos.</summary>
    public async Task<CodReconcileResultDto> ReconcileAsync(CodReconcileRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Codes is null || req.Codes.Count == 0) throw new ValidationException("codes", CodRules.CodesRequired);
        if (req.Codes.Count > CodRules.MaxCodesPerRun) throw new ValidationException("codes", CodRules.TooManyCodes);
        var runSource = req.Source?.Trim();
        if (runSource is { Length: > CodRules.MaxSourceLength }) throw new ValidationException("source", CodRules.SourceTooLong);
        var dry = req.DryRun == true;
        int? scopeClientId = null;
        if (req.ClientPublicId is Guid scopeClient)
            scopeClientId = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == scopeClient).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct) ?? throw new ValidationException("clientPublicId", CodRules.ClientInvalid);
        var inputs = new List<(string Code, string? Source)>();
        for (var i = 0; i < req.Codes.Count; i++)
        {
            var code = CodRules.NormalizeCode(req.Codes[i].Code);
            if (code.Length == 0) throw new ValidationException($"codes[{i}]", CodRules.CodesRequired);
            if (code.Length > CodRules.MaxCodeLength) throw new ValidationException($"codes[{i}]", CodRules.CodeTooLong);
            var src = req.Codes[i].Source?.Trim();
            if (src is { Length: > CodRules.MaxSourceLength }) throw new ValidationException($"codes[{i}]", CodRules.SourceTooLong);
            inputs.Add((code, src));
        }

        var items = new List<CodReconcileItemDto>();
        Guid? runPublicId = null;
        async Task RunAsync(CancellationToken ct2)
        {
            items.Clear();
            var codMap = await StatusMapAsync(StatusDomains.CodStatus, ct2);
            var colMap = await StatusMapAsync(CodCodes.CollectionStatusDomain, ct2);
            var collectedId = colMap.First(kv => kv.Value.Code == CodCollectionStatuses.Collected).Key;
            var reconciledId = colMap.First(kv => kv.Value.Code == CodCollectionStatuses.Reconciled).Key;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CodReconcileRun? run = null;
            if (!dry)
            {
                run = new CodReconcileRun { PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), Source = runSource, CreatedAtUtc = clock.UtcNow, CreatedBy = tenant.UserId };
                db.Set<CodReconcileRun>().Add(run);
                await db.SaveGuardedAsync(Concurrency, ct2);   // para tener su id al enlazar cada cobro cuadrado
                runPublicId = run.PublicId;
            }
            var clients = new Dictionary<int, string>();

            foreach (var (code, source) in inputs)
            {
                string result;
                TransportOrder? order = null;
                decimal amount = 0m;
                var count = 0;
                if (!seen.Add(code)) result = CodReconcileResults.Duplicate;
                else
                {
                    var (orders, ambiguous) = await ResolveAsync(code, scopeClientId, collectedId, ct2);
                    if (orders.Count == 0) result = CodReconcileResults.NotFound;
                    else if (ambiguous) result = CodReconcileResults.Ambiguous;
                    else
                    {
                        order = dry ? orders[0] : await db.TransportOrders.AsTracking().FirstAsync(o => o.TransportOrderId == orders[0].TransportOrderId, ct2);
                        if (order.CodStatusCodeId is null) result = CodReconcileResults.NoCod;
                        else
                        {
                            var rows = await (dry ? db.Set<CodCollection>().AsNoTracking() : db.Set<CodCollection>().AsTracking()).Where(c => c.TransportOrderId == order.TransportOrderId).ToListAsync(ct2);
                            var open = rows.Where(c => c.StatusCodeId == collectedId).ToList();
                            if (open.Count == 0) result = rows.Any(c => colMap.GetValueOrDefault(c.StatusCodeId)?.Code is CodCollectionStatuses.Reconciled or CodCollectionStatuses.Remitted)
                                ? CodReconcileResults.AlreadyReconciled : CodReconcileResults.NothingCollected;
                            else
                            {
                                result = CodReconcileResults.Reconciled;
                                amount = open.Sum(c => c.AmountCollected);
                                count = open.Count;
                                if (!dry)
                                {
                                    foreach (var c in open)
                                    {
                                        c.StatusCodeId = reconciledId;
                                        c.ReconciledAtUtc = clock.UtcNow;
                                        c.ReconciledBy = tenant.UserId;
                                        c.ReconcileRunId = run!.CodReconcileRunId;
                                    }
                                    // el COD de la orden pasa a CUADRADO solo si ya estaba cobrado del todo
                                    if (codMap[order.CodStatusCodeId.Value].Code == CodStatuses.Collected)
                                    {
                                        var to = await statuses.TransitionAsync(StatusDomains.CodStatus, EntityTypes.OrderCod, order.TransportOrderId, order.CodStatusCodeId, CodStatuses.Reconciled, null, ct2);
                                        order.CodStatusCodeId = to.StatusCodeId;
                                    }
                                }
                            }
                        }
                    }
                }
                string? clientName = null;
                if (order is not null)
                {
                    if (!clients.TryGetValue(order.ClientId, out clientName))
                        clients[order.ClientId] = clientName = (await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == order.ClientId, ct2)).Name;
                }
                items.Add(new CodReconcileItemDto(code, source, result, MessageFor(result, amount, count), order?.PublicId, order?.OrderNumber, order is null ? null : NullIfEmpty(order.ClientInvoiceNumber), clientName,
                    order?.CodAmount ?? 0m, amount, count));
                run?.Items.Add(new CodReconcileItem { Code = code, Source = source, Result = result, TransportOrderId = order?.TransportOrderId, Amount = amount, Collections = count });
            }
            if (run is not null)
            {
                run.Total = items.Count;
                run.Reconciled = items.Count(i => i.Result == CodReconcileResults.Reconciled);
                run.Issues = items.Count - run.Reconciled - items.Count(i => i.Result == CodReconcileResults.AlreadyReconciled);
                await db.SaveGuardedAsync(Concurrency, ct2);
            }
        }

        if (dry) await RunAsync(ct);
        else await db.RunInTransactionAsync(RunAsync, ct);

        var reconciled = items.Count(i => i.Result == CodReconcileResults.Reconciled);
        var issues = items.Count - reconciled - items.Count(i => i.Result == CodReconcileResults.AlreadyReconciled);
        return new CodReconcileResultDto(runPublicId, dry, items.Count, reconciled, issues, items);
    }

    // ================================================================ corridas

    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email!, ct);
    }

    public async Task<PagedResult<CodRunListItemDto>> RunsAsync(int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var query = db.Set<CodReconcileRun>().AsNoTracking();
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.CodReconcileRunId).Skip(skip).Take(take).ToListAsync(ct);
        var names = await UserNamesAsync(page.Select(r => r.CreatedBy), ct);
        return new PagedResult<CodRunListItemDto>(page.Select(r => new CodRunListItemDto(r.PublicId, r.Source, r.Total, r.Reconciled, r.Issues, r.CreatedAtUtc, r.CreatedBy is int u ? names.GetValueOrDefault(u) : null)).ToList(),
            total, skip, take);
    }

    public async Task<CodRunDto> GetRunAsync(Guid publicId, CancellationToken ct)
    {
        var run = await db.Set<CodReconcileRun>().AsNoTracking().Include(r => r.Items).FirstOrDefaultAsync(r => r.PublicId == publicId, ct) ?? throw new NotFoundException("Corrida", feminine: true);
        var names = await UserNamesAsync(new[] { run.CreatedBy }, ct);
        var orderIds = run.Items.Where(i => i.TransportOrderId != null).Select(i => i.TransportOrderId!.Value).Distinct().ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId)).ToDictionaryAsync(o => o.TransportOrderId, ct);
        var clientIds = orders.Values.Select(o => o.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var items = run.Items.OrderBy(i => i.CodReconcileItemId).Select(i =>
        {
            var o = i.TransportOrderId is int id ? orders.GetValueOrDefault(id) : null;
            return new CodReconcileItemDto(i.Code, i.Source, i.Result, MessageFor(i.Result, i.Amount, i.Collections), o?.PublicId, o?.OrderNumber, o is null ? null : NullIfEmpty(o.ClientInvoiceNumber),
                o is null ? null : clients.GetValueOrDefault(o.ClientId), o?.CodAmount ?? 0m, i.Amount, i.Collections);
        }).ToList();
        return new CodRunDto(run.PublicId, run.Source, run.Total, run.Reconciled, run.Issues, run.CreatedAtUtc, run.CreatedBy is int u ? names.GetValueOrDefault(u) : null, items);
    }
}
