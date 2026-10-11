using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-11 (b) — «Motivo por default del despacho manual» (Ajustes de la compañía): reglas puras, PUT/GET /tenant/settings
/// (código del catálogo activo y habilitado; vacío = quitar; null = sin cambio; 400 con el mensaje exacto sin guardar), un
/// default deshabilitado después se ignora sin romper el guardado del resto, isDefault en GET /manual-issues/reasons, permisos,
/// auditoría, contrato compatible (arreglo con los campos de LookupValueDto + isDefault) y espejo en logistica-db-update.sql.
/// </summary>
public sealed class ManualIssueDefaultReasonTests
{
    // ================================================================ reglas puras

    [Fact]
    public void Default_reason_value_null_is_no_change_blank_clears_and_a_code_is_normalized()
    {
        Assert.Equal((false, (string?)null), PickBatchRules.NormalizeDefaultReason(null));
        Assert.Equal((true, (string?)null), PickBatchRules.NormalizeDefaultReason(""));
        Assert.Equal((true, (string?)null), PickBatchRules.NormalizeDefaultReason("   "));
        Assert.Equal((true, "SALE"), PickBatchRules.NormalizeDefaultReason(" sale "));
    }

    [Fact]
    public void Only_the_saved_reason_still_enabled_and_active_is_the_default()
    {
        Assert.True(PickBatchRules.IsDefaultReason(7, true, true, 7));
        Assert.False(PickBatchRules.IsDefaultReason(7, true, true, 8));
        Assert.False(PickBatchRules.IsDefaultReason(7, true, true, null));
        Assert.False(PickBatchRules.IsDefaultReason(7, false, true, 7));   // deshabilitado por la compañía: se ignora
        Assert.False(PickBatchRules.IsDefaultReason(7, true, false, 7));   // inactivo: se ignora
    }

