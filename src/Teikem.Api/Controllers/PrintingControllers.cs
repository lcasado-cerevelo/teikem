using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 43 (F-G1) — Sistema → Impresoras y labels: impresoras de la compañía (perfil de impresión: uso, tamaño, girar,
/// predeterminada). Módulo SYSTEM (núcleo, siempre encendido); todo con printers.manage. Rutas por PublicId; nunca DELETE.
/// El servidor nunca se conecta a una impresora: el navegador imprime directo con QZ Tray o con el PDF del navegador.
/// </summary>
[ApiController]
[Route("api/v1/printers")]
[Authorize]
[RequireModule(ModuleKeys.System)]
public sealed class PrintersController(PrinterService printers) : ControllerBase
{
    /// <summary>Impresoras de la compañía (por defecto solo las activas), por uso y nombre.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<IReadOnlyList<PrinterDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => printers.ListAsync(includeInactive, ct);

    /// <summary>Alta: nombre y uso obligatorios; la primera activa de su uso nace predeterminada. 409 si el nombre ya lo usa otra activa.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<PrinterDto> Create([FromBody] PrinterRequest req, CancellationToken ct) => printers.CreateAsync(req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<PrinterDto> Get(Guid publicId, CancellationToken ct) => printers.GetAsync(publicId, ct);

    /// <summary>Edición: null = sin cambio; el uso no se cambia (400); rowVersion opcional (409 si cambió).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<PrinterDto> Update(Guid publicId, [FromBody] PrinterRequest req, CancellationToken ct) => printers.UpdateAsync(publicId, req, ct);

    /// <summary>Baja lógica; si era la predeterminada, el uso queda sin predeterminada (aplica la integrada).</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<PrinterDto> Deactivate(Guid publicId, CancellationToken ct) => printers.SetActiveAsync(publicId, false, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<PrinterDto> Reactivate(Guid publicId, CancellationToken ct) => printers.SetActiveAsync(publicId, true, ct);

    /// <summary>La hace predeterminada de su uso (quita la anterior). 409 si está inactiva.</summary>
    [HttpPost("{publicId:guid}/make-default"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<PrinterDto> MakeDefault(Guid publicId, CancellationToken ct) => printers.MakeDefaultAsync(publicId, ct);
}

/// <summary>
/// Lote 43 (F-G1) — plantillas de label (constructor del label: elementos, posición en 9 zonas y tamaño). Módulo SYSTEM;
/// todo con printers.manage. Rutas por PublicId; nunca DELETE.
/// </summary>
[ApiController]
[Route("api/v1/label-templates")]
[Authorize]
[RequireModule(ModuleKeys.System)]
public sealed class LabelTemplatesController(LabelTemplateService templates) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<IReadOnlyList<LabelTemplateDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => templates.ListAsync(includeInactive, ct);

    /// <summary>Alta: nombre y construcción obligatorios (400 con el mensaje exacto si la construcción no vale).</summary>
    [HttpPost, RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<LabelTemplateDto> Create([FromBody] LabelTemplateRequest req, CancellationToken ct) => templates.CreateAsync(req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<LabelTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ct);

    /// <summary>Edición: null = sin cambio; rowVersion opcional (409 si cambió).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<LabelTemplateDto> Update(Guid publicId, [FromBody] LabelTemplateRequest req, CancellationToken ct) => templates.UpdateAsync(publicId, req, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<LabelTemplateDto> Deactivate(Guid publicId, CancellationToken ct) => templates.SetActiveAsync(publicId, false, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<LabelTemplateDto> Reactivate(Guid publicId, CancellationToken ct) => templates.SetActiveAsync(publicId, true, ct);

    [HttpPost("{publicId:guid}/make-default"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<LabelTemplateDto> MakeDefault(Guid publicId, CancellationToken ct) => templates.MakeDefaultAsync(publicId, ct);
}

/// <summary>
/// Lote 43 (F-G1) — impresión de una estación y ajustes de impresión de la compañía. Módulo SYSTEM.
/// - station-options: impresoras y plantillas activas, el label integrado, «Imprimir labels directo» y si QZ Tray tiene firma. Lo lee
///   quien imprime (órdenes, escaneo, rutas, inventario) o administra impresoras.
/// - settings: «Imprimir labels directo a la impresora» (printers.manage).
/// - qz/certificate y qz/sign: certificado público y firma SHA512withRSA de lo que pide QZ Tray en la computadora del usuario (mismo
///   permiso que station-options). La clave privada nunca sale del servidor; el servidor nunca se conecta a QZ Tray ni a una impresora.
/// </summary>
[ApiController]
[Route("api/v1/printing")]
[Authorize]
[RequireModule(ModuleKeys.System)]
public sealed class PrintingController(PrintingOptionsService options, QzSigningService qz) : ControllerBase
{
    /// <summary>Permisos que pueden leer las opciones de impresión y pedir firmas de QZ Tray (cualquiera de).</summary>
    public const string StationOptionsPermission = PermissionCatalog.OrdersView + "|" + PermissionCatalog.TripsScan + "|" + PermissionCatalog.TripsView
                                                   + "|" + PermissionCatalog.InventoryView + "|" + PermissionCatalog.PrintersManage;

    [HttpGet("station-options"), RequirePermission(StationOptionsPermission)]
    public Task<PrintStationOptionsDto> StationOptions(CancellationToken ct) => options.GetStationOptionsAsync(ct);

    [HttpGet("settings"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<PrintingSettingsDto> Settings(CancellationToken ct) => options.GetSettingsAsync(ct);

    /// <summary>«Imprimir labels directo a la impresora» de la compañía (null = sin cambio).</summary>
    [HttpPut("settings"), RequirePermission(PermissionCatalog.PrintersManage)]
    public Task<PrintingSettingsDto> UpdateSettings([FromBody] PrintingSettingsRequest req, CancellationToken ct) => options.UpdateSettingsAsync(req, ct);

    /// <summary>Certificado público de QZ Tray; 409 con un mensaje claro si el servidor no tiene certificado y clave.</summary>
    [HttpGet("qz/certificate"), RequirePermission(StationOptionsPermission)]
    public QzCertificateDto QzCertificate() => qz.GetCertificate();

    /// <summary>Firma lo que pide QZ Tray (máximo 8192 caracteres). 409 si no hay certificado y clave.</summary>
    [HttpPost("qz/sign"), RequirePermission(StationOptionsPermission)]
    public QzSignatureDto QzSign([FromBody] QzSignRequest req) => qz.Sign(req);
}
