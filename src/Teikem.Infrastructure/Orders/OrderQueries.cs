using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Orders;

/// <summary>
/// Consultas compartidas del Lote 3 sobre el DbContext, siempre bajo el filtro global de tenant y bajo el OrderScope
/// (DECISIÓN 19): las usan OrderService, OrderReadService, OrderStatusService, OrderImportService y el efecto de estatus.
/// Las hijas de la orden (paradas, líneas, referencias) no llevan TenantId y se alcanzan SOLO a través de la orden resuelta aquí.
/// </summary>
public static class OrderQueries
{
    public const string OrderLabel = "Orden";
    public const string ConsigneeLabel = "Consignatario";
    public const string PickupLabel = "Localización de recogido";

    /// <summary>Base de lista, lookup y exposición: db.TransportOrders acotado al cliente del scope cuando lo fija.</summary>
    public static IQueryable<TransportOrder> ScopedOrders(this TeikemDbContext db, OrderScope scope)
    {
        IQueryable<TransportOrder> q = db.TransportOrders;
        if (scope.ClientId is int clientId) q = q.Where(o => o.ClientId == clientId);
        return q;
    }

    /// <summary>Orden del tenant y del scope por PublicId, sin tracking, con paradas, líneas y referencias; 404 'Orden' (también si es de otro cliente del scope: sin oráculo).</summary>
    public static async Task<TransportOrder> ResolveOrderForReadAsync(this TeikemDbContext db, Guid publicId, OrderScope scope, CancellationToken ct)
        => await db.ScopedOrders(scope).AsNoTracking()
               .Include(o => o.Stops).Include(o => o.CargoLines).Include(o => o.References)
               .FirstOrDefaultAsync(o => o.PublicId == publicId, ct)
           ?? throw new NotFoundException(OrderLabel);

    /// <summary>
    /// Igual que ResolveOrderForReadAsync pero con tracking (escrituras dentro de RunInTransactionAsync) y SOLO órdenes activas:
    /// una orden eliminada en captura (baja lógica, IsActive=0) responde 404 'Orden' en editar, eliminar, confirmar, reprecio,
    /// cancelar y estatus (L253: después de eliminarla no vuelve al pipeline). La ficha sigue leyéndola con ResolveOrderForReadAsync.
    /// </summary>
    public static async Task<TransportOrder> ResolveOrderForWriteAsync(this TeikemDbContext db, Guid publicId, OrderScope scope, CancellationToken ct)
        => await db.ScopedOrders(scope)
               .Include(o => o.Stops).Include(o => o.CargoLines).Include(o => o.References)
               .FirstOrDefaultAsync(o => o.PublicId == publicId && o.IsActive, ct)
           ?? throw new NotFoundException(OrderLabel);

    /// <summary>
    /// Location del tenant que pertenece al cliente (ClientId == clientId) o es compartida (ClientId null); en cualquier otro
    /// caso 404 con la etiqueta (nunca revela que existe en otro cliente). Inactiva ⇒ 400 en el campo indicado. Sin tracking.
    /// </summary>
    public static async Task<Location> ResolveClientLocationAsync(this TeikemDbContext db, int clientId, Guid locationPublicId, string label, string field, CancellationToken ct)
    {
        var loc = await db.Locations.AsNoTracking()
                      .FirstOrDefaultAsync(l => l.PublicId == locationPublicId && (l.ClientId == clientId || l.ClientId == null), ct)
                  ?? throw new NotFoundException(label);
        if (!loc.IsActive) throw new ValidationException(field, $"{label} está inactivo; reactívelo o elija otro.");
        return loc;
    }

    public static Task<Location> ResolveConsigneeAsync(this TeikemDbContext db, int clientId, Guid locationPublicId, CancellationToken ct)
        => db.ResolveClientLocationAsync(clientId, locationPublicId, ConsigneeLabel, "consigneeLocationPublicId", ct);

    public static Task<Location> ResolvePickupAsync(this TeikemDbContext db, int clientId, Guid locationPublicId, CancellationToken ct)
        => db.ResolveClientLocationAsync(clientId, locationPublicId, PickupLabel, "pickupLocationPublicId", ct);

    /// <summary>
    /// Punto de recogido por defecto del cliente (DECISIÓN 13): la Location activa DefaultPickupLocationId, si no la
    /// CORPORATE activa del cliente, si no null (la orden nace solo con la parada DELIVERY). Sin tracking.
    /// </summary>
    public static async Task<Location?> DefaultPickupAsync(this TeikemDbContext db, Client client, CancellationToken ct)
    {
        if (client.DefaultPickupLocationId is int defaultId)
        {
            var explicitPickup = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.LocationId == defaultId && l.IsActive, ct);
            if (explicitPickup is not null) return explicitPickup;
        }
        var clientId = client.ClientId;
        var corporateId = await db.LookupCodes.AsNoTracking()
            .Where(c => c.Entity == LookupDomains.LocationType && c.InternalCode == LocationTypes.Corporate)
            .Select(c => (int?)c.LookupCodeId).FirstOrDefaultAsync(ct);
        if (corporateId is null) return null;
        return await db.Locations.AsNoTracking()
            .Where(l => l.ClientId == clientId && l.IsActive && l.LocationTypeLookupId == corporateId)
            .OrderBy(l => l.LocationId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>¿El StatusCode es la etapa inicial de su dominio?</summary>
    public static async Task<bool> IsInitialAsync(this TeikemDbContext db, int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.IsInitial).FirstOrDefaultAsync(ct);

    /// <summary>Código del StageKind (PIPELINE | LATERAL | TERMINAL) del StatusCode; cadena vacía si no existe.</summary>
    public static async Task<string> StageKindAsync(this TeikemDbContext db, int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.StageKind!.InternalCode).FirstOrDefaultAsync(ct)
           ?? string.Empty;
}
