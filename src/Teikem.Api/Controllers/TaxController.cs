using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 55 (P3) — impuestos (IVU), módulo TAX, permiso tax.manage. Las tasas no se editan (se cierra la vigencia y se crea otra): por eso no hay
/// PUT/PATCH/DELETE de tasas. Las exenciones y la categoría del producto se cambian; las exenciones se dan de baja, nunca se borran.
/// </summary>
[ApiController]
[Route("api/v1/tax")]
[Authorize]
[RequireModule(ModuleKeys.Tax)]
public sealed class TaxController(TaxService tax) : ControllerBase
{
    /// <summary>Historial de tasas (la vigente primero por código); activeOnly = solo las vigentes hoy.</summary>
    [HttpGet("rates"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<IReadOnlyList<TaxRateDto>> Rates([FromQuery] bool activeOnly, CancellationToken ct) => tax.ListRatesAsync(activeOnly, ct);

    [HttpPost("rates"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxRateDto> CreateRate([FromBody] TaxRateRequest req, CancellationToken ct) => tax.CreateRateAsync(req, ct);

    /// <summary>Cierra la vigencia de la tasa (la única "edición" que tiene).</summary>
    [HttpPost("rates/{id:int}/close"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxRateDto> CloseRate(int id, [FromBody] TaxRateCloseRequest req, CancellationToken ct) => tax.CloseRateAsync(id, req, ct);

    [HttpGet("settings"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxSettingsDto> Settings(CancellationToken ct) => tax.GetSettingsAsync(ct);

    [HttpPatch("settings"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxSettingsDto> UpdateSettings([FromBody] TaxSettingsRequest req, CancellationToken ct) => tax.UpdateSettingsAsync(req, ct);

    [HttpGet("exemptions"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<IReadOnlyList<TaxExemptionDto>> Exemptions([FromQuery] int? clientId, CancellationToken ct) => tax.ListExemptionsAsync(clientId, ct);

    [HttpPost("exemptions"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxExemptionDto> CreateExemption([FromBody] TaxExemptionRequest req, CancellationToken ct) => tax.CreateExemptionAsync(req, ct);

    [HttpPatch("exemptions/{id:int}"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxExemptionDto> UpdateExemption(int id, [FromBody] TaxExemptionPatchRequest req, CancellationToken ct) => tax.UpdateExemptionAsync(id, req, ct);

    [HttpPost("exemptions/{id:int}/deactivate"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxExemptionDto> DeactivateExemption(int id, CancellationToken ct) => tax.SetExemptionActiveAsync(id, false, ct);

    [HttpPost("exemptions/{id:int}/reactivate"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxExemptionDto> ReactivateExemption(int id, CancellationToken ct) => tax.SetExemptionActiveAsync(id, true, ct);

    /// <summary>Productos activos con su categoría de impuesto (buscador q y filtro category; hasta 500 por página).</summary>
    [HttpGet("products"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<PagedResult<ProductTaxCategoryDto>> Products([FromQuery] string? q, [FromQuery] string? category, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => tax.ListProductCategoriesAsync(q, category, skip, take, ct);

    [HttpGet("products/{productId:int}/category"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<ProductTaxCategoryDto> ProductCategory(int productId, CancellationToken ct) => tax.GetProductCategoryAsync(productId, ct);

    /// <summary>Categoría de impuesto del producto: TAXABLE, EXEMPT o ZERO_RATE.</summary>
    [HttpPut("products/{productId:int}/category"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<ProductTaxCategoryDto> SetProductCategory(int productId, [FromBody] ProductTaxCategoryRequest req, CancellationToken ct) => tax.SetProductCategoryAsync(productId, req, ct);

    /// <summary>Calcula el impuesto de unas líneas con las reglas de la compañía (lo mismo que usarán las órdenes de venta y la factura). No guarda nada.</summary>
    [HttpPost("calculate"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxCalcDto> Calculate([FromBody] TaxCalcRequest req, CancellationToken ct) => tax.CalculateAsync(req, ct);
}
