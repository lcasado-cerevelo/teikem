using Teikem.Domain.Constants;
using Teikem.Domain.Printing;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 43 (F-G1) — reglas puras de las impresoras: cada mensaje exacto del manual 12, uso y tamaño, uso inmutable y conexión
/// BROWSER o QZ_TRAY.
/// </summary>
public class PrintingRulesTests
{
    [Fact]
    public void Name_is_required_trimmed_and_at_most_80()
    {
        Assert.Equal((null, "Escriba el nombre de la impresora."), PrintingRules.NormalizeName("   ", PrintingRules.PrinterNameRequiredMessage));
        Assert.Equal((null, "Escriba el nombre de la plantilla."), PrintingRules.NormalizeName(null, PrintingRules.TemplateNameRequiredMessage));
        Assert.Equal(("Zebra ZD420 — Mostrador 1", null), PrintingRules.NormalizeName("  Zebra ZD420 — Mostrador 1 ", PrintingRules.PrinterNameRequiredMessage));
        Assert.Equal((new string('a', 80), null), PrintingRules.NormalizeName(new string('a', 80), PrintingRules.PrinterNameRequiredMessage));
        Assert.Equal((null, "El nombre no puede exceder 80 caracteres."), PrintingRules.NormalizeName(new string('a', 81), PrintingRules.PrinterNameRequiredMessage));
    }

    [Fact]
    public void Model_and_notes_are_optional_with_their_maximums()
    {
        Assert.Equal((null, null), PrintingRules.NormalizeOptional("  ", PrintingRules.ModelMaxLength, PrintingRules.ModelTooLongMessage));
        Assert.Equal(("ZD420", null), PrintingRules.NormalizeOptional(" ZD420 ", PrintingRules.ModelMaxLength, PrintingRules.ModelTooLongMessage));
        Assert.Equal((null, "El modelo no puede exceder 80 caracteres."), PrintingRules.NormalizeOptional(new string('m', 81), PrintingRules.ModelMaxLength, PrintingRules.ModelTooLongMessage));
        Assert.Equal((null, "La nota no puede exceder 300 caracteres."), PrintingRules.NormalizeOptional(new string('n', 301), PrintingRules.NotesMaxLength, PrintingRules.NotesTooLongMessage));
        Assert.Equal((new string('n', 300), null), PrintingRules.NormalizeOptional(new string('n', 300), PrintingRules.NotesMaxLength, PrintingRules.NotesTooLongMessage));
    }

    [Fact]
    public void Purpose_is_required_and_known()
    {
        Assert.Equal((null, "Indique el uso de la impresora: LABEL (labels) o DOCUMENT (documentos)."), PrintingRules.NormalizePurpose(null));
        Assert.Equal((null, "Indique el uso de la impresora: LABEL (labels) o DOCUMENT (documentos)."), PrintingRules.NormalizePurpose(" "));
        Assert.Equal(("LABEL", null), PrintingRules.NormalizePurpose("label"));
        Assert.Equal(("DOCUMENT", null), PrintingRules.NormalizePurpose(" Document "));
        Assert.Equal((null, "Uso de impresora desconocido: 'POSTER'."), PrintingRules.NormalizePurpose("POSTER"));
    }

    [Fact]
    public void Purpose_cannot_change_but_the_same_purpose_is_not_a_change()
    {
        Assert.False(PrintingRules.ChangesPurpose(PrinterPurposes.Label, null));
        Assert.False(PrintingRules.ChangesPurpose(PrinterPurposes.Label, "label"));
        Assert.True(PrintingRules.ChangesPurpose(PrinterPurposes.Label, "DOCUMENT"));
        Assert.Equal("El uso de la impresora no se puede cambiar; cree otra impresora.", PrintingRules.PurposeImmutableMessage);
    }

