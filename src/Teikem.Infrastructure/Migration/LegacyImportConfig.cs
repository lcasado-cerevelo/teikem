using System.Text.Json;
using System.Text.Json.Serialization;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Migration;

/// <summary>
/// Lote 10 (P2): modelo del JSON de configuración del comando <c>import-legacy</c> (una compañía por archivo).
/// Se lee con System.Text.Json en camelCase, admitiendo comentarios y comas finales. Las rutas de los archivos fuente
/// pueden ser absolutas o relativas a la carpeta del JSON; <see cref="Load"/> las deja resueltas a rutas absolutas.
/// </summary>
public sealed class LegacyImportConfig
{
    public const string SourceMswm = "mswm";
    public const string SourceQuickBooks = "quickbooks";
    public const string SourceNone = "none";

    public LegacyCompanyConfig Company { get; set; } = new();
    public LegacySourcesConfig Sources { get; set; } = new();
    public LegacyProductsConfig Products { get; set; } = new();
    public LegacySuppliersConfig Suppliers { get; set; } = new();
    public LegacyClientsConfig Clients { get; set; } = new();
    public LegacyWarehouseConfig Warehouse { get; set; } = new();
    public LegacyOpeningBalancesConfig OpeningBalances { get; set; } = new();
    public LegacyReportConfig Report { get; set; } = new();

    /// <summary>Ruta absoluta del JSON leído (null si la configuración se armó en memoria).</summary>
    [JsonIgnore] public string? ConfigPath { get; private set; }

    /// <summary>Carpeta contra la que se resolvieron las rutas relativas.</summary>
    [JsonIgnore] public string? BaseDirectory { get; private set; }

    /// <summary>Carpeta de salida del reporte ya resuelta: report.outputDir o, si falta, la carpeta del primer archivo fuente.</summary>
    [JsonIgnore]
    public string ResolvedOutputDir =>
        !string.IsNullOrWhiteSpace(Report.OutputDir)
            ? Report.OutputDir!
            : Path.GetDirectoryName(Sources.Products ?? Sources.Customers ?? Sources.Vendors ?? string.Empty) is { Length: > 0 } dir
                ? dir
                : BaseDirectory ?? Directory.GetCurrentDirectory();

