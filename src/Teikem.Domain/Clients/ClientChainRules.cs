using System.Security.Cryptography;
using System.Text;
using Teikem.Domain.Sales;

namespace Teikem.Domain.Clients;

/// <summary>
/// Lote 245 (Lote B del plan) — una línea de venta vista por el análisis por cadena: el cliente de Teikem (null = el historial no lo emparejó), el nombre tal como viene,
/// un día cualquiera del mes de la línea (se toma su mes), el monto y la clase (PRODUCT, DISCOUNT, SHIPPING u OTHER).
/// </summary>
public sealed record ChainSaleLine(int? ClientId, string ClientName, DateOnly Month, decimal Amount, string LineClass);

/// <summary>Lote 245 — lo que el análisis necesita de un cliente: su nombre y si está activo (los inactivos no cuentan en ClientCount).</summary>
public sealed record ChainClientInfo(int ClientId, string Name, bool IsActive);

/// <summary>Lote 245 — lo que el análisis necesita de una cadena.</summary>
public sealed record ChainInfo(int ChainId, string Name, bool IsActive);

/// <summary>Lote 245 — un cliente dentro de una fila del análisis por cadena (ClientId null = cliente del historial sin emparejar, agrupado por nombre).</summary>
public sealed record ChainSalesClient(int? ClientId, string ClientName, decimal CompareSales, decimal Sales, decimal Change, decimal? ChangePct, decimal? ShareOfChain);

/// <summary>Lote 245 — una fila del análisis por cadena (ChainId null = «Sin cadena»), con su desglose por cliente.</summary>
public sealed record ChainSalesRow(int? ChainId, string ChainName, bool IsActive, int ClientCount, int ClientsWithSales, decimal CompareSales, decimal Sales, decimal Change,
    decimal? ChangePct, decimal? ShareOfNet, IReadOnlyList<ChainSalesClient> Clients);

/// <summary>Lote 245 — totales de las filas que se devuelven (con una sola cadena, los de esa cadena).</summary>
public sealed record ChainSalesTotals(decimal CompareSales, decimal Sales, decimal Change, decimal? ChangePct);

/// <summary>Lote 245 — conciliación del mes COMPLETO: cadenas + sin cadena = venta neta del mes (Difference = lo que no cuadra; debería ser 0).</summary>
public sealed record ChainSalesReconciliation(decimal ChainsTotal, decimal NoChain, decimal MonthNet, decimal Difference);

/// <summary>Lote 245 — resultado de <see cref="ClientChainRules.SalesByChain"/>. NoChain = null cuando se pidió una sola cadena.</summary>
public sealed record ChainSalesResult(IReadOnlyList<ChainSalesRow> Rows, ChainSalesRow? NoChain, ChainSalesTotals Totals, ChainSalesReconciliation Reconciliation);

/// <summary>Lote 245 — venta neta de un mes (primer día del mes).</summary>
public sealed record ChainMonthSales(DateOnly Month, decimal Sales);

/// <summary>Lote 245 — uno de los clientes principales de la cadena en el rango, con su participación en la venta del rango.</summary>
public sealed record ChainTopClient(int ClientId, decimal Sales, decimal? Share);

/// <summary>Lote 245 — resultado de <see cref="ClientChainRules.Series"/>.</summary>
public sealed record ChainSeriesResult(IReadOnlyList<ChainMonthSales> Months, IReadOnlyList<ChainTopClient> TopClients);

/// <summary>
/// Lote 245 (Lote B del plan de rutas, cadenas y precios) — reglas PURAS de las cadenas de clientes (sin BD; las prueba xunit):
/// - Una cadena agrupa clientes (Walgreens, CVS…); un cliente pertenece a una sola cadena o a ninguna. No es una ruta ni cambia la ruta del cliente.
/// - Nombre obligatorio de hasta 100 caracteres (único entre las activas de la compañía, lo valida el servicio); notas de hasta 500.
/// - Alta masiva por el inicio del nombre: el prefijo y los nombres se comparan con <see cref="NameKey"/> (sin acentos, mayúsculas, puntos y comas como espacio); con
///   «palabra completa» el carácter que sigue al prefijo no puede ser letra ni dígito. La simulación da una huella (<see cref="Fingerprint"/>) que la aplicación debe
///   repetir: si los clientes que coinciden o su cadena cambiaron, se vuelve a simular.
/// - Análisis por cadena: venta neta (producto + descuentos; envío y otros no cuentan) del mes y del mes de comparación, por la cadena ACTUAL del cliente; las líneas
///   sin cliente emparejado caen en «Sin cadena», agrupadas por nombre. Redondeo a 2 decimales (AwayFromZero) solo al final.
/// </summary>
public static class ClientChainRules
{
    public const int NameMaxLength = 100;
    public const int NotesMaxLength = 500;
    public const int PrefixMinLength = 3;
    public const int MaxBulk = 2000;
    public const int MaxPageSize = 500;
    /// <summary>Cuántos clientes principales trae la serie de una cadena.</summary>
    public const int TopClientsCount = 5;

