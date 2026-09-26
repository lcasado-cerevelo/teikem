using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P0) — resolvers de pertenencia de Trips y rutas para las asociaciones polimórficas (ContactPoint,
/// CustomFieldValue) y el historial de estatus. Trip lleva TenantId (filtro global); Route y RouteStop NO:
/// se alcanzan SIEMPRE a través de db.Trips ya filtrado (BOLA por id hijo). Junto con OwnerReadPermission/OwnerWritePermission
/// (trips.view / trips.plan) cierran el historial de TRIP, ROUTE y ROUTE_STOP. Un id de otro tenant o inexistente responde
/// false → 404. OPTIMIZATION_RUN es bitácora de solo lectura: se registra con ClosedOwnedEntityResolver (siempre 404) en
/// DependencyInjection, de modo que nadie escribe contactos ni campos personalizados sobre una corrida y no hay oráculo
/// de existencia de ids (404 frente a 403).
/// </summary>
public sealed class TripOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Trip;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.Trips.AsNoTracking().AnyAsync(t => t.TripId == id, ct);
}

public sealed class RouteOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Route;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.Trips.AsNoTracking().SelectMany(t => t.Routes).AnyAsync(r => r.RouteId == id, ct);
}

public sealed class RouteStopOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.RouteStop;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.Trips.AsNoTracking().SelectMany(t => t.Routes).SelectMany(r => r.Stops).AnyAsync(s => s.RouteStopId == id, ct);
}
