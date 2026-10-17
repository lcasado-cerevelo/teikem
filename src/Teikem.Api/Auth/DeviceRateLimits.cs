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
    /// <summary>2026-10-07: POST /auth/forgot-password y /auth/reset-password: 5 por minuto por IP y ruta (RateLimiting:PasswordResetPerMinute).</summary>
    public const string PasswordReset = "password-reset";
    /// <summary>
    /// Lote 100: POST /auth/login y /auth/mfa/verify: 60 por minuto por dirección IP (todas las rutas juntas; RateLimiting:LoginPerMinute). Frena a quien prueba muchas cuentas con la misma
    /// contraseña (el bloqueo por cuenta frena a quien prueba muchas contraseñas en una cuenta). Holgado a propósito: una oficina entera suele salir por la misma IP.
    /// </summary>
    public const string Login = "login";
    public const int DefaultLoginPerMinute = 60;
    /// <summary>Lote 103: GET /public/track/{slug}/{waybill} (anónimo, «¿Cómo va mi orden?»): 20 por minuto por IP (RateLimiting:TrackPerMinute); el conduce es consecutivo, así que el tope frena a quien recorra todos los números.</summary>
    public const string Track = "public-track";
    public const int DefaultTrackPerMinute = 20;
    public const int DefaultPasswordResetPerMinute = 5;
    public const int DefaultEnrollPerMinute = 10;
    public const int DefaultDeviceAuthPerMinute = 60;
    public const string RejectedMessage = "Demasiados intentos; espere un minuto e intente de nuevo.";

    public static IServiceCollection AddDeviceRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var enroll = Positive(config.GetValue<int?>("RateLimiting:DeviceEnrollPerMinute"), DefaultEnrollPerMinute);
        var auth = Positive(config.GetValue<int?>("RateLimiting:DeviceAuthPerMinute"), DefaultDeviceAuthPerMinute);
        var reset = Positive(config.GetValue<int?>("RateLimiting:PasswordResetPerMinute"), DefaultPasswordResetPerMinute);
        var login = Positive(config.GetValue<int?>("RateLimiting:LoginPerMinute"), DefaultLoginPerMinute);
        var track = Positive(config.GetValue<int?>("RateLimiting:TrackPerMinute"), DefaultTrackPerMinute);
        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Enroll, ctx => PerIpAndPath(ctx, enroll));
            o.AddPolicy(DeviceAuth, ctx => PerIpAndPath(ctx, auth));
            o.AddPolicy(PasswordReset, ctx => PerIpAndPath(ctx, reset));
            o.AddPolicy(Login, ctx => PerIp(ctx, login));
            o.AddPolicy(Track, ctx => PerIp(ctx, track));
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

    private static RateLimitPartition<string> PerIp(HttpContext ctx, int permits)
        => RateLimitPartition.GetFixedWindowLimiter(
            $"login|{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    private static RateLimitPartition<string> PerIpAndPath(HttpContext ctx, int permits)
        => RateLimitPartition.GetFixedWindowLimiter(
            // Ruta normalizada (minúsculas, sin barra final): '/Enroll/' y '/enroll' comparten el mismo cupo.
            $"{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}|{ctx.Request.Path.Value?.TrimEnd('/').ToLowerInvariant()}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    private static int Positive(int? value, int fallback) => value is int v && v > 0 ? v : fallback;
}
