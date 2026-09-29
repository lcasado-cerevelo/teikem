using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Teikem.Infrastructure.Migration;

/// <summary>Ítem del WMS (dbo.Item).</summary>
public sealed record WmsItem(string ItemId, string? Description);

/// <summary>Posición del WMS (dbo.Location). OnHold = LocationHold.</summary>
public sealed record WmsLocation(string LocationId, int LocationType, string? Description, bool OnHold);

/// <summary>Existencia del WMS por posición (dbo.Inventory, solo OnHandQuantity distinta de cero).</summary>
public sealed record WmsInventoryRow(string LocationId, string ItemId, decimal OnHandQuantity);

/// <summary>Código de barras del WMS (dbo.ItemUPC).</summary>
public sealed record WmsUpc(string ItemId, string Upc);

/// <summary>
/// Lote 10 (P2): lector de SOLO LECTURA de la base MSWM del WMS anterior, por una cadena de conexión propia
/// (ConnectionStrings:LegacyMswm). Solo ejecuta SELECT parametrizados por @warehouseId; nunca escribe.
/// Los textos vacíos del WMS vienen como '§' y se convierten a null; las filas sin id se descartan.
/// </summary>
public sealed class MswmReader(string connectionString)
{
    public const int CommandTimeoutSeconds = 120;

    /// <summary>Marcador de texto vacío del WMS.</summary>
    public const string EmptyMarker = "§";

    public const string ItemsSql =
        "SELECT ItemId, Description FROM dbo.Item WHERE WarehouseId = @warehouseId";

    public const string LocationsSql =
        "SELECT LocationId, LocationType, Description, LocationHold FROM dbo.Location WHERE WarehouseId = @warehouseId";

    public const string InventorySql =
        "SELECT LocationId, ItemId, OnHandQuantity FROM dbo.Inventory WHERE WarehouseId = @warehouseId AND OnHandQuantity <> 0";

    public const string UpcsSql =
        "SELECT ItemId, UPC FROM dbo.ItemUPC WHERE WarehouseId = @warehouseId";

    /// <summary>Todas las consultas que ejecuta el lector (para verificar que solo son SELECT por @warehouseId).</summary>
    public static IReadOnlyList<string> AllQueries { get; } = new[] { ItemsSql, LocationsSql, InventorySql, UpcsSql };

    /// <summary>Recorta y convierte '§' o vacío a null.</summary>
    public static string? Clean(object? value)
    {
        if (value is null || value is DBNull) return null;
        var s = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(s) || s == EmptyMarker ? null : s;
    }

    public Task<IReadOnlyList<WmsItem>> ReadItemsAsync(string warehouseId, CancellationToken ct = default)
        => QueryAsync(ItemsSql, warehouseId, r =>
            Clean(r["ItemId"]) is { } id ? new WmsItem(id, Clean(r["Description"])) : null, ct);

    public Task<IReadOnlyList<WmsLocation>> ReadLocationsAsync(string warehouseId, CancellationToken ct = default)
        => QueryAsync(LocationsSql, warehouseId, r =>
            Clean(r["LocationId"]) is { } id
                ? new WmsLocation(
                    id,
                    r["LocationType"] is DBNull ? 0 : Convert.ToInt32(r["LocationType"], CultureInfo.InvariantCulture),
                    Clean(r["Description"]),
                    r["LocationHold"] is not DBNull && Convert.ToBoolean(r["LocationHold"], CultureInfo.InvariantCulture))
                : null, ct);

    public Task<IReadOnlyList<WmsInventoryRow>> ReadInventoryAsync(string warehouseId, CancellationToken ct = default)
        => QueryAsync(InventorySql, warehouseId, r =>
            Clean(r["LocationId"]) is { } loc && Clean(r["ItemId"]) is { } item
                ? new WmsInventoryRow(loc, item, Convert.ToDecimal(r["OnHandQuantity"], CultureInfo.InvariantCulture))
                : null, ct);

    public Task<IReadOnlyList<WmsUpc>> ReadUpcsAsync(string warehouseId, CancellationToken ct = default)
        => QueryAsync(UpcsSql, warehouseId, r =>
            Clean(r["ItemId"]) is { } id && Clean(r["UPC"]) is { } upc ? new WmsUpc(id, upc) : null, ct);

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, string warehouseId, Func<SqlDataReader, T?> map, CancellationToken ct)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Falta la cadena de conexión de la base MSWM.");

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.Parameters.Add(new SqlParameter("@warehouseId", System.Data.SqlDbType.NVarChar, Math.Max(50, warehouseId.Length)) { Value = warehouseId });

        var list = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (map(reader) is { } row) list.Add(row);
        return list;
    }
}
