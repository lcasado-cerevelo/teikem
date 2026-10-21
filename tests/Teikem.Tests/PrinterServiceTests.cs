using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Printing;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 43 (F-G1) — pruebas de SERVICIO de impresoras y plantillas de label sobre InMemory (con el filtro global de tenant real):
/// nombre repetido entre las activas = 409 y se reusa tras desactivar; la primera de su uso nace predeterminada; hacer predeterminada
/// quita la anterior; desactivar la predeterminada la deja sin predeterminada (D-FG6) y reactivar no la vuelve; una impresora de otra
/// compañía es 404; el uso no se cambia; las opciones de estación traen solo lo activo y el label integrado.
/// InMemory no aplica los índices únicos (la segunda barrera es de SQL Server: la cubre el smoke).
/// </summary>
public sealed class PrinterServiceTests : IDisposable
{
    private const int TenantA = 1;
    private const int TenantB = 2;
    private readonly TenantContext _tenant = new() { TenantId = TenantA, UserId = 1, IsAuthenticated = true };
    private readonly TeikemDbContext _db;

    public PrinterServiceTests()
    {
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("printers-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new TeikemDbContext(options, _tenant);
    }

    public void Dispose() => _db.Dispose();

    private PrinterService Printers => new(_db, _tenant);
    private LabelTemplateService Templates => new(_db, _tenant);

    private static PrinterRequest Label(string name, string? media = null) => new(Name: name, Purpose: "LABEL", MediaSize: media);

    [Fact]
    public async Task First_of_its_purpose_is_default_and_make_default_moves_it()
    {
        var zd = await Printers.CreateAsync(Label("Zebra ZD420 — Mostrador 1"), default);
        var zt = await Printers.CreateAsync(Label("Zebra ZT230 — Almacén", "4x4"), default);
        var doc = await Printers.CreateAsync(new PrinterRequest(Name: "HP LaserJet", Purpose: "document"), default);
        Assert.True(zd.IsDefault);
        Assert.False(zt.IsDefault);
        Assert.True(doc.IsDefault);   // la primera de documentos también
        Assert.Equal("4X4", zt.MediaSize);
        Assert.Equal("LETTER", doc.MediaSize);
        Assert.Equal("BROWSER", zd.ConnectionType);
        _db.ChangeTracker.Clear();

        var now = await Printers.MakeDefaultAsync(zt.PublicId, default);
        Assert.True(now.IsDefault);
        _db.ChangeTracker.Clear();
        var list = await Printers.ListAsync(false, default);
        Assert.Equal(new[] { zt.PublicId }, list.Where(p => p.Purpose == "LABEL" && p.IsDefault).Select(p => p.PublicId));
        Assert.True(list.Single(p => p.PublicId == doc.PublicId).IsDefault);   // la de documentos no se toca
    }

    [Fact]
    public async Task Duplicate_active_name_is_409_and_the_name_is_reusable_after_deactivation()
    {
        var first = await Printers.CreateAsync(Label("Zebra"), default);
        var dup = await Assert.ThrowsAsync<ConflictException>(() => Printers.CreateAsync(Label("  Zebra "), default));
        Assert.Equal("Ya existe una impresora activa con ese nombre.", dup.Message);
        _db.ChangeTracker.Clear();

        await Printers.SetActiveAsync(first.PublicId, false, default);
        _db.ChangeTracker.Clear();
        var again = await Printers.CreateAsync(Label("Zebra"), default);
        Assert.True(again.IsActive);
        _db.ChangeTracker.Clear();

        // reactivar la vieja con el nombre ya usado por otra activa: 409
        var react = await Assert.ThrowsAsync<ConflictException>(() => Printers.SetActiveAsync(first.PublicId, true, default));
        Assert.Equal(PrintingRules.DuplicatePrinterMessage, react.Message);
        _db.ChangeTracker.Clear();

        // renombrar a un nombre activo: 409
        var other = await Printers.CreateAsync(Label("Otra"), default);
        _db.ChangeTracker.Clear();
        var rename = await Assert.ThrowsAsync<ConflictException>(() => Printers.UpdateAsync(other.PublicId, new PrinterRequest(Name: "Zebra"), default));
        Assert.Equal(PrintingRules.DuplicatePrinterMessage, rename.Message);
    }

    [Fact]
    public async Task Deactivating_the_default_clears_it_and_reactivating_does_not_restore_it()
    {
        var zd = await Printers.CreateAsync(Label("ZD"), default);
        var zt = await Printers.CreateAsync(Label("ZT"), default);
        _db.ChangeTracker.Clear();
        var off = await Printers.SetActiveAsync(zd.PublicId, false, default);
        Assert.False(off.IsActive);
        Assert.False(off.IsDefault);
        _db.ChangeTracker.Clear();
        Assert.DoesNotContain(await Printers.ListAsync(true, default), p => p.IsDefault && p.Purpose == "LABEL");   // sin predeterminada: aplica la integrada

        var inactive = await Assert.ThrowsAsync<ConflictException>(() => Printers.MakeDefaultAsync(zd.PublicId, default));
        Assert.Equal("La impresora está inactiva; reactívela antes de hacerla predeterminada.", inactive.Message);
        _db.ChangeTracker.Clear();

        var on = await Printers.SetActiveAsync(zd.PublicId, true, default);
        Assert.True(on.IsActive);
        Assert.False(on.IsDefault);
        _db.ChangeTracker.Clear();
        // una nueva del uso, sin predeterminada activa, nace predeterminada
        var nw = await Printers.CreateAsync(Label("Nueva"), default);
        Assert.True(nw.IsDefault);
        Assert.False((await Printers.GetAsync(zt.PublicId, default)).IsDefault);
    }

    [Fact]
    public async Task Purpose_is_immutable_media_follows_it_and_edits_patch_only_what_comes()
    {
        var zd = await Printers.CreateAsync(new PrinterRequest(Name: "ZD", Purpose: "LABEL", Model: "ZD420", Notes: "Mostrador"), default);
        _db.ChangeTracker.Clear();
        var purpose = await Assert.ThrowsAsync<ValidationException>(() => Printers.UpdateAsync(zd.PublicId, new PrinterRequest(Purpose: "DOCUMENT"), default));
        Assert.Equal(PrintingRules.PurposeImmutableMessage, purpose.Message);
        _db.ChangeTracker.Clear();
        var media = await Assert.ThrowsAsync<ValidationException>(() => Printers.UpdateAsync(zd.PublicId, new PrinterRequest(MediaSize: "LETTER"), default));
        Assert.Equal(PrintingRules.LabelMediaMessage, media.Message);
        _db.ChangeTracker.Clear();
        var conn = await Assert.ThrowsAsync<ValidationException>(() => Printers.UpdateAsync(zd.PublicId, new PrinterRequest(ConnectionType: "NETWORK"), default));
        Assert.Equal(PrintingRules.UnknownConnectionMessage("NETWORK"), conn.Message);
        _db.ChangeTracker.Clear();

        var edited = await Printers.UpdateAsync(zd.PublicId, new PrinterRequest(Purpose: "label", MediaSize: "4x2", RotateMedia: true, Notes: "",
            ConnectionType: "QZ_TRAY", SystemName: "  ZDesigner ZD420-203dpi ZPL "), default);
        Assert.Equal("QZ_TRAY", edited.ConnectionType);
        Assert.Equal("ZDesigner ZD420-203dpi ZPL", edited.SystemName);
        Assert.Equal("4X2", edited.MediaSize);
        Assert.True(edited.RotateMedia);
        Assert.Null(edited.Notes);
        Assert.Equal("ZD420", edited.Model);   // null = sin cambio
        Assert.Equal("ZD", edited.Name);

        var create = await Assert.ThrowsAsync<ValidationException>(() => Printers.CreateAsync(new PrinterRequest(Name: "X"), default));
        Assert.Equal(PrintingRules.PurposeRequiredMessage, create.Message);
        Assert.Equal("purpose", Assert.Single(create.Errors!.Keys));
    }

    [Fact]
    public async Task Another_company_printer_or_template_is_404()
    {
        var zd = await Printers.CreateAsync(Label("ZD"), default);
        var tpl = await Templates.CreateAsync(new LabelTemplateRequest("4×6 — COD", LabelTemplateService.ToDto(LabelLayout.BuiltIn)), default);
        _db.ChangeTracker.Clear();
        _tenant.TenantId = TenantB;
        var p = await Assert.ThrowsAsync<NotFoundException>(() => Printers.GetAsync(zd.PublicId, default));
        Assert.Equal("Impresora no encontrada.", p.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => Printers.MakeDefaultAsync(zd.PublicId, default));
        var t = await Assert.ThrowsAsync<NotFoundException>(() => Templates.UpdateAsync(tpl.PublicId, new LabelTemplateRequest(Name: "x"), default));
        Assert.Equal("Plantilla de label no encontrada.", t.Message);
        Assert.Empty(await Printers.ListAsync(true, default));
        // la otra compañía puede usar el mismo nombre y su primera también nace predeterminada
        Assert.True((await Printers.CreateAsync(Label("ZD"), default)).IsDefault);
    }

    [Fact]
    public async Task Templates_validate_the_layout_and_follow_the_same_default_rules()
    {
        var missing = await Assert.ThrowsAsync<ValidationException>(() => Templates.CreateAsync(new LabelTemplateRequest("Sin construcción"), default));
        Assert.Equal("Falta la construcción del label.", missing.Message);
        var built = LabelTemplateService.ToDto(LabelLayout.BuiltIn);
        var badPos = built with { Cod = new LabelElementDto(true, "zz", "md") };
        var bad = await Assert.ThrowsAsync<ValidationException>(() => Templates.CreateAsync(new LabelTemplateRequest("Mala", badPos), default));
        Assert.Equal("Posición desconocida en «Monto COD»: 'zz'. Use tl, tc, tr, ml, mc, mr, bl, bc o br.", bad.Message);

        var cod = await Templates.CreateAsync(new LabelTemplateRequest("4×6 — COD", built with { Sender = null, Cod = new LabelElementDto(true, "BR", "lg") }), default);
        Assert.True(cod.IsDefault);
        Assert.Equal(new LabelElementDto(true, "br", "lg"), cod.Layout.Cod);
        Assert.Equal(new LabelElementDto(false, "tc", "sm"), cod.Layout.Sender);   // null = el del integrado
        var std = await Templates.CreateAsync(new LabelTemplateRequest("4×6 — Estándar", built), default);
        Assert.False(std.IsDefault);
        var dup = await Assert.ThrowsAsync<ConflictException>(() => Templates.CreateAsync(new LabelTemplateRequest("4×6 — COD", built), default));
        Assert.Equal("Ya existe una plantilla de label activa con ese nombre.", dup.Message);
        _db.ChangeTracker.Clear();

        await Templates.MakeDefaultAsync(std.PublicId, default);
        _db.ChangeTracker.Clear();
        var list = await Templates.ListAsync(false, null, default);
        Assert.Equal(new[] { std.PublicId }, list.Where(t => t.IsDefault).Select(t => t.PublicId));
        _db.ChangeTracker.Clear();

        var off = await Templates.SetActiveAsync(std.PublicId, false, default);
        Assert.False(off.IsDefault);
        _db.ChangeTracker.Clear();
        var inactive = await Assert.ThrowsAsync<ConflictException>(() => Templates.MakeDefaultAsync(std.PublicId, default));
        Assert.Equal("La plantilla está inactiva; reactívela antes de hacerla predeterminada.", inactive.Message);
        _db.ChangeTracker.Clear();

        // la construcción guardada es el JSON canónico; un JSON dañado en la base se lee como el integrado
        var row = await _db.LabelTemplates.SingleAsync(t => t.PublicId == cod.PublicId);
        Assert.Equal(LabelLayout.TryParse(row.LayoutJson)!.ToJson(), row.LayoutJson);
        row.LayoutJson = "{dañado";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        Assert.Equal(built, (await Templates.GetAsync(cod.PublicId, default)).Layout);
    }

    [Fact]
    public async Task Invoice_labels_are_a_second_kind_of_template_with_their_own_default_and_names()
    {
        var piece = LabelTemplateService.ToDto(LabelLayout.BuiltIn);
        var inv = LabelTemplateService.ToDto(LabelLayout.BuiltInInvoice);

        var p1 = await Templates.CreateAsync(new LabelTemplateRequest("Estándar", piece), default);
        var i1 = await Templates.CreateAsync(new LabelTemplateRequest("Factura", inv, Kind: "invoice"), default);
        Assert.Equal(("PIECE", true), (p1.Kind, p1.IsDefault));
        Assert.Equal(("INVOICE", true), (i1.Kind, i1.IsDefault));          // la primera de CADA tipo nace predeterminada
        // el mismo nombre vale en el otro tipo y la predeterminada de uno no quita la del otro
        var i2 = await Templates.CreateAsync(new LabelTemplateRequest("Estándar", inv, Kind: "INVOICE"), default);
        Assert.False(i2.IsDefault);
        _db.ChangeTracker.Clear();
        await Templates.MakeDefaultAsync(i2.PublicId, default);
        _db.ChangeTracker.Clear();
        Assert.Equal(new[] { p1.PublicId, i2.PublicId }.OrderBy(x => x), (await Templates.ListAsync(false, null, default)).Where(t => t.IsDefault).Select(t => t.PublicId).OrderBy(x => x));
        Assert.Equal(new[] { i1.PublicId, i2.PublicId }.OrderBy(x => x), (await Templates.ListAsync(false, "INVOICE", default)).Select(t => t.PublicId).OrderBy(x => x));
        Assert.Single(await Templates.ListAsync(false, "piece", default));
        var dup = await Assert.ThrowsAsync<ConflictException>(() => Templates.CreateAsync(new LabelTemplateRequest("Factura", inv, Kind: "INVOICE"), default));
        Assert.Equal("Ya existe una plantilla de label activa con ese nombre.", dup.Message);

        // el tipo no se cambia y uno desconocido es 400
        var immutable = await Assert.ThrowsAsync<ValidationException>(() => Templates.UpdateAsync(i1.PublicId, new LabelTemplateRequest(Kind: "PIECE"), default));
        Assert.Equal("El tipo de la plantilla no se puede cambiar; cree otra.", immutable.Message);
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => Templates.CreateAsync(new LabelTemplateRequest("X", inv, Kind: "ZZ"), default));
        Assert.Equal("El tipo de plantilla debe ser PIECE (label de pieza) o INVOICE (label de factura).", unknown.Message);
    }

