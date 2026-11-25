using System.Globalization;
using System.Text;
using Teikem.Domain.Common;

namespace Teikem.Domain.Tenancy;

/// <summary>
/// Lote 243 — texto de dinero, fechas, horas y teléfonos con el formato de la compañía (<see cref="TenantFormat"/>), para lo que
/// sale del servidor ya escrito (el correo de la orden de venta; después, los de factura). Lógica pura: no depende de la cultura del
/// proceso. Dinero: símbolo antes («$1,234.50») o después con un espacio («1.234,50 €»), los decimales de la moneda (0, 2 o 3,
/// mitad hacia afuera) y los separadores de miles y decimal; el signo menos va delante de todo («-$5.00»). Fechas en el orden y con
/// el separador de la compañía (MDY, DMY, YMD); hora en 12 h («3:05 PM») o 24 h («15:05»).
/// </summary>
public static class TenantFormatText
{
    /// <summary>Monto con el símbolo y los separadores de la compañía.</summary>
    public static string Money(decimal amount, TenantFormat f)
    {
        var number = Number(Math.Abs(amount), f.CurrencyDecimals, f);
        var sign = Math.Round(amount, f.CurrencyDecimals, MidpointRounding.AwayFromZero) < 0m ? "-" : string.Empty;
        return f.CurrencySymbolPosition == TenantFormatRules.SymbolPositions.After
            ? $"{sign}{number} {f.CurrencySymbol}"
            : $"{sign}{f.CurrencySymbol}{number}";
    }

    /// <summary>Número con <paramref name="decimals"/> decimales fijos (mitad hacia afuera) y los separadores de la compañía; sin símbolo.</summary>
    public static string Number(decimal value, int decimals, TenantFormat f)
    {
        decimals = Math.Clamp(decimals, 0, 6);
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        var negative = rounded < 0m;
        var text = Math.Abs(rounded).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        var integer = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        var sb = new StringBuilder();
        for (var i = 0; i < integer.Length; i++)
        {
            if (i > 0 && (integer.Length - i) % 3 == 0) sb.Append(f.ThousandsSeparator);
            sb.Append(integer[i]);
        }
        if (fraction.Length > 0) sb.Append(f.DecimalSeparator).Append(fraction);
        return (negative ? "-" : string.Empty) + sb;
    }

    /// <summary>Cantidad sin ceros de más («4», «2.5», «1,250»): hasta 3 decimales, con los separadores de la compañía.</summary>
    public static string Quantity(decimal value, TenantFormat f)
    {
        var rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        var decimals = 0;
        while (decimals < 3 && Math.Round(rounded, decimals) != rounded) decimals++;
        return Number(rounded, decimals, f);
    }

    /// <summary>Fecha en el orden y con el separador de la compañía (MDY → 11/24/2026; DMY → 24/11/2026; YMD → 2026-11-24 con «-»).</summary>
    public static string Date(DateOnly d, TenantFormat f)
    {
        var dd = d.Day.ToString("00", CultureInfo.InvariantCulture);
        var mm = d.Month.ToString("00", CultureInfo.InvariantCulture);
        var yyyy = d.Year.ToString("0000", CultureInfo.InvariantCulture);
        var s = f.DateSeparator;
        return f.DateOrder switch
        {
            TenantFormatRules.DateOrders.DayMonthYear => $"{dd}{s}{mm}{s}{yyyy}",
            TenantFormatRules.DateOrders.YearMonthDay => $"{yyyy}{s}{mm}{s}{dd}",
            _ => $"{mm}{s}{dd}{s}{yyyy}",
        };
    }

    /// <summary>Hora local en 12 h («3:05 PM», «12:00 AM») o 24 h («15:05»).</summary>
    public static string Time(TimeOnly t, TenantFormat f)
    {
        if (f.TimeFormat == 24) return t.ToString("HH:mm", CultureInfo.InvariantCulture);
        var hour = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        return $"{hour.ToString(CultureInfo.InvariantCulture)}:{t.Minute.ToString("00", CultureInfo.InvariantCulture)} {(t.Hour < 12 ? "AM" : "PM")}";
    }

    /// <summary>Fecha y hora de un instante YA LOCAL (la hora de la compañía).</summary>
    public static string DateTime(System.DateTime local, TenantFormat f)
        => $"{Date(DateOnly.FromDateTime(local), f)} {Time(TimeOnly.FromDateTime(local), f)}";

    /// <summary>Fecha y hora de un instante UTC en la zona horaria de la compañía (zona desconocida → la de Puerto Rico, como LocalDay).</summary>
    public static string DateTimeFromUtc(System.DateTime utc, TenantFormat f)
        => DateTime(LocalDay.ToLocal(utc, LocalDay.ResolveZone(f.TimeZoneId)), f);

    /// <summary>
    /// Teléfono guardado con solo dígitos, escrito con la máscara de la compañía (cada # es un dígito: «(787) 555-1234»). Si la cantidad
    /// de dígitos no coincide con la máscara, se devuelve tal cual (sin inventar formato).
    /// </summary>
    public static string Phone(string? digits, TenantFormat f)
    {
        var raw = (digits ?? string.Empty).Trim();
        var only = new string(raw.Where(char.IsAsciiDigit).ToArray());
        var slots = f.PhoneMask.Count(c => c == TenantFormatRules.PhoneMaskDigit);
        if (only.Length == 0 || only.Length != slots || only.Length != raw.Length) return raw;
        var sb = new StringBuilder();
        var i = 0;
        foreach (var c in f.PhoneMask) sb.Append(c == TenantFormatRules.PhoneMaskDigit ? only[i++] : c);
        return sb.ToString();
    }
}