    // ---------------------------------------------------------------- acciones de la asignación y del alta masiva (mismos valores que ClientChainCodes)
    public const string ActionAssign = "ASSIGN";
    public const string ActionMove = "MOVE";
    public const string ActionAlready = "ALREADY";
    public const string ActionSkipOtherChain = "SKIP_OTHER_CHAIN";
    public const string ActionExcluded = "EXCLUDED";

    /// <summary>Etiqueta del grupo de los clientes sin cadena (en las respuestas del API).</summary>
    public const string NoChainName = "Sin cadena";

    // ---------------------------------------------------------------- mensajes exactos (el manual y la FAQ los citan)
    public const string NameRequired = "Escriba el nombre de la cadena.";
    public static readonly string NameTooLong = $"El nombre no puede pasar de {NameMaxLength} caracteres.";
    public static readonly string NotesTooLong = $"Las notas no pueden pasar de {NotesMaxLength} caracteres.";
    public const string NameTaken = "Ya hay una cadena activa con ese nombre.";
    public const string NotFound = "Cadena no encontrada.";
    public const string ChainInactive = "La cadena está desactivada: actívela o escoja otra.";
    public const string BodyRequired = "El cuerpo de la solicitud es obligatorio.";
    public const string ClientsRequired = "Indique los clientes (clientPublicIds).";
    public static readonly string TooManyInCall = $"No se pueden mandar más de {MaxBulk} clientes en una sola llamada.";
    public const string PrefixTooShort = "Escriba al menos 3 letras del inicio del nombre.";
    public static readonly string PrefixTooLong = $"El prefijo no puede pasar de {NameMaxLength} caracteres.";
    public const string FingerprintRequired = "Falta la huella de la simulación (fingerprint): simule antes de aplicar.";
    public const string FingerprintStale = "Los clientes que coinciden cambiaron desde la simulación: vuelva a simular antes de aplicar.";
    public const string ExclusionOutside = "Un cliente excluido no está entre los que encontró la simulación.";
    public const string ChainOrNoChain = "Escoja una cadena o «Sin cadena», no las dos.";
    public const string ChainRequired = "Indique la cadena (chainPublicId).";

    public static string BulkTooMany(int matched)
        => $"La simulación encontró {matched} clientes y el máximo por aplicación es {MaxBulk}: use un prefijo más largo.";

    public static string HasActiveClients(int count)
        => count == 1
            ? "La cadena tiene 1 cliente activo: quítelo de la cadena antes de desactivarla."
            : $"La cadena tiene {count} clientes activos: quítelos de la cadena antes de desactivarla.";

    /// <summary>
    /// Lote 247 (DECISIÓN 13, contrato del Lote 245): 409 al desactivar una cadena que tiene listas de precios (incluidos sus precios especiales) u ofertas INCLUDE
    /// publicadas y vigentes.
    /// </summary>
    public static string HasPricesOrOffers(int lists, int offers)
        => $"La cadena tiene precios especiales u ofertas publicados y vigentes ({lists} listas de precios y {offers} ofertas): quítela de ellos antes de desactivarla.";

    // ---------------------------------------------------------------- nombre, notas y prefijo

