using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Accounting;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 181 — la búsqueda de lo contabilizable (uniones y subconsulta «no está ya en una corrida viva») y el índice único filtrado por origen contra SQL Server REAL: InMemory no prueba ninguna de las dos
/// cosas. Base desechable (NotificationSqlFixture); se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class AccountingRunSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    [Fact]
    public async Task Preview_translates_to_sql_and_a_source_line_cannot_be_in_two_live_runs()
    {
        if (fx.ConnectionString is null) return;
        var tenantId = await fx.NewTenantAsync("accounting-sql");
        var now = DateTime.UtcNow;
        var today = TenantClock.Default.Today;
        int lineId;
        await using (var seed = fx.NewDb(tenantId))
        {
            var own = new Product { PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = "P-1", Name = "Propio", IsActive = true };
            var theirs = new Product { PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = "P-2", Name = "De cliente", ClientId = 1, IsActive = true };
            seed.Set<Product>().AddRange(own, theirs);
            await seed.SaveChangesAsync();
            var receipt = new ReceiptHeader { PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = 1, ReceiptTypeLookupId = 1, Number = "REC-00001", StatusCodeId = 1, ReceivedAtUtc = now, IsActive = true };
            receipt.Lines.Add(new ReceiptLine { ProductId = own.ProductId, ReceivedQty = 10m, UnitCost = 5m, CostSource = "PO" });
            receipt.Lines.Add(new ReceiptLine { ProductId = theirs.ProductId, ReceivedQty = 3m, UnitCost = 9m, CostSource = "PRODUCT" });
            seed.Set<ReceiptHeader>().Add(receipt);
            await seed.SaveChangesAsync();
            lineId = receipt.Lines.First(l => l.ProductId == own.ProductId).ReceiptLineId;
        }

        var tenant = new TenantContext { TenantId = tenantId };
        await using var db = fx.NewDb(tenant);
        var svc = new AccountingRunService(db, tenant, null!, null!, null!, TenantClock.Default, null!);
        var preview = await svc.PreviewAsync(AccountingCodes.KindPurchases, new AccountingRunRequest("X", today.AddDays(-1), today), default);
        var line = Assert.Single(preview.Lines);
        Assert.Equal(("P-1", 10m, 5m, 50m, "REC-00001"), (line.Sku, line.Quantity, line.UnitCost, line.TotalCost, line.DocumentNumber));

        // la línea entra en una corrida viva: ya no se ofrece, y el índice único impide meterla en otra
        var run = new AccountingRun { PublicId = Guid.NewGuid(), TenantId = tenantId, Kind = AccountingCodes.KindPurchases, Number = "CMP-00001", PeriodStart = today, PeriodEnd = today, StatusCodeId = 1, CurrencyLookupId = 1 };
        run.Lines.Add(new AccountingRunLine { TenantId = tenantId, SourceType = AccountingCodes.SourceReceiptLine, SourceId = lineId, DocDate = today, DocumentNumber = "REC-00001", ProductId = 1, Sku = "P-1", ProductName = "Propio", Quantity = 10m });
        db.Set<AccountingRun>().Add(run);
        await db.SaveChangesAsync();
        Assert.Equal(0, (await svc.PreviewAsync(AccountingCodes.KindPurchases, new AccountingRunRequest("X", today.AddDays(-1), today), default)).LineCount);

        var second = new AccountingRun { PublicId = Guid.NewGuid(), TenantId = tenantId, Kind = AccountingCodes.KindPurchases, Number = "CMP-00002", PeriodStart = today, PeriodEnd = today, StatusCodeId = 1, CurrencyLookupId = 1 };
        second.Lines.Add(new AccountingRunLine { TenantId = tenantId, SourceType = AccountingCodes.SourceReceiptLine, SourceId = lineId, DocDate = today, DocumentNumber = "REC-00001", ProductId = 1, Sku = "P-1", ProductName = "Propio", Quantity = 10m });
        db.Set<AccountingRun>().Add(second);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // al cancelar la primera sus líneas quedan inactivas y la línea vuelve a ofrecerse y a poder entrar en otra corrida
        db.ChangeTracker.Clear();
        foreach (var l in await db.Set<AccountingRunLine>().AsTracking().Where(l => l.AccountingRunId == run.AccountingRunId).ToListAsync()) l.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(1, (await svc.PreviewAsync(AccountingCodes.KindPurchases, new AccountingRunRequest("X", today.AddDays(-1), today), default)).LineCount);
        db.Set<AccountingRun>().Add(new AccountingRun
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, Kind = AccountingCodes.KindPurchases, Number = "CMP-00003", PeriodStart = today, PeriodEnd = today, StatusCodeId = 1, CurrencyLookupId = 1,
            Lines = { new AccountingRunLine { TenantId = tenantId, SourceType = AccountingCodes.SourceReceiptLine, SourceId = lineId, DocDate = today, DocumentNumber = "REC-00001", ProductId = 1, Sku = "P-1", ProductName = "Propio", Quantity = 10m } },
        });
        await db.SaveChangesAsync();
    }
}
