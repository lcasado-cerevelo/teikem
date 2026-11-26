using Teikem.Domain.Common;

namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 249 (Lote E: modificaciones de órdenes de venta) — constantes de la «Modificación de la orden». Viven aquí (y no en CatalogDomains) para que el lote se pueda
/// pasar solo a otra rama. El EntityType, el dominio de estatus y el catálogo de canales se siembran en la sección «Lote 249» de logistica-db-update.sql.
/// </summary>
public static class SalesChangeCodes
{
    /// <summary>EntityType de la bitácora, del historial de estatus y de la fuente de datos «Modificaciones de órdenes de venta».</summary>
    public const string EntityType = "SALES_ORDER_CHANGE";
    /// <summary>Dominio de estatus (StatusCode) de la modificación.</summary>
    public const string StatusDomain = "SalesOrderChangeStatus";
    /// <summary>Dominio de catálogo (LookupCode) del canal por el que autorizó el cliente.</summary>
    public const string ChannelDomain = "SalesChangeChannel";
    /// <summary>Versión de la foto de antes y después (SalesChangeSnapshot.V).</summary>
    public const int SnapshotVersion = 1;
}

/// <summary>
/// Estatus de una modificación. PENDING_APPROVAL (inicial: espera la segunda aprobación; no se aplicó nada) → APPLIED (terminal: todos los efectos en la misma
/// transacción); REJECTED y WITHDRAWN (terminales, solo desde PENDING_APPROVAL, sin efectos). Sin segunda aprobación la modificación nace y pasa a APPLIED en
/// la misma transacción.
/// </summary>
public static class SalesOrderChangeStatuses
{
    public const string PendingApproval = "PENDING_APPROVAL";
    public const string Applied = "APPLIED";
    public const string Rejected = "REJECTED";
    public const string Withdrawn = "WITHDRAWN";
    public static readonly string[] All = { PendingApproval, Applied, Rejected, Withdrawn };
}

/// <summary>Tipo de modificación: MODIFY (cambia la orden misma) | ADDITION (crea una orden interna de adición vinculada; CK_SalesOrderChange_Kind en SQL).</summary>
public static class SalesChangeKinds
{
    public const string Modify = "MODIFY";
    public const string Addition = "ADDITION";
    public static readonly string[] All = { Modify, Addition };
}

/// <summary>Cómo autorizó el cliente (catálogo SalesChangeChannel): por teléfono, por correo, en persona o por mensaje.</summary>
public static class SalesChangeChannels
{
    public const string Phone = "PHONE";
    public const string Email = "EMAIL";
    public const string InPerson = "IN_PERSON";
    public const string Message = "MESSAGE";
    public static readonly string[] All = { Phone, Email, InPerson, Message };
}

/// <summary>
/// Cuándo pide segunda aprobación una modificación (Tenant/Client.SalesChangeApprovalMode): NONE (nunca), ALWAYS (siempre) o ABOVE_AMOUNT (cuando el cambio del
/// total, en valor absoluto, es ESTRICTAMENTE mayor que el monto). CK_Tenant_SalesChangeApproval / CK_Client_SalesChangeApproval en SQL.
/// </summary>
public static class SalesChangeApprovalModes
{
    public const string None = "NONE";
    public const string Always = "ALWAYS";
    public const string AboveAmount = "ABOVE_AMOUNT";
    public static readonly string[] All = { None, Always, AboveAmount };
}

/// <summary>
/// Lote 249 — una modificación de una orden de venta ya solicitada (o la orden interna de adición que nació de ella): quién la registró, quién la autorizó en el
/// cliente y cómo, el motivo, el adjunto, la foto de antes y después, los efectos, los totales y la segunda aprobación. Nunca DELETE: una pendiente se rechaza o
/// se retira. ChangeNo es correlativo por orden (UQ_SalesOrderChange_No). IsPending = 1 solo en PENDING_APPROVAL (UX_SalesOrderChange_Pending: una sola por
/// orden). Las fotos JSON no se copian a la bitácora ([NotAudited]): ya viven aquí.
/// </summary>
[AuditEntity(SalesChangeCodes.EntityType)]
public class SalesOrderChange : ITenantScoped, IHasStatus, IAuditStamped
{
    public int SalesOrderChangeId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int SalesOrderId { get; set; }
    public int ChangeNo { get; set; }
    /// <summary>SalesChangeKinds: MODIFY | ADDITION.</summary>
    public string Kind { get; set; } = SalesChangeKinds.Modify;
    public int StatusCodeId { get; set; }
    /// <summary>1 mientras está PENDING_APPROVAL (índice único filtrado: una sola pendiente por orden).</summary>
    [NotAudited] public bool IsPending { get; set; }

    // --- registro y autorización del cliente
    public int RegisteredBy { get; set; }
    public DateTime RegisteredAtUtc { get; set; }
    public string AuthorizedByName { get; set; } = string.Empty;
    public string? AuthorizedByTitle { get; set; }
    /// <summary>El contacto del cliente que autorizó (opcional; validado contra el cliente de la orden).</summary>
    public int? AuthorizedByContactId { get; set; }
    /// <summary>LookupCode SalesChangeChannel.</summary>
    public int AuthorizationChannelLookupId { get; set; }
    public DateTime AuthorizedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    /// <summary>StoredFile DOCUMENT cuyo dueño es la ORDEN (captura del correo o del mensaje); uno solo por modificación (UX_SalesOrderChange_Attachment).</summary>
    public int? AttachmentFileId { get; set; }

    // --- foto y efectos (JSON estable en camelCase; no van a la bitácora)
    [NotAudited] public string RequestJson { get; set; } = "{}";
    [NotAudited] public string BeforeJson { get; set; } = "{}";
    [NotAudited] public string AfterJson { get; set; } = "{}";
    [NotAudited] public string? EffectsJson { get; set; }
    public string? Summary { get; set; }

    // --- totales y contadores
    public decimal TotalBefore { get; set; }
    public decimal TotalAfter { get; set; }
    public decimal TotalDelta { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public int LinesChanged { get; set; }
    public int ReversedBatchCount { get; set; }

    // --- banderas
    public bool HasPriceChange { get; set; }
    public bool HasTaxOverride { get; set; }
    public bool HasTermsChange { get; set; }
    public bool ReopenedPack { get; set; }
    public bool CreditOverride { get; set; }
    public bool RequiresApproval { get; set; }

    // --- aprobación y aplicación
    /// <summary>La regla que pidió la segunda aprobación (SalesChangeApprovalModes: ALWAYS | ABOVE_AMOUNT); NULL = no la pidió.</summary>
    public string? ApprovalRule { get; set; }
    /// <summary>Huella SHA-256 (64 hex) del estado de la orden al registrar: al aprobar se compara (si cambió → 409).</summary>
    public string BaseFingerprint { get; set; } = string.Empty;
    public int? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionNote { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
    /// <summary>Solo en ADDITION: la orden interna de adición que nació de esta modificación (CK_SalesOrderChange_Addition).</summary>
    public int? AdditionSalesOrderId { get; set; }

    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}
