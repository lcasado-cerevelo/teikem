using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P8) — proveedores de Compras (13B). Nombre único entre los activos del tenant (UX_Supplier_Name es la última
/// línea: 409 'Ya existe un proveedor activo con ese nombre.'), término de pago del catálogo PaymentTerm, baja lógica y
/// reactivación. PATCH: null = sin cambio; "" = quitar el valor (salvo el nombre, obligatorio); rowVersion opcional.
/// </summary>
public sealed class SupplierService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    public const string NameTaken = "Ya existe un proveedor activo con ese nombre.";
    public const string NameRequired = "El nombre del proveedor es obligatorio.";
    public const string EmailInvalid = "El correo electrónico no es válido.";
    public const string NotFound = "Proveedor";
    public const int NameMaxLength = 200;
    public const int ContactMaxLength = 150;
    public const int PhoneMaxLength = 40;
    public const int EmailMaxLength = 150;
    public const int NotesMaxLength = 2000;

    public static string TooLong(string label, int max) => $"{label} admite como máximo {max} caracteres.";
    public static string UnknownPaymentTerm(string code) => $"Término de pago desconocido: '{code}'.";

    // ---------------------------------------------------------------- lista

    public async Task<IReadOnlyList<SupplierDto>> ListAsync(bool includeInactive, string? search, CancellationToken ct)
    {
        var q = db.Set<Supplier>().AsNoTracking();
        if (!includeInactive) q = q.Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Name.Contains(s) || (x.ContactName != null && x.ContactName.Contains(s)) || (x.Email != null && x.Email.Contains(s)));
        }
        var rows = await q.OrderBy(s => s.Name).ThenBy(s => s.SupplierId).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    // ---------------------------------------------------------------- alta

    public async Task<SupplierDto> CreateAsync(SupplierRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = RequireTenant();
        var errors = new Dictionary<string, string[]>();
        var name = Required(req.Name, errors);
        var contact = Optional(req.ContactName, "contactName", "El contacto", ContactMaxLength, errors);
        var phone = Optional(req.Phone, "phone", "El teléfono", PhoneMaxLength, errors);
        var email = Email(req.Email, errors);
        var notes = Optional(req.Notes, "notes", "Las notas", NotesMaxLength, errors);
        var paymentTermId = await PaymentTermAsync(req.PaymentTerm, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        if (await db.Set<Supplier>().AnyAsync(s => s.IsActive && s.Name == name, ct)) throw new ConflictException(NameTaken);
        var supplier = new Supplier
        {
            TenantId = tenantId, Name = name!, ContactName = contact, Phone = phone, Email = email,
            PaymentTermLookupId = paymentTermId, Notes = notes, IsActive = true,
        };
        db.Set<Supplier>().Add(supplier);
        await db.SaveGuardedAsync(NameTaken, ct);
        return (await ToDtosAsync(new List<Supplier> { supplier }, ct))[0];
    }

    // ---------------------------------------------------------------- edición

    public async Task<SupplierDto> UpdateAsync(int id, SupplierPatchRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var supplier = await db.Set<Supplier>().FirstOrDefaultAsync(s => s.SupplierId == id, ct) ?? throw new NotFoundException(NotFound);
        db.ApplyRowVersion(supplier, req.RowVersion);

        var errors = new Dictionary<string, string[]>();
        string? name = req.Name is null ? null : Required(req.Name, errors);
        var contact = Optional(req.ContactName, "contactName", "El contacto", ContactMaxLength, errors);
        var phone = Optional(req.Phone, "phone", "El teléfono", PhoneMaxLength, errors);
        var email = Email(req.Email, errors);
        var notes = Optional(req.Notes, "notes", "Las notas", NotesMaxLength, errors);
        int? paymentTermId = req.PaymentTerm is null || req.PaymentTerm.Trim().Length == 0 ? null : await PaymentTermAsync(req.PaymentTerm, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        if (name is not null && name != supplier.Name)
        {
            if (supplier.IsActive && await db.Set<Supplier>().AnyAsync(s => s.SupplierId != id && s.IsActive && s.Name == name, ct))
                throw new ConflictException(NameTaken);
            supplier.Name = name;
        }
        if (req.ContactName is not null) supplier.ContactName = contact;
        if (req.Phone is not null) supplier.Phone = phone;
        if (req.Email is not null) supplier.Email = email;
        if (req.Notes is not null) supplier.Notes = notes;
        if (req.PaymentTerm is not null) supplier.PaymentTermLookupId = paymentTermId;

        await db.SaveGuardedAsync(NameTaken, ct);
        return (await ToDtosAsync(new List<Supplier> { supplier }, ct))[0];
    }

    /// <summary>Baja lógica o reactivación. Reactivar exige que ningún otro proveedor activo tenga el mismo nombre (409).</summary>
    public async Task<SupplierDto> SetActiveAsync(int id, bool active, CancellationToken ct)
    {
        var supplier = await db.Set<Supplier>().FirstOrDefaultAsync(s => s.SupplierId == id, ct) ?? throw new NotFoundException(NotFound);
        if (supplier.IsActive != active)
        {
            if (active && await db.Set<Supplier>().AnyAsync(s => s.SupplierId != id && s.IsActive && s.Name == supplier.Name, ct))
                throw new ConflictException(NameTaken);
            supplier.IsActive = active;
            await db.SaveGuardedAsync(NameTaken, ct);
        }
        return (await ToDtosAsync(new List<Supplier> { supplier }, ct))[0];
    }

    // ---------------------------------------------------------------- helpers

    private async Task<IReadOnlyList<SupplierDto>> ToDtosAsync(List<Supplier> rows, CancellationToken ct)
    {
        var terms = new Dictionary<int, string>();
        foreach (var id in rows.Where(r => r.PaymentTermLookupId != null).Select(r => r.PaymentTermLookupId!.Value).Distinct())
            if (await lookups.GetAsync(id, ct) is { } lc) terms[id] = lc.InternalCode;
        return rows.Select(s => new SupplierDto(s.SupplierId, s.Name, s.ContactName, s.Phone, s.Email,
            s.PaymentTermLookupId is int t ? terms.GetValueOrDefault(t) : null, s.Notes, s.IsActive,
            Convert.ToBase64String(s.RowVersion ?? Array.Empty<byte>()))).ToList();
    }

    private async Task<int?> PaymentTermAsync(string? code, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var id = await lookups.TryGetIdAsync(LookupDomains.PaymentTerm, code.Trim().ToUpperInvariant(), ct);
        if (id is null) errors["paymentTerm"] = new[] { UnknownPaymentTerm(code.Trim()) };
        return id;
    }

    private static string? Required(string? value, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) { errors["name"] = new[] { NameRequired }; return null; }
        var v = value.Trim();
        if (v.Length > NameMaxLength) { errors["name"] = new[] { TooLong("El nombre", NameMaxLength) }; return null; }
        return v;
    }

    private static string? Optional(string? value, string field, string label, int max, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > max) { errors[field] = new[] { TooLong(label, max) }; return null; }
        return v;
    }

    private static string? Email(string? value, Dictionary<string, string[]> errors)
    {
        var v = Optional(value, "email", "El correo electrónico", EmailMaxLength, errors);
        if (v is null) return null;
        var at = v.IndexOf('@');
        if (at <= 0 || at == v.Length - 1 || v.Contains(' ')) { errors["email"] = new[] { EmailInvalid }; return null; }
        return v;
    }

    private int RequireTenant() => ((TenantContext)tenant).RequireTenantId();
}
