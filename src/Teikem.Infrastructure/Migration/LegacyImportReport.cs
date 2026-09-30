using System.Globalization;
using System.Text;
using Teikem.Domain.Migration;

namespace Teikem.Infrastructure.Migration;

/// <summary>Totales de una entidad en el resumen (leídos, creados, ya existían, omitidos, rechazados y esperados).</summary>
public sealed class LegacyReportTotal(string entity)
{
    public string Entity { get; } = entity;
    public int Read { get; set; }
    public int Created { get; set; }
    public int Existing { get; set; }
    public int Skipped { get; set; }
    public int Rejected { get; set; }
    /// <summary>Modo --update: filas existentes cuyos datos se actualizaron desde QuickBooks.</summary>
    public int Updated { get; set; }

    /// <summary>Total esperado según el plan (totales_esperados), para comparar; null = sin referencia.</summary>
    public int? Expected { get; set; }
}

/// <summary>Rechazo o advertencia: entidad, clave natural de la fila (SKU, nombre, posición) y mensaje exacto.
/// Severe = rechazo grave (conciliación con diferencias o error de configuración): hace fallar el comando.</summary>
public sealed record LegacyReportIssue(string Entity, string Key, string Message, bool Severe = false);

/// <summary>Mapeo aplicado (SKU normalizado, término de pago, zona de una posición, etc.).</summary>
public sealed record LegacyReportMapping(string Kind, string From, string To);

/// <summary>Actualización aplicada en modo --update: entidad, clave, campo, valor anterior y nuevo.</summary>
public sealed record LegacyReportUpdate(string Entity, string Key, string Field, string? From, string? To);

/// <summary>Línea del saldo inicial: SKU, código de posición y cantidad.</summary>
public sealed record LegacyReportBalance(string Sku, string Bin, decimal Quantity);

/// <summary>
/// Lote 11: cupo estimado de una posición del WMS (CSV -cupos): posición, zona, pasillo, máximo histórico (null = sin
/// historial), cupo, origen (HISTORIAL, PASILLO, ZONA, ALMACEN) y qué hizo el importador con él.
/// </summary>
public sealed record LegacyReportCapacity(string Bin, string Zone, string? Aisle, decimal? HistoricalMax, int Capacity, string Origin, string Result);

/// <summary>
/// Lote 10 (P2): acumulador del reporte del importador. Secciones: Resumen (totales por entidad), Rechazos,
/// Advertencias, Mapeos y SaldoInicial. <see cref="WriteAsync"/> escribe un .md en español con la marca
/// 'SIMULACIÓN (dry-run)' o 'CARGA REAL' en el título y un CSV por sección.
/// </summary>
public sealed class LegacyImportReport
{
    public const string DryRunMark = "SIMULACIÓN (dry-run)";
    public const string RealRunMark = "CARGA REAL";

    private readonly List<LegacyReportTotal> _resumen = new();
    private readonly List<LegacyReportIssue> _rechazos = new();
    private readonly List<LegacyReportIssue> _advertencias = new();
    private readonly List<LegacyReportMapping> _mapeos = new();
    private readonly List<LegacyReportBalance> _saldoInicial = new();
    private readonly List<LegacyReportUpdate> _actualizaciones = new();
    private readonly List<LegacyReportCapacity> _cupos = new();
    private readonly List<KeyValuePair<string, string>> _info = new();

    public LegacyImportReport() { }

    public LegacyImportReport(string company, bool dryRun)
    {
        Company = company;
        DryRun = dryRun;
    }

    public string Company { get; set; } = string.Empty;
    public bool DryRun { get; set; }

    /// <summary>Hora local de generación; da el sello {yyyyMMdd-HHmm} de los archivos.</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    public IReadOnlyList<LegacyReportTotal> Resumen => _resumen;
    public IReadOnlyList<LegacyReportIssue> Rechazos => _rechazos;
    public IReadOnlyList<LegacyReportIssue> Advertencias => _advertencias;
    public IReadOnlyList<LegacyReportMapping> Mapeos => _mapeos;
    public IReadOnlyList<LegacyReportBalance> SaldoInicial => _saldoInicial;
    public IReadOnlyList<LegacyReportUpdate> Actualizaciones => _actualizaciones;
    /// <summary>Lote 11: cupo estimado de cada posición del WMS y qué se hizo con él (vacío sin MSWM).</summary>
    public IReadOnlyList<LegacyReportCapacity> Cupos => _cupos;
    /// <summary>true cuando el reporte corresponde a una corrida con --update.</summary>
    public bool UpdateMode { get; set; }

