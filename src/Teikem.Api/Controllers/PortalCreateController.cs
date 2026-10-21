using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 156 — lo que el cliente crea desde su portal de almacén: productos propios, avisos de llegada y avisos de salida (más verlos y cancelarlos). Cada cosa la enciende el administrador de la compañía por cliente
/// (403 con el mensaje si está apagada) y solo los accesos ADMIN u OPERADOR del cliente la usan. Reglas y mensajes son los del personal (mismos servicios); todo a nombre del cliente de la sesión.
/// </summary>
[ApiController]
[Route("api/v1/portal/warehouse")]
[Authorize]
[RequireModule(ModuleKeys.ClientPortal)]
public sealed class PortalCreateController(PortalCreateService create) : ControllerBase
{
    /// <summary>Los almacenes activos de la compañía, para elegir en un aviso.</summary>
    [HttpGet("warehouses"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<IReadOnlyList<PortalWarehouseOptionDto>> Warehouses(CancellationToken ct) => create.WarehousesAsync(ct);

    /// <summary>Crea un producto propio del cliente (si la compañía se lo permite).</summary>
    [HttpPost("products"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalProductDto> CreateProduct([FromBody] PortalProductCreateRequest req, CancellationToken ct) => create.CreateProductAsync(req, ct);

    /// <summary>Edita un producto que el cliente ya creó (nombre, código de barras, marca, modelo, peso, volumen, costo). Producto de otro cliente → 404; sin cambios → 400.</summary>
    [HttpPatch("products/{publicId:guid}"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalProductDto> UpdateProduct(Guid publicId, [FromBody] PortalProductPatchRequest req, CancellationToken ct) => create.UpdateProductAsync(publicId, req, ct);

    [HttpGet("arrivals"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<IReadOnlyList<PortalArrivalDto>> Arrivals([FromQuery] string? search, CancellationToken ct) => create.ListArrivalsAsync(search, ct);

    [HttpPost("arrivals"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalArrivalDto> CreateArrival([FromBody] PortalArrivalRequest req, CancellationToken ct) => create.CreateArrivalAsync(req, ct);

    [HttpPatch("arrivals/{id:int}"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalArrivalDto> UpdateArrival(int id, [FromBody] PortalArrivalRequest req, CancellationToken ct) => create.UpdateArrivalAsync(id, req, ct);

    [HttpPost("arrivals/{id:int}/cancel"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalArrivalDto> CancelArrival(int id, CancellationToken ct) => create.CancelArrivalAsync(id, ct);

    [HttpGet("exits"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalExitPageDto> Exits([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => create.ListExitsAsync(status, search, skip, take, ct);

    [HttpGet("exits/{publicId:guid}"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalExitDto> Exit(Guid publicId, CancellationToken ct) => create.GetExitAsync(publicId, ct);

    [HttpPost("exits"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalExitDto> CreateExit([FromBody] PortalExitRequest req, CancellationToken ct) => create.CreateExitAsync(req, ct);

    [HttpPatch("exits/{publicId:guid}"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalExitDto> UpdateExit(Guid publicId, [FromBody] PortalExitHeaderRequest req, CancellationToken ct) => create.UpdateExitAsync(publicId, req, ct);

    [HttpPatch("exits/{publicId:guid}/lines/{lineId:int}"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalExitDto> ModifyExitLine(Guid publicId, int lineId, [FromBody] PortalExitLineModifyRequest req, CancellationToken ct) => create.ModifyExitLineAsync(publicId, lineId, req, ct);

    [HttpPost("exits/{publicId:guid}/cancel"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalExitDto> CancelExit(Guid publicId, [FromBody] PortalCancelRequest? req, CancellationToken ct) => create.CancelExitAsync(publicId, req?.Comment, ct);
}

public sealed record PortalCancelRequest(string? Comment = null);
