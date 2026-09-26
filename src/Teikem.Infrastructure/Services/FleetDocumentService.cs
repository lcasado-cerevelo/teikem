using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P3) — panel "Documentos por vencer": documentos de vehículo, licencias y certificaciones unidos.
/// - Lee EXCLUSIVAMENTE con FleetQueries.LoadFleetDocumentsAsync (parte de Vehicles/Drivers bajo el filtro de tenant y
///   aplica la regla "vigente por tipo"); aquí no se reescribe la unión ni la regla.
/// - Solo dueños activos y no terminales; los documentos superados (renovados) no aparecen.
/// - Ventana: ExpiryDate ≤ hoy + withinDays; includeExpired=false quita los ya vencidos.
/// </summary>
public sealed class FleetDocumentService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    public const int MaxWithinDays = 365;
    public const string WithinDaysRangeMessage = "withinDays debe estar entre 0 y 365.";

    /// <summary>Tipos válidos del filtro docType[]: los cuatro tipos de documento de vehículo, LICENSE y CERTIFICATION.</summary>
    internal static readonly string[] KnownDocumentKinds =
    {
        FleetDocumentKinds.Registration, FleetDocumentKinds.Insurance, FleetDocumentKinds.Inspection, FleetDocumentKinds.Permit,
        FleetDocumentKinds.License, FleetDocumentKinds.Certification,
    };

    internal static readonly string[] KnownOwnerKinds = { FleetOwnerKinds.Vehicle, FleetOwnerKinds.Driver };

    public static string UnknownDocTypeMessage(string code) => $"Tipo de documento desconocido: '{code}'.";
    public static string UnknownEntityMessage(string code) => $"Entidad desconocida: '{code}'; use VEHICLE o DRIVER.";

    public async Task<IReadOnlyList<ExpiringDocumentDto>> GetExpiringAsync(ExpiringDocumentsQuery q, CancellationToken ct)
    {
        q ??= new ExpiringDocumentsQuery();
        if (q.WithinDays is < 0 or > MaxWithinDays) throw new ValidationException("withinDays", WithinDaysRangeMessage);

        var kinds = SplitCodes(q.DocType);
        var unknownKind = kinds.FirstOrDefault(k => !KnownDocumentKinds.Contains(k));
        if (unknownKind is not null) throw new ValidationException("docType", UnknownDocTypeMessage(unknownKind));

        var owners = SplitCodes(q.Entity);
        var unknownOwner = owners.FirstOrDefault(o => !KnownOwnerKinds.Contains(o));
        if (unknownOwner is not null) throw new ValidationException("entity", UnknownEntityMessage(unknownOwner));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var until = today.AddDays(q.WithinDays);

        // Qué dueños consultar: por el filtro entity[] y, si docType[] solo pide licencias/certificaciones (o solo tipos de
        // vehículo), se evita la consulta que no aporta nada.
        var wantsVehicleKinds = kinds.Count == 0 || kinds.Any(k => k != FleetDocumentKinds.License && k != FleetDocumentKinds.Certification);
        var wantsDriverKinds = kinds.Count == 0 || kinds.Any(k => k == FleetDocumentKinds.License || k == FleetDocumentKinds.Certification);
        var includeVehicles = (owners.Count == 0 || owners.Contains(FleetOwnerKinds.Vehicle)) && wantsVehicleKinds;
        var includeDrivers = (owners.Count == 0 || owners.Contains(FleetOwnerKinds.Driver)) && wantsDriverKinds;
        if (!includeVehicles && !includeDrivers) return Array.Empty<ExpiringDocumentDto>();

        var rows = await db.LoadFleetDocumentsAsync(today,
            new FleetDocumentScope(IncludeVehicles: includeVehicles, IncludeDrivers: includeDrivers, OnlyActiveOwners: true), ct);

        var selected = Expiring(rows, today, until, q.IncludeExpired)
            .Where(r => kinds.Count == 0 || kinds.Contains(KindOf(r)))
            .OrderBy(r => r.ExpiryDate!.Value).ThenBy(r => r.OwnerCode, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.RowKey, StringComparer.Ordinal)
            .ToList();

        var labels = new Dictionary<int, string>();
        var result = new List<ExpiringDocumentDto>(selected.Count);
        foreach (var r in selected)
        {
            var expiry = r.ExpiryDate!.Value;
            result.Add(new ExpiringDocumentDto(r.RowKey, r.OwnerKind, r.OwnerPublicId, r.OwnerCode, r.OwnerName, KindOf(r),
                r.DocTypeCode, await LabelAsync(lookups, labels, r, tenant.Lang, ct), r.DocNumber, r.IssuedDate, expiry,
                FleetRules.DaysTo(expiry, today), FleetRules.ExpiryState(expiry, today, q.WithinDays)));
        }
        return result;
    }

    /// <summary>
    /// Filas que cuentan como "por vencer": dueño activo y no terminal, documento no superado, con vencimiento y dentro de la
    /// ventana (≤ <paramref name="until"/>); sin vencidos si <paramref name="includeExpired"/> es false. La comparten el panel
    /// y la fuente FLEET_DOCUMENT.
    /// </summary>
    internal static IEnumerable<FleetDocumentRow> Expiring(IEnumerable<FleetDocumentRow> rows, DateOnly today, DateOnly? until, bool includeExpired)
        => rows.Where(r => r.OwnerIsActive && !r.OwnerIsTerminal && !r.IsSuperseded && r.ExpiryDate.HasValue)
            .Where(r => until is null || r.ExpiryDate!.Value <= until.Value)
            .Where(r => includeExpired || r.ExpiryDate!.Value >= today);

    /// <summary>
    /// Clase del documento en términos de FleetDocumentKinds: el tipo de documento (REGISTRATION, INSURANCE, …) en un
    /// vehículo; LICENSE o CERTIFICATION en un chofer.
    /// </summary>
    internal static string KindOf(FleetDocumentRow r)
        => r.OwnerKind == FleetOwnerKinds.Vehicle ? r.DocTypeCode : r.DocumentKind;

    /// <summary>Etiqueta multilingüe del tipo (LookupCode) con caché local por id; si no está en caché, el código.</summary>
    internal static async Task<string> LabelAsync(ILookupCache lookups, Dictionary<int, string> cache, FleetDocumentRow r, string lang, CancellationToken ct)
    {
        if (cache.TryGetValue(r.DocTypeLookupId, out var label)) return label;
        var lc = await lookups.GetAsync(r.DocTypeLookupId, ct);
        label = lc is null ? r.DocTypeCode : MultilingualText.Resolve(lc.LabelJson, lang);
        cache[r.DocTypeLookupId] = label;
        return label;
    }

    /// <summary>Filtro multi-valor (acepta ?x=A&amp;x=B y ?x=A,B), en mayúsculas y sin repetidos.</summary>
    private static List<string> SplitCodes(string[]? codes)
        => codes is null
            ? new List<string>()
            : codes.Where(c => c is not null)
                .SelectMany(c => c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(c => c.ToUpperInvariant()).Distinct().ToList();
}
