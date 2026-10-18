using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Cod;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 91 (P12) — base de los proveedores de «Actividad reciente» de Operación y Contabilidad. Comparte con el de Almacén el catálogo ActivityEventType (etiqueta, obligatorio y
/// encendido por defecto, con el LookupCodeOverride del tenant) y la lectura del historial de estatus; cada proveedor dice qué transición produce cada evento. Solo lectura,
/// AsNoTracking bajo el filtro global de tenant.
/// </summary>
public abstract class ActivityProviderBase(TeikemDbContext db, ILookupCache lookups)
{
    protected TeikemDbContext Db { get; } = db;
    protected abstract string ModuleCode { get; }

    protected sealed record CatalogEntry(string Label, bool Mandatory, bool DefaultOn);

    /// <summary>Una transición del historial que produce un evento: entidad, estatus destino y código del evento.</summary>
    protected sealed record Source(string EntityType, string Domain, string ToStatus, string Event);

    protected sealed record Hit(string EntityType, int EntityId, string Event, DateTime AtUtc, int? UserId);

    /// <summary>Eventos activos del módulo en el catálogo (TenantId NULL) con la etiqueta en el idioma del usuario y el override del tenant aplicado.</summary>
    protected async Task<IReadOnlyDictionary<string, CatalogEntry>> CatalogAsync(string lang, CancellationToken ct)
    {
        var map = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        var rows = (await lookups.GetDomainAsync(ActivityRules.CatalogDomain, ct)).Where(l => l.IsActive && l.TenantId is null).ToList();
        if (rows.Count == 0) return map;
        var ids = rows.Select(l => l.LookupCodeId).ToList();
        var overrides = await Db.LookupCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.LookupCodeId)).ToDictionaryAsync(o => o.LookupCodeId, ct);
        foreach (var l in rows)
        {
            var baseMeta = ActivityRules.ParseMeta(l.ExtraJson);
            if (!string.Equals(baseMeta.Module, ModuleCode, StringComparison.OrdinalIgnoreCase)) continue;
            var o = overrides.GetValueOrDefault(l.LookupCodeId);
            if (ActivityRules.ApplyOverride(baseMeta, o?.IsEnabled ?? true, o?.CustomExtraJson) is not { } meta) continue;
            map[l.InternalCode] = new CatalogEntry(MultilingualText.Resolve(MultilingualText.Merge(l.LabelJson, o?.CustomLabelJson), lang), meta.Mandatory, meta.DefaultOn);
        }
        return map;
    }

    protected static Func<string, bool> WantedIn(IReadOnlyDictionary<string, CatalogEntry> catalog, bool onlyMandatory)
        => code => catalog.TryGetValue(code, out var e) && ActivityRules.IsShown(e.Mandatory, e.DefaultOn, onlyMandatory);

    /// <summary>Transiciones del historial desde `fromUtc` cuyos eventos se muestran.</summary>
    protected async Task<List<Hit>> HistoryAsync(IReadOnlyList<Source> sources, DateTime fromUtc, Func<string, bool> wanted, CancellationToken ct)
    {
        var active = sources.Where(s => wanted(s.Event)).ToList();
        if (active.Count == 0) return new List<Hit>();
        var domains = active.Select(s => s.Domain).Distinct().ToList();
        var statusRows = await Db.StatusCodes.AsNoTracking().Where(s => domains.Contains(s.Entity)).Select(s => new { s.StatusCodeId, s.Entity, s.InternalCode }).ToListAsync(ct);
        var typeIds = new Dictionary<int, string>();
        foreach (var t in active.Select(s => s.EntityType).Distinct())
            if (await lookups.TryGetIdAsync(LookupDomains.EntityType, t, ct) is int id) typeIds[id] = t;
        // (tipo, id de estatus) → evento
        var eventOf = new Dictionary<(string Type, int StatusId), string>();
        foreach (var s in active)
            foreach (var st in statusRows.Where(r => r.Entity == s.Domain && string.Equals(r.InternalCode, s.ToStatus, StringComparison.OrdinalIgnoreCase)))
                eventOf[(s.EntityType, st.StatusCodeId)] = s.Event;
        if (typeIds.Count == 0 || eventOf.Count == 0) return new List<Hit>();

        var typeList = typeIds.Keys.ToList();
        var statusList = eventOf.Keys.Select(k => k.StatusId).Distinct().ToList();
        var rows = await Db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.ChangedAtUtc >= fromUtc && typeList.Contains(h.EntityTypeLookupId) && statusList.Contains(h.ToStatusCodeId))
            .Select(h => new { h.EntityTypeLookupId, h.EntityId, h.ToStatusCodeId, h.ChangedAtUtc, h.ChangedBy }).ToListAsync(ct);
        var hits = new List<Hit>();
        foreach (var r in rows)
        {
            var type = typeIds[r.EntityTypeLookupId];
            if (eventOf.TryGetValue((type, r.ToStatusCodeId), out var ev)) hits.Add(new Hit(type, r.EntityId, ev, r.ChangedAtUtc, r.ChangedBy));
        }
        return hits;
    }

    protected ActivityEventDto Make(IReadOnlyDictionary<string, CatalogEntry> catalog, DateTime atUtc, string code, string entityType, int entityId, Guid? publicId, string reference, string? detail, int? userId)
    {
        var e = catalog[code];
        return new ActivityEventDto(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc), code, ModuleCode, e.Mandatory, e.Label, entityType, entityId, publicId, reference, detail, userId, null);
    }

    protected async Task<Dictionary<int, string>> ClientNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();
        return await Db.Clients.AsNoTracking().Where(c => list.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
    }

    protected static string Money(decimal amount) => amount.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
}

