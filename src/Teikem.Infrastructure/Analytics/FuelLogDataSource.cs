using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 4 (P5) — fuente de datos de la bitácora de combustible para vistas, indicadores y gráficos (módulos G/H/I).
/// Sigue el patrón de TransportOrderDataSource: lectura AsNoTracking bajo el filtro de tenant, tope de
/// ClientDataSourceHelpers.MaxRows filas, respeto de q.Ids (relaciones muchos-a-uno) y del rango q.FromUtc/q.ToUtc sobre
/// FillDateUtc (desde inclusivo, hasta exclusivo).
/// - DistanceKm/KmPerLiter/CostPerKm se calculan con FuelEfficiency sobre la serie ACTIVA COMPLETA de cada vehículo (no
///   sobre el rango): la primera carga del período se mide contra la anterior aunque quede fuera. Una carga inactiva
///   aparece (IsActive = false) pero sin eficiencia.
/// - Nombres de campo estables: no cambiarlos sin revisar el contenido de análisis que los use.
/// </summary>
public sealed class FuelLogDataSource(TeikemDbContext db) : IDataSource
{
    public string Key => EntityTypes.FuelLog;
    public string LabelEs => "Cargas de combustible";
    public string LabelEn => "Fuel logs";
    public string? EntityTypeCode => EntityTypes.FuelLog;
    /// <summary>Actividad de período por fecha de la carga.</summary>
    public string? DateField => "FillDateUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("VehicleId", "Id de vehículo", "Vehicle id", DataFieldType.Number),
        new DataField("VehicleCode", "Vehículo", "Vehicle", DataFieldType.Text),
        new DataField("DriverId", "Id de chofer", "Driver id", DataFieldType.Number),
        new DataField("DriverName", "Chofer", "Driver", DataFieldType.Text),
        new DataField("FillDateUtc", "Fecha de la carga", "Fill date", DataFieldType.Date),
        new DataField("OdometerKm", "Odómetro (km)", "Odometer (km)", DataFieldType.Number),
        new DataField("Liters", "Litros", "Liters", DataFieldType.Number),
        new DataField("TotalCost", "Costo total", "Total cost", DataFieldType.Number, IsMoney: true),
        new DataField("Station", "Estación", "Station", DataFieldType.Text),
        new DataField("DistanceKm", "Distancia (km)", "Distance (km)", DataFieldType.Number),
        new DataField("KmPerLiter", "km/L", "km/L", DataFieldType.Number),
        new DataField("CostPerKm", "Costo por km", "Cost per km", DataFieldType.Number, IsMoney: true),
        new DataField("IsActive", "Activa", "Active", DataFieldType.Bool),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Vehicle", EntityTypes.Vehicle, "VehicleId", "Vehículo", "Vehicle"),
        new DataRelation("Driver", EntityTypes.Driver, "DriverId", "Chofer", "Driver"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.FuelLogs.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(f => wanted.Contains(f.FuelLogId));
        }
        if (q.FromUtc.HasValue) query = query.Where(f => f.FillDateUtc >= q.FromUtc.Value);
        if (q.ToUtc.HasValue) query = query.Where(f => f.FillDateUtc < q.ToUtc.Value);

        var logs = await query.OrderByDescending(f => f.FillDateUtc).ThenByDescending(f => f.FuelLogId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (logs.Count == 0) return new List<DataRow>();

        // Serie activa completa de los vehículos presentes: una consulta (sin N+1).
        var vehicleIds = logs.Select(l => l.VehicleId).Distinct().ToList();
        var series = await db.FuelLogs.AsNoTracking()
            .Where(f => f.IsActive && vehicleIds.Contains(f.VehicleId))
            .Select(f => new { f.VehicleId, f.FuelLogId, f.FillDateUtc, f.OdometerKm, f.Liters, f.TotalCost })
            .ToListAsync(ct);
        var efficiency = series.GroupBy(s => s.VehicleId)
            .SelectMany(g => FuelEfficiency.Compute(g.Select(s => new FuelReading(s.FuelLogId, s.FillDateUtc, s.OdometerKm, s.Liters, s.TotalCost))).Rows)
            .ToDictionary(r => r.Id);

        var vehicleCodes = await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .Select(v => new { v.VehicleId, v.Code }).ToDictionaryAsync(v => v.VehicleId, v => v.Code, ct);

        var driverIds = logs.Where(l => l.DriverId.HasValue).Select(l => l.DriverId!.Value).Distinct().ToList();
        var driverNames = driverIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.DriverId))
                .Select(d => new { d.DriverId, d.FullName }).ToDictionaryAsync(d => d.DriverId, d => d.FullName, ct);

        var rows = new List<DataRow>(logs.Count);
        foreach (var l in logs)
        {
            var e = l.IsActive ? efficiency.GetValueOrDefault(l.FuelLogId) : null;
            rows.Add(new DataRow
            {
                ["Id"] = l.FuelLogId,
                ["VehicleId"] = l.VehicleId,
                ["VehicleCode"] = vehicleCodes.GetValueOrDefault(l.VehicleId),
                ["DriverId"] = l.DriverId,
                ["DriverName"] = l.DriverId is int did ? driverNames.GetValueOrDefault(did) : null,
                ["FillDateUtc"] = l.FillDateUtc,
                ["OdometerKm"] = l.OdometerKm,
                ["Liters"] = l.Liters,
                ["TotalCost"] = l.TotalCost,
                ["Station"] = l.Station,
                ["DistanceKm"] = e?.DistanceKm,
                ["KmPerLiter"] = e?.KmPerLiter,
                ["CostPerKm"] = e?.CostPerKm,
                ["IsActive"] = l.IsActive,
            });
        }
        return rows;
    }
}
