using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Export;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 94 (P9) — liquidación a choferes (LIQ-#####). GENERADA → REVISADA → APROBADA (AAL2 en el controlador) → EXPORTADA; una generada o revisada se cancela y libera lo que reservó.
/// - Generar junta, del período (días de la compañía), lo que hay por pagar: (a) las ENTREGAS: los intentos vigentes sin liquidar de cada orden y chofer, que el motor de pago convierte en dinero con la fórmula
///   de la política (congelada en la corrida) y las tarifas vigentes en la fecha de la entrega (la de entrega por servicio y tipo de paquete de la orden y las de intento por nivel); una tarifa que falta
///   deja la línea en $0 con la nota «sin tarifa configurada» (nunca se inventa un monto); (b) los VIAJES pagados (DriverTrip abiertos) con el monto que se congeló al crearlos. Una entrega especial no
///   genera intentos: solo cuenta su viaje. Los intentos sin chofer se cuentan aparte (no se pagan hasta que la oficina los complete).
/// - Nunca se paga dos veces: al generar se reservan los intentos (SettledRunId) y los viajes (SettlementRunId) bajo bloqueo de sus órdenes y viajes; cancelar la corrida los libera.
/// - El cálculo se congela: una corrida ya generada no se recalcula si cambian las tarifas o la política. El ajuste manual de una línea conserva el monto calculado y exige el motivo.
/// - Aprobar pasa los viajes de la corrida a LIQUIDADO. La plataforma calcula y exporta; no emite el pago.
/// </summary>
public sealed class DriverSettlementService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService numbers, ILookupCache lookups, ITenantClock clock, ExportTemplateService templates)
{
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;

    // ================================================================ cálculo (sin guardar)

    private sealed record BuiltLine(int DriverId, string Kind, DateOnly Date, int? OrderId, int? TripId, string Description, int AttemptCount, IReadOnlyList<PayoutLine> Breakdown,
        bool RateMissing, decimal Amount, IReadOnlyList<int> AttemptIds);

    private sealed record Built(string Formula, IReadOnlyList<BuiltLine> Lines, int SkippedNoDriver);

    private async Task<string> FormulaAsync(CancellationToken ct)
    {
        var policy = await db.Set<DriverPayPolicy>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (policy is null) return DriverPayoutRules.DefaultFormula;
        var code = (await lookups.GetAsync(policy.PayoutFormulaLookupId, ct))?.InternalCode;
        return DriverPayoutRules.IsKnownFormula(code) ? code!.Trim().ToUpperInvariant() : DriverPayoutRules.DefaultFormula;
    }

