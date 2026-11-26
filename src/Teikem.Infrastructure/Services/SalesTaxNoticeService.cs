using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Tax;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Tax;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 248 (Lote D del plan, pieza E) — aviso de exención de IVU al escoger el cliente de una orden de venta y en el resumen
/// (GET /api/v1/sales-orders/tax-notice?client={publicId}). Solo avisa: no bloquea nada ni cambia la orden.
/// Se basa en la EXENCIÓN (lo que cambia el monto), con la misma vigencia efectiva que el cálculo (<see cref="ClientExemptionBacking.Effective"/> y
/// <see cref="TaxRules.ResolveExemptions"/>) y el «hoy» de la compañía (ITenantClock):
/// - EXPIRED: la exención con documento vencida más reciente, si nada vigente cubre lo que ella cubría (como el aviso del cálculo).
/// - EXPIRING: el fin de la cobertura vigente (el «hasta» más tardío de las exenciones que cubren lo mismo) cae dentro del aviso de la compañía
///   (<see cref="ExpiryNoticeRules.WarnDays"/> de Tenant.DocExpiryThresholdDays). Una cobertura sin «hasta» no avisa.
/// - Las exenciones viejas sin documento («falta el documento») cuentan como cobertura (siguen eximiendo igual) pero nunca son el motivo del aviso: al vendedor
///   no se le muestran.
/// Módulo TAX apagado: NONE. Sin cliente: 400; cliente que no existe o es de otra compañía: 404 (DbSet filtrado por compañía).
/// </summary>
public sealed class SalesTaxNoticeService(TeikemDbContext db, ITenantContext tenant, ModuleService modules, ITenantClock? clock = null)
{
    /// <summary>400 — falta el cliente.</summary>
    public const string ClientRequired = "Indique el cliente.";
    /// <summary>{0} = fecha en el formato de la compañía.</summary>
    public const string ExpiredFormat = "El certificado de exención de IVU de este cliente venció el {0}: se cobrará el IVU.";
    /// <summary>{0} = fecha en el formato de la compañía; {1} = días que faltan.</summary>
    public const string ExpiringFormat = "El certificado de exención de IVU de este cliente vence el {0} (en {1} días): después se cobrará el IVU.";

    public async Task<SalesTaxNoticeDto> GetAsync(Guid? clientPublicId, CancellationToken ct)
    {
        if (clientPublicId is not Guid cp || cp == Guid.Empty) throw new ValidationException("client", ClientRequired);
        var clientId = await db.Clients.AsNoTracking().Where(c => c.PublicId == cp).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Cliente");
        if (!await modules.IsEnabledAsync(ModuleKeys.Tax, ct)) return SalesTaxNoticeDto.None;

        var exemptions = await db.Set<ClientTaxExemption>().AsNoTracking().Where(e => e.ClientId == clientId && e.IsActive)
            .Select(e => new { e.ClientTaxExemptionId, e.Scope, e.ComponentCodes, e.CertificateNumber, e.ValidFrom, e.ValidTo, e.ClientDocumentId })
            .ToListAsync(ct);
        if (exemptions.Count == 0) return SalesTaxNoticeDto.None;

        var docIds = exemptions.Where(e => e.ClientDocumentId is not null).Select(e => e.ClientDocumentId!.Value).Distinct().ToList();
        var docs = docIds.Count == 0
            ? new Dictionary<int, (ExemptionBackingDocument Backing, int TypeId)>()
            : (await db.Set<ClientDocument>().AsNoTracking().Where(d => docIds.Contains(d.ClientDocumentId))
                    .Select(d => new { d.ClientDocumentId, d.IsActive, d.ValidFrom, d.ValidTo, d.DocumentNumber, d.DocumentTypeLookupId })
                    .ToListAsync(ct))
                .ToDictionary(d => d.ClientDocumentId, d => (Backing: new ExemptionBackingDocument(d.IsActive, d.ValidFrom, d.ValidTo, d.DocumentNumber), TypeId: d.DocumentTypeLookupId));

        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var types = docs.Count == 0 ? new Dictionary<int, ClientDocumentTypeInfo>()
            : await ClientDocumentQueries.TypesByIdAsync(db, tenantId, docs.Values.Select(d => d.TypeId), ct);

        var entries = new List<SalesTaxNoticeEntry>(exemptions.Count);
        foreach (var e in exemptions)
        {
            (ExemptionBackingDocument Backing, int TypeId)? doc = e.ClientDocumentId is int d && docs.TryGetValue(d, out var row) ? row : null;
            var eff = ClientExemptionBacking.Effective(e.ClientDocumentId, e.ValidFrom, e.ValidTo, e.CertificateNumber, doc?.Backing);
            if (!eff.Applies) continue;   // documento quitado: no exime nada (la lista de Impuestos la marca; aquí no hay nada que cubrir)
            var label = doc is { } dd && types.TryGetValue(dd.TypeId, out var type) ? MultilingualText.Resolve(type.LabelJson, tenant.Lang) : null;
            entries.Add(new SalesTaxNoticeEntry(
                new TaxExemptionInfo(e.Scope, TaxRules.SplitComponentCodes(e.ComponentCodes), eff.Certificate, eff.From, eff.To),
                e.ClientDocumentId is not null, label));
        }

        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var format = t is null ? TenantFormatRules.Defaults(TenantFormatRules.DefaultRegion) : TenantFormatRules.Read(t);
        var warnDays = ExpiryNoticeRules.WarnDays(ExpiryNoticeRules.ThresholdsOrDefault(t?.DocExpiryThresholdDays));
        return Decide(entries, (clock ?? TenantClock.Default).Today, warnDays, format);
    }