    /// <summary>Datos generales que encabezan el reporte (configuración, compañía, almacén, fuente del saldo...).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Info => _info;

    public bool HasSevereRejections => _rechazos.Any(r => r.Severe);

    // ---------------------------------------------------------------- acumuladores

    /// <summary>Totales de la entidad (se crea en orden de aparición).</summary>
    public LegacyReportTotal Total(string entity)
    {
        var t = _resumen.FirstOrDefault(x => string.Equals(x.Entity, entity, StringComparison.OrdinalIgnoreCase));
        if (t is null) { t = new LegacyReportTotal(entity); _resumen.Add(t); }
        return t;
    }

    public void CountRead(string entity, int n = 1) => Total(entity).Read += n;
    public void CountCreated(string entity, int n = 1) => Total(entity).Created += n;
    public void CountExisting(string entity, int n = 1) => Total(entity).Existing += n;
    public void CountSkipped(string entity, int n = 1) => Total(entity).Skipped += n;
    public void CountUpdated(string entity, int n = 1) => Total(entity).Updated += n;
    public void SetExpected(string entity, int expected) => Total(entity).Expected = expected;

    /// <summary>Anota un rechazo con el mensaje exacto y suma uno a los rechazados de la entidad.</summary>
    public void Reject(string entity, string key, string message, bool severe = false)
    {
        _rechazos.Add(new LegacyReportIssue(entity, key, message, severe));
        Total(entity).Rejected++;
    }

    public void Warn(string entity, string key, string message) => _advertencias.Add(new LegacyReportIssue(entity, key, message));

    public void Map(string kind, string from, string to) => _mapeos.Add(new LegacyReportMapping(kind, from, to));

    public void AddOpeningBalance(string sku, string bin, decimal quantity) => _saldoInicial.Add(new LegacyReportBalance(sku, bin, quantity));

    /// <summary>Anota un cambio aplicado (o que se aplicaría, en dry-run) sobre una fila existente en modo --update.</summary>
    public void AddUpdate(string entity, string key, string field, string? from, string? to) => _actualizaciones.Add(new LegacyReportUpdate(entity, key, field, from, to));

    public void AddInfo(string label, string value) => _info.Add(new(label, value));

    /// <summary>Anota el cupo estimado de una posición y el resultado (asignado, se asignaría, se conserva…).</summary>
    public void AddCapacity(BinCapacityEstimate e, string result)
        => _cupos.Add(new LegacyReportCapacity(e.Code, e.ZoneCode, e.Aisle, e.HistoricalMax, e.Capacity, e.Origin, result));

    // ---------------------------------------------------------------- render

    public string Title => $"Migración de datos — {Company} — {(DryRun ? DryRunMark : RealRunMark)}{(UpdateMode ? " — modo --update" : string.Empty)}";

