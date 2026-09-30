using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Exportación de la lista de Recibos con sus líneas (GET /api/v1/receipts?includeLines=true) sobre InMemory (ReceivingFixture):
/// - sin includeLines la lista no trae líneas (Lines = null, contrato de siempre);
/// - con includeLines cada recibo de la página trae TODAS sus líneas, iguales a las de la ficha (esperado, recibido,
///   diferencia con signo, lote); un recibo sin líneas trae una lista vacía;
/// - los filtros y la paginación de la lista se respetan;
/// - un recibo de otro tenant (y sus líneas) no aparece aunque su línea apunte a un producto visible.
/// </summary>
public sealed class ReceiptListLinesTests
{
    private static async Task<(ReceiptDetailDto WithVariance, ReceiptDetailDto Exact, ReceiptDetailDto Empty)> SeedAsync(ReceivingFixture f)
    {
        var receipts = f.Get<ReceiptService>();
        // Ciego con diferencia (esperado 5, recibido 3) y una segunda línea con lote.
        var withVariance = await receipts.CreateAsync(new ReceiptCreateRequest(Type: ReceiptTypes.Blind, Carrier: "DHL", Reference: "EXP-1",
            Lines: new[]
            {
                new ReceiptLineRequest(f.ProductNonePublicId, 3m, ExpectedQty: 5m),
                new ReceiptLineRequest(f.ProductLotPublicId, 2m, new LotInput("L-EXP", null, new DateOnly(2027, 1, 31)), ExpectedQty: 2m),
            }), default);
        // Ciego sin diferencia.
        var exact = await receipts.CreateAsync(new ReceiptCreateRequest(Type: ReceiptTypes.Blind,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 4m, ExpectedQty: 4m) }), default);
        // Solo encabezado (sin líneas).
        var empty = await receipts.CreateAsync(new ReceiptCreateRequest(Type: ReceiptTypes.Return), default);
        return (withVariance, exact, empty);
    }

    [Fact]
    public async Task List_without_includeLines_keeps_lines_null()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        await SeedAsync(f);

        var page = await f.Get<ReceiptService>().ListAsync(new ReceiptQuery(), default);

        Assert.Equal(3, page.Total);
        Assert.All(page.Items, i => Assert.Null(i.Lines));
    }

    [Fact]
    public async Task List_with_includeLines_brings_every_line_of_each_receipt_like_the_detail()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var (withVariance, exact, empty) = await SeedAsync(f);
        var receipts = f.Get<ReceiptService>();

        var page = await receipts.ListAsync(new ReceiptQuery(IncludeLines: true), default);

        Assert.Equal(3, page.Items.Count);
        var byId = page.Items.ToDictionary(i => i.PublicId);

        var v = byId[withVariance.Header.PublicId];
        Assert.NotNull(v.Lines);
        Assert.Equal(2, v.Lines!.Count);
        Assert.Equal(v.LineCount, v.Lines.Count);
        Assert.Equal(("PN", 5m, 3m, -2m), (v.Lines[0].Sku, v.Lines[0].ExpectedQty!.Value, v.Lines[0].ReceivedQty, v.Lines[0].VarianceQty));
        Assert.Equal(("PL", "L-EXP", 0m), (v.Lines[1].Sku, v.Lines[1].LotNumber, v.Lines[1].VarianceQty));
        Assert.Equal(TrackingTypes.Lot, v.Lines[1].TrackingTypeCode);
        // Mismas líneas (y en el mismo orden) que la ficha del recibo.
        var detail = await receipts.GetAsync(withVariance.Header.PublicId, default);
        Assert.Equal(detail.Lines.Select(l => (l.Id, l.Sku, l.ExpectedQty, l.ReceivedQty, l.VarianceQty, l.LotNumber, l.StagingBinCode)),
            v.Lines.Select(l => (l.Id, l.Sku, l.ExpectedQty, l.ReceivedQty, l.VarianceQty, l.LotNumber, l.StagingBinCode)));

        var e = byId[exact.Header.PublicId];
        Assert.Equal(0m, Assert.Single(e.Lines!).VarianceQty);

        // Un recibo sin líneas trae una lista vacía (no null).
        Assert.NotNull(byId[empty.Header.PublicId].Lines);
        Assert.Empty(byId[empty.Header.PublicId].Lines!);

        // La ficha sigue sin líneas en el encabezado (van en ReceiptDetailDto.Lines).
        Assert.Null(detail.Header.Lines);
    }

    [Fact]
    public async Task List_with_includeLines_respects_filters_and_paging()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var (withVariance, exact, empty) = await SeedAsync(f);
        var receipts = f.Get<ReceiptService>();

        // Filtro de diferencia: solo el recibo con faltante, con TODAS sus líneas (también la que cuadra).
        var shortOnly = await receipts.ListAsync(new ReceiptQuery(Variance: new[] { ReceiptStatusRules.VarianceShort }, IncludeLines: true), default);
        var only = Assert.Single(shortOnly.Items);
        Assert.Equal(withVariance.Header.PublicId, only.PublicId);
        Assert.Equal(2, only.Lines!.Count);

        // Búsqueda libre y tipo.
        var returns = await receipts.ListAsync(new ReceiptQuery(Types: new[] { ReceiptTypes.Return }, IncludeLines: true), default);
        Assert.Equal(empty.Header.PublicId, Assert.Single(returns.Items).PublicId);
        var byRef = await receipts.ListAsync(new ReceiptQuery(Search: "EXP-1", IncludeLines: true), default);
        Assert.Equal(withVariance.Header.PublicId, Assert.Single(byRef.Items).PublicId);

        // Paginación: cada página trae solo las líneas de sus recibos (orden: más reciente primero).
        var first = await receipts.ListAsync(new ReceiptQuery(Skip: 0, Take: 2, IncludeLines: true), default);
        var second = await receipts.ListAsync(new ReceiptQuery(Skip: 2, Take: 2, IncludeLines: true), default);
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Items.Count);
        var last = Assert.Single(second.Items);
        var allLineIds = first.Items.Concat(second.Items).SelectMany(i => i.Lines!).Select(l => l.Id).ToList();
        Assert.Equal(allLineIds.Distinct().Count(), allLineIds.Count);
        Assert.Equal(3, allLineIds.Count);
        Assert.Contains(exact.Header.PublicId, first.Items.Concat(second.Items).Select(i => i.PublicId));
        Assert.Equal(last.LineCount, last.Lines!.Count);
    }

    [Fact]
    public async Task List_with_includeLines_does_not_leak_receipts_or_lines_of_another_tenant()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var (withVariance, _, _) = await SeedAsync(f);

        // Recibo de OTRO tenant con una línea que apunta a un producto visible: ni el recibo ni su línea deben salir.
        var foreign = new ReceiptHeader
        {
            ReceiptHeaderId = 9001, PublicId = Guid.NewGuid(), TenantId = ReceivingFixture.TenantId + 1, WarehouseId = f.WarehouseId,
            ReceiptTypeLookupId = f.LookupId(LookupDomains.ReceiptType, ReceiptTypes.Blind), Number = "REC-99999",
            StatusCodeId = f.StatusId(StatusDomains.ReceiptStatus, ReceiptStatuses.Expected), IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(5),
        };
        f.Db.Set<ReceiptHeader>().Add(foreign);
        f.Db.Set<ReceiptLine>().Add(new ReceiptLine { ReceiptLineId = 9101, ReceiptHeaderId = 9001, ProductId = f.ProductNoneId, ReceivedQty = 77m, ExpectedQty = 70m });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var page = await f.Get<ReceiptService>().ListAsync(new ReceiptQuery(IncludeLines: true), default);

        Assert.Equal(3, page.Total);
        Assert.DoesNotContain(page.Items, i => i.PublicId == foreign.PublicId || i.Number == "REC-99999");
        Assert.DoesNotContain(page.Items.SelectMany(i => i.Lines!), l => l.Id == 9101 || l.ReceivedQty == 77m);
        Assert.Contains(page.Items, i => i.PublicId == withVariance.Header.PublicId);
    }
}