    /// <summary>
    /// Regla pura del aviso sobre las exenciones que APLICAN (ya pasadas por <see cref="ClientExemptionBacking.Effective"/>). Backed = tiene documento (las viejas sin
    /// documento cubren pero nunca disparan el aviso).
    /// </summary>
    public static SalesTaxNoticeDto Decide(IReadOnlyList<SalesTaxNoticeEntry> entries, DateOnly today, int warnDays, TenantFormat format)
    {
        if (entries.Count == 0) return SalesTaxNoticeDto.None;
        var outcome = TaxRules.ResolveExemptions(entries.Select(e => e.Info), today);

        // ¿Lo cubre algo vigente? Una total vigente lo cubre todo; si no, una parcial queda cubierta si todos sus componentes siguen exentos.
        bool Covered(TaxExemptionInfo info) => outcome.Full is not null
            || (info.Scope == TaxExemptionScopes.Components && info.ComponentCodes.Count > 0 && info.ComponentCodes.All(outcome.ComponentCertificates.ContainsKey));

        // EXPIRED: la vencida con documento más reciente (por «hasta»), si nada vigente cubre lo que cubría
        var lastExpired = entries.Where(e => e.Backed && e.Info.ValidTo is DateOnly vt && vt < today)
            .OrderByDescending(e => e.Info.ValidTo).ThenByDescending(e => e.Info.ValidFrom).FirstOrDefault();
        if (lastExpired is not null && !Covered(lastExpired.Info))
        {
            var vt = lastExpired.Info.ValidTo!.Value;
            return new SalesTaxNoticeDto(SalesTaxNoticeLevels.Expired, string.Format(CultureInfo.InvariantCulture, ExpiredFormat, TenantFormatText.Date(vt, format)),
                vt, vt.DayNumber - today.DayNumber, lastExpired.Info.CertificateNumber, lastExpired.DocumentTypeLabel);
        }

        // EXPIRING: fin de cada cobertura vigente = el «hasta» más tardío de las exenciones que la dan (sin «hasta» = no vence); se avisa del más cercano
        var valid = entries.Where(e => TaxRules.InEffect(e.Info.ValidFrom, e.Info.ValidTo, today)).ToList();
        var ends = new List<SalesTaxNoticeEntry>();
        if (outcome.Full is not null)
        {
            if (CoverageEnd(valid.Where(e => e.Info.Scope != TaxExemptionScopes.Components)) is { } end) ends.Add(end);
        }
        else
        {
            foreach (var code in outcome.ComponentCertificates.Keys)
                if (CoverageEnd(valid.Where(e => e.Info.Scope == TaxExemptionScopes.Components
                        && e.Info.ComponentCodes.Contains(code, StringComparer.OrdinalIgnoreCase))) is { } end)
                    ends.Add(end);
        }
        var soonest = ends.Where(e => e.Backed).OrderBy(e => e.Info.ValidTo).FirstOrDefault();
        if (soonest?.Info.ValidTo is DateOnly until)
        {
            var days = until.DayNumber - today.DayNumber;
            if (days >= 0 && days <= Math.Max(0, warnDays))
                return new SalesTaxNoticeDto(SalesTaxNoticeLevels.Expiring,
                    string.Format(CultureInfo.InvariantCulture, ExpiringFormat, TenantFormatText.Date(until, format), days),
                    until, days, soonest.Info.CertificateNumber, soonest.DocumentTypeLabel);
        }
        return SalesTaxNoticeDto.None;
    }

    /// <summary>La exención que da el fin más tardío de una cobertura; null si alguna no tiene «hasta» (la cobertura no vence) o no hay ninguna.</summary>
    private static SalesTaxNoticeEntry? CoverageEnd(IEnumerable<SalesTaxNoticeEntry> covering)
    {
        var list = covering.ToList();
        if (list.Count == 0 || list.Any(e => e.Info.ValidTo is null)) return null;
        // a igual «hasta», gana la que tiene documento (así una vieja sin documento no esconde el aviso del certificado)
        return list.OrderByDescending(e => e.Info.ValidTo).ThenByDescending(e => e.Backed).First();
    }
}

/// <summary>Lote 248 — una exención que aplica, para <see cref="SalesTaxNoticeService.Decide"/>: su vigencia efectiva, si tiene documento y la etiqueta del tipo.</summary>
public sealed record SalesTaxNoticeEntry(TaxExemptionInfo Info, bool Backed, string? DocumentTypeLabel);
