using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Notifications;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 157 — editar un aviso de llegada de cliente antes de que llegue, y los avisos (solicitado, modificado, cancelado) que publica.</summary>
public sealed class AsnUpdateTests
{
    private sealed record World(WmsFixture F, Warehouse W1, Warehouse W2, Teikem.Domain.Clients.Client Mine, Teikem.Domain.Clients.Client Other, Product P, Product Q, Product Foreign, AsnService Svc);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<INotificationEventProvider, AsnEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<AsnService>();
        });
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var mine = await f.AddClientAsync("MIO");
        var other = await f.AddClientAsync("OTRO");
        var p = await f.AddProductAsync("MED", ownerClientId: mine.ClientId);
        var q = await f.AddProductAsync("VIT", ownerClientId: mine.ClientId);
        var foreign = await f.AddProductAsync("AJENO", ownerClientId: other.ClientId);
        return new World(f, w1, w2, mine, other, p, q, foreign, f.Get<AsnService>());
    }

    private static Task<List<NotificationOutbox>> OutboxAsync(WmsFixture f, string code)
        => f.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == code).ToListAsync();

    [Fact]
    public async Task An_expected_asn_changes_warehouse_reference_date_and_lines_before_it_arrives()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var asn = await x.Svc.CreateAsync(new AsnCreateRequest(x.W1.PublicId, x.Mine.PublicId, "OC-1", Lines: new[] { new AsnLineRequest(x.P.PublicId, 5m) }), default);
        Assert.Single(await OutboxAsync(x.F, AsnEventCodes.Requested));

        var edited = await x.Svc.UpdateAsync(asn.Id, new AsnUpdateRequest(x.W2.PublicId, "OC-2", new DateOnly(2026, 12, 1),
            new[] { new AsnLineRequest(x.Q.PublicId, 9m) }), default);

        Assert.Equal(("W2", "OC-2", new DateOnly(2026, 12, 1)), (edited.WarehouseCode, edited.Reference, edited.ExpectedDate));
        var line = Assert.Single(edited.Lines);
        Assert.Equal(("VIT", 9m), (line.Sku, line.ExpectedQty));
        Assert.Single(await OutboxAsync(x.F, AsnEventCodes.Updated));
    }

    [Fact]
    public async Task Fields_left_out_do_not_change_and_an_empty_reference_clears_it()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var asn = await x.Svc.CreateAsync(new AsnCreateRequest(x.W1.PublicId, x.Mine.PublicId, "OC-1", Lines: new[] { new AsnLineRequest(x.P.PublicId, 5m) }), default);
        var same = await x.Svc.UpdateAsync(asn.Id, new AsnUpdateRequest(ExpectedDate: new DateOnly(2026, 12, 5)), default);
        Assert.Equal(("W1", "OC-1", 5m), (same.WarehouseCode, same.Reference, same.Lines[0].ExpectedQty));
        var cleared = await x.Svc.UpdateAsync(asn.Id, new AsnUpdateRequest(Reference: ""), default);
        Assert.Null(cleared.Reference);
    }

    [Fact]
    public async Task A_cancelled_asn_or_one_with_another_clients_product_cannot_be_edited()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var asn = await x.Svc.CreateAsync(new AsnCreateRequest(x.W1.PublicId, x.Mine.PublicId, Lines: new[] { new AsnLineRequest(x.P.PublicId, 5m) }), default);
        await Assert.ThrowsAsync<ValidationException>(() => x.Svc.UpdateAsync(asn.Id, new AsnUpdateRequest(Lines: new[] { new AsnLineRequest(x.Foreign.PublicId, 1m) }), default));

        await x.Svc.CancelAsync(asn.Id, default);
        Assert.Single(await OutboxAsync(x.F, AsnEventCodes.Cancelled));
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.UpdateAsync(asn.Id, new AsnUpdateRequest(Reference: "tarde"), default));
        Assert.Equal(ReceiptRules.AsnNotExpected, ex.Message);
    }

    [Fact]
    public async Task The_expiry_travels_with_the_lot_and_is_refused_without_one()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var lotProduct = await x.F.AddProductAsync("LOTE-1", TrackingTypes.Lot, ownerClientId: x.Mine.ClientId);
        var asn = await x.Svc.CreateAsync(new AsnCreateRequest(x.W1.PublicId, x.Mine.PublicId,
            Lines: new[] { new AsnLineRequest(lotProduct.PublicId, 4m, "L-9", ExpiryDate: new DateOnly(2027, 3, 1)) }), default);
        var line = Assert.Single(asn.Lines);
        Assert.Equal(("LOT", "L-9", new DateOnly(2027, 3, 1)), (line.TrackingType, line.LotNumber, line.ExpiryDate));

        // un vencimiento sin lote no se acepta
        await Assert.ThrowsAsync<ValidationException>(() => x.Svc.UpdateAsync(asn.Id,
            new AsnUpdateRequest(Lines: new[] { new AsnLineRequest(lotProduct.PublicId, 4m, ExpiryDate: new DateOnly(2027, 3, 1)) }), default));
    }
}
