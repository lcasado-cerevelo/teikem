using System.Text.Json;
using Teikem.Domain.Tenancy;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Marca por compañía (2026-10) — BrandingRules: los 13 temas predefinidos pasan, cada regla falla con su mensaje exacto y el orden de
/// las comprobaciones es el del contrato. La PARIDAD con la web se prueba contra los vectores compartidos
/// (tests/shared/brand-vectors.json, el mismo archivo que lee vitest): entrada → válido/código/mensaje/comprobaciones idénticos.
/// </summary>
public sealed class BrandingRulesTests
{
    private sealed record VectorCheck(string Type, string? Key, string? Mode, double Value, double Min, bool Pass);
    private sealed record VectorCase(string Name, string Input, int? PadToLength, bool Valid, string? Code, string? Message, List<VectorCheck> Checks);

    private static readonly Lazy<(int MaxChars, double MinHue, List<VectorCase> Cases)> Vectors = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "shared", "brand-vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var cases = new List<VectorCase>();
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var checks = new List<VectorCheck>();
            if (c.TryGetProperty("checks", out var cs))
                foreach (var k in cs.EnumerateArray())
                {
                    var isContrast = k.GetProperty("type").GetString() == "contrast";
                    checks.Add(new VectorCheck(k.GetProperty("type").GetString()!, isContrast ? k.GetProperty("key").GetString() : null,
                        isContrast ? k.GetProperty("mode").GetString() : null, k.GetProperty(isContrast ? "ratio" : "distance").GetDouble(),
                        k.GetProperty("min").GetDouble(), k.GetProperty("pass").GetBoolean()));
                }
            cases.Add(new VectorCase(c.GetProperty("name").GetString()!, c.GetProperty("input").GetString()!,
                c.TryGetProperty("padToLength", out var pad) ? pad.GetInt32() : null, c.GetProperty("valid").GetBoolean(),
                c.TryGetProperty("code", out var code) ? code.GetString() : null, c.TryGetProperty("message", out var msg) ? msg.GetString() : null, checks));
        }
        var limits = root.GetProperty("limits");
        return (limits.GetProperty("maxChars").GetInt32(), limits.GetProperty("minHueDistance").GetDouble(), cases);
    });

    public static IEnumerable<object[]> VectorNames() => Vectors.Value.Cases.Select(c => new object[] { c.Name });

    private static string Raw(VectorCase c) => c.PadToLength is int n ? c.Input + new string(' ', n - c.Input.Length) : c.Input;

    private static string Std(string preset) =>
        $"{{\"preset\":\"{preset}\",\"useCustom\":false,\"custom\":{{\"flow\":\"#1F6FE5\",\"money\":\"#FF6A1A\",\"neutral\":\"#2B3A5C\"}}}}";

    // ------------------------------------------------------------------ paridad con la web

    [Fact]
    public void Shared_vectors_declare_the_same_limits_as_the_rules_and_cover_everything()
    {
        Assert.Equal(BrandingRules.MaxJsonChars, Vectors.Value.MaxChars);
        Assert.Equal(BrandingRules.MinHueDistance, Vectors.Value.MinHue);
        Assert.Equal(BrandingRules.Presets.Select(p => $"predefinido {p.Id}"), Vectors.Value.Cases.Where(c => c.Name.StartsWith("predefinido ")).Select(c => c.Name));
        Assert.True(Vectors.Value.Cases.Count(c => !c.Valid) >= 30);
        Assert.Equal(
            new[] { "badHex", "badType", "contrast", "hue", "malformed", "notObject", "statusColor", "tooLarge", "unknownField", "unknownPreset" },
            Vectors.Value.Cases.Select(c => c.Code).Where(c => c is not null).Distinct().OrderBy(c => c, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Shared_vector_gives_the_same_result_as_the_web(string name)
    {
        var c = Vectors.Value.Cases.Single(x => x.Name == name);
        var v = BrandingRules.Validate(Raw(c));
        Assert.Equal(c.Valid, v.Ok);
        Assert.Equal(c.Code, v.Code);
        Assert.Equal(c.Message, v.Message);
        Assert.Equal(c.Checks.Count, v.Checks.Count);
        for (var i = 0; i < c.Checks.Count; i++)
        {
            var e = c.Checks[i];
            var a = v.Checks[i];
            Assert.Equal((e.Type, e.Key, e.Mode, e.Value, e.Min, e.Pass), (a.Type, a.Key, a.Mode, a.Value, a.Min, a.Pass));
        }
    }

    // ------------------------------------------------------------------ reglas

    [Fact]
    public void All_thirteen_presets_pass_every_check()
    {
        Assert.Equal(13, BrandingRules.Presets.Count);
        foreach (var p in BrandingRules.Presets)
        {
            var v = BrandingRules.Validate(Std(p.Id));
            Assert.True(v.Ok, $"{p.Id}: {v.Message}");
            Assert.Equal(9, v.Checks.Count);
            Assert.All(v.Checks, c => Assert.True(c.Pass, $"{p.Id} {c.Key} {c.Mode}"));
        }
    }

    [Fact]
    public void Empty_clears_the_brand_and_missing_fields_use_the_defaults()
    {
        Assert.True(BrandingRules.Validate("").Ok);
        Assert.Empty(BrandingRules.Validate("").Checks);
        var v = BrandingRules.Validate("{}");
        Assert.True(v.Ok);
        Assert.Equal(BrandingRules.Validate(Std("teikem")).Checks, v.Checks);
    }

    [Fact]
    public void Each_rule_fails_with_its_exact_message_and_code()
    {
        void Fail(string json, string code, string message)
        {
            var v = BrandingRules.Validate(json);
            Assert.False(v.Ok);
            Assert.Equal((code, message), (v.Code, v.Message));
        }
        Fail("{", "malformed", "La marca no es un JSON válido.");
        Fail("[]", "notObject", "La marca debe ser un objeto JSON.");
        Fail("{\"logoUrl\":\"x\"}", "unknownField", "La marca trae un campo desconocido: 'logoUrl'.");
        Fail("{\"custom\":{\"accent\":\"#fff\"}}", "unknownField", "La marca trae un campo desconocido: 'custom.accent'.");
        Fail("{\"danger\":\"#0f0\"}", "statusColor", "Los colores de estado (ok, warn, danger, info) no se pueden personalizar: 'danger'.");
        Fail("{\"custom\":{\"OK\":\"#0f0\"}}", "statusColor", "Los colores de estado (ok, warn, danger, info) no se pueden personalizar: 'custom.OK'.");
        Fail("{\"preset\":1}", "badType", "El campo 'preset' tiene un tipo inválido.");
        Fail("{\"useCustom\":\"si\"}", "badType", "El campo 'useCustom' tiene un tipo inválido.");
        Fail("{\"custom\":[]}", "badType", "El campo 'custom' tiene un tipo inválido.");
        Fail("{\"custom\":{\"flow\":5}}", "badType", "El campo 'custom.flow' tiene un tipo inválido.");
        Fail("{\"custom\":{\"money\":\"rojo\"}}", "badHex", "El color 'custom.money' no es hexadecimal (use #RGB o #RRGGBB).");
        Fail("{\"custom\":{\"flow\":\"#fff\\n\"}}", "badHex", "El color 'custom.flow' no es hexadecimal (use #RGB o #RRGGBB).");
        Fail("{\"preset\":\"neon\"}", "unknownPreset", "El tema predefinido 'neon' no existe.");
        Fail("{\"useCustom\":true,\"custom\":{\"flow\":\"#000000\"}}", "contrast", "El contraste del color de operación en modo oscuro es 1.34:1; el mínimo es 4.5:1.");
        Fail("{\"useCustom\":true,\"custom\":{\"money\":\"#FFFF00\"}}", "contrast", "El contraste del color de dinero en modo claro es 2.69:1; el mínimo es 4.5:1.");
        Fail("{\"useCustom\":true,\"custom\":{\"flow\":\"#1F6FE5\",\"money\":\"#1F6FE5\"}}", "hue", "Los colores de operación y de dinero son demasiado parecidos: 0° de separación y el mínimo es 40°.");
    }

    [Fact]
    public void The_size_limit_is_4096_characters()
    {
        var json = "{\"preset\":\"teikem\"}";
        Assert.True(BrandingRules.Validate(json + new string(' ', 4096 - json.Length)).Ok);
        var v = BrandingRules.Validate(json + new string(' ', 4097 - json.Length));
        Assert.Equal(("tooLarge", "La marca es demasiado grande (máximo 4096 caracteres); los logos se suben aparte."), (v.Code, v.Message));
    }

    [Fact]
    public void Checks_are_reported_in_order_dark_then_light_and_the_first_failure_wins()
    {
        var v = BrandingRules.Validate("{\"useCustom\":true,\"custom\":{\"flow\":\"#FFFFFF\"}}");
        Assert.Equal("contrast", v.Code);
        Assert.Equal("El contraste del color de operación en modo claro es 1.56:1; el mínimo es 4.5:1.", v.Message);
        Assert.Equal(new[] { "text", "muted", "flow", "money", "text", "muted", "flow", "money", null }, v.Checks.Select(c => c.Key));
        Assert.Equal(new[] { "dark", "dark", "dark", "dark", "light", "light", "light", "light", null }, v.Checks.Select(c => c.Mode));
    }

    [Fact]
    public void The_neutral_saturation_of_a_custom_theme_is_capped()
    {
        // un azul puro tiene saturación 1.0: se acota a 0.45 (si no, teñiría la interfaz entera) y sigue siendo legible
        var vivid = BrandingRules.Colors(new BrandSettings("teikem", true, "#1F6FE5", "#FF6A1A", "#0000FF"));
        Assert.Equal(BrandingRules.NeutralSatMax, vivid.Ns);
        var preset = BrandingRules.Colors(new BrandSettings("marino", false, "#000000", "#000000", "#0000FF"));
        Assert.Equal(("#1F59A3", "#E08A17", 215d, 0.42), (preset.Flow, preset.Money, preset.Nh, preset.Ns));
    }

    [Fact]
    public void Hex_helpers_accept_short_long_and_hashless_and_reject_the_rest()
    {
        Assert.True(BrandingRules.IsValidHex("#abc"));
        Assert.True(BrandingRules.IsValidHex("AABBCC"));
        Assert.False(BrandingRules.IsValidHex("#abcd"));
        Assert.False(BrandingRules.IsValidHex(" #abc"));
        Assert.False(BrandingRules.IsValidHex("#abc\n"));
        Assert.False(BrandingRules.IsValidHex(null));
        Assert.Equal("#AABBCC", BrandingRules.NormalizeHex("abc"));
    }
}
