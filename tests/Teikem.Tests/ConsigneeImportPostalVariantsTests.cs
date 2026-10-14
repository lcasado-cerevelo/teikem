using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 46 (F-B3): reglas puras de la inferencia de pueblo, estado y ZIP de entrega con el catálogo de códigos postales
/// (PostalInference) y de los tipos de ZIP (ZipClasses). Catálogo sintético; los datos reales los carga logistica-db-zip-catalog.sql.
/// </summary>
public sealed class ConsigneePostalInferenceTests
{
    private static PostalEntry Pr(int order, string city, string zip, string municipality, string? zipClass)
        => PostalEntry.Create(order, zip, city, "PR", municipality, "PR", zipClass);

    private static PostalEntry Us(int order, string city, string zip, string state, string? zipClass)
        => PostalEntry.Create(order, zip, city, state, null, "US", zipClass);

    private static readonly PostalIndex Index = new(new[]
    {
        Pr(1, "PONCE", "00716", "Ponce", ZipClasses.Standard), Pr(2, "PONCE", "00728", "Ponce", ZipClasses.Standard), Pr(3, "PONCE", "00734", "Ponce", ZipClasses.PoBox),
        Pr(4, "BAYAMON", "00961", "Bayamón", ZipClasses.Standard), Pr(5, "BAYAMON", "00956", "Bayamón", ZipClasses.Standard), Pr(6, "BAYAMON", "00960", "Bayamón", ZipClasses.PoBox),
        Pr(7, "TOA BAJA", "00949", "Toa Baja", ZipClasses.Standard), Pr(8, "SABANA SECA", "00952", "Toa Baja", ZipClasses.Standard),
        Pr(9, "ANGELES", "00611", "Utuado", ZipClasses.PoBox), Pr(10, "UTUADO", "00641", "Utuado", ZipClasses.Standard),
        Pr(11, "CATANO", "00962", "Cataño", null), Pr(12, "FT BUCHANAN", "00934", "Guaynabo", ZipClasses.Unique),
        Pr(13, "VILLA UNICA", "00990", "Villa Única", ZipClasses.Unique), Pr(14, "VILLA UNICA", "00995", "Villa Única", ZipClasses.Standard),
        Us(20, "BOSTON", "02108", "MA", ZipClasses.Standard), Us(21, "SPRINGFIELD", "62701", "IL", ZipClasses.Standard), Us(22, "SPRINGFIELD", "01103", "MA", ZipClasses.Standard),
        Us(23, "APO", "09001", "AE", ZipClasses.Military), Us(24, "BARROW", "99723", "AK", ZipClasses.PoBox),
    });

    private static PostalInferenceResult Infer(string? city, string? zip, string? state = null, string country = "PR")
        => PostalInference.Infer(Index, country, city, state, zip);

    [Fact]
    public void A_zip_fills_the_town_with_the_municipality_and_the_state()
    {
        var r = Infer(null, "00952");
        Assert.False(r.IsRejected);
        Assert.Equal(("Toa Baja", "PR", "00952"), (r.City, r.State, r.PostalCode)); // el pueblo es el municipio, no el barrio postal
        Assert.Equal(new[] { "city", "state" }, r.Inferred);
        Assert.Null(r.OriginalPostalCode);
        Assert.Equal(new[] { "Pueblo tomado del código postal 00952: Toa Baja.", "Estado tomado del catálogo de códigos postales: PR." }, r.Warnings);
    }

    [Fact]
    public void A_town_alone_gets_the_first_delivery_zip_in_a_fixed_order()
    {
        var r = Infer("bayamón", null);
        Assert.Equal(("bayamón", "PR", "00956"), (r.City, r.State, r.PostalCode)); // el menor STANDARD, no el primero del catálogo (00961)
        Assert.Equal(new[] { "postalCode", "state" }, r.Inferred);
        Assert.Contains("Código postal asignado: 00956, el primero de entrega de bayamón.", r.Warnings);

        Assert.Equal("00949", Infer("TOA BAJA", null).PostalCode);           // por ciudad postal antes que por municipio
        Assert.Equal("00952", Infer("Sabana Seca", null).PostalCode);
        Assert.Equal("00934", Infer("Guaynabo", null).PostalCode);           // por municipio; solo UNIQUE → ese
        Assert.Equal("00995", Infer("Villa Única", null).PostalCode);        // STANDARD antes que UNIQUE aunque el código sea mayor
        Assert.Equal("00962", Infer("Cataño", null).PostalCode);             // sin clasificar (el script de datos no lo trae): se admite
        Assert.Equal("00641", Infer("Utuado", null).PostalCode);
    }

