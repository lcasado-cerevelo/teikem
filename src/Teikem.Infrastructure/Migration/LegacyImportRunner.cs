using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence.Scripts;

namespace Teikem.Infrastructure.Migration;

/// <summary>Argumentos ya interpretados del verbo import-legacy (Error != null = sintaxis incorrecta).</summary>
public sealed record LegacyImportArgs(string? ConfigPath, bool DryRun, string? Error, bool Update = false);

/// <summary>
/// Lote 10 (P0): verbo CLI <c>dotnet run --project src/Teikem.Api -- import-legacy &lt;config.json&gt; [--dry-run]</c>.
/// Carga la configuración, ejecuta <see cref="LegacyImportService"/> en un scope propio, escribe el reporte (.md + .csv) y lo
/// resume por consola. Códigos de salida: 0 sin rechazos graves; 1 con rechazos graves (conciliación con diferencias, error
/// de configuración o falla de lectura de las fuentes); 2 si la sintaxis es incorrecta.
/// </summary>
public sealed class LegacyImportRunner(IServiceScopeFactory scopes, ILogger<LegacyImportRunner> logger)
{
    public const string Verb = "import-legacy";
    public const string DryRunFlag = "--dry-run";
    /// <summary>Modo --update (pedido de Luis, 2026-09-29): las filas existentes se actualizan desde QuickBooks; el saldo inicial no se toca.</summary>
    public const string UpdateFlag = "--update";
    public const string Usage = "Uso: dotnet run --project src/Teikem.Api -- import-legacy <config.json> [--dry-run] [--update]";

    public const int ExitOk = 0;
    public const int ExitSevere = 1;
    public const int ExitUsage = 2;

    /// <summary>
    /// Interpreta los argumentos de la línea de comandos: exactamente una ruta después del verbo y, opcionalmente, --dry-run
    /// (en cualquier posición). Cualquier otra opción o una segunda ruta es un error de sintaxis.
    /// </summary>
    public static LegacyImportArgs ParseArgs(IReadOnlyList<string> args)
    {
        var start = -1;
        for (var i = 0; i < args.Count; i++)
            if (string.Equals(args[i], Verb, StringComparison.OrdinalIgnoreCase)) { start = i; break; }
        if (start < 0) return new(null, false, Usage);

        string? path = null;
        var dryRun = false;
        var update = false;
        foreach (var a in args.Skip(start + 1))
        {
            if (string.Equals(a, DryRunFlag, StringComparison.OrdinalIgnoreCase)) { dryRun = true; continue; }
            if (string.Equals(a, UpdateFlag, StringComparison.OrdinalIgnoreCase)) { update = true; continue; }
            if (a.StartsWith('-')) return new(null, false, Usage);
            if (path is not null) return new(null, false, Usage);
            path = a;
        }
        return string.IsNullOrWhiteSpace(path) ? new(null, false, Usage) : new(path, dryRun, null, update);
    }

