using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 4 / P0: las constantes de Flota, choferes y mantenimiento coinciden con los literales de Diseño/logistica-db-seed.sql
/// y el catálogo de permisos separa flota (fleet.*) de compensación (driverpay.*, R8). Si alguien cambia un código aquí o en
/// el seed sin el otro, esta prueba lo delata.
/// </summary>
public class FleetCatalogTests
{
    [Fact]
    public void Status_domains_and_codes_match_the_seed()
    {
        Assert.Equal("VehicleStatus", StatusDomains.VehicleStatus);
        Assert.Equal("DriverStatus", StatusDomains.DriverStatus);
        Assert.Equal("WorkOrderStatus", StatusDomains.WorkOrderStatus);
        Assert.Equal("DriverTripStatus", StatusDomains.DriverTripStatus);

        Assert.Equal(new[] { "ACTIVE", "MAINTENANCE", "INACTIVE" }, new[] { VehicleStatuses.Active, VehicleStatuses.Maintenance, VehicleStatuses.Inactive });
        Assert.Equal(new[] { "ACTIVE", "UNAVAILABLE", "INACTIVE" }, new[] { DriverStatuses.Active, DriverStatuses.Unavailable, DriverStatuses.Inactive });
        Assert.Equal(new[] { "OPEN", "IN_PROGRESS", "CLOSED", "CANCELLED" },
            new[] { WorkOrderStatuses.Open, WorkOrderStatuses.InProgress, WorkOrderStatuses.Closed, WorkOrderStatuses.Cancelled });
        Assert.Equal(new[] { "OPEN", "SETTLED", "CANCELLED" }, new[] { DriverTripStatuses.Open, DriverTripStatuses.Settled, DriverTripStatuses.Cancelled });
    }

    [Fact]
    public void Lookup_domains_and_codes_match_the_seed()
    {
        Assert.Equal("VehicleType", LookupDomains.VehicleType);
        Assert.Equal("Ownership", LookupDomains.Ownership);
        Assert.Equal("FuelType", LookupDomains.FuelType);
        Assert.Equal("VehicleDocType", LookupDomains.VehicleDocType);
        Assert.Equal("MaintenanceTrigger", LookupDomains.MaintenanceTrigger);
        Assert.Equal("MaintenanceType", LookupDomains.MaintenanceType);
        Assert.Equal("LicenseClass", LookupDomains.LicenseClass);
        Assert.Equal("CertificationType", LookupDomains.CertificationType);
        Assert.Equal("DevicePlatform", LookupDomains.DevicePlatform);
        Assert.Equal("DriverPayoutFormula", LookupDomains.DriverPayoutFormula);

        Assert.Equal(new[] { "MILEAGE", "TIME", "BOTH" }, new[] { MaintenanceTriggers.Mileage, MaintenanceTriggers.Time, MaintenanceTriggers.Both });
        Assert.Equal(new[] { "PREVENTIVE", "CORRECTIVE" }, new[] { MaintenanceTypes.Preventive, MaintenanceTypes.Corrective });
        Assert.Equal(new[] { "DELIVERY_PLUS_ATTEMPTS", "DELIVERY_INCLUDES_FIRST", "FAILED_REPLACES_DELIVERY" },
            new[] { DriverPayoutFormulas.DeliveryPlusAttempts, DriverPayoutFormulas.DeliveryIncludesFirst, DriverPayoutFormulas.FailedReplacesDelivery });
        // Los cuatro tipos de VehicleDocType sembrados + las dos clases de documento del chofer
        Assert.Equal(new[] { "REGISTRATION", "INSURANCE", "INSPECTION", "PERMIT", "LICENSE", "CERTIFICATION" },
            new[] { FleetDocumentKinds.Registration, FleetDocumentKinds.Insurance, FleetDocumentKinds.Inspection, FleetDocumentKinds.Permit,
                    FleetDocumentKinds.License, FleetDocumentKinds.Certification });
        Assert.Equal(new[] { "VEHICLE", "DRIVER" }, new[] { FleetOwnerKinds.Vehicle, FleetOwnerKinds.Driver });
        Assert.Equal(new[] { "EXPIRED", "EXPIRING", "OK", "NO_EXPIRY" }, new[] { ExpiryStates.Expired, ExpiryStates.Expiring, ExpiryStates.Ok, ExpiryStates.NoExpiry });
        Assert.Equal(new[] { "OK", "DUE_SOON", "OVERDUE", "NO_BASELINE" },
            new[] { MaintenanceDueStates.Ok, MaintenanceDueStates.DueSoon, MaintenanceDueStates.Overdue, MaintenanceDueStates.NoBaseline });
    }

