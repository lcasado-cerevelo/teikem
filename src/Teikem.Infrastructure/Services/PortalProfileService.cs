using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 120 (P10) — mi perfil en el portal: quién soy y los datos de mi empresa (solo lectura: los cambia la compañía). La persona solo cambia su NOMBRE; el correo es con el que entra (cambiarlo exige verificarlo: no se hace aquí) y la
/// contraseña usa su propio cambio (<c>PUT /auth/password</c>). Es del cliente y de la persona de la sesión.
/// </summary>
public sealed class PortalProfileService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    public const int MaxNameLength = 150;
    public const string NameRequired = "Escriba su nombre.";
    public const string NameTooLong = "El nombre no puede pasar de 150 caracteres.";

    private TenantContext T => (TenantContext)tenant;
    private int ClientId => T.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    public async Task<PortalProfileDto> GetAsync(CancellationToken ct)
    {
        var cid = ClientId;
        var userId = T.RequireUserId();
        var person = await db.Set<PortalUser>().AsNoTracking().Where(p => p.UserId == userId && p.ClientId == cid && p.IsActive).FirstOrDefaultAsync(ct) ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.FullName, u.Email }).FirstOrDefaultAsync(ct);
        var role = await lookups.GetAsync(person.RoleLookupId, ct);
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == cid, ct);
        var corporateId = await lookups.TryGetIdAsync(LookupDomains.LocationType, LocationTypes.Corporate, ct);
        var address = await db.Set<Location>().AsNoTracking().Where(l => l.ClientId == cid && l.IsActive && l.LocationTypeLookupId == corporateId).OrderBy(l => l.LocationId).FirstOrDefaultAsync(ct);
        return new PortalProfileDto(user?.FullName ?? person.FullName, user?.Email ?? person.Email, role?.InternalCode, MultilingualText.Resolve(role?.LabelJson, tenant.Lang) ?? role?.InternalCode,
            new PortalCompanyDto(client.Name, client.Code, client.LegalName, client.TaxId, address?.Line1, address?.Line2, address?.City, address?.State, address?.PostalCode));
    }

    public async Task<PortalProfileDto> SaveAsync(PortalProfileRequest? req, CancellationToken ct)
    {
        var name = req?.FullName?.Trim();
        if (string.IsNullOrEmpty(name)) throw new ValidationException("fullName", NameRequired);
        if (name.Length > MaxNameLength) throw new ValidationException("fullName", NameTooLong);
        var cid = ClientId;
        var userId = T.RequireUserId();
        var person = await db.Set<PortalUser>().AsTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.ClientId == cid && p.IsActive, ct) ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        person.FullName = name;
        // la misma persona puede ser de varios clientes: su nombre es uno solo
        foreach (var other in await db.Set<PortalUser>().IgnoreQueryFilters().AsTracking().Where(p => p.UserId == userId && p.PortalUserId != person.PortalUserId).ToListAsync(ct)) other.FullName = name;
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null) user.FullName = name;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }
}