    [Fact]
    public void Zip_and_town_are_both_kept_and_a_mismatch_is_only_a_warning()
    {
        var ok = Infer("Toa Baja", "00952");
        Assert.Empty(ok.Warnings.Where(w => w.Contains("no de", StringComparison.Ordinal)));
        Assert.Equal(new[] { "state" }, ok.Inferred);

        var mismatch = Infer("Caguas", "00716");
        Assert.False(mismatch.IsRejected);
        Assert.Equal(("Caguas", "00716"), (mismatch.City, mismatch.PostalCode));
        Assert.Contains("El código postal 00716 es de Ponce, no de Caguas; se guardan los dos como vienen.", mismatch.Warnings);

        var state = Infer("Ponce", "00716", state: "NY");
        Assert.Equal("NY", state.State);
        Assert.Contains("El estado NY no coincide con el del código postal (PR); se guarda como viene.", state.Warnings);
        Assert.DoesNotContain(Infer("Ponce", "00716", state: "Puerto Rico").Warnings, w => w.StartsWith("El estado", StringComparison.Ordinal));
    }

    [Fact]
    public void A_po_box_or_military_zip_is_replaced_by_a_delivery_zip_of_the_same_city_or_rejected()
    {
        var r = Infer(null, "00734");
        Assert.Equal(("00716", "00734", "Ponce"), (r.PostalCode, r.OriginalPostalCode, r.City));
        Assert.Contains("postalCode", r.Inferred);
        Assert.Contains("Código postal ajustado: 00734 es de apartados postales (P.O. Box); se usó 00716, de entrega en Ponce.", r.Warnings);

        // la ciudad postal ANGELES solo tiene apartados: se busca en su municipio
        var byMunicipality = Infer("Utuado", "00611");
        Assert.Equal(("00641", "00611"), (byMunicipality.PostalCode, byMunicipality.OriginalPostalCode));
        Assert.DoesNotContain(byMunicipality.Warnings, w => w.Contains("no de", StringComparison.Ordinal)); // el pueblo coincide con el ZIP original

        var military = Infer(null, "09001", country: "US");
        Assert.True(military.IsRejected);
        Assert.Equal("postalCode", military.ErrorField);
        Assert.Equal("El código postal 09001 es de correo militar (APO/FPO) y APO no tiene un código postal de entrega con que sustituirlo; indique el código postal de la dirección física.", military.Error);
        Assert.Equal("El código postal 99723 es de apartados postales (P.O. Box) y BARROW no tiene un código postal de entrega con que sustituirlo; indique el código postal de la dirección física.",
            Infer(null, "99723", country: "US").Error);
    }

    [Fact]
    public void Unknown_zip_or_town_rejects_the_row_with_a_clear_message()
    {
        Assert.Equal(("postalCode", "El código postal 00999 no está en el catálogo de códigos postales; corríjalo, o déjelo vacío e indique el pueblo."),
            (Infer(null, "00999").ErrorField, Infer(null, "00999").Error));
        Assert.Equal("El código postal 9A1 no es válido: use 5 dígitos (o ZIP+4, como 00901-1234).", Infer("Ponce", "9A1").Error);
        Assert.Equal(("city", "El pueblo 'Ciudad Gótica' no está en el catálogo de códigos postales; corríjalo o indique el código postal."),
            (Infer("Ciudad Gótica", null).ErrorField, Infer("Ciudad Gótica", null).Error));
        Assert.Equal("El pueblo 'Barrow' no tiene un código postal de entrega (solo de apartados postales o militares); indique el código postal de la dirección física.",
            Infer("Barrow", null, country: "US").Error);
        Assert.Equal("El pueblo 'Springfield' existe en varios estados (IL, MA); indique el estado o el código postal.", Infer("Springfield", null, country: "US").Error);
        Assert.Equal(("01103", "ma"), (Infer("Springfield", null, state: "ma", country: "US").PostalCode, Infer("Springfield", null, state: "ma", country: "US").State)); // el estado del archivo no se reescribe
    }

