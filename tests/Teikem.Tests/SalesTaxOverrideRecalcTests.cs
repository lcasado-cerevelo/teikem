using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P3): el IVU MANUAL de una línea (porcentaje): el importe sale de cantidad × precio × % (SalesChangeRules.ManualTax), la línea guarda lo que daría el cálculo
// de hoy, un cambio de cantidad lo recalcula por el porcentaje, quitarlo vuelve al cálculo y solo afecta esa línea (la exención del cliente sigue para las demás).
public sealed class SalesTaxOverrideRecalcTests
{
    private static async Task<SalesChangeWorld> WorldAsync(bool exempt = false)
    {
        var x = await SalesChangeWorld.CreateAsync(tax: true);
        var tax = x.F.Get<TaxService>();
        await tax.CreateRateAsync(new TaxRateRequest("IVU", "IVU", new DateOnly(2020, 1, 1), null,
            new[] { new TaxComponentDto("STATE", "Estatal", 10.5m), new TaxComponentDto("CITY", "Municipal", 1m) }), default);
        await tax.UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "IVU"), default);
        if (exempt)
            await TaxTestData.SeedDocumentExemptionAsync(tax, x.F.Db, WmsFixture.TenantId, x.C.ClientId, "CERT-1", new DateOnly(2020, 1, 1), TenantClock.Default.Today.AddYears(1));
        x.F.Db.ChangeTracker.Clear();
        return x;
    }

    private static async Task<SalesOrderLine> LineAsync(SalesChangeWorld x, int lineId)
        => await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderLineId == lineId);

    [Fact]
    public async Task A_manual_rate_is_kept_when_the_quantity_changes_and_clearing_it_brings_back_the_calculation()
    {
        await using var x = await WorldAsync();
        var o = await x.RequestedAsync((x.P2, 3m));   // 3 × 8.00
        var lineId = o.Lines[0].Id;
        Assert.Equal(2.76m, o.TaxAmount);   // 11.5 %

        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Tax, LineId: lineId, TaxPct: 1m, Note: "Acuerdo con el cliente") });
        var manual = await LineAsync(x, lineId);
        Assert.Equal((TaxReasons.ManualOverride, 1m, 0.24m, false), (manual.TaxReason, manual.TaxRatePct, manual.TaxAmount, manual.IsTaxExempt));
        Assert.Equal((11.5m, 2.76m), (manual.TaxComputedRatePct, manual.TaxComputedAmount));
        Assert.Equal(("Acuerdo con el cliente", 1), (manual.TaxOverrideNote, manual.TaxOverrideBy));
        var dto = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((0.24m, 24.24m), (dto.TaxAmount, dto.Total));
        Assert.Equal((TaxReasons.ManualOverride, 11.5m, 2.76m), (dto.Lines[0].TaxReason, dto.Lines[0].TaxComputedRatePct, dto.Lines[0].TaxComputedAmount));

        // cambiar la cantidad recalcula por el porcentaje manual (5 × 8 = 40 → 0.40), no por el cálculo
        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Qty, LineId: lineId, Quantity: 5m) }, changeNo: 2);
        var more = await LineAsync(x, lineId);
        Assert.Equal((1m, 0.40m, 4.60m), (more.TaxRatePct, more.TaxAmount, more.TaxComputedAmount));

        // quitar el IVU manual vuelve al cálculo normal
        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.ClearTax, LineId: lineId) }, changeNo: 3);
        var back = await LineAsync(x, lineId);
        Assert.Equal((TaxReasons.Taxable, 11.5m, 4.60m), (back.TaxReason, back.TaxRatePct, back.TaxAmount));
        Assert.Null(back.TaxComputedAmount);
        Assert.Null(back.TaxOverrideNote);
        await x.AssertCoherentAsync(o.PublicId);
    }

    [Fact]
    public async Task The_manual_rate_only_touches_its_line_and_the_client_exemption_stays_for_the_others()
    {
        await using var x = await WorldAsync(exempt: true);
        var o = await x.RequestedAsync((x.P2, 3m), (x.P1, 1m));
        Assert.Equal(0m, o.TaxAmount);

        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Tax, LineId: o.Lines[0].Id, TaxPct: 1m, Note: "Venta para reventa sin certificado") });
        var line1 = await LineAsync(x, o.Lines[0].Id);
        var line2 = await LineAsync(x, o.Lines[1].Id);
        Assert.Equal((0.24m, 0m), (line1.TaxAmount, line1.TaxComputedAmount));   // el cálculo de hoy de esa línea sigue exento
        Assert.Equal((0m, true), (line2.TaxAmount, line2.IsTaxExempt));
        Assert.NotEqual(TaxReasons.ManualOverride, line2.TaxReason);
        Assert.Equal(0.24m, (await x.Svc.GetAsync(o.PublicId, default)).TaxAmount);
    }

    [Fact]
    public async Task Without_the_tax_module_a_manual_rate_is_a_422_and_a_bad_rate_or_missing_reason_is_a_400()
    {
        await using var x = await WorldAsync();
        var o = await x.RequestedAsync((x.P2, 3m));
        var lineId = o.Lines[0].Id;
        var orderId = await x.IdOfAsync(o.PublicId);
        var noNote = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.ApplyOpsAsync(orderId,
            new SalesChangeApplyInput(new[] { new ChangeOp(ChangeOpKinds.Tax, LineId: lineId, TaxPct: 1m) }, 1), default));
        Assert.Equal(SalesChangeRules.TaxNoteRequired, noNote.Message);
        x.F.Db.ChangeTracker.Clear();
        var bad = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.ApplyOpsAsync(orderId,
            new SalesChangeApplyInput(new[] { new ChangeOp(ChangeOpKinds.Tax, LineId: lineId, TaxPct: 100.5m, Note: "x") }, 1), default));
        Assert.Equal(SalesChangeRules.TaxPctRange, bad.Message);
        x.F.Db.ChangeTracker.Clear();

        x.F.SetModules(ModuleKeys.SalesOrders);
        var off = await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.ApplyOpsAsync(orderId,
            new SalesChangeApplyInput(new[] { new ChangeOp(ChangeOpKinds.Tax, LineId: lineId, TaxPct: 1m, Note: "x") }, 1), default));
        Assert.Equal(SalesOrderService.ChangeTaxModuleOff, off.Message);
    }
}
