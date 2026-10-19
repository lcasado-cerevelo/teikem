using System.Reflection;
using System.Text.RegularExpressions;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence.Scripts;
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
    public void Permission_catalog_has_69_codes_including_credit_override()
    {
        // Lote 4: 49 → 52 (fleet.view, driverpay.view, driverpay.manage). Lote 5: 52 → 54 (trips.view, trips.scan). Lote 6: 54 → 58. Lote 8A: 58 → 60 (devices.manage, warehouse.count.capture). Lote F8a: 60 → 65 (pulse.*). Lote 14: 65 → 66 (pulse.attention). Lote 27: 66 → 68 (rental.extend, rental.return).
        Assert.Equal(108, PermissionCatalog.All.Count);   // Lote 118: + orders.deliver; Lote 112: + orders.return; Lote 111: + claims.view, claims.manage; Lote 106: + orders.reopen;   // Lote 104: + clientmessages.reply, clientmessages.manage;   // 2026-10-08: + warehouse.damage; 2026-10-11: + warehouse.issue; 2026-10-12: + orders.view_price; 2026-10-12 (Lote 43): + printers.manage; 2026-10-12: + inventory.adjust.multi; 2026-10-16 (Lote 72): + billing.view
        Assert.Equal(108, PermissionCatalog.All.Select(p => p.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
        Assert.Equal("SUBMITTED", OrderStatuses.Submitted);
        Assert.Equal("PICKUP", OrderStatuses.Pickup);
        Assert.Equal("UNASSIGNED", OrderStatuses.Unassigned);
        Assert.Equal("ASSIGNED", OrderStatuses.Assigned);
        Assert.Equal("IN_TRANSIT", OrderStatuses.InTransit);
        Assert.Equal("DELIVERED", OrderStatuses.Delivered);
        Assert.Equal("FAILED", OrderStatuses.Failed);
        Assert.Equal("CANCELLED", OrderStatuses.Cancelled);
        Assert.Equal("IN_WAREHOUSE", OrderStatuses.InWarehouse);
        Assert.Equal("OrderStatus", StatusDomains.OrderStatus);
    }

    /// <summary>
    /// Lote 106: el seed (CONGELADO) trae el flujo viejo; la sección (an) del 2026-10-17 de logistica-db-update.sql renombra en sitio
    /// (DRAFT→SUBMITTED, INBOUND→UNASSIGNED, PLANNED→ASSIGNED), da de baja CONFIRMED/ARRIVED/ON_HOLD/PARTIAL y agrega IN_WAREHOUSE.
    /// El catálogo activo que resulta de aplicar seed + esa sección debe ser exactamente el de OrderStatuses.
    /// </summary>
    [Fact]
    public void Order_statuses_are_the_catalog_of_seed_plus_update()
    {
        var root = DatabaseInitializer.ResolveRepoRoot(null);
        var seed = File.ReadAllText(Path.Combine(root, "Diseño", "logistica-db-seed.sql"));
        var update = File.ReadAllText(Path.Combine(root, "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

        var codes = Regex.Matches(seed, @"\('OrderStatus','([A-Z_]+)'").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Contains("DRAFT", codes);

        var start = update.IndexOf("2026-10-17 (an) — Lote 106", StringComparison.Ordinal);
        Assert.True(start >= 0, "Falta la sección (an) del Lote 106 en logistica-db-update.sql.");
        var next = Regex.Match(update[(start + 10)..], @"\n\s+\d{4}-\d{2}-\d{2} \([a-z]+\) —");
        var section = next.Success ? update.Substring(start, next.Index + 10) : update[start..];

        foreach (Match m in Regex.Matches(section, @"SET InternalCode='([A-Z_]+)'[^\n]*\nWHERE Entity='OrderStatus' AND InternalCode='([A-Z_]+)'"))
        {
            Assert.True(codes.Remove(m.Groups[2].Value), $"El update renombra {m.Groups[2].Value}, que no está en el seed.");
            codes.Add(m.Groups[1].Value);
        }
        // Se borran los estatus que el update lista en el SELECT del remapeo (CONFIRMED, ARRIVED, ON_HOLD, PARTIAL) antes del DELETE FROM dbo.StatusCode.
        Assert.Contains("DELETE s FROM dbo.StatusCode s JOIN @old", section);
        var retired = Regex.Match(section, @"FROM dbo\.StatusCode s WHERE s\.Entity='OrderStatus' AND s\.InternalCode IN \(([^)]*)\)");
        Assert.True(retired.Success, "La sección (an) debe borrar los estatus viejos.");
        foreach (Match m in Regex.Matches(retired.Groups[1].Value, @"'([A-Z_]+)'")) codes.Remove(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(section, @"VALUES \('OrderStatus','([A-Z_]+)'")) codes.Add(m.Groups[1].Value);
        // Lote 112: secciones posteriores agregan estatus de orden (RETURNED en la (at)); esas inserciones también cuentan
        foreach (Match m in Regex.Matches(update, @"VALUES \(N'OrderStatus', N'([A-Z_]+)'")) codes.Add(m.Groups[1].Value);

        var constants = typeof(OrderStatuses).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        Assert.Equal(constants.OrderBy(c => c), codes.OrderBy(c => c));
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