    [Fact]
    public void Zip_plus_four_compares_by_its_five_digits_and_uncovered_countries_are_left_alone()
    {
        var r = Infer(null, "00716-1234");
        Assert.Equal(("00716-1234", "Ponce"), (r.PostalCode, r.City)); // el ZIP+4 de entrega se guarda como viene
        Assert.Equal(("02108", "BOSTON", "MA"), (Infer(null, "02108", country: "us").PostalCode, Infer(null, "02108", country: "us").City, Infer(null, "02108", country: "us").State));

        var other = Infer("Santo Domingo", "10101", country: "DO");
        Assert.False(other.IsRejected);
        Assert.Empty(other.Inferred);
        Assert.Empty(other.Warnings);
        Assert.False(Infer(null, null).IsRejected); // sin pueblo ni ZIP decide la regla de obligatorios
    }

    [Theory]
    [InlineData("00901", true, "00901")]
    [InlineData(" 00901-1234 ", true, "00901")]
    [InlineData("009011234", true, "00901")]
    [InlineData("901", false, "")]
    [InlineData("00901-12", false, "")]
    [InlineData("ABCDE", false, "")]
    public void Zip5_reads_five_digits_or_zip_plus_four(string text, bool ok, string expected)
    {
        Assert.Equal(ok, PostalInference.TryZip5(text, out var zip));
        Assert.Equal(expected, zip);
    }

    [Fact]
    public void Zip_class_filter_accepts_the_four_classes_and_delivery()
    {
        Assert.Equal((null, true, null), ZipClasses.ParseFilter(null));
        Assert.True(ZipClasses.ParseFilter("delivery").IncludeUnclassified);      // DELIVERY: misma regla que la importación
        Assert.False(ZipClasses.ParseFilter("STANDARD,UNIQUE").IncludeUnclassified);
        Assert.True(ZipClasses.Passes(null, ZipClasses.ParseFilter("DELIVERY").Classes, true));
        Assert.False(ZipClasses.Passes("PO_BOX", ZipClasses.ParseFilter("DELIVERY").Classes, true));
        Assert.Equal(new[] { "STANDARD", "UNIQUE" }, ZipClasses.ParseFilter("delivery").Classes!.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { "MILITARY", "PO_BOX" }, ZipClasses.ParseFilter("PO_BOX, military").Classes!.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal("BOX", ZipClasses.ParseFilter("STANDARD,BOX").Invalid);
        Assert.Equal("Tipo de código postal desconocido: BOX. Use STANDARD, PO_BOX, UNIQUE, MILITARY o DELIVERY.", ZipClasses.InvalidFilterMessage("BOX"));
        Assert.True(ZipClasses.IsDelivery(null));
        Assert.False(ZipClasses.IsDelivery(ZipClasses.PoBox));
        Assert.False(ZipClasses.IsDelivery(ZipClasses.Military));
    }
}

/// <summary>Lote 46 (F-B3): reglas puras de las variantes de mapeo (condición por fila).</summary>
public sealed class ConsigneeImportVariantRulesTests
{
    private static ImportColumn[] Cols(params string[] fields) => fields.Select((f, i) => new ImportColumn(i + 1, f)).ToArray();

    private static ImportVariant V(string? name, ImportColumn[] columns, params ImportVariantCondition[] conditions) => new(name, conditions, columns);

    [Theory]
    [InlineData("EMPTY", null, "", true)]
    [InlineData("EMPTY", null, "  ", true)]
    [InlineData("EMPTY", null, "x", false)]
    [InlineData("NOT_EMPTY", null, "x", true)]
    [InlineData("not_empty", null, "", false)]
    [InlineData("EQUALS", "puerto rico", " PUERTO   RICO ", true)]
    [InlineData("EQUALS", "Bayamón", "BAYAMON", true)]
    [InlineData("EQUALS", "PR", "PUERTO RICO", false)]
    [InlineData("CONTAINS", "rico", "Puerto Rico", true)]
    [InlineData("CONTAINS", "ponce", "Puerto Rico", false)]
    [InlineData("CONTAINS", "x", "", false)]
    [InlineData("OTHER", "x", "x", false)]
    public void Conditions_compare_without_accents_case_or_extra_spaces(string op, string? value, string cell, bool expected)
        => Assert.Equal(expected, ConsigneeImportVariants.Matches(new[] { "a", cell }, new ImportVariantCondition(2, op, value)));

    [Fact]
    public void A_missing_cell_counts_as_empty()
    {
        Assert.True(ConsigneeImportVariants.Matches(new[] { "a" }, new ImportVariantCondition(9, "EMPTY")));
        Assert.False(ConsigneeImportVariants.Matches(new[] { "a" }, new ImportVariantCondition(9, "NOT_EMPTY")));
    }

