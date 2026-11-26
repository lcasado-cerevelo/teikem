using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Clients;

/// <summary>Una cadena tal como la necesitan las listas y el análisis.</summary>
public sealed record ChainRef(int Id, Guid PublicId, string Name, bool IsActive);

/// <summary>
/// Lote 245 (Lote B del plan) — único punto de lectura de la cadena de los clientes para clientes, órdenes de venta, análisis y fuentes de datos (lo reutilizan
/// los Lotes C, E y Facturación). Siempre sobre los DbSet FILTRADOS por tenant (lo que no es de la compañía no existe) y con AsNoTracking. Un PublicId de otra
/// compañía da null / vacío, sin decir cuál.
/// </summary>
public static class ClientChainQueries
{
    /// <summary>Las cadenas de los ids pedidos (activas o no), por id. Los ids que no son de la compañía no salen.</summary>
    public static async Task<Dictionary<int, ChainRef>> ChainsByIdAsync(TeikemDbContext db, IEnumerable<int> chainIds, CancellationToken ct = default)
    {
        var ids = chainIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, ChainRef>();
        var rows = await db.Set<ClientChain>().AsNoTracking()
            .Where(x => ids.Contains(x.ClientChainId))
            .Select(x => new { x.ClientChainId, x.PublicId, x.Name, x.IsActive })
            .ToListAsync(ct);
        return rows.ToDictionary(x => x.ClientChainId, x => new ChainRef(x.ClientChainId, x.PublicId, x.Name, x.IsActive));
    }

    /// <summary>
    /// Cliente → cadena, SOLO los clientes que tienen cadena (activos o no; la cadena también, activa o no: para precios y ofertas se usa
    /// <see cref="ActiveClientChainMapAsync"/>). <paramref name="clientIds"/> null = todos los de la compañía; vacío = ninguno.
    /// </summary>
    public static async Task<Dictionary<int, int>> ClientChainMapAsync(TeikemDbContext db, IReadOnlyCollection<int>? clientIds, CancellationToken ct = default)
    {
        if (clientIds is { Count: 0 }) return new Dictionary<int, int>();
        var q = db.Clients.AsNoTracking().Where(c => c.ChainId != null);
        if (clientIds is not null)
        {
            var ids = clientIds.Distinct().ToList();
            q = q.Where(c => ids.Contains(c.ClientId));
        }
        var rows = await q.Select(c => new { c.ClientId, ChainId = c.ChainId!.Value }).ToListAsync(ct);
        return rows.ToDictionary(x => x.ClientId, x => x.ChainId);
    }

    /// <summary>
    /// Lote 247 — la cadena del cliente SOLO si está activa (la que cuenta para los precios y las ofertas); null = sin cliente, sin cadena, cadena inactiva o
    /// cliente de otra compañía.
    /// </summary>
    public static async Task<int?> ActiveChainOfClientAsync(TeikemDbContext db, int? clientId, CancellationToken ct = default)
    {
        if (clientId is not int id) return null;
        return await db.Clients.AsNoTracking()
            .Where(c => c.ClientId == id && c.ChainId != null && db.Set<ClientChain>().Any(ch => ch.ClientChainId == c.ChainId && ch.IsActive))
            .Select(c => c.ChainId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Lote 247 — como <see cref="ClientChainMapAsync"/>, pero solo con las cadenas ACTIVAS (cliente → cadena activa). <paramref name="clientIds"/> null = todos los de
    /// la compañía; vacío = ninguno.
    /// </summary>
    public static async Task<Dictionary<int, int>> ActiveClientChainMapAsync(TeikemDbContext db, IReadOnlyCollection<int>? clientIds, CancellationToken ct = default)
    {
        if (clientIds is { Count: 0 }) return new Dictionary<int, int>();
        var q = db.Clients.AsNoTracking().Where(c => c.ChainId != null && db.Set<ClientChain>().Any(ch => ch.ClientChainId == c.ChainId && ch.IsActive));
        if (clientIds is not null)
        {
            var ids = clientIds.Distinct().ToList();
            q = q.Where(c => ids.Contains(c.ClientId));
        }
        var rows = await q.Select(c => new { c.ClientId, ChainId = c.ChainId!.Value }).ToListAsync(ct);
        return rows.ToDictionary(x => x.ClientId, x => x.ChainId);
    }

    /// <summary>El id interno de la cadena por su PublicId; null si no existe o es de otra compañía.</summary>
    public static async Task<int?> ChainIdByPublicIdAsync(TeikemDbContext db, Guid publicId, CancellationToken ct = default)
        => await db.Set<ClientChain>().AsNoTracking()
            .Where(x => x.PublicId == publicId)
            .Select(x => (int?)x.ClientChainId)
            .FirstOrDefaultAsync(ct);

    /// <summary>Los ids internos de las cadenas por sus PublicId (los que no existen o son de otra compañía no salen).</summary>
    public static async Task<List<int>> ChainIdsByPublicIdsAsync(TeikemDbContext db, IReadOnlyCollection<Guid> publicIds, CancellationToken ct = default)
    {
        if (publicIds.Count == 0) return new List<int>();
        var ids = publicIds.Distinct().ToList();
        return await db.Set<ClientChain>().AsNoTracking()
            .Where(x => ids.Contains(x.PublicId))
            .Select(x => x.ClientChainId)
            .ToListAsync(ct);
    }
}
