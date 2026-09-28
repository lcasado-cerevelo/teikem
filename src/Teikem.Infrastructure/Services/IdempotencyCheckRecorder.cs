using Teikem.Domain.Security;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 8A — anota (por petición) las comprobaciones de módulo y permiso que una operación con Idempotency-Key hace DENTRO
/// del servicio o del controlador (p. ej. purchasing.receive y PURCHASING al recibir contra una OC, orders.create al
/// recolectar y empacar, warehouse.count para el conteo a ciegas). El middleware de idempotencia las guarda con la respuesta
/// y las vuelve a evaluar antes de repetirla, porque la repetición no pasa por el servicio ni por el controlador.
/// Solo anota entre <see cref="Start"/> y <see cref="Stop"/> (las comprobaciones de la autorización ya se repiten solas).
/// </summary>
public sealed class IdempotencyCheckRecorder
{
    private readonly List<IdempotencyCheck> checks = new();

    public bool IsRecording { get; private set; }

    public void Start()
    {
        checks.Clear();
        IsRecording = true;
    }

    public IReadOnlyList<IdempotencyCheck> Stop()
    {
        IsRecording = false;
        return checks.ToList();
    }

    /// <summary>Anota la primera evaluación de cada (tipo, código).</summary>
    public void Record(string kind, string code, bool result)
    {
        if (!IsRecording) return;
        if (checks.Any(c => c.Kind == kind && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase))) return;
        checks.Add(new IdempotencyCheck(kind, code, result));
    }
}
