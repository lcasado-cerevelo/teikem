using System.Globalization;
using Teikem.Domain.Wms;

namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 235 — lo DISPONIBLE al escoger productos en una venta (pedido guiado, «Nueva orden» y app de vendedores). Reglas puras:
/// - Disponible de un producto en un almacén = lo «disponible para vender» que usa Solicitar (en mano − reservado, en posiciones activas de zonas activas que no
///   son de cuarentena, cruce de muelle ni renta).
/// - Kit que se arma al pedir (tipo KIT: el kit de oferta) = cuántos kits COMPLETOS se pueden armar con lo disponible de sus piezas en ese almacén.
/// - Kit armado (ASSEMBLED_KIT) = su propia existencia disponible, como cualquier producto.
/// - SÍ se puede pedir más de lo disponible en el almacén de la orden (el vendedor puede saber que viene una compra de camino): la pantalla NO lo bloquea, lo
///   MARCA («Backorder: N» y «Solo hay X disponibles en {almacén}; N quedan en backorder.»). Al solicitar, el servidor sigue como siempre (409 de faltante salvo
///   allowBackorder: reserva lo disponible y deja el resto en backorder).
/// </summary>
public static class SalesStockRules
{
    /// <summary>Kits completos que se pueden armar en un almacén con lo disponible de cada pieza (pieza sin disponible = 0; kit sin piezas = 0).</summary>
    public static decimal KitsAvailable(IEnumerable<(int ComponentProductId, decimal QtyPerKit)> components, IReadOnlyDictionary<int, decimal> sellableByProduct)
        => KitRules.CanBuild(components.Select(c => (sellableByProduct.GetValueOrDefault(c.ComponentProductId), c.QtyPerKit)));

    /// <summary>Lo que quedaría en backorder: lo pedido que pasa de lo disponible (0 si alcanza).</summary>
    public static decimal Backorder(decimal quantity, decimal available) => Math.Max(0m, quantity - Math.Max(0m, available));

    /// <summary>El aviso del renglón: «Solo hay X disponibles en {almacén}; N quedan en backorder.» (X = 0: «No hay disponible en {almacén}; N quedan en backorder.»).</summary>
    public static string BackorderMessage(decimal available, decimal backorder, string warehouse)
    {
        var x = Math.Max(0m, available);
        var head = x == 0m ? $"No hay disponible en {warehouse}" : x == 1m ? $"Solo hay 1 disponible en {warehouse}" : $"Solo hay {Num(x)} disponibles en {warehouse}";
        return backorder == 1m ? $"{head}; 1 queda en backorder." : $"{head}; {Num(backorder)} quedan en backorder.";
    }

    private static string Num(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
