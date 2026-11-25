using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;
using R = Teikem.Domain.Clients.ClientChainRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 245 (Lote B del plan) — cadenas de clientes contra SQL Server REAL (se omite sin TEIKEM_TEST_SQLSERVER), sobre la base desechable de
/// <see cref="NotificationSqlFixture"/> (creada desde el modelo EF y sin FK). PrepareAsync la deja «como antes del lote» (quita la tabla, el índice y la columna que
/// creó EnsureCreated), crea dbo.AppliedMigration y el DEFAULT de LookupCode.CreatedAtUtc (el modelo EF no los trae), siembra la categoría BILLING y los roles
/// TenantAdmin, SalesSupervisor y Sales de dos compañías, y corre DOS veces los lotes GO de la sección del lote (acotada al siguiente encabezado). Después se prueban
/// la FK COMPUESTA por compañía (547), el índice único filtrado, la carrera desactivar ↔ asignar (RowVersion), el rowVersion viejo y la traducción de los conteos.
/// </summary>
public sealed class ClientChainSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    private const string Marker = "Lote 245: Cadenas de clientes (Lote B del plan";
    private const string TenantAName = "Compañía A cadenas (sección)";
    private const string TenantBName = "Compañía B cadenas (sección)";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    /// <summary>La sección del lote: desde su encabezado hasta el de la siguiente (o el final del archivo).</summary>
    private static string Section()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección del Lote 245 en logistica-db-update.sql.");
        return SqlSections.From(sql, sql.LastIndexOf("/* ----", at, StringComparison.Ordinal));
    }

    private static string Statements(string section)
        => string.Join("\n", Regex.Replace(section, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline)
            .Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    private static List<string> Batches(string section)
        => Regex.Split(section, @"^\s*GO\s*$", RegexOptions.Multiline).Select(b => b.Trim()).Where(b => Statements(b).Trim().Length > 0).ToList();

    private static async Task ExecAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    /// <summary>Idempotente: la primera vez deja la base como antes del lote, siembra lo que la sección necesita y corre la sección dos veces.</summary>
    private async Task PrepareAsync()
    {
        await Gate.WaitAsync();
        try
        {
            await using var c = new SqlConnection(fx.ConnectionString);
            await c.OpenAsync();
            if ((int)(await ScalarAsync(c, "SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'UQ_ClientChain_IdTenant'"))! > 0) return;

            // la base como antes del lote: sin la tabla, la columna ni su índice (los creó EnsureCreated desde el modelo EF)
            await ExecAsync(c, """
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Client_ChainId' AND object_id = OBJECT_ID('dbo.Client')) DROP INDEX IX_Client_ChainId ON dbo.Client;
                IF COL_LENGTH(N'dbo.Client', N'ChainId') IS NOT NULL ALTER TABLE dbo.Client DROP COLUMN ChainId;
                IF OBJECT_ID(N'dbo.ClientChain', N'U') IS NOT NULL DROP TABLE dbo.ClientChain;
                IF OBJECT_ID(N'dbo.AppliedMigration', N'U') IS NULL CREATE TABLE dbo.AppliedMigration (Code VARCHAR(60) NOT NULL PRIMARY KEY, AppliedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
                IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_LookupCode_CreatedAtUtc_ChainTest')
                    ALTER TABLE dbo.LookupCode ADD CONSTRAINT DF_LookupCode_CreatedAtUtc_ChainTest DEFAULT SYSUTCDATETIME() FOR CreatedAtUtc;
                """);

            await using (var db = fx.NewDb(new TenantContext()))
            {
                if (!await db.LookupCodes.IgnoreQueryFilters().AnyAsync(l => l.Entity == LookupDomains.PermissionCategory && l.InternalCode == "BILLING"))
                    db.LookupCodes.Add(new LookupCode { Entity = LookupDomains.PermissionCategory, InternalCode = "BILLING", LabelJson = "{\"es\":\"Facturación\"}", IsSystem = true });
                var a = new Tenant { Name = TenantAName, DefaultLangCode = "es" };
                var b = new Tenant { Name = TenantBName, DefaultLangCode = "es" };
                db.Tenants.AddRange(a, b);
                await db.SaveChangesAsync();
                foreach (var t in new[] { a, b })
                    foreach (var name in new[] { "TenantAdmin", "SalesSupervisor", "Sales" })
                        db.AppRoles.Add(new Role { TenantId = t.TenantId, Name = name, IsActive = true });
                db.AppRoles.Add(new Role { TenantId = null, Name = "TenantAdmin", IsActive = true, IsSystem = true });   // la plantilla global no se toca
                await db.SaveChangesAsync();
            }

            // dos corridas: la segunda no hace nada ni falla
            foreach (var _ in new[] { 1, 2 })
                foreach (var batch in Batches(Section()))
                    await ExecAsync(c, batch);
        }
        finally { Gate.Release(); }
    }

    private static ClientChainService Svc(TeikemDbContext db, TenantContext tenant) => new(db, tenant, TenantClock.Default);

    private static TenantContext Ctx(int tenantId) => new() { TenantId = tenantId, UserId = 1, IsAuthenticated = true };

    private async Task<int> ClientAsync(int tenantId, string name, bool isActive = true)
    {
        await using var db = fx.NewDb(Ctx(tenantId));
        var cl = new Client { PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "S" + Guid.NewGuid().ToString("N")[..8], Name = name, StatusCodeId = 1, IsActive = isActive, CreatedAtUtc = DateTime.UtcNow };
        db.Clients.Add(cl);
        await db.SaveChangesAsync();
        return cl.ClientId;
    }

    private async Task<ClientChainDetailDto> CreateChainAsync(int tenantId, string name)
    {
        await using var db = fx.NewDb(Ctx(tenantId));
        return await Svc(db, Ctx(tenantId)).CreateAsync(new ClientChainRequest(name, null), default);
    }

    private async Task<Guid> ClientPublicIdAsync(int tenantId, int clientId)
    {
        await using var db = fx.NewDb(Ctx(tenantId));
        return await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId).Select(c => c.PublicId).SingleAsync();
    }

    // ---------------------------------------------------------------- la sección

    [SqlServerFact]
    public async Task Section_runs_twice()
    {
        await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        // una tercera corrida tampoco falla ni duplica nada
        foreach (var batch in Batches(Section())) await ExecAsync(c, batch);

        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'UQ_ClientChain_IdTenant' AND parent_object_id = OBJECT_ID('dbo.ClientChain')"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_ClientChain_Name' AND object_id = OBJECT_ID('dbo.ClientChain') AND is_unique = 1 AND has_filter = 1"));
        Assert.Contains("IsActive", (string)(await ScalarAsync(c, "SELECT filter_definition FROM sys.indexes WHERE name = 'UX_ClientChain_Name'"))!);
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_ClientChain_PublicId' AND is_unique = 1"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_Client_Chain' AND object_id = OBJECT_ID('dbo.Client') AND has_filter = 1"));
        Assert.Equal(2, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id = OBJECT_ID('dbo.FK_Client_Chain')"));
        Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.LookupCode WHERE Entity = N'EntityType' AND InternalCode = N'{ClientChainCodes.EntityType}'"));
        Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.Permission WHERE Code = '{PermissionCatalog.SalesChain}'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.AppliedMigration WHERE Code = 'LB_CLIENT_CHAIN_PERMS'"));

        // sales.chain en TenantAdmin y SalesSupervisor de las dos compañías; no en Sales ni en la plantilla global
        var withPerm = new List<(string Tenant, string Role)>();
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT t.Name, r.Name FROM dbo.RolePermission rp JOIN dbo.Role r ON r.RoleId = rp.RoleId JOIN dbo.Permission p ON p.PermissionId = rp.PermissionId
                LEFT JOIN dbo.Tenant t ON t.TenantId = r.TenantId
                WHERE p.Code = '{PermissionCatalog.SalesChain}'
                """;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) withPerm.Add((rd.IsDBNull(0) ? "(global)" : rd.GetString(0), rd.GetString(1)));
        }
        Assert.Equal(new[] { (TenantAName, "SalesSupervisor"), (TenantAName, "TenantAdmin"), (TenantBName, "SalesSupervisor"), (TenantBName, "TenantAdmin") },
            withPerm.OrderBy(x => x.Tenant).ThenBy(x => x.Role).ToArray());

        // ninguna cadena se crea sola
        Assert.Equal(0, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.ClientChain ch JOIN dbo.Tenant t ON t.TenantId = ch.TenantId WHERE t.Name IN (N'{TenantAName}', N'{TenantBName}')"));
    }

    // ---------------------------------------------------------------- FK compuesta

    [SqlServerFact]
    public async Task Composite_fk_rejects_cross_tenant()
    {
        await PrepareAsync();
        var (x, y) = (await fx.NewTenantAsync("Cadenas X"), await fx.NewTenantAsync("Cadenas Y"));
        var chainX = await CreateChainAsync(x, "Walgreens");
        var chainY = await CreateChainAsync(y, "Walgreens");
        int idX, idY;
        await using (var db = fx.NewDb(new TenantContext()))
        {
            idX = await db.Set<ClientChain>().IgnoreQueryFilters().Where(ch => ch.PublicId == chainX.PublicId).Select(ch => ch.ClientChainId).SingleAsync();
            idY = await db.Set<ClientChain>().IgnoreQueryFilters().Where(ch => ch.PublicId == chainY.PublicId).Select(ch => ch.ClientChainId).SingleAsync();
        }
        var clientX = await ClientAsync(x, "Walgreens 1");

        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        var ex = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"UPDATE dbo.Client SET ChainId = {idY} WHERE ClientId = {clientX};"));
        Assert.Equal(547, ex.Number);
        Assert.Contains("FK_Client_Chain", ex.Message);
        await ExecAsync(c, $"UPDATE dbo.Client SET ChainId = {idX} WHERE ClientId = {clientX};");
        Assert.Equal(idX, await ScalarAsync(c, $"SELECT ChainId FROM dbo.Client WHERE ClientId = {clientX}"));
    }

    // ---------------------------------------------------------------- índice único filtrado

    [SqlServerFact]
    public async Task Filtered_unique_index()
    {
        await PrepareAsync();
        var t = await fx.NewTenantAsync("Cadenas índice");
        await using (var db = fx.NewDb(Ctx(t)))
        {
            db.Set<ClientChain>().Add(new ClientChain { PublicId = Guid.NewGuid(), TenantId = t, Name = "CVS", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveGuardedAsync(R.NameTaken, default);
        }
        // saltándose el servicio: otra activa con el mismo nombre → 409 por el índice
        await using (var db = fx.NewDb(Ctx(t)))
        {
            db.Set<ClientChain>().Add(new ClientChain { PublicId = Guid.NewGuid(), TenantId = t, Name = "CVS", CreatedAtUtc = DateTime.UtcNow });
            var dup = await Assert.ThrowsAsync<ConflictException>(() => db.SaveGuardedAsync(R.NameTaken, default));
            Assert.Equal(R.NameTaken, dup.Message);
        }
        // una inactiva con el mismo nombre sí entra
        await using (var db = fx.NewDb(Ctx(t)))
        {
            db.Set<ClientChain>().Add(new ClientChain { PublicId = Guid.NewGuid(), TenantId = t, Name = "CVS", IsActive = false, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveGuardedAsync(R.NameTaken, default);
            Assert.Equal(2, await db.Set<ClientChain>().CountAsync(ch => ch.Name == "CVS"));
        }
    }

    // ---------------------------------------------------------------- carrera desactivar ↔ asignar

    [SqlServerFact]
    public async Task Deactivate_vs_assign_race()
    {
        await PrepareAsync();
        var t = await fx.NewTenantAsync("Cadenas carrera");
        var chain = await CreateChainAsync(t, "Walgreens");
        var clientId = await ClientAsync(t, "Walgreens 1");
        var clientPublicId = await ClientPublicIdAsync(t, clientId);

        // A carga la cadena para desactivarla (ya contó cero clientes)
        await using var dbA = fx.NewDb(Ctx(t));
        var loaded = await dbA.Set<ClientChain>().AsTracking().SingleAsync(ch => ch.PublicId == chain.PublicId);
        Assert.Equal(0, await dbA.Clients.CountAsync(cl => cl.ChainId == loaded.ClientChainId && cl.IsActive));
        loaded.IsActive = false;

        // B asigna un cliente y confirma (toca la cadena: cambia su RowVersion)
        await using (var dbB = fx.NewDb(Ctx(t)))
        {
            var r = await Svc(dbB, Ctx(t)).AssignAsync(chain.PublicId, new ClientChainAssignRequest(new[] { clientPublicId }), default);
            Assert.Equal(1, r.Assigned);
        }

        // A guarda → 409; la cadena sigue activa con su cliente
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => dbA.SaveGuardedAsync(R.NameTaken, default));
        Assert.Equal(DbExtensions.ConcurrencyMessage, conflict.Message);
        await using var check = fx.NewDb(Ctx(t));
        var detail = await Svc(check, Ctx(t)).GetAsync(chain.PublicId, default);
        Assert.True(detail.IsActive);
        Assert.Equal(1, detail.ClientCount);
        // y por el servicio, la desactivación vuelve a contar y da 409 con el mensaje
        var blocked = await Assert.ThrowsAsync<ConflictException>(() => Svc(check, Ctx(t)).SetActiveAsync(chain.PublicId, false, default));
        Assert.Equal(R.HasActiveClients(1), blocked.Message);
    }

    // ---------------------------------------------------------------- rowVersion, conteos y alta masiva traducidos a SQL

    [SqlServerFact]
    public async Task Stale_rowversion_on_patch_and_counts()
    {
        await PrepareAsync();
        var t = await fx.NewTenantAsync("Cadenas conteos");
        await using var db = fx.NewDb(Ctx(t));
        var svc = Svc(db, Ctx(t));
        var walg = await svc.CreateAsync(new ClientChainRequest("Walgreens", "nota"), default);
        Assert.False(string.IsNullOrEmpty(walg.RowVersion));

        var edited = await svc.UpdateAsync(walg.PublicId, new ClientChainRequest("Walgreens PR", null, RowVersion: walg.RowVersion), default);
        Assert.NotEqual(walg.RowVersion, edited.RowVersion);
        var stale = await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateAsync(walg.PublicId, new ClientChainRequest("Otra", null, RowVersion: walg.RowVersion), default));
        Assert.Equal(DbExtensions.ConcurrencyMessage, stale.Message);

        // conteos con GroupBy: solo clientes activos
        var ids = new List<int>
        {
            await ClientAsync(t, "Walgreens 1"), await ClientAsync(t, "Walgreens 2"), await ClientAsync(t, "Walgreens viejo", isActive: false),
        };
        var publicIds = new List<Guid>();
        foreach (var id in ids) publicIds.Add(await ClientPublicIdAsync(t, id));
        await svc.AssignAsync(walg.PublicId, new ClientChainAssignRequest(publicIds), default);
        var item = Assert.Single(await svc.ListAsync(false, "Walg", default));
        Assert.Equal(2, item.ClientCount);
        var detail = await svc.GetAsync(walg.PublicId, default);
        Assert.Equal((2, 1), (detail.ClientCount, detail.InactiveClientCount));
        Assert.Equal(3, (await svc.ListClientsAsync(walg.PublicId, null, true, 0, 2, true, default)).Items.Count);

        // el alta masiva también corre en SQL: simular, aplicar e idempotencia
        var cvs = await svc.CreateAsync(new ClientChainRequest("CVS", null), default);
        await ClientAsync(t, "CVS #1");
        await ClientAsync(t, "CVS 2");
        var preview = await svc.PreviewBulkAsync(cvs.PublicId, new ClientChainBulkRequest("cvs"), default);
        Assert.Equal((2, 2), (preview.Matched, preview.ToAssign));
        var applied = await svc.ApplyBulkAsync(cvs.PublicId, new ClientChainBulkRequest("cvs", Fingerprint: preview.Fingerprint), default);
        Assert.Equal(2, applied.Assigned);
        var again = await svc.PreviewBulkAsync(cvs.PublicId, new ClientChainBulkRequest("cvs"), default);
        Assert.Equal((0, 2), (again.ToAssign, again.AlreadyInChain));
    }
}
