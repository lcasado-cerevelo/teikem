using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.CustomFields;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Dsl;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Capa F: campos personalizados por tenant + entidad. Definiciones (con opciones o lista de catálogo), y valores EAV tipados
/// por registro con validación en servicio: requerido, único por entidad, tipo, DSL (regex/min/max/longitud), opciones válidas.
/// </summary>
public sealed class CustomFieldService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, IEnumerable<IOwnedEntityResolver> resolvers, PermissionService permissions)
{
    private static readonly Regex KeyRegex = new("^[a-z][a-z0-9_]{1,59}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<CustomFieldDefinitionDto>> GetDefinitionsAsync(string? entityType, bool includeInactive, CancellationToken ct)
    {
        var q = db.CustomFieldDefinitions.AsNoTracking().Include(d => d.EntityType).Include(d => d.DataType).Include(d => d.Options).AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType)) { var id = await lookups.GetIdAsync(LookupDomains.EntityType, entityType, ct); q = q.Where(d => d.EntityTypeLookupId == id); }
        if (!includeInactive) q = q.Where(d => d.IsActive);
        var list = await q.OrderBy(d => d.EntityTypeLookupId).ThenBy(d => d.SortOrder).ThenBy(d => d.FieldKey).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<CustomFieldDefinitionDto> GetDefinitionAsync(int id, CancellationToken ct)
        => ToDto(await LoadDefinitionAsync(id, ct));

    public async Task<CustomFieldDefinitionDto> CreateDefinitionAsync(string entityType, CustomFieldDefinitionUpsert req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityType, ct);
        var key = (req.FieldKey ?? "").Trim().ToLowerInvariant();
        if (!KeyRegex.IsMatch(key)) throw new ValidationException("fieldKey", "Clave inválida: minúsculas, dígitos y '_' (2-60), empezando por letra.");
        if (await db.CustomFieldDefinitions.AnyAsync(d => d.EntityTypeLookupId == entityTypeId && d.FieldKey == key, ct))
            throw new ConflictException($"Ya existe el campo '{key}' en {entityType}.");
        var dataTypeId = await lookups.GetIdAsync(LookupDomains.CustomFieldDataType, req.DataType, ct);
        await ValidateSpecAsync(req, ct);

