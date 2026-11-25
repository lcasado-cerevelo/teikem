using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;
using A = Teikem.Domain.Sales.SalesAnalysisRules;
using K = Teikem.Domain.Sales.SalesRouteCodes;
using R = Teikem.Domain.Sales.SalesRouteRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — <see cref="SalesRouteService"/> en la base en memoria con dos compañías (datos INVENTADOS): personas de la ruta (titular,
/// apoyo, cerrar, anular, historial), validaciones de persona y fechas, la venta del cliente (ruta, vendedor preferido de la ruta del cliente, RowVersion), la ruta en
/// bloque, «Pasar rutas desde representantes» (simulación, idempotencia) y el buscador de personas. Hoy = 2026-11-25 en Puerto Rico.
/// </summary>
public sealed class SalesRouteServiceTests
{
    private const int Other = WmsFixture.OtherTenantId;
    private static readonly DateOnly Today = new(2026, 11, 25);
    private static readonly DateOnly D1 = new(2026, 10, 1);
    private static readonly DateOnly D15 = new(2026, 10, 15);
    private static readonly DateTime Now = new(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);

    private static SalesRouteService Svc(WmsFixture f) => new(f.Db, f.Tenant, new TenantClock(TenantClock.Default.Zone, () => Now));

    private static Task<List<SalesRouteAssignment>> RowsAsync(WmsFixture f, int routeId)
        => f.Db.Set<SalesRouteAssignment>().IgnoreQueryFilters().AsNoTracking().Where(a => a.SalesRouteId == routeId).OrderBy(a => a.SalesRouteAssignmentId).ToListAsync();

    // ================================================================ personas de la ruta

    [Fact]
    public async Task Primer_titular_y_el_nuevo_cierra_al_anterior_con_su_fecha_de_inicio()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(101, "Ana Titular");
        await f.AddActiveMemberAsync(102, "Beto Nuevo");
        var norte = await f.AddRouteAsync("Norte");
        var svc = Svc(f);

        var p1 = await svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(101, "primary", D1), default);
        Assert.Equal(101, p1.Primary!.UserId);
        Assert.Equal(K.RolePrimary, p1.Primary.Role);
        Assert.True(p1.Primary.IsCurrent);
        Assert.True(p1.Primary.UserIsActiveMember);
        Assert.Equal("Ana Titular", p1.Primary.UserName);

        var p2 = await svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(102, K.RolePrimary, D15), default);
        Assert.Equal(102, p2.Primary!.UserId);
        var closed = Assert.Single(p2.History);
        Assert.Equal(101, closed.UserId);
        Assert.Equal(D15, closed.ValidTo);
        Assert.False(closed.IsCurrent);

        // sin fecha = hoy
        await f.AddActiveMemberAsync(103, "Caro Hoy");
        var p3 = await svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(103, K.RoleSupport), default);
        Assert.Equal(Today, p3.Supports.Single().ValidFrom);
    }

    [Fact]
    public async Task Hacer_titular_a_un_apoyo_cierra_su_fila_de_apoyo_y_los_dobles_dan_409()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(201, "Titular");
        await f.AddActiveMemberAsync(202, "Apoyo");
        var norte = await f.AddRouteAsync("Norte");
        var svc = Svc(f);
        await svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(201, K.RolePrimary, D1), default);
        await svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(202, K.RoleSupport, D1), default);

        var again = await Assert.ThrowsAsync<ConflictException>(() => svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(201, K.RolePrimary, D15), default));
        Assert.Equal(R.AlreadyPrimary, again.Message);
        var support = await Assert.ThrowsAsync<ConflictException>(() => svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(202, K.RoleSupport, D15), default));
        Assert.Equal(R.AlreadySupport, support.Message);
        var primaryAsSupport = await Assert.ThrowsAsync<ConflictException>(() => svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(201, K.RoleSupport, D15), default));
        Assert.Equal(R.PrimaryCannotBeSupport, primaryAsSupport.Message);

        var p = await svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(202, K.RolePrimary, D15), default);
        Assert.Equal(202, p.Primary!.UserId);
        Assert.Empty(p.Supports);
        Assert.Equal(2, p.History.Count);   // el titular anterior y la fila de apoyo, cerradas el día 15
        Assert.All(p.History, h => Assert.Equal(D15, h.ValidTo));
        Assert.Contains(p.History, h => h.UserId == 202 && h.Role == K.RoleSupport);
    }

    [Fact]
    public async Task Fechas_futuras_o_anteriores_al_titular_dan_400()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(301, "Titular");
        await f.AddActiveMemberAsync(302, "Otro");
        var norte = await f.AddRouteAsync("Norte");
        var svc = Svc(f);

        var future = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(301, K.RolePrimary, Today.AddDays(1)), default));
        Assert.Equal(R.ValidFromFuture, future.Message);
        await svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(301, K.RolePrimary, D15), default);
        var before = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(302, K.RolePrimary, D1), default));
        Assert.Equal(R.ValidFromBeforePrimary(D15), before.Message);
        var role = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(302, "JEFE", D15), default));
        Assert.Equal(R.RoleInvalid, role.Message);
        var user = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(null, K.RoleSupport), default));
        Assert.Equal(R.UserRequired, user.Message);
    }

    [Fact]
    public async Task Cerrar_valida_fechas_y_estado_y_anular_saca_del_historial()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(401, "Titular");
        await f.AddActiveMemberAsync(402, "Apoyo por error");
        var norte = await f.AddRouteAsync("Norte");
        var sur = await f.AddRouteAsync("Sur");
        var svc = Svc(f);
        var p = await svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(401, K.RolePrimary, D15), default);
        var id = p.Primary!.Id;

        var early = await Assert.ThrowsAsync<ValidationException>(() => svc.CloseAsync(norte.SalesRouteId, id, new SalesRouteCloseRequest(D1), default));
        Assert.Equal(R.ValidToBeforeFrom, early.Message);
        var future = await Assert.ThrowsAsync<ValidationException>(() => svc.CloseAsync(norte.SalesRouteId, id, new SalesRouteCloseRequest(Today.AddDays(1)), default));
        Assert.Equal(R.ValidToFuture, future.Message);
        var otherRoute = await Assert.ThrowsAsync<NotFoundException>(() => svc.CloseAsync(sur.SalesRouteId, id, null, default));
        Assert.Equal("Asignación no encontrada.", otherRoute.Message);

        var closed = await svc.CloseAsync(norte.SalesRouteId, id, null, default);   // hoy
        Assert.Null(closed.Primary);
        Assert.Equal(Today, closed.History.Single().ValidTo);
        var twice = await Assert.ThrowsAsync<ConflictException>(() => svc.CloseAsync(norte.SalesRouteId, id, null, default));
        Assert.Equal(R.AssignmentClosed, twice.Message);

        var s = await svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(402, K.RoleSupport, D15), default);
        var sid = s.Supports.Single().Id;
        var voided = await svc.VoidAsync(norte.SalesRouteId, sid, default);
        Assert.Empty(voided.Supports);
        Assert.DoesNotContain(voided.History, h => h.Id == sid);   // sale del historial
        var audit = await svc.GetPeopleAsync(norte.SalesRouteId, true, true, default);
        var row = Assert.Single(audit.History, h => h.Id == sid);
        Assert.True(row.IsVoided);
        Assert.Equal(D15, row.ValidTo);   // ValidTo ??= ValidFrom
        var again = await Assert.ThrowsAsync<ConflictException>(() => svc.VoidAsync(norte.SalesRouteId, sid, default));
        Assert.Equal(R.AssignmentVoided, again.Message);

        // sin historial: solo las vigentes
        Assert.Empty((await svc.GetPeopleAsync(norte.SalesRouteId, false, false, default)).History);
    }

    [Fact]
    public async Task Persona_sin_membresia_suspendida_de_otra_compania_o_del_portal_da_400()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddSuspendedMemberAsync(501, "Suspendida");
        await f.AddMemberAsync(502, "Invitado", MembershipStatuses.Invited, userActive: true);
        await f.AddMemberAsync(503, "Inactivo", MembershipStatuses.Active, userActive: false);
        await f.AddActiveMemberAsync(504, "De otra", tenantId: Other);
        await f.AddPortalUserAsync(505, "Portal");
        var norte = await f.AddRouteAsync("Norte");
        var svc = Svc(f);

        async Task<string> Fail(int userId)
            => (await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(norte.SalesRouteId, new SalesRouteAssignRequest(userId, K.RoleSupport), default))).Message;
        Assert.Equal(R.UserSuspended, await Fail(501));
        Assert.Equal(R.UserSuspended, await Fail(502));
        Assert.Equal(R.UserSuspended, await Fail(503));
        Assert.Equal(R.UserNotMember, await Fail(504));
        Assert.Equal(R.UserNotMember, await Fail(505));
        Assert.Equal(R.UserNotMember, await Fail(999));
        Assert.Empty(await RowsAsync(f, norte.SalesRouteId));
    }

    [Fact]
    public async Task Ruta_de_otra_compania_da_404_e_inactiva_da_400()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(601, "Ana");
        var ajena = await f.AddRouteAsync("Ajena", Other);
        var vieja = await f.AddRouteAsync("Vieja", isActive: false);
        var svc = Svc(f);

        var nf = await Assert.ThrowsAsync<NotFoundException>(() => svc.AssignAsync(ajena.SalesRouteId, new SalesRouteAssignRequest(601, K.RoleSupport), default));
        Assert.Equal("Ruta no encontrada.", nf.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetPeopleAsync(ajena.SalesRouteId, true, false, default));
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(vieja.SalesRouteId, new SalesRouteAssignRequest(601, K.RoleSupport), default));
        Assert.Equal(A.RouteInactive, inactive.Message);
    }

    [Fact]
    public async Task Candidatos_solo_personas_activas_con_buscador()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(701, "Vendedor Uno");
        await f.AddActiveMemberAsync(702, "Vendedora Dos");
        await f.AddSuspendedMemberAsync(703, "Vendedor Suspendido");
        await f.AddActiveMemberAsync(704, "Vendedor Ajeno", tenantId: Other);
        await f.AddPortalUserAsync(705, "Vendedor Portal");
        var svc = Svc(f);

        var all = await svc.ListCandidatesAsync(null, 50, default);
        Assert.Equal(new[] { 701, 702 }, all.Select(c => c.UserId).OrderBy(x => x).ToArray());
        var dos = await svc.ListCandidatesAsync("Dos", 50, default);
        Assert.Equal(702, Assert.Single(dos).UserId);
        Assert.Single(await svc.ListCandidatesAsync(null, 1, default));
        Assert.Equal(2, (await svc.ListCandidatesAsync(null, 500, default)).Count);   // el tope se acota a 50
    }

    // ================================================================ venta del cliente

    [Fact]
    public async Task SetClientSales_ruta_y_preferido_de_la_ruta_del_cliente()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(801, "Titular Norte");
        await f.AddActiveMemberAsync(802, "Apoyo Norte");
        await f.AddActiveMemberAsync(803, "De Sur");
        await f.AddSuspendedMemberAsync(804, "Apoyo Suspendido");
        var norte = await f.AddRouteAsync("Norte");
        var sur = await f.AddRouteAsync("Sur");
        var vieja = await f.AddRouteAsync("Vieja", isActive: false);
        await f.AddAssignmentAsync(norte.SalesRouteId, 801, true, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 802, false, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 804, false, D1);
        await f.AddAssignmentAsync(sur.SalesRouteId, 803, true, D1);
        var c = await f.AddClientAsync("C1");
        var svc = Svc(f);

        // sin ruta no admite preferido
        var noRoute = await Assert.ThrowsAsync<ValidationException>(() => svc.SetClientSalesAsync(c.ClientId, new SalesClientSalesRequest(PreferredUserId: 801), default));
        Assert.Equal(R.PreferredNeedsRoute, noRoute.Message);
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => svc.SetClientSalesAsync(c.ClientId, new SalesClientSalesRequest(RouteId: vieja.SalesRouteId), default));
        Assert.Equal(A.RouteInactive, inactive.Message);

        // ruta y preferido en la misma llamada (el preferido se valida con la ruta nueva)
        var s = await svc.SetClientSalesAsync(c.ClientId, new SalesClientSalesRequest(RouteId: norte.SalesRouteId, PreferredUserId: 802), default);
        Assert.Equal(norte.SalesRouteId, s.RouteId);
        Assert.Equal(norte.SalesRouteId, s.DirectRouteId);
        Assert.False(s.RouteFromRep);
        Assert.Equal(802, s.Preferred!.UserId);
        Assert.False(s.PreferredOutOfRoute);
        Assert.Equal(802, s.ProposedUserId);
        Assert.Equal("Apoyo Norte", s.ProposedName);
        Assert.Equal(K.SellerKindPreferred, s.ProposedKind);
        Assert.Equal(801, s.Primary!.UserId);

        async Task<string> Fail(int userId)
            => (await Assert.ThrowsAsync<ValidationException>(() => svc.SetClientSalesAsync(c.ClientId, new SalesClientSalesRequest(PreferredUserId: userId), default))).Message;
        Assert.Equal(R.PreferredNotInRoute, await Fail(803));   // es de otra ruta
        Assert.Equal(R.PreferredNotInRoute, await Fail(804));   // de la ruta, pero suspendido
        Assert.Equal(R.PreferredNotInRoute, await Fail(999));   // no es miembro

        // cambiar de ruta no bloquea: el preferido queda y la ficha lo señala
        var moved = await svc.SetClientSalesAsync(c.ClientId, new SalesClientSalesRequest(RouteId: sur.SalesRouteId), default);
        Assert.Equal(802, moved.Preferred!.UserId);
        Assert.True(moved.PreferredOutOfRoute);
        // mandar el mismo preferido no es un cambio
        Assert.True((await svc.SetClientSalesAsync(c.ClientId, new SalesClientSalesRequest(PreferredUserId: 802), default)).PreferredOutOfRoute);

        var cleared = await svc.SetClientSalesAsync(c.ClientId, new SalesClientSalesRequest(ClearRoute: true, ClearPreferred: true), default);
        Assert.Null(cleared.RouteId);
        Assert.Null(cleared.Preferred);
        Assert.Null(cleared.ProposedUserId);
    }

    [Fact]
    public async Task GetClientSales_hereda_la_ruta_del_representante_y_404_para_otra_compania()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(901, "Titular Metro");
        var metro = await f.AddRouteAsync("Metro");
        await f.AddAssignmentAsync(metro.SalesRouteId, 901, true, D1);
        var rep = await f.AddRepAsync("Rep Metro", metro.SalesRouteId, null);
        var c = await f.AddClientAsync("C1");
        await f.SetClientSalesAsync(c.ClientId, repId: rep.SalesRepId);
        var ajeno = await f.AddClientAsync("X1", Other);
        var svc = Svc(f);

        var s = await svc.GetClientSalesAsync(c.ClientId, default);
        Assert.Equal(metro.SalesRouteId, s.RouteId);
        Assert.Equal("Metro", s.RouteName);
        Assert.True(s.RouteFromRep);
        Assert.Null(s.DirectRouteId);
        Assert.Equal(rep.SalesRepId, s.RepId);
        Assert.Equal("Rep Metro", s.RepName);
        Assert.Equal("Metro", s.RepRouteName);
        Assert.True(s.RepIsActive);
        Assert.Equal(901, s.ProposedUserId);
        Assert.Equal(K.SellerKindPrimary, s.ProposedKind);

        // la ruta heredada admite preferido de esa ruta
        Assert.Equal(901, (await svc.SetClientSalesAsync(c.ClientId, new SalesClientSalesRequest(PreferredUserId: 901), default)).Preferred!.UserId);

        var nf = await Assert.ThrowsAsync<NotFoundException>(() => svc.GetClientSalesAsync(ajeno.ClientId, default));
        Assert.Equal("Cliente no encontrado.", nf.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.SetClientSalesAsync(ajeno.ClientId, new SalesClientSalesRequest(ClearRoute: true), default));
    }

    [Fact]
    public async Task SetClientSales_con_RowVersion_vieja_da_409()
    {
        await using var f = await WmsFixture.CreateAsync();
        var norte = await f.AddRouteAsync("Norte");
        var c = await f.AddClientAsync("C1");
        var svc = Svc(f);
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.SetClientSalesAsync(c.ClientId, new SalesClientSalesRequest(RouteId: norte.SalesRouteId, RowVersion: stale), default));
        Assert.Equal(DbExtensions.ConcurrencyMessage, ex.Message);
        Assert.Null((await f.Db.Clients.AsNoTracking().SingleAsync(x => x.ClientId == c.ClientId)).SalesRouteId);
    }

    [Fact]
    public async Task AssignClientsRoute_valida_tope_y_compania_y_cuenta_los_cambios()
    {
        await using var f = await WmsFixture.CreateAsync();
        var norte = await f.AddRouteAsync("Norte");
        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        await f.SetClientSalesAsync(c2.ClientId, routeId: norte.SalesRouteId);
        var ajeno = await f.AddClientAsync("X1", Other);
        var svc = Svc(f);

        var many = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignClientsRouteAsync(new SalesClientRouteBulkRequest(Enumerable.Range(1, 2001).ToList(), norte.SalesRouteId), default));
        Assert.Equal(A.TooMany, many.Message);
        var none = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignClientsRouteAsync(new SalesClientRouteBulkRequest(new List<int>(), norte.SalesRouteId), default));
        Assert.Equal(A.ClientsRequired, none.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.AssignClientsRouteAsync(new SalesClientRouteBulkRequest(new[] { c1.ClientId, ajeno.ClientId }, norte.SalesRouteId), default));

        var r = await svc.AssignClientsRouteAsync(new SalesClientRouteBulkRequest(new[] { c1.ClientId, c2.ClientId }, norte.SalesRouteId), default);
        Assert.Equal(1, r.Updated);   // c2 ya la tenía
        Assert.Equal("Norte", r.RouteName);
        var off = await svc.AssignClientsRouteAsync(new SalesClientRouteBulkRequest(new[] { c1.ClientId, c2.ClientId }, null), default);
        Assert.Equal(2, off.Updated);
        Assert.True(await f.Db.Clients.AsNoTracking().Where(x => x.ClientId == c1.ClientId || x.ClientId == c2.ClientId).AllAsync(x => x.SalesRouteId == null));
    }

    // ================================================================ pasar rutas desde representantes

    [Fact]
    public async Task RoutesFromReps_simula_aplica_y_la_segunda_corrida_no_cambia_nada()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(1001, "Vendedor Norte");
        await f.AddActiveMemberAsync(1002, "Vendedor Sur A");
        await f.AddActiveMemberAsync(1003, "Vendedor Sur B");
        await f.AddSuspendedMemberAsync(1004, "Vendedor Suspendido");
        var norte = await f.AddRouteAsync("Norte");
        var sur = await f.AddRouteAsync("Sur");
        var cadenas = await f.AddRouteAsync("Cadenas");
        var este = await f.AddRouteAsync("Este");
        var repNorte = await f.AddRepAsync("Rep Norte", norte.SalesRouteId, 1001);
        await f.AddRepAsync("Rep Norte Sin Usuario", norte.SalesRouteId, null);
        var repSurA = await f.AddRepAsync("Rep Sur A", sur.SalesRouteId, 1002);
        await f.AddRepAsync("Rep Sur B", sur.SalesRouteId, 1003);
        var repCadenas = await f.AddRepAsync("ADVANCE SOLUTIONS GROUP LLC", cadenas.SalesRouteId, null);
        await f.AddRepAsync("Rep Este", este.SalesRouteId, 1004);   // usuario suspendido: no cuenta
        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        var c3 = await f.AddClientAsync("C3");
        var c4 = await f.AddClientAsync("C4");
        await f.SetClientSalesAsync(c1.ClientId, repId: repNorte.SalesRepId);
        await f.SetClientSalesAsync(c2.ClientId, repId: repSurA.SalesRepId);
        await f.SetClientSalesAsync(c3.ClientId, routeId: este.SalesRouteId, repId: repCadenas.SalesRepId);   // ya tiene ruta directa: no se toca
        await f.SetClientSalesAsync(c4.ClientId, repId: repCadenas.SalesRepId);
        // historial: el inicio es el primer día del mes más antiguo con datos
        f.Db.Set<SalesQuota>().Add(new SalesQuota { TenantId = WmsFixture.TenantId, SalesRepId = repNorte.SalesRepId, Month = new DateOnly(2026, 3, 1), Amount = 100m });
        f.Db.Invoices.Add(new Invoice { TenantId = WmsFixture.TenantId, ClientId = c1.ClientId, IssueDate = new DateOnly(2026, 2, 17), IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var svc = Svc(f);

        var dry = await svc.RoutesFromRepsAsync(true, default);
        Assert.True(dry.DryRun);
        Assert.Equal(3, dry.ClientsRouted);   // c1, c2 y c4 (Cadenas es una ruta más)
        Assert.Equal(1, dry.PrimariesSet);    // Norte
        Assert.Equal(2, dry.SupportsSet);     // Sur A y Sur B
        Assert.Equal(new DateOnly(2026, 2, 1), dry.ValidFrom);
        Assert.Equal(new[] { "ADVANCE SOLUTIONS GROUP LLC", "Rep Norte Sin Usuario" }, dry.RepsWithoutUser.Select(r => r.RepName).ToArray());
        Assert.Equal(new[] { "Rep Sur A", "Rep Sur B" }, dry.RoutesWithSeveralReps.Select(r => r.RepName).OrderBy(n => n).ToArray());
        Assert.Empty(await f.Db.Set<SalesRouteAssignment>().AsNoTracking().ToListAsync());   // la simulación no escribe
        Assert.Null((await f.Db.Clients.AsNoTracking().SingleAsync(x => x.ClientId == c1.ClientId)).SalesRouteId);

        var run = await svc.RoutesFromRepsAsync(false, default);
        Assert.False(run.DryRun);
        Assert.Equal((3, 1, 2), (run.ClientsRouted, run.PrimariesSet, run.SupportsSet));
        var clients = await f.Db.Clients.AsNoTracking().Where(x => x.TenantId == WmsFixture.TenantId).ToDictionaryAsync(x => x.ClientId, x => x.SalesRouteId);
        Assert.Equal(norte.SalesRouteId, clients[c1.ClientId]);
        Assert.Equal(sur.SalesRouteId, clients[c2.ClientId]);
        Assert.Equal(este.SalesRouteId, clients[c3.ClientId]);
        Assert.Equal(cadenas.SalesRouteId, clients[c4.ClientId]);
        var norteRows = await RowsAsync(f, norte.SalesRouteId);
        var primary = Assert.Single(norteRows);
        Assert.True(primary.IsPrimary);
        Assert.Equal(1001, primary.UserId);
        Assert.Equal(new DateOnly(2026, 2, 1), primary.ValidFrom);
        var surRows = await RowsAsync(f, sur.SalesRouteId);
        Assert.Equal(new[] { 1002, 1003 }, surRows.Select(a => a.UserId).OrderBy(x => x).ToArray());
        Assert.All(surRows, a => Assert.False(a.IsPrimary));
        Assert.Empty(await RowsAsync(f, este.SalesRouteId));
        Assert.Empty(await RowsAsync(f, cadenas.SalesRouteId));

        var second = await svc.RoutesFromRepsAsync(false, default);
        Assert.Equal((0, 0, 0), (second.ClientsRouted, second.PrimariesSet, second.SupportsSet));
        Assert.Equal(3, await f.Db.Set<SalesRouteAssignment>().AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task RoutesFromReps_sin_datos_empieza_hoy_y_no_traslapa_un_titular_cerrado()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(1101, "Anterior");
        await f.AddActiveMemberAsync(1102, "Nuevo");
        var norte = await f.AddRouteAsync("Norte");
        await f.AddAssignmentAsync(norte.SalesRouteId, 1101, true, D1, D15);
        var rep = await f.AddRepAsync("Rep Norte", norte.SalesRouteId, 1102);
        var svc = Svc(f);

        Assert.Equal(Today, (await svc.RoutesFromRepsAsync(true, default)).ValidFrom);   // sin historial, facturas ni cuotas: hoy

        f.Db.Set<SalesQuota>().Add(new SalesQuota { TenantId = WmsFixture.TenantId, SalesRepId = rep.SalesRepId, Month = new DateOnly(2026, 9, 1), Amount = 50m });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var r = await svc.RoutesFromRepsAsync(false, default);
        Assert.Equal(new DateOnly(2026, 9, 1), r.ValidFrom);
        Assert.Equal(1, r.PrimariesSet);
        var open = (await RowsAsync(f, norte.SalesRouteId)).Single(a => a.ValidTo == null);
        Assert.Equal(1102, open.UserId);
        Assert.Equal(D15, open.ValidFrom);   // no empieza antes del cierre del titular anterior
    }
}
