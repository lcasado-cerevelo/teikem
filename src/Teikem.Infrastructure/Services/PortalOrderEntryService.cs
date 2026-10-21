using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 105 — entrada de órdenes del cliente desde su portal (decidido con Luis, 2026-10-17: la orden entra DIRECTO al flujo normal, solo los campos básicos). El cliente sale de la sesión (<c>cid</c>), nunca del
/// cuerpo; se reutiliza <see cref="OrderService.CreateAsync"/> con el alcance del cliente (mismas validaciones, numeración, conduce, crédito y duplicados que la web de la compañía). Pueden crear los roles
/// de portal CLIENT_ADMIN y CLIENT_OPERATOR; CLIENT_READONLY solo mira (403).
/// </summary>
public sealed class PortalOrderEntryService(TeikemDbContext db, ITenantContext tenant, OrderService orders)
{
    public const int MaxPackages = 50;
    public const int MaxNotes = 500;
    public const string ReadOnlyMessage = "Su usuario solo puede consultar; pídale a un administrador de su empresa que le dé permiso para crear envíos.";
    public const string ConsigneeRequiredMessage = "Escoja el consignatario o escriba uno nuevo (nombre, dirección y ciudad).";
    public const string ConsigneeBothMessage = "Escoja un consignatario o escriba uno nuevo, no las dos cosas.";
    public const string PackagesRequiredMessage = "Agregue al menos un paquete.";
    public const string TooManyPackagesMessage = "Un envío puede llevar hasta 50 paquetes.";
    public const string NotesTooLongMessage = "Las notas no pueden pasar de 500 caracteres.";

    private TenantContext Ctx => (TenantContext)tenant;
    private int ClientId => Ctx.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    /// <summary>¿Puede esta persona crear órdenes? Según su rol de portal en el cliente de la sesión.</summary>
    public async Task<bool> CanCreateAsync(CancellationToken ct)
    {
        var userId = Ctx.RequireUserId();
        var cid = ClientId;
        var role = await (from p in db.PortalUsers.AsNoTracking()
                          join r in db.LookupCodes.AsNoTracking() on p.RoleLookupId equals r.LookupCodeId
                          where p.UserId == userId && p.ClientId == cid && p.IsActive
                          select r.InternalCode).FirstOrDefaultAsync(ct);
        return role is PortalRoles.ClientAdmin or PortalRoles.ClientOperator;
    }

    public async Task<PortalOrderFormDto> GetFormAsync(CancellationToken ct)
    {
        var cid = ClientId;
        if (!await CanCreateAsync(ct)) return new PortalOrderFormDto(false, Array.Empty<PortalPlaceDto>(), Array.Empty<PortalPlaceDto>(), MaxPackages);
        var rows = await (from l in db.Locations.AsNoTracking()
                          join t in db.LookupCodes.AsNoTracking() on l.LocationTypeLookupId equals t.LookupCodeId
                          where l.ClientId == cid && l.IsActive
                          orderby l.Name
                          select new { l.PublicId, l.Name, l.Line1, l.Line2, l.City, l.State, l.PostalCode, Type = t.InternalCode }).ToListAsync(ct);
        var consignees = rows.Where(r => r.Type == LocationTypes.Delivery).Select(r => new PortalPlaceDto(r.PublicId, r.Name, r.Line1, r.Line2, r.City, r.State, r.PostalCode)).ToList();
        var pickups = rows.Where(r => r.Type is LocationTypes.Pickup or LocationTypes.Both).Select(r => new PortalPlaceDto(r.PublicId, r.Name, r.Line1, r.Line2, r.City, r.State, r.PostalCode)).ToList();
        var typesInvoice = await db.Clients.AsNoTracking().Where(c => c.ClientId == cid).Select(c => c.ClientAssignsInvoiceNumber).FirstOrDefaultAsync(ct);   // si no, la factura la numera Teikem y el campo no se muestra
        return new PortalOrderFormDto(true, consignees, pickups, MaxPackages, typesInvoice);
    }

    public async Task<PortalOrderCreatedDto> CreateAsync(PortalOrderCreateRequest req, CancellationToken ct)
    {
        var cid = ClientId;
        if (!await CanCreateAsync(ct)) throw new ForbiddenException(ReadOnlyMessage);
        if (req.ConsigneePublicId is not null && req.NewConsignee is not null) throw new ValidationException("consignee", ConsigneeBothMessage);
        OrderConsigneeCreateRequest? newConsignee = null;
        if (req.ConsigneePublicId is null)
        {
            var n = req.NewConsignee;
            if (n is null || string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrWhiteSpace(n.Line1) || string.IsNullOrWhiteSpace(n.City)) throw new ValidationException("consignee", ConsigneeRequiredMessage);
            newConsignee = new OrderConsigneeCreateRequest(n.Name.Trim(), n.Line1.Trim(), n.Line2?.Trim(), n.City.Trim(), n.State?.Trim(), n.PostalCode?.Trim(), "PR", null);
        }
        if (req.Packages is null || req.Packages.Count == 0) throw new ValidationException("packages", PackagesRequiredMessage);
        if (req.Packages.Count > MaxPackages) throw new ValidationException("packages", TooManyPackagesMessage);
        var notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        if (notes is { Length: > MaxNotes }) throw new ValidationException("notes", NotesTooLongMessage);

        var create = new OrderCreateRequest(
            ClientPublicId: null,
            ConsigneeLocationPublicId: req.ConsigneePublicId,
            NewConsignee: newConsignee,
            PickupLocationPublicId: req.PickupPublicId,
            ServiceType: null,
            Priority: null,
            Packages: req.Packages.Select(p => new OrderPackageLineRequest(null, string.IsNullOrWhiteSpace(p.Description) ? null : p.Description.Trim(), p.Pieces, p.WeightKg)).ToList(),
            CodAmount: req.CodAmount,
            OrderNumber: null,
            ClientInvoiceNumber: string.IsNullOrWhiteSpace(req.ClientInvoiceNumber) ? null : req.ClientInvoiceNumber.Trim(),
            RequestedDate: req.RequestedDate,
            PromisedDate: null,
            Notes: notes,
            References: null);
        var o = await orders.CreateAsync(create, new OrderScope(cid), null, ct);
        return new PortalOrderCreatedDto(o.PublicId, o.WaybillNumber ?? string.Empty, o.OrderNumber, o.Status, o.StatusLabel);
    }
}