/// <summary>
/// Lote 91 (P12) — pestaña Operación (orders.view; módulo LTL_GROUND): órdenes entregadas, falladas, canceladas o en pausa y rutas despachadas, completadas o canceladas, leídas del
/// historial de estatus. La referencia es el número de la orden o el código de la ruta; el detalle, el cliente (y su factura).
/// </summary>
public sealed class OperationsActivityProvider(TeikemDbContext db, ILookupCache lookups) : ActivityProviderBase(db, lookups), IActivityEventProvider
{
    protected override string ModuleCode => BusinessModules.Operations;
    public string Module => BusinessModules.Operations;
    public string RequiredPermission => PermissionCatalog.OrdersView;
    public string? TenantModule => ModuleKeys.LtlGround;

    private static readonly Source[] Sources =
    {
        new(EntityTypes.TransportOrder, StatusDomains.OrderStatus, OrderStatuses.Delivered, ActivityEvents.OrderDelivered),
        new(EntityTypes.TransportOrder, StatusDomains.OrderStatus, OrderStatuses.Failed, ActivityEvents.OrderFailed),
        new(EntityTypes.TransportOrder, StatusDomains.OrderStatus, OrderStatuses.Cancelled, ActivityEvents.OrderCancelled),
        new(EntityTypes.Trip, StatusDomains.TripStatus, TripStatuses.Dispatched, ActivityEvents.TripDispatched),
        new(EntityTypes.Trip, StatusDomains.TripStatus, TripStatuses.Completed, ActivityEvents.TripCompleted),
        new(EntityTypes.Trip, StatusDomains.TripStatus, TripStatuses.Cancelled, ActivityEvents.TripCancelled),
    };

