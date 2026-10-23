using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Geocoding;

/// <summary>
/// 2026-10-22 — busca la latitud y la longitud de las paradas que están en una ruta y todavía no la tienen (pedido de Luis: solo cuando se arma la ruta). Corre en segundo plano, fuera de la petición del despachador:
/// 1) toma las paradas de la versión vigente de rutas no cerradas de hoy en adelante (ayer incluido), sin ubicación y sin intento; 2) cada una se «reclama» con un UPDATE atómico (varias copias del API no la buscan
/// dos veces); 3) si la dirección ya se buscó (misma compañía y misma dirección normalizada) usa lo guardado y NO consulta a Google; 4) si no, reserva una consulta del mes con un UPDATE atómico contra el tope
/// (<c>GoogleMaps:MonthlyCap</c>): sin cupo, la parada queda sin intento y se retoma el mes siguiente; 5) consulta a Google (un intento, 30 s) y guarda el resultado por dirección y en la parada, con su precisión.
/// Tres fallos seguidos detienen el ciclo (llave o cuota mal: no se sigue gastando). El SQL va con la compañía explícita (como TripQueries); GeoPoint (GEOGRAPHY) y las columnas técnicas no se mapean en EF.
/// Los métodos son públicos para las pruebas con SQL real.
/// </summary>
public sealed class StopGeocodeProcessor(IServiceScopeFactory scopes, IOptions<GoogleMapsOptions> options, ILogger<StopGeocodeProcessor> logger)
{
    public const int BatchSize = 25;
    private bool _capWarned;
    private int? _exactId;
    private int? _approxId;

    /// <summary>Una pasada: devuelve cuántas paradas quedaron resueltas (con ubicación o marcadas como no encontradas).</summary>
    public async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var opt = options.Value;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
        var geocoder = scope.ServiceProvider.GetRequiredService<IAddressGeocoder>();
        var now = DateTime.UtcNow;

        // un RUNNING viejo (el API se cayó a medias) se da por fallido; se reintenta pasado un rato
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OrderStop SET GeoStatus = {StopGeocodeRules.StatusFailed} WHERE GeoStatus = {StopGeocodeRules.StatusRunning} AND GeoCheckedAtUtc < {now - StopGeocodeRules.StaleAfter}", ct);

