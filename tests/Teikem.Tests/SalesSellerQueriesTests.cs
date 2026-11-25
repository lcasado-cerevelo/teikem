using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;
using K = Teikem.Domain.Sales.SalesRouteCodes;
using R = Teikem.Domain.Sales.SalesRouteRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — consultas del equipo de venta y escrituras compartidas de las personas de la ruta, en la base en memoria con dos compañías
/// (datos INVENTADOS): «persona activa» (usuario activo + membresía ACTIVE, sin portal ni otra compañía), el equipo de un cliente (ruta directa vs. heredada, propuesto,
/// preferido fuera de la ruta), quiénes reciben los avisos, las personas de un día sin anuladas, la línea de tiempo de titulares, el vendedor de una orden y el
/// titular automático (no hace nada si ya hay titular o la ruta está inactiva).
/// </summary>
public sealed class SalesSellerQueriesTests
{
    private const int T = WmsFixture.TenantId;
    private const int Other = WmsFixture.OtherTenantId;
    private static readonly DateOnly D1 = new(2026, 10, 1);
    private static readonly DateOnly D15 = new(2026, 10, 15);
    private static readonly DateTime Now = new(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ActiveMemberIds_excluye_inactivo_suspendido_invitado_portal_y_otra_compania()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(101, "Ana Activa");
        await f.AddMemberAsync(102, "Beto Inactivo", MembershipStatuses.Active, userActive: false);
        await f.AddSuspendedMemberAsync(103, "Carla Suspendida");
        await f.AddMemberAsync(104, "Dino Invitado", MembershipStatuses.Invited, userActive: true);
        await f.AddPortalUserAsync(105, "Eva Portal");
        await f.AddActiveMemberAsync(106, "Fito Otra", tenantId: Other);

        var ids = await SalesSellerQueries.ActiveMemberIdsAsync(f.Db, T, new[] { 101, 102, 103, 104, 105, 106, 999 }, default);
        Assert.Equal(new[] { 101 }, ids.OrderBy(x => x).ToArray());
        Assert.Empty(await SalesSellerQueries.ActiveMemberIdsAsync(f.Db, T, Array.Empty<int>(), default));

        // la de la otra compañía es activa allá
        using (f.AsTenant(Other))
            Assert.Equal(new[] { 106 }, (await SalesSellerQueries.ActiveMemberIdsAsync(f.Db, Other, new[] { 101, 106 }, default)).ToArray());
    }

    [Fact]
    public async Task Team_con_ruta_directa_titular_apoyos_preferido_y_propuesto()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(201, "Titular Norte");
        await f.AddActiveMemberAsync(202, "Apoyo Norte");
        await f.AddSuspendedMemberAsync(203, "Apoyo Suspendido");
        await f.AddActiveMemberAsync(204, "Oficina");
        var norte = await f.AddRouteAsync("Norte");
        await f.AddAssignmentAsync(norte.SalesRouteId, 201, true, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 202, false, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 203, false, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 204, false, D1, D15);   // cerrada: ya no es de la ruta
        var c = await f.AddClientAsync("C1");
        await f.SetClientSalesAsync(c.ClientId, routeId: norte.SalesRouteId, preferredUserId: 202);

