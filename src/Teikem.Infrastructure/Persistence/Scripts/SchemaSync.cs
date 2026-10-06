using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Teikem.Infrastructure.Persistence.Scripts;

/// <summary>
/// Actualiza el esquema de una base YA CREADA a lo que dice el script de estructura (db-update, 2026-10-06). El script no se
/// puede volver a correr sobre una base existente (los CREATE TABLE no están protegidos), así que se aplica a una base temporal
/// vacía («sombra», en el mismo servidor), se compara con la real y se aplica solo lo que falta (SchemaDiffer), todo en una
/// transacción. La base sombra se borra siempre al terminar. Nunca borra tablas ni columnas.
/// </summary>
public sealed class SchemaSync(string connectionString, ILogger logger)
{
    /// <summary>Calcula qué falta en la base real (no la modifica; sí crea y borra la base sombra).</summary>
    public async Task<SchemaDiffResult> PlanAsync(string structureScriptPath, CancellationToken ct = default)
    {
        var csb = new SqlConnectionStringBuilder(connectionString);
        var realName = csb.InitialCatalog;
        csb.InitialCatalog = $"{realName}_shadow_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var shadowCs = csb.ConnectionString;
        var shadowRunner = new SqlScriptRunner(shadowCs, logger);
        try
        {
            logger.LogInformation("Creando la base temporal '{Db}' con el script de estructura actual ...", csb.InitialCatalog);
            await shadowRunner.EnsureDatabaseAsync(ct);
            await shadowRunner.ApplyAsync([new SqlScript("logistica-db-estructura.sql", structureScriptPath)], ct);
            var desired = await ReadAsync(shadowCs, ct);
            var actual = await ReadAsync(connectionString, ct);
            return SchemaDiffer.Diff(desired, actual);
        }
        finally
        {
            try { await shadowRunner.DropDatabaseAsync(CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "No se pudo borrar la base temporal '{Db}'; bórrela a mano.", csb.InitialCatalog); }
        }
    }

    /// <summary>Aplica los cambios en una sola transacción: si uno falla, no queda nada a medias.</summary>
    public async Task ApplyAsync(IReadOnlyList<SchemaChange> changes, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var change in changes)
            {
                logger.LogInformation("{Description}", change.Description);
                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandTimeout = 600;
                    cmd.CommandText = change.Sql;
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (SqlException ex)
                {
                    throw new InvalidOperationException($"Falló «{change.Description}»: {ex.Message}\n--- SQL ---\n{change.Sql}", ex);
                }
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<SchemaModel> ReadAsync(string cs, CancellationToken ct)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(ct);
        return await SchemaReader.ReadAsync(conn, ct);
    }
}
