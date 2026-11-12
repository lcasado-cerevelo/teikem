using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 241 — «Análisis de ventas» (módulo Órdenes de venta). Ver con sales.analysis.view; administrar (rutas, representantes, cuotas, líneas y marcas, representante de los
/// clientes, ajustes, importar el historial de QuickBooks y enlazar nombres sin emparejar) con sales.analysis.manage. El TenantId sale de la sesión. Bajas lógicas
/// (deactivate/reactivate), nunca DELETE. Meses «AAAA-MM».
/// </summary>
[ApiController]
[Route("api/v1/sales-analysis")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesAnalysisController(SalesAnalysisService analysis, SalesAnalysisSetupService setup, SalesHistoryImportService importer) : ControllerBase
{
    private const string View = PermissionCatalog.SalesAnalysisView + "|" + PermissionCatalog.SalesAnalysisManage;
    private const string Manage = PermissionCatalog.SalesAnalysisManage;
    private const long MaxFileBytes = 20L * 1024 * 1024;

    // ---------------------------------------------------------------- vistas

    /// <summary>Vista «Por representante»: month (por omisión el último mes completo) contra compareMonth (por omisión el anterior).</summary>
    [HttpGet("by-rep"), RequirePermission(View)]
    public Task<SalesByRepDto> ByRep([FromQuery] string? month, [FromQuery] string? compareMonth, CancellationToken ct) => analysis.GetByRepAsync(month, compareMonth, ct);

    /// <summary>Transacciones del mes, paginadas (skip/take hasta 500) o completas con all = true (exportar). repId = 0: sin representante.</summary>
    [HttpGet("transactions"), RequirePermission(View)]
    public Task<PagedResult<SalesTransactionDto>> Transactions([FromQuery] string? month, [FromQuery] int? repId, [FromQuery] string? q, [FromQuery] int skip = 0,
        [FromQuery] int take = 100, [FromQuery] bool all = false, CancellationToken ct = default)
        => analysis.GetTransactionsAsync(month, repId, q, skip, take, all, ct);

    /// <summary>Vista «Movimiento de productos» de fromMonth a toMonth (por omisión los 12 meses que terminan en el del día de corte) al día asOf (por omisión hoy).</summary>
    [HttpGet("product-movement"), RequirePermission(View)]
    public Task<ProductMovementDto> ProductMovement([FromQuery] string? fromMonth, [FromQuery] string? toMonth, [FromQuery] DateOnly? asOf, [FromQuery] int? leadTimeDays,
        [FromQuery] decimal? serviceLevelZ, [FromQuery] int? topClients, CancellationToken ct)
        => analysis.GetMovementAsync(fromMonth, toMonth, asOf, leadTimeDays, serviceLevelZ, topClients, ct);

    /// <summary>Series para gráficos: venta por mes (total y por línea), venta vs. cuota por representante, participación por línea, unidades de los N principales y cobertura.</summary>
    [HttpGet("charts"), RequirePermission(View)]
    public Task<SalesChartsDto> Charts([FromQuery] string? fromMonth, [FromQuery] string? toMonth, [FromQuery] string? month, [FromQuery] int? topProducts, CancellationToken ct)
        => analysis.GetChartsAsync(fromMonth, toMonth, month, topProducts, ct);

    /// <summary>Estado: fecha de corte, lotes importados, clientes sin representante, marcas sin línea, nombres del historial sin emparejar y traslape de fuentes.</summary>
    [HttpGet("status"), RequirePermission(View)]
    public Task<SalesAnalysisStatusDto> Status(CancellationToken ct) => analysis.GetStatusAsync(ct);

    // ---------------------------------------------------------------- ajustes

    [HttpGet("settings"), RequirePermission(View)]
    public Task<SalesAnalysisSettingsDto> Settings(CancellationToken ct) => setup.GetSettingsAsync(ct);

    /// <summary>Fecha de corte (clearCutoff la quita), días de entrega y nivel de servicio (z) del punto de reorden sugerido.</summary>
    [HttpPut("settings"), RequirePermission(Manage)]
    public Task<SalesAnalysisSettingsDto> UpdateSettings([FromBody] SalesAnalysisSettingsRequest req, CancellationToken ct) => setup.UpdateSettingsAsync(req, ct);

    // ---------------------------------------------------------------- rutas

    [HttpGet("routes"), RequirePermission(View)]
    public Task<IReadOnlyList<SalesRouteDto>> Routes([FromQuery] bool includeInactive, CancellationToken ct) => setup.ListRoutesAsync(includeInactive, ct);

    [HttpPost("routes"), RequirePermission(Manage)]
    public Task<SalesRouteDto> CreateRoute([FromBody] SalesRouteRequest req, CancellationToken ct) => setup.CreateRouteAsync(req, ct);

    [HttpPatch("routes/{id:int}"), RequirePermission(Manage)]
    public Task<SalesRouteDto> UpdateRoute(int id, [FromBody] SalesRouteRequest req, CancellationToken ct) => setup.UpdateRouteAsync(id, req, ct);

    /// <summary>Baja lógica: 409 si tiene representantes activos.</summary>
    [HttpPost("routes/{id:int}/deactivate"), RequirePermission(Manage)]
    public Task<SalesRouteDto> DeactivateRoute(int id, CancellationToken ct) => setup.SetRouteActiveAsync(id, false, ct);

    [HttpPost("routes/{id:int}/reactivate"), RequirePermission(Manage)]
    public Task<SalesRouteDto> ReactivateRoute(int id, CancellationToken ct) => setup.SetRouteActiveAsync(id, true, ct);

    // ---------------------------------------------------------------- representantes

    [HttpGet("reps"), RequirePermission(View)]
    public Task<IReadOnlyList<SalesRepDto>> Reps([FromQuery] bool includeInactive, CancellationToken ct) => setup.ListRepsAsync(includeInactive, ct);

    [HttpPost("reps"), RequirePermission(Manage)]
    public Task<SalesRepDto> CreateRep([FromBody] SalesRepRequest req, CancellationToken ct) => setup.CreateRepAsync(req, ct);

    [HttpPatch("reps/{id:int}"), RequirePermission(Manage)]
    public Task<SalesRepDto> UpdateRep(int id, [FromBody] SalesRepRequest req, CancellationToken ct) => setup.UpdateRepAsync(id, req, ct);

    /// <summary>Baja lógica: 409 si tiene clientes activos asignados.</summary>
    [HttpPost("reps/{id:int}/deactivate"), RequirePermission(Manage)]
    public Task<SalesRepDto> DeactivateRep(int id, CancellationToken ct) => setup.SetRepActiveAsync(id, false, ct);

    [HttpPost("reps/{id:int}/reactivate"), RequirePermission(Manage)]
    public Task<SalesRepDto> ReactivateRep(int id, CancellationToken ct) => setup.SetRepActiveAsync(id, true, ct);

    /// <summary>Pasa los valores del campo personalizado «Representante» (sales_rep) a representantes y enlaza los clientes sin representante (idempotente).</summary>
    [HttpPost("reps/from-custom-field"), RequirePermission(Manage)]
    public Task<SalesRepMigrationDto> RepsFromCustomField(CancellationToken ct) => setup.RepsFromCustomFieldAsync(ct);

    [HttpGet("reps/{id:int}/quotas"), RequirePermission(View)]
    public Task<SalesQuotaHistoryDto> RepQuotas(int id, CancellationToken ct) => setup.GetRepQuotasAsync(id, ct);

    // ---------------------------------------------------------------- cuotas

    /// <summary>Cuotas de un mes (por omisión el mes en curso): la propia de cada representante y la vigente.</summary>
    [HttpGet("quotas"), RequirePermission(View)]
    public Task<SalesQuotaMonthDto> Quotas([FromQuery] string? month, CancellationToken ct) => setup.GetQuotasAsync(month, ct);

    /// <summary>Edición en bloque de un mes: items [{repId, amount}] (amount null = quitar la propia del mes).</summary>
    [HttpPut("quotas/{month}"), RequirePermission(Manage)]
    public Task<SalesQuotaMonthDto> SetQuotas(string month, [FromBody] SalesQuotaBulkRequest req, CancellationToken ct) => setup.SetQuotasAsync(month, req, ct);

    // ---------------------------------------------------------------- líneas y marcas

    [HttpGet("lines"), RequirePermission(View)]
    public Task<IReadOnlyList<ProductLineDto>> Lines([FromQuery] bool includeInactive, CancellationToken ct) => setup.ListLinesAsync(includeInactive, ct);

    [HttpPost("lines"), RequirePermission(Manage)]
    public Task<ProductLineDto> CreateLine([FromBody] ProductLineRequest req, CancellationToken ct) => setup.CreateLineAsync(req, ct);

    [HttpPatch("lines/{id:int}"), RequirePermission(Manage)]
    public Task<ProductLineDto> UpdateLine(int id, [FromBody] ProductLineRequest req, CancellationToken ct) => setup.UpdateLineAsync(id, req, ct);

    [HttpPost("lines/{id:int}/deactivate"), RequirePermission(Manage)]
    public Task<ProductLineDto> DeactivateLine(int id, CancellationToken ct) => setup.SetLineActiveAsync(id, false, ct);

    [HttpPost("lines/{id:int}/reactivate"), RequirePermission(Manage)]
    public Task<ProductLineDto> ReactivateLine(int id, CancellationToken ct) => setup.SetLineActiveAsync(id, true, ct);

    /// <summary>Reemplaza las marcas de la línea (409 si una marca ya está en otra línea).</summary>
    [HttpPut("lines/{id:int}/brands"), RequirePermission(Manage)]
    public Task<ProductLineDto> SetLineBrands(int id, [FromBody] ProductLineBrandsRequest req, CancellationToken ct) => setup.SetLineBrandsAsync(id, req, ct);

    /// <summary>Las marcas de los productos con su línea efectiva (las que no tienen línea primero).</summary>
    [HttpGet("brands"), RequirePermission(View)]
    public Task<IReadOnlyList<SalesBrandDto>> Brands(CancellationToken ct) => setup.ListBrandsAsync(ct);

    // ---------------------------------------------------------------- clientes ↔ representante

    /// <summary>Clientes activos con su representante; repId filtra por representante y unassigned = true los que no tienen.</summary>
    [HttpGet("clients"), RequirePermission(View)]
    public Task<PagedResult<SalesClientRepDto>> Clients([FromQuery] int? repId, [FromQuery] bool unassigned, [FromQuery] string? q, [FromQuery] int skip = 0,
        [FromQuery] int take = 100, CancellationToken ct = default)
        => setup.ListClientsAsync(repId, unassigned, q, skip, take, ct);

    /// <summary>Asigna (o quita con repId null) el representante de un cliente.</summary>
    [HttpPut("clients/{clientId:int}/rep"), RequirePermission(Manage)]
    public Task<SalesClientRepBulkResultDto> AssignClient(int clientId, [FromBody] SalesClientRepRequest req, CancellationToken ct) => setup.AssignClientAsync(clientId, req, ct);

    /// <summary>Asigna (o quita) el representante de varios clientes (hasta 2000).</summary>
    [HttpPost("clients/assign-rep"), RequirePermission(Manage)]
    public Task<SalesClientRepBulkResultDto> AssignClients([FromBody] SalesClientRepBulkRequest req, CancellationToken ct) => setup.AssignClientsAsync(req, ct);

    // ---------------------------------------------------------------- historial

    /// <summary>Enlaza a mano un nombre crudo del historial (kind CLIENT o PRODUCT) a un cliente o producto: re-enlaza las líneas y queda para las próximas importaciones.</summary>
    [HttpPost("unmatched/link"), RequirePermission(Manage)]
    public Task<SalesAliasResultDto> Link([FromBody] SalesAliasRequest req, CancellationToken ct) => setup.LinkAsync(req, ct);

    /// <summary>
    /// Importar el historial de QuickBooks subiendo los archivos (multipart/form-data): salesFile (Sales by Item Detail, CSV) y/o purchasesFile (Purchases by Item Detail,
    /// CSV), dryRun (por omisión true: simula y no escribe), cutoffDate opcional (se guarda como fecha de corte) y repsFromCustomField. Hasta 20 MB por archivo.
    /// </summary>
    [HttpPost("import"), Consumes("multipart/form-data"), RequirePermission(Manage)]
    [RequestSizeLimit(45 * 1024 * 1024)]
    public async Task<SalesHistoryImportReportDto> Import(IFormFile? salesFile, IFormFile? purchasesFile, [FromForm] bool? dryRun, [FromForm] DateOnly? cutoffDate,
        [FromForm] bool? repsFromCustomField, CancellationToken ct)
    {
        var files = new List<SalesHistoryFile>();
        if (salesFile is { Length: > 0 }) files.Add(new SalesHistoryFile(salesFile.FileName, await ReadAsync(salesFile, ct), SalesAnalysisCodes.KindSales));
        if (purchasesFile is { Length: > 0 }) files.Add(new SalesHistoryFile(purchasesFile.FileName, await ReadAsync(purchasesFile, ct), SalesAnalysisCodes.KindPurchases));
        if (files.Count == 0) throw new ValidationException("files", SalesAnalysisRules.FileRequired);
        return await importer.ImportAsync(null, files, dryRun ?? true, cutoffDate, repsFromCustomField ?? false, SalesAnalysisCodes.ImportApi, ct);
    }

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > MaxFileBytes) throw new ValidationException("files", SalesAnalysisRules.FileTooLarge);
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
