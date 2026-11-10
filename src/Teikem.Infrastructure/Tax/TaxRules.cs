using Teikem.Domain.Tax;

namespace Teikem.Infrastructure.Tax;

/// <summary>Tratamiento de una línea, ya decidido por el servicio (categoría del producto, regla del cargo, exención del cliente).</summary>
public enum TaxTreatment { Taxable, ZeroRate, ExemptProduct, ExemptClient, NotTaxableCharge }

public sealed record TaxLineInput(string Key, decimal Amount, TaxTreatment Treatment);

/// <summary>Una tasa ya resuelta para la fecha: porcentaje total y sus componentes (cuya suma es el porcentaje).</summary>
public sealed record TaxRateInfo(int TaxRateId, string Code, string Name, decimal Percent, IReadOnlyList<TaxComponentInfo> Components);

public sealed record TaxComponentInfo(string Code, string Name, decimal Percent);

/// <summary>IsExempt (Lote 237): la exención del cliente cubre este componente; Amount = 0 y Percent = el porcentaje del componente en la tasa.</summary>
public sealed record TaxComponentAmount(string Code, string Name, decimal Percent, decimal Amount, bool IsExempt = false);

public sealed record TaxLineResult(string Key, decimal Amount, decimal Net, decimal TaxAmount, decimal Total, string Reason, bool IsTaxExempt,
    int? TaxRateId, decimal TaxRatePct, IReadOnlyList<TaxComponentAmount> Components);

public sealed record TaxDocumentResult(IReadOnlyList<TaxLineResult> Lines, decimal Subtotal, decimal TaxAmount, decimal Total,
    IReadOnlyList<TaxComponentAmount> Components);

/// <summary>Lote 237 — una exención del cliente tal como la ve el cálculo: alcance, componentes (si es parcial), certificado y vigencia.</summary>
public sealed record TaxExemptionInfo(string Scope, IReadOnlyList<string> ComponentCodes, string CertificateNumber, DateOnly ValidFrom, DateOnly? ValidTo);

/// <summary>
/// Lote 237 — lo que las exenciones del cliente deciden en una fecha. Full = una exención de TODO el impuesto está vigente (gana a las parciales);
/// ComponentCertificates = componente exento → certificado que lo exime (unión de las parciales vigentes; vacío si Full);
/// Expired = la exención vencida de la que hay que avisar (null si ninguna, o si lo que cubría sigue cubierto por una vigente).
/// </summary>
public sealed record TaxExemptionOutcome(TaxExemptionInfo? Full, IReadOnlyDictionary<string, string> ComponentCertificates, TaxExemptionInfo? Expired);

/// <summary>
/// Lote 55 — cálculo puro del impuesto (sin base de datos, probado con xunit). Reglas:
/// - Precios SIN impuesto: neto = monto; impuesto = neto × % ; total = neto + impuesto.
/// - Precios CON impuesto incluido: bruto = monto; impuesto = bruto − bruto ÷ (1 + %) ; neto = bruto − impuesto.
/// - Redondeo LINE: el impuesto de cada línea se redondea a 2 decimales (mitad hacia afuera) y el documento suma las líneas.
///   Redondeo DOCUMENT: cada línea lleva el impuesto con 4 decimales y el documento redondea UNA vez la suma.
/// - Cada línea se reparte entre los componentes de la tasa (estatal, municipal…) y el último absorbe la diferencia de redondeo.
/// - Exento / cargo no gravable: impuesto 0. Tasa cero: gravable al 0 %.
/// - Lote 237 — exención PARCIAL del cliente (exemptComponents): a la línea gravable se le aplica la tasa SIN los componentes exentos (11.5 % → 10.5 %);
///   la línea sale con motivo EXEMPT_CLIENT_PARTIAL, su copia (TaxRatePct) es el porcentaje realmente cobrado y el componente exento va en 0 (IsExempt).
///   Un código que la tasa no tiene, o un componente de 0 %, no cambia nada. Si los componentes exentos son TODA la tasa, la línea queda como EXEMPT_CLIENT.
/// </summary>
public static class TaxRules
{
    public const int LineDecimals = 2;
    public const int DocumentLineDecimals = 4;

