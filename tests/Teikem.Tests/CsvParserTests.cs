using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 3 (P9): lector CSV puro del importador de órdenes (comillas, delimitador, saltos dentro de comillas, cabecera, filas vacías, límites).</summary>
public class CsvParserTests
{
    [Fact]
    public void Parses_simple_rows_with_header_skipped()
    {
        var rows = CsvParser.Parse("name,line1,city\nFarmacia,Calle 5,Ponce\nColmado,Calle 9,Yauco\n", ',', hasHeader: true);
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].RowNumber);
        Assert.Equal(2, rows[0].Line);
        Assert.Equal(new[] { "Farmacia", "Calle 5", "Ponce" }, rows[0].Cells);
        Assert.Equal(2, rows[1].RowNumber);
        Assert.Equal(3, rows[1].Line);
    }

    [Fact]
    public void Without_header_first_line_is_data()
    {
        var rows = CsvParser.Parse("Farmacia,Calle 5,Ponce", ',', hasHeader: false);
        Assert.Single(rows);
        Assert.Equal(1, rows[0].Line);
        Assert.Equal("Ponce", rows[0].Cells[2]);
    }

    [Fact]
    public void Quoted_cell_keeps_delimiter_and_escaped_quotes()
    {
        var rows = CsvParser.Parse("\"Farmacia, Inc.\",\"Calle \"\"5\"\" #12\",Ponce", ',', hasHeader: false);
        Assert.Single(rows);
        Assert.Equal("Farmacia, Inc.", rows[0].Cells[0]);
        Assert.Equal("Calle \"5\" #12", rows[0].Cells[1]);
        Assert.Equal("Ponce", rows[0].Cells[2]);
    }

    [Fact]
    public void Semicolon_delimiter_from_template()
    {
        var rows = CsvParser.Parse("Farmacia;Calle 5, local 2;Ponce\r\nColmado;Calle 9;Yauco", ';', hasHeader: false);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Calle 5, local 2", rows[0].Cells[1]);
        Assert.Equal("Yauco", rows[1].Cells[2]);
    }

    [Fact]
    public void Newline_inside_quotes_stays_in_the_cell_and_line_numbers_follow()
    {
        var rows = CsvParser.Parse("h1,h2\n\"Farmacia\nPonce\",Calle 5\nColmado,Calle 9\n", ',', hasHeader: true);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Farmacia\nPonce", rows[0].Cells[0]);
        Assert.Equal(2, rows[0].Line);
        Assert.Equal(4, rows[1].Line); // la fila anterior ocupó dos líneas físicas
    }

    [Fact]
    public void Empty_rows_are_ignored_and_do_not_count()
    {
        var rows = CsvParser.Parse("h1,h2\n\n  ,  \nFarmacia,Calle 5\n\n", ',', hasHeader: true);
        Assert.Single(rows);
        Assert.Equal(1, rows[0].RowNumber);
        Assert.Equal(4, rows[0].Line);
    }

    [Fact]
    public void Unquoted_cells_are_trimmed_and_bom_is_tolerated()
    {
        var rows = CsvParser.Parse("﻿ Farmacia , Calle 5 ,Ponce", ',', hasHeader: false);
        Assert.Equal(new[] { "Farmacia", "Calle 5", "Ponce" }, rows[0].Cells);
    }

    [Fact]
    public void Empty_content_yields_no_rows()
    {
        Assert.Empty(CsvParser.Parse("", ',', true));
        Assert.Empty(CsvParser.Parse(null, ',', true));
    }

    [Fact]
    public void Row_limit_throws_with_exact_message()
    {
        var content = string.Join("\n", Enumerable.Range(1, 3).Select(i => $"r{i},x"));
        var ex = Assert.Throws<CsvLimitException>(() => CsvParser.Parse(content, ',', hasHeader: false, maxRows: 2));
        Assert.Equal("El archivo supera el límite de 5.000 filas o 2 MB.", ex.Message);
        Assert.Equal(CsvParser.LimitMessage, ex.Message);
        Assert.Equal(5000, CsvParser.MaxRows);
    }

    [Fact]
    public void Size_limit_is_two_megabytes()
    {
        Assert.False(CsvParser.ExceedsSize(new string('a', 100)));
        Assert.True(CsvParser.ExceedsSize(new string('a', CsvParser.MaxBytes + 1)));
        Assert.Throws<CsvLimitException>(() => CsvParser.Parse(new string('a', CsvParser.MaxBytes + 1), ',', false));
    }

    [Fact]
    public void Invalid_delimiter_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => CsvParser.Parse("a,b", '"', false));
        Assert.Throws<ArgumentException>(() => CsvParser.Parse("a,b", '\n', false));
    }
}
