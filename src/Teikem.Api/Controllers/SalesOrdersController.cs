using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Cuerpo opcional de cancelar: el motivo queda en el historial de estatus.</summary>
public sealed record SalesOrderCancelRequest(string? Comment = null);

/// <summary>
/// Lote 57 (P4, S1) — órdenes de venta (módulo SALES_ORDERS). Lectura con sales.view; crear y editar con sales.create; cancelar con sales.cancel; el precio
/// distinto del de lista exige además sales.price (lo valida el servicio). Las órdenes se exponen por PublicId y nunca se borran: se cancelan.
/// </summary>
[ApiController]
[Route("api/v1/sales-orders")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesOrdersController(SalesOrderService sales) : ControllerBase
{
    /// <summary>
    /// Lista paginada (más reciente primero). Filtros: status (DRAFT, REQUESTED, PARTIAL, SHIPPED, CANCELLED), cliente, buscador q (número, cliente, producto),
    /// solo con backorder y fechas. 2026-11-09: status, clientPublicId y productPublicId se pueden REPETIR (varios valores = cualquiera de ellos; un solo valor
    /// funciona como antes) y dateField dice qué fecha filtran from/to: REQUESTED (solicitada, por omisión) o REQUIRED (requerida).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.SalesView)]
    public Task<PagedResult<SalesOrderListItemDto>> List([FromQuery] string[]? status, [FromQuery] Guid[]? clientPublicId, [FromQuery] string? q, [FromQuery] bool backorderOnly,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct,
        [FromQuery] bool assignedToMe = false, [FromQuery] int? assignedUserId = null, [FromQuery] bool urgent = false,
        [FromQuery] Guid[]? productPublicId = null, [FromQuery] string? dateField = null)
        => sales.ListAsync(null, null, q, backorderOnly, from, to, skip, take, ct, assignedToMe, assignedUserId, urgent,   // Lote 222: urgent = solo urgentes
            statuses: status, clientPublicIds: clientPublicId, productPublicIds: productPublicId, dateField: dateField);

    /// <summary>Lote 195: asigna (o desasigna con userId null) una orden de venta solicitada o despachada en parte a quien la va a despachar. Supervisión.</summary>
    [HttpPost("{publicId:guid}/assign"), RequirePermission(PermissionCatalog.WarehouseManage + "|" + PermissionCatalog.WarehouseAppReassign)]
    public async Task<SalesOrderDto> Assign(Guid publicId, [FromBody] TaskAssignRequest req, [FromServices] PermissionService perms, CancellationToken ct)
    {
        await ReassignGuard.EnsureAsync(perms, ct);
        return await sales.AssignAsync(publicId, req, ct);
    }

    /// <summary>Lote 195: «tomar» una orden de venta sin asignar. Quien puede recolectar.</summary>
    [HttpPost("{publicId:guid}/take"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<SalesOrderDto> Take(Guid publicId, [FromBody] TaskTakeRequest? req, CancellationToken ct) => sales.TakeAsync(publicId, req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<SalesOrderDto> Get(Guid publicId, CancellationToken ct) => sales.GetAsync(publicId, ct);

    /// <summary>Crea la orden en borrador (OV-#####). Sin precio en una línea se usa el precio de venta del producto; otro precio exige sales.price.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> Create([FromBody] SalesOrderRequest req, CancellationToken ct) => sales.CreateAsync(req, ct);

    /// <summary>Edita una orden en borrador; las líneas, si vienen, reemplazan a las actuales.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> Update(Guid publicId, [FromBody] SalesOrderRequest req, CancellationToken ct) => sales.UpdateAsync(publicId, req, ct);

    /// <summary>
    /// Lote 230 — las piezas de un kit (de su catálogo) y con qué productos se puede SUSTITUIR cada una en una orden de venta: misma categoría exacta, mismo precio de
    /// venta, propios, activos y que no sean kits ni otra pieza del kit; una pieza marcada «no sustituible» no trae sustitutos. Sirve antes de guardar la línea.
    /// 404 si el producto no existe; 422 si no es un kit.
    /// </summary>
    [HttpGet("kits/{kitPublicId:guid}/substitutes"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<KitSubstitutesDto> KitSubstitutes(Guid kitPublicId, CancellationToken ct) => sales.SubstitutesAsync(kitPublicId, ct);

    /// <summary>Qué pasaría si se solicitara hoy: pedido / disponible / faltante por línea y el crédito del cliente. No guarda nada.</summary>
    [HttpGet("{publicId:guid}/availability"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesAvailabilityDto> Availability(Guid publicId, CancellationToken ct) => sales.CheckAsync(publicId, ct);

    /// <summary>Solicita la orden (DRAFT → REQUESTED): valida y reserva en la misma transacción. Faltante = 409; allowBackorder reserva lo disponible; crédito excedido = 422 (overrideCredit con orders.credit_override).</summary>
    [HttpPost("{publicId:guid}/request"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> Request(Guid publicId, [FromBody] SalesOrderRequestRequest? req, CancellationToken ct) => sales.RequestAsync(publicId, req, ct);

    /// <summary>Cambia la cantidad y/o el precio de una línea de una orden solicitada (reajusta la reserva).</summary>
    [HttpPatch("{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> ModifyLine(Guid publicId, int lineId, [FromBody] SalesOrderLineModifyRequest req, CancellationToken ct) => sales.ModifyLineAsync(publicId, lineId, req, ct);

    /// <summary>Líneas con backorder de las órdenes solicitadas, la más antigua primero, con lo disponible hoy y lo que se podría surtir ahora.</summary>
    [HttpGet("backorders"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<IReadOnlyList<SalesBackorderLineDto>> Backorders([FromQuery] string? q, CancellationToken ct) => sales.BackordersAsync(q, ct);

    /// <summary>Surte todas las líneas con backorder que se puedan, de la más antigua a la más reciente.</summary>
    [HttpPost("backorders/fulfill"), RequirePermission(PermissionCatalog.SalesBackorder)]
    public Task<SalesFulfillResultDto> FulfillAll(CancellationToken ct) => sales.FulfillAllAsync(ct);

    /// <summary>Reserva para la línea lo que haya (todo o parte de su backorder).</summary>
    [HttpPost("{publicId:guid}/lines/{lineId:int}/fulfill"), RequirePermission(PermissionCatalog.SalesBackorder)]
    public Task<SalesOrderDto> FulfillLine(Guid publicId, int lineId, CancellationToken ct) => sales.FulfillLineAsync(publicId, lineId, ct);

    /// <summary>Cierra la línea con faltante: no espera más inventario (lo reservado sigue para despacharse).</summary>
    [HttpPost("{publicId:guid}/lines/{lineId:int}/close"), RequirePermission(PermissionCatalog.SalesBackorder)]
    public Task<SalesOrderDto> CloseLine(Guid publicId, int lineId, [FromBody] SalesOrderCancelRequest? req, CancellationToken ct) => sales.CloseLineAsync(publicId, lineId, req?.Comment, ct);

    /// <summary>Despacha contra la orden: consume la reserva, saca el inventario y crea la recolección (por partes; nunca más de lo reservado). Con `delivery` también crea la entrega, todo en una transacción.</summary>
    [HttpPost("{publicId:guid}/dispatch"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<SalesDispatchResultDto> Dispatch(Guid publicId, [FromBody] SalesDispatchRequest? req, [FromServices] SalesDeliveryService delivery, CancellationToken ct)
        => req?.Delivery is null ? sales.DispatchAsync(publicId, req, ct) : delivery.DispatchAndDeliverAsync(publicId, req, ct);

    /// <summary>Crea la entrega (orden de transporte) de un despacho de la orden de venta. Cliente y consignatario salen de la orden si no se mandan.</summary>
    [HttpPost("{publicId:guid}/delivery"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<SalesOrderDto> Delivery(Guid publicId, [FromBody] SalesDeliveryRequest? req, [FromServices] SalesDeliveryService delivery, CancellationToken ct) => delivery.CreateAsync(publicId, req, ct);

    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.SalesCancel)]
    public Task<SalesOrderDto> Cancel(Guid publicId, [FromBody] SalesOrderCancelRequest? req, CancellationToken ct) => sales.CancelAsync(publicId, req?.Comment, ct);

    // ---------------------------------------------------------------- Lote 222 (pedido de vendedores)

    /// <summary>Ajustes → Operación → Ventas: cargo de envío de la compañía, mínimo para envío gratis (medido después del IVU) y si el cargo lleva IVU.</summary>
    [HttpGet("settings"), RequirePermission(PermissionCatalog.SalesView + "|" + PermissionCatalog.AdminTenant)]
    public Task<SalesShippingSettingsDto> ShippingSettings(CancellationToken ct) => sales.GetShippingSettingsAsync(ct);

    /// <summary>Cambia los ajustes de envío (null = sin cambio; cargo 0 = sin cargo; mínimo 0 = sin mínimo). 400 con el mensaje exacto si un monto no es válido.</summary>
    [HttpPatch("settings"), RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<SalesShippingSettingsDto> UpdateShippingSettings([FromBody] SalesShippingSettingsRequest? req, CancellationToken ct) => sales.UpdateShippingSettingsAsync(req, ct);

    /// <summary>Panel Ventas del Pulso del día: pedidos de hoy, urgentes abiertos, clientes por aprobar y órdenes que esperan aprobación (exige además sales.view).</summary>
    [HttpGet("pulse"), RequirePermission(PermissionCatalog.PulseSales)]
    public Task<PulseSalesPanelDto> Pulse([FromServices] SalesPulseService pulse, CancellationToken ct) => pulse.GetAsync(ct);

    // ---------------------------------------------------------------- Lote 223 (pedido guiado)

    /// <summary>
    /// Firma dibujada de quien autoriza el pedido (PNG o JPG, hasta Files:MaxMegabytes), con su nombre (authorizedByName; "" lo quita, ausente no lo cambia). Solo en borrador (422).
    /// Queda como StoredFile SIGNATURE de la orden; se ve por GET /files/{terms.signatureFilePublicId}. Subir otra reemplaza la anterior.
    /// </summary>
    [HttpPost("{publicId:guid}/signature"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.SalesCreate)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<SalesOrderDto> SetSignature(Guid publicId, IFormFile? file, [FromForm] string? authorizedByName, [FromServices] SalesDraftService drafts,
        [FromServices] StoredFileService files, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", Teikem.Domain.Sales.SalesDraftStepRules.SignatureRequiredFile);
        if (file.Length > files.MaxBytes) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", Teikem.Domain.Files.StoredFileRules.TooLarge(files.MaxBytes / 1024 / 1024));
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return await drafts.SetSignatureAsync(publicId, authorizedByName, file.FileName, file.ContentType, buffer.ToArray(), ct);
    }

    /// <summary>Quita la firma de la orden en borrador (el archivo se queda, desligado). POST y no DELETE: en las órdenes de venta no se borra nada.</summary>
    [HttpPost("{publicId:guid}/signature/remove"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> ClearSignature(Guid publicId, [FromServices] SalesDraftService drafts, CancellationToken ct) => drafts.ClearSignatureAsync(publicId, ct);

    /// <summary>
    /// «Enviar pedido» del pedido guiado: reautentica al vendedor (authMethod PASSWORD con su contraseña y su código MFA si lo tiene; BIOMETRIC desde la app, válido solo con una
    /// verificación reciente de la sesión: si no, 403 aal2_required) y solicita la orden como POST /request (faltante 409 → repetir con allowBackorder; crédito excedido 422).
    /// Contraseña o código incorrectos = 400 (cuentan para el bloqueo de la cuenta). Queda en terms.submittedWithAuth.
    /// </summary>
    [HttpPost("{publicId:guid}/submit"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> Submit(Guid publicId, [FromBody] SalesOrderSubmitRequest? req, [FromServices] SalesDraftService drafts, CancellationToken ct)
        => drafts.SubmitAsync(publicId, req, long.TryParse(User.FindFirst(TeikemClaims.SessionId)?.Value, out var s) ? s : 0, ct);
}
