using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Xunit;
using R = Teikem.Domain.Clients.ClientChainRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 245 (Lote B del plan) — reglas PURAS de las cadenas de clientes: nombre, notas y prefijo, coincidencia por el inicio del nombre (palabra completa), clasificación
/// de la asignación, huella de la simulación, exclusiones, mensajes exactos que citan el manual y la FAQ, análisis por cadena y serie mensual.
/// </summary>
public sealed class ClientChainRulesTests
{
    private const string P = SalesAnalysisCodes.ClassProduct;
    private const string D = SalesAnalysisCodes.ClassDiscount;
    private const string S = SalesAnalysisCodes.ClassShipping;
    private const string O = SalesAnalysisCodes.ClassOther;

    private static readonly DateOnly Oct = new(2026, 10, 1);
    private static readonly DateOnly Sep = new(2026, 9, 1);

    // ---------------------------------------------------------------- nombre, notas y prefijo

    [Fact]
    public void NormalizeName()
    {
        Assert.Equal(("Walgreens", (string?)null), R.NormalizeName("  Walgreens  "));
        Assert.Equal("Escriba el nombre de la cadena.", R.NormalizeName("   ").Error);
        Assert.Equal("Escriba el nombre de la cadena.", R.NormalizeName(null).Error);
        Assert.Null(R.NormalizeName(new string('a', 100)).Error);
        Assert.Equal("El nombre no puede pasar de 100 caracteres.", R.NormalizeName(new string('a', 101)).Error);

        Assert.Equal(((string?)null, (string?)null), R.NormalizeNotes("   "));
        Assert.Equal("Solo farmacias", R.NormalizeNotes(" Solo farmacias ").Notes);
        Assert.Null(R.NormalizeNotes(new string('n', 500)).Error);
        Assert.Equal("Las notas no pueden pasar de 500 caracteres.", R.NormalizeNotes(new string('n', 501)).Error);
    }

    [Fact]
    public void PrefixKey()
    {
        Assert.Equal("Escriba al menos 3 letras del inicio del nombre.", R.PrefixKey("  a. ").Error);
        Assert.Equal("Escriba al menos 3 letras del inicio del nombre.", R.PrefixKey("ab").Error);
        Assert.Equal("Escriba al menos 3 letras del inicio del nombre.", R.PrefixKey("a-b").Error);
        Assert.Equal("Escriba al menos 3 letras del inicio del nombre.", R.PrefixKey(null).Error);
        Assert.Equal(("WALGREENS", (string?)null), R.PrefixKey("Walgreens."));
        Assert.Equal("CANO", R.PrefixKey("Caño").Key);
        Assert.Null(R.PrefixKey(new string('a', 100)).Error);
        Assert.Equal("El prefijo no puede pasar de 100 caracteres.", R.PrefixKey(new string('a', 101)).Error);
    }

    private static bool Matches(string name, string prefix, bool wholeWord) => R.MatchesPrefix(R.NameKey(name), R.PrefixKey(prefix).Key!, wholeWord);

    [Fact]
    public void MatchesPrefix()
    {
        foreach (var whole in new[] { true, false })
        {
            Assert.True(Matches("WALGREENS #1", "Walgreens", whole));
            Assert.True(Matches("walgreens 2", "Walgreens", whole));
            Assert.True(Matches("Walgreens-3", "Walgreens", whole));
            Assert.True(Matches("Walgreens", "Walgreens", whole));
            Assert.False(Matches("CVS Walgreens", "Walgreens", whole));   // solo el inicio
        }

        // palabra completa: el carácter siguiente no puede ser letra ni dígito
        Assert.False(Matches("Walgreensboro", "Walgreens", true));
        Assert.False(Matches("Walgreens1", "Walgreens", true));
        Assert.True(Matches("Walgreensboro", "Walgreens", false));
        Assert.True(Matches("Walgreens1", "Walgreens", false));

        // acentos: Ñ → N, así que «CAÑÓN» empieza con «CANO» pero no como palabra completa
        Assert.False(Matches("FARMACIA CAÑÓN", "Farmacia Caño", true));
        Assert.True(Matches("FARMACIA CAÑÓN", "Farmacia Caño", false));
        Assert.True(Matches("Farmacia Caño #4", "Farmacia Cano", true));

        // un prefijo que termina en guion se respeta: lo que sigue puede ser letra
        Assert.True(Matches("Walg-Norte", "Walg-", true));
        Assert.False(Matches("Walgreens", "Walg-", true));
        Assert.False(Matches("Walgreens", "Walg-", false));

        // un espacio final se pierde en la clave y equivale a palabra completa
        Assert.Equal(R.PrefixKey("Walgreens").Key, R.PrefixKey("Walgreens ").Key);
        Assert.False(Matches("Walgreensboro", "Walgreens ", true));
        Assert.True(Matches("Walgreens 9", "Walgreens ", true));
    }