    /// <summary>
    /// Ruta del JSON: tal cual (absoluta o relativa al directorio actual) y, si no existe y es relativa, relativa a la raíz del
    /// repositorio (dotnet run puede ejecutar desde la carpeta del proyecto).
    /// </summary>
    public static string ResolveConfigPath(string path)
    {
        if (File.Exists(path) || Path.IsPathRooted(path)) return path;
        try
        {
            var fromRoot = Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), path);
            return File.Exists(fromRoot) ? fromRoot : path;
        }
        catch (InvalidOperationException) { return path; }
    }

    /// <summary>Variable de entorno con la contraseña inicial del administrador de la compañía (opcional).</summary>
    public const string AdminPasswordVariable = "TEIKEM_IMPORT_ADMIN_PASSWORD";

    /// <summary>Variable de entorno (= 1) para que el administrador de la compañía sea también administrador de plataforma.</summary>
    public const string AdminPlatformVariable = "TEIKEM_IMPORT_ADMIN_PLATFORM";

    public async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        // La consola de Windows no usa UTF-8 por defecto: sin esto los acentos del resumen salen ilegibles.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
        catch (IOException) { }

        var parsed = ParseArgs(args);
        if (parsed.Error is not null)
        {
            Console.Error.WriteLine(parsed.Error);
            return ExitUsage;
        }

        LegacyImportConfig cfg;
        try
        {
            cfg = LegacyImportConfig.Load(ResolveConfigPath(parsed.ConfigPath!));
            // la contraseña inicial del administrador no va en el JSON: viene de la variable de entorno (si no, se genera una temporal)
            var envPassword = Environment.GetEnvironmentVariable(AdminPasswordVariable);
            if (!string.IsNullOrWhiteSpace(envPassword) && string.IsNullOrWhiteSpace(cfg.Company.AdminPassword)) cfg.Company.AdminPassword = envPassword;
            if (Environment.GetEnvironmentVariable(AdminPlatformVariable) is "1" or "true") cfg.Company.AdminIsPlatformAdmin = true;
        }
        catch (ValidationException ex)
        {
            Console.Error.WriteLine($"Error de configuración: {ex.Message}");
            return ExitSevere;
        }

        Console.WriteLine($"Migración de '{cfg.Company.Name}' — {(parsed.DryRun ? LegacyImportReport.DryRunMark : LegacyImportReport.RealRunMark)}{(parsed.Update ? " — modo --update" : string.Empty)}");
        LegacyImportReport report;
        string? temporaryPassword;
        try
        {
            using var scope = scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<LegacyImportService>();
            report = await service.RunAsync(cfg, parsed.DryRun, parsed.Update, ct);
            temporaryPassword = service.TemporaryAdminPassword;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Falla de lectura de las fuentes (archivo con columnas faltantes, MSWM inaccesible) o de la base: nada que reportar.
            logger.LogError(ex, "La migración de {Company} se detuvo.", cfg.Company.Name);
            Console.Error.WriteLine($"La migración se detuvo: {(ex is TeikemException te ? te.Message : ex.GetBaseException().Message)}");
            return ExitSevere;
        }

        var paths = await report.WriteAsync(cfg.ResolvedOutputDir, cfg.ResolvedPrefix, ct);
        Console.WriteLine(Summary(report));
        Console.WriteLine($"Reporte: {paths[0]}");
        foreach (var p in paths.Skip(1)) Console.WriteLine($"         {p}");
        // La contraseña temporal no se imprime (la consola suele quedar en bitácoras): se restablece desde la administración de usuarios.
        if (temporaryPassword is not null)
            Console.WriteLine($"Se creó el usuario administrador {cfg.Company.AdminEmail} con una contraseña temporal que no se muestra; "
                              + "restablézcala desde la administración de usuarios antes de entregar el acceso.");

        if (report.HasSevereRejections)
        {
            Console.Error.WriteLine($"Hubo {report.Rechazos.Count(r => r.Severe)} rechazo(s) grave(s); revise el reporte.");
            return ExitSevere;
        }
        return ExitOk;
    }

    /// <summary>Tabla de resumen para la consola (entidad, leídos, creados, ya existían, omitidos, rechazados, esperados).</summary>
    public static string Summary(LegacyImportReport report)
    {
        var rows = new List<string[]> { new[] { "Entidad", "Leídos", "Creados", "Ya existían", "Omitidos", "Rechazados", "Esperados", "Actualizados" } };
        rows.AddRange(report.Resumen.Select(t => new[]
        {
            t.Entity, N(t.Read), N(t.Created), N(t.Existing), N(t.Skipped), N(t.Rejected), t.Expected is { } e ? N(e) : "—", N(t.Updated),
        }));
        var widths = Enumerable.Range(0, rows[0].Length).Select(i => rows.Max(r => r[i].Length)).ToArray();
        var lines = rows.Select(r => string.Join("  ", r.Select((c, i) => i == 0 ? c.PadRight(widths[i]) : c.PadLeft(widths[i]))));
        return string.Join(Environment.NewLine, lines)
               + Environment.NewLine
               + $"Rechazos: {report.Rechazos.Count} · Advertencias: {report.Advertencias.Count} · Asientos de saldo inicial: {report.SaldoInicial.Count}"
               + (report.UpdateMode ? $" · Actualizaciones: {report.Actualizaciones.Count}" : string.Empty);

        static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
