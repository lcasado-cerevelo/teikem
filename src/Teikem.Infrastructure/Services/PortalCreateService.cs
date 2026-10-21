using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 156 — lo que el cliente crea desde su portal: productos propios, avisos de llegada y avisos de salida. NO duplica reglas: delega en <see cref="ProductService"/>, <see cref="AsnService"/> y
/// <see cref="ExitNoticeService"/> (las mismas validaciones, numeración, reservas, historial y avisos del personal), siempre a nombre del cliente de la sesión (<c>cid</c>). Lo gobierna una bandera POR CLIENTE
/// (<see cref="PortalCreateRules"/>) y solo lo usan los accesos ADMIN u OPERADOR; lo ajeno responde 404 (sin oráculo).
/// </summary>
public sealed class PortalCreateService(TeikemDbContext db, ITenantContext tenant, ProductService products, AsnService asns, ExitNoticeService exits, PortalWarehouseService warehouse)
{
    private const int MaxList = 100;

    private sealed record Session(int ClientId, Guid ClientPublicId);

    /// <summary>El cliente de la sesión si el acceso puede crear y la bandera pedida está encendida; si no, 403 con el mensaje que corresponde.</summary>
    private async Task<Session> RequireAsync(Func<Client, bool> flag, string offMessage, CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        var cid = tc.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        var role = await (from p in db.PortalUsers.AsNoTracking().IgnoreQueryFilters()
                          join r in db.LookupCodes.AsNoTracking() on p.RoleLookupId equals r.LookupCodeId
                          where p.UserId == tc.UserId && p.ClientId == cid && p.TenantId == tc.TenantId && p.IsActive
                          select r.InternalCode).FirstOrDefaultAsync(ct);
        if (role is null) throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        if (!PortalCreateRules.RoleCanCreate(role)) throw new ForbiddenException(PortalCreateRules.ReadOnlyForbidden);
        var client = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == cid, ct) ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        if (!flag(client)) throw new ForbiddenException(offMessage);
        return new Session(client.ClientId, client.PublicId);
    }

    /// <summary>Los almacenes activos de la compañía (para elegir adónde llega o de dónde sale).</summary>
    public async Task<IReadOnlyList<PortalWarehouseOptionDto>> WarehousesAsync(CancellationToken ct)
        => await db.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Code).Select(w => new PortalWarehouseOptionDto(w.PublicId, w.Code, w.Name)).ToListAsync(ct);

    // ================================================================ productos

    public async Task<PortalProductDto> CreateProductAsync(PortalProductCreateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var s = await RequireAsync(c => c.PortalCanCreateProducts, PortalCreateRules.ProductsOff, ct);
        var created = await products.CreateAsync(new ProductCreateRequest(req.Sku, req.Name, OwnerClientPublicId: s.ClientPublicId, BaseUom: req.BaseUom, TrackingType: req.TrackingType,
            WeightKg: req.WeightKg, VolumeM3: req.VolumeM3, Barcode: req.Barcode, PurchaseCost: req.PurchaseCost, Brand: req.Brand, Model: req.Model), ct);
        var page = await warehouse.ProductsAsync(created.Product.Sku, true, 0, 100, ct);
        return page.Items.First(p => p.PublicId == created.Product.PublicId);
    }

    /// <summary>
    /// Edita un producto que el cliente ya creó (mismos permisos que crearlos: la bandera de la compañía y un rol ADMIN u OPERADOR). Solo productos DEL cliente de la sesión (otro → 404 como si no existiera);
    /// las reglas y mensajes son los de <see cref="ProductService.UpdateAsync"/>. Cuerpo vacío (sin ningún cambio) → 400.
    /// </summary>
    public async Task<PortalProductDto> UpdateProductAsync(Guid publicId, PortalProductPatchRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var s = await RequireAsync(c => c.PortalCanCreateProducts, PortalCreateRules.ProductsOff, ct);
        var mine = await db.Set<Product>().AsNoTracking().AnyAsync(p => p.PublicId == publicId && p.ClientId == s.ClientId, ct);
        if (!mine) throw new NotFoundException("Producto", publicId);
        if (req.Name is null && req.Barcode is null && req.Brand is null && req.Model is null && req.WeightKg is null && req.VolumeM3 is null && req.PurchaseCost is null)
            throw new ValidationException("body", "No hay ningún cambio que guardar.");
        await products.UpdateAsync(publicId, new ProductPatchRequest(Name: req.Name, Barcode: req.Barcode, Brand: req.Brand, Model: req.Model, WeightKg: req.WeightKg, VolumeM3: req.VolumeM3, PurchaseCost: req.PurchaseCost), ct);
        var sku = await db.Set<Product>().AsNoTracking().Where(p => p.PublicId == publicId).Select(p => p.Sku).FirstAsync(ct);
        return (await warehouse.ProductsAsync(sku, true, 0, 100, ct)).Items.First(p => p.PublicId == publicId);
    }

    // ================================================================ avisos de llegada

    public async Task<PortalArrivalDto> CreateArrivalAsync(PortalArrivalRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var s = await RequireAsync(c => c.PortalCanCreateArrivals, PortalCreateRules.ArrivalsOff, ct);
        var lines = (req.Lines ?? Array.Empty<PortalArrivalLineRequest>()).Select(l => new AsnLineRequest(l.ProductPublicId, l.ExpectedQty, l.LotNumber, l.UnitPrice, ExpiryDate: l.ExpiryDate)).ToList();
        var created = await asns.CreateAsync(new AsnCreateRequest(req.WarehousePublicId, s.ClientPublicId, req.Reference, req.ExpectedDate, lines), ct);
        return ToArrival(created);
    }

    public async Task<IReadOnlyList<PortalArrivalDto>> ListArrivalsAsync(string? search, CancellationToken ct)
    {
        var cid = ((TenantContext)tenant).PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        var publicId = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == cid).Select(c => c.PublicId).FirstOrDefaultAsync(ct);
        var list = await asns.ListAsync(new AsnQuery(ClientPublicId: publicId, Search: search), InventoryScope.Any, ct);
        return list.Where(a => a.ClientPublicId == publicId).Take(MaxList).Select(ToArrival).ToList();
    }

    public async Task<PortalArrivalDto> CancelArrivalAsync(int id, CancellationToken ct)
    {
        var s = await RequireAsync(c => c.PortalCanCreateArrivals, PortalCreateRules.ArrivalsOff, ct);
        var current = await asns.GetAsync(id, InventoryScope.Any, ct);
        if (current.ClientPublicId != s.ClientPublicId) throw new NotFoundException(AsnService.AsnWhat);   // lo ajeno no existe para este cliente
        return ToArrival(await asns.CancelAsync(id, ct));
    }

    /// <summary>Edita mi aviso de llegada mientras siga esperado y sin recibo abierto (almacén, referencia, fecha, líneas). Las mismas reglas del personal.</summary>
    public async Task<PortalArrivalDto> UpdateArrivalAsync(int id, PortalArrivalRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var s = await RequireAsync(c => c.PortalCanCreateArrivals, PortalCreateRules.ArrivalsOff, ct);
        var current = await asns.GetAsync(id, InventoryScope.Any, ct);
        if (current.ClientPublicId != s.ClientPublicId) throw new NotFoundException(AsnService.AsnWhat);
        var lines = req.Lines?.Select(l => new AsnLineRequest(l.ProductPublicId, l.ExpectedQty, l.LotNumber, l.UnitPrice, ExpiryDate: l.ExpiryDate)).ToList();
        return ToArrival(await asns.UpdateAsync(id, new AsnUpdateRequest(req.WarehousePublicId, req.Reference, req.ExpectedDate, lines), ct));
    }

    private static PortalArrivalDto ToArrival(AsnDto a) => new(a.Id, a.WarehousePublicId, a.WarehouseCode, a.Reference, a.ExpectedDate, a.StatusCode, a.Status,
        a.StatusCode == AsnStatuses.Expected && a.ReceiptPublicId is null && a.IsActive, a.Lines.Select(l => new PortalArrivalLineDto(l.Sku, l.ProductName, l.ExpectedQty, l.LotNumber, l.UnitPrice, l.ExpiryDate, l.ProductPublicId, l.TrackingType)).ToList(), a.Number);

    // ================================================================ avisos de salida

    public async Task<PortalExitDto> CreateExitAsync(PortalExitRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var s = await RequireAsync(c => c.PortalCanCreateExits, PortalCreateRules.ExitsOff, ct);
        var lines = (req.Lines ?? Array.Empty<PortalExitLineRequest>()).Select(l => new ExitNoticeLineRequest(l.ProductPublicId, l.Quantity, l.LotId)).ToList();
        var created = await exits.CreateAsync(new ExitNoticeRequest(req.WarehousePublicId, s.ClientPublicId, req.Reference, req.Destination, req.RequiredDate, req.Notes, lines), ct);
        return ToExit(created);
    }

    public async Task<PortalExitPageDto> ListExitsAsync(string? status, string? search, int skip, int take, CancellationToken ct)
    {
        var cid = ((TenantContext)tenant).PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        var publicId = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == cid).Select(c => c.PublicId).FirstOrDefaultAsync(ct);
        var page = await exits.ListAsync(status, publicId, search, false, null, null, skip, Math.Clamp(take <= 0 ? 25 : take, 1, MaxList), ct);
        return new PortalExitPageDto(page.Total, page.Skip, page.Take, page.Items.Where(i => i.ClientPublicId == publicId)
            .Select(i => new PortalExitListItemDto(i.PublicId, i.Number, i.StatusCode, i.Status, i.WarehouseCode, i.Reference, i.Destination, i.RequestedDate, i.RequiredDate, i.LineCount, i.QtyOrdered, i.QtyShipped, i.HasBackorder)).ToList());
    }

    public async Task<PortalExitDto> GetExitAsync(Guid publicId, CancellationToken ct)
    {
        var cid = ((TenantContext)tenant).PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        var mine = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == cid).Select(c => c.PublicId).FirstOrDefaultAsync(ct);
        var notice = await exits.GetAsync(publicId, ct);
        if (notice.ClientPublicId != mine) throw new NotFoundException("Aviso de salida");
        return ToExit(notice);
    }

    public async Task<PortalExitDto> CancelExitAsync(Guid publicId, string? comment, CancellationToken ct)
    {
        var s = await RequireAsync(c => c.PortalCanCreateExits, PortalCreateRules.ExitsOff, ct);
        var notice = await exits.GetAsync(publicId, ct);
        if (notice.ClientPublicId != s.ClientPublicId) throw new NotFoundException("Aviso de salida");
        return ToExit(await exits.CancelAsync(publicId, comment, ct));
    }

    /// <summary>Edita los datos de mi aviso de salida mientras siga solicitado.</summary>
    public async Task<PortalExitDto> UpdateExitAsync(Guid publicId, PortalExitHeaderRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var s = await RequireAsync(c => c.PortalCanCreateExits, PortalCreateRules.ExitsOff, ct);
        var notice = await exits.GetAsync(publicId, ct);
        if (notice.ClientPublicId != s.ClientPublicId) throw new NotFoundException("Aviso de salida");
        return ToExit(await exits.UpdateHeaderAsync(publicId, new ExitNoticeHeaderRequest(req.Reference, req.Destination, req.RequiredDate, req.Notes), ct));
    }

    /// <summary>Cambia la cantidad pedida de una línea de mi aviso solicitado (se reajusta la reserva).</summary>
    public async Task<PortalExitDto> ModifyExitLineAsync(Guid publicId, int lineId, PortalExitLineModifyRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var s = await RequireAsync(c => c.PortalCanCreateExits, PortalCreateRules.ExitsOff, ct);
        var notice = await exits.GetAsync(publicId, ct);
        if (notice.ClientPublicId != s.ClientPublicId) throw new NotFoundException("Aviso de salida");
        return ToExit(await exits.ModifyLineAsync(publicId, lineId, new ExitNoticeLineModifyRequest(req.Quantity), ct));
    }

    private static PortalExitDto ToExit(ExitNoticeDto n) => new(n.PublicId, n.Number, n.StatusCode, n.Status, n.WarehousePublicId, n.WarehouseCode, n.Reference, n.Destination, n.RequestedDate, n.RequiredDate,
        n.Notes, n.HasBackorder, n.CanCancel, n.Lines.Select(l => new PortalExitLineDto(l.Id, l.Sku, l.ProductName, l.LotNumber, l.QtyOrdered, l.QtyReserved, l.QtyBackordered, l.QtyShipped)).ToList());
}