    [Fact]
    public void The_first_variant_whose_conditions_all_hold_wins()
    {
        var variants = new[]
        {
            V("B", Cols("name", "line1", "city"), new ImportVariantCondition(9, "EQUALS", "PUERTO RICO"), new ImportVariantCondition(8, "NOT_EMPTY")),
            V("A", Cols("name", "line1", "postalCode"), new ImportVariantCondition(9, "EMPTY")),
        };
        var b = new[] { "1", "n", "c", "t", "n", "l", "barrio", "Ponce", "Puerto Rico" };
        var bWithoutTown = new[] { "1", "n", "c", "t", "n", "l", "barrio", "", "Puerto Rico" };
        var a = new[] { "1", "n", "l1", "l2", "00716", "c", "t" };
        Assert.Equal(0, ConsigneeImportVariants.Select(b, variants));
        Assert.Equal(-1, ConsigneeImportVariants.Select(bWithoutTown, variants)); // B pide las dos; A pide la 9 vacía
        Assert.Equal(1, ConsigneeImportVariants.Select(a, variants));
        Assert.Equal(0, ConsigneeImportVariants.Select(a, new[] { V(null, Cols("name", "line1", "city")) })); // sin condición: siempre
        Assert.Equal("Variante 2", ConsigneeImportVariants.DisplayName(V("  ", Cols("name")), 1));
        Assert.Equal("Formato B", ConsigneeImportVariants.DisplayName(V(" Formato B ", Cols("name")), 0));
    }

    [Fact]
    public void Validation_reports_conditions_order_and_each_variant_mapping_with_its_prefix()
    {
        var errors = ConsigneeImportVariants.Validate(new[]
        {
            V(new string('x', 61), Cols("name", "line1", "city")),
            V("B", Cols("name", "city"), new ImportVariantCondition(0, "EQUALS"), new ImportVariantCondition(2, "LIKE", "a"), new ImportVariantCondition(3, "CONTAINS", new string('y', 101))),
            V("C", Cols("name", "line1")),
        }, new Dictionary<string, string> { ["defaultServiceMinutes"] = "diez" });

        Assert.Equal("Máximo 60 caracteres.", errors["variants[0].name"]);
        Assert.Equal("La variante 1 no tiene condición y deja sin uso a las que le siguen; póngala al final o agréguele una condición.", errors["variants[0].conditions"]);
        Assert.Equal("La posición debe ser 1 o mayor.", errors["variants[1].conditions[0].position"]);
        Assert.Equal("La condición «igual a» o «contiene» necesita un valor.", errors["variants[1].conditions[0].value"]);
        Assert.Equal("Condición desconocida: LIKE. Use EMPTY, NOT_EMPTY, EQUALS o CONTAINS.", errors["variants[1].conditions[1].operator"]);
        Assert.Equal("Máximo 100 caracteres.", errors["variants[1].conditions[2].value"]);
        Assert.Equal("La plantilla debe incluir las columnas name y line1.", errors["variants[1].columns"]);
        Assert.Equal("La plantilla debe indicar la ciudad (city) o el código postal (postalCode), en una columna o en defaults.", errors["variants[2].columns"]);
        Assert.Equal("Los minutos de servicio deben ser un número entero.", errors["defaults.defaultServiceMinutes"]); // una sola vez, sin prefijo
        Assert.False(errors.ContainsKey("variants[2].conditions"));                                                 // la última puede ir sin condición

        Assert.Equal("Una plantilla admite a lo sumo 10 variantes.",
            ConsigneeImportVariants.Validate(Enumerable.Range(0, 11).Select(_ => V(null, Cols("name", "line1", "city"), new ImportVariantCondition(1, "EMPTY"))).ToList(), null)["variants"]);
        Assert.Equal("Una variante admite a lo sumo 5 condiciones.",
            ConsigneeImportVariants.Validate(new[] { V(null, Cols("name", "line1", "city"), Enumerable.Range(1, 6).Select(p => new ImportVariantCondition(p, "EMPTY")).ToArray()) }, null)["variants[0].conditions"]);
    }

