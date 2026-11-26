namespace Teikem.Infrastructure.Contracts;

// ============================================================================================================================================================
// Lote 249 (Lote E) — contratos de la «Modificación de la orden de venta» (SalesOrderChangeService): registrar, previsualizar, aprobar, rechazar, retirar, la
// orden interna de adición, la bandeja «Por aprobar» y los ajustes de segunda aprobación (compañía y cliente). Lo que no viene no cambia.
// ============================================================================================================================================================

// ---------------------------------------------------------------- solicitudes

/// <summary>
/// Una operación de la modificación. Kind: ADD (ProductPublicId y Quantity; UnitPrice opcional, distinto del de lista exige sales.price), QTY (LineId y Quantity),
/// REMOVE (LineId), PRICE (LineId y UnitPrice), TAX (LineId, TaxPct y Note = el motivo; exige sales.tax), CLEAR_TAX (LineId), TERMS y NOTES (sus valores van en
/// <see cref="SalesOrderChangeRequest.Terms"/> y <see cref="SalesOrderChangeRequest.Notes"/>).
/// </summary>
public sealed record SalesOrderChangeLineOp(string? Kind, int? LineId = null, Guid? ProductPublicId = null, decimal? Quantity = null, decimal? UnitPrice = null,
    decimal? TaxPct = null, string? Note = null);

/// <summary>Los términos que cambian (null = no cambia): forma de pago y término (códigos; "" = quitarla), entrega (DELIVERY | PICKUP), consignatario y consignación.</summary>
public sealed record SalesOrderChangeTermsRequest(string? PaymentMethod = null, string? PaymentTerm = null, string? DeliveryMode = null, Guid? ConsigneePublicId = null,
    bool? ClearConsignee = null, bool? IsConsignment = null);

/// <summary>
/// Registrar una modificación de una orden ya solicitada. Obligatorios: las operaciones, quién autorizó en el cliente (nombre; cargo y contacto opcionales), cómo
/// (PHONE | EMAIL | IN_PERSON | MESSAGE), cuándo (vacío = ahora; ni futura ni anterior a la orden) y el motivo. AttachmentFilePublicId = la evidencia subida antes
/// con POST change-attachments (obligatoria si el ajuste de la compañía o del cliente lo exige). ReopenPack = reabrir en la misma llamada los empaques que lo
/// necesiten (warehouse.reopen-pack; ReopenReason, si no viene, es el motivo de la modificación). AllowBackorder deja en backorder lo que falte al subir o
/// agregar; OverrideCredit autoriza un crédito excedido (orders.credit_override). RowVersion = el de la ficha (409 si la orden cambió).
/// </summary>
public sealed record SalesOrderChangeRequest(
    IReadOnlyList<SalesOrderChangeLineOp>? Ops = null,
    string? AuthorizedByName = null,
    string? AuthorizedByTitle = null,
    int? AuthorizedByContactId = null,
    string? AuthorizationChannel = null,
    DateTime? AuthorizedAtUtc = null,
    string? Reason = null,
    Guid? AttachmentFilePublicId = null,
    SalesOrderChangeTermsRequest? Terms = null,
    string? Notes = null,
    bool? ReopenPack = null,
    string? ReopenReason = null,
    bool? AllowBackorder = null,
    bool? OverrideCredit = null,
    string? RowVersion = null);

/// <summary>
/// La orden interna de adición: las líneas nuevas (producto y cantidad; precio opcional con sales.price), notas y la misma autorización del cliente que una
/// modificación. Se crea y se solicita en una sola transacción (o al aprobar, si toca segunda aprobación).
/// </summary>
public sealed record SalesOrderAdditionRequest(
    IReadOnlyList<SalesOrderLineRequest>? Lines = null,
    string? Notes = null,
    string? AuthorizedByName = null,
    string? AuthorizedByTitle = null,
    int? AuthorizedByContactId = null,
    string? AuthorizationChannel = null,
    DateTime? AuthorizedAtUtc = null,
    string? Reason = null,
    Guid? AttachmentFilePublicId = null,
    bool? AllowBackorder = null,
    bool? OverrideCredit = null);

/// <summary>Aprobar (nota opcional; OverrideCredit si al aprobar el crédito ya no alcanza), rechazar (nota obligatoria) o retirar (nota opcional) una modificación pendiente.</summary>
public sealed record SalesOrderChangeDecisionRequest(string? Note = null, bool? OverrideCredit = null);

/// <summary>La bandeja «Por aprobar» de la compañía, paginada en el servidor (skip/take, tope 200), con buscador libre y orden.</summary>
/// <param name="Sort">registeredAt (más reciente primero, por omisión) | -registeredAt | order | client | delta | -delta.</param>
public sealed record SalesChangePendingQuery(int Skip = 0, int Take = 50, string? Q = null, string? Sort = null);

/// <summary>Ajustes de la compañía (null = no cambia). ApprovalMode: NONE | ALWAYS | ABOVE_AMOUNT (con monto &gt; 0).</summary>
public sealed record SalesChangeSettingsRequest(string? ApprovalMode = null, decimal? ApprovalAmount = null, bool? AttachmentRequired = null);

