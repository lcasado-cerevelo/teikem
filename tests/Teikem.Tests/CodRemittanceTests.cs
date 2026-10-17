using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Clients;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Export;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 86 (P8): remesa COD al cliente — agrupa los cobros cuadrados, calcula bruto, comisión y neto, y sigue abierta → cuadrada → aprobada → remitida (o se cancela).
public sealed class CodRemittanceTests
{
    [Fact]
    public void Rules_commission_pct_resolution_and_validation()
    {
        Assert.Equal(4.8m, RemittanceRules.Commission(60m, 8m));
        Assert.Equal(0.03m, RemittanceRules.Commission(0.25m, 10m));   // 0.025 → 0.03 (hacia afuera del cero)
        Assert.Equal(12m, RemittanceRules.ResolvePct(12m, 5m, 8m));
        Assert.Equal(5m, RemittanceRules.ResolvePct(null, 5m, 8m));
        Assert.Equal(8m, RemittanceRules.ResolvePct(null, null, 8m));
        Assert.True(RemittanceRules.IsValidPct(0m));
        Assert.True(RemittanceRules.IsValidPct(100m));
        Assert.False(RemittanceRules.IsValidPct(100.01m));
        Assert.False(RemittanceRules.IsValidPct(-1m));
        Assert.False(RemittanceRules.IsValidPct(8.255m));
    }

