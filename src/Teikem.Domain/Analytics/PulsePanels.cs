using Teikem.Domain.Constants;

namespace Teikem.Domain.Analytics;

/// <summary>
/// Un panel del Pulso del día: su clave, el permiso <c>pulse.*</c> que lo abre, los permisos de lectura de los datos que
/// muestra (vacío = se decide por elemento, como en Indicadores y Gráficos), el módulo del tenant que debe estar encendido
/// (null = sin requisito) y su orden por defecto.
/// </summary>
public sealed record PulsePanelDef(string Key, string Permission, string[] DataPermissions, string? Module, int DefaultSortOrder);

/// <summary>
/// Lote F8a (P1) — registro de paneles del Pulso del día (loteF8-plan.md §2.3). Un panel entra en el Pulso de un usuario solo si
/// tiene su permiso, todos sus permisos de datos y su módulo está encendido. Reservados, no declarados hasta su lote:
/// ORDERS_RIVER (10, F3), COD_RIVER (11, 7C), DECISIONS (15, F3/F5) y RADAR (60, F3).
/// Lote 14 (D6): ATTENTION ("Necesita tu atención") con orden 5, el más bajo de los paneles: queda justo debajo del
/// encabezado y de la franja que el Lote 15 pondrá con orden negativo (por eso no es negativo) y antes de ORDERS_RIVER (10).
/// No lleva permiso de datos ni módulo: cada fila decide el suyo (IAttentionItemProvider); se usa esta clave y no la
/// reservada DECISIONS porque el dueño la llama "Necesita tu atención" (Operación y COD podrán sumar filas con sus proveedores).
/// Lote 15 (D1, D6): WAREHOUSE_DAY (franja "Almacén hoy") con orden −10, el primero: justo debajo de la fecha y antes de
/// ATTENTION, también en los Pulsos ya organizados (Organizar guarda índice × 10 desde 0; un panel sin fila propia usa su orden
/// por defecto, así que solo un orden negativo lo deja arriba). Reusa pulse.warehouse, inventory.view y WMS_LOTSERIAL (los
/// mismos del panel Almacén): no hay permiso nuevo. Orden final por defecto: WAREHOUSE_DAY, ATTENTION, INDICATORS, CHARTS,
/// WAREHOUSE, ACTIVITY.
/// Receta para un panel nuevo: una constante aquí, una fila en PermissionCatalog.All (+ plantillas), una línea en el seed y
/// una entrada en el registro del frontend.
/// </summary>
public static class PulsePanels
{
    public const string Indicators = "INDICATORS";
    public const string Charts = "CHARTS";
    public const string Warehouse = "WAREHOUSE";
    public const string Activity = "ACTIVITY";
    public const string Attention = "ATTENTION";
    public const string WarehouseDay = "WAREHOUSE_DAY";

    public static readonly IReadOnlyList<PulsePanelDef> All = new List<PulsePanelDef>
    {
        new(Indicators, PermissionCatalog.PulseIndicators, Array.Empty<string>(), ModuleKeys.Analytics, 20),
        new(Charts, PermissionCatalog.PulseCharts, Array.Empty<string>(), ModuleKeys.Analytics, 30),
        new(Warehouse, PermissionCatalog.PulseWarehouse, new[] { PermissionCatalog.InventoryView }, ModuleKeys.WmsLotSerial, 40),
        new(Activity, PermissionCatalog.PulseActivity, new[] { PermissionCatalog.AnalyticsView }, ModuleKeys.Analytics, 50),
        new(Attention, PermissionCatalog.PulseAttention, Array.Empty<string>(), null, 5),
        new(WarehouseDay, PermissionCatalog.PulseWarehouse, new[] { PermissionCatalog.InventoryView }, ModuleKeys.WmsLotSerial, -10),
    };

    /// <summary>Panel por clave (sin distinguir mayúsculas); null si no está en el registro.</summary>
    public static PulsePanelDef? Find(string? key)
        => string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(p => string.Equals(p.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>¿El usuario ve el panel? Permiso del panel + todos sus permisos de datos + módulo encendido.</summary>
    public static bool CanSee(PulsePanelDef panel, Func<string, bool> hasPermission, Func<string, bool> isModuleEnabled)
        => hasPermission(panel.Permission)
           && panel.DataPermissions.All(hasPermission)
           && (panel.Module is null || isModuleEnabled(panel.Module));

    /// <summary>
    /// Módulos del tenant que habilitan un módulo de negocio (<c>BusinessModule</c> de indicadores y gráficos), basta uno:
    /// WAREHOUSE → WMS_LOTSERIAL; OPERATIONS → LTL_GROUND; ACCOUNTING → COD o LTL_GROUND. Vacío = sin requisito (desconocido).
    /// </summary>
    public static IReadOnlyList<string> TenantModulesFor(string? businessModule)
        => (businessModule ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            BusinessModules.Warehouse => new[] { ModuleKeys.WmsLotSerial },
            BusinessModules.Operations => new[] { ModuleKeys.LtlGround },
            BusinessModules.Accounting => new[] { ModuleKeys.Cod, ModuleKeys.LtlGround },
            _ => Array.Empty<string>(),
        };

    /// <summary>¿Está encendido el módulo de negocio para el tenant? (regla 4 de §2.2).</summary>
    public static bool IsBusinessModuleEnabled(string? businessModule, Func<string, bool> isModuleEnabled)
    {
        var required = TenantModulesFor(businessModule);
        return required.Count == 0 || required.Any(isModuleEnabled);
    }

    /// <summary>
    /// Resolución de un panel: fila del usuario → fila de la compañía → registro. Devuelve visibilidad, orden y origen
    /// (<see cref="PulseSources"/>).
    /// </summary>
    public static (bool IsVisible, int SortOrder, string Source) ResolvePanel(PulsePanelDef panel, PulsePanelSetting? mine, PulsePanelSetting? company)
    {
        if (mine is not null) return (mine.IsVisible, mine.SortOrder, PulseSources.User);
        if (company is not null) return (company.IsVisible, company.SortOrder, PulseSources.Company);
        return (true, panel.DefaultSortOrder, PulseSources.Default);
    }

    /// <summary>
    /// Resolución de un indicador o gráfico dentro de su panel: la preferencia del usuario (PulseSortOrder / ShowInPulse, campo
    /// por campo) pisa la definición, que es el nivel compañía (la escribe <c>PUT …/layout?scope=company</c>). Origen "user" si
    /// el usuario tiene alguno de los dos campos propios; si no, "company".
    /// </summary>
    public static (bool IsVisible, int SortOrder, string Source) ResolveItem(int? userSortOrder, bool? userShowInPulse, int definitionSortOrder, bool definitionShowInPulse)
    {
        var personal = userSortOrder.HasValue || userShowInPulse.HasValue;
        return (userShowInPulse ?? definitionShowInPulse, userSortOrder ?? definitionSortOrder, personal ? PulseSources.User : PulseSources.Company);
    }
}

/// <summary>Origen del orden/visibilidad efectivos de un panel o elemento del Pulso.</summary>
public static class PulseSources
{
    public const string User = "user";
    public const string Company = "company";
    public const string Default = "default";
}
