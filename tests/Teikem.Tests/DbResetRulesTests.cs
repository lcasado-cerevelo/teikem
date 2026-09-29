using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 10 (P3) — reglas puras de db-reset: confirmación con --yes, servidor local o --allow-remote, nunca la base del WMS
/// heredado (MSWM*), sintaxis y códigos de salida (0 proceder, 1 falla de configuración, 2 rechazo).
/// </summary>
public class DbResetRulesTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost,1433")]
    [InlineData("LOCALHOST\\SQLEXPRESS")]
    [InlineData("tcp:localhost,1433")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1,14330")]
    [InlineData("(local)")]
    [InlineData(".")]
    [InlineData(".\\SQLEXPRESS")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("(localdb)\\MSSQLLocalDB")]
    public void Local_servers_are_recognized(string dataSource) => Assert.True(DbResetRules.IsLocalServer(dataSource));

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("sqlserver,1433")]
    [InlineData("teikem-prod.database.windows.net")]
    [InlineData("192.168.1.20")]
    [InlineData("")]
    [InlineData(null)]
    public void Remote_or_missing_servers_are_not_local(string? dataSource) => Assert.False(DbResetRules.IsLocalServer(dataSource));

    [Fact]
    public void Without_yes_it_refuses_with_the_exact_message_and_exit_2()
    {
        var d = DbResetRules.Decide(new[] { "db-reset" }, "localhost,1433", "Teikem");
        Assert.False(d.Ok);
        Assert.Equal(DbResetRules.ExitRefused, d.ExitCode);
        Assert.Equal("db-reset borra la base de datos completa; confirme con --yes.", d.Message);
    }

    [Fact]
    public void With_yes_on_a_local_server_it_proceeds()
    {
        var d = DbResetRules.Decide(new[] { "db-reset", "--yes" }, "localhost,1433", "Teikem");
        Assert.True(d.Ok);
        Assert.Equal(DbResetRules.ExitOk, d.ExitCode);
        Assert.Null(d.Message);
    }

    [Fact]
    public void Remote_server_needs_allow_remote()
    {
        var refused = DbResetRules.Decide(new[] { "db-reset", "--yes" }, "sqlserver,1433", "Teikem");
        Assert.False(refused.Ok);
        Assert.Equal(DbResetRules.ExitRefused, refused.ExitCode);
        Assert.Equal("La cadena de conexión no apunta a un servidor local; use --allow-remote si de verdad quiere borrar esa base.", refused.Message);

        var allowed = DbResetRules.Decide(new[] { "db-reset", "--yes", "--allow-remote" }, "sqlserver,1433", "Teikem");
        Assert.True(allowed.Ok);
    }

    [Theory]
    [InlineData("MSWM")]
    [InlineData("mswm")]
    [InlineData("MSWM_20260928")]
    public void The_legacy_wms_database_is_never_dropped(string database)
    {
        var d = DbResetRules.Decide(new[] { "db-reset", "--yes", "--allow-remote" }, "localhost", database);
        Assert.False(d.Ok);
        Assert.Equal(DbResetRules.ExitRefused, d.ExitCode);
        Assert.Equal("db-reset no toca la base del WMS heredado.", d.Message);
    }

    [Theory]
    [InlineData("db-reset --yes --force")]
    [InlineData("db-reset Teikem --yes")]
    [InlineData("db-init")]
    public void Anything_else_is_a_usage_error(string commandLine)
    {
        var d = DbResetRules.Decide(commandLine.Split(' '), "localhost", "Teikem");
        Assert.False(d.Ok);
        Assert.Equal(DbResetRules.ExitRefused, d.ExitCode);
        Assert.Equal("Uso: dotnet run --project src/Teikem.Api -- db-reset --yes [--allow-remote]", d.Message);
    }

    [Fact]
    public void Missing_connection_string_is_a_configuration_failure()
    {
        var d = DbResetRules.Decide(new[] { "db-reset", "--yes" }, null, null);
        Assert.False(d.Ok);
        Assert.Equal(DbResetRules.ExitFailed, d.ExitCode);
        Assert.Equal("Falta ConnectionStrings:Teikem o no indica la base de datos.", d.Message);
    }

    [Fact]
    public void Flags_are_case_insensitive_and_order_free()
    {
        Assert.True(DbResetRules.Decide(new[] { "DB-RESET", "--ALLOW-REMOTE", "--YES" }, "sqlserver", "Teikem").Ok);
    }
}
