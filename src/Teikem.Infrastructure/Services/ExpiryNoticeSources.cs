using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 248 — algo con fecha «hasta» que el trabajo DOCUMENT_EXPIRY_SCAN revisa: un documento del cliente (CLIENT_DOCUMENT) o, desde la pieza G, un precio especial
/// (SPECIAL_PRICE). Cada fuente se registra en DI como <c>IExpiryNoticeSource</c> (scoped) y corre como la compañía del trabajo (sin usuario), leyendo con AsNoTracking y
/// TenantId explícito.
/// </summary>
public interface IExpiryNoticeSource
{
    /// <summary>Clave de la fuente (CLIENT_DOCUMENT, SPECIAL_PRICE).</summary>
    string Key { get; }

    /// <summary>Módulo que debe estar encendido en la compañía para que la fuente corra.</summary>
    string ModuleKey { get; }

    /// <summary>Prefijo de la clave de no repetición (máximo unos pocos caracteres: «CD», «SP»).</summary>
    string DedupPrefix { get; }

    /// <summary>false = solo avisa antes de vencer o el día que vence (umbrales ≥ 0), nunca de lo ya vencido.</summary>
    bool NotifyAfterExpiry { get; }

    /// <summary>
    /// Lo que vence en la ventana [hoy − <paramref name="lookbackDays"/>, hoy + <paramref name="lookaheadDays"/>] (bordes incluidos); <paramref name="lookbackDays"/> null =
    /// sin límite hacia atrás (aviso diario encendido). La fuente ya quita lo que no debe avisar (renovado, dado de baja). El escaneo decide el umbral.
    /// </summary>
    Task<IReadOnlyList<ExpiryCandidate>> CandidatesAsync(DateOnly today, int lookaheadDays, int? lookbackDays, CancellationToken ct);
}

/// <summary>
/// Lote 248 — un candidato a aviso: el id interno (va en la clave de no repetición), su «hasta», el evento de «por vencer», el de «vencido» (null = la fuente no avisa de
/// lo vencido) y las variables del aviso (el escaneo agrega <c>days</c> y <c>threshold</c>).
/// </summary>
public sealed record ExpiryCandidate(int EntityId, DateOnly ValidTo, string ExpiringEvent, string? ExpiredEvent, IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// Lote 248 — fuente CLIENT_DOCUMENT: los documentos del cliente con «hasta» (módulo de catálogo, prefijo «CD»). Lee con <see cref="ClientDocumentQueries.ValidityRowsAsync"/>
/// (la MISMA definición de «renovado» que la lista «Documentos por vencer») y quita los renovados; documentos y clientes dados de baja ya no salen de la consulta.
/// Las variables salen en el idioma de la compañía y con su formato de fecha (el contenido las vuelve a armar en el idioma de cada destinatario al enviar).
/// </summary>
public sealed class ClientDocumentExpirySource(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups) : IExpiryNoticeSource
{
    public const string SourceKey = "CLIENT_DOCUMENT";

    public string Key => SourceKey;
    public string ModuleKey => ModuleKeys.Catalog;
    public string DedupPrefix => DocumentExpiryCodes.ClientDocumentPrefix;
    public bool NotifyAfterExpiry => true;

    public async Task<IReadOnlyList<ExpiryCandidate>> CandidatesAsync(DateOnly today, int lookaheadDays, int? lookbackDays, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var filter = new ValidityFilter
        {
            ValidToFrom = lookbackDays is int back ? today.AddDays(-Math.Max(0, back)) : null,
            ValidToTo = today.AddDays(Math.Max(0, lookaheadDays)),
            IncludeMissing = false,
        };
        var rows = (await ClientDocumentQueries.ValidityRowsAsync(db, lookups, tenantId, filter, ct)).Where(r => !r.IsRenewed && r.ValidTo is not null).ToList();
        if (rows.Count == 0) return Array.Empty<ExpiryCandidate>();

        var t = await db.Tenants.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var format = t is null ? TenantFormatRules.Defaults(TenantFormatRules.DefaultRegion) : TenantFormatRules.Read(t);
        var lang = ClientDocumentNoticeText.LangOf(t?.DefaultLangCode);
        return rows.Select(r => new ExpiryCandidate(r.DocumentId, r.ValidTo!.Value, ClientDocumentEventCodes.Expiring, ClientDocumentEventCodes.Expired,
            ClientDocumentNoticeText.BaseValues(r, format, lang, NotificationAudience.Staff).ToDictionary(kv => kv.Key, kv => (string?)kv.Value, StringComparer.Ordinal))).ToList();
    }
}
