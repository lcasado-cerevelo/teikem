using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Api.Auth;

/// <summary>
/// Lote 8A — limitador de intentos de los endpoints anónimos del aparato de almacén (limitador de .NET 8, ventana fija de un
/// minuto por dirección IP y por ruta; sin cola). Complementa el bloqueo del PIN (5 fallos → 15 minutos, PinService):
/// <list type="bullet">
/// <item><see cref="Enroll"/>: POST /api/v1/devices/enroll (el código de registro de 8 caracteres es lo único que se puede
/// adivinar sin credencial): 10 por minuto por IP (RateLimiting:DeviceEnrollPerMinute).</item>
/// <item><see cref="DeviceAuth"/>: POST /api/v1/auth/device/users, /auth/device/login y /devices/heartbeat: 60 por minuto por
/// IP y ruta (RateLimiting:DeviceAuthPerMinute). Más holgado porque los aparatos de un almacén suelen salir por la misma IP
/// (NAT) y el heartbeat es periódico.</item>
/// </list>
/// Rechazo → 429 ProblemDetails 'Demasiados intentos; espere un minuto e intente de nuevo.' (code rate_limited) con cabecera
/// Retry-After. Detrás de un proxy inverso, la IP es la del proxy salvo que se configure UseForwardedHeaders (decisión a
/// revisar en docs/lote8A-decisiones.md). El login con contraseña conserva su defensa propia (lockout de Identity por cuenta).
/// </summary>
public static class DeviceRateLimits
{
    public const string Enroll = "device-enroll";
    public const string DeviceAuth = "device-auth";
    public const int DefaultEnrollPerMinute = 10;
    public const int DefaultDeviceAuthPerMinute = 60;
    public const string RejectedMessage = "Demasiados intentos; espere un minuto e intente de nuevo.";

    public static IServiceCollection AddDeviceRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var enroll = Positive(config.GetValue<int?>("RateLimiting:DeviceEnrollPerMinute"), DefaultEnrollPerMinute);
        var auth = Positive(config.GetValue<int?>("RateLimiting:DeviceAuthPerMinute"), DefaultDeviceAuthPerMinute);
        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Enroll, ctx => PerIpAndPath(ctx, enroll));
            o.AddPolicy(DeviceAuth, ctx => PerIpAndPath(ctx, auth));
            o.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.Headers.RetryAfter = "60";
                var correlation = http.RequestServices.GetService<ITenantContext>()?.CorrelationId ?? Guid.Empty;
                await http.Response.WriteAsJsonAsync(new
                {
                    type = "https://teikem.app/errors/rate_limited", title = RejectedMessage, status = 429, code = "rate_limited",
                    correlationId = correlation,
                }, ct);
            };
        });
    }

    private static RateLimitPartition<string> PerIpAndPath(HttpContext ctx, int permits)
        => RateLimitPartition.GetFixedWindowLimiter(
            // Ruta normalizada (minúsculas, sin barra final): '/Enroll/' y '/enroll' comparten el mismo cupo.
            $"{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}|{ctx.Request.Path.Value?.TrimEnd('/').ToLowerInvariant()}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    private static int Positive(int? value, int fallback) => value is int v && v > 0 ? v : fallback;
}
