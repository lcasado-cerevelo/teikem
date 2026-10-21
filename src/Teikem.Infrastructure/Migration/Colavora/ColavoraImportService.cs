using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Migration.Colavora;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Migration.Colavora;

/// <summary>Fases del verbo import-colavora (--solo acepta estas claves).</summary>
public static class ColavoraPhases
{
    public const string Catalogs = "catalogos";
    public const string Users = "usuarios";
    public const string Zones = "zonas";
    public const string Clients = "clientes";
    public const string Consignees = "consignatarios";
    public const string Contracts = "contratos";
    public const string Orders = "ordenes";
    public static readonly string[] All = { Catalogs, Users, Zones, Clients, Consignees, Contracts, Orders };
}

/// <summary>
/// Migración de Colavora Courier → Teikem (2026-10-18, decisiones de Luis en docs/migracion/colavora/analisis.md §7).
/// Propia de esta migración: no comparte código con el importador de almacén de Depot (import-legacy). Lee las dos bases de
/// Colavora con <see cref="ColavoraReader"/> (solo lectura) y escribe en Teikem: los maestros por los servicios de Teikem
/// (compañía, clientes, choferes, zonas, contratos, campos) y las órdenes con su historial directo por EF en lotes (con la
/// auditoría apagada: 20 millones de filas no caben en la bitácora), conservando fechas y usuarios originales.
/// </summary>
public sealed partial class ColavoraImportService(
    TeikemDbContext db,
    TenantContext tc,
    ILookupCache lookups,
    ProvisioningService provisioning,
    UserAdminService userAdmin,
    DriverService drivers,
    DispatchZoneService dispatchZones,
    ZoneGroupService zoneGroups,
    ClientService clients,
    LocationService locations,
    ContactPointService contacts,
    ContractService contractService,
    CustomFieldService customFields,
    StoredFileService storedFiles,
    Files.IFileStore fileStore,
    IConfiguration config,
    ILogger<ColavoraImportService> logger)
{
    /// <summary>Contraseña temporal si el aprovisionamiento creó al administrador (el runner solo avisa; nunca la imprime ni la reporta).</summary>
    public string? TemporaryAdminPassword { get; private set; }

    /// <summary>Estado de la corrida: ids resueltos en Teikem por clave de Colavora.</summary>
    private sealed class RunState
    {
        public required ColavoraImportConfig Config { get; init; }
        public required ColavoraImportReport Report { get; init; }
        public required ColavoraReader Reader { get; init; }
        public required string CompanyId { get; init; }
        public bool DryRun { get; init; }
        public int? TenantId { get; set; }
        public int AdminUserId { get; set; }
        public DateTime MigrationUtc { get; init; }
        public HashSet<string> Phases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        // catálogos
        public Dictionary<string, int> OrderStatus { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> StopStatus { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> TripStatus { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> CodStatus { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> DriverStatus { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Colavora ServiceTypeID → LookupCodeId (ServiceType) y nombre.</summary>
        public Dictionary<int, (int LookupId, string Name, bool PickUp)> ServiceTypes { get; } = new();
        /// <summary>Colavora PackageTypeID → LookupCodeId (PackageType), nombre y si es la factura (tipo IN).</summary>
        public Dictionary<int, (int LookupId, string Name, bool IsInvoice)> PackageTypes { get; } = new();
        /// <summary>Código de CodType de Teikem → LookupCodeId.</summary>
        public Dictionary<string, int> CodTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Código de razón de Colavora (306, 03…) → LookupCodeId (DeliveryFailureReason) y descripción.</summary>
        public Dictionary<string, (int LookupId, string Description)> Reasons { get; } = new(StringComparer.Ordinal);
        /// <summary>Usuarios de Colavora que aparecen asignados en las órdenes (AssignDL/AssignPK): son los choferes. Se carga una vez.</summary>
        public HashSet<int>? AssignedUserIds { get; set; }
        /// <summary>Clientes «Recogido/Devolución» de Colavora → el cliente real al que se unen (código de cliente de Colavora).</summary>
        public IReadOnlyDictionary<string, ColavoraRules.SubClientLink> SubClients { get; set; } = new Dictionary<string, ColavoraRules.SubClientLink>();
        /// <summary>El tipo de servicio «Pick up» (PK) de la compañía, para las órdenes de los clientes «Recogido».</summary>
        public int PickupServiceTypeId { get; set; }
        /// <summary>Clientes que quedan como principal de una variante por servicio y cambian de nombre al nombre base (código de cliente de Colavora → nombre nuevo).</summary>
        public IReadOnlyDictionary<string, string> ClientRenames { get; set; } = new Dictionary<string, string>();
        public int DefaultServiceTypeId { get; set; }
        public int DefaultPackageTypeId { get; set; }
        /// <summary>Los globales con que arranca el estado (solo para saber si ya se cambió al tipo de la compañía).</summary>
        public int GlobalServiceTypeId { get; set; }
        public int GlobalPackageTypeId { get; set; }
        public int EntityTypeOrderId { get; set; }
        public int StopTypePickupId { get; set; }
        public int StopTypeDeliveryId { get; set; }
        public int RefTypeOtherId { get; set; }
        public int RefTypePoId { get; set; }
        public int LocationTypeDeliveryId { get; set; }
        public int LocationTypePickupId { get; set; }
        public int CountryPrId { get; set; }
        public int ContactPhoneId { get; set; }
        public int ContactEmailId { get; set; }
        public int EntityTypeLocationId { get; set; }

        // maestros
        /// <summary>Colavora UserID → Teikem user id.</summary>
        public Dictionary<int, int> Users { get; } = new();
        /// <summary>Código de chofer de Colavora (812, 2006, KENNETH) → DriverId.</summary>
        public Dictionary<string, int> Drivers { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Colavora UserID → código de chofer (para los AssignDL numéricos de la orden).</summary>
        public Dictionary<int, string> DriverCodeByUser { get; } = new();
        /// <summary>Código de zona de Teikem → DispatchZoneId.</summary>
        public Dictionary<string, int> Zones { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Lote 129: código del nivel (METRO/ISLA) → ZoneGroupId.</summary>
        public Dictionary<string, int> ZoneGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>ZIP → DispatchZoneId (zona de entrega de la orden, Lote 129).</summary>
        public Dictionary<string, int> ZoneIdByZip { get; } = new(StringComparer.Ordinal);
        /// <summary>PackageTypeID de Colavora que son descripción, no pieza (P11).</summary>
        public HashSet<int> DescriptionPackageTypes { get; } = new();
        /// <summary>Sender de Colavora (código del cliente) → cliente en Teikem.</summary>
        public Dictionary<string, ClientRef> Clients { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Colavora SenderID → Sender.</summary>
        public Dictionary<int, string> SenderCodeById { get; } = new();
        /// <summary>(ClientId, número de consignatario) → LocationId.</summary>
        public Dictionary<(int ClientId, string Number), int> ConsigneesByNumber { get; } = new();
        /// <summary>ZIP → ciudad (tabla Zip de Colavora) para completar direcciones sin ciudad.</summary>
        public Dictionary<string, string> CityByZip { get; } = new(StringComparer.Ordinal);
        public int? DivisionFieldId { get; set; }
        public int? ColavoraIdFieldId { get; set; }
        public HashSet<string> DivisionOptions { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Divisiones fuera de la lista de la compañía y sin uso reciente: sus órdenes se rechazan como error.</summary>
        public HashSet<string> StaleDivisions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool DivisionRulesLoaded { get; set; }
        /// <summary>Código de viaje (M-manifiesto) → TripId.</summary>
        public Dictionary<string, int> Trips { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Números de pieza ya usados en la compañía (índice único UX_OrderPiece).</summary>
        public HashSet<string> PieceNumbers { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ClientRef(int ClientId, Guid PublicId, string Code, string Name, int? PickupLocationId, int? ContractId);

    public async Task<ColavoraImportReport> RunAsync(ColavoraImportConfig cfg, bool dryRun, IReadOnlyCollection<string>? onlyPhases, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        var company = cfg.Company.Name!;
        var report = new ColavoraImportReport(company, dryRun);
        report.AddInfo("Configuración", cfg.ConfigPath ?? "(en memoria)");
        report.AddInfo("Compañía de Colavora", cfg.Company.ColavoraCompanyId!);

        var courierCs = config.GetConnectionString(cfg.Sources.CourierConnectionStringName);
        var loginCs = config.GetConnectionString(cfg.Sources.LoginConnectionStringName);
        if (string.IsNullOrWhiteSpace(courierCs)) throw new ValidationException($"Falta la cadena de conexión ConnectionStrings:{cfg.Sources.CourierConnectionStringName} (corra scripts\\crear-variable-colavora.ps1).");
        if (string.IsNullOrWhiteSpace(loginCs)) throw new ValidationException($"Falta la cadena de conexión ConnectionStrings:{cfg.Sources.LoginConnectionStringName} (corra scripts\\crear-variable-colavora.ps1).");

        var phases = new HashSet<string>(onlyPhases is { Count: > 0 } ? onlyPhases : ColavoraPhases.All, StringComparer.OrdinalIgnoreCase);
        report.AddInfo("Fases", string.Join(", ", ColavoraPhases.All.Where(phases.Contains)));
        var s = new RunState
        {
            Config = cfg, Report = report, Reader = new ColavoraReader(courierCs, loginCs), CompanyId = cfg.Company.ColavoraCompanyId!,
            DryRun = dryRun, MigrationUtc = cfg.Options.MigrationDateUtc ?? DateTime.UtcNow, Phases = phases,
        };
        report.AddInfo("Fecha de la migración (corte de órdenes abiertas)", cfg.Options.OpenOrderDays is int od ? $"{s.MigrationUtc:yyyy-MM-dd HH:mm} UTC, órdenes abiertas (cualquier estatus) de más de {od} días → Entregada" : $"{s.MigrationUtc:yyyy-MM-dd HH:mm} UTC, Pending de más de {cfg.Options.PendingCloseDays} días → Entregada");
        foreach (var t in cfg.Options.DescriptionPackageTypes) s.DescriptionPackageTypes.Add(t);
        report.AddInfo("Tipos de paquete que son descripción (no pieza)", s.DescriptionPackageTypes.Count == 0 ? "ninguno" : string.Join(", ", s.DescriptionPackageTypes.OrderBy(x => x)));

        // (a) Compañía: siempre (las demás fases la necesitan).
        if (!await ResolveCompanyAsync(s, ct)) return report;

        tc.IsPlatformAdmin = true;
        tc.IsAuthenticated = true;
        tc.Lang = cfg.Company.Lang;
        using var asTenant = s.TenantId is int tid ? tc.As(tid, s.AdminUserId) : null;

        await LoadTeikemCatalogsAsync(s, ct);
        await ImportCatalogsAsync(s, ct);           // tipos de servicio, paquete, COD y razones (siempre: las órdenes los necesitan)
        if (phases.Contains(ColavoraPhases.Users)) await ImportUsersAndDriversAsync(s, ct); else await LoadExistingUsersAndDriversAsync(s, ct);
        if (phases.Contains(ColavoraPhases.Zones)) await ImportZonesAsync(s, ct); else await LoadExistingZonesAsync(s, ct);
        if (phases.Contains(ColavoraPhases.Clients)) await ImportClientsAsync(s, ct); else await LoadExistingClientsAsync(s, ct);
        if (phases.Contains(ColavoraPhases.Consignees)) await ImportConsigneesAsync(s, ct); else await LoadExistingConsigneesAsync(s, ct);
        if (phases.Contains(ColavoraPhases.Contracts)) await ImportContractsAsync(s, ct); else await LoadExistingContractsAsync(s, ct);
        await ImportCustomFieldsAsync(s, ct);
        if (phases.Contains(ColavoraPhases.Orders)) await ImportOrdersAsync(s, ct);
        return report;
    }

    // ================================================================ compañía (D1: tenant nuevo por compañía de Colavora)

    private async Task<bool> ResolveCompanyAsync(RunState s, CancellationToken ct)
    {
        const string E = ColavoraEntities.Company;
        var cfg = s.Config;
        var report = s.Report;
        var name = cfg.Company.Name!;
        report.CountRead(E);

        var source = await s.Reader.ReadCompanyAsync(s.CompanyId, ct);
        if (source is null)
        {
            report.Reject(E, s.CompanyId, $"La compañía {s.CompanyId} no existe en colavora_courier_login.Company.", severe: true);
            return false;
        }
        report.AddInfo("Compañía en Colavora", $"{source.Name} ({source.City}, {source.State})");

        Tenant? existing;
        using (tc.BypassTenantFilter())
            existing = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == name, ct);

        if (existing is not null)
        {
            report.CountExisting(E);
            report.AddInfo("Compañía en Teikem", $"{existing.Name} (id {existing.TenantId}, ya existía)");
            s.TenantId = existing.TenantId;
            if (string.IsNullOrWhiteSpace(cfg.Company.AdminEmail)) { report.Reject(E, name, "company.adminEmail es obligatorio.", severe: true); return false; }
            if (s.DryRun)
            {
                using (tc.BypassTenantFilter())
                    s.AdminUserId = await db.UserTenants.IgnoreQueryFilters().Where(m => m.TenantId == existing.TenantId).OrderBy(m => m.UserTenantId).Select(m => m.UserId).FirstOrDefaultAsync(ct);
                return true;
            }
            using (tc.BypassTenantFilter())
            using (tc.As(existing.TenantId))
            {
                var (adminId, temp) = await provisioning.EnsureAdminUserAsync(existing, cfg.Company.AdminEmail.Trim(), cfg.Company.AdminFullName ?? cfg.Company.AdminEmail.Trim(),
                    string.IsNullOrWhiteSpace(cfg.Company.AdminPassword) ? null : cfg.Company.AdminPassword, ct);
                TemporaryAdminPassword = temp;
                s.AdminUserId = adminId;
            }
            return true;
        }

        report.CountCreated(E);
        if (s.DryRun)
        {
            report.AddInfo("Compañía en Teikem", $"{name} (nueva: se aprovisionaría)");
            return true;
        }
        if (string.IsNullOrWhiteSpace(cfg.Company.AdminEmail)) { report.Reject(E, name, "company.adminEmail es obligatorio para aprovisionar la compañía.", severe: true); return false; }
        try
        {
            var modules = new List<string>(cfg.Company.Modules);
            var result = await provisioning.ProvisionAsync(new TenantProvisionRequest(name, cfg.Company.LegalName ?? source.Name, cfg.Company.TaxId, cfg.Company.Lang,
                modules, cfg.Company.AdminEmail.Trim(), cfg.Company.AdminFullName ?? cfg.Company.AdminEmail.Trim(),
                string.IsNullOrWhiteSpace(cfg.Company.AdminPassword) ? null : cfg.Company.AdminPassword), ct);
            TemporaryAdminPassword = result.TemporaryPassword;
            s.TenantId = result.Tenant.Id;
            s.AdminUserId = result.AdminUserId;
            report.AddInfo("Compañía en Teikem", $"{name} (id {result.Tenant.Id}, aprovisionada)");
            return true;
        }
        catch (TeikemException ex)
        {
            report.Reject(E, name, Describe(ex), severe: true);
            return false;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    // ================================================================ catálogos de Teikem que las fases necesitan

    private async Task LoadTeikemCatalogsAsync(RunState s, CancellationToken ct)
    {
        async Task Fill(string domain, Dictionary<string, int> into)
        {
            foreach (var sc in await db.StatusCodes.AsNoTracking().Where(x => x.Entity == domain).Select(x => new { x.InternalCode, x.StatusCodeId }).ToListAsync(ct))
                into[sc.InternalCode] = sc.StatusCodeId;
        }
        await Fill(StatusDomains.OrderStatus, s.OrderStatus);
        await Fill(StatusDomains.StopStatus, s.StopStatus);
        await Fill(StatusDomains.TripStatus, s.TripStatus);
        await Fill(StatusDomains.CodStatus, s.CodStatus);
        await Fill(StatusDomains.DriverStatus, s.DriverStatus);
        foreach (var code in new[] { OrderStatuses.Submitted, OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.Assigned, OrderStatuses.InTransit, OrderStatuses.Delivered, OrderStatuses.Failed, OrderStatuses.Cancelled, OrderStatuses.Returned })
            if (!s.OrderStatus.ContainsKey(code)) throw new ValidationException($"El estatus de orden {code} no existe en Teikem; ejecute db-update antes de la migración.");

        s.EntityTypeOrderId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
        s.EntityTypeLocationId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.Location, ct);
        s.StopTypePickupId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Pickup, ct);
        s.StopTypeDeliveryId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        s.RefTypeOtherId = await lookups.GetIdAsync(LookupDomains.OrderRefType, "OTHER", ct);
        s.RefTypePoId = await lookups.GetIdAsync(LookupDomains.OrderRefType, "CLIENT_PO", ct);
        s.LocationTypeDeliveryId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Delivery, ct);
        s.LocationTypePickupId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Pickup, ct);
        s.CountryPrId = await lookups.GetIdAsync(LookupDomains.Country, "PR", ct);
        s.ContactPhoneId = await lookups.GetIdAsync(LookupDomains.ContactType, "PHONE", ct);
        s.ContactEmailId = await lookups.GetIdAsync(LookupDomains.ContactType, "EMAIL", ct);
        // arranque: los globales; el catálogo de la compañía los reemplaza por su «Next Day» y su «Box» (tipos de cada compañía, decisión de Luis 2026-10-20)
        s.DefaultServiceTypeId = s.GlobalServiceTypeId = await lookups.GetIdAsync(LookupDomains.ServiceType, "STANDARD", ct);
        s.DefaultPackageTypeId = s.GlobalPackageTypeId = await lookups.GetIdAsync(LookupDomains.PackageType, "BOX", ct);
    }

    // ================================================================ utilidades

    /// <summary>Ejecuta una escritura por servicio; una excepción de dominio se anota como rechazo de la fila y se sigue.</summary>
    private async Task<T?> TryAsync<T>(RunState s, string entity, string key, Func<Task<T>> action) where T : class
    {
        try { return await action(); }
        catch (TeikemException ex)
        {
            s.Report.Reject(entity, key, Describe(ex));
            db.ChangeTracker.Clear();
            return null;
        }
        catch (DbUpdateException ex)
        {
            s.Report.Reject(entity, key, ex.GetBaseException().Message);
            db.ChangeTracker.Clear();
            return null;
        }
    }

    private static string Describe(TeikemException ex) => ex is ValidationException { Errors.Count: > 0 } v
        ? string.Join(" ", v.Errors.SelectMany(e => e.Value ?? Array.Empty<string>()))
        : ex.Message;

    /// <summary>Usuario de Teikem para un usuario de Colavora (null si no se migró: System, OnTime…).</summary>
    private static int? UserOf(RunState s, int? colavoraUserId)
        => colavoraUserId is int id && s.Users.TryGetValue(id, out var u) ? u : null;

    private int TenantIdOrThrow(RunState s) => s.TenantId ?? throw new InvalidOperationException("Sin tenant.");
}
