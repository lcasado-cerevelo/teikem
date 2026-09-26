using System.Globalization;

namespace Teikem.Domain.Trips;

/// <summary>
/// Zona resuelta de la parada pendiente de la orden, tal como la ve la decisión del escaneo. El servicio la arma a partir de
/// DispatchZoneMatcher.Resolve (ZoneResolution); se desacopla aquí para que la regla sea pura y probable sin la BD.
/// - ZoneCode: código de la zona ganadora (null si no hay o si es ambigua).
/// - Ambiguous: la dirección empata en el mismo nivel de precedencia con varias zonas.
/// - Candidates: códigos de las zonas empatadas (solo con Ambiguous).
/// </summary>
public sealed record ScanZone(string? ZoneCode, bool Ambiguous, IReadOnlyList<string> Candidates)
{
    public static readonly ScanZone None = new(null, false, Array.Empty<string>());
}

/// <summary>
/// Hechos del escaneo, reunidos por OutboundScanService antes de decidir:
/// - MatchCount/MatchedBy: resultado del lookup exacto de /orders/lookup (número &gt; empaque &gt; factura).
/// - CurrentTripCode: ruta vigente de la orden (TripOrder IsCurrent), o null.
/// - IneligibleReason: motivo de TripRules.CheckEligibility, o null.
/// - Zone: zona de la parada DELIVERY pendiente, o null si no se pudo resolver.
/// - OpenTripCode: ruta abierta (DRAFT/PLANNED, activa) de esa zona en PlanDate, la de menor id, o null.
/// </summary>
public sealed record ScanFacts(
    int MatchCount,
    string? MatchedBy,
    string? OrderNumber,
    string? CurrentTripCode,
    string? IneligibleReason,
    ScanZone? Zone,
    string? OpenTripCode,
    DateOnly PlanDate);

/// <summary>Decisión del escaneo: resultado tipado, código de motivo (null si se asignó), mensaje exacto y palabra de voz.</summary>
public sealed record ScanDecision(string Outcome, string? ReasonCode, string Message, string Voice);

/// <summary>Resultados tipados de la estación de escaneo Outbound.</summary>
public static class ScanOutcomes
{
    public const string FoundAssigned = "FOUND_ASSIGNED";
    public const string FoundUnassigned = "FOUND_UNASSIGNED";
    public const string AlreadyAssigned = "ALREADY_ASSIGNED";
    public const string NotFound = "NOT_FOUND";
    public const string NotEligible = "NOT_ELIGIBLE";
}

/// <summary>Palabra que pronuncia la estación (el front la convierte en voz).</summary>
public static class ScanVoices
{
    public const string Found = "found";
    public const string Duplicate = "dup";
    public const string NotFound = "notfound";
}

/// <summary>Códigos de motivo del resultado del escaneo.</summary>
public static class ScanReasonCodes
{
    public const string NoMatch = "NO_MATCH";
    public const string MultipleMatches = "MULTIPLE_MATCHES";
    public const string InTrip = "IN_TRIP";
    public const string NotEligible = "NOT_ELIGIBLE";
    public const string NoZone = "NO_ZONE";
    public const string AmbiguousZone = "AMBIGUOUS_ZONE";
    public const string NoOpenRoute = "NO_OPEN_ROUTE";
}

/// <summary>
/// Lote 5 (P6) — reglas puras de la estación de escaneo Outbound. Los mensajes exactos viven aquí porque el manual y la FAQ
/// los citan. Decide aplica una precedencia fija:
/// 1. sin coincidencias → NOT_FOUND;
/// 2. varias coincidencias → NOT_FOUND (ambiguo: escanee el empaque);
/// 3. ya tiene ruta vigente → ALREADY_ASSIGNED (gana a 'no elegible': una orden ya despachada dice 'Ya');
/// 4. no elegible → NOT_ELIGIBLE con el motivo de TripRules;
/// 5. sin zona → FOUND_UNASSIGNED NO_ZONE;
/// 6. zona ambigua → FOUND_UNASSIGNED AMBIGUOUS_ZONE;
/// 7. sin ruta abierta de la zona en la fecha → FOUND_UNASSIGNED NO_OPEN_ROUTE;
/// 8. en otro caso → FOUND_ASSIGNED.
/// </summary>
public static class OutboundScanRules
{
    /// <summary>Largo máximo del código escaneado (el empaque y el número de orden caben de sobra).</summary>
    public const int MaxCodeLength = 40;

