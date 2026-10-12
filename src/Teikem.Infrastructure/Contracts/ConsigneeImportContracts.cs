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
    bool Skipped);

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

/// <summary>Confirmación. Rows: subconjunto de filas (ordinales) a crear; vacío o ausente = todas las válidas.</summary>
public sealed record ConsigneeImportConfirmRequest(IList<int>? Rows = null);

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

    [JsonIgnore] public bool IsValid => Errors.Count == 0;

    public ConsigneeImportRowDto ToDto() => new(
        Row, Line, Values,
        OwnerResolved ? new ConsigneeImportOwnerDto(OwnerClientPublicId, OwnerClientName, OwnerClientId is null) : null,
        Errors, Warnings, IsValid, LocationPublicId, Error, Skipped == true);
}
