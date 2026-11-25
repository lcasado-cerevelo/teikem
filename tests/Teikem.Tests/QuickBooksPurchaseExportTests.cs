using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 246 (Lote F, P5): QuickBooks de compras — la lista de por pasar (qué entra y qué no), proveedores y productos nuevos, avisos, exportar por ids o todas (anota, no
// marca), errores con su código, «Sí, ya están», «No pasó», marcar y quitar la marca, la cuenta de órdenes de compra en los ajustes, permisos y aislamiento por tenant.
public sealed class QuickBooksPurchaseExportTests
{
    private sealed class World : IAsyncDisposable
    {
        public WmsFixture F { get; init; } = null!;
        public Warehouse W { get; init; } = null!;
        public QuickBooksService Qb => F.Get<QuickBooksService>();
        private int _n;

        public static async Task<World> CreateAsync()
        {
            var f = await WmsFixture.CreateAsync(s =>
            {
                s.AddSingleton<ITenantClock>(TenantClock.Default);
                s.AddSingleton<QuickBooksService>();
            });
            // términos de pago (no los trae el fixture): al final, sin tocar los ids anteriores
            var all = await f.Db.LookupCodes.AsNoTracking().ToListAsync();
            var id = all.Max(l => l.LookupCodeId) + 1;
            var terms = new[] { "NET30", "COD" }.Select(c => new LookupCode { LookupCodeId = id++, Entity = LookupDomains.PaymentTerm, InternalCode = c, LabelJson = $"{{\"es\":\"{c}\"}}", IsActive = true }).ToList();
            f.Db.LookupCodes.AddRange(terms);
            f.Db.Users.Add(new ApplicationUser { Id = 1, UserName = "ana@t.local", Email = "ana@t.local", FullName = "Ana Compras", IsActive = true });
            await f.Db.SaveChangesAsync();
            f.Lookups.Load(all.Concat(terms));
            f.Db.ChangeTracker.Clear();
            return new World { F = f, W = await f.AddWarehouseAsync("W1") };
        }

        public int Lookup(string domain, string code) => F.Db.LookupCodes.AsNoTracking().Single(l => l.Entity == domain && l.InternalCode == code).LookupCodeId;

        public async Task<Supplier> SupplierAsync(string name, Action<Supplier>? edit = null, int? tenantId = null)
        {
            var s = new Supplier { TenantId = tenantId ?? WmsFixture.TenantId, Name = name, ContactName = "Pedro", Phone = "7875550100", PhoneCountryCallingCode = "1", Email = "p@prov.test", IsActive = true };
            edit?.Invoke(s);
            F.Db.Suppliers.Add(s);
            await F.Db.SaveChangesAsync();
            F.Db.ChangeTracker.Clear();
            return s;
        }

        public async Task<PurchaseOrder> PoAsync(Supplier s, string status, (Product P, decimal Qty, decimal Cost)[] lines, string? origin = null, string? notes = null,
            Action<PurchaseOrder>? edit = null, int? tenantId = null, int? warehouseId = null)
        {
            var po = new PurchaseOrder
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId ?? WmsFixture.TenantId, SupplierId = s.SupplierId, WarehouseId = warehouseId ?? W.WarehouseId,
                Number = $"PO-{++_n:00000}", OrderDate = new DateOnly(2026, 11, 1).AddDays(_n), ExpectedDate = new DateOnly(2026, 12, 1),
                StatusCodeId = F.StatusId(StatusDomains.PurchaseOrderStatus, status), Notes = notes, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
                OriginLookupId = origin is null ? null : F.LookupId(PurchaseOrderOrigins.Domain, origin),
            };
            foreach (var l in lines) po.Lines.Add(new PurchaseOrderLine { ProductId = l.P.ProductId, QtyOrdered = l.Qty, UnitCost = l.Cost });
            edit?.Invoke(po);
            F.Db.PurchaseOrders.Add(po);
            await F.Db.SaveChangesAsync();
            F.Db.ChangeTracker.Clear();
            return po;
        }

        public Task<PurchaseOrder> RowAsync(PurchaseOrder po) => F.Db.PurchaseOrders.AsNoTracking().SingleAsync(o => o.PurchaseOrderId == po.PurchaseOrderId);

        public async Task<string> StateAsync(PurchaseOrder po)
        {
            var row = await RowAsync(po);
            var origin = row.OriginLookupId is int o ? F.Db.LookupCodes.AsNoTracking().Single(l => l.LookupCodeId == o).InternalCode : null;
            return PurchaseOrderQuickBooksRules.State(origin, F.StatusCodeOf(row.StatusCodeId), row.QuickBooksExportedAt, row.QuickBooksAt);
        }

        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    private static string Text(PurchaseQuickBooksExportDto dto)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetString(Convert.FromBase64String(dto.ContentBase64));
    }

    private static string[][] Rows(string iif, string kind)
        => iif.Split("\r\n").Where(l => l.StartsWith(kind + "\t", StringComparison.Ordinal) || l == kind).Select(l => l.Split('\t')).ToArray();

    private static PurchaseQuickBooksSelectionRequest Ids(params PurchaseOrder[] pos) => new(pos.Select(p => p.PublicId).ToList());
    private static PurchaseQuickBooksExportRequest ExportIds(params PurchaseOrder[] pos) => new(pos.Select(p => p.PublicId).ToList());

    [Fact]
    public async Task Pending_takes_sent_partial_and_received_from_teikem_and_excel_only()
    {
        await using var x = await World.CreateAsync();
        var p = await x.F.AddProductAsync("MED", purchaseCost: 4m);
        var s = await x.SupplierAsync("Proveedor Uno");
        var sent = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 2m, 5m) });                                         // origen NULL = TEIKEM
        var partial = await x.PoAsync(s, PurchaseOrderStatuses.Partial, new[] { (p, 1m, 5m) }, PurchaseOrderOrigins.Excel);
        var received = await x.PoAsync(s, PurchaseOrderStatuses.Received, new[] { (p, 3m, 5m) }, PurchaseOrderOrigins.Teikem);
        await x.PoAsync(s, PurchaseOrderStatuses.Draft, new[] { (p, 1m, 5m) });
        await x.PoAsync(s, PurchaseOrderStatuses.Cancelled, new[] { (p, 1m, 5m) });
        await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 5m) }, PurchaseOrderOrigins.QuickBooks);
        await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 5m) }, edit: o => o.IsActive = false);
        await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 5m) }, edit: o => (o.QuickBooksAt, o.QuickBooksBy) = (DateTime.UtcNow, 1));
        // la de otra compañía no se ve
        var otherW = await x.F.AddWarehouseAsync("W2", WmsFixture.OtherTenantId);
        var otherS = await x.SupplierAsync("Proveedor Otro", tenantId: WmsFixture.OtherTenantId);
        await x.PoAsync(otherS, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 5m) }, tenantId: WmsFixture.OtherTenantId, warehouseId: otherW.WarehouseId);

        var pending = await x.Qb.GetPurchasePendingAsync(default);
        Assert.Equal(new[] { sent.Number, partial.Number, received.Number }, pending.Orders.Select(o => o.Number));
        Assert.Equal((3, 30m, 0, (DateTime?)null), (pending.Count, pending.Total, pending.AlreadyExportedCount, pending.LastExportedAtUtc));
        Assert.Equal(QuickBooksRules.DefaultPurchaseOrderAccount, pending.PurchaseOrderAccount);
        var row = pending.Orders[1];
        Assert.Equal((PurchaseOrderStatuses.Partial, PurchaseOrderOrigins.Excel, 1, 5m, 5m), (row.StatusCode, row.OriginCode, row.Lines, row.Total, row.FileTotal));
        Assert.Equal(PurchaseOrderOrigins.Teikem, pending.Orders[0].OriginCode);
        Assert.True(row.NewSupplier);
        Assert.Equal(new[] { "MED" }, row.NewProductSkus);
        var ns = Assert.Single(pending.NewSuppliers);
        Assert.Equal(("Proveedor Uno", "Pedro", "+17875550100", "p@prov.test"), (ns.QuickBooksName, ns.ContactName, ns.Phone, ns.Email));
        Assert.Equal("MED", Assert.Single(pending.NewProducts).Sku);
        Assert.Empty(pending.Warnings);
    }

    [Fact]
    public async Task Pending_lists_the_non_blocking_warnings_and_known_suppliers_are_not_new()
    {
        await using var x = await World.CreateAsync();
        var longSku = new string('S', 35);
        var p = await x.F.AddProductAsync("MED");
        var pLong = await x.F.AddProductAsync(longSku);
        var pOmega = await x.F.AddProductAsync("OHM-漢");
        var longName = "Proveedor con un nombre larguísimo de más de 41";
        var sLong = await x.SupplierAsync(longName, s => s.PaymentTermLookupId = x.Lookup(LookupDomains.PaymentTerm, "NET30"));
        var known = await x.SupplierAsync("Proveedor Conocido", s => s.QuickBooksSentAt = DateTime.UtcNow);   // relleno del SQL: ya está en QuickBooks
        var rounding = await x.PoAsync(sLong, PurchaseOrderStatuses.Sent, new[] { (p, 3m, 0.3333m), (pLong, 1m, 1m) }, notes: "Pedido urgente ✓ [IMP:abc|1]");
        var eur = await x.PoAsync(known, PurchaseOrderStatuses.Sent, new[] { (pOmega, 1m, 2m) },
            edit: o => o.CurrencyLookupId = x.F.LookupId(LookupDomains.Currency, "EUR"));

        var pending = await x.Qb.GetPurchasePendingAsync(default);
        Assert.Equal(new[] { longName }, pending.NewSuppliers.Select(s => s.Name));   // el conocido no sale como nuevo
        Assert.False(pending.Orders.Single(o => o.Number == eur.Number).NewSupplier);
        var newSupplier = pending.NewSuppliers.Single();
        Assert.Equal(("NET30", (string?)null), (newSupplier.PaymentTerm, newSupplier.QuickBooksTerms));
        var r = pending.Orders.Single(o => o.Number == rounding.Number);
        Assert.Equal((1.9999m, 2.00m), (r.Total, r.FileTotal));
        Assert.Contains(QuickBooksRules.VendorNameTooLong(longName), pending.Warnings);
        Assert.Contains(QuickBooksRules.ItemNameTooLong(longSku), pending.Warnings);
        Assert.Contains(PurchaseOrderQuickBooksRules.UnmappedSupplierTerm("NET30"), pending.Warnings);
        Assert.Contains(PurchaseOrderQuickBooksRules.RoundingDifference(rounding.Number, 1.9999m, 2.00m), pending.Warnings);
        Assert.Contains(PurchaseOrderQuickBooksRules.CurrencyDiffers(eur.Number, "EUR", "USD"), pending.Warnings);
        Assert.Contains(PurchaseOrderQuickBooksRules.UnencodableText($"El memo de la orden de compra {rounding.Number}", "✓"), pending.Warnings);
        Assert.Contains(PurchaseOrderQuickBooksRules.UnencodableText("El SKU «OHM-漢»", "漢"), pending.Warnings);

        // con el término ya nombrado en QuickBooks, ese aviso desaparece y el proveedor nuevo lo lleva
        await x.Qb.UpdateSettingsAsync(new QuickBooksSettingsRequest(Terms: new Dictionary<string, string?> { ["NET30"] = "Net 30" }), default);
        var again = await x.Qb.GetPurchasePendingAsync(default);
        Assert.DoesNotContain(PurchaseOrderQuickBooksRules.UnmappedSupplierTerm("NET30"), again.Warnings);
        Assert.Equal("Net 30", again.NewSuppliers.Single().QuickBooksTerms);
    }

    [Fact]
    public async Task Export_by_ids_and_all_annotates_the_file_date_but_does_not_mark()
    {
        await using var x = await World.CreateAsync();
        var p = await x.F.AddProductAsync("MED:1", purchaseCost: 4m);
        var s = await x.SupplierAsync("Proveedor Uno", e => e.PaymentTermLookupId = x.Lookup(LookupDomains.PaymentTerm, "NET30"));
        var po1 = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 2m, 5.25m) }, PurchaseOrderOrigins.Excel, "Notas\tcon tab [IMP:x|1]");
        var po2 = await x.PoAsync(s, PurchaseOrderStatuses.Received, new[] { (p, 1m, 3m) });
        await x.Qb.UpdateSettingsAsync(new QuickBooksSettingsRequest(Terms: new Dictionary<string, string?> { ["NET30"] = "Net 30" }), default);

        var first = await x.Qb.ExportPurchaseOrdersAsync(ExportIds(po1), default);
        Assert.Equal((1, 10.50m, 0, true, 1, 1), (first.OrderCount, first.Total, first.AlreadyExportedCount, first.IncludedNew, first.NewSupplierCount, first.NewProductCount));
        Assert.StartsWith("teikem-quickbooks-compras-", first.FileName);
        Assert.EndsWith(".iif", first.FileName);
        var iif = Text(first);
        var vend = Assert.Single(Rows(iif, "VEND"));
        Assert.Equal(new[] { "VEND", "Proveedor Uno", "Proveedor Uno", "Pedro", "+17875550100", "p@prov.test", "Net 30", "" }, vend);
        Assert.Equal("MED-1", Assert.Single(Rows(iif, "INVITEM"))[1]);
        var trns = Assert.Single(Rows(iif, "TRNS"));
        Assert.Equal(("PURCHORD", "Purchase Orders", "Proveedor Uno", "-10.50", po1.Number, "Notas con tab", "12/01/2026"), (trns[2], trns[4], trns[5], trns[6], trns[7], trns[8], trns[11]));
        var spl = Assert.Single(Rows(iif, "SPL"));
        Assert.Equal(("PURCHORD", "Inventory Asset", "10.50", "2", "5.25", "MED-1"), (spl[2], spl[4], spl[6], spl[10], spl[11], spl[12]));
        Assert.Single(Rows(iif, "ENDTRNS"));
        var row = Assert.Single(first.Orders);
        Assert.Equal((first.ExportedAtUtc, "Ana Compras"), (row.ExportedAtUtc!.Value, row.ExportedByName));

        // exportar no marca: sigue por pasar, con la fecha y quién
        var after = await x.Qb.GetPurchasePendingAsync(default);
        Assert.Equal((2, 1), (after.Count, after.AlreadyExportedCount));
        Assert.Equal(first.ExportedAtUtc, after.LastExportedAtUtc);
        var po1Row = await x.RowAsync(po1);
        Assert.Equal((first.ExportedAtUtc, (int?)1, (DateTime?)null), (po1Row.QuickBooksExportedAt!.Value, po1Row.QuickBooksExportedBy, po1Row.QuickBooksAt));
        Assert.Equal(QuickBooksSyncStates.Exported, await x.StateAsync(po1));
        Assert.Equal(QuickBooksSyncStates.Pending, await x.StateAsync(po2));

        // todas, sin los nuevos: las dos órdenes, sin !VEND ni !INVITEM, y avisa cuántas ya habían salido
        var second = await x.Qb.ExportPurchaseOrdersAsync(new PurchaseQuickBooksExportRequest(All: true, IncludeNew: false), default);
        Assert.Equal((2, 1, false, 0, 0), (second.OrderCount, second.AlreadyExportedCount, second.IncludedNew, second.NewSupplierCount, second.NewProductCount));
        Assert.Equal(first.ExportedAtUtc, second.LastExportedAtUtc);
        var iif2 = Text(second);
        Assert.Empty(Rows(iif2, "VEND"));
        Assert.Empty(Rows(iif2, "INVITEM"));
        Assert.DoesNotContain("!VEND", iif2);
        Assert.Equal(2, Rows(iif2, "TRNS").Length);
        Assert.Equal(QuickBooksSyncStates.Exported, await x.StateAsync(po2));
    }

    [Fact]
    public async Task Export_errors_come_with_their_exact_message_and_status()
    {
        await using var x = await World.CreateAsync();
        var p = await x.F.AddProductAsync("MED");
        var s = await x.SupplierAsync("Proveedor Uno");

        var none = await Assert.ThrowsAsync<StatusRuleException>(() => x.Qb.ExportPurchaseOrdersAsync(new PurchaseQuickBooksExportRequest(All: true), default));
        Assert.Equal(PurchaseOrderQuickBooksRules.NothingPending, none.Message);
        var empty = await Assert.ThrowsAsync<ValidationException>(() => x.Qb.ExportPurchaseOrdersAsync(null, default));
        Assert.Equal(PurchaseOrderQuickBooksRules.SelectionRequired, empty.Message);
        var many = await Assert.ThrowsAsync<ValidationException>(() => x.Qb.ExportPurchaseOrdersAsync(
            new PurchaseQuickBooksExportRequest(Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToList()), default));
        Assert.Equal(PurchaseOrderQuickBooksRules.TooManyIds, many.Message);

        var sent = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) });
        var missing = await Assert.ThrowsAsync<ValidationException>(() => x.Qb.ExportPurchaseOrdersAsync(
            new PurchaseQuickBooksExportRequest(new[] { sent.PublicId, Guid.NewGuid() }), default));
        Assert.Equal(PurchaseOrderQuickBooksRules.NotFound, missing.Message);
        // la de otra compañía cuenta como inexistente (sin oráculo)
        var otherW = await x.F.AddWarehouseAsync("W2", WmsFixture.OtherTenantId);
        var otherS = await x.SupplierAsync("Proveedor Otro", tenantId: WmsFixture.OtherTenantId);
        var foreign = await x.PoAsync(otherS, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) }, tenantId: WmsFixture.OtherTenantId, warehouseId: otherW.WarehouseId);
        Assert.Equal(PurchaseOrderQuickBooksRules.NotFound, (await Assert.ThrowsAsync<ValidationException>(() => x.Qb.ExportPurchaseOrdersAsync(ExportIds(foreign), default))).Message);
        var inactive = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) }, edit: o => o.IsActive = false);
        Assert.Equal(PurchaseOrderQuickBooksRules.NotFound, (await Assert.ThrowsAsync<ValidationException>(() => x.Qb.ExportPurchaseOrdersAsync(ExportIds(inactive), default))).Message);

        var fromQb = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) }, PurchaseOrderOrigins.QuickBooks);
        Assert.Equal(PurchaseOrderQuickBooksRules.FromQuickBooks(fromQb.Number),
            (await Assert.ThrowsAsync<StatusRuleException>(() => x.Qb.ExportPurchaseOrdersAsync(ExportIds(fromQb), default))).Message);
        var draft = await x.PoAsync(s, PurchaseOrderStatuses.Draft, new[] { (p, 1m, 1m) });
        Assert.Equal(PurchaseOrderQuickBooksRules.NotExportableStatus(draft.Number, PurchaseOrderStatuses.Draft),
            (await Assert.ThrowsAsync<StatusRuleException>(() => x.Qb.ExportPurchaseOrdersAsync(ExportIds(sent, draft), default))).Message);
        Assert.Null((await x.RowAsync(sent)).QuickBooksExportedAt);   // una mala = no sale ninguna

        // marcada y después exportada → 409 (el estado se lee después del candado)
        await x.Qb.MarkPurchaseOrdersAsync(Ids(sent), default);
        var marked = await Assert.ThrowsAsync<ConflictException>(() => x.Qb.ExportPurchaseOrdersAsync(ExportIds(sent), default));
        Assert.Equal(PurchaseOrderQuickBooksRules.AlreadyInQuickBooks(sent.Number), marked.Message);
        // y ya no hay nada por pasar
        Assert.Equal(PurchaseOrderQuickBooksRules.NothingPending,
            (await Assert.ThrowsAsync<StatusRuleException>(() => x.Qb.ExportPurchaseOrdersAsync(new PurchaseQuickBooksExportRequest(All: true), default))).Message);
    }

    [Fact]
    public async Task Confirm_marks_only_exported_ones_and_their_suppliers_and_products_stop_being_new()
    {
        await using var x = await World.CreateAsync();
        var p = await x.F.AddProductAsync("MED");
        var s = await x.SupplierAsync("Proveedor Uno");
        var po1 = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) });
        var po2 = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) });

        var never = await Assert.ThrowsAsync<StatusRuleException>(() => x.Qb.ConfirmPurchaseOrdersAsync(Ids(po1), default));
        Assert.Equal(PurchaseOrderQuickBooksRules.NotExported(po1.Number), never.Message);
        Assert.Equal(PurchaseOrderQuickBooksRules.SelectionRequired,
            (await Assert.ThrowsAsync<ValidationException>(() => x.Qb.ConfirmPurchaseOrdersAsync(new PurchaseQuickBooksSelectionRequest(All: true), default))).Message);

        await x.Qb.ExportPurchaseOrdersAsync(ExportIds(po1), default);
        var res = await x.Qb.ConfirmPurchaseOrdersAsync(Ids(po1), default);
        Assert.Equal((1, 0, 1, 1), (res.Marked, res.AlreadyMarked, res.SuppliersSent, res.ProductsSent));
        var row = await x.RowAsync(po1);
        Assert.Equal((res.AtUtc, (int?)1), (row.QuickBooksAt!.Value, row.QuickBooksBy));
        Assert.Equal(QuickBooksSyncStates.InQuickBooks, await x.StateAsync(po1));
        Assert.NotNull((await x.F.Db.Suppliers.AsNoTracking().SingleAsync(e => e.SupplierId == s.SupplierId)).QuickBooksSentAt);
        Assert.NotNull((await x.F.Db.Products.AsNoTracking().SingleAsync(e => e.ProductId == p.ProductId)).QuickBooksSentAt);

        var pending = await x.Qb.GetPurchasePendingAsync(default);
        Assert.Equal(new[] { po2.Number }, pending.Orders.Select(o => o.Number));
        Assert.Empty(pending.NewSuppliers);
        Assert.Empty(pending.NewProducts);
        var again = await x.Qb.ConfirmPurchaseOrdersAsync(Ids(po1), default);
        Assert.Equal((0, 1, 0, 0), (again.Marked, again.AlreadyMarked, again.SuppliersSent, again.ProductsSent));
        Assert.Equal(res.AtUtc, (await x.RowAsync(po1)).QuickBooksAt);   // ni quién ni cuándo cambian
    }

    [Fact]
    public async Task Not_passed_returns_exported_ones_to_pending_and_refuses_marked_or_quickbooks_ones()
    {
        await using var x = await World.CreateAsync();
        var p = await x.F.AddProductAsync("MED");
        var s = await x.SupplierAsync("Proveedor Uno");
        var exported = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) });
        var cancelled = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) });
        var neverOut = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) });
        var fromQb = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) }, PurchaseOrderOrigins.QuickBooks);
        await x.Qb.ExportPurchaseOrdersAsync(ExportIds(exported, cancelled), default);
        // la cancelada después de salir sigue «exportada, por confirmar»
        var c = await x.F.Db.PurchaseOrders.AsTracking().SingleAsync(o => o.PurchaseOrderId == cancelled.PurchaseOrderId);
        c.StatusCodeId = x.F.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Cancelled);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(QuickBooksSyncStates.Exported, await x.StateAsync(cancelled));

        var res = await x.Qb.NotPassedPurchaseOrdersAsync(Ids(exported, cancelled, neverOut), default);
        Assert.Equal((2, 1), (res.Reset, res.Unchanged));
        var row = await x.RowAsync(exported);
        Assert.Equal(((DateTime?)null, (int?)null), (row.QuickBooksExportedAt, row.QuickBooksExportedBy));
        Assert.Equal(QuickBooksSyncStates.Pending, await x.StateAsync(exported));
        Assert.Equal(QuickBooksSyncStates.NotExportable, await x.StateAsync(cancelled));
        Assert.Equal(QuickBooksSyncStates.Pending, await x.StateAsync(neverOut));

        Assert.Equal(PurchaseOrderQuickBooksRules.FromQuickBooks(fromQb.Number),
            (await Assert.ThrowsAsync<StatusRuleException>(() => x.Qb.NotPassedPurchaseOrdersAsync(Ids(fromQb), default))).Message);
        await x.Qb.ExportPurchaseOrdersAsync(ExportIds(exported), default);
        await x.Qb.ConfirmPurchaseOrdersAsync(Ids(exported), default);
        Assert.Equal(PurchaseOrderQuickBooksRules.NotPassedMarked(exported.Number),
            (await Assert.ThrowsAsync<ConflictException>(() => x.Qb.NotPassedPurchaseOrdersAsync(Ids(exported), default))).Message);
    }

    [Fact]
    public async Task Mark_by_hand_and_unmark_follow_the_eligibility_rules()
    {
        await using var x = await World.CreateAsync();
        var p = await x.F.AddProductAsync("MED");
        var s = await x.SupplierAsync("Proveedor Uno");
        var a = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) });
        var b = await x.PoAsync(s, PurchaseOrderStatuses.Partial, new[] { (p, 1m, 1m) });
        var c = await x.PoAsync(s, PurchaseOrderStatuses.Received, new[] { (p, 1m, 1m) });
        var draft = await x.PoAsync(s, PurchaseOrderStatuses.Draft, new[] { (p, 1m, 1m) });
        var fromQb = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) }, PurchaseOrderOrigins.QuickBooks);

        Assert.Equal(PurchaseOrderQuickBooksRules.NotExportableStatus(draft.Number, PurchaseOrderStatuses.Draft),
            (await Assert.ThrowsAsync<StatusRuleException>(() => x.Qb.MarkPurchaseOrdersAsync(Ids(draft), default))).Message);
        Assert.Equal(PurchaseOrderQuickBooksRules.FromQuickBooks(fromQb.Number),
            (await Assert.ThrowsAsync<StatusRuleException>(() => x.Qb.MarkPurchaseOrdersAsync(Ids(fromQb), default))).Message);
        Assert.Equal(PurchaseOrderQuickBooksRules.FromQuickBooks(fromQb.Number),
            (await Assert.ThrowsAsync<StatusRuleException>(() => x.Qb.UnmarkPurchaseOrdersAsync(Ids(fromQb), default))).Message);

        var one = await x.Qb.MarkPurchaseOrdersAsync(Ids(a), default);
        Assert.Equal((1, 0, 1, 1), (one.Marked, one.AlreadyMarked, one.SuppliersSent, one.ProductsSent));
        await x.Qb.ExportPurchaseOrdersAsync(ExportIds(b), default);
        var all = await x.Qb.MarkPurchaseOrdersAsync(new PurchaseQuickBooksSelectionRequest(All: true), default);
        Assert.Equal((2, 0, 0, 0), (all.Marked, all.AlreadyMarked, all.SuppliersSent, all.ProductsSent));   // b y c; a ya estaba
        var ids = await x.Qb.MarkPurchaseOrdersAsync(Ids(a, b), default);
        Assert.Equal((0, 2), (ids.Marked, ids.AlreadyMarked));
        Assert.Equal(one.AtUtc, (await x.RowAsync(a)).QuickBooksAt);
        Assert.Equal(0, (await x.Qb.GetPurchasePendingAsync(default)).Count);
        Assert.Equal(QuickBooksSyncStates.NotExportable, await x.StateAsync(draft));

        // quitar la marca: la que salió en un archivo vuelve a «exportada», la que no a por pasar
        var un = await x.Qb.UnmarkPurchaseOrdersAsync(Ids(a, b, draft), default);
        Assert.Equal((2, 1), (un.Marked, un.AlreadyMarked));
        Assert.Equal(QuickBooksSyncStates.Pending, await x.StateAsync(a));
        Assert.Equal(QuickBooksSyncStates.Exported, await x.StateAsync(b));
        Assert.Equal(QuickBooksSyncStates.InQuickBooks, await x.StateAsync(c));
        Assert.NotNull((await x.F.Db.Suppliers.AsNoTracking().SingleAsync(e => e.SupplierId == s.SupplierId)).QuickBooksSentAt);   // siguen como mandados
        Assert.Equal(new[] { a.Number, b.Number }, (await x.Qb.GetPurchasePendingAsync(default)).Orders.Select(o => o.Number));
    }

    [Fact]
    public async Task The_purchase_order_account_is_a_setting_and_goes_in_the_file()
    {
        await using var x = await World.CreateAsync();
        Assert.Equal(QuickBooksRules.DefaultPurchaseOrderAccount, (await x.Qb.GetSettingsAsync(default)).PurchaseOrderAccount);
        var changed = await x.Qb.UpdateSettingsAsync(new QuickBooksSettingsRequest(PurchaseOrderAccount: "  Órdenes de compra  "), default);
        Assert.Equal("Órdenes de compra", changed.PurchaseOrderAccount);
        Assert.Equal(QuickBooksRules.DefaultSalesAccount, changed.SalesAccount);   // lo demás no cambia

        var p = await x.F.AddProductAsync("MED");
        var s = await x.SupplierAsync("Proveedor Uno");
        var po = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) });
        Assert.Equal("Órdenes de compra", (await x.Qb.GetPurchasePendingAsync(default)).PurchaseOrderAccount);
        var file = await x.Qb.ExportPurchaseOrdersAsync(ExportIds(po), default);
        Assert.Equal("Órdenes de compra", Assert.Single(Rows(Text(file), "TRNS"))[4]);

        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => x.Qb.UpdateSettingsAsync(new QuickBooksSettingsRequest(PurchaseOrderAccount: new string('A', 160)), default));
        Assert.Equal(QuickBooksRules.AccountTooLong, tooLong.Message);
        Assert.Equal(QuickBooksRules.DefaultPurchaseOrderAccount, (await x.Qb.UpdateSettingsAsync(new QuickBooksSettingsRequest(PurchaseOrderAccount: ""), default)).PurchaseOrderAccount);
        Assert.Null((await x.F.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId)).QuickBooksPurchaseOrderAccount);
    }

    [Fact]
    public async Task Without_purchasing_quickbooks_everything_is_403_and_each_company_sees_only_its_own()
    {
        await using var x = await World.CreateAsync();
        var p = await x.F.AddProductAsync("MED");
        var s = await x.SupplierAsync("Proveedor Uno");
        var po = await x.PoAsync(s, PurchaseOrderStatuses.Sent, new[] { (p, 1m, 1m) });

        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Equal(0, (await x.Qb.GetPurchasePendingAsync(default)).Count);

        x.F.SetPermissions(PermissionCatalog.PurchasingView, PermissionCatalog.PurchasingManage, PermissionCatalog.SalesQuickBooksExport, PermissionCatalog.SalesQuickBooksMark);
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Qb.GetPurchasePendingAsync(default));
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Qb.ExportPurchaseOrdersAsync(ExportIds(po), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Qb.ConfirmPurchaseOrdersAsync(Ids(po), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Qb.NotPassedPurchaseOrdersAsync(Ids(po), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Qb.MarkPurchaseOrdersAsync(Ids(po), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Qb.UnmarkPurchaseOrdersAsync(Ids(po), default));
        Assert.Null((await x.RowAsync(po)).QuickBooksExportedAt);

        x.F.SetPermissions(PermissionCatalog.PurchasingQuickBooks);
        Assert.Equal(1, (await x.Qb.GetPurchasePendingAsync(default)).Count);
    }
}