        var def = new CustomFieldDefinition
        {
            TenantId = tenantId, EntityTypeLookupId = entityTypeId, FieldKey = key, DataTypeLookupId = dataTypeId,
            LabelJson = MultilingualText.Serialize(req.Labels), DescriptionJson = req.Descriptions is { Count: > 0 } ? MultilingualText.Serialize(req.Descriptions) : null,
            IsRequired = req.IsRequired, IsUnique = req.IsUnique, DefaultValue = req.DefaultValue, ValidationJson = req.ValidationJson,
            RefEntity = req.RefEntity, ShowInList = req.ShowInList, SortOrder = req.SortOrder ?? 100,
        };
        ApplyOptions(def, req.Options);
        db.CustomFieldDefinitions.Add(def);
        await db.SaveChangesAsync(ct);
        return ToDto(await LoadDefinitionAsync(def.CustomFieldDefinitionId, ct));
    }

    public async Task<CustomFieldDefinitionDto> UpdateDefinitionAsync(int id, CustomFieldDefinitionUpsert req, CancellationToken ct)
    {
        var def = await LoadDefinitionAsync(id, ct, track: true);
        await ValidateSpecAsync(req, ct);
        var dataTypeId = await lookups.GetIdAsync(LookupDomains.CustomFieldDataType, req.DataType, ct);
        if (dataTypeId != def.DataTypeLookupId && await db.CustomFieldValues.AnyAsync(v => v.CustomFieldDefinitionId == id, ct))
            throw new ConflictException("No se puede cambiar el tipo de un campo que ya tiene valores guardados.");
        def.DataTypeLookupId = dataTypeId;
        if (req.Labels is { Count: > 0 }) def.LabelJson = MultilingualText.Serialize(req.Labels);
        if (req.Descriptions is not null) def.DescriptionJson = req.Descriptions.Count == 0 ? null : MultilingualText.Serialize(req.Descriptions);
        def.IsRequired = req.IsRequired; def.IsUnique = req.IsUnique; def.DefaultValue = req.DefaultValue;
        def.ValidationJson = req.ValidationJson; def.RefEntity = req.RefEntity; def.ShowInList = req.ShowInList;
        if (req.SortOrder.HasValue) def.SortOrder = req.SortOrder.Value;
        ApplyOptions(def, req.Options);
        await db.SaveChangesAsync(ct);
        return ToDto(await LoadDefinitionAsync(id, ct));
    }

    public async Task SetDefinitionActiveAsync(int id, bool active, CancellationToken ct)
    {
        var def = await LoadDefinitionAsync(id, ct, track: true);
        def.IsActive = active;
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidateSpecAsync(CustomFieldDefinitionUpsert req, CancellationToken ct)
    {
        if (req.Labels is null || req.Labels.Count == 0) throw new ValidationException("labels", "La etiqueta es obligatoria.");
        var dt = req.DataType.ToUpperInvariant();
        if (dt == CustomFieldDataTypes.LookupRef && string.IsNullOrWhiteSpace(req.RefEntity))
            throw new ValidationException("refEntity", "LOOKUP_REF requiere el dominio de catálogo referenciado.");
        if (!string.IsNullOrWhiteSpace(req.RefEntity) && !await db.CatalogDomains.AnyAsync(d => d.DomainKey == req.RefEntity && d.IsActive, ct))
            throw new ValidationException("refEntity", $"La lista '{req.RefEntity}' no existe.");
        if (dt is CustomFieldDataTypes.Select or CustomFieldDataTypes.MultiSelect && string.IsNullOrWhiteSpace(req.RefEntity) && (req.Options is null || req.Options.Count == 0))
            throw new ValidationException("options", "Un campo de selección necesita opciones o una lista del catálogo (refEntity).");
        if (!string.IsNullOrWhiteSpace(req.ValidationJson))
        {
            try { RuleEvaluator.ParseValidation(req.ValidationJson); }
            catch (JsonException) { throw new ValidationException("validationJson", "ValidationJson no es JSON válido."); }
        }
    }

    private void ApplyOptions(CustomFieldDefinition def, IList<CustomFieldOptionUpsert>? options)
    {
        if (options is null) return;
        var wanted = options.Select(o => o.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in def.Options) existing.IsActive = wanted.Contains(existing.OptionValue); // desactivar, no borrar
        var sort = 0;
        foreach (var o in options)
        {
            sort += 10;
            var ex = def.Options.FirstOrDefault(x => x.OptionValue.Equals(o.Value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (ex is null) def.Options.Add(new CustomFieldOption { OptionValue = o.Value.Trim(), LabelJson = MultilingualText.Serialize(o.Labels), SortOrder = o.SortOrder ?? sort });
            else { ex.LabelJson = MultilingualText.Serialize(o.Labels); ex.SortOrder = o.SortOrder ?? sort; ex.IsActive = true; }
        }
    }

    // ---------------- Valores por registro ----------------

    public async Task<IReadOnlyList<CustomFieldValueDto>> GetValuesAsync(string entityType, int entityId, CancellationToken ct)
    {
        // Defensa en profundidad: leer valores exige el permiso de lectura de la entidad dueña (403 + PERMISSION_DENIED).
        if (PermissionCatalog.OwnerReadPermission.TryGetValue(entityType, out var readPerm)) await permissions.EnsureAsync(readPerm, ct);
        var defs = await ActiveDefinitionsAsync(entityType, ct);
        var ids = defs.Select(d => d.CustomFieldDefinitionId).ToList();
        var values = await db.CustomFieldValues.AsNoTracking().Where(v => ids.Contains(v.CustomFieldDefinitionId) && v.EntityId == entityId).ToDictionaryAsync(v => v.CustomFieldDefinitionId, ct);
        var result = new List<CustomFieldValueDto>();
        foreach (var d in defs)
        {
            values.TryGetValue(d.CustomFieldDefinitionId, out var v);
            var raw = v is null ? ParseDefault(d) : Extract(d, v);
            result.Add(new CustomFieldValueDto(d.FieldKey, d.DataType!.InternalCode, raw, await DisplayAsync(d, raw, ct)));
        }
        return result;
    }

    /// <summary>Guarda (upsert) los valores enviados; valida requerido/único/tipo/DSL/opciones y pertenencia del registro al tenant.</summary>
    public async Task<IReadOnlyList<CustomFieldValueDto>> SetValuesAsync(string entityType, int entityId, IDictionary<string, object?> input, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        // Escribir valores exige el permiso de edición del módulo dueño (CLIENT → clients.update, etc.); sin entrada, comportamiento anterior.
        if (PermissionCatalog.OwnerWritePermission.TryGetValue(entityType, out var writePerm)) await permissions.EnsureAsync(writePerm, ct);
        // La respuesta relee los valores con el permiso de lectura del dueño: se exige ANTES de guardar, para que nunca se
        // persista un valor cuya respuesta termine en 403.
        if (PermissionCatalog.OwnerReadPermission.TryGetValue(entityType, out var readPermFirst)) await permissions.EnsureAsync(readPermFirst, ct);
        await EnsureEntityExistsAsync(entityType, entityId, ct);
        var defs = await ActiveDefinitionsAsync(entityType, ct);
        var defByKey = defs.ToDictionary(d => d.FieldKey, StringComparer.OrdinalIgnoreCase);
        input = new Dictionary<string, object?>(input, StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string[]>();
        var unknown = input.Keys.Where(k => !defByKey.ContainsKey(k)).ToList();
        foreach (var u in unknown) errors[u] = new[] { "Campo personalizado desconocido." };

        var ids = defs.Select(d => d.CustomFieldDefinitionId).ToList();
        var existing = await db.CustomFieldValues.Where(v => ids.Contains(v.CustomFieldDefinitionId) && v.EntityId == entityId).ToListAsync(ct);

        foreach (var d in defs)
        {
            var provided = input.TryGetValue(d.FieldKey, out var rawInput);
            var current = existing.FirstOrDefault(v => v.CustomFieldDefinitionId == d.CustomFieldDefinitionId);
            if (!provided && current is not null) continue; // no tocado
            object? normalized;
            try { normalized = Normalize(d, provided ? rawInput : ParseDefault(d)); }
            catch (ValidationException ex) { errors[d.FieldKey] = new[] { ex.Message }; continue; }

            var fieldErrors = new List<string>();
            if (normalized is null || (normalized is string s && s.Length == 0) || (normalized is string[] arr && arr.Length == 0))
            {
                if (d.IsRequired) fieldErrors.Add("Este campo es obligatorio.");
                normalized = null;
            }
            else
            {
                fieldErrors.AddRange(RuleEvaluator.Validate(normalized is string[] a ? string.Join(",", a) : normalized, d.ValidationJson));
                fieldErrors.AddRange(await ValidateOptionsAsync(d, normalized, ct));
                if (d.IsUnique) fieldErrors.AddRange(await ValidateUniqueAsync(d, entityId, normalized, ct));
            }
            if (fieldErrors.Count > 0) { errors[d.FieldKey] = fieldErrors.ToArray(); continue; }

            if (current is null)
            {
                current = new CustomFieldValue { TenantId = tenantId, CustomFieldDefinitionId = d.CustomFieldDefinitionId, EntityId = entityId };
                db.CustomFieldValues.Add(current);
            }
            Store(d, current, normalized);
            current.UpdatedAtUtc = DateTime.UtcNow;
            current.UpdatedBy = tenant.UserId;
        }
        if (errors.Count > 0) throw new ValidationException(errors);
        await db.SaveChangesAsync(ct);
        return await GetValuesAsync(entityType, entityId, ct);
    }

    private async Task EnsureEntityExistsAsync(string entityType, int entityId, CancellationToken ct)
    {
        var resolver = resolvers.FirstOrDefault(r => r.EntityTypeCode.Equals(entityType, StringComparison.OrdinalIgnoreCase));
        if (resolver is null) return; // módulo dueño aún no construido: no se puede verificar (se documenta)
        if (!await resolver.ExistsInTenantAsync(entityId, ct)) throw new NotFoundException(entityType, entityId);
    }

    private async Task<List<CustomFieldDefinition>> ActiveDefinitionsAsync(string entityType, CancellationToken ct)
    {
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityType, ct);
        return await db.CustomFieldDefinitions.AsNoTracking().Include(d => d.DataType).Include(d => d.Options)
            .Where(d => d.EntityTypeLookupId == entityTypeId && d.IsActive).OrderBy(d => d.SortOrder).ToListAsync(ct);
    }

    private async Task<CustomFieldDefinition> LoadDefinitionAsync(int id, CancellationToken ct, bool track = false)
    {
        var q = db.CustomFieldDefinitions.Include(d => d.EntityType).Include(d => d.DataType).Include(d => d.Options).AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(d => d.CustomFieldDefinitionId == id, ct) ?? throw new NotFoundException("Campo personalizado", id);
    }

    // ---------------- Tipado ----------------

    private static object? ParseDefault(CustomFieldDefinition d) => string.IsNullOrEmpty(d.DefaultValue) ? null : d.DefaultValue;

    /// <summary>Convierte la entrada (JSON/objeto) al tipo del campo; lanza ValidationException si no coincide.</summary>
    public static object? Normalize(CustomFieldDefinition d, object? raw)
    {
        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Null) raw = null;
            else if (je.ValueKind == JsonValueKind.Array) raw = je.EnumerateArray().Select(e => e.ToString()).ToArray();
            else if (je.ValueKind is JsonValueKind.True or JsonValueKind.False) raw = je.GetBoolean();
            else if (je.ValueKind == JsonValueKind.Number) raw = je.GetDecimal();
            else raw = je.GetString();
        }
        if (raw is null) return null;
        var type = d.DataType!.InternalCode.ToUpperInvariant();
        switch (type)
        {
            case CustomFieldDataTypes.Number:
                return RuleEvaluator.ToDecimal(raw) ?? throw new ValidationException(d.FieldKey, "Se esperaba un número.");
            case CustomFieldDataTypes.Bool:
                return RuleEvaluator.ToBool(raw) ?? throw new ValidationException(d.FieldKey, "Se esperaba sí/no.");
            case CustomFieldDataTypes.Date:
            {
                var dt = RuleEvaluator.ToDate(raw) ?? throw new ValidationException(d.FieldKey, "Se esperaba una fecha (yyyy-MM-dd).");
                return dt.Date;
            }
            case CustomFieldDataTypes.DateTime:
                return RuleEvaluator.ToDate(raw) ?? throw new ValidationException(d.FieldKey, "Se esperaba una fecha y hora ISO.");
            case CustomFieldDataTypes.MultiSelect:
                return raw switch
                {
                    string[] arr => arr.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(),
                    string s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    _ => throw new ValidationException(d.FieldKey, "Se esperaba una lista de valores."),
                };
            default:
                return raw is string[] a2 ? string.Join(",", a2) : RuleEvaluator.ToText(raw);
        }
    }

    private static void Store(CustomFieldDefinition d, CustomFieldValue v, object? value)
    {
        v.ValueText = null; v.ValueNumber = null; v.ValueDate = null; v.ValueBool = null;
        switch (value)
        {
            case null: break;
            case decimal n: v.ValueNumber = n; break;
            case bool b: v.ValueBool = b; break;
            case DateTime dt: v.ValueDate = dt; break;
            case string[] arr: v.ValueText = string.Join(",", arr); break;
            default: v.ValueText = value.ToString(); break;
        }
    }

    private static object? Extract(CustomFieldDefinition d, CustomFieldValue v)
    {
        var type = d.DataType!.InternalCode.ToUpperInvariant();
        return type switch
        {
            CustomFieldDataTypes.Number => v.ValueNumber,
            CustomFieldDataTypes.Bool => v.ValueBool,
            CustomFieldDataTypes.Date => v.ValueDate?.ToString("yyyy-MM-dd"),
            CustomFieldDataTypes.DateTime => v.ValueDate,
            CustomFieldDataTypes.MultiSelect => v.ValueText?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
            _ => v.ValueText,
        };
    }

    private async Task<IEnumerable<string>> ValidateOptionsAsync(CustomFieldDefinition d, object value, CancellationToken ct)
    {
        var type = d.DataType!.InternalCode.ToUpperInvariant();
        if (type is not (CustomFieldDataTypes.Select or CustomFieldDataTypes.MultiSelect or CustomFieldDataTypes.LookupRef)) return Array.Empty<string>();
        var selected = value is string[] arr ? arr : new[] { RuleEvaluator.ToText(value) };
        HashSet<string> allowed;
        if (!string.IsNullOrWhiteSpace(d.RefEntity))
            allowed = (await db.LookupCodes.AsNoTracking().Where(l => l.Entity == d.RefEntity && l.IsActive).Select(l => l.InternalCode).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        else
            allowed = d.Options.Where(o => o.IsActive).Select(o => o.OptionValue).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bad = selected.Where(s => !allowed.Contains(s)).ToList();
        return bad.Count == 0 ? Array.Empty<string>() : new[] { $"Valor(es) no permitido(s): {string.Join(", ", bad)}." };
    }

    private async Task<IEnumerable<string>> ValidateUniqueAsync(CustomFieldDefinition d, int entityId, object value, CancellationToken ct)
    {
        var q = db.CustomFieldValues.AsNoTracking().Where(v => v.CustomFieldDefinitionId == d.CustomFieldDefinitionId && v.EntityId != entityId);
        var exists = value switch
        {
            decimal n => await q.AnyAsync(v => v.ValueNumber == n, ct),
            bool b => await q.AnyAsync(v => v.ValueBool == b, ct),
            DateTime dt => await q.AnyAsync(v => v.ValueDate == dt, ct),
            string[] arr => await q.AnyAsync(v => v.ValueText == string.Join(",", arr), ct),
            _ => await q.AnyAsync(v => v.ValueText == value.ToString(), ct),
        };
        return exists ? new[] { "Ya existe otro registro con este valor (campo único)." } : Array.Empty<string>();
    }

    private async Task<string?> DisplayAsync(CustomFieldDefinition d, object? raw, CancellationToken ct)
    {
        if (raw is null) return null;
        var type = d.DataType!.InternalCode.ToUpperInvariant();
        if (type is CustomFieldDataTypes.Select or CustomFieldDataTypes.MultiSelect or CustomFieldDataTypes.LookupRef)
        {
            var keys = raw is string[] arr ? arr : new[] { RuleEvaluator.ToText(raw) };
            var labels = new List<string>();
            foreach (var k in keys)
            {
                string? label = null;
                if (!string.IsNullOrWhiteSpace(d.RefEntity))
                {
                    var id = await lookups.TryGetIdAsync(d.RefEntity, k, ct);
                    if (id is not null) label = MultilingualText.Resolve((await lookups.GetAsync(id.Value, ct))?.LabelJson, tenant.Lang);
                }
                else label = d.Options.FirstOrDefault(o => o.OptionValue.Equals(k, StringComparison.OrdinalIgnoreCase)) is { } o ? MultilingualText.Resolve(o.LabelJson, tenant.Lang) : null;
                labels.Add(label ?? k);
            }
            return string.Join(", ", labels);
        }
        return raw switch
        {
            decimal n => n.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "Sí" : "No",
            DateTime dt => dt.ToString("O"),
            _ => raw.ToString(),
        };
    }

    private CustomFieldDefinitionDto ToDto(CustomFieldDefinition d) => new(
        d.CustomFieldDefinitionId, d.EntityType?.InternalCode ?? "", d.FieldKey, MultilingualText.Resolve(d.LabelJson, tenant.Lang), MultilingualText.Parse(d.LabelJson),
        MultilingualText.Resolve(d.DescriptionJson, tenant.Lang), d.DataType?.InternalCode ?? "", d.IsRequired, d.IsUnique, d.DefaultValue, d.ValidationJson,
        d.RefEntity, d.ShowInList, d.SortOrder, d.IsActive,
        d.Options.OrderBy(o => o.SortOrder).Select(o => new CustomFieldOptionDto(o.CustomFieldOptionId, o.OptionValue, MultilingualText.Resolve(o.LabelJson, tenant.Lang), MultilingualText.Parse(o.LabelJson), o.SortOrder, o.IsActive)).ToList());
}
