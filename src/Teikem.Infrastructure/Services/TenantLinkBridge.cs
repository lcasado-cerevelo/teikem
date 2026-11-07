using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>El enlace activo de la compañía del principal como CLIENTE: por quién despacha y con qué cliente de allá.</summary>
public sealed record ActiveTenantLink(int TenantLinkId, int ProviderTenantId, int ProviderClientId, string ProviderName);

/// <summary>
/// Lote 227 (P7 del pedido de vendedores) — las entregas por la compañía enlazada (lado de la compañía CLIENTE, la del principal).
/// - Crear la entrega de un despacho con el enlace activo (desde SalesDeliveryService): en la MISMA transacción del despacho, la recolección pasa a empacada (sin orden de
///   transporte aquí) y queda la fila TenantLinkOrder (única por despacho: idempotencia) con lo que se manda: consignatario de la orden de venta (como consignatario nuevo
///   del lado de allá, que lo reconoce por nombre y dirección), paquetes, COD, fechas, prioridad (urgente → HIGH) y la referencia de la orden de venta en las notas.
/// - Mandarla (al instante si no hay una transacción abierta; si no, o si falló por algo pasajero, el trabajo INTERCOMPANY_SYNC): en el contexto acotado de la otra compañía
///   (<see cref="LinkedTenantScope"/>: esa compañía, sin usuario, solo el cliente que nos representa), por el MISMO camino que la API de clientes (OrderService.CreateAsync con
///   el alcance de ese cliente), marcada con su origen (TENANT_LINK_ORDER + id): si ya existe allá, no se crea otra. Si la otra compañía la rechaza (validación), queda FAILED
///   con su mensaje y un aviso; se reintenta a mano.
/// - Traer de vuelta (trabajo INTERCOMPANY_SYNC o «Revisar ahora»): número, estatus (y si es final), quién recibió y cuándo; un cambio de estatus avisa. La prueba de entrega
///   y sus archivos se leen en vivo de allá, solo de esa orden. Todo exige el enlace ACTIVE: terminado, no se manda ni se trae nada (lo traído se queda como estaba).
/// </summary>
public sealed class TenantLinkBridge(TeikemDbContext db, ITenantContext tenant, StatusService statuses, ModuleService modules, LinkedTenantScope linked, ILookupCache lookups,
    INotificationPublisher? notices = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lo que se guarda para mandar: la orden de allá y los datos de aquí que la acompañan (referencias y avisos).</summary>
    public sealed record Payload(OrderCreateRequest Order, string SalesOrderNumber, string PickBatchNumber, string ClientCompanyName, bool IsUrgent, string? ConsigneeName);

    private sealed record ProviderSnapshot(int OrderId, Guid PublicId, string Number, string StatusCode, string StatusLabelJson, bool IsFinal, DateTime? DeliveredAtUtc, string? DeliveredTo);

    private TenantContext T => (TenantContext)tenant;

    // ================================================================ el enlace de la compañía cliente

    /// <summary>El enlace ACTIVE de la compañía del principal como cliente (con el módulo encendido aquí); null = no hay puente y la entrega se crea aquí, como siempre.</summary>
    public async Task<ActiveTenantLink?> ActiveAsClientAsync(CancellationToken ct)
    {
        if (tenant.TenantId is not int me || !await modules.IsEnabledAsync(ModuleKeys.Intercompany, ct)) return null;
        var link = await db.Set<TenantLink>().AsNoTracking().Where(l => l.ClientTenantId == me && l.Status == TenantLinkStatuses.Active && l.ProviderClientId != null)
            .Select(l => new { l.TenantLinkId, l.ProviderTenantId, l.ProviderClientId }).FirstOrDefaultAsync(ct);
        if (link is null) return null;
        var name = await db.Tenants.AsNoTracking().Where(t => t.TenantId == link.ProviderTenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        return new ActiveTenantLink(link.TenantLinkId, link.ProviderTenantId, link.ProviderClientId!.Value, name);
    }

    /// <summary>La compañía de allá está activa y tiene encendidos el módulo de compañías enlazadas y el de Transporte.</summary>
    public async Task<bool> ProviderAvailableAsync(int providerTenantId, CancellationToken ct)
    {
        if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.TenantId == providerTenantId && t.IsActive, ct)) return false;
        var keys = await modules.GetEnabledKeysAsync(providerTenantId, ct);
        return keys.Contains(ModuleKeys.Intercompany) && keys.Contains(ModuleKeys.LtlGround);
    }

    // ================================================================ crear la entrega (en la transacción del despacho)

    /// <summary>
    /// La entrega del despacho por la compañía enlazada: valida lo que allá es obligatorio (consignatario, paquetes), deja la recolección empacada y la fila por mandar. Corre dentro
    /// de la transacción de quien llama; devuelve el id de la fila. Idempotente: si el despacho ya tiene su entrega por el enlace, devuelve la misma.
    /// </summary>
    public async Task<int> CreateLinkedDeliveryAsync(ActiveTenantLink link, int salesOrderId, int pickBatchId, OrderCreateRequest data, CancellationToken ct)
    {
        var existing = await db.Set<TenantLinkOrder>().AsNoTracking().Where(o => o.PickBatchId == pickBatchId).Select(o => (int?)o.TenantLinkOrderId).FirstOrDefaultAsync(ct);
        if (existing is int already) return already;
        if (!await ProviderAvailableAsync(link.ProviderTenantId, ct)) throw new StatusRuleException(TenantLinkRules.ProviderUnavailable(link.ProviderName));

        var me = T.RequireTenantId();
        var order = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.SalesOrderId == salesOrderId).Select(o => new { o.Number, o.IsUrgent }).FirstAsync(ct);
        var batch = await db.Set<PickBatch>().AsTracking().FirstAsync(b => b.PickBatchId == pickBatchId, ct);
        var consignee = await ConsigneeAsync(data, ct) ?? throw new ValidationException("order.consigneeLocationPublicId", TenantLinkRules.ConsigneeRequired);
        var packages = (data.Packages ?? new List<OrderPackageLineRequest>())
            .Select(p => new OrderPackageLineRequest(p.PackageType, p.Description, p.Pieces, p.WeightKg, p.VolumeM3)).ToList();
        var packageErrors = OrderRules.ValidatePackages(packages.Select(p => new PackageLineInput(p.Description, p.Pieces, p.WeightKg, p.VolumeM3)).ToList(), false, data.CodAmount);
        if (packageErrors.Count > 0) { var e = packageErrors.First(); throw new ValidationException("order." + e.Key, e.Value); }
        var company = await db.Tenants.AsNoTracking().Where(t => t.TenantId == me).Select(t => t.Name).FirstAsync(ct);

        var notes = TenantLinkRules.Truncate(string.Join(" · ", new[] { $"{company} · orden de venta {order.Number} · despacho {batch.Number}", data.Notes?.Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s))), TenantLinkRules.MaxOrderNotesLength);
        var request = new OrderCreateRequest(null, null, consignee, null, null, data.Priority, packages, data.CodAmount, null, null, data.RequestedDate, data.PromisedDate, notes, null,
            ConfirmNow: data.ConfirmNow);
        var payload = new Payload(request, order.Number, batch.Number, company, order.IsUrgent, consignee.Name);

        // la recolección queda empacada aquí (su orden de transporte vive en la otra compañía)
        var packed = await statuses.TransitionAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, batch.PickBatchId, batch.StatusCodeId, PickBatchStatuses.Packed,
            $"Entrega por {link.ProviderName}", ct);
        batch.StatusCodeId = packed.StatusCodeId;
        batch.PackedAtUtc = DateTime.UtcNow;
        batch.PackedBy = tenant.UserId;
        var row = new TenantLinkOrder
        {
            PublicId = Guid.NewGuid(), TenantId = me, TenantLinkId = link.TenantLinkId, SalesOrderId = salesOrderId, PickBatchId = pickBatchId, ProviderTenantId = link.ProviderTenantId,
            RequestJson = JsonSerializer.Serialize(payload, Json), State = TenantLinkOrderStates.Pending, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        db.Set<TenantLinkOrder>().Add(row);
        await db.SaveGuardedAsync(SalesRules.Concurrency, ct);
        return row.TenantLinkOrderId;
    }

    /// <summary>El consignatario de la entrega como consignatario «nuevo» para allá: el que trae la entrega, o el de la orden de venta (con su país).</summary>
    private async Task<OrderConsigneeCreateRequest?> ConsigneeAsync(OrderCreateRequest data, CancellationToken ct)
    {
        if (data.NewConsignee is { } given)
            return given with { Code = null, AllowDupInvoice = false };
        if (data.ConsigneeLocationPublicId is not Guid locationId) return null;
        var loc = await db.Locations.AsNoTracking().Where(l => l.PublicId == locationId)
            .Select(l => new { l.Name, l.Line1, l.Line2, l.City, l.State, l.PostalCode, l.CountryLookupId, l.DeliveryNotes, l.DefaultServiceMinutes }).FirstOrDefaultAsync(ct);
        if (loc is null) return null;
        var country = (await lookups.GetAsync(loc.CountryLookupId, ct))?.InternalCode;
        return new OrderConsigneeCreateRequest(loc.Name, loc.Line1, loc.Line2, loc.City, loc.State, loc.PostalCode, country, null, DeliveryNotes: loc.DeliveryNotes,
            DefaultServiceMinutes: loc.DefaultServiceMinutes);
    }

    // ================================================================ mandar

    /// <summary>Manda la entrega ahora si se puede (sin transacción abierta: la fila ya está guardada). Una falla no se propaga: queda en la fila y la reintenta el trabajo.</summary>
    public async Task TryPushAsync(int linkOrderId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return;
        await PushAsync(linkOrderId, DateTime.UtcNow, ct);
    }

    /// <summary>Lo mismo, por el despacho (después de despachar y entregar en una sola transacción).</summary>
    public async Task TryPushBatchAsync(Guid pickBatchPublicId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return;
        var id = await (from o in db.Set<TenantLinkOrder>().AsNoTracking()
                        join b in db.Set<PickBatch>().AsNoTracking() on o.PickBatchId equals b.PickBatchId
                        where b.PublicId == pickBatchPublicId
                        select (int?)o.TenantLinkOrderId).FirstOrDefaultAsync(ct);
        if (id is int linkOrderId) await PushAsync(linkOrderId, DateTime.UtcNow, ct);
    }

    /// <summary>Crea la orden allá (una sola vez). true si quedó creada en esta llamada.</summary>
    public async Task<bool> PushAsync(int linkOrderId, DateTime nowUtc, CancellationToken ct)
    {
        var row = await db.Set<TenantLinkOrder>().AsTracking().FirstOrDefaultAsync(o => o.TenantLinkOrderId == linkOrderId, ct);
        if (row is null || row.State != TenantLinkOrderStates.Pending || row.ClaimedUntilUtc > nowUtc) return false;
        // reclamo: dos copias del API (o la petición y el trabajo) nunca mandan la misma a la vez
        var token = Guid.NewGuid();
        row.ClaimToken = token;
        row.ClaimedUntilUtc = nowUtc + TenantLinkRules.ClaimFor;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.Entry(row).State = EntityState.Detached; return false; }

        var link = await db.Set<TenantLink>().AsNoTracking().FirstAsync(l => l.TenantLinkId == row.TenantLinkId && l.ClientTenantId == row.TenantId, ct);
        var providerName = await db.Tenants.AsNoTracking().Where(t => t.TenantId == link.ProviderTenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var payload = JsonSerializer.Deserialize<Payload>(row.RequestJson, Json)!;
        ProviderSnapshot? created = null;
        string? error = null;
        var final = false;
        if (link.Status != TenantLinkStatuses.Active || link.ProviderClientId is null)
            (error, final) = (TenantLinkRules.LinkNotActive(providerName), true);
        else if (!await ProviderAvailableAsync(link.ProviderTenantId, ct))
            error = TenantLinkRules.ProviderUnavailable(providerName);
        else
        {
            try
            {
                created = await linked.RunAsync(link.ProviderTenantId, link.ProviderClientId.Value,
                    (sp, c) => CreateInProviderAsync(sp, row.TenantLinkOrderId, link.ProviderClientId.Value, payload, c), ct);
            }
            catch (TeikemException ex) { (error, final) = (ex.Message, true); }   // la otra compañía la rechazó (validación, cliente suspendido…): no se reintenta sola
            catch (Exception ex) when (ex is not OperationCanceledException) { error = ex.Message; }   // pasajero: se reintenta con espera creciente
        }

        // la fila otra vez (en las pruebas el contexto es compartido y el alta de allá pudo limpiar el seguimiento)
        row = await db.Set<TenantLinkOrder>().AsTracking().FirstAsync(o => o.TenantLinkOrderId == linkOrderId, ct);
        row.Attempts++;
        row.ClaimToken = null;
        row.ClaimedUntilUtc = null;
        if (created is not null)
        {
            row.State = TenantLinkOrderStates.Created;
            row.LastError = null;
            row.NextAttemptUtc = null;
            Apply(row, created, nowUtc);
        }
        else
        {
            row.LastError = TenantLinkRules.Truncate(error, TenantLinkRules.MaxErrorLength);
            if (final || row.Attempts >= TenantLinkRules.MaxAutoAttempts)
            {
                row.State = TenantLinkOrderStates.Failed;
                row.NextAttemptUtc = null;
                await NotifyAsync(TenantLinkEventCodes.DeliveryFailed, row, payload, providerName, new() { ["error"] = row.LastError }, $"TLO-{row.TenantLinkOrderId}-F{row.Attempts}", ct);
            }
            else row.NextAttemptUtc = nowUtc + TenantLinkRules.Backoff(row.Attempts);
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return false; }   // otro se adelantó; la orden de allá ya tiene su marca y no se duplica
        return created is not null;
    }

    /// <summary>En la otra compañía (contexto acotado): si la orden de esta entrega ya existe, la devuelve; si no, la crea por el camino de la API de clientes.</summary>
    private static async Task<ProviderSnapshot> CreateInProviderAsync(IServiceProvider sp, int linkOrderId, int clientId, Payload payload, CancellationToken ct)
    {
        var db2 = sp.GetRequiredService<TeikemDbContext>();
        var lookups2 = sp.GetRequiredService<ILookupCache>();
        var sourceType = await lookups2.GetIdAsync(LookupDomains.EntityType, TenantLinkCodes.OrderEntityType, ct);
        var existing = await db2.TransportOrders.AsNoTracking()
            .Where(o => o.SourceEntityTypeLookupId == sourceType && o.SourceEntityId == linkOrderId && o.ClientId == clientId)
            .Select(o => (int?)o.TransportOrderId).FirstOrDefaultAsync(ct);
        if (existing is int found) return await SnapshotAsync(db2, lookups2, found, ct);

        // tipos de paquete que allá no existen: el de la compañía por omisión (la descripción se queda)
        var codes = payload.Order.Packages?.Where(p => !string.IsNullOrWhiteSpace(p.PackageType)).Select(p => p.PackageType!.Trim().ToUpperInvariant()).Distinct().ToList() ?? new List<string>();
        var known = codes.Count == 0 ? new List<string>()
            : await db2.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.PackageType && l.IsActive && codes.Contains(l.InternalCode)).Select(l => l.InternalCode).ToListAsync(ct);
        var packages = payload.Order.Packages?.Select(p => string.IsNullOrWhiteSpace(p.PackageType) || known.Contains(p.PackageType.Trim(), StringComparer.OrdinalIgnoreCase) ? p : p with { PackageType = null }).ToList();
        // el cliente de allá decide si teclea sus números: el de la orden = el despacho de aquí, el de la factura = la orden de venta de aquí
        var client = await db2.Clients.AsNoTracking().Where(c => c.ClientId == clientId).Select(c => new { c.PublicId, c.ClientAssignsOrderNumber, c.ClientAssignsInvoiceNumber }).FirstOrDefaultAsync(ct)
                     ?? throw new StatusRuleException(TenantLinkRules.ProviderClientNotUsable(string.Empty));
        var request = payload.Order with
        {
            ClientPublicId = client.PublicId,
            Packages = packages,
            OrderNumber = client.ClientAssignsOrderNumber ? payload.PickBatchNumber : null,
            ClientInvoiceNumber = client.ClientAssignsInvoiceNumber ? payload.SalesOrderNumber : null,
        };
        var orders = sp.GetRequiredService<OrderService>();
        var dto = await orders.CreateAsync(request, new OrderScope(clientId), new OrderCreationOptions(null, TenantLinkCodes.OrderEntityType, linkOrderId), ct);
        if (payload.IsUrgent && sp.GetService<INotificationPublisher>() is { } publisher)
        {
            // el pedido urgente: el transporte de allá tiene constancia (aviso), además de la prioridad alta de la orden
            await publisher.PublishAsync(TenantLinkEventCodes.UrgentOrder, new Dictionary<string, string?>
            {
                ["orderNumber"] = dto.OrderNumber, ["company"] = payload.ClientCompanyName, ["salesOrder"] = payload.SalesOrderNumber, ["consignee"] = payload.ConsigneeName,
            }, $"TLO-{linkOrderId}-U", ct);
            await db2.SaveChangesAsync(ct);
        }
        return await SnapshotAsync(db2, lookups2, dto.Id, ct);
    }

    /// <summary>La foto de la orden de allá: número, estatus (y si es final) y su comprobante de entrega vigente.</summary>
    private static async Task<ProviderSnapshot?> TrySnapshotAsync(TeikemDbContext db2, ILookupCache lookups2, int orderId, int linkOrderId, CancellationToken ct)
    {
        var sourceType = await lookups2.GetIdAsync(LookupDomains.EntityType, TenantLinkCodes.OrderEntityType, ct);
        // solo la orden que nació de ESTA entrega (marca de origen); cualquier otra orden de allá no se puede leer
        if (!await db2.TransportOrders.AsNoTracking().AnyAsync(o => o.TransportOrderId == orderId && o.SourceEntityTypeLookupId == sourceType && o.SourceEntityId == linkOrderId, ct)) return null;
        return await SnapshotAsync(db2, lookups2, orderId, ct);
    }

    private static async Task<ProviderSnapshot> SnapshotAsync(TeikemDbContext db2, ILookupCache lookups2, int orderId, CancellationToken ct)
    {
        var o = await db2.TransportOrders.AsNoTracking().Where(x => x.TransportOrderId == orderId)
            .Select(x => new { x.TransportOrderId, x.PublicId, x.OrderNumber, x.StatusCodeId, x.IsActive }).FirstAsync(ct);
        var st = await db2.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == o.StatusCodeId).Select(s => new { s.InternalCode, s.LabelJson, s.StageKindLookupId }).FirstAsync(ct);
        var terminal = await lookups2.TryGetIdAsync(LookupDomains.StageKind, StageKinds.Terminal, ct);
        var proof = await db2.Set<DeliveryProof>().AsNoTracking().Where(p => p.TransportOrderId == orderId && p.IsActive).OrderByDescending(p => p.DeliveryProofId)
            .Select(p => new { p.DeliveredAtUtc, p.RecipientName }).FirstOrDefaultAsync(ct);
        var delivered = proof?.DeliveredAtUtc;
        return new ProviderSnapshot(o.TransportOrderId, o.PublicId, o.OrderNumber, st.InternalCode, st.LabelJson, TenantLinkRules.IsFinal(st.StageKindLookupId == terminal, o.IsActive),
            delivered, TenantLinkRules.Truncate(proof?.RecipientName, TenantLinkRules.MaxDeliveredToLength));
    }

    private static void Apply(TenantLinkOrder row, ProviderSnapshot s, DateTime nowUtc)
    {
        row.ProviderOrderId = s.OrderId;
        row.ProviderOrderPublicId = s.PublicId;
        row.ProviderOrderNumber = s.Number;
        row.ProviderStatusCode = s.StatusCode;
        row.ProviderStatusLabelJson = TenantLinkRules.Truncate(s.StatusLabelJson, 400);
        row.IsFinal = s.IsFinal;
        row.DeliveredAtUtc = s.DeliveredAtUtc;
        row.DeliveredTo = s.DeliveredTo;
        row.LastSyncedAtUtc = nowUtc;
    }

    // ================================================================ traer de vuelta

    /// <summary>Lee la orden de allá y actualiza la foto; avisa si cambió el estatus. false si el enlace ya no está activo o la orden no se pudo leer.</summary>
    public async Task<bool> SyncAsync(int linkOrderId, DateTime nowUtc, CancellationToken ct)
    {
        var row = await db.Set<TenantLinkOrder>().AsTracking().FirstOrDefaultAsync(o => o.TenantLinkOrderId == linkOrderId, ct);
        if (row is null || row.State != TenantLinkOrderStates.Created || row.ProviderOrderId is not int providerOrderId) return false;
        var link = await db.Set<TenantLink>().AsNoTracking().FirstAsync(l => l.TenantLinkId == row.TenantLinkId && l.ClientTenantId == row.TenantId, ct);
        if (link.Status != TenantLinkStatuses.Active || link.ProviderClientId is null || !await ProviderAvailableAsync(link.ProviderTenantId, ct)) return false;
        var snap = await linked.RunAsync(link.ProviderTenantId, link.ProviderClientId.Value,
            (sp, c) => TrySnapshotAsync(sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ILookupCache>(), providerOrderId, linkOrderId, c), ct);
        row = await db.Set<TenantLinkOrder>().AsTracking().FirstAsync(o => o.TenantLinkOrderId == linkOrderId, ct);
        if (snap is null)
        {
            // la orden de allá ya no se puede leer con el cliente del enlace (la movieron de cliente): se deja de revisar
            row.IsFinal = true;
            row.LastSyncedAtUtc = nowUtc;
            await db.SaveChangesAsync(ct);
            return false;
        }
        var changed = !string.Equals(row.ProviderStatusCode, snap.StatusCode, StringComparison.Ordinal);
        Apply(row, snap, nowUtc);
        if (changed)
        {
            var payload = JsonSerializer.Deserialize<Payload>(row.RequestJson, Json)!;
            var name = await db.Tenants.AsNoTracking().Where(t => t.TenantId == link.ProviderTenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
            await NotifyAsync(TenantLinkEventCodes.DeliveryStatus, row, payload, name, new()
            {
                ["status"] = MultilingualText.Resolve(snap.StatusLabelJson, tenant.Lang),
                ["deliveredTo"] = snap.DeliveredTo is null ? string.Empty : $"Recibió: {snap.DeliveredTo}.",
            }, $"TLO-{row.TenantLinkOrderId}-{snap.StatusCode}", ct);
        }
        await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>El trabajo INTERCOMPANY_SYNC de la compañía cliente: manda lo pendiente que ya toca y revisa las entregas que no han terminado.</summary>
    public async Task<string> RunDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        var pending = await db.Set<TenantLinkOrder>().AsNoTracking()
            .Where(o => o.State == TenantLinkOrderStates.Pending && (o.NextAttemptUtc == null || o.NextAttemptUtc <= nowUtc) && (o.ClaimedUntilUtc == null || o.ClaimedUntilUtc < nowUtc))
            .OrderBy(o => o.TenantLinkOrderId).Select(o => o.TenantLinkOrderId).Take(50).ToListAsync(ct);
        var created = 0;
        foreach (var id in pending) if (await PushAsync(id, nowUtc, ct)) created++;
        var open = await db.Set<TenantLinkOrder>().AsNoTracking().Where(o => o.State == TenantLinkOrderStates.Created && !o.IsFinal)
            .OrderBy(o => o.LastSyncedAtUtc).ThenBy(o => o.TenantLinkOrderId).Select(o => o.TenantLinkOrderId).Take(200).ToListAsync(ct);
        var changed = 0;
        foreach (var id in open) if (await SyncAsync(id, nowUtc, ct)) changed++;
        return $"Entregas por compañía enlazada: {created} de {pending.Count} mandada(s); {open.Count} revisada(s), {changed} con estatus nuevo.";
    }

    // ================================================================ pantalla y API (compañía cliente)

    public async Task<TenantLinkDeliveryPageDto> ListAsync(string? state, string? q, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, 200);
        var query = Rows();
        if (!string.IsNullOrWhiteSpace(state))
        {
            var s = state.Trim().ToUpperInvariant();
            if (!TenantLinkOrderStates.All.Contains(s)) throw new ValidationException("state", "El estado debe ser PENDING, CREATED o FAILED.");
            query = query.Where(x => x.Row.State == s);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            query = query.Where(x => x.SalesOrderNumber.Contains(t) || x.PickBatchNumber.Contains(t) || (x.Row.ProviderOrderNumber != null && x.Row.ProviderOrderNumber.Contains(t)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Row.TenantLinkOrderId).Skip(skip).Take(take).ToListAsync(ct);
        var names = await ProviderNamesAsync(rows.Select(r => r.Row.ProviderTenantId), ct);
        return new TenantLinkDeliveryPageDto(rows.Select(r => ToDto(r.Row, names.GetValueOrDefault(r.Row.ProviderTenantId, string.Empty), r.SalesOrderPublicId, r.SalesOrderNumber,
            r.PickBatchPublicId, r.PickBatchNumber, tenant.Lang)).ToList(), total);
    }

    public async Task<TenantLinkDeliveryDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var r = await Rows().FirstOrDefaultAsync(x => x.Row.PublicId == publicId, ct) ?? throw new NotFoundException(TenantLinkRules.DeliveryNotFound, feminine: true);
        var names = await ProviderNamesAsync(new[] { r.Row.ProviderTenantId }, ct);
        return ToDto(r.Row, names.GetValueOrDefault(r.Row.ProviderTenantId, string.Empty), r.SalesOrderPublicId, r.SalesOrderNumber, r.PickBatchPublicId, r.PickBatchNumber, tenant.Lang);
    }

    /// <summary>Reintentar una entrega FAILED (después de corregir lo que dijo la otra compañía): vuelve a PENDING y se manda al instante.</summary>
    public async Task<TenantLinkDeliveryDto> RetryAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.Set<TenantLinkOrder>().AsTracking().FirstOrDefaultAsync(o => o.PublicId == publicId, ct) ?? throw new NotFoundException(TenantLinkRules.DeliveryNotFound, feminine: true);
        if (row.State != TenantLinkOrderStates.Failed) throw new StatusRuleException(TenantLinkRules.RetryOnlyFailed);
        row.State = TenantLinkOrderStates.Pending;
        row.Attempts = 0;
        row.NextAttemptUtc = null;
        row.LastError = null;
        await db.SaveChangesAsync(ct);
        await PushAsync(row.TenantLinkOrderId, DateTime.UtcNow, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>«Revisar ahora»: trae el estatus de la orden de allá sin esperar al trabajo.</summary>
    public async Task<TenantLinkDeliveryDto> SyncNowAsync(Guid publicId, CancellationToken ct)
    {
        var (row, _) = await CreatedWithActiveLinkAsync(publicId, ct);
        await SyncAsync(row.TenantLinkOrderId, DateTime.UtcNow, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>El comprobante de entrega de la orden de allá (en vivo, solo de esa orden), o null si todavía no se registra.</summary>
    public async Task<DeliveryProofDto?> ProofAsync(Guid publicId, CancellationToken ct)
    {
        var (row, link) = await CreatedWithActiveLinkAsync(publicId, ct);
        return await linked.RunAsync<DeliveryProofDto?>(link.ProviderTenantId, link.ProviderClientId!.Value, async (sp, c) =>
        {
            if (await TrySnapshotAsync(sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ILookupCache>(), row.ProviderOrderId!.Value, row.TenantLinkOrderId, c) is null) return null;
            return await sp.GetRequiredService<DeliveryRegistrationService>().FindByOrderAsync(row.ProviderOrderPublicId!.Value, c);
        }, ct);
    }

    /// <summary>Un archivo (firma o foto) del comprobante de entrega de la orden de allá; 404 si no es de ese comprobante.</summary>
    public async Task<StoredFileContent> ProofFileAsync(Guid publicId, Guid filePublicId, CancellationToken ct)
    {
        var (row, link) = await CreatedWithActiveLinkAsync(publicId, ct);
        return await linked.RunAsync(link.ProviderTenantId, link.ProviderClientId!.Value, async (sp, c) =>
        {
            var db2 = sp.GetRequiredService<TeikemDbContext>();
            var lookups2 = sp.GetRequiredService<ILookupCache>();
            if (await TrySnapshotAsync(db2, lookups2, row.ProviderOrderId!.Value, row.TenantLinkOrderId, c) is null) throw new NotFoundException(TenantLinkRules.ProofFileNotFound);
            var owner = await lookups2.TryGetIdAsync(LookupDomains.EntityType, DeliveryProofCodes.EntityType, c);
            var proofIds = db2.Set<DeliveryProof>().AsNoTracking().Where(p => p.TransportOrderId == row.ProviderOrderId && p.IsActive).Select(p => p.DeliveryProofId);
            var file = await db2.Set<StoredFile>().AsNoTracking()
                .Where(f => f.PublicId == filePublicId && f.IsActive && f.OwnerEntityLookupId == owner && proofIds.Contains(f.OwnerId)).FirstOrDefaultAsync(c)
                ?? throw new NotFoundException(TenantLinkRules.ProofFileNotFound);
            var bytes = await sp.GetRequiredService<IFileStore>().ReadAsync(file.TenantId, file.StorageKey, c);
            return new StoredFileContent(file.FileName, file.ContentType, bytes);
        }, ct);
    }

    private async Task<(TenantLinkOrder Row, TenantLink Link)> CreatedWithActiveLinkAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.Set<TenantLinkOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.PublicId == publicId, ct) ?? throw new NotFoundException(TenantLinkRules.DeliveryNotFound, feminine: true);
        if (row.State != TenantLinkOrderStates.Created || row.ProviderOrderId is null) throw new StatusRuleException(TenantLinkRules.NotCreatedYet);
        var link = await db.Set<TenantLink>().AsNoTracking().FirstAsync(l => l.TenantLinkId == row.TenantLinkId && l.ClientTenantId == row.TenantId, ct);
        if (link.Status != TenantLinkStatuses.Active || link.ProviderClientId is null)
        {
            var name = (await ProviderNamesAsync(new[] { link.ProviderTenantId }, ct)).GetValueOrDefault(link.ProviderTenantId, string.Empty);
            throw new StatusRuleException(TenantLinkRules.LinkInactive(name));
        }
        return (row, link);
    }

    private sealed class RowView
    {
        public TenantLinkOrder Row { get; init; } = null!;
        public Guid SalesOrderPublicId { get; init; }
        public string SalesOrderNumber { get; init; } = string.Empty;
        public Guid PickBatchPublicId { get; init; }
        public string PickBatchNumber { get; init; } = string.Empty;
    }

    private IQueryable<RowView> Rows()
        => from o in db.Set<TenantLinkOrder>().AsNoTracking()
           join s in db.Set<SalesOrder>().AsNoTracking() on o.SalesOrderId equals s.SalesOrderId
           join b in db.Set<PickBatch>().AsNoTracking() on o.PickBatchId equals b.PickBatchId
           select new RowView { Row = o, SalesOrderPublicId = s.PublicId, SalesOrderNumber = s.Number, PickBatchPublicId = b.PublicId, PickBatchNumber = b.Number };

    private async Task<Dictionary<int, string>> ProviderNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Tenants.AsNoTracking().Where(t => list.Contains(t.TenantId)).ToDictionaryAsync(t => t.TenantId, t => t.Name, ct);
    }

    public static TenantLinkDeliveryDto ToDto(TenantLinkOrder r, string providerName, Guid salesOrderPublicId, string salesOrderNumber, Guid pickBatchPublicId, string pickBatchNumber, string lang)
        => new(r.PublicId, r.State, r.LastError, r.Attempts, providerName, r.ProviderOrderPublicId, r.ProviderOrderNumber, r.ProviderStatusCode,
            r.ProviderStatusLabelJson is null ? null : MultilingualText.Resolve(r.ProviderStatusLabelJson, lang), r.IsFinal, r.DeliveredAtUtc, r.DeliveredTo, r.LastSyncedAtUtc,
            r.CreatedAtUtc, salesOrderPublicId, salesOrderNumber, pickBatchPublicId, pickBatchNumber);

    private async Task NotifyAsync(string eventCode, TenantLinkOrder row, Payload payload, string providerName, Dictionary<string, string?> extra, string dedup, CancellationToken ct)
    {
        if (notices is null) return;
        var values = new Dictionary<string, string?>
        {
            ["salesOrder"] = payload.SalesOrderNumber, ["dispatch"] = payload.PickBatchNumber, ["company"] = providerName, ["orderNumber"] = row.ProviderOrderNumber ?? string.Empty,
        };
        foreach (var kv in extra) values[kv.Key] = kv.Value;
        await notices.PublishAsync(eventCode, values, dedup, ct);
    }
}