    // ---------------------------------------------------------------- asignación

    [Fact]
    public void Classify()
    {
        Assert.Equal("ALREADY", R.Classify(7, 7, false));
        Assert.Equal("ALREADY", R.Classify(7, 7, true));
        Assert.Equal("ASSIGN", R.Classify(null, 7, false));
        Assert.Equal("ASSIGN", R.Classify(null, 7, true));
        Assert.Equal("SKIP_OTHER_CHAIN", R.Classify(3, 7, false));
        Assert.Equal("MOVE", R.Classify(3, 7, true));
    }

    [Fact]
    public void Fingerprint()
    {
        var a = new (int, int?)[] { (1, null), (2, 5), (3, 7) };
        var baseFp = R.Fingerprint(7, "WALGREENS", true, false, a);
        Assert.Matches("^[0-9a-f]{64}$", baseFp);

        // no depende del orden de entrada
        Assert.Equal(baseFp, R.Fingerprint(7, "WALGREENS", true, false, new (int, int?)[] { (3, 7), (1, null), (2, 5) }));

        // cambia si cambia la cadena de un cliente, si entra o sale uno, o si cambian palabra completa, mover, la cadena destino o el prefijo
        Assert.NotEqual(baseFp, R.Fingerprint(7, "WALGREENS", true, false, new (int, int?)[] { (1, 5), (2, 5), (3, 7) }));
        Assert.NotEqual(baseFp, R.Fingerprint(7, "WALGREENS", true, false, new (int, int?)[] { (1, null), (2, 5), (3, 7), (4, null) }));
        Assert.NotEqual(baseFp, R.Fingerprint(7, "WALGREENS", true, false, new (int, int?)[] { (1, null), (2, 5) }));
        Assert.NotEqual(baseFp, R.Fingerprint(7, "WALGREENS", false, false, a));
        Assert.NotEqual(baseFp, R.Fingerprint(7, "WALGREENS", true, true, a));
        Assert.NotEqual(baseFp, R.Fingerprint(8, "WALGREENS", true, false, a));
        Assert.NotEqual(baseFp, R.Fingerprint(7, "WALGREEN", true, false, a));
        // las exclusiones no entran: la firma no las recibe, así que excluir no cambia la huella
    }

    [Fact]
    public void ValidateExclusions_and_messages()
    {
        var matched = new[] { 1, 2, 3 };
        Assert.Null(R.ValidateExclusions(matched, null));
        Assert.Null(R.ValidateExclusions(matched, Array.Empty<int>()));
        Assert.Null(R.ValidateExclusions(matched, new[] { 1, 3 }));
        Assert.Equal("Un cliente excluido no está entre los que encontró la simulación.", R.ValidateExclusions(matched, new[] { 1, 9 }));

        Assert.Equal("La cadena tiene 1 cliente activo: quítelo de la cadena antes de desactivarla.", R.HasActiveClients(1));
        Assert.Equal("La cadena tiene 3 clientes activos: quítelos de la cadena antes de desactivarla.", R.HasActiveClients(3));
        Assert.Equal("La simulación encontró 2001 clientes y el máximo por aplicación es 2000: use un prefijo más largo.", R.BulkTooMany(2001));

        Assert.Equal("Ya hay una cadena activa con ese nombre.", R.NameTaken);
        Assert.Equal("Cadena no encontrada.", R.NotFound);
        Assert.Equal("La cadena está desactivada: actívela o escoja otra.", R.ChainInactive);
        Assert.Equal("El cuerpo de la solicitud es obligatorio.", R.BodyRequired);
        Assert.Equal("Indique los clientes (clientPublicIds).", R.ClientsRequired);
        Assert.Equal("No se pueden mandar más de 2000 clientes en una sola llamada.", R.TooManyInCall);
        Assert.Equal("Falta la huella de la simulación (fingerprint): simule antes de aplicar.", R.FingerprintRequired);
        Assert.Equal("Los clientes que coinciden cambiaron desde la simulación: vuelva a simular antes de aplicar.", R.FingerprintStale);
        Assert.Equal("Escoja una cadena o «Sin cadena», no las dos.", R.ChainOrNoChain);
        Assert.Equal("Indique la cadena (chainPublicId).", R.ChainRequired);
        Assert.Equal((100, 500, 3, 2000, 500), (R.NameMaxLength, R.NotesMaxLength, R.PrefixMinLength, R.MaxBulk, R.MaxPageSize));
    }

