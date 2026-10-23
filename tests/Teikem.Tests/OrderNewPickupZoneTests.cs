using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-23 (CMS): la dirección del paciente es el punto de RECOGIDA del envío (escrito libre: se compara con el directorio y, si no coincide, se crea), el consignatario es uno fijo de CMS y la ruta del cliente
/// fuerza la zona de despacho (por código o nombre). OrderService real sobre InMemory (el rollback real lo cubre el smoke).
/// </summary>
public sealed class OrderNewPickupZoneTests
{
    private static OrderConsigneeCreateRequest Patient(string name = "Alvarado, Sylvia", string line1 = "Bo Amelia III")
        => new(name, line1, null, "Guaynabo", null, "00696", null, null);

    private static OrderCreateRequest Req(OrderSubmissionLifecycleTests.World x, OrderConsigneeCreateRequest? pickup, string? zone, Guid? pickupPublicId = null)
        => new(x.C.PublicId, x.Consignee.PublicId, null, pickupPublicId, "STANDARD", null,
            new List<OrderPackageLineRequest> { new("BOX", "HOSPITAL BED"), new("BOX", "MOTOR") }, null, null, null, null, null, "LLAMAR ANTES", null,
            OverrideCredit: true, NewPickup: pickup, DispatchZoneCode: zone);

    private static async Task<OrderSubmissionLifecycleTests.World> WorldAsync()
    {
        var x = await OrderSubmissionLifecycleTests.NewWorldAsync();
        // InMemory no aplica el DEFAULT NEWID() de PublicId: se asigna al guardar, como haría SQL Server
        x.Db.SavingChanges += (_, _) =>
        {
            foreach (var e in x.Db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
                if (e.Metadata.FindProperty("PublicId") is { ClrType: var t } p && t == typeof(Guid) && (Guid)e.Property(p.Name).CurrentValue! == Guid.Empty)
                    e.Property(p.Name).CurrentValue = Guid.NewGuid();
        };
        x.Db.LookupCodes.Add(new Teikem.Domain.Catalogs.LookupCode { LookupCodeId = 6000, Entity = Teikem.Domain.Constants.LookupDomains.LocationType, InternalCode = Teikem.Domain.Constants.LocationTypes.Both, LabelJson = "{\"es\":\"Ambas\"}", IsActive = true });
        await x.Db.SaveChangesAsync();
        x.F.Lookups.Load(x.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        x.Db.ChangeTracker.Clear();
        x.Db.DispatchZones.Add(new DispatchZone { DispatchZoneId = 7001, TenantId = WmsFixture.TenantId, Code = "ERD1", Name = "ERD Pick Up 1", IsActive = true });
        x.Db.DispatchZones.Add(new DispatchZone { DispatchZoneId = 7002, TenantId = WmsFixture.TenantId, Code = "OLD", Name = "Ruta vieja", IsActive = false });
        await x.Db.SaveChangesAsync();
        x.Db.ChangeTracker.Clear();
        return x;
    }

    [Fact]
    public async Task The_patient_address_becomes_the_pickup_stop_and_the_fixed_consignee_the_delivery_stop()
    {
        var x = await WorldAsync();
        var dto = await x.Orders.CreateAsync(Req(x, Patient(), zone: null), OrderScope.Any, null, default);

        Assert.NotNull(dto.Pickup);
        Assert.Equal("Alvarado, Sylvia", dto.Pickup!.Name);
        Assert.Equal("Bo Amelia III", dto.Pickup.Line1);
        Assert.Equal("Guaynabo", dto.Pickup.City);
        Assert.Equal("Tienda Uno", dto.Delivery.Name);   // el consignatario fijo
        var patient = await x.Db.Locations.AsNoTracking().SingleAsync(l => l.Name == "Alvarado, Sylvia");
        Assert.Equal(x.C.ClientId, patient.ClientId);     // la dirección queda en el directorio del cliente
    }

    [Fact]
    public async Task The_same_patient_twice_reuses_the_location_instead_of_duplicating_it()
    {
        var x = await WorldAsync();
        await x.Orders.CreateAsync(Req(x, Patient(), null), OrderScope.Any, null, default);
        x.Db.ChangeTracker.Clear();
        await x.Orders.CreateAsync(Req(x, Patient(), null), OrderScope.Any, null, default);
        Assert.Equal(1, await x.Db.Locations.AsNoTracking().CountAsync(l => l.Name == "Alvarado, Sylvia"));
    }

    [Fact]
    public async Task The_route_forces_the_zone_by_name_or_by_code_and_an_unknown_one_is_refused()
    {
        var x = await WorldAsync();
        var byName = await x.Orders.CreateAsync(Req(x, Patient(), "ERD Pick Up 1"), OrderScope.Any, null, default);
        x.Db.ChangeTracker.Clear();
        var byCode = await x.Orders.CreateAsync(Req(x, Patient("Perez, Juan", "Calle Luna 5"), "erd1"), OrderScope.Any, null, default);
        x.Db.ChangeTracker.Clear();
        Assert.Equal(7001, (await x.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.PublicId == byName.PublicId)).DeliveryZoneId);
        Assert.Equal(7001, (await x.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.PublicId == byCode.PublicId)).DeliveryZoneId);

        var unknown = await Assert.ThrowsAsync<ValidationException>(() => x.Orders.CreateAsync(Req(x, Patient("Otro", "Calle 9"), "ERD 9"), OrderScope.Any, null, default));
        Assert.Equal(OrderService.UnknownZoneMessage("ERD 9"), unknown.Message);
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => x.Orders.CreateAsync(Req(x, Patient("Otro", "Calle 9"), "Ruta vieja"), OrderScope.Any, null, default));
        Assert.Equal(OrderService.UnknownZoneMessage("Ruta vieja"), inactive.Message);
    }

    [Fact]
    public async Task A_new_pickup_and_an_existing_one_together_are_refused_and_an_incomplete_pickup_says_what_is_missing()
    {
        var x = await WorldAsync();
        var both = await Assert.ThrowsAsync<ValidationException>(() => x.Orders.CreateAsync(Req(x, Patient(), null, pickupPublicId: x.Consignee.PublicId), OrderScope.Any, null, default));
        Assert.Equal(OrderService.NewPickupBothMessage, both.Message);

        var noLine = await Assert.ThrowsAsync<ValidationException>(() => x.Orders.CreateAsync(Req(x, Patient(line1: ""), null), OrderScope.Any, null, default));
        Assert.Equal("La dirección (línea 1) del punto de recogida es obligatoria.", noLine.Message);
    }

    [Fact]
    public async Task Without_a_new_pickup_or_a_zone_the_order_is_created_as_always()
    {
        var x = await WorldAsync();
        var dto = await x.Orders.CreateAsync(Req(x, null, null), OrderScope.Any, null, default);
        Assert.NotEqual(7001, (await x.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.PublicId == dto.PublicId)).DeliveryZoneId);
        Assert.Null(dto.Pickup);   // este cliente no tiene recogida por defecto
    }

    [Fact]
    public void The_import_templates_know_the_pickup_and_zone_fields()
    {
        foreach (var f in new[] { ImportFields.PickupName, ImportFields.PickupLine1, ImportFields.PickupLine2, ImportFields.PickupCity, ImportFields.PickupState, ImportFields.PickupPostalCode, ImportFields.Zone })
            Assert.True(ImportFields.IsKnown(f), f);
        Assert.Equal("pickupName", ImportFields.Canonical("PICKUPNAME"));
    }
}
