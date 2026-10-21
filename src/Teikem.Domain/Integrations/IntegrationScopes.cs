using System.Text.RegularExpressions;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Integrations;

/// <summary>Familias de alcances: cada una se da con su propio permiso (<c>integrations.create.&lt;familia&gt;</c>).</summary>
public static class IntegrationFamilies
{
    public const string Transport = "TRANSPORT";
    public const string Warehouse = "WAREHOUSE";
    public const string Files = "FILES";

    public static string? PermissionOf(string family) => family switch
    {
        Transport => PermissionCatalog.IntegrationsCreateTransport,
        Warehouse => PermissionCatalog.IntegrationsCreateWarehouse,
        Files => PermissionCatalog.IntegrationsCreateFiles,
        _ => null,
    };
}

/// <summary>Un alcance que se le puede dar a una integración: de qué familia es, qué módulo de la compañía necesita y qué permisos le da a la cuenta de servicio.</summary>
public sealed record IntegrationScope(string Code, string Family, string? ModuleKey, string LabelEs, string LabelEn, IReadOnlyList<string> Permissions, bool NeedsClient = false);

/// <summary>
/// Lote 132 (P11) — catálogo de alcances. Quien crea una integración solo puede darle alcances cuyos permisos él mismo tiene, de una familia que él puede crear y de un
/// módulo que la compañía tiene encendido (sin escalada de privilegios). Los alcances de la API del cliente (lecturas, consignatarios, perfil, almacén…) se agregan aquí en el lote de la API.
/// </summary>
public static class IntegrationScopes
{
    /// <summary>Entrada de órdenes por archivo (carpeta, subida manual y, después, SFTP o correo): crea órdenes y los consignatarios que falten.</summary>
    public const string FilesOrdersImport = "files.orders_import";
    /// <summary>Lote 137b: salida de órdenes del cliente por archivo (SFTP/FTP) o por API saliente (HTTP).</summary>
    public const string FilesOrdersExport = "files.orders_export";
    /// <summary>2026-10-21: salida de un catálogo (productos…) por archivo (SFTP/FTP) o por API saliente (HTTP), con la plantilla de la entidad.</summary>
    public const string FilesCatalogExport = "files.catalog_export";

