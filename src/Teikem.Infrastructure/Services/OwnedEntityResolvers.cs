using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Resolvers de pertenencia del Lote 2 para asociaciones polimórficas (ContactPoint, CustomFieldValue):
/// responden si el id existe en el tenant activo. Client/Location/Contract llevan TenantId y quedan cubiertos por el
/// filtro global; ClientContact no lo lleva, así que se alcanza SIEMPRE a través de su Client (regla tenant-security).
/// </summary>
public sealed class ClientOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Client;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.Clients.AsNoTracking().AnyAsync(c => c.ClientId == id, ct);
}

public sealed class ClientContactOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.ClientContact;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => (from contact in db.ClientContacts.AsNoTracking()
            join client in db.Clients.AsNoTracking() on contact.ClientId equals client.ClientId // Client filtrado por tenant
            where contact.ClientContactId == id
            select contact.ClientContactId).AnyAsync(ct);
}

public sealed class LocationOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Location;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.Locations.AsNoTracking().AnyAsync(l => l.LocationId == id, ct);
}

public sealed class ContractOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Contract;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.Contracts.AsNoTracking().AnyAsync(c => c.ContractId == id, ct);
}
