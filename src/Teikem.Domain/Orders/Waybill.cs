using System.Globalization;

namespace Teikem.Domain.Orders;

/// <summary>
/// Número de conduce (waybill number): el identificador ÚNICO de cada orden de entrega en toda la compañía, que usan el cliente (sender) y el consignatario para referirse a ella. Lo genera Teikem al crear la orden; no se teclea
/// ni cambia. El número de orden (puede repetirse entre clientes) y el de empaque (EMP-#####, identificador de escaneo) siguen existiendo.
/// Lote 110 (dictado por Luis, 2026-10-18): el conduce se arma como el número de la pieza pero con un secuencial del día en vez de la hora —
/// <c>{Code del cliente}{Code del consignatario o su ID}{aammdd}{secuencial del día de ese consignatario}</c>, sin separadores nuestros (los Codes tal cual vengan; máximo 10 caracteres cada uno). Antes (Lote 97): contador de la
/// compañía con el patrón CND-#######; las órdenes que ya lo tenían lo conservan.
/// </summary>
public static class WaybillCodes
{
    public const string NumberKind = "WAYBILL";
    /// <summary>Patrón de antes del Lote 110 (las órdenes viejas lo conservan).</summary>
    public const string NumberPattern = "CND-#######";
    public const string UniqueIndex = "UX_Order_Waybill";

    /// <summary>Lo que se repite en todos los conduces de un consignatario en un día: {Code cliente}{Code consignatario o su ID}{aammdd} (la fecha es la de la compañía).</summary>
    public static string DayPrefix(string clientPart, string consigneePart, DateTime localNow)
        => string.Concat(clientPart, consigneePart, localNow.ToString("yyMMdd", CultureInfo.InvariantCulture));

    /// <summary>El siguiente conduce del día: el prefijo más (el mayor secuencial ya usado con ese prefijo + 1; el primero es 1). <paramref name="existing"/> son los conduces de la compañía que empiezan con el prefijo.</summary>
    public static string Next(string dayPrefix, IEnumerable<string?> existing)
    {
        long max = 0;
        foreach (var w in existing)
        {
            if (w is null || !w.StartsWith(dayPrefix, StringComparison.Ordinal)) continue;
            var tail = w[dayPrefix.Length..];
            if (tail.Length > 0 && tail.All(char.IsAsciiDigit) && long.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        }
        return string.Concat(dayPrefix, (max + 1).ToString(CultureInfo.InvariantCulture));
    }
}