    [Fact]
    public void Connection_is_browser_or_qz_tray()
    {
        Assert.Equal(("BROWSER", null), PrintingRules.NormalizeConnection(null));
        Assert.Equal(("BROWSER", null), PrintingRules.NormalizeConnection("browser"));
        Assert.Equal(("QZ_TRAY", null), PrintingRules.NormalizeConnection("qz_tray"));
        Assert.Equal(("QZ_TRAY", null), PrintingRules.NormalizeConnection("QZ-Tray"));
        foreach (var other in new[] { "NETWORK", "RAW9100", "IPP" })
            Assert.Equal((null, $"Conexión de impresora desconocida: '{other}'. Use BROWSER (PDF del navegador) o QZ_TRAY (directo con QZ Tray)."), PrintingRules.NormalizeConnection(other));
        Assert.Equal((null, "El nombre en el sistema no puede exceder 200 caracteres."),
            PrintingRules.NormalizeOptional(new string('s', 201), PrintingRules.SystemNameMaxLength, PrintingRules.SystemNameTooLongMessage));
    }

    [Fact]
    public void Media_size_depends_on_the_purpose()
    {
        Assert.Equal(("4X6", null), PrintingRules.NormalizeMedia(PrinterPurposes.Label, null));
        Assert.Equal(("LETTER", null), PrintingRules.NormalizeMedia(PrinterPurposes.Document, ""));
        Assert.Equal(("4X4", null), PrintingRules.NormalizeMedia(PrinterPurposes.Label, "4x4"));
        Assert.Equal(("4X2", null), PrintingRules.NormalizeMedia(PrinterPurposes.Label, "4 × 2"));
        Assert.Equal((null, "Una impresora de labels usa etiquetas de 4X6, 4X4 o 4X2."), PrintingRules.NormalizeMedia(PrinterPurposes.Label, "LETTER"));
        Assert.Equal((null, "Una impresora de documentos usa papel carta (LETTER)."), PrintingRules.NormalizeMedia(PrinterPurposes.Document, "4X6"));
        Assert.Equal((null, "Una impresora de documentos usa papel carta (LETTER)."), PrintingRules.NormalizeMedia(PrinterPurposes.Document, "A4"));
    }

    [Fact]
    public void Conflict_messages_are_exact()
    {
        Assert.Equal("Ya existe una impresora activa con ese nombre.", PrintingRules.DuplicatePrinterMessage);
        Assert.Equal("La impresora está inactiva; reactívela antes de hacerla predeterminada.", PrintingRules.PrinterInactiveDefaultMessage);
        Assert.Equal("Ya existe una plantilla de label activa con ese nombre.", PrintingRules.DuplicateTemplateMessage);
        Assert.Equal("La plantilla está inactiva; reactívela antes de hacerla predeterminada.", PrintingRules.TemplateInactiveDefaultMessage);
    }
}

/// <summary>
/// Lote 43 (F-G1) — construcción del label: el integrado es válido, posición y tamaño conocidos, «como código de barras» solo en waybill
/// y factura, al menos un código de barras visible, y el JSON canónico de ida y vuelta (≤ 2000 caracteres).
/// </summary>
public class LabelLayoutTests
{
    private static Dictionary<string, LabelElement?> Input(params (string Key, LabelElement? E)[] items)
    {
        var d = new Dictionary<string, LabelElement?>(StringComparer.Ordinal);
        foreach (var (k, e) in items) d[k] = e;
        return d;
    }

    [Fact]
    public void Built_in_layout_is_valid_and_matches_the_mockup()
    {
        var b = LabelLayout.BuiltIn;
        Assert.True(b.HasVisibleBarcode());
        Assert.Equal(new LabelElement(true, "tl", "md"), b.Logo);
        Assert.Equal(new LabelElement(false, "tc", "sm"), b.Sender);
        Assert.Equal(new LabelElement(true, "mc", "md"), b.Barcode);
        Assert.Equal(new LabelElement(false, "bl", "sm", false), b.Waybill);
        Assert.Equal(new LabelElement(true, "br", "md"), b.Cod);
        Assert.Equal(9, b.Elements().Count());
        var (layout, error) = LabelLayout.Normalize(Input());
        Assert.Null(error);
        Assert.Equal(b, layout);
    }

