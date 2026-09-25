using Teikem.Domain.Common;

namespace Teikem.Domain.Orders;

/// <summary>
/// Contador atómico de numeración (Lote 3, DECISIÓN 3): una fila por (Tenant, Kind, Cliente); ORDER/INVOICE/PACKAGE por
/// cliente y PACKBATCH por tenant (ClientId NULL). NextValue es el siguiente consecutivo a entregar; se consume con
/// UPDATE … OUTPUT dentro de la transacción del alta (INumberSequenceService). Contador técnico: no se audita.
/// </summary>
public class NumberSequence : ITenantScoped
{
    public int NumberSequenceId { get; set; }
    public int TenantId { get; set; }
    /// <summary>NumberKinds: ORDER, INVOICE, PACKAGE, PACKBATCH.</summary>
    public string Kind { get; set; } = string.Empty;
    public int? ClientId { get; set; }
    public long NextValue { get; set; } = 1;
}
