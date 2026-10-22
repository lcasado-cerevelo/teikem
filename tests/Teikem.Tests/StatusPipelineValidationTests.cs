using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-21 — Validador del pipeline de estatus: la etapa inicial siempre se exige; la terminal y la del pipeline solo en los dominios que las definen. Un dominio sin etapa terminal (el estatus del cliente:
/// Prospecto → Activo con Suspendido) ya no muestra «Debe quedar habilitada al menos una etapa terminal», que nadie podía corregir.
/// </summary>
public sealed class StatusPipelineValidationTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s => s.AddSingleton<StatusService>());

    [Fact]
    public async Task A_domain_that_defines_no_terminal_stage_is_valid()
    {
        await using var f = await CreateAsync();
        // DockStatus: libre (inicial, pipeline) y ocupado/mantenimiento (laterales); ninguna etapa terminal
        var result = await f.Get<StatusService>().ValidatePipelineAsync(StatusDomains.DockStatus, null, default);
        Assert.True(result.IsValid, string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task A_domain_that_defines_a_terminal_stage_still_needs_one_enabled()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<StatusService>();
        Assert.True((await svc.ValidatePipelineAsync(StatusDomains.WarehouseStatus, null, default)).IsValid);

        // la compañía apaga la única etapa terminal (INACTIVE) de WarehouseStatus
        f.Db.StatusCodeOverrides.Add(new StatusCodeOverride { TenantId = WmsFixture.TenantId, StatusCodeId = f.StatusId(StatusDomains.WarehouseStatus, WarehouseStatuses.Inactive), IsEnabled = false });
        await f.Db.SaveChangesAsync();
        var result = await svc.ValidatePipelineAsync(StatusDomains.WarehouseStatus, null, default);
        Assert.False(result.IsValid);
        Assert.Contains("Debe quedar habilitada al menos una etapa terminal.", result.Errors);
    }

    [Fact]
    public async Task The_initial_stage_is_always_required()
    {
        await using var f = await CreateAsync();
        f.Db.StatusCodeOverrides.Add(new StatusCodeOverride { TenantId = WmsFixture.TenantId, StatusCodeId = f.StatusId(StatusDomains.DockStatus, DockStatuses.Free), IsEnabled = false });
        await f.Db.SaveChangesAsync();
        var result = await f.Get<StatusService>().ValidatePipelineAsync(StatusDomains.DockStatus, null, default);
        Assert.False(result.IsValid);
        Assert.Contains("Debe quedar habilitada al menos una etapa inicial.", result.Errors);
    }
}
