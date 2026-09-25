using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 3 (P9): importador de órdenes por plantilla de posición de columna, en dos pasos (validar → confirmar).

// ---------------------------------------------------------------- plantillas

/// <summary>Columna de la plantilla: posición 1..n en el archivo y campo del catálogo ImportFields.</summary>
public sealed record ImportTemplateColumnDto(int Position, string Field);

/// <summary>Plantilla reutilizable. ClientPublicId null = plantilla general del tenant (sirve para cualquier cliente).</summary>
public sealed record ImportTemplateDto(
    int Id,
    Guid PublicId,
    string Kind,
    string Name,
    Guid? ClientPublicId,
    string? ClientName,
    string Delimiter,
    bool HasHeader,
    IReadOnlyList<ImportTemplateColumnDto> Columns,
    IReadOnlyDictionary<string, string>? Defaults,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>
/// Alta de plantilla. Columns: posiciones únicas &gt;= 1 y campos del catálogo (al menos consigneeName o consigneeCode y
/// packageType/pieces en columna o en Defaults). Delimiter: un carácter (',' por defecto). Kind: ORDER por defecto.
/// </summary>
public sealed record ImportTemplateCreateRequest(
    string Name,
    IList<ImportTemplateColumnDto>? Columns,
    IDictionary<string, string>? Defaults = null,
    Guid? ClientPublicId = null,
    string? Delimiter = null,
    bool HasHeader = true,
    string? Kind = null);

/// <summary>Edición parcial: null = sin cambio. Columns/Defaults reemplazan la lista completa; ClearDefaults borra los defaults; MakeGeneral quita el cliente.</summary>
public sealed record ImportTemplatePatchRequest(
    string? Name = null,
    IList<ImportTemplateColumnDto>? Columns = null,
    IDictionary<string, string>? Defaults = null,
    bool? ClearDefaults = null,
    Guid? ClientPublicId = null,
    bool? MakeGeneral = null,
    string? Delimiter = null,
    bool? HasHeader = null);

// ---------------------------------------------------------------- lotes (validar → confirmar)

/// <summary>Cuerpo JSON de POST /orders/import/validate (la variante multipart envía templatePublicId, clientPublicId y el archivo 'file').</summary>
public sealed record ImportValidateRequest(Guid TemplatePublicId, Guid ClientPublicId, string? Content, string? FileName = null);

/// <summary>Consignatario resuelto de una fila: EXISTING (del directorio, por código o por coincidencia nombre + línea 1) o CREATE (se creará al confirmar).</summary>
public sealed record ImportConsigneeDto(string Action, string Name, Guid? LocationPublicId);

/// <summary>
/// Fila del lote. Row = ordinal de datos (1 = primera fila después de la cabecera); Line = línea física del archivo.
/// Errors por campo (la fila no se crea); Warnings informativos (p. ej. factura repetida confirmable). Tras confirmar:
/// OrderPublicId/OrderNumber/OrderStatus si se creó, o Error con el motivo; Skipped si no estaba en el subconjunto elegido.
/// </summary>
public sealed record ImportRowDto(
    int Row,
    int Line,
    IReadOnlyDictionary<string, string> Values,
    ImportConsigneeDto? Consignee,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlyList<string> Warnings,
    bool IsValid,
    Guid? OrderPublicId,
    string? OrderNumber,
    string? OrderStatus,
    string? Error,
    bool Skipped);

/// <summary>Vista previa del lote (resultado de validar y de GET del lote). Created/Failed solo tras confirmar.</summary>
public sealed record ImportPreviewDto(
    Guid BatchPublicId,
    string Status,
    Guid TemplatePublicId,
    string TemplateName,
    Guid ClientPublicId,
    string ClientName,
    string? FileName,
    int RowCount,
    int ValidRows,
    IReadOnlyList<ImportRowDto> Rows,
    DateTime CreatedAtUtc,
    DateTime? ConfirmedAtUtc,
    int? Created,
    int? Failed);

/// <summary>
/// Confirmación. Rows: subconjunto de filas (ordinales) a crear; vacío = todas las válidas. ConfirmNow confirma cada orden
/// creada (cotiza y verifica crédito); OverrideCredit (exige orders.credit_override) autoriza el exceso de crédito;
/// ConfirmDuplicateInvoice crea las filas con factura repetida confirmable ('Crear de todos modos').
/// </summary>
public sealed record ImportConfirmRequest(
    IList<int>? Rows = null,
    bool ConfirmNow = false,
    bool OverrideCredit = false,
    bool ConfirmDuplicateInvoice = false);

/// <summary>Resultado de la confirmación: órdenes creadas, filas fallidas (con su error) y filas omitidas.</summary>
public sealed record ImportResultDto(
    Guid BatchPublicId,
    string Status,
    int Created,
    int Failed,
    int Skipped,
    IReadOnlyList<ImportRowDto> Rows);

// ---------------------------------------------------------------- forma persistida (ImportBatch.RowsJson)

/// <summary>Consignatario resuelto tal como se guarda en RowsJson.</summary>
public sealed class ImportRowConsigneeRecord
{
    public string Action { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? LocationPublicId { get; set; }
}

/// <summary>
/// Entrada por fila en ImportBatch.RowsJson (camelCase, nulos omitidos): valores parseados, consignatario resuelto o por
/// crear, errores por campo, avisos y, tras confirmar, la orden creada o el error. Es el registro de trabajo del lote;
/// la API lo expone como ImportRowDto.
/// </summary>
public sealed class ImportRowRecord
{
    public int Row { get; set; }
    public int Line { get; set; }
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    public ImportRowConsigneeRecord? Consignee { get; set; }
    public Dictionary<string, string> Errors { get; set; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; set; } = new();
    public Guid? OrderPublicId { get; set; }
    public string? OrderNumber { get; set; }
    public string? OrderStatus { get; set; }
    public string? Error { get; set; }
    public bool? Skipped { get; set; }

    [JsonIgnore] public bool IsValid => Errors.Count == 0;

    public ImportRowDto ToDto() => new(Row, Line, Values, Consignee is null ? null : new ImportConsigneeDto(Consignee.Action, Consignee.Name, Consignee.LocationPublicId),
        Errors, Warnings, IsValid, OrderPublicId, OrderNumber, OrderStatus, Error, Skipped == true);
}
