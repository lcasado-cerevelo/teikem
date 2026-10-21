using System.Text.Json;

namespace Teikem.Domain.Orders;

/// <summary>
/// Lote 114 — marcas de un tipo de servicio (dictado por Luis): «pick up» (recojo y traigo: Sometida → Recogida → En almacén, que es final; no sale a ruta: si se quiere sacar es otra orden o el cliente viene a buscarlo) y
/// «facturable» (interruptor: un pick up puede cobrarse o no). Viven en el ExtraJson del tipo de servicio: <c>{"pickUp":true,"billable":true}</c>; el ajuste de la compañía (CustomExtraJson) pisa lo que diga el valor global.
/// Sin marcas: no es pick up y sí es facturable.
/// </summary>
public sealed record ServiceTypeMarks(bool PickUp, bool Billable)
{
    public static readonly ServiceTypeMarks Default = new(false, true);

    public static ServiceTypeMarks Parse(string? json) => Default.Apply(json);

    /// <summary>Aplica encima las marcas que traiga ese JSON (las que falten se quedan como estaban); un JSON roto se ignora.</summary>
    public ServiceTypeMarks Apply(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return this;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return this;
            var pickUp = PickUp;
            var billable = Billable;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Name.Equals("pickUp", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) pickUp = p.Value.GetBoolean();
                else if (p.Name.Equals("billable", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) billable = p.Value.GetBoolean();
            }
            return new ServiceTypeMarks(pickUp, billable);
        }
        catch (JsonException) { return this; }
    }

    public const string PickUpOnlyMessageFormat = "El envío {0} es de pick up: se queda en el almacén. Para sacarlo cree otro envío.";
}
