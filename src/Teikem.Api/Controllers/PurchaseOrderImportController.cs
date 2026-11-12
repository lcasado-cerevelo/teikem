using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 239 — importar órdenes de compra desde un archivo (módulo PURCHASING, permiso purchasing.manage). Origen GENERIC (CSV o .xlsx con columnas
/// Supplier, Number, Date, SKU, Qty, Cost…) o QUICKBOOKS (exportación «Transaction Detail» de compras de QuickBooks Desktop). La orden de compra no
/// mueve inventario; una orden con un producto o proveedor que no existe se bloquea (no se crea) y el resultado dice por qué.
/// </summary>
[ApiController]
[Route("api/v1/purchase-orders/import")]
[Authorize]
[RequireModule(ModuleKeys.Purchasing)]
public sealed class PurchaseOrderImportController(PurchaseOrderImportService imports) : ControllerBase
{
    /// <summary>
    /// Multipart/form-data: <c>file</c> (CSV o .xlsx), <c>source</c> (GENERIC por omisión o QUICKBOOKS), <c>status</c> (DRAFT por omisión o SENT = completada),
    /// <c>warehousePublicId</c> (opcional si la compañía tiene un solo almacén activo) y <c>commit</c> (false = vista previa sin guardar; true = crear las órdenes listas).
    /// </summary>
    [HttpPost, Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.PurchasingManage)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<PurchaseOrderImportResultDto> Import([FromForm] string? source, [FromForm] string? status, [FromForm] Guid? warehousePublicId,
        [FromForm] bool commit, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file", PurchaseOrderImportRules.NoFile);
        if (file.Length > CsvParser.MaxBytes) throw new ValidationException("file", CsvParser.LimitMessage);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return await imports.ImportAsync(source, ms.ToArray(), file.FileName, commit, status, warehousePublicId, ct);
    }
}
