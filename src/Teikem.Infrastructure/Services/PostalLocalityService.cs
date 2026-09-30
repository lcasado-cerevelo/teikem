using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 1 (cambios de Almacén) — búsqueda en el catálogo global de localidades postales (dbo.PostalLocality, sembrado por el seed).
/// - Un solo campo: por ciudad postal o municipio (sin acentos ni mayúsculas: 'mayaguez' encuentra Mayagüez, 'toa baja'
///   encuentra también 00952 SABANA SECA) o por prefijo de código postal ('0094' → 00949, 00950...). Reglas en PostalLocalityRules.
/// - Catálogo USPS de ~42.500 filas, de solo lectura: se lee UNA vez y se guarda en memoria (IMemoryCache, 1 hora) con la ciudad y
///   el municipio ya normalizados, para que cada búsqueda sea un recorrido barato; la comparación sin acentos se hace en código,
///   igual en SQL Server que en pruebas, sin depender de la intercalación de la BD.
/// - A igual rango, primero Puerto Rico (el país por defecto de los almacenes), luego ciudad y ZIP.
/// - Global (sin TenantId): no hay filtro de tenant que aplicar; el país se resuelve con ILookupCache y su etiqueta en el idioma
///   del usuario.
/// </summary>
public sealed class PostalLocalityService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, IMemoryCache cache)
{
    private const string CacheKey = "postal-localities:v2";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private sealed record Row(int Id, string City, string PostalCode, string? State, string? Municipality, int CountryLookupId,
        string CityKey, string? MunicipalityKey);

    private sealed record Catalog(IReadOnlyList<Row> Rows, int? HomeCountryLookupId);

    public async Task<IReadOnlyList<PostalLocalityDto>> SearchAsync(string? search, int take, CancellationToken ct)
    {
        var catalog = await CatalogAsync(ct);
        var term = PostalLocalityRules.Normalize(search);
        // buscando por ZIP se ordena por ZIP; buscando por nombre, por ciudad y luego ZIP
        var byZip = PostalLocalityRules.IsPostalCodeSearch(term);
        var hits = catalog.Rows
            .Select(r => (Row: r, Rank: PostalLocalityRules.RankNormalized(term, r.CityKey, r.MunicipalityKey, r.PostalCode)))
            .Where(x => x.Rank is not null)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Row.CountryLookupId == catalog.HomeCountryLookupId ? 0 : 1)
            .ThenBy(x => byZip ? x.Row.PostalCode : x.Row.CityKey, StringComparer.Ordinal)
            .ThenBy(x => x.Row.PostalCode, StringComparer.Ordinal)
            .Take(PostalLocalityRules.Take(take))
            .Select(x => x.Row)
            .ToList();

        var list = new List<PostalLocalityDto>(hits.Count);
        foreach (var r in hits)
        {
            var country = await lookups.GetAsync(r.CountryLookupId, ct);
            list.Add(new PostalLocalityDto(r.Id, r.City, r.PostalCode, r.State, country?.InternalCode ?? "",
                country is null ? "" : MultilingualText.Resolve(country.LabelJson, tenant.Lang), r.Municipality));
        }
        return list;
    }

    private async Task<Catalog> CatalogAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out Catalog? cached) && cached is not null) return cached;
        var rows = (await db.Set<PostalLocality>().AsNoTracking()
                .Where(p => p.IsActive)
                .Select(p => new { p.PostalLocalityId, p.City, p.PostalCode, p.State, p.Municipality, p.CountryLookupId })
                .ToListAsync(ct))
            .Select(p => new Row(p.PostalLocalityId, p.City, p.PostalCode, p.State, p.Municipality, p.CountryLookupId,
                PostalLocalityRules.Normalize(p.City),
                string.IsNullOrWhiteSpace(p.Municipality) ? null : PostalLocalityRules.Normalize(p.Municipality)))
            .ToList();
        var home = await db.LookupCodes.AsNoTracking()
            .Where(l => l.Entity == LookupDomains.Country && l.InternalCode == Teikem.Domain.Wms.WarehouseRules.DefaultCountry)
            .Select(l => (int?)l.LookupCodeId)
            .FirstOrDefaultAsync(ct);
        var catalog = new Catalog(rows, home);
        cache.Set(CacheKey, catalog, CacheTtl);
        return catalog;
    }
}