    private async Task<Built> BuildAsync(int? driverId, DateOnly start, DateOnly end, IReadOnlySet<int>? onlyOrders, IReadOnlySet<int>? onlyTrips, CancellationToken ct)
    {
        var formula = await FormulaAsync(ct);
        var (from, toExclusive) = clock.UtcRange(start, end);
        var lines = new List<BuiltLine>();

        // ---- entregas: los intentos vigentes sin liquidar del período (las entregas especiales se pagan por viaje)
        var attemptsQuery = db.Set<DeliveryAttempt>().AsNoTracking().Where(a => a.IsActive && a.SettledRunId == null && a.AttemptedAtUtc >= from && a.AttemptedAtUtc < toExclusive
            && !db.DriverTrips.Any(t => t.TransportOrderId == a.TransportOrderId && t.IsActive));
        var skipped = await attemptsQuery.CountAsync(a => a.DriverId == null, ct);
        var attempts = await attemptsQuery.Where(a => a.DriverId != null && (driverId == null || a.DriverId == driverId)).ToListAsync(ct);
        if (onlyOrders is not null) attempts = attempts.Where(a => onlyOrders.Contains(a.TransportOrderId)).ToList();
        if (attempts.Count > 0)
        {
            var orderIds = attempts.Select(a => a.TransportOrderId).Distinct().ToList();
            var orders = await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId)).Select(o => new { o.TransportOrderId, o.OrderNumber, o.ServiceTypeLookupId }).ToDictionaryAsync(o => o.TransportOrderId, ct);
            var cargo = await db.Set<CargoLine>().AsNoTracking().Where(c => orderIds.Contains(c.TransportOrderId) && c.IsActive && c.PackageTypeLookupId != null)
                .Select(c => new { c.TransportOrderId, c.CargoLineId, c.PackageTypeLookupId }).ToListAsync(ct);
            var packageOf = cargo.GroupBy(c => c.TransportOrderId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.CargoLineId).First().PackageTypeLookupId!.Value);
            var driverIds = attempts.Select(a => a.DriverId!.Value).Distinct().ToList();
            var deliveryRates = await db.Set<DriverDeliveryRate>().AsNoTracking().Where(r => r.IsActive && driverIds.Contains(r.DriverId)).ToListAsync(ct);
            var attemptRates = await db.Set<DriverAttemptRate>().AsNoTracking().Where(r => r.IsActive && driverIds.Contains(r.DriverId)).ToListAsync(ct);

            foreach (var g in attempts.GroupBy(a => (Driver: a.DriverId!.Value, Order: a.TransportOrderId)))
            {
                var ordered = g.OrderBy(a => a.AttemptNumber).ToList();
                var order = orders.GetValueOrDefault(g.Key.Order);
                var last = ordered.FirstOrDefault(a => a.Delivered) ?? ordered[^1];
                var date = clock.DayOf(last.AttemptedAtUtc);
                decimal? deliveryRate = null;
                if (order is not null && packageOf.TryGetValue(g.Key.Order, out var package))
                    deliveryRate = DriverPayoutRules.PickCurrent(deliveryRates.Where(r => r.DriverId == g.Key.Driver && r.ServiceTypeLookupId == order.ServiceTypeLookupId && r.PackageTypeLookupId == package), date)?.Rate;
                var rates = new Dictionary<int, decimal>();
                foreach (var byLevel in attemptRates.Where(r => r.DriverId == g.Key.Driver).GroupBy(r => r.AttemptNumber))
                    if (DriverPayoutRules.PickCurrent(byLevel, date) is { } pick) rates[byLevel.Key] = pick.Rate;
                IReadOnlyList<PayoutLine> breakdown;
                decimal amount;
                var missing = false;
                try
                {
                    var result = DriverPayoutRules.Compute(formula, deliveryRate, rates, ordered.Select(a => new PayoutAttempt(a.AttemptNumber, a.Delivered)));
                    breakdown = result.Lines;
                    amount = result.Total;
                    missing = result.Lines.Any(l => l.Note == DriverPayoutRules.NoRateNote);
                }
                catch (ArgumentException ex)
                {
                    breakdown = new[] { new PayoutLine(DriverPayoutRules.LineDelivery, null, 0m, ex.Message) };
                    amount = 0m;
                    missing = true;
                }
                lines.Add(new BuiltLine(g.Key.Driver, SettlementCodes.LineDelivery, date, g.Key.Order, null, $"Orden {order?.OrderNumber ?? g.Key.Order.ToString()}", ordered.Count, breakdown, missing, amount,
                    ordered.Select(a => a.DeliveryAttemptId).ToList()));
            }
        }

        // ---- viajes pagados abiertos del período
        var openId = await db.StatusIdAsync(StatusDomains.DriverTripStatus, DriverTripStatuses.Open, ct);
        var trips = await db.DriverTrips.AsNoTracking().Where(t => t.IsActive && t.StatusCodeId == openId && t.SettlementRunId == null && t.TripDate >= start && t.TripDate <= end
            && (driverId == null || t.DriverId == driverId)).Include(t => t.TripType).Include(t => t.Order).ToListAsync(ct);
        if (onlyTrips is not null) trips = trips.Where(t => onlyTrips.Contains(t.DriverTripId)).ToList();
        foreach (var t in trips)
        {
            var name = t.TripType?.Name ?? "Viaje";
            var breakdown = new[] { new PayoutLine(SettlementCodes.LineTrip, null, t.Amount, t.RateMissing ? DriverPayoutRules.NoRateNote : null) };
            lines.Add(new BuiltLine(t.DriverId, SettlementCodes.LineTrip, t.TripDate, t.TransportOrderId, t.DriverTripId, t.Order is null ? $"Viaje: {name}" : $"Viaje: {name} · orden {t.Order.OrderNumber}", 0, breakdown,
                t.RateMissing, t.Amount, Array.Empty<int>()));
        }
        return new Built(formula, lines.OrderBy(l => l.Date).ThenBy(l => l.DriverId).ThenBy(l => l.Description, StringComparer.Ordinal).ToList(), skipped);
    }

    private async Task<(int? DriverId, Driver? Driver)> DriverAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is null) return (null, null);
        var d = await db.Set<Driver>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new ValidationException("driverPublicId", SettlementRules.DriverInvalid);
        return (d.DriverId, d);
    }

    private static void ValidateRequest(DriverSettlementRequest? req, out DateOnly start, out DateOnly end, out string? note)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.PeriodStart is not DateOnly s || req.PeriodEnd is not DateOnly e) throw new ValidationException("periodStart", SettlementRules.PeriodRequired);
        if (e < s) throw new ValidationException("periodEnd", SettlementRules.PeriodInvalid);
        note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is { Length: > SettlementRules.MaxNoteLength }) throw new ValidationException("note", SettlementRules.NoteTooLong);
        (start, end) = (s, e);
    }

    /// <summary>Lo que entraría en la liquidación del período (y del chofer, si se indica), sin guardar nada.</summary>
    public async Task<DriverSettlementPreviewDto> PreviewAsync(DriverSettlementRequest? req, CancellationToken ct)
    {
        ValidateRequest(req, out var start, out var end, out _);
        var (driverId, _) = await DriverAsync(req!.DriverPublicId, ct);
        var built = await BuildAsync(driverId, start, end, null, null, ct);
        var views = await ViewsAsync(built.Lines.Select((l, i) => new LineView(0, l.DriverId, l.Kind, l.Date, l.OrderId, l.TripId, l.Description, l.AttemptCount, l.Breakdown, l.RateMissing, l.Amount, l.Amount, false, null)).ToList(), ct);
        return new DriverSettlementPreviewDto(built.Formula, views.Count, views.Sum(v => v.Amount), built.SkippedNoDriver, views);
    }

    // ================================================================ generar

    public async Task<DriverSettlementDto> GenerateAsync(DriverSettlementRequest? req, CancellationToken ct)
    {
        ValidateRequest(req, out var start, out var end, out var note);
        var (driverId, _) = await DriverAsync(req!.DriverPublicId, ct);
        var first = await BuildAsync(driverId, start, end, null, null, ct);
        if (first.Lines.Count == 0) throw new StatusRuleException(SettlementRules.NothingToSettle);
        var orderIds = first.Lines.Where(l => l.Kind == SettlementCodes.LineDelivery).Select(l => l.OrderId!.Value).Distinct().OrderBy(i => i).ToList();
        var tripIds = first.Lines.Where(l => l.TripId != null).Select(l => l.TripId!.Value).Distinct().OrderBy(i => i).ToList();
        Guid publicId = Guid.Empty;

        await db.RunInTransactionAsync(async ct2 =>
        {
            // 1) se bloquean las órdenes y los viajes (en orden de id) y se vuelve a calcular con lo que hay AHORA, solo de lo bloqueado: dos corridas a la vez no pagan lo mismo
            foreach (var id in orderIds) await db.LockTransportOrderAsync(id, ct2);
            await db.LockDriverTripsAsync(tripIds, ct2);
            var built = await BuildAsync(driverId, start, end, orderIds.ToHashSet(), tripIds.ToHashSet(), ct2);
            if (built.Lines.Count == 0) throw new StatusRuleException(SettlementRules.NothingToSettle);

            // 2) la corrida y sus líneas (congeladas)
            var tenantRow = await db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == T.RequireTenantId(), ct2);
            var currencyId = await lookups.GetIdAsync(LookupDomains.Currency, tenantRow.CurrencyCode, ct2);
            var initial = (await statuses.GetInitialAsync(SettlementCodes.StatusDomain, ct2)).StatusCodeId;
            await numbers.EnsureAsync(SettlementCodes.NumberKind, null, ct2);
            var seq = await numbers.NextAsync(SettlementCodes.NumberKind, null, ct2);
            var run = new DriverSettlementRun
            {
                PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), Number = NumberFormat.Resolve(SettlementCodes.NumberPattern, seq), DriverId = driverId, PeriodStart = start, PeriodEnd = end,
                StatusCodeId = initial, PayoutFormulaCode = built.Formula, CurrencyLookupId = currencyId, SkippedNoDriver = built.SkippedNoDriver, Note = note, GeneratedAtUtc = clock.UtcNow, GeneratedBy = tenant.UserId,
            };
            foreach (var l in built.Lines)
                run.Lines.Add(new DriverSettlementLine
                {
                    TenantId = run.TenantId, DriverId = l.DriverId, Kind = l.Kind, ServiceDate = l.Date, TransportOrderId = l.OrderId, DriverTripId = l.TripId, Description = Trim(l.Description, 250), AttemptCount = l.AttemptCount,
                    BreakdownJson = JsonSerializer.Serialize(l.Breakdown.Select(b => new BreakdownJson(b.Kind, b.AttemptNumber, b.Amount, b.Note))), RateMissing = l.RateMissing, CalculatedAmount = l.Amount, Amount = l.Amount,
                });
            run.LineCount = run.Lines.Count;
            run.TotalAmount = run.Lines.Sum(x => x.Amount);
            db.Set<DriverSettlementRun>().Add(run);
            await db.SaveGuardedAsync(SettlementRules.Concurrency, ct2);
            await statuses.TransitionAsync(SettlementCodes.StatusDomain, SettlementCodes.EntityType, run.DriverSettlementRunId, null, DriverSettlementStatuses.Generated, null, ct2);

            // 3) se reservan los intentos y los viajes (los servicios anidados pueden soltar lo rastreado: se vuelven a cargar)
            var attemptIds = built.Lines.SelectMany(l => l.AttemptIds).ToList();
            foreach (var a in await db.Set<DeliveryAttempt>().AsTracking().Where(a => attemptIds.Contains(a.DeliveryAttemptId)).ToListAsync(ct2)) a.SettledRunId = run.DriverSettlementRunId;
            var claimedTrips = built.Lines.Where(l => l.TripId != null).Select(l => l.TripId!.Value).ToList();
            foreach (var t in await db.DriverTrips.AsTracking().Where(t => claimedTrips.Contains(t.DriverTripId)).ToListAsync(ct2)) t.SettlementRunId = run.DriverSettlementRunId;
            await db.SaveGuardedAsync(SettlementRules.Concurrency, ct2);
            publicId = run.PublicId;
        }, ct);
        return await GetAsync(publicId, ct);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    private sealed record BreakdownJson(string Kind, int? AttemptNumber, decimal Amount, string? Note);

    // ================================================================ estatus

    private sealed record StatusInfo(string Code, string Label, string Color);

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == SettlementCodes.StatusDomain).ToListAsync(ct))
            .ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode, MultilingualText.Resolve(s.LabelJson, T.Lang) ?? s.InternalCode, s.ColorHex ?? "#9CA3AF"));

    /// <summary>
    /// Mueve la corrida de un estatus a otro. La transición va PRIMERO y la corrida se vuelve a cargar después: StatusService puede soltar lo rastreado (en las pruebas con base en memoria), y lo que se
    /// cambia antes de una transición se perdería. `effect` solo cambia campos y puede hacer sus propias transiciones.
    /// </summary>
    private async Task<DriverSettlementDto> MoveAsync(Guid publicId, string from, string to, string wrongMessage, string? comment, Func<DriverSettlementRun, CancellationToken, Task>? effect, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var head = await db.Set<DriverSettlementRun>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Liquidación", feminine: true);
            var map = await StatusMapAsync(ct2);
            if (map[head.StatusCodeId].Code != from) throw new StatusRuleException(wrongMessage);
            var st = await statuses.TransitionAsync(SettlementCodes.StatusDomain, SettlementCodes.EntityType, head.DriverSettlementRunId, head.StatusCodeId, to, comment, ct2);
            var r = await db.Set<DriverSettlementRun>().AsTracking().FirstAsync(x => x.DriverSettlementRunId == head.DriverSettlementRunId, ct2);
            r.StatusCodeId = st.StatusCodeId;
            if (effect is not null) await effect(r, ct2);
            await db.SaveGuardedAsync(SettlementRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>GENERADA → REVISADA.</summary>
    public Task<DriverSettlementDto> ReviewAsync(Guid publicId, CancellationToken ct)
        => MoveAsync(publicId, DriverSettlementStatuses.Generated, DriverSettlementStatuses.Reviewed, SettlementRules.ReviewNeedsGenerated, null, (r, _) =>
        {
            (r.ReviewedBy, r.ReviewedAtUtc) = (tenant.UserId, clock.UtcNow);
            return Task.CompletedTask;
        }, ct);

    /// <summary>REVISADA → APROBADA: quien aprueba y cuándo, y los viajes de la corrida pasan a LIQUIDADO. El controlador exige AAL2.</summary>
    public Task<DriverSettlementDto> ApproveAsync(Guid publicId, CancellationToken ct)
        => MoveAsync(publicId, DriverSettlementStatuses.Reviewed, DriverSettlementStatuses.Approved, SettlementRules.ApproveNeedsReviewed, null, async (r, ct2) =>
        {
            (r.ApprovedBy, r.ApprovedAtUtc) = (tenant.UserId, clock.UtcNow);
            var runId = r.DriverSettlementRunId;
            var openId = await db.StatusIdAsync(StatusDomains.DriverTripStatus, DriverTripStatuses.Open, ct2);
            var tripIds = await db.DriverTrips.AsNoTracking().Where(t => t.SettlementRunId == runId && t.StatusCodeId == openId).Select(t => t.DriverTripId).ToListAsync(ct2);
            foreach (var id in tripIds)
            {
                var st = await statuses.TransitionAsync(StatusDomains.DriverTripStatus, EntityTypes.DriverTrip, id, openId, DriverTripStatuses.Settled, $"Liquidación {r.Number}", ct2);
                (await db.DriverTrips.AsTracking().FirstAsync(t => t.DriverTripId == id, ct2)).StatusCodeId = st.StatusCodeId;
            }
            // lo rastreado pudo soltarse: la corrida se vuelve a enlazar antes de que MoveAsync guarde
            var tracked = await db.Set<DriverSettlementRun>().AsTracking().FirstAsync(x => x.DriverSettlementRunId == runId, ct2);
            (tracked.StatusCodeId, tracked.ApprovedBy, tracked.ApprovedAtUtc) = (r.StatusCodeId, r.ApprovedBy, r.ApprovedAtUtc);
        }, ct);

    /// <summary>Cancela una corrida generada o revisada: lo que reservó (intentos y viajes) vuelve a quedar por liquidar.</summary>
    public async Task<DriverSettlementDto> CancelAsync(Guid publicId, string? comment, CancellationToken ct)
    {
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        await db.RunInTransactionAsync(async ct2 =>
        {
            var head = await db.Set<DriverSettlementRun>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Liquidación", feminine: true);
            var map = await StatusMapAsync(ct2);
            if (map[head.StatusCodeId].Code is not (DriverSettlementStatuses.Generated or DriverSettlementStatuses.Reviewed)) throw new StatusRuleException(SettlementRules.CancelNeedsOpen);
            var st = await statuses.TransitionAsync(SettlementCodes.StatusDomain, SettlementCodes.EntityType, head.DriverSettlementRunId, head.StatusCodeId, DriverSettlementStatuses.Cancelled, text, ct2);
            var runId = head.DriverSettlementRunId;
            var r = await db.Set<DriverSettlementRun>().AsTracking().FirstAsync(x => x.DriverSettlementRunId == runId, ct2);
            r.StatusCodeId = st.StatusCodeId;
            r.TotalAmount = 0m;
            foreach (var a in await db.Set<DeliveryAttempt>().AsTracking().Where(a => a.SettledRunId == runId).ToListAsync(ct2)) a.SettledRunId = null;
            foreach (var t in await db.DriverTrips.AsTracking().Where(t => t.SettlementRunId == runId).ToListAsync(ct2)) t.SettlementRunId = null;
            await db.SaveGuardedAsync(SettlementRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ exportar

    /// <summary>El archivo de la liquidación (una fila por línea) con una plantilla del alcance DRIVER_SETTLEMENT, de una liquidación aprobada o exportada; sin cambiar nada.</summary>
    public async Task<ExportFileDto> PreviewExportAsync(Guid publicId, Guid? templatePublicId, CancellationToken ct)
    {
        var r = await db.Set<DriverSettlementRun>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException("Liquidación", feminine: true);
        var code = (await StatusMapAsync(ct))[r.StatusCodeId].Code;
        if (code is not (DriverSettlementStatuses.Approved or DriverSettlementStatuses.Exported)) throw new StatusRuleException(SettlementRules.ExportNeedsApproved);
        var template = await templates.ResolveAsync(ExportCodes.ScopeDriverSettlement, templatePublicId, ct);
        var currency = (await lookups.GetAsync(r.CurrencyLookupId, ct))?.InternalCode ?? "USD";
        var dids = r.Lines.Select(l => l.DriverId).Distinct().ToList();
        var drivers = await db.Set<Driver>().AsNoTracking().Where(d => dids.Contains(d.DriverId)).ToDictionaryAsync(d => d.DriverId, ct);
        var oids = r.Lines.Where(l => l.TransportOrderId != null).Select(l => l.TransportOrderId!.Value).Distinct().ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => oids.Contains(o.TransportOrderId)).ToDictionaryAsync(o => o.TransportOrderId, o => o.OrderNumber, ct);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var n = 0;
        foreach (var l in r.Lines.OrderBy(x => x.ServiceDate).ThenBy(x => x.DriverSettlementLineId))
        {
            n++;
            var breakdown = ParseBreakdown(l.BreakdownJson);
            var d = drivers.GetValueOrDefault(l.DriverId);
            rows.Add(new Dictionary<string, object?>
            {
                ["SETTLEMENT_NUMBER"] = r.Number, ["PERIOD_START"] = r.PeriodStart, ["PERIOD_END"] = r.PeriodEnd, ["DRIVER_CODE"] = d?.EmployeeCode, ["DRIVER_NAME"] = d?.FullName, ["CURRENCY"] = currency,
                ["LINE_NO"] = n, ["LINE_KIND"] = l.Kind, ["SERVICE_DATE"] = l.ServiceDate, ["ORDER_NUMBER"] = l.TransportOrderId is int oid ? orders.GetValueOrDefault(oid) : null, ["DESCRIPTION"] = l.Description,
                ["ATTEMPTS"] = l.AttemptCount, ["DELIVERY_AMOUNT"] = breakdown.Where(b => b.Kind is DriverPayoutRules.LineDelivery or SettlementCodes.LineTrip).Sum(b => b.Amount),
                ["ATTEMPTS_AMOUNT"] = breakdown.Where(b => b.Kind == DriverPayoutRules.LineAttempt).Sum(b => b.Amount), ["CALCULATED_AMOUNT"] = l.CalculatedAmount, ["AMOUNT"] = l.Amount,
                ["ADJUSTMENT_NOTE"] = l.AdjustmentNote, ["PAYOUT_FORMULA"] = r.PayoutFormulaCode, ["RUN_TOTAL"] = r.TotalAmount,
            });
        }
        var file = ExportEngine.Render(ExportCodes.ScopeDriverSettlement, template.Format, template.Delimiter, template.DateFormat, template.IncludeHeader, ExportRules.ParseColumns(template.ColumnsJson), rows, $"liquidacion-{r.Number}");
        return new ExportFileDto(file.FileName, file.ContentType, file.Text, file.RowCount, template.Name);
    }

    /// <summary>Arma el archivo y, si la liquidación estaba aprobada, la pasa a EXPORTADA (quién y cuándo). Una exportada se puede volver a exportar (otra plantilla, otra copia).</summary>
    public async Task<ExportFileDto> ExportAsync(Guid publicId, Guid? templatePublicId, CancellationToken ct)
    {
        var file = await PreviewExportAsync(publicId, templatePublicId, ct);
        var head = await db.Set<DriverSettlementRun>().AsNoTracking().FirstAsync(x => x.PublicId == publicId, ct);
        if ((await StatusMapAsync(ct))[head.StatusCodeId].Code == DriverSettlementStatuses.Approved)
            await MoveAsync(publicId, DriverSettlementStatuses.Approved, DriverSettlementStatuses.Exported, SettlementRules.ExportNeedsApproved, null, (r, _) =>
            {
                (r.ExportedBy, r.ExportedAtUtc) = (tenant.UserId, clock.UtcNow);
                return Task.CompletedTask;
            }, ct);
        return file;
    }

    // ================================================================ ajustar una línea

    /// <summary>Ajuste manual del monto de una línea (solo mientras la corrida está generada o revisada): conserva el calculado, exige el motivo y recalcula el total.</summary>
    public async Task<DriverSettlementDto> AdjustLineAsync(Guid publicId, int lineId, DriverSettlementAdjustRequest? req, CancellationToken ct)
    {
        if (req?.Amount is not decimal amount) throw new ValidationException("amount", SettlementRules.AdjustAmountRequired);
        if (!SettlementRules.IsValidAmount(amount)) throw new ValidationException("amount", SettlementRules.AdjustAmountInvalid);
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is { Length: > SettlementRules.MaxAdjustNoteLength }) throw new ValidationException("note", SettlementRules.AdjustNoteTooLong);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var run = await db.Set<DriverSettlementRun>().AsTracking().Include(r => r.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Liquidación", feminine: true);
            var code = (await StatusMapAsync(ct2))[run.StatusCodeId].Code;
            if (code is not (DriverSettlementStatuses.Generated or DriverSettlementStatuses.Reviewed)) throw new StatusRuleException(SettlementRules.AdjustNeedsOpen);
            var line = run.Lines.FirstOrDefault(l => l.DriverSettlementLineId == lineId) ?? throw new NotFoundException("Línea", feminine: true);
            if (amount == line.CalculatedAmount)
            {
                (line.Amount, line.AdjustmentNote, line.AdjustedBy, line.AdjustedAtUtc) = (line.CalculatedAmount, null, null, null);
            }
            else
            {
                if (note is null) throw new ValidationException("note", SettlementRules.AdjustNoteRequired);
                (line.Amount, line.AdjustmentNote, line.AdjustedBy, line.AdjustedAtUtc) = (amount, note, tenant.UserId, clock.UtcNow);
            }
            run.TotalAmount = run.Lines.Sum(l => l.Amount);
            await db.SaveGuardedAsync(SettlementRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ consultas

    public async Task<PagedResult<DriverSettlementListItemDto>> ListAsync(string? status, Guid? driverPublicId, string? q, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var map = await StatusMapAsync(ct);
        var query = db.Set<DriverSettlementRun>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var ids = map.Where(kv => kv.Value.Code.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            query = query.Where(r => ids.Contains(r.StatusCodeId));
        }
        if (driverPublicId is Guid dp)
        {
            var did = await db.Set<Driver>().AsNoTracking().Where(d => d.PublicId == dp).Select(d => (int?)d.DriverId).FirstOrDefaultAsync(ct);
            query = did is null ? query.Where(r => false) : query.Where(r => r.DriverId == did || db.Set<DriverSettlementLine>().Any(l => l.DriverSettlementRunId == r.DriverSettlementRunId && l.DriverId == did));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            var driverIds = await db.Set<Driver>().AsNoTracking().Where(d => d.FullName.Contains(s) || d.EmployeeCode.Contains(s)).Select(d => d.DriverId).ToListAsync(ct);
            query = query.Where(r => r.Number.Contains(s) || (r.DriverId != null && driverIds.Contains(r.DriverId.Value)));
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(r => r.GeneratedAtUtc).ThenByDescending(r => r.DriverSettlementRunId).Skip(skip).Take(take).ToListAsync(ct);
        var dids = page.Where(r => r.DriverId != null).Select(r => r.DriverId!.Value).Distinct().ToList();
        var drivers = await db.Set<Driver>().AsNoTracking().Where(d => dids.Contains(d.DriverId)).ToDictionaryAsync(d => d.DriverId, d => new { d.PublicId, d.EmployeeCode, d.FullName }, ct);
        var items = page.Select(r =>
        {
            var st = map.GetValueOrDefault(r.StatusCodeId) ?? new StatusInfo("", "", "#9CA3AF");
            var d = r.DriverId is int di ? drivers.GetValueOrDefault(di) : null;
            return new DriverSettlementListItemDto(r.PublicId, r.Number, st.Code, st.Label, st.Color, d?.PublicId, d?.EmployeeCode, d?.FullName, r.PeriodStart, r.PeriodEnd, r.LineCount, r.TotalAmount, r.PayoutFormulaCode, r.GeneratedAtUtc);
        }).ToList();
        return new PagedResult<DriverSettlementListItemDto>(items, total, skip, take);
    }

    public async Task<DriverSettlementDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var r = await db.Set<DriverSettlementRun>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException("Liquidación", feminine: true);
        var st = (await StatusMapAsync(ct)).GetValueOrDefault(r.StatusCodeId) ?? new StatusInfo("", "", "#9CA3AF");
        var views = await ViewsAsync(r.Lines.OrderBy(l => l.ServiceDate).ThenBy(l => l.DriverSettlementLineId).Select(l => new LineView(l.DriverSettlementLineId, l.DriverId, l.Kind, l.ServiceDate, l.TransportOrderId, l.DriverTripId,
            l.Description, l.AttemptCount, ParseBreakdown(l.BreakdownJson), l.RateMissing, l.CalculatedAmount, l.Amount, l.AdjustmentNote != null || l.Amount != l.CalculatedAmount, l.AdjustmentNote)).ToList(), ct);
        Driver? driver = r.DriverId is int di ? await db.Set<Driver>().AsNoTracking().FirstOrDefaultAsync(d => d.DriverId == di, ct) : null;
        async Task<string?> UserAsync(int? id) => id is int u ? await db.Users.AsNoTracking().Where(x => x.Id == u).Select(x => x.FullName ?? x.Email).FirstOrDefaultAsync(ct) : null;
        var currency = (await lookups.GetAsync(r.CurrencyLookupId, ct))?.InternalCode ?? "USD";
        return new DriverSettlementDto(r.PublicId, r.Number, st.Code, st.Label, st.Color, driver?.PublicId, driver?.EmployeeCode, driver?.FullName, r.PeriodStart, r.PeriodEnd, r.PayoutFormulaCode, currency,
            r.LineCount, r.TotalAmount, views.Where(v => v.Kind == SettlementCodes.LineDelivery).Sum(v => v.Amount), views.Where(v => v.Kind == SettlementCodes.LineTrip).Sum(v => v.Amount), r.SkippedNoDriver, r.Note,
            r.GeneratedAtUtc, await UserAsync(r.ReviewedBy), r.ReviewedAtUtc, await UserAsync(r.ApprovedBy), r.ApprovedAtUtc, r.ExportedAtUtc, r.RowVersion is null ? string.Empty : Convert.ToBase64String(r.RowVersion), views);
    }

    private static IReadOnlyList<PayoutLine> ParseBreakdown(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<PayoutLine>();
        try { return (JsonSerializer.Deserialize<List<BreakdownJson>>(json) ?? new()).Select(b => new PayoutLine(b.Kind, b.AttemptNumber, b.Amount, b.Note)).ToList(); }
        catch (JsonException) { return Array.Empty<PayoutLine>(); }
    }

    private sealed record LineView(int Id, int DriverId, string Kind, DateOnly Date, int? OrderId, int? TripId, string Description, int AttemptCount, IReadOnlyList<PayoutLine> Breakdown, bool RateMissing,
        decimal Calculated, decimal Amount, bool Adjusted, string? Note);

    private async Task<List<DriverSettlementLineDto>> ViewsAsync(IReadOnlyList<LineView> views, CancellationToken ct)
    {
        if (views.Count == 0) return new List<DriverSettlementLineDto>();
        var dids = views.Select(v => v.DriverId).Distinct().ToList();
        var drivers = await db.Set<Driver>().AsNoTracking().Where(d => dids.Contains(d.DriverId)).ToDictionaryAsync(d => d.DriverId, d => new { d.PublicId, d.EmployeeCode, d.FullName }, ct);
        var oids = views.Where(v => v.Kind == SettlementCodes.LineDelivery && v.OrderId != null).Select(v => v.OrderId!.Value).Distinct().ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => oids.Contains(o.TransportOrderId)).ToDictionaryAsync(o => o.TransportOrderId, o => new { o.PublicId, o.OrderNumber }, ct);
        var tids = views.Where(v => v.TripId != null).Select(v => v.TripId!.Value).Distinct().ToList();
        var trips = await db.DriverTrips.AsNoTracking().Where(t => tids.Contains(t.DriverTripId)).ToDictionaryAsync(t => t.DriverTripId, t => t.PublicId, ct);
        return views.Select(v =>
        {
            var d = drivers.GetValueOrDefault(v.DriverId);
            var o = v.Kind == SettlementCodes.LineDelivery && v.OrderId is int oid ? orders.GetValueOrDefault(oid) : null;
            return new DriverSettlementLineDto(v.Id, d?.PublicId ?? Guid.Empty, d?.EmployeeCode ?? string.Empty, d?.FullName ?? string.Empty, v.Kind, v.Date, o?.PublicId, o?.OrderNumber,
                v.TripId is int tid ? trips.GetValueOrDefault(tid) : null, v.Description, v.AttemptCount, v.Breakdown.Select(b => new PayoutLineDto(b.Kind, b.AttemptNumber, b.Amount, b.Note)).ToList(), v.RateMissing,
                v.Calculated, v.Amount, v.Adjusted, v.Note);
        }).ToList();
    }
}
