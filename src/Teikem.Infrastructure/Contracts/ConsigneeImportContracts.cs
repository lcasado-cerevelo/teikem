using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 38 (F-B): importación CSV de consignatarios en dos pasos (validar → confirmar). Reutiliza ImportTemplate/ImportBatch
// con Kind='CONSIGNEE'; las plantillas viajan como ImportTemplateDto / ImportTemplateCreateRequest / ImportTemplatePatchRequest.

/// <summary>Campo del catálogo de la plantilla: si la fila debe traerlo (en columna o en defaults) y si admite valor por defecto.</summary>
public sealed record ImportFieldDto(string Field, bool Required, bool DefaultAllowed);

/// <summary>
/// Cuerpo JSON de POST /locations/import/validate (la variante multipart envía templatePublicId, clientPublicId opcional y el
/// archivo 'file'). ClientPublicId null = lote sin cliente: cada fila es compartida o indica su cliente (clientCode/clientName).
/// </summary>
public sealed record ConsigneeImportValidateRequest(Guid TemplatePublicId, Guid? ClientPublicId, string? Content, string? FileName = null);

/// <summary>Dueño resuelto de la fila: un cliente o compartida del operador (IsShared).</summary>
public sealed record ConsigneeImportOwnerDto(Guid? ClientPublicId, string? ClientName, bool IsShared);

/// <summary>
/// Fila del lote. Row = ordinal de datos (1 = primera fila después de la cabecera); Line = línea física del archivo.
/// Owner null si el cliente de la fila no se pudo resolver. Errors por campo (la fila no se crea); Warnings informativos.
/// Tras confirmar: LocationPublicId si se creó, o Error con el motivo; Skipped si no estaba en el subconjunto elegido.
/// Lote 46 (F-B3): Inferred = campos completados con el catálogo de códigos postales (city, state, postalCode); OriginalPostalCode =
/// el ZIP del archivo cuando se sustituyó por uno de entrega («código postal ajustado»); Variant/VariantName = variante de mapeo
/// que se usó (1..n; null si la plantilla no tiene variantes); Cells = las celdas de la fila cuando ninguna variante aplicó.
/// </summary>
public sealed record ConsigneeImportRowDto(
    int Row,
    int Line,
    IReadOnlyDictionary<string, string> Values,
    ConsigneeImportOwnerDto? Owner,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlyList<string> Warnings,
    bool IsValid,
    Guid? LocationPublicId,
    string? Error,
    bool Skipped,
    IReadOnlyList<string>? Inferred = null,
    string? OriginalPostalCode = null,
    int? Variant = null,
    string? VariantName = null,
    IReadOnlyList<string>? Cells = null);

/// <summary>Vista previa del lote (resultado de validar y de GET del lote). ClientPublicId null = lote sin cliente. Created/Failed solo tras confirmar.</summary>
public sealed record ConsigneeImportPreviewDto(
    Guid BatchPublicId,
    string Status,
    Guid TemplatePublicId,
    string TemplateName,
    Guid? ClientPublicId,
    string? ClientName,
    string? FileName,
    int RowCount,
    int ValidRows,
    IReadOnlyList<ConsigneeImportRowDto> Rows,
    DateTime CreatedAtUtc,
    DateTime? ConfirmedAtUtc,
    int? Created,
    int? Failed);

/// <summary>
/// Confirmación. Rows: subconjunto de filas (ordinales) a crear; vacío o ausente = todas las válidas. RememberClient (Lote 45):
/// guarda en la plantilla el cliente del lote como su cliente por defecto (null = compartidos) para la próxima importación.
/// </summary>
public sealed record ConsigneeImportConfirmRequest(IList<int>? Rows = null, bool RememberClient = false);

/// <summary>Columna del archivo revisado (Lote 45): posición 1..n, texto del encabezado (si la plantilla lo trae) y hasta 3 valores de muestra.</summary>
public sealed record ImportFileColumnDto(int Position, string? Header, IReadOnlyList<string> Samples);

/// <summary>
/// Resultado de POST /locations/import/inspect-file (Lote 45, no guarda nada): formato (CSV o XLSX), hojas del libro (vacío en CSV),
/// hoja leída, filas de datos, columnas con su encabezado y muestras y las primeras 3 filas completas (SampleRows, para la vista
/// previa de las columnas unidas), para armar el mapeo en el editor de plantillas.
/// </summary>
public sealed record ImportFileInspectionDto(string Format, IReadOnlyList<string> Sheets, string? Sheet, int RowCount, IReadOnlyList<ImportFileColumnDto> Columns,
    IReadOnlyList<IReadOnlyList<string>> SampleRows);

/// <summary>Resultado de la confirmación: consignatarios creados, filas fallidas (con su error) y filas omitidas.</summary>
public sealed record ConsigneeImportResultDto(
    Guid BatchPublicId,
    string Status,
    int Created,
    int Failed,
    int Skipped,
    IReadOnlyList<ConsigneeImportRowDto> Rows);

// ---------------------------------------------------------------- forma persistida (ImportBatch.RowsJson, [NotAudited])

/// <summary>
/// Entrada por fila en ImportBatch.RowsJson de un lote CONSIGNEE (camelCase, nulos omitidos): valores normalizados, dueño
/// resuelto (OwnerClientId interno; la API solo expone el PublicId), errores por campo, avisos y, tras confirmar, el
/// consignatario creado o el error. La API lo expone como ConsigneeImportRowDto.
/// </summary>
public sealed class ConsigneeImportRowRecord
{
    public int Row { get; set; }
    public int Line { get; set; }
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    /// <summary>true si el dueño quedó resuelto (cliente o compartida); false si el cliente de la fila no se pudo resolver.</summary>
    public bool OwnerResolved { get; set; }
    public int? OwnerClientId { get; set; }
    public Guid? OwnerClientPublicId { get; set; }
    public string? OwnerClientName { get; set; }
    public Dictionary<string, string> Errors { get; set; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; set; } = new();
    public Guid? LocationPublicId { get; set; }
    public string? Error { get; set; }
    public bool? Skipped { get; set; }
    /// <summary>Lote 46 (F-B3): campos completados con el catálogo de códigos postales (null = ninguno).</summary>
    public List<string>? Inferred { get; set; }
    /// <summary>Lote 46 (F-B3): ZIP del archivo sustituido por uno de entrega (apartados o militar → entrega de la misma ciudad).</summary>
    public string? OriginalPostalCode { get; set; }
    /// <summary>Lote 46 (F-B3): variante de mapeo usada (1..n) y su nombre; null sin variantes.</summary>
    public int? Variant { get; set; }
    public string? VariantName { get; set; }
    /// <summary>Lote 46 (F-B3): celdas de la fila, solo cuando ninguna variante aplicó (para ver qué traía).</summary>
    public List<string>? Cells { get; set; }

    [JsonIgnore] public bool IsValid => Errors.Count == 0;

    public ConsigneeImportRowDto ToDto() => new(
        Row, Line, Values,
        OwnerResolved ? new ConsigneeImportOwnerDto(OwnerClientPublicId, OwnerClientName, OwnerClientId is null) : null,
        Errors, Warnings, IsValid, LocationPublicId, Error, Skipped == true,
        Inferred is { Count: > 0 } ? Inferred : null, OriginalPostalCode, Variant, VariantName, Cells);
}
