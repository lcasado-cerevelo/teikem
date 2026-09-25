namespace Teikem.Infrastructure.Orders;

/// <summary>
/// Contador atómico de numeración sobre dbo.NumberSequence (Lote 3, DECISIÓN 3): ORDER/INVOICE/PACKAGE por cliente,
/// PACKBATCH por tenant (ClientId null). El TenantId sale siempre del principal.
/// Protocolo: EnsureAsync en autocommit ANTES de la transacción (crea la fila si falta, idempotente) para que dentro de
/// ella solo corra el UPDATE; NextAsync dentro de la transacción ambiente del llamador (bloqueo de fila hasta el commit:
/// serializa altas concurrentes y un rollback devuelve el número, sin huecos) o en autocommit si se llama fuera (se usa
/// para "saltar" un valor chocado con un número tecleado). Los números se dibujan siempre en NumberingRules.DrawOrder.
/// </summary>
public interface INumberSequenceService
{
    /// <summary>Asegura la fila del contador (idempotente; ignora la carrera de UQ_NumberSequence). Llamar en autocommit, antes de abrir la transacción.</summary>
    Task EnsureAsync(string kind, int? clientId, CancellationToken ct);

    /// <summary>
    /// Consume y devuelve el siguiente valor (UPDATE … OUTPUT deleted.NextValue). Sin fila ⇒ InvalidOperationException
    /// (error de programación: falta EnsureAsync), nunca de usuario.
    /// </summary>
    Task<long> NextAsync(string kind, int? clientId, CancellationToken ct);
}
