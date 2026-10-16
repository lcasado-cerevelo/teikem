using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>Constantes de los kits (Lote 68). Viven aquí (y no en CatalogDomains) para que el lote se pueda pasar solo a otra rama.</summary>
public static class KitCodes
{
    /// <summary>Dominio de catálogo (LookupCode) con el tipo de producto. Sin tipo (NULL) = producto normal.</summary>
    public const string ProductTypeDomain = "ProductType";
    public const int MaxComponents = 50;
}

/// <summary>Tipo de producto: normal o kit (un producto que se arma al pedir con otros productos, sin existencia propia).</summary>
public static class ProductTypes
{
    public const string Standard = "STANDARD";
    public const string Kit = "KIT";
    public static readonly string[] All = { Standard, Kit };
}

/// <summary>
/// Lote 68 — componente de un kit: el kit se compone de <see cref="Quantity"/> unidades del producto componente. El kit NO tiene existencia ni movimientos
/// propios: todo pasa en sus componentes. Un componente nunca es otro kit (sin kits dentro de kits) y es del mismo dueño de inventario que el kit. Se audita
/// con el EntityType del producto (como los lotes).
/// </summary>
[AuditEntity(Constants.EntityTypes.Product)]
public class ProductKitComponent : ITenantScoped
{
    public int ProductKitComponentId { get; set; }
    public int TenantId { get; set; }
    public int KitProductId { get; set; }
    public int ComponentProductId { get; set; }
    public decimal Quantity { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Lote 68 — reglas puras de los kits: mensajes y disponibilidad («se pueden armar N»). Sin BD.</summary>
public static class KitRules
{
    public static string NotAKit(string sku) => $"El producto {sku} no es un kit.";
    public static string KitHasNoStock(string sku) => $"{sku} es un kit: no tiene existencia propia. Los movimientos se hacen sobre sus componentes.";
    public const string NeedComponents = "Un kit necesita al menos un componente.";
    public const string ComponentQuantity = "La cantidad de cada componente debe ser mayor que cero.";
    public const string TrackingNotAllowed = "Un kit no se rastrea por lote ni serie.";
    public static string TooManyComponents() => $"Un kit lleva como máximo {KitCodes.MaxComponents} componentes.";
    public static string DuplicateComponent(string sku) => $"El componente {sku} está repetido.";
    public static string SelfComponent(string sku) => $"El kit {sku} no puede ser componente de sí mismo.";
    public static string ComponentIsKit(string sku) => $"{sku} es un kit: un kit no puede llevar otro kit como componente.";
    public static string ComponentInactive(string sku) => $"El componente {sku} está inactivo.";
    public static string ComponentOtherOwner(string sku) => $"El componente {sku} es de otro dueño de inventario que el kit.";
    public static string HasStock(string sku) => $"{sku} tiene existencia: no se puede convertir en kit mientras tenga existencia o reservas.";
    public static string IsComponent(string sku) => $"{sku} es componente de otro kit: no puede ser un kit.";
    public static string UsedInSales(string sku) => $"El kit {sku} ya se usó en órdenes de venta: no se puede volver un producto normal.";
    public const string ComponentNotFound = "Componente";
    public const string CollectNoBinChoice = "Un kit se recolecta por sus componentes: no se escoge posición, lote ni series.";

    /// <summary>
    /// Cuántos kits completos se pueden armar con lo disponible: el menor cociente disponible ÷ cantidad por kit entre los componentes, hacia abajo (un kit es
    /// una pieza entera). Sin componentes = 0. Un componente sin disponible = 0.
    /// </summary>
    public static int CanBuild(IEnumerable<(decimal Available, decimal QtyPerKit)> components)
    {
        var list = components.ToList();
        if (list.Count == 0 || list.Any(c => c.QtyPerKit <= 0m)) return 0;
        var min = list.Min(c => Math.Max(0m, c.Available) / c.QtyPerKit);
        return (int)Math.Floor(min);
    }
}
