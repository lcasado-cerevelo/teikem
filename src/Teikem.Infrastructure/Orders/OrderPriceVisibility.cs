using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Orders;

/// <summary>
/// Lote 39 (decisión del dueño, 2026-10-12): ÚNICO lugar donde se decide si el usuario actual ve los precios de las órdenes
/// (permiso orders.view_price, del principal vía PermissionService; el administrador de plataforma lo tiene). Lo consultan:
/// OrderReadService (lista, lookup y ficha; también las respuestas de crear, editar, confirmar, reprecio, cancelar y estatus,
/// que salen de la ficha), OrderStatusController (GET /orders/{id}/quote → 403), OrderStatusEffect (422 credit_exceeded sin cifras),
/// StatusService (comentario del override de crédito en el historial de la orden), OrderImportService (error de crédito de una fila)
/// y TransportOrderDataSource (campo QuotedAmount de Análisis). Lo que se oculta lo dice <see cref="OrderPriceRules"/>.
/// Sin este servicio (null), los llamadores ocultan: falla cerrado.
/// </summary>
public sealed class OrderPriceVisibility(PermissionService permissions)
{
    public const string Permission = PermissionCatalog.OrdersViewPrice;

    /// <summary>Título exacto del 403 de GET /orders/{id}/quote sin el permiso.</summary>
    public const string ForbiddenMessage = "Falta el permiso 'orders.view_price'.";

    /// <summary>¿El usuario actual ve los precios de las órdenes?</summary>
    public Task<bool> CanViewAsync(CancellationToken ct) => permissions.HasPermissionAsync(Permission, ct);

    /// <summary>Guard de la cotización completa: 403 'Falta el permiso 'orders.view_price'.' + PERMISSION_DENIED.</summary>
    public Task EnsureAsync(CancellationToken ct) => permissions.EnsureAsync(Permission, ct);

    /// <summary>Igual que <see cref="CanViewAsync(CancellationToken)"/>, pero con el servicio opcional: null = no ve (falla cerrado).</summary>
    public static async Task<bool> CanViewAsync(OrderPriceVisibility? prices, CancellationToken ct)
        => prices is not null && await prices.CanViewAsync(ct);
}

/// <summary>
/// Lote 39: reglas puras de qué se oculta sin orders.view_price (sin romper el contrato: los campos quedan en null o sin cifras,
/// nunca 403 salvo la cotización completa). El monto COD (CodAmount) NO es un precio y sigue visible.
/// </summary>
public static class OrderPriceRules
{
    /// <summary>Fila del listado / lookup: QuotedAmount → null.</summary>
    public static OrderListItemDto Apply(OrderListItemDto item, bool canView) => canView ? item : item with { QuotedAmount = null };

    /// <summary>Ficha (y respuestas de crear/editar/confirmar/reprecio/cancelar/estatus): QuotedAmount → null.</summary>
    public static OrderDetailDto Apply(OrderDetailDto order, bool canView) => canView ? order : order with { QuotedAmount = null };

    /// <summary>Fila de un lote de importación: el error de crédito excedido pierde las cifras (los demás errores no cambian).</summary>
    public static ImportRowDto Apply(ImportRowDto row, bool canView) => canView ? row : row with { Error = RedactCreditText(row.Error) };

    /// <summary>Aviso de crédito con cifras → <see cref="CreditRules.RedactedMessage"/>; cualquier otro texto queda igual.</summary>
    public static string? RedactCreditText(string? text) => CreditRules.IsCreditMessage(text) ? CreditRules.RedactedMessage : text;

    /// <summary>
    /// Comentario del historial de estatus de una orden: el del override de crédito ('Crédito excedido autorizado por {quién}
    /// (límite …, en curso …, esta orden …)') pierde el paréntesis de cifras; los demás comentarios quedan igual.
    /// </summary>
    public static string? RedactHistoryComment(string? comment, bool canView)
    {
        if (canView || comment is null || !comment.StartsWith(OrderStatusService.CreditOverrideCommentPrefix, StringComparison.Ordinal)) return comment;
        var at = comment.LastIndexOf(" (límite ", StringComparison.Ordinal);
        return at < 0 ? comment : comment[..at];
    }
}
