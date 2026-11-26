using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Lote 248 (pieza G) — las variables de un aviso de precio especial, en UN solo lugar (las usan la fuente del escaneo, el contenido al enviar y la vista previa con una
/// lista real). <c>days</c> y <c>threshold</c> los pone el escaneo.
/// </summary>
public static class SpecialPriceNoticeText
{
    public static Dictionary<string, string> BaseValues(SpecialPriceExpiryRow row, TenantFormat format, string lang)
    {
        var l = ClientDocumentNoticeText.LangOf(lang);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["owner"] = row.OwnerName,
            ["ownerKind"] = SpecialPriceDataSource.OwnerKindLabel(row.OwnerKind, l),
            ["priceList"] = row.Name,
            ["validTo"] = row.ValidTo is DateOnly to ? TenantFormatText.Date(to, format) : string.Empty,
            ["itemCount"] = row.ItemCount.ToString(CultureInfo.InvariantCulture),
            ["priceListPublicId"] = row.PublicId.ToString("N"),
            ["validToIso"] = ClientDocumentNoticeText.IsoDate(row.ValidTo),
            ["ownerLink"] = SpecialPriceExpiryRules.OwnerLink(row.OwnerKind, row.OwnerPublicId),
        };
    }
}

/// <summary>
/// Lote 248 (pieza G) — contenido del aviso «Precio especial por vencer» (SALES_SPECIAL_PRICE_EXPIRING): las variables en el idioma de cada destinatario, el destinatario
/// del evento CLIENT_SELLERS (de un precio de cliente, SalesSellerQueries.NoticeUsersAsync de ese cliente; de un precio de cadena, la unión de los de sus clientes
/// activos, tope <see cref="MaxChainRecipients"/>) y la revisión al enviar (la lista se desactivó, volvió a borrador, se renovó, cambió su «hasta» o su dueño se dio de
/// baja → el aviso no sale y queda SKIPPED con su motivo). Nunca va al cliente.
/// <para>
/// SOLO LECTURA: cada llamada abre su PROPIO scope como la compañía SIN usuario (TenantContext.AsAnonymous), con el DbContext del scope en NoTracking y con
/// TenantId == tenantId explícito además del filtro global.
/// </para>
/// </summary>
public sealed class SpecialPriceNoticeContent(IServiceScopeFactory scopes) : INotificationContentProvider
{
    /// <summary>Tope de vendedores de un precio de cadena (la unión de los de sus clientes activos).</summary>
    public const int MaxChainRecipients = 50;

    public const string NoClientSellersReason = ClientDocumentNoticeContent.NoSellersReason;
    public const string NoChainSellersReason = "Ningún cliente activo de la cadena tiene ruta de venta ni vendedor preferido con usuario activo.";
    public const string InactiveReason = "El precio especial se desactivó: no se avisa.";
    public const string UnpublishedReason = "El precio especial volvió a borrador: no se avisa.";
    public const string RenewedReason = "El precio especial ya se renovó: no se avisa.";
    public const string ValidToChangedReason = "La fecha «hasta» del precio especial cambió: este aviso ya no aplica.";
    public const string OwnerInactiveReason = "El cliente o la cadena del precio especial está dado de baja: no se avisa.";

    public IReadOnlyCollection<string> EventCodes { get; } = SpecialPriceEventCodes.All;
    public string? ReferencePermission => PermissionCatalog.SalesView;
    public string? ReferenceModule => ModuleKeys.SalesOrders;

    private static bool Handles(string? eventCode) => eventCode is not null && SpecialPriceEventCodes.All.Contains(eventCode, StringComparer.Ordinal);

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

    /// <summary>La lista dedicada por su PublicId (activa o no, publicada o no); null si no existe, no es dedicada o es de otra compañía.</summary>
    private static async Task<SpecialPriceExpiryRow?> FindAsync(TeikemDbContext db, int tenantId, string? publicId, CancellationToken ct)
    {
        if (!Guid.TryParse((publicId ?? string.Empty).Trim(), out var pid)) return null;
        var id = await db.Set<PriceList>().AsNoTracking()
            .Where(l => l.PublicId == pid && l.TenantId == tenantId && (l.SpecialClientId != null || l.SpecialChainId != null))
            .Select(l => (int?)l.PriceListId).FirstOrDefaultAsync(ct);
        if (id is null) return null;
        return (await SpecialPriceExpiryQueries.RowsAsync(db, tenantId, new[] { id.Value }, onlyLive: false, null, null, ct)).FirstOrDefault();
    }

    private static Task<Tenant?> TenantAsync(TeikemDbContext db, int tenantId, CancellationToken ct)
        => db.Tenants.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

    private static TenantFormat FormatOf(Tenant? t) => t is null ? TenantFormatRules.Defaults(TenantFormatRules.DefaultRegion) : TenantFormatRules.Read(t);

    // ---------------------------------------------------------------- variables con una lista real

