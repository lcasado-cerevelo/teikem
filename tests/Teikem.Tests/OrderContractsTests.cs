using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Infrastructure.Contracts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 3 / P0 (injerto, DECISIÓN 15): el contrato de los DTOs de órdenes queda fijado por reflexión para que nadie agregue
/// por descuido campos que se fijan al crear (cliente, números) o que no se capturan (tipo de COD, empaque).
/// </summary>
public class OrderContractsTests
{
    private static string[] PropertyNames<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();

    [Fact]
    public void Patch_request_does_not_expose_fields_fixed_at_creation()
    {
        var names = PropertyNames<OrderPatchRequest>();
        foreach (var forbidden in new[] { "ClientPublicId", "OrderNumber", "ClientInvoiceNumber", "PackBatchNumber", "CodType" })
            Assert.DoesNotContain(forbidden, names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_request_does_not_expose_pack_batch_number_nor_cod_type()
    {
        var names = PropertyNames<OrderCreateRequest>();
        Assert.DoesNotContain("PackBatchNumber", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CodType", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ClientPublicId", names);
        Assert.Contains("OrderNumber", names);
        Assert.Contains("ClientInvoiceNumber", names);
    }

    [Theory]
    [InlineData(typeof(OrderCreateRequest))]
    [InlineData(typeof(OrderPatchRequest))]
    public void Create_and_patch_requests_collect_unknown_keys_in_Extra(Type type)
    {
        var extra = type.GetProperty("Extra");
        Assert.NotNull(extra);
        Assert.NotNull(extra!.GetCustomAttribute<JsonExtensionDataAttribute>());
        Assert.True(extra.CanWrite);
        Assert.Equal(typeof(IDictionary<string, System.Text.Json.JsonElement>), extra.PropertyType);
    }

    [Theory]
    [InlineData(typeof(OrderDetailDto))]
    [InlineData(typeof(OrderListItemDto))]
    public void Client_invoice_number_is_a_non_nullable_string(Type type)
    {
        var prop = type.GetProperty("ClientInvoiceNumber");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
        var info = new NullabilityInfoContext().Create(prop);
        Assert.Equal(NullabilityState.NotNull, info.ReadState);
    }

    [Fact]
    public void List_item_has_pack_batch_number_as_first_business_column()
    {
        var ctor = typeof(OrderListItemDto).GetConstructors().Single();
        var parameters = ctor.GetParameters().Select(p => p.Name).ToArray();
        Assert.Equal("Id", parameters[0]);
        Assert.Equal("PublicId", parameters[1]);
        Assert.Equal("PackBatchNumber", parameters[2]);
    }

    [Theory]
    [InlineData(typeof(OrderScope))]
    [InlineData(typeof(OrderCreationOptions))]
    public void Internal_service_types_are_not_http_dtos(Type type)
    {
        Assert.Null(type.GetConstructor(Type.EmptyTypes));
        Assert.Null(type.GetCustomAttribute<FromBodyAttribute>());
        foreach (var prop in type.GetProperties())
            Assert.Null(prop.GetCustomAttribute<FromBodyAttribute>());
    }

    [Fact]
    public void OrderScope_Any_has_no_client()
    {
        Assert.Null(OrderScope.Any.ClientId);
        Assert.Equal(7, new OrderScope(7).ClientId);
    }
}
