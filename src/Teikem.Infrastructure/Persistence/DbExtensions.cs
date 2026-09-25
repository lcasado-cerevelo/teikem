using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Persistence;

/// <summary>
/// Escrituras compuestas y guardas de persistencia (Lote 2):
/// - RunInTransactionAsync: transacción explícita bajo la estrategia de reintentos (EnableRetryOnFailure rechaza
///   transacciones de usuario fuera de ella). Regla: todo lo que se carga se carga dentro del delegado, porque al
///   reintentar se limpia el ChangeTracker.
/// - SaveGuardedAsync: traduce DbUpdateConcurrencyException y violaciones de índice único (SqlException 2601/2627) a 409.
/// - ApplyRowVersion: control de concurrencia optimista opcional en PATCH (409 si el registro cambió).
/// </summary>
public static class DbExtensions
{
    public const string ConcurrencyMessage = "El registro fue modificado por otro usuario; recargue e intente de nuevo.";

    public static Task<T> RunInTransactionAsync<T>(this TeikemDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        // Ya hay una transacción abierta (llamada anidada): se une a ella; el dueño externo confirma o revierte.
        if (db.Database.CurrentTransaction is not null) return work(ct);

        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async ct2 =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct2);
            var result = await work(ct2);
            await tx.CommitAsync(ct2);
            return result;
        }, ct);
    }

    public static async Task RunInTransactionAsync(this TeikemDbContext db, Func<CancellationToken, Task> work, CancellationToken ct)
        => await db.RunInTransactionAsync<object?>(async ct2 => { await work(ct2); return null; }, ct);

    /// <summary>SaveChanges con traducción a 409: concurrencia (RowVersion) o violación de índice único.</summary>
    public static async Task<int> SaveGuardedAsync(this TeikemDbContext db, string conflictMessage, CancellationToken ct)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ConcurrencyMessage);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictException(conflictMessage);
        }
    }

    /// <summary>¿La excepción viene de un índice único / restricción UNIQUE de SQL Server (2601 índice único, 2627 constraint)?</summary>
    public static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql)
            {
                foreach (SqlError err in sql.Errors)
                    if (err.Number is 2601 or 2627) return true;
                return sql.Number is 2601 or 2627;
            }
        }
        return false;
    }

    /// <summary>
    /// Fija el valor original de RowVersion con el que el cliente leyó el registro (base64 de la ficha). Si el PATCH no lo
    /// trae, no hay control de concurrencia. Si difiere del actual, SaveGuardedAsync responde 409.
    /// </summary>
    public static void ApplyRowVersion<TEntity>(this TeikemDbContext db, TEntity entity, string? rowVersionBase64) where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(rowVersionBase64)) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(rowVersionBase64.Trim()); }
        catch (FormatException) { throw new ValidationException("rowVersion", "rowVersion inválido: se espera el valor base64 devuelto por la ficha."); }
        if (bytes.Length == 0) return;

        var entry = db.Entry(entity);
        if (entry.Metadata.FindProperty("RowVersion") is null)
            throw new InvalidOperationException($"La entidad {typeof(TEntity).Name} no tiene RowVersion.");
        entry.Property("RowVersion").OriginalValue = bytes;
    }
}
