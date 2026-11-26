using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 249 (Lote E) — la ORDEN INTERNA DE ADICIÓN: lo que se le añade a una orden que ya no admite cambios (facturada, empacada sin reabrir, ya salida) viaja en
/// una orden normal con ParentSalesOrderId (cuelga de la RAÍZ: una adición de una adición cuelga de la original). Nace con el alta de siempre (CreateCoreAsync, en
/// borrador) y se solicita con el RequestAsync de siempre: un solo camino a REQUESTED (reserva, crédito, aviso «Orden solicitada»). No pide la firma del cliente
/// (la autorización queda en la modificación ADDITION de la madre) y copia de la madre el cliente, el almacén, el consignatario, la entrega, la forma de pago y la
/// consignación; el vendedor y la ruta los copia FreezeSellerAsync.
/// </summary>
public sealed partial class SalesOrderService
{
    /// <summary>422: la madre no está en curso (solo se añade a una orden solicitada, parcial o despachada).</summary>
    public const string AdditionParentNotOpen = "Solo se le puede añadir a una orden solicitada, parcial o despachada.";

    /// <summary>Las notas de la adición: «Adición de OV-…» y, si vienen, las del usuario.</summary>
    public static string AdditionNotes(string rootNumber, string? notes)
    {
        var prefix = $"Adición de {rootNumber}";
        var extra = notes?.Trim();
        var text = string.IsNullOrEmpty(extra) ? prefix : $"{prefix} — {extra}";
        return text.Length <= SalesRules.MaxNotesLength ? text : text[..SalesRules.MaxNotesLength];
    }

    /// <summary>
    /// Crea y solicita la adición de <paramref name="parent"/> y devuelve su PublicId. Corre en la transacción que abrió el servicio de modificaciones (que ya aseguró
    /// el contador OV fuera de ella): el alta y la solicitud se unen a esa transacción; un faltante o un crédito excedido revierten todo (no quedan borradores).
    /// </summary>
    public async Task<Guid> CreateAdditionCoreAsync(SalesOrder parent, IReadOnlyList<SalesOrderLineRequest> lines, string? notes, bool allowBackorder, bool overrideCredit,
        CancellationToken ct, string? comment = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var map = await StatusMapAsync(ct);
        var parentCode = map.TryGetValue(parent.StatusCodeId, out var st) ? st.Code : string.Empty;
        if (parentCode is not (SalesOrderStatuses.Requested or SalesOrderStatuses.Partial or SalesOrderStatuses.Shipped))
            throw new StatusRuleException(AdditionParentNotOpen);
        if (lines is null || lines.Count == 0) throw new ValidationException("lines", SalesRules.LinesRequired);

        var rootId = parent.ParentSalesOrderId ?? parent.SalesOrderId;
        var root = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.SalesOrderId == rootId)
            .Select(o => new { o.SalesOrderId, o.Number }).FirstOrDefaultAsync(ct) ?? throw NotFound();
        var clientPublicId = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == parent.ClientId).Select(c => (Guid?)c.PublicId).FirstOrDefaultAsync(ct);
        var warehousePublicId = await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == parent.WarehouseId).Select(w => (Guid?)w.PublicId).FirstOrDefaultAsync(ct);
        var consigneePublicId = parent.ConsigneeId is int cid
            ? await db.Set<Location>().AsNoTracking().Where(l => l.LocationId == cid && l.IsActive).Select(l => (Guid?)l.PublicId).FirstOrDefaultAsync(ct)
            : null;
        var method = parent.PaymentMethodLookupId is int m ? (await lookups.GetAsync(m, ct))?.InternalCode : null;
        var term = parent.PaymentTermLookupId is int t ? (await lookups.GetAsync(t, ct))?.InternalCode : null;

        // "" = sin forma de pago / sin término (si no, el alta los prellenaría con los del cliente y no serían los de la madre)
        var req = new SalesOrderRequest(warehousePublicId, clientPublicId, consigneePublicId, null, parent.RequiredDate, null, AdditionNotes(root.Number, notes), lines,
            DeliveryMode: parent.DeliveryMode ?? string.Empty, PaymentMethod: method ?? string.Empty, PaymentTerm: term ?? string.Empty, IsConsignment: parent.IsConsignment);
        var (id, _, _, _) = await CreateCoreAsync(req, new SalesOrderCreateOptions(root.SalesOrderId), ct);
        await RequestAsync(id, new SalesOrderRequestRequest(allowBackorder, overrideCredit, comment), ct);
        return id;
    }
}
