using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Teikem.Domain.Common;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Contracts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 4 / P0: el contrato de los DTOs de flota, choferes, mantenimiento y pago queda fijado por reflexión (como
/// OrderContractsTests): los campos que se fijan al crear no aparecen en los PATCH, el PushToken nunca sale, ninguna
/// solicitud de tarifa ni de viaje trae al chofer (R31: sale de la ruta) y la orden expone al chofer sin montos.
/// </summary>
public class FleetContractsTests
{
    private static string[] PropertyNames(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();

    [Theory]
    [InlineData(typeof(VehiclePatchRequest), new[] { "Code", "HomeWarehouseId" })]
    [InlineData(typeof(DriverPatchRequest), new[] { "Code", "EmployeeCode" })]
    [InlineData(typeof(WorkOrderPatchRequest), new[] { "Number", "VehiclePublicId" })]
    [InlineData(typeof(DriverRateUpdateRequest), new[] { "ServiceType", "PackageType", "SpecialServiceTypeId" })]
    [InlineData(typeof(FuelLogPatchRequest), new[] { "VehiclePublicId" })]
    public void Patch_requests_do_not_expose_fields_fixed_at_creation(Type type, string[] forbidden)
    {
        var names = PropertyNames(type);
        foreach (var f in forbidden) Assert.DoesNotContain(f, names, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(typeof(VehiclePatchRequest))]
    [InlineData(typeof(DriverPatchRequest))]
    [InlineData(typeof(WorkOrderPatchRequest))]
    [InlineData(typeof(DriverRateUpdateRequest))]
    [InlineData(typeof(FuelLogPatchRequest))]
    public void Patch_requests_collect_unknown_keys_in_Extra(Type type)
    {
        var extra = type.GetProperty("Extra");
        Assert.NotNull(extra);
        Assert.NotNull(extra!.GetCustomAttribute<JsonExtensionDataAttribute>());
        Assert.True(extra.CanWrite);
        Assert.Equal(typeof(IDictionary<string, JsonElement>), extra.PropertyType);
    }

    [Fact]
    public void Driver_device_dto_never_exposes_the_push_token()
    {
        var names = PropertyNames(typeof(DriverDeviceDto));
        Assert.DoesNotContain("PushToken", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("HasPushToken", names);
    }

    [Theory]
    [InlineData(typeof(DriverDeliveryRateCreateRequest))]
    [InlineData(typeof(DriverTripRateCreateRequest))]
    [InlineData(typeof(DriverRateUpdateRequest))]
    [InlineData(typeof(DriverRateCloseRequest))]
    [InlineData(typeof(DriverTripCreateRequest))]
    [InlineData(typeof(DriverTripCancelRequest))]
    [InlineData(typeof(DriverPayPolicyPatchRequest))]
    public void Rate_and_trip_requests_never_carry_the_driver(Type type)
    {
        var names = PropertyNames(type);
        Assert.DoesNotContain("DriverId", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DriverPublicId", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Order_create_request_takes_the_driver_as_last_optional_parameter()
    {
        var ctor = typeof(OrderCreateRequest).GetConstructors().Single();
        var last = ctor.GetParameters().Last();
        Assert.Equal("DriverPublicId", last.Name);
        Assert.Equal(typeof(Guid?), last.ParameterType);
        Assert.True(last.HasDefaultValue);
        Assert.Null(last.DefaultValue);
    }

    [Fact]
    public void Order_detail_exposes_the_assigned_driver_without_amounts()
    {
        // Lote 5: al trío AssignedDriver* le siguen AssignedTripPublicId y AssignedTripCode (ruta vigente), todos con default.
        var ctor = typeof(OrderDetailDto).GetConstructors().Single();
        var tail = ctor.GetParameters().TakeLast(5).ToArray();
        Assert.Equal(new[] { "AssignedDriverPublicId", "AssignedDriverCode", "AssignedDriverName", "AssignedTripPublicId", "AssignedTripCode" },
            tail.Select(p => p.Name));
        Assert.All(tail, p => Assert.True(p.HasDefaultValue));

        var names = PropertyNames(typeof(OrderDetailDto));
        Assert.DoesNotContain(names, n => n.StartsWith("AssignedDriver", StringComparison.Ordinal)
                                         && (n.Contains("Amount", StringComparison.Ordinal) || n.Contains("Rate", StringComparison.Ordinal)));
        Assert.DoesNotContain(names, n => n.StartsWith("Driver", StringComparison.Ordinal)
                                         && (n.Contains("Amount", StringComparison.Ordinal) || n.Contains("Rate", StringComparison.Ordinal)));
        Assert.DoesNotContain("TripAmount", names);
    }

    [Fact]
    public void Prepared_service_types_do_not_live_in_the_contracts_namespace()
    {
        var contracts = typeof(OrderDetailDto).Assembly.GetTypes()
            .Where(t => t.Namespace == "Teikem.Infrastructure.Contracts")
            .Select(t => t.Name).ToList();
        Assert.DoesNotContain(contracts, n => n.StartsWith("Prepared", StringComparison.Ordinal));
    }

    [Fact]
    public void Fleet_dtos_are_sealed_records_and_not_ef_entities()
    {
        var fleetDtos = new[]
        {
            typeof(VehicleListItemDto), typeof(VehicleDetailDto), typeof(VehicleDocumentDto), typeof(ExpiringDocumentDto), typeof(FleetAvailabilityDto),
            typeof(DriverListItemDto), typeof(DriverDetailDto), typeof(DriverLicenseDto), typeof(DriverCertificationDto), typeof(DriverDeviceDto),
            typeof(DispatchZoneDto), typeof(MaintenanceScheduleDto), typeof(MaintenanceDueDto), typeof(WorkOrderListItemDto), typeof(WorkOrderDetailDto),
            typeof(MaintenanceTaskDto), typeof(FuelLogDto), typeof(FuelLogPageDto), typeof(DriverRatesDto), typeof(DriverDeliveryRateDto),
            typeof(DriverAttemptRateDto), typeof(DriverTripRateDto), typeof(DriverPayPolicyDto), typeof(PayoutPreviewDto), typeof(DriverTripDto),
        };

        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var entityClrTypes = db.Model.GetEntityTypes().Select(e => e.ClrType).ToHashSet();
        foreach (var t in fleetDtos)
        {
            Assert.True(t.IsSealed, $"{t.Name} debe ser sealed");
            Assert.NotNull(t.GetMethod("<Clone>$")); // record
            Assert.DoesNotContain(t, entityClrTypes);
        }
    }

    [Fact]
    public void Vehicle_list_query_and_work_order_list_query_defaults()
    {
        var v = new VehicleListQuery();
        Assert.False(v.IncludeInactive);
        var w = new WorkOrderListQuery();
        Assert.Equal((0, 100), (w.Skip, w.Take));
        var e = new ExpiringDocumentsQuery();
        Assert.Equal((30, true), (e.WithinDays, e.IncludeExpired));
    }

    /// <summary>
    /// Hallazgo de revisión: ningún flujo del lote crea DriverDevice (lo registra la app del Lote 7), así que la auditoría del
    /// smoke no puede demostrar que el PushToken no llega a AuditLog; se fija el atributo por reflexión.
    /// </summary>
    [Fact]
    public void Driver_device_push_token_is_sensitive_and_last_seen_not_audited()
    {
        Assert.NotNull(typeof(DriverDevice).GetProperty(nameof(DriverDevice.PushToken))!.GetCustomAttribute<SensitiveDataAttribute>());
        Assert.NotNull(typeof(DriverDevice).GetProperty(nameof(DriverDevice.LastSeenUtc))!.GetCustomAttribute<NotAuditedAttribute>());
    }
}
