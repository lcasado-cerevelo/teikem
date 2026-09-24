namespace Teikem.Infrastructure.Exceptions;

/// <summary>Excepciones de dominio que el middleware del API traduce a ProblemDetails.</summary>
public abstract class TeikemException(string message, int statusCode, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IDictionary<string, string[]>? Errors { get; init; }
}

public sealed class NotFoundException(string what, object? key = null)
    : TeikemException(key is null ? $"{what} no encontrado." : $"{what} '{key}' no encontrado.", 404, "not_found");

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
