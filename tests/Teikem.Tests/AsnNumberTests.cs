using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Notifications;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 170 (A5, 2026-10-21) — el aviso de llegada tiene número LLE-##### por compañía: lo asigna el contador ASN al crearlo, sale en el aviso de notificación, se busca por él y la base lo lleva (sección del SQL).</summary>
public sealed class AsnNumberTests
{
    private sealed record World(WmsFixture F, Warehouse W, Teikem.Domain.Clients.Client Mine, Product P, AsnService Svc);

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
        var w = await f.AddWarehouseAsync("W1");
        var mine = await f.AddClientAsync("MIO");
        var p = await f.AddProductAsync("MED", ownerClientId: mine.ClientId);
        return new World(f, w, mine, p, f.Get<AsnService>());
    }

    private static AsnCreateRequest Req(World x, string reference) => new(x.W.PublicId, x.Mine.PublicId, reference, Lines: new[] { new AsnLineRequest(x.P.PublicId, 5m) });

    [Fact]
    public void The_counter_is_known_and_the_pattern_is_LLE()
    {
        Assert.True(NumberingRules.IsKnownKind(AsnCodes.NumberKind));
        Assert.Equal(("ASN", "LLE-#####"), (AsnCodes.NumberKind, AsnCodes.NumberPattern));
    }

    [Fact]
    public async Task Each_new_asn_gets_the_next_number_in_order()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var a = await x.Svc.CreateAsync(Req(x, "OC-1"), default);
        var b = await x.Svc.CreateAsync(Req(x, "OC-2"), default);
        Assert.Equal(("LLE-00001", "LLE-00002"), (a.Number, b.Number));
        Assert.Equal("LLE-00002", (await x.Svc.GetAsync(b.Id, InventoryScope.Any, default)).Number);
    }

    [Fact]
    public async Task The_notification_carries_the_number_and_not_the_internal_id()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var a = await x.Svc.CreateAsync(Req(x, "OC-1"), default);
        var outbox = await x.F.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == AsnEventCodes.Requested).ToListAsync();
        var payload = Assert.Single(outbox).PayloadJson;
        Assert.Contains("LLE-00001", payload);
        Assert.DoesNotContain($"#{a.Id}", payload);
    }

    [Fact]
    public async Task The_list_search_finds_an_asn_by_its_number()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await x.Svc.CreateAsync(Req(x, "OC-1"), default);
        var second = await x.Svc.CreateAsync(Req(x, "OC-2"), default);
        var found = await x.Svc.ListAsync(new AsnQuery(Search: "LLE-00002"), InventoryScope.Any, default);
        Assert.Equal(second.Id, Assert.Single(found).Id);
    }

    [Fact]
    public void Update_sql_adds_the_number_numbers_existing_asns_and_the_counter_in_an_idempotent_section()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-21 (Lote 170)", StringComparison.Ordinal);
        Assert.True(start > 0);
        var section = sql[start..];
        var nextHeader = section.IndexOf("\n/* ----", 10, StringComparison.Ordinal); // solo ESTA sección
        if (nextHeader > 0) section = section[..nextHeader];
        Assert.Contains("IF COL_LENGTH('dbo.Asn', 'Number') IS NULL", section);
        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY TenantId ORDER BY AsnId)", section);
        Assert.Contains("'LLE-' + RIGHT('00000'", section);
        Assert.Contains("'ASN'", section);
        Assert.Contains("CREATE UNIQUE INDEX UX_Asn_Number ON dbo.Asn (TenantId, Number)", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
    }
}
