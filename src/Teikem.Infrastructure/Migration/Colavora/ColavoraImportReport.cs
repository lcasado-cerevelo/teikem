using System.Globalization;
using System.Text;

namespace Teikem.Infrastructure.Migration.Colavora;

/// <summary>Totales por entidad del reporte de la migración de Colavora.</summary>
public sealed class ColavoraReportTotal(string entity)
{
    public string Entity { get; } = entity;
    public long Read { get; set; }
    public long Created { get; set; }
    public long Existing { get; set; }
    public long Skipped { get; set; }
    public long Rejected { get; set; }
}

public sealed record ColavoraReportIssue(string Entity, string Key, string Message, bool Severe = false);

/// <summary>Nombres de las entidades del resumen (en el orden de carga).</summary>
public static class ColavoraEntities
{
    public const string Company = "Compañía";
    public const string Catalogs = "Catálogos";
    public const string Reasons = "Razones de entrega";
    public const string Users = "Usuarios";
    public const string Drivers = "Choferes";
    public const string Zones = "Zonas de despacho";
    public const string Clients = "Clientes";
    public const string Consignees = "Consignatarios";
    public const string Contracts = "Contratos y tarifas";
    public const string CustomFields = "Campos personalizados";
    public const string Orders = "Envíos";
    public const string Pieces = "Piezas";
    public const string History = "Historial";
    public const string Attempts = "Intentos de entrega";
    public const string Trips = "Viajes (manifiestos)";
    public const string Proofs = "Comprobantes de entrega";
    public const string ProofFiles = "Firmas y fotos";

    public static readonly string[] Order =
    {
        Company, Catalogs, Reasons, Users, Drivers, Zones, Clients, Consignees, Contracts, CustomFields,
        Orders, Pieces, History, Attempts, Trips, Proofs, ProofFiles,
    };
}

/// <summary>
/// Reporte de una corrida de <c>import-colavora</c> (propio de esta migración; no comparte nada con el importador de almacén):
/// totales por entidad, rechazos y advertencias con la clave de la fila de Colavora, e información de la corrida. Se escribe
/// como .md y .csv fuera del repositorio. Los rechazos se guardan todos; las advertencias se cuentan y se guardan hasta un tope.
/// </summary>
public sealed class ColavoraImportReport(string company, bool dryRun)
{
    public const string DryRunMark = "SIMULACIÓN (dry-run)";
    public const string RealRunMark = "CARGA REAL";
    public const int MaxStoredWarnings = 5000;

    private readonly List<ColavoraReportTotal> _totals = ColavoraEntities.Order.Select(e => new ColavoraReportTotal(e)).ToList();
    private readonly List<ColavoraReportIssue> _rejections = new();
    private readonly List<ColavoraReportIssue> _warnings = new();
    private readonly Dictionary<string, long> _warningCounts = new(StringComparer.Ordinal);
    private readonly List<KeyValuePair<string, string>> _info = new();

    public string Company { get; } = company;
    public bool DryRun { get; } = dryRun;
    public DateTime GeneratedAt { get; } = DateTime.Now;
    public IReadOnlyList<ColavoraReportTotal> Totals => _totals;
    public IReadOnlyList<ColavoraReportIssue> Rejections => _rejections;
    public IReadOnlyList<ColavoraReportIssue> Warnings => _warnings;
    public IReadOnlyList<KeyValuePair<string, string>> Info => _info;
    public long WarningCount { get; private set; }
    public bool HasSevereRejections => _rejections.Any(r => r.Severe);

    public ColavoraReportTotal Total(string entity)
    {
        var t = _totals.FirstOrDefault(x => x.Entity == entity);
        if (t is null) { t = new ColavoraReportTotal(entity); _totals.Add(t); }
        return t;
    }

    public void CountRead(string entity, long n = 1) => Total(entity).Read += n;
    public void CountCreated(string entity, long n = 1) => Total(entity).Created += n;
    public void CountExisting(string entity, long n = 1) => Total(entity).Existing += n;
    public void CountSkipped(string entity, long n = 1) => Total(entity).Skipped += n;

    public void Reject(string entity, string key, string message, bool severe = false)
    {
        Total(entity).Rejected++;
        _rejections.Add(new ColavoraReportIssue(entity, key, message, severe));
    }

    public void Warn(string entity, string key, string message)
    {
        WarningCount++;
        _warningCounts[message] = _warningCounts.GetValueOrDefault(message) + 1;
        if (_warnings.Count < MaxStoredWarnings) _warnings.Add(new ColavoraReportIssue(entity, key, message));
    }

