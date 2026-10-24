using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 5 (P6): estación de escaneo Outbound. Módulo núcleo LTL_GROUND; permiso trips.scan (Despachador y Operador de
/// almacén). Usa el mismo lookup exacto de /orders/lookup (número &gt; empaque &gt; factura) y asigna la orden a la ruta abierta
/// del día de su zona. Responde siempre 200 con un resultado tipado (FOUND_ASSIGNED, FOUND_UNASSIGNED, ALREADY_ASSIGNED,
/// NOT_FOUND, NOT_ELIGIBLE) y la palabra de voz (found, dup, notfound); 400 solo si el código está vacío o es muy largo.
/// Nunca crea rutas ni cambia chofer, vehículo u OrderStatus. La solicitud no lleva TenantId ni ids internos.
/// </summary>
[ApiController]
[Route("api/v1/scan")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class ScanController(OutboundScanService scanner, ScanActionService actions, ScanLookupService lookup) : ControllerBase
{
    /// <summary>
    /// Lote 216: la lupa de la estación. Busca el número de una pieza (o, si no es de una pieza, el del envío, conduce, empaque o factura) y dice lo que haya en la base, también si está cancelado, final, dado de baja, perdido, en una
    /// reclamación o «Por verificar». Solo lee. 400 si el código está vacío o es muy largo.
    /// </summary>
    [HttpGet("lookup"), RequirePermission(PermissionCatalog.TripsScan)]
    public Task<ScanLookupDto> Lookup([FromQuery] string? code, CancellationToken ct) => lookup.LookupAsync(code, ct);

    /// <summary>Lote 110: «Cerrar conteo» de una orden: lo que no se escaneó en la acción en que va queda faltante «Por verificar».</summary>
    [HttpPost("close-count"), RequirePermission(PermissionCatalog.TripsScan)]
    public Task<ScanCloseCountResultDto> CloseCount([FromBody] ScanCloseCountRequest req, CancellationToken ct)
        => actions.CloseCountAsync(req, ct);

    /// <summary>Lote 107: escaneo con acción escogida { action: PICKUP|ARRIVE|ROUTE|DEPART, code, planDate? }; brinca estatus y registra los de en medio; si ya está más adelante o es final no hace nada.</summary>
    [HttpPost("action"), RequirePermission(PermissionCatalog.TripsScan)]
    public Task<ScanActionResultDto> Action([FromBody] ScanActionRequest req, CancellationToken ct)
        => actions.ApplyAsync(req, ct);

    /// <summary>Escaneo de salida: { code, planDate? } (sin fecha = hoy en la zona de la compañía).</summary>
    [HttpPost("outbound"), RequirePermission(PermissionCatalog.TripsScan)]
    public Task<OutboundScanResultDto> Outbound([FromBody] OutboundScanRequest req, CancellationToken ct)
        => scanner.ScanAsync(req, ct);
}
