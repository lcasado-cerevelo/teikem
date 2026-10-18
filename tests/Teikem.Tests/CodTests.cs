using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Clients;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 84 (P8): ciclo COD — cobro contra entrega (parciales, estatus del COD de la orden) y reconciliación por códigos (la factura o la orden leída de las hojas escaneadas).
public sealed class CodTests
{
    [Fact]
    public void Rules_amount_pending_status_and_code_normalization()
    {
        Assert.True(CodRules.IsValidAmount(10.25m));
        Assert.False(CodRules.IsValidAmount(0m));
        Assert.False(CodRules.IsValidAmount(10.255m));
        Assert.Equal(40m, CodRules.Pending(100m, 60m));
        Assert.Equal(0m, CodRules.Pending(100m, 120m));
        Assert.Equal("PENDING", CodRules.OrderStatusFor(100m, 0m));
        Assert.Equal("PARTIAL", CodRules.OrderStatusFor(100m, 60m));
        Assert.Equal("COLLECTED", CodRules.OrderStatusFor(100m, 100m));
        Assert.Equal("FAC-001", CodRules.NormalizeCode("  FAC-001\r\n"));
        Assert.Equal(string.Empty, CodRules.NormalizeCode("  "));
        Assert.True(CodMethods.NeedsCheckNumber("CHECK"));
        Assert.False(CodMethods.NeedsCheckNumber("CASH"));
        Assert.Equal("Cuadrado: 2 cobro(s) por 60.00.", CodService.MessageFor(CodReconcileResults.Reconciled, 60m, 2));
    }

