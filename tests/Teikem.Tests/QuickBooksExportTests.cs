using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.CustomFields;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 225 (P5 del pedido de vendedores): QuickBooks — todas las no marcadas salen siempre; aviso de «ya salieron»; marcar a mano y confirmar; clientes y productos solo la
// primera vez; kits por componente; ajustes (cuentas, ítem de IVU, términos); permisos; «Necesita tu atención», panel Ventas y aviso de la mañana.
public sealed class QuickBooksExportTests
{
    private static Task<SalesVendorWorld> WorldAsync() => SalesVendorWorld.CreateAsync(s =>
    {
        s.AddSingleton<QuickBooksService>();
        s.AddSingleton<SalesQuickBooksAttentionProvider>();
    });

    private static QuickBooksService Qb(SalesVendorWorld x) => x.F.Get<QuickBooksService>();

    private static AttentionScope Scope => new(WmsFixture.TenantId, 1, "es", new HashSet<string> { ModuleKeys.SalesOrders });

    private static string Text(QuickBooksExportDto dto)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetString(Convert.FromBase64String(dto.ContentBase64));
    }

    private static string[] Rows(string iif, string kind) => iif.Split("\r\n").Where(l => l.StartsWith(kind + "\t", StringComparison.Ordinal)).ToArray();

    /// <summary>
    /// Una factura del cliente con una línea por producto (de una orden de venta real: el número va en el memo). status = estatus de la factura; extra = líneas de más
    /// (cargo de envío, flete…). salesLines = false: una factura sin nada de ventas (solo flete).
    /// </summary>
    private static async Task<Invoice> InvoiceAsync(SalesVendorWorld x, string number, Client c, (Product P, decimal Qty, decimal Price)[] lines, decimal tax = 0m,
        string status = InvoiceStatuses.Issued, bool salesLines = true, Func<SalesOrder, IEnumerable<InvoiceLine>>? extra = null, DateOnly? issue = null)
    {
        var draft = await x.Sales.CreateAsync(new SalesOrderRequest(WarehousePublicId: x.W.PublicId, ClientPublicId: c.PublicId,
            Lines: lines.Select(l => new SalesOrderLineRequest(l.P.PublicId, l.Qty)).ToArray()), default);
        var so = await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.PublicId == draft.PublicId);
        var soLines = await x.F.Db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == so.SalesOrderId).OrderBy(l => l.LineNo).ToListAsync();
        var productSale = x.Lookup(BillingCodes.ChargeTypeDomain, BillingCodes.ProductSale);
        var freight = x.Lookup(BillingCodes.ChargeTypeDomain, "FREIGHT");
        var invoice = new Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, InvoiceNumber = number, StatusCodeId = x.F.StatusId(BillingCodes.InvoiceStatusDomain, status),
            IssueDate = issue ?? new DateOnly(2026, 11, 2), DueDate = (issue ?? new DateOnly(2026, 11, 2)).AddDays(30), CurrencyLookupId = 1, TaxAmount = tax, IsActive = true,
        };
        for (var i = 0; i < lines.Length; i++)
            invoice.Lines.Add(salesLines
                ? new InvoiceLine { ProductId = lines[i].P.ProductId, ChargeTypeLookupId = productSale, Description = lines[i].P.Sku, Quantity = lines[i].Qty, UnitAmount = lines[i].Price,
                    SalesOrderLineId = soLines[i].SalesOrderLineId, TaxRatePct = tax > 0m ? 11.5m : 0m }
                : new InvoiceLine { ChargeTypeLookupId = freight, Description = "Flete", Quantity = 1m, UnitAmount = lines[i].Price });
        foreach (var l in extra?.Invoke(so) ?? Array.Empty<InvoiceLine>()) invoice.Lines.Add(l);
        invoice.Subtotal = invoice.Lines.Sum(l => Math.Round(l.Quantity * l.UnitAmount, 2));
        x.F.Db.Set<Invoice>().Add(invoice);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return invoice;
    }

    private static Task<Invoice> SimpleAsync(SalesVendorWorld x, string number, Client c, Product p, string status = InvoiceStatuses.Issued)
        => InvoiceAsync(x, number, c, new[] { (p, 2m, 10m) }, 2.30m, status);

    private static Task<Invoice> RowAsync(SalesVendorWorld x, Guid id) => x.F.Db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.PublicId == id);

    [Fact]
    public async Task Only_issued_sales_invoices_are_pending_and_export_always_takes_all_the_unmarked_ones()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        Assert.Equal(0, (await Qb(x).GetPendingAsync(default)).Count);
        var none = await Assert.ThrowsAsync<StatusRuleException>(() => Qb(x).ExportAsync(null, default));
        Assert.Equal(QuickBooksRules.NothingPending, none.Message);

        var inv1 = await SimpleAsync(x, "FAC-1", c, p);
        await SimpleAsync(x, "FAC-DRAFT", c, p, InvoiceStatuses.Draft);   // el borrador no
        await SimpleAsync(x, "FAC-VOID", c, p, InvoiceStatuses.Void);     // la anulada no
        await InvoiceAsync(x, "FAC-FLETE", c, new[] { (p, 1m, 50m) }, salesLines: false);   // la de solo flete (transporte) no
        var paid = await SimpleAsync(x, "FAC-PAID", c, p, InvoiceStatuses.Paid);   // la pagada sí

        var pending = await Qb(x).GetPendingAsync(default);
        Assert.Equal(new[] { "FAC-1", "FAC-PAID" }, pending.Invoices.Select(i => i.Number).OrderBy(n => n));
        Assert.Equal((2, 44.60m, 0), (pending.Count, pending.Total, pending.AlreadyExportedCount));
        var row = pending.Invoices.Single(i => i.Number == "FAC-1");
        Assert.True(row.NewClient);
        Assert.Equal(new[] { "MED" }, row.NewProductSkus);
        Assert.Single(row.OrderNumbers);
        Assert.Null(row.ExportedAtUtc);
        Assert.Equal("C1", Assert.Single(pending.NewClients).Code);
        Assert.Equal("MED", Assert.Single(pending.NewProducts).Sku);

        var first = await Qb(x).ExportAsync(new QuickBooksExportRequest(), default);
        Assert.Equal((2, 0, true, 1, 1), (first.InvoiceCount, first.AlreadyExportedCount, first.IncludedNew, first.NewClientCount, first.NewProductCount));
        Assert.EndsWith(".iif", first.FileName);
        var iif = Text(first);
        Assert.Single(Rows(iif, "INVITEM"));
        Assert.Single(Rows(iif, "CUST"));
        Assert.Equal(2, Rows(iif, "TRNS").Length);
        Assert.Contains(Rows(iif, "TRNS"), r => r.Split('\t')[7] == "FAC-1" && r.Split('\t')[6] == "22.30");
        // exportar no marca: siguen por pasar, con la fecha del archivo
        var after = await Qb(x).GetPendingAsync(default);
        Assert.Equal((2, 2), (after.Count, after.AlreadyExportedCount));
        Assert.Equal(first.ExportedAtUtc, after.LastExportedAtUtc);
        Assert.Null((await RowAsync(x, inv1.PublicId)).QuickBooksAt);

        // entra otra factura: el archivo nuevo trae las de antes y la nueva, y avisa cuántas ya habían salido
        await SimpleAsync(x, "FAC-2", c, p);
        var second = await Qb(x).ExportAsync(new QuickBooksExportRequest(IncludeNew: false), default);
        Assert.Equal((3, 2, false), (second.InvoiceCount, second.AlreadyExportedCount, second.IncludedNew));
        Assert.Equal(first.ExportedAtUtc, second.LastExportedAtUtc);
        var iif2 = Text(second);
        Assert.Empty(Rows(iif2, "INVITEM"));   // sin los nuevos: solo las facturas
        Assert.Empty(Rows(iif2, "CUST"));
        Assert.Equal(3, Rows(iif2, "TRNS").Length);
        Assert.All(second.Invoices, i => Assert.Equal(second.ExportedAtUtc, i.ExportedAtUtc));
        Assert.Equal(paid.PublicId, second.Invoices.Single(i => i.Number == "FAC-PAID").PublicId);
    }

    [Fact]
    public async Task Confirming_marks_the_imported_ones_and_their_clients_and_products_stop_being_new()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var inv1 = await SimpleAsync(x, "FAC-1", c, p);
        var inv2 = await SimpleAsync(x, "FAC-2", c, p);
        await Qb(x).ExportAsync(null, default);
        var inv3 = await SimpleAsync(x, "FAC-3", c, p);   // entró después del archivo

        var never = await Assert.ThrowsAsync<StatusRuleException>(() => Qb(x).ConfirmAsync(new QuickBooksConfirmRequest(new[] { inv1.PublicId, inv3.PublicId }), default));
        Assert.Equal(QuickBooksRules.NotExported("FAC-3"), never.Message);
        Assert.Null((await RowAsync(x, inv1.PublicId)).QuickBooksAt);   // nada a medias

        var ok = await Qb(x).ConfirmAsync(new QuickBooksConfirmRequest(new[] { inv1.PublicId }), default);   // FAC-2 falló en QuickBooks: la desmarcó
        Assert.Equal((1, 0, 1, 1), (ok.Marked, ok.AlreadyMarked, ok.ClientsSent, ok.ProductsSent));
        var row1 = await RowAsync(x, inv1.PublicId);
        Assert.NotNull(row1.QuickBooksAt);
        Assert.Equal(1, row1.QuickBooksBy);
        Assert.NotNull((await x.F.Db.Clients.AsNoTracking().SingleAsync(r => r.ClientId == c.ClientId)).QuickBooksSentAt);
        Assert.NotNull((await x.F.Db.Products.AsNoTracking().SingleAsync(r => r.ProductId == p.ProductId)).QuickBooksSentAt);

        var pending = await Qb(x).GetPendingAsync(default);
        Assert.Equal(new[] { "FAC-2", "FAC-3" }, pending.Invoices.Select(i => i.Number));
        Assert.All(pending.Invoices, i => { Assert.False(i.NewClient); Assert.Empty(i.NewProductSkus); });   // ya no son nuevos
        Assert.Empty(pending.NewClients);
        Assert.Empty(pending.NewProducts);
        var iif = Text(await Qb(x).ExportAsync(null, default));
        Assert.Empty(Rows(iif, "CUST"));
        Assert.Empty(Rows(iif, "INVITEM"));

        // confirmar otra vez la misma no cambia ni quién ni cuándo
        var again = await Qb(x).ConfirmAsync(new QuickBooksConfirmRequest(new[] { inv1.PublicId, inv2.PublicId }), default);
        Assert.Equal((1, 1), (again.Marked, again.AlreadyMarked));
        Assert.Equal(row1.QuickBooksAt, (await RowAsync(x, inv1.PublicId)).QuickBooksAt);

        var history = await Qb(x).GetHistoryAsync(null, null, null, 0, 50, default);
        Assert.Equal(2, history.Total);
        Assert.All(history.Items, h => Assert.Equal(1, h.QuickBooksBy));
        Assert.Equal(22.30m, history.Items[0].Total);
        Assert.Single((await Qb(x).GetHistoryAsync(null, null, "FAC-2", 0, 50, default)).Items);
        Assert.Empty((await Qb(x).GetHistoryAsync(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), null, null, 0, 50, default)).Items);
    }

    [Fact]
    public async Task Marking_by_hand_one_or_all_and_unmarking()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var inv1 = await SimpleAsync(x, "FAC-1", c, p);
        await SimpleAsync(x, "FAC-2", c, p);
        await SimpleAsync(x, "FAC-3", c, p);
        var draft = await SimpleAsync(x, "FAC-DRAFT", c, p, InvoiceStatuses.Draft);

        Assert.Equal(QuickBooksRules.BodyRequired, (await Assert.ThrowsAsync<ValidationException>(() => Qb(x).MarkAsync(null, default))).Message);
        Assert.Equal(QuickBooksRules.SelectionRequired, (await Assert.ThrowsAsync<ValidationException>(() => Qb(x).MarkAsync(new QuickBooksMarkRequest(), default))).Message);
        Assert.Equal(QuickBooksRules.InvoicesNotFound,
            (await Assert.ThrowsAsync<ValidationException>(() => Qb(x).MarkAsync(new QuickBooksMarkRequest(new[] { inv1.PublicId, Guid.NewGuid() }), default))).Message);
        Assert.Equal(QuickBooksRules.InvoicesNotFound,
            (await Assert.ThrowsAsync<ValidationException>(() => Qb(x).MarkAsync(new QuickBooksMarkRequest(new[] { draft.PublicId }), default))).Message);
        Assert.Equal(QuickBooksRules.TooManyIds, (await Assert.ThrowsAsync<ValidationException>(() =>
            Qb(x).MarkAsync(new QuickBooksMarkRequest(Enumerable.Range(0, QuickBooksRules.MaxIdsPerCall + 1).Select(_ => Guid.NewGuid()).ToArray()), default))).Message);

        // a mano no hace falta haberla exportado
        var one = await Qb(x).MarkAsync(new QuickBooksMarkRequest(new[] { inv1.PublicId }), default);
        Assert.Equal((1, 0, 1, 1), (one.Marked, one.AlreadyMarked, one.ClientsSent, one.ProductsSent));
        Assert.Equal(2, (await Qb(x).CountPendingAsync(default)).Count);
        var all = await Qb(x).MarkAsync(new QuickBooksMarkRequest(All: true), default);
        Assert.Equal((2, 0, 0, 0), (all.Marked, all.AlreadyMarked, all.ClientsSent, all.ProductsSent));
        Assert.Equal(0, (await Qb(x).GetPendingAsync(default)).Count);
        Assert.Null((await RowAsync(x, draft.PublicId)).QuickBooksAt);   // «todas» = las por pasar, no el borrador

        // quitar la marca: vuelve a por pasar; el cliente y el producto siguen como ya mandados
        var undo = await Qb(x).UnmarkAsync(new QuickBooksMarkRequest(new[] { inv1.PublicId }), default);
        Assert.Equal(1, undo.Marked);
        var back = Assert.Single((await Qb(x).GetPendingAsync(default)).Invoices);
        Assert.Equal("FAC-1", back.Number);
        Assert.False(back.NewClient);
        Assert.Null((await RowAsync(x, inv1.PublicId)).QuickBooksBy);
    }

    [Fact]
    public async Task A_kit_goes_by_its_components_prorated_and_only_the_components_are_new_products()
    {
        await using var x = await WorldAsync();
        var strips = await x.ProductAsync("52800", 6.99m);
        var monitor = await x.ProductAsync("070120", 7.99m);
        var kit = await x.ProductAsync("OFERTA1", 41.94m, 0m);
        var kitRow = await x.F.Db.Products.AsTracking().SingleAsync(r => r.ProductId == kit.ProductId);
        kitRow.ProductTypeLookupId = x.Lookup(KitCodes.ProductTypeDomain, ProductTypes.Kit);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var c = await x.ClientAsync("C1");
        var shipping = x.Lookup(BillingCodes.ChargeTypeDomain, SalesShippingCodes.ChargeType);

        // la orden se arma con un producto cualquiera; la línea de la factura es el kit y su foto de componentes (6 + 1 por kit)
        var inv = await InvoiceAsync(x, "FAC-K", c, new[] { (strips, 2m, 41.94m) },
            extra: so => new[] { new InvoiceLine { SalesOrderId = so.SalesOrderId, ChargeTypeLookupId = shipping, Description = "Cargo de envío", Quantity = 1m, UnitAmount = 12m } });
        var invLine = await x.F.Db.Set<InvoiceLine>().AsTracking().SingleAsync(l => l.InvoiceId == inv.InvoiceId && l.SalesOrderLineId != null);
        invLine.ProductId = kit.ProductId;
        x.F.Db.Set<SalesOrderLineComponent>().AddRange(
            new SalesOrderLineComponent { TenantId = WmsFixture.TenantId, SalesOrderLineId = invLine.SalesOrderLineId!.Value, ComponentProductId = strips.ProductId, Quantity = 6m },
            new SalesOrderLineComponent { TenantId = WmsFixture.TenantId, SalesOrderLineId = invLine.SalesOrderLineId!.Value, ComponentProductId = monitor.ProductId, Quantity = 1m });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var pending = await Qb(x).GetPendingAsync(default);
        Assert.Equal(new[] { "070120", "52800" }, pending.NewProducts.Select(p => p.Sku));   // el kit no
        Assert.Equal(new[] { "070120", "52800" }, Assert.Single(pending.Invoices).NewProductSkus);

        var iif = Text(await Qb(x).ExportAsync(null, default));
        Assert.Equal(new[] { "070120", "52800" }, Rows(iif, "INVITEM").Select(r => r.Split('\t')[1]).OrderBy(s => s, StringComparer.Ordinal));
        var spl = Rows(iif, "SPL").Select(r => r.Split('\t')).ToList();
        Assert.Equal(new[] { ("52800", "-70.46", "-12"), ("070120", "-13.42", "-2"), ("Shipping", "-12.00", "-1") }, spl.Select(s => (s[12], s[6], s[10])));
        Assert.Equal("95.88", Rows(iif, "TRNS").Single().Split('\t')[6]);

        // al marcar, quedan mandados los componentes, no el kit
        var mark = await Qb(x).MarkAsync(new QuickBooksMarkRequest(All: true), default);
        Assert.Equal(2, mark.ProductsSent);
        Assert.Null((await x.F.Db.Products.AsNoTracking().SingleAsync(r => r.ProductId == kit.ProductId)).QuickBooksSentAt);
    }

    [Fact]
    public async Task A_client_migrated_from_QuickBooks_is_never_new_and_goes_with_its_QuickBooks_name()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var def = new CustomFieldDefinition
        {
            TenantId = WmsFixture.TenantId, EntityTypeLookupId = x.Lookup(Teikem.Domain.Constants.LookupDomains.EntityType, EntityTypes.Client), FieldKey = Teikem.Infrastructure.Migration.LegacyImportPlanner.QbCodeFieldKey,
            LabelJson = "{\"es\":\"Código QuickBooks\"}", DataTypeLookupId = 1, IsActive = true,
        };
        x.F.Db.CustomFieldDefinitions.Add(def);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.CustomFieldValues.Add(new CustomFieldValue { TenantId = WmsFixture.TenantId, CustomFieldDefinitionId = def.CustomFieldDefinitionId, EntityId = c.ClientId, ValueText = " Farmacia Central:Ponce " });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        await SimpleAsync(x, "FAC-1", c, p);

        var row = Assert.Single((await Qb(x).GetPendingAsync(default)).Invoices);
        Assert.False(row.NewClient);
        Assert.Equal("Farmacia Central-Ponce", row.QuickBooksCustomer);   // tal como sale en el archivo
        var iif = Text(await Qb(x).ExportAsync(null, default));
        Assert.Empty(Rows(iif, "CUST"));
        Assert.Equal("Farmacia Central-Ponce", Rows(iif, "TRNS").Single().Split('\t')[5]);
    }

    [Fact]
    public async Task The_settings_accounts_tax_item_and_terms_go_into_the_file()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1", r => r.PaymentTermLookupId = x.Lookup(Teikem.Domain.Constants.LookupDomains.PaymentTerm, "NET30"));
        await SimpleAsync(x, "FAC-1", c, p);

        var defaults = await Qb(x).GetSettingsAsync(default);
        Assert.Equal((QuickBooksRules.DefaultSalesAccount, QuickBooksRules.DefaultTaxItem, QuickBooksRules.DefaultShippingItem), (defaults.SalesAccount, defaults.TaxItem, defaults.ShippingItem));
        Assert.All(defaults.Terms, t => Assert.Null(t.QuickBooksName));
        Assert.Contains(QuickBooksRules.UnmappedTerm("NET30"), (await Qb(x).GetPendingAsync(default)).Warnings);

        Assert.Equal(QuickBooksRules.UnknownTerm("XX"), (await Assert.ThrowsAsync<ValidationException>(() =>
            Qb(x).UpdateSettingsAsync(new QuickBooksSettingsRequest(Terms: new Dictionary<string, string?> { ["XX"] = "Algo" }), default))).Message);
        Assert.Equal(QuickBooksRules.AccountTooLong, (await Assert.ThrowsAsync<ValidationException>(() =>
            Qb(x).UpdateSettingsAsync(new QuickBooksSettingsRequest(SalesAccount: new string('a', 160)), default))).Message);
        Assert.Equal(QuickBooksRules.ItemTooLong, (await Assert.ThrowsAsync<ValidationException>(() =>
            Qb(x).UpdateSettingsAsync(new QuickBooksSettingsRequest(TaxItem: new string('a', 32)), default))).Message);
        Assert.Equal(QuickBooksRules.TermTooLong, (await Assert.ThrowsAsync<ValidationException>(() =>
            Qb(x).UpdateSettingsAsync(new QuickBooksSettingsRequest(Terms: new Dictionary<string, string?> { ["NET30"] = new string('a', 32) }), default))).Message);
        Assert.Equal(QuickBooksRules.BodyRequired, (await Assert.ThrowsAsync<ValidationException>(() => Qb(x).UpdateSettingsAsync(null, default))).Message);

        var saved = await Qb(x).UpdateSettingsAsync(new QuickBooksSettingsRequest(SalesAccount: " Sales:Gloves ", TaxItem: "IVU 11.5", TaxAccount: "IVU por pagar",
            Terms: new Dictionary<string, string?> { ["net30"] = "Net 30", ["COD"] = "C.O.D." }), default);
        Assert.Equal(("Sales:Gloves", "IVU 11.5", "IVU por pagar"), (saved.SalesAccount, saved.TaxItem, saved.TaxAccount));
        Assert.Equal("Net 30", saved.Terms.Single(t => t.Code == "NET30").QuickBooksName);
        Assert.Empty((await Qb(x).GetPendingAsync(default)).Warnings);

        var iif = Text(await Qb(x).ExportAsync(null, default));
        var trns = Rows(iif, "TRNS").Single().Split('\t');
        Assert.Equal(("Net 30", "12/02/2026"), (trns[13], trns[12]));
        var spl = Rows(iif, "SPL").Select(r => r.Split('\t')).ToList();
        Assert.Equal(("Sales:Gloves", "IVU por pagar", "IVU 11.5", "AUTOSTAX"), (spl[0][4], spl[1][4], spl[1][12], spl[1][14]));
        Assert.Equal("Net 30", Rows(iif, "CUST").Single().Split('\t')[17]);
        Assert.Equal("Sales:Gloves", Rows(iif, "INVITEM").Single().Split('\t')[5]);

        // "" = volver al valor por omisión; un término con "" sale del mapa; lo que no viene no cambia
        var reset = await Qb(x).UpdateSettingsAsync(new QuickBooksSettingsRequest(SalesAccount: "", Terms: new Dictionary<string, string?> { ["COD"] = "" }), default);
        Assert.Equal((QuickBooksRules.DefaultSalesAccount, "IVU 11.5"), (reset.SalesAccount, reset.TaxItem));
        Assert.Null(reset.Terms.Single(t => t.Code == "COD").QuickBooksName);
        Assert.Equal("Net 30", reset.Terms.Single(t => t.Code == "NET30").QuickBooksName);
        Assert.Null((await x.F.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId)).QuickBooksSalesAccount);
    }

    [Fact]
    public async Task Who_can_see_mark_and_export()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var inv = await SimpleAsync(x, "FAC-1", c, p);
        var ids = new QuickBooksMarkRequest(new[] { inv.PublicId });

        x.F.SetPermissions(PermissionCatalog.SalesQuickBooksView);   // solo ver
        Assert.Equal(1, (await Qb(x).GetPendingAsync(default)).Count);
        await Qb(x).GetSettingsAsync(default);
        await Assert.ThrowsAsync<ForbiddenException>(() => Qb(x).ExportAsync(null, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => Qb(x).MarkAsync(ids, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => Qb(x).ConfirmAsync(new QuickBooksConfirmRequest(ids.InvoiceIds), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => Qb(x).UpdateSettingsAsync(new QuickBooksSettingsRequest(), default));

        x.F.SetPermissions(PermissionCatalog.SalesQuickBooksMark);   // marcar a mano no necesita exportar
        await Assert.ThrowsAsync<ForbiddenException>(() => Qb(x).ExportAsync(null, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => Qb(x).ConfirmAsync(new QuickBooksConfirmRequest(ids.InvoiceIds), default));
        Assert.Equal(1, (await Qb(x).GetPendingAsync(default)).Count);   // la lista sí la ve: marca mirándola
        Assert.Equal(1, (await Qb(x).MarkAsync(ids, default)).Marked);
        Assert.Equal(1, (await Qb(x).UnmarkAsync(ids, default)).Marked);

        x.F.SetPermissions(PermissionCatalog.SalesQuickBooksExport);   // exportar y confirmar
        await Assert.ThrowsAsync<ForbiddenException>(() => Qb(x).MarkAsync(ids, default));
        await Qb(x).ExportAsync(null, default);
        Assert.Equal(1, (await Qb(x).ConfirmAsync(new QuickBooksConfirmRequest(ids.InvoiceIds), default)).Marked);

        x.F.SetPermissions(PermissionCatalog.SalesView);   // ver órdenes de venta no basta
        await Assert.ThrowsAsync<ForbiddenException>(() => Qb(x).GetPendingAsync(default));
        await Assert.ThrowsAsync<ForbiddenException>(() => Qb(x).GetSettingsAsync(default));

        // las plantillas: la Oficina de ventas y el administrador traen los tres; quien marca o exporta entra a la lista
        foreach (var code in new[] { PermissionCatalog.SalesQuickBooksView, PermissionCatalog.SalesQuickBooksMark, PermissionCatalog.SalesQuickBooksExport })
        {
            Assert.Contains(code, PermissionCatalog.RoleTemplates["SalesOffice"]);
            Assert.Contains(code, PermissionCatalog.RoleTemplates["TenantAdmin"]);
            Assert.DoesNotContain(code, PermissionCatalog.RoleTemplates["Sales"]);
            Assert.Equal("BILLING", Assert.Single(PermissionCatalog.All, p => p.Code == code).Category);
            Assert.Contains(code, PermissionDisplayOrder.Codes);
        }
        var implied = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionCatalog.SalesQuickBooksExport };
        PermissionCatalog.ExpandImplied(implied);
        Assert.Contains(PermissionCatalog.SalesView, implied);   // la pestaña vive en Órdenes de venta
        Assert.Equal(QuickBooksService.ListPermissions.OrderBy(c => c), new[] { PermissionCatalog.SalesQuickBooksView, PermissionCatalog.SalesQuickBooksMark, PermissionCatalog.SalesQuickBooksExport }.OrderBy(c => c));
    }

    [Fact]
    public void The_update_script_adds_the_columns_and_gives_the_permissions_to_the_cloned_roles_once()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Diseño"))) dir = dir.Parent;
        var sql = File.ReadAllText(Path.Combine(dir!.FullName, "Diseño", "logistica-db-update.sql"));
        var section = sql[sql.IndexOf("Lote 225 (P5 del pedido de vendedores)", StringComparison.Ordinal)..];
        foreach (var (table, column) in new[] { ("Invoice", "QuickBooksAt"), ("Invoice", "QuickBooksBy"), ("Invoice", "QuickBooksExportedAt"), ("Client", "QuickBooksSentAt"),
                     ("Product", "QuickBooksSentAt"), ("Tenant", "QuickBooksSalesAccount"), ("Tenant", "QuickBooksTaxItem"), ("Tenant", "QuickBooksTermsJson") })
            Assert.Contains($"COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL", section);
        foreach (var code in new[] { PermissionCatalog.SalesQuickBooksView, PermissionCatalog.SalesQuickBooksMark, PermissionCatalog.SalesQuickBooksExport })
            Assert.Contains($"WHERE Code = '{code}'", section);
        Assert.Contains("L225_SALES_QUICKBOOKS", section);
        Assert.Contains("r.Name IN ('TenantAdmin', 'SalesOffice')", section);
        Assert.DoesNotContain("DELETE FROM dbo.RolePermission", section);   // solo agrega
    }

    [Fact]
    public async Task Needs_your_attention_the_sales_panel_and_the_morning_notice()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var provider = x.F.Get<SalesQuickBooksAttentionProvider>();
        Assert.Equal(PermissionCatalog.SalesQuickBooksView, provider.RequiredPermission);
        Assert.Equal(0, (await provider.ReadAsync(Scope, 5, default)).Total);
        Assert.Equal(0, await Qb(x).NotifyPendingAsync(default));   // nada pendiente: no hay aviso

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await InvoiceAsync(x, "FAC-OLD", c, new[] { (p, 1m, 10m) }, issue: today.AddDays(-10));
        await InvoiceAsync(x, "FAC-1", c, new[] { (p, 2m, 10m) }, issue: today.AddDays(-1));
        await Qb(x).ExportAsync(null, default);
        await InvoiceAsync(x, "FAC-NEW", c, new[] { (p, 1m, 10m) }, issue: today);

        var attention = await provider.ReadAsync(Scope, 5, default);
        var item = Assert.Single(attention.Items);
        Assert.Equal((3, "danger", "3", "2"), (item.Count, item.Tone, item.Params["count"], item.Params["exported"]));
        Assert.Equal(today.AddDays(-10).ToString("yyyy-MM-dd"), item.Params["oldest"]);
        Assert.Equal("quickbooks", attention.Group!.Query!["tab"]);

        // el panel Ventas: con sales.quickbooks.view trae la cifra y su lista; sin él, null
        var pulse = new SalesPulseService(x.F.Db, x.F.Get<ITenantClock>(), x.F.Get<PermissionService>(), Qb(x));
        var dto = await pulse.GetAsync(default);
        Assert.Equal(3, dto.InvoicesToQuickBooks);
        Assert.Equal("/sales-orders?tab=quickbooks", dto.Routes!["invoicesToQuickBooks"]);
        x.F.SetPermissions(PermissionCatalog.PulseSales, PermissionCatalog.SalesView);
        Assert.Null((await pulse.GetAsync(default)).InvoicesToQuickBooks);
        x.F.SetPermissions(PermissionCatalog.PulseSales, PermissionCatalog.SalesView, PermissionCatalog.SalesQuickBooksView, PermissionCatalog.SalesQuickBooksMark);

        // el aviso de la mañana: uno al día, con la cifra
        var job = new SalesQuickBooksPendingJob();
        Assert.Equal((JobKeys.SalesQuickBooksPending, new TimeSpan(7, 0, 0)), (job.Key, job.DefaultDailyAt));
        var ctx = new JobRunContext(WmsFixture.TenantId, "test", x.F.Services, DateTime.UtcNow);
        Assert.Equal("Aviso del día: 3 factura(s) por pasar a QuickBooks.", await job.RunAsync(ctx, default));
        var outbox = Assert.Single(await x.F.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == SalesEventCodes.QuickBooksPending).ToListAsync());
        var values = NotificationRules.ParseValues(outbox.PayloadJson);
        Assert.Equal(("3", "2"), (values["count"], values["exported"]));
        Assert.Equal("El aviso de hoy ya había salido.", await job.RunAsync(ctx, default));
        Assert.Single(await x.F.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == SalesEventCodes.QuickBooksPending).ToListAsync());
        Assert.NotNull(x.F.Get<NotificationEventCatalog>().Find(SalesEventCodes.QuickBooksPending));

        await Qb(x).MarkAsync(new QuickBooksMarkRequest(All: true), default);
        Assert.Equal("No hay facturas por pasar a QuickBooks.", await job.RunAsync(ctx, default));
        Assert.Equal(0, (await provider.ReadAsync(Scope, 5, default)).Total);
    }
}
