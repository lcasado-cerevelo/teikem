using Teikem.Infrastructure.Notifications;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 197 — la categoría con que la pantalla de reglas agrupa los eventos de aviso.</summary>
public class NotificationEventCategoryTests
{
    [Theory]
    [InlineData("ASN_RECEIVED", "WAREHOUSE")]
    [InlineData("EXIT_NOTICE_BACKORDER_RESERVED", "WAREHOUSE")]
    [InlineData("INVENTORY_STOCK_LOW", "WAREHOUSE")]
    [InlineData("SALES_ORDER_SHIPPED", "SALES")]
    [InlineData("FLEET_DOC_EXPIRING", "TRANSPORT")]
    [InlineData("FLEET_VEHICLE_IDLE", "TRANSPORT")]
    [InlineData("FLEET_MAINTENANCE_DUE_SOON", "MAINTENANCE")]
    [InlineData("FLEET_MAINTENANCE_OVERDUE", "MAINTENANCE")]
    [InlineData("FLEET_WORKORDER_STALE", "MAINTENANCE")]
    [InlineData("INTEGRATION_RUN", "INTEGRATIONS")]
    [InlineData("TEST", "OTHER")]
    public void Each_event_code_falls_in_its_category(string code, string category) => Assert.Equal(category, NotificationEventCategories.Of(code));

    [Fact]
    public void Every_registered_event_has_a_known_category()
    {
        var providers = new INotificationEventProvider[] { new TestEventProvider(), new FleetEventProvider(), new AsnEventProvider(), new ExitNoticeEventProvider(), new InventoryEventProvider(), new SalesEventProvider(), new IntegrationEventProvider() };
        foreach (var e in new NotificationEventCatalog(providers).All) Assert.Contains(NotificationEventCategories.Of(e.Code), NotificationEventCategories.Order);
    }
}
