using Teikem.Domain.Constants;

namespace Teikem.Domain.Orders;

/// <summary>
/// Lote 113 — «Devolver tras N intentos» (dictado por Luis): por compañía se define con cuántos intentos fallidos se sugiere devolver la orden (por omisión 2; 0 = sin sugerencia). Es solo una SUGERENCIA y una marca:
/// nunca bloquea (la orden se puede seguir reintentando). No se sugiere en una orden Sometida (todavía no se recoge) ni en una que ya terminó.
/// </summary>
public static class ReturnSuggestionRules
{
    public const int DefaultAfterAttempts = 2;
    public const int MaxAfterAttempts = 20;
    public const string InvalidMessage = "Indique con cuántos intentos fallidos se sugiere devolver: de 0 (sin sugerencia) a 20.";

    private static readonly string[] NeverSuggest =
    {
        OrderStatuses.Submitted, OrderStatuses.Delivered, OrderStatuses.Cancelled, OrderStatuses.InWarehouse, OrderStatuses.Returned,
    };

    public static bool IsValid(int afterAttempts) => afterAttempts is >= 0 and <= MaxAfterAttempts;

    public static bool Suggest(int afterAttempts, int failedAttempts, string? statusCode)
        => afterAttempts > 0 && failedAttempts >= afterAttempts && !NeverSuggest.Contains(statusCode ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public static string Message(int failedAttempts) => $"Lleva {failedAttempts} intento(s) fallido(s): se sugiere devolverla.";
}
