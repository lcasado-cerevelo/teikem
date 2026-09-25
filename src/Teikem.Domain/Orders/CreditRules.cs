using System.Globalization;

namespace Teikem.Domain.Orders;

/// <summary>
/// Datos del chequeo de crédito de una orden (R18): límite del cliente (NULL = sin límite), saldo pendiente ("en curso",
/// lo aporta IClientBalanceProvider) y monto de la orden que se está confirmando.
/// </summary>
public sealed record CreditCheck(decimal? Limit, decimal Exposure, decimal NewAmount);

/// <summary>
/// Reglas puras del límite de crédito (Lote 3, P4). Sin EF: recibe los tres montos y decide.
/// - Sin límite (Limit NULL) siempre pasa y no hay "disponible".
/// - Una exposición negativa (créditos a favor, datos sucios) se trata como 0.
/// - Pasa si exposición + monto nuevo &lt;= límite (la igualdad se permite: el cliente puede consumir el límite justo).
/// - Available = límite − exposición (puede ser negativo si ya está sobregirado).
/// El exceso NO bloquea de forma definitiva: avisa (422 credit_exceeded) y se autoriza con overrideCredit + permiso
/// orders.credit_override (ajuste C de Luis, 2026-09-25).
/// </summary>
public static class CreditRules
{
    public static (bool Ok, decimal? Available) Evaluate(CreditCheck check)
    {
        if (check.Limit is null) return (true, null);
        var limit = check.Limit.Value;
        var exposure = Exposure(check);
        return (exposure + check.NewAmount <= limit, limit - exposure);
    }

    /// <summary>Mensaje exacto del aviso (montos con dos decimales y punto decimal, independientes de la cultura del servidor).</summary>
    public static string Message(CreditCheck check)
        => $"El cliente excede su límite de crédito: límite {Format(check.Limit ?? 0m)}, en curso {Format(Exposure(check))}, esta orden {Format(check.NewAmount)}.";

    /// <summary>Exposición efectiva: nunca negativa.</summary>
    public static decimal Exposure(CreditCheck check) => check.Exposure < 0m ? 0m : check.Exposure;

    /// <summary>Formato monetario del aviso y de la bitácora: "0.00" invariante.</summary>
    public static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
