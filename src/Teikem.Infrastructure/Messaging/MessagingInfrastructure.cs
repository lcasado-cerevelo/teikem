using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Messaging;

/// <summary>
/// Lote 53 — empuja un evento a las personas conectadas EN VIVO (la web abierta). La implementación real vive en el API (SignalR); aquí
/// existe una nula para las pruebas y los comandos de consola. El empuje es un «aviso de que cambió algo»: el cliente vuelve a pedir lo
/// suyo, y si la conexión cae, el sondeo de respaldo (30 s) lo alcanza. Nunca lleva datos que el cliente no pueda pedir por la API.
/// </summary>
public interface IMessageLiveNotifier
{
    Task PushAsync(int tenantId, IReadOnlyCollection<int> userIds, string eventName, MessageLiveEvent payload, CancellationToken ct);
}

public sealed class NullMessageLiveNotifier : IMessageLiveNotifier
{
    public Task PushAsync(int tenantId, IReadOnlyCollection<int> userIds, string eventName, MessageLiveEvent payload, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>«Visto por última vez» de las personas (la web lo renueva cada ~30 s). En línea = visto hace menos de 90 s.</summary>
public sealed class PresenceService(TeikemDbContext db)
{
    public async Task PingAsync(int userId, DateTime nowUtc, CancellationToken ct)
    {
        var updated = await db.UserPresences.Where(p => p.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSeenUtc, nowUtc), ct);
        if (updated > 0) return;
        try
        {
            db.UserPresences.Add(new UserPresence { UserId = userId, LastSeenUtc = nowUtc });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // dos primeras señales a la vez (dos ventanas): la otra ya la creó
            db.ChangeTracker.Clear();
            await db.UserPresences.Where(p => p.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSeenUtc, nowUtc), ct);
        }
    }

    public async Task<Dictionary<int, DateTime>> LoadAsync(IReadOnlyCollection<int> userIds, CancellationToken ct)
        => userIds.Count == 0
            ? new Dictionary<int, DateTime>()
            : await db.UserPresences.AsNoTracking().Where(p => userIds.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.LastSeenUtc, ct);
}
