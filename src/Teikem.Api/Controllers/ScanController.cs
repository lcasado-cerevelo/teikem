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
public sealed class ScanController(OutboundScanService scanner) : ControllerBase
{
    /// <summary>Escaneo de salida: { code, planDate? } (sin fecha = hoy UTC).</summary>
    [HttpPost("outbound"), RequirePermission(PermissionCatalog.TripsScan)]
    public Task<OutboundScanResultDto> Outbound([FromBody] OutboundScanRequest req, CancellationToken ct)
        => scanner.ScanAsync(req, ct);
}
