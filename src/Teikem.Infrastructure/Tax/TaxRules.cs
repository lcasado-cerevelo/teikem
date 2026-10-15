using Teikem.Domain.Tax;

namespace Teikem.Infrastructure.Tax;

/// <summary>Tratamiento de una línea, ya decidido por el servicio (categoría del producto, regla del cargo, exención del cliente).</summary>
public enum TaxTreatment { Taxable, ZeroRate, ExemptProduct, ExemptClient, NotTaxableCharge }

public sealed record TaxLineInput(string Key, decimal Amount, TaxTreatment Treatment);

/// <summary>Una tasa ya resuelta para la fecha: porcentaje total y sus componentes (cuya suma es el porcentaje).</summary>
public sealed record TaxRateInfo(int TaxRateId, string Code, string Name, decimal Percent, IReadOnlyList<TaxComponentInfo> Components);

public sealed record TaxComponentInfo(string Code, string Name, decimal Percent);

public sealed record TaxComponentAmount(string Code, string Name, decimal Percent, decimal Amount);

public sealed record TaxLineResult(string Key, decimal Amount, decimal Net, decimal TaxAmount, decimal Total, string Reason, bool IsTaxExempt,
    int? TaxRateId, decimal TaxRatePct, IReadOnlyList<TaxComponentAmount> Components);

public sealed record TaxDocumentResult(IReadOnlyList<TaxLineResult> Lines, decimal Subtotal, decimal TaxAmount, decimal Total,
    IReadOnlyList<TaxComponentAmount> Components);

/// <summary>
/// Lote 55 — cálculo puro del impuesto (sin base de datos, probado con xunit). Reglas:
/// - Precios SIN impuesto: neto = monto; impuesto = neto × % ; total = neto + impuesto.
/// - Precios CON impuesto incluido: bruto = monto; impuesto = bruto − bruto ÷ (1 + %) ; neto = bruto − impuesto.
/// - Redondeo LINE: el impuesto de cada línea se redondea a 2 decimales (mitad hacia afuera) y el documento suma las líneas.
///   Redondeo DOCUMENT: cada línea lleva el impuesto con 4 decimales y el documento redondea UNA vez la suma.
/// - Cada línea se reparte entre los componentes de la tasa (estatal, municipal…) y el último absorbe la diferencia de redondeo.
/// - Exento / cargo no gravable: impuesto 0. Tasa cero: gravable al 0 %.
/// </summary>
public static class TaxRules
{
    public const int LineDecimals = 2;
    public const int DocumentLineDecimals = 4;

    public static TaxDocumentResult Calculate(IReadOnlyList<TaxLineInput> lines, TaxRateInfo? rate, bool pricesIncludeTax, string roundingMode)
    {
        var byLine = roundingMode != TaxRoundingModes.Document;
        var lineDecimals = byLine ? LineDecimals : DocumentLineDecimals;
        var pct = rate?.Percent ?? 0m;
        var results = new List<TaxLineResult>();
        decimal rawSum = 0m;

        foreach (var l in lines)
        {
            var taxed = l.Treatment is TaxTreatment.Taxable or TaxTreatment.ZeroRate;
            var linePct = l.Treatment == TaxTreatment.Taxable ? pct : 0m;
            decimal raw = 0m;
            if (taxed && linePct > 0)
                raw = pricesIncludeTax ? l.Amount - l.Amount / (1m + linePct / 100m) : l.Amount * linePct / 100m;
            var tax = Math.Round(raw, lineDecimals, MidpointRounding.AwayFromZero);
            rawSum += raw;
            var net = pricesIncludeTax ? l.Amount - tax : l.Amount;
            var total = pricesIncludeTax ? l.Amount : l.Amount + tax;
            var comps = tax > 0 && rate is not null && l.Treatment == TaxTreatment.Taxable
                ? Allocate(tax, rate, lineDecimals)
                : Array.Empty<TaxComponentAmount>();
            results.Add(new TaxLineResult(l.Key, l.Amount, net, tax, total, ReasonOf(l.Treatment),
                l.Treatment is TaxTreatment.ExemptProduct or TaxTreatment.ExemptClient,
                l.Treatment == TaxTreatment.Taxable ? rate?.TaxRateId : null, linePct, comps));
        }

        var docTax = byLine ? results.Sum(r => r.TaxAmount) : Math.Round(rawSum, LineDecimals, MidpointRounding.AwayFromZero);
        var sumAmount = results.Sum(r => r.Amount);
        var subtotal = pricesIncludeTax ? sumAmount - docTax : results.Sum(r => r.Net);
        var grand = pricesIncludeTax ? sumAmount : subtotal + docTax;

        IReadOnlyList<TaxComponentAmount> docComponents;
        if (rate is null || docTax <= 0) docComponents = Array.Empty<TaxComponentAmount>();
        else if (byLine) docComponents = SumComponents(results, rate);
        else docComponents = Allocate(docTax, rate, LineDecimals);

        return new TaxDocumentResult(results, subtotal, docTax, grand, docComponents);
    }

    /// <summary>Reparte un impuesto entre los componentes de la tasa según su porcentaje; el último recibe el resto para que la suma sea exacta.</summary>
    public static IReadOnlyList<TaxComponentAmount> Allocate(decimal tax, TaxRateInfo rate, int decimals)
    {
        if (rate.Components.Count == 0 || rate.Percent <= 0) return Array.Empty<TaxComponentAmount>();
        var list = new List<TaxComponentAmount>();
        decimal used = 0m;
        for (var i = 0; i < rate.Components.Count; i++)
        {
            var c = rate.Components[i];
            var amount = i == rate.Components.Count - 1
                ? tax - used
                : Math.Round(tax * c.Percent / rate.Percent, decimals, MidpointRounding.AwayFromZero);
            used += amount;
            list.Add(new TaxComponentAmount(c.Code, c.Name, c.Percent, amount));
        }
        return list;
    }

    private static IReadOnlyList<TaxComponentAmount> SumComponents(IReadOnlyList<TaxLineResult> lines, TaxRateInfo rate)
        => rate.Components.Select(c => new TaxComponentAmount(c.Code, c.Name, c.Percent,
            lines.SelectMany(l => l.Components).Where(x => x.Code == c.Code).Sum(x => x.Amount))).ToList();

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