    private sealed class World(WmsFixture f, Client c, CodService cod)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public CodService Cod { get; } = cod;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<CodService>();
        });
        f.SetModules(ModuleKeys.Cod);
        var c = await f.AddClientAsync("C1");
        return new World(f, c, f.Get<CodService>());
    }

    /// <summary>Una orden con COD de `cod` dólares (estatus del COD: por cobrar) y, si se pide, el número de la factura del cliente.</summary>
    private static async Task<TransportOrder> OrderAsync(World x, string number, decimal? cod, string? invoice = null, string status = OrderStatuses.Delivered, Client? client = null)
    {
        var o = await x.F.AddOrderAsync((client ?? x.C).ClientId, number, status);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        if (cod is not null) (t.CodAmount, t.CodStatusCodeId) = (cod, x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Pending));
        if (invoice is not null) t.ClientInvoiceNumber = invoice;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    private static CodCollectionRequest Cash(decimal amount) => new("CASH", amount);

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task Collecting_validates_and_moves_the_order_cod_to_partial_and_then_collected()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, "T-1", 100m, "FAC-1");

        var first = await x.Cod.CollectAsync(o.PublicId, Cash(60m), default);
        Assert.Equal(("PARTIAL", 100m, 60m, 40m), (first.CodStatus, first.CodAmount, first.Collected, first.Pending));
        var c1 = Assert.Single(first.Collections);
        Assert.Equal(("CASH", 60m, "COLLECTED"), (c1.Method, c1.Amount, c1.Status));

        Assert.Equal(CodRules.ExceedsPending("T-1", 40m), await MsgOf(() => x.Cod.CollectAsync(o.PublicId, Cash(50m), default)));
        var second = await x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest("CHECK", 40m, CheckNumber: "1234", Notes: "Cheque de la farmacia"), default);
        Assert.Equal(("COLLECTED", 100m, 0m), (second.CodStatus, second.Collected, second.Pending));
        Assert.Equal(2, second.Collections.Count);
        Assert.Equal("1234", second.Collections[1].CheckNumber);
        Assert.Equal(CodRules.ExceedsPending("T-1", 0m), await MsgOf(() => x.Cod.CollectAsync(o.PublicId, Cash(1m), default)));

        // el historial del COD de la orden queda registrado (PENDING → PARTIAL → COLLECTED)
        Assert.Equal(2, await x.F.Db.EntityStatusHistories.CountAsync(h => h.EntityId == o.TransportOrderId && h.ToStatusCodeId == x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Partial)
            || h.EntityId == o.TransportOrderId && h.ToStatusCodeId == x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Collected)));
    }

    [Fact]
    public async Task Collecting_a_whole_amount_at_once_goes_straight_to_collected_and_the_validations_have_their_messages()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, "T-1", 80m);
        Assert.Equal(CodRules.MethodRequired, await MsgOf(() => x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest(null, 10m), default)));
        Assert.Equal(CodRules.MethodInvalid, await MsgOf(() => x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest("BITCOIN", 10m), default)));
        Assert.Equal(CodRules.AmountInvalid, await MsgOf(() => x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest("CASH", 0m), default)));
        Assert.Equal(CodRules.AmountInvalid, await MsgOf(() => x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest("CASH", 1.005m), default)));
        Assert.Equal(CodRules.CheckNumberRequired, await MsgOf(() => x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest("CHECK", 10m), default)));
        Assert.Equal(CodRules.ReferenceTooLong, await MsgOf(() => x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest("ATH_MOVIL", 10m, Reference: new string('r', 81)), default)));
        Assert.Equal(CodRules.NoteTooLong, await MsgOf(() => x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest("CASH", 10m, Notes: new string('n', 301)), default)));
        var done = await x.Cod.CollectAsync(o.PublicId, Cash(80m), default);
        Assert.Equal("COLLECTED", done.CodStatus);

        var noCod = await OrderAsync(x, "T-2", null);
        Assert.Equal(CodRules.OrderHasNoCod, await MsgOf(() => x.Cod.CollectAsync(noCod.PublicId, Cash(5m), default)));
        var draft = await OrderAsync(x, "T-3", 50m, status: OrderStatuses.Submitted);
        Assert.Equal(CodRules.OrderNotCollectable, await MsgOf(() => x.Cod.CollectAsync(draft.PublicId, Cash(5m), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Cod.CollectAsync(Guid.NewGuid(), Cash(5m), default));
    }

    [Fact]
    public async Task Reconciling_by_invoice_or_order_number_squares_what_was_collected_and_reports_each_code()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var a = await OrderAsync(x, "T-1", 100m, "FAC-1");
        var b = await OrderAsync(x, "T-2", 50m, "FAC-2");
        var partial = await OrderAsync(x, "T-3", 90m, "FAC-3");
        var untouched = await OrderAsync(x, "T-4", 30m, "FAC-4");
        var noCod = await OrderAsync(x, "T-5", null, "FAC-5");
        await x.Cod.CollectAsync(a.PublicId, Cash(100m), default);
        await x.Cod.CollectAsync(b.PublicId, Cash(20m), default);
        await x.Cod.CollectAsync(b.PublicId, new CodCollectionRequest("ATH_MOVIL", 30m, Reference: "ATH-77"), default);
        await x.Cod.CollectAsync(partial.PublicId, Cash(40m), default);

        var codes = new[]
        {
            new CodReconcileCode("FAC-1", "hojas.pdf p1"),     // por la factura (en SQL Server la comparación no distingue mayúsculas)
            new CodReconcileCode("T-2", "hojas.pdf p2"),       // por el número de la orden
            new CodReconcileCode("FAC-3", "hojas.pdf p3"),     // cobrado en parte: cuadra lo cobrado, la orden sigue PARCIAL
            new CodReconcileCode("FAC-4"),                     // con COD pero sin cobros
            new CodReconcileCode("FAC-5"),                     // sin COD
            new CodReconcileCode("NADA-1"),                    // no existe
            new CodReconcileCode(" FAC-1 \n"),                 // la misma hoja otra vez
        };
        var preview = await x.Cod.ReconcileAsync(new CodReconcileRequest(codes, DryRun: true), default);
        Assert.True(preview.DryRun);
        Assert.Null(preview.RunPublicId);
        Assert.Equal(3, preview.Reconciled);
        Assert.Equal("COLLECTED", (await x.Cod.GetOrderAsync(a.PublicId, default)).CodStatus);   // la vista previa no cambió nada

        var result = await x.Cod.ReconcileAsync(new CodReconcileRequest(codes, "hojas.pdf"), default);
        Assert.NotNull(result.RunPublicId);
        Assert.Equal((7, 3, 4), (result.Total, result.Reconciled, result.Issues));
        Assert.Equal(new[]
        {
            CodReconcileResults.Reconciled, CodReconcileResults.Reconciled, CodReconcileResults.Reconciled, CodReconcileResults.NothingCollected, CodReconcileResults.NoCod, CodReconcileResults.NotFound,
            CodReconcileResults.Duplicate,
        }, result.Items.Select(i => i.Result).ToArray());
        Assert.Equal(("T-1", "FAC-1", "Cliente C1", 100m, 1), (result.Items[0].OrderNumber, result.Items[0].InvoiceNumber, result.Items[0].ClientName, result.Items[0].Reconciled, result.Items[0].Collections));
        Assert.Equal((50m, 2), (result.Items[1].Reconciled, result.Items[1].Collections));

        var ra = await x.Cod.GetOrderAsync(a.PublicId, default);
        Assert.Equal("RECONCILED", ra.CodStatus);
        Assert.All(ra.Collections, c => Assert.Equal("RECONCILED", c.Status));
        Assert.NotNull(ra.Collections[0].ReconciledAtUtc);
        Assert.Equal("RECONCILED", (await x.Cod.GetOrderAsync(b.PublicId, default)).CodStatus);
        var rp = await x.Cod.GetOrderAsync(partial.PublicId, default);
        Assert.Equal("PARTIAL", rp.CodStatus);   // falta por cobrar: la orden sigue parcial aunque lo cobrado ya cuadró
        Assert.Equal("RECONCILED", rp.Collections[0].Status);

        // lo que se cobre después queda por cuadrar; y volver a leer una hoja ya cuadrada avisa
        await x.Cod.CollectAsync(partial.PublicId, Cash(50m), default);
        Assert.Equal("COLLECTED", (await x.Cod.GetOrderAsync(partial.PublicId, default)).CodStatus);
        var again = await x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode("FAC-1"), new CodReconcileCode("FAC-3") }), default);
        Assert.Equal(new[] { CodReconcileResults.AlreadyReconciled, CodReconcileResults.Reconciled }, again.Items.Select(i => i.Result).ToArray());
        Assert.Equal("RECONCILED", (await x.Cod.GetOrderAsync(partial.PublicId, default)).CodStatus);
    }

    [Fact]
    public async Task The_runs_are_kept_with_their_codes_and_a_code_shared_by_two_orders_is_ambiguous()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var other = await x.F.AddClientAsync("C2");
        var a = await OrderAsync(x, "T-1", 10m, "9001");
        var b = await OrderAsync(x, "T-2", 20m, "9001", client: other);   // la misma factura en dos clientes
        // con una sola orden que tenga cobros por cuadrar, el código se resuelve solo; con dos queda ambiguo
        await x.Cod.CollectAsync(a.PublicId, Cash(10m), default);
        var preview = await x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode("9001") }, DryRun: true), default);
        Assert.Equal((CodReconcileResults.Reconciled, "T-1"), (preview.Items[0].Result, preview.Items[0].OrderNumber));
        await x.Cod.CollectAsync(b.PublicId, Cash(20m), default);
        var res = await x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode("9001", "manual") }, "A mano"), default);
        Assert.Equal(CodReconcileResults.Ambiguous, res.Items[0].Result);
        Assert.Equal("COLLECTED", (await x.Cod.GetOrderAsync(a.PublicId, default)).CodStatus);   // no se cuadra nada

        var runs = await x.Cod.RunsAsync(0, 10, default);
        var run = Assert.Single(runs.Items);
        Assert.Equal(("A mano", 1, 0, 1), (run.Source, run.Total, run.Reconciled, run.Issues));
        var detail = await x.Cod.GetRunAsync(run.PublicId, default);
        Assert.Equal(("9001", "manual", CodReconcileResults.Ambiguous), (detail.Items[0].Code, detail.Items[0].Source, detail.Items[0].Result));

        // por el número de la orden, o diciendo de qué cliente son las hojas, sí se cuadra
        var ok = await x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode("T-1") }), default);
        Assert.Equal(CodReconcileResults.Reconciled, ok.Items[0].Result);
        var byClient = await x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode("9001") }, ClientPublicId: other.PublicId), default);
        Assert.Equal((CodReconcileResults.Reconciled, "T-2"), (byClient.Items[0].Result, byClient.Items[0].OrderNumber));
        Assert.Equal(3, (await x.Cod.RunsAsync(0, 10, default)).Total);
        Assert.Equal(CodRules.ClientInvalid, await MsgOf(() => x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode("9001") }, ClientPublicId: Guid.NewGuid()), default)));
    }

    [Fact]
    public async Task The_list_of_orders_with_cod_filters_by_status_and_search_and_other_companies_see_nothing()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var a = await OrderAsync(x, "T-1", 100m, "FAC-1");
        await OrderAsync(x, "T-2", 50m, "FAC-2");
        await OrderAsync(x, "T-3", null, "FAC-3");
        await x.Cod.CollectAsync(a.PublicId, Cash(60m), default);

        var all = await x.Cod.OrdersAsync(null, null, null, 0, 50, default);
        Assert.Equal(2, all.Total);   // la orden sin COD no sale
        var partial = Assert.Single((await x.Cod.OrdersAsync("PARTIAL", null, null, 0, 50, default)).Items);
        Assert.Equal(("T-1", 100m, 60m, 40m), (partial.OrderNumber, partial.CodAmount, partial.Collected, partial.Pending));
        Assert.Single((await x.Cod.OrdersAsync(null, null, "FAC-2", 0, 50, default)).Items);
        Assert.Equal(2, (await x.Cod.OrdersAsync(null, x.C.PublicId, null, 0, 50, default)).Total);

        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Equal(0, (await x.Cod.OrdersAsync(null, null, null, 0, 50, default)).Total);
    }

    [Fact]
    public async Task The_reconcile_request_is_validated_and_the_endpoints_are_protected_by_module_and_permission()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        Assert.Equal(CodRules.CodesRequired, await MsgOf(() => x.Cod.ReconcileAsync(new CodReconcileRequest(), default)));
        Assert.Equal(CodRules.CodesRequired, await MsgOf(() => x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode("  ") }), default)));
        Assert.Equal(CodRules.CodeTooLong, await MsgOf(() => x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode(new string('9', 81)) }), default)));
        Assert.Equal(CodRules.TooManyCodes, await MsgOf(() => x.Cod.ReconcileAsync(new CodReconcileRequest(Enumerable.Range(0, 2001).Select(i => new CodReconcileCode("C" + i)).ToArray()), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Cod.GetRunAsync(Guid.NewGuid(), default));

        Assert.Contains(ModuleKeys.Cod, typeof(CodController).GetCustomAttributes<RequireModuleAttribute>().Select(a => a.ModuleKey));
        string Policy(string action) => typeof(CodController).GetMethod(action)!.GetCustomAttributes<RequirePermissionAttribute>().Single().Policy;
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.CodCollect, Policy(nameof(CodController.Collect)));
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.CodReconcile, Policy(nameof(CodController.Reconcile)));
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.CodView, Policy(nameof(CodController.Orders)));
    }
}