    /// <summary>Reporte completo en Markdown (tablas en español).</summary>
    public string RenderMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(Md(Title)).AppendLine();
        sb.Append("Generado: ").AppendLine(GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        if (DryRun) sb.AppendLine().AppendLine("> Simulación: no se escribió nada en la base de datos.");
        sb.AppendLine();

        if (_info.Count > 0)
        {
            sb.AppendLine("| Dato | Valor |").AppendLine("|---|---|");
            foreach (var (k, v) in _info) sb.AppendLine($"| {Md(k)} | {Md(v)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Resumen").AppendLine();
        if (_resumen.Count == 0) sb.AppendLine("Sin datos.");
        else
        {
            sb.AppendLine("| Entidad | Leídos | Creados | Ya existían | Omitidos | Rechazados | Esperados | Actualizados |")
              .AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var t in _resumen)
                sb.AppendLine($"| {Md(t.Entity)} | {t.Read} | {t.Created} | {t.Existing} | {t.Skipped} | {t.Rejected} | {(t.Expected?.ToString(CultureInfo.InvariantCulture) ?? "—")} | {t.Updated} |");
        }
        sb.AppendLine();

        sb.AppendLine($"## Rechazos ({_rechazos.Count})").AppendLine();
        if (_rechazos.Count == 0) sb.AppendLine("Sin rechazos.");
        else
        {
            sb.AppendLine("| Entidad | Clave | Motivo | Grave |").AppendLine("|---|---|---|---|");
            foreach (var r in _rechazos) sb.AppendLine($"| {Md(r.Entity)} | {Md(r.Key)} | {Md(r.Message)} | {(r.Severe ? "Sí" : "No")} |");
        }
        sb.AppendLine();

        sb.AppendLine($"## Advertencias ({_advertencias.Count})").AppendLine();
        if (_advertencias.Count == 0) sb.AppendLine("Sin advertencias.");
        else
        {
            sb.AppendLine("| Entidad | Clave | Detalle |").AppendLine("|---|---|---|");
            foreach (var w in _advertencias) sb.AppendLine($"| {Md(w.Entity)} | {Md(w.Key)} | {Md(w.Message)} |");
        }
        sb.AppendLine();

        sb.AppendLine($"## Mapeos ({_mapeos.Count})").AppendLine();
        if (_mapeos.Count == 0) sb.AppendLine("Sin mapeos.");
        else
        {
            sb.AppendLine("| Tipo | Origen | Destino |").AppendLine("|---|---|---|");
            foreach (var m in _mapeos) sb.AppendLine($"| {Md(m.Kind)} | {Md(m.From)} | {Md(m.To)} |");
        }
        sb.AppendLine();

        sb.AppendLine($"## Actualizaciones ({_actualizaciones.Count})").AppendLine();
        if (_actualizaciones.Count == 0) sb.AppendLine(UpdateMode ? "Sin cambios sobre las filas existentes." : "Sin actualizaciones (solo con --update).");
        else
        {
            sb.AppendLine("| Entidad | Clave | Campo | Antes | Después |").AppendLine("|---|---|---|---|---|");
            foreach (var u in _actualizaciones) sb.AppendLine($"| {Md(u.Entity)} | {Md(u.Key)} | {Md(u.Field)} | {Md(u.From)} | {Md(u.To)} |");
        }
        sb.AppendLine();

        if (_cupos.Count > 0) RenderCapacities(sb);

        var units = _saldoInicial.Sum(b => b.Quantity);
        sb.AppendLine($"## Saldo inicial ({_saldoInicial.Count} asientos, {Qty(units)} unidades)").AppendLine();
        if (_saldoInicial.Count == 0) sb.AppendLine("Sin saldo inicial.");
        else
        {
            sb.AppendLine("| SKU | Posición | Cantidad |").AppendLine("|---|---|---:|");
            foreach (var b in _saldoInicial) sb.AppendLine($"| {Md(b.Sku)} | {Md(b.Bin)} | {Qty(b.Quantity)} |");
            sb.AppendLine($"| **Total** | | **{Qty(units)}** |");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Lote 11: resumen de los cupos estimados — por origen (posiciones, cupo mínimo, mediana y máximo) y por resultado. El
    /// detalle por posición va en el CSV -cupos.
    /// </summary>
    private void RenderCapacities(StringBuilder sb)
    {
        sb.AppendLine($"## Cupos de posición estimados ({_cupos.Count} posiciones)").AppendLine();
        sb.AppendLine("Cupo = máximo histórico de la posición en el WMS redondeado hacia arriba a la decena (HISTORIAL); sin historial, "
                      + "la mediana de su pasillo (PASILLO), de su zona (ZONA) o del almacén (ALMACEN). Detalle por posición en el CSV `-cupos`.")
          .AppendLine();
        sb.AppendLine("| Origen | Posiciones | Cupo mínimo | Mediana | Cupo máximo |").AppendLine("|---|---:|---:|---:|---:|");
        foreach (var origin in BinCapacityOrigins.All.Concat(_cupos.Select(c => c.Origin)).Distinct(StringComparer.Ordinal))
        {
            var caps = _cupos.Where(c => c.Origin == origin).Select(c => c.Capacity).OrderBy(c => c).ToList();
            if (caps.Count == 0) { sb.AppendLine($"| {Md(origin)} | 0 | — | — | — |"); continue; }
            sb.AppendLine($"| {Md(origin)} | {I(caps.Count)} | {I(caps[0])} | {I(BinCapacityRules.Median(caps)!.Value)} | {I(caps[^1])} |");
        }
        var all = _cupos.Select(c => c.Capacity).OrderBy(c => c).ToList();
        sb.AppendLine($"| **Total** | **{I(all.Count)}** | {I(all[0])} | {I(BinCapacityRules.Median(all)!.Value)} | {I(all[^1])} |").AppendLine();

        sb.AppendLine("| Resultado | Posiciones |").AppendLine("|---|---:|");
        foreach (var g in _cupos.GroupBy(c => c.Result).OrderByDescending(g => g.Count()))
            sb.AppendLine($"| {Md(g.Key)} | {I(g.Count())} |");
        sb.AppendLine();
    }

    /// <summary>CSV (RFC-4180) de cada sección: nombre de archivo sufijo → contenido. El de cupos solo si hay cupos estimados.</summary>
    public IReadOnlyList<(string Suffix, string Content)> RenderCsv()
    {
        var list = new List<(string, string)>
        {
            ("resumen", Csv(new[] { "Entidad", "Leidos", "Creados", "YaExistian", "Omitidos", "Rechazados", "Esperados", "Actualizados" },
                _resumen.Select(t => new[]
                {
                    t.Entity, I(t.Read), I(t.Created), I(t.Existing), I(t.Skipped), I(t.Rejected),
                    t.Expected is { } e ? I(e) : string.Empty, I(t.Updated),
                }))),
            ("rechazos", Csv(new[] { "Entidad", "Clave", "Motivo", "Grave" },
                _rechazos.Select(r => new[] { r.Entity, r.Key, r.Message, r.Severe ? "Sí" : "No" }))),
            ("advertencias", Csv(new[] { "Entidad", "Clave", "Detalle" },
                _advertencias.Select(w => new[] { w.Entity, w.Key, w.Message }))),
            ("mapeos", Csv(new[] { "Tipo", "Origen", "Destino" },
                _mapeos.Select(m => new[] { m.Kind, m.From, m.To }))),
            ("saldo-inicial", Csv(new[] { "SKU", "Posicion", "Cantidad" },
                _saldoInicial.Select(b => new[] { b.Sku, b.Bin, Qty(b.Quantity) }))),
            ("actualizaciones", Csv(new[] { "Entidad", "Clave", "Campo", "Antes", "Despues" },
                _actualizaciones.Select(u => new[] { u.Entity, u.Key, u.Field, u.From ?? string.Empty, u.To ?? string.Empty }))),
        };
        if (_cupos.Count > 0)
            list.Add(("cupos", Csv(new[] { "Posicion", "Zona", "Pasillo", "MaximoHistorico", "Cupo", "Origen", "Resultado" },
                _cupos.Select(c => new[]
                {
                    c.Bin, c.Zone, c.Aisle ?? string.Empty, c.HistoricalMax is { } m ? Qty(m) : string.Empty, I(c.Capacity), c.Origin, c.Result,
                }))));
        return list;
    }

    /// <summary>
    /// Escribe {prefix}-{yyyyMMdd-HHmm}.md y un CSV por sección ({prefix}-{sello}-{sección}.csv) en
    /// <paramref name="outputDir"/> (se crea si no existe). Devuelve las rutas; la primera es la del .md.
    /// </summary>
    public async Task<IReadOnlyList<string>> WriteAsync(string outputDir, string prefix, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        var baseName = $"{SafeFileName(string.IsNullOrWhiteSpace(prefix) ? "migracion" : prefix.Trim())}-{GeneratedAt.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}";
        var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true); // Excel abre bien los acentos con BOM

        var paths = new List<string>();
        var md = Path.Combine(outputDir, baseName + ".md");
        await File.WriteAllTextAsync(md, RenderMarkdown(), new UTF8Encoding(false), ct);
        paths.Add(md);

        foreach (var (suffix, content) in RenderCsv())
        {
            var csv = Path.Combine(outputDir, $"{baseName}-{suffix}.csv");
            await File.WriteAllTextAsync(csv, content, utf8Bom, ct);
            paths.Add(csv);
        }
        return paths;
    }

    // ---------------------------------------------------------------- utilidades

    private static string I(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Qty(decimal q) => q.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Celda de tabla Markdown: escapa '|' y aplana saltos de línea.</summary>
    private static string Md(string? s)
        => (s ?? string.Empty).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace("|", "\\|");

    private static string Csv(IEnumerable<string> header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", header.Select(CsvCell))).Append("\r\n");
        foreach (var r in rows) sb.Append(string.Join(",", r.Select(CsvCell))).Append("\r\n");
        return sb.ToString();
    }

    private static string CsvCell(string? s)
    {
        s ??= string.Empty;
        return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    private static string SafeFileName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
