using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 39 (B) — filtros nuevos de GET /api/v1/orders: serviceTypes, packageTypes (alguna línea ACTIVA de ese tipo), cities (sin
/// mayúsculas ni tildes), postalCodePrefix, clientIds (unión con clientId) y statuses (unión con status); combinaciones (AND
/// entre filtros), total y paginación, alcance de datos del usuario y filtro de tenant, y 400/404 de valores desconocidos.
/// Reglas puras (OrderListFilterRules) y el servicio real (OrderReadService) sobre InMemory (OrderReadFixture).
/// </summary>
public sealed class OrderListFilterTests
{
    // ================================================================ reglas puras

    [Fact]
    public void Codes_are_trimmed_uppercased_deduplicated_and_blanks_ignored()
    {
        Assert.Equal(new[] { "STANDARD", "EXPRESS" }, OrderListFilterRules.NormalizeCodes(new[] { " standard ", "", null, "EXPRESS", "Standard" }));
        Assert.Empty(OrderListFilterRules.NormalizeCodes(null));
        Assert.Equal(new[] { "Bayamón", "Caguas" }, OrderListFilterRules.NormalizeTexts(new[] { " Bayamón ", "bayamón", "  ", "Caguas" }));
        Assert.Null(OrderListFilterRules.NormalizePostalPrefix("   "));
        Assert.Equal("009", OrderListFilterRules.NormalizePostalPrefix(" 009 "));
    }

    [Fact]
    public void Cities_match_without_case_accents_or_extra_spaces()
    {
        var stored = new[] { "Bayamón", "bayamon", "BAYAMÓN ", "San  Juan", "Caguas", "" };
        Assert.Equal(new[] { "Bayamón", "bayamon", "BAYAMÓN " }, OrderListFilterRules.MatchingCities(stored, new[] { "Bayamon" }));
        Assert.Equal(new[] { "San  Juan", "Caguas" }, OrderListFilterRules.MatchingCities(stored, new[] { "san juan", "CAGUAS" }));
        Assert.Empty(OrderListFilterRules.MatchingCities(stored, new[] { "Ponce" }));
        Assert.True(OrderListFilterRules.MatchesPostalPrefix("00961", "009"));
        Assert.False(OrderListFilterRules.MatchesPostalPrefix(null, "009"));
    }

    [Fact]
    public void Unknown_code_messages_are_the_existing_ones()
    {
        Assert.Equal("Tipo de servicio desconocido: XX.", OrderListFilterRules.UnknownServiceType("XX"));
        Assert.Equal("Tipo de paquete desconocido: YY.", OrderListFilterRules.UnknownPackageType("YY"));
        Assert.Equal("Estatus desconocido: ZZ.", OrderListFilterRules.UnknownStatus("ZZ"));
    }

    // ================================================================ servicio (InMemory)

    private static OrderListQuery Q(
        IReadOnlyList<string>? serviceTypes = null, IReadOnlyList<string>? packageTypes = null, IReadOnlyList<string>? cities = null,
        string? zip = null, IReadOnlyList<Guid>? clientIds = null, IReadOnlyList<string>? statuses = null,
        Guid? clientId = null, string? status = null, int skip = 0, int take = 100)
        => new(clientId, status, null, null, null, null, null, null, null, false, skip, take,
            ServiceTypes: serviceTypes, PackageTypes: packageTypes, Cities: cities, PostalCodePrefix: zip, ClientPublicIds: clientIds, Statuses: statuses);

    private static async Task<int[]> IdsAsync(OrderReadFixture f, OrderListQuery q)
    {
        var page = await f.Reader.GetListAsync(q, OrderScope.Any, default);
        Assert.Equal(page.Items.Count, page.Total <= q.Take ? page.Total : q.Take);
        return page.Items.Select(i => i.Id).OrderBy(i => i).ToArray();
    }

    [Fact]
    public async Task Without_new_filters_lists_only_the_tenant_orders()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        Assert.Equal(new[] { 1, 2, 3, 4 }, await IdsAsync(f, Q()));
    }

    [Fact]
    public async Task Service_types_any_of_case_insensitive()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        Assert.Equal(new[] { 2, 4 }, await IdsAsync(f, Q(serviceTypes: new[] { "EXPRESS" })));
        Assert.Equal(new[] { 1, 2, 3, 4 }, await IdsAsync(f, Q(serviceTypes: new[] { "standard", " express " })));
        Assert.Empty(await IdsAsync(f, Q(serviceTypes: new[] { "NEXT_DAY" })));   // existe en la lista, sin órdenes
    }

    [Fact]
    public async Task Package_types_match_any_active_line()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        Assert.Equal(new[] { 3 }, await IdsAsync(f, Q(packageTypes: new[] { "PALLET" })));   // la PALLET inactiva de O2 no cuenta
        Assert.Equal(new[] { 1, 4 }, await IdsAsync(f, Q(packageTypes: new[] { "BOX" })));
        Assert.Equal(new[] { 1, 2, 4 }, await IdsAsync(f, Q(packageTypes: new[] { "box", "ENVELOPE" })));
    }

    [Fact]
    public async Task Cities_ignore_case_and_accents()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        Assert.Equal(new[] { 1, 3 }, await IdsAsync(f, Q(cities: new[] { "BAYAMON" })));          // 'Bayamón' y 'bayamon'
        Assert.Equal(new[] { 1, 3, 4 }, await IdsAsync(f, Q(cities: new[] { "bayamón", "Caguas" })));
        Assert.Empty(await IdsAsync(f, Q(cities: new[] { "Ponce" })));
    }

    [Fact]
    public async Task Postal_code_prefix_filters_the_consignee_zip()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        Assert.Equal(new[] { 1, 2, 3 }, await IdsAsync(f, Q(zip: "009")));
        Assert.Equal(new[] { 4 }, await IdsAsync(f, Q(zip: " 0072 ")));
        Assert.Equal(new[] { 2 }, await IdsAsync(f, Q(zip: "00901")));
        Assert.Empty(await IdsAsync(f, Q(zip: "007319")));
    }

    [Fact]
    public async Task Client_ids_join_the_single_client_id()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        Assert.Equal(new[] { 3, 4 }, await IdsAsync(f, Q(clientIds: new[] { f.C2.PublicId, f.C3.PublicId })));
        Assert.Equal(new[] { 1, 2, 4 }, await IdsAsync(f, Q(clientId: f.C1.PublicId, clientIds: new[] { f.C3.PublicId })));
        Assert.Equal(new[] { 1, 2 }, await IdsAsync(f, Q(clientId: f.C1.PublicId, clientIds: new[] { f.C1.PublicId })));
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => f.Reader.GetListAsync(Q(clientIds: new[] { Guid.NewGuid() }), OrderScope.Any, default));
        Assert.Equal("Cliente no encontrado.", ex.Message);
    }

    [Fact]
    public async Task Statuses_join_the_single_status()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        Assert.Equal(new[] { 2, 3 }, await IdsAsync(f, Q(statuses: new[] { "confirmed" })));
        Assert.Equal(new[] { 1, 4 }, await IdsAsync(f, Q(status: "DRAFT", statuses: new[] { OrderStatuses.Cancelled })));
        Assert.Equal(new[] { 1 }, await IdsAsync(f, Q(status: "draft", statuses: new[] { "DRAFT" })));
    }

    [Fact]
    public async Task Filters_combine_with_and_and_keep_total_and_paging()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        Assert.Equal(new[] { 3 }, await IdsAsync(f, Q(serviceTypes: new[] { "STANDARD" }, cities: new[] { "bayamon" }, statuses: new[] { "CONFIRMED" })));
        Assert.Equal(new[] { 2 }, await IdsAsync(f, Q(zip: "009", packageTypes: new[] { "ENVELOPE" }, clientIds: new[] { f.C1.PublicId })));

        // Total = todo lo que cumple el filtro; la página trae solo 'take'.
        var page = await f.Reader.GetListAsync(Q(cities: new[] { "Bayamon", "Caguas" }, take: 2), OrderScope.Any, default);
        Assert.Equal(3, page.Total);
        Assert.Equal(new[] { 4, 3 }, page.Items.Select(i => i.Id).ToArray());   // más reciente primero
        var next = await f.Reader.GetListAsync(Q(cities: new[] { "Bayamon", "Caguas" }, skip: 2, take: 2), OrderScope.Any, default);
        Assert.Equal(3, next.Total);
        Assert.Equal(new[] { 1 }, next.Items.Select(i => i.Id).ToArray());
    }

    [Theory]
    [InlineData("serviceTypes", "Tipo de servicio desconocido: XX.")]
    [InlineData("packageTypes", "Tipo de paquete desconocido: YY.")]
    [InlineData("statuses", "Estatus desconocido: ZZ.")]
    public async Task Unknown_codes_are_400_with_the_field(string field, string message)
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var q = field switch
        {
            "serviceTypes" => Q(serviceTypes: new[] { "STANDARD", "xx" }),
            "packageTypes" => Q(packageTypes: new[] { "yy" }),
            _ => Q(statuses: new[] { "CONFIRMED", "zz" }),
        };
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Reader.GetListAsync(q, OrderScope.Any, default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(message, ex.Message);
        Assert.Equal(new[] { message }, ex.Errors![field]);
    }

    [Fact]
    public async Task The_legacy_status_keeps_its_message_and_field()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Reader.GetListAsync(Q(status: " Nada "), OrderScope.Any, default));
        Assert.Equal("Estatus desconocido: Nada.", ex.Message);
        Assert.True(ex.Errors!.ContainsKey("status"));
    }

    [Fact]
    public async Task User_data_scope_by_client_still_applies_to_every_filter()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        f.Tenant.ScopedClientIds = new[] { f.C1.ClientId };
        Assert.Equal(new[] { 1, 2 }, await IdsAsync(f, Q()));
        Assert.Equal(new[] { 1 }, await IdsAsync(f, Q(cities: new[] { "bayamon" })));          // O3 (Cliente Dos) queda fuera
        Assert.Equal(new[] { 1, 2 }, await IdsAsync(f, Q(zip: "009")));
        Assert.Empty(await IdsAsync(f, Q(statuses: new[] { "CANCELLED" })));
        // Un cliente fuera de su alcance no se revela: 404 'Cliente'.
        await Assert.ThrowsAsync<NotFoundException>(() => f.Reader.GetListAsync(Q(clientIds: new[] { f.C2.PublicId }), OrderScope.Any, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Reader.GetListAsync(Q(clientIds: new[] { f.C1.PublicId, f.C3.PublicId }), OrderScope.Any, default));
    }

    [Fact]
    public async Task Another_tenant_orders_and_clients_never_appear()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        // La orden 5 (tenant 2) está en Bayamón 00961, STANDARD, CONFIRMED, BOX: ningún filtro la trae.
        Assert.DoesNotContain(5, await IdsAsync(f, Q(cities: new[] { "Bayamón" }, zip: "00961", serviceTypes: new[] { "STANDARD" }, packageTypes: new[] { "BOX" })));
        var foreignClient = f.Db.Clients.IgnoreQueryFiltersAsList().Single(c => c.TenantId == 2).PublicId;
        await Assert.ThrowsAsync<NotFoundException>(() => f.Reader.GetListAsync(Q(clientIds: new[] { foreignClient }), OrderScope.Any, default));
    }

    [Fact]
    public async Task Order_scope_of_a_client_rejects_other_clients_without_revealing_them()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var scope = new OrderScope(f.C1.ClientId);
        var page = await f.Reader.GetListAsync(Q(cities: new[] { "bayamon" }), scope, default);
        Assert.Equal(new[] { 1 }, page.Items.Select(i => i.Id).ToArray());
        await Assert.ThrowsAsync<NotFoundException>(() => f.Reader.GetListAsync(Q(clientIds: new[] { f.C1.PublicId, f.C2.PublicId }), scope, default));
    }

    [Fact]
    public void Order_list_item_already_has_what_the_mockup_list_needs()
    {
        // Columnas de la maqueta (Órdenes): Empaque, N.º de orden, fecha, consignatario, pueblo, ZIP, servicio, paquete (una fila por
        // tipo con descripciones), piezas, COD y estatus; más la factura del cliente. Todo existe en OrderListItemDto.
        var props = typeof(OrderListItemDto).GetProperties().Select(p => p.Name).ToHashSet();
        foreach (var name in new[] { "PackBatchNumber", "OrderNumber", "ClientInvoiceNumber", "CreatedAtUtc", "ConsigneeName", "ConsigneeCity",
                     "ConsigneePostalCode", "ServiceType", "ServiceTypeLabel", "Packages", "TotalPieces", "CodAmount", "Status", "StatusLabel", "ClientName" })
            Assert.Contains(name, props);
    }
}

internal static class OrderTestQueryExtensions
{
    /// <summary>Lee las filas sin el filtro global (para tomar datos de otro tenant en una prueba).</summary>
    public static List<T> IgnoreQueryFiltersAsList<T>(this Microsoft.EntityFrameworkCore.DbSet<T> set) where T : class
        => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(set).ToList();
}
