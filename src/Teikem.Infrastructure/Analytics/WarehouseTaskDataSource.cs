using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 6 (P5) — fuente de datos WAREHOUSE_TASK (tareas de almacén del tenant) para vistas, indicadores ('Tareas de almacén
/// pendientes') y gráficos. Actividad de período por CreatedAtUtc (desde inclusivo, hasta exclusivo). Sigue el patrón de
/// las fuentes del Lote 5: AsNoTracking bajo el filtro de tenant, tope ClientDataSourceHelpers.MaxRows, respeto de q.Ids.
/// - Las filas se arman con el mismo WarehouseTaskReads que la cola (sin N+1): tipo, estatus con la etiqueta del tenant,
///   almacén, SKU, posiciones, asignado y origen legible (RefLabel).
/// - AgeHours = horas (1 decimal) desde el alta hasta el cierre o, si sigue abierta, hasta ahora (WarehouseTaskRules.AgeHours).
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (AnalyticsSeedFieldsTests lo verifica).
/// </summary>
public sealed class WarehouseTaskDataSource(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups) : IDataSource
{
    public string Key => EntityTypes.WarehouseTask;
    public string LabelEs => "Tareas de almacén";
    public string LabelEn => "Warehouse tasks";
    public string? EntityTypeCode => EntityTypes.WarehouseTask;
    public string? DateField => "CreatedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("CreatedAtUtc", "Creada el", "Created at", DataFieldType.Date),
        new DataField("Type", "Tipo", "Type", DataFieldType.Text),
        new DataField("TypeCode", "Código de tipo", "Type code", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("Priority", "Prioridad", "Priority", DataFieldType.Number),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("Quantity", "Cantidad", "Quantity", DataFieldType.Number),
        new DataField("FromBin", "Posición de origen", "From bin", DataFieldType.Text),
        new DataField("ToBin", "Posición de destino", "To bin", DataFieldType.Text),
        new DataField("AssignedTo", "Asignada a", "Assigned to", DataFieldType.Text),
        new DataField("CompletedAtUtc", "Completada el", "Completed at", DataFieldType.Date),
        new DataField("AgeHours", "Antigüedad (horas)", "Age (hours)", DataFieldType.Number),
        new DataField("RefLabel", "Origen", "Source", DataFieldType.Text),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<WarehouseTask>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(t => wanted.Contains(t.WarehouseTaskId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(t => t.CreatedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(t => t.CreatedAtUtc < to);

        var tasks = await query.OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.WarehouseTaskId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (tasks.Count == 0) return new List<DataRow>();

        var dtos = await WarehouseTaskReads.BuildAsync(db, tenant, lookups, Array.Empty<IWarehouseTaskHandler>(), tasks, ct);
        var now = DateTime.UtcNow;
        var rows = new List<DataRow>(dtos.Count);
        foreach (var t in dtos)
        {
            rows.Add(new DataRow
            {
                ["Id"] = t.Id,
                ["CreatedAtUtc"] = t.CreatedAtUtc,
                ["Type"] = t.Type,
                ["TypeCode"] = t.TypeCode,
                ["Status"] = t.Status,
                ["StatusCode"] = t.StatusCode,
                ["Priority"] = t.Priority,
                ["WarehouseCode"] = t.WarehouseCode,
                ["Sku"] = t.Sku,
                ["Quantity"] = t.Quantity,
                ["FromBin"] = t.FromBinCode,
                ["ToBin"] = t.ToBinCode,
                ["AssignedTo"] = t.AssignedToName,
                ["CompletedAtUtc"] = t.CompletedAtUtc,
                ["AgeHours"] = WarehouseTaskRules.AgeHours(t.CreatedAtUtc, t.CompletedAtUtc, now),
                ["RefLabel"] = t.RefLabel,
            });
        }
        return rows;
    }
}
