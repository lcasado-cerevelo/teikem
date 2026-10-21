namespace Teikem.Domain.Billing;

/// <summary>
/// Lote 115 — cargo por devolución (dictado por Luis): opcional, por contrato del cliente, y de tres maneras: ORDER (un monto por orden devuelta, a todos los tipos de paquete), PACKAGE_TYPE (un monto por cada tipo de paquete que se devuelve)
/// o PIECE (un monto por cada pieza devuelta). Sin modo no hay cargo. Se le cobra a la orden devuelta al quedar «Devuelta» y se factura como su propio cargo (recargo), aparte del flete.
/// </summary>
public static class ReturnFeeModes
{
    public const string Order = "ORDER";
    public const string PackageType = "PACKAGE_TYPE";
    public const string Piece = "PIECE";
    public static readonly string[] All = { Order, PackageType, Piece };

    public static string? Normalize(string? raw)
    {
        var m = raw?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(m) ? null : All.Contains(m) ? m : "?";
    }
}

public static class ReturnFeeRules
{
    public const string InvalidModeMessage = "Indique el cargo por devolución: por envío, por tipo de paquete o por pieza.";
    public const string NegativeMessage = "El cargo por devolución no puede ser negativo.";
    public const string AmountRequiredMessage = "Indique el monto del cargo por devolución.";
    public const string ByTypeRequiredMessage = "Indique el monto de al menos un tipo de paquete.";
    public const string DuplicateTypeMessage = "El tipo de paquete {0} está repetido.";
    public const string DescriptionFormat = "{0} · Cargo por devolución";

    /// <summary>
    /// Lo que cuestan <paramref name="pieceTypes"/> (el tipo de paquete de cada pieza que se devuelve) con ese modo. ORDER cobra el monto una sola vez (<paramref name="alreadyCharged"/> = la orden ya lo tiene);
    /// PIECE cobra el monto por cada pieza; PACKAGE_TYPE suma el monto de cada tipo (un tipo sin monto no cobra).
    /// </summary>
    public static decimal Compute(string? mode, decimal? amount, IReadOnlyDictionary<int, decimal> byType, IReadOnlyList<int?> pieceTypes, bool alreadyCharged)
    {
        if (pieceTypes.Count == 0) return 0m;
        return mode switch
        {
            ReturnFeeModes.Order => alreadyCharged ? 0m : amount ?? 0m,
            ReturnFeeModes.Piece => (amount ?? 0m) * pieceTypes.Count,
            ReturnFeeModes.PackageType => pieceTypes.Sum(t => t is int id && byType.TryGetValue(id, out var a) ? a : 0m),
            _ => 0m,
        };
    }
}
