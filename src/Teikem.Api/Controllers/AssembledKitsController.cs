using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 232 — KIT ARMADO (producto con existencia propia; módulo WMS_LOTSERIAL). El alta va por POST /products con isAssembledKit=true (ProductsController).
/// Aquí: la composición, el «se pueden armar» por almacén, armar y desarmar (inventory.adjust, como el ajuste de inventario de la web; solo web) y el historial.
/// Productos y armados por PublicId; posiciones y lotes por id entero, resueltos dentro de su almacén / producto filtrados.
/// </summary>
[ApiController]
[Route("api/v1/products/{publicId:guid}/assembled-kit")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class AssembledKitsController(AssembledKitService kits) : ControllerBase
{
    /// <summary>
    /// El kit armado: composición (piezas y cantidad por kit, con su rastreo), rastreo del kit, compositionLocked (ya tiene armados, movimientos o existencia: la
    /// composición no se cambia), «se pueden armar» por almacén activo (canAssemble = en UN armado con un solo lote por pieza; canAssembleTotal = juntando lotes) y
    /// la existencia del kit. Un producto que no es kit armado contesta isAssembledKit=false.
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<AssembledKitDto> Get(Guid publicId, CancellationToken ct) => kits.GetAsync(publicId, ct);

    /// <summary>
    /// Reemplaza la composición (y el rastreo que resulta: LOT si alguna pieza va por lote). 400: sin piezas, cantidad no positiva, repetida, el propio kit, otro kit,
    /// inactiva, de otro dueño o con serie; 404 pieza inexistente; 409 «…ya tiene armados, movimientos o existencia… Cree un kit nuevo.»; 422 si no es kit armado.
    /// </summary>
    [HttpPut, RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<AssembledKitDto> SetComposition(Guid publicId, [FromBody] AssembledKitSetRequest req, CancellationToken ct) => kits.SetCompositionAsync(publicId, req, ct);

    /// <summary>Historial de armados del kit (más reciente primero; take ≤ 200), cada uno con el lote y la cantidad de cada pieza y sus desarmados. Costos solo con inventory.value.</summary>
    [HttpGet("assemblies"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<KitAssemblyPageDto> Assemblies(Guid publicId, CancellationToken ct, [FromQuery] int skip = 0, [FromQuery] int take = 50)
        => kits.ListAssembliesAsync(publicId, skip, take, ct);

    /// <summary>
    /// Armar N kits (entero) en la posición toBinId: salen las piezas de lo disponible (por la rotación o del lote/posición escogidos; un solo lote por pieza) y entran
    /// los kits, en una transacción. 400 cantidad/posición/notas; 404 kit, posición o lote; 409 insufficient_stock (cuántos kits alcanzan); 422 no es kit armado, inactivo,
    /// sin piezas, pieza con serie o posición de cuarentena/renta/cross-dock.
    /// </summary>
    [HttpPost("assemblies"), RequirePermission(PermissionCatalog.InventoryAdjust)]
    public Task<KitAssemblyResultDto> Assemble(Guid publicId, [FromBody] KitAssembleRequest req, CancellationToken ct) => kits.AssembleAsync(publicId, req, ct);

    /// <summary>
    /// Desarmar N kits de un armado: solo kits disponibles (no reservados ni despachados); cada pieza vuelve a su lote, a toBinId o a la posición de donde salió, al costo
    /// con que salió. 400 cantidad/notas/posición; 404 kit, armado o posición; 409 quedan menos por desarmar o no hay tantos disponibles.
    /// </summary>
    [HttpPost("assemblies/{assemblyPublicId:guid}/disassemble"), RequirePermission(PermissionCatalog.InventoryAdjust)]
    public Task<KitDisassemblyResultDto> Disassemble(Guid publicId, Guid assemblyPublicId, [FromBody] KitDisassembleRequest req, CancellationToken ct)
        => kits.DisassembleAsync(publicId, assemblyPublicId, req, ct);
}
