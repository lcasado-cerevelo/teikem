using System.Text;
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
/// Lote 3 (P9): importador de órdenes en dos pasos. POST validate (JSON {templatePublicId, clientPublicId, content} o
/// multipart con 'file') parsea, valida fila a fila SIN crear órdenes y guarda un lote VALIDATED; POST confirm crea las
/// filas válidas (una transacción por fila) y pasa el lote a CONFIRMED; POST discard lo descarta. Módulo núcleo LTL_GROUND;
/// el operador interno pasa OrderScope.Any (el portal pasará el ClientId del principal).
/// </summary>
[ApiController]
[Route("api/v1/orders/import")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class OrderImportController(OrderImportService imports) : ControllerBase
{
    /// <summary>
    /// Validar (JSON): el CSV viaja en content. Límite 5.000 filas / 2 MB → 400. Sin [Consumes] a propósito: la variante
    /// multipart gana por su [Consumes] y una petición sin Content-Type cae aquí (415 del [FromBody]) en vez de ser ambigua (500).
    /// </summary>
    [HttpPost("validate"), RequirePermission(PermissionCatalog.OrdersCreate)]
    public Task<ImportPreviewDto> ValidateJson([FromBody] ImportValidateRequest req, CancellationToken ct)
        => imports.ValidateAsync(req, OrderScope.Any, ct);

    /// <summary>Validar (multipart/form-data): campos templatePublicId y clientPublicId y el archivo 'file' (UTF-8). Límite 5.000 filas / 2 MB → 400.</summary>
    [HttpPost("validate"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.OrdersCreate)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<ImportPreviewDto> ValidateFile([FromForm] Guid templatePublicId, [FromForm] Guid clientPublicId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file", OrderImportService.ContentRequiredMessage);
        if (file.Length > CsvParser.MaxBytes) throw new ValidationException("file", CsvParser.LimitMessage);
        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            content = await reader.ReadToEndAsync(ct);
        return await imports.ValidateAsync(new ImportValidateRequest(templatePublicId, clientPublicId, content, file.FileName), OrderScope.Any, ct);
    }

    /// <summary>Vista previa del lote (filas, consignatario resuelto, errores y avisos; tras confirmar, la orden creada o el error por fila).</summary>
    [HttpGet("{batchPublicId:guid}"), RequirePermission(PermissionCatalog.OrdersView)]
    public Task<ImportPreviewDto> Get(Guid batchPublicId, CancellationToken ct)
        => imports.GetAsync(batchPublicId, OrderScope.Any, ct);

    /// <summary>Confirmar: crea las filas válidas (rows = subconjunto; confirmNow; overrideCredit exige orders.credit_override; confirmDuplicateInvoice). Lote ya confirmado → 409.</summary>
    [HttpPost("{batchPublicId:guid}/confirm"), RequirePermission(PermissionCatalog.OrdersCreate)]
    public Task<ImportResultDto> Confirm(Guid batchPublicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ImportConfirmRequest? req, CancellationToken ct)
        => imports.ConfirmAsync(batchPublicId, req, OrderScope.Any, ct);

    /// <summary>Descartar un lote pendiente de confirmar (422 si ya fue confirmado o descartado).</summary>
    [HttpPost("{batchPublicId:guid}/discard"), RequirePermission(PermissionCatalog.OrdersCreate)]
    public Task<ImportPreviewDto> Discard(Guid batchPublicId, CancellationToken ct)
        => imports.DiscardAsync(batchPublicId, OrderScope.Any, ct);
}
