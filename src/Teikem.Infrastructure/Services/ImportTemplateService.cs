using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 3 (P9): plantillas de importación por posición de columna (como CTPL-1 de consignatarios), reutilizables.
/// - Una plantilla es del tenant (ClientId NULL = general) o de un cliente; se valida con ImportMapping.ValidateColumns
///   (posiciones únicas &gt;= 1, campos del catálogo, consignatario por nombre o código, paquete por columna o defaults).
/// - Nombre único por (tenant, kind) (UQ_ImportTemplate → 409). Baja lógica IsActive=0, nunca DELETE.
/// - ColumnsJson = [{position, field}], DefaultsJson = {campo: valor} (camelCase). Los helpers de (de)serialización
///   los comparte OrderImportService.
/// </summary>
public sealed class ImportTemplateService(TeikemDbContext db, ITenantContext tenant)
{
    public const int NameMaxLength = 120;
    public const string NameTakenMessage = "Ya existe una plantilla de importación con ese nombre.";
    public const string NotFoundLabel = "Plantilla de importación";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ---------------------------------------------------------------- consulta

    /// <summary>Plantillas del tenant por tipo (ORDER por defecto). Con clientPublicId: las generales y las de ese cliente.</summary>
    public async Task<IReadOnlyList<ImportTemplateDto>> GetListAsync(string? kind, Guid? clientPublicId, bool includeInactive, CancellationToken ct)
    {
        var k = NormalizeKind(kind);
        var q = db.ImportTemplates.AsNoTracking().Where(t => t.Kind == k);
        if (!includeInactive) q = q.Where(t => t.IsActive);
        if (clientPublicId is Guid cpid)
        {
            var clientId = (await db.ResolveClientAsync(cpid, ct)).ClientId;
            q = q.Where(t => t.ClientId == null || t.ClientId == clientId);
        }
        var rows = await q.OrderBy(t => t.Name).ThenBy(t => t.ImportTemplateId).ToListAsync(ct);
        var clients = await ClientNamesAsync(rows.Where(r => r.ClientId.HasValue).Select(r => r.ClientId!.Value), ct);
        return rows.Select(r => ToDto(r, clients)).ToList();
    }

