using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Teikem.Infrastructure.Seeding;

namespace Teikem.Infrastructure.Persistence.Scripts;

/// <summary>
/// Orquesta la inicialización de la BD respetando la convención del README de Diseño (la BD se entrega SEPARADA del API
/// y hay UN SOLO set de scripts):
///   1. Diseño/logistica-db-estructura.sql            (estructura completa, incl. Identity; se aplica una sola vez por hash)
///   2. Diseño/logistica-db-seed.sql                  (seed idempotente)
///   3. Seeders de código: permisos (siempre) y tenant demo (opcional).
/// </summary>
public sealed class DatabaseInitializer(IServiceProvider services, IConfiguration config, ILogger<DatabaseInitializer> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var connectionString = config.GetConnectionString("Teikem")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Teikem.");
        var root = ResolveRepoRoot(config["Database:RepoRoot"]);
        var designDir = Path.Combine(root, config["Database:DesignFolder"] ?? "Diseño");
        logger.LogInformation("Raíz del repo: {Root}", root);

        var runner = new SqlScriptRunner(connectionString, logger);
        await runner.EnsureDatabaseAsync(ct);

        var scripts = new List<SqlScript>
        {
            new("logistica-db-estructura.sql", Path.Combine(designDir, "logistica-db-estructura.sql")),
            new("logistica-db-seed.sql", Path.Combine(designDir, "logistica-db-seed.sql")),
        };
        await runner.ApplyAsync(scripts, ct);
        await RunCodeSeedersAsync(ct);
        logger.LogInformation("Inicialización de BD completada.");
    }

    private async Task RunCodeSeedersAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync(ct);
        if (config.GetValue<bool>("Seed:Demo:Enabled"))
            await scope.ServiceProvider.GetRequiredService<DemoTenantSeeder>().SeedAsync(ct);
    }

    /// <summary>
    /// db-update (2026-10-06, actualización de staging/producción): lleva una base YA CREADA a la versión actual sin borrarla.
    ///   1. Esquema: lo que falta según el script de estructura (SchemaSync; tablas, columnas, claves, índices, checks, FKs, vistas).
    ///   2. Datos de referencia: el seed idempotente y los seeders de código (permisos).
    /// Si la base no existe o está vacía equivale a db-init. Con dryRun solo muestra el plan (no cambia la base real). Devuelve el
    /// código de salida: 0 bien (también «nada que hacer»), 2 si quedaron diferencias que exigen revisión manual (se aplicó el resto).
    /// </summary>
    public async Task<int> UpdateAsync(bool dryRun, CancellationToken ct = default)
    {
        var connectionString = config.GetConnectionString("Teikem")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Teikem.");
        var root = ResolveRepoRoot(config["Database:RepoRoot"]);
        var designDir = Path.Combine(root, config["Database:DesignFolder"] ?? "Diseño");
        var structure = new SqlScript("logistica-db-estructura.sql", Path.Combine(designDir, "logistica-db-estructura.sql"));
        var seed = new SqlScript("logistica-db-seed.sql", Path.Combine(designDir, "logistica-db-seed.sql"));
        foreach (var s in new[] { structure, seed })
            if (!File.Exists(s.Path)) throw new FileNotFoundException($"Script SQL no encontrado: {s.Path}");

        var runner = new SqlScriptRunner(connectionString, logger);
        await runner.EnsureDatabaseAsync(ct);
        if (!await runner.TableExistsAsync("dbo.Tenant", ct))
        {
            if (dryRun) { Console.WriteLine("db-update (simulación): la base está vacía; db-update la crearía completa como db-init."); return 0; }
            Console.WriteLine("db-update: la base está vacía; se crea completa (como db-init).");
            await RunAsync(ct);
            return 0;
        }

        var sync = new SchemaSync(connectionString, logger);
        var plan = await sync.PlanAsync(structure.Path, ct);
        Console.WriteLine($"db-update: {plan.Changes.Count} cambio(s) de esquema por aplicar.");
        foreach (var c in plan.Changes) Console.WriteLine($"  + {c.Description}");
        foreach (var w in plan.Warnings) Console.WriteLine($"  ! REVISAR A MANO: {w}");
        if (plan.Extras.Count > 0)
            Console.WriteLine($"  (en la base y ya no en el script, se dejan como están: {string.Join("; ", plan.Extras.Take(15))}{(plan.Extras.Count > 15 ? $"; y {plan.Extras.Count - 15} más" : "")})");
        if (dryRun)
        {
            Console.WriteLine("db-update (simulación): no se cambió nada.");
            if (config.GetValue<bool>("Database:ShowSql")) foreach (var c in plan.Changes) Console.WriteLine($"-- {c.Description}\n{c.Sql};\nGO");
            return plan.Warnings.Count > 0 ? 2 : 0;
        }

        if (plan.Changes.Count > 0) await sync.ApplyAsync(plan.Changes, ct);
        await runner.MarkAppliedAsync(structure, ct);
        await runner.ApplyAsync([seed], ct);
        await RunCodeSeedersAsync(ct);
        Console.WriteLine(plan.Warnings.Count > 0
            ? "db-update: terminado, con diferencias que debe revisar a mano (arriba)."
            : "db-update: base de datos al día.");
        return plan.Warnings.Count > 0 ? 2 : 0;
    }

    /// <summary>
    /// Lote 10 (db-reset, pedido de Luis 2026-09-29): borra la base de ConnectionStrings:Teikem y la vuelve a crear con RunAsync
    /// (estructura, seed y seeders), es decir, la deja exactamente como un db-init sobre un servidor limpio. Las reglas de
    /// seguridad (--yes, servidor local, nunca MSWM*) las aplica quien llama (DbResetRules) antes de llegar aquí.
    /// </summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        var connectionString = config.GetConnectionString("Teikem")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Teikem.");
        var runner = new SqlScriptRunner(connectionString, logger);
        await runner.DropDatabaseAsync(ct);
        await RunAsync(ct);
        logger.LogInformation("Base de datos recreada en blanco e inicializada.");
    }

    /// <summary>Sube desde el directorio de ejecución hasta encontrar Teikem.sln (o usa la ruta configurada).</summary>
    public static string ResolveRepoRoot(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("No se encontró la raíz del repositorio (Teikem.sln). Configure Database:RepoRoot.");
    }
}
