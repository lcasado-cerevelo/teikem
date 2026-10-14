using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
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
/// Lote 38 (F-B): el mismo servicio atiende las plantillas de consignatarios (Kind CONSIGNEE). Cada controlador pasa el tipo
/// que atiende (expectedKind): una plantilla de otro tipo responde 404 (sin oráculo) y un kind distinto en el cuerpo o la
/// consulta responde 400 'Tipo de plantilla desconocido'. La validación y la normalización de columnas y defaults siguen el
/// catálogo del tipo (ImportMapping para ORDER, ConsigneeImportMapping para CONSIGNEE).
/// Lote 45 (F-B2): las columnas de CONSIGNEE pueden ser «No usar» (ignore), unir varias columnas en un campo (Part/Separator)
/// y la plantilla guarda un cliente por defecto (DefaultClientId: activo y, si la plantilla es de un cliente, ese mismo).
/// Lote 46 (F-B3): una plantilla CONSIGNEE puede tener VARIANTES de mapeo (VariantsJson) con condición por fila; ColumnsJson queda
/// con las columnas de la primera. Sin variantes (NULL) todo sigue igual. Los defaults son comunes a todas las variantes.
/// </summary>
public sealed class ImportTemplateService(TeikemDbContext db, ITenantContext tenant)
{
    public const int NameMaxLength = 120;
    public const string NameTakenMessage = "Ya existe una plantilla de importación con ese nombre.";
    public const string NotFoundLabel = "Plantilla de importación";
    public const string DefaultClientMismatchMessage = "El cliente por defecto debe ser el mismo cliente de la plantilla.";
    public const string DefaultClientBothMessage = "Indique un cliente por defecto o quítelo, no ambos.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ---------------------------------------------------------------- consulta

    /// <summary>
    /// Plantillas del tenant del tipo que atiende el llamador (expectedKind; kind de la consulta, si viene, debe coincidir:
    /// si no, 400). Con clientPublicId: las generales y las de ese cliente.
    /// </summary>
    public async Task<IReadOnlyList<ImportTemplateDto>> GetListAsync(string expectedKind, string? kind, Guid? clientPublicId, bool includeInactive, CancellationToken ct)
    {
        var k = NormalizeKind(kind, expectedKind);
        var q = db.ImportTemplates.AsNoTracking().Where(t => t.Kind == k);
        if (!includeInactive) q = q.Where(t => t.IsActive);
        if (clientPublicId is Guid cpid)
        {
            var clientId = (await db.ResolveClientAsync(cpid, ct)).ClientId;
            q = q.Where(t => t.ClientId == null || t.ClientId == clientId);
        }
        var rows = await q.OrderBy(t => t.Name).ThenBy(t => t.ImportTemplateId).ToListAsync(ct);
        var clients = await ClientNamesAsync(rows.SelectMany(ClientIdsOf), ct);
        return rows.Select(r => ToDto(r, clients)).ToList();
    }

