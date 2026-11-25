using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — personas de la ruta de venta (titular y apoyo, con historial; cerrar y anular, nunca DELETE), la ruta y el vendedor
/// preferido de cada cliente (uno a uno y en bloque) y «Pasar rutas desde representantes» (con simulación). Módulo Órdenes de venta; leer con
/// sales.analysis.view|manage y escribir con sales.analysis.manage. El TenantId sale de la sesión. Comparte el prefijo /api/v1/sales-analysis con el Lote 241
/// (los ids llevan :int, así no chocan con sus rutas).
/// </summary>
[ApiController]
[Route("api/v1/sales-analysis")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public sealed class SalesRoutesController(SalesRouteService routes) : ControllerBase
{
    private const string View = PermissionCatalog.SalesAnalysisView + "|" + PermissionCatalog.SalesAnalysisManage;
    private const string Manage = PermissionCatalog.SalesAnalysisManage;

    // ---------------------------------------------------------------- personas de la ruta

    /// <summary>Titular y apoyos vigentes de la ruta; history = true agrega el historial cerrado e includeVoided las anuladas (auditoría).</summary>
    [HttpGet("routes/{id:int}/people"), RequirePermission(View)]
    public Task<SalesRoutePeopleDto> People(int id, [FromQuery] bool history = false, [FromQuery] bool includeVoided = false, CancellationToken ct = default)
        => routes.GetPeopleAsync(id, history, includeVoided, ct);

    /// <summary>Pone a una persona en la ruta: role PRIMARY (titular; cierra al anterior) o SUPPORT (apoyo); validFrom por omisión hoy.</summary>
    [HttpPost("routes/{id:int}/people"), RequirePermission(Manage)]
    public Task<SalesRoutePeopleDto> Assign(int id, [FromBody] SalesRouteAssignRequest req, CancellationToken ct) => routes.AssignAsync(id, req, ct);

    /// <summary>Cierra una asignación vigente (validTo por omisión hoy).</summary>
    [HttpPost("routes/{id:int}/people/{assignmentId:int}/close"), RequirePermission(Manage)]
    public Task<SalesRoutePeopleDto> Close(int id, int assignmentId, [FromBody] SalesRouteCloseRequest? req, CancellationToken ct)
        => routes.CloseAsync(id, assignmentId, req, ct);

    /// <summary>Anula una asignación registrada por error: deja de contar en el historial y en la atribución.</summary>
    [HttpPost("routes/{id:int}/people/{assignmentId:int}/void"), RequirePermission(Manage)]
    public Task<SalesRoutePeopleDto> Void(int id, int assignmentId, CancellationToken ct) => routes.VoidAsync(id, assignmentId, ct);

    /// <summary>Personas activas de la compañía para ponerlas en una ruta, con buscador por nombre o correo (take hasta 50).</summary>
    [HttpGet("sellers/candidates"), RequirePermission(Manage)]
    public Task<IReadOnlyList<SalesSellerCandidateDto>> Candidates([FromQuery] string? q, [FromQuery] int take = 20, CancellationToken ct = default)
        => routes.ListCandidatesAsync(q, take, ct);

    // ---------------------------------------------------------------- venta del cliente

    /// <summary>Ruta (directa o heredada), titular, apoyos, vendedor preferido, vendedor propuesto y representante heredado del cliente.</summary>
    [HttpGet("clients/{clientId:int}/sales"), RequirePermission(View)]
    public Task<SalesClientSalesDto> ClientSales(int clientId, CancellationToken ct) => routes.GetClientSalesAsync(clientId, ct);

    /// <summary>Cambia la ruta directa y el vendedor preferido del cliente (el preferido debe ser persona activa de su ruta).</summary>
    [HttpPut("clients/{clientId:int}/sales"), RequirePermission(Manage)]
    public Task<SalesClientSalesDto> SetClientSales(int clientId, [FromBody] SalesClientSalesRequest req, CancellationToken ct)
        => routes.SetClientSalesAsync(clientId, req, ct);

    /// <summary>Pone (o quita con routeId null) la ruta directa de varios clientes (hasta 2000).</summary>
    [HttpPost("clients/assign-route"), RequirePermission(Manage)]
    public Task<SalesClientRouteBulkResultDto> AssignClientsRoute([FromBody] SalesClientRouteBulkRequest req, CancellationToken ct)
        => routes.AssignClientsRouteAsync(req, ct);

    // ---------------------------------------------------------------- pasar rutas desde representantes

    /// <summary>Clientes sin ruta → la de su representante; rutas sin titular → su único representante con usuario (varios: de apoyo). dryRun por omisión true.</summary>
    [HttpPost("routes/from-reps"), RequirePermission(Manage)]
    public Task<SalesRoutesFromRepsDto> RoutesFromReps([FromQuery] bool dryRun = true, CancellationToken ct = default) => routes.RoutesFromRepsAsync(dryRun, ct);
}
