using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// 2026-10-08 — Daños: algo que llegó dañado en un recibo o se dañó en el almacén. Se reporta (se manda a cuarentena o se desecha de una vez), y lo que
/// está en cuarentena se desecha después o se recupera a una posición de guardado. Todo bajo el módulo WMS_LOTSERIAL y el permiso warehouse.damage. Los
/// movimientos de inventario son del ledger (ajuste con motivo Daño o transferencia); aquí solo queda el reporte y su estatus (REPORTED → QUARANTINED →
/// DISCARDED | RECOVERED; REPORTED → DISCARDED). Historial: /api/v1/status/history/DAMAGE_REPORT/{id}.
/// </summary>
[ApiController]
[Route("api/v1/damage-reports")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class DamageReportsController(DamageService damages) : ControllerBase
{
    /// <summary>Daños activos, del más reciente al más antiguo. Filtros: status (REPORTED, QUARANTINED, DISCARDED, RECOVERED; varios separados por coma), origin (RECEIPT o WAREHOUSE), q (SKU, nombre o DAN-#####).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.WarehouseDamage)]
    public Task<DamageReportPageDto> List([FromQuery] string? status, [FromQuery] string? origin, [FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => damages.ListAsync(status, origin, q, skip, take, ct);

    [HttpGet("{id:int}"), RequirePermission(PermissionCatalog.WarehouseDamage)]
    public Task<DamageReportDto> Get(int id, CancellationToken ct) => damages.GetAsync(id, ct);

    /// <summary>
    /// Reporta un daño y lo manda a cuarentena (QUARANTINE) o lo desecha de una vez (DISCARD). Origen RECEIPT: indique el recibo; esas unidades no entran como
    /// buenas (cuarentena = entran a la posición de cuarentena; desechar de una vez = no entran). Origen WAREHOUSE: indique la posición donde estaba.
    /// 400 por campo; 404 producto, recibo, posición o lote; 409 sin existencia suficiente (insufficient_stock); 422 serie, o almacén sin posición de cuarentena.
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseDamage)]
    public Task<DamageReportDto> Report([FromBody] DamageReportRequest req, CancellationToken ct) => damages.ReportAsync(req, ct);

    /// <summary>Desecha lo que está en cuarentena (ajuste negativo con motivo Daño). Solo desde QUARANTINED → 422 'Solo se resuelve un daño que está en cuarentena.'.</summary>
    [HttpPost("{id:int}/discard"), RequirePermission(PermissionCatalog.WarehouseDamage)]
    public Task<DamageReportDto> Discard(int id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DamageResolveRequest? req, CancellationToken ct)
        => damages.DiscardAsync(id, req, ct);

    /// <summary>Recupera lo que está en cuarentena a una posición de guardado (toBinId; nunca de cuarentena, recepción, cruce de muelle ni renta).</summary>
    [HttpPost("{id:int}/recover"), RequirePermission(PermissionCatalog.WarehouseDamage)]
    public Task<DamageReportDto> Recover(int id, [FromBody] DamageResolveRequest req, CancellationToken ct) => damages.RecoverAsync(id, req, ct);
}
