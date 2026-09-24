using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Módulo 0B: ajustes de la compañía (defaults de captura, calendario laboral, política MFA/sesión, marca) y feriados.</summary>
public sealed class TenantService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    public async Task<TenantSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId, ct) ?? throw new NotFoundException("Compañía");
        return await ToDtoAsync(t, ct);
    }

    public async Task<TenantSettingsDto> UpdateSettingsAsync(TenantSettingsUpdateRequest req, CancellationToken ct)
    {
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId, ct) ?? throw new NotFoundException("Compañía");
        if (!string.IsNullOrWhiteSpace(req.Name)) t.Name = req.Name.Trim();
        if (req.LegalName is not null) t.LegalName = req.LegalName;
        if (req.TaxId is not null) t.TaxId = req.TaxId;
        if (!string.IsNullOrWhiteSpace(req.DefaultLangCode))
        {
            if (req.DefaultLangCode.Length != 2) throw new ValidationException("defaultLangCode", "Código de idioma de 2 letras.");
            t.DefaultLangCode = req.DefaultLangCode.ToLowerInvariant();
        }
        if (req.WorkDaysMask.HasValue) { if (req.WorkDaysMask.Value is 0 or > 127) throw new ValidationException("workDaysMask", "Máscara inválida (1-127)."); t.WorkDaysMask = req.WorkDaysMask.Value; }
        if (req.MaxStopsPerRouteDefault.HasValue) { if (req.MaxStopsPerRouteDefault.Value < 1) throw new ValidationException("maxStopsPerRouteDefault", "Debe ser ≥ 1."); t.MaxStopsPerRouteDefault = req.MaxStopsPerRouteDefault.Value; }
        if (req.DefaultServiceType is not null) t.DefaultServiceTypeLookupId = req.DefaultServiceType == "" ? null : await lookups.GetIdAsync(LookupDomains.ServiceType, req.DefaultServiceType, ct);
        if (req.DefaultPackageType is not null) t.DefaultPackageTypeLookupId = req.DefaultPackageType == "" ? null : await lookups.GetIdAsync(LookupDomains.PackageType, req.DefaultPackageType, ct);
        if (req.MfaRequired.HasValue) t.MfaRequired = req.MfaRequired.Value;
        if (req.Aal2WindowMinutes.HasValue) { if (req.Aal2WindowMinutes.Value is < 5 or > 240) throw new ValidationException("aal2WindowMinutes", "Entre 5 y 240 minutos."); t.Aal2WindowMinutes = req.Aal2WindowMinutes.Value; }
        if (req.SessionDays.HasValue) { if (req.SessionDays.Value is < 1 or > 365) throw new ValidationException("sessionDays", "Entre 1 y 365 días."); t.SessionDays = req.SessionDays.Value; }
        if (req.BrandingJson is not null)
        {
            if (req.BrandingJson.Length > 0)
            {
                try { using var _ = JsonDocument.Parse(req.BrandingJson); }
                catch (JsonException) { throw new ValidationException("brandingJson", "BrandingJson no es JSON válido."); }
                if (req.BrandingJson.Length > 200_000) throw new ValidationException("brandingJson", "BrandingJson demasiado grande (los logos van a blob storage).");
            }
            t.BrandingJson = req.BrandingJson.Length == 0 ? null : req.BrandingJson;
        }
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(t, ct);
    }

    public async Task<IReadOnlyList<TenantHolidayDto>> GetHolidaysAsync(int? year, CancellationToken ct)
    {
        var q = db.TenantHolidays.AsNoTracking().Where(h => h.IsActive);
        if (year.HasValue) q = q.Where(h => h.IsRecurring || h.HolidayDate.Year == year.Value);
        var list = await q.OrderBy(h => h.HolidayDate).ToListAsync(ct);
        return list.Select(h => new TenantHolidayDto(h.TenantHolidayId, h.HolidayDate, h.Name, h.IsRecurring)).ToList();
    }

    public async Task<TenantHolidayDto> AddHolidayAsync(TenantHolidayRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        var existing = await db.TenantHolidays.FirstOrDefaultAsync(h => h.HolidayDate == req.Date, ct);
        if (existing is not null)
        {
            existing.Name = req.Name.Trim(); existing.IsRecurring = req.IsRecurring; existing.IsActive = true;
            await db.SaveChangesAsync(ct);
            return new TenantHolidayDto(existing.TenantHolidayId, existing.HolidayDate, existing.Name, existing.IsRecurring);
        }
        var h = new TenantHoliday { TenantId = tenantId, HolidayDate = req.Date, Name = req.Name.Trim(), IsRecurring = req.IsRecurring };
        db.TenantHolidays.Add(h);
        await db.SaveChangesAsync(ct);
        return new TenantHolidayDto(h.TenantHolidayId, h.HolidayDate, h.Name, h.IsRecurring);
    }

    public async Task RemoveHolidayAsync(int id, CancellationToken ct)
    {
        var h = await db.TenantHolidays.FirstOrDefaultAsync(x => x.TenantHolidayId == id, ct) ?? throw new NotFoundException("Feriado", id);
        h.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Calendario laboral: ¿es día hábil? (WorkDaysMask + feriados, incluidos los recurrentes).</summary>
    public async Task<bool> IsWorkDayAsync(DateOnly date, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId, ct) ?? throw new NotFoundException("Compañía");
        var bit = 1 << (int)date.DayOfWeek; // Dom=1, Lun=2 ... Sáb=64
        if ((t.WorkDaysMask & bit) == 0) return false;
        return !await db.TenantHolidays.AsNoTracking().AnyAsync(h => h.IsActive && (h.HolidayDate == date || (h.IsRecurring && h.HolidayDate.Month == date.Month && h.HolidayDate.Day == date.Day)), ct);
    }

    /// <summary>Últimos N días hábiles terminando hoy (para tendencias sin barras muertas de fin de semana/feriado).</summary>
    public async Task<IReadOnlyList<DateOnly>> LastWorkDaysAsync(int n, CancellationToken ct)
    {
        var result = new List<DateOnly>();
        var d = DateOnly.FromDateTime(DateTime.UtcNow);
        var guard = 0;
        while (result.Count < n && guard++ < n * 5)
        {
            if (await IsWorkDayAsync(d, ct)) result.Add(d);
            d = d.AddDays(-1);
        }
        result.Reverse();
        return result;
    }

    private async Task<TenantSettingsDto> ToDtoAsync(Tenant t, CancellationToken ct) => new(
        t.TenantId, t.PublicId, t.Name, t.LegalName, t.TaxId, t.DefaultLangCode, t.WorkDaysMask, t.MaxStopsPerRouteDefault,
        t.DefaultServiceTypeLookupId is null ? null : (await lookups.GetAsync(t.DefaultServiceTypeLookupId.Value, ct))?.InternalCode,
        t.DefaultPackageTypeLookupId is null ? null : (await lookups.GetAsync(t.DefaultPackageTypeLookupId.Value, ct))?.InternalCode,
        t.MfaRequired, t.Aal2WindowMinutes, t.SessionDays, t.BrandingJson, t.IsActive);
}
