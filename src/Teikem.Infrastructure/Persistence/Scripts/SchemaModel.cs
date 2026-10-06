using Microsoft.Data.SqlClient;

namespace Teikem.Infrastructure.Persistence.Scripts;

// Modelo mínimo del esquema (lo que usa el script de estructura): tablas, columnas, claves, índices, checks, FKs y vistas.
// Lo lee SchemaReader del catálogo de SQL Server y lo compara SchemaDiffer (db-update, 2026-10-06).

public sealed record ColumnDef(
    string Name,
    string TypeSql,
    bool IsNullable,
    bool IsIdentity,
    long IdentitySeed,
    long IdentityIncrement,
    string? ComputedExpr,
    bool IsPersisted,
    string? Collation,
    string? DefaultName,
    string? DefaultDef,
    bool DefaultSystemNamed);

public sealed record KeyDef(string Name, bool SystemNamed, bool IsPrimary, string Clustering, IReadOnlyList<string> Columns);

public sealed record IndexColumn(string Name, bool Descending);

public sealed record IndexDef(string Name, bool IsUnique, string Clustering, IReadOnlyList<IndexColumn> Keys, IReadOnlyList<string> Includes, string? Filter);

public sealed record CheckDef(string Name, bool SystemNamed, string Definition);

public sealed record FkDef(
    string Name,
    bool SystemNamed,
    IReadOnlyList<string> Columns,
    string RefTable,
    IReadOnlyList<string> RefColumns,
    string DeleteAction,
    string UpdateAction);

public sealed class TableDef(string schema, string name)
{
    public string Schema { get; } = schema;
    public string Name { get; } = name;
    public string Key => $"{Schema}.{Name}";
    public List<ColumnDef> Columns { get; } = [];
    public KeyDef? Primary { get; set; }
    public List<KeyDef> Uniques { get; } = [];
    public List<IndexDef> Indexes { get; } = [];
    public List<CheckDef> Checks { get; } = [];
    public List<FkDef> Fks { get; } = [];
}

public sealed record ViewDef(string Schema, string Name, string Definition)
{
    public string Key => $"{Schema}.{Name}";
}

