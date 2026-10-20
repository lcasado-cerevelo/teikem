using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Método de rotación del inventario (2026-10-11, Lote 34). FEFO (vence primero, sale primero) es el de siempre y el de todas las
/// compañías existentes; FIFO (entra primero, sale primero) usa la fecha de PRIMERA ENTRADA del saldo (StockBalance.FirstReceivedAtUtc).
/// Se configura por compañía (Tenant.RotationMethod) y un producto puede tener su excepción (Product.RotationMethod, NULL = el de la compañía).
/// </summary>
public static class RotationMethods
{
    public const string Fefo = "FEFO";
    public const string Fifo = "FIFO";
    /// <summary>2026-10-20: por posición — sin mirar fechas, sale primero lo que está en la posición que toca (tipo de zona y luego código de la posición).</summary>
    public const string Position = "POSITION";

    public static readonly IReadOnlyList<string> All = new[] { Fefo, Fifo, Position };
    /// <summary>Lo que puede escoger un producto CON lote (el vencimiento solo existe ahí).</summary>
    public static readonly IReadOnlyList<string> ForLot = new[] { Fefo, Fifo };
    /// <summary>Lo que puede escoger un producto SIN lote o por serie (no vence).</summary>
    public static readonly IReadOnlyList<string> ForNoLot = new[] { Fifo, Position };
}

/// <summary>
/// Los dos métodos de rotación de la compañía (2026-10-20): <see cref="Lot"/> para los productos por lote (FEFO por omisión, o FIFO) y <see cref="NoLot"/> para los
/// productos sin lote y por serie, que no vencen (FIFO por omisión, o por posición). Un valor vacío o que no aplica a su tipo cae en el de fábrica.
/// </summary>
public sealed record CompanyRotation(string? Lot, string? NoLot);

/// <summary>
/// Cómo se aplica el método de rotación en las SALIDAS (recolección y despacho manual) — 2026-10-20: SUGGEST (por omisión) = el sistema propone el lote y la posición
/// que corresponden y quien despacha puede cambiarlos (las pantallas advierten); ENFORCE = el sistema escoge solo y reparte la cantidad, y el servidor rechaza una
/// posición o un lote explícitos que no sean los que la rotación asigna. Se configura por compañía (Tenant.RotationEnforcement).
/// </summary>
public static class RotationEnforcements
{
    public const string Suggest = "SUGGEST";
    public const string Enforce = "ENFORCE";

    public static readonly IReadOnlyList<string> All = new[] { Suggest, Enforce };
}

public static class RotationRules
{
    public const string InvalidMethod = "El método de rotación debe ser FEFO o FIFO.";
    public const string InvalidNoLotMethod = "El método de rotación de los productos sin lote debe ser FIFO o POSITION (por posición).";
    public const string InvalidProductMethodForLot = "Un producto con lote solo admite rotación FEFO o FIFO.";
    public const string InvalidProductMethodForNoLot = "Un producto sin lote ni series solo admite rotación FIFO o POSITION (por posición).";

    /// <summary>¿El método aplica a ese tipo de producto? Con lote: FEFO o FIFO; sin lote ni series: FIFO o por posición.</summary>
    public static bool AppliesTo(string? method, bool lotTracked)
        => Parse(method) is string m && (lotTracked ? RotationMethods.ForLot : RotationMethods.ForNoLot).Contains(m);

    /// <summary>
    /// Método efectivo de un producto (2026-10-20): su excepción, si aplica a su tipo; si no, el de la compañía para ese tipo (con lote → <see cref="CompanyRotation.Lot"/>; sin lote ni
    /// series → <see cref="CompanyRotation.NoLot"/>); si tampoco aplica, el de fábrica (FEFO con lote, FIFO sin lote).
    /// </summary>
    public static string Resolve(string? productMethod, CompanyRotation company, bool lotTracked)
    {
        if (AppliesTo(productMethod, lotTracked)) return Parse(productMethod)!;
        var companyMethod = lotTracked ? company.Lot : company.NoLot;
        if (AppliesTo(companyMethod, lotTracked)) return Parse(companyMethod)!;
        return lotTracked ? RotationMethods.Fefo : RotationMethods.Fifo;
    }
    public const string InvalidEnforcement = "La aplicación de la rotación debe ser SUGGEST (sugerir) o ENFORCE (obligar).";

    /// <summary>Normaliza a SUGGEST/ENFORCE (sin distinguir mayúsculas ni espacios); null si no es ninguno de los dos.</summary>
    public static string? ParseEnforcement(string? value)
    {
        var v = value?.Trim().ToUpperInvariant();
        return v is RotationEnforcements.Suggest or RotationEnforcements.Enforce ? v : null;
    }

    /// <summary>¿La rotación se obliga? Todo lo que no sea ENFORCE (incluido null) es sugerir, lo de siempre.</summary>
    public static bool IsEnforced(string? enforcement)
        => string.Equals(ParseEnforcement(enforcement), RotationEnforcements.Enforce, StringComparison.Ordinal);

    /// <summary>Mensaje 409 cuando, con la rotación obligatoria, la línea pide una posición o un lote distintos de los que la rotación asigna.</summary>
    public static string EnforcedMismatch(string sku, string expectedBins)
        => $"La rotación es obligatoria: {sku} debe salir de {expectedBins}. No escoja posición ni lote; el sistema los asigna.";

    /// <summary>Normaliza a FEFO/FIFO (sin distinguir mayúsculas ni espacios); null si no es ninguno de los dos.</summary>
    public static string? Parse(string? value)
    {
        var v = value?.Trim().ToUpperInvariant();
        return v is RotationMethods.Fefo or RotationMethods.Fifo or RotationMethods.Position ? v : null;
    }

    /// <summary>Método efectivo: la excepción del producto, si no la de la compañía, si no FEFO (lo que había antes del lote).</summary>
    public static string Resolve(string? productMethod, string? tenantMethod)
        => Parse(productMethod) ?? Parse(tenantMethod) ?? RotationMethods.Fefo;

    public static bool IsFifo(string? method) => string.Equals(Parse(method), RotationMethods.Fifo, StringComparison.Ordinal);

    public static bool IsPosition(string? method) => string.Equals(Parse(method), RotationMethods.Position, StringComparison.Ordinal);

    /// <summary>
    /// Clave de orden «más viejo primero» para FIFO: con fecha de entrada antes que sin ella (un saldo sin fecha conocida va al final,
    /// como el «sin vencimiento» de FEFO). Menor = sale antes.
    /// </summary>
    public static (int HasNoDate, DateTime Date) FifoKey(DateTime? receivedAtUtc)
        => receivedAtUtc.HasValue ? (0, receivedAtUtc.Value) : (1, DateTime.MaxValue);
}
