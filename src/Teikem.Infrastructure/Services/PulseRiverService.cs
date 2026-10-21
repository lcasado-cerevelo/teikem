using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 88 (P12) — los dos ríos del Pulso (la maqueta, «las dos corrientes vivas»). Son fotos de AHORA (sin tabla de instantáneas todavía): cada nodo cuenta lo que hay en ese punto del flujo.
/// - ÓRDENES («Paquetes en la calle»): Entrada (creadas hoy; con las que esperan recogida o recibo), En almacén (recibidas), Por despachar (en almacén o planificadas SIN chofer), En ruta (con los choferes
///   de las rutas en curso) y Entregado hoy (por el historial de estatus). Entrada y Entregado llevan las barritas de 7 días.
/// - COD («Dinero COD»): Por cobrar (órdenes ya entregadas o en destino con COD sin cobrar del todo, y lo que falta), Cobrado (en la calle y sin cuadrar, con efectivo / cheques / otros),
///   Cuadrado (cuadrado y sin remesa, con cuántos clientes) y Remesa lista (remesas abiertas, cuadradas o aprobadas: el neto por remitir).
/// Los datos siguen el filtro de la compañía; los permisos y el módulo los pide el controlador. Las sumas de dinero suponen una sola moneda (la de la compañía).
/// </summary>
public sealed class PulseRiverService(TeikemDbContext db, ITenantClock clock, ILookupCache lookups, KpiHistory? history = null)
{
    public const int SparkDays = 7;

    private static PulseRiverValueDto Count(string key, decimal v) => new(key, v, "count");
    private static PulseRiverValueDto Money(string key, decimal v) => new(key, v, "money");

