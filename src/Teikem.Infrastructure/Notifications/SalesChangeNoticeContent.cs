using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Lote 249 (Lote E) — contenido de los avisos de las modificaciones de órdenes de venta (SALES_ORDER_CHANGED, SALES_ORDER_ADDITION_CREATED, SALES_PACK_REOPENED y
/// SALES_CHANGE_APPROVAL_PENDING): los destinatarios del evento y las variables con un documento real para la vista previa. El texto sale con la plantilla de
/// omisión (BuildAsync devuelve null: el aviso ya trae sus variables al publicarse). Nunca va al cliente.
/// <list type="bullet">
/// <item>SELLER = el vendedor congelado de la orden (SalesOrderSeller, Lote 244), solo si es persona activa.</item>
/// <item>REGISTRANT = quien registró la modificación (SalesOrderChange.RegisteredBy); en «Orden de venta modificada» solo cuando se aplicó al APROBARLA (quien la
/// aplicó al registrarla ya lo sabe).</item>
/// <item>ASSIGNEE = el encargado del almacén de la orden (SalesOrder.AssignedToUserId), solo si es persona activa.</item>
/// </list>
/// SOLO LECTURA: cada llamada abre su PROPIO scope como la compañía SIN usuario (TenantContext.AsAnonymous), lee con AsNoTracking y filtra TenantId == tenantId
/// además del filtro global.
/// </summary>
public sealed class SalesChangeNoticeContent(IServiceScopeFactory scopes) : INotificationContentProvider
{
    public const string NoSellerReason = SalesOrderEmailContent.NoSellerReason;
    public const string NoAssigneeReason = "La orden no tiene un encargado del almacén con usuario activo en la compañía.";
    public const string NoRegistrantReason = "Quien registró la modificación ya no es una persona activa de la compañía.";
    public const string AppliedOnRegisterReason = "La modificación se aplicó al registrarla: quien la registró ya lo sabe.";
    public const string NotFoundWhat = "Orden de venta o modificación";

    public IReadOnlyCollection<string> EventCodes { get; } = SalesChangeEventCodes.All;
    public string? ReferencePermission => PermissionCatalog.SalesView;
    public string? ReferenceModule => ModuleKeys.SalesOrders;

    private static bool Handles(string? eventCode) => eventCode is not null && SalesChangeEventCodes.All.Contains(eventCode, StringComparer.Ordinal);

    // ---------------------------------------------------------------- scope propio de solo lectura

    private async Task<T> InScopeAsync<T>(int tenantId, Func<IServiceProvider, TeikemDbContext, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var context = sp.GetRequiredService<TenantContext>();
        using var asTenant = context.AsAnonymous(tenantId);
        var db = sp.GetRequiredService<TeikemDbContext>();
        var previousTracking = db.ChangeTracker.QueryTrackingBehavior;
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        try
        {
            return await work(sp, db);
        }
        finally
        {
            db.ChangeTracker.QueryTrackingBehavior = previousTracking;
        }
    }

    private static async Task<SalesOrder?> OrderAsync(TeikemDbContext db, int tenantId, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        if (values.TryGetValue("orderPublicId", out var pid) && Guid.TryParse(pid, out var publicId))
            return await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.PublicId == publicId && o.TenantId == tenantId, ct);
        if (values.TryGetValue("orderNumber", out var number) && !string.IsNullOrWhiteSpace(number))
        {
            var n = number.Trim();
            return await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Number == n && o.TenantId == tenantId, ct);
        }
        return null;
    }

    private static async Task<SalesOrderChange?> ChangeAsync(TeikemDbContext db, int tenantId, IReadOnlyDictionary<string, string> values, CancellationToken ct)
        => values.TryGetValue("changePublicId", out var cid) && Guid.TryParse(cid, out var changeId)
            ? await db.Set<SalesOrderChange>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == changeId && c.TenantId == tenantId, ct)
            : null;

    // ---------------------------------------------------------------- variables con un documento real

    /// <summary>
    /// Las variables del aviso para la vista previa: reference = el PublicId de una modificación (las del registro: quién, quién autorizó, motivo, resumen y
    /// totales) o el de una orden / su número (las de la orden; en una modificación, la más reciente de esa orden).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ValuesForReferenceAsync(int tenantId, string eventCode, string reference, CancellationToken ct)
    {
        if (!Handles(eventCode)) throw new InvalidOperationException($"El contenido de las modificaciones de órdenes de venta no atiende el evento '{eventCode}'.");
        var r = (reference ?? string.Empty).Trim();
        return await InScopeAsync(tenantId, async (sp, db) =>
        {
            SalesOrderChange? change = null;
            SalesOrder? order = null;
            if (Guid.TryParse(r, out var g))
            {
                change = await db.Set<SalesOrderChange>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == g && c.TenantId == tenantId, ct);
                order = change is not null
                    ? await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.SalesOrderId == change.SalesOrderId && o.TenantId == tenantId, ct)
                    : await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.PublicId == g && o.TenantId == tenantId, ct);
            }
            else if (r.Length > 0)
            {
                var number = r.ToUpperInvariant();
                order = await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Number == number && o.TenantId == tenantId, ct);
            }
            if (order is null) throw new NotFoundException(NotFoundWhat, feminine: true);
            change ??= eventCode == SalesChangeEventCodes.PackReopened ? null
                : await db.Set<SalesOrderChange>().AsNoTracking().Where(c => c.SalesOrderId == order.SalesOrderId && c.TenantId == tenantId)
                    .OrderByDescending(c => c.ChangeNo).FirstOrDefaultAsync(ct);

            var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == order.ClientId && c.TenantId == tenantId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
            var warehouse = await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == order.WarehouseId && w.TenantId == tenantId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["orderNumber"] = order.Number, ["client"] = client, ["warehouse"] = warehouse, ["orderPublicId"] = order.PublicId.ToString("N"),
            };
            if (change is not null)
            {
                foreach (var kv in await sp.GetRequiredService<SalesOrderService>().ChangeNoticeValuesAsync(change, ct)) values[kv.Key] = kv.Value ?? string.Empty;
                if (change.AdditionSalesOrderId is int aid)
                {
                    var add = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.SalesOrderId == aid && o.TenantId == tenantId).Select(o => new { o.Number, o.PublicId }).FirstOrDefaultAsync(ct);
                    values["additionNumber"] = add?.Number ?? string.Empty;
                    values["additionPublicId"] = add?.PublicId.ToString("N") ?? string.Empty;
                }
                values["approvalRule"] = SalesOrderChangeService.ApprovalRuleText(change.ApprovalRule, null);
                values["approvedBy"] = string.Empty;
                values["warehouseImpact"] = string.Empty;
            }
            return (IReadOnlyDictionary<string, string>)values;
        });
    }

    // ---------------------------------------------------------------- destinatarios del evento

    public async Task<NotificationContextResolution> RecipientsAsync(int tenantId, string eventCode, string contextKey, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        if (!Handles(eventCode)) return NotificationContextResolution.Empty(null);
        var key = (contextKey ?? string.Empty).Trim().ToUpperInvariant();
        if (key is not (SalesChangeContextRecipients.Seller or SalesChangeContextRecipients.Registrant or SalesChangeContextRecipients.Assignee))
            return NotificationContextResolution.Empty(null);
        return await InScopeAsync(tenantId, async (_, db) =>
        {
            var order = await OrderAsync(db, tenantId, values, ct);
            if (order is null) return NotificationContextResolution.Empty(null);
            switch (key)
            {
                case SalesChangeContextRecipients.Seller:
                {
                    var seller = await SalesOrderSeller.ResolveAsync(db, tenantId, order, ct);
                    return seller.Users.Count == 0 ? NotificationContextResolution.Empty(NoSellerReason) : new NotificationContextResolution(seller.Users, null);
                }
                case SalesChangeContextRecipients.Assignee:
                    return order.AssignedToUserId is int assignee
                        ? await PeopleAsync(db, tenantId, new[] { assignee }, NoAssigneeReason, ct)
                        : NotificationContextResolution.Empty(NoAssigneeReason);
                default:
                {
                    var change = await ChangeAsync(db, tenantId, values, ct);
                    if (change is null || change.SalesOrderId != order.SalesOrderId) return NotificationContextResolution.Empty(null);
                    if (eventCode == SalesChangeEventCodes.OrderChanged && change.DecidedBy is null) return NotificationContextResolution.Empty(AppliedOnRegisterReason);
                    return await PeopleAsync(db, tenantId, new[] { change.RegisteredBy }, NoRegistrantReason, ct);
                }
            }
        });
    }

    /// <summary>Las personas ACTIVAS (usuario activo con membresía ACTIVE en la compañía) de esos usuarios; sin ninguna, el motivo.</summary>
    private static async Task<NotificationContextResolution> PeopleAsync(TeikemDbContext db, int tenantId, IReadOnlyList<int> ids, string emptyReason, CancellationToken ct)
    {
        var active = await SalesSellerQueries.ActiveMemberIdsAsync(db, tenantId, ids, ct);
        var wanted = ids.Where(active.Contains).Distinct().ToList();
        if (wanted.Count == 0) return NotificationContextResolution.Empty(emptyReason);
        var users = await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber }).ToListAsync(ct);
        var people = users.Select(u => new NotificationContextPerson(u.Id, string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName.Trim(), u.Email, u.PhoneNumber,
            NotificationAudience.Staff, null)).ToList();
        return people.Count == 0 ? NotificationContextResolution.Empty(emptyReason) : new NotificationContextResolution(people, null);
    }

    // ---------------------------------------------------------------- el contenido al enviar

    /// <summary>Sin contenido propio: el aviso sale con las variables que trae al publicarse y la plantilla (de la regla o la de omisión).</summary>
    public Task<NotificationContent?> BuildAsync(NotificationContentRequest req, CancellationToken ct) => Task.FromResult<NotificationContent?>(null);

    // ---------------------------------------------------------------- ejemplo (sin base de datos)

    /// <summary>La modificación de ejemplo para «Enviar prueba» y la vista previa sin documento real.</summary>
    public NotificationContent Sample(string eventCode, NotificationAudience audience, string lang)
    {
        var en = string.Equals(lang?.Trim(), "en", StringComparison.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orderNumber"] = "OV-00040",
            ["client"] = "Farmacia Central",
            ["warehouse"] = en ? "Main warehouse" : "Almacén principal",
            ["orderPublicId"] = "0f8fad5bd9cb469fa16570867728950e",
            ["changeNo"] = "2",
            ["changePublicId"] = "6f9619ff8b86d011b42d00c04fc964ff",
            ["registeredBy"] = "Ana Pérez",
            ["authorizedBy"] = en ? "María López (purchasing)" : "María López (compras)",
            ["channel"] = en ? "By phone" : "Por teléfono",
            ["reason"] = en ? "The customer asked for 5 more boxes." : "El cliente pidió 5 cajas más.",
            ["summary"] = en ? "Changed: MED-10 (quantity 10 → 15). Total 1200.00 → 1450.00." : "Cambió: MED-10 (cantidad 10 → 15). Total 1200.00 → 1450.00.",
            ["totalBefore"] = "$1,200.00",
            ["totalAfter"] = "$1,450.00",
            ["totalDelta"] = "+$250.00",
            ["approvedBy"] = "Luis Rivera",
            ["warehouseImpact"] = en ? "No changes in the warehouse." : "Sin cambios en el almacén.",
            ["additionNumber"] = "OV-00045",
            ["additionPublicId"] = "7c9e6679742540de944be07fc1f90ae7",
            ["approvalRule"] = en ? "the change exceeds $500.00" : "el cambio pasa de $500.00",
            ["batchNumber"] = "EMP-00031",
            ["shipmentNumber"] = "ORD-01234",
            ["reopenedBy"] = "Ana Pérez",
        };
        return new NotificationContent(values, new Dictionary<string, NotificationBlock>(), Array.Empty<EmailAttachment>());
    }
}
