namespace Teikem.Domain.Fleet;

/// <summary>
/// Reglas puras del chofer (Lote 4, P2): nombre, tope de paradas, número de licencia/certificación y zona de despacho.
/// Sin EF ni servicios: cada validación devuelve el mensaje exacto de error (o null si es válida) y el servicio lo
/// traduce a 400 en el campo correspondiente. Se prueba con xunit (DriverRulesTests).
/// </summary>
public static class DriverRules
{
    /// <summary>Largo de Driver.EmployeeCode (NVARCHAR(30)): el 'Código' del chofer, fijo desde el alta.</summary>
    public const int CodeMaxLength = 30;
    /// <summary>Largo de Driver.FullName (NVARCHAR(150)).</summary>
    public const int FullNameMaxLength = 150;
    /// <summary>Largo de DriverLicense.LicenseNumber y DriverCertification.CertNumber (NVARCHAR(60)).</summary>
    public const int DocumentNumberMaxLength = 60;
    /// <summary>Largo de DispatchZone.Code (NVARCHAR(20)).</summary>
    public const int ZoneCodeMaxLength = 20;
    /// <summary>Largo de DispatchZone.Name (NVARCHAR(120)); el nombre de la zona primaria es el 'Área' del chofer.</summary>
    public const int ZoneNameMaxLength = 120;

    public const string CodeRequiredMessage = "El código del chofer es obligatorio.";
    public const string CodeImmutableMessage = "El código del chofer se fija al crearlo; no se puede cambiar.";
    public const string DuplicateCodeMessage = "Ya existe un chofer con ese código.";
    public const string FullNameRequiredMessage = "El nombre del chofer es obligatorio.";
    public static readonly string FullNameTooLongMessage = $"El nombre del chofer admite como máximo {FullNameMaxLength} caracteres.";
    public const string MaxStopsMessage = "El tope de paradas debe ser mayor o igual a 1.";
    public const string LicenseNumberRequiredMessage = "El número de licencia es obligatorio.";
    public static readonly string LicenseNumberTooLongMessage = $"El número de licencia admite como máximo {DocumentNumberMaxLength} caracteres.";
    public static readonly string CertNumberTooLongMessage = $"El número de certificación admite como máximo {DocumentNumberMaxLength} caracteres.";

    // Vínculo con un usuario interno (ValidateUserLinkAsync del servicio)
    public const string UserNotInternalMessage = "Solo un usuario interno se puede vincular a un chofer.";
    public const string UserMembershipInactiveMessage = "El usuario no tiene una membresía activa en esta compañía.";
    public const string UserAlreadyLinkedMessage = "El usuario ya está vinculado a otro chofer.";

    // Estatus y baja
    public const string CannotReactivateRetiredMessage = "El chofer fue eliminado; no se puede reactivar.";
    public const string AlreadyRetiredMessage = "El chofer ya fue eliminado.";
    /// <summary>Un chofer eliminado (estatus terminal) solo se consulta: ficha, licencias y certificaciones no admiten cambios.</summary>
    public const string RetiredReadOnlyMessage = "El chofer fue eliminado; solo se consulta su historial.";

    // Zonas de despacho
    public const string ZoneInactiveMessage = "La zona de despacho está inactiva.";
    public const string ZoneCodeRequiredMessage = "El código de la zona es obligatorio.";
    public const string ZoneCodeImmutableMessage = "El código de la zona se fija al crearla; no se puede cambiar.";
    public const string DuplicateZoneMessage = "Ya existe una zona de despacho con ese código.";
    public static readonly string ZoneNameTooLongMessage = $"El nombre de la zona admite como máximo {ZoneNameMaxLength} caracteres.";
    public const string ZoneHasDriversMessage = "La zona tiene choferes asignados; reasígnelos antes de inactivarla.";

    /// <summary>Nombre obligatorio, recortado, máximo 150. Devuelve (valor, null) o (null, mensaje).</summary>
    public static (string? Value, string? Error) ValidateFullName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, FullNameRequiredMessage);
        var v = raw.Trim();
        return v.Length > FullNameMaxLength ? (null, FullNameTooLongMessage) : (v, null);
    }

    /// <summary>Tope de paradas propio del chofer: null = usa el default del tenant; si viene, ≥ 1.</summary>
    public static string? ValidateMaxStops(int? value) => value is < 1 ? MaxStopsMessage : null;

    /// <summary>Número de licencia obligatorio, recortado, máximo 60.</summary>
    public static (string? Value, string? Error) ValidateLicenseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, LicenseNumberRequiredMessage);
        var v = raw.Trim();
        return v.Length > DocumentNumberMaxLength ? (null, LicenseNumberTooLongMessage) : (v, null);
    }

    /// <summary>Número de certificación opcional (vacío = null), recortado, máximo 60.</summary>
    public static (string? Value, string? Error) ValidateCertNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var v = raw.Trim();
        return v.Length > DocumentNumberMaxLength ? (null, CertNumberTooLongMessage) : (v, null);
    }

    /// <summary>Nombre de la zona opcional (vacío = null), recortado, máximo 120.</summary>
    public static (string? Value, string? Error) ValidateZoneName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var v = raw.Trim();
        return v.Length > ZoneNameMaxLength ? (null, ZoneNameTooLongMessage) : (v, null);
    }
}
