using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Security;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 8A (P1) — aparatos de confianza de la app de almacén (UserDevice; DriverDevice del Lote 4 no cambia).
/// - Alta por el administrador (devices.manage): código único por compañía (409 'Ya existe un aparato con ese código.'),
///   almacén por defecto y tema (LIGHT | DARK). Devuelve UNA sola vez el código de registro de 8 caracteres (vence en 24 h).
/// - Registro en el aparato (anónimo): con el código válido se genera el secreto del aparato (32 bytes aleatorios) que
///   también se muestra UNA sola vez; cierra las sesiones previas del aparato y fija su sello SessionsNotBeforeUtc. Código y secreto se guardan solo como hash SHA-256 (son aleatorios de alta entropía,
///   se buscan por igualdad, como los refresh tokens); el código es de un solo uso.
/// - Aparato + secreto autentican la lista de usuarios, el login con PIN (AuthService) y el heartbeat.
/// - Desactivar revoca todas las sesiones (refresh tokens) emitidas a ese aparato y corta en el acto sus access tokens
///   (OnTokenValidated revisa el claim `did` contra UserDevice.IsActive y SessionsNotBeforeUtc con caché de 60 s que aquí
///   se borra). Desactivar, reactivar y registrar fijan el sello SessionsNotBeforeUtc = ahora: todo access token con `did`
///   emitido antes (claim `iat`) se rechaza aunque el aparato vuelva a estar activo. Reactivar no revive sesiones: en su
///   misma transacción fija el sello y revoca las sesiones que quedaran vivas (TOKEN_REVOKED device_reactivated) y cada
///   usuario vuelve a entrar con su PIN.
/// - Todo cambio del aparato queda en AuditLog (USER_DEVICE); LastSeenUtc/LastUserId/AppVersion son técnicos, no se auditan
///   y viven en UserDeviceActivity (sin RowVersion) para que el heartbeat y el login no invaliden la rowVersion del PATCH.
/// </summary>
public sealed class DeviceService(
    TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, ModuleService modules, PermissionService permissions,
    ISecurityEventWriter security, IMemoryCache cache)
{
    public const string DuplicateCodeMessage = "Ya existe un aparato con ese código.";
    public const string InvalidEnrollCodeMessage = "El código de registro no es válido o venció.";
    /// <summary>2026-09-30: el teléfono ya tiene un registro en la compañía de este código ({0} = compañía, {1} = código del aparato).</summary>
    public const string AlreadyRegisteredMessage = "Este teléfono ya está registrado en {0} como {1}. Pide al administrador un código de otra compañía.";
    public const string InvalidDeviceMessage = "El aparato no está registrado o fue desactivado.";
    public const string CodeRequiredMessage = "El código del aparato es obligatorio.";
    public const string CodeTooLongMessage = "El código del aparato admite hasta 30 caracteres.";
    public const string InvalidThemeMessage = "El tema no es válido; use LIGHT o DARK.";
    public const string InactiveWarehouseMessage = "El almacén por defecto está dado de baja.";
    public const string InactiveDeviceMessage = "El aparato está desactivado; reactívelo antes de generar un código de registro.";
    /// <summary>Nombre que muestra device/users para un usuario sin nombre (nunca el correo: el aparato es compartido).</summary>
    public const string UnnamedUser = "Usuario";

    public const int CodeMaxLength = 30;
    public const int NameMaxLength = 100;
    public const int ModelMaxLength = 80;
    public const int AppVersionMaxLength = 20;
    public const int EnrollCodeLength = 8;
    public static readonly TimeSpan EnrollCodeLifetime = TimeSpan.FromHours(24);
    public const int DefaultDeviceSessionDays = 30;
    public const string DefaultTheme = UiThemes.Light;
    public const string DefaultPlatform = "ANDROID";

    /// <summary>Alfabeto del código de registro: sin 0/O, 1/I/L para teclearlo sin confusiones en el aparato.</summary>
    private const string EnrollAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    // ---------------------------------------------------------------- administración (con JWT, devices.manage)

    /// <summary>Aparatos de la compañía por código; por defecto solo los activos.</summary>
    public async Task<IReadOnlyList<DeviceDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Set<UserDevice>().AsNoTracking();
        if (!includeInactive) q = q.Where(d => d.IsActive);
        var rows = await q.OrderBy(d => d.Code).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<DeviceDto> GetAsync(Guid publicId, CancellationToken ct)
        => (await ToDtosAsync(new[] { await ResolveAsync(publicId, track: false, ct) }, ct))[0];

    /// <summary>Alta: valida, fija plataforma ANDROID y tema (LIGHT por defecto) y genera el código de registro (24 h).</summary>
    public async Task<DeviceCreatedDto> CreateAsync(DeviceCreateRequest req, CancellationToken ct)
    {
        var ctx = (TenantContext)tenant;
        var tenantId = ctx.RequireTenantId();
        var errors = new Dictionary<string, string[]>();
        // Sin código: se genera uno (Lote F8a, P5 — la pantalla de alta no lo pide, solo Nombre y almacén). Con
        // código: se respeta tal cual (uso administrativo directo, como en los aparatos ya sembrados/probados).
        var code = req.Code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) code = null;
        else if (code.Length > CodeMaxLength) errors["code"] = new[] { CodeTooLongMessage };
        var name = Optional(req.Name, "name", "El nombre", NameMaxLength, errors);
        var model = Optional(req.Model, "model", "El modelo", ModelMaxLength, errors);
        var themeId = await ThemeIdAsync(string.IsNullOrWhiteSpace(req.Theme) ? DefaultTheme : req.Theme, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);
        var warehouseId = req.DefaultWarehousePublicId is Guid wid ? await ActiveWarehouseIdAsync(wid, ct) : (int?)null;

        if (code is not null)
        {
            // UNIQUE (TenantId, Code) sin filtro: el código de un aparato desactivado tampoco se reutiliza.
            if (await db.Set<UserDevice>().AnyAsync(d => d.Code == code, ct)) throw new ConflictException(DuplicateCodeMessage);
        }
        else
        {
            code = await NewUniqueDeviceCodeAsync(ct);
        }

        var enrollCode = NewEnrollCode();
        var device = new UserDevice
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = name, Model = model,
            PlatformLookupId = await lookups.GetIdAsync(LookupDomains.DevicePlatform, DefaultPlatform, ct),
            EnrollCodeHash = Hash(enrollCode), EnrollCodeExpiresUtc = DateTime.UtcNow.Add(EnrollCodeLifetime),
            DefaultWarehouseId = warehouseId, ThemeLookupId = themeId, RegisteredBy = ctx.UserId, RegisteredAtUtc = DateTime.UtcNow, IsActive = true,
        };
        db.Set<UserDevice>().Add(device);
        await db.SaveGuardedAsync(DuplicateCodeMessage, ct);
        return new DeviceCreatedDto(await GetAsync(device.PublicId, ct), enrollCode);
    }

    /// <summary>Código legible generado por el servidor (p. ej. "AP-7K4QXR") cuando el alta no manda uno; unos pocos
    /// reintentos alcanzan de sobra contra el UNIQUE (alta entropía, uso normal — no una carrera esperada).</summary>
    private async Task<string> NewUniqueDeviceCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"AP-{new string(Enumerable.Range(0, 6).Select(_ => EnrollAlphabet[RandomNumberGenerator.GetInt32(EnrollAlphabet.Length)]).ToArray())}";
            if (!await db.Set<UserDevice>().AnyAsync(d => d.Code == candidate, ct)) return candidate;
        }
        throw new ConflictException(DuplicateCodeMessage);
    }

    /// <summary>Edición: nombre ("" lo quita), almacén por defecto (o quitarlo) y tema. El código no cambia.</summary>
    public async Task<DeviceDto> UpdateAsync(Guid publicId, DevicePatchRequest req, CancellationToken ct)
    {
        var device = await ResolveAsync(publicId, track: true, ct);
        db.ApplyRowVersion(device, req.RowVersion);
        var errors = new Dictionary<string, string[]>();
        if (req.Name is not null) device.Name = Optional(req.Name, "name", "El nombre", NameMaxLength, errors);
        if (req.Theme is not null) device.ThemeLookupId = await ThemeIdAsync(req.Theme, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);
        if (req.ClearDefaultWarehouse) device.DefaultWarehouseId = null;
        else if (req.DefaultWarehousePublicId is Guid wid) device.DefaultWarehouseId = await ActiveWarehouseIdAsync(wid, ct);
        await db.SaveGuardedAsync(DuplicateCodeMessage, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Desactiva el aparato, invalida su código de registro pendiente y revoca todas sus sesiones.</summary>
    public async Task<DeviceDto> DeactivateAsync(Guid publicId, CancellationToken ct)
    {
        // Baja y revocación de sesiones en una sola transacción: no queda un aparato desactivado con sesiones vivas.
        var (device, revoked) = await db.RunInTransactionAsync(async ct2 =>
        {
            var d = await ResolveAsync(publicId, track: true, ct2);
            if (!d.IsActive) return (d, (int?)null);
            d.IsActive = false;
            d.SessionsNotBeforeUtc = DateTime.UtcNow;
            d.EnrollCodeHash = null;
            d.EnrollCodeExpiresUtc = null;
            await db.SaveGuardedAsync(DuplicateCodeMessage, ct2);
            return (d, (int?)await RevokeDeviceSessionsAsync(d, ct2));
        }, ct);
        if (revoked is int count)
        {
            cache.Remove(DeviceClaims.ActiveCacheKey(device.PublicId));
            await WriteSessionsRevokedAsync(device, "device_deactivated", count, ct);
        }
        return await GetAsync(publicId, ct);
    }

    /// <summary>
    /// Reactiva el aparato (conserva su secreto). En la misma transacción fija el sello SessionsNotBeforeUtc = ahora (los
    /// access tokens con `did` emitidos antes, incluidos los de antes de la baja, siguen rechazados) y revoca toda sesión
    /// (refresh token) del aparato que siguiera viva (p. ej. la emitida por un refresh que corría durante la baja): nada
    /// anterior a la reactivación revive; cada usuario vuelve a entrar con su PIN. Deja TOKEN_REVOKED con motivo
    /// device_reactivated (aunque no hubiera ninguna sesión).
    /// </summary>
    public async Task<DeviceDto> ReactivateAsync(Guid publicId, CancellationToken ct)
    {
        var (device, revoked) = await db.RunInTransactionAsync(async ct2 =>
        {
            var d = await ResolveAsync(publicId, track: true, ct2);
            if (d.IsActive) return (d, (int?)null);
            d.IsActive = true;
            d.SessionsNotBeforeUtc = DateTime.UtcNow;
            await db.SaveGuardedAsync(DuplicateCodeMessage, ct2);
            return (d, (int?)await RevokeDeviceSessionsAsync(d, ct2));
        }, ct);
        if (revoked is int count)
        {
            cache.Remove(DeviceClaims.ActiveCacheKey(device.PublicId));
            await WriteSessionsRevokedAsync(device, "device_reactivated", count, ct, always: true);
        }
        return await GetAsync(publicId, ct);
    }

    /// <summary>Genera un código de registro nuevo (el anterior deja de servir); se muestra una sola vez y vence en 24 h.</summary>
    public async Task<DeviceCreatedDto> RegenerateEnrollCodeAsync(Guid publicId, CancellationToken ct)
    {
        var device = await ResolveAsync(publicId, track: true, ct);
        if (!device.IsActive) throw new StatusRuleException(InactiveDeviceMessage);
        var enrollCode = NewEnrollCode();
        device.EnrollCodeHash = Hash(enrollCode);
        device.EnrollCodeExpiresUtc = DateTime.UtcNow.Add(EnrollCodeLifetime);
        await db.SaveGuardedAsync(DuplicateCodeMessage, ct);
        return new DeviceCreatedDto(await GetAsync(publicId, ct), enrollCode);
    }

    // ---------------------------------------------------------------- anónimos (sin JWT)

    /// <summary>
    /// Registro del aparato con el código de un solo uso: genera el secreto (una sola vez), invalida el código y cierra
    /// sesiones previas del aparato (reinstalación; sello SessionsNotBeforeUtc = ahora: sus access tokens anteriores dejan
    /// de servir). Código inválido, vencido, de un aparato desactivado o de una compañía sin el módulo WMS → 401
    /// 'El código de registro no es válido o venció.' (sin oráculo).
    /// </summary>
    public async Task<DeviceEnrolledDto> EnrollAsync(DeviceEnrollRequest req, CancellationToken ct)
    {
        var normalized = NormalizeEnrollCode(req.EnrollCode);
        UserDevice? device = null;
        if (normalized is not null)
        {
            var hash = Hash(normalized);
            var now = DateTime.UtcNow;
            device = await db.Set<UserDevice>().IgnoreQueryFilters().AsTracking()
                .FirstOrDefaultAsync(d => d.EnrollCodeHash == hash && d.EnrollCodeExpiresUtc > now && d.IsActive, ct);
        }
        if (device is null || !await TenantUsableAsync(device.TenantId, ct))
        {
            await WriteAnonymousFailureAsync(SecurityEventTypes.ApiCredential, device?.TenantId, new { action = "device_enroll" }, ct);
            throw new UnauthorizedException(InvalidEnrollCodeMessage);
        }

        // Un teléfono puede estar registrado en varias compañías, pero una sola vez en cada una: si ya tiene un registro
        // vigente en la compañía de este código, se avisa sin consumir el código.
        if (req.RegisteredDevicePublicIds is { Count: > 0 } registered)
        {
            var enrollTenantId = device.TenantId;
            var existing = await db.Set<UserDevice>().IgnoreQueryFilters().AsNoTracking()
                .Where(d => registered.Contains(d.PublicId) && d.TenantId == enrollTenantId && d.IsActive && d.SecretHash != null)
                .Select(d => d.Code).FirstOrDefaultAsync(ct);
            if (existing is not null)
            {
                var companyName = await db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => t.TenantId == enrollTenantId).Select(t => t.Name).FirstAsync(ct);
                throw new ConflictException(string.Format(AlreadyRegisteredMessage, companyName, existing));
            }
        }

        using var _ = ((TenantContext)tenant).AsAnonymous(device.TenantId);
        var secret = NewSecret();
        var deviceId = device.UserDeviceId;
        var deviceTenantId = device.TenantId;
        var codeHash = Hash(normalized!);
        // Secreto nuevo, código consumido, datos técnicos y cierre de sesiones previas en una sola transacción.
        // RunInTransactionAsync limpia el rastreador: el aparato se vuelve a leer dentro y se exige el mismo código aún
        // vigente. Dos enroll simultáneos con el mismo código: el segundo, o ya no encuentra el código al releer, o choca al
        // guardar (RowVersion de UserDevice o PK de UserDeviceActivity → ConflictException); en ambos casos recibe el mismo
        // 401 que un código inválido (sin oráculo), nunca un 409.
        int revoked;
        try
        {
            (device, revoked) = await db.RunInTransactionAsync(async ct2 =>
            {
                var now = DateTime.UtcNow;
                var d = await db.Set<UserDevice>().IgnoreQueryFilters().AsTracking().FirstOrDefaultAsync(x => x.UserDeviceId == deviceId, ct2);
                if (d is null || d.EnrollCodeHash != codeHash || !(d.EnrollCodeExpiresUtc > now) || !d.IsActive)
                    throw new UnauthorizedException(InvalidEnrollCodeMessage);
                d.SecretHash = Hash(secret);
                d.EnrollCodeHash = null;
                d.EnrollCodeExpiresUtc = null;
                d.EnrolledAtUtc = now;
                d.SessionsNotBeforeUtc = now;   // reinstalación: los access tokens anteriores del aparato dejan de servir
                if (Clip(req.Model, ModelMaxLength) is string model) d.Model = model;
                // Datos técnicos en UserDeviceActivity (misma transacción): último contacto y versión de la app.
                var activity = await db.Set<UserDeviceActivity>().IgnoreQueryFilters().AsTracking()
                    .FirstOrDefaultAsync(a => a.UserDeviceId == d.UserDeviceId, ct2);
                if (activity is null)
                    db.Set<UserDeviceActivity>().Add(activity = new UserDeviceActivity { UserDeviceId = d.UserDeviceId, TenantId = d.TenantId });
                activity.LastSeenUtc = now;
                if (Clip(req.AppVersion, AppVersionMaxLength) is string version) activity.AppVersion = version;
                await db.SaveGuardedAsync(DuplicateCodeMessage, ct2);
                return (d, await RevokeDeviceSessionsAsync(d, ct2));
            }, ct);
        }
        catch (Exception ex) when (ex is UnauthorizedException or ConflictException)
        {
            // Código consumido por otro enroll (relectura) o choque con el enroll que ganó la carrera (guardado):
            // mismo 401 y mismo SecurityEvent que el camino de código inválido, fuera de la transacción ya revertida.
            db.ChangeTracker.Clear();
            await WriteAnonymousFailureAsync(SecurityEventTypes.ApiCredential, deviceTenantId, new { action = "device_enroll" }, ct);
            throw new UnauthorizedException(InvalidEnrollCodeMessage);
        }
        cache.Remove(DeviceClaims.ActiveCacheKey(device.PublicId));
        await WriteSessionsRevokedAsync(device, "device_enrolled", revoked, ct);
        await security.WriteAsync(SecurityEventTypes.ApiCredential, SecurityOutcomes.Success, null, device.TenantId, new { action = "device_enrolled", device = device.Code }, ct);

        var tenantName = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == device.TenantId).Select(t => t.Name).FirstAsync(ct);
        var (warehousePublicId, theme, receivingMode) = await PreferencesAsync(device, ct);
        return new DeviceEnrolledDto(device.PublicId, secret, tenantName, warehousePublicId, theme, receivingMode);
    }

    /// <summary>
    /// Usuarios que pueden entrar en el aparato: internos, activos, con membresía ACTIVE en la compañía del aparato, con PIN
    /// definido y con inventory.view (nunca el admin de plataforma); ordenados por nombre. Aparato inválido o desactivado → 401.
    /// </summary>
    public async Task<IReadOnlyList<DeviceUserDto>> GetDeviceUsersAsync(DeviceUsersRequest req, CancellationToken ct)
    {
        var device = await AuthenticateAsync(req.DevicePublicId, req.DeviceSecret, "users", ct, track: false);
        using var _ = ((TenantContext)tenant).AsAnonymous(device.TenantId);
        var activeMembership = await ActiveMembershipIdAsync(ct);
        var portalKind = await lookups.TryGetIdAsync(LookupDomains.UserKind, UserKinds.Portal, ct);
        var candidates = await (from m in db.UserTenants.AsNoTracking()
                                join p in db.Set<UserPin>().AsNoTracking() on m.UserId equals p.UserId
                                where m.TenantId == device.TenantId && p.TenantId == device.TenantId && m.StatusCodeId == activeMembership
                                      && m.User!.IsActive && !m.User.IsPlatformAdmin && (m.User.UserKindLookupId == null || m.User.UserKindLookupId != portalKind)
                                select new { m.UserId, m.User!.FullName, m.User.Email })
            .ToListAsync(ct);
        var list = new List<DeviceUserDto>(candidates.Count);
        foreach (var c in candidates)
        {
            if (!(await permissions.GetEffectivePermissionsAsync(c.UserId, device.TenantId, ct)).Contains(PermissionCatalog.InventoryView)) continue;
            // Sin nombre: 'Usuario' (el aparato es compartido; el correo no se muestra).
            var name = string.IsNullOrWhiteSpace(c.FullName) ? UnnamedUser : c.FullName.Trim();
            list.Add(new DeviceUserDto(c.UserId, name, Initials(c.FullName, c.Email)));
        }
        return list.OrderBy(u => u.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Heartbeat: aparato + secreto → LastSeenUtc y AppVersion; devuelve estado, almacén por defecto, tema y hora del
    /// servidor. Un aparato desactivado (o de una compañía inactiva o sin el módulo) recibe IsActive = false, no un error,
    /// para que la app bloquee la entrada. Secreto inválido → 401.
    /// </summary>
    public async Task<DeviceHeartbeatDto> HeartbeatAsync(HeartbeatRequest req, CancellationToken ct)
    {
        var device = await FindBySecretAsync(req.DevicePublicId, req.DeviceSecret, track: false, ct) ?? throw new UnauthorizedException(InvalidDeviceMessage);
        using var _ = ((TenantContext)tenant).AsAnonymous(device.TenantId);
        var usable = device.IsActive && await TenantUsableAsync(device.TenantId, ct);
        await TouchAsync(device, null, req.AppVersion, ct);
        var (warehousePublicId, theme, receivingMode) = await PreferencesAsync(device, ct);
        return new DeviceHeartbeatDto(usable, warehousePublicId, theme, DateTime.UtcNow, receivingMode);
    }

    // ---------------------------------------------------------------- costuras para AuthService

    /// <summary>
    /// Aparato activo por PublicId + secreto, de una compañía activa con el módulo WMS encendido; si no, 401
    /// 'El aparato no está registrado o fue desactivado.'. Devuelve la entidad rastreada (sin rastrear con track: false,
    /// para quien solo lee).
    /// </summary>
    internal async Task<UserDevice> AuthenticateAsync(Guid publicId, string? secret, string action, CancellationToken ct, bool track = true)
    {
        var device = await FindBySecretAsync(publicId, secret, track, ct);
        if (device is null || !device.IsActive || !await TenantUsableAsync(device.TenantId, ct))
        {
            // El intento rechazado queda como LOGIN/FAILURE con stage=device (p. ej. el aparato perdido que el
            // administrador desactivó); con aparato conocido, en la bitácora de su compañía.
            await WriteAnonymousFailureAsync(SecurityEventTypes.Login, device?.TenantId,
                new { stage = "device", action, device = device?.Code, devicePublicId = publicId,
                      reason = device is null ? "device_invalid" : !device.IsActive ? "device_inactive" : "tenant_unusable" }, ct);
            throw new UnauthorizedException(InvalidDeviceMessage);
        }
        return device;
    }

    /// <summary>Aparato por Id sin filtro de tenant (refresh de una sesión de aparato); null si no existe.</summary>
    internal Task<UserDevice?> FindByIdAsync(int userDeviceId, CancellationToken ct)
        => db.Set<UserDevice>().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(d => d.UserDeviceId == userDeviceId, ct);

    /// <summary>Días de sesión de aparato de la compañía (Tenant.DeviceSessionDays; 30 si no es positivo).</summary>
    internal async Task<int> SessionDaysAsync(int tenantId, CancellationToken ct)
    {
        var days = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId)
            .Select(t => t.DeviceSessionDays).FirstOrDefaultAsync(ct);
        return days > 0 ? days : DefaultDeviceSessionDays;
    }

    /// <summary>
    /// Datos técnicos del aparato (LastSeenUtc, LastUserId, AppVersion) sin AuditLog, en UserDeviceActivity (tabla 1:1 sin
    /// RowVersion): así el heartbeat y el login con PIN NO cambian el ROWVERSION de UserDevice y la rowVersion que leyó la
    /// pantalla de administración sigue sirviendo para el PATCH (solo una edición real da 409). Se escribe con un UPDATE
    /// directo (ExecuteUpdate), sin el rastreador, para que un heartbeat y un login simultáneos del mismo aparato no choquen.
    /// La fila la crea el enroll; si faltara, se inserta (y ante la carrera de dos inserciones se vuelve a actualizar).
    /// </summary>
    internal async Task TouchAsync(UserDevice device, int? userId, string? appVersion, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var version = Clip(appVersion, AppVersionMaxLength);
        Task<int> UpdateRowAsync() => db.Set<UserDeviceActivity>().IgnoreQueryFilters()
            .Where(a => a.UserDeviceId == device.UserDeviceId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.LastSeenUtc, now)
                .SetProperty(a => a.LastUserId, a => userId ?? a.LastUserId)
                .SetProperty(a => a.AppVersion, a => version ?? a.AppVersion), ct);
        if (await UpdateRowAsync() > 0) return;

        var row = new UserDeviceActivity { UserDeviceId = device.UserDeviceId, TenantId = device.TenantId, LastSeenUtc = now, LastUserId = userId, AppVersion = version };
        db.Set<UserDeviceActivity>().Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DbExtensions.IsUniqueViolation(ex))
        {
            db.Entry(row).State = EntityState.Detached;
            await UpdateRowAsync();
        }
        finally
        {
            if (db.Entry(row).State != EntityState.Detached) db.Entry(row).State = EntityState.Detached;
        }
    }

    // ---------------------------------------------------------------- reglas puras (probables en pruebas)

    /// <summary>Normaliza el código tecleado: sin espacios ni guiones, en mayúsculas; null si no tiene la forma esperada.</summary>
    public static string? NormalizeEnrollCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = new string(raw.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return s.Length == EnrollCodeLength && s.All(c => EnrollAlphabet.Contains(c)) ? s : null;
    }

    /// <summary>Iniciales para el botón del usuario en el aparato: primera letra de las dos primeras palabras del nombre.</summary>
    public static string Initials(string? fullName, string? email)
    {
        var words = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var letters = words.Take(2).Select(w => char.ToUpperInvariant(w[0])).ToArray();
        if (letters.Length > 0) return new string(letters);
        return string.IsNullOrEmpty(email) ? "?" : char.ToUpperInvariant(email[0]).ToString();
    }

    /// <summary>SHA-256 en hex minúsculas (mismo formato que los refresh tokens).</summary>
    public static string Hash(string value) => JwtTokenService.HashToken(value);

    // ---------------------------------------------------------------- internos

    private async Task<UserDevice> ResolveAsync(Guid publicId, bool track, CancellationToken ct)
    {
        var q = track ? db.Set<UserDevice>().AsTracking() : db.Set<UserDevice>().AsNoTracking();
        return await q.FirstOrDefaultAsync(d => d.PublicId == publicId, ct) ?? throw new NotFoundException("Aparato");
    }

    private async Task<UserDevice?> FindBySecretAsync(Guid publicId, string? secret, bool track, CancellationToken ct)
    {
        if (publicId == Guid.Empty || string.IsNullOrWhiteSpace(secret)) return null;
        var q = track ? db.Set<UserDevice>().IgnoreQueryFilters().AsTracking() : db.Set<UserDevice>().IgnoreQueryFilters().AsNoTracking();
        var device = await q.FirstOrDefaultAsync(d => d.PublicId == publicId, ct);
        if (device?.SecretHash is null) return null;
        var expected = Encoding.ASCII.GetBytes(device.SecretHash);
        var actual = Encoding.ASCII.GetBytes(Hash(secret.Trim()));
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? device : null;
    }

    /// <summary>Compañía activa y con el módulo WMS_LOTSERIAL encendido (también lo usa el refresh de sesiones de aparato).</summary>
    internal async Task<bool> TenantUsableAsync(int tenantId, CancellationToken ct)
    {
        var active = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.IsActive).FirstOrDefaultAsync(ct);
        if (!active) return false;
        using var _ = ((TenantContext)tenant).As(tenantId);
        return await modules.IsEnabledAsync(ModuleKeys.WmsLotSerial, ct);
    }

    /// <summary>
    /// Evento de fallo de un flujo anónimo del aparato: sin el usuario ni la compañía de un bearer ajeno que venga en la
    /// petición (el writer usaría el contexto como respaldo). Con aparato conocido, en la compañía del aparato.
    /// </summary>
    private async Task WriteAnonymousFailureAsync(string eventType, int? tenantId, object detail, CancellationToken ct)
    {
        var ctx = (TenantContext)tenant;
        var (t, u) = (ctx.TenantId, ctx.UserId);
        ctx.TenantId = null;
        ctx.UserId = null;
        try { await security.WriteAsync(eventType, SecurityOutcomes.Failure, null, tenantId, detail, ct); }
        finally { ctx.TenantId = t; ctx.UserId = u; }
    }

    /// <summary>Revoca las sesiones vivas del aparato (dentro de la transacción del llamador); devuelve cuántas.</summary>
    private async Task<int> RevokeDeviceSessionsAsync(UserDevice device, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.TenantId == device.TenantId && t.RevokedAtUtc == null && t.UserDeviceId == device.UserDeviceId)
            .ToListAsync(ct);
        if (tokens.Count == 0) return 0;
        var now = DateTime.UtcNow;
        foreach (var t in tokens) t.RevokedAtUtc = now;
        db.SuppressAudit = true;
        try { await db.SaveChangesAsync(ct); }
        finally { db.SuppressAudit = false; }
        return tokens.Count;
    }

    /// <summary>
    /// TOKEN_REVOKED de las sesiones del aparato, después de confirmar la transacción (nada si no había ninguna, salvo con
    /// always: la reactivación deja constancia de que cerró las sesiones aunque el conteo sea 0).
    /// </summary>
    private async Task WriteSessionsRevokedAsync(UserDevice device, string reason, int count, CancellationToken ct, bool always = false)
    {
        if (count == 0 && !always) return;
        await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Success, tenant.UserId, device.TenantId,
            new { scope = "device", device = device.Code, reason, count }, ct);
    }

    private async Task<int> ActiveMembershipIdAsync(CancellationToken ct)
        => await db.StatusCodes.AsNoTracking()
               .Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active)
               .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException($"Estatus {StatusDomains.MembershipStatus}", MembershipStatuses.Active);

    private async Task<int?> ThemeIdAsync(string theme, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        var code = theme.Trim().ToUpperInvariant();
        var id = await lookups.TryGetIdAsync(LookupDomains.UiTheme, code, ct);
        if (id is null) errors["theme"] = new[] { InvalidThemeMessage };
        return id;
    }

    private async Task<int> ActiveWarehouseIdAsync(Guid publicId, CancellationToken ct)
    {
        var w = await db.ResolveWarehouseAsync(publicId, track: false, ct);
        if (!w.IsActive) throw new StatusRuleException(InactiveWarehouseMessage);
        return w.WarehouseId;
    }

    /// <summary>
    /// Almacén por defecto, tema y (Lote 16) modo de recepción del almacén por defecto: PUTAWAY | DIRECT (NULL en la base =
    /// PUTAWAY); null si el aparato no tiene almacén por defecto.
    /// </summary>
    private async Task<(Guid? WarehousePublicId, string? Theme, string? ReceivingMode)> PreferencesAsync(UserDevice device, CancellationToken ct)
    {
        var warehouse = device.DefaultWarehouseId is int wid
            ? await db.Warehouses.AsNoTracking().IgnoreQueryFilters().Where(w => w.WarehouseId == wid && w.TenantId == device.TenantId)
                .Select(w => new { w.PublicId, w.ReceivingModeLookupId }).FirstOrDefaultAsync(ct)
            : null;
        var theme = device.ThemeLookupId is int tid ? (await lookups.GetAsync(tid, ct))?.InternalCode : null;
        string? mode = null;
        if (warehouse is not null)
            mode = ReceivingModeRules.Normalize(warehouse.ReceivingModeLookupId is int mid ? (await lookups.GetAsync(mid, ct))?.InternalCode : null);
        return (warehouse?.PublicId, theme ?? DefaultTheme, mode);
    }

    private async Task<IReadOnlyList<DeviceDto>> ToDtosAsync(IReadOnlyCollection<UserDevice> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<DeviceDto>();
        var warehouseIds = rows.Where(d => d.DefaultWarehouseId.HasValue).Select(d => d.DefaultWarehouseId!.Value).Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);
        var deviceIds = rows.Select(d => d.UserDeviceId).ToList();
        var activity = await db.Set<UserDeviceActivity>().AsNoTracking().Where(a => deviceIds.Contains(a.UserDeviceId))
            .ToDictionaryAsync(a => a.UserDeviceId, ct);
        var userIds = activity.Values.Where(a => a.LastUserId.HasValue).Select(a => a.LastUserId!.Value).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FullName ?? u.Email }).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var list = new List<DeviceDto>(rows.Count);
        foreach (var d in rows)
        {
            var platform = await lookups.GetAsync(d.PlatformLookupId, ct);
            var theme = d.ThemeLookupId is int tid ? await lookups.GetAsync(tid, ct) : null;
            var w = d.DefaultWarehouseId is int wid && warehouses.TryGetValue(wid, out var wr) ? wr : null;
            var a = activity.GetValueOrDefault(d.UserDeviceId);
            list.Add(new DeviceDto(
                d.PublicId, d.Code, d.Name, d.Model, platform?.InternalCode, a?.AppVersion, d.SecretHash is not null, d.EnrolledAtUtc,
                d.EnrollCodeHash is not null ? d.EnrollCodeExpiresUtc : null, a?.LastSeenUtc, a?.LastUserId,
                a?.LastUserId is int uid ? userNames.GetValueOrDefault(uid) : null, w?.PublicId, w?.Code, theme?.InternalCode,
                d.RegisteredAtUtc, d.IsActive, d.RowVersion is { Length: > 0 } rv ? Convert.ToBase64String(rv) : null));
        }
        return list;
    }

    private static string? Optional(string? value, string field, string label, int max, Dictionary<string, string[]> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) errors[field] = new[] { $"{label} admite hasta {max} caracteres." };
        return v;
    }

    private static string? Clip(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }

    private static string NewEnrollCode()
        => new(Enumerable.Range(0, EnrollCodeLength).Select(_ => EnrollAlphabet[RandomNumberGenerator.GetInt32(EnrollAlphabet.Length)]).ToArray());

    /// <summary>Secreto del aparato: 32 bytes aleatorios en base64url.</summary>
    private static string NewSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
