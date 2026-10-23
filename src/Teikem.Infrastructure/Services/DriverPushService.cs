using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Avisos emergentes al teléfono del chofer (2026-10-23). La app registra su token de FCM por compañía (un teléfono por chofer y compañía; el mismo token en otro chofer del teléfono se pasa al nuevo);
/// un trabajo programado cada minuto manda los avisos de la bandeja que todavía no salieron (DriverNotice.PushedAtUtc nulo, de las últimas 24 horas). Si FCM no está configurado, no hace nada
/// y el aviso queda solo en la bandeja. Un token que FCM rechaza (la app se desinstaló) se desactiva.
/// </summary>
public sealed class DriverPushService(TeikemDbContext db, ITenantContext tenant, IPushSender push, ITenantClock? clock = null)
{
    public const string TokenRequiredMessage = "Falta el token del teléfono.";
    public const string PlatformInvalidMessage = "La plataforma debe ser ANDROID o IOS.";
    public const int MaxTokenLength = 400;
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    private async Task<Driver> CurrentDriverAsync(CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        return await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.IsActive, ct) ?? throw new ForbiddenException(DriverAppRules.NotADriverMessage);
    }

    /// <summary>Registra (o actualiza) el teléfono. Idempotente: el mismo token solo se guarda una vez en la compañía.</summary>
    public async Task RegisterDeviceAsync(DriverDeviceRegisterRequest? req, CancellationToken ct)
    {
        var token = req?.PushToken?.Trim();
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) throw new ValidationException("pushToken", TokenRequiredMessage);
        var platform = req?.Platform?.Trim().ToUpperInvariant();
        if (platform is not ("ANDROID" or "IOS")) throw new ValidationException("platform", PlatformInvalidMessage);
        var driver = await CurrentDriverAsync(ct);
        var platformId = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.DevicePlatform && l.InternalCode == platform).Select(l => l.LookupCodeId).FirstAsync(ct);
        // el mismo token en otro chofer de la compañía (teléfono compartido): pasa a este
        var same = await db.DriverDevices.AsTracking().Where(d => d.PushToken == token).ToListAsync(ct);
        var mine = same.FirstOrDefault(d => d.DriverId == driver.DriverId);
        foreach (var other in same.Where(d => d.DriverId != driver.DriverId && d.IsActive)) other.IsActive = false;
        if (mine is null)
            db.DriverDevices.Add(new DriverDevice { TenantId = ((TenantContext)tenant).RequireTenantId(), DriverId = driver.DriverId, PlatformLookupId = platformId, PushToken = token, AppVersion = Trim(req?.AppVersion, 30), LastSeenUtc = _clock.UtcNow });
        else
        {
            mine.IsActive = true; mine.PlatformLookupId = platformId; mine.AppVersion = Trim(req?.AppVersion, 30); mine.LastSeenUtc = _clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    private static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);

    /// <summary>
    /// Lote 212 (B): manda un aviso al teléfono de los choferes que son estos usuarios (un mensaje del chat). Sin Firebase configurado no hace nada. Un token rechazado se desactiva.
    /// Devuelve a cuántos teléfonos se mandó. Quien llama debe tratar cualquier fallo como no fatal: el mensaje ya quedó guardado.
    /// </summary>
    public async Task<int> SendToUsersAsync(IReadOnlyCollection<int> userIds, string title, string? body, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        if (!push.IsConfigured || userIds.Count == 0) return 0;
        // dos consultas: mezclar AsTracking con un AsNoTracking en el mismo join deja TODA la consulta sin seguimiento y el token muerto no se desactivaría
        var driverIds = await db.Drivers.AsNoTracking().Where(dr => dr.UserId != null && userIds.Contains(dr.UserId.Value)).Select(dr => dr.DriverId).ToListAsync(ct);
        if (driverIds.Count == 0) return 0;
        var devices = await db.DriverDevices.AsTracking().Where(d => driverIds.Contains(d.DriverId) && d.IsActive && d.PushToken != null).Take(50).ToListAsync(ct);
        var sent = 0;
        foreach (var d in devices)
        {
            var r = await push.SendAsync(d.PushToken!, title, body, data, ct);
            if (r == PushOutcome.InvalidToken) d.IsActive = false;
            else if (r == PushOutcome.Sent) sent++;
        }
        if (devices.Count > 0) await db.SaveChangesAsync(ct);
        return sent;
    }

    /// <summary>Manda los avisos pendientes de esta compañía. Devuelve (enviados, sin teléfono, fallidos por reintentar).</summary>
    public async Task<(int Sent, int NoDevice, int Retry)> DispatchPendingAsync(CancellationToken ct)
    {
        if (!push.IsConfigured) return (0, 0, 0);
        var since = _clock.UtcNow.AddHours(-24);
        var pending = await db.DriverNotices.AsTracking().Where(n => n.PushedAtUtc == null && n.CreatedAtUtc >= since).OrderBy(n => n.DriverNoticeId).Take(200).ToListAsync(ct);
        int sent = 0, noDevice = 0, retry = 0;
        foreach (var n in pending)
        {
            var devices = await db.DriverDevices.AsTracking().Where(d => d.DriverId == n.DriverId && d.IsActive && d.PushToken != null).ToListAsync(ct);
            if (devices.Count == 0) { n.PushedAtUtc = _clock.UtcNow; noDevice++; continue; }
            var data = new Dictionary<string, string> { ["kind"] = n.Kind, ["notice"] = n.PublicId.ToString() };
            var anyFailed = false;
            foreach (var d in devices)
            {
                var r = await push.SendAsync(d.PushToken!, n.Title, n.Body, data, ct);
                if (r == PushOutcome.InvalidToken) d.IsActive = false;
                else if (r == PushOutcome.Failed) anyFailed = true;
            }
            if (anyFailed) retry++;
            else { n.PushedAtUtc = _clock.UtcNow; sent++; }
        }
        if (pending.Count > 0) await db.SaveChangesAsync(ct);
        return (sent, noDevice, retry);
    }
}

/// <summary>Trabajo programado: manda cada minuto los avisos emergentes pendientes de los choferes.</summary>
public sealed class DriverPushDispatchJob : Jobs.IScheduledJob
{
    public string Key => Jobs.JobKeys.DriverPushDispatch;
    public string NameEs => "Avisos emergentes a los choferes";
    public string NameEn => "Driver push notifications";
    public string DescriptionEs => "Manda al teléfono de cada chofer los avisos nuevos de su bandeja (necesita Firebase configurado).";
    public string DescriptionEn => "Sends each driver's new notices to their phone (needs Firebase configured).";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 60;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(2);

    public async Task<string?> RunAsync(Jobs.JobRunContext context, CancellationToken ct)
    {
        var svc = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DriverPushService>(context.Services);
        var (sent, noDevice, retry) = await svc.DispatchPendingAsync(ct);
        return sent + noDevice + retry == 0 ? null : $"{sent} enviados, {noDevice} sin teléfono, {retry} por reintentar.";
    }
}
