using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
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

// Lote 126: procesamiento de la factura del cliente («second check») en la lectura de hojas: sin COD se coteja contra la orden entregada, con COD se cuadra y también queda procesada;
// el modo (AUTO/COD/INVOICE/BOTH) solo advierte; el contrato puede cobrar el cotejo y la corrida de facturación lo mete como línea INVOICE_CHECK.
public sealed class InvoiceCheckTests
{
    private sealed class World(WmsFixture f, Client c, CodService cod, BillingService billing, ContractService contracts)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public CodService Cod { get; } = cod;
        public BillingService Billing { get; } = billing;
        public ContractService Contracts { get; } = contracts;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<CodService>();
            s.AddSingleton<TaxService>();
            s.AddSingleton<RateService>();
            s.AddSingleton<OrderQuoteService>();
            s.AddSingleton<BillingService>();
            s.AddSingleton<ContractService>();
        });
        f.SetModules(ModuleKeys.Cod, ModuleKeys.Billing, ModuleKeys.LtlGround);
        var c = await f.AddClientAsync("C1");
        return new World(f, c, f.Get<CodService>(), f.Get<BillingService>(), f.Get<ContractService>());
    }

    private static async Task<TransportOrder> OrderAsync(World x, string number, string invoice, decimal? cod = null, string status = OrderStatuses.Delivered, int? contractId = null)
    {
        var o = await x.F.AddOrderAsync(x.C.ClientId, number, status);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        t.ClientInvoiceNumber = invoice;
        t.ContractId = contractId;
        if (cod is not null) (t.CodAmount, t.CodStatusCodeId) = (cod, x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Pending));
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    private static async Task<Contract> ContractAsync(World x, bool bill, decimal? fee)
    {
        var c = new Contract
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.C.ClientId, ContractNumber = "K-1", Title = "Marco", StartDate = new DateOnly(2026, 1, 1),
            StatusCodeId = x.F.StatusId(StatusDomains.ContractStatus, ContractStatuses.Active), IsActive = true, BillInvoiceCheck = bill, InvoiceCheckFee = fee,
        };
        x.F.Db.Set<Contract>().Add(c);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return c;
    }

    private static async Task<TransportOrder> ReloadAsync(World x, int id) => await x.F.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == id);

    private static CodReconcileCode[] Codes(params string[] codes) => codes.Select(c => new CodReconcileCode(c)).ToArray();

    [Fact]
    public async Task An_invoice_without_cod_is_checked_against_its_delivered_order_once_and_the_sheet_of_a_cod_order_also_marks_the_invoice_processed()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var plain = await OrderAsync(x, "T-1", "FAC-1");                                      // sin COD, entregada
        var open = await OrderAsync(x, "T-2", "FAC-2", status: OrderStatuses.InTransit);        // sin COD, en camino
        var cod = await OrderAsync(x, "T-3", "FAC-3", cod: 50m);                                // con COD, cobrada
        await x.Cod.CollectAsync(cod.PublicId, new CodCollectionRequest("CASH", 50m), default);

        var preview = await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1", "FAC-2", "FAC-3", "NADA"), DryRun: true), default);
        Assert.Equal(new[] { CodReconcileResults.InvoiceChecked, CodReconcileResults.NotDelivered, CodReconcileResults.Reconciled, CodReconcileResults.NotFound }, preview.Items.Select(i => i.Result).ToArray());
        Assert.Equal(new[] { CodReconcileKinds.Invoice, CodReconcileKinds.Invoice, CodReconcileKinds.Cod, CodReconcileKinds.Cod }, preview.Items.Select(i => i.Kind).ToArray());
        Assert.Equal((1, 1, 2), (preview.Reconciled, preview.Checked, preview.Issues));
        Assert.Null((await ReloadAsync(x, plain.TransportOrderId)).InvoiceProcessedAtUtc);     // la vista previa no marca nada

        var run = await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1", "FAC-2", "FAC-3"), "hojas.pdf"), default);
        Assert.Equal((1, 1, 1), (run.Reconciled, run.Checked, run.Issues));
        var runId = (await x.F.Db.Set<CodReconcileRun>().AsNoTracking().SingleAsync(r => r.PublicId == run.RunPublicId)).CodReconcileRunId;
        var p = await ReloadAsync(x, plain.TransportOrderId);
        Assert.NotNull(p.InvoiceProcessedAtUtc);
        Assert.Equal((1, runId), (p.InvoiceProcessedBy, p.InvoiceProcessRunId));
        var c = await ReloadAsync(x, cod.TransportOrderId);
        Assert.Equal(runId, c.InvoiceProcessRunId);                                             // la hoja de COD también deja la factura procesada
        Assert.Null((await ReloadAsync(x, open.TransportOrderId)).InvoiceProcessedAtUtc);

        // la misma factura otra vez: ya procesada (no se vuelve a marcar); el detalle de la corrida trae Kind y Checked
        var again = await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1")), default);
        Assert.Equal(CodReconcileResults.AlreadyProcessed, again.Items[0].Result);
        Assert.Equal(p.InvoiceProcessedAtUtc, (await ReloadAsync(x, plain.TransportOrderId)).InvoiceProcessedAtUtc);
        var detail = await x.Cod.GetRunAsync(run.RunPublicId!.Value, default);
        Assert.Equal(1, detail.Checked);
        Assert.Equal(CodReconcileKinds.Invoice, detail.Items[0].Kind);
    }

    [Fact]
    public async Task The_mode_only_warns_and_everything_is_processed_anyway()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, "T-1", "FAC-1");
        var cod = await OrderAsync(x, "T-2", "FAC-2", cod: 10m);
        await x.Cod.CollectAsync(cod.PublicId, new CodCollectionRequest("CASH", 10m), default);

        Assert.Empty((await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1", "FAC-2"), DryRun: true), default)).Warnings!);                                  // AUTO: sin advertencias
        Assert.Equal(new[] { CodReconcileWarnings.NotCodSheets }, (await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1", "FAC-2"), DryRun: true, Mode: "COD"), default)).Warnings);
        Assert.Equal(new[] { CodReconcileWarnings.CodSheets }, (await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1", "FAC-2"), DryRun: true, Mode: "invoice"), default)).Warnings);
        Assert.Empty((await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1", "FAC-2"), DryRun: true, Mode: "BOTH"), default)).Warnings!);
        Assert.Equal(new[] { CodReconcileWarnings.NoCodSheets }, (await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1"), DryRun: true, Mode: "BOTH"), default)).Warnings);
        Assert.Equal(new[] { CodReconcileWarnings.NoInvoiceSheets }, (await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-2"), DryRun: true, Mode: "BOTH"), default)).Warnings);
        Assert.Empty((await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("NADA"), DryRun: true, Mode: "BOTH"), default)).Warnings!);                              // nada encontrado: nada que advertir
        var bad = await Assert.ThrowsAsync<ValidationException>(() => x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1"), Mode: "PDF"), default));
        Assert.Equal(CodRules.ModeInvalid, bad.Message);

        // con modo COD se procesan igual las dos
        var run = await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1", "FAC-2"), Mode: "COD"), default);
        Assert.Equal((1, 1), (run.Reconciled, run.Checked));
    }

    [Fact]
    public async Task The_contract_fee_is_billed_once_as_an_invoice_check_line_and_only_when_the_contract_charges_it()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var paying = await ContractAsync(x, true, 3.5m);
        var a = await OrderAsync(x, "T-1", "FAC-1", contractId: paying.ContractId);
        var b = await OrderAsync(x, "T-2", "FAC-2", contractId: paying.ContractId, status: OrderStatuses.InTransit);   // no entregada: no se procesa
        var free = await OrderAsync(x, "T-3", "FAC-3");                                                                  // sin contrato: sin cargo
        await x.Cod.ReconcileAsync(new CodReconcileRequest(Codes("FAC-1", "FAC-2", "FAC-3")), default);
        Assert.NotNull((await ReloadAsync(x, free.TransportOrderId)).InvoiceProcessedAtUtc);

        var period = new BillingRunRequest(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));
        var run = await x.Billing.GenerateAsync(period, default);
        var inv = Assert.Single(run.Invoices);
        var line = Assert.Single((await x.Billing.GetInvoiceAsync(inv.PublicId, default)).Lines);
        Assert.Equal((3.5m, 1m), (line.UnitAmount, line.Quantity));
        Assert.Contains("Cotejo de factura", line.Description);
        Assert.Contains("T-1", line.Description);
        Assert.Equal(BillingRules.NothingToBill, (await Assert.ThrowsAnyAsync<TeikemException>(() => x.Billing.GenerateAsync(period, default))).Message);   // nunca dos veces
        Assert.NotNull(a); Assert.NotNull(b);
    }

    [Fact]
    public async Task The_contract_fee_is_set_with_validation()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var contract = await ContractAsync(x, false, null);
        Assert.Equal(InvoiceCheckFeeRules.AmountRequiredMessage, (await Assert.ThrowsAsync<ValidationException>(() => x.Contracts.SetInvoiceCheckFeeAsync(contract.PublicId, new InvoiceCheckFeeRequest(true, null), default))).Message);
        Assert.Equal(InvoiceCheckFeeRules.NegativeMessage, (await Assert.ThrowsAsync<ValidationException>(() => x.Contracts.SetInvoiceCheckFeeAsync(contract.PublicId, new InvoiceCheckFeeRequest(true, -1m), default))).Message);
        var on = await x.Contracts.SetInvoiceCheckFeeAsync(contract.PublicId, new InvoiceCheckFeeRequest(true, 2m), default);
        Assert.Equal((true, 2m), (on.InvoiceCheckFee!.Enabled, on.InvoiceCheckFee.Amount));
        var off = await x.Contracts.SetInvoiceCheckFeeAsync(contract.PublicId, new InvoiceCheckFeeRequest(false, 9m), default);
        Assert.Equal((false, (decimal?)null), (off.InvoiceCheckFee!.Enabled, off.InvoiceCheckFee.Amount));
    }
}
