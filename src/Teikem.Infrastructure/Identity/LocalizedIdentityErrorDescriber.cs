using Microsoft.AspNetCore.Identity;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Infrastructure.Identity;

/// <summary>
/// Lote 120 — los mensajes de ASP.NET Core Identity en el idioma de la persona (es por omisión, en). Antes salían siempre en inglés («Incorrect password.», «Passwords must be at least 12 characters.») bajo una pantalla en
/// español. El código del error no cambia (<c>PasswordMismatch</c>, <c>PasswordTooShort</c>…): el servicio sigue reconociéndolos por código; solo cambia el texto. Solo se traducen los de contraseña y de cuenta repetida,
/// que son los que la persona puede ver; lo demás sigue con el texto por omisión.
/// </summary>
public sealed class LocalizedIdentityErrorDescriber(ITenantContext tenant) : IdentityErrorDescriber
{
    private bool English => string.Equals(tenant.Lang, "en", StringComparison.OrdinalIgnoreCase);
    private IdentityError E(IdentityError e, string es, string en) { e.Description = English ? en : es; return e; }

    public override IdentityError PasswordMismatch() => E(base.PasswordMismatch(), "La contraseña actual es incorrecta.", "The current password is incorrect.");
    public override IdentityError PasswordTooShort(int length) => E(base.PasswordTooShort(length), $"La contraseña debe tener al menos {length} caracteres.", $"The password must be at least {length} characters long.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => E(base.PasswordRequiresUniqueChars(uniqueChars), $"La contraseña debe tener al menos {uniqueChars} caracteres distintos.", $"The password must use at least {uniqueChars} different characters.");
    public override IdentityError PasswordRequiresDigit() => E(base.PasswordRequiresDigit(), "La contraseña debe tener al menos un número.", "The password must have at least one digit.");
    public override IdentityError PasswordRequiresLower() => E(base.PasswordRequiresLower(), "La contraseña debe tener al menos una letra minúscula.", "The password must have at least one lowercase letter.");
    public override IdentityError PasswordRequiresUpper() => E(base.PasswordRequiresUpper(), "La contraseña debe tener al menos una letra mayúscula.", "The password must have at least one uppercase letter.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => E(base.PasswordRequiresNonAlphanumeric(), "La contraseña debe tener al menos un símbolo.", "The password must have at least one symbol.");
    public override IdentityError DuplicateEmail(string email) => E(base.DuplicateEmail(email), "Ya existe una cuenta con ese correo.", "An account with that email already exists.");
    public override IdentityError DuplicateUserName(string userName) => E(base.DuplicateUserName(userName), "Ya existe una cuenta con ese correo.", "An account with that email already exists.");
}
