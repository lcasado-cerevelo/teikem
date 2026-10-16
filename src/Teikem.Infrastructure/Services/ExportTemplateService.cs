using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Export;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 77 (P5) — plantillas de exportación a contabilidad (formato, delimitador, fecha, encabezado y columnas) por compañía y alcance. Cada compañía arranca con «Formato estándar» (todas las columnas)
/// la primera vez que se consulta un alcance, para que el mecanismo se vea sin configurar nada. El nombre no se repite entre las activas (sin distinguir mayúsculas ni acentos). Una plantilla no se
/// borra: se desactiva, y no se puede desactivar la última del alcance.
/// </summary>
public sealed class ExportTemplateService(TeikemDbContext db, ITenantContext tenant)
{
    public const string Concurrency = "La plantilla cambió mientras se guardaba. Vuelva a abrirla e intente de nuevo.";
    private TenantContext T => (TenantContext)tenant;

    private static string RequireScope(string? scope)
    {
        var s = scope?.Trim().ToUpperInvariant() ?? ExportCodes.ScopeBilling;
        if (!ExportCodes.Scopes.Contains(s)) throw new ValidationException("scope", ExportRules.ScopeInvalid);
        return s;
    }

    public IReadOnlyList<ExportFieldDto> Fields(string? scope)
    {
        var s = RequireScope(scope);
        var lang = T.Lang;
        return ExportFields.Of(s).Select(f => new ExportFieldDto(f.Code, f.Kind.ToString().ToUpperInvariant(), f.DefaultHeader, lang == "en" ? f.LabelEn : f.LabelEs)).ToList();
    }

    public async Task<IReadOnlyList<ExportTemplateDto>> ListAsync(string? scope, CancellationToken ct)
    {
        var s = RequireScope(scope);
        await EnsureDefaultAsync(s, ct);
        var rows = await db.ExportTemplates.AsNoTracking().Where(t => t.Scope == s && t.IsActive).ToListAsync(ct);
        return rows.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(ToDto).ToList();
    }

