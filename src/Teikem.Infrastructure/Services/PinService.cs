using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services
{
    /// <summary>423 'PIN bloqueado por 15 minutos.' (5 intentos fallidos seguidos en el aparato).</summary>
    public sealed class PinLockedException() : TeikemException(PinRules.LockedMessage, 423, "pin_locked");

    /// <summary>
    /// Lote 8A (P1) — PIN por usuario para entrar en los aparatos de almacén (UserPin, uno por usuario y compañía).
    /// - El PIN se guarda solo como hash con el MISMO PasswordHasher de Identity; nunca sale en un DTO ni en la bitácora.
    /// - Mi cuenta: definirlo exige la contraseña actual (400 'La contraseña actual es incorrecta.'); quitarlo no. Cada
    ///   contraseña equivocada cuenta en el bloqueo por cuenta de Identity (el mismo del login: 5 fallos → 15 minutos) y,
    ///   con la cuenta bloqueada, responde el mismo 400 aunque la contraseña sea correcta.
    /// - Administración (devices.manage o admin.users, y AAL2 para asignarlo): asignar o quitar el PIN de un usuario interno
    ///   de la misma compañía que NO tenga más permisos que quien lo hace (403) ni sea admin de plataforma (404): un PIN
    ///   impuesto por otro no puede abrir una cuenta con más privilegios (el login por aparato no pide contraseña ni MFA).
    /// - Guardar un PIN reinicia el contador de intentos y el bloqueo; cambiarlo, restablecerlo o quitarlo cierra las
    ///   sesiones del usuario en aparatos (sus refresh tokens con UserDeviceId quedan revocados; SecurityEvent TOKEN_REVOKED).
    /// - Todo cambio escribe SecurityEvent PASSWORD_CHANGE con detalle target = 'pin'.
    /// - En el login por aparato: 5 fallos seguidos → bloqueo de 15 minutos (PinRules); 401 'PIN incorrecto.' / 423.
    /// </summary>
    public sealed class PinService(
        TeikemDbContext db, UserManager<ApplicationUser> users, ITenantContext tenant, PermissionService permissions, ISecurityEventWriter security)
    {
        public const string WrongPasswordMessage = "La contraseña actual es incorrecta.";
        public const string WrongPinMessage = "PIN incorrecto.";
        public const string DuplicatePinMessage = "El PIN del usuario cambió al mismo tiempo en otra sesión; intente de nuevo.";
        public const string HigherPrivilegesMessage = "No puede asignar ni quitar el PIN de un usuario con más permisos que usted.";
        /// <summary>
        /// 403 del login por aparato (y 401 del refresh de una sesión de aparato) cuando el PIN lo asignó otra persona que ya no
        /// cubre los permisos actuales del usuario (le subieron los permisos, o quien lo asignó perdió los suyos, se desactivó o
        /// dejó la compañía). El PIN deja de servir hasta que el propio usuario lo defina en Mi cuenta (o lo reasigne alguien
        /// con al menos sus permisos).
        /// </summary>
        public const string AssignerLowerPrivilegesMessage = "Su PIN lo asignó otra persona que ya no tiene sus permisos; defina su propio PIN en Mi cuenta.";

        // ---------------------------------------------------------------- Mi cuenta

        /// <summary>Estado del PIN del usuario en la compañía activa (sin el PIN, que no se puede leer).</summary>
        public async Task<PinStatusDto> GetMineAsync(CancellationToken ct)
        {
            var userId = ((TenantContext)tenant).RequireUserId();
            ((TenantContext)tenant).RequireTenantId();
            var pin = await db.Set<UserPin>().AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
            return ToStatus(pin);
        }

        /// <summary>Define o cambia el PIN propio: formato (PinRules) y contraseña actual correcta.</summary>
        public async Task<PinStatusDto> SetMineAsync(PinSetRequest req, CancellationToken ct)
        {
            var ctx = (TenantContext)tenant;
            var userId = ctx.RequireUserId();
            var tenantId = ctx.RequireTenantId();
            var pinValue = ValidatePin(req.Pin);
            var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
            // Mismo bloqueo por cuenta de Identity que el login (5 fallos → 15 minutos): una sesión de aparato abierta solo con
            // PIN no puede usar esta ruta para adivinar la contraseña sin límite. Bloqueada → el mismo 400 (sin oráculo).
            if (await users.IsLockedOutAsync(user))
            {
                await security.WriteAsync(SecurityEventTypes.Lockout, SecurityOutcomes.Blocked, userId, tenantId, new { target = "pin", reason = "password" }, ct);
                throw new ValidationException("currentPassword", WrongPasswordMessage);
            }
            if (!await users.CheckPasswordAsync(user, req.CurrentPassword ?? string.Empty))
            {
                await users.AccessFailedAsync(user);
                var locked = await users.IsLockedOutAsync(user);
                await security.WriteAsync(locked ? SecurityEventTypes.Lockout : SecurityEventTypes.PasswordChange, locked ? SecurityOutcomes.Blocked : SecurityOutcomes.Failure,
                    userId, tenantId, new { target = "pin", reason = "password" }, ct);
                throw new ValidationException("currentPassword", WrongPasswordMessage);
            }
            await users.ResetAccessFailedCountAsync(user);
            var pin = await UpsertAsync(user, tenantId, pinValue, userId, ct);
            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, userId, tenantId, new { target = "pin" }, ct);
            return ToStatus(pin);
        }

        /// <summary>Quita el PIN propio y cierra las sesiones del usuario en los aparatos de la compañía.</summary>
        public async Task RemoveMineAsync(CancellationToken ct)
        {
            var ctx = (TenantContext)tenant;
            await RemoveAsync(ctx.RequireUserId(), ctx.RequireTenantId(), by: null, ct);
        }

        // ---------------------------------------------------------------- administración

        /// <summary>Asigna o restablece el PIN de otro usuario interno de la compañía (devices.manage o admin.users).</summary>
        public async Task<PinStatusDto> SetForUserAsync(int userId, PinAdminSetRequest req, CancellationToken ct)
        {
            var tenantId = ((TenantContext)tenant).RequireTenantId();
            await EnsureAdminAsync(ct);
            var user = await ResolveTenantUserAsync(userId, tenantId, ct);
            await EnsureNotHigherAsync(user, tenantId, ct);
            var pinValue = ValidatePin(req.Pin);
            var pin = await UpsertAsync(user, tenantId, pinValue, tenant.UserId, ct);
            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, user.Id, tenantId, new { target = "pin", by = tenant.UserId }, ct);
            return ToStatus(pin);
        }

        /// <summary>Quita el PIN de otro usuario interno de la compañía y cierra sus sesiones en aparatos.</summary>
        public async Task RemoveForUserAsync(int userId, CancellationToken ct)
        {
            var tenantId = ((TenantContext)tenant).RequireTenantId();
            await EnsureAdminAsync(ct);
            var user = await ResolveTenantUserAsync(userId, tenantId, ct);
            await EnsureNotHigherAsync(user, tenantId, ct);
            await RemoveAsync(user.Id, tenantId, by: tenant.UserId, ct);
        }

        // ---------------------------------------------------------------- login por aparato

        /// <summary>
        /// Verifica el PIN en el login por aparato (el contexto ya corre como la compañía del aparato). Bloqueado → 423 y
        /// SecurityEvent LOCKOUT; incorrecto o sin PIN → 401 'PIN incorrecto.' y cuenta el intento (el 5.º bloquea: 423).
        /// Correcto → reinicia el contador. Los eventos LOGIN de éxito los escribe AuthService.
        /// <para>Concurrencia: el contador NO se lee y reescribe en memoria (N intentos en paralelo leerían el mismo valor y el
        /// bloqueo nunca llegaría). Cada resultado se asienta con un solo UPDATE atómico en SQL condicionado a que la fila NO
        /// esté bloqueada: un fallo hace FailedCount + 1 y, al llegar a 5, fija el bloqueo de 15 minutos con el contador en 0
        /// (como PinRules.RegisterFailure); un acierto pone el contador en 0 (PinRules.RegisterSuccess). Si el UPDATE no
        /// afecta ninguna fila, otro intento ya bloqueó → 423 también para el PIN correcto. Así, por ronda de bloqueo solo 5
        /// intentos fallidos pueden asentarse antes del bloqueo, vengan en serie o en paralelo. No se usa el rastreador ni una
        /// transacción (RunInTransactionAsync limpiaría el UserDevice rastreado que AuthService sigue usando).</para>
        /// </summary>
        internal async Task VerifyForLoginAsync(ApplicationUser user, int tenantId, string? pinValue, string deviceCode, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var pin = await db.Set<UserPin>().AsNoTracking().IgnoreQueryFilters()
                .Where(p => p.UserId == user.Id && p.TenantId == tenantId)
                .Select(p => new { p.UserPinId, p.PinHash, p.LockedUntilUtc })
                .FirstOrDefaultAsync(ct);
            if (pin is null)
            {
                // Sin PIN definido: el mismo 401 que un PIN incorrecto (no hay contador que mover).
                await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Failure, user.Id, tenantId, new { stage = "device", device = deviceCode }, ct);
                throw new UnauthorizedException(WrongPinMessage);
            }
            if (PinRules.IsLocked(pin.LockedUntilUtc, now)) await LockedAsync(user, tenantId, deviceCode, ct);

            // Se recorta igual que al definirlo (ValidatePin): ' 4826 ' es el mismo PIN que '4826'.
            pinValue = pinValue?.Trim();
            var result = string.IsNullOrEmpty(pinValue)
                ? PasswordVerificationResult.Failed
                : users.PasswordHasher.VerifyHashedPassword(user, pin.PinHash, pinValue);
            // Solo la fila de este PIN y solo si no está bloqueada a esta hora (el bloqueo pudo llegar en paralelo).
            var row = db.Set<UserPin>().IgnoreQueryFilters()
                .Where(p => p.UserPinId == pin.UserPinId && (p.LockedUntilUtc == null || p.LockedUntilUtc <= now));

            if (result == PasswordVerificationResult.Failed)
            {
                var max = PinRules.MaxFailedAttempts;
                var lockUntil = now.Add(PinRules.LockDuration);
                // En el UPDATE todas las expresiones leen el valor anterior de la fila.
                var counted = await row.ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.LockedUntilUtc, p => p.FailedCount + 1 >= max ? (DateTime?)lockUntil : null)
                    .SetProperty(p => p.FailedCount, p => p.FailedCount + 1 >= max ? 0 : p.FailedCount + 1), ct);
                // Sin fila afectada ya estaba bloqueado; si se contó, se relee por si este (u otro en paralelo) llegó al 5.º.
                var locked = counted == 0 || PinRules.IsLocked(await db.Set<UserPin>().AsNoTracking().IgnoreQueryFilters()
                    .Where(p => p.UserPinId == pin.UserPinId).Select(p => p.LockedUntilUtc).FirstOrDefaultAsync(ct), now);
                await security.WriteAsync(locked ? SecurityEventTypes.Lockout : SecurityEventTypes.Login, locked ? SecurityOutcomes.Blocked : SecurityOutcomes.Failure,
                    user.Id, tenantId, new { stage = "device", device = deviceCode, target = "pin" }, ct);
                if (locked) throw new PinLockedException();
                throw new UnauthorizedException(WrongPinMessage);
            }

            // Correcto: contador a cero y sin bloqueo; si el hasher pide re-hash (cambio de versión de Identity), en la misma sentencia.
            var (failed, cleared) = PinRules.RegisterSuccess();
            var rehash = result == PasswordVerificationResult.SuccessRehashNeeded ? users.PasswordHasher.HashPassword(user, pinValue!) : null;
            var reset = rehash is not null
                ? await row.ExecuteUpdateAsync(s => s.SetProperty(p => p.FailedCount, failed).SetProperty(p => p.LockedUntilUtc, cleared)
                    .SetProperty(p => p.PinHash, rehash), ct)
                : await row.ExecuteUpdateAsync(s => s.SetProperty(p => p.FailedCount, failed).SetProperty(p => p.LockedUntilUtc, cleared), ct);
            if (reset == 0) await LockedAsync(user, tenantId, deviceCode, ct);

            // Anti-escalada diferida: el límite de privilegios del PIN impuesto por otro se vuelve a comprobar en cada login
            // (no solo al asignarlo), porque los permisos del usuario pueden haber subido después. Solo tras un PIN correcto.
            if (!await AssignerStillCoversAsync(user.Id, tenantId, ct))
            {
                await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Blocked, user.Id, tenantId,
                    new { stage = "device", device = deviceCode, target = "pin", reason = "pin_assigner_lower_privileges" }, ct);
                throw new ForbiddenException(AssignerLowerPrivilegesMessage);
            }
        }

        /// <summary>
        /// ¿Sigue valiendo el PIN del usuario respecto de quien lo asignó (UserPin.UpdatedBy)? true si no hay PIN, si lo
        /// definió el propio usuario (UpdatedBy null o él mismo), o si quien lo asignó es admin de plataforma activo, o si está
        /// activo, con membresía ACTIVE en la compañía y los permisos efectivos ACTUALES del usuario caben en los suyos.
        /// Lo usan el login por aparato y el refresh de una sesión de aparato (AuthService).
        /// </summary>
        public async Task<bool> AssignerStillCoversAsync(int userId, int tenantId, CancellationToken ct)
        {
            var pin = await db.Set<UserPin>().AsNoTracking().IgnoreQueryFilters()
                .Where(p => p.UserId == userId && p.TenantId == tenantId)
                .Select(p => new { p.UpdatedBy })
                .FirstOrDefaultAsync(ct);
            if (pin?.UpdatedBy is not int assignerId || assignerId == userId) return true;
            var assigner = await db.Users.AsNoTracking().IgnoreQueryFilters()
                .Where(u => u.Id == assignerId).Select(u => new { u.IsActive, u.IsPlatformAdmin })
                .FirstOrDefaultAsync(ct);
            if (assigner is null || !assigner.IsActive) return false;
            if (assigner.IsPlatformAdmin) return true;
            var activeMembership = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active)
                .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
            var member = activeMembership is not null && await db.UserTenants.AsNoTracking().IgnoreQueryFilters()
                .AnyAsync(m => m.UserId == assignerId && m.TenantId == tenantId && m.StatusCodeId == activeMembership, ct);
            if (!member) return false;
            var target = await permissions.GetEffectivePermissionsAsync(userId, tenantId, ct);
            var theirs = await permissions.GetEffectivePermissionsAsync(assignerId, tenantId, ct);
            return target.IsSubsetOf(theirs);
        }

        /// <summary>PIN bloqueado: SecurityEvent LOCKOUT y 423 'PIN bloqueado por 15 minutos.'.</summary>
        private async Task LockedAsync(ApplicationUser user, int tenantId, string deviceCode, CancellationToken ct)
        {
            await security.WriteAsync(SecurityEventTypes.Lockout, SecurityOutcomes.Blocked, user.Id, tenantId, new { stage = "device", device = deviceCode, target = "pin" }, ct);
            throw new PinLockedException();
        }

        /// <summary>Usuarios (de la lista dada) con PIN definido en la compañía activa.</summary>
        internal async Task<HashSet<int>> UsersWithPinAsync(IReadOnlyCollection<int> userIds, CancellationToken ct)
        {
            if (userIds.Count == 0) return new HashSet<int>();
            var ids = await db.Set<UserPin>().AsNoTracking().Where(p => userIds.Contains(p.UserId)).Select(p => p.UserId).ToListAsync(ct);
            return ids.ToHashSet();
        }

        // ---------------------------------------------------------------- internos

        private static string ValidatePin(string? pin)
        {
            var error = PinRules.Validate(pin);
            if (error is not null) throw new ValidationException("pin", error);
            return pin!.Trim();
        }

        private async Task<UserPin> UpsertAsync(ApplicationUser user, int tenantId, string pinValue, int? by, CancellationToken ct)
        {
            var pin = await db.Set<UserPin>().AsTracking().FirstOrDefaultAsync(p => p.UserId == user.Id && p.TenantId == tenantId, ct);
            var deviceTokens = new List<RefreshToken>();
            if (pin is null)
            {
                pin = new UserPin { TenantId = tenantId, UserId = user.Id };
                db.Set<UserPin>().Add(pin);
            }
            else
            {
                // Cambiar o restablecer el PIN cierra las sesiones del usuario en aparatos (quien entró con el PIN viejo
                // no sigue renovando su sesión), en la misma unidad de trabajo que el hash nuevo.
                deviceTokens = await db.RefreshTokens
                    .Where(t => t.UserId == user.Id && t.TenantId == tenantId && t.RevokedAtUtc == null && t.UserDeviceId != null)
                    .ToListAsync(ct);
                var now = DateTime.UtcNow;
                foreach (var t in deviceTokens) t.RevokedAtUtc = now;
            }
            pin.PinHash = users.PasswordHasher.HashPassword(user, pinValue);
            pin.FailedCount = 0;
            pin.LockedUntilUtc = null;
            pin.UpdatedAtUtc = DateTime.UtcNow;
            pin.UpdatedBy = by;
            await db.SaveGuardedAsync(DuplicatePinMessage, ct);
            if (deviceTokens.Count > 0)
                await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Success, user.Id, tenantId,
                    new { scope = "devices", reason = "pin_changed", count = deviceTokens.Count }, ct);
            return pin;
        }

        private async Task RemoveAsync(int userId, int tenantId, int? by, CancellationToken ct)
        {
            var pin = await db.Set<UserPin>().AsTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.TenantId == tenantId, ct);
            // Sesiones en aparatos del usuario (refresh tokens ligados a un UserDevice): sin PIN no se vuelve a entrar.
            var deviceTokens = await db.RefreshTokens
                .Where(t => t.UserId == userId && t.TenantId == tenantId && t.RevokedAtUtc == null && t.UserDeviceId != null)
                .ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var t in deviceTokens) t.RevokedAtUtc = now;
            // El PIN es una credencial, no un registro con historial: se borra la fila (el hecho queda en SecurityEvent).
            if (pin is not null) db.Set<UserPin>().Remove(pin);
            db.SuppressAudit = true;
            try { await db.SaveChangesAsync(ct); }
            finally { db.SuppressAudit = false; }
            if (pin is not null)
                await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, userId, tenantId, new { target = "pin", action = "removed", by }, ct);
            if (deviceTokens.Count > 0)
                await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Success, userId, tenantId, new { scope = "devices", reason = "pin_removed", count = deviceTokens.Count }, ct);
        }

        private async Task EnsureAdminAsync(CancellationToken ct)
        {
            if (await permissions.HasPermissionAsync(PermissionCatalog.DevicesManage, ct) || await permissions.HasPermissionAsync(PermissionCatalog.AdminUsers, ct)) return;
            await security.WriteAsync(SecurityEventTypes.PermissionDenied, SecurityOutcomes.Blocked, tenant.UserId, tenant.TenantId, new { permission = $"{PermissionCatalog.DevicesManage}|{PermissionCatalog.AdminUsers}" }, ct);
            throw new ForbiddenException($"Falta el permiso '{PermissionCatalog.DevicesManage}' o '{PermissionCatalog.AdminUsers}'.");
        }

        /// <summary>
        /// Anti-escalada: el admin de plataforma solo lo toca otro admin de plataforma (si no, 404 sin oráculo); y el usuario
        /// destino no puede tener permisos efectivos que quien llama no tenga (403 'No puede asignar ni quitar el PIN de un
        /// usuario con más permisos que usted.'), porque con ese PIN se entraría como él en un aparato sin contraseña ni MFA.
        /// </summary>
        private async Task EnsureNotHigherAsync(ApplicationUser user, int tenantId, CancellationToken ct)
        {
            if (tenant.IsPlatformAdmin) return;
            if (user.IsPlatformAdmin) throw new NotFoundException("Usuario");
            var callerId = ((TenantContext)tenant).RequireUserId();
            if (user.Id == callerId) return;
            var target = await permissions.GetEffectivePermissionsAsync(user.Id, tenantId, ct);
            var mine = await permissions.GetEffectivePermissionsAsync(callerId, tenantId, ct);
            if (target.IsSubsetOf(mine)) return;
            await security.WriteAsync(SecurityEventTypes.PermissionDenied, SecurityOutcomes.Blocked, callerId, tenantId, new { target = "pin", user = user.Id, reason = "higher_privileges" }, ct);
            throw new ForbiddenException(HigherPrivilegesMessage);
        }

        /// <summary>Usuario interno (no de portal) con membresía en la compañía activa; si no, 404 'Usuario no encontrado.' (sin oráculo).</summary>
        private async Task<ApplicationUser> ResolveTenantUserAsync(int userId, int tenantId, CancellationToken ct)
        {
            var member = await db.UserTenants.AsNoTracking()
                .AnyAsync(m => m.UserId == userId && m.TenantId == tenantId && (m.User!.UserKind == null || m.User.UserKind.InternalCode != UserKinds.Portal), ct);
            if (!member) throw new NotFoundException("Usuario");
            return await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Usuario");
        }

        private static PinStatusDto ToStatus(UserPin? pin)
            => new(pin is not null, pin?.LockedUntilUtc is DateTime l && l > DateTime.UtcNow ? l : null, pin?.UpdatedAtUtc);
    }
}