    public void AddInfo(string label, string value) => _info.Add(new(label, value));

    public string Title => $"Migración de Colavora Courier — {Company} — {(DryRun ? DryRunMark : RealRunMark)}";

    public string RenderMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {Title}");
        sb.AppendLine();
        sb.AppendLine($"Generado: {GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        if (_info.Count > 0)
        {
            sb.AppendLine("## Corrida");
            sb.AppendLine();
            foreach (var (k, v) in _info) sb.AppendLine($"- **{k}**: {v}");
            sb.AppendLine();
        }
        sb.AppendLine("## Resumen");
        sb.AppendLine();
        sb.AppendLine("| Entidad | Leídas | Creadas | Ya existían | Omitidas | Rechazadas |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var t in _totals.Where(t => t.Read + t.Created + t.Existing + t.Skipped + t.Rejected > 0))
            sb.AppendLine($"| {t.Entity} | {N(t.Read)} | {N(t.Created)} | {N(t.Existing)} | {N(t.Skipped)} | {N(t.Rejected)} |");
        sb.AppendLine();
        sb.AppendLine($"## Rechazos ({N(_rejections.Count)})");
        sb.AppendLine();
        if (_rejections.Count == 0) sb.AppendLine("Ninguno.");
        else
        {
            sb.AppendLine("| Entidad | Clave | Mensaje | Grave |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var r in _rejections.Take(2000)) sb.AppendLine($"| {r.Entity} | {Cell(r.Key)} | {Cell(r.Message)} | {(r.Severe ? "sí" : "")} |");
            if (_rejections.Count > 2000) sb.AppendLine($"| … | … | {N(_rejections.Count - 2000)} rechazos más en el CSV | |");
        }
        sb.AppendLine();
        sb.AppendLine($"## Advertencias ({N(WarningCount)})");
        sb.AppendLine();
        if (WarningCount == 0) sb.AppendLine("Ninguna.");
        else
        {
            sb.AppendLine("| Advertencia | Veces |");
            sb.AppendLine("|---|---:|");
            foreach (var (msg, n) in _warningCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                sb.AppendLine($"| {Cell(msg)} | {N(n)} |");
            sb.AppendLine();
            sb.AppendLine($"El detalle por fila (hasta {N(MaxStoredWarnings)}) está en el CSV de advertencias.");
        }
        return sb.ToString();
    }

    public IReadOnlyList<(string Suffix, string Content)> RenderCsv()
    {
        var list = new List<(string, string)>();
        var sb = new StringBuilder("Entidad,Leidas,Creadas,YaExistian,Omitidas,Rechazadas\n");
        foreach (var t in _totals) sb.AppendLine($"{Csv(t.Entity)},{t.Read},{t.Created},{t.Existing},{t.Skipped},{t.Rejected}");
        list.Add(("resumen", sb.ToString()));
        sb = new StringBuilder("Entidad,Clave,Mensaje,Grave\n");
        foreach (var r in _rejections) sb.AppendLine($"{Csv(r.Entity)},{Csv(r.Key)},{Csv(r.Message)},{(r.Severe ? 1 : 0)}");
        list.Add(("rechazos", sb.ToString()));
        sb = new StringBuilder("Entidad,Clave,Mensaje\n");
        foreach (var w in _warnings) sb.AppendLine($"{Csv(w.Entity)},{Csv(w.Key)},{Csv(w.Message)}");
        list.Add(("advertencias", sb.ToString()));
        return list;
    }

    /// <summary>Escribe <c>{prefix}-{compañía}-{fecha}.md</c> y los CSV en <paramref name="outputDir"/>; devuelve las rutas.</summary>
    public async Task<IReadOnlyList<string>> WriteAsync(string outputDir, string prefix, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        var stamp = GeneratedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var slug = new string(Company.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c)).ToArray());
        var baseName = $"{prefix}-{slug}-{stamp}{(DryRun ? "-dryrun" : string.Empty)}";
        var utf8 = new UTF8Encoding(false);
        var paths = new List<string>();
        var md = Path.Combine(outputDir, baseName + ".md");
        await File.WriteAllTextAsync(md, RenderMarkdown(), utf8, ct);
        paths.Add(md);
        foreach (var (suffix, content) in RenderCsv())
        {
            var p = Path.Combine(outputDir, $"{baseName}-{suffix}.csv");
            await File.WriteAllTextAsync(p, content, utf8, ct);
            paths.Add(p);
        }
        return paths;
    }

    private static string N(long n) => n.ToString("N0", CultureInfo.GetCultureInfo("es-PR"));
    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
}
