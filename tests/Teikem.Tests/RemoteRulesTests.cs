using Teikem.Domain.Integrations;
using Xunit;

namespace Teikem.Tests;

// Lote 134 (P11): conectores SFTP/FTP para cualquier cliente — validación, huella del servidor, qué archivos se toman y rutas remotas seguras.
public sealed class RemoteRulesTests
{
    private const string Fp = "nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8";   // SHA-256 en base64 (43 caracteres)
    private static RemoteConfig Cfg() => new("sftp.axis.com", 22, "teikem", "/entrada", HostFingerprint: "SHA256:" + Fp);
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private static RemoteFileInfo F(string name, int ageSeconds = 600, long size = 100) => new(name, size, Now.AddSeconds(-ageSeconds));

    [Fact]
    public void A_valid_sftp_configuration_has_no_errors_and_a_key_only_works_with_sftp()
    {
        Assert.Empty(RemoteRules.Validate(Cfg(), IntegrationConnectorKinds.Sftp, hasSecret: true));
        Assert.Empty(RemoteRules.Validate(Cfg() with { AuthKind = RemoteAuthKinds.Key }, IntegrationConnectorKinds.Sftp, true));
        Assert.Equal(RemoteRules.KeyOnlySftpMessage, RemoteRules.Validate(Cfg() with { AuthKind = RemoteAuthKinds.Key }, IntegrationConnectorKinds.Ftp, true)["remote.authKind"]);
    }

    [Fact]
    public void The_configuration_is_validated_with_exact_messages()
    {
        var bad = RemoteRules.Validate(new RemoteConfig("", 0, "", "", "../fuera", "a\nb", "RARA", "no-es-huella", true, new EmailTextMatch("RARO", "x")), IntegrationConnectorKinds.Sftp, hasSecret: false);
        Assert.Equal(RemoteRules.HostRequiredMessage, bad["remote.host"]);
        Assert.Equal(RemoteRules.PortMessage, bad["remote.port"]);
        Assert.Equal(RemoteRules.UsernameRequiredMessage, bad["remote.username"]);
        Assert.Equal(RemoteRules.UnknownAuthMessage("RARA"), bad["remote.authKind"]);
        Assert.Equal(RemoteRules.DirRequiredMessage, bad["remote.dir"]);
        Assert.Equal(RemoteRules.DirInvalidMessage, bad["remote.processedDir"]);
        Assert.Equal(RemoteRules.DirInvalidMessage, bad["remote.errorDir"]);
        Assert.Equal(RemoteRules.FingerprintFormatMessage, bad["remote.hostFingerprint"]);
        Assert.Equal(EmailRules.UnknownOperatorMessage("RARO"), bad["remote.fileName"]);
        Assert.Equal(RemoteRules.SecretRequiredMessage(RemoteAuthKinds.Password), RemoteRules.Validate(Cfg(), IntegrationConnectorKinds.Sftp, false)["remote.secret"]);
        Assert.Equal(RemoteRules.SecretRequiredMessage(RemoteAuthKinds.Key), RemoteRules.Validate(Cfg() with { AuthKind = RemoteAuthKinds.Key }, IntegrationConnectorKinds.Sftp, false)["remote.secret"]);
        Assert.Equal(RemoteRules.HostRequiredMessage, RemoteRules.Validate(null, IntegrationConnectorKinds.Ftp, true)["remote"]);
    }

    [Theory]
    [InlineData("SHA256:" + Fp, Fp)]
    [InlineData("sha256:" + Fp + "=", Fp)]
    [InlineData(" " + Fp + " ", Fp)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_fingerprint_is_normalized(string? input, string? expected) => Assert.Equal(expected, RemoteRules.NormalizeFingerprint(input));

    [Fact]
    public void The_fingerprint_must_match_what_the_server_presents()
    {
        Assert.True(RemoteRules.FingerprintMatches("SHA256:" + Fp, Fp + "="));
        Assert.False(RemoteRules.FingerprintMatches("SHA256:" + Fp, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
        Assert.False(RemoteRules.FingerprintMatches(null, Fp));    // sin huella fijada nunca se confía
        Assert.False(RemoteRules.FingerprintMatches("", Fp));
    }

    [Fact]
    public void Files_are_chosen_by_type_and_name_and_the_ones_still_being_written_wait()
    {
        var files = new[] { F("b.csv"), F("a.CSV"), F("c.pdf"), F(".oculto.csv"), F("reciente.csv", ageSeconds: 3), F("ordenes-1.txt"), F("ordenes-2.csv") };
        Assert.Equal(new[] { "a.CSV", "b.csv", "ordenes-1.txt", "ordenes-2.csv" }, RemoteRules.SelectFiles(files, Cfg(), Now).Select(f => f.Name));   // sin pdf, sin ocultos y sin el reciente
        var byName = Cfg() with { FileName = new EmailTextMatch("WILDCARD", "ordenes-*") };
        Assert.Equal(new[] { "ordenes-1.txt", "ordenes-2.csv" }, RemoteRules.SelectFiles(files, byName, Now).Select(f => f.Name));
        Assert.Equal(new[] { "c.pdf" }, RemoteRules.SelectFiles(files, Cfg() with { FileTypes = new[] { "pdf" } }, Now).Select(f => f.Name));
        Assert.Equal(new[] { "reciente.csv" }, RemoteRules.SelectFiles(files, Cfg(), Now.AddMinutes(5)).Select(f => f.Name).Where(n => n == "reciente.csv"));   // pasado el tiempo, ya se toma
    }

    [Fact]
    public void At_most_a_hundred_files_per_run_in_name_order()
    {
        var many = Enumerable.Range(0, 130).Select(i => F($"f{i:D3}.csv")).ToList();
        var chosen = RemoteRules.SelectFiles(many, Cfg(), Now);
        Assert.Equal(RemoteRules.MaxFilesPerRun, chosen.Count);
        Assert.Equal("f000.csv", chosen[0].Name);
        Assert.Equal("f099.csv", chosen[^1].Name);
    }

    [Theory]
    [InlineData("/", "a.csv", "/a.csv")]
    [InlineData("", "a.csv", "/a.csv")]
    [InlineData("/entrada", "a.csv", "/entrada/a.csv")]
    [InlineData("/entrada/", "/a.csv", "/entrada/a.csv")]
    public void Remote_paths_are_joined_without_double_slashes(string dir, string name, string expected) => Assert.Equal(expected, RemoteRules.Combine(dir, name));

    [Fact]
    public void The_connector_kinds_include_sftp_and_ftp()
    {
        Assert.True(IntegrationConnectorKinds.IsRemote(IntegrationConnectorKinds.Sftp));
        Assert.True(IntegrationConnectorKinds.IsRemote(IntegrationConnectorKinds.Ftp));
        Assert.False(IntegrationConnectorKinds.IsRemote(IntegrationConnectorKinds.Email));
        Assert.Contains(IntegrationConnectorKinds.Ftp, IntegrationConnectorKinds.Available);
        Assert.Contains(IntegrationConnectorKinds.Sftp, IntegrationConnectorKinds.Available);
    }
}
