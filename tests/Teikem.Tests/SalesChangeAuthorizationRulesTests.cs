using Teikem.Domain.Sales;
using Xunit;
using R = Teikem.Domain.Sales.SalesChangeRules;

namespace Teikem.Tests;

/// <summary>Lote 249 (P2) — quién autorizó en el cliente y cómo (mensajes exactos, 400) y el estado de la factura frente a QuickBooks.</summary>
public sealed class SalesChangeAuthorizationRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OrderCreated = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static (string Field, string Message)? V(string? name = "Ana Ruiz", string? title = "Compras", string? channel = "PHONE", DateTime? at = null, string? reason = "Pidió 2 más")
        => R.ValidateAuthorization(name, title, channel, at, reason, Now, OrderCreated);

    [Fact]
    public void Una_autorizacion_completa_vale()
    {
        Assert.Null(V());
        Assert.Null(V(title: null));
        Assert.Null(V(at: Now.AddHours(-2)));
        Assert.Null(V(channel: " in_person "));
    }

    [Fact]
    public void Nombre_obligatorio_y_hasta_150()
    {
        Assert.Equal(("authorizedByName", "Indique quién autorizó el cambio en el cliente."), V(name: null));
        Assert.Equal(("authorizedByName", "Indique quién autorizó el cambio en el cliente."), V(name: "   "));
        Assert.Equal(("authorizedByName", "El nombre de quien autorizó no puede pasar de 150 caracteres."), V(name: new string('a', 151)));
        Assert.Null(V(name: new string('a', 150)));
    }

    [Fact]
    public void Cargo_hasta_100()
    {
        Assert.Equal(("authorizedByTitle", "El cargo de quien autorizó no puede pasar de 100 caracteres."), V(title: new string('c', 101)));
        Assert.Null(V(title: new string('c', 100)));
    }

    [Fact]
    public void Canal_obligatorio_y_valido()
    {
        Assert.Equal(("authorizationChannel", "Indique cómo autorizó el cliente (por teléfono, por correo, en persona o por mensaje)."), V(channel: null));
        Assert.Equal(("authorizationChannel", "Indique cómo autorizó el cliente (por teléfono, por correo, en persona o por mensaje)."), V(channel: " "));
        Assert.Equal(("authorizationChannel", "El canal de autorización no es válido: use PHONE, EMAIL, IN_PERSON o MESSAGE."), V(channel: "FAX"));
        Assert.Equal("EMAIL", R.NormalizeChannel(" email "));
        Assert.Null(R.NormalizeChannel("fax"));
    }

    [Fact]
    public void Fecha_futura_con_5_minutos_de_holgura_limite_incluido()
    {
        Assert.Null(V(at: Now.AddMinutes(5)));
        Assert.Equal(("authorizedAtUtc", "La fecha de la autorización no puede ser futura."), V(at: Now.AddMinutes(5).AddTicks(1)));
        Assert.Equal(("authorizedAtUtc", "La fecha de la autorización no puede ser futura."), V(at: Now.AddDays(1)));
    }

    [Fact]
    public void Fecha_anterior_a_la_orden()
    {
        Assert.Equal(("authorizedAtUtc", "La fecha de la autorización no puede ser anterior a la orden."), V(at: OrderCreated.AddSeconds(-1)));
        Assert.Null(V(at: OrderCreated));
    }

    [Fact]
    public void Motivo_obligatorio_y_hasta_500()
    {
        Assert.Equal(("reason", "Indique el motivo de la modificación."), V(reason: null));
        Assert.Equal(("reason", "Indique el motivo de la modificación."), V(reason: "  "));
        Assert.Equal(("reason", "El motivo de la modificación no puede pasar de 500 caracteres."), V(reason: new string('m', 501)));
        Assert.Null(V(reason: new string('m', 500)));
    }

    [Theory]
    [InlineData(null, false, false, "NOT_ISSUED")]
    [InlineData("DRAFT", false, false, "NOT_ISSUED")]
    [InlineData("VOID", true, true, "NOT_ISSUED")]
    [InlineData("ISSUED", false, false, "PENDING_EXPORT")]
    [InlineData("OVERDUE", true, false, "EXPORTED_UNCONFIRMED")]
    [InlineData("PAID", true, true, "IN_QUICKBOOKS")]
    [InlineData("ISSUED", false, true, "IN_QUICKBOOKS")]
    public void Estado_de_QuickBooks(string? status, bool exported, bool confirmed, string expected)
    {
        DateTime? exportedAt = exported ? Now.AddDays(-1) : null;
        DateTime? qbAt = confirmed ? Now : null;
        Assert.Equal(expected, R.QuickBooksState(status, exportedAt, qbAt));
        Assert.Contains(expected, SalesChangeQuickBooksStates.All);
    }
}