    [Fact]
    public void Normalize_trims_and_drops_the_value_of_empty_checks()
    {
        var v = ConsigneeImportVariants.Normalize(new ImportVariant("  ", new[] { new ImportVariantCondition(9, " equals ", " Puerto Rico "), new ImportVariantCondition(5, "empty", "x") },
            new[] { new ImportColumn(2, "NAME"), new ImportColumn(1, "line1") }));
        Assert.Null(v.Name);
        Assert.Equal(new[] { new ImportVariantCondition(9, "EQUALS", "Puerto Rico"), new ImportVariantCondition(5, "EMPTY") }, v.Conditions);
        Assert.Equal(new[] { new ImportColumn(1, "line1"), new ImportColumn(2, "name") }, v.Columns);
    }
}

/// <summary>
/// Lote 46 (F-B3): la importación con el servicio real (InMemory) sobre el catálogo sintético de ConsigneeImportWorld: inferencia y
/// ajuste del ZIP en la vista previa, plantilla con variantes (archivo con dos disposiciones de columnas; datos sintéticos), alta y
/// edición de variantes, búsqueda de localidades por tipo, modelo EF y la sección SQL.
/// </summary>
public sealed class ConsigneeImportPostalVariantsTests
{
    /// <summary>Formato A: número, nombre, dirección 1, dirección 2, ZIP, contacto, teléfono.</summary>
    private static readonly ImportTemplateColumnDto[] FormatA =
        [new(1, "code"), new(2, "name"), new(3, "line1"), new(4, "line2"), new(5, "postalCode"), new(6, "contactName"), new(7, "contactPhone")];

    /// <summary>Formato B: número, nombre, contacto, teléfono, nombre repetido, dirección 1, barrio, pueblo, país.</summary>
    private static readonly ImportTemplateColumnDto[] FormatB =
        [new(1, "code"), new(2, "name"), new(3, "contactName"), new(4, "contactPhone"), new(5, "ignore"), new(6, "line1"), new(7, "line2"), new(8, "city"), new(9, "ignore")];

    private static ImportTemplateVariantDto[] MixedVariants() =>
    [
        new("Formato B", new[] { new ImportVariantConditionDto(9, "EQUALS", "PUERTO RICO") }, FormatB),
        new("Formato A", new[] { new ImportVariantConditionDto(9, "EMPTY") }, FormatA),
    ];

    private static Task<ImportTemplateDto> MixedTemplateAsync(WmsFixture f)
        => f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("Mixta", null, Variants: MixedVariants()), ImportKinds.Consignee, default);

    // Datos sintéticos con la forma del archivo real (dos disposiciones mezcladas en las mismas filas).
    private const string MixedCsv =
        "Numero,Nombre,C3,C4,C5,C6,C7,C8,C9\n" +
        "A-1,Colmado Uno,Calle 1,Urb. Sol,00952,Ana Prueba,787-555-0101,,\n" +
        "B-1,Farmacia Dos,Beto Prueba,787-555-0202,Farmacia Dos,Carr. 2 km 3,Bo. Pueblo,Bayamón,PUERTO RICO\n" +
        "A-2,Ferretería Tres,Calle 3,,00734,,,,\n" +
        "X-1,Fila rara,a,b,c,d,e,f,USA\n";

    [Fact]
    public async Task A_mixed_file_uses_the_first_matching_variant_per_row_and_rejects_rows_without_one()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var tpl = await MixedTemplateAsync(f);
        Assert.Equal(2, tpl.Variants!.Count);
        Assert.Equal(FormatB.Select(c => c.Position), tpl.Columns.Select(c => c.Position)); // ColumnsJson = la primera variante

        var preview = await f.Get<ConsigneeImportService>().ValidateAsync(new ConsigneeImportValidateRequest(tpl.PublicId, null, MixedCsv), default);
        Assert.Equal(4, preview.RowCount);
        Assert.Equal(3, preview.ValidRows);

        var a1 = preview.Rows[0];
        Assert.True(a1.IsValid, string.Join(" | ", a1.Errors.Values));
        Assert.Equal((2, "Formato A"), (a1.Variant, a1.VariantName));
        Assert.Equal(("Toa Baja", "PR", "00952", "Urb. Sol"), (a1.Values["city"], a1.Values["state"], a1.Values["postalCode"], a1.Values["line2"]));
        Assert.Equal(new[] { "city", "state" }, a1.Inferred);

