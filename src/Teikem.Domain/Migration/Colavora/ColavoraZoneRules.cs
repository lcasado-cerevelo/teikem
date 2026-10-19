using System.Globalization;

namespace Teikem.Domain.Migration.Colavora;

/// <summary>Un consignatario de Colavora para armar zonas: su zona (01–15 en De La Paz; código de chofer en Advance) y su ZIP.</summary>
public sealed record ColavoraConsigneeZone(string? Zone, string? PostalCode);

/// <summary>Una fila de la tabla Zip de Colavora: ZIP, nivel METRO/ISLA, código del chofer de entrega y usuario de entrega.</summary>
public sealed record ColavoraZipRow(string Zip, string? Area, string? DriverCode, int? DeliveryUserId);

/// <summary>Nivel de zona de Teikem (Lote 129: ZoneGroup) planeado desde Zip.Zone de Colavora: METRO / ISLA, con su chofer por omisión.</summary>
public sealed record PlannedZoneGroup(string Code, string Name, int SortOrder, string? DriverCode, int? DriverUserId);

/// <summary>Zona de despacho de Teikem planeada: código, nombre, nivel, sus ZIP y el chofer (código de Colavora) más frecuente.</summary>
public sealed record PlannedDispatchZone(string Code, string Name, string? GroupCode, IReadOnlyList<string> PostalCodes, string? DriverCode, int? DriverUserId);

public sealed record ColavoraZonePlan(IReadOnlyList<PlannedZoneGroup> Groups, IReadOnlyList<PlannedDispatchZone> Zones);

/// <summary>
/// D12 (Luis, 2026-10-18) + Lote 129 (2026-10-19): zonas de ruta por ZIP bajo los niveles Isla/Metro. Cada zona de los
/// consignatarios de Colavora se vuelve una zona de despacho con los ZIP en los que es la zona mayoritaria; su nivel es el
/// METRO/ISLA mayoritario de esos ZIP (tabla Zip) y su chofer el que la tabla Zip asigna con más frecuencia. Cada nivel toma
/// como chofer por omisión el chofer de entrega más frecuente de sus ZIP. Un ZIP solo puede estar en una zona (regla de Teikem).
/// </summary>
public static class ColavoraZoneRules
{
    public const string ZoneCodePrefix = "R";

    /// <summary>Código de zona de Teikem para una zona de Colavora («01» → R01; «823» → R823).</summary>
    public static string ZoneCode(string zone) => ZoneCodePrefix + zone.Trim().ToUpperInvariant();

    /// <summary>Código del nivel a partir de Zip.Zone («METRO», «ISLA»); null si viene vacío.</summary>
    public static string? GroupCode(string? area)
    {
        var a = (area ?? string.Empty).Trim().ToUpperInvariant();
        return a.Length == 0 ? null : a.Length > 20 ? a[..20] : a;
    }

    public static ColavoraZonePlan Build(IEnumerable<ColavoraConsigneeZone> consignees, IEnumerable<ColavoraZipRow> zips)
    {
        var zipRows = zips.Where(z => ColavoraRules.NormalizeZip(z.Zip) is not null)
            .GroupBy(z => ColavoraRules.NormalizeZip(z.Zip)!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // niveles: METRO / ISLA con el chofer de entrega más frecuente de sus ZIP
        var groups = new List<PlannedZoneGroup>();
        var sort = 0;
        foreach (var g in zipRows.Values.GroupBy(z => GroupCode(z.Area)).Where(g => g.Key is not null).OrderBy(g => g.Key == "METRO" ? 0 : 1).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var rows = g.ToList();
            groups.Add(new PlannedZoneGroup(g.Key!, GroupName(g.Key!), ++sort, MostCommon(rows.Select(r => r.DriverCode)), MostCommonInt(rows.Select(r => r.DeliveryUserId))));
        }

        // ZIP → zona ganadora (más consignatarios).
        var votes = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var c in consignees)
        {
            var zone = (c.Zone ?? string.Empty).Trim();
            var zip = ColavoraRules.NormalizeZip(c.PostalCode);
            if (zone.Length == 0 || zip is null) continue;
            if (!votes.TryGetValue(zip, out var byZone)) votes[zip] = byZone = new Dictionary<string, int>(StringComparer.Ordinal);
            byZone[zone] = byZone.GetValueOrDefault(zone) + 1;
        }
        var zipZone = votes.ToDictionary(kv => kv.Key, kv => kv.Value.OrderByDescending(z => z.Value).ThenBy(z => z.Key, StringComparer.Ordinal).First().Key, StringComparer.Ordinal);

        var zones = new List<PlannedDispatchZone>();
        foreach (var g in zipZone.GroupBy(kv => kv.Value, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var zoneZips = g.Select(kv => kv.Key).OrderBy(z => z, StringComparer.Ordinal).ToList();
            var rows = zoneZips.Select(z => zipRows.GetValueOrDefault(z)).Where(r => r is not null).Select(r => r!).ToList();
            var driverCode = MostCommon(rows.Select(r => r.DriverCode));
            var driverUser = MostCommonInt(rows.Select(r => r.DeliveryUserId));
            var group = MostCommon(rows.Select(r => GroupCode(r.Area)));
            zones.Add(new PlannedDispatchZone(ZoneCode(g.Key), ZoneName(g.Key, driverCode), group, zoneZips, driverCode, driverUser));
        }
        return new ColavoraZonePlan(groups, zones);
    }

    private static string? MostCommon(IEnumerable<string?> values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v)).GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key).FirstOrDefault();

    private static int? MostCommonInt(IEnumerable<int?> values)
        => values.Where(v => v is > 0).GroupBy(v => v!.Value).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).Select(x => (int?)x.Key).FirstOrDefault();

    /// <summary>Nombre legible: «Ruta 01» o «Ruta 823 (chofer 823)».</summary>
    public static string ZoneName(string zone, string? driverCode)
    {
        var z = zone.Trim();
        var name = int.TryParse(z, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? $"Ruta {z}" : $"Zona {z}";
        return string.IsNullOrWhiteSpace(driverCode) || string.Equals(driverCode.Trim(), z, StringComparison.OrdinalIgnoreCase) ? name : $"{name} (chofer {driverCode.Trim()})";
    }

    public static string GroupName(string code) => code.ToUpperInvariant() switch { "METRO" => "Metro", "ISLA" => "Isla", _ => code };
}
