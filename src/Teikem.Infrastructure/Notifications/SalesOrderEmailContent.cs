using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Tax;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Quién es el vendedor de una orden para el correo: el nombre que se muestra, quién la capturó (si es otra persona) y los usuarios que reciben «Vendedor de la orden».</summary>
public sealed record SalesOrderSellerInfo(string? Name, string? CapturedByName, IReadOnlyList<NotificationContextPerson> Users);

/// <summary>
/// Lote 243 — el vendedor de una orden de venta, en UN solo lugar (el Lote A lo cambiará cuando la orden guarde su vendedor):
/// - Nombre: el representante del cliente (Client.SalesRepId → SalesRep activo) si existe; si no, quien la solicitó (RequestedBy, o CreatedBy).
/// - «Capturó: X»: quien la solicitó, cuando es otra persona que el representante.
/// - Usuarios de «Vendedor de la orden»: quien la solicitó y el usuario del representante si es otro; solo ACTIVOS y MIEMBROS de la compañía.
/// Solo lectura (AsNoTracking) y con TenantId explícito además del filtro global.
/// </summary>
public static class SalesOrderSeller
{
    public static async Task<SalesOrderSellerInfo> ResolveAsync(TeikemDbContext db, int tenantId, SalesOrder order, CancellationToken ct)
    {
        var requester = order.RequestedBy ?? order.CreatedBy;
        var repId = await db.Clients.AsNoTracking().Where(c => c.ClientId == order.ClientId && c.TenantId == tenantId).Select(c => c.SalesRepId).FirstOrDefaultAsync(ct);
        var rep = repId is int r
            ? await db.Set<SalesRep>().AsNoTracking().FirstOrDefaultAsync(x => x.SalesRepId == r && x.TenantId == tenantId && x.IsActive, ct)
            : null;
        var ids = new List<int>();
        if (requester is int q) ids.Add(q);
        if (rep?.UserId is int ru && ru != requester) ids.Add(ru);
        var users = ids.Count == 0 ? new()
            : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber, u.IsActive }).ToListAsync(ct);
        var members = ids.Count == 0 ? new List<int>()
            : await db.UserTenants.AsNoTracking()
                .Where(m => m.TenantId == tenantId && ids.Contains(m.UserId)
                    // solo la membresía ACTIVE: un suspendido en la compañía conserva la fila y el usuario activo, pero ya no tiene acceso
                    && db.StatusCodes.Any(s => s.StatusCodeId == m.StatusCodeId && s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active))
                .Select(m => m.UserId).Distinct().ToListAsync(ct);

        string? NameOf(int? id)
        {
            var u = id is int x ? users.FirstOrDefault(y => y.Id == x) : null;
            return u is null ? null : string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName;
        }
        var requesterName = NameOf(requester);
        var name = string.IsNullOrWhiteSpace(rep?.Name) ? requesterName : rep!.Name.Trim();
        string? captured = null;
        if (rep is not null && !string.IsNullOrWhiteSpace(requesterName))
        {
            var other = rep.UserId is int repUser ? repUser != requester : !string.Equals(rep.Name?.Trim(), requesterName, StringComparison.OrdinalIgnoreCase);
            if (other) captured = requesterName;
        }
        var people = ids.Select(id => users.FirstOrDefault(u => u.Id == id))
            .Where(u => u is not null && u.IsActive && members.Contains(u.Id))
            .Select(u => new NotificationContextPerson(u!.Id, string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName, u.Email, u.PhoneNumber, NotificationAudience.Staff, null))
            .ToList();
        return new SalesOrderSellerInfo(name, captured, people);
    }
}

