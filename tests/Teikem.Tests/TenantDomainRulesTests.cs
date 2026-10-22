using Teikem.Domain.Tenancy;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-22 — dominio y subdominio de la compañía para el logo de la pantalla de entrada.</summary>
public class TenantDomainRulesTests
{
    [Theory]
    [InlineData("Logistica.MiEmpresa.com", "logistica.miempresa.com")]
    [InlineData("https://logistica.miempresa.com/login?x=1", "logistica.miempresa.com")]
    [InlineData("  miempresa.com:8443  ", "miempresa.com")]
    [InlineData("miempresa.com.", "miempresa.com")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void El_dominio_se_normaliza(string? input, string expected) => Assert.Equal(expected, TenantDomainRules.NormalizeDomain(input));

    [Theory]
    [InlineData("localhost")]
    [InlineData("mi empresa.com")]
    [InlineData("-malo.com")]
    [InlineData("a..com")]
    public void Un_dominio_invalido_da_null(string input) => Assert.Null(TenantDomainRules.NormalizeDomain(input));

    [Theory]
    [InlineData("Depot", "depot")]
    [InlineData("depot-2", "depot-2")]
    [InlineData("", "")]
    public void El_subdominio_se_normaliza(string input, string expected) => Assert.Equal(expected, TenantDomainRules.NormalizeSubdomain(input));

    [Theory]
    [InlineData("depot.teikem.com")]
    [InlineData("depot_x")]
    [InlineData("-depot")]
    public void Un_subdominio_invalido_da_null(string input) => Assert.Null(TenantDomainRules.NormalizeSubdomain(input));

    [Theory]
    [InlineData("depot.teikem.com", "depot.teikem.com", "depot")]
    [InlineData("www.miempresa.com", "miempresa.com", null)]
    [InlineData("www.depot.teikem.com", "depot.teikem.com", "depot")]
    [InlineData("Staging.AdvanceLogisticsPR.com:443", "staging.advancelogisticspr.com", "staging")]
    [InlineData("miempresa.com", "miempresa.com", null)]
    public void Se_buscan_el_dominio_completo_y_el_subdominio(string host, string domain, string? sub)
        => Assert.Equal((domain, sub), TenantDomainRules.Candidates(host));

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData(null)]
    public void Localhost_y_las_IP_no_buscan_nada(string? host) => Assert.Equal((null, null), TenantDomainRules.Candidates(host));
}
