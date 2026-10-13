using Teikem.Domain.Common;

namespace Teikem.Domain.Printing;

/// <summary>
/// Lote 43 (F-G1) — Impresora: un PERFIL de impresión de la compañía (no un aparato que el servidor conozca). El navegador arma el PDF
/// y el sistema operativo elige la impresora física (D-FG1: el servidor nunca abre conexiones a impresoras).
/// - Uso LABEL (etiquetas 4X6/4X4/4X2) o DOCUMENT (carta, LETTER); el uso no se cambia después de crearla.
/// - ConnectionType: BROWSER (PDF + diálogo del navegador) o QZ_TRAY (directo a la impresora del sistema con QZ Tray, instalado en cada
///   computadora; el navegador habla con QZ Tray en localhost). SystemName = nombre EXACTO de la impresora en Windows/macOS (vacío = Name).
/// - IsDefault: la predeterminada de la compañía para su uso (UX_Printer_Default: una por uso entre las activas). Desactivarla
///   la deja sin predeterminada (D-FG6). El nombre es único entre las activas (UX_Printer_Name).
/// - Nunca DELETE: baja lógica (IsActive = 0).
/// </summary>
[AuditEntity(Constants.EntityTypes.Printer)]
public class Printer : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int PrinterId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Nombre exacto de la impresora en el sistema de la computadora (QZ Tray imprime por este nombre); null = Name.</summary>
    public string? SystemName { get; set; }
    public string? Model { get; set; }
    public string? Notes { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = Constants.PrinterConnections.Browser;
    public string MediaSize { get; set; } = string.Empty;
    public bool RotateMedia { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}
