using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Clients;

/// <summary>La «tarjeta» de un cliente para un documento: dirección (líneas), teléfono, correo y nombre del contacto principal.</summary>
public sealed record ClientCard(IReadOnlyList<string> Address, string? Phone, string? Email, string? ContactName)
{
    public static readonly ClientCard Empty = new(Array.Empty<string>(), null, null, null);
}

/// <summary>Un correo al que se manda la copia al cliente de un aviso (con el nombre de la persona, si se sabe).</summary>
public sealed record ClientNoticeEmail(string Email, string? Name);

/// <summary>
/// Lote 243 — consultas de solo lectura sobre los contactos de un cliente para los avisos y documentos (el correo de la orden de venta;
/// las reutiliza el Lote D). Siempre A TRAVÉS DEL CLIENTE DEL TENANT: ClientContact no tiene TenantId, así que primero se comprueba que
/// el cliente sea de la compañía (TenantId explícito además del filtro global; el procesador de avisos corre sin usuario) y solo después
/// se leen sus contactos y medios de contacto (ContactPoint). Todo con AsNoTracking.
/// </summary>
public static class ClientContactQueries
{
    public const string EmailType = "EMAIL";
    public static readonly string[] PhoneTypes = { "PHONE", "MOBILE" };

    private sealed record Point(int OwnerEntityLookupId, int OwnerId, string Type, string Value, string? Extension, bool IsPrimary, int Id);

    private sealed record Loaded(Client Client, List<ClientContact> Contacts, List<Point> Points, int? ContactEntityId, int? ClientEntityId);