    /// <summary>Plantilla del tipo esperado; una de otro tipo responde 404 (la ruta de órdenes no ve las de consignatarios y viceversa).</summary>
    public async Task<ImportTemplateDto> GetAsync(Guid publicId, string expectedKind, CancellationToken ct)
    {
        var k = NormalizeKind(expectedKind, expectedKind);
        var row = await db.ImportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.PublicId == publicId && t.Kind == k, ct)
                  ?? throw new NotFoundException(NotFoundLabel);
        var clients = await ClientNamesAsync(ClientIdsOf(row), ct);
        return ToDto(row, clients);
    }

    /// <summary>
    /// Plantilla utilizable para importar al cliente dado: activa, del tipo pedido y general o de ese mismo cliente.
    /// Sin cliente (clientId null, lote CONSIGNEE sin cliente) solo sirven las generales. Una plantilla de otro cliente o de
    /// otro tipo responde 404 (sin oráculo).
    /// </summary>
    public async Task<ImportTemplate> ResolveForImportAsync(Guid templatePublicId, int? clientId, string kind, CancellationToken ct)
    {
        var k = NormalizeKind(kind, kind);
        var row = await db.ImportTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.PublicId == templatePublicId && t.Kind == k && (t.ClientId == null || t.ClientId == clientId), ct)
            ?? throw new NotFoundException(NotFoundLabel);
        if (!row.IsActive) throw new ValidationException("templatePublicId", "La plantilla está inactiva; reactívela o elija otra.");
        return row;
    }

    // ---------------------------------------------------------------- alta / edición

    public async Task<ImportTemplateDto> CreateAsync(ImportTemplateCreateRequest req, string expectedKind, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var name = RequireName(req.Name);
        var kind = NormalizeKind(req.Kind, expectedKind);
        var delimiter = NormalizeDelimiter(req.Delimiter);
        var defaults = ToDefaults(req.Defaults);
        var variants = ToVariants(req.Variants);
        if (variants.Count > 0 && !IsConsignee(kind)) throw new ValidationException("variants", ConsigneeImportVariants.OnlyConsigneeMessage);
        if (variants.Count > 0 && req.Columns is { Count: > 0 }) throw new ValidationException("columns", ConsigneeImportVariants.ColumnsInsideVariantsMessage);
        var columns = variants.Count > 0 ? variants[0].Columns : ToColumns(req.Columns);
        ThrowIfErrors(variants.Count > 0 ? ValidateVariants(variants, defaults) : ValidateColumns(kind, columns, defaults));

        int? clientId = null;
        if (req.ClientPublicId is Guid cpid) clientId = (await db.ResolveClientAsync(cpid, ct)).ClientId;
        var defaultClientId = await ResolveDefaultClientAsync(req.DefaultClientPublicId, ct);
        EnsureDefaultClientMatches(clientId, defaultClientId);

        var row = new ImportTemplate
        {
            TenantId = tenantId,
            Kind = kind,
            Name = name,
            ClientId = clientId,
            DefaultClientId = defaultClientId,
            Delimiter = delimiter.ToString(),
            HasHeader = req.HasHeader,
            ColumnsJson = SerializeColumns(columns, kind),
            DefaultsJson = defaults.Count == 0 ? null : SerializeDefaults(defaults, kind),
            VariantsJson = variants.Count > 0 ? SerializeVariants(variants) : null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.ImportTemplates.Add(row);
        await db.SaveGuardedAsync(NameTakenMessage, ct);
        return await GetAsync(row.PublicId, kind, ct);
    }

    public async Task<ImportTemplateDto> UpdateAsync(Guid publicId, ImportTemplatePatchRequest req, string expectedKind, CancellationToken ct)
    {
        var kind = NormalizeKind(expectedKind, expectedKind);
        if (req.MakeGeneral == true && req.ClientPublicId.HasValue)
            throw new ValidationException("clientPublicId", "Indique un cliente o marque la plantilla como general, no ambos.");
        if (req.ClearDefaults == true && req.Defaults is { Count: > 0 })
            throw new ValidationException("defaults", "Indique defaults nuevos o clearDefaults, no ambos.");
        if (req.ClearDefaultClient == true && req.DefaultClientPublicId.HasValue)
            throw new ValidationException("defaultClientPublicId", DefaultClientBothMessage);
        if (req.Variants is { Count: > 0 } && !IsConsignee(kind)) throw new ValidationException("variants", ConsigneeImportVariants.OnlyConsigneeMessage);
        if (req.Variants is { Count: > 0 } && req.Columns is not null) throw new ValidationException("columns", ConsigneeImportVariants.ColumnsInsideVariantsMessage);

        int? newClientId = null;
        if (req.ClientPublicId is Guid cpid) newClientId = (await db.ResolveClientAsync(cpid, ct)).ClientId;
        var newDefaultClientId = await ResolveDefaultClientAsync(req.DefaultClientPublicId, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await db.ImportTemplates.FirstOrDefaultAsync(t => t.PublicId == publicId && t.Kind == kind, ct2)
                      ?? throw new NotFoundException(NotFoundLabel);

            // Variantes (Lote 46): null = las que tenga; [] = vuelve a una sola variante; lista = las reemplaza.
            var current = ParseVariants(row.VariantsJson);
            var variants = req.Variants is null ? current : ToVariants(req.Variants);
            if (req.Variants is null && current.Count > 0 && req.Columns is not null)
                throw new ValidationException("columns", ConsigneeImportVariants.ColumnsInsideVariantsMessage);
            var columns = variants.Count > 0
                ? variants[0].Columns
                : req.Columns is not null ? ToColumns(req.Columns)
                : current.Count > 0 ? current[0].Columns
                : ParseColumns(row.ColumnsJson);
            var defaults = req.ClearDefaults == true
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : req.Defaults is null ? ParseDefaults(row.DefaultsJson) : ToDefaults(req.Defaults);
            ThrowIfErrors(variants.Count > 0 ? ValidateVariants(variants, defaults) : ValidateColumns(kind, columns, defaults));

            if (req.Name is not null) row.Name = RequireName(req.Name);
            if (req.Delimiter is not null) row.Delimiter = NormalizeDelimiter(req.Delimiter).ToString();
            if (req.HasHeader is bool hasHeader) row.HasHeader = hasHeader;
            if (req.MakeGeneral == true) row.ClientId = null;
            else if (newClientId is int c) row.ClientId = c;
            if (req.ClearDefaultClient == true) row.DefaultClientId = null;
            else if (newDefaultClientId is int dc) row.DefaultClientId = dc;
            EnsureDefaultClientMatches(row.ClientId, row.DefaultClientId);
            if (req.Columns is not null || req.Variants is not null) row.ColumnsJson = SerializeColumns(columns, kind);
            if (req.Variants is not null) row.VariantsJson = variants.Count > 0 ? SerializeVariants(variants) : null;
            if (req.ClearDefaults == true || req.Defaults is not null) row.DefaultsJson = defaults.Count == 0 ? null : SerializeDefaults(defaults, kind);
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveGuardedAsync(NameTakenMessage, ct2);
        }, ct);
        return await GetAsync(publicId, kind, ct);
    }

    /// <summary>Baja lógica (nunca DELETE): deja de aparecer en el selector; los lotes que la usaron conservan la referencia.</summary>
    public async Task<ImportTemplateDto> DeactivateAsync(Guid publicId, string expectedKind, CancellationToken ct)
    {
        var kind = NormalizeKind(expectedKind, expectedKind);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await db.ImportTemplates.FirstOrDefaultAsync(t => t.PublicId == publicId && t.Kind == kind, ct2) ?? throw new NotFoundException(NotFoundLabel);
            if (!row.IsActive) throw new ConflictException("La plantilla ya está inactiva.");
            row.IsActive = false;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct2);
        }, ct);
        return await GetAsync(publicId, kind, ct);
    }

    public async Task<ImportTemplateDto> ReactivateAsync(Guid publicId, string expectedKind, CancellationToken ct)
    {
        var kind = NormalizeKind(expectedKind, expectedKind);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await db.ImportTemplates.FirstOrDefaultAsync(t => t.PublicId == publicId && t.Kind == kind, ct2) ?? throw new NotFoundException(NotFoundLabel);
            if (row.IsActive) throw new ConflictException("La plantilla ya está activa.");
            row.IsActive = true;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct2);
        }, ct);
        return await GetAsync(publicId, kind, ct);
    }

    // ---------------------------------------------------------------- JSON de columnas y defaults (compartido con el importador)

    public static IReadOnlyList<ImportColumn> ParseColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ImportColumn>();
        var items = JsonSerializer.Deserialize<List<ImportTemplateColumnDto>>(json, JsonOptions) ?? new List<ImportTemplateColumnDto>();
        return items.Select(i => new ImportColumn(i.Position, i.Field, i.Part, i.Separator)).ToList();
    }

    /// <summary>Lote 46 (F-B3): variantes guardadas (vacío si la plantilla no tiene: una sola variante, ColumnsJson).</summary>
    public static IReadOnlyList<ImportVariant> ParseVariants(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ImportVariant>();
        var items = JsonSerializer.Deserialize<List<ImportTemplateVariantDto>>(json, JsonOptions) ?? new List<ImportTemplateVariantDto>();
        return items.Select(ToVariant).ToList();
    }

    /// <summary>Lote 46 (F-B3): variantes normalizadas (ConsigneeImportVariants.Normalize) en JSON camelCase.</summary>
    public static string SerializeVariants(IReadOnlyList<ImportVariant> variants)
        => JsonSerializer.Serialize(variants.Select(ConsigneeImportVariants.Normalize).Select(ToVariantDto).ToList(), JsonOptions);

    private static ImportVariant ToVariant(ImportTemplateVariantDto v)
        => new(v.Name,
            (v.Conditions ?? Array.Empty<ImportVariantConditionDto>()).Select(c => new ImportVariantCondition(c.Position, c.Operator ?? string.Empty, c.Value)).ToList(),
            (v.Columns ?? Array.Empty<ImportTemplateColumnDto>()).Select(c => new ImportColumn(c.Position, c.Field ?? string.Empty, c.Part, c.Separator)).ToList());

    private static ImportTemplateVariantDto ToVariantDto(ImportVariant v)
        => new(v.Name,
            v.Conditions.Select(c => new ImportVariantConditionDto(c.Position, c.Operator, c.Value)).ToList(),
            v.Columns.OrderBy(c => c.Position).ThenBy(c => c.Part ?? 0).Select(c => new ImportTemplateColumnDto(c.Position, c.Field, c.Part, c.Separator)).ToList());

    private static List<ImportVariant> ToVariants(IList<ImportTemplateVariantDto>? variants)
        => variants is null ? new List<ImportVariant>() : variants.Where(v => v is not null).Select(ToVariant).ToList();

    public static IReadOnlyDictionary<string, string> ParseDefaults(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.Ordinal);
        var items = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions) ?? new Dictionary<string, string?>();
        return items.Where(i => i.Value is not null).ToDictionary(i => i.Key, i => i.Value!, StringComparer.Ordinal);
    }

    /// <summary>
    /// Columnas ordenadas por posición con el nombre canónico del catálogo del tipo (ORDER por defecto). Lote 45: CONSIGNEE se
    /// normaliza con ConsigneeImportMapping.NormalizeColumns (conserva «No usar», partes y separadores); ORDER no los usa.
    /// </summary>
    public static string SerializeColumns(IReadOnlyList<ImportColumn> columns, string kind = ImportKinds.Order)
        => IsConsignee(kind)
            ? JsonSerializer.Serialize(ConsigneeImportMapping.NormalizeColumns(columns).Select(c => new ImportTemplateColumnDto(c.Position, c.Field, c.Part, c.Separator)).ToList(), JsonOptions)
            : JsonSerializer.Serialize(columns.OrderBy(c => c.Position).Select(c => new ImportTemplateColumnDto(c.Position, CanonicalField(kind, c.Field) ?? c.Field)).ToList(), JsonOptions);

    /// <summary>
    /// Defaults normalizados con el catálogo del tipo (ORDER por defecto). Lote 38: con CONSIGNEE se conservan sus campos
    /// (allowDupInvoice, defaultServiceMinutes, ventana, notas…), que el catálogo de órdenes descartaría.
    /// </summary>
    public static string SerializeDefaults(IReadOnlyDictionary<string, string> defaults, string kind = ImportKinds.Order)
        => JsonSerializer.Serialize(IsConsignee(kind) ? ConsigneeImportMapping.NormalizeDefaults(defaults) : ImportMapping.NormalizeDefaults(defaults), JsonOptions);

    /// <summary>
    /// Lote 40 (F-C3): catálogo de campos que una plantilla de ÓRDENES puede mapear, para el editor de plantillas (equivale a
    /// GET /locations/import/fields). Ningún campo es obligatorio por sí solo: las reglas son de grupo (consigneeName o
    /// consigneeCode; packageType o pieces, en columna o en defaults) y las valida ValidateColumns al guardar.
    /// </summary>
    public static IReadOnlyList<ImportFieldDto> GetOrderFields()
        => ImportFields.All.Select(f => new ImportFieldDto(f, Required: false, DefaultAllowed: !ImportFields.NoDefault.Contains(f))).ToList();

    /// <summary>Delimitador efectivo de la plantilla (un carácter; ',' si la fila no lo trae).</summary>
    public static char DelimiterOf(ImportTemplate template)
    {
        var d = Convert.ToString(template.Delimiter);
        return string.IsNullOrEmpty(d) ? CsvParser.DefaultDelimiter : d[0];
    }

    // ---------------------------------------------------------------- helpers

    public static string UnknownKindMessage(string kind) => $"Tipo de plantilla desconocido: {kind}.";

    /// <summary>
    /// Tipo efectivo: vacío → el que atiende el llamador; distinto del esperado → 400 'Tipo de plantilla desconocido: X.'
    /// (la ruta de órdenes no acepta CONSIGNEE ni la de consignatarios ORDER).
    /// </summary>
    private static string NormalizeKind(string? kind, string expectedKind)
    {
        var expected = expectedKind.Trim().ToUpperInvariant();
        if (expected is not (ImportKinds.Order or ImportKinds.Consignee)) throw new ValidationException("kind", UnknownKindMessage(expected));
        if (string.IsNullOrWhiteSpace(kind)) return expected;
        var k = kind.Trim().ToUpperInvariant();
        if (k != expected) throw new ValidationException("kind", UnknownKindMessage(k));
        return k;
    }

    private static bool IsConsignee(string? kind) => string.Equals(kind?.Trim(), ImportKinds.Consignee, StringComparison.OrdinalIgnoreCase);

    private static string? CanonicalField(string kind, string? field)
        => IsConsignee(kind) ? ConsigneeImportFields.Canonical(field) : ImportFields.Canonical(field);

    /// <summary>
    /// Validación de la definición según el catálogo del tipo. CONSIGNEE: además, el teléfono por defecto con la misma regla
    /// que la fila (ContactPointService, 'defaults.contactPhone').
    /// </summary>
    public static IReadOnlyDictionary<string, string> ValidateColumns(string kind, IReadOnlyList<ImportColumn> columns, IReadOnlyDictionary<string, string> defaults)
    {
        if (!IsConsignee(kind)) return ImportMapping.ValidateColumns(columns, defaults);
        var errors = new Dictionary<string, string>(ConsigneeImportMapping.ValidateColumns(columns, defaults), StringComparer.Ordinal);
        foreach (var (key, value) in defaults)
        {
            if (ConsigneeImportFields.Canonical(key) != ConsigneeImportFields.ContactPhone || string.IsNullOrWhiteSpace(value)) continue;
            try { ContactPointService.ValidateValue("PHONE", value); }
            catch (ValidationException ex) { errors[$"defaults.{key}"] = ex.Message; }
        }
        return errors;
    }

    /// <summary>
    /// Lote 46 (F-B3): validación de las variantes (ConsigneeImportVariants.Validate: condiciones, orden y el mapeo de cada una) y el
    /// teléfono por defecto con la misma regla que la fila.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ValidateVariants(IReadOnlyList<ImportVariant> variants, IReadOnlyDictionary<string, string> defaults)
    {
        var errors = new Dictionary<string, string>(ConsigneeImportVariants.Validate(variants, defaults), StringComparer.Ordinal);
        foreach (var (key, value) in ValidateColumns(ImportKinds.Consignee, Array.Empty<ImportColumn>(), defaults))
            if (key.StartsWith("defaults.", StringComparison.Ordinal)) errors.TryAdd(key, value);
        return errors;
    }

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException("name", "El nombre de la plantilla es obligatorio.");
        var n = name.Trim();
        if (n.Length > NameMaxLength) throw new ValidationException("name", $"Máximo {NameMaxLength} caracteres.");
        return n;
    }

    /// <summary>Delimitador de un carácter (',' si viene vacío; 'tab' o '\t' = tabulador); otro valor → 400 'delimiter'.</summary>
    public static char NormalizeDelimiter(string? delimiter)
    {
        if (string.IsNullOrEmpty(delimiter)) return CsvParser.DefaultDelimiter;
        var d = delimiter switch { "\\t" or "tab" or "TAB" => "\t", _ => delimiter };
        if (d.Length != 1 || d[0] is '"' or '\r' or '\n')
            throw new ValidationException("delimiter", "El delimitador debe ser un solo carácter (p. ej. ',' o ';'), distinto de comilla y salto de línea.");
        return d[0];
    }

    private static List<ImportColumn> ToColumns(IList<ImportTemplateColumnDto>? columns)
        => columns is null ? new List<ImportColumn>() : columns.Select(c => new ImportColumn(c.Position, c.Field ?? string.Empty, c.Part, c.Separator)).ToList();

    /// <summary>Cliente por defecto pedido (404 si no es del tenant, 409 si está de baja) o null.</summary>
    private async Task<int?> ResolveDefaultClientAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is not Guid id || id == Guid.Empty) return null;
        return ClientQueries.EnsureClientActive(await db.ResolveClientAsync(id, ct)).ClientId;
    }

    /// <summary>Una plantilla de un cliente solo sirve para ese cliente: su cliente por defecto, si lo tiene, es ese mismo.</summary>
    private static void EnsureDefaultClientMatches(int? templateClientId, int? defaultClientId)
    {
        if (templateClientId is int c && defaultClientId is int d && c != d)
            throw new ValidationException("defaultClientPublicId", DefaultClientMismatchMessage);
    }

    /// <summary>
    /// Lote 45 (F-B2): la importación confirmada recuerda en la plantilla el cliente con que se importó (null = compartidos), para
    /// proponerlo la próxima vez. No hace nada si ya es ese o si la plantilla es de otro cliente.
    /// </summary>
    public async Task RememberDefaultClientAsync(int importTemplateId, int? clientId, CancellationToken ct)
    {
        var row = await db.ImportTemplates.FirstOrDefaultAsync(t => t.ImportTemplateId == importTemplateId, ct);
        if (row is null || row.DefaultClientId == clientId) return;
        if (row.ClientId is int c && clientId is int d && c != d) return;
        row.DefaultClientId = clientId;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<int> ClientIdsOf(ImportTemplate t)
    {
        if (t.ClientId is int c) yield return c;
        if (t.DefaultClientId is int d) yield return d;
    }

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
        (Guid PublicId, string Name)? defaultClient = t.DefaultClientId is int did && clients.TryGetValue(did, out var dc) ? dc : null;
        var defaults = ParseDefaults(t.DefaultsJson);
        return new ImportTemplateDto(
            t.ImportTemplateId, t.PublicId, t.Kind, t.Name, client?.PublicId, client?.Name,
            DelimiterOf(t).ToString(), t.HasHeader,
            ParseColumns(t.ColumnsJson).OrderBy(c => c.Position).ThenBy(c => c.Part ?? 0)
                .Select(c => new ImportTemplateColumnDto(c.Position, c.Field, c.Part, c.Separator)).ToList(),
            defaults.Count == 0 ? null : defaults,
            t.IsActive, t.CreatedAtUtc, t.UpdatedAtUtc,
            defaultClient?.PublicId, defaultClient?.Name,
            string.IsNullOrWhiteSpace(t.VariantsJson) ? null : ParseVariants(t.VariantsJson).Select(ToVariantDto).ToList());
    }
}
