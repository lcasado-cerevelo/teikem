using Teikem.Domain.Common;

namespace Teikem.Domain.Analytics;

/// <summary>Lote 90 (P12) — fotos diarias de indicadores (KpiDailySnapshot): una cifra por indicador y por día de la compañía, tomada por el trabajo nocturno.</summary>
public static class KpiCodes
{
    /// <summary>Fuente de datos para indicadores y gráficos (historial por día) y su EntityType.</summary>
    public const string DataSource = "KPI_DAILY";
    /// <summary>Dominio de catálogo con la clave de cada indicador (ya existe en el seed original; el Lote 90 suma las suyas).</summary>
    public const string KeyDomain = "KpiKey";
}

/// <summary>
/// Una cifra de una foto diaria: ORDERS_* cuentan órdenes («foto de ese momento»; las de flujo —creadas y entregadas— son las del día), COD_* son montos en la moneda de la compañía. Cada clave dice su tipo
/// (`count` o `money`) y su etiqueta; el trabajo `KPI_SNAPSHOT` guarda todas las que su módulo permite.
/// </summary>
public sealed record KpiDefinition(string Key, string Kind, string LabelEs, string LabelEn, string Module);

public static class KpiKeys
{
    public const string OrdersCreated = "ORDERS_CREATED";
    public const string OrdersInWarehouse = "ORDERS_IN_WAREHOUSE";
    public const string OrdersUnassigned = "ORDERS_UNASSIGNED";
    public const string OrdersOnRoute = "ORDERS_ON_ROUTE";
    public const string OrdersDelivered = "ORDERS_DELIVERED";
    public const string OrdersOpen = "ORDERS_OPEN";
    public const string CodToCollect = "COD_TO_COLLECT";
    public const string CodCollected = "COD_COLLECTED";
    public const string CodReconciled = "COD_RECONCILED";
    public const string CodRemittanceReady = "COD_REMITTANCE_READY";

    public const string KindCount = "count";
    public const string KindMoney = "money";

    /// <summary>Módulos de la compañía de los que depende cada grupo de indicadores (ModuleKeys).</summary>
    public const string ModuleOrders = "LTL_GROUND";
    public const string ModuleCod = "COD";

    public static readonly IReadOnlyList<KpiDefinition> All = new[]
    {
        new KpiDefinition(OrdersCreated, KindCount, "Órdenes nuevas del día", "New orders of the day", ModuleOrders),
        new KpiDefinition(OrdersInWarehouse, KindCount, "Órdenes en almacén", "Orders in warehouse", ModuleOrders),
        new KpiDefinition(OrdersUnassigned, KindCount, "Órdenes sin chofer", "Orders without a driver", ModuleOrders),
        new KpiDefinition(OrdersOnRoute, KindCount, "Órdenes en ruta", "Orders on route", ModuleOrders),
        new KpiDefinition(OrdersDelivered, KindCount, "Órdenes entregadas del día", "Orders delivered in the day", ModuleOrders),
        new KpiDefinition(OrdersOpen, KindCount, "Órdenes abiertas", "Open orders", ModuleOrders),
        new KpiDefinition(CodToCollect, KindMoney, "COD por cobrar", "COD to collect", ModuleCod),
        new KpiDefinition(CodCollected, KindMoney, "COD cobrado sin cuadrar", "COD collected, not reconciled", ModuleCod),
        new KpiDefinition(CodReconciled, KindMoney, "COD cuadrado sin remesa", "COD reconciled, no remittance", ModuleCod),
        new KpiDefinition(CodRemittanceReady, KindMoney, "Remesas listas (neto)", "Remittances ready (net)", ModuleCod),
    };

    public static KpiDefinition? Find(string? key) => All.FirstOrDefault(k => string.Equals(k.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Una cifra de un día. Sin dimensión (DimEntityLookupId y DimId nulos) en este lote; el esquema ya las trae para desgloses futuros (por cliente, por chofer).</summary>
public class KpiSnapshot : ITenantScoped
{
    public long KpiDailySnapshotId { get; set; }
    public int TenantId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int KpiKeyLookupId { get; set; }
    public int? DimEntityLookupId { get; set; }
    public int? DimId { get; set; }
    public decimal? NumericValue { get; set; }
}

public static class KpiRules
{
    public const int MaxDays = 400;
    public const int DefaultDays = 7;
    public const string DaysInvalid = "Los días deben estar entre 1 y 400.";

    public static int ClampDays(int days) => Math.Clamp(days <= 0 ? DefaultDays : days, 1, MaxDays);

    /// <summary>Los días del rango [hoy − (días − 1), hoy], del más viejo al más nuevo.</summary>
    public static IReadOnlyList<DateOnly> Window(DateOnly today, int days)
    {
        var n = ClampDays(days);
        return Enumerable.Range(0, n).Select(i => today.AddDays(-(n - 1 - i))).ToList();
    }

    /// <summary>La serie de un indicador para una ventana de días: el valor de cada día o 0 donde no hay foto (el día sin foto no es «cero» en la realidad, pero las barritas lo dibujan así).</summary>
    public static IReadOnlyList<decimal> Series(IEnumerable<DateOnly> window, IReadOnlyDictionary<DateOnly, decimal> byDay)
        => window.Select(d => byDay.TryGetValue(d, out var v) ? v : 0m).ToList();
}
