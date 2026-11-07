using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 222 (P2 del pedido de vendedores, dictado de Luis §5 y §8) — el cliente que entra por un pedido:
/// - <b>Alta mínima</b> (clients.create_basic): negocio, razón social / DBA, contacto con su teléfono (10 dígitos), extensión, teléfono 2 y correo, SURI, dirección física y postal y
///   forma de pago (su término de pago). Nace <b>PROSPECTO</b> (StatusService.StartAtAsync, con historial) y avisa a quien aprueba (SALES_CLIENT_PENDING_APPROVAL).
///   Mientras es prospecto se le arman y solicitan pedidos (se reserva) pero no se despachan (SalesOrder.AwaitingClientApproval).
/// - <b>Aprobar</b> (sales.approve_client): → ACTIVO con su límite de crédito y término; la constancia (quién, límite, término, comentario) queda en el historial de estatus y
///   en la bitácora, y sus órdenes dejan de esperar.
/// - <b>Rechazar</b> (sales.approve_client): comentario obligatorio; se cancelan sus órdenes abiertas (borrador o solicitada, liberando lo reservado) y queda SUSPENDIDO.
/// Todo cambio de estatus va por StatusService. El TenantId sale del principal.
/// </summary>
public sealed class ClientProspectService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses, ClientService clients,
    LocationService locations, ContactPointService contacts, SalesOrderService sales, INotificationPublisher? notifications = null)
{
    private const int NameMax = 200, LegalNameMax = 250, ContactMax = 150;

    // ---------------------------------------------------------------- alta mínima

    public async Task<ClientDetailDto> CreateBasicAsync(ClientBasicCreateRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        // 1) todo se valida antes de crear nada
        var name = Require(req.Name, "name", "El nombre del negocio es obligatorio.", NameMax);
        var legalName = Optional(req.LegalName, "legalName", LegalNameMax);
        var contactName = Require(req.ContactName, "contactName", ClientProspectRules.ContactRequired, ContactMax);
        var (phone, phoneError) = ClientProspectRules.NormalizePhone10(req.Phone, required: true);
        if (phoneError is not null) throw new ValidationException("phone", phoneError);
        var (ext, extError) = ClientProspectRules.NormalizeExtension(req.PhoneExtension);
        if (extError is not null) throw new ValidationException("phoneExtension", extError);
        var (phone2, phone2Error) = ClientProspectRules.NormalizePhone10(req.Phone2, required: false);
        if (phone2Error is not null) throw new ValidationException("phone2", phone2Error);
        string? email = null;
        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            try { email = ContactPointService.ValidateValue("EMAIL", req.Email); }
            catch (ValidationException ex) { throw new ValidationException("email", ex.Message); }
        }
        var (suri, suriError) = ClientProspectRules.NormalizeMerchantRegistration(req.MerchantRegistration);
        if (suriError is not null) throw new ValidationException("merchantRegistration", suriError);
        var physical = req.PhysicalAddress;
        if (physical is null || string.IsNullOrWhiteSpace(physical.Line1) || string.IsNullOrWhiteSpace(physical.City))
            throw new ValidationException("physicalAddress", ClientProspectRules.AddressRequired);
        var postal = req.PostalAddress is { } pa && !string.IsNullOrWhiteSpace(pa.Line1) ? pa : null;
        if (postal is not null && string.IsNullOrWhiteSpace(postal.City)) throw new ValidationException("postalAddress.city", "La ciudad es obligatoria.");
        string? paymentTerm = null;
        if (!string.IsNullOrWhiteSpace(req.PaymentTerm))
        {
            paymentTerm = req.PaymentTerm.Trim().ToUpperInvariant();
            if (await lookups.TryGetIdAsync(LookupDomains.PaymentTerm, paymentTerm, ct) is null)
                throw new ValidationException("paymentTerm", $"Término de pago desconocido: '{req.PaymentTerm.Trim()}'.");
        }

        // 2) el cliente (nace PROSPECTO), su contacto con los teléfonos y el correo, y sus direcciones, en una sola transacción
        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var created = await clients.CreateAsync(new ClientCreateRequest(null, name, legalName, null, paymentTerm, null, null, null), ct2, bornAtStatus: ClientStatuses.Prospect);
            var client = await db.Clients.AsTracking().FirstAsync(c => c.PublicId == created.PublicId, ct2);
            client.MerchantRegistration = suri;
            var contact = new ClientContact { ClientId = client.ClientId, FullName = contactName, IsPrimary = true, IsActive = true };
            db.ClientContacts.Add(contact);
            await db.SaveGuardedAsync("Ya existe un contacto principal activo para este cliente.", ct2);
            // el vendedor no edita clientes (clients.update): la autorización es la del alta mínima
            await contacts.AddAsync(EntityTypes.ClientContact, contact.ClientContactId, new ContactPointUpsertRequest("PHONE", phone!, ext, null, true), ct2, enforceOwnerWrite: false);
            if (phone2 is not null) await contacts.AddAsync(EntityTypes.ClientContact, contact.ClientContactId, new ContactPointUpsertRequest("PHONE", phone2, null, null, false), ct2, enforceOwnerWrite: false);
            if (email is not null) await contacts.AddAsync(EntityTypes.ClientContact, contact.ClientContactId, new ContactPointUpsertRequest("EMAIL", email, null, null, true), ct2, enforceOwnerWrite: false);
            await locations.CreateCoreAsync(Address(name, LocationTypes.Corporate, physical), client.ClientId, ct2);
            if (postal is not null) await locations.CreateCoreAsync(Address(name, LocationTypes.Billing, postal), client.ClientId, ct2);
            if (notifications is not null)
                await notifications.PublishAsync(SalesEventCodes.ClientPendingApproval, new Dictionary<string, string?>
                {
                    ["client"] = client.Name, ["clientCode"] = client.Code, ["seller"] = await CurrentUserNameAsync(ct2),
                }, $"PROSPECT-{client.PublicId:N}", ct2);
            await db.SaveGuardedAsync("Ya existe un cliente con ese código.", ct2);
            return client.PublicId;
        }, ct);
        return await clients.GetAsync(publicId, ct);
    }

    private static LocationUpsertRequest Address(string name, string type, ClientBasicAddressRequest a)
        => new(null, null, name, type, a.Line1!.Trim(), a.Line2, a.City!.Trim(), string.IsNullOrWhiteSpace(a.State) ? "PR" : a.State, a.PostalCode, a.Country);

    // ---------------------------------------------------------------- aprobar y rechazar

    /// <summary>PROSPECTO → ACTIVO con su límite de crédito y término (lo que no viene no cambia). Sus órdenes dejan de esperar la aprobación y ya se pueden despachar.</summary>
    public async Task<ClientDetailDto> ApproveAsync(Guid publicId, ClientApproveRequest? req, CancellationToken ct)
    {
        req ??= new ClientApproveRequest();
        if (req.CreditLimit is < 0) throw new ValidationException("creditLimit", "El límite de crédito no puede ser negativo.");
        int? termId = null;
        string? termLabel = null;
        if (!string.IsNullOrWhiteSpace(req.PaymentTerm))
        {
            termId = await lookups.TryGetIdAsync(LookupDomains.PaymentTerm, req.PaymentTerm.Trim().ToUpperInvariant(), ct)
                     ?? throw new ValidationException("paymentTerm", $"Término de pago desconocido: '{req.PaymentTerm.Trim()}'.");
        }
        var comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();

        await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.Clients.AsTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct2) ?? throw new NotFoundException("Cliente");
            await EnsureProspectAsync(client, ct2);
            if (req.CreditLimit is decimal limit) client.CreditLimit = limit;
            if (termId is int t) client.PaymentTermLookupId = t;
            if (client.PaymentTermLookupId is int current) termLabel = Label(await lookups.GetAsync(current, ct2));
            var text = ClientProspectRules.ApprovalComment(await CurrentUserNameAsync(ct2), client.CreditLimit, termLabel, comment);
            var to = await statuses.TransitionAsync(StatusDomains.ClientStatus, EntityTypes.Client, client.ClientId, client.StatusCodeId, ClientStatuses.Active, Trim500(text), ct2);
            client.StatusCodeId = to.StatusCodeId;
            var waiting = await db.Set<SalesOrder>().AsTracking().Where(o => o.ClientId == client.ClientId && o.AwaitingClientApproval).ToListAsync(ct2);
            foreach (var o in waiting) o.AwaitingClientApproval = false;
            await db.SaveGuardedAsync("Ya existe un cliente con ese código.", ct2);
        }, ct);
        return await clients.GetAsync(publicId, ct);
    }

    /// <summary>Rechaza el prospecto: cancela sus órdenes abiertas (borrador o solicitadas, libera lo reservado) y lo deja SUSPENDIDO. El comentario es obligatorio.</summary>
    public async Task<ClientRejectResultDto> RejectAsync(Guid publicId, ClientRejectRequest? req, CancellationToken ct)
    {
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        if (comment is null) throw new ValidationException("comment", ClientProspectRules.RejectNeedsComment);
        if (comment.Length > StatusService.CommentMaxLength) throw new ValidationException("comment", StatusService.CommentTooLongMessage);
        var peek = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct) ?? throw new NotFoundException("Cliente");
        await EnsureProspectAsync(peek, ct);

        var cancelled = new List<string>();
        await db.RunInTransactionAsync(async ct2 =>
        {
            cancelled.Clear();
            var open = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == SalesCodes.StatusDomain && (s.InternalCode == SalesOrderStatuses.Draft || s.InternalCode == SalesOrderStatuses.Requested))
                .Select(s => s.StatusCodeId).ToListAsync(ct2);
            var orders = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.ClientId == peek.ClientId && open.Contains(o.StatusCodeId))
                .OrderBy(o => o.SalesOrderId).Select(o => new { o.PublicId, o.Number }).ToListAsync(ct2);
            foreach (var o in orders)
            {
                await sales.CancelAsync(o.PublicId, Trim500($"Cliente rechazado: {comment}"), ct2);
                cancelled.Add(o.Number);
            }
            // se carga después de cancelar: cada cancelación guarda por su cuenta
            var client = await db.Clients.AsTracking().FirstAsync(c => c.ClientId == peek.ClientId, ct2);
            await EnsureProspectAsync(client, ct2);
            var text = ClientProspectRules.RejectionComment(await CurrentUserNameAsync(ct2), cancelled.Count, comment);
            var to = await statuses.TransitionAsync(StatusDomains.ClientStatus, EntityTypes.Client, client.ClientId, client.StatusCodeId, ClientStatuses.Suspended, Trim500(text), ct2);
            client.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync("Ya existe un cliente con ese código.", ct2);
        }, ct);
        return new ClientRejectResultDto(await clients.GetAsync(publicId, ct), cancelled.ToList());
    }

    // ---------------------------------------------------------------- piezas

    private async Task EnsureProspectAsync(Client client, CancellationToken ct)
    {
        var code = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == client.StatusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct);
        if (code != ClientStatuses.Prospect) throw new StatusRuleException(ClientProspectRules.NotProspect);
    }

    private async Task<string> CurrentUserNameAsync(CancellationToken ct)
    {
        if (tenant.UserId is not int userId) return "sistema";
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName ?? u.Email).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(name) ? $"usuario {userId}" : name;
    }

    private string? Label(Teikem.Domain.Catalogs.LookupCode? l) => l is null ? null : MultilingualText.Resolve(l.LabelJson, tenant.Lang);

    private static string Trim500(string text) => text.Length <= StatusService.CommentMaxLength ? text : text[..StatusService.CommentMaxLength];

    private static string Require(string? value, string field, string message, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException(field, message);
        var v = value.Trim();
        if (v.Length > max) throw new ValidationException(field, $"No puede exceder {max} caracteres.");
        return v;
    }

    private static string? Optional(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > max) throw new ValidationException(field, $"No puede exceder {max} caracteres.");
        return v;
    }
}
