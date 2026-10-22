using System.Text.Json.Nodes;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-22 — la bitácora muestra nombres, no llaves internas.</summary>
public class AuditChangesHumanizerTests
{
    private static string? Resolve(AuditChangesHumanizer.FkKind k, int id) => (k, id) switch
    {
        (AuditChangesHumanizer.FkKind.Status, 5) => "Esperado",
        (AuditChangesHumanizer.FkKind.Status, 6) => "Recibido",
        (AuditChangesHumanizer.FkKind.User, 7) => "Ana Ruiz",
        _ => null,
    };

    [Fact]
    public void Estatus_y_usuario_se_ven_con_su_nombre_y_sin_la_llave_interna()
    {
        var json = """{"$key":"ASN-1","StatusCodeId":{"from":5,"to":6},"AssignedToUserId":7,"Notes":"x"}""";
        var r = JsonNode.Parse(AuditChangesHumanizer.Humanize(json, "es", Resolve)!)!.AsObject();
        Assert.False(r.ContainsKey("$key"));
        Assert.False(r.ContainsKey("StatusCodeId"));
        Assert.Equal("Esperado", r["Estatus"]!["from"]!.GetValue<string>());
        Assert.Equal("Recibido", r["Estatus"]!["to"]!.GetValue<string>());
        Assert.Equal("Ana Ruiz", r["Asignado a"]!.GetValue<string>());
        Assert.Equal("x", r["Notes"]!.GetValue<string>());
    }

    [Fact]
    public void Lo_que_no_se_puede_nombrar_no_muestra_el_numero()
    {
        var json = """{"CarrierId":99,"ClientId":{"from":1,"to":2},"$reason":"Cambio"}""";
        var s = AuditChangesHumanizer.Humanize(json, "en", Resolve)!;
        Assert.DoesNotContain("99", s);
        Assert.Contains("(no name)", s);
        Assert.Contains("\"Reason\":\"Cambio\"", s);
    }

    [Fact]
    public void Un_detalle_que_no_es_objeto_se_devuelve_igual()
    {
        Assert.Equal("texto", AuditChangesHumanizer.Humanize("texto", "es", Resolve));
        Assert.Null(AuditChangesHumanizer.Humanize(null, "es", Resolve));
    }
}
