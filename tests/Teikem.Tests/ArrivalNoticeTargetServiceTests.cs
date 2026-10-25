using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Integrations;
using Teikem.Domain.Notifications;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-23: el destino ARRIVAL_NOTICE crea el aviso de llegada REAL (AsnService) del cliente: producto por su SKU, almacén del lector, un aviso por referencia, y un documento con una línea ilegible o un producto que
/// no existe no deja ningún aviso. (La parte de lectura del documento la prueba NoticeDocumentTargetsTests con el texto real de los PDF.)
/// </summary>
public sealed class ArrivalNoticeTargetServiceTests
{
    private sealed record World(WmsFixture F, Warehouse W, Warehouse Other, Teikem.Domain.Clients.Client Client, ArrivalNoticeDocumentTarget Target);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<INotificationEventProvider, AsnEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<AsnService>();
            s.AddSingleton<ArrivalNoticeDocumentTarget>();
        });
        var w = await f.AddWarehouseAsync("DEPOT");
        var other = await f.AddWarehouseAsync("OTRO");   // la compañía tiene dos almacenes activos: sin almacén en el lector el aviso no podría crearse
        var client = await f.AddClientAsync("AXISCARE");
        await f.AddProductAsync("402-RE-351001-U", ownerClientId: client.ClientId);
        await f.AddProductAsync("402-RE-343002U-S", ownerClientId: client.ClientId);
        return new World(f, w, other, client, f.Get<ArrivalNoticeDocumentTarget>());
    }

    private static DocumentTargetRequest Request(World x, string csv, Guid? warehouse, string file = "envio.pdf.csv")
        => new(new Integration { IntegrationId = 1, TenantId = WmsFixture.TenantId, Name = "Axiscare avisos", ServiceUserId = 1 }, new ImportTemplate { HasHeader = false, Delimiter = "," }, x.Client,
            new IntegrationRun { IntegrationRunId = 1, TenantId = WmsFixture.TenantId, IntegrationId = 1 }, file, Encoding.UTF8.GetBytes(csv), CheckDuplicate: true,
            Reader: new DocumentReader("Envío de mercancía", ReaderSources.Pdf, DocumentTargets.ArrivalNotice, WarehousePublicId: warehouse));

    [Fact]
    public async Task The_shipment_makes_one_arrival_notice_in_the_readers_warehouse_with_the_units()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var outcome = await x.Target.ProcessAsync(Request(x, ",402-RE-351001-U,400bx\n,402-RE-343002U-S,165bx\n", x.W.PublicId), default);

        Assert.Empty(outcome.Rejected);
        Assert.Equal(1, outcome.Log.OrdersCreated);
        var asn = await x.F.Db.Set<Asn>().AsNoTracking().Include(a => a.Lines).SingleAsync();
        Assert.Equal(x.W.WarehouseId, asn.WarehouseId);
        Assert.Equal(x.Client.ClientId, asn.ClientId);
        Assert.Equal(new[] { 400m, 165m }, asn.Lines.OrderBy(l => l.AsnLineId).Select(l => l.ExpectedQty));
        Assert.StartsWith("LLE-", asn.Number);
    }

    [Fact]
    public async Task The_notice_date_is_the_documents_then_the_e_mails_then_the_services()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        // el documento trae fecha (columna 5): manda sobre la del correo
        var withDate = await x.Target.ProcessAsync(Request(x, "REF-A,402-RE-351001-U,10bx,,10/01/2026\n", x.W.PublicId) with { MailDate = new DateOnly(2026, 10, 20) }, default);
        // el documento no trae fecha: se usa la del correo
        var withoutDate = await x.Target.ProcessAsync(Request(x, "REF-B,402-RE-351001-U,10bx\n", x.W.PublicId, "otro.pdf.csv") with { MailDate = new DateOnly(2026, 10, 20) }, default);

        Assert.Empty(withDate.Rejected);
        Assert.Empty(withoutDate.Rejected);
        var asns = await x.F.Db.Set<Asn>().AsNoTracking().OrderBy(a => a.AsnId).ToListAsync();
        Assert.Equal(new DateOnly?(new DateOnly(2026, 10, 1)), asns[0].ExpectedDate);
        Assert.Equal(new DateOnly?(new DateOnly(2026, 10, 20)), asns[1].ExpectedDate);
    }

    [Fact]
    public async Task Two_references_make_two_notices()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var outcome = await x.Target.ProcessAsync(Request(x, "FAC-1,402-RE-351001-U,5\nFAC-2,402-RE-343002U-S,7\nFAC-2,402-RE-351001-U,1\n", x.W.PublicId), default);
        Assert.Equal(2, outcome.Created.Count);
        Assert.Equal(new[] { "FAC-1", "FAC-2" }, (await x.F.Db.Set<Asn>().AsNoTracking().OrderBy(a => a.AsnId).ToListAsync()).Select(a => a.Reference));
    }

    [Fact]
    public async Task An_unknown_product_or_an_unreadable_quantity_leaves_no_notice_at_all()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var unknown = await x.Target.ProcessAsync(Request(x, ",402-RE-351001-U,5\n,NO-EXISTE,2\n", x.W.PublicId), default);
        Assert.Empty(unknown.Created);
        Assert.Equal(NoticeRowRules.SkuUnknown("NO-EXISTE"), Assert.Single(unknown.Rejected).Reason);

        var unreadable = await x.Target.ProcessAsync(Request(x, ",402-RE-351001-U,5\n,402-RE-343002U-S,\n", x.W.PublicId, "otro.csv"), default);
        Assert.Empty(unreadable.Created);
        Assert.Contains("cantidad", Assert.Single(unreadable.Rejected).Reason);
        Assert.Empty(await x.F.Db.Set<Asn>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task With_two_active_warehouses_and_none_on_the_reader_the_notice_goes_to_the_tray_with_the_reason()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var outcome = await x.Target.ProcessAsync(Request(x, ",402-RE-351001-U,5\n", warehouse: null), default);
        Assert.Empty(outcome.Created);
        var rejected = Assert.Single(outcome.Rejected);
        Assert.Contains("Aviso de llegada", rejected.Reason);
    }

    [Fact]
    public async Task The_same_document_content_is_not_processed_twice()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var first = await x.Target.ProcessAsync(Request(x, ",402-RE-351001-U,5\n", x.W.PublicId), default);
        Assert.Single(first.Created);
        x.F.Db.IntegrationFileLogs.Add(first.Log);
        await x.F.Db.SaveChangesAsync();
        var second = await x.Target.ProcessAsync(Request(x, ",402-RE-351001-U,5\n", x.W.PublicId), default);
        Assert.Empty(second.Created);
        Assert.Equal(IntegrationFileResults.Duplicate, second.Log.Result);
    }
}
