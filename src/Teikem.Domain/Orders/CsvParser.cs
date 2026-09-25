using System.Text;

namespace Teikem.Domain.Orders;

/// <summary>Fila de datos del archivo: número ordinal (1 = primera fila de datos, sin contar la cabecera) y línea física de inicio.</summary>
public sealed record CsvRow(int RowNumber, int Line, IReadOnlyList<string> Cells);

/// <summary>El archivo excede el límite de filas o de tamaño (el servicio lo traduce a 400 con CsvParser.LimitMessage).</summary>
public sealed class CsvLimitException() : Exception(CsvParser.LimitMessage);

/// <summary>
/// Lector CSV puro (Lote 3, P9 — importador de órdenes): delimitador configurable por plantilla, comillas dobles con
/// escape "" y saltos de línea dentro de comillas, cabecera opcional, filas vacías ignoradas, BOM tolerado.
/// Límites duros: 5.000 filas de datos y 2 MB (CsvLimitException). Sin EF ni servicios: se prueba con xunit.
/// </summary>
public static class CsvParser
{
    public const int MaxRows = 5000;
    public const int MaxBytes = 2 * 1024 * 1024;
    public const char DefaultDelimiter = ',';
    public const string LimitMessage = "El archivo supera el límite de 5.000 filas o 2 MB.";

    /// <summary>¿El contenido supera los 2 MB (en UTF-8)?</summary>
    public static bool ExceedsSize(string? content)
        => content is not null && Encoding.UTF8.GetByteCount(content) > MaxBytes;

    /// <summary>
    /// Parsea el contenido completo. Las celdas sin comillas se recortan; las entrecomilladas se conservan tal cual.
    /// Una fila cuyas celdas están todas vacías se ignora (no cuenta para el límite ni para la numeración).
    /// </summary>
    public static IReadOnlyList<CsvRow> Parse(string? content, char delimiter = DefaultDelimiter, bool hasHeader = true, int maxRows = MaxRows)
    {
        if (delimiter is '"' or '\r' or '\n') throw new ArgumentException("El delimitador no puede ser comilla ni salto de línea.", nameof(delimiter));
        if (string.IsNullOrEmpty(content)) return Array.Empty<CsvRow>();
        if (ExceedsSize(content)) throw new CsvLimitException();

        var text = content[0] == '﻿' ? content[1..] : content;
        var rows = new List<CsvRow>();
        var physicalLine = 1;
        var rowNumber = 0;
        var headerSkipped = !hasHeader;

        foreach (var (cells, startLine, consumedLines) in Records(text, delimiter))
        {
            var lineOfRecord = physicalLine + startLine;
            physicalLine += consumedLines;

            if (cells.All(string.IsNullOrWhiteSpace)) continue; // fila vacía
            if (!headerSkipped) { headerSkipped = true; continue; }

            rowNumber++;
            if (rowNumber > maxRows) throw new CsvLimitException();
            rows.Add(new CsvRow(rowNumber, lineOfRecord, cells));
        }
        return rows;
    }

    /// <summary>Registros lógicos: (celdas, desplazamiento de línea del inicio dentro del bloque, líneas físicas consumidas).</summary>
    private static IEnumerable<(List<string> Cells, int StartLine, int ConsumedLines)> Records(string text, char delimiter)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var wasQuoted = false;
        var consumed = 0;
        var i = 0;

        void Commit()
        {
            cells.Add(wasQuoted ? cell.ToString() : cell.ToString().Trim());
            cell.Clear();
            wasQuoted = false;
        }

        while (i < text.Length)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                if (ch == '\n') consumed++;
                cell.Append(ch); i++; continue;
            }

            if (ch == '"' && cell.Length == 0) { inQuotes = true; wasQuoted = true; i++; continue; }
            if (ch == delimiter) { Commit(); i++; continue; }
            if (ch == '\r' || ch == '\n')
            {
                Commit();
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                i++;
                consumed++;
                yield return (cells, 0, consumed);
                cells = new List<string>();
                consumed = 0;
                continue;
            }
            cell.Append(ch); i++;
        }

        // último registro sin salto de línea final
        if (cell.Length > 0 || cells.Count > 0 || wasQuoted)
        {
            Commit();
            yield return (cells, 0, consumed + 1);
        }
    }
}
