using System.Reflection;
using Teikem.Infrastructure.Contracts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 8A, decisión 23: los campos de entrada del aparato y del PIN son anulables a propósito. Con &lt;Nullable&gt;enable&lt;/Nullable&gt;,
/// [ApiController] trata un <c>string</c> no anulable como obligatorio y responde el 400 genérico de MVC en inglés
/// ('The Pin field is required.') antes de llegar al servicio, que es quien responde en español (401 'El aparato no está
/// registrado o fue desactivado.', 400 'El PIN debe tener de 4 a 6 dígitos.', 400 'La contraseña actual es incorrecta.',
/// 401 'El código de registro no es válido o venció.'). La firma posicional (nombre, tipo y nulabilidad) queda fijada por
/// reflexión, como en WmsContractsTests: volver a poner <c>string Pin</c> o <c>string DeviceSecret</c> rompe esta prueba.
/// </summary>
public class DeviceContractsTests
{
    private static readonly NullabilityInfoContext Nullability = new();

    private static string Signature(Type t)
        => string.Join(", ", t.GetConstructors().Single().GetParameters().Select(p => $"{TypeName(p.ParameterType, Nullability.Create(p))} {p.Name}"));

    private static string TypeName(Type t, NullabilityInfo info)
    {
        if (Nullable.GetUnderlyingType(t) is Type u) return TypeName(u, info) + "?";
        var name = t switch
        {
            _ when t == typeof(int) => "int",
            _ when t == typeof(bool) => "bool",
            _ when t == typeof(string) => "string",
            _ => t.Name,
        };
        return !t.IsValueType && info.ReadState == NullabilityState.Nullable ? name + "?" : name;
    }

    public static readonly TheoryData<Type, string> Signatures = new()
    {
        { typeof(DeviceEnrollRequest), "string? EnrollCode, string? Model, string? AppVersion" },
        { typeof(DeviceUsersRequest), "Guid DevicePublicId, string? DeviceSecret" },
        { typeof(DeviceLoginRequest), "Guid DevicePublicId, string? DeviceSecret, int UserId, string? Pin" },
        { typeof(PinSetRequest), "string? CurrentPassword, string? Pin" },
        { typeof(PinAdminSetRequest), "string? Pin" },
        { typeof(HeartbeatRequest), "Guid DevicePublicId, string? DeviceSecret, string? AppVersion" },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Device_and_pin_inputs_keep_their_nullable_signature(Type contract, string expected)
        => Assert.Equal(expected, Signature(contract));
}