    public async Task<IReadOnlyDictionary<string, string>> ValuesForReferenceAsync(int tenantId, string eventCode, string reference, CancellationToken ct)
    {
        if (!Handles(eventCode)) throw new InvalidOperationException($"El contenido de los precios especiales no atiende el evento '{eventCode}'.");
        return await InScopeAsync(tenantId, async (sp, db) =>
        {
            var row = await FindAsync(db, tenantId, reference, ct);
            if (row is null || !row.IsActive) throw new NotFoundException(SpecialPriceRules.RowNotFound);
            var tenant = await TenantAsync(db, tenantId, ct);
            var values = SpecialPriceNoticeText.BaseValues(row, FormatOf(tenant), ClientDocumentNoticeText.LangOf(tenant?.DefaultLangCode));
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
        if (key != SpecialPriceContextRecipients.ClientSellers) return NotificationContextResolution.Empty(null);
        return await InScopeAsync(tenantId, async (_, db) =>
        {
            var row = await FindAsync(db, tenantId, values.GetValueOrDefault("priceListPublicId"), ct);
            if (row is null) return NotificationContextResolution.Empty(null);
            if (row.OwnerKind == SpecialPriceOwnerKinds.Client)
            {
                var ids = await SalesSellerQueries.NoticeUsersAsync(db, tenantId, row.OwnerId, ct);
                return await PeopleAsync(db, ids, NoClientSellersReason, ct);
            }

            // cadena: la unión de los vendedores de sus clientes activos, sin repetir y con tope
            var clientIds = await db.Clients.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.ChainId == row.OwnerId && c.IsActive)
                .OrderBy(c => c.ClientId).Select(c => c.ClientId).ToListAsync(ct);
            var union = new List<int>();
            var seen = new HashSet<int>();
            foreach (var clientId in clientIds)
            {
                foreach (var id in await SalesSellerQueries.NoticeUsersAsync(db, tenantId, clientId, ct))
                {
                    if (!seen.Add(id)) continue;
                    union.Add(id);
                    if (union.Count >= MaxChainRecipients) break;
                }
                if (union.Count >= MaxChainRecipients) break;
            }
            return await PeopleAsync(db, union, NoChainSellersReason, ct);
        });
    }

    private static async Task<NotificationContextResolution> PeopleAsync(TeikemDbContext db, IReadOnlyList<int> ids, string emptyReason, CancellationToken ct)
    {
        if (ids.Count == 0) return NotificationContextResolution.Empty(emptyReason);
        var wanted = ids.ToList();
        var users = await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber }).ToDictionaryAsync(u => u.Id, ct);
        var people = ids.Where(users.ContainsKey).Select(id =>
        {
            var u = users[id];
            var name = string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName.Trim();
            return new NotificationContextPerson(u.Id, name, u.Email, u.PhoneNumber, NotificationAudience.Staff, null);
        }).ToList();
        return people.Count == 0 ? NotificationContextResolution.Empty(emptyReason) : new NotificationContextResolution(people, null);
    }

    // ---------------------------------------------------------------- el contenido al enviar

    public async Task<NotificationContent?> BuildAsync(NotificationContentRequest req, CancellationToken ct)
    {
        if (!Handles(req.EventCode)) return null;
        var lang = ClientDocumentNoticeText.LangOf(req.Lang);
        return await InScopeAsync(req.TenantId, async (_, db) =>
        {
            var row = await FindAsync(db, req.TenantId, req.Values.GetValueOrDefault("priceListPublicId"), ct);
            if (row is null) return null;
            if (!row.IsActive) return NotificationContent.Skip(InactiveReason);
            if (!row.IsPublished) return NotificationContent.Skip(UnpublishedReason);
            if (!row.OwnerIsActive) return NotificationContent.Skip(OwnerInactiveReason);
            if (req.Values.TryGetValue("validToIso", out var published) && !string.Equals(published?.Trim() ?? string.Empty, ClientDocumentNoticeText.IsoDate(row.ValidTo), StringComparison.Ordinal))
                return NotificationContent.Skip(ValidToChangedReason);
            if (row.IsRenewed) return NotificationContent.Skip(RenewedReason);

            var tenant = await TenantAsync(db, req.TenantId, ct);
            var values = new Dictionary<string, string>(req.Values, StringComparer.Ordinal);
            foreach (var (k, v) in SpecialPriceNoticeText.BaseValues(row, FormatOf(tenant), lang)) values[k] = v;   // gana el contenido
            return new NotificationContent(values, new Dictionary<string, NotificationBlock>(), Array.Empty<EmailAttachment>());
        });
    }

    // ---------------------------------------------------------------- ejemplo (sin base de datos)

    /// <summary>El precio especial de ejemplo para «Enviar prueba» y la vista previa sin lista real: el de la cadena Walgreens que vence en 15 días.</summary>
    public NotificationContent Sample(string eventCode, NotificationAudience audience, string lang)
    {
        var en = ClientDocumentNoticeText.LangOf(lang) == "en";
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["owner"] = "Walgreens",
            ["ownerKind"] = SpecialPriceDataSource.OwnerKindLabel(SpecialPriceOwnerKinds.Chain, en ? "en" : "es"),
            ["priceList"] = "Precio especial — Walgreens (del 01/12/2026 al 31/12/2026)",
            ["validTo"] = "12/31/2026",
            ["days"] = "15",
            ["threshold"] = "15",
            ["itemCount"] = "12",
            ["priceListPublicId"] = "3f2504e04f8941d39a0c0305e82c3301",
            ["validToIso"] = "2026-12-31",
            ["ownerLink"] = "/catalog/chains?id=9a1f3c52-7d1e-4b8a-9a55-2d7c0f0e1b11",
        };
        return new NotificationContent(values, new Dictionary<string, NotificationBlock>(), Array.Empty<EmailAttachment>());
    }
}
