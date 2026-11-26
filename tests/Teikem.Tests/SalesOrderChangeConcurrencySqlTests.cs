using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 249 (P5) — concurrencia de las modificaciones contra SQL Server REAL (se omite sin TEIKEM_TEST_SQLSERVER), sobre la base desechable de
/// <see cref="NotificationSqlFixture"/> (modelo EF, con sus índices únicos filtrados, sin FK). Dos registros SIMULTÁNEOS de una modificación pendiente de la misma
/// orden: uno gana y el otro recibe el 409 del servicio (UX_SalesOrderChange_Pending es la red); dos modificaciones con el mismo adjunto: igual
/// (UX_SalesOrderChange_Attachment); y el candado de la cabecera (UPDLOCK, con TenantId) que toman TODOS los escritores de la orden hace esperar al segundo.
/// </summary>
public sealed class SalesOrderChangeConcurrencySqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    private static TenantContext Ctx(int tenantId, int userId = 1) => new() { TenantId = tenantId, UserId = userId, IsAuthenticated = true };

    private async Task<(int TenantId, int OrderId)> OrderAsync(string tenantName)
    {
        var tenantId = await fx.NewTenantAsync(tenantName);
        await using var db = fx.NewDb(Ctx(tenantId));
        var o = new SalesOrder
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, Number = "OV-" + Guid.NewGuid().ToString("N")[..8], WarehouseId = 1, ClientId = 1, StatusCodeId = 1,
            RequestedDate = new DateOnly(2026, 11, 27), CreatedAtUtc = DateTime.UtcNow,
        };
        db.Set<SalesOrder>().Add(o);
        await db.SaveChangesAsync();
        return (tenantId, o.SalesOrderId);
    }

    private static SalesOrderChange Change(int tenantId, int orderId, int changeNo, bool pending, int? attachment = null) => new()
    {
        PublicId = Guid.NewGuid(), TenantId = tenantId, SalesOrderId = orderId, ChangeNo = changeNo, Kind = SalesChangeKinds.Modify, StatusCodeId = 1, IsPending = pending,
        RegisteredBy = 1, RegisteredAtUtc = DateTime.UtcNow, AuthorizedByName = "María", AuthorizationChannelLookupId = 1, AuthorizedAtUtc = DateTime.UtcNow,
        Reason = "Carrera", BaseFingerprint = new string('0', 64), AttachmentFileId = attachment, CreatedAtUtc = DateTime.UtcNow,
    };

    /// <summary>Corre las dos escrituras a la vez y devuelve lo que pasó en cada una (null = se guardó).</summary>
    private static async Task<List<Exception?>> RaceAsync(Func<Task> first, Func<Task> second)
    {
        using var start = new ManualResetEventSlim(false);
        var t1 = Task.Run(async () => { start.Wait(); await first(); });
        var t2 = Task.Run(async () => { start.Wait(); await second(); });
        start.Set();
        var outcomes = new List<Exception?>();
        foreach (var t in new[] { t1, t2 })
        {
            try { await t; outcomes.Add(null); }
            catch (Exception ex) { outcomes.Add(ex); }
        }
        return outcomes;
    }

    [SqlServerFact]
    public async Task Two_simultaneous_pending_changes_of_the_same_order_leave_one_and_the_other_gets_409()
    {
        var (tenantId, orderId) = await OrderAsync("Compañía modificaciones (carrera de pendientes)");
        for (var round = 0; round < 5; round++)
        {
            var no = round * 2 + 1;
            await using var db1 = fx.NewDb(Ctx(tenantId));
            await using var db2 = fx.NewDb(Ctx(tenantId));
            db1.Set<SalesOrderChange>().Add(Change(tenantId, orderId, no, pending: true));
            db2.Set<SalesOrderChange>().Add(Change(tenantId, orderId, no + 1, pending: true));

            var outcomes = await RaceAsync(() => db1.SaveGuardedAsync(SalesOrderChangeService.PendingExistsGeneric, default),
                () => db2.SaveGuardedAsync(SalesOrderChangeService.PendingExistsGeneric, default));

            Assert.Single(outcomes, o => o is null);
            var conflict = Assert.IsType<ConflictException>(Assert.Single(outcomes, o => o is not null));
            Assert.Equal(SalesOrderChangeService.PendingExistsGeneric, conflict.Message);
            await using var check = fx.NewDb(Ctx(tenantId));
            Assert.Equal(1, await check.Set<SalesOrderChange>().CountAsync(c => c.SalesOrderId == orderId && c.IsPending));
            // la pendiente se decide (deja de serlo) y la siguiente vuelta puede registrar otra
            await check.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.SalesOrderChange SET IsPending = 0 WHERE SalesOrderId = {orderId}");
        }
    }

    [SqlServerFact]
    public async Task Two_simultaneous_changes_with_the_same_attachment_leave_one()
    {
        var (tenantId, orderId) = await OrderAsync("Compañía modificaciones (carrera de adjuntos)");
        await using var db1 = fx.NewDb(Ctx(tenantId));
        await using var db2 = fx.NewDb(Ctx(tenantId));
        db1.Set<SalesOrderChange>().Add(Change(tenantId, orderId, 1, pending: false, attachment: 4242));
        db2.Set<SalesOrderChange>().Add(Change(tenantId, orderId, 2, pending: false, attachment: 4242));

        var outcomes = await RaceAsync(() => db1.SaveGuardedAsync(SalesOrderChangeService.AttachmentUsed, default),
            () => db2.SaveGuardedAsync(SalesOrderChangeService.AttachmentUsed, default));

        Assert.Single(outcomes, o => o is null);
        Assert.Equal(SalesOrderChangeService.AttachmentUsed, Assert.IsType<ConflictException>(Assert.Single(outcomes, o => o is not null)).Message);
    }

    [SqlServerFact]
    public async Task The_header_lock_makes_the_second_writer_wait_and_only_locks_rows_of_the_company()
    {
        var (tenantId, orderId) = await OrderAsync("Compañía modificaciones (candado de la cabecera)");
        var otherTenant = await fx.NewTenantAsync("Otra compañía (candado de la cabecera)");

        await using var first = fx.NewDb(Ctx(tenantId));
        await using var tx = await first.Database.BeginTransactionAsync();
        var locked = await first.LockSalesOrderAsync(orderId, default);
        Assert.Equal(orderId, locked.SalesOrderId);

        // el segundo escritor espera el candado (con un tope de espera corto: 1222 = se acabó el tiempo de espera del candado)
        await using var second = fx.NewDb(Ctx(tenantId));
        await using (var tx2 = await second.Database.BeginTransactionAsync())
        {
            await second.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 300;");
            var e = await Assert.ThrowsAnyAsync<Exception>(() => second.LockSalesOrderAsync(orderId, default));
            var sql = e as SqlException ?? e.InnerException as SqlException;
            Assert.Equal(1222, sql?.Number);
        }

        await tx.RollbackAsync();

        // la orden de otra compañía no se encuentra (el candado lleva TenantId): 404 sin oráculo
        await using var foreign = fx.NewDb(Ctx(otherTenant));
        await using (var tx3 = await foreign.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<NotFoundException>(() => foreign.LockSalesOrderAsync(orderId, default));
    }
}
