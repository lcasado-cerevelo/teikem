namespace Teikem.Infrastructure.Exceptions;

/// <summary>Excepciones de dominio que el middleware del API traduce a ProblemDetails.</summary>
public abstract class TeikemException(string message, int statusCode, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IDictionary<string, string[]>? Errors { get; init; }
}

/// <summary>
/// 404 con el mensaje '{what} no encontrado.' (o '{what} '{key}' no encontrado.'). Con feminine = true concuerda en
/// femenino: 'Zona de despacho no encontrada.', 'Licencia no encontrada.' (Lote 4). Las llamadas existentes no cambian.
/// </summary>
public sealed class NotFoundException(string what, object? key = null, bool feminine = false)
    : TeikemException(BuildMessage(what, key, feminine), 404, "not_found")
{
    private static string BuildMessage(string what, object? key, bool feminine)
    {
        var suffix = feminine ? "no encontrada." : "no encontrado.";
        return key is null ? $"{what} {suffix}" : $"{what} '{key}' {suffix}";
    }
}

public sealed class ValidationException : TeikemException
{
    public ValidationException(string message) : base(message, 400, "validation") { }
    public ValidationException(string field, string message)
        : base(message, 400, "validation") { Errors = new Dictionary<string, string[]> { [field] = new[] { message } }; }
    public ValidationException(IDictionary<string, string[]> errors)
        : base("Datos inválidos.", 400, "validation") { Errors = errors; }
}

public sealed class ConflictException(string message) : TeikemException(message, 409, "conflict");

public sealed class ForbiddenException(string message = "No tiene permiso para esta acción.")
    : TeikemException(message, 403, "forbidden");

public sealed class UnauthorizedException(string message = "No autenticado.")
    : TeikemException(message, 401, "unauthorized");

/// <summary>El estatus actual no permite la acción (StatusCapability) o la transición es ilegal.</summary>
public sealed class StatusRuleException(string message) : TeikemException(message, 422, "status_rule");

/// <summary>El módulo requerido no está encendido para el tenant (TenantModule).</summary>
public sealed class ModuleDisabledException(string moduleKey)
    : TeikemException($"El módulo '{moduleKey}' no está habilitado para esta compañía.", 403, "module_disabled");

/// <summary>Acción sensible: exige reautenticación reciente (AAL2).</summary>
public sealed class StepUpRequiredException()
    : TeikemException("Esta acción requiere reautenticación reciente (AAL2).", 403, "aal2_required");
