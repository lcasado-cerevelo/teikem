using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Trips;

/// <summary>Ruta vigente de una orden (TripOrder IsCurrent) con lo mínimo para decidir y responder.</summary>
public sealed record CurrentTripRef(int TripId, Guid TripPublicId, string Code, string StatusCode, int? DriverId);

/// <summary>Parada DELIVERY pendiente (no terminal, de menor secuencia) de una orden: la que se rutea.</summary>
public sealed record PendingDeliveryStop(int TransportOrderId, int OrderStopId, string? PostalCode, string City, DateTime? WindowStartUtc,
    DateTime? WindowEndUtc, int ServiceMinutes, int? GeocodeAccuracyLookupId);

/// <summary>Coordenada de una parada (GEOGRAPHY) y su precisión de geocodificación.</summary>
public sealed record StopPointRow(double Lat, double Lng, int? GeocodeAccuracyLookupId);

/// <summary>Llave para buscar el último ping de una ruta: su chofer y su salida real (para el ping de respaldo).</summary>
public sealed record TripPingKey(int TripId, int? DriverId, DateTime? ActualStartUtc);

/// <summary>Último ping de una ruta. LinkedToTrip = false si es el ping del chofer sin TripId desde la salida (respaldo).</summary>
public sealed record DriverPingRow(double Lat, double Lng, decimal? SpeedKmh, int? HeadingDeg, DateTime CapturedAtUtc, DateTime ReceivedAtUtc, bool LinkedToTrip);

/// <summary>
/// Lote 5 (P0) — consultas compartidas de Trips y rutas (costuras implementadas). Es el ÚNICO lugar del lote con SQL crudo
/// (bloqueos UPDLOCK, GEOGRAPHY, pin manual y pings) y cada sentencia lleva 'TenantId =' explícito, además del filtro global
/// cuando se compone sobre un DbSet (RawSqlConfinementTests lo verifica).
///
/// ORDEN DE BLOQUEO ÚNICO del lote (evita interbloqueos entre planificar, agregar, escanear, optimizar y despachar):
///   1. la fila Tenant (LockTenantPlanningAsync; solo 'Planificar el día');
///   2. los Trips, por TripId ascendente (LockTripAsync / LockTripsAsync);
///   3. las órdenes, por TransportOrderId, en UNA sola sentencia (LockOrdersAsync);
///   4. después se escribe.
/// Los bloqueos exigen una transacción abierta (RunInTransactionAsync) con proveedor relacional; con InMemory (pruebas) se
/// carga tracked sin bloqueo y las consultas de GEOGRAPHY/pings devuelven vacío.
/// </summary>
public static class TripQueries
{
    /// <summary>Etiqueta del 404: 'Ruta no encontrada.'</summary>
    public const string TripLabel = "Ruta";

    // ================================================================ rutas

    /// <summary>Ruta del tenant por PublicId (404 'Ruta no encontrada.'). Las eliminadas también se resuelven.</summary>
    public static async Task<Trip> ResolveTripAsync(this TeikemDbContext db, Guid publicId, bool track, CancellationToken ct)
    {
        var q = track ? db.Trips.AsTracking() : db.Trips.AsNoTracking();
        return await q.FirstOrDefaultAsync(t => t.PublicId == publicId, ct) ?? throw TripNotFound();
    }

    /// <summary>
    /// Carga la ruta del tenant con bloqueo de fila (UPDLOCK, ROWLOCK) hasta el fin de la transacción, tracked (paso 2 del
    /// orden de bloqueo). 404 'Ruta no encontrada.' si no es del tenant. Exige transacción con proveedor relacional.
    /// </summary>
    public static async Task<Trip> LockTripAsync(this TeikemDbContext db, int tripId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.Trips.AsTracking().FirstOrDefaultAsync(t => t.TripId == tripId, ct) ?? throw TripNotFound();

        RequireTransaction(db, nameof(LockTripAsync));
        var tenantId = db.CurrentTenantId;
        return await db.Trips
                   .FromSqlInterpolated($"SELECT * FROM dbo.Trip WITH (UPDLOCK, ROWLOCK) WHERE TripId = {tripId} AND TenantId = {tenantId}")
                   .AsTracking()
                   .FirstOrDefaultAsync(ct)
               ?? throw TripNotFound();
    }

