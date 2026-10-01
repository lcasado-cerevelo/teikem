using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Seeding;

/// <summary>
/// Tenant demo (Advance Logistics) para desarrollo: módulos encendidos según el módulo 0B del documento maestro
/// (Última milla, COD, WMS, Equipos en alquiler solo tracking, Portal, Campos personalizados, Compras + Catálogo/Análisis/Sistema;
/// apagados Cross-dock, Marítimo, Facturación de alquiler), admin de compañía, un despachador y un admin de plataforma.
/// Lote 6 (D49): si el tenant tiene WMS_LOTSERIAL, siembra el almacén ALM-01 con zonas STG (STAGING), PCK, RSV y QUA, sus
/// posiciones y los muelles D1/D2, sin productos ni inventario (la conciliación queda en cero). Así el tenant demo recibe desde
/// el primer arranque. Se activa con Seed:Demo:Enabled=true. Idempotente (por código).
/// </summary>
public sealed class DemoTenantSeeder(TeikemDbContext db, ITenantContext tenant, IConfiguration config, ProvisioningService provisioning, UserAdminService userAdmin,
    SystemAnalyticsSeeder analyticsSeeder, StatusService statuses, ILookupCache lookups, ILogger<DemoTenantSeeder> logger)
{
    public const string DemoWarehouseCode = "ALM-01";
    public const string DemoWarehouseName = "Almacén principal";

    /// <summary>Zonas del almacén demo: código, nombre, tipo y posiciones (código, pasillo, rack, nivel, posición).</summary>
    private static readonly (string Code, string Name, string ZoneType, (string Code, string? Aisle, string? Rack, string? Level, string? Position)[] Bins)[] DemoZones =
    {
        ("STG", "Recepción", ZoneTypes.Staging, new (string, string?, string?, string?, string?)[] { ("STG-01", null, null, null, null) }),
        ("PCK", "Picking", ZoneTypes.Picking, Enumerable.Range(1, 4).Select(i => ($"A01-R01-N1-P0{i}", (string?)"A01", (string?)"R01", (string?)"N1", (string?)$"P0{i}")).ToArray()),
        ("RSV", "Reserva", ZoneTypes.Reserve, Enumerable.Range(1, 4).Select(i => ($"B01-R01-N1-P0{i}", (string?)"B01", (string?)"R01", (string?)"N1", (string?)$"P0{i}")).ToArray()),
        ("QUA", "Cuarentena", ZoneTypes.Quarantine, new (string, string?, string?, string?, string?)[] { ("Q-01", null, null, null, null) }),
    };

    private static readonly (string Code, string DockType)[] DemoDocks = { ("D1", DockTypes.Inbound), ("D2", DockTypes.Outbound) };

    public static readonly string[] AdvanceModules =
    {
        ModuleKeys.LtlGround, ModuleKeys.Cod, ModuleKeys.WmsLotSerial, ModuleKeys.RentalEquipment, ModuleKeys.ClientPortal,
        ModuleKeys.CustomFields, ModuleKeys.Purchasing, ModuleKeys.Catalog, ModuleKeys.Analytics, ModuleKeys.System,
    };

    public async Task SeedAsync(CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        var name = config["Seed:Demo:TenantName"] ?? "Advance Logistics";
        var adminEmail = config["Seed:Demo:AdminEmail"] ?? "teikem+admin@cerevelo.com";
        var adminPassword = config["Seed:Demo:AdminPassword"] ?? "Teikem_Admin_2026!";
        var platformEmail = config["Seed:Demo:PlatformAdminEmail"] ?? "teikem+support@cerevelo.com";
        var platformPassword = config["Seed:Demo:PlatformAdminPassword"] ?? adminPassword;

        int tenantId;
        using (tc.BypassTenantFilter())
        {
            var existing = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == name, ct);
            if (existing is null)
            {
                var result = await provisioning.ProvisionAsync(new TenantProvisionRequest(name, "Advance Logistics, LLC", null, "es", AdvanceModules, adminEmail, "Administrador Advance", adminPassword), ct);
                tenantId = result.Tenant.Id;
                // Tenant.MfaRequired es true por default (Lote F8a): el tenant demo lo apaga, si no las pruebas
                // automatizadas y el desarrollo local quedarían bloqueados por el segundo factor en cada login.
                var created = await db.Tenants.FirstAsync(t => t.TenantId == tenantId, ct);
                created.MfaRequired = false;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Tenant demo '{Name}' aprovisionado (id {Id}).", name, tenantId);
            }
            else
            {
                tenantId = existing.TenantId;
                using var _ = tc.As(tenantId);
                await provisioning.EnsureAdminUserAsync(existing, adminEmail, "Administrador Advance", adminPassword, ct);
                if (existing.MfaRequired)
                {
                    var tracked = await db.Tenants.FirstAsync(t => t.TenantId == tenantId, ct);
                    tracked.MfaRequired = false;
                    await db.SaveChangesAsync(ct);
                }
                // Una BD del Lote 1 recibe el contenido de sistema nuevo (vistas/indicadores/gráficos de lotes posteriores); idempotente por nombre.
                await analyticsSeeder.SeedForTenantAsync(tenantId, ct);
                logger.LogInformation("Tenant demo '{Name}' ya existe (id {Id}); se verifica admin y contenido de análisis de sistema.", name, tenantId);
            }
        }

        using (tc.As(tenantId))
        {
            var admin = await db.Users.AsNoTracking().FirstAsync(u => u.NormalizedEmail == adminEmail.ToUpperInvariant(), ct);
            tc.UserId = admin.Id;
            const string despachoEmail = "teikem+dispatch@cerevelo.com";
            if (!await db.Users.AnyAsync(u => u.NormalizedEmail == despachoEmail.ToUpperInvariant(), ct))
            {
                await userAdmin.CreateUserAsync(new UserCreateRequest(despachoEmail, "Carlos Rivera", adminPassword, new[] { "Dispatcher" }, UserKinds.Internal), ct);
                logger.LogInformation("Usuario demo {Email} creado.", despachoEmail);
            }
            // Admin de plataforma (soporte Teikem): opera cualquier tenant, sin membresía
            var platform = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == platformEmail.ToUpperInvariant(), ct);
            if (platform is null)
            {
                var (created, _) = await userAdmin.CreateUserAsync(new UserCreateRequest(platformEmail, "Soporte Teikem", platformPassword, new[] { "TenantAdmin" }, UserKinds.Internal), ct);
                platform = await db.Users.FirstAsync(u => u.Id == created.Id, ct);
            }
            if (!platform.IsPlatformAdmin) { platform.IsPlatformAdmin = true; await db.SaveChangesAsync(ct); }
            // 2026-10-01 (Luis: "no quiero darlo por sentado"): los usuarios de la demo también pasan por el primer ingreso. Solo
            // con Seed:Demo:CompleteOnboarding=true (pruebas automatizadas locales) se marcan completos.
            if (config.GetValue("Seed:Demo:CompleteOnboarding", false))
            {
                var fixtureEmails = new[] { adminEmail, despachoEmail, platformEmail }.Select(e => e.ToUpperInvariant()).ToList();
                var fixtures = await db.Users.Where(u => u.NormalizedEmail != null && fixtureEmails.Contains(u.NormalizedEmail) && u.OnboardingRequired).ToListAsync(ct);
                foreach (var u in fixtures)
                {
                    u.OnboardingRequired = false;
                    u.MustChangePassword = false;
                    u.EmailConfirmed = true;
                    u.EmailVerifiedUtc ??= DateTime.UtcNow;
                }
                if (fixtures.Count > 0) await db.SaveChangesAsync(ct);
            }
            await SeedDemoWarehouseAsync(tenantId, ct);
            tc.UserId = null;
        }
    }

    /// <summary>
    /// Lote 6 (D49): almacén demo ALM-01 'Almacén principal' (PR), ACTIVE con historial WAREHOUSE; zonas y posiciones; muelles
    /// D1 INBOUND y D2 OUTBOUND, FREE con historial WAREHOUSE_DOCK. Solo si el tenant tiene WMS_LOTSERIAL; idempotente por código.
    /// </summary>
    private async Task SeedDemoWarehouseAsync(int tenantId, CancellationToken ct)
    {
        if (!await db.TenantModules.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.ModuleKey == ModuleKeys.WmsLotSerial && m.IsEnabled, ct)) return;

        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Code == DemoWarehouseCode, ct);
        if (warehouse is null)
        {
            var active = await statuses.GetInitialAsync(StatusDomains.WarehouseStatus, ct);
            warehouse = new Warehouse
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Code = DemoWarehouseCode, Name = DemoWarehouseName, City = "San Juan",
                CountryLookupId = await lookups.GetIdAsync(LookupDomains.Country, "PR", ct), StatusCodeId = active.StatusCodeId, IsActive = true,
                // Lote 16 (D8): la demo recibe con acomodo (posición de recepción STG-01 + tareas).
                ReceivingModeLookupId = await lookups.TryGetIdAsync(LookupDomains.ReceivingMode, ReceivingModes.Putaway, ct),
            };
            db.Warehouses.Add(warehouse);
            await db.SaveChangesAsync(ct);
            var to = await statuses.TransitionAsync(StatusDomains.WarehouseStatus, EntityTypes.Warehouse, warehouse.WarehouseId, null, active.InternalCode, null, ct);
            warehouse.StatusCodeId = to.StatusCodeId;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Almacén demo {Code} creado (id {Id}).", DemoWarehouseCode, warehouse.WarehouseId);
        }

        foreach (var (zoneCode, zoneName, zoneType, bins) in DemoZones)
        {
            var zone = await db.WarehouseZones.FirstOrDefaultAsync(z => z.WarehouseId == warehouse.WarehouseId && z.Code == zoneCode, ct);
            if (zone is null)
            {
                zone = new WarehouseZone
                {
                    WarehouseId = warehouse.WarehouseId, Code = zoneCode, Name = zoneName, IsActive = true,
                    ZoneTypeLookupId = await lookups.GetIdAsync(LookupDomains.ZoneType, zoneType, ct),
                };
                db.WarehouseZones.Add(zone);
                await db.SaveChangesAsync(ct);
            }
            foreach (var bin in bins)
            {
                if (await db.WarehouseBins.AnyAsync(b => b.WarehouseId == warehouse.WarehouseId && b.Code == bin.Code, ct)) continue;
                db.WarehouseBins.Add(new WarehouseBin
                {
                    WarehouseZoneId = zone.WarehouseZoneId, WarehouseId = warehouse.WarehouseId, Code = bin.Code,
                    Aisle = bin.Aisle, Rack = bin.Rack, Level = bin.Level, Position = bin.Position, IsActive = true,
                });
            }
            await db.SaveChangesAsync(ct);
        }

        foreach (var (dockCode, dockType) in DemoDocks)
        {
            if (await db.WarehouseDocks.AnyAsync(d => d.WarehouseId == warehouse.WarehouseId && d.Code == dockCode, ct)) continue;
            var free = await statuses.GetInitialAsync(StatusDomains.DockStatus, ct);
            var dock = new WarehouseDock
            {
                WarehouseId = warehouse.WarehouseId, Code = dockCode, DockTypeLookupId = await lookups.GetIdAsync(LookupDomains.DockType, dockType, ct),
                StatusCodeId = free.StatusCodeId, IsActive = true,
            };
            db.WarehouseDocks.Add(dock);
            await db.SaveChangesAsync(ct);
            var to = await statuses.TransitionAsync(StatusDomains.DockStatus, EntityTypes.WarehouseDock, dock.WarehouseDockId, null, free.InternalCode, null, ct);
            dock.StatusCodeId = to.StatusCodeId;
            await db.SaveChangesAsync(ct);
        }
    }
}
