using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Teikem.Infrastructure.Persistence;

/// <summary>
/// Lote 42 (2026-10-12) — filtro global de tenant BUSCABLE por índice.
///
/// Antes, el filtro <c>e =&gt; TenantFilterDisabled || e.TenantId == CurrentTenantId</c> llegaba a SQL Server como
/// <c>(@__ef_filter__TenantFilterDisabled_0 = CAST(1 AS bit) OR [t].[TenantId] = @tid)</c>: como el plan en caché tiene que servir
/// también con el bypass encendido, SQL Server no podía buscar por ningún índice que empiece por TenantId y recorría la tabla
/// completa, con las filas de TODAS las compañías (Lote 41: 17.291 páginas por lista o saldo a 508 mil órdenes, esperas e
/// interbloqueos entre compañías).
///
/// Ahora el filtro es <c>e =&gt; QueryFilterTenantId == null || e.TenantId == QueryFilterTenantId</c>, donde
/// <see cref="TeikemDbContext.QueryFilterTenantId"/> vale <c>null</c> SOLO con el bypass encendido (y si no hay compañía, -1: no se ve
/// nada, como antes). EF, por defecto, evaluaría <c>QueryFilterTenantId == null</c> en el cliente y lo mandaría como otro parámetro
/// <c>bit</c> (el mismo OR de antes). Este plugin le pide a EF que NO pre-evalúe ESA comparación exacta: así llega a la traducción
/// como <c>@p IS NULL</c>, que EF resuelve con el valor real del parámetro al armar el comando (procesador de nulidad) y guarda en su
/// caché de comandos SEPARADO por nulidad de cada parámetro (es el mismo mecanismo con el que EF ya traduce <c>x == @p</c> cuando
/// <c>@p</c> es null). Resultado, sin tocar el texto SQL a mano:
/// <list type="bullet">
/// <item>compañía activa: <c>WHERE [t].[TenantId] = @__ef_filter__QueryFilterTenantId_0</c> (buscable);</item>
/// <item>opcional (catálogos con TenantId NULL = global): <c>[t].[TenantId] IS NULL OR [t].[TenantId] = @p</c>;</item>
/// <item>bypass (aprovisionamiento, seeders, plataforma): sin condición de tenant, igual que hoy.</item>
/// </list>
/// Lo mismo para el alcance de datos del usuario por almacén y por cliente (<c>QueryFilterWarehouseIds/ClientIds == null || ids.Contains(col)</c>):
/// sin alcance el término desaparece (antes iba <c>@p = 1 OR [x].[ClientId] IN (OPENJSON…)</c> en TODA consulta, lo que confundía al
/// optimizador: COUNT por estatus 17.255 lecturas con el índice disponible); con alcance queda <c>[x].[ClientId] IN (OPENJSON(@ids))</c>.
/// FALLA CERRADO: si una versión futura de EF dejara de consultar este plugin, la comparación volvería a mandarse como parámetro
/// <c>bit</c> y el SQL quedaría EXACTAMENTE como antes (filtro completo, solo más lento). Las pruebas <c>TenantFilterSqlShapeTests</c>
/// fallan si cambia la forma del SQL generado, y <c>TenantIsolationSqlServerTests</c> comprueban la no filtración contra SQL Server.
/// </summary>
public sealed class TenantFilterEvaluatablePlugin : IEvaluatableExpressionFilterPlugin
{
    /// <summary>
    /// Las propiedades del contexto que alimentan el filtro global (null = sin esa condición): la compañía
    /// (<see cref="TeikemDbContext.QueryFilterTenantId"/>, null solo con el bypass) y el alcance de datos del usuario por almacén y por
    /// cliente (<see cref="TeikemDbContext.QueryFilterWarehouseIds"/>, <see cref="TeikemDbContext.QueryFilterClientIds"/>, null = sin alcance).
    /// </summary>
    internal static readonly IReadOnlyList<PropertyInfo> FilterProperties = new[]
    {
        nameof(TeikemDbContext.QueryFilterTenantId), nameof(TeikemDbContext.QueryFilterWarehouseIds), nameof(TeikemDbContext.QueryFilterClientIds),
    }.Select(n => typeof(TeikemDbContext).GetProperty(n) ?? throw new InvalidOperationException($"TeikemDbContext.{n} no existe.")).ToArray();

    /// <summary>
    /// Falso SOLO para <c>ctx.&lt;propiedad del filtro&gt; == null</c> (en cualquier orden), la comparación que arman
    /// <c>TeikemDbContext.BuildTenantFilter</c> y <c>AddScopeFilter</c>. Todo lo demás: verdadero (decide el filtro del proveedor, como
    /// siempre). Devolver falso no cambia el resultado de nada: solo hace que esa comparación se traduzca a SQL en vez de calcularse antes.
    /// </summary>
    public bool IsEvaluatableExpression(Expression expression)
        => !(expression is BinaryExpression { NodeType: ExpressionType.Equal } b
             && ((IsFilterProperty(b.Left) && IsNullConstant(b.Right)) || (IsFilterProperty(b.Right) && IsNullConstant(b.Left))));

    // Por declaración (no por instancia de PropertyInfo): Expression.Property sobre una subclase del contexto devuelve otra instancia.
    private static bool IsFilterProperty(Expression e)
        => e is MemberExpression { Member: PropertyInfo p, Expression: not null }
           && FilterProperties.Any(f => p.DeclaringType == f.DeclaringType && p.Name == f.Name);

    private static bool IsNullConstant(Expression e) => e is ConstantExpression { Value: null };
}

/// <summary>Registra <see cref="TenantFilterEvaluatablePlugin"/> en el proveedor de servicios interno de EF (lo agrega TeikemDbContext.OnConfiguring).</summary>
public sealed class TenantFilterOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
        => new EntityFrameworkServicesBuilder(services).TryAdd<IEvaluatableExpressionFilterPlugin, TenantFilterEvaluatablePlugin>();

    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "TenantFilterSeekable ";
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Teikem:TenantFilterSeekable"] = "1";
    }
}
