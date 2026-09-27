using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 6 (D37) — inventario insuficiente: 409 con code 'insufficient_stock'. El mensaje es el primer faltante
/// (InventoryRules.InsufficientStockMessage) y Errors lleva uno por asiento ('postings[i]'). La operación completa se revierte
/// (incluido el número EMP de una recolección). También traduce la violación 547 de CK_StockBalance_Qty (última línea en SQL).
/// </summary>
public sealed class InsufficientStockException : TeikemException
{
    public const string ErrorCode = "insufficient_stock";

    public InsufficientStockException(string message, IDictionary<string, string[]>? errors = null) : base(message, 409, ErrorCode)
    {
        Errors = errors ?? new Dictionary<string, string[]> { ["quantity"] = new[] { message } };
    }
}
