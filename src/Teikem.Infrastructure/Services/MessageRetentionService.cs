using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 212 (B) — las fotos del chat viven poco: pasados Tenant.MessagePhotoDays días (30 por omisión) se BORRAN (se elimina el contenido del almacén y queda «Foto borrada el …» en el hilo) o se ARCHIVAN
/// (dejan de mostrarse en el chat pero el contenido se conserva, para cuando haya un almacén en frío). El texto de los mensajes nunca se borra. Lo que importa como evidencia no vive aquí.
/// </summary>
public sealed class MessageRetentionService(TeikemDbContext db, ITenantContext tenant, IFileStore files, ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;
    public const int BatchSize = 500;

    public async Task<MessagingSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var t = await CurrentTenantAsync(false, ct);
        return new MessagingSettingsDto(t.MessagePhotoDays, t.MessagePhotoAction);
    }

    public async Task<MessagingSettingsDto> UpdateSettingsAsync(MessagingSettingsRequest req, CancellationToken ct)
    {
        var t = await CurrentTenantAsync(true, ct);
        if (req.PhotoDays is int days)
        {
            if (days is < 1 or > 3650) throw new ValidationException("photoDays", MessagingRules.PhotoDaysMessage);
            t.MessagePhotoDays = days;
        }
        if (req.PhotoAction is not null)
            t.MessagePhotoAction = MessagingRules.NormalizePhotoAction(req.PhotoAction) ?? throw new ValidationException("photoAction", MessagingRules.PhotoActionMessage);
        await db.SaveChangesAsync(ct);
        return new MessagingSettingsDto(t.MessagePhotoDays, t.MessagePhotoAction);
    }

    private async Task<Teikem.Domain.Tenancy.Tenant> CurrentTenantAsync(bool track, CancellationToken ct)
    {
        var id = ((TenantContext)tenant).RequireTenantId();
        var q = track ? db.Tenants.AsTracking() : db.Tenants.AsNoTracking();
        return await q.FirstOrDefaultAsync(x => x.TenantId == id, ct) ?? throw new NotFoundException("Compañía", null, feminine: true);
    }

    /// <summary>Procesa las fotos vencidas de esta compañía (hasta 500 por corrida). Devuelve (borradas, archivadas).</summary>
    public async Task<(int Deleted, int Archived)> PurgeAsync(CancellationToken ct)
    {
        var t = await CurrentTenantAsync(false, ct);
        var tenantId = t.TenantId;
        var now = _clock.UtcNow;
        var cutoff = now.AddDays(-Math.Max(1, t.MessagePhotoDays));
        var due = await db.MessageAttachments.AsTracking().Where(a => a.State == MessageAttachmentStates.Active && a.CreatedAtUtc < cutoff).OrderBy(a => a.MessageAttachmentId).Take(BatchSize).ToListAsync(ct);
        if (due.Count == 0) return (0, 0);
        var archive = t.MessagePhotoAction == "ARCHIVE";
        foreach (var a in due)
        {
            if (archive)
            {
                a.State = MessageAttachmentStates.Archived;
            }
            else
            {
                if (a.StorageKey is not null) await files.DeleteAsync(tenantId, a.StorageKey, ct);
                if (a.ThumbStorageKey is not null) await files.DeleteAsync(tenantId, a.ThumbStorageKey, ct);
                a.StorageKey = null;
                a.ThumbStorageKey = null;
                a.State = MessageAttachmentStates.Deleted;
            }
            a.RemovedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
        return archive ? (0, due.Count) : (due.Count, 0);
    }
}

/// <summary>Trabajo programado: borra o archiva las fotos del chat que pasaron sus días de vida. Cada noche a las 03:45 (hora local).</summary>
public sealed class MessageAttachmentPurgeJob : Jobs.IScheduledJob
{
    public string Key => Jobs.JobKeys.MessageAttachmentPurge;
    public string NameEs => "Limpiar las fotos del chat";
    public string NameEn => "Clean up chat photos";
    public string DescriptionEs => "Borra o archiva (según el ajuste de la compañía) las fotos del chat más viejas que sus días de vida (30 por omisión). El texto no se borra.";
    public string DescriptionEn => "Deletes or archives (per company setting) chat photos older than their lifetime (30 days by default). Message text is never deleted.";
    public string DefaultKind => Teikem.Domain.Constants.JobScheduleKinds.Daily;
    public int? DefaultIntervalSeconds => null;
    public TimeSpan? DefaultDailyAt => new(3, 45, 0);
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(10);

    public async Task<string?> RunAsync(Jobs.JobRunContext context, CancellationToken ct)
    {
        var svc = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<MessageRetentionService>(context.Services);
        var (deleted, archived) = await svc.PurgeAsync(ct);
        return deleted + archived == 0 ? "Nada que limpiar." : $"{deleted} fotos borradas y {archived} archivadas.";
    }
}
