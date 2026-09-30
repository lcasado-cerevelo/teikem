using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 15 (P3) — franja "Almacén hoy" del Pulso del día (solo lectura). Reglas de conteo en WarehousePulseRules (D1–D3).
/// - Días LOCALES de la compañía con el punto único del Lote 14 (ITenantClock, hora de Puerto Rico): los 7 días que terminan hoy.
///   No sale del motor de gráficos (hallazgo 7 del plan: trabaja en memoria con tope de filas y agrupa por día UTC): los
///   movimientos se agregan EN SQL con una sola consulta agrupada por (índice de día local, tipo, motivo) — el índice de día es
///   un CASE anidado sobre las medianoches locales en UTC (<see cref="DayIndexProjection"/>) — y solo bajan unas decenas de filas.
/// - Recibidas y salida (D2) salen de la misma consulta: RECEIPT, ISSUE, CROSSDOCK y los ajustes RECEIPT_VARIANCE y
///   PICK_BATCH_REVERSAL. Todos tienen UN solo lado (el ledger lo exige), así que el filtro de almacén es el de ese lado
///   (ToWarehouseId ?? FromWarehouseId): el destino para lo recibido, el origen para la salida, el destino de la reversa.
/// - Conteos con diferencia: conteos activos en RECONCILED_VARIANCE ('Diferencia', Lote 14) con ReconciledAtUtc en el rango; se
///   traen solo las fechas (son pocos) y se reparten por día local en memoria. Es la misma definición que el indicador
///   'Conteos con diferencia' (prueba de consistencia en WarehousePulseServiceTests).
/// - Productos bajo mínimo: el número de ahora con la MISMA consulta que GET /products?belowMin=true (ProductService), la que
///   usa el panel Almacén: la cifra es idéntica.
/// - Almacenes por PublicId bajo el filtro de tenant: uno que no existe o es de otra compañía no cae en "todos" (cifras en cero,
///   como el Kárdex).
/// </summary>
public sealed class WarehousePulseService(TeikemDbContext db, ILookupCache lookups, ITenantClock clock, ProductService products)
{
    public async Task<WarehousePulseDaysDto> DaysAsync(WarehousePulseQuery? q, CancellationToken ct)
    {
        q ??= new WarehousePulseQuery();
        if (!WarehousePulseRules.IsValidDays(q.Days)) throw new ValidationException("days", WarehousePulseRules.DaysOutOfRange);
        var days = WarehousePulseRules.Days(clock.UtcNow, clock.Zone, q.Days);
        var fromUtc = days[0].StartUtc;
        var toUtc = days[^1].EndUtc;

        // Almacenes pedidos (PublicId → id bajo el filtro de tenant). null = todos.
        List<int>? warehouseIds = null;
        Guid[]? warehousePublicIds = null;
        if (q.WarehousePublicIds is { Length: > 0 })
        {
            var pubs = q.WarehousePublicIds.Distinct().ToList();
            var found = await db.Set<Warehouse>().AsNoTracking().Where(w => pubs.Contains(w.PublicId))
                .Select(w => new { w.WarehouseId, w.PublicId }).ToListAsync(ct);
            warehouseIds = found.Select(w => w.WarehouseId).ToList();
            warehousePublicIds = found.Select(w => w.PublicId).ToArray();
        }

        // Movimientos: una consulta agrupada en SQL.
        var ids = new PulseTxnIds(
            await LookupIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Receipt, ct),
            await LookupIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Issue, ct),
            await LookupIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.CrossDock, ct),
            await LookupIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment, ct),
            await LookupIdAsync(LookupDomains.AdjustmentReason, AdjustmentReasons.ReceiptVariance, ct),
            await LookupIdAsync(LookupDomains.AdjustmentReason, AdjustmentReasons.PickBatchReversal, ct));
        var groups = warehouseIds is { Count: 0 }
            ? new List<WarehousePulseGroupRow>()
            : await MovementGroupsQuery(db.Set<InventoryTransaction>().AsNoTracking(), days, ids, warehouseIds).ToListAsync(ct);

        var codes = new Dictionary<int, string>();
        foreach (var id in groups.SelectMany(g => new int?[] { g.TxnTypeLookupId, g.ReasonLookupId }).Where(i => i.HasValue).Select(i => i!.Value).Distinct())
            codes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? string.Empty;
        string? Code(int? id) => id is int i ? codes.GetValueOrDefault(i) : null;

        var received = WarehousePulseRules.Series(days.Count, groups.Select(g =>
            (g.Day, WarehousePulseRules.ReceivedUnits(Code(g.TxnTypeLookupId), Code(g.ReasonLookupId), g.Quantity))));
        var receivedMoves = WarehousePulseRules.Series(days.Count, groups
            .Where(g => WarehousePulseRules.IsReceived(Code(g.TxnTypeLookupId), Code(g.ReasonLookupId))).Select(g => (g.Day, (decimal)g.Count)));
        var outbound = WarehousePulseRules.Series(days.Count, groups.Select(g =>
            (g.Day, WarehousePulseRules.OutboundUnits(Code(g.TxnTypeLookupId), Code(g.ReasonLookupId), g.Quantity))));
        var outboundMoves = WarehousePulseRules.Series(days.Count, groups
            .Where(g => WarehousePulseRules.IsOutbound(Code(g.TxnTypeLookupId), Code(g.ReasonLookupId))).Select(g => (g.Day, (decimal)g.Count)));

        // Conteos cerrados en Diferencia (solo las fechas; se reparten por día local).
        var varianceId = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.CycleCountStatus && s.InternalCode == CycleCountStatuses.ReconciledVariance)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        var reconciledAt = new List<DateTime>();
        if (varianceId is int vid && warehouseIds is not { Count: 0 })
        {
            var counts = db.Set<CycleCount>().AsNoTracking()
                .Where(c => c.IsActive && c.StatusCodeId == vid && c.ReconciledAtUtc != null && c.ReconciledAtUtc >= fromUtc && c.ReconciledAtUtc < toUtc);
            if (warehouseIds is not null) counts = counts.Where(c => warehouseIds.Contains(c.WarehouseId));
            reconciledAt = await counts.Select(c => c.ReconciledAtUtc!.Value).ToListAsync(ct);
        }
        var variance = WarehousePulseRules.Series(days.Count, reconciledAt.Select(t => (WarehousePulseRules.IndexOf(days, t), 1m)));

        // Productos bajo mínimo en este momento: la consulta de GET /products?belowMin=true (take=1 y total).
        var belowMin = warehouseIds is { Count: 0 }
            ? 0
            : (await products.ListAsync(new ProductListQuery(BelowMin: true, Take: 1, WarehousePublicIds: warehousePublicIds), InventoryScope.Any, ct)).Total;

        var dayDtos = days.Select((d, i) => new WarehousePulseDayDto(d.Date, InventoryRules.Round4(received[i]), (int)receivedMoves[i],
            InventoryRules.Round4(outbound[i]), (int)outboundMoves[i], (int)variance[i])).ToList();
        var today = dayDtos[^1];
        return new WarehousePulseDaysDto(ZoneName(clock.Zone), days[^1].Date, fromUtc, toUtc, dayDtos,
            today.ReceivedUnits, InventoryRules.Round4(dayDtos.Sum(d => d.ReceivedUnits)),
            today.OutboundUnits, InventoryRules.Round4(dayDtos.Sum(d => d.OutboundUnits)),
            today.CountsWithVariance, dayDtos.Sum(d => d.CountsWithVariance), belowMin,
            WarehousePulseRules.CountsAlert(today.CountsWithVariance), WarehousePulseRules.BelowMinAlert(belowMin));
    }

    private async Task<int> LookupIdAsync(string domain, string code, CancellationToken ct)
        => await lookups.TryGetIdAsync(domain, code, ct) ?? -1;

    /// <summary>Id IANA de la zona (p. ej. America/Puerto_Rico) aunque el sistema la haya resuelto con su id de Windows.</summary>
    public static string ZoneName(TimeZoneInfo zone)
    {
        if (ReferenceEquals(zone, LocalDay.DefaultZone)) return LocalDay.DefaultZoneId;
        if (zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;
    }

    // ================================================================ consulta agrupada (pública para probar su traducción)

    /// <summary>Ids de catálogo de los movimientos que cuentan (−1 = el código no existe: no coincide con nada).</summary>
    public sealed record PulseTxnIds(int Receipt, int Issue, int CrossDock, int Adjustment, int ReceiptVariance, int PickBatchReversal);

    /// <summary>
    /// Movimientos del rango de <paramref name="days"/> que cuentan para la franja, agrupados por (índice de día local, tipo,
    /// motivo) con Σ Quantity (CON signo) y número de movimientos. warehouseIds null = todos; si no, el lado único del movimiento
    /// (ToWarehouseId ?? FromWarehouseId) debe estar en la lista. EF lo traduce a una subconsulta con el CASE agrupada en SQL (sin BD: ToQueryString).
    /// </summary>
    public static IQueryable<WarehousePulseGroupRow> MovementGroupsQuery(IQueryable<InventoryTransaction> source, IReadOnlyList<PulseDay> days,
        PulseTxnIds ids, IReadOnlyCollection<int>? warehouseIds)
    {
        var from = days[0].StartUtc;
        var to = days[^1].EndUtc;
        var (receipt, issue, crossDock, adjustment, receiptVariance, reversal) = (ids.Receipt, ids.Issue, ids.CrossDock, ids.Adjustment,
            ids.ReceiptVariance, ids.PickBatchReversal);
        var q = source.Where(t => t.CreatedAtUtc >= from && t.CreatedAtUtc < to
                                  && (t.TxnTypeLookupId == receipt || t.TxnTypeLookupId == issue || t.TxnTypeLookupId == crossDock
                                      || (t.TxnTypeLookupId == adjustment && (t.ReasonLookupId == receiptVariance || t.ReasonLookupId == reversal))));
        if (warehouseIds is not null)
        {
            var list = warehouseIds.Select(i => (int?)i).ToList();
            q = q.Where(t => list.Contains(t.ToWarehouseId ?? t.FromWarehouseId));
        }
        return q.Select(DayIndexProjection(days))
            .GroupBy(m => new { m.Day, m.TxnTypeLookupId, m.ReasonLookupId })
            .Select(g => new WarehousePulseGroupRow
            {
                Day = g.Key.Day, TxnTypeLookupId = g.Key.TxnTypeLookupId, ReasonLookupId = g.Key.ReasonLookupId,
                Quantity = g.Sum(m => m.Quantity), Count = g.Count(),
            });
    }

    /// <summary>
    /// Proyección del movimiento con su índice de día local: CASE anidado sobre las medianoches locales en UTC
    /// (CreatedAtUtc &lt; fin del día 0 → 0; &lt; fin del día 1 → 1; …; si no, el último). Supone el movimiento ya dentro del rango.
    /// </summary>
    public static Expression<Func<InventoryTransaction, PulseMovement>> DayIndexProjection(IReadOnlyList<PulseDay> days)
    {
        var t = Expression.Parameter(typeof(InventoryTransaction), "t");
        var created = Expression.Property(t, nameof(InventoryTransaction.CreatedAtUtc));
        Expression day = Expression.Constant(days.Count - 1);
        // Constantes sin Kind (como las fechas de la base): el literal de SQL queda como datetime2 sin sufijo de zona.
        for (var i = days.Count - 2; i >= 0; i--)
            day = Expression.Condition(Expression.LessThan(created, Expression.Constant(DateTime.SpecifyKind(days[i].EndUtc, DateTimeKind.Unspecified))),
                Expression.Constant(i), day);
        var body = Expression.MemberInit(Expression.New(typeof(PulseMovement)),
            Expression.Bind(typeof(PulseMovement).GetProperty(nameof(PulseMovement.Day))!, day),
            Expression.Bind(typeof(PulseMovement).GetProperty(nameof(PulseMovement.TxnTypeLookupId))!,
                Expression.Property(t, nameof(InventoryTransaction.TxnTypeLookupId))),
            Expression.Bind(typeof(PulseMovement).GetProperty(nameof(PulseMovement.ReasonLookupId))!,
                Expression.Property(t, nameof(InventoryTransaction.ReasonLookupId))),
            Expression.Bind(typeof(PulseMovement).GetProperty(nameof(PulseMovement.Quantity))!,
                Expression.Property(t, nameof(InventoryTransaction.Quantity))));
        return Expression.Lambda<Func<InventoryTransaction, PulseMovement>>(body, t);
    }

    /// <summary>Movimiento proyectado con su índice de día (member-init: traducible por EF).</summary>
    public sealed class PulseMovement
    {
        public int Day { get; set; }
        public int TxnTypeLookupId { get; set; }
        public int? ReasonLookupId { get; set; }
        public decimal Quantity { get; set; }
    }

    /// <summary>Fila agrupada: índice de día, tipo, motivo, Σ Quantity con signo y número de movimientos.</summary>
    public sealed class WarehousePulseGroupRow
    {
        public int Day { get; init; }
        public int TxnTypeLookupId { get; init; }
        public int? ReasonLookupId { get; init; }
        public decimal Quantity { get; init; }
        public int Count { get; init; }
    }
}
