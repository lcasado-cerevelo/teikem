using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>Lote 182 (C4): contador y patrón del número de la recepción maestra (por compañía, ClientId NULL).</summary>
public static class ReceiptGroupCodes
{
    public const string NumberKind = "RECEIPTGROUP";
    public const string NumberPattern = "RCM-#####";
}

/// <summary>
/// Lote 182 (C4) — recepción maestra: un solo camión (o un solo grupo de llegadas) que trae varios documentos (avisos de llegada y/o órdenes de compra) DEL MISMO DUEÑO y almacén. Cada documento conserva su
/// recibo de siempre (con sus líneas, tandas, estatus y acomodo): la maestra los reúne bajo un número (RCM-#####), con un solo transporte y referencia, y permite confirmarlos juntos. Se arma al abrir los
/// recibos (no se suman documentos después) y no hace nada nuevo con el inventario: cada recibo lo asienta como siempre. Mismo patrón que la recolección maestra (PickGroup, Lote 176).
/// </summary>
public class ReceiptGroup : ITenantScoped, ISoftDeletable
{
    public int ReceiptGroupId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    /// <summary>El dueño de lo que llega: el cliente 3PL de todos los avisos; NULL = lo propio de la compañía (órdenes de compra o avisos nacidos de una).</summary>
    public int? ClientId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    /// <summary>Lote 184: id de la recepción juntos que armó el aparato (cola): los recibos que llegan con la misma llave entran a la misma maestra; hace el reintento idempotente. NULL = maestra de la web.</summary>
    public Guid? ClientKey { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>Reglas puras y mensajes de la recepción maestra (manual 06 «Recepción maestra» y FAQ).</summary>
public static class ReceiptGroupRules
{
    public const int MinDocuments = 2;
    public const int MaxDocuments = 20;
    public const int MaxNoteLength = 500;

    public const string Label = "Recepción maestra";
    public const string DocumentsRequired = "Elija al menos dos documentos para recibir juntos; para uno solo use el recibo normal.";
    public const string TooManyDocuments = "Una recepción maestra admite como máximo 20 documentos.";
    public const string DocumentRequired = "Cada documento lleva un aviso de llegada o una orden de compra, uno solo.";
    public const string DocumentRepeated = "El mismo documento está repetido.";
    public const string NoteTooLong = "La nota no puede pasar de 500 caracteres.";
    public const string NumberTaken = "Ya existe una recepción maestra con ese número; intente de nuevo.";
    public const string AlreadyConfirmed = "Todos los recibos de esta recepción maestra ya están confirmados.";
    public const string DeleteHasConfirmed = "No se puede eliminar la recepción maestra: alguno de sus recibos ya está confirmado.";
    public const string Concurrency = "La recepción maestra cambió mientras se guardaba. Revise y vuelva a intentar.";

    public const string StatusOpen = "OPEN";
    public const string StatusConfirmed = "CONFIRMED";
    public const string StatusCancelled = "CANCELLED";

    public const string GroupNeedsDocument = "Recibir juntos es solo de documentos: indique un aviso de llegada o una orden de compra.";
    public const string GroupGone = "La recepción maestra ya se eliminó; abra una nueva.";
    public static string JoinOtherOwner(string number) => $"Este documento es de otro dueño que los de la recepción maestra {number}: reciba por separado lo de dueños distintos.";
    public static string JoinOtherWarehouse(string number) => $"Este documento es de otro almacén que los de la recepción maestra {number}.";
    public static string OtherOwner(string a, string b) => $"Los documentos deben ser del mismo dueño: «{a}» y «{b}» son de dueños distintos. Reciba por separado lo de dueños distintos.";
    public static string OtherWarehouse(string a, string b) => $"Los documentos deben ser del mismo almacén: «{a}» y «{b}» son de almacenes distintos.";

    /// <summary>
    /// Valida el conjunto elegido: entre 2 y 20, cada uno con un aviso de llegada O una orden de compra (uno solo) y ninguno repetido. Devuelve (campo, mensaje) o null.
    /// </summary>
    public static (string Field, string Message)? ValidateSelection(IReadOnlyList<(int? AsnId, Guid? PurchaseOrderPublicId)> docs)
    {
        if (docs.Count < MinDocuments) return ("documents", DocumentsRequired);
        if (docs.Count > MaxDocuments) return ("documents", TooManyDocuments);
        var seen = new HashSet<string>();
        for (var i = 0; i < docs.Count; i++)
        {
            var (asn, po) = docs[i];
            if ((asn is null) == (po is null)) return ($"documents[{i}]", DocumentRequired);
            if (!seen.Add(asn is int a ? $"A{a}" : $"P{po}")) return ($"documents[{i}]", DocumentRepeated);
        }
        return null;
    }

    /// <summary>Estatus derivado: CANCELLED si no está activa; CONFIRMED si todos sus recibos están confirmados; OPEN en otro caso.</summary>
    public static string Status(bool isActive, int memberCount, int confirmedCount)
        => !isActive ? StatusCancelled : memberCount > 0 && confirmedCount == memberCount ? StatusConfirmed : StatusOpen;
}
