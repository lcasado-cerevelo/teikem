using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Orders
{
    /// <summary>
    /// Contador atómico de numeración sobre dbo.NumberSequence (Lote 3, P1). El TenantId sale SIEMPRE del principal.
    ///
    /// Protocolo:
    /// 1. EnsureAsync corre en autocommit ANTES de la transacción del alta: crea la fila del contador si falta
    ///    (idempotente; una carrera entre dos primeros llamadores se resuelve ignorando la violación de UQ_NumberSequence).
    ///    Así dentro de la transacción solo corre el UPDATE y no hay errores de sentencia que la invaliden.
    /// 2. NextAsync corre dentro de la transacción ambiente del llamador: UPDATE … OUTPUT deleted.NextValue toma un
    ///    bloqueo de fila que dura hasta el commit/rollback, lo que serializa las altas simultáneas del mismo ámbito y
    ///    hace que un rollback devuelva el número (sin huecos). Llamado fuera de una transacción consume el valor en
    ///    autocommit: así OrderService "salta" un número automático que chocó con uno tecleado.
    /// </summary>
    public sealed class NumberSequenceService(TeikemDbContext db, ITenantContext tenant) : INumberSequenceService
    {
        private const string EnsureSql = """
            INSERT INTO dbo.NumberSequence (TenantId, Kind, ClientId, NextValue)
            SELECT @t, @k, @c, 1
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.NumberSequence
                WHERE TenantId = @t AND Kind = @k AND ((ClientId IS NULL AND @c IS NULL) OR ClientId = @c))
            """;

        // La columna de salida debe llamarse [Value]: es el nombre que SqlQueryRaw<long> de EF Core 8 materializa.
        // La consulta NO se compone (sin Where/First/Take): así EF ejecuta el UPDATE tal cual, sin envolverlo en subconsulta.
        private const string NextSql = """
            UPDATE dbo.NumberSequence SET NextValue = NextValue + 1
            OUTPUT deleted.NextValue AS [Value]
            WHERE TenantId = @t AND Kind = @k AND ((ClientId IS NULL AND @c IS NULL) OR ClientId = @c)
            """;

        public const string MissingRowMessage = "Falta la fila del contador; llame a EnsureAsync antes de la transacción.";

        public async Task EnsureAsync(string kind, int? clientId, CancellationToken ct)
        {
            var tenantId = ((TenantContext)tenant).RequireTenantId();
            EnsureKind(kind);
            try
            {
                await db.Database.ExecuteSqlRawAsync(EnsureSql, Parameters(tenantId, kind, clientId), ct);
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                // Otro llamador creó la fila entre el NOT EXISTS y el INSERT: ya existe, que es lo que queríamos.
            }
        }

        public async Task<long> NextAsync(string kind, int? clientId, CancellationToken ct)
        {
            var tenantId = ((TenantContext)tenant).RequireTenantId();
            EnsureKind(kind);
            var values = await db.Database
                .SqlQueryRaw<long>(NextSql, Parameters(tenantId, kind, clientId))
                .ToListAsync(ct);
            if (values.Count == 0) throw new InvalidOperationException(MissingRowMessage);
            return values[0];
        }

        private static object[] Parameters(int tenantId, string kind, int? clientId) =>
        [
            new SqlParameter("@t", SqlDbType.Int) { Value = tenantId },
            new SqlParameter("@k", SqlDbType.VarChar, 20) { Value = kind },
            new SqlParameter("@c", SqlDbType.Int) { Value = clientId.HasValue ? clientId.Value : DBNull.Value },
        ];

        private static void EnsureKind(string kind)
        {
            if (!NumberingRules.IsKnownKind(kind))
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de número desconocido; use ORDER, INVOICE, PACKAGE o PACKBATCH.");
        }

        /// <summary>Violación de UNIQUE (2627) o índice único (2601) en cualquier nivel de la cadena de excepciones.</summary>
        private static bool IsUniqueViolation(Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException)
            {
                if (e is SqlException sql)
                {
                    foreach (SqlError err in sql.Errors)
                        if (err.Number is 2601 or 2627) return true;
                    return sql.Number is 2601 or 2627;
                }
            }
            return false;
        }
    }
}