    [Fact]
    public void Unknown_position_or_size_names_the_element()
    {
        var (_, pos) = LabelLayout.Normalize(Input(("cod", new LabelElement(true, "xx", "md"))));
        Assert.Equal("Posición desconocida en «Monto COD»: 'xx'. Use tl, tc, tr, ml, mc, mr, bl, bc o br.", pos);
        var (_, size) = LabelLayout.Normalize(Input(("consignee", new LabelElement(true, "ml", "xl"))));
        Assert.Equal("Tamaño desconocido en «Consignatario»: 'xl'. Use sm, md o lg.", size);
        // mayúsculas y espacios se aceptan; vacío = el del integrado
        var (ok, none) = LabelLayout.Normalize(Input(("cod", new LabelElement(true, " BR ", "LG")), ("logo", new LabelElement(true, "", ""))));
        Assert.Null(none);
        Assert.Equal(new LabelElement(true, "br", "lg"), ok!.Cod);
        Assert.Equal(new LabelElement(true, "tl", "md"), ok.Logo);
    }

    [Fact]
    public void As_barcode_only_for_waybill_and_invoice()
    {
        var (_, error) = LabelLayout.Normalize(Input(("consignee", new LabelElement(true, "ml", "md", true))));
        Assert.Equal("Solo el número de waybill y la factura se pueden mostrar como código de barras.", error);
        var (ok, none) = LabelLayout.Normalize(Input(("waybill", new LabelElement(true, "bl", "sm", true)), ("barcode", new LabelElement(true, "mc", "md", true))));
        Assert.Null(none);
        Assert.True(ok!.Waybill.AsBarcode);
        Assert.False(ok.Barcode.AsBarcode);   // el código de barras siempre lo es: la bandera se ignora
    }

    [Fact]
    public void At_least_one_visible_barcode()
    {
        var off = new LabelElement(false, "mc", "md");
        var (_, error) = LabelLayout.Normalize(Input(("barcode", off)));
        Assert.Equal("El label necesita al menos un código de barras para poder escanearlo.", error);
        // la factura como código de barras basta
        var (ok, none) = LabelLayout.Normalize(Input(("barcode", off), ("invoice", new LabelElement(true, "bl", "md", true))));
        Assert.Null(none);
        Assert.True(ok!.HasVisibleBarcode());
        // waybill como código pero oculto no cuenta
        var (_, hidden) = LabelLayout.Normalize(Input(("barcode", off), ("invoice", new LabelElement(true, "bl", "md")), ("waybill", new LabelElement(false, "bl", "md", true))));
        Assert.Equal(LabelLayout.NeedsBarcodeMessage, hidden);
    }

    [Fact]
    public void Canonical_json_round_trips_and_fits_the_column()
    {
        var (layout, _) = LabelLayout.Normalize(Input(("sender", new LabelElement(true, "tc", "lg")), ("invoice", new LabelElement(true, "br", "lg", true))));
        var json = layout!.ToJson();
        Assert.StartsWith("{\"logo\":{\"on\":true,\"pos\":\"tl\",\"size\":\"md\",\"asBarcode\":false},\"service\":", json);
        Assert.True(json.Length <= LabelLayout.MaxJsonLength, $"{json.Length} caracteres");
        Assert.Equal(layout, LabelLayout.TryParse(json));
        Assert.Equal(json, LabelLayout.TryParse(json)!.ToJson());
        // dañado o inválido: null (quien lee usa el integrado)
        Assert.Null(LabelLayout.TryParse("{no es json"));
        Assert.Null(LabelLayout.TryParse("[]"));
        Assert.Null(LabelLayout.TryParse("{\"barcode\":{\"on\":false,\"pos\":\"mc\",\"size\":\"md\"}}"));
        // claves desconocidas se ignoran
        Assert.Equal(LabelLayout.BuiltIn, LabelLayout.TryParse("{\"foo\":1}"));
    }
}
