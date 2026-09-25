using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Clients;

/// <summary>
/// Consultas compartidas del Lote 2 sobre el DbContext (siempre bajo el filtro global de tenant).
/// Las usan ClientService, LocationService, ContractService, RateService, SpecialServiceService y PortalUserService.
/// </summary>
public static class ClientQueries
{
    // Caché de ids de estatus por instancia de DbContext (= por request, porque el contexto es scoped).
    private static readonly ConditionalWeakTable<TeikemDbContext, Dictionary<string, int>> StatusIds = new();

    /// <summary>Cliente del tenant por PublicId (lectura, sin tracking) o 404 'Cliente'.</summary>
    public static async Task<Client> ResolveClientAsync(this TeikemDbContext db, Guid publicId, CancellationToken ct)
        => await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct)
           ?? throw new NotFoundException("Cliente");

    /// <summary>Mensaje exacto (Lote 3, ajuste A) cuando se intenta crear algo nuevo sobre un cliente dado de baja.</summary>
    public const string ClientInactiveMessage = "El cliente está dado de baja; solo se consulta su historial.";

    /// <summary>
    /// Lote 3 (ajuste A): un cliente dado de baja (IsActive=0) conserva ficha, historial, órdenes, contratos y tarifas en
    /// consulta, pero no admite nada NUEVO (órdenes, invitaciones de portal, contratos, componentes/tramos de tarifa,
    /// servicios especiales) → 409. Las lecturas, cierres, cancelaciones, suspensiones y reactivaciones no pasan por aquí.
    /// </summary>
    public static Client EnsureClientActive(Client client)
    {
        if (!client.IsActive) throw new ConflictException(ClientInactiveMessage);
        return client;
    }

    /// <summary>Contrato del tenant por PublicId con su cliente (lectura, sin tracking) o 404 'Contrato'.</summary>
    public static async Task<Contract> ResolveContractAsync(this TeikemDbContext db, Guid publicId, CancellationToken ct)
        => await db.Contracts.AsNoTracking().Include(c => c.Client).FirstOrDefaultAsync(c => c.PublicId == publicId, ct)
           ?? throw new NotFoundException("Contrato");

    /// <summary>
    /// Contrato vigente del cliente en asOf: IsActive con estatus ACTIVE y StartDate &lt;= asOf. La fecha fin NO se evalúa:
    /// un contrato con EndDate pasada sigue vigente hasta que alguien lo pase a EXPIRED/CANCELLED (decisión de Luis).
    /// Si no hay ACTIVE, el DRAFT más reciente por StartDate (para configurar tarifas antes de activar).
    /// Nunca EXPIRED/CANCELLED; null si no existe. Lectura sin tracking.
    /// </summary>
    public static async Task<Contract?> CurrentContractAsync(this TeikemDbContext db, int clientId, DateOnly asOf, CancellationToken ct)
    {
        var activeId = await db.StatusIdAsync(StatusDomains.ContractStatus, ContractStatuses.Active, ct);
        var active = await db.Contracts.AsNoTracking()
            .Where(c => c.ClientId == clientId && c.IsActive && c.StatusCodeId == activeId && c.StartDate <= asOf)
            .OrderByDescending(c => c.StartDate).ThenByDescending(c => c.ContractId)
            .FirstOrDefaultAsync(ct);
        if (active is not null) return active;

        var draftId = await db.StatusIdAsync(StatusDomains.ContractStatus, ContractStatuses.Draft, ct);
        return await db.Contracts.AsNoTracking()
            .Where(c => c.ClientId == clientId && c.IsActive && c.StatusCodeId == draftId)
            .OrderByDescending(c => c.StartDate).ThenByDescending(c => c.ContractId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Id de un StatusCode por (dominio, código), con caché por request. 404 si el código no existe.</summary>
    public static async Task<int> StatusIdAsync(this TeikemDbContext db, string domain, string code, CancellationToken ct)
    {
        var cache = StatusIds.GetValue(db, _ => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var key = domain + "|" + code;
        if (cache.TryGetValue(key, out var id)) return id;
        var found = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == domain && s.InternalCode == code)
            .Select(s => (int?)s.StatusCodeId)
            .FirstOrDefaultAsync(ct);
        if (found is null) throw new NotFoundException($"Estatus {domain}", code);
        cache[key] = found.Value;
        return found.Value;
    }
}
