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
/// Lote 11 (cupo de posiciones): total de unidades de una posición del WMS en una "foto" de su historial. Source = tabla de
/// origen (WmsBinHistorySources); una fila por foto y posición (inventario actual o anterior: una foto; conteo cíclico: una por
/// solicitud e iteración; historial de conteo: una por solicitud; acomodos: una por día). Solo totales &gt; 0.
/// </summary>
public sealed record WmsBinQuantity(string Source, string LocationId, decimal Quantity);

/// <summary>Tablas del WMS de las que sale el historial de existencias por posición (columna Source de WmsBinQuantity).</summary>
public static class WmsBinHistorySources
{
    public const string Inventory = "Inventory";
    public const string InventoryOld = "Inventory_Old";
    public const string CycleCountInventory = "CycleCountInventory";
    public const string CycleCountHistory = "CycleCountHistory";
    public const string PutAwayHistory = "PutAwayHistory";
}

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

    // Lote 11 (cupo de posiciones): historial de existencias por posición, ya sumado por foto en SQL (totales > 0).

    /// <summary>Inventario actual: una foto (total en mano por posición).</summary>
    public const string InventoryPhotoSql =
        "SELECT LocationId, SUM(OnHandQuantity) AS Quantity FROM dbo.Inventory WHERE WarehouseId = @warehouseId "
        + "GROUP BY LocationId HAVING SUM(OnHandQuantity) > 0";

    /// <summary>Inventario anterior (Inventory_Old): una foto.</summary>
    public const string InventoryOldPhotoSql =
        "SELECT LocationId, SUM(OnHandQuantity) AS Quantity FROM dbo.Inventory_Old WHERE WarehouseId = @warehouseId "
        + "GROUP BY LocationId HAVING SUM(OnHandQuantity) > 0";

    /// <summary>Conteos cíclicos: una foto por solicitud e iteración (lo contado, CountQuantity).</summary>
    public const string CycleCountPhotoSql =
        "SELECT LocationId, SUM(CountQuantity) AS Quantity FROM dbo.CycleCountInventory WHERE WarehouseId = @warehouseId "
        + "GROUP BY Request, Iteration, LocationId HAVING SUM(CountQuantity) > 0";

    /// <summary>
    /// Historial de conteo: una foto por solicitud. En MSWM la posición contada está en LocationIdCounted (LocationId viene
    /// vacío) y OnHandQuantity es siempre 0: la cantidad resultante del conteo es OnHandQuantity + AdjustmentQuantity. Las
    /// filas revertidas no cuentan.
    /// </summary>
    public const string CycleCountHistoryPhotoSql =
        "SELECT LocationIdCounted, LocationId, SUM(OnHandQuantity + AdjustmentQuantity) AS Quantity FROM dbo.CycleCountHistory "
        + "WHERE WarehouseId = @warehouseId AND (Reversed IS NULL OR Reversed = 0) "
        + "GROUP BY Request, LocationIdCounted, LocationId HAVING SUM(OnHandQuantity + AdjustmentQuantity) > 0";

    /// <summary>Acomodos: lo acomodado HACIA la posición (LocationTo), sumado por día.</summary>
    public const string PutAwayPhotoSql =
        "SELECT LocationTo AS LocationId, SUM(Quantity) AS Quantity FROM dbo.PutAwayHistory WHERE WarehouseId = @warehouseId "
        + "GROUP BY LocationTo, CAST(TransDate AS date) HAVING SUM(Quantity) > 0";

    /// <summary>Consultas del historial por posición, con la tabla de origen.</summary>
    public static IReadOnlyList<(string Source, string Sql)> BinHistoryQueries { get; } = new[]
    {
        (WmsBinHistorySources.Inventory, InventoryPhotoSql),
        (WmsBinHistorySources.InventoryOld, InventoryOldPhotoSql),
        (WmsBinHistorySources.CycleCountInventory, CycleCountPhotoSql),
        (WmsBinHistorySources.CycleCountHistory, CycleCountHistoryPhotoSql),
        (WmsBinHistorySources.PutAwayHistory, PutAwayPhotoSql),
    };

    /// <summary>Todas las consultas que ejecuta el lector (para verificar que solo son SELECT por @warehouseId).</summary>
    public static IReadOnlyList<string> AllQueries { get; } =
        new[] { ItemsSql, LocationsSql, InventorySql, UpcsSql }.Concat(BinHistoryQueries.Select(q => q.Sql)).ToArray();

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

    /// <summary>
    /// Lote 11: historial de existencias por posición de las cinco tablas (una fila por foto y posición, totales &gt; 0). En el
    /// historial de conteo la posición es LocationIdCounted y, si viene vacía, LocationId.
    /// </summary>
    public async Task<IReadOnlyList<WmsBinQuantity>> ReadBinHistoryAsync(string warehouseId, CancellationToken ct = default)
    {
        var all = new List<WmsBinQuantity>();
        foreach (var (source, sql) in BinHistoryQueries)
        {
            var rows = await QueryAsync(sql, warehouseId, r =>
            {
                var location = source == WmsBinHistorySources.CycleCountHistory
                    ? Clean(r["LocationIdCounted"]) ?? Clean(r["LocationId"])
                    : Clean(r["LocationId"]);
                return location is null || r["Quantity"] is DBNull
                    ? null
                    : new WmsBinQuantity(source, location, Convert.ToDecimal(r["Quantity"], CultureInfo.InvariantCulture));
            }, ct);
            all.AddRange(rows);
        }
        return all;
    }

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