        var b1 = preview.Rows[1];
        Assert.True(b1.IsValid, string.Join(" | ", b1.Errors.Values));
        Assert.Equal((1, "Formato B"), (b1.Variant, b1.VariantName));
        Assert.Equal(("Farmacia Dos", "Carr. 2 km 3", "Bo. Pueblo", "Bayamón", "00956"), (b1.Values["name"], b1.Values["line1"], b1.Values["line2"], b1.Values["city"], b1.Values["postalCode"]));
        Assert.Equal("Beto Prueba", b1.Values["contactName"]);
        Assert.False(b1.Values.ContainsKey("country") && b1.Values["country"] != "PR"); // la columna «PUERTO RICO» no se usa: país por defecto
        Assert.Contains("Código postal asignado: 00956, el primero de entrega de Bayamón.", b1.Warnings);

        var a2 = preview.Rows[2];
        Assert.True(a2.IsValid);
        Assert.Equal(("00716", "00734", "Ponce"), (a2.Values["postalCode"], a2.OriginalPostalCode, a2.Values["city"]));
        Assert.Contains("Código postal ajustado: 00734 es de apartados postales (P.O. Box); se usó 00716, de entrega en Ponce.", a2.Warnings);

        var x1 = preview.Rows[3];
        Assert.False(x1.IsValid);
        Assert.Null(x1.Variant);
        Assert.Equal("Ninguna variante de la plantilla aplica a esta fila; revise las condiciones de las variantes.", x1.Errors["variant"]);
        Assert.Equal("USA", x1.Cells![8]);
        Assert.Null(x1.Owner);