    public static TaxDocumentResult Calculate(IReadOnlyList<TaxLineInput> lines, TaxRateInfo? rate, bool pricesIncludeTax, string roundingMode,
        IReadOnlyCollection<string>? exemptComponents = null)
    {
        var byLine = roundingMode != TaxRoundingModes.Document;
        var lineDecimals = byLine ? LineDecimals : DocumentLineDecimals;
        var pct = rate?.Percent ?? 0m;
        var exempt = ExemptCodesOf(rate, exemptComponents);
        var exemptPct = rate is null ? 0m : rate.Components.Where(c => exempt.Contains(c.Code)).Sum(c => c.Percent);
        var effectivePct = Math.Max(0m, pct - exemptPct);
        var partial = exemptPct > 0m && effectivePct > 0m;      // se cobra una parte de la tasa
        var allExempt = exemptPct > 0m && effectivePct <= 0m;   // los componentes exentos son toda la tasa: exención completa
        var results = new List<TaxLineResult>();
        decimal rawSum = 0m;

        foreach (var l in lines)
        {
            var treatment = allExempt && l.Treatment == TaxTreatment.Taxable ? TaxTreatment.ExemptClient : l.Treatment;
            var taxed = treatment is TaxTreatment.Taxable or TaxTreatment.ZeroRate;
            var linePct = treatment == TaxTreatment.Taxable ? effectivePct : 0m;
            decimal raw = 0m;
            if (taxed && linePct > 0)
                raw = pricesIncludeTax ? l.Amount - l.Amount / (1m + linePct / 100m) : l.Amount * linePct / 100m;
            var tax = Math.Round(raw, lineDecimals, MidpointRounding.AwayFromZero);
            rawSum += raw;
            var net = pricesIncludeTax ? l.Amount - tax : l.Amount;
            var total = pricesIncludeTax ? l.Amount : l.Amount + tax;
            // con exención parcial el desglose sale siempre (aunque el impuesto sea 0 por un monto ínfimo): ahí se ve el componente exento
            var comps = rate is not null && treatment == TaxTreatment.Taxable && (tax > 0 || partial)
                ? Allocate(tax, rate, lineDecimals, exempt)
                : Array.Empty<TaxComponentAmount>();
            var reason = treatment == TaxTreatment.Taxable && partial ? TaxReasons.ExemptClientPartial : ReasonOf(treatment);
            results.Add(new TaxLineResult(l.Key, l.Amount, net, tax, total, reason,
                treatment is TaxTreatment.ExemptProduct or TaxTreatment.ExemptClient,
                treatment == TaxTreatment.Taxable ? rate?.TaxRateId : null, linePct, comps));
        }

        var docTax = byLine ? results.Sum(r => r.TaxAmount) : Math.Round(rawSum, LineDecimals, MidpointRounding.AwayFromZero);
        var sumAmount = results.Sum(r => r.Amount);
        var subtotal = pricesIncludeTax ? sumAmount - docTax : results.Sum(r => r.Net);
        var grand = pricesIncludeTax ? sumAmount : subtotal + docTax;

        IReadOnlyList<TaxComponentAmount> docComponents;
        var showPartial = partial && results.Any(r => r.Reason == TaxReasons.ExemptClientPartial);
        if (rate is null || (docTax <= 0 && !showPartial)) docComponents = Array.Empty<TaxComponentAmount>();
        else if (byLine) docComponents = SumComponents(results, rate, exempt);
        else docComponents = Allocate(docTax, rate, LineDecimals, exempt);

        return new TaxDocumentResult(results, subtotal, docTax, grand, docComponents);
    }