    // ================================================================ servicio (InMemory)

    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<TenantService>();
        s.AddSingleton<LookupService>();
        s.AddSingleton(sp => new PickBatchService(
            sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ILookupCache>(),
            sp.GetRequiredService<StatusService>(), sp.GetRequiredService<INumberSequenceService>(), sp.GetRequiredService<PermissionService>(),
            sp.GetRequiredService<InventoryLedger>(), null!));
    });

    private static TenantSettingsUpdateRequest Put(string? defaultReason = null, string? name = null)
        => new(name, null, null, null, null, null, null, null, null, null, null, null, null, DefaultManualIssueReason: defaultReason);

    private static async Task<int?> StoredAsync(WmsFixture f)
    {
        f.Db.ChangeTracker.Clear();
        return await f.Db.Tenants.AsNoTracking().Where(t => t.TenantId == WmsFixture.TenantId).Select(t => t.DefaultManualIssueReasonLookupId).SingleAsync();
    }

    private static async Task SetOverrideAsync(WmsFixture f, int tenantId, string code, bool enabled)
    {
        var id = f.LookupId(LookupDomains.ManualIssueReason, code);
        using (f.AsTenant(tenantId))
        {
            var o = await f.Db.LookupCodeOverrides.AsTracking().FirstOrDefaultAsync(x => x.LookupCodeId == id);
            if (o is null) f.Db.LookupCodeOverrides.Add(new LookupCodeOverride { TenantId = tenantId, LookupCodeId = id, IsEnabled = enabled });
            else o.IsEnabled = enabled;
            await f.Db.SaveChangesAsync();
        }
        f.Db.ChangeTracker.Clear();
    }

    private static async Task<IReadOnlyList<ManualIssueReasonDto>> ReasonsAsync(WmsFixture f)
        => await f.Get<PickBatchService>().WithDefaultReasonAsync(
            await f.Get<LookupService>().GetValuesAsync(LookupDomains.ManualIssueReason, false, default), default);

    [Fact]
    public async Task Without_a_default_nothing_is_preselected()
    {
        await using var f = await FixtureAsync();
        Assert.Null((await f.Get<TenantService>().GetSettingsAsync(default)).DefaultManualIssueReason);
        var reasons = await ReasonsAsync(f);
        Assert.Equal(ManualIssueReasons.All.Length, reasons.Count);
        Assert.DoesNotContain(reasons, r => r.IsDefault);
    }

    [Fact]
    public async Task Saving_a_reason_stores_its_id_and_marks_it_as_default_in_the_reasons()
    {
        await using var f = await FixtureAsync();
        var dto = await f.Get<TenantService>().UpdateSettingsAsync(Put(" sale "), default);
        Assert.Equal(ManualIssueReasons.Sale, dto.DefaultManualIssueReason);
        Assert.Equal(f.LookupId(LookupDomains.ManualIssueReason, ManualIssueReasons.Sale), await StoredAsync(f));
        Assert.Equal(ManualIssueReasons.Sale, (await f.Get<TenantService>().GetSettingsAsync(default)).DefaultManualIssueReason);

        var reasons = await ReasonsAsync(f);
        Assert.Equal(ManualIssueReasons.Sale, Assert.Single(reasons, r => r.IsDefault).Code);

        // null = sin cambio (guardar otro ajuste no lo toca); "" = quitarlo.
        await f.Get<TenantService>().UpdateSettingsAsync(Put(name: "Otra"), default);
        Assert.Equal(f.LookupId(LookupDomains.ManualIssueReason, ManualIssueReasons.Sale), await StoredAsync(f));
        Assert.Null((await f.Get<TenantService>().UpdateSettingsAsync(Put(""), default)).DefaultManualIssueReason);
        Assert.Null(await StoredAsync(f));
        Assert.DoesNotContain(await ReasonsAsync(f), r => r.IsDefault);
    }

    [Fact]
    public async Task Unknown_inactive_or_disabled_reason_is_a_400_with_the_exact_message_and_nothing_is_saved()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        await svc.UpdateSettingsAsync(Put(ManualIssueReasons.Sample), default);
        var sample = f.LookupId(LookupDomains.ManualIssueReason, ManualIssueReasons.Sample);
        f.Db.ChangeTracker.Clear();

        var unknown = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Put("regalo", name: "No se guarda"), default));
        Assert.Equal("El motivo REGALO no existe o está inactivo.", Assert.Single(unknown.Errors!["defaultManualIssueReason"]));
        Assert.Equal("El motivo REGALO no existe o está inactivo.", unknown.Message);
        Assert.Equal(400, unknown.StatusCode);
        f.Db.ChangeTracker.Clear();

        // Otro dominio con el mismo código no cuenta.
        var otherDomain = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Put("STANDARD"), default));
        Assert.Equal(PickBatchRules.ManualReasonUnknown("STANDARD"), Assert.Single(otherDomain.Errors!["defaultManualIssueReason"]));
        f.Db.ChangeTracker.Clear();

        var other = await f.Db.LookupCodes.AsTracking().SingleAsync(l => l.Entity == LookupDomains.ManualIssueReason && l.InternalCode == ManualIssueReasons.Other);
        other.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Put("OTHER"), default));
        Assert.Equal(PickBatchRules.ManualReasonUnknown("OTHER"), Assert.Single(inactive.Errors!["defaultManualIssueReason"]));
        f.Db.ChangeTracker.Clear();

        await SetOverrideAsync(f, WmsFixture.TenantId, ManualIssueReasons.InternalUse, enabled: false);
        var disabled = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Put("internal_use"), default));
        Assert.Equal(PickBatchRules.ManualReasonUnknown("INTERNAL_USE"), Assert.Single(disabled.Errors!["defaultManualIssueReason"]));
        f.Db.ChangeTracker.Clear();

        Assert.Equal(sample, await StoredAsync(f));
        Assert.Equal("Tenant de prueba", (await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId)).Name);
    }

    [Fact]
    public async Task A_default_disabled_later_is_ignored_without_breaking_the_settings()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        await svc.UpdateSettingsAsync(Put(ManualIssueReasons.Sale), default);
        var sale = f.LookupId(LookupDomains.ManualIssueReason, ManualIssueReasons.Sale);
        f.Db.ChangeTracker.Clear();

        // Otra compañía que deshabilita SALE no afecta a esta (override por compañía).
        await SetOverrideAsync(f, WmsFixture.OtherTenantId, ManualIssueReasons.Sale, enabled: false);
        Assert.Equal(ManualIssueReasons.Sale, Assert.Single(await ReasonsAsync(f), r => r.IsDefault).Code);

        // La compañía deshabilita SALE: deja de salir (y de marcarse), los ajustes lo muestran sin default y el valor se conserva.
        await SetOverrideAsync(f, WmsFixture.TenantId, ManualIssueReasons.Sale, enabled: false);
        var reasons = await ReasonsAsync(f);
        Assert.DoesNotContain(reasons, r => r.IsDefault);
        Assert.DoesNotContain(reasons, r => r.Code == ManualIssueReasons.Sale);
        Assert.Null((await svc.GetSettingsAsync(default)).DefaultManualIssueReason);
        Assert.Equal(sale, await StoredAsync(f));

        // Guardar el resto de los ajustes (y hasta volver a mandar el mismo código) no falla.
        Assert.Equal("Renombrada", (await svc.UpdateSettingsAsync(Put(name: "Renombrada"), default)).Name);
        f.Db.ChangeTracker.Clear();
        Assert.Null((await svc.UpdateSettingsAsync(Put("sale"), default)).DefaultManualIssueReason);
        Assert.Equal(sale, await StoredAsync(f));

        // Al rehabilitarlo vuelve a ser el default.
        await SetOverrideAsync(f, WmsFixture.TenantId, ManualIssueReasons.Sale, enabled: true);
        Assert.Equal(ManualIssueReasons.Sale, Assert.Single(await ReasonsAsync(f), r => r.IsDefault).Code);
        Assert.Equal(ManualIssueReasons.Sale, (await svc.GetSettingsAsync(default)).DefaultManualIssueReason);

        // Si se inactiva en el catálogo también se ignora.
        var row = await f.Db.LookupCodes.AsTracking().SingleAsync(l => l.LookupCodeId == sale);
        row.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.DoesNotContain(await ReasonsAsync(f), r => r.IsDefault);
        Assert.Null((await svc.GetSettingsAsync(default)).DefaultManualIssueReason);
    }

    [Fact]
    public async Task The_server_still_requires_the_reason_even_with_a_default()
    {
        await using var f = await FixtureAsync();
        await f.Get<TenantService>().UpdateSettingsAsync(Put(ManualIssueReasons.Sale), default);
        f.Db.ChangeTracker.Clear();
        var w = await f.AddWarehouseAsync("W1");
        var p = await f.AddProductAsync("PN");
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<PickBatchService>().ManualIssueAsync(
            new ManualIssueCreateRequest(w.PublicId, new[] { new PickBatchLineRequest(p.PublicId, 1m) }, null, null), default));
        Assert.Equal(PickBatchRules.ManualReasonRequired, Assert.Single(ex.Errors!["reasonCode"]));
    }

    // ================================================================ permisos, auditoría y contrato

    [Fact]
    public void Endpoints_keep_their_permissions_and_the_change_is_audited()
    {
        var put = typeof(TenantController).GetMethod(nameof(TenantController.Update))!;
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.AdminTenant, put.GetCustomAttribute<RequirePermissionAttribute>()!.Policy);
        var reasons = typeof(ManualIssuesController).GetMethod(nameof(ManualIssuesController.Reasons))!;
        Assert.Equal("reasons", reasons.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.WarehouseIssue, reasons.GetCustomAttribute<RequirePermissionAttribute>()!.Policy);
        Assert.Equal(typeof(Task<IReadOnlyList<ManualIssueReasonDto>>), reasons.ReturnType);

        Assert.Equal(EntityTypes.Tenant, typeof(Tenant).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        Assert.Null(typeof(Tenant).GetProperty(nameof(Tenant.DefaultManualIssueReasonLookupId))!.GetCustomAttribute<NotAuditedAttribute>());
    }

    [Fact]
    public void Reason_dto_keeps_every_lookup_value_field_and_adds_is_default()
    {
        // Compatibilidad con la app y la web: la respuesta sigue siendo un arreglo con los campos de LookupValueDto (mismo orden).
        var lookupFields = typeof(LookupValueDto).GetConstructors().Single().GetParameters().Select(p => (p.Name, p.ParameterType)).ToList();
        var reasonFields = typeof(ManualIssueReasonDto).GetConstructors().Single().GetParameters().Select(p => (p.Name, p.ParameterType)).ToList();
        Assert.Equal(lookupFields.Append(("IsDefault", typeof(bool))), reasonFields);

        var v = new LookupValueDto(5, LookupDomains.ManualIssueReason, "SALE", "Venta", new Dictionary<string, string> { ["es"] = "Venta" },
            null, null, 4, true, true, false, null, true);
        var d = ManualIssueReasonDto.From(v, true);
        Assert.Equal((5, "SALE", "Venta", 4, true, true), (d.Id, d.Code, d.Label, d.SortOrder, d.IsEnabled, d.IsDefault));
    }

    [Fact]
    public void Settings_contract_keeps_the_default_reason_as_an_optional_field_followed_only_by_the_default_category()
    {
        // 2026-10-11 (c): después del motivo por default solo se agregó, también opcional y al final, la categoría por defecto de los productos nuevos
        var dto = typeof(TenantSettingsDto).GetConstructors().Single().GetParameters();
        Assert.Equal(("DefaultManualIssueReason", typeof(string)), (dto[^2].Name, dto[^2].ParameterType));
        Assert.Equal(("DefaultProductCategoryId", typeof(int?)), (dto[^1].Name, dto[^1].ParameterType));
        foreach (var p in new[] { dto[^2], dto[^1] }) { Assert.True(p.HasDefaultValue); Assert.Null(p.DefaultValue); }
        var req = typeof(TenantSettingsUpdateRequest).GetConstructors().Single().GetParameters();
        Assert.Equal(("DefaultManualIssueReason", typeof(string)), (req[^2].Name, req[^2].ParameterType));
        Assert.Equal(("DefaultProductCategoryId", typeof(int?)), (req[^1].Name, req[^1].ParameterType));
        foreach (var p in new[] { req[^2], req[^1] }) { Assert.True(p.HasDefaultValue); Assert.Null(p.DefaultValue); }
    }

    // ================================================================ espejo SQL

    [Fact]
    public void Update_sql_adds_the_column_and_its_fk_in_a_new_idempotent_section_at_the_end()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-11 (b) — Motivo por default del despacho manual", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-11 — Despacho manual (DMA-#####)", StringComparison.Ordinal));
        var section = sql[start..];
        Assert.Contains("IF COL_LENGTH('dbo.Tenant', 'DefaultManualIssueReasonLookupId') IS NULL\n    ALTER TABLE dbo.Tenant ADD DefaultManualIssueReasonLookupId INT NULL;", section);
        Assert.Contains("IF OBJECT_ID('dbo.FK_Tenant_DefaultManualIssueReason', 'F') IS NULL", section);
        Assert.Contains("FOREIGN KEY (DefaultManualIssueReasonLookupId) REFERENCES dbo.LookupCode(LookupCodeId);", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.Tenant", section, StringComparison.OrdinalIgnoreCase);   // nadie nace con default
    }
}
