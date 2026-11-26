using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Teikem.Domain.Tenancy;
using Xunit;
using R = Teikem.Domain.Sales.SalesChangeRules;

namespace Teikem.Tests;

/// <summary>Lote 249 (P2) — segunda aprobación y adjunto: el cliente manda sobre la compañía (NULL hereda); NONE, ALWAYS y ABOVE_AMOUNT (estrictamente mayor).</summary>
public sealed class SalesChangeApprovalRulesTests
{
    [Fact]
    public void Sin_ajustes_del_cliente_hereda_los_de_la_compania()
    {
        var s = R.EffectiveSettings("ABOVE_AMOUNT", 500m, true, null, null, null);
        Assert.Equal(new SalesChangeSettings("ABOVE_AMOUNT", 500m, true, false, false), s);
    }

    [Fact]
    public void El_cliente_manda_con_su_modo_su_monto_y_su_adjunto()
    {
        var s = R.EffectiveSettings("ABOVE_AMOUNT", 500m, false, "ALWAYS", null, true);
        Assert.Equal(new SalesChangeSettings("ALWAYS", null, true, true, true), s);

        var amount = R.EffectiveSettings("NONE", null, true, "ABOVE_AMOUNT", 50m, null);
        Assert.Equal(new SalesChangeSettings("ABOVE_AMOUNT", 50m, true, true, false), amount);

        // el cliente puede apagar lo que pide la compañía
        var off = R.EffectiveSettings("ALWAYS", null, true, "NONE", null, false);
        Assert.Equal(new SalesChangeSettings("NONE", null, false, true, true), off);
    }

    [Fact]
    public void Con_las_entidades_de_compania_y_cliente()
    {
        var tenant = new Tenant { SalesChangeApprovalMode = "ALWAYS", SalesChangeAttachmentRequired = false };
        Assert.Equal("ALWAYS", R.EffectiveSettings(tenant, null).ApprovalMode);
        var client = new Client { SalesChangeApprovalMode = null, SalesChangeAttachmentRequired = true };
        var s = R.EffectiveSettings(tenant, client);
        Assert.Equal(("ALWAYS", true), (s.ApprovalMode, s.AttachmentRequired));
        Assert.False(s.ApprovalFromClient);
        Assert.True(s.AttachmentFromClient);
        // por omisión: NONE y adjunto opcional
        var d = R.EffectiveSettings(new Tenant(), new Client());
        Assert.Equal(new SalesChangeSettings("NONE", null, false, false, false), d);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(100000.0)]
    [InlineData(-100000.0)]
    public void NONE_nunca_pide_y_ALWAYS_siempre(double delta)
    {
        Assert.False(R.RequiresApproval("NONE", null, (decimal)delta));
        Assert.True(R.RequiresApproval("ALWAYS", null, (decimal)delta));
        Assert.False(R.RequiresApproval(null, null, (decimal)delta));
    }

    [Fact]
    public void ABOVE_AMOUNT_es_estrictamente_mayor_y_usa_el_valor_absoluto()
    {
        Assert.False(R.RequiresApproval("ABOVE_AMOUNT", 100m, 100m));
        Assert.True(R.RequiresApproval("ABOVE_AMOUNT", 100m, 100.01m));
        Assert.False(R.RequiresApproval("ABOVE_AMOUNT", 100m, -100m));
        Assert.True(R.RequiresApproval("ABOVE_AMOUNT", 100m, -100.01m));
        Assert.False(R.RequiresApproval("ABOVE_AMOUNT", 100m, 0m));
    }

    [Fact]
    public void La_regla_que_pidio_la_aprobacion()
    {
        Assert.Equal("ABOVE_AMOUNT", R.ApprovalRule(new SalesChangeSettings("ABOVE_AMOUNT", 10m, false, false, false), 10.01m));
        Assert.Null(R.ApprovalRule(new SalesChangeSettings("ABOVE_AMOUNT", 10m, false, false, false), 10m));
        Assert.Equal("ALWAYS", R.ApprovalRule(new SalesChangeSettings("ALWAYS", null, false, false, false), 0m));
        Assert.Null(R.ApprovalRule(new SalesChangeSettings("NONE", null, false, false, false), 999m));
    }

    [Fact]
    public void Adjunto_obligatorio_por_compania_y_por_cliente()
    {
        Assert.True(R.EffectiveSettings("NONE", null, true, null, null, null).AttachmentRequired);
        Assert.True(R.EffectiveSettings("NONE", null, false, null, null, true).AttachmentRequired);
        Assert.False(R.EffectiveSettings("NONE", null, true, null, null, false).AttachmentRequired);
        Assert.False(R.EffectiveSettings("NONE", null, false, null, null, null).AttachmentRequired);
        Assert.Equal("Adjunte la evidencia de la autorización del cliente (captura del correo o del mensaje): es obligatoria para este cliente.", R.AttachmentRequired);
    }
}
