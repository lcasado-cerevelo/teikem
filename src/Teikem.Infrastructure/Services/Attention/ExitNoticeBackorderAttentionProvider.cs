using System.Globalization;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 194 — fila de "Necesita tu atención" por cada aviso de salida abierto con pendiente (backorder): lo que sigue sin reservarse aunque el sistema lo reserva solo (al confirmar
/// un recibo y cada 10 minutos). Una fila por aviso, los más antiguos primero. Tono: danger si ya hay inventario para reservar parte del pendiente (la reserva automática no se dio) o si
/// lleva más de 7 días esperando; warn si solo está esperando mercancía. Módulo WMS_LOTSERIAL y exitnotice.create. "Revisar" abre el aviso en Llegadas y salidas.
/// Params (cadenas): publicId, number, client, warehouse, lines, units (pendientes), fulfillable (ya reservables) y days.
/// </summary>
public sealed class ExitNoticeBackorderAttentionProvider(ExitNoticeService notices) : IAttentionItemProvider
{
    public const string ItemCode = "EXIT_NOTICE_BACKORDER";
    public const string Route = "/warehouse/notices";
    public const int LongWaitDays = 7;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.WmsLotSerial;
    public string RequiredPermission => PermissionCatalog.ExitNoticeCreate;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        var lines = await notices.BackordersAsync(null, ct);
        var docs = lines.GroupBy(l => l.NoticePublicId)
            .Select(g => new { First = g.OrderBy(l => l.RequestedAtUtc).First(), Lines = g.ToList() })
            .OrderBy(x => x.First.RequestedAtUtc).ThenBy(x => x.First.Number, StringComparer.Ordinal)
            .ToList();
        if (docs.Count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);

        var items = new List<AttentionItemDto>();
        foreach (var d in docs.Take(Math.Max(0, take)))
        {
            var days = d.Lines.Max(l => l.DaysWaiting);
            var fulfillable = d.Lines.Sum(l => l.Fulfillable);
            var publicId = d.First.NoticePublicId.ToString();
            var parameters = new Dictionary<string, string>
            {
                ["publicId"] = publicId,
                ["number"] = d.First.Number,
                ["client"] = d.First.ClientName,
                ["warehouse"] = d.First.WarehouseName,
                ["lines"] = d.Lines.Count.ToString(CultureInfo.InvariantCulture),
                ["units"] = d.Lines.Sum(l => l.QtyBackordered).ToString("0.###", CultureInfo.InvariantCulture),
                ["fulfillable"] = fulfillable.ToString("0.###", CultureInfo.InvariantCulture),
                ["days"] = days.ToString(CultureInfo.InvariantCulture),
            };
            items.Add(new AttentionItemDto(ItemCode, BusinessModules.Warehouse, fulfillable > 0m || days > LongWaitDays ? "danger" : "warn", 1, parameters, Route,
                new Dictionary<string, string> { ["tab"] = "exit-notices", ["notice"] = publicId }, d.First.RequestedAtUtc));
        }
        var group = new AttentionGroupDto(ItemCode, BusinessModules.Warehouse, docs.Count, Route, new Dictionary<string, string> { ["tab"] = "exit-notices" });
        return new AttentionProviderResult(docs.Count, items, group);
    }
}