    // ---------------------------------------------------------------- análisis por cadena

    // cadenas: 1 Walgreens (activa), 2 CVS (activa), 3 Vieja (inactiva, con venta), 4 Nueva (activa, sin clientes ni venta), 5 Muerta (inactiva, sin venta)
    private static readonly ChainInfo[] Chains =
    {
        new(1, "Walgreens", true), new(2, "CVS", true), new(3, "Vieja", false), new(4, "Nueva", true), new(5, "Muerta", false),
    };

    // clientes: 10, 11, 12 en Walgreens (12 inactivo); 20 en CVS; 30 en Vieja; 40 y 41 sin cadena; 42 sin cadena e inactivo
    private static readonly Dictionary<int, ChainClientInfo> Clients = new[]
    {
        new ChainClientInfo(10, "Walgreens #1", true), new ChainClientInfo(11, "Walgreens #2", true), new ChainClientInfo(12, "Walgreens #3", false),
        new ChainClientInfo(20, "CVS Bayamón", true), new ChainClientInfo(30, "Vieja Central", true),
        new ChainClientInfo(40, "Farmacia Central", true), new ChainClientInfo(41, "Farmacia Sin Venta", true), new ChainClientInfo(42, "Cerrada", false),
    }.ToDictionary(c => c.ClientId);

    private static readonly Dictionary<int, int> ClientChain = new() { [10] = 1, [11] = 1, [12] = 1, [20] = 2, [30] = 3 };

    // historial (líneas importadas, a veces sin cliente emparejado) y facturas de Teikem inventados
    private static List<ChainSaleLine> Lines() => new()
    {
        // Walgreens: oct 1000 - 50 (descuento) + 200; sep 600; el envío y otros no cuentan
        new(10, "WALGREENS #1", new DateOnly(2026, 10, 3), 1000m, P),
        new(10, "WALGREENS #1", new DateOnly(2026, 10, 3), -50m, D),
        new(10, "WALGREENS #1", new DateOnly(2026, 10, 3), 75m, S),
        new(10, "WALGREENS #1", new DateOnly(2026, 10, 3), 30m, O),
        new(11, "Walgreens #2", new DateOnly(2026, 10, 20), 200m, P),
        new(10, "WALGREENS #1", new DateOnly(2026, 9, 10), 600m, P),
        // CVS: oct 800, sep 0 (ChangePct null)
        new(20, "CVS Bayamon", new DateOnly(2026, 10, 9), 800m, P),
        // Vieja (inactiva) vendió en septiembre
        new(30, "Vieja Central", new DateOnly(2026, 9, 2), 120m, P),
        // sin cadena: cliente 40 y dos líneas sin cliente con el mismo nombre escrito distinto
        new(40, "Farmacia Central", new DateOnly(2026, 10, 15), 300m, P),
        new(null, "Colmado Pepe", new DateOnly(2026, 10, 1), 100m, P),
        new(null, "COLMADO PEPE.", new DateOnly(2026, 10, 2), 50m, P),
        new(null, "Colmado Pepe", new DateOnly(2026, 9, 2), 40m, P),
        // un mes que no es ninguno de los dos no cuenta
        new(10, "WALGREENS #1", new DateOnly(2026, 8, 2), 9999m, P),
    };

