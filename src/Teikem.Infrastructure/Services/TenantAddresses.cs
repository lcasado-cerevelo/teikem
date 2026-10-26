using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>2026-10-26 — domicilio y dirección postal de la compañía (filas de dbo.Location sin cliente, tipos COMPANY y COMPANY_POSTAL). Las usan los ajustes de la compañía y el alta de una compañía nueva.</summary>
internal static class TenantAddresses
{
    /// <summary>
    /// Domicilio y dirección postal de la compañía (2026-10-26). Address = el domicilio (se crea o se corrige); PostalAddress = la dirección postal (se crea o se corrige) y
    /// PostalSameAsAddress = true la quita (vale el domicilio). Cada una es una fila de dbo.Location sin cliente, tipo COMPANY / COMPANY_POSTAL; quitar es baja lógica, nunca DELETE.
    /// </summary>
    internal static async Task ApplyAsync(TeikemDbContext db, ILookupCache lookups, Tenant t, TenantAddressInput? address, TenantAddressInput? postal, bool? postalSame, CancellationToken ct)
    {
        if (address is not null) t.CorporateLocationId = await UpsertAsync(db, lookups, t, t.CorporateLocationId, LocationTypes.Company, address, "address", ct);
        if (postal is not null && postalSame != true) t.BillingLocationId = await UpsertAsync(db, lookups, t, t.BillingLocationId, LocationTypes.CompanyPostal, postal, "postalAddress", ct);
        else if (postalSame == true && t.BillingLocationId is int billingId)
        {
            var loc = await db.Locations.FirstOrDefaultAsync(l => l.LocationId == billingId, ct);
            if (loc is not null) loc.IsActive = false;
            t.BillingLocationId = null;
        }
    }

    private static async Task<int> UpsertAsync(TeikemDbContext db, ILookupCache lookups, Tenant t, int? currentId, string typeCode, TenantAddressInput a, string field, CancellationToken ct)
    {
        if (TenantContactRules.ValidateAddress(a.Line1, a.Line2, a.City, a.State, a.PostalCode) is { } bad) throw new ValidationException($"{field}.{bad.Field}", bad.Message);
        var country = string.IsNullOrWhiteSpace(a.Country) ? TenantContactRules.DefaultCountry : a.Country.Trim().ToUpperInvariant();
        if (!CountryCode.IsValid(country)) throw new ValidationException($"{field}.country", CountryCode.InvalidMessage);
        var countryId = await lookups.GetIdAsync(LookupDomains.Country, country, ct);
        var typeId = await lookups.GetIdAsync(LookupDomains.LocationType, typeCode, ct);
        var loc = currentId is int id ? await db.Locations.FirstOrDefaultAsync(l => l.LocationId == id, ct) : null;
        if (loc is null)
        {
            loc = new Location { TenantId = t.TenantId, ClientId = null, CreatedAtUtc = DateTime.UtcNow };
            db.Locations.Add(loc);
        }
        loc.Name = t.Name;
        loc.LocationTypeLookupId = typeId;
        loc.Line1 = a.Line1!.Trim();
        loc.Line2 = string.IsNullOrWhiteSpace(a.Line2) ? null : a.Line2.Trim();
        loc.City = a.City!.Trim();
        loc.State = string.IsNullOrWhiteSpace(a.State) ? null : a.State.Trim();
        loc.PostalCode = string.IsNullOrWhiteSpace(a.PostalCode) ? null : a.PostalCode.Trim();
        loc.CountryLookupId = countryId;
        loc.IsActive = true;
        await db.SaveChangesAsync(ct);   // la compañía apunta a su id
        return loc.LocationId;
    }

    /// <summary>Si la compañía cambió de nombre, sus dos direcciones llevan el nombre nuevo.</summary>
    internal static async Task RenameAsync(TeikemDbContext db, Tenant t, CancellationToken ct)
    {
        var ids = new[] { t.CorporateLocationId, t.BillingLocationId }.Where(i => i.HasValue).Select(i => i!.Value).ToList();
        if (ids.Count == 0) return;
        foreach (var l in await db.Locations.Where(l => ids.Contains(l.LocationId)).ToListAsync(ct)) l.Name = t.Name;
    }

    internal static async Task<TenantAddressDto?> ReadAsync(TeikemDbContext db, string lang, int? locationId, CancellationToken ct)
    {
        if (locationId is not int id) return null;
        var l = await db.Locations.AsNoTracking().Include(x => x.Country).FirstOrDefaultAsync(x => x.LocationId == id && x.IsActive, ct);
        return l is null ? null : new TenantAddressDto(l.Line1, l.Line2, l.City, l.State, l.PostalCode, l.Country?.InternalCode ?? string.Empty, MultilingualText.Resolve(l.Country?.LabelJson, lang));
    }
}
