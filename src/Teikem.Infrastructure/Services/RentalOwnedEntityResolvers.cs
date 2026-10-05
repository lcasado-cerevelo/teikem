using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

// Lote 27 (Rentas) — resolvers de pertenencia de la renta, la devolución de renta y el proceso del equipo devuelto para las rutas
// polimórficas (contactos, campos personalizados, historial). Los tres llevan TenantId: se consultan bajo el filtro global; un id
// de otra compañía o inexistente responde false → 404.

public sealed class RentalOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Rental;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.Rentals.AsNoTracking().AnyAsync(r => r.RentalId == id, ct);
}

public sealed class RentalReturnOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.RentalReturn;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.RentalReturns.AsNoTracking().AnyAsync(r => r.RentalReturnId == id, ct);
}

public sealed class RentalProcessOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.RentalProcess;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.RentalProcesses.AsNoTracking().AnyAsync(p => p.RentalProcessId == id, ct);
}
