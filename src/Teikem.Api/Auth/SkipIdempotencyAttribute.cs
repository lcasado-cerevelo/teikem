namespace Teikem.Api.Auth;

/// <summary>
/// [SkipIdempotency]: el endpoint (o todos los de un controlador) NO usa la clave de idempotencia (Idempotency-Key). Es para las respuestas que
/// traen credenciales en claro (tokens, secretos de aparato, enlaces de invitación, PIN, contraseñas temporales): el middleware guarda la respuesta
/// para repetirla y no debe guardar eso. Antes era una lista fija de rutas en IdempotencyRules; ahora cada endpoint lo DECLARA y la prueba
/// IdempotencyOptOutTests fija el conjunto exacto de endpoints marcados (agregar uno nuevo sin pensarlo no pasa).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipIdempotencyAttribute(string reason) : Attribute
{
    /// <summary>Por qué se excluye (documentación viva; no se usa en tiempo de ejecución).</summary>
    public string Reason { get; } = reason;
}
