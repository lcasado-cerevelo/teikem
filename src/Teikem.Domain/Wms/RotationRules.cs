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

    public static readonly IReadOnlyList<string> All = new[] { Fefo, Fifo };
}

public static class RotationRules
{
    public const string InvalidMethod = "El método de rotación debe ser FEFO o FIFO.";

    /// <summary>Normaliza a FEFO/FIFO (sin distinguir mayúsculas ni espacios); null si no es ninguno de los dos.</summary>
    public static string? Parse(string? value)
    {
        var v = value?.Trim().ToUpperInvariant();
        return v is RotationMethods.Fefo or RotationMethods.Fifo ? v : null;
    }

    /// <summary>Método efectivo: la excepción del producto, si no la de la compañía, si no FEFO (lo que había antes del lote).</summary>
    public static string Resolve(string? productMethod, string? tenantMethod)
        => Parse(productMethod) ?? Parse(tenantMethod) ?? RotationMethods.Fefo;

    public static bool IsFifo(string? method) => string.Equals(Parse(method), RotationMethods.Fifo, StringComparison.Ordinal);

    /// <summary>
    /// Clave de orden «más viejo primero» para FIFO: con fecha de entrada antes que sin ella (un saldo sin fecha conocida va al final,
    /// como el «sin vencimiento» de FEFO). Menor = sale antes.
    /// </summary>
    public static (int HasNoDate, DateTime Date) FifoKey(DateTime? receivedAtUtc)
        => receivedAtUtc.HasValue ? (0, receivedAtUtc.Value) : (1, DateTime.MaxValue);
}
