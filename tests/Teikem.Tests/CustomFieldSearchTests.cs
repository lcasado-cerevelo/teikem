using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.CustomFields;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 130: campos personalizados con «Mostrar en la búsqueda» → filtro del tipo del campo en la lista de su entidad; y todas las Vistas admiten los campos personalizados como columna y filtro.
public sealed class CustomFieldSearchTests
{
    private static async Task<WmsFixture> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<OrderPriceVisibility>();
            s.AddSingleton<OrderReadService>();
            s.AddSingleton<IDataSource, TransportOrderDataSource>();
            s.AddSingleton<IDataSourceRegistry, DataSourceRegistry>();
            s.AddSingleton<AnalyticsEngine>();
        });
        f.SetModules(ModuleKeys.LtlGround);
        return f;
    }

    private static async Task<int> DefAsync(WmsFixture f, string key, string type, bool showInList)
    {
        var d = new CustomFieldDefinition
        {
            TenantId = WmsFixture.TenantId, EntityTypeLookupId = f.LookupId(LookupDomains.EntityType, EntityTypes.TransportOrder), FieldKey = key,
            DataTypeLookupId = f.LookupId(LookupDomains.CustomFieldDataType, type), LabelJson = $"{{\"es\":\"{key}\"}}", IsActive = true, ShowInList = showInList,
        };
        f.Db.CustomFieldDefinitions.Add(d);
        await f.Db.SaveChangesAsync();
        return d.CustomFieldDefinitionId;
    }

    private static void Value(WmsFixture f, int def, int orderId, string? text = null, decimal? number = null, DateTime? date = null, bool? flag = null)
        => f.Db.CustomFieldValues.Add(new CustomFieldValue { TenantId = WmsFixture.TenantId, CustomFieldDefinitionId = def, EntityId = orderId, ValueText = text, ValueNumber = number, ValueDate = date, ValueBool = flag });

    private static async Task<string[]> NumbersAsync(WmsFixture f, params CustomFieldFilter[] filters)
        => (await f.Get<OrderReadService>().GetListAsync(new OrderListQuery(null, null, null, null, null, null, null, null, null, CustomFields: filters), OrderScope.Any, default))
            .Items.Select(i => i.OrderNumber).OrderBy(n => n).ToArray();

    [Fact]
    public void Query_string_keys_become_filters_with_from_and_to()
    {
        var q = new Dictionary<string, StringValues>
        {
            ["cf.division"] = "GLT", ["cf.peso.from"] = "5", ["cf.peso.to"] = "10", ["CF.Entrega.From"] = "2026-10-01", ["cf.vacio"] = " ", ["take"] = "5", ["cf."] = "x",
        };
        var filters = CustomFieldFilters.FromQuery(q).OrderBy(x => x.Key).ToList();
        Assert.Equal(new[] { "division", "entrega", "peso" }, filters.Select(x => x.Key).ToArray());
        Assert.Equal("GLT", filters[0].Value);
        Assert.Equal(("2026-10-01", (string?)null), (filters[1].From, filters[1].To));
        Assert.Equal(("5", "10"), (filters[2].From, filters[2].To));
        Assert.Empty(CustomFieldFilters.FromQuery(new Dictionary<string, StringValues>()));
    }

    [Fact]
    public async Task The_list_filters_by_each_field_type_and_only_for_fields_enabled_for_search()
    {
        await using var f = await NewWorldAsync();
        var c = await f.AddClientAsync("C1");
        var a = await f.AddOrderAsync(c.ClientId, "A", OrderStatuses.Submitted);
        var b = await f.AddOrderAsync(c.ClientId, "B", OrderStatuses.Submitted);
        var n = await f.AddOrderAsync(c.ClientId, "N", OrderStatuses.Submitted);
        var division = await DefAsync(f, "division", "SELECT", true);
        var nota = await DefAsync(f, "nota", "TEXT", true);
        var peso = await DefAsync(f, "peso", "NUMBER", true);
        var entrega = await DefAsync(f, "entrega", "DATE", true);
        var urgente = await DefAsync(f, "urgente", "BOOL", true);
        var tags = await DefAsync(f, "tags", "MULTISELECT", true);
        var oculto = await DefAsync(f, "oculto", "TEXT", false);
        Value(f, division, a.TransportOrderId, text: "GLT"); Value(f, division, b.TransportOrderId, text: "OXY");
        Value(f, nota, a.TransportOrderId, text: "Frágil, llamar antes"); Value(f, nota, b.TransportOrderId, text: "Sin novedad");
        Value(f, peso, a.TransportOrderId, number: 5m); Value(f, peso, b.TransportOrderId, number: 12m);
        Value(f, entrega, a.TransportOrderId, date: new DateTime(2026, 10, 5)); Value(f, entrega, b.TransportOrderId, date: new DateTime(2026, 10, 20));
        Value(f, urgente, a.TransportOrderId, flag: true); Value(f, urgente, b.TransportOrderId, flag: false);
        Value(f, tags, a.TransportOrderId, text: "frio,fragil"); Value(f, tags, b.TransportOrderId, text: "pesado");
        Value(f, oculto, a.TransportOrderId, text: "x");
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        Assert.Equal(new[] { "A", "B", "N" }, await NumbersAsync(f));                                                                 // sin filtros: todas
        Assert.Equal(new[] { "A" }, await NumbersAsync(f, new CustomFieldFilter("division", "GLT")));                                   // lista: igualdad
        Assert.Equal(new[] { "A" }, await NumbersAsync(f, new CustomFieldFilter("nota", "llamar")));                                    // texto: contiene
        Assert.Equal(new[] { "B" }, await NumbersAsync(f, new CustomFieldFilter("peso", From: "10")));                                  // número: desde
        Assert.Equal(new[] { "A", "B" }, await NumbersAsync(f, new CustomFieldFilter("peso", From: "5", To: "12")));                    // número: rango inclusivo
        Assert.Equal(new[] { "A" }, await NumbersAsync(f, new CustomFieldFilter("entrega", From: "2026-10-01", To: "2026-10-05")));    // fecha: el día final cuenta
        Assert.Equal(new[] { "B" }, await NumbersAsync(f, new CustomFieldFilter("entrega", From: "2026-10-06")));
        Assert.Equal(new[] { "B" }, await NumbersAsync(f, new CustomFieldFilter("urgente", "false")));                                  // sí/no
        Assert.Equal(new[] { "A" }, await NumbersAsync(f, new CustomFieldFilter("tags", "fragil")));                                    // selección múltiple: contiene la opción
        Assert.Empty(await NumbersAsync(f, new CustomFieldFilter("tags", "frag")));                                                     // no por pedazos de opción
        Assert.Equal(new[] { "A" }, await NumbersAsync(f, new CustomFieldFilter("division", "GLT"), new CustomFieldFilter("peso", To: "6")));   // todos los filtros a la vez
        Assert.Empty(await NumbersAsync(f, new CustomFieldFilter("division", "GLT"), new CustomFieldFilter("peso", From: "10")));

        // inválidos: campo desconocido, no habilitado para la búsqueda, valor mal formado
        Assert.Equal(CustomFieldFilters.UnknownMessage("nada"), (await Assert.ThrowsAsync<ValidationException>(() => NumbersAsync(f, new CustomFieldFilter("nada", "x")))).Message);
        Assert.Equal(CustomFieldFilters.NotEnabledMessage("oculto"), (await Assert.ThrowsAsync<ValidationException>(() => NumbersAsync(f, new CustomFieldFilter("oculto", "x")))).Message);
        Assert.Equal(CustomFieldFilters.InvalidValueMessage("peso"), (await Assert.ThrowsAsync<ValidationException>(() => NumbersAsync(f, new CustomFieldFilter("peso", From: "mucho")))).Message);
        Assert.Equal(CustomFieldFilters.InvalidValueMessage("urgente"), (await Assert.ThrowsAsync<ValidationException>(() => NumbersAsync(f, new CustomFieldFilter("urgente", "quizás")))).Message);
        _ = n;
    }

    [Fact]
    public async Task Views_carry_custom_fields_as_columns_and_filters_without_any_option()
    {
        await using var f = await NewWorldAsync();
        var c = await f.AddClientAsync("C1");
        var a = await f.AddOrderAsync(c.ClientId, "A", OrderStatuses.Submitted);
        var b = await f.AddOrderAsync(c.ClientId, "B", OrderStatuses.Submitted);
        var oculto = await DefAsync(f, "oculto", "TEXT", false);   // sin «Mostrar en la búsqueda»: igual está en las vistas
        Value(f, oculto, a.TransportOrderId, text: "rojo"); Value(f, oculto, b.TransportOrderId, text: "azul");
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var engine = f.Get<AnalyticsEngine>();
        var spec = new ReportSpec { SourceKey = EntityTypes.TransportOrder, Columns = new[] { "OrderNumber", "cf.oculto" }, FilterJson = "{\"and\":[{\"field\":\"cf.oculto\",\"op\":\"eq\",\"value\":\"azul\"}]}" };
        var result = await engine.RunReportAsync(spec, default);
        var row = Assert.Single(result.Rows);
        Assert.Equal(("B", "azul"), ((string?)row["OrderNumber"], (string?)row["cf.oculto"]));
        Assert.Contains(result.Columns, col => col.Key == "cf.oculto");
    }
}
