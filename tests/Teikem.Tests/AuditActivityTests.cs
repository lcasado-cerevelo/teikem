using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Audit;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote F10 — GET /api/v1/audit/activity (Seguridad y auditoría → Actividad): el total es el conteo REAL de las dos bitácoras
/// (antes salía de una ventana de skip+take filas y crecía al paginar), las páginas se continúan sin repetir ni saltar filas, el
/// tipo filtra la bitácora y el texto se busca en la base (usuario, etiqueta del tipo, detalle, número de la entidad). Lo de
/// otra compañía no aparece.
/// </summary>
public class AuditActivityTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<WmsFixture> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s => s.AddSingleton<AuditQueryService>());
        LookupCode L(int id, string entity, string code, string es) => new() { LookupCodeId = id, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{es}\"}}", IsActive = true };
        f.Db.LookupCodes.AddRange(
            L(9101, LookupDomains.SecurityEventType, SecurityEventTypes.Login, "Inicio de sesión"),
            L(9102, LookupDomains.SecurityEventType, SecurityEventTypes.PermissionDenied, "Permiso denegado"),
            L(9103, LookupDomains.SecurityOutcome, SecurityOutcomes.Success, "Éxito"),
            L(9104, LookupDomains.SecurityOutcome, SecurityOutcomes.Failure, "Fallo"),
            L(9105, LookupDomains.AuditAction, AuditActions.Update, "Modificación"),
            L(9106, LookupDomains.EntityType, "TENANT", "Compañía"));
        f.Db.Users.Add(new ApplicationUser { Id = 1, UserName = "yo@t.local", Email = "yo@t.local", FullName = "Yo Mismo", IsActive = true });
        f.Db.Users.Add(new ApplicationUser { Id = 2, UserName = "ana@t.local", Email = "ana@t.local", FullName = "Ana Pérez", IsActive = true });
        // 30 cambios (uno por minuto, pares) y 20 eventos (impares): intercalados en el tiempo
        for (var i = 0; i < 30; i++)
            f.Db.AuditLogs.Add(new AuditLog { TenantId = WmsFixture.TenantId, EntityTypeLookupId = 9106, EntityId = 100 + i, ActionLookupId = 9105, UserId = 1,
                ChangesJson = $"{{\"SessionDays\":{{\"from\":{i},\"to\":{i + 1}}}}}", CreatedAtUtc = T0.AddMinutes(2 * i) });
        for (var i = 0; i < 20; i++)
            f.Db.SecurityEvents.Add(new SecurityEvent { TenantId = WmsFixture.TenantId, UserId = i == 0 ? 2 : 1, EventTypeLookupId = i == 0 ? 9102 : 9101,
                OutcomeLookupId = i == 0 ? 9104 : 9103, DetailJson = i == 1 ? "{\"device\":\"zebra-42\"}" : null, CreatedAtUtc = T0.AddMinutes(2 * i + 1) });
        await f.Db.SaveChangesAsync();
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            f.Db.AuditLogs.Add(new AuditLog { TenantId = WmsFixture.OtherTenantId, EntityTypeLookupId = 9106, EntityId = 1, ActionLookupId = 9105, UserId = 1, CreatedAtUtc = T0 });
            f.Db.SecurityEvents.Add(new SecurityEvent { TenantId = WmsFixture.OtherTenantId, UserId = 1, EventTypeLookupId = 9101, OutcomeLookupId = 9103, CreatedAtUtc = T0 });
            await f.Db.SaveChangesAsync();
        }
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        return f;
    }

    [Fact]
    public async Task Total_is_the_real_count_and_pages_continue_without_gaps_or_repeats()
    {
        var f = await SeedAsync();
        var svc = f.Get<AuditQueryService>();

        var first = await svc.GetActivityAsync("all", null, null, null, 0, 10, default);
        Assert.Equal(50, first.Total);
        Assert.Equal(10, first.Items.Count);

        var all = new List<(string, long, DateTime)>();
        for (var skip = 0; skip < 60; skip += 10)
        {
            var page = await svc.GetActivityAsync("all", null, null, null, skip, 10, default);
            Assert.Equal(50, page.Total); // no crece al paginar
            all.AddRange(page.Items.Select(r => (r.Kind, r.Id, r.CreatedAtUtc)));
        }
        Assert.Equal(50, all.Count);
        Assert.Equal(50, all.Distinct().Count());
        Assert.Equal(all.OrderByDescending(x => x.Item3).Select(x => x.Item3), all.Select(x => x.Item3));
        Assert.Equal(30, all.Count(x => x.Item1 == "change"));
    }

    [Fact]
    public async Task Kind_and_date_range_filter_each_log()
    {
        var f = await SeedAsync();
        var svc = f.Get<AuditQueryService>();

        var security = await svc.GetActivityAsync("security", null, null, null, 0, 5, default);
        Assert.Equal(20, security.Total);
        Assert.All(security.Items, r => Assert.Equal("security", r.Kind));
        var changes = await svc.GetActivityAsync("changes", null, null, null, 0, 5, default);
        Assert.Equal(30, changes.Total);
        // [T0, T0+10min): cambios 0..4 (minutos 0,2,4,6,8) y eventos 0..4 (1,3,5,7,9)
        var range = await svc.GetActivityAsync("all", null, T0, T0.AddMinutes(10), 0, 50, default);
        Assert.Equal(10, range.Total);
    }

    [Fact]
    public async Task Text_searches_user_type_label_detail_and_entity_number_in_the_database()
    {
        var f = await SeedAsync();
        var svc = f.Get<AuditQueryService>();

        var ana = await svc.GetActivityAsync("all", "Ana", null, null, 0, 50, default);
        var row = Assert.Single(ana.Items);
        Assert.Equal(1, ana.Total);
        Assert.Equal("PERMISSION_DENIED", row.TypeCode);

        // etiqueta del tipo, sin distinguir mayúsculas (la resuelve el catálogo, no la base)
        Assert.Equal(1, (await svc.GetActivityAsync("all", "permiso", null, null, 0, 50, default)).Total);
        Assert.Equal(1, (await svc.GetActivityAsync("security", "zebra-42", null, null, 0, 50, default)).Total);
        var entity = await svc.GetActivityAsync("changes", "#105", null, null, 0, 50, default);
        Assert.Contains("#105", Assert.Single(entity.Items).Type);
        // "Modificación" = las 30 de la bitácora de cambios
        Assert.Equal(30, (await svc.GetActivityAsync("all", "Modificación", null, null, 0, 10, default)).Total);
    }
}