/// <summary>Ajustes del cliente (se reemplazan completos): ApprovalMode "" o null = hereda de la compañía; AttachmentRequired null = hereda.</summary>
public sealed record SalesClientChangeSettingsRequest(string? ApprovalMode = null, decimal? ApprovalAmount = null, bool? AttachmentRequired = null);

// ---------------------------------------------------------------- respuestas

/// <summary>Lo que el usuario puede hacer en la modificación (para la pantalla; el servidor lo vuelve a exigir).</summary>
public sealed record SalesChangePermissionsDto(bool Modify, bool Price, bool Tax, bool ReopenPack, bool Approve, bool CreditOverride);

/// <summary>Ajustes vigentes para una orden (el cliente manda; ApprovalFromClient / AttachmentFromClient dicen de dónde salió cada uno).</summary>
public sealed record SalesChangeEffectiveSettingsDto(string ApprovalMode, decimal? ApprovalAmount, bool AttachmentRequired, bool ApprovalFromClient, bool AttachmentFromClient);

/// <summary>
/// Posición de una línea antes de modificar: mínimo sin reabrir (lo salido + lo empacado), mínimo reabriendo (lo salido), lo recolectado, lo empacado, lo salido,
/// lo facturado, si está quitada y por qué no admite cambios (LockedReason) o no admite cambio de precio ni de IVU (PriceLockedReason).
/// </summary>
public sealed record SalesChangeLinePositionDto(int LineId, int LineNo, Guid ProductPublicId, string Sku, string Name, decimal QtyOrdered, decimal MinQuantity,
    decimal MinQuantityWithReopen, decimal InCollected, decimal InPacked, decimal Out, decimal Invoiced, bool IsRemoved, string? LockedReason, string? PriceLockedReason,
    decimal UnitPrice, decimal TaxRatePct, bool HasTaxOverride, bool IsKit);

/// <summary>Una recolección activa de la orden con su etapa frente a la modificación: REVERSIBLE, REOPENABLE o LEFT (y por qué).</summary>
public sealed record SalesChangeBatchDto(Guid PublicId, string Number, string Status, string Stage, string? Reason, string PackNumber, Guid? GroupPublicId, string? GroupNumber,
    Guid? ShipmentPublicId, string? ShipmentNumber);

/// <summary>Una factura viva de la orden o de una de sus adiciones, con su estado frente a QuickBooks (NOT_ISSUED | PENDING_EXPORT | EXPORTED_UNCONFIRMED | IN_QUICKBOOKS).</summary>
public sealed record SalesOrderInvoiceRefDto(Guid PublicId, string Number, string Status, string QuickBooksState, DateOnly? IssueDate, decimal Total, Guid OrderPublicId,
    string OrderNumber, bool IsAddition);

/// <summary>
/// GET edit-options: la etapa de la orden y sus bloqueos (OrderBlocker; MustUseAddition = facturada o salió completa: solo se le puede añadir), las recolecciones
/// con su etapa, la posición por línea, las facturas de la orden y de sus adiciones, los ajustes vigentes, la modificación pendiente y los permisos del usuario.
/// </summary>
public sealed record SalesOrderEditOptionsDto(
    Guid OrderPublicId,
    string OrderNumber,
    string Status,
    string? OrderBlocker,
    bool MustUseAddition,
    bool CanAddition,
    IReadOnlyList<SalesChangeLinePositionDto> Lines,
    IReadOnlyList<SalesChangeBatchDto> Batches,
    IReadOnlyList<SalesOrderInvoiceRefDto> Invoices,
    SalesChangeEffectiveSettingsDto Settings,
    SalesOrderChangeListItemDto? Pending,
    SalesChangePermissionsDto Permissions,
    string BillingTrigger,
    decimal Total,
    string? RowVersion,
    bool IsAddition = false,
    Guid? RootPublicId = null,
    string? RootNumber = null);

/// <summary>Un campo que cambió (cantidad, precio, % e importe del IVU, razón, términos), con su valor antes y después.</summary>
public sealed record SalesChangeFieldDiffDto(string Field, string? Before, string? After);

/// <summary>Una línea en la diferencia: ADDED (nueva o reactivada), REMOVED (quitada) o CHANGED (con sus campos).</summary>
public sealed record SalesChangeDiffLineDto(int LineId, int LineNo, string Sku, string Name, string Change, IReadOnlyList<SalesChangeFieldDiffDto> Fields);

/// <summary>La diferencia entre antes y después.</summary>
public sealed record SalesChangeDiffDto(IReadOnlyList<SalesChangeDiffLineDto> Added, IReadOnlyList<SalesChangeDiffLineDto> Removed, IReadOnlyList<SalesChangeDiffLineDto> Changed,
    IReadOnlyList<SalesChangeFieldDiffDto> Terms, decimal TotalBefore, decimal TotalAfter, decimal Delta);

