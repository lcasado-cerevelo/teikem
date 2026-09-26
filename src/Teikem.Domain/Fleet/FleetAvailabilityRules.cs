using System.Globalization;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Lote 4 (P3) — reglas puras de disponibilidad para despacho (R7). Evalúan un snapshot ya armado (sin BD) y devuelven
/// los problemas encontrados; el recurso está disponible si ninguno es bloqueante. Las consume
/// FleetAvailabilityService (panel, entrega especial con chofer) y las consumirá tal cual el planificador de Despacho.
/// - Los documentos IsSuperseded (hay otro activo del mismo tipo con vencimiento posterior) se IGNORAN por completo:
///   un documento vencido que ya se renovó no bloquea ni avisa.
/// - Un documento vence el día siguiente a su ExpiryDate: el mismo día de vencimiento todavía es válido (y "por vencer").
/// - Bloquean: DRIVER_INACTIVE, DRIVER_STATUS, NO_VALID_LICENSE, VEHICLE_INACTIVE, VEHICLE_STATUS, VEHICLE_DOC_EXPIRED y
///   WORK_ORDER_IN_PROGRESS. Solo avisan: CERT_EXPIRED, DOC_EXPIRING (≤ 30 días) y VEHICLE_NO_DOCUMENTS.
/// - El mantenimiento preventivo vencido NO se evalúa aquí (decisión del lote).
/// </summary>
public static class FleetAvailabilityRules
{
    /// <summary>Ventana de aviso "por vencer" (días), la misma que FleetRules.ExpiryState por defecto.</summary>
    public const int ExpiringWithinDays = 30;

    public const string DriverInactiveMessage = "El chofer está inactivo.";
    public const string VehicleInactiveMessage = "El vehículo está inactivo.";
    public const string NoValidLicenseMessage = "Sin licencia vigente.";
    public const string VehicleNoDocumentsMessage = "El vehículo no tiene documentos registrados.";

    public static string DriverStatusMessage(string statusLabel) => $"Chofer en estatus '{statusLabel}'.";
    public static string VehicleStatusMessage(string statusLabel) => $"Vehículo en estatus '{statusLabel}'.";
    public static string LicenseExpiredMessage(DateOnly expiry) => $"Licencia vencida el {Format(expiry)}.";
    public static string LicenseExpiringMessage(string label, DateOnly expiry) => $"Licencia {label} vence el {Format(expiry)}.";
    public static string CertificationExpiredMessage(string label, DateOnly expiry) => $"Certificación {label} vencida el {Format(expiry)}.";
    public static string CertificationExpiringMessage(string label, DateOnly expiry) => $"Certificación {label} vence el {Format(expiry)}.";
    public static string VehicleDocExpiredMessage(string label, DateOnly expiry) => $"{label} vencido el {Format(expiry)}.";
    public static string VehicleDocExpiringMessage(string label, DateOnly expiry) => $"{label} vence el {Format(expiry)}.";
    public static string WorkOrderInProgressMessage(string number) => $"Orden de trabajo {number} en proceso.";

    /// <summary>Disponibilidad de un chofer en <paramref name="date"/>.</summary>
    public static AvailabilityResult EvaluateDriver(DriverAvailabilitySnapshot s, DateOnly date)
    {
        var blocking = new List<AvailabilityIssue>();
        var warnings = new List<AvailabilityIssue>();

        if (!s.IsActive) blocking.Add(new AvailabilityIssue(AvailabilityIssueCodes.DriverInactive, DriverInactiveMessage, true));
        if (!s.IsInitialStatus)
            blocking.Add(new AvailabilityIssue(AvailabilityIssueCodes.DriverStatus, DriverStatusMessage(LabelOf(s.StatusLabel, "?")), true));

        // Licencias: basta una vigente (no superada, no vencida). Sin ninguna registrada → 'Sin licencia vigente.';
        // todas vencidas → 'Licencia vencida el {la más reciente}.'
        var licenses = Current(s.Licenses);
        if (licenses.Count == 0)
            blocking.Add(new AvailabilityIssue(AvailabilityIssueCodes.NoValidLicense, NoValidLicenseMessage, true));
        else
        {
            var valid = licenses.Where(l => !IsExpired(l.ExpiryDate, date)).ToList();
            if (valid.Count == 0)
            {
                var latest = licenses.Max(l => l.ExpiryDate!.Value);
                blocking.Add(new AvailabilityIssue(AvailabilityIssueCodes.NoValidLicense, LicenseExpiredMessage(latest), true));
            }
            else
            {
                foreach (var l in valid.Where(l => IsExpiring(l.ExpiryDate, date)))
                    warnings.Add(new AvailabilityIssue(AvailabilityIssueCodes.DocExpiring, LicenseExpiringMessage(LabelOf(l.TypeLabel, l.TypeCode), l.ExpiryDate!.Value), false));
            }
        }

        // Certificaciones: solo avisan (vencida o por vencer).
        foreach (var c in Current(s.Certifications))
        {
            if (IsExpired(c.ExpiryDate, date))
                warnings.Add(new AvailabilityIssue(AvailabilityIssueCodes.CertExpired, CertificationExpiredMessage(LabelOf(c.TypeLabel, c.TypeCode), c.ExpiryDate!.Value), false));
            else if (IsExpiring(c.ExpiryDate, date))
                warnings.Add(new AvailabilityIssue(AvailabilityIssueCodes.DocExpiring, CertificationExpiringMessage(LabelOf(c.TypeLabel, c.TypeCode), c.ExpiryDate!.Value), false));
        }

        return Result(blocking, warnings);
    }

