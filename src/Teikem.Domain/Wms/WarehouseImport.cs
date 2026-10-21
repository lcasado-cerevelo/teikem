using Teikem.Domain.Orders;

namespace Teikem.Domain.Wms;

/// <summary>
/// 2026-10-21 — Plantillas de importación de ALMACENES (3.ª entidad del motor genérico). Obligatorios: code y name, siempre en columna (cada almacén trae los suyos). País y modo de recepción
/// (y la dirección, salvo la calle) admiten valor por defecto.
/// </summary>
public static class WarehouseImportFields
{
    public const string Code = "code";
    public const string Name = "name";
    public const string Line1 = "line1";
    public const string City = "city";
    public const string State = "state";
    public const string PostalCode = "postalCode";
    public const string Country = "country";
    public const string ReceivingMode = "receivingMode";

    public static readonly IReadOnlyList<string> All = new[] { Code, Name, Line1, City, State, PostalCode, Country, ReceivingMode };
    public static readonly IReadOnlySet<string> NoDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Code, Name, Line1 };
    public const string CodeColumnMessage = "La plantilla debe incluir la columna code.";
    public const string NameColumnMessage = "La plantilla debe incluir la columna name.";

    public static readonly ImportEntityMeta Meta = new(ImportKinds.Warehouse,
        All.Select(f => new ImportFieldSpec(f, Required: f is Code or Name, DefaultAllowed: !NoDefault.Contains(f))).ToList(),
        new[]
        {
            new ImportRequirement(new[] { Code }, AllowDefault: false, CodeColumnMessage),
            new ImportRequirement(new[] { Name }, AllowDefault: false, NameColumnMessage),
        });
}
