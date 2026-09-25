using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Identity;

namespace Teikem.Domain.Orders;

/// <summary>
/// Lote de importación de órdenes (Lote 3, ajuste D): resultado del paso "validar" (VALIDATED, inicial) con una entrada por
/// fila en RowsJson (valores parseados, consignatario resuelto o por crear, errores por campo, avisos y, tras confirmar,
/// orderPublicId o el error). "Confirmar" crea las filas válidas y lo pasa a CONFIRMED; "descartar" a DISCARDED (terminal).
/// El estatus se mueve solo vía StatusService (dominio ImportBatchStatus, EntityType IMPORT_BATCH).
/// </summary>
[AuditEntity(Constants.EntityTypes.ImportBatch)]
public class ImportBatch : ITenantScoped, IHasStatus
{
    public int ImportBatchId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    /// <summary>ImportKinds: ORDER.</summary>
    public string Kind { get; set; } = ImportKinds.Order;
    public int ImportTemplateId { get; set; }
    public int ClientId { get; set; }
    public string? FileName { get; set; }
    public int RowCount { get; set; }
    public int ValidRows { get; set; }
    public int StatusCodeId { get; set; }
    /// <summary>Registro de trabajo del lote (una entrada por fila; forma ImportRowRecord). Excluido del diff de auditoría por tamaño.</summary>
    [NotAudited] public string RowsJson { get; set; } = "[]";
    public int CreatedBy { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedBy { get; set; }

    public ImportTemplate? Template { get; set; }
    public Client? Client { get; set; }
    public StatusCode? Status { get; set; }
    public ApplicationUser? Creator { get; set; }
}
