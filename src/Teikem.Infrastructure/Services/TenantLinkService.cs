using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 227 (P7 del pedido de vendedores) — los enlaces entre compañías (módulo INTERCOMPANY, Sistema → Integraciones / API → «Compañía enlazada»).
/// - Proponer: el administrador de una compañía elige otra a la que él también pertenece (no se enumeran las compañías de la plataforma) y dice el papel de la suya: CLIENT
///   («somos cliente de la otra») o PROVIDER («la otra es nuestra cliente», con el cliente propio que la representa). La propuesta es la aceptación de quien propone.
/// - Aceptar o rechazar: lo hace la OTRA compañía. Si la que acepta es la que da el transporte, elige su cliente (activo; uno por compañía enlazada). Solo entonces el
///   enlace queda ACTIVE: nada cruza sin las dos aceptaciones.
/// - Terminar: cualquiera de las dos, con motivo. Un enlace terminado o rechazado no se reabre (se propone otro).
/// La compañía sale SIEMPRE del principal: un enlace donde la compañía de quien mira no es ni cliente ni proveedor «no existe» (404, sin oráculo). Una compañía cliente tiene a
/// lo sumo un enlace propuesto o activo (despacha por una sola). Cada cambio avisa a la otra compañía (evento INTEGRATION_LINK_CHANGED, en su propio contexto).
/// </summary>
public sealed class TenantLinkService(TeikemDbContext db, ITenantContext tenant, ModuleService modules, LinkedTenantScope linked)
{
    private TenantContext T => (TenantContext)tenant;

    // ---------------------------------------------------------------- lectura

    /// <summary>Las otras compañías (activas) a las que pertenece quien mira; para la plataforma, todas las activas.</summary>
    public async Task<TenantLinkOptionsDto> OptionsAsync(CancellationToken ct)
    {
        var me = T.RequireTenantId();
        var ids = await CandidateTenantIdsAsync(me, ct);
        var tenants = await db.Tenants.AsNoTracking().Where(t => ids.Contains(t.TenantId)).OrderBy(t => t.Name).Select(t => new { t.TenantId, t.PublicId, t.Name }).ToListAsync(ct);
        var list = new List<TenantLinkCompanyDto>();
        foreach (var t in tenants)
        {
            var keys = await modules.GetEnabledKeysAsync(t.TenantId, ct);
            list.Add(new TenantLinkCompanyDto(t.PublicId, t.Name, keys.Contains(ModuleKeys.Intercompany), keys.Contains(ModuleKeys.LtlGround)));
        }
        var mine = await modules.GetEnabledKeysAsync(me, ct);
        return new TenantLinkOptionsDto(list, mine.Contains(ModuleKeys.LtlGround));
    }

    public async Task<IReadOnlyList<TenantLinkDto>> ListAsync(CancellationToken ct)
    {
        var me = T.RequireTenantId();
        var rows = await db.Set<TenantLink>().AsNoTracking().Where(l => l.ClientTenantId == me || l.ProviderTenantId == me)
            .OrderByDescending(l => l.ProposedAtUtc).ThenByDescending(l => l.TenantLinkId).ToListAsync(ct);
        return await ToDtosAsync(me, rows, ct);
    }

