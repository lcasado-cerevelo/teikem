namespace Teikem.Infrastructure.Contracts;

// Lote 84 (P8) — ciclo COD: cobro contra entrega y reconciliación de las hojas (facturas) que regresan de la calle.

/// <summary>POST /cod/orders/{publicId}/collections: un cobro (parcial o total). Method = código del método (CASH, CHECK, COMPANY_CHECK, ATH_MOVIL, CARD); el cheque pide su número.</summary>
public sealed record CodCollectionRequest(string? Method = null, decimal? Amount = null, string? CheckNumber = null, string? Reference = null, string? Notes = null, DateTime? CollectedAtUtc = null);

public sealed record CodCollectionDto(long Id, string Method, string MethodLabel, decimal Amount, string? CheckNumber, string? Reference, DateTime CollectedAtUtc, string Status, string StatusLabel,
    string StatusColor, DateTime? ReconciledAtUtc, string? Notes);

/// <summary>El COD de una orden: lo pedido, lo cobrado, lo que falta y cada cobro.</summary>
public sealed record CodOrderDto(Guid OrderPublicId, string OrderNumber, string? InvoiceNumber, Guid ClientPublicId, string ClientName, string OrderStatus, string OrderStatusLabel, decimal CodAmount,
    decimal Collected, decimal Pending, string CodStatus, string CodStatusLabel, string CodStatusColor, IReadOnlyList<CodCollectionDto> Collections);

public sealed record CodOrderListItemDto(Guid OrderPublicId, string OrderNumber, string? InvoiceNumber, Guid ClientPublicId, string ClientName, string OrderStatusLabel, decimal CodAmount, decimal Collected,
    decimal Pending, string CodStatus, string CodStatusLabel, string CodStatusColor, DateTime CreatedAtUtc);

/// <summary>Un código leído (de una hoja escaneada o escrito): Source dice de dónde (archivo y página).</summary>
public sealed record CodReconcileCode(string? Code = null, string? Source = null);

/// <summary>
/// POST /cod/reconcile: los códigos de la corrida. DryRun = solo dice qué pasaría (no cuadra nada ni guarda la corrida). ClientPublicId = las hojas son de ese cliente (el número de factura se cuenta por cliente
/// y se repite entre clientes: con el cliente, el código solo busca entre sus órdenes).
/// </summary>
public sealed record CodReconcileRequest(IReadOnlyList<CodReconcileCode>? Codes = null, string? Source = null, bool? DryRun = null, Guid? ClientPublicId = null);

public sealed record CodReconcileItemDto(string Code, string? Source, string Result, string Message, Guid? OrderPublicId, string? OrderNumber, string? InvoiceNumber, string? ClientName, decimal CodAmount,
    decimal Reconciled, int Collections);

public sealed record CodReconcileResultDto(Guid? RunPublicId, bool DryRun, int Total, int Reconciled, int Issues, IReadOnlyList<CodReconcileItemDto> Items);

public sealed record CodRunListItemDto(Guid PublicId, string? Source, int Total, int Reconciled, int Issues, DateTime CreatedAtUtc, string? CreatedByName);

public sealed record CodRunDto(Guid PublicId, string? Source, int Total, int Reconciled, int Issues, DateTime CreatedAtUtc, string? CreatedByName, IReadOnlyList<CodReconcileItemDto> Items);