    /// <summary>De los códigos exentos del cliente, los que la tasa SÍ tiene (sin distinguir mayúsculas). Los demás no afectan.</summary>
    public static IReadOnlySet<string> ExemptCodesOf(TaxRateInfo? rate, IReadOnlyCollection<string>? exemptComponents)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rate is null || exemptComponents is null) return set;
        foreach (var c in rate.Components)
            if (exemptComponents.Contains(c.Code, StringComparer.OrdinalIgnoreCase)) set.Add(c.Code);
        return set;
    }

    /// <summary>Los códigos de una exención parcial que la tasa NO tiene (para avisar: no cambian el impuesto).</summary>
    public static IReadOnlyList<string> UnknownComponents(TaxRateInfo rate, IEnumerable<string> exemptComponents)
        => exemptComponents.Where(code => !rate.Components.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Reparte un impuesto entre los componentes de la tasa según su porcentaje; el último recibe el resto para que la suma sea exacta.
    /// Lote 237 — los componentes exentos (exempt) van en 0 y el impuesto se reparte solo entre los que se cobran.
    /// </summary>
    public static IReadOnlyList<TaxComponentAmount> Allocate(decimal tax, TaxRateInfo rate, int decimals, IReadOnlySet<string>? exempt = null)
    {
        if (rate.Components.Count == 0 || rate.Percent <= 0) return Array.Empty<TaxComponentAmount>();
        bool IsExempt(TaxComponentInfo c) => exempt is not null && exempt.Contains(c.Code);
        var chargedPct = rate.Components.Where(c => !IsExempt(c)).Sum(c => c.Percent);
        var lastCharged = -1;
        for (var i = 0; i < rate.Components.Count; i++)
            if (!IsExempt(rate.Components[i])) lastCharged = i;
        var list = new List<TaxComponentAmount>();
        decimal used = 0m;
        for (var i = 0; i < rate.Components.Count; i++)
        {
            var c = rate.Components[i];
            if (IsExempt(c))
            {
                list.Add(new TaxComponentAmount(c.Code, c.Name, c.Percent, 0m, true));
                continue;
            }
            var amount = i == lastCharged
                ? tax - used
                : chargedPct <= 0 ? 0m : Math.Round(tax * c.Percent / chargedPct, decimals, MidpointRounding.AwayFromZero);
            used += amount;
            list.Add(new TaxComponentAmount(c.Code, c.Name, c.Percent, amount));
        }
        return list;
    }

    private static IReadOnlyList<TaxComponentAmount> SumComponents(IReadOnlyList<TaxLineResult> lines, TaxRateInfo rate, IReadOnlySet<string> exempt)
        => rate.Components.Select(c => new TaxComponentAmount(c.Code, c.Name, c.Percent,
            lines.SelectMany(l => l.Components).Where(x => x.Code == c.Code).Sum(x => x.Amount), exempt.Contains(c.Code))).ToList();

    /// <summary>
    /// Lote 237 — combina las exenciones ACTIVAS del cliente en una fecha: una de TODO el impuesto vigente gana; si no, las parciales vigentes se unen
    /// (cada componente con el certificado de la exención más reciente que lo cubre). De las vencidas se avisa de la más reciente, salvo que lo que
    /// cubría siga cubierto por una vigente (p. ej. el certificado municipal renovado).
    /// </summary>
    public static TaxExemptionOutcome ResolveExemptions(IEnumerable<TaxExemptionInfo> activeExemptions, DateOnly date)
    {
        var all = activeExemptions.OrderByDescending(e => e.ValidFrom).ToList();
        var valid = all.Where(e => InEffect(e.ValidFrom, e.ValidTo, date)).ToList();
        var full = valid.FirstOrDefault(e => e.Scope != TaxExemptionScopes.Components);
        var components = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (full is null)
            foreach (var e in valid)
                foreach (var code in e.ComponentCodes)
                    components.TryAdd(code, e.CertificateNumber);
        TaxExemptionInfo? expired = null;
        if (full is null)
        {
            var lastExpired = all.Where(e => e.ValidTo is DateOnly vt && vt < date).OrderByDescending(e => e.ValidTo).FirstOrDefault();
            if (lastExpired is not null)
            {
                var stillCovered = lastExpired.Scope == TaxExemptionScopes.Components && lastExpired.ComponentCodes.Count > 0
                    && lastExpired.ComponentCodes.All(components.ContainsKey);
                if (!stillCovered) expired = lastExpired;
            }
        }
        return new TaxExemptionOutcome(full, components, expired);
    }

    /// <summary>«CITY,STATE» → ["CITY","STATE"] (como se guarda en ClientTaxExemption.ComponentCodes).</summary>
    public static IReadOnlyList<string> SplitComponentCodes(string? stored)
        => string.IsNullOrWhiteSpace(stored) ? Array.Empty<string>() : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Lote 237 — valida y normaliza el alcance de una exención. Devuelve el mensaje de error (o null) y, en scope / codes, lo que se guarda:
    /// ALL sin componentes, o COMPONENTS con los códigos en mayúsculas, sin repetir y ordenados.
    /// </summary>
    public static string? ValidateExemptionScope(string? scope, IReadOnlyList<string>? componentCodes, out string normalizedScope, out IReadOnlyList<string> codes)
    {
        normalizedScope = string.IsNullOrWhiteSpace(scope) ? TaxExemptionScopes.All : scope.Trim().ToUpperInvariant();
        codes = Array.Empty<string>();
        if (!TaxExemptionScopes.Values.Contains(normalizedScope)) return ExemptionScopeInvalid;
        if (normalizedScope == TaxExemptionScopes.All) return null;
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in componentCodes ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (ValidateCode(raw, out var code) is not null) return ExemptionComponentInvalid;
            set.Add(code);
        }
        if (set.Count == 0) return ExemptionComponentsRequired;
        if (set.Count > TaxExemptionScopes.MaxComponents) return ExemptionTooManyComponents;
        codes = set.ToList();
        return null;
    }

    public static string ReasonOf(TaxTreatment t) => t switch
    {
        TaxTreatment.Taxable => TaxReasons.Taxable,
        TaxTreatment.ZeroRate => TaxReasons.ZeroRate,
        TaxTreatment.ExemptProduct => TaxReasons.ExemptProduct,
        TaxTreatment.ExemptClient => TaxReasons.ExemptClient,
        _ => TaxReasons.NotTaxableCharge,
    };

    // ---------------------------------------------------------------- validaciones de datos (mensajes exactos del manual)

    public const string CodeRequired = "Escriba el código de la tasa.";
    public const string CodeTooLong = "El código de la tasa no puede pasar de 30 caracteres.";
    public const string CodeFormat = "El código solo puede llevar letras, números, guion y guion bajo.";
    public const string NameRequired = "Escriba el nombre de la tasa.";
    public const string NameTooLong = "El nombre no puede pasar de 100 caracteres.";
    public const string ComponentsRequired = "La tasa necesita al menos un componente (por ejemplo, Estatal).";
    public const string ComponentDuplicate = "Hay dos componentes con el mismo código.";
    public const string ComponentPercentRange = "El porcentaje de cada componente debe estar entre 0 y 100.";
    public const string RatePercentRange = "La suma de los componentes no puede pasar de 100 %.";
    public const string DatesOrder = "La fecha final no puede ser anterior a la inicial.";
    public const string RateOverlap = "Ya hay una tasa con ese código que cubre esas fechas. Cierre la vigencia de la anterior primero.";
    public const string RateNotFound = "Tasa no encontrada.";
    public const string RateAlreadyClosed = "La tasa ya tiene fecha final.";
    public const string CloseBeforeStart = "La fecha de cierre no puede ser anterior al inicio de la vigencia.";
    public const string RoundingInvalid = "El redondeo debe ser LINE (por línea) o DOCUMENT (por documento).";
    public const string DefaultRateUnknown = "No hay ninguna tasa con ese código.";
    public const string NoRateInEffect = "No hay una tasa de impuesto vigente en esa fecha. Configure una en Impuestos → Tasas.";
    public const string NoDefaultRate = "Escoja la tasa por omisión en Impuestos → Reglas.";
    public const string ExemptionTypeRequired = "Escriba el tipo de exención.";
    public const string CertificateRequired = "Escriba el número del certificado.";
    public const string ExemptionTypeTooLong = "El tipo de exención no puede pasar de 60 caracteres.";
    public const string CertificateTooLong = "El número del certificado no puede pasar de 60 caracteres.";
    public const string NotesTooLong = "Las notas no pueden pasar de 300 caracteres.";
    public const string ExemptionNotFound = "Exención no encontrada.";
    // Lote 237 — alcance de la exención
    public const string ExemptionScopeInvalid = "El alcance de la exención debe ser ALL (todo el impuesto) o COMPONENTS (solo algunos componentes).";
    public const string ExemptionComponentsRequired = "Marque al menos un componente del impuesto que la exención cubre.";
    public const string ExemptionComponentInvalid = "El código de un componente no es válido (letras, números, guion y guion bajo; hasta 30 caracteres).";
    public const string ExemptionTooManyComponents = "Una exención puede nombrar hasta 10 componentes.";
    /// <summary>{0} = código del componente.</summary>
    public const string ExemptionComponentUnknownFormat = "Ninguna tasa de la compañía tiene el componente «{0}».";
    /// <summary>Aviso del cálculo. {0} = certificado, {1} = código del componente.</summary>
    public const string ExemptionComponentNotInRateFormat = "La exención del cliente (certificado {0}) nombra el componente {1}, que la tasa vigente no tiene: no cambió el impuesto.";
    public const string ClientNotFound = "Cliente no encontrado.";
    public const string ProductNotFound = "Producto no encontrado.";
    public const string CategoryInvalid = "La categoría de impuesto debe ser TAXABLE, EXEMPT o ZERO_RATE.";
    public const string ChargeTypeInvalid = "Tipo de cargo no válido.";
    public const string LinesRequired = "Agregue al menos una línea para calcular.";
    public const string AmountInvalid = "El monto de la línea no es válido.";
    public const string TooManyLines = "Se pueden calcular hasta 500 líneas a la vez.";

    public static string? ValidateCode(string? code, out string normalized)
    {
        normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0) return CodeRequired;
        if (normalized.Length > 30) return CodeTooLong;
        foreach (var ch in normalized)
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')) return CodeFormat;
        return null;
    }

    /// <summary>Vigente en una fecha: desde (incluida) hasta (incluida); sin fecha final = abierta.</summary>
    public static bool InEffect(DateOnly from, DateOnly? to, DateOnly on) => on >= from && (to is null || on <= to);

    /// <summary>Dos vigencias del mismo código se traslapan si comparten algún día.</summary>
    public static bool Overlaps(DateOnly aFrom, DateOnly? aTo, DateOnly bFrom, DateOnly? bTo)
        => (bTo is null || aFrom <= bTo) && (aTo is null || bFrom <= aTo);
}
