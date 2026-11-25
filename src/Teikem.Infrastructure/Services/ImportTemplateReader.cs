using Teikem.Domain.Orders;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>Una fila del archivo ya mapeada con la plantilla: línea física del archivo y valores por campo canónico (celda o valor fijo de la plantilla).</summary>
public sealed record ImportTemplateRow(int Line, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Lote 246 (Lote F) — lector reutilizable de un archivo con una plantilla de importación del motor genérico (<see cref="ImportEntityMeta"/>): CSV o .xlsx con el
/// delimitador y el encabezado de la plantilla (<see cref="ImportFileReader"/>), columnas y valores fijos de la plantilla (<see cref="ImportTemplateService.ParseColumns"/> /
/// <see cref="ImportTemplateService.ParseDefaults"/>) y <see cref="ImportEntityMeta.MapRow"/> por fila. Omite las filas vacías. Pasarse del límite (5.000 filas / 2 MB)
/// → 400 con <see cref="CsvParser.LimitMessage"/>. No toca la base: quien lo usa resuelve la plantilla (ImportTemplateService.ResolveForImportAsync) y revisa los valores.
/// </summary>
public static class ImportTemplateReader
{
    public static IReadOnlyList<ImportTemplateRow> Read(ImportTemplate template, ImportEntityMeta meta, byte[] bytes, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(meta);
        var columns = ImportTemplateService.ParseColumns(template.ColumnsJson);
        var defaults = meta.NormalizeDefaults(ImportTemplateService.ParseDefaults(template.DefaultsJson));

        ImportFileTable table;
        try { table = ImportFileReader.Read(bytes, fileName, ImportTemplateService.DelimiterOf(template), template.HasHeader, null); }
        catch (CsvLimitException) { throw new ValidationException("file", CsvParser.LimitMessage); }

        var rows = new List<ImportTemplateRow>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Cells.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new ImportTemplateRow(row.Line, meta.MapRow(row.Cells, columns, defaults)));
        }
        return rows;
    }
}