/// <summary>
/// Lote 243 (Lote G del plan de Solutions) — contenido del aviso «Orden de venta solicitada» (SALES_ORDER_REQUESTED): la orden completa en
/// el correo (bloque {orderDetail}, como el de Formsite), las variables que se arman al enviar, la firma adjunta y los destinatarios del
/// evento SELLER («Vendedor de la orden») y CLIENT («Cliente de la orden»).
/// <para>
/// SOLO LECTURA: cada llamada abre su PROPIO scope como la compañía SIN usuario (TenantContext.AsAnonymous), con el DbContext del scope en
/// NoTracking (así lo que SalesOrderService.GetAsync lee nunca queda rastreado ni llega al SaveChanges del procesador de avisos) y con
/// TenantId == tenantId explícito además del filtro global. Las etiquetas de catálogo salen en el idioma del correo.
/// </para>
/// Estatus al enviar: CANCELLED → «La orden {n} se canceló antes de enviar el aviso.»; DRAFT → «La orden {n} sigue en borrador.» (SKIPPED);
/// la orden no existe (o es de otra compañía) → null (el aviso sale con lo que trae). La firma que no se puede leer no detiene el correo.
/// </summary>
/// <remarks>
/// Alcance de datos: el scope propio hereda el alcance de almacén y cliente (UserDataScope) de quien llama (<c>caller</c>). En el
/// procesador de avisos no hay usuario y el alcance es null (no restringe); en la vista previa y en «Enviar prueba» con una orden real, una
/// orden fuera del alcance del usuario da 404 igual que GET /sales-orders/{id}.
/// </remarks>
public sealed class SalesOrderEmailContent(IServiceScopeFactory scopes, IConfiguration config, ILogger<SalesOrderEmailContent> logger, TenantContext? caller = null) : INotificationContentProvider
{
    public const string NotFoundWhat = "Orden de venta";
    public const string NoSellerReason = "La orden no tiene un vendedor con usuario activo en la compañía.";
    public const string NoClientEmailReason = "El cliente no tiene correo principal ni contactos marcados con correo.";
    public static string CancelledReason(string number) => $"La orden {number} se canceló antes de enviar el aviso.";
    public static string DraftReason(string number) => $"La orden {number} sigue en borrador.";

