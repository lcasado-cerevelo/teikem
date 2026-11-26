using Teikem.Domain.Clients;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 248 — marcas del tipo de documento del cliente («lleva vigencia», «es de impuestos») en ExtraJson, pisadas por el CustomExtraJson de la compañía.</summary>
public sealed class ClientDocumentTypeMarksTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("{}", false, false)]
    [InlineData("{roto", false, false)]
    [InlineData("[true]", false, false)]
    [InlineData("{\"requiresValidTo\":true,\"isTax\":true}", true, true)]
    [InlineData("{\"requiresValidTo\":false,\"isTax\":true}", false, true)]
    [InlineData("{\"RequiresValidTo\":true}", true, false)]
    [InlineData("{\"requiresValidTo\":\"true\",\"isTax\":1}", false, false)]
    [InlineData("{\"otra\":5,\"isTax\":true}", false, true)]
    public void Parse_lee_las_marcas_y_ignora_lo_roto(string? json, bool requiresValidTo, bool isTax)
        => Assert.Equal(new ClientDocumentTypeMarks(requiresValidTo, isTax), ClientDocumentTypeMarks.Parse(json));

    [Fact]
    public void La_compania_pisa_al_global_solo_en_lo_que_trae()
    {
        const string global = "{\"requiresValidTo\":true,\"isTax\":true}";
        Assert.Equal(new ClientDocumentTypeMarks(true, true), ClientDocumentTypeMarks.Resolve(global, null));
        Assert.Equal(new ClientDocumentTypeMarks(false, true), ClientDocumentTypeMarks.Resolve(global, "{\"requiresValidTo\":false}"));
        Assert.Equal(new ClientDocumentTypeMarks(true, false), ClientDocumentTypeMarks.Resolve(global, "{\"isTax\":false,\"x\":1}"));
        Assert.Equal(new ClientDocumentTypeMarks(true, true), ClientDocumentTypeMarks.Resolve(global, "{roto"));
        Assert.Equal(new ClientDocumentTypeMarks(true, false), ClientDocumentTypeMarks.Resolve(null, "{\"requiresValidTo\":true}"));
    }

    [Fact]
    public void WriteTo_conserva_las_demas_llaves_y_reemplaza_las_marcas()
    {
        var json = new ClientDocumentTypeMarks(true, false).WriteTo("{\"color\":\"red\",\"isTax\":true,\"n\":3}");
        Assert.Equal(new ClientDocumentTypeMarks(true, false), ClientDocumentTypeMarks.Parse(json));
        Assert.Contains("\"color\":\"red\"", json);
        Assert.Contains("\"n\":3", json);
        Assert.Equal("{\"requiresValidTo\":false,\"isTax\":true}", new ClientDocumentTypeMarks(false, true).WriteTo("{roto"));
        Assert.Equal("{\"requiresValidTo\":false,\"isTax\":false}", ClientDocumentTypeMarks.Default.WriteTo(null));
    }
}