    public const string EmptyCodeMessage = "Escanee o escriba un código.";
    public const string CodeTooLongMessage = "El código no puede exceder 40 caracteres.";

    public const string NotFoundMessage = "No se encontró la orden.";
    public const string MultipleMatchesMessage = "Hay varias órdenes con ese código; escanee el empaque.";
    public const string NoZoneMessage = "No se pudo resolver la zona de despacho por código postal ni pueblo; queda sin asignar.";

    /// <summary>'Ya estaba en la ruta {código}.'</summary>
    public static string AlreadyAssignedMessage(string tripCode)
        => string.Format(CultureInfo.InvariantCulture, "Ya estaba en la ruta {0}.", tripCode);

    /// <summary>'El código postal o pueblo pertenece a varias zonas ({códigos}); queda sin asignar.'</summary>
    public static string AmbiguousZoneMessage(IEnumerable<string>? zoneCodes)
    {
        var codes = (zoneCodes ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal);
        return string.Format(CultureInfo.InvariantCulture,
            "El código postal o pueblo pertenece a varias zonas ({0}); queda sin asignar.", string.Join(", ", codes));
    }

    /// <summary>'No hay ruta abierta para la zona {código} en la fecha {yyyy-MM-dd}; queda sin asignar.'</summary>
    public static string NoOpenRouteMessage(string zoneCode, DateOnly planDate)
        => string.Format(CultureInfo.InvariantCulture,
            "No hay ruta abierta para la zona {0} en la fecha {1}; queda sin asignar.",
            zoneCode, planDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>'Asignada a la ruta {código}.'</summary>
    public static string AssignedMessage(string tripCode)
        => string.Format(CultureInfo.InvariantCulture, "Asignada a la ruta {0}.", tripCode);

    /// <summary>Recorta espacios y valida el código escaneado. Devuelve el código normalizado o el mensaje de error (400).</summary>
    public static (string? Code, string? Error) NormalizeCode(string? raw)
    {
        var code = raw?.Trim();
        if (string.IsNullOrEmpty(code)) return (null, EmptyCodeMessage);
        if (code.Length > MaxCodeLength) return (null, CodeTooLongMessage);
        return (code, null);
    }

    /// <summary>Palabra de voz de un resultado: FOUND_* → found; ALREADY_ASSIGNED → dup; NOT_FOUND y NOT_ELIGIBLE → notfound.</summary>
    public static string Voice(string outcome) => outcome switch
    {
        ScanOutcomes.FoundAssigned or ScanOutcomes.FoundUnassigned => ScanVoices.Found,
        ScanOutcomes.AlreadyAssigned => ScanVoices.Duplicate,
        _ => ScanVoices.NotFound,
    };

    /// <summary>Decisión del escaneo con la precedencia fija documentada en la clase.</summary>
    public static ScanDecision Decide(ScanFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.MatchCount <= 0)
            return Make(ScanOutcomes.NotFound, ScanReasonCodes.NoMatch, NotFoundMessage);
        if (facts.MatchCount > 1)
            return Make(ScanOutcomes.NotFound, ScanReasonCodes.MultipleMatches, MultipleMatchesMessage);

        if (!string.IsNullOrWhiteSpace(facts.CurrentTripCode))
            return Make(ScanOutcomes.AlreadyAssigned, ScanReasonCodes.InTrip, AlreadyAssignedMessage(facts.CurrentTripCode!));

        if (!string.IsNullOrWhiteSpace(facts.IneligibleReason))
            return Make(ScanOutcomes.NotEligible, ScanReasonCodes.NotEligible, facts.IneligibleReason!);

        var zone = facts.Zone;
        if (zone is not null && zone.Ambiguous)
            return Make(ScanOutcomes.FoundUnassigned, ScanReasonCodes.AmbiguousZone, AmbiguousZoneMessage(zone.Candidates));
        if (zone is null || string.IsNullOrWhiteSpace(zone.ZoneCode))
            return Make(ScanOutcomes.FoundUnassigned, ScanReasonCodes.NoZone, NoZoneMessage);

        if (string.IsNullOrWhiteSpace(facts.OpenTripCode))
            return Make(ScanOutcomes.FoundUnassigned, ScanReasonCodes.NoOpenRoute, NoOpenRouteMessage(zone.ZoneCode!, facts.PlanDate));

        return Make(ScanOutcomes.FoundAssigned, null, AssignedMessage(facts.OpenTripCode!));
    }

    private static ScanDecision Make(string outcome, string? reasonCode, string message)
        => new(outcome, reasonCode, message, Voice(outcome));
}
