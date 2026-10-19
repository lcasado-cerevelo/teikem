using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 135 (P11) — autenticación de la API de los clientes: <c>POST /api/v1/api-auth/token</c> (llave tk_… + secreto → access token de 15 minutos con los alcances de la integración) y
/// <c>GET /api/v1/api-auth/me</c> (lo que la llave puede saber de sí misma). El token se usa como «Authorization: Bearer …» en las rutas de sus alcances (ver el capítulo 39 del manual).
/// </summary>
[ApiController]
[Route("api/v1/api-auth")]
public sealed class ApiAuthController(ApiKeyService keys, TenantContext tenant) : ControllerBase
{
    /// <summary>Intercambia la llave y su secreto por un access token. 401 «Credenciales inválidas.» (sin decir cuál falló) · 429 si se pasó del límite de uso de la llave o de intentos desde esa dirección.</summary>
    [HttpPost("token"), AllowAnonymous]
    public async Task<ApiTokenDto> Token([FromBody] ApiTokenRequest req, CancellationToken ct)
    {
        // freno de intentos por dirección (adivinar llaves): 30 por minuto; el límite propio de cada llave se aplica dentro de IssueTokenAsync
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var gate = keys.Limiter.TryAcquire(-Math.Abs(ip.GetHashCode() % int.MaxValue) - 1, 30);
        if (!gate.Allowed) throw new RateLimitedException(ApiKeyRules.RateLimitedMessage(gate.RetryAfterSeconds), gate.RetryAfterSeconds);
        var (token, _) = await keys.IssueTokenAsync(req, ct);
        return token;
    }

    /// <summary>La llave: integración, alcances, cliente al que está limitada, límite de uso y vencimiento.</summary>
    [HttpGet("me"), Authorize]
    public async Task<ApiInfoDto> Me(CancellationToken ct)
    {
        if (!tenant.IsApiKey) throw new ForbiddenException("Esta ruta es para llaves de API.");
        return await keys.InfoAsync(User.FindFirst(TeikemClaims.ApiKeyId)?.Value ?? string.Empty, ct);
    }
}