    public async Task<TenantLinkDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var me = T.RequireTenantId();
        var row = await FindAsync(me, publicId, tracking: false, ct);
        return (await ToDtosAsync(me, new List<TenantLink> { row }, ct))[0];
    }

    // ---------------------------------------------------------------- proponer, aceptar, rechazar, terminar

    public async Task<TenantLinkDto> ProposeAsync(TenantLinkProposeRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var me = T.RequireTenantId();
        var role = TenantLinkRules.NormalizeRole(req.Role) ?? throw new ValidationException("role", TenantLinkRules.RoleInvalid);
        if (req.CompanyPublicId is not Guid otherGuid || otherGuid == Guid.Empty) throw new ValidationException("companyPublicId", TenantLinkRules.CompanyRequired);
        var (note, noteError) = TenantLinkRules.NormalizeNote(req.Note);
        if (noteError is not null) throw new ValidationException("note", noteError);

        var other = await db.Tenants.AsNoTracking().Where(t => t.PublicId == otherGuid && t.IsActive).Select(t => new { t.TenantId, t.Name }).FirstOrDefaultAsync(ct);
        if (other is not null && other.TenantId == me) throw new ValidationException("companyPublicId", TenantLinkRules.SameCompany);
        // la otra compañía debe ser una de las de quien propone y tener el módulo; si no, el mismo mensaje (no se revela si existe)
        if (other is null || !(await CandidateTenantIdsAsync(me, ct)).Contains(other.TenantId) || !(await modules.GetEnabledKeysAsync(other.TenantId, ct)).Contains(ModuleKeys.Intercompany))
            throw new ValidationException("companyPublicId", TenantLinkRules.CompanyNotAvailable);

        var (clientTenant, providerTenant) = role == TenantLinkCodes.RoleClient ? (me, other.TenantId) : (other.TenantId, me);
        if (!(await modules.GetEnabledKeysAsync(providerTenant, ct)).Contains(ModuleKeys.LtlGround)) throw new StatusRuleException(TenantLinkRules.ProviderNeedsTransport);
        int? providerClientId = null;
        if (role == TenantLinkCodes.RoleProvider)
        {
            if (req.ClientPublicId is not Guid clientGuid || clientGuid == Guid.Empty) throw new ValidationException("clientPublicId", TenantLinkRules.ClientRequired);
            providerClientId = await UsableClientAsync(clientGuid, ct);
            await EnsureClientFreeAsync(me, providerClientId.Value, null, ct);
        }
        if (await db.Set<TenantLink>().AsNoTracking().AnyAsync(l => l.ClientTenantId == clientTenant && TenantLinkStatuses.Open.Contains(l.Status), ct))
            throw new ConflictException(TenantLinkRules.AlreadyOpen);

        var row = new TenantLink
        {
            PublicId = Guid.NewGuid(), ClientTenantId = clientTenant, ProviderTenantId = providerTenant, ProviderClientId = providerClientId, Status = TenantLinkStatuses.Proposed,
            ProposedByTenantId = me, ProposedBy = tenant.UserId, ProposedAtUtc = DateTime.UtcNow, Note = note,
        };
        db.Set<TenantLink>().Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); throw new ConflictException(TenantLinkRules.AlreadyOpen); }   // UX_TenantLink_ClientOpen: otra propuesta llegó a la vez
        await NotifyOtherAsync(row, me, "PROPOSED", note, ct);
        return await GetAsync(row.PublicId, ct);
    }

    public async Task<TenantLinkDto> AcceptAsync(Guid publicId, TenantLinkAcceptRequest? req, CancellationToken ct)
    {
        var me = T.RequireTenantId();
        var (note, noteError) = TenantLinkRules.NormalizeNote(req?.Note);
        if (noteError is not null) throw new ValidationException("note", noteError);
        var row = await FindAsync(me, publicId, tracking: true, ct);
        if (row.Status != TenantLinkStatuses.Proposed) throw new StatusRuleException(TenantLinkRules.NotProposed);
        if (row.ProposedByTenantId == me) throw new StatusRuleException(TenantLinkRules.CannotRespondOwn);
        if (row.ProviderTenantId == me)
        {
            if (!(await modules.GetEnabledKeysAsync(me, ct)).Contains(ModuleKeys.LtlGround)) throw new StatusRuleException(TenantLinkRules.ProviderNeedsTransport);
            if (req?.ClientPublicId is not Guid clientGuid || clientGuid == Guid.Empty) throw new ValidationException("clientPublicId", TenantLinkRules.ClientRequired);
            var clientId = await UsableClientAsync(clientGuid, ct);
            await EnsureClientFreeAsync(me, clientId, row.TenantLinkId, ct);
            row.ProviderClientId = clientId;
        }
        row.Status = TenantLinkStatuses.Active;
        row.RespondedBy = tenant.UserId;
        row.RespondedAtUtc = DateTime.UtcNow;
        row.ResponseNote = note;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new ConflictException(TenantLinkRules.NotProposed); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); throw new ConflictException(TenantLinkRules.ClientAlreadyLinked); }   // UX_TenantLink_ProviderClient
        await NotifyOtherAsync(row, me, "ACCEPTED", note, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Rechazar una propuesta: la otra compañía la rechaza, o quien la propuso la retira. Con motivo.</summary>
    public async Task<TenantLinkDto> RejectAsync(Guid publicId, TenantLinkReasonRequest? req, CancellationToken ct)
    {
        var me = T.RequireTenantId();
        var reason = Reason(req);
        var row = await FindAsync(me, publicId, tracking: true, ct);
        if (row.Status != TenantLinkStatuses.Proposed) throw new StatusRuleException(TenantLinkRules.NotProposed);
        row.Status = TenantLinkStatuses.Rejected;
        row.RespondedBy = tenant.UserId;
        row.RespondedAtUtc = DateTime.UtcNow;
        row.ResponseNote = reason;
        await SaveOrConflictAsync(TenantLinkRules.NotProposed, ct);
        await NotifyOtherAsync(row, me, "REJECTED", reason, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Terminar un enlace activo (cualquiera de las dos, con motivo). Desde ese momento no se manda ni se trae nada; lo ya traído se queda como estaba.</summary>
    public async Task<TenantLinkDto> EndAsync(Guid publicId, TenantLinkReasonRequest? req, CancellationToken ct)
    {
        var me = T.RequireTenantId();
        var reason = Reason(req);
        var row = await FindAsync(me, publicId, tracking: true, ct);
        if (row.Status != TenantLinkStatuses.Active) throw new StatusRuleException(TenantLinkRules.NotActive);
        row.Status = TenantLinkStatuses.Ended;
        row.EndedByTenantId = me;
        row.EndedBy = tenant.UserId;
        row.EndedAtUtc = DateTime.UtcNow;
        row.EndReason = reason;
        await SaveOrConflictAsync(TenantLinkRules.NotActive, ct);
        await NotifyOtherAsync(row, me, "ENDED", reason, ct);
        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- apoyo

    /// <summary>Las otras compañías activas a las que pertenece el usuario (membresía ACTIVE); la plataforma las ve todas.</summary>
    private async Task<List<int>> CandidateTenantIdsAsync(int me, CancellationToken ct)
    {
        if (tenant.IsPlatformAdmin) return await db.Tenants.AsNoTracking().Where(t => t.IsActive && t.TenantId != me).Select(t => t.TenantId).ToListAsync(ct);
        if (tenant.UserId is not int uid) return new List<int>();
        var activeId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        if (activeId is null) return new List<int>();
        var mine = await db.UserTenants.AsNoTracking().IgnoreQueryFilters().Where(m => m.UserId == uid && m.StatusCodeId == activeId && m.TenantId != me).Select(m => m.TenantId).ToListAsync(ct);
        return await db.Tenants.AsNoTracking().Where(t => t.IsActive && mine.Contains(t.TenantId)).Select(t => t.TenantId).ToListAsync(ct);
    }

    /// <summary>Un cliente de la compañía del principal (filtro global), activo y en estatus ACTIVE (ni prospecto ni suspendido).</summary>
    private async Task<int> UsableClientAsync(Guid clientGuid, CancellationToken ct)
    {
        var activeId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.ClientStatus && s.InternalCode == ClientStatuses.Active).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        var c = await db.Clients.AsNoTracking().Where(x => x.PublicId == clientGuid).Select(x => new { x.ClientId, x.IsActive, x.StatusCodeId }).FirstOrDefaultAsync(ct);
        if (c is null || !c.IsActive || c.StatusCodeId != activeId) throw new ValidationException("clientPublicId", TenantLinkRules.ClientNotUsable);
        return c.ClientId;
    }

    private async Task EnsureClientFreeAsync(int providerTenantId, int clientId, int? exceptLinkId, CancellationToken ct)
    {
        if (await db.Set<TenantLink>().AsNoTracking().AnyAsync(l => l.ProviderTenantId == providerTenantId && l.ProviderClientId == clientId && TenantLinkStatuses.Open.Contains(l.Status)
                && (exceptLinkId == null || l.TenantLinkId != exceptLinkId), ct))
            throw new ConflictException(TenantLinkRules.ClientAlreadyLinked);
    }

    /// <summary>El enlace solo «existe» para las dos compañías que une; para cualquier otra, 404.</summary>
    private async Task<TenantLink> FindAsync(int me, Guid publicId, bool tracking, CancellationToken ct)
    {
        var q = db.Set<TenantLink>().Where(l => l.PublicId == publicId && (l.ClientTenantId == me || l.ProviderTenantId == me));
        return await (tracking ? q.AsTracking() : q.AsNoTracking()).FirstOrDefaultAsync(ct) ?? throw new NotFoundException(TenantLinkRules.NotFound);
    }

    private static string Reason(TenantLinkReasonRequest? req)
    {
        if (string.IsNullOrWhiteSpace(req?.Reason)) throw new ValidationException("reason", TenantLinkRules.ReasonRequired);
        var (value, error) = TenantLinkRules.NormalizeNote(req.Reason);
        if (error is not null) throw new ValidationException("reason", error);
        return value!;
    }

    private async Task SaveOrConflictAsync(string message, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new ConflictException(message); }
    }

    private async Task<IReadOnlyList<TenantLinkDto>> ToDtosAsync(int me, List<TenantLink> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<TenantLinkDto>();
        var otherIds = rows.Select(r => r.ClientTenantId == me ? r.ProviderTenantId : r.ClientTenantId).Distinct().ToList();
        var tenants = await db.Tenants.AsNoTracking().Where(t => otherIds.Contains(t.TenantId)).ToDictionaryAsync(t => t.TenantId, t => new { t.PublicId, t.Name }, ct);
        var userIds = rows.SelectMany(r => new[] { r.ProposedBy, r.RespondedBy }).Where(u => u is not null).Select(u => u!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        // el cliente que representa a la otra compañía: solo lo ve el proveedor (es su cliente; el filtro global ya lo acota a la compañía de quien mira)
        var clientIds = rows.Where(r => r.ProviderTenantId == me && r.ProviderClientId is not null).Select(r => r.ProviderClientId!.Value).Distinct().ToList();
        var clients = clientIds.Count == 0 ? new() : await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => new { c.PublicId, c.Name }, ct);
        // entregas: el cliente cuenta las suyas (TenantLinkOrder es de su compañía); el proveedor cuenta las órdenes nacidas del puente para ese cliente
        var linkIds = rows.Select(r => r.TenantLinkId).ToList();
        var asClient = await db.Set<TenantLinkOrder>().AsNoTracking().Where(o => linkIds.Contains(o.TenantLinkId)).GroupBy(o => o.TenantLinkId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var asProvider = new Dictionary<int, int>();
        if (clientIds.Count > 0)
        {
            var sourceType = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.EntityType && l.InternalCode == TenantLinkCodes.OrderEntityType).Select(l => (int?)l.LookupCodeId).FirstOrDefaultAsync(ct);
            if (sourceType is int st)
                asProvider = await db.TransportOrders.AsNoTracking().Where(o => o.SourceEntityTypeLookupId == st && clientIds.Contains(o.ClientId)).GroupBy(o => o.ClientId)
                    .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        }
        return rows.Select(r =>
        {
            var weAreClient = r.ClientTenantId == me;
            var otherId = weAreClient ? r.ProviderTenantId : r.ClientTenantId;
            var other = tenants.GetValueOrDefault(otherId);
            var client = !weAreClient && r.ProviderClientId is int pc && clients.TryGetValue(pc, out var cc) ? cc : null;
            var count = weAreClient ? asClient.GetValueOrDefault(r.TenantLinkId) : r.ProviderClientId is int pcid && r.Status != TenantLinkStatuses.Proposed ? asProvider.GetValueOrDefault(pcid) : 0;
            return new TenantLinkDto(r.PublicId, weAreClient ? TenantLinkCodes.RoleClient : TenantLinkCodes.RoleProvider, other?.PublicId ?? Guid.Empty, other?.Name ?? string.Empty, r.Status,
                r.ProposedByTenantId == me, r.ProposedBy is int pb ? users.GetValueOrDefault(pb) : null, r.ProposedAtUtc, r.Note,
                r.RespondedBy is int rb ? users.GetValueOrDefault(rb) : null, r.RespondedAtUtc, r.ResponseNote, r.EndedAtUtc, r.EndReason, r.EndedByTenantId == me,
                client?.PublicId, client?.Name, r.Status == TenantLinkStatuses.Proposed && r.ProposedByTenantId != me, r.Status == TenantLinkStatuses.Active, count);
        }).ToList();
    }

    /// <summary>Aviso a la OTRA compañía (en su propio contexto, sin ver ningún dato de clientes): quién hizo qué con el enlace. Una falla del aviso no deshace el cambio.</summary>
    private async Task NotifyOtherAsync(TenantLink row, int me, string action, string? note, CancellationToken ct)
    {
        var otherId = row.ClientTenantId == me ? row.ProviderTenantId : row.ClientTenantId;
        var myName = await db.Tenants.AsNoTracking().Where(t => t.TenantId == me).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var clientName = await db.Tenants.AsNoTracking().Where(t => t.TenantId == row.ClientTenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var providerName = await db.Tenants.AsNoTracking().Where(t => t.TenantId == row.ProviderTenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        if (!(await modules.GetEnabledKeysAsync(otherId, ct)).Contains(ModuleKeys.Intercompany)) return;
        try
        {
            await linked.RunAsync(otherId, null, async (sp, c) =>
            {
                var publisher = sp.GetService<INotificationPublisher>();
                if (publisher is null) return 0;
                var values = new Dictionary<string, string?>
                {
                    ["company"] = myName, ["client"] = clientName, ["provider"] = providerName, ["action"] = TenantLinkEventProvider.ActionText(action), ["note"] = note ?? string.Empty,
                };
                await publisher.PublishAsync(TenantLinkEventCodes.LinkChanged, values, $"TL-{row.PublicId:N}-{action}", c);
                await sp.GetRequiredService<TeikemDbContext>().SaveChangesAsync(c);
                return 0;
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // el aviso no deshace el cambio del enlace (ya guardado): se pierde solo el aviso
        }
    }
}
