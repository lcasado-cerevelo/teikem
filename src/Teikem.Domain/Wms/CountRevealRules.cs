namespace Teikem.Domain.Wms;

/// <summary>Valores del ajuste de compañía "Mostrar lo esperado al contar" (Tenant.CountExpectedReveal).</summary>
public static class CountRevealModes
{
    /// <summary>Ningún contador ve lo esperado, sin excepciones.</summary>
    public const string None = "NONE";
    /// <summary>Solo los contadores marcados con Sí (UserTenant.CountSeeExpected = 1) lo ven al capturar. Valor por defecto.</summary>
    public const string Marked = "MARKED";
    /// <summary>Todos los contadores lo ven al capturar, salvo los marcados con No.</summary>
    public const string All = "ALL";

    public static readonly IReadOnlyList<string> Values = new[] { None, Marked, All };
}

/// <summary>Cómo ve lo esperado quien abre el conteo (CycleCountDetailDto.Reveal).</summary>
public static class CountRevealViews
{
    /// <summary>Con warehouse.count (supervisor): lo ve desde el inicio, como siempre.</summary>
    public const string Full = "FULL";
    /// <summary>Contador autorizado: lo ve de cada línea DESPUÉS de capturarla (POST …/lines/{id}/check).</summary>
    public const string AtCapture = "AT_CAPTURE";
    /// <summary>Conteo a ciegas, sin retroalimentación.</summary>
    public const string None = "NONE";
}

/// <summary>Estado de la verificación de una línea (CycleCountLine.CheckState).</summary>
public static class CountCheckStates
{
    /// <summary>La primera cifra quedó dentro del margen: línea cerrada para el contador.</summary>
    public const string Match = "MATCH";
    /// <summary>La primera cifra quedó fuera del margen: el contador debe recontar (sin decirle el esperado).</summary>
    public const string Recount = "RECOUNT";
    /// <summary>Ya se recontó: línea cerrada para el contador; el supervisor reconcilia con la primera y la última cifra.</summary>
    public const string Final = "FINAL";
}

/// <summary>
/// Conteo informado al capturar (tarea 25): el contador (solo warehouse.count.capture) ve lo esperado de una línea únicamente después de
/// aceptar su cantidad; si queda fuera del margen se le pide recontar sin decirle el esperado; tras el reconteo la línea se cierra. Quien
/// tiene warehouse.count (supervisor) no pasa por esto. Reglas puras; los mensajes los citan el manual y la FAQ.
/// </summary>
public static class CountRevealRules
{
    public const string LineLocked = "La línea ya se verificó; no se puede cambiar su cantidad.";
    public const string NotAllowed = "No está habilitado ver lo esperado al contar.";
    public static string UnknownMode(string value) => $"Modo desconocido: '{value}'. Use NONE, MARKED o ALL.";
    public const string TolerancePctRange = "El margen de reconteo debe estar entre 0 y 100 %.";
    public const string SerialNotSupported = "Los productos con serie no se verifican contra lo esperado.";
    public const string CheckedQtyMismatch = "La cantidad no es la que se verificó en la línea; no se puede cambiar después de ver el resultado.";

    /// <summary>Margen por defecto: 0 % = cualquier diferencia pide reconteo.</summary>
    public const decimal DefaultTolerancePct = 0m;

    /// <summary>Normaliza el modo de la compañía; null o desconocido = MARKED (el seguro: nadie hasta que se marque).</summary>
    public static string Normalize(string? mode)
        => CountRevealModes.Values.FirstOrDefault(v => string.Equals(v, mode?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? CountRevealModes.Marked;

    public static (string? Mode, string? Error) ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var v = value.Trim();
        var known = CountRevealModes.Values.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase));
        return known is null ? (null, UnknownMode(v)) : (known, null);
    }

    /// <summary>
    /// ¿Puede ESTE contador ver lo esperado al capturar? NONE → no; ALL → sí salvo que su ajuste sea No; MARKED → solo si su ajuste es Sí.
    /// (El supervisor no pasa por aquí: ve todo, ver <see cref="ViewFor"/>.)
    /// </summary>
    public static bool CanRevealAtCapture(string? tenantMode, bool? userFlag) => Normalize(tenantMode) switch
    {
        CountRevealModes.None => false,
        CountRevealModes.All => userFlag != false,
        _ => userFlag == true,
    };

    /// <summary>Cómo ve el conteo quien lo abre.</summary>
    public static string ViewFor(bool hasCountPermission, bool canRevealAtCapture)
        => hasCountPermission ? CountRevealViews.Full : canRevealAtCapture ? CountRevealViews.AtCapture : CountRevealViews.None;

    /// <summary>¿La cantidad contada queda dentro del margen (en %) de lo esperado? Con 0 % solo la igualdad exacta.</summary>
    public static bool WithinTolerance(decimal expected, decimal counted, decimal tolerancePct)
    {
        var diff = Math.Abs(counted - expected);
        if (diff == 0m) return true;
        if (tolerancePct <= 0m) return false;
        return diff <= expected * tolerancePct / 100m;
    }

    /// <summary>La línea ya no admite verificar de nuevo: cerrada por coincidencia o por reconteo.</summary>
    public static bool IsClosed(string? checkState) => checkState is CountCheckStates.Match or CountCheckStates.Final;

    /// <summary>
    /// Resultado de una verificación: sin estado previo → MATCH (dentro del margen) o RECOUNT; con RECOUNT previo → FINAL (sea cual sea la
    /// segunda cifra). El estado previo cerrado no llega aquí (el servicio responde 409 LineLocked).
    /// </summary>
    public static string Next(string? previousState, bool within)
        => previousState == CountCheckStates.Recount ? CountCheckStates.Final : within ? CountCheckStates.Match : CountCheckStates.Recount;

    /// <summary>
    /// ¿Se puede guardar esta cantidad en una línea ya verificada? El contador (sin warehouse.count) solo puede guardar la cantidad que
    /// verificó (LastCheckQty): así no se ajusta lo contado al esperado después de verlo. El supervisor siempre puede (la corrección queda
    /// registrada por la regla de evidencia).
    /// </summary>
    public static bool CanSaveChecked(decimal? lastCheckQty, decimal? counted, bool hasCountPermission)
        => hasCountPermission || lastCheckQty is null || lastCheckQty == counted;
}
