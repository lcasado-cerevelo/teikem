using Teikem.Domain.Constants;

namespace Teikem.Domain.Orders;

/// <summary>Lote 89 (P12) — Radar de órdenes abiertas del Pulso: qué es «abierta» y cómo se agrupa. Solo lo abierto: lo entregado, cancelado o fallido no estorba aquí, y un borrador todavía no es una orden en curso.</summary>
public static class RadarRules
{
    public const string GroupByTown = "TOWN";
    public const string GroupByClient = "CLIENT";
    public const int DefaultTake = 6;
    public const int MaxTake = 20;

    public static readonly string[] OpenStatuses =
    {
        OrderStatuses.Confirmed, OrderStatuses.Pickup, OrderStatuses.Inbound, OrderStatuses.Planned, OrderStatuses.InTransit, OrderStatuses.Arrived, OrderStatuses.OnHold, OrderStatuses.Partial,
    };

    public const string GroupByInvalid = "Agrupe por pueblo (TOWN) o por cliente (CLIENT).";

    /// <summary>Normaliza el agrupar por (sin distinguir mayúsculas; vacío = por pueblo). null = no válido.</summary>
    public static string? NormalizeGroupBy(string? value)
    {
        var v = string.IsNullOrWhiteSpace(value) ? GroupByTown : value.Trim().ToUpperInvariant();
        return v is GroupByTown or GroupByClient ? v : null;
    }

    public static int ClampTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
}