    /// <summary>Prefijo del reporte ya resuelto (report.prefix o 'migracion').</summary>
    [JsonIgnore]
    public string ResolvedPrefix => string.IsNullOrWhiteSpace(Report.Prefix) ? "migracion" : Report.Prefix!.Trim();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Lee, resuelve rutas y valida el JSON. Lanza <see cref="ValidationException"/> con el mensaje en español.</summary>
    public static LegacyImportConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("Falta la ruta del archivo de configuración.");
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new ValidationException(FileNotFound(fullPath));
        var cfg = Parse(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath)!);
        cfg.ConfigPath = fullPath;
        return cfg;
    }

    /// <summary>
    /// Deserializa y valida el contenido JSON resolviendo las rutas relativas contra <paramref name="baseDirectory"/>.
    /// Con <paramref name="checkFiles"/> = false no verifica que los archivos existan (útil en pruebas).
    /// </summary>
    public static LegacyImportConfig Parse(string json, string baseDirectory, bool checkFiles = true)
    {
        LegacyImportConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<LegacyImportConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ValidationException($"El archivo de configuración no es un JSON válido: {ex.Message}");
        }
        if (cfg is null) throw new ValidationException("El archivo de configuración está vacío.");

        cfg.BaseDirectory = Path.GetFullPath(baseDirectory);
        cfg.Normalize();
        cfg.Validate(checkFiles);
        return cfg;
    }

    /// <summary>Mensaje exacto de archivo inexistente.</summary>
    public static string FileNotFound(string path) => $"El archivo no existe: {path}.";

    /// <summary>Rellena nulos con sus valores por defecto, resuelve rutas y normaliza textos y diccionarios.</summary>
    private void Normalize()
    {
        Company ??= new();
        Sources ??= new();
        Products ??= new();
        Suppliers ??= new();
        Clients ??= new();
        Warehouse ??= new();
        OpeningBalances ??= new();
        Report ??= new();

        Company.Modules ??= new();
        Company.Name = Company.Name?.Trim();

        Sources.Products = ResolvePath(Sources.Products);
        Sources.Customers = ResolvePath(Sources.Customers);
        Sources.Vendors = ResolvePath(Sources.Vendors);
        if (Sources.ExtraProducts is not null) Sources.ExtraProducts.Path = ResolvePath(Sources.ExtraProducts.Path);

        Products.Types ??= new() { "Inventory Part", "Inventory Assembly" };
        Products.ExcludeQuickBooksCategories ??= new();
        Products.ExcludeSkus ??= new();
        Products.CategoryByQuickBooksCategory = new Dictionary<string, string>(
            Products.CategoryByQuickBooksCategory ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(Products.BaseUom)) Products.BaseUom = "UN";
        if (string.IsNullOrWhiteSpace(Products.TrackingType)) Products.TrackingType = "NONE";

        Suppliers.ExcludeNames ??= new();
        Clients.ExcludeNames ??= new();
        if (string.IsNullOrWhiteSpace(Clients.DefaultCurrency)) Clients.DefaultCurrency = "USD";

        Warehouse.Zones ??= new();
        Warehouse.SkipLocationIds ??= new();
        foreach (var z in Warehouse.Zones) z.MatchLocationIds ??= new();
        if (Warehouse.SingleBin?.Zone is { } sz) sz.MatchLocationIds ??= new();
        if (string.IsNullOrWhiteSpace(Warehouse.Country)) Warehouse.Country = "PR";

        OpeningBalances.Source = string.IsNullOrWhiteSpace(OpeningBalances.Source)
            ? SourceNone
            : OpeningBalances.Source.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(OpeningBalances.Reason)) OpeningBalances.Reason = "OPENING_BALANCE";

        if (!string.IsNullOrWhiteSpace(Report.OutputDir)) Report.OutputDir = ResolvePath(Report.OutputDir);
    }

    private string? ResolvePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        var trimmed = p.Trim();
        return Path.IsPathRooted(trimmed) ? Path.GetFullPath(trimmed) : Path.GetFullPath(Path.Combine(BaseDirectory!, trimmed));
    }

    /// <summary>Validación en el orden del plan; el primer error se lanza como <see cref="ValidationException"/>.</summary>
    private void Validate(bool checkFiles)
    {
        if (string.IsNullOrWhiteSpace(Company.Name)) throw new ValidationException("company.name es obligatorio.");
        if (string.IsNullOrWhiteSpace(Sources.Products)) throw new ValidationException("sources.products es obligatorio.");

        if (checkFiles)
        {
            foreach (var file in new[] { Sources.Products, Sources.Customers, Sources.Vendors, Sources.ExtraProducts?.Path })
                if (file is not null && !File.Exists(file)) throw new ValidationException(FileNotFound(file));
        }

        if (OpeningBalances.Source is not (SourceMswm or SourceQuickBooks or SourceNone))
            throw new ValidationException("openingBalances.source debe ser mswm, quickbooks o none.");

        // Lote 16: modo de recepción del almacén (opcional; PUTAWAY o DIRECT).
        if (ReceivingModeRules.ParseMode(Warehouse.ReceivingMode).Error is not null)
            throw new ValidationException("warehouse.receivingMode debe ser PUTAWAY o DIRECT.");

        if (OpeningBalances.Source == SourceMswm
            && (string.IsNullOrWhiteSpace(Sources.Mswm?.ConnectionStringName) || string.IsNullOrWhiteSpace(Sources.Mswm?.WarehouseId)))
            throw new ValidationException(
                "Con openingBalances.source=mswm se requiere sources.mswm.connectionStringName y sources.mswm.warehouseId.");
    }
}

