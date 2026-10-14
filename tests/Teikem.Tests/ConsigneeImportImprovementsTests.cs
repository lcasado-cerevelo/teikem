using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 45 (F-B2): libro .xlsx mínimo escrito a mano (OpenXML con cadenas compartidas y estilos), para probar el lector real
/// (ExcelDataReader) sin archivos binarios en el repositorio. Estilos: 0 general, 1 «00000», 2 fecha, 3 hora, 4 fecha y hora.
/// </summary>
internal static class Xlsx
{
    /// <summary>Celda con estilo (número de cellXfs).</summary>
    public sealed record Styled(object Value, int Style);

    public static byte[] Build(params (string Name, object?[][] Rows)[] sheets)
    {
        var strings = new List<string>();
        int Str(string s) { var i = strings.IndexOf(s); if (i >= 0) return i; strings.Add(s); return strings.Count - 1; }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Put(string path, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }

            var sheetXml = new List<string>();
            foreach (var (_, rows) in sheets)
            {
                var sb = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
                for (var r = 0; r < rows.Length; r++)
                {
                    sb.Append($"<row r=\"{r + 1}\">");
                    for (var c = 0; c < rows[r].Length; c++)
                    {
                        var v = rows[r][c];
                        if (v is null) continue;
                        var style = 0;
                        if (v is Styled st) { style = st.Style; v = st.Value; }
                        var refName = $"{(char)('A' + c)}{r + 1}";
                        sb.Append(v switch
                        {
                            string s => $"<c r=\"{refName}\" t=\"s\" s=\"{style}\"><v>{Str(s)}</v></c>",
                            bool b => $"<c r=\"{refName}\" t=\"b\" s=\"{style}\"><v>{(b ? 1 : 0)}</v></c>",
                            DateTime d => $"<c r=\"{refName}\" s=\"{style}\"><v>{d.ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>",
                            IFormattable n => $"<c r=\"{refName}\" s=\"{style}\"><v>{n.ToString(null, CultureInfo.InvariantCulture)}</v></c>",
                            _ => throw new ArgumentException("celda no soportada"),
                        });
                    }
                    sb.Append("</row>");
                }
                sb.Append("</sheetData></worksheet>");
                sheetXml.Add(sb.ToString());
            }

            var overrides = string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
            Put("[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
                overrides + "</Types>");
            Put("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Put("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                string.Concat(sheets.Select((s, i) => $"<sheet name=\"{SecurityElement.Escape(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) +
                "<Relationship Id=\"rIdS\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "<Relationship Id=\"rIdSS\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/></Relationships>");
            Put("xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"00000\"/></numFmts>" +
                "<fonts count=\"1\"><font/></fonts><fills count=\"1\"><fill/></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs>" +
                "<cellXfs count=\"5\"><xf numFmtId=\"0\"/><xf numFmtId=\"164\" applyNumberFormat=\"1\"/><xf numFmtId=\"14\" applyNumberFormat=\"1\"/><xf numFmtId=\"20\" applyNumberFormat=\"1\"/><xf numFmtId=\"22\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
            for (var i = 0; i < sheetXml.Count; i++) Put($"xl/worksheets/sheet{i + 1}.xml", sheetXml[i]);
            Put("xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                string.Concat(strings.Select(s => $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(s)}</t></si>")) + "</sst>");
        }
        return ms.ToArray();
    }
}

/// <summary>Lote 45 (F-B2): lector de archivos de importación (CSV y Excel .xlsx) con el ExcelDataReader real.</summary>
public sealed class ImportFileReaderTests
{
    [Fact]
    public void Csv_keeps_the_header_apart_and_numbers_the_data_rows()
    {
        var t = ImportFileReader.Read(Encoding.UTF8.GetBytes("﻿código;nombre\n\nA-1;Uno\nA-2;Dos\n"), "a.csv", ';', hasHeader: true, sheet: null);
        Assert.Equal(ImportFileFormats.Csv, t.Format);
        Assert.Empty(t.Sheets);
        Assert.Equal(new[] { "código", "nombre" }, t.Header);
        Assert.Equal(new[] { (1, 3), (2, 4) }, t.Rows.Select(r => (r.RowNumber, r.Line)));
        Assert.Equal(new[] { "A-2", "Dos" }, t.Rows[1].Cells);
    }

