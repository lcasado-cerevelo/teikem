using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
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
public sealed class PulseRiverService(TeikemDbContext db, ITenantClock clock, ILookupCache lookups)
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

    // ================================================================ órdenes

    public async Task<PulseRiverDto> OrdersAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var (todayFrom, todayTo) = clock.UtcRange(today, today);
        var sparkFrom = clock.StartOfDayUtc(today.AddDays(-(SparkDays - 1)));
        var st = await StatusIdsAsync(StatusDomains.OrderStatus, ct);
        int Id(string code) => st.GetValueOrDefault(code, -1);
        int cancelledId = Id(OrderStatuses.Cancelled), confirmedId = Id(OrderStatuses.Confirmed), pickupId = Id(OrderStatuses.Pickup), inboundId = Id(OrderStatuses.Inbound), plannedId = Id(OrderStatuses.Planned),
            inTransitId = Id(OrderStatuses.InTransit), arrivedId = Id(OrderStatuses.Arrived);

        var orders = db.TransportOrders.AsNoTracking().Where(o => o.IsActive);

        // Entrada: creadas hoy (sin canceladas) y, aparte, las que esperan recogida o recibo
        var created = await orders.Where(o => o.CreatedAtUtc >= sparkFrom && o.StatusCodeId != cancelledId).Select(o => o.CreatedAtUtc).ToListAsync(ct);
        var entryToday = created.Count(c => c >= todayFrom && c < todayTo);
        var waiting = await orders.CountAsync(o => o.StatusCodeId == confirmedId || o.StatusCodeId == pickupId, ct);

        var inWarehouse = await orders.CountAsync(o => o.StatusCodeId == inboundId, ct);

        // Por despachar: en almacén o planificadas sin una ruta con chofer
        var tripOk = new[] { TripStatuses.Draft, TripStatuses.Planned, TripStatuses.Dispatched, TripStatuses.InProgress };
        var tripIds = (await StatusIdsAsync(StatusDomains.TripStatus, ct)).Where(kv => tripOk.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        var withDriver = db.Set<Teikem.Domain.Trips.TripOrder>().AsNoTracking().Where(to => to.IsCurrent && db.Set<Teikem.Domain.Trips.Trip>().Any(t => t.TripId == to.TripId && t.IsActive && t.DriverId != null && tripIds.Contains(t.StatusCodeId)))
            .Select(to => to.TransportOrderId);
        var toDispatch = await orders.CountAsync(o => (o.StatusCodeId == inboundId || o.StatusCodeId == plannedId) && !withDriver.Contains(o.TransportOrderId), ct);

        // En ruta: en tránsito o en destino, y los choferes de las rutas en curso
        var onRoute = await orders.CountAsync(o => o.StatusCodeId == inTransitId || o.StatusCodeId == arrivedId, ct);
        var tripStatuses = await StatusIdsAsync(StatusDomains.TripStatus, ct);
        var live = new[] { tripStatuses.GetValueOrDefault(TripStatuses.Dispatched, -1), tripStatuses.GetValueOrDefault(TripStatuses.InProgress, -1) };
        var drivers = await db.Set<Teikem.Domain.Trips.Trip>().AsNoTracking().Where(t => t.IsActive && t.DriverId != null && live.Contains(t.StatusCodeId)).Select(t => t.DriverId).Distinct().CountAsync(ct);

        // Entregado hoy: por el historial de estatus de la orden
        var entityType = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
        var deliveredId = Id(OrderStatuses.Delivered);
        var delivered = await db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == entityType && h.ToStatusCodeId == deliveredId && h.ChangedAtUtc >= sparkFrom)
            .Select(h => new { h.EntityId, h.ChangedAtUtc }).ToListAsync(ct);
        var deliveredToday = delivered.Where(d => d.ChangedAtUtc >= todayFrom && d.ChangedAtUtc < todayTo).Select(d => d.EntityId).Distinct().Count();

        return new PulseRiverDto("ORDERS", today, new[]
        {
            new PulseRiverNodeDto("ENTRY", entryToday, "count", new[] { Count("waiting", waiting) }, Spark(created, today)),
            new PulseRiverNodeDto("IN_WAREHOUSE", inWarehouse, "count", Array.Empty<PulseRiverValueDto>(), null),
            new PulseRiverNodeDto("TO_DISPATCH", toDispatch, "count", Array.Empty<PulseRiverValueDto>(), null),
            new PulseRiverNodeDto("ON_ROUTE", onRoute, "count", new[] { Count("drivers", drivers) }, null),
            new PulseRiverNodeDto("DELIVERED", deliveredToday, "count", Array.Empty<PulseRiverValueDto>(), Spark(delivered.GroupBy(d => d.EntityId).Select(g => g.Min(x => x.ChangedAtUtc)), today)),
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

        // Por cobrar: la entrega ya ocurrió (en destino, entregada o parcial) y el COD no se cobró del todo
        var atDestination = new[] { Id(orderSt, OrderStatuses.Arrived), Id(orderSt, OrderStatuses.Delivered), Id(orderSt, OrderStatuses.Partial) };
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
            new PulseRiverNodeDto("TO_COLLECT", toCollect.Count, "count", new[] { Money("amount", toCollectAmount) }, null),
            new PulseRiverNodeDto("COLLECTED", collectedRows.Sum(c => c.AmountCollected), "money", new[] { Money("cash", cash), Money("checks", checks), Money("other", other) }, null),
            new PulseRiverNodeDto("RECONCILED", reconciled.Sum(r => r.AmountCollected), "money", new[] { Count("clients", reconciledClients) }, null),
            new PulseRiverNodeDto("REMITTANCE", batches.Sum(b => b.Net), "money", new[] { Count("remittances", batches.Count), Count("clients", batches.Select(b => b.ClientId).Distinct().Count()) }, null),
        });
    }
}
