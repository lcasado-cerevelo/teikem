using Teikem.Domain.Sales;
using Xunit;
using K = Teikem.Domain.Sales.SalesRouteCodes;
using R = Teikem.Domain.Sales.SalesRouteRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — reglas PURAS de las rutas de venta: vigencias (intervalo medio abierto, anuladas), fechas de inicio y cierre, vendedor propuesto
/// (cada eslabón), elección y congelado, vendedor preferido (ajuste del dueño), atribución de la venta (toda la matriz), normalización a representante, cuota REP/ROUTE
/// y los mensajes exactos que citan el manual y la FAQ.
/// </summary>
public sealed class SalesRouteRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);
    private static readonly IReadOnlySet<int> None = new HashSet<int>();

    // ---------------------------------------------------------------- vigencias

    [Fact]
    public void IsCurrent_usa_el_intervalo_medio_abierto_y_excluye_las_anuladas()
    {
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 15);
        Assert.False(R.IsCurrent(from, to, true, new DateOnly(2026, 10, 31)));
        Assert.True(R.IsCurrent(from, to, true, from));
        Assert.True(R.IsCurrent(from, to, true, new DateOnly(2026, 11, 14)));
        Assert.False(R.IsCurrent(from, to, true, to));   // el día de cierre ya no cuenta
        Assert.True(R.IsCurrent(from, null, true, new DateOnly(2030, 1, 1)));
        Assert.False(R.IsCurrent(from, null, false, new DateOnly(2026, 11, 5)));   // anulada
        Assert.False(R.IsCurrent(from, from, true, from));   // tramo de duración cero
    }

    [Fact]
    public void CheckValidFrom_rechaza_futura_y_anterior_al_titular_actual()
    {
        Assert.Equal(R.ValidFromFuture, R.CheckValidFrom(Today.AddDays(1), Today, null));
        Assert.Equal("La fecha de inicio no puede ser anterior a la del titular actual (2026-11-10).",
            R.CheckValidFrom(new DateOnly(2026, 11, 9), Today, new DateOnly(2026, 11, 10)));
        Assert.Null(R.CheckValidFrom(new DateOnly(2026, 11, 10), Today, new DateOnly(2026, 11, 10)));   // igual = vale
        Assert.Null(R.CheckValidFrom(Today, Today, null));
        Assert.Null(R.CheckValidFrom(new DateOnly(2020, 1, 1), Today, null));
    }

    [Fact]
    public void CheckClose_rechaza_anterior_al_inicio_y_futura()
    {
        var from = new DateOnly(2026, 11, 10);
        Assert.Equal(R.ValidToBeforeFrom, R.CheckClose(from, from.AddDays(-1), Today));
        Assert.Equal(R.ValidToFuture, R.CheckClose(from, Today.AddDays(1), Today));
        Assert.Null(R.CheckClose(from, from, Today));   // igual al inicio = vale
        Assert.Null(R.CheckClose(from, Today, Today));
    }

    [Fact]
    public void PrimaryOn_toma_el_titular_del_dia_y_la_linea_de_tiempo_por_ruta()
    {
        var spans = new[]
        {
            new SalesPrimarySpan(10, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 15)),
            new SalesPrimarySpan(11, new DateOnly(2026, 6, 15), null),
        };
        Assert.Null(R.PrimaryOn(spans, new DateOnly(2025, 12, 31)));
        Assert.Equal(10, R.PrimaryOn(spans, new DateOnly(2026, 6, 14)));
        Assert.Equal(11, R.PrimaryOn(spans, new DateOnly(2026, 6, 15)));
        var timeline = new SalesPrimaryTimeline(new Dictionary<int, IReadOnlyList<SalesPrimarySpan>> { [5] = spans });
        Assert.Equal(11, timeline.On(5, Today));
        Assert.Null(timeline.On(6, Today));
        Assert.Null(SalesPrimaryTimeline.Empty.On(5, Today));
    }

    [Fact]
    public void ParseRole_acepta_PRIMARY_y_SUPPORT()
    {
        Assert.True(R.ParseRole("primary"));
        Assert.False(R.ParseRole(" SUPPORT "));
        Assert.Null(R.ParseRole("OWNER"));
        Assert.Null(R.ParseRole(null));
        Assert.Equal(K.RolePrimary, R.RoleOf(true));
        Assert.Equal(K.RoleSupport, R.RoleOf(false));
    }

    // ---------------------------------------------------------------- vendedor de la orden

    private static SellerCandidates Cands(int? preferred = null, bool prefActive = true, int? primary = null, bool primActive = true, int? requestedBy = null, params int[] people)
        => new(preferred, prefActive, primary, primActive, requestedBy, people.ToHashSet());

    [Fact]
    public void ProposeSeller_recorre_cada_eslabon()
    {
        Assert.Equal(7, R.ProposeSeller(Cands(preferred: 7, primary: 8, requestedBy: 9, people: new[] { 8, 9 })));   // preferido activo
        Assert.Equal(8, R.ProposeSeller(Cands(preferred: 7, prefActive: false, primary: 8, requestedBy: 9, people: new[] { 8, 9 })));   // preferido suspendido → titular
        Assert.Equal(9, R.ProposeSeller(Cands(primary: 8, primActive: false, requestedBy: 9, people: new[] { 9 })));   // titular inactivo → quien crea, si es de la ruta
        Assert.Null(R.ProposeSeller(Cands(primary: 8, primActive: false, requestedBy: 1, people: new[] { 9 })));   // la oficina (fuera de la ruta) → nadie
        Assert.Null(R.ProposeSeller(Cands()));
    }

    [Fact]
    public void SellerKindOf_dice_de_donde_sale_el_vendedor()
    {
        var c = Cands(preferred: 7, primary: 8, people: new[] { 8, 9 });
        Assert.Equal(K.SellerKindPreferred, R.SellerKindOf(7, c));
        Assert.Equal(K.SellerKindPrimary, R.SellerKindOf(8, c));
        Assert.Equal(K.SellerKindSupport, R.SellerKindOf(9, c));
        Assert.Null(R.SellerKindOf(1, c));
        Assert.Null(R.SellerKindOf(null, c));
    }

    [Fact]
    public void IsAllowedSellerChoice_solo_el_preferido_activo_o_una_persona_vigente_de_la_ruta()
    {
        var c = Cands(preferred: 7, primary: 8, people: new[] { 8, 9 });
        Assert.True(R.IsAllowedSellerChoice(7, c));
        Assert.True(R.IsAllowedSellerChoice(8, c));
        Assert.True(R.IsAllowedSellerChoice(9, c));
        Assert.False(R.IsAllowedSellerChoice(1, c));
        Assert.False(R.IsAllowedSellerChoice(7, Cands(preferred: 7, prefActive: false, people: new[] { 8 })));
    }

    [Fact]
    public void FreezeSeller_respeta_la_eleccion_activa_y_si_no_congela_la_propuesta()
    {
        var c = Cands(preferred: 7, primary: 8, people: new[] { 8, 9 });
        Assert.Equal(9, R.FreezeSeller(9, true, c));
        Assert.Equal(7, R.FreezeSeller(9, false, c));   // la elección ya no vale (suspendida o fuera de la ruta) → la propuesta
        Assert.Equal(7, R.FreezeSeller(null, false, c));
        Assert.Null(R.FreezeSeller(null, false, Cands(requestedBy: 1)));
    }

    // ---------------------------------------------------------------- vendedor preferido (ajuste del dueño, 2026-11-25)

    [Fact]
    public void CheckPreferred_exige_ruta_y_persona_activa_de_la_ruta()
    {
        Assert.Equal("El cliente necesita una ruta antes de asignarle un vendedor preferido.", R.CheckPreferred(null, true));
        Assert.Equal("El vendedor preferido debe ser una persona activa de la ruta del cliente.", R.CheckPreferred(3, false));
        Assert.Null(R.CheckPreferred(3, true));
    }

    [Fact]
    public void PreferredOutOfRoute_solo_senala_cuando_ya_no_es_de_la_ruta()
    {
        Assert.False(R.PreferredOutOfRoute(null, None));
        Assert.True(R.PreferredOutOfRoute(7, None));
        Assert.False(R.PreferredOutOfRoute(7, new HashSet<int> { 7 }));
    }

    // ---------------------------------------------------------------- atribución

    private const int Norte = 1, Metro = 2;
    private static readonly DateOnly Day = new(2026, 10, 10);

    private static int? NoPrimary(int route, DateOnly day) => null;

    [Fact]
    public void Attribute_la_copia_con_valor_gana_a_la_ruta_actual()
    {
        var a = R.Attribute(new AttributionInput(Norte, 50, Metro, null, false, null, Day), NoPrimary);
        Assert.Equal(Norte, a.RouteId);
        Assert.Equal(new SalesPersonRef(null, 50), a.Person);
    }

    [Fact]
    public void Attribute_la_copia_vacia_cae_en_la_regla_de_hoy()
    {
        var a = R.Attribute(new AttributionInput(null, null, Metro, null, false, null, Day), (r, _) => r == Metro ? 60 : null);
        Assert.Equal(Metro, a.RouteId);
        Assert.Equal("U60", a.Person!.Key);
    }

    [Fact]
    public void Attribute_sin_ruta_directa_usa_la_del_representante_y_el_representante_va_antes_del_titular()
    {
        var a = R.Attribute(new AttributionInput(null, null, null, 4, true, Norte, Day), (_, _) => 70);
        Assert.Equal(Norte, a.RouteId);
        Assert.Equal(new SalesPersonRef(4, null), a.Person);
        Assert.Equal("R4", a.Person!.Key);

        // con ruta directa igual a la del representante, también va el representante
        var b = R.Attribute(new AttributionInput(null, null, Norte, 4, true, Norte, Day), (_, _) => 70);
        Assert.Equal("R4", b.Person!.Key);
    }

    [Fact]
    public void Attribute_un_representante_de_otra_ruta_o_inactivo_no_se_usa_como_persona()
    {
        var otra = R.Attribute(new AttributionInput(null, null, Metro, 4, true, Norte, Day), (r, _) => r == Metro ? 80 : 81);
        Assert.Equal(Metro, otra.RouteId);
        Assert.Equal("U80", otra.Person!.Key);

        var copiaOtraRuta = R.Attribute(new AttributionInput(Metro, null, null, 4, true, Norte, Day), (r, _) => r == Metro ? 80 : 81);
        Assert.Equal(Metro, copiaOtraRuta.RouteId);
        Assert.Equal("U80", copiaOtraRuta.Person!.Key);

        var inactivo = R.Attribute(new AttributionInput(null, null, null, 4, false, Norte, Day), (_, _) => 90);
        Assert.Equal(Norte, inactivo.RouteId);
        Assert.Equal("U90", inactivo.Person!.Key);
    }

    [Fact]
    public void Attribute_toma_el_titular_por_la_fecha_de_la_linea()
    {
        var timeline = new SalesPrimaryTimeline(new Dictionary<int, IReadOnlyList<SalesPrimarySpan>>
        {
            [Norte] = new[] { new SalesPrimarySpan(10, new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 15)), new SalesPrimarySpan(11, new DateOnly(2026, 10, 15), null) },
        });
        var antes = R.Attribute(new AttributionInput(null, null, Norte, null, false, null, new DateOnly(2026, 10, 14)), timeline.On);
        var despues = R.Attribute(new AttributionInput(null, null, Norte, null, false, null, new DateOnly(2026, 10, 15)), timeline.On);
        Assert.Equal("U10", antes.Person!.Key);
        Assert.Equal("U11", despues.Person!.Key);
    }

    [Fact]
    public void Attribute_sin_nada_da_sin_ruta_y_sin_vendedor()
    {
        var a = R.Attribute(new AttributionInput(null, null, null, null, false, null, Day), (_, _) => 1);
        Assert.Null(a.RouteId);
        Assert.Null(a.Person);

        // un representante sin ruta y un cliente sin ruta: la venta es del representante (paridad con el Lote 241)
        var rep = R.Attribute(new AttributionInput(null, null, null, 4, true, null, Day), (_, _) => 1);
        Assert.Null(rep.RouteId);
        Assert.Equal("R4", rep.Person!.Key);
    }

    [Fact]
    public void NormalizePerson_pasa_el_usuario_que_es_representante_a_ese_representante()
    {
        var map = new Dictionary<int, int> { [50] = 4 };
        Assert.Equal("R4", R.NormalizePerson(new SalesPersonRef(null, 50), map)!.Key);
        Assert.Equal("U51", R.NormalizePerson(new SalesPersonRef(null, 51), map)!.Key);
        Assert.Equal("R9", R.NormalizePerson(new SalesPersonRef(9, null), map)!.Key);
        Assert.Null(R.NormalizePerson(null, map));
        Assert.Equal(string.Empty, new SalesPersonRef(null, null).Key);
        Assert.True(new SalesPersonRef(null, null).IsEmpty);
    }

    // ---------------------------------------------------------------- cuota

    [Fact]
    public void ParseQuotaBy_acepta_REP_y_ROUTE()
    {
        Assert.Equal(K.QuotaByRep, R.ParseQuotaBy(" rep "));
        Assert.Equal(K.QuotaByRoute, R.ParseQuotaBy("ROUTE"));
        Assert.Null(R.ParseQuotaBy("CHAIN"));
        Assert.Null(R.ParseQuotaBy(""));
        Assert.Null(R.ParseQuotaBy(null));
        Assert.True(R.IsValidQuotaBy("route"));
        Assert.False(R.IsValidQuotaBy("x"));
    }

    // ---------------------------------------------------------------- mensajes exactos

    [Fact]
    public void Los_mensajes_exactos_no_cambian()
    {
        Assert.Equal("El papel debe ser PRIMARY (titular) o SUPPORT (apoyo).", R.RoleInvalid);
        Assert.Equal("Escoja el usuario.", R.UserRequired);
        Assert.Equal("El usuario no pertenece a esta compañía.", R.UserNotMember);
        Assert.Equal("El usuario no está activo en esta compañía: actívelo o escoja otro.", R.UserSuspended);
        Assert.Equal("Escoja el usuario del representante: el vendedor siempre es un usuario de Teikem.", R.RepUserRequired);
        Assert.Equal("El usuario ya es el representante «Ana».", R.RepUserTaken("Ana"));
        Assert.Equal("La fecha de inicio no puede ser futura.", R.ValidFromFuture);
        Assert.Equal("La persona ya es titular de esta ruta.", R.AlreadyPrimary);
        Assert.Equal("La persona ya es apoyo de esta ruta.", R.AlreadySupport);
        Assert.Equal("La persona es la titular de esta ruta: cierre su asignación antes de ponerla de apoyo.", R.PrimaryCannotBeSupport);
        Assert.Equal("Asignación", R.AssignmentNotFound);
        Assert.Equal("La asignación ya está cerrada.", R.AssignmentClosed);
        Assert.Equal("La asignación ya está anulada.", R.AssignmentVoided);
        Assert.Equal("La fecha de cierre no puede ser anterior a la de inicio.", R.ValidToBeforeFrom);
        Assert.Equal("La fecha de cierre no puede ser futura.", R.ValidToFuture);
        Assert.Equal("Otra persona cambió las personas de la ruta al mismo tiempo: recargue e intente de nuevo.", R.PeopleChangedConcurrently);
        Assert.Equal("La ruta tiene clientes asignados: páselos a otra ruta antes de desactivarla.", R.RouteHasClients);
        Assert.Equal("La cuota se lleva por ruta (ROUTE) o por persona (REP).", R.QuotaByInvalid);
        Assert.Equal("Cada renglón lleva el representante (repId) o la ruta (routeId), uno de los dos.", R.QuotaTargetInvalid);
        Assert.Equal("El vendedor debe ser el vendedor preferido del cliente o una persona vigente de su ruta.", R.SellerNotAllowed);
        Assert.Equal("El vendedor preferido debe ser una persona activa de la ruta del cliente.", R.PreferredNotInRoute);
        Assert.Equal("El cliente necesita una ruta antes de asignarle un vendedor preferido.", R.PreferredNeedsRoute);
        Assert.Equal("El vendedor preferido ya no pertenece a la ruta del cliente.", R.PreferredOutOfRouteNotice);
        Assert.Equal("Sin vendedor", R.NoSellerName);
        Assert.Equal("Sin ruta", R.NoRouteName);
        Assert.Equal("SALES_ROUTE", K.RouteDataSourceKey);
        Assert.Equal("SALES_ROUTE_ASSIGNMENT", K.AssignmentDataSourceKey);
        Assert.Equal("SALES_ROUTE_NO_PRIMARY", K.NoPrimaryAttentionCode);
    }

    [Fact]
    public void Los_avisos_llevan_el_conteo_y_los_nombres()
    {
        Assert.Equal("1 ruta activa sin titular: Cadenas.", R.RoutesWithoutPrimary(1, new[] { "Cadenas" }));
        Assert.Equal("2 rutas activas sin titular: Norte y Metro.", R.RoutesWithoutPrimary(2, new[] { "Norte", "Metro" }));
        Assert.Equal("1 ruta con el titular inactivo: Sur.", R.RoutesWithInactivePrimary(1, new[] { "Sur" }));
        Assert.StartsWith("3 representantes sin usuario: A, B y C.", R.RepsWithoutUser(3, new[] { "A", "B", "C" }));
        Assert.Equal("1 cliente activo sin ruta: asígnele una ruta.", R.ClientsWithoutRoute(1));
        Assert.Equal("5 clientes activos sin ruta: asígneles una ruta.", R.ClientsWithoutRoute(5));
        Assert.Equal("El usuario «Ana» está en varios representantes activos (Ana Norte y Ana Metro): su venta se cuenta en el primero que se creó.",
            R.UserInSeveralReps("Ana", new[] { "Ana Norte", "Ana Metro" }));
        var many = Enumerable.Range(1, 12).Select(i => "R" + i).ToList();
        Assert.EndsWith("R10 y 2 más", R.NameList(many));
        Assert.Equal("-", R.NameList(Array.Empty<string>()));
    }
}
