using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P5): aislamiento entre compañías. La orden, el adjunto, el contacto y la modificación de OTRA compañía responden 404 sin decir si existen (el TenantId
// sale del contexto, nunca del request); la bandeja «Por aprobar» y la fuente SALES_ORDER_CHANGE solo devuelven filas de la compañía actual.
public sealed class SalesOrderChangeTenantIsolationTests
{
    /// <summary>Una orden de la OTRA compañía (escrita a mano: InMemory sin FK) con una modificación pendiente y un archivo suyo.</summary>
    private static async Task<(SalesOrder Order, SalesOrderChange Change, StoredFile File)> ForeignAsync(SalesChangeWorld x, int ourOrderId)
    {
        var order = new SalesOrder
        {
            SalesOrderId = 77001, PublicId = Guid.NewGuid(), TenantId = WmsFixture.OtherTenantId, Number = "OV-99999", WarehouseId = x.W.WarehouseId, ClientId = x.C.ClientId,
            StatusCodeId = x.F.StatusId(SalesCodes.StatusDomain, SalesOrderStatuses.Requested), RequestedDate = TenantClock.Default.Today,
        };
        var change = new SalesOrderChange
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.OtherTenantId, SalesOrderId = order.SalesOrderId, ChangeNo = 1, Kind = SalesChangeKinds.Modify, StatusCodeId = 9101,
            IsPending = true, RegisteredBy = 5, RegisteredAtUtc = DateTime.UtcNow, AuthorizedByName = "Otra", AuthorizationChannelLookupId = 9002, AuthorizedAtUtc = DateTime.UtcNow,
            Reason = "De otra compañía", BaseFingerprint = new string('0', 64), TotalDelta = 1m,
        };
        // un archivo de la otra compañía que dice ser de NUESTRA orden (el filtro de compañía lo esconde)
        var file = new StoredFile
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.OtherTenantId, OwnerEntityLookupId = x.F.LookupId(LookupDomains.EntityType, SalesCodes.EntityType), OwnerId = ourOrderId,
            Kind = StoredFileCodes.KindDocument, FileName = "ajeno.png", ContentType = "image/png", SizeBytes = 8, Sha256 = new string('a', 64), StorageKey = "x", IsActive = true,
        };
        x.F.Db.Set<SalesOrder>().Add(order);
        x.F.Db.Set<SalesOrderChange>().Add(change);
        x.F.Db.Set<StoredFile>().Add(file);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return (order, change, file);
    }

    [Fact]
    public async Task The_order_attachment_contact_and_change_of_another_company_are_404_without_an_oracle()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var ourId = await x.IdOfAsync(o.PublicId);
        var (foreign, foreignChange, foreignFile) = await ForeignAsync(x, ourId);
        var svc = ChangeKit.Service(x);
        const string orderNotFound = "Orden de venta no encontrada.";

        Assert.Equal(orderNotFound, (await Assert.ThrowsAsync<NotFoundException>(() => svc.EditOptionsAsync(foreign.PublicId, default))).Message);
        Assert.Equal(orderNotFound, (await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAsync(foreign.PublicId, ChangeKit.Req(ChangeKit.Qty(1, 2m)), default))).Message);
        Assert.Equal(orderNotFound, (await Assert.ThrowsAsync<NotFoundException>(() => svc.ListAsync(foreign.PublicId, default))).Message);
        Assert.Equal(orderNotFound, (await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAdditionAsync(foreign.PublicId, ChangeKit.Addition((x.P2, 1m)), default))).Message);
        // una orden que no existe responde lo mismo
        Assert.Equal(orderNotFound, (await Assert.ThrowsAsync<NotFoundException>(() => svc.EditOptionsAsync(Guid.NewGuid(), default))).Message);

        // la modificación de la otra compañía, pedida por nuestra orden
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(o.PublicId, foreignChange.PublicId, default));
        x.F.Tenant.UserId = 2;
        await Assert.ThrowsAsync<NotFoundException>(() => svc.ApproveAsync(o.PublicId, foreignChange.PublicId, null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.RejectAsync(o.PublicId, foreignChange.PublicId, new SalesOrderChangeDecisionRequest("no"), default));
        x.F.Tenant.UserId = 1;

        // el archivo ajeno (aunque diga ser de nuestra orden) y el contacto de otro cliente
        var file = await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAsync(o.PublicId,
            ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)) with { AttachmentFilePublicId = foreignFile.PublicId }, default));
        Assert.Equal("Archivo no encontrado.", file.Message);
        var otherClient = await x.F.AddClientAsync("C2");
        x.F.Db.Set<ClientContact>().Add(new ClientContact { ClientContactId = 88001, ClientId = otherClient.ClientId, FullName = "Pedro", IsActive = true });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var contact = await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAsync(o.PublicId,
            ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)) with { AuthorizedByContactId = 88001 }, default));
        Assert.Equal("Contacto no encontrado.", contact.Message);

        Assert.Empty(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().Where(c => c.TenantId == WmsFixture.TenantId).ToListAsync());
        Assert.Equal(3m, (await x.Svc.GetAsync(o.PublicId, default)).Lines[0].QtyOrdered);
    }

    [Fact]
    public async Task The_pending_tray_and_the_data_source_only_return_rows_of_the_company()
    {
        await using var x = await ChangeKit.WorldAsync();
        await ChangeKit.SetTenantAsync(x.F, SalesChangeApprovalModes.Always);
        var o = await x.RequestedAsync((x.P1, 3m));
        var ours = await ChangeKit.Service(x).CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)), default);
        var (_, foreignChange, _) = await ForeignAsync(x, await x.IdOfAsync(o.PublicId));

        var tray = await ChangeKit.Service(x).PendingApprovalsAsync(null, default);
        Assert.Equal(ours.Item.PublicId, Assert.Single(tray.Items).PublicId);
        Assert.Equal(1, tray.Total);

        var rows = await new SalesOrderChangeDataSource(x.F.Db, x.F.Lookups, x.F.Tenant).LoadAsync(new DataQuery(), default);
        Assert.Equal(ours.Item.PublicId.ToString(), Assert.Single(rows)["PublicId"]);
        Assert.DoesNotContain(rows, r => (string?)r["PublicId"] == foreignChange.PublicId.ToString());
    }
}