    [Fact]
    public void The_built_in_invoice_label_is_just_a_centered_barcode_and_the_invoice_element_is_never_used()
    {
        var b = LabelLayout.BuiltInInvoice;
        Assert.Equal(new LabelElement(true, "mc", "lg"), b.Barcode);
        Assert.All(b.Elements().Where(e => e.Key != "barcode"), e => Assert.False(e.Element.On));
        // aunque llegue «Invoice» encendido y como código de barras, en el label de factura se apaga (si no, la factura saldría dos veces)
        var input = LabelLayout.BuiltInInvoice.Elements().ToDictionary(e => e.Key, e => (LabelElement?)e.Element);
        input["invoice"] = new LabelElement(true, "bl", "sm", true);
        var (layout, error) = LabelLayout.Normalize(input, LabelTemplateKinds.Invoice);
        Assert.Null(error);
        Assert.False(layout!.Invoice.On);
        Assert.False(layout.Invoice.AsBarcode);
        Assert.Equal("PIECE", LabelTemplateKinds.Normalize(null));
        Assert.Null(LabelTemplateKinds.Normalize("zz"));
    }

    [Fact]
    public async Task Station_options_bring_only_active_printers_and_templates_and_the_built_in_layout()
    {
        var zd = await Printers.CreateAsync(new PrinterRequest(Name: "ZD", Purpose: "LABEL", Notes: "secreto"), default);
        var old = await Printers.CreateAsync(Label("Vieja"), default);
        await Templates.CreateAsync(new LabelTemplateRequest("COD", LabelTemplateService.ToDto(LabelLayout.BuiltIn)), default);
        _db.ChangeTracker.Clear();
        await Printers.SetActiveAsync(old.PublicId, false, default);
        _db.ChangeTracker.Clear();

        _db.Tenants.Add(new Teikem.Domain.Tenancy.Tenant { TenantId = TenantA, Name = "A" });
        await _db.SaveChangesAsync();
        var options = await Options().GetStationOptionsAsync(default);
        var only = Assert.Single(options.Printers);
        Assert.Equal(zd.PublicId, only.PublicId);
        Assert.True(only.IsDefault);
        Assert.Single(options.Templates);
        Assert.Equal(LabelTemplateService.ToDto(LabelLayout.BuiltIn), options.BuiltInLayout);
        Assert.False(options.PrintLabelsDirect);
        Assert.False(options.QzSigningConfigured);

        // «Imprimir labels directo a la impresora» de la compañía
        Assert.True((await Options().UpdateSettingsAsync(new PrintingSettingsRequest(true), default)).PrintLabelsDirect);
        _db.ChangeTracker.Clear();
        Assert.True((await Options().GetStationOptionsAsync(default)).PrintLabelsDirect);
        Assert.True((await Options().UpdateSettingsAsync(new PrintingSettingsRequest(null), default)).PrintLabelsDirect);   // null = sin cambio
    }

    private PrintingOptionsService Options()
        => new(_db, _tenant, new QzSigningService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
}
