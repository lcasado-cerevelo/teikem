using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.PendingP0;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services
{
    /// <summary>423 'PIN bloqueado por 15 minutos.' (5 intentos fallidos seguidos en el aparato).</summary>
    public sealed class PinLockedException() : TeikemException(PinRules.LockedMessage, 423, "pin_locked");

    /// <summary>
    /// Lote 8A (P1) — PIN por usuario para entrar en los aparatos de almacén (UserPin, uno por usuario y compañía).
    /// - El PIN se guarda solo como hash con el MISMO PasswordHasher de Identity; nunca sale en un DTO ni en la bitácora.
    /// - Mi cuenta: definirlo exige la contraseña actual (400 'La contraseña actual es incorrecta.'); quitarlo no.
    /// - Administración (devices.manage o admin.users): asignar o quitar el PIN de un usuario interno de la misma compañía.
    /// - Guardar un PIN reinicia el contador de intentos y el bloqueo; quitarlo cierra las sesiones del usuario en aparatos.
    /// - Todo cambio escribe SecurityEvent PASSWORD_CHANGE con detalle target = 'pin'.
    /// - En el login por aparato: 5 fallos seguidos → bloqueo de 15 minutos (PinRules); 401 'PIN incorrecto.' / 423.
    /// </summary>
    public sealed class PinService(
        TeikemDbContext db, UserManager<ApplicationUser> users, ITenantContext tenant, PermissionService permissions, ISecurityEventWriter security)
    {
        public const string WrongPasswordMessage = "La contraseña actual es incorrecta.";
        public const string WrongPinMessage = "PIN incorrecto.";
        public const string DuplicatePinMessage = "El PIN del usuario cambió al mismo tiempo en otra sesión; intente de nuevo.";

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
            if (!await users.CheckPasswordAsync(user, req.CurrentPassword ?? string.Empty))
            {
                await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Failure, userId, tenantId, new { target = "pin", reason = "password" }, ct);
                throw new ValidationException("currentPassword", WrongPasswordMessage);
            }
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
            await RemoveAsync(user.Id, tenantId, by: tenant.UserId, ct);
        }

        // ---------------------------------------------------------------- login por aparato

        /// <summary>
        /// Verifica el PIN en el login por aparato (el contexto ya corre como la compañía del aparato). Bloqueado → 423 y
        /// SecurityEvent LOCKOUT; incorrecto o sin PIN → 401 'PIN incorrecto.' y cuenta el intento (el 5.º bloquea: 423).
        /// Correcto → reinicia el contador. Los eventos LOGIN de éxito los escribe AuthService.
        /// </summary>
        internal async Task VerifyForLoginAsync(ApplicationUser user, int tenantId, string? pinValue, string deviceCode, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var pin = await db.Set<UserPin>().AsTracking().FirstOrDefaultAsync(p => p.UserId == user.Id && p.TenantId == tenantId, ct);
            if (pin is not null && PinRules.IsLocked(pin.LockedUntilUtc, now))
            {
                await security.WriteAsync(SecurityEventTypes.Lockout, SecurityOutcomes.Blocked, user.Id, tenantId, new { stage = "device", device = deviceCode, target = "pin" }, ct);
                throw new PinLockedException();
            }

            var result = pin is null || string.IsNullOrEmpty(pinValue)
                ? PasswordVerificationResult.Failed
                : users.PasswordHasher.VerifyHashedPassword(user, pin.PinHash, pinValue);
            if (result == PasswordVerificationResult.Failed)
            {
                var locked = false;
                if (pin is not null)
                {
                    (pin.FailedCount, pin.LockedUntilUtc) = PinRules.RegisterFailure(pin.FailedCount, now);
                    locked = PinRules.IsLocked(pin.LockedUntilUtc, now);
                    await db.SaveChangesAsync(ct);
                }
                await security.WriteAsync(locked ? SecurityEventTypes.Lockout : SecurityEventTypes.Login, locked ? SecurityOutcomes.Blocked : SecurityOutcomes.Failure,
                    user.Id, tenantId, new { stage = "device", device = deviceCode }, ct);
                if (locked) throw new PinLockedException();
                throw new UnauthorizedException(WrongPinMessage);
            }

            // Correcto: contador a cero; si el hasher pide re-hash (cambio de versión de Identity), se actualiza.
            if (result == PasswordVerificationResult.SuccessRehashNeeded) pin!.PinHash = users.PasswordHasher.HashPassword(user, pinValue!);
            if (pin!.FailedCount != 0 || pin.LockedUntilUtc is not null)
            {
                pin.FailedCount = 0;
                pin.LockedUntilUtc = null;
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
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
            if (pin is null)
            {
                pin = new UserPin { TenantId = tenantId, UserId = user.Id };
                db.Set<UserPin>().Add(pin);
            }
            pin.PinHash = users.PasswordHasher.HashPassword(user, pinValue);
            pin.FailedCount = 0;
            pin.LockedUntilUtc = null;
            pin.UpdatedAtUtc = DateTime.UtcNow;
            pin.UpdatedBy = by;
            await db.SaveGuardedAsync(DuplicatePinMessage, ct);
            return pin;
        }

        private async Task RemoveAsync(int userId, int tenantId, int? by, CancellationToken ct)
        {
            var pin = await db.Set<UserPin>().AsTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.TenantId == tenantId, ct);
            // Sesiones en aparatos del usuario (refresh tokens ligados a un UserDevice): sin PIN no se vuelve a entrar.
            var deviceTokens = await db.RefreshTokens
                .Where(t => t.UserId == userId && t.TenantId == tenantId && t.RevokedAtUtc == null && EF.Property<int?>(t, DeviceService.RefreshTokenDeviceColumn) != null)
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
            if (await permissions.HasPermissionAsync(P0Keys.DevicesManage, ct) || await permissions.HasPermissionAsync(PermissionCatalog.AdminUsers, ct)) return;
            await security.WriteAsync(SecurityEventTypes.PermissionDenied, SecurityOutcomes.Blocked, tenant.UserId, tenant.TenantId, new { permission = $"{P0Keys.DevicesManage}|{PermissionCatalog.AdminUsers}" }, ct);
            throw new ForbiddenException($"Falta el permiso '{P0Keys.DevicesManage}' o '{PermissionCatalog.AdminUsers}'.");
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

// ==================================================================== contratos propios de P1 (Lote 8A)

namespace Teikem.Infrastructure.Contracts
{
    /// <summary>
    /// Edición del aparato (PATCH): null = sin cambio. Name "" = quitar el nombre. DefaultWarehousePublicId fija el almacén
    /// por defecto; ClearDefaultWarehouse = true lo quita. Theme: LIGHT | DARK. RowVersion opcional (409 si cambió).
    /// </summary>
    public sealed record DevicePatchRequest(string? Name, Guid? DefaultWarehousePublicId, bool ClearDefaultWarehouse = false, string? Theme = null, string? RowVersion = null);

    /// <summary>Respuesta del heartbeat: el aparato desactivado recibe IsActive = false (la app bloquea la entrada).</summary>
    public sealed record DeviceHeartbeatDto(bool IsActive, Guid? DefaultWarehousePublicId, string? Theme, DateTime ServerTimeUtc);

    /// <summary>Estado del PIN (nunca el PIN): definido, bloqueado hasta (si está bloqueado) y última actualización.</summary>
    public sealed record PinStatusDto(bool HasPin, DateTime? LockedUntilUtc, DateTime? UpdatedAtUtc);
}

// ==================================================================== PENDIENTE DE P0 (borrar al integrar P0)
// Lote 8A: P0 (base compartida) declara estos tipos en sus propios archivos (Domain/Entities/UserDevice.cs,
// Domain/Entities/UserPin.cs, Contracts/DeviceContracts.cs, PinRules, PermissionCatalog.DevicesManage). Se declaran aquí
// SOLO para que esta pieza compile mientras P0 se integra. Al integrar P0:
//   1. Borrar todo lo que sigue desde este bloque hasta el final del archivo.
//   2. Quitar `using Teikem.Infrastructure.PendingP0;` de DeviceService, PinService, AuthService y UserAdminService, y
//      `using Teikem.Domain.Entities;` si P0 usó otro espacio de nombres para las entidades.
//   3. Reemplazar P0Keys.DevicesManage por PermissionCatalog.DevicesManage y P0Keys.UiTheme por la constante de P0.
//   4. Ajustar al contrato real de P0 las llamadas a PinRules y la construcción de DeviceDto (DeviceService.ToDtoAsync).
// Los choques son ruidosos a propósito: el mismo nombre en P0 da CS0101 (contratos) o CS0104 (entidades, PinRules).

namespace Teikem.Infrastructure.Contracts
{
    public sealed record DeviceCreateRequest(string Code, string? Name, string? Model, Guid? DefaultWarehousePublicId, string? Theme);
    public sealed record DeviceDto(
        Guid PublicId, string Code, string? Name, string? Model, string? Platform, string? AppVersion, bool IsEnrolled, DateTime? EnrolledAtUtc,
        DateTime? EnrollCodeExpiresUtc, DateTime? LastSeenUtc, int? LastUserId, string? LastUserName, Guid? DefaultWarehousePublicId,
        string? DefaultWarehouseCode, string? Theme, DateTime RegisteredAtUtc, bool IsActive, string? RowVersion);
    public sealed record DeviceCreatedDto(DeviceDto Device, string EnrollCode);
    public sealed record DeviceEnrollRequest(string EnrollCode, string? Model, string? AppVersion);
    public sealed record DeviceEnrolledDto(Guid DevicePublicId, string DeviceSecret, string TenantName, Guid? DefaultWarehousePublicId, string? Theme);
    public sealed record DeviceUsersRequest(Guid DevicePublicId, string DeviceSecret);
    public sealed record DeviceUserDto(int UserId, string FullName, string Initials);
    public sealed record DeviceLoginRequest(Guid DevicePublicId, string DeviceSecret, int UserId, string Pin);
    public sealed record PinSetRequest(string CurrentPassword, string Pin);
    public sealed record PinAdminSetRequest(string Pin);
    public sealed record HeartbeatRequest(Guid DevicePublicId, string DeviceSecret, string? AppVersion);
}

namespace Teikem.Domain.Entities
{
    /// <summary>Marca temporal: hace válido `using Teikem.Domain.Entities;` antes de integrar P0. Borrar con el bloque.</summary>
    internal static class PendingP0Marker { }
}

namespace Teikem.Infrastructure.PendingP0
{
    using Teikem.Domain.Common;

    public static class P0Keys
    {
        public const string DevicesManage = "devices.manage";
        public const string UiTheme = "UiTheme";
        public const string EntityTypeUserDevice = "USER_DEVICE";
    }

    [AuditEntity(P0Keys.EntityTypeUserDevice)]
    public class UserDevice : ITenantScoped, ISoftDeletable
    {
        public int UserDeviceId { get; set; }
        [NotAudited] public Guid PublicId { get; set; }
        public int TenantId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Model { get; set; }
        public int PlatformLookupId { get; set; }
        public string? AppVersion { get; set; }
        [SensitiveData] public string? EnrollCodeHash { get; set; }
        public DateTime? EnrollCodeExpiresUtc { get; set; }
        [SensitiveData] public string? SecretHash { get; set; }
        public DateTime? EnrolledAtUtc { get; set; }
        [NotAudited] public DateTime? LastSeenUtc { get; set; }
        public int? LastUserId { get; set; }
        public int? DefaultWarehouseId { get; set; }
        public int? ThemeLookupId { get; set; }
        public int? RegisteredBy { get; set; }
        [NotAudited] public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        [NotAudited] public byte[]? RowVersion { get; set; }
    }

    public class UserPin : ITenantScoped
    {
        public int UserPinId { get; set; }
        public int TenantId { get; set; }
        public int UserId { get; set; }
        [SensitiveData] public string PinHash { get; set; } = string.Empty;
        public int FailedCount { get; set; }
        public DateTime? LockedUntilUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public int? UpdatedBy { get; set; }
    }

    /// <summary>Reglas puras del PIN (P0): 4 a 6 dígitos, sin secuencias triviales, 5 fallos → bloqueo de 15 minutos.</summary>
    public static class PinRules
    {
        public const int MinLength = 4;
        public const int MaxLength = 6;
        public const int MaxFailedAttempts = 5;
        public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
        public const string FormatMessage = "El PIN debe tener de 4 a 6 dígitos.";
        public const string TrivialMessage = "El PIN no puede ser una secuencia trivial.";
        public const string LockedMessage = "PIN bloqueado por 15 minutos.";

        public static string? Validate(string? pin)
        {
            var p = pin?.Trim() ?? string.Empty;
            if (p.Length is < MinLength or > MaxLength || !p.All(char.IsAsciiDigit)) return FormatMessage;
            if (p.All(c => c == p[0])) return TrivialMessage;
            bool Step(int d) { for (var i = 1; i < p.Length; i++) if (p[i] - p[i - 1] != d) return false; return true; }
            return Step(1) || Step(-1) ? TrivialMessage : null;
        }

        public static bool IsLocked(DateTime? lockedUntilUtc, DateTime nowUtc) => lockedUntilUtc is DateTime l && l > nowUtc;

        /// <summary>Un fallo más: al llegar a 5 se bloquea 15 minutos y el contador vuelve a cero.</summary>
        public static (int FailedCount, DateTime? LockedUntilUtc) RegisterFailure(int failedCount, DateTime nowUtc)
        {
            var next = Math.Max(0, failedCount) + 1;
            return next >= MaxFailedAttempts ? (0, nowUtc.Add(LockDuration)) : (next, null);
        }
    }
}
