using Teikem.Domain.Orders;

namespace Teikem.Domain.Wms;

/// <summary>
/// 2026-10-21 — Plantillas de importación de PROVEEDORES (2.ª entidad del motor genérico). Obligatorio: name (siempre en columna: el nombre es único entre los proveedores activos). Los datos propios
/// de cada proveedor (contacto, teléfono, correo) no admiten valor por defecto; sí el prefijo del teléfono, la condición de pago y las notas.
/// </summary>
public static class SupplierImportFields
{
    public const string Name = "name";
    public const string ContactName = "contactName";
    public const string Phone = "phone";
    public const string PhoneCountryCallingCode = "phoneCountryCallingCode";
    public const string Email = "email";
    public const string PaymentTerm = "paymentTerm";
    public const string Notes = "notes";

    public static readonly IReadOnlyList<string> All = new[] { Name, ContactName, Phone, PhoneCountryCallingCode, Email, PaymentTerm, Notes };
    public static readonly IReadOnlySet<string> NoDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Name, ContactName, Phone, Email };
    public const string NameColumnMessage = "La plantilla debe incluir la columna name.";

    public static readonly ImportEntityMeta Meta = new(ImportKinds.Supplier,
        All.Select(f => new ImportFieldSpec(f, Required: f == Name, DefaultAllowed: !NoDefault.Contains(f))).ToList(),
        new[] { new ImportRequirement(new[] { Name }, AllowDefault: false, NameColumnMessage) });
}