    /// <summary>
    /// Igual que LockTripAsync(int) pero por PublicId: primero resuelve el id (lectura normal, filtro de tenant) y después
    /// bloquea por la llave primaria (sin barrer la tabla con UPDLOCK).
    /// </summary>
    public static async Task<Trip> LockTripAsync(this TeikemDbContext db, Guid publicId, CancellationToken ct)
    {
        var tripId = await db.Trips.AsNoTracking().Where(t => t.PublicId == publicId).Select(t => (int?)t.TripId).FirstOrDefaultAsync(ct)
                     ?? throw TripNotFound();
        return await db.LockTripAsync(tripId, ct);
    }

    /// <summary>Bloquea varias rutas UNA POR UNA en orden ascendente de TripId (orden de bloqueo del lote); omite las que no existan.</summary>
    public static async Task<IReadOnlyList<Trip>> LockTripsAsync(this TeikemDbContext db, IEnumerable<int> tripIds, CancellationToken ct)
    {
        var result = new List<Trip>();
        foreach (var id in (tripIds ?? Array.Empty<int>()).Distinct().OrderBy(id => id))
        {
            try { result.Add(await db.LockTripAsync(id, ct)); }
            catch (NotFoundException) { /* ya no existe en el tenant: se omite */ }
        }
        return result;
    }

    /// <summary>
    /// Bloquea las órdenes del tenant (UPDLOCK, ROWLOCK) en UNA sola sentencia sobre la llave primaria (paso 3 del orden de
    /// bloqueo) y las devuelve tracked, en TransportOrderId ascendente (con sus paradas si includeStops). Las que no son del
    /// tenant no aparecen. Exige transacción con proveedor relacional.
    /// </summary>
    public static async Task<IReadOnlyList<TransportOrder>> LockOrdersAsync(this TeikemDbContext db, IReadOnlyCollection<int> orderIds,
        bool includeStops, CancellationToken ct)
    {
        var ids = (orderIds ?? Array.Empty<int>()).Distinct().OrderBy(id => id).ToList();
        if (ids.Count == 0) return Array.Empty<TransportOrder>();

        IQueryable<TransportOrder> q;
        if (!db.Database.IsRelational())
            q = db.TransportOrders.AsTracking().Where(o => ids.Contains(o.TransportOrderId));
        else
        {
            RequireTransaction(db, nameof(LockOrdersAsync));
            var tenantId = db.CurrentTenantId;
            var json = JsonSerializer.Serialize(ids);
            q = db.TransportOrders
                .FromSqlInterpolated($"SELECT * FROM dbo.TransportOrder WITH (UPDLOCK, ROWLOCK) WHERE TenantId = {tenantId} AND TransportOrderId IN (SELECT CAST(value AS INT) FROM OPENJSON({json}))")
                .AsTracking();
        }
        if (includeStops) q = q.Include(o => o.Stops);
        var orders = await q.ToListAsync(ct);
        return orders.OrderBy(o => o.TransportOrderId).ToList();
    }

    /// <summary>
    /// Serializa 'Planificar el día' por tenant (paso 1 del orden de bloqueo): U lock sobre la fila Tenant hasta el fin de la
    /// transacción. El U lock es compatible con las lecturas S del resto de la aplicación. No-op con InMemory.
    /// </summary>
    public static async Task LockTenantPlanningAsync(this TeikemDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        RequireTransaction(db, nameof(LockTenantPlanningAsync));
        var tenantId = db.CurrentTenantId;
        _ = await db.Database
            .SqlQuery<int>($"SELECT TenantId AS Value FROM dbo.Tenant WITH (UPDLOCK, ROWLOCK) WHERE TenantId = {tenantId}")
            .ToListAsync(ct);
    }

    /// <summary>
    /// Versión vigente de la ruta (UX_Route_Trip_Active: a lo sumo una). Con track prefiere la instancia que ya esté en el
    /// tracker (sin las marcadas para borrar); sin track lee lo guardado. Route no tiene TenantId: el llamador ya resolvió el
    /// Trip bajo el filtro de tenant.
    /// </summary>
    public static async Task<Route?> ActiveRouteAsync(this TeikemDbContext db, int tripId, bool track, CancellationToken ct)
    {
        if (!track)
            return await db.Routes.AsNoTracking().Where(r => r.TripId == tripId && r.IsActive)
                .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);

