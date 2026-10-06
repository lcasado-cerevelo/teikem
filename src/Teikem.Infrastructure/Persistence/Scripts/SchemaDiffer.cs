using System.Text;
using System.Text.RegularExpressions;

namespace Teikem.Infrastructure.Persistence.Scripts;

/// <summary>Un cambio de esquema por aplicar. Phase ordena la ejecución (ver SchemaDiffer).</summary>
public sealed record SchemaChange(int Phase, string Description, string Sql);

public sealed class SchemaDiffResult
{
    public List<SchemaChange> Changes { get; } = [];
    /// <summary>Diferencias que NO se aplican solas (por ejemplo, volver NOT NULL una columna con datos): revisión manual.</summary>
    public List<string> Warnings { get; } = [];
    /// <summary>Objetos que existen en la base pero ya no en el script; nunca se borran.</summary>
    public List<string> Extras { get; } = [];
}

/// <summary>
/// Compara el esquema deseado (el script de estructura aplicado a una base vacía) contra el de una base existente y genera
/// SOLO lo que falta: tablas, columnas, claves, índices, checks, defaults, FKs y vistas. Nunca borra tablas ni columnas.
/// Fases: 1 tablas nuevas · 2 columnas nuevas · 3 retiro de objetos cambiados y ajustes de columnas · 4 PK/únicas · 5 índices ·
/// 6 checks y defaults · 8 FKs · 9 vistas. Las claves foráneas, los checks y los defaults sin nombre propio se comparan por
/// firma (columnas/definición), no por el nombre que les puso SQL Server.
/// </summary>
public static partial class SchemaDiffer
{
    public static SchemaDiffResult Diff(SchemaModel desired, SchemaModel actual)
    {
        var result = new SchemaDiffResult();
        foreach (var d in desired.Tables.Values.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!actual.Tables.TryGetValue(d.Key, out var a))
            {
                AddNewTable(result, d);
                continue;
            }
            DiffColumns(result, d, a, actual);
            DiffKeys(result, d, a);
            DiffIndexes(result, d, a);
            DiffChecks(result, d, a);
            DiffFks(result, d, a);
        }
        foreach (var a in actual.Tables.Keys.Where(k => !desired.Tables.ContainsKey(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            result.Extras.Add($"Tabla {a}");
        foreach (var v in desired.Views.Values.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!actual.Views.TryGetValue(v.Key, out var av))
                result.Changes.Add(new SchemaChange(9, $"Crear vista {v.Key}", v.Definition));
            else if (Normalize(v.Definition) != Normalize(av.Definition))
                result.Changes.Add(new SchemaChange(9, $"Actualizar vista {v.Key}", CreateToAlter().Replace(v.Definition, "ALTER VIEW", 1)));
        }
        return Stable(result);
    }

    // List<T>.Sort no es estable; se conserva el orden de generación dentro de cada fase.
    private static SchemaDiffResult Stable(SchemaDiffResult r)
    {
        var ordered = r.Changes.Select((c, i) => (c, i)).OrderBy(x => x.c.Phase).ThenBy(x => x.i).Select(x => x.c).ToList();
        r.Changes.Clear();
        r.Changes.AddRange(ordered);
        return r;
    }

    private static void AddNewTable(SchemaDiffResult result, TableDef d)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE ").Append(QT(d)).AppendLine(" (");
        var lines = new List<string>();
        lines.AddRange(d.Columns.Select(c => "    " + ColumnSql(c)));
        if (d.Primary is { } pk) lines.Add("    " + KeySql(pk));
        lines.AddRange(d.Uniques.Select(u => "    " + KeySql(u)));
        lines.AddRange(d.Checks.Select(c => "    " + CheckSql(c)));
        sb.AppendLine(string.Join(",\n", lines)).Append(')');
        result.Changes.Add(new SchemaChange(1, $"Crear tabla {d.Key}", sb.ToString()));
        foreach (var i in d.Indexes) result.Changes.Add(new SchemaChange(5, $"Crear índice {i.Name} en {d.Key}", IndexSql(d, i)));
        foreach (var f in d.Fks) result.Changes.Add(new SchemaChange(8, $"Agregar FK {FkLabel(d, f)}", FkSql(d, f)));
    }

    private static void DiffColumns(SchemaDiffResult result, TableDef d, TableDef a, SchemaModel actual)
    {
        var existing = a.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var c in d.Columns)
        {
            if (!existing.TryGetValue(c.Name, out var ac))
            {
                result.Changes.Add(new SchemaChange(2, $"Agregar columna {d.Key}.{c.Name}", $"ALTER TABLE {QT(d)} ADD {ColumnSql(c)}"));
                continue;
            }
            if (c.ComputedExpr is not null || ac.ComputedExpr is not null)
            {
                if (c.ComputedExpr != ac.ComputedExpr) result.Warnings.Add($"{d.Key}.{c.Name}: la columna calculada cambió; revísela a mano.");
                continue;
            }
            DiffColumnShape(result, d, c, ac);
            if (c.DefaultDef is null) continue;
            if (ac.DefaultDef is null)
                result.Changes.Add(new SchemaChange(6, $"Agregar valor por defecto {d.Key}.{c.Name}", DefaultSql(d, c)));
            else if (ac.DefaultDef != c.DefaultDef)
            {
                result.Changes.Add(new SchemaChange(3, $"Retirar valor por defecto anterior {d.Key}.{c.Name}", $"ALTER TABLE {QT(d)} DROP CONSTRAINT {Q(ac.DefaultName!)}"));
                result.Changes.Add(new SchemaChange(6, $"Cambiar valor por defecto {d.Key}.{c.Name}", DefaultSql(d, c)));
            }
        }
        var wanted = d.Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ac in a.Columns.Where(c => !wanted.Contains(c.Name)))
        {
            result.Extras.Add($"Columna {d.Key}.{ac.Name}");
            // Una columna sobrante NOT NULL sin valor por defecto hace fallar todos los INSERT nuevos: se retira si la tabla está vacía
            // (no se pierde nada); si tiene datos, se avisa para que alguien decida.
            if (ac.IsNullable || ac.DefaultDef is not null || ac.IsIdentity || ac.ComputedExpr is not null || ac.TypeSql == "ROWVERSION") continue;
            if (actual.EmptyTables.Contains(d.Key)) DropObsoleteColumn(result, a, ac);
            else result.Warnings.Add($"{d.Key}.{ac.Name}: columna obsoleta NOT NULL sin valor por defecto en una tabla con datos; bloquea los INSERT nuevos. Decida qué hacer con esos datos y quítela a mano.");
        }
    }

    private static void DropObsoleteColumn(SchemaDiffResult result, TableDef a, ColumnDef col)
    {
        bool Has(IEnumerable<string> cols) => cols.Contains(col.Name, StringComparer.OrdinalIgnoreCase);
        if (a.Primary is not null && Has(a.Primary.Columns))
        {
            result.Warnings.Add($"{a.Key}.{col.Name}: columna obsoleta que forma parte de la clave primaria; revísela a mano.");
            return;
        }
        foreach (var f in a.Fks.Where(f => Has(f.Columns)))
            result.Changes.Add(new SchemaChange(3, $"Retirar FK obsoleta {f.Name} de {a.Key}", $"ALTER TABLE {QT(a)} DROP CONSTRAINT {Q(f.Name)}"));
        foreach (var u in a.Uniques.Where(u => Has(u.Columns)))
            result.Changes.Add(new SchemaChange(3, $"Retirar restricción única obsoleta {u.Name} de {a.Key}", $"ALTER TABLE {QT(a)} DROP CONSTRAINT {Q(u.Name)}"));
        foreach (var i in a.Indexes.Where(i => Has(i.Keys.Select(k => k.Name)) || Has(i.Includes)))
            result.Changes.Add(new SchemaChange(3, $"Retirar índice obsoleto {i.Name} de {a.Key}", $"DROP INDEX {Q(i.Name)} ON {QT(a)}"));
        foreach (var c in a.Checks.Where(c => c.Definition.Contains($"[{col.Name}]", StringComparison.OrdinalIgnoreCase)))
            result.Changes.Add(new SchemaChange(3, $"Retirar check obsoleto {c.Name} de {a.Key}", $"ALTER TABLE {QT(a)} DROP CONSTRAINT {Q(c.Name)}"));
        result.Changes.Add(new SchemaChange(3, $"Retirar columna obsoleta {a.Key}.{col.Name} (la tabla está vacía)", $"ALTER TABLE {QT(a)} DROP COLUMN {Q(col.Name)}"));
    }

    private static void DiffColumnShape(SchemaDiffResult result, TableDef d, ColumnDef want, ColumnDef have)
    {
        var typeChanged = want.TypeSql != have.TypeSql;
        var widen = typeChanged && IsWidening(have.TypeSql, want.TypeSql);
        if (typeChanged && !widen)
        {
            result.Warnings.Add($"{d.Key}.{want.Name}: el tipo es {want.TypeSql} en el script y {have.TypeSql} en la base; revísela a mano.");
            typeChanged = false;
        }
        var relax = want.IsNullable && !have.IsNullable;
        if (!want.IsNullable && have.IsNullable)
            result.Warnings.Add($"{d.Key}.{want.Name}: el script la pide NOT NULL y la base la tiene NULL; complete los datos y cámbiela a mano.");
        if (!typeChanged && !relax) return;
        var type = widen ? want.TypeSql : have.TypeSql;
        var collate = want.Collation is null ? "" : $" COLLATE {want.Collation}";
        var nullSql = relax || have.IsNullable ? "NULL" : "NOT NULL";
        result.Changes.Add(new SchemaChange(3, $"Ajustar columna {d.Key}.{want.Name}", $"ALTER TABLE {QT(d)} ALTER COLUMN {Q(want.Name)} {type}{collate} {nullSql}"));
    }

    private static void DiffKeys(SchemaDiffResult result, TableDef d, TableDef a)
    {
        if (d.Primary is { } pk)
        {
            if (a.Primary is null)
                result.Changes.Add(new SchemaChange(4, $"Agregar clave primaria de {d.Key}", $"ALTER TABLE {QT(d)} ADD {KeySql(pk)}"));
            else if (!SameList(pk.Columns, a.Primary.Columns))
                result.Warnings.Add($"{d.Key}: la clave primaria cambió de columnas; revísela a mano.");
        }
        foreach (var u in d.Uniques)
            if (!a.Uniques.Any(x => SameList(x.Columns, u.Columns)))
                result.Changes.Add(new SchemaChange(4, $"Agregar restricción única ({string.Join(", ", u.Columns)}) en {d.Key}", $"ALTER TABLE {QT(d)} ADD {KeySql(u)}"));
    }

    private static void DiffIndexes(SchemaDiffResult result, TableDef d, TableDef a)
    {
        foreach (var i in d.Indexes)
        {
            var have = a.Indexes.FirstOrDefault(x => string.Equals(x.Name, i.Name, StringComparison.OrdinalIgnoreCase));
            if (have is null)
            {
                result.Changes.Add(new SchemaChange(5, $"Crear índice {i.Name} en {d.Key}", IndexSql(d, i)));
                continue;
            }
            if (SameIndex(i, have)) continue;
            result.Changes.Add(new SchemaChange(3, $"Retirar índice cambiado {i.Name} de {d.Key}", $"DROP INDEX {Q(have.Name)} ON {QT(d)}"));
            result.Changes.Add(new SchemaChange(5, $"Recrear índice {i.Name} en {d.Key}", IndexSql(d, i)));
        }
    }

    private static void DiffChecks(SchemaDiffResult result, TableDef d, TableDef a)
    {
        foreach (var c in d.Checks)
        {
            var have = c.SystemNamed
                ? a.Checks.FirstOrDefault(x => x.Definition == c.Definition)
                : a.Checks.FirstOrDefault(x => string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase));
            if (have is null)
            {
                result.Changes.Add(new SchemaChange(6, $"Agregar check {(c.SystemNamed ? c.Definition : c.Name)} en {d.Key}", $"ALTER TABLE {QT(d)} ADD {CheckSql(c)}"));
                continue;
            }
            if (have.Definition == c.Definition) continue;
            result.Changes.Add(new SchemaChange(3, $"Retirar check cambiado {have.Name} de {d.Key}", $"ALTER TABLE {QT(d)} DROP CONSTRAINT {Q(have.Name)}"));
            result.Changes.Add(new SchemaChange(6, $"Recrear check {c.Name} en {d.Key}", $"ALTER TABLE {QT(d)} ADD {CheckSql(c)}"));
        }
    }

    private static void DiffFks(SchemaDiffResult result, TableDef d, TableDef a)
    {
        foreach (var f in d.Fks)
        {
            var have = a.Fks.FirstOrDefault(x => SameList(x.Columns, f.Columns) && string.Equals(x.RefTable, f.RefTable, StringComparison.OrdinalIgnoreCase) && SameList(x.RefColumns, f.RefColumns));
            if (have is null) result.Changes.Add(new SchemaChange(8, $"Agregar FK {FkLabel(d, f)}", FkSql(d, f)));
            else if (have.DeleteAction != f.DeleteAction || have.UpdateAction != f.UpdateAction)
                result.Warnings.Add($"{d.Key}: la FK {FkLabel(d, f)} cambió su acción en cascada; revísela a mano.");
        }
    }

    // ---------- DDL ----------

    public static string Q(string name) => "[" + name.Replace("]", "]]") + "]";

    private static string QT(TableDef t) => $"{Q(t.Schema)}.{Q(t.Name)}";

    private static string QRef(string schemaDotName)
    {
        var dot = schemaDotName.IndexOf('.');
        return $"{Q(schemaDotName[..dot])}.{Q(schemaDotName[(dot + 1)..])}";
    }

    public static string ColumnSql(ColumnDef c)
    {
        if (c.ComputedExpr is not null) return $"{Q(c.Name)} AS {c.ComputedExpr}{(c.IsPersisted ? " PERSISTED" : "")}";
        var sb = new StringBuilder($"{Q(c.Name)} {c.TypeSql}");
        if (c.Collation is not null) sb.Append(" COLLATE ").Append(c.Collation);
        if (c.IsIdentity) sb.Append($" IDENTITY({c.IdentitySeed},{c.IdentityIncrement})");
        sb.Append(c.IsNullable ? " NULL" : " NOT NULL");
        if (c.DefaultDef is not null)
            sb.Append(c.DefaultSystemNamed || c.DefaultName is null ? "" : $" CONSTRAINT {Q(c.DefaultName)}").Append(" DEFAULT ").Append(c.DefaultDef);
        return sb.ToString();
    }

    private static string DefaultSql(TableDef t, ColumnDef c) =>
        $"ALTER TABLE {QT(t)} ADD{(c.DefaultSystemNamed || c.DefaultName is null ? "" : $" CONSTRAINT {Q(c.DefaultName)}")} DEFAULT {c.DefaultDef} FOR {Q(c.Name)}";

    private static string KeySql(KeyDef k) =>
        $"{(k.SystemNamed ? "" : $"CONSTRAINT {Q(k.Name)} ")}{(k.IsPrimary ? "PRIMARY KEY" : "UNIQUE")} {k.Clustering} ({string.Join(", ", k.Columns.Select(Q))})";

    private static string CheckSql(CheckDef c) => $"{(c.SystemNamed ? "" : $"CONSTRAINT {Q(c.Name)} ")}CHECK {c.Definition}";

    private static string IndexSql(TableDef t, IndexDef i)
    {
        var sb = new StringBuilder("CREATE ");
        if (i.IsUnique) sb.Append("UNIQUE ");
        sb.Append(i.Clustering).Append(" INDEX ").Append(Q(i.Name)).Append(" ON ").Append(QT(t))
            .Append(" (").Append(string.Join(", ", i.Keys.Select(k => Q(k.Name) + (k.Descending ? " DESC" : "")))).Append(')');
        if (i.Includes.Count > 0) sb.Append(" INCLUDE (").Append(string.Join(", ", i.Includes.Select(Q))).Append(')');
        if (i.Filter is not null) sb.Append(" WHERE ").Append(i.Filter);
        return sb.ToString();
    }

    private static string FkSql(TableDef t, FkDef f)
    {
        var sb = new StringBuilder($"ALTER TABLE {QT(t)} ADD ");
        if (!f.SystemNamed) sb.Append("CONSTRAINT ").Append(Q(f.Name)).Append(' ');
        sb.Append("FOREIGN KEY (").Append(string.Join(", ", f.Columns.Select(Q))).Append(") REFERENCES ").Append(QRef(f.RefTable))
            .Append(" (").Append(string.Join(", ", f.RefColumns.Select(Q))).Append(')');
        if (f.DeleteAction != "NO_ACTION") sb.Append(" ON DELETE ").Append(f.DeleteAction.Replace('_', ' '));
        if (f.UpdateAction != "NO_ACTION") sb.Append(" ON UPDATE ").Append(f.UpdateAction.Replace('_', ' '));
        return sb.ToString();
    }

    private static string FkLabel(TableDef t, FkDef f) => $"{t.Key}({string.Join(", ", f.Columns)}) → {f.RefTable}";

    // ---------- comparaciones ----------

    private static bool SameList(IReadOnlyList<string> x, IReadOnlyList<string> y) =>
        x.Count == y.Count && x.Zip(y).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));

    private static bool SameIndex(IndexDef x, IndexDef y) =>
        x.IsUnique == y.IsUnique
        && x.Keys.Count == y.Keys.Count
        && x.Keys.Zip(y.Keys).All(p => string.Equals(p.First.Name, p.Second.Name, StringComparison.OrdinalIgnoreCase) && p.First.Descending == p.Second.Descending)
        && x.Includes.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(y.Includes.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
        && (x.Filter ?? "") == (y.Filter ?? "");

    /// <summary>Solo se ensancha automáticamente VARCHAR/NVARCHAR/VARBINARY (más largo o MAX); lo demás se avisa.</summary>
    public static bool IsWidening(string from, string to)
    {
        var f = SizedType().Match(from);
        var t = SizedType().Match(to);
        if (!f.Success || !t.Success || f.Groups[1].Value != t.Groups[1].Value) return false;
        if (f.Groups[1].Value is not ("VARCHAR" or "NVARCHAR" or "VARBINARY")) return false;
        var fl = f.Groups[2].Value == "MAX" ? int.MaxValue : int.Parse(f.Groups[2].Value);
        var tl = t.Groups[2].Value == "MAX" ? int.MaxValue : int.Parse(t.Groups[2].Value);
        return tl > fl;
    }

    private static string Normalize(string sql) => WhiteSpace().Replace(sql, " ").Trim();

    [GeneratedRegex(@"^([A-Z]+)\((MAX|\d+)\)$")]
    private static partial Regex SizedType();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex(@"^\s*CREATE\s+VIEW", RegexOptions.IgnoreCase)]
    private static partial Regex CreateToAlter();
}
