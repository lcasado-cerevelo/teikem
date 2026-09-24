using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Seeding;

/// <summary>
/// Tenant demo (Advance Logistics) para desarrollo: módulos encendidos según el módulo 0B del documento maestro
/// (Última milla, COD, WMS, Equipos en alquiler solo tracking, Portal, Campos personalizados, Compras + Catálogo/Análisis/Sistema;
/// apagados Cross-dock, Marítimo, Facturación de alquiler), admin de compañía, un despachador y un admin de plataforma.
/// Se activa con Seed:Demo:Enabled=true. Idempotente.
/// </summary>
public sealed class DemoTenantSeeder(TeikemDbContext db, ITenantContext tenant, IConfiguration config, ProvisioningService provisioning, UserAdminService userAdmin, ILogger<DemoTenantSeeder> logger)
{
    public static readonly string[] AdvanceModules =
    {
        ModuleKeys.LtlGround, ModuleKeys.Cod, ModuleKeys.WmsLotSerial, ModuleKeys.RentalEquipment, ModuleKeys.ClientPortal,
        ModuleKeys.CustomFields, ModuleKeys.Purchasing, ModuleKeys.Catalog, ModuleKeys.Analytics, ModuleKeys.System,
    };

    public async Task SeedAsync(CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        var name = config["Seed:Demo:TenantName"] ?? "Advance Logistics";
        var adminEmail = config["Seed:Demo:AdminEmail"] ?? "admin@teikem.local";
        var adminPassword = config["Seed:Demo:AdminPassword"] ?? "Teikem_Admin_2026!";
        var platformEmail = config["Seed:Demo:PlatformAdminEmail"] ?? "soporte@teikem.local";
        var platformPassword = config["Seed:Demo:PlatformAdminPassword"] ?? adminPassword;

        int tenantId;
        using (tc.BypassTenantFilter())
        {
            var existing = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == name, ct);
            if (existing is null)
            {
                var result = await provisioning.ProvisionAsync(new TenantProvisionRequest(name, "Advance Logistics, LLC", null, "es", AdvanceModules, adminEmail, "Administrador Advance", adminPassword), ct);
                tenantId = result.Tenant.Id;
                logger.LogInformation("Tenant demo '{Name}' aprovisionado (id {Id}).", name, tenantId);
            }
            else
            {
                tenantId = existing.TenantId;
                using var _ = tc.As(tenantId);
                await provisioning.EnsureAdminUserAsync(existing, adminEmail, "Administrador Advance", adminPassword, ct);
                logger.LogInformation("Tenant demo '{Name}' ya existe (id {Id}); se verifica admin.", name, tenantId);
            }
        }

        using (tc.As(tenantId))
        {
            var admin = await db.Users.AsNoTracking().FirstAsync(u => u.NormalizedEmail == adminEmail.ToUpperInvariant(), ct);
            tc.UserId = admin.Id;
            if (!await db.Users.AnyAsync(u => u.NormalizedEmail == "DESPACHO@TEIKEM.LOCAL", ct))
            {
                await userAdmin.CreateUserAsync(new UserCreateRequest("despacho@teikem.local", "Carlos Rivera", adminPassword, new[] { "Dispatcher" }, UserKinds.Internal), ct);
                logger.LogInformation("Usuario demo despacho@teikem.local creado.");
            }
            // Admin de plataforma (soporte Teikem): opera cualquier tenant, sin membresía
            var platform = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == platformEmail.ToUpperInvariant(), ct);
            if (platform is null)
            {
                var (created, _) = await userAdmin.CreateUserAsync(new UserCreateRequest(platformEmail, "Soporte Teikem", platformPassword, new[] { "TenantAdmin" }, UserKinds.Internal), ct);
                platform = await db.Users.FirstAsync(u => u.Id == created.Id, ct);
            }
            if (!platform.IsPlatformAdmin) { platform.IsPlatformAdmin = true; await db.SaveChangesAsync(ct); }
            tc.UserId = null;
        }
    }
}