    public async Task<ExportTemplateDto> GetAsync(Guid publicId, CancellationToken ct)
        => ToDto(await db.ExportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.PublicId == publicId && t.IsActive, ct) ?? throw new NotFoundException("Plantilla de exportación", feminine: true));

    public async Task<ExportTemplateDto> CreateAsync(ExportTemplateRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var scope = RequireScope(req.Scope);
        var v = Validate(req, null);
        await EnsureNameFreeAsync(scope, v.Name, null, ct);
        var t = new ExportTemplate
        {
            PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), Scope = scope, Name = v.Name, Format = v.Format, Delimiter = v.Delimiter, DateFormat = v.DateFormat,
            IncludeHeader = v.IncludeHeader, ColumnsJson = ExportRules.SerializeColumns(v.Columns),
        };
        db.ExportTemplates.Add(t);
        await db.SaveGuardedAsync(Concurrency, ct);
        return ToDto(t);
    }

    public async Task<ExportTemplateDto> UpdateAsync(Guid publicId, ExportTemplateRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var t = await db.ExportTemplates.AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct) ?? throw new NotFoundException("Plantilla de exportación", feminine: true);
        var v = Validate(req, t);
        await EnsureNameFreeAsync(t.Scope, v.Name, t.ExportTemplateId, ct);
        (t.Name, t.Format, t.Delimiter, t.DateFormat, t.IncludeHeader, t.ColumnsJson) = (v.Name, v.Format, v.Delimiter, v.DateFormat, v.IncludeHeader, ExportRules.SerializeColumns(v.Columns));
        await db.SaveGuardedAsync(Concurrency, ct);
        return ToDto(t);
    }

    public async Task DeactivateAsync(Guid publicId, CancellationToken ct)
    {
        var t = await db.ExportTemplates.AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct) ?? throw new NotFoundException("Plantilla de exportación", feminine: true);
        if (!await db.ExportTemplates.AnyAsync(x => x.Scope == t.Scope && x.IsActive && x.ExportTemplateId != t.ExportTemplateId, ct)) throw new ConflictException(ExportRules.LastTemplate);
        t.IsActive = false;
        await db.SaveGuardedAsync(Concurrency, ct);
    }

    /// <summary>La plantilla pedida o, si no se pide ninguna, la primera del alcance por nombre (la estándar).</summary>
    public async Task<ExportTemplate> ResolveAsync(string scope, Guid? publicId, CancellationToken ct)
    {
        await EnsureDefaultAsync(scope, ct);
        if (publicId is Guid id)
        {
            var found = await db.ExportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.PublicId == id && t.IsActive, ct) ?? throw new NotFoundException("Plantilla de exportación", feminine: true);
            if (found.Scope != scope) throw new ValidationException("templatePublicId", ExportRules.TemplateScopeMismatch);
            return found;
        }
        return (await db.ExportTemplates.AsNoTracking().Where(t => t.Scope == scope && t.IsActive).ToListAsync(ct)).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).First();
    }

    // ------------------------------------------------------------------ ayudas

    private async Task EnsureDefaultAsync(string scope, CancellationToken ct)
    {
        if (await db.ExportTemplates.AnyAsync(t => t.Scope == scope && t.IsActive, ct)) return;
        db.ExportTemplates.Add(new ExportTemplate
        {
            PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), Scope = scope, Name = ExportCodes.DefaultTemplateName, Format = ExportCodes.FormatCsv, Delimiter = ",",
            DateFormat = ExportCodes.DateIso, IncludeHeader = true, ColumnsJson = ExportRules.SerializeColumns(ExportRules.DefaultColumns(scope)),
        });
        await db.SaveGuardedAsync(Concurrency, ct);
    }

    private async Task EnsureNameFreeAsync(string scope, string name, int? exceptId, CancellationToken ct)
    {
        var key = ExportRules.NormalizeName(name);
        var names = await db.ExportTemplates.AsNoTracking().Where(t => t.Scope == scope && t.IsActive && t.ExportTemplateId != exceptId).Select(t => t.Name).ToListAsync(ct);
        if (names.Any(n => ExportRules.NormalizeName(n) == key)) throw new ConflictException(ExportRules.NameTaken);
    }

    private sealed record Valid(string Name, string Format, string Delimiter, string DateFormat, bool IncludeHeader, IReadOnlyList<ExportColumn> Columns);

    /// <summary>Valida y completa con lo que ya tenía la plantilla (cuando se cambia) o con los valores por omisión (cuando se crea).</summary>
    private static Valid Validate(ExportTemplateRequest req, ExportTemplate? current)
    {
        var name = (req.Name ?? current?.Name)?.Trim();
        if (string.IsNullOrEmpty(name)) throw new ValidationException("name", ExportRules.NameRequired);
        if (name.Length > ExportRules.MaxNameLength) throw new ValidationException("name", ExportRules.NameTooLong);
        var format = (req.Format ?? current?.Format ?? ExportCodes.FormatCsv).Trim().ToUpperInvariant();
        if (!ExportCodes.Formats.Contains(format)) throw new ValidationException("format", ExportRules.FormatInvalid);
        var delimiter = req.Delimiter ?? current?.Delimiter ?? ",";
        if (string.Equals(delimiter, "tab", StringComparison.OrdinalIgnoreCase) || delimiter == "\\t") delimiter = "\t";
        if (!ExportCodes.Delimiters.Contains(delimiter)) throw new ValidationException("delimiter", ExportRules.DelimiterInvalid);
        var dateFormat = (req.DateFormat ?? current?.DateFormat ?? ExportCodes.DateIso).Trim().ToUpperInvariant();
        if (!ExportCodes.DateFormats.Contains(dateFormat)) throw new ValidationException("dateFormat", ExportRules.DateFormatInvalid);
        var scope = current?.Scope ?? RequireScope(req.Scope);
        IReadOnlyList<ExportColumn> columns = req.Columns is null
            ? (current is null ? ExportRules.DefaultColumns(scope) : ExportRules.ParseColumns(current.ColumnsJson))
            : req.Columns.Select(c => new ExportColumn(c.Field?.Trim().ToUpperInvariant() ?? string.Empty, string.IsNullOrWhiteSpace(c.Header) ? null : c.Header.Trim())).ToList();
        if (columns.Count == 0) throw new ValidationException("columns", ExportRules.ColumnsRequired);
        if (columns.Count > ExportRules.MaxColumns) throw new ValidationException("columns", ExportRules.TooManyColumns);
        var known = ExportFields.Of(scope).Select(f => f.Code).ToHashSet();
        if (columns.Any(c => !known.Contains(c.Field))) throw new ValidationException("columns", ExportRules.FieldInvalid);
        if (columns.Select(c => c.Field).Distinct().Count() != columns.Count) throw new ValidationException("columns", ExportRules.FieldRepeated);
        if (columns.Any(c => c.Header is { Length: > ExportRules.MaxHeaderLength })) throw new ValidationException("columns", ExportRules.HeaderTooLong);
        return new Valid(name, format, delimiter, dateFormat, req.IncludeHeader ?? current?.IncludeHeader ?? true, columns);
    }

    public static ExportTemplateDto ToDto(ExportTemplate t)
        => new(t.PublicId, t.Scope, t.Name, t.Format, t.Delimiter, t.DateFormat, t.IncludeHeader, ExportRules.ParseColumns(t.ColumnsJson).Select(c => new ExportColumnDto(c.Field, c.Header)).ToList());
}