    private static async Task<Loaded?> LoadAsync(TeikemDbContext db, ILookupCache lookups, int tenantId, int clientId, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId && c.TenantId == tenantId, ct);
        if (client is null) return null;
        var contacts = await db.ClientContacts.AsNoTracking().Where(c => c.ClientId == client.ClientId && c.IsActive)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.ClientContactId).ToListAsync(ct);
        var contactEntity = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.ClientContact, ct);
        var clientEntity = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Client, ct);
        var contactIds = contacts.Select(c => c.ClientContactId).ToList();
        var rows = await db.ContactPoints.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive
                        && ((contactEntity != null && p.OwnerEntityLookupId == contactEntity && contactIds.Contains(p.OwnerId))
                            || (clientEntity != null && p.OwnerEntityLookupId == clientEntity && p.OwnerId == client.ClientId)))
            .OrderByDescending(p => p.IsPrimary).ThenBy(p => p.ContactPointId)
            .Select(p => new { p.OwnerEntityLookupId, p.OwnerId, p.ContactTypeLookupId, p.Value, p.Extension, p.IsPrimary, p.ContactPointId })
            .ToListAsync(ct);
        var typeCodes = new Dictionary<int, string>();
        foreach (var id in rows.Select(r => r.ContactTypeLookupId).Distinct())
            typeCodes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? string.Empty;
        var points = rows.Select(r => new Point(r.OwnerEntityLookupId, r.OwnerId, typeCodes.GetValueOrDefault(r.ContactTypeLookupId, string.Empty), r.Value, r.Extension, r.IsPrimary, r.ContactPointId)).ToList();
        return new Loaded(client, contacts, points, contactEntity, clientEntity);
    }

    private static IEnumerable<Point> OfContact(Loaded x, int contactId)
        => x.Points.Where(p => x.ContactEntityId is int ce && p.OwnerEntityLookupId == ce && p.OwnerId == contactId);

    private static IEnumerable<Point> OfClient(Loaded x)
        => x.Points.Where(p => x.ClientEntityId is int ce && p.OwnerEntityLookupId == ce && p.OwnerId == x.Client.ClientId);

    /// <summary>
    /// Dirección, teléfono, correo y contacto principal del cliente, con la misma regla de QuickBooks (que no se toca): dirección CORPORATE
    /// (física) y, si no hay, la BILLING (postal); teléfono y correo, primero los del contacto principal y luego los de los demás contactos
    /// y los del propio cliente (los marcados como principales primero). Un cliente de otra compañía (o inexistente) da la tarjeta vacía.
    /// <paramref name="phoneFormat"/> escribe el teléfono (p. ej. con la máscara de la compañía); null = tal cual.
    /// </summary>
    public static async Task<ClientCard> CardAsync(TeikemDbContext db, ILookupCache lookups, int tenantId, int clientId, CancellationToken ct,
        Func<string, string>? phoneFormat = null)
    {
        var x = await LoadAsync(db, lookups, tenantId, clientId, ct);
        if (x is null) return ClientCard.Empty;

        var corporate = await lookups.TryGetIdAsync(LookupDomains.LocationType, LocationTypes.Corporate, ct);
        var billing = await lookups.TryGetIdAsync(LookupDomains.LocationType, LocationTypes.Billing, ct);
        var typeIds = new[] { corporate, billing }.Where(t => t is not null).Select(t => t!.Value).ToList();
        var locations = typeIds.Count == 0 ? new List<Location>()
            : await db.Locations.AsNoTracking().Where(l => l.TenantId == tenantId && l.ClientId == x.Client.ClientId && l.IsActive && typeIds.Contains(l.LocationTypeLookupId))
                .OrderBy(l => l.LocationId).ToListAsync(ct);
        var address = locations.FirstOrDefault(l => l.LocationTypeLookupId == corporate) ?? locations.FirstOrDefault(l => l.LocationTypeLookupId == billing);

        var primary = x.Contacts.FirstOrDefault();
        var ordered = x.Points.OrderBy(p => primary is not null && x.ContactEntityId is int ce && p.OwnerEntityLookupId == ce && p.OwnerId == primary.ClientContactId ? 0 : 1).ToList();
        var phone = ordered.FirstOrDefault(p => PhoneTypes.Contains(p.Type));
        var phoneText = phone is null ? null : (phoneFormat?.Invoke(phone.Value) ?? phone.Value) + (string.IsNullOrWhiteSpace(phone.Extension) ? string.Empty : $" x{phone.Extension.Trim()}");
        var email = ordered.FirstOrDefault(p => p.Type == EmailType)?.Value?.Trim();
        return new ClientCard(AddressLines(address), phoneText, email, primary?.FullName);
    }

    /// <summary>Líneas de una dirección: calle, segunda línea y «ciudad estado código postal».</summary>
    public static IReadOnlyList<string> AddressLines(Location? l)
    {
        if (l is null) return Array.Empty<string>();
        var city = string.Join(" ", new[] { l.City, l.State, l.PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        return new[] { l.Line1, l.Line2, city }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
    }

    /// <summary>
    /// Correos de la copia al cliente de un aviso: el correo principal del contacto principal (activo); si no tiene, el correo principal
    /// registrado en el propio cliente. Más cada contacto ACTIVO marcado «Recibe avisos por correo» con un correo activo válido (el
    /// principal de ese contacto primero). Sin repetir (sin distinguir mayúsculas). Un cliente de otra compañía (o inexistente) no da ninguno.
    /// </summary>
    public static async Task<IReadOnlyList<ClientNoticeEmail>> NoticeEmailsAsync(TeikemDbContext db, ILookupCache lookups, int tenantId, int clientId, CancellationToken ct)
    {
        var x = await LoadAsync(db, lookups, tenantId, clientId, ct);
        if (x is null) return Array.Empty<ClientNoticeEmail>();
        var result = new List<ClientNoticeEmail>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? email, string? name)
        {
            if (!NotificationRules.IsValidEmail(email)) return;
            var e = email!.Trim();
            if (seen.Add(e)) result.Add(new ClientNoticeEmail(e, string.IsNullOrWhiteSpace(name) ? null : name.Trim()));
        }
        static string? FirstEmail(IEnumerable<Point> points) => points.Where(p => p.Type == EmailType && NotificationRules.IsValidEmail(p.Value)).Select(p => p.Value).FirstOrDefault();

        var primary = x.Contacts.FirstOrDefault(c => c.IsPrimary);
        var main = primary is null ? null : FirstEmail(OfContact(x, primary.ClientContactId));
        if (main is not null) Add(main, primary!.FullName);
        else Add(FirstEmail(OfClient(x)), x.Client.Name);
        foreach (var c in x.Contacts.Where(c => c.ReceivesEmailNotifications))
            Add(FirstEmail(OfContact(x, c.ClientContactId)), c.FullName);
        return result;
    }
}
