using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;
using TripEntity = Teikem.Domain.Trips.Trip;

namespace Teikem.Tests;

/// <summary>
/// Verificación del Lote 41 con varias compañías y volumen sintético (2026-10-12): la sección «2026-10-12 (b)» de
/// logistica-db-update.sql agrega, al final e idempotente, los índices que dejan buscar sin recorrer TransportOrder ni Trip
/// (por PublicId, saldo de crédito del cliente, búsqueda exacta del escaneo y rutas de un día); solo agrega (sin DROP ni DELETE) y el modelo EF
/// los declara con el mismo nombre, columnas, INCLUDE y filtro.
/// </summary>
public sealed class VolumeIndexesTests
{
    private const string SectionTitle = "2026-10-12 (b) — Índices para buscar sin recorrer TransportOrder ni Trip";

    private static string UpdateSql() =>
        File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    [Fact]
    public void Update_sql_adds_the_indexes_in_a_new_idempotent_section_at_the_end()
    {
        var sql = UpdateSql();
        var start = sql.IndexOf(SectionTitle, StringComparison.Ordinal);
        Assert.True(start > 0, "Falta la sección 2026-10-12 (b) en logistica-db-update.sql.");
        Assert.True(start > sql.IndexOf("2026-10-12 (a) — Ver precios de las órdenes", StringComparison.Ordinal), "La sección debe ir después de la (a).");
        var section = sql[start..];

        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransportOrder_PublicId' AND object_id = OBJECT_ID('dbo.TransportOrder'))\n    CREATE INDEX IX_TransportOrder_PublicId ON dbo.TransportOrder(PublicId);", section);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Trip_PublicId' AND object_id = OBJECT_ID('dbo.Trip'))\n    CREATE INDEX IX_Trip_PublicId ON dbo.Trip(PublicId);", section);
        Assert.Contains("CREATE INDEX IX_Order_Client_Balance ON dbo.TransportOrder(ClientId, StatusCodeId) INCLUDE (TenantId, QuotedAmount)\n        WHERE IsActive = 1 AND QuotedAmount IS NOT NULL;", section);
        Assert.Contains("CREATE INDEX IX_Order_OrderNumber ON dbo.TransportOrder(OrderNumber) WHERE IsActive = 1;", section);
        Assert.Contains("CREATE INDEX IX_Order_PackBatchNumber ON dbo.TransportOrder(PackBatchNumber) WHERE IsActive = 1;", section);
        Assert.Contains("CREATE INDEX IX_Order_ClientInvoiceNumber ON dbo.TransportOrder(ClientInvoiceNumber) WHERE IsActive = 1;", section);
        Assert.Contains("CREATE INDEX IX_Trip_PlanDate ON dbo.Trip(PlanDate) INCLUDE (TenantId, DriverId, VehicleId, DispatchZoneId, StatusCodeId)\n        WHERE IsActive = 1;", section);

        // Cada CREATE INDEX lleva su guarda (idempotente) y la sección solo agrega.
        var creates = section.Split('\n').Count(l => l.TrimStart().StartsWith("CREATE INDEX", StringComparison.Ordinal));
        var guards = section.Split('\n').Count(l => l.StartsWith("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = ", StringComparison.Ordinal));
        Assert.Equal(7, creates);
        Assert.Equal(creates, guards);
        Assert.DoesNotContain("DROP ", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNIQUE", section, StringComparison.Ordinal);
    }

    public static TheoryData<Type, string, string[], string[], string?> Indexes() => new()
    {
        { typeof(TransportOrder), "IX_TransportOrder_PublicId", new[] { nameof(TransportOrder.PublicId) }, Array.Empty<string>(), null },
        { typeof(TripEntity), "IX_Trip_PublicId", new[] { nameof(TripEntity.PublicId) }, Array.Empty<string>(), null },
        { typeof(TransportOrder), "IX_Order_Client_Balance", new[] { nameof(TransportOrder.ClientId), nameof(TransportOrder.StatusCodeId) },
            new[] { nameof(TransportOrder.TenantId), nameof(TransportOrder.QuotedAmount) }, "[IsActive] = 1 AND [QuotedAmount] IS NOT NULL" },
        { typeof(TransportOrder), "IX_Order_OrderNumber", new[] { nameof(TransportOrder.OrderNumber) }, Array.Empty<string>(), "[IsActive] = 1" },
        { typeof(TransportOrder), "IX_Order_PackBatchNumber", new[] { nameof(TransportOrder.PackBatchNumber) }, Array.Empty<string>(), "[IsActive] = 1" },
        { typeof(TransportOrder), "IX_Order_ClientInvoiceNumber", new[] { nameof(TransportOrder.ClientInvoiceNumber) }, Array.Empty<string>(), "[IsActive] = 1" },
        { typeof(TripEntity), "IX_Trip_PlanDate", new[] { nameof(TripEntity.PlanDate) },
            new[] { nameof(TripEntity.TenantId), nameof(TripEntity.DriverId), nameof(TripEntity.VehicleId), nameof(TripEntity.DispatchZoneId), nameof(TripEntity.StatusCodeId) }, "[IsActive] = 1" },
    };

    [Theory]
    [MemberData(nameof(Indexes))]
    public void Ef_model_mirrors_each_index(Type clr, string name, string[] columns, string[] include, string? filter)
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var model = db.GetService<IDesignTimeModel>().Model; // el INCLUDE solo vive en el modelo de diseño
        var index = model.FindEntityType(clr)!.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == name);
        Assert.NotNull(index);
        Assert.False(index!.IsUnique);
        Assert.Equal(columns, index.Properties.Select(p => p.Name));
        Assert.Equal(include, index.GetIncludeProperties()?.ToArray() ?? Array.Empty<string>());
        Assert.Equal(filter, index.GetFilter());
    }

    [Fact]
    public void Balance_index_filter_matches_the_balance_query_so_no_other_query_can_use_it()
    {
        // El índice del saldo lleva el mismo filtro que la consulta del saldo (QuotedOrdersBalanceProvider: IsActive y
        // QuotedAmount != null): así el optimizador no lo elige para los COUNT de la lista de órdenes (medido: los volvía
        // seriales y más lentos).
        var provider = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "src", "Teikem.Infrastructure", "Services", "QuotedOrdersBalanceProvider.cs"));
        Assert.Contains("o.ClientId == clientId && o.IsActive && o.QuotedAmount != null", provider);
        Assert.Contains("WHERE IsActive = 1 AND QuotedAmount IS NOT NULL;", UpdateSql());
    }
}