    public static readonly IReadOnlyList<IntegrationScope> All =
    [
        new(FilesOrdersImport, IntegrationFamilies.Files, ModuleKeys.LtlGround, "Entrada de envíos por archivo", "Shipment intake by file",
            [PermissionCatalog.OrdersView, PermissionCatalog.OrdersCreate, PermissionCatalog.LocationsRead, PermissionCatalog.LocationsCreate, PermissionCatalog.ClientsRead], NeedsClient: true),

        new(FilesOrdersExport, IntegrationFamilies.Files, ModuleKeys.LtlGround, "Salida de envíos por archivo o API", "Shipment output by file or API",
            [PermissionCatalog.OrdersView, PermissionCatalog.ClientsRead, PermissionCatalog.LocationsRead], NeedsClient: true),

        new(FilesCatalogExport, IntegrationFamilies.Files, ModuleKeys.WmsLotSerial, "Salida de productos por archivo o API", "Product output by file or API", [PermissionCatalog.InventoryView]),

        // ---- API del cliente (Lote 135): cada alcance abre un grupo de rutas (ApiAccessRules) y da a la cuenta de servicio solo los permisos que esas rutas piden
        new(ApiScopes.OrdersRead, IntegrationFamilies.Transport, ModuleKeys.LtlGround, "API: leer envíos, su seguimiento y el comprobante de entrega", "API: read shipments, tracking and proof of delivery",
            [PermissionCatalog.OrdersView, PermissionCatalog.ClientsRead, PermissionCatalog.LocationsRead], NeedsClient: true),
        new(ApiScopes.OrdersCreate, IntegrationFamilies.Transport, ModuleKeys.LtlGround, "API: crear envíos", "API: create shipments",
            [PermissionCatalog.OrdersView, PermissionCatalog.OrdersCreate, PermissionCatalog.ClientsRead, PermissionCatalog.LocationsRead, PermissionCatalog.LocationsCreate], NeedsClient: true),
        new(ApiScopes.InvoicesRead, IntegrationFamilies.Transport, ModuleKeys.Billing, "API: leer facturas", "API: read invoices", [PermissionCatalog.BillingView, PermissionCatalog.ClientsRead], NeedsClient: true),
        new(ApiScopes.ConsigneesRead, IntegrationFamilies.Transport, ModuleKeys.LtlGround, "API: leer consignatarios propios", "API: read own consignees", [PermissionCatalog.LocationsRead, PermissionCatalog.ClientsRead], NeedsClient: true),
        new(ApiScopes.ConsigneesWrite, IntegrationFamilies.Transport, ModuleKeys.LtlGround, "API: agregar y editar consignatarios propios (sin borrar)", "API: add and edit own consignees (no delete)",
            [PermissionCatalog.LocationsRead, PermissionCatalog.LocationsCreate, PermissionCatalog.LocationsUpdate, PermissionCatalog.ClientsRead], NeedsClient: true),
        new(ApiScopes.ClientRead, IntegrationFamilies.Transport, ModuleKeys.LtlGround, "API: leer el perfil del propio cliente", "API: read own client profile", [PermissionCatalog.ClientsRead], NeedsClient: true),
        new(ApiScopes.ClientWrite, IntegrationFamilies.Transport, ModuleKeys.LtlGround, "API: editar el perfil del propio cliente", "API: edit own client profile", [PermissionCatalog.ClientsRead, PermissionCatalog.ClientsUpdate], NeedsClient: true),
        new(ApiScopes.WarehouseRead, IntegrationFamilies.Warehouse, ModuleKeys.WmsLotSerial, "API: ver inventario, productos, recibos y despachos", "API: view inventory, products, receipts and dispatches", [PermissionCatalog.InventoryView]),
        new(ApiScopes.WarehouseWrite, IntegrationFamilies.Warehouse, ModuleKeys.WmsLotSerial, "API: crear productos, recibir, despachar, ajustar y transferir", "API: create products, receive, dispatch, adjust and transfer",
            [PermissionCatalog.InventoryView, PermissionCatalog.InventoryManage, PermissionCatalog.InventoryAdjust, PermissionCatalog.WarehouseReceive, PermissionCatalog.WarehouseIssue, PermissionCatalog.WarehouseTransfer, PermissionCatalog.WarehouseAdjust]),
        new(ApiScopes.SalesRead, IntegrationFamilies.Warehouse, ModuleKeys.SalesOrders, "API: leer órdenes de venta", "API: read sales orders", [PermissionCatalog.SalesView]),
        new(ApiScopes.SalesWrite, IntegrationFamilies.Warehouse, ModuleKeys.SalesOrders, "API: crear y solicitar órdenes de venta", "API: create and request sales orders", [PermissionCatalog.SalesView, PermissionCatalog.SalesCreate]),
        new(ApiScopes.PurchasingRead, IntegrationFamilies.Warehouse, ModuleKeys.Purchasing, "API: leer órdenes de compra", "API: read purchase orders", [PermissionCatalog.PurchasingView]),
        new(ApiScopes.PurchasingWrite, IntegrationFamilies.Warehouse, ModuleKeys.Purchasing, "API: crear y enviar órdenes de compra", "API: create and send purchase orders", [PermissionCatalog.PurchasingView, PermissionCatalog.PurchasingManage]),
        new(ApiScopes.WebhooksManage, IntegrationFamilies.Transport, ModuleKeys.LtlGround, "API: avisos por webhook de los envíos y facturas del cliente", "API: webhook notices for the client's shipments and invoices", [PermissionCatalog.ClientsRead], NeedsClient: true),
    ];

    /// <summary>¿Alguno de los alcances necesita un cliente? (los de transporte: la llave solo ve lo de ese cliente.)</summary>
    public static bool AnyNeedsClient(IEnumerable<string> scopeCodes) => scopeCodes.Select(Find).Any(s => s is { NeedsClient: true });

    /// <summary>Los alcances de la API (los que abren rutas), sin el de importar archivos.</summary>
    public static bool IsApiScope(string code) => ApiAccessRules.Routes.Any(r => string.Equals(r.Scope, code, StringComparison.OrdinalIgnoreCase));