    [Fact]
    public void Csv_encoding_is_detected_utf8_with_or_without_bom_else_windows_1252()
    {
        const string text = "nombre;pueblo\nPeñuelas Ñame;Añasco\n";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1252 = Encoding.GetEncoding(1252).GetBytes(text);           // lo que exporta Excel en Windows («CSV»), sin BOM
        Assert.Equal(text, ImportFileReader.DecodeText(cp1252));
        Assert.Equal(text, ImportFileReader.DecodeText(Encoding.UTF8.GetBytes(text)));                 // UTF-8 sin BOM
        Assert.Equal(text, ImportFileReader.DecodeText(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()));
        Assert.Equal(text, ImportFileReader.DecodeText(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()));
        var t = ImportFileReader.Read(cp1252, "consignatarios.csv", ';', hasHeader: true, sheet: null);
        Assert.Equal(new[] { "Peñuelas Ñame", "Añasco" }, t.Rows[0].Cells);
    }

    [Fact]
    public void Xlsx_reads_the_first_sheet_or_the_chosen_one_and_converts_cells_like_the_user_sees_them()
    {
        var bytes = Xlsx.Build(
            ("Portada", new object?[][] { new object?[] { "Nada que ver" } }),
            ("Consignatarios", new object?[][]
            {
                new object?[] { "Código", "Nombre", "ZIP", "ZIP sin formato", "Desde", "Fecha", "Fecha y hora", "Activo", "Decimal" },
                new object?[] { 1001, "Farmacia Uno", new Xlsx.Styled(901, 1), 901, new Xlsx.Styled(new DateTime(1899, 12, 30, 8, 0, 0), 3),
                    new Xlsx.Styled(new DateTime(2026, 10, 13), 2), new Xlsx.Styled(new DateTime(2026, 10, 13, 14, 30, 0), 4), true, 2.5 },
                new object?[] { },                                       // fila vacía: no cuenta
                new object?[] { "  C-2 ", "Farmacia Dos" },
            }));

        var first = ImportFileReader.Read(bytes, "libro.xlsx", ',', hasHeader: true, sheet: null);
        Assert.Equal(ImportFileFormats.Xlsx, first.Format);
        Assert.Equal(new[] { "Portada", "Consignatarios" }, first.Sheets);
        Assert.Equal("Portada", first.Sheet);
        Assert.Empty(first.Rows);

        var t = ImportFileReader.Read(bytes, "libro.xlsx", ',', hasHeader: true, sheet: " consignatarios ");
        Assert.Equal("Consignatarios", t.Sheet);
        Assert.Equal("Código", t.Header![0]);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(new[] { "1001", "Farmacia Uno", "00901", "901", "08:00", "2026-10-13", "2026-10-13 14:30", "true", "2.5" }, t.Rows[0].Cells);
        Assert.Equal((1, 2), (t.Rows[0].RowNumber, t.Rows[0].Line));
        Assert.Equal((2, 4), (t.Rows[1].RowNumber, t.Rows[1].Line));   // la línea física salta la fila vacía
        Assert.Equal(new[] { "C-2", "Farmacia Dos" }, t.Rows[1].Cells); // sin las celdas vacías del final, recortado
    }

    [Fact]
    public void Unknown_sheet_old_xls_and_broken_files_are_400_with_their_message()
    {
        var bytes = Xlsx.Build(("Hoja1", new object?[][] { new object?[] { "a" } }));
        var sheet = Assert.Throws<ValidationException>(() => ImportFileReader.Read(bytes, "x.xlsx", ',', true, "Otra"));
        Assert.Equal("El libro no tiene la hoja 'Otra'.", sheet.Message);

        var xls = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0 };
        Assert.Equal("Formato de archivo no admitido: use CSV o Excel (.xlsx).",
            Assert.Throws<ValidationException>(() => ImportFileReader.Read(xls, "viejo.xls", ',', true, null)).Message);

