using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Common;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;
using K = Teikem.Domain.Sales.SalesRouteCodes;
using R = Teikem.Domain.Sales.SalesAnalysisRules;
using RR = Teikem.Domain.Sales.SalesRouteRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — la cuota por RUTA en SalesQuota (SalesRouteId) y el ajuste QuotaBy, en la base en memoria (datos INVENTADOS): herencia mensual
/// por ruta (un mes sin cuota vale la anterior; quitar la de un mes vuelve a la anterior), cuota de ruta y de representante en el mismo mes, renglón con los dos ids o
/// sin ninguno (400), cuota negativa o con 3 decimales (400), GET quotas?by=ROUTE con RouteRows y el total, el ajuste (REP por omisión; otro valor = 400), las cuotas
/// de una ruta y la lista de rutas y clientes con lo nuevo (titular, apoyos, clientes, cuota, ruta efectiva y preferido).
/// </summary>
public sealed class SalesQuotaByRouteTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 16, 0, 0, DateTimeKind.Utc);

    private static async Task<WmsFixture> FixtureAsync()
        => await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(new TenantClock(LocalDay.DefaultZone, () => Now));
            s.AddTransient<SalesAnalysisLineReader>();
            s.AddTransient<SalesAnalysisService>();
            s.AddTransient<SalesAnalysisSetupService>();
        });

    private static SalesQuotaBulkRequest Items(params SalesQuotaItemRequest[] items) => new(items);

    [Fact]
    public async Task Route_quota_inherits_month_to_month_and_lives_next_to_the_rep_quota()
    {
        await using var f = await FixtureAsync();
        var setup = f.Get<SalesAnalysisSetupService>();
        var norte = await f.AddRouteAsync("Norte", sortOrder: 1);
        var metro = await f.AddRouteAsync("Metro", sortOrder: 2);
        var rep = await f.AddRepAsync("Ana", norte.SalesRouteId, null);

        await setup.SetQuotasAsync("2026-09", Items(new SalesQuotaItemRequest(null, 1000m, norte.SalesRouteId)), default);
        // en el mismo mes: la cuota de la ruta y la del representante conviven
        var oct = await setup.SetQuotasAsync("2026-10", Items(new SalesQuotaItemRequest(null, 1200m, norte.SalesRouteId), new SalesQuotaItemRequest(rep.SalesRepId, 400m)), default);
        Assert.Equal(K.QuotaByRep, oct.QuotaBy);
        Assert.Equal((400m, 400m, 1200m), (oct.TotalEffective, oct.RepTotalEffective, oct.RouteTotalEffective));
        var norteRow = oct.RouteRows!.Single(r => r.RouteId == norte.SalesRouteId);
        Assert.Equal((1200m, 1200m, "2026-10"), (norteRow.Amount!.Value, norteRow.Effective!.Value, norteRow.EffectiveFrom));
        Assert.Equal(((decimal?)null, (decimal?)null), (oct.RouteRows!.Single(r => r.RouteId == metro.SalesRouteId).Amount, oct.RouteRows!.Single(r => r.RouteId == metro.SalesRouteId).Effective));
        Assert.Equal(400m, oct.Rows.Single().Amount);

        // un mes sin cuota propia vale la anterior; quitar la de octubre vuelve a la de septiembre
        var nov = await setup.GetQuotasAsync("2026-11", "ROUTE", default);
        Assert.Equal((K.QuotaByRoute, 1200m), (nov.QuotaBy, nov.TotalEffective));
        Assert.Equal(((decimal?)null, (decimal?)1200m, "2026-10"), (nov.RouteRows!.Single(r => r.RouteId == norte.SalesRouteId).Amount,
            nov.RouteRows!.Single(r => r.RouteId == norte.SalesRouteId).Effective, nov.RouteRows!.Single(r => r.RouteId == norte.SalesRouteId).EffectiveFrom));
        await setup.SetQuotasAsync("2026-10", Items(new SalesQuotaItemRequest(null, null, norte.SalesRouteId)), default);
        var back = (await setup.GetQuotasAsync("2026-10", "route", default)).RouteRows!.Single(r => r.RouteId == norte.SalesRouteId);
        Assert.Equal(((decimal?)null, (decimal?)1000m, "2026-09"), (back.Amount, back.Effective, back.EffectiveFrom));
        Assert.Equal(400m, (await setup.GetQuotasAsync("2026-10", default)).Rows.Single().Effective);   // la del representante no se tocó

        var history = await setup.GetRouteQuotasAsync(norte.SalesRouteId, default);
        Assert.Equal(new[] { ("2026-09", 1000m) }, history.Quotas.Select(q => (q.Month, q.Amount)).ToArray());
        Assert.Equal(1, await f.Db.Set<SalesQuota>().CountAsync(q => q.SalesRouteId == norte.SalesRouteId && !q.IsActive));   // baja lógica, sin DELETE
    }

    [Fact]
    public async Task Invalid_rows_and_settings_give_the_exact_messages()
    {
        await using var f = await FixtureAsync();
        var setup = f.Get<SalesAnalysisSetupService>();
        var norte = await f.AddRouteAsync("Norte");
        var rep = await f.AddRepAsync("Ana", norte.SalesRouteId, null);

        var both = await Assert.ThrowsAsync<ValidationException>(() => setup.SetQuotasAsync("2026-10", Items(new SalesQuotaItemRequest(rep.SalesRepId, 10m, norte.SalesRouteId)), default));
        Assert.Equal(RR.QuotaTargetInvalid, both.Message);
        var none = await Assert.ThrowsAsync<ValidationException>(() => setup.SetQuotasAsync("2026-10", Items(new SalesQuotaItemRequest(null, 10m)), default));
        Assert.Equal(RR.QuotaTargetInvalid, none.Message);
        var negative = await Assert.ThrowsAsync<ValidationException>(() => setup.SetQuotasAsync("2026-10", Items(new SalesQuotaItemRequest(null, -1m, norte.SalesRouteId)), default));
        Assert.Equal(R.QuotaInvalid, negative.Message);
        var decimals = await Assert.ThrowsAsync<ValidationException>(() => setup.SetQuotasAsync("2026-10", Items(new SalesQuotaItemRequest(null, 1.005m, norte.SalesRouteId)), default));
        Assert.Equal(R.QuotaInvalid, decimals.Message);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => setup.SetQuotasAsync("2026-10", Items(new SalesQuotaItemRequest(null, 1m, 9999)), default));
        Assert.Equal("Ruta no encontrada.", missing.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => setup.GetRouteQuotasAsync(9999, default));

        var badBy = await Assert.ThrowsAsync<ValidationException>(() => setup.GetQuotasAsync("2026-10", "CADENA", default));
        Assert.Equal(RR.QuotaByInvalid, badBy.Message);
        Assert.Equal(K.QuotaByRep, (await setup.GetSettingsAsync(default)).QuotaBy);   // por omisión REP
        var badSetting = await Assert.ThrowsAsync<ValidationException>(() => setup.UpdateSettingsAsync(new SalesAnalysisSettingsRequest(QuotaBy: "X"), default));
        Assert.Equal(RR.QuotaByInvalid, badSetting.Message);
        var ok = await setup.UpdateSettingsAsync(new SalesAnalysisSettingsRequest(QuotaBy: " route "), default);
        Assert.Equal(K.QuotaByRoute, ok.QuotaBy);
        Assert.Equal(K.QuotaByRoute, (await setup.UpdateSettingsAsync(new SalesAnalysisSettingsRequest(LeadTimeDays: 9), default)).QuotaBy);   // sin quotaBy no cambia
        Assert.Equal(K.QuotaByRoute, (await setup.GetQuotasAsync("2026-10", default)).QuotaBy);   // sin «by», el del ajuste
    }

    [Fact]
    public async Task Routes_and_clients_lists_show_primary_supports_clients_quota_and_effective_route()
    {
        await using var f = await FixtureAsync();
        var setup = f.Get<SalesAnalysisSetupService>();
        await f.AddActiveMemberAsync(401, "Titular Uno");
        await f.AddSuspendedMemberAsync(402, "Apoyo Suspendido");
        var norte = await f.AddRouteAsync("Norte", sortOrder: 1);
        var metro = await f.AddRouteAsync("Metro", sortOrder: 2);
        var rep = await f.AddRepAsync("Rep Metro", metro.SalesRouteId, null);
        await f.AddAssignmentAsync(norte.SalesRouteId, 401, true, new DateOnly(2026, 1, 1));
        await f.AddAssignmentAsync(norte.SalesRouteId, 402, false, new DateOnly(2026, 1, 1));
        await f.AddAssignmentAsync(norte.SalesRouteId, 402, false, new DateOnly(2025, 6, 1), new DateOnly(2025, 12, 1));   // cerrada: no cuenta
        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        var c3 = await f.AddClientAsync("C3");
        await f.SetClientSalesAsync(c1.ClientId, norte.SalesRouteId, preferredUserId: 401);
        await f.SetClientSalesAsync(c2.ClientId, null, rep.SalesRepId);
        await setup.SetQuotasAsync("2026-11", Items(new SalesQuotaItemRequest(null, 500m, norte.SalesRouteId)), default);

        var routes = await setup.ListRoutesAsync(false, default);
        var n = routes.Single(r => r.Id == norte.SalesRouteId);
        Assert.Equal((401, "Titular Uno", true, true, 1, 1, 500m), (n.PrimaryUserId!.Value, n.PrimaryName, n.PrimaryIsActiveMember, n.HasPrimary, n.SupportCount, n.ClientCount, n.CurrentQuota!.Value));
        var m = routes.Single(r => r.Id == metro.SalesRouteId);
        Assert.Equal((false, 0, 1, 1), (m.HasPrimary, m.SupportCount, m.ClientCount, m.RepCount));   // C2 cuenta en Metro por su representante

        var all = await setup.ListClientsAsync(null, false, null, false, null, 0, 50, default);
        var r1 = all.Items.Single(c => c.ClientId == c1.ClientId);
        Assert.Equal((norte.SalesRouteId, "Norte", "Norte", false, 401, "Titular Uno"), (r1.DirectRouteId!.Value, r1.DirectRouteName, r1.EffectiveRouteName, r1.RouteFromRep, r1.PreferredUserId!.Value, r1.PreferredName));
        var r2 = all.Items.Single(c => c.ClientId == c2.ClientId);
        Assert.Equal(((int?)null, metro.SalesRouteId, "Metro", true), (r2.DirectRouteId, r2.EffectiveRouteId!.Value, r2.EffectiveRouteName, r2.RouteFromRep));

        var inMetro = await setup.ListClientsAsync(null, false, metro.SalesRouteId, false, null, 0, 50, default);
        Assert.Equal(c2.ClientId, Assert.Single(inMetro.Items).ClientId);
        var noRoute = await setup.ListClientsAsync(null, false, null, true, null, 0, 50, default);
        Assert.Equal(c3.ClientId, Assert.Single(noRoute.Items).ClientId);
    }
}
