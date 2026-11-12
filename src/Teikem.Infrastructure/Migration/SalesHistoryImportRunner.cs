using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Migration;

/// <summary>
/// Lote 241 — configuración del verbo <c>import-sales-history</c> (JSON en camelCase, admite comentarios y comas finales; rutas absolutas o relativas a la carpeta del JSON):
/// <code>
/// { "company": "Advance Solutions", "sales": "SalesByItemDetail.CSV", "purchases": "PurchasesByItemDetail.CSV", "cutoffDate": "2026-11-06", "repsFromCustomField": true }
/// </code>
/// company obligatorio; al menos uno de sales / purchases; cutoffDate opcional (se guarda como fecha de corte de la compañía); repsFromCustomField (por omisión true) pasa
/// los valores del campo personalizado «Representante» a representantes y enlaza los clientes sin representante.
/// </summary>
public sealed class SalesHistoryImportConfig
{
    public string? Company { get; set; }
    public string? Sales { get; set; }
    public string? Purchases { get; set; }
    public DateOnly? CutoffDate { get; set; }
    public bool RepsFromCustomField { get; set; } = true;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    /// <summary>Lee y valida el JSON; las rutas quedan absolutas. Lanza <see cref="ValidationException"/> con el mensaje en español.</summary>
    public static SalesHistoryImportConfig Load(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new ValidationException(LegacyImportConfig.FileNotFound(full));
        SalesHistoryImportConfig? cfg;
        try { cfg = JsonSerializer.Deserialize<SalesHistoryImportConfig>(File.ReadAllText(full), Options); }
        catch (JsonException ex) { throw new ValidationException($"El archivo de configuración no es un JSON válido: {ex.Message}"); }
        if (cfg is null) throw new ValidationException("El archivo de configuración está vacío.");
        var dir = Path.GetDirectoryName(full)!;
        cfg.Company = cfg.Company?.Trim();
        cfg.Sales = Resolve(cfg.Sales, dir);
        cfg.Purchases = Resolve(cfg.Purchases, dir);
        if (string.IsNullOrWhiteSpace(cfg.Company)) throw new ValidationException("company es obligatorio.");
        if (cfg.Sales is null && cfg.Purchases is null) throw new ValidationException("Indique al menos uno de los dos archivos: sales o purchases.");
        foreach (var f in new[] { cfg.Sales, cfg.Purchases })
            if (f is not null && !File.Exists(f)) throw new ValidationException(LegacyImportConfig.FileNotFound(f));
        return cfg;
    }

    private static string? Resolve(string? p, string dir)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        var t = p.Trim();
        return Path.IsPathRooted(t) ? Path.GetFullPath(t) : Path.GetFullPath(Path.Combine(dir, t));
    }
}

/// <summary>
/// Lote 241 — verbo CLI <c>dotnet run --project src/Teikem.Api -- import-sales-history &lt;config.json&gt; [--dry-run]</c>: importa (o simula, con --dry-run) el historial de
/// ventas y compras de QuickBooks a la compañía indicada (se busca por nombre; no se crea). Imprime el reporte (líneas por clase, rango de fechas, totales por mes para cuadrar
/// y los clientes e ítems que no emparejaron) y lo deja en JSON junto al archivo de configuración. Salida: 0 bien, 1 error (configuración, compañía o archivo), 2 sintaxis.
/// </summary>
public sealed class SalesHistoryImportRunner(IServiceScopeFactory scopes, ILogger<SalesHistoryImportRunner> logger)
{
    public const string Verb = "import-sales-history";
    public const string Usage = "Uso: dotnet run --project src/Teikem.Api -- import-sales-history <config.json> [--dry-run]";

    public static string CompanyNotFound(string name) => $"No existe la compañía «{name}» (se busca por nombre exacto).";

    /// <summary>(ruta, simulación, error de sintaxis).</summary>
    public static (string? Path, bool DryRun, string? Error) ParseArgs(IReadOnlyList<string> args)
    {
        var start = -1;
        for (var i = 0; i < args.Count; i++) if (string.Equals(args[i], Verb, StringComparison.OrdinalIgnoreCase)) { start = i; break; }
        if (start < 0) return (null, false, Usage);
        string? path = null;
        var dry = false;
        foreach (var a in args.Skip(start + 1))
        {
            if (string.Equals(a, LegacyImportRunner.DryRunFlag, StringComparison.OrdinalIgnoreCase)) { dry = true; continue; }
            if (a.StartsWith('-') || path is not null) return (null, false, Usage);
            path = a;
        }
        return string.IsNullOrWhiteSpace(path) ? (null, false, Usage) : (path, dry, null);
    }

    public async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { }
        var (path, dryRun, error) = ParseArgs(args);
        if (error is not null) { Console.Error.WriteLine(error); return LegacyImportRunner.ExitUsage; }
        SalesHistoryImportConfig cfg;
        try { cfg = SalesHistoryImportConfig.Load(LegacyImportRunner.ResolveConfigPath(path!)); }
        catch (ValidationException ex) { Console.Error.WriteLine($"Error de configuración: {ex.Message}"); return LegacyImportRunner.ExitSevere; }

        try
        {
            using var scope = scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var tc = sp.GetRequiredService<TenantContext>();
            var db = sp.GetRequiredService<TeikemDbContext>();
            int? tenantId;
            using (tc.BypassTenantFilter())
                tenantId = await db.Tenants.AsNoTracking().Where(t => t.Name == cfg.Company).Select(t => (int?)t.TenantId).FirstOrDefaultAsync(ct);
            if (tenantId is not int tid) { Console.Error.WriteLine(CompanyNotFound(cfg.Company!)); return LegacyImportRunner.ExitSevere; }
            using var asTenant = tc.As(tid);
            var files = new List<SalesHistoryFile>();
            if (cfg.Sales is not null) files.Add(new SalesHistoryFile(cfg.Sales, await File.ReadAllBytesAsync(cfg.Sales, ct), SalesAnalysisCodes.KindSales));
            if (cfg.Purchases is not null) files.Add(new SalesHistoryFile(cfg.Purchases, await File.ReadAllBytesAsync(cfg.Purchases, ct), SalesAnalysisCodes.KindPurchases));
            var service = sp.GetRequiredService<SalesHistoryImportService>();
            var report = await service.ImportAsync(cfg.Company!, files, dryRun, cfg.CutoffDate, cfg.RepsFromCustomField, SalesAnalysisCodes.ImportCli, ct);
            Console.WriteLine(SalesHistoryImportService.Describe(report));
            var dir = Path.GetDirectoryName(Path.GetFullPath(LegacyImportRunner.ResolveConfigPath(path!)))!;
            var outFile = Path.Combine(dir, $"historial-ventas-{DateTime.Now:yyyyMMdd-HHmmss}{(dryRun ? "-simulacion" : string.Empty)}.json");
            await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), ct);
            Console.WriteLine($"Reporte: {outFile}");
            return LegacyImportRunner.ExitOk;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "La importación del historial de ventas de {Company} se detuvo.", cfg.Company);
            Console.Error.WriteLine($"La importación se detuvo: {(ex is TeikemException te ? te.Message : ex.GetBaseException().Message)}");
            return LegacyImportRunner.ExitSevere;
        }
    }
}