        var cap = opt.MonthlyCap;
        var month = StopGeocodeRules.UsageMonth(now);
        var retryBefore = now - StopGeocodeRules.RetryFailedAfter;
        var minPlanDate = DateOnly.FromDateTime(now.AddDays(-1).Date);
        var due = await db.Database.SqlQuery<Candidate>($@"
            SELECT TOP ({BatchSize}) os.OrderStopId AS StopId, o.TenantId AS TenantId, os.SnapLine1 AS Line1, os.SnapLine2 AS Line2, os.SnapCity AS City, os.SnapState AS State, os.SnapPostalCode AS PostalCode
            FROM dbo.RouteStop rs
            JOIN dbo.Route r ON r.RouteId = rs.RouteId AND r.IsActive = 1
            JOIN dbo.Trip t ON t.TripId = r.TripId AND t.IsActive = 1 AND t.PlanDate >= {minPlanDate}
            JOIN dbo.StatusCode sc ON sc.StatusCodeId = t.StatusCodeId AND sc.Entity = {StatusDomains.TripStatus} AND sc.InternalCode NOT IN ({TripStatuses.Completed}, {TripStatuses.Cancelled})
            JOIN dbo.OrderStop os ON os.OrderStopId = rs.OrderStopId
            JOIN dbo.TransportOrder o ON o.TransportOrderId = os.TransportOrderId
            WHERE os.GeoPoint IS NULL AND (os.GeoStatus IS NULL OR (os.GeoStatus = {StopGeocodeRules.StatusFailed} AND os.GeoCheckedAtUtc < {retryBefore}))
            ORDER BY t.PlanDate, rs.RouteStopId").ToListAsync(ct);

        var resolved = 0;
        var failures = 0;
        foreach (var c in due)
        {
            ct.ThrowIfCancellationRequested();
            // reclamar
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OrderStop SET GeoStatus = {StopGeocodeRules.StatusRunning}, GeoCheckedAtUtc = {DateTime.UtcNow} WHERE OrderStopId = {c.StopId} AND GeoPoint IS NULL AND (GeoStatus IS NULL OR GeoStatus = {StopGeocodeRules.StatusFailed})", ct);
            if (claimed == 0) continue;

            var address = StopGeocodeRules.BuildAddress(c.Line1, c.Line2, c.City, c.State, c.PostalCode);
            var key = StopGeocodeRules.AddressKey(c.Line1, c.Line2, c.City, c.State, c.PostalCode);
            if (key.Length == 0) { await SetStatusAsync(db, c.StopId, StopGeocodeRules.StatusNotFound, ct); resolved++; continue; }

            // ya buscada: se usa lo guardado, sin consultar
            var cached = (await db.Database.SqlQuery<CachedRow>($"SELECT Status AS Status, Latitude AS Lat, Longitude AS Lng, Confidence AS Confidence FROM dbo.AddressGeocode WHERE TenantId = {c.TenantId} AND AddressKey = {key}").ToListAsync(ct)).FirstOrDefault();
            if (cached is not null)
            {
                if (cached.Status == StopGeocodeRules.StatusOk && cached.Lat is decimal clat && cached.Lng is decimal clng) await ApplyAsync(db, c.StopId, clat, clng, cached.Confidence, ct);
                else await SetStatusAsync(db, c.StopId, StopGeocodeRules.StatusNotFound, ct);
                resolved++;
                continue;
            }

            // una consulta del mes, con tope
            if (!await ReserveCallAsync(db, month, cap, ct))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OrderStop SET GeoStatus = NULL, GeoCheckedAtUtc = NULL WHERE OrderStopId = {c.StopId} AND GeoStatus = {StopGeocodeRules.StatusRunning}", ct);   // sin intento: se retoma cuando haya cupo
                if (!_capWarned) { logger.LogWarning("Se llegó al tope mensual de {Cap} consultas de ubicación (GoogleMaps:MonthlyCap); las paradas quedan sin ubicación hasta el mes siguiente o hasta que se suba el tope.", cap); _capWarned = true; }
                break;
            }
            _capWarned = false;

            GeocodeOutcome outcome;
            try { outcome = await geocoder.GeocodeAsync(address, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogError(ex, "La ubicación de la parada {StopId} falló.", c.StopId); outcome = GeocodeOutcome.Failed; }

            if (outcome.Status == DeliveryProofCodes.GeoOk && outcome.Lat is decimal lat && outcome.Lng is decimal lng)
            {
                await SaveCacheAsync(db, c.TenantId, key, StopGeocodeRules.StatusOk, lat, lng, outcome.Confidence, ct);
                await ApplyAsync(db, c.StopId, lat, lng, outcome.Confidence, ct);
                resolved++;
                failures = 0;
            }
            else if (outcome.Status == DeliveryProofCodes.GeoNotFound)
            {
                await SaveCacheAsync(db, c.TenantId, key, StopGeocodeRules.StatusNotFound, null, null, null, ct);
                await SetStatusAsync(db, c.StopId, StopGeocodeRules.StatusNotFound, ct);
                resolved++;
                failures = 0;
            }
            else
            {
                await SetStatusAsync(db, c.StopId, StopGeocodeRules.StatusFailed, ct);   // se reintenta pasado un rato
                if (++failures >= StopGeocodeRules.MaxConsecutiveFailures) { logger.LogWarning("Google falló {N} veces seguidas al ubicar paradas; se detiene este ciclo (revise la llave y la cuota).", failures); break; }
            }
        }
        return resolved;
    }

