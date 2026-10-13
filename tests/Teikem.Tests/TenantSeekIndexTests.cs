using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 42 — con el filtro de tenant buscable, la sección «2026-10-12 (c)» de logistica-db-update.sql agrega (al final, idempotente)
/// IX_Order_Tenant_Created para la lista de órdenes por compañía, y el modelo EF lo declara igual. La lista con texto libre sobre las
/// paradas (búsqueda o consignatario) ordena por una expresión EQUIVALENTE que el índice no entrega ya ordenada (si no, SQL Server
/// recorre toda la compañía en orden probando orden por orden: medido 1,4 s → 5,5 s); el orden y los resultados no cambian.
/// </summary>
public sealed class TenantSeekIndexTests
{
    private const string SectionTitle = "2026-10-12 (c) — Índice de la lista de órdenes por compañía";

    private static string UpdateSql() =>
        File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    [Fact]
    public void Update_sql_adds_the_order_list_index_in_a_new_idempotent_section_after_b()
    {
        var sql = UpdateSql();
        var start = sql.IndexOf(SectionTitle, StringComparison.Ordinal);
        Assert.True(start > 0, "Falta la sección 2026-10-12 (c) en logistica-db-update.sql.");
        Assert.True(start > sql.IndexOf("2026-10-12 (b) — Índices para buscar sin recorrer TransportOrder ni Trip", StringComparison.Ordinal), "La sección debe ir después de la (b).");
        var next = sql.IndexOf("\n/* ----", start + SectionTitle.Length + 200, StringComparison.Ordinal);
        var section = next > 0 ? sql[start..next] : sql[start..];

        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Order_Tenant_Created' AND object_id = OBJECT_ID('dbo.TransportOrder'))\n"
                        + "    CREATE INDEX IX_Order_Tenant_Created ON dbo.TransportOrder(TenantId, CreatedAtUtc DESC, TransportOrderId DESC)\n"
                        + "        INCLUDE (ClientId, StatusCodeId, ServiceTypeLookupId, OrderNumber, ClientInvoiceNumber, PackBatchNumber) WHERE IsActive = 1;\nGO", section);
        var lines = section.Split('\n');
        Assert.Equal(1, lines.Count(l => l.TrimStart().StartsWith("CREATE INDEX", StringComparison.Ordinal)));
        Assert.Equal(1, lines.Count(l => l.StartsWith("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = ", StringComparison.Ordinal)));
        Assert.DoesNotContain("DROP ", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNIQUE", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Ef_model_mirrors_the_index_with_descending_keys_include_and_filter()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var model = db.GetService<IDesignTimeModel>().Model; // el INCLUDE solo vive en el modelo de diseño
        var index = model.FindEntityType(typeof(TransportOrder))!.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == "IX_Order_Tenant_Created");
        Assert.NotNull(index);
        Assert.False(index!.IsUnique);
        Assert.Equal(new[] { nameof(TransportOrder.TenantId), nameof(TransportOrder.CreatedAtUtc), nameof(TransportOrder.TransportOrderId) }, index.Properties.Select(p => p.Name));
        Assert.Equal(new[] { false, true, true }, index.IsDescending);
        Assert.Equal(new[] { nameof(TransportOrder.ClientId), nameof(TransportOrder.StatusCodeId), nameof(TransportOrder.ServiceTypeLookupId),
            nameof(TransportOrder.OrderNumber), nameof(TransportOrder.ClientInvoiceNumber), nameof(TransportOrder.PackBatchNumber) }, index.GetIncludeProperties());
        Assert.Equal("[IsActive] = 1", index.GetFilter());
    }

    [Fact]
    public void The_free_text_order_reaches_sql_as_an_expression_the_index_cannot_satisfy()
    {
        // Si una versión de EF simplificara «+ 0 ms», la búsqueda volvería al plan lento: esta prueba lo avisa.
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var sql = db.TransportOrders.Where(o => o.IsActive)
            .OrderByDescending(o => o.CreatedAtUtc.AddMilliseconds(0)).ThenByDescending(o => o.TransportOrderId).Skip(0).Take(100)
            .ToQueryString();
        Assert.Contains("ORDER BY DATEADD(millisecond, CAST(0.0E0 AS int), [t].[CreatedAtUtc]) DESC, [t].[TransportOrderId] DESC", sql);
        var service = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "src", "Teikem.Infrastructure", "Services", "OrderReadService.cs"));
        Assert.Contains("var freeTextOnStops = !string.IsNullOrWhiteSpace(q.Search) || !string.IsNullOrWhiteSpace(q.Consignee);", service);
        Assert.Contains("query.OrderByDescending(o => o.CreatedAtUtc.AddMilliseconds(0)).ThenByDescending(o => o.TransportOrderId)", service);
    }

    [Fact]
    public async Task Search_and_consignee_keep_exactly_the_order_and_page_of_the_plain_list()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        // empate de fecha entre dos órdenes: desempata TransportOrderId descendente, con y sin texto libre
        var tie = await f.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == f.O3.TransportOrderId);
        tie.CreatedAtUtc = (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == f.O2.TransportOrderId)).CreatedAtUtc;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        static OrderListQuery Q(string? consignee = null, string? search = null, int skip = 0, int take = 100)
            => new(null, null, null, null, null, consignee, search, null, null, false, skip, take);
        async Task<int[]> Ids(OrderListQuery q)
        {
            var page = await f.Reader.GetListAsync(q, OrderScope.Any, default);
            return page.Items.Select(i => i.Id).ToArray();
        }

        var plain = await Ids(Q());
        Assert.Equal(new[] { 4, 3, 2, 1 }, plain);   // 3 y 2 empatan en fecha: primero el id mayor
        Assert.Equal(plain, await Ids(Q(search: "Consignatario")));
        Assert.Equal(plain, await Ids(Q(consignee: "Consignatario")));
        Assert.Equal(plain[1..3], await Ids(Q(search: "Consignatario", skip: 1, take: 2)));
        Assert.Equal(new[] { 3 }, await Ids(Q(search: "Consignatario 3")));
    }
}
