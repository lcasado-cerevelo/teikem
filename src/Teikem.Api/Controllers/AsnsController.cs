using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P4) — avisos de llegada (ASN) bajo el módulo WMS_LOTSERIAL. Lectura con inventory.view; alta y cancelación con
/// warehouse.receive. Un aviso de cliente lleva solo productos activos de ese cliente; los avisos de orden de compra los
/// crea la recepción contra PO. Lecturas con InventoryScope.Any (usuarios internos; el Portal pasará el cliente, D44).
/// Historial de estatus: /api/v1/status/history/ASN/{id}.
/// </summary>
[ApiController]
[Route("api/v1/asns")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class AsnsController(AsnService asns) : ControllerBase
{
    /// <summary>Avisos activos (hasta 200, recientes primero) con filtros warehousePublicId, status, clientPublicId y search (referencia, cliente, PO).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<AsnDto>> List([FromQuery] Guid? warehousePublicId, [FromQuery] string[]? status, [FromQuery] Guid? clientPublicId,
        [FromQuery] string? search, CancellationToken ct)
        => asns.ListAsync(new AsnQuery(warehousePublicId, status is { Length: > 0 } ? status : null, clientPublicId, search), InventoryScope.Any, ct);

    [HttpGet("{id:int}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<AsnDto> Get(int id, CancellationToken ct) => asns.GetAsync(id, InventoryScope.Any, ct);

    /// <summary>Alta de un aviso de cliente: almacén (o el único activo), cliente dueño y 1..200 líneas de sus productos; nace EXPECTED.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<AsnDto> Create([FromBody] AsnCreateRequest req, CancellationToken ct) => asns.CreateAsync(req, ct);

    /// <summary>Cancela un aviso EXPECTED (422 si ya se recibió o canceló; 409 con un recibo abierto).</summary>
    [HttpPost("{id:int}/cancel"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<AsnDto> Cancel(int id, CancellationToken ct) => asns.CancelAsync(id, ct);
}
