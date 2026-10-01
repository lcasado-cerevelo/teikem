using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Compañía con la que entra un usuario de varias compañías cuando el login no pide una (pedido de Luis, 2026-09-30: no se
/// pregunta; se entra directo y se cambia desde el selector de la cabecera).
/// </summary>
public static class LoginTenantRules
{
    /// <summary>
    /// El predeterminado del usuario si sigue siendo miembro activo → la membresía marcada como predeterminada → la primera por
    /// nombre. <paramref name="memberships"/> trae solo membresías activas y no está vacía.
    /// </summary>
    public static int Pick(int? userDefaultTenantId, IReadOnlyList<TenantOptionDto> memberships)
    {
        if (memberships.Count == 0) throw new ArgumentException("Sin membresías activas.", nameof(memberships));
        if (userDefaultTenantId is int d && memberships.Any(m => m.TenantId == d)) return d;
        var flagged = memberships.FirstOrDefault(m => m.IsDefault);
        if (flagged is not null) return flagged.TenantId;
        return memberships.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(m => m.TenantId).First().TenantId;
    }
}
