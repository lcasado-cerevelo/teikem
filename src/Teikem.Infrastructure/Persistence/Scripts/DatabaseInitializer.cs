using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Teikem.Infrastructure.Seeding;

namespace Teikem.Infrastructure.Persistence.Scripts;

/// <summary>
/// Orquesta la inicialización de la BD respetando la convención del README de Diseño (la BD se entrega SEPARADA del API):
///   1. db/migrations/0001_identity.sql               (Identity, guarded)
///   2. Diseño/logistica-db-estructura.sql            (estructura, una sola vez)
///   3. Diseño/logistica-db-seed.sql                  (seed idempotente)
///   4. db/migrations/0002+ (extensiones del lote)    (idempotentes)
///   5. Seeders de código: permisos (siempre) y tenant demo (opcional).
/// </summary>
public sealed class DatabaseInitializer(IServiceProvider services, IConfiguration config, ILogger<DatabaseInitializer> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var connectionString = config.GetConnectionString("Teikem")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Teikem.");
        var root = ResolveRepoRoot(config["Database:RepoRoot"]);
        var designDir = Path.Combine(root, config["Database:DesignFolder"] ?? "Diseño");
        var migrationsDir = Path.Combine(root, config["Database:MigrationsFolder"] ?? Path.Combine("db", "migrations"));
        logger.LogInformation("Raíz del repo: {Root}", root);

        var runner = new SqlScriptRunner(connectionString, logger);
        await runner.EnsureDatabaseAsync(ct);

        var scripts = new List<SqlScript>
        {
            new("0001_identity.sql", Path.Combine(migrationsDir, "0001_identity.sql")),
            new("logistica-db-estructura.sql", Path.Combine(designDir, "logistica-db-estructura.sql")),
            new("logistica-db-seed.sql", Path.Combine(designDir, "logistica-db-seed.sql")),
        };
        foreach (var f in Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(f);
            if (name.StartsWith("0001_")) continue;
            scripts.Add(new SqlScript(name, f));
        }
        await runner.ApplyAsync(scripts, ct);

        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync(ct);
        if (config.GetValue<bool>("Seed:Demo:Enabled"))
            await scope.ServiceProvider.GetRequiredService<DemoTenantSeeder>().SeedAsync(ct);
        logger.LogInformation("Inicialización de BD completada.");
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
