using Teikem.Domain.Common;

namespace Teikem.Domain.Trips;

public static class DriverNoticeCodes
{
    /// <summary>EntityType de la bitácora (sembrado en la sección 2026-10-23 (d) de update.sql).</summary>
    public const string EntityType = "DRIVER_NOTICE";
    public const string ManifestDispatched = "MANIFEST_DISPATCHED";
    public const string Message = "MESSAGE";
    public const int TitleMaxLength = 120;
    public const int BodyMaxLength = 1000;
    /// <summary>Los avisos se conservan este tiempo y luego se purgan.</summary>
    public const int RetentionDays = 60;
    public const int MaxListed = 100;
}

/// <summary>
/// Un aviso para el chofer en su app (bandeja de avisos): lo escribe la oficina o lo genera el sistema (p. ej. «Nuevo manifiesto» al despacharle una ruta). Se marca leído desde la app.
/// El aviso emergente (push) llega más adelante; esta bandeja es la fuente.
/// </summary>
[AuditEntity(DriverNoticeCodes.EntityType)]
public class DriverNotice : ITenantScoped
{
    public int DriverNoticeId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public int DriverId { get; set; }
    public string Kind { get; set; } = DriverNoticeCodes.Message;
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public int? TripId { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public DateTime? ReadAtUtc { get; set; }
    /// <summary>Cuándo se mandó el aviso emergente al teléfono (null = pendiente de mandar).</summary>
    [NotAudited] public DateTime? PushedAtUtc { get; set; }
}

public static class DriverNoticeRules
{
    public const string TitleRequiredMessage = "Indique el título del aviso.";
    public const string TitleTooLongMessage = "El título no puede pasar de 120 caracteres.";
    public const string BodyTooLongMessage = "El texto no puede pasar de 1000 caracteres.";
    public static string ManifestDispatchedTitle(string code) => $"Nuevo manifiesto {code}";
    public const string ManifestDispatchedBody = "Se le despachó un manifiesto. Ábralo en la app y acéptelo para salir.";
}
