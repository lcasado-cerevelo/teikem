using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Interruptor global del servidor (<c>Auth:TrustedNetworks</c>): apagado por defecto; apagado, la lista se carga pero NO salta el código MFA.</summary>
public sealed class TrustedNetworkOptions
{
    public bool Enabled { get; set; }
}

/// <summary>Entrada vigente que cubre una IP: lo que el login anota en la bitácora.</summary>
public sealed record TrustedNetworkMatch(string Name, string Cidr);

/// <summary>
/// IP preferida / red de confianza (2026-10-12; docs/ip-preferida-mfa-diseno.md). Administra la lista por compañía (alta, cambio, baja, máximo de
/// entradas) y decide si una IP está en ella. Cada cambio queda en la bitácora de seguridad (TRUSTED_NETWORK), en la auditoría de la entidad y se avisa
/// por correo a los administradores de la compañía (si el correo falla, el cambio NO se revierte). El permiso (admin.tenant) y el MFA reciente
/// ([RequireAal2]) los exige el controlador.
/// </summary>
public sealed class TrustedNetworkService(TeikemDbContext db, ITenantContext tenant, ISecurityEventWriter security, IOptions<TrustedNetworkOptions> options,
    ITransactionalEmailSender? email = null, ILogger<TrustedNetworkService>? logger = null)
{
    public const string DuplicateMessage = "Esa IP o rango ya está en la lista";
    public static string LimitReachedMessage(int max) => $"Esta compañía llegó a su máximo de {max} entradas.";
    public const string LimitNegativeMessage = "El máximo no puede ser negativo.";

    /// <summary>El interruptor global del servidor.</summary>
    public bool FeatureEnabled => options.Value.Enabled;

    // ================================================================ login

    /// <summary>
    /// ¿La IP está en una entrada ACTIVA y vigente de la compañía? Null si el interruptor global está apagado, la IP no se pudo leer o no hay
    /// coincidencia. Lo llama el login (anónimo: por eso se pasa el tenant y se ignora el filtro global).
    /// </summary>
    public async Task<TrustedNetworkMatch?> MatchAsync(int tenantId, string? ip, CancellationToken ct)
    {
        if (!FeatureEnabled) return null;
        return await MatchCoreAsync(tenantId, ip, ct);
    }

    private async Task<TrustedNetworkMatch?> MatchCoreAsync(int tenantId, string? ip, CancellationToken ct)
    {
        var address = TrustedNetworkRules.NormalizeIp(ip);
        if (address is null) return null;
        var now = DateTime.UtcNow;
        var rows = await db.TenantTrustedNetworks.AsNoTracking().IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.IsActive && (n.ExpiresAtUtc == null || n.ExpiresAtUtc > now))
            .OrderBy(n => n.TenantTrustedNetworkId)
            .Select(n => new { n.Name, n.Cidr }).ToListAsync(ct);
        foreach (var r in rows)
        {
            var parsed = TrustedNetworkRules.Parse(r.Cidr).Cidr;
            if (parsed is not null && TrustedNetworkRules.Contains(parsed, address)) return new TrustedNetworkMatch(r.Name, parsed.Text);
        }
        return null;
    }

    // ================================================================ lectura

    public async Task<TrustedNetworksDto> GetAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var rows = await db.TenantTrustedNetworks.AsNoTracking().OrderBy(n => n.Name).ThenBy(n => n.TenantTrustedNetworkId).ToListAsync(ct);
        var max = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.TrustedNetworkMaxEntries).FirstOrDefaultAsync(ct);
        var current = TrustedNetworkRules.NormalizeIp(tenant.IpAddress);
        var confirmerIds = rows.Where(r => r.InternalOnlyConfirmedByUserId.HasValue).Select(r => r.InternalOnlyConfirmedByUserId!.Value).Distinct().ToList();
        var names = confirmerIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => confirmerIds.Contains(u.Id)).Select(u => new { u.Id, Name = u.FullName ?? u.Email ?? u.UserName ?? "" })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var now = DateTime.UtcNow;
        var items = rows.Select(r => ToDto(r, current, names, now)).ToList();
        var covering = items.FirstOrDefault(i => i.ContainsCurrentIp && i.IsActive && !i.IsExpired);
        return new TrustedNetworksDto(items, max, FeatureEnabled, current?.ToString(), covering?.Name);
    }

    private static TrustedNetworkDto ToDto(TenantTrustedNetwork n, IPAddress? current, IReadOnlyDictionary<int, string> names, DateTime now)
    {
        var cidr = TrustedNetworkRules.Parse(n.Cidr).Cidr;
        return new TrustedNetworkDto(n.PublicId, n.Name, n.Cidr, n.IsActive, n.ExpiresAtUtc, n.ExpiresAtUtc is DateTime e && e <= now, n.Note,
            n.InternalOnlyConfirmedAtUtc, n.InternalOnlyConfirmedByUserId is int u && names.TryGetValue(u, out var nm) ? nm : null, n.CreatedAtUtc,
            cidr is null ? "0" : TrustedNetworkRules.AddressCount(cidr), cidr is not null && TrustedNetworkRules.Contains(cidr, current));
    }

    // ================================================================ alta, cambio y baja

    public async Task<TrustedNetworkDto> CreateAsync(TrustedNetworkRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var userId = ((TenantContext)tenant).RequireUserId();
        var errors = new Dictionary<string, string[]>();
        var name = ValidateName(req.Name, true, errors);
        var cidr = ValidateCidr(req.Cidr, errors);
        var note = ValidateNote(req.Note, errors);
        ValidateExpiry(req.ExpiresAtUtc, errors);
        if (req.ConfirmInternalOnly != true) errors["confirmInternalOnly"] = new[] { TrustedNetworkRules.NeedsInternalOnlyConfirmation };
        if (errors.Count > 0) throw new ValidationException(errors);

        if (await db.TenantTrustedNetworks.AnyAsync(n => n.Cidr == cidr!.Text, ct)) throw new ConflictException($"{DuplicateMessage}.");
        var max = await MaxEntriesAsync(tenantId, ct);
        if (max is int m && await db.TenantTrustedNetworks.CountAsync(ct) >= m) throw new ConflictException(LimitReachedMessage(m));

        var now = DateTime.UtcNow;
        var entity = new TenantTrustedNetwork
        {
            TenantId = tenantId, Name = name!, Cidr = cidr!.Text, IsActive = req.IsActive ?? true, ExpiresAtUtc = req.ExpiresAtUtc, Note = note,
            InternalOnlyConfirmedByUserId = userId, InternalOnlyConfirmedAtUtc = now, CreatedAtUtc = now, CreatedBy = userId,
        };
        db.TenantTrustedNetworks.Add(entity);
        await db.SaveChangesAsync(ct);
        await RecordAsync("created", entity, tenantId, userId, new { confirmedInternalOnly = true, active = entity.IsActive, expiresAtUtc = entity.ExpiresAtUtc }, ct);
        return await GetOneAsync(entity.PublicId, ct);
    }

    public async Task<TrustedNetworkDto> UpdateAsync(Guid publicId, TrustedNetworkRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var userId = ((TenantContext)tenant).RequireUserId();
        var entity = await db.TenantTrustedNetworks.FirstOrDefaultAsync(n => n.PublicId == publicId, ct) ?? throw new NotFoundException("Red de confianza", null, true);
        var errors = new Dictionary<string, string[]>();
        var before = new { name = entity.Name, cidr = entity.Cidr, active = entity.IsActive, expiresAtUtc = entity.ExpiresAtUtc };

        if (req.Name is not null) entity.Name = ValidateName(req.Name, false, errors) ?? entity.Name;
        var cidrChanged = false;
        if (req.Cidr is not null)
        {
            var cidr = ValidateCidr(req.Cidr, errors);
            if (cidr is not null && cidr.Text != entity.Cidr)
            {
                if (req.ConfirmInternalOnly != true) errors["confirmInternalOnly"] = new[] { TrustedNetworkRules.NeedsInternalOnlyConfirmation };
                else if (await db.TenantTrustedNetworks.AnyAsync(n => n.Cidr == cidr.Text && n.PublicId != publicId, ct)) throw new ConflictException($"{DuplicateMessage}.");
                else { entity.Cidr = cidr.Text; cidrChanged = true; }
            }
        }
        if (req.IsActive is bool active) entity.IsActive = active;
        if (req.ClearExpiry) entity.ExpiresAtUtc = null;
        else if (req.ExpiresAtUtc is not null) { ValidateExpiry(req.ExpiresAtUtc, errors); entity.ExpiresAtUtc = req.ExpiresAtUtc; }
        if (req.Note is not null) entity.Note = ValidateNote(req.Note, errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        if (cidrChanged) { entity.InternalOnlyConfirmedByUserId = userId; entity.InternalOnlyConfirmedAtUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
        var action = req.IsActive is false && before.active ? "deactivated" : req.IsActive is true && !before.active ? "activated" : "updated";
        await RecordAsync(action, entity, tenantId, userId, new { before, confirmedInternalOnly = cidrChanged ? true : (bool?)null }, ct);
        return await GetOneAsync(entity.PublicId, ct);
    }

    public async Task DeleteAsync(Guid publicId, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var userId = ((TenantContext)tenant).RequireUserId();
        var entity = await db.TenantTrustedNetworks.FirstOrDefaultAsync(n => n.PublicId == publicId, ct) ?? throw new NotFoundException("Red de confianza", null, true);
        db.TenantTrustedNetworks.Remove(entity);
        await db.SaveChangesAsync(ct);
        await RecordAsync("deleted", entity, tenantId, userId, null, ct);
    }

    /// <summary>Cambia el máximo de entradas (null = sin tope). Las que ya hay se conservan aunque queden por encima: solo se bloquean las altas nuevas.</summary>
    public async Task<TrustedNetworksDto> SetMaxEntriesAsync(int? maxEntries, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var userId = ((TenantContext)tenant).RequireUserId();
        if (maxEntries is < 0) throw new ValidationException("maxEntries", LimitNegativeMessage);
        var t = await db.Tenants.IgnoreQueryFilters().FirstAsync(x => x.TenantId == tenantId, ct);
        var before = t.TrustedNetworkMaxEntries;
        if (before != maxEntries)
        {
            t.TrustedNetworkMaxEntries = maxEntries;
            await db.SaveChangesAsync(ct);
            await security.WriteAsync(SecurityEventTypes.TrustedNetwork, SecurityOutcomes.Success, userId, tenantId, new { action = "limit_changed", before, after = maxEntries }, ct);
            await NotifyAdminsAsync(tenantId, userId, "limit_changed", null, null, $"{(before?.ToString() ?? "—")} → {(maxEntries?.ToString() ?? "—")}", ct);
        }
        return await GetAsync(ct);
    }

    // ================================================================ interno

    private async Task<TrustedNetworkDto> GetOneAsync(Guid publicId, CancellationToken ct)
    {
        var all = await GetAsync(ct);
        return all.Items.First(i => i.PublicId == publicId);
    }

    private async Task<int?> MaxEntriesAsync(int tenantId, CancellationToken ct)
        => await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.TrustedNetworkMaxEntries).FirstOrDefaultAsync(ct);

    private static string? ValidateName(string? text, bool required, Dictionary<string, string[]> errors)
    {
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s)) { if (required || text is not null) errors["name"] = new[] { TrustedNetworkRules.NameRequired }; return null; }
        if (s.Length > TrustedNetworkRules.NameMax) { errors["name"] = new[] { TrustedNetworkRules.NameTooLong }; return null; }
        return s;
    }

    private static TrustedCidr? ValidateCidr(string? text, Dictionary<string, string[]> errors)
    {
        var parsed = TrustedNetworkRules.Parse(text);
        if (parsed.Error is not null) { errors["cidr"] = new[] { parsed.Error }; return null; }
        return parsed.Cidr;
    }

    private static string? ValidateNote(string? text, Dictionary<string, string[]> errors)
    {
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Length > TrustedNetworkRules.NoteMax) { errors["note"] = new[] { TrustedNetworkRules.NoteTooLong }; return null; }
        return s;
    }

    private static void ValidateExpiry(DateTime? expires, Dictionary<string, string[]> errors)
    {
        if (expires is DateTime e && e <= DateTime.UtcNow) errors["expiresAtUtc"] = new[] { TrustedNetworkRules.ExpiryInPast };
    }

    /// <summary>Bitácora de seguridad (TRUSTED_NETWORK) y aviso por correo a los administradores de la compañía.</summary>
    private async Task RecordAsync(string action, TenantTrustedNetwork entity, int tenantId, int userId, object? extra, CancellationToken ct)
    {
        await security.WriteAsync(SecurityEventTypes.TrustedNetwork, SecurityOutcomes.Success, userId, tenantId,
            new { action, name = entity.Name, cidr = entity.Cidr, extra }, ct);
        await NotifyAdminsAsync(tenantId, userId, action, entity.Name, entity.Cidr, null, ct);
    }

    /// <summary>
    /// Correo a quienes administran la compañía (permiso admin.tenant por un rol o como permiso extra, con correo verificado), incluido quien hizo el
    /// cambio: qué se hizo, con qué IP/rango, quién, desde qué IP y cuándo. Nunca hace fallar el cambio: si el correo no sale, se anota y se sigue.
    /// </summary>
    private async Task NotifyAdminsAsync(int tenantId, int actorId, string action, string? name, string? cidr, string? detail, CancellationToken ct)
    {
        if (email is null || !email.IsConfigured) return;
        try
        {
            var viaRoles = db.AppUserRoles.AsNoTracking().IgnoreQueryFilters()
                .Where(ur => ur.TenantId == tenantId && ur.Role!.IsActive && ur.Role.Permissions.Any(rp => rp.Permission!.Code == PermissionCatalog.AdminTenant))
                .Select(ur => ur.UserId);
            var viaExtra = db.UserPermissions.AsNoTracking().IgnoreQueryFilters()
                .Where(up => up.TenantId == tenantId && up.Permission!.Code == PermissionCatalog.AdminTenant).Select(up => up.UserId);
            var ids = await viaRoles.Union(viaExtra).ToListAsync(ct);
            if (ids.Count == 0) return;
            var recipients = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive && u.Email != null && u.EmailVerifiedUtc != null)
                .Select(u => new { u.Email, u.FullName }).ToListAsync(ct);
            if (recipients.Count == 0) return;
            var actor = await db.Users.AsNoTracking().Where(u => u.Id == actorId).Select(u => u.FullName ?? u.Email ?? u.UserName ?? "").FirstOrDefaultAsync(ct) ?? "";
            var company = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => new { t.Name, t.DefaultLangCode }).FirstAsync(ct);
            var en = string.Equals(company.DefaultLangCode, "en", StringComparison.OrdinalIgnoreCase);
            var (subject, html) = BuildEmail(en, company.Name, action, name, cidr, detail, actor, tenant.IpAddress, DateTime.UtcNow);
            foreach (var r in recipients)
            {
                try { await email.SendAsync(r.Email!, r.FullName, subject, html, ct, company.Name); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger?.LogWarning(ex, "No se pudo avisar por correo el cambio de la lista de IPs preferidas a {Email}", r.Email); }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "No se pudo armar el aviso por correo del cambio de la lista de IPs preferidas");
        }
    }

    /// <summary>Asunto y cuerpo del aviso (es/en según el idioma de la compañía). Público para probarlo sin correo.</summary>
    public static (string Subject, string Html) BuildEmail(bool english, string company, string action, string? name, string? cidr, string? detail, string actor, string? ip, DateTime utc)
    {
        static string H(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var what = (english, action) switch
        {
            (false, "created") => "Se agregó una IP preferida",
            (false, "updated") => "Se cambió una IP preferida",
            (false, "activated") => "Se activó una IP preferida",
            (false, "deactivated") => "Se desactivó una IP preferida",
            (false, "deleted") => "Se eliminó una IP preferida",
            (false, "limit_changed") => "Se cambió el máximo de entradas de IP preferida",
            (true, "created") => "A preferred IP was added",
            (true, "updated") => "A preferred IP was changed",
            (true, "activated") => "A preferred IP was activated",
            (true, "deactivated") => "A preferred IP was deactivated",
            (true, "deleted") => "A preferred IP was deleted",
            (true, "limit_changed") => "The preferred IP entry limit was changed",
            _ => action,
        };
        var subject = $"{what} — {company}";
        var when = utc.ToString("yyyy-MM-dd HH:mm 'UTC'");
        string row(string k, string? v) => string.IsNullOrWhiteSpace(v) ? "" : $"<tr><td style=\"padding:2px 12px 2px 0;color:#555\">{H(k)}</td><td><strong>{H(v)}</strong></td></tr>";
        var rows = english
            ? row("Network", name) + row("IP / range", cidr) + row("Change", detail) + row("Done by", actor) + row("From IP", ip) + row("When", when)
            : row("Red", name) + row("IP / rango", cidr) + row("Cambio", detail) + row("Lo hizo", actor) + row("Desde la IP", ip) + row("Cuándo", when);
        var intro = english
            ? "People connecting from a preferred IP skip the verification code at sign-in. If you do not recognize this change, review the list in Settings → Security and change the affected passwords."
            : "Quien se conecte desde una IP preferida no recibe el pedido del código de verificación al iniciar sesión. Si no reconoce este cambio, revise la lista en Ajustes → Seguridad y cambie las contraseñas que correspondan.";
        var html = $"""
            <div style="font-family:Arial,Helvetica,sans-serif;max-width:520px">
              <h2 style="color:#0B2C66">{H(what)}</h2>
              <p>{H(company)}</p>
              <table style="border-collapse:collapse;font-size:14px">{rows}</table>
              <p style="font-size:13px;color:#555">{H(intro)}</p>
            </div>
            """;
        return (subject, html);
    }
}