    public async Task<IReadOnlyList<ActivityEventDto>> ReadAsync(ActivityScope scope, DateTime fromUtc, bool onlyMandatory, CancellationToken ct)
    {
        var catalog = await CatalogAsync(scope.Lang, ct);
        var hits = await HistoryAsync(Sources, fromUtc, WantedIn(catalog, onlyMandatory), ct);
        var events = new List<ActivityEventDto>();
        if (hits.Count == 0) return events;

        var orderHits = hits.Where(h => h.EntityType == EntityTypes.TransportOrder).ToList();
        if (orderHits.Count > 0)
        {
            var ids = orderHits.Select(h => h.EntityId).Distinct().ToList();
            var orders = await Db.TransportOrders.AsNoTracking().Where(o => ids.Contains(o.TransportOrderId))
                .Select(o => new { o.TransportOrderId, o.PublicId, o.OrderNumber, o.ClientInvoiceNumber, o.ClientId }).ToDictionaryAsync(o => o.TransportOrderId, ct);
            var clients = await ClientNamesAsync(orders.Values.Select(o => o.ClientId), ct);
            foreach (var h in orderHits)
            {
                if (!orders.TryGetValue(h.EntityId, out var o)) continue;
                var client = clients.GetValueOrDefault(o.ClientId);
                var detail = string.IsNullOrWhiteSpace(o.ClientInvoiceNumber) ? client : $"{client} · {o.ClientInvoiceNumber}";
                events.Add(Make(catalog, h.AtUtc, h.Event, EntityTypes.TransportOrder, h.EntityId, o.PublicId, o.OrderNumber, detail, h.UserId));
            }
        }
        var tripHits = hits.Where(h => h.EntityType == EntityTypes.Trip).ToList();
        if (tripHits.Count > 0)
        {
            var ids = tripHits.Select(h => h.EntityId).Distinct().ToList();
            var trips = await Db.Set<Domain.Trips.Trip>().AsNoTracking().Where(t => ids.Contains(t.TripId))
                .Select(t => new { t.TripId, t.PublicId, t.Code, t.PlanDate }).ToDictionaryAsync(t => t.TripId, ct);
            foreach (var h in tripHits)
            {
                if (!trips.TryGetValue(h.EntityId, out var t)) continue;
                events.Add(Make(catalog, h.AtUtc, h.Event, EntityTypes.Trip, h.EntityId, t.PublicId, t.Code, t.PlanDate.ToString("yyyy-MM-dd"), h.UserId));
            }
        }
        return events;
    }
}

/// <summary>
/// Lote 91 (P12) — pestaña Contabilidad, parte de facturación (billing.view; módulo BILLING): facturas emitidas, pagadas, vencidas o anuladas y corridas aprobadas (historial de
/// estatus) y los pagos registrados o anulados (tabla Payment). La referencia es el número de la factura; el detalle, el cliente y el monto.
/// </summary>
public sealed class BillingActivityProvider(TeikemDbContext db, ILookupCache lookups) : ActivityProviderBase(db, lookups), IActivityEventProvider
{
    protected override string ModuleCode => BusinessModules.Accounting;
    public string Module => BusinessModules.Accounting;
    public string RequiredPermission => PermissionCatalog.BillingView;
    public string? TenantModule => ModuleKeys.Billing;

    private static readonly Source[] Sources =
    {
        new(BillingCodes.InvoiceEntityType, BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Issued, ActivityEvents.InvoiceIssued),
        new(BillingCodes.InvoiceEntityType, BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Paid, ActivityEvents.InvoicePaid),
        new(BillingCodes.InvoiceEntityType, BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Overdue, ActivityEvents.InvoiceOverdue),
        new(BillingCodes.InvoiceEntityType, BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Void, ActivityEvents.InvoiceVoided),
        new(BillingCodes.RunEntityType, BillingCodes.RunStatusDomain, BillingRunStatuses.Approved, ActivityEvents.BillingRunApproved),
    };