    /// <summary>Disponibilidad de un vehículo en <paramref name="date"/>.</summary>
    public static AvailabilityResult EvaluateVehicle(VehicleAvailabilitySnapshot s, DateOnly date)
    {
        var blocking = new List<AvailabilityIssue>();
        var warnings = new List<AvailabilityIssue>();

        if (!s.IsActive) blocking.Add(new AvailabilityIssue(AvailabilityIssueCodes.VehicleInactive, VehicleInactiveMessage, true));
        if (!s.IsInitialStatus)
            blocking.Add(new AvailabilityIssue(AvailabilityIssueCodes.VehicleStatus, VehicleStatusMessage(LabelOf(s.StatusLabel, "?")), true));

        var docs = Current(s.Documents);
        if (s.Documents.Count == 0)
            warnings.Add(new AvailabilityIssue(AvailabilityIssueCodes.VehicleNoDocuments, VehicleNoDocumentsMessage, false));

        foreach (var d in docs)
        {
            if (IsExpired(d.ExpiryDate, date))
                blocking.Add(new AvailabilityIssue(AvailabilityIssueCodes.VehicleDocExpired, VehicleDocExpiredMessage(LabelOf(d.TypeLabel, d.TypeCode), d.ExpiryDate!.Value), true));
            else if (IsExpiring(d.ExpiryDate, date))
                warnings.Add(new AvailabilityIssue(AvailabilityIssueCodes.DocExpiring, VehicleDocExpiringMessage(LabelOf(d.TypeLabel, d.TypeCode), d.ExpiryDate!.Value), false));
        }

        foreach (var number in s.InProgressWorkOrderNumbers.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal))
            blocking.Add(new AvailabilityIssue(AvailabilityIssueCodes.WorkOrderInProgress, WorkOrderInProgressMessage(number), true));

        return Result(blocking, warnings);
    }

    /// <summary>Vencido: la fecha de vencimiento ya pasó (ExpiryDate &lt; date). Sin vencimiento nunca vence.</summary>
    public static bool IsExpired(DateOnly? expiry, DateOnly date) => expiry is DateOnly e && e < date;

    /// <summary>Por vencer: vence hoy o dentro de los próximos 30 días.</summary>
    public static bool IsExpiring(DateOnly? expiry, DateOnly date)
        => expiry is DateOnly e && e >= date && e.DayNumber - date.DayNumber <= ExpiringWithinDays;

    /// <summary>Documentos que cuentan: los no superados, ordenados por vencimiento (los sin vencimiento al final).</summary>
    private static List<AvailabilityDocSnapshot> Current(IReadOnlyList<AvailabilityDocSnapshot>? docs)
        => (docs ?? Array.Empty<AvailabilityDocSnapshot>())
            .Where(d => !d.IsSuperseded)
            .OrderBy(d => d.ExpiryDate ?? DateOnly.MaxValue).ThenBy(d => d.TypeCode, StringComparer.Ordinal)
            .ToList();

    private static AvailabilityResult Result(List<AvailabilityIssue> blocking, List<AvailabilityIssue> warnings)
        => new(blocking.Count == 0, blocking.Concat(warnings).ToList());

    private static string LabelOf(string? label, string fallback) => string.IsNullOrWhiteSpace(label) ? fallback : label;

    private static string Format(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
