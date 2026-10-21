namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 176 — recolección maestra (varios documentos del MISMO cliente y almacén que se recolectan juntos y salen en UN envío). Reglas puras y mensajes (manual 06 y FAQ).
/// Decidido por el dueño: se arma al recolectar (no se suman documentos después); un solo cliente y un solo destino; para otro destino, otra recolección.
/// </summary>
public static class PickGroupRules
{
    public const int MinDocuments = 2;
    public const int MaxDocuments = 20;

    public const string KindSalesOrder = "SALES_ORDER";
    public const string KindExitNotice = "EXIT_NOTICE";

    public const string Label = "Recolección maestra";
    public const string DocumentsRequired = "Elija al menos dos documentos para recolectar juntos; para uno solo use el despacho normal.";
    public const string TooManyDocuments = "Una recolección maestra admite como máximo 20 documentos.";
    public const string DocumentRequired = "Indique el documento (tipo y id).";
    public const string DocumentRepeated = "El mismo documento está repetido.";
    public const string NumberTaken = "Ya existe una recolección con ese número; intente de nuevo.";
    public const string OrderTaken = "El envío ya está ligado a otra recolección.";

    public static string UnknownKind(string kind) => $"Tipo de documento desconocido: «{kind}». Use SALES_ORDER o EXIT_NOTICE.";
    public static string OtherClient(string a, string b) => $"Los documentos deben ser del mismo cliente: «{a}» y «{b}» son de clientes distintos. Haga recolecciones separadas.";
    public static string OtherWarehouse(string a, string b) => $"Los documentos deben ser del mismo almacén: «{a}» y «{b}» son de almacenes distintos.";
    public static string NothingReserved(string number) => $"«{number}» no tiene nada reservado por sacar.";
    public static string NotPackable(string number) => $"La recolección {number} ya está empacada o eliminada.";
    public static string InGroup(string batch, string group) => $"La recolección {batch} pertenece a la recolección maestra {group}: empáquela o elimínela desde la maestra.";
    public static string DeleteBlocked(string number, string orderStatus) => $"La recolección {number} no se puede eliminar: su envío ya está en «{orderStatus}».";

    /// <summary>Tipo normalizado (mayúsculas) o null si no es uno de los dos.</summary>
    public static string? NormalizeKind(string? kind)
    {
        var k = kind?.Trim().ToUpperInvariant();
        return k is KindSalesOrder or KindExitNotice ? k : null;
    }

    /// <summary>
    /// Valida el conjunto de documentos elegidos (tipo, id): entre 2 y 20 y ninguno repetido. Devuelve el error (campo, mensaje) o null.
    /// </summary>
    public static (string Field, string Message)? ValidateSelection(IReadOnlyList<(string? Kind, Guid? PublicId)> docs)
    {
        if (docs.Count < MinDocuments) return ("documents", DocumentsRequired);
        if (docs.Count > MaxDocuments) return ("documents", TooManyDocuments);
        var seen = new HashSet<(string, Guid)>();
        for (var i = 0; i < docs.Count; i++)
        {
            var k = NormalizeKind(docs[i].Kind);
            if (docs[i].Kind is null || docs[i].PublicId is null) return ($"documents[{i}]", DocumentRequired);
            if (k is null) return ($"documents[{i}].kind", UnknownKind(docs[i].Kind!));
            if (!seen.Add((k, docs[i].PublicId!.Value))) return ($"documents[{i}]", DocumentRepeated);
        }
        return null;
    }

    /// <summary>Estatus de la maestra, derivado de sus recolecciones activas: PACKED si tiene envío, CANCELLED si no está activa, COLLECTED en otro caso.</summary>
    public static string Status(bool isActive, int? transportOrderId)
        => !isActive ? "CANCELLED" : transportOrderId is null ? "COLLECTED" : "PACKED";
}
