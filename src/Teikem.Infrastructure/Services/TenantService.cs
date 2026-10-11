using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Módulo 0B: ajustes de la compañía (defaults de captura, calendario laboral, política MFA/sesión, marca, región y formatos) y
/// feriados. Región y formatos (2026-10): reglas en TenantFormatRules (dominio); al guardar se invalida la zona en caché del
/// reloj de la compañía (TenantZoneCache) para que "hoy" use la zona nueva desde la siguiente petición.
/// </summary>
public sealed class TenantService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, ITenantClock? clock = null, TenantZoneCache? zones = null)
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
        if (req.DeviceSessionDays.HasValue) { if (req.DeviceSessionDays.Value is < 1 or > 365) throw new ValidationException("deviceSessionDays", "Entre 1 y 365 días."); t.DeviceSessionDays = req.DeviceSessionDays.Value; }
        if (req.BrandingJson is not null)
        {
            // Marca por compañía: las mismas reglas que la pantalla (BrandingRules, vectores compartidos con la web); vacío = quitarla.
            var brand = BrandingRules.Validate(req.BrandingJson);
            if (!brand.Ok) throw new ValidationException("brandingJson", brand.Message!);
            t.BrandingJson = req.BrandingJson.Length == 0 ? null : req.BrandingJson;
        }
        if (req.CountExpectedReveal is not null)
        {
            var (mode, modeError) = CountRevealRules.ParseMode(req.CountExpectedReveal);
            if (modeError is not null || mode is null) throw new ValidationException("countExpectedReveal", modeError ?? CountRevealRules.UnknownMode(req.CountExpectedReveal));
            t.CountExpectedReveal = mode;
        }
        if (req.CountRecountTolerancePct.HasValue)
        {
            var pct = req.CountRecountTolerancePct.Value;
            if (pct is < 0m or > 100m || decimal.Round(pct, 2) != pct) throw new ValidationException("countRecountTolerancePct", CountRevealRules.TolerancePctRange);
            t.CountRecountTolerancePct = pct;
        }
        if (req.CountRevealShowsNumber.HasValue) t.CountRevealShowsNumber = req.CountRevealShowsNumber.Value;
        if (req.CountAutoCloseMatching.HasValue) t.CountAutoCloseMatching = req.CountAutoCloseMatching.Value;
        await ApplyDefaultManualIssueReasonAsync(t, req.DefaultManualIssueReason, ct);
        if (req.DefaultProductCategoryId.HasValue) await ApplyDefaultProductCategoryAsync(t, req.DefaultProductCategoryId.Value, ct);
        var formatChanges = FormatChanges(req);
        if (!formatChanges.IsEmpty) ApplyFormat(t, TenantFormatRules.Read(t), formatChanges);
        await db.SaveChangesAsync(ct);
        if (!formatChanges.IsEmpty) zones?.Invalidate(t.TenantId);
        return await ToDtoAsync(t, ct);
    }

    /// <summary>
    /// «Motivo por default del despacho manual» (2026-10-11 b): null = sin cambio; vacío = quitar; un código = debe existir, estar
    /// activo y habilitado para la compañía (400 'El motivo {CÓDIGO} no existe o está inactivo.' en defaultManualIssueReason).
    /// Volver a mandar el código ya guardado no se revalida (guardar el resto de los ajustes nunca falla por un motivo que la
    /// compañía deshabilitó después). El cambio lo audita el interceptor (Tenant es [AuditEntity]).
    /// </summary>
    private async Task ApplyDefaultManualIssueReasonAsync(Tenant t, string? value, CancellationToken ct)
    {
        var (change, code) = PickBatchRules.NormalizeDefaultReason(value);
        if (!change) return;
        if (code is null) { t.DefaultManualIssueReasonLookupId = null; return; }
        if (t.DefaultManualIssueReasonLookupId is int current
            && string.Equals((await lookups.GetAsync(current, ct))?.InternalCode, code, StringComparison.OrdinalIgnoreCase)) return;
        t.DefaultManualIssueReasonLookupId = await ManualIssueReasonLookup.FindUsableIdAsync(db, code, ct)
            ?? throw new ValidationException("defaultManualIssueReason", PickBatchRules.ManualReasonUnknown(code));
    }

    /// <summary>Regiones con sus valores por defecto y valores permitidos de cada campo (pantalla Región y formatos).</summary>
    public TenantFormatOptionsDto GetFormatOptions() => new(
        TenantFormatRules.Regions.Select(r => ToFormatDto(TenantFormatRules.Defaults(r))).ToList(),
        TenantFormatRules.SymbolPositions.All, TenantFormatRules.CurrencyDecimalsAllowed.Select(b => (int)b).ToList(),
        TenantFormatRules.DateOrders.All, TenantFormatRules.DateSeparators, TenantFormatRules.TimeFormats.Select(b => (int)b).ToList(),
        TenantFormatRules.WeekStartDays.Select(b => (int)b).ToList(), TenantFormatRules.ThousandsSeparators, TenantFormatRules.DecimalSeparators);

    /// <summary>
    /// Aplica región y formatos sobre la compañía (regla de región de TenantFormatRules.Apply) o lanza el 400 con el mensaje exacto
    /// y el campo. Lo usa también el aprovisionamiento (partiendo de los valores por defecto de Puerto Rico).
    /// </summary>
    public static void ApplyFormat(Tenant t, TenantFormat current, TenantFormatChanges changes)
    {
        var (format, field, error) = TenantFormatRules.Apply(current, changes);
        if (format is null) throw new ValidationException(field!, error!);
        TenantFormatRules.Write(t, format);
    }

    private static TenantFormatChanges FormatChanges(TenantSettingsUpdateRequest r) => new(
        r.RegionCode, r.TimeZoneId, r.CurrencyCode, r.CurrencySymbol, r.CurrencySymbolPosition, r.CurrencyDecimals, r.DateOrder, r.DateSeparator,
        r.TimeFormat, r.WeekStartDay, r.ThousandsSeparator, r.DecimalSeparator, r.PhoneCountryCode, r.PhoneMask);

    private static TenantFormatDto ToFormatDto(TenantFormat f) => new(
        f.RegionCode, f.TimeZoneId, f.CurrencyCode, f.CurrencySymbol, f.CurrencySymbolPosition, f.CurrencyDecimals, f.DateOrder, f.DateSeparator,
        f.TimeFormat, f.WeekStartDay, f.ThousandsSeparator, f.DecimalSeparator, f.PhoneCountryCode, f.PhoneMask);

    public async Task<IReadOnlyList<TenantHolidayDto>> GetHolidaysAsync(int? year, CancellationToken ct)
    {
        var q = db.TenantHolidays.AsNoTracking().Where(h => h.IsActive);
        if (year.HasValue) q = q.Where(h => h.IsRecurring || h.HolidayDate.Year == year.Value);
        var list = await q.OrderBy(h => h.HolidayDate).ToListAsync(ct);
        return list.Select(h => new TenantHolidayDto(h.TenantHolidayId, h.HolidayDate, h.Name, h.IsRecurring)).ToList();
    }

    public const string HolidayExistsMessage = "Ya hay un feriado en esa fecha.";

    public async Task<TenantHolidayDto> AddHolidayAsync(TenantHolidayRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        var existing = await db.TenantHolidays.FirstOrDefaultAsync(h => h.HolidayDate == req.Date, ct);
        // Una fecha con feriado activo no se pisa en silencio: 409. Uno dado de baja (soft delete) se reactiva con los datos nuevos.
        if (existing is { IsActive: true })
            throw new ConflictException(HolidayExistsMessage) { Errors = new Dictionary<string, string[]> { ["date"] = [HolidayExistsMessage] } };
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

    /// <summary>
    /// Últimos N días hábiles terminando hoy (para tendencias sin barras muertas de fin de semana/feriado). Región y formatos
    /// (2026-10): "hoy" es el día local de la compañía (ITenantClock, su zona horaria), ya no el día UTC.
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> LastWorkDaysAsync(int n, CancellationToken ct)
    {
        var result = new List<DateOnly>();
        var d = (clock ?? TenantClock.Default).Today;
        var guard = 0;
        while (result.Count < n && guard++ < n * 5)
        {
            if (await IsWorkDayAsync(d, ct)) result.Add(d);
            d = d.AddDays(-1);
        }
        result.Reverse();
        return result;
    }

    /// <summary>
    /// «Categoría por defecto de los productos nuevos» (2026-10-11 c): 0 o menos = quitar; un id = debe existir (bajo el filtro de la
    /// compañía) y estar activa (400 en defaultProductCategoryId). Volver a mandar la ya guardada no se revalida: guardar el resto de los
    /// ajustes nunca falla por una categoría que se dio de baja después.
    /// </summary>
    private async Task ApplyDefaultProductCategoryAsync(Tenant t, int categoryId, CancellationToken ct)
    {
        if (categoryId <= 0) { t.DefaultProductCategoryId = null; return; }
        if (t.DefaultProductCategoryId == categoryId) return;
        var usable = await db.Set<ProductCategory>().AsNoTracking().AnyAsync(c => c.ProductCategoryId == categoryId && c.IsActive, ct);
        if (!usable) throw new ValidationException("defaultProductCategoryId", ProductRules.DefaultCategoryUnusable);
        t.DefaultProductCategoryId = categoryId;
    }

    /// <summary>La categoría por defecto guardada solo si sigue existiendo y activa (si no, null: la pantalla no preselecciona nada).</summary>
    private async Task<int?> UsableDefaultCategoryAsync(int? categoryId, CancellationToken ct)
        => categoryId is int id && await db.Set<ProductCategory>().AsNoTracking().AnyAsync(c => c.ProductCategoryId == id && c.IsActive, ct) ? id : null;

    private async Task<TenantSettingsDto> ToDtoAsync(Tenant t, CancellationToken ct) => new(
        t.TenantId, t.PublicId, t.Name, t.LegalName, t.TaxId, t.DefaultLangCode, t.WorkDaysMask, t.MaxStopsPerRouteDefault,
        t.DefaultServiceTypeLookupId is null ? null : (await lookups.GetAsync(t.DefaultServiceTypeLookupId.Value, ct))?.InternalCode,
        t.DefaultPackageTypeLookupId is null ? null : (await lookups.GetAsync(t.DefaultPackageTypeLookupId.Value, ct))?.InternalCode,
        t.MfaRequired, t.Aal2WindowMinutes, t.SessionDays, t.DeviceSessionDays, t.BrandingJson, t.IsActive,
        t.RegionCode, t.TimeZoneId, t.CurrencyCode, t.CurrencySymbol, t.CurrencySymbolPosition, t.CurrencyDecimals,
        t.DateOrder, t.DateSeparator, t.TimeFormat, t.WeekStartDay, t.ThousandsSeparator, t.DecimalSeparator,
        t.PhoneCountryCode, t.PhoneMask, !TenantFormatRules.MatchesRegionDefaults(TenantFormatRules.Read(t)),
        CountRevealRules.Normalize(t.CountExpectedReveal), t.CountRecountTolerancePct, t.CountRevealShowsNumber, t.CountAutoCloseMatching,
        await ManualIssueReasonLookup.UsableCodeAsync(db, t.DefaultManualIssueReasonLookupId, ct),
        await UsableDefaultCategoryAsync(t.DefaultProductCategoryId, ct));
}
