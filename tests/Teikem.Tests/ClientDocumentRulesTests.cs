using Teikem.Domain.Clients;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 248 — reglas puras de los documentos del cliente con vigencia: validación de número y fechas, días que faltan, estado derivado y «renovado».</summary>
public sealed class ClientDocumentRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);
    private static readonly ClientDocumentTypeMarks Dated = new(true, true);
    private static readonly ClientDocumentTypeMarks Free = ClientDocumentTypeMarks.Default;

    [Fact]
    public void Mensajes_exactos()
    {
        Assert.Equal("El tipo de documento no existe o está apagado en Catálogos de valores.", ClientDocumentRules.TypeInvalid);
        Assert.Equal("El número del documento no puede pasar de 60 caracteres.", ClientDocumentRules.NumberTooLong);
        Assert.Equal("Este tipo de documento lleva vigencia: escriba la fecha «hasta».", ClientDocumentRules.ValidToRequired);
        Assert.Equal("La fecha «hasta» no puede ser anterior a la fecha «desde».", ClientDocumentRules.DatesOrder);
        Assert.Equal("Este documento respalda una exención de IVU vigente: dé de baja la exención en Impuestos antes de quitarlo.", ClientDocumentRules.BacksExemptionCannotRemove);
        Assert.Equal("Este documento respalda una exención de IVU: cambiar su número o su vigencia exige el permiso de impuestos (tax.manage).", ClientDocumentRules.BacksExemptionNeedsTax);
        Assert.Equal("Este documento respalda una exención de IVU: no se le puede quitar la fecha «hasta».", ClientDocumentRules.BacksExemptionNeedsValidTo);
    }

    [Fact]
    public void ValidateMetadata_por_campo()
    {
        Assert.Equal((null, null), ClientDocumentRules.ValidateMetadata(null, null, null, Free));
        Assert.Equal((null, null), ClientDocumentRules.ValidateMetadata("  ", null, null, Free));
        Assert.Equal((null, null), ClientDocumentRules.ValidateMetadata(new string('9', 60), null, null, Free));
        Assert.Equal(("documentNumber", ClientDocumentRules.NumberTooLong), ClientDocumentRules.ValidateMetadata(new string('9', 61), null, null, Free));
        Assert.Equal((null, null), ClientDocumentRules.ValidateMetadata("  " + new string('9', 60) + "  ", null, null, Free));
        Assert.Equal(("validTo", ClientDocumentRules.ValidToRequired), ClientDocumentRules.ValidateMetadata("C-1", Today, null, Dated));
        Assert.Equal(("validTo", ClientDocumentRules.DatesOrder), ClientDocumentRules.ValidateMetadata("C-1", Today, Today.AddDays(-1), Free));
        Assert.Equal((null, null), ClientDocumentRules.ValidateMetadata("C-1", Today, Today, Dated));
        // Un «hasta» pasado se acepta: es el registro de un documento real.
        Assert.Equal((null, null), ClientDocumentRules.ValidateMetadata("C-1", null, Today.AddYears(-2), Dated));
        Assert.Null(ClientDocumentRules.NormalizeNumber("   "));
        Assert.Equal("A-1", ClientDocumentRules.NormalizeNumber(" A-1 "));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(-3, -3)]
    public void DaysToExpiry(int? offset, int? expected)
        => Assert.Equal(expected, ClientDocumentRules.DaysToExpiry(offset is int o ? Today.AddDays(o) : null, Today));

    [Theory]
    // offset del «hasta» (null = sin «hasta»), días de aviso, renovado, lleva vigencia → estado
    [InlineData(null, 30, false, false, ClientDocumentExpiryStates.NoExpiry)]
    [InlineData(null, 30, false, true, ClientDocumentExpiryStates.MissingValidTo)]
    [InlineData(null, 30, true, true, ClientDocumentExpiryStates.Renewed)]
    [InlineData(31, 30, false, true, ClientDocumentExpiryStates.Valid)]
    [InlineData(30, 30, false, true, ClientDocumentExpiryStates.Expiring)]
    [InlineData(0, 30, false, true, ClientDocumentExpiryStates.Expiring)]
    [InlineData(-1, 30, false, true, ClientDocumentExpiryStates.Expired)]
    [InlineData(-1, 30, true, true, ClientDocumentExpiryStates.Renewed)]
    [InlineData(0, 0, false, false, ClientDocumentExpiryStates.Expiring)]
    [InlineData(1, 0, false, false, ClientDocumentExpiryStates.Valid)]
    [InlineData(1, -5, false, false, ClientDocumentExpiryStates.Valid)]
    public void ExpiryState_por_tabla(int? offset, int warnDays, bool renewed, bool requiresValidTo, string expected)
        => Assert.Equal(expected, ClientDocumentRules.ExpiryState(offset is int o ? Today.AddDays(o) : null, Today, warnDays, renewed, requiresValidTo));

    private static ClientDocumentValidityKey K(int id, DateOnly? to, int client = 1, int type = 10) => new(id, client, type, to);

    [Fact]
    public void IsRenewed_con_hasta_posterior_o_igual_y_mas_nuevo()
    {
        var doc = K(5, Today);
        Assert.False(ClientDocumentRules.IsRenewed(doc, new[] { doc }));
        Assert.True(ClientDocumentRules.IsRenewed(doc, new[] { doc, K(3, Today.AddDays(1)) }));      // otro posterior, aunque se subió antes
        Assert.False(ClientDocumentRules.IsRenewed(doc, new[] { K(7, Today.AddDays(-1)) }));         // otro anterior
        Assert.True(ClientDocumentRules.IsRenewed(doc, new[] { K(7, Today) }));                     // mismo «hasta», subido después
        Assert.False(ClientDocumentRules.IsRenewed(doc, new[] { K(3, Today) }));                    // mismo «hasta», subido antes
        Assert.True(ClientDocumentRules.IsRenewed(doc, new[] { K(7, null) }));                      // sin «hasta», subido después
        Assert.False(ClientDocumentRules.IsRenewed(doc, new[] { K(3, null) }));                     // sin «hasta» y más viejo: no apaga al nuevo
        Assert.False(ClientDocumentRules.IsRenewed(doc, new[] { K(9, Today.AddYears(1), client: 2) }));   // otro cliente
        Assert.False(ClientDocumentRules.IsRenewed(doc, new[] { K(9, Today.AddYears(1), type: 11) }));    // otro tipo
    }

    [Fact]
    public void IsRenewed_sin_hasta_lo_reemplaza_cualquiera_mas_nuevo()
    {
        var doc = K(5, null);
        Assert.True(ClientDocumentRules.IsRenewed(doc, new[] { K(6, Today) }));
        Assert.True(ClientDocumentRules.IsRenewed(doc, new[] { K(6, null) }));
        Assert.False(ClientDocumentRules.IsRenewed(doc, new[] { K(4, Today.AddYears(1)) }));
    }
}
