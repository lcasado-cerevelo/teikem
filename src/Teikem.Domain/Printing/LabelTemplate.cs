using Teikem.Domain.Common;

namespace Teikem.Domain.Printing;

/// <summary>
/// Lote 43 (F-G1) — Plantilla de label: el DISEÑO del label de envío (qué elementos se ven, en cuál de las 9 zonas y de qué tamaño),
/// independiente del tamaño de la etiqueta (ese sale de la impresora). LayoutJson es el JSON canónico de <see cref="LabelLayout"/>
/// (lo valida y lo escribe el servicio; sin ISJSON en SQL). Una predeterminada por compañía (UX_LabelTemplate_Default); nombre único
/// entre las activas (UX_LabelTemplate_Name). Nunca DELETE.
/// </summary>
[AuditEntity(Constants.EntityTypes.LabelTemplate)]
public class LabelTemplate : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int LabelTemplateId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LayoutJson { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}
