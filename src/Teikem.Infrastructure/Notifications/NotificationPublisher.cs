using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Lote 51 — cómo un módulo avisa que pasó algo. PublishAsync SOLO anota en la cola (NotificationOutbox) dentro del contexto del
/// llamador: se guarda y se confirma (o se revierte) junto con la operación de negocio, así que un aviso nunca deshace la operación
/// y una operación revertida no avisa nada. El envío lo hace después el procesador, fuera de la transacción.
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// eventCode: uno del catálogo (si no existe es un error de programación: InvalidOperationException). values: las {variables} del
    /// evento. dedupKey: si ya hay un aviso con la misma clave (de la compañía y el evento) no se anota otro; devuelve false.
    /// </summary>
    Task<bool> PublishAsync(string eventCode, IReadOnlyDictionary<string, string?>? values, string? dedupKey, CancellationToken ct);
}

public sealed class NotificationPublisher(TeikemDbContext db, ITenantContext tenant, NotificationEventCatalog catalog) : INotificationPublisher
{
    public const int MaxDedupKeyLength = 100;

    public async Task<bool> PublishAsync(string eventCode, IReadOnlyDictionary<string, string?>? values, string? dedupKey, CancellationToken ct)
    {
        var def = catalog.Find(eventCode) ?? throw new InvalidOperationException(NotificationRules.UnknownEventMessage(eventCode));
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var key = string.IsNullOrWhiteSpace(dedupKey) ? null : dedupKey.Trim();
        if (key is { Length: > MaxDedupKeyLength }) throw new InvalidOperationException($"La clave de no repetición no puede pasar de {MaxDedupKeyLength} caracteres.");
        if (key is not null)
        {
            var pending = db.NotificationOutbox.Local.Any(o => o.TenantId == tenantId && o.EventCode == def.Code && o.DedupKey == key);
            if (pending || await db.NotificationOutbox.AsNoTracking().AnyAsync(o => o.EventCode == def.Code && o.DedupKey == key, ct)) return false;
        }
        db.NotificationOutbox.Add(new NotificationOutbox
        {
            TenantId = tenantId,
            EventCode = def.Code,
            PayloadJson = NotificationRules.SerializeValues(values),
            DedupKey = key,
            NextAttemptUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = tenant.UserId,
        });
        return true;
    }
}
