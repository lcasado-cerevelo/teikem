using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Accounting;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 181 (H1) — contabilización de despachos (módulo de almacén): el costo congelado de lo que salió del inventario propio por recolecciones y despachos. Ver con billing.view; generar, aceptar y cancelar con billing.generate; exportar con billing.export. Flujo: generada → aceptada → exportada. Teikem junta el costo ya congelado de lo
/// PROPIO (lo de un cliente no se contabiliza) y exporta con una plantilla; no emite el asiento en el sistema contable.
/// </summary>
[ApiController]
[Route("api/v1/dispatch-accounting")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class DispatchAccountingController(AccountingRunService runs) : ControllerBase
{
    private const string Kind = AccountingCodes.KindDispatches;

    /// <summary>Las corridas, de la más reciente a la más antigua. Filtros: status (GENERATED, ACCEPTED, EXPORTED, CANCELLED) y q (número).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.BillingView)]
    public Task<PagedResult<AccountingRunListItemDto>> List([FromQuery] string? status, [FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => runs.ListAsync(Kind, status, q, skip, take, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.BillingView)]
    public Task<AccountingRunDto> Get(Guid publicId, CancellationToken ct) => runs.GetAsync(Kind, publicId, ct);

    /// <summary>Lo que entraría en la corrida del período (desde y hasta, días de la compañía); no guarda nada.</summary>
    [HttpPost("preview"), RequirePermission(PermissionCatalog.BillingView)]
    public Task<AccountingPreviewDto> Preview([FromBody] AccountingRunRequest req, CancellationToken ct) => runs.PreviewAsync(Kind, req, ct);

    /// <summary>Genera la corrida con lo propio del período que no esté ya en otra corrida viva; sin nada → 422.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.BillingGenerate + "|" + PermissionCatalog.DispatchAccountingManage)]
    public Task<AccountingRunDto> Generate([FromBody] AccountingRunRequest req, CancellationToken ct) => runs.GenerateAsync(Kind, req, ct);

    /// <summary>GENERADA → ACEPTADA (la corrida queda congelada).</summary>
    [HttpPost("{publicId:guid}/accept"), RequirePermission(PermissionCatalog.BillingGenerate + "|" + PermissionCatalog.DispatchAccountingManage)]
    public Task<AccountingRunDto> Accept(Guid publicId, CancellationToken ct) => runs.AcceptAsync(Kind, publicId, ct);

    /// <summary>Cancela una corrida generada o aceptada; sus líneas quedan libres para otra corrida.</summary>
    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.BillingGenerate + "|" + PermissionCatalog.DispatchAccountingManage)]
    public Task<AccountingRunDto> Cancel(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AccountingRunCancelRequest? req, CancellationToken ct)
        => runs.CancelAsync(Kind, publicId, req?.Comment, ct);

    /// <summary>El archivo de la corrida (una fila por línea) con una plantilla del alcance DISPATCH_ACCOUNTING, sin cambiar nada; sin plantilla, la primera activa.</summary>
    [HttpGet("{publicId:guid}/export-preview"), RequirePermission(PermissionCatalog.BillingExport + "|" + PermissionCatalog.DispatchAccountingManage)]
    public Task<ExportFileDto> ExportPreview(Guid publicId, [FromQuery] Guid? templatePublicId, CancellationToken ct) => runs.PreviewExportAsync(Kind, publicId, templatePublicId, ct);

    /// <summary>Exporta: arma el archivo y pasa la corrida aceptada a exportada.</summary>
    [HttpPost("{publicId:guid}/export"), RequirePermission(PermissionCatalog.BillingExport + "|" + PermissionCatalog.DispatchAccountingManage)]
    public Task<ExportFileDto> Export(Guid publicId, [FromQuery] Guid? templatePublicId, CancellationToken ct) => runs.ExportAsync(Kind, publicId, templatePublicId, ct);
}
