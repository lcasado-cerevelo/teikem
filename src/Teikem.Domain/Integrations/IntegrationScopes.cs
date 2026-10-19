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
public sealed record IntegrationScope(string Code, string Family, string? ModuleKey, string LabelEs, string LabelEn, IReadOnlyList<string> Permissions);

/// <summary>
/// Lote 132 (P11) — catálogo de alcances. Quien crea una integración solo puede darle alcances cuyos permisos él mismo tiene, de una familia que él puede crear y de un
/// módulo que la compañía tiene encendido (sin escalada de privilegios). Los alcances de la API del cliente (lecturas, consignatarios, perfil, almacén…) se agregan aquí en el lote de la API.
/// </summary>
public static class IntegrationScopes
{
    /// <summary>Entrada de órdenes por archivo (carpeta, subida manual y, después, SFTP o correo): crea órdenes y los consignatarios que falten.</summary>
    public const string FilesOrdersImport = "files.orders_import";

    public static readonly IReadOnlyList<IntegrationScope> All =
    [
        new(FilesOrdersImport, IntegrationFamilies.Files, ModuleKeys.LtlGround, "Entrada de órdenes por archivo", "Order intake by file",
            [PermissionCatalog.OrdersView, PermissionCatalog.OrdersCreate, PermissionCatalog.LocationsRead, PermissionCatalog.LocationsCreate, PermissionCatalog.ClientsRead]),
    ];

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
    public const string ClientRequiredMessage = "Indique el cliente de las órdenes de esta integración.";
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
