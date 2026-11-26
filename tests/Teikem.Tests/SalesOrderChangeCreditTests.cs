using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P5): el crédito frente a una modificación. Solo se revisa si el total SUBE; excedido = 422 credit_exceeded con el aviso guardado; overrideCredit exige
// orders.credit_override y deja CreditOverride en la modificación y el evento de seguridad; la adición que excede se rechaza antes de abrir la transacción.
public sealed class SalesOrderChangeCreditTests
{
    [Fact]
    public async Task Raising_the_total_above_the_limit_is_a_422_with_the_notice_and_the_override_needs_the_permission()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));   // 30
        await ChangeKit.SetClientAsync(x.F, x.C.ClientId, c => c.CreditLimit = 40m);
        var security = new RecordingSecurityWriter();
        var svc = ChangeKit.Service(x, security);

        var e = await Assert.ThrowsAsync<CreditExceededException>(() => svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 5m)), default));
        Assert.Equal((422, "credit_exceeded"), (e.StatusCode, e.Code));
        Assert.Contains(x.Publisher.Published, p => p.Event == SalesEventCodes.CreditOverrideNeeded);
        Assert.Empty(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().ToListAsync());
        Assert.Equal(3m, (await x.Svc.GetAsync(o.PublicId, default)).Lines[0].QtyOrdered);

        var withOverride = ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 5m)) with { OverrideCredit = true };
        x.F.SetPermissions(PermissionCatalog.SalesModify, PermissionCatalog.SalesView);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(o.PublicId, withOverride, default));

        x.F.Tenant.IsPlatformAdmin = true;
        var dto = await svc.CreateAsync(o.PublicId, withOverride, default);
        Assert.Equal((SalesOrderChangeStatuses.Applied, true), (dto.Item.Status, dto.CreditOverride));
        var ev = Assert.Single(security.Events);
        Assert.Equal(SecurityEventTypes.RoleChange, ev.Type);
        Assert.Contains("credit_override", ev.Detail);
        Assert.Contains(dto.Item.PublicId.ToString(), ev.Detail);
    }

    [Fact]
    public async Task Lowering_the_total_never_checks_the_credit()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 5m));   // 50
        await ChangeKit.SetClientAsync(x.F, x.C.ClientId, c => c.CreditLimit = 10m);   // ya excedido
        var security = new RecordingSecurityWriter();

        var dto = await ChangeKit.Service(x, security).CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)), default);

        Assert.Equal((SalesOrderChangeStatuses.Applied, false, -10m), (dto.Item.Status, dto.CreditOverride, dto.Item.TotalDelta));
        Assert.DoesNotContain(x.Publisher.Published, p => p.Event == SalesEventCodes.CreditOverrideNeeded);
        Assert.Empty(security.Events);
    }

    [Fact]
    public async Task An_addition_that_exceeds_the_credit_is_refused_before_the_transaction_without_child_or_record()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));   // la madre (30) cuenta en la exposición
        await ChangeKit.SetClientAsync(x.F, x.C.ClientId, c => c.CreditLimit = 35m);

        var e = await Assert.ThrowsAsync<CreditExceededException>(() => ChangeKit.Service(x).CreateAdditionAsync(o.PublicId, ChangeKit.Addition((x.P1, 1m)), default));

        Assert.Equal(422, e.StatusCode);
        Assert.Empty(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().ToListAsync());
        Assert.Equal(1, await x.F.Db.Set<SalesOrder>().AsNoTracking().CountAsync());
        Assert.Contains(x.Publisher.Published, p => p.Event == SalesEventCodes.CreditOverrideNeeded);
    }
}
