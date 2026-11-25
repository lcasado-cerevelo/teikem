using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 245 (Lote B del plan de rutas, cadenas y precios) — cadenas de clientes (Walgreens, CVS…): lista, ficha y clientes de la cadena; crear, editar, desactivar y
/// reactivar (nunca DELETE); asignar y quitar clientes; cambiar la cadena de un cliente desde su ficha y el alta masiva por el inicio del nombre (simular y aplicar
/// con la huella). Módulo Órdenes de venta. Ver con sales.chain|clients.read|sales.view (para que los filtros de Clientes y Órdenes funcionen para quien ya ve esas
/// pantallas); administrar con sales.chain. El TenantId sale de la sesión; ninguna acción lo recibe.
/// </summary>
[ApiController]
[Route("api/v1/client-chains")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public sealed class ClientChainsController(ClientChainService chains) : ControllerBase
{
    private const string View = PermissionCatalog.SalesChain + "|" + PermissionCatalog.ClientsRead + "|" + PermissionCatalog.SalesView;
    private const string Manage = PermissionCatalog.SalesChain;

    // ---------------------------------------------------------------- lectura

    /// <summary>Cadenas de la compañía (includeInactive = también las desactivadas), con buscador por nombre. clientCount = clientes activos.</summary>
    [HttpGet(""), RequirePermission(View)]
    public Task<IReadOnlyList<ClientChainListItemDto>> List([FromQuery] bool includeInactive = false, [FromQuery] string? q = null, CancellationToken ct = default)
        => chains.ListAsync(includeInactive, q, ct);

    /// <summary>Ficha de la cadena con sus conteos de clientes y el rowVersion.</summary>
    [HttpGet("{publicId:guid}"), RequirePermission(View)]
    public Task<ClientChainDetailDto> Get(Guid publicId, CancellationToken ct) => chains.GetAsync(publicId, ct);

    /// <summary>Clientes de la cadena, paginados (take hasta 500); all = true los trae todos (exportar u ordenar la consulta completa).</summary>
    [HttpGet("{publicId:guid}/clients"), RequirePermission(View)]
    public Task<PagedResult<ClientChainClientDto>> Clients(Guid publicId, [FromQuery] string? q = null, [FromQuery] bool includeInactive = false,
        [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] bool all = false, CancellationToken ct = default)
        => chains.ListClientsAsync(publicId, q, includeInactive, skip, take, all, ct);

    // ---------------------------------------------------------------- administrar

    /// <summary>Crea una cadena (nombre único entre las activas; notas opcionales).</summary>
    [HttpPost(""), RequirePermission(Manage)]
    public Task<ClientChainDetailDto> Create([FromBody] ClientChainRequest req, CancellationToken ct) => chains.CreateAsync(req, ct);

    /// <summary>Edita nombre y notas (clearNotes las borra); rowVersion opcional (409 si cambió).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(Manage)]
    public Task<ClientChainDetailDto> Update(Guid publicId, [FromBody] ClientChainRequest req, CancellationToken ct) => chains.UpdateAsync(publicId, req, ct);

    /// <summary>Desactiva la cadena (409 si tiene clientes activos).</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(Manage)]
    public Task<ClientChainDetailDto> Deactivate(Guid publicId, CancellationToken ct) => chains.SetActiveAsync(publicId, false, ct);

    /// <summary>Reactiva la cadena (409 si otra activa ya tiene ese nombre).</summary>
    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(Manage)]
    public Task<ClientChainDetailDto> Reactivate(Guid publicId, CancellationToken ct) => chains.SetActiveAsync(publicId, true, ct);

    /// <summary>Asigna clientes a la cadena (hasta 2000); los de otra cadena se saltan salvo moveFromOtherChains.</summary>
    [HttpPost("{publicId:guid}/clients"), RequirePermission(Manage)]
    public Task<ClientChainAssignResultDto> Assign(Guid publicId, [FromBody] ClientChainAssignRequest req, CancellationToken ct) => chains.AssignAsync(publicId, req, ct);

    /// <summary>Quita clientes de la cadena (los que no están en ella cuentan en notInChain).</summary>
    [HttpPost("{publicId:guid}/clients/remove"), RequirePermission(Manage)]
    public Task<ClientChainAssignResultDto> Remove(Guid publicId, [FromBody] ClientChainRemoveRequest req, CancellationToken ct) => chains.RemoveAsync(publicId, req, ct);

    /// <summary>Cambia (o quita, chainPublicId null) la cadena de un cliente desde su ficha; puede mover entre cadenas.</summary>
    [HttpPut("by-client/{clientPublicId:guid}"), RequirePermission(Manage)]
    public Task<ClientChainMembershipDto> SetClientChain(Guid clientPublicId, [FromBody] ClientChainSetRequest req, CancellationToken ct)
        => chains.SetClientChainAsync(clientPublicId, req, ct);

    /// <summary>Simula el alta masiva por el inicio del nombre (no escribe nada): acciones, conteos y la huella.</summary>
    [HttpPost("{publicId:guid}/bulk-assign/preview"), RequirePermission(Manage)]
    public Task<ClientChainBulkPreviewDto> BulkPreview(Guid publicId, [FromBody] ClientChainBulkRequest req, CancellationToken ct) => chains.PreviewBulkAsync(publicId, req, ct);

    /// <summary>Aplica el alta masiva con la huella de la simulación (409 si los clientes cambiaron: vuelva a simular).</summary>
    [HttpPost("{publicId:guid}/bulk-assign/apply"), RequirePermission(Manage)]
    public Task<ClientChainBulkResultDto> BulkApply(Guid publicId, [FromBody] ClientChainBulkRequest req, CancellationToken ct) => chains.ApplyBulkAsync(publicId, req, ct);
}
