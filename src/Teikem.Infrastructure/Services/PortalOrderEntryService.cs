using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 105 — entrada de órdenes del cliente desde su portal (decidido con Luis, 2026-10-17: la orden entra DIRECTO al flujo normal). Lote 213: los mismos campos que la entrada del personal (tipo de servicio, tipo de paquete por
/// línea, número de orden si el cliente lo asigna y fecha límite), salvo precios, prioridad y entrega especial. El cliente sale de la sesión (<c>cid</c>), nunca del
/// cuerpo; se reutiliza <see cref="OrderService.CreateAsync"/> con el alcance del cliente (mismas validaciones, numeración, conduce, crédito y duplicados que la web de la compañía). Pueden crear los roles
/// de portal CLIENT_ADMIN y CLIENT_OPERATOR; CLIENT_READONLY solo mira (403).
/// </summary>
public sealed class PortalOrderEntryService(TeikemDbContext db, ITenantContext tenant, OrderService orders, LocationService? locations = null, ContactPointService? contacts = null)
{
    public const int MaxPackages = 50;
    public const int MaxNotes = 500;
    public const string ReadOnlyMessage = "Su usuario solo puede consultar; pídale a un administrador de su empresa que le dé permiso para crear envíos.";
    public const string ConsigneeRequiredMessage = "Escoja el consignatario o escriba uno nuevo (nombre, dirección y ciudad).";
    public const string ConsigneeBothMessage = "Escoja un consignatario o escriba uno nuevo, no las dos cosas.";
    public const string PackagesRequiredMessage = "Agregue al menos un paquete.";
    public const string TooManyPackagesMessage = "Un envío puede llevar hasta 50 paquetes.";
    public const string NotesTooLongMessage = "Las notas no pueden pasar de 500 caracteres.";
    public const string PromisedBeforeRequestedMessage = "La fecha límite no puede ser anterior a la fecha lista.";
    public const string ConsigneeNameRequiredMessage = "El nombre del consignatario es obligatorio.";
    public const string ConsigneeLine1RequiredMessage = "La dirección del consignatario es obligatoria.";
    public const string ConsigneeCityRequiredMessage = "El pueblo del consignatario es obligatorio.";
    public const string PhoneLabel = "Teléfono";

    /// <summary>Tipos de contacto que cuentan como teléfono del consignatario en el formulario (los mismos del aviso al consignatario).</summary>
    private static readonly string[] PhoneTypes = { "MOBILE", "PHONE", "WHATSAPP" };

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
                          select new { l.LocationId, l.PublicId, l.Name, l.Line1, l.Line2, l.City, l.State, l.PostalCode, l.Code, Type = t.InternalCode }).ToListAsync(ct);
        var phones = await PrimaryPhonesAsync(rows.Select(r => r.LocationId).ToList(), ct);
        PortalPlaceDto Place(int id, Guid publicId, string name, string line1, string? line2, string city, string? state, string? postal, string? code)
            => new(publicId, name, line1, line2, city, state, postal, string.IsNullOrWhiteSpace(code) ? null : code.Trim(), phones.GetValueOrDefault(id));
        var consignees = rows.Where(r => r.Type == LocationTypes.Delivery).Select(r => Place(r.LocationId, r.PublicId, r.Name, r.Line1, r.Line2, r.City, r.State, r.PostalCode, r.Code)).ToList();
        var pickups = rows.Where(r => r.Type is LocationTypes.Pickup or LocationTypes.Both).Select(r => Place(r.LocationId, r.PublicId, r.Name, r.Line1, r.Line2, r.City, r.State, r.PostalCode, r.Code)).ToList();

        // si el cliente no teclea sus números, la factura y la orden las numera Teikem y esos campos no se muestran
        var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == cid)
            .Select(c => new { c.ClientAssignsInvoiceNumber, c.ClientAssignsOrderNumber, c.DefaultServiceTypeLookupId, c.DefaultPackageTypeLookupId }).FirstOrDefaultAsync(ct);
        var tenantId = Ctx.RequireTenantId();
        var company = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId)
            .Select(t => new { t.DefaultServiceTypeLookupId, t.DefaultPackageTypeLookupId }).FirstOrDefaultAsync(ct);
        var catalog = await EnabledCatalogAsync(ct);
        var serviceTypes = catalog.Where(c => c.Entity == LookupDomains.ServiceType).ToList();
        var packageTypes = catalog.Where(c => c.Entity == LookupDomains.PackageType).ToList();
        return new PortalOrderFormDto(true, consignees, pickups, MaxPackages,
            ClientTypesInvoiceNumber: client?.ClientAssignsInvoiceNumber ?? false,
            ClientAssignsOrderNumber: client?.ClientAssignsOrderNumber ?? false,
            ServiceTypes: serviceTypes.Select(c => new PortalCatalogItemDto(c.Code, c.Label)).ToList(),
            PackageTypes: packageTypes.Select(c => new PortalCatalogItemDto(c.Code, c.Label)).ToList(),
            DefaultServiceType: DefaultCode(serviceTypes, client?.DefaultServiceTypeLookupId, company?.DefaultServiceTypeLookupId),
            DefaultPackageType: DefaultCode(packageTypes, client?.DefaultPackageTypeLookupId, company?.DefaultPackageTypeLookupId));
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
        // Lote 213: la fecha límite no puede quedar antes de la fecha lista (OrderService no lo valida)
        if (req.PromisedDate is DateTime promised && req.RequestedDate is DateTime requested && promised.Date < requested.Date)
            throw new ValidationException("promisedDate", PromisedBeforeRequestedMessage);

        var create = new OrderCreateRequest(
            ClientPublicId: null,
            ConsigneeLocationPublicId: req.ConsigneePublicId,
            NewConsignee: newConsignee,
            PickupLocationPublicId: req.PickupPublicId,
            ServiceType: Clean(req.ServiceType),   // vacío = el predeterminado de la compañía (OrderService); un código apagado o desconocido → 400
            Priority: null,
            Packages: req.Packages.Select(p => new OrderPackageLineRequest(Clean(p.PackageType), Clean(p.Description), p.Pieces, p.WeightKg)).ToList(),
            CodAmount: req.CodAmount,
            OrderNumber: Clean(req.OrderNumber),   // OrderService decide si el cliente puede teclearlo (400 si no asigna sus números)
            ClientInvoiceNumber: Clean(req.ClientInvoiceNumber),
            RequestedDate: req.RequestedDate,
            PromisedDate: req.PromisedDate,
            Notes: notes,
            References: null);
        var o = await orders.CreateAsync(create, new OrderScope(cid), null, ct);
        return new PortalOrderCreatedDto(o.PublicId, o.WaybillNumber ?? string.Empty, o.OrderNumber, o.Status, o.StatusLabel, o.ClientInvoiceNumber);
    }

    /// <summary>
    /// 2026-10-23 (pedido de Luis: «lo grabas ahí mismo y ya queda ese consignatario guardado»): crea un consignatario del cliente de la sesión sin esperar a la orden, para escogerlo en la misma pantalla de captura.
    /// Si ya existe uno activo con el mismo nombre y dirección (sin importar mayúsculas ni acentos) se devuelve ese, sin duplicar. El teléfono, si viene, queda como el principal del consignatario (todo o nada: si
    /// el teléfono no vale, no se crea nada). Mismo permiso que crear envíos: administrador u operador.
    /// </summary>
    public async Task<PortalPlaceDto> CreateConsigneeAsync(PortalConsigneeCreateRequest? req, CancellationToken ct)
    {
        var cid = ClientId;
        if (!await CanCreateAsync(ct)) throw new ForbiddenException(ReadOnlyMessage);
        if (locations is null) throw new InvalidOperationException("Crear consignatarios no está disponible en este contexto.");
        var name = Clean(req?.Name) ?? throw new ValidationException("name", ConsigneeNameRequiredMessage);
        var line1 = Clean(req?.Line1) ?? throw new ValidationException("line1", ConsigneeLine1RequiredMessage);
        var city = Clean(req?.City) ?? throw new ValidationException("city", ConsigneeCityRequiredMessage);

        // ¿ya existe? (consignatarios y «ambos» del cliente)
        var types = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.LocationType && (l.InternalCode == LocationTypes.Delivery || l.InternalCode == LocationTypes.Both)).Select(l => l.LookupCodeId).ToListAsync(ct);
        var candidates = await db.Locations.AsNoTracking().Where(l => l.ClientId == cid && l.IsActive && types.Contains(l.LocationTypeLookupId))
            .OrderBy(l => l.LocationId).Select(l => new { l.LocationId, l.Name, l.Line1 }).ToListAsync(ct);
        var matchId = ConsigneeMatch.FindMatch(candidates.Select(c => (c.LocationId, c.Name, c.Line1)), name, line1);
        if (matchId is int existing) return await PlaceAsync(existing, ct);

        var upsert = new LocationUpsertRequest(null, null, name, LocationTypes.Delivery, line1, Clean(req?.Line2), city, Clean(req?.State), Clean(req?.PostalCode), "PR");
        var phone = Clean(req?.Phone);
        if (phone is not null && contacts is null) throw new InvalidOperationException("Guardar el teléfono no está disponible en este contexto.");
        var locationId = await db.RunInTransactionAsync(async ct2 =>
        {
            var (id, _) = await locations.CreateCoreAsync(upsert, cid, ct2);
            if (phone is not null)
                await contacts!.AddAsync(EntityTypes.Location, id, new ContactPointUpsertRequest("PHONE", phone, null, PhoneLabel, IsPrimary: true), ct2, enforceOwnerWrite: false);   // ya se exigió ser administrador u operador del cliente
            return id;
        }, ct);
        return await PlaceAsync(locationId, ct);
    }

    /// <summary>El consignatario como lo ve el formulario (con su número y su teléfono principal).</summary>
    private async Task<PortalPlaceDto> PlaceAsync(int locationId, CancellationToken ct)
    {
        var l = await db.Locations.AsNoTracking().Where(x => x.LocationId == locationId).Select(x => new { x.PublicId, x.Name, x.Line1, x.Line2, x.City, x.State, x.PostalCode, x.Code }).FirstAsync(ct);
        var phones = await PrimaryPhonesAsync(new List<int> { locationId }, ct);
        return new PortalPlaceDto(l.PublicId, l.Name, l.Line1, l.Line2, l.City, l.State, l.PostalCode, string.IsNullOrWhiteSpace(l.Code) ? null : l.Code.Trim(), phones.GetValueOrDefault(locationId));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record CatalogRow(int Id, string Entity, string Code, string Label, int Sort);

    /// <summary>
    /// Tipos de servicio y de paquete que la compañía tiene habilitados (activos y sin apagar en su override), con la etiqueta en el idioma de la sesión (con el nombre que le puso la compañía, si lo cambió) y en su orden
    /// (el de la compañía si lo cambió; luego la etiqueta). Es lo mismo que acepta la captura: OrderService rechaza un código apagado.
    /// </summary>
    private async Task<List<CatalogRow>> EnabledCatalogAsync(CancellationToken ct)
    {
        var codes = await db.LookupCodes.AsNoTracking()
            .Where(c => c.IsActive && (c.Entity == LookupDomains.ServiceType || c.Entity == LookupDomains.PackageType))
            .Select(c => new { c.LookupCodeId, c.Entity, c.InternalCode, c.LabelJson, c.SortOrder }).ToListAsync(ct);
        var ids = codes.Select(c => c.LookupCodeId).ToList();
        var overrides = (await db.LookupCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.LookupCodeId))
                .Select(o => new { o.LookupCodeId, o.CustomLabelJson, o.IsEnabled, o.SortOverride }).ToListAsync(ct))
            .GroupBy(o => o.LookupCodeId).ToDictionary(g => g.Key, g => g.First());
        var result = new List<CatalogRow>(codes.Count);
        foreach (var c in codes)
        {
            var o = overrides.GetValueOrDefault(c.LookupCodeId);
            if (o is { IsEnabled: false }) continue;
            var label = MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang);
            result.Add(new CatalogRow(c.LookupCodeId, c.Entity, c.InternalCode, string.IsNullOrEmpty(label) ? c.InternalCode : label, o?.SortOverride ?? c.SortOrder));
        }
        return result.OrderBy(r => r.Sort).ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>El predeterminado del cliente o, si no tiene, el de la compañía; solo si está entre los habilitados (si no, el formulario lo mandaría y la captura lo rechazaría).</summary>
    private static string? DefaultCode(IReadOnlyList<CatalogRow> enabled, int? clientDefault, int? companyDefault)
    {
        foreach (var id in new[] { clientDefault, companyDefault })
            if (id is int v && enabled.FirstOrDefault(r => r.Id == v) is { } row) return row.Code;
        return null;
    }

    /// <summary>
    /// El teléfono principal de cada localización (móvil, teléfono o WhatsApp activos; el principal primero), en UNA consulta para todas. Va tal cual se guardó, con su prefijo de país delante si lo tiene
    /// (por ejemplo «+1 787-555-0100»).
    /// </summary>
    private async Task<Dictionary<int, string>> PrimaryPhonesAsync(List<int> locationIds, CancellationToken ct)
    {
        if (locationIds.Count == 0) return new Dictionary<int, string>();
        var rows = await (from c in db.ContactPoints.AsNoTracking()
                          join owner in db.LookupCodes.AsNoTracking() on c.OwnerEntityLookupId equals owner.LookupCodeId
                          join type in db.LookupCodes.AsNoTracking() on c.ContactTypeLookupId equals type.LookupCodeId
                          where owner.Entity == LookupDomains.EntityType && owner.InternalCode == EntityTypes.Location
                                && locationIds.Contains(c.OwnerId) && c.IsActive && PhoneTypes.Contains(type.InternalCode)
                          select new { c.OwnerId, c.ContactPointId, c.IsPrimary, c.Value, c.CountryCallingCode }).ToListAsync(ct);
        return rows.Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .GroupBy(r => r.OwnerId)
            .ToDictionary(g => g.Key, g =>
            {
                var r = g.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.ContactPointId).First();
                var cc = r.CountryCallingCode?.Trim().TrimStart('+');
                return string.IsNullOrEmpty(cc) ? r.Value.Trim() : $"+{cc} {r.Value.Trim()}";
            });
    }
}
