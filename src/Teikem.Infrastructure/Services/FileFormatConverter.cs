using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>El archivo ya convertido al CSV que lee el importador de órdenes (con el delimitador y el encabezado que dice la plantilla) y cuántas filas de datos trae.</summary>
public sealed record ConvertedFile(string Kind, string Content, int Rows);

/// <summary>
/// Lote 137 (P11) — convierte lo que llega (hoja de Excel .xlsx, JSON o XML con mapeo por ruta) al CSV de la plantilla de importación, para que el resto del camino sea el de siempre (hash, importador,
/// bandeja). Un CSV no se toca (devuelve null). Los problemas del archivo salen como 400 con el mensaje exacto y el archivo queda en la bandeja.
/// </summary>
public static class FileFormatConverter
{
    public const string NotExcelMessage = "El archivo no es un libro de Excel (.xlsx).";

    public static ConvertedFile? Convert(byte[] bytes, string fileName, FileFormatConfig? config, ImportTemplate template)
    {
        var kind = FileFormatRules.Resolve(config, fileName, bytes);
        var delimiter = ImportTemplateService.DelimiterOf(template);
        switch (kind)
        {
            case FileFormatKinds.Excel:
            {
                if (!ImportFileReader.LooksLikeXlsx(bytes, fileName)) throw new ValidationException("file", NotExcelMessage);
                var table = Read(bytes, fileName, delimiter, template.HasHeader, config?.Sheet);
                return new ConvertedFile(kind, FileFormatRules.ToCsv(template.HasHeader ? table.Header : null, table.Rows.Select(r => r.Cells), delimiter), table.Rows.Count);
            }
            case FileFormatKinds.Json or FileFormatKinds.Xml:
            {
                var table = FileFormatRules.ToTable(bytes, kind, config ?? new FileFormatConfig(), out var error) ?? throw new ValidationException("file", error!);
                return new ConvertedFile(kind, FileFormatRules.ToCsv(template.HasHeader ? table.Header : null, table.Rows, delimiter), table.Rows.Count);
            }
            default:
                return null;   // CSV: sigue el camino de siempre
        }
    }

    /// <summary>Lee un libro de Excel (la hoja pedida o la primera); un libro con más filas o bytes que el límite del CSV se rechaza con su mensaje.</summary>
    public static ImportFileTable Read(byte[] bytes, string fileName, char delimiter, bool hasHeader, string? sheet)
    {
        try { return ImportFileReader.Read(bytes, fileName, delimiter, hasHeader, string.IsNullOrWhiteSpace(sheet) ? null : sheet.Trim()); }
        catch (CsvLimitException) { throw new ValidationException("file", CsvParser.LimitMessage); }
    }
}