public sealed class SchemaModel
{
    public Dictionary<string, TableDef> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ViewDef> Views { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tablas sin filas (clave «esquema.tabla»); solo se llena en la base real. Permite retirar columnas obsoletas sin perder datos.</summary>
    public HashSet<string> EmptyTables { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Lee el esquema de la base de la conexión abierta (sin la tabla de control dbo.__SchemaVersion).</summary>
public static class SchemaReader
{
    public static async Task<SchemaModel> ReadAsync(SqlConnection conn, CancellationToken ct = default)
    {
        var model = new SchemaModel();
        var dbCollation = (string?)await ScalarAsync(conn, "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS NVARCHAR(128))", ct);

        // Columnas (con identidad, calculadas y su default).
        await ReadAsync(conn, """
            SELECT s.name, t.name, c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
                   CAST(ic.seed_value AS BIGINT), CAST(ic.increment_value AS BIGINT), c.is_computed, cc.definition, cc.is_persisted,
                   c.collation_name, dc.name, dc.definition, dc.is_system_named
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE t.is_ms_shipped = 0 AND t.name <> '__SchemaVersion'
            ORDER BY s.name, t.name, c.column_id
            """, ct, r =>
        {
            var table = GetOrAdd(model, r.GetString(0), r.GetString(1));
            var typeName = r.GetString(3);
            var collation = r.IsDBNull(14) ? null : r.GetString(14);
            if (collation is not null && string.Equals(collation, dbCollation, StringComparison.OrdinalIgnoreCase)) collation = null;
            table.Columns.Add(new ColumnDef(
                r.GetString(2),
                FormatType(typeName, r.GetInt16(4), r.GetByte(5), r.GetByte(6)),
                r.GetBoolean(7),
                r.GetBoolean(8),
                r.IsDBNull(9) ? 1 : r.GetInt64(9),
                r.IsDBNull(10) ? 1 : r.GetInt64(10),
                r.GetBoolean(11) && !r.IsDBNull(12) ? r.GetString(12) : null,
                !r.IsDBNull(13) && r.GetBoolean(13),
                collation,
                r.IsDBNull(15) ? null : r.GetString(15),
                r.IsDBNull(16) ? null : r.GetString(16),
                !r.IsDBNull(17) && r.GetBoolean(17)));
        });

        // Índices, claves primarias y restricciones únicas (una fila por columna de índice).
        var indexRows = new List<(string Schema, string Table, int IndexId, string Name, string Clustering, bool Unique, bool Primary, bool UniqueConstraint, string? Filter, bool SystemNamed, string Column, bool Desc, bool Included)>();
        await ReadAsync(conn, """
            SELECT s.name, t.name, i.index_id, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint, i.filter_definition,
                   ISNULL(kc.is_system_named, 0), c.name, ic.is_descending_key, ic.is_included_column
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            LEFT JOIN sys.key_constraints kc ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
            WHERE i.type IN (1, 2) AND i.is_hypothetical = 0 AND t.is_ms_shipped = 0 AND t.name <> '__SchemaVersion'
            ORDER BY s.name, t.name, i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id
            """, ct, r => indexRows.Add((r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetBoolean(5), r.GetBoolean(6), r.GetBoolean(7),
            r.IsDBNull(8) ? null : r.GetString(8), r.GetBoolean(9), r.GetString(10), r.GetBoolean(11), r.GetBoolean(12))));
        foreach (var g in indexRows.GroupBy(x => (x.Schema, x.Table, x.IndexId)))
        {
            var first = g.First();
            var table = GetOrAdd(model, first.Schema, first.Table);
            var keyCols = g.Where(x => !x.Included).ToList();
            if (first.Primary)
                table.Primary = new KeyDef(first.Name, first.SystemNamed, true, first.Clustering, keyCols.Select(x => x.Column).ToList());
            else if (first.UniqueConstraint)
                table.Uniques.Add(new KeyDef(first.Name, first.SystemNamed, false, first.Clustering, keyCols.Select(x => x.Column).ToList()));
            else
                table.Indexes.Add(new IndexDef(first.Name, first.Unique, first.Clustering, keyCols.Select(x => new IndexColumn(x.Column, x.Desc)).ToList(),
                    g.Where(x => x.Included).Select(x => x.Column).ToList(), first.Filter));
        }

        await ReadAsync(conn, """
            SELECT s.name, t.name, cc.name, cc.is_system_named, cc.definition
            FROM sys.check_constraints cc
            JOIN sys.tables t ON t.object_id = cc.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0 AND t.name <> '__SchemaVersion'
            ORDER BY s.name, t.name, cc.name
            """, ct, r => GetOrAdd(model, r.GetString(0), r.GetString(1)).Checks.Add(new CheckDef(r.GetString(2), r.GetBoolean(3), r.GetString(4))));

        var fkRows = new List<(int FkId, string Schema, string Table, string Name, bool SystemNamed, string Del, string Upd, string RefSchema, string RefTable, string Col, string RefCol)>();
        await ReadAsync(conn, """
            SELECT fk.object_id, s.name, t.name, fk.name, fk.is_system_named, fk.delete_referential_action_desc, fk.update_referential_action_desc,
                   rs.name, rt.name, pc.name, rc.name
            FROM sys.foreign_keys fk
            JOIN sys.tables t ON t.object_id = fk.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE t.is_ms_shipped = 0 AND t.name <> '__SchemaVersion'
            ORDER BY fk.object_id, fkc.constraint_column_id
            """, ct, r => fkRows.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10))));
        foreach (var g in fkRows.GroupBy(x => x.FkId))
        {
            var f = g.First();
            GetOrAdd(model, f.Schema, f.Table).Fks.Add(new FkDef(f.Name, f.SystemNamed, g.Select(x => x.Col).ToList(), $"{f.RefSchema}.{f.RefTable}", g.Select(x => x.RefCol).ToList(), f.Del, f.Upd));
        }

        await ReadAsync(conn, """
            SELECT s.name, v.name, m.definition
            FROM sys.views v
            JOIN sys.schemas s ON s.schema_id = v.schema_id
            JOIN sys.sql_modules m ON m.object_id = v.object_id
            WHERE v.is_ms_shipped = 0
            """, ct, r =>
        {
            var v = new ViewDef(r.GetString(0), r.GetString(1), r.GetString(2));
            model.Views[v.Key] = v;
        });

        try
        {
            await ReadAsync(conn, """
                SELECT s.name, t.name
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE t.is_ms_shipped = 0
                  AND NOT EXISTS (SELECT 1 FROM sys.dm_db_partition_stats p WHERE p.object_id = t.object_id AND p.index_id IN (0, 1) AND p.row_count > 0)
                """, ct, r => model.EmptyTables.Add($"{r.GetString(0)}.{r.GetString(1)}"));
        }
        catch (SqlException) { /* sin permiso de estado de la base: no se retira nada automáticamente */ }
        return model;
    }

    private static TableDef GetOrAdd(SchemaModel model, string schema, string name)
    {
        var key = $"{schema}.{name}";
        if (!model.Tables.TryGetValue(key, out var t)) model.Tables[key] = t = new TableDef(schema, name);
        return t;
    }

    /// <summary>Tipo de columna tal como se escribe en DDL (longitud, precisión y escala incluidas).</summary>
    public static string FormatType(string typeName, int maxLength, int precision, int scale)
    {
        var t = typeName.ToLowerInvariant();
        return t switch
        {
            "nvarchar" or "nchar" => $"{t.ToUpperInvariant()}({(maxLength == -1 ? "MAX" : (maxLength / 2).ToString())})",
            "varchar" or "char" or "varbinary" or "binary" => $"{t.ToUpperInvariant()}({(maxLength == -1 ? "MAX" : maxLength.ToString())})",
            "decimal" or "numeric" => $"{t.ToUpperInvariant()}({precision},{scale})",
            "datetime2" or "datetimeoffset" or "time" => $"{t.ToUpperInvariant()}({scale})",
            "timestamp" => "ROWVERSION",
            _ => t.ToUpperInvariant(),
        };
    }

    private static async Task<object?> ScalarAsync(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static async Task ReadAsync(SqlConnection conn, string sql, CancellationToken ct, Action<SqlDataReader> row)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 300;
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) row(r);
    }
}
