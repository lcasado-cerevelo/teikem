using Teikem.Domain.Orders;

namespace Teikem.Domain.Wms;

/// <summary>
/// 2026-10-21 — Plantillas de importación de POSICIONES de almacén (4.ª entidad del motor genérico). El almacén (warehouseCode) y la zona (zoneCode) van en una columna o como valor por defecto (un archivo
/// de una sola zona); la posición trae su código (code) o sus partes (aisle, rack, level, position, de las que sale el código). Código y partes no admiten valor por defecto.
/// </summary>
public static class BinImportFields
{
    public const string WarehouseCode = "warehouseCode";
    public const string ZoneCode = "zoneCode";
    public const string Code = "code";
    public const string Aisle = "aisle";
    public const string Rack = "rack";
    public const string Level = "level";
    public const string Position = "position";
    public const string MaxWeightKg = "maxWeightKg";
    public const string MaxCapacityQty = "maxCapacityQty";

    public static readonly IReadOnlyList<string> All = new[] { WarehouseCode, ZoneCode, Code, Aisle, Rack, Level, Position, MaxWeightKg, MaxCapacityQty };
    public static readonly IReadOnlySet<string> NoDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Code, Aisle, Rack, Level, Position };
    public const string WarehouseMessage = "La plantilla debe incluir la columna warehouseCode o un valor por defecto para warehouseCode.";
    public const string ZoneMessage = "La plantilla debe incluir la columna zoneCode o un valor por defecto para zoneCode.";
    public const string CodeMessage = "La plantilla debe incluir la columna code o las columnas de la posición (aisle, rack, level, position).";

    public static readonly ImportEntityMeta Meta = new(ImportKinds.Bin,
        All.Select(f => new ImportFieldSpec(f, Required: f is WarehouseCode or ZoneCode, DefaultAllowed: !NoDefault.Contains(f))).ToList(),
        new[]
        {
            new ImportRequirement(new[] { WarehouseCode }, AllowDefault: true, WarehouseMessage),
            new ImportRequirement(new[] { ZoneCode }, AllowDefault: true, ZoneMessage),
            new ImportRequirement(new[] { Code, Aisle, Rack, Level, Position }, AllowDefault: false, CodeMessage),
        });
}
