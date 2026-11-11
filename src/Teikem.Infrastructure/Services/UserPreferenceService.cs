using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Preferencias propias de la persona en la compañía activa (<see cref="UserPreference"/>): leer y guardar una clave permitida. Nunca de otra persona.</summary>
public sealed class UserPreferenceService(TeikemDbContext db, ITenantContext tenant)
{
    public const string InvalidValueMessage = "El valor de la preferencia no es válido.";

    private int RequireUser() => tenant.UserId ?? throw new ForbiddenException("Se requiere una sesión.");

    public async Task<UserPreferenceDto> GetAsync(string key, CancellationToken ct)
    {
        if (!UserPreferenceKeys.IsKnown(key)) throw new NotFoundException("Preferencia", key, true);
        var userId = RequireUser();
        var value = await db.UserPreferences.AsNoTracking().Where(p => p.UserId == userId && p.PrefKey == key).Select(p => p.PrefValue).FirstOrDefaultAsync(ct);
        return new UserPreferenceDto(key, value);
    }

    public async Task<UserPreferenceDto> SetAsync(string key, string? value, CancellationToken ct)
    {
        if (!UserPreferenceKeys.IsKnown(key)) throw new NotFoundException("Preferencia", key, true);
        var v = (value ?? string.Empty).Trim();
        if (!UserPreferenceKeys.IsValid(key, v)) throw new ValidationException("value", InvalidValueMessage);
        var userId = RequireUser();
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("Se requiere una compañía activa.");
        var row = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.PrefKey == key, ct);
        if (row is null) db.UserPreferences.Add(new UserPreference { TenantId = tenantId, UserId = userId, PrefKey = key, PrefValue = v });
        else if (row.PrefValue != v) { row.PrefValue = v; row.UpdatedAtUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
        return new UserPreferenceDto(key, v);
    }
}