        var broken = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5 };
        Assert.Equal("No se pudo leer el archivo de Excel; ábralo y guárdelo otra vez como .xlsx.",
            Assert.Throws<ValidationException>(() => ImportFileReader.Read(broken, "roto.xlsx", ',', true, null)).Message);
    }

    [Fact]
    public void The_row_limit_applies_to_excel_too()
    {
        var rows = new[] { new object?[] { "nombre" } }.Concat(Enumerable.Range(1, 4).Select(i => new object?[] { $"F{i}" })).ToArray();
        var bytes = Xlsx.Build(("Hoja1", rows));
        Assert.Equal(4, ImportFileReader.Read(bytes, "x.xlsx", ',', true, null, maxRows: 4).Rows.Count);
        Assert.Throws<CsvLimitException>(() => ImportFileReader.Read(bytes, "x.xlsx", ',', true, null, maxRows: 3));
    }

    [Theory]
    [InlineData(901d, "00000", "00901")] [InlineData(901d, "General", "901")] [InlineData(901d, null, "901")] [InlineData(123456d, "00000", "123456")]
    [InlineData(7875550101d, "0", "7875550101")] [InlineData(0.25d, null, "0.25")] [InlineData(-5d, "00000", "-5")]
    public void Number_cells_become_plain_text(double value, string? format, string expected)
        => Assert.Equal(expected, ImportFileReader.NumberText(value, format));

    [Fact]
    public void Excel_times_are_rounded_to_the_second()
        => Assert.Equal("08:00", ImportFileReader.DateText(new DateTime(1899, 12, 30, 7, 59, 59, 999)));
}

