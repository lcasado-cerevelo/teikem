using Teikem.Domain.Common;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Lote 129 (dictado de Luis, 2026-10-19) — NIVEL DE ZONA: un grupo de zonas de despacho por compañía (Advance: METRO e ISLA; cada compañía los suyos o ninguno). Es un dato de la zona, no una
/// tarifa: la orden hereda el nivel de su zona de entrega y queda disponible en listas, filtros, Radar, vistas e indicadores. Las tarifas del chofer y del contrato PUEDEN llevar nivel (opcional).
/// DefaultDriverId: el chofer al que se le mandan las rutas de las zonas de este nivel que no tienen chofer propio (Advance: Isla se la entrega entera a un solo «chofer», una compañía externa).
/// </summary>
[AuditEntity(Constants.EntityTypes.ZoneGroup)]
public class ZoneGroup : ITenantScoped, ISoftDeletable
{
    public int ZoneGroupId { get; set; }
    public int TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int SortOrder { get; set; }
    public int? DefaultDriverId { get; set; }
    public bool IsActive { get; set; } = true;

    public Driver? DefaultDriver { get; set; }
}

/// <summary>Reglas puras del nivel de zona.</summary>
public static class ZoneGroupRules
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 120;
    public const string CodeRequiredMessage = "Indique el código del nivel.";
    public static readonly string CodeTooLongMessage = $"El código del nivel admite como máximo {CodeMaxLength} caracteres.";
    public static readonly string NameTooLongMessage = $"El nombre del nivel admite como máximo {NameMaxLength} caracteres.";
    public const string DuplicateMessage = "Ya existe un nivel de zona con ese código.";
    public const string CodeImmutableMessage = "El código del nivel se fija al crearlo y no se puede cambiar.";
    public const string NotFoundMessage = "El nivel de zona no existe o está inactivo.";
    public const string DriverNotFoundMessage = "El chofer por omisión no existe o está inactivo.";
    public const string InUseMessage = "El nivel tiene zonas activas; quíteselo a las zonas antes de inactivarlo.";

    public static (string? Code, string? Error) NormalizeCode(string? raw)
    {
        var code = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0) return (null, CodeRequiredMessage);
        if (code.Length > CodeMaxLength) return (null, CodeTooLongMessage);
        return (code, null);
    }

    public static (string? Name, string? Error) ValidateName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        if (name.Length == 0) return (null, null);
        return name.Length > NameMaxLength ? (null, NameTooLongMessage) : (name, null);
    }
}
