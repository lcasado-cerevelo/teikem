using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 3 (P5) — fuente de datos de Órdenes de transporte para vistas, indicadores y gráficos (módulos G/H/I).
/// Sigue el patrón de ClientDataSources: lectura AsNoTracking, tope de 20 000 filas, etiquetas resueltas con
/// ILookupCache/MultilingualText y mapas de StatusCode por dominio, respeto de q.Ids (relaciones muchos-a-uno) y del
/// rango q.FromUtc/q.ToUtc sobre CreatedAtUtc (desde inclusivo, hasta exclusivo).
/// - Las paradas y las líneas no llevan TenantId: se alcanzan SOLO a través de las órdenes ya filtradas por tenant
///   (SelectMany sobre db.TransportOrders), en dos consultas por ids (sin N+1).
/// - Los nombres de campo son los que usa SystemAnalyticsSeeder (vista "Órdenes", indicadores "Órdenes en curso" y
///   "COD por cobrar" —este usa CodStatusCode—, gráfico "Órdenes por estatus"): no cambiarlos sin cambiar el seeder.
/// </summary>
public sealed class TransportOrderDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.TransportOrder;
    public string LabelEs => "Órdenes";
    public string LabelEn => "Orders";
    /// <summary>Habilita los campos personalizados de la orden como columnas (módulo F).</summary>
    public string? EntityTypeCode => EntityTypes.TransportOrder;
    /// <summary>Actividad de período por fecha de captura (decisión 22 del Lote 3).</summary>
    public string? DateField => "CreatedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("PackBatchNumber", "Empaque", "Pack batch", DataFieldType.Text),
        new DataField("OrderNumber", "Número de orden", "Order number", DataFieldType.Text),
        new DataField("ClientInvoiceNumber", "Factura del cliente", "Client invoice", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("ConsigneeName", "Consignatario", "Consignee", DataFieldType.Text),
        new DataField("ConsigneeCity", "Pueblo del consignatario", "Consignee city", DataFieldType.Text),
        new DataField("ConsigneePostalCode", "Código postal del consignatario", "Consignee postal code", DataFieldType.Text),
        new DataField("ConsigneeLocationId", "Id de consignatario", "Consignee location id", DataFieldType.Number),
        new DataField("ServiceType", "Tipo de servicio", "Service type", DataFieldType.Text),
        new DataField("ServiceTypeCode", "Código de tipo de servicio", "Service type code", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("IsSpecialDelivery", "Entrega especial", "Special delivery", DataFieldType.Bool),
        new DataField("SpecialServiceName", "Servicio especial", "Special service", DataFieldType.Text),
        new DataField("TotalPieces", "Piezas", "Pieces", DataFieldType.Number),
        new DataField("PackagesSummary", "Paquetes", "Packages", DataFieldType.Text),
        new DataField("CodAmount", "Monto COD", "COD amount", DataFieldType.Number, IsMoney: true),
        new DataField("CodStatus", "Estatus COD", "COD status", DataFieldType.Text),
        new DataField("CodStatusCode", "Código de estatus COD", "COD status code", DataFieldType.Text),
        new DataField("QuotedAmount", "Monto cotizado", "Quoted amount", DataFieldType.Number, IsMoney: true),
        new DataField("ConfirmedAtUtc", "Confirmada el", "Confirmed at", DataFieldType.Date),
        new DataField("CreatedAtUtc", "Creada el", "Created at", DataFieldType.Date),
        new DataField("IsActive", "Activa", "Active", DataFieldType.Bool),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
        new DataRelation("Consignee", EntityTypes.Location, "ConsigneeLocationId", "Consignatario", "Consignee"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.TransportOrders.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(o => wanted.Contains(o.TransportOrderId));
        }
        // Rango sobre CreatedAtUtc: desde inclusivo, hasta exclusivo (el resolutor de rangos entrega "mañana 00:00")
        if (q.FromUtc.HasValue) query = query.Where(o => o.CreatedAtUtc >= q.FromUtc.Value);
        if (q.ToUtc.HasValue) query = query.Where(o => o.CreatedAtUtc < q.ToUtc.Value);

        var orders = await query.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.TransportOrderId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (orders.Count == 0) return new List<DataRow>();

        var ids = orders.Select(o => o.TransportOrderId).ToList();
        var lang = tenant.Lang;
        var orderStatus = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.OrderStatus, ct);
        var codStatus = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.CodStatus, ct);

        // Parada DELIVERY (consignatario) de cada orden: una consulta por ids a través de las órdenes del tenant
        var deliveryTypeId = await lookups.TryGetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var deliveryByOrder = deliveryTypeId is null
            ? new Dictionary<int, OrderStop>()
            : (await db.TransportOrders.AsNoTracking()
                .Where(o => ids.Contains(o.TransportOrderId))
                .SelectMany(o => o.Stops)
                .Where(s => s.StopTypeLookupId == deliveryTypeId.Value)
                .ToListAsync(ct))
                .GroupBy(s => s.TransportOrderId)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Sequence).ThenBy(s => s.OrderStopId).First());

        // Líneas activas de cada orden: segunda consulta por ids (sin N+1)
        var linesByOrder = (await db.TransportOrders.AsNoTracking()
                .Where(o => ids.Contains(o.TransportOrderId))
                .SelectMany(o => o.CargoLines)
                .Where(l => l.IsActive)
                .ToListAsync(ct))
            .GroupBy(l => l.TransportOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.CargoLineId).ToList());

        // Nombres de cliente y de servicio especial, una consulta cada uno
        var clientIds = orders.Select(o => o.ClientId).Distinct().ToList();
        var clientNames = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);

        var specialIds = orders.Where(o => o.SpecialServiceId.HasValue).Select(o => o.SpecialServiceId!.Value).Distinct().ToList();
        var specialNames = specialIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SpecialServices.AsNoTracking().Where(s => specialIds.Contains(s.SpecialServiceId))
                .Select(s => new { s.SpecialServiceId, Name = s.Type!.Name }).ToDictionaryAsync(s => s.SpecialServiceId, s => s.Name, ct);

        var rows = new List<DataRow>(orders.Count);
        foreach (var o in orders)
        {
            var delivery = deliveryByOrder.GetValueOrDefault(o.TransportOrderId);
            var lines = linesByOrder.GetValueOrDefault(o.TransportOrderId);
            var specialName = o.SpecialServiceId is int ssId ? specialNames.GetValueOrDefault(ssId) : null;

            rows.Add(new DataRow
            {
                ["Id"] = o.TransportOrderId,
                ["PublicId"] = o.PublicId.ToString(),
                ["PackBatchNumber"] = o.PackBatchNumber,
                ["OrderNumber"] = o.OrderNumber,
                ["ClientInvoiceNumber"] = o.ClientInvoiceNumber,
                ["ClientId"] = o.ClientId,
                ["ClientName"] = clientNames.GetValueOrDefault(o.ClientId),
                ["ConsigneeName"] = delivery?.SnapName,
                ["ConsigneeCity"] = delivery?.SnapCity,
                ["ConsigneePostalCode"] = delivery?.SnapPostalCode,
                ["ConsigneeLocationId"] = delivery?.LocationId,
                ["ServiceType"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, o.ServiceTypeLookupId, lang, ct),
                ["ServiceTypeCode"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, o.ServiceTypeLookupId, ct),
                ["Status"] = ClientDataSourceHelpers.StatusLabel(orderStatus, o.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(orderStatus, o.StatusCodeId),
                ["IsSpecialDelivery"] = o.IsSpecialDelivery,
                ["SpecialServiceName"] = specialName,
                ["TotalPieces"] = o.TotalPieces,
                ["PackagesSummary"] = await PackagesSummaryAsync(o.IsSpecialDelivery, specialName, lines, lang, ct),
                ["CodAmount"] = o.CodAmount,
                ["CodStatus"] = o.CodStatusCodeId is int cs ? ClientDataSourceHelpers.StatusLabel(codStatus, cs, lang) : null,
                ["CodStatusCode"] = o.CodStatusCodeId is int csc ? ClientDataSourceHelpers.StatusCodeOf(codStatus, csc) : null,
                ["QuotedAmount"] = o.QuotedAmount,
                ["ConfirmedAtUtc"] = o.ConfirmedAtUtc,
                ["CreatedAtUtc"] = o.CreatedAtUtc,
                ["IsActive"] = o.IsActive,
            });
        }
        return rows;
    }

    /// <summary>
    /// Resumen "Caja ×2 + Sobre ×1" consolidado por tipo de paquete (OrderRules.PackagesSummary); una entrega especial
    /// muestra el nombre del servicio. Una línea sin tipo (solo posible en la especial) usa su descripción.
    /// </summary>
    private async Task<string?> PackagesSummaryAsync(bool isSpecial, string? specialName, List<CargoLine>? lines, string lang, CancellationToken ct)
    {
        if (isSpecial) return specialName ?? lines?.FirstOrDefault()?.Description;
        if (lines is null || lines.Count == 0) return null;

        var labeled = new List<(string Label, int Pieces)>(lines.Count);
        foreach (var l in lines)
        {
            var label = await ClientDataSourceHelpers.LookupLabelAsync(lookups, l.PackageTypeLookupId, lang, ct) ?? l.Description;
            labeled.Add((label, (int)l.Quantity));
        }
        return OrderRules.PackagesSummary(labeled);
    }
}
