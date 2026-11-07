using Microsoft.Extensions.DependencyInjection;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 227 (P7 del pedido de vendedores) — el ÚNICO lugar por donde el puente entre compañías hace algo en la otra compañía: un alcance de DI propio (su DbContext y su
/// TenantContext), puesto como esa compañía SIN usuario («Sistema», como los trabajos programados) y acotado al cliente que la representa, igual que una llave de la API de
/// clientes (ScopedClientIds = ese cliente: el filtro global solo deja ver lo suyo). Lo que se haga adentro no ve nada de la compañía de quien llama, y lo de quien llama no ve
/// nada de la otra: cada lado con su contexto. El contexto se restaura al salir (en el alcance nuevo no hace falta; en las pruebas, que comparten instancias, sí).
/// Quien lo llama ya comprobó que hay un enlace entre las dos compañías (ACTIVE para mandar o leer órdenes; un aviso del cambio del enlace va sin cliente).
/// </summary>
public sealed class LinkedTenantScope(IServiceScopeFactory scopes)
{
    public async Task<T> RunAsync<T>(int tenantId, int? clientId, Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var saved = new Saved(ctx.TenantId, ctx.UserId, ctx.IsAuthenticated, ctx.IsPlatformAdmin, ctx.TenantFilterDisabled, ctx.ScopedWarehouseIds, ctx.ScopedClientIds,
            ctx.IsPortal, ctx.IsApiKey, ctx.PortalClientId, ctx.PortalLocationId, ctx.Aal2VerifiedAtUtc);
        try
        {
            ctx.TenantId = tenantId;
            ctx.UserId = null;
            ctx.IsAuthenticated = true;
            ctx.IsPlatformAdmin = false;
            ctx.TenantFilterDisabled = false;
            ctx.ScopedWarehouseIds = null;
            ctx.ScopedClientIds = clientId is int only ? new[] { only } : new[] { -1 };   // sin cliente (solo un aviso a la otra compañía) no ve ningún dato de clientes
            ctx.IsPortal = false;
            ctx.IsApiKey = true;   // se comporta como una llave de la API de clientes de ese cliente
            ctx.PortalClientId = null;
            ctx.PortalLocationId = null;
            ctx.Aal2VerifiedAtUtc = null;
            return await work(scope.ServiceProvider, ct);
        }
        finally
        {
            (ctx.TenantId, ctx.UserId, ctx.IsAuthenticated, ctx.IsPlatformAdmin, ctx.TenantFilterDisabled, ctx.ScopedWarehouseIds, ctx.ScopedClientIds) =
                (saved.TenantId, saved.UserId, saved.IsAuthenticated, saved.IsPlatformAdmin, saved.FilterDisabled, saved.Warehouses, saved.Clients);
            (ctx.IsPortal, ctx.IsApiKey, ctx.PortalClientId, ctx.PortalLocationId, ctx.Aal2VerifiedAtUtc) = (saved.IsPortal, saved.IsApiKey, saved.PortalClientId, saved.PortalLocationId, saved.Aal2);
        }
    }

    private sealed record Saved(int? TenantId, int? UserId, bool IsAuthenticated, bool IsPlatformAdmin, bool FilterDisabled, IReadOnlyCollection<int>? Warehouses,
        IReadOnlyCollection<int>? Clients, bool IsPortal, bool IsApiKey, int? PortalClientId, int? PortalLocationId, DateTime? Aal2);
}
