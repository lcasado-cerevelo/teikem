using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 3 / P0: el catálogo de permisos y las constantes de órdenes coinciden con el seed SQL (literales). Si alguien
/// cambia un código aquí o en Diseño/logistica-db-seed.sql sin el otro, esta prueba lo delata.
/// </summary>
public class OrderCatalogTests
{
    [Fact]
    public void Owner_permissions_of_transport_order_are_orders_view_and_orders_edit()
    {
        Assert.Equal("orders.view", PermissionCatalog.OwnerReadPermission["TRANSPORT_ORDER"]);
        Assert.Equal("orders.edit", PermissionCatalog.OwnerWritePermission["TRANSPORT_ORDER"]);
    }

    [Theory]
    [InlineData("ORDER_COD", "orders.view")]
    [InlineData("ORDER_STOP", "orders.view")]
    [InlineData("IMPORT_BATCH", "orders.view")]
    [InlineData("IMPORT_TEMPLATE", "orders.edit")]
    public void Owner_permissions_close_the_other_entity_types_of_the_lot(string entityType, string readPermission)
    {
        // Historial, contactos y campos personalizados de estos EntityType no quedan abiertos a cualquier autenticado.
        Assert.Equal(readPermission, PermissionCatalog.OwnerReadPermission[entityType]);
        Assert.Equal("orders.edit", PermissionCatalog.OwnerWritePermission[entityType]);
    }

    [Fact]
    public void Permission_catalog_has_54_codes_including_credit_override()
    {
        // Lote 4: 49 → 52 (fleet.view, driverpay.view, driverpay.manage). Lote 5: 52 → 54 (trips.view, trips.scan).
        Assert.Equal(54, PermissionCatalog.All.Count);
        Assert.Equal(54, PermissionCatalog.All.Select(p => p.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var credit = Assert.Single(PermissionCatalog.All, p => p.Code == "orders.credit_override");
        Assert.Equal("ORDERS", credit.Category);
        Assert.Equal("Autorizar órdenes sobre el límite de crédito", credit.LabelEs);
        Assert.Equal("Authorize orders over credit limit", credit.LabelEn);
    }

    [Fact]
    public void Credit_override_is_in_TenantAdmin_and_Billing_templates_only()
    {
        var withIt = PermissionCatalog.RoleTemplates.Where(t => t.Value.Contains(PermissionCatalog.OrdersCreditOverride)).Select(t => t.Key).OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "Billing", "TenantAdmin" }, withIt);
    }

    [Fact]
    public void Number_kinds_match_the_sql_check_constraint()
    {
        Assert.Equal("ORDER", NumberKinds.Order);
        Assert.Equal("INVOICE", NumberKinds.Invoice);
        Assert.Equal("PACKAGE", NumberKinds.Package);
        Assert.Equal("PACKBATCH", NumberKinds.PackBatch);
    }

    [Fact]
    public void Order_statuses_match_the_seed()
    {
        Assert.Equal("DRAFT", OrderStatuses.Draft);
        Assert.Equal("CONFIRMED", OrderStatuses.Confirmed);
        Assert.Equal("PICKUP", OrderStatuses.Pickup);
        Assert.Equal("INBOUND", OrderStatuses.Inbound);
        Assert.Equal("PLANNED", OrderStatuses.Planned);
        Assert.Equal("IN_TRANSIT", OrderStatuses.InTransit);
        Assert.Equal("ARRIVED", OrderStatuses.Arrived);
        Assert.Equal("DELIVERED", OrderStatuses.Delivered);
        Assert.Equal("ON_HOLD", OrderStatuses.OnHold);
        Assert.Equal("PARTIAL", OrderStatuses.Partial);
        Assert.Equal("FAILED", OrderStatuses.Failed);
        Assert.Equal("CANCELLED", OrderStatuses.Cancelled);
        Assert.Equal("OrderStatus", StatusDomains.OrderStatus);
    }

    [Fact]
    public void Stop_and_cod_statuses_match_the_seed()
    {
        Assert.Equal("StopStatus", StatusDomains.StopStatus);
        Assert.Equal("PENDING", StopStatuses.Pending);
        Assert.Equal("EN_ROUTE", StopStatuses.EnRoute);
        Assert.Equal("COMPLETED", StopStatuses.Completed);
        Assert.Equal("FAILED", StopStatuses.Failed);

        Assert.Equal("CodStatus", StatusDomains.CodStatus);
        Assert.Equal("PENDING", CodStatuses.Pending);
        Assert.Equal("PARTIAL", CodStatuses.Partial);
        Assert.Equal("COLLECTED", CodStatuses.Collected);
        Assert.Equal("RECONCILED", CodStatuses.Reconciled);
        Assert.Equal("REMITTED", CodStatuses.Remitted);

        Assert.Equal("ImportBatchStatus", StatusDomains.ImportBatchStatus);
    }

    [Fact]
    public void Entity_types_and_lookup_domains_of_the_lot_match_the_seed()
    {
        Assert.Equal("ORDER_COD", EntityTypes.OrderCod);
        Assert.Equal("ORDER_STOP", EntityTypes.OrderStop);
        Assert.Equal("TRANSPORT_ORDER", EntityTypes.TransportOrder);
        Assert.Equal("IMPORT_TEMPLATE", EntityTypes.ImportTemplate);
        Assert.Equal("IMPORT_BATCH", EntityTypes.ImportBatch);
        Assert.Equal("PICKUP", StopTypes.Pickup);
        Assert.Equal("DELIVERY", StopTypes.Delivery);
        Assert.Equal("StopType", LookupDomains.StopType);
        Assert.Equal("OrderPriority", LookupDomains.OrderPriority);
        Assert.Equal("OrderRefType", LookupDomains.OrderRefType);
        Assert.Equal("CodType", LookupDomains.CodType);
        Assert.Equal("GeocodeAccuracy", LookupDomains.GeocodeAccuracy);
        Assert.Equal("UnitOfMeasure", LookupDomains.UnitOfMeasure);
    }
}