    /// <summary>Reserva una consulta del mes con un UPDATE atómico contra el tope: false si ya no hay cupo (o el tope es cero).</summary>
    public static async Task<bool> ReserveCallAsync(TeikemDbContext db, string month, int cap, CancellationToken ct)
    {
        if (cap <= 0) return false;
        await db.Database.ExecuteSqlInterpolatedAsync($"IF NOT EXISTS (SELECT 1 FROM dbo.GeocodeUsage WHERE UsageMonth = {month}) BEGIN TRY INSERT INTO dbo.GeocodeUsage (UsageMonth, Calls) VALUES ({month}, 0) END TRY BEGIN CATCH END CATCH", ct);
        var reserved = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.GeocodeUsage SET Calls = Calls + 1 WHERE UsageMonth = {month} AND Calls < {cap}", ct);
        return reserved == 1;
    }

    private static Task<int> SetStatusAsync(TeikemDbContext db, int stopId, string status, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OrderStop SET GeoStatus = {status}, GeoCheckedAtUtc = {DateTime.UtcNow} WHERE OrderStopId = {stopId}", ct);

    private static Task<int> SaveCacheAsync(TeikemDbContext db, int tenantId, string key, string status, decimal? lat, decimal? lng, string? confidence, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($@"
            MERGE dbo.AddressGeocode AS t USING (SELECT {tenantId} AS TenantId, {key} AS AddressKey) AS s ON t.TenantId = s.TenantId AND t.AddressKey = s.AddressKey
            WHEN MATCHED THEN UPDATE SET Status = {status}, Latitude = {lat}, Longitude = {lng}, Confidence = {confidence}, Attempts = t.Attempts + 1, CheckedAtUtc = {DateTime.UtcNow}
            WHEN NOT MATCHED THEN INSERT (TenantId, AddressKey, Status, Latitude, Longitude, Confidence, Attempts, CheckedAtUtc) VALUES (s.TenantId, s.AddressKey, {status}, {lat}, {lng}, {confidence}, 1, {DateTime.UtcNow});", ct);

    /// <summary>Guarda la ubicación en la parada con su precisión (EXACT si Google dio techo; lo demás, aproximado).</summary>
    private async Task ApplyAsync(TeikemDbContext db, int stopId, decimal lat, decimal lng, string? confidence, CancellationToken ct)
    {
        var accuracyId = await AccuracyIdAsync(db, StopGeocodeRules.AccuracyOf(confidence), ct);
        var la = (double)lat;
        var lo = (double)lng;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OrderStop SET GeoPoint = geography::Point({la}, {lo}, 4326), GeocodeAccuracyLookupId = {accuracyId}, GeoStatus = {StopGeocodeRules.StatusOk}, GeoCheckedAtUtc = {DateTime.UtcNow} WHERE OrderStopId = {stopId}", ct);
    }

    private async Task<int?> AccuracyIdAsync(TeikemDbContext db, string code, CancellationToken ct)
    {
        if (code == GeocodeAccuracies.Exact && _exactId is int e) return e;
        if (code != GeocodeAccuracies.Exact && _approxId is int a) return a;
        var id = (await db.Database.SqlQuery<int>($"SELECT LookupCodeId AS Value FROM dbo.LookupCode WHERE Entity = {LookupDomains.GeocodeAccuracy} AND InternalCode = {code} AND TenantId IS NULL").ToListAsync(ct)).Cast<int?>().FirstOrDefault();
        if (code == GeocodeAccuracies.Exact) _exactId = id; else _approxId = id;
        return id;
    }

    public sealed record Candidate(int StopId, int TenantId, string? Line1, string? Line2, string? City, string? State, string? PostalCode);
    public sealed record CachedRow(string Status, decimal? Lat, decimal? Lng, string? Confidence);
}

/// <summary>Despierta cada pocos segundos al procesador de ubicaciones de paradas (solo si Google está encendido y con llave).</summary>
public sealed class StopGeocodeWorker(StopGeocodeProcessor processor, IOptions<GoogleMapsOptions> options, ILogger<StopGeocodeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.IsUsable) await processor.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "El ciclo de ubicaciones de paradas falló."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 1, 300)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
