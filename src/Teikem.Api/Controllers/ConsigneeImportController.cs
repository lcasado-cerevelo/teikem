using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 38 (F-B): importación de consignatarios en dos pasos. POST validate (JSON {templatePublicId, clientPublicId?, content}
/// o multipart con 'file' en validate-file) parsea y valida fila a fila SIN crear nada y guarda un lote VALIDATED; POST confirm
/// crea las filas válidas (una transacción por fila) y pasa el lote a CONFIRMED; POST discard lo descarta. Módulo CATALOG;
/// leer exige locations.read y escribir locations.create.
/// </summary>
[ApiController]
[Route("api/v1/locations/import")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class ConsigneeImportController(ConsigneeImportService imports) : ControllerBase
{
    /// <summary>Catálogo de campos que la plantilla puede mapear (obligatorio, admite default).</summary>
    [HttpGet("fields"), RequirePermission(PermissionCatalog.LocationsRead)]
    public IReadOnlyList<ImportFieldDto> Fields() => ConsigneeImportService.GetFields();

    /// <summary>Validar (JSON): el CSV viaja en content. Límite 5.000 filas / 2 MB → 400. Una petición sin Content-Type responde 415.</summary>
    [HttpPost("validate"), RequirePermission(PermissionCatalog.LocationsCreate)]
    public Task<ConsigneeImportPreviewDto> ValidateJson([FromBody] ConsigneeImportValidateRequest req, CancellationToken ct)
        => imports.ValidateAsync(req, ct);

    /// <summary>
    /// Validar (multipart/form-data): templatePublicId, clientPublicId opcional (cliente por defecto), el archivo 'file' —CSV en UTF-8
    /// o Excel .xlsx (Lote 45)— y, para Excel, 'sheet' (nombre de la hoja; sin él, la primera). Límite 5.000 filas / 2 MB → 400.
    /// </summary>
    [HttpPost("validate-file"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.LocationsCreate)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<ConsigneeImportPreviewDto> ValidateFile([FromForm] Guid templatePublicId, [FromForm] Guid? clientPublicId, IFormFile? file, [FromForm] string? sheet, CancellationToken ct)
    {
        var bytes = await ReadAsync(file, ct);
        return await imports.ValidateFileAsync(templatePublicId, clientPublicId, file!.FileName, bytes, sheet, ct);
    }

    /// <summary>
    /// Lote 45 (F-B2): revisa el archivo SIN guardar nada, para armar el mapeo: formato (CSV/XLSX), hojas del libro, hoja leída,
    /// filas de datos y, por columna, el encabezado y hasta 3 valores de muestra. delimiter/hasHeader son los del editor (',' y sí
    /// por defecto); sheet elige la hoja. Mismos límites que validar.
    /// </summary>
    [HttpPost("inspect-file"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.LocationsCreate)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<ImportFileInspectionDto> InspectFile(IFormFile? file, [FromForm] string? delimiter, [FromForm] bool? hasHeader, [FromForm] string? sheet, CancellationToken ct)
    {
        var bytes = await ReadAsync(file, ct);
        return ConsigneeImportService.InspectFile(bytes, file!.FileName, delimiter, hasHeader ?? true, sheet);
    }

    private static async Task<byte[]> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file", ConsigneeImportService.ContentRequiredMessage);
        if (file.Length > CsvParser.MaxBytes) throw new ValidationException("file", CsvParser.LimitMessage);
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    /// <summary>Vista previa del lote (filas, dueño, errores y avisos; tras confirmar, el consignatario creado o el error por fila).</summary>
    [HttpGet("{batchPublicId:guid}"), RequirePermission(PermissionCatalog.LocationsRead)]
    public Task<ConsigneeImportPreviewDto> Get(Guid batchPublicId, CancellationToken ct) => imports.GetAsync(batchPublicId, ct);

    /// <summary>Confirmar: crea las filas válidas (rows = subconjunto; vacío = todas las válidas). Lote ya confirmado o descartado → 409.</summary>
    [HttpPost("{batchPublicId:guid}/confirm"), RequirePermission(PermissionCatalog.LocationsCreate)]
    public Task<ConsigneeImportResultDto> Confirm(Guid batchPublicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConsigneeImportConfirmRequest? req, CancellationToken ct)
        => imports.ConfirmAsync(batchPublicId, req, ct);

    /// <summary>Descartar un lote pendiente de confirmar (422 si ya fue confirmado o descartado).</summary>
    [HttpPost("{batchPublicId:guid}/discard"), RequirePermission(PermissionCatalog.LocationsCreate)]
    public Task<ConsigneeImportPreviewDto> Discard(Guid batchPublicId, CancellationToken ct) => imports.DiscardAsync(batchPublicId, ct);
}
