using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 3 (P9): importador de órdenes por plantilla de posición de columna, en dos pasos (validar → confirmar).

// ---------------------------------------------------------------- plantillas

/// <summary>
/// Columna de la plantilla: posición 1..n en el archivo y campo del catálogo ImportFields. Lote 45 (F-B2, solo consignatarios):
/// Field = "ignore" marca la columna «No usar»; un campo con varias columnas las une en el orden de Part (1, 2…) con el Separator
/// de cada parte después de la primera (null = un espacio); la misma posición puede alimentar varios campos.
/// </summary>
public sealed record ImportTemplateColumnDto(int Position, string Field, int? Part = null, string? Separator = null);

/// <summary>
/// Lote 46 (F-B3, solo consignatarios): condición de una variante sobre la columna Position del archivo. Operator: EMPTY (vacía),
/// NOT_EMPTY (trae algo), EQUALS (igual a Value) o CONTAINS (contiene Value); el texto se compara sin acentos ni mayúsculas.
/// </summary>
public sealed record ImportVariantConditionDto(int Position, string Operator, string? Value = null);

/// <summary>
/// Lote 46 (F-B3, solo consignatarios): variante de mapeo. A cada fila se le aplica la PRIMERA variante cuyas condiciones (todas)
/// se cumplen; sin condiciones aplica siempre (solo puede ir al final). Columns: el mapeo de esa variante (mismas reglas que las
/// columnas de la plantilla). Los defaults son los de la plantilla.
/// </summary>
public sealed record ImportTemplateVariantDto(string? Name, IReadOnlyList<ImportVariantConditionDto>? Conditions, IReadOnlyList<ImportTemplateColumnDto> Columns);

/// <summary>Lote 131: regla de línea de un archivo con varias líneas por orden (Kind DESCRIPTION o INVOICE; si la celda de la columna Position cumple la condición, la línea no es pieza).</summary>
public sealed record ImportLineRuleDto(string Kind, int Position, string Operator, string? Value = null);

/// <summary>
/// Plantilla reutilizable. ClientPublicId null = plantilla general del tenant (sirve para cualquier cliente). DefaultClientPublicId
/// (Lote 45): cliente que la importación propone por defecto («todo el archivo es de X»); no limita el uso de la plantilla.
/// </summary>
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
    DateTime? UpdatedAtUtc,
    Guid? DefaultClientPublicId = null,
    string? DefaultClientName = null,
    IReadOnlyList<ImportTemplateVariantDto>? Variants = null,
    bool MultiLine = false,
    IReadOnlyList<ImportLineRuleDto>? LineRules = null);

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
    string? Kind = null,
    Guid? DefaultClientPublicId = null,
    IList<ImportTemplateVariantDto>? Variants = null,
    bool MultiLine = false,
    IList<ImportLineRuleDto>? LineRules = null);

/// <summary>
/// Edición parcial: null = sin cambio. Columns/Defaults reemplazan la lista completa; ClearDefaults borra los defaults; MakeGeneral quita el cliente.
/// DefaultClientPublicId cambia el cliente por defecto y ClearDefaultClient lo quita (Lote 45; excluyentes).
/// Variants (Lote 46, consignatarios): null = sin cambio; lista = reemplaza las variantes; [] = vuelve a una sola variante (con Columns
/// o, si no viene, las columnas de la primera variante). Con variantes, Columns no se envía.
/// </summary>
public sealed record ImportTemplatePatchRequest(
    string? Name = null,
    IList<ImportTemplateColumnDto>? Columns = null,
    IDictionary<string, string>? Defaults = null,
    bool? ClearDefaults = null,
    Guid? ClientPublicId = null,
    bool? MakeGeneral = null,
    string? Delimiter = null,
    bool? HasHeader = null,
    Guid? DefaultClientPublicId = null,
    bool? ClearDefaultClient = null,
    IList<ImportTemplateVariantDto>? Variants = null,
    bool? MultiLine = null,
    IList<ImportLineRuleDto>? LineRules = null);

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
    bool Skipped,
    IReadOnlyList<ImportLineDto>? Lines = null);

/// <summary>Lote 131: una línea del archivo dentro de su orden (Kind: PIECE, DESCRIPTION o INVOICE; Row = fila del archivo; sus propios errores).</summary>
public sealed record ImportLineDto(int Row, string Kind, IReadOnlyDictionary<string, string> Values, IReadOnlyDictionary<string, string> Errors);

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
    /// <summary>Lote 131: las líneas del archivo de esta orden (solo plantillas con varias líneas por orden).</summary>
    public List<ImportLineRecord>? Lines { get; set; }

    [JsonIgnore] public bool IsValid => Errors.Count == 0;

    public ImportRowDto ToDto() => new(Row, Line, Values, Consignee is null ? null : new ImportConsigneeDto(Consignee.Action, Consignee.Name, Consignee.LocationPublicId),
        Errors, Warnings, IsValid, OrderPublicId, OrderNumber, OrderStatus, Error, Skipped == true,
        Lines?.Select(l => new ImportLineDto(l.Row, l.Kind, l.Values, l.Errors)).ToList());
}

/// <summary>Lote 131: línea del archivo guardada en RowsJson (sus valores mapeados y sus errores).</summary>
public sealed class ImportLineRecord
{
    public int Row { get; set; }
    public string Kind { get; set; } = Teikem.Domain.Orders.ImportLineKinds.Piece;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Errors { get; set; } = new(StringComparer.Ordinal);
}
