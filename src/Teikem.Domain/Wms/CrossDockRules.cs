using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Asignación PLANNED que entra al reparto de la confirmación, en orden de creación (CreatedAtUtc, Id).</summary>
public sealed record CrossDockSplitInput(int AllocationId, decimal AllocatedQty);

/// <summary>Parte de una asignación en el reparto: lo confirmado (cubierto) y el faltante outbound visible.</summary>
public sealed record CrossDockSplitShare(int AllocationId, decimal AllocatedQty, decimal ConfirmedQty)
{
    public decimal ShortQty => AllocatedQty - ConfirmedQty;
}

/// <summary>Resultado del reparto: partes por asignación y remanente que va a putaway.</summary>
public sealed record CrossDockSplitResult(IReadOnlyList<CrossDockSplitShare> Shares, decimal Remainder)
{
    public decimal TotalConfirmed => Shares.Sum(s => s.ConfirmedQty);
    public decimal TotalShort => Shares.Sum(s => s.ShortQty);
}

/// <summary>
/// Lote 6 (P9) — reglas puras del cruce de muelle (R20-R22, maestro L316-L320, D29). Los mensajes son públicos porque el
/// manual y la FAQ los citan literalmente.
/// Dos modos de asignación:
/// - (a) sobre un recibo ABIERTO: se asigna contra lo recibido de la línea (AllocatableOpen). Al confirmar el recibo, Split
///   reparte FIFO lo realmente recibido entre las asignaciones: ConfirmedQty por asignación, ShortQty = AllocatedQty −
///   ConfirmedQty (el faltante outbound visible de L320) y el remanente va a putaway.
/// - (b) sobre un recibo ya confirmado: se asigna contra lo recibido no comprometido, limitado por el putaway pendiente
///   (AllocatableConfirmed); la asignación reduce la PUTAWAY y reserva el saldo de staging.
/// </summary>
public static class CrossDockRules
{
    public const int QuantityDecimals = 3;

    public static string Exceeds(decimal allocatable)
        => $"La cantidad excede lo disponible para cruce de muelle ({FormatQty(allocatable)}).";

    public const string ReceiptNotConfirmed = "La recepción de la línea todavía no se confirma; la mercancía se mueve después de confirmar.";
    public const string NothingConfirmed = "La asignación no recibió mercancía al confirmar; cancélela.";

    public static string ExactQty(decimal confirmedQty)
        => $"La tarea de cruce de muelle se completa por la cantidad confirmada ({FormatQty(confirmedQty)}).";

    public const string StagingZone = "La mercancía debe estar en la zona de staging del plan.";
    public const string OrderNotShippable = "La orden no admite asignaciones (cancelada, entregada o dada de baja).";
    public const string PlanNotOpen = "El plan ya fue completado.";
    public const string CannotComplete = "Mueva o cancele las asignaciones pendientes antes de completar el plan.";

    // Mensajes adicionales (también citados por el manual).
    public const string QtyPositive = "La cantidad debe ser mayor que cero.";
    public const string QtyDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string SerialInteger = "En productos con serie la cantidad debe ser entera.";
    public const string AllocationNotPlanned = "La asignación ya fue movida o cancelada.";
    public const string ReceiptNotAllocatable = "El recibo de la línea no admite asignaciones (eliminado o sin putaway pendiente).";
    public const string StagingZoneType = "La zona de staging del plan debe ser de tipo CROSSDOCK o STAGING.";
    public const string StagingZoneInactive = "La zona de staging del plan está inactiva.";
    public const string WarehouseMismatch = "La línea del recibo pertenece a otro almacén que el plan.";
    public const string CargoLineNotOfOrder = "La línea de carga no pertenece a la orden.";
    public const string ReceiptLineRequired = "Indique la línea del recibo.";
    public const string OrderRequired = "Indique la orden.";
    public const string SerialsNotInStaging = "Las series de la línea ya no están disponibles en la posición de staging; cancele la asignación.";

    /// <summary>Estatus del plan que admiten asignar, cancelar y mover (COMPLETED es terminal).</summary>
    public static readonly IReadOnlyList<string> OpenPlanStatuses = new[] { CrossDockStatuses.Open, CrossDockStatuses.Allocated };

    public static bool IsPlanOpen(string? statusCode) => OpenPlanStatuses.Any(s => Eq(s, statusCode));

