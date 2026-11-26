using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Lote 248 — las variables de un aviso de documento del cliente, en UN solo lugar (las usan la fuente del escaneo, el contenido al enviar y la vista previa con un
/// documento real). <c>days</c> y <c>threshold</c> los pone el escaneo (dependen del día y del umbral cruzado).
/// </summary>
public static class ClientDocumentNoticeText
{
    public static string LangOf(string? lang) => string.Equals(lang?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";

    /// <summary>La nota de la exención de IVU (vacía si el documento no respalda una exención activa); al cliente le pide el documento renovado.</summary>
    public static string TaxNote(string lang, NotificationAudience audience, bool backsExemption)
    {
        if (!backsExemption) return string.Empty;
        var en = LangOf(lang) == "en";
        return audience == NotificationAudience.Customer
            ? en ? "This document backs your sales tax (IVU) exemption: send us the renewed document before it expires to stay exempt."
                 : "Este documento respalda su exención de IVU: envíenos el documento renovado antes de que venza para seguir exento."
            : en ? "It backs a sales tax (IVU) exemption: once it expires, sales carry IVU again."
                 : "Respalda una exención de IVU: al vencer, las ventas vuelven a llevar IVU.";
    }

    /// <summary>Las variables del documento (sin <c>days</c> ni <c>threshold</c>), con la etiqueta del tipo en <paramref name="lang"/> y la fecha en el formato de la compañía.</summary>
    public static Dictionary<string, string> BaseValues(ClientDocumentValidityRow row, TenantFormat format, string lang, NotificationAudience audience)
    {
        var l = LangOf(lang);
        var label = MultilingualText.Resolve(row.TypeLabelJson, l);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client"] = row.ClientName,
            ["clientCode"] = row.ClientCode,
            ["documentType"] = string.IsNullOrWhiteSpace(label) ? row.TypeCode : label,
            ["documentNumber"] = row.Number?.Trim() ?? string.Empty,
            ["validTo"] = row.ValidTo is DateOnly to ? TenantFormatText.Date(to, format) : string.Empty,
            ["taxNote"] = TaxNote(l, audience, row.BackingExemptionId is not null),
            ["clientPublicId"] = row.ClientPublicId.ToString("N"),
            ["documentPublicId"] = row.PublicId.ToString("N"),
            ["validToIso"] = IsoDate(row.ValidTo),
        };
    }

    /// <summary>«2026-12-31» (vacío si no hay fecha): lo que compara el contenido para saber si la fecha «hasta» cambió desde que se publicó el aviso.</summary>
    public static string IsoDate(DateOnly? d) => d is DateOnly x ? x.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Días que faltan (o que pasaron desde que venció), siempre positivo.</summary>
    public static string Days(DateOnly validTo, DateOnly today) => Math.Abs(validTo.DayNumber - today.DayNumber).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Lote 248 — contenido de los avisos de documentos del cliente (SALES_CLIENT_DOCUMENT_EXPIRING / _EXPIRED): las variables en el idioma de cada destinatario, los
/// destinatarios del evento CLIENT_SELLERS («Vendedores del cliente», SalesSellerQueries.NoticeUsersAsync) y CLIENT («Cliente del documento») y la revisión al enviar
/// (el documento se quitó, se renovó, cambió su fecha «hasta» o el cliente se dio de baja → el aviso no sale y queda SKIPPED con su motivo).
/// <para>
/// SOLO LECTURA: cada llamada abre su PROPIO scope como la compañía SIN usuario (TenantContext.AsAnonymous), con el DbContext del scope en NoTracking y con
/// TenantId == tenantId explícito además del filtro global.
/// </para>
/// </summary>
public sealed class ClientDocumentNoticeContent(IServiceScopeFactory scopes) : INotificationContentProvider
{
    public const string NotFoundWhat = "Documento del cliente";
    public const string NoSellersReason = "El cliente no tiene ruta de venta ni vendedor preferido con usuario activo.";
    public const string CompanyOffReason = "La compañía no tiene encendido «Avisar al cliente» (Ajustes → Avisos de vencimiento).";
    public const string ClientOffReason = "El cliente tiene apagado el aviso de documentos por vencer.";
    public const string NoClientEmailReason = "El cliente no tiene correo principal ni contactos marcados «Recibe avisos por correo».";
    public const string RemovedReason = "El documento ya se quitó: no se avisa.";
    public const string RenewedReason = "El documento ya se renovó: no se avisa.";
    public const string ValidToChangedReason = "La fecha «hasta» del documento cambió: este aviso ya no aplica.";
    public const string ClientInactiveReason = "El cliente está dado de baja: no se avisa.";

    public IReadOnlyCollection<string> EventCodes { get; } = ClientDocumentEventCodes.All;
    public string? ReferencePermission => PermissionCatalog.ClientsRead;
    public string? ReferenceModule => ModuleKeys.Catalog;

    private static bool Handles(string? eventCode) => eventCode is not null && ClientDocumentEventCodes.All.Contains(eventCode, StringComparer.Ordinal);

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

    private sealed record DocRef(int DocumentId, int ClientId, bool IsActive);

    private static async Task<DocRef?> FindAsync(TeikemDbContext db, int tenantId, string? publicId, CancellationToken ct)
    {
        if (!Guid.TryParse((publicId ?? string.Empty).Trim(), out var pid)) return null;
        return await db.Set<ClientDocument>().AsNoTracking().Where(d => d.PublicId == pid && d.TenantId == tenantId)
            .Select(d => new DocRef(d.ClientDocumentId, d.ClientId, d.IsActive)).FirstOrDefaultAsync(ct);
    }

    private static Task<Tenant?> TenantAsync(TeikemDbContext db, int tenantId, CancellationToken ct)
        => db.Tenants.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

    private static TenantFormat FormatOf(Tenant? t) => t is null ? TenantFormatRules.Defaults(TenantFormatRules.DefaultRegion) : TenantFormatRules.Read(t);

    private static async Task<ClientDocumentValidityRow?> RowAsync(IServiceProvider sp, TeikemDbContext db, int tenantId, int documentId, CancellationToken ct)
        => (await ClientDocumentQueries.ValidityRowsAsync(db, sp.GetRequiredService<ILookupCache>(), tenantId,
            new ValidityFilter { DocumentIds = new[] { documentId }, IncludeMissing = true }, ct)).FirstOrDefault();

    // ---------------------------------------------------------------- variables con un documento real

    public async Task<IReadOnlyDictionary<string, string>> ValuesForReferenceAsync(int tenantId, string eventCode, string reference, CancellationToken ct)
    {
        if (!Handles(eventCode)) throw new InvalidOperationException($"El contenido de los documentos del cliente no atiende el evento '{eventCode}'.");
        return await InScopeAsync(tenantId, async (sp, db) =>
        {
            var doc = await FindAsync(db, tenantId, reference, ct);
            var row = doc is null || !doc.IsActive ? null : await RowAsync(sp, db, tenantId, doc.DocumentId, ct);
            if (row is null) throw new NotFoundException(NotFoundWhat);
            var tenant = await TenantAsync(db, tenantId, ct);
            var values = ClientDocumentNoticeText.BaseValues(row, FormatOf(tenant), ClientDocumentNoticeText.LangOf(tenant?.DefaultLangCode), NotificationAudience.Staff);
            var today = sp.GetRequiredService<ITenantClock>().Today;
            values["days"] = row.ValidTo is DateOnly to ? ClientDocumentNoticeText.Days(to, today) : string.Empty;
            values["threshold"] = string.Empty;
            return (IReadOnlyDictionary<string, string>)values;
        });
    }

    // ---------------------------------------------------------------- destinatarios del evento

    public async Task<NotificationContextResolution> RecipientsAsync(int tenantId, string eventCode, string contextKey, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        if (!Handles(eventCode)) return NotificationContextResolution.Empty(null);
        var key = (contextKey ?? string.Empty).Trim().ToUpperInvariant();
        if (key is not (ClientDocumentContextRecipients.ClientSellers or ClientDocumentContextRecipients.Client)) return NotificationContextResolution.Empty(null);
        return await InScopeAsync(tenantId, async (sp, db) =>
        {
            var doc = await FindAsync(db, tenantId, values.GetValueOrDefault("documentPublicId"), ct);
            if (doc is null) return NotificationContextResolution.Empty(null);
            return key == ClientDocumentContextRecipients.ClientSellers
                ? await SellersAsync(db, tenantId, doc.ClientId, ct)
                : await ClientAsync(sp, db, tenantId, doc.ClientId, ct);
        });
    }

    private static async Task<NotificationContextResolution> SellersAsync(TeikemDbContext db, int tenantId, int clientId, CancellationToken ct)
    {
        var ids = await SalesSellerQueries.NoticeUsersAsync(db, tenantId, clientId, ct);
        if (ids.Count == 0) return NotificationContextResolution.Empty(NoSellersReason);
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber }).ToDictionaryAsync(u => u.Id, ct);
        var people = ids.Where(users.ContainsKey).Select(id =>
        {
            var u = users[id];
            var name = string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName.Trim();
            return new NotificationContextPerson(u.Id, name, u.Email, u.PhoneNumber, NotificationAudience.Staff, null);
        }).ToList();
        return people.Count == 0 ? NotificationContextResolution.Empty(NoSellersReason) : new NotificationContextResolution(people, null);
    }

    private static async Task<NotificationContextResolution> ClientAsync(IServiceProvider sp, TeikemDbContext db, int tenantId, int clientId, CancellationToken ct)
    {
        var tenant = await TenantAsync(db, tenantId, ct);
        if (tenant is null || !tenant.DocExpiryNotifyClient) return NotificationContextResolution.Empty(CompanyOffReason);
        var setting = await db.Set<ClientNotificationSetting>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.ClientId == clientId && s.Kind == ClientNotificationKinds.DocumentExpiring, ct);
        // excepción a «sin fila = apagado»: sin fila sigue a la compañía; la fila con el correo apagado lo apaga para este cliente
        if (setting is not null && !setting.EmailEnabled) return NotificationContextResolution.Empty(ClientOffReason);

        var people = new List<(string Email, string? Name)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in await ClientContactQueries.NoticeEmailsAsync(db, sp.GetRequiredService<ILookupCache>(), tenantId, clientId, ct))
            if (seen.Add(e.Email)) people.Add((e.Email, e.Name));
        IReadOnlyList<string> extra;
        try { extra = ClientNotificationRules.ParseEmails(setting?.ExtraEmails); }
        catch (FormatException) { extra = Array.Empty<string>(); }   // se validaron al guardar; uno roto no detiene el aviso
        foreach (var e in extra)
            if (seen.Add(e)) people.Add((e, null));
        if (people.Count == 0) return NotificationContextResolution.Empty(NoClientEmailReason);

        var noticeLang = await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId && c.TenantId == tenantId).Select(c => c.NoticeLang).FirstOrDefaultAsync(ct);
        var lang = ClientDocumentNoticeText.LangOf(string.IsNullOrWhiteSpace(noticeLang) ? tenant.DefaultLangCode : noticeLang);
        return new NotificationContextResolution(people.Select(p => new NotificationContextPerson(null, p.Name, p.Email, null, NotificationAudience.Customer, lang)).ToList(), null);
    }

    // ---------------------------------------------------------------- el contenido al enviar

    public async Task<NotificationContent?> BuildAsync(NotificationContentRequest req, CancellationToken ct)
    {
        if (!Handles(req.EventCode)) return null;
        var lang = ClientDocumentNoticeText.LangOf(req.Lang);
        return await InScopeAsync(req.TenantId, async (sp, db) =>
        {
            var tenantId = req.TenantId;
            var doc = await FindAsync(db, tenantId, req.Values.GetValueOrDefault("documentPublicId"), ct);
            if (doc is null) return null;
            if (!doc.IsActive) return NotificationContent.Skip(RemovedReason);
            var clientActive = await db.Clients.AsNoTracking().Where(c => c.ClientId == doc.ClientId && c.TenantId == tenantId).Select(c => (bool?)c.IsActive).FirstOrDefaultAsync(ct);
            if (clientActive != true) return NotificationContent.Skip(ClientInactiveReason);
            var row = await RowAsync(sp, db, tenantId, doc.DocumentId, ct);
            if (row is null) return NotificationContent.Skip(RemovedReason);   // el archivo del documento se quitó
            if (row.IsRenewed) return NotificationContent.Skip(RenewedReason);
            if (req.Values.TryGetValue("validToIso", out var published) && !string.Equals(published?.Trim() ?? string.Empty, ClientDocumentNoticeText.IsoDate(row.ValidTo), StringComparison.Ordinal))
                return NotificationContent.Skip(ValidToChangedReason);

            var tenant = await TenantAsync(db, tenantId, ct);
            var values = new Dictionary<string, string>(req.Values, StringComparer.Ordinal);
            foreach (var (k, v) in ClientDocumentNoticeText.BaseValues(row, FormatOf(tenant), lang, req.Audience)) values[k] = v;   // gana el contenido
            return new NotificationContent(values, new Dictionary<string, NotificationBlock>(), Array.Empty<EmailAttachment>());
        });
    }

    // ---------------------------------------------------------------- ejemplo (sin base de datos)

    /// <summary>El documento de ejemplo para «Enviar prueba» y la vista previa sin documento real: certificado de exención estatal de Farmacia Central que respalda una exención.</summary>
    public NotificationContent Sample(string eventCode, NotificationAudience audience, string lang)
    {
        var l = ClientDocumentNoticeText.LangOf(lang);
        var en = l == "en";
        var expired = string.Equals(eventCode, ClientDocumentEventCodes.Expired, StringComparison.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client"] = "Farmacia Central",
            ["clientCode"] = "FARMACIA-CE",
            ["documentType"] = en ? "State sales tax (IVU) exemption certificate" : "Certificado de exención de IVU estatal",
            ["documentNumber"] = "E-12345",
            ["validTo"] = expired ? "11/24/2026" : "12/31/2026",
            ["days"] = expired ? "3" : "15",
            ["threshold"] = expired ? "0" : "15",
            ["taxNote"] = ClientDocumentNoticeText.TaxNote(l, audience, true),
            ["clientPublicId"] = "0f8fad5bd9cb469fa16570867728950e",
            ["documentPublicId"] = "7c9e6679742540de944be07fc1f90ae7",
            ["validToIso"] = expired ? "2026-11-24" : "2026-12-31",
        };
        return new NotificationContent(values, new Dictionary<string, NotificationBlock>(), Array.Empty<EmailAttachment>());
    }
}
