using Teikem.Domain.Catalogs;

namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// Caché en memoria de los LookupCode globales (TenantId NULL, sembrados) para resolver ids por (Entity, Code)
/// sin ir a la BD en cada auditoría/evento. Se invalida al editar catálogos.
/// </summary>
public interface ILookupCache
{
    Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default);
    Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default);
    Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default);
    Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default);
    void Invalidate();
}
