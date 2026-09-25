using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Clients;

/// <summary>Clases de componente que administra este lote (RateComponentType BASE_FREIGHT y EXTRA_PIECE).</summary>
public static class RateKinds
{
    /// <summary>Tarifa por servicio: BASE_FREIGHT / FIXED / PER_SHIPMENT, monto fijo por envío.</summary>
    public const string PerService = "PER_SERVICE";
    /// <summary>Pieza extra: EXTRA_PIECE / TIERED / GRADUATED / PER_PIECE, tramos por número de pieza.</summary>
    public const string ExtraPiece = "EXTRA_PIECE";
}

/// <summary>De dónde salió cada monto de la cotización (R35).</summary>
public static class RateSources
{
    public const string Contract = "CONTRACT";
    public const string Generic = "GENERIC";
    public const string None = "NONE";
}

/// <summary>Redondeo monetario de la plataforma: 4 decimales, AwayFromZero (el front muestra 2).</summary>
public static class Money
{
    public static decimal Round4(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

/// <summary>Tramo vigente de un componente EXTRA_PIECE (ToUnit null = abierto).</summary>
public sealed record TierRow(int Id, int FromUnit, int? ToUnit, decimal Rate);

/// <summary>
/// Fila de tarifa vigente ya resuelta (sin ids de catálogo): ContractId null = tarifa genérica del tenant,
/// donde ServiceType/PackageType null = comodín. Kind = RateKinds. Rate = FlatAmount en PER_SERVICE (ignorado en EXTRA_PIECE).
/// </summary>
public sealed record RateRow(int Id, int? ContractId, string Kind, string? ServiceType, string? PackageType, decimal Rate, IReadOnlyList<TierRow> Tiers)
{
    public bool IsGeneric => ContractId is null;
}

/// <summary>Modelo de facturación del contrato vigente. Sin contrato vigente: ContractFlags.None (todo apagado).</summary>
public sealed record ContractFlags(bool BillPerService, bool BillExtraPiece, bool BillDispatchFee, bool BillCodFee,
    decimal? DispatchFee, string? CodFeeType, decimal? CodFeeValue)
{
    public static readonly ContractFlags None = new(false, false, false, false, null, null, null);
}

/// <summary>Línea a cotizar: una por (servicio, tipo de paquete).</summary>
public sealed record QuoteLine(string ServiceType, string PackageType, int Pieces);

/// <summary>Resultado de un monto resuelto: valor, fuente (RateSources) e id de la fila usada.</summary>
public sealed record RateMatch(decimal? Amount, string Source, int? RowId);

/// <summary>Resultado de las piezas extra: monto, fuente y las filas (contrato y/o genérica) que aportaron tramos.</summary>
public sealed record ExtraPiecesResult(decimal Amount, string Source, IReadOnlyList<int> RowIds);

public sealed record QuoteLineResult(string ServiceType, string PackageType, int Pieces,
    decimal? BaseRate, string BaseSource, decimal ExtraPieces, string ExtraSource, decimal LineTotal);

public sealed record QuoteResult(IReadOnlyList<QuoteLineResult> Lines, decimal DispatchFee, decimal CodFee, decimal Total, IReadOnlyList<int> RateComponentIds);

/// <summary>
/// Motor puro de cotización por contrato (Lote 2, P4). No conoce EF: recibe las filas vigentes en la fecha consultada
/// (del contrato y genéricas del tenant) y las banderas del contrato, y devuelve los montos.
/// Reglas (bitácora R30–R35):
/// - Especificidad de una fila: exacto servicio+paquete &gt; servicio+comodín &gt; comodín+paquete &gt; comodín total;
///   las filas del contrato van antes que las genéricas.
/// - Componente apagado en el contrato o sin fila ⇒ se usa la genérica del tenant; sin genérica ⇒ null/NONE (base) o 0/NONE (extra).
/// - Pieza extra GRADUATED marginal: la pieza 1 nunca es extra; cada pieza 2..n paga la tarifa del tramo donde cae;
///   una pieza sin tramo en el contrato cae a los tramos genéricos; sin genérica paga 0.
/// - Despacho: una sola vez por orden. COD: fijo o por ciento del monto COD cobrado (nunca del total de la orden).
/// - Despacho y COD se suman a la primera línea; cada monto se redondea a 4 decimales AwayFromZero.
/// </summary>
public static class ContractRateResolver
{
    /// <summary>
    /// Mejor fila para (kind, servicio, paquete): filas del contrato antes que genéricas y, dentro de cada grupo, la más específica.
    /// Dos filas con la misma especificidad dentro del mismo grupo violan la invariante del índice UQ_RateComponent_Open.
    /// </summary>
    public static RateRow? Match(IEnumerable<RateRow> rows, string kind, string serviceType, string packageType)
    {
        var contract = Best(rows.Where(r => !r.IsGeneric), kind, serviceType, packageType);
        return contract ?? Best(rows.Where(r => r.IsGeneric), kind, serviceType, packageType);
    }

    private static RateRow? Best(IEnumerable<RateRow> rows, string kind, string serviceType, string packageType)
    {
        RateRow? best = null;
        var bestScore = -1;
        var tie = false;
        foreach (var r in rows)
        {
            if (!Same(r.Kind, kind)) continue;
            var score = Specificity(r, serviceType, packageType);
            if (score < 0) continue;
            if (score > bestScore) { best = r; bestScore = score; tie = false; }
            else if (score == bestScore) tie = true;
        }
        if (tie)
            throw new InvalidOperationException(
                $"Hay más de una tarifa vigente de tipo {kind} para {serviceType}/{packageType} con la misma especificidad; revise UQ_RateComponent_Open.");
        return best;
    }

    /// <summary>3 = exacto, 2 = servicio + comodín, 1 = comodín + paquete, 0 = comodín total, -1 = no aplica.</summary>
    private static int Specificity(RateRow r, string serviceType, string packageType)
    {
        var svc = r.ServiceType is null ? (bool?)null : Same(r.ServiceType, serviceType);
        var pkg = r.PackageType is null ? (bool?)null : Same(r.PackageType, packageType);
        if (svc == false || pkg == false) return -1;
        return (svc, pkg) switch
        {
            (true, true) => 3,
            (true, null) => 2,
            (null, true) => 1,
            _ => 0,
        };
    }

    /// <summary>Tarifa base por servicio (R35): contrato encendido y con fila ⇒ CONTRACT; si no, genérica ⇒ GENERIC; si no, null/NONE.</summary>
    public static RateMatch BaseRate(IEnumerable<RateRow> rows, ContractFlags flags, string serviceType, string packageType)
    {
        var list = rows as IReadOnlyCollection<RateRow> ?? rows.ToList();
        var candidates = flags.BillPerService ? list : list.Where(r => r.IsGeneric);
        var row = Match(candidates, RateKinds.PerService, serviceType, packageType);
        if (row is null) return new RateMatch(null, RateSources.None, null);
        return new RateMatch(Money.Round4(row.Rate), row.IsGeneric ? RateSources.Generic : RateSources.Contract, row.Id);
    }

    /// <summary>
    /// Piezas extra GRADUATED marginal. La pieza 1 nunca es extra. Para cada pieza 2..n se busca el tramo del contrato
    /// (si el componente está encendido); si no hay, el tramo genérico; si tampoco, la pieza paga 0.
    /// Source: CONTRACT si alguna pieza usó tramo del contrato; GENERIC si solo genéricos; NONE si ninguna pieza encontró tramo.
    /// </summary>
    public static ExtraPiecesResult ExtraPieces(IEnumerable<RateRow> rows, ContractFlags flags, string serviceType, string packageType, int pieces)
    {
        if (pieces < 2) return new ExtraPiecesResult(0m, RateSources.None, Array.Empty<int>());
        var list = rows as IReadOnlyCollection<RateRow> ?? rows.ToList();

        var contractRow = flags.BillExtraPiece
            ? Best(list.Where(r => !r.IsGeneric), RateKinds.ExtraPiece, serviceType, packageType)
            : null;
        var genericRow = Best(list.Where(r => r.IsGeneric), RateKinds.ExtraPiece, serviceType, packageType);

        decimal total = 0m;
        var usedContract = false;
        var usedGeneric = false;
        for (var unit = 2; unit <= pieces; unit++)
        {
            var tier = FindTier(contractRow, unit);
            if (tier is not null) { total += tier.Rate; usedContract = true; continue; }
            tier = FindTier(genericRow, unit);
            if (tier is not null) { total += tier.Rate; usedGeneric = true; }
        }

        var ids = new List<int>(2);
        if (usedContract && contractRow is not null) ids.Add(contractRow.Id);
        if (usedGeneric && genericRow is not null) ids.Add(genericRow.Id);
        var source = usedContract ? RateSources.Contract : usedGeneric ? RateSources.Generic : RateSources.None;
        return new ExtraPiecesResult(Money.Round4(total), source, ids);
    }

    private static TierRow? FindTier(RateRow? row, int unit)
    {
        if (row is null) return null;
        foreach (var t in row.Tiers)
            if (unit >= t.FromUnit && (t.ToUnit is null || unit <= t.ToUnit.Value)) return t;
        return null;
    }

    /// <summary>Cargo por despacho: apagado ⇒ 0; encendido ⇒ el monto del contrato, una sola vez por orden.</summary>
    public static decimal DispatchFee(ContractFlags flags)
        => flags.BillDispatchFee ? Money.Round4(flags.DispatchFee ?? 0m) : 0m;

    /// <summary>Cargo por COD: apagado ⇒ 0; FIXED ⇒ valor; PERCENT ⇒ monto COD × valor / 100 (siempre sobre el COD, nunca sobre el total).</summary>
    public static decimal CodFee(ContractFlags flags, decimal codAmount)
    {
        if (!flags.BillCodFee || flags.CodFeeValue is null || string.IsNullOrWhiteSpace(flags.CodFeeType)) return 0m;
        var value = flags.CodFeeValue.Value;
        return flags.CodFeeType.Trim().ToUpperInvariant() switch
        {
            PricingTypes.Fixed => Money.Round4(value),
            PricingTypes.Percent => Money.Round4(codAmount * value / 100m),
            _ => 0m,
        };
    }

    /// <summary>
    /// Índice (base 0) de la primera línea cuyo par (servicio, tipo de paquete) ya apareció antes, o null si no hay repetidos.
    /// El llamador la rechaza con 400: dos líneas del mismo par cobrarían dos tarifas base en vez de base + pieza extra
    /// (documento L1096: una línea por grupo de tipo de paquete con el total de piezas).
    /// </summary>
    public static int? FindDuplicateLine(IEnumerable<QuoteLine> lines)
    {
        var seen = new HashSet<(string, string)>();
        var i = 0;
        foreach (var l in lines)
        {
            var key = (l.ServiceType.Trim().ToUpperInvariant(), l.PackageType.Trim().ToUpperInvariant());
            if (!seen.Add(key)) return i;
            i++;
        }
        return null;
    }

    /// <summary>
    /// Cotiza una orden: una línea por (servicio, paquete) con su base y sus piezas extra; despacho y COD una sola vez,
    /// sumados a la primera línea (R31–R33). RateComponentIds = filas realmente usadas (R30), sin duplicados.
    /// Las líneas repetidas no se consolidan aquí: RateService las rechaza con FindDuplicateLine.
    /// </summary>
    public static QuoteResult QuoteOrder(IEnumerable<RateRow> rows, ContractFlags flags, IEnumerable<QuoteLine> lines, decimal codAmount)
    {
        var list = rows as IReadOnlyCollection<RateRow> ?? rows.ToList();
        var dispatch = DispatchFee(flags);
        var cod = CodFee(flags, codAmount);
        var ids = new List<int>();
        var result = new List<QuoteLineResult>();
        var first = true;
        decimal total = 0m;

        foreach (var line in lines)
        {
            var baseRate = BaseRate(list, flags, line.ServiceType, line.PackageType);
            var extra = ExtraPieces(list, flags, line.ServiceType, line.PackageType, line.Pieces);
            if (baseRate.RowId is int baseId && !ids.Contains(baseId)) ids.Add(baseId);
            foreach (var id in extra.RowIds) if (!ids.Contains(id)) ids.Add(id);

            var lineTotal = (baseRate.Amount ?? 0m) + extra.Amount;
            if (first) { lineTotal += dispatch + cod; first = false; }
            lineTotal = Money.Round4(lineTotal);
            total += lineTotal;
            result.Add(new QuoteLineResult(line.ServiceType, line.PackageType, line.Pieces,
                baseRate.Amount, baseRate.Source, extra.Amount, extra.Source, lineTotal));
        }

        // Sin líneas no hay orden: despacho y COD no se cobran solos.
        if (result.Count == 0) { dispatch = 0m; cod = 0m; }
        return new QuoteResult(result, dispatch, cod, Money.Round4(total), ids);
    }

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
