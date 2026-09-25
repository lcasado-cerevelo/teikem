using System.Text.RegularExpressions;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Clients;

/// <summary>
/// Reglas puras del contrato (Lote 2, P3): sin EF ni servicios, para poder probarlas con xunit.
/// - Cargo por COD: fijo (monto por orden, >= 0) o por ciento del monto COD cobrado (0..100). No existe "base" del cargo
///   (decisión de Luis): el por ciento es siempre sobre el COD, nunca sobre el total de la orden.
/// - Niveles de servicio: un solo nivel por tipo de servicio, MaxTransitHours > 0, OnTimeTargetPct 0..100.
/// - Numeración: '{Code}-C{n+1}' sobre los números ya existentes del cliente.
/// - Activación: un solo contrato ACTIVE por cliente y nunca con el cliente SUSPENDED. La fecha fin NO se evalúa:
///   no hay vencimiento automático (un contrato con EndDate pasada sigue vigente hasta que alguien lo pase a EXPIRED/CANCELLED).
/// </summary>
public static class ContractRules
{
    public const int TitleMaxLength = 200;
    public const int ContractNumberMaxLength = 40;

    /// <summary>Valida tipo y valor del cargo por COD. Lanza ValidationException (400) con el campo afectado.</summary>
    public static void ValidateCodFee(string? type, decimal? value)
    {
        var t = type?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(t))
            throw new ValidationException("type", "Indique el tipo del cargo por COD: FIXED (monto fijo por orden) o PERCENT (por ciento del monto COD).");
        if (value is null)
            throw new ValidationException("value", "Indique el valor del cargo por COD.");
        switch (t)
        {
            case PricingTypes.Fixed:
                if (value.Value < 0) throw new ValidationException("value", "El cargo fijo por COD no puede ser negativo.");
                break;
            case PricingTypes.Percent:
                if (value.Value < 0 || value.Value > 100) throw new ValidationException("value", "El por ciento del cargo por COD debe estar entre 0 y 100.");
                break;
            default:
                throw new ValidationException("type", $"Tipo de cargo por COD desconocido: '{type}'. Use FIXED o PERCENT.");
        }
    }

    /// <summary>
    /// Vigencia del contrato: la fecha fin, si existe, no puede ser anterior al inicio (400 con el campo indicado).
    /// La usan el alta (ContractService y el contrato inicial de ClientService) y la edición de fechas; CK_Contract_Dates
    /// es la segunda barrera en SQL.
    /// </summary>
    public static void ValidateDates(DateOnly start, DateOnly? end, string field = "endDate")
    {
        if (end is DateOnly e && e < start)
            throw new ValidationException(field, "La fecha fin no puede ser anterior a la fecha de inicio.");
    }

    /// <summary>
    /// Valida la lista completa de niveles de servicio: tipo obligatorio y no repetido, MaxTransitHours > 0,
    /// PickupWindowMin >= 0, OnTimeTargetPct 0..100 y PenaltyAmount >= 0. Lanza ValidationException (400).
    /// fieldPrefix permite anidar el campo del error (p. ej. 'contract.serviceLevels' en el alta compuesta del cliente).
    /// </summary>
    public static void ValidateServiceLevels(IEnumerable<ServiceLevelUpsert>? items, string fieldPrefix = "serviceLevels")
    {
        if (items is null) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        foreach (var it in items)
        {
            var field = $"{fieldPrefix}[{i}]";
            var code = it.ServiceType?.Trim();
            if (string.IsNullOrEmpty(code))
                throw new ValidationException($"{field}.serviceType", "El tipo de servicio del nivel es obligatorio.");
            if (!seen.Add(code))
                throw new ValidationException($"{field}.serviceType", $"El tipo de servicio '{code.ToUpperInvariant()}' está repetido: solo puede haber un nivel de servicio por tipo.");
            if (it.MaxTransitHours is <= 0)
                throw new ValidationException($"{field}.maxTransitHours", "Las horas máximas de tránsito deben ser mayores que cero.");
            if (it.PickupWindowMin is < 0)
                throw new ValidationException($"{field}.pickupWindowMin", "La ventana de recogido (minutos) no puede ser negativa.");
            if (it.OnTimeTargetPct is < 0 or > 100)
                throw new ValidationException($"{field}.onTimeTargetPct", "La meta de puntualidad debe estar entre 0 y 100.");
            if (it.PenaltyAmount is < 0)
                throw new ValidationException($"{field}.penaltyAmount", "La penalidad no puede ser negativa.");
            i++;
        }
    }

    /// <summary>
    /// Siguiente número de contrato del cliente: '{Code}-C{n+1}', con n = mayor sufijo numérico entre los existentes
    /// con el formato '{Code}-C{n}'. Números con otro formato (asignados a mano) se ignoran.
    /// </summary>
    public static string NextContractNumber(string clientCode, IEnumerable<string>? existingNumbers)
    {
        var code = (clientCode ?? string.Empty).Trim();
        if (code.Length == 0) throw new ValidationException("clientCode", "El cliente no tiene código; no se puede numerar el contrato.");
        var rx = new Regex("^" + Regex.Escape(code) + @"-C(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var max = 0;
        foreach (var n in existingNumbers ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            var m = rx.Match(n.Trim());
            if (m.Success && int.TryParse(m.Groups[1].Value, out var v) && v > max) max = v;
        }
        return $"{code}-C{max + 1}";
    }

    /// <summary>
    /// Regla de activación (ContractStatusEffect): el cliente no puede estar SUSPENDED y no puede tener ya otro
    /// contrato ACTIVE (un solo contrato activo por cliente a la vez). Lanza StatusRuleException (422).
    /// La fecha fin no interviene: es informativa y no vence nada.
    /// </summary>
    public static void EnsureCanActivate(string? clientStatusCode, bool clientHasOtherActiveContract)
    {
        if (string.Equals(clientStatusCode?.Trim(), ClientStatuses.Suspended, StringComparison.OrdinalIgnoreCase))
            throw new StatusRuleException("No se puede activar el contrato: el cliente está suspendido. Reactive al cliente primero.");
        if (clientHasOtherActiveContract)
            throw new StatusRuleException("El cliente ya tiene un contrato vigente. Cancele o expire el contrato anterior antes de activar este.");
    }
}
