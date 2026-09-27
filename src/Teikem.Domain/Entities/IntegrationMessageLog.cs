using Teikem.Domain.Common;

namespace Teikem.Domain.Entities;

/// <summary>
/// Bitácora de mensajes de integración (módulo 13). Lote 8A: también guarda la idempotencia del API (cabecera
/// Idempotency-Key): clave lógica (TenantId, UserId, IdempotencyKey) con índice único filtrado UX_IntegrationLog_Idem;
/// RequestHash = SHA-256 (hex) de método, ruta y cuerpo; ResponseCode/ResponseJson = respuesta guardada para repetirla
/// (null mientras la operación está en vuelo). Nunca guarda el cuerpo de la petición de la idempotencia (puede traer PIN o
/// contraseñas). Registros técnicos: se borran pasados 7 días (no llevan historial).
/// </summary>
public class IntegrationMessageLog : ITenantScoped
{
    public long IntegrationMessageLogId { get; set; }
    public int TenantId { get; set; }
    public int? ApiCredentialId { get; set; }
    /// <summary>LookupCode Entity='MessageDirection' (INBOUND para la idempotencia del API).</summary>
    public int DirectionLookupId { get; set; }
    public string? Endpoint { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestJson { get; set; }
    public int? ResponseCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    // --- Lote 8A: idempotencia del API ---
    public int? UserId { get; set; }
    public string? RequestHash { get; set; }
    public string? ResponseJson { get; set; }
    public string? Method { get; set; }
}