/// <summary>Compañía (tenant) destino: se busca por nombre y, si no existe, se aprovisiona.</summary>
public sealed class LegacyCompanyConfig
{
    public string? Name { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string Lang { get; set; } = "es";
    public List<string> Modules { get; set; } = new();
    public string? AdminEmail { get; set; }
    public string? AdminFullName { get; set; }
}

/// <summary>Archivos fuente de QuickBooks y, opcionalmente, la base MSWM del WMS.</summary>
public sealed class LegacySourcesConfig
{
    public string? Products { get; set; }
    public string? Customers { get; set; }
    public string? Vendors { get; set; }
    public LegacyExtraProductsConfig? ExtraProducts { get; set; }
    public LegacyMswmConfig? Mswm { get; set; }
}

/// <summary>Archivo de ítems adicional del que se toman los SKU ausentes cuya categoría coincide con whereCategory.</summary>
public sealed class LegacyExtraProductsConfig
{
    public string? Path { get; set; }
    public string? WhereCategory { get; set; }
}

/// <summary>Conexión de solo lectura a MSWM: nombre de la cadena en ConnectionStrings y almacén del WMS.</summary>
public sealed class LegacyMswmConfig
{
    public string? ConnectionStringName { get; set; }
    public string? WarehouseId { get; set; }
}

public sealed class LegacyProductsConfig
{
    public List<string> Types { get; set; } = new() { "Inventory Part", "Inventory Assembly" };
    public string? DefaultCategory { get; set; }
    /// <summary>true = la categoría del producto es su fabricante (columna Manufacturer / MANUFACTERS del archivo de ítems); vacío = defaultCategory.</summary>
    public bool CategoryFromManufacturer { get; set; }
    public Dictionary<string, string> CategoryByQuickBooksCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ExcludeQuickBooksCategories { get; set; } = new();
    public List<string> ExcludeSkus { get; set; } = new();
    public string BaseUom { get; set; } = "UN";
    public string TrackingType { get; set; } = "NONE";
    public bool NameFallbackToSku { get; set; } = true;
    public bool CreateUnknownWmsSkusWithStock { get; set; }
}

public sealed class LegacySuppliersConfig
{
    /// <summary>null = todos los proveedores del archivo.</summary>
    public List<string>? IncludeNames { get; set; }
    public List<string> ExcludeNames { get; set; } = new();
}

public sealed class LegacyClientsConfig
{
    public List<string> ExcludeNames { get; set; } = new();
    public string DefaultCurrency { get; set; } = "USD";
    public bool RepCustomField { get; set; } = true;
    public bool QbCodeCustomField { get; set; } = true;
}

public sealed class LegacyWarehouseConfig
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Line1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "PR";
    public List<LegacyZoneConfig> Zones { get; set; } = new();
    public List<string> SkipLocationIds { get; set; } = new();
    public LegacySingleBinConfig? SingleBin { get; set; }
    /// <summary>Lote 16 (D8): modo de recepción PUTAWAY | DIRECT (opcional; sin él, PUTAWAY). Solo al crear el almacén: --update no lo pisa.</summary>
    public string? ReceivingMode { get; set; }
    /// <summary>
    /// Lote 16 (D12): código de la posición de recepción por defecto (de una zona STAGING o CROSSDOCK de este almacén). Solo al
    /// crear el almacén (después de crear sus posiciones); --update no la pisa.
    /// </summary>
    public string? DefaultReceivingBin { get; set; }
}

/// <summary>Zona destino y cómo se le asignan las posiciones del WMS (por descripción o por id exacto).</summary>
public sealed class LegacyZoneConfig
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? ZoneType { get; set; }
    public string? MatchDescription { get; set; }
    public List<string> MatchLocationIds { get; set; } = new();
}

/// <summary>Almacén sin ubicaciones de origen: una única zona con una única posición.</summary>
public sealed class LegacySingleBinConfig
{
    public LegacyZoneConfig? Zone { get; set; }
    public string? Bin { get; set; }
}

public sealed class LegacyOpeningBalancesConfig
{
    /// <summary>mswm | quickbooks | none (se normaliza a minúsculas).</summary>
    public string Source { get; set; } = LegacyImportConfig.SourceNone;
    public string Reason { get; set; } = "OPENING_BALANCE";
    public string? Notes { get; set; }
}

public sealed class LegacyReportConfig
{
    /// <summary>Opcional: si falta, el reporte queda en la carpeta del primer archivo fuente.</summary>
    public string? OutputDir { get; set; }
    public string? Prefix { get; set; }
}
