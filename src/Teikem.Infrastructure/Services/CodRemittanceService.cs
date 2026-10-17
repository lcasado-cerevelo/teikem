using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Cod;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Export;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 86 (P8) — remesa COD al cliente (REM-#####). ABIERTA → CUADRADA → APROBADA (AAL2 en el controlador) → REMITIDA; una abierta o cuadrada se cancela (se desactiva y libera sus cobros).
/// - Crear agrupa los cobros CUADRADOS (reconciliados) del cliente cuya fecha de cobro cae en el período (en la zona de la compañía) y que todavía no están en otra remesa; la comisión es la pedida, si no la
///   del contrato activo del cliente (Contract.CodCommissionPct) y, si no, la de la compañía (Tenant.CodDefaultCommissionPct, 8 % por omisión). Comisión por línea = monto × % redondeado a 2 decimales; neto = bruto − comisión.
/// - Remitir pasa los cobros a REMITIDO y, si todos los cobros de una orden ya se remitieron, el COD de la orden a REMITIDO (terminal). La plataforma calcula y exporta; no emite el pago.
/// - Exportar arma el archivo con una plantilla del alcance COD_REMITTANCE (una fila por cobro) de una remesa aprobada o remitida.
/// </summary>
public sealed class CodRemittanceService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService numbers, ILookupCache lookups, ITenantClock clock, ExportTemplateService templates)
{
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;

    // ================================================================ ajustes

