namespace Teikem.Domain.Orders;

/// <summary>
/// Lote 97 (P10) — número de conduce (waybill number): el identificador ÚNICO de cada orden de entrega en toda la compañía, que usan el cliente (sender) y el consignatario para referirse a ella.
/// Lo genera Teikem al crear la orden (contador WAYBILL por compañía, patrón CND-#######); no se teclea ni cambia. El número de orden (puede repetirse entre clientes) y el de empaque
/// (EMP-#####, identificador de escaneo) siguen existiendo.
/// </summary>
public static class WaybillCodes
{
    public const string NumberKind = "WAYBILL";
    public const string NumberPattern = "CND-#######";
    public const string UniqueIndex = "UX_Order_Waybill";
}