    /// <summary>¿El tipo de zona sirve como staging de un plan? CROSSDOCK o STAGING.</summary>
    public static bool IsStagingZoneType(string? zoneTypeCode)
        => Eq(zoneTypeCode, ZoneTypes.CrossDock) || Eq(zoneTypeCode, ZoneTypes.Staging);

    /// <summary>Cantidad válida: &gt; 0, máximo 3 decimales y entera si el producto lleva serie. Mensaje o null.</summary>
    public static string? ValidateQuantity(decimal? qty, bool isSerial)
    {
        if (qty is not decimal q || q <= 0m) return QtyPositive;
        if (decimal.Round(q, QuantityDecimals) != q) return QtyDecimals;
        if (isSerial && decimal.Truncate(q) != q) return SerialInteger;
        return null;
    }

    /// <summary>Modo (a), recibo ABIERTO: lo recibido de la línea menos lo ya asignado (activo), nunca negativo.</summary>
    public static decimal AllocatableOpen(decimal baseQty, decimal activeAllocated) => Math.Max(0m, baseQty - activeAllocated);

    /// <summary>
    /// Modo (b), recibo confirmado: lo recibido no comprometido con otras asignaciones (activas o movidas), limitado por el
    /// putaway todavía pendiente de esa mercancía en staging. Nunca negativo.
    /// </summary>
    public static decimal AllocatableConfirmed(decimal received, decimal activeAllocated, decimal pendingPutaway)
        => Math.Max(0m, Math.Min(received - activeAllocated, pendingPutaway));

    /// <summary>
    /// Reparto FIFO de lo recibido entre las asignaciones PLANNED de la línea, en el orden dado (CreatedAtUtc, Id): cada una
    /// recibe hasta lo asignado mientras alcance; lo que sobra es el remanente que va a putaway.
    /// Ejemplo: recibido 10 con asignaciones 4 y 8 → 4 y 6 (faltante 2), remanente 0; recibido 15 → 4 y 8, remanente 3.
    /// </summary>
    public static CrossDockSplitResult Split(decimal received, IReadOnlyList<CrossDockSplitInput> allocations)
    {
        var left = Math.Max(0m, received);
        var shares = new List<CrossDockSplitShare>(allocations.Count);
        foreach (var a in allocations)
        {
            var confirmed = Math.Max(0m, Math.Min(a.AllocatedQty, left));
            left -= confirmed;
            shares.Add(new CrossDockSplitShare(a.AllocationId, a.AllocatedQty, confirmed));
        }
        return new CrossDockSplitResult(shares, left);
    }

    /// <summary>Faltante outbound de una asignación: 0 mientras el recibo no se confirma (ConfirmedQty null).</summary>
    public static decimal ShortQty(decimal allocatedQty, decimal? confirmedQty)
        => confirmedQty is decimal c ? Math.Max(0m, allocatedQty - c) : 0m;

    /// <summary>¿Se puede completar el plan? Solo si ninguna asignación sigue PLANNED.</summary>
    public static bool CanComplete(IEnumerable<string?> allocationStatusCodes)
        => !allocationStatusCodes.Any(s => Eq(s, AllocationStatuses.Planned));

    /// <summary>¿La orden admite asignaciones? Activa y ni cancelada, ni entregada, ni en otra etapa terminal.</summary>
    public static bool IsOrderShippable(bool isActive, string? statusCode, string? stageKind)
    {
        if (!isActive) return false;
        if (Eq(statusCode, OrderStatuses.Cancelled) || Eq(statusCode, OrderStatuses.Delivered)) return false;
        return !Eq(stageKind, StageKinds.Terminal);
    }

    /// <summary>
    /// Reparte una reducción entre las PUTAWAY pendientes (en orden): cuánto se quita a cada una. Devuelve la lista de
    /// (índice, cantidad a restar); si no alcanza, reparte lo que haya.
    /// </summary>
    public static IReadOnlyList<(int Index, decimal Reduce)> ReducePlan(IReadOnlyList<decimal> pendingQuantities, decimal qty)
    {
        var left = qty;
        var result = new List<(int, decimal)>();
        for (var i = 0; i < pendingQuantities.Count && left > 0m; i++)
        {
            var take = Math.Min(pendingQuantities[i], left);
            if (take <= 0m) continue;
            result.Add((i, take));
            left -= take;
        }
        return result;
    }

    public static string FormatQty(decimal qty)
        => qty.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
