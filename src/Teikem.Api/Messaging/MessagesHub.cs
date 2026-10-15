using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Messaging;

/// <summary>
/// Lote 53 — la conexión en vivo de la web (SignalR, /hubs/messages). El token viaja en el query «access_token» (los WebSockets no pueden
/// mandar cabeceras). Cada conexión entra al grupo de SU persona EN SU compañía (el «tid» del token), así que un empuje nunca llega a
/// otra compañía. El cliente solo recibe avisos de que algo cambió y vuelve a pedirlo por la API (donde se aplican permisos y filtros):
/// el hub no devuelve datos y no acepta mensajes (escribir es POST /api/v1/messages/...). Ping() renueva la presencia.
/// </summary>
[Authorize]
public sealed class MessagesHub(PresenceService presence) : Hub
{
    public static string Group(int tenantId, int userId) => $"t{tenantId}:u{userId}";

    private bool TryIdentity(out int tenantId, out int userId)
    {
        var user = Context.User;
        tenantId = 0;
        userId = 0;
        return user is not null
            && int.TryParse(user.FindFirst(TeikemClaims.Subject)?.Value, out userId)
            && int.TryParse(user.FindFirst(TeikemClaims.TenantId)?.Value, out tenantId);
    }

    public override async Task OnConnectedAsync()
    {
        if (!TryIdentity(out var tenantId, out var userId))
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(tenantId, userId));
        await presence.PingAsync(userId, DateTime.UtcNow, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryIdentity(out _, out var userId)) await presence.PingAsync(userId, DateTime.UtcNow, CancellationToken.None);   // «visto por última vez»
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>La web avisa cada ~30 s que sigue abierta.</summary>
    public async Task Ping()
    {
        if (TryIdentity(out _, out var userId)) await presence.PingAsync(userId, DateTime.UtcNow, Context.ConnectionAborted);
    }
}

/// <summary>Empuja los eventos de mensajería a los grupos (persona × compañía) conectados.</summary>
public sealed class HubMessageLiveNotifier(IHubContext<MessagesHub> hub) : IMessageLiveNotifier
{
    public Task PushAsync(int tenantId, IReadOnlyCollection<int> userIds, string eventName, MessageLiveEvent payload, CancellationToken ct)
        => userIds.Count == 0 ? Task.CompletedTask : hub.Clients.Groups(userIds.Select(id => MessagesHub.Group(tenantId, id)).ToList()).SendAsync(eventName, payload, ct);
}
