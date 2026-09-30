using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 13 — reglas puras del ciclo de estatus del recibo (sin BD). El servicio guarda los seis estatus como StatusCode y
/// los recorre SIEMPRE con StatusService.TransitionAsync; aquí solo se decide el destino y el camino:
/// - Abiertos (EXPECTED, RECEIVING, DISCREPANCY): los sincroniza el servicio con cada cambio de líneas (OpenTarget).
/// - Confirmados (RECEIVED, RECEIVED_VARIANCE): destino de la confirmación (ConfirmedTarget).
/// - PUTAWAY: terminal, al cerrar el último acomodo (o directo si no hubo nada que acomodar).
/// 'enabled' son los códigos habilitados para la compañía (StatusService.GetPipelineAsync(dominio, false)); un estatus
/// apagado se salta con el respaldo de cada regla.
/// </summary>
public static class ReceiptStatusRules
{
    /// <summary>Fases de la lista de recibos (filtro 'phase').</summary>
    public const string PhaseOpen = "OPEN";
    public const string PhasePendingPutaway = "PENDING_PUTAWAY";
    public const string PhaseDone = "DONE";

    /// <summary>Filtro 'variance' de la lista: faltante, sobrante o sin diferencia.</summary>
    public const string VarianceShort = "SHORT";
    public const string VarianceOver = "OVER";
    public const string VarianceNone = "NONE";

    public static string UnknownPhase(string phase) => $"Fase desconocida: '{phase}'. Use OPEN, PENDING_PUTAWAY o DONE.";
    public static string UnknownVariance(string variance) => $"Diferencia desconocida: '{variance}'. Use SHORT, OVER o NONE.";

    private static string Code(string? code) => (code ?? "").Trim().ToUpperInvariant();

    /// <summary>¿El recibo está abierto (se edita, se confirma y se borra)? EXPECTED, RECEIVING o DISCREPANCY.</summary>
    public static bool IsOpen(string? code) => Code(code) is ReceiptStatuses.Expected or ReceiptStatuses.Receiving or ReceiptStatuses.Discrepancy;

    /// <summary>¿El recibo está confirmado y con acomodo por cerrar? RECEIVED o RECEIVED_VARIANCE.</summary>
    public static bool IsConfirmed(string? code) => Code(code) is ReceiptStatuses.Received or ReceiptStatuses.ReceivedWithVariance;

    /// <summary>¿Ya está en el Kárdex? Confirmado o PUTAWAY.</summary>
    public static bool IsPosted(string? code) => IsConfirmed(code) || Code(code) == ReceiptStatuses.Putaway;

    /// <summary>
    /// Estatus abierto que corresponde al recibo según sus líneas:
    /// - sin líneas: se queda en EXPECTED si ya estaba; si no, RECEIVING (un recibo nunca vuelve a Esperado);
    /// - RECEIVING apagado por la compañía: EXPECTED (sin él no hay camino a DISCREPANCY);
    /// - con diferencia y DISCREPANCY encendido: DISCREPANCY; si no, RECEIVING.
    /// </summary>
    public static string OpenTarget(string current, int lineCount, bool hasVariance, IReadOnlyCollection<string> enabled)
    {
        var cur = Code(current);
        var receivingOn = enabled.Contains(ReceiptStatuses.Receiving);
        if (lineCount == 0)
            return cur == ReceiptStatuses.Expected || !receivingOn ? ReceiptStatuses.Expected : ReceiptStatuses.Receiving;
        if (!receivingOn) return ReceiptStatuses.Expected;
        return hasVariance && enabled.Contains(ReceiptStatuses.Discrepancy) ? ReceiptStatuses.Discrepancy : ReceiptStatuses.Receiving;
    }

    /// <summary>Destino de la confirmación: RECEIVED_VARIANCE si hay diferencia y está encendido; si no, RECEIVED.</summary>
    public static string ConfirmedTarget(bool hasVariance, IReadOnlyCollection<string> enabled)
        => hasVariance && enabled.Contains(ReceiptStatuses.ReceivedWithVariance) ? ReceiptStatuses.ReceivedWithVariance : ReceiptStatuses.Received;

    /// <summary>
    /// Camino escalonado (una transición por paso, con historial) del estatus actual al destino. Desde EXPECTED, los
    /// laterales DISCREPANCY y RECEIVED_VARIANCE pasan antes por RECEIVING (sus entradas laterales sembradas salen de
    /// RECEIVING); si RECEIVING está apagado se va directo. Igual estatus → camino vacío.
    /// </summary>
    public static IReadOnlyList<string> Path(string current, string target, IReadOnlyCollection<string> enabled)
    {
        var cur = Code(current);
        var to = Code(target);
        if (cur == to) return Array.Empty<string>();
        if (cur == ReceiptStatuses.Expected && (to is ReceiptStatuses.Discrepancy or ReceiptStatuses.ReceivedWithVariance)
            && enabled.Contains(ReceiptStatuses.Receiving))
            return new[] { ReceiptStatuses.Receiving, to };
        return new[] { to };
    }

    /// <summary>Códigos de estatus de una fase (OPEN, PENDING_PUTAWAY, DONE) o el error 400.</summary>
    public static (IReadOnlyList<string> Codes, string? Error) PhaseCodes(string? phase)
    {
        var p = Code(phase);
        return p switch
        {
            PhaseOpen => (ReceiptStatuses.OpenCodes, null),
            PhasePendingPutaway => (ReceiptStatuses.ConfirmedCodes, null),
            PhaseDone => (new[] { ReceiptStatuses.Putaway }, null),
            _ => (Array.Empty<string>(), UnknownPhase(phase?.Trim() ?? "")),
        };
    }

    /// <summary>Normaliza el filtro 'variance' (SHORT, OVER, NONE; sin distinguir mayúsculas; vacíos se ignoran) o el error 400.</summary>
    public static (IReadOnlyList<string> Codes, string? Error) ParseVariance(IEnumerable<string?>? values)
    {
        var result = new List<string>();
        if (values is null) return (result, null);
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var v = Code(raw);
            if (v is not (VarianceShort or VarianceOver or VarianceNone)) return (Array.Empty<string>(), UnknownVariance(raw.Trim()));
            if (!result.Contains(v)) result.Add(v);
        }
        return (result, null);
    }
}