    /// <summary>Nombre del adjunto de la firma: «firma-OV-00012.png».</summary>
    public static string SignatureFileName(string orderNumber, string? contentType)
        => $"firma-{orderNumber}.{(string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png")}";

    public IReadOnlyCollection<string> EventCodes { get; } = new[] { SalesEventCodes.OrderRequested };
    public string? ReferencePermission => PermissionCatalog.SalesView;
    public string? ReferenceModule => ModuleKeys.SalesOrders;

    private bool Handles(string? eventCode) => string.Equals(eventCode, SalesEventCodes.OrderRequested, StringComparison.Ordinal);

    // ---------------------------------------------------------------- scope propio de solo lectura

    private async Task<T> InScopeAsync<T>(int tenantId, string? lang, Func<IServiceProvider, TeikemDbContext, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var context = sp.GetRequiredService<TenantContext>();
        using var asTenant = context.AsAnonymous(tenantId);
        var previousLang = context.Lang;
        // el alcance de datos de quien llama (almacén y cliente) pasa al scope propio: el filtro global de SalesOrder lo aplica
        var (previousWarehouses, previousClients) = (context.ScopedWarehouseIds, context.ScopedClientIds);
        if (caller is not null && !ReferenceEquals(caller, context))
            (context.ScopedWarehouseIds, context.ScopedClientIds) = (caller.ScopedWarehouseIds, caller.ScopedClientIds);
        if (!string.IsNullOrWhiteSpace(lang)) context.Lang = lang;
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
            context.Lang = previousLang;
            (context.ScopedWarehouseIds, context.ScopedClientIds) = (previousWarehouses, previousClients);
        }
    }

    /// <summary>La orden del aviso: por orderPublicId; si no viene (avisos anteriores al Lote 243), por orderNumber.</summary>
    private static async Task<SalesOrder?> FindOrderAsync(TeikemDbContext db, int tenantId, IReadOnlyDictionary<string, string> values, CancellationToken ct)
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

    private static Task<Tenant?> TenantAsync(TeikemDbContext db, int tenantId, CancellationToken ct)
        => db.Tenants.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

    private static string LangOf(string? lang) => string.Equals(lang?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";

    // ---------------------------------------------------------------- variables con un documento real

    public async Task<IReadOnlyDictionary<string, string>> ValuesForReferenceAsync(int tenantId, string eventCode, string reference, CancellationToken ct)
    {
        if (!Handles(eventCode)) throw new InvalidOperationException($"El contenido de la orden de venta no atiende el evento '{eventCode}'.");
        var r = (reference ?? string.Empty).Trim();
        var byGuid = Guid.TryParse(r, out var publicId);
        return await InScopeAsync(tenantId, null, async (_, db) =>
        {
            SalesOrder? order = null;
            if (byGuid) order = await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.PublicId == publicId && o.TenantId == tenantId, ct);
            else if (r.Length > 0)
            {
                var number = r.ToUpperInvariant();
                order = await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Number == number && o.TenantId == tenantId, ct);
            }
            if (order is null) throw byGuid || r.Length == 0 ? new NotFoundException(NotFoundWhat, feminine: true) : new NotFoundException(NotFoundWhat, r, feminine: true);

            var lines = await db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == order.SalesOrderId && l.TenantId == tenantId).ToListAsync(ct);
            var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == order.ClientId && c.TenantId == tenantId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
            var warehouse = await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == order.WarehouseId && w.TenantId == tenantId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
            var backordered = lines.Count(l => l.QtyBackordered > 0m);
            var total = SalesOrderExtraRules.Total(order.Subtotal, order.TaxAmount, order.ShippingCharge, order.ShippingTaxAmount);
            // como si el evento se hubiera publicado ahora (misma huella: no sale el aviso de «la orden cambió»)
            return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["orderNumber"] = order.Number,
                ["client"] = client,
                ["warehouse"] = warehouse,
                ["orderPublicId"] = order.PublicId.ToString("N"),
                ["lineCount"] = lines.Count.ToString(CultureInfo.InvariantCulture),
                ["detail"] = backordered == 0 ? "Quedó todo reservado." : $"{backordered} línea(s) con faltante en backorder.",
                ["orderFingerprint"] = SalesOrderEmailRules.Fingerprint(lines.Select(l => (l.LineNo, l.ProductId, l.QtyOrdered, l.UnitPrice)), total),
            };
        });
    }

    // ---------------------------------------------------------------- destinatarios del evento

    public async Task<NotificationContextResolution> RecipientsAsync(int tenantId, string eventCode, string contextKey, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        if (!Handles(eventCode)) return NotificationContextResolution.Empty(null);
        var key = (contextKey ?? string.Empty).Trim().ToUpperInvariant();
        if (key is not (SalesContextRecipients.Seller or SalesContextRecipients.Client)) return NotificationContextResolution.Empty(null);
        return await InScopeAsync(tenantId, null, async (sp, db) =>
        {
            var order = await FindOrderAsync(db, tenantId, values, ct);
            if (order is null) return NotificationContextResolution.Empty(null);
            if (key == SalesContextRecipients.Seller)
            {
                var seller = await SalesOrderSeller.ResolveAsync(db, tenantId, order, ct);
                return seller.Users.Count == 0 ? NotificationContextResolution.Empty(NoSellerReason) : new NotificationContextResolution(seller.Users, null);
            }
            var lookups = sp.GetRequiredService<ILookupCache>();
            var emails = await ClientContactQueries.NoticeEmailsAsync(db, lookups, tenantId, order.ClientId, ct);
            if (emails.Count == 0) return NotificationContextResolution.Empty(NoClientEmailReason);
            var noticeLang = await db.Clients.AsNoTracking().Where(c => c.ClientId == order.ClientId && c.TenantId == tenantId).Select(c => c.NoticeLang).FirstOrDefaultAsync(ct);
            var lang = string.IsNullOrWhiteSpace(noticeLang) ? (await TenantAsync(db, tenantId, ct))?.DefaultLangCode : noticeLang;
            return new NotificationContextResolution(emails.Select(e => new NotificationContextPerson(null, e.Name, e.Email, null, NotificationAudience.Customer, LangOf(lang))).ToList(), null);
        });
    }

    // ---------------------------------------------------------------- el contenido al enviar

    public async Task<NotificationContent?> BuildAsync(NotificationContentRequest req, CancellationToken ct)
    {
        if (!Handles(req.EventCode)) return null;
        var lang = LangOf(req.Lang);
        return await InScopeAsync(req.TenantId, lang, async (sp, db) =>
        {
            var tenantId = req.TenantId;
            var order = await FindOrderAsync(db, tenantId, req.Values, ct);
            if (order is null) return null;
            var status = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == order.StatusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct);
            if (status == SalesOrderStatuses.Cancelled) return NotificationContent.Skip(CancelledReason(order.Number));
            if (status == SalesOrderStatuses.Draft) return NotificationContent.Skip(DraftReason(order.Number));

            SalesOrderDto dto;
            try { dto = await sp.GetRequiredService<SalesOrderService>().GetAsync(order.PublicId, ct); }
            catch (NotFoundException) { return null; }

            var tenant = await TenantAsync(db, tenantId, ct);
            var format = tenant is null ? TenantFormatRules.Defaults(TenantFormatRules.DefaultRegion) : TenantFormatRules.Read(tenant);
            var lookups = sp.GetRequiredService<ILookupCache>();
            var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == order.ClientId && c.TenantId == tenantId, ct);
            // sin la ficha del cliente (vista previa de quien no tiene clients.read): ni dirección, ni teléfono, ni registro de comerciante
            var card = req.IncludeClientCard
                ? await ClientContactQueries.CardAsync(db, lookups, tenantId, order.ClientId, ct, d => TenantFormatText.Phone(d, format))
                : ClientCard.Empty;
            var consignee = dto.ConsigneePublicId is Guid cg
                ? await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.PublicId == cg && l.TenantId == tenantId, ct)
                : null;
            var seller = await SalesOrderSeller.ResolveAsync(db, tenantId, order, ct);

            // desglose del IVU guardado por componente (estatal, municipal) y huella vigente
            var lines = await db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == order.SalesOrderId && l.TenantId == tenantId).ToListAsync(ct);
            var rateIds = lines.Where(l => l.TaxRateId != null).Select(l => l.TaxRateId!.Value).Distinct().ToList();
            var rates = rateIds.Count == 0 ? new Dictionary<int, TaxRateInfo>()
                : (await db.TaxRates.AsNoTracking().Include(t => t.Components).Where(t => rateIds.Contains(t.TaxRateId) && t.TenantId == tenantId).ToListAsync(ct))
                    .ToDictionary(t => t.TaxRateId, t => new TaxRateInfo(t.TaxRateId, t.Code, t.Name, t.Percent,
                        t.Components.OrderBy(c => c.SortOrder).ThenBy(c => c.TaxRateComponentId).Select(c => new TaxComponentInfo(c.Code, c.Name, c.Percent)).ToList()));
            var terms = dto.Terms;
            var taxRows = TaxBreakdownRules.Compute(lines.Select(l => new TaxBreakdownLine(l.TaxRateId, l.TaxRatePct, l.TaxAmount, l.IsTaxExempt)).ToList(),
                terms?.ShippingTaxAmount ?? 0m, rates, format.CurrencyDecimals);
            var fingerprint = SalesOrderEmailRules.Fingerprint(lines.Select(l => (l.LineNo, l.ProductId, l.QtyOrdered, l.UnitPrice)), dto.Total);
            req.Values.TryGetValue("orderFingerprint", out var published);

            var staff = req.Audience == NotificationAudience.Staff;
            var signature = await SignatureAsync(sp, db, tenantId, order, ct);
            var link = staff ? OrderLink(order.PublicId) : null;
            var model = new SalesOrderEmailModel
            {
                Lang = lang, Audience = req.Audience, Format = format, CompanyName = tenant?.Name ?? string.Empty,
                OrderNumber = dto.Number, IsUrgent = terms?.IsUrgent ?? order.IsUrgent, OrderDate = dto.RequestedDate, RequestedAtUtc = dto.RequestedAtUtc,
                SellerName = seller.Name, CapturedByName = seller.CapturedByName,
                ClientName = dto.ClientName, ClientCode = client?.Code, ClientAddress = card.Address, ClientPhone = card.Phone, MerchantRegistration = req.IncludeClientCard ? client?.MerchantRegistration : null,
                ClientPendingApproval = terms?.AwaitingClientApproval ?? false,
                DeliveryMode = terms?.DeliveryMode ?? ShippingRules.EffectiveMode(order.DeliveryMode, order.ConsigneeId is not null),
                ConsigneeName = dto.ConsigneeName, ConsigneeAddress = ClientContactQueries.AddressLines(consignee), WarehouseName = dto.WarehouseName,
                PaymentMethod = terms?.PaymentMethodLabel, PaymentTerm = terms?.PaymentTermLabel, PaymentChanged = terms?.PaymentChanged ?? false,
                IsConsignment = terms?.IsConsignment ?? false, RequiredDate = dto.RequiredDate, Notes = dto.Notes, SubmittedWithAuth = terms?.SubmittedWithAuth,
                Lines = dto.Lines.OrderBy(l => l.LineNo).Select(l => new SalesOrderEmailLine(l.LineNo, l.Sku, l.ProductName, l.QtyOrdered, l.UnitPrice, l.LineSubtotal,
                    l.IsTaxExempt, l.IsClosed ? 0m : l.QtyBackordered,
                    (l.Components ?? new List<SalesOrderLineComponentDto>()).Select(c => new SalesOrderEmailComponent(c.Sku, c.Name, c.QtyPerKit, c.Needed, c.SubstitutesSku)).ToList())).ToList(),
                Subtotal = dto.Subtotal, ShippingCharge = terms?.ShippingCharge ?? 0m, ShippingReason = terms?.ShippingReasonLabel,
                Taxes = ToEmailTaxes(taxRows, lang), Total = dto.Total,
                AuthorizedByName = terms?.AuthorizedByName, HasSignature = signature is not null,
                Link = link, ChangedSinceRequest = staff && SalesOrderEmailRules.Changed(published, fingerprint),
            };
            return Content(req.Values, model, link, order.PublicId, signature);
        });
    }

    /// <summary>Renglones del IVU con su etiqueta en el idioma del correo (el porcentaje solo en los componentes).</summary>
    public static IReadOnlyList<SalesOrderEmailTax> ToEmailTaxes(IReadOnlyList<TaxBreakdownRow> rows, string lang)
        => rows.Select(r => new SalesOrderEmailTax(TaxBreakdownRules.Label(r, lang), r.Kind == TaxBreakdownKind.Component ? r.Percent : null, r.Amount, r.IsExempt)).ToList();

    /// <summary>Las variables (las del aviso más las que arma el contenido; gana el contenido), el bloque {orderDetail} y el adjunto de la firma.</summary>
    private static NotificationContent Content(IReadOnlyDictionary<string, string> payload, SalesOrderEmailModel m, string? link, Guid orderPublicId, EmailAttachment? signature)
    {
        var values = new Dictionary<string, string>(payload, StringComparer.Ordinal)
        {
            ["orderNumber"] = m.OrderNumber,
            ["client"] = m.ClientName,
            ["warehouse"] = m.WarehouseName,
            ["orderPublicId"] = orderPublicId.ToString("N"),
            ["lineCount"] = m.Lines.Count.ToString(CultureInfo.InvariantCulture),
            ["orderDate"] = TenantFormatText.Date(m.OrderDate, m.Format),
            ["seller"] = m.SellerName ?? string.Empty,
            ["total"] = TenantFormatText.Money(m.Total, m.Format),
            ["deliveryMode"] = SalesOrderEmailHtml.DeliveryText(m),
            ["paymentMethod"] = SalesOrderEmailHtml.PaymentText(m) ?? string.Empty,
            ["orderLink"] = m.IsStaff && SalesOrderEmailHtml.IsSafeLink(link) ? link! : string.Empty,
        };
        // la copia al cliente no lleva las marcas internas del aviso ({detail} dice el faltante en backorder): se VACÍA (no se quita),
        // porque el motor mezcla el payload con lo del contenido y una clave quitada volvería con su valor
        if (!m.IsStaff) values["detail"] = string.Empty;
        var blocks = new Dictionary<string, NotificationBlock>(StringComparer.Ordinal)
        {
            ["orderDetail"] = new NotificationBlock(SalesOrderEmailHtml.Render(m), SalesOrderEmailHtml.Summary(m)),
        };
        return new NotificationContent(values, blocks, signature is null ? Array.Empty<EmailAttachment>() : new[] { signature });
    }

    /// <summary>Enlace a la orden en la web: Notifications:WebBaseUrl o, si falta, Auth:PasswordReset:WebBaseUrl. Sin ninguna (o no http/https) = sin botón.</summary>
    public string? OrderLink(Guid orderPublicId)
    {
        var baseUrl = config["Notifications:WebBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = config["Auth:PasswordReset:WebBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        var link = $"{baseUrl.Trim().TrimEnd('/')}/sales-orders?order={orderPublicId:N}";
        return SalesOrderEmailHtml.IsSafeLink(link) ? link : null;
    }

    /// <summary>La firma de quien autorizó como adjunto. Si no hay almacén, el archivo no existe o no se puede leer: sin adjunto y una advertencia (nunca falla el correo).</summary>
    private async Task<EmailAttachment?> SignatureAsync(IServiceProvider sp, TeikemDbContext db, int tenantId, SalesOrder order, CancellationToken ct)
    {
        if (order.SignatureFileId is not int fileId) return null;
        try
        {
            var file = await db.Set<StoredFile>().AsNoTracking().FirstOrDefaultAsync(f => f.StoredFileId == fileId && f.TenantId == tenantId && f.IsActive, ct);
            if (file is null)
            {
                logger.LogWarning("La firma de la orden {OrderNumber} (compañía {TenantId}) no está entre los archivos activos; el correo sale sin adjunto.", order.Number, tenantId);
                return null;
            }
            var store = sp.GetService<IFileStore>();
            if (store is null || !store.IsConfigured)
            {
                logger.LogWarning("No hay almacén de archivos configurado: la firma de la orden {OrderNumber} (compañía {TenantId}) no se adjunta.", order.Number, tenantId);
                return null;
            }
            var bytes = await store.ReadAsync(file.TenantId, file.StorageKey, ct);
            if (bytes.Length == 0) return null;
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/png" : file.ContentType;
            return new EmailAttachment(SignatureFileName(order.Number, contentType), contentType, bytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo leer la firma de la orden {OrderNumber} (compañía {TenantId}); el correo sale sin adjunto.", order.Number, tenantId);
            return null;
        }
    }

    // ---------------------------------------------------------------- ejemplo (sin base de datos)

    /// <summary>PNG de 1 × 1 de la firma del ejemplo.</summary>
    private static readonly byte[] SamplePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// <summary>Identificador fijo de la orden del ejemplo.</summary>
    public static readonly Guid SampleOrderPublicId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    /// <summary>
    /// La orden de ejemplo para «Enviar prueba» y la vista previa sin documento real: Farmacia Central, 2 líneas y un kit con 2 componentes,
    /// IVU 10.5 % + 1 % y con firma.
    /// </summary>
    public NotificationContent Sample(string eventCode, NotificationAudience audience, string lang)
    {
        var l = LangOf(lang);
        var en = l == "en";
        var format = TenantFormatRules.Defaults(TenantFormatRules.DefaultRegion);
        var rate = new TaxRateInfo(1, "IVU", "IVU", 11.5m, new[]
        {
            new TaxComponentInfo("STATE", en ? "State tax" : "IVU estatal", 10.5m),
            new TaxComponentInfo("CITY", en ? "Municipal tax" : "IVU municipal", 1m),
        });
        var lines = new[]
        {
            new SalesOrderEmailLine(1, "MED-500", en ? "Acetaminophen 500 mg (box of 100)" : "Acetaminofén 500 mg (caja de 100)", 3m, 12.50m, 37.50m),
            new SalesOrderEmailLine(2, "VIT-C", en ? "Vitamin C 1000 mg" : "Vitamina C 1000 mg", 2m, 8.75m, 17.50m),
            new SalesOrderEmailLine(3, "KIT-BOT", en ? "Basic first-aid kit" : "Botiquín básico", 1m, 25.00m, 25.00m, Components: new[]
            {
                new SalesOrderEmailComponent("VEN-01", en ? "Elastic bandages" : "Vendas elásticas", 2m, 2m),
                new SalesOrderEmailComponent("ALC-70", en ? "Isopropyl alcohol 70 %" : "Alcohol isopropílico 70 %", 1m, 1m, "ALC-60"),
            }),
        };
        var lineTax = new[] { 4.31m, 2.01m, 2.88m };   // 11.5 % de cada línea, redondeado por línea
        var taxRows = TaxBreakdownRules.Compute(lineTax.Select(t => new TaxBreakdownLine(1, 11.5m, t, false)).ToList(), 0m,
            new Dictionary<int, TaxRateInfo> { [1] = rate }, format.CurrencyDecimals);
        var staff = audience == NotificationAudience.Staff;
        var link = staff ? OrderLink(SampleOrderPublicId) : null;
        var model = new SalesOrderEmailModel
        {
            Lang = l, Audience = audience, Format = format, CompanyName = en ? "Your company" : "Su compañía",
            OrderNumber = "OV-00012", OrderDate = new DateOnly(2026, 11, 24), RequestedAtUtc = new DateTime(2026, 11, 24, 14, 5, 0, DateTimeKind.Utc),
            SellerName = "Ana Pérez", CapturedByName = "Luis Rivera",
            ClientName = "Farmacia Central", ClientCode = "FARMACIA-CE", ClientAddress = new[] { "Calle Sol 10", "Ponce PR 00716" },
            ClientPhone = "(787) 555-1234", MerchantRegistration = "SURI-12345",
            DeliveryMode = SalesDeliveryModes.Delivery, ConsigneeName = "Farmacia Central — Sucursal Ponce", ConsigneeAddress = new[] { "Calle Comercio 25", "Ponce PR 00730" },
            WarehouseName = en ? "Main warehouse" : "Almacén principal",
            PaymentMethod = en ? "Credit" : "Crédito", PaymentTerm = en ? "30 days" : "30 días",
            RequiredDate = new DateOnly(2026, 11, 26), Notes = en ? "Deliver before noon." : "Entregar antes del mediodía.",
            Lines = lines, Subtotal = 80.00m, Taxes = ToEmailTaxes(taxRows, l), Total = 89.20m,
            AuthorizedByName = "María López", HasSignature = true, Link = link,
        };
        var payload = new Dictionary<string, string>
        {
            ["detail"] = en ? "Everything was reserved." : "Quedó todo reservado.",
        };
        return Content(payload, model, link, SampleOrderPublicId, new EmailAttachment(SignatureFileName(model.OrderNumber, "image/png"), "image/png", SamplePng));
    }
}
