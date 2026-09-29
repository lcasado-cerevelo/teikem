namespace Teikem.Infrastructure.Persistence.Scripts;

/// <summary>Resultado de la validación de db-reset: Ok = proceder; si no, el código de salida y el mensaje exacto.</summary>
public sealed record DbResetDecision(bool Ok, int ExitCode, string? Message);

/// <summary>
/// Lote 10 (P3, pedido de Luis 2026-09-29): reglas puras del verbo <c>dotnet run --project src/Teikem.Api -- db-reset --yes [--allow-remote]</c>,
/// que borra la base de <c>ConnectionStrings:Teikem</c> y la vuelve a inicializar como db-init sobre servidor limpio.
/// Seguros: confirmación explícita (--yes), rechazo de servidores no locales sin --allow-remote y nunca la base del WMS heredado (MSWM*).
/// </summary>
public static class DbResetRules
{
    public const string Verb = "db-reset";
    public const string YesFlag = "--yes";
    public const string AllowRemoteFlag = "--allow-remote";

    public const string Usage = "Uso: dotnet run --project src/Teikem.Api -- db-reset --yes [--allow-remote]";
    public const string ConfirmRequired = "db-reset borra la base de datos completa; confirme con --yes.";
    public const string RemoteRefused = "La cadena de conexión no apunta a un servidor local; use --allow-remote si de verdad quiere borrar esa base.";
    public const string LegacyRefused = "db-reset no toca la base del WMS heredado.";
    public const string ConnectionMissing = "Falta ConnectionStrings:Teikem o no indica la base de datos.";

    /// <summary>Prefijo de las bases del WMS heredado (MSWM, MSWM_20260928, …): jamás se borran desde aquí.</summary>
    public const string LegacyPrefix = "MSWM";

    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitRefused = 2;

    private static readonly string[] LocalHosts = { "localhost", "127.0.0.1", "(local)", ".", "::1", "[::1]" };

    /// <summary>
    /// ¿El Data Source apunta al servidor local? Acepta 'localhost', 'localhost,1433', 'tcp:localhost\INSTANCIA', '127.0.0.1',
    /// '(local)', '.', '::1', '[::1]' y '(localdb)\…' (con o sin puerto/instancia), sin distinguir mayúsculas.
    /// </summary>
    public static bool IsLocalServer(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource)) return false;
        var host = dataSource.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        if (host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)) return true;
        var cut = host.IndexOfAny(new[] { ',', '\\' });
        if (cut >= 0) host = host[..cut];
        host = host.Trim();
        return LocalHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>¿La base pertenece al WMS heredado?</summary>
    public static bool IsLegacyDatabase(string? databaseName)
        => databaseName is not null && databaseName.Trim().StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Decide si se puede borrar y reinicializar: sintaxis (solo --yes y --allow-remote después del verbo), confirmación,
    /// base del WMS heredado y servidor local. El orden de las comprobaciones es el de los mensajes: primero lo que el
    /// operador puede corregir en la línea de comandos, después lo que protege la base.
    /// </summary>
    public static DbResetDecision Decide(IReadOnlyList<string> args, string? dataSource, string? databaseName)
    {
        var start = -1;
        for (var i = 0; i < args.Count; i++)
            if (string.Equals(args[i], Verb, StringComparison.OrdinalIgnoreCase)) { start = i; break; }
        if (start < 0) return new(false, ExitRefused, Usage);

        var yes = false;
        var allowRemote = false;
        foreach (var a in args.Skip(start + 1))
        {
            if (string.Equals(a, YesFlag, StringComparison.OrdinalIgnoreCase)) { yes = true; continue; }
            if (string.Equals(a, AllowRemoteFlag, StringComparison.OrdinalIgnoreCase)) { allowRemote = true; continue; }
            return new(false, ExitRefused, Usage);
        }
        if (!yes) return new(false, ExitRefused, ConfirmRequired);
        if (string.IsNullOrWhiteSpace(dataSource) || string.IsNullOrWhiteSpace(databaseName)) return new(false, ExitFailed, ConnectionMissing);
        if (IsLegacyDatabase(databaseName)) return new(false, ExitRefused, LegacyRefused);
        if (!IsLocalServer(dataSource) && !allowRemote) return new(false, ExitRefused, RemoteRefused);
        return new(true, ExitOk, null);
    }
}
