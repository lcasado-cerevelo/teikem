using Teikem.Domain.Common;

namespace Teikem.Domain.Entities;

/// <summary>
/// Lote 8A — PIN personal (4 a 6 dígitos) con el que el usuario entra en los aparatos de almacén de la compañía. Uno por
/// usuario y compañía (UQ_UserPin_User). Solo se guarda el hash, con el mismo PasswordHasher de Identity ([SensitiveData]).
/// 5 intentos fallidos seguidos → bloqueo de 15 minutos (PinRules). Es una credencial, no un registro con historial: los
/// cambios quedan en SecurityEvent (PASSWORD_CHANGE, detalle 'pin'), no en AuditLog.
/// </summary>
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
