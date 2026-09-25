using Teikem.Infrastructure.Clients;

namespace Teikem.Infrastructure.Orders;

/// <summary>
/// Consolidación pura de las líneas de una orden para cotizar (Lote 3, P4). Una orden admite varias CargoLine del mismo
/// tipo de paquete (DECISIÓN 14), pero el motor de tarifas exige una sola línea por (servicio, tipo de paquete):
/// ContractRateResolver.FindDuplicateLine rechaza pares repetidos porque dos líneas del mismo par cobrarían dos tarifas
/// base en vez de base + pieza extra (documento L1096: "una línea por grupo de tipo de paquete con el total de piezas").
/// Aquí se suman las piezas por tipo (sin distinguir mayúsculas) conservando el orden de aparición.
/// </summary>
public static class OrderQuoteLines
{
    /// <summary>
    /// Agrupa por tipo de paquete (case-insensitive, recortado), suma piezas y respeta el orden de aparición.
    /// Lista vacía cuando no hay líneas (entrega especial: se cotiza por el servicio especial, no por líneas).
    /// </summary>
    public static IReadOnlyList<QuoteLine> Build(string serviceType, IEnumerable<(string PackageType, int Pieces)> lines)
    {
        var svc = (serviceType ?? string.Empty).Trim();
        var order = new List<string>();
        var pieces = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (packageType, count) in lines)
        {
            var pkg = (packageType ?? string.Empty).Trim();
            if (pkg.Length == 0) continue;
            if (!pieces.ContainsKey(pkg))
            {
                order.Add(pkg);
                spelling[pkg] = pkg;
                pieces[pkg] = 0;
            }
            pieces[pkg] += count;
        }

        return order.Select(k => new QuoteLine(svc, spelling[k], pieces[k])).ToList();
    }
}