    private sealed class World(WmsFixture f, Client c, CodService cod, CodRemittanceService rem)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public CodService Cod { get; } = cod;
        public CodRemittanceService Rem { get; } = rem;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<CodService>();
            s.AddSingleton<ExportTemplateService>();
            s.AddSingleton<CodRemittanceService>();
        });
        f.SetModules(ModuleKeys.Cod);
        var c = await f.AddClientAsync("C1");
        return new World(f, c, f.Get<CodService>(), f.Get<CodRemittanceService>());
    }

    private static async Task<TransportOrder> OrderAsync(World x, string number, decimal cod, string invoice, Client? client = null)
    {
        var o = await x.F.AddOrderAsync((client ?? x.C).ClientId, number, OrderStatuses.Delivered);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        (t.CodAmount, t.CodStatusCodeId, t.ClientInvoiceNumber) = (cod, x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Pending), invoice);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    /// <summary>Cobra y cuadra: la orden queda con su COD cuadrado (listo para una remesa).</summary>
    private static async Task CollectedAndReconciledAsync(World x, TransportOrder o, params (string Method, decimal Amount, string? Check)[] payments)
    {
        foreach (var (m, a, chk) in payments) await x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest(m, a, chk), default);
        await x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode(o.OrderNumber) }), default);
    }

    private static CodRemittanceRequest Req(World x, decimal? pct = null)
        => new(x.C.PublicId, TenantClock.Default.Today.AddDays(-1), TenantClock.Default.Today.AddDays(1), pct);

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    /// <summary>Tres órdenes del cliente: A (100: 60 efectivo + 40 cheque) y B (50) cuadradas, y C (30) cobrada sin cuadrar; y otra de otro cliente.</summary>
    private static async Task<(TransportOrder A, TransportOrder B, TransportOrder C, TransportOrder D)> SeedAsync(World x)
    {
        var other = await x.F.AddClientAsync("C2");
        var a = await OrderAsync(x, "T-1", 100m, "FAC-1");
        var b = await OrderAsync(x, "T-2", 50m, "FAC-2");
        var c = await OrderAsync(x, "T-3", 30m, "FAC-3");
        var d = await OrderAsync(x, "T-4", 20m, "FAC-4", other);
        await CollectedAndReconciledAsync(x, a, ("CASH", 60m, null), ("CHECK", 40m, "5521"));
        await CollectedAndReconciledAsync(x, b, ("CASH", 50m, null));
        await x.Cod.CollectAsync(c.PublicId, new CodCollectionRequest("CASH", 30m), default);   // sin cuadrar
        await CollectedAndReconciledAsync(x, d, ("CASH", 20m, null));
        return (a, b, c, d);
    }

    [Fact]
    public async Task Creating_groups_the_reconciled_collections_of_the_client_and_computes_gross_commission_and_net()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await SeedAsync(x);

        var preview = await x.Rem.CandidatesAsync(x.C.PublicId, TenantClock.Default.Today.AddDays(-1), TenantClock.Default.Today.AddDays(1), null, default);
        Assert.Equal((8m, "TENANT", 150m, 12m, 138m), (preview.CommissionPct, preview.CommissionSource, preview.GrossCollected, preview.CommissionAmount, preview.NetRemitted));
        Assert.Equal(3, preview.Lines.Count);   // lo sin cuadrar y lo del otro cliente no entran

        var r = await x.Rem.CreateAsync(Req(x), default);
        Assert.Equal(("REM-00001", "OPEN", 150m, 8m, 12m, 138m, "USD"), (r.Number, r.Status, r.GrossCollected, r.CommissionPct, r.CommissionAmount, r.NetRemitted, r.Currency));
        Assert.Equal(new[] { 4.8m, 3.2m, 4m }, r.Lines.Select(l => l.CommissionAmount).ToArray());
        Assert.Equal(new[] { 55.2m, 36.8m, 46m }, r.Lines.Select(l => l.NetAmount).ToArray());
        Assert.Equal(new[] { "T-1", "T-1", "T-2" }, r.Lines.Select(l => l.OrderNumber).ToArray());
        Assert.Equal("FAC-1", r.Lines[0].InvoiceNumber);

        // cada cobro entra en una sola remesa
        Assert.Equal(RemittanceRules.NothingToRemit, await MsgOf(() => x.Rem.CreateAsync(Req(x), default)));
        Assert.Empty((await x.Rem.CandidatesAsync(x.C.PublicId, TenantClock.Default.Today.AddDays(-1), TenantClock.Default.Today.AddDays(1), null, default)).Lines);
        // el período fuera de la fecha del cobro no trae nada
        Assert.Equal(RemittanceRules.NothingToRemit, await MsgOf(() => x.Rem.CreateAsync(new CodRemittanceRequest(x.C.PublicId, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31)), default)));
    }

    [Fact]
    public async Task The_commission_comes_from_the_request_then_the_active_contract_then_the_company_default_and_is_validated()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await SeedAsync(x);
        var from = TenantClock.Default.Today.AddDays(-1);
        var to = TenantClock.Default.Today.AddDays(1);

        Assert.Equal(("TENANT", 8m), ((await x.Rem.CandidatesAsync(x.C.PublicId, from, to, null, default)).CommissionSource, 8m));
        Assert.Equal(10m, (await x.Rem.UpdateSettingsAsync(new CodSettingsRequest(10m), default)).DefaultCommissionPct);
        Assert.Equal(10m, (await x.Rem.CandidatesAsync(x.C.PublicId, from, to, null, default)).CommissionPct);
        Assert.Equal(RemittanceRules.SettingsPctInvalid, await MsgOf(() => x.Rem.UpdateSettingsAsync(new CodSettingsRequest(101m), default)));

        x.F.Db.Set<Contract>().Add(new Contract
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.C.ClientId, ContractNumber = "K-1", Title = "Marco", StartDate = new DateOnly(2026, 1, 1), CodCommissionPct = 5m,
            StatusCodeId = x.F.StatusId(StatusDomains.ContractStatus, ContractStatuses.Active), IsActive = true,
        });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var contract = await x.Rem.CandidatesAsync(x.C.PublicId, from, to, null, default);
        Assert.Equal((5m, "CONTRACT", 7.5m), (contract.CommissionPct, contract.CommissionSource, contract.CommissionAmount));
        var asked = await x.Rem.CandidatesAsync(x.C.PublicId, from, to, 12.5m, default);
        Assert.Equal((12.5m, "REQUEST"), (asked.CommissionPct, asked.CommissionSource));

        Assert.Equal(RemittanceRules.PctInvalid, await MsgOf(() => x.Rem.CreateAsync(Req(x, 100.5m), default)));
        Assert.Equal(RemittanceRules.ClientRequired, await MsgOf(() => x.Rem.CreateAsync(new CodRemittanceRequest(null, from, to), default)));
        Assert.Equal(RemittanceRules.ClientInvalid, await MsgOf(() => x.Rem.CreateAsync(new CodRemittanceRequest(Guid.NewGuid(), from, to), default)));
        Assert.Equal(RemittanceRules.PeriodRequired, await MsgOf(() => x.Rem.CreateAsync(new CodRemittanceRequest(x.C.PublicId), default)));
        Assert.Equal(RemittanceRules.PeriodInvalid, await MsgOf(() => x.Rem.CreateAsync(new CodRemittanceRequest(x.C.PublicId, to, from), default)));
        var r = await x.Rem.CreateAsync(Req(x), default);
        Assert.Equal(5m, r.CommissionPct);
    }

    [Fact]
    public async Task The_status_flow_goes_open_reconciled_approved_remitted_and_remitting_closes_the_cod_of_the_orders()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var (a, b, c, _) = await SeedAsync(x);
        var r = await x.Rem.CreateAsync(Req(x), default);

        Assert.Equal(RemittanceRules.ApproveNeedsReconciled, await MsgOf(() => x.Rem.ApproveAsync(r.PublicId, default)));
        Assert.Equal(RemittanceRules.RemitNeedsApproved, await MsgOf(() => x.Rem.RemitAsync(r.PublicId, null, default)));
        Assert.Equal(RemittanceRules.ExportNeedsApproved, await MsgOf(() => x.Rem.ExportAsync(r.PublicId, null, default)));
        Assert.Equal("RECONCILED", (await x.Rem.ReviewAsync(r.PublicId, default)).Status);
        Assert.Equal(RemittanceRules.ReviewNeedsOpen, await MsgOf(() => x.Rem.ReviewAsync(r.PublicId, default)));
        var approved = await x.Rem.ApproveAsync(r.PublicId, default);
        Assert.Equal("APPROVED", approved.Status);
        Assert.NotNull(approved.ApprovedAtUtc);
        Assert.Equal(RemittanceRules.CancelNotAllowed, await MsgOf(() => x.Rem.CancelAsync(r.PublicId, default)));
        Assert.Equal(RemittanceRules.ExportRefTooLong, await MsgOf(() => x.Rem.RemitAsync(r.PublicId, new CodRemitRequest(new string('r', 81)), default)));

        var done = await x.Rem.RemitAsync(r.PublicId, new CodRemitRequest("CHK-9001"), default);
        Assert.Equal(("REMITTED", "CHK-9001"), (done.Status, done.ExportRef));
        Assert.NotNull(done.RemittedAtUtc);
        Assert.Equal(RemittanceRules.RemitNeedsApproved, await MsgOf(() => x.Rem.RemitAsync(r.PublicId, null, default)));

        // los cobros quedaron REMITIDOS y el COD de las órdenes remitidas también; la que no se cuadró sigue como estaba
        var ra = await x.Cod.GetOrderAsync(a.PublicId, default);
        Assert.Equal("REMITTED", ra.CodStatus);
        Assert.All(ra.Collections, col => Assert.Equal("REMITTED", col.Status));
        Assert.Equal("REMITTED", (await x.Cod.GetOrderAsync(b.PublicId, default)).CodStatus);
        Assert.Equal("COLLECTED", (await x.Cod.GetOrderAsync(c.PublicId, default)).CodStatus);
        // el historial de la remesa queda: ABIERTA → CUADRADA → APROBADA → REMITIDA
        Assert.Equal(4, await x.F.Db.EntityStatusHistories.CountAsync(h => h.ToStatusCodeId == x.F.StatusId("RemittanceStatus", "OPEN") || h.ToStatusCodeId == x.F.StatusId("RemittanceStatus", "RECONCILED")
            || h.ToStatusCodeId == x.F.StatusId("RemittanceStatus", "APPROVED") || h.ToStatusCodeId == x.F.StatusId("RemittanceStatus", "REMITTED")));
    }

    [Fact]
    public async Task Cancelling_an_open_remittance_frees_its_collections_for_another_one()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await SeedAsync(x);
        var r = await x.Rem.CreateAsync(Req(x), default);
        var cancelled = await x.Rem.CancelAsync(r.PublicId, default);
        Assert.Equal("OPEN", cancelled.Status);
        Assert.Empty((await x.Rem.ListAsync(null, null, null, 0, 50, default)).Items);   // no sale en la lista
        await Assert.ThrowsAsync<NotFoundException>(() => x.Rem.ReviewAsync(r.PublicId, default));
        var again = await x.Rem.CreateAsync(Req(x, 9m), default);
        Assert.Equal(("REM-00002", 150m, 9m), (again.Number, again.GrossCollected, again.CommissionPct));
    }

    [Fact]
    public async Task The_export_has_one_row_per_collection_with_the_commission_and_net_and_the_list_filters()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await SeedAsync(x);
        var r = await x.Rem.CreateAsync(Req(x), default);
        await x.Rem.ReviewAsync(r.PublicId, default);
        await x.Rem.ApproveAsync(r.PublicId, default);

        var file = await x.Rem.ExportAsync(r.PublicId, null, default);
        Assert.Equal(("remesa-REM-00001.csv", 3), (file.FileName, file.RowCount));
        var lines = file.Text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);   // encabezado + 3 cobros
        Assert.StartsWith("RemittanceNo,", lines[0]);
        Assert.Contains("REM-00001", lines[1]);
        Assert.Contains("FAC-1", lines[1]);
        Assert.Contains("4.80", lines[1]);
        Assert.Contains("55.20", lines[1]);
        Assert.Contains("5521", lines[2]);   // el cheque
        Assert.Contains("12.00", lines[3]);   // la comisión de la remesa

        var open = await x.Rem.ListAsync("APPROVED", x.C.PublicId, "REM", 0, 50, default);
        var item = Assert.Single(open.Items);
        Assert.Equal(("REM-00001", 3, 138m), (item.Number, item.LineCount, item.NetRemitted));
        Assert.Empty((await x.Rem.ListAsync("OPEN", null, null, 0, 50, default)).Items);

        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Empty((await x.Rem.ListAsync(null, null, null, 0, 50, default)).Items);
    }

    [Fact]
    public void The_remittance_scope_has_its_own_fields_and_the_endpoints_are_protected()
    {
        Assert.Contains(ExportCodes.ScopeCodRemittance, ExportCodes.Scopes);
        Assert.Contains(ExportFields.Of(ExportCodes.ScopeCodRemittance), f => f.Code == "NET_AMOUNT");
        Assert.Contains(ModuleKeys.Cod, typeof(CodRemittancesController).GetCustomAttributes<RequireModuleAttribute>().Select(a => a.ModuleKey));
        string Policy(string action) => typeof(CodRemittancesController).GetMethod(action)!.GetCustomAttributes<RequirePermissionAttribute>().Single().Policy;
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.CodRemit, Policy(nameof(CodRemittancesController.Create)));
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.CodRemit, Policy(nameof(CodRemittancesController.Remit)));
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.CodView, Policy(nameof(CodRemittancesController.List)));
        Assert.NotNull(typeof(CodRemittancesController).GetMethod(nameof(CodRemittancesController.Approve))!.GetCustomAttribute<RequireAal2Attribute>());
    }
}