    [Fact]
    public void SalesByChain_cuenta_producto_y_descuento_y_concilia()
    {
        var r = R.SalesByChain(Lines(), ClientChain, Clients, Chains, Oct, Sep);

        // filas: activas (aunque sin venta) + inactiva con venta; la inactiva sin venta no sale; orden por venta desc y luego nombre
        Assert.Equal(new[] { "Walgreens", "CVS", "Nueva", "Vieja" }, r.Rows.Select(x => x.ChainName));

        var wal = r.Rows[0];
        Assert.Equal(1150m, wal.Sales);           // 1000 - 50 + 200; envío y otros fuera
        Assert.Equal(600m, wal.CompareSales);
        Assert.Equal(550m, wal.Change);
        Assert.Equal(0.9167m, wal.ChangePct);
        Assert.Equal(2, wal.ClientCount);        // el 12 está inactivo
        Assert.Equal(2, wal.ClientsWithSales);
        Assert.Equal(new int?[] { 10, 11 }, wal.Clients.Select(c => c.ClientId));
        Assert.Equal("Walgreens #1", wal.Clients[0].ClientName);   // el nombre de Teikem, no el del historial
        Assert.Equal(wal.Sales, wal.Clients.Sum(c => c.Sales));
        Assert.Equal(wal.CompareSales, wal.Clients.Sum(c => c.CompareSales));
        Assert.Equal(SalesAnalysisRules.Share(950m, 1150m), wal.Clients[0].ShareOfChain);

        var cvs = r.Rows[1];
        Assert.Equal(800m, cvs.Sales);
        Assert.Null(cvs.ChangePct);              // la comparación es 0
        Assert.Equal(1, cvs.ClientCount);

        var nueva = r.Rows[2];
        Assert.Equal((0m, 0, 0), (nueva.Sales, nueva.ClientCount, nueva.Clients.Count));

        var vieja = r.Rows[3];
        Assert.False(vieja.IsActive);
        Assert.Equal((0m, 120m), (vieja.Sales, vieja.CompareSales));

        // «Sin cadena»: el cliente 40 y «Colmado Pepe» agrupado por nombre
        var nc = r.NoChain!;
        Assert.Null(nc.ChainId);
        Assert.Equal("Sin cadena", nc.ChainName);
        Assert.Equal(450m, nc.Sales);            // 300 + 100 + 50
        Assert.Equal(40m, nc.CompareSales);
        Assert.Equal(2, nc.ClientCount);         // 40 y 41 (el 42 está inactivo)
        Assert.Equal(2, nc.ClientsWithSales);
        Assert.Equal(2, nc.Clients.Count);       // el 41 no vendió: no sale en el desglose sin cadena
        Assert.Equal((40, "Farmacia Central", 300m), (nc.Clients[0].ClientId!.Value, nc.Clients[0].ClientName, nc.Clients[0].Sales));
        Assert.Null(nc.Clients[1].ClientId);
        Assert.Equal(("Colmado Pepe", 150m, 40m), (nc.Clients[1].ClientName, nc.Clients[1].Sales, nc.Clients[1].CompareSales));
        Assert.Equal(nc.Sales, nc.Clients.Sum(c => c.Sales));

        // conciliación: cadenas + sin cadena = neta del mes
        Assert.Equal(1950m, r.Reconciliation.ChainsTotal);
        Assert.Equal(450m, r.Reconciliation.NoChain);
        Assert.Equal(2400m, r.Reconciliation.MonthNet);
        Assert.Equal(0m, r.Reconciliation.Difference);

        // participación sobre la neta y totales
        Assert.Equal(SalesAnalysisRules.Share(1150m, 2400m), wal.ShareOfNet);
        Assert.Equal(SalesAnalysisRules.Share(450m, 2400m), nc.ShareOfNet);
        Assert.Equal(new ChainSalesTotals(760m, 2400m, 1640m, SalesAnalysisRules.ChangePct(760m, 2400m)), r.Totals);

        // el desglose de cada fila suma la fila
        foreach (var row in r.Rows) Assert.Equal(row.Sales, row.Clients.Sum(c => c.Sales));
    }

    [Fact]
    public void SalesByChain_redondea_solo_al_final()
    {
        var chains = new[] { new ChainInfo(1, "A", true) };
        var clients = new Dictionary<int, ChainClientInfo> { [1] = new(1, "Uno", true) };
        var map = new Dictionary<int, int> { [1] = 1 };
        // 0.004 + 0.004 = 0.008 → 0.01 (redondear cada línea daría 0); 0.005 → 0.01 con AwayFromZero (bancario daría 0)
        var lines = new List<ChainSaleLine>
        {
            new(1, "Uno", Oct, 0.004m, P), new(1, "Uno", Oct, 0.004m, P),
            new(null, "Suelto", Oct, 0.005m, P),
        };
        var r = R.SalesByChain(lines, map, clients, chains, Oct, Sep);
        Assert.Equal(0.01m, r.Rows[0].Sales);
        Assert.Equal(0.01m, r.Rows[0].Clients[0].Sales);
        Assert.Equal(0.01m, r.NoChain!.Sales);
        Assert.Equal(0.01m, r.Reconciliation.MonthNet);   // 0.013 → 0.01
        Assert.Equal(-0.01m, r.Reconciliation.Difference); // lo que no cuadra por redondeo se ve, no se esconde
    }

