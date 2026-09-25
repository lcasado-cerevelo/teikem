namespace Teikem.Domain.Clients;

/// <summary>
/// Resumen legible del modelo de facturación del contrato (los 5 checkboxes) en orden fijo:
/// "Por servicio, Pieza extra, Despacho, COD, Especiales" / "Per service, Extra piece, Dispatch, COD, Special".
/// Ninguno marcado → "Sin componentes" / "No components". Lo usan la lista de clientes, la ficha y las fuentes de datos.
/// </summary>
public static class BillingModelSummary
{
    public static string Build(bool perService, bool extraPiece, bool dispatch, bool cod, bool special, string? lang)
    {
        var en = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
        var parts = new List<string>(5);
        if (perService) parts.Add(en ? "Per service" : "Por servicio");
        if (extraPiece) parts.Add(en ? "Extra piece" : "Pieza extra");
        if (dispatch) parts.Add(en ? "Dispatch" : "Despacho");
        if (cod) parts.Add("COD");
        if (special) parts.Add(en ? "Special" : "Especiales");
        return parts.Count == 0 ? (en ? "No components" : "Sin componentes") : string.Join(", ", parts);
    }
}
