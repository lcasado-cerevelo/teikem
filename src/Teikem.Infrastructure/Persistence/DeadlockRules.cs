using Microsoft.Data.SqlClient;

namespace Teikem.Infrastructure.Persistence;

/// <summary>
/// Lote 41 — red de seguridad para los interbloqueos de SQL Server (error 1205, "chosen as the deadlock victim").
/// La estrategia de reintentos de EF (EnableRetryOnFailure) ya repite la transacción completa varias veces; si aun así la
/// operación vuelve a perder, sale <c>RetryLimitExceededException</c> (o, en una transacción anidada sin reintentos, la
/// <c>SqlException</c> / <c>DbUpdateException</c> directa). Antes eso era un 500 'Error interno.'; ahora
/// ExceptionHandlingMiddleware responde 409 <see cref="Code"/> con un mensaje que invita a reintentar.
/// <para>La traducción vive en el middleware (no en RunInTransactionAsync) a propósito: IdempotencyMiddleware guarda las
/// respuestas de las excepciones de dominio, y un reintento con la misma Idempotency-Key repetiría el 409 para siempre; como
/// excepción no de dominio, la clave se libera y el reintento vuelve a ejecutar la operación.</para>
/// </summary>
public static class DeadlockRules
{
    /// <summary>Número de error de SQL Server de la víctima de un interbloqueo.</summary>
    public const int DeadlockErrorNumber = 1205;

    /// <summary>Código estable del ProblemDetails (409).</summary>
    public const string Code = "concurrent_edit";

    /// <summary>Mensaje en las rutas (/api/v1/trips…).</summary>
    public const string RouteMessage = "Otra persona está modificando esta ruta al mismo tiempo; vuelva a intentarlo.";

    /// <summary>Mensaje en el resto del API.</summary>
    public const string GenericMessage = "Otra persona está modificando estos datos al mismo tiempo; vuelva a intentarlo.";

    /// <summary>¿La excepción (o alguna interna: RetryLimitExceededException, DbUpdateException…) es un interbloqueo 1205?</summary>
    public static bool IsDeadlock(Exception? ex) => IsDeadlock(ex, SqlErrorNumbers);

    /// <summary>
    /// Igual, con el lector de números de error inyectado (SqlException no tiene constructor público: las pruebas usan uno
    /// propio). Recorre InnerException y las internas de AggregateException; tope de profundidad para no ciclar.
    /// </summary>
    public static bool IsDeadlock(Exception? ex, Func<Exception, IEnumerable<int>> errorNumbers)
    {
        ArgumentNullException.ThrowIfNull(errorNumbers);
        var pending = new Stack<(Exception Ex, int Depth)>();
        if (ex is not null) pending.Push((ex, 0));
        while (pending.Count > 0)
        {
            var (e, depth) = pending.Pop();
            if (errorNumbers(e).Contains(DeadlockErrorNumber)) return true;
            if (depth >= 16) continue;
            if (e is AggregateException agg)
                foreach (var inner in agg.InnerExceptions) pending.Push((inner, depth + 1));
            else if (e.InnerException is not null)
                pending.Push((e.InnerException, depth + 1));
        }
        return false;
    }

    /// <summary>Mensaje del 409 según la ruta del request: el de rutas en /api/v1/trips…, el genérico en lo demás.</summary>
    public static string MessageFor(string? requestPath)
        => requestPath is not null && requestPath.StartsWith("/api/v1/trips", StringComparison.OrdinalIgnoreCase)
            ? RouteMessage
            : GenericMessage;

    /// <summary>Números de error de una SqlException (todos sus SqlError y el principal); vacío para cualquier otra excepción.</summary>
    private static IEnumerable<int> SqlErrorNumbers(Exception e)
    {
        if (e is not SqlException sql) yield break;
        yield return sql.Number;
        foreach (SqlError err in sql.Errors) yield return err.Number;
    }
}
