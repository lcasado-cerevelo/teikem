using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Export;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 77 (P5): plantillas de exportación a contabilidad — el motor puro (formato, delimitador, fechas, comillas), el catálogo de plantillas por compañía y el archivo de una corrida de facturación.
public sealed class ExportTemplateTests
{
    private static readonly ExportColumn[] Two = { new("INVOICE_NUMBER", "No."), new("INVOICE_TOTAL") };

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(params (string Number, decimal Total, DateOnly? Date)[] r)
        => r.Select(x => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["INVOICE_NUMBER"] = x.Number, ["INVOICE_TOTAL"] = x.Total, ["ISSUE_DATE"] = x.Date }).ToList();

    [Fact]
    public void The_engine_renders_header_columns_in_order_money_with_two_decimals_and_crlf()
    {
        var file = ExportEngine.Render(ExportCodes.ScopeBilling, "CSV", ",", ExportCodes.DateIso, true, Two, Rows(("FAC-00001", 12.3m, null), ("FAC-00002", 0.005m, null)), "x");
        Assert.Equal("No.,InvoiceTotal\r\nFAC-00001,12.30\r\nFAC-00002,0.01\r\n", file.Text);
        Assert.Equal(("x.csv", 2), (file.FileName, file.RowCount));
        Assert.StartsWith("text/csv", file.ContentType);
    }

    [Fact]
    public void Without_header_other_delimiters_and_txt_files()
    {
        var csv = ExportEngine.Render(ExportCodes.ScopeBilling, "CSV", ";", ExportCodes.DateIso, false, Two, Rows(("A", 1m, null)), "x");
        Assert.Equal("A;1.00\r\n", csv.Text);
        var tab = ExportEngine.Render(ExportCodes.ScopeBilling, "TXT", "\t", ExportCodes.DateIso, true, Two, Rows(("A", 1m, null)), "x");
        Assert.Equal("No.\tInvoiceTotal\r\nA\t1.00\r\n", tab.Text);
        Assert.Equal(("x.txt", "text/plain; charset=utf-8"), (tab.FileName, tab.ContentType));
    }

    [Fact]
    public void Csv_quotes_what_carries_the_delimiter_quotes_or_line_breaks_and_txt_replaces_them_with_a_space()
    {
        var rows = new[] { (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["INVOICE_NUMBER"] = "A,\"B\"\nC", ["INVOICE_TOTAL"] = 1m } };
        Assert.Equal("\"A,\"\"B\"\"\nC\",1.00\r\n", ExportEngine.Render(ExportCodes.ScopeBilling, "CSV", ",", ExportCodes.DateIso, false, Two, rows, "x").Text);
        Assert.Equal("A,\"B\" C|1.00\r\n", ExportEngine.Render(ExportCodes.ScopeBilling, "TXT", "|", ExportCodes.DateIso, false, Two, rows, "x").Text);
    }

    [Fact]
    public void Dates_use_the_templates_format_and_numbers_drop_extra_zeros()
    {
        var d = new DateOnly(2026, 3, 9);
        Assert.Equal(("2026-03-09", "03/09/2026", "09/03/2026"), (ExportEngine.FormatDate(d, ExportCodes.DateIso), ExportEngine.FormatDate(d, ExportCodes.DateUs), ExportEngine.FormatDate(d, ExportCodes.DateEu)));
        var qty = ExportFields.Billing.Single(f => f.Code == "QUANTITY");
        var date = ExportFields.Billing.Single(f => f.Code == "ISSUE_DATE");
        Assert.Equal(("4", "2.5", "0.3333"), (ExportEngine.FormatValue(qty, 4m, ""), ExportEngine.FormatValue(qty, 2.50m, ""), ExportEngine.FormatValue(qty, 0.33333m, "")));
        Assert.Equal(("", "09/03/2026"), (ExportEngine.FormatValue(date, null, ExportCodes.DateEu), ExportEngine.FormatValue(date, d, ExportCodes.DateEu)));
    }

    [Fact]
    public void Same_input_gives_the_same_file_and_unknown_columns_are_left_out()
    {
        var rows = Rows(("A", 1m, new DateOnly(2026, 1, 2)));
        var cols = new[] { new ExportColumn("INVOICE_NUMBER"), new ExportColumn("NO_EXISTE"), new ExportColumn("ISSUE_DATE") };
        var a = ExportEngine.Render(ExportCodes.ScopeBilling, "CSV", ",", ExportCodes.DateUs, true, cols, rows, "x");
        Assert.Equal(a.Text, ExportEngine.Render(ExportCodes.ScopeBilling, "CSV", ",", ExportCodes.DateUs, true, cols, rows, "x").Text);
        Assert.Equal("InvoiceNo,InvoiceDate\r\nA,01/02/2026\r\n", a.Text);
    }

    [Fact]
    public void Names_compare_without_case_accents_or_extra_spaces_and_columns_round_trip_through_json()
    {
        Assert.Equal(ExportRules.NormalizeName("  Formato   Estándar "), ExportRules.NormalizeName("FORMATO ESTANDAR"));
        Assert.NotEqual(ExportRules.NormalizeName("A"), ExportRules.NormalizeName("B"));
        var json = ExportRules.SerializeColumns(new[] { new ExportColumn("A", "Encabezado"), new ExportColumn("B") });
        Assert.Equal(new[] { ("A", (string?)"Encabezado"), ("B", null) }, ExportRules.ParseColumns(json).Select(c => (c.Field, c.Header)).ToArray());
        Assert.Empty(ExportRules.ParseColumns("{no es json"));
        Assert.Equal(ExportFields.Billing.Count, ExportRules.DefaultColumns(ExportCodes.ScopeBilling).Count);
        Assert.Equal(ExportFields.Billing.Count, ExportFields.Billing.Select(f => f.Code).Distinct().Count());
    }

    // ------------------------------------------------------------------ servicio

    private sealed class World(WmsFixture f, ExportTemplateService templates, BillingService billing, BillingExportService export)
    {
        public WmsFixture F { get; } = f;
        public ExportTemplateService Templates { get; } = templates;
        public BillingService Billing { get; } = billing;
        public BillingExportService Export { get; } = export;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<RateService>();
            s.AddSingleton<OrderQuoteService>();
            s.AddSingleton<BillingService>();
            s.AddSingleton<ExportTemplateService>();
            s.AddSingleton<BillingExportService>();
        });
        f.SetModules(ModuleKeys.Billing);
        return new World(f, f.Get<ExportTemplateService>(), f.Get<BillingService>(), f.Get<BillingExportService>());
    }

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task Each_company_starts_with_the_standard_template_with_every_column()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var list = await x.Templates.ListAsync(null, default);
        var t = Assert.Single(list);
        Assert.Equal((ExportCodes.DefaultTemplateName, "CSV", ",", "YYYY-MM-DD", true), (t.Name, t.Format, t.Delimiter, t.DateFormat, t.IncludeHeader));
        Assert.Equal(ExportFields.Billing.Count, t.Columns.Count);
        Assert.Single(await x.Templates.ListAsync("billing", default));   // no se duplica
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Equal(ExportCodes.DefaultTemplateName, Assert.Single(await x.Templates.ListAsync(null, default)).Name);   // cada compañía la suya
        Assert.Equal(t.PublicId, Assert.Single(await x.Templates.ListAsync(null, default)).PublicId);
        Assert.Equal(ExportRules.ScopeInvalid, await MsgOf(() => x.Templates.ListAsync("COMPRAS", default)));
        Assert.Equal("Cliente", x.Templates.Fields(null).Single(f => f.Code == "CLIENT_NAME").Label);
    }

    [Fact]
    public async Task Create_update_and_deactivate_with_their_validations()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await x.Templates.ListAsync(null, default);
        var cols = new[] { new ExportColumnDto("invoice_number", "Factura"), new ExportColumnDto("INVOICE_TOTAL") };
        var made = await x.Templates.CreateAsync(new ExportTemplateRequest(Name: " QuickBooks ", Format: "txt", Delimiter: "tab", DateFormat: "mm/dd/yyyy", IncludeHeader: false, Columns: cols), default);
        Assert.Equal(("QuickBooks", "TXT", "\t", "MM/DD/YYYY", false), (made.Name, made.Format, made.Delimiter, made.DateFormat, made.IncludeHeader));
        Assert.Equal(new[] { "INVOICE_NUMBER", "INVOICE_TOTAL" }, made.Columns.Select(c => c.Field).ToArray());

        Assert.Equal(ExportRules.NameTaken, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: "quickbooks"), default)));
        Assert.Equal(ExportRules.NameTaken, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: "formato estandar"), default)));
        Assert.Equal(ExportRules.NameRequired, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: " "), default)));
        Assert.Equal(ExportRules.NameTooLong, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: new string('x', 81)), default)));
        Assert.Equal(ExportRules.FormatInvalid, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: "n", Format: "XML"), default)));
        Assert.Equal(ExportRules.DelimiterInvalid, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: "n", Delimiter: "#"), default)));
        Assert.Equal(ExportRules.DateFormatInvalid, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: "n", DateFormat: "AAAA"), default)));
        Assert.Equal(ExportRules.ColumnsRequired, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: "n", Columns: Array.Empty<ExportColumnDto>()), default)));
        Assert.Equal(ExportRules.FieldInvalid, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: "n", Columns: new[] { new ExportColumnDto("NADA") }), default)));
        Assert.Equal(ExportRules.FieldRepeated, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: "n", Columns: new[] { new ExportColumnDto("SKU"), new ExportColumnDto("sku") }), default)));
        Assert.Equal(ExportRules.HeaderTooLong, await MsgOf(() => x.Templates.CreateAsync(new ExportTemplateRequest(Name: "n", Columns: new[] { new ExportColumnDto("SKU", new string('h', 61)) }), default)));

        // cambiar: lo que no se manda queda como estaba; renombrar a uno ajeno choca, a sí mismo no
        var changed = await x.Templates.UpdateAsync(made.PublicId, new ExportTemplateRequest(Delimiter: ";", Name: "QUICKBOOKS"), default);
        Assert.Equal(("QUICKBOOKS", ";", "TXT", 2), (changed.Name, changed.Delimiter, changed.Format, changed.Columns.Count));
        Assert.Equal(ExportRules.NameTaken, await MsgOf(() => x.Templates.UpdateAsync(made.PublicId, new ExportTemplateRequest(Name: "Formato estándar"), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Templates.UpdateAsync(Guid.NewGuid(), new ExportTemplateRequest(Name: "z"), default));

        // desactivar: se puede mientras quede otra; el nombre queda libre
        await x.Templates.DeactivateAsync(made.PublicId, default);
        Assert.Single(await x.Templates.ListAsync(null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Templates.GetAsync(made.PublicId, default));
        var last = (await x.Templates.ListAsync(null, default))[0];
        Assert.Equal(ExportRules.LastTemplate, await MsgOf(() => x.Templates.DeactivateAsync(last.PublicId, default)));
        await x.Templates.CreateAsync(new ExportTemplateRequest(Name: "QuickBooks"), default);
    }

    [Fact]
    public async Task Templates_are_isolated_by_company()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var mine = (await x.Templates.ListAsync(null, default))[0];
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => x.Templates.GetAsync(mine.PublicId, default));
            await Assert.ThrowsAsync<NotFoundException>(() => x.Templates.UpdateAsync(mine.PublicId, new ExportTemplateRequest(Name: "x"), default));
        }
    }

    // ------------------------------------------------------------------ archivo de una corrida

    private static async Task<(BillingRunDto Run, int Id)> ApprovedRunAsync(World x, params (string Number, decimal Amount)[] orders)
    {
        var client = await x.F.AddClientAsync("C1");
        foreach (var (number, amount) in orders)
        {
            var o = await x.F.AddOrderAsync(client.ClientId, number, OrderStatuses.Delivered);
            var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
            (t.QuotedAmount, t.QuotedAtUtc) = (amount, DateTime.UtcNow.AddDays(-1));
            await x.F.Db.SaveChangesAsync();
            x.F.Db.ChangeTracker.Clear();
        }
        var run = await x.Billing.GenerateAsync(new BillingRunRequest(client.PublicId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))), default);
        return (run, run.Id);
    }

    [Fact]
    public async Task A_run_must_be_approved_to_export_and_exporting_marks_it_once_but_can_be_repeated()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var (_, id) = await ApprovedRunAsync(x, ("T-1", 25m));
        Assert.Equal(ExportRules.ExportNeedsApproved, await MsgOf(() => x.Export.PreviewAsync(id, null, default)));   // generada
        await x.Billing.ReviewAsync(id, default);
        Assert.Equal(ExportRules.ExportNeedsApproved, await MsgOf(() => x.Export.ExportAsync(id, null, default)));   // revisada
        await x.Billing.ApproveAsync(id, default);

        var preview = await x.Export.PreviewAsync(id, null, default);
        Assert.Equal("APPROVED", (await x.Billing.GetRunAsync(id, default)).Status);   // la vista previa no cambia nada
        var file = await x.Export.ExportAsync(id, null, default);
        Assert.Equal(preview.Text, file.Text);   // reproducible
        var after = await x.Billing.GetRunAsync(id, default);
        Assert.Equal("EXPORTED", after.Status);
        Assert.NotNull(after.ExportedAtUtc);
        var again = await x.Export.ExportAsync(id, null, default);   // una exportada se puede volver a exportar
        Assert.Equal(file.Text, again.Text);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Export.PreviewAsync(9999, null, default));
    }

    [Fact]
    public async Task The_file_has_one_row_per_invoice_line_with_the_templates_format()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var (_, id) = await ApprovedRunAsync(x, ("T-1", 25m), ("T-2", 10m));
        await x.Billing.ReviewAsync(id, default);
        await x.Billing.ApproveAsync(id, default);

        var std = await x.Export.PreviewAsync(id, null, default);
        var lines = std.Text.TrimEnd().Split("\r\n");
        Assert.Equal(("facturacion-corrida-" + id + ".csv", 2, ExportCodes.DefaultTemplateName), (std.FileName, std.RowCount, std.TemplateName));
        Assert.Equal(3, lines.Length);   // encabezado + 2 líneas
        Assert.StartsWith("InvoiceNo,InvoiceDate,DueDate,CustomerCode,Customer,", lines[0]);
        Assert.Contains("FAC-00001", lines[1]);
        Assert.Contains("FREIGHT", lines[1]);
        Assert.Contains("T-1", lines[1]);
        Assert.Contains("25.00", lines[1]);

        var custom = await x.Templates.CreateAsync(new ExportTemplateRequest(Name: "Contable", Format: "TXT", Delimiter: "|", DateFormat: "DD/MM/YYYY", IncludeHeader: false,
            Columns: new[] { new ExportColumnDto("INVOICE_NUMBER"), new ExportColumnDto("ISSUE_DATE"), new ExportColumnDto("LINE_TOTAL"), new ExportColumnDto("ORDER_NUMBER") }), default);
        var file = await x.Export.PreviewAsync(id, custom.PublicId, default);
        var rows = file.Text.TrimEnd().Split("\r\n");
        Assert.Equal(2, rows.Length);
        var today = TenantClock.Default.Today.ToString("dd/MM/yyyy");
        Assert.Equal($"FAC-00001|{today}|25.00|T-1", rows[0]);
        Assert.EndsWith(".txt", file.FileName);
    }

    [Fact]
    public async Task Voided_invoices_are_left_out_and_a_foreign_template_is_rejected()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var (_, id) = await ApprovedRunAsync(x, ("T-1", 25m));
        await x.Billing.ReviewAsync(id, default);
        var approved = await x.Billing.ApproveAsync(id, default);
        await x.Billing.VoidAsync(approved.Invoices[0].PublicId, "Error", default);
        Assert.Equal(0, (await x.Export.PreviewAsync(id, null, default)).RowCount);

        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var other = (await x.Templates.ListAsync(null, default))[0];
            using (x.F.AsTenant(WmsFixture.TenantId))
                await Assert.ThrowsAsync<NotFoundException>(() => x.Export.PreviewAsync(id, other.PublicId, default));
        }
    }
}
