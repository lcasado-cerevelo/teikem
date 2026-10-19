using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence.Scripts;

namespace Teikem.Infrastructure.Migration.Colavora;

/// <summary>Argumentos ya interpretados del verbo import-colavora (Error != null = sintaxis incorrecta).</summary>
public sealed record ColavoraImportArgs(string? ConfigPath, bool DryRun, IReadOnlyList<string> Phases, int? From, int? To, string? Error, DateTime? SinceDate = null);

/// <summary>
/// Verbo CLI <c>dotnet run --project src/Teikem.Api -- import-colavora &lt;config.json&gt; [--dry-run] [--solo fase,fase] [--desde N] [--hasta N]</c>.
/// Migra una compañía de Colavora Courier (propio: no comparte nada con import-legacy). Escribe el reporte (.md + .csv) fuera
/// del repositorio y lo resume por consola. Códigos de salida: 0 sin rechazos graves; 1 con rechazos graves o falla de lectura;
/// 2 si la sintaxis es incorrecta.
/// </summary>
public sealed class ColavoraImportRunner(IServiceScopeFactory scopes, ILogger<ColavoraImportRunner> logger)
{
    public const string Verb = "import-colavora";
    public const string DryRunFlag = "--dry-run";
    public const string OnlyFlag = "--solo";
    public const string FromFlag = "--desde";
    public const string ToFlag = "--hasta";
    /// <summary>Para pruebas: solo las órdenes creadas en los últimos N meses (--meses) o desde una fecha (--desde-fecha aaaa-mm-dd).</summary>
    public const string MonthsFlag = "--meses";
    public const string SinceFlag = "--desde-fecha";
    public const string Usage = "Uso: dotnet run --project src/Teikem.Api -- import-colavora <config.json> [--dry-run] [--solo catalogos,usuarios,zonas,clientes,consignatarios,contratos,ordenes] [--desde <ShipmentID>] [--hasta <ShipmentID>] [--meses <N> | --desde-fecha <aaaa-mm-dd>]";
    public const string AdminPasswordVariable = "TEIKEM_IMPORT_ADMIN_PASSWORD";

    public const int ExitOk = 0;
    public const int ExitSevere = 1;
    public const int ExitUsage = 2;

    public static ColavoraImportArgs ParseArgs(IReadOnlyList<string> args)
    {
        var start = -1;
        for (var i = 0; i < args.Count; i++)
            if (string.Equals(args[i], Verb, StringComparison.OrdinalIgnoreCase)) { start = i; break; }
        if (start < 0) return new(null, false, Array.Empty<string>(), null, null, Usage);

        string? path = null;
        var dryRun = false;
        var phases = new List<string>();
        int? from = null, to = null;
        DateTime? since = null;
        for (var i = start + 1; i < args.Count; i++)
        {
            var a = args[i];
            if (string.Equals(a, DryRunFlag, StringComparison.OrdinalIgnoreCase)) { dryRun = true; continue; }
            if (string.Equals(a, MonthsFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= args.Count || !int.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out var months) || months <= 0) return Bad();
                since = DateTime.Today.AddMonths(-months);
                continue;
            }
            if (string.Equals(a, SinceFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= args.Count || !DateTime.TryParseExact(args[i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return Bad();
                since = d;
                continue;
            }
            if (string.Equals(a, OnlyFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= args.Count) return Bad();
                foreach (var p in args[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!ColavoraPhases.All.Contains(p, StringComparer.OrdinalIgnoreCase)) return Bad();
                    phases.Add(p.ToLowerInvariant());
                }
                continue;
            }
            if (string.Equals(a, FromFlag, StringComparison.OrdinalIgnoreCase) || string.Equals(a, ToFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= args.Count || !int.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return Bad();
                if (string.Equals(a, FromFlag, StringComparison.OrdinalIgnoreCase)) from = n; else to = n;
                continue;
            }
            if (a.StartsWith('-') || path is not null) return Bad();
            path = a;
        }
        return string.IsNullOrWhiteSpace(path) ? Bad() : new(path, dryRun, phases, from, to, null, since);

        static ColavoraImportArgs Bad() => new(null, false, Array.Empty<string>(), null, null, Usage);
    }

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

    public async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
        catch (IOException) { }

        var parsed = ParseArgs(args);
        if (parsed.Error is not null)
        {
            Console.Error.WriteLine(parsed.Error);
            return ExitUsage;
        }

        ColavoraImportConfig cfg;
        try
        {
            cfg = ColavoraImportConfig.Load(ResolveConfigPath(parsed.ConfigPath!));
            var envPassword = Environment.GetEnvironmentVariable(AdminPasswordVariable);
            if (!string.IsNullOrWhiteSpace(envPassword)) cfg.Company.AdminPassword = envPassword;
            if (parsed.From is int f) cfg.Options.FromShipmentId = f;
            if (parsed.To is int t) cfg.Options.ToShipmentId = t;
            if (parsed.SinceDate is DateTime sd) cfg.Options.SinceDate = sd;
        }
        catch (ValidationException ex)
        {
            Console.Error.WriteLine($"Error de configuración: {ex.Message}");
            return ExitSevere;
        }

        Console.WriteLine($"Migración de Colavora Courier → '{cfg.Company.Name}' (CompanyID {cfg.Company.ColavoraCompanyId}) — {(parsed.DryRun ? ColavoraImportReport.DryRunMark : ColavoraImportReport.RealRunMark)}{(cfg.Options.SinceDate is DateTime since ? $" — solo órdenes creadas desde {since:yyyy-MM-dd}" : string.Empty)}");
        ColavoraImportReport report;
        string? temporaryPassword;
        try
        {
            using var scope = scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ColavoraImportService>();
            report = await service.RunAsync(cfg, parsed.DryRun, parsed.Phases, ct);
            temporaryPassword = service.TemporaryAdminPassword;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "La migración de {Company} se detuvo.", cfg.Company.Name);
            Console.Error.WriteLine($"La migración se detuvo: {(ex is TeikemException te ? te.Message : ex.GetBaseException().Message)}");
            return ExitSevere;
        }

        IReadOnlyList<string> files;
        try { files = await report.WriteAsync(cfg.ResolvedOutputDir, cfg.Report.Prefix!, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"No se pudo escribir el reporte en {cfg.ResolvedOutputDir}: {ex.Message}");
            files = Array.Empty<string>();
        }

        Console.WriteLine();
        Console.WriteLine(report.Title);
        foreach (var t in report.Totals.Where(t => t.Read + t.Created + t.Existing + t.Skipped + t.Rejected > 0))
            Console.WriteLine($"  {t.Entity,-26} leídas {t.Read,10:N0}  creadas {t.Created,10:N0}  existían {t.Existing,10:N0}  omitidas {t.Skipped,8:N0}  rechazadas {t.Rejected,8:N0}");
        Console.WriteLine($"  Rechazos: {report.Rejections.Count:N0} ({report.Rejections.Count(r => r.Severe)} graves); advertencias: {report.WarningCount:N0}");
        foreach (var r in report.Rejections.Where(r => r.Severe).Take(10)) Console.WriteLine($"  GRAVE [{r.Entity}] {r.Key}: {r.Message}");
        foreach (var f in files) Console.WriteLine($"  Reporte: {f}");
        if (temporaryPassword is not null) Console.WriteLine("  El administrador de la compañía se creó con una contraseña temporal (no se muestra): use «olvidé mi contraseña» o la variable TEIKEM_IMPORT_ADMIN_PASSWORD.");
        return report.HasSevereRejections ? ExitSevere : ExitOk;
    }
}
