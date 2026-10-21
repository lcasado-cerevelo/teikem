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
/// 2026-10-21 — Plantillas e importación de ALMACENES y de POSICIONES (3.ª y 4.ª entidades del motor genérico). Módulo WMS_LOT_SERIAL; ver exige inventory.view y escribir warehouse.manage. Plantilla = <c>ImportTemplate</c> de
/// Kind WAREHOUSE o BIN. Solo crea: lo que ya existe se rechaza por fila.
/// </summary>
[ApiController]
[Route("api/v1/warehouses/import-templates")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class WarehouseImportTemplatesController(ImportTemplateService templates) : ControllerBase
{
    [HttpGet("fields"), RequirePermission(PermissionCatalog.InventoryView)]
    public IReadOnlyList<ImportFieldDto> Fields() => ImportTemplateService.GetEntityFields(ImportKinds.Warehouse);

    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<ImportTemplateDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => templates.GetListAsync(ImportKinds.Warehouse, null, null, includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ImportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ImportKinds.Warehouse, ct);

    [HttpPost, RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<ImportTemplateDto> Create([FromBody] ImportTemplateCreateRequest req, CancellationToken ct) => templates.CreateAsync(req, ImportKinds.Warehouse, ct);

    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<ImportTemplateDto> Update(Guid publicId, [FromBody] ImportTemplatePatchRequest req, CancellationToken ct) => templates.UpdateAsync(publicId, req, ImportKinds.Warehouse, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<ImportTemplateDto> Deactivate(Guid publicId, CancellationToken ct) => templates.DeactivateAsync(publicId, ImportKinds.Warehouse, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<ImportTemplateDto> Reactivate(Guid publicId, CancellationToken ct) => templates.ReactivateAsync(publicId, ImportKinds.Warehouse, ct);
}

[ApiController]
[Route("api/v1/warehouses/import")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class WarehouseImportController(EntityImportService imports) : ControllerBase
{
    /// <summary>Importar almacenes (multipart/form-data): <c>templatePublicId</c>, <c>file</c> (CSV o .xlsx) y <c>commit</c> (false = revisar sin guardar; true = crear las filas válidas).</summary>
    [HttpPost, Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.WarehouseManage)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<EntityImportResultDto> Import([FromForm] Guid templatePublicId, [FromForm] bool commit, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file", EntityImportService.NoFileMessage);
        if (file.Length > CsvParser.MaxBytes) throw new ValidationException("file", CsvParser.LimitMessage);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return await imports.ImportAsync(ImportKinds.Warehouse, templatePublicId, ms.ToArray(), file.FileName, commit, ct);
    }
}

/// <summary>Plantillas de importación de posiciones: <c>/api/v1/bins/import-templates</c> (el almacén y la zona viajan en cada fila).</summary>
[ApiController]
[Route("api/v1/bins/import-templates")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class BinImportTemplatesController(ImportTemplateService templates) : ControllerBase
{
    [HttpGet("fields"), RequirePermission(PermissionCatalog.InventoryView)]
    public IReadOnlyList<ImportFieldDto> Fields() => ImportTemplateService.GetEntityFields(ImportKinds.Bin);

    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<ImportTemplateDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => templates.GetListAsync(ImportKinds.Bin, null, null, includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ImportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ImportKinds.Bin, ct);

    [HttpPost, RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<ImportTemplateDto> Create([FromBody] ImportTemplateCreateRequest req, CancellationToken ct) => templates.CreateAsync(req, ImportKinds.Bin, ct);

    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<ImportTemplateDto> Update(Guid publicId, [FromBody] ImportTemplatePatchRequest req, CancellationToken ct) => templates.UpdateAsync(publicId, req, ImportKinds.Bin, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<ImportTemplateDto> Deactivate(Guid publicId, CancellationToken ct) => templates.DeactivateAsync(publicId, ImportKinds.Bin, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<ImportTemplateDto> Reactivate(Guid publicId, CancellationToken ct) => templates.ReactivateAsync(publicId, ImportKinds.Bin, ct);
}

[ApiController]
[Route("api/v1/bins/import")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class BinImportController(EntityImportService imports) : ControllerBase
{
    /// <summary>Importar posiciones (multipart/form-data): <c>templatePublicId</c>, <c>file</c> (CSV o .xlsx) y <c>commit</c> (false = revisar sin guardar; true = crear las filas válidas).</summary>
    [HttpPost, Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.WarehouseManage)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<EntityImportResultDto> Import([FromForm] Guid templatePublicId, [FromForm] bool commit, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file", EntityImportService.NoFileMessage);
        if (file.Length > CsvParser.MaxBytes) throw new ValidationException("file", CsvParser.LimitMessage);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return await imports.ImportAsync(ImportKinds.Bin, templatePublicId, ms.ToArray(), file.FileName, commit, ct);
    }
}