        var local = db.ChangeTracker.Entries<Route>()
            .Where(e => e.State != EntityState.Deleted && e.Entity.TripId == tripId && e.Entity.IsActive)
            .Select(e => e.Entity)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();
        if (local is not null) return local;
        var loaded = await db.Routes.AsTracking().Where(r => r.TripId == tripId && r.IsActive)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);
        // Una versión cargada que el tracker ya tenía desactivada (sin guardar) no es la vigente.
        return loaded is { IsActive: true } ? loaded : null;
    }

    /// <summary>Ruta vigente de cada orden (TripOrder IsCurrent → Trip, ambos bajo el filtro de tenant), por TransportOrderId.</summary>
    public static async Task<Dictionary<int, CurrentTripRef>> CurrentTripsOfOrdersAsync(this TeikemDbContext db, IReadOnlyCollection<int> orderIds, CancellationToken ct)
    {
        var ids = (orderIds ?? Array.Empty<int>()).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, CurrentTripRef>();
        var rows = await (from to in db.TripOrders.AsNoTracking()
                          join t in db.Trips.AsNoTracking() on to.TripId equals t.TripId
                          join s in db.StatusCodes.AsNoTracking() on t.StatusCodeId equals s.StatusCodeId
                          where to.IsCurrent && ids.Contains(to.TransportOrderId)
                          select new { to.TransportOrderId, to.TripOrderId, t.TripId, t.PublicId, t.Code, s.InternalCode, t.DriverId })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.TransportOrderId)
            .ToDictionary(g => g.Key, g =>
            {
                var r = g.OrderByDescending(x => x.TripOrderId).First();
                return new CurrentTripRef(r.TripId, r.PublicId, r.Code, r.InternalCode, r.DriverId);
            });
    }

    /// <summary>
    /// Miembros de las zonas ACTIVAS del tenant (zona bajo el filtro de tenant; DispatchZoneMember no tiene TenantId y solo se
    /// alcanza por su zona), con el código del criterio. Base de DispatchZoneMatcher.Resolve / FindConflict.
    /// </summary>
    public static async Task<IReadOnlyList<ZoneMember>> ActiveZoneMembersAsync(this TeikemDbContext db, CancellationToken ct)
    {
        var rows = await (from m in db.DispatchZoneMembers.AsNoTracking()
                          join z in db.DispatchZones.AsNoTracking() on m.DispatchZoneId equals z.DispatchZoneId
                          join l in db.LookupCodes.AsNoTracking() on m.MatchTypeLookupId equals l.LookupCodeId
                          where z.IsActive
                          orderby z.Code, m.DispatchZoneMemberId
                          select new { z.Code, l.InternalCode, m.MatchValue })
            .ToListAsync(ct);
        return rows.Select(r => new ZoneMember(r.Code, r.InternalCode, r.MatchValue)).ToList();
    }

    /// <summary>
    /// Parada DELIVERY no terminal de menor secuencia de cada orden del tenant (la que se rutea), por TransportOrderId. Las
    /// órdenes sin parada pendiente no aparecen. deliveryStopTypeId sale de ILookupCache (StopType DELIVERY).
    /// </summary>
    public static async Task<Dictionary<int, PendingDeliveryStop>> PendingDeliveryStopsAsync(this TeikemDbContext db, IReadOnlyCollection<int> orderIds,
        int deliveryStopTypeId, CancellationToken ct)
    {
        var ids = (orderIds ?? Array.Empty<int>()).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, PendingDeliveryStop>();
        var terminalStopIds = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.StopStatus && s.StageKind != null && s.StageKind.InternalCode == StageKinds.Terminal)
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var rows = await (from os in db.OrderStops.AsNoTracking()
                          join o in db.TransportOrders.AsNoTracking() on os.TransportOrderId equals o.TransportOrderId
                          where ids.Contains(os.TransportOrderId) && os.StopTypeLookupId == deliveryStopTypeId
                                && !terminalStopIds.Contains(os.StatusCodeId)
                          select new
                          {
                              os.TransportOrderId, os.OrderStopId, os.Sequence, os.SnapPostalCode, os.SnapCity,
                              os.WindowStartUtc, os.WindowEndUtc, os.ServiceMinutes, os.GeocodeAccuracyLookupId,
                          })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.TransportOrderId)
            .ToDictionary(g => g.Key, g =>
            {
                var r = g.OrderBy(x => x.Sequence).ThenBy(x => x.OrderStopId).First();
                return new PendingDeliveryStop(r.TransportOrderId, r.OrderStopId, r.SnapPostalCode, r.SnapCity ?? string.Empty,
                    r.WindowStartUtc, r.WindowEndUtc, r.ServiceMinutes, r.GeocodeAccuracyLookupId);
            });
    }

    // ================================================================ GEOGRAPHY (SQL crudo con TenantId)

    /// <summary>
    /// Coordenadas (GeoPoint.Lat/.Long) y precisión de las paradas del tenant (JOIN a TransportOrder por TenantId), por
    /// OrderStopId. Las paradas sin coordenada no aparecen. Vacío con InMemory.
    /// </summary>
    public static async Task<Dictionary<int, StopPointRow>> StopPointsAsync(this TeikemDbContext db, IReadOnlyCollection<int> orderStopIds, CancellationToken ct)
    {
        var ids = (orderStopIds ?? Array.Empty<int>()).Distinct().ToList();
        if (ids.Count == 0 || !db.Database.IsRelational()) return new Dictionary<int, StopPointRow>();
        var tenantId = db.CurrentTenantId;
        var json = JsonSerializer.Serialize(ids);
        var rows = await db.Database.SqlQuery<PointSqlRow>(
                $"SELECT os.OrderStopId AS OrderStopId, os.GeoPoint.Lat AS Lat, os.GeoPoint.Long AS Lng, os.GeocodeAccuracyLookupId AS GeocodeAccuracyLookupId FROM dbo.OrderStop os JOIN dbo.TransportOrder o ON o.TransportOrderId = os.TransportOrderId WHERE o.TenantId = {tenantId} AND os.GeoPoint IS NOT NULL AND os.OrderStopId IN (SELECT CAST(value AS INT) FROM OPENJSON({json}))")
            .ToListAsync(ct);
        return rows.GroupBy(r => r.OrderStopId)
            .ToDictionary(g => g.Key, g => new StopPointRow(g.First().Lat, g.First().Lng, g.First().GeocodeAccuracyLookupId));
    }

    /// <summary>
    /// Pin manual: fija la coordenada (WGS84) de la parada del tenant. Corre en la transacción del llamador. 404 si no
    /// afecta exactamente una fila (parada inexistente o de otro tenant). No-op con InMemory.
    /// </summary>
    public static async Task SetStopPointAsync(this TeikemDbContext db, int orderStopId, double lat, double lng, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        var tenantId = db.CurrentTenantId;
        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE os SET GeoPoint = geography::Point({lat}, {lng}, 4326) FROM dbo.OrderStop os JOIN dbo.TransportOrder o ON o.TransportOrderId = os.TransportOrderId WHERE os.OrderStopId = {orderStopId} AND o.TenantId = {tenantId}", ct);
        if (affected != 1) throw new NotFoundException("Parada", null, true);
    }

    // ================================================================ pings (SQL crudo con TenantId)

    /// <summary>
    /// Último ping de cada ruta, en dos sentencias para todo el lote:
    /// 1. el último DriverLocationPing con ese TripId (ROW_NUMBER por TripId, CapturedAtUtc desc);
    /// 2. para las rutas sin ping, con chofer y salida real, el último ping del chofer SIN TripId desde ActualStartUtc
    ///    (LinkedToTrip = false).
    /// Vacío con InMemory o sin llaves.
    /// </summary>
    public static async Task<Dictionary<int, DriverPingRow>> LastPingsAsync(this TeikemDbContext db, IReadOnlyCollection<TripPingKey> keys, CancellationToken ct)
    {
        var result = new Dictionary<int, DriverPingRow>();
        var list = (keys ?? Array.Empty<TripPingKey>()).Where(k => k is not null).GroupBy(k => k.TripId).Select(g => g.First()).ToList();
        if (list.Count == 0 || !db.Database.IsRelational()) return result;
        var tenantId = db.CurrentTenantId;

        var tripJson = JsonSerializer.Serialize(list.Select(k => k.TripId).ToList());
        var byTrip = await db.Database.SqlQuery<PingSqlRow>(
                $"SELECT x.TripId, x.Lat, x.Lng, x.SpeedKmh, x.HeadingDeg, x.CapturedAtUtc, x.ReceivedAtUtc FROM (SELECT p.TripId AS TripId, p.GeoPoint.Lat AS Lat, p.GeoPoint.Long AS Lng, p.SpeedKmh AS SpeedKmh, p.HeadingDeg AS HeadingDeg, p.CapturedAtUtc AS CapturedAtUtc, p.ReceivedAtUtc AS ReceivedAtUtc, ROW_NUMBER() OVER (PARTITION BY p.TripId ORDER BY p.CapturedAtUtc DESC, p.DriverLocationPingId DESC) AS rn FROM dbo.DriverLocationPing p WHERE p.TenantId = {tenantId} AND p.TripId IN (SELECT CAST(value AS INT) FROM OPENJSON({tripJson}))) x WHERE x.rn = 1")
            .ToListAsync(ct);
        foreach (var r in byTrip)
            result[r.TripId] = new DriverPingRow(r.Lat, r.Lng, r.SpeedKmh, r.HeadingDeg, r.CapturedAtUtc, r.ReceivedAtUtc, true);

        var fallback = list.Where(k => !result.ContainsKey(k.TripId) && k.DriverId.HasValue && k.ActualStartUtc.HasValue).ToList();
        if (fallback.Count == 0) return result;
        var fallbackJson = JsonSerializer.Serialize(fallback.Select(k => new
        {
            tripId = k.TripId,
            driverId = k.DriverId!.Value,
            startUtc = k.ActualStartUtc!.Value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        }).ToList());
        var byDriver = await db.Database.SqlQuery<PingSqlRow>(
                $"SELECT k.TripId AS TripId, x.Lat, x.Lng, x.SpeedKmh, x.HeadingDeg, x.CapturedAtUtc, x.ReceivedAtUtc FROM OPENJSON({fallbackJson}) WITH (TripId INT '$.tripId', DriverId INT '$.driverId', StartUtc DATETIME2 '$.startUtc') k CROSS APPLY (SELECT TOP (1) p.GeoPoint.Lat AS Lat, p.GeoPoint.Long AS Lng, p.SpeedKmh AS SpeedKmh, p.HeadingDeg AS HeadingDeg, p.CapturedAtUtc AS CapturedAtUtc, p.ReceivedAtUtc AS ReceivedAtUtc FROM dbo.DriverLocationPing p WHERE p.TenantId = {tenantId} AND p.DriverId = k.DriverId AND p.TripId IS NULL AND p.CapturedAtUtc >= k.StartUtc ORDER BY p.CapturedAtUtc DESC, p.DriverLocationPingId DESC) x")
            .ToListAsync(ct);
        foreach (var r in byDriver)
            result[r.TripId] = new DriverPingRow(r.Lat, r.Lng, r.SpeedKmh, r.HeadingDeg, r.CapturedAtUtc, r.ReceivedAtUtc, false);
        return result;
    }

    // ================================================================ helpers

    private static NotFoundException TripNotFound() => new(TripLabel, null, true);

    private static void RequireTransaction(TeikemDbContext db, string what)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException($"{what} requiere una transacción abierta (RunInTransactionAsync).");
    }

    /// <summary>Fila de SqlQuery para coordenadas (tipo sin mapear: propiedades con setter).</summary>
    private sealed class PointSqlRow
    {
        public int OrderStopId { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public int? GeocodeAccuracyLookupId { get; set; }
    }

    /// <summary>Fila de SqlQuery para pings (tipo sin mapear: propiedades con setter).</summary>
    private sealed class PingSqlRow
    {
        public int TripId { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public decimal? SpeedKmh { get; set; }
        public int? HeadingDeg { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
    }
}
