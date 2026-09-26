namespace Teikem.Domain.Common;

/// <summary>
/// Códigos del catálogo Country: ISO 3166-1 alfa-2 (dos letras mayúsculas). Es el invariante que supone la columna
/// CHAR(2) de dbo.OrderStop.SnapCountryCode (snapshot de la parada); un código más largo haría fallar el alta de la orden.
/// </summary>
public static class CountryCode
{
    public const string InvalidMessage = "El código de país debe ser ISO 3166-1 alfa-2 (2 letras).";

    /// <summary>true si el código (ya normalizado a mayúsculas) tiene exactamente dos letras A–Z.</summary>
    public static bool IsValid(string? code) => code is { Length: 2 } && code.All(char.IsAsciiLetterUpper);
}
