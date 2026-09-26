using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Domain.Trips;

/// <summary>Parada que no cupo en una corrida, tal como se guarda en OptimizationRun.ResponseJson.</summary>
public sealed record RoutePlanUnassigned(Guid OrderPublicId, string OrderNumber, string ReasonCode);

/// <summary>Plan resultante de una corrida OK: paradas en secuencia, las que no cupieron y la geometría.</summary>
public sealed record RoutePlanResponse(IReadOnlyList<int> OrderedStopIds, IReadOnlyList<RoutePlanUnassigned> Unassigned, string? Polyline);

/// <summary>
/// Lote 5 (P0) — (de)serialización del plan de una corrida (OptimizationRun.ResponseJson). La lectura es TOLERANTE: la ficha
/// muestra el motivo de las paradas que no entraron y un JSON nulo, vacío, dañado o con otra forma nunca debe romperla
/// (devuelve una lista vacía y omite los elementos incompletos). NUNCA lanza.
/// </summary>
public static class RoutePlanJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(RoutePlanResponse plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(new
        {
            orderedStopIds = plan.OrderedStopIds ?? Array.Empty<int>(),
            unassigned = (plan.Unassigned ?? Array.Empty<RoutePlanUnassigned>())
                .Select(u => new { orderPublicId = u.OrderPublicId, orderNumber = u.OrderNumber, reasonCode = u.ReasonCode }),
            polyline = plan.Polyline,
        }, Options);
    }

    public static IReadOnlyList<RoutePlanUnassigned> ParseUnassigned(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<RoutePlanUnassigned>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<RoutePlanUnassigned>();
            if (!TryGet(doc.RootElement, "unassigned", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return Array.Empty<RoutePlanUnassigned>();

            var list = new List<RoutePlanUnassigned>();
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (!TryGet(e, "orderPublicId", out var idEl) || idEl.ValueKind != JsonValueKind.String || !idEl.TryGetGuid(out var id)) continue;
                if (!TryGet(e, "orderNumber", out var numEl) || numEl.ValueKind != JsonValueKind.String) continue;
                if (!TryGet(e, "reasonCode", out var reasonEl) || reasonEl.ValueKind != JsonValueKind.String) continue;
                var number = numEl.GetString();
                var reason = reasonEl.GetString();
                if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(reason)) continue;
                list.Add(new RoutePlanUnassigned(id, number, reason));
            }
            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<RoutePlanUnassigned>();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return Array.Empty<RoutePlanUnassigned>();
        }
    }

    /// <summary>Propiedad por nombre sin distinguir mayúsculas (tolera 'Unassigned' y 'unassigned').</summary>
    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
