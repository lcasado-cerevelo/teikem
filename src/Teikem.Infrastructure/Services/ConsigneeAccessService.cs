using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 117 — el cliente (desde el portal) le da acceso a un consignatario suyo, por invitación a un correo (decidido con Luis; docs/p10-portal-diseno.md). Reutiliza la invitación del portal (enlace de 48 h, un solo uso, el
/// reenvío mata el anterior): la fila de <c>PortalUser</c> lleva el consignatario (<c>LocationId</c>) y el rol CONSIGNEE. Solo el administrador u operador del cliente invita, reenvía o quita; quien solo consulta ve quién tiene acceso.
/// Todo se limita al cliente de la sesión: un consignatario de otro cliente responde 404 como si no existiera.
/// </summary>
public sealed class ConsigneeAccessService(TeikemDbContext db, ITenantContext tenant, PortalUserService portalUsers, ConsigneeNoticeService notices, ILookupCache lookups)
{
    public const string ReadOnlyMessage = "Su rol en el portal es de solo consulta: no puede dar acceso a un consignatario.";
    public const string NoEmailMessage = "Escriba el correo del consignatario: no tiene uno registrado.";
    public const string ConsigneeNotFoundMessage = "Consignatario";
    public const string AccessNotFoundMessage = "Acceso del consignatario";

    private TenantContext T => (TenantContext)tenant;
    private int ClientId => T.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    private async Task EnsureCanEditAsync(CancellationToken ct)
    {
        var userId = T.RequireUserId();
        var cid = ClientId;
        var role = await (from p in db.PortalUsers.AsNoTracking()
                          join r in db.LookupCodes.AsNoTracking() on p.RoleLookupId equals r.LookupCodeId
                          where p.UserId == userId && p.ClientId == cid && p.IsActive
                          select r.InternalCode).FirstOrDefaultAsync(ct);
        if (role is not (PortalRoles.ClientAdmin or PortalRoles.ClientOperator)) throw new ForbiddenException(ReadOnlyMessage);
    }

    /// <summary>El consignatario (localización de entrega) del cliente de la sesión; 404 si es de otro cliente o no existe.</summary>
    private async Task<Location> ConsigneeAsync(Guid publicId, CancellationToken ct)
    {
        var cid = ClientId;
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Delivery, ct);
        return await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.PublicId == publicId && l.ClientId == cid && l.IsActive && l.LocationTypeLookupId == deliveryTypeId, ct)
               ?? throw new NotFoundException(ConsigneeNotFoundMessage);
    }

    private async Task<Guid> ClientPublicIdAsync(CancellationToken ct)
    {
        var cid = ClientId;
        return await db.Clients.AsNoTracking().Where(c => c.ClientId == cid).Select(c => c.PublicId).FirstOrDefaultAsync(ct);
    }

    private ConsigneeAccessDto ToDto(PortalUser p) => new(p.PortalUserId, p.Email, p.FullName, p.Status?.InternalCode ?? "", MultilingualText.Resolve(p.Status?.LabelJson, tenant.Lang), p.LastLoginUtc);

    /// <summary>Quién tiene acceso a ese consignatario (las bajas definitivas no se listan).</summary>
    public async Task<IReadOnlyList<ConsigneeAccessDto>> ListAsync(Guid locationPublicId, CancellationToken ct)
    {
        var loc = await ConsigneeAsync(locationPublicId, ct);
        var rows = await db.PortalUsers.AsNoTracking().Include(p => p.Status)
            .Where(p => p.ClientId == loc.ClientId && p.LocationId == loc.LocationId && p.IsActive && p.Status!.InternalCode != PortalUserStatuses.Disabled)
            .OrderBy(p => p.Email).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// Invita a un correo a entrar al portal como ese consignatario. Sin correo escrito usa el principal que el consignatario ya tiene registrado. 400 sin correo o con uno inválido; 409 si ese correo no está disponible
    /// (ya tiene acceso, o es de otra compañía o del staff: el mismo mensaje para todo, sin revelar dónde está).
    /// </summary>
    public async Task<ConsigneeAccessInviteResultDto> InviteAsync(Guid locationPublicId, ConsigneeAccessInviteRequest? req, CancellationToken ct)
    {
        await EnsureCanEditAsync(ct);
        var loc = await ConsigneeAsync(locationPublicId, ct);
        var email = req?.Email?.Trim();
        if (string.IsNullOrEmpty(email)) email = (await notices.ContactsAsync(loc.LocationId, ct)).Email;
        if (string.IsNullOrWhiteSpace(email)) throw new ValidationException("email", NoEmailMessage);
        var result = await portalUsers.InviteAsync(await ClientPublicIdAsync(ct), new PortalInviteRequest(email, req?.FullName?.Trim() is { Length: > 0 } n ? n : loc.Name, PortalRoles.Consignee), ct, loc.LocationId);
        return new ConsigneeAccessInviteResultDto(await LoadAsync(loc, result.User.Id, ct), result.InviteToken, result.ExpiresAtUtc);
    }

    /// <summary>Reenvía la invitación (el enlace anterior deja de servir). 409 si ya la aceptó.</summary>
    public async Task<ConsigneeAccessInviteResultDto> ResendAsync(Guid locationPublicId, int accessId, CancellationToken ct)
    {
        await EnsureCanEditAsync(ct);
        var loc = await ConsigneeAsync(locationPublicId, ct);
        await LoadAsync(loc, accessId, ct);   // 404 si no es de ese consignatario
        var result = await portalUsers.ResendInviteAsync(await ClientPublicIdAsync(ct), accessId, ct);
        return new ConsigneeAccessInviteResultDto(await LoadAsync(loc, accessId, ct), result.InviteToken, result.ExpiresAtUtc);
    }

    /// <summary>Quita el acceso (baja definitiva de esa persona; el consignatario sigue existiendo y puede recibir avisos).</summary>
    public async Task RemoveAsync(Guid locationPublicId, int accessId, CancellationToken ct)
    {
        await EnsureCanEditAsync(ct);
        var loc = await ConsigneeAsync(locationPublicId, ct);
        await LoadAsync(loc, accessId, ct);
        await portalUsers.RemoveAsync(await ClientPublicIdAsync(ct), accessId, ct);
    }

    private async Task<ConsigneeAccessDto> LoadAsync(Location loc, int accessId, CancellationToken ct)
    {
        var row = await db.PortalUsers.AsNoTracking().Include(p => p.Status)
                      .FirstOrDefaultAsync(p => p.PortalUserId == accessId && p.ClientId == loc.ClientId && p.LocationId == loc.LocationId, ct)
                  ?? throw new NotFoundException(AccessNotFoundMessage, accessId);
        return ToDto(row);
    }
}