        var team = (await SalesSellerQueries.TeamAsync(f.Db, T, c.ClientId, 204, default))!;
        Assert.Equal(norte.SalesRouteId, team.RouteId);
        Assert.Equal("Norte", team.RouteName);
        Assert.False(team.RouteFromRep);
        Assert.Equal(norte.SalesRouteId, team.DirectRouteId);
        Assert.Equal(201, team.Primary!.UserId);
        Assert.True(team.Primary.IsActive);
        Assert.Equal("Titular Norte", team.Primary.Name);
        Assert.Equal(new[] { 202, 203 }, team.Supports.Select(s => s.UserId).OrderBy(x => x).ToArray());
        Assert.False(team.Supports.Single(s => s.UserId == 203).IsActive);
        Assert.Equal(202, team.Preferred!.UserId);
        Assert.False(team.PreferredOutOfRoute);
        Assert.Equal(202, team.ProposedUserId);
        Assert.Equal(K.SellerKindPreferred, team.ProposedKind);
        Assert.Equal(new[] { 201, 202 }, team.Candidates.ActiveRoutePeople.OrderBy(x => x).ToArray());   // sin la suspendida ni la cerrada
        Assert.False(R.IsAllowedSellerChoice(204, team.Candidates));   // la oficina no es de la ruta
    }

    [Fact]
    public async Task Team_hereda_la_ruta_del_representante_y_propone_por_la_cadena()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddSuspendedMemberAsync(301, "Titular Suspendido");
        await f.AddActiveMemberAsync(302, "Apoyo Metro");
        await f.AddActiveMemberAsync(303, "Oficina");
        var metro = await f.AddRouteAsync("Metro");
        var rep = await f.AddRepAsync("Rep Metro", metro.SalesRouteId, null);
        await f.AddAssignmentAsync(metro.SalesRouteId, 301, true, D1);
        await f.AddAssignmentAsync(metro.SalesRouteId, 302, false, D1);
        var c = await f.AddClientAsync("C1");
        await f.SetClientSalesAsync(c.ClientId, repId: rep.SalesRepId);

        var porApoyo = (await SalesSellerQueries.TeamAsync(f.Db, T, c.ClientId, 302, default))!;
        Assert.Equal(metro.SalesRouteId, porApoyo.RouteId);
        Assert.True(porApoyo.RouteFromRep);
        Assert.Null(porApoyo.DirectRouteId);
        Assert.False(porApoyo.Primary!.IsActive);
        Assert.Equal(302, porApoyo.ProposedUserId);   // titular suspendido → quien crea, porque es apoyo vigente
        Assert.Equal(K.SellerKindSupport, porApoyo.ProposedKind);

        var porOficina = (await SalesSellerQueries.TeamAsync(f.Db, T, c.ClientId, 303, default))!;
        Assert.Null(porOficina.ProposedUserId);   // la oficina no queda como vendedora
        Assert.Null(porOficina.ProposedKind);
    }

    [Fact]
    public async Task Team_senala_el_preferido_que_ya_no_es_de_la_ruta_sin_dejar_de_proponerlo_y_sin_ruta_no_hay_equipo()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(401, "Titular");
        await f.AddActiveMemberAsync(402, "Ex de la ruta");
        var norte = await f.AddRouteAsync("Norte");
        await f.AddAssignmentAsync(norte.SalesRouteId, 401, true, D1);
        var c = await f.AddClientAsync("C1");
        await f.SetClientSalesAsync(c.ClientId, routeId: norte.SalesRouteId, preferredUserId: 402);

        var team = (await SalesSellerQueries.TeamAsync(f.Db, T, c.ClientId, null, default))!;
        Assert.True(team.PreferredOutOfRoute);
        Assert.Equal(402, team.ProposedUserId);   // no bloquea: el preferido activo se sigue proponiendo

        var sinRuta = await f.AddClientAsync("C2");
        var vacio = (await SalesSellerQueries.TeamAsync(f.Db, T, sinRuta.ClientId, 401, default))!;
        Assert.Null(vacio.RouteId);
        Assert.Null(vacio.RouteName);
        Assert.Null(vacio.Primary);
        Assert.Empty(vacio.Supports);
        Assert.Null(vacio.ProposedUserId);

        Assert.Null(await SalesSellerQueries.TeamAsync(f.Db, T, 987654, null, default));
        var ajeno = await f.AddClientAsync("X1", Other);
        Assert.Null(await SalesSellerQueries.TeamAsync(f.Db, T, ajeno.ClientId, null, default));
    }

    [Fact]
    public async Task NoticeUsers_preferido_titular_y_apoyos_distintos_y_sin_inactivos()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(501, "Titular");
        await f.AddActiveMemberAsync(502, "Apoyo");
        await f.AddSuspendedMemberAsync(503, "Apoyo Suspendido");
        await f.AddActiveMemberAsync(504, "Preferido de fuera");
        var norte = await f.AddRouteAsync("Norte");
        await f.AddAssignmentAsync(norte.SalesRouteId, 501, true, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 502, false, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 503, false, D1);
        var c = await f.AddClientAsync("C1");
        await f.SetClientSalesAsync(c.ClientId, routeId: norte.SalesRouteId, preferredUserId: 504);
        var c2 = await f.AddClientAsync("C2");
        await f.SetClientSalesAsync(c2.ClientId, routeId: norte.SalesRouteId, preferredUserId: 501);   // el preferido es el titular: una sola vez

        Assert.Equal(new[] { 501, 502, 504 }, (await SalesSellerQueries.NoticeUsersAsync(f.Db, T, c.ClientId, default)).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { 501, 502 }, (await SalesSellerQueries.NoticeUsersAsync(f.Db, T, c2.ClientId, default)).OrderBy(x => x).ToArray());
        Assert.Empty(await SalesSellerQueries.NoticeUsersAsync(f.Db, T, 987654, default));
    }

    [Fact]
    public async Task PeopleOn_excluye_las_anuladas_y_respeta_el_dia_y_la_linea_de_tiempo_de_titulares()
    {
        await using var f = await WmsFixture.CreateAsync();
        var norte = await f.AddRouteAsync("Norte");
        var ajena = await f.AddRouteAsync("Ajena", Other);
        await f.AddAssignmentAsync(norte.SalesRouteId, 601, true, D1, D15);
        await f.AddAssignmentAsync(norte.SalesRouteId, 602, true, D15);
        await f.AddAssignmentAsync(norte.SalesRouteId, 603, false, D1, isActive: false);   // anulada
        await f.AddAssignmentAsync(norte.SalesRouteId, 604, false, D1);
        await f.AddAssignmentAsync(ajena.SalesRouteId, 605, true, D1, tenantId: Other);

        var d10 = await SalesSellerQueries.PeopleOnAsync(f.Db, T, new[] { norte.SalesRouteId, ajena.SalesRouteId }, new DateOnly(2026, 10, 10), default);
        Assert.Equal(new[] { 601, 604 }, d10.Select(p => p.UserId).ToArray());   // titular primero
        Assert.True(d10[0].IsPrimary);
        var d15 = await SalesSellerQueries.PeopleOnAsync(f.Db, T, new[] { norte.SalesRouteId }, D15, default);
        Assert.Equal(new[] { 602, 604 }, d15.Select(p => p.UserId).ToArray());
        Assert.Empty(await SalesSellerQueries.PeopleOnAsync(f.Db, T, Array.Empty<int>(), D15, default));

        var timeline = await SalesSellerQueries.PrimaryTimelineAsync(f.Db, T, null, default);
        Assert.Equal(601, timeline.On(norte.SalesRouteId, new DateOnly(2026, 10, 14)));
        Assert.Equal(602, timeline.On(norte.SalesRouteId, D15));
        Assert.Null(timeline.On(norte.SalesRouteId, new DateOnly(2026, 9, 30)));
        Assert.Null(timeline.On(ajena.SalesRouteId, D15));   // de otra compañía
        Assert.Empty((await SalesSellerQueries.PrimaryTimelineAsync(f.Db, T, Array.Empty<int>(), default)).Spans);
    }

    [Fact]
    public async Task OrderSeller_devuelve_el_vendedor_guardado_si_es_activo_y_su_nombre()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(701, "Vendedora Activa");
        await f.AddSuspendedMemberAsync(702, "Vendedor Suspendido");
        var c = await f.AddClientAsync("C1");
        var w = await f.AddWarehouseAsync("W1");
        var copyAt = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        var o1 = await OrderAsync(f, c.ClientId, w.WarehouseId, 701, copyAt);
        var o2 = await OrderAsync(f, c.ClientId, w.WarehouseId, 702, copyAt);
        var o3 = await OrderAsync(f, c.ClientId, w.WarehouseId, null, null);

        var s1 = (await SalesSellerQueries.OrderSellerAsync(f.Db, T, o1, default))!;
        Assert.Equal(701, s1.SellerUserId);
        Assert.True(s1.IsActiveMember);
        Assert.Equal("Vendedora Activa", s1.Name);
        Assert.Equal(copyAt, s1.SalesCopyAtUtc);
        Assert.False((await SalesSellerQueries.OrderSellerAsync(f.Db, T, o2, default))!.IsActiveMember);
        var s3 = (await SalesSellerQueries.OrderSellerAsync(f.Db, T, o3, default))!;
        Assert.Null(s3.SellerUserId);
        Assert.Null(s3.SalesCopyAtUtc);
        Assert.Null(await SalesSellerQueries.OrderSellerAsync(f.Db, T, 987654, default));
    }

    private static async Task<int> OrderAsync(WmsFixture f, int clientId, int warehouseId, int? seller, DateTime? copyAt)
    {
        var o = new SalesOrder
        {
            PublicId = Guid.NewGuid(), TenantId = T, Number = "OV-" + Guid.NewGuid().ToString("N")[..6], WarehouseId = warehouseId, ClientId = clientId,
            RequestedDate = D1, StatusCodeId = 1, CreatedAtUtc = Now, SellerUserId = seller, SalesCopyAtUtc = copyAt,
        };
        f.Db.SalesOrders.Add(o);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return o.SalesOrderId;
    }

    // ---------------------------------------------------------------- escrituras compartidas

    [Fact]
    public async Task OpenPrimaryIfVacant_no_hace_nada_si_ya_hay_titular_o_la_ruta_esta_inactiva()
    {
        await using var f = await WmsFixture.CreateAsync();
        var conTitular = await f.AddRouteAsync("Con titular");
        var inactiva = await f.AddRouteAsync("Inactiva", isActive: false);
        var vacante = await f.AddRouteAsync("Vacante");
        await f.AddAssignmentAsync(conTitular.SalesRouteId, 801, true, D1);
        await f.AddAssignmentAsync(vacante.SalesRouteId, 803, false, D1);   // apoyo que pasa a titular

        Assert.False(await SalesRouteAssignmentOps.OpenPrimaryIfVacantAsync(f.Db, T, conTitular.SalesRouteId, 802, D15, 1, Now, default));
        Assert.False(await SalesRouteAssignmentOps.OpenPrimaryIfVacantAsync(f.Db, T, inactiva.SalesRouteId, 802, D15, 1, Now, default));
        Assert.True(await SalesRouteAssignmentOps.OpenPrimaryIfVacantAsync(f.Db, T, vacante.SalesRouteId, 803, D15, 1, Now, default));
        f.Db.ChangeTracker.Clear();

        var rows = await f.Db.Set<SalesRouteAssignment>().AsNoTracking().OrderBy(a => a.SalesRouteAssignmentId).ToListAsync();
        Assert.Single(rows, a => a.SalesRouteId == conTitular.SalesRouteId);
        Assert.DoesNotContain(rows, a => a.SalesRouteId == inactiva.SalesRouteId);
        var v = rows.Where(a => a.SalesRouteId == vacante.SalesRouteId).ToList();
        Assert.Equal(2, v.Count);
        Assert.Equal(D15, v[0].ValidTo);   // su fila de apoyo se cerró
        Assert.False(v[0].IsPrimary);
        Assert.True(v[1].IsPrimary);
        Assert.Null(v[1].ValidTo);
        Assert.Equal(D15, v[1].ValidFrom);
        Assert.Equal(1, v[1].CreatedBy);
    }

    [Fact]
    public async Task Ops_titular_nuevo_cierra_el_anterior_y_los_choques_dan_409()
    {
        await using var f = await WmsFixture.CreateAsync();
        var norte = await f.AddRouteAsync("Norte");
        var first = await SalesRouteAssignmentOps.OpenPrimaryAsync(f.Db, T, norte.SalesRouteId, 901, D1, 1, Now, default);
        await SalesRouteAssignmentOps.OpenPrimaryAsync(f.Db, T, norte.SalesRouteId, 902, D15, 1, Now, default);
        f.Db.ChangeTracker.Clear();
        var old = await f.Db.Set<SalesRouteAssignment>().AsNoTracking().SingleAsync(a => a.SalesRouteAssignmentId == first.SalesRouteAssignmentId);
        Assert.Equal(D15, old.ValidTo);
        Assert.Equal(Now, old.ClosedAtUtc);

        var again = await Assert.ThrowsAsync<ConflictException>(() => SalesRouteAssignmentOps.OpenPrimaryAsync(f.Db, T, norte.SalesRouteId, 902, D15, 1, Now, default));
        Assert.Equal(R.AlreadyPrimary, again.Message);
        var asSupport = await Assert.ThrowsAsync<ConflictException>(() => SalesRouteAssignmentOps.OpenSupportAsync(f.Db, T, norte.SalesRouteId, 902, D15, 1, Now, default));
        Assert.Equal(R.PrimaryCannotBeSupport, asSupport.Message);
        await SalesRouteAssignmentOps.OpenSupportAsync(f.Db, T, norte.SalesRouteId, 903, D15, 1, Now, default);
        var twice = await Assert.ThrowsAsync<ConflictException>(() => SalesRouteAssignmentOps.OpenSupportAsync(f.Db, T, norte.SalesRouteId, 903, D15, 1, Now, default));
        Assert.Equal(R.AlreadySupport, twice.Message);
        var before = await Assert.ThrowsAsync<ValidationException>(() => SalesRouteAssignmentOps.OpenPrimaryAsync(f.Db, T, norte.SalesRouteId, 904, D1, 1, Now, default));
        Assert.Equal(R.ValidFromBeforePrimary(D15), before.Message);
    }

    [Fact]
    public async Task Ops_cerrar_y_anular_validan_el_estado_y_las_fechas()
    {
        await using var f = await WmsFixture.CreateAsync();
        var norte = await f.AddRouteAsync("Norte");
        var today = new DateOnly(2026, 11, 25);
        var a = await f.AddAssignmentAsync(norte.SalesRouteId, 951, false, D15);
        var b = await f.AddAssignmentAsync(norte.SalesRouteId, 952, false, D15);

        var early = await Assert.ThrowsAsync<ValidationException>(() => SalesRouteAssignmentOps.CloseAsync(f.Db, T, a.SalesRouteAssignmentId, D1, today, 1, Now, default));
        Assert.Equal(R.ValidToBeforeFrom, early.Message);
        var future = await Assert.ThrowsAsync<ValidationException>(() => SalesRouteAssignmentOps.CloseAsync(f.Db, T, a.SalesRouteAssignmentId, today.AddDays(1), today, 1, Now, default));
        Assert.Equal(R.ValidToFuture, future.Message);
        await SalesRouteAssignmentOps.CloseAsync(f.Db, T, a.SalesRouteAssignmentId, today, today, 1, Now, default);
        var closed = await Assert.ThrowsAsync<ConflictException>(() => SalesRouteAssignmentOps.CloseAsync(f.Db, T, a.SalesRouteAssignmentId, today, today, 1, Now, default));
        Assert.Equal(R.AssignmentClosed, closed.Message);

        var voided = await SalesRouteAssignmentOps.VoidAsync(f.Db, T, b.SalesRouteAssignmentId, 1, Now, default);
        Assert.False(voided.IsActive);
        Assert.Equal(D15, voided.ValidTo);
        Assert.Equal(1, voided.VoidedBy);
        var twice = await Assert.ThrowsAsync<ConflictException>(() => SalesRouteAssignmentOps.VoidAsync(f.Db, T, b.SalesRouteAssignmentId, 1, Now, default));
        Assert.Equal(R.AssignmentVoided, twice.Message);
        var closeVoided = await Assert.ThrowsAsync<ConflictException>(() => SalesRouteAssignmentOps.CloseAsync(f.Db, T, b.SalesRouteAssignmentId, today, today, 1, Now, default));
        Assert.Equal(R.AssignmentVoided, closeVoided.Message);

        var other = await f.AddAssignmentAsync(norte.SalesRouteId, 953, false, D15, tenantId: Other);
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => SalesRouteAssignmentOps.VoidAsync(f.Db, T, other.SalesRouteAssignmentId, 1, Now, default));
        Assert.Equal($"Asignación '{other.SalesRouteAssignmentId}' no encontrada.", nf.Message);
    }
}