    public async Task<CodSettingsDto> GetSettingsAsync(CancellationToken ct)
        => new((await db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == T.RequireTenantId(), ct)).CodDefaultCommissionPct);

    public async Task<CodSettingsDto> UpdateSettingsAsync(CodSettingsRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var t = await db.Tenants.AsTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        if (req.DefaultCommissionPct is decimal pct)
        {
            if (!RemittanceRules.IsValidPct(pct)) throw new ValidationException("defaultCommissionPct", RemittanceRules.SettingsPctInvalid);
            t.CodDefaultCommissionPct = pct;
        }
        await db.SaveGuardedAsync(RemittanceRules.Concurrency, ct);
        return new CodSettingsDto(t.CodDefaultCommissionPct);
    }

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

    public async Task<PagedResult<CodRemittanceListItemDto>> ListAsync(string? status, Guid? clientPublicId, string? q, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var map = await StatusMapAsync(RemittanceCodes.StatusDomain, ct);
        var query = db.Set<CodRemittanceBatch>().AsNoTracking().Where(r => r.IsActive);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var ids = map.Where(kv => kv.Value.Code.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            query = query.Where(r => ids.Contains(r.StatusCodeId));
        }
        if (clientPublicId is Guid cp)
        {
            var cid = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == cp).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct);
            query = cid is null ? query.Where(r => false) : query.Where(r => r.ClientId == cid);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            var clientIds = await db.Set<Client>().AsNoTracking().Where(c => c.Name.Contains(s)).Select(c => c.ClientId).ToListAsync(ct);
            query = query.Where(r => r.Number.Contains(s) || clientIds.Contains(r.ClientId));
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(r => r.GeneratedAtUtc).ThenByDescending(r => r.CodRemittanceBatchId).Skip(skip).Take(take).ToListAsync(ct);
        var ids2 = page.Select(r => r.CodRemittanceBatchId).ToList();
        var counts = (await db.Set<CodRemittanceLine>().AsNoTracking().Where(l => ids2.Contains(l.CodRemittanceBatchId)).GroupBy(l => l.CodRemittanceBatchId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.N);
        var cids = page.Select(r => r.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => cids.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => new { c.PublicId, c.Name }, ct);
        var items = page.Select(r =>
        {
            var st = map.GetValueOrDefault(r.StatusCodeId) ?? new StatusInfo("", "", "#9CA3AF");
            var c = clients.GetValueOrDefault(r.ClientId);
            return new CodRemittanceListItemDto(r.PublicId, r.Number, st.Code, st.Label, st.Color, c?.PublicId ?? Guid.Empty, c?.Name ?? string.Empty, r.PeriodStart, r.PeriodEnd, r.GrossCollected, r.CommissionPct,
                r.CommissionAmount, r.GrossCollected - r.CommissionAmount, counts.GetValueOrDefault(r.CodRemittanceBatchId), r.GeneratedAtUtc, r.RemittedAtUtc);
        }).ToList();
        return new PagedResult<CodRemittanceListItemDto>(items, total, skip, take);
    }

    private async Task<IReadOnlyList<CodRemittanceLineDto>> LineDtosAsync(IReadOnlyList<(long LineId, CodCollection C, decimal Commission)> rows, CancellationToken ct)
    {
        var orderIds = rows.Select(r => r.C.TransportOrderId).Distinct().ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId)).ToDictionaryAsync(o => o.TransportOrderId, ct);
        var list = new List<CodRemittanceLineDto>();
        foreach (var (lineId, c, commission) in rows)
        {
            var o = orders[c.TransportOrderId];
            var method = await LookupAsync(c.MethodLookupId, ct);
            list.Add(new CodRemittanceLineDto(lineId, c.CodCollectionId, o.PublicId, o.OrderNumber, string.IsNullOrWhiteSpace(o.ClientInvoiceNumber) ? null : o.ClientInvoiceNumber, c.CollectedAtUtc, method.Code, method.Label,
                c.CheckNumber, c.Reference, c.AmountCollected, commission, c.AmountCollected - commission));
        }
        return list;
    }

    public async Task<CodRemittanceDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var r = await db.Set<CodRemittanceBatch>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException("Remesa", feminine: true);
        return await ToDtoAsync(r, ct);
    }

    private async Task<CodRemittanceDto> ToDtoAsync(CodRemittanceBatch r, CancellationToken ct)
    {
        var st = (await StatusMapAsync(RemittanceCodes.StatusDomain, ct)).GetValueOrDefault(r.StatusCodeId) ?? new StatusInfo("", "", "#9CA3AF");
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == r.ClientId, ct);
        var ids = r.Lines.Select(l => l.CodCollectionId).ToList();
        var collections = await db.Set<CodCollection>().AsNoTracking().Where(c => ids.Contains(c.CodCollectionId)).ToDictionaryAsync(c => c.CodCollectionId, ct);
        var rows = r.Lines.OrderBy(l => l.CodRemittanceLineId).Select(l => (l.CodRemittanceLineId, collections[l.CodCollectionId], l.CommissionAmount)).ToList();
        var lines = await LineDtosAsync(rows, ct);
        string? approver = null;
        if (r.ApprovedBy is int u) approver = await db.Users.AsNoTracking().Where(x => x.Id == u).Select(x => x.FullName ?? x.Email).FirstOrDefaultAsync(ct);
        var currency = await LookupAsync(r.CurrencyLookupId, ct);
        return new CodRemittanceDto(r.PublicId, r.Number, st.Code, st.Label, st.Color, client.PublicId, client.Name, r.PeriodStart, r.PeriodEnd, r.GrossCollected, r.CommissionPct, r.CommissionAmount,
            r.GrossCollected - r.CommissionAmount, currency.Code, r.GeneratedAtUtc, r.ApprovedAtUtc, approver, r.RemittedAtUtc, r.ExportRef, lines);
    }

    // ================================================================ crear

    private async Task<Client> ClientAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is null) throw new ValidationException("clientPublicId", RemittanceRules.ClientRequired);
        return await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct) ?? throw new ValidationException("clientPublicId", RemittanceRules.ClientInvalid);
    }

    private static void ValidatePeriod(DateOnly? start, DateOnly? end)
    {
        if (start is null || end is null) throw new ValidationException("periodStart", RemittanceRules.PeriodRequired);
        if (end < start) throw new ValidationException("periodEnd", RemittanceRules.PeriodInvalid);
    }

    /// <summary>La comisión que aplica y de dónde sale (REQUEST, CONTRACT o TENANT).</summary>
    private async Task<(decimal Pct, string Source, int? CurrencyId)> CommissionAsync(int clientId, decimal? requested, CancellationToken ct)
    {
        if (requested is decimal r && !RemittanceRules.IsValidPct(r)) throw new ValidationException("commissionPct", RemittanceRules.PctInvalid);
        var activeId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.ContractStatus && s.InternalCode == ContractStatuses.Active).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        var today = clock.Today;
        var contract = activeId is null ? null : await db.Set<Contract>().AsNoTracking().Where(c => c.ClientId == clientId && c.IsActive && c.StatusCodeId == activeId && c.StartDate <= today && (c.EndDate == null || c.EndDate >= today))
            .OrderByDescending(c => c.StartDate).FirstOrDefaultAsync(ct);
        var tenantPct = (await db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == T.RequireTenantId(), ct)).CodDefaultCommissionPct;
        var pct = RemittanceRules.ResolvePct(requested, contract?.CodCommissionPct, tenantPct);
        return (pct, requested is not null ? "REQUEST" : contract?.CodCommissionPct is not null ? "CONTRACT" : "TENANT", contract?.CurrencyLookupId);
    }

    private async Task<List<CodCollection>> EligibleAsync(int clientId, DateOnly start, DateOnly end, bool tracking, CancellationToken ct)
    {
        var reconciledId = (await db.StatusCodes.AsNoTracking().FirstAsync(s => s.Entity == CodCodes.CollectionStatusDomain && s.InternalCode == CodCollectionStatuses.Reconciled, ct)).StatusCodeId;
        var (from, toExclusive) = clock.UtcRange(start, end);
        var orderIds = db.TransportOrders.AsNoTracking().Where(o => o.ClientId == clientId).Select(o => o.TransportOrderId);
        var query = (tracking ? db.Set<CodCollection>().AsTracking() : db.Set<CodCollection>().AsNoTracking())
            .Where(c => c.StatusCodeId == reconciledId && c.CodRemittanceBatchId == null && orderIds.Contains(c.TransportOrderId));
        if (from is DateTime f) query = query.Where(c => c.CollectedAtUtc >= f);
        if (toExclusive is DateTime t) query = query.Where(c => c.CollectedAtUtc < t);
        return await query.OrderBy(c => c.CollectedAtUtc).ThenBy(c => c.CodCollectionId).ToListAsync(ct);
    }

    /// <summary>Lo que entraría en la remesa (los cobros cuadrados del cliente en el período) y la comisión que se aplicaría, sin crear nada.</summary>
    public async Task<CodRemittanceCandidatesDto> CandidatesAsync(Guid? clientPublicId, DateOnly? start, DateOnly? end, decimal? commissionPct, CancellationToken ct)
    {
        var client = await ClientAsync(clientPublicId, ct);
        ValidatePeriod(start, end);
        var (pct, source, _) = await CommissionAsync(client.ClientId, commissionPct, ct);
        var rows = await EligibleAsync(client.ClientId, start!.Value, end!.Value, false, ct);
        var lines = await LineDtosAsync(rows.Select(c => (0L, c, RemittanceRules.Commission(c.AmountCollected, pct))).ToList(), ct);
        var gross = rows.Sum(c => c.AmountCollected);
        var commission = lines.Sum(l => l.CommissionAmount);
        return new CodRemittanceCandidatesDto(client.PublicId, client.Name, start.Value, end.Value, pct, source, gross, commission, gross - commission, lines);
    }

    public async Task<CodRemittanceDto> CreateAsync(CodRemittanceRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var client = await ClientAsync(req.ClientPublicId, ct);
        ValidatePeriod(req.PeriodStart, req.PeriodEnd);
        Guid publicId = Guid.Empty;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var (pct, _, contractCurrency) = await CommissionAsync(client.ClientId, req.CommissionPct, ct2);
            var rows = await EligibleAsync(client.ClientId, req.PeriodStart!.Value, req.PeriodEnd!.Value, true, ct2);
            if (rows.Count == 0) throw new StatusRuleException(RemittanceRules.NothingToRemit);
            var initial = (await StatusMapAsync(RemittanceCodes.StatusDomain, ct2)).First(kv => kv.Value.Code == RemittanceStatuses.Open).Key;
            var tenantRow = await db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == T.RequireTenantId(), ct2);
            var currencyId = contractCurrency ?? await lookups.GetIdAsync(LookupDomains.Currency, tenantRow.CurrencyCode, ct2);
            await numbers.EnsureAsync(RemittanceCodes.NumberKind, null, ct2);
            var seq = await numbers.NextAsync(RemittanceCodes.NumberKind, null, ct2);
            var batch = new CodRemittanceBatch
            {
                PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), ClientId = client.ClientId, Number = NumberFormat.Resolve(RemittanceCodes.NumberPattern, seq), PeriodStart = req.PeriodStart!.Value,
                PeriodEnd = req.PeriodEnd!.Value, StatusCodeId = initial, CommissionPct = pct, CurrencyLookupId = currencyId, GeneratedAtUtc = clock.UtcNow,
            };
            foreach (var c in rows)
                batch.Lines.Add(new CodRemittanceLine { CodCollectionId = c.CodCollectionId, TransportOrderId = c.TransportOrderId, AmountCollected = c.AmountCollected, CommissionAmount = RemittanceRules.Commission(c.AmountCollected, pct) });
            batch.GrossCollected = batch.Lines.Sum(l => l.AmountCollected);
            batch.CommissionAmount = batch.Lines.Sum(l => l.CommissionAmount);
            db.Set<CodRemittanceBatch>().Add(batch);
            await db.SaveGuardedAsync(RemittanceRules.Concurrency, ct2);
            await statuses.TransitionAsync(RemittanceCodes.StatusDomain, RemittanceCodes.EntityType, batch.CodRemittanceBatchId, null, RemittanceStatuses.Open, null, ct2);
            // los servicios anidados pueden soltar lo rastreado (en las pruebas con base en memoria): los cobros se vuelven a cargar antes de enlazarlos a la remesa
            var collectionIds = rows.Select(c => c.CodCollectionId).ToList();
            foreach (var c in await db.Set<CodCollection>().AsTracking().Where(c => collectionIds.Contains(c.CodCollectionId)).ToListAsync(ct2)) c.CodRemittanceBatchId = batch.CodRemittanceBatchId;
            await db.SaveGuardedAsync(RemittanceRules.Concurrency, ct2);
            publicId = batch.PublicId;
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ estatus

    /// <summary>
    /// Mueve la remesa de un estatus a otro. La transición va PRIMERO y la remesa se vuelve a cargar después: StatusService puede soltar lo rastreado (en las pruebas con base en memoria), y lo que
    /// se cambia antes de una transición se perdería. `effect` solo cambia campos (no hace transiciones).
    /// </summary>
    private async Task<CodRemittanceDto> MoveAsync(Guid publicId, string from, string to, string wrongMessage, Func<CodRemittanceBatch, CancellationToken, Task>? effect, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var head = await db.Set<CodRemittanceBatch>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct2) ?? throw new NotFoundException("Remesa", feminine: true);
            var map = await StatusMapAsync(RemittanceCodes.StatusDomain, ct2);
            if (map[head.StatusCodeId].Code != from) throw new StatusRuleException(wrongMessage);
            var st = await statuses.TransitionAsync(RemittanceCodes.StatusDomain, RemittanceCodes.EntityType, head.CodRemittanceBatchId, head.StatusCodeId, to, null, ct2);
            var r = await db.Set<CodRemittanceBatch>().AsTracking().FirstAsync(x => x.CodRemittanceBatchId == head.CodRemittanceBatchId, ct2);
            r.StatusCodeId = st.StatusCodeId;
            if (effect is not null) await effect(r, ct2);
            await db.SaveGuardedAsync(RemittanceRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>ABIERTA → CUADRADA: se revisó el cuadre de lo cobrado.</summary>
    public Task<CodRemittanceDto> ReviewAsync(Guid publicId, CancellationToken ct)
        => MoveAsync(publicId, RemittanceStatuses.Open, RemittanceStatuses.Reconciled, RemittanceRules.ReviewNeedsOpen, null, ct);

    /// <summary>CUADRADA → APROBADA (quien aprueba y cuándo).</summary>
    public Task<CodRemittanceDto> ApproveAsync(Guid publicId, CancellationToken ct)
        => MoveAsync(publicId, RemittanceStatuses.Reconciled, RemittanceStatuses.Approved, RemittanceRules.ApproveNeedsReconciled, (r, _) =>
        {
            (r.ApprovedBy, r.ApprovedAtUtc) = (tenant.UserId, clock.UtcNow);
            return Task.CompletedTask;
        }, ct);

    /// <summary>APROBADA → REMITIDA: los cobros pasan a REMITIDO y, si todos los de una orden ya se remitieron, el COD de la orden a REMITIDO.</summary>
    public async Task<CodRemittanceDto> RemitAsync(Guid publicId, CodRemitRequest? req, CancellationToken ct)
    {
        var exportRef = req?.ExportRef?.Trim();
        if (exportRef is { Length: > RemittanceRules.MaxExportRefLength }) throw new ValidationException("exportRef", RemittanceRules.ExportRefTooLong);
        await db.RunInTransactionAsync(async ct2 =>
        {
            await MoveAsync(publicId, RemittanceStatuses.Approved, RemittanceStatuses.Remitted, RemittanceRules.RemitNeedsApproved, async (r, ct3) =>
            {
                (r.RemittedAtUtc, r.ExportRef) = (clock.UtcNow, string.IsNullOrEmpty(exportRef) ? r.ExportRef : exportRef);
                var remittedId = (await db.StatusCodes.AsNoTracking().FirstAsync(s => s.Entity == CodCodes.CollectionStatusDomain && s.InternalCode == CodCollectionStatuses.Remitted, ct3)).StatusCodeId;
                foreach (var c in await db.Set<CodCollection>().AsTracking().Where(c => c.CodRemittanceBatchId == r.CodRemittanceBatchId).ToListAsync(ct3)) c.StatusCodeId = remittedId;
            }, ct2);
            await AdvanceOrdersAsync(publicId, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>El COD de cada orden de la remesa pasa a REMITIDO cuando está CUADRADO y ningún cobro suyo (sin anular) quedó sin remitir.</summary>
    private async Task AdvanceOrdersAsync(Guid publicId, CancellationToken ct)
    {
        var batchId = await db.Set<CodRemittanceBatch>().AsNoTracking().Where(r => r.PublicId == publicId).Select(r => r.CodRemittanceBatchId).FirstAsync(ct);
        var remittedId = (await db.StatusCodes.AsNoTracking().FirstAsync(s => s.Entity == CodCodes.CollectionStatusDomain && s.InternalCode == CodCollectionStatuses.Remitted, ct)).StatusCodeId;
        var voidId = (await db.StatusCodes.AsNoTracking().FirstAsync(s => s.Entity == CodCodes.CollectionStatusDomain && s.InternalCode == CodCollectionStatuses.Void, ct)).StatusCodeId;
        var codMap = await StatusMapAsync(StatusDomains.CodStatus, ct);
        var orderIds = await db.Set<CodCollection>().AsNoTracking().Where(c => c.CodRemittanceBatchId == batchId).Select(c => c.TransportOrderId).Distinct().ToListAsync(ct);
        foreach (var orderId in orderIds)
        {
            var pending = await db.Set<CodCollection>().AsNoTracking().AnyAsync(c => c.TransportOrderId == orderId && c.StatusCodeId != voidId && c.StatusCodeId != remittedId, ct);
            var cur = (await db.TransportOrders.AsNoTracking().FirstAsync(o => o.TransportOrderId == orderId, ct)).CodStatusCodeId;
            if (pending || cur is not int curId || codMap[curId].Code != CodStatuses.Reconciled) continue;
            var st = await statuses.TransitionAsync(StatusDomains.CodStatus, EntityTypes.OrderCod, orderId, curId, CodStatuses.Remitted, null, ct);
            var order = await db.TransportOrders.AsTracking().FirstAsync(o => o.TransportOrderId == orderId, ct);
            order.CodStatusCodeId = st.StatusCodeId;
            await db.SaveGuardedAsync(RemittanceRules.Concurrency, ct);
        }
    }

    /// <summary>Cancela una remesa abierta o cuadrada: se desactiva y sus cobros quedan libres para otra remesa.</summary>
    public async Task<CodRemittanceDto> CancelAsync(Guid publicId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await db.Set<CodRemittanceBatch>().AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct2) ?? throw new NotFoundException("Remesa", feminine: true);
            var code = (await StatusMapAsync(RemittanceCodes.StatusDomain, ct2))[r.StatusCodeId].Code;
            if (code is not (RemittanceStatuses.Open or RemittanceStatuses.Reconciled)) throw new StatusRuleException(RemittanceRules.CancelNotAllowed);
            r.IsActive = false;
            foreach (var c in await db.Set<CodCollection>().AsTracking().Where(c => c.CodRemittanceBatchId == r.CodRemittanceBatchId).ToListAsync(ct2)) c.CodRemittanceBatchId = null;
            await db.SaveGuardedAsync(RemittanceRules.Concurrency, ct2);
        }, ct);
        var cancelled = await db.Set<CodRemittanceBatch>().AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.PublicId == publicId, ct);
        return await ToDtoAsync(cancelled, ct);
    }

    // ================================================================ exportar

    /// <summary>El archivo de la remesa (una fila por cobro) con una plantilla del alcance COD_REMITTANCE; sin cambiar nada.</summary>
    public async Task<ExportFileDto> ExportAsync(Guid publicId, Guid? templatePublicId, CancellationToken ct)
    {
        var r = await db.Set<CodRemittanceBatch>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct) ?? throw new NotFoundException("Remesa", feminine: true);
        var code = (await StatusMapAsync(RemittanceCodes.StatusDomain, ct))[r.StatusCodeId].Code;
        if (code is not (RemittanceStatuses.Approved or RemittanceStatuses.Remitted)) throw new StatusRuleException(RemittanceRules.ExportNeedsApproved);
        var template = await templates.ResolveAsync(ExportCodes.ScopeCodRemittance, templatePublicId, ct);
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == r.ClientId, ct);
        var currency = (await LookupAsync(r.CurrencyLookupId, ct)).Code;
        var ids = r.Lines.Select(l => l.CodCollectionId).ToList();
        var collections = await db.Set<CodCollection>().AsNoTracking().Where(c => ids.Contains(c.CodCollectionId)).ToDictionaryAsync(c => c.CodCollectionId, ct);
        var orderIds = collections.Values.Select(c => c.TransportOrderId).Distinct().ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId)).ToDictionaryAsync(o => o.TransportOrderId, ct);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var l in r.Lines.OrderBy(x => x.CodRemittanceLineId))
        {
            var c = collections[l.CodCollectionId];
            var o = orders[l.TransportOrderId];
            rows.Add(new Dictionary<string, object?>
            {
                ["REMITTANCE_NUMBER"] = r.Number, ["PERIOD_START"] = r.PeriodStart, ["PERIOD_END"] = r.PeriodEnd, ["CLIENT_CODE"] = client.Code, ["CLIENT_NAME"] = client.Name, ["CURRENCY"] = currency,
                ["ORDER_NUMBER"] = o.OrderNumber, ["INVOICE_NUMBER"] = string.IsNullOrWhiteSpace(o.ClientInvoiceNumber) ? null : o.ClientInvoiceNumber, ["COLLECTED_DATE"] = clock.DayOf(c.CollectedAtUtc),
                ["METHOD"] = (await LookupAsync(c.MethodLookupId, ct)).Code, ["CHECK_NUMBER"] = c.CheckNumber, ["REFERENCE"] = c.Reference, ["AMOUNT_COLLECTED"] = l.AmountCollected, ["COMMISSION_PCT"] = r.CommissionPct,
                ["COMMISSION_AMOUNT"] = l.CommissionAmount, ["NET_AMOUNT"] = l.AmountCollected - l.CommissionAmount, ["BATCH_GROSS"] = r.GrossCollected, ["BATCH_COMMISSION"] = r.CommissionAmount,
                ["BATCH_NET"] = r.GrossCollected - r.CommissionAmount,
            });
        }
        var file = ExportEngine.Render(ExportCodes.ScopeCodRemittance, template.Format, template.Delimiter, template.DateFormat, template.IncludeHeader, ExportRules.ParseColumns(template.ColumnsJson), rows, $"remesa-{r.Number}");
        return new ExportFileDto(file.FileName, file.ContentType, file.Text, file.RowCount, template.Name);
    }
}
