using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 248 (Lote D del plan) — lista «Documentos por vencer» (GET /api/v1/client-documents/expiring, clients.read, módulo CATALOG). Lee con
/// <see cref="ClientDocumentQueries.ValidityRowsAsync"/> (la misma definición de «renovado» que el escaneo de avisos y la fuente CLIENT_DOCUMENT) y quita los renovados:
/// - los que vencen de hoy a hoy + WithinDays (sin WithinDays = el aviso de la compañía, el mayor umbral positivo de Tenant.DocExpiryThresholdDays);
/// - con IncludeExpired, también los ya vencidos; con IncludeMissing, los de un tipo con vigencia que no tienen «hasta» (MISSING_VALID_TO);
/// - las exenciones de IVU viejas sin documento («falta el documento», todas las activas) solo si quien consulta tiene tax.manage y el módulo TAX está encendido.
/// Ordena por «hasta» ascendente (los sin «hasta» al final), con tope de 2000 filas (Truncated). Solo lectura (AsNoTracking) y TenantId de la sesión.
/// </summary>
public sealed class ClientDocumentExpiryService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, PermissionService permissions, ModuleService modules,
    ITenantClock? clock = null)
{
    public const int MaxRows = 2000;
    public const int MinWithinDays = 1;
    public const int MaxWithinDays = 365;
    /// <summary>400 — withinDays fuera de rango.</summary>
    public const string WithinDaysRange = "Los días deben ser un número entero entre 1 y 365.";

    public async Task<ExpiringClientDocumentsDto> ListExpiringAsync(ExpiringClientDocumentsQuery query, CancellationToken ct)
    {
        if (query.WithinDays is int w && (w < MinWithinDays || w > MaxWithinDays)) throw new ValidationException("withinDays", WithinDaysRange);
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var today = (clock ?? TenantClock.Default).Today;
        var csv = await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenantId).Select(t => t.DocExpiryThresholdDays).FirstOrDefaultAsync(ct);
        var warnDays = ExpiryNoticeRules.WarnDays(ExpiryNoticeRules.ThresholdsOrDefault(csv));
        var within = query.WithinDays ?? warnDays;

        int? clientId = null;
        if (query.ClientPublicId is Guid cp)
        {
            clientId = await db.Clients.AsNoTracking().Where(c => c.PublicId == cp).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Cliente");
        }

        var rows = await ClientDocumentQueries.ValidityRowsAsync(db, lookups, tenantId, new ValidityFilter
        {
            ClientId = clientId,
            ValidToFrom = query.IncludeExpired ? null : today,
            ValidToTo = today.AddDays(within),
            IncludeMissing = query.IncludeMissing,
        }, ct);

        var items = new List<ExpiringClientDocumentDto>(rows.Count);
        foreach (var r in rows)
        {
            if (r.IsRenewed) continue;
            // sin «hasta»: solo los de un tipo con vigencia (los demás no vencen)
            if (r.ValidTo is null && !r.Marks.RequiresValidTo) continue;
            var state = ClientDocumentRules.ExpiryState(r.ValidTo, today, warnDays, false, r.Marks.RequiresValidTo);
            items.Add(new ExpiringClientDocumentDto(ExpiringClientDocumentRowKinds.Document, r.PublicId, null, r.ClientPublicId, r.ClientCode, r.ClientName,
                r.TypeCode, MultilingualText.Resolve(r.TypeLabelJson, tenant.Lang), r.Number, r.ValidFrom, r.ValidTo, ClientDocumentRules.DaysToExpiry(r.ValidTo, today), state,
                r.Marks.IsTax, r.Marks.RequiresValidTo, r.BackingExemptionId is not null, r.FilePublicId, r.CreatedAtUtc));
        }

        if (await permissions.HasPermissionAsync(PermissionCatalog.TaxManage, ct) && await modules.IsEnabledAsync(ModuleKeys.Tax, ct))
            items.AddRange(await ExemptionsWithoutDocumentAsync(tenantId, clientId, today, warnDays, query.IncludeExpired, ct));

        IEnumerable<ExpiringClientDocumentDto> filtered = items;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            filtered = filtered.Where(i => Has(i.ClientCode, q) || Has(i.ClientName, q) || Has(i.DocumentTypeLabel, q) || Has(i.DocumentType, q) || Has(i.DocumentNumber, q));
        }
        var ordered = filtered
            .OrderBy(i => i.ValidTo is null).ThenBy(i => i.ValidTo).ThenBy(i => i.ClientName, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.ClientCode, StringComparer.Ordinal)
            .ToList();
        var truncated = ordered.Count > MaxRows;
        return new ExpiringClientDocumentsDto(warnDays, within, truncated, truncated ? ordered.Take(MaxRows).ToList() : ordered);
    }

    private static bool Has(string? value, string q) => value is not null && value.Contains(q, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Exenciones de IVU ACTIVAS sin documento (las de antes del Lote 248) de clientes activos: salen todas (les falta el documento sin importar la fecha), menos las ya
    /// vencidas cuando no se piden vencidos.
    /// </summary>
    private async Task<List<ExpiringClientDocumentDto>> ExemptionsWithoutDocumentAsync(int tenantId, int? clientId, DateOnly today, int warnDays, bool includeExpired,
        CancellationToken ct)
    {
        var q = from e in db.Set<ClientTaxExemption>().AsNoTracking()
                join c in db.Clients.AsNoTracking() on e.ClientId equals c.ClientId
                where e.TenantId == tenantId && c.TenantId == tenantId && e.IsActive && e.ClientDocumentId == null && c.IsActive
                select new { e.PublicId, e.ExemptionType, e.CertificateNumber, e.ValidFrom, e.ValidTo, e.CreatedAtUtc, c.ClientId, ClientPublicId = c.PublicId, c.Code, c.Name };
        if (clientId is int id) q = q.Where(x => x.ClientId == id);
        if (!includeExpired) q = q.Where(x => x.ValidTo == null || x.ValidTo >= today);
        var rows = await q.ToListAsync(ct);
        return rows.Select(x => new ExpiringClientDocumentDto(ExpiringClientDocumentRowKinds.ExemptionWithoutDocument, null, x.PublicId, x.ClientPublicId, x.Code, x.Name,
                null, x.ExemptionType, x.CertificateNumber, x.ValidFrom, x.ValidTo, ClientDocumentRules.DaysToExpiry(x.ValidTo, today),
                ClientDocumentRules.ExpiryState(x.ValidTo, today, warnDays, false, false), true, false, false, null, x.CreatedAtUtc))
            .ToList();
    }
}