/// <summary>Una línea en la foto (antes o después).</summary>
public sealed record SalesChangeSnapshotLineDto(int LineId, int LineNo, string Sku, string Name, decimal QtyOrdered, decimal QtyShipped, decimal QtyInvoiced,
    decimal UnitPrice, decimal? ResolvedUnitPrice, string? PriceSource, decimal TaxRatePct, decimal TaxAmount, string? TaxReason, decimal? TaxComputedRatePct,
    decimal? TaxComputedAmount, bool IsRemoved);

/// <summary>Los términos en la foto.</summary>
public sealed record SalesChangeSnapshotTermsDto(string? PaymentMethod, string? PaymentTerm, string? DeliveryMode, string? ConsigneeName, bool IsConsignment, string? Notes);

/// <summary>La foto de la orden antes o después de la modificación (líneas, términos y totales).</summary>
public sealed record SalesChangeSnapshotDto(IReadOnlyList<SalesChangeSnapshotLineDto> Lines, SalesChangeSnapshotTermsDto Terms, decimal Subtotal, decimal TaxAmount,
    decimal ShippingCharge, decimal ShippingTaxAmount, decimal Total);

/// <summary>Los efectos en el almacén: recolecciones que se deshacen (reversa mínima) y empaques que se reabren, con el texto del aviso.</summary>
public sealed record SalesChangeEffectsDto(IReadOnlyList<string> ReversedBatches, IReadOnlyList<string> ReopenedPacks, string WarehouseImpact);

/// <summary>
/// POST changes/preview (no escribe nada): antes, después proyectado, diferencia y resumen, efectos, crédito, si pide segunda aprobación (y por qué), si exige
/// adjunto, si hay que reabrir y los bloqueos (texto exacto; vacío = se puede registrar).
/// </summary>
public sealed record SalesOrderChangePreviewDto(
    SalesChangeSnapshotDto Before,
    SalesChangeSnapshotDto After,
    SalesChangeDiffDto Diff,
    string Summary,
    SalesChangeEffectsDto Effects,
    SalesCreditDto Credit,
    bool CreditChecked,
    bool CreditExceeded,
    bool RequiresApproval,
    string? ApprovalRule,
    bool AttachmentRequired,
    bool NeedsReopen,
    IReadOnlyList<string> Blockers,
    decimal TotalBefore,
    decimal TotalAfter,
    decimal TotalDelta);

/// <summary>Una modificación en el historial de la orden o en la bandeja «Por aprobar».</summary>
public sealed record SalesOrderChangeListItemDto(
    Guid PublicId,
    Guid OrderPublicId,
    string OrderNumber,
    string? ClientName,
    int ChangeNo,
    string Kind,
    string Status,
    string StatusLabel,
    string StatusColor,
    bool IsPending,
    int RegisteredBy,
    string? RegisteredByName,
    DateTime RegisteredAtUtc,
    string AuthorizedByName,
    string? AuthorizedByTitle,
    string Channel,
    string ChannelLabel,
    string Reason,
    string? Summary,
    decimal TotalBefore,
    decimal TotalAfter,
    decimal TotalDelta,
    bool RequiresApproval,
    string? ApprovalRule,
    string? DecidedByName,
    DateTime? DecidedAtUtc,
    Guid? AdditionPublicId,
    string? AdditionNumber);

/// <summary>Una modificación completa: el renglón del historial más el contacto, el adjunto, las banderas, la decisión, las fotos, la diferencia y los efectos.</summary>
public sealed record SalesOrderChangeDto(
    SalesOrderChangeListItemDto Item,
    int? AuthorizedByContactId,
    DateTime AuthorizedAtUtc,
    Guid? AttachmentFilePublicId,
    string? AttachmentFileName,
    int LinesAdded,
    int LinesRemoved,
    int LinesChanged,
    int ReversedBatchCount,
    bool HasPriceChange,
    bool HasTaxOverride,
    bool HasTermsChange,
    bool ReopenedPack,
    bool CreditOverride,
    int? DecidedBy,
    string? DecisionNote,
    DateTime? AppliedAtUtc,
    SalesChangeSnapshotDto? Before,
    SalesChangeSnapshotDto? After,
    SalesChangeDiffDto? Diff,
    SalesChangeEffectsDto? Effects);

/// <summary>La bandeja «Por aprobar» paginada en el servidor (Items de la página, Total con el filtro).</summary>
public sealed record SalesChangePendingPageDto(IReadOnlyList<SalesOrderChangeListItemDto> Items, int Total, int Skip, int Take);

/// <summary>Ajustes de la compañía.</summary>
public sealed record SalesChangeSettingsDto(string ApprovalMode, decimal? ApprovalAmount, bool AttachmentRequired);

/// <summary>Ajustes del cliente (null = hereda) y los vigentes que resultan.</summary>
public sealed record SalesClientChangeSettingsDto(Guid ClientPublicId, string? ApprovalMode, decimal? ApprovalAmount, bool? AttachmentRequired, SalesChangeEffectiveSettingsDto Effective);