    public async Task<ImportTemplateDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.ImportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.PublicId == publicId, ct)
                  ?? throw new NotFoundException(NotFoundLabel);
        var clients = await ClientNamesAsync(row.ClientId.HasValue ? new[] { row.ClientId.Value } : Array.Empty<int>(), ct);
        return ToDto(row, clients);
    }

    /// <summary>
    /// Plantilla utilizable para importar al cliente dado: activa, del tipo pedido y general o de ese mismo cliente.
    /// Una plantilla de otro cliente responde 404 (sin oráculo).
    /// </summary>
    public async Task<ImportTemplate> ResolveForImportAsync(Guid templatePublicId, int clientId, string kind, CancellationToken ct)
    {
        var k = NormalizeKind(kind);
        var row = await db.ImportTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.PublicId == templatePublicId && t.Kind == k && (t.ClientId == null || t.ClientId == clientId), ct)
            ?? throw new NotFoundException(NotFoundLabel);
        if (!row.IsActive) throw new ValidationException("templatePublicId", "La plantilla está inactiva; reactívela o elija otra.");
        return row;
    }

    // ---------------------------------------------------------------- alta / edición

    public async Task<ImportTemplateDto> CreateAsync(ImportTemplateCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var name = RequireName(req.Name);
        var kind = NormalizeKind(req.Kind);
        var delimiter = NormalizeDelimiter(req.Delimiter);
        var columns = ToColumns(req.Columns);
        var defaults = ToDefaults(req.Defaults);
        ThrowIfErrors(ImportMapping.ValidateColumns(columns, defaults));

        int? clientId = null;
        if (req.ClientPublicId is Guid cpid) clientId = (await db.ResolveClientAsync(cpid, ct)).ClientId;

        var row = new ImportTemplate
        {
            TenantId = tenantId,
            Kind = kind,
            Name = name,
            ClientId = clientId,
            Delimiter = delimiter.ToString(),
            HasHeader = req.HasHeader,
            ColumnsJson = SerializeColumns(columns),
            DefaultsJson = defaults.Count == 0 ? null : SerializeDefaults(defaults),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.ImportTemplates.Add(row);
        await db.SaveGuardedAsync(NameTakenMessage, ct);
        return await GetAsync(row.PublicId, ct);
    }

    public async Task<ImportTemplateDto> UpdateAsync(Guid publicId, ImportTemplatePatchRequest req, CancellationToken ct)
    {
        if (req.MakeGeneral == true && req.ClientPublicId.HasValue)
            throw new ValidationException("clientPublicId", "Indique un cliente o marque la plantilla como general, no ambos.");
        if (req.ClearDefaults == true && req.Defaults is { Count: > 0 })
            throw new ValidationException("defaults", "Indique defaults nuevos o clearDefaults, no ambos.");

        int? newClientId = null;
        if (req.ClientPublicId is Guid cpid) newClientId = (await db.ResolveClientAsync(cpid, ct)).ClientId;

        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await db.ImportTemplates.FirstOrDefaultAsync(t => t.PublicId == publicId, ct2)
                      ?? throw new NotFoundException(NotFoundLabel);

            var columns = req.Columns is null ? ParseColumns(row.ColumnsJson) : ToColumns(req.Columns);
            var defaults = req.ClearDefaults == true
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : req.Defaults is null ? ParseDefaults(row.DefaultsJson) : ToDefaults(req.Defaults);
            ThrowIfErrors(ImportMapping.ValidateColumns(columns, defaults));

            if (req.Name is not null) row.Name = RequireName(req.Name);
            if (req.Delimiter is not null) row.Delimiter = NormalizeDelimiter(req.Delimiter).ToString();
            if (req.HasHeader is bool hasHeader) row.HasHeader = hasHeader;
            if (req.MakeGeneral == true) row.ClientId = null;
            else if (newClientId is int c) row.ClientId = c;
            if (req.Columns is not null) row.ColumnsJson = SerializeColumns(columns);
            if (req.ClearDefaults == true || req.Defaults is not null) row.DefaultsJson = defaults.Count == 0 ? null : SerializeDefaults(defaults);
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveGuardedAsync(NameTakenMessage, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Baja lógica (nunca DELETE): deja de aparecer en el selector; los lotes que la usaron conservan la referencia.</summary>
    public async Task<ImportTemplateDto> DeactivateAsync(Guid publicId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await db.ImportTemplates.FirstOrDefaultAsync(t => t.PublicId == publicId, ct2) ?? throw new NotFoundException(NotFoundLabel);
            if (!row.IsActive) throw new ConflictException("La plantilla ya está inactiva.");
            row.IsActive = false;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    public async Task<ImportTemplateDto> ReactivateAsync(Guid publicId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await db.ImportTemplates.FirstOrDefaultAsync(t => t.PublicId == publicId, ct2) ?? throw new NotFoundException(NotFoundLabel);
            if (row.IsActive) throw new ConflictException("La plantilla ya está activa.");
            row.IsActive = true;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- JSON de columnas y defaults (compartido con el importador)

    public static IReadOnlyList<ImportColumn> ParseColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ImportColumn>();
        var items = JsonSerializer.Deserialize<List<ImportTemplateColumnDto>>(json, JsonOptions) ?? new List<ImportTemplateColumnDto>();
        return items.Select(i => new ImportColumn(i.Position, i.Field)).ToList();
    }

    public static IReadOnlyDictionary<string, string> ParseDefaults(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.Ordinal);
        var items = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions) ?? new Dictionary<string, string?>();
        return items.Where(i => i.Value is not null).ToDictionary(i => i.Key, i => i.Value!, StringComparer.Ordinal);
    }

    public static string SerializeColumns(IReadOnlyList<ImportColumn> columns)
        => JsonSerializer.Serialize(columns.OrderBy(c => c.Position).Select(c => new ImportTemplateColumnDto(c.Position, ImportFields.Canonical(c.Field) ?? c.Field)).ToList(), JsonOptions);

    public static string SerializeDefaults(IReadOnlyDictionary<string, string> defaults)
        => JsonSerializer.Serialize(ImportMapping.NormalizeDefaults(defaults), JsonOptions);

    /// <summary>Delimitador efectivo de la plantilla (un carácter; ',' si la fila no lo trae).</summary>
    public static char DelimiterOf(ImportTemplate template)
    {
        var d = Convert.ToString(template.Delimiter);
        return string.IsNullOrEmpty(d) ? CsvParser.DefaultDelimiter : d[0];
    }

    // ---------------------------------------------------------------- helpers

    private static string NormalizeKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return ImportKinds.Order;
        var k = kind.Trim().ToUpperInvariant();
        if (k != ImportKinds.Order) throw new ValidationException("kind", $"Tipo de plantilla desconocido: {k}.");
        return k;
    }

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException("name", "El nombre de la plantilla es obligatorio.");
        var n = name.Trim();
        if (n.Length > NameMaxLength) throw new ValidationException("name", $"Máximo {NameMaxLength} caracteres.");
        return n;
    }

    private static char NormalizeDelimiter(string? delimiter)
    {
        if (string.IsNullOrEmpty(delimiter)) return CsvParser.DefaultDelimiter;
        var d = delimiter switch { "\\t" or "tab" or "TAB" => "\t", _ => delimiter };
        if (d.Length != 1 || d[0] is '"' or '\r' or '\n')
            throw new ValidationException("delimiter", "El delimitador debe ser un solo carácter (p. ej. ',' o ';'), distinto de comilla y salto de línea.");
        return d[0];
    }

    private static List<ImportColumn> ToColumns(IList<ImportTemplateColumnDto>? columns)
        => columns is null ? new List<ImportColumn>() : columns.Select(c => new ImportColumn(c.Position, c.Field ?? string.Empty)).ToList();

    private static Dictionary<string, string> ToDefaults(IDictionary<string, string>? defaults)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (defaults is null) return result;
        foreach (var (key, value) in defaults)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;
            result[key.Trim()] = value.Trim();
        }
        return result;
    }

    private static void ThrowIfErrors(IReadOnlyDictionary<string, string> errors)
    {
        if (errors.Count == 0) return;
        throw new ValidationException(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
    }

    private async Task<Dictionary<int, (Guid PublicId, string Name)>> ClientNamesAsync(IEnumerable<int> clientIds, CancellationToken ct)
    {
        var ids = clientIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, (Guid, string)>();
        return await db.Clients.AsNoTracking().Where(c => ids.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.PublicId, c.Name })
            .ToDictionaryAsync(c => c.ClientId, c => (c.PublicId, c.Name), ct);
    }

    private static ImportTemplateDto ToDto(ImportTemplate t, IReadOnlyDictionary<int, (Guid PublicId, string Name)> clients)
    {
        (Guid PublicId, string Name)? client = t.ClientId is int cid && clients.TryGetValue(cid, out var c) ? c : null;
        var defaults = ParseDefaults(t.DefaultsJson);
        return new ImportTemplateDto(
            t.ImportTemplateId, t.PublicId, t.Kind, t.Name, client?.PublicId, client?.Name,
            DelimiterOf(t).ToString(), t.HasHeader,
            ParseColumns(t.ColumnsJson).OrderBy(c => c.Position).Select(c => new ImportTemplateColumnDto(c.Position, c.Field)).ToList(),
            defaults.Count == 0 ? null : defaults,
            t.IsActive, t.CreatedAtUtc, t.UpdatedAtUtc);
    }
}
