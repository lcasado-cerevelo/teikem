using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P8) — proveedores de Compras (módulo PURCHASING). Lectura con purchasing.view; alta, edición, baja y
/// reactivación con purchasing.manage. Nombre único entre los activos (409); término de pago del catálogo PaymentTerm.
/// Ninguna solicitud lleva TenantId: sale del principal (un proveedor de otro tenant es 404).
/// </summary>
[ApiController]
[Route("api/v1/suppliers")]
[Authorize]
[RequireModule(ModuleKeys.Purchasing)]
public sealed class SuppliersController(SupplierService suppliers) : ControllerBase
{
    /// <summary>Proveedores por nombre; por defecto solo los activos. search: nombre, contacto o correo.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<IReadOnlyList<SupplierDto>> List([FromQuery] bool includeInactive, [FromQuery] string? search, CancellationToken ct)
        => suppliers.ListAsync(includeInactive, search, ct);

    [HttpPost, RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<SupplierDto> Create([FromBody] SupplierRequest req, CancellationToken ct) => suppliers.CreateAsync(req, ct);

    /// <summary>PATCH: null = sin cambio; "" = quitar el valor (salvo el nombre). rowVersion opcional (409 si cambió).</summary>
    [HttpPatch("{id:int}"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<SupplierDto> Update(int id, [FromBody] SupplierPatchRequest req, CancellationToken ct) => suppliers.UpdateAsync(id, req, ct);

    [HttpPost("{id:int}/deactivate"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<SupplierDto> Deactivate(int id, CancellationToken ct) => suppliers.SetActiveAsync(id, false, ct);

    [HttpPost("{id:int}/reactivate"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<SupplierDto> Reactivate(int id, CancellationToken ct) => suppliers.SetActiveAsync(id, true, ct);
}
