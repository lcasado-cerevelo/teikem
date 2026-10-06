using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 29 (Rentas R3) — reglas puras de los reportes, indicadores y avisos de rentas: la ventana de "por vencer" (7 días), los
/// filtros sembrados de los indicadores y vistas de sistema (nombres de campo de RentalDataSource, RentalReturnDataSource y
/// RentalProcessDataSource), el resumen de condiciones de una devolución, los días de anticipación y los días en proceso. Sin BD.
/// "Vencida" y "por vencer" son datos calculados con el día de la compañía (RentalRules.IsOverdue / IsDueWithin), no estatus.
/// </summary>
public static class RentalAnalyticsRules
{
    /// <summary>Ventana de "por vencer" del aviso "Necesita tu atención" y del indicador "Rentas por vencer (7 días)".</summary>
    public const int DueSoonDays = 7;

    // ---------------------------------------------------------------- contenido de sistema (nombres estables)

    public const string DueSoonIndicatorName = "Rentas por vencer (7 días)";
    public const string OverdueIndicatorName = "Rentas vencidas";
    public const string ReturnsByReasonChartName = "Devoluciones de renta por motivo";
    public const string OnRentByClientReportName = "Equipos en renta por cliente";
    public const string DueSoonReportName = "Rentas por vencer (7 días)";
    public const string OverdueReportName = "Rentas vencidas";
    public const string ReturnsByReasonReportName = "Devoluciones de renta por motivo";
    public const string InProcessReportName = "Equipos en proceso";

    /// <summary>Por vencer = abierta (Programada o En renta) con la fecha de recogido entre hoy y hoy + 7 (ambos inclusive).</summary>
    public const string DueSoonFilter =
        "{\"and\":[{\"field\":\"IsOpen\",\"op\":\"isTrue\"},{\"field\":\"DaysToPickup\",\"op\":\"gte\",\"value\":0},{\"field\":\"DaysToPickup\",\"op\":\"lte\",\"value\":7}]}";

    /// <summary>Vencida = abierta con la fecha de recogido antes de hoy (campo IsOverdue de la fuente RENTAL).</summary>
    public const string OverdueFilter = "{\"and\":[{\"field\":\"IsOverdue\",\"op\":\"isTrue\"}]}";

    /// <summary>En renta hoy = rentas En renta (equipo en el cliente).</summary>
    public const string OnRentFilter = "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"ON_RENT\"}]}";

    /// <summary>Equipos en proceso = procesos abiertos (ni Lista ni Dada de baja).</summary>
    public const string OpenProcessFilter = "{\"and\":[{\"field\":\"IsOpen\",\"op\":\"isTrue\"}]}";

    /// <summary>Vista agrupada 'Equipos en renta por cliente': rentas y equipos en el cliente por cliente, con totales.</summary>
    public const string OnRentByClientGroup =
        "{\"by\":[\"ClientName\"],\"aggregates\":[{\"fn\":\"COUNT\"},{\"fn\":\"SUM\",\"field\":\"UnitsOnRent\"}],\"totals\":true}";

    /// <summary>Vista agrupada 'Devoluciones de renta por motivo': devoluciones y equipos devueltos por motivo, con totales.</summary>
    public const string ReturnsByReasonGroup =
        "{\"by\":[\"Reason\"],\"aggregates\":[{\"fn\":\"COUNT\"},{\"fn\":\"SUM\",\"field\":\"Units\"}],\"totals\":true}";

    // ---------------------------------------------------------------- cálculos

    /// <summary>¿Abierta? (Programada o En renta: el equipo está apartado o en el cliente).</summary>
    public static bool IsOpen(string? statusCode) => RentalRules.IsOpen(statusCode);

    /// <summary>¿Entra en el aviso "Necesita tu atención"? Abierta y vencida o con el recogido en los próximos 7 días.</summary>
    public static bool NeedsAttention(string? statusCode, DateOnly pickup, DateOnly today)
        => RentalRules.IsOverdue(statusCode, pickup, today) || RentalRules.IsDueWithin(statusCode, pickup, today, DueSoonDays);

    /// <summary>Días de anticipación de una devolución: cuántos días antes de la fecha de recogido vigente volvió (0 si no fue anticipada).</summary>
    public static int DaysEarly(DateOnly returnedOn, DateOnly pickupDate) => Math.Max(0, pickupDate.DayNumber - returnedOn.DayNumber);

    /// <summary>Días de calendario (de la compañía) en proceso: del día de inicio al de fin, o a hoy si sigue abierto; nunca negativo.</summary>
    public static int DaysInProcess(DateOnly startedOn, DateOnly? completedOn, DateOnly today)
        => Math.Max(0, (completedOn ?? today).DayNumber - startedOn.DayNumber);

    /// <summary>Orden fijo de las condiciones en el resumen (la mejor primero).</summary>
    private static int ConditionRank(string? code) => code switch
    {
        RentalReturnConditions.Good => 0,
        RentalReturnConditions.Damaged => 1,
        RentalReturnConditions.Incomplete => 2,
        _ => 3,
    };

    /// <summary>Códigos de condición distintos de una devolución, en orden fijo (GOOD, DAMAGED, INCOMPLETE, otros por código).</summary>
    public static IReadOnlyList<string> DistinctConditions(IEnumerable<string?> codes)
        => codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!).Distinct(StringComparer.Ordinal)
            .OrderBy(ConditionRank).ThenBy(c => c, StringComparer.Ordinal).ToList();
}