        // Lo guardado en el lote (RowsJson) devuelve lo mismo
        var again = await f.Get<ConsigneeImportService>().GetAsync(preview.BatchPublicId, default);
        Assert.Equal(("00734", "Formato A"), (again.Rows[2].OriginalPostalCode, again.Rows[2].VariantName));
        Assert.Equal(new[] { "city", "state" }, again.Rows[0].Inferred);
    }

    [Fact]
    public async Task Unknown_zip_or_town_rejects_the_row_and_other_countries_are_not_touched()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        // un país que el catálogo de códigos postales no cubre
        var all = await f.Db.LookupCodes.AsNoTracking().ToListAsync();
        var dominican = new LookupCode { LookupCodeId = 9002, Entity = LookupDomains.Country, InternalCode = "DO", LabelJson = "{\"es\":\"República Dominicana\"}", IsActive = true };
        f.Db.LookupCodes.Add(dominican);
        await f.Db.SaveChangesAsync();
        all.Add(dominican);
        f.Lookups.Load(all);
        var tpl = await f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("Simple",
            new ImportTemplateColumnDto[] { new(1, "name"), new(2, "line1"), new(3, "city"), new(4, "postalCode"), new(5, "country") }), ImportKinds.Consignee, default);
        var preview = await f.Get<ConsigneeImportService>().ValidateAsync(new ConsigneeImportValidateRequest(tpl.PublicId, null,
            "n,l,p,z,c\nUno,Calle 1,,00999,\nDos,Calle 2,Ciudad Gótica,,\nTres,Calle 3,Santo Domingo,10101,DO\nCuatro,Calle 4,,,\nCinco,Calle 5,Caguas,00716,PR\n"), default);

        Assert.Equal("El código postal 00999 no está en el catálogo de códigos postales; corríjalo, o déjelo vacío e indique el pueblo.", preview.Rows[0].Errors["postalCode"]);
        Assert.Equal("El pueblo 'Ciudad Gótica' no está en el catálogo de códigos postales; corríjalo o indique el código postal.", preview.Rows[1].Errors["city"]);
        Assert.True(preview.Rows[2].IsValid);                     // fuera del catálogo (DO): como antes
        Assert.Null(preview.Rows[2].Inferred);
        Assert.Equal("La ciudad es obligatoria.", preview.Rows[3].Errors["city"]); // ni pueblo ni ZIP
        Assert.True(preview.Rows[4].IsValid);                     // ZIP y pueblo que no coinciden: aviso, se guardan los dos
        Assert.Equal(("Caguas", "00716"), (preview.Rows[4].Values["city"], preview.Rows[4].Values["postalCode"]));
        Assert.Contains("El código postal 00716 es de Ponce, no de Caguas; se guardan los dos como vienen.", preview.Rows[4].Warnings);
    }

    [Fact]
    public async Task Variants_are_saved_listed_edited_and_removed_and_mixing_them_with_columns_is_rejected()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var svc = f.Get<ImportTemplateService>();
        var tpl = await MixedTemplateAsync(f);
        var stored = await f.Db.ImportTemplates.AsNoTracking().SingleAsync(t => t.PublicId == tpl.PublicId);
        Assert.Contains("\"operator\":\"EQUALS\"", stored.VariantsJson);
        Assert.Equal(new ImportVariantConditionDto(9, "EQUALS", "PUERTO RICO"), tpl.Variants![0].Conditions!.Single());
        Assert.Equal("Formato A", (await svc.GetListAsync(ImportKinds.Consignee, null, null, false, default)).Single().Variants![1].Name);

        // Con variantes, columns no va
        var both = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ImportTemplateCreateRequest("Ambas", FormatA, Variants: MixedVariants()), ImportKinds.Consignee, default));
        Assert.Equal("Esta plantilla usa variantes: las columnas van dentro de cada variante (variants), no en columns.", both.Errors!["columns"][0]);
        var patchColumns = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(Columns: FormatA), ImportKinds.Consignee, default));
        Assert.Equal("Esta plantilla usa variantes: las columnas van dentro de cada variante (variants), no en columns.", patchColumns.Errors!["columns"][0]);
        // Las órdenes no tienen variantes
        var order = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ImportTemplateCreateRequest("Órdenes", null, Variants: MixedVariants()), ImportKinds.Order, default));
        Assert.Equal("Las variantes de mapeo solo existen en las plantillas de consignatarios.", order.Errors!["variants"][0]);
        // Errores con su llave por variante
        var bad = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(Variants: new[]
        {
            new ImportTemplateVariantDto("Sin condición", null, FormatA), new ImportTemplateVariantDto("B", new[] { new ImportVariantConditionDto(9, "EQUALS") }, FormatB),
        }), ImportKinds.Consignee, default));
        Assert.Equal("La variante 1 no tiene condición y deja sin uso a las que le siguen; póngala al final o agréguele una condición.", bad.Errors!["variants[0].conditions"][0]);
        Assert.Equal("La condición «igual a» o «contiene» necesita un valor.", bad.Errors!["variants[1].conditions[0].value"][0]);

        // Editar: la segunda sin condición (al final) y otro nombre
        var edited = await svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(Variants: new[] { MixedVariants()[0], new ImportTemplateVariantDto("Resto", null, FormatA) }), ImportKinds.Consignee, default);
        Assert.Equal(new[] { "Formato B", "Resto" }, edited.Variants!.Select(v => v.Name));
        Assert.Empty(edited.Variants![1].Conditions!);
        // Cambiar otra cosa no toca las variantes
        Assert.Equal(2, (await svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(Name: "Mixta 2"), ImportKinds.Consignee, default)).Variants!.Count);

        // [] vuelve a una sola variante con las columnas de la primera
        var single = await svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(Variants: Array.Empty<ImportTemplateVariantDto>()), ImportKinds.Consignee, default);
        Assert.Null(single.Variants);
        Assert.Equal(FormatB.Select(c => c.Field), single.Columns.Select(c => c.Field));
        Assert.Null((await f.Db.ImportTemplates.AsNoTracking().SingleAsync(t => t.PublicId == tpl.PublicId)).VariantsJson);
        // y una plantilla sin variantes se edita con columns, como siempre
        Assert.Equal(7, (await svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(Columns: FormatA), ImportKinds.Consignee, default)).Columns.Count);
    }

    [Fact]
    public async Task Postal_locality_search_returns_the_zip_class_and_filters_by_it()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var svc = new PostalLocalityService(f.Db, f.Tenant, f.Get<ILookupCache>(), new MemoryCache(new MemoryCacheOptions()));

        var all = await svc.SearchAsync("ponce", 0, default);
        Assert.Equal(new[] { ("00716", "STANDARD"), ("00728", "STANDARD"), ("00734", "PO_BOX") }, all.Select(l => (l.PostalCode, l.ZipClass!)));
        Assert.Equal(new[] { "00716", "00728" }, (await svc.SearchAsync("ponce", 0, default, "delivery")).Select(l => l.PostalCode));
        Assert.Equal(new[] { "00734" }, (await svc.SearchAsync("ponce", 0, default, "PO_BOX")).Select(l => l.PostalCode));
        Assert.Empty(await svc.SearchAsync("catano", 0, default, "STANDARD,UNIQUE,PO_BOX,MILITARY")); // sin clasificar: no salen con tipos explícitos
        Assert.Single(await svc.SearchAsync("catano", 0, default));
        Assert.Single(await svc.SearchAsync("catano", 0, default, "DELIVERY"));                     // con DELIVERY también
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.SearchAsync("ponce", 0, default, "BOX"));
        Assert.Equal("Tipo de código postal desconocido: BOX. Use STANDARD, PO_BOX, UNIQUE, MILITARY o DELIVERY.", ex.Errors!["zipClass"][0]);

        var index = await svc.IndexAsync(default);
        Assert.True(index.Covers("PR"));
        Assert.True(index.Covers("us"));
        Assert.False(index.Covers("DO"));
        Assert.Equal("Ponce", index.ByZip("PR", "00734").Single().Town);
    }

    [Fact]
    public void Ef_model_maps_the_new_postal_columns_and_the_template_variants()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var e = db.Model.FindEntityType(typeof(PostalLocality))!;
        var zipClass = e.FindProperty(nameof(PostalLocality.ZipClass))!;
        Assert.Equal((10, false, true), (zipClass.GetMaxLength(), zipClass.IsUnicode(), zipClass.IsNullable));
        var locale = e.FindProperty(nameof(PostalLocality.LocaleType))!;
        Assert.Equal((1, true, false), (locale.GetMaxLength(), locale.IsFixedLength(), locale.IsUnicode()));
        Assert.Equal(40, e.FindProperty(nameof(PostalLocality.DistrictName))!.GetMaxLength());
        Assert.Equal((3, false), (e.FindProperty(nameof(PostalLocality.DistrictNo))!.GetMaxLength(), e.FindProperty(nameof(PostalLocality.DistrictNo))!.IsUnicode()));
        foreach (var p in new[] { nameof(PostalLocality.Latitude), nameof(PostalLocality.Longitude) })
            Assert.Equal((9, 6), (e.FindProperty(p)!.GetPrecision(), e.FindProperty(p)!.GetScale()));
        Assert.Equal(typeof(byte?), e.FindProperty(nameof(PostalLocality.GeoAccuracy))!.ClrType);
        Assert.Contains(e.GetIndexes(), i => i.GetDatabaseName() == "IX_PostalLocality_PostalCode" && !i.IsUnique);

        var t = db.Model.FindEntityType(typeof(ImportTemplate))!;
        var variants = t.FindProperty(nameof(ImportTemplate.VariantsJson))!;
        Assert.True(variants.IsNullable);
        Assert.Null(variants.GetMaxLength());
    }

    [Fact]
    public void Update_sql_adds_the_postal_columns_and_template_variants_in_a_new_idempotent_section_at_the_end()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-14 — Catálogo de códigos postales con tipo, distrito y geolocalización", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-13 — Importación de consignatarios, mejoras (Lote 45, F-B2", StringComparison.Ordinal));
        var section = UpdateSqlSection.From(sql, start);
        foreach (var (column, type) in new[] { ("ZipClass", "VARCHAR(10)"), ("LocaleType", "CHAR(1)"), ("DistrictName", "NVARCHAR(40)"), ("DistrictNo", "VARCHAR(3)"),
                     ("Latitude", "DECIMAL(9,6)"), ("Longitude", "DECIMAL(9,6)"), ("GeoAccuracy", "TINYINT") })
            Assert.Contains($"IF COL_LENGTH('dbo.PostalLocality', '{column}') IS NULL\n    ALTER TABLE dbo.PostalLocality ADD {column} {type} NULL;", section);
        Assert.Contains("CHECK (ZipClass IS NULL OR ZipClass IN ('STANDARD','PO_BOX','UNIQUE','MILITARY'))", section);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PostalLocality_PostalCode'", section);
        Assert.Contains("IF COL_LENGTH('dbo.ImportTemplate', 'VariantsJson') IS NULL\n    ALTER TABLE dbo.ImportTemplate ADD VariantsJson NVARCHAR(MAX) NULL;", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", section, StringComparison.OrdinalIgnoreCase);

        // El script de datos aparte usa esas columnas, exige la sección antes (guarda) y da crédito a GeoNames
        var data = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-zip-catalog.sql"));
        Assert.Contains("IF COL_LENGTH('dbo.PostalLocality', 'ZipClass') IS NULL OR COL_LENGTH('dbo.PostalLocality', 'Latitude') IS NULL", data);
        Assert.Contains("Creative Commons", data);
        Assert.Contains("(City, PostalCode, State, CountryLookupId, ZipClass, LocaleType, DistrictName, DistrictNo, Latitude, Longitude, GeoAccuracy)", data);
    }
}