    /// <summary>Nombre recortado; vacío → <see cref="NameRequired"/>; más de 100 caracteres → <see cref="NameTooLong"/>.</summary>
    public static (string? Name, string? Error) NormalizeName(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, NameRequired);
        if (name.Length > NameMaxLength) return (null, NameTooLong);
        return (name, null);
    }

    /// <summary>Clave del nombre para comparar (la misma del análisis de ventas): sin acentos (Ñ → N), mayúsculas, puntos y comas como espacio, espacios juntos.</summary>
    public static string NameKey(string? name) => SalesAnalysisRules.NameKey(name);

    /// <summary>Notas recortadas; vacías → null; más de 500 caracteres → <see cref="NotesTooLong"/>.</summary>
    public static (string? Notes, string? Error) NormalizeNotes(string? raw)
    {
        var notes = raw?.Trim();
        if (string.IsNullOrEmpty(notes)) return (null, null);
        if (notes.Length > NotesMaxLength) return (null, NotesTooLong);
        return (notes, null);
    }

    /// <summary>
    /// Clave del prefijo del alta masiva (<see cref="NameKey"/>): menos de 3 letras o dígitos → <see cref="PrefixTooShort"/>; más de 100 caracteres →
    /// <see cref="PrefixTooLong"/>.
    /// </summary>
    public static (string? Key, string? Error) PrefixKey(string? raw)
    {
        var key = NameKey(raw);
        if (key.Count(char.IsLetterOrDigit) < PrefixMinLength) return (null, PrefixTooShort);
        if (key.Length > NameMaxLength) return (null, PrefixTooLong);
        return (key, null);
    }

    /// <summary>
    /// ¿El nombre (ya en <see cref="NameKey"/>) empieza con el prefijo (ya en clave)? Con <paramref name="wholeWord"/>, el carácter que sigue al prefijo no puede ser
    /// letra ni dígito, salvo que el prefijo termine en un carácter que no lo es (por ejemplo «-»). Un espacio final del prefijo se pierde en la clave, así que con
    /// palabra completa da lo mismo escribirlo o no.
    /// </summary>
    public static bool MatchesPrefix(string nameKey, string prefixKey, bool wholeWord)
    {
        if (string.IsNullOrEmpty(prefixKey) || !nameKey.StartsWith(prefixKey, StringComparison.Ordinal)) return false;
        if (!wholeWord || nameKey.Length == prefixKey.Length) return true;
        if (!char.IsLetterOrDigit(prefixKey[^1])) return true;
        return !char.IsLetterOrDigit(nameKey[prefixKey.Length]);
    }

    // ---------------------------------------------------------------- asignación

    /// <summary>
    /// Qué le pasa a un cliente al asignarlo a la cadena destino: ya está en ella → ALREADY; sin cadena → ASSIGN; en otra cadena → MOVE si se pidió mover, si no
    /// SKIP_OTHER_CHAIN (se informa y no se toca).
    /// </summary>
    public static string Classify(int? currentChainId, int targetChainId, bool moveFromOtherChains)
    {
        if (currentChainId is not int current) return ActionAssign;
        if (current == targetChainId) return ActionAlready;
        return moveFromOtherChains ? ActionMove : ActionSkipOtherChain;
    }

    /// <summary>
    /// Huella de una simulación del alta masiva: SHA-256 (hex en minúsculas) de la cadena destino, el prefijo, palabra completa, mover y los clientes que coinciden con
    /// su cadena actual, ordenados por ClientId (no depende del orden de entrada). NO incluye las exclusiones: excluir no obliga a volver a simular.
    /// </summary>
    public static string Fingerprint(int targetChainId, string prefixKey, bool wholeWord, bool moveFromOtherChains, IEnumerable<(int ClientId, int? ChainId)> matched)
    {
        var sb = new StringBuilder();
        sb.Append("chain=").Append(targetChainId).Append('|')
          .Append("prefix=").Append(prefixKey).Append('|')
          .Append("whole=").Append(wholeWord ? '1' : '0').Append('|')
          .Append("move=").Append(moveFromOtherChains ? '1' : '0');
        foreach (var (clientId, chainId) in matched.OrderBy(m => m.ClientId).ThenBy(m => m.ChainId ?? 0))
            sb.Append('|').Append(clientId).Append(':').Append(chainId?.ToString() ?? "-");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>Toda exclusión debe estar entre los clientes que encontró la simulación; si no → <see cref="ExclusionOutside"/>. NULL = vale.</summary>
    public static string? ValidateExclusions(IEnumerable<int> matchedIds, IEnumerable<int>? excludedIds)
    {
        if (excludedIds is null) return null;
        var matched = matchedIds as IReadOnlySet<int> ?? matchedIds.ToHashSet();
        return excludedIds.All(matched.Contains) ? null : ExclusionOutside;
    }

    // ---------------------------------------------------------------- análisis por cadena

    private static decimal M2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private sealed class Acc
    {
        public decimal Sales;
        public decimal Compare;
        public bool HasMovement => Sales != 0m || Compare != 0m;
    }

    /// <summary>
    /// Venta neta por cadena del mes contra el mes de comparación, por la cadena ACTUAL de cada cliente (<paramref name="clientChain"/>: ClientId → ChainId, solo los
    /// que tienen cadena).
    /// - Filas: todas las cadenas activas (aunque no vendan) y las inactivas que tuvieron venta en alguno de los dos meses; orden por venta descendente y luego nombre.
    /// - Cada fila trae su desglose por cliente: los clientes activos de la cadena (aunque no vendan) y cualquier cliente de la cadena con venta.
    /// - «Sin cadena»: los clientes sin cadena con venta y las líneas sin cliente emparejado, agrupadas por <see cref="NameKey"/> del nombre. ClientCount = clientes
    ///   activos sin cadena.
    /// - ClientCount = clientes activos; ClientsWithSales = clientes del desglose con venta en el mes.
    /// - ShareOfNet = venta de la fila ÷ venta neta del mes; ShareOfChain = venta del cliente ÷ venta de su fila.
    /// - Conciliación del mes completo: ChainsTotal (todas las cadenas) + NoChain = MonthNet; Difference debería ser 0.
    /// - Con <paramref name="onlyChainId"/>: Rows trae solo esa cadena, NoChain = null y los totales son los de esa cadena; la conciliación sigue siendo del mes completo.
    /// Montos sumados sin redondear y redondeados a 2 decimales (AwayFromZero) al final.
    /// </summary>
    public static ChainSalesResult SalesByChain(IEnumerable<ChainSaleLine> lines, IReadOnlyDictionary<int, int> clientChain,
        IReadOnlyDictionary<int, ChainClientInfo> clients, IEnumerable<ChainInfo> chains, DateOnly month, DateOnly compareMonth, int? onlyChainId = null)
    {
        var m = SalesAnalysisRules.MonthStart(month);
        var cm = SalesAnalysisRules.MonthStart(compareMonth);
        var chainById = chains.GroupBy(c => c.ChainId).ToDictionary(g => g.Key, g => g.First());

        // la cadena de un cliente, solo si la cadena existe (una cadena desconocida cuenta como «Sin cadena»)
        int? ChainOf(int clientId) => clientChain.TryGetValue(clientId, out var ch) && chainById.ContainsKey(ch) ? ch : null;

        // acumulados por cliente: «C{id}» = cliente de Teikem, «N{clave}» = nombre del historial sin emparejar
        var byClient = new Dictionary<string, Acc>(StringComparer.Ordinal);
        var rawNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var monthNet = 0m;
        foreach (var l in lines)
        {
            if (!SalesAnalysisRules.CountsForRep(l.LineClass)) continue;
            var lm = SalesAnalysisRules.MonthStart(l.Month);
            var inM = lm == m;
            var inC = lm == cm;
            if (!inM && !inC) continue;
            var key = l.ClientId is int cid ? "C" + cid : "N" + NameKey(l.ClientName);
            if (!byClient.TryGetValue(key, out var acc)) byClient[key] = acc = new Acc();
            if (inM) { acc.Sales += l.Amount; monthNet += l.Amount; }
            if (inC) acc.Compare += l.Amount;
            rawNames.TryAdd(key, string.IsNullOrWhiteSpace(l.ClientName) ? "-" : l.ClientName.Trim());
        }

        string NameOf(string key)
            => key[0] == 'C' && clients.TryGetValue(int.Parse(key.AsSpan(1)), out var info) ? info.Name : rawNames.GetValueOrDefault(key) ?? string.Empty;

        // a qué fila va cada cliente (null = «Sin cadena»)
        var members = new Dictionary<int, List<string>>();
        var noChainKeys = new List<string>();
        foreach (var key in byClient.Keys)
        {
            if (key[0] == 'C' && ChainOf(int.Parse(key.AsSpan(1))) is int ch)
                (members.TryGetValue(ch, out var list) ? list : members[ch] = new List<string>()).Add(key);
            else noChainKeys.Add(key);
        }
        foreach (var (clientId, info) in clients)
        {
            if (!info.IsActive || ChainOf(clientId) is not int ch) continue;
            var key = "C" + clientId;
            var list = members.TryGetValue(ch, out var l) ? l : members[ch] = new List<string>();
            if (!list.Contains(key)) list.Add(key);
        }

        ChainSalesRow BuildRow(int? chainId, string name, bool isActive, int clientCount, IEnumerable<string> keys)
        {
            var accs = keys.Select(k => (Key: k, Acc: byClient.GetValueOrDefault(k) ?? new Acc())).ToList();
            var rawSales = accs.Sum(a => a.Acc.Sales);
            var rawCompare = accs.Sum(a => a.Acc.Compare);
            var detail = accs
                .Select(a =>
                {
                    var s = M2(a.Acc.Sales);
                    var c = M2(a.Acc.Compare);
                    int? id = a.Key[0] == 'C' ? int.Parse(a.Key.AsSpan(1)) : null;
                    return new ChainSalesClient(id, NameOf(a.Key), c, s, M2(s - c), SalesAnalysisRules.ChangePct(c, s), SalesAnalysisRules.Share(a.Acc.Sales, rawSales));
                })
                .OrderByDescending(c => c.Sales).ThenBy(c => c.ClientName, StringComparer.InvariantCultureIgnoreCase).ThenBy(c => c.ClientId ?? 0).ToList();
            var sales = M2(rawSales);
            var compare = M2(rawCompare);
            return new ChainSalesRow(chainId, name, isActive, clientCount, accs.Count(a => M2(a.Acc.Sales) != 0m), compare, sales, M2(sales - compare),
                SalesAnalysisRules.ChangePct(compare, sales), SalesAnalysisRules.Share(rawSales, monthNet), detail);
        }

        var rows = new List<ChainSalesRow>();
        foreach (var chain in chainById.Values)
        {
            var keys = members.GetValueOrDefault(chain.ChainId) ?? new List<string>();
            var hasMovement = keys.Any(k => byClient.TryGetValue(k, out var a) && a.HasMovement);
            if (!chain.IsActive && !hasMovement) continue;
            var clientCount = clients.Values.Count(c => c.IsActive && ChainOf(c.ClientId) == chain.ChainId);
            rows.Add(BuildRow(chain.ChainId, chain.Name, chain.IsActive, clientCount, keys));
        }
        rows = rows.OrderByDescending(r => r.Sales).ThenBy(r => r.ChainName, StringComparer.InvariantCultureIgnoreCase).ThenBy(r => r.ChainId).ToList();

        var noChainCount = clients.Values.Count(c => c.IsActive && ChainOf(c.ClientId) is null);
        var noChain = BuildRow(null, NoChainName, true, noChainCount, noChainKeys);

        var chainsTotal = rows.Sum(r => r.Sales);
        var net = M2(monthNet);
        var reconciliation = new ChainSalesReconciliation(chainsTotal, noChain.Sales, net, M2(net - (chainsTotal + noChain.Sales)));

        if (onlyChainId is int only)
        {
            var row = rows.Where(r => r.ChainId == only).ToList();
            var tc = row.Sum(r => r.CompareSales);
            var ts = row.Sum(r => r.Sales);
            return new ChainSalesResult(row, null, new ChainSalesTotals(tc, ts, M2(ts - tc), SalesAnalysisRules.ChangePct(tc, ts)), reconciliation);
        }

        var totalCompare = rows.Sum(r => r.CompareSales) + noChain.CompareSales;
        var totalSales = chainsTotal + noChain.Sales;
        return new ChainSalesResult(rows, noChain, new ChainSalesTotals(totalCompare, totalSales, M2(totalSales - totalCompare),
            SalesAnalysisRules.ChangePct(totalCompare, totalSales)), reconciliation);
    }

    /// <summary>
    /// Serie mensual de una cadena en [desde, hasta] (meses): la venta neta de cada mes de sus clientes (<paramref name="clientIds"/>; los meses sin venta van en 0) y
    /// los <see cref="TopClientsCount"/> clientes con más venta en el rango (solo los de venta positiva), con su participación en la venta del rango. Montos
    /// redondeados al final.
    /// </summary>
    public static ChainSeriesResult Series(IEnumerable<ChainSaleLine> lines, IEnumerable<int> clientIds, DateOnly fromMonth, DateOnly toMonth)
    {
        var ids = clientIds as IReadOnlySet<int> ?? clientIds.ToHashSet();
        var months = SalesAnalysisRules.Months(fromMonth, toMonth);
        var first = SalesAnalysisRules.MonthStart(fromMonth);
        var last = SalesAnalysisRules.MonthStart(toMonth);
        var byMonth = months.ToDictionary(x => x, _ => 0m);
        var byClient = new Dictionary<int, decimal>();
        foreach (var l in lines)
        {
            if (l.ClientId is not int cid || !ids.Contains(cid) || !SalesAnalysisRules.CountsForRep(l.LineClass)) continue;
            var lm = SalesAnalysisRules.MonthStart(l.Month);
            if (lm < first || lm > last) continue;
            byMonth[lm] += l.Amount;
            byClient[cid] = byClient.GetValueOrDefault(cid) + l.Amount;
        }
        var total = byMonth.Values.Sum();
        var top = byClient.Where(kv => kv.Value > 0m).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(TopClientsCount)
            .Select(kv => new ChainTopClient(kv.Key, M2(kv.Value), SalesAnalysisRules.Share(kv.Value, total))).ToList();
        return new ChainSeriesResult(months.Select(x => new ChainMonthSales(x, M2(byMonth[x]))).ToList(), top);
    }
}
