using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Common;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;
using R = Teikem.Domain.Sales.SalesAnalysisRules;
using RR = Teikem.Domain.Sales.SalesRouteRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — el representante con usuario OBLIGATORIO y el TITULAR AUTOMÁTICO, en la base en memoria (datos INVENTADOS): alta sin usuario
/// (400), con el usuario de otro representante activo (409), usuario no miembro o suspendido (400), quitar el usuario (400), reactivar con un usuario tomado (409), un
/// representante heredado sin usuario se puede renombrar; crear un representante con usuario en una ruta sin titular lo hace titular (BecamePrimary) y en una con titular
/// no; ponerle usuario a un heredado lo hace titular; desactivar una ruta con clientes da 409 y sin clientes cierra sus personas vigentes con fecha de hoy.
/// </summary>
public sealed class SalesRepAutoPrimaryTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 25);

    private static async Task<(WmsFixture F, SalesAnalysisSetupService Setup)> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(new TenantClock(LocalDay.DefaultZone, () => Now));
            s.AddTransient<SalesAnalysisLineReader>();
            s.AddTransient<SalesAnalysisService>();
            s.AddTransient<SalesAnalysisSetupService>();
        });
        await f.AddActiveMemberAsync(501, "Vendedor Uno");
        await f.AddActiveMemberAsync(502, "Vendedor Dos");
        await f.AddSuspendedMemberAsync(503, "Vendedor Suspendido");
        await f.AddPortalUserAsync(504, "Cliente Portal");
        await f.AddActiveMemberAsync(505, "Otra Compania", tenantId: WmsFixture.OtherTenantId);
        return (f, f.Get<SalesAnalysisSetupService>());
    }

    private static Task<List<SalesRouteAssignment>> OpenAsync(WmsFixture f, int routeId)
        => f.Db.Set<SalesRouteAssignment>().AsNoTracking().Where(a => a.SalesRouteId == routeId && a.IsActive && a.ValidTo == null).ToListAsync();

    [Fact]
    public async Task The_rep_user_is_required_unique_and_an_active_member()
    {
        var (f, setup) = await WorldAsync();
        await using var _ = f;
        var norte = await setup.CreateRouteAsync(new SalesRouteRequest("Norte"), default);

        var noUser = await Assert.ThrowsAsync<ValidationException>(() => setup.CreateRepAsync(new SalesRepRequest("Sin Usuario", norte.Id), default));
        Assert.Equal(RR.RepUserRequired, noUser.Message);
        var suspended = await Assert.ThrowsAsync<ValidationException>(() => setup.CreateRepAsync(new SalesRepRequest("Susp", norte.Id, UserId: 503), default));
        Assert.Equal(RR.UserSuspended, suspended.Message);
        foreach (var outsider in new[] { 504, 505, 999 })
        {
            var notMember = await Assert.ThrowsAsync<ValidationException>(() => setup.CreateRepAsync(new SalesRepRequest("Fuera", norte.Id, UserId: outsider), default));
            Assert.Equal(RR.UserNotMember, notMember.Message);
        }

        var uno = await setup.CreateRepAsync(new SalesRepRequest("Uno", norte.Id, UserId: 501), default);
        Assert.True(uno.HasUser && uno.UserIsActiveMember);
        var taken = await Assert.ThrowsAsync<ConflictException>(() => setup.CreateRepAsync(new SalesRepRequest("Uno Bis", norte.Id, UserId: 501), default));
        Assert.Equal(RR.RepUserTaken("Uno"), taken.Message);
        var clear = await Assert.ThrowsAsync<ValidationException>(() => setup.UpdateRepAsync(uno.Id, new SalesRepRequest(null, ClearUser: true), default));
        Assert.Equal(RR.RepUserRequired, clear.Message);

        // un representante heredado sin usuario (del Lote 241) se puede renombrar y cambiar de ruta sin mandar usuario
        var legacy = await f.AddRepAsync("Heredado", null, null);
        var renamed = await setup.UpdateRepAsync(legacy.SalesRepId, new SalesRepRequest("Heredado Renombrado", norte.Id), default);
        Assert.Equal(("Heredado Renombrado", false, false), (renamed.Name, renamed.HasUser, renamed.BecamePrimary));
        Assert.Equal(new[] { legacy.SalesRepId }, (await setup.ListRepsAsync(false, true, default)).Select(r => r.Id).ToArray());   // withoutUser

        // reactivar con un usuario que ya es de otro representante activo → 409
        await setup.SetRepActiveAsync(uno.Id, false, default);
        await setup.CreateRepAsync(new SalesRepRequest("Uno Nuevo", norte.Id, UserId: 501), default);
        var reactivate = await Assert.ThrowsAsync<ConflictException>(() => setup.SetRepActiveAsync(uno.Id, true, default));
        Assert.Equal(RR.RepUserTaken("Uno Nuevo"), reactivate.Message);
    }

    [Fact]
    public async Task Creating_or_giving_a_user_to_a_rep_of_a_route_without_primary_makes_it_primary()
    {
        var (f, setup) = await WorldAsync();
        await using var _ = f;
        var norte = await setup.CreateRouteAsync(new SalesRouteRequest("Norte"), default);
        var metro = await setup.CreateRouteAsync(new SalesRouteRequest("Metro"), default);

        var uno = await setup.CreateRepAsync(new SalesRepRequest("Uno", norte.Id, UserId: 501), default);
        Assert.True(uno.BecamePrimary);
        Assert.True(uno.IsPrimaryOfRoute);
        var primary = Assert.Single(await OpenAsync(f, norte.Id));
        Assert.Equal((501, true, Today), (primary.UserId, primary.IsPrimary, primary.ValidFrom));

        // la ruta ya tiene titular: el segundo no lo reemplaza (el administrador decide)
        var dos = await setup.CreateRepAsync(new SalesRepRequest("Dos", norte.Id, UserId: 502), default);
        Assert.False(dos.BecamePrimary);
        Assert.Equal(501, Assert.Single(await OpenAsync(f, norte.Id)).UserId);

        // ponerle usuario a un representante heredado de una ruta sin titular lo hace titular
        var legacy = await f.AddRepAsync("Heredado Metro", metro.Id, null);
        Assert.Empty(await OpenAsync(f, metro.Id));
        await setup.SetRepActiveAsync(dos.Id, false, default);   // libera al usuario 502
        var withUser = await setup.UpdateRepAsync(legacy.SalesRepId, new SalesRepRequest(null, UserId: 502), default);
        Assert.True(withUser.BecamePrimary);
        Assert.Equal(502, Assert.Single(await OpenAsync(f, metro.Id)).UserId);
        Assert.True((await setup.ListRoutesAsync(false, default)).Single(r => r.Id == metro.Id).HasPrimary);
    }

    [Fact]
    public async Task Deactivating_a_route_with_clients_is_blocked_and_without_them_closes_its_people_today()
    {
        var (f, setup) = await WorldAsync();
        await using var _ = f;
        var norte = await f.AddRouteAsync("Norte");
        await f.AddAssignmentAsync(norte.SalesRouteId, 501, true, new DateOnly(2026, 3, 1));
        await f.AddAssignmentAsync(norte.SalesRouteId, 502, false, Today);
        var client = await f.AddClientAsync("C1");
        await f.SetClientSalesAsync(client.ClientId, norte.SalesRouteId);

        var busy = await Assert.ThrowsAsync<ConflictException>(() => setup.SetRouteActiveAsync(norte.SalesRouteId, false, default));
        Assert.Equal(RR.RouteHasClients, busy.Message);
        Assert.Equal(2, (await OpenAsync(f, norte.SalesRouteId)).Count);

        await f.SetClientSalesAsync(client.ClientId);
        var off = await setup.SetRouteActiveAsync(norte.SalesRouteId, false, default);
        Assert.False(off.IsActive);
        Assert.Empty(await OpenAsync(f, norte.SalesRouteId));
        var closed = await f.Db.Set<SalesRouteAssignment>().AsNoTracking().Where(a => a.SalesRouteId == norte.SalesRouteId).ToListAsync();
        Assert.All(closed, a => Assert.Equal((Today, true), (a.ValidTo!.Value, a.IsActive)));

        // reactivar no las reabre
        var on = await setup.SetRouteActiveAsync(norte.SalesRouteId, true, default);
        Assert.False(on.HasPrimary);

        // los representantes activos siguen bloqueando como en el Lote 241
        await f.AddRepAsync("Rep", norte.SalesRouteId, null);
        var reps = await Assert.ThrowsAsync<ConflictException>(() => setup.SetRouteActiveAsync(norte.SalesRouteId, false, default));
        Assert.Equal(R.RouteHasReps, reps.Message);
    }
}
