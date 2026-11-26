namespace Teikem.Infrastructure.Tax;

/// <summary>Lote 248 — el documento que respalda una exención, tal como lo necesita <see cref="ClientExemptionBacking.Effective"/>.</summary>
public sealed record ExemptionBackingDocument(bool IsActive, DateOnly? ValidFrom, DateOnly? ValidTo, string? DocumentNumber);

/// <summary>
/// Lote 248 — la vigencia EFECTIVA de una exención. Applies = false: la exención no exime nada en ninguna fecha (su documento se quitó o no existe).
/// </summary>
public readonly record struct ExemptionBackingResult(bool Applies, DateOnly From, DateOnly? To, string Certificate);

/// <summary>
/// Lote 248 — LA ÚNICA función que decide la vigencia efectiva de una exención del cliente (la usan el cálculo, la lista de exenciones y el aviso al vender):
/// - Vieja sin documento (<c>clientDocumentId</c> NULL): igual que siempre — sus propias fechas y su certificado, incluso con «hasta» NULL (los montos no cambian).
/// - Vinculada a un documento que no existe o está quitado: NO aplica.
/// - Vinculada a un documento activo: desde = «desde» del documento (o el de la exención si el documento no tiene); hasta = «hasta» del documento (si el documento
///   no tuviera, la copia de la exención: nunca queda abierta por un hueco del documento); certificado = número del documento (o la copia de la exención).
/// </summary>
public static class ClientExemptionBacking
{
    public static ExemptionBackingResult Effective(int? clientDocumentId, DateOnly exValidFrom, DateOnly? exValidTo, string exCertificate, ExemptionBackingDocument? doc)
    {
        if (clientDocumentId is null) return new ExemptionBackingResult(true, exValidFrom, exValidTo, exCertificate);
        if (doc is null || !doc.IsActive) return new ExemptionBackingResult(false, exValidFrom, exValidTo, exCertificate);
        var certificate = string.IsNullOrWhiteSpace(doc.DocumentNumber) ? exCertificate : doc.DocumentNumber.Trim();
        return new ExemptionBackingResult(true, doc.ValidFrom ?? exValidFrom, doc.ValidTo ?? exValidTo, certificate);
    }
}
