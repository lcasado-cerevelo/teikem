namespace Teikem.Domain.Security;

/// <summary>
/// Ventana de reautenticación (AAL2) de la compañía (Tenant.Aal2WindowMinutes): cuánto tiempo vale la verificación reciente para las acciones sensibles.
/// 2026-10-11: solo 15, 30 o 60 minutos (las ventanas de la maqueta y de la pantalla de Seguridad). Antes aceptaba cualquier valor entre 5 y 240. Una compañía que ya
/// tenía otro valor guardado lo conserva y puede guardar los demás ajustes (solo se valida un valor NUEVO); para cambiarlo, uno de los tres.
/// </summary>
public static class Aal2WindowRules
{
    public static readonly IReadOnlyList<int> Allowed = new[] { 15, 30, 60 };

    public const string InvalidMessage = "La ventana de reautenticación debe ser de 15, 30 o 60 minutos.";

    /// <summary>null = válido. <paramref name="current"/> = el valor ya guardado (reenviarlo igual no es un cambio).</summary>
    public static string? Validate(int requested, int current)
        => Allowed.Contains(requested) || requested == current ? null : InvalidMessage;
}
