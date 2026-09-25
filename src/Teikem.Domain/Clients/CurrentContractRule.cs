namespace Teikem.Domain.Clients;

/// <summary>
/// Regla pura del "contrato vigente" de un cliente (decisión de Luis, Lote 2), compartida por ClientQueries.CurrentContractAsync
/// (versión EF), ClientService (lista) y ClientDataSource (fuente de análisis) para que las tres copias no diverjan:
/// - Contrato IsActive con estatus ACTIVE y StartDate &lt;= asOf. La fecha fin NO se evalúa: un contrato con EndDate pasada
///   sigue vigente hasta que alguien lo pase manualmente a EXPIRED o CANCELLED.
/// - Si no hay ACTIVE, el DRAFT más reciente por StartDate (desempate por ContractId mayor), para configurar tarifas antes de activar.
/// - Nunca EXPIRED/CANCELLED; null si no existe.
/// </summary>
public static class CurrentContractRule
{
    public static Contract? Pick(IEnumerable<Contract> contracts, Func<Contract, string?> statusCodeOf, DateOnly asOf)
    {
        var rows = contracts.Where(c => c.IsActive).ToList();
        var active = rows.Where(c => Is(statusCodeOf(c), Constants.ContractStatuses.Active) && c.StartDate <= asOf)
            .OrderByDescending(c => c.StartDate).ThenByDescending(c => c.ContractId).FirstOrDefault();
        if (active is not null) return active;
        return rows.Where(c => Is(statusCodeOf(c), Constants.ContractStatuses.Draft))
            .OrderByDescending(c => c.StartDate).ThenByDescending(c => c.ContractId).FirstOrDefault();
    }

    private static bool Is(string? code, string expected) => string.Equals(code?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
