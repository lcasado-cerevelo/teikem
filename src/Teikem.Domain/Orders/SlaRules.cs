using Teikem.Domain.Constants;

namespace Teikem.Domain.Orders;

/// <summary>Estados del SLA de una orden (Lote 122, P12): ON_TRACK (a tiempo), AT_RISK (en riesgo de pasarlo), BREACHED (ya lo pasó y sigue abierta), MET (se entregó dentro del SLA), MISSED (se entregó fuera del SLA) y NO_SLA (no aplica).</summary>
public static class SlaStates
{
    public const string OnTrack = "ON_TRACK", AtRisk = "AT_RISK", Breached = "BREACHED", Met = "MET", Missed = "MISSED", NoSla = "NO_SLA";
    public static readonly string[] All = { OnTrack, AtRisk, Breached, Met, Missed, NoSla };
}

/// <summary>El resultado de evaluar el SLA de una orden: el estado, el vencimiento y cuántas horas quedan (negativo = ya pasó; nulo si no aplica o ya se entregó).</summary>
public sealed record SlaResult(string State, int? MaxHours, DateTime? DueUtc, double? HoursLeft)
{
    public static readonly SlaResult None = new(SlaStates.NoSla, null, null, null);
}

/// <summary>
/// Lote 122 (P12) — el SLA de una orden (el nivel de servicio del contrato por tipo de servicio: <c>MaxTransitHours</c>). El reloj corre desde que la orden se RECOGE (primer paso a «Recogida»; si no hay, desde que se confirmó) hasta que
/// se ENTREGA. Una orden abierta que ya se recogió está «en riesgo» cuando le queda menos del 25 % del plazo (y al menos una hora) y «vencida» cuando ya lo pasó; una entregada es «cumplida» o «fuera de SLA» según cuándo se entregó. Una
/// orden que todavía no se recoge, cancelada, devuelta, en almacén (pick up) o de un tipo de servicio sin SLA no aplica. Pura: sin base de datos ni reloj propio.
/// </summary>
public static class SlaRules
{
    /// <summary>Fracción del plazo que, al quedar, pone la orden «en riesgo».</summary>
    public const double RiskFraction = 0.25;
    public static readonly TimeSpan MinRiskWindow = TimeSpan.FromHours(1);

    /// <summary>Estatus abiertos en los que el reloj corre (ya se recogieron y siguen en curso).</summary>
    public static readonly string[] RunningStatuses = { OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.Assigned, OrderStatuses.InTransit, OrderStatuses.Failed };

    public static SlaResult Evaluate(int? maxHours, DateTime? startUtc, DateTime? deliveredUtc, string? statusCode, DateTime nowUtc)
    {
        if (maxHours is not int max || max <= 0 || startUtc is not DateTime start) return SlaResult.None;
        var due = start.AddHours(max);
        if (string.Equals(statusCode, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
        {
            var at = deliveredUtc ?? due;
            return new SlaResult(at <= due ? SlaStates.Met : SlaStates.Missed, max, due, null);
        }
        if (!RunningStatuses.Contains(statusCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)) return SlaResult.None;
        var left = (due - nowUtc).TotalHours;
        var riskWindow = Math.Max(MinRiskWindow.TotalHours, max * RiskFraction);
        var state = left < 0 ? SlaStates.Breached : left <= riskWindow ? SlaStates.AtRisk : SlaStates.OnTrack;
        return new SlaResult(state, max, due, Math.Round(left, 1));
    }
}
