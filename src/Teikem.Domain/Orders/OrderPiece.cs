using System.Globalization;
using Teikem.Domain.Common;

namespace Teikem.Domain.Orders;

/// <summary>
/// Lote 109 — una PIEZA (paquete) de una orden con su propio número único (dictado por Luis, 2026-10-18; reemplaza el supuesto del Lote 44 de repetir el número de la línea en cada pieza). La orden tiene su número único
/// (el conduce, que usan el cliente y el consignatario); cada pieza tiene el suyo, que es el del label y el que se escanea. Se crean al someter la orden: una línea de 3 piezas crea 3 piezas. Se dan de baja (IsActive = 0)
/// cuando la orden se edita en Sometida y se reemplazan sus paquetes.
/// </summary>
public class OrderPiece : ITenantScoped
{
    public int OrderPieceId { get; set; }
    public int TenantId { get; set; }
    public int TransportOrderId { get; set; }
    public int CargoLineId { get; set; }
    /// <summary>Posición de la pieza dentro de la orden (1..N, corrida sobre todas sus líneas).</summary>
    public int Sequence { get; set; }
    /// <summary>El número del label (código de barras): único dentro de la compañía mientras la pieza esté activa.</summary>
    public string PackageNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>Lote 110: null = normal; TO_VERIFY = faltante «Por verificar» (no se escaneó en un estatus por el que pasó la orden; las reclamaciones lo continúan en el Lote 111).</summary>
    public string? MissingStatus { get; set; }
    public DateTime? MissingAtUtc { get; set; }
    /// <summary>Lote 111: la reclamación a la que pertenece (mientras esté Reclamada).</summary>
    public int? ClaimId { get; set; }
    /// <summary>Lote 111: apareció estando perdida o reclamada: se escaneó con esta acción y espera que se cierre el conteo para pasar a una orden nueva.</summary>
    public DateTime? ReappearedAtUtc { get; set; }
    public string? ReappearedAction { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public TransportOrder? Order { get; set; }
    public CargoLine? Line { get; set; }
}

/// <summary>
/// Cómo se numera una pieza. <b>Formato del sistema</b> (el que vale cuando el cliente no tiene uno configurado; decidido por Luis): <c>{Code del cliente}{Code del consignatario}{aammdd}{hhmmssfff}{n}</c> —el Code del cliente, el Code del
/// consignatario (si no tiene, su ID), la fecha abreviada, la hora con milisegundos y el número de la pieza dentro de la orden— SIN separadores que agreguemos (los Codes se toman tal cual vengan, con rayas si las traen). Cada Code
/// tiene como máximo <see cref="CodeMax"/> caracteres (un código, no un título); si uno ya guardado es más largo se usan sus primeros 10. Con 10 + 10 + 6 + 9 + 3 son 38 caracteres como máximo: cabe en el campo (40) y en un
/// Code 128 de 4 pulgadas (unos 8 mil de módulo). Una línea con número tecleado por el cliente numera sus piezas con ese número (y el número de la pieza si tiene más de una).
/// </summary>
public static class PieceNumbering
{
    public const int MaxLength = 40;
    /// <summary>Largo máximo del Code de un cliente o de un consignatario.</summary>
    public const int CodeMax = 10;

    /// <summary>La parte del número que aporta un Code: tal cual (hasta 10 caracteres); sin Code, el ID entero.</summary>
    public static string Part(string? code, int id)
    {
        var c = code?.Trim();
        if (string.IsNullOrEmpty(c)) return id.ToString(CultureInfo.InvariantCulture);
        return c.Length > CodeMax ? c[..CodeMax] : c;
    }

    /// <summary>El número de la pieza con el formato del sistema (la fecha y la hora son las de la compañía).</summary>
    public static string ForSystem(string clientPart, string consigneePart, DateTime localNow, int sequence)
        => string.Concat(clientPart, consigneePart, localNow.ToString("yyMMdd", CultureInfo.InvariantCulture), localNow.ToString("HHmmssfff", CultureInfo.InvariantCulture),
            sequence.ToString(CultureInfo.InvariantCulture));

    /// <summary>Número de una pieza de una línea con número tecleado: el mismo si la línea tiene una sola pieza; con más, el tecleado más el número de la pieza (1 la primera).</summary>
    public static string FromTyped(string typed, int index, int count) => count <= 1 ? typed : string.Concat(typed, index.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Estados de un faltante: «Por verificar» (Lote 110), «Perdido», «Reclamada» y «Resuelta» (Lote 111).</summary>
public static class PieceMissing
{
    public const string ToVerify = "TO_VERIFY";
    public const string Lost = "LOST";
    public const string Claimed = "CLAIMED";
    public const string Resolved = "RESOLVED";
}

/// <summary>
/// Lote 110 — el escaneo de una pieza con una acción (PICKUP, ARRIVE, ROUTE o DEPART). Una fila por pieza y acción (el mismo escaneo repetido es un duplicado). De ahí sale el conteo «4 de 5» y cuándo una orden queda incompleta.
/// </summary>
public class OrderPieceScan : ITenantScoped
{
    public long OrderPieceScanId { get; set; }
    public int TenantId { get; set; }
    public int OrderPieceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ScannedBy { get; set; }

    public OrderPiece? Piece { get; set; }
}
