using Teikem.Domain.Tax;

namespace Teikem.Infrastructure.Tax;

/// <summary>
/// Lote 248 — un período de exención por temporada tal como lo ve el cálculo: nombre, vigencia (bordes incluidos), alcance y los destinos ACTIVOS
/// (productos y categorías; una categoría incluye sus subcategorías).
/// </summary>
public sealed record TaxPeriodInfo(int Id, string Name, DateOnly ValidFrom, DateOnly ValidTo, string Scope, IReadOnlySet<int> ProductIds, IReadOnlySet<int> CategoryIds);

/// <summary>Lote 248 — estado derivado de un período (no hay StatusCode): programado, vigente, terminado o dado de baja.</summary>
public static class TaxExemptionPeriodStates
{
    public const string Scheduled = "SCHEDULED";
    public const string Active = "ACTIVE";
    public const string Ended = "ENDED";
    public const string Inactive = "INACTIVE";
}

/// <summary>Lote 248 — reglas puras y mensajes exactos de la exención de producto por temporada (TaxExemptionPeriod).</summary>
public static class TaxExemptionPeriodRules
{
    public const int MaxNameLength = 100;
    public const int MaxReasonLength = 300;
    public const int MaxProducts = 2000;
    public const int MaxCategories = 200;

    public const string NameRequired = "Escriba el nombre del período de exención.";
    public const string NameTooLong = "El nombre del período no puede pasar de 100 caracteres.";
    public const string ReasonRequired = "Escriba el motivo de la exención (por ejemplo, la orden administrativa que la decreta).";
    public const string ReasonTooLong = "El motivo no puede pasar de 300 caracteres.";
    public const string ValidFromRequired = "Escriba desde qué fecha rige la exención.";
    public const string ValidToRequired = "Escriba hasta qué fecha rige la exención.";
    public const string ScopeInvalid = "El alcance debe ser ALL (todos los productos) o SELECTED (categorías o productos escogidos).";
    public const string SelectionRequired = "Escoja al menos una categoría o un producto.";
    public const string TooManyTargets = "Un período puede escoger hasta 2000 productos y 200 categorías.";
    /// <summary>409.</summary>
    public const string StartedOnlyEnd = "El período ya empezó: solo se le puede cambiar la fecha final o darlo de baja.";
    public const string EndBeforeToday = "La fecha final no puede quedar antes de hoy.";
    /// <summary>404.</summary>
    public const string NotFound = "Período de exención no encontrado.";
    /// <summary>409 — dos personas guardaron a la vez.</summary>
    public const string Concurrent = "El período cambió mientras lo editaba. Vuelva a abrirlo.";

    /// <summary>Vigente en una fecha: desde y hasta incluidos.</summary>
    public static bool InEffect(DateOnly from, DateOnly to, DateOnly on) => on >= from && on <= to;

    public static string State(bool isActive, DateOnly from, DateOnly to, DateOnly today)
        => !isActive ? TaxExemptionPeriodStates.Inactive
            : today < from ? TaxExemptionPeriodStates.Scheduled
            : today > to ? TaxExemptionPeriodStates.Ended
            : TaxExemptionPeriodStates.Active;

    /// <summary>Alcance normalizado (vacío = ALL) o null si no es válido.</summary>
    public static string? NormalizeScope(string? scope)
    {
        var s = string.IsNullOrWhiteSpace(scope) ? TaxExemptionPeriodScopes.All : scope.Trim().ToUpperInvariant();
        return TaxExemptionPeriodScopes.Values.Contains(s) ? s : null;
    }

    /// <summary>La categoría y todas sus antecesoras (padre, abuelo…), a prueba de ciclos. <paramref name="parentOf"/>: categoría → padre (NULL = raíz).</summary>
    public static IReadOnlySet<int> CategoryWithAncestors(int? categoryId, IReadOnlyDictionary<int, int?> parentOf)
    {
        var set = new HashSet<int>();
        var current = categoryId;
        while (current is int c && set.Add(c))
            current = parentOf.TryGetValue(c, out var p) ? p : null;
        return set;
    }

    /// <summary>
    /// Los períodos (ya filtrados como vigentes y activos) que cubren un producto: ALL; o SELECTED que lo escoge, o que escoge su categoría o una de sus antecesoras
    /// (una categoría incluye sus subcategorías, no sus hermanas).
    /// </summary>
    public static IReadOnlyList<TaxPeriodInfo> Covering(IEnumerable<TaxPeriodInfo> periods, int productId, int? categoryId, IReadOnlyDictionary<int, int?> parentOf)
    {
        IReadOnlySet<int>? chain = null;
        var list = new List<TaxPeriodInfo>();
        foreach (var p in periods)
        {
            if (p.Scope == TaxExemptionPeriodScopes.All || p.ProductIds.Contains(productId)) { list.Add(p); continue; }
            if (p.CategoryIds.Count == 0 || categoryId is null) continue;
            chain ??= CategoryWithAncestors(categoryId, parentOf);
            if (chain.Any(p.CategoryIds.Contains)) list.Add(p);
        }
        return list;
    }

    public static bool Covers(IEnumerable<TaxPeriodInfo> periods, int productId, int? categoryId, IReadOnlyDictionary<int, int?> parentOf)
        => Covering(periods, productId, categoryId, parentOf).Count > 0;

    /// <summary>El nombre que se muestra cuando varios períodos se traslapan: el que empieza primero (y, si empatan, el de id menor).</summary>
    public static string? FirstName(IEnumerable<TaxPeriodInfo> periods)
        => periods.OrderBy(p => p.ValidFrom).ThenBy(p => p.Id).FirstOrDefault()?.Name;
}
