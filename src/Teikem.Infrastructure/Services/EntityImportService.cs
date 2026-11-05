using Teikem.Domain.Orders;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

public sealed record EntityImportRowDto(int Row, int Line, string? Key, string? Label, string Status, IReadOnlyDictionary<string, string> Errors);

/// <summary>
/// Resultado de importar una entidad: sin confirmar (vista previa, no guarda nada) o confirmado (crea o actualiza los válidos y dice por fila qué pasó).
/// Estados de fila: OK/CREATED (alta), UPDATE/UPDATED (actualiza un registro existente; solo las entidades que lo admiten) y ERROR.
/// </summary>
public sealed record EntityImportResultDto(bool Committed, int Total, int Valid, int Rejected, int Created, IReadOnlyList<EntityImportRowDto> Rows, int Updated = 0);

/// <summary>Resultado de revisar una fila: sus errores (vacío = válida), cómo mostrarla (clave y nombre) y lo que hace falta para guardarla. <c>IsUpdate</c>: la fila actualiza un registro que ya existe en vez de crearlo.</summary>
public sealed record ImportRowCheck(IReadOnlyDictionary<string, string> Errors, string? Key, string? Label, object? Payload, bool IsUpdate = false);

/// <summary>
/// 2026-10-21 — La parte de una entidad del motor genérico que toca la base de datos: cargar lo que necesita para revisar filas (catálogos, repetidos), revisar una fila con las MISMAS reglas del alta
/// manual (sin guardar) y crearla. Una por entidad (<c>ProductImportRows</c>, <c>SupplierImportRows</c>…), registrada en DependencyInjection; sus campos viven en <see cref="ImportEntities"/>.
/// </summary>
public interface IImportEntityRows
{
    string Kind { get; }

    /// <summary>Lo que la revisión necesita de toda la carga (se pide una vez por archivo; puede guardar el estado de repetidos dentro del archivo).</summary>
    Task<object> LoadContextAsync(CancellationToken ct);

    Task<ImportRowCheck> CheckRowAsync(object context, IReadOnlyDictionary<string, string> values, CancellationToken ct);

    /// <summary>
    /// Igual que la anterior, pero además recibe <paramref name="fileValues"/>: lo que trae el archivo SIN los valores por defecto de la plantilla. Lo usan las entidades que actualizan
    /// lo existente: ahí la celda vacía significa "sin cambio" y un valor por defecto no debe pisar lo que ya tiene el registro. Por omisión ignora <paramref name="fileValues"/>.
    /// </summary>
    Task<ImportRowCheck> CheckRowAsync(object context, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> fileValues, CancellationToken ct)
        => CheckRowAsync(context, values, ct);

    /// <summary>Guarda la fila ya revisada (crea o actualiza, según <see cref="ImportRowCheck.IsUpdate"/>). Una <see cref="TeikemException"/> (validación o conflicto) se informa en esa fila y no frena a las demás.</summary>
    Task CommitRowAsync(object payload, CancellationToken ct);
}

/// <summary>
/// 2026-10-21 — Importador genérico de entidades con plantilla (dictado de Luis, 2026-10-19; decisiones del 2026-10-21): lee el archivo (CSV o .xlsx, hasta 5.000 filas / 2 MB), aplica la plantilla
/// (columnas → campos, valores por defecto), revisa cada fila con la entidad y, con <c>commit</c>, guarda las válidas. Por omisión solo CREA (lo repetido se rechaza por fila);
/// los productos (2026-11-05, pedido de Luis) además ACTUALIZAN el que ya existe por SKU y dueño: ver <c>ProductImportRows</c>.
/// </summary>
public sealed class EntityImportService(ImportTemplateService templates, IEnumerable<IImportEntityRows> entities)
{
    public const string NoFileMessage = "Elija el archivo a importar.";
    public const string NoRowsMessage = "El archivo no trae filas.";
    public const string RepeatedInFileMessage = "El registro está repetido en el archivo.";
    public const string NotANumberMessage = "Debe ser un número.";

    public async Task<EntityImportResultDto> ImportAsync(string kind, Guid templatePublicId, byte[] bytes, string? fileName, bool commit, CancellationToken ct)
    {
        var meta = ImportEntities.TryGet(kind) ?? throw new ValidationException("kind", ImportTemplateService.UnknownKindMessage(kind));
        var entity = entities.FirstOrDefault(e => string.Equals(e.Kind, meta.Kind, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("kind", ImportTemplateService.UnknownKindMessage(kind));
        if (bytes is null || bytes.Length == 0) throw new ValidationException("file", NoFileMessage);
        var template = await templates.ResolveForImportAsync(templatePublicId, null, meta.Kind, ct);
        var columns = ImportTemplateService.ParseColumns(template.ColumnsJson);
        var defaults = ImportTemplateService.ParseDefaults(template.DefaultsJson);

        ImportFileTable table;
        try { table = ImportFileReader.Read(bytes, fileName, ImportTemplateService.DelimiterOf(template), template.HasHeader, null); }
        catch (CsvLimitException) { throw new ValidationException("file", CsvParser.LimitMessage); }
        if (table.Rows.Count == 0) throw new ValidationException("file", NoRowsMessage);

        var context = await entity.LoadContextAsync(ct);
        var rows = new List<EntityImportRowDto>();
        var payloads = new List<(int Index, object Payload, bool IsUpdate)>();
        var noDefaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
        {
            var values = meta.MapRow(row.Cells, columns, defaults);
            var fileValues = meta.MapRow(row.Cells, columns, noDefaults);
            var check = await entity.CheckRowAsync(context, values, fileValues, ct);
            rows.Add(new EntityImportRowDto(row.RowNumber, row.Line, check.Key, check.Label, check.Errors.Count == 0 ? (check.IsUpdate ? "UPDATE" : "OK") : "ERROR", check.Errors));
            if (check.Errors.Count == 0 && check.Payload is not null) payloads.Add((rows.Count - 1, check.Payload, check.IsUpdate));
        }

        var created = 0;
        var updated = 0;
        if (commit)
        {
            foreach (var (index, payload, isUpdate) in payloads)
            {
                try
                {
                    await entity.CommitRowAsync(payload, ct);
                    rows[index] = rows[index] with { Status = isUpdate ? "UPDATED" : "CREATED" };
                    if (isUpdate) updated++; else created++;
                }
                catch (TeikemException ex)
                {
                    var errs = ex.Errors is { Count: > 0 }
                        ? ex.Errors.ToDictionary(e => e.Key, e => string.Join(" ", e.Value), StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal) { ["row"] = ex.Message };
                    rows[index] = rows[index] with { Status = "ERROR", Errors = errs };
                }
            }
        }
        var valid = rows.Count(r => r.Status is "OK" or "CREATED" or "UPDATE" or "UPDATED");
        return new EntityImportResultDto(commit, rows.Count, valid, rows.Count - valid, created, rows, updated);
    }
}
