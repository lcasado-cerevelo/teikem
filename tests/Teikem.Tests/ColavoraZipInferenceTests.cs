using Teikem.Domain.Migration.Colavora;
using Xunit;

namespace Teikem.Tests;

// Migración de Colavora (2026-10-22): ZIP/zona de los envíos que llegan sin ZIP del consignatario (historial primero, municipio después).
public sealed class ColavoraZipInferenceTests
{
    private static readonly Dictionary<string, string> CityByZip = new()
    {
        ["00714"] = "ARROYO",
        ["00646"] = "DORADO",
        ["00786"] = "LA PLATA, AIBONITO",
        ["00956"] = "BAYAMON", ["00916"] = "BAYAMON",
        ["00983"] = "CAROLINA", ["00985"] = "CAROLINA",
        ["00901"] = "SAN JUAN", ["00907"] = "SAN JUAN",
    };

    private static readonly Dictionary<string, int> Zones = new()
    {
        ["00714"] = 818, ["00646"] = 801,
        ["00786"] = 815,
        ["00956"] = 810, ["00916"] = 823,     // Bayamón se reparte entre dos zonas
        ["00983"] = 808, ["00985"] = 808,     // Carolina, una sola zona
        ["00901"] = 823, ["00907"] = 823,
    };

    private static ColavoraZipInference.MunicipalityIndex Index() => new(CityByZip, Zones);

    private static readonly IReadOnlyCollection<string> None = Array.Empty<string>();

    [Fact]
    public void Fold_quita_acentos_signos_y_espacios()
        => Assert.Equal("URB BAYAMON 2", ColavoraZipInference.Fold("  Urb. Bayamón  #2 "));

    [Fact]
    public void Municipio_de_un_solo_zip_devuelve_zip_ciudad_y_zona()
    {
        var r = Index().Find("CLINICA ARROYO", "CALLE 1 #20", null);
        Assert.NotNull(r);
        Assert.Equal("00714", r!.Zip);
        Assert.Equal("ARROYO", r.City);
        Assert.Equal(818, r.ZoneId);
        Assert.Equal(ColavoraZipInference.SourceMunicipality, r.Source);
    }

    [Fact]
    public void Municipio_con_varios_zip_en_una_zona_da_la_zona_sin_zip()
    {
        var r = Index().Find("FARMACIA", "AVE PRINCIPAL", "URB BARALT CAROLINA");
        Assert.NotNull(r);
        Assert.Null(r!.Zip);
        Assert.Equal("CAROLINA", r.City);
        Assert.Equal(808, r.ZoneId);
    }

    [Fact]
    public void Municipio_repartido_entre_zonas_no_se_adivina()
        => Assert.Null(Index().Find("HOSPITAL", "CALLE 5", "BAYAMON"));

    [Fact]
    public void Dos_pueblos_de_zonas_distintas_en_el_texto_no_se_adivina()
        => Assert.Null(Index().Find("CLINICA DORADO", "CALLE ARROYO", "ARROYO"));

    [Fact]
    public void Nombre_de_calle_no_cuenta_como_pueblo()
        => Assert.Null(Index().Find("FARMACIA XYZ", "AVE CAROLINA #12", null));

    [Fact]
    public void El_pueblo_se_busca_por_palabra_completa()
        => Assert.Null(Index().Find("EL DORADOS CORP", "CALLE 1", null));

    [Fact]
    public void Pueblo_tras_la_coma_de_la_tabla_Zip_se_reconoce()
    {
        var r = Index().Find("ASILO", "BO LA PLATA, AIBONITO", null);
        Assert.NotNull(r);
        Assert.Equal("AIBONITO", r!.City);
        Assert.Equal(815, r.ZoneId);
    }

    [Fact]
    public void Historial_con_un_solo_zip_gana_sobre_el_municipio()
    {
        var r = ColavoraZipInference.Infer("CLINICA DORADO", "CALLE 1", null, new[] { "00714" }, None, Index(), Zones, CityByZip);
        Assert.NotNull(r);
        Assert.Equal("00714", r!.Zip);
        Assert.Equal(818, r.ZoneId);
        Assert.Equal(ColavoraZipInference.SourceHistory, r.Source);
    }

    [Fact]
    public void Historial_solo_por_nombre_se_usa_si_no_hay_por_nombre_y_direccion()
    {
        var r = ColavoraZipInference.Infer("X", "CALLE 1", null, None, new[] { "00646" }, Index(), Zones, CityByZip);
        Assert.Equal("00646", r!.Zip);
    }

    [Fact]
    public void Historial_con_varios_zip_pasa_al_municipio()
    {
        var r = ColavoraZipInference.Infer("CLINICA ARROYO", "CALLE 1", null, new[] { "00714", "00646" }, None, Index(), Zones, CityByZip);
        Assert.Equal(ColavoraZipInference.SourceMunicipality, r!.Source);
        Assert.Equal("00714", r.Zip);
    }

    [Fact]
    public void Historial_con_zip_sin_zona_pasa_al_municipio()
    {
        var r = ColavoraZipInference.Infer("CLINICA ARROYO", "CALLE 1", null, new[] { "00000" }, None, Index(), Zones, CityByZip);
        Assert.Equal(ColavoraZipInference.SourceMunicipality, r!.Source);
    }

    [Fact]
    public void Sin_historial_ni_municipio_queda_sin_zona()
        => Assert.Null(ColavoraZipInference.Infer("DR PEREZ", "URB LAS FLORES 5", null, None, None, Index(), Zones, CityByZip));
}
