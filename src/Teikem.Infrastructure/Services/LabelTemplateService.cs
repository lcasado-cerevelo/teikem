using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Printing;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 43 (F-G1) — plantillas de label de la compañía (el constructor de la maqueta: 9 elementos con Mostrar, Posición y Tamaño).
/// - Nombre obligatorio (recortado, ≤ 80), único entre las ACTIVAS (409; UX_LabelTemplate_Name es la segunda barrera).
/// - La construcción se valida y se guarda como JSON canónico (<see cref="LabelLayout"/>): posición y tamaño conocidos, «como código de
///   barras» solo en waybill y factura, y al menos un código de barras visible.
/// - La primera plantilla activa nace predeterminada; «hacer predeterminada» quita la anterior en una transacción con dos SaveChanges;
///   desactivar la predeterminada la deja sin predeterminada (aplica el label integrado). Nunca DELETE.
/// </summary>
public sealed class LabelTemplateService(TeikemDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<LabelTemplateDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.LabelTemplates.AsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        var rows = await q.OrderBy(t => t.Name).ThenBy(t => t.LabelTemplateId).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<LabelTemplateDto> GetAsync(Guid publicId, CancellationToken ct) => ToDto(await LoadAsync(publicId, ct, track: false));

    public async Task<LabelTemplateDto> CreateAsync(LabelTemplateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var (name, nameError) = PrintingRules.NormalizeName(req.Name, PrintingRules.TemplateNameRequiredMessage);
        if (nameError is not null) throw new ValidationException("name", nameError);
        if (req.Layout is null) throw new ValidationException("layout", LabelLayout.LayoutRequiredMessage);
        var layout = NormalizeLayout(req.Layout);

        if (await db.LabelTemplates.AnyAsync(t => t.IsActive && t.Name == name, ct)) throw new ConflictException(PrintingRules.DuplicateTemplateMessage);
        var hasDefault = await db.LabelTemplates.AnyAsync(t => t.IsActive && t.IsDefault, ct);

        var template = new LabelTemplate
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name!,
            LayoutJson = layout.ToJson(),
            IsDefault = !hasDefault,   // la primera activa nace predeterminada
            IsActive = true,
        };
        db.LabelTemplates.Add(template);
        await db.SaveGuardedAsync(PrintingRules.DuplicateTemplateMessage, ct);
        return ToDto(template);
    }

    /// <summary>Edición: null = sin cambio. rowVersion opcional (409 si cambió).</summary>
    public async Task<LabelTemplateDto> UpdateAsync(Guid publicId, LabelTemplateRequest req, CancellationToken ct)
    {
        var template = await LoadAsync(publicId, ct);
        db.ApplyRowVersion(template, req.RowVersion);
        if (req.Name is not null)
        {
            var (name, error) = PrintingRules.NormalizeName(req.Name, PrintingRules.TemplateNameRequiredMessage);
            if (error is not null) throw new ValidationException("name", error);
            if (template.IsActive && !string.Equals(name, template.Name, StringComparison.Ordinal)
                && await db.LabelTemplates.AnyAsync(t => t.IsActive && t.Name == name && t.LabelTemplateId != template.LabelTemplateId, ct))
                throw new ConflictException(PrintingRules.DuplicateTemplateMessage);
            template.Name = name!;
        }
        if (req.Layout is not null) template.LayoutJson = NormalizeLayout(req.Layout).ToJson();
        await db.SaveGuardedAsync(PrintingRules.DuplicateTemplateMessage, ct);
        return ToDto(template);
    }

    /// <summary>Baja lógica o reactivación (desactivar la predeterminada la deja sin predeterminada; reactivar no la vuelve).</summary>
    public async Task<LabelTemplateDto> SetActiveAsync(Guid publicId, bool active, CancellationToken ct)
    {
        var template = await LoadAsync(publicId, ct);
        if (template.IsActive == active) return ToDto(template);
        if (active)
        {
            if (await db.LabelTemplates.AnyAsync(t => t.IsActive && t.Name == template.Name && t.LabelTemplateId != template.LabelTemplateId, ct))
                throw new ConflictException(PrintingRules.DuplicateTemplateMessage);
            template.IsActive = true;
        }
        else
        {
            template.IsActive = false;
            template.IsDefault = false;
        }
        await db.SaveGuardedAsync(PrintingRules.DuplicateTemplateMessage, ct);
        return ToDto(template);
    }

    /// <summary>La hace la predeterminada de la compañía (quita la anterior primero; dos SaveChanges en una transacción). 409 si está inactiva.</summary>
    public Task<LabelTemplateDto> MakeDefaultAsync(Guid publicId, CancellationToken ct)
        => db.RunInTransactionAsync(async ct2 =>
        {
            var template = await LoadAsync(publicId, ct2);
            if (!template.IsActive) throw new ConflictException(PrintingRules.TemplateInactiveDefaultMessage);
            if (template.IsDefault) return ToDto(template);

            var previous = await db.LabelTemplates.Where(t => t.IsDefault && t.LabelTemplateId != template.LabelTemplateId).ToListAsync(ct2);
            if (previous.Count > 0)
            {
                foreach (var t in previous) t.IsDefault = false;
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            }
            template.IsDefault = true;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return ToDto(template);
        }, ct);

    private async Task<LabelTemplate> LoadAsync(Guid publicId, CancellationToken ct, bool track = true)
    {
        var q = track ? db.LabelTemplates : db.LabelTemplates.AsNoTracking();
        return await q.FirstOrDefaultAsync(t => t.PublicId == publicId, ct)
               ?? throw new NotFoundException("Plantilla de label", null, true);
    }

    /// <summary>Valida la construcción del cliente (400 con el mensaje exacto de <see cref="LabelLayout"/>).</summary>
    public static LabelLayout NormalizeLayout(LabelLayoutDto dto)
    {
        var input = new Dictionary<string, LabelElement?>(StringComparer.Ordinal)
        {
            ["logo"] = FromDto(dto.Logo),
            ["service"] = FromDto(dto.Service),
            ["sender"] = FromDto(dto.Sender),
            ["consignee"] = FromDto(dto.Consignee),
            ["address"] = FromDto(dto.Address),
            ["barcode"] = FromDto(dto.Barcode),
            ["waybill"] = FromDto(dto.Waybill),
            ["invoice"] = FromDto(dto.Invoice),
            ["cod"] = FromDto(dto.Cod),
        };
        var (layout, error) = LabelLayout.Normalize(input);
        if (error is not null) throw new ValidationException("layout", error);
        return layout!;
    }

    private static LabelElement? FromDto(LabelElementDto? e) => e is null ? null : new LabelElement(e.On, e.Pos, e.Size, e.AsBarcode);

    private static LabelElementDto ToDto(LabelElement e) => new(e.On, e.Pos, e.Size, e.AsBarcode);

    /// <summary>La construcción como DTO (todos los elementos presentes).</summary>
    public static LabelLayoutDto ToDto(LabelLayout l) => new(
        ToDto(l.Logo), ToDto(l.Service), ToDto(l.Sender), ToDto(l.Consignee), ToDto(l.Address),
        ToDto(l.Barcode), ToDto(l.Waybill), ToDto(l.Invoice), ToDto(l.Cod));

    /// <summary>Plantilla como DTO; un JSON dañado en la base se lee como el label integrado (nunca un 500).</summary>
    public static LabelTemplateDto ToDto(LabelTemplate t) => new(
        t.PublicId, t.Name, ToDto(LabelLayout.TryParse(t.LayoutJson) ?? LabelLayout.BuiltIn), t.IsDefault, t.IsActive,
        Convert.ToBase64String(t.RowVersion ?? Array.Empty<byte>()));
}
