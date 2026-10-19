using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Identity;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 120 — los mensajes de contraseña de Identity salen en el idioma de la persona y su código no cambia (el servicio los reconoce por código).</summary>
public class LocalizedIdentityErrorDescriberTests
{
    private static LocalizedIdentityErrorDescriber In(string lang) => new(new TenantContext { Lang = lang });

    [Fact]
    public void By_default_the_messages_are_in_spanish_and_in_english_when_the_person_uses_english()
    {
        Assert.Equal("La contraseña actual es incorrecta.", In("es").PasswordMismatch().Description);
        Assert.Equal("The current password is incorrect.", In("en").PasswordMismatch().Description);
        Assert.Equal("La contraseña debe tener al menos 12 caracteres.", In("es").PasswordTooShort(12).Description);
        Assert.Equal("The password must be at least 12 characters long.", In("en").PasswordTooShort(12).Description);
        Assert.Equal("La contraseña debe tener al menos 4 caracteres distintos.", In("es").PasswordRequiresUniqueChars(4).Description);
        Assert.Equal("Ya existe una cuenta con ese correo.", In("es").DuplicateEmail("a@b.co").Description);
    }

    [Fact]
    public void The_error_codes_do_not_change()
    {
        Assert.Equal(nameof(Microsoft.AspNetCore.Identity.IdentityErrorDescriber.PasswordMismatch), In("es").PasswordMismatch().Code);
        Assert.Equal(nameof(Microsoft.AspNetCore.Identity.IdentityErrorDescriber.PasswordTooShort), In("en").PasswordTooShort(12).Code);
    }
}