    [Fact]
    public void SalesByChain_ordena_por_venta_y_luego_por_nombre()
    {
        var chains = new[] { new ChainInfo(1, "Beta", true), new ChainInfo(2, "alfa", true), new ChainInfo(3, "Gama", true) };
        var clients = new Dictionary<int, ChainClientInfo> { [1] = new(1, "B", true), [2] = new(2, "A", true), [3] = new(3, "G", true) };
        var map = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        var lines = new List<ChainSaleLine> { new(1, "B", Oct, 100m, P), new(2, "A", Oct, 100m, P), new(3, "G", Oct, 500m, P) };
        var r = R.SalesByChain(lines, map, clients, chains, Oct, Sep);
        Assert.Equal(new[] { "Gama", "alfa", "Beta" }, r.Rows.Select(x => x.ChainName));
    }

    [Fact]
    public void SalesByChain_con_una_sola_cadena()
    {
        var r = R.SalesByChain(Lines(), ClientChain, Clients, Chains, Oct, Sep, onlyChainId: 2);
        var row = Assert.Single(r.Rows);
        Assert.Equal(2, row.ChainId);
        Assert.Null(r.NoChain);
        Assert.Equal(new ChainSalesTotals(0m, 800m, 800m, null), r.Totals);
        // la conciliación sigue siendo la del mes completo
        Assert.Equal(new ChainSalesReconciliation(1950m, 450m, 2400m, 0m), r.Reconciliation);

        // una cadena inactiva sin venta no tiene fila
        Assert.Empty(R.SalesByChain(Lines(), ClientChain, Clients, Chains, Oct, Sep, onlyChainId: 5).Rows);
    }

    [Fact]
    public void SalesByChain_mismo_mes_suma_en_los_dos_y_cadena_desconocida_es_sin_cadena()
    {
        var map = new Dictionary<int, int>(ClientChain) { [40] = 99 };   // cadena que no viene en la lista
        var r = R.SalesByChain(Lines(), map, Clients, Chains, Oct, Oct);
        Assert.Equal(450m, r.NoChain!.Sales);
        Assert.Equal(450m, r.NoChain.CompareSales);
        Assert.Equal(r.Rows[0].Sales, r.Rows[0].CompareSales);
        Assert.Equal(0m, r.Reconciliation.Difference);
    }

    // ---------------------------------------------------------------- serie

    [Fact]
    public void Series_meses_y_clientes_principales()
    {
        var ids = new[] { 1, 2, 3, 4, 5, 6, 7 };
        var lines = new List<ChainSaleLine>();
        for (var i = 1; i <= 7; i++) lines.Add(new ChainSaleLine(i, "C" + i, new DateOnly(2026, 3, 5), i * 100m, P));
        lines.Add(new ChainSaleLine(1, "C1", new DateOnly(2026, 5, 5), 50m, P));
        lines.Add(new ChainSaleLine(1, "C1", new DateOnly(2026, 5, 5), -10m, D));
        lines.Add(new ChainSaleLine(1, "C1", new DateOnly(2026, 5, 5), 999m, S));       // envío: no cuenta
        lines.Add(new ChainSaleLine(99, "Otro", new DateOnly(2026, 3, 5), 5000m, P));   // otro cliente: no cuenta
        lines.Add(new ChainSaleLine(null, "Suelto", new DateOnly(2026, 3, 5), 5000m, P));
        lines.Add(new ChainSaleLine(2, "C2", new DateOnly(2026, 7, 1), 7000m, P));      // fuera del rango

        var r = R.Series(lines, ids, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1));
        Assert.Equal(6, r.Months.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), r.Months[0].Month);
        Assert.Equal(0m, r.Months[0].Sales);
        Assert.Equal(2800m, r.Months[2].Sales);   // marzo: 100 + … + 700
        Assert.Equal(40m, r.Months[4].Sales);     // mayo: 50 - 10

        Assert.Equal(new[] { 7, 6, 5, 4, 3 }, r.TopClients.Select(t => t.ClientId));
        Assert.Equal(700m, r.TopClients[0].Sales);
        Assert.Equal(SalesAnalysisRules.Share(700m, 2840m), r.TopClients[0].Share);

        // 12 meses de un año completo, todos en 0 si no hay venta
        var empty = R.Series(Array.Empty<ChainSaleLine>(), ids, new DateOnly(2025, 11, 1), new DateOnly(2026, 10, 1));
        Assert.Equal(12, empty.Months.Count);
        Assert.All(empty.Months, mm => Assert.Equal(0m, mm.Sales));
        Assert.Empty(empty.TopClients);
    }
}
