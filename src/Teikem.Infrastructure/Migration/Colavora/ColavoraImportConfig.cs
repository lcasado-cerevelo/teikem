using System.Text.Json;
using System.Text.Json.Serialization;
using Teikem.Domain.Migration.Colavora;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Migration.Colavora;

/// <summary>
/// Migración de Colavora Courier: modelo del JSON de configuración del verbo <c>import-colavora</c> (una compañía de Colavora
/// por archivo: docs/migracion/colavora/import.advance.json e import.delapaz.json). camelCase, comentarios y comas finales permitidos.
/// Las cadenas de conexión NO van aquí: se leen de ConnectionStrings:ColavoraCourier y ConnectionStrings:ColavoraCourierLogin
/// (variables de entorno que crea scripts/crear-variable-colavora.ps1).
/// </summary>
public sealed class ColavoraImportConfig
{
    public ColavoraCompanyConfig Company { get; set; } = new();
    public ColavoraSourcesConfig Sources { get; set; } = new();
    public ColavoraOptionsConfig Options { get; set; } = new();
    public ColavoraReportConfig Report { get; set; } = new();

    [JsonIgnore] public string? ConfigPath { get; private set; }
    [JsonIgnore] public string? BaseDirectory { get; private set; }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ColavoraImportConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("Falta la ruta del archivo de configuración.");
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new ValidationException($"El archivo no existe: {fullPath}.");
        var cfg = Parse(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath)!);
        cfg.ConfigPath = fullPath;
        return cfg;
    }

    public static ColavoraImportConfig Parse(string json, string baseDirectory)
    {
        ColavoraImportConfig? cfg;
        try { cfg = JsonSerializer.Deserialize<ColavoraImportConfig>(json, JsonOptions); }
        catch (JsonException ex) { throw new ValidationException($"El archivo de configuración no es un JSON válido: {ex.Message}"); }
        if (cfg is null) throw new ValidationException("El archivo de configuración está vacío.");
        cfg.BaseDirectory = Path.GetFullPath(baseDirectory);
        cfg.Normalize();
        cfg.Validate();
        return cfg;
    }

    private void Normalize()
    {
        Company ??= new();
        Sources ??= new();
        Options ??= new();
        Report ??= new();
        Company.Name = Company.Name?.Trim();
        Company.ColavoraCompanyId = Company.ColavoraCompanyId?.Trim();
        Company.Modules ??= new();
        if (string.IsNullOrWhiteSpace(Company.Lang)) Company.Lang = "es";
        if (string.IsNullOrWhiteSpace(Sources.CourierConnectionStringName)) Sources.CourierConnectionStringName = "ColavoraCourier";
        if (string.IsNullOrWhiteSpace(Sources.LoginConnectionStringName)) Sources.LoginConnectionStringName = "ColavoraCourierLogin";
        Options.ExcludeSenders ??= new();
        Options.DescriptionPackageTypes ??= new();
        Options.ExcludeSenderPatterns ??= new() { "-DEV", "-PCK", "SAMPLE:" };
        if (Options.PendingCloseDays <= 0) Options.PendingCloseDays = ColavoraRules.DefaultPendingCloseDays;
        if (Options.BatchSize <= 0) Options.BatchSize = 500;
        if (string.IsNullOrWhiteSpace(Options.DivisionFieldKey)) Options.DivisionFieldKey = "division";
        if (string.IsNullOrWhiteSpace(Options.ColavoraIdFieldKey)) Options.ColavoraIdFieldKey = "colavora_id";
        Options.PodFilesDir = ResolvePath(Options.PodFilesDir);
        if (!string.IsNullOrWhiteSpace(Report.OutputDir)) Report.OutputDir = ResolvePath(Report.OutputDir);
        if (string.IsNullOrWhiteSpace(Report.Prefix)) Report.Prefix = "colavora";
    }

    private string? ResolvePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        var t = p.Trim();
        return Path.IsPathRooted(t) ? Path.GetFullPath(t) : Path.GetFullPath(Path.Combine(BaseDirectory!, t));
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Company.Name)) throw new ValidationException("company.name es obligatorio.");
        if (string.IsNullOrWhiteSpace(Company.ColavoraCompanyId)) throw new ValidationException("company.colavoraCompanyId es obligatorio (10029 o 10030).");
        if (Options.FromShipmentId is int f && Options.ToShipmentId is int t && t < f) throw new ValidationException("options.toShipmentId debe ser mayor o igual que fromShipmentId.");
    }

    /// <summary>Carpeta de salida del reporte: report.outputDir o la carpeta del JSON.</summary>
    [JsonIgnore] public string ResolvedOutputDir => Report.OutputDir ?? BaseDirectory ?? Directory.GetCurrentDirectory();
}