    private async Task<Dictionary<string, int>> StatusIdsAsync(string domain, CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).Select(s => new { s.InternalCode, s.StatusCodeId }).ToListAsync(ct)).ToDictionary(s => s.InternalCode, s => s.StatusCodeId);

    /// <summary>Cantidad por día de los últimos SparkDays (el último es hoy) a partir de instantes UTC.</summary>
    private decimal[] Spark(IEnumerable<DateTime> instantsUtc, DateOnly today)
    {
        var counts = new decimal[SparkDays];
        foreach (var at in instantsUtc)
        {
            var back = today.DayNumber - clock.DayOf(at).DayNumber;
            if (back >= 0 && back < SparkDays) counts[SparkDays - 1 - back]++;
        }
        return counts;
    }

    /// <summary>Órdenes sin asignar o asignadas SIN una ruta con chofer (la ruta no está cancelada ni completada). Las usan el río, el aviso «sin chofer» y el Radar.</summary>
    public async Task<IQueryable<TransportOrder>> UnassignedQueryAsync(CancellationToken ct)
    {
        var st = await StatusIdsAsync(StatusDomains.OrderStatus, ct);
        int unassignedId = st.GetValueOrDefault(OrderStatuses.Unassigned, -1), assignedId = st.GetValueOrDefault(OrderStatuses.Assigned, -1);
        var tripOk = new[] { TripStatuses.Draft, TripStatuses.Planned, TripStatuses.Dispatched, TripStatuses.InProgress };
        var tripIds = (await StatusIdsAsync(StatusDomains.TripStatus, ct)).Where(kv => tripOk.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        var withDriver = db.Set<Teikem.Domain.Trips.TripOrder>().AsNoTracking().Where(to => to.IsCurrent && db.Set<Teikem.Domain.Trips.Trip>().Any(t => t.TripId == to.TripId && t.IsActive && t.DriverId != null && tripIds.Contains(t.StatusCodeId)))
            .Select(to => to.TransportOrderId);
        return db.TransportOrders.AsNoTracking().Where(o => o.IsActive && (o.StatusCodeId == unassignedId || o.StatusCodeId == assignedId) && !withDriver.Contains(o.TransportOrderId));
    }

    /// <summary>
    /// Las barritas de 7 días de un nodo con el historial de las fotos diarias: los 6 días anteriores salen de las fotos (0 donde no hubo) y el último es el valor de AHORA. Sin servicio de historial (pruebas),
    /// o sin ninguna foto todavía, no hay barritas (null) para no dibujar siete ceros.
    /// </summary>
    private async Task<IReadOnlyList<decimal>?> SparkFromHistoryAsync(string key, decimal today, CancellationToken ct)
    {
        if (history is null) return null;
        var series = (await history.SeriesAsync(key, SparkDays, ct)).ToArray();
        if (series.Take(SparkDays - 1).All(v => v == 0m) && today == 0m) return null;
        series[^1] = today;
        return series;
    }

    // ================================================================ órdenes

    /// <summary>
    /// Las existencias de AHORA que guardan las fotos diarias (KPI): cuántas órdenes están en el almacén (sin asignar) y cuántas por despachar (en almacén o planificadas sin chofer).
    /// Antes eran dos tarjetas del río; el río ya no las muestra (2026-10-21, dictado de Luis) pero las fotos siguen guardándolas.
    /// </summary>
    public async Task<(int InWarehouse, int ToDispatch)> StockAsync(CancellationToken ct)
    {
        var st = await StatusIdsAsync(StatusDomains.OrderStatus, ct);
        var unassignedId = st.GetValueOrDefault(OrderStatuses.Unassigned, -1);
        var inWarehouse = await db.TransportOrders.AsNoTracking().CountAsync(o => o.IsActive && o.StatusCodeId == unassignedId, ct);
        var toDispatch = await (await UnassignedQueryAsync(ct)).CountAsync(ct);
        return (inWarehouse, toDispatch);
    }

    /// <summary>
    /// «Paquetes en la calle» (2026-10-21, dictado de Luis), cinco tarjetas en este orden:
    /// ENTRY = las sometidas hoy (con las que esperan recogida); IN_WAREHOUSE = los envíos que ENTRARON al almacén (por recogido o por el escaneo del almacén), cada envío una sola vez, el
    /// día de la primera vez, con las barras de 7 días (en un flujo sano coincide con ENTRY); ROUTES = las rutas que salieron ese día, con barras; DELIVERED = los entregados hoy, con las barras
    /// de los 7 días; ON_ROUTE = los envíos en ruta ahora (con los choferes) y, más pequeño, los envíos completados (entregados) hoy (decidido por Luis, 2026-10-21).
    /// </summary>
    public async Task<PulseRiverDto> OrdersAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var (todayFrom, todayTo) = clock.UtcRange(today, today);
        var sparkFrom = clock.StartOfDayUtc(today.AddDays(-(SparkDays - 1)));
        var st = await StatusIdsAsync(StatusDomains.OrderStatus, ct);
        int Id(string code) => st.GetValueOrDefault(code, -1);
        int cancelledId = Id(OrderStatuses.Cancelled), submittedId = Id(OrderStatuses.Submitted), pickupId = Id(OrderStatuses.Pickup), unassignedId = Id(OrderStatuses.Unassigned),
            inTransitId = Id(OrderStatuses.InTransit);

        var orders = db.TransportOrders.AsNoTracking().Where(o => o.IsActive);

        // Entrada: creadas hoy (sin canceladas) y, aparte, las que esperan recogida o recibo
        var created = await orders.Where(o => o.CreatedAtUtc >= sparkFrom && o.StatusCodeId != cancelledId).Select(o => o.CreatedAtUtc).ToListAsync(ct);
        var entryToday = created.Count(c => c >= todayFrom && c < todayTo);
        var waiting = await orders.CountAsync(o => o.StatusCodeId == submittedId || o.StatusCodeId == pickupId, ct);

        // En almacén: la PRIMERA vez que cada envío llegó al almacén (recogido o sin asignar). Se toman los cambios de estatus de la semana; los envíos que ya habían llegado antes
        // (otro cambio anterior a la ventana) no se cuentan otra vez, y uno que pasa por recogido y luego por sin asignar cuenta una sola vez, el día en que llegó.
        var entityType = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
        var intoWarehouse = new[] { pickupId, unassignedId };
        var windowRows = await db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.EntityTypeLookupId == entityType && intoWarehouse.Contains(h.ToStatusCodeId) && h.ChangedAtUtc >= sparkFrom)
            .Select(h => new { h.EntityId, h.ChangedAtUtc }).ToListAsync(ct);
        var firstInWindow = windowRows.GroupBy(r => r.EntityId).ToDictionary(g => g.Key, g => g.Min(x => x.ChangedAtUtc));
        var alreadyIn = new HashSet<int>();
        foreach (var chunk in firstInWindow.Keys.Chunk(1000))
            alreadyIn.UnionWith(await db.EntityStatusHistories.AsNoTracking()
                .Where(h => h.EntityTypeLookupId == entityType && intoWarehouse.Contains(h.ToStatusCodeId) && h.ChangedAtUtc < sparkFrom && chunk.Contains(h.EntityId))
                .Select(h => h.EntityId).Distinct().ToListAsync(ct));
        var enteredWarehouse = firstInWindow.Where(kv => !alreadyIn.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        var enteredToday = enteredWarehouse.Count(c => c >= todayFrom && c < todayTo);

        // Rutas: las que salieron cada día (despachadas, en curso o completadas) por su fecha de plan, y las completadas hoy
        var tripSt = await StatusIdsAsync(StatusDomains.TripStatus, ct);
        var departed = new[] { TripStatuses.Dispatched, TripStatuses.InProgress, TripStatuses.Completed }.Select(c => tripSt.GetValueOrDefault(c, -1)).ToList();
        var weekFrom = today.AddDays(-(SparkDays - 1));
        var trips = db.Set<Teikem.Domain.Trips.Trip>().AsNoTracking().Where(t => t.IsActive);
        var tripDays = await trips.Where(t => departed.Contains(t.StatusCodeId) && t.PlanDate >= weekFrom && t.PlanDate <= today).Select(t => t.PlanDate).ToListAsync(ct);
        var routesSpark = new decimal[SparkDays];
        foreach (var d in tripDays) routesSpark[SparkDays - 1 - (today.DayNumber - d.DayNumber)]++;
        var routesToday = tripDays.Count(d => d == today);

        // En ruta: en tránsito, y los choferes de las rutas en curso
        var onRoute = await orders.CountAsync(o => o.StatusCodeId == inTransitId, ct);
        var live = new[] { tripSt.GetValueOrDefault(TripStatuses.Dispatched, -1), tripSt.GetValueOrDefault(TripStatuses.InProgress, -1) };
        var drivers = await db.Set<Teikem.Domain.Trips.Trip>().AsNoTracking().Where(t => t.IsActive && t.DriverId != null && live.Contains(t.StatusCodeId)).Select(t => t.DriverId).Distinct().CountAsync(ct);

        // Entregado hoy: por el historial de estatus de la orden
        var deliveredId = Id(OrderStatuses.Delivered);
        var delivered = await db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == entityType && h.ToStatusCodeId == deliveredId && h.ChangedAtUtc >= sparkFrom)
            .Select(h => new { h.EntityId, h.ChangedAtUtc }).ToListAsync(ct);
        var deliveredToday = delivered.Where(d => d.ChangedAtUtc >= todayFrom && d.ChangedAtUtc < todayTo).Select(d => d.EntityId).Distinct().Count();

        return new PulseRiverDto("ORDERS", today, new[]
        {
            new PulseRiverNodeDto("ENTRY", entryToday, "count", new[] { Count("waiting", waiting) }, Spark(created, today)),
            new PulseRiverNodeDto("IN_WAREHOUSE", enteredToday, "count", Array.Empty<PulseRiverValueDto>(), Spark(enteredWarehouse, today)),
            new PulseRiverNodeDto("ROUTES", routesToday, "count", Array.Empty<PulseRiverValueDto>(), routesSpark),
            new PulseRiverNodeDto("DELIVERED", deliveredToday, "count", Array.Empty<PulseRiverValueDto>(), Spark(delivered.GroupBy(d => d.EntityId).Select(g => g.Min(x => x.ChangedAtUtc)), today)),
            new PulseRiverNodeDto("ON_ROUTE", onRoute, "count", new[] { Count("drivers", drivers), Count("completed", deliveredToday) }, null),
        });
    }

    // ================================================================ COD

    public async Task<PulseRiverDto> CodAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var orderSt = await StatusIdsAsync(StatusDomains.OrderStatus, ct);
        var codSt = await StatusIdsAsync(StatusDomains.CodStatus, ct);
        var colSt = await StatusIdsAsync(CodCodes.CollectionStatusDomain, ct);
        var remSt = await StatusIdsAsync(RemittanceCodes.StatusDomain, ct);
        int Id(Dictionary<string, int> map, string code) => map.GetValueOrDefault(code, -1);
        var voidId = Id(colSt, CodCollectionStatuses.Void);
        var collectedStatusId = Id(colSt, CodCollectionStatuses.Collected);
        var reconciledStatusId = Id(colSt, CodCollectionStatuses.Reconciled);

        // Por cobrar: la entrega ya ocurrió (entregada) y el COD no se cobró del todo
        var atDestination = new[] { Id(orderSt, OrderStatuses.Delivered) };
        var open = new[] { Id(codSt, CodStatuses.Pending), Id(codSt, CodStatuses.Partial) };
        var toCollect = await db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.CodStatusCodeId != null && open.Contains(o.CodStatusCodeId!.Value) && atDestination.Contains(o.StatusCodeId))
            .Select(o => new { Cod = o.CodAmount ?? 0m, Collected = db.Set<CodCollection>().Where(c => c.TransportOrderId == o.TransportOrderId && c.StatusCodeId != voidId).Sum(c => (decimal?)c.AmountCollected) ?? 0m })
            .ToListAsync(ct);
        var toCollectAmount = toCollect.Sum(x => Math.Max(0m, x.Cod - x.Collected));

        // Cobrado (en la calle, sin cuadrar) con su desglose por método
        var collectedRows = await db.Set<CodCollection>().AsNoTracking().Where(c => c.StatusCodeId == collectedStatusId).Select(c => new { c.MethodLookupId, c.AmountCollected }).ToListAsync(ct);
        decimal cash = 0m, checks = 0m, other = 0m;
        foreach (var g in collectedRows.GroupBy(c => c.MethodLookupId))
        {
            var code = (await lookups.GetAsync(g.Key, ct))?.InternalCode ?? string.Empty;
            var sum = g.Sum(x => x.AmountCollected);
            if (code == CodMethods.Cash) cash += sum;
            else if (CodMethods.NeedsCheckNumber(code)) checks += sum;
            else other += sum;
        }

        // Cuadrado y sin remesa
        var reconciled = await db.Set<CodCollection>().AsNoTracking().Where(c => c.StatusCodeId == reconciledStatusId && c.CodRemittanceBatchId == null)
            .Select(c => new { c.TransportOrderId, c.AmountCollected }).ToListAsync(ct);
        var reconciledOrders = reconciled.Select(r => r.TransportOrderId).Distinct().ToList();
        var reconciledClients = reconciledOrders.Count == 0 ? 0 : await db.TransportOrders.AsNoTracking().Where(o => reconciledOrders.Contains(o.TransportOrderId)).Select(o => o.ClientId).Distinct().CountAsync(ct);

        // Remesas listas para pagar (abiertas, cuadradas o aprobadas)
        var pending = new[] { Id(remSt, RemittanceStatuses.Open), Id(remSt, RemittanceStatuses.Reconciled), Id(remSt, RemittanceStatuses.Approved) };
        var batches = await db.Set<CodRemittanceBatch>().AsNoTracking().Where(b => b.IsActive && pending.Contains(b.StatusCodeId)).Select(b => new { b.ClientId, Net = b.GrossCollected - b.CommissionAmount }).ToListAsync(ct);

        return new PulseRiverDto("COD", today, new[]
        {
            new PulseRiverNodeDto("TO_COLLECT", toCollect.Count, "count", new[] { Money("amount", toCollectAmount) }, await SparkFromHistoryAsync(KpiKeys.CodToCollect, toCollectAmount, ct)),
            new PulseRiverNodeDto("COLLECTED", collectedRows.Sum(c => c.AmountCollected), "money", new[] { Money("cash", cash), Money("checks", checks), Money("other", other) }, await SparkFromHistoryAsync(KpiKeys.CodCollected, collectedRows.Sum(c => c.AmountCollected), ct)),
            new PulseRiverNodeDto("RECONCILED", reconciled.Sum(r => r.AmountCollected), "money", new[] { Count("clients", reconciledClients) }, await SparkFromHistoryAsync(KpiKeys.CodReconciled, reconciled.Sum(r => r.AmountCollected), ct)),
            new PulseRiverNodeDto("REMITTANCE", batches.Sum(b => b.Net), "money", new[] { Count("remittances", batches.Count), Count("clients", batches.Select(b => b.ClientId).Distinct().Count()) }, await SparkFromHistoryAsync(KpiKeys.CodRemittanceReady, batches.Sum(b => b.Net), ct)),
        });
    }
}