/// <summary>
/// Lote 45 (F-B2): mejoras de la importación de consignatarios con el servicio real (InMemory): Excel con hoja elegida, «No usar»,
/// columnas unidas, persona de contacto, ZIP rellenado, revisión del archivo, cliente por defecto de la plantilla y el espejo SQL.
/// </summary>
public sealed class ConsigneeImportImprovementsTests
{
    private static Task<ImportTemplateDto> TemplateAsync(WmsFixture f, string name, IList<ImportTemplateColumnDto> columns, Guid? defaultClient = null, Guid? client = null)
        => f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest(name, columns, ClientPublicId: client, DefaultClientPublicId: defaultClient), ImportKinds.Consignee, default);

    /// <summary>Cód. | Nombre | (basura) | Calle | Urb. | Pueblo | ZIP | Contacto | Teléfono.</summary>
    private static readonly ImportTemplateColumnDto[] Joined =
    [
        new(1, "code"), new(1, "name", 1), new(2, "name", 2, " - "), new(3, "ignore"), new(4, "line1"), new(5, "line1", Separator: ", "),
        new(6, "city"), new(7, "postalCode"), new(8, "contactName"), new(9, "contactPhone"),
    ];

    [Fact]
    public async Task Excel_file_with_a_chosen_sheet_goes_through_the_same_mapping_and_row_validation()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var tpl = await TemplateAsync(f, "Excel", Joined);
        var bytes = Xlsx.Build(
            ("Notas", new object?[][] { new object?[] { "ignorar" } }),
            ("Datos", new object?[][]
            {
                new object?[] { "Cód", "Nombre", "Basura", "Calle", "Urb", "Pueblo", "ZIP", "Contacto", "Tel" },
                new object?[] { "C-001", "Farmacia Uno", "x", "Calle 1", "Urb. Sol", "San Juan", 901, "Ana Rivera", 7875550101d },
                new object?[] { "C-002", null, "x", "Calle 2", null, "Ponce", new Xlsx.Styled(716, 1), null, null },
            }));

        var preview = await f.Get<ConsigneeImportService>().ValidateFileAsync(tpl.PublicId, null, "consignatarios.xlsx", bytes, "Datos", default);

        Assert.Equal(2, preview.RowCount);
        Assert.Equal("consignatarios.xlsx", preview.FileName);
        var r1 = preview.Rows[0];
        Assert.True(r1.IsValid, string.Join(" | ", r1.Errors.Values));
        Assert.Equal("C-001", r1.Values["code"]);
        Assert.Equal("C-001 - Farmacia Uno", r1.Values["name"]);
        Assert.Equal("Calle 1, Urb. Sol", r1.Values["line1"]);
        Assert.Equal("00901", r1.Values["postalCode"]);          // Excel lo guardó como número: se rellena (PR)
        Assert.Equal("Ana Rivera", r1.Values["contactName"]);
        Assert.Contains("555", r1.Values["contactPhone"]);       // el número de Excel pasa la misma validación del ContactPoint
        Assert.False(r1.Values.ContainsKey("ignore"));
        Assert.Equal(2, r1.Line);

        var r2 = preview.Rows[1];
        Assert.True(r2.IsValid);
        Assert.Equal("C-002", r2.Values["name"]);                // la parte vacía no deja el separador
        Assert.Equal("00716", r2.Values["postalCode"]);
        Assert.False(r2.Values.ContainsKey("contactName"));
    }

    [Fact]
    public async Task The_csv_json_validation_also_joins_ignores_and_validates_the_contact_name()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var tpl = await TemplateAsync(f, "CSV", Joined);
        var preview = await f.Get<ConsigneeImportService>().ValidateAsync(new ConsigneeImportValidateRequest(tpl.PublicId, null,
            "c,n,b,l,u,p,z,contacto,t\nC-9,Nueve,,Calle 9,,Ponce,2108," + new string('x', 151) + ",\n"), default);

        Assert.Equal("Máximo 150 caracteres.", preview.Rows[0].Errors["contactName"]);
        Assert.Equal("02108", preview.Rows[0].Values["postalCode"]);
    }

    [Fact]
    public async Task A_windows_1252_csv_with_generic_columns_mixed_phones_and_repeated_numbers_validates_with_its_messages()
    {
        // Forma de un archivo real (datos sintéticos): Windows-1252 sin BOM, columnas vacías o «ColumnN» que no se usan, teléfonos en
        // formatos mezclados (a veces dos en la celda), un número de consignatario repetido y ZIP vacío.
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var tpl = await TemplateAsync(f, "Real", new ImportTemplateColumnDto[]
        {
            new(1, "code"), new(2, "name"), new(3, "ignore"), new(4, "ignore"), new(5, "line1"), new(6, "city"), new(7, "postalCode"), new(8, "contactPhone"),
        });
        const string csv =
            "Numero,Nombre,Column2,Column3,Direccion,Pueblo,Zip,Telefono\r\n" +
            "N-1,Farmacia Peñuelas,,,Calle Ñ 1,Peñuelas,,(787)555-0101\r\n" +
            "N-2,Colmado Añasco,x,y,Calle 2,Añasco,610,787 555-0202 / 939-555-0303\r\n" +
            "N-1,Otra con el mismo número,,,Calle 3,Ponce,00716,7875550404\r\n";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1252).GetBytes(csv);

        var preview = await f.Get<ConsigneeImportService>().ValidateFileAsync(tpl.PublicId, null, "consignatarios.csv", bytes, null, default);

        var r1 = preview.Rows[0];
        Assert.True(r1.IsValid, string.Join(" | ", r1.Errors.Values));
        Assert.Equal("Farmacia Peñuelas", r1.Values["name"]);       // la ñ llega bien
        Assert.Equal("7875550101", r1.Values["contactPhone"]);
        Assert.Equal("00624", r1.Values["postalCode"]);             // ZIP vacío: Lote 46, el primero de entrega del pueblo
        Assert.Contains("postalCode", r1.Inferred!);
        var r2 = preview.Rows[1];
        Assert.True(r2.IsValid);
        Assert.Equal("7875550202", r2.Values["contactPhone"]);
        Assert.Equal("00610", r2.Values["postalCode"]);
        Assert.Contains("La celda del teléfono trae más de un número; se tomó el primero (7875550202).", r2.Warnings);
        Assert.Equal("El código N-1 se repite en la fila 1 del archivo.", preview.Rows[2].Errors["code"]); // dice el número y la fila
        Assert.Equal(2, preview.ValidRows);
    }

    [Fact]
    public async Task Inspect_file_reports_sheets_headers_and_samples_without_saving_anything()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var bytes = Xlsx.Build(
            ("Uno", new object?[][] { new object?[] { "A", "B" }, new object?[] { "a1", null }, new object?[] { "a2", "b2" }, new object?[] { "a3", "b3" }, new object?[] { "a4", "b4" } }),
            ("Dos", new object?[][] { new object?[] { "X" } }));
        var info = ConsigneeImportService.InspectFile(bytes, "libro.xlsx", null, hasHeader: true, sheet: null);

        Assert.Equal("XLSX", info.Format);
        Assert.Equal(new[] { "Uno", "Dos" }, info.Sheets);
        Assert.Equal("Uno", info.Sheet);
        Assert.Equal(4, info.RowCount);
        Assert.Equal(new[] { "A", "B" }, info.Columns.Select(c => c.Header));
        Assert.Equal(new[] { "a1", "a2", "a3" }, info.Columns[0].Samples);
        Assert.Equal(new[] { "b2", "b3", "b4" }, info.Columns[1].Samples);   // las celdas vacías no son muestra
        Assert.Equal(new[] { "a1" }, info.SampleRows[0]);                     // primeras 3 filas completas (sin las vacías del final)
        Assert.Equal(new[] { "a3", "b3" }, info.SampleRows[2]);
        Assert.Equal(3, info.SampleRows.Count);

        var csv = ConsigneeImportService.InspectFile(Encoding.UTF8.GetBytes("n|l\nUno|Calle\n"), "a.csv", "|", hasHeader: false, sheet: null);
        Assert.Equal(2, csv.RowCount);                                      // sin encabezado, la primera fila es de datos
        Assert.All(csv.Columns, c => Assert.Null(c.Header));
        Assert.Equal(0, await f.Db.ImportBatches.CountAsync());

        Assert.Equal("Indique el contenido del archivo CSV.",
            Assert.Throws<ValidationException>(() => ConsigneeImportService.InspectFile(Array.Empty<byte>(), "a.csv", null, true, null)).Message);
    }

    [Fact]
    public async Task The_template_keeps_ignored_columns_joined_parts_and_its_default_client()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var acme = await f.AddClientAsync("ACME");
        var tpl = await TemplateAsync(f, "Guardada", Joined, defaultClient: acme.PublicId);

        Assert.Equal(acme.PublicId, tpl.DefaultClientPublicId);
        Assert.Equal("Cliente ACME", tpl.DefaultClientName);
        Assert.Null(tpl.ClientPublicId);                                    // sigue siendo general
        Assert.Contains(tpl.Columns, c => c is { Position: 3, Field: "ignore" });
        Assert.Equal(new[] { (1, (int?)1, (string?)null), (2, 2, " - ") }, tpl.Columns.Where(c => c.Field == "name").Select(c => (c.Position, c.Part, c.Separator)));
        Assert.Equal(new[] { (4, (int?)1, (string?)null), (5, 2, ", ") }, tpl.Columns.Where(c => c.Field == "line1").Select(c => (c.Position, c.Part, c.Separator)));
        Assert.Equal(new ImportTemplateColumnDto(1, "code"), tpl.Columns.Single(c => c.Field == "code"));

        var listed = (await f.Get<ImportTemplateService>().GetListAsync(ImportKinds.Consignee, null, null, false, default)).Single();
        Assert.Equal("Cliente ACME", listed.DefaultClientName);

        var cleared = await f.Get<ImportTemplateService>().UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(ClearDefaultClient: true), ImportKinds.Consignee, default);
        Assert.Null(cleared.DefaultClientPublicId);
        Assert.Equal(10, cleared.Columns.Count);                            // el PATCH sin columnas no las toca
    }

    [Fact]
    public async Task Default_client_rules_use_their_messages()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var acme = await f.AddClientAsync("ACME");
        var otro = await f.AddClientAsync("OTRO");
        var gone = await f.AddClientAsync("BAJA");
        using (f.AsTenant(WmsFixture.TenantId))
        {
            (await f.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == gone.ClientId)).IsActive = false;
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        }
        var svc = f.Get<ImportTemplateService>();

        var mismatch = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, "De ACME", Joined, defaultClient: otro.PublicId, client: acme.PublicId));
        Assert.Equal("El cliente por defecto debe ser el mismo cliente de la plantilla.", mismatch.Message);
        Assert.Equal(ClientQueries.ClientInactiveMessage, (await Assert.ThrowsAsync<ConflictException>(() => TemplateAsync(f, "Baja", Joined, defaultClient: gone.PublicId))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => TemplateAsync(f, "Nadie", Joined, defaultClient: Guid.NewGuid()));

        var tpl = await TemplateAsync(f, "General", Joined);
        var both = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(DefaultClientPublicId: acme.PublicId, ClearDefaultClient: true), ImportKinds.Consignee, default));
        Assert.Equal("Indique un cliente por defecto o quítelo, no ambos.", both.Message);
        var set = await svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(DefaultClientPublicId: otro.PublicId), ImportKinds.Consignee, default);
        Assert.Equal(otro.PublicId, set.DefaultClientPublicId);
        // volverla plantilla de ACME con OTRO por defecto no cuadra
        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(ClientPublicId: acme.PublicId), ImportKinds.Consignee, default));
    }

    [Fact]
    public async Task Confirming_with_remember_client_saves_it_in_the_template()
    {
        // La confirmación usa ExecuteUpdateAsync (no soportado en InMemory): se prueba el paso que la confirmación llama.
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var acme = await f.AddClientAsync("ACME");
        var otro = await f.AddClientAsync("OTRO");
        var svc = f.Get<ImportTemplateService>();
        var tpl = await TemplateAsync(f, "Recordar", Joined);

        await svc.RememberDefaultClientAsync(tpl.Id, acme.ClientId, default);
        Assert.Equal(acme.PublicId, (await svc.GetAsync(tpl.PublicId, ImportKinds.Consignee, default)).DefaultClientPublicId);
        await svc.RememberDefaultClientAsync(tpl.Id, null, default);        // importó como compartidos
        Assert.Null((await svc.GetAsync(tpl.PublicId, ImportKinds.Consignee, default)).DefaultClientPublicId);

        var ofAcme = await TemplateAsync(f, "De ACME", Joined, client: acme.PublicId);
        await svc.RememberDefaultClientAsync(ofAcme.Id, otro.ClientId, default); // la plantilla de un cliente no recuerda otro
        Assert.Null((await svc.GetAsync(ofAcme.PublicId, ImportKinds.Consignee, default)).DefaultClientPublicId);
    }

    [Fact]
    public async Task Contact_name_is_a_real_field_of_the_consignee()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var locations = f.Get<LocationService>();
        var created = await locations.CreateAsync(new LocationUpsertRequest(null, null, "Farmacia", LocationTypes.Delivery, "Calle 1", null, "Ponce", null, null, null,
            ContactName: "  Ana Rivera "), default);
        Assert.Equal("Ana Rivera", created.ContactName);

        var changed = await locations.UpdateAsync(created.PublicId, new LocationPatchRequest(ContactName: "Luis"), default);
        Assert.Equal("Luis", changed.ContactName);
        var cleared = await locations.UpdateAsync(created.PublicId, new LocationPatchRequest(ContactName: ""), default);
        Assert.Null(cleared.ContactName);
        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => locations.UpdateAsync(created.PublicId, new LocationPatchRequest(ContactName: new string('x', 151)), default));
        Assert.Equal("Máximo 150 caracteres.", tooLong.Message);

        Assert.Null((await f.Db.Locations.AsNoTracking().SingleAsync(l => l.PublicId == created.PublicId)).ContactName);
        Assert.Single(await locations.GetListAsync(new LocationQuery(null, Search: "Farmacia"), default));
    }

    [Fact]
    public async Task Ef_model_maps_contact_name_and_the_default_client()
    {
        await using var f = await WmsFixture.CreateAsync();
        var location = f.Db.Model.FindEntityType(typeof(Location))!;
        Assert.Equal(150, location.FindProperty(nameof(Location.ContactName))!.GetMaxLength());
        Assert.True(location.FindProperty(nameof(Location.ContactName))!.IsNullable);
        var template = f.Db.Model.FindEntityType(typeof(ImportTemplate))!;
        var fk = template.GetForeignKeys().Single(k => k.Properties.Single().Name == nameof(ImportTemplate.DefaultClientId));
        Assert.False(fk.IsRequired);
        Assert.Equal(typeof(Client), fk.PrincipalEntityType.ClrType);
    }

    [Fact]
    public void Update_sql_adds_both_columns_in_a_new_idempotent_section_at_the_end()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-13 — Importación de consignatarios, mejoras (Lote 45, F-B2", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-12 — Duración de la sesión en MINUTOS", StringComparison.Ordinal));
        var section = sql[start..];
        var end = section.IndexOf("\n/* ---", 10, StringComparison.Ordinal);
        if (end > 0) section = section[..end];
        Assert.Contains("IF COL_LENGTH('dbo.Location', 'ContactName') IS NULL\n    ALTER TABLE dbo.Location ADD ContactName NVARCHAR(150) NULL;", section);
        Assert.Contains("IF COL_LENGTH('dbo.ImportTemplate', 'DefaultClientId') IS NULL\n    ALTER TABLE dbo.ImportTemplate ADD DefaultClientId INT NULL CONSTRAINT FK_ImportTemplate_DefaultClient REFERENCES dbo.Client(ClientId);", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
    }
}