/// <summary>Compañía destino en Teikem (tenant nuevo, D1) y su compañía de origen en Colavora (CompanyID 10029 / 10030).</summary>
public sealed class ColavoraCompanyConfig
{
    /// <summary>CompanyID de Colavora: 10029 = Advance Logistics, 10030 = De La Paz Transport.</summary>
    public string? ColavoraCompanyId { get; set; }
    public string? Name { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string Lang { get; set; } = "es";
    public List<string> Modules { get; set; } = new();
    public string? AdminEmail { get; set; }
    public string? AdminFullName { get; set; }
    /// <summary>No va en el JSON: viene de la variable de entorno TEIKEM_IMPORT_ADMIN_PASSWORD.</summary>
    [JsonIgnore] public string? AdminPassword { get; set; }
}

public sealed class ColavoraSourcesConfig
{
    public string CourierConnectionStringName { get; set; } = "ColavoraCourier";
    public string LoginConnectionStringName { get; set; } = "ColavoraCourierLogin";
}

public sealed class ColavoraOptionsConfig
{
    /// <summary>D3: las Pending con más días que estos se migran como Entregada.</summary>
    public int PendingCloseDays { get; set; } = ColavoraRules.DefaultPendingCloseDays;
    /// <summary>Opción de Luis: días que se dejan abiertas las órdenes; las abiertas (de cualquier estatus) con más días se migran como Entregada. Sin valor, solo se cierran las Pending (PendingCloseDays).</summary>
    public int? OpenOrderDays { get; set; }
    /// <summary>Fecha de la migración (para el corte de Pending); sin ella, ahora.</summary>
    public DateTime? MigrationDateUtc { get; set; }
    public List<string> ExcludeSenders { get; set; } = new();
    public List<string> ExcludeSenderPatterns { get; set; } = new();
    public int BatchSize { get; set; } = 500;
    /// <summary>
    /// P11 (Luis, 2026-10-19): PackageTypeID de Colavora cuyas «piezas» son DESCRIPCIÓN de lo que va (el ítem de AxisCare, tipo 5 Drum):
    /// van como descripción y cantidad de la línea de carga, sin pieza ni label. La factura (tipo IN) va al número de factura. Todo lo demás es pieza.
    /// </summary>
    public List<int> DescriptionPackageTypes { get; set; } = new();
    public int? FromShipmentId { get; set; }
    public int? ToShipmentId { get; set; }
    /// <summary>Solo las órdenes creadas en Colavora desde esta fecha (local de Puerto Rico), para pruebas: --desde-fecha o --meses N.</summary>
    public DateTime? SinceDate { get; set; }
    /// <summary>D10: carpeta donde se guardan firmas y fotos de los comprobantes (archivos en disco). Sin ella no se guardan imágenes.</summary>
    public string? PodFilesDir { get; set; }
    /// <summary>Clave del campo personalizado de la orden para la división de AxisCare (D6).</summary>
    public string DivisionFieldKey { get; set; } = "division";
    /// <summary>Clave del campo personalizado con el ShipmentID de Colavora (rastreo y delta).</summary>
    public string ColavoraIdFieldKey { get; set; } = "colavora_id";
    /// <summary>Dominio para inventar correo a los choferes cuyo username no es correo (p. ej. choferes.advancelogistics.com); sin él, el chofer se crea sin usuario.</summary>
    public string? DriverEmailDomain { get; set; }
}

public sealed class ColavoraReportConfig
{
    public string? OutputDir { get; set; }
    public string? Prefix { get; set; }
}
