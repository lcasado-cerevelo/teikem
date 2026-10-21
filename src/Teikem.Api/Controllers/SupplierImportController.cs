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
/// 2026-10-21 — Plantillas e importación de PROVEEDORES (2.ª entidad del motor genérico). Módulo PURCHASING; leer exige purchasing.view y escribir purchasing.manage. Plantilla = <c>ImportTemplate</c> de
/// Kind SUPPLIER. Solo crea: un proveedor activo con el mismo nombre se rechaza por fila.
/// </summary>
[ApiController]
[Route("api/v1/suppliers/import-templates")]
[Authorize]
[RequireModule(ModuleKeys.Purchasing)]
public sealed class SupplierImportTemplatesController(ImportTemplateService templates) : ControllerBase
{
    [HttpGet("fields"), RequirePermission(PermissionCatalog.PurchasingView)]
    public IReadOnlyList<ImportFieldDto> Fields() => ImportTemplateService.GetEntityFields(ImportKinds.Supplier);

    [HttpGet, RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<IReadOnlyList<ImportTemplateDto>> List([FromQuery] bool includeInactive, CancellationToken ct)
        => templates.GetListAsync(ImportKinds.Supplier, null, null, includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<ImportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ImportKinds.Supplier, ct);

    [HttpPost, RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<ImportTemplateDto> Create([FromBody] ImportTemplateCreateRequest req, CancellationToken ct) => templates.CreateAsync(req, ImportKinds.Supplier, ct);

    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<ImportTemplateDto> Update(Guid publicId, [FromBody] ImportTemplatePatchRequest req, CancellationToken ct)
        => templates.UpdateAsync(publicId, req, ImportKinds.Supplier, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<ImportTemplateDto> Deactivate(Guid publicId, CancellationToken ct) => templates.DeactivateAsync(publicId, ImportKinds.Supplier, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<ImportTemplateDto> Reactivate(Guid publicId, CancellationToken ct) => templates.ReactivateAsync(publicId, ImportKinds.Supplier, ct);
}

[ApiController]
[Route("api/v1/suppliers/import")]
[Authorize]
[RequireModule(ModuleKeys.Purchasing)]
public sealed class SupplierImportController(EntityImportService imports) : ControllerBase
{
    /// <summary>Importar proveedores (multipart/form-data): <c>templatePublicId</c>, <c>file</c> (CSV o .xlsx) y <c>commit</c> (false = revisar sin guardar; true = crear las filas válidas).</summary>
    [HttpPost, Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.PurchasingManage)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<EntityImportResultDto> Import([FromForm] Guid templatePublicId, [FromForm] bool commit, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file", EntityImportService.NoFileMessage);
        if (file.Length > CsvParser.MaxBytes) throw new ValidationException("file", CsvParser.LimitMessage);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return await imports.ImportAsync(ImportKinds.Supplier, templatePublicId, ms.ToArray(), file.FileName, commit, ct);
    }
}
