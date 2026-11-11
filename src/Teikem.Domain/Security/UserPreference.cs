using Teikem.Domain.Common;

namespace Teikem.Domain.Security;

/// <summary>
/// Preferencia PROPIA de una persona en una compañía (2026-11-11, pedido de Luis): «con qué modo captura las órdenes de venta» y «el último almacén
/// que usó». Una fila por (compañía, usuario, clave); el valor es texto corto. Las claves permitidas viven en <see cref="UserPreferenceKeys"/>.
/// </summary>
public class UserPreference : ITenantScoped
{
    public int UserPreferenceId { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public string PrefKey { get; set; } = string.Empty;
    public string PrefValue { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Claves permitidas y su validación. Una clave nueva se agrega aquí (y, si hace falta, en la pantalla que la usa).</summary>
public static class UserPreferenceKeys
{
    /// <summary>Modo de captura de órdenes de venta: <c>wizard</c> (asistente por pasos) o <c>full</c> (formulario completo).</summary>
    public const string SalesEntryMode = "sales.entryMode";
    /// <summary>PublicId (GUID) del último almacén que la persona usó, en la web o en la app de almacén.</summary>
    public const string LastWarehouse = "warehouse.last";

    public static bool IsKnown(string key) => key is SalesEntryMode or LastWarehouse;

    public static bool IsValid(string key, string value) => key switch
    {
        SalesEntryMode => value is "wizard" or "full",
        LastWarehouse => Guid.TryParse(value, out _),
        _ => false,
    };
}
