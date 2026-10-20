using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Códigos de los eventos de aviso de inventario (Lote 141, 2026-10-20). Hasta 60 caracteres (columna EventCode).</summary>
public static class InventoryEventCodes
{
    public const string LotExpiring = "INVENTORY_LOT_EXPIRING";
    public const string LotExpired = "INVENTORY_LOT_EXPIRED";
    public const string StockLow = "INVENTORY_STOCK_LOW";
    public const string StockOut = "INVENTORY_STOCK_OUT";
    public const string NoMovement = "INVENTORY_NO_MOVEMENT";
    public const string NoCost = "INVENTORY_NO_COST";

    public static readonly string[] All = { LotExpiring, LotExpired, StockLow, StockOut, NoMovement, NoCost };
}

/// <summary>
/// Lote 141 — los eventos de aviso del inventario. Los publica el trabajo diario INVENTORY_ALERT_SCAN (<see cref="Services.InventoryAlertService"/>): UN aviso por evento con el
/// conjunto completo (no uno por lote), que no se repite mientras el conjunto sea el mismo en la misma semana. Todos son opcionales (el usuario puede silenciarlos en su bandeja).
/// Las reglas (a quién y por qué canal) las arma el administrador en Sistema → Reglas de aviso: no se siembra ninguna. Para agregar un evento nuevo de inventario: un código aquí, su
/// definición abajo y su detección en <see cref="Services.InventoryAlertService"/>; el motor de avisos no cambia.
/// </summary>
public sealed class InventoryEventProvider : INotificationEventProvider
{
    private static NotificationVariable Count(string sample) => new("count", "Cantidad", "Count", sample);
    private static NotificationVariable Items(string sample) => new("items", "Detalle (uno por renglón)", "Detail (one per line)", sample);

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            InventoryEventCodes.LotExpiring, ModuleKeys.WmsLotSerial, "Lotes por vencer", "Lots about to expire",
            new[] { Count("3"), new NotificationVariable("days", "Días de anticipación", "Days ahead", "30"), Items("MED-1 · L-3 · 2026-11-02 · 40 · ALM-01") },
            IsOptional: true, LinkTemplate: "/warehouse/kardex",
            "{count} lote(s) por vencer en {days} días", "{count} lot(s) expiring within {days} days",
            "Hay {count} lote(s) con existencia que vencen en los próximos {days} días (producto · lote · vencimiento · cantidad · almacén):\n{items}",
            "{count} lot(s) with stock expire within the next {days} days (product · lot · expiry · quantity · warehouse):\n{items}"),
        new NotificationEventDef(
            InventoryEventCodes.LotExpired, ModuleKeys.WmsLotSerial, "Lotes vencidos con existencia", "Expired lots still in stock",
            new[] { Count("2"), Items("MED-1 · L-1 · 2026-09-30 · 12 · ALM-01") },
            IsOptional: true, LinkTemplate: "/warehouse/kardex",
            "{count} lote(s) vencido(s) con existencia", "{count} expired lot(s) still in stock",
            "Hay {count} lote(s) vencido(s) que todavía tienen existencia (producto · lote · vencimiento · cantidad · almacén):\n{items}",
            "{count} expired lot(s) still have stock (product · lot · expiry · quantity · warehouse):\n{items}"),
        new NotificationEventDef(
            InventoryEventCodes.StockLow, ModuleKeys.WmsLotSerial, "Productos bajo el mínimo", "Products below minimum",
            new[] { Count("4"), Items("MED-1 · disponible 3 · mínimo 10") },
            IsOptional: true, LinkTemplate: "/warehouse/products",
            "{count} producto(s) bajo el mínimo", "{count} product(s) below minimum",
            "Hay {count} producto(s) con menos disponible que su mínimo (producto · disponible · mínimo):\n{items}",
            "{count} product(s) have less available than their minimum (product · available · minimum):\n{items}"),
        new NotificationEventDef(
            InventoryEventCodes.StockOut, ModuleKeys.WmsLotSerial, "Productos sin existencia", "Products out of stock",
            new[] { Count("2"), Items("MED-1 · VIT-2") },
            IsOptional: true, LinkTemplate: "/warehouse/products",
            "{count} producto(s) sin existencia", "{count} product(s) out of stock",
            "Se quedaron sin existencia {count} producto(s):\n{items}",
            "{count} product(s) ran out of stock:\n{items}"),
        new NotificationEventDef(
            InventoryEventCodes.NoMovement, ModuleKeys.WmsLotSerial, "Productos sin movimiento", "Products with no movement",
            new[] { Count("5"), new NotificationVariable("days", "Días sin movimiento", "Days without movement", "90"), Items("MED-1 · 120 días · 40 en mano") },
            IsOptional: true, LinkTemplate: "/warehouse/products",
            "{count} producto(s) sin movimiento en {days} días", "{count} product(s) with no movement in {days} days",
            "Hay {count} producto(s) con existencia y sin movimientos hace {days} días o más (producto · días · en mano):\n{items}",
            "{count} product(s) have stock and no movement for {days} days or more (product · days · on hand):\n{items}"),
        new NotificationEventDef(
            InventoryEventCodes.NoCost, ModuleKeys.WmsLotSerial, "Productos con existencia sin costo", "Products in stock without a cost",
            new[] { Count("3"), Items("MED-1 · 40 en mano") },
            IsOptional: true, LinkTemplate: "/warehouse/products",
            "{count} producto(s) con existencia sin costo", "{count} product(s) in stock without a cost",
            "Hay {count} producto(s) con existencia y sin costo de compra; sin costo no se puede valorar el inventario (producto · en mano):\n{items}",
            "{count} product(s) have stock but no purchase cost, so inventory cannot be valued (product · on hand):\n{items}"),
    };
}
