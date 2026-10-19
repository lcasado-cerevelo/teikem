using Teikem.Infrastructure.Abstractions;
using Xunit;

namespace Teikem.Tests;

/// <summary>Redirección de todo el correo saliente en test (2026-10-19): nadie real recibe; todo va al buzón de Cerevelo con el destinatario original.</summary>
public class EmailRedirectRulesTests
{
    private static EmailRedirectOptions Opts(bool plus = true, params string[] except) => new() { RedirectTo = "teikem@cerevelo.com", PlusAddressing = plus, ExceptDomains = except, Label = "TEST" };

    [Fact]
    public void SinRedirectTo_ElCorreoSaleTalCual()
    {
        var r = EmailRedirectRules.Apply(new EmailRedirectOptions(), "cliente@suempresa.com", "Cliente", "Su orden", "<p>hola</p>");
        Assert.False(r.Redirected);
        Assert.Equal("cliente@suempresa.com", r.ToEmail);
        Assert.Equal("Su orden", r.Subject);
        Assert.Equal("<p>hola</p>", r.HtmlContent);
        Assert.False(EmailRedirectRules.IsEnabled(null));
        Assert.False(EmailRedirectRules.IsEnabled(new EmailRedirectOptions { RedirectTo = "sin-arroba" }));
    }

    [Fact]
    public void ConRedirectTo_VaAlBuzonConDireccionMasYDestinatarioOriginalEnAsuntoYCuerpo()
    {
        var r = EmailRedirectRules.Apply(Opts(), "cliente@suempresa.com", "Cliente Uno", "Su orden fue entregada", "<p>hola</p>");
        Assert.True(r.Redirected);
        Assert.Equal("teikem+cliente_suempresa.com@cerevelo.com", r.ToEmail);
        Assert.Equal("[TEST → cliente@suempresa.com] Su orden fue entregada", r.Subject);
        Assert.StartsWith("<div", r.HtmlContent);
        Assert.Contains("Cliente Uno &lt;cliente@suempresa.com&gt;", r.HtmlContent);
        Assert.EndsWith("<p>hola</p>", r.HtmlContent);
        Assert.Equal("Cliente Uno", r.ToName);
    }

    [Fact]
    public void SinDireccionMas_TodoAlBuzonTalCual()
    {
        var r = EmailRedirectRules.Apply(Opts(plus: false), "consignatario@gmail.com", null, "Aviso", "x");
        Assert.Equal("teikem@cerevelo.com", r.ToEmail);
        Assert.Contains("consignatario@gmail.com", r.HtmlContent);
    }

    [Fact]
    public void DominiosExceptuados_RecibenDeVerdad_SinDistinguirMayusculas()
    {
        var r = EmailRedirectRules.Apply(Opts(true, "Cerevelo.com", "@advancelogisticspr.com"), "luis@CEREVELO.com", null, "Código MFA", "123456");
        Assert.False(r.Redirected);
        Assert.Equal("luis@CEREVELO.com", r.ToEmail);
        Assert.Equal("Código MFA", r.Subject);
        var otro = EmailRedirectRules.Apply(Opts(true, "cerevelo.com"), "alguien@otro.com", null, "a", "b");
        Assert.True(otro.Redirected);
    }

    [Fact]
    public void SinEtiqueta_UsaTest_YConEtiquetaLaPoneEnMayusculas()
    {
        var sin = EmailRedirectRules.Apply(new EmailRedirectOptions { RedirectTo = "qa@cerevelo.com", PlusAddressing = false }, "a@b.com", null, "s", "h");
        Assert.StartsWith("[TEST → a@b.com]", sin.Subject);
        var con = EmailRedirectRules.Apply(new EmailRedirectOptions { RedirectTo = "qa@cerevelo.com", PlusAddressing = false, Label = "staging" }, "a@b.com", null, "s", "h");
        Assert.StartsWith("[STAGING → a@b.com]", con.Subject);
    }
}
