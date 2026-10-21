using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// 2026-10-21 — Plantillas e importación de PRODUCTOS (primera entidad del motor genérico de plantillas; dictado de Luis 2026-10-19). Módulo WMS_LOT_SERIAL; leer exige inventory.view y escribir
/// inventory.manage. La plantilla es un <c>ImportTemplate</c> de Kind PRODUCT (la de otro tipo responde 404 y un kind distinto en el cuerpo, 400). Baja lógica, nunca DELETE.
/// </summary>
[ApiController]
[Route("api/v1/products/import-templates")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class ProductImportTemplatesController(ImportTemplateService templates) : ControllerBase
{
    /// <summary>Campos que una plantilla de productos puede mapear (obligatorio: sku; name en columna o por defecto).</summary>
    [HttpGet("fields"), RequirePermission(PermissionCatalog.InventoryView)]
    public IReadOnlyList<ImportFieldDto> Fields() => ImportTemplateService.GetEntityFields(ImportKinds.Product);

    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<ImportTemplateDto>> List([FromQuery] bool includeInactive, CancellationToken ct)
        => templates.GetListAsync(ImportKinds.Product, null, null, includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ImportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ImportKinds.Product, ct);

    [HttpPost, RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ImportTemplateDto> Create([FromBody] ImportTemplateCreateRequest req, CancellationToken ct) => templates.CreateAsync(req, ImportKinds.Product, ct);

    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ImportTemplateDto> Update(Guid publicId, [FromBody] ImportTemplatePatchRequest req, CancellationToken ct)
        => templates.UpdateAsync(publicId, req, ImportKinds.Product, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ImportTemplateDto> Deactivate(Guid publicId, CancellationToken ct) => templates.DeactivateAsync(publicId, ImportKinds.Product, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ImportTemplateDto> Reactivate(Guid publicId, CancellationToken ct) => templates.ReactivateAsync(publicId, ImportKinds.Product, ct);
}

[ApiController]
[Route("api/v1/products/import")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class ProductImportController(EntityImportService imports) : ControllerBase
{
    /// <summary>
    /// Importar productos (multipart/form-data): <c>templatePublicId</c>, el archivo <c>file</c> (CSV o .xlsx) y <c>commit</c> (false = vista previa que no guarda; true = crea las filas válidas).
    /// Solo crea: los repetidos se rechazan por fila. Máximo 5.000 filas / 2 MB → 400.
    /// </summary>
    [HttpPost, Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.InventoryManage)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<EntityImportResultDto> Import([FromForm] Guid templatePublicId, [FromForm] bool commit, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file", EntityImportService.NoFileMessage);
        if (file.Length > CsvParser.MaxBytes) throw new ValidationException("file", CsvParser.LimitMessage);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return await imports.ImportAsync(ImportKinds.Product, templatePublicId, ms.ToArray(), file.FileName, commit, ct);
    }
}