    public async Task<IReadOnlyList<ActivityEventDto>> ReadAsync(ActivityScope scope, DateTime fromUtc, bool onlyMandatory, CancellationToken ct)
    {
        var catalog = await CatalogAsync(scope.Lang, ct);
        var wanted = WantedIn(catalog, onlyMandatory);
        var events = new List<ActivityEventDto>();

        var hits = await HistoryAsync(Sources, fromUtc, wanted, ct);
        var invoiceHits = hits.Where(h => h.EntityType == BillingCodes.InvoiceEntityType).ToList();
        if (invoiceHits.Count > 0)
        {
            var ids = invoiceHits.Select(h => h.EntityId).Distinct().ToList();
            var invoices = await Db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.InvoiceId))
                .Select(i => new { i.InvoiceId, i.PublicId, i.InvoiceNumber, i.ClientId, Total = i.Subtotal + i.TaxAmount }).ToDictionaryAsync(i => i.InvoiceId, ct);
            var clients = await ClientNamesAsync(invoices.Values.Select(i => i.ClientId), ct);
            foreach (var h in invoiceHits)
                if (invoices.TryGetValue(h.EntityId, out var i))
                    events.Add(Make(catalog, h.AtUtc, h.Event, BillingCodes.InvoiceEntityType, h.EntityId, i.PublicId, i.InvoiceNumber, $"{clients.GetValueOrDefault(i.ClientId)} · {Money(i.Total)}", h.UserId));
        }
        var runHits = hits.Where(h => h.EntityType == BillingCodes.RunEntityType).ToList();
        if (runHits.Count > 0)
        {
            var ids = runHits.Select(h => h.EntityId).Distinct().ToList();
            var runs = await Db.Set<BillingRun>().AsNoTracking().Where(r => ids.Contains(r.BillingRunId))
                .Select(r => new { r.BillingRunId, r.PeriodStart, r.PeriodEnd, r.InvoiceCount, r.TotalAmount }).ToDictionaryAsync(r => r.BillingRunId, ct);
            foreach (var h in runHits)
                if (runs.TryGetValue(h.EntityId, out var r))
                    events.Add(Make(catalog, h.AtUtc, h.Event, BillingCodes.RunEntityType, h.EntityId, null, $"{r.PeriodStart:yyyy-MM-dd} – {r.PeriodEnd:yyyy-MM-dd}", $"{r.InvoiceCount} · {Money(r.TotalAmount)}", h.UserId));
        }

        if (wanted(ActivityEvents.PaymentRecorded) || wanted(ActivityEvents.PaymentVoided))
        {
            var payments = await Db.Set<Payment>().AsNoTracking().Where(p => p.PaidAtUtc >= fromUtc || (p.VoidedAtUtc != null && p.VoidedAtUtc >= fromUtc))
                .Select(p => new { p.PaymentId, p.InvoiceId, p.Amount, p.PaidAtUtc, p.RecordedBy, p.VoidedAtUtc, p.VoidedBy, p.Reference }).ToListAsync(ct);
            if (payments.Count > 0)
            {
                var ids = payments.Select(p => p.InvoiceId).Distinct().ToList();
                var invoices = await Db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.InvoiceId)).Select(i => new { i.InvoiceId, i.PublicId, i.InvoiceNumber, i.ClientId }).ToDictionaryAsync(i => i.InvoiceId, ct);
                var clients = await ClientNamesAsync(invoices.Values.Select(i => i.ClientId), ct);
                foreach (var p in payments)
                {
                    if (!invoices.TryGetValue(p.InvoiceId, out var i)) continue;
                    var detail = $"{clients.GetValueOrDefault(i.ClientId)} · {Money(p.Amount)}";
                    if (p.PaidAtUtc >= fromUtc && wanted(ActivityEvents.PaymentRecorded))
                        events.Add(Make(catalog, p.PaidAtUtc, ActivityEvents.PaymentRecorded, BillingCodes.InvoiceEntityType, p.InvoiceId, i.PublicId, i.InvoiceNumber, detail, p.RecordedBy));
                    if (p.VoidedAtUtc is DateTime v && v >= fromUtc && wanted(ActivityEvents.PaymentVoided))
                        events.Add(Make(catalog, v, ActivityEvents.PaymentVoided, BillingCodes.InvoiceEntityType, p.InvoiceId, i.PublicId, i.InvoiceNumber, detail, p.VoidedBy));
                }
            }
        }
        return events;
    }
}

/// <summary>
/// Lote 91 (P12) — pestaña Contabilidad, parte de COD (cod.view; módulo COD): cobros registrados y cuadrados (tabla CodCollection, un evento por cobro) y remesas aprobadas o
/// remitidas (historial de estatus). La referencia es el número de la orden (o de la remesa); el detalle, el cliente y el monto.
/// </summary>
public sealed class CodActivityProvider(TeikemDbContext db, ILookupCache lookups) : ActivityProviderBase(db, lookups), IActivityEventProvider
{
    protected override string ModuleCode => BusinessModules.Accounting;
    public string Module => BusinessModules.Accounting;
    public string RequiredPermission => PermissionCatalog.CodView;
    public string? TenantModule => ModuleKeys.Cod;

