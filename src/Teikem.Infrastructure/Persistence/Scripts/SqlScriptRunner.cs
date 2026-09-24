using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Teikem.Infrastructure.Persistence.Scripts;

public sealed record SqlScript(string Name, string Path);

/// <summary>
/// Ejecuta scripts T-SQL (con separadores GO) de forma idempotente, registrando (nombre, hash) en dbo.__SchemaVersion.
/// Un script se vuelve a aplicar solo si su contenido cambió (los de Diseño/ son MERGE/guarded y toleran re-ejecución;
/// el de estructura se aplica una única vez sobre BD limpia).
/// </summary>
public sealed partial class SqlScriptRunner(string connectionString, ILogger logger)
{
    [GeneratedRegex(@"^\s*GO(?:\s+\d+)?\s*(?:--.*)?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GoSeparator();

    public async Task EnsureDatabaseAsync(CancellationToken ct = default)
    {
        var csb = new SqlConnectionStringBuilder(connectionString);
        var dbName = csb.InitialCatalog;
        if (string.IsNullOrWhiteSpace(dbName)) throw new InvalidOperationException("La cadena de conexión no indica Initial Catalog/Database.");
        csb.InitialCatalog = "master";
        await using var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID(@n) IS NULL BEGIN DECLARE @sql NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@n); EXEC(@sql); END";
        cmd.Parameters.AddWithValue("@n", dbName);
        await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Base de datos '{Db}' disponible.", dbName);
    }

    public async Task<IReadOnlyList<string>> ApplyAsync(IEnumerable<SqlScript> scripts, CancellationToken ct = default)
    {
        var applied = new List<string>();
        await using var conn = new SqlConnection(connectionString);
        conn.InfoMessage += (_, e) => logger.LogInformation("SQL: {Message}", e.Message);
        await conn.OpenAsync(ct);
        await ExecAsync(conn, null, """
            IF OBJECT_ID('dbo.__SchemaVersion') IS NULL
            CREATE TABLE dbo.__SchemaVersion (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                ScriptName NVARCHAR(200) NOT NULL,
                Sha256 CHAR(64) NOT NULL,
                AppliedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                CONSTRAINT UQ___SchemaVersion UNIQUE (ScriptName, Sha256));
            """, ct);

        foreach (var script in scripts)
        {
            if (!File.Exists(script.Path)) throw new FileNotFoundException($"Script SQL no encontrado: {script.Path}");
            var sql = await File.ReadAllTextAsync(script.Path, ct);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();

            await using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(1) FROM dbo.__SchemaVersion WHERE ScriptName=@n AND Sha256=@h";
                check.Parameters.AddWithValue("@n", script.Name);
                check.Parameters.AddWithValue("@h", hash);
                if ((int)(await check.ExecuteScalarAsync(ct))! > 0)
                {
                    logger.LogInformation("Script {Name} ya aplicado (hash {Hash}); se omite.", script.Name, hash[..8]);
                    continue;
                }
            }

            logger.LogInformation("Aplicando {Name} ...", script.Name);
            var batches = GoSeparator().Split(sql).Select(b => b.Trim()).Where(b => b.Length > 0 && !IsOnlyComments(b)).ToList();
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                var i = 0;
                foreach (var batch in batches)
                {
                    i++;
                    try { await ExecAsync(conn, tx, batch, ct); }
                    catch (SqlException ex)
                    {
                        throw new InvalidOperationException($"Error en {script.Name}, lote #{i}: {ex.Message}\n--- lote ---\n{Truncate(batch)}", ex);
                    }
                }
                await using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO dbo.__SchemaVersion (ScriptName, Sha256) VALUES (@n, @h)";
                    ins.Parameters.AddWithValue("@n", script.Name);
                    ins.Parameters.AddWithValue("@h", hash);
                    await ins.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                applied.Add(script.Name);
                logger.LogInformation("Script {Name} aplicado ({Batches} lotes).", script.Name, batches.Count);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        return applied;
    }

    private static bool IsOnlyComments(string batch)
    {
        var stripped = Regex.Replace(batch, @"/\*.*?\*/", "", RegexOptions.Singleline);
        stripped = Regex.Replace(stripped, @"--.*$", "", RegexOptions.Multiline);
        return string.IsNullOrWhiteSpace(stripped);
    }

    private static string Truncate(string s) => s.Length > 600 ? s[..600] + " ..." : s;

    private static async Task ExecAsync(SqlConnection conn, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandTimeout = 600;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