    public static IntegrationScope? Find(string? code)
        => All.FirstOrDefault(s => string.Equals(s.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Errores (campo → mensaje) de los alcances pedidos: desconocido, de una familia que quien crea no puede dar, de un módulo que la compañía no tiene, o con permisos que quien crea no tiene.
    /// <paramref name="creatorPermissions"/> son los permisos efectivos de quien crea; <paramref name="enabledModules"/> los módulos encendidos de la compañía.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(IReadOnlyList<string> requested, ISet<string> creatorPermissions, ISet<string> enabledModules)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (requested.Count == 0) { errors["scopes"] = NoScopesMessage; return errors; }
        foreach (var code in requested)
        {
            var scope = Find(code);
            if (scope is null) { errors["scopes"] = UnknownScopeMessage(code); continue; }
            if (scope.ModuleKey is not null && !enabledModules.Contains(scope.ModuleKey)) { errors["scopes"] = ModuleOffMessage(scope.LabelEs); continue; }
            var familyPermission = IntegrationFamilies.PermissionOf(scope.Family);
            if (familyPermission is null || !creatorPermissions.Contains(familyPermission)) { errors["scopes"] = FamilyForbiddenMessage(scope.LabelEs, familyPermission ?? scope.Family); continue; }
            var missing = scope.Permissions.Where(p => !creatorPermissions.Contains(p)).ToList();
            if (missing.Count > 0) errors["scopes"] = MissingPermissionsMessage(scope.LabelEs, missing);
        }
        return errors;
    }

    /// <summary>Permisos que recibe la cuenta de servicio: la unión de los de los alcances.</summary>
    public static IReadOnlyList<string> PermissionsFor(IEnumerable<string> scopeCodes)
        => scopeCodes.Select(Find).Where(s => s is not null).SelectMany(s => s!.Permissions).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToList();

    public const string NoScopesMessage = "Escoja al menos un alcance para la integración.";
    public const string ApiNeedsClientMessage = "Los alcances de transporte de la API necesitan el cliente: la llave solo ve lo de ese cliente.";
    public const string OutboundScopeOnlyMessage = "Una integración de salida solo admite el alcance de salida de envíos (o el de salida de productos si manda un catálogo).";
    public const string InboundScopeOnlyMessage = "Una integración de entrada no admite el alcance de salida de envíos ni de productos.";
    public const string ApiScopesOnlyMessage = "Una integración de tipo API solo admite alcances de la API.";
    public const string FileScopeOnlyMessage = "Esta integración recibe archivos: solo admite el alcance de entrada de envíos por archivo.";
    public static string UnknownScopeMessage(string code) => $"El alcance '{code}' no existe.";
    public static string ModuleOffMessage(string label) => $"La compañía no tiene el módulo que necesita «{label}».";
    public static string FamilyForbiddenMessage(string label, string permission) => $"No puede dar el alcance «{label}»: le falta el permiso '{permission}'.";
    public static string MissingPermissionsMessage(string label, IEnumerable<string> missing) => $"No puede dar el alcance «{label}»: usted no tiene los permisos {string.Join(", ", missing)}.";
}

/// <summary>Reglas puras de una integración: nombre, carpeta, horario y nombres de la cuenta de servicio.</summary>
public static class IntegrationRules
{
    public const int NameMax = 120;
    public const int DescriptionMax = 400;
    public const int FolderMax = 60;
    public const int MaxRetentionDays = 365;
    public const int DefaultRetentionDays = 30;

    public const string NameRequiredMessage = "El nombre de la integración es obligatorio.";
    public static string NameMaxMessage => $"Máximo {NameMax} caracteres.";
    public const string ClientRequiredMessage = "Indique el cliente de los envíos de esta integración.";
    public const string TemplateRequiredMessage = "Escoja la plantilla de importación con la que se leen los archivos.";
    public const string ConnectorNotAvailableMessage = "Ese tipo de conector todavía no está disponible.";
    public const string FolderRequiredMessage = "Indique el nombre de la carpeta de entrada.";
    public const string FolderInvalidMessage = "El nombre de la carpeta solo admite letras, números, guion y guion bajo (sin rutas).";
    public static string IntervalMessage => $"El intervalo debe estar entre {IntegrationScheduleKinds.MinIntervalSeconds} y {IntegrationScheduleKinds.MaxIntervalSeconds} segundos.";
    public const string ManualNoScheduleMessage = "Una integración de subida manual no se programa por horario.";
    public static string RetentionMessage => $"Los días de retención deben estar entre 1 y {MaxRetentionDays}.";
    public const string NotifyUserMessage = "Uno de los usuarios a avisar no existe o está inactivo.";
    public const string NameTakenMessage = "Ya existe una integración con ese nombre.";
    public const string InactiveMessage = "La integración está apagada; enciéndala para correrla.";
    public const string RunningMessage = "La integración ya se está ejecutando; espere a que termine.";

    private static readonly Regex FolderPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    public static bool IsValidFolder(string? folder) => !string.IsNullOrWhiteSpace(folder) && folder.Trim().Length <= FolderMax && FolderPattern.IsMatch(folder.Trim());

    /// <summary>Correo interno de la cuenta de servicio (no es un buzón real: la cuenta no inicia sesión).</summary>
    public static string ServiceEmail(Guid publicId) => $"integration-{publicId:N}@service.teikem.local";

    public static string ServiceRoleName(string integrationName) => $"Integración: {integrationName.Trim()}";

    public static string ServiceUserName(string integrationName) => $"Integración {integrationName.Trim()}";

    /// <summary>Cuándo toca la próxima corrida de una integración por horario (null si es manual).</summary>
    public static DateTime? NextRun(string scheduleKind, int? intervalSeconds, DateTime fromUtc)
        => scheduleKind == IntegrationScheduleKinds.Interval && intervalSeconds is int s ? fromUtc.AddSeconds(s) : null;
}