    private static readonly Source[] Sources =
    {
        new(RemittanceCodes.EntityType, RemittanceCodes.StatusDomain, RemittanceStatuses.Approved, ActivityEvents.CodRemittanceApproved),
        new(RemittanceCodes.EntityType, RemittanceCodes.StatusDomain, RemittanceStatuses.Remitted, ActivityEvents.CodRemitted),
    };

    public async Task<IReadOnlyList<ActivityEventDto>> ReadAsync(ActivityScope scope, DateTime fromUtc, bool onlyMandatory, CancellationToken ct)
    {
        var catalog = await CatalogAsync(scope.Lang, ct);
        var wanted = WantedIn(catalog, onlyMandatory);
        var events = new List<ActivityEventDto>();

        if (wanted(ActivityEvents.CodCollected) || wanted(ActivityEvents.CodReconciled))
        {
            var rows = await Db.Set<CodCollection>().AsNoTracking().Where(c => c.CollectedAtUtc >= fromUtc || (c.ReconciledAtUtc != null && c.ReconciledAtUtc >= fromUtc))
                .Select(c => new { c.TransportOrderId, c.AmountCollected, c.CollectedAtUtc, c.CollectedBy, c.ReconciledAtUtc, c.ReconciledBy }).ToListAsync(ct);
            if (rows.Count > 0)
            {
                var ids = rows.Select(r => r.TransportOrderId).Distinct().ToList();
                var orders = await Db.TransportOrders.AsNoTracking().Where(o => ids.Contains(o.TransportOrderId)).Select(o => new { o.TransportOrderId, o.PublicId, o.OrderNumber, o.ClientId }).ToDictionaryAsync(o => o.TransportOrderId, ct);
                var clients = await ClientNamesAsync(orders.Values.Select(o => o.ClientId), ct);
                foreach (var r in rows)
                {
                    if (!orders.TryGetValue(r.TransportOrderId, out var o)) continue;
                    var detail = $"{clients.GetValueOrDefault(o.ClientId)} · {Money(r.AmountCollected)}";
                    if (r.CollectedAtUtc >= fromUtc && wanted(ActivityEvents.CodCollected))
                        events.Add(Make(catalog, r.CollectedAtUtc, ActivityEvents.CodCollected, EntityTypes.TransportOrder, r.TransportOrderId, o.PublicId, o.OrderNumber, detail, r.CollectedBy));
                    if (r.ReconciledAtUtc is DateTime rec && rec >= fromUtc && wanted(ActivityEvents.CodReconciled))
                        events.Add(Make(catalog, rec, ActivityEvents.CodReconciled, EntityTypes.TransportOrder, r.TransportOrderId, o.PublicId, o.OrderNumber, detail, r.ReconciledBy));
                }
            }
        }

        var hits = await HistoryAsync(Sources, fromUtc, wanted, ct);
        if (hits.Count > 0)
        {
            var ids = hits.Select(h => h.EntityId).Distinct().ToList();
            var batches = await Db.Set<CodRemittanceBatch>().AsNoTracking().Where(b => ids.Contains(b.CodRemittanceBatchId))
                .Select(b => new { b.CodRemittanceBatchId, b.PublicId, b.Number, b.ClientId, b.NetRemitted }).ToDictionaryAsync(b => b.CodRemittanceBatchId, ct);
            var clients = await ClientNamesAsync(batches.Values.Select(b => b.ClientId), ct);
            foreach (var h in hits)
                if (batches.TryGetValue(h.EntityId, out var b))
                    events.Add(Make(catalog, h.AtUtc, h.Event, RemittanceCodes.EntityType, h.EntityId, b.PublicId, b.Number, $"{clients.GetValueOrDefault(b.ClientId)} · {Money(b.NetRemitted)}", h.UserId));
        }
        return events;
    }
}
