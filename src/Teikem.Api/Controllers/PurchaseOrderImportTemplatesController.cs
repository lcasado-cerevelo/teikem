using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 246 (Lote F) — plantillas de importación de ÓRDENES DE COMPRA definidas por el usuario (<c>ImportTemplate</c> Kind PURCHASE_ORDER, motor genérico). Módulo
/// PURCHASING; leer exige purchasing.view y escribir purchasing.manage. Campos: supplier, documentNumber, orderDate, expectedDate, sku, description, quantity y unitCost;
/// documentNumber, sku y quantity van en columna y el proveedor en su columna o como valor fijo. La plantilla no lleva cliente. Se usa al importar con
/// <c>POST /api/v1/purchase-orders/import</c> (source=TEMPLATE, templatePublicId). La ruta de envíos (/api/v1/import-templates) no acepta este tipo.
/// </summary>
[ApiController]
[Route("api/v1/purchase-orders/import-templates")]
[Authorize]
[RequireModule(ModuleKeys.Purchasing)]
public sealed class PurchaseOrderImportTemplatesController(ImportTemplateService templates) : ControllerBase
{
    [HttpGet("fields"), RequirePermission(PermissionCatalog.PurchasingView)]
    public IReadOnlyList<ImportFieldDto> Fields() => ImportTemplateService.GetEntityFields(ImportKinds.PurchaseOrder);

    [HttpGet, RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<IReadOnlyList<ImportTemplateDto>> List([FromQuery] bool includeInactive, CancellationToken ct)
        => templates.GetListAsync(ImportKinds.PurchaseOrder, null, null, includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<ImportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ImportKinds.PurchaseOrder, ct);

    [HttpPost, RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<ImportTemplateDto> Create([FromBody] ImportTemplateCreateRequest req, CancellationToken ct) => templates.CreateAsync(req, ImportKinds.PurchaseOrder, ct);

    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<ImportTemplateDto> Update(Guid publicId, [FromBody] ImportTemplatePatchRequest req, CancellationToken ct)
        => templates.UpdateAsync(publicId, req, ImportKinds.PurchaseOrder, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<ImportTemplateDto> Deactivate(Guid publicId, CancellationToken ct) => templates.DeactivateAsync(publicId, ImportKinds.PurchaseOrder, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<ImportTemplateDto> Reactivate(Guid publicId, CancellationToken ct) => templates.ReactivateAsync(publicId, ImportKinds.PurchaseOrder, ct);
}