    [Fact]
    public void Entity_types_reuse_WORK_ORDER_and_add_the_six_new_ones()
    {
        // WORK_ORDER ya estaba sembrado (seed:140); no existe MAINTENANCE_WORK_ORDER.
        Assert.Equal("WORK_ORDER", EntityTypes.WorkOrder);
        Assert.Equal("VEHICLE", EntityTypes.Vehicle);
        Assert.Equal("DRIVER", EntityTypes.Driver);
        Assert.Equal("MAINTENANCE_SCHEDULE", EntityTypes.MaintenanceSchedule);
        Assert.Equal("FUEL_LOG", EntityTypes.FuelLog);
        Assert.Equal("FLEET_DOCUMENT", EntityTypes.FleetDocument);
        Assert.Equal("DRIVER_RATE", EntityTypes.DriverRate);
        Assert.Equal("DRIVER_TRIP", EntityTypes.DriverTrip);
        Assert.Equal("DISPATCH_ZONE", EntityTypes.DispatchZone);
        Assert.Null(typeof(EntityTypes).GetField("MaintenanceWorkOrder"));
    }

    [Fact]
    public void Capability_and_number_kind_of_the_work_order()
    {
        Assert.Equal("EDIT_WORK_ORDER", Capabilities.EditWorkOrder);
        Assert.Equal("WORKORDER", NumberKinds.WorkOrder);
        Assert.True(NumberingRules.IsKnownKind("WORKORDER"));
        Assert.True(NumberingRules.IsKnownKind(NumberKinds.PackBatch));
        Assert.False(NumberingRules.IsKnownKind("WORK_ORDER"));
    }

    [Fact]
    public void Permission_catalog_has_52_distinct_codes_with_the_three_new_fleet_permissions()
    {
        Assert.Equal(52, PermissionCatalog.All.Count);
        Assert.Equal(52, PermissionCatalog.All.Select(p => p.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var view = Assert.Single(PermissionCatalog.All, p => p.Code == "fleet.view");
        Assert.Equal(("FLEET", "Ver flota y choferes", "View fleet & drivers"), (view.Category, view.LabelEs, view.LabelEn));
        var payView = Assert.Single(PermissionCatalog.All, p => p.Code == "driverpay.view");
        Assert.Equal(("FLEET", "Ver tarifas y viajes de choferes", "View driver rates & trips"), (payView.Category, payView.LabelEs, payView.LabelEn));
        var payManage = Assert.Single(PermissionCatalog.All, p => p.Code == "driverpay.manage");
        Assert.Equal(("FLEET", "Gestionar tarifas y viajes de choferes", "Manage driver rates & trips"), (payManage.Category, payManage.LabelEs, payManage.LabelEn));

        Assert.Equal("fleet.view", PermissionCatalog.FleetView);
        Assert.Equal("driverpay.view", PermissionCatalog.DriverPayView);
        Assert.Equal("driverpay.manage", PermissionCatalog.DriverPayManage);
    }

    [Theory]
    [InlineData("fleet.view", new[] { "Dispatcher", "ReadOnly", "TenantAdmin" })]
    [InlineData("driverpay.view", new[] { "Billing", "TenantAdmin" })]
    [InlineData("driverpay.manage", new[] { "TenantAdmin" })]
    public void Role_templates_receive_the_new_permissions(string code, string[] expectedRoles)
    {
        var withIt = PermissionCatalog.RoleTemplates.Where(t => t.Value.Contains(code)).Select(t => t.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedRoles, withIt);
    }

    [Fact]
    public void Driver_template_has_no_fleet_nor_driverpay_permission()
    {
        var driver = PermissionCatalog.RoleTemplates["Driver"];
        Assert.DoesNotContain(driver, c => c.StartsWith("fleet.", StringComparison.Ordinal) || c.StartsWith("driverpay.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("VEHICLE", "fleet.view", "fleet.manage")]
    [InlineData("DRIVER", "fleet.view", "fleet.manage")]
    [InlineData("DISPATCH_ZONE", "fleet.view", "fleet.manage")]
    [InlineData("FLEET_DOCUMENT", "fleet.view", "fleet.manage")]
    [InlineData("MAINTENANCE_SCHEDULE", "fleet.view", "fleet.maintenance")]
    [InlineData("WORK_ORDER", "fleet.view", "fleet.maintenance")]
    [InlineData("FUEL_LOG", "fleet.view", "fleet.maintenance")]
    [InlineData("DRIVER_RATE", "driverpay.view", "driverpay.manage")]
    [InlineData("DRIVER_TRIP", "driverpay.view", "driverpay.manage")]
    public void Owner_permissions_of_the_fleet_entity_types(string entityType, string read, string write)
    {
        Assert.Equal(read, PermissionCatalog.OwnerReadPermission[entityType]);
        Assert.Equal(write, PermissionCatalog.OwnerWritePermission[entityType]);
    }

    [Fact]
    public void New_permissions_propagate_to_cloned_template_roles()
    {
        var newCodes = new HashSet<string> { "fleet.view", "driverpay.view", "driverpay.manage" };
        Assert.Equal(new[] { "fleet.view" }, PermissionCatalog.CodesToPropagate("Dispatcher", newCodes, new HashSet<string>()));
        Assert.Equal(new[] { "driverpay.view" }, PermissionCatalog.CodesToPropagate("Billing", newCodes, new HashSet<string>()));
        Assert.Empty(PermissionCatalog.CodesToPropagate("Driver", newCodes, new HashSet<string>()));
    }
}
