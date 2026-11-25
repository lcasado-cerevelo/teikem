using System.Text.RegularExpressions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 243 (G3) — el correo con la orden sale al SOLICITARLA, y solicitar tiene UN solo camino a REQUESTED: SalesOrderService.RequestAsync (lo usan
/// POST /sales-orders/{id}/request y «Enviar pedido» del pedido guiado). Esta prueba recorre src/ y falla si aparece otra llamada
/// TransitionAsync(... SalesOrderStatuses.Requested ...) que se salte el aviso, o si el método que la hace deja de llamar a NotifyRequestedAsync.
/// (El regreso de un despacho a REQUESTED usa una variable, no la constante: no es una solicitud y no manda nada.)
/// </summary>
public sealed class SalesRequestedChokePointTests
{
    private static readonly Regex TransitionToRequested = new(@"TransitionAsync\s*\([^;]*?SalesOrderStatuses\.Requested\b[^;]*;", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>Una declaración de miembro de clase (4 espacios de sangría): marca el final del método donde está la transición.</summary>
    private static readonly Regex NextMember = new(@"\n    (public|private|internal|protected)\b[^\n]*\(", RegexOptions.CultureInvariant);

    private static IEnumerable<(string Path, string Text)> Sources()
        => Directory.EnumerateFiles(Path.Combine(TripCatalogTests.RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (p, File.ReadAllText(p).Replace("\r\n", "\n")));

    [Fact]
    public void Hay_un_solo_camino_a_REQUESTED_y_avisa()
    {
        var hits = Sources().SelectMany(s => TransitionToRequested.Matches(s.Text).Select(m => (s.Path, s.Text, Match: m))).ToList();
        var hit = Assert.Single(hits);
        Assert.EndsWith(Path.Combine("Services", "SalesOrderService.Request.cs"), hit.Path);

        var start = hit.Match.Index + hit.Match.Length;
        var next = NextMember.Match(hit.Text, start);
        var rest = next.Success ? hit.Text[start..next.Index] : hit.Text[start..];
        Assert.Contains("await NotifyRequestedAsync(", rest);
        // y la declaración del método que la contiene es RequestAsync
        var before = hit.Text[..hit.Match.Index];
        var declaration = NextMember.Matches(before).LastOrDefault();
        Assert.NotNull(declaration);
        Assert.Contains("RequestAsync(", before[declaration!.Index..].Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
    }

    [Fact]
    public void El_aviso_de_orden_solicitada_solo_se_publica_desde_solicitar()
    {
        // (otros servicios tienen su propio NotifyRequestedAsync, p. ej. el aviso de salida: aquí solo cuenta el de la orden de venta)
        var calls = Sources().Where(s => Path.GetFileName(s.Path).StartsWith("SalesOrderService", StringComparison.Ordinal) && s.Text.Contains("NotifyRequestedAsync("))
            .Select(s => Path.GetFileName(s.Path)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "SalesOrderService.Notifications.cs", "SalesOrderService.Request.cs" }, calls);
    }
}
